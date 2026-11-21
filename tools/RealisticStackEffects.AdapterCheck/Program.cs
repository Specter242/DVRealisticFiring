using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using DV.Simulation.Controllers;
using DV.WeatherSystem;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticStackEffects.Api;
using RealisticStackEffects.Core;

namespace RealisticStackEffects.AdapterCheck
{
    /// <summary>
    /// Checks Realistic Stack Effects' reading of the game's own firebox and
    /// exhaust (<see cref="StockStack"/>) against the installed game's sim,
    /// and that its patches find their targets.
    /// </summary>
    internal static class Program
    {
        private const string Id = "firebox";

        private static int failures;

        private static int Main()
        {
            var gameParams = new SimGameParams(false, false, false, 1f, 1f);
            CheckPatchTargets();
            CheckWeatherFog();
            CheckFirebox(gameParams);
            CheckOilBurnerDetection(gameParams);
            CheckStockStackRebind(gameParams);
            CheckExhaust(gameParams);
            CheckContract();
            CheckMultiStackVents();
            CheckMatchSortingExclusions();
            CheckSettings();
            Console.WriteLine(failures == 0 ? "All stack effects adapter checks passed" : $"{failures} stack effects adapter checks failed");
            return failures == 0 ? 0 : 1;
        }

        // The stock smoke reader's methods the mod patches, each found once.
        private static void CheckPatchTargets()
        {
            foreach (var name in new[] { nameof(SteamSmokeParticlePortReader.Init), "UpdateParticles" })
            {
                var found = typeof(SteamSmokeParticlePortReader)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Count(m => m.Name == name);
                Check(found == 1, $"the stock smoke reader's {name} is there to patch ({found} found)");
            }
            Check(typeof(SteamSmokeParticlePortReader).GetField(nameof(SteamSmokeParticlePortReader.fireOnPortId)) != null,
                "the stock smoke reader names its firebox's FIRE_ON port");
            // The coal transfer every shovelful goes through, and what it reads.
            var transfer = typeof(DV.Simulation.Cars.FireboxSimController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == nameof(DV.Simulation.Cars.FireboxSimController.TransferCoal)).ToList();
            Check(transfer.Count == 1 && transfer[0].GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(float) })
                  && transfer[0].GetParameters()[0].Name == "coalMass"
                  && typeof(DV.Simulation.Cars.FireboxSimController).GetField("coalConsumptionMultiplier") != null,
                "the game's coal transfer, TransferCoal(float coalMass), is there for the shovelful's smoke-out");
            var after = typeof(StockStack).Assembly.GetType("RealisticStackEffects.SmokeReaderInitPatch")
                ?.GetCustomAttributes(typeof(HarmonyLib.HarmonyAfter), false).Cast<HarmonyLib.HarmonyAfter>()
                .SelectMany(a => a.info.after ?? new string[0]).ToList() ?? new System.Collections.Generic.List<string>();
            Check(after.Contains("RealisticFiring"),
                "the plume is set up after Realistic Firing's own patch, so an older Realistic Firing keeps its engines' smoke");
            var boundFireProp = typeof(ChimneyPlume).GetProperty("BoundFire", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(boundFireProp != null && boundFireProp.PropertyType == typeof(IStackFire),
                "ChimneyPlume exposes its bound fire so SmokeReaderInitPatch only registers StockStack once bound");
            var boundCarProp = typeof(ChimneyPlume).GetProperty("BoundCar", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(boundCarProp != null && boundCarProp.PropertyType == typeof(TrainCar),
                "ChimneyPlume exposes its bound car so SmokeReaderInitPatch clears a moved reader's previous car");
        }

        // The game's live weather fog reading: WeatherDriver's local height-fog
        // density (interpolated across low/high fog zones) and its mapping into
        // PlumeClimate.FogFromDensity.
        private static void CheckWeatherFog()
        {
            var getLocalFog = typeof(WeatherDriver).GetMethod(nameof(WeatherDriver.GetLocalFogDensity),
                BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            var chungusProp = typeof(WeatherDriver).GetProperty(nameof(WeatherDriver.CurrentChungusState),
                BindingFlags.Instance | BindingFlags.Public);
            var lowField = typeof(WeatherStateChungus).GetField(nameof(WeatherStateChungus.currentLow),
                BindingFlags.Instance | BindingFlags.Public);
            var highField = typeof(WeatherStateChungus).GetField(nameof(WeatherStateChungus.currentHigh),
                BindingFlags.Instance | BindingFlags.Public);
            var overallFogProp = typeof(WeatherSnapshot).GetProperty(nameof(WeatherSnapshot.OverallFogDensity),
                BindingFlags.Instance | BindingFlags.Public);
            Check(getLocalFog != null && getLocalFog.ReturnType == typeof(float)
                  && chungusProp != null && chungusProp.PropertyType == typeof(WeatherStateChungus)
                  && lowField != null && lowField.FieldType == typeof(WeatherSnapshot)
                  && highField != null && highField.FieldType == typeof(WeatherSnapshot)
                  && overallFogProp != null && overallFogProp.PropertyType == typeof(float),
                "WeatherDriver exposes GetLocalFogDensity() -> float and its low/high WeatherSnapshot fog density");

            var snapshot = new WeatherSnapshot { fogHeightDensity = 0.18f };
            var clearFog = PlumeClimate.FogFromDensity(snapshot.OverallFogDensity);
            snapshot.fogHeightDensity = 0.56f;
            var moderateFog = PlumeClimate.FogFromDensity(snapshot.OverallFogDensity);
            snapshot.fogHeightDensity = 1f;
            var denseFog = PlumeClimate.FogFromDensity(snapshot.OverallFogDensity);
            Check(clearFog == 0f && moderateFog > 0.3f && moderateFog < 0.6f && denseFog == 1f,
                $"WeatherSnapshot.OverallFogDensity maps clear ({clearFog:F2}), valley fog ({moderateFog:F2}), and dense fog ({denseFog:F2})");
        }

        // The stock firebox read through its ports: its heat and the most it
        // makes, its smoke when the draft falls short, and a clear stack
        // when air passes beyond the coal's need.
        private static void CheckFirebox(SimGameParams gameParams)
        {
            var def = new FireboxDefinition { ID = Id, maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f, startupMaxPressure = 13f };
            var firebox = new Firebox(def);
            firebox.SetGameParams(gameParams);
            var air = Wire(firebox, def.airFlow, PortValueType.MASS_RATE, 0.5f);
            Wire(firebox, def.boilerPressure, PortValueType.PRESSURE, 14f);
            Wire(firebox, def.boilerTemperature, PortValueType.TEMPERATURE, 190f);
            Wire(firebox, def.coalDumpControl, PortValueType.CONTROL, 0.5f);
            var stack = new StockStack(null, P(firebox, def.fireOnReadOut), def, port => P(firebox, port), firebox.airFlow);

            P(firebox, def.coalControlExtIn).Value += 60f;
            P(firebox, def.ignitionExtIn).ExternalValueUpdate(1f);
            Run(firebox, 60f);
            var starved = Read(stack, null);
            var oxygen = P(firebox, def.oxygenAvailabilityReadOut).Value;
            var most = (float)typeof(Firebox).GetField("maxHeatOutput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(firebox);
            Check(starved.FireOn && Math.Abs(starved.MostHeat - most) <= 1e-3f * most && starved.Heat == P(firebox, def.heatReadOut).Value,
                $"the stock firebox's heat {starved.Heat / 1e6f:F2} MW of the most it makes, {starved.MostHeat / 1e6f:F2} MW (the game's {most / 1e6f:F2})");
            var density = P(firebox, def.smokeDensityReadOut).Value;
            Check(oxygen < 0.9f && Math.Abs(starved.Soot - StockStack.SootShare * density) < 1e-4f && starved.Soot > 0.3f && starved.ClearStack == 0f,
                $"a fire short of air smokes a heavy grey: oxygen {oxygen:F2}, smoke density {density:F2}, soot {starved.Soot:F2}, clear stack {starved.ClearStack:F2}");

            air.Value = 20f;
            Run(firebox, 60f);
            var drawn = Read(stack, null);
            oxygen = P(firebox, def.oxygenAvailabilityReadOut).Value;
            Check(oxygen == 1f && drawn.Soot < 0.05f && drawn.ClearStack > 0.5f,
                $"a fire with air to spare runs clean and its stack clears: oxygen {oxygen:F2}, soot {drawn.Soot:F2}, clear stack {drawn.ClearStack:F2}");

            // A stock shovelful, delivered as the shovel sends it and told to
            // the stack as the coal transfer's patch tells it: its smoke-out
            // reaches the stack a moment later and clears in seconds.
            Read(stack, null);
            P(firebox, def.coalControlExtIn).Value += 36f;
            stack.Thrown(36f);
            float early = 0f, late = 0f;
            for (var t = 0f; t < 3f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                var smokeOut = Read(stack, null).FireboxSmokeOut;
                if (t < 0.1f)
                    early = smokeOut;
                late = smokeOut;
            }
            for (var t = 0f; t < 40f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                Read(stack, null);
            }
            var cleared = Read(stack, null).FireboxSmokeOut;
            Check(early < 0.1f * late && late > 20f && cleared < 1f,
                $"a stock shovelful smokes out at the stack a moment after it lands: {early:F1} firebox puffs a second at 0.1 s, {late:F0} at 3 s, {cleared:F1} after 40 s");

            // Two smoke readers on one car both take its shovelfuls.
            var shared = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var first = new StockStack(shared, P(firebox, def.fireOnReadOut), def, port => P(firebox, port), firebox.airFlow);
            var second = new StockStack(shared, P(firebox, def.fireOnReadOut), def, port => P(firebox, port), firebox.airFlow);
            foreach (var each in StockStack.On(shared))
                each.Thrown(3f);
            for (var t = 0f; t < 2f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                Read(first, null);
                Read(second, null);
            }
            Check(StockStack.On(shared).Count == 2 && Read(first, null).FireboxSmokeOut > 5f && Read(second, null).FireboxSmokeOut > 5f,
                "two smoke readers on one engine both smoke out its shovelful");

            // The stock fire's heat dies away over its combustion's smoothing time.
            P(firebox, def.extinguishExtIn).ExternalValueUpdate(1f);
            Run(firebox, 60f);
            var dead = Read(stack, null);
            Check(!dead.FireOn && dead.Heat < 0.05f * starved.Heat, $"a fire put out reads as out, its heat dying away ({dead.Heat / 1e6f:F3} MW)");

            // The start-up helper's way: a cold box filled, then lit before
            // the plume reads it again; the cold coal smokes nothing.
            var cold = new StockStack(null, P(firebox, def.fireOnReadOut), def, port => P(firebox, port), firebox.airFlow);
            Read(cold, null);
            P(firebox, def.coalControlExtIn).Value += 100f;
            cold.Thrown(100f);
            P(firebox, def.ignitionExtIn).ExternalValueUpdate(1f);
            firebox.Tick(0.02f);
            var lit = Read(cold, null);
            for (var t = 0f; t < 3f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                lit = Read(cold, null);
            }
            Check(lit.FireOn && lit.FireboxSmokeOut == 0f, "coal thrown on a dead fire smokes nothing once it is lit");
            P(firebox, def.extinguishExtIn).ExternalValueUpdate(1f);
            Run(firebox, 1f);

            var bare = Read(new StockStack(null, P(firebox, def.fireOnReadOut), null, null, null), null);
            Check(bare.MostHeat == 0f && bare.Soot == 0f && !bare.FireOn && bare.Cinders, "without a firebox's definition only whether the fire is on is read");
        }

        // Standalone CCL oil-burner detection: require positive CCL stoker
        // wiring to a verified FUEL port (matching OilBurnerFit.FeedsFuel).
        // Hand-fired coal locos, lubricating OIL containers, auxiliary FUEL
        // tanks without a burner connection, and dangling ports stay coal-fired.
        private static void CheckOilBurnerDetection(SimGameParams gameParams)
        {
            var fireboxDef = new FireboxDefinition { ID = Id, maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f, startupMaxPressure = 13f };
            var coalDef = new CoalContainerDefinition { ID = "coal", capacity = 4000f, defaultValue = 4000f };
            var lubeDef = new OilContainerDefinition { ID = "oil", capacity = 50f, defaultValue = 50f };
            var fuelDef = new FuelContainerDefinition { ID = "fuel", capacity = 6500f, defaultValue = 6500f };
            var fuelInDef = new PortForwardFuelInDefinition { ID = "fuelIn" };
            var fuelOutDef = new PortForwardFuelOutDefinition { ID = "fuelOut" };
            var oilInDef = new PortForwardOilInDefinition { ID = "oilIn" };
            var tenderFuelPorts = new ConfigurablePortsDefinition
            {
                ID = "tenderCoal",
                ports = new[] { new PortDefinition(PortType.READONLY_OUT, PortValueType.FUEL, "AMOUNT") },
            };
            var lubePorts = new ConfigurablePortsDefinition
            {
                ID = "lubePorts",
                ports = new[] { new PortDefinition(PortType.READONLY_OUT, PortValueType.OIL, "AMOUNT") },
            };

            // Coal tank loco with lubricating oil container -> NOT oil-fired.
            var coalTankGraph = new SimConnectionDefinition
            {
                executionOrder = new SimComponentDefinition[] { fireboxDef, coalDef, lubeDef },
                portReferenceConnections = Array.Empty<PortReferenceConnection>(),
            };
            Check(!StockStack.IsOilFired(coalTankGraph, null),
                "a coal tank locomotive with an OIL lubricator container is not classified as oil-fired");

            // Coal tender loco (no on-car coal container, only OIL lubricator) -> NOT oil-fired.
            var coalTenderGraph = new SimConnectionDefinition
            {
                executionOrder = new SimComponentDefinition[] { fireboxDef, lubeDef },
                portReferenceConnections = Array.Empty<PortReferenceConnection>(),
            };
            Check(!StockStack.IsOilFired(coalTenderGraph, null),
                "a coal tender locomotive with an OIL lubricator container and no on-car coal container is not classified as oil-fired");

            // Hand-fired tender-coal loco with an auxiliary FUEL tank (no burner/stoker connection) -> NOT oil-fired.
            var tenderCoalWithAuxFuelGraph = new SimConnectionDefinition
            {
                executionOrder = new SimComponentDefinition[] { fireboxDef, fuelDef, lubeDef },
                portReferenceConnections = Array.Empty<PortReferenceConnection>(),
            };
            Check(!StockStack.IsOilFired(tenderCoalWithAuxFuelGraph, null),
                "a hand-fired tender-coal locomotive with an unconnected auxiliary FUEL tank stays coal-fired");

            // Unconnected forwarded FUEL ports without a burner/stoker connection -> NOT oil-fired.
            var unconnectedFuelPortsGraph = new SimConnectionDefinition
            {
                executionOrder = new SimComponentDefinition[] { fireboxDef, tenderFuelPorts, lubeDef },
                portReferenceConnections = Array.Empty<PortReferenceConnection>(),
            };
            Check(!StockStack.IsOilFired(unconnectedFuelPortsGraph, null),
                "an engine with unconnected forwarded FUEL ports and no stoker stays coal-fired");

            // Coal loco with both coal and an auxiliary fuel container (not feeding firebox) -> NOT oil-fired.
            var coalWithAuxFuelGraph = new SimConnectionDefinition
            {
                executionOrder = new SimComponentDefinition[] { fireboxDef, coalDef, fuelDef, lubeDef },
                portReferenceConnections = Array.Empty<PortReferenceConnection>(),
            };
            Check(!StockStack.IsOilFired(coalWithAuxFuelGraph, null),
                "a coal locomotive that also carries a separate fuel container stays coal-fired");

            // Direct CCL stoker checks when CCL is installed.
            var dv = Environment.GetEnvironmentVariable("DV_INSTALL_DIR")
                     ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local/share/Steam/steamapps/common/Derail Valley");
            var importer = Path.Combine(dv, "Mods", "DVCustomCarLoader", "CCL.Importer.dll");
            if (File.Exists(importer))
            {
                var stokerType = Assembly.LoadFrom(importer).GetType(StockStack.CclStokerTypeName);
                Check(stokerType != null, $"CCL mechanical stoker type '{StockStack.CclStokerTypeName}' is found in CCL.Importer.dll");
                if (stokerType != null)
                {
                    var stokerDef = (SimComponentDefinition)Activator.CreateInstance(stokerType);
                    stokerDef.ID = "autostoker";

                    var coalStokerPorts = new ConfigurablePortsDefinition
                    {
                        ID = "tenderCoal",
                        ports = new[] { new PortDefinition(PortType.READONLY_OUT, PortValueType.COAL, "AMOUNT") },
                    };
                    var coalStokerGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, coalStokerPorts, lubeDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "tenderCoal.AMOUNT") },
                    };
                    Check(!StockStack.IsOilFired(coalStokerGraph, null),
                        "a CCL stoker locomotive fed from COAL ports with an OIL lubricator stays coal-fired");

                    // Lubricating OIL must never be treated as fuel oil, even if wired to a stoker.
                    var lubeContainerStokerGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, lubeDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "oil.AMOUNT") },
                    };
                    var lubeForwardStokerGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, oilInDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "oilIn.SIM_OUT") },
                    };
                    var lubePortsStokerGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, lubePorts },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "lubePorts.AMOUNT") },
                    };
                    Check(!StockStack.IsOilFired(lubeContainerStokerGraph, null)
                          && !StockStack.IsOilFired(lubeForwardStokerGraph, null)
                          && !StockStack.IsOilFired(lubePortsStokerGraph, null),
                        "a CCL stoker pointing at lubricating OIL (OilContainer, PortForwardOil, or OIL port) is not classified as oil-fired");

                    // Dangling port references must return false instead of blindly matching the target component's class.
                    var danglingFuelGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, fuelDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "fuel.MISSING") },
                    };
                    var danglingLubeGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, lubeDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "oil.MISSING") },
                    };
                    var danglingForwardGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, fuelInDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "fuelIn.MISSING") },
                    };
                    var danglingConfigGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, tenderFuelPorts },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "tenderCoal.MISSING") },
                    };
                    Check(!StockStack.IsOilFired(danglingFuelGraph, null)
                          && !StockStack.IsOilFired(danglingLubeGraph, null)
                          && !StockStack.IsOilFired(danglingForwardGraph, null)
                          && !StockStack.IsOilFired(danglingConfigGraph, null),
                        "a CCL stoker pointing at a dangling port (fuel.MISSING, oil.MISSING, fuelIn.MISSING, tenderCoal.MISSING) stays coal-fired");

                    // Positive CCL oil-burner cases: FuelContainerDefinition, PortForwardFuel*, ConfigurablePortsDefinition (FUEL).
                    var fuelContainerStokerGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, fuelDef, lubeDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "fuel.AMOUNT") },
                    };
                    Check(StockStack.IsOilFired(fuelContainerStokerGraph, null),
                        "a CCL stoker wired to FuelContainerDefinition.AMOUNT is classified as oil-fired");

                    var fuelForwardInGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, fuelInDef, lubeDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "fuelIn.SIM_OUT") },
                    };
                    var fuelForwardOutGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, fuelOutDef, lubeDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "fuelOut.FORWARD_OUT") },
                    };
                    Check(StockStack.IsOilFired(fuelForwardInGraph, null) && StockStack.IsOilFired(fuelForwardOutGraph, null),
                        "a CCL stoker wired to PortForwardFuelIn/Out valid ports is classified as oil-fired");

                    var oilStokerGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef, tenderFuelPorts, lubeDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "tenderCoal.AMOUNT") },
                    };
                    Check(StockStack.IsOilFired(oilStokerGraph, null),
                        "a CCL stoker locomotive fed from ConfigurablePortsDefinition FUEL ports (Santa Fe 2901 pattern) is classified as oil-fired");

                    // SimulationFlow port lookup: FUEL accepted, OIL rejected, missing rejected.
                    var flow = MakeFlowWithPorts(
                        new Port("custom", new PortDefinition(PortType.READONLY_OUT, PortValueType.FUEL, "FUEL_OUT"), 100f),
                        new Port("custom", new PortDefinition(PortType.READONLY_OUT, PortValueType.OIL, "OIL_OUT"), 50f));
                    var flowFuelGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "custom.FUEL_OUT") },
                    };
                    var flowOilGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "custom.OIL_OUT") },
                    };
                    var flowMissingGraph = new SimConnectionDefinition
                    {
                        executionOrder = new SimComponentDefinition[] { fireboxDef, stokerDef },
                        portReferenceConnections = new[] { new PortReferenceConnection("autostoker.COAL_AMOUNT", "custom.MISSING") },
                    };
                    Check(StockStack.IsOilFired(flowFuelGraph, flow)
                          && !StockStack.IsOilFired(flowOilGraph, flow)
                          && !StockStack.IsOilFired(flowMissingGraph, flow),
                        "SimulationFlow lookup accepts FUEL ports, rejects lubricating OIL ports, and rejects missing ports");
                }
            }

            // Runtime StockStack cinder flag & shovelful suppression for oil-fired vs coal-fired.
            var firebox = new Firebox(fireboxDef);
            firebox.SetGameParams(gameParams);
            Wire(firebox, fireboxDef.airFlow, PortValueType.MASS_RATE, 4f);
            Wire(firebox, fireboxDef.boilerPressure, PortValueType.PRESSURE, 14f);
            Wire(firebox, fireboxDef.boilerTemperature, PortValueType.TEMPERATURE, 190f);
            Wire(firebox, fireboxDef.coalDumpControl, PortValueType.CONTROL, 0.5f);
            P(firebox, fireboxDef.coalControlExtIn).Value += 60f;
            P(firebox, fireboxDef.ignitionExtIn).ExternalValueUpdate(1f);
            Run(firebox, 2f);

            var coalStack = new StockStack(null, P(firebox, fireboxDef.fireOnReadOut), fireboxDef, port => P(firebox, port), firebox.airFlow, oilFired: false);
            var oilStack = new StockStack(null, P(firebox, fireboxDef.fireOnReadOut), fireboxDef, port => P(firebox, port), firebox.airFlow, oilFired: true);
            oilStack.Thrown(36f);
            for (var t = 0f; t < 2f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                Read(oilStack, null);
            }
            var coalReading = Read(coalStack, null);
            var oilReading = Read(oilStack, null);
            Check(coalReading.Cinders && !oilReading.Cinders && oilReading.FireboxSmokeOut == 0f,
                "standalone StockStack keeps Cinders = true on coal locos and sets Cinders = false (no shovelful smoke-out) on oil-fired locos");
        }

        private static SimulationFlow MakeFlowWithPorts(params Port[] ports)
        {
            var flow = (SimulationFlow)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SimulationFlow));
            typeof(SimulationFlow).GetField(nameof(SimulationFlow.AllPorts), BindingFlags.Instance | BindingFlags.Public).SetValue(flow, ports.ToList());
            var map = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Port>(ports.ToDictionary(p => p.id));
            typeof(SimulationFlow).GetField("fullPortIdToPort", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(flow, map);
            return flow;
        }

        // Rebinding the same smoke reader replaces its StockStack instead of
        // duplicating it; two distinct readers on one car stay separate;
        // switching a reader to an external fire provider drops its stale one;
        // and a failed rebind or exception before binding leaves an active
        // plume's existing StockStack registered so shovelfuls still reach it.
        private static void CheckStockStackRebind(SimGameParams gameParams)
        {
            var def = new FireboxDefinition { ID = Id, maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f, startupMaxPressure = 13f };
            var firebox = new Firebox(def);
            firebox.SetGameParams(gameParams);
            Wire(firebox, def.airFlow, PortValueType.MASS_RATE, 20f);
            Wire(firebox, def.boilerPressure, PortValueType.PRESSURE, 14f);
            Wire(firebox, def.boilerTemperature, PortValueType.TEMPERATURE, 190f);
            Wire(firebox, def.coalDumpControl, PortValueType.CONTROL, 0.5f);
            P(firebox, def.coalControlExtIn).Value += 60f;
            P(firebox, def.ignitionExtIn).ExternalValueUpdate(1f);
            Run(firebox, 30f);

            var fireOn = P(firebox, def.fireOnReadOut);
            StockStack Make(TrainCar c, SteamSmokeParticlePortReader r, bool register = true) =>
                new StockStack(c, fireOn, def, port => P(firebox, port), firebox.airFlow, r, register);
            void ThrowAndStep(TrainCar c, params StockStack[] stacks)
            {
                P(firebox, def.coalControlExtIn).Value = 100f;
                foreach (var each in StockStack.On(c))
                    each.Thrown(3f);
                for (var t = 0f; t < 2f; t += 0.02f)
                {
                    firebox.Tick(0.02f);
                    foreach (var s in stacks)
                        Read(s, null);
                }
            }

            // 1. Re-initializing the same (car, reader) replaces the previous StockStack.
            var car = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var readerA = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var oldStack = Make(car, readerA);
            var newStack = Make(car, readerA);
            ThrowAndStep(car, oldStack, newStack);
            var afterRebind = StockStack.On(car);
            Check(afterRebind.Count == 1 && ReferenceEquals(afterRebind[0], newStack)
                  && Read(oldStack, null).FireboxSmokeOut == 0f && Read(newStack, null).FireboxSmokeOut > 5f,
                "re-initializing the same (car, reader) replaces its StockStack; the old one stops taking shovelfuls");

            // 2. Two distinct readers on the same car both stay registered and both take shovelfuls.
            var twinCar = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var frontReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var rearReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var frontOld = Make(twinCar, frontReader);
            var rear = Make(twinCar, rearReader);
            var frontNew = Make(twinCar, frontReader);
            ThrowAndStep(twinCar, frontOld, frontNew, rear);
            var twinStacks = StockStack.On(twinCar);
            Check(twinStacks.Count == 2 && twinStacks.Contains(frontNew) && twinStacks.Contains(rear)
                  && Read(frontOld, null).FireboxSmokeOut == 0f && Read(frontNew, null).FireboxSmokeOut > 5f && Read(rear, null).FireboxSmokeOut > 5f,
                "two distinct smoke readers on one car stay registered across rebinds and both take shovelfuls");

            // 3. Switching a (car, reader) to an external fire provider removes any stale StockStack for that reader.
            var switchedCar = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var switchedA = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var switchedB = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var plumeA = new PlumeHarness();
            var plumeB = new PlumeHarness();
            var externalActive = false;
            var externalThrows = false;
            StackSources.Register("RebindCheck", c => ReferenceEquals(c, switchedCar) && externalActive
                ? new SwitchableSource(() => externalThrows)
                : null);

            SmokeReaderInitPatch.AttachReader(switchedCar, switchedA,
                (out string h) => { h = "stock"; return Make(switchedCar, switchedA, register: false); },
                fire => plumeA.Bind(switchedCar, fire), plumeA.TryGetActiveBinding, out _);
            SmokeReaderInitPatch.AttachReader(switchedCar, switchedB,
                (out string h) => { h = "stock"; return Make(switchedCar, switchedB, register: false); },
                fire => plumeB.Bind(switchedCar, fire), plumeB.TryGetActiveBinding, out _);
            var stockA = (StockStack)plumeA.BoundFire;
            var stockB = (StockStack)plumeB.BoundFire;
            var beforeSwitchCount = StockStack.On(switchedCar).Count;

            externalActive = true;
            SmokeReaderInitPatch.AttachReader(switchedCar, switchedA,
                (out string h) => { h = "stock"; return Make(switchedCar, switchedA, register: false); },
                fire => plumeA.Bind(switchedCar, fire), plumeA.TryGetActiveBinding, out var externalHow);
            var externalA = plumeA.BoundFire;
            ThrowAndStep(switchedCar, stockA, stockB);
            var afterSwitch = StockStack.On(switchedCar);
            Check(beforeSwitchCount == 2 && externalHow == "the fire from RebindCheck"
                  && afterSwitch.Count == 1 && ReferenceEquals(afterSwitch[0], stockB)
                  && Read(stockA, null).FireboxSmokeOut == 0f && Read(stockB, null).FireboxSmokeOut > 5f,
                "switching a (car, reader) to an external fire provider removes its stale StockStack without touching another reader");

            // If the external fire fails and falls back to stock, a later healthy rebind clears the fallback StockStack too.
            externalThrows = true;
            var fallbackReading = default(StackReading);
            externalA.Read(0.02f, null, ref fallbackReading);
            var afterFallbackCount = StockStack.On(switchedCar).Count;
            externalThrows = false;
            SmokeReaderInitPatch.AttachReader(switchedCar, switchedA,
                (out string h) => { h = "stock"; return Make(switchedCar, switchedA, register: false); },
                fire => plumeA.Bind(switchedCar, fire), plumeA.TryGetActiveBinding, out _);
            var afterSecondSwitchCount = StockStack.On(switchedCar).Count;
            SmokeReaderInitPatch.AttachReader(switchedCar, switchedB,
                (out string h) => { h = "stock"; return Make(switchedCar, switchedB, register: false); },
                fire => plumeB.Bind(switchedCar, fire), plumeB.TryGetActiveBinding, out _);
            Check(afterFallbackCount == 2 && afterSecondSwitchCount == 1 && StockStack.On(switchedCar).Count == 0,
                "a fallback StockStack is registered on external failure and removed when an external provider rebinds");
            externalActive = false;

            // 4. Active stock plume -> failed stock rebind (e.g. missing exhaust-pressure port):
            // the original StockStack stays registered and receives the next shovelful.
            var failedCar = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var failedReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var failedPlume = new PlumeHarness();

            SmokeReaderInitPatch.AttachReader(failedCar, failedReader,
                (out string h) => { h = "stock"; return Make(failedCar, failedReader, register: false); },
                fire => failedPlume.Bind(failedCar, fire), failedPlume.TryGetActiveBinding, out _);
            var originalStack = (StockStack)failedPlume.BoundFire;

            StockStack discardedCandidate = null;
            var missingPortResult = SmokeReaderInitPatch.AttachReader(failedCar, failedReader,
                (out string h) => { h = "stock"; discardedCandidate = Make(failedCar, failedReader, register: false); return discardedCandidate; },
                _ => "a smoke input port is missing; keeping the stock smoke",
                failedPlume.TryGetActiveBinding, out _);
            ThrowAndStep(failedCar, originalStack, discardedCandidate);
            var afterFailedStockRebind = StockStack.On(failedCar);
            Check(missingPortResult.Contains("missing") && afterFailedStockRebind.Count == 1
                  && ReferenceEquals(afterFailedStockRebind[0], originalStack)
                  && ReferenceEquals(failedPlume.BoundFire, originalStack)
                  && Read(originalStack, null).FireboxSmokeOut > 5f && Read(discardedCandidate, null).FireboxSmokeOut == 0f,
                "a failed stock rebind leaves the active plume's original StockStack registered and taking shovelfuls");

            // 5. Active stock plume -> failed external-provider takeover (missing exhaust-pressure port):
            // the original StockStack stays registered and receives the next shovelful.
            var failedExtCar = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var failedExtReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var failedExtPlume = new PlumeHarness();
            var failedExtActive = false;
            StackSources.Register("FailedExternalTakeoverCheck", c => ReferenceEquals(c, failedExtCar) && failedExtActive
                ? new SwitchableSource(() => false)
                : null);
            SmokeReaderInitPatch.AttachReader(failedExtCar, failedExtReader,
                (out string h) => { h = "stock"; return Make(failedExtCar, failedExtReader, register: false); },
                fire => failedExtPlume.Bind(failedExtCar, fire), failedExtPlume.TryGetActiveBinding, out _);
            var originalExtStack = (StockStack)failedExtPlume.BoundFire;

            failedExtActive = true;
            SmokeReaderInitPatch.AttachReader(failedExtCar, failedExtReader,
                (out string h) => { h = "stock"; return Make(failedExtCar, failedExtReader, register: false); },
                _ => "a smoke input port is missing; keeping the stock smoke",
                failedExtPlume.TryGetActiveBinding, out var failedExternalHow);
            ThrowAndStep(failedExtCar, originalExtStack);
            var afterFailedExternalTakeover = StockStack.On(failedExtCar);
            Check(failedExternalHow == "the fire from FailedExternalTakeoverCheck" && afterFailedExternalTakeover.Count == 1
                  && ReferenceEquals(afterFailedExternalTakeover[0], originalExtStack)
                  && ReferenceEquals(failedExtPlume.BoundFire, originalExtStack)
                  && Read(originalExtStack, null).FireboxSmokeOut > 5f,
                "a failed external-provider takeover leaves the active plume's original StockStack registered and taking shovelfuls");
            failedExtActive = false;

            // 6. Exception during rebind: before binding preserves the old registration;
            // after binding registers the newly bound fire; on a fresh attach registers nothing.
            var preThrowCar = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var preThrowReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var preThrowPlume = new PlumeHarness();
            var preThrowExternal = false;
            StackSources.Register("PreBindThrowCheck", c => ReferenceEquals(c, preThrowCar) && preThrowExternal
                ? new SwitchableSource(() => false)
                : null);
            SmokeReaderInitPatch.AttachReader(preThrowCar, preThrowReader,
                (out string h) => { h = "stock"; return Make(preThrowCar, preThrowReader, register: false); },
                fire => preThrowPlume.Bind(preThrowCar, fire), preThrowPlume.TryGetActiveBinding, out _);
            var preThrowOriginal = (StockStack)preThrowPlume.BoundFire;

            preThrowExternal = true;
            var threwBeforeBindExternal = false;
            try
            {
                SmokeReaderInitPatch.AttachReader(preThrowCar, preThrowReader,
                    (out string h) => { h = "stock"; return Make(preThrowCar, preThrowReader, register: false); },
                    _ => throw new InvalidOperationException("pre-bind attach failure"),
                    preThrowPlume.TryGetActiveBinding, out _);
            }
            catch (InvalidOperationException)
            {
                threwBeforeBindExternal = true;
            }
            preThrowExternal = false;
            var threwBeforeBindStock = false;
            StockStack thrownCandidate = null;
            try
            {
                SmokeReaderInitPatch.AttachReader(preThrowCar, preThrowReader,
                    (out string h) => { h = "stock"; thrownCandidate = Make(preThrowCar, preThrowReader, register: false); return thrownCandidate; },
                    _ => throw new InvalidOperationException("pre-bind stock failure"),
                    preThrowPlume.TryGetActiveBinding, out _);
            }
            catch (InvalidOperationException)
            {
                threwBeforeBindStock = true;
            }
            ThrowAndStep(preThrowCar, preThrowOriginal, thrownCandidate);
            var afterPreBindThrows = StockStack.On(preThrowCar);
            Check(threwBeforeBindExternal && threwBeforeBindStock && afterPreBindThrows.Count == 1
                  && ReferenceEquals(afterPreBindThrows[0], preThrowOriginal)
                  && Read(preThrowOriginal, null).FireboxSmokeOut > 5f && Read(thrownCandidate, null).FireboxSmokeOut == 0f,
                "an exception before binding leaves the active plume's original StockStack registered and taking shovelfuls");

            var postThrowCar = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var postThrowReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var postThrowPlume = new PlumeHarness();
            SmokeReaderInitPatch.AttachReader(postThrowCar, postThrowReader,
                (out string h) => { h = "stock"; return Make(postThrowCar, postThrowReader, register: false); },
                fire => postThrowPlume.Bind(postThrowCar, fire), postThrowPlume.TryGetActiveBinding, out _);
            var postThrowOriginal = (StockStack)postThrowPlume.BoundFire;
            var threwAfterBind = false;
            StockStack reboundBeforeThrow = null;
            try
            {
                SmokeReaderInitPatch.AttachReader(postThrowCar, postThrowReader,
                    (out string h) => { h = "stock"; reboundBeforeThrow = Make(postThrowCar, postThrowReader, register: false); return reboundBeforeThrow; },
                    fire => postThrowPlume.BindThenThrow(postThrowCar, fire),
                    postThrowPlume.TryGetActiveBinding, out _);
            }
            catch (InvalidOperationException)
            {
                threwAfterBind = true;
            }
            ThrowAndStep(postThrowCar, postThrowOriginal, reboundBeforeThrow);
            var afterPostBindThrow = StockStack.On(postThrowCar);
            Check(threwAfterBind && afterPostBindThrow.Count == 1
                  && ReferenceEquals(afterPostBindThrow[0], reboundBeforeThrow)
                  && ReferenceEquals(postThrowPlume.BoundFire, reboundBeforeThrow)
                  && Read(postThrowOriginal, null).FireboxSmokeOut == 0f && Read(reboundBeforeThrow, null).FireboxSmokeOut > 5f,
                "an exception after binding updates StockStack registration to match the active plume's bound fire");

            // 7. Fresh attach failure (exception or another assembly's plume) and
            // incomplete plume retirement leave no registered StockStack.
            var rejectedCar = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var rejectedReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var rejectedPlume = new PlumeHarness();
            SmokeReaderInitPatch.AttachReader(rejectedCar, rejectedReader,
                (out string h) => { h = "stock"; return Make(rejectedCar, rejectedReader, register: false); },
                _ => "RealisticFiring's plume already drives this stack",
                rejectedPlume.TryGetActiveBinding, out _);
            var countAfterOtherPlume = StockStack.On(rejectedCar).Count;
            var freshThrew = false;
            try
            {
                SmokeReaderInitPatch.AttachReader(rejectedCar, rejectedReader,
                    (out string h) => { h = "stock"; return Make(rejectedCar, rejectedReader, register: false); },
                    _ => throw new InvalidOperationException("fresh attach failure"),
                    rejectedPlume.TryGetActiveBinding, out _);
            }
            catch (InvalidOperationException)
            {
                freshThrew = true;
            }
            var countAfterFreshThrow = StockStack.On(rejectedCar).Count;
            SmokeReaderInitPatch.AttachReader(failedCar, failedReader,
                (out string h) => { h = "stock"; return Make(failedCar, failedReader, register: false); },
                _ => failedPlume.RetireIncomplete(),
                failedPlume.TryGetActiveBinding, out _);
            Check(countAfterOtherPlume == 0 && freshThrew && countAfterFreshThrow == 0 && StockStack.On(failedCar).Count == 0,
                "another assembly's plume, a failed fresh attach, and retiring an incomplete plume leave no StockStack registered");

            // 8. Cross-car rebind (car A -> car B):
            // a failed rebind preserves car A and leaves car B empty; a successful rebind
            // (stock -> stock, stock -> external, or lazy fallback -> stock) removes car A's
            // entry without disturbing other readers on car A and updates car B; retiring an
            // incomplete plume on car B also clears car A.
            var moveCarA = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var moveCarB = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var moveReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var bystanderReader = (SteamSmokeParticlePortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamSmokeParticlePortReader));
            var movePlume = new PlumeHarness();
            var bystanderPlume = new PlumeHarness();
            var crossExternalOnA = false;
            var crossExternalAThrows = false;
            StackSources.Register("CrossCarRebindCheck", c =>
                ReferenceEquals(c, moveCarA) && crossExternalOnA
                    ? new SwitchableSource(() => crossExternalAThrows)
                    : null);

            SmokeReaderInitPatch.AttachReader(moveCarA, moveReader,
                (out string h) => { h = "stock"; return Make(moveCarA, moveReader, register: false); },
                fire => movePlume.Bind(moveCarA, fire), movePlume.TryGetActiveBinding, out _);
            SmokeReaderInitPatch.AttachReader(moveCarA, bystanderReader,
                (out string h) => { h = "stock"; return Make(moveCarA, bystanderReader, register: false); },
                fire => bystanderPlume.Bind(moveCarA, fire), bystanderPlume.TryGetActiveBinding, out _);
            var moveStockA = (StockStack)movePlume.BoundFire;
            var bystanderStockA = (StockStack)bystanderPlume.BoundFire;

            // 8a. Failed rebind from car A to car B (missing port + pre-bind exception) preserves car A and leaves car B empty.
            StockStack failedMoveCandidate = null;
            SmokeReaderInitPatch.AttachReader(moveCarB, moveReader,
                (out string h) => { h = "stock"; failedMoveCandidate = Make(moveCarB, moveReader, register: false); return failedMoveCandidate; },
                _ => "a smoke input port is missing; keeping the stock smoke",
                movePlume.TryGetActiveBinding, out _);
            var moveThrewBeforeBind = false;
            try
            {
                SmokeReaderInitPatch.AttachReader(moveCarB, moveReader,
                    (out string h) => { h = "stock"; return Make(moveCarB, moveReader, register: false); },
                    _ => throw new InvalidOperationException("cross-car pre-bind failure"),
                    movePlume.TryGetActiveBinding, out _);
            }
            catch (InvalidOperationException)
            {
                moveThrewBeforeBind = true;
            }
            ThrowAndStep(moveCarA, moveStockA, bystanderStockA, failedMoveCandidate);
            var carAAfterFailedMove = StockStack.On(moveCarA);
            Check(moveThrewBeforeBind && carAAfterFailedMove.Count == 2 && carAAfterFailedMove.Contains(moveStockA) && carAAfterFailedMove.Contains(bystanderStockA)
                  && StockStack.On(moveCarB).Count == 0
                  && ReferenceEquals(movePlume.BoundCar, moveCarA) && ReferenceEquals(movePlume.BoundFire, moveStockA)
                  && Read(moveStockA, null).FireboxSmokeOut > 5f && Read(failedMoveCandidate, null).FireboxSmokeOut == 0f,
                "a failed cross-car rebind preserves car A's StockStack and leaves car B empty");

            // 8b. Successful stock rebind from car A to car B removes car A's entry for moveReader (keeping bystander) and registers on car B.
            SmokeReaderInitPatch.AttachReader(moveCarB, moveReader,
                (out string h) => { h = "stock"; return Make(moveCarB, moveReader, register: false); },
                fire => movePlume.Bind(moveCarB, fire), movePlume.TryGetActiveBinding, out _);
            var moveStockB = (StockStack)movePlume.BoundFire;
            // Let any residual smoke-out from 8a clear on moveStockA before throwing on car A and car B.
            for (var t = 0f; t < 40f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                Read(moveStockA, null);
                Read(bystanderStockA, null);
            }
            ThrowAndStep(moveCarA, moveStockA, bystanderStockA);
            var moveStockASmokeAfterMove = Read(moveStockA, null).FireboxSmokeOut;
            var bystanderSmokeAfterMove = Read(bystanderStockA, null).FireboxSmokeOut;
            ThrowAndStep(moveCarB, moveStockB);
            var carAAfterMove = StockStack.On(moveCarA);
            var carBAfterMove = StockStack.On(moveCarB);
            Check(carAAfterMove.Count == 1 && ReferenceEquals(carAAfterMove[0], bystanderStockA)
                  && carBAfterMove.Count == 1 && ReferenceEquals(carBAfterMove[0], moveStockB)
                  && ReferenceEquals(movePlume.BoundCar, moveCarB)
                  && moveStockASmokeAfterMove < 1f && bystanderSmokeAfterMove > 5f && Read(moveStockB, null).FireboxSmokeOut > 5f,
                "a successful cross-car stock rebind removes car A's StockStack without touching another reader and registers on car B");

            // 8c. Active stock plume on car B -> successful external takeover onto car A (and vice versa): old car's stock entry is cleared.
            crossExternalOnA = true;
            SmokeReaderInitPatch.AttachReader(moveCarA, moveReader,
                (out string h) => { h = "stock"; return Make(moveCarA, moveReader, register: false); },
                fire => movePlume.Bind(moveCarA, fire), movePlume.TryGetActiveBinding, out var crossExternalAHow);
            var crossExternalAFire = movePlume.BoundFire;
            Check(crossExternalAHow == "the fire from CrossCarRebindCheck"
                  && StockStack.On(moveCarB).Count == 0
                  && StockStack.On(moveCarA).Count == 1 && ReferenceEquals(StockStack.On(moveCarA)[0], bystanderStockA),
                "a successful cross-car external takeover clears the previous car's StockStack and registers none on the new car");

            // 8d. External lazy fallback on car A -> failed rebind to car B preserves fallback on car A ->
            // successful rebind to car B (stock) clears fallback from car A and registers on car B.
            crossExternalAThrows = true;
            var crossFallbackReading = default(StackReading);
            crossExternalAFire.Read(0.02f, null, ref crossFallbackReading);
            var carACountWithFallback = StockStack.On(moveCarA).Count;
            var fallbackOnA = StockStack.On(moveCarA).FirstOrDefault(s => !ReferenceEquals(s, bystanderStockA));
            SmokeReaderInitPatch.AttachReader(moveCarB, moveReader,
                (out string h) => { h = "stock"; return Make(moveCarB, moveReader, register: false); },
                _ => "a smoke input port is missing; keeping the stock smoke",
                movePlume.TryGetActiveBinding, out _);
            var carACountAfterFailedFallbackMove = StockStack.On(moveCarA).Count;
            var carBCountAfterFailedFallbackMove = StockStack.On(moveCarB).Count;
            SmokeReaderInitPatch.AttachReader(moveCarB, moveReader,
                (out string h) => { h = "stock"; return Make(moveCarB, moveReader, register: false); },
                fire => movePlume.Bind(moveCarB, fire), movePlume.TryGetActiveBinding, out _);
            var reboundFromFallbackOnB = (StockStack)movePlume.BoundFire;
            ThrowAndStep(moveCarA, fallbackOnA, bystanderStockA);
            var fallbackSmokeAfterMove = Read(fallbackOnA, null).FireboxSmokeOut;
            ThrowAndStep(moveCarB, reboundFromFallbackOnB);
            Check(carACountWithFallback == 2 && fallbackOnA != null
                  && carACountAfterFailedFallbackMove == 2 && carBCountAfterFailedFallbackMove == 0
                  && StockStack.On(moveCarA).Count == 1 && ReferenceEquals(StockStack.On(moveCarA)[0], bystanderStockA)
                  && StockStack.On(moveCarB).Count == 1 && ReferenceEquals(StockStack.On(moveCarB)[0], reboundFromFallbackOnB)
                  && fallbackSmokeAfterMove == 0f && Read(reboundFromFallbackOnB, null).FireboxSmokeOut > 5f,
                "a cross-car rebind clears a lazy external fallback StockStack on car A and registers the new stack on car B");

            // 8e. Retiring an incomplete plume during a cross-car attach cleans up the previous car too.
            SmokeReaderInitPatch.AttachReader(moveCarA, moveReader,
                (out string h) => { h = "stock"; return Make(moveCarA, moveReader, register: false); },
                _ => movePlume.RetireIncomplete(), movePlume.TryGetActiveBinding, out _);
            Check(StockStack.On(moveCarB).Count == 0
                  && StockStack.On(moveCarA).Count == 1 && ReferenceEquals(StockStack.On(moveCarA)[0], bystanderStockA),
                "retiring an incomplete plume on another car clears the previous car's StockStack");
            crossExternalOnA = false;
            crossExternalAThrows = false;
        }

        private sealed class PlumeHarness
        {
            private bool active;
            private IStackFire boundFire;
            private TrainCar boundCar;

            public IStackFire BoundFire => active ? boundFire : null;
            public TrainCar BoundCar => active ? boundCar : null;

            public bool TryGetActiveBinding(out IStackFire fire, out TrainCar car)
            {
                car = boundCar;
                if (active)
                {
                    fire = boundFire;
                    return true;
                }
                fire = null;
                return false;
            }

            public string Bind(TrainCar car, IStackFire fire)
            {
                boundCar = car;
                boundFire = fire;
                active = true;
                return "realistic chimney smoke rebound to the existing plume";
            }

            public string BindThenThrow(TrainCar car, IStackFire fire)
            {
                Bind(car, fire);
                throw new InvalidOperationException("post-bind attach failure");
            }

            public string RetireIncomplete()
            {
                active = false;
                boundFire = null;
                boundCar = null;
                return "incomplete plume retired; keeping the stock smoke until the next initialization";
            }
        }

        private sealed class SwitchableSource : IStackFireSource
        {
            private readonly Func<bool> shouldThrow;

            public SwitchableSource(Func<bool> shouldThrow) => this.shouldThrow = shouldThrow;

            public bool Tended => true;

            public void Read(float dt, SteamExhaust exhaust, ref StackFireReading reading)
            {
                if (shouldThrow())
                    throw new InvalidOperationException("switched external fire failed");
                reading.FireOn = true;
            }
        }

        // The stock exhaust's steam up the stack: the blower's as the game
        // works it out, and the natural draft taken off a cold engine's flow.
        private static void CheckExhaust(SimGameParams gameParams)
        {
            var def = new SteamExhaustDefinition { ID = "exhaust", passiveExhaust = 0.2f, entrainmentRatio = 1.95f, maxBlowerFlow = 0.2f, pressureForMaxBlowerFlow = 3f };
            var exhaust = (SteamExhaust)def.InstantiateImplementation();
            exhaust.SetGameParams(gameParams);
            var steam = Wire(exhaust, def.exhaustFlow, PortValueType.MASS_RATE, 0f);
            Wire(exhaust, def.engineMaxFlowReader, PortValueType.MASS_RATE, 2f);
            Wire(exhaust, def.boilerPressure, PortValueType.PRESSURE, 13.4f);
            var blower = Wire(exhaust, def.blowerControl, PortValueType.CONTROL, 0f);
            Wire(exhaust, def.whistleControl, PortValueType.CONTROL, 0f);
            Wire(exhaust, def.damperControl, PortValueType.CONTROL, 1f);
            exhaust.InitializationAfterConnecting();
            var stack = new StockStack(null, null, null, null, null);
            var total = P(exhaust, def.totalFlowNormalizedReadOut);

            exhaust.Tick(0.02f);
            var cold = Read(stack, exhaust);
            Check(total.Value > 0f && Math.Abs(total.Value - cold.NaturalFlow) < 1e-5f && cold.StackSteam == 0f,
                $"a cold engine's natural draft is all of the stock flow ({total.Value:F3}, natural {cold.NaturalFlow:F3}): no stream");

            blower.Value = 0.5f;
            steam.Value = 1.2f;
            exhaust.Tick(0.02f);
            var working = Read(stack, exhaust);
            var used = P(exhaust, def.steamConsumptionReadOut).Value;
            var drawn = P(exhaust, def.airFlowReadOut).Value;
            // The steam meets only the air its blast draws, not the passive draft.
            var blast = def.entrainmentRatio * working.StackSteam;
            Check(Math.Abs(working.BlowerSteam - used) < 1e-5f && Math.Abs(working.StackSteam - (1.2f + used)) < 1e-5f
                  && Math.Abs(working.AirThrough - blast) < 1e-4f && Math.Abs(drawn - def.passiveExhaust - blast) < 1e-4f,
                $"the blower's steam {working.BlowerSteam:F3} kg/s as the game's ({used:F3}), {working.StackSteam:F2} kg/s up the stack, " +
                $"{working.AirThrough:F2} kg/s of the {drawn:F2} kg/s of air with it (the blast's)");
        }

        // Another mod's fire through the public contract: every field of the
        // plume's reading has its own in the contract's, and crosses intact;
        // a fire that throws hands its stack to the game's firebox.
        private static void CheckContract()
        {
            var inner = typeof(StackReading).GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(f => f.Name, f => f.FieldType);
            var outer = typeof(StackFireReading).GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(f => f.Name, f => f.FieldType);
            var unmatched = inner.Where(f => !outer.TryGetValue(f.Key, out var type) || type != f.Value).Select(f => f.Key)
                .Concat(outer.Keys.Where(name => !inner.ContainsKey(name))).ToList();
            Check(unmatched.Count == 0 && StackSources.ApiVersion == 3 && StackSources.EngineSteam(null) == null
                  && typeof(IEngineSteam).GetMethod("Add") != null
                  && typeof(StackGeometry).GetField(nameof(StackGeometry.LipHeight))?.FieldType == typeof(float?)
                  && typeof(StackGeometry).GetField(nameof(StackGeometry.BoreRadius))?.FieldType == typeof(float?)
                  && typeof(StackGeometry).GetField(nameof(StackGeometry.OutsideRadius))?.FieldType == typeof(float?)
                  && typeof(StackSources).GetMethod(nameof(StackSources.RegisterGeometry)) != null,
                $"the contract's reading matches the plume's, {inner.Count} fields{(unmatched.Count > 0 ? $" (unmatched: {string.Join(", ", unmatched)})" : "")}, version {StackSources.ApiVersion} (with StackGeometry)");

            // API v3 geometry provider validation, sanitization, and fault isolation.
            var rejectedBadReg = false;
            try { StackSources.RegisterGeometry("", _ => null); }
            catch (ArgumentException) { rejectedBadReg = true; }
            var rejectedNullFn = false;
            try { StackSources.RegisterGeometry("NullGeom", null); }
            catch (ArgumentException) { rejectedNullFn = true; }
            Check(rejectedBadReg && rejectedNullFn, "RegisterGeometry rejects an empty mod ID or null provider");

            var throwingCalls = 0;
            StackSources.RegisterGeometry("ThrowingGeom", _ =>
            {
                throwingCalls++;
                throw new InvalidOperationException("broken geometry provider");
            });
            StackSources.RegisterGeometry("InvalidGeom", _ => new StackGeometry
            {
                LipHeight = -1f,
                BoreRadius = float.NaN,
                OutsideRadius = float.PositiveInfinity,
            });
            StackSources.RegisterGeometry("GoodGeom", _ => new StackGeometry
            {
                LipHeight = 9f,
                BoreRadius = -0.2f,
                OutsideRadius = 0.24f,
            });

            var firstGeom = StackSources.GeometryFor(null, out var firstBy);
            var secondGeom = StackSources.GeometryFor(null, out var secondBy);
            Check(throwingCalls == 1 && firstBy == "GoodGeom" && secondBy == "GoodGeom"
                  && firstGeom.HasValue && firstGeom.Value.LipHeight == StackGeometryHint.MaxLipHeight
                  && !firstGeom.Value.BoreRadius.HasValue && firstGeom.Value.OutsideRadius == 0.24f
                  && secondGeom.HasValue && secondGeom.Value.LipHeight == StackGeometryHint.MaxLipHeight,
                $"a throwing geometry provider is isolated after one call ({throwingCalls}), all-invalid hints are skipped, and valid hints are sanitized ({firstBy})");

            StackSources.Register("Check", car => new Marked());
            var fire = StackSources.For(null, () => new StockStack(null, null, null, null, null), out var by);
            var reading = default(StackReading);
            fire.Read(0.02f, null, ref reading);
            var wrong = typeof(StackReading).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(f => !Equals(f.GetValue(reading), Marked.Value(f.Name, f.FieldType))).Select(f => f.Name).ToList();
            Check(by == "Check" && wrong.Count == 0 && fire.Tended,
                $"another mod's fire crosses the contract intact{(wrong.Count > 0 ? $" (lost: {string.Join(", ", wrong)})" : "")}");

            Marked.Throw = true;
            reading = default(StackReading);
            fire.Read(0.02f, null, ref reading);
            Check(!reading.FireOn && reading.Soot == 0f && float.IsNaN(reading.ExhaustWetness) && reading.Cinders,
                "a fire that throws hands its stack to the game's firebox");
        }

        private static void CheckMultiStackVents()
        {
            var plumeType = typeof(ChimneyPlume);
            var onCar = plumeType.GetField("OnCar", BindingFlags.Static | BindingFlags.NonPublic);
            var expectedTable = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, Core.CarPlumes<ChimneyPlume>>);
            Check(onCar != null && onCar.FieldType == expectedTable,
                "ChimneyPlume tracks every stack on a car in a per-car CarPlumes<ChimneyPlume> registry");

            var attachVents = HarmonyLib.AccessTools.Method(plumeType, "AttachVents");
            var release = HarmonyLib.AccessTools.Method(plumeType, "Release");
            var updatePlume = HarmonyLib.AccessTools.Method(plumeType, "UpdatePlume");
            var tryGet = HarmonyLib.AccessTools.Method(plumeType, nameof(ChimneyPlume.TryGet));
            var named = HarmonyLib.AccessTools.Method(plumeType, "Named");
            var attachVentsCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(attachVents)
                .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList();
            var canOwnIndex = attachVentsCalls.FindIndex(m => m.Name == nameof(Core.CarPlumes<ChimneyPlume>.CanOwnVents));
            var createIndex = attachVentsCalls.FindIndex(m => m.DeclaringType == typeof(SteamVents) && m.Name == nameof(SteamVents.Create));
            Check(canOwnIndex >= 0 && createIndex > canOwnIndex,
                "AttachVents checks CarPlumes.CanOwnVents before creating SteamVents");

            var releaseCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(release)
                .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList();
            var ventsReleaseIndex = releaseCalls.FindIndex(m => m.DeclaringType == typeof(SteamVents) && m.Name == nameof(SteamVents.Release));
            var rosterRemoveIndex = releaseCalls.FindIndex(m => m.DeclaringType == typeof(Core.CarPlumes<ChimneyPlume>)
                && m.Name == nameof(Core.CarPlumes<ChimneyPlume>.Remove));
            Check(ventsReleaseIndex >= 0 && rosterRemoveIndex > ventsReleaseIndex,
                "Release restores stock renderers via SteamVents.Release before marking vents handoff in CarPlumes.Remove");

            var updateCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(updatePlume)
                .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList();
            Check(updateCalls.Any(m => m.Name == nameof(Core.CarPlumes<ChimneyPlume>.TryConsumeVentsHandoff))
                  && updateCalls.Any(m => m.Name == nameof(Core.CarPlumes<ChimneyPlume>.TryConsumeSortingHandoff)),
                "UpdatePlume promotes a surviving stack via both TryConsumeVentsHandoff and TryConsumeSortingHandoff");

            var matchSorting = HarmonyLib.AccessTools.Method(plumeType, "MatchSorting");
            var matchSortingCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(matchSorting)
                .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList();
            Check(matchSortingCalls.Any(m => m.Name == nameof(Core.CarPlumes<ChimneyPlume>.CanOwnSorting))
                  && matchSortingCalls.Any(m => m.Name == nameof(Core.CarPlumes.CollectStackRenderers)),
                "MatchSorting guards sorting ownership via CanOwnSorting and builds exclusions via CarPlumes.CollectStackRenderers");

            var ventsFor = typeof(PlumeCapture).GetMethod("VentsFor", BindingFlags.Static | BindingFlags.NonPublic);
            var ventsForCalls = ventsFor != null
                ? HarmonyLib.PatchProcessor.GetOriginalInstructions(ventsFor)
                    .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList()
                : new System.Collections.Generic.List<MethodInfo>();
            Check(ventsForCalls.Any(m => m.DeclaringType == plumeType && m.Name == nameof(ChimneyPlume.TryGet)),
                "PlumeCapture resolves the car's SteamVents owner via ChimneyPlume.TryGet when framing a secondary stack");

            var tryGetCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(tryGet)
                .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList();
            var namedCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(named)
                .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList();
            var maxTopCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(HarmonyLib.AccessTools.Method(plumeType, nameof(ChimneyPlume.TryGetMaxPlumeTop)))
                .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList();
            Check(tryGetCalls.Any(m => m.Name == nameof(Core.CarPlumes<ChimneyPlume>.Primary))
                  && namedCalls.Any(m => m.Name == nameof(Core.CarPlumes.SelectPrimary))
                  && maxTopCalls.Any(m => m.Name == nameof(Core.CarPlumes<ChimneyPlume>.TryGetMax)),
                "TryGet, Named, and TryGetMaxPlumeTop select the car's primary vents-owning stack and tallest plume through CarPlumes");

            // Exercise CarPlumes<T> with real SteamVents shells for multi-stack ownership,
            // primary selection, max PlumeTop, and release/handoff fallback.
            var vents1 = (SteamVents)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamVents));
            var vents2 = (SteamVents)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SteamVents));
            var front = new StackSlot { Active = true, Vents = vents1, PlumeTop = 7f };
            var rear = new StackSlot { Active = true, Vents = null, PlumeTop = 18f };
            var roster = new Core.CarPlumes<StackSlot>();
            roster.Register(front);
            roster.Register(rear);
            bool Active(StackSlot s) => s != null && s.Active;
            bool HasVents(StackSlot s) => s != null && s.Vents != null;

            Check(roster.Count == 2 && !roster.CanOwnVents(rear, Active, HasVents)
                  && ReferenceEquals(roster.Primary(Active, HasVents), front)
                  && roster.TryGetMax(Active, s => s.PlumeTop, out var tallest) && Math.Abs(tallest - 18f) < 1e-4f,
                "a two-stack car keeps both plumes registered, blocks duplicate SteamVents, returns the vents owner from Primary, and reports max PlumeTop");

            front.Active = false;
            front.Vents = null;
            front.HasSorting = false;
            roster.Remove(front, ownedVents: true, ownedSorting: true);
            var promotedVents = roster.NeedsVentsHandoff && roster.TryConsumeVentsHandoff(rear, Active, HasVents);
            var promotedSorting = roster.NeedsSortingHandoff && roster.TryConsumeSortingHandoff(rear, Active, HasSorting);
            rear.Vents = vents2;
            rear.HasSorting = true;
            Check(promotedVents && promotedSorting && !roster.NeedsAnyHandoff
                  && ReferenceEquals(roster.Primary(Active, HasVents), rear) && ReferenceEquals(rear.Vents, vents2),
                "releasing the primary vents+sorting owner promotes the surviving stack cleanly on its next update");

            // Split ownership: stack A holds stock steam sorting while stack B already owns SteamVents.
            // Releasing A triggers only a sorting handoff so B acquires sorting without recreating SteamVents.
            var splitRoster = new Core.CarPlumes<StackSlot>();
            var sortingOwner = new StackSlot { Active = true, Vents = null, HasSorting = true };
            var ventsOwner = new StackSlot { Active = true, Vents = vents1, HasSorting = false };
            splitRoster.Register(sortingOwner);
            splitRoster.Register(ventsOwner);
            sortingOwner.Active = false;
            sortingOwner.HasSorting = false;
            splitRoster.Remove(sortingOwner, ownedVents: false, ownedSorting: true);
            var splitTookVents = splitRoster.TryConsumeVentsHandoff(ventsOwner, Active, HasVents);
            var splitTookSorting = splitRoster.TryConsumeSortingHandoff(ventsOwner, Active, HasSorting, HasVents);
            ventsOwner.HasSorting = splitTookSorting;
            Check(!splitTookVents && splitTookSorting && !splitRoster.NeedsAnyHandoff
                  && ReferenceEquals(ventsOwner.Vents, vents1) && ventsOwner.HasSorting,
                "split ownership hands off stock steam sorting to the vents-owning stack without recreating its SteamVents");

            // Teardown of the last stack clears any pending handoff and leaves Primary null.
            rear.Active = false;
            rear.Vents = null;
            rear.HasSorting = false;
            roster.Remove(rear, ownedVents: true, ownedSorting: true);
            Check(roster.Count == 0 && !roster.NeedsAnyHandoff && roster.Primary(Active, HasVents) == null,
                "releasing the final stack clears the car's roster and pending handoffs");
        }

        // MatchSorting excludes sibling smoke/ember renderers (even from readers
        // that have not initialized or registered yet) and inactive sibling FX
        // children (such as disabled heat shimmer) without touching stock engine
        // steam renderers like DynamoSteam or CylCockSteam.
        private static void CheckMatchSortingExclusions()
        {
            var plumeType = typeof(ChimneyPlume);
            var matchSorting = HarmonyLib.AccessTools.Method(plumeType, "MatchSorting");
            var renderersUnder = HarmonyLib.AccessTools.Method(plumeType, "RenderersUnder");
            var matchCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(matchSorting)
                .Select(i => i.operand as MethodInfo).Where(m => m != null).ToList();

            var scansReaders = matchCalls.Any(m => m.Name == nameof(UnityEngine.Component.GetComponentsInChildren)
                && m.IsGenericMethod
                && m.GetGenericArguments().SequenceEqual(new[] { typeof(SteamSmokeParticlePortReader) }));
            var scansPlumes = matchCalls.Any(m => m.Name == nameof(UnityEngine.Component.GetComponentsInChildren)
                && m.IsGenericMethod
                && m.GetGenericArguments().SequenceEqual(new[] { plumeType }));
            Check(scansReaders && scansPlumes,
                "MatchSorting scans all SteamSmokeParticlePortReader and ChimneyPlume components on the car");

            // Inspect MatchSorting, RenderersUnder, and the non-generic lambdas
            // referenced by MatchSorting (its ldftn targets) for field/method access.
            var matchInstructions = HarmonyLib.PatchProcessor.GetOriginalInstructions(matchSorting);
            var inspectedMethods = new System.Collections.Generic.HashSet<MethodBase> { matchSorting, renderersUnder };
            foreach (var instr in matchInstructions)
            {
                if (instr.operand is MethodBase target && !target.ContainsGenericParameters
                    && target.DeclaringType != null
                    && (target.DeclaringType == plumeType || target.DeclaringType.DeclaringType == plumeType))
                    inspectedMethods.Add(target);
            }
            var allInstructions = inspectedMethods
                .Where(m => m != null && m.GetMethodBody() != null)
                .SelectMany(m => HarmonyLib.PatchProcessor.GetOriginalInstructions(m))
                .ToList();
            var fieldsRead = allInstructions.Select(i => i.operand as FieldInfo).Where(f => f != null).ToList();
            var readsSmokeParent = fieldsRead.Any(f => f.DeclaringType == typeof(SteamSmokeParticlePortReader) && f.Name == nameof(SteamSmokeParticlePortReader.smokeParticlesParent));
            var readsEmberParent = fieldsRead.Any(f => f.DeclaringType == typeof(SteamSmokeParticlePortReader) && f.Name == nameof(SteamSmokeParticlePortReader.emberParticlesParent));
            var readsEffectsRoot = fieldsRead.Any(f => f.DeclaringType == plumeType && f.Name == "effectsRoot");
            Check(readsSmokeParent && readsEmberParent && readsEffectsRoot,
                "MatchSorting excludes each reader's smokeParticlesParent and emberParticlesParent plus each plume's effectsRoot");

            var underInstructions = HarmonyLib.PatchProcessor.GetOriginalInstructions(renderersUnder);
            var includesInactive = false;
            for (var i = 1; i < underInstructions.Count; i++)
            {
                if (underInstructions[i].operand is MethodInfo m
                    && m.DeclaringType == typeof(UnityEngine.GameObject)
                    && m.Name == nameof(UnityEngine.GameObject.GetComponentsInChildren)
                    && m.IsGenericMethod
                    && m.GetGenericArguments().SequenceEqual(new[] { typeof(UnityEngine.ParticleSystemRenderer) })
                    && underInstructions[i - 1].opcode == System.Reflection.Emit.OpCodes.Ldc_I4_1)
                {
                    includesInactive = true;
                    break;
                }
            }
            Check(includesInactive,
                "RenderersUnder queries GameObject.GetComponentsInChildren<ParticleSystemRenderer>(true) so inactive FX and stock smoke/ember children are excluded");

            // Behavioral check with real Unity/game shell objects (no Unity native calls):
            // unregistered sibling reader's smoke/ember renderers and an inactive sibling FX
            // renderer are excluded, while genuine stock engine steam renderers remain.
            T Shell<T>() => (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T));
            var ownCloud = Shell<UnityEngine.ParticleSystemRenderer>();
            var ownInactiveHeat = Shell<UnityEngine.ParticleSystemRenderer>();
            var siblingCloud = Shell<UnityEngine.ParticleSystemRenderer>();
            var siblingInactiveHeat = Shell<UnityEngine.ParticleSystemRenderer>();
            var unregisteredSmoke = Shell<UnityEngine.ParticleSystemRenderer>();
            var unregisteredEmber = Shell<UnityEngine.ParticleSystemRenderer>();
            var dynamoSteam = Shell<UnityEngine.ParticleSystemRenderer>();
            var cylCockSteam = Shell<UnityEngine.ParticleSystemRenderer>();

            var ownFx = Shell<UnityEngine.GameObject>();
            var siblingFx = Shell<UnityEngine.GameObject>();
            var unregisteredSmokeParent = Shell<UnityEngine.GameObject>();
            var unregisteredEmberParent = Shell<UnityEngine.GameObject>();

            var ownPlume = Shell<ChimneyPlume>();
            var siblingPlume = Shell<ChimneyPlume>();
            var effectsRootField = plumeType.GetField("effectsRoot", BindingFlags.Instance | BindingFlags.NonPublic);
            effectsRootField.SetValue(ownPlume, ownFx);
            effectsRootField.SetValue(siblingPlume, siblingFx);

            var unregisteredReader = Shell<SteamSmokeParticlePortReader>();
            unregisteredReader.smokeParticlesParent = unregisteredSmokeParent;
            unregisteredReader.emberParticlesParent = unregisteredEmberParent;

            var hierarchy = new System.Collections.Generic.Dictionary<UnityEngine.GameObject, UnityEngine.ParticleSystemRenderer[]>(ReferenceComparer<UnityEngine.GameObject>.Instance)
            {
                [ownFx] = new[] { ownCloud, ownInactiveHeat },
                [siblingFx] = new[] { siblingCloud, siblingInactiveHeat },
                [unregisteredSmokeParent] = new[] { unregisteredSmoke },
                [unregisteredEmberParent] = new[] { unregisteredEmber },
            };
            var activeSystems = new System.Collections.Generic.Dictionary<ChimneyPlume, UnityEngine.ParticleSystemRenderer[]>(ReferenceComparer<ChimneyPlume>.Instance)
            {
                [ownPlume] = new[] { ownCloud },
                // Note: siblingInactiveHeat is absent from active Systems() (deactivated heat shimmer).
                [siblingPlume] = new[] { siblingCloud },
            };

            var excluded = Core.CarPlumes.CollectStackRenderers(
                new[] { ownCloud },
                new[] { ownPlume, siblingPlume },
                new[] { unregisteredReader },
                p => activeSystems.TryGetValue(p, out var r) ? r : Array.Empty<UnityEngine.ParticleSystemRenderer>(),
                p => (UnityEngine.GameObject)effectsRootField.GetValue(p),
                r => r.smokeParticlesParent,
                r => r.emberParticlesParent,
                go => !ReferenceEquals(go, null) && hierarchy.TryGetValue(go, out var r) ? r : Array.Empty<UnityEngine.ParticleSystemRenderer>(),
                ReferenceComparer<UnityEngine.ParticleSystemRenderer>.Instance);

            Check(excluded.Contains(unregisteredSmoke) && excluded.Contains(unregisteredEmber),
                "an unregistered sibling reader's stock smoke and ember renderers are excluded from MatchSorting");
            Check(excluded.Contains(siblingInactiveHeat) && excluded.Contains(ownInactiveHeat) && excluded.Contains(siblingCloud),
                "an inactive sibling FX renderer (such as disabled heat shimmer) and sibling active renderers are excluded from MatchSorting");
            Check(!excluded.Contains(dynamoSteam) && !excluded.Contains(cylCockSteam),
                "genuine stock engine steam renderers (DynamoSteam, CylCockSteam) stay available for MatchSorting");
        }

        private sealed class ReferenceComparer<T> : System.Collections.Generic.IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private static bool HasSorting(StackSlot s) => s != null && s.HasSorting;

        private sealed class StackSlot
        {
            public bool Active;
            public SteamVents Vents;
            public bool HasSorting;
            public float PlumeTop;
        }

        // A fire that sets each field of the contract's reading to its own value.
        private sealed class Marked : IStackFireSource
        {
            public static bool Throw;

            public bool Tended => true;

            public void Read(float dt, SteamExhaust exhaust, ref StackFireReading reading)
            {
                if (Throw)
                    throw new InvalidOperationException("a broken fire");
                object boxed = reading;
                foreach (var field in typeof(StackFireReading).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    field.SetValue(boxed, Value(field.Name, field.FieldType));
                reading = (StackFireReading)boxed;
            }

            public static object Value(string name, Type type) =>
                type == typeof(bool) ? (object)true : 1f + Math.Abs(name.GetHashCode() % 1000) / 7f;
        }

        // The settings as Unity Mod Manager's window draws them (and a settings
        // list such as the Comms Tablet's reads them): one heading, the most
        // used first, short labels with tooltips, the developer settings
        // undrawn; and a Settings.xml saved in the old order loading whole.
        private static void CheckSettings()
        {
            var drawn = typeof(Settings).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => (Field: f, Draw: f.GetCustomAttributes(typeof(UnityModManagerNet.DrawAttribute), false).OfType<UnityModManagerNet.DrawAttribute>().LastOrDefault(),
                    Heading: f.GetCustomAttributes(typeof(UnityEngine.HeaderAttribute), false).OfType<UnityEngine.HeaderAttribute>().FirstOrDefault()?.header))
                .Where(d => d.Draw != null).ToList();
            var order = new[] { "SmokeQuality", "SmokeSeason", "SteamVisibility", "WindStrength", "HeatShimmer", "EngineSteam", "CustomSmokeEffects" };
            Check(drawn.Select(d => d.Field.Name).SequenceEqual(order) && drawn[0].Heading == "Chimney smoke" && drawn.Skip(1).All(d => d.Heading == null),
                "the settings show under one heading, Chimney smoke, the most used first, and no developer setting: " + string.Join(", ", drawn.Select(d => d.Field.Name)));
            var heatDraw = drawn.Single(d => d.Field.Name == "HeatShimmer").Draw;
            Check(heatDraw.Label == "Heat shimmer", $"Heat shimmer applies live without a restart label ('{heatDraw.Label}')");
            // Unity Mod Manager's tooltip box (rich text) is one line per line of text: broken at about 80 characters.
            Check(drawn.All(d => !string.IsNullOrEmpty(d.Draw.Label) && d.Draw.Label.Length <= 60 && !string.IsNullOrEmpty(d.Draw.Tooltip) && !d.Draw.Tooltip.Contains("<")
                                 && d.Draw.Tooltip.Split('\n').All(line => line.Length <= 80)),
                $"every setting has a label of at most 60 characters (longest {drawn.Max(d => d.Draw.Label?.Length ?? 0)}) and a plain-text tooltip in lines of at most 80");

            const string old = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Settings>"
                + "<CustomSmokeEffects>false</CustomSmokeEffects><HeatShimmer>false</HeatShimmer><EngineSteam>false</EngineSteam>"
                + "<SmokeQuality>Auto</SmokeQuality><SmokeSeason>Winter</SmokeSeason><LogSetup>false</LogSetup>"
                + "<StatusLogInterval>5</StatusLogInterval><PlumeTestBed>a=1</PlumeTestBed></Settings>";
            var serializer = new XmlSerializer(typeof(Settings));
            bool Kept(Settings s) => !s.CustomSmokeEffects && !s.HeatShimmer && !s.EngineSteam && s.SmokeQuality == Core.SmokeQuality.Auto
                && s.SmokeSeason == Core.SmokeSeason.Winter && s.SteamVisibility == 1f && s.WindStrength == 1f
                && s.StatusLogInterval == 5 && s.PlumeTestBed == "a=1";
            Settings loaded;
            using (var reader = new StringReader(old))
                loaded = (Settings)serializer.Deserialize(reader);
            string saved;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, loaded);
                saved = writer.ToString();
            }
            using (var reader = new StringReader(saved))
                Check(Kept(loaded) && Kept((Settings)serializer.Deserialize(reader)),
                    "a Settings.xml in the old order without the new sliders loads defaults (1.0) and every value, and keeps them saved again");
            loaded.SteamVisibility = 1.6f;
            loaded.WindStrength = 0.4f;
            string customized;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, loaded);
                customized = writer.ToString();
            }
            using (var reader = new StringReader(customized))
            {
                var roundTripped = (Settings)serializer.Deserialize(reader);
                Check(Math.Abs(roundTripped.SteamVisibility - 1.6f) < 1e-5f && Math.Abs(roundTripped.WindStrength - 0.4f) < 1e-5f,
                    $"non-default SteamVisibility ({roundTripped.SteamVisibility:F1}) and WindStrength ({roundTripped.WindStrength:F1}) round-trip cleanly");
            }
            Check(!saved.Contains("LogSetup") && typeof(Settings).GetField("LogSetup") == null,
                "a Settings.xml with LogSetup loads without it, and it is not saved again");
        }

        private static StackReading Read(StockStack stack, SteamExhaust exhaust)
        {
            var reading = default(StackReading);
            stack.Read(0.02f, exhaust, ref reading);
            return reading;
        }

        private static void Run(SimComponent component, float seconds)
        {
            for (var t = 0f; t < seconds; t += 0.02f)
                component.Tick(0.02f);
        }

        private static Port Wire(SimComponent component, PortReferenceDefinition reference, PortValueType type, float value)
        {
            var port = new Port(reference.ID.ToLowerInvariant(), new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value);
            component.GetAllPortReferences().Single(r => r.id == $"{component.id}.{reference.ID}").SetPortReference(port);
            return port;
        }

        private static Port P(SimComponent component, PortDefinition port) =>
            component.GetAllPorts().Single(p => p.id == $"{component.id}.{port.ID}");

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
    }
}
