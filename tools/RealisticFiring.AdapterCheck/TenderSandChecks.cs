using System;
using System.Runtime.Serialization;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using LocoSim.Resources;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.AdapterCheck
{
    internal static class TenderSandChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            // Measured from the maker's 1.0.1 tender hatch. Only bounds and its
            // child pose, never third-party mesh data, are needed for this check.
            var bounds = new Bounds(new Vector3(0f, -0.0007648548f, 0.004228534f),
                new Vector3(0.008107791f, 0.004570791f, 0.0005243011f));
            var position = new Vector3(0f, 0.31193554f, -0.38951185f);
            var rotation = new Quaternion(-0.00000011920928f, 0f, 0f, 1f);
            var scale = new Vector3(100f, 100.00003f, 100.00003f);
            var fits = TenderFlueSandBox.TryLayout(bounds, position, rotation, scale, out var center, out var size);
            check(fits && Math.Abs(center.x) < 0.0001f && Math.Abs(center.y - 0.235450f) < 0.0001f
                && Math.Abs(center.z + 0.0178734f) < 0.0001f
                && Math.Abs(size.x - 0.730779f) < 0.0001f && Math.Abs(size.y - 0.377079f) < 0.0001f,
                "the 2901 sand fits inside its existing tender hatch, 2.5 cm below the lid and 4 cm clear of its rim");
            var up = TenderFlueSandBox.PickupRotation * Vector3.up;
            check((up - Vector3.forward).sqrMagnitude < 1e-10f,
                "the shovel pickup faces up through the tender hatch in the maker's rotated anchor frame");
            check(!TenderFlueSandBox.TryLayout(new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f)), position,
                    rotation, scale, out _, out _)
                && !TenderFlueSandBox.TryLayout(bounds, new Vector3(float.NaN, 0f, 0f), rotation, scale, out _, out _),
                "a changed or malformed tender hatch gets no guessed sand pickup");
            check(!TenderFlueSandBox.CanScoop(true, 0f) && !TenderFlueSandBox.CanScoop(true, 0.49f)
                && TenderFlueSandBox.CanScoop(true, 0.5f) && TenderFlueSandBox.CanScoop(true, 1f)
                && !TenderFlueSandBox.CanScoop(false, 1f) && !TenderFlueSandBox.CanScoop(true, float.NaN)
                && !TenderFlueSandBox.CanScoop(true, float.PositiveInfinity),
                "loading requires an open tender lid and a coupled source engine; closing or uncoupling blocks it");
            check(TenderFlueSandBox.UsesTender("A.T.&S.F. 2901-A") && !TenderFlueSandBox.UsesTender("LocoS282A")
                && !TenderFlueSandBox.UsesTender(null), "only the 2901 replaces its cab sand box with the tender pickup");

            // The maker already supplied the visible sand. This contract
            // prevents reintroducing RF's extra surface/material over it.
            var hookup = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(FlueSandBox), "BuildInTender"));
            check(!hookup.Exists(i => i.Calls(AccessTools.Method(typeof(FlueSandBox), "SandSurface"))
                    || i.Calls(AccessTools.Method(typeof(FlueSandBox), "SandMaterial"))
                    || i.Calls(AccessTools.Method(typeof(FlueSandBox), "Part"))),
                "the tender hookup adds only shovel controls, keeping the maker's sand surface and materials");

            // Exercise the real pickup's resource operations without native
            // GameObjects. A missing carrier is the closed/stale target case.
            var supply = new ResourceContainer("sand", ResourceContainerType.SAND, 2f, 10f,
                Port("REFILL", PortType.EXTERNAL_IN), Port("CONSUME", PortType.EXTERNAL_IN),
                Port("AMOUNT", PortType.READONLY_OUT), Port("NORMALIZED", PortType.READONLY_OUT),
                Port("CAPACITY", PortType.READONLY_OUT));
            var box = (FlueSandBox)FormatterServices.GetUninitializedObject(typeof(FlueSandBox));
            AccessTools.Field(typeof(FlueSandBox), "sand").SetValue(box, supply);
            var tenderMode = AccessTools.Field(typeof(FlueSandBox), "usesTender");
            FlueSandBox.TakeLoaded(float.MaxValue);
            try
            {
                var first = box.Take(FlueSandBox.Shovelful);
                var last = box.Take(FlueSandBox.Shovelful);
                check(first == 1.5f && last == 0.5f && box.Available == 0f && FlueSandBox.Loaded == 2f,
                    "flue sand uses the existing resource, retains a 1.5 kg shovel load, and cannot overdraw it");
                tenderMode.SetValue(box, true);
                check(box.Take(1f) == 0f && box.PutBack(1f) == 0f && FlueSandBox.Loaded == 2f
                    && supply.amountReadOut.Value == 0f,
                    "a stale tender target cannot take or return sand after losing its source");
                tenderMode.SetValue(box, false);
                supply.refillExtIn.Value = 9f;
                var returned = box.PutBack(2f);
                check(returned == 1f && supply.amountReadOut.Value == 10f && FlueSandBox.Loaded == 1f,
                    "returning sand to a full supply leaves any excess on the shovel");
                var saved = supply.GetSaveStateData();
                supply.consumeExtIn.Value = 10f;
                supply.SetSaveStateData(saved);
                check(supply.amountReadOut.Value == 10f, "the tender pickup keeps the maker's existing sand save data");
            }
            finally
            {
                FlueSandBox.TakeLoaded(float.MaxValue);
                // No native shovel is involved; all other checks start empty.
            }
        }

        private static PortDefinition Port(string id, PortType type) => new PortDefinition(type, PortValueType.GENERIC, id);
    }
}
