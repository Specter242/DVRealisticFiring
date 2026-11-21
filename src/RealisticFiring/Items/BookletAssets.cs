using System;
using System.IO;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// One booklet's compressed, mipmapped pages from the booklets' bundle,
    /// shared by every copy of that booklet: no runtime image decoding or
    /// rendering. The bundle holds the firing manual's pages and the checklist
    /// booklet's; each set opens it, takes its own pages, and lets it go.
    /// </summary>
    internal sealed class BookletAssets
    {
        internal const string BundleFile = "realisticfiring_manual";
        internal static readonly BookletAssets Manual = new BookletAssets("firing manual", "manual_p", FiringManualContents.PageCount);
        internal static readonly BookletAssets Checklists = new BookletAssets("firing checklists", "checklists_p", ChecklistBookletContents.PageCount);

        private readonly string what, prefix;
        private readonly int count;
        private Texture[] pages;
        private Sprite icon;

        private BookletAssets(string what, string prefix, int count)
        {
            this.what = what;
            this.prefix = prefix;
            this.count = count;
        }

        /// <summary>The pages in order, the cover first; null when they could not be loaded (logged).</summary>
        internal Texture[] Pages
        {
            get
            {
                if (pages == null) Load();
                return pages;
            }
        }

        /// <summary>The inventory icon: the cover.</summary>
        internal Sprite Icon => icon;

        private void Load()
        {
            AssetBundle bundle = null;
            var loaded = new Texture[count];
            try
            {
                var path = Path.Combine(Main.Mod.Path, BundleFile);
                if (!File.Exists(path)) throw new FileNotFoundException("The booklets' bundle is missing", path);
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) throw new InvalidDataException("Cannot read the booklets' bundle");
                for (var i = 0; i < loaded.Length; i++)
                {
                    var texture = bundle.LoadAsset<Texture2D>(prefix + i.ToString("D2"));
                    if (texture == null) throw new InvalidDataException($"Missing {what} page {i}");
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
                Main.Mod?.Logger.Warning($"Could not load the {what}: " + error.Message);
            }
            finally
            {
                if (bundle != null) bundle.Unload(false);
            }
        }
    }
}
