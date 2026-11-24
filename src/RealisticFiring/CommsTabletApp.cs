using System;
using System.Collections.Generic;
using DV.CabControls;
using DV.Customization.Gadgets;
using RealisticFiring.Core;
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
    /// square (y up) as the Comms Tablet draws its own app symbols
    /// (<see cref="FlameGlyphRaster"/>: the same pixels as before in a few
    /// milliseconds, not 1.5 s of the tablet's first frame).
    /// </summary>
    internal static class FlameGlyph
    {
        internal static Texture2D Draw(int size)
        {
            var alpha = FlameGlyphRaster.Alpha(size);
            var pixels = new Color32[alpha.Length];
            for (var i = 0; i < alpha.Length; i++)
                pixels[i] = new Color32(255, 255, 255, alpha[i]);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RealisticFiring_FlameGlyph", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
