using System;
using System.Globalization;

namespace RealisticFiring.Core
{
    /// <summary>
    /// Wire and persistence snapshot of a <see cref="CabooseStoveModel"/>.
    /// Plain value fields only so <c>ClientMirror.Codec</c> carries it directly.
    /// </summary>
    public struct CabooseStoveSnapshot
    {
        public float Wood;
        public float Char;
        public float FrontEnergy;
        public float FrontTime;
        public float RagOil;
        public int RagCount;
        public bool RagLit;
        public bool Glowing;
        public float Involved;
        public float SurfaceChar;
        public float Heat01;
        public float Smoke01;
        public float Flame01;
        public float Ember01;
        public float WarmthC;
        public float DoorOpening;
        public float LighterPilot;
        public float StarterHeat;
        public float StarterSmoke;
    }

    /// <summary>
    /// A small cast-iron caboose wood stove built on <see cref="StarterFuel"/>:
    /// firewood splits laid or thrown in, lit with a burning oiled rag (or
    /// directly with the lighter when the lighter-shortcut setting is enabled),
    /// burning steadily with natural stove draft, warming the caboose interior,
    /// and dying cleanly down to glowing embers and out when fuel runs out.
    /// No Unity or game dependencies.
    /// </summary>
    public sealed class CabooseStoveModel
    {
        /// <summary>One firewood split, kg (matches <see cref="StarterFuel.PieceMass"/>).</summary>
        public const float PieceMass = StarterFuel.PieceMass;

        /// <summary>Maximum fuel mass the stove chamber holds, kg (eight splits).</summary>
        public const float MaxWoodKg = StarterFuel.ZoneCapacity;

        /// <summary>Maximum number of full firewood splits the stove holds.</summary>
        public const int MaxPieces = 8;

        /// <summary>Door opening above which the stove door counts as open for loading, lighting, and extra draft.</summary>
        public const float DoorOpenThreshold = 0.2f;

        /// <summary>Default outside temperature when no weather/seasons reading is supplied, °C.</summary>
        public const float DefaultAmbientC = 15f;

        /// <summary>Number of floats stored by <see cref="Save"/> / <see cref="Restore"/>.</summary>
        public const int SaveStateLength = StarterFuel.StateLength + 5;

        // Natural stove draft, kg/s of air: shut door throttles draft to a
        // gentle long-lasting burn through the ash vent; an open door admits
        // plenty of air for livelier flames and faster wood consumption.
        private const float ClosedDraftAir = 0.010f;
        private const float OpenDraftAir = 0.055f;

        // Radiant feedback from the small enclosed cast-iron stove walls, W/m²:
        // only active while wood or embers are already burning (never self-ignites cold wood).
        private const float BurningWallFlux = 18000f;
        private const float EmberWallFlux = 7500f;

        // Lighter shortcut pilot (when LighterLightsCoal is on): heats laid
        // wood to piloted ignition over a few seconds without creating a fake rag.
        private const float LighterPilotDuration = 40f;
        private const float LighterPilotFlux = 68000f;

        // Heat output of a cozy full stove fire, W (scaled with StarterFuel.Pace).
        private const float FullStoveHeat = 14000f * StarterFuel.Pace;

        // Thermal smoothing constants, s.
        private const float HeatRiseTau = 10f;
        private const float HeatFallTau = 30f;
        private const float SmokeTau = 4f;
        private const float WarmthRiseTau = 45f;
        private const float WarmthCoolTau = 90f;

        private readonly StarterFuel starter = new StarterFuel();
        private float lighterPilot;
        private float ambientC = DefaultAmbientC;

        public CabooseStoveModel(float ambient = DefaultAmbientC)
        {
            ambientC = ClampAmbient(ambient);
            WarmthC = ambientC;
        }

        /// <summary>Underlying wood and rag bed.</summary>
        public StarterFuel Fuel => starter;

        /// <summary>Whether the stove fire is currently alight (burning rag, flaming wood, glowing embers, or lighter pilot).</summary>
        public bool Lit => starter.Alight || lighterPilot > 0f;

        /// <summary>Whether wood is actively flaming.</summary>
        public bool Flaming => starter.Involved > 0f;

        /// <summary>Whether glowing char embers remain.</summary>
        public bool GlowingEmbers => starter.Glowing;

        /// <summary>Whether an oiled rag inside the stove is burning.</summary>
        public bool RagLit => starter.RagLit;

        /// <summary>Remaining oil on rags in the stove, kg.</summary>
        public float RagOil => starter.RagOil;

        /// <summary>Number of oiled rags currently inside the stove.</summary>
        public int RagCount => starter.RagCount;

        /// <summary>Unburnt firewood remaining in the stove, kg.</summary>
        public float WoodKg => starter.Wood;

        /// <summary>Glowing or cold wood char remaining in the stove, kg.</summary>
        public float CharKg => starter.Char;

        /// <summary>Total wood and char mass inside the stove, kg.</summary>
        public float FuelKg => starter.Wood + starter.Char;

        /// <summary>Normalized stove heat and glow intensity, 0 to 1.</summary>
        public float Heat01 { get; private set; }

        /// <summary>Normalized wood-smoke output for the roof stovepipe, 0 to 1.</summary>
        public float Smoke01 { get; private set; }

        /// <summary>Normalized visible flame strength inside the stove, 0 to 1.</summary>
        public float Flame01 { get; private set; }

        /// <summary>Normalized glowing ember bed strength, 0 to 1.</summary>
        public float Ember01 { get; private set; }

        /// <summary>Stove door opening, 0 shut to 1 wide open.</summary>
        public float DoorOpening { get; private set; }

        /// <summary>Whether the stove door is open enough to load, light, and see into the stove.</summary>
        public bool DoorOpen => DoorOpening >= DoorOpenThreshold;

        /// <summary>Caboose interior air temperature warmed by the stove, °C.</summary>
        public float WarmthC { get; private set; }

        /// <summary>Whether the stove has non-trivial fuel, fire, or lingering warmth worth saving.</summary>
        public bool HasActiveState =>
            Lit || FuelKg > 1e-4f || RagOil > 1e-6f || Heat01 > 0.01f;

        /// <summary>Whether at least <paramref name="mass"/> kg of wood fits in the stove.</summary>
        public bool CanAddWood(float mass = PieceMass) =>
            mass > 0f && !float.IsNaN(mass) && !float.IsInfinity(mass)
            && MaxWoodKg - starter.Mass >= mass - 1e-4f;

        /// <summary>Adds firewood splits to the stove. Returns the mass accepted, kg.</summary>
        public float AddWood(float mass = PieceMass) =>
            starter.AddWood(mass, MaxWoodKg);

        /// <summary>Adds a rag (dry, oiled, or burning) to the stove. Returns whether it was accepted.</summary>
        public bool AddRag(float oil, bool lit) =>
            starter.AddRag(oil, lit);

        /// <summary>Sets the stove door opening (0 shut to 1 open).</summary>
        public void SetDoorOpening(float opening)
        {
            DoorOpening = Smoothing.Clamp01(Smoothing.Finite(opening));
        }

        /// <summary>
        /// Attempts to light the stove directly with a lighter.
        /// Lights an unlit oiled rag already inside regardless of setting;
        /// lights laid firewood directly when <paramref name="lighterShortcutEnabled"/>
        /// is true (the <c>LighterLightsCoal</c> setting).
        /// </summary>
        public bool TryIgniteWithLighter(bool lighterShortcutEnabled)
        {
            if (Lit)
                return false;
            if (starter.RagOil > 0f && !starter.RagLit)
            {
                var state = starter.Save();
                state[5] = 1f;
                starter.Restore(state);
                return starter.RagLit;
            }
            if (!lighterShortcutEnabled || starter.Wood <= 0f)
                return false;
            lighterPilot = LighterPilotDuration;
            return true;
        }

        /// <summary>Extinguishes the fire immediately, leaving unburnt fuel in the stove.</summary>
        public void Extinguish()
        {
            lighterPilot = 0f;
            starter.Extinguish();
            Flame01 = 0f;
            Ember01 = 0f;
        }

        /// <summary>Clears all fuel and resets the stove cold at <paramref name="ambient"/> °C.</summary>
        public void Clear(float ambient = DefaultAmbientC)
        {
            ambientC = ClampAmbient(ambient);
            lighterPilot = 0f;
            starter.Clear();
            Heat01 = 0f;
            Smoke01 = 0f;
            Flame01 = 0f;
            Ember01 = 0f;
            WarmthC = ambientC;
        }

        /// <summary>Advances the stove fire, chimney smoke, and caboose warmth by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt, float ambient = DefaultAmbientC, bool drowned = false)
        {
            if (!(dt > 0f) || float.IsNaN(dt) || float.IsInfinity(dt))
                return;
            ambientC = ClampAmbient(ambient);

            if (drowned)
            {
                Extinguish();
                Heat01 = Approach(Heat01, 0f, dt, 4f);
                Smoke01 = Approach(Smoke01, 0f, dt, 2f);
                WarmthC = Approach(WarmthC, ambientC, dt, WarmthCoolTau);
                return;
            }

            // Natural stove draft increases when the stove door is open.
            var airRate = Smoothing.Lerp(ClosedDraftAir, OpenDraftAir, DoorOpening);

            // Cast-iron firebox walls reflect radiant heat back onto burning wood/embers,
            // keeping small 1-2 piece fires alive without ever self-igniting cold wood.
            var wallFlux = starter.Involved > 0f
                ? BurningWallFlux * (0.55f + 0.45f * Heat01) * (0.88f + 0.12f * DoorOpening)
                : starter.Glowing
                    ? EmberWallFlux * (0.35f + 0.65f * Heat01)
                    : 0f;

            var pilotFlux = 0f;
            if (lighterPilot > 0f)
            {
                if (starter.Wood <= 0f)
                {
                    lighterPilot = 0f;
                }
                else
                {
                    pilotFlux = LighterPilotFlux;
                    var drain = starter.Involved > 0f && starter.Flaming >= 0.25f ? dt * 4f : dt;
                    lighterPilot = Math.Max(0f, lighterPilot - drain);
                }
            }

            var chamberAmbient = Math.Min(340f, ambientC + 210f * Heat01);
            starter.Step(dt, airRate, 0f, wallFlux + pilotFlux, 0f, chamberAmbient, false);

            // Once wood runs completely out, clear any remaining lighter pilot.
            if (starter.Wood <= 0f && !starter.Burning)
                lighterPilot = 0f;

            // Normalized outputs.
            var flameStrength = starter.Involved > 0f
                ? (float)Math.Sqrt(Smoothing.Clamp01(starter.Heat / FullStoveHeat)) * (float)Math.Sqrt(Math.Max(0.15f, starter.Flaming))
                : starter.RagLit || lighterPilot > 0f
                    ? 0.22f
                    : 0f;
            Flame01 = Smoothing.Clamp01(flameStrength);

            var emberStrength = starter.Glowing
                ? Smoothing.Clamp01(starter.EmberGlow * 1.35f + 0.25f * Flame01)
                : 0f;
            Ember01 = emberStrength;

            var heatTarget = starter.Burning
                ? Smoothing.Clamp01((float)Math.Sqrt(Smoothing.Clamp01(starter.Heat / FullStoveHeat)) * 0.85f + 0.30f * emberStrength)
                : (starter.RagLit || lighterPilot > 0f) ? 0.14f : 0f;
            Heat01 = Approach(Heat01, heatTarget, dt, heatTarget >= Heat01 ? HeatRiseTau : HeatFallTau);

            var smokeTarget = starter.Involved > 0f
                ? Smoothing.Clamp01(0.18f + 0.65f * starter.Smoke + 0.25f * Flame01)
                : starter.RagLit || lighterPilot > 0f
                    ? 0.18f
                    : starter.Glowing
                        ? 0.08f * emberStrength
                        : 0f;
            Smoke01 = Approach(Smoke01, smokeTarget, dt, SmokeTau);

            // Caboose interior warmth: brings a cold winter caboose up to a cozy ~22-24 °C,
            // and adds noticeable gentle warmth even on mild/warm days.
            var maxGain = Math.Max(7f, 24f - ambientC);
            var warmthTarget = ambientC + maxGain * Heat01 * (1f - 0.10f * DoorOpening);
            WarmthC = Approach(WarmthC, warmthTarget, dt, warmthTarget >= WarmthC ? WarmthRiseTau : WarmthCoolTau);
        }

        /// <summary>Short crosshair hover status describing the stove's current state.</summary>
        public string StatusText(bool lighterShortcutEnabled = false)
        {
            var door = DoorOpen ? "door open" : "door shut";
            var warmth = WarmthC.ToString("F0", CultureInfo.InvariantCulture) + " °C";
            var wood = WoodKg.ToString("F1", CultureInfo.InvariantCulture) + " kg wood";

            if (Flaming)
                return $"Caboose stove: burning warmly · {warmth} ({wood}, {door})";
            if (GlowingEmbers)
                return $"Caboose stove: glowing embers · {warmth} ({door})";
            if (RagLit || lighterPilot > 0f)
                return WoodKg > 0f
                    ? $"Caboose stove: catching ({wood}, {door})"
                    : $"Caboose stove: rag burning — lay firewood ({door})";
            if (WoodKg > 0f)
            {
                var how = RagOil > 0f
                    ? "light the oiled rag with your lighter"
                    : lighterShortcutEnabled
                        ? "light with a burning oiled rag or lighter"
                        : "light with a burning oiled rag";
                return $"Caboose stove: {wood} laid, unlit — {how} ({door})";
            }
            if (RagOil > 0f)
                return $"Caboose stove: oiled rag laid — add firewood and light ({door})";
            return DoorOpen
                ? "Caboose stove: cold, empty — lay firewood and a lit oiled rag (door open)"
                : "Caboose stove: cold, empty (door shut)";
        }

        /// <summary>Captures full state for multiplayer mirroring or persistence.</summary>
        public CabooseStoveSnapshot Snapshot()
        {
            var s = starter.Save();
            return new CabooseStoveSnapshot
            {
                Wood = s[0],
                Char = s[1],
                FrontEnergy = s[2],
                FrontTime = s[3],
                RagOil = s[4],
                RagLit = s[5] == 1f,
                Glowing = s[6] == 1f,
                Involved = s[7],
                SurfaceChar = s[8],
                RagCount = (int)s[9],
                Heat01 = Heat01,
                Smoke01 = Smoke01,
                Flame01 = Flame01,
                Ember01 = Ember01,
                WarmthC = WarmthC,
                DoorOpening = DoorOpening,
                LighterPilot = lighterPilot,
                StarterHeat = starter.Heat,
                StarterSmoke = starter.Smoke,
            };
        }

        /// <summary>Applies a host snapshot on a multiplayer client, rejecting non-finite fields.</summary>
        public void Mirror(CabooseStoveSnapshot snap)
        {
            var state = new[]
            {
                snap.Wood, snap.Char, snap.FrontEnergy, snap.FrontTime, snap.RagOil,
                snap.RagLit ? 1f : 0f, snap.Glowing ? 1f : 0f, snap.Involved, snap.SurfaceChar, snap.RagCount,
            };
            starter.Restore(state);
            starter.MirrorOutput(Smoothing.Finite(snap.StarterHeat), Smoothing.Finite(snap.StarterSmoke));
            Heat01 = Smoothing.Clamp01(Smoothing.Finite(snap.Heat01));
            Smoke01 = Smoothing.Clamp01(Smoothing.Finite(snap.Smoke01));
            Flame01 = Smoothing.Clamp01(Smoothing.Finite(snap.Flame01));
            Ember01 = Smoothing.Clamp01(Smoothing.Finite(snap.Ember01));
            WarmthC = Math.Max(-40f, Math.Min(90f, Smoothing.Finite(snap.WarmthC, ambientC)));
            DoorOpening = Smoothing.Clamp01(Smoothing.Finite(snap.DoorOpening));
            lighterPilot = Math.Max(0f, Math.Min(LighterPilotDuration, Smoothing.Finite(snap.LighterPilot)));
        }

        /// <summary>Restores stove state from a named-field snapshot; rejects non-finite values by resetting cold.</summary>
        public bool Restore(CabooseStoveSnapshot snap, float ambient = DefaultAmbientC)
        {
            Clear(ambient);
            if (!IsFinite(snap.Wood) || !IsFinite(snap.Char) || !IsFinite(snap.FrontEnergy)
                || !IsFinite(snap.FrontTime) || !IsFinite(snap.RagOil) || !IsFinite(snap.Involved)
                || !IsFinite(snap.SurfaceChar) || !IsFinite(snap.Heat01) || !IsFinite(snap.Smoke01)
                || !IsFinite(snap.WarmthC) || !IsFinite(snap.LighterPilot) || !IsFinite(snap.DoorOpening))
                return false;
            var state = new[]
            {
                snap.Wood, snap.Char, snap.FrontEnergy, snap.FrontTime, snap.RagOil,
                snap.RagLit ? 1f : 0f, snap.Glowing ? 1f : 0f, snap.Involved, snap.SurfaceChar, snap.RagCount,
            };
            starter.Restore(state);
            Heat01 = Smoothing.Clamp01(snap.Heat01);
            Smoke01 = Smoothing.Clamp01(snap.Smoke01);
            WarmthC = Math.Max(-40f, Math.Min(90f, snap.WarmthC));
            lighterPilot = Math.Max(0f, Math.Min(LighterPilotDuration, snap.LighterPilot));
            DoorOpening = Smoothing.Clamp01(snap.DoorOpening);
            Flame01 = starter.Involved > 0f ? Math.Max(0.25f, Heat01) : (starter.RagLit || lighterPilot > 0f ? 0.2f : 0f);
            Ember01 = starter.Glowing ? Smoothing.Clamp01(starter.EmberGlow * 1.35f) : 0f;
            return true;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Serializes the stove state to a compact finite float array.</summary>
        public float[] Save()
        {
            var data = new float[SaveStateLength];
            starter.Save(data, 0);
            data[StarterFuel.StateLength] = Heat01;
            data[StarterFuel.StateLength + 1] = Smoke01;
            data[StarterFuel.StateLength + 2] = WarmthC;
            data[StarterFuel.StateLength + 3] = lighterPilot;
            data[StarterFuel.StateLength + 4] = DoorOpening;
            return data;
        }

        /// <summary>Restores stove state from <see cref="Save"/>; tolerates null, wrong lengths, and non-finite entries.</summary>
        public void Restore(float[] data, float ambient = DefaultAmbientC)
        {
            Clear(ambient);
            if (data == null || data.Length < StarterFuel.StateLength)
                return;
            for (var i = 0; i < data.Length; i++)
                if (float.IsNaN(data[i]) || float.IsInfinity(data[i]))
                    return;
            starter.Restore(data, 0, StarterFuel.StateLength);
            if (data.Length >= SaveStateLength)
            {
                Heat01 = Smoothing.Clamp01(data[StarterFuel.StateLength]);
                Smoke01 = Smoothing.Clamp01(data[StarterFuel.StateLength + 1]);
                WarmthC = Math.Max(-40f, Math.Min(90f, data[StarterFuel.StateLength + 2]));
                lighterPilot = Math.Max(0f, Math.Min(LighterPilotDuration, data[StarterFuel.StateLength + 3]));
                DoorOpening = Smoothing.Clamp01(data[StarterFuel.StateLength + 4]);
            }
            Flame01 = starter.Involved > 0f ? Math.Max(0.25f, Heat01) : (starter.RagLit || lighterPilot > 0f ? 0.2f : 0f);
            Ember01 = starter.Glowing ? Smoothing.Clamp01(starter.EmberGlow * 1.35f) : 0f;
        }

        private static float ClampAmbient(float celsius) =>
            Math.Max(-40f, Math.Min(60f, Smoothing.Finite(celsius, DefaultAmbientC)));

        private static float Approach(float current, float target, float dt, float tau)
        {
            var alpha = 1f - (float)Math.Exp(-dt / Math.Max(0.01f, tau));
            return current + (target - current) * alpha;
        }
    }
}
