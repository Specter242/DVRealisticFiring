using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using DV.Items;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Sim;
using RealisticStackEffects;

namespace RealisticFiring.AdapterCheck
{
 internal static class Program
    {
        private const string Id = "firebox";

        private static int failures;

        private static int Main(string[] args)
        {
            RealisticFiring.Main.Settings = new Settings { LogCoalDeliveries = false };
            var gameParams = new SimGameParams(false, false, false, 1f, 1f);
            var def = new FireboxDefinition { ID = Id };

            var stock = new Firebox(def);
            var mine = Create(def, gameParams);
            Check(Describe(stock) == Describe(mine),
                $"same ports, types, defaults, and port references ({stock.GetAllPorts().Count} ports, {stock.GetAllPortReferences().Count} references)");
            Check(stock.id == mine.id && mine.HasSaveData, "same component ID, with save data");

            // Add-and-reset delivery, as FireboxSimController.TransferCoal and CCL's stoker write it.
            foreach (var c in new SimComponent[] { stock, mine })
            {
                P(c, "COAL_CONTROL_EXT_IN").Value += 7.5f;
                P(c, "COAL_CONTROL_EXT_IN").Value += 100f;
            }
            Check(P(mine, "COAL_CONTROL_EXT_IN").Value == 0f, "delivery port resets to 0");
            Check(P(mine, "COAL_LEVEL").Value == P(stock, "COAL_LEVEL").Value,
                $"overfilled coal level matches stock ({P(mine, "COAL_LEVEL").Value} kg)");
            Check(mine.LastDeliveryMass > 0f && mine.LastDeliveryMass < 100f,
                $"the last delivery shows what landed, not what was sent ({mine.LastDeliveryMass:F1} of 100 kg)");
            foreach (var c in new SimComponent[] { stock, mine })
                P(c, "COAL_CONTROL_EXT_IN").Value += -20f;
            Check(Math.Abs(P(mine, "COAL_LEVEL").Value - P(stock, "COAL_LEVEL").Value) < 1e-3f, "negative delivery removes coal like stock");

            // A steady draft, as SteamExhaust would provide.
            var air = new Port("exhaust", new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "AIR_FLOW"), 4f);
            mine.GetAllPortReferences().Single(r => r.id == $"{Id}.AIR_FLOW").SetPortReference(air);

            P(mine, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            mine.Tick(0.02f);
            Check(P(mine, "FIRE_ON").Value == 1f && P(mine, "IGNITION_EXT_IN").Value == 0f, "ignition request lights the fire and resets");
            for (var i = 0; i < 500; i++)
                mine.Tick(0.02f);
            Check(P(mine, "COAL_ENV_DAMAGE_METER").Value > 0f, $"environmental damage meter accumulates ({P(mine, "COAL_ENV_DAMAGE_METER").Value:F2} kg)");
            Check(P(mine, "TEMPERATURE").Value > 100f && P(mine, "HEAT").Value > 0f, "fire heats");

            // The fire-dump lever (COAL_DUMP_CONTROL, 0.5 at center) doubles as the rocking grate lever.
            var lever = new Port("coalDump", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 0.5f);
            var grate = Create(def, gameParams);
            grate.GetAllPortReferences().Single(r => r.id == $"{Id}.COAL_DUMP_CONTROL").SetPortReference(lever);
            grate.GetAllPortReferences().Single(r => r.id == $"{Id}.AIR_FLOW").SetPortReference(air);
            grate.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 60f });
            Run(grate, 60f);
            var ashBeforeRock = grate.Model.AshMass;
            var dumpedBeforeRock = grate.Model.CoalDumped;
            lever.Value = 0f;
            Run(grate, 0.3f);
            lever.Value = 0.5f;
            Run(grate, 0.3f);
            Check(grate.Model.AshMass < 0.6f * ashBeforeRock && grate.Model.CoalDumped == dumpedBeforeRock,
                $"a quick rock of the dump lever shakes the grate (ash {ashBeforeRock:F2} -> {grate.Model.AshMass:F2} kg) without dumping");
            P(grate, "COAL_CONTROL_EXT_IN").Value += 60f;
            var dumpedBeforeHold = grate.Model.CoalDumped;
            lever.Value = 0f;
            Run(grate, 3f);
            lever.Value = 0.5f;
            Check(grate.Model.CoalDumped - dumpedBeforeHold > 5f,
                $"holding the dump lever over dumps the fire ({grate.Model.CoalDumped - dumpedBeforeHold:F1} kg dumped)");

            var save = mine.GetSaveStateData();
            Check(save.Value<bool>("fireOn") && save["coalMass"] != null && save["coalEnvDmg"] != null && save["rfZones"] is JArray
                  && save["rfFireman"] == null,
                "save has the stock keys plus rfZones (and no fireman's mode while he is off)");
            var stockReader = new Firebox(def);
            stockReader.SetGameParams(gameParams);
            stockReader.SetSaveStateData(save);
            stockReader.Tick(0.02f);
            Check(Math.Abs(P(stockReader, "COAL_LEVEL").Value - save.Value<float>("coalMass")) < 0.05f && P(stockReader, "FIRE_ON").Value == 1f,
                "the stock firebox loads the mod's save");

            var copy = Create(def, gameParams);
            copy.SetSaveStateData(save);
            Check(P(copy, "COAL_LEVEL").Value == P(mine, "COAL_LEVEL").Value && P(copy, "FIRE_ON").Value == 1f
                  && save["rfZones"].ToString() == copy.GetSaveStateData()["rfZones"].ToString(),
                "the mod loads its own save, zones included");

            var fromStock = Create(def, gameParams);
            fromStock.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 36f, ["coalEnvDmg"] = 12f });
            Check(P(fromStock, "COAL_LEVEL").Value == 36f && P(fromStock, "FIRE_ON").Value == 1f
                  && P(fromStock, "COAL_ENV_DAMAGE_METER").Value == 12f, "the mod loads a stock save");

            // Door air through the port FireboxSimController names.
            var closedDoorAir = mine.Model.DoorAirFlow;
            var door = new Port("door", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            mine.AttachDoorPort(door);
            door.Value = 1f;
            mine.Tick(0.02f);
            Check(mine.DoorOpening == 1f && mine.Model.DoorAirFlow > 5f * closedDoorAir,
                $"door port feeds air above the fire ({closedDoorAir:F2} -> {mine.Model.DoorAirFlow:F2} kg/s)");

            P(mine, "EXTINGUISH_EXT_IN").ExternalValueUpdate(1f);
            mine.Tick(0.02f);
            Check(P(mine, "FIRE_ON").Value == 0f && P(mine, "EXTINGUISH_EXT_IN").Value == 0f, "extinguish request puts the fire out and resets");

            // Boiler-broken shutdown fires on the referenced port's change.
            var broken = new Port("boiler", new PortDefinition(PortType.READONLY_OUT, PortValueType.STATE, "IS_BROKEN"));
            var lit = Create(def, gameParams);
            lit.GetAllPortReferences().Single(r => r.id == $"{Id}.BOILER_BROKEN_STATE").SetPortReference(broken);
            lit.InitializationAfterConnecting();
            lit.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 20f });
            broken.Value = 1f;
            Check(P(lit, "FIRE_ON").Value == 0f && !lit.Model.FireOn, "a broken boiler puts the fire out");

            // The loco start-up (StartupHelper.Startup) fills the firebox with
            // TransferCoal(9999999) and lights it; StartupPatch then lays the
            // ready fire.
            var started = Create(def, gameParams);
            started.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 10f });
            P(started, "COAL_CONTROL_EXT_IN").Value += 9999999f / 4f;
            P(started, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            started.PrepareFire();
            started.Tick(0.02f);
            Check(started.Model.FireOn && P(started, "FIRE_ON").Value == 1f
                  && started.Model.Zones.All(z => z.Fresh == 0f && z.Burning > 0f)
                  && Math.Abs(P(started, "COAL_LEVEL").Value - 0.5f * started.Model.Parameters.MaxCoalCapacity) < 0.5f
                  && started.LastDeliveryMass == 0f,
                $"start-up lays a medium, burning fire ({P(started, "COAL_LEVEL").Value:F1} kg of {started.Model.Parameters.MaxCoalCapacity:F0}), not a box of green coal");

            // A stoker's per-tick trickle is continuous feed, not a shovelful.
            var stoked = Create(def, gameParams);
            stoked.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 20f });
            for (var i = 0; i < 10; i++)
                P(stoked, "COAL_CONTROL_EXT_IN").Value += 0.2f;
            Check(stoked.LastDeliveryMass == 0f && Math.Abs(stoked.TakeContinuousFeed() - 2f) < 1e-3f
                  && Math.Abs(P(stoked, "COAL_LEVEL").Value - 22f) < 1e-3f,
                "a stoker's trickle feeds the fire as continuous feed, not as shovelfuls");

            CheckInjector(gameParams);
            CheckInjectorSteamValve(gameParams);
            CheckJk3Injectors(gameParams);
            CheckLms8FInjectors(gameParams);
            CheckJk3Firebox(gameParams);
            CheckBoilerWater();
            CheckGlassTilt();
            CheckFiremanGlassDisplay();
            CheckVrShedHoseParts();
            CheckStoker(gameParams, def);
            CheckOilBurner(gameParams);
            CheckClientMirror(gameParams, def);
            CheckBooster();
            CheckCylinders(gameParams);
            CheckBlower(gameParams);
            CheckAirPump(gameParams);
            CheckSafetyValveFit(gameParams);
            CheckCoalStore(gameParams);

            CheckControlSteps();
            CheckShovelLoads();
            CheckFiremanHand();
            CheckPatchTargets();
            CheckStackEffectsFeed();
            CheckPlumeReinitialization();
            CheckPatchEachClass();
            CheckCarPoolGuard();
            var after = false;
            var contained = true;
            try
            {
                Patches.SetupGuard.Run(null, "check", () => throw new InvalidOperationException("a fitting that breaks"));
                after = true;
            }
            catch (Exception)
            {
                contained = false;
            }
            Check(contained && after, "a car's setup step that throws is logged and the setup goes on (SetupGuard)");
            CheckPurchaseGuard();
            Check(AccessTools.Field(typeof(ShovelNonPhysicalCoal), "_coalMassLoaded")?.FieldType == typeof(float),
                "the shovel's load is readable, for saying a shovelful does not fit the firebox");
            CheckFireIronLoading();
            CheckFiringTablet();
            CheckFiringManual();
            CheckFiringTabletVrButtonSpec();
            CheckFiremanOrders();
            CheckFiringTabletStartingItems();
            CheckNewDefaults();
            CheckFireboxQualitySettings();
            CheckShedStandReload();
            CheckRetiredBrakeSetting();
            Check(AccessTools.Field(typeof(DV.Interaction.AGrabHandler), Visuals.FiringValvePassThrough.PassThroughField)?.FieldType
                  == typeof(DV.Interaction.AGrabHandler.InteractionPassThroughDelegate),
                "a control's pass-through is wrappable, so the oil burner's valves can be reached past the firing valve");
            // Optional installed mods, by file name: Multiplayer's and Skin Manager's assemblies.
            foreach (var path in args.Where(File.Exists))
            {
                if (Path.GetFileName(path) == "Multiplayer.dll")
                    CheckMultiplayerCabHooks(path);
                else if (Path.GetFileName(path) == Items.SkinManagerFireIronLoading.AssemblyName + ".dll")
                    CheckSkinManagerFireIron(path);
                else if (Path.GetFileName(path) == SeasonsMod.AssemblyName + ".dll")
                    CheckDvSeasons(path);
            }
            Console.WriteLine(failures == 0 ? "All adapter checks passed" : $"{failures} adapter checks failed");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(SimComponent component, float seconds)
        {
            for (var t = 0f; t < seconds; t += 0.02f)
                component.Tick(0.02f);
        }

        private static void CheckStandSync(Assembly multiplayer)
        {
            const string harmonyId = "RealisticFiring.AdapterCheck.StandHoses";
            // Written and read back as the Multiplayer mod carries them.
            void RoundTrip(string name, Action<BinaryWriter> write, Action<BinaryReader> read)
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                        write(writer);
                    stream.Position = 0;
                    using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                        read(reader);
                    Check(stream.Position == stream.Length, $"a {name} reads back to its last byte");
                }
            }
            var sent = new Multiplayer.StandPlugPacket
            {
                Stand = "SM-A-02-P at 26.3", Place = (byte)Shed.PlugPlace.Held, Holder = 3, Car = 4711,
                Position = new UnityEngine.Vector3(1.5f, -2f, 12800.25f), Rotation = new UnityEngine.Quaternion(0f, 0.6f, 0f, 0.8f),
                Settled = true, PoseOnly = true, Seq = 65535,
            };
            var got = new Multiplayer.StandPlugPacket();
            RoundTrip(nameof(Multiplayer.StandPlugPacket), sent.Serialize, got.Deserialize);
            Check(got.Stand == sent.Stand && got.Place == sent.Place && got.Holder == 3 && got.Car == 4711
                  && got.Position == sent.Position && got.Rotation.y == 0.6f && got.Rotation.w == 0.8f && got.Settled && got.PoseOnly && got.Seq == 65535,
                "a stand's plug packet carries its stand, count, place, holder, engine, pose, and flags");
            var valve = new Multiplayer.StandValvePacket();
            RoundTrip(nameof(Multiplayer.StandValvePacket), new Multiplayer.StandValvePacket { Stand = "HB-T1-03", Value = 0.37f, Seq = 12 }.Serialize, valve.Deserialize);
            Check(valve.Stand == "HB-T1-03" && valve.Value == 0.37f && valve.Seq == 12, "a stand's valve packet carries its stand, count, and opening");
            Check(Shed.ShedNet.Newer(1, 0) && Shed.ShedNet.Newer(0, 65535) && Shed.ShedNet.Newer(10, 65000) && !Shed.ShedNet.Newer(5, 5)
                  && !Shed.ShedNet.Newer(65535, 0) && !Shed.ShedNet.Newer(4, 9),
                "a later word on a stand counts higher, wrapping past 65535, so one delivered late is dropped");

            var report = Multiplayer.StandSync.Report(sent);
            var notANumber = Multiplayer.StandSync.Report(new Multiplayer.StandPlugPacket { Stand = "x", Position = new UnityEngine.Vector3(float.NaN, 0f, 0f) });
            var noTurn = Multiplayer.StandSync.Report(new Multiplayer.StandPlugPacket { Stand = "x", Rotation = new UnityEngine.Quaternion(0f, 0f, 0f, 0f) });
            var noPlace = Multiplayer.StandSync.Report(new Multiplayer.StandPlugPacket { Stand = "x", Place = 9 });
            var noStand = Multiplayer.StandSync.Report(new Multiplayer.StandPlugPacket { Stand = new string('x', 200) });
            Check(report.HasValue && report.Value.Place == Shed.PlugPlace.Held && report.Value.Car == 4711 && report.Value.Seq == 65535
                  && !notANumber.HasValue && !noTurn.HasValue && !noPlace.HasValue && !noStand.HasValue,
                "a stand's plug packet is read into a report, and one not a number, unturned, of no place, or of no stand is dropped");

            Shed.PlugReport At(Shed.PlugPlace place, byte holder = 0, ushort car = 0, bool settled = false, float x = 0f) =>
                new Shed.PlugReport { Place = place, Holder = holder, Car = car, Settled = settled, Position = new UnityEngine.Vector3(x, 0f, 0f) };
            Check(At(Shed.PlugPlace.Held, 1).SamePlace(At(Shed.PlugPlace.Held, 1, x: 5f))
                  && !At(Shed.PlugPlace.Held, 1).SamePlace(At(Shed.PlugPlace.Held, 2))
                  && !At(Shed.PlugPlace.InEngine, car: 7).SamePlace(At(Shed.PlugPlace.InEngine, car: 8))
                  && At(Shed.PlugPlace.Loose, settled: true).SamePlace(At(Shed.PlugPlace.Loose, x: 3f))
                  && At(Shed.PlugPlace.OnStand, holder: 4).SamePlace(At(Shed.PlugPlace.OnStand)),
                "a plug's place is its holder for a held one and its engine for one plugged in, its pose aside");

            Check(Shed.ShedNet.HostPlug("no such stand", At(Shed.PlugPlace.Held, 2), false, 2, UnityEngine.Vector3.zero, out _) == Shed.Relay.None
                  && Shed.ShedNet.HostValve("no such stand", 1f, 1, 2, UnityEngine.Vector3.zero, out _, out _) == Shed.Relay.None,
                "the host passes on nothing for a stand it has not built");
            Shed.ShedNet.ClientValve("not built yet", 0.5f, 3);
            Shed.ShedNet.ClientPlug("not built yet", At(Shed.PlugPlace.Loose, settled: true), false);
            Shed.ShedNet.Reset();
            Check(true, "a client keeps the host's word for a stand not built yet, and forgets it as a session ends");

            // Our reach check goes before the game's PropHose.Update, which
            // the Multiplayer mod rewrites for the hoses it networks; its
            // other hose patches are why a client's stands start loose.
            var update = AccessTools.Method(typeof(PropHose), "Update");
            var patches = new[] { "Multiplayer.Patches.World.PropHosePatch:OnEnable", "Multiplayer.Patches.World.PropHosePatch:Update",
                "Multiplayer.Patches.World.PluggableObjectPatch:Awake" };
            Check(patches.All(p => multiplayer.GetType(p.Split(':')[0])?.GetMethod(p.Split(':')[1], BindingFlags.Public | BindingFlags.Static) != null),
                "the installed Multiplayer mod still skips a hose's own snap home on clients and rewrites its reach check");
            try
            {
                new HarmonyLib.Harmony(harmonyId).Patch(update, prefix: new HarmonyLib.HarmonyMethod(typeof(Shed.StandHoseReachPatch), "Prefix"));
                Check(HarmonyLib.Harmony.GetPatchInfo(update)?.Prefixes.Any(p => p.owner == harmonyId) == true,
                    "a stand hose's reach check patches the game's PropHose.Update");
            }
            catch (Exception e)
            {
                Check(false, "a stand hose's reach check: " + e.GetBaseException().Message);
            }
            finally
            {
                new HarmonyLib.Harmony(harmonyId).UnpatchAll(harmonyId);
            }
        }

        private static void CheckMultiplayerCabHooks(string path)
        {
            const string harmonyId = "RealisticFiring.AdapterCheck.MultiplayerCab";
            var modFolder = Path.GetDirectoryName(path);
            var gameFolder = Path.GetDirectoryName(Path.GetDirectoryName(modFolder));
            // Resolve game/mod dependencies without adding the game's
            // mscorlib to MONO_PATH and replacing Mono's own core library.
            ResolveEventHandler resolve = (_, request) =>
            {
                var name = new AssemblyName(request.Name).Name;
                if (name == "mscorlib" || name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal))
                    return null;
                foreach (var folder in new[] { modFolder, Path.Combine(gameFolder, "DerailValley_Data", "Managed") })
                {
                    var file = Path.Combine(folder, name + ".dll");
                    if (File.Exists(file))
                        return Assembly.LoadFrom(file);
                }
                return null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolve;
            try
            {
                var assembly = Assembly.LoadFrom(path);
                var type = assembly.GetType("Multiplayer.Components.Networking.Train.NetworkedTrainCar", throwOnError: true);
                var targets = Multiplayer.CabControlsCompatibility.PatchTargets(type);
                var helpers = targets.SelectMany(method => Multiplayer.CabControlsCompatibility.Transpile(
                    HarmonyLib.PatchProcessor.GetOriginalInstructions(method)))
                    .Select(instruction => instruction.operand as MethodInfo)
                    .Where(method => method?.DeclaringType == typeof(Multiplayer.CabControlsCompatibility)).ToArray();
                Check(targets.Length == 4 && helpers.Count(method => method.Name == "Discover") == 1
                    && helpers.Count(method => method.Name == "ResolvePort") == 2 && helpers.Count(method => method.Name == "Kind") == 2,
                    "installed Multiplayer cab hookup uses feeder discovery, both port reads, and null-safe scroll logging");
                var warnings = new System.Collections.Generic.List<string>();
                Multiplayer.CabControlsCompatibility.Install(harmonyId, _ => { }, warnings.Add);
                var load = HarmonyLib.AccessTools.DeclaredMethod(type, "OnTrainCarInteriorLoaded");
                Check(warnings.Count == 0 && targets.Concat(new MethodBase[] { load }).All(method =>
                    HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Contains(harmonyId) == true),
                    "all five Multiplayer cab patches compile and install against the installed assembly"
                    + (warnings.Count > 0 ? ": " + string.Join("; ", warnings) : ""));
                // The unload notification exits before touching a Unity
                // component; invoking the patched original proves that path.
                load.Invoke(System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type), new object[] { null });
                Check(true, "a null interior notification skips Multiplayer's load coroutine");
                CheckStandSync(assembly);
            }
            catch (Exception e)
            {
                Check(false, "Multiplayer cab compatibility: " + e.GetBaseException().Message);
            }
            finally
            {
                new HarmonyLib.Harmony(harmonyId).UnpatchAll(harmonyId);
                AppDomain.CurrentDomain.AssemblyResolve -= resolve;
            }
        }

        private static void CheckJk3Firebox(SimGameParams gameParams)
        {
            // Lilla and Cruzer's JK3 1.0.1 release bundle (2026-10-10).
            // Keep its smaller firebox and tender consumption, rather than
            // substituting the S060's or S282's figures.
            var def = new FireboxDefinition
            {
                ID = Id,
                maxCoalCapacity = 70f,
                burnTime = 200f,
                efficiencyAtMaxCombustion = 0.5f,
                coalDumpRate = 4f,
                combustionRateSmoothTime = 5f,
                temperatureSmoothTime = 15f,
                startupMaxPressure = 13f,
            };
            var stock = new Firebox(def);
            var fire = Create(def, gameParams);
            Check(FireboxRegistry.SupportedLiveries.Contains("Jk3A") && !FireboxRegistry.SupportedLiveries.Contains("Jk3B"),
                "the JK3 engine gets a realistic firebox; its tender does not");
            Check(Describe(stock) == Describe(fire), "the JK3 firebox preserves its stock ports and defaults");
            Check(fire.Model.Parameters.MaxCoalCapacity == 70f && fire.Model.Parameters.BurnTime == 200f,
                "the JK3 keeps its 70 kg firebox and 200 s burn time");

            fire.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 30f, ["coalEnvDmg"] = 12f });
            var pileMass = ShovelSize.PileCapacity(3f, ShovelSize.StandardShovelChunks, 1.8f);
            P(fire, "COAL_CONTROL_EXT_IN").Value += pileMass / 1.8f;
            Check(Math.Abs(P(fire, "COAL_LEVEL").Value - 33f) < 1e-4f && P(fire, "COAL_CONTROL_EXT_IN").Value == 0f,
                "a JK3 shovelful takes 5.4 tender kg and lands 3 kg on the grate");

            var saved = fire.GetSaveStateData();
            stock.SetSaveStateData(saved);
            Check(stock.GetSaveStateData().Value<float>("coalMass") == 33f
                  && stock.GetSaveStateData().Value<bool>("fireOn")
                  && stock.GetSaveStateData().Value<float>("coalEnvDmg") == 12f,
                "the stock JK3 firebox can load the realistic fire's coal, ignition, and damage state");

            var character = FireboxCharacters.ForLivery("Jk3A");
            var jk3 = new FireModel(fire.Model.Parameters) { Character = character.Grate, GrateArea = 1.430f };
            var smallSwitcher = new FireModel(fire.Model.Parameters) { Character = FireboxCharacters.ForLivery("LocoS060").Grate, GrateArea = 1.430f };
            var typical = new FireModel(fire.Model.Parameters) { GrateArea = 1.430f };
            float Pull(FireModel model) => model.DraftWeight(GrateZone.Front) / model.DraftWeight(GrateZone.UnderDoor);
            // With no arch asserted the JK3 draws forward; the arched S060 a
            // little harder at the back, a typical sealed arch more so.
            Check(Pull(jk3) > 1f && Pull(smallSwitcher) < 1f && Pull(typical) < Pull(smallSwitcher) && !character.PrototypeSlope.HasValue,
                $"the level JK3 grate draws moderately forward (front/back {Pull(jk3):F2}); the S060's arched box a little to the back ({Pull(smallSwitcher):F2}), a typical sealed arch more ({Pull(typical):F2})");

            float FreshRound(FireModel model)
            {
                model.Restore(fireOn: true, coalMass: 35f);
                var inputs = new FireInputs { AirFlow = 0.5f * model.Parameters.MaxCombustionRate * FireModel.AirPerCoal,
                    StartupMultiplier = 1f, BoilerTemperature = 180f };
                for (var t = 0f; t < 60f; t += 0.02f)
                    model.Step(0.02f, inputs);
                model.AddCoalEvenly(17.5f);
                for (var t = 0f; t < 10f; t += 0.02f)
                    model.Step(0.02f, inputs);
                return model.SootDensity;
            }
            var jk3Smoke = FreshRound(jk3);
            var switcherSmoke = FreshRound(smallSwitcher);
            var typicalSmoke = FreshRound(typical);
            Check(jk3Smoke > typicalSmoke && jk3Smoke < switcherSmoke,
                $"a fresh JK3 round smokes more than the typical box, less than the S060 profile (soot {jk3Smoke:F2}, typical {typicalSmoke:F2}, S060 {switcherSmoke:F2})");
        }

        // The realistic injector against a stock boiler with the S282's figures.
        private static void CheckInjector(SimGameParams gameParams)
        {
            var lever = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var mechanical = new Port("axlePump", new PortDefinition(PortType.OUT, PortValueType.CONTROL, "OUT"));
            var tenderWater = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 40000f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));

            Boiler NewBoiler(float pressure, Port control)
            {
                var def = new BoilerDefinition
                {
                    ID = "boiler",
                    diameter = 1.7f,
                    length = 8.8f,
                    capacityMultiplier = 0.85f,
                    maxInjectorRate = 20f,
                    waterConsumptionMultiplier = 4f,
                    safetyValveOpeningPressure = 15f,
                    safetyValveClosingPressure = 14.8f,
                    spawnPressure = pressure,
                    spawnWaterLevel = 9000f,
                };
                var boiler = (Boiler)def.InstantiateImplementation();
                boiler.SetGameParams(gameParams);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler.INJECTOR").SetPortReference(control);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER").SetPortReference(tenderWater);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER_CONSUMPTION").SetPortReference(consumed);
                return boiler;
            }

            // Runs the rig, collecting what it reports taken from the tender.
            float Run(InjectorRig rig, float seconds)
            {
                var taken = 0f;
                for (var t = 0f; t < seconds; t += 0.02f)
                {
                    consumed.Value = 0f;
                    rig.Simulate(0.02f);
                    taken += consumed.Value;
                    rig.BoilerVessel.Update();
                }
                return taken;
            }

            Check(!InjectorRig.TryCreate(null, NewBoiler(12f, mechanical), out var mechanicalNote) && mechanicalNote.Contains("stock"),
                "an injector driven by another component's output keeps the stock injector");
            Check(InjectorRig.TryCreate(null, NewBoiler(12f, lever), out var leverNote) && leverNote.Contains("realistic"),
                "an injector worked by a cab control gets the realistic injector");

            var hot = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, hot, out _);
            InjectorRig.TryGet(hot, out var rig);
            var massBefore = rig.BoilerVessel.mass;
            var pressureBefore = rig.BoilerVessel.pressure;
            lever.Value = 0.1f;
            var spilled = Run(rig, 0.5f);
            Check(rig.Flow.State == InjectorState.Priming && spilled > 0f && rig.BoilerVessel.mass <= massBefore,
                $"cracked at {pressureBefore:F0} bar it spills while priming ({spilled:F0} tender units in 0.5 s, nothing into the boiler)");
            Run(rig, 1.5f);
            lever.Value = 1f;
            var drawn = Run(rig, 3f);
            Check(rig.Flow.State == InjectorState.Working && rig.BoilerVessel.mass > massBefore + 10f
                  && rig.Flow.FeedTemperature > 55f && rig.Flow.FeedTemperature < 95f && drawn > 0f,
                $"primed and opened, it feeds the boiler ({rig.BoilerVessel.mass - massBefore:F0} kg in, {rig.Flow.WaterIn:F1} kg/s at {rig.Flow.FeedTemperature:F0} C, " +
                $"steam {rig.Flow.SteamUsed:F2} kg/s, pressure {pressureBefore:F1} -> {rig.BoilerVessel.pressure:F1} bar)");

            // An engine set up with its injector open to feed, as a save loads: it carries on.
            var loaded = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, loaded, out _);
            InjectorRig.TryGet(loaded, out var loadedRig);
            var loadedMass = loadedRig.BoilerVessel.mass;
            Run(loadedRig, 2f);
            Check(loadedRig.Flow.State == InjectorState.Working && loadedRig.Flow.OverflowSteam == 0f && loadedRig.BoilerVessel.mass > loadedMass,
                $"loaded with its injector open, it carries on feeding ({loadedRig.BoilerVessel.mass - loadedMass:F0} kg in 2 s)");

            var slammed = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, slammed, out _);
            InjectorRig.TryGet(slammed, out var slammedRig);
            slammedRig.EndResume();
            var slammedMass = slammedRig.BoilerVessel.mass;
            var slammedPressure = slammedRig.BoilerVessel.pressure;
            Run(slammedRig, 2f);
            Check(slammedRig.Flow.State == InjectorState.Broken && slammedRig.BoilerVessel.mass < slammedMass
                  && slammedRig.Flow.OverflowSteam > 0f && slammedRig.BoilerVessel.pressure < slammedPressure,
                $"opened wide before it primes, the jet breaks and blows boiler steam ({slammedMass - slammedRig.BoilerVessel.mass:F1} kg in 2 s, " +
                $"injector at {slammedRig.Flow.InjectorTemperature:F0} C)");

            lever.Value = 0.1f;
            var cold = NewBoiler(2f, lever);
            InjectorRig.TryCreate(null, cold, out _);
            InjectorRig.TryGet(cold, out var coldRig);
            var coldMass = coldRig.BoilerVessel.mass;
            var coldSpill = Run(coldRig, 3f);
            Check(coldRig.Flow.State == InjectorState.LowPressure && coldSpill > 0f && coldRig.BoilerVessel.mass <= coldMass,
                $"below its starting pressure it only spills water ({coldSpill:F0} tender units in 3 s)");

            lever.Value = 0f;
            var empty = NewBoiler(1f, lever);
            InjectorRig.TryCreate(null, empty, out _);
            InjectorRig.TryGet(empty, out var emptyRig);
            var emptyMass = emptyRig.BoilerVessel.mass;
            emptyRig.HandPumping = true;
            var pumpedFromTender = Run(emptyRig, 10f);
            emptyRig.HandPumping = false;
            var afterPump = emptyRig.BoilerVessel.mass;
            emptyRig.HoseIn = true;
            emptyRig.HoseOpening = 1f;
            var hoseFromTender = Run(emptyRig, 10f);
            var afterHose = emptyRig.BoilerVessel.mass;
            var wideOpen = afterHose - afterPump;
            emptyRig.HoseOpening = 0.5f;
            Run(emptyRig, 10f);
            var halfOpen = emptyRig.BoilerVessel.mass - afterHose;
            emptyRig.HoseOpening = 0f;
            var shutMass = emptyRig.BoilerVessel.mass;
            Run(emptyRig, 2f);
            var shutFilling = emptyRig.HoseFilling;
            emptyRig.HoseIn = false;
            Check(afterPump - emptyMass > 10f && pumpedFromTender > 0f && wideOpen > 100f && hoseFromTender == 0f
                  && halfOpen > 0.4f * wideOpen && halfOpen < 0.6f * wideOpen && emptyRig.BoilerVessel.mass == shutMass && !shutFilling,
                $"with no steam the hand pump lifts from the tender ({afterPump - emptyMass:F0} kg in 10 s) " +
                $"and the water hose fills from the mains ({wideOpen:F0} kg in 10 s, {halfOpen:F0} with its stand's valve half open, none shut)");

            rig.HoseIn = true;
            rig.HoseOpening = 1f;
            var hotMass = rig.BoilerVessel.mass;
            lever.Value = 0f;
            Run(rig, 2f);
            Check(rig.BoilerVessel.mass <= hotMass && !rig.HoseFilling, "a hose cannot fill a boiler under pressure");
            rig.HoseIn = false;
            rig.HoseOpening = 0f;

            // Its first water a plugging washes a cold boiler out (as the
            // hydrant key did), once until it is taken out and put back.
            var washed = NewBoiler(1f, lever);
            InjectorRig.TryCreate(null, washed, out _);
            InjectorRig.TryGet(washed, out var washedRig);
            var washedWater = BoilerWaterRig.Create(washed, null, BoilerWaterRig.Guard.FusiblePlug);
            washedWater.MeltPlug();
            washedRig.HoseIn = true;
            washedRig.HoseOpening = 1f;
            Run(washedRig, 0.1f);
            var renewedOnce = !washedWater.Plug.Melted && washedRig.TakeWashedOut() && !washedRig.TakeWashedOut();
            washedWater.MeltPlug();
            Run(washedRig, 0.1f);
            var keptWhileIn = washedWater.Plug.Melted;
            washedRig.HoseIn = false;
            washedRig.HoseOpening = 0f;
            Run(washedRig, 0.1f);
            washedRig.HoseIn = true;
            washedRig.HoseOpening = 1f;
            Run(washedRig, 0.1f);
            Check(renewedOnce && keptWhileIn && !washedWater.Plug.Melted && washedRig.TakeWashedOut(),
                "the hose's first water washes a cold boiler out (a melted plug renewed), once until the hose is taken out and put back");

            var plant = InjectorRig.PlantPressure;
            Check(InjectorRig.DirectSteamFlow(1f, 1f) == InjectorRig.DirectSteamMost && InjectorRig.DirectSteamFlow(0.5f, 1f) == 0.5f * InjectorRig.DirectSteamMost
                  && InjectorRig.DirectSteamFlow(1f, 0.8f * plant) < InjectorRig.DirectSteamMost && InjectorRig.DirectSteamFlow(1f, 0.8f * plant) > 0f
                  && InjectorRig.DirectSteamFlow(1f, plant) == 0f && InjectorRig.DirectSteamFlow(0f, 1f) == 0f,
                $"direct steam is choked at {InjectorRig.DirectSteamMost:F1} kg/s wide open below half the plant's pressure, " +
                $"{InjectorRig.DirectSteamFlow(1f, 0.8f * plant):F2} kg/s at 80 % of it, none at the plant's {InjectorRig.PlantGauge:F0} psi");

            var repaired = NewBoiler(1f, lever);
            InjectorRig.TryCreate(null, repaired, out _);
            InjectorRig.TryGet(repaired, out var hotRig);
            hotRig.HoseIn = true;
            hotRig.HoseHot = true;
            hotRig.HoseOpening = 1f;
            Run(hotRig, 1f);
            var hotFeed = hotRig.FeedTemperatureLastStep;
            hotRig.HoseOpening = 0f;
            hotRig.HoseSteam = 1f;
            var steamedFrom = hotRig.BoilerVessel.mass;
            var steamedFromPressure = hotRig.BoilerVessel.pressure;
            Run(hotRig, 300f);
            var steamedMass = hotRig.BoilerVessel.mass - steamedFrom;
            var steamedPressure = hotRig.BoilerVessel.pressure;
            Run(hotRig, 4000f);
            var settled = hotRig.BoilerVessel.pressure;
            Check(Math.Abs(hotFeed - InjectorRig.HotWaterTemperature) < 0.01f && steamedMass > 100f && steamedPressure > steamedFromPressure + 1f
                  && settled <= plant + 0.05f && settled > plant - 1f && hotRig.SteamRate < 0.3f,
                $"a repair stand fills at {hotFeed:F0} C, and its steam raises the boiler ({steamedFromPressure:F1} -> {steamedPressure:F1} bar and {steamedMass:F0} kg of water in 5 min, " +
                $"{settled:F2} bar of the plant's {plant:F2} after 72 min, then {hotRig.SteamRate:F2} kg/s)");

            var flooded = NewBoiler(1f, lever);
            InjectorRig.TryCreate(null, flooded, out _);
            InjectorRig.TryGet(flooded, out var floodedRig);
            floodedRig.HoseIn = true;
            floodedRig.HoseSteam = 1f;
            flooded.waterLevelReadOut.Value = 0.96f;
            var floodedMass = floodedRig.BoilerVessel.mass;
            Run(floodedRig, 2f);
            var heldOff = floodedRig.SteamFull && !floodedRig.Steaming && floodedRig.BoilerVessel.mass == floodedMass;
            flooded.waterLevelReadOut.Value = 0.93f;
            Run(floodedRig, 2f);
            var stillHeld = floodedRig.SteamFull;
            flooded.waterLevelReadOut.Value = 0.90f;
            Run(floodedRig, 2f);
            Check(heldOff && stillHeld && !floodedRig.SteamFull && floodedRig.Steaming && floodedRig.BoilerVessel.mass > floodedMass,
                $"the plant's steam is held off with the glass at {InjectorRig.SteamFullLevel:P0} and let in again once blown down below it");

            // Two guests on the hand pump: one letting go leaves the other
            // pumping (audit 2026-10-10: one boolean for every player).
            var shared = NewBoiler(1f, lever);
            InjectorRig.TryCreate(null, shared, out _);
            InjectorRig.TryGet(shared, out var sharedRig);
            sharedRig.RemotePump(1, true);
            sharedRig.RemotePump(2, true);
            sharedRig.RemotePump(1, false);
            var sharedMass = sharedRig.BoilerVessel.mass;
            Run(sharedRig, 2f);
            var stillPumped = sharedRig.BoilerVessel.mass > sharedMass;
            sharedRig.RemotePump(2, false);
            sharedRig.RemotePump(2, false);
            var stoppedMass = sharedRig.BoilerVessel.mass;
            Run(sharedRig, 2f);
            Check(stillPumped && !sharedRig.RemoteHandPumping && sharedRig.BoilerVessel.mass <= stoppedMass,
                "two guests on the hand pump: one letting go leaves the other pumping, and it stops when both have");

            // A Worthington SA beside the injector, matched to an engine exhausting 8 kg/s.
            var fed = NewBoiler(12f, lever);
            InjectorRig.TryCreate(null, fed, out _);
            InjectorRig.TryGet(fed, out var pumpRig);
            var valve = new Port("rfFeedPump", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var exhaust = new Port("steamEngine", new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "EXHAUST_FLOW"), 8f);
            var fullExhaust = new Port("steamEngine", new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "MAX_FLOW"), 15f);
            var chest = new Port("steamEngine", new PortDefinition(PortType.READONLY_OUT, PortValueType.PRESSURE, "STEAM_CHEST_PRESSURE"), 12f);
            pumpRig.AttachPump(new FeedPump(new FeedPumpSpec(FeedHeaterType.Open, 18f), InjectorRig.TenderWaterTemperature),
                "Worthington SA", valve, exhaust, fullExhaust, chest);
            valve.Value = 8f / 18f;
            var pumpMass = pumpRig.BoilerVessel.mass;
            var pumpTaken = Run(pumpRig, 400f) / 4f;
            var pumpAdded = pumpRig.BoilerVessel.mass - pumpMass;
            Check(pumpRig.PumpFlow.State == FeedPumpState.Feeding && pumpAdded > 2000f && pumpRig.PumpFlow.FeedTemperature > 95f && pumpTaken < pumpAdded,
                $"the feed pump fills the boiler with heated water ({pumpAdded:F0} kg in 400 s, now at {pumpRig.PumpFlow.FeedTemperature:F0} C, " +
                $"{pumpTaken:F0} kg from the tender, the rest condensed exhaust; pressure {pumpRig.BoilerVessel.pressure:F1} bar)");
            exhaust.Value = 0f;
            chest.Value = 1f;
            valve.Value = 0.25f;
            Run(pumpRig, 5f);
            Check(pumpRig.PumpFlow.State == FeedPumpState.Choked, "with the throttle shut its drifting valve chokes it to half");
        }

        private static void CheckVrShedHoseParts()
        {
            // A shed hose is a copy of a live station hose, stripped of what
            // the game added as it woke; in VR the plug's control (GizmoVRTK)
            // added its interactable in the game's own DV.CabControls.VRTK, so
            // a copy kept the station's, stale and first, beside its own, and
            // the plug shook in the hand and stayed put (Trainguy611 and
            // SeanJAnimations, 2026-10-19 to 26). Not run: no VR here.
            var added = new[]
            {
                typeof(DV.CabControls.VRTK.GizmoVRTK), typeof(DV.CabControls.VRTK.VRTK_ControlImplBaseInteractableObject),
                typeof(VRTK.GrabAttachMechanics.VRTK_ChildOfControllerGrabAttach),
                typeof(VRTK.SecondaryControllerGrabActions.VRTK_SwapControllerGrabAction), typeof(PluggableObjectKinematicVRFix),
                typeof(DV.CabControls.NonVR.GizmoNonVR),
            };
            var kept = new[] { typeof(PluggableObject), typeof(PropHose), typeof(PlugSocket) };
            Check(added.All(Shed.ShedHoses.IsRuntimePart) && !kept.Any(Shed.ShedHoses.IsRuntimePart),
                "a shed hose's copy loses all the plug's VR grab parts the game adds as it wakes, its interactable too, and keeps the hose" +
                string.Concat(added.Where(t => !Shed.ShedHoses.IsRuntimePart(t)).Select(t => "; kept " + t.Name)
                    .Concat(kept.Where(Shed.ShedHoses.IsRuntimePart).Select(t => "; lost " + t.Name))));
        }

        // The fireman takes the glass's tilt off as the game puts it on: the
        // stock boiler's level step at a tilt, against level.
        private static void CheckGlassTilt()
        {
            var def = new BoilerDefinition
            {
                ID = "boiler", diameter = 1.7f, length = 8.8f, capacityMultiplier = 0.85f,
                maxInjectorRate = 20f, waterConsumptionMultiplier = 4f, maxBlowdownRate = 100f,
                safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                spawnPressure = 14f, spawnWaterLevel = 12000f,
                crownSheetNormalizedWaterLevel = 0.6f, steamOutletNormalizedWaterLevel = 0.98f,
            };
            var boiler = (Boiler)def.InstantiateImplementation();
            var level = typeof(Boiler).GetMethod("SimulateWaterLevel", BindingFlags.Instance | BindingFlags.NonPublic);
            const float span = 0.98f - 0.6f;
            float Shift(float angle)
            {
                boiler.angleExtIn.Value = 0f;
                level.Invoke(boiler, new object[] { 0.02f });
                var flat = boiler.waterLevelReadOut.Value;
                boiler.angleExtIn.Value = angle;
                level.Invoke(boiler, new object[] { 0.02f });
                return (boiler.waterLevelReadOut.Value - flat) / span;
            }
            // 2 % front down (the game's angle is the car's pitch, its euler x), then front up.
            var down = (float)(Math.Atan(0.02) * 180.0 / Math.PI);
            var stockDown = Shift(down);
            var oursDown = BoilerWaterRig.GlassTiltNow(boiler, span);
            var stockUp = Shift(360f - down);
            var oursUp = BoilerWaterRig.GlassTiltNow(boiler, span);
            Check(Math.Abs(stockDown - oursDown) < 1e-3f && Math.Abs(stockUp - oursUp) < 1e-3f && stockDown < 0f && stockUp > 0f,
                $"the fireman reads the glass's tilt as the game shifts it (2 % front down {oursDown:F3}, game {stockDown:F3}; front up {oursUp:F3}, game {stockUp:F3})");
            Check(Math.Abs(BoilerWaterRig.GlassPerGrade(boiler, span) * 0.02f - stockUp) < 2e-3f,
                $"a grade ahead turns into the same shift ({BoilerWaterRig.GlassPerGrade(boiler, span) * 0.02f:F3} for 2 %)");
        }

        private static object displayGlassSpan;
        private static MethodInfo displayGlassShow;

        // Supply a learned cab calibration without creating native Unity objects.
        private static bool CalibratedGlassPrefix(float level, ref float __result)
        {
            __result = (float)displayGlassShow.Invoke(displayGlassSpan, new object[] { level });
            return false;
        }

        private static void CheckFiremanGlassDisplay()
        {
            var shown = typeof(FiremanRig).GetProperty("GlassShown");
            var target = typeof(FiremanRig).GetProperty("GlassTargetShown");
            var read = AccessTools.Method(typeof(Visuals.GaugeGlass), "Read", new[] { typeof(TrainCar), typeof(string), typeof(float) });
            Check(shown != null && target != null && read != null,
                "the fireman's displayed glass and target use the shared cab-glass calibration");
            if (shown == null || target == null || read == null)
                return;

            var boiler = (Boiler)new BoilerDefinition
            {
                ID = "boiler", diameter = 1.7f, length = 8.8f,
                crownSheetNormalizedWaterLevel = 0.6f, steamOutletNormalizedWaterLevel = 0.98f,
            }.InstantiateImplementation();
            var rig = (FiremanRig)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(FiremanRig));
            AccessTools.Field(typeof(FiremanRig), "boiler").SetValue(rig, boiler);
            var man = new AutoFireman();
            AccessTools.Field(typeof(FiremanRig), "<CoalFireman>k__BackingField").SetValue(rig, man);
            var injector = (InjectorRig)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(InjectorRig));
            AccessTools.Field(typeof(InjectorRig), "boiler").SetValue(injector, boiler);
            var spanType = typeof(Visuals.GaugeGlass).GetNestedType("Span", BindingFlags.NonPublic);
            displayGlassShow = spanType.GetMethod("Show");
            var harmony = new Harmony("RealisticFiring.AdapterCheck.GlassDisplay");
            var correct = true;
            try
            {
                harmony.Patch(read, prefix: new HarmonyMethod(typeof(Program), nameof(CalibratedGlassPrefix)));
                // JK3 1.0.1's measured glass, LMS 8F, the stock span, and
                // reader modifiers. Compare against the stock indicator itself.
                foreach (var spec in new[] { (0.68f, 0.85f, 1f, 0f, false), (0.70f, 0.95f, 1f, 0f, false),
                    (0.6f, 0.98f, 1f, 0f, false), (0.5f, 0.9f, -2f, 0.5f, true) })
                {
                    var indicator = (IndicatorScaler)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(IndicatorScaler));
                    indicator.minValue = spec.Item1;
                    indicator.maxValue = spec.Item2;
                    var reader = (DV.Simulation.Ports.IndicatorPortReader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DV.Simulation.Ports.IndicatorPortReader));
                    reader.valueMultiplier = spec.Item3;
                    reader.valueOffset = spec.Item4;
                    reader.useAbsoluteValue = spec.Item5;
                    displayGlassSpan = Activator.CreateInstance(spanType, new object[] { indicator, reader });
                    float Expected(float raw)
                    {
                        var value = raw * reader.valueMultiplier + reader.valueOffset;
                        AccessTools.Field(typeof(Indicator), "value").SetValue(indicator, reader.useAbsoluteValue ? Math.Abs(value) : value);
                        return indicator.NormalizedValue;
                    }
                    foreach (var raw in new[] { 0.64f, 0.68f, 0.765f, 0.85f, 0.98f })
                    {
                        boiler.waterLevelReadOut.Value = raw;
                        correct &= Math.Abs((float)shown.GetValue(rig) - Expected(raw)) < 1e-5f
                            && Math.Abs(Visuals.GaugeGlass.Read(injector) - Expected(raw)) < 1e-5f;
                    }
                    foreach (var goal in new[] { 0f, 0.3f, 0.55f, 0.75f, 1f, 1.2f })
                    {
                        AccessTools.Field(typeof(AutoFiremanBase), "<GlassTarget>k__BackingField").SetValue(man, goal);
                        correct &= Math.Abs((float)target.GetValue(rig) - Expected(0.6f + goal * 0.38f)) < 1e-5f
                            && man.GlassTarget == goal;
                    }
                }
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                displayGlassSpan = null;
                displayGlassShow = null;
            }
            Check(correct, "fireman levels and holding targets match the cab/F6 calibration, including JK3, modifiers, and out-of-range targets");
            bool Calls(string name, MethodInfo getter)
            {
                var method = AccessTools.Method(typeof(FiremanPanel), name);
                var iterator = method.GetCustomAttribute<System.Runtime.CompilerServices.IteratorStateMachineAttribute>();
                if (iterator != null)
                    method = AccessTools.Method(iterator.StateMachineType, "MoveNext");
                return PatchProcessor.GetOriginalInstructions(method).Any(i => Equals(i.operand, getter));
            }
            Check(Calls("Read", shown.GetGetMethod()) && Calls("ReadRows", shown.GetGetMethod())
                  && Calls("ReadRows", target.GetGetMethod()) && Calls("Describe", shown.GetGetMethod()) && Calls("Describe", target.GetGetMethod()),
                "the compact fireman HUD, tablet/shared card, detailed rows and descriptions use calibrated glass readings");
        }

        // The boiler water rig on a stock boiler with the S282's crown sheet,
        // its hooks run between the stock steps in tick order as the patches do
        // (the stock damage step needs Unity's curves, so its crown sheet test
        // is read instead).
        private static bool BlockClockSkip() => false;

        private static void CheckBoilerWater()
        {
            var failuresOn = new SimGameParams(true, false, false, 1f, 1f);
            var injector = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var blowdown = new Port("blowdown", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var tenderWater = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 40000f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));
            var heat = new Port("firebox", new PortDefinition(PortType.READONLY_OUT, PortValueType.HEAT_RATE, "HEAT"));
            var fireTemperature = new Port("firebox", new PortDefinition(PortType.READONLY_OUT, PortValueType.TEMPERATURE, "TEMPERATURE"), 1000f);
            var steamDraw = new Port("steam", new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "CONSUMPTION"));

            Boiler NewBoiler(float waterVolume, float spawn = 14f)
            {
                var def = new BoilerDefinition
                {
                    ID = "boiler",
                    diameter = 1.7f,
                    length = 8.8f,
                    capacityMultiplier = 0.85f,
                    maxInjectorRate = 20f,
                    waterConsumptionMultiplier = 4f,
                    maxBlowdownRate = 100f,
                    safetyValveOpeningPressure = 15f,
                    safetyValveClosingPressure = 14.8f,
                    spawnPressure = spawn,
                    spawnWaterLevel = waterVolume,
                    crownSheetNormalizedWaterLevel = 0.6f,
                    crownSheetTempSmoothTime = 60f,
                    crownSheetOverheatTemp = 600f,
                    minimumExplosionPressure = 5f,
                    steamOutletNormalizedWaterLevel = 0.98f,
                };
                var boiler = (Boiler)def.InstantiateImplementation();
                boiler.SetGameParams(failuresOn);
                void Connect(string name, Port port) => boiler.GetAllPortReferences().Single(r => r.id == "boiler." + name).SetPortReference(port);
                Connect("INJECTOR", injector);
                Connect("BLOWDOWN", blowdown);
                Connect("WATER", tenderWater);
                Connect("WATER_CONSUMPTION", consumed);
                Connect("HEAT", heat);
                Connect("FIREBOX_TEMPERATURE", fireTemperature);
                Connect("STEAM_CONSUMPTION", steamDraw);
                return boiler;
            }

            void Call(Boiler boiler, string step, float dt) =>
                typeof(Boiler).GetMethod(step, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(boiler, new object[] { dt });

            void Run(Boiler boiler, BoilerWaterRig rig, float seconds)
            {
                const float dt = 0.02f;
                for (var t = 0f; t < seconds; t += dt)
                {
                    rig.BeginTick(dt);
                    Call(boiler, "SimulateInjector", dt);
                    rig.AfterFeed();
                    Call(boiler, "SimulateSteamConsumption", dt);
                    Call(boiler, "SimulateSafetyValve", dt);
                    var before = rig.MassNow;
                    Call(boiler, "SimulateBlowdown", dt);
                    rig.AfterBlowdown(before);
                    Call(boiler, "SimulateEvaporation", dt);
                    Call(boiler, "SimulateWaterLevel", dt);
                    rig.AfterWaterLevel(dt);
                    rig.SimulateCrownSheet(dt);
                }
            }

            RealisticFiring.Main.Settings.BoilerWaterQuality = FeedWaterQuality.Average;

            // The feed brings solids at the setting's concentration; the blowdown takes them out.
            var fed = NewBoiler(12000f);
            var fedRig = BoilerWaterRig.Create(fed, null, BoilerWaterRig.Guard.None);
            var massBefore = fedRig.MassNow;
            var solidsBefore = fedRig.Water.Solids + fedRig.Water.SuspendedSludge;
            injector.Value = 0.5f;
            Run(fed, fedRig, 10f);
            injector.Value = 0f;
            var added = fedRig.MassNow - massBefore;
            var brought = fedRig.Water.Solids + fedRig.Water.SuspendedSludge + fedRig.Water.SettledSludge - solidsBefore;
            var expected = added * fedRig.Water.FeedConcentration * 1e-6f;
            Check(added > 50f && Math.Abs(brought - expected) < 0.1f * expected,
                $"the feed brings its solids ({added:F0} kg fed, {brought * 1000f:F1} g of solids, {expected * 1000f:F1} g expected)");
            blowdown.Value = 1f;
            var beforeBlow = fedRig.MassNow;
            Run(fed, fedRig, 3f);
            blowdown.Value = 0f;
            Check(beforeBlow - fedRig.MassNow > 200f && fedRig.Water.BlownOut > 0f,
                $"a 3-second blowdown takes the water and its solids ({beforeBlow - fedRig.MassNow:F0} kg, {fedRig.Water.BlownOut * 1000f:F1} g of solids)");

            // Water past foaming shows in the glass and carries water over.
            var foamy = NewBoiler(12000f);
            var foamyRig = BoilerWaterRig.Create(foamy, null, BoilerWaterRig.Guard.None);
            foamyRig.Water.Restore(foamyRig.MassNow * 6000e-6f, 0f, 0f, foamyRig.MassNow);
            Run(foamy, foamyRig, 20f);
            var rise = 0f;
            for (var i = 0; i < 50; i++)
            {
                Run(foamy, foamyRig, 0.1f);
                rise = Math.Max(rise, foamyRig.GlassLevel - foamyRig.TrueLevel);
            }
            var quality = foamy.outletSteamQualityReadOut.Value;
            Check(foamyRig.Water.Foam > 0.1f && rise > 0.01f && quality < 0.99f,
                $"foaming water lifts the glass and wets the steam (foam {foamyRig.Water.Foam:F2}, glass up to {rise:F3} high, steam quality {quality:F3})");

            // A bared crown sheet: the plug melts and saves the sheet; without one it fails.
            var plugged = NewBoiler(8000f);
            var plugRig = BoilerWaterRig.Create(plugged, null, BoilerWaterRig.Guard.FusiblePlug);
            var bare = NewBoiler(8000f);
            var bareRig = BoilerWaterRig.Create(bare, null, BoilerWaterRig.Guard.None);
            // A new boiler's crown sheet starts at 0 C: let it reach the water's
            // temperature under a dead fire before the fire heats the bared sheet.
            fireTemperature.Value = 0f;
            Run(plugged, plugRig, 600f);
            Run(bare, bareRig, 600f);
            fireTemperature.Value = 1000f;

            var pressureBefore = plugRig.BoilerPressure;
            var meltTime = 0f;
            while (!plugRig.Plug.Melted && meltTime < 60f)
            {
                Run(plugged, plugRig, 0.5f);
                meltTime += 0.5f;
            }
            Run(plugged, plugRig, 120f);
            Run(bare, bareRig, 120f + meltTime);
            Check(plugRig.Plug.Melted && meltTime < 15f && !plugRig.CrownSheetWouldFail && plugRig.PlugVent > 0f && plugRig.BoilerPressure < pressureBefore,
                $"bared, the plug melts in {meltTime:F1} s and blows into the firebox ({plugRig.PlugVent:F2} kg/s, {pressureBefore:F1} -> {plugRig.BoilerPressure:F1} bar), " +
                $"holding the crown sheet at {plugRig.CrownSheetTemperatureNow:F0} C");
            Check(bareRig.CrownSheetWouldFail, $"without a plug the stock crown sheet fails ({bareRig.CrownSheetTemperatureNow:F0} C at {bareRig.BoilerPressure:F1} bar)");

            var alarmed = NewBoiler(8000f);
            var alarmRig = BoilerWaterRig.Create(alarmed, null, BoilerWaterRig.Guard.LowWaterAlarm);
            Run(alarmed, alarmRig, 1f);
            var quiet = NewBoiler(12000f);
            var quietRig = BoilerWaterRig.Create(quiet, null, BoilerWaterRig.Guard.LowWaterAlarm);
            Run(quiet, quietRig, 1f);
            Check(alarmRig.Alarm.Sounding && !quietRig.Alarm.Sounding,
                $"the alarm blows at {alarmRig.TrueLevel:F2} of the boiler and not at {quietRig.TrueLevel:F2} (crown sheet at 0.60)");

            // Saved and loaded beside the stock keys; a washout renews it all.
            var data = plugged.GetSaveStateData();
            plugRig.Save(data);
            var loaded = NewBoiler(8000f);
            var loadedRig = BoilerWaterRig.Create(loaded, null, BoilerWaterRig.Guard.FusiblePlug);
            loaded.SetSaveStateData(data);
            BoilerWaterRig.OnLoaded(loaded, data);
            Check(loadedRig.Plug.Melted && Math.Abs(loadedRig.Water.Solids - plugRig.Water.Solids) < 1e-4f,
                "the water and the melted plug save and load with the boiler");
            loadedRig.Washout();
            Check(!loadedRig.Plug.Melted && loadedRig.Water.SettledSludge == 0f && loadedRig.PlugVent == 0f,
                "a washout renews the plug and freshens the water");

            // A pit stop's repair renews a melted plug, as the game renews a
            // burst boiler; the damage loading, a repair short of 95 %, and a
            // repair with the plug whole do not.
            var repairedBoiler = NewBoiler(8000f);
            var repairRig = BoilerWaterRig.Create(repairedBoiler, null, BoilerWaterRig.Guard.FusiblePlug);
            repairRig.MeltPlug();
            blowdown.Value = 1f;
            Run(repairedBoiler, repairRig, 5f);
            blowdown.Value = 0f;
            repairRig.NoteHealth(0.4f, 1f);
            repairRig.NoteHealth(1f, 1f);
            var meltedAtLoad = repairRig.Plug.Melted;
            for (var i = 0; i < 60; i++)
                repairRig.NoteHealth(0.5f, 0.5f);
            repairRig.NoteHealth(0.9f, 0.5f);
            var meltedShort = repairRig.Plug.Melted;
            var drained = repairRig.MassNow;
            repairRig.NoteHealth(0.9f, 1f);
            Run(repairedBoiler, repairRig, 0.02f);
            Check(meltedAtLoad && meltedShort && !repairRig.Plug.Melted && repairRig.PlugVent == 0f && repairRig.MassNow > drained,
                $"a repair of the engine renews a melted plug and refills the boiler ({drained:F0} -> {repairRig.MassNow:F0} kg); the damage loading and a repair short of 95 % do not");
            var whole = NewBoiler(8000f);
            var wholeRig = BoilerWaterRig.Create(whole, null, BoilerWaterRig.Guard.FusiblePlug);
            for (var i = 0; i < 60; i++)
                wholeRig.NoteHealth(0.5f, 1f);
            var wholeMass = wholeRig.MassNow;
            wholeRig.NoteHealth(1f, 1f);
            Check(!wholeRig.Plug.Melted && wholeRig.MassNow == wholeMass, "a repair with the plug whole leaves the boiler as it is");

            // A cold boiler's water is at the air's temperature, not boiling:
            // a fire heats it at the atmosphere's pressure until it boils,
            // where the game's cold boiler raised pressure at once; and the
            // water's temperature saves and loads.
            injector.Value = 0f;
            blowdown.Value = 0f;
            steamDraw.Value = 0f;
            var cold = NewBoiler(8000f, 1f);
            var coldRig = BoilerWaterRig.Create(cold, null, BoilerWaterRig.Guard.None);
            var startTemp = coldRig.WaterTemperature;
            var stock = NewBoiler(8000f, 1f);
            Run(cold, coldRig, 0.02f);
            Call(stock, "SimulateWaterLevel", 0.02f);
            var coldLevel = coldRig.TrueLevel;
            var stockLevel = stock.waterLevelReadOut.Value;
            heat.Value = 20e6f;
            Run(cold, coldRig, 60f);
            for (var t = 0f; t < 60f; t += 0.02f)
                Call(stock, "SimulateEvaporation", 0.02f);
            var warmTemp = coldRig.WaterTemperature;
            var warmPressure = coldRig.BoilerPressure;
            var stockPressure = stock.pressureReadOut.Value;
            var saved = cold.GetSaveStateData();
            coldRig.Save(saved);
            var reloaded = NewBoiler(8000f, 1f);
            var reloadedRig = BoilerWaterRig.Create(reloaded, null, BoilerWaterRig.Guard.None);
            reloaded.SetSaveStateData(saved);
            var stockLoadTemp = reloadedRig.WaterTemperature;
            BoilerWaterRig.OnLoaded(reloaded, saved);
            var loadedTemp = reloadedRig.WaterTemperature;
            var boiledAt = 0f;
            for (var t = 60f; t < 400f && boiledAt == 0f; t += 1f)
            {
                Run(cold, coldRig, 1f);
                if (coldRig.BoilerPressure > 1.05f)
                    boiledAt = t + 1f;
            }
            heat.Value = 0f;
            Check(coldLevel < stockLevel - 0.015f,
                $"cold water stands lower in the glass than boiling: {coldLevel:F3} of the boiler at 25 C, the game's {stockLevel:F3}");
            Check(Math.Abs(startTemp - BoilerWaterRig.AmbientWater) < 1f && warmPressure <= 1.01f && warmTemp > 50f && warmTemp < 70f
                  && stockPressure > 1.5f && boiledAt > 100f && Math.Abs(loadedTemp - warmTemp) < 0.5f && stockLoadTemp > 99f,
                $"a cold boiler starts at {startTemp:F0} C and after 60 s of a 20 MW fire is {warmTemp:F0} C at {warmPressure - 1f:F2} bar on the gauge " +
                $"(the game's cold boiler {stockPressure - 1f:F1} bar), boiling after {boiledAt:F0} s; saved and loaded at {loadedTemp:F0} C (the stock load {stockLoadTemp:F0} C)");

            var started = NewBoiler(8000f, 1f);
            var startedRig = BoilerWaterRig.Create(started, null, BoilerWaterRig.Guard.None);
            Run(started, startedRig, 0.02f);
            var steelBefore = startedRig.Steel.Temperature;
            started.waterChangeRequestedExtIn.ExternalValueUpdate(3f);
            startedRig.Washout();
            var startedAt = startedRig.BoilerPressure;
            Run(started, startedRig, 60f);
            Check(steelBefore < 30f && startedAt > 12.5f && startedRig.BoilerPressure > startedAt - 0.3f
                  && Math.Abs(startedRig.Steel.Temperature - startedRig.WaterTemperature) < 2f,
                $"a cold engine started up holds its {startedAt - 1f:F1} bar: {startedRig.BoilerPressure - 1f:F1} bar a minute on, " +
                $"its steel {startedRig.Steel.Temperature:F0} C (was {steelBefore:F0}) with its water at {startedRig.WaterTemperature:F0} C");

            // The steel gives its heat back as the pressure falls: drawing
            // 5 kg/s for a minute with no fire, the pressure falls less than
            // in the game's boiler of water alone. And the falling pressure
            // flashes steam in the water: the glass stands above the water,
            // and settles back once the pressure rises again.
            void RunStock(Boiler boiler, float seconds)
            {
                const float dt = 0.02f;
                for (var t = 0f; t < seconds; t += dt)
                {
                    Call(boiler, "SimulateSteamConsumption", dt);
                    Call(boiler, "SimulateSafetyValve", dt);
                    Call(boiler, "SimulateEvaporation", dt);
                    Call(boiler, "SimulateWaterLevel", dt);
                }
            }
            var steeled = NewBoiler(8000f);
            var steeledRig = BoilerWaterRig.Create(steeled, null, BoilerWaterRig.Guard.None);
            var waterOnly = NewBoiler(8000f);
            Run(steeled, steeledRig, 1f);
            RunStock(waterOnly, 1f);
            var steeledStart = steeledRig.BoilerPressure;
            var bareStart = waterOnly.pressureReadOut.Value;
            steamDraw.Value = 5f;
            Run(steeled, steeledRig, 30f);
            var flashing = steeled.waterLevelReadOut.Value - steeledRig.TrueLevel;
            Run(steeled, steeledRig, 30f);
            RunStock(waterOnly, 60f);
            steamDraw.Value = 0f;
            var steeledDrop = steeledStart - steeledRig.BoilerPressure;
            var bareDrop = bareStart - waterOnly.pressureReadOut.Value;
            heat.Value = 20e6f;
            Run(steeled, steeledRig, 30f);
            heat.Value = 0f;
            var settled = steeled.waterLevelReadOut.Value - steeledRig.TrueLevel;
            // Soot and scale keep heat from the water; a repair of the
            // engine cleans the share of its wear repaired.
            var clean = NewBoiler(8000f, 1f);
            var cleanRig = BoilerWaterRig.Create(clean, null, BoilerWaterRig.Guard.None);
            var fouled = NewBoiler(8000f, 1f);
            var fouledRig = BoilerWaterRig.Create(fouled, null, BoilerWaterRig.Guard.None);
            fouledRig.Fouling.Restore(0f, 0.1f);
            heat.Value = 20e6f;
            Run(clean, cleanRig, 60f);
            Run(fouled, fouledRig, 60f);
            heat.Value = 0f;
            var cleanRise = cleanRig.WaterTemperature - BoilerWaterRig.AmbientWater;
            var fouledRise = fouledRig.WaterTemperature - BoilerWaterRig.AmbientWater;
            for (var i = 0; i < 60; i++)
                fouledRig.NoteHealth(1f, 0.8f);
            fouledRig.NoteHealth(1f, 0.9f);
            var halfRepaired = fouledRig.Fouling.SootLoss;
            fouledRig.NoteHealth(1f, 1f);
            Check(fouledRise < 0.93f * cleanRise && Math.Abs(halfRepaired - 0.05f) < 0.005f && fouledRig.Fouling.SootLoss < 1e-4f,
                $"tubes sooted 10 % warm a cold boiler {fouledRise:F1} K in a minute against {cleanRise:F1} K clean; repairing half the engine's wear " +
                $"leaves {halfRepaired:P0} soot, the rest none");
            Check(steeledDrop < 0.95f * bareDrop && flashing > 0.01f && settled < 0.002f,
                $"drawing 5 kg/s for a minute with no fire the pressure falls {steeledDrop:F2} bar with the steel, {bareDrop:F2} with water alone; " +
                $"falling, the glass stands {flashing:F3} of the boiler above the water, rising again {settled:F3}");

            // A hot boiler blown down dry and filled from the shed's hot-water
            // hose doesn't burst: the steel gives the first water in no more
            // heat than it can take (Progamer0totally and Enchestor, 2026-10-26:
            // an S282 with no fire or pressure blew up as the hot water went in).
            var vesselField = typeof(Boiler).GetField("vessel", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var opening in new[] { 1f, 0.1f })
            {
                var washed2 = NewBoiler(12000f, 13f);
                var washedRig2 = BoilerWaterRig.Create(washed2, null, BoilerWaterRig.Guard.None);
                fireTemperature.Value = 190f;
                heat.Value = 0f;
                var peakPressure = 0f;
                var peakCrown = 0f;
                var peakWater = 0f;
                var lowWater = float.MaxValue;
                var burst = false;
                void Watch()
                {
                    peakPressure = Math.Max(peakPressure, washedRig2.BoilerPressure);
                    peakCrown = Math.Max(peakCrown, washedRig2.CrownSheetTemperatureNow);
                    if (washedRig2.MassNow > 0f)
                    {
                        peakWater = Math.Max(peakWater, washedRig2.WaterTemperature);
                        lowWater = Math.Min(lowWater, washedRig2.WaterTemperature);
                    }
                    burst |= washedRig2.CrownSheetWouldFail || washedRig2.BoilerPressure > 15f * 1.5f
                        || float.IsNaN(washedRig2.BoilerPressure) || float.IsInfinity(washedRig2.BoilerPressure);
                }
                blowdown.Value = 1f;
                for (var t = 0f; t < 600f && washedRig2.MassNow > 0f; t += 1f)
                {
                    Run(washed2, washedRig2, 1f);
                    Watch();
                }
                blowdown.Value = 0f;
                var drainedDry = washedRig2.MassNow <= 0f;
                var steelWhenDry = washedRig2.Steel.Temperature;
                for (var t = 0f; t < 60f; t += 0.02f)
                {
                    const float dt = 0.02f;
                    washedRig2.BeginTick(dt);
                    var vessel = (WaterPressureVessel)vesselField.GetValue(washed2);
                    vessel.AddWater(InjectorRig.HoseRate * opening * dt, InjectorRig.HotWaterTemperature);
                    washedRig2.AfterFeed();
                    Call(washed2, "SimulateSteamConsumption", dt);
                    Call(washed2, "SimulateSafetyValve", dt);
                    Call(washed2, "SimulateEvaporation", dt);
                    Call(washed2, "SimulateWaterLevel", dt);
                    washedRig2.AfterWaterLevel(dt);
                    washedRig2.SimulateCrownSheet(dt);
                    Watch();
                }
                fireTemperature.Value = 1000f;
                Check(drainedDry && !burst && peakWater < 250f && lowWater > 0f,
                    $"a hot boiler blown dry (steel {steelWhenDry:F0} C) and filled from the shed's hot-water hose {opening:P0} open for a minute doesn't burst: " +
                    $"water {lowWater:F0} to {peakWater:F0} C, crown sheet up to {peakCrown:F0} C, pressure up to {peakPressure:F1} bar" +
                    (drainedDry ? "" : $" (not drained: {washedRig2.MassNow:F0} kg left)"));
            }

            // Exercise the hose pressure interlock, not direct AddWater:
            // after a drained, unheated boiler cools, the real hose must
            // reach its fill stop without needing blowdown or a restart.
            foreach (var hotWater in new[] { false, true })
            {
                var filling = NewBoiler(12000f, 13f);
                var waterRig = BoilerWaterRig.Create(filling, null, BoilerWaterRig.Guard.None);
                var vessel = (WaterPressureVessel)vesselField.GetValue(filling);
                vessel.RemoveWater(vessel.mass);
                vessel.Update();
                Call(filling, "SimulateWaterLevel", 0.1f);
                waterRig.Steel.Settle(192f);
                heat.Value = 0f;
                fireTemperature.Value = 25f;
                blowdown.Value = injector.Value = steamDraw.Value = 0f;
                waterRig.CoolEmpty(8f * 3600f);
                var cooled = waterRig.Steel.Temperature;
                InjectorRig.TryCreate(null, filling, out _);
                InjectorRig.TryGet(filling, out var hose);
                hose.HoseIn = true;
                hose.HoseOpening = 0.5f;
                hose.HoseHot = hotWater;
                var mostPressure = 0f;
                for (var i = 0; i < 24000 && filling.waterLevelReadOut.Value < InjectorRig.HoseFillLevel; i++)
                {
                    const float dt = 0.1f;
                    waterRig.BeginTick(dt);
                    hose.Simulate(dt);
                    waterRig.AfterFeed();
                    Call(filling, "SimulateSteamConsumption", dt);
                    Call(filling, "SimulateEvaporation", dt);
                    Call(filling, "SimulateWaterLevel", dt);
                    waterRig.AfterWaterLevel(dt);
                    mostPressure = Math.Max(mostPressure, vessel.pressure);
                }
                hose.Simulate(0.1f);
                Check(cooled < 50f && filling.waterLevelReadOut.Value >= InjectorRig.HoseFillLevel
                      && !hose.HoseFilling && mostPressure <= InjectorRig.HoseMaxPressure,
                    $"a hot drained boiler cools to {cooled:F1} C and the actual {(hotWater ? "hot" : "cold")} hose at half opening reaches its fill stop " +
                    $"({filling.waterLevelReadOut.Value:P1}, peak {mostPressure:F2} bar absolute) without blowdown");
            }
            // Multiplayer can veto a clock skip. Exercise Harmony's real
            // skip-original path: our postfix must not cool anything then.
            var blocked = NewBoiler(12000f, 13f);
            var blockedRig = BoilerWaterRig.Create(blocked, null, BoilerWaterRig.Guard.None);
            var blockedVessel = (WaterPressureVessel)vesselField.GetValue(blocked);
            blockedVessel.RemoveWater(blockedVessel.mass);
            blockedVessel.Update();
            heat.Value = 0f;
            blockedRig.Steel.Settle(192f);
            var clockPatch = new Harmony("RealisticFiring.AdapterCheck.BlockedClockSkip");
            try
            {
                clockPatch.CreateClassProcessor(typeof(EmptyBoilerTimeAdvancePatch)).Patch();
                clockPatch.Patch(AccessTools.Method(typeof(DV.TimeAdvance), nameof(DV.TimeAdvance.AdvanceTime)),
                    prefix: new HarmonyMethod(typeof(Program), nameof(BlockClockSkip)) { priority = Priority.First });
                DV.TimeAdvance.AdvanceTime(8f * 3600f, true);
                Check(blockedRig.Steel.Temperature == 192f,
                    "a clock skip vetoed by another Harmony prefix does not cool a drained boiler");
            }
            finally
            {
                clockPatch.UnpatchAll(clockPatch.Id);
            }
            fireTemperature.Value = 1000f;
        }

        // An oil burner: the oil firebox has the stock firebox's contract, the
        // oil feed CCL's stoker's; lit waste and oil with the atomizer light the
        // fire, the tender pays at the stoker's multiplier, and the burner's
        // steam comes from the boiler.
        private static void CheckOilBurner(SimGameParams gameParams)
        {
            var fireboxDef = new FireboxDefinition { ID = Id, maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f, startupMaxPressure = 22f };
            var stock = new Firebox(fireboxDef);
            var oil = new RealisticOilFirebox(fireboxDef, null);

            // Oil pooled in the pan and its vapor survive a save, and old
            // saves keep the pooled oil they wrote under the stock coal key
            // (audit 2026-10-10: both came back zero).
            var pooled = new RealisticOilFirebox(fireboxDef, null);
            var hazard = pooled.Model.Snapshot();
            hazard.PooledOil = 3f;
            hazard.Vapor = 1f;
            pooled.Model.Mirror(hazard);
            var oilSave = pooled.GetSaveStateData();
            var reloaded = new RealisticOilFirebox(fireboxDef, null);
            reloaded.SetSaveStateData(oilSave);
            var oldSave = new RealisticOilFirebox(fireboxDef, null);
            oldSave.SetSaveStateData(new JObject { ["coalMass"] = 2f });
            Check(reloaded.Model.PooledOil == 3f && reloaded.Model.Vapor == 1f && oldSave.Model.PooledOil == 2f && oldSave.Model.Vapor == 0f,
                $"pooled oil and vapor survive a save ({reloaded.Model.PooledOil:F0} and {reloaded.Model.Vapor:F0} kg), and an old save keeps its pooled oil");
            oil.SetGameParams(gameParams);
            Check(Describe(stock) == Describe(oil) && stock.id == oil.id && oil.HasSaveData,
                $"the oil firebox keeps the stock firebox's ID, ports, and references ({oil.GetAllPorts().Count} ports, {oil.GetAllPortReferences().Count} references)" +
                Difference(Describe(stock), Describe(oil)));

            var dv = Environment.GetEnvironmentVariable("DV_INSTALL_DIR")
                     ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local/share/Steam/steamapps/common/Derail Valley");
            var importer = Path.Combine(dv, "Mods", "DVCustomCarLoader", "CCL.Importer.dll");
            if (!File.Exists(importer))
            {
                Console.WriteLine("skip oil feed checks: Custom Car Loader is not installed");
                return;
            }
            var cclDefType = Assembly.LoadFrom(importer).GetType(OilBurnerFit.StokerTypeName);
            var cclDef = (SimComponentDefinition)Activator.CreateInstance(cclDefType);
            cclDef.ID = "autostoker";
            var ccl = cclDef.InstantiateImplementation();
            var feed = new OilFeed(StokerDefinitionPatch.Read(cclDef), null, 0.5f, steamWired: false);
            feed.SetGameParams(gameParams);
            Check(Describe(ccl) == Describe(feed) && ccl.id == feed.id,
                $"the oil feed keeps CCL's stoker's ID, ports, and references ({feed.GetAllPorts().Count} ports, {feed.GetAllPortReferences().Count} references)");

            var valve = new Port("stokerControl", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var tender = new Port("tenderCoal", new PortDefinition(PortType.READONLY_OUT, PortValueType.FUEL, "AMOUNT"), 5000f);
            var consume = new Port("tenderCoal", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.FUEL, "CONSUME_EXT_IN"));
            var atomizer = new Port(OilBurnerFit.Atomizer.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 0.3f);
            feed.GetAllPortReferences().Single(r => r.id == "autostoker.CONTROL").SetPortReference(valve);
            feed.GetAllPortReferences().Single(r => r.id == "autostoker.COAL_AMOUNT").SetPortReference(tender);
            feed.GetAllPortReferences().Single(r => r.id == "autostoker.COAL_CONSUMPTION").SetPortReference(consume);
            void Feed(string name, float value) =>
                oil.GetAllPortReferences().Single(r => r.id == $"{Id}.{name}").SetPortReference(
                    new Port("src", new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, name), value));
            // The draft a well-set fire of 0.92 kg/s wants: its design excess air.
            Feed("AIR_FLOW", 0.92f * FireModel.AirPerCoal);
            Feed("BOILER_PRESSURE", 15f);
            Feed("BOILER_TEMPERATURE", 198f);
            // Moving, so the stock steam-raising speedup (it burns fuel and
            // makes heat faster at a stand) stays out of the comparison.
            Feed("FORWARD_SPEED", 10f);
            oil.Feed = feed;
            oil.AttachPorts(null, atomizer, null);

            P(oil, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            valve.Value = 0.4f;
            float tenderDrawn = 0f;
            for (var t = 0f; t < 30f; t += 0.02f)
            {
                consume.Value = 0f;
                oil.Tick(0.02f);
                feed.Tick(0.02f);
                tenderDrawn += consume.Value;
            }
            var burned = oil.Model.FeedRate;
            Check(P(oil, "FIRE_ON").Value == 1f && P(oil, "HEAT").Value > 1e6f && oil.Model.SootDensity < 0.2f,
                $"lit waste and oil with the atomizer light the fire ({P(oil, "HEAT").Value / 1e6f:F1} MW at {burned:F2} kg/s, soot {oil.Model.SootDensity:F2}, " +
                $"excess air {oil.Model.ExcessAir:F2}, atomizing {oil.Model.Atomizing:F2})");
            var feedSteam = feed.GetAllPorts().Single(p => p.id == "autostoker.STEAM_CONSUMPTION").Value;
            var fed = oil.Model.FeedRate * 30f;
            Check(tenderDrawn > 0f && Math.Abs(tenderDrawn - 0.5f * fed) < 0.25f * fed && feedSteam > 0f,
                $"the tender pays at the stoker's multiplier ({tenderDrawn:F1} units for about {fed:F1} kg) and the burner takes steam ({feedSteam:F3} kg/s)");

            var data = oil.GetSaveStateData();
            var loaded = new RealisticOilFirebox(fireboxDef, null);
            loaded.SetSaveStateData(data);
            // The oil fireman's mode is saved as the coal fireman's is, and
            // waits for his cab to be set up.
            var firemanSave = (JObject)data.DeepClone();
            firemanSave[RealisticFirebox.FiremanSaveKey] = (int)FiremanMode.Road;
            var manned = new RealisticOilFirebox(fireboxDef, null);
            manned.SetSaveStateData(firemanSave);
            Check(data[RealisticFirebox.FiremanSaveKey] == null && manned.SavedFiremanMode == FiremanMode.Road
                  && manned.GetSaveStateData().Value<int>(RealisticFirebox.FiremanSaveKey) == (int)FiremanMode.Road,
                $"the oil fireman's mode saves and loads ({manned.SavedFiremanMode}), and none is written while he is off");
            Check(loaded.Model.FireOn && Math.Abs(loaded.Model.BrickTemperature - oil.Model.BrickTemperature) < 1f
                  && Math.Abs(loaded.Model.OilTemperature - oil.Model.OilTemperature) < 0.1f,
                $"the oil fire saves and loads (brick {oil.Model.BrickTemperature:F0} C, oil {oil.Model.OilTemperature:F0} C)");

            // A dead engine (1 bar) has no steam to atomize its oil until a
            // shed's steam line is hooked up, whose steam is not the boiler's.
            var dead = new RealisticOilFirebox(fireboxDef, null);
            dead.SetGameParams(gameParams);
            void DeadFeed(string name, float value) =>
                dead.GetAllPortReferences().Single(r => r.id == $"{Id}.{name}").SetPortReference(
                    new Port("src", new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, name), value));
            DeadFeed("AIR_FLOW", 0.92f * FireModel.AirPerCoal);
            DeadFeed("BOILER_PRESSURE", 1f);
            DeadFeed("BOILER_TEMPERATURE", 25f);
            DeadFeed("FORWARD_SPEED", 0f);
            dead.Feed = feed;
            var heater = new Port(OilBurnerFit.Heater.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 1f);
            dead.AttachPorts(null, atomizer, heater);
            valve.Value = 0f;
            for (var t = 0f; t < 1f; t += 0.02f)
                dead.Tick(0.02f);
            var coldOil = dead.Model.OilTemperature;
            P(dead, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            valve.Value = 0.4f;
            for (var t = 0f; t < 10f; t += 0.02f)
                dead.Tick(0.02f);
            var unlit = P(dead, "FIRE_ON").Value;
            var pooledCold = dead.Model.PooledOil;
            // On a stand's line, the heater warms the oil for ten minutes, then the fire is lit.
            RealisticOilFirebox.HouseLineOf = (_, _) => 1f;
            valve.Value = 0f;
            for (var t = 0f; t < 600f; t += 0.02f)
                dead.Tick(0.02f);
            var warmed = dead.Model.OilTemperature;
            P(dead, "IGNITION_EXT_IN").ExternalValueUpdate(1f);
            valve.Value = 0.4f;
            for (var t = 0f; t < 10f; t += 0.02f)
            {
                dead.Tick(0.02f);
                feed.Tick(0.02f);
            }
            var lineSteam = feed.GetAllPorts().Single(p => p.id == "autostoker.STEAM_CONSUMPTION").Value;
            Check(coldOil < 20f && unlit == 0f && pooledCold == 0f && P(dead, "FIRE_ON").Value == 1f && dead.Model.OnSteamLine && lineSteam == 0f,
                $"a dead engine's cold oil will not run and it will not light; on a shed's steam line its heater warms the oil " +
                $"({coldOil:F0} -> {warmed:F0} C in ten minutes) and it lights without drawing the boiler's steam " +
                $"({dead.Model.FeedRate:F2} kg/s of oil, atomizing {dead.Model.Atomizing:F2})");

            // The cab's shop steam valve alone puts no line on the burner; a stand's line does, as far as its valve is open.
            var valved = new RealisticOilFirebox(fireboxDef, null);
            valved.SetGameParams(gameParams);
            foreach (var reference in valved.GetAllPortReferences())
                reference.SetPortReference(new Port("src", new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "OUT"),
                    reference.id.EndsWith("BOILER_PRESSURE") ? 1f : 0f));
            var shopSteam = new Port(OilBurnerFit.ShopSteam.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 1f);
            valved.AttachPorts(null, atomizer, heater, shopSteam);
            RealisticOilFirebox.HouseLineOf = (_, _) => 0f;
            for (var t = 0f; t < 2f; t += 0.02f)
                valved.Tick(0.02f);
            var cabOnly = valved.Model.OnSteamLine;
            RealisticOilFirebox.HouseLineOf = (_, _) => 0.5f;
            for (var t = 0f; t < 2f; t += 0.02f)
                valved.Tick(0.02f);
            Check(!cabOnly && valved.Model.OnSteamLine && Math.Abs(valved.SteamLineOpening - 0.5f) < 1e-4f,
                $"the cab's shop steam valve alone puts no line on the burner; a stand's line does, as far as its valve is open ({valved.SteamLineOpening:P0})");
            RealisticOilFirebox.HouseLineOf = (car, exhaust) => exhaust != null ? RealisticFiring.Shed.ShedConnections.BlowerLine(exhaust) : 0f;

            // The firing valve's stop: joined to its firebox, the feed never
            // gives the oil less than the stop, and the stop saves.
            var stopped = new RealisticOilFirebox(fireboxDef, null);
            feed.Join(stopped);
            valve.Value = 0f;
            stopped.Stop.Toggle(0.2f);
            var held = feed.FiringValve;
            var stopLoaded = new RealisticOilFirebox(fireboxDef, null);
            stopLoaded.SetSaveStateData(stopped.GetSaveStateData());
            stopped.Stop.Toggle(0f);
            Check(stopped.Feed == feed && Math.Abs(held - 0.2f) < 1e-4f && Math.Abs(stopLoaded.Stop.At - 0.2f) < 1e-4f && feed.FiringValve == 0f,
                $"with the firing valve's stop in, the lever shut gives {held:P0}, the stop saves and loads ({stopLoaded.Stop.At:P0}), and out it shuts");
        }

        // What a multiplayer host sends clients' views of a loco: captured from
        // the real parts and shown on fresh ones, it reads back the same.
        private static (Boiler Boiler, ExternalControl Right, ExternalControl Left, SimComponent[] Components, Port Water, Port Consumed)
            Jk3InjectorSetup(SimGameParams gameParams, float divisorValue = 0.5f, bool negative = false, bool invert = false, bool swap = false)
        {
            var right = new ExternalControl(new ExternalControlDefinition { ID = "injector", saveState = true });
            var left = new ExternalControl(new ExternalControlDefinition { ID = "injector2", saveState = true });
            var add = new ConfigurableAdd(new ConfigurableAddDefinition
            {
                ID = "injectorAdd", negativeA = negative,
                aReader = new PortReferenceDefinition(PortValueType.GENERIC, "INJECTOR1"),
                bReader = new PortReferenceDefinition(PortValueType.GENERIC, "INJECTOR2"),
                addReadOut = new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "INJECTOR_X2"),
            });
            add.aReader.SetPortReference(swap ? left.controlExtIn : right.controlExtIn);
            add.bReader.SetPortReference(swap ? right.controlExtIn : left.controlExtIn);
            var divisor = new ConfigurablePorts(new ConfigurablePortsDefinition
            {
                ID = "injectorDivisor", ports = new[] { new PortDefinition(PortType.IN, PortValueType.GENERIC, "DIVISOR") },
                startingValues = new[] { divisorValue },
            });
            var normalized = new ConfigurableMultiplier(new ConfigurableMultiplierDefinition
            {
                ID = "injectorNormalized", invertB = invert,
                aReader = new PortReferenceDefinition(PortValueType.GENERIC, "INJECTOR_X2"),
                bReader = new PortReferenceDefinition(PortValueType.GENERIC, "DIVISOR"),
                mulReadOut = new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "INJECTOR_NORMALIZED"),
            });
            normalized.aReader.SetPortReference(add.addReadOut);
            normalized.bReader.SetPortReference(divisor.ports[0]);
            var particles = new ConfigurablePorts(new ConfigurablePortsDefinition
            {
                ID = "injectorParticles", startingValues = new[] { 0f, 0f },
                ports = new[]
                {
                    new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "INJECTOR_R_PARTICLES_CLAMP"),
                    new PortDefinition(PortType.READONLY_OUT, PortValueType.GENERIC, "INJECTOR_L_PARTICLES_CLAMP"),
                },
            });
            var boiler = new Boiler(new BoilerDefinition
            {
                ID = "boiler", diameter = 1.33f, length = 4.7f, capacityMultiplier = 0.85f,
                maxInjectorRate = 8f, waterConsumptionMultiplier = 1.5f,
                safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.7f,
                spawnPressure = 15f, spawnWaterLevel = 4000f,
            });
            boiler.SetGameParams(gameParams);
            boiler.injectorControl.SetPortReference(normalized.mulReadOut);
            var water = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 8250f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));
            boiler.water.SetPortReference(water);
            boiler.waterConsumption.SetPortReference(consumed);
            return (boiler, right, left, new SimComponent[] { divisor, right, left, add, normalized, particles, boiler }, water, consumed);
        }

        // Emerald's LMS 8F 1.2.1, as its bundle wires it: injector1 (left) and
        // injector2 (right) added, halved by a constant port, into the
        // boiler's injector, the halved output typed as a cab control.
        private static (Boiler Boiler, SimComponent[] Components)
            Lms8FInjectorSetup(SimGameParams gameParams, float factorValue = 0.5f)
        {
            var left = new ExternalControl(new ExternalControlDefinition { ID = "injector1", saveState = true });
            var right = new ExternalControl(new ExternalControlDefinition { ID = "injector2", saveState = true });
            var add = new ConfigurableAdd(new ConfigurableAddDefinition
            {
                ID = "injectoradding",
                aReader = new PortReferenceDefinition(PortValueType.CONTROL, "injector1"),
                bReader = new PortReferenceDefinition(PortValueType.CONTROL, "injector2"),
                addReadOut = new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "injectoradd"),
            });
            add.aReader.SetPortReference(left.controlExtIn);
            add.bReader.SetPortReference(right.controlExtIn);
            var factor = new ConfigurablePort(new ConfigurablePortDefinition
            {
                ID = "injector_multiplier", value = factorValue,
                port = new PortDefinition(PortType.EXTERNAL_IN, PortValueType.GENERIC, "multiplier"),
            });
            var calc = new ConfigurableMultiplier(new ConfigurableMultiplierDefinition
            {
                ID = "injector_calc",
                aReader = new PortReferenceDefinition(PortValueType.GENERIC, "injectoradd"),
                bReader = new PortReferenceDefinition(PortValueType.GENERIC, "multiplier"),
                mulReadOut = new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "injectorhalf"),
            });
            calc.aReader.SetPortReference(add.addReadOut);
            calc.bReader.SetPortReference(factor.GetAllPorts().Single());
            var boiler = new Boiler(new BoilerDefinition
            {
                ID = "boiler", diameter = 1.737f, length = 5.467f, capacityMultiplier = 0.85f,
                maxInjectorRate = 20f, waterConsumptionMultiplier = 2.5f,
                safetyValveOpeningPressure = 15.5f, safetyValveClosingPressure = 14.5f,
                spawnPressure = 15f, spawnWaterLevel = 9000f,
            });
            boiler.SetGameParams(gameParams);
            boiler.injectorControl.SetPortReference(calc.mulReadOut);
            var water = new Port("tenderWater", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 18000f);
            var consumed = new Port("tenderWater", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));
            boiler.water.SetPortReference(water);
            boiler.waterConsumption.SetPortReference(consumed);
            return (boiler, new SimComponent[] { left, right, add, factor, calc, boiler });
        }

        private static void CheckLms8FInjectors(SimGameParams gameParams)
        {
            var setup = Lms8FInjectorSetup(gameParams);
            var hand = InjectorRig.FiremanControl(setup.Boiler, "em_lms_8f_loc_black", setup.Components);
            var changedFallback = Lms8FInjectorSetup(gameParams, factorValue: 1f);
            Check(hand != null && hand.id == "injector2.EXT_IN"
                  && InjectorRig.FiremanControl(changedFallback.Boiler, "em_lms_8f_loc_black", changedFallback.Components) == null,
                "the stock-injector 8F fireman uses the verified right cab valve even though its calculated average is typed as a control");
            var made = InjectorRig.TryCreate(null, setup.Boiler, "em_lms_8f_loc_black", setup.Components, out var note);
            InjectorRig.TryGet(setup.Boiler, out var rig);
            Check(made && rig != null && rig.Circuits.Count == 2
                  && rig.Circuits[0].Control.id == "injector2.EXT_IN" && rig.Circuits[1].Control.id == "injector1.EXT_IN",
                $"the LMS 8F's two injectors are each worked on their own, the fireman's right first ({note})");
            // With the water regulators added, the cab's own levers are the steam valves.
            var fitted = Lms8FInjectorSetup(gameParams);
            var waterRight = new ExternalControl(new ExternalControlDefinition { ID = InjectorValves.Water.Id, saveState = true });
            var waterLeft = new ExternalControl(new ExternalControlDefinition { ID = InjectorValves.WaterLeft.Id, saveState = true });
            InjectorRig.TryCreate(null, fitted.Boiler, "em_lms_8f_loc_black", fitted.Components.Concat(new SimComponent[] { waterRight, waterLeft }), out _);
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
            if (right != null && right.OwnLeverIsSteam && right.SteamValve == steamLever && right.WaterValve == waterRight.controlExtIn)
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
            Check(opened, "the LMS 8F's own lever is the steam valve and the added regulator the water: water runs at the overflow, then it primes and feeds");
            // The tender's water valve to it, shut: no water, whatever the regulator.
            var starvedThenOpened = false;
            if (opened)
            {
                var tenderValve = new Port(InjectorValves.TenderWater.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 0f);
                right.TenderValve = tenderValve;
                var starved = Step(1f);
                starvedThenOpened = right.TenderValveShut && starved.State == InjectorState.NoWater && starved.WaterIn == 0f
                                    && swapped.OpenTenderValve(0) && tenderValve.Value >= 1f && !right.TenderValveShut;
            }
            Check(starvedThenOpened, "the LMS 8F tender's water valve shut starves its injector, and the fireman opens it");
            var changed = Lms8FInjectorSetup(gameParams, factorValue: 1f);
            InjectorRig.TryCreate(null, changed.Boiler, "em_lms_8f_loc_grey", changed.Components, out _);
            Check(!InjectorRig.TryGet(changed.Boiler, out var single) || single.Circuits.Count == 1,
                "a changed LMS 8F injector calculation is not taken for its two injectors");
        }

        // An engine fitted with its own injector steam valve: the stock lever
        // is the water valve, the new control shut, primed, or wide open.
        private static void CheckInjectorSteamValve(SimGameParams gameParams)
        {
            var water = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var tenderWater = new Port("tender", new PortDefinition(PortType.READONLY_OUT, PortValueType.WATER, "AMOUNT"), 40000f);
            var consumed = new Port("tender", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.WATER, "CONSUME_EXT_IN"));
            var steamControl = (ExternalControl)new ExternalControlDefinition { ID = InjectorValves.Steam.Id }.InstantiateImplementation();
            var def = new BoilerDefinition
            {
                ID = "boiler", diameter = 1.7f, length = 8.8f, capacityMultiplier = 0.85f, maxInjectorRate = 5f,
                waterConsumptionMultiplier = 4f, safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                spawnPressure = 13f, spawnWaterLevel = 9000f,
            };
            var boiler = (Boiler)def.InstantiateImplementation();
            boiler.SetGameParams(gameParams);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.INJECTOR").SetPortReference(water);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER").SetPortReference(tenderWater);
            boiler.GetAllPortReferences().Single(r => r.id == "boiler.WATER_CONSUMPTION").SetPortReference(consumed);
            var made = InjectorRig.TryCreate(null, boiler, "LocoS060", new SimComponent[] { boiler, steamControl }, out var note);
            InjectorRig.TryGet(boiler, out var rig);
            var steam = steamControl.controlExtIn;
            InjectorFlow Run(float steamAt, float waterAt, float seconds)
            {
                steam.Value = steamAt;
                water.Value = waterAt;
                for (var t = 0f; t < seconds; t += 0.02f)
                {
                    consumed.Value = 0f;
                    rig.Simulate(0.02f);
                    rig.BoilerVessel.Update();
                }
                return rig.Circuits[0].Flow;
            }
            if (!made || rig == null || rig.Circuits[0].Added == null)
            {
                Check(false, $"the S060 gets its injector steam valve ({note})");
                return;
            }
            var gravity = Run(0f, 1f, 1f);
            Run(InjectorSpec.SteamPrime, 1f, 3f);
            var full = Run(1f, 1f, 2f);
            var part = Run(1f, 0.6f, 2f);
            Check(gravity.State == InjectorState.Off && gravity.Overflow > 0f && full.State == InjectorState.Working
                  && part.State == InjectorState.Working && Math.Abs(part.WaterIn / full.WaterIn - 0.6f) < 0.05f,
                $"with its own steam valve the S060's water runs from the overflow until it is primed ({gravity.Overflow:F2} kg/s), " +
                $"then feeds as its water valve sets ({part.WaterIn:F2} kg/s at 60 %, {full.WaterIn:F2} wide open)");
        }

        private static void CheckJk3Injectors(SimGameParams gameParams)
        {
            InjectorRig Rig((Boiler Boiler, ExternalControl Right, ExternalControl Left, SimComponent[] Components, Port Water, Port Consumed) setup)
            {
                InjectorRig.TryCreate(null, setup.Boiler, "Jk3A", setup.Components, out _);
                InjectorRig.TryGet(setup.Boiler, out var rig);
                rig?.EndResume();
                return rig;
            }
            void Tick(InjectorRig rig, float seconds)
            {
                for (var i = 0; i < (int)(seconds / 0.02f); i++)
                    rig.Simulate(0.02f);
            }
            var setup = Jk3InjectorSetup(gameParams);
            var stockHand = InjectorRig.FiremanControl(setup.Boiler, "Jk3A", setup.Components);
            var changed = Jk3InjectorSetup(gameParams, divisorValue: 1f);
            Check(ReferenceEquals(stockHand, setup.Right.controlExtIn)
                  && InjectorRig.FiremanControl(changed.Boiler, "Jk3A", changed.Components) == null,
                "without a realistic injector rig the JK3 fireman reaches the verified right cab valve, never its read-only average or a changed graph");
            Check(!InjectorRig.TryCreate(null, setup.Boiler, out _)
                  && !InjectorRig.TryCreate(null, setup.Boiler, "OtherLoco", setup.Components, out _),
                "calculated injector inputs remain stock without the JK3 livery and verified graph");
            foreach (var invalid in new[]
            {
                Jk3InjectorSetup(gameParams, divisorValue: 1f), Jk3InjectorSetup(gameParams, negative: true),
                Jk3InjectorSetup(gameParams, invert: true), Jk3InjectorSetup(gameParams, swap: true),
            })
                Check(!InjectorRig.TryCreate(null, invalid.Boiler, "Jk3A", invalid.Components, out _),
                    "a changed JK3 injector calculation retains stock behavior");
            Check(!InjectorRig.TryCreate(null, setup.Boiler, "Jk3A", setup.Components.Where(c => c.id != "injector2"), out _),
                "a missing JK3 valve retains stock behavior");

            var before = string.Join(";", setup.Components.Select(Describe));
            var rig = Rig(setup);
            Check(rig != null && rig.Circuits.Count == 2 && rig.Circuits.All(c => c.Injector.Spec.MaxDelivery == 2.5f)
                  && rig.MaxDelivery == 5f && before == string.Join(";", setup.Components.Select(Describe)),
                "JK3 has two 2.5 kg/s injectors without changing any stock ports or defaults");
            setup.Right.controlExtIn.Value = 0.1f;
            setup.Left.controlExtIn.Value = 1f;
            Tick(rig, 2f);
            // Once it sings, opened past its least so it feeds clean.
            setup.Right.controlExtIn.Value = 0.6f;
            Tick(rig, 0.5f);
            Check(rig.Circuits[0].Flow.State == InjectorState.Working && rig.Circuits[1].Flow.State == InjectorState.Broken
                  && rig.Circuits[0].Flow.OverflowSteam == 0f && rig.Circuits[1].Flow.OverflowSteam > 0f
                  && rig.Circuits[1].Flow.InjectorTemperature > rig.Circuits[0].Flow.InjectorTemperature + 30f,
                "priming one JK3 valve does not prime the other; jet failure, heat, and overflow stay independent");
            setup.Right.controlExtIn.Value = 1f;
            rig.Simulate(0.02f);
            Check(Math.Abs(rig.Flow.WaterIn - 2.5f) < 0.001f && setup.Boiler.injectorFlowNormalizedReadOut.Value == 0.5f,
                "one working JK3 injector delivers half capacity and only its valve contributes to the boiler readout");
            var particles = setup.Components.OfType<ConfigurablePorts>().Single(c => c.id == "injectorParticles").ports;
            Check(particles[0].Value == 1f && particles[1].Value == 0f,
                "JK3's stock side particle readouts follow each injector's actual feed");
            setup.Right.controlExtIn.Value = 0f;
            rig.Simulate(0.02f);
            Check(rig.Circuits[0].Flow.State == InjectorState.Off && rig.Circuits[1].Flow.State == InjectorState.Broken,
                "shutting one JK3 injector does not reset the other injector's broken jet");
            var description = FireController.Describe(rig);
            Check(description.Contains("right injector off") && description.Contains("left injector BROKEN"),
                "the overlay describes the JK3 injectors separately");
            Check(setup.Right.GetSaveStateData().Value<float>("val") == 0f
                  && setup.Left.GetSaveStateData().Value<float>("val") == 1f,
                "JK3's two controls keep their independent stock save keys");

            var both = Jk3InjectorSetup(gameParams);
            var bothRig = Rig(both);
            both.Right.controlExtIn.Value = both.Left.controlExtIn.Value = 0.1f;
            Tick(bothRig, 2f);
            both.Right.controlExtIn.Value = both.Left.controlExtIn.Value = 1f;
            var mass = bothRig.BoilerVessel.mass;
            both.Consumed.Value = 0f;
            bothRig.Simulate(0.5f);
            Check(Math.Abs(bothRig.Flow.WaterIn - 5f) < 0.001f && both.Boiler.injectorFlowNormalizedReadOut.Value == 1f
                  && Math.Abs(bothRig.BoilerVessel.mass - mass - (bothRig.Flow.WaterIn - bothRig.Flow.SteamUsed) * 0.5f) < 0.002f
                  && Math.Abs(both.Consumed.Value - bothRig.Flow.TenderDraw * 0.5f * 1.5f) < 0.001f,
                "both JK3 injectors deliver 5 kg/s total, with steam deducted and both tender draws reported once");
            both.Water.Value = 0.03f * 1.5f;
            both.Consumed.Value = 0f;
            bothRig.Simulate(0.5f);
            Check(bothRig.Flow.TenderDraw * 0.5f <= 0.03001f && both.Consumed.Value <= 0.04501f
                  && bothRig.Circuits[1].Flow.State == InjectorState.NoWater,
                "the paired JK3 injectors share the remaining tender water without overdrawing it");
            both.Water.Value = 8250f;
            var volume = (float)typeof(Boiler).GetField("totalVolume", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(both.Boiler);
            // These stock vessel readouts are the rig's pressure and space inputs.
            bothRig.BoilerVessel.waterVolume = volume - 1f;
            bothRig.Simulate(2f);
            Check(Math.Abs(bothRig.Flow.WaterIn * 2f - 1f) < 0.001f && bothRig.Circuits[1].Flow.WaterIn == 0f,
                "the JK3 injectors share the last kilogram of boiler space without overfilling it");
            bothRig.BoilerVessel.waterVolume = volume;
            bothRig.Simulate(0.02f);
            Check(bothRig.Flow.WaterIn == 0f && bothRig.Circuits.All(c => c.Flow.Overflow > 0f),
                "both JK3 injectors spill when the shared boiler has no room for more water");

            var client = Jk3InjectorSetup(gameParams);
            var clientRig = Rig(client);
            var data = ClientMirror.Capture(null, rig, null, null, null);
            ClientMirror.Apply(data, null, clientRig, null, null, null);
            Check(clientRig.Circuits.Select(c => c.Flow).SequenceEqual(rig.Circuits.Select(c => c.Flow))
                  && clientRig.Flow.Equals(rig.Flow),
                "multiplayer mirrors each JK3 injector's state, heat, and overflow by control port");
            both.Boiler.isBrokenReadOut.Value = 1f;
            bothRig.Simulate(0.02f);
            Check(bothRig.Circuits.All(c => c.Flow.State == InjectorState.Off) && bothRig.Flow.TenderDraw == 0f,
                "a broken JK3 boiler shuts down both injectors");
        }

        private static void CheckClientMirror(SimGameParams gameParams, FireboxDefinition def)
        {
            var host = Create(def, gameParams);
            host.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 40f });
            host.DeliverShovelful(5f, GrateZone.Front);
            host.LastStoker = new StokerFlow { Fed = 0.4f, Jammed = true, ManifoldPressure = 3.2f, TableCoal = 2f, TableFill = .6f, TableNeedsClearing = true };
            host.StokerEngineSpeed = 0.6f;

            Boiler NewBoiler()
            {
                var boilerDef = new BoilerDefinition
                {
                    ID = "boiler", diameter = 1.7f, length = 8.8f, capacityMultiplier = 0.85f, maxInjectorRate = 20f,
                    waterConsumptionMultiplier = 4f, safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                    spawnPressure = 12f, spawnWaterLevel = 9000f, crownSheetNormalizedWaterLevel = 0.6f,
                };
                var boiler = (Boiler)boilerDef.InstantiateImplementation();
                boiler.SetGameParams(gameParams);
                boiler.GetAllPortReferences().Single(r => r.id == "boiler.INJECTOR")
                    .SetPortReference(new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN")));
                return boiler;
            }
            var hostBoiler = NewBoiler();
            InjectorRig.TryCreate(null, hostBoiler, out _);
            InjectorRig.TryGet(hostBoiler, out var hostRig);
            hostRig.Flow = new InjectorFlow { State = InjectorState.Priming, Overflow = 1.3f, OverflowSteam = 0.2f, WaterIn = 0f };
            hostRig.HoseIn = true;
            hostRig.HoseOpening = 0.7f;
            hostRig.HoseFilling = true;
            var hostWater = BoilerWaterRig.Create(hostBoiler, null, BoilerWaterRig.Guard.FusiblePlug);
            hostWater.MeltPlug();
            hostWater.PlugVent = 0.8f;
            var oilDef = new FireboxDefinition { ID = Id, maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f };
            var hostOil = new RealisticOilFirebox(oilDef, null);
            hostOil.Model.PrepareFire();

            var data = ClientMirror.Capture(host, hostRig, hostWater, null, hostOil);

            var client = Create(def, gameParams);
            var clientBoiler = NewBoiler();
            InjectorRig.TryCreate(null, clientBoiler, out _);
            InjectorRig.TryGet(clientBoiler, out var clientRig);
            var clientWater = BoilerWaterRig.Create(clientBoiler, null, BoilerWaterRig.Guard.FusiblePlug);
            var clientOil = new RealisticOilFirebox(oilDef, null);
            ClientMirror.Apply(data, client, clientRig, clientWater, null, clientOil);

            Check(client.Model.FireOn && client.Model.SaveZones().SequenceEqual(host.Model.SaveZones())
                  && client.LastStoker is StokerFlow flow && flow.Jammed && flow.Fed == 0.4f && client.StokerEngineSpeed == 0.6f
                  && client.StokerTableCoal == 2f && client.StokerTableLoaded,
                $"a client's view gets the host's bed zone by zone and its stoker's reading ({data.Length} bytes; " +
                $"front zone {client.Model.Zones[(int)GrateZone.Front].Coal:F1} kg)");
            Check(clientRig.Flow.State == InjectorState.Priming && clientRig.Flow.Overflow == 1.3f
                  && clientRig.HoseFilling && clientRig.HoseIn && clientRig.HoseOpening == 0.7f
                  && clientWater.Plug.Melted && clientWater.PlugVent == 0.8f,
                "and the injector's overflow, the hose, and the melted plug's blow");
            Check(clientOil.Model.Snapshot().Equals(hostOil.Model.Snapshot()), "and the oil fire's state");

            // Damaged state from the wire is dropped, section by section,
            // without throwing or reading one section into the next (audit
            // 2026-10-10: a one-byte payload threw out of the decoder).
            bool Survives(byte[] bytes)
            {
                try
                {
                    ClientMirror.Apply(bytes, client, clientRig, clientWater, null, clientOil);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            var incompatible = (byte[])data.Clone();
            incompatible[3] ^= 1;
            client.StokerEngineSpeed = .2f;
            var mismatchOk = Survives(incompatible) && client.StokerEngineSpeed == .2f;
            var legacy = data.Skip(ClientMirror.HeaderSize).ToArray();
            mismatchOk &= Survives(legacy) && client.StokerEngineSpeed == .2f;
            Check(mismatchOk && ClientMirror.Capture(null, null, null, null, null) == null,
                "incompatible or legacy mirror layouts are rejected before any state changes; empty captures stay empty");
            var injectorAt = ClientMirror.HeaderSize;
            while (data[injectorAt] != 2)
                injectorAt += 3 + (data[injectorAt + 1] | data[injectorAt + 2] << 8);
            var injectorLength = data[injectorAt + 1] | data[injectorAt + 2] << 8;
            // The injector section with a NaN for its first float and the rest
            // of the stream intact: the injector is left alone, the others apply.
            var poisoned = (byte[])data.Clone();
            var nan = BitConverter.GetBytes(float.NaN);
            Array.Copy(nan, 0, poisoned, injectorAt + 3 + 4, 4);
            clientRig.Flow = new InjectorFlow { State = InjectorState.Off };
            client.StokerEngineSpeed = 0f;
            var poisonedOk = Survives(poisoned) && clientRig.Flow.State == InjectorState.Off && client.StokerEngineSpeed == 0.6f;
            // A section that claims to be shorter than it reads: it fails alone.
            var shortened = (byte[])data.Clone();
            shortened[injectorAt + 1] = 8;
            shortened[injectorAt + 2] = 0;
            var tail = data.Length - (injectorAt + 3 + injectorLength);
            var shortStream = new byte[injectorAt + 3 + 8 + tail];
            Array.Copy(shortened, 0, shortStream, 0, injectorAt + 3 + 8);
            Array.Copy(data, injectorAt + 3 + injectorLength, shortStream, injectorAt + 3 + 8, tail);
            clientWater.PlugVent = 0f;
            var shortOk = Survives(shortStream) && clientWater.PlugVent == 0.8f;
            byte[] Framed(params byte[] sections) => data.Take(ClientMirror.HeaderSize).Concat(sections).ToArray();
            client.StokerEngineSpeed = 0f;
            var unknownThenGood = Framed(new byte[] { 99, 2, 0, 7, 7 }.Concat(legacy).ToArray());
            var unknownOk = Survives(unknownThenGood) && client.StokerEngineSpeed == .6f;
            Check(Survives(Framed(1)) && Survives(Framed(1, 200)) && Survives(Framed(1, 255, 255, 0))
                  && unknownOk && Survives(data.Take(ClientMirror.HeaderSize - 1).ToArray()) && poisonedOk && shortOk,
                "damaged state from the host is dropped section by section: a broken header, a length past the end, an unknown tag, " +
                "a NaN, and a section shorter than it reads, the good sections still applied");
        }

        // The Hudson's booster in the sim: between the main engine's torque and
        // the traction, on the steam chest and the reverser, dropping its latch
        // when the reverser is hooked up.
        private static void CheckBooster()
        {
            var def = new RealisticBoosterDefinition
            {
                ID = BoosterFit.BoosterId,
                MinCutoff = 0.05f,
                MaxCutoff = 0.9f,
                TrailerLoad = 25174f,
                DrivingAxles = 3,
                FrictionCoefficient = 0.3f, // the Hudson's
            };
            var booster = new RealisticBooster(def, null);
            Port Out(string id, PortValueType type, float value) =>
                new Port(id, new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value);
            var main = Out("steamEngine", PortValueType.TORQUE, 100000f);
            var chest = Out("chest", PortValueType.PRESSURE, 1f + 250f / 14.5038f);
            var boilerPressure = Out("boiler", PortValueType.PRESSURE, 19f);
            var reverser = Out("reverser", PortValueType.CONTROL, 1f);
            var latch = new Port(BoosterFit.Latch.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var idle = new Port(BoosterFit.Idle.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            void Wire(PortReferenceDefinition reference, Port port) =>
                booster.GetAllPortReferences().Single(r => r.id == $"{def.ID}.{reference.ID}").SetPortReference(port);
            Wire(def.MainTorque, main);
            Wire(def.ChestPressure, chest);
            Wire(def.BoilerPressure, boilerPressure);
            Wire(def.Reverser, reverser);
            Wire(def.Latch, latch);
            Wire(def.Idle, idle);
            var torque = booster.GetAllPorts().Single(port => port.id == $"{def.ID}.{def.TorqueOut.ID}");

            Run(booster, 1f);
            Check(Math.Abs(torque.Value - main.Value) < 1f, $"cut out, the booster passes the main engine's torque through ({torque.Value:F0} N·m)");

            idle.Value = 1f;
            Run(booster, 180f);
            idle.Value = 0f;
            Run(booster, 20f);
            latch.Value = 1f;
            Run(booster, 25f);
            var added = (torque.Value - main.Value) / 4.44822f;
            Check(booster.Model.State == BoosterState.Working && added > 11900f && added < 12300f,
                $"idled, then latched in at a stand with the reverser forward, it adds {added:F0} lb on a 1 m wheel after its cocks close");

            reverser.Value = 0.5f;
            Run(booster, 0.5f);
            Check(latch.Value == 0f && booster.Model.State == BoosterState.CutOut && Math.Abs(torque.Value - main.Value) < 1f,
                "hooking the reverser back drops the cab's latch and cuts it out");
        }

        // The replacement for CCL's stoker, against CCL's own, on the S464-730's
        // figures: 10 tender kg/s, firebox multiplier 4.
        private static void CheckStoker(SimGameParams gameParams, FireboxDefinition fireboxDef)
        {
            var dv = Environment.GetEnvironmentVariable("DV_INSTALL_DIR")
                     ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local/share/Steam/steamapps/common/Derail Valley");
            var importer = Path.Combine(dv, "Mods", "DVCustomCarLoader", "CCL.Importer.dll");
            if (!File.Exists(importer))
            {
                Console.WriteLine("skip stoker checks: Custom Car Loader is not installed");
                return;
            }
            var cclDefType = Assembly.LoadFrom(importer).GetType("CCL.Importer.Components.Simulation.SteamMechanicalStokerDefinitionInternal");
            var cclDef = (SimComponentDefinition)Activator.CreateInstance(cclDefType);
            cclDef.ID = "autostoker";
            var ccl = cclDef.InstantiateImplementation();
            var values = StokerDefinitionPatch.Read(cclDef);
            const float multiplier = 4f;
            var mine = new RealisticStoker(values, null, multiplier, steamWired: false);
            mine.SetGameParams(gameParams);
            Check(Describe(ccl) == Describe(mine) && ccl.id == mine.id,
                $"the stoker keeps CCL's ID, ports, and references ({ccl.GetAllPorts().Count} ports, {ccl.GetAllPortReferences().Count} references)");

            // The mod's own stoker (the S282's retrofit) has the same contract.
            var ownDef = new RealisticStokerDefinition { ID = "autostoker", MaxTransferRate = 10f, CoalMultiplier = multiplier };
            var own = new RealisticStoker(ownDef.Values(), null, multiplier, steamWired: false);
            Check(Describe(ccl) == Describe(own),
                "the mod's own stoker for the S282 has CCL's stoker's ports and references");

            var boilerDef = new BoilerDefinition
            {
                ID = "boiler", diameter = 2.3f, length = 13f, capacityMultiplier = 0.85f, maxInjectorRate = 36f,
                waterConsumptionMultiplier = 1.5f, safetyValveOpeningPressure = 20.3f, safetyValveClosingPressure = 20f,
                spawnPressure = 15f, spawnWaterLevel = 36000f,
            };
            var boiler = (Boiler)boilerDef.InstantiateImplementation();
            boiler.SetGameParams(gameParams);
            var vessel = InjectorRig.VesselOf(boiler);

            var throttle = new Port("stokerControl", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 1f);
            var pressure = new Port("boilerGauge", new PortDefinition(PortType.READONLY_OUT, PortValueType.PRESSURE, "PRESSURE"), 15f);
            var tender = new Port("tenderCoal", new PortDefinition(PortType.READONLY_OUT, PortValueType.COAL, "AMOUNT"), 10000f);
            var consume = new Port("tenderCoal", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.COAL, "CONSUME_EXT_IN"));

            void Connect(SimComponent stoker, SimComponent firebox)
            {
                PortReference R(string name) => stoker.GetAllPortReferences().Single(r => r.id == $"autostoker.{name}");
                R("CONTROL").SetPortReference(throttle);
                R("STEAM_PRESSURE").SetPortReference(pressure);
                R("COAL_AMOUNT").SetPortReference(tender);
                R("COAL_CONSUMPTION").SetPortReference(consume);
                R("FIREBOX_COAL_LEVEL").SetPortReference(firebox.GetAllPorts().Single(p => p.id == $"{Id}.COAL_LEVEL"));
                R("FIREBOX_COAL_CAPACITY").SetPortReference(firebox.GetAllPorts().Single(p => p.id == $"{Id}.COAL_CAPACITY"));
                R("FIREBOX_COAL_CONTROL").SetPortReference(firebox.GetAllPorts().Single(p => p.id == $"{Id}.COAL_CONTROL_EXT_IN"));
            }

            var zoned = Create(fireboxDef, gameParams);
            zoned.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 10f });
            Connect(mine, zoned);
            mine.Attach(zoned, boiler);
            var steamBefore = vessel.mass;
            float drawn = 0f, consumed = 0f, steam = 0f;
            for (var t = 0f; t < 6f; t += 0.02f)
            {
                consume.Value = 0f;
                mine.Tick(0.02f);
                consumed += consume.Value;
                drawn += zoned.LastStoker.Value.Drawn;
                steam += zoned.LastStoker.Value.SteamUsed * 0.02f;
            }
            var zones = zoned.Model.Zones;
            Check(mine.FeedsZonedFire && drawn > 1f && Math.Abs(consumed - multiplier * drawn) < 0.01f * consumed
                  && zones[(int)GrateZone.Front].Coal > zones[(int)GrateZone.UnderDoor].Coal,
                $"it throws coal onto the zones and takes {multiplier:F0} tender kg per firebox kg ({drawn:F1} kg fed, {consumed:F1} tender kg)");
            Check(steamBefore - vessel.mass > 0f && Math.Abs(steamBefore - vessel.mass - steam) < RealisticStoker.SteamLot + 0.02f * steam
                  && mine.GetAllPorts().Single(p => p.id == "autostoker.STOKING_NORMALIZED").Value > 0.6f,
                $"its engine runs and takes its steam from the boiler ({steam:F2} kg in 6 s, {steamBefore - vessel.mass:F2} kg taken)");

            // With the cab's jet valves, the stoker follows them: master shut
            // drops the coal at the door.
            var jets = StokerJetControls.Valves
                .Select(v => new Port(v.Id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), v.Default))
                .ToArray();
            jets[0].Value = 0f;
            var heel = Create(fireboxDef, gameParams);
            heel.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 0.1f });
            var valved = new RealisticStoker(values, null, multiplier, steamWired: true);
            valved.SetGameParams(gameParams);
            Connect(valved, heel);
            valved.Attach(heel, boiler);
            valved.AttachJets(jets);
            for (var t = 0f; t < 6f; t += 0.02f)
                valved.Tick(0.02f);
            var heelZones = heel.Model.Zones;
            var backRow = heelZones[(int)GrateZone.BackLeft].Coal + heelZones[(int)GrateZone.UnderDoor].Coal + heelZones[(int)GrateZone.BackRight].Coal;
            Check(heel.LastStoker.Value.ManifoldPressure == 0f && backRow > 0.75f * heel.Model.CoalMass,
                $"it follows the cab's jet valves: master jet shut, {backRow / heel.Model.CoalMass:P0} of the coal lands at the door");

            // Without the zoned fire, coal goes through a stock firebox's port.
            var stock = new Firebox(fireboxDef);
            stock.SetGameParams(gameParams);
            var fallback = new RealisticStoker(values, null, multiplier, steamWired: true);
            fallback.SetGameParams(gameParams);
            Connect(fallback, stock);
            fallback.Attach(null, boiler);
            var stockConsumed = 0f;
            for (var t = 0f; t < 6f; t += 0.02f)
            {
                consume.Value = 0f;
                fallback.Tick(0.02f);
                stockConsumed += consume.Value;
            }
            var stockCoal = P(stock, "COAL_LEVEL").Value;
            Check(!fallback.FeedsZonedFire && stockCoal > 1f && Math.Abs(stockConsumed - multiplier * stockCoal) < 0.01f * stockConsumed,
                $"without the zoned fire it feeds a stock firebox's port at the right multiplier ({stockCoal:F1} kg, {stockConsumed:F1} tender kg)");
            var heldFuel = fallback.Model.Spec.MaxFeedRate * fallback.Model.Spec.TableCapacitySeconds;
            fallback.Model.RestoreState(new StokerState { TableCoal = heldFuel, Packing = .8f });
            for (var t = 0f; t < 5f; t += .02f) fallback.Tick(.02f);
            Check(fallback.Model.EngineSpeed > .5f && fallback.Model.TableCoal == heldFuel,
                "stock-firebox fallback keeps running while preserving table fuel from a formerly zoned fire");

            // A jam survives a save, whether the stoker loads before or after
            // its model is built (audit 2026-10-10: a recreated stoker came
            // back unjammed).
            valved.Model.Jam();
            var jamSave = valved.GetSaveStateData();
            var early = new RealisticStoker(values, null, multiplier, steamWired: true);
            early.SetGameParams(gameParams);
            Connect(early, heel);
            early.SetSaveStateData(jamSave);
            early.Attach(heel, boiler);
            var late = new RealisticStoker(values, null, multiplier, steamWired: true);
            late.SetGameParams(gameParams);
            Connect(late, heel);
            late.Attach(heel, boiler);
            late.SetSaveStateData(jamSave);
            var clear = new RealisticStoker(values, null, multiplier, steamWired: true);
            clear.SetGameParams(gameParams);
            Connect(clear, heel);
            clear.Attach(heel, boiler);
            clear.SetSaveStateData(new JObject());
            Check(valved.HasSaveData && jamSave.Value<bool>(RealisticStoker.JammedSaveKey) && early.Model.Jammed && late.Model.Jammed
                  && !clear.Model.Jammed && clear.GetSaveStateData()[RealisticStoker.JammedSaveKey] == null,
                "a jammed stoker stays jammed through a save and a recreated stoker; a clear one saves nothing");

            var operatingLever = new Port(StokerJetControls.Reverse.PortId,
                new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"), 1f);
            valved.Attach(heel, boiler, operatingLever);
            early.Attach(heel, boiler, operatingLever);
            valved.Model.RestoreState(new StokerState { TableCoal = 1.5f, TableFines = .6f, TableWater = .1f,
                Packing = .7f, TableWarmth = .4f, TableCake = .2f, Jammed = true, PackingJam = true,
                TurnsToClear = 5f, Reversed = 1f, TableNeedsClearing = true });
            var wetSave = valved.GetSaveStateData();
            early.SetSaveStateData(wetSave);
            var beforeBuild = new RealisticStoker(values, null, multiplier, steamWired: true);
            beforeBuild.SetGameParams(gameParams); Connect(beforeBuild, heel);
            beforeBuild.SetSaveStateData(wetSave); beforeBuild.Attach(heel, boiler, operatingLever);
            var stored = early.Model.CaptureState(); var built = beforeBuild.Model.CaptureState();
            Check(stored.TableCoal == 1.5f && built.TableCoal == 1.5f && stored.TableFines == .6f && built.TableWater == .1f
                  && early.Model.Spec.PackingJams && beforeBuild.Model.Spec.PackingJams
                  && stored.Packing == .7f && built.Packing == .7f && built.TableCake == .2f && stored.Reversed == 1f && built.TurnsToClear == 5f
                  && stored.PackingJam && built.Jammed && stored.TableNeedsClearing && built.TableNeedsClearing,
                "stoker fuel, fine mix, water, packing, coating and reversal progress round-trip before and after model creation");
            var withoutLever = new RealisticStoker(values, null, multiplier, steamWired: true);
            withoutLever.SetGameParams(gameParams); Connect(withoutLever, heel);
            withoutLever.SetSaveStateData(wetSave); withoutLever.Attach(heel, boiler);
            var noPacking = withoutLever.Model.CaptureState();
            Check(!withoutLever.Model.Spec.PackingJams && noPacking.Packing == 0f
                  && noPacking.TableCoal == stored.TableCoal && noPacking.TableFines == stored.TableFines
                  && noPacking.TableWater == stored.TableWater && noPacking.TableCake == stored.TableCake,
                "without a reversing lever, loading clears disabled packing while preserving table fuel, fines, water and coating");
            early.SetSaveStateData(new JObject { [RealisticStoker.JammedSaveKey] = true,
                [RealisticStoker.HandlingSaveKey] = new JObject { ["TableCoal"] = "bad" } });
            Check(early.Model.Jammed && early.Model.TableCoal == 0f,
                "malformed new stoker data falls back to the old jam key without injecting fuel or aborting load");
        }

        private static RealisticFirebox Create(FireboxDefinition def, SimGameParams gameParams)
        {
            var firebox = new RealisticFirebox(def, null);
            firebox.SetGameParams(gameParams);
            return firebox;
        }

        private static Port P(SimComponent component, string portId) =>
            component.GetAllPorts().Single(p => p.id == $"{Id}.{portId}");

        // What one description has that the other lacks, for a failed comparison.
        private static string Difference(string expected, string actual)
        {
            if (expected == actual)
                return "";
            var a = expected.Split('\n');
            var b = actual.Split('\n');
            return " [missing: " + string.Join("; ", a.Except(b)) + "] [extra: " + string.Join("; ", b.Except(a)) + "]";
        }

        private static string Describe(SimComponent component)
        {
            var writeAllowed = typeof(PortReference).GetField("writeAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
            var ports = component.GetAllPorts().Select(p => $"port {p.id} {p.type} {p.valueType} = {p.Value}");
            var references = component.GetAllPortReferences().Select(r => $"ref {r.id} = {r.Value} write {writeAllowed.GetValue(r)}");
            return string.Join("\n", ports.Concat(references).OrderBy(line => line, StringComparer.Ordinal));
        }

        // Every Harmony patch class's target resolves to exactly one method in
        // the game's assemblies, as PatchAll will look it up. One that does
        // not (missing, or ambiguous like Resources.Load(string) against
        // Load<T>(string)) makes PatchAll throw and the whole mod fail to load.
        // The realistic cylinders on the stock engine with the S282's figures,
        // patched as in game, against the same engine with the setting off.
        private static void CheckCylinders(SimGameParams gameParams)
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.Cylinders");
            foreach (var patch in new[] { typeof(CylinderAdmissionPatch), typeof(CylinderMeanTorquePatch), typeof(CylinderPistonForcePatch), typeof(CylinderExhaustPatch) })
                harmony.CreateClassProcessor(patch).Patch();
            var settings = RealisticFiring.Main.Settings;
            try
            {
                // Reverser for a cut-off: the stock notches from 5 % to 90 %.
                float Notch(float cutoff) => (cutoff - 0.05f) / 0.85f;

                var stockStart = RunEngine(gameParams, false, 1f, 10f);
                var realStart = RunEngine(gameParams, true, 1f, 10f);
                Check(realStart.Torque > 0.9f * stockStart.Torque && realStart.Torque < stockStart.Torque,
                    $"in full gear at a walk the realistic cylinders pull nearly the stock torque ({realStart.Torque:F0} against {stockStart.Torque:F0} N·m)");

                var stockQuarter = RunEngine(gameParams, false, Notch(0.25f), 180f);
                var realQuarter = RunEngine(gameParams, true, Notch(0.25f), 180f);
                Check(realQuarter.Torque > 1.3f * stockQuarter.Torque && realQuarter.Exhaust > stockQuarter.Exhaust,
                    $"notched up to 25 % at 3 turns a second they hold their steam ({realQuarter.Torque:F0} against {stockQuarter.Torque:F0} N·m, " +
                    $"exhaust {realQuarter.Exhaust:F1} against {stockQuarter.Exhaust:F1} bar)");

                var realFull = RunEngine(gameParams, true, 1f, 180f);
                Check(realFull.Flow < 6.6f && realFull.Chest < 13f,
                    $"in full gear at speed the throttle's flow limits them through the steam chest ({realFull.Flow:F2} kg/s, chest {realFull.Chest:F1} bar)");

                // A loco left stock keeps the game's own cylinders with the setting on.
                var keptQuarter = RunEngine(gameParams, true, Notch(0.25f), 180f, leftStock: true);
                Check(Math.Abs(keptQuarter.Torque - stockQuarter.Torque) < 0.01f * Math.Abs(stockQuarter.Torque) + 1f,
                    $"a loco left stock keeps the game's cylinders ({keptQuarter.Torque:F0} against the stock {stockQuarter.Torque:F0} N·m)");

                var slow = RunEngine(gameParams, true, Notch(0.5f), 40f);
                var fast = RunEngine(gameParams, true, Notch(0.5f), 70f);
                Check(Math.Abs(slow.Torque - fast.Torque) < 0.15f * fast.Torque,
                    $"each piston's force averages to the mean pressure's torque across the stock's blend ({slow.Torque:F0} at 40 rpm, {fast.Torque:F0} at 70)");
            }
            finally
            {
                settings.RealisticCylinders = true;
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // The period blower on the game's exhaust, patched as in game: the
        // S060's stock exhaust, then with its fitted blower and natural draft.
        private static void CheckBlower(SimGameParams gameParams)
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.Blower");
            harmony.CreateClassProcessor(typeof(BlowerPatch)).Patch();
            try
            {
                SteamExhaust New(out Port air, out Port steam)
                {
                    var def = new SteamExhaustDefinition { ID = "exhaust", passiveExhaust = 0.2f, entrainmentRatio = 1.95f, maxBlowerFlow = 0.2f, pressureForMaxBlowerFlow = 3f };
                    var exhaust = (SteamExhaust)def.InstantiateImplementation();
                    exhaust.SetGameParams(gameParams);
                    void Wire(PortReferenceDefinition reference, PortValueType type, float value) =>
                        exhaust.GetAllPortReferences().Single(r => r.id == $"{def.ID}.{reference.ID}")
                            .SetPortReference(new Port(reference.ID.ToLowerInvariant(), new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value));
                    Wire(def.exhaustFlow, PortValueType.MASS_RATE, 0f);
                    Wire(def.engineMaxFlowReader, PortValueType.MASS_RATE, 2f);
                    Wire(def.boilerPressure, PortValueType.PRESSURE, 13.4f);
                    Wire(def.blowerControl, PortValueType.CONTROL, 1f);
                    Wire(def.whistleControl, PortValueType.CONTROL, 0f);
                    Wire(def.damperControl, PortValueType.CONTROL, 1f);
                    air = exhaust.GetAllPorts().Single(p => p.id == $"{def.ID}.{def.airFlowReadOut.ID}");
                    steam = exhaust.GetAllPorts().Single(p => p.id == $"{def.ID}.{def.steamConsumptionReadOut.ID}");
                    return exhaust;
                }

                var stock = New(out var stockAir, out var stockSteam);
                stock.Tick(0.02f);
                var fitted = New(out var fittedAir, out var fittedSteam);
                BlowerFit.Fit(fitted, new SteamDraft(0.4f, 0.3f, fitted.entrainmentRatio, 13.4f));
                fitted.Tick(0.02f);
                Check(Math.Abs(stockAir.Value - 0.59f) < 0.01f && Math.Abs(fittedAir.Value - 2.3f) < 0.01f && Math.Abs(fittedSteam.Value - 0.4f) < 0.01f,
                    $"the S060's period blower wide open draws {fittedAir.Value:F2} kg/s of air for {fittedSteam.Value:F2} kg/s of steam " +
                    $"(the game's {stockAir.Value:F2} for {stockSteam.Value:F2})");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // The rain's water in a tender's coal, kept by the game's coal
        // container: saved beside its stock key and back on load, before or
        // after the store is made; coal loaded at the stage mixes in dry.
        private static void CheckCoalStore(SimGameParams gameParams)
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.CoalStore");
            harmony.CreateClassProcessor(typeof(CoalSavePatch)).Patch();
            harmony.CreateClassProcessor(typeof(CoalLoadPatch)).Patch();
            try
            {
                ResourceContainer NewTender()
                {
                    var tender = (ResourceContainer)new CoalContainerDefinition { ID = "tenderCoal", capacity = 10000f, defaultValue = 5000f }.InstantiateImplementation();
                    tender.SetGameParams(gameParams);
                    return tender;
                }

                var wet = NewTender();
                var store = CoalStore.Create(wet);
                store.Water.Restore(CoalMoisture.Soaked);
                var saved = wet.GetSaveStateData();

                // Loaded before its store is made, as the game loads a car.
                var loaded = NewTender();
                loaded.SetSaveStateData(saved);
                var restored = CoalStore.Create(loaded);

                // Filled from half to full with coal as delivered.
                restored.Step(0.02f, 1f);
                loaded.refillExtIn.ExternalValueUpdate(5000f);
                restored.Step(0.02f, 1f);
                var mixed = (CoalMoisture.Soaked + CoalMoisture.Delivered) / 2f;
                Check(saved.ContainsKey(CoalStore.MoistureKey) && saved.ContainsKey("filled")
                      && Math.Abs(loaded.amountReadOut.Value - 10000f) < 1f && Math.Abs(restored.Moisture - mixed) < 0.002f,
                    $"a tender's wet coal saves its water ({saved.Value<float>(CoalStore.MoistureKey):P0}) beside the stock key, comes back on load, " +
                    $"and mixes with coal loaded at the stage: {restored.Moisture:P1} with the tender filled from half");

                // The Hudson's coal bunker door: shut, an hour's rain leaves the coal as delivered; opened, it soaks.
                var door = new Port("coalbunkedoorControl", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
                var covered = CoalStore.Create(NewTender(), door);
                for (var t = 0f; t < 3600f; t += 1f)
                    covered.Step(1f, 1f);
                var dryUnder = covered.Moisture;
                door.ExternalValueUpdate(1f);
                for (var t = 0f; t < 900f; t += 1f)
                    covered.Step(1f, 1f);
                Check(covered.HasCover && door.id == CoalStore.CoverPorts[0] && Math.Abs(dryUnder - CoalMoisture.Delivered) < 0.001f
                      && covered.Moisture > 0.07f,
                    $"the Hudson's shut coal bunker door keeps the rain off ({dryUnder:P1} after an hour's rain), opened it soaks ({covered.Moisture:P1} in a quarter hour)");

                // The containers of cars gone are let go (a container is never
                // Unity's null; all 200 were kept before, 2026-10-25).
                var abandoned = AbandonedCoal(NewTender, 200);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var kept = abandoned.Count(w => w.IsAlive);
                Check(kept <= 2, $"coal stores let the containers of cars gone go: {kept} of {abandoned.Count} kept after a full collection");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // Containers given stores, a save's water waiting for some, and then
        // dropped, each known only weakly.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static List<WeakReference> AbandonedCoal(Func<ResourceContainer> make, int count)
        {
            var dropped = new List<WeakReference>();
            for (var i = 0; i < count; i++)
            {
                var container = make();
                if (i % 2 == 0)
                    CoalStore.Create(container).Step(0.02f, 0f);
                else
                    CoalStore.Load(container, new JObject { [CoalStore.MoistureKey] = 0.05f });
                dropped.Add(new WeakReference(container));
            }
            return dropped;
        }

        // A fired engine's safety valves fitted from its prototype, worked by
        // the stock valve: the S060's passing its fitted 3.5 kg/s over its
        // lift, the 8F's lifting at 225 psi, not its maker's 210, each
        // reseating 3 psi under. With failures off a boiler never bursts.
        private static void CheckSafetyValveFit(SimGameParams gameParams)
        {
            var vesselField = typeof(Boiler).GetField("vessel", BindingFlags.Instance | BindingFlags.NonPublic);
            float Field(Boiler boiler, string name) => (float)typeof(Boiler).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(boiler);
            float Mass(Boiler boiler) => ((WaterPressureVessel)vesselField.GetValue(boiler)).mass;

            // Steam the valve lets go in a second from a boiler at <paramref name="pressure"/>.
            float Vented(float makerLift, float pressure, float? fitLift, float fitCapacity, out Boiler boiler)
            {
                var def = new BoilerDefinition
                {
                    ID = "boiler", diameter = 1.7f, length = 5.5f, capacityMultiplier = 0.85f, maxInjectorRate = 5f,
                    safetyValveOpeningPressure = makerLift, safetyValveClosingPressure = makerLift - 1f, safetyValveSlop = 0.1f,
                    maxSafetyValveVentRate = 2f, spawnPressure = pressure, spawnWaterLevel = 6000f,
                };
                boiler = (Boiler)def.InstantiateImplementation();
                boiler.SetGameParams(gameParams);
                if (fitLift.HasValue)
                    SafetyValveFit.Fit(boiler, fitLift.Value, fitCapacity);
                var before = Mass(boiler);
                typeof(Boiler).GetMethod("SimulateSafetyValve", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(boiler, new object[] { 1f });
                return before - Mass(boiler);
            }

            var s060Stock = Vented(15.5f, 15.6f, null, 0f, out _);
            var s060 = Vented(15.5f, 15.6f, 15.5f, 3.5f, out var s060Boiler);
            var lms8fStock = Vented(15.5f, 16f, null, 0f, out _);
            var lms8f = Vented(15.5f, 16f, 16.5f, 5.5f, out var lms8fBoiler);
            Check(Math.Abs(s060 - 3.5f) < 0.01f && Math.Abs(s060Stock - 2f) < 0.01f
                  && Math.Abs(Field(s060Boiler, "safetyValveClosingPressure") - 15.3f) < 1e-4f
                  && lms8f == 0f && Math.Abs(lms8fStock - 2f) < 0.01f
                  && Math.Abs(Field(lms8fBoiler, "safetyValveOpeningPressure") - 16.5f) < 1e-4f
                  && Math.Abs(Field(lms8fBoiler, "safetyValveClosingPressure") - 16.3f) < 1e-4f
                  && float.IsPositiveInfinity(SafetyValveFit.BurstPressure(s060Boiler)),
                $"fitted safety valves: the S060's vents {s060:F2} kg/s over its lift (its maker's {s060Stock:F2}); at 16 bar the 8F's " +
                $"vents {lms8f:F2} kg/s, lifting at {Field(lms8fBoiler, "safetyValveOpeningPressure"):F1} ({SafetyValveFit.Psi(16.5f):F0} psi; its maker's " +
                $"lifts at 15.5 and vents {lms8fStock:F2}), reseating at {Field(lms8fBoiler, "safetyValveClosingPressure"):F1}; no bursting with failures off");
        }

        // The S282's air pump on period steam, patched as in game: pumping
        // flat out it takes its fitted steam, not the game's 1 kg/s, and its
        // exhaust draws the fire through the blast pipe with the blower shut.
        // The S484-840's pump fitted as the UP 4-8-4s' two cross-compounds:
        // their air from 12.5 bar, not the maker's 180 L/s only at 20, into
        // the FEF-1's reservoir, not its 8,000 L.
        private static void CheckAirPump(SimGameParams gameParams)
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.AirPump");
            harmony.CreateClassProcessor(typeof(BlowerPatch)).Patch();
            harmony.CreateClassProcessor(typeof(AirPumpPatch)).Patch();
            try
            {
                SteamCompressor NewPump(out Port steam, SteamCompressorDefinition def = null, float boiler = 14f)
                {
                    def = def ?? new SteamCompressorDefinition { ID = "compressor" };
                    var pump = (SteamCompressor)def.InstantiateImplementation();
                    pump.SetGameParams(gameParams);
                    void Wire(PortReferenceDefinition reference, PortValueType type, float value) =>
                        pump.GetAllPortReferences().Single(r => r.id == $"{def.ID}.{reference.ID}")
                            .SetPortReference(new Port(reference.ID.ToLowerInvariant(), new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value));
                    Wire(def.compressorControl, PortValueType.CONTROL, 1f);
                    Wire(def.steamPressure, PortValueType.PRESSURE, boiler);
                    pump.GetAllPorts().Single(p => p.id == $"{def.ID}.{def.activationSignalExtIn.ID}").ExternalValueUpdate(1f);
                    pump.GetAllPorts().Single(p => p.id == $"{def.ID}.{def.mainResPressureNormalizedExtIn.ID}").ExternalValueUpdate(0.5f);
                    steam = pump.GetAllPorts().Single(p => p.id == $"{def.ID}.{def.steamConsumptionReadOut.ID}");
                    return pump;
                }

                var exhaustDef = new SteamExhaustDefinition { ID = "exhaust", passiveExhaust = 0.6f, entrainmentRatio = 1.95f, maxBlowerFlow = 0.5f, pressureForMaxBlowerFlow = 3f };
                var exhaust = (SteamExhaust)exhaustDef.InstantiateImplementation();
                exhaust.SetGameParams(gameParams);
                void WireExhaust(PortReferenceDefinition reference, PortValueType type, float value) =>
                    exhaust.GetAllPortReferences().Single(r => r.id == $"{exhaustDef.ID}.{reference.ID}")
                        .SetPortReference(new Port(reference.ID.ToLowerInvariant(), new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value));
                WireExhaust(exhaustDef.exhaustFlow, PortValueType.MASS_RATE, 0f);
                WireExhaust(exhaustDef.engineMaxFlowReader, PortValueType.MASS_RATE, 6f);
                WireExhaust(exhaustDef.boilerPressure, PortValueType.PRESSURE, 14f);
                WireExhaust(exhaustDef.blowerControl, PortValueType.CONTROL, 0f);
                WireExhaust(exhaustDef.whistleControl, PortValueType.CONTROL, 0f);
                WireExhaust(exhaustDef.damperControl, PortValueType.CONTROL, 1f);
                var air = exhaust.GetAllPorts().Single(p => p.id == $"{exhaustDef.ID}.{exhaustDef.airFlowReadOut.ID}");

                var stock = NewPump(out var stockSteam);
                var fitted = NewPump(out var fittedSteam);
                var draft = new SteamDraft(0.5f, 0.6f, exhaust.entrainmentRatio, 15f);
                BlowerFit.Fit(exhaust, draft, fitted, AirPump.NineAndHalf.Steam);
                exhaust.Tick(0.02f);
                var idle = air.Value;
                for (var i = 0; i < 1500; i++)
                {
                    stock.Tick(0.02f);
                    fitted.Tick(0.02f);
                }
                exhaust.Tick(0.02f);
                Check(Math.Abs(fittedSteam.Value - 0.18f) < 0.005f && Math.Abs(stockSteam.Value - 1f) < 0.02f && Math.Abs(idle - 0.6f) < 0.01f
                      && Math.Abs(air.Value - (0.6f + 1.95f * 0.18f)) < 0.01f,
                    $"the S282's air pump flat out takes {fittedSteam.Value:F2} kg/s of steam (the game's {stockSteam.Value:F2}) " +
                    $"and draws {air.Value:F2} kg/s of air through the fire with the blower shut ({idle:F2} with it still)");

                SteamCompressorDefinition Big() => new SteamCompressorDefinition
                {
                    ID = "compressor", maxProductionRate = 180f, maxSteamConsumption = 12f, pressureForMaxProduction = 20f,
                    activationPressureThreshold = 7f, mainReservoirVolume = 8000f, smoothTime = 3f,
                };
                var production = new List<string>();
                var fittedRight = true;
                foreach (var boiler in new[] { 8f, 14f })
                {
                    var maker = NewPump(out _, Big(), boiler);
                    var ups = NewPump(out var upsSteam, Big(), boiler);
                    BlowerFit.FitAirPump(ups, AirPump.CrossCompound, 2, 1491f);
                    BlowerFit.Fit(exhaust, draft, ups, 2f * AirPump.CrossCompound.Steam);
                    for (var i = 0; i < 1500; i++)
                    {
                        maker.Tick(0.02f);
                        ups.Tick(0.02f);
                    }
                    var share = Math.Min(1f, (boiler - 1f) / (AirPump.CrossCompound.FullSpeedPressure - 1f));
                    var volume = ups.GetAllPorts().Single(p => p.id == "compressor.MAIN_RES_VOLUME").Value;
                    fittedRight &= Math.Abs(ups.productionRateReadOut.Value - 2f * 61.8f * share) < 0.5f
                                   && Math.Abs(upsSteam.Value - 0.38f * share) < 0.01f && volume == 1491f;
                    production.Add($"at {boiler:F0} bar {ups.productionRateReadOut.Value:F0} L/s into {volume:F0} L, {upsSteam.Value:F2} kg/s of steam " +
                                   $"(the maker's {maker.productionRateReadOut.Value:F0} L/s into 8000 L)");
                }
                Check(fittedRight, "the S484-840's two cross-compound pumps pump " + string.Join("; ", production));
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private struct EngineRun
        {
            public float Torque;
            public float Exhaust;
            public float Chest;
            public float Flow;
        }

        // The S282's engine at a steady crank speed and full throttle on 14 bar
        // of saturated steam for 20 s; its torque averaged over the last 4 s.
        private static EngineRun RunEngine(SimGameParams gameParams, bool realistic, float reverser, float rpm, bool leftStock = false)
        {
            RealisticFiring.Main.Settings.RealisticCylinders = realistic;
            var def = new ReciprocatingSteamEngineDefinition
            {
                ID = "steamEngine",
                numCylinders = 2,
                cylinderBore = 0.61f,
                pistonStroke = 0.711f,
                minCutoff = 0.05f,
                maxCutoff = 0.9f,
                throttleMaxFlow = 6f,
                steamChestVolume = 1000f,
            };
            var engine = new ReciprocatingSteamEngine(def);
            engine.SetGameParams(gameParams);
            if (leftStock)
                LeftStock.Keep(engine);
            void Wire(PortReferenceDefinition reference, PortValueType type, float value) =>
                engine.GetAllPortReferences().Single(r => r.id == $"{def.ID}.{reference.ID}")
                    .SetPortReference(new Port(reference.ID.ToLowerInvariant(), new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value));
            Wire(def.throttleControl, PortValueType.CONTROL, 1f);
            Wire(def.reverserControl, PortValueType.CONTROL, reverser);
            Wire(def.cylinderCockControl, PortValueType.CONTROL, 0f);
            Wire(def.intakePressure, PortValueType.PRESSURE, 14f);
            Wire(def.intakeTemperature, PortValueType.TEMPERATURE, 195f);
            Wire(def.intakeQuality, PortValueType.GENERIC, 1f);
            Wire(def.crankRpm, PortValueType.RPM, rpm);
            Wire(def.lubricationNormalized, PortValueType.OIL, 1f);
            Port Out(PortDefinition port) => engine.GetAllPorts().Single(p => p.id == $"{def.ID}.{port.ID}");
            var torque = Out(def.torqueOut);
            var sum = 0.0;
            var samples = 0;
            for (var t = 0f; t < 20f; t += 0.02f)
            {
                engine.Tick(0.02f);
                if (t >= 16f)
                {
                    sum += torque.Value;
                    samples++;
                }
            }
            return new EngineRun
            {
                Torque = (float)(sum / samples),
                Exhaust = Out(def.exhaustPressureReadOut).Value,
                Chest = Out(def.steamChestPressureReadOut).Value,
                Flow = Out(def.exhaustFlowReadOut).Value,
            };
        }

        // ControlSteps reaches a live lever's and valve's notches by name.
        private static void CheckControlSteps()
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var missing = new System.Collections.Generic.List<string>();
            void Field(Type type, string name)
            {
                if (HarmonyLib.AccessTools.Field(type, name) == null)
                    missing.Add($"{type.Name}.{name}");
            }
            Field(typeof(DV.CabControls.LeverBase), "spec");
            Field(typeof(DV.CabControls.LeverBase), "steppedJoint");
            Field(typeof(DV.CabControls.RotaryBase), "steppedJoint");
            Field(typeof(DV.CabControls.RotaryBase), "scrollWheelHoverScroll");
            if (typeof(DV.CabControls.RotaryBase).GetProperty("Spec", any) == null)
                missing.Add("RotaryBase.Spec");
            // The specs live in DV.CabControls.Spec, which this check does not reference.
            var lever = HarmonyLib.AccessTools.TypeByName("DV.CabControls.Spec.Lever");
            var rotary = HarmonyLib.AccessTools.TypeByName("DV.CabControls.Spec.Rotary");
            if (lever == null || rotary == null)
                missing.Add("the Lever and Rotary specs");
            else
            {
                Field(lever, "notches");
                Field(lever, "scrollWheelHoverScroll");
                Field(rotary, "notches");
                Field(rotary, "scrollWheelHoverScroll");
            }
            if (typeof(SteppedJoint).GetProperty("SingleNotchAngle", any)?.GetSetMethod(true) == null)
                missing.Add("SteppedJoint.SingleNotchAngle (set)");
            Check(missing.Count == 0, "the members ControlSteps resteps a live control by exist" + (missing.Count > 0 ? $" (not: {string.Join(", ", missing)})" : ""));
        }

        // The fireman's hand on a cab control's port, with a notched lever as
        // the cab's feeder and stepped joint put it back (to tenths), and the
        // player taking it.
        private static void CheckFiremanHand()
        {
            var port = new Port("blower", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            port.ValueUpdatedInternally += v =>
            {
                var notch = (float)Math.Round(v * 10f) / 10f;
                if (notch != v)
                    port.ExternalValueUpdate(notch);
            };
            var moves = 0;
            port.ValueUpdatedInternally += _ => moves++;
            var hand = new FiremanHand("blower", port, 0.5f);
            var t = 0f;
            void Run(float want, float seconds)
            {
                for (var end = t + seconds; t < end; t += 0.02f)
                    hand.Work(want, t);
            }

            Run(0.37f, 2f);
            var travelled = moves;
            Run(0.37f, 10f);
            Check(port.Value == 0.4f && !hand.Yielded(t) && travelled >= 3 && moves - travelled <= 4,
                $"he moves a notched lever at his pace ({travelled} moves on the way) and takes the notch it settles on " +
                $"without fighting it ({moves - travelled} moves in the next 10 s)");
            port.ExternalValueUpdate(0.8f);
            Run(0.37f, 1f);
            Check(hand.Yielded(t) && port.Value == 0.8f, "a lever the player moves is theirs");
            Run(0.37f, FiremanHand.YieldTime);
            Check(!hand.Yielded(t) && port.Value == 0.4f, "after a minute he takes it back");

            var smooth = new Port("injector", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var dragged = new FiremanHand("injector", smooth, 1.2f);
            for (t = 0f; t < 3f; t += 0.02f)
                dragged.Work(0.5f, t);
            // The player eases it open a little at a time, slower than he would notice tick by tick.
            for (var i = 0; i < 100; i++, t += 0.02f)
            {
                smooth.ExternalValueUpdate(smooth.Value + 0.002f);
                dragged.Work(0.5f, t);
            }
            Check(dragged.Yielded(t) && smooth.Value > 0.6f, $"a slow drag is the player's too (left at {smooth.Value:F2})");
            dragged.Release();
            for (var end = t + 1f; t < end; t += 0.02f)
                dragged.Work(0.5f, t);
            Check(!dragged.Yielded(t) && smooth.Value == 0.5f, "a new order gives it back to him at once");

            var doorPort = new Port("fireboxDoor", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, "EXT_IN"));
            var door = new FiremanHand("door", doorPort, 3f);
            var halfway = 0f;
            for (t = 0f; t < 1f; t += 0.02f)
            {
                door.Work(1f, t);
                if (t <= 0.17f)
                    halfway = doorPort.Value;
            }
            Check(halfway > 0.2f && halfway < 0.8f && doorPort.Value == 1f,
                $"he swings the door open over a third of a second, not in a jump ({halfway:F2} open at 0.17 s)");
        }

        // SandOnShovelPatch dresses the shovel's load lumps as sand.
        private static void CheckShovelLoads()
        {
            var field = HarmonyLib.AccessTools.Field(typeof(ShovelNonPhysicalCoal), "loadedCoalVisuals");
            Check(field != null && field.FieldType.FullName == "UnityEngine.GameObject[]",
                "the shovel keeps its load lumps in 'loadedCoalVisuals', which a load of flue sand dresses as sand");
        }

        // The mod applies its patches a class at a time: one that fails (here
        // a target that is not there) is named, and the next still applies
        // (a player's report with Remote Dispatch, 2026-10-17: PatchAll had
        // stopped at a failure, leaving the shovel's fixes unpatched).
        private static void CheckPatchEachClass()
        {
            const string id = "RealisticFiring.AdapterCheck.PatchEach";
            var harmony = new HarmonyLib.Harmony(id);
            var errors = new System.Collections.Generic.List<string>();
            var failed = RealisticFiring.Main.PatchEachClass(harmony, new[] { typeof(BrokenProbePatch), typeof(GoodProbePatch) }, errors.Add);
            var patched = PatchProbe.Target() == 2;
            harmony.UnpatchAll(id);
            Check(failed.Count == 1 && failed[0] == typeof(BrokenProbePatch).FullName && errors.Count == 1 && patched,
                "patches apply a class at a time: one that fails is named in the log and the next still applies");
        }

        // The realistic fires fed to Realistic Stack Effects: every field of
        // the plume's reading crosses into its contract's intact.
        private static void CheckStackEffectsFeed()
        {
            var fire = new MarkedFire();
            var reading = default(RealisticStackEffects.Api.StackFireReading);
            new RealisticFiring.StackEffects.Source(fire).Read(0.02f, null, ref reading);
            var fields = typeof(StackReading).GetFields(BindingFlags.Instance | BindingFlags.Public);
            var lost = fields.Where(f =>
            {
                var other = typeof(RealisticStackEffects.Api.StackFireReading).GetField(f.Name);
                return other == null || !Equals(other.GetValue(reading), MarkedFire.Value(f.Name, f.FieldType));
            }).Select(f => f.Name).ToList();
            Check(lost.Count == 0, $"the realistic fires reach Realistic Stack Effects intact, {fields.Length} fields" +
                                   (lost.Count > 0 ? $" (lost: {string.Join(", ", lost)})" : ""));
        }

        private static void CheckPlumeReinitialization()
        {
            var type = typeof(ChimneyPlume);
            var bind = AccessTools.Method(type, "BindInputs");
            Check(bind != null, "a repeated plume setup rebinds the sim inputs separately from creating its effects");
            if (bind == null)
                return;
            var binding = PatchProcessor.GetOriginalInstructions(bind);
            var written = binding.Where(i => i.opcode == System.Reflection.Emit.OpCodes.Stfld)
                .Select(i => (i.operand as FieldInfo)?.Name).ToList();
            Check(new[] { "fire", "beats", "crankRotation", "exhaustPressure", "totalFlow", "exhaustSim", "boilerBroken", "hasLastPosition" }.All(written.Contains)
                  && !written.Any(n => new[] { "smoke", "sootCore", "jet", "heat", "effectsRoot", "volume" }.Contains(n)),
                "a plume rebind replaces the fire, beat phase and every sim port without replacing its effects");
            var instructions = PatchProcessor.GetOriginalInstructions(AccessTools.Method(type, "Attach"));
            int ComponentCall(string name) => instructions.FindIndex(i => i.operand is MethodInfo m && m.Name == name
                && m.IsGenericMethod && m.GetGenericArguments().Contains(type));
            var get = ComponentCall("GetComponent");
            var add = ComponentCall("AddComponent");
            Check(get >= 0 && add > get && instructions.Skip(get).Take(add - get).Any(i => i.opcode == System.Reflection.Emit.OpCodes.Ret),
                "plume attachment checks and returns its existing driver before another component or FX clone can be created");
        }

        // A fire that sets each field of the plume's reading to its own value.
        private sealed class MarkedFire : IStackFire
        {
            public bool Tended => true;

            public void Read(float dt, LocoSim.Implementations.SteamExhaust exhaust, ref StackReading reading)
            {
                object boxed = reading;
                foreach (var field in typeof(StackReading).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    field.SetValue(boxed, Value(field.Name, field.FieldType));
                reading = (StackReading)boxed;
            }

            public static object Value(string name, Type type) =>
                type == typeof(bool) ? (object)true : 1f + Math.Abs(name.GetHashCode() % 1000) / 7f;
        }

        private static void CheckPatchTargets()
        {
            var patches = typeof(RealisticFiring.Main).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length > 0)
                .ToList();
            var bad = new System.Collections.Generic.List<string>();
            foreach (var type in patches)
            {
                try
                {
                    // As Harmony does, skip a patch whose Prepare says no (one for a mod that is not loaded).
                    var prepare = HarmonyLib.AccessTools.DeclaredMethod(type, "Prepare");
                    if (prepare != null && prepare.GetParameters().Length == 0 && !(bool)prepare.Invoke(null, null))
                        continue;
                    var targetMethods = HarmonyLib.AccessTools.DeclaredMethod(type, "TargetMethods");
                    if (targetMethods != null)
                    {
                        var originals = ((System.Collections.Generic.IEnumerable<MethodBase>)targetMethods.Invoke(null, null)).ToList();
                        if (originals.Count == 0 || originals.Any(m => m == null))
                            bad.Add($"{type.Name}: missing target methods");
                        continue;
                    }
                    var targetMethod = HarmonyLib.AccessTools.DeclaredMethod(type, "TargetMethod");
                    if (targetMethod != null)
                    {
                        if (targetMethod.Invoke(null, null) == null)
                            bad.Add($"{type.Name}: no target");
                        continue;
                    }
                    // The class's attributes, merged with each patch method's
                    // own where it names its target, as PatchAll does.
                    var classInfo = HarmonyLib.HarmonyMethodExtensions.GetFromType(type);
                    var methodInfos = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Select(m => HarmonyLib.HarmonyMethodExtensions.GetFromMethod(m))
                        .Where(list => list.Count > 0)
                        .ToList();
                    var targets = methodInfos.Count > 0
                        ? methodInfos.Select(list => HarmonyLib.HarmonyMethod.Merge(classInfo.Concat(list).ToList())).ToList()
                        : new System.Collections.Generic.List<HarmonyLib.HarmonyMethod> { HarmonyLib.HarmonyMethod.Merge(classInfo) };
                    foreach (var info in targets)
                    {
                        var original = info.methodType == HarmonyLib.MethodType.Constructor
                            ? (MethodBase)HarmonyLib.AccessTools.DeclaredConstructor(info.declaringType, info.argumentTypes)
                            : HarmonyLib.AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
                        if (original == null)
                            bad.Add($"{type.Name}: no target for {info.methodName ?? "(unnamed)"}");
                    }
                }
                catch (Exception e)
                {
                    bad.Add($"{type.Name}: {e.GetBaseException().Message}");
                }
            }
            Check(bad.Count == 0, $"all {patches.Count} Harmony patch targets resolve" + (bad.Count > 0 ? $" (not: {string.Join("; ", bad)})" : ""));
        }

        // The aiming keypad, off in 1.11.0, is on by default: settings saved
        // with it off get it on once, and a player's later choice stays.
        private static void CheckNewDefaults()
        {
            var fresh = new Settings();
            var saved = new Settings { ShowAimKeypad = false };
            var turnedOn = saved.ApplyNewDefaults() && saved.ShowAimKeypad;
            saved.ShowAimKeypad = false;
            var choiceKept = !saved.ApplyNewDefaults() && !saved.ShowAimKeypad;
            Check(fresh.ShowAimKeypad && turnedOn && choiceKept,
                "the aiming keypad is on by default, turned on once for settings saved with it off, and a later choice stays");
        }

        private static void CheckShedStandReload()
        {
            var sites = (Dictionary<string, List<UnityEngine.GameObject>>)AccessTools.Field(typeof(Shed.ShedStands), "ByRoundhouse").GetValue(null);
            var live = (UnityEngine.GameObject)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.GameObject));
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(live, new IntPtr(1));
            try
            {
                sites.Add("adapter dead", new List<UnityEngine.GameObject> { null });
                sites.Add("adapter failed build", new List<UnityEngine.GameObject>());
                sites.Add("adapter live", new List<UnityEngine.GameObject> { live });
                Shed.ShedStands.ForgetDestroyedSites();
                Shed.ShedStands.ForgetDestroyedSites();
                Check(!sites.ContainsKey("adapter dead") && !sites.ContainsKey("adapter failed build")
                      && sites.TryGetValue("adapter live", out var kept) && kept.Count == 1 && ReferenceEquals(kept[0], live),
                    "shed reload bookkeeping retries destroyed/failed sites and keeps live stands without duplicating them");
            }
            finally
            {
                sites.Remove("adapter dead");
                sites.Remove("adapter failed build");
                sites.Remove("adapter live");
                AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(live, IntPtr.Zero);
            }
        }

        private static void CheckFireboxQualitySettings()
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Settings));
            using (var reader = new StringReader("<Settings />"))
            {
                var legacy = (Settings)serializer.Deserialize(reader);
                Check(legacy.FireboxQuality == FireboxQuality.High,
                    "settings saved before firebox presets retain High detail");
                Check(legacy.LowWaterAlarmVolume == 1f,
                    "old settings default the low-water alarm volume to normal");
            }
            foreach (var volume in new[] { 0f, 0.5f, 2f })
            {
                using (var writer = new StringWriter())
                {
                    serializer.Serialize(writer, new Settings { LowWaterAlarmVolume = volume });
                    using (var reader = new StringReader(writer.ToString()))
                        Check(((Settings)serializer.Deserialize(reader)).LowWaterAlarmVolume == volume,
                            $"low-water alarm volume {volume:F1} survives settings saves");
                }
            }
            var retained = true;
            foreach (FireboxQuality firebox in Enum.GetValues(typeof(FireboxQuality)))
                foreach (var chimney in EveryValue(new Settings().SmokeQuality))
                {
                    var settings = new Settings { FireboxQuality = firebox, SmokeQuality = chimney, ShowZonedCoalBed = false };
                    using (var writer = new StringWriter())
                    {
                        serializer.Serialize(writer, settings);
                        using (var reader = new StringReader(writer.ToString()))
                        {
                            var restored = (Settings)serializer.Deserialize(reader);
                            restored.ApplyNewDefaults();
                            retained &= restored.FireboxQuality == firebox && restored.SmokeQuality == chimney
                                && !restored.ShowZonedCoalBed;
                        }
                    }
                }
            Check(retained, "all firebox and chimney quality combinations survive settings saves independently of the visual-off switch");
        }

        private static void CheckRetiredBrakeSetting()
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Settings));
            foreach (var oldValue in new[] { "true", "false" })
            {
                using (var reader = new StringReader("<Settings><ClickToBailOff>" + oldValue + "</ClickToBailOff>"
                    + "<FireboxQuality>Low</FireboxQuality><ShovelfulMass>4.5</ShovelfulMass><ShowOverlay>true</ShowOverlay></Settings>"))
                {
                    var restored = (Settings)serializer.Deserialize(reader);
                    using (var writer = new StringWriter())
                    {
                        serializer.Serialize(writer, restored);
                        Check(restored.FireboxQuality == FireboxQuality.Low && restored.ShovelfulMass == 4.5f && restored.ShowOverlay
                              && !writer.ToString().Contains("ClickToBailOff"),
                            $"legacy brake setting ({oldValue}) is ignored on load and omitted on save; other preferences survive");
                    }
                }
            }
        }

        // Both cores build the shared SmokeQuality, which this check sees twice:
        // an enum's values by the type of a value at hand.
        private static IEnumerable<T> EveryValue<T>(T _) where T : Enum => Enum.GetValues(typeof(T)).Cast<T>();

        private static void CheckFiremanOrders()
        {
            var oldClient = Net.ClientCheck;
            var oldSender = Net.Sender;
            // Native Unity objects are not needed here. A nonzero cached pointer
            // lets Net's null check recognize these synthetic car identities.
            Sim.FiremanRig Rig()
            {
                var car = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
                AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(car, new IntPtr(1));
                var rig = (Sim.FiremanRig)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sim.FiremanRig));
                AccessTools.Field(typeof(Sim.FiremanRig), "<Car>k__BackingField").SetValue(rig, car);
                AccessTools.Field(typeof(Sim.FiremanRig), "<CoalFireman>k__BackingField").SetValue(rig, new AutoFireman());
                return rig;
            }
            try
            {
                var a = Rig();
                var b = Rig();
                var packets = new List<(TrainCar Car, FireAction Action, float Value)>();
                Net.ClientCheck = () => true;
                Net.Sender = (car, action, zone, direction, value) => packets.Add((car, action, value));
                FiremanOrders.Order(a, FiremanMode.Road);
                FiremanOrders.Order(a, FiremanMode.Road);
                Check(packets.Count == 1 && ReferenceEquals(packets[0].Car, a.Car)
                      && packets[0].Action == FireAction.FiremanMode && packets[0].Value == (float)FiremanMode.Road
                      && FiremanOrders.ModeOf(a) == FiremanMode.Road && FiremanOrders.ModeOf(b) == FiremanMode.Off
                      && a.Fireman.Mode == FiremanMode.Off,
                    "tablet and F7 share per-engine client orders, send once, and leave the client's local fireman alone");
                FiremanOrders.Order(a, FiremanMode.Off);
                FiremanOrders.Call(b, FiremanCall.HeavyAhead);
                Check(packets.Count == 3 && packets[1].Value == (float)FiremanMode.Off
                      && ReferenceEquals(packets[2].Car, b.Car) && packets[2].Action == FireAction.FiremanCall
                      && packets[2].Value == (float)FiremanCall.HeavyAhead && b.Fireman.Call == FiremanCall.None,
                    "tablet orders can stop the host fireman and send engineer's calls to the selected engine");
                Net.ClientCheck = () => false;
                FiremanOrders.Call(a, FiremanCall.HeavyAhead);
                var called = FiremanOrders.CallActive(a, FiremanCall.HeavyAhead) && a.Fireman.CallLeft > 0f;
                FiremanOrders.Call(a, FiremanCall.HeavyAhead);
                var cancelled = a.Fireman.Call == FiremanCall.None;
                FiremanOrders.Call(a, FiremanCall.EasingOff);
                Check(called && cancelled && FiremanOrders.CallActive(a, FiremanCall.EasingOff)
                      && packets.Count == 3 && b.Fireman.Call == FiremanCall.None,
                    "host engineer's calls activate, cancel on a second press, and stay local to their engine");
            }
            finally
            {
                Net.ClientCheck = oldClient;
                Net.Sender = oldSender;
            }
        }

        private static void CheckFiringTabletVrButtonSpec()
        {
            // The stock VR control dereferences the hand poses during Awake.
            // Exercise the production initializer without a Unity scene.
            var spec = (DV.CabControls.Spec.Button)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DV.CabControls.Spec.Button));
            Items.FiringTabletSetup.ConfigurePageButton(spec, null);
            var poses = spec.handPosesOverride;
            Check(poses != null && poses.nearTouchPose == DV.Interaction.HandPose.Generic
                  && poses.touchPose == DV.Interaction.HandPose.Generic && poses.grabPose == DV.Interaction.HandPose.Generic
                  && spec.colliderGameObjects.Length == 1 && !spec.createRigidbody && !spec.useJoints,
                "runtime tablet tabs initialize VR hand poses before control Awake and do not add a second rigidbody");
        }

        private static void CheckFireIronLoading()
        {
            var targets = Items.FireIronLoading.TargetMethods().ToList();
            Check(targets.Count == 2 && targets.All(m => m != null && m.DeclaringType.Assembly == typeof(StartingItemsController).Assembly),
                "fire iron patches only saved-item and purchased-item game methods");
            var resourceLoad = Items.FireIronLoading.ResourceLoad;
            foreach (var target in targets.Where(m => m != null))
            {
                var original = HarmonyLib.PatchProcessor.GetOriginalInstructions(target);
                var patched = Items.FireIronLoading.Transpiler(original, target).ToList();
                var loadIndexes = Enumerable.Range(0, original.Count).Where(i => original[i].Calls(resourceLoad)).ToList();
                var replacement = HarmonyLib.AccessTools.DeclaredMethod(typeof(Items.FireIronLoading), "LoadPrefab");
                Check(loadIndexes.Count == 1 && patched.Count == original.Count
                      && loadIndexes.All(i => patched[i].Calls(replacement)
                          && patched[i].labels.SequenceEqual(original[i].labels)
                          && patched[i].blocks.SequenceEqual(original[i].blocks))
                      && Enumerable.Range(0, original.Count).Except(loadIndexes).All(i => ReferenceEquals(patched[i], original[i])),
                    $"{target.DeclaringType.Name}.{target.Name}: redirects only the prefab lookup, preserving the stock item lifecycle");
            }
            var patches = typeof(RealisticFiring.Main).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length > 0);
            Check(!patches.Any(t => HarmonyLib.HarmonyMethodExtensions.GetFromType(t).Any(info => info.declaringType == typeof(UnityEngine.Resources)))
                  && !targets.Contains(resourceLoad), "no global Unity Resources.Load patch");
        }

        private static void CheckFiringTablet()
        {
            var item = typeof(DV.Customization.Gadgets.GadgetItem);
            var gadget = typeof(DV.Customization.Gadgets.GadgetBase);
            Check(AccessTools.Field(item, "gadgetPrefab")?.FieldType == gadget
                  && AccessTools.Field(item, "onlyPlaceInOneAxis")?.FieldType == typeof(bool)
                  && AccessTools.Field(gadget, "boundsCenter")?.FieldType == typeof(UnityEngine.Vector3)
                  && AccessTools.Field(gadget, "boundsSize")?.FieldType == typeof(UnityEngine.Vector3)
                  && AccessTools.Field(gadget, "requiredMountPoints")?.FieldType == typeof(int)
                  && AccessTools.Field(gadget, "removalMethod")?.FieldType == typeof(DV.Customization.Gadgets.GadgetBase.GadgetRemovalMethod)
                  && AccessTools.Field(gadget, "vrIgnoreColliders")?.FieldType == typeof(UnityEngine.Collider[])
                  && AccessTools.Field(gadget, "highlightMeshes")?.FieldType == typeof(UnityEngine.MeshFilter[])
                  && AccessTools.Field(gadget, "requirements")?.FieldType == typeof(DV.Customization.TrainCarCustomization.TrainCarCustomizerBase.TrainCarRequirements),
                "tablet's stock gadget configuration fields resolve with their expected types");
            Check(typeof(Items.FiringTabletGadget).IsSubclassOf(gadget)
                  && AccessTools.Method(item, "OnItemSaveDataRequestedInternal") != null
                  && AccessTools.Method(item, "OnItemSaveDataLoadedInternal") != null,
                "tablet uses the stock item-to-gadget serialization lifecycle");
            Check(Items.FiringTabletGadget.SavedPage(new JObject()) == 0
                  && Items.FiringTabletGadget.SavedPage(new JObject { ["rfTabletPage"] = "bad" }) == 0
                  && Items.FiringTabletGadget.SavedPage(new JObject { ["rfTabletPage"] = -12 }) == 0
                  && Items.FiringTabletGadget.SavedPage(new JObject { ["rfTabletPage"] = 1 }) == 1
                  && Items.FiringTabletGadget.SavedPage(new JObject { ["rfTabletPage"] = 2 }) == 2
                  && Items.FiringTabletGadget.SavedPage(new JObject { ["rfTabletPage"] = 3 }) == 3
                  && Items.FiringTabletGadget.SavedPage(new JObject { ["rfTabletPage"] = long.MaxValue }) == 3
                  && FireOverlay.Pages == 3,
                "saved tablet restores all four pages without adding a fireman page to F6; missing, invalid, and out-of-range data stays bounded");
            var framed = true;
            foreach (var fov in new[] { 30f, 31f, 45f, 65f, 90f, 110f, 120f })
                foreach (var aspect in new[] { 4d / 3d, 16d / 9d, 21d / 9d })
                {
                    var at = Items.FiringTabletHold.CameraOffset(fov, (float)aspect);
                    var halfHeight = (at.z + Items.FiringTabletScreen.ButtonFront) * Math.Tan(fov * Math.PI / 360d);
                    framed &= at.z > 0.1f
                        && at.x - Items.FiringTablet.Size.x * 0.5f > 0.03f
                        && at.y + Items.FiringTablet.Size.y * 0.5f < halfHeight
                        && at.y - Items.FiringTablet.Size.y * 0.5f > -halfHeight
                        && Math.Abs(at.x) + Items.FiringTablet.Size.x * 0.5f < halfHeight * aspect;
                }
            Check(framed, "desktop tablet keeps the placement ray clear and its whole shell in view at 30–120 degree FOV and 4:3–21:9 aspect ratios");
            var settings = new Settings { ShowOverlay = false, OverlayPage = 2 };
            FireOverlay.Step(settings);
            var first = settings.ShowOverlay && settings.OverlayPage == 0;
            FireOverlay.Step(settings);
            FireOverlay.Step(settings);
            FireOverlay.Step(settings);
            Check(first && !settings.ShowOverlay && settings.OverlayPage == 2 && !settings.GiveFiringTablet,
                "F6 retains its show/three-pages/hide cycle and does not create a tablet");
        }

        private static List<StartingItems.StartingItem> tabletStartingKit;

        private static bool TabletStartingKitPrefix(ref List<StartingItems.StartingItem> __result)
        {
            __result = tabletStartingKit;
            return false;
        }

        private static void CheckFiringManual()
        {
            var rows = Items.FiringManualContents.Entries;
            Check(rows.Length == 15 && Items.FiringManualContents.PageCount == 17
                  && Items.FiringManualContents.ContentsPage == 1,
                "the manual preserves O1's cover and fifteen lessons, with contents inserted after the cover");
            for (var i = 0; i < rows.Length; i++)
            {
                Check(Items.FiringManualContents.TryDestination(i, 1, out var target) && target == i + 2
                      && rows[i].Area.width > 0 && rows[i].Area.yMin >= 0
                      && rows[i].Area.yMax < Items.FiringManualContents.Height,
                    $"manual contents row {i + 1} opens its printed page {i + 2}");
            }
            Check(!Items.FiringManualContents.TryDestination(-1, 1, out _)
                  && !Items.FiringManualContents.TryDestination(15, 1, out _)
                  && !Items.FiringManualContents.TryDestination(0, 0, out _)
                  && !Items.FiringManualContents.TryDestination(0, 2, out _),
                "manual links reject non-contents pages and invalid rows");
            var top = Items.FiringManualLinks.PrintedPoint(new UnityEngine.Vector2(0, 0));
            var bottom = Items.FiringManualLinks.PrintedPoint(new UnityEngine.Vector2(1184, 1684));
            Check(top.x > 0 && top.z < 0 && bottom.x < 0 && bottom.z > 0,
                "manual print maps left to +x and top to -z, the portrait orientation confirmed in the brake booklet");
            var touch = (DV.CabControls.Spec.Touchscreen)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DV.CabControls.Spec.Touchscreen));
            touch.handPosesOverride = new DV.Interaction.InteractionHandPoses();
            var originalPoses = touch.handPosesOverride;
            touch.createRigidbody = true;
            touch.maxVRTolerance = 1f;
            touch.hoverVerticalOffset = 0f;
            Items.FiringManualControl.ConfigureGrid(touch, rows.Length);
            Check(touch.handPosesOverride != null && touch.gridSize.x == 1 && touch.gridSize.y == rows.Length
                  && touch.createRigidbody && ReferenceEquals(touch.handPosesOverride, originalPoses)
                  && touch.maxVRTolerance == 1f && touch.hoverVerticalOffset == 0f
                  && !touch.flipHorizontalCoords && !touch.flipVerticalCoords && touch.useEntireScreen,
                "manual grid override preserves the stock touchscreen poses, rigidbody, VR tolerance, and hover height");
            Check(typeof(PageBook).GetMethod("FlipTo", new[] { typeof(int) }) != null
                  && typeof(PageBook).GetMethod("OnItemSaveDataRequested")?.ReturnType == typeof(JObject)
                  && typeof(PageBook).GetEvent("PageFlipped") != null
                  && Items.FiringManual.SourcePrefab == "VehicleCatalog",
                "manual retains the stock portrait catalog's grips, page-flip API, and page-save contract");
            var book = (PageBook)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PageBook));
            book.currentPage = 13;
            var saved = book.OnItemSaveDataRequested(new JObject());
            Check((int)saved["BookletPageNumber"] == 13, "manual page 13 saves with the stock BookletPageNumber key");
            book.currentPage = 0;
            Check(book.OnItemSaveDataRequested(saved)["BookletPageNumber"] == null, "returning to the cover clears the saved page as stock booklets do");
            InventoryItemSpec Spec(string name)
            {
                var value = (InventoryItemSpec)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(InventoryItemSpec));
                AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(value, name);
                return value;
            }
            var manual = Spec(Items.FiringManual.PrefabName);
            var basic = new List<StartingItems.StartingItem>();
            var steam = new List<StartingItems.StartingItem> { new StartingItems.StartingItem { item = Spec("shovel") } };
            var kit = Items.FiringManualStartingItems.WithManual(steam, () => manual);
            Check(ReferenceEquals(basic, Items.FiringManualStartingItems.WithManual(basic, () => throw new Exception("unused")))
                  && kit.Count == 2 && steam.Count == 1 && kit[1].ItemPrefabName == Items.FiringManual.PrefabName
                  && ReferenceEquals(kit, Items.FiringManualStartingItems.WithManual(kit, () => throw new Exception("duplicate")))
                  && ReferenceEquals(steam, Items.FiringManualStartingItems.WithManual(steam, () => null)),
                "steam starting kits gain one manual without mutating assets or duplicating an existing entry; missing templates are safe");
        }

        private static void CheckFiringTabletStartingItems()
        {
            var prefabName = AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName");
            InventoryItemSpec Spec(string name)
            {
                // Only the serialized prefab name is needed; no Unity object is created.
                var spec = (InventoryItemSpec)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(InventoryItemSpec));
                prefabName.SetValue(spec, name);
                return spec;
            }
            var radio = new StartingItems.StartingItem { item = Spec("Radio"), preferredRelativeSlot = 0 };
            var shovel = new StartingItems.StartingItem { item = Spec("shovel"), preferredRelativeSlot = 3 };
            var tablet = Spec(Items.FiringTablet.PrefabName);
            var basic = new List<StartingItems.StartingItem> { radio };
            var steam = new List<StartingItems.StartingItem> { radio, shovel };
            var built = 0;
            InventoryItemSpec Tablet() { built++; return tablet; }
            Check(ReferenceEquals(basic, Items.FiringTabletStartingItems.WithTablet(basic, Tablet)) && built == 0,
                "a starting kit without the shovel stays unchanged and never builds a tablet");
            tabletStartingKit = Items.FiringTabletStartingItems.WithTablet(steam, Tablet);
            Check(built == 1 && tabletStartingKit.Count == 3 && steam.Count == 2
                  && tabletStartingKit.Select(i => i.ItemPrefabName).Take(2).SequenceEqual(steam.Select(i => i.ItemPrefabName))
                  && tabletStartingKit.Last().ItemPrefabName == Items.FiringTablet.PrefabName
                  && tabletStartingKit.Last().allowDuplicates,
                "the steam kit gets one purchasable tablet in a copy, preserving the shared asset and existing item order");
            Check(ReferenceEquals(tabletStartingKit, Items.FiringTabletStartingItems.WithTablet(tabletStartingKit, Tablet)) && built == 1,
                "a starting kit already containing the tablet does not add or build another");
            Check(ReferenceEquals(steam, Items.FiringTabletStartingItems.WithTablet(steam, () => null))
                  && Items.FiringTabletStartingItems.WithTablet(null, Tablet) == null,
                "a missing tablet template or missing kit leaves the stock loader's result intact");

            var target = AccessTools.DeclaredMethod(typeof(StartingItemsController), "GetStartingItemsPrefabs");
            var safeguard = AccessTools.DeclaredMethod(typeof(StartingItemsController), "StartingItemsSafeguard");
            Check(target?.ReturnType == typeof(List<StartingItems.StartingItem>) && safeguard != null,
                "tablet starting equipment hooks the resolved kit used by the stock save safeguard");
            if (target == null || safeguard == null)
                return;
            var harmony = new Harmony("RealisticFiring.AdapterCheck.TabletStartingKit");
            try
            {
                // Feed the extended kit to the real stock safeguard, without loading Unity resources.
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Program), nameof(TabletStartingKitPrefix)));
                var controller = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(StartingItemsController));
                StorageItemData Saved(string name, int slot = -1) => new StorageItemData(name,
                    UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity, true, inventorySlotIndex: slot);
                var inventory = new List<StorageItemData>();
                var combined = new List<StorageItemData> { Saved("Radio", 0), Saved("shovel", 3), Saved("other", 12) };
                safeguard.Invoke(controller, new object[] { inventory, combined, null, true });
                Check(inventory.Count == 1 && inventory[0].itemPrefabName == Items.FiringTablet.PrefabName
                      && inventory[0].inventorySlotIndex == 13 && inventory[0].belongsToPlayer,
                    "the stock safeguard grants the tablet in the next free backpack slot without displacing existing gear");
                var fullBackpack = Enumerable.Range(0, 36).Where(slot => slot < 4 || slot == 11 || slot >= 12)
                    .Select(slot => Saved("gear-" + slot, slot)).ToList();
                fullBackpack.Single(i => i.inventorySlotIndex == 0).itemPrefabName = "Radio";
                fullBackpack.Single(i => i.inventorySlotIndex == 35).itemPrefabName = "shovel";
                var overflow = new List<StorageItemData>();
                safeguard.Invoke(controller, new object[] { overflow, fullBackpack, null, true });
                Check(overflow.Count == 1 && overflow[0].itemPrefabName == Items.FiringTablet.PrefabName
                      && overflow[0].inventorySlotIndex == 4 && fullBackpack.Count == 29,
                    "an Engineer-style full backpack puts the tablet in a free hotbar slot without replacing any starting gear");
                inventory.Clear();
                combined.Add(Saved(Items.FiringTablet.PrefabName));
                safeguard.Invoke(controller, new object[] { inventory, combined, null, false });
                Check(inventory.Count == 0 && combined.Count == 4,
                    "a tablet saved outside inventory, including a mounted gadget, prevents another starter tablet");
                combined.Add(Saved(Items.FiringTablet.PrefabName, 14));
                safeguard.Invoke(controller, new object[] { inventory, combined, null, false });
                Check(inventory.Count == 0 && combined.Count == 5,
                    "the stock starting-kit safeguard preserves additional purchased tablets");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                tabletStartingKit = null;
            }
        }

        // Skin Manager rewrites the game's saved-item and purchased-item
        // methods to make items through its own loaders: with its patches in
        // first, as Info.json's LoadAfter has it, the fire iron's lookup must
        // be redirected in those loaders, and the game methods keep Skin
        // Manager's calls.
        private static void CheckSkinManagerFireIron(string path)
        {
            const string skinId = "SkinManagerMod";
            const string mineId = "RealisticFiring.AdapterCheck.SkinManager";
            var resolve = ModResolver(path);
            AppDomain.CurrentDomain.AssemblyResolve += resolve;
            try
            {
                var assembly = Assembly.LoadFrom(path);
                var loaders = Items.SkinManagerFireIronLoading.Targets();
                var resourceLoad = Items.FireIronLoading.ResourceLoad;
                var replacement = HarmonyLib.AccessTools.DeclaredMethod(typeof(Items.FireIronLoading), "LoadPrefab");
                Check(loaders.Count == 2 && Items.SkinManagerFireIronLoading.Found && Items.SkinManagerFireIronLoading.Prepare(),
                    $"Skin Manager (assembly {assembly.GetName().Version}): both item loaders found");
                foreach (var loader in loaders)
                {
                    var original = HarmonyLib.PatchProcessor.GetOriginalInstructions(loader);
                    var patched = Items.SkinManagerFireIronLoading.Transpiler(original, loader).ToList();
                    var loads = Enumerable.Range(0, original.Count).Where(i => original[i].Calls(resourceLoad)).ToList();
                    Check(loads.Count == 1 && patched.Count == original.Count && loads.All(i => patched[i].Calls(replacement))
                          && Enumerable.Range(0, original.Count).Except(loads).All(i => ReferenceEquals(patched[i], original[i])),
                        $"Skin Manager's {loader.DeclaringType.Name}.{loader.Name}: redirects only its prefab lookup");
                }

                var skin = new HarmonyLib.Harmony(skinId);
                foreach (var name in new[] { "SkinManagerMod.Patches.ShopPatches", "SkinManagerMod.Patches.InventoryPatches" })
                    skin.CreateClassProcessor(assembly.GetType(name, true)).Patch();
                var mine = new HarmonyLib.Harmony(mineId);
                mine.CreateClassProcessor(typeof(Items.FireIronLoading)).Patch();
                mine.CreateClassProcessor(typeof(Items.SkinManagerFireIronLoading)).Patch();
                mine.CreateClassProcessor(typeof(Items.PurchaseGuard)).Patch();
                var games = Items.FireIronLoading.TargetMethods().ToList();
                var gamesKeepSkin = games.All(game =>
                {
                    var current = HarmonyLib.PatchProcessor.GetCurrentInstructions(game);
                    return !current.Any(i => i.Calls(resourceLoad))
                           && current.Any(i => i.operand is MethodInfo m && loaders.Contains(m));
                });
                var loadersRedirected = loaders.All(loader =>
                {
                    var current = HarmonyLib.PatchProcessor.GetCurrentInstructions(loader);
                    return current.Count(i => i.Calls(replacement)) == 1 && !current.Any(i => i.Calls(resourceLoad));
                });
                Check(gamesKeepSkin && loadersRedirected,
                    "with Skin Manager patched first, the game methods make items through its loaders and the fire iron is found there");
            }
            catch (Exception e)
            {
                Check(false, "Skin Manager fire iron lookup: " + e.GetBaseException().Message);
            }
            finally
            {
                new HarmonyLib.Harmony(mineId).UnpatchAll(mineId);
                new HarmonyLib.Harmony(skinId).UnpatchAll(skinId);
                AppDomain.CurrentDomain.AssemblyResolve -= resolve;
            }
        }

        // Resolves an installed mod's dependencies from its folder, the
        // game's Managed folder and Unity Mod Manager's, and the other mods'
        // folders, without adding the game's mscorlib to MONO_PATH and
        // replacing Mono's own core library.
        // DV Seasons: the state the chimney's steam and the mod's ambient read
        // by reflection is where SeasonsMod looks for it, and the stock
        // boiler's cooling it retargets still has its one 25 °C, so a cold
        // boiler matches what the stock boiler cools toward.
        private static void CheckDvSeasons(string path)
        {
            var resolve = ModResolver(path);
            AppDomain.CurrentDomain.AssemblyResolve += resolve;
            try
            {
                var assembly = Assembly.LoadFrom(path);
                var (runtime, currentState) = SeasonsMod.Resolve(assembly.GetType(SeasonsMod.MainType));
                var state = currentState?.PropertyType;
                Check(runtime != null && currentState != null && state?.FullName == SeasonsMod.StateType
                      && state.GetProperty(SeasonsMod.TemperatureProperty)?.PropertyType == typeof(float)
                      && new[] { "Current", "Next", "Transition", "SnowAmount" }.All(p => state.GetProperty(p) != null),
                    $"DV Seasons (assembly {assembly.GetName().Version}): its runtime's current state and temperature are where the mod reads them");
                var cooling = HarmonyLib.AccessTools.Method(typeof(Boiler), "SimulateSteamConsumption");
                var ambient = cooling != null
                    ? HarmonyLib.PatchProcessor.GetOriginalInstructions(cooling)
                        .Count(i => i.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && i.operand is float f && Math.Abs(f - SimConsts.OUTSIDE_TEMPERATURE) < 1e-4f)
                    : 0;
                Check(ambient == 1, "the stock boiler cools toward one outside temperature, which DV Seasons retargets and the mod's cold boiler follows");
            }
            catch (Exception e)
            {
                Check(false, $"DV Seasons could not be checked: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolve;
            }
        }

        private static ResolveEventHandler ModResolver(string path)
        {
            var modFolder = Path.GetDirectoryName(path);
            var game = Environment.GetEnvironmentVariable("DV_INSTALL_DIR")
                       ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/Steam/steamapps/common/Derail Valley");
            var managed = Path.Combine(game, "DerailValley_Data", "Managed");
            var mods = Path.Combine(game, "Mods");
            var folders = new[] { modFolder, managed, Path.Combine(managed, "UnityModManager") }
                .Concat(Directory.Exists(mods) ? Directory.GetDirectories(mods) : new string[0]).ToArray();
            return (_, request) =>
            {
                var name = new AssemblyName(request.Name).Name;
                if (name == "mscorlib" || name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal))
                    return null;
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
                if (loaded != null)
                    return loaded;
                foreach (var folder in folders)
                {
                    var file = Path.Combine(folder, name + ".dll");
                    if (File.Exists(file))
                        return Assembly.LoadFrom(file);
                }
                return null;
            };
        }

        // The load-hang guard patches the game's pool coroutine and TrainCar.Awake
        // with finalizers: Harmony must accept both against the installed game.
        // A purchase that fails must free the registers: the guard's finalizer
        // installs beside the fire iron's transpiler on the shops' coroutine,
        // and the fields it recovers with are there.
        private static void CheckPurchaseGuard()
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.Purchase");
            try
            {
                harmony.CreateClassProcessor(typeof(Items.FireIronLoading)).Patch();
                harmony.CreateClassProcessor(typeof(Items.PurchaseGuard)).Patch();
                // Skin Manager is not loaded yet here: its class skips itself.
                var skinPatched = harmony.CreateClassProcessor(typeof(Items.SkinManagerFireIronLoading)).Patch();
                Check(!Items.SkinManagerFireIronLoading.Prepare() && (skinPatched == null || skinPatched.Count == 0)
                      && !Items.SkinManagerFireIronLoading.Found,
                    "without Skin Manager its fire iron lookup patches nothing and does not fail");
                var info = HarmonyLib.Harmony.GetPatchInfo(Items.PurchaseGuard.TargetMethod());
                Check(info?.Finalizers.Count == 1 && info.Transpilers.Count == 1 && Items.PurchaseGuard.Resolves,
                    "a failed purchase frees the registers: the guard installs beside the iron's lookup and finds the shops' queue");
            }
            catch (Exception e)
            {
                Check(false, $"the purchase guard installs ({e.GetType().Name}: {e.Message})");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private static void CheckCarPoolGuard()
        {
            var harmony = new HarmonyLib.Harmony("RealisticFiring.AdapterCheck.CarPool");
            try
            {
                harmony.CreateClassProcessor(typeof(RealisticFiring.Patches.CarPoolGuard)).Patch();
                harmony.CreateClassProcessor(typeof(RealisticFiring.Patches.TrainCarAwakeReport)).Patch();
                var pool = HarmonyLib.AccessTools.EnumeratorMoveNext(HarmonyLib.AccessTools.Method(typeof(CarSpawner), "InitPoolCoro"));
                var awake = HarmonyLib.AccessTools.Method(typeof(TrainCar), "Awake");
                Check(HarmonyLib.Harmony.GetPatchInfo(pool)?.Finalizers.Count == 1 && HarmonyLib.Harmony.GetPatchInfo(awake)?.Finalizers.Count == 1,
                    "the load-hang guard's finalizers install on the car pool's coroutine and TrainCar.Awake");
            }
            catch (Exception e)
            {
                Check(false, $"the load-hang guard's finalizers install ({e.GetType().Name}: {e.Message})");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
    }

    internal static class PatchProbe
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static int Target() => 1;
    }

    [HarmonyLib.HarmonyPatch(typeof(PatchProbe), "NoSuchMethod")]
    internal static class BrokenProbePatch
    {
        private static void Postfix()
        {
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(PatchProbe), nameof(PatchProbe.Target))]
    internal static class GoodProbePatch
    {
        private static void Postfix(ref int __result) => __result = 2;
    }
}
