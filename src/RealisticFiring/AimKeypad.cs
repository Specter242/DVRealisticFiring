using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring
{
 internal static class AimKeypad
    {
        private const float LineWidth = 2f;
        private static readonly Color Line = new Color(1f, 0.85f, 0.45f, 0.85f);
        private static readonly Color Lit = new Color(0.55f, 0.8f, 1f);
        private static readonly Color Unlit = new Color(1f, 0.95f, 0.85f, 0.9f);
        private static GUIStyle label;
        private static string logged;

        public static void Draw(DoorAimSample aim)
        {
            var camera = Camera.main;
            if (camera == null || aim.Car == null || aim.Box.size == Vector3.zero)
                return;
            var box = aim.Box;
            // The firehole as calibrated, for writing into FireholeOpening.
            var hole = $"middle across {box.center.x:F3} m, foot {box.min.y:F3} m, {box.size.x:F3} by {box.size.y:F3} m, in the plane z {box.min.z:F3} m";
            if (hole != logged && Event.current.type == EventType.Repaint)
            {
                logged = hole;
                var car = TrainCar.Resolve(aim.Car.gameObject);
                var livery = car != null && car.carLivery != null ? car.carLivery.id : aim.Car.name;
                Main.Mod?.Logger.Log($"Firehole for the aim on '{livery}': {hole}");
            }
            // The face toward the cab: facing the firebox faces the car's front.
            Vector2? Point(float across, float up)
            {
                var local = new Vector3(Mathf.Lerp(box.min.x, box.max.x, across), Mathf.Lerp(box.min.y, box.max.y, up), box.min.z);
                var screen = camera.WorldToScreenPoint(aim.Car.TransformPoint(local));
                return screen.z > 0f ? new Vector2(screen.x, Screen.height - screen.y) : (Vector2?)null;
            }

            for (var i = 0; i <= 3; i++)
            {
                var t = i / 3f;
                DrawLine(Point(t, 0f), Point(t, 1f));
                DrawLine(Point(0f, t), Point(1f, t));
            }

            label = label ?? new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true };
            for (var row = 0; row < GrateLayout.Rows; row++)
            for (var column = 0; column < GrateLayout.Columns; column++)
            {
                var centre = Point((column + 0.5f) / 3f, (row + 0.5f) / 3f);
                if (!centre.HasValue)
                    continue;
                var zone = GrateLayout.At(row, column);
                var area = new Rect(centre.Value.x - 60f, centre.Value.y - 18f, 120f, 36f);
                label.normal.textColor = Color.black;
                GUI.Label(new Rect(area.x + 1f, area.y + 1f, area.width, area.height), GrateLayout.Describe(zone), label);
                label.normal.textColor = zone == aim.Zone ? Lit : Unlit;
                GUI.Label(area, GrateLayout.Describe(zone), label);
            }
        }

        private static void DrawLine(Vector2? from, Vector2? to)
        {
            if (!from.HasValue || !to.HasValue)
                return;
            var a = from.Value;
            var d = to.Value - a;
            var matrix = GUI.matrix;
            var color = GUI.color;
            GUI.color = Line;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - LineWidth / 2f, d.magnitude, LineWidth), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = color;
        }
    }
}
