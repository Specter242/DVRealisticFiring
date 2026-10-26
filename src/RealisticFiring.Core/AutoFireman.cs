using System;

namespace RealisticFiring.Core
{
 public sealed class AutoFireman : AutoFiremanBase
    {
        /// <summary>Coal added to the plan per bar under the target, as a share of the firebox's capacity; twice that taken off per bar over it.</summary>
        public const float PressureGain = 0.1f;

        // He learns from the gauge only with the fire within this share of his mark.
        private const float AtMark = 0.15f;

        /// <summary>Lit, the fire is never thinner than this share of the capacity, nor thicker than the most.</summary>
        public const float MinFire = 0.1f;
        public const float MaxFire = 0.85f;

        /// <summary>
        /// With the engine not working its exhaust draws little, and green
        /// coal piled on that draft only smokes: standing, drifting, or
        /// building for a start he keeps the fire to this share of the
        /// capacity, as a skilled fireman knows to.
        /// </summary>
        public const float IdleFire = 0.2f;

        // Firing: a light round, a heavier one when far behind, the time
        // between throws and between rounds, and the door's lead and hold
        // around each throw, in s.
        public const int LightRound = 4;
        public const int HeavyRound = 6;
        public const float ThrowInterval = 2.5f;
        public const float HurriedThrowInterval = 2f;
        public const float RoundGap = 8f;
        public const float HurriedRoundGap = 3f;
        private const float DoorLead = 0.4f;

        /// <summary>With the stoker feeding, he fires by hand only a corner it starves, at most this many shovelfuls a round.</summary>
        public const int StokerRound = 2;

        public const float BoostedThrowInterval = 1f;
        private const float BoostStokes = 1.5f;

        /// <summary>He fires no more while the stack is this black (the fire's soot density).</summary>
        public const float BlackSmoke = 0.35f;

        // After a round that smokes, the door is cracked this far for this long.
        private const float SmokeToCrack = 0.2f;
        public const float DoorCrack = 0.15f;
        public const float CrackTime = 10f;

        // Blower: standing or drifting, raising steam or building the fire,
        // during a round standing, and near the safety valve.
        private const float IdleBlower = 0.3f;

        /// <summary>Under this much work heard the damper checks the fire over his mark, as standing.</summary>
        public const float LightWork = 0.4f;
        private const float KeepAliveBlower = 0.15f;
        private const float RaisingBlower = 1f;
        private const float FiringBlower = 0.5f;
        private const float QuietBlower = 0.1f;

        // Not working, the damper closes as the pressure climbs this far over
        // his target, in bar, but no further than this with the stack black:
        // shut on a heavy fire it smothered and smoked black at a stop (S282
        // in game, 2026-10-10: the draft 0.09 kg/s, 100 % smoke).
        private const float DamperRange = 0.5f;
        private const float SmokeDamper = 0.4f;

        /// <summary>A wiggle of the grate every this much work, in s heard at half work or more.</summary>
        public const float WiggleEvery = 720f;

        // A half shake when ash is this share of the bed, with the engine eased,
        // no sooner than this after the last.
        private const float ShakeAshShare = 0.25f;
        private const float ShakeSpacing = 300f;

        // Clinker in any zone over this share of its reference bed needs a clean.
        internal const float CleanClinkerShare = 0.25f;

        // The stoker: he brings the fire to his mark over about this long, in
        // s; reverses a jam at this throttle; and sets the jets about the
        // working setting (master 1.4 bar, front, back, and fines part open),
        // the front and fines turned down as the work rises (the draft carries
        // the coal forward), and both ends steered toward where the bed runs
        // short. Shut, the master is left cracked so the table keeps cool.
        private const float StokerCatchUp = 40f;
        private const float JamThrottle = 0.5f;
        private const float WorkingMaster = 0.35f;
        private const float WorkingFront = 0.6f;
        private const float WorkingBack = 0.6f;
        private const float WorkingFines = 0.5f;
        private const float FrontEasing = 0.1f;
        private const float FinesEasing = 0.2f;
        private const float JetSteer = 0.4f;
        public const float CrackedMaster = 0.03f;

        private int roundLeft;
        private float roundClock;
        private float roundInterval;
        private float sinceRound = float.PositiveInfinity;
        private float crackLeft;
        private float workClock;
        private float sinceShake;

        /// <summary>The coal he wants on the grate, in kg.</summary>
        public float CoalTarget { get; private set; }

        /// <summary>Shovelfuls left in the round he is firing.</summary>
        public int RoundLeft => roundLeft;

        /// <summary>Whether the stack is too black to fire into.</summary>
        public bool WaitingOnSmoke { get; private set; }

        /// <summary>Whether he has the stoker feeding.</summary>
        public bool Stoking { get; private set; }

        public FiremanActions Step(float dt, FireModel fire, FiremanReadings r)
        {
            if (fire == null)
                throw new ArgumentNullException(nameof(fire));
            var actions = new FiremanActions { Notices = Notices(fire, r), StokerDirection = StokerDirection.Forward };
            if (Mode == FiremanMode.Off || !(dt > 0f))
            {
                roundLeft = 0;
                return actions;
            }

            var j = Perceive(dt, r, fire.Parameters);
            Fire(dt, fire, r, j, ref actions);
            Blower(fire, r, j, ref actions);
            Water(dt, r, j, ref actions);
            Grate(dt, fire, ref actions);
            return actions;
        }

        private void Fire(float dt, FireModel fire, FiremanReadings r, Judgement j, ref FiremanActions actions)
        {
            var p = fire.Parameters;
            var coal = fire.CoalMass;
            var least = MinFire * p.MaxCoalCapacity;
            // At the safety valve, more coal only blows off, unless it is for work ahead.
            var lifting = r.Pressure >= r.SafetyValve - LiftMargin;

            // He learns what the fire really gives from the gauge (door air,
            // green coal warming, ash), with the fire at his mark.
            var atMark = fire.FireOn && CoalTarget > 0f && Math.Abs(coal - CoalTarget) <= AtMark * CoalTarget;
            LearnFromGauge(dt, r, j, atMark, lifting, WaitingOnSmoke);

            // Building for work ahead, a full boiler does not hold him back.
            var error = PressureTarget - r.Pressure;
            var rate = RateFor(p, PlannedSteam * SteamHeat * Trim);
            var auxiliary = Math.Max(0f, r.AuxiliarySteam);
            var target = rate * p.BurnTime
                         + PressureGain * p.MaxCoalCapacity * (error > 0f ? error : j.Building ? 0f : 2f * error);
            // Not working, a thin fire, but standing as much more as the
            // auxiliaries' steam needs: their exhaust and the blower draw it.
            // Building for a start, the blower he opens is for the fire.
            if (!j.Working)
                target = Math.Min(target, IdleFire * p.MaxCoalCapacity
                                          + (j.Building ? 0f : RateFor(p, auxiliary * SteamHeat * Trim) * p.BurnTime));
            CoalTarget = Clamp(target, least, MaxFire * p.MaxCoalCapacity);

            WaitingOnSmoke = fire.SootDensity > BlackSmoke && coal > least;
            // With the tender dry he puts the shovel down at once.
            var outOfWater = r.TenderWater <= 0f;
            if (outOfWater)
                roundLeft = 0;

            var boost = Math.Max(1f, r.StartupMultiplier);
            Stoking = r.HasStoker && (j.Working || boost >= BoostStokes) && fire.FireOn && !outOfWater && Mode != FiremanMode.KeepAlive;
            if (r.HasStoker)
                Stoker(fire, r, boost * rate, coal, ref actions);

            sinceRound += dt;
            crackLeft = Math.Max(0f, crackLeft - dt);
            var shovel = Math.Max(0.1f, r.ShovelMass);
            // With the stoker feeding he fires only a corner it starves;
            // otherwise, or with the start-up boost burning faster than a
            // stoker feeds, the whole of what the fire is short.
            var boosted = boost > 1.01f;
            var corners = Stoking && !boosted;
            var deficit = corners ? ZoneShortfall(fire, CoalTarget) : CoalTarget - coal;
            // Far behind with the engine working he hurries; with it not
            // working the draft could not take a hurried round.
            var far = !Stoking && j.Working && deficit > HeavyRound * shovel;
            if (roundLeft == 0 && fire.FireOn && !outOfWater && !WaitingOnSmoke && (!lifting || j.Building || coal < least)
                && deficit >= (corners ? 1f : 0.5f) * shovel && r.TenderCoal >= shovel
                && sinceRound >= (far ? HurriedRoundGap : RoundGap) / boost)
            {
                var most = corners ? StokerRound : far || boosted ? HeavyRound : LightRound;
                roundLeft = Math.Max(1, Math.Min(most, (int)Math.Round(deficit / shovel)));
                roundInterval = Math.Max(boosted ? BoostedThrowInterval : 0f, (far ? HurriedThrowInterval : ThrowInterval) / boost);
                roundClock = 0f;
            }

            if (roundLeft > 0)
            {
                // Each throw comes a door's lead into its interval; the door
                // is open from the lead before it to the lead after.
                var before = roundClock;
                roundClock += dt;
                var throwAt = DoorLead;
                if (before < throwAt && roundClock >= throwAt)
                {
                    actions.Shovelful = Math.Min(shovel, Math.Max(0f, r.TenderCoal));
                    actions.Aim = fire.NeediestZone();
                }
                actions.Door = roundClock < 2f * DoorLead ? 1f : 0f;
                if (roundClock >= roundInterval)
                {
                    roundClock -= roundInterval;
                    roundLeft--;
                    if (roundLeft == 0)
                    {
                        sinceRound = 0f;
                        crackLeft = fire.SmokeDensity > SmokeToCrack ? CrackTime : 0f;
                    }
                }
            }
            if (roundLeft == 0)
            {
                // Cracked after a smoky round, to burn the smoke of a fire the
                // exhaust no longer draws, or to cool the fire off the safety valve.
                var smoking = !j.Working && fire.SmokeDensity > SmokeToCrack;
                actions.Door = fire.FireOn && (crackLeft > 0f || smoking || lifting) ? DoorCrack : 0f;
            }
        }

        // The stoker: run to hold the fire at his mark while the engine works,
        // its jets set for the work and steered to where the bed runs short;
        // reversed a few turns when it jams; shut standing, the master cracked.
        private void Stoker(FireModel fire, FiremanReadings r, float rate, float coal, ref FiremanActions actions)
        {
            if (r.StokerJammed)
            {
                // The engine stalls with its gauge pegged: reverse it to clear the jam.
                actions.StokerDirection = StokerDirection.Reverse;
                actions.StokerThrottle = JamThrottle;
                actions.MasterJet = WorkingMaster;
                actions.FrontJets = WorkingFront;
                actions.BackJets = WorkingBack;
                actions.FinesJet = WorkingFines;
                return;
            }
            if (!Stoking)
            {
                actions.MasterJet = CrackedMaster;
                return;
            }
            var full = Math.Max(0.01f, r.StokerFullFeed);
            actions.StokerThrottle = Clamp(rate / full + (CoalTarget - coal) / (full * StokerCatchUp), 0f, 1f);

            // Where the bed runs short, front or back, he opens those jets and eases the others.
            var (front, back) = RowShortfalls(fire, CoalTarget);
            var rowShare = Math.Max(0.1f, CoalTarget / GrateLayout.Rows);
            var steer = Clamp((back - front) / rowShare, -1f, 1f) * JetSteer;
            actions.MasterJet = WorkingMaster;
            actions.FrontJets = Clamp(WorkingFront - FrontEasing * HeardWork - 0.5f * steer, 0.3f, 1f);
            actions.BackJets = Clamp(WorkingBack + steer, 0.3f, 1f);
            actions.FinesJet = Clamp(WorkingFines - FinesEasing * HeardWork, 0.2f, 0.8f);
        }

        // The zone furthest short of its share of the coal he wants, by how
        // much, in kg: shares go as the square of each zone's draft, as
        // NeediestZone weighs them.
        private static float ZoneShortfall(FireModel fire, float wanted)
        {
            var weights = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
                weights += fire.DraftWeight((GrateZone)i) * fire.DraftWeight((GrateZone)i);
            var shortest = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var weight = fire.DraftWeight((GrateZone)i);
                var share = weights > 0f ? wanted * weight * weight / weights : wanted / GrateLayout.ZoneCount;
                shortest = Math.Max(shortest, share - fire.Zones[i].Coal);
            }
            return shortest;
        }

        // How far the front and back rows fall short of their shares, in kg.
        private static (float Front, float Back) RowShortfalls(FireModel fire, float wanted)
        {
            var weights = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
                weights += fire.DraftWeight((GrateZone)i) * fire.DraftWeight((GrateZone)i);
            float front = 0f, back = 0f;
            for (var column = 0; column < GrateLayout.Columns; column++)
            {
                foreach (var (row, isFront) in new[] { (GrateLayout.Rows - 1, true), (0, false) })
                {
                    var zone = GrateLayout.At(row, column);
                    var weight = fire.DraftWeight(zone);
                    var share = weights > 0f ? wanted * weight * weight / weights : wanted / GrateLayout.ZoneCount;
                    var shortfall = share - fire.Zones[(int)zone].Coal;
                    if (isFront)
                        front += shortfall;
                    else
                        back += shortfall;
                }
            }
            return (front, back);
        }

        private void Blower(FireModel fire, FiremanReadings r, Judgement j, ref FiremanActions actions)
        {
            actions.Damper = 1f;
            if (!fire.FireOn)
                return;
            var smoking = fire.SootDensity > BlackSmoke;
            if (j.Working ? HeardWork < LightWork : !j.Building)
                actions.Damper = Clamp(1f - (r.Pressure - PressureTarget) / DamperRange, smoking ? SmokeDamper : 0f, 1f);
            if (j.Working)
                return;
            var blower = Mode == FiremanMode.KeepAlive ? KeepAliveBlower : IdleBlower;
            if (r.Pressure >= r.SafetyValve - SoakMargin)
                blower = QuietBlower;
            if (r.Standing && (j.Building || r.Pressure < PressureTarget - 1f))
                blower = RaisingBlower;
            if (actions.Door > DoorCrack)
                blower = Math.Max(blower, FiringBlower);
            // A black stack standing wants draft to burn its smoke.
            if (smoking)
                blower = Math.Max(blower, IdleBlower);
            actions.Blower = blower;
        }

        private void Grate(float dt, FireModel fire, ref FiremanActions actions)
        {
            sinceShake += dt;
            // Work heard, at half work or more counting in full.
            workClock += dt * Clamp(HeardWork / 0.5f, 0f, 1f);
            if (!fire.FireOn || roundLeft > 0)
                return;
            // A dull red fire: ash. He shakes it with the engine eased.
            var bed = fire.CoalMass + fire.AshMass;
            if (bed > 0f && fire.AshMass / bed > ShakeAshShare
                && HeardWork <= WorkingShare && sinceShake >= ShakeSpacing)
            {
                actions.Shake = ShakeStroke.Shake;
                sinceShake = 0f;
                workClock = 0f;
            }
            else if (workClock >= WiggleEvery)
            {
                actions.Shake = ShakeStroke.Wiggle;
                sinceShake = 0f;
                workClock = 0f;
            }
        }

        private static FiremanNotice Notices(FireModel fire, FiremanReadings r)
        {
            var notices = WaterNotices(r);
            foreach (var zone in fire.Zones)
            {
                if (zone.Clinker > CleanClinkerShare * fire.ReferenceZoneMass)
                {
                    notices |= FiremanNotice.CleanFire;
                    break;
                }
            }
            if (fire.AshpanFill >= 0.5f)
                notices |= FiremanNotice.EmptyAshpan;
            if (r.TenderCoal < Math.Max(0.1f, r.ShovelMass))
                notices |= FiremanNotice.TenderCoalOut;
            if (!fire.FireOn)
                notices |= FiremanNotice.FireOut;
            return notices;
        }
    }
}
