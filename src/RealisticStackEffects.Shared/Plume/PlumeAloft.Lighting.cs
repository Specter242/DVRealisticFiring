using UnityEngine;

namespace RealisticStackEffects
{
    internal sealed partial class PlumeAloft
    {
        /// <summary>
        /// The plume's middle at each age it was last found at, youngest
        /// first, into <paramref name="into"/> from <paramref name="from"/>
        /// on, in its particles' own space; how many. For what is drawn
        /// with the plume to be put before or after it (SteamVents).
        /// </summary>
        public int Middles(Vector3[] into, int from)
        {
            var n = 0;
            for (var i = 0; i < ShapeAges && from + n < into.Length; i++)
                if (middleKnown[i])
                    into[from + n++] = middle[i];
            return n;
        }

        /// <summary>
        /// How far through the flipbook a particle <paramref name="age"/> s
        /// old is, 0 to 1: its age over the system's own lifetime, as the
        /// flipbook plays (then holds its last frame).
        /// </summary>
        public float FlipbookAt(float age) => nominalLife > 0f ? Mathf.Clamp01(age / nominalLife) : 1f;

        /// <summary>
        /// Which of the wisp flipbook's eight wisps (its rows) a particle is, 0
        /// to 7: each its own, as the plume's flipbook picked a row at random.
        /// </summary>
        public static float WispRow(uint seed) => Mathf.Min(7f, Mathf.Floor(Hash(seed ^ 0x3c6ef372u) * 8f));

        /// <summary>Where a particle's wisp starts in its row, 0 to 1, apart from its row and turn.</summary>
        public static float WispPhase(uint seed) => Hash(seed ^ 0xa54ff53au);

        /// <summary>
        /// A particle's wisp's own turn, radians: any way at birth, spinning
        /// up to <paramref name="spin"/> rad/s either way as 1.13's particles
        /// did, apart from the rolling puffs' near-upright pose; slowing as
        /// the wisp grows, as an eddy's turn slows with its size, its rate
        /// over 1 + its age over <paramref name="ease"/> s (0: steady).
        /// </summary>
        public static float WispTurn(uint seed, float age, float spin, float ease)
        {
            var turned = ease > 0f ? ease * Mathf.Log(1f + age / ease) : age;
            return Hash(seed ^ 0x9e3779b9u) * 2f * Mathf.PI + (2f * Hash(seed ^ 0x7f4a7c15u) - 1f) * spin * turned;
        }

        /// <summary>
        /// A rolling particle's first custom data (see ChimneyPlume.Carry):
        /// its wisp, its turn at <paramref name="age"/> s (WispTurn), how
        /// <paramref name="hard"/> its beat was, and how far through the
        /// flipbook it is.
        /// </summary>
        public Vector4 WispOf(Record record, float age, float spin, float ease, float hard)
        {
            var turned = ease > 0f ? ease * Mathf.Log(1f + age / ease) : age;
            return new Vector4(record.Wisp, record.TurnFrom + record.TurnWay * spin * turned, hard, FlipbookAt(Mathf.Max(0f, age)));
        }

        /// <summary>How hard the beat was that <paramref name="record"/>'s particle left on: <paramref name="now"/>'s, the first time it is asked.</summary>
        public float HardOf(Record record, float now)
        {
            if (float.IsNaN(record.Hard))
                record.Hard = now;
            return record.Hard;
        }

        /// <summary>
        /// A particle's second custom data for the smoke shader: how far it
        /// has rolled up into billows, 0 to 1 (x), how much of the sun the
        /// plume between keeps from it, 0 to 1 (y), and as rolling steam how
        /// far its puff has rolled, in loops (z), and which puff it is, plus
        /// a half when mirrored (w). As rolling steam x is instead the
        /// plume's opacity where it is, before its share with the particles
        /// overlapping it: a dense plume's particles are each faint, and by
        /// their own opacity dense black smoke turned to wisps; and y how far
        /// it is on the plume's silhouette as seen, 0 to 1 (its way out from
        /// the plume's middle across the view, × how far out it is): there
        /// its thin edge frays into strands, which over the plume's faces
        /// turned toward the view veiled its lobes.
        /// </summary>
        public Vector4 ExtraOf(Record record, float age, Vector3 position)
        {
            age = Mathf.Max(0f, age);
            var onset = air.BillowAge > 0f ? Mathf.SmoothStep(0f, 1f, age / air.BillowAge) : 1f;
            var roll = 0f;
            var puff = 0f;
            if (air.Puffs > 0)
            {
                // Each its own puff, mirrored or not, from its own place in
                // the loop; fast off the jet, slowing as its eddies spend.
                puff = Mathf.Min(air.Puffs - 1, Mathf.Floor(record.Puff * air.Puffs)) + (record.Mirrored ? 0.5f : 0f);
                // Each at its own pace, so puffs of an age do not turn together.
                roll = record.RollFrom + Rolled(age, air.RollRate, air.RollSlows) * record.RollPace;
            }
            var shade = record.ShadeRound == shadeRound ? record.Shade : 0f;
            if (air.Puffs > 0)
            {
                onset = record.Here;
                shade = 0f;
                var shape = record.Shape;
                if (air.HasEye && shape.w > 0f)
                {
                    var toEye = (air.Eye - position).normalized;
                    var facing = Vector3.Dot(new Vector3(shape.x, shape.y, shape.z), toEye);
                    shade = shape.w * Mathf.Sqrt(Mathf.Max(0f, 1f - facing * facing));
                }
            }
            return new Vector4(onset, shade, roll, puff);
        }

        /// <summary>
        /// How many loops a puff has rolled by <paramref name="age"/> s,
        /// rolling <paramref name="rate"/> loops a second off the jet and
        /// half that by <paramref name="slows"/> s: the turbulence the jet
        /// leaves dies away as the puff mixes into the air.
        /// </summary>
        public static float Rolled(float age, float rate, float slows) =>
            slows > 0f ? rate * slows * Mathf.Log(1f + Mathf.Max(0f, age) / slows) : rate * Mathf.Max(0f, age);

        // Records a particle among those of its age, and its part of the
        // plume's surface from their middle as the last full turn found it.
        private void Shape(Record record, Vector3 position, Vector3 velocity, float age, float width, float since)
        {
            var at = Mathf.Min(ShapeAges - 1, (int)(age / ShapeAgeStep));
            // Its way out from the plume's middle, × how far out.
            var seen = Vector3.zero;
            var measured = false;
            if (air.ShapeAcross)
            {
                int below = -1, above = -1;
                for (var k = Mathf.Min(at + 1, ShapeAges - 1); k >= Mathf.Max(0, at - MiddleReach); k--)
                    if (middleKnown[k] && middleAge[k] <= age) { below = k; break; }
                for (var k = Mathf.Max(0, at - 1); k <= Mathf.Min(ShapeAges - 1, at + MiddleReach); k++)
                    if (middleKnown[k] && middleAge[k] > age) { above = k; break; }
                if (below >= 0 || above >= 0)
                {
                    Vector3 centre, along;
                    if (below >= 0 && above >= 0)
                    {
                        var t = (age - middleAge[below]) / Mathf.Max(1e-3f, middleAge[above] - middleAge[below]);
                        centre = Vector3.Lerp(middle[below], middle[above], t);
                        along = middle[above] - middle[below];
                    }
                    else
                    {
                        centre = middle[below >= 0 ? below : above];
                        along = velocity;
                    }
                    var off = position - centre;
                    if (along.sqrMagnitude > 1e-6f)
                    {
                        along.Normalize();
                        off -= along * Vector3.Dot(off, along);
                    }
                    var outward = Mathf.Clamp01(off.magnitude / Mathf.Max(0.5f, 0.5f * width));
                    if (outward > 1e-3f)
                        seen = off.normalized * outward;
                    measured = true;
                }
            }
            else if (middleKnown[at])
            {
                var off = position - middle[at];
                var outward = Mathf.Clamp01(off.magnitude / Mathf.Max(0.5f, 0.5f * width));
                if (outward > 1e-3f)
                    seen = off.normalized * outward;
                measured = true;
            }
            // Eased to it, kept while there is nothing to measure it from.
            if (air.ShapeSmoothing > 0f && record.Shaped)
            {
                var before = record.Shape;
                var was = new Vector3(before.x, before.y, before.z) * before.w;
                seen = measured
                    ? Vector3.Lerp(was, seen, 1f - Mathf.Exp(-since / air.ShapeSmoothing))
                    : was;
            }
            var far = seen.magnitude;
            record.Shape = far > 1e-3f ? new Vector4(seen.x / far, seen.y / far, seen.z / far, Mathf.Clamp01(far)) : Vector4.zero;
            record.Shaped = true;
        }

        // Each particle's shade from the plume between it and the sun, from the turn's particles.
        private void Shadows()
        {
            using var timing = StackHost.TimePhase(PlumePhase.Shadows);
            var toSun = air.ToSun;
            if (casters.Count == 0 || air.ShadowDensity <= 0f || toSun.sqrMagnitude < 0.5f)
            {
                casters.Clear();
                shadeRound++;
                meanShade = shadedShare = 0f;
                return;
            }
            toSun.Normalize();
            var low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var high = -low;
            foreach (var caster in casters)
            {
                var reach = Vector3.one * (0.5f * caster.Size);
                low = Vector3.Min(low, caster.Position - reach);
                high = Vector3.Max(high, caster.Position + reach);
            }
            var extent = high - low;
            var cell = Mathf.Max(SmallestCell, Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)) / (ShadowCells - 2));
            // A cell's margin round the plume, so the spread smoke stays in.
            low -= Vector3.one * cell;
            var nx = Mathf.Min(ShadowCells, Mathf.CeilToInt(extent.x / cell) + 3);
            var ny = Mathf.Min(ShadowCells, Mathf.CeilToInt(extent.y / cell) + 3);
            var nz = Mathf.Min(ShadowCells, Mathf.CeilToInt(extent.z / cell) + 3);
            var cells = nx * ny * nz;
            if (grid.Length < cells)
                grid = new float[cells];
            else
                System.Array.Clear(grid, 0, cells);

            // Each particle's smoke, spread over the eight cells round it.
            var perVolume = 1f / (cell * cell * cell);
            foreach (var caster in casters)
            {
                var depth = -Mathf.Log(1f - Mathf.Clamp(caster.Opacity, 0f, 0.95f));
                var smoke = depth * (Mathf.PI / 6f) * caster.Size * caster.Size * perVolume;
                Splat((caster.Position - low) / cell, smoke, nx, ny, nz);
            }

            // Each particle's way to the sun through it, a cell a step: the light left is its sun.
            var total = 0f;
            var shaded = 0;
            // A new round: a particle not among this turn's casters has no shade.
            var before = shadeRound++;
            foreach (var caster in casters)
            {
                var from = Mathf.Max(MarchFrom, 0.35f * caster.Size / cell);
                var at = (caster.Position - low) / cell + toSun * from;
                var depth = 0f;
                for (var k = 0; k < 2 * ShadowCells; k++)
                {
                    if (at.x < 0f || at.y < 0f || at.z < 0f || at.x > nx - 1 || at.y > ny - 1 || at.z > nz - 1)
                        break;
                    depth += Sample(at, nx, ny, nz);
                    at += toSun;
                }
                var shade = 1f - Mathf.Exp(-depth * cell * air.ShadowDensity);
                if (caster.Record.ShadeRound == before)
                    shade = caster.Record.Shade + (shade - caster.Record.Shade) * ShadeEasing;
                caster.Record.Shade = shade;
                caster.Record.ShadeRound = shadeRound;
                total += shade;
                if (shade > 0.5f)
                    shaded++;
            }
            meanShade = total / casters.Count;
            shadedShare = (float)shaded / casters.Count;
            casters.Clear();
        }

        private void Splat(Vector3 at, float amount, int nx, int ny, int nz)
        {
            var x0 = Mathf.Clamp(Mathf.FloorToInt(at.x), 0, nx - 2);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(at.y), 0, ny - 2);
            var z0 = Mathf.Clamp(Mathf.FloorToInt(at.z), 0, nz - 2);
            var fx = Mathf.Clamp01(at.x - x0);
            var fy = Mathf.Clamp01(at.y - y0);
            var fz = Mathf.Clamp01(at.z - z0);
            for (var dz = 0; dz < 2; dz++)
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var weight = (dx == 0 ? 1f - fx : fx) * (dy == 0 ? 1f - fy : fy) * (dz == 0 ? 1f - fz : fz);
                grid[((z0 + dz) * ny + y0 + dy) * nx + x0 + dx] += amount * weight;
            }
        }

        private float Sample(Vector3 at, int nx, int ny, int nz)
        {
            var x0 = Mathf.Min(Mathf.FloorToInt(at.x), nx - 2);
            var y0 = Mathf.Min(Mathf.FloorToInt(at.y), ny - 2);
            var z0 = Mathf.Min(Mathf.FloorToInt(at.z), nz - 2);
            var fx = at.x - x0;
            var fy = at.y - y0;
            var fz = at.z - z0;
            var row = (z0 * ny + y0) * nx + x0;
            var up = nx;
            var deep = nx * ny;
            var a = grid[row] + (grid[row + 1] - grid[row]) * fx;
            var b = grid[row + up] + (grid[row + up + 1] - grid[row + up]) * fx;
            var c = grid[row + deep] + (grid[row + deep + 1] - grid[row + deep]) * fx;
            var d = grid[row + deep + up] + (grid[row + deep + up + 1] - grid[row + deep + up]) * fx;
            var near = a + (b - a) * fy;
            var far = c + (d - c) * fy;
            return near + (far - near) * fz;
        }

        // A curve over a particle's life squeezed into the first share of it,
        // then held at its end.
        private static ParticleSystem.MinMaxCurve Compress(ParticleSystem.MinMaxCurve curve, float share)
        {
            share = Mathf.Clamp(share, 0.001f, 1f);
            AnimationCurve Squeeze(AnimationCurve c)
            {
                if (c == null || c.length == 0)
                    return c;
                var keys = new Keyframe[c.length + 1];
                for (var i = 0; i < c.length; i++)
                {
                    var k = c[i];
                    keys[i] = new Keyframe(k.time * share, k.value, k.inTangent / share, k.outTangent / share);
                }
                var end = c.Evaluate(1f);
                keys[c.length] = new Keyframe(1f, end, 0f, 0f);
                keys[c.length - 1].outTangent = 0f;
                return new AnimationCurve(keys);
            }
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Curve:
                    return new ParticleSystem.MinMaxCurve(curve.curveMultiplier, Squeeze(curve.curveMax));
                case ParticleSystemCurveMode.TwoCurves:
                    return new ParticleSystem.MinMaxCurve(curve.curveMultiplier, Squeeze(curve.curveMin), Squeeze(curve.curveMax));
                default:
                    return curve;
            }
        }

        private static float MeanLifetime(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return 0.5f * (curve.constantMin + curve.constantMax);
                default:
                    return curve.Evaluate(0.5f);
            }
        }
    }
}
