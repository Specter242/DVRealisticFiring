using System;

namespace RealisticFiring.Core
{
    /// <summary>Finite oil-soaked waste. Fuel survives transfers; ignition never refills it.</summary>
    public sealed class RagFuel
    {
        /// <summary>Oil a rag holds, kg.</summary>
        public const float OilCapacity = 0.06f;
        /// <summary>
        /// Oil burned per rag, kg/s: about 200 s of flame. Chosen for play; a
        /// small wick fire burns at roughly 0.01 kg/m²s over ~0.03 m².
        /// </summary>
        public const float OilBurnRate = 0.0003f;
        /// <summary>Stoichiometric air for a lubricating oil, kg/kg.</summary>
        public const float OilAir = 14.5f;
        public float Oil { get; private set; }
        public bool Lit { get; private set; }
        public float RemainingSeconds => Oil / OilBurnRate;
        public float Soak(float mass)
        {
            var accepted = Lit ? 0f : Math.Min(Positive(mass), OilCapacity - Oil);
            Oil += accepted;
            return accepted;
        }
        public bool Ignite()
        {
            if (Oil <= 0f) return false;
            Lit = true;
            return true;
        }
        public void Extinguish() => Lit = false;
        /// <summary>Burns in open air (a rag held or lying loose).</summary>
        public float Burn(float seconds) => Burn(seconds, float.MaxValue);
        public float Burn(float seconds, float air)
        {
            if (!Lit) return 0f;
            var used = Math.Min(Oil, Math.Min(OilBurnRate * Positive(seconds), Positive(air) / OilAir));
            Oil -= used;
            if (Oil <= 0f) { Oil = 0f; Lit = false; }
            return used;
        }
        public void Restore(float oil, bool lit)
        {
            Oil = Math.Min(OilCapacity, Positive(oil));
            Lit = lit && Oil > 0f;
        }
        internal static float Positive(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
    }

    /// <summary>
    /// Wood and oiled rags on one grate zone: surface heating to piloted
    /// ignition, flame spread over the pile, flaming pyrolysis that needs
    /// enough heat flux to keep going, and a glowing char bed.
    ///
    /// Physical anchors (USDA FPL Wood Handbook, GTR-190, 2010, ch. 18):
    /// piloted ignition at a surface of 347-377 °C (365 °C overall); a
    /// critical ignition flux of 10-13 kW/m²; ignition from 3 s at 55 kW/m²
    /// to 930 s at 18 kW/m²; ~20 MJ/kg gross heat of dry wood; char residue
    /// of 25 % or less of the dry mass; mass loss under constant flux of
    /// 3.8 g/m²s at 18 kW/m² and 8.6 at 55 for southern pine (Table 18-3).
    /// Stoichiometric air 5.96 kg/kg from C 50 %, H 6 %, O 43 %. Flaming
    /// auto-extinction near 3.5 g/m²s (Emberley et al., timber, 2017).
    /// Lumped areas, mutual radiation in the pile, flame-contact fluxes, and
    /// the rag's burning time are approximations chosen for play.
    /// Heat is joules; masses kg; temperatures Celsius. No Unity dependencies.
    /// </summary>
    public sealed class StarterFuel
    {
        /// <summary>One split piece as delivered, kg (15 % moisture, wet basis).</summary>
        public const float PieceMass = 0.65f;
        public const float ZoneCapacity = PieceMass * 8f;
        /// <summary>Saved values per zone.</summary>
        public const int StateLength = 10;
        private const int LegacyStateLength = 6;

        // Wood as delivered: seasoned splits, 15 % water wet basis.
        public const float Moisture = 0.15f;
        /// <summary>Char left of the dry wood that pyrolyses.</summary>
        public const float CharYield = 0.22f;
        /// <summary>Net heat of dry wood, J/kg (20 MJ/kg gross less the water its hydrogen forms).</summary>
        public const float WoodHeat = 18.7e6f;
        /// <summary>Wood char burning to CO2, J/kg (carbon 32.8 MJ/kg; char is ~90 % carbon).</summary>
        public const float CharHeat = 30e6f;
        /// <summary>The volatiles' share: (18.7 − 0.22 × 30) / 0.78 MJ/kg.</summary>
        public const float VolatileHeat = (WoodHeat - CharYield * CharHeat) / (1f - CharYield);
        public const float OilHeat = 40e6f;
        public const float WoodAir = 5.96f;
        public const float CharAir = 11.5f;
        public const float VolatileAir = (WoodAir - CharYield * CharAir) / (1f - CharYield);
        /// <summary>Warming and boiling off the wood's water, J/kg.</summary>
        public const float EvaporationEnergy = 2.6e6f;
        /// <summary>Mineral ash of dry wood.</summary>
        public const float AshFraction = 0.005f;

        // A quarter split of a 13.5 cm log, 0.33 m long, at 553 kg/m³ wet:
        // 0.086 m² of surface, 70 % of it open to flame in a loose pile.
        public const float AreaPerPiece = 0.060f;
        // Char is lighter and finer than the wood it came from.
        private const float CharAreaPerKg = 0.25f;

        // Surface heating: wood k 0.12 W/mK, c 1.7 kJ/kgK, 553 kg/m³, its
        // water's latent heat spread over the rise to ignition (×1.71).
        private const float Conductivity = 0.12f;
        private const float HeatCapacity = 553f * 1700f * 1.71f;
        private const float Diffusivity = Conductivity / HeatCapacity;
        private const float ThermalInertia = Conductivity * HeatCapacity;
        public const float IgnitionTemperature = 365f;
        private const float Emissivity = 0.9f;
        private const float Convection = 10f;
        private const float Sigma = 5.67e-8f;

        // Flame fluxes onto wood, W/m² (flame contact is 20-40 kW/m²).
        private const float RagFlameFlux = 25000f;
        /// <summary>Wood under one bunched rag, m².</summary>
        public const float RagContactArea = 0.03f;
        // A flat sample's own flame nets about +7 kW/m² over its losses under
        // a cone heater (Table 18-3 fit); a loose split in the open, less.
        private const float OwnFlameFlux = 22000f;
        private const float AdjacentFlameFlux = 25000f;
        private const float MutualFlux = 30000f;
        private const float MutualScale = 1.5f;
        private const float EmberFlux = 25000f;
        private const float EmberHalf = 0.3f;
        // A burning surface's char at ~450 °C.
        private const float BurningSurface = 450f;
        /// <summary>Effective heat to gasify wood, J/kg: the Table 18-3 slope.</summary>
        private const float Gasification = 7.0e6f;
        /// <summary>Below this the flames die back, kg/m²s.</summary>
        public const float CriticalMassFlux = 0.003f;
        private const float ExtinctionTime = 15f;
        // Flame runs up and along the pieces: the unburnt area beside it,
        // twice the burning area, ignites after its ignition delay.
        private const float SpreadFactor = 2f;
        // Cold coal over the wood: it shades the flames and draws heat.
        private const float CoverHalf = 2f;
        private const float CoverLoss = 10000f;
        // Glowing char burns out over about 15 minutes with air.
        private const float CharTime = 900f;
 public const float Pace = 5f;
        /// <summary>Char that keeps glowing without flames or coal beside it, kg.</summary>
        public const float SelfGlowingChar = 0.15f;
        // A burned-down pile's embers burn at least this fast, kg/s: its
        // last 50 g in about a minute and a half.
        private const float EmberFloor = 0.0006f;
        private const float MaxStep = 0.25f;

        public float Wood { get; private set; }
        public float Char { get; private set; }
        /// <summary>Heat stored in the hottest unlit wood surface, J/m².</summary>
        public float FrontEnergy { get; private set; }
        /// <summary>How long that surface has been heated, s: its heated depth.</summary>
        public float FrontTime { get; private set; }
        public float RagOil { get; private set; }
        public int RagCount { get; private set; }
        public bool RagLit { get; private set; }
        /// <summary>The char bed is glowing.</summary>
        public bool Glowing { get; private set; }
        /// <summary>Wood surface in flame, m².</summary>
        public float Involved { get; private set; }
        /// <summary>How much of the visible wood surface has charred, 0 to 1; independent of the char mass.</summary>
        public float SurfaceChar { get; private set; }

        public float Heat { get; private set; }
        public float AirUsed { get; private set; }
        public float AshProduced { get; private set; }
        public float Smoke { get; private set; }
        /// <summary>Wood flame and ember heat that can light coal, W (the rag's flame is excluded).</summary>
        public float CoalSupportHeat { get; private set; }
        /// <summary>Burning per area of flame, kg/m²s, dry.</summary>
        public float MassFlux { get; private set; }

        public float Mass => Wood + Char + RagOil;
        public float Area => AreaPerPiece * Wood / PieceMass + CharAreaPerKg * Char;
        public float Flaming => Area > 0f ? Smoothing.Clamp01(Involved / Area) : 0f;
        public bool Burning => Involved > 0f || Glowing;
        public bool Alight => RagLit || Burning;
        public float CharFraction => Wood + Char > 0f ? Char / (Wood + Char) : 0f;
        public float EmberGlow => Glowing ? Char / (Char + EmberHalf) : 0f;
        public float FrontTemperature => frontTemperature;
        private float frontTemperature = 20f;
        private float environment = 20f;

        public float AddWood(float mass, float capacity)
        {
            var accepted = Math.Min(RagFuel.Positive(mass), Math.Max(0f, Math.Min(ZoneCapacity, capacity) - Mass));
            if (accepted <= 0f) return 0f;
            // Fresh pieces bring new, uncharred surface.
            var before = Area;
            Wood += accepted;
            SurfaceChar *= before / Math.Max(1e-6f, Area);
            return accepted;
        }

        /// <summary>
        /// A rag lands: its oil joins the zone. A dry or spent rag is cloth
        /// that burns away with the fire, accepted with no pilot.
        /// </summary>
        public bool AddRag(float oil, bool lit)
        {
            oil = Math.Min(RagFuel.OilCapacity, RagFuel.Positive(oil));
            if (oil <= 0f) return true;
            if (RagCount >= 4) return false;
            RagOil += oil;
            RagCount++;
            // A burning rag lights an oiled rag it lands on.
            RagLit |= lit;
            return true;
        }

        public void Extinguish()
        {
            RagLit = Glowing = false;
            Involved = 0f;
            CoolFront(100f);
        }

        public void Clear()
        {
            Wood = Char = RagOil = Involved = SurfaceChar = FrontEnergy = FrontTime = 0f;
            Heat = AirUsed = AshProduced = Smoke = CoalSupportHeat = MassFlux = 0f;
            RagCount = 0;
            RagLit = Glowing = false;
            frontTemperature = environment;
        }

        /// <summary>
        /// Advances the zone. <paramref name="airRate"/> is the zone's dry
        /// air, kg/s; <paramref name="coalCover"/> the cold coal over it, kg;
        /// <paramref name="coalFlux"/> radiation from burning coal under and
        /// beside it, W/m², which also pilots; <paramref name="neighborFlux"/>
        /// flame from wood in the zones beside it, W/m²;
        /// <paramref name="ambient"/> the firebox walls, °C.
        /// </summary>
        public void Step(float dt, float airRate, float coalCover, float coalFlux, float neighborFlux, float ambient, bool drowned)
        {
            Heat = AirUsed = AshProduced = Smoke = CoalSupportHeat = MassFlux = 0f;
            if (!(dt > 0f) || float.IsInfinity(dt) || float.IsNaN(dt)) return;
            environment = Math.Max(-40f, Math.Min(400f, float.IsNaN(ambient) || float.IsInfinity(ambient) ? 20f : ambient));
            if (drowned) { Extinguish(); return; }
            airRate = RagFuel.Positive(airRate);
            coalCover = RagFuel.Positive(coalCover);
            coalFlux = RagFuel.Positive(coalFlux);
            neighborFlux = RagFuel.Positive(neighborFlux);
            var steps = (int)Math.Ceiling(dt / MaxStep);
            var h = dt / steps;
            float heat = 0f, air = 0f, support = 0f, smoke = 0f, flux = 0f;
            for (var s = 0; s < steps; s++)
            {
                SubStep(h, airRate, coalCover, coalFlux, neighborFlux, ref heat, ref air, ref support, ref smoke, ref flux);
            }
            Heat = heat / dt;
            AirUsed = air / dt;
            CoalSupportHeat = support / dt;
            Smoke = Smoothing.Clamp01(smoke / steps);
            MassFlux = flux / steps;
        }

        private float Loss(float surface)
        {
            var t = surface + 273.15f;
            var e = environment + 273.15f;
            return Emissivity * Sigma * (t * t * t * t - e * e * e * e) + Convection * (surface - environment);
        }

        private void SubStep(float h, float airRate, float coalCover, float coalFlux, float neighborFlux,
            ref float heat, ref float airUsed, ref float support, ref float smoke, ref float fluxSum)
        {
            var cover = coalCover / (coalCover + CoverHalf);
            // Deep cold coal restricts the air reaching the wood beneath.
            var air = airRate * h / (1f + coalCover / 3f);
            var airStart = air;
            var area = Area;
            if (Involved > area) Involved = area;
            if (area <= 0f) { Involved = 0f; Glowing = Glowing && Char > 0f; }

            // The rag: its flame needs air; it lights from flame beside it.
            var pilot = Involved > 0f || Glowing || coalFlux > 0f || neighborFlux > 0f;
            if (RagOil > 0f && !RagLit && pilot && air > 0f) RagLit = true;
            if (air <= 0f) RagLit = false;
            var oil = RagLit ? Math.Min(RagOil, Math.Min(RagFuel.OilBurnRate * RagCount * h, air / RagFuel.OilAir)) : 0f;
            RagOil -= oil;
            air -= oil * RagFuel.OilAir;
            if (RagOil <= 1e-7f) { RagOil = 0f; RagLit = false; RagCount = 0; }
            var ragBurning = oil > 0f;
            var ragArea = ragBurning ? Math.Min(area, RagContactArea * RagCount) : 0f;
            var ragFlux = ragBurning ? RagFlameFlux * (1f - 0.7f * cover) : 0f;
            heat += oil * OilHeat;

            var ember = EmberGlow * EmberFlux;
            var external = coalFlux + neighborFlux + ember;
            // Burning surfaces, the rag's flame among them, heat each other.
            var pieces = (Involved + 2f * ragArea) / AreaPerPiece;
            var mutual = MutualFlux * (1f - (float)Math.Exp(-Math.Max(0f, pieces - 1f) / MutualScale)) * (1f - 0.3f * cover);
            var critical = Loss(IgnitionTemperature);

            // The hottest unlit surface: under the rag, beside the flames, or
            // over the coal. Integral heat-conduction profile (Goodman): the
            // heated depth grows as sqrt(6 α t) and the surface rises with
            // the stored heat over that depth.
            var adjacent = Involved > 0f ? AdjacentFlameFlux * (1f - 0.6f * cover) + 0.3f * mutual : 0f;
            var incident = Math.Max(ragArea > 0f ? ragFlux : 0f, adjacent) + external;
            if (area - Involved > 1e-6f)
            {
                FrontTime += h;
                var depth = Math.Max(0.0005f, (float)Math.Sqrt(6f * Diffusivity * FrontTime));
                FrontEnergy = Math.Max(0f, FrontEnergy + (incident - Loss(frontTemperature)) * h);
                frontTemperature = Math.Min(IgnitionTemperature + 50f, environment + 3f * FrontEnergy / (HeatCapacity * depth));
                if (frontTemperature <= environment + 0.5f && incident <= 0f) { FrontEnergy = 0f; FrontTime = 0f; frontTemperature = environment; }
            }
            if (frontTemperature > 250f)
                SurfaceChar = Math.Max(SurfaceChar, 0.4f * Smoothing.Clamp01((frontTemperature - 250f) / (IgnitionTemperature - 250f))
                    * Math.Min(1f, Math.Max(ragArea, AreaPerPiece * 0.5f) / Math.Max(1e-6f, area)));

            // The burning surface gasifies by its net heat flux.
            var ragShare = Involved > 0f ? Math.Min(1f, ragArea / Involved) : 0f;
            var net = OwnFlameFlux + mutual + external + ragFlux * ragShare - Loss(BurningSurface) - CoverLoss * cover;
            var massFlux = Involved > 0f ? Math.Max(0f, net) / Gasification : 0f;
            // Piloted ignition of that surface: a patch catches.
            if (Involved <= 0f && Wood > 0f && frontTemperature >= IgnitionTemperature && (ragBurning || pilot))
                Involved = Math.Min(area, ragArea > 0f ? ragArea : AreaPerPiece * 0.5f);
            // Flame spread: the area beside the flames ignites after its
            // ignition delay, while those flames hold (or the rag's burns).
            else if (Involved > 0f && Wood > 0f && incident > critical && (massFlux >= CriticalMassFlux || ragBurning))
            {
                var rise = IgnitionTemperature - environment;
                var drive = incident - critical;
                var delay = ThermalInertia / 1.5f * (rise / drive) * (rise / drive);
                Involved = Math.Min(area, Involved + SpreadFactor * Involved * (1f - Involved / area) * h / Math.Max(1f, delay));
            }

            var dry = Math.Min(Wood * (1f - Moisture), massFlux * Involved * h * Pace);
            var wet = dry / (1f - Moisture);
            Wood = Math.Max(0f, Wood - wet);
            if (Wood < 1e-6f) Wood = 0f;
            // Flames need wood to gasify: with the pile's wood gone, its char
            // only glows (preview 950's check drew flames over bare char).
            var spent = Wood <= 0f;
            if (spent) Involved = 0f;
            Char += dry * CharYield;
            var volatiles = dry * (1f - CharYield);
            var burnt = Math.Min(volatiles, air / VolatileAir);
            air -= burnt * VolatileAir;
            var flameHeat = burnt * VolatileHeat;
            heat += flameHeat - (wet - dry) * EvaporationEnergy;
            // Air-starved, the flame is smaller and gives back less heat.
            var completeness = volatiles > 0f ? burnt / volatiles : 1f;
            if (Involved > 0f)
            {
                fluxSum += massFlux;
                var sustain = completeness * massFlux;
                if (sustain < CriticalMassFlux)
                    Involved = Math.Max(0f, Involved - Involved * h / ExtinctionTime * (1f - sustain / CriticalMassFlux));
                if (Involved < 1e-5f) Involved = 0f;
                SurfaceChar = Math.Max(SurfaceChar, Flaming);
                if (Char > 0f) Glowing = true;
            }

            var kept = Involved > 0f || coalFlux > 0f || neighborFlux > 0f || Char >= SelfGlowingChar || spent && Glowing;
            Glowing = Char > 0f && (Glowing || Involved > 0f || coalFlux > 0f || neighborFlux > 0f) && kept;
            var charRate = Char * Pace / CharTime * (1f - 0.6f * Flaming);
            // A burned-down pile's last embers burn out, not on for ever ever more slowly.
            if (spent) charRate = Math.Max(charRate, EmberFloor);
            var charBurnt = Glowing ? Math.Min(Char, Math.Min(charRate * h, air / CharAir)) : 0f;
            Char -= charBurnt;
            if (Char < 1e-6f) { Char = 0f; Glowing = false; }
            air -= charBurnt * CharAir;
            AshProduced += charBurnt * AshFraction / CharYield;
            var charHeat = charBurnt * CharHeat;
            heat += charHeat;
            support += flameHeat + charHeat;
            airUsed += airStart - air;

            var unburnt = volatiles > 0f ? 1f - completeness : 0f;
            smoke += volatiles > 0f ? Smoothing.Clamp01(0.15f + 0.8f * unburnt + 0.4f * cover) : (oil > 0f ? 0.1f : 0f);
            if (Wood + Char <= 0f) { Involved = 0f; Glowing = false; }
        }

        private void CoolFront(float limit)
        {
            if (frontTemperature <= limit) return;
            var depth = Math.Max(0.0005f, (float)Math.Sqrt(6f * Diffusivity * FrontTime));
            frontTemperature = limit;
            FrontEnergy = Math.Max(0f, (limit - environment) * HeatCapacity * depth / 3f);
        }

        public float Dump(float fraction)
        {
            fraction = Smoothing.Clamp01(fraction);
            var removed = Mass * fraction;
            Wood *= 1f - fraction; Char *= 1f - fraction; RagOil *= 1f - fraction; Involved *= 1f - fraction;
            if (Mass < 0.00001f) { removed += Mass; Clear(); }
            return removed;
        }

        public void MirrorOutput(float heat, float smoke)
        {
            Heat = RagFuel.Positive(heat);
            Smoke = Smoothing.Clamp01(RagFuel.Positive(smoke));
        }

        public float[] Save()
        {
            var state = new float[StateLength];
            Save(state, 0);
            return state;
        }

        public void Save(float[] state, int offset)
        {
            state[offset] = Wood; state[offset + 1] = Char; state[offset + 2] = FrontEnergy; state[offset + 3] = FrontTime;
            state[offset + 4] = RagOil; state[offset + 5] = RagLit ? 1f : 0f; state[offset + 6] = Glowing ? 1f : 0f;
            state[offset + 7] = Involved; state[offset + 8] = SurfaceChar; state[offset + 9] = RagCount;
        }

        public void Restore(float[] state) => Restore(state, 0, state?.Length ?? 0);

        /// <summary>Restores this zone's values from <paramref name="length"/> saved values (the current 10 or the first release's 6).</summary>
        public void Restore(float[] state, int offset, int length)
        {
            Clear();
            if (state == null || (length != StateLength && length != LegacyStateLength) || offset < 0 || offset + length > state.Length) return;
            for (var i = offset; i < offset + length; i++) if (float.IsNaN(state[i]) || float.IsInfinity(state[i])) return;
            Wood = Math.Min(ZoneCapacity, Math.Max(0f, state[offset]));
            Char = Math.Min(ZoneCapacity, Math.Max(0f, state[offset + 1]));
            if (length == LegacyStateLength)
            {
                // The first preview saved a surface temperature and one burning flag.
                RagOil = Math.Max(0f, Math.Min(4f * RagFuel.OilCapacity, state[offset + 3]));
                RagCount = (int)Math.Ceiling(RagOil / RagFuel.OilCapacity - 1e-4f);
                RagLit = state[offset + 4] == 1f && RagOil > 0f;
                var burning = state[offset + 5] == 1f && Wood + Char > 0f;
                Glowing = burning && Char > 0f;
                Involved = burning && Wood > 0f ? 0.5f * Area : 0f;
                SurfaceChar = burning ? 0.5f : 0f;
                FrontTime = 60f;
                frontTemperature = Math.Max(environment, Math.Min(IgnitionTemperature, state[offset + 2]));
                var depth = (float)Math.Sqrt(6f * Diffusivity * FrontTime);
                FrontEnergy = Math.Max(0f, (frontTemperature - environment) * HeatCapacity * depth / 3f);
                return;
            }
            FrontEnergy = Math.Max(0f, Math.Min(1e8f, state[offset + 2]));
            FrontTime = Math.Max(0f, Math.Min(1e6f, state[offset + 3]));
            RagOil = Math.Max(0f, Math.Min(4f * RagFuel.OilCapacity, state[offset + 4]));
            RagCount = RagOil > 0f ? Math.Max(1, Math.Min(4, (int)state[offset + 9])) : 0;
            RagLit = state[offset + 5] == 1f && RagOil > 0f;
            Glowing = state[offset + 6] == 1f && Char > 0f;
            Involved = Wood > 0f ? Math.Max(0f, Math.Min(Area, state[offset + 7])) : 0f;
            SurfaceChar = Smoothing.Clamp01(state[offset + 8]);
            var d = Math.Max(0.0005f, (float)Math.Sqrt(6f * Diffusivity * FrontTime));
            frontTemperature = Math.Min(IgnitionTemperature + 50f, environment + 3f * FrontEnergy / (HeatCapacity * d));
        }
    }
}
