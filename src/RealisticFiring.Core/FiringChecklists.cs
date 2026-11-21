using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
    /// <summary>The firing procedures that have live step-by-step checklists.</summary>
    public enum ChecklistId
    {
        CoalColdStart,
        OilColdStart,
        InjectorsAndWater,
        CleaningClinkerAndAsh,
        StokerSetup,
        DriftingAndLayover,
    }

    /// <summary>One step in a live firing checklist.</summary>
    public readonly struct ChecklistStep
    {
        public ChecklistStep(string title, string how, bool done, bool current)
        {
            Title = title ?? "";
            How = how ?? "";
            Done = done;
            Current = current;
        }

        public string Title { get; }
        public string How { get; }
        public bool Done { get; }
        public bool Current { get; }
    }

    /// <summary>An evaluated live checklist with its steps and completion state.</summary>
    public sealed class Checklist
    {
        public Checklist(ChecklistId id, string name, string shortName, IReadOnlyList<ChecklistStep> steps, bool autoSelected)
        {
            Id = id;
            Name = name ?? "";
            ShortName = shortName ?? "";
            Steps = steps ?? Array.Empty<ChecklistStep>();
            AutoSelected = autoSelected;
            var done = 0;
            var current = -1;
            for (var i = 0; i < Steps.Count; i++)
            {
                if (Steps[i].Done)
                    done++;
                if (current < 0 && Steps[i].Current)
                    current = i;
            }
            CompletedCount = done;
            CurrentIndex = current;
            AllDone = Steps.Count > 0 && done == Steps.Count;
        }

        public ChecklistId Id { get; }
        public string Name { get; }
        public string ShortName { get; }
        public IReadOnlyList<ChecklistStep> Steps { get; }
        public bool AutoSelected { get; }
        public int CompletedCount { get; }
        public int CurrentIndex { get; }
        public bool AllDone { get; }
    }

    /// <summary>
    /// Engine-free snapshot of locomotive state for live checklist evaluation.
    /// Zero Unity or game references so it remains unit-testable in Core.
    /// </summary>
    public struct ChecklistSnapshot
    {
        public bool IsOil;
        public bool FireOn;
        public float GlassLevel;
        /// <summary>Boiler gauge pressure, bar (0 = atmospheric).</summary>
        public float BoilerGaugeBar;
        public float Damper;
        public float Blower;
        public bool HasDoor;
        public float DoorOpening;

        // Coal cold start
        public float WoodMass;
        public bool RagLit;
        public bool WoodCaught;
        public float BurningCoalMass;
        public float TotalCoalMass;
        public bool LighterLightsCoal;

        /// <summary>The coal has caught since the fire was last out (<see cref="ColdStartLatch"/>).</summary>
        public bool CoalCaught;

        /// <summary>A coal cold start under way, held until its list is done or the coal has burned a while (<see cref="ColdStartLatch"/>).</summary>
        public bool ColdStartHeld;

        // Oil burner (reused with FiringAdvice.Steps)
        public OilReading Oil;

        // Injectors & water
        public string InjectorName;
        public bool HasSeparateSteamValve;
        public bool HasTenderWaterValve;
        public bool TenderWaterValveOpen;
        public float InjectorWaterValve;
        public float InjectorCrackOpening;
        public bool InjectorSteamPrime;
        public bool InjectorSteamFeed;
        public InjectorState InjectorState;
        public bool InjectorWorking;
        public bool InjectorPriming;
        public bool InjectorHasTrouble;
        public float OverflowWaterKgPerS;

        // Cleaning clinker & ash
        public float ForwardSpeedMs;
        public float Throttle;
        public float ClinkerMass;
        public bool NeedsCleaning;
        /// <summary>Ash over <see cref="AutoFireman.ShakeAshShare"/> of the bed: a shake of the grate due (<see cref="FiringAdvice.WantsShake"/>).</summary>
        public bool WantsShake;
        public float FrontCoalKg;
        public float BackCoalKg;
        public float FrontClinkerKg;
        public int ThinZone;
        public float AshpanFill;
        public bool AshpanFull;
        public bool SlicedRecently;
        public bool DropGrateOpenedRecently;
        public bool OnFreeAshTrack;

        // Stoker setup
        public bool HasStoker;
        public bool StokerJammed;
        public bool StokerForward;
        public float StokerMasterBar;
        public float StokerFrontBar;
        public float StokerBackBar;
        public float StokerFinesBar;
        public float StokerSpeed;
        public bool BackCornersCovered;

        // Drifting & layover
        public float Cutoff;
        public bool DriftingHookedUp;
        public bool HasDriftingValve;
        public bool DriftingValveOpen;
        public bool HasByPassValves;
        public bool HasSniftingValves;
    }

    /// <summary>Live readings from one injector circuit for single-circuit checklist selection.</summary>
    public struct InjectorCircuitSnapshot
    {
        public string Name;
        public bool HasSeparateSteamValve;
        public bool HasTenderWaterValve;
        public bool TenderWaterValveOpen;
        public float WaterValve;
        public float SteamValve;
        public float CrackOpening;
        public InjectorState State;
        public float OverflowWaterKgPerS;
    }

    /// <summary>
    /// Live step-by-step checklists computed from engine state. The paper
    /// firing checklists booklet prints the same steps' titles, static.
    /// </summary>
    public static class FiringChecklists
    {
        /// <summary>Minimum gauge pressure (bar) for a live-steam injector to pick up.</summary>
        public const float InjectorMinGaugeBar = 2.5f;

        /// <summary>Minimum gauge pressure (bar) for a mechanical stoker engine to turn.</summary>
        public const float StokerMinGaugeBar = 2.0f;

        /// <summary>Wood pieces recommended to build a self-sustaining coal start.</summary>
        public const int RecommendedWoodPieces = 4;

        /// <summary>Burning coal mass (kg) at which the coal fire is established.</summary>
        public const float EstablishedCoalKg = 0.5f;

        /// <summary>Human-readable title for <paramref name="id"/>.</summary>
        public static string TitleOf(ChecklistId id)
        {
            switch (id)
            {
                case ChecklistId.CoalColdStart: return "Cold Start (Coal)";
                case ChecklistId.OilColdStart: return "Cold Start (Oil)";
                case ChecklistId.InjectorsAndWater: return "Injectors & Water";
                case ChecklistId.CleaningClinkerAndAsh: return "Cleaning Clinker & Ash";
                case ChecklistId.StokerSetup: return "Stoker Setup";
                case ChecklistId.DriftingAndLayover: return "Drifting & Layover";
                default: return id.ToString();
            }
        }

        /// <summary>Compact uppercase tag for <paramref name="id"/>.</summary>
        public static string ShortNameOf(ChecklistId id)
        {
            switch (id)
            {
                case ChecklistId.CoalColdStart: return "COLD START";
                case ChecklistId.OilColdStart: return "OIL START";
                case ChecklistId.InjectorsAndWater: return "INJECTORS";
                case ChecklistId.CleaningClinkerAndAsh: return "CLEAN FIRE";
                case ChecklistId.StokerSetup: return "STOKER";
                case ChecklistId.DriftingAndLayover: return "DRIFTING";
                default: return id.ToString().ToUpperInvariant();
            }
        }

        /// <summary>Checklists that apply to this locomotive.</summary>
        public static IReadOnlyList<ChecklistId> Applicable(ChecklistSnapshot state)
        {
            if (state.IsOil)
                return new[] { ChecklistId.OilColdStart, ChecklistId.InjectorsAndWater, ChecklistId.DriftingAndLayover };

            if (state.HasStoker)
            {
                return new[]
                {
                    ChecklistId.CoalColdStart,
                    ChecklistId.InjectorsAndWater,
                    ChecklistId.CleaningClinkerAndAsh,
                    ChecklistId.StokerSetup,
                    ChecklistId.DriftingAndLayover,
                };
            }

            return new[]
            {
                ChecklistId.CoalColdStart,
                ChecklistId.InjectorsAndWater,
                ChecklistId.CleaningClinkerAndAsh,
                ChecklistId.DriftingAndLayover,
            };
        }

        /// <summary>
        /// Picks the single most relevant injector circuit to track on the checklist
        /// without mixing valve positions, tender supply, or overflow across circuits.
        /// Returns -1 when <paramref name="circuits"/> is null or empty.
        /// </summary>
        public static int SelectInjectorCircuit(IReadOnlyList<InjectorCircuitSnapshot> circuits)
        {
            if (circuits == null || circuits.Count == 0)
                return -1;
            var bestIndex = 0;
            var bestPriority = CircuitPriority(circuits[0]);
            for (var i = 1; i < circuits.Count; i++)
            {
                var p = CircuitPriority(circuits[i]);
                if (p > bestPriority)
                {
                    bestPriority = p;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static int CircuitPriority(InjectorCircuitSnapshot c)
        {
            switch (c.State)
            {
                case InjectorState.Broken:
                case InjectorState.Hot:
                case InjectorState.NoWater:
                    return 5;
                case InjectorState.Priming:
                case InjectorState.LowPressure:
                    return 4;
                case InjectorState.Working:
                    return 3;
            }
            if (c.HasSeparateSteamValve && c.SteamValve >= InjectorSpec.SteamPrimeFrom)
                return 2;
            if (c.WaterValve > InjectorSpec.OpenThreshold)
                return 1;
            return 0;
        }

        /// <summary>
        /// Populates all injector fields of <paramref name="snapshot"/> strictly from
        /// the single selected circuit in <paramref name="circuits"/>.
        /// </summary>
        public static void ApplyInjectorCircuit(ref ChecklistSnapshot snapshot, IReadOnlyList<InjectorCircuitSnapshot> circuits)
        {
            var index = SelectInjectorCircuit(circuits);
            if (index < 0)
                return;
            var c = circuits[index];
            snapshot.InjectorName = c.Name ?? "";
            snapshot.HasSeparateSteamValve = c.HasSeparateSteamValve;
            snapshot.HasTenderWaterValve = c.HasTenderWaterValve;
            snapshot.TenderWaterValveOpen = !c.HasTenderWaterValve || c.TenderWaterValveOpen;
            snapshot.InjectorWaterValve = c.WaterValve;
            snapshot.InjectorCrackOpening = c.CrackOpening;
            snapshot.InjectorSteamPrime = c.HasSeparateSteamValve
                && c.SteamValve >= InjectorSpec.SteamPrimeFrom
                && c.SteamValve < InjectorSpec.SteamFeedFrom;
            snapshot.InjectorSteamFeed = c.HasSeparateSteamValve && c.SteamValve >= InjectorSpec.SteamFeedFrom;
            snapshot.InjectorState = c.State;
            snapshot.InjectorWorking = c.State == InjectorState.Working;
            snapshot.InjectorPriming = c.State == InjectorState.Priming;
            snapshot.InjectorHasTrouble = IsInjectorTrouble(c.State);
            snapshot.OverflowWaterKgPerS = c.OverflowWaterKgPerS;
        }

        private static bool IsInjectorTrouble(InjectorState state) =>
            state == InjectorState.Hot || state == InjectorState.Broken
            || state == InjectorState.NoWater || state == InjectorState.LowPressure;

        /// <summary>Picks the most relevant checklist for the engine's current state.</summary>
        public static ChecklistId AutoSelect(ChecklistSnapshot state)
        {
            var injectorTrouble = state.InjectorHasTrouble || IsInjectorTrouble(state.InjectorState);
            var injectorPriming = state.InjectorPriming || state.InjectorState == InjectorState.Priming;
            if (state.IsOil)
            {
                if (!state.FireOn)
                    return ChecklistId.OilColdStart;
                if (state.GlassLevel < FiringAdvice.LowGlass || injectorTrouble || injectorPriming)
                    return ChecklistId.InjectorsAndWater;
                if (state.DriftingHookedUp || (state.Throttle <= 0.02f && Math.Abs(state.ForwardSpeedMs) >= 1.5f))
                    return ChecklistId.DriftingAndLayover;
                return state.Throttle <= 0.02f ? ChecklistId.DriftingAndLayover : ChecklistId.InjectorsAndWater;
            }

            // Coal locomotive
            if (!state.FireOn || (state.BurningCoalMass < EstablishedCoalKg && !state.CoalCaught)
                || (state.ColdStartHeld && state.GlassLevel >= FiringAdvice.LowGlass))
                return ChecklistId.CoalColdStart;
            if (state.GlassLevel < FiringAdvice.LowGlass || injectorTrouble || injectorPriming)
                return ChecklistId.InjectorsAndWater;
            if (state.NeedsCleaning || state.AshpanFull || state.WantsShake)
                return ChecklistId.CleaningClinkerAndAsh;
            if (state.HasStoker && (state.StokerJammed || (state.Throttle > 0.1f && (state.StokerSpeed < 0.05f || state.StokerMasterBar < 0.3f))))
                return ChecklistId.StokerSetup;
            if (state.DriftingHookedUp || (state.Throttle <= 0.02f && Math.Abs(state.ForwardSpeedMs) >= 1.5f))
                return ChecklistId.DriftingAndLayover;
            if (state.HasStoker && state.Throttle > 0.1f)
                return ChecklistId.StokerSetup;
            return state.Throttle <= 0.02f ? ChecklistId.DriftingAndLayover : ChecklistId.InjectorsAndWater;
        }

        /// <summary>Steps forward or backward through the checklists applicable to <paramref name="state"/>.</summary>
        public static ChecklistId StepSelection(ChecklistSnapshot state, ChecklistId current, int delta)
        {
            var list = Applicable(state);
            if (list.Count == 0)
                return current;
            var index = -1;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == current)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
                return list[0];
            var next = ((index + delta) % list.Count + list.Count) % list.Count;
            return list[next];
        }

        /// <summary>
        /// Evaluates <paramref name="selected"/> (or auto-selects when null or
        /// not applicable to this engine) against <paramref name="state"/>.
        /// </summary>
        public static Checklist Evaluate(ChecklistSnapshot state, ChecklistId? selected = null)
        {
            var applicable = Applicable(state);
            var isApplicable = false;
            if (selected.HasValue)
            {
                for (var i = 0; i < applicable.Count; i++)
                {
                    if (applicable[i] == selected.Value)
                    {
                        isApplicable = true;
                        break;
                    }
                }
            }
            var auto = !isApplicable;
            var id = isApplicable ? selected.Value : AutoSelect(state);
            IReadOnlyList<ChecklistStep> steps;
            switch (id)
            {
                case ChecklistId.CoalColdStart:
                    steps = EvaluateCoalColdStart(state);
                    break;
                case ChecklistId.OilColdStart:
                    steps = EvaluateOilColdStart(state);
                    break;
                case ChecklistId.InjectorsAndWater:
                    steps = EvaluateInjectorsAndWater(state);
                    break;
                case ChecklistId.CleaningClinkerAndAsh:
                    steps = EvaluateCleaning(state);
                    break;
                case ChecklistId.StokerSetup:
                    steps = EvaluateStoker(state);
                    break;
                case ChecklistId.DriftingAndLayover:
                    steps = EvaluateDrifting(state);
                    break;
                default:
                    steps = Array.Empty<ChecklistStep>();
                    break;
            }
            var title = id == ChecklistId.InjectorsAndWater && !string.IsNullOrEmpty(state.InjectorName)
                ? $"{TitleOf(id)} ({state.InjectorName})"
                : TitleOf(id);
            return new Checklist(id, title, ShortNameOf(id), steps, auto);
        }

        private static IReadOnlyList<ChecklistStep> EvaluateCoalColdStart(ChecklistSnapshot s)
        {
            var coalLit = s.FireOn && (s.BurningCoalMass >= EstablishedCoalKg || s.CoalCaught);
            var hasWater = s.GlassLevel >= FiringAdvice.LowGlass;
            var damperOpen = s.Damper >= 0.5f || coalLit;
            var doorShut = coalLit && (!s.HasDoor || s.DoorOpening <= 0.1f);

            if (s.LighterLightsCoal)
            {
                var coalLaid = s.TotalCoalMass > 0f || coalLit;
                return BuildSteps(new[]
                {
                    ("Water in glass", $"check the glass (~half, now {s.GlassLevel * 100f:F0}%); shed water hose or hold C", hasWater),
                    ("Damper open", "open the damper so natural draft reaches the grate", damperOpen),
                    ("Shovel coal onto grate", "shovel coal onto the grate through the open fire door", coalLaid),
                    ("Light coal with lighter", "light the laid coal with the lighter at the open fire door", coalLit),
                    ("Fire door shut", "shut the fire door once the coal fire is established", doorShut),
                });
            }

            var pieces = s.WoodMass / StarterFuel.PieceMass;
            var woodLaid = pieces >= RecommendedWoodPieces - 0.1f || s.WoodCaught || coalLit;
            var ragIn = s.RagLit || s.WoodCaught || coalLit;
            var woodCaught = s.WoodCaught || coalLit;

            return BuildSteps(new[]
            {
                ("Water in glass", $"check the glass (~half, now {s.GlassLevel * 100f:F0}%); shed water hose or hold C", hasWater),
                ("Damper open", "open the damper so natural draft reaches the grate", damperOpen),
                ("4+ wood pieces laid", $"lay {RecommendedWoodPieces}+ pieces together through the open door (now {Math.Floor(pieces + 0.05f):F0})", woodLaid),
                ("Lit oiled rag in firebox", "oil a rag with the oil can, light it with the lighter, place it on the wood", ragIn),
                ("Wood caught", "give the wood ~1 minute to catch and spread before adding coal", woodCaught),
                ("First coal lit", "add a little coal over the burning wood and let it catch", coalLit),
                ("Fire door shut", "shut the fire door once the coal fire is established", doorShut),
            });
        }

        private static IReadOnlyList<ChecklistStep> EvaluateOilColdStart(ChecklistSnapshot s)
        {
            // Unify directly with the 2901's FiringAdvice.Steps(OilReading).
            var oil = s.Oil;
            if (oil.GlassLevel <= 0f && s.GlassLevel > 0f)
                oil.GlassLevel = s.GlassLevel;
            var waterOk = s.GlassLevel >= FiringAdvice.LowGlass;
            var oilSteps = FiringAdvice.Steps(oil);

            var raw = new List<(string title, string how, bool done)>(oilSteps.Count + 1)
            {
                ("Water in glass", $"check the glass before lighting (now {s.GlassLevel * 100f:F0}%); shed water hose or hold C", waterOk),
            };
            for (var i = 0; i < oilSteps.Count; i++)
            {
                var step = oilSteps[i];
                // FiringAdvice.Steps marks step 5 false while unlit; once lit all lighting steps are done.
                var stepDone = s.FireOn || step.Done;
                raw.Add((step.Title, step.How, stepDone));
            }
            return BuildSteps(raw);
        }

        private static IReadOnlyList<ChecklistStep> EvaluateInjectorsAndWater(ChecklistSnapshot s)
        {
            var working = s.InjectorWorking || s.InjectorState == InjectorState.Working;
            var broken = s.InjectorState == InjectorState.Broken;
            var hot = s.InjectorState == InjectorState.Hot;
            var crack = s.InjectorCrackOpening > 0f ? s.InjectorCrackOpening : InjectorSpec.DefaultCrackOpening;
            var pressureOk = s.BoilerGaugeBar >= InjectorMinGaugeBar || working;
            var waterOpen = s.InjectorWaterValve > InjectorSpec.OpenThreshold || working;
            // Injector.Next transitions Priming -> Working once pickUpTimer reaches PickUpTime
            // and marks the jet taken; ticking Prime on low overflow earlier would break the jet.
            var primed = working;
            var feeding = working && (s.HasSeparateSteamValve ? s.InjectorSteamFeed : s.InjectorWaterValve > crack);
            var glassHalf = s.GlassLevel >= 0.40f && s.GlassLevel <= 0.80f;
            var side = string.IsNullOrEmpty(s.InjectorName) ? "" : s.InjectorName + " ";

            var items = new List<(string title, string how, bool done)>
            {
                ("Pressure ≥ 2.5 bar", $"boiler needs at least {InjectorMinGaugeBar:F1} bar gauge (now {s.BoilerGaugeBar:F1} bar)", pressureOk),
            };
            if (s.HasTenderWaterValve)
                items.Add(("Tender valve open", $"open the {side}tender water valve on the tender front", s.TenderWaterValveOpen));
            items.Add(("Water valve open",
                s.HasSeparateSteamValve ? $"open {side}INJ WATER (at least ~40% to feed steadily)" : $"crack the {side}injector lever to let water in",
                waterOpen));
            if (broken)
            {
                items.Add((
                    s.HasSeparateSteamValve ? "Jet broken: reset steam" : "Jet broken: shut and re-crack",
                    s.HasSeparateSteamValve
                        ? $"Shut {side}INJ STEAM to Off, then back to Prime until overflow quiets"
                        : $"Shut the {side}injector lever, then crack it until it picks up",
                    false));
            }
            else if (hot)
            {
                items.Add((
                    s.HasSeparateSteamValve ? "Injector hot: cool at Prime" : "Injector hot: cool the body",
                    s.HasSeparateSteamValve
                        ? "Hold at Prime to cool, or shut it for a bit"
                        : "Hold the lever cracked to cool, or shut it for a bit",
                    false));
            }
            items.Add((
                s.HasSeparateSteamValve ? "Steam to Prime until overflow quiets" : "Crack lever until it sings",
                s.HasSeparateSteamValve ? $"set {side}INJ STEAM to Prime and listen for the overflow to quiet" : $"hold the {side}lever cracked until the jet picks up",
                primed));
            items.Add((
                s.HasSeparateSteamValve ? "Steam to Feed" : "Open lever to feed",
                s.HasSeparateSteamValve ? $"move {side}INJ STEAM to Feed once picked up" : $"open the {side}lever past ~40% to feed without waste",
                feeding));
            items.Add(("Glass ~half", $"keep the gauge glass between 40% and 75% (now {s.GlassLevel * 100f:F0}%)", glassHalf));
            return BuildSteps(items);
        }

        private static IReadOnlyList<ChecklistStep> EvaluateCleaning(ChecklistSnapshot s)
        {
            var clinkerClear = !s.NeedsCleaning;
            var stopped = Math.Abs(s.ForwardSpeedMs) < 1.5f && s.Throttle <= 0.1f;
            var pulledBack = clinkerClear || s.FrontCoalKg <= s.BackCoalKg * 0.65f + 0.5f;
            var sliced = clinkerClear || s.SlicedRecently;
            var pushedForward = clinkerClear || (s.ClinkerMass > 0f && s.FrontClinkerKg >= s.ClinkerMass * 0.45f);
            var dropped = clinkerClear || s.DropGrateOpenedRecently;
            var spreadAndFresh = clinkerClear && s.FireOn && s.TotalCoalMass > 0f && s.ThinZone < 0;
            var ashpanEmpty = !s.AshpanFull && s.AshpanFill < FiringAdvice.FullAshpan * 0.5f;

            var ashHow = s.OnFreeAshTrack
                ? $"hold the ASHPAN handle from the ground (now {s.AshpanFill * 100f:F0}%; free on this track)"
                : $"hold the ASHPAN handle from the ground on a service, parking, or shed track (now {s.AshpanFill * 100f:F0}%)";

            return BuildSteps(new[]
            {
                ("Stop / light work", "shut off and stop or work very lightly while cleaning", clinkerClear ? (ashpanEmpty || stopped) : stopped),
                ("Pull live fire back", "use the fire iron (scroll back) to pull live coal off the front grate", pulledBack),
                ("Slice clinker with fire iron", "click through the open door with the fire iron to break clinker loose", sliced),
                ("Push clinker forward", "scroll forward with the fire iron to push broken clinker onto the drop grate", pushedForward),
                ("Drop grate briefly", "open the DROP GRATE floor lever for a few seconds, then shut it", dropped),
                ("Spread fire & add fresh coal", "level the live fire across the grate and shovel a few fresh rounds", spreadAndFresh),
                ("Shake ash through the grate", "half a stroke of the fire-dump lever (or the Shake grate key) with the throttle eased", !s.WantsShake),
                ("Empty ashpan on service/shed track", ashHow, ashpanEmpty),
            });
        }

        private static IReadOnlyList<ChecklistStep> EvaluateStoker(ChecklistSnapshot s)
        {
            var pressureUp = s.BoilerGaugeBar >= StokerMinGaugeBar;
            var forward = s.StokerForward && !s.StokerJammed;
            var masterSet = s.StokerMasterBar >= 0.4f;
            var jetsSet = s.StokerFrontBar >= 0.2f && s.StokerBackBar >= 0.15f && s.StokerFinesBar >= 0.15f;
            var feeding = s.StokerSpeed > 0.05f && !s.StokerJammed;
            var backCorners = s.BackCornersCovered;

            return BuildSteps(new[]
            {
                ("Steam pressure up", $"at least {StokerMinGaugeBar:F0} bar gauge to turn the stoker (now {s.BoilerGaugeBar:F1} bar)", pressureUp),
                ("Floor lever Forward", s.StokerJammed ? "stoker jammed: throw floor lever to Reverse to clear, then Forward" : "set the stoker floor lever to Forward", forward),
                ("Master jet set", $"open MASTER jet (~1.4 bar; now {s.StokerMasterBar:F1} bar)", masterSet),
                ("Front/Back/Fines jets set", $"start near 1.2 front, 0.8 back, 0.7 fines (now {s.StokerFrontBar:F1}/{s.StokerBackBar:F1}/{s.StokerFinesBar:F1} bar)", jetsSet),
                ("Stoker engine feeding", s.StokerJammed ? "clear jam first, then open STOKER valve to hold fire steady" : "open STOKER steam valve to hold the bed steady while working", feeding),
                ("Hand-fire back corners", "keep the back corners and under the door filled by shovel", backCorners),
            });
        }

        private static IReadOnlyList<ChecklistStep> EvaluateDrifting(ChecklistSnapshot s)
        {
            var moving = Math.Abs(s.ForwardSpeedMs) >= 1.5f;
            var throttleShut = s.Throttle <= 0.02f;
            var gearProtected = !moving || !s.DriftingHookedUp;
            var blowerCracked = !moving || s.Blower >= 0.05f;
            var damperEased = moving ? s.Damper <= 0.75f : s.Damper <= 0.45f;

            var gearHow = s.HasByPassValves && s.DriftingHookedUp
                ? (s.HasDriftingValve
                    ? "by-pass valves aren't enough at this speed: open the DRIFTING valve or drop reverser to full gear (≥75% cut-off)"
                    : "by-pass valves aren't enough at this speed: drop reverser to full gear (≥75% cut-off)")
                : s.HasByPassValves
                    ? "by-pass valves open automatically while drifting; open DRIFTING valve or drop to full gear at high speed"
                    : s.HasDriftingValve
                        ? "open the DRIFTING valve or drop reverser to full gear (≥75% cut-off)"
                        : s.HasSniftingValves
                            ? "drop reverser to full gear (snifting valves spare part of the wear hooked up)"
                            : "drop the reverser to full gear (≥75% cut-off) to prevent cylinder braking & wear";

            return BuildSteps(new[]
            {
                ("Throttle shut", "close the regulator cleanly before drifting or laying over", throttleShut),
                ("Reverser full gear or drifting/by-pass", gearHow, gearProtected),
                ("Blower cracked on shut-off", "crack the blower on shut-off to keep draft forward and prevent blowback", blowerCracked),
                ("Damper eased for standing", moving ? "ease the damper if pressure climbs toward the safety valve" : "ease the damper standing so the fire rests without lifting the safeties", damperEased),
            });
        }

        private static IReadOnlyList<ChecklistStep> BuildSteps(IReadOnlyList<(string title, string how, bool done)> items)
        {
            var current = -1;
            for (var i = 0; i < items.Count; i++)
            {
                if (!items[i].done)
                {
                    current = i;
                    break;
                }
            }
            var result = new ChecklistStep[items.Count];
            for (var i = 0; i < items.Count; i++)
                result[i] = new ChecklistStep(items[i].title, items[i].how, items[i].done, i == current);
            return result;
        }
    }
}
