using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.Interaction;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>Use the S282's real grips instead of helpers that reach across other controls.</summary>
    internal sealed class S282InteractionAreas : MonoBehaviour
    {
        private readonly List<(Collider Collider, bool Enabled)> originals = new List<(Collider, bool)>();

        internal static void Attach(TrainCar car, Transform interior)
        {
            if (car?.carLivery?.id != "LocoS282A" || interior == null) return;
            var fitted = interior.GetComponent<S282InteractionAreas>();
            if (fitted == null) interior.gameObject.AddComponent<S282InteractionAreas>();
            else fitted.StartCoroutine(fitted.Start()); // A rebuilt manifold has new native areas.
        }

        private IEnumerator Start()
        {
            var pending = new List<StaticInteractionArea>();
            foreach (var path in new[] { "Center/Regulator/IA_Regulator", "Center/Firebox/IA_FireboxHandle" })
            {
                var area = transform.Find(path)?.GetComponent<StaticInteractionArea>();
                if (area != null) pending.Add(area);
            }
            var manifold = transform.Find(StokerCab.RootName);
            if (manifold != null)
                pending.AddRange(manifold.GetComponentsInChildren<Rotary>(true)
                    .Select(w => w.nonVrStaticInteractionArea).Where(a => a != null));
            // Native joints and their area handlers can finish several frames after Fire.Start.
            for (var frame = 0; frame < 120 && pending.Any(a => a != null && a.grabHandler == null); frame++)
                yield return null;
            originals.RemoveAll(p => p.Collider == null);
            UseGrips("Center/Regulator/C_Regulator", "Center/Regulator/IA_Regulator/collider");
            // Leave the lower IA collider (the firehole/shoveling target) exactly as it is.
            UseGrips("Center/Firebox/C_FireboxHandle", "Center/Firebox/IA_FireboxHandle/IA collider (1)");
            if (manifold != null)
                foreach (var wheel in manifold.GetComponentsInChildren<Rotary>(true))
                {
                    var area = wheel.nonVrStaticInteractionArea;
                    if (area == null) continue;
                    foreach (var collider in area.GetComponentsInChildren<Collider>(true))
                        UseGrips(wheel.transform, collider);
                }
        }

        private void UseGrips(string controlPath, string areaPath)
        {
            var control = transform.Find(controlPath);
            var old = transform.Find(areaPath)?.GetComponent<Collider>();
            if (control != null && old != null) UseGrips(control, old);
        }

        private void UseGrips(Transform control, Collider old)
        {
            if (old == null || !old.enabled || !old.isTrigger) return;
            var spec = control.GetComponent<ControlSpec>();
            var handler = control.GetComponent<AGrabHandler>();
            var area = old.GetComponentInParent<StaticInteractionArea>();
            if (spec?.colliderGameObjects == null || handler == null || area?.grabHandler != handler) return;
            var grips = spec.colliderGameObjects.Where(g => g != null)
                .SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Distinct().ToArray();
            // Retain the helper if a changed cab has no working, registered physical grips.
            if (!grips.Any(c => c.enabled && !c.isTrigger && handler.interactionColliders.Contains(c))) return;
            if (grips.Contains(old) || GetComponentsInChildren<ItemUseTarget>(true)
                .Any(t => t.targetColliders != null && t.targetColliders.Contains(old))) return;

            // The native handler already owns the gripping shapes. They now define the
            // entire clickable volume: no extra colliders, joint mass, tracking lag or ray overrides.
            originals.Add((old, old.enabled));
            old.enabled = false;
        }

        private void OnDestroy()
        {
            foreach (var original in originals)
                if (original.Collider != null) original.Collider.enabled = original.Enabled;
        }
    }
}
