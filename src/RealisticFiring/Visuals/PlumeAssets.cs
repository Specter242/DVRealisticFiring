using System.IO;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The custom chimney smoke effects: a prefab built by
    /// <c>scripts/build-fx.sh</c> into an asset bundle that ships beside the
    /// mod's assembly. Loaded once, on first use; without it the plume drives
    /// the game's own smoke systems.
    /// </summary>
    internal static class PlumeAssets
    {
        public const string BundleFile = "realisticfiring_fx";
        public const string PrefabName = "ChimneyPlumeFX";
        public const string OverflowPrefabName = "InjectorOverflowFX";
        public const string FireboxPrefabName = "FireboxFX";
        public const string FireIronMaterialName = "FireIron";

        private static bool loaded;
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
                if (!loaded)
                    Load();
                return prefab;
            }
        }

        /// <summary>The injector overflow prefab, or null when the bundle lacks it.</summary>
        public static GameObject OverflowPrefab
        {
            get
            {
                if (!loaded)
                    Load();
                return overflowPrefab;
            }
        }

        /// <summary>The firebox's bed, flames, and smoke, or null when the bundle lacks them.</summary>
        /// <summary>The fire iron's forged look, or null without the bundle.</summary>
        public static Material FireIronMaterial
        {
            get
            {
                if (!loaded)
                    Load();
                return fireIronMaterial;
            }
        }

        /// <summary>The fire iron's inventory icon, and its outline for when it lies dropped; null without the bundle.</summary>
        public static Texture2D FireIronIcon
        {
            get
            {
                if (!loaded)
                    Load();
                return fireIronIcon;
            }
        }

        public static Texture2D FireIronIconDropped
        {
            get
            {
                if (!loaded)
                    Load();
                return fireIronIconDropped;
            }
        }

        public static GameObject FireboxPrefab
        {
            get
            {
                if (!loaded)
                    Load();
                return fireboxPrefab;
            }
        }

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
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Main.Mod.Logger.Warning($"Could not load the custom smoke bundle {path}; using the game's smoke systems");
                return;
            }
            prefab = bundle.LoadAsset<GameObject>(PrefabName);
            overflowPrefab = bundle.LoadAsset<GameObject>(OverflowPrefabName);
            fireboxPrefab = bundle.LoadAsset<GameObject>(FireboxPrefabName);
            fireIronMaterial = bundle.LoadAsset<Material>(FireIronMaterialName);
            fireIronIcon = bundle.LoadAsset<Texture2D>("fire_iron_icon");
            fireIronIconDropped = bundle.LoadAsset<Texture2D>("fire_iron_icon_dropped");
            Main.Mod.Logger.Log(prefab != null
                ? "Custom smoke effects loaded"
                : $"The smoke bundle has no '{PrefabName}'; using the game's smoke systems");
        }
    }
}
