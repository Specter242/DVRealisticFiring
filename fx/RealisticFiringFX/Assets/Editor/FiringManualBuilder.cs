using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RealisticFiringFX
{
    /// <summary>
    /// Builds only the booklets' bundle, the firing manual's pages and the
    /// checklist booklet's; leaves both smoke bundles unchanged.
    /// </summary>
    public static class FiringManualBuilder
    {
        // The manual's pages (tools/firing-booklet/build-manual.py) and the
        // checklist booklet's (tools/firing-booklet/build-checklists.py).
        private const int ManualPages = 19, ChecklistPages = 7;

        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-outputPath");
            var output = at >= 0 ? args[at + 1] : "Build/AssetBundles";
            AssetDatabase.Refresh();
            var files = new string[ManualPages + ChecklistPages];
            for (var i = 0; i < files.Length; i++)
            {
                var path = i < ManualPages
                    ? "Assets/RealisticFiring/Manual/manual_p" + i.ToString("D2") + ".png"
                    : "Assets/RealisticFiring/Manual/checklists_p" + (i - ManualPages).ToString("D2") + ".png";
                if (!File.Exists(path)) throw new FileNotFoundException("Missing staged booklet page", path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.isReadable = false;
                importer.npotScale = TextureImporterNPOTScale.ToNearest;
                importer.maxTextureSize = 2048;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                var platform = importer.GetPlatformTextureSettings("Standalone");
                platform.overridden = true;
                platform.maxTextureSize = 2048;
                platform.format = TextureImporterFormat.BC7;
                platform.compressionQuality = 100;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
                files[i] = path;
            }
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output,
                new[] { new AssetBundleBuild { assetBundleName = "realisticfiring_manual", assetNames = files } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new InvalidOperationException("Firing manual bundle build failed");
            Debug.Log($"FIRING MANUAL BUILD PASS: {ManualPages} manual and {ChecklistPages} checklist mipmapped page textures");
        }
    }
}
