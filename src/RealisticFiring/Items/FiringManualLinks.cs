using System.Collections;
using DV.CabControls;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>Connects the prebuilt stock map touchscreen to the manual's contents.</summary>
    internal sealed class FiringManualLinks : MonoBehaviour
    {
        private PageBook book;
        private TouchscreenBase screen;
        private BoxCollider hitbox;
        private bool ready, subscribed;

        private void OnEnable() => StartCoroutine(Initialize());

        private IEnumerator Initialize()
        {
            book = GetComponent<PageBook>();
            var control = FiringManualControl.Find(gameObject);
            if (control == null)
            {
                Main.Mod?.Logger.Warning("The firing manual has no cloned stock contents control");
                yield break;
            }
            hitbox = control.GetComponent<BoxCollider>();
            hitbox.enabled = false;
            control.gameObject.SetActive(true);
            // The subtree is authored in the inactive item template. Its stock
            // spec may wake after this parent; allow that Awake/Start to finish.
            yield return null;
            screen = control.GetComponent<TouchscreenBase>();
            if (screen == null)
            {
                control.gameObject.SetActive(false);
                Main.Mod?.Logger.Warning("The firing manual's stock contents control did not wake");
                yield break;
            }
            if (!subscribed)
            {
                screen.SectionPressed += Pressed;
                book.PageFlipped += PageFlipped;
                subscribed = true;
            }
            while (!book.PagesGenerated || !screen.IsInitialized) yield return null;
            ready = true;
            SetInteractive(book.currentPage == FiringManualContents.ContentsPage);
        }

        internal static Vector3 PrintedPoint(Vector2 point) => new Vector3(
            (0.5f - point.x / FiringManualContents.Width) * FiringManual.PageWidth,
            0f,
            (point.y / FiringManualContents.Height - 0.5f) * FiringManual.PageHeight);

        private void SetInteractive(bool visible)
        {
            if (hitbox == null) return;
            hitbox.enabled = visible;
            if (screen == null) return;
            if (!visible) screen.Untouch();
            if (screen.gameObject.activeSelf != visible) screen.gameObject.SetActive(visible);
        }

        private void PageFlipped(int page)
        {
            if (!ready) return;
            // Matches the stock-based brake booklet: page identity is the only
            // visibility gate; do not hold input off on animation-state polling.
            SetInteractive(page == FiringManualContents.ContentsPage);
        }

        private void Pressed(Vector2Int section)
        {
            if (section.x == 0 && FiringManualContents.TryDestination(section.y, book.currentPage, out var page))
            {
                book.FlipTo(page);
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            ready = false;
            SetInteractive(false);
        }

        private void OnDestroy()
        {
            if (screen != null)
            {
                screen.SectionPressed -= Pressed;
            }
            if (book != null) book.PageFlipped -= PageFlipped;
        }
    }
}
