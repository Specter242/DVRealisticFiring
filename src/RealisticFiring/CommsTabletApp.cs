using System;
using System.Collections.Generic;
using DV.CabControls;
using DV.Customization.Gadgets;
using RealisticFiring.Items;
using UnityEngine;

namespace RealisticFiring
{
 public static class CommsTabletApp
    {
        public const int Contract = 1;
        public const string Id = "realistic-firing";
        public const string Name = "Firing";
        /// <summary>The firing tablet's prefab name: retired while merged, a saved one loading as the Comms Tablet.</summary>
        public const string Tablet = FiringTablet.PrefabName;

        private sealed class View
        {
            internal FiringTabletPanel Panel;
            internal GameObject Tablet;
            internal GadgetBase Mounted;
            internal ItemBase Item;
            internal Action Invalidate;
            internal Action<int, Rect, bool> PlaceSlot;
            internal Action<int> HideSlotsFrom;
            internal bool Held, Read;
            internal float RefreshAt;
            internal int ReadPage = -1;
            internal TrainCar ReadCar;
        }

        private static readonly Dictionary<int, View> views = new Dictionary<int, View>();
        private static int next;
        /// <summary>The page last chosen, which the app shows wherever it is opened (held or mounted).</summary>
        private static int page;
        private static Texture2D icon;

        /// <summary>A flame, white on clear, for the tablet's round badge.</summary>
        public static Texture2D Icon()
        {
            if (icon == null)
                icon = FlameGlyph.Draw(192);
            return icon;
        }

        public static bool Available() => Main.Mod != null;

        public static int Open(RectTransform region, GameObject tablet, Action invalidate, Action<int, Rect, bool> placeSlot,
            Action<int> hideSlotsFrom, int slotCount, bool vr)
        {
            var id = ++next;
            // The region is the whole screen: 1080 by 924 canvas pixels, twice the firing tablet's units.
            var size = region.rect.size;
            var scale = size.x > 0f && size.y > 0f ? Mathf.Min(size.x / FiringTabletPanel.Width, size.y / FiringTabletPanel.Height) : 2f;
            views[id] = new View
            {
                Panel = new FiringTabletPanel(region, scale, "comms-tablet-" + id + "-", "FIRING", vr || Visuals.CabControlSpotlight.IsVr()),
                Tablet = tablet,
                Mounted = tablet != null ? tablet.GetComponent<GadgetBase>() : null,
                Item = tablet != null ? tablet.GetComponent<ItemBase>() : null,
                Invalidate = invalidate,
                PlaceSlot = placeSlot,
                HideSlotsFrom = hideSlotsFrom,
            };
            return id;
        }

        public static void Close(int view)
        {
            if (!views.TryGetValue(view, out var v))
                return;
            views.Remove(view);
            v.Panel.Release();
        }

        public static void Shown(int view, bool shown)
        {
            if (shown && views.TryGetValue(view, out var v))
                v.RefreshAt = 0f;
        }

        public static void Held(int view, bool held)
        {
            if (!views.TryGetValue(view, out var v))
                return;
            v.Held = held;
            v.RefreshAt = 0f;
        }

        /// <summary>Each frame while shown: the shovel's aim, and the readings once a second or when the engine or page changes.</summary>
        public static void Update(int view)
        {
            if (!views.TryGetValue(view, out var v))
                return;
            var car = TargetCar(v);
            if (v.Read && v.Panel.UpdateAim())
                v.Invalidate();
            if (v.Read && Time.unscaledTime < v.RefreshAt && v.ReadPage == page && v.ReadCar == car)
                return;
            ReadNow(v, car);
            v.Invalidate();
        }

        public static void Draw(int view)
        {
            if (!views.TryGetValue(view, out var v))
                return;
            if (!v.Read || v.ReadPage != page)
                ReadNow(v, TargetCar(v));
            v.Panel.PlaceButtons(v.PlaceSlot);
            v.HideSlotsFrom(FiringTabletPanel.Buttons);
        }

        /// <summary>A page tab chooses that page; on the Fireman page an order goes to the fireman, as on the firing tablet.</summary>
        public static void Pressed(int view, int slot)
        {
            if (!views.TryGetValue(view, out var v) || slot < 0 || slot >= FiringTabletPanel.Buttons)
                return;
            // Resolved again on every press: carried into another cab, the
            // tablet never commands the engine of a cached reading.
            var car = TargetCar(v);
            if (slot < FiringTabletPanel.Pages)
                page = slot;
            else if (page == FiringTabletPanel.FiremanPage)
                FiringTabletPanel.GiveOrder(car, slot - FiringTabletPanel.Pages);
            else if (page == FiringTabletPanel.ChecklistPage)
                v.Panel.HandleChecklistAction(car, slot - FiringTabletPanel.Pages);
            ReadNow(v, car);
        }

        /// <summary>None: every pressable part is a button, as in VR.</summary>
        public static Rect PressArea(int view) => Rect.zero;

        public static void PressAt(int view, Vector2 canvas) { }

        private static void ReadNow(View v, TrainCar car)
        {
            v.Panel.Read(car, page, v.Mounted != null);
            v.Read = true;
            v.ReadPage = page;
            v.ReadCar = car;
            v.RefreshAt = Time.unscaledTime + 1f;
        }

        // As the firing tablet's: on a mount its car, in hand the player's, else the car it lies in.
        private static TrainCar TargetCar(View v)
        {
            if (v.Mounted != null)
                return v.Mounted.TrainCar;
            if (v.Held || (v.Item != null && v.Item.IsGrabbed()))
                return PlayerManager.Car;
            return v.Tablet != null ? TrainCar.Resolve(v.Tablet) : null;
        }
    }

    /// <summary>
    /// The Firing app's icon: a filled flame with a tongue of flame cut out of
    /// its base, white on clear, drawn from its distance field in a unit
    /// square (y up) as the Comms Tablet draws its own app symbols.
    /// </summary>
    internal static class FlameGlyph
    {
        // Cubic Bézier segments, each from the last one's end.
        private static readonly float[][] Outer =
        {
            new[] { 0.50f, 0.14f, 0.68f, 0.14f, 0.78f, 0.27f, 0.78f, 0.42f },
            new[] { 0.78f, 0.42f, 0.78f, 0.53f, 0.73f, 0.60f, 0.70f, 0.67f },
            new[] { 0.70f, 0.67f, 0.66f, 0.60f, 0.62f, 0.58f, 0.60f, 0.58f },
            new[] { 0.60f, 0.58f, 0.62f, 0.70f, 0.56f, 0.80f, 0.46f, 0.88f },
            new[] { 0.46f, 0.88f, 0.47f, 0.76f, 0.40f, 0.68f, 0.32f, 0.58f },
            new[] { 0.32f, 0.58f, 0.25f, 0.50f, 0.22f, 0.45f, 0.22f, 0.40f },
            new[] { 0.22f, 0.40f, 0.22f, 0.25f, 0.33f, 0.14f, 0.50f, 0.14f },
        };

        private static readonly float[][] Inner =
        {
            new[] { 0.50f, 0.22f, 0.60f, 0.22f, 0.65f, 0.29f, 0.65f, 0.36f },
            new[] { 0.65f, 0.36f, 0.65f, 0.45f, 0.57f, 0.50f, 0.52f, 0.58f },
            new[] { 0.52f, 0.58f, 0.49f, 0.50f, 0.40f, 0.46f, 0.37f, 0.38f },
            new[] { 0.37f, 0.38f, 0.35f, 0.29f, 0.41f, 0.22f, 0.50f, 0.22f },
        };

        internal static Texture2D Draw(int size)
        {
            var outer = Polygon(Outer);
            var inner = Polygon(Inner);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    var d = Mathf.Max(Distance(outer, u, v), -Distance(inner, u, v));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d * size) * 255f));
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RealisticFiring_FlameGlyph", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Vector2[] Polygon(float[][] segments)
        {
            const int steps = 24;
            var points = new List<Vector2>();
            foreach (var s in segments)
                for (var i = 0; i < steps; i++)
                {
                    var t = i / (float)steps;
                    float a = (1 - t) * (1 - t) * (1 - t), b = 3 * (1 - t) * (1 - t) * t, c = 3 * (1 - t) * t * t, d = t * t * t;
                    points.Add(new Vector2(a * s[0] + b * s[2] + c * s[4] + d * s[6], a * s[1] + b * s[3] + c * s[5] + d * s[7]));
                }
            return points.ToArray();
        }

        // The signed distance to a closed polygon: negative inside.
        private static float Distance(Vector2[] v, float x, float y)
        {
            var p = new Vector2(x, y);
            var best = float.MaxValue;
            var sign = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var t = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                best = Mathf.Min(best, (w - e * t).sqrMagnitude);
                bool above = p.y >= v[i].y, below = p.y < v[j].y, left = e.x * w.y > e.y * w.x;
                if ((above && below && left) || (!above && !below && !left))
                    sign = -sign;
            }
            return sign * Mathf.Sqrt(best);
        }
    }
}
