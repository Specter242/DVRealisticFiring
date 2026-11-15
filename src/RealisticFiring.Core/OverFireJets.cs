using System;

namespace RealisticFiring.Core
{
 public sealed class OverFireJets
    {
        public OverFireJets(int count, JetTube tube)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Must be at least one.");
            Count = count;
            Tube = tube;
        }

        /// <summary>How many jets the box has.</summary>
        public int Count { get; }

        /// <summary>Their tubes and nozzles.</summary>
        public JetTube Tube { get; }

        // lb/h to kg/s.
        private const float PoundsAnHour = 0.45359237f / 3600f;

        // psi to bar.
        private const float PsiPerBar = 14.5038f;

        // Benton and Engdahl's Table 1: gauge pressure (psi), and a jet's
        // steam and air (lb/h), for each tube and nozzle.
        private static readonly float[] Psi = { 0f, 50f, 100f, 150f, 200f };
        private static readonly float[] SmallSteam = { 0f, 42f, 73f, 103f, 135f };
        private static readonly float[] SmallAir = { 0f, 665f, 875f, 1045f, 1185f };
        private static readonly float[] LargeSteam = { 0f, 60f, 105f, 149f, 194f };
        private static readonly float[] LargeAir = { 0f, 1225f, 1620f, 1920f, 2175f };

        /// <summary>
        /// The firebox's draft adds to a jet's air: Table 2's about 4 to 6 per
        /// cent an inch of water; 5 per cent an inch here.
        /// </summary>
        public const float DraftGainPerInch = 0.05f;

        /// <summary>
        /// One jet's steam and air, kg/s, with <paramref name="gaugeBar"/> bar
        /// at its nozzle and <paramref name="draftInches"/> inches of water of
        /// firebox draft. Under 50 psi each falls in a straight line to nothing
        /// at no pressure, so a cold boiler blows none; over 200 psi the steam
        /// goes on with the absolute pressure, as a choked nozzle's does, and
        /// the air on its last slope.
        /// </summary>
        public static (float Steam, float Air) PerJet(JetTube tube, float gaugeBar, float draftInches = 0f)
        {
            var psi = Math.Max(0f, gaugeBar * PsiPerBar);
            if (!(psi > 0f))
                return (0f, 0f);
            var steamTable = tube == JetTube.Large ? LargeSteam : SmallSteam;
            var airTable = tube == JetTube.Large ? LargeAir : SmallAir;
            float steam, air;
            var last = Psi.Length - 1;
            if (psi >= Psi[last])
            {
                steam = steamTable[last] * (psi + 14.7f) / (Psi[last] + 14.7f);
                air = airTable[last] + (airTable[last] - airTable[last - 1]) / (Psi[last] - Psi[last - 1]) * (psi - Psi[last]);
            }
            else
            {
                var i = 1;
                while (Psi[i] < psi)
                    i++;
                var t = (psi - Psi[i - 1]) / (Psi[i] - Psi[i - 1]);
                steam = Smoothing.Lerp(steamTable[i - 1], steamTable[i], t);
                air = Smoothing.Lerp(airTable[i - 1], airTable[i], t);
            }
            air *= 1f + DraftGainPerInch * Math.Max(0f, draftInches);
            return (steam * PoundsAnHour, air * PoundsAnHour);
        }

        /// <summary>
        /// All the jets' steam and air, kg/s, with the valve <paramref name="opening"/>
        /// open (0 to 1) on a boiler at <paramref name="boilerGaugeBar"/>: the
        /// globe valve throttles the nozzles' pressure in proportion.
        /// </summary>
        public (float Steam, float Air) Flow(float opening, float boilerGaugeBar, float draftInches = 0f)
        {
            var open = Smoothing.Clamp01(opening);
            if (!(open > 0f) || !(boilerGaugeBar > 0f))
                return (0f, 0f);
            var (steam, air) = PerJet(Tube, open * boilerGaugeBar, draftInches);
            return (Count * steam, Count * air);
        }
    }

    /// <summary>The jets' tubes and nozzles (Benton and Engdahl, Table 1).</summary>
    public enum JetTube
    {
        /// <summary>A 2-1/4-in air tube with a 1/8-in nozzle: a switcher's (BE: never larger than 1/8 in in this tube).</summary>
        Small,

        /// <summary>A 3-1/4-in air tube with a 0.150-in (No. 25 drill) nozzle: a road engine's, for penetration across a big fire.</summary>
        Large,
    }
}
