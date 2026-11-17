using System;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
    /// <summary>
    /// The chimney plume's look that is not the model's: how the particles
    /// are drawn. The test bed edits it live; the defaults are the tuned values.
    /// </summary>
    internal sealed class PlumeLook
    {
 public float Light = 1.1f;

        /// <summary>The smoke's fraying at its edges, 0 fading evenly.</summary>
        public float Fray = 0.5f;

        public float SkyLight = 1.5f;
        public float SunLight = 0.65f;
        public float MaxLight = 2f;
        public float Wrap = 0.4f;
        public float Relief = 0.6f;
        public float Bulge = 0.1f;
        public float SelfShadow = 0.5f;
        public float Scatter = 0.25f;

        public float Flat = 1f;
        public float Terminator = 0.25f;
        public float ShadeLight = 0.6f;
        public float SunShadow = 0f;
        public float ShadowDensity = 0.05f;

        public float Volume = 1f;
        public float VolumeShape = 1f;
        public float VolumeDensity = 0.03f;

        /// <summary>
        /// Smoke at the camera fades, shown from this far, m, fully from
        /// this (2026-10-21: inside a fast train's trail the view was a flat
        /// white wall of the nearest particles).
        /// </summary>
        public float NearFadeFrom = 0.5f;
        public float NearFadeTo = 4f;

        /// <summary>The light on the plume greyed toward its brightness, 0 to 1.</summary>
        public float LightGrey = 0f;

        public float NightSkyLight = 0.7f;
        public float NightGrey = 0.5f;

        /// <summary>
        /// How far the sun's own light leaves the steam and smoke from sunset
        /// through dusk (PlumeWeather.DirectLight), 0 (as before: all of the
        /// game's light, however low its sun) to 1.
        /// </summary>
        public float TwilightSun = 1f;

        /// <summary>
        /// A plume particle's size, × the plume's width where it is (0.75 until
        /// 2026-10-21, then 0.9, then 0.6: with the trail spreading faster,
        /// its wisps tens of meters wide drew soft; 0.75 again with 1.13's
        /// wisps and spreading, 2026-10-23).
        /// </summary>
        public float FillWidth = 0.75f;

 public float SmokeFillWidth = 0.75f;

        /// <summary>How far out a particle drifts from where it left the stack, × the plume's widening (0.3 until 2026-10-21).</summary>
        public float Disperse = 0.4f;

        /// <summary>
        /// How far a particle's smoke's opacity is shared with those
        /// overlapping it, 0 (each as opaque as the plume) to 1 (PlumeAloft):
        /// at 1 a fresh charge's smoke all but vanished, the wisps covering
        /// only a fifth of their quads.
        /// </summary>
        public float Overlap = 0.5f;

 public float SmokeAsSteamFrom = 0.5f;

 public float WispLargestFrom = 60f;
        public float WispLargestTo = 150f;
        public float SmokeAsSteamTo = 0.85f;


        /// <summary>How far each particle is lit as its part of the plume's surface, × (0 each alone; PlumeAloft.ShapeOf).</summary>
        public float PlumeShape = 0f;

        // The plume as parcels that carry their smoke (PlumeParcels), 1 on:
        // a particle is a lump this share of the plume's envelope across (no
        // smaller than this, m shown), this many kept in each envelope's
        // length, each carrying the model's depth over this area, m², and a
        // light haze's smoke this share of that. In place of each particle as
        // wide and as opaque as the plume (FillWidth, Overlap, WispShare,
        // SmokeAsSteam's sharing).
        public float Parcels = 0f;
        public float ParcelLump = 0.4f;
        public float ParcelLeast = 1f;
        public float ParcelsAcross = 20f;
        public float ParcelArea = 1.2f;
        public float ParcelLightSmoke = 0.09f;

        /// <summary>Black smoke's amount, × steam's for the same depth in the model.</summary>
        public float ParcelDenseSmoke = 0.27f;

        /// <summary>How far a hard beat billows out as its blast is spent: this much wider, m, for each metre from the lip over the jet's length.</summary>
        public float ParcelBlast = 1f;

        /// <summary>How fast the plume's lumps turn over within it, × its rise over its half-width (PlumeParcels.Roll; 0 each keeps its place).</summary>
        public float ParcelRoll = 0.5f;

        /// <summary>Where the plume's optical depth across is this or less its lumps have mixed into an even haze; from the next they stand apart.</summary>
        public float ParcelMixedBelow = 0.3f;
        public float ParcelMixedAbove = 1.5f;

        /// <summary>How far a beat's particles leave as one cluster, a core and lobes beside it, 0 (strung up the jet) to 1.</summary>
        public float BeatCluster = 0.6f;

        /// <summary>
        /// How far thick steam is drawn as round billows, 0 to 1 (the shader's
        /// second flipbook), from a particle's opacity <see cref="BillowFrom"/>
        /// to <see cref="BillowFull"/>, the wisps as it thins.
        /// </summary>
        public float Billow = 1f;
        public float BillowFrom = 0.3f;
        public float BillowFull = 0.8f;

 public float BillowAge = 1f;

        /// <summary>The surface's up across the particle, ±1 (which way the texture's normal reads).</summary>
        public float BinormalSign = 1f;

        /// <summary>A particle's width while it goes fast through the air, against its size.</summary>
        public float JetWidth = 0.5f;

        /// <summary>The narrow-jet zone above the smoke emitter at most, m (the stack's lip where it is known).</summary>
        public float JetZone = 1f;

        /// <summary>How hard the plume's eddies stir it, ×.</summary>
        public float Eddies = 1f;

 public float RollRate = 0.5f;
        public float RollSlows = 3f;

        /// <summary>Rolling steam: how far the plume's far side from the sun shades its puffs, 0 to 1.</summary>
        public float SixPlume = 0.5f;

        /// <summary>
        /// Rolling steam: the sun's light a puff scatters into its shade, 0 to
        /// 1 (at 0.6 its lobes hardly showed; 0.35 matched the photos' shade).
        /// </summary>
        public float RollShade = 0.4f;

 public float ThickFloor = 0.05f;

        /// <summary>Development: rolling steam's opacity drawn red, × this (0 off), to find faint coverage.</summary>
        public float DebugAlpha = 0f;

        /// <summary>Development: the cloud and sooty core hidden (above 0), to time what drawing them costs.</summary>
        public float HidePlume = 0f;

        /// <summary>Development: the jet's streaks hidden (above 0).</summary>
        public float HideJet = 0f;

        /// <summary>Development: the heat shimmer hidden (above 0).</summary>
        public float HideHeat = 0f;

        /// <summary>
        /// Development: the smoke at half resolution laid over the scene as
        /// 1, a magenta tint over the whole screen; 2, its coverage in red;
        /// 3, the scene's depth in green and the half-size depth in blue,
        /// each a hundredth per meter (<see cref="PlumeOffscreen"/>); 0 off.
        /// </summary>
        public float OffscreenDebug = 0f;

        /// <summary>
        /// Rolling steam: the share of the sun's light scattered into a puff's
        /// shade that takes the sky's color rather than the sun's, 0 to 1
        /// (none drew a dawn's whole plume peach; 0.6 tinted a day's plume blue-gray).
        /// </summary>
        public float ShadeSky = 0.3f;

 public float RiseShown = 0.7f;

 public float WispBelow = 0.15f;
        public float WispAbove = 0.6f;

 public float WispFrayEdge = 0.5f;

 public float WispAgeFrom = 0.2f;
        public float WispAgeTo = 0.45f;
        // Limit the rounded trail at speed; 0 retains the stationary handoff.
        public float PuffTrail = 10f;
        // The beats' puffs and what leaves between them as one mixture
        // (PlumeModel.AsOne); 0 for each its own, as until 2026-11-12.
        public float OneExhaust = 1f;
        // Development comparison with the old, mismatched steam priorities
        // (0: the game's own steam keeps its sorting orders, and the engine's
        // steam the mod draws is all drawn over the plume).
        public float MatchSteamOrder = 1f;

        /// <summary>
        /// Rolling steam: how far a wisp blends to its next frame, 0 to 1 (0:
        /// it steps, as the plume's flipbook did on 2026-10-21).
        /// </summary>
        public float WispBlend = 0f;

 public float WispSheet = 1f;

        /// <summary>
        /// Rolling steam: 1, each wisp keeps its own row of the sheet, from a
        /// frame of its own past the young compact ones; 0, the whole sheet
        /// in turn (WispSheet), one row blending into another, its strands
        /// fading out and others in (Sol, 2026-10-23: the light firing's
        /// flashing; its trail crisper, its curls clearer).
        /// </summary>
        public float WispCoherence = 1f;

 public float WispHold = 0f;

 public float WispFadeFrom = 0.8f;

 public float ThinWispAgeFrom = 0.1f;
        public float ThinWispAgeTo = 0.3f;
        public float LongPuffsFrom = 0.6f;
        public float LongPuffsBy = 1f;

        /// <summary>
        /// Rolling steam: 1, the dead dropped before the particles are set,
        /// so each keeps its own custom data (Sol, 2026-10-23); 0, a survivor
        /// moved into a dead one's place drew its wisp for a frame.
        /// </summary>
        public float StableParticleData = 1f;

 public float WispBaked = 1f;

        /// <summary>
        /// Rolling steam: 1, each wisp at its own turn, any way, slowly
        /// spinning, as 1.13's; 0, its particle's, near upright as the puffs:
        /// all its strands lay one way, a grain round a rope (2026-10-23).
        /// </summary>
        public float WispTurn = 1f;

 public float WispSpin = 0.3f;
        public float WispSpinEase = 1f;

        /// <summary>Those wisps lit as 1.13 lit them (WispPuffLit 0): its plume light.</summary>
        public float WispBakedLight = 1.6f;

        /// <summary>
        /// Rolling steam: 1, those wisps lit as the puffs are, as a puff of the
        /// flipbook's mean six ways' light, at WispPuffLight of it (by 1.13's
        /// light the wisps were tan and the puffs white, and each puff handing
        /// off drew a pale disc with a tan rim).
        /// </summary>
        public float WispPuffLit = 1f;
        public float WispPuffLight = 1f;

 public float WispShare = 0f;

 public float WispRelief = 1f;
        public float WispBulge = 0.4f;
        public float WispScatter = 0.5f;
        public float WispSunLight = 0.9f;

        /// <summary>Rolling steam: opacity below this is drawn as none (a pixel dropped unblended, the plume's cost mostly the blending).</summary>
        public float LeastOpacity = 0.002f;

 public float Fringe = 0.8f;
        public float FringeCore = 0.5f;

 public float Upright = 1f;
        public float Grow = 1f;
        public float WispReach = 1f;

        /// <summary>
        /// Steam around the engine (SteamVents): how hard every source
        /// blows, ×; how fast the air mixes in, ×; each parcel's opacity's
        /// depth, ×; its drawn width, × its mixture's; how fast its warmth
        /// lifts it, ×; how fast it rolls, × the plume's; and how far its
        /// edge frays into strands, 0 to 1.
        /// </summary>
        public float VentStrength = 1f;
        public float VentMixing = 1f;
        public float VentDensity = 1f;
        public float VentFill = 1.6f;
        public float VentRise = 1f;
        public float VentRoll = 2f;
        public float VentEdge = 0.5f;

        /// <summary>Steam around the engine: a dry jet's clear core, × its kind's.</summary>
        public float VentClear = 1f;

        /// <summary>Rolling steam: a fringe's strands' opacity, × its particle's wisp's, 0 to 1.</summary>
        public float FringeShare = 1f;

        /// <summary>
        /// Rolling steam: a particle is fringed from this far on the plume's
        /// silhouette (its way out from the plume's middle across the view, ×
        /// how far out; PlumeAloft.ExtraOf), wholly by the next: over the
        /// plume's faces turned toward the view the strands veiled its lobes.
        /// Set for the measure across the plume (<see cref="ShapeAcross"/>):
        /// from its age band's middle, 0.45 and 0.8, along the trail too it
        /// measured particles further out, and across at those a puff frayed
        /// a tenth as much (2026-10-24: as many fray from 0.3 and 0.55).
        /// </summary>
        public float FringeRimFrom = 0.3f;
        public float FringeRimTo = 0.55f;

 public float ShapeAcross = 1f;

 public float ShapeSmoothing = 1f;

 public float SootAlbedo = 0.04f;

 public float LipWidth = 0.7f;

        /// <summary>
        /// How far above the lip the smoke is held at least the bore's width,
        /// in bore diameters: the photos' neck, 1 to 3 diameters before it spreads.
        /// </summary>
        public float Neck = 2f;

        /// <summary>
        /// Past the neck, how fast the smoke's least width grows, m of radius
        /// a metre: the photos' 0.2 to 0.25 (a half-angle of 10 to 15°).
        /// </summary>
        public float NeckSpread = 0.2f;

        /// <summary>The heat shimmer's bend at its strongest, a share of the view (0.006 until 2026-10-17, too faint to see).</summary>
        public float ShimmerStrength = 0.02f;

 public float ShimmerSize = 0.75f;

        /// <summary>The shimmer's particles a second while it shows.</summary>
        public float ShimmerRate = 20f;

        /// <summary>How long a shimmer particle lives, s (1.2 to 1.8 until 2026-10-17: it ended just past the bend).</summary>
        public float ShimmerLife = 3f;

        /// <summary>How fast the shimmer weakens as it widens: its strength as its starting width over its width to this power.</summary>
        public float ShimmerDiffuse = 1f;

        public PlumeLook Copy() => (PlumeLook)MemberwiseClone();
    }

 internal static class PlumeCurrent
    {
        /// <summary>The model's tuning every plume uses.</summary>
        public static readonly PlumeTuning Tuning = new PlumeTuning();

        /// <summary>The plume's look every plume uses.</summary>
        public static readonly PlumeLook Look = new PlumeLook();

        /// <summary>Bumped when a look setting that plumes apply once (their materials) changes.</summary>
        public static int LookVersion { get; private set; }

        internal static void LookChanged() => LookVersion++;
    }
}
