using System;
using System.Linq;

namespace RealisticFiring.Core
{
    public sealed partial class FireModel
    {
        // Radiation onto wood from a zone's burning coal at its full ignition
        // support, W/m²; a working coal fire's radiation is 50-100 kW/m².
        private const float CoalFluxOnWood = 40000f;

        // Flame from wood burning in the next zone, W/m², at full involvement.
        private const float NeighborWoodFlux = 20000f;
        private const float WoodHeatToCoal = 0.3f;

        private readonly float[] coalOnWood = new float[GrateLayout.ZoneCount];
        private readonly float[] freshBefore = new float[GrateLayout.ZoneCount];
        private readonly float[] starterCoalFlux = new float[GrateLayout.ZoneCount];
        private readonly float[] starterNeighborFlux = new float[GrateLayout.ZoneCount];
        private readonly float[] woodIgnition = new float[GrateLayout.ZoneCount];
        private readonly float[] woodHeat = new float[GrateLayout.ZoneCount];

        public StarterFuel[] Starters { get; } = Enumerable.Range(0, GrateLayout.ZoneCount).Select(_ => new StarterFuel()).ToArray();

        public float StarterMass
        {
            get
            {
                var mass = 0f;
                foreach (var s in Starters) mass += s.Mass;
                return mass;
            }
        }

        /// <summary>The unburned wood in the box, kg.</summary>
        public float StarterWood
        {
            get
            {
                var mass = 0f;
                foreach (var s in Starters) mass += s.Wood;
                return mass;
            }
        }

        public bool StarterAlight
        {
            get
            {
                foreach (var s in Starters)
                    if (s.Alight)
                        return true;
                return false;
            }
        }

 public GrateZone StarterZone(GrateZone aimed)
        {
            var best = -1;
            var most = NegligibleMass;
            for (var i = 0; i < Starters.Length; i++)
            {
                if (Starters[i].Mass > most)
                {
                    most = Starters[i].Mass;
                    best = i;
                }
            }
            return best >= 0 ? (GrateZone)best : aimed;
        }

        /// <summary>Fresh coal fired onto a zone's wood after it was laid, kg: what smothers the wood. Coal already under it does not.</summary>
        public float CoalOnWood(GrateZone zone) => coalOnWood[(int)zone];

        /// <summary>A zone's coal ignition support from its wood fire and its neighbors', 0 to 1, before any startup speedup.</summary>
        public float WoodIgnitionSupport(GrateZone zone) => woodIgnition[(int)zone];

        public float AddWood(float mass, GrateZone zone) => Starters[(int)zone].AddWood(mass,
            Math.Max(0f, parameters.MaxCoalCapacity - CoalMass - StarterMass) + Starters[(int)zone].Mass);

        public bool AddRag(float oil, bool lit, GrateZone zone) => Starters[(int)zone].AddRag(oil, lit);

        public float[] SaveStarters()
        {
            var state = new float[Starters.Length * StarterFuel.StateLength];
            for (var i = 0; i < Starters.Length; i++)
                Starters[i].Save(state, i * StarterFuel.StateLength);
            return state;
        }

        /// <summary>Restores the zones' starter fuel; the first preview's six values a zone still load.</summary>
        public void RestoreStarters(float[] state)
        {
            var stride = state != null && state.Length % Starters.Length == 0 ? state.Length / Starters.Length : 0;
            for (var i = 0; i < Starters.Length; i++)
                Starters[i].Restore(state, i * stride, stride);
            // A new-format cold start has no synthetic lighter support.
            if (state != null)
                lighter = 0f;
        }

        private void BeforeCoal()
        {
            for (var i = 0; i < zones.Length; i++)
                freshBefore[i] = zones[i].Fresh;
        }

        // Coal that landed on a zone's wood since BeforeCoal lies on top of it.
        private void AfterCoal()
        {
            for (var i = 0; i < zones.Length; i++)
                if (Starters[i].Wood + Starters[i].Char > NegligibleMass)
                    coalOnWood[i] += Math.Max(0f, zones[i].Fresh - freshBefore[i]);
        }

        // Wood or rag in the zone or beside it: its coal may be catching from the wood.
        private bool WoodNear(int zone)
        {
            if (Starters[zone].Mass > NegligibleMass)
                return true;
            var neighbors = GrateLayout.Neighbors((GrateZone)zone);
            for (var k = 0; k < neighbors.Count; k++)
                if (Starters[(int)neighbors[k]].Mass > NegligibleMass)
                    return true;
            return false;
        }

        // The zone holding the most wood, the fire's; -1 with none.
        private int WoodZone()
        {
            var fire = -1;
            var most = NegligibleMass;
            for (var i = 0; i < Starters.Length; i++)
            {
                var wood = Starters[i].Wood + Starters[i].Char;
                if (wood > most)
                {
                    most = wood;
                    fire = i;
                }
            }
            return fire;
        }

        private void GatherRags()
        {
            var fire = WoodZone();
            if (fire < 0)
                return;
            for (var i = 0; i < Starters.Length; i++)
                if (i != fire && Starters[i].Wood + Starters[i].Char <= NegligibleMass)
                    Starters[i].MoveRagsTo(Starters[fire]);
        }

        private void StepStarters(float dt, float dryAir, float boilerTemperature, bool drowned, out float starterHeat, out float starterSmoke)
        {
            starterHeat = 0f;
            starterSmoke = 0f;
            GatherRags();
            var flamingAnywhere = 0f;
            foreach (var starter in Starters)
                flamingAnywhere = Math.Max(flamingAnywhere, starter.Flaming);
            for (var i = 0; i < zones.Length; i++)
            {
                var neighbors = GrateLayout.Neighbors((GrateZone)i);
                var flaming = flamingAnywhere;
                var burning = 0f;
                for (var k = 0; k < neighbors.Count; k++)
                    burning += zones[(int)neighbors[k]].Burning;
                // Coal fired onto the wood smothers it; coal under it doesn't, and what has caught no longer covers it.
                coalOnWood[i] = Starters[i].Wood + Starters[i].Char > NegligibleMass ? Math.Min(coalOnWood[i], zones[i].Fresh) : 0f;
                starterNeighborFlux[i] = NeighborWoodFlux * flaming;
                var coal = (zones[i].Burning + tuning.NeighborIgnitionWeight * burning / neighbors.Count) / tuning.IgnitionBurningMass;
                starterCoalFlux[i] = FireOn && zones[i].Burning + burning > NegligibleMass ? CoalFluxOnWood * Smoothing.Clamp01(coal) : 0f;
            }
            var fireZone = WoodZone();
            var boxAir = 0f;
            foreach (var zone in zones)
                boxAir += zone.AirFlow;
            for (var i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var starter = Starters[i];
                var whole = i == fireZone && boxAir > 0f;
                starter.Step(dt, (whole ? boxAir : zone.AirFlow) * dryAir, coalOnWood[i], starterCoalFlux[i], starterNeighborFlux[i],
                    boilerTemperature, drowned);
                // Reserve the actual oxygen consumed before the coal and over-fire stages.
                var used = starter.AirUsed / Math.Max(0.001f, dryAir);
                if (whole)
                {
                    var share = Math.Min(1f, used / boxAir);
                    foreach (var other in zones)
                        other.AirFlow = Math.Max(0f, other.AirFlow * (1f - share));
                    boxAir = 0f;
                }
                else
                    zone.AirFlow = Math.Max(0f, zone.AirFlow - used);
                zone.Ash += starter.AshProduced;
                starterHeat += starter.Heat;
                starterSmoke = Math.Max(starterSmoke, starter.Smoke);
            }
            // Coal catches from the wood fire's own heat as from the same heat
            // of burning coal (IgnitionBurningMass is full support).
            var reference = tuning.IgnitionBurningMass * FireboxParameters.CoalHeatContent / carbonBurnTime;
            // The wood fire laid over the bed heats the coal across it, not only the coal beside its own zone.
            var woodSupport = 0f;
            foreach (var starter in Starters)
                woodSupport = Math.Max(woodSupport, starter.CoalSupportHeat);
            for (var i = 0; i < zones.Length; i++)
            {
                woodHeat[i] = Math.Max(Starters[i].CoalSupportHeat, woodSupport);
                woodIgnition[i] = Smoothing.Clamp01(woodHeat[i] / reference);
            }
            if (StarterAlight)
                FireOn = true;
        }
    }
}
