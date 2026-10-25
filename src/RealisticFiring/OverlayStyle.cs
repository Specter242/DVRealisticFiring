using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring
{
 internal static class OverlayFrame
    {
        public const float Width = 540f;
        public const float Height = 462f;
        public const float Pad = 16f;
        public const float GaugeTop = 46f;
        public const float HeaderTop = 12f;
        public const float TabHeight = 26f;
        public const float LocoWidth = 200f;
        public const float GaugePlateTop = 78f;
        public const float GaugePlateHeight = 20f;
        public const float GaugeValueTop = 101f;
        public const float GaugeValueHeight = 24f;
        public const float GaugeMarkTop = 107f;
        public const float GaugeSmokeTop = 127f;
        public const float BodyTop = 180f;
        public const float BodyHeight = 196f;
        public const float NextTop = 388f;
        public const float NextHeight = 64f;
        public const float GaugeGap = 6f;
        public const float GaugeDialWidth = 112f;
    }

 internal static class OverlayTabs
    {
        public const int Count = 3;
        public const float Width = 96f;
        public const float Height = 26f;
        public const float Gap = 6f;
        public const float VrScale = 1.875f;
        private static Sprite plateOff;
        private static Sprite plateOn;

        public static string Name(int page, bool oil = false) => page == 0 && oil ? "BURNER" : page == 0 ? "FIRE" : page == 1 ? "WATER" : "ENGINE";
        public static Color OffText => OverlayArt.Dim;
        public static Color HoverText => OverlayArt.Soft;
        public static Color OnText => OverlayArt.Cream;

        public static Sprite OffSprite => Plate(ref plateOff, OverlayArt.PlateOff);
        public static Sprite OnSprite => Plate(ref plateOn, OverlayArt.PlateOn);

        private static Sprite Plate(ref Sprite sprite, Texture2D texture)
        {
            if (sprite == null || sprite.texture != texture)
                sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(5f, 5f, 5f, 5f));
            return sprite;
        }
    }

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
            normal = { background = OverlayArt.PlateOff, textColor = OverlayTabs.OffText },
            hover = { background = OverlayArt.PlateOff, textColor = OverlayTabs.HoverText },
            active = { background = OverlayArt.PlateOn, textColor = OverlayTabs.OnText },
            border = new RectOffset(5, 5, 5, 5),
        };

        /// <summary>The plate that is chosen: brass-edged and cream.</summary>
        public static GUIStyle PlateOn(GUIStyle off) => new GUIStyle(off)
        {
            normal = { background = OverlayArt.PlateOn, textColor = OverlayTabs.OnText },
            hover = { background = OverlayArt.PlateOn, textColor = OverlayTabs.OnText },
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
