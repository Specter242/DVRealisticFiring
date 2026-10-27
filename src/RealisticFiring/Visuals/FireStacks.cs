using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticStackEffects;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// A realistic fire under its chimney plume (<see cref="IStackFire"/>):
    /// what the coal fire and the oil burner share, the fusible plug's steam,
    /// the realistic cylinders' wet exhaust, and the fitted draft's steam.
    /// </summary>
    internal abstract class RealisticStack : IStackFire
    {
        // The share of the walls' condensate still water as the exhaust leaves.
        private const float ExhaustWater = 0.5f;

        protected readonly TrainCar Car;
        private BoilerWaterRig boilerWater;
        private EngineCylinders cylinders;
        private float cylindersLookedFor = float.NegativeInfinity;

        protected RealisticStack(TrainCar car) => Car = car;

        public abstract bool Tended { get; }

        public void Read(float dt, SteamExhaust exhaust, ref StackReading reading)
        {
            ReadFire(dt, ref reading);
            reading.PlugSteam = boilerWater != null || BoilerWaterRig.TryGet(Car, out boilerWater) ? boilerWater.PlugVent : 0f;
            reading.ExhaustWetness = ExhaustWetness();
            reading.StackSteam = StackSteam(exhaust);
            reading.BlowerSteam = exhaust != null ? BlowerFit.BlowerSteam(exhaust) + BlowerFit.PumpSteam(exhaust) : 0f;
            reading.NaturalFlow = NaturalFlow(exhaust);
        }

        protected abstract void ReadFire(float dt, ref StackReading reading);

        // The exhaust's water: the share of the steam the cylinders' walls
        // condensed, half of it still water as it leaves (a saturated engine
        // or cold cylinders; a superheated engine's, none). Unknown while the
        // engine takes no steam.
        private float ExhaustWetness()
        {
            if (cylinders == null && Time.time - cylindersLookedFor > 1f)
            {
                cylindersLookedFor = Time.time;
                EngineCylinders.TryGet(Car, out _, out cylinders);
            }
            if (cylinders == null || !(cylinders.Admitted > 0f))
                return float.NaN;
            return Mathf.Clamp(ExhaustWater * cylinders.Condensed / cylinders.Admitted, 0f, 0.3f);
        }

        // The steam going up the stack, kg/s: the engine's exhaust, the
        // blower's, and the air pump's, as the exhaust works them out.
        private static float StackSteam(SteamExhaust exhaust)
        {
            if (exhaust == null)
                return 0f;
            return Mathf.Max(0f, exhaust.exhaustFlow.Value) + Mathf.Max(0f, BlowerFit.BlowerSteam(exhaust)) + BlowerFit.PumpSteam(exhaust);
        }

        // The stock total flow through the smokebox counts the stack's natural
        // draft, air that flows even through a dead engine; the plume wants
        // the steam (exhaust and blower) and the air it draws, so a cold
        // engine shows no stream. The natural draft's share of it, as the
        // stock exhaust works it out.
        private static float NaturalFlow(SteamExhaust exhaust)
        {
            if (exhaust == null)
                return float.NaN;
            var engineMax = exhaust.engineMaxFlowReader.Value;
            var maxTotal = BlowerFit.TryGet(exhaust, out var draft)
                ? draft.MaxTotal(engineMax)
                : engineMax + exhaust.passiveExhaust + exhaust.entrainmentRatio * engineMax;
            if (!(maxTotal > 0f))
                return float.NaN;
            return BlowerFit.Natural(exhaust) * Mathf.Lerp(SteamExhaust.MIN_DAMPER, 1f, exhaust.damperControl.Value) / maxTotal;
        }
    }

    /// <summary>
    /// A coal fire's smoke: its soot, a shaken grate's ash, and the
    /// firebox's gas and smoke-out through the tubes a moment later; a
    /// stoker's lost fines and a shaken grate's coal as cinders.
    /// </summary>
    internal sealed class CoalStack : RealisticStack
    {
        // A grate shaken while the engine works hard throws live coal and ash
        // up the stack: "a cloud of smoke and a hail of sparks are belched
        // from the stack" (Baker p.63). Its ash darkens the smoke, clearing
        // over this time, in s.
        private const float ShakeSmokePerKg = 8f;
        private const float ShakeSmokeClearing = 3f;

        // The firebox's smoke takes about this long through the tubes and
        // smokebox to the stack, in s.
        private const float TubeTransit = 1.5f;

        private readonly RealisticFirebox firebox;
        private float tubeGas;
        private float tubeSmokeOut;
        private float shakeCindersSeen = float.NaN;
        private float shakeSmoke;

        public CoalStack(TrainCar car, RealisticFirebox firebox) : base(car) => this.firebox = firebox;

        public override bool Tended => FireboxRegistry.TryGetTended(PlayerManager.Car, out var coal) && coal == firebox;

        protected override void ReadFire(float dt, ref StackReading reading)
        {
            var fire = firebox.Model;
            var shaken = float.IsNaN(shakeCindersSeen) ? 0f : Mathf.Max(0f, fire.ShakeCinders - shakeCindersSeen);
            shakeCindersSeen = fire.ShakeCinders;
            shakeSmoke = Mathf.Clamp01(shakeSmoke * Mathf.Exp(-dt / ShakeSmokeClearing) + shaken * ShakeSmokePerKg);
            var transit = 1f - Mathf.Exp(-dt / TubeTransit);
            tubeGas += (firebox.Smoke.TotalGasRate - tubeGas) * transit;
            tubeSmokeOut += (firebox.Smoke.TotalSmokeOutRate - tubeSmokeOut) * transit;

            reading.FireOn = fire.FireOn;
            reading.Heat = fire.Heat;
            reading.MostHeat = fire.Parameters.MaxHeatOutput;
            reading.Soot = Mathf.Max(fire.SootDensity, shakeSmoke);
            reading.FireboxGas = tubeGas;
            reading.FireboxSmokeOut = tubeSmokeOut;
            // No smoke to see and air to spare: a clear stack, as an oil fire's with too much air.
            var air = fire.PrimaryAirFlow + fire.DoorAirFlow;
            reading.AirThrough = air;
            var fireboxSoot = PlumeModel.FireboxSoot(new PlumeInputs { FireOn = fire.FireOn, FireboxGas = tubeGas, FireboxSmokeOut = tubeSmokeOut });
            reading.ClearStack = PlumeModel.CoalClearStack(Mathf.Max(reading.Soot, fireboxSoot), air > 0f ? fire.ExcessAirFlow / air : 0f);
            reading.Cinders = true;
            reading.Fines = firebox.LastStoker is StokerFlow stoked ? stoked.CinderRate * dt : 0f;
            reading.ShakenCoal = shaken;
        }
    }

    /// <summary>An oil fire's smoke: its stack's darkness (a sanding's and a flareback's black clouds with it), clear with too much air.</summary>
    internal sealed class OilStack : RealisticStack
    {
        private readonly RealisticOilFirebox oil;

        public OilStack(TrainCar car, RealisticOilFirebox oil) : base(car) => this.oil = oil;

        public override bool Tended => RealisticOilFirebox.TryGetTended(PlayerManager.Car, out var burner) && burner == oil;

        protected override void ReadFire(float dt, ref StackReading reading)
        {
            var (soot, clear) = OilPlume.Stack(oil.StackDarkness);
            reading.FireOn = oil.Model.FireOn;
            reading.Heat = oil.Model.Heat;
            reading.MostHeat = oil.Model.Parameters.MaxHeatOutput;
            reading.Soot = soot;
            reading.ClearStack = clear;
            reading.AirThrough = oil.AirFlowIn;
        }
    }
}
