using System;
using System.Linq;

namespace RealisticFiring.Core
{
    public sealed partial class FireModel
    {
        /// <summary>
        /// Restores from a stock save, which only knows the total coal. A lit
        /// fire comes back evenly alight; an unlit one as fresh coal. The
        /// ashpan comes back empty.
        /// </summary>
        public void Restore(bool fireOn, float coalMass)
        {
            Ashpan = 0f;
            foreach (var starter in Starters) starter.Clear();
            coalMass = Math.Max(0f, Math.Min(Smoothing.Finite(coalMass), parameters.MaxCoalCapacity));
            foreach (var zone in zones)
            {
                zone.Clear();
                if (fireOn)
                    zone.Burning = coalMass * zoneArea;
                else
                    zone.Fresh = coalMass * zoneArea;
            }
            FireOn = fireOn;
            lighter = fireOn && BurningMass < tuning.DyingEmbers ? tuning.LighterTime : 0f;
            DryWater();
        }

 public void LayReadyFire(bool lit)
        {
            Ashpan = 0f;
            foreach (var starter in Starters) starter.Clear();
            var coalMass = parameters.MaxCoalCapacity * tuning.ReferenceDepthFraction;
            var weightSum = 0f;
            foreach (var weight in draftWeights)
                weightSum += weight * weight;
            for (var i = 0; i < zones.Length; i++)
            {
                var share = weightSum > 0f ? draftWeights[i] * draftWeights[i] / weightSum : zoneArea;
                var mass = Math.Min(coalMass * share, parameters.MaxCoalCapacity * zoneArea);
                zones[i].Clear();
                if (lit)
                    zones[i].Burning = mass;
                else
                    zones[i].Fresh = mass;
            }
            FireOn = lit;
            lighter = lit && BurningMass < tuning.DyingEmbers ? tuning.LighterTime : 0f;
            DryWater();
        }

        /// <summary>
        /// Brings the coal to <paramref name="coalMass"/> keeping the bed's
        /// shape: each zone's fresh and burning coal scaled alike, or spread
        /// evenly (burning if the fire is on) onto an empty grate. For a fire
        /// that follows another's total, as a multiplayer client's does the host's.
        /// </summary>
        public void MatchCoal(float coalMass)
        {
            coalMass = Math.Max(0f, Math.Min(coalMass, parameters.MaxCoalCapacity));
            var current = CoalMass;
            if (current < NegligibleMass)
            {
                foreach (var zone in zones)
                {
                    if (FireOn)
                        zone.Burning += coalMass * zoneArea;
                    else
                        zone.Fresh += coalMass * zoneArea;
                }
                return;
            }
            var scale = coalMass / current;
            foreach (var zone in zones)
            {
                zone.Fresh *= scale;
                zone.Burning *= scale;
            }
        }

        /// <summary>Zone masses as a flat array: fresh, burning, ash, clinker per zone.</summary>
        public float[] SaveZones()
        {
            var data = new float[GrateLayout.ZoneCount * 4];
            for (var i = 0; i < zones.Length; i++)
            {
                data[i * 4] = zones[i].Fresh;
                data[i * 4 + 1] = zones[i].Burning;
                data[i * 4 + 2] = zones[i].Ash;
                data[i * 4 + 3] = zones[i].Clinker;
            }
            return data;
        }

        /// <summary>Coal (fresh plus burning) in data written by <see cref="SaveZones"/>.</summary>
        public static float SavedCoalMass(float[] data)
        {
            var coal = 0f;
            if (data == null)
                return coal;
            for (var i = 0; i + 1 < data.Length; i += 4)
                coal += data[i] + data[i + 1];
            return coal;
        }

        /// <summary>
        /// Shows another fire's zones (fresh, burning, ash, clinker each, as
        /// <see cref="SaveZones"/> gives them), keeping this fire's own air
        /// and burn rates, so a mirror (a multiplayer client's) does not dim
        /// between updates. Returns false and changes nothing on bad data.
        /// </summary>
        public bool MirrorZones(bool fireOn, float[] data)
        {
            if (data == null || data.Length != GrateLayout.ZoneCount * 4 || data.Any(value => !(value >= 0f) || float.IsInfinity(value)))
                return false;
            for (var i = 0; i < zones.Length; i++)
            {
                zones[i].Fresh = data[i * 4];
                zones[i].Burning = data[i * 4 + 1];
                zones[i].Ash = data[i * 4 + 2];
                zones[i].Clinker = data[i * 4 + 3];
            }
            FireOn = fireOn;
            lighter = fireOn && BurningMass < tuning.DyingEmbers ? tuning.LighterTime : 0f;
            DryWater();
            return true;
        }

        /// <summary>Restores zones written by <see cref="SaveZones"/>; false if the data does not fit.</summary>
        public bool RestoreZones(bool fireOn, float[] data)
        {
            if (data == null || data.Length != GrateLayout.ZoneCount * 4)
                return false;

            foreach (var value in data)
            {
                if (!(value >= 0f) || float.IsInfinity(value))
                    return false;
            }
            if (SavedCoalMass(data) > parameters.MaxCoalCapacity * 1.001f)
                return false;

            for (var i = 0; i < zones.Length; i++)
            {
                zones[i].Clear();
                zones[i].Fresh = data[i * 4];
                zones[i].Burning = data[i * 4 + 1];
                zones[i].Ash = data[i * 4 + 2];
                zones[i].Clinker = data[i * 4 + 3];
            }
            FireOn = fireOn;
            lighter = fireOn && BurningMass < tuning.DyingEmbers ? tuning.LighterTime : 0f;
            DryWater();
            return true;
        }
    }
}
