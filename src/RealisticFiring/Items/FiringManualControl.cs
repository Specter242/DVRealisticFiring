using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    /// <summary>Installs the stock map's serialized touchscreen before any manual control wakes.</summary>
    internal static class FiringManualControl
    {
        internal const string MapPrefab = "MapSchematic", SourceAnchor = "TouchScreenMapAnchor";
        internal const string AnchorName = "[firing manual contents anchor]";

        internal static void Install(GameObject item)
        {
            if (item.activeInHierarchy)
                throw new InvalidOperationException("The manual touchscreen must be fitted while its template is inactive");
            var map = Resources.Load<GameObject>(MapPrefab);
            var source = map != null ? map.transform.Find(SourceAnchor) : null;
            if (source == null) throw new InvalidOperationException("The stock map contents touchscreen is missing");
            var anchor = Object.Instantiate(source, item.transform, false);
            anchor.name = AnchorName;
            var screen = anchor.Find("Touchscreen");
            var spec = screen != null ? screen.GetComponent<DV.CabControls.Spec.Touchscreen>() : null;
            var box = screen != null ? screen.GetComponent<BoxCollider>() : null;
            if (spec == null || box == null || screen.GetComponent<HighlightTag>() == null || !box.isTrigger)
                throw new InvalidOperationException("The stock map touchscreen has changed");
            // Unity remaps references inside the cloned subtree. Keep the stock
            // collider objects, hand poses, tolerance, audio, and interaction data.
            foreach (var colliderObject in spec.colliderGameObjects)
                if (colliderObject == null || !colliderObject.transform.IsChildOf(anchor))
                    throw new InvalidOperationException("A cloned touchscreen collider still points outside its anchor");
            var entries = FiringManualContents.Entries;
            var first = entries[0].Area;
            var area = Rect.MinMaxRect(first.xMin, first.yMin, first.xMax, entries[entries.Length - 1].Area.yMax);
            Place(anchor, screen, box, area);
            ConfigureGrid(spec, entries.Length);
        }

        internal static Transform Find(GameObject item) => item.transform.Find(AnchorName + "/Touchscreen");

        internal static void Place(Transform anchor, Transform screen, BoxCollider box, Rect area)
        {
            var printed = FiringManualLinks.PrintedPoint(area.center);
            anchor.localPosition = new Vector3(printed.x, anchor.localPosition.y, printed.z);
            anchor.localRotation = Quaternion.identity;
            screen.localPosition = Vector3.zero;
            screen.localRotation = Quaternion.identity;
            box.center = new Vector3(0f, box.center.y, 0f);
            box.size = new Vector3(area.width / FiringManualContents.Width * FiringManual.PageWidth,
                box.size.y, area.height / FiringManualContents.Height * FiringManual.PageHeight);
        }

        internal static void ConfigureGrid(DV.CabControls.Spec.Touchscreen spec, int rows)
        {
            spec.gridSize = new Vector2Int(1, rows);
            spec.useEntireScreen = true;
            spec.flipHorizontalCoords = spec.flipVerticalCoords = false;
        }
    }
}
