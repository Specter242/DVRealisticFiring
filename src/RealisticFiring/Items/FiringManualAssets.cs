using System;
using System.IO;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>Compressed, mipmapped pages shared by every manual; no runtime image decoding or rendering.</summary>
    internal static class FiringManualAssets
    {
        internal const string BundleFile = "realisticfiring_manual";
        private static Texture[] pages;
        private static Sprite icon;
        internal static Texture[] Pages
        {
            get
            {
                if (pages == null) Load();
                return pages;
            }
        }
        internal static Sprite Icon => icon;

        private static void Load()
        {
            AssetBundle bundle = null;
            var loaded = new Texture[FiringManualContents.PageCount];
            try
            {
                var path = Path.Combine(Main.Mod.Path, BundleFile);
                if (!File.Exists(path)) throw new FileNotFoundException("Firing manual bundle is missing", path);
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) throw new InvalidDataException("Cannot read the firing manual bundle");
                for (var i = 0; i < loaded.Length; i++)
                {
                    var texture = bundle.LoadAsset<Texture2D>("manual_p" + i.ToString("D2"));
                    if (texture == null) throw new InvalidDataException("Missing manual page " + i);
                    texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    loaded[i] = texture;
                }
                var cover = (Texture2D)loaded[0];
                icon = Sprite.Create(cover, new Rect(0, 0, cover.width, cover.height), new Vector2(0.5f, 0.5f));
                icon.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                pages = loaded;
            }
            catch (Exception error)
            {
                foreach (var texture in loaded)
                    if (texture != null) UnityEngine.Object.Destroy(texture);
                Main.Mod?.Logger.Warning("Could not load the firing manual: " + error.Message);
            }
            finally
            {
                if (bundle != null) bundle.Unload(false);
            }
        }
    }
}
