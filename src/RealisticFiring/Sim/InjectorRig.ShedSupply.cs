using LocoSim.Implementations;
using UnityEngine;

namespace RealisticFiring.Sim
{
    internal sealed partial class InjectorRig
    {
        /// <summary>The shed's water hose, from the mains, its stand's valve wide open, in kg/s.</summary>
        public const float HoseRate = 15f;

        /// <summary>A hose only fills a boiler with no pressure in it, absolute bar.</summary>
        public const float HoseMaxPressure = 1.5f;

        /// <summary>That pressure in gauge bar, matching the cab and overlay.</summary>
        public const float HoseMaxGauge = HoseMaxPressure - 1f;

        /// <summary>The hose fill stops at this water level, as a fireman fills to the top of the glass.</summary>
        public const float HoseFillLevel = 0.85f;

        /// <summary>
        /// A repair stand's water, the plant's hot water for filling before
        /// direct steaming: 180 °F (National Boiler Washing Co., Railway Age
        /// 18 July 1925, pp. 146-147).
        /// </summary>
        public const float HotWaterTemperature = 82f;

        /// <summary>
        /// The plant's steam at the engine for direct steaming, absolute bar:
        /// 150 psi (direct steaming from 150 lb, RME June 1927, p. 363).
        /// </summary>
        public const float PlantPressure = 11.36f;

        /// <summary>That pressure in gauge bar, matching the cab and overlay.</summary>
        public const float PlantGauge = PlantPressure - 1f;

        /// <summary>
        /// Direct steam through the repair stand's hose and the engine's
        /// connection, wide open with the boiler below about half the plant's
        /// pressure, kg/s: Napier's rule through a 1½-in. bore, the 2-in.
        /// blow-off connection less the drop's losses (165 psia × 1.77 in.² /
        /// 70 = 4.2 lb/s).
        /// </summary>
        public const float DirectSteamMost = 1.9f;

        // Below this share of the plant's pressure the flow is choked, the
        // most the bore passes; above it, it falls to none at the plant's.
        private const float ChokedBelow = 0.58f;

 public const float SteamFullLevel = 0.95f;
        private const float SteamRoomAgain = 0.92f;

        // Steaming starts over this flow and ends under the second, kg/s, so
        // a trickle at the plant's pressure does not flap on and off.
        private const float SteamingFrom = 0.05f;
        private const float SteamingUnder = 0.01f;

        /// <summary>Direct steam into a boiler at <paramref name="boilerPressure"/> (absolute bar), its valve open <paramref name="opening"/>, kg/s.</summary>
        public static float DirectSteamFlow(float opening, float boilerPressure)
        {
            var ratio = boilerPressure / PlantPressure;
            if (!(opening > 0f) || ratio >= 1f)
                return 0f;
            var past = Mathf.Max(0f, ratio - ChokedBelow) / (1f - ChokedBelow);
            return DirectSteamMost * Mathf.Clamp01(opening) * Mathf.Sqrt(1f - past * past);
        }

 public bool HoseIn { get; set; }
        public float HoseOpening { get; set; }

 public bool HoseHot { get; set; }
        public float HoseSteam { get; set; }

        /// <summary>The plant's steam let into the boiler on the last tick, kg/s.</summary>
        public float SteamRate { get; set; }

        /// <summary>Whether the plant's steam is going in, starting and ending past a margin (the host's, shown to clients).</summary>
        public bool Steaming { get; set; }

        /// <summary>Whether the plant's steam is held off with the boiler full of its water, until blown down.</summary>
        public bool SteamFull { get; set; }

        /// <summary>
        /// What the plant's steam and hot water have put on the engine's bill
        /// since the hose went in (<see cref="PlantFee"/>): water and fuel, kg,
        /// and the fuel's kind (coal, or an oil burner's fuel).
        /// </summary>
        public float PlantWaterBilled { get; private set; }
        public float PlantFuelBilled { get; private set; }
        public string PlantFuelName { get; private set; } = "coal";

        private float plantSteamOwed;
        private float plantHotOwed;
        private float plantClock;
        private bool hoseWasIn;

        /// <summary>The plant's steam let in this tick, kg: pure water, no feed's solids.</summary>
        public float SteamedLastStep { get; private set; }

        /// <summary>Whether the hose put water in on the last tick.</summary>
        public bool HoseFilling { get; set; }

        // This plugging's washout is done; one was done since last asked.
        private bool hoseWashed;
        private bool washedOut;

        /// <summary>Whether the hose washed the boiler out since last asked, once, for a notice.</summary>
        public bool TakeWashedOut()
        {
            var washed = washedOut;
            washedOut = false;
            return washed;
        }

        private void StepHose(bool broken)
        {
            if (Car != null)
            {
                var line = Shed.ShedConnections.WaterLine(Car);
                HoseIn = line.In;
                HoseOpening = line.In ? Mathf.Clamp01(line.Opening) : 0f;
                HoseHot = line.In && line.Hot;
                HoseSteam = line.In ? Mathf.Clamp01(line.Steam) : 0f;
            }
            if (!HoseIn)
                hoseWashed = false;
            if (HoseIn && !hoseWasIn)
                PlantWaterBilled = PlantFuelBilled = 0f;
            else if (!HoseIn && hoseWasIn)
                BillPlant();
            hoseWasIn = HoseIn;
            var flowing = HoseIn && HoseOpening > 0f && BoilerPressure <= HoseMaxPressure;
            if (flowing && !hoseWashed)
            {
                hoseWashed = true;
                WashOut();
            }
            var filling = flowing && !broken && PhysicalWaterLevel < HoseFillLevel;
            if (filling != HoseFilling)
                Main.Mod?.Logger.Log($"{Car?.ID}: water hose {(filling ? "filling" : "stopped")} at {BoilerPressure:F2} bar, water {PhysicalWaterLevel:P0}");
            HoseFilling = filling;
        }

        private void StepDirectSteam(WaterPressureVessel vessel, float delta, float space, bool broken)
        {
            SteamedLastStep = 0f;
            if (Net.IsClient)
                return;
            if (!SteamFull && PhysicalWaterLevel >= SteamFullLevel && HoseIn && HoseSteam > 0f)
            {
                SteamFull = true;
                Main.Mod?.Logger.Log($"{Car?.ID}: direct steam held off: the boiler is full at {BoilerPressure:F2} bar, water {PhysicalWaterLevel:P0}");
            }
            else if (SteamFull && (PhysicalWaterLevel < SteamRoomAgain || !HoseIn))
                SteamFull = false;
            var steam = HoseIn && !broken && !SteamFull ? Mathf.Min(DirectSteamFlow(HoseSteam, BoilerPressure) * StartupBoost() * delta, space) : 0f;
            if (steam > 0f)
            {
                vessel.mass += steam;
                vessel.enthalpy += SteamTables.SteamSpecificEnthalpy(PlantPressure) * steam;
                SteamedLastStep = steam;
            }
            SteamRate = steam / delta;
            var steaming = Steaming ? SteamRate > SteamingUnder : SteamRate > SteamingFrom;
            if (steaming != Steaming)
                Main.Mod?.Logger.Log($"{Car?.ID}: direct steam {(steaming ? "in" : "stopped")} at {BoilerPressure:F2} bar, water {WaterLevel:P0}");
            Steaming = steaming;
        }

        // The plant's steam and hot water go on the engine's bill, paid at
        // the Career Manager (PlantFee), a few seconds' at a time. In game,
        // on the host only.
        private void ChargePlant(float delta, float steamKg, float hotKg)
        {
            if (Car == null || Net.IsClient)
                return;
            plantSteamOwed += steamKg;
            plantHotOwed += hotKg;
            if ((plantClock -= delta) > 0f)
                return;
            plantClock = PlantFee.BillEvery;
            BillPlant();
        }

        private void BillPlant()
        {
            if (Car == null || !(plantSteamOwed > 0f) && !(plantHotOwed > 0f))
                return;
            var billed = PlantFee.Bill(Car, plantSteamOwed, plantHotOwed);
            plantSteamOwed = plantHotOwed = 0f;
            PlantWaterBilled += billed.Water;
            PlantFuelBilled += billed.Fuel;
            if (billed.FuelType.HasValue)
                PlantFuelName = billed.FuelType == DV.ThingTypes.ResourceType.Coal ? "coal" : "fuel";
        }

        // The game's start-up speeding of a fire, standing below working
        // pressure; none where the engine has no fire of the mod's.
        private float StartupBoost() =>
            Car != null && FireboxRegistry.TryGet(Car, out var coal) ? coal.StartupMultiplier
            : Car != null && RealisticOilFirebox.TryGet(Car, out var oil) ? oil.StartupBoost
            : 1f;

        // A washout, with the boiler cold at the shed, where there is
        // something to wash out.
        private void WashOut()
        {
            if (!BoilerWaterRig.TryGet(boiler, out var water) || !(Main.Settings.BoilerWaterImpurities || water.Plug != null))
                return;
            water.Washout();
            washedOut = true;
        }
    }
}
