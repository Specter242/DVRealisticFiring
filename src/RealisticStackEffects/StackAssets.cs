using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticStackEffects
{
    /// <summary>
    /// The custom chimney smoke effects: the plume's prefab and the materials
    /// that draw it at half resolution, in an asset bundle that ships beside
    /// the mod's assembly. Loaded on first use; without it the plume drives
    /// the game's own smoke systems. Kept as Realistic Firing keeps its
    /// effects across the game's bundle cleanup on the way to the menu.
    /// </summary>
    internal static class StackAssets
    {
        public const string BundleFile = "realisticstackeffects_fx";
        public const string PrefabName = "ChimneyPlumeFX";
        public const string CompositeMaterialName = "PlumeComposite";
        public const string DepthMaterialName = "PlumeDepth";

        private static bool loaded;
        private static GameObject prefab;
        private static Material compositeMaterial;
        private static Material depthMaterial;

        /// <summary>The plume prefab, or null when the bundle is missing or unreadable.</summary>
        public static GameObject Prefab
        {
            get
            {
                Ensure();
                return prefab;
            }
        }

        /// <summary>Lays the chimney's smoke drawn at half resolution over the scene (<see cref="PlumeOffscreen"/>); null without the bundle.</summary>
        public static Material CompositeMaterial
        {
            get
            {
                Ensure();
                return compositeMaterial;
            }
        }

        /// <summary>Lays the scene's depth at half resolution for the smoke drawn so; null without the bundle.</summary>
        public static Material DepthMaterial
        {
            get
            {
                Ensure();
                return depthMaterial;
            }
        }

        // SceneSwitcher destroys all loaded bundles and their assets on the
        // way back to the menu. Detach ours after reading it; unused-asset
        // protection alone cannot stop UnloadAllAssetBundles(true).
        private static void Ensure()
        {
            if (!loaded || Lost(prefab) || Lost(compositeMaterial) || Lost(depthMaterial))
                Load();
        }

        // An asset loaded once and since destroyed: Unity's null for a dead object.
        private static bool Lost(Object asset) => !ReferenceEquals(asset, null) && asset == null;

        private static void Load()
        {
            loaded = true;
            prefab = null;
            compositeMaterial = depthMaterial = null;
            var path = Path.Combine(StackHost.Mod.Path, BundleFile);
            if (!File.Exists(path))
            {
                StackHost.Mod.Logger.Log($"No custom smoke bundle at {path}; using the game's smoke systems");
                return;
            }
            AssetBundle bundle = null;
            try
            {
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    StackHost.Mod.Logger.Warning($"Could not load the custom smoke bundle {path}; using the game's smoke systems");
                    return;
                }
                prefab = Keep(bundle.LoadAsset<GameObject>(PrefabName));
                compositeMaterial = Keep(bundle.LoadAsset<Material>(CompositeMaterialName));
                depthMaterial = Keep(bundle.LoadAsset<Material>(DepthMaterialName));
                StackHost.Mod.Logger.Log(prefab != null
                    ? "Custom smoke effects loaded"
                    : $"The smoke bundle has no '{PrefabName}'; using the game's smoke systems");
            }
            catch (Exception e)
            {
                StackHost.Mod.Logger.LogException("Could not load the custom smoke effects; using the game's smoke systems", e);
            }
            finally
            {
                // Release the bundle's file data, keeping the objects and their
                // dependencies alive independently of global bundle cleanup.
                if (bundle != null)
                    bundle.Unload(false);
            }
        }

        // Retain only the assets the plume uses. Their referenced materials,
        // meshes, shaders, and textures are reachable through these roots.
        private static T Keep<T>(T asset) where T : Object
        {
            if (asset != null)
                asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return asset;
        }
    }
}
