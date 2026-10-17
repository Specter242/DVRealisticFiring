using System.Collections.Generic;
using UnityEngine;

namespace RealisticFiring.Shed
{
 internal static class WaterFittings
    {
        /// <summary>The fitting stands this far out from the firebox to its spud's mouth, where the hose's coupling seats.</summary>
        public const float Proud = 0.17f;

        private static readonly Vector3 Up = Vector3.up;
        private static readonly Vector3 Across = Vector3.right;

        private const string HandleName = "cock handle";

        private static Vector2 P(float r, float h) => new Vector2(r, h);

        /// <summary>
        /// Builds the filling connection under <paramref name="socket"/> (its
        /// +y outboard, its origin the spud's mouth) on <paramref name="layer"/>,
        /// its spud sized for a hose <paramref name="scale"/> times the blower
        /// line's. Returns it.
        /// </summary>
        public static Transform Filling(Transform socket, int layer, float scale)
        {
            var mesh = new FittingMesh();
            var face = -Proud;
            const float flange = 0.014f;
            const float flangeRadius = 0.055f;
            // The flange, round, on the firebox's sheet, and its four studs' nuts.
            mesh.Turned(Metal.Iron, new[]
            {
                P(0f, face), P(flangeRadius, face), P(flangeRadius, face + flange - 0.003f), P(flangeRadius - 0.003f, face + flange), P(0f, face + flange),
            }, Vector3.zero, Up);
            for (var i = 0; i < 4; i++)
            {
                var a = (45f + 90f * i) * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.041f;
                mesh.Rod(Metal.Steel, 0.0065f, face + flange, face + flange + 0.018f, at, Up);
                mesh.Hex(Metal.Steel, 0.019f, face + flange, face + flange + 0.012f, at, Up, 30f);
            }
            // The nipple out from the flange.
            var cockFrom = -0.115f;
            mesh.Rod(Metal.Iron, 0.024f, face + flange, cockFrom, Vector3.zero, Up);
            // The plug cock: its body, the tapered plug across it, a square
            // head, and a flat handle along the pipe when open.
            var cockTo = cockFrom + 0.05f;
            mesh.Turned(Metal.Brass, new[]
            {
                P(0f, cockFrom), P(0.03f, cockFrom), P(0.034f, cockFrom + 0.008f), P(0.034f, cockTo - 0.008f), P(0.03f, cockTo), P(0f, cockTo),
            }, Vector3.zero, Up);
            var plug = new Vector3(0f, 0.5f * (cockFrom + cockTo), 0f);
            mesh.Turned(Metal.Brass, new[] { P(0f, -0.042f), P(0.02f, -0.042f), P(0.017f, 0.04f), P(0f, 0.04f) }, plug, Across);
            // The hose spud: the blower fitting's, scaled: its hex, its
            // coarse thread to the mouth, and its bore.
            mesh.Hex(Metal.Steel, 0.041f * scale, cockTo, -0.026f * scale, Vector3.zero, Up);
            var spud = new List<Vector2>();
            for (var i = 0; i < 8; i++)
            {
                var h = (-0.026f + 0.003f * i) * scale;
                spud.Add(P(0.0162f * scale, h));
                spud.Add(P(0.0178f * scale, h + 0.0015f * scale));
            }
            spud.AddRange(new[]
            {
                P(0.0162f * scale, -0.002f * scale), P(0.0150f * scale, 0f), P(0.0115f * scale, 0f), P(0.0115f * scale, -0.018f * scale), P(0f, -0.018f * scale),
            });
            mesh.Turned(Metal.Steel, spud, Vector3.zero, Up);
            var fitting = mesh.Build(socket, "RealisticFiring filling connection", layer);

            // The plug's square head and the handle on it, turning about the
            // plug's axis.
            var handle = new FittingMesh();
            handle.Box(Metal.Brass, Across * 0.047f, new Vector3(0.014f, 0.016f, 0.016f), Quaternion.identity);
            handle.Box(Metal.Iron, Across * 0.06f + Up * 0.045f, new Vector3(0.012f, 0.11f, 0.02f), Quaternion.identity);
            handle.Build(fitting, HandleName, layer).localPosition = plug;
            Cock(fitting, false);
            return fitting;
        }

        /// <summary>Turns the filling connection's cock open, its handle along the pipe, or shut, across it.</summary>
        public static void Cock(Transform fitting, bool open)
        {
            var handle = fitting != null ? fitting.Find(HandleName) : null;
            if (handle != null)
                handle.localRotation = open ? Quaternion.identity : Quaternion.AngleAxis(90f, Across);
        }
    }
}
