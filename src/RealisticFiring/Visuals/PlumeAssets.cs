using System.IO;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The custom chimney smoke effects: a prefab built by
    /// <c>scripts/build-fx.sh</c> into an asset bundle that ships beside the
    /// mod's assembly. Loaded on first use; without it the plume drives the
    /// game's own smoke systems.
    /// </summary>
    internal static class PlumeAssets
    {
        public const string BundleFile = "realisticfiring_fx";
        public const string PrefabName = "ChimneyPlumeFX";
        public const string OverflowPrefabName = "InjectorOverflowFX";
        public const string FireboxPrefabName = "FireboxFX";
        public const string FireIronMaterialName = "FireIron";

        private static bool loaded;
        private static AssetBundle bundle;
        private static GameObject prefab;
        private static GameObject overflowPrefab;
        private static GameObject fireboxPrefab;
        private static Material fireIronMaterial;
        private static Texture2D fireIronIcon;
        private static Texture2D fireIronIconDropped;

        /// <summary>The plume prefab, or null when the bundle is missing or unreadable.</summary>
        public static GameObject Prefab
        {
            get
            {
                Ensure();
                return prefab;
            }
        }

        /// <summary>The injector overflow prefab, or null when the bundle lacks it.</summary>
        public static GameObject OverflowPrefab
        {
            get
            {
                Ensure();
                return overflowPrefab;
            }
        }

        /// <summary>The fire iron's forged look, or null without the bundle.</summary>
        public static Material FireIronMaterial
        {
            get
            {
                Ensure();
                return fireIronMaterial;
            }
        }

        /// <summary>The fire iron's inventory icon, and its outline for when it lies dropped; null without the bundle.</summary>
        public static Texture2D FireIronIcon
        {
            get
            {
                Ensure();
                return fireIronIcon;
            }
        }

        public static Texture2D FireIronIconDropped
        {
            get
            {
                Ensure();
                return fireIronIconDropped;
            }
        }

        /// <summary>The firebox's bed, flames, and smoke, or null when the bundle lacks them.</summary>
        public static GameObject FireboxPrefab
        {
            get
            {
                Ensure();
                return fireboxPrefab;
            }
        }

        // Loads the bundle on first use, and its assets again if the game has
        // unloaded them. Going back to the main menu, the game unloads every
        // asset no scene object uses, and these, held only here, went with
        // it: every loco built after that had the stock smoke and a bed of
        // nine tiles with no smoke or flames in the firebox (in game,
        // 2026-10-10, on the S060 after a reload).
        private static void Ensure()
        {
            if (!loaded)
                Load();
            else if (bundle != null && Unloaded())
            {
                LoadAssets();
                Main.Mod.Logger.Log("Custom smoke effects reloaded: the game had unloaded them");
            }
        }

        // An asset loaded once and since destroyed: Unity's null for a dead object.
        private static bool Unloaded() =>
            Lost(prefab) || Lost(overflowPrefab) || Lost(fireboxPrefab) || Lost(fireIronMaterial) || Lost(fireIronIcon) || Lost(fireIronIconDropped);

        private static bool Lost(Object asset) => !ReferenceEquals(asset, null) && asset == null;

        // The bundle stays loaded: the prefab's materials and textures live in it.
        private static void Load()
        {
            loaded = true;
            var path = Path.Combine(Main.Mod.Path, BundleFile);
            if (!File.Exists(path))
            {
                Main.Mod.Logger.Log($"No custom smoke bundle at {path}; using the game's smoke systems");
                return;
            }
            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Main.Mod.Logger.Warning($"Could not load the custom smoke bundle {path}; using the game's smoke systems");
                return;
            }
            LoadAssets();
            Main.Mod.Logger.Log(prefab != null
                ? "Custom smoke effects loaded"
                : $"The smoke bundle has no '{PrefabName}'; using the game's smoke systems");
        }

        // Every asset in the bundle, the prefabs' materials, textures, and
        // meshes among them, kept through the game's unloading of unused ones.
        private static void LoadAssets()
        {
            foreach (var asset in bundle.LoadAllAssets())
                asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            prefab = bundle.LoadAsset<GameObject>(PrefabName);
            overflowPrefab = bundle.LoadAsset<GameObject>(OverflowPrefabName);
            fireboxPrefab = bundle.LoadAsset<GameObject>(FireboxPrefabName);
            fireIronMaterial = bundle.LoadAsset<Material>(FireIronMaterialName);
            fireIronIcon = bundle.LoadAsset<Texture2D>("fire_iron_icon");
            fireIronIconDropped = bundle.LoadAsset<Texture2D>("fire_iron_icon_dropped");
        }
    }
}
