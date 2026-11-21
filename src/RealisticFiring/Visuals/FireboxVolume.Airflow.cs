using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    internal sealed partial class FireboxVolume
    {
        // The airflow's smoke under this is not drawn (the shader's own
        // limit for a step with nothing in it); and the smoke's top, a share
        // of the box's height.
        private const float SmokeDrawn = 0.004f;

        // The airflow steps at a fixed rate, and the picture is drawn between
        // its last two steps: its time kept at any frame rate (a frame's step,
        // clamped to 50 ms, ran slow under load, its random pushes changed
        // with the frame rate, and at 144 fps it cost a step a frame). Behind
        // a hitch it catches up this many steps at most and lets the rest go.
        private const float FlowStep = 1f / 30f;
        private const int FlowCatchUp = 4;

        private FireboxAirflow airflow;
        private Texture2D flowA;
        private Texture2D flowAPrev;
        private Texture2D flowB;
        private float smokeCeiling = 1f;
        private Color[] flowPixelsB;
        private float flowDue;
        private Color[] flowBeforeA;
        private Color[] flowBeforeB;
        private Color[] flowNowA;
        private Color[] flowNowB;
        private Vector4 weightsBefore;
        private Vector4 weightsNow;
        private Vector4 flowWeights;
        private bool restartedA;
        private bool restartedB;
        private float[] columnBurn;
        private float[] columnHeat;
        private float[] columnSurface;
        private float[] columnSmoke;

        // The airflow over the box's cross-section, and its maps for the shader.
        private void InitAirflow()
        {
            airflow = new FireboxAirflow(boxSize.x, boxSize.y);
            Texture2D FlowTexture(string name) => new Texture2D(airflow.Across, airflow.Up, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            flowA = FlowTexture("RealisticFiring flow phases");
            flowAPrev = FlowTexture("RealisticFiring flow phases, a step before");
            flowB = FlowTexture("RealisticFiring flow bend");
            var cells = airflow.Across * airflow.Up;
            flowPixelsB = new Color[cells];
            flowBeforeA = new Color[cells];
            flowBeforeB = new Color[cells];
            flowNowA = new Color[cells];
            flowNowB = new Color[cells];
            ReadFlow();
            System.Array.Copy(flowNowA, flowBeforeA, flowNowA.Length);
            System.Array.Copy(flowNowB, flowBeforeB, flowNowB.Length);
            weightsBefore = weightsNow;
            columnBurn = new float[airflow.Across];
            columnHeat = new float[airflow.Across];
            columnSurface = new float[airflow.Across];
            columnSmoke = new float[airflow.Across];
            UploadFlow(false);
        }

        private void DisposeAirflow()
        {
            if (flowA != null)
                Destroy(flowA);
            if (flowAPrev != null)
                Destroy(flowAPrev);
            if (flowB != null)
                Destroy(flowB);
        }

        // Carries the flames on the airflow: steps it at its fixed rate, sends
        // the shader its maps after each step, and has it draw them between
        // the last two (_FlowLerp), so nothing goes up between the steps.
        private void Carry(float dt, float speed, float forcing)
        {
            flowDue += dt;
            var steps = 0;
            for (; flowDue >= FlowStep && steps < FlowCatchUp; steps++)
            {
                flowDue -= FlowStep;
                StepFlow(speed, forcing);
            }
            flowDue = Mathf.Min(flowDue, FlowStep);
            if (steps > 0)
                UploadFlow(steps == 1);
            var t = flowDue / FlowStep;
            // A phase that restarted in the last step is shown as it is now:
            // between its old and new coordinates is nowhere, and its weight
            // is nil there anyway.
            material.SetVector(FlowLerpId, new Vector4(restartedA ? 1f : t, restartedB ? 1f : t, t, 0f));
            flowWeights = Vector4.Lerp(weightsBefore, weightsNow, t);
        }

        // The airflow's maps after a step: where the gas was when each phase
        // began, now (_FlowA) and a step before (_FlowAPrev); where across it
        // was a moment ago and its smoke, now and a step before, together
        // (_FlowB). After one step the map that was now is the one before,
        // so the two swap and only the new one goes up.
        private void UploadFlow(bool oneStep)
        {
            (flowA, flowAPrev) = (flowAPrev, flowA);
            flowA.SetPixels(flowNowA);
            flowA.Apply(false);
            if (!oneStep)
            {
                flowAPrev.SetPixels(flowBeforeA);
                flowAPrev.Apply(false);
            }
            // The highest row holding any smoke to speak of, now or a step
            // before, and so the smoke's top (the shader reads between rows).
            var smokeRow = -1;
            for (var k = 0; k < flowPixelsB.Length; k++)
            {
                flowPixelsB[k] = new Color(flowNowB[k].r, flowNowB[k].g, flowBeforeB[k].r, flowBeforeB[k].g);
                if (flowNowB[k].g > SmokeDrawn || flowBeforeB[k].g > SmokeDrawn)
                    smokeRow = Mathf.Max(smokeRow, k / airflow.Across);
            }
            smokeCeiling = smokeRow < 0 ? 0f : Mathf.Clamp01((smokeRow + 1.5f) / airflow.Up);
            flowB.SetPixels(flowPixelsB);
            flowB.Apply(false);
            material.SetTexture(FlowAId, flowA);
            material.SetTexture(FlowAPrevId, flowAPrev);
            material.SetTexture(FlowBId, flowB);
        }

        // Steps the airflow from each column across the box: how hard its coal
        // burns (its zones' flames, across the bed's rows), how hot its gas
        // leaves (the more its fresh coal's gas burns, the hotter), and its
        // coal's surface.
        private void StepFlow(float speed, float forcing)
        {
            for (var i = 0; i < airflow.Across; i++)
            {
                var x = (i + 0.5f) / airflow.Across;
                var burn = 0f;
                var hot = 0f;
                var surface = 0f;
                var smoke = 0f;
                var inBed = 0;
                for (var row = 0; row < 3; row++)
                {
                    var z = (row + 0.5f) / 3f;
                    var bs = bedS.x * x + bedS.y * z + bedS.z;
                    var bt = bedT.x * x + bedT.y * z + bedT.z;
                    if (bs < 0f || bs > 1f || bt < 0f || bt > 1f)
                        continue;
                    var a = ZoneAt(pixelsA, bs, bt);
                    var b = ZoneAt(pixelsB, bs, bt);
                    burn += a.r;
                    hot += a.r * b.r * (0.6f + 0.4f * b.b);
                    surface += a.a;
                    smoke += a.b;
                    inBed++;
                }
                columnBurn[i] = inBed > 0 ? burn / inBed : 0f;
                columnHeat[i] = inBed > 0 ? hot / inBed : 0f;
                columnSurface[i] = (inBed > 0 ? surface / inBed : grateLevel) * boxSize.y;
                columnSmoke[i] = inBed > 0 ? smoke / inBed : 0f;
            }
            airflow.EddySpin = Mathf.Lerp(EddySpinDrifting, EddySpinForced, forcing);
            airflow.EddyRate = EddyRate;
            airflow.EddyGrowthTime = Mathf.Max(0f, EddyGrowthTime);
            airflow.EddyRadius = EddyRadius;
            airflow.EddyLow = EddyLow;
            airflow.EddyHigh = EddyHigh;
            airflow.Period = FlowPeriod;
            System.Array.Copy(flowNowA, flowBeforeA, flowNowA.Length);
            System.Array.Copy(flowNowB, flowBeforeB, flowNowB.Length);
            weightsBefore = weightsNow;
            var restartsA = airflow.RestartsA;
            var restartsB = airflow.RestartsB;
            airflow.Step(FlowStep, columnBurn, columnHeat, columnSurface, speed, columnSmoke);
            restartedA = airflow.RestartsA != restartsA;
            restartedB = airflow.RestartsB != restartsB;
            ReadFlow();
        }

        // The airflow's maps as they are now: where the gas was at each
        // phase's start, m; where across it was a moment ago, m.
        private void ReadFlow()
        {
            for (var j = 0; j < airflow.Up; j++)
            {
                for (var i = 0; i < airflow.Across; i++)
                {
                    var k = j * airflow.Across + i;
                    flowNowA[k] = new Color(airflow.PhaseAAcross(i, j), airflow.PhaseAUp(i, j), airflow.PhaseBAcross(i, j), airflow.PhaseBUp(i, j));
                    flowNowB[k] = new Color(airflow.CameFrom(i, j), airflow.Smoke(i, j), 0f, 0f);
                }
            }
            weightsNow = new Vector4(airflow.WeightA, airflow.WeightB, 0f, 0f);
        }

        // A zone texture's value at the bed's (across, along), bilinear between
        // the zones' middles as the shader reads it.
        private static Color ZoneAt(Color[] pixels, float across, float along)
        {
            var fx = Mathf.Clamp(across * GrateLayout.Columns - 0.5f, 0f, GrateLayout.Columns - 1f);
            var fy = Mathf.Clamp(along * GrateLayout.Rows - 0.5f, 0f, GrateLayout.Rows - 1f);
            var x0 = Mathf.Min((int)fx, GrateLayout.Columns - 2);
            var y0 = Mathf.Min((int)fy, GrateLayout.Rows - 2);
            var tx = fx - x0;
            var ty = fy - y0;
            Color P(int c, int r) => pixels[r * GrateLayout.Columns + c];
            return Color.Lerp(Color.Lerp(P(x0, y0), P(x0 + 1, y0), tx), Color.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), tx), ty);
        }
    }
}
