using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
    /// <summary>
    /// The distributor jets of a Standard-type stoker (HT), as the manifold
    /// labels them: two back jets for the back corners, two front jets that
    /// throw each half of the coal up the grate, and the fine-coal jet that
    /// spreads fines over the whole firebox.
    /// </summary>
    public enum StokerJet
    {
        LeftBack,
        RightBack,
        LeftFront,
        RightFront,
        Fines,
    }

    /// <summary>Which way the stoker engine's operating lever sends it.</summary>
    public enum StokerDirection
    {
        Forward,
        Neutral,
        Reverse,
    }

    /// <summary>The stoker's cab controls, each from 0 (shut) to 1 (wide open).</summary>
    public struct StokerControls
    {
        /// <summary>The operating (reverse) lever: forward feeds, neutral stops, reverse backs the screw up.</summary>
        public StokerDirection Direction;

        /// <summary>Stoker engine throttle: its speed, and so the feed.</summary>
        public float Throttle;

        /// <summary>Intermediate (master) jet valve: the manifold's pressure.</summary>
        public float MasterJet;

        public float LeftBack;
        public float RightBack;
        public float LeftFront;
        public float RightFront;
        public float Fines;

        public float Jet(StokerJet jet)
        {
            switch (jet)
            {
                case StokerJet.LeftBack: return LeftBack;
                case StokerJet.RightBack: return RightBack;
                case StokerJet.LeftFront: return LeftFront;
                case StokerJet.RightFront: return RightFront;
                case StokerJet.Fines: return Fines;
                default: throw new ArgumentOutOfRangeException(nameof(jet), jet, null);
            }
        }

        /// <summary>
        /// A sound setting for steady work: about 21 psi on the manifold, the
        /// front and back jets and the fines part open. The front has its
        /// share of the coal at light work; the back, drawing hardest under a
        /// sealed arch, runs short, as stokers left it, for the shovel.
        /// </summary>
        public static StokerControls Working(float throttle) => new StokerControls
        {
            Throttle = throttle,
            MasterJet = 0.35f,
            LeftBack = 0.6f,
            RightBack = 0.6f,
            LeftFront = 0.6f,
            RightFront = 0.6f,
            Fines = 0.5f,
        };
    }

 public sealed class StokerSpec
    {
        /// <summary>Heat to raise a kg of feedwater to steam, in J/kg, for steam-use estimates.</summary>
        public const float SteamHeat = 2.6e6f;

        // Jets and engine together use about 1.8 % of the boiler's
        // evaporation at full work: jets 1.1 %, engine 0.7 % (design.md [6]).
        private const float EngineSteamShare = 0.0072f;
        private const float WorkingJetSteamShare = 0.0112f;

        public StokerSpec(FireboxParameters firebox, float maxFeedRate)
        {
            if (firebox == null)
                throw new ArgumentNullException(nameof(firebox));
            if (!(maxFeedRate > 0f))
                throw new ArgumentOutOfRangeException(nameof(maxFeedRate), maxFeedRate, "Must be positive.");

            MaxFeedRate = maxFeedRate;
            var evaporation = firebox.MaxHeatOutput / SteamHeat;
            EngineSteamAtFull = EngineSteamShare * evaporation;
            JetSteamPerBar = WorkingJetSteamShare * evaporation / WorkingJetLoad(this);
        }

        /// <summary>Coal fed at full engine speed, in firebox kg/s.</summary>
        public float MaxFeedRate { get; }

        /// <summary>Boiler pressure below which the engine won't turn (CCL's stoker uses 2).</summary>
        public float MinWorkingPressure { get; set; } = 2f;

        /// <summary>Boiler pressure at which the open throttle reaches full speed (CCL's default 6).</summary>
        public float FullSpeedPressure { get; set; } = 6f;

        /// <summary>Time for the engine and conveyor to come up to speed, in s.</summary>
        public float EngineSmoothTime { get; set; } = 3f;

        /// <summary>The engine gauge at full speed, in bar (about 36 psi).</summary>
        public float EngineWorkingPressure { get; set; } = 2.5f;

        /// <summary>Engine steam at full speed, in kg/s.</summary>
        public float EngineSteamAtFull { get; }

        /// <summary>Steam through one fully open jet per bar of manifold pressure, in kg/s.</summary>
        public float JetSteamPerBar { get; }

        /// <summary>Manifold pressure with the master valve wide open, in bar (60 psi).</summary>
        public float ManifoldMaxPressure { get; set; } = 4.1f;

        /// <summary>Front jet pressure that throws its lane to the front row, in bar (about 23 psi).</summary>
        public float FrontReachPressure { get; set; } = 1.6f;

        /// <summary>Spread of a front lane's coal about where it is thrown, in rows (one standard deviation).</summary>
        public float ThrowSpread { get; set; } = 0.6f;

        /// <summary>Back jet pressure that blows its lane into the back corner, in bar.</summary>
        public float BackReachPressure { get; set; } = 0.8f;

        /// <summary>Fine-coal jet pressure that spreads fines over the whole grate, in bar.</summary>
        public float FinesReachPressure { get; set; } = 0.7f;

        /// <summary>How far the draft carries thrown coal toward the front at full draft, in rows.</summary>
        public float DraftCarry { get; set; } = 0.3f;

        /// <summary>
        /// Share of each back jet's lane of the lump coal. Kept small: on real
        /// stokers the back corners run short and are fired by hand.
        /// </summary>
        public float BackLaneShare { get; set; } = 0.065f;

        /// <summary>
        /// How much more of the lump coal goes to the left lanes than the
        /// right, as a share of each lane: a Standard HT's screw sends the
        /// fines right and the lumps left, so its right back corner runs short.
        /// </summary>
        public float LumpsLeftBias { get; set; }

        /// <summary>Share of a front lane that lands on its own side; the rest goes up the middle.</summary>
        public float FrontLaneSideShare { get; set; } = 0.72f;

        /// <summary>Share of the crushed coal that is fines.</summary>
        public float FinesShare { get; set; } = 0.25f;

        /// <summary>Nominal soaked coal passed before packing fills the screw; inferred, not an empirical HT rate.</summary>
        public float CoalToPack { get; set; } = 5000f;
        public bool PackingJams { get; set; } = true;
        /// <summary>Maximum fuel held on the table, as seconds of rated feed.</summary>
        public float TableCapacitySeconds { get; set; } = 3f;
        public float TableResidenceSeconds { get; set; } = 2f;

        /// <summary>
        /// Coal fed, on average, between jams from lumps or tramp iron, in
        /// firebox kg; zero or less for none.
        /// </summary>
        public float MeanCoalBetweenJams { get; set; } = 2500f;

        /// <summary>Engine turns a second at full speed.</summary>
        public float RevolutionsPerSecond { get; set; } = 1.5f;

        /// <summary>Turns in reverse that clear a jam: four or five, sometimes six (design.md [1]).</summary>
        public float MinRevolutionsToClear { get; set; } = 4f;

        public float MaxRevolutionsToClear { get; set; } = 6f;

        /// <summary>Share of the fines the draft carries out of the stack at full draft.</summary>
        public float FinesCarryoverAtFullDraft { get; set; } = 0.475f;

        /// <summary>How sharply carryover grows with draft.</summary>
        public float CarryoverExponent { get; set; } = 1.4f;

        /// <summary>
        /// Share of fine coal bound into fresh-burning clumps, at
        /// <see cref="DampFinesMoisture"/> and wetter (inferred: "With fine
        /// coal, wetting it down tends to prevent too much fine coal being
        /// drawn out through the stack", Santa Fe, Instructions for Enginemen,
        /// 1921, p.31; "moist fine coal cokes better", Baker, 1922).
        /// </summary>
        public float DampFinesHold { get; set; } = 0.4f;

        /// <summary>
        /// Moisture at which fines hold best: a bed of screenings passed air
        /// most freely at 7.5 % (Power Plant Engineering, 15 Feb 1923).
        /// </summary>
        public float DampFinesMoisture { get; set; } = 0.075f;

        /// <summary>
        /// Rain-soaked coal sticks on the distributor table and the jets
        /// throw it as if this much weaker (inferred: "raise the jets
        /// slightly" for wet coal, New York State, Locomotive Firing, 1944,
        /// Unit P-6 pp.81-82).
        /// </summary>
        public float WetTableLoss { get; set; } = 0.15f;

        // Jet opening times manifold pressure, summed over the jets, at the
        // working setting: what the working jet steam share is spent on.
        private static float WorkingJetLoad(StokerSpec spec)
        {
            var working = StokerControls.Working(1f);
            var load = 0f;
            for (var jet = 0; jet < Stoker.JetCount; jet++)
                load += working.Jet((StokerJet)jet);
            return load * working.MasterJet * spec.ManifoldMaxPressure;
        }
    }

    /// <summary>What the stoker does with the boiler, tender, and fire this tick.</summary>
    public struct StokerInputs
    {
        public StokerControls Controls;

        /// <summary>Boiler pressure, in the boiler pressure port's units.</summary>
        public float BoilerPressure;

        /// <summary>Coal in the tender, in firebox kg.</summary>
        public float TenderCoal;

        /// <summary>The exhaust draft through the firebox, in kg/s of air.</summary>
        public float AirFlow;

        /// <summary>The water's share of the tender's coal (<see cref="CoalMoisture"/>), 0 to 1.</summary>
        public float CoalMoisture;
        /// <summary>Optional post-crusher fine fraction for a different coal mix; absent uses the stoker's normal coal.</summary>
        public float? CoalFinesShare;
    }

    /// <summary>The stoker's output for one tick.</summary>
    public struct StokerFlow
    {
        /// <summary>The conveyor is jammed: the engine stalls going forward.</summary>
        public bool Jammed;

        /// <summary>Coal taken from the tender, in firebox kg.</summary>
        public float Drawn;

        /// <summary>Coal that landed on the grate, in firebox kg.</summary>
        public float Fed;

        /// <summary>Coal lost as cinders out of the stack or over the arch, in kg/s.</summary>
        public float CinderRate;
        /// <summary>Fuel actually leaving the table, including cinders, in kg/s; independent of screw speed.</summary>
        public float EjectedRate;

        /// <summary>Steam used by the engine and jets, in kg/s.</summary>
        public float SteamUsed;

        /// <summary>The stoker engine gauge, in bar.</summary>
        public float EnginePressure;

        /// <summary>Pressure in the jet manifold, in bar.</summary>
        public float ManifoldPressure;

        public float LeftBackPressure;
        public float RightBackPressure;
        public float LeftFrontPressure;
        public float RightFrontPressure;
        public float FinesPressure;

        /// <summary>Visible retained coal and its clearing notice, also shown to multiplayer clients.</summary>
        public float TableCoal, TableFill;
        public bool TableNeedsClearing;
    }

    /// <summary>
    /// A Standard-type mechanical stoker. A steam engine drives a screw that
    /// carries crushed coal from the tender to a table just inside the door;
    /// steam jets blow it across the grate. The engine's throttle sets the
    /// feed; each jet's pressure sets how far its lane of coal goes, and hard
    /// draft carries it farther. A shut jet drops its coal at the door, which
    /// builds a heel; too much pressure blows coal over the arch. The fines
    /// catch at once while wet bound fines join fresh coal. Jet capacity is
    /// shared by the feed; retained fuel stays on the table until delivered
    /// or cleared with the hook. Coarse coal loads the crusher, wet fines can
    /// pack the screw, and the draft carries loose fines out of the stack.
    /// </summary>
    public sealed class Stoker
    {
        public const int JetCount = 5;

        // Fine-coal jet pressure beyond this share of its reach blows fines
        // over the arch, half as fast as it rises.
        private const float FinesOvershoot = 1.2f;

        // A back jet's lane overshoots into the middle row past this share of
        // its reach, up to half the lane; the draft adds to its throw.
        private const float BackOvershoot = 1.3f;
        private const float BackDraftCarry = 0.3f;

        // Share of coal thrown past the front row that banks there rather than
        // going over the arch.
        private const float BankedPastFront = 0.5f;

        private readonly FireboxParameters firebox;
        private readonly float[] lumps = new float[GrateLayout.ZoneCount];
        private readonly float[] fines = new float[GrateLayout.ZoneCount];
        private readonly float[] lumpShares = new float[GrateLayout.ZoneCount];
        private readonly float[] fineShares = new float[GrateLayout.ZoneCount];
        private readonly float[] rowWeights = new float[GrateLayout.Rows + 1];

        private readonly Random random;
        private float revolutionsToClear;
        private float reversed;
        private readonly StokerHandling handling;
        private float shaftLoad = 1f;
        private bool packingJam;
        private float distributionLumpLoss;

        public Stoker(StokerSpec spec, FireboxParameters firebox, Random random = null)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            this.firebox = firebox ?? throw new ArgumentNullException(nameof(firebox));
            this.random = random ?? new Random();
            handling = new StokerHandling(spec);
        }

        /// <summary>
        /// The conveyor is jammed by a lump or tramp iron: going forward the
        /// engine stalls with its gauge near boiler pressure; reversing it a
        /// few turns clears the jam.
        /// </summary>
        public bool Jammed { get; private set; }

        /// <summary>Jams the conveyor now, as a lump or tramp iron would.</summary>
        public void Jam()
        {
            Jammed = true;
            packingJam = false;
            EngineSpeed = 0f;
            reversed = 0f;
            revolutionsToClear = Spec.MinRevolutionsToClear
                                 + (float)random.NextDouble() * (Spec.MaxRevolutionsToClear - Spec.MinRevolutionsToClear);
        }

        public StokerSpec Spec { get; }
        public float TableCoal => handling.Table.Coal;
        public float TableFill => handling.Fill;
        public bool TableNeedsClearing => handling.NeedsClearing;
        public float Packing => handling.Packing;

        public StokerState CaptureState() => new StokerState
        {
            TableCoal = TableCoal, TableFines = handling.Table.Fines, TableWater = handling.Table.Water,
            Packing = Packing, TableWarmth = handling.Warmth, TableCake = handling.Cake,
            TableNeedsClearing = TableNeedsClearing,
            EngineSpeed = EngineSpeed, Jammed = Jammed, PackingJam = packingJam,
            Reversed = reversed, TurnsToClear = revolutionsToClear,
        };

        public void RestoreState(StokerState state)
        {
            handling.Restore(state);
            Jammed = state.Jammed; packingJam = state.PackingJam && Jammed;
            EngineSpeed = Jammed ? 0f : Clamp01(StokerHandling.Finite(state.EngineSpeed));
            revolutionsToClear = Math.Max(Spec.MinRevolutionsToClear,
                Math.Min(Spec.MaxRevolutionsToClear, StokerHandling.Finite(state.TurnsToClear)));
            reversed = Math.Min(revolutionsToClear, StokerHandling.Finite(state.Reversed));
        }

        /// <summary>Engine speed as a share of full speed.</summary>
        public float EngineSpeed { get; private set; }

        /// <summary>Where the last coal fed went: each zone's share of it as lumps.</summary>
        public IReadOnlyList<float> LumpShares => lumpShares;

        /// <summary>Where the last coal fed went: each zone's share of it as fines.</summary>
        public IReadOnlyList<float> FineShares => fineShares;

        /// <summary>Share of the last coal fed lost up the stack or over the arch.</summary>
        public float LostShare { get; private set; }
        public float LostLumpShare { get; private set; }
        public float LostFineShare { get; private set; }

        /// <summary>The share of the stoked coal each zone receives, and the share lost, at the given settings.</summary>
        public float Distribute(StokerControls controls, float boilerPressure, float airFlow, float[] lumpShares, float[] fineShares, float moisture = 0f) =>
            DistributeAtRate(controls, boilerPressure, airFlow, lumpShares, fineShares, 0f, moisture);

        public float DistributeAtRate(StokerControls controls, float boilerPressure, float airFlow, float[] lumpShares, float[] fineShares,
            float feedRate, float moisture = 0f, float? finesShare = null)
        {
            if (lumpShares == null || fineShares == null
                || lumpShares.Length != GrateLayout.ZoneCount || fineShares.Length != GrateLayout.ZoneCount)
                throw new ArgumentException("Need one share per zone.");
            Array.Clear(lumpShares, 0, lumpShares.Length);
            Array.Clear(fineShares, 0, fineShares.Length);

            // Wet coal sticks on the table: the jets throw it as if weaker.
            var soaked = Clamp01((moisture - Core.CoalMoisture.Delivered) / (Core.CoalMoisture.Soaked - Core.CoalMoisture.Delivered));
            var manifold = ManifoldPressure(controls, boilerPressure) / (1f + Spec.WetTableLoss * soaked);
            var rawFines = Clamp01(StokerHandling.Finite(finesShare ?? Spec.FinesShare));
            var damp = Clamp01((moisture - Core.CoalMoisture.Delivered) / Math.Max(1e-3f, Spec.DampFinesMoisture - Core.CoalMoisture.Delivered));
            var looseFines = rawFines * (1f - Clamp01(Spec.DampFinesHold) * damp);
            var draft = DraftFraction(airFlow);
            // Bound fines launch as clumps and join fresh coal on the grate.
            var lump = 1f - looseFines;
            var lost = 0f;

            // Back jets blow their lanes sideways into the back corners; short
            // of reach the coal drops off the table under the door.
            var left = 1f + Math.Max(-1f, Math.Min(1f, Spec.LumpsLeftBias));
            var right = 2f - left;
            var backLane = lump * Spec.BackLaneShare;
            ThrowBack(backLane * left, ThrowPressure(StokerJet.LeftBack, controls, boilerPressure, manifold, feedRate, lump / .75f), draft, 0, lumpShares);
            ThrowBack(backLane * right, ThrowPressure(StokerJet.RightBack, controls, boilerPressure, manifold, feedRate, lump / .75f), draft, GrateLayout.Columns - 1, lumpShares);

            // Front jets throw their halves up the grate; past the front row
            // the coal goes over the arch.
            var frontLane = lump * (1f - 2f * Spec.BackLaneShare) / 2f;
            lost += ThrowFront(frontLane * left, ThrowPressure(StokerJet.LeftFront, controls, boilerPressure, manifold, feedRate, lump / .75f), draft, 0, lumpShares);
            lost += ThrowFront(frontLane * right, ThrowPressure(StokerJet.RightFront, controls, boilerPressure, manifold, feedRate, lump / .75f), draft, GrateLayout.Columns - 1, lumpShares);
            distributionLumpLoss = lost;

            // Fines: the draft lifts part of them straight out of the stack;
            // the fine-coal jet spreads the rest, or they fall at the door.
            var carried = Math.Min(0.95f, Spec.FinesCarryoverAtFullDraft * (float)Math.Pow(Math.Min(draft, 1.5f), Spec.CarryoverExponent));
            var remaining = looseFines * (1f - carried);
            lost += looseFines * carried;
            var reach = ThrowPressure(StokerJet.Fines, controls, boilerPressure, manifold, feedRate, looseFines / .25f) / Spec.FinesReachPressure;
            var overArch = Math.Min(0.5f, Math.Max(0f, 0.5f * (reach - FinesOvershoot)));
            lost += remaining * overArch;
            remaining *= 1f - overArch;
            var spread = Clamp01(reach);
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
                fineShares[i] += remaining * spread / GrateLayout.ZoneCount;
            fineShares[(int)GrateZone.UnderDoor] += remaining * (1f - spread);

            return lost;
        }

        /// <summary>
        /// Runs the stoker for <paramref name="dt"/> seconds, putting its coal on
        /// <paramref name="fire"/>'s grate.
        /// </summary>
        public StokerFlow Step(float dt, StokerInputs inputs, FireModel fire)
        {
            if (fire == null)
                throw new ArgumentNullException(nameof(fire));
            var flow = new StokerFlow();
            if (!(dt > 0f) || float.IsInfinity(dt))
                return flow;

            var controls = inputs.Controls;
            var moisture = Math.Min(Core.CoalMoisture.Soaked, StokerHandling.Finite(inputs.CoalMoisture));
            var rawFines = Clamp01(StokerHandling.Finite(inputs.CoalFinesShare ?? Spec.FinesShare));
            handling.Warm(dt, fire.FireOn);
            shaftLoad = handling.ShaftLoad(moisture, rawFines);
            var rate = AdvanceEngine(dt, controls.Throttle, inputs.BoilerPressure, controls.Direction, true) * handling.Yield(moisture, rawFines);
            var room = Math.Max(0f, fire.Parameters.MaxCoalCapacity - fire.CoalMass);
            var offered = Math.Min(rate * dt, Math.Min(StokerHandling.Finite(inputs.TenderCoal),
                Math.Min(room, Math.Max(0f, handling.Capacity - TableCoal))));
            var incoming = new StokerCoal { Coal = offered, Fines = offered * rawFines, Water = offered * StokerCoal.RainWater(moisture) };
            var delivery = handling.Prepare(dt, incoming, JetBudget(controls, inputs.BoilerPressure));
            flow.Drawn = offered;
            if (delivery.Coal > 0f)
            {
                var lostShare = DistributeAtRate(controls, inputs.BoilerPressure, inputs.AirFlow, lumps, fines,
                    delivery.Coal / dt, delivery.Moisture, delivery.FineShare);
                LostShare = lostShare;
                LostLumpShare = distributionLumpLoss;
                LostFineShare = Math.Max(0f, lostShare - LostLumpShare);
                for (var i = 0; i < GrateLayout.ZoneCount; i++)
                {
                    lumpShares[i] = lumps[i];
                    fineShares[i] = fines[i];
                    lumps[i] *= delivery.Coal;
                    fines[i] *= delivery.Coal;
                }
                var landing = delivery.Coal * (1f - lostShare);
                var accepted = fire.AddStoked(lumps, fines, delivery.Moisture);
                // Coal not accepted by a full box stays on the table. Intake
                // and ejection are separate transactions; never refund fuel
                // to a tender after it has entered the retained mixture.
                var share = landing > 0f ? accepted / landing : 1f;
                flow.Fed = accepted;
                flow.CinderRate = delivery.Coal * lostShare * share / dt;
                flow.EjectedRate = delivery.Coal * share / dt;
                handling.Remove(delivery.Scale(share));
            }
            else
            {
                Array.Clear(lumpShares, 0, lumpShares.Length);
                Array.Clear(fineShares, 0, fineShares.Length);
                LostShare = 0f;
                LostLumpShare = LostFineShare = 0f;
            }
            handling.PassCoal(offered, moisture, rawFines, rate);
            if (!Jammed && Spec.PackingJams && Packing >= 1f)
            {
                Jam();
                packingJam = true;
            }
            else if (!Jammed && offered > 0f && Spec.MeanCoalBetweenJams > 0f)
            {
                // A throughput hazard, independent of the frame duration.
                // Coarse coal adds crushing obstructions; foreign matter
                // remains possible even in a fine-rich load.
                var crushing = .5f + .5f * (1f - rawFines) / .75f;
                if (random.NextDouble() < 1d - Math.Exp(-offered * crushing / Spec.MeanCoalBetweenJams)) Jam();
            }

            ReadGauges(controls, inputs.BoilerPressure, ref flow);
            return flow;
        }

        /// <summary>A hook or rake dislodges retained fuel onto the grate; no fuel is drawn from the tender.</summary>
        public float ClearTable(FireModel fire, float most = 3f)
        {
            if (fire == null) throw new ArgumentNullException(nameof(fire));
            if (!(most > 0f)) return 0f;
            // The hook scrapes the plate even when there is no loose fuel
            // left to move. Coating is not additional fuel.
            handling.Cake = 0f;
            if (!(TableCoal > 0f)) return 0f;
            var moved = Math.Min(most, TableCoal);
            var mixture = handling.Table.Scale(moved / TableCoal);
            Array.Clear(lumps, 0, lumps.Length); Array.Clear(fines, 0, fines.Length);
            lumps[(int)GrateZone.UnderDoor] = moved;
            var accepted = fire.AddStoked(lumps, fines, mixture.Moisture);
            handling.Remove(mixture.Scale(accepted / moved));
            return accepted;
        }

        // Each jet carries its own lane. Closing a different valve cannot
        // directly weaken this jet; the common table can still back up.
        private float ThrowPressure(StokerJet jet, StokerControls controls, float boilerPressure,
            float effectiveManifold, float feedRate, float laneLoad)
        {
            var working = StokerControls.Working(1f);
            var pressure = ManifoldPressure(controls, boilerPressure) * Clamp01(controls.Jet(jet));
            var reference = working.MasterJet * Spec.ManifoldMaxPressure * working.Jet(jet);
            var loading = feedRate / Spec.MaxFeedRate * laneLoad / Math.Max(.1f, pressure / Math.Max(.01f, reference));
            return effectiveManifold * Clamp01(controls.Jet(jet)) / (float)Math.Sqrt(Math.Max(1f, loading));
        }

        private float JetBudget(StokerControls controls, float boilerPressure)
        {
            var opening = 0f;
            var workingOpening = 0f;
            var working = StokerControls.Working(1f);
            for (var i = 0; i < JetCount; i++)
            {
                opening += Clamp01(controls.Jet((StokerJet)i));
                workingOpening += working.Jet((StokerJet)i);
            }
            return ManifoldPressure(controls, boilerPressure) * opening /
                Math.Max(.01f, working.MasterJet * Spec.ManifoldMaxPressure * workingOpening);
        }

        /// <summary>
        /// Runs the engine alone for <paramref name="dt"/> seconds: it eases
        /// toward the throttle's speed when the boiler has pressure, in the
        /// lever's direction. Jammed, it stalls going forward; reversed far
        /// enough, it clears the jam. Returns the feed rate it drives, in kg/s
        /// (nothing unless it runs forward).
        /// </summary>
        public float RunEngine(float dt, float throttle, float boilerPressure, StokerDirection direction = StokerDirection.Forward)
        {
            // The stock-firebox adapter uses only the engine, and must not
            // stall against fuel retained by a previously enabled zoned fire.
            shaftLoad = 1f;
            return AdvanceEngine(dt, throttle, boilerPressure, direction, false);
        }

        private float AdvanceEngine(float dt, float throttle, float boilerPressure, StokerDirection direction, bool loaded)
        {
            var feeding = direction == StokerDirection.Forward && !Jammed;
            if (!(dt > 0f))
                return feeding ? EngineSpeed * Spec.MaxFeedRate : 0f;
            var pressureFactor = Clamp01((boilerPressure - Spec.MinWorkingPressure)
                                         / (Spec.FullSpeedPressure - Spec.MinWorkingPressure));
            var stalled = Jammed && direction == StokerDirection.Forward;
            // Ease into table backpressure instead of switching between a
            // fully running and stopped screw at the capacity boundary.
            var tableRoom = loaded && feeding ? Clamp01((1f - handling.Fill) * 5f) : 1f;
            var target = direction == StokerDirection.Neutral || stalled ? 0f
                : Math.Min(1f, Clamp01(throttle) * pressureFactor * tableRoom / (direction == StokerDirection.Reverse ? 1f : shaftLoad));
            EngineSpeed = stalled ? 0f : EngineSpeed + (target - EngineSpeed) * (1f - (float)Math.Exp(-dt / Spec.EngineSmoothTime));
            if (EngineSpeed < 1e-4f && target == 0f)
                EngineSpeed = 0f;

            if (Jammed && direction == StokerDirection.Reverse)
            {
                reversed += EngineSpeed * Spec.RevolutionsPerSecond * dt;
                if (reversed >= revolutionsToClear)
                {
                    Jammed = false;
                    packingJam = false;
                    reversed = 0f;
                }
            }
            if (direction == StokerDirection.Reverse)
                handling.Reverse(EngineSpeed * Spec.RevolutionsPerSecond * dt);
            return feeding ? EngineSpeed * Spec.MaxFeedRate : 0f;
        }

        /// <summary>Fills in the gauges and the steam used at the engine's present speed.</summary>
        public void ReadGauges(StokerControls controls, float boilerPressure, ref StokerFlow flow)
        {
            handling.ReadTable();
            flow.TableCoal = TableCoal;
            flow.TableFill = TableFill;
            flow.TableNeedsClearing = TableNeedsClearing;
            flow.ManifoldPressure = ManifoldPressure(controls, boilerPressure);
            flow.LeftBackPressure = flow.ManifoldPressure * Clamp01(controls.LeftBack);
            flow.RightBackPressure = flow.ManifoldPressure * Clamp01(controls.RightBack);
            flow.LeftFrontPressure = flow.ManifoldPressure * Clamp01(controls.LeftFront);
            flow.RightFrontPressure = flow.ManifoldPressure * Clamp01(controls.RightFront);
            flow.FinesPressure = flow.ManifoldPressure * Clamp01(controls.Fines);
            var jetLoad = flow.LeftBackPressure + flow.RightBackPressure + flow.LeftFrontPressure
                          + flow.RightFrontPressure + flow.FinesPressure;
            flow.SteamUsed = Spec.EngineSteamAtFull * EngineSpeed * shaftLoad + Spec.JetSteamPerBar * jetLoad;
            flow.Jammed = Jammed;
            // Stalled against a jam, the engine's gauge climbs to what its
            // throttle lets through from the boiler.
            flow.EnginePressure = Jammed && controls.Direction == StokerDirection.Forward
                ? Clamp01(controls.Throttle) * Math.Max(0f, boilerPressure)
                : Math.Min(Math.Max(0f, boilerPressure), Spec.EngineWorkingPressure * EngineSpeed * shaftLoad
                    + Math.Max(0f, shaftLoad - 1f) * .25f * Math.Max(0f, boilerPressure) * Clamp01(controls.Throttle));
        }

        // The manifold's gauge pressure: the boiler's (absolute) less the
        // atmosphere's, so a cold boiler blows nothing through the jets.
        private float ManifoldPressure(StokerControls controls, float boilerPressure) =>
            Clamp01(controls.MasterJet) * Math.Min(Spec.ManifoldMaxPressure, Math.Max(0f, boilerPressure - 1f));

        // Draft as a share of what the stock maximum combustion rate needs.
        private float DraftFraction(float airFlow) =>
            Math.Max(0f, airFlow) / (firebox.MaxCombustionRate * FireModel.AirPerCoal);

        private void ThrowBack(float lane, float pressure, float draft, int column, float[] shares)
        {
            // A jet that is shut throws nothing; the draft only carries coal a jet has thrown.
            var reach = pressure > 0f ? pressure / Spec.BackReachPressure + BackDraftCarry * draft : 0f;
            var corner = GrateLayout.At(0, column);
            if (reach < 1f)
            {
                shares[(int)corner] += lane * reach;
                shares[(int)GrateZone.UnderDoor] += lane * (1f - reach);
                return;
            }
            var overshoot = Math.Min(0.5f, Math.Max(0f, reach - BackOvershoot));
            shares[(int)corner] += lane * (1f - overshoot);
            shares[(int)GrateLayout.At(1, column)] += lane * overshoot;
        }

        // Spreads a front lane over the rows about where it is thrown. Row 3
        // is past the front row: half of that banks against the tube plate on
        // the front row, half goes over the arch. Returns the share lost.
        private float ThrowFront(float lane, float pressure, float draft, int column, float[] shares)
        {
            // A jet that is shut throws nothing; its lane drops off the table.
            var reach = pressure > 0f ? 2f * pressure / Spec.FrontReachPressure + Spec.DraftCarry * draft : 0f;
            var sum = 0f;
            for (var row = 0; row < rowWeights.Length; row++)
            {
                var offset = (row - reach) / Spec.ThrowSpread;
                rowWeights[row] = (float)Math.Exp(-0.5f * offset * offset);
                sum += rowWeights[row];
            }
            if (sum <= 1e-6f)
            {
                // Thrown far past the front: it all goes over the arch.
                return lane;
            }

            var past = rowWeights[GrateLayout.Rows] / sum;
            for (var row = 0; row < GrateLayout.Rows; row++)
            {
                var mass = lane * (rowWeights[row] / sum + (row == GrateLayout.Rows - 1 ? past * BankedPastFront : 0f));
                shares[(int)GrateLayout.At(row, column)] += mass * Spec.FrontLaneSideShare;
                shares[(int)GrateLayout.At(row, 1)] += mass * (1f - Spec.FrontLaneSideShare);
            }
            return lane * past * (1f - BankedPastFront);
        }

        private static float Clamp01(float value) => Smoothing.Clamp01(value);
    }
}
