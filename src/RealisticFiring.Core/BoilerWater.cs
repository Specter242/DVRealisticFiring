using System;

namespace RealisticFiring.Core
{
    /// <summary>How hard the feed water is: the dissolved solids it brings in.</summary>
    public enum FeedWaterQuality
    {
        Good,
        Average,
        Bad,
    }

    /// <summary>What the boiler water does this tick, from the outside.</summary>
    public struct BoilerWaterInputs
    {
        /// <summary>Water in the boiler, in kg.</summary>
        public float WaterMass;

        /// <summary>Water fed in this tick, in kg, less any steam the feed took from the boiler.</summary>
        public float Fed;

        /// <summary>Water blown down this tick, in kg.</summary>
        public float BlownDown;

        /// <summary>How hard the boiler is worked, 0 standing to 1 the throttle's full flow.</summary>
        public float Work;

        /// <summary>How full the boiler is, 0 at the crown sheet to 1 at the steam outlet.</summary>
        public float Fullness;
    }

 public sealed class BoilerWater
    {
        // Feed water's solids by quality, in ppm: good, average, bad (about 9,
        // 20, and 47 grains a gallon).
        private static readonly float[] FeedPpm = { 150f, 350f, 800f };

        /// <summary>Concentration at which the water starts to foam, in ppm (2,500 to 3,500 in practice).</summary>
        public const float FoamOnsetPpm = 2500f;

        /// <summary>Concentration at which it foams freely, in ppm (200 to 250 grains a gallon).</summary>
        public const float FoamFullPpm = 4500f;

        /// <summary>Share of the feed's solids that come out as sludge; the rest stay dissolved.</summary>
        public const float SludgeShare = 0.3f;

        // Sludge settles out with this time constant at a standstill, in s;
        // hard work stirs settled sludge up with this one, at full work.
        private const float SettleTime = 600f;
        private const float StirTime = 180f;

        // The blowdown cock sits at the mud ring, where sludge settles: a kg
        // blown takes this many times its share of the settled sludge while it
        // lies gathered there; blowing on draws it away (time constant, s), and
        // it gathers again while the cock is shut.
        private const float MudRingBias = 25f;
        private const float GatheredDrawTime = 3f;
        private const float GatherTime = 5f;

        // Foam comes on and settles over this time, in s. Standing water
        // foams this much of what working water does, and water carried over
        // half the glass foams up to this much more at the top.
        private const float FoamTime = 4f;
        private const float StandingFoam = 0.3f;
        private const float HighWaterFoam = 0.8f;

        /// <summary>Share of the steam that is water, with the water foaming freely.</summary>
        public const float MaxCarryover = 0.3f;

        // Foam lifts the water in the glass by up to this much of the boiler
        // (about 2 inches at the glass), and surges it up and down by this.
        private const float GlassSwell = 0.04f;
        private const float GlassSurge = 0.03f;

        private float gathered = 1f;
        private float clock;

        public BoilerWater(FeedWaterQuality quality, float waterMass)
        {
            Quality = quality;
            Washout(waterMass);
        }

        /// <summary>The feed water's quality, from now on.</summary>
        public FeedWaterQuality Quality { get; set; }

        /// <summary>The feed's solids, in ppm.</summary>
        public float FeedConcentration => FeedPpm[(int)Quality];

        /// <summary>Dissolved solids in the boiler, in kg.</summary>
        public float Solids { get; private set; }

        /// <summary>Sludge settled at the mud ring, in kg.</summary>
        public float SettledSludge { get; private set; }

        /// <summary>Sludge stirred up in the water, in kg.</summary>
        public float SuspendedSludge { get; private set; }

        /// <summary>Dissolved solids, in ppm, as last stepped.</summary>
        public float Concentration { get; private set; }

        /// <summary>Dissolved solids and stirred-up sludge, which make the water foam, in ppm.</summary>
        public float FoamingConcentration { get; private set; }

        /// <summary>How hard the water foams, 0 still to 1 foaming freely.</summary>
        public float Foam { get; private set; }

        /// <summary>Share of the steam leaving as water.</summary>
        public float Carryover => MaxCarryover * Foam * (float)Math.Sqrt(Foam);

        /// <summary>How far foam puts the water in the glass above the true level, in boiler fractions; it surges.</summary>
        public float GlassRise => GlassSwell * Foam + GlassSurge * (float)Math.Sqrt(Foam) * Surge(clock);

        /// <summary>Solids and sludge blown down since the last washout, in kg.</summary>
        public float BlownOut { get; private set; }

        /// <summary>Washes the boiler out and fills it with fresh water.</summary>
        public void Washout(float waterMass)
        {
            var brought = Math.Max(0f, waterMass) * FeedConcentration * 1e-6f;
            Solids = brought * (1f - SludgeShare);
            SuspendedSludge = brought * SludgeShare;
            SettledSludge = 0f;
            BlownOut = 0f;
            Measure(Math.Max(1f, waterMass));
            Foam = 0f;
            gathered = 1f;
        }

        /// <summary>Restores saved contents.</summary>
        public void Restore(float solids, float settled, float suspended, float waterMass)
        {
            Solids = Clean(solids);
            SettledSludge = Clean(settled);
            SuspendedSludge = Clean(suspended);
            Measure(Math.Max(1f, waterMass));
            Foam = 0f;
        }

        public void Step(float dt, BoilerWaterInputs inputs)
        {
            if (!(dt > 0f))
                return;
            clock += dt;
            var water = Math.Max(1f, inputs.WaterMass);
            var work = Smoothing.Clamp01(inputs.Work);

            // The feed's solids: dissolved, and part as sludge in the water.
            var brought = Math.Max(0f, inputs.Fed) * FeedConcentration * 1e-6f;
            Solids += brought * (1f - SludgeShare);
            SuspendedSludge += brought * SludgeShare;

            // Sludge settles when the engine stands; hard work stirs it up.
            var settle = SuspendedSludge * Math.Min(1f, (1f - work) * dt / SettleTime);
            var stir = SettledSludge * Math.Min(1f, work * work * dt / StirTime);
            SuspendedSludge += stir - settle;
            SettledSludge += settle - stir;

            // The blowdown takes the water as it is, and the settled sludge
            // gathered at its cock, which it draws away as it blows on.
            var blown = Math.Max(0f, inputs.BlownDown);
            if (blown > 0f)
            {
                var share = Math.Min(1f, blown / (water + blown));
                var solids = Solids * share;
                var suspended = SuspendedSludge * share;
                var settled = SettledSludge * Math.Min(1f, share * MudRingBias * gathered);
                Solids -= solids;
                SuspendedSludge -= suspended;
                SettledSludge -= settled;
                BlownOut += solids + suspended + settled;
                gathered *= (float)Math.Exp(-dt / GatheredDrawTime);
            }
            else
            {
                gathered += (1f - gathered) * Math.Min(1f, dt / GatherTime);
            }

            Measure(water);
            var potential = Smoothing.Clamp01((FoamingConcentration - FoamOnsetPpm) / (FoamFullPpm - FoamOnsetPpm));
            var high = Smoothing.Clamp01(2f * inputs.Fullness - 1f);
            var target = Smoothing.Clamp01(potential * (StandingFoam + (1f - StandingFoam) * work) * (1f + HighWaterFoam * high));
            Foam += (target - Foam) * Math.Min(1f, dt / FoamTime);
        }

        private void Measure(float water)
        {
            Concentration = Solids / water * 1e6f;
            FoamingConcentration = (Solids + SuspendedSludge) / water * 1e6f;
        }

        // The water's surge in the glass, -1 to 1: two beats, so it rises and
        // falls unevenly, as a riled glass does.
        private static float Surge(float time) =>
            0.6f * (float)Math.Sin(time * 2.0 * Math.PI * 0.7) + 0.4f * (float)Math.Sin(time * 2.0 * Math.PI * 1.9 + 1.3);

        private static float Clean(float value) => value >= 0f && !float.IsInfinity(value) ? value : 0f;
    }
}
