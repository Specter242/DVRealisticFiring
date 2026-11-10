using System;
using System.Linq;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    internal static class GwiInjectorChecks
    {
        internal static void Run(SimGameParams gameParams, Action<bool, string> check)
        {
            // GWI 2.0.0: five limited notches, so the first opening is 1 / (5 - 1).
            foreach (var livery in new[] { "GWI40xx", "GWI_Manor" })
            {
                var lever = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
                var boiler = NewBoiler(gameParams, lever);
                check(InjectorRig.TryCreate(null, boiler, livery, null, out _), livery + " retains its realistic injector");
                InjectorRig.TryGet(boiler, out var rig);
                var circuit = rig.Circuits.Single();
                rig.EndResume();
                var before = rig.BoilerVessel.mass;
                lever.Value = .25f;
                Run(rig, 3);
                check(rig.Flow.State == InjectorState.Working && rig.BoilerVessel.mass > before,
                    livery + " primes and feeds from its first 25% notch after the load allowance ends");
                lever.Value = 1;
                before = rig.BoilerVessel.mass;
                Run(rig, 2);
                check(rig.Flow.State == InjectorState.Working && rig.BoilerVessel.mass > before,
                    livery + " opens to full feed after priming");
                check(circuit.Control == lever && circuit.Added == null && circuit.Injector.Spec.MaxDelivery == 20
                      && circuit.Injector.Spec.RatedPressure == 15 && !circuit.Injector.Spec.Lifting,
                    livery + " keeps the maker's control, pressure, delivery rate and non-lifting arrangement");

                // A fresh start at the next notch still needs priming; closing resets a failed start.
                lever.Value = 0;
                Run(rig, 1);
                lever.Value = .5f;
                Run(rig, .2f);
                check(rig.Flow.State == InjectorState.Broken && rig.Flow.WaterIn == 0,
                    livery + " still rejects skipping its priming notch");
                lever.Value = 0;
                Run(rig, 1);
                lever.Value = .25f;
                Run(rig, 3);
                check(rig.Flow.State == InjectorState.Working && rig.Flow.WaterIn > 0,
                    livery + " can close and prime again after a failed start");

                var loaded = NewBoiler(gameParams, lever);
                InjectorRig.TryCreate(null, loaded, livery, null, out _);
                InjectorRig.TryGet(loaded, out var resumed);
                lever.Value = 1;
                Run(resumed, 1);
                check(resumed.Flow.State == InjectorState.Working && resumed.Flow.WaterIn > 0,
                    livery + " retains the saved-running control resume allowance");
            }

            // No broader allowance for other engines, unknown liveries, or a changed GWI graph.
            foreach (var livery in new[] { "LocoS282A", "LocoS060", "Jk3A", "S464-730", "FEF-3", "S484-840A",
                "em_lms_8f_loc_black", "em_lms_8f_loc_grey", "A.T.&S.F. 2901-A", "S2102-870", "unknown", null, "GWI_Small_Tender" })
            {
                var lever = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
                var boiler = NewBoiler(gameParams, lever);
                InjectorRig.TryCreate(null, boiler, livery, null, out _);
                InjectorRig.TryGet(boiler, out var rig);
                check(rig.Circuits.Single().Injector.Spec.CrackOpening == .2f,
                    (livery ?? "no livery") + " retains its existing priming threshold");
            }
            foreach (var livery in new[] { "GWI40xx", "GWI_Manor" })
            {
                var different = new Port("different", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
                var boiler = NewBoiler(gameParams, different);
                InjectorRig.TryCreate(null, boiler, livery, null, out _);
                InjectorRig.TryGet(boiler, out var rig);
                check(rig.Circuits.Single().Injector.Spec.CrackOpening == .2f,
                    livery + " does not apply the known-notch fit to an unrecognized control");
                var mechanical = new Port("pump", new PortDefinition(PortType.OUT, PortValueType.CONTROL, "OUT"));
                check(!InjectorRig.TryCreate(null, NewBoiler(gameParams, mechanical), livery, null, out _),
                    livery + " still leaves a mechanically driven injector stock");
            }
        }

        private static Boiler NewBoiler(SimGameParams gameParams, Port lever)
        {
            var boiler = (Boiler)new BoilerDefinition
            {
                ID = "boiler", diameter = 1.5f, length = 6, capacityMultiplier = .85f,
                maxInjectorRate = 20, waterConsumptionMultiplier = 4,
                safetyValveOpeningPressure = 15, safetyValveClosingPressure = 14.8f,
                spawnPressure = 12, spawnWaterLevel = 9000,
            }.InstantiateImplementation();
            boiler.SetGameParams(gameParams);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.INJECTOR").SetPortReference(lever);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER").SetPortReference(
                new Port("tenderWater", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 30000));
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER_CONSUMPTION").SetPortReference(
                new Port("tenderWater", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN")));
            return boiler;
        }

        private static void Run(InjectorRig rig, float seconds)
        {
            for (var t = 0f; t < seconds; t += .02f)
            {
                rig.Simulate(.02f);
                rig.BoilerVessel.Update();
            }
        }
    }
}
