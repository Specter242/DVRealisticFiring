using System.Collections.Generic;
using System.Linq;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring
{
    /// <summary>
    /// Readings of the fires this mod runs, for other mods to show in their
    /// own way: what the fire overlay (F6) and the firing tablet show, as
    /// values rather than a drawn page. A caller finds this class by its name,
    /// <c>RealisticFiring.Api</c>, and calls it by reflection, so neither mod
    /// needs the other, and only base library types cross. The names, units,
    /// and meanings listed on <see cref="Read"/> stay as they are from version
    /// to version, while this mod's internals don't: a change to one raises
    /// <see cref="Version"/>; a reading added does not. The fireman's orders
    /// are <see cref="FiremanApi"/>'s.
    /// <para>
    /// Call it on Unity's main thread, a few times a second at most: each call
    /// reads the engine afresh and builds a new snapshot. On a multiplayer
    /// guest the readings are what the guest's own overlay shows: the grate,
    /// stoker, injectors, and fireman come from the host, while heat, draft,
    /// and smoke are the guest's own estimate from them.
    /// </para>
    /// </summary>
    public static class Api
    {
        /// <summary>Raised when a reading's name, unit, or meaning changes; one added doesn't raise it.</summary>
        public const int Version = 1;

        /// <summary>
        /// Whether this mod runs <paramref name="car"/>'s fire: a coal or oil
        /// engine it fires (the engine itself, not its tender). False for any
        /// other car and for null.
        /// </summary>
        public static bool Has(TrainCar car) => FireboxRegistry.TryGet(car, out _) || RealisticOilFirebox.TryGet(car, out _);

        /// <summary>
        /// The readings of <paramref name="car"/>'s fire, boiler, and engine
        /// now; null when <see cref="Has"/> is false. Each value is a float, a
        /// bool, a string, a <c>Dictionary&lt;string, object&gt;</c> of more,
        /// or a <c>List&lt;object&gt;</c> of such dictionaries. Every number
        /// is a float in the unit its name ends with: <c>Pct</c> percent
        /// (0 to 100 unless said), <c>Bar</c> gauge bar (0 is atmospheric),
        /// <c>C</c> degrees Celsius, <c>Kg</c> kilograms, <c>KgPerS</c>
        /// kilograms a second, <c>S</c> seconds, <c>Ppm</c> parts per million.
        /// A level is the overlay's mark:
        /// "Ok", "Watch" (wants doing soon), or "Act" (wants doing now). A
        /// section the engine lacks is left out; the keys listed in a section
        /// are always there unless said.
        /// <list type="bullet">
        /// <item><c>fuel</c>: "Coal" or "Oil".</item>
        /// <item><c>fire</c>: <c>lit</c>; <c>heatPct</c>, the heat against
        /// the most the firebox gives (the overlay's good band 40 to 90), and
        /// not capped: the game's startup speedup on a cold engine takes it
        /// well past 100, so clamp it for a bar;
        /// <c>level</c>; <c>draftPct</c>, the air through the fire against
        /// what it wants, 100 the right mix (coal: under it choked, over it
        /// thin; oil: rich and lean), 0 while out, and not capped: the
        /// overlay's dial stops at 250, but a dying fire reads far higher;
        /// <c>draftLevel</c>; <c>smokePct</c>, the smoke from the chimney,
        /// 0 clear to 100 black (coal: the unburned gas leaving the fire,
        /// grey by 40 and black by 90; oil: the stack's darkness, a light
        /// haze being right); coal only: <c>temperatureC</c>, the
        /// firebox's.</item>
        /// <item><c>grate</c>, coal only: <c>coalKg</c> in the box;
        /// <c>ashpanPct</c>, <c>ashpanFull</c> (wants emptying);
        /// <c>clinkerKg</c>, <c>needsCleaning</c>; <c>doorPct</c>, the fire
        /// door's opening, left out on an engine whose door this mod can't
        /// read; <c>thinZone</c>, the zone running short of coal, or ""
        /// when none is; <c>zoneReferenceKg</c>, a zone's working bed;
        /// <c>zones</c>, the 3 by 3 grate as seen from the cab, a list of
        /// nine from the back row at the door to the front under the tubes,
        /// each row left to right ("BackLeft", "UnderDoor", "BackRight",
        /// "MidLeft", "Center", "MidRight", "FrontLeft", "Front",
        /// "FrontRight"), each with <c>name</c>, <c>coalKg</c>,
        /// <c>burningPct</c> (its burning against the most a zone burns),
        /// and <c>clinkerKg</c>.</item>
        /// <item><c>coal</c>, coal only: <c>moisturePct</c>, the water in
        /// the coal fired, as a share of its mass; <c>condition</c>, "Dry",
        /// "Damp", or "Wet"; <c>coverOpen</c>, only on an engine whose coal
        /// has a cover (a bunker door).</item>
        /// <item><c>stoker</c>, with a mechanical stoker: <c>jammed</c>;
        /// <c>speedPct</c>, its engine's; <c>tableKg</c>, coal on its
        /// table; <c>tableLoaded</c>, coal piling there that wants raking;
        /// <c>jetsBar</c>, the jet manifold's pressure.</item>
        /// <item><c>burner</c>, oil only: <c>firingValvePct</c>;
        /// <c>stopPct</c>, the firing valve's stop, only while one is set;
        /// <c>feedKgPerS</c>, the oil burned; <c>atomizerPct</c>, the
        /// atomizer valve; <c>atomizingPct</c>, its steam against what the
        /// oil needs (under 100 too little); <c>oilC</c>;
        /// <c>heaterPct</c>, the oil heater's valve; <c>brickC</c>, the
        /// firebox brick's; <c>onShedSteam</c>, fed from a shed's steam
        /// line; <c>steamLinePct</c>, the cab's valve for it;
        /// <c>flueSootPct</c>; <c>lighting</c>, while the fire is out the
        /// steps to light it, in order, each with <c>title</c>,
        /// <c>how</c>, <c>done</c>, and <c>current</c>, and empty while it
        /// is lit.</item>
        /// <item><c>boiler</c>: <c>pressureBar</c>; <c>workingBar</c>,
        /// where its safety valves lift; <c>pressurePct</c>, the one
        /// against the other (good 85 to 100), and not capped: under 0 below
        /// atmospheric, over 100 past the valves; <c>pressureLevel</c>;
        /// <c>glassPct</c>, the water as the cab's gauge glass shows it,
        /// 0 to 100 of the glass (good 40 to 75), or the boiler's own level
        /// until a cab of that engine type has loaded this session;
        /// <c>glassLevel</c>. With
        /// this mod's boiler water (nearly always): <c>waterC</c>;
        /// <c>heating</c>, raising steam from cold, the water below
        /// boiling; <c>sootLossPct</c> and <c>scaleLossPct</c>, the heat
        /// the tubes lose to soot and the boiler to scale;
        /// <c>thermalShockWearPct</c>, accumulated tube/stay leak wear;
        /// <c>thermalShockLossPct</c>, heat lost from thermal-shock leaks;
        /// <c>warmingTooFast</c>, true while a cold boiler is being forced
        /// faster than safe; <c>tubeLeakKgPerS</c>, slow water/steam leak rate;
        /// <c>impuritiesPpm</c> and <c>foamPct</c>, only while the
        /// impurities setting is on, plus <c>glassChokePct</c> on engines with
        /// glass blow-through cocks; <c>plug</c> with <c>melted</c> and
        /// <c>coreC</c>, on an engine with a fusible plug;
        /// <c>lowWaterAlarm</c> with <c>sounding</c>, on one with an
        /// alarm.</item>
        /// <item><c>injectors</c>: this mod's injectors, empty when the
        /// engine's own work: each with <c>name</c> ("right" or "left"
        /// where it has more than one, else ""); <c>state</c>, "Off",
        /// "Priming", "Working", "Hot", "LowPressure", "NoWater", or
        /// "Broken" (the jet broken: shut it and start again);
        /// <c>waterKgPerS</c>, what it feeds; <c>tenderShut</c>, the
        /// tender's valve to it shut; and either <c>steamValve</c>
        /// ("Off", "Prime", or "Feed") and <c>waterValvePct</c>, or
        /// <c>leverPct</c> for one worked by a single lever.</item>
        /// <item><c>overflow</c>, with this mod's injectors:
        /// <c>waterKgPerS</c> and <c>steamKgPerS</c> wasted from their
        /// overflows.</item>
        /// <item><c>feedPump</c>, with a feed pump: <c>state</c>, "Off",
        /// "Feeding", "Choked" (drifting, half rate), "Racing",
        /// "LostSuction", or "Tripped"; <c>waterKgPerS</c>;
        /// <c>temperatureC</c>, the feed's.</item>
        /// <item><c>engine</c>, while this mod works the cylinders:
        /// <c>cutoffPct</c>; <c>steamChestBar</c>; <c>meanPressureBar</c>,
        /// the mean effective pressure; <c>steamKgPerS</c>, the steam the
        /// cylinders use; <c>condensingPct</c>, the share of it condensing
        /// in them; <c>superheated</c>, and while it is, <c>superheatC</c>,
        /// <c>steamC</c>, <c>superheaterElementC</c>,
        /// <c>superheaterHealthPct</c>, and <c>superheaterOverheating</c>.</item>
        /// <item><c>booster</c>, with a booster fitted: <c>state</c>,
        /// "CutOut", "Idling", "CocksOpen", or "Working"; <c>warmPct</c>.</item>
        /// <item><c>fireman</c>, with this mod's automatic fireman:
        /// <c>mode</c>, "Off", "KeepAlive", "Shunting", or "Road";
        /// <c>call</c>, "None", "HeavyAhead", or "EasingOff";
        /// <c>callLeftS</c>, until the call lapses.</item>
        /// <item><c>advice</c>: the next step at the foot of the overlay,
        /// <c>level</c> and <c>text</c>.</item>
        /// <item><c>checklist</c>: the auto-selected live firing checklist for
        /// this locomotive: <c>id</c> ("CoalColdStart", "OilColdStart",
        /// "InjectorsAndWater", "CleaningClinkerAndAsh", "StokerSetup", or
        /// "DriftingAndLayover"), <c>name</c>, <c>shortName</c>,
        /// <c>autoSelected</c>, <c>allDone</c>, <c>completedPct</c> (0 to 100),
        /// and <c>steps</c>, a list of dictionaries each with <c>title</c>,
        /// <c>how</c>, <c>done</c>, and <c>current</c>.</item>
        /// </list>
        /// </summary>
        public static IDictionary<string, object> Read(TrainCar car)
        {
            if (FireboxRegistry.TryGet(car, out var coal))
                return ReadParts(coal, null, car);
            if (RealisticOilFirebox.TryGet(car, out var oil))
                return ReadParts(null, oil, car);
            return null;
        }

        private static Dictionary<string, object> ReadParts(RealisticFirebox coal, RealisticOilFirebox oil, TrainCar car)
        {
            // The car's own parts, not a coupled engine's.
            InjectorRig.TryGet(car, out var rig);
            BoilerWaterRig.TryGet(car, out var water);
            return ReadParts(coal, oil, rig, water, car);
        }

        /// <summary>The readings from the parts given: one fire, coal or oil, and what else the car has.</summary>
        internal static Dictionary<string, object> ReadParts(RealisticFirebox coal, RealisticOilFirebox oil, InjectorRig rig, BoilerWaterRig water, TrainCar car)
        {
            var readings = new Dictionary<string, object>();
            if (coal != null)
                ReadCoal(readings, coal, rig, water);
            else
                ReadOil(readings, oil, water);
            if (rig != null || water != null)
                readings["boiler"] = Boiler(rig, water);
            readings["injectors"] = Injectors(rig);
            if (rig != null)
            {
                readings["overflow"] = new Dictionary<string, object>
                {
                    ["waterKgPerS"] = rig.Circuits.Sum(c => c.Flow.Overflow),
                    ["steamKgPerS"] = rig.Circuits.Sum(c => c.Flow.OverflowSteam),
                };
                if (rig.Pump != null)
                    readings["feedPump"] = new Dictionary<string, object>
                    {
                        ["state"] = rig.PumpFlow.State.ToString(),
                        ["waterKgPerS"] = rig.PumpFlow.WaterIn,
                        ["temperatureC"] = rig.PumpFlow.FeedTemperature,
                    };
            }
            if (EngineCylinders.TryGet(car, out var engine, out var cylinders))
                readings["engine"] = Engine(engine, cylinders);
            if (RealisticBooster.TryGet(car, out var booster) && booster.Fitted)
                readings["booster"] = new Dictionary<string, object>
                {
                    ["state"] = booster.Flow.State.ToString(),
                    ["warmPct"] = booster.Model.Warmth * 100f,
                };
            if (FiremanRig.TryGet(car, out var fireman))
                readings["fireman"] = new Dictionary<string, object>
                {
                    ["mode"] = FiremanOrders.ModeOf(fireman).ToString(),
                    ["call"] = fireman.Fireman.Call.ToString(),
                    ["callLeftS"] = fireman.Fireman.CallLeft,
                };
            readings["checklist"] = ChecklistSection(ChecklistReader.EvaluateParts(coal, oil, rig, water, car));
            return readings;
        }

        private static Dictionary<string, object> ChecklistSection(Checklist checklist) => new Dictionary<string, object>
        {
            ["id"] = checklist.Id.ToString(),
            ["name"] = checklist.Name,
            ["shortName"] = checklist.ShortName,
            ["autoSelected"] = checklist.AutoSelected,
            ["allDone"] = checklist.AllDone,
            ["completedPct"] = checklist.Steps.Count > 0 ? checklist.CompletedCount * 100f / checklist.Steps.Count : 100f,
            ["steps"] = checklist.Steps.Select(step => (object)new Dictionary<string, object>
            {
                ["title"] = step.Title,
                ["how"] = step.How,
                ["done"] = step.Done,
                ["current"] = step.Current,
            }).ToList(),
        };

        private static void ReadCoal(Dictionary<string, object> readings, RealisticFirebox firebox, InjectorRig rig, BoilerWaterRig water)
        {
            var fire = firebox.Model;
            var heat = fire.Heat / Mathf.Max(1f, fire.Parameters.MaxHeatOutput);
            readings["fuel"] = "Coal";
            readings["fire"] = new Dictionary<string, object>
            {
                ["lit"] = fire.FireOn,
                ["heatPct"] = heat * 100f,
                ["level"] = FireOverlay.FireLevel(fire.FireOn, heat).ToString(),
                ["draftPct"] = fire.FireOn ? fire.AirRatio * 100f : 0f,
                ["draftLevel"] = (fire.FireOn ? FireOverlay.DraftLevel(fire.AirRatio) : AdviceLevel.Ok).ToString(),
                ["smokePct"] = fire.SmokeDensity * 100f,
                ["temperatureC"] = fire.Temperature,
            };

            // A zone's burning against the most the grate burns, shared out, as the overlay's grate map glows.
            var perZone = fire.Parameters.MaxCombustionRate / GrateLayout.ZoneCount;
            var thin = FiringAdvice.ThinZone(fire.Zones);
            var grate = new Dictionary<string, object>
            {
                ["coalKg"] = fire.CoalMass,
                ["ashpanPct"] = fire.AshpanFill * 100f,
                ["ashpanFull"] = fire.AshpanFill >= FiringAdvice.FullAshpan,
                ["clinkerKg"] = fire.ClinkerMass,
                ["needsCleaning"] = FiringAdvice.WantsCleaning(fire),
                ["thinZone"] = thin >= 0 ? ((GrateZone)thin).ToString() : "",
                ["zoneReferenceKg"] = fire.ReferenceZoneMass,
                ["zones"] = fire.Zones.Select((zone, i) => (object)new Dictionary<string, object>
                {
                    ["name"] = ((GrateZone)i).ToString(),
                    ["coalKg"] = zone.Coal,
                    ["burningPct"] = perZone > 0f ? Mathf.Clamp01(zone.CombustionRate / perZone) * 100f : 0f,
                    ["clinkerKg"] = zone.Clinker,
                }).ToList(),
            };
            if (firebox.HasDoorPort)
                grate["doorPct"] = firebox.DoorOpening * 100f;
            readings["grate"] = grate;

            var moisture = fire.FeedMoisture;
            var coal = new Dictionary<string, object>
            {
                ["moisturePct"] = moisture * 100f,
                ["condition"] = moisture >= FireOverlay.WetCoal ? "Wet" : moisture >= FireOverlay.DampCoal ? "Damp" : "Dry",
            };
            if (firebox.CoalCoverOpen is float cover)
                coal["coverOpen"] = cover >= FireOverlay.ShutCover;
            readings["coal"] = coal;

            if (firebox.LastStoker is StokerFlow stoker)
                readings["stoker"] = new Dictionary<string, object>
                {
                    ["jammed"] = stoker.Jammed,
                    ["speedPct"] = firebox.StokerEngineSpeed * 100f,
                    ["tableKg"] = firebox.StokerTableCoal,
                    ["tableLoaded"] = firebox.StokerTableLoaded,
                    ["jetsBar"] = stoker.ManifoldPressure,
                };
            readings["advice"] = Said(FireOverlay.CoalAdvice(firebox, rig, water));
        }

        private static void ReadOil(Dictionary<string, object> readings, RealisticOilFirebox oil, BoilerWaterRig water)
        {
            var fire = oil.Model;
            var heat = fire.Heat / Mathf.Max(1f, fire.Parameters.MaxHeatOutput);
            // The oil's air against its best mix, so 100 % is right, as on the overlay.
            var air = fire.ExcessAir / OilFire.BestExcessAir;
            readings["fuel"] = "Oil";
            readings["fire"] = new Dictionary<string, object>
            {
                ["lit"] = fire.FireOn,
                ["heatPct"] = heat * 100f,
                ["level"] = FireOverlay.FireLevel(fire.FireOn, heat).ToString(),
                ["draftPct"] = fire.FireOn ? air * 100f : 0f,
                ["draftLevel"] = (fire.FireOn ? FireOverlay.DraftLevel(air) : AdviceLevel.Ok).ToString(),
                ["smokePct"] = fire.StackSmoke * 100f,
            };
            var reading = FireController.ReadOil(oil);
            var burner = new Dictionary<string, object>
            {
                ["firingValvePct"] = (oil.Feed != null ? oil.Feed.FiringValve : 0f) * 100f,
                ["feedKgPerS"] = fire.FeedRate,
                ["atomizerPct"] = oil.AtomizerValve * 100f,
                ["atomizingPct"] = fire.Atomizing * 100f,
                ["oilC"] = fire.OilTemperature,
                ["heaterPct"] = oil.HeaterValve * 100f,
                ["brickC"] = fire.BrickTemperature,
                ["onShedSteam"] = fire.OnSteamLine,
                ["steamLinePct"] = oil.SteamLineOpening * 100f,
                ["flueSootPct"] = fire.FlueSoot * 100f,
                ["lighting"] = fire.FireOn
                    ? new List<object>()
                    : FiringAdvice.Steps(reading).Select(step => (object)new Dictionary<string, object>
                    {
                        ["title"] = step.Title,
                        ["how"] = step.How,
                        ["done"] = step.Done,
                        ["current"] = step.Current,
                    }).ToList(),
            };
            if (oil.Stop.In)
                burner["stopPct"] = oil.Stop.At * 100f;
            readings["burner"] = burner;
            readings["advice"] = Said(FireOverlay.OilAdvice(reading, water));
        }

        private static Dictionary<string, object> Boiler(InjectorRig rig, BoilerWaterRig water)
        {
            var gauge = FireOverlay.GaugePressure(rig, water);
            var working = FireOverlay.WorkingPressure(rig, water);
            var glass = FireOverlay.GlassShown(rig, water);
            var boiler = new Dictionary<string, object>
            {
                ["pressureBar"] = gauge,
                ["workingBar"] = working,
                ["pressurePct"] = gauge / working * 100f,
                ["pressureLevel"] = FireOverlay.BoilerLevel(gauge / working).ToString(),
                ["glassPct"] = glass * 100f,
                ["glassLevel"] = FireOverlay.GlassLevel(glass).ToString(),
            };
            if (water == null)
                return boiler;
            boiler["waterC"] = water.WaterTemperature;
            boiler["heating"] = water.BelowBoilingNow;
            boiler["sootLossPct"] = water.Fouling.SootLoss * 100f;
            boiler["scaleLossPct"] = BoilerFouling.ScaleLossOf(water.Fouling.ScaleMass, water.HeatingSurface) * 100f;
            var shockOn = water.ThermalShockActive;
            boiler["thermalShockWearPct"] = shockOn ? water.ThermalShock.Wear * 100f : 0f;
            boiler["thermalShockLossPct"] = water.ThermalShockEfficiencyLoss * 100f;
            boiler["warmingTooFast"] = shockOn && water.ThermalShock.WarmingTooFast;
            boiler["tubeLeakKgPerS"] = shockOn ? water.TubeLeakRate : 0f;
            if (water.ImpuritiesActive)
            {
                boiler["impuritiesPpm"] = water.FoamingConcentrationNow;
                boiler["foamPct"] = water.FoamNow * 100f;
                if (water.HasGlassCocks)
                    boiler["glassChokePct"] = water.GlassChoke.Choke * 100f;
            }
            if (water.Plug != null)
                boiler["plug"] = new Dictionary<string, object>
                {
                    ["melted"] = water.Plug.Melted,
                    ["coreC"] = water.Plug.CoreTemperature,
                };
            if (water.Alarm != null)
                boiler["lowWaterAlarm"] = new Dictionary<string, object> { ["sounding"] = water.Alarm.Sounding };
            return boiler;
        }

        private static List<object> Injectors(InjectorRig rig)
        {
            var injectors = new List<object>();
            if (rig == null)
                return injectors;
            foreach (var circuit in rig.Circuits)
            {
                var injector = new Dictionary<string, object>
                {
                    ["name"] = circuit.Name,
                    ["state"] = circuit.Flow.State.ToString(),
                    ["waterKgPerS"] = circuit.Flow.WaterIn,
                    ["tenderShut"] = circuit.TenderValveShut,
                };
                if (circuit.SteamValve != null)
                {
                    injector["steamValve"] = InjectorValves.Position(circuit.SteamValve.Value);
                    injector["waterValvePct"] = circuit.WaterValve.Value * 100f;
                }
                else
                    injector["leverPct"] = circuit.Control.Value * 100f;
                injectors.Add(injector);
            }
            return injectors;
        }

        private static Dictionary<string, object> Engine(ReciprocatingSteamEngine engine, EngineCylinders cylinders)
        {
            var cycle = cylinders.Cycle;
            var readings = new Dictionary<string, object>
            {
                ["cutoffPct"] = cycle.Events.Cutoff * 100f,
                ["steamChestBar"] = engine.steamChestPressureReadOut.Value - 1f,
                ["meanPressureBar"] = cycle.MeanEffectivePressure,
                ["steamKgPerS"] = engine.exhaustFlowReadOut.Value,
                ["condensingPct"] = cylinders.Admitted > 0f ? cylinders.Condensed / cylinders.Admitted * 100f : 0f,
            };
            var superheated = EngineSuperheater.TryGet(engine, out var superheater) && !superheater.Model.Spec.Saturated;
            readings["superheated"] = superheated;
            if (superheated)
            {
                var careOn = superheater.SuperheaterCareActive;
                readings["superheatC"] = superheater.Model.Superheat;
                readings["steamC"] = superheater.SteamTemperature(engine.intakePressure.Value);
                readings["superheaterElementC"] = superheater.Model.ElementTemperature;
                readings["superheaterHealthPct"] = careOn ? superheater.Model.Health * 100f : 100f;
                readings["superheaterOverheating"] = careOn && superheater.Model.Overheating;
            }
            return readings;
        }

        private static Dictionary<string, object> Said(Advice advice) => new Dictionary<string, object>
        {
            ["level"] = advice.Level.ToString(),
            ["text"] = advice.Text ?? "",
        };
    }
}
