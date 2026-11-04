using System.Collections.Generic;
using DV.CabControls;
using DV.CabControls.Spec;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>Uses the 2901 maker's working tender hatch without copying or altering its controls.</summary>
    internal static class TenderFlueSandBox
    {
        internal const string EngineId = "A.T.&S.F. 2901-A", TenderId = "A.T.&S.F. 2901-B";
        internal const string RootName = "[RealisticFiring tender flue sand]";
        private const string AnchorPath = "Control/SandBox", LidName = "Sand Box Empty", MeshName = "Sand Box";
        private static readonly HashSet<string> Warned = new HashSet<string>();
        internal static readonly Quaternion PickupRotation = new Quaternion(0.70710678f, 0f, 0f, 0.70710678f);

        internal static bool UsesTender(string livery) => livery == EngineId;
        internal static bool CanScoop(bool coupledToSource, float lidValue) => coupledToSource && lidValue >= 0.5f && lidValue <= 1f;

        internal static void EnsureBuilt(TrainCar engine)
        {
            if (engine == null || !UsesTender(engine.carLivery.id))
                return;
            var tender = engine.rearCoupler.CoupledCar();
            if (!Matches(tender, engine))
                tender = engine.frontCoupler.CoupledCar();
            if (!Matches(tender, engine) || tender.loadedExternalInteractables == null)
                return;
            var anchor = tender.loadedExternalInteractables.transform.Find(AnchorPath);
            var existing = anchor != null ? anchor.Find(RootName) : null;
            if (existing != null)
            {
                existing.GetComponent<FlueSandBox>()?.BindEngine(engine);
                return;
            }
            var lid = anchor != null ? anchor.Find(LidName) : null;
            var control = lid != null ? lid.GetComponent<ControlImplBase>() : null;
            // ControlsInstantiator may not have built the native control yet.
            if (lid != null && control == null)
                return;
            var spec = lid != null ? lid.GetComponent<Lever>() : null;
            var mesh = lid != null ? lid.Find(MeshName) : null;
            var filter = mesh != null ? mesh.GetComponent<MeshFilter>() : null;
            if (spec == null || filter == null || filter.sharedMesh == null
                || (spec.jointAxis - Vector3.right).sqrMagnitude > 0.001f
                || Mathf.Abs(spec.jointLimitMin) > 1f || Mathf.Abs(spec.jointLimitMax - 115f) > 1f
                || !TryLayout(filter.sharedMesh.bounds, mesh.localPosition, mesh.localRotation, mesh.localScale,
                    out var center, out var size))
            {
                if (Warned.Add(TenderId))
                    Main.Mod?.Logger.Log($"{engine.ID}: the 2901 tender sandbox does not match the supported hatch; no flue-sand pickup added");
                return;
            }
            var box = FlueSandBox.BuildInTender(engine, tender, anchor, control, center, size);
            if (box != null && Main.Settings.LogFireboxCreation)
                Main.Mod?.Logger.Log($"{engine.ID}: flue sand inside the tender's existing sandbox; open its lid to load the shovel");
        }

        private static bool Matches(TrainCar tender, TrainCar engine) => tender != null
            && tender.carLivery.id == TenderId && tender.frontCoupler.CoupledCar() == engine;

        /// <summary>
        /// The mesh in the lid's authored closed frame, independent of its live
        /// hinge angle. The pickup sits 2.5 cm below the lid, with 4 cm clear of each rim.
        /// </summary>
        internal static bool TryLayout(Bounds mesh, Vector3 position, Quaternion rotation, Vector3 scale,
            out Vector3 center, out Vector2 size)
        {
            center = default;
            size = default;
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < 8; i++)
            {
                var corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var at = position + rotation * Vector3.Scale(corner, scale);
                min = Vector3.Min(min, at);
                max = Vector3.Max(max, at);
            }
            var span = max - min;
            // A changed or malformed asset gets no guessed shovel target.
            if (!(span.x > 0.4f && span.x < 1.2f && span.y > 0.25f && span.y < 0.8f
                && span.z > 0f && span.z < 0.2f) || !Finite(min) || !Finite(max))
                return false;
            center = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, min.z - 0.025f);
            size = new Vector2(span.x - 0.08f, span.y - 0.08f);
            return true;
        }

        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
