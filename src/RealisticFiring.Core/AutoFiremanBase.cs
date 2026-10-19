using System;

namespace RealisticFiring.Core
{
 public abstract class AutoFiremanBase
    {
        /// <summary>Heat to make a kg of steam from warm feed, in J.</summary>
        public const float SteamHeat = 2.6e6f;

        // Each mode's pressure, as a share of the safety valve's gauge pressure.
        public const float RoadPressureShare = 0.96f;
        public const float ShuntingPressureShare = 0.9f;
        public const float KeepAlivePressureShare = 0.55f;

        /// <summary>Easing off, the pressure may fall this much under the mode's, in bar.</summary>
        public const float EasingPressureDrop = 1f;

        /// <summary>On a damaged boiler he holds at least this far under where it would burst, in bar.</summary>
        public const float BurstMargin = 1.5f;

        // What the fire really gives against what he expects, learned from
        // the gauge: with the work steady a while and the fire at his mark,
        // the pressure he foresees a minute on (from where the needle is and
        // which way it moves), short of his, raises it at this rate per
        // bar-second. Its range, and where a skilled fireman starts: this
        // fire gives about a quarter less heat than the book's figure.
        protected const float TrimRate = 0.002f;
        protected const float GaugeLook = 60f;
        private const float GaugeTrendTime = 20f;
        protected const float SteadyWorkTime = 30f;
        private const float SteadyWorkChange = 0.1f;
        protected const float MinTrim = 0.6f;
        protected const float MaxTrim = 2.2f;
        public const float StartTrim = 1.3f;

        // A start coming: standing with the reverser this far out of
        // mid-gear and the brakes off. A stop coming: moving with the
        // throttle under this and the brakes on.
        private const float StartCutoff = 0.3f;
        private const float ShutThrottle = 0.05f;

        /// <summary>
        /// Knowing the road, he fires for the work it will take soon, but
        /// never for less than this share of what the engine works now: over
        /// the top he eases, he does not let the fire die.
        /// </summary>
        public const float RoadFloor = 0.5f;

        // Shunting fires for the average of its work over this long, in s.
        private const float ShuntingAverageTime = 120f;

        // What a call or a coming start asks of the fire, as a share of the most steam it makes.
        public const float HeavyShare = 0.6f;
        public const float StartShare = 0.4f;

        /// <summary>A call holds at most this long, in s.</summary>
        public const float CallTime = 180f;

        // A heavy call ends once the engine has worked this hard this long.
        protected const float HeavyWorkShare = 0.4f;
        private const float HeavyWorkTime = 60f;

        // An easing call ends when the engine opens up again past this much
        // more than it drew when called, after a moment.
        private const float EasingReopen = 1.5f;
        private const float EasingSettle = 10f;

        // Within this of the safety valve, in bar, more fire only blows off.
        protected const float LiftMargin = 0.15f;

        // Above this share of full work heard, the engine is working: no blower.
        protected const float WorkingShare = 0.15f;

        // Water: the glass's usual goal, before heavy work, easing off, and
        // soaking up heat by the safety valve; the band around it.
        public const float GlassGoal = 0.55f;
        public const float GlassBeforeWork = 0.75f;
        public const float GlassEasing = 0.75f;
        public const float GlassSoak = 0.8f;
        public const float GlassBand = 0.05f;

        /// <summary>
        /// Working hard (over <see cref="SacrificeFromWork"/>) more than
        /// <see cref="SacrificeFrom"/> bar short of pressure, he lets the glass
        /// fall to save the heat cold feed takes (the injector can outfeed the
        /// boiling), the further the further the pressure has fallen: down to
        /// this at <see cref="SacrificeOver"/> bar short; he fills it again once
        /// the work eases. Hard work coming, he carries the glass high to have
        /// the water to spend.
        /// </summary>
        public const float GlassLeast = 0.3f;

        /// <summary>
        /// On the grades ahead he keeps the glass in sight: reading no higher
        /// than this on the steepest climb, and no lower than
        /// <see cref="GlassLeast"/> on the steepest descent, where the water
        /// runs forward off the crown sheet (the crown sheet first). He judges
        /// the water as it would stand level (<see cref="FiremanReadings.GlassTilt"/>
        /// taken off), which is what primes the cylinders; on a descent he had
        /// filled to his usual reading, and the glass ran full once the road
        /// levelled (a player's screenshot, 2026-10-18).
        /// </summary>
        public const float GlassTop = 0.9f;
        public const float SacrificeFromWork = 0.5f;
        public const float SacrificeFrom = 0.5f;
        public const float SacrificeOver = 1.5f;

        // Filling, he takes no more than the fire can heat: none extra once
        // the pressure is this far short of his, in bar, unless the glass is
        // under his floor. Storing water ahead of hard work, he fires for it
        // too (refilling after work, the fire has the heat already): warming
        // the injector's feed to the boiler's heat takes this, in J/kg.
        private const float FillShortfall = 1f;
        private const float FeedWarming = 0.6e6f;

        // How he watches the glass: his reading follows it over a few
        // seconds (it bobs), and its trend over twenty; he looks this often
        // and moves the lever by this share of what it seems to want,
        // bringing the glass back to his goal over about this long, in s.
        private const float GlassReadTime = 4f;
        private const float TrendTime = 20f;
        public const float GlanceEvery = 5f;
        private const float GlanceShare = 0.5f;
        private const float ApproachTime = 120f;

        // He hears the work over a few seconds; a change this big moves the
        // lever at once, by what he has learned each share of work takes,
        // learned over steady running with this time constant, in s.
        private const float WorkHearTime = 3f;
        private const float WorkStep = 0.05f;
        private const float LearnTime = 120f;
        private const float SteadyTrend = 0.0002f;
        private const float LearnFromWork = 0.1f;

        // Without a figure for the boiler, the water from the glass's bottom to its top, in kg.
        private const float UsualGlassMass = 4000f;

        // He keeps the lever this far over the injector's least, so a notched
        // lever does not settle under it and waste at the overflow.
        private const float LeastMargin = 0.06f;

        /// <summary>
        /// With a feed pump fitted, once the engine has run with steam on this
        /// long, in s, he feeds with the pump, the injector shut, however light
        /// the work; standing or drifting he goes back to the injector, as the
        /// period practice was (an open heater has no exhaust to warm the feed).
        /// </summary>
        public const float PumpAfter = 120f;

        /// <summary>Standing or drifting this long, in s, he shuts the pump for the injector: not for a moment's easing.</summary>
        public const float PumpDrop = 15f;

        /// <summary>The water valve he starts a two-valve injector on, before he regulates it; at least its least.</summary>
        public const float StartWater = 0.6f;

        private const float PumpLeast = FeedPumpSpec.ShutOpening + 0.03f;
        private const float PumpRest = 60f;

        // Within this of the safety valve, in bar, he soaks heat up with
        // feed, until the pressure has fallen this far under it: the cold
        // feed itself lowers the pressure, and stopping at once would only
        // start the injector again a moment later.
        protected const float SoakMargin = 0.3f;
        private const float SoakRelease = 1f;

        /// <summary>
        /// Once on, the injector runs at least this long, and once off rests
        /// this long, in s, unless the glass strays three bands from his goal:
        /// a fireman does not flick it on and off.
        /// </summary>
        public const float LeastRun = 30f;
        public const float LeastRest = 15f;

        // Below the target by this much, in bar, a hard-worked engine's water may sag.
        private const float SagDeficit = 0.5f;

        // A broken jet is shut this long, in s, before it is cracked again.
        private const float BrokenRest = 2f;

        // Blowdown: on above and off below these solids, in ppm; burst open
        // and shut times, in s; never below this pressure, absolute bar.
        public const float BlowdownFrom = 2200f;
        public const float BlowdownTo = 1900f;
        public const float BurstOpen = 3f;
        public const float BurstShut = 6f;
        private const float BlowdownLeastPressure = 3f;

        /// <summary>The water is out of sight in the glass at this share, about where a glass's bottom sits.</summary>
        public const float LowGlass = 0.1f;

        /// <summary>What he makes of the moment, for the fire.</summary>
        protected struct Judgement
        {
            public float Most;
            public float Full;
            public bool Working;
            public bool Building;
            public bool StartComing;
        }

        private float callAge;
        private float heavyWorked;
        private float workAtCall;
        private float averageWork;
        private float workMark;
        private float lastPressure = float.NaN;
        private float steamOnFor;
        private float steamOffFor;
        private bool feeding;
        private float feedingFor = LeastRest;
        // His reading of the glass, as the water would stand level.
        private float seenGlass = float.NaN;
        private float glassTrend;
        private float workAtLever;
        private float feedLever = float.NaN;
        private float leverPerWork = float.NaN;
        private float pumpLever = float.NaN;
        private float pumpPerWork = float.NaN;
        private bool onPump;
        private float pumpRest;
        private float sinceGlance;
        private float fillSteam;
        private bool soaking;
        private float brokenRest;
        private bool blowingDown;
        private float burstLeft;
        private float sinceBurst = BurstShut;
        private FiremanCall call;

        /// <summary>What he hears of the engine's work, over a few seconds.</summary>
        protected float HeardWork { get; private set; }

        /// <summary>How long the work he hears has held steady, in s.</summary>
        protected float SteadyFor { get; private set; }

        /// <summary>Which way the gauge's needle moves, in bar/s.</summary>
        protected float PressureTrend { get; private set; }

        public FiremanMode Mode { get; set; }

        /// <summary>The engineer's call; it lapses by itself (see <see cref="CallTime"/>).</summary>
        public FiremanCall Call
        {
            get => call;
            set
            {
                call = value;
                callAge = 0f;
                heavyWorked = 0f;
                workAtCall = float.NaN;
            }
        }

        /// <summary>How long the call holds at most, in s; 0 with none.</summary>
        public float CallLeft => call == FiremanCall.None ? 0f : Math.Max(0f, CallTime - callAge);

        /// <summary>Steam he is firing for, in kg/s.</summary>
        public float PlannedSteam { get; private set; }

        /// <summary>The pressure he holds, absolute bar.</summary>
        public float PressureTarget { get; private set; }

        /// <summary>Where he holds the water in the glass.</summary>
        public float GlassTarget { get; private set; } = GlassGoal;

        /// <summary>His correction for what the fire really gives: 1 when it gives what he expects.</summary>
        public float Trim { get; protected set; } = StartTrim;

        /// <summary>Whether he sees a stop coming: throttle shut and brakes on, moving.</summary>
        public bool StopComing { get; private set; }

        /// <summary>Whether he is feeding the boiler.</summary>
        public bool Feeding => feeding;

        /// <summary>Whether he feeds with the pump rather than the injector.</summary>
        public bool OnPump => onPump;

        /// <summary>Whether he is blowing the boiler down.</summary>
        public bool BlowingDown => blowingDown;

        /// <summary>The most steam a fire of these figures can make, in kg/s.</summary>
        public static float MostSteam(FireboxParameters p) => p.MaxHeatOutput / SteamHeat;

        /// <summary>
        /// The burn rate that gives <paramref name="heat"/> W, in kg/s, as the
        /// stock firebox's efficiency falls with the rate.
        /// </summary>
        public static float RateFor(FireboxParameters p, float heat)
        {
            if (!(heat > 0f))
                return 0f;
            var most = p.MaxCombustionRate;
            var c = FireboxParameters.CoalHeatContent;
            // heat = r c (1 + (e - 1) r / most), solved for r.
            var a = (p.EfficiencyAtMaxCombustion - 1f) * c / most;
            var disc = c * c + 4f * a * heat;
            if (disc <= 0f)
                return most;
            return Math.Min(most, 2f * heat / (c + (float)Math.Sqrt(disc)));
        }

        /// <summary>The pressure a mode holds, absolute bar, for a safety valve lifting at <paramref name="safetyValve"/>.</summary>
        public static float PressureFor(FiremanMode mode, float safetyValve)
        {
            var share = mode == FiremanMode.Road ? RoadPressureShare
                : mode == FiremanMode.Shunting ? ShuntingPressureShare
                : KeepAlivePressureShare;
            return 1f + share * Math.Max(0f, safetyValve - 1f);
        }

        /// <summary>
        /// Hears and sees the moment: the work, the gauge, the engineer, the
        /// calls; sets his pressure and the steam he fires for.
        /// </summary>
        protected Judgement Perceive(float dt, FiremanReadings r, FireboxParameters p)
        {
            var most = MostSteam(p);
            var full = r.FullWorkSteam > 0f ? r.FullWorkSteam : most;
            Hear(dt, r);
            var working = HeardWork > WorkingShare;
            averageWork += (HeardWork - averageWork) * Math.Min(1f, dt / ShuntingAverageTime);
            var startComing = r.Standing && r.Cutoff > StartCutoff && !r.Braking;
            StopComing = !r.Standing && r.Throttle < ShutThrottle && r.Braking;
            AgeCall(dt);

            var easing = call == FiremanCall.EasingOff || StopComing;
            PressureTarget = PressureFor(Mode, r.SafetyValve) - (easing ? EasingPressureDrop : 0f);
            if (r.BurstPressure > 0f)
                PressureTarget = Math.Min(PressureTarget, Math.Max(1f, r.BurstPressure - BurstMargin));
            // Building is for the work coming, not for the water he stores:
            // counting that made him build, so carry the glass high, so store
            // and fire for it, on level road until the safety valve lifted
            // (S282 in game, 2026-10-10: the fire short of air at 23 % work).
            var forWork = Plan(r, most, full, startComing);
            PlannedSteam = forWork + fillSteam;
            return new Judgement
            {
                Most = most,
                Full = full,
                Working = working,
                Building = forWork > HeardWork * full + StartShare * most * 0.5f,
                StartComing = startComing,
            };
        }

        // What he hears and sees: the work in the exhaust (and whether it has
        // held steady), and the gauge's needle and which way it moves.
        private void Hear(float dt, FiremanReadings r)
        {
            HeardWork += (Math.Max(0f, r.Work) - HeardWork) * Math.Min(1f, dt / WorkHearTime);
            if (Math.Abs(HeardWork - workMark) > SteadyWorkChange)
            {
                workMark = HeardWork;
                SteadyFor = 0f;
            }
            else
            {
                SteadyFor += dt;
            }
            if (!float.IsNaN(lastPressure))
                PressureTrend += ((r.Pressure - lastPressure) / dt - PressureTrend) * Math.Min(1f, dt / GaugeTrendTime);
            lastPressure = r.Pressure;
        }

        private void AgeCall(float dt)
        {
            if (call == FiremanCall.None)
                return;
            callAge += dt;
            if (float.IsNaN(workAtCall))
                workAtCall = HeardWork;
            if (call == FiremanCall.HeavyAhead && HeardWork > HeavyWorkShare)
                heavyWorked += dt;
            var done = callAge >= CallTime
                       || call == FiremanCall.HeavyAhead && heavyWorked >= HeavyWorkTime
                       || call == FiremanCall.EasingOff && callAge >= EasingSettle
                          && HeardWork > EasingReopen * workAtCall + WorkingShare;
            if (done)
                Call = FiremanCall.None;
        }

        // The steam he fires for: the work he hears, as he knows his engine's
        // steam at full work, and what he expects of the road and the engineer.
        private float Plan(FiremanReadings r, float most, float full, bool startComing)
        {
            if (Mode == FiremanMode.KeepAlive || call == FiremanCall.EasingOff || StopComing)
                return 0f;
            var heard = HeardWork * full;
            var soon = Math.Max(0f, r.WorkSoon) * full;
            var plan = Mode == FiremanMode.Road
                ? r.RoadKnown ? Math.Max(soon, RoadFloor * heard) : heard
                : Math.Max(averageWork * full, 0.5f * heard);
            if (call == FiremanCall.HeavyAhead)
                plan = Math.Max(plan, HeavyShare * most);
            if (startComing)
                plan = Math.Max(plan, StartShare * most);
            return plan;
        }

        /// <summary>
        /// Learns what the fire really gives from the gauge, when the fire is
        /// as he wants it (<paramref name="atMark"/>): with the work steady, a
        /// needle that will be short of his pressure a minute on means it gives
        /// less than he reckoned, and over it, more.
        /// </summary>
        protected void LearnFromGauge(float dt, FiremanReadings r, Judgement j, bool atMark, bool lifting, bool smoking)
        {
            if (SteadyFor >= SteadyWorkTime && atMark && !lifting && !smoking
                && PlannedSteam > WorkingShare * j.Most && call == FiremanCall.None && !StopComing)
            {
                var foreseen = PressureTarget - r.Pressure - GaugeLook * PressureTrend;
                Trim = Clamp(Trim + TrimRate * foreseen * dt, MinTrim, MaxTrim);
            }
        }

        /// <summary>The water: the glass and its trend, the injector or pump, and blowdowns.</summary>
        protected void Water(float dt, FiremanReadings r, Judgement j, ref FiremanActions actions)
        {
            // He reads the glass through its bobbing and watches which way it
            // goes, as the water would stand level: he knows the grade he is on.
            var level = r.Glass - r.GlassTilt;
            if (float.IsNaN(seenGlass))
                seenGlass = level;
            var before = seenGlass;
            seenGlass += (level - seenGlass) * Math.Min(1f, dt / GlassReadTime);
            glassTrend += ((seenGlass - before) / dt - glassTrend) * Math.Min(1f, dt / TrendTime);

            var hardAhead = call == FiremanCall.HeavyAhead || j.Building || r.WorkAhead > SacrificeFromWork && Mode == FiremanMode.Road;
            var goal = hardAhead ? GlassBeforeWork : GlassGoal;
            var struggling = r.Pressure < PressureTarget - SagDeficit;
            var short_ = PressureTarget - r.Pressure - SacrificeFrom;
            if (HeardWork > SacrificeFromWork && short_ > 0f)
                goal += (GlassLeast - goal) * Clamp(short_ / (SacrificeOver - SacrificeFrom), 0f, 1f);
            if (call == FiremanCall.EasingOff || StopComing)
                goal = GlassEasing;
            var quiet = HeardWork <= WorkingShare;
            if (quiet && r.Pressure >= r.SafetyValve - SoakMargin)
                soaking = true;
            else if (!quiet || r.Pressure < r.SafetyValve - SoakRelease)
                soaking = false;
            if (soaking)
                goal = Math.Max(goal, GlassSoak);
            // The glass in sight on the road ahead, the crown sheet covered first.
            var up = Math.Max(0f, Math.Max(r.GlassTilt, r.GlassTiltHigh));
            var down = Math.Min(0f, Math.Min(r.GlassTilt, r.GlassTiltLow));
            goal = Math.Max(Math.Min(goal, GlassTop - up), GlassLeast - down);
            // Where he holds it as the glass reads it here.
            GlassTarget = goal + r.GlassTilt;

            // Once the engine has run a while with steam on, a fitted pump does
            // the feeding, light work or hard; standing or drifting, the
            // injector. Running light, the heard work alone shut a warm pump
            // (a player, 2026-10-16).
            var steamOn = !r.Standing && r.Throttle >= ShutThrottle;
            steamOnFor = steamOn ? steamOnFor + dt : 0f;
            steamOffFor = steamOn ? 0f : steamOffFor + dt;
            pumpRest = Math.Max(0f, pumpRest - dt);
            if (r.HasPump && (r.Pump == FeedPumpState.Tripped || r.Pump == FeedPumpState.LostSuction))
                pumpRest = PumpRest;
            var usePump = r.HasPump && pumpRest <= 0f && (onPump ? steamOffFor < PumpDrop : steamOnFor >= PumpAfter);
            if (usePump != onPump)
            {
                onPump = usePump;
                feedLever = pumpLever = float.NaN;
            }

            // He turns a feed down before he shuts it: only with it at its
            // least and the glass still high does it go off.
            feedingFor += dt;
            var stray = 3f * GlassBand;
            var least = onPump ? PumpLeast
                : r.InjectorLeast > 0f ? Math.Min(1f, r.InjectorLeast + LeastMargin)
                : InjectorSpec.OpenThreshold + 0.02f;
            var turnedDown = onPump
                ? float.IsNaN(pumpLever) || pumpLever <= least + 0.01f
                : r.Injector != InjectorState.Working || float.IsNaN(feedLever) || feedLever <= least + 0.01f;
            // Over his usual level more water is storage, not need: an injector
            // that will not turn down far enough to fill no faster than the fire
            // heats it waits for the pressure.
            var storing = seenGlass >= GlassGoal;
            var shortBy = PressureTarget - r.Pressure;
            if (!feeding && seenGlass < goal - GlassBand && (feedingFor >= LeastRest || seenGlass < goal - stray)
                && (!storing || shortBy < 0.5f * FillShortfall))
            {
                feeding = true;
                feedingFor = 0f;
            }
            else if (feeding && (seenGlass > goal + GlassBand && feedingFor >= LeastRun && turnedDown
                                 || seenGlass > goal + stray
                                 || storing && turnedDown && shortBy > FillShortfall))
            {
                feeding = false;
                feedingFor = 0f;
            }

            brokenRest = Math.Max(0f, brokenRest - dt);
            if (r.Injector == InjectorState.Broken)
                brokenRest = BrokenRest;
            var canFeed = feeding && r.TenderWater > 0f;
            if (onPump)
            {
                actions.Injector = 0f;
                actions.Pump = canFeed
                    ? Regulate(dt, r, goal, least, r.PumpFullFeed, ref pumpLever, ref pumpPerWork, hardAhead)
                    : 0f;
                if (actions.Pump <= 0f)
                {
                    pumpLever = float.NaN;
                    fillSteam = 0f;
                }
            }
            else if (r.InjectorSteamValve)
            {
                canFeed &= brokenRest <= 0f;
                // Water first, then steam to its priming notch; picked up,
                // the steam wide open and the feed regulated with the water
                // valve. Stopping, the steam shut and the water after it, so a
                // non-lifter runs none out of the overflow.
                var start = Math.Max(least, StartWater);
                var watered = r.InjectorLever > InjectorSpec.OpenThreshold + 0.05f;
                switch (r.Injector)
                {
                    case InjectorState.Off:
                    case InjectorState.NoWater when r.TenderWater > 0f:
                        var go = canFeed && r.Pressure >= r.InjectorStartPressure;
                        actions.Injector = go ? start : 0f;
                        actions.InjectorSteam = go && watered ? InjectorSpec.SteamPrime : 0f;
                        break;
                    case InjectorState.Priming:
                    case InjectorState.Hot:
                        actions.Injector = canFeed ? start : 0f;
                        actions.InjectorSteam = canFeed ? InjectorSpec.SteamPrime : 0f;
                        break;
                    case InjectorState.Working:
                        actions.Injector = canFeed
                            ? Regulate(dt, r, goal, least, r.InjectorFullFeed, ref feedLever, ref leverPerWork, hardAhead)
                            : 0f;
                        actions.InjectorSteam = actions.Injector > 0f ? 1f : 0f;
                        break;
                    default:
                        // Low pressure, a dry tender, or a broken jet: steam shut, then the water.
                        actions.InjectorSteam = 0f;
                        actions.Injector = r.InjectorSteam < InjectorSpec.SteamPrimeFrom ? 0f : r.InjectorLever;
                        break;
                }
                if (r.Injector != InjectorState.Working || actions.Injector <= 0f)
                {
                    feedLever = float.NaN;
                    fillSteam = 0f;
                }
            }
            else
            {
                canFeed &= brokenRest <= 0f;
                var crack = 0.5f * r.InjectorCrack;
                switch (r.Injector)
                {
                    case InjectorState.Off:
                        actions.Injector = canFeed && r.Pressure >= r.InjectorStartPressure ? crack : 0f;
                        break;
                    case InjectorState.Priming:
                    case InjectorState.Hot:
                        actions.Injector = canFeed ? crack : 0f;
                        break;
                    case InjectorState.Working:
                        actions.Injector = canFeed
                            ? Regulate(dt, r, goal, least, r.InjectorFullFeed, ref feedLever, ref leverPerWork, hardAhead)
                            : 0f;
                        break;
                    default:
                        // Low pressure, a dry tender, or a broken jet: shut it.
                        actions.Injector = 0f;
                        break;
                }
                if (r.Injector != InjectorState.Working || actions.Injector <= 0f)
                {
                    feedLever = float.NaN;
                    fillSteam = 0f;
                }
            }

            // Blowdown in bursts while the solids are high and the glass has water to spare.
            if (r.Solids >= BlowdownFrom)
                blowingDown = true;
            else if (r.Solids <= BlowdownTo)
                blowingDown = false;
            var blow = blowingDown && seenGlass > Math.Max(goal, GlassGoal) && !struggling && r.Pressure > BlowdownLeastPressure;
            if (burstLeft <= 0f && blow && (sinceBurst += dt) >= BurstShut)
                burstLeft = BurstOpen;
            if (burstLeft > 0f)
            {
                actions.Blowdown = blow ? 1f : 0f;
                burstLeft = blow ? burstLeft - dt : 0f;
                if (burstLeft <= 0f)
                    sinceBurst = 0f;
            }
            else if (!blow)
            {
                sinceBurst += dt;
            }
        }

        /// <summary>
        /// A feed's lever while it works, as a fireman sets it: from the glass
        /// and which way it is going, moved a little at each look, and at once
        /// by what he has learned the work takes when he hears it change.
        /// </summary>
        private float Regulate(float dt, FiremanReadings r, float goal, float least, float fullFeed,
            ref float lever, ref float perWork, bool storing)
        {
            var full = Math.Max(0.1f, fullFeed);
            var glassMass = r.GlassMass > 0f ? r.GlassMass : UsualGlassMass;
            if (float.IsNaN(perWork))
                perWork = r.FullWorkSteam > 0f ? r.FullWorkSteam / full : 0.5f;
            // The trend that brings the glass to his goal over a couple of
            // minutes, filling no faster than the fire can heat the water.
            var wantTrend = (goal - seenGlass) / ApproachTime;
            // And the heat that water takes, for no more than the feed can deliver.
            fillSteam = storing ? Math.Min(Math.Max(0f, wantTrend) * glassMass, full) * FeedWarming / SteamHeat : 0f;
            if (wantTrend > 0f && seenGlass > GlassLeast)
                wantTrend *= Clamp(1f - (PressureTarget - r.Pressure) / FillShortfall, 0f, 1f);
            if (float.IsNaN(lever))
            {
                // Just started: open it to what the work he hears takes, and the glass's shortfall.
                lever = perWork * HeardWork + wantTrend * glassMass / full;
                workAtLever = HeardWork;
                sinceGlance = 0f;
            }
            if (Math.Abs(HeardWork - workAtLever) > WorkStep)
            {
                lever += perWork * (HeardWork - workAtLever);
                workAtLever = HeardWork;
            }
            sinceGlance += dt;
            if (sinceGlance >= GlanceEvery)
            {
                sinceGlance = 0f;
                lever += GlanceShare * (wantTrend - glassTrend) * glassMass / full;
            }
            lever = Clamp(lever, least, 1f);

            // Steady water at steady work: that is what the work takes.
            if (Math.Abs(glassTrend) < SteadyTrend && HeardWork > LearnFromWork && lever > least && lever < 1f)
                perWork += (lever / HeardWork - perWork) * Math.Min(1f, dt / LearnTime);
            return lever;
        }

        /// <summary>What every fireman says: the water, and the tender.</summary>
        protected static FiremanNotice WaterNotices(FiremanReadings r)
        {
            var notices = FiremanNotice.None;
            if (r.Glass <= LowGlass)
                notices |= FiremanNotice.LowWater;
            if (r.TenderWater <= 0f)
                notices |= FiremanNotice.TenderWaterOut;
            return notices;
        }

        protected static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
