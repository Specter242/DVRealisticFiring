using System;

namespace RealisticFiring.Core
{
 public readonly struct DriftFittings
    {
        public DriftFittings(bool snifting, bool byPass, float valve)
        {
            Snifting = snifting;
            ByPass = byPass;
            Valve = float.IsNaN(valve) ? 0f : Math.Max(0f, Math.Min(1f, valve));
        }

        public bool Snifting { get; }
        public bool ByPass { get; }

        /// <summary>The drifting valve's opening, 0 to 1; 0 where there is none.</summary>
        public float Valve { get; }

        public static DriftFittings None => new DriftFittings(false, false, 0f);
    }

    /// <summary>A drifting engine's steam chests as its fittings leave them.</summary>
    public readonly struct DriftState
    {
        public DriftState(bool drifting, float chest, float steamShare, bool sniftingOpen, bool byPassOpen)
        {
            Drifting = drifting;
            Chest = chest;
            SteamShare = steamShare;
            SniftingOpen = sniftingOpen;
            ByPassOpen = byPassOpen;
        }

        /// <summary>The throttle shut and the engine moving with its gear (or in mid-gear): the cylinders run on what is in the chests.</summary>
        public bool Drifting { get; }

        /// <summary>The chests' pressure, absolute bar.</summary>
        public float Chest { get; }

        /// <summary>Of what the cylinders draw, the share the drifting valve's steam fills.</summary>
        public float SteamShare { get; }

        public bool SniftingOpen { get; }
        public bool ByPassOpen { get; }

        public static DriftState NotDrifting => new DriftState(false, 1f, 0f, false, false);
    }

 public static class Drifting
    {
        /// <summary>The reverser's travel from mid-gear at which an engine drifts freely: about three-quarter stroke.</summary>
        public const float FreeReverser = 0.8f;

        /// <summary>A throttle open this far keeps steam in the cylinders.</summary>
        public const float CrackedThrottle = 0.02f;

        /// <summary>Below this speed, m/s, it does no harm worth counting.</summary>
        public const float HarmlessSpeed = 5.5f;

        /// <summary>The engine's health lost per hour drifting in mid-gear at <see cref="FullHarmSpeed"/> m/s, throttle shut (inferred).</summary>
        public const float WearPerHour = 0.05f;
        public const float FullHarmSpeed = 20f;

 public const float SniftingShare = 1f / 3f;

        /// <summary>Of the drifting wear, the share left with by-pass valves open: the cylinders' ends joined, they hardly draw at release (inferred).</summary>
        public const float ByPassShare = 1f / 3f;

        /// <summary>Faster than this, m/s, the pistons draw the chests below the atmosphere and snifting valves lift.</summary>
        public const float SniftingSpeed = 1.5f;

        /// <summary>The pressure a bare engine's chests fall to drifting, absolute bar (Newell: a mean vacuum of 2 to 10 psi; inferred).</summary>
        public const float ChestVacuum = 0.5f;

        /// <summary>The most a drifting valve's steam holds the chests to, absolute bar: its small pipe against the cylinders' draw.</summary>
        public const float MaxChest = 2f;

        /// <summary>The mean piston speed of Newell's drag figures, m/s: 1,068 ft/min, his engine at 40 mph.</summary>
        public const float ReferencePistonSpeed = 5.43f;

        /// <summary>The drag's mean effective pressure at <see cref="ReferencePistonSpeed"/>, bar: at 50 % cut-off, Newell's 5.2 psi.</summary>
        public const float DragAtHalf = 5.2f / 14.5038f;

        /// <summary>In full gear, Newell's 1.1 psi, bar.</summary>
        public const float DragInFullGear = 1.1f / 14.5038f;

        /// <summary>Hooked up to a quarter or less, bar (inferred: Newell measured only 50 % and full gear; the compression mid-gear mostly comes back, Henderson 1904).</summary>
        public const float DragHookedUp = 6f / 14.5038f;

        /// <summary>The drag at most this many times its figure at <see cref="ReferencePistonSpeed"/> (inferred: it rises with the speed, the air's friction in the ports).</summary>
        public const float MostSpeedShare = 1.5f;

        /// <summary>Below this mean piston speed, m/s, by-pass valves leave no drag: Newell's 534 ft/min, 20 mph.</summary>
        public const float ByPassFreeBelow = 2.71f;

        /// <summary>Over this much more piston speed, m/s, the by-pass valves' drag grows to all of it: half at Newell's 40 mph.</summary>
        public const float ByPassSpan = 5.43f;

        /// <summary>Of what the cylinders draw from the chests, the share by-pass valves leave them drawing: the gas goes end to end (inferred).</summary>
        public const float ByPassDraw = 0.2f;

        /// <summary>
        /// A drifting valve wide open, m²: about a 3/8-in. orifice, passing
        /// about 0.12 kg/s (950 lb/h) at 14 bar (inferred: a few hundred to
        /// 1,000 lb/h on a large engine; Newell: "it would not be economical
        /// or practical to design a drifting valve large enough to provide
        /// sufficient steam to destroy the vacuum unless a by-pass valve was
        /// also employed").
        /// </summary>
        public const float DriftingValveArea = 5.9e-5f;

        // Napier's rule, w (lb/s) = A (in²) · P (psia) / 70, in kg/s per m² per bar.
        private const float NapierPerSquareMetreBar = 145.7f;

        // Saturated steam at the atmosphere, L/kg.
        private const float SteamAtAtmosphere = 1694f;

        /// <summary>Whether drifting this way draws smokebox gas into the cylinders.</summary>
        public static bool HookedUp(float throttle, float reverser, float speed) =>
            throttle < CrackedThrottle && Math.Abs(speed) > HarmlessSpeed && Math.Abs(reverser) < FreeReverser;

        /// <summary>Whether the reverser is with the motion or in mid-gear, not against it.</summary>
        public static bool WithTheMotion(float reverser, float speed) => reverser * speed >= 0f;

        /// <summary>The steam a drifting valve open <paramref name="opening"/> passes from a boiler at <paramref name="boiler"/> absolute bar, kg/s.</summary>
        public static float ValveSteam(float opening, float boiler) =>
            float.IsNaN(opening) || float.IsNaN(boiler) ? 0f
                : Math.Max(0f, Math.Min(1f, opening)) * DriftingValveArea * NapierPerSquareMetreBar * Math.Max(0f, boiler);

        /// <summary>
        /// What the cylinders draw from the chests at the atmosphere, kg/s:
        /// each stroke fills the cut-off and clearance of its cylinder
        /// (<paramref name="cylinderVolume"/> L), two strokes a turn a cylinder.
        /// </summary>
        public static float Draw(float cutoff, float clearance, float cylinderVolume, int cylinders, float turnsPerSecond) =>
            Math.Max(0f, cutoff + clearance) * Math.Max(0f, cylinderVolume) * 2f * Math.Max(0, cylinders) * Math.Abs(turnsPerSecond)
            / SteamAtAtmosphere;

        /// <summary>
        /// The chests of an engine drifting with <paramref name="fittings"/>:
        /// the drifting valve's <paramref name="valveSteam"/> against the
        /// cylinders' <paramref name="draw"/> (both kg/s; by-pass valves, open
        /// whenever it drifts, leave it drawing <see cref="ByPassDraw"/> of
        /// that) sets their pressure, no lower than a vacuum, or the
        /// atmosphere where snifting valves let air in; snifting valves lift
        /// only while the steam falls short.
        /// </summary>
        public static DriftState State(float throttle, float reverser, float speed, float draw, float valveSteam, DriftFittings fittings)
        {
            if (!(throttle < CrackedThrottle) || !(Math.Abs(speed) > SniftingSpeed) || !WithTheMotion(reverser, speed))
                return DriftState.NotDrifting;
            var steam = Math.Max(0f, valveSteam);
            var drawn = Math.Max(0f, draw) * (fittings.ByPass ? ByPassDraw : 1f);
            var share = drawn > 0f ? Math.Min(1f, steam / drawn) : steam > 0f ? 1f : 0f;
            var filled = drawn > 0f ? Math.Min(MaxChest, steam / drawn) : steam > 0f ? MaxChest : 0f;
            var snifting = fittings.Snifting && share < 1f;
            var chest = Math.Max(snifting ? 1f : ChestVacuum, filled);
            return new DriftState(true, chest, share, snifting, fittings.ByPass);
        }

        /// <summary>
        /// The engine's health lost per second drifting so, 0 to 1: hooked up
        /// with the throttle shut, less with the chests filled with steam,
        /// with snifting valves open, and with by-pass valves open.
        /// </summary>
        public static float Wear(float throttle, float reverser, float speed, DriftState state)
        {
            if (!HookedUp(throttle, reverser, speed))
                return 0f;
            var wear = WearPerHour / 3600f * Math.Abs(speed) / FullHarmSpeed * (FreeReverser - Math.Abs(reverser)) / FreeReverser;
            return wear * (1f - state.SteamShare) * (state.SniftingOpen ? SniftingShare : 1f) * (state.ByPassOpen ? ByPassShare : 1f);
        }

        /// <summary>The wear with no fittings: hooked up with the throttle shut.</summary>
        public static float Wear(float throttle, float reverser, float speed) =>
            Wear(throttle, reverser, speed, State(throttle, reverser, speed, 1f, 0f, DriftFittings.None));

        /// <summary>
        /// The drag's mean effective pressure at <see cref="ReferencePistonSpeed"/>
        /// in a notch of <paramref name="cutoff"/>, bar: Newell's at 50 % and
        /// in full gear, and the hooked-up figure from a quarter down.
        /// </summary>
        public static float DragAt(float cutoff)
        {
            var c = float.IsNaN(cutoff) ? 0.5f : Math.Max(0f, Math.Min(1f, cutoff));
            if (c <= 0.25f)
                return DragHookedUp;
            if (c <= 0.5f)
                return DragHookedUp + (DragAtHalf - DragHookedUp) * (c - 0.25f) / 0.25f;
            if (c <= 0.9f)
                return DragAtHalf + (DragInFullGear - DragAtHalf) * (c - 0.5f) / 0.4f;
            return DragInFullGear;
        }

        /// <summary>The drag's share of its figure at <paramref name="pistonSpeed"/> m/s (mean): none standing, rising with the speed (Newell: about a third at 10 mph of what it is at 40).</summary>
        public static float SpeedShare(float pistonSpeed) =>
            float.IsNaN(pistonSpeed) ? 0f : Math.Min(MostSpeedShare, Math.Abs(pistonSpeed) / ReferencePistonSpeed);

        /// <summary>Of the drag, the share by-pass valves leave at <paramref name="pistonSpeed"/>: none until 20 mph on Newell's engine, half at 40.</summary>
        public static float ByPassShareOfDrag(float pistonSpeed) =>
            float.IsNaN(pistonSpeed) ? 0f : Math.Max(0f, Math.Min(1f, (Math.Abs(pistonSpeed) - ByPassFreeBelow) / ByPassSpan));

        /// <summary>
        /// The mean effective pressure drifting, bar, negative: the drag in
        /// this notch at this piston speed; by-pass valves leave their share
        /// of it, and the drifting valve's steam, with them, takes that away
        /// as it fills what they draw. Snifting valves and drifting steam
        /// alone leave it as it is.
        /// </summary>
        public static float Mep(DriftState state, float cutoff, float pistonSpeed)
        {
            if (!state.Drifting)
                return 0f;
            var drag = DragAt(cutoff) * SpeedShare(pistonSpeed);
            if (state.ByPassOpen)
                drag *= ByPassShareOfDrag(pistonSpeed) * (1f - state.SteamShare);
            return -drag;
        }

        /// <summary>The mean piston speed of a <paramref name="stroke"/> m stroke at <paramref name="turnsPerSecond"/>, m/s.</summary>
        public static float PistonSpeed(float stroke, float turnsPerSecond) =>
            float.IsNaN(turnsPerSecond) ? 0f : 2f * Math.Max(0f, stroke) * Math.Abs(turnsPerSecond);
    }
}
