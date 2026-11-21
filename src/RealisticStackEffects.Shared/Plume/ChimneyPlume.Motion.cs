using System.Collections.Generic;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
    internal sealed partial class ChimneyPlume
    {
        // The widest a particle is drawn <paramref name="above"/> m from the
        // lip's middle (below 0, in the stack): its shown disk no wider than
        // the stack in it, and above it reaching no lower than the lip beside
        // the stack.
        private float Widest(float above) =>
            2f * (above <= 0f ? outsideRadius : Mathf.Sqrt(outsideRadius * outsideRadius + above * above)) / VisibleShare;

        private const float NeckReach = 10f;
        private const float FarSpread = 0.5f * PlumeRise.WidthPerHeight;

        private float Least(float above) => Spreading(above, Look.LipWidth);

        // The width, drawn, of what leaves the lip <paramref name="lip"/> ×
        // the bore wide and spreads as the exhaust does <paramref name="above"/> m over it.
        private float Spreading(float above, float lip)
        {
            var neck = Look.Neck * 2f * boreRadius;
            var near = Mathf.Clamp(above - neck, 0f, NeckReach);
            var far = Mathf.Max(0f, above - neck - NeckReach);
            return (lip * 2f * boreRadius + 2f * Look.NeckSpread * near + 2f * FarSpread * far) / VisibleShare;
        }

        // Cinders are heavy for their size: light drag toward still air and
        // full gravity, so they arc up and fall back (PlumeModel.SparkDrag).
        private void Fly(ParticleSystem system, float dt)
        {
            if (system == null)
                return;
            var count = Particles(system);
            if (count == 0)
                return;
            var keep = Mathf.Exp(-PlumeModel.SparkDrag * dt);
            var fall = Vector3.down * (PlumeModel.Gravity * dt);
            var local = system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            var wind = local
                ? system.transform.InverseTransformDirection(airVelocity - stackVelocity)
                : airVelocity;
            var up = local ? system.transform.InverseTransformDirection(Vector3.up) : Vector3.up;
            var hasCap = RoofLevel(system, up, out var cap);
            for (var i = 0; i < count; i++)
            {
                var velocity = wind + (buffer[i].velocity - wind) * keep + fall;
                // A cinder that strikes a tunnel's roof falls back off it.
                var level = Vector3.Dot(buffer[i].position, up);
                var upward = Vector3.Dot(velocity, up);
                if (hasCap && level > cap)
                {
                    buffer[i].position += up * (cap - level);
                    if (upward > 0f)
                        velocity -= up * ((1f + CinderBounce) * upward);
                }
                buffer[i].velocity = velocity;
            }
            UploadParticles(system, count);
        }

        // In the narrow-jet zone: below its top along the stack's axis, and
        // near the axis. By height alone, smoke the train had left behind
        // below the stack's level (past a crest, or where the line falls
        // away) was taken back into the zone: carried along with the train
        // and never retired until it rose back above the stack (loady740,
        // Nexus, 2026-10-27: a line of puffs left in the air).
        private static bool InZone(Vector3 position, Vector3 emitter, Vector3 axis, float zone)
        {
            var offset = position - emitter;
            var along = Vector3.Dot(offset, axis);
            return along < zone && (offset - axis * along).sqrMagnitude < ZoneReach * ZoneReach;
        }

        private int Particles(ParticleSystem system)
        {
            using var timing = StackHost.TimePhase(PlumePhase.Buffers);
            var capacity = system.main.maxParticles;
            if (buffer.Length < capacity)
                buffer = new ParticleSystem.Particle[capacity];
            return system.GetParticles(buffer);
        }

        private void UploadParticles(ParticleSystem system, int count)
        {
            using var timing = StackHost.TimePhase(PlumePhase.Buffers);
            system.SetParticles(buffer, count);
        }

        // In the narrow-jet zone a particle is held thin and carried with the
        // stack: each frame it takes the stack's current velocity plus its own
        // rise, so speeding up, braking, or curving cannot push it through the
        // stack's wall. Above the zone: linear drag toward still air plus the
        // plume's rise at its height (PlumeRise), so a puff stops after its
        // launch speed over the drag (the jet length for the hardest beat)
        // and then rises with the plume; the stack's velocity it carried
        // decays the same way, so the plume trails behind a moving train. The
        // cloud and sooty core are carried as the plume (PlumeAloft): wide,
        // fading, and fewer as they rise. A system in the stack's own space
        // (the jet) moves with the stack already, and the still air there
        // blows back at the train's speed.
        private void Drift(ParticleSystem system, float dt, bool lift)
        {
            if (system == null)
                return;
            var count = Particles(system);
            if (count == 0)
                return;

            var keep = Mathf.Exp(-PlumeModel.JetDrag(tuning) * dt);
            var space = system.transform;
            var local = system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            var up = local ? space.InverseTransformDirection(Vector3.up) : Vector3.up;
            var flux = lift ? stackFlux : 0f;
            // The air the smoke drifts in: the weather's wind (and a test's
            // train speed); past the stack, the wind less the train's own speed.
            var wind = airVelocity;
            var past = wind - stackVelocity;
            var airSpeed = new Vector2(past.x, past.z).magnitude;
            var sun = Sun();
            var toSun = sun != null && sun.isActiveAndEnabled ? -sun.transform.forward : Vector3.zero;
            // Where the plume is seen from, for its silhouette (a capture's held view first).
            var eyeCamera = PlayerManager.ActiveCamera;
#if PUBLIC_RELEASE
            var eye = eyeCamera != null ? eyeCamera.transform.position : (Vector3?)null;
#else
            var eye = PlumeCapture.HeldAt ?? (eyeCamera != null ? eyeCamera.transform.position : (Vector3?)null);
#endif
            var axis = local ? Vector3.up : space.up;
            var carried = local ? Vector3.zero : stackVelocity;
            var air = local ? space.InverseTransformDirection(past) : wind;
            var worldWind = PlumeWeather.Wind;
            var simWind = local ? space.InverseTransformDirection(worldWind) : worldWind;
            var zone = stackLip.HasValue
                ? Mathf.Min(Mathf.Max(0f, stackLip.Value) + LipMargin, Look.JetZone)
                : Look.JetZone;
            var emitter = SimulationPoint(system, Emitter(system));
            var zoneTop = emitter + axis * zone;
            if (aloft.TryGetValue(system, out var plume))
            {
                var ageScale = 1f;
                if (puffMaterials.TryGetValue(system, out var puffMaterial) && puffMaterial != null && puffMaterial.HasProperty(PuffAgeScaleId))
                {
                    ageScale = AsParcels
                        ? 1f
                        : PlumeRise.PuffAgeScale(wispTrainSpeed, plume.PlayingTime, Mathf.Max(Look.WispAgeTo, Look.ThinWispAgeTo), Look.PuffTrail);
                    puffMaterial.SetFloat(PuffAgeScaleId, ageScale);
                }
                Carry(system, plume, count, new PlumeAloft.Air
                {
                    Up = up,
                    Still = air,
                    Drag = PlumeModel.JetDrag(tuning),
                    Elapsed = dt,
                    Period = passPeriod,
                    Flux = flux,
                    Speed = airSpeed,
                    Train = carried,
                    Wind = simWind,
                    InversionCeiling = PlumeWeather.InversionCeiling,
                    InversionStrength = PlumeWeather.InversionStrength,
                    TrainSpeed = wispTrainSpeed,
                    ExitSpeed = exitSpeed,
                    Cloud = steamCloud,
                    Mixing = tuning.SteamMixing,
                    Forming = tuning.SteamForming,
                    WakeCarry = tuning.WakeCarry,
                    WakeDepth = tuning.WakeDepth,
                    JetWidth = Look.JetWidth,
                    Fray = Look.Fray,
                    Eddies = Look.Eddies,
                    RiseShown = Look.RiseShown,
                    Spread = tuning.TrailSpread,
                    FillWidth = Look.FillWidth,
                    SmokeFillWidth = Look.SmokeFillWidth,
                    Disperse = Look.Disperse,
                    Overlap = Look.Overlap,
                    SmokeAsSteamFrom = Look.SmokeAsSteamFrom,
                    WispLargestFrom = Look.WispLargestFrom,
                    WispLargestTo = Look.WispLargestTo,
                    WispShare = Look.WispShare,
                    WispAgeFrom = WispAgeFrom * ageScale,
                    WispAgeTo = WispAgeTo * ageScale,
                    WispFrayEdge = Look.WispFrayEdge,
                    WispBelow = Look.WispBelow,
                    WispAbove = Look.WispAbove,
                    ShapeAcross = Look.ShapeAcross > 0.5f,
                    ShapeSmoothing = Look.ShapeSmoothing,
                    SmokeAsSteamTo = Look.SmokeAsSteamTo,
                    Shape = Look.PlumeShape > 0f,
                    ToSun = local ? space.InverseTransformDirection(toSun) : toSun,
                    HasEye = eye.HasValue,
                    Eye = eye.HasValue ? (local ? space.InverseTransformPoint(eye.Value) : eye.Value) : Vector3.zero,
                    ShadowDensity = Look.SunShadow > 0f ? Look.ShadowDensity : 0f,
                    BillowAge = Look.BillowAge,
                    Puffs = rollingPuffs,
                    RollRate = Look.RollRate,
                    RollSlows = Look.RollSlows,
                    Roof = SameSpace(system, smoke) ? roof : null,
                    InTunnel = roof.OverStack.HasValue,
                    Faint = SmokeDetail.Faint(StackHost.Quality),
                    Floor = Vector3.Dot(zoneTop, up),
                    Parcels = Look.Parcels,
                    Bore = 2f * boreRadius,
                    BeatHard = beatHard,
                    BlastSpread = Look.ParcelBlast,
                    Roll = Look.ParcelRoll,
                    MixedBelow = Look.ParcelMixedBelow,
                    MixedAbove = Look.ParcelMixedAbove,
                    JetLength = tuning.JetLength,
                    Neck = Look.Neck * 2f * boreRadius,
                    WhiteByHeight = tuning.WhiteByHeight,
                    Lump = Look.ParcelLump,
                    LeastLump = Look.ParcelLeast,
                    LumpsAcross = Look.ParcelsAcross,
                    ParcelArea = Look.ParcelArea,
                    LightSmoke = Look.ParcelLightSmoke,
                    DenseSmoke = Look.ParcelDenseSmoke,
                }, emitter, axis, zone, zoneTop, carried);
                return;
            }
            var hasCap = RoofLevel(system, up, out var cap);
            var growth = system.sizeOverLifetime.enabled ? system.sizeOverLifetime.size : new ParticleSystem.MinMaxCurve(1f);
            if (!intendedSizes.TryGetValue(system, out var sizes))
                intendedSizes[system] = sizes = new SeedMemory<float>();
            var streaks = system == jet && jetVelocityScale > 0f;
            for (var i = 0; i < count; i++)
            {
                var particle = buffer[i];
                var intended = sizes.Recall(particle.randomSeed, particle.startSize);
                var life = particle.startLifetime > 0f ? 1f - particle.remainingLifetime / particle.startLifetime : 0f;
                var shown = intended * growth.Evaluate(life);
                var velocity = streaks ? jetVelocities.Recall(particle.randomSeed, particle.velocity) : particle.velocity;
                var inZone = InZone(particle.position, emitter, axis, zone);
                float drawn;
                if (inZone)
                {
                    drawn = Mathf.Min(shown, Widest(0f));
                    var upward = Mathf.Max(Vector3.Dot(velocity - carried, axis), MinZoneRise);
                    velocity = carried + axis * upward;
                }
                else
                {
                    drawn = Mathf.Min(system == heat ? Mathf.Max(shown, HotWidth(particle, zoneTop, up, airSpeed)) : shown,
                        Widest(Vector3.Distance(particle.position, zoneTop)));
                    var rise = PlumeRise.RiseSpeed(flux, Vector3.Dot(particle.position - zoneTop, up), airSpeed);
                    var target = air + up * rise;
                    velocity = target + (velocity - target) * keep;
                    // A tunnel's roof stops it.
                    var level = Vector3.Dot(particle.position, up);
                    if (hasCap && level > cap)
                        particle.position += up * (cap - level);
                    var upward = Vector3.Dot(velocity, up);
                    if (hasCap && level >= cap - RoofBand && upward > 0f)
                        velocity -= up * upward;
                }
                particle.startSize = shown > 1e-4f ? intended * drawn / shown : intended;

                if (system == heat)
                {
                    var strength = heatStrengths.Recall(particle.randomSeed, ((Color)particle.startColor).a);
                    var born = Mathf.Max(1e-3f, intended * growth.Evaluate(0f));
                    var diffused = strength * Mathf.Pow(Mathf.Min(1f, born / Mathf.Max(born, drawn)), Look.ShimmerDiffuse);
                    particle.startColor = new Color(1f, 1f, 1f, diffused);
                }

                if (system == jet && jetParcels.TryRecall(particle.randomSeed, out var jetParcel))
                {
                    var age = Mathf.Max(0f, particle.startLifetime - particle.remainingLifetime);
                    particle.startColor = JetColor(jetParcel, age, Vector3.Dot(particle.position - zoneTop, up));
                }

                if (streaks)
                {
                    // Inside the stack a streak runs straight down it. Above,
                    // it points back at the mouth, and the particle is moved
                    // by the difference so it still travels by its true velocity.
                    var drawnVelocity = velocity;
                    if (!inZone)
                    {
                        var toMouth = particle.position - zoneTop;
                        var room = Mathf.Max(0f, toMouth.magnitude - jetLengthScale * drawn);
                        var length = Mathf.Min(jetVelocityScale * velocity.magnitude, room);
                        drawnVelocity = toMouth.normalized * (length / jetVelocityScale);
                    }
                    particle.position += (velocity - drawnVelocity) * dt;
                    particle.velocity = drawnVelocity;
                    jetVelocities.Keep(particle.randomSeed, velocity);
                }
                else
                {
                    particle.velocity = velocity;
                }
                buffer[i] = particle;
            }
            sizes.Forget();
            if (streaks)
                jetVelocities.Forget();
            if (system == jet)
                jetParcels.Forget();
            if (system == heat)
                heatStrengths.Forget();
            UploadParticles(system, count);
        }

        // The width the hot gas has spread to at a shimmer particle: the lip's
        // (× the shimmer's width) spreading as the exhaust does with its
        // height, and by its age's turbulence, as the smoke's plume.
        private float HotWidth(ParticleSystem.Particle particle, Vector3 zoneTop, Vector3 up, float airSpeed)
        {
            var height = Mathf.Max(0f, Vector3.Dot(particle.position - zoneTop, up));
            var age = Mathf.Max(0f, particle.startLifetime - particle.remainingLifetime);
            var plume = PlumeRise.PlumeWidth(height, PlumeRise.SpreadWidth(age, airSpeed, tuning.TrailSpread)) * HotPlumeShare;
            return Look.ShimmerSize * Mathf.Max(Spreading(height, 1f), plume / VisibleShare);
        }

        // The hot core's share of the plume's visible width.
        private const float HotPlumeShare = 0.5f;

        // The cloud's and sooty core's particles: held thin in the narrow-jet
        // zone and moved with the stack as Drift does there; above it carried
        // as the plume.
        private void Carry(ParticleSystem system, PlumeAloft plume, int count, PlumeAloft.Air air,
            Vector3 emitter, Vector3 axis, float zone, Vector3 zoneTop, Vector3 carried)
        {
            var compact = Look.StableParticleData > 0.5f;
            var kept = 0;
            if (count > SmokeCounts)
                StackHost.SmokeInTheAir();
            using (StackHost.TimePhase(PlumePhase.Carry))
            {
                plume.Begin(air, count);
                for (var i = 0; i < count; i++)
                {
                    // In its place in the buffer: copied out and back, 132
                    // bytes each way for every particle every pass.
                    ref var particle = ref buffer[i];
                    var inZone = InZone(particle.position, emitter, axis, zone);
                    if (inZone)
                    {
                        var upward = Mathf.Max(Vector3.Dot(particle.velocity - carried, axis), MinZoneRise);
                        particle.velocity = carried + axis * upward;
                    }
                    if (plume.Turn(ref particle, inZone, i))
                    {
                        // From the lip's middle: below it in the stack, above it out;
                        // its least width by its height over the lip.
                        var height = Vector3.Dot(particle.position - zoneTop, air.Up);
                        var above = inZone ? Vector3.Dot(particle.position - zoneTop, axis) : Vector3.Distance(particle.position, zoneTop);
                        plume.Work(ref particle, height, Least(inZone ? above : Mathf.Max(0f, height)), Widest(above), inZone, above);
                    }
                    // SetParticles drops the dead and moves the last survivors
                    // into their places, but the custom data written after it
                    // goes by place: a moved particle drew a dead one's wisp, turn
                    // and age for a frame (Sol, 2026-10-23). Dropped here first,
                    // each keeps its own.
                    if (compact && particle.remainingLifetime <= 0f)
                        continue;
                    plume.Keep(kept);
                    if (kept != i)
                        buffer[kept] = particle;
                    kept++;
                }
                count = kept;
                plume.End();
            }
            plumeTop = Mathf.Max(plumeTop, plume.Top);
            UploadParticles(system, count);
            using (StackHost.TimePhase(PlumePhase.CustomData))
            {
                // The values by place, a place for each particle the system
                // now holds: its dead dropped above, as many as were sent.
                if (compact)
                {
                    Size(shapeData, count);
                    Size(extraData, count);
                }
                else
                {
                    system.GetCustomParticleData(shapeData, ParticleSystemCustomData.Custom1);
                    system.GetCustomParticleData(extraData, ParticleSystemCustomData.Custom2);
                }
                var wisps = rollingPuffs > 0;
                // Rolling steam's wisp is one of the wisp flipbook's rows (x,
                // rounded; its fraction its own start in the row × 0.49), at
                // its own turn (y), how hard its beat was (z), its frames by
                // age, played over the system's own lifetime as the flipbook
                // was (PlumeAloft), the last held: its share of that time (w).
                var longFrom = Mathf.Min(Look.LongPuffsFrom, Look.LongPuffsBy - 0.01f);
                var filled = Mathf.Min(count, Mathf.Min(shapeData.Count, extraData.Count));
                for (var i = 0; i < filled; i++)
                {
                    var record = plume.At(i);
                    var age = buffer[i].startLifetime - buffer[i].remainingLifetime;
                    if (wisps)
                    {
                        var hard = Mathf.SmoothStep(0f, 1f, (plume.HardOf(record, beatHard) - longFrom) / (Look.LongPuffsBy - longFrom));
                        shapeData[i] = plume.WispOf(record, age, Look.WispSpin, Look.WispSpinEase, hard);
                    }
                    else if (air.Shape)
                    {
                        shapeData[i] = record.Shape;
                    }
                    extraData[i] = plume.ExtraOf(record, age, buffer[i].position);
                }
                if (wisps || air.Shape)
                    system.SetCustomParticleData(shapeData, ParticleSystemCustomData.Custom1);
                system.SetCustomParticleData(extraData, ParticleSystemCustomData.Custom2);
            }
            volume?.Gather(system, buffer, count);
        }

        // A list of <paramref name="count"/> values, whatever it held.
        private static void Size(List<Vector4> list, int count)
        {
            if (list.Count > count)
                list.RemoveRange(count, list.Count - count);
            while (list.Count < count)
                list.Add(default);
        }

        private PlumeAloft Aloft(ParticleSystem system) => system != null && aloft.TryGetValue(system, out var plume) ? plume : null;

        // The roof over the stack on <paramref name="up"/> in the system's
        // own space, a little under it; false in the open.
        private bool RoofLevel(ParticleSystem system, Vector3 up, out float level)
        {
            level = 0f;
            if (!(roof.OverStack is TunnelRoof.Sample over))
                return false;
            var world = WorldPoint(smoke, over.Point);
            level = Vector3.Dot(SimulationPoint(system, world), up) - RoofBand;
            return true;
        }

        private static bool SameSpace(ParticleSystem a, ParticleSystem b)
        {
            var one = a.main;
            var other = b.main;
            return one.simulationSpace == other.simulationSpace
                   && (one.simulationSpace != ParticleSystemSimulationSpace.Custom || one.customSimulationSpace == other.customSimulationSpace)
                   && (one.simulationSpace != ParticleSystemSimulationSpace.Local || a.transform == b.transform);
        }

        // A point in the space the system simulates in, back in the world.
        internal static Vector3 WorldPoint(ParticleSystem system, Vector3 point)
        {
            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.Local:
                    return system.transform.TransformPoint(point);
                case ParticleSystemSimulationSpace.Custom when main.customSimulationSpace != null:
                    return main.customSimulationSpace.TransformPoint(point);
                default:
                    return point;
            }
        }

        // A world point in the space the system simulates in (the game moves
        // the world origin, so the stock smoke simulates in a custom space).
        internal static Vector3 SimulationPoint(ParticleSystem system, Vector3 world)
        {
            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.Local:
                    return system.transform.InverseTransformPoint(world);
                case ParticleSystemSimulationSpace.Custom when main.customSimulationSpace != null:
                    return main.customSimulationSpace.InverseTransformPoint(world);
                default:
                    return world;
            }
        }

        // The game's smoke emitter in world space (its shape's point). Its depth
        // inside the stack differs between locos, so smoke starts here and the
        // narrow-jet zone (see Drift) keeps it inside the stack.
        internal static Vector3 Emitter(ParticleSystem system) =>
            system.transform.TransformPoint(system.shape.position);
    }
}
