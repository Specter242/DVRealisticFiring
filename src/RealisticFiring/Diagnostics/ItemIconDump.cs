using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DV;
using RealisticFiring.Items;
using UnityEngine;
using Object = UnityEngine.Object;

#if !PUBLIC_RELEASE
namespace RealisticFiring.Diagnostics
{
    /// <summary>
    /// Writes the inventory icons to PNG from a running game, to compare the
    /// mod's with the game's (Items/ItemIcons.cs): each of the mod's items'
    /// normal and dropped icons as the hotbar shows them, a few stock items'
    /// own icons, and those stock items rendered by <see cref="ItemIcons"/>
    /// from their previews in the view the game used, to check the renderer
    /// against the game's. Set <c>DumpItemIcons</c> in Settings.xml (not in
    /// the menu); once a game is loaded the icons go to <c>icon-dump/</c> in
    /// the mod's folder with an <c>index.tsv</c>, and the setting turns off.
    /// The PNGs hold the game's art: local reference, never committed.
    /// </summary>
    internal static class ItemIconDump
    {
        /// <summary>Stock items to compare with: tools, a device, a booklet, a lamp.</summary>
        internal static readonly string[] Stock =
        {
            "shovel", "Hammer", "Oiler", "Lantern", "Lighter", "Flashlight", "CommsRadio", "Clinometer", "Calculator",
            "VehicleCatalog", "CanisterFuel", "Crate", "BoxCardboard_open", "PaintCan",
        };

        /// <summary>The mod's items, by prefab name, built if they are not yet.</summary>
        internal static IEnumerable<(string Name, GameObject Template)> Ours()
        {
            yield return (SandScoop.PrefabName, SandScoop.Template);
            yield return (FireIron.PrefabName, FireIron.Template);
            yield return (FiringTablet.PrefabName, FiringTablet.Template);
            yield return (FiringManual.PrefabName, FiringManual.Template);
            yield return (ChecklistBooklet.PrefabName, ChecklistBooklet.Template);
            foreach (StartingFuelKind kind in Enum.GetValues(typeof(StartingFuelKind)))
            {
                var template = FireStartingItems.Template(kind);
                yield return (template != null ? template.name : "StartingFuel_" + kind, template);
            }
        }

        /// <summary>Dumps into <paramref name="folder"/>; returns how many PNGs it wrote.</summary>
        internal static int Dump(string folder)
        {
            Directory.CreateDirectory(folder);
            var index = new StringBuilder("file\tsource\titem\twidth\theight\n");
            var written = 0;
            void Write(string file, string source, string item, Sprite sprite)
            {
                var size = Save(sprite, Path.Combine(folder, file));
                if (size == null)
                    return;
                written++;
                index.Append($"{file}\t{source}\t{item}\t{size.Value.x}\t{size.Value.y}\n");
            }

            foreach (var (name, template) in Ours())
            {
                var spec = template != null ? template.GetComponent<InventoryItemSpec>() : null;
                if (spec == null)
                    continue;
                Write($"ours_{name}_Icon.png", "ours", name, spec.ItemIconSprite);
                Write($"ours_{name}_Icon_Dropped.png", "ours", name, spec.ItemIconSpriteDropped);
            }
            foreach (var name in Stock)
            {
                var spec = StockSpec(name);
                if (spec == null)
                    continue;
                Write($"stock_{name}_Icon.png", "stock", name, spec.ItemIconSprite);
                Write($"stock_{name}_Icon_Dropped.png", "stock", name, spec.ItemIconSpriteDropped);
                if (spec.PreviewPrefab == null)
                    continue;
                try
                {
                    var icons = ItemIcons.RenderNow(spec.PreviewPrefab, ItemIcons.StockView(spec), name, ItemIcons.Light,
                        ItemIcons.Canvas, ItemIcons.Supersample, ItemIcons.LongSide);
                    Write($"rendered_{name}_Icon.png", "rendered", name, icons.Icon);
                    Write($"rendered_{name}_Icon_Dropped.png", "rendered", name, icons.Dropped);
                    Release(icons.Icon);
                    Release(icons.Dropped);
                }
                catch (Exception error)
                {
                    index.Append($"-\trendered\t{name}\tfailed: {error.Message}\t\n");
                }
            }
            File.WriteAllText(Path.Combine(folder, "index.tsv"), index.ToString());
            return written;
        }

        /// <summary>A stock item's spec: from the game's item list, else its prefab.</summary>
        internal static InventoryItemSpec StockSpec(string prefab)
        {
            var listed = Globals.G?.Items?.items?.FirstOrDefault(s => s != null && string.Equals(s.ItemPrefabName, prefab, StringComparison.OrdinalIgnoreCase));
            if (listed != null)
                return listed;
            var loaded = Resources.Load<GameObject>(prefab);
            return loaded != null ? loaded.GetComponent<InventoryItemSpec>() : null;
        }

        /// <summary>Writes <paramref name="sprite"/>'s pixels (its own rectangle of its texture, read through the GPU) as a PNG; its size, or null.</summary>
        internal static Vector2Int? Save(Sprite sprite, string path)
        {
            if (sprite == null || sprite.texture == null)
                return null;
            var texture = sprite.texture;
            var rect = sprite.textureRect;
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            var read = new Texture2D(Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height), TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(rect.x, rect.y, read.width, read.height), 0, 0, false);
                read.Apply(false, false);
                File.WriteAllBytes(path, read.EncodeToPNG());
                return new Vector2Int(read.width, read.height);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(read);
            }
        }

        private static void Release(Sprite sprite)
        {
            if (sprite == null)
                return;
            if (sprite.texture != null)
                Object.Destroy(sprite.texture);
            Object.Destroy(sprite);
        }
    }
}
#endif
