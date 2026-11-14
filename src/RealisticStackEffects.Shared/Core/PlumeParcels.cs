using System;

namespace RealisticStackEffects.Core
{
 public static class PlumeParcels
    {
        /// <summary>A particle's square shows its smoke across about this share of its width.</summary>
        public const float ShownShare = 0.6f;

        // The plume's visible envelope against its width by the plume laws
        // (PlumeRise.PlumeWidth, which particles 0.75 of it wide, set up to
        // 0.4 of it out, drew 1.25 wide).
        private const float EnvelopePerWidth = 1.25f;

        /// <summary>
        /// How wide the plume shows where it is <paramref name="plumeWidth"/>
        /// m wide by its rise and spread, and no narrower than the exhaust
        /// spreading from the lip shows, <paramref name="leastShown"/> m.
        /// </summary>
        public static float Envelope(float plumeWidth, float leastShown) =>
            Math.Max(EnvelopePerWidth * Math.Max(0f, plumeWidth), Math.Max(0f, leastShown));

        /// <summary>
        /// A particle's size, m, to show a lump <paramref name="lump"/> of
        /// the envelope across.
        /// </summary>
        public static float Size(float envelope, float lump) =>
            Math.Max(0f, lump) * Math.Max(0f, envelope) / ShownShare;

        /// <summary>
        /// How far from the plume's middle a particle sits, m, for how far
        /// out its place is (<paramref name="out"/>, 0 the middle to 1 the
        /// rim): its lump's edge reaches the envelope's at 1.
        /// </summary>
        public static float Offset(float envelope, float lump, float @out) =>
            0.5f * Math.Max(0f, envelope) * Math.Max(0f, 1f - lump) * Math.Min(1f, Math.Max(0f, @out));

        /// <summary>
        /// How far out a particle's place is, 0 to 1, for its draw by its
        /// seed (<paramref name="draw"/>, 0 to 1). Even through a ball
        /// (the cube root) half of them sat in its outer fifth, the rim's
        /// small lobes, and the sky showed through a dense column's
        /// middle; more sit nearer the middle, the plume's body.
        /// </summary>
        public static float Out(float draw) => (float)Math.Pow(Math.Min(1f, Math.Max(0f, draw)), 0.6);

        /// <summary>
        /// The share of the particles kept where the plume is
        /// <paramref name="envelope"/> m across and moves on at
        /// <paramref name="speed"/> m/s, emitted <paramref name="rate"/> a
        /// second: as many as put <paramref name="across"/> lumps in each
        /// envelope's length of it. Fewer than that left the stack: all.
        /// </summary>
        public static float Kept(float envelope, float speed, float rate, float across)
        {
            if (!(rate > 0f) || !(envelope > 0f))
                return 1f;
            return Math.Min(1f, Math.Max(0f, across) * Math.Max(0f, speed) / (envelope * rate));
        }

        /// <summary>
        /// A parcel's smoke, against its steam, for the same optical depth
        /// in the model: the model's depths are what one particle showed,
        /// and a light haze's particles shared theirs where steam's and
        /// dense smoke's did not. <paramref name="dense"/> is how far its
        /// smoke is drawn as steam was (0 a light haze, 1 black smoke),
        /// <paramref name="light"/> a light haze's share, and
        /// <paramref name="black"/> black smoke's: at steam's whole amount
        /// its trail was several times thicker far downwind than it had
        /// drawn, a dark haze that stayed.
        /// </summary>
        public static float SmokeAmount(float dense, float light, float black = 1f) =>
            light + (Math.Max(light, black) - light) * Math.Min(1f, Math.Max(0f, dense));

        /// <summary>
        /// The optical depth through the middle of a particle
        /// <paramref name="size"/> m wide carrying <paramref name="depth"/>
        /// (the model's, at the stack) over <paramref name="area"/> m², the
        /// share <paramref name="kept"/> of the particles that left with it
        /// still carried: its amount, and its retired neighbors', over the
        /// disc it shows.
        /// </summary>
        public static float Depth(float depth, float area, float size, float kept)
        {
            var shown = ShownShare * Math.Max(1e-3f, size);
            var disc = (float)(Math.PI / 4.0) * shown * shown;
            return Math.Min(MostDepth, Math.Max(0f, depth) * Math.Max(0f, area) / (disc * Math.Max(1e-3f, kept)));
        }

 public static void Roll(float axial, float radial, out float axialRate, out float radialRate)
        {
            axialRate = 1.5f * (1f - 2f * radial * radial - axial * axial);
            radialRate = 1.5f * radial * axial;
        }

        /// <summary>No particle is drawn deeper than this: 0.998 opaque.</summary>
        public const float MostDepth = 6f;

        /// <summary>The opacity of an optical depth.</summary>
        public static float Opacity(float depth) => 1f - (float)Math.Exp(-Math.Max(0f, depth));

        /// <summary>
        /// The plume's optical depth across, from what left the stack: each
        /// particle's <paramref name="amount"/> (its depth times its area,
        /// m²), <paramref name="rate"/> of them a second, spread over a
        /// plume <paramref name="envelope"/> m across that moves on at
        /// <paramref name="speed"/> m/s. Whatever its particles' number and
        /// size now.
        /// </summary>
        public static float AcrossDepth(float amount, float rate, float speed, float envelope) =>
            Math.Max(0f, amount) * Math.Max(0f, rate) / (Math.Max(0.1f, speed) * Math.Max(0.1f, envelope));

        // A lump in the plume's middle is this many times the usual, and
        // one at its rim this: the middle ones are its body, which shows
        // no gaps, and the rim's its lobes.
        private const float BodyLump = 1.9f;
        private const float RimLump = 0.8f;

        // Mixed into an even haze, a lump is this share of the plume
        // across, and this share of the usual number fill it.
        private const float MixedLump = 0.6f;

        /// <summary>The share of the usual number of lumps that fill a plume mixed into an even haze.</summary>
        public const float MixedShare = 0.6f;

        /// <summary>
        /// A lump's share of the plume's envelope, for the usual
        /// <paramref name="share"/>: by how far out it sits
        /// (<paramref name="out"/>, 0 the middle to 1 the rim), and how far
        /// the plume has <paramref name="mixed"/> into an even haze there
        /// (0 to 1), where a lump is most of the plume across.
        /// </summary>
        public static float Lump(float share, float @out, float mixed)
        {
            var place = Math.Min(1f, Math.Max(0f, @out));
            var lump = share * (BodyLump + (RimLump - BodyLump) * place);
            var haze = Math.Min(1f, Math.Max(0f, mixed));
            return Math.Min(1f, lump + (Math.Max(lump, MixedLump) - lump) * haze);
        }
    }
}
