using System;
using System.Collections;
using System.IO;
using System.Linq;
using RealisticFiring.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ManualAssetCheck
{
    static ManualAssetCheck() => EditorApplication.playModeStateChanged += state =>
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("RFManualCheck", false))
        {
            SessionState.SetBool("RFManualCheck", false);
            new GameObject("Manual asset check").AddComponent<ManualAssetRunner>();
        }
    };
    public static void Run() { SessionState.SetBool("RFManualCheck", true); EditorApplication.isPlaying = true; }
}

public sealed class ManualAssetRunner : MonoBehaviour
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
                if (!check.MoveNext()) break;
                current = check.Current;
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); yield break; }
            yield return current;
        }
        Debug.Log("ALL ASSET LIFETIME CHECKS PASSED");
        EditorApplication.Exit(0);
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        Debug.Log("ASSET CHECK PASS: " + message);
    }

    private IEnumerator Check()
    {
        var args = Environment.GetCommandLineArgs();
        RealisticFiring.Main.Mod.Path = Path.GetDirectoryName(args[Array.IndexOf(args, "-fxBundle") + 1]);
        var pages = FiringManualAssets.Pages;
        Require(pages != null && pages.Length == FiringManualContents.PageCount && pages.All(p => p != null), "all selected manual pages load, including the throttle lesson");
        var textures = pages.Cast<Texture2D>().ToArray();
        Require(textures.All(t => t.width == 1024 && t.height == 2048 && t.mipmapCount > 1 && !t.isReadable
                             && t.format == TextureFormat.BC7 && t.filterMode == FilterMode.Trilinear),
            "manual pages use 1024x2048 BC7 textures, mipmaps, and no CPU pixel copy");
        Require(FiringManualAssets.Icon != null && FiringManualAssets.Icon.texture == pages[0], "manual inventory icon uses its own cover");
        Require(!AssetBundle.GetAllLoadedAssetBundles().Any(b => b.name == FiringManualAssets.BundleFile),
            "manual releases its bundle handle after loading");
        var lobby = SceneManager.CreateScene("manual lobby");
        for (var n = 0; n < 2; n++)
        {
            var scene = SceneManager.CreateScene("manual world " + n);
            SceneManager.SetActiveScene(scene);
            var page = new GameObject("Manual page reference").AddComponent<MeshRenderer>();
            page.sharedMaterial = new Material(Shader.Find("Unlit/Texture")) { mainTexture = pages[2] };
            var material = page.sharedMaterial;
            SceneManager.SetActiveScene(lobby);
            yield return SceneManager.UnloadSceneAsync(scene);
            Object.Destroy(material);
            AssetBundle.UnloadAllAssetBundles(true);
            yield return Resources.UnloadUnusedAssets();
            Require(textures.All(t => t != null) && ReferenceEquals(pages, FiringManualAssets.Pages)
                    && FiringManualAssets.Icon != null, "manual pages and icon survive scene and global asset cleanup " + n);
        }
    }
}

namespace RealisticFiring
{
    internal static class Main { internal static readonly ModEntry Mod = new ModEntry(); }
    internal sealed class ModEntry { internal string Path; internal readonly ModLogger Logger = new ModLogger(); }
    internal sealed class ModLogger { internal void Warning(string message) => Debug.LogWarning(message); }
}
