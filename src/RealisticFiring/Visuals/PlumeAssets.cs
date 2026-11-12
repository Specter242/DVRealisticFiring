using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

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
        public const string FireboxWallMaterialName = "FireboxWall";
        public const string FireboxVolumeMaterialName = "FireboxVolume";
        public const string CoalLumpsMaterialName = "FireboxCoalLumps";
        public const string PlumeCompositeMaterialName = "PlumeComposite";
        public const string PlumeDepthMaterialName = "PlumeDepth";
        public const string StokerJetMaterialName = "StokerJet";
        public const string OverlayFontName = "FiraSansCondensed-Medium";
        public const string StartingFuelPrefabName = "StartingFuelFX";
        private static readonly string[] StartingFuelMaterials = { "StartingFuelSplit", "StartingFuelBark", "StartingFuelEnd", "StartingFuelCloth" };
        private static readonly string[] StartingFuelIcons = { "starting_wood_icon", "starting_rag_icon", "starting_crate_icon", "starting_tin_icon",
            "starting_wood_icon_dropped", "starting_rag_icon_dropped", "starting_crate_icon_dropped", "starting_tin_icon_dropped" };
        public const string PlateFontName = "FiraSansCondensed-SemiBold";

        private static bool loaded;
        private static GameObject prefab;
        private static GameObject overflowPrefab;
        private static GameObject fireboxPrefab;
        private static Material fireIronMaterial;
        private static Material fireboxWallMaterial;
        private static Material fireboxVolumeMaterial;
        private static Material coalLumpsMaterial;
        private static Material plumeCompositeMaterial;
        private static Material plumeDepthMaterial;
        private static Material stokerJetMaterial;
        private static Texture2D fireIronIcon;
        private static Texture2D fireIronIconDropped;
        private static GameObject startingFuelPrefab;
        private static readonly System.Collections.Generic.Dictionary<string, Object> startingFuelAssets = new System.Collections.Generic.Dictionary<string, Object>();
        private static Font overlayFont;
        private static Font plateFont;

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

        /// <summary>The firebox's walls lit only by the fire (copied for each shell), or null without the bundle.</summary>
        public static Material FireboxWallMaterial
        {
            get
            {
                Ensure();
                return fireboxWallMaterial;
            }
        }

        /// <summary>The firebox's flames and smoke as one volume (copied for each firebox), or null without the bundle.</summary>
        public static Material FireboxVolumeMaterial
        {
            get
            {
                Ensure();
                return fireboxVolumeMaterial;
            }
        }

        /// <summary>The coal on the grate as lumps (copied for each bed), or null without the bundle.</summary>
        public static Material CoalLumpsMaterial
        {
            get
            {
                Ensure();
                return coalLumpsMaterial;
            }
        }

        /// <summary>The stoker's continuous steam streams, or null in an older bundle.</summary>
        public static Material StokerJetMaterial
        {
            get { Ensure(); return stokerJetMaterial; }
        }

        /// <summary>The fire iron's inventory icon, and its outline for when it lies dropped; null without the bundle.</summary>
        /// <summary>Lays the chimney's smoke drawn at half resolution over the scene (<see cref="RealisticStackEffects.PlumeOffscreen"/>); null in an older bundle.</summary>
        public static Material PlumeCompositeMaterial
        {
            get
            {
                Ensure();
                return plumeCompositeMaterial;
            }
        }

        /// <summary>Lays the scene's depth at half resolution for the chimney's smoke drawn so (<see cref="RealisticStackEffects.PlumeOffscreen"/>); null in an older bundle.</summary>
        public static Material PlumeDepthMaterial
        {
            get
            {
                Ensure();
                return plumeDepthMaterial;
            }
        }

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

        /// <summary>The starting fuel's authored bodies (wood, rags, crate, tin), or null in an older bundle.</summary>
        public static GameObject StartingFuelPrefab
        {
            get { Ensure(); return startingFuelPrefab; }
        }

        /// <summary>The fire-lit copy of a starting fuel material, by the cab material's name; null if none.</summary>
        public static Material StartingFuelFireMaterial(string cabMaterial)
        {
            Ensure();
            return startingFuelAssets.TryGetValue(cabMaterial + "Fire", out var asset) ? asset as Material : null;
        }

        /// <summary>A starting fuel inventory icon by name, or null.</summary>
        public static Texture2D StartingFuelIcon(string name)
        {
            Ensure();
            return startingFuelAssets.TryGetValue(name, out var asset) ? asset as Texture2D : null;
        }

        /// <summary>The fire overlay's typeface for its text and its plates; null without the bundle's fonts.</summary>
        public static Font OverlayFont
        {
            get
            {
                Ensure();
                return overlayFont;
            }
        }

        public static Font PlateFont
        {
            get
            {
                Ensure();
                return plateFont;
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

        // SceneSwitcher destroys all loaded bundles and their assets on the
        // way back to the menu. Detach ours after reading it; unused-asset
        // protection alone cannot stop UnloadAllAssetBundles(true).
        private static void Ensure()
        {
            if (!loaded || Unloaded())
                Load();
        }

        // An asset loaded once and since destroyed: Unity's null for a dead object.
        private static bool Unloaded() =>
            Lost(prefab) || Lost(overflowPrefab) || Lost(fireboxPrefab) || Lost(fireIronMaterial) || Lost(fireboxWallMaterial) || Lost(fireboxVolumeMaterial) || Lost(coalLumpsMaterial) || Lost(plumeCompositeMaterial) || Lost(plumeDepthMaterial) || Lost(fireIronIcon) || Lost(fireIronIconDropped)
            || Lost(stokerJetMaterial) || Lost(overlayFont) || Lost(plateFont) || Lost(startingFuelPrefab);

        private static bool Lost(Object asset) => !ReferenceEquals(asset, null) && asset == null;

        private static void Load()
        {
            loaded = true;
            prefab = overflowPrefab = fireboxPrefab = startingFuelPrefab = null;
            startingFuelAssets.Clear();
            fireIronMaterial = fireboxWallMaterial = fireboxVolumeMaterial = coalLumpsMaterial = plumeCompositeMaterial = plumeDepthMaterial = stokerJetMaterial = null;
            fireIronIcon = fireIronIconDropped = null;
            overlayFont = plateFont = null;
            var path = Path.Combine(Main.Mod.Path, BundleFile);
            if (!File.Exists(path))
            {
                Main.Mod.Logger.Log($"No custom smoke bundle at {path}; using the game's smoke systems");
                return;
            }
            AssetBundle bundle = null;
            try
            {
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    Main.Mod.Logger.Warning($"Could not load the custom smoke bundle {path}; using the game's smoke systems");
                    return;
                }
                prefab = Keep(bundle.LoadAsset<GameObject>(PrefabName));
                overflowPrefab = Keep(bundle.LoadAsset<GameObject>(OverflowPrefabName));
                fireboxPrefab = Keep(bundle.LoadAsset<GameObject>(FireboxPrefabName));
                fireIronMaterial = Usable(Keep(bundle.LoadAsset<Material>(FireIronMaterialName)));
                fireboxWallMaterial = Usable(Keep(bundle.LoadAsset<Material>(FireboxWallMaterialName)));
                fireboxVolumeMaterial = Usable(Keep(bundle.LoadAsset<Material>(FireboxVolumeMaterialName)));
                coalLumpsMaterial = Usable(Keep(bundle.LoadAsset<Material>(CoalLumpsMaterialName)));
                plumeCompositeMaterial = Usable(Keep(bundle.LoadAsset<Material>(PlumeCompositeMaterialName)));
                plumeDepthMaterial = Usable(Keep(bundle.LoadAsset<Material>(PlumeDepthMaterialName)));
                stokerJetMaterial = Usable(Keep(bundle.LoadAsset<Material>(StokerJetMaterialName)));
                startingFuelPrefab = Keep(bundle.LoadAsset<GameObject>(StartingFuelPrefabName));
                foreach (var name in StartingFuelMaterials)
                {
                    var material = Usable(Keep(bundle.LoadAsset<Material>(name + "Fire")));
                    if (material != null) startingFuelAssets[name + "Fire"] = material;
                }
                foreach (var name in StartingFuelIcons)
                {
                    var icon = Keep(bundle.LoadAsset<Texture2D>(name));
                    if (icon != null) startingFuelAssets[name] = icon;
                }
                if (startingFuelPrefab != null && startingFuelPrefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m == null || !m.shader.isSupported)))
                {
                    Main.Mod.Logger.Warning("The starting fuel's materials are not supported by this graphics setup; using simple bodies");
                    startingFuelPrefab = null;
                }
                fireIronIcon = Keep(bundle.LoadAsset<Texture2D>("fire_iron_icon"));
                fireIronIconDropped = Keep(bundle.LoadAsset<Texture2D>("fire_iron_icon_dropped"));
                overlayFont = Keep(bundle.LoadAsset<Font>(OverlayFontName));
                plateFont = Keep(bundle.LoadAsset<Font>(PlateFontName));
                Main.Mod.Logger.Log(prefab != null
                    ? "Custom smoke effects loaded"
                    : $"The smoke bundle has no '{PrefabName}'; using the game's smoke systems");
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Could not load custom effects; using stock visuals", e);
            }
            finally
            {
                // Release the bundle's file data, keeping the objects and their
                // dependencies alive independently of global bundle cleanup.
                if (bundle != null)
                    bundle.Unload(false);
            }
        }

        // A material whose shader this graphics setup cannot run draws as
        // Unity's magenta error shader. It counts as missing, and what uses
        // it draws the stock way, as with no bundle (audit 2026-11-09: the
        // fire volume needs shader model 5 and had no fallback, so there it
        // would have been a magenta block in the firebox with the flame
        // cards that stand in for it hidden).
        private static Material Usable(Material material)
        {
            if (material == null || material.shader != null && material.shader.isSupported)
                return material;
            Main.Mod.Logger.Warning($"'{material.name}' is not supported by this graphics setup; what it draws falls back to the stock look");
            return null;
        }

        // Retain only the assets we use. Their referenced materials, meshes,
        // shaders, and textures are reachable through these retained roots.
        private static T Keep<T>(T asset) where T : Object
        {
            if (asset != null)
                asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return asset;
        }
    }
}
