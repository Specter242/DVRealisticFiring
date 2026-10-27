using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using RealisticStackEffects;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Compiled with Realistic Stack Effects' production StackAssets.cs in an
// isolated Unity project, as Realistic Firing's PlumeAssets.cs is checked.
// No game launch, game assemblies, or desktop interaction is needed.
[InitializeOnLoad]
public static class StackAssetsCheck
{
    static StackAssetsCheck() => EditorApplication.playModeStateChanged += OnPlay;

    public static void Run()
    {
        SessionState.SetBool("RSEAssetCheck", true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("RSEAssetCheck", false))
            return;
        SessionState.SetBool("RSEAssetCheck", false);
        new GameObject("Stack assets check").AddComponent<StackAssetsRunner>();
    }
}

public sealed class StackAssetsRunner : MonoBehaviour
{
    private IEnumerator Start()
    {
        Object.DontDestroyOnLoad(gameObject);
        var check = Check();
        while (true)
        {
            object current;
            try
            {
                if (!check.MoveNext())
                    break;
                current = check.Current;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
                yield break;
            }
            yield return current;
        }
        Debug.Log("ALL ASSET LIFETIME CHECKS PASSED");
        EditorApplication.Exit(0);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        Debug.Log("ASSET CHECK PASS: " + message);
    }

    private static IEnumerator Check()
    {
        var args = Environment.GetCommandLineArgs();
        var bundlePath = args[Array.IndexOf(args, "-fxBundle") + 1];
        StackHost.Mod.Path = Path.GetDirectoryName(bundlePath);
        var assets = Assets();
        Require(assets.All(a => a != null), "the plume's prefab and its half-resolution materials load");
        Require(!AssetBundle.GetAllLoadedAssetBundles().Any(b => b.name == StackAssets.BundleFile),
            "the plume's assets stay without a loaded bundle handle");
        var materials = assets.OfType<GameObject>().SelectMany(p => p.GetComponentsInChildren<Renderer>(true))
            .SelectMany(r => r.sharedMaterials).Concat(assets.OfType<Material>()).Where(m => m != null).Distinct().ToArray();
        var textures = materials.SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture)).Where(t => t != null).Distinct().ToArray();
        var shaders = materials.Select(m => m.shader).Distinct().ToArray();
        Require(materials.Length > 0 && textures.Length > 0 && shaders.All(s => s != null && s.isSupported),
            $"the plume's {materials.Length} materials carry their textures and {shaders.Length} working shaders");

        for (var reload = 0; reload < 3; reload++)
        {
            var scene = SceneManager.CreateScene("Loaded world " + reload);
            var plume = Object.Instantiate(StackAssets.Prefab);
            SceneManager.MoveGameObjectToScene(plume, scene);
            AssetBundle.UnloadAllAssetBundles(true);
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            Require(plume == null, "the world's plume is destroyed on scene unload " + reload);
            Require(assets.All(a => a != null) && Assets().SequenceEqual(assets),
                "the cached assets survive global bundle and scene cleanup " + reload);
            Require(materials.All(m => m != null) && textures.All(t => t != null) && shaders.All(s => s != null),
                "their dependencies survive cleanup " + reload);
            var next = Object.Instantiate(StackAssets.Prefab);
            Require(next.GetComponentsInChildren<ParticleSystem>(true).Length > 0,
                "the plume's particles instantiate on the next load " + reload);
            Object.Destroy(next);
            yield return null;
        }

        Object.DestroyImmediate(assets[0], true);
        Require(StackAssets.Prefab != null && StackAssets.Prefab != assets[0],
            "an explicitly destroyed cached prefab is loaded again");
        var composite = StackAssets.CompositeMaterial;
        Object.DestroyImmediate(composite, true);
        Require(StackAssets.CompositeMaterial != null && StackAssets.CompositeMaterial != composite,
            "an explicitly destroyed cached material is loaded again");
        Reset();
        StackHost.Mod.Path = Path.Combine(Path.GetTempPath(), "rse-missing-" + Guid.NewGuid());
        Require(Assets().All(a => a == null), "a missing bundle leaves the custom effects unavailable without blocking loading");
    }

    private static Object[] Assets() => new Object[] { StackAssets.Prefab, StackAssets.CompositeMaterial, StackAssets.DepthMaterial };

    private static void Reset()
    {
        foreach (var field in typeof(StackAssets).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            field.SetValue(null, field.FieldType == typeof(bool) ? (object)false : null);
    }
}

namespace RealisticStackEffects
{
    // Only the file location and logging boundary are substituted.
    internal static class StackHost
    {
        internal static readonly ModEntry Mod = new ModEntry();
    }

    internal sealed class ModEntry
    {
        internal string Path;
        internal readonly ModLogger Logger = new ModLogger();
    }

    internal sealed class ModLogger
    {
        internal void Log(string message) => Debug.Log(message);
        internal void Warning(string message) => Debug.LogWarning(message);
        internal void LogException(string message, Exception e) => Debug.LogWarning(message + ": " + e);
    }
}
