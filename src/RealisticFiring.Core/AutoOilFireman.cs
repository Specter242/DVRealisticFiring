using System;

namespace RealisticFiring.Core
{
 public sealed class AutoOilFireman : AutoFiremanBase
    {
        /// <summary>Oil added per bar under his pressure, as a share of the fire's most; twice that taken off per bar over.</summary>
        public const float OilPressureGain = 0.15f;

        /// <summary>
        /// Lit, he keeps at least this share of the valve open on cold brick,
        /// and this on brick glowing white-hot, as he sees it through the peep
        /// hole: a light fire on cold brick blows out at a slip or a sudden
        /// throttle, where hot brick holds a small one.
        /// </summary>
        public const float LeastFireColdBrick = 0.1f;
        public const float LeastFireHotBrick = 0.03f;

        /// <summary>
        /// The atomizing he aims for, over what the oil needs: steady
        /// yellow-white through the peep hole. Spitting and smoky orange is
        /// too little; a throbbing drum, from 2.5 times, too much.
        /// </summary>
        public const float AtomizingGoal = 1.3f;

        // He trims the atomizer toward the goal by this much a second per
        // unit he sees off it, at most this far from what he reckons the oil
        // takes.
        private const float AtomizerTrimRate = 0.3f;
        private const float AtomizerTrimMost = 0.5f;

        /// <summary>The oil he keeps, in °C: 45 to 80 runs well; warmer sputters, colder hardly runs.</summary>
        public const float OilGoal = 60f;

        // The heater cracked holds warm oil; each degree short opens it this much more.
        private const float HeaterHold = 0.05f;
        private const float HeaterPerDegree = 0.08f;

        // The stack as he sees it (the smoke's darkness): grey or black from
        // this, clear (too much air) under this, a light haze between; he
        // moves the damper and blower this fast toward the haze, per second.
        // He judged by the air's excess before, which he cannot see, and
        // called a haze black (2901 in game, 2026-10-10).
        public const float BlackSmoke = 0.3f;
        public const float ClearSmoke = 0.1f;
        private const float AirStep = 0.15f;
        private const float LeastDamper = 0.2f;

        // Black, the oil is more than the air burns: he cuts the valve back
        // this fast, in its travel a second, until it clears, and once it has
        // stayed clear this long, in s, lets it back up this fast as the air
        // comes (2901 in game, 2026-10-10: half the oil unburned and black for
        // most of a minute pulling away).
        private const float BlackCut = 0.05f;
        private const float CapHold = 15f;
        private const float CapRecover = 0.005f;

        // The atomizer cracked at least this far with oil running: more
        // drummed on a small fire (2901 in game, 2026-10-10: 5 % open was 16
        // times the need on a standing fire).
        private const float LeastAtomizer = 0.01f;

        // Blower: a little standing, more raising steam standing, wide to
        // clear the box with the fire out, near shut at the safety valve.
        private const float StandingBlower = 0.15f;
        private const float RaisingBlower = 0.6f;
        private const float ClearingBlower = 0.5f;

        /// <summary>
        /// He sands the flues when they have sooted this far (steaming falls
        /// off after smoking), working hard, a throw this big, no oftener than
        /// this, in s: a quart, thrown through the peep hole.
        /// </summary>
        public const float SootToSand = 0.3f;
        public const float SandThrow = 0.7f;
        public const float SandEvery = 600f;
        private const float SandWork = 0.5f;

        private float atomizerTrim;
        private float damper = 1f;
        private float blower = StandingBlower;
        private float sinceSand = SandEvery;
        private float oilCap = 1f;
        private float sinceBlack = CapHold;

        /// <summary>The firing valve he wants.</summary>
        public float Valve { get; private set; }

        /// <summary>The least valve he keeps lit, for the brick's heat.</summary>
        public float LeastFire { get; private set; } = LeastFireColdBrick;

        /// <summary>How the stack looks to him.</summary>
        public bool StackBlack { get; private set; }
        public bool StackClear { get; private set; }

        public FiremanActions Step(float dt, OilFire fire, FiremanReadings r)
        {
            if (fire == null)
                throw new ArgumentNullException(nameof(fire));
            var actions = new FiremanActions { Notices = Notices(fire, r) };
            if (Mode == FiremanMode.Off || !(dt > 0f))
                return actions;

            var j = Perceive(dt, r, fire.Parameters);
            Fire(dt, fire, r, j, ref actions);
            Air(dt, fire, r, j, ref actions);
            Water(dt, r, j, ref actions);
            Sand(dt, fire, r, ref actions);
            return actions;
        }

        private void Fire(float dt, OilFire fire, FiremanReadings r, Judgement j, ref FiremanActions actions)
        {
            var p = fire.Parameters;
            var lifting = r.Pressure >= r.SafetyValve - LiftMargin;
            StackBlack = fire.StackSmoke >= BlackSmoke;
            StackClear = fire.FireOn && fire.FeedRate > 0f && fire.StackSmoke < ClearSmoke;

            // The oil fire answers in seconds, so with the valve between its
            // stops the fire is as he set it, and the gauge tells him what it gives.
            var glow = Clamp((fire.BrickTemperature - OilFire.ColdBrick) / (OilFire.HotBrick - OilFire.ColdBrick), 0f, 1f);
            LeastFire = LeastFireColdBrick + (LeastFireHotBrick - LeastFireColdBrick) * glow;
            var settable = fire.FireOn && Valve > LeastFire && Valve < 1f;
            LearnFromGauge(dt, r, j, settable, lifting, StackBlack);

            var error = PressureTarget - r.Pressure;
            var want = RateFor(p, PlannedSteam * SteamHeat * Trim)
                       + OilPressureGain * p.MaxCombustionRate * (error > 0f ? error : j.Building ? 0f : 2f * error);
            // Never oil into a dead firebox: out, with the brick glowing hot
            // enough to relight it the oil catches again at once, but on dark
            // brick it would pool and flare back. With the tender's water gone
            // he lets the fire die.
            var alight = fire.FireOn || fire.BrickTemperature > OilFire.BrickRelights;
            sinceBlack = StackBlack ? 0f : sinceBlack + dt;
            if (StackBlack)
                oilCap = Math.Max(LeastFire, Math.Min(oilCap, Valve) - BlackCut * dt);
            else if (sinceBlack >= CapHold)
                oilCap = Math.Min(1f, oilCap + CapRecover * dt);
            Valve = alight && r.TenderWater > 0f && r.TenderCoal > 0f
                ? Clamp(Math.Min(want / Math.Max(1e-3f, fire.MaxFeed), oilCap), LeastFire, 1f)
                : 0f;
            actions.FiringValve = Valve;

            // The oil warm enough to run and burn clean, not so hot it sputters.
            actions.Heater = Clamp(HeaterHold + HeaterPerDegree * (OilGoal - fire.OilTemperature), 0f, 1f);

            // The atomizer matched to the oil (more for a hard exhaust and
            // cold oil), trimmed by the flame he sees.
            var cold = Clamp((OilFire.WarmOil - fire.OilTemperature) / (OilFire.WarmOil - OilFire.AmbientTemperature), 0f, 1f);
            var reckoned = AtomizingGoal * Math.Max(Valve, LeastFire) * (0.6f + 0.4f * Math.Min(1f, HeardWork)) * (1f + 1.5f * cold)
                           / OilFire.AtomizerCapacity;
            if (fire.FireOn && fire.FeedRate > 0f)
                atomizerTrim = Clamp(atomizerTrim + AtomizerTrimRate * (AtomizingGoal - fire.Atomizing) * dt,
                    -AtomizerTrimMost, AtomizerTrimMost);
            actions.Atomizer = Clamp(reckoned + atomizerTrim, LeastAtomizer, 1f);
        }

        // The damper and blower, worked toward a light haze at the stack.
        private void Air(float dt, OilFire fire, FiremanReadings r, Judgement j, ref FiremanActions actions)
        {
            if (!fire.FireOn)
            {
                // Out: the box cleared with the blower, ready for lighting.
                damper = 1f;
                blower = ClearingBlower;
            }
            else
            {
                var toward = StackBlack ? 1f : StackClear ? -1f : 0f;
                if (j.Working)
                {
                    damper = Clamp(damper + toward * AirStep * dt, LeastDamper, 1f);
                    // Black with the damper wide: the blower helps.
                    blower = StackBlack && damper >= 0.99f ? Clamp(blower + AirStep * dt, 0f, 0.5f) : Clamp(blower - AirStep * dt, 0f, 1f);
                }
                else
                {
                    // Standing or drifting, the blower makes the draft.
                    blower = Clamp(blower + toward * AirStep * dt, 0f, 1f);
                    if (r.Standing && r.Pressure < PressureTarget - 1f)
                        blower = Math.Max(blower, RaisingBlower);
                    damper = Clamp(damper + toward * AirStep * dt, LeastDamper, 1f);
                }
            }
            actions.Damper = damper;
            actions.Blower = blower;
            actions.Door = 0f;
        }

        // Sooted flues sanded while working hard: black clouds at the stack as it scours.
        private void Sand(float dt, OilFire fire, FiremanReadings r, ref FiremanActions actions)
        {
            sinceSand += dt;
            if (!fire.FireOn || fire.FlueSoot < SootToSand || HeardWork < SandWork || sinceSand < SandEvery)
                return;
            if (r.Sand < SandThrow)
                return;
            actions.Sand = SandThrow;
            sinceSand = 0f;
        }

        private static FiremanNotice Notices(OilFire fire, FiremanReadings r)
        {
            var notices = WaterNotices(r);
            if (r.TenderCoal <= 0f)
                notices |= FiremanNotice.TenderCoalOut;
            if (!fire.FireOn)
                notices |= FiremanNotice.FireOut;
            if (fire.OilTemperature < OilFire.WarmOil - 5f)
                notices |= FiremanNotice.ColdOil;
            if (fire.FlueSoot >= SootToSand && r.Sand < SandThrow)
                notices |= FiremanNotice.NoSand;
            return notices;
        }
    }
}
