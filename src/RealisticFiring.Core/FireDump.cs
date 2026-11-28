namespace RealisticFiring.Core
{
    /// <summary>What the fire-dump lever did to the fire this tick.</summary>
    public struct FireDumpTick
    {
        /// <summary>Rocks completed this tick that shake the grate, for the caller to give.</summary>
        public int Shakes;

        /// <summary>Shorter rocks completed this tick that wiggle the grate, for the caller to give.</summary>
        public int Wiggles;

        /// <summary>The dump's flow this tick, 0 to 1, for COAL_DUMP_FLOW_NORMALIZED.</summary>
        public float Flow;

        /// <summary>Coal the dump took this tick, in kg.</summary>
        public float Dumped;

        /// <summary>This tick used up the hold's share with coal still on the grate.</summary>
        public bool HoldSpent;
    }

    /// <summary>
    /// The fire-dump lever as the sim that owns the fire (a multiplayer host,
    /// or single player) acts on it each tick: the <see cref="GrateLever"/>
    /// reads rocks and holds, each hold's <see cref="DumpStroke"/> caps what it
    /// may dump, and the flow eases in and out as the stock firebox's does
    /// (SmoothDamp, 0.5 s).
    /// </summary>
    public sealed class FireDump
    {
        /// <summary>The stock firebox's smoothing of the dump flow, in s.</summary>
        public const float SmoothTime = 0.5f;

        private readonly GrateLever lever = new GrateLever();
        private readonly DumpStroke stroke = new DumpStroke();
        private float flow;
        private float flowVelocity;

        /// <summary>The current hold's allowance.</summary>
        public DumpStroke Stroke => stroke;

        /// <summary>
        /// Reads the lever at <paramref name="value"/> (0 to 1, 0.5 at center)
        /// and runs the dump on <paramref name="fire"/> for one tick. Shakes and
        /// wiggles are returned, not given, so the caller can log them.
        /// </summary>
        public FireDumpTick Step(FireModel fire, float value, float dt)
        {
            var state = lever.Update(value, dt);
            var parameters = fire.Parameters;
            if (state.DumpBegan)
                stroke.Begin(fire.CoalMass, parameters.MaxCoalCapacity);
            var target = fire.HasDumpableContents && stroke.Allow(state.Dump, fire.CoalMass, parameters.CoalDumpRate, dt) > 0f
                ? state.Dump
                : 0f;
            flow = Smoothing.SmoothDamp(flow, target, ref flowVelocity, SmoothTime, dt);
            if (target == 0f && flow < 0.01f)
            {
                flow = 0f;
                flowVelocity = 0f;
            }

            var tick = new FireDumpTick { Shakes = state.Shakes, Wiggles = state.Wiggles };
            tick.Flow = stroke.Allow(flow, fire.CoalMass, parameters.CoalDumpRate, dt);
            if (tick.Flow > 0f)
            {
                tick.Dumped = fire.Dump(tick.Flow, dt);
                stroke.Took(tick.Dumped);
                tick.HoldSpent = stroke.Spent && fire.CoalMass > 0f;
            }
            return tick;
        }
    }
}
