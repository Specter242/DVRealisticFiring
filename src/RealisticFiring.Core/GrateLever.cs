using System;

namespace RealisticFiring.Core
{
    /// <summary>What the grate lever asks of the fire this tick.</summary>
    public struct GrateLeverState
    {
        /// <summary>Rocks of half a stroke or more completed this tick, each one shake of the grate.</summary>
        public int Shakes;

        /// <summary>Shorter rocks completed this tick, each one wiggle of the grate.</summary>
        public int Wiggles;

        /// <summary>Fire dump request, 0 to 1, as the stock dump control gives it.</summary>
        public float Dump;
    }

    /// <summary>
    /// Reads the cab's fire-dump lever as a rocking grate lever, as on a real
    /// loco: rocking it off center and back (or across) shakes the grate once
    /// per rock, a short rock wiggling it and one of at least
    /// <see cref="ShakeStroke"/> shaking it; holding it over past the stock dump point for longer than
    /// <see cref="HoldTime"/> dumps the fire at the stock rate. Lever values run
    /// 0 to 1 with 0.5 at center, as the stock COAL_DUMP_CONTROL does.
    /// </summary>
    public sealed class GrateLever
    {
        /// <summary>Within this of center the lever counts as centered.</summary>
        public const float CenterBand = 0.08f;

        /// <summary>The stock firebox starts dumping this far off center.</summary>
        public const float DumpStart = 0.25f;

        /// <summary>Seconds over the dump point before a rock becomes a dump.</summary>
        public const float HoldTime = 1f;

        /// <summary>A rock this far off center or more shakes the grate; a shorter one wiggles it.</summary>
        public const float ShakeStroke = 0.17f;

        private int side;
        private float reach;
        private float heldOver;
        private bool dumped;

        public GrateLeverState Update(float value, float dt)
        {
            var offset = value - 0.5f;
            var deviation = Math.Abs(offset);
            var newSide = deviation < CenterBand ? 0 : Math.Sign(offset);
            var shakes = 0;
            var wiggles = 0;
            if (newSide != side)
            {
                // Leaving a side, back to center or straight across, completes a rock.
                if (side != 0 && !dumped)
                {
                    if (reach >= ShakeStroke)
                        shakes++;
                    else
                        wiggles++;
                }
                if (newSide != 0)
                {
                    heldOver = 0f;
                    dumped = false;
                }
                side = newSide;
                reach = 0f;
            }
            if (side != 0)
                reach = Math.Max(reach, deviation);

            var dump = 0f;
            if (side != 0 && deviation > DumpStart)
            {
                heldOver += dt;
                if (heldOver > HoldTime)
                {
                    dumped = true;
                    dump = Math.Min(1f, (deviation - DumpStart) / (0.5f - DumpStart));
                }
            }
            return new GrateLeverState { Shakes = shakes, Wiggles = wiggles, Dump = dump };
        }
    }
}
