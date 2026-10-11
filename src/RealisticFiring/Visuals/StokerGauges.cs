using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using TMPro;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The stoker's gauges beside the jet manifold, as on the Standard HT: the
    /// stoker engine's, a duplex gauge for the two front jets (red hand left,
    /// black hand right), and the fine-coal jet's; the back jets had none.
    /// Each is the stock S282's boiler pressure gauge at half size with its
    /// printed dial swapped for a blank face and our own bar scale, its needle
    /// driven from the stoker's last tick instead of a sim port.
    /// </summary>
    internal static class StokerGauges
    {
        private const string SourceGauge = "Swivelables/Pressuremeter";
        private const string SourceNeedle = "I_BoilerPressure";
        private const string SourceDial = "s282_label_pressuremeter";
        private const string SourceLabelText = "LocoS282A_Interior_Labels/Blower/Label_RM_1/Text";

        private const float NeedleUpdateThresholdBar = 0.005f;

        private const float Scale = 0.5f;
        private const float Column = 0.165f;
        private const float Depth = 0.035f;

        // Where the numbers sit, and the title, as shares of the dial's radius.
        private const float NumberRadius = 0.7f;
        private const float TitleDrop = 0.42f;

        private static readonly Color Face = new Color(0.9f, 0.87f, 0.78f);
        private static readonly Color Ink = new Color(0.1f, 0.09f, 0.08f);
        private static readonly Color RedHand = new Color(0.75f, 0.08f, 0.05f);
        private static readonly Color BlackHand = new Color(0.08f, 0.08f, 0.08f);

        private static Mesh disc;
        private static Material faceMaterial;

        internal enum Reading
        {
            Engine,
            LeftFront,
            RightFront,
            Fines,
        }

        private sealed class Spec
        {
            public Spec(float y, string title, float fullScale, float step, params (Reading Reading, Color? Tint)[] hands)
            {
                Y = y;
                Title = title;
                FullScale = fullScale;
                Step = step;
                Hands = hands;
            }

            public float Y { get; }
            public string Title { get; }
            public float FullScale { get; }
            public float Step { get; }
            public (Reading Reading, Color? Tint)[] Hands { get; }
        }

        private static readonly Spec[] Gauges =
        {
            new Spec(0.03f, "STOKER\nbar", 7f, 1f, (Reading.Engine, null)),
            new Spec(-0.08f, "FRONT JETS\nbar", 5f, 1f, (Reading.LeftFront, RedHand), (Reading.RightFront, BlackHand)),
            new Spec(-0.19f, "FINES\nbar", 5f, 1f, (Reading.Fines, null)),
        };

        /// <summary>
        /// Gauges built in an inactive manifold, waiting for their scales:
        /// TextMeshPro sets its own text box size when it wakes, so the scales
        /// are written once the manifold is active.
        /// </summary>
        internal sealed class Built
        {
            private readonly List<System.Action> scales = new List<System.Action>();

            public int Count { get; set; }

            public void Add(System.Action scale) => scales.Add(scale);

            /// <summary>Writes every gauge's scale and title; call once the manifold is active.</summary>
            public void WriteScales()
            {
                foreach (var scale in scales)
                    scale();
                scales.Clear();
            }
        }

        /// <summary>Builds the gauges in the manifold's frame, which must still be inactive.</summary>
        public static Built Build(Transform frame, Transform cabPrefab, TrainCar car)
        {
            var built = new Built();
            if (!Sources(cabPrefab, out var source, out var font))
                return built;

            var driver = frame.gameObject.AddComponent<StokerGaugeDriver>();
            driver.Car = car;
            foreach (var spec in Gauges)
            {
                var needles = Clone(frame, source, font, built, new Vector3(Column, spec.Y, Depth), spec.Title, spec.FullScale, spec.Step,
                    spec.Hands.Select(h => h.Tint).ToArray());
                for (var i = 0; i < needles.Count; i++)
                    driver.Add(needles[i], spec.Hands[i].Reading);
            }
            return built;
        }

        /// <summary>
        /// One gauge at a point in a frame that must still be inactive, its
        /// hand and scale reading a pressure in bar gauge.
        /// </summary>
        public static Built BuildOne(Transform frame, Transform cabPrefab, Vector3 at, string title, float fullScaleBar, float stepBar, System.Func<float> readBar)
        {
            var built = new Built();
            if (!Sources(cabPrefab, out var source, out var font))
                return built;
            var needles = Clone(frame, source, font, built, at, title, fullScaleBar, stepBar, new Color?[] { null });
            var driver = needles[0].gameObject.AddComponent<FuncGaugeDriver>();
            driver.Needle = needles[0];
            driver.ReadBar = readBar;
            return built;
        }

        private static bool Sources(Transform cabPrefab, out Transform source, out TextMeshPro font)
        {
            source = cabPrefab.Find(SourceGauge);
            var labelText = cabPrefab.Find(SourceLabelText);
            font = labelText != null ? labelText.GetComponent<TextMeshPro>() : null;
            return source != null && source.Find(SourceNeedle) != null;
        }

        // The S282's pressure gauge at half size at a point in the frame, with
        // a needle per hand (tinted when given), its dial blanked and its scale
        // and title queued for writing.
        private static List<IndicatorGauge> Clone(Transform frame, Transform source, TextMeshPro font, Built built, Vector3 at,
            string title, float fullScale, float step, Color?[] tints)
        {
            var gauge = Object.Instantiate(source.gameObject, frame, false).transform;
            gauge.name = "gauge " + title.Split('\n')[0];
            gauge.localPosition = at;
            gauge.localRotation = Quaternion.identity;
            gauge.localScale = Vector3.one * Scale;

            var firstNeedle = gauge.Find(SourceNeedle);
            foreach (var reader in firstNeedle.GetComponents<MonoBehaviour>().Where(c => c.GetType().Name == "IndicatorPortReader").ToList())
                Object.DestroyImmediate(reader);
            var needles = new List<IndicatorGauge> { firstNeedle.GetComponent<IndicatorGauge>() };
            for (var i = 1; i < tints.Length; i++)
                needles.Add(Object.Instantiate(firstNeedle.gameObject, gauge, false).GetComponent<IndicatorGauge>());

            for (var i = 0; i < needles.Count; i++)
            {
                needles[i].minValue = 0f;
                needles[i].maxValue = fullScale;
                if (tints[i] is Color tint)
                    foreach (var renderer in needles[i].GetComponentsInChildren<Renderer>(true))
                        renderer.material.color = tint;
            }

            var spec = new Spec(at.y, title, fullScale, step);
            var dial = gauge.Find(SourceDial);
            var dialFilter = dial != null ? dial.GetComponent<MeshFilter>() : null;
            if (dialFilter != null && dialFilter.sharedMesh != null)
            {
                BlankFace(dial, dialFilter.sharedMesh.bounds);
                if (font != null)
                {
                    var needle = needles[0];
                    built.Add(() => WriteScale(gauge, dial, dialFilter.sharedMesh.bounds, needle, spec, font));
                }
            }
            built.Count++;
            return needles;
        }

        // Swaps the printed dial for a blank face just in front of it.
        private static void BlankFace(Transform dial, Bounds bounds)
        {
            dial.GetComponent<Renderer>().enabled = false;

            // The face lies just in front of where the printed dial was.
            var radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
            var face = new GameObject("face").transform;
            face.SetParent(dial, false);
            face.localPosition = bounds.center - new Vector3(0f, 0f, 0.0006f);
            face.localScale = new Vector3(radius, radius, 1f);
            face.gameObject.AddComponent<MeshFilter>().sharedMesh = Disc();
            face.gameObject.AddComponent<MeshRenderer>().sharedMaterial = FaceMaterial();
        }

        // Writes the scale where the needle actually points at each value, and
        // the title below the pivot.
        private static void WriteScale(Transform gauge, Transform dial, Bounds bounds, IndicatorGauge needle, Spec spec, TextMeshPro font)
        {
            if (gauge == null || needle == null)
                return;
            var radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
            var center = dial.TransformPoint(bounds.center - new Vector3(0f, 0f, 0.0012f));
            var worldRadius = dial.TransformVector(new Vector3(radius, 0f, 0f)).magnitude;
            var pivot = needle.needle != null ? needle.needle : needle.transform;
            for (var value = 0f; value <= spec.FullScale + 0.01f; value += spec.Step)
            {
                needle.Value = value;
                var direction = Vector3.ProjectOnPlane(NeedleTip(pivot) - pivot.position, dial.forward).normalized;
                Text(gauge, font, value.ToString("0"), center + direction * worldRadius * NumberRadius, dial, worldRadius * 0.34f, worldRadius * 0.16f);
            }
            needle.Value = 0f;
            Text(gauge, font, spec.Title, center - dial.up * worldRadius * TitleDrop, dial, worldRadius * 0.95f, worldRadius * 0.34f);
        }

        // The needle's tip: the far end of its mesh along its longest axis.
        private static Vector3 NeedleTip(Transform pivot)
        {
            var filter = pivot.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null)
                return pivot.position + pivot.up;
            var b = filter.sharedMesh.bounds;
            var axis = b.extents.x >= b.extents.y && b.extents.x >= b.extents.z ? 0 : b.extents.y >= b.extents.z ? 1 : 2;
            var tip = b.center;
            tip[axis] += (b.center[axis] < 0f ? -1f : 1f) * b.extents[axis];
            return filter.transform.TransformPoint(tip);
        }

        // Text measured once laid out and scaled to fit the given world size,
        // whatever TextMeshPro's own units are. The parent must be active.
        private static void Text(Transform parent, TextMeshPro font, string text, Vector3 position, Transform facing, float width, float height)
        {
            var go = new GameObject("text " + text.Replace('\n', ' '));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = font.font;
            tmp.fontSharedMaterial = font.fontSharedMaterial;
            tmp.text = text;
            tmp.color = Ink;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = false;
            tmp.fontSize = 10f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.ForceMeshUpdate();
            var size = tmp.textBounds.size;
            if (!(size.x > 0f) || !(size.y > 0f))
            {
                Object.Destroy(go);
                return;
            }
            var parentScale = parent.lossyScale.x;
            go.transform.localScale = Vector3.one * Mathf.Min(width / (size.x * parentScale), height / (size.y * parentScale));
            // TextMeshPro reads from its +z side's back; face the viewer, upright.
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing.forward, facing.up));
        }

        // A unit disc facing −z (toward the viewer): clockwise as seen from −z,
        // Unity's front face.
        private static Mesh Disc()
        {
            if (disc != null)
                return disc;
            const int sides = 32;
            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();
            for (var i = 0; i <= sides; i++)
            {
                var angle = 2f * Mathf.PI * i / sides;
                vertices.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
            }
            for (var i = 1; i <= sides; i++)
                triangles.AddRange(new[] { 0, i + 1, i });
            disc = new Mesh { name = "RealisticFiring disc" };
            disc.SetVertices(vertices);
            disc.SetNormals(Enumerable.Repeat(Vector3.back, vertices.Count).ToList());
            disc.SetTriangles(triangles, 0);
            disc.RecalculateBounds();
            return disc;
        }

        private static Material FaceMaterial()
        {
            if (faceMaterial != null)
                return faceMaterial;
            faceMaterial = new Material(Shader.Find("Standard")) { name = "RealisticFiring gauge face", color = Face };
            faceMaterial.SetFloat("_Glossiness", 0.2f);
            return faceMaterial;
        }

        /// <summary>Moves one gauge's hand to a reading, in bar gauge.</summary>
        internal sealed class FuncGaugeDriver : MonoBehaviour
        {
            public IndicatorGauge Needle { get; set; }

            public System.Func<float> ReadBar { get; set; }

            private void Update()
            {
                if (Needle == null || ReadBar == null)
                    return;
                var bar = ReadBar();
                if (Mathf.Abs(Needle.Value - bar) > NeedleUpdateThresholdBar)
                    Needle.Value = bar;
            }
        }

        /// <summary>Moves the gauges' hands to the stoker's last tick.</summary>
        internal sealed class StokerGaugeDriver : MonoBehaviour
        {
            private readonly List<(IndicatorGauge Needle, Reading Reading)> hands = new List<(IndicatorGauge, Reading)>();

            public TrainCar Car { get; set; }

            public void Add(IndicatorGauge needle, Reading reading) => hands.Add((needle, reading));

            private void Update()
            {
                if (!FireboxRegistry.TryGet(Car, out var firebox) || !(firebox.LastStoker is StokerFlow flow))
                    return;
                foreach (var (needle, reading) in hands)
                {
                    if (needle == null)
                        continue;
                    var bar = Read(flow, reading);
                    if (Mathf.Abs(needle.Value - bar) > NeedleUpdateThresholdBar)
                        needle.Value = bar;
                }
            }

            private static float Read(StokerFlow flow, Reading reading)
            {
                switch (reading)
                {
                    case Reading.Engine: return flow.EnginePressure;
                    case Reading.LeftFront: return flow.LeftFrontPressure;
                    case Reading.RightFront: return flow.RightFrontPressure;
                    case Reading.Fines: return flow.FinesPressure;
                    default: return 0f;
                }
            }
        }
    }
}
