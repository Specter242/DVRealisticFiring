using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DV.Simulation.Controllers;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using LocoSim.Resources;
using Newtonsoft.Json.Linq;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    // Exercise the production stoker, resource controller and MP packet reader
    // without creating native Unity objects or opening a network session.
    internal static class MultiplayerStokerChecks
    {
        private static readonly Dictionary<RealisticStoker, CoalPileSimController> Piles = new Dictionary<RealisticStoker, CoalPileSimController>();
        private static float modifier;
        private static bool FindPile(RealisticStoker __instance, ref CoalPileSimController __result)
        {
            if (!Piles.TryGetValue(__instance, out var pile)) return true;
            __result = pile;
            return false;
        }
        private static bool ConsumptionModifier(ref float __result) { __result = modifier; return false; }

        internal static void Run(Assembly multiplayer, Action<bool, string> check)
        {
            const string owner = "RealisticFiring.AdapterCheck.StokerCoal";
            var harmony = new Harmony(owner);
            var wasClient = Net.ClientCheck;
            var hadJams = RealisticFiring.Main.Settings.StokerJams;
            var piles = new List<CoalPileSimController>();
            try
            {
                // This checks resource transport, not random screw jams.
                // A jam during its six-second delivery window masks the
                // network result; jam handling has separate Core coverage.
                RealisticFiring.Main.Settings.StokerJams = false;
                harmony.Patch(AccessTools.Method(typeof(RealisticStoker), "TenderPile"), prefix: new HarmonyMethod(typeof(MultiplayerStokerChecks), nameof(FindPile)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(CoalPileSimController), "ConsumptionModifier"), prefix: new HarmonyMethod(typeof(MultiplayerStokerChecks), nameof(ConsumptionModifier)));
                foreach (var scale in new[] { 0f, .5f, 1f, 2f })
                {
                    modifier = scale;
                    Net.ClientCheck = () => false;
                    var hostCoal = Container(1000f);
                    var clientCoal = Container(1000f);
                    // A small positive relay must not limit the screw on every
                    // frame while a full real pile is available behind it.
                    var relay = Port("tenderCoal", "AMOUNT", PortType.READONLY_OUT, PortValueType.COAL, .001f);
                    var consume = Port("tenderCoal", "CONSUME_EXT_IN", PortType.EXTERNAL_IN, PortValueType.COAL);
                    var engine = Port("stokerControl", "EXT_IN", PortType.EXTERNAL_IN, PortValueType.CONTROL);
                    var reverse = Port("rfStokerReverse", "EXT_IN", PortType.EXTERNAL_IN, PortValueType.CONTROL, .5f);
                    var jets = StokerJetControls.Valves.Select(v => Port(v.Id, "EXT_IN", PortType.EXTERNAL_IN, PortValueType.CONTROL, 0f)).ToArray();
                    var host = Fire();
                    var client = Fire();
                    var stoker = Stoker(host, engine, relay, consume, reverse, jets);
                    var pile = Pile(hostCoal); piles.Add(pile); Piles.Add(stoker, pile);
                    var enginePorts = jets.Concat(new[] { engine, reverse }).ToArray();
                    ApplyPorts(multiplayer, enginePorts, enginePorts.Select(p => p.id == engine.id ? .7f : p.id == reverse.id ? 1f : .8f).ToArray());
                    var drawnFuel = 0f;
                    for (var i = 0; i < 300; i++)
                    { stoker.Tick(.02f); drawnFuel += host.LastStoker.Value.Drawn; }
                    var drawn = 1000f - hostCoal.amountReadOut.Value;
                    var fed = host.Model.CoalMass - 10f;
                    check(stoker.Direction == Core.StokerDirection.Forward && host.LastStoker.Value.ManifoldPressure > 0f
                          && stoker.CoalReached > 20f && fed > 2f,
                        $"guest engine, reverse and jet controls feed the host fire despite a stale positive tender relay (modifier {scale}, {fed:F3} kg fed)");
                    check(Math.Abs(drawn - 4f * drawnFuel * scale) < .05f && consume.Value == 0f,
                        $"host stoker draws once from the real coal pile, without sending consumption through the engine relay (modifier {scale})");
                    if (scale == 1f)
                    {
                        // A working host fire, not just a coal counter. Keep
                        // the draft and pressure fixed to isolate fuel supply.
                        host.PrepareFire();
                        host.GetAllPortReferences().Single(r => r.id == "firebox.AIR_FLOW")
                            .SetPortReference(Port("exhaust", "AIR_FLOW", PortType.READONLY_OUT, PortValueType.MASS_RATE, 4f));
                        host.GetAllPortReferences().Single(r => r.id == "firebox.BOILER_PRESSURE")
                            .SetPortReference(Port("boiler", "PRESSURE", PortType.READONLY_OUT, PortValueType.PRESSURE, 15f));
                        ApplyPorts(multiplayer, new[] { engine }, new[] { .25f });
                        var peakHeat = 0f;
                        var peakTemperature = 0f;
                        for (var i = 0; i < 3000; i++)
                        {
                            stoker.Tick(.02f); host.Tick(.02f);
                            peakHeat = Math.Max(peakHeat, host.Model.Heat);
                            peakTemperature = Math.Max(peakTemperature, host.Model.Temperature);
                        }
                        check(host.Model.FireOn && peakTemperature > 500f && peakHeat > 0f,
                            $"guest-worked stoker supplies a burning host fire (peak {peakTemperature:F0} C, {peakHeat / 1e6f:F2} MW; end {host.Model.CoalMass:F2} kg, {host.Model.BurningMass:F2} burning, {host.AirFlowIn:F1} kg/s air)");
                    }
                    ApplyPorts(multiplayer, new[] { clientCoal.amountReadOut, clientCoal.normalizedReadOutPort },
                        new[] { hostCoal.amountReadOut.Value, hostCoal.normalizedReadOutPort.Value });
                    var mirror = ClientMirror.Capture(host, null, null, null, null);
                    ClientMirror.Apply(mirror, client, null, null, null, null, "stoker guest");
                    check(clientCoal.amountReadOut.Value == hostCoal.amountReadOut.Value
                          && Math.Abs(client.Model.CoalMass - host.Model.CoalMass) < .001f
                          && client.LastStoker.Value.Fed == host.LastStoker.Value.Fed,
                        $"host tender amount and stoked fire reach the guest together (modifier {scale})");
                    var stoppedCoal = hostCoal.amountReadOut.Value;
                    ApplyPorts(multiplayer, new[] { reverse }, new[] { 0f });
                    stoker.Tick(.02f);
                    var reversed = stoker.Direction == Core.StokerDirection.Reverse && host.LastStoker.Value.Drawn == 0f;
                    ApplyPorts(multiplayer, new[] { reverse }, new[] { .5f });
                    stoker.Tick(.02f);
                    check(reversed && stoker.Direction == Core.StokerDirection.Neutral && host.LastStoker.Value.Drawn == 0f
                          && hostCoal.amountReadOut.Value == stoppedCoal,
                        "guest reverse and neutral reach the host without drawing tender fuel");
                    ApplyPorts(multiplayer, new[] { reverse }, new[] { 1f });
                    var before = host.Model.CoalMass;
                    var coalBefore = hostCoal.amountReadOut.Value;
                    Net.ClientCheck = () => true;
                    stoker.Tick(1f);
                    check(host.Model.CoalMass == before && hostCoal.amountReadOut.Value == coalBefore,
                        "guest stoker ticks neither feed nor consume coal a second time");
                    Net.ClientCheck = () => false;
                    hostCoal.amountReadOut.Value = 0f;
                    relay.Value = 10000f;
                    for (var i = 0; i < 100; i++) stoker.Tick(.02f);
                    check(stoker.CoalReached == 0f && host.LastStoker.Value.Drawn == 0f,
                        "an empty real pile stops the screw drawing fuel even if the relayed coal amount is stale and high");
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Stoker transport fixture: " + e);
                throw;
            }
            finally
            {
                RealisticFiring.Main.Settings.StokerJams = hadJams;
                Net.ClientCheck = wasClient;
                harmony.UnpatchAll(owner);
                Piles.Clear();
                foreach (var pile in piles) AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(pile, IntPtr.Zero);
            }
        }

        private static ResourceContainer Container(float amount) => new ResourceContainer("coal", ResourceContainerType.COAL, amount, 1000f,
            new PortDefinition(PortType.EXTERNAL_IN, PortValueType.COAL, "REFILL_EXT_IN"),
            new PortDefinition(PortType.EXTERNAL_IN, PortValueType.COAL, "CONSUME_EXT_IN"),
            new PortDefinition(PortType.READONLY_OUT, PortValueType.COAL, "AMOUNT"),
            new PortDefinition(PortType.READONLY_OUT, PortValueType.COAL, "NORMALIZED"),
            new PortDefinition(PortType.READONLY_OUT, PortValueType.COAL, "CAPACITY"));

        private static CoalPileSimController Pile(ResourceContainer coal)
        {
            var pile = (CoalPileSimController)FormatterServices.GetUninitializedObject(typeof(CoalPileSimController));
            // Only Unity's null test reads this marker; no native method is
            // called on this controller. Its actual managed coal methods run.
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(pile, new IntPtr(1));
            AccessTools.Field(typeof(CoalPileSimController), "coalAvailablePort").SetValue(pile, coal.amountReadOut);
            AccessTools.Field(typeof(CoalPileSimController), "coalCapacityPort").SetValue(pile, coal.capacityReadOutPort);
            AccessTools.Field(typeof(CoalPileSimController), "coalConsumePort").SetValue(pile, coal.consumeExtIn);
            return pile;
        }

        private static RealisticFirebox Fire()
        {
            var fire = new RealisticFirebox(new FireboxDefinition { ID = "firebox" }, null);
            fire.SetGameParams(new SimGameParams(false, false, false, 1f, 1f));
            fire.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 10f });
            return fire;
        }

        private static RealisticStoker Stoker(RealisticFirebox fire, Port engine, Port relay, Port consume, Port reverse, Port[] jets)
        {
            var definition = new RealisticStokerDefinition { ID = "mechStoker", MaxTransferRate = 10f };
            var stoker = new RealisticStoker(definition.Values(), null, 4f, steamWired: true);
            stoker.SetGameParams(new SimGameParams(false, false, false, modifier, 1f));
            void Wire(string name, Port target) => stoker.GetAllPortReferences().Single(r => r.id == "mechStoker." + name).SetPortReference(target);
            Wire("CONTROL", engine);
            Wire("STEAM_PRESSURE", Port("boiler", "PRESSURE", PortType.READONLY_OUT, PortValueType.PRESSURE, 15f));
            Wire("COAL_AMOUNT", relay); Wire("COAL_CONSUMPTION", consume);
            stoker.Attach(fire, null, reverse); stoker.AttachJets(jets);
            return stoker;
        }

        private static Port Port(string component, string id, PortType type, PortValueType valueType, float value = 0f) =>
            new Port(component, new PortDefinition(type, valueType, id), value);

        private static void ApplyPorts(Assembly assembly, Port[] ports, float[] values)
        {
            var type = assembly.GetType("Multiplayer.Components.Networking.Train.NetworkedTrainCar", true);
            var networkCar = FormatterServices.GetUninitializedObject(type);
            var flow = (SimulationFlow)FormatterServices.GetUninitializedObject(typeof(SimulationFlow));
            AccessTools.Field(typeof(SimulationFlow), "fullPortIdToPort").SetValue(flow,
                new ReadOnlyDictionary<string, Port>(ports.ToDictionary(p => p.id)));
            AccessTools.Field(type, "hasSimFlow").SetValue(networkCar, true);
            AccessTools.Field(type, "simulationFlow").SetValue(networkCar, flow);
            var ids = new uint[ports.Length];
            var map = (Dictionary<uint, string>)AccessTools.Field(type, "netIdToPort").GetValue(null);
            for (var i = 0; i < ports.Length; i++)
            {
                uint hash = 2166136261;
                foreach (var c in ports[i].id) hash = unchecked((hash ^ c) * 16777619);
                ids[i] = hash; map[hash] = ports[i].id;
            }
            var apply = AccessTools.Method(type, "Common_UpdatePorts");
            var packet = Activator.CreateInstance(apply.GetParameters()[0].ParameterType);
            AccessTools.Property(packet.GetType(), "PortIds").SetValue(packet, ids, null);
            AccessTools.Property(packet.GetType(), "PortValues").SetValue(packet, values, null);
            apply.Invoke(networkCar, new[] { packet });
        }
    }
}
