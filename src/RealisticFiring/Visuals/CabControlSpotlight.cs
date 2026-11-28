using System;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.Simulation.Ports;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// On-demand Cab Control Spotlight ("Show added controls"): briefly (~10 s,
    /// or toggled off again) renders floating screen labels pointing to every
    /// cab control added or reworked by Realistic Firing on the current
    /// locomotive, using the actual transforms of the installed fittings so it
    /// works across stock and Custom Car Loader cabs.
    /// </summary>
    internal static class CabControlSpotlight
    {
        internal const float DurationSeconds = 10f;
        private const float MaxDistance = 12f;

        internal struct Marker
        {
            public string Label;
            public string Detail;
            public Transform Target;
            public Vector3 WorldOffset;
        }

        internal struct LabelBox
        {
            public float X;
            public float Y;
            public float Width;
            public float Height;
            public float AnchorX;
            public float AnchorY;
            public string Label;
            public string Detail;
        }

        private static readonly List<Marker> ActiveMarkers = new List<Marker>();
        private static TrainCar activeCar;
        private static float expiresAt = float.NegativeInfinity;
        private static GUIStyle pillStyle, detailStyle, bannerStyle;
        private static Font fontFor;

        /// <summary>Whether the OnGUI spotlight can be displayed (desktop only; invisible in VR).</summary>
        internal static bool Supported => !IsVr();

        internal static bool IsVr()
        {
            try
            {
                return VRManager.IsVREnabled();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Whether the spotlight is currently showing on screen.</summary>
        internal static bool Active => activeCar != null && Time.unscaledTime < expiresAt;

        /// <summary>Seconds remaining before auto-dismissal.</summary>
        internal static float RemainingSeconds => Active ? Mathf.Max(0f, expiresAt - Time.unscaledTime) : 0f;

        /// <summary>Dismisses the spotlight immediately.</summary>
        internal static void Dismiss()
        {
            activeCar = null;
            ActiveMarkers.Clear();
            expiresAt = float.NegativeInfinity;
        }

        /// <summary>
        /// Toggles the spotlight for <paramref name="car"/> (or its tended
        /// engine). Returns a short notice for the player.
        /// </summary>
        internal static string Toggle(TrainCar car)
        {
            if (!Supported)
            {
                Dismiss();
                return "Added-control labels are desktop-only (not visible in VR)";
            }
            if (HudPolicy.Hidden)
            {
                Dismiss();
                return "On-screen helpers are hidden by the multiplayer host";
            }
            if (Active)
            {
                Dismiss();
                return "Added controls hidden";
            }
            var target = ResolveEngine(car);
            if (target == null)
                return "Board a supported steam locomotive to highlight its added controls";
            ActiveMarkers.Clear();
            Collect(target, ActiveMarkers);
            if (ActiveMarkers.Count == 0)
                return "No added Realistic Firing controls are loaded on this locomotive yet";
            activeCar = target;
            expiresAt = Time.unscaledTime + DurationSeconds;
            return $"Showing {ActiveMarkers.Count} added controls for {DurationSeconds:F0} s (press again to hide)";
        }

        private static TrainCar ResolveEngine(TrainCar car)
        {
            if (car == null)
                return null;
            if (FireboxRegistry.TryGetTended(car, out var coal))
                return coal.Car;
            if (RealisticOilFirebox.TryGetTended(car, out var oil))
                return oil.Car;
            if (InjectorRig.TryGetTended(car, out var rig))
                return rig.Car;
            return null;
        }

        /// <summary>
        /// Collects every Realistic Firing added/reworked cab control currently
        /// installed on <paramref name="car"/> (and its coupled tender).
        /// </summary>
        internal static void Collect(TrainCar car, List<Marker> results)
        {
            if (car == null || results == null)
                return;
            var interior = car.loadedInterior != null ? car.loadedInterior.transform : null;
            if (interior != null)
            {
                CollectInjectorSteam(interior, results);
                CollectSimpleRoot(interior, DriftingValveCab.RootName, "DRIFTING", "steam to chests while drifting", results);
                CollectSimpleRoot(interior, JetsCab.RootName, "JETS", "over-fire smoke jets", results);
                CollectSimpleRoot(interior, DropGrateCab.RootName, "DROP GRATE", "drop front clinker", results);
                CollectTryCocks(interior, results);
                CollectSimpleRoot(interior, FeedPumpCab.BackheadRoot, "FEED PUMP", "exhaust-heated feedwater", results);
                CollectBooster(interior, results);
                CollectStoker(interior, results);
                CollectOilBurner(interior, results);
            }
            CollectAshpan(car, results);
            CollectTenderFittings(car, results);
        }

        private static void CollectInjectorSteam(Transform interior, List<Marker> results)
        {
            var root = interior.Find(InjectorSteamCab.RootName);
            if (root == null)
                return;
            var added = 0;
            // S282 Lilla model and standard InjectorSteamCab both place named control children.
            foreach (var feeder in root.GetComponentsInChildren<InteractablePortFeeder>(false))
            {
                var id = feeder.portId ?? "";
                var name = feeder.gameObject.name ?? "";
                if (id.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0)
                    AddUnique(results, "INJ WATER", "water rate valve", feeder.transform);
                else
                    AddUnique(results, "INJ STEAM", "Off / Prime / Feed", feeder.transform);
                added++;
            }
            if (added == 0)
                results.Add(new Marker { Label = "INJ STEAM", Detail = "Off / Prime / Feed", Target = root });
        }

        private static void CollectSimpleRoot(Transform interior, string rootName, string label, string detail, List<Marker> results)
        {
            var root = interior.Find(rootName);
            if (root == null)
                return;
            var control = root.GetComponentInChildren<InteractablePortFeeder>(false);
            results.Add(new Marker { Label = label, Detail = detail, Target = control != null ? control.transform : root });
        }

        private static void CollectTryCocks(Transform interior, List<Marker> results)
        {
            var root = interior.Find(TryCockCab.RootName);
            if (root == null)
                return;
            var middle = root.Find(TryCockRig.Valves[1].Id + " frame");
            if (middle == null)
                middle = root.Find(TryCockRig.Valves[0].Id + " frame");
            if (middle == null)
                middle = root;
            results.Add(new Marker { Label = "TRY COCKS", Detail = "check water height & blow glass", Target = middle });
        }

        private static void CollectBooster(Transform interior, List<Marker> results)
        {
            var root = interior.Find(BoosterCab.Root);
            if (root == null)
                return;
            var latch = root.Find(BoosterFit.Latch.Id);
            var idle = root.Find(BoosterFit.Idle.Id);
            if (latch != null)
                results.Add(new Marker { Label = "BOOSTER LATCH", Detail = "cut booster in (full forward)", Target = latch });
            if (idle != null)
                results.Add(new Marker { Label = "IDLING VALVE", Detail = "warm booster before cut-in", Target = idle });
            if (latch == null && idle == null)
                results.Add(new Marker { Label = "BOOSTER", Detail = "latch & idling valve", Target = root });
        }

        private static void CollectStoker(Transform interior, List<Marker> results)
        {
            var root = interior.Find(StokerCab.RootName);
            if (root == null)
                return;
            var engine = root.Find(StokerJetControls.Engine.Id);
            if (engine == null)
                engine = root.Find("stoker engine valve");
            var master = root.Find(StokerJetControls.Master.Id);
            var lever = root.Find(StokerJetControls.Reverse.Id);
            var pump = root.Find(FeedwaterFit.Valve.Id);

            if (engine != null)
                results.Add(new Marker { Label = "STOKER", Detail = "stoker engine throttle", Target = engine });
            if (master != null)
                results.Add(new Marker { Label = "STOKER JETS", Detail = "master, front, back & fines jets", Target = master });
            else if (engine == null)
                results.Add(new Marker { Label = "STOKER", Detail = "manifold & distribution jets", Target = root });
            if (lever != null)
                results.Add(new Marker { Label = "STOKER LEVER", Detail = "Forward / Neutral / Reverse jam", Target = lever });
            if (pump != null)
                results.Add(new Marker { Label = "FEED PUMP", Detail = "exhaust-heated feedwater", Target = pump });
        }

        private static void CollectOilBurner(Transform interior, List<Marker> results)
        {
            // An S282 converted to oil: its own burner's controls, drawn with its cab.
            var s282 = interior.Find(S282OilCab.RootName);
            if (s282 != null)
            {
                var marks = new[]
                {
                    (S282OilConversion.FiringValve.Id, "FIRING VALVE", "oil to the burner"),
                    (S282OilCab.AtomizerName, "ATOMIZER", "steam to break up oil"),
                    (S282OilCab.HeaterName, "OIL HEATER", "warm oil to 45–80 °C"),
                    ("flue sand box", "SAND BOX", "flue sand, through the peephole"),
                };
                foreach (var (name, label, detail) in marks)
                {
                    var found = FindChild(s282, name);
                    if (found != null)
                        results.Add(new Marker { Label = label, Detail = detail, Target = found });
                }
            }
            var root = interior.Find("[RealisticFiring oil burner valves]");
            if (root == null)
                return;
            var atomizer = FindChild(root, OilBurnerFit.Atomizer.Id);
            var heater = FindChild(root, OilBurnerFit.Heater.Id);
            var shopSteam = FindChild(root, OilBurnerFit.ShopSteam.Id);
            if (atomizer != null)
                results.Add(new Marker { Label = "ATOMIZER", Detail = "steam to break up oil", Target = atomizer });
            if (heater != null)
                results.Add(new Marker { Label = "OIL HEATER", Detail = "warm oil to 45–80 °C", Target = heater });
            if (shopSteam != null)
                results.Add(new Marker { Label = "SHOP STEAM", Detail = "shed blower line indicator", Target = shopSteam });
            if (atomizer == null && heater == null && shopSteam == null)
                results.Add(new Marker { Label = "OIL VALVES", Detail = "atomizer & oil heater", Target = root });
        }

        private static void CollectAshpan(TrainCar car, List<Marker> results)
        {
            var ext = car.loadedExternalInteractables != null ? car.loadedExternalInteractables.transform : null;
            var ashpan = ext != null ? ext.Find("[RealisticFiring ashpan lever]") : null;
            if (ashpan == null)
                ashpan = FindChild(car.transform, "[RealisticFiring ashpan lever]");
            if (ashpan != null)
            {
                var control = ashpan.GetComponentInChildren<ControlImplBase>(false);
                results.Add(new Marker { Label = "ASHPAN", Detail = "hold from ground to empty", Target = control != null ? control.transform : ashpan });
            }
        }

        /// <summary>
        /// Returns <paramref name="car"/>'s own coupled tender (coupled behind its
        /// rear coupler with the tender's front coupler meeting this engine),
        /// never a neighboring locomotive's tender when double-heading.
        /// </summary>
        internal static TrainCar CoupledTender(TrainCar car)
        {
            if (car == null)
                return null;
            var tender = car.rearCoupler.CoupledCar();
            if (tender == null)
                return null;
            if (tender.frontCoupler != null && tender.frontCoupler.CoupledCar() != car)
                return null;
            return tender;
        }

        private static void CollectTenderFittings(TrainCar car, List<Marker> results)
        {
            var tender = CoupledTender(car);
            if (tender == null)
                return;
            var ext = tender.loadedExternalInteractables != null ? tender.loadedExternalInteractables.transform : null;
            var waterValves = ext != null ? ext.Find("[RealisticFiring tender water valves]") : null;
            if (waterValves == null)
                waterValves = FindChild(tender.transform, "[RealisticFiring tender water valves]");
            if (waterValves != null)
                results.Add(new Marker { Label = "TENDER WATER", Detail = "open to feed injectors", Target = waterValves });
            var sand = ext != null ? ext.Find(TenderFlueSandBox.RootName) : null;
            if (sand == null)
                sand = FindChild(tender.transform, TenderFlueSandBox.RootName);
            if (sand != null)
                results.Add(new Marker { Label = "FLUE SAND", Detail = "open lid, fill the sand scoop for the peep hole", Target = sand });
        }

        private static Transform FindChild(Transform parent, string name)
        {
            if (parent == null)
                return null;
            foreach (var t in parent.GetComponentsInChildren<Transform>(false))
                if (t.name == name)
                    return t;
            return null;
        }

        private static void AddUnique(List<Marker> results, string label, string detail, Transform target)
        {
            // Keep separate labels when two valves are clearly apart (e.g. JK3 left/right), skip duplicates on same transform.
            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].Label == label && results[i].Target != null
                    && (results[i].Target.position - target.position).sqrMagnitude < 0.04f)
                    return;
            }
            results.Add(new Marker { Label = label, Detail = detail, Target = target });
        }

        /// <summary>
        /// Pure screen-space vertical de-overlapping for floating labels so
        /// adjacent cab controls stay readable.
        /// </summary>
        internal static void ResolveOverlap(List<LabelBox> boxes, float minGap, float minY, float maxY)
        {
            if (boxes == null || boxes.Count < 2)
                return;
            boxes.Sort((a, b) => a.Y.CompareTo(b.Y));
            for (var pass = 0; pass < 4; pass++)
            {
                var moved = false;
                for (var i = 1; i < boxes.Count; i++)
                {
                    var prev = boxes[i - 1];
                    var cur = boxes[i];
                    var horizOverlap = cur.X < prev.X + prev.Width + minGap && prev.X < cur.X + cur.Width + minGap;
                    if (!horizOverlap)
                        continue;
                    var neededY = prev.Y + prev.Height + minGap;
                    if (cur.Y < neededY)
                    {
                        cur.Y = Mathf.Min(maxY - cur.Height, neededY);
                        boxes[i] = cur;
                        moved = true;
                    }
                }
                if (!moved)
                    break;
            }
            for (var i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                b.Y = Mathf.Clamp(b.Y, minY, Mathf.Max(minY, maxY - b.Height));
                boxes[i] = b;
            }
        }

        /// <summary>Draws the floating control labels while active.</summary>
        internal static void Draw()
        {
            if (HudPolicy.Hidden || !Active || !Supported)
                return;
            if (activeCar == null)
            {
                Dismiss();
                return;
            }
            var camera = PlayerManager.ActiveCamera;
            if (camera == null)
                return;
            Styles();

            var k = OverlayStyle.Scale;
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            var screenW = Screen.width / k;
            var screenH = Screen.height / k;

            var boxes = new List<LabelBox>(ActiveMarkers.Count);
            for (var i = 0; i < ActiveMarkers.Count; i++)
            {
                var m = ActiveMarkers[i];
                if (m.Target == null || !m.Target.gameObject.activeInHierarchy)
                    continue;
                var world = m.Target.position + m.WorldOffset;
                var dist = Vector3.Distance(RenderedView.Eye(camera), world);
                if (dist > MaxDistance)
                    continue;
                var sp = RenderedView.WorldToScreenPoint(camera, world);
                if (sp.z <= 0.05f)
                    continue;
                var gx = sp.x / k;
                var gy = (Screen.height - sp.y) / k;
                if (gx < -40f || gx > screenW + 40f || gy < -40f || gy > screenH + 40f)
                    continue;

                var titleSize = pillStyle.CalcSize(new GUIContent(m.Label));
                var detailSize = string.IsNullOrEmpty(m.Detail) ? Vector2.zero : detailStyle.CalcSize(new GUIContent(m.Detail));
                var w = Mathf.Max(titleSize.x, detailSize.x) + 18f;
                var h = string.IsNullOrEmpty(m.Detail) ? 24f : 38f;
                var bx = Mathf.Clamp(gx - w * 0.5f, 8f, Mathf.Max(8f, screenW - w - 8f));
                var by = Mathf.Clamp(gy - h - 14f, 44f, Mathf.Max(44f, screenH - h - 8f));
                boxes.Add(new LabelBox
                {
                    X = bx, Y = by, Width = w, Height = h,
                    AnchorX = gx, AnchorY = gy,
                    Label = m.Label, Detail = m.Detail,
                });
            }

            ResolveOverlap(boxes, 6f, 44f, screenH - 8f);

            var oldColor = GUI.color;
            for (var i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                // Anchor dot at the actual control transform and leader line to the pill.
                DrawLine(new Vector2(b.AnchorX, b.AnchorY), new Vector2(b.X + b.Width * 0.5f, b.Y + b.Height), OverlayArt.Brass, 2f);
                GUI.color = OverlayArt.Caution;
                GUI.DrawTexture(new Rect(b.AnchorX - 4f, b.AnchorY - 4f, 8f, 8f), OverlayArt.Mark(Core.AdviceLevel.Ok));
                GUI.color = Color.white;

                var rect = new Rect(b.X, b.Y, b.Width, b.Height);
                GUI.Box(rect, GUIContent.none, pillStyle);
                GUI.Label(new Rect(b.X + 8f, b.Y + 3f, b.Width - 16f, 18f), b.Label, pillStyle);
                if (!string.IsNullOrEmpty(b.Detail))
                    GUI.Label(new Rect(b.X + 8f, b.Y + 20f, b.Width - 16f, 16f), b.Detail, detailStyle);
            }
            GUI.color = oldColor;

            var banner = $"ADDED CONTROLS ({boxes.Count}/{ActiveMarkers.Count} in view) · {RemainingSeconds:F0} s";
            var bSize = bannerStyle.CalcSize(new GUIContent(banner));
            var bw = bSize.x + 28f;
            GUI.Box(new Rect((screenW - bw) * 0.5f, 10f, bw, 28f), banner, bannerStyle);

            GUI.matrix = matrix;
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            var delta = b - a;
            var length = delta.magnitude;
            if (length < 1f)
                return;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var saved = GUI.matrix;
            var oldColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), OverlayArt.White);
            GUI.color = oldColor;
            GUI.matrix = saved;
        }

        private static void Styles()
        {
            var font = PlumeAssets.OverlayFont;
            if (pillStyle != null && fontFor == font)
                return;
            fontFor = font;
            pillStyle = new GUIStyle(OverlayStyle.Text(14, OverlayArt.Cream, TextAnchor.UpperLeft, OverlayStyle.PlateFont))
            {
                normal = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
                border = new RectOffset(5, 5, 5, 5),
                padding = new RectOffset(0, 0, 0, 0),
            };
            detailStyle = OverlayStyle.Text(12, OverlayArt.Soft, TextAnchor.UpperLeft);
            bannerStyle = new GUIStyle(OverlayStyle.Text(15, OverlayArt.Cream, TextAnchor.MiddleCenter, OverlayStyle.PlateFont))
            {
                normal = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
                border = new RectOffset(5, 5, 5, 5),
            };
        }
    }
}
