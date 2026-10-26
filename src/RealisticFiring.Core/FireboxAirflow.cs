using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
 public sealed class FireboxAirflow
    {
        public int Across { get; }
        public int Up { get; }
        public float Width { get; }
        public float Height { get; }

        /// <summary>Upward pull on hot gas, m/s² at full heat.</summary>
        public float Buoyancy { get; set; } = 3f;

        /// <summary>The gas's heat fades over this long, s.</summary>
        public float Cooling { get; set; } = 0.6f;

        /// <summary>Vorticity confinement's strength.</summary>
        public float Confinement { get; set; } = 6f;

        /// <summary>The random pushes over the coal, m/s² at full burn, at the coal.</summary>
        public float Kicks { get; set; } = 25f;

 public float KickDepth { get; set; } = 0.15f;

        /// <summary>
        /// New eddies a second, each a mass of gas set turning near a side
        /// wall, up the wall and in over the top, just above the coal; the
        /// flow carries it up and the confinement keeps it turning.
        /// </summary>
        public float EddyRate { get; set; } = 1.5f;

 public float EddyLow { get; set; } = 0.05f;
        public float EddyHigh { get; set; } = 0.3f;

        /// <summary>A new eddy's spin at its core, rad/s (set from the draft).</summary>
        public float EddySpin { get; set; } = 4f;

        /// <summary>A new eddy's core, m.</summary>
        public float EddyRadius { get; set; } = 0.2f;

        /// <summary>Seconds over which a new eddy gains its full impulse.</summary>
        public float EddyGrowthTime { get; set; } = 0.3f;

        /// <summary>The tube bank's opening in the crown, a share of the width about its middle.</summary>
        public float Outlet { get; set; } = 0.6f;

        /// <summary>
        /// Hot smoke's own rise through still gas, m/s: a standing fire's
        /// smoke fills the box though its gas hardly moves (the footage:
        /// standing after firing, the box murky with it). The coal's smoke is
        /// diluted in the gas leaving it, the more the faster that goes past
        /// this (working, it streams out thin).
        /// </summary>
        public float SmokeRise { get; set; } = 0.25f;

        /// <summary>
        /// How fully the gas leaving the coal dilutes its smoke, as the power
        /// of its speed against <see cref="SmokeRise"/>: 1 in proportion, less
        /// as a shovelful's thick smoke-out mixes into it only partly (in
        /// proportion, a working fire's fresh smoke drew as nothing; the
        /// footage's: pale wisps streaming to the tubes).
        /// </summary>
        public float SmokeDilution { get; set; } = 0.5f;

        /// <summary>The smoke in the box thins over this long, s, as it mixes and leaks out.</summary>
        public float SmokeClearing { get; set; } = 12f;

        /// <summary>Smoke at the tube bank's opening leaves over this long, s.</summary>
        public float SmokeLeaving { get; set; } = 0.3f;

        /// <summary>Each phase of the material coordinates lasts this long, s.</summary>
        public float Period { get; set; } = 1.4f;

        /// <summary>How far back the bending of the flames' bodies looks, s.</summary>
        public float Bend { get; set; } = 0.35f;

        // The pressure's solve: Gauss-Seidel sweeps, over-relaxed.
        private const int PressureIterations = 16;
        private const float Relaxation = 1.7f;

        // A neighbor's pressure in the solve: a cell's own (beyond a wall, the
        // coal, or the closed crown), or nil (over the tube bank's opening).
        private const int Same = -1;
        private const int Nil = -2;

        private readonly float hx;
        private readonly float hy;
        private readonly float[] u;
        private readonly float[] v;
        private readonly float[] heat;
        private readonly float[] u0;
        private readonly float[] v0;
        private readonly float[] heat0;
        private readonly float[] smoke;
        private readonly float[] smoke0;
        private readonly float[] smokeIn;
        private readonly float[] pressure;
        private readonly int[] left;
        private readonly int[] right;
        private readonly int[] down;
        private readonly int[] upper;
        private readonly float[] kick;
        private readonly float[] divergence;
        private readonly float[] curl;
        private readonly bool[] bed;
        private readonly float[] inflow;
        private readonly float[] source;
        private readonly float[] coal;
        private readonly float[] ax;
        private readonly float[] ay;
        private readonly float[] bx;
        private readonly float[] by;
        private readonly float[] scratch;
        private readonly float[] sideways;
        private readonly Random random = new Random(588030);
        private float phase;
        private float eddyDue;
        private float ageA;
        private float ageB;
        private readonly List<FormingEddy> formingEddies = new List<FormingEddy>();

        private struct FormingEddy
        {
            public float X, Y, Spin, Age, Duration;
        }

        public FireboxAirflow(float width, float height, int across = 16)
        {
            Width = Math.Max(0.1f, width);
            Height = Math.Max(0.1f, height);
            Across = Math.Max(4, across);
            Up = Math.Max(4, (int)Math.Round(Across * Height / Width));
            hx = Width / Across;
            hy = Height / Up;
            var n = Across * Up;
            u = new float[n];
            v = new float[n];
            heat = new float[n];
            u0 = new float[n];
            v0 = new float[n];
            heat0 = new float[n];
            smoke = new float[n];
            smoke0 = new float[n];
            smokeIn = new float[Across];
            pressure = new float[n];
            left = new int[n];
            right = new int[n];
            down = new int[n];
            upper = new int[n];
            kick = new float[Across];
            divergence = new float[n];
            curl = new float[n];
            bed = new bool[n];
            inflow = new float[Across];
            source = new float[Across];
            coal = new float[Across];
            ax = new float[n];
            ay = new float[n];
            bx = new float[n];
            by = new float[n];
            scratch = new float[n];
            sideways = new float[n];
            ResetCoordinates(ax, ay);
            ResetCoordinates(bx, by);
            phase = 0f;
            ageA = 0f;
            ageB = 0.5f * Period;
        }

        /// <summary>The gas's velocity across and up at a cell, m/s.</summary>
        public float VelocityAcross(int i, int j) => u[Index(i, j)];
        public float VelocityUp(int i, int j) => v[Index(i, j)];

        /// <summary>The gas's heat at a cell, 0 to 1.</summary>
        public float Heat(int i, int j) => heat[Index(i, j)];

        /// <summary>The smoke in the gas at a cell, against the coal's under a standing fire.</summary>
        public float Smoke(int i, int j) => smoke[Index(i, j)];

        /// <summary>Where the gas now at a cell was when phase A began, m.</summary>
        public float PhaseAAcross(int i, int j) => ax[Index(i, j)];
        public float PhaseAUp(int i, int j) => ay[Index(i, j)];

        /// <summary>Where the gas now at a cell was when phase B began, m.</summary>
        public float PhaseBAcross(int i, int j) => bx[Index(i, j)];
        public float PhaseBUp(int i, int j) => by[Index(i, j)];

        /// <summary>Where across the gas now at a cell was <see cref="Bend"/> ago, m.</summary>
        public float CameFrom(int i, int j) => sideways[Index(i, j)];

        /// <summary>How many times each phase has restarted, for telling a restart from motion.</summary>
        public int RestartsA { get; private set; }
        public int RestartsB { get; private set; }

        /// <summary>How many eddies have begun forming, for tracing the flow's motion.</summary>
        public int EddiesStarted { get; private set; }

        /// <summary>The phases' weights, adding to 1, easing to rest at both ends of each fade.</summary>
        public float WeightA => Ease(1f - Math.Abs(2f * phase - 1f));
        public float WeightB => 1f - WeightA;

        /// <summary>
        /// Steps the flow. Per column across: <paramref name="burn"/> how hard
        /// the coal burns there, 0 to 1 (its air); <paramref name="sourceHeat"/>
        /// the heat of the gas leaving it, 0 to 1; <paramref name="surface"/>
        /// the coal's surface, m up; <paramref name="smokeSource"/>, if given,
        /// the smoke the coal gives off. <paramref name="speed"/> is the primary
        /// air's speed through coal burning fully, m/s, beats included.
        /// </summary>
        public void Step(float dt, float[] burn, float[] sourceHeat, float[] surface, float speed, float[] smokeSource = null)
        {
            if (!(dt > 0f))
                return;
            dt = Math.Min(dt, 0.05f);
            for (var i = 0; i < Across; i++)
            {
                var b = Clamp01(burn[i]);
                inflow[i] = Math.Max(0f, speed) * (0.3f + 0.7f * b);
                source[i] = Clamp01(sourceHeat[i]) * b;
                coal[i] = Math.Max(0f, surface[i]);
                // Diluted in the gas leaving the coal: as thick as given where
                // it only rises on its own, thinner the faster the gas goes.
                smokeIn[i] = smokeSource != null ? Math.Max(0f, smokeSource[i]) * (float)Math.Pow(SmokeRise / (inflow[i] + SmokeRise), SmokeDilution) : 0f;
                for (var j = 0; j < Up; j++)
                    bed[Index(i, j)] = (j + 0.5f) * hy < surface[i];
            }

            ApplyBed();
            AddForces(dt, burn);
            Array.Copy(u, u0, u.Length);
            Array.Copy(v, v0, v.Length);
            Array.Copy(heat, heat0, heat.Length);
            Array.Copy(smoke, smoke0, smoke.Length);
            Advect(u0, u, dt);
            Advect(v0, v, dt);
            Advect(heat0, heat, dt);
            Advect(smoke0, smoke, dt, SmokeRise);
            var fade = (float)Math.Exp(-dt / Math.Max(0.01f, Cooling));
            for (var k = 0; k < heat.Length; k++)
                heat[k] *= fade;
            // The smoke thins as it mixes and leaks, and leaves at the tube
            // bank's opening.
            var clearing = (float)Math.Exp(-dt / Math.Max(0.1f, SmokeClearing));
            var leaving = (float)Math.Exp(-dt / Math.Max(0.01f, SmokeLeaving));
            for (var j = 0; j < Up; j++)
                for (var i = 0; i < Across; i++)
                    smoke[Index(i, j)] *= j == Up - 1 && InOutlet(i) ? clearing * leaving : clearing;
            ApplyBed();
            Project();
            ApplyBed();
            CarryCoordinates(dt);
            TraceSideways();
        }

        // The coal: its air up through it at its own speed and heat; across,
        // the gas slides freely over its surface (held still there, gas just
        // leaving the coal was traced back to none, so nothing turned near it).
        private void ApplyBed()
        {
            for (var i = 0; i < Across; i++)
            {
                var top = 0;
                while (top < Up && bed[Index(i, top)])
                    top++;
                var slide = top < Up ? u[Index(i, top)] : 0f;
                for (var j = 0; j < top; j++)
                {
                    var k = Index(i, j);
                    u[k] = slide;
                    v[k] = inflow[i];
                    heat[k] = source[i];
                    smoke[k] = smokeIn[i];
                }
            }
        }

        private void AddForces(float dt, float[] burn)
        {
            // Hot gas rises.
            for (var k = 0; k < v.Length; k++)
                if (!bed[k])
                    v[k] += Buoyancy * heat[k] * dt;

            // Vorticity confinement: puts back the swirl the coarse grid smears.
            for (var j = 0; j < Up; j++)
                for (var i = 0; i < Across; i++)
                    curl[Index(i, j)] = (V(i + 1, j) - V(i - 1, j)) / (2f * hx) - (U(i, j + 1) - U(i, j - 1)) / (2f * hy);
            var h = Math.Min(hx, hy);
            for (var j = 1; j < Up - 1; j++)
            {
                for (var i = 1; i < Across - 1; i++)
                {
                    var k = Index(i, j);
                    if (bed[k])
                        continue;
                    var gx = (Math.Abs(curl[Index(i + 1, j)]) - Math.Abs(curl[Index(i - 1, j)])) / (2f * hx);
                    var gy = (Math.Abs(curl[Index(i, j + 1)]) - Math.Abs(curl[Index(i, j - 1)])) / (2f * hy);
                    var length = (float)Math.Sqrt(gx * gx + gy * gy) + 1e-5f;
                    gx /= length;
                    gy /= length;
                    var w = curl[k];
                    u[k] += Confinement * h * gy * w * dt;
                    v[k] -= Confinement * h * gx * w * dt;
                }
            }

            // A new eddy now and then, near a side wall just above the coal:
            // turning up the wall and in over the top (clockwise at the left,
            // seen from the door).
            eddyDue += EddyRate * dt;
            while (eddyDue >= 1f)
            {
                eddyDue -= 1f;
                var leftWall = random.NextDouble() < 0.5;
                var cx = (float)(Width * (leftWall ? 0.12 + 0.2 * random.NextDouble() : 0.68 + 0.2 * random.NextDouble()));
                var below = coal[Math.Min(Across - 1, (int)(cx / hx))];
                var cy = below + EddyLow + (EddyHigh - EddyLow) * (float)random.NextDouble();
                var spin = EddySpin * (float)(0.6 + 0.8 * random.NextDouble()) * (leftWall ? -1f : 1f);
                EddiesStarted++;
                if (EddyGrowthTime <= 0f)
                    Stir(cx, cy, spin);
                else
                    formingEddies.Add(new FormingEddy { X = cx, Y = cy, Spin = spin, Duration = EddyGrowthTime });
            }
            // The same total impulse, eased in over a short formation time.
            // An instant vortex also jumped the flame's traced origin sideways,
            // so even a standing fire twitched with each new eddy.
            for (var i = formingEddies.Count - 1; i >= 0; i--)
            {
                var eddy = formingEddies[i];
                var before = Clamp01(eddy.Age / eddy.Duration);
                eddy.Age += dt;
                var after = Clamp01(eddy.Age / eddy.Duration);
                Stir(eddy.X, eddy.Y, eddy.Spin * (Ease(after) - Ease(before)));
                if (after >= 1f)
                    formingEddies.RemoveAt(i);
                else
                    formingEddies[i] = eddy;
            }

            // Random pushes across, over the burning coal and fading up from
            // it, each column's wandering a little each step, set the gas
            // rolling as it leaves the coal.
            var wander = (float)Math.Exp(-dt / 0.15f);
            var depth = Math.Max(0.01f, KickDepth);
            for (var i = 0; i < Across; i++)
            {
                kick[i] = kick[i] * wander + (1f - wander) * (float)(random.NextDouble() * 2.0 - 1.0) * 3f;
                var b = Clamp01(burn[i]);
                if (b <= 0f)
                    continue;
                for (var j = 0; j < Up; j++)
                {
                    var k = Index(i, j);
                    var above = (j + 0.5f) * hy - coal[i];
                    if (bed[k] || above > 4f * depth)
                        continue;
                    u[k] += Kicks * b * kick[i] * (float)Math.Exp(-above / depth) * dt;
                }
            }
        }

        // Sets the gas about (cx, cy) turning at spin rad/s, positive
        // counterclockwise: a solid core, slower as one over the distance out
        // of it, fading out by three times it.
        private void Stir(float cx, float cy, float spin)
        {
            var core = Math.Max(0.02f, EddyRadius);
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    var k = Index(i, j);
                    if (bed[k])
                        continue;
                    var dx = (i + 0.5f) * hx - cx;
                    var dy = (j + 0.5f) * hy - cy;
                    var r = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (r > 3f * core)
                        continue;
                    var speed = r < core ? spin : spin * core / Math.Max(r, 1e-4f);
                    var fade = r < core ? 1f : 1f - (r - core) / (2f * core);
                    // Tangential: counterclockwise is (-dy, dx).
                    u[k] += -dy * speed * fade;
                    v[k] += dx * speed * fade;
                }
            }
        }

        // Carries a field with the gas, and rising this much faster, m/s.
        private void Advect(float[] from, float[] to, float dt, float rise = 0f)
        {
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    var k = Index(i, j);
                    var x = (i + 0.5f) * hx - u[k] * dt;
                    var y = (j + 0.5f) * hy - (v[k] + rise) * dt;
                    to[k] = Sample(from, x, y);
                }
            }
        }

        // Makes the flow incompressible: the side walls and the crown are
        // closed (no flow through them, the pressure's slope nil), the coal
        // gives its air at its own speed, and the tube bank's opening holds
        // the pressure at nil, so the gas leaves there.
        private void Project()
        {
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    var k = Index(i, j);
                    divergence[k] = bed[k] ? 0f
                        : (UFace(i + 1, j, k) - UFace(i - 1, j, k)) / (2f * hx) + (VFace(i, j + 1, k) - VFace(i, j - 1, k)) / (2f * hy);
                }
            }
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    var k = Index(i, j);
                    left[k] = Neighbor(i - 1, j);
                    right[k] = Neighbor(i + 1, j);
                    down[k] = Neighbor(i, j - 1);
                    upper[k] = Neighbor(i, j + 1);
                }
            }
            var ix2 = 1f / (hx * hx);
            var iy2 = 1f / (hy * hy);
            var diagonal = 2f * ix2 + 2f * iy2;
            for (var n = 0; n < PressureIterations; n++)
            {
                for (var k = 0; k < pressure.Length; k++)
                {
                    if (bed[k])
                        continue;
                    var p = pressure[k];
                    var sum = (Of(left[k], p) + Of(right[k], p)) * ix2 + (Of(down[k], p) + Of(upper[k], p)) * iy2;
                    pressure[k] = p + Relaxation * ((sum - divergence[k]) / diagonal - p);
                }
            }
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    var k = Index(i, j);
                    if (bed[k])
                        continue;
                    var p = pressure[k];
                    u[k] -= (P(i + 1, j, p) - P(i - 1, j, p)) / (2f * hx);
                    v[k] -= (P(i, j + 1, p) - P(i, j - 1, p)) / (2f * hy);
                }
            }
            // Nothing through the side walls.
            for (var j = 0; j < Up; j++)
            {
                u[Index(0, j)] = Math.Max(0f, u[Index(0, j)]) * 0.5f;
                u[Index(Across - 1, j)] = Math.Min(0f, u[Index(Across - 1, j)]) * 0.5f;
            }
        }

        private int Neighbor(int i, int j)
        {
            if (i < 0 || i >= Across || j < 0)
                return Same;
            if (j >= Up)
                return InOutlet(i) ? Nil : Same;
            var k = Index(i, j);
            return bed[k] ? Same : k;
        }

        private float Of(int neighbor, float here) => neighbor >= 0 ? pressure[neighbor] : neighbor == Nil ? 0f : here;

        // A neighbor's pressure: beyond a wall, the coal, or the closed crown
        // the same as here; above the tube bank's opening nil.
        private float P(int i, int j, float here)
        {
            if (i < 0 || i >= Across || j < 0)
                return here;
            if (j >= Up)
                return InOutlet(i) ? 0f : here;
            var k = Index(i, j);
            return bed[k] ? here : pressure[k];
        }

        // A neighbor's velocity across for the divergence: beyond a side wall
        // the mirror of here's, so none passes it.
        private float UFace(int i, int j, int here)
        {
            if (i < 0 || i >= Across)
                return -u[here];
            return u[Index(i, Math.Max(0, Math.Min(Up - 1, j)))];
        }

        // And up: under the grid the coal's air; over it the crown's mirror,
        // or the gas leaving through the tube bank's opening.
        private float VFace(int i, int j, int here)
        {
            if (j < 0)
                return inflow[i];
            if (j >= Up)
                return InOutlet(i) ? v[here] : -v[here];
            return v[Index(i, j)];
        }

        private bool InOutlet(int i)
        {
            var x = (i + 0.5f) / Across;
            return Math.Abs(x - 0.5f) <= 0.5f * Outlet;
        }

        private float U(int i, int j) => u[Index(Math.Max(0, Math.Min(Across - 1, i)), Math.Max(0, Math.Min(Up - 1, j)))];
        private float V(int i, int j) => v[Index(Math.Max(0, Math.Min(Across - 1, i)), Math.Max(0, Math.Min(Up - 1, j)))];

        // The material coordinates ride the gas; the coal's air brings in
        // gas from under the surface, as far under as it has come since the
        // phase began. Each phase restarts where its weight is nil.
        private void CarryCoordinates(float dt)
        {
            var before = phase;
            phase += dt / Math.Max(0.1f, Period);
            ageA += dt;
            ageB += dt;
            CarryOne(ax, ay, ageA, dt);
            CarryOne(bx, by, ageB, dt);
            if (phase >= 1f)
            {
                phase -= 1f;
                ResetCoordinates(ax, ay);
                ageA = 0f;
                RestartsA++;
            }
            if (before < 0.5f && phase >= 0.5f)
            {
                ResetCoordinates(bx, by);
                ageB = 0f;
                RestartsB++;
            }
        }

        private void CarryOne(float[] cx, float[] cy, float age, float dt)
        {
            Array.Copy(cx, scratch, cx.Length);
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    var k = Index(i, j);
                    cx[k] = bed[k] ? (i + 0.5f) * hx : Sample(scratch, (i + 0.5f) * hx - u[k] * dt, (j + 0.5f) * hy - v[k] * dt);
                }
            }
            Array.Copy(cy, scratch, cy.Length);
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    var k = Index(i, j);
                    cy[k] = bed[k] ? (j + 0.5f) * hy - inflow[i] * age : Sample(scratch, (i + 0.5f) * hx - u[k] * dt, (j + 0.5f) * hy - v[k] * dt);
                }
            }
        }

        private void ResetCoordinates(float[] cx, float[] cy)
        {
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    cx[Index(i, j)] = (i + 0.5f) * hx;
                    cy[Index(i, j)] = (j + 0.5f) * hy;
                }
            }
        }

        // Where across the gas at each cell was a moment ago, traced back
        // along the flow in a few steps.
        private void TraceSideways()
        {
            const int steps = 3;
            var step = Bend / steps;
            for (var j = 0; j < Up; j++)
            {
                for (var i = 0; i < Across; i++)
                {
                    var x = (i + 0.5f) * hx;
                    var y = (j + 0.5f) * hy;
                    for (var n = 0; n < steps; n++)
                    {
                        var du = Sample(u, x, y);
                        var dv = Sample(v, x, y);
                        x = Math.Max(0f, Math.Min(Width, x - du * step));
                        y = Math.Max(0f, Math.Min(Height, y - dv * step));
                    }
                    sideways[Index(i, j)] = x;
                }
            }
        }

        private float Sample(float[] field, float x, float y)
        {
            var fx = Math.Max(0f, Math.Min(Across - 1f, x / hx - 0.5f));
            var fy = Math.Max(0f, Math.Min(Up - 1f, y / hy - 0.5f));
            var i0 = Math.Min(Across - 2, (int)fx);
            var j0 = Math.Min(Up - 2, (int)fy);
            var tx = fx - i0;
            var ty = fy - j0;
            var a = field[Index(i0, j0)] + (field[Index(i0 + 1, j0)] - field[Index(i0, j0)]) * tx;
            var b = field[Index(i0, j0 + 1)] + (field[Index(i0 + 1, j0 + 1)] - field[Index(i0, j0 + 1)]) * tx;
            return a + (b - a) * ty;
        }

        private int Index(int i, int j) => j * Across + i;

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        private static float Ease(float x) => x * x * (3f - 2f * x);
    }
}
