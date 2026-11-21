using System;

namespace RealisticFiring.Core
{
    /// <summary>How the cab's gauge glass behaves, for the next-step advice.</summary>
    public enum GlassCheck
    {
        /// <summary>Clear and lively, or not applicable.</summary>
        Clear,

        /// <summary>Dirty water and the glass has not been blown through in a while.</summary>
        Due,

        /// <summary>The lower passage is choking: the glass moves sluggishly.</summary>
        Sluggish,

        /// <summary>Heavily choked: the glass is sticking and cannot be trusted.</summary>
        Stuck,
    }

    /// <summary>What the gauge glass sees this tick, from the boiler and its own cocks.</summary>
    public struct GaugeGlassChokeInputs
    {
        /// <summary>True water surface at the backhead (true level + swell + foam), in boiler fractions.</summary>
        public float SurfaceLevel;

        /// <summary>Visible gauge glass span in boiler fractions (e.g. S282 0.63 to 0.96).</summary>
        public float GlassBottom;
        public float GlassTop;

        /// <summary>Whether choking can accumulate (false when water impurities are off or in Relaxed).</summary>
        public bool ImpuritiesEnabled;

        /// <summary>Dissolved solids and suspended sludge in the boiler, in ppm.</summary>
        public float FoamingConcentration;

        /// <summary>Suspended sludge in the boiler, in ppm.</summary>
        public float SuspendedSludgePpm;

        /// <summary>Boiler water foaming intensity, 0 still to 1 foaming freely.</summary>
        public float Foam;

        /// <summary>How hard the boiler is worked, 0 standing to 1 full steam.</summary>
        public float Work;

        /// <summary>Boiler pressure, bar absolute.</summary>
        public float Pressure;

        /// <summary>Whether the boiler is steaming (water at boiling point with steam above it).</summary>
        public bool Boiling;

        /// <summary>Glass bottom blow-through cock opening, 0 shut to 1 wide open.</summary>
        public float BottomCock;

        /// <summary>Glass top blow-through cock opening, 0 shut to 1 wide open.</summary>
        public float TopCock;
    }

 public sealed class GaugeGlassChoke
    {
        /// <summary>Foaming concentration below which clean water does not choke the passage, in ppm.</summary>
        public const float ChokeOnsetPpm = 1000f;

        /// <summary>Reference full-choke concentration, in ppm.</summary>
        public const float ChokeFullPpm = BoilerWater.FoamFullPpm;

        /// <summary>Choke growth rate per second at unit impurity drive (~60 min at unit drive; ~60–90 min in dirty water, ~35–40 min with active foaming).</summary>
        public const float MaxChokeRate = 1f / 3600f;

        /// <summary>Choke below this passes freely with no damping.</summary>
        public const float ClearBand = 0.05f;

        /// <summary>Choke at or above this makes the glass noticeably sluggish.</summary>
        public const float SluggishThreshold = 0.25f;

        /// <summary>Choke at or above this stalls the column and lets condensation creep the reading high.</summary>
        public const float StuckThreshold = 0.70f;

        /// <summary>Seconds steaming on dirty water without blowing through before advice reminds the crew (20 minutes).</summary>
        public const float BlowReminderTime = 1200f;

        // Clearing under boiler pressure: bottom cock flushes the lower passage
        // directly (about 1.7 s wide open at working pressure clears a full plug);
        // the top cock helps from the steam side at a smaller share.
        public const float FlushRate = 0.6f;
        private const float TopFlushShare = 0.4f;
        private const float Atmosphere = 1.01325f;
        private const float FullFlushGaugeBar = 4f;
        private const float MinFlushShare = 0.01f;

        /// <summary>Absolute boiler pressure (bar) required before blowing the glass through can flush choking (~0.04 bar gauge).</summary>
        public const float BlowMinPressure = Atmosphere + MinFlushShare * FullFlushGaugeBar;

        /// <summary>Whether boiler pressure (bar absolute) is high enough for blowing the glass through to flush choking.</summary>
        public static bool CanBlowThrough(float boilerPressure) => Smoothing.Finite(boilerPressure, 1f) >= BlowMinPressure;

        // Blow-through drawdown and rebound: brisk when clear, sluggish while choked.
        private const float DrawDown = 3f;
        private const float TopDrawShare = 0.5f;
        public const float ClearFallTime = 0.25f;
        public const float ChokedFallTime = 1.2f;
        public const float ClearReboundTime = 0.45f;
        public const float ChokedReboundTime = 4.5f;

        // Steam condensing on the glass walls while the lower passage is stuck,
        // in boiler fractions per second (~0.007 per minute at full choke).
        public const float CondenseRate = 0.00012f;

        private const float DefaultGlassBottom = 0.6f;
        private const float DefaultGlassTop = 0.96f;

        private float columnLevel = float.NaN;
        private float shownLevel = float.NaN;
        private float drawdown;

        /// <summary>Obstruction of the lower glass passage, 0 clear to 1 blocked.</summary>
        public float Choke { get; private set; }

        /// <summary>Whether the glass is noticeably damped.</summary>
        public bool IsSluggish => Choke >= SluggishThreshold;

        /// <summary>Whether the glass is heavily choked and prone to reading falsely high.</summary>
        public bool IsStuck => Choke >= StuckThreshold;

        /// <summary>Whether <see cref="ColumnLevel"/> and <see cref="ShownLevel"/> have been initialized.</summary>
        public bool HasLevel => !float.IsNaN(columnLevel);

        /// <summary>Water level standing in the glass column (with choking lag/sticking, before temporary blow-through drawdown).</summary>
        public float ColumnLevel => columnLevel;

        /// <summary>Water level currently shown in the glass (including blow-through drawdown and rebound).</summary>
        public float ShownLevel => shownLevel;

        /// <summary>Whether the glass is currently being blown through or rebounding from a blow-through.</summary>
        public bool Blowing { get; private set; }

        /// <summary>Seconds steaming since the glass was last blown clear or washed out.</summary>
        public float TimeSinceBlowThrough { get; private set; }

        /// <summary>The blow-through drawdown fall time constant right now, in s: brisk when clear, sluggish when choked.</summary>
        public float FallTimeNow => ClearFallTime + (ChokedFallTime - ClearFallTime) * Choke;

        /// <summary>The rebound time constant right now, in s: brisk when clear, sluggish when choked.</summary>
        public float ReboundTimeNow =>
            ClearReboundTime + (ChokedReboundTime - ClearReboundTime) * (0.3f * Choke + 0.7f * Choke * Choke);

        /// <summary>Clears any choking (e.g. when water impurities are switched off) without discarding the column.</summary>
        public void Clear()
        {
            Choke = 0f;
            TimeSinceBlowThrough = 0f;
        }

        /// <summary>Washout or fresh boiler: clear passage and re-snap column to the true surface on the next tick.</summary>
        public void Reset()
        {
            Choke = 0f;
            TimeSinceBlowThrough = 0f;
            columnLevel = float.NaN;
            shownLevel = float.NaN;
            drawdown = 0f;
            Blowing = false;
        }

        /// <summary>Restores saved choking state, tolerating missing or out-of-range values.</summary>
        public void Restore(float choke, float savedColumnLevel, float timeSinceBlowThrough)
        {
            Choke = Smoothing.Clamp01(Smoothing.Finite(choke));
            columnLevel = !float.IsNaN(savedColumnLevel) && !float.IsInfinity(savedColumnLevel) ? savedColumnLevel : float.NaN;
            shownLevel = columnLevel;
            drawdown = 0f;
            TimeSinceBlowThrough = Math.Max(0f, Smoothing.Finite(timeSinceBlowThrough));
            Blowing = false;
        }

        /// <summary>Applies the host's mirrored choking state on a multiplayer guest.</summary>
        public void Mirror(float choke, float mirroredColumnLevel, float timeSinceBlowThrough, bool blowing)
        {
            Choke = Smoothing.Clamp01(Smoothing.Finite(choke));
            if (!float.IsNaN(mirroredColumnLevel) && !float.IsInfinity(mirroredColumnLevel))
                columnLevel = mirroredColumnLevel;
            TimeSinceBlowThrough = Math.Max(0f, Smoothing.Finite(timeSinceBlowThrough));
            Blowing = blowing;
            if (!blowing)
                drawdown = 0f;
            if (!float.IsNaN(columnLevel))
                shownLevel = columnLevel - drawdown;
        }

        /// <summary>How the glass looks to the fireman's advice, given whether impurities are active, current concentration, and absolute boiler pressure (bar).</summary>
        public GlassCheck Status(bool impuritiesEnabled, float foamingConcentration, float boilerPressure = Atmosphere + FullFlushGaugeBar)
        {
            if (!impuritiesEnabled || !CanBlowThrough(boilerPressure))
                return GlassCheck.Clear;
            if (IsStuck)
                return GlassCheck.Stuck;
            if (IsSluggish)
                return GlassCheck.Sluggish;
            if (Smoothing.Finite(foamingConcentration) >= ChokeOnsetPpm && TimeSinceBlowThrough >= BlowReminderTime)
                return GlassCheck.Due;
            return GlassCheck.Clear;
        }

        public void Step(float dt, GaugeGlassChokeInputs inputs)
        {
            if (!(dt > 0f) || float.IsNaN(dt) || float.IsInfinity(dt))
                return;

            var surface = Smoothing.Finite(inputs.SurfaceLevel);
            var glassBottom = inputs.GlassTop > inputs.GlassBottom + 1e-3f ? Smoothing.Finite(inputs.GlassBottom, DefaultGlassBottom) : DefaultGlassBottom;
            var glassTop = inputs.GlassTop > inputs.GlassBottom + 1e-3f ? Smoothing.Finite(inputs.GlassTop, DefaultGlassTop) : DefaultGlassTop;
            var span = Math.Max(1e-3f, glassTop - glassBottom);

            if (float.IsNaN(columnLevel))
            {
                columnLevel = shownLevel = surface;
                drawdown = 0f;
            }

            var bottom = Smoothing.Clamp01(Smoothing.Finite(inputs.BottomCock));
            var top = Smoothing.Clamp01(Smoothing.Finite(inputs.TopCock));
            var blow = bottom + TopFlushShare * top;
            var pressure = Smoothing.Finite(inputs.Pressure, 1f);
            var pressureShare = Smoothing.Clamp01((pressure - Atmosphere) / FullFlushGaugeBar);
            var flushing = blow > 0.01f && CanBlowThrough(pressure);

            if (!inputs.ImpuritiesEnabled)
            {
                Choke = 0f;
                TimeSinceBlowThrough = 0f;
                columnLevel = surface;
            }
            else
            {
                if (flushing)
                {
                    Choke = Math.Max(0f, Choke - FlushRate * Math.Min(1.2f, blow) * pressureShare * dt);
                    if (Choke <= 0.01f)
                        Choke = 0f;
                    if (Choke < ClearBand && blow * pressureShare > 0.05f)
                        TimeSinceBlowThrough = 0f;
                }
                else if (inputs.Boiling)
                {
                    var ppm = Math.Max(0f, Smoothing.Finite(inputs.FoamingConcentration));
                    var sludgePpm = Math.Max(0f, Smoothing.Finite(inputs.SuspendedSludgePpm));
                    var foam = Smoothing.Clamp01(Smoothing.Finite(inputs.Foam));
                    var work = Smoothing.Clamp01(Smoothing.Finite(inputs.Work));
                    var impurity = Smoothing.Clamp01((ppm - ChokeOnsetPpm) / (ChokeFullPpm - ChokeOnsetPpm));
                    var sludgeBoost = 0.6f + 0.4f * Smoothing.Clamp01(sludgePpm / 1000f);
                    var circulation = 0.5f + 0.5f * work;
                    var drive = impurity * sludgeBoost * circulation + 0.7f * foam;
                    Choke = Smoothing.Clamp01(Choke + drive * MaxChokeRate * dt);
                    TimeSinceBlowThrough += dt;
                }

                // Update the glass column level: free within ClearBand, damped
                // above it, tapering passage flow in both directions to zero at
                // full choke while steam condensing in the upper connection
                // slowly creeps the trapped column upward.
                if (Choke <= ClearBand)
                {
                    columnLevel = surface;
                }
                else
                {
                    var c = (Choke - ClearBand) / (1f - ClearBand);
                    if (flushing)
                    {
                        var flushLag = 0.3f + 2.5f * c;
                        columnLevel += (surface - columnLevel) * Math.Min(1f, dt / flushLag);
                    }
                    else
                    {
                        var stuckShare = Smoothing.Clamp01((Choke - StuckThreshold) / (1f - StuckThreshold));
                        var passFactor = (1f - stuckShare) * (1f - stuckShare);
                        var diff = surface - columnLevel;
                        if (diff >= 0f)
                        {
                            var upLag = 6f * c + 240f * c * c * c;
                            columnLevel += diff * passFactor * Math.Min(1f, dt / upLag);
                        }
                        else
                        {
                            var downLag = 12f * c + 480f * c * c * c;
                            columnLevel += diff * passFactor * Math.Min(1f, dt / downLag);
                        }

                        if (inputs.Boiling && Choke > StuckThreshold && columnLevel < glassTop)
                            columnLevel = Math.Min(glassTop, columnLevel + CondenseRate * stuckShare * dt);
                    }
                }
            }

            // Blow-through drawdown while a glass cock is open, then sharp (clear)
            // or sluggish (choked) rebound once shut. Track the residual drawdown
            // offset independently of ColumnLevel so rising or falling boiler water
            // never prevents Blowing from settling once the cocks are closed.
            var bottomEdge = glassBottom - 0.05f * span;
            var maxDrawdown = Math.Max(0f, columnLevel - bottomEdge);
            drawdown = Math.Min(drawdown, maxDrawdown);
            var drawn = Smoothing.Clamp01((bottom + TopDrawShare * top) * DrawDown);
            if (drawn > 0f)
            {
                float targetDrawdown;
                if (columnLevel <= glassBottom)
                {
                    targetDrawdown = 0f;
                }
                else
                {
                    var topEdge = Math.Min(columnLevel, glassTop + 0.05f * span);
                    var target = Math.Min(columnLevel, topEdge + (bottomEdge - topEdge) * drawn);
                    targetDrawdown = Math.Max(0f, columnLevel - target);
                }
                var fallTime = FallTimeNow;
                drawdown += (targetDrawdown - drawdown) * Math.Min(1f, dt / fallTime);
                drawdown = Math.Max(0f, Math.Min(drawdown, maxDrawdown));
                Blowing = true;
            }
            else if (Blowing)
            {
                var reboundTime = ReboundTimeNow;
                drawdown *= Math.Max(0f, 1f - Math.Min(1f, dt / reboundTime));
                if (drawdown < 0.002f * span)
                {
                    drawdown = 0f;
                    Blowing = false;
                }
            }
            shownLevel = columnLevel - drawdown;
        }
    }
}
