using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring
{
 internal static class OverlayStyle
    {
        public const float MinScale = 0.85f;
        public const float MaxScale = 1.5f;

        /// <summary>How much a layout for a 1080-line screen is scaled on this one.</summary>
        public static float Scale => Mathf.Clamp(Screen.height / 1080f, MinScale, MaxScale);

        /// <summary>The overlay's face, or null (the skin's) without the effects bundle.</summary>
        public static Font Font => PlumeAssets.OverlayFont;

        /// <summary>The plates' heavier face.</summary>
        public static Font PlateFont => PlumeAssets.PlateFont != null ? PlumeAssets.PlateFont : PlumeAssets.OverlayFont;

        /// <summary>A line of text, unwrapped and clipped, with no padding or margin.</summary>
        public static GUIStyle Text(int size, Color color, TextAnchor anchor, Font font = null) => new GUIStyle(GUI.skin.label)
        {
            font = font != null ? font : Font, fontSize = size, alignment = anchor, wordWrap = false, clipping = TextClipping.Clip,
            padding = new RectOffset(), margin = new RectOffset(), normal = { textColor = color },
        };

        /// <summary>A sliced background.</summary>
        public static GUIStyle Back(Texture2D texture, int border) =>
            new GUIStyle { normal = { background = texture }, border = new RectOffset(border, border, border, border) };

        /// <summary>An enamel plate to press: dim, lit brass while pressed.</summary>
        public static GUIStyle PlateOff(int size) => new GUIStyle(Text(size, OverlayArt.Dim, TextAnchor.MiddleCenter, PlateFont))
        {
            normal = { background = OverlayArt.PlateOff, textColor = OverlayArt.Dim },
            hover = { background = OverlayArt.PlateOff, textColor = OverlayArt.Soft },
            active = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
            border = new RectOffset(5, 5, 5, 5),
        };

        /// <summary>The plate that is chosen: brass-edged and cream.</summary>
        public static GUIStyle PlateOn(GUIStyle off) => new GUIStyle(off)
        {
            normal = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
            hover = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
        };

        /// <summary>Draws a reading's state mark, tinted by its level.</summary>
        public static void Mark(Core.AdviceLevel level, Rect rect)
        {
            var old = GUI.color;
            GUI.color = OverlayArt.LevelColor(level);
            GUI.DrawTexture(rect, OverlayArt.Mark(level));
            GUI.color = old;
        }

        /// <summary>
        /// The next step's box: a tinted plate, a bar of the level's color
        /// down its left and the level's mark; its text goes 30 in.
        /// </summary>
        public static void LevelBox(Rect rect, Core.AdviceLevel level, GUIStyle fill)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = level == Core.AdviceLevel.Act ? new Color(OverlayArt.Alarm.r, OverlayArt.Alarm.g, OverlayArt.Alarm.b, 0.08f) : new Color(1f, 1f, 1f, 0.05f);
            GUI.Box(rect, GUIContent.none, fill);
            GUI.backgroundColor = old;
            var color = GUI.color;
            GUI.color = OverlayArt.LevelColor(level);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), OverlayArt.White);
            GUI.color = color;
            Mark(level, new Rect(rect.x + 12f, rect.y + 12f, 11f, 11f));
        }

        /// <summary>The card's rivets in its top corners.</summary>
        public static void Rivets(float width)
        {
            GUI.DrawTexture(new Rect(7f, 7f, 6f, 6f), OverlayArt.Rivet);
            GUI.DrawTexture(new Rect(width - 13f, 7f, 6f, 6f), OverlayArt.Rivet);
        }

        /// <summary>A row's rule under it.</summary>
        public static void Rule(Rect row)
        {
            var old = GUI.color;
            GUI.color = OverlayArt.Rule;
            GUI.DrawTexture(new Rect(row.x, row.yMax - 1f, row.width, 1f), OverlayArt.White);
            GUI.color = old;
        }
    }
}
