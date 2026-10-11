using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using RealisticFiring.Visuals;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Compiled with the production PlumeAssets.cs in an isolated Unity project.
// No game launch, game assemblies, or desktop interaction is needed.
[InitializeOnLoad]
public static class AssetLifetimeCheck
{
    static AssetLifetimeCheck() => EditorApplication.playModeStateChanged += OnPlay;

    public static void Run()
    {
        SessionState.SetBool("RFAssetCheck", true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("RFAssetCheck", false))
            return;
        SessionState.SetBool("RFAssetCheck", false);
        new GameObject("Asset lifetime check").AddComponent<AssetLifetimeRunner>();
    }
}

public sealed class AssetLifetimeRunner : MonoBehaviour
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
        RealisticFiring.Main.Mod.Path = Path.GetDirectoryName(bundlePath);
        var assets = Assets();
        Require(assets.All(a => a != null), "all six production effects assets load");
        Require(!AssetBundle.GetAllLoadedAssetBundles().Any(b => b.name == "realisticfiring_fx"),
            "effects retain their objects without a loaded bundle handle");
        var materials = assets.OfType<GameObject>().SelectMany(p => p.GetComponentsInChildren<Renderer>(true))
            .SelectMany(r => r.sharedMaterials).Append((Material)assets[3]).Where(m => m != null).Distinct().ToArray();
        var textures = materials.SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture)).Where(t => t != null).Distinct().ToArray();
        var shaders = materials.Select(m => m.shader).Distinct().ToArray();
        Require(materials.Length > 0 && textures.Length > 0 && shaders.Length > 0,
            "bundle prefabs expose material, texture, and shader dependencies");

        // A dormant fire-iron template survives scenes and holds these assets.
        var iron = new GameObject("Persistent item template");
        iron.SetActive(false);
        Object.DontDestroyOnLoad(iron);
        iron.AddComponent<MeshRenderer>().sharedMaterial = (Material)assets[3];

        for (var reload = 0; reload < 3; reload++)
        {
            var scene = SceneManager.CreateScene("Loaded world " + reload);
            var effects = assets.OfType<GameObject>().Select(Object.Instantiate).ToArray();
            foreach (var effect in effects)
                SceneManager.MoveGameObjectToScene(effect, scene);
            AssetBundle.UnloadAllAssetBundles(true);
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            Require(effects.All(e => e == null), "world effects are destroyed on scene unload " + reload);
            Require(assets.All(a => a != null) && Assets().SequenceEqual(assets),
                "cached effects and icons survive global bundle and scene cleanup " + reload);
            Require(materials.All(m => m != null) && textures.All(t => t != null)
                    && shaders.All(s => s != null)
                    && iron.GetComponent<MeshRenderer>().sharedMaterial == assets[3],
                "dependencies and the persistent item's material survive cleanup " + reload);
            var nextWorld = Object.Instantiate(PlumeAssets.Prefab);
            Require(nextWorld.GetComponentsInChildren<ParticleSystem>(true).Length > 0,
                "custom particles instantiate on the next load " + reload);
            Object.Destroy(nextWorld);
            yield return null;
        }

        Object.DestroyImmediate(assets[0], true);
        Require(PlumeAssets.Prefab != null && PlumeAssets.Prefab != assets[0],
            "an explicitly destroyed cached prefab is loaded again");
        Reset();
        RealisticFiring.Main.Mod.Path = Path.Combine(Path.GetTempPath(), "rf-missing-" + Guid.NewGuid());
        Require(Assets().All(a => a == null) && Assets().All(a => a == null),
            "a missing bundle leaves optional visuals unavailable without blocking loading");
    }

    private static Object[] Assets() => new Object[]
    {
        PlumeAssets.Prefab, PlumeAssets.OverflowPrefab, PlumeAssets.FireboxPrefab,
        PlumeAssets.FireIronMaterial, PlumeAssets.FireIronIcon, PlumeAssets.FireIronIconDropped,
    };

    private static void Reset()
    {
        foreach (var field in typeof(PlumeAssets).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            field.SetValue(null, field.FieldType == typeof(bool) ? (object)false : null);
    }
}

namespace RealisticFiring
{
    // Only the file location and logging boundary are substituted.
    internal static class Main
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
