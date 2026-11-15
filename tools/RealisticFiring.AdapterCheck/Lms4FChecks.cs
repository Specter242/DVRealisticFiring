using System;
using System.Linq;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;

namespace RealisticFiring.AdapterCheck
{
 internal static class Lms4FChecks
    {
        private static readonly string[] Liveries = { "em_fowler_4f_44422", "em_fowler_4f_4027" };

        // Its boiler as the bundle has it: 1.59 by 4.51 m, the valve lifting at
        // 13.3 bar and reseating at 12.0, the injectors 7 kg/s together, a
        // 30,000 L tender.
        private static BoilerDefinition Boiler() => new BoilerDefinition
        {
            ID = "boiler", diameter = 1.59f, length = 4.51f, capacityMultiplier = 0.85f,
            maxInjectorRate = 7f, waterConsumptionMultiplier = 2f,
            safetyValveOpeningPressure = 13.3f, safetyValveClosingPressure = 12f,
            spawnPressure = 12.5f, spawnWaterLevel = 6000f,
        };

        internal static void Run(SimGameParams gameParams, Action<bool, string> check)
        {
            check(Liveries.All(FireboxRegistry.SupportedLiveries.Contains)
                  && !FireboxRegistry.SupportedLiveries.Contains("em_lms_4f_tender_br")
                  && !FireboxRegistry.SupportedLiveries.Contains("em_lms_4f_tender_lms")
                  && Liveries.All(l => !LeftStock.Is(l, "em_fowler_4f", false)),
                "both LMS 4F liveries get the realistic firebox and are listed; its tenders are not fired");

            var burnTimes = Liveries.Select(l => BoilerFit.TryBurnTime(l, 150f, .5f, oil: false, out var t) ? t : 0f).ToList();
            var sized = new FireboxParameters(150f, burnTimes[0], .5f);
            const float asBuilt = 12000f * 0.45359237f / 3600f;
            var mostAtLift = sized.MaxHeatOutput / 1.95e6f;
            var valves = Liveries.Select(l => SafetyValveFit.TryGetFit(l, out var fit) ? fit : (Lift: 0f, Capacity: 0f)).ToList();
            check(burnTimes.All(t => t > 0f && t == burnTimes[0])
                  && Math.Abs(sized.MaxHeatOutput - asBuilt * AutoFiremanBase.SteamHeat) < 1e3f
                  && valves.All(v => v.Lift == 13.3f && v.Capacity == 2.5f) && mostAtLift < 2.5f,
                $"the LMS 4F's boiler is its as-built 12,000 lb/h ({sized.MaxHeatOutput / 1e6f:F2} MW, its burn time 180 → {sized.BurnTime:F0} s), " +
                $"its valves 2.5 kg/s at 13.3 bar, over its fire's most as steam at the lift ({mostAtLift:F2} kg/s)");

            // 21.1 sq ft, where the pile measures 1.43 by 2.18 m.
            const float grate = 21.1f * 0.09290304f;
            float Area(string id, float measured) => FireboxCharacters.GrateAreaForLivery(id, measured);
            var fire = new FireModel(sized) { GrateArea = Area(Liveries[0], 1.43f * 2.18f) };
            check(Liveries.All(l => Math.Abs(Area(l, 0) - grate) < 1e-5f && Math.Abs(Area(l, 1.43f * 2.18f) - grate) < 1e-5f)
                  && Math.Abs(fire.AshpanCapacity - 95f * grate) < .001f
                  && Math.Abs(fire.ForcedZoneRate * GrateLayout.ZoneCount - 600f * grate / 3600) < 1e-5f,
                $"the LMS 4F's ashpan and forced fire take its 21.1 sq ft grate ({grate:F2} m²), not its pile's 3.1 m² through the box's sides");
            check(Liveries.All(l => FireboxCharacters.ForLivery(l).PrototypeSlope == null),
                "the LMS 4F keeps its maker's level pile: its grate's slope is not found");

            foreach (var livery in Liveries)
                CheckInjectors(gameParams, check, livery);

            var levers = Liveries.Select(CabWaterRegulators.InPlaceFor).ToList();
            check(levers.All(l => l != null && l.Left.x == -l.Right.x && l.Left.z == l.Right.z && l.Left.x < -1f && l.Left.z < -3.6f
                                  && l.Reach >= 0.115f && l.Turn == 150f)
                  && Liveries.All(l => !CabWaterRegulators.CutFromMesh(l))
                  && CabWaterRegulators.CutFromMesh("em_lms_8f_loc_black") && CabWaterRegulators.InPlaceFor("em_lms_8f_loc_black") == null,
                "the LMS 4F's own injector water valves, at the cab's back corners, are worked in place, not added levers; the 8F's are still cut and turned");

            var steamPaths = Liveries.Select(l => EngineSteamPath.TryFit(l, 400, 3, out var spec) ? spec : null).ToList();
            check(steamPaths.All(s => s != null && s.Regulator == RegulatorLocation.BeforeSuperheater
                                      && s.PipeVolume == 130 && s.ChestVolume == 400 && s.MaxFlow == 3),
                "both LMS 4F liveries regulate at the dome before the superheater, 130 L of circuit beside their maker's 400 L chest and 3 kg/s");
        }

        // As the 8F's: the fireman's right first, the cab's own handles the
        // steam valves and the added regulators the water.
        private static void CheckInjectors(SimGameParams gameParams, Action<bool, string> check, string livery)
        {
            var setup = Program.Lms8FInjectorSetup(gameParams, boilerDefinition: Boiler(), tenderWater: 30000f);
            var hand = InjectorRig.FiremanControl(setup.Boiler, livery, setup.Components);
            var changed = Program.Lms8FInjectorSetup(gameParams, factorValue: 1f, boilerDefinition: Boiler(), tenderWater: 30000f);
            check(hand != null && hand.id == "injector2.EXT_IN"
                  && InjectorRig.FiremanControl(changed.Boiler, livery, changed.Components) == null,
                $"{livery}: the stock-injector fireman uses the verified right cab valve of its two-injector graph");

            var made = InjectorRig.TryCreate(null, setup.Boiler, livery, setup.Components, out var note);
            InjectorRig.TryGet(setup.Boiler, out var rig);
            check(made && rig != null && rig.Circuits.Count == 2
                  && rig.Circuits[0].Control.id == "injector2.EXT_IN" && rig.Circuits[1].Control.id == "injector1.EXT_IN"
                  && rig.Circuits.All(c => Math.Abs(c.Injector.Spec.MaxDelivery - 2.7f) < 1e-4f && !c.Injector.Spec.Lifting
                                          && Math.Abs(c.Injector.Spec.RatedPressure - 13.3f) < 1e-4f),
                $"{livery}: two non-lifting injectors of 2.7 kg/s each at its 13.3 bar, the fireman's right first ({note})");

            var fitted = Program.Lms8FInjectorSetup(gameParams, boilerDefinition: Boiler(), tenderWater: 30000f);
            var waterRight = new ExternalControl(new ExternalControlDefinition { ID = InjectorValves.Water.Id, saveState = true });
            var waterLeft = new ExternalControl(new ExternalControlDefinition { ID = InjectorValves.WaterLeft.Id, saveState = true });
            InjectorRig.TryCreate(null, fitted.Boiler, livery, fitted.Components.Concat(new SimComponent[] { waterRight, waterLeft }), out _);
            InjectorRig.TryGet(fitted.Boiler, out var swapped);
            var right = swapped?.Circuits[0];
            var steamLever = fitted.Components.OfType<ExternalControl>().Single(c => c.id == "injector2").controlExtIn;
            InjectorFlow Step(float seconds)
            {
                for (var t = 0f; t < seconds; t += 0.02f)
                    swapped.Simulate(0.02f);
                return right.Flow;
            }
            var opened = false;
            if (right != null && right.OwnLeverIsSteam && right.SteamValve == steamLever && right.WaterValve == waterRight.controlExtIn
                && swapped.Circuits[1].OwnLeverIsSteam && swapped.Circuits[1].WaterValve == waterLeft.controlExtIn)
            {
                swapped.EndResume();
                waterRight.controlExtIn.Value = 0.6f;
                var gravity = Step(1f);
                steamLever.Value = InjectorSpec.SteamPrime;
                Step(2f);
                steamLever.Value = 1f;
                var feeding = Step(3f);
                opened = gravity.State == InjectorState.Off && gravity.Overflow > 0f && feeding.State == InjectorState.Working && feeding.WaterIn > 0f;
            }
            check(opened, $"{livery}: its own handle is the steam valve and its water regulator the water: water runs at the overflow, then it primes and feeds at 12.5 bar");
        }
    }
}
