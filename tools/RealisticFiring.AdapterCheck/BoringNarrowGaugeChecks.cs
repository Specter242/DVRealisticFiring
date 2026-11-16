using System;
using System.Linq;
using LocoSim.Definitions;
using LocoSim.Implementations;
using LocoSim.Resources;
using RealisticFiring.Core;
using RealisticFiring.Shed;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
 internal static class BoringNarrowGaugeChecks
    {
        private sealed class Engine
        {
            public string Livery;
            // The bundle's firebox: kg, s, efficiency at its most.
            public float Coal, BurnTime, Efficiency;
            // The pile's extent, m, as the bed measures it.
            public float PileWidth, PileLength;
            // The bundle's boiler and valve, absolute bar.
            public float Diameter, Length, Lift, InjectorRate;
            // The fitted valve, kg/s, and injector, kg/s.
            public float Valve, Injector;
        }

        private static readonly Engine[] Engines =
        {
            new Engine { Livery = "S260N-580A", Coal = 55f, BurnTime = 100f, Efficiency = .3f, PileWidth = .48f, PileLength = .91f,
                         Diameter = 1.18f, Length = 3.2f, Lift = 12f, InjectorRate = 5f, Valve = 1f, Injector = 1.5f },
            new Engine { Livery = "S040N-270", Coal = 40f, BurnTime = 80f, Efficiency = .4f, PileWidth = .35f, PileLength = .86f,
                         Diameter = .72f, Length = 2.45f, Lift = 10f, InjectorRate = 4f, Valve = .5f, Injector = .6f },
            new Engine { Livery = "S0440NG-890", Coal = 60f, BurnTime = 140f, Efficiency = .5f, PileWidth = .64f, PileLength = 1.2f,
                         Diameter = 1.18f, Length = 4.27f, Lift = 13.5f, InjectorRate = 12f, Valve = 1f, Injector = 2f },
            new Engine { Livery = "S440NCF-400A", Coal = 60f, BurnTime = 120f, Efficiency = .3f, PileWidth = .65f, PileLength = 1.18f,
                         Diameter = 1.02f, Length = 3.77f, Lift = 11.5f, InjectorRate = 6f, Valve = 1f, Injector = 1.8f },
        };

        internal static void Run(SimGameParams gameParams, Action<bool, string> check)
        {
            var liveries = Engines.Select(e => e.Livery).ToArray();
            check(liveries.All(FireboxRegistry.SupportedLiveries.Contains)
                  && new[] { "S260N-580B", "S040N-270 Aux Tender", "S440NCF-400B" }.All(l => !FireboxRegistry.SupportedLiveries.Contains(l))
                  && liveries.All(l => !LeftStock.Is(l, l, false)),
                "BorING's 2-6-0, 0-4-0, geared engine and cab-forward get the realistic firebox; the maker's tenders are not fired");

            // The cab-forward is fired from the front: its firing frame is the
            // car's turned half about, so a box ahead of its door in the car
            // (greater z) lies behind it (lesser z) there, and its left is the
            // fireman's right. Every other engine's is the car's own.
            var coal = new UnityEngine.Bounds();
            coal.SetMinMax(new UnityEngine.Vector3(-.33f, 1.11f, .17f), new UnityEngine.Vector3(.32f, 1.13f, 1.35f));
            var turned = FiringFrame.FromCar(true, coal);
            check(FiringFrame.Reversed("S440NCF-400A") && liveries.Where(l => l != "S440NCF-400A").All(l => !FiringFrame.Reversed(l))
                  && !FiringFrame.Reversed("LocoS282A") && !FiringFrame.Reversed((string)null)
                  && Math.Abs(turned.min.x + .32f) < 1e-5f && Math.Abs(turned.max.x - .33f) < 1e-5f
                  && Math.Abs(turned.min.z + 1.35f) < 1e-5f && Math.Abs(turned.max.z + .17f) < 1e-5f
                  && turned.min.y == coal.min.y && turned.max.y == coal.max.y
                  && FiringFrame.FromCar(false, coal) == coal
                  && FiringFrame.FromCar(true, new UnityEngine.Vector3(.2f, 1f, 1.4f)) == new UnityEngine.Vector3(-.2f, 1f, -1.4f),
                "BorING's cab-forward is fired from the front: its firing frame turns the car's half about, its door at the bed's back; no other engine's turns");
            // Its seated firehole is in that frame: its door at car z 1.41, facing forward.
            check(FireholeOpening.TryHole("S440NCF-400A", out var hole) && hole.Plane < 0f
                  && Math.Abs(FiringFrame.FromCar(true, new UnityEngine.Vector3(hole.Middle, hole.Foot, 1.408f)).z - hole.Plane) < 1e-4f,
                "BorING's cab-forward's firehole is seated in its firing frame, its door at car z 1.41");
            check(!ShedConnections.HasWaterConnection("S040N-270")
                  && ShedConnections.HasWaterConnection("S260N-580A") && ShedConnections.HasWaterConnection("S0440NG-890")
                  && !BoilerWaterRig.PlugMeltedRepairOnlyNotice.Contains("hose") && BoilerWaterRig.PlugMeltedNotice.Contains("hose"),
                "BorING's 0-4-0 has no shed water connection, and its melted plug names only the paid repair; the 2-6-0 and geared engine keep theirs");
            check(!ShedConnections.HasBlowerConnection("S040N-270") && !ShedConnections.HasBlowerConnection("S440NCF-400A")
                  && ShedConnections.HasBlowerConnection("S260N-580A") && ShedConnections.HasBlowerConnection("S0440NG-890")
                  && ShedConnections.HasWaterConnection("S440NCF-400A"),
                "BorING's 0-4-0 and cab-forward have no shed blower line; the 2-6-0 and geared engine keep theirs, and the cab-forward its water");

            foreach (var e in Engines)
            {
                // The maker's fire stands: no prototype's steam sizes it.
                check(!BoilerFit.TryBurnTime(e.Livery, e.Coal, e.Efficiency, oil: false, out _),
                    $"{e.Livery} keeps its maker's fire ({e.Coal:F0} kg, {e.BurnTime:F0} s, {e.Efficiency:F1} at the most)");

                // The maker's pile is the grate.
                var pile = e.PileWidth * e.PileLength;
                check(Math.Abs(FireboxCharacters.GrateAreaForLivery(e.Livery, pile) - pile) < 1e-5f
                      && FireboxCharacters.ForLivery(e.Livery).PrototypeSlope == null,
                    $"{e.Livery}'s grate is its maker's pile, {pile:F2} m², level");

                // The valve at the maker's lift passes a quarter more than the standing fire on the maker's grate, blower wide open.
                var standing = StandingHeat(e);
                var steam = standing / Latent(e.Lift);
                var fitted = SafetyValveFit.TryGetFit(e.Livery, out var valve) ? valve : (Lift: 0f, Capacity: 0f);
                check(fitted.Lift == e.Lift && fitted.Capacity == e.Valve && e.Valve >= 1.25f * steam && e.Valve - 0.5f < 1.25f * steam,
                    $"{e.Livery}'s valves lift at its maker's {e.Lift:F1} bar and pass {e.Valve:F1} kg/s, a quarter over its standing fire " +
                    $"({standing / 1e6f:F2} MW, {steam:F2} kg/s at the lift), rounded up to half a kg/s");

                CheckInjector(gameParams, check, e);

                check(EngineSteamPath.SaturatedPath(e.Livery) && !EngineSteamPath.TryFit(e.Livery, 30f, 0.65f, out _),
                    $"{e.Livery} keeps its native saturated throttle and chest");
            }
        }

        // The fire's heat standing, door shut, blower wide open, the box kept
        // 30 to 100 % full, averaged over its last fifteen minutes of
        // twenty-five, several rounds of firing, W.
        private static float StandingHeat(Engine e)
        {
            BlowerFit.TryGetDraft(e.Livery, out var fitted);
            var parameters = new FireboxParameters(e.Coal, e.BurnTime, e.Efficiency);
            // A tended box: its clinker cleaned.
            var fire = new FireModel(parameters, new FireTuning { ClinkerTendency = 0f })
            {
                GrateArea = e.PileWidth * e.PileLength,
                Character = FireboxCharacters.ForLivery(e.Livery).Grate,
            };
            fire.Restore(fireOn: true, coalMass: 0.6f * e.Coal);
            var shovel = new ShovelDelivery(parameters);
            var draft = new SteamDraft(fitted.MaxBlower, fitted.Natural, 1.65f, e.Lift);
            var zones = (GrateZone[])Enum.GetValues(typeof(GrateZone));
            var next = 0;
            float sum = 0f, n = 0f;
            for (var t = 0f; t < 1500f; t += 0.05f)
            {
                if (fire.CoalMass < 0.3f * e.Coal)
                    while (fire.CoalMass < 0.9f * e.Coal)
                        shovel.Deliver(fire, 3f, zones[next++ % zones.Length]);
                fire.Step(0.05f, new FireInputs
                {
                    AirFlow = draft.Air(0f, draft.BlowerSteam(e.Lift, 1f), 1f),
                    StartupMultiplier = 1f,
                    BoilerTemperature = 190f,
                    BoilerPressure = e.Lift,
                });
                if (t >= 600f)
                {
                    sum += fire.Heat;
                    n++;
                }
            }
            return sum / n;
        }

        // The game's latent heat at an absolute pressure, J/kg.
        private static float Latent(float pressure) =>
            1000f * (SteamTables.SteamSpecificEnthalpy(pressure) - SteamTables.WaterSpecificEnthalpy(pressure));

        // The cab's one lever: a lifting injector with its own steam valve,
        // rated at the lift, the lever its water valve.
        private static void CheckInjector(SimGameParams gameParams, Action<bool, string> check, Engine e)
        {
            var water = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var tenderWater = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 3000f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));
            var steamControl = (ExternalControl)new ExternalControlDefinition { ID = InjectorValves.Steam.Id }.InstantiateImplementation();
            var boiler = (Boiler)new BoilerDefinition
            {
                ID = "boiler", diameter = e.Diameter, length = e.Length, capacityMultiplier = .85f, maxInjectorRate = e.InjectorRate,
                waterConsumptionMultiplier = .5f, safetyValveOpeningPressure = e.Lift, safetyValveClosingPressure = e.Lift - .2f,
                // Two-thirds full of the maker's boiler.
                spawnPressure = e.Lift - 1f, spawnWaterLevel = 2f / 3f * .85f * 1000f * (float)Math.PI * e.Diameter * e.Diameter / 4f * e.Length,
            }.InstantiateImplementation();
            boiler.SetGameParams(gameParams);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.INJECTOR").SetPortReference(water);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER").SetPortReference(tenderWater);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER_CONSUMPTION").SetPortReference(consumed);
            var made = InjectorRig.TryCreate(null, boiler, e.Livery, new SimComponent[] { boiler, steamControl }, out var note);
            InjectorRig.TryGet(boiler, out var rig);
            var circuit = rig?.Circuits.Count == 1 ? rig.Circuits[0] : null;
            check(made && circuit != null && circuit.Added != null && ReferenceEquals(circuit.Control, water)
                  && circuit.Injector.Spec.Lifting && Math.Abs(circuit.Injector.Spec.MaxDelivery - e.Injector) < 1e-4f
                  && Math.Abs(circuit.Injector.Spec.RatedPressure - e.Lift) < 1e-4f,
                $"{e.Livery}: one lifting injector of {e.Injector:F1} kg/s at {e.Lift:F1} bar, its own steam valve, the cab's lever its water ({note})");
            if (circuit == null || circuit.Added == null)
                return;
            InjectorFlow Run(float steamAt, float waterAt, float seconds)
            {
                steamControl.controlExtIn.Value = steamAt;
                water.Value = waterAt;
                for (var t = 0f; t < seconds; t += 0.02f)
                {
                    consumed.Value = 0f;
                    rig.Simulate(0.02f);
                    rig.BoilerVessel.Update();
                }
                return circuit.Flow;
            }
            rig.EndResume();
            var still = Run(0f, 1f, 1f);
            Run(InjectorSpec.SteamPrime, 1f, 3f);
            var full = Run(1f, 1f, 2f);
            check(still.State == InjectorState.Off && still.Overflow == 0f && still.TenderDraw == 0f
                  && full.State == InjectorState.Working && full.WaterIn > 0f,
                $"{e.Livery}: no water rises until its steam lifts it; then it feeds ({full.WaterIn:F2} kg/s)");
        }
    }
}
