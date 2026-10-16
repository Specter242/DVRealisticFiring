namespace RealisticFiring.Sim
{
 internal readonly struct AirPump
    {
        public AirPump(string name, float air, float steam, float fullSpeedPressure)
        {
            Name = name;
            Air = air;
            Steam = steam;
            FullSpeedPressure = fullSpeedPressure;
        }

        /// <summary>
        /// The 9½-in. single-stage: 39 cu ft of free air a minute on 60 lb of
        /// steam a 100 cu ft; it pumps to about 15 lb under the steam, so it
        /// needs about 131 psi to reach the game's cut-out, 9 bar absolute (pp.57, 59).
        /// </summary>
        public static readonly AirPump NineAndHalf = new AirPump("9½-in.", 18.4f, 0.18f, 10f);

        /// <summary>
        /// The 8½-in. cross-compound: 131 cu ft a minute on 19.65 lb of steam a
        /// 100 cu ft (p.59); built for 200 lb of steam against 140 of air (p.45),
        /// so it needs about 166 psi to reach the cut-out (inferred).
        /// </summary>
        public static readonly AirPump CrossCompound = new AirPump("8½-in. cross-compound", 61.8f, 0.19f, 12.5f);

        public string Name { get; }

        /// <summary>Free air flat out, bar·L/s.</summary>
        public float Air { get; }

        /// <summary>Steam flat out, kg/s.</summary>
        public float Steam { get; }

        /// <summary>The boiler pressure it runs full speed from, absolute bar.</summary>
        public float FullSpeedPressure { get; }
    }
}
