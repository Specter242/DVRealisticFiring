using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
    internal sealed partial class PlumeAloft
    {
        /// <summary>
        /// Finds <paramref name="particle"/>'s record (<see cref="Last"/>),
        /// and whether this pass is its turn to be worked out in full
        /// (<see cref="Work"/>, with its height and widths, which only a
        /// particle on its turn needs): by its age, or still fading in, or
        /// fading out, or first seen. Else the game moves it as it is.
        /// </summary>
        public bool Turn(ref ParticleSystem.Particle particle, bool inStack, int place)
        {
            var age = Mathf.Max(0f, particle.startLifetime - particle.remainingLifetime);
            var seed = particle.randomSeed;
            // Among those of its age, for the plume's middle there (Shape).
            if (shaping && !inStack)
            {
                var band = Mathf.Min(ShapeAges - 1, (int)(age / ShapeAgeStep));
                middleSum[band] += particle.position;
                middleAgeSum[band] += age;
                middleCount[band]++;
            }
            turnAge = age;
            // The record that stood at its place last pass is its own unless
            // a particle before it has gone since: then found by its seed.
            var record = place < placed.Length ? placed[place] : null;
            if (record != null && record.Seed == seed && seed != 0 || records.TryGetValue(seed, out record))
            {
                if (record.Cycle < cycle)
                    record.Cycle = cycle;
                Last = turnRecord = record;
                return age < FadeIn || record.Retiring || turns.Due(seed, age, turn);
            }
            else
            {
                // First seen (not emitted from the stack by the mod): worked out at once.
                record = Fresh(seed, new Parcel
                {
                    Thickness = PlumeRise.Parcel(((Color)particle.startColor).a, air.SmokeShare),
                    Color = particle.startColor,
                    SmokeColor = particle.startColor,
                    Size = particle.startSize,
                    Rate = emissionRate,
                });
                Last = turnRecord = record;
                if (!inStack)
                    particle.velocity += air.Carried;
                return true;
            }
        }

        /// <summary>
        /// Works out the particle <see cref="Turn"/> just found due,
        /// <paramref name="height"/> m above the stack and drawn no narrower
        /// than <paramref name="least"/> (the bore, at the lip) and no wider
        /// than <paramref name="widest"/>; still <paramref name="inStack"/>,
        /// only its size and fade (the caller moves it). False when it is
        /// retired.
        /// </summary>
        public bool Work(ref ParticleSystem.Particle particle, float height, float least, float widest, bool inStack, float away = 0f)
        {
            var age = turnAge;
            var seed = particle.randomSeed;
            var record = turnRecord;
            ref var parcel = ref record.Parcel;
            // Once a cycle, for the cycle's figures.
            var onTurn = turns.Counted(seed, turn);
            // The drag over the time since it was last carried: a pass, or
            // as many as its turns are apart.
            var since = parcel.CarriedAt > 0f ? Mathf.Clamp(now - parcel.CarriedAt, 1e-4f, MostSince) : Mathf.Max(air.Elapsed, 1e-4f);
            var keep = Mathf.Exp(-air.Drag * since);

            var grown = parcel.Size * growth.Evaluate(nominalLife > 0f ? Mathf.Clamp01(age / nominalLife) : 1f);
            var shown = 1f;
            var color = parcel.Color;
            var leaving = 1f;
            var cloud = parcel.Cloud ?? air.Cloud;
            if (inStack)
            {
                // Moving with the stack: its speed through the air is its rise.
                var narrow = JetNarrowing((particle.velocity - air.Train).magnitude);
                particle.startSize = Mathf.Min(Mathf.Max(grown * narrow, least), widest);
                // Its smoke shows at once, its white only as it forms.
                var formed = PlumeRise.SteamLeft(parcel.Thickness, cloud, age, air.Speed, air.Mixing, air.Forming);
                var whole = formed + parcel.Thickness.SmokeDepth;
                var smokeAtLip = record.SmokeAtLip;
                var steamAtLip = PlumeRise.OpacityOf(formed, 0f);
                Remember(record, 1f - (1f - steamAtLip) * (1f - smokeAtLip), particle.startSize);
                shown = air.Parcels > 0.5f
                    ? PlumeParcels.Opacity(PlumeParcels.Depth(formed, air.ParcelArea, particle.startSize, 1f)
                                           + PlumeParcels.Depth(parcel.Thickness.SmokeDepth,
                                               air.ParcelArea * PlumeParcels.SmokeAmount(AsSteam(smokeAtLip), air.LightSmoke, air.DenseSmoke), particle.startSize, 1f))
                    : Drawn(steamAtLip, smokeAtLip,
                        particle.startSize, 1f, (particle.velocity - air.Train).magnitude, AsSteam(smokeAtLip), parcel.Rate);
                if (whole > 1e-4f)
                    color = Color.Lerp(SteamColor, parcel.SmokeColor, PlumeModel.SmokeTint(parcel.Thickness.SmokeDepth / whole));
                if (onTurn)
                    stats.InStack++;
            }
            else
            {
                // Under a tunnel's roof, a train in the bore pushes its air along.
                var roof = default(TunnelRoof.Sample);
                var under = air.Roof != null && air.Roof.TryFind(particle.position, out roof);
                // Surface-layer wind strengthens smoothly with height aloft (skipped inside tunnels).
                var shear = under ? 1f : PlumeWind.ShearFactor(height);
                var stillAtHeight = air.Still + air.Wind * (shear - 1f);
                var speedAtHeight = air.Speed;
                if (shear > 1f && air.Wind.sqrMagnitude > 1e-6f)
                {
                    var pastAtHeight = stillAtHeight - air.Train;
                    pastAtHeight -= air.Up * Vector3.Dot(pastAtHeight, air.Up);
                    speedAtHeight = pastAtHeight.magnitude;
                }
                // Near the train its wake carries the plume along, and the air
                // goes past it the slower.
                var wake = PlumeRise.Wake(height, air.WakeCarry, air.WakeDepth);
                var airSpeed = speedAtHeight * (1f - wake);
                var still = stillAtHeight + (air.Train - stillAtHeight) * wake;
                if (under && air.InTunnel)
                {
                    still += (air.Train - still) * TunnelSmoke.PistonShare;
                    airSpeed *= 1f - TunnelSmoke.PistonShare;
                }
                // Buoyant rise, gently damped at a calm dawn/night inversion
                // ceiling (where suppressed rise spreads laterally without
                // accelerating dilution/retirement), plus slipstream downwash
                // confined to the locomotive's wake when drifting fast.
                var undampedRise = PlumeRise.RiseSpeed(air.Flux, height, airSpeed, air.RiseShown);
                var inversionDamping = under ? 1f : PlumeRise.InversionDamping(height, air.InversionCeiling, air.InversionStrength);
                var rise = undampedRise * inversionDamping;
                var inversionSpread = under ? 0f : PlumeRise.InversionSpread(undampedRise, inversionDamping);
                var horizontalDistance = Mathf.Sqrt(Mathf.Max(0f, away * away - height * height));
                var downwashDt = Mathf.Max(since, turns.Apart(age) * Mathf.Max(air.Elapsed, 1e-4f));
                var inDownwashWake = !under && downwashStrength > 0f && horizontalDistance < PlumeRise.DownwashWakeLength;
                var downwash = inDownwashWake
                    ? PlumeRise.Downwash(downwashStrength, height, horizontalDistance, age, air.TrainSpeed, downwashDt)
                    : 0f;
                // Out its own way, evenly over the plume's breadth as it widens,
                // with its rise and the air's turbulence.
                var angle = Hash(particle.randomSeed ^ 0x5bd1e995u) * 2f * Mathf.PI;
                var share = air.Disperse * Mathf.Sqrt(Hash(particle.randomSeed ^ 0x27d4eb2du));
                var smokeAtLip = record.SmokeAtLip;
                var asSteam = AsSteam(smokeAtLip);
                // Spread on from where it was, so a train that slows leaves it as spread.
                // Inversion lateral flattening widens outward motion without inflating parcel.Spread
                // so a calm-dawn capped layer hangs together rather than thinning prematurely.
                var spreadDt = age - parcel.SpreadAge;
                parcel.Spread = PlumeRise.SpreadOn(parcel.Spread, spreadDt, speedAtHeight, air.Spread);
                parcel.InversionSpread = PlumeRise.InversionSpreadOn(parcel.InversionSpread, spreadDt, inversionSpread);
                parcel.SpreadAge = age;
                var spread = parcel.Spread;
                // How thin, how wide, and how few the plume is here: once for all that follows.
                var dilution = PlumeRise.Dilution(height, spread);
                var width = PlumeRise.PlumeWidth(height, spread);
                var survival = PlumeRise.SurvivalAt(width);
                var smokeHere = PlumeRise.OpacityAt(parcel.Thickness.SmokeDepth, dilution);
                var widening = PlumeRise.WideningRate(rise, speedAtHeight, air.Spread, inversionSpread);
                var outward = (across * Mathf.Cos(angle) + along * Mathf.Sin(angle)) * (share * widening);
                var stir = Stir(particle.position, height, now, under);
                // White steam tinted by its smoke, turning the smoke's color
                // as the steam evaporates (as its puff was tinted at the stack).
                var steam = PlumeRise.SteamLeft(parcel.Thickness, cloud, age, air.Speed, air.Mixing, air.Forming,
                    SteamCloud.Sheath(height, air.Bore, air.WhiteByHeight));
                var depth = steam + parcel.Thickness.SmokeDepth;
                var plumeShown = PlumeRise.OpacityAt(depth, dilution);
                shown = plumeShown;
                if (depth > 1e-4f)
                    color = Color.Lerp(SteamColor, parcel.SmokeColor, PlumeModel.SmokeTint(parcel.Thickness.SmokeDepth / depth));
                var narrow = JetNarrowing((particle.velocity - still).magnitude);
                var fill = Mathf.Lerp(air.FillWidth, air.SmokeFillWidth, asSteam);
                var steamHere = PlumeRise.OpacityAt(steam, dilution);
                var here = 1f - (1f - steamHere) * (1f - smokeHere);
                // As parcels: a lump of the plume, set across it, only as
                // many kept as fill it, each its own amount and its retired
                // neighbors' over the disc it shows (PlumeParcels).
                var parcels = air.Parcels > 0.5f;
                var lumpSize = 0f;
                if (parcels)
                {
                    // A hard beat billows out as its blast is spent, within
                    // a few metres of the lip (the photos' working beats, 5
                    // to 10 bores wide 2 to 3 m up), and keeps that width.
                    var envelope = PlumeParcels.Envelope(width, PlumeParcels.ShownShare * least)
                                   + air.BlastSpread * HardOf(record, air.BeatHard) * Mathf.Clamp(away - air.Neck, 0f, air.JetLength);
                    // Its place across the plume, any way out from its
                    // middle, turning over within it as the plume rolls.
                    if (!parcel.Placed)
                    {
                        parcel.Placed = true;
                        parcel.Place = Way(seed) * PlumeParcels.Out(Hash(seed ^ 0x27d4eb2du));
                    }
                    if (air.Roll > 0f && !under)
                    {
                        var axial = Vector3.Dot(parcel.Place, air.Up);
                        var out3 = parcel.Place - air.Up * axial;
                        var radial = out3.magnitude;
                        PlumeParcels.Roll(axial, radial, out var axialRate, out var radialRate);
                        var turned = Mathf.Min(0.5f, air.Roll * rise / Mathf.Max(0.5f, 0.5f * envelope) * since);
                        var placed = parcel.Place + (air.Up * axialRate + (radial > 1e-4f ? out3 / radial : Vector3.zero) * radialRate) * turned;
                        parcel.Place = placed.sqrMagnitude > 1f ? placed.normalized : placed;
                    }
                    // It moves on at its own speed while its blast lasts.
                    var moving = Mathf.Max(Mathf.Max(1f, (particle.velocity - still).magnitude), Mathf.Max(airSpeed, rise));
                    // How thick the plume is across here, from what left the
                    // stack: thin, its lumps have mixed into an even haze.
                    var amount = air.ParcelArea * (steam + parcel.Thickness.SmokeDepth * PlumeParcels.SmokeAmount(asSteam, air.LightSmoke, air.DenseSmoke));
                    var acrossDepth = PlumeParcels.AcrossDepth(amount, parcel.Rate, moving, envelope);
                    var mixed = 1f - Mathf.SmoothStep(0f, 1f, (acrossDepth - air.MixedBelow) / Mathf.Max(0.01f, air.MixedAbove - air.MixedBelow));
                    // A lump of it: larger in the plume's middle, its body,
                    // and smaller at its rim, its lobes; no smaller than the
                    // least, nor larger than the plume.
                    var lump = PlumeParcels.Lump(air.Lump, parcel.Place.magnitude, mixed) * Mathf.Lerp(0.9f, 1.1f, Hash(seed ^ 0x9e3779b9u));
                    lump = Mathf.Clamp(lump * envelope, Mathf.Min(envelope, air.LeastLump), envelope) / Mathf.Max(1e-3f, envelope);
                    // As many as fill it where its lumps are their share of
                    // it: nearer the lip, where a lump is the column's width,
                    // fewer would do, but those retired there would be missed
                    // above, where it widens. Mixed, fewer fill it.
                    var wanted = air.LumpsAcross * Mathf.Lerp(1f, PlumeParcels.MixedShare, mixed);
                    survival = PlumeParcels.Kept(Mathf.Max(envelope, air.LeastLump / Mathf.Max(0.05f, air.Lump)), moving, parcel.Rate, wanted);
                    // Its size is the plume's there: not held narrow while fast.
                    lumpSize = PlumeParcels.Size(envelope, lump);
                    var steamDepth = PlumeParcels.Depth(steam, air.ParcelArea, lumpSize, survival);
                    var smokeDepth = PlumeParcels.Depth(parcel.Thickness.SmokeDepth,
                        air.ParcelArea * PlumeParcels.SmokeAmount(asSteam, air.LightSmoke, air.DenseSmoke), lumpSize, survival);
                    steamHere = PlumeParcels.Opacity(steamDepth);
                    smokeHere = PlumeParcels.Opacity(smokeDepth);
                    here = 1f - (1f - steamHere) * (1f - smokeHere);
                    plumeShown = PlumeParcels.Opacity(acrossDepth);
                    shown = here;
                    // Carried there as its place moves (a beat billowing
                    // out moves its lumps' places at metres a second), and
                    // the rest of the way over a moment. Under an inversion
                    // layer the vertical spread flattens while lateral spread
                    // widens horizontally without inflating parcel.Spread.
                    PlumeParcels.TargetOffsets(envelope, lump, inversionDamping, parcel.InversionSpread,
                        out var horizontalOffset, out var verticalOffset);
                    var verticalPlace = Vector3.Dot(parcel.Place, air.Up);
                    var horizontalPlace = parcel.Place - air.Up * verticalPlace;
                    var set = horizontalPlace * horizontalOffset + air.Up * (verticalPlace * verticalOffset);
                    outward = Vector3.ClampMagnitude(
                        (set - parcel.Offset) / Mathf.Max(since, PlaceEase) + (set - parcel.Set) / Mathf.Max(since, 0.01f), MostPlaceSpeed);
                    parcel.Set = set;
                    parcel.Offset += outward * since;
                }
                var wisp = air.WispAgeTo > air.WispAgeFrom
                    ? Mathf.SmoothStep(0f, 1f, (FlipbookAt(age) - air.WispAgeFrom) / (air.WispAgeTo - air.WispAgeFrom))
                    : 1f;
                // Rolling steam drawn as a wisp, as the shader has it: where
                // the plume is thin, or past the puffs by its age.
                var asWisp = air.Puffs > 0
                    ? Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, (here - air.WispBelow) / Mathf.Max(0.01f, air.WispAbove - air.WispBelow)), wisp)
                    : 0f;
                var size = parcels
                    ? Mathf.Min(lumpSize * Thinning(shown, asWisp), widest)
                    : Mathf.Min(Mathf.Max(Mathf.Max(grown, width * fill) * narrow, least) * Thinning(shown, asWisp), widest);
                Remember(record, here, size);
                if (!parcels)
                    shown = Drawn(steamHere, smokeHere,
                        size, survival, Mathf.Max(airSpeed, rise), asSteam, parcel.Rate, air.WispShare * wisp);
                if (under)
                    size = Mathf.Min(size, TunnelSmoke.WidestParticle);
                else
                {
                    var vertical = rise - downwash;
                    if (parcels)
                    {
                        // Its way to its place is given it outright: dragged
                        // toward as the rest is, it lagged a fast beat's
                        // billowing by metres, a thin column of beads.
                        var target = still + air.Up * vertical + stir;
                        particle.velocity = target + (particle.velocity - parcel.Outward - target) * keep + outward;
                        parcel.Outward = outward;
                    }
                    else
                    {
                        var target = still + air.Up * vertical + outward + stir;
                        particle.velocity = target + (particle.velocity - target) * keep;
                    }
                    if (inDownwashWake)
                    {
                        var verticalNow = Vector3.Dot(particle.velocity, air.Up);
                        var boundedVertical = PlumeRise.ClampDownwashVertical(verticalNow, height, downwashDt);
                        if (boundedVertical > verticalNow)
                            particle.velocity += air.Up * (boundedVertical - verticalNow);
                    }
                }
                particle.startSize = size;
                if (under)
                    UnderRoof(ref particle, roof, still, rise, outward + stir, age, keep);
                if (shaping)
                    Shape(record, particle.position, particle.velocity, age, width, since);
                // Fewer, wider particles carry the plume as it widens: one past
                // its share fades out, then goes; one too thin to see goes at once.
                // At a lower smoke quality one where the plume is faint fades
                // out as well (Air.Faint), unless its white is still forming.
                // (Whether its white has formed is asked only of one faint
                // enough to go: it decides nothing else.)
                var faint = age > FadeIn && plumeShown < Mathf.Max(air.Faint, PlumeRise.Invisible);
                var formedAll = faint && (parcel.Thickness.SteamDepth < 1e-3f || cloud == null
                                          || cloud.PastThickest(age, air.Speed, air.Mixing, air.Forming));
                leaving = parcel.Retirement.Fade(now,
                    PlumeRetirement.ShouldRetire(age, FadeIn, plumeShown, air.Faint, formedAll, Hash(seed) <= survival), FadeOut);
                parcel.CarriedAt = now;
                if (leaving < 1f)
                    record.Retiring = true;
                // Faintness begins retirement; only the completed fade removes
                // the parcel. Physical opacity is not the shader's visible alpha.
                if (leaving <= 0f)
                {
                    particle.remainingLifetime = 0f;
                    if (onTurn)
                        stats.Retired++;
                    return false;
                }
                cycleTop = Mathf.Max(cycleTop, height);
                if (onTurn)
                {
                    stats.Aloft++;
                    stats.SizeSum += particle.startSize;
                    stats.AlphaSum += shown;
                    if (particle.startSize > stats.Widest)
                    {
                        stats.Widest = particle.startSize;
                        stats.WidestDrawn = particle.GetCurrentSize(system);
                        stats.WidestHeight = height;
                    }
                }
            }
            var fade = parcel.FromStack ? 1f : Mathf.Clamp01(age / FadeIn);
            particle.startColor = new Color(color.r, color.g, color.b, shown * fade * leaving);
            if (onTurn && shadowTurn)
                casters.Add(new Caster { Position = particle.position, Size = particle.startSize, Opacity = shown * fade * leaving, Record = record });
            return true;
        }

        // Under a tunnel's roof: up to the roof, then along it its own way as
        // a ceiling jet that slows, its spread and stirring only along the
        // bore, and held between the walls.
        private void UnderRoof(ref ParticleSystem.Particle particle, TunnelRoof.Sample roof, Vector3 still, float rise,
            Vector3 drift, float age, float keep)
        {
            var size = particle.startSize;
            var cap = TunnelSmoke.Cap(Vector3.Dot(roof.Point, air.Up), size, air.Floor);
            var level = Vector3.Dot(particle.position, air.Up);
            var atRoof = level >= cap - RoofBand;
            var way = Hash(particle.randomSeed ^ 0x165667b1u) < 0.5f ? -1f : 1f;
            var jet = atRoof ? way * TunnelSmoke.JetSpeed(rise, age) : 0f;
            var target = still + air.Up * (atRoof ? 0f : rise) + roof.Along * (Vector3.Dot(drift, roof.Along) + jet);
            particle.velocity = target + (particle.velocity - target) * keep;
            if (level > cap)
                particle.position += air.Up * (cap - level);
            if (atRoof)
            {
                var upward = Vector3.Dot(particle.velocity, air.Up);
                if (upward > 0f)
                    particle.velocity -= air.Up * upward;
            }
            var side = Vector3.Dot(particle.position - roof.Point, roof.Across);
            var reach = TunnelSmoke.WallReach(size);
            if (Mathf.Abs(side) <= reach)
                return;
            var sign = Mathf.Sign(side);
            particle.position -= roof.Across * (side - sign * reach);
            var outwards = sign * Vector3.Dot(particle.velocity, roof.Across);
            if (outwards > 0f)
                particle.velocity -= roof.Across * (sign * outwards);
        }

        // A way out from the plume's middle, evenly over the sphere, by the particle's seed.
        private static Vector3 Way(uint seed)
        {
            var y = 2f * Hash(seed ^ 0x85ebca6bu) - 1f;
            var turn = 2f * Mathf.PI * Hash(seed ^ 0x5bd1e995u);
            var flat = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            return new Vector3(flat * Mathf.Cos(turn), y, flat * Mathf.Sin(turn));
        }

        // A particle's opacity drawn, its white steam's <paramref name="steam"/>
        // and its smoke's <paramref name="smoke"/>: its smoke's shared with
        // the particles overlapping it, <paramref name="size"/> m wide, the
        // share <paramref name="kept"/> still carried, moving away at
        // <paramref name="speed"/> m/s, <paramref name="rate"/> a second
        // emitted as it was, so together they show the plume's.
        // The steam's is not: its opacity was set by eye drawn this way, and
        // white steam is thick enough that its overlap only fills it in
        // (shared, a frosty day's billows drew as thin, frayed wisps). Past
        // the puffs it is, by <paramref name="steamSharing"/> (Air.WispShare):
        // stacked ten and more deep, the wisps drew an opaque, soft mass.
        private float Drawn(float steam, float smoke, float size, float kept, float speed, float asSteam, float rate, float steamSharing = 0f)
        {
            // Dense smoke is drawn as the steam is, by how dense it left the
            // stack (<paramref name="asSteam"/>, AsSteam of its opacity at
            // the lip): thick enough that its overlap only fills it in.
            var sharing = air.Overlap * (1f - asSteam);
            var overlaps = rate * size * kept / Mathf.Max(1f, speed);
            var smokeDrawn = 1f - Mathf.Pow(1f - Mathf.Min(smoke, 0.999f), 1f / (1f + sharing * overlaps));
            var steamDrawn = 1f - Mathf.Pow(1f - Mathf.Min(steam, 0.999f), 1f / (1f + steamSharing * overlaps));
            return 1f - (1f - steamDrawn) * (1f - smokeDrawn);
        }

        private float Thinning(float shown, float asWisp)
        {
            var edge = Mathf.Lerp(FrayEdge * Mathf.Clamp01(air.Fray) + SoftEdge, air.WispFrayEdge * Mathf.Clamp01(air.Fray), asWisp);
            return Mathf.Min(MostThinGrowth, 1f / Mathf.Max(0.05f, 1f - edge * (1f - Mathf.Clamp01(shown))));
        }

        // While a particle goes fast through the air it is held narrow, the
        // jet's width; slowing, it swells to its size.
        private float JetNarrowing(float speed) =>
            Mathf.Lerp(1f, Mathf.Clamp01(air.JetWidth), Mathf.Clamp01((speed - JetSlow) / (JetFast - JetSlow)));

        // The plume's divergence-free curl eddies and slow horizontal meander
        // (PlumeTurbulence): analytical curl of a height-enveloped vector
        // potential in the orthonormal (across, Up, along) frame, scaled by
        // frame-constant eddySpeed so 3D divergence is identically zero
        // everywhere, plus slow crosswind weaving in open air.
        private Vector3 Stir(Vector3 position, float height, float now, bool under)
        {
            if (!(air.Eddies > 0f) || !(height > 0f))
                return Vector3.zero;
            var eddy = Vector3.zero;
            if (eddySpeed > 0f)
            {
                var u = Vector3.Dot(position, across);
                var w = Vector3.Dot(position, along);
                PlumeTurbulence.Curl(u, height, w, now, out var cx, out var cy, out var cz);
                eddy = (across * cx + air.Up * cy + along * cz) * eddySpeed;
            }
            if (!under)
            {
                PlumeTurbulence.Meander(now, height, windAcross, windAlong, out var mx, out var mz);
                eddy += (across * mx + along * mz) * air.Eddies;
            }
            return eddy;
        }

        // How far smoke as dense as smokeAtLip as it left the stack is drawn as
        // the steam is, 0 to 1.
        private float AsSteam(float smokeAtLip) =>
            Mathf.SmoothStep(0f, 1f, (smokeAtLip - air.SmokeAsSteamFrom) / Mathf.Max(0.01f, air.SmokeAsSteamTo - air.SmokeAsSteamFrom));

        // The plume's opacity at a particle <paramref name="size"/> m wide,
        // for how wispy it is drawn, kept for this turn and seen from the
        // last. Thinning smoke of any kind frays into wisps (the photos'
        // black smoke tears into streaks where it thins), but a giant
        // particle far down the trail stays a puff: its wisp's strands, tens
        // of meters across, drew hard, straight shards.
        private void Remember(Record record, float opacity, float size)
        {
            var giant = air.WispLargestTo > air.WispLargestFrom
                ? Mathf.SmoothStep(0f, 1f, (size - air.WispLargestFrom) / (air.WispLargestTo - air.WispLargestFrom))
                : 0f;
            opacity = Mathf.Lerp(opacity, 1f, giant);
            record.Here = opacity;
        }

        // An even spread from 0 to 1 by the particle's seed.
        private static float Hash(uint seed) => unchecked(seed * 2654435761u) / 4294967296f;
    }
}
