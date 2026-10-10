using System;

namespace RealisticFiring.Core
{
    /// <summary>What the cab and the engine give an oil fire each tick.</summary>
    public struct OilFireInputs
    {
        /// <summary>The firing valve, 0 shut to 1 wide open.</summary>
        public float FiringValve;

        /// <summary>The atomizer valve, 0 shut to 1 wide open.</summary>
        public float Atomizer;

        /// <summary>The oil heater's valve, 0 shut to 1 wide open.</summary>
        public float Heater;

        /// <summary>Air drawn through the firebox, in kg/s (the stock draft, dampers included).</summary>
        public float AirFlow;

        /// <summary>The fire door's opening, 0 to 1.</summary>
        public float DoorOpening;

        /// <summary>How hard the exhaust beats, 0 standing to 1 working flat out.</summary>
        public float Exhaust;

        /// <summary>Boiler pressure, absolute bar.</summary>
        public float BoilerPressure;

        /// <summary>Boiler water temperature, °C.</summary>
        public float BoilerTemperature;

        /// <summary>Fuel the tender can give, in firebox kg.</summary>
        public float TenderFuel;

        /// <summary>Water at the burner (a drowned firebox), 0 to 1: over half puts the fire out.</summary>
        public float IntakeWater;

        /// <summary>The stock steam-raising speedup, 1 normally.</summary>
        public float StartupMultiplier;

        /// <summary>A shed's steam line on the burner's valves, absolute bar; 0 with none.</summary>
        public float SteamLinePressure;
    }

    /// <summary>What an oil fire shows, to mirror it elsewhere (a multiplayer client).</summary>
    public struct OilFireState
    {
        public bool FireOn;
        public float FeedRate;
        public float BurnRate;
        public float Heat;
        public float Temperature;
        public float BrickTemperature;
        public float OilTemperature;
        public float ExcessAir;
        public float Atomizing;
        public float AtomizerSteam;
        public float HeaterSteamUsed;
        public float Drumming;
        public float SootDensity;
        public float SmokeDensity;
        public float StackSmoke;
        public float Vapor;
        public float PooledOil;
        public float Torch;
        public float FlueSoot;
        public float SandBurst;
    }

 public sealed class OilFire
    {
        /// <summary>The firing valve wide open feeds this much over the stock fire's most, so it can be forced.</summary>
        public const float MaxFeedFactor = 1.15f;

        /// <summary>Atomizing steam needed per kg of oil, at full exhaust, in kg.</summary>
        public const float AtomizingSteamPerKg = 0.25f;

 public const float AtomizerCapacity = 1.5f;

        // The atomizer's need rises with the exhaust: standing it needs this share.
        private const float StandingAtomizing = 0.6f;

        // Past this much more atomizing than needed the fire drums, fully at
        // the next; the extra steam carries this heat away per kg.
        private const float DrummingStart = 2.5f;
        private const float DrummingFull = 5f;
        private const float SteamHeatCarried = 1.2e6f;

        // Poorly atomized oil goes unburned, and below this share drips.
        private const float UnatomizedLoss = 0.6f;
        private const float DripBelow = 0.5f;

        /// <summary>Air over the stoichiometric need that makes the light haze of a well-fired engine.</summary>
        public const float BestExcessAir = 1.2f;

        // A well-set fire's haze in the stack's darkness, which fades to a
        // clear stack by this excess air (period: a clear stack wastes oil).
        public const float HazeSmoke = 0.2f;
        public const float ClearStackAir = 2f;

        // The stack's soot is gentler than the flues' (black at a third of
        // the oil unburned) and follows the fire over this time, in s, as the
        // smoke works through the tubes.
        private const float StackSootGain = 3f;
        private const float StackSootTime = 2.5f;
        private float stackSoot;

        // Excess air past this wastes this share of the heat per unit of
        // excess, at most the cap.
        private const float WastefulExcessAir = 1.4f;
        private const float ExcessAirLoss = 0.12f;
        private const float MaxExcessAirLoss = 0.4f;

        /// <summary>The brick a fire holds at, hot, and a cold firebox's, in °C.</summary>
        public const float HotBrick = 1100f;
        public const float ColdBrick = 100f;

        // The brick heats toward the fire with this time constant and cools
        // toward the boiler with that one, faster with air through the box.
        private const float BrickHeatTime = 240f;
        private const float BrickCoolTime = 900f;

        /// <summary>Above this the brick relights oil by itself, and vaporizes it, in °C.</summary>
        public const float BrickRelights = 800f;
        public const float BrickVaporizes = 250f;

        // Excess air at which the draft blows the fire out: on cold brick, and
        // on hot; and the smallest fire cold and hot brick hold, as a share of
        // the stock most.
        private const float BlowOutColdBrick = 4f;
        private const float BlowOutHotBrick = 14f;
        private const float SmallestFireColdBrick = 0.08f;
        private const float SmallestFireHotBrick = 0.015f;

        // Without this much atomizing the oil does not burn.
        private const float FlameAtomizing = 0.2f;

        /// <summary>The lit waste thrown in to light the fire burns this long, in s.</summary>
        public const float TorchTime = 25f;

        /// <summary>Vapor and pooled oil past this, in firebox kg, flare back when lit.</summary>
        public const float FlarebackFuel = 0.3f;

        /// <summary>This much vapor and pooled oil makes the worst flareback, in firebox kg.</summary>
        public const float WorstFlareback = 4f;

        // Air that clears the firebox of vapor: a box-full, in kg.
        private const float BoxAir = 12f;

        // Pooled oil vaporizes off brick hotter than vaporizing with this time constant, in s.
        private const float PoolVaporizeTime = 30f;

        // Flue soot from smoke: a fire smoking black fouls the flues fully in
        // this long at full feed (sand every 10 to 12 miles smoking hard, K02
        // p. 46); even a clean fire gums them in this long.
        private const float SootingTime = 600f;
        private const float GummingTime = 6f * 3600f;

        /// <summary>Fully fouled flues pass this share less of the fire's heat.</summary>
        public const float SootedHeatLoss = 0.25f;

        /// <summary>
        /// Sand that scours fully fouled flues clean under a full pull, in kg:
        /// "about a quart at a time" every 10 to 12 miles smoking hard (K02 p. 46).
        /// </summary>
        public const float SandToScour = 1.5f;

        // The black cloud a sanding throws clears over this time, in s.
        private const float SandBurstTime = 6f;

        /// <summary>Air temperature, and the oil's best range for flow and atomizing, in °C.</summary>
        public const float AmbientTemperature = 15f;
        public const float WarmOil = 45f;
        public const float HotOil = 80f;

        // The heater wide open warms the tender's oil this fast, in °C/s; it
        // cools to the air with this time constant, in s.
        private const float HeaterWarming = 0.06f;
        private const float OilCoolTime = 4f * 3600f;

        // Heavy fuel oil hardly runs below this, in °C, and runs freely once
        // warm: the tender feeds the burner by gravity, so only the heater's
        // steam makes cold oil flow. Cold oil also wants this much more atomizing.
        public const float OilStops = 20f;
        private const float ColdOilAtomizing = 1.5f;

        /// <summary>Heater steam with the valve wide open, in kg/s.</summary>
        public const float HeaterSteam = 0.05f;

        // The fire answers the valve with this time constant, in s.
        private const float ResponseTime = 1.5f;

        private readonly FireboxParameters parameters;
        private float responseVelocity;
        private float temperatureVelocity;
        private float burning;
        private float clock;

        public OilFire(FireboxParameters parameters)
        {
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            BrickTemperature = ColdBrick;
            OilTemperature = WarmOil;
            Temperature = 100f;
        }

        public FireboxParameters Parameters => parameters;

        /// <summary>The firing valve wide open, in firebox kg/s.</summary>
        public float MaxFeed => MaxFeedFactor * parameters.MaxCombustionRate;

        public bool FireOn { get; private set; }

        /// <summary>Oil fed this tick, in firebox kg/s (what the tender gives).</summary>
        public float FeedRate { get; private set; }

        /// <summary>Oil burned, in firebox kg/s.</summary>
        public float BurnRate { get; private set; }

        /// <summary>Heat into the boiler, in W.</summary>
        public float Heat { get; private set; }

        /// <summary>Firebox gas temperature, in °C.</summary>
        public float Temperature { get; private set; }

        /// <summary>The brick lining's temperature, in °C.</summary>
        public float BrickTemperature { get; private set; }

        /// <summary>The tender's oil temperature, in °C.</summary>
        public float OilTemperature { get; private set; }

        /// <summary>Air over the stoichiometric need of the oil fed (0 with no oil).</summary>
        public float ExcessAir { get; private set; }

        /// <summary>The atomizer's steam over what the oil needs.</summary>
        public float Atomizing { get; private set; }

        /// <summary>Atomizing steam used, in kg/s.</summary>
        public float AtomizerSteam { get; private set; }

        /// <summary>Heater steam used, in kg/s.</summary>
        public float HeaterSteamUsed { get; private set; }

        /// <summary>Whether the atomizer and heater take their steam from a shed's steam line, at more than the boiler has.</summary>
        public bool OnSteamLine { get; private set; }

        /// <summary>How hard over-atomized oil drums in the firebox, 0 to 1.</summary>
        public float Drumming { get; private set; }

        /// <summary>Black smoke leaving the fire, 0 to 1, as the stack shows it.</summary>
        public float SootDensity { get; private set; }

        /// <summary>Smoke as the stock firebox reports it: six times the unburned share.</summary>
        public float SmokeDensity { get; private set; }

        /// <summary>
        /// The stack's darkness for the cab's own smoke, 0 white (the exhaust's
        /// steam alone) to 1 black: a light haze from a well-set fire, fading to
        /// nothing as the air runs past what it wastes, and blackening with the
        /// oil that does not burn ("a light gray or brown haze is right", ICS
        /// p. 54; RME22 p. 688).
        /// </summary>
        public float StackSmoke { get; private set; }

        /// <summary>Oil vapor in the firebox, in firebox kg; it shows as white smoke.</summary>
        public float Vapor { get; private set; }

        /// <summary>Oil pooled in the firepan, in firebox kg.</summary>
        public float PooledOil { get; private set; }

        /// <summary>The lit waste's time left, in s.</summary>
        public float Torch { get; private set; }

        /// <summary>Flue fouling, 0 clean to 1 fully sooted.</summary>
        public float FlueSoot { get; private set; }

        /// <summary>A sanding's black cloud at the stack, 0 to 1.</summary>
        public float SandBurst { get; private set; }

        /// <summary>The flareback on the last step, 0 none to 1 the worst; reset each step.</summary>
        public float Flareback { get; private set; }

        /// <summary>Burn rate over the stock most.</summary>
        public float CombustionRateNormalized => BurnRate / parameters.MaxCombustionRate;

        /// <summary>What the fire shows now.</summary>
        public OilFireState Snapshot() => new OilFireState
        {
            FireOn = FireOn,
            FeedRate = FeedRate,
            BurnRate = BurnRate,
            Heat = Heat,
            Temperature = Temperature,
            BrickTemperature = BrickTemperature,
            OilTemperature = OilTemperature,
            ExcessAir = ExcessAir,
            Atomizing = Atomizing,
            AtomizerSteam = AtomizerSteam,
            HeaterSteamUsed = HeaterSteamUsed,
            Drumming = Drumming,
            SootDensity = SootDensity,
            SmokeDensity = SmokeDensity,
            StackSmoke = StackSmoke,
            Vapor = Vapor,
            PooledOil = PooledOil,
            Torch = Torch,
            FlueSoot = FlueSoot,
            SandBurst = SandBurst,
        };

        /// <summary>Shows another fire's state, as a multiplayer client shows the host's.</summary>
        public void Mirror(OilFireState state)
        {
            FireOn = state.FireOn;
            FeedRate = state.FeedRate;
            BurnRate = state.BurnRate;
            Heat = state.Heat;
            Temperature = state.Temperature;
            BrickTemperature = state.BrickTemperature;
            OilTemperature = state.OilTemperature;
            ExcessAir = state.ExcessAir;
            Atomizing = state.Atomizing;
            AtomizerSteam = state.AtomizerSteam;
            HeaterSteamUsed = state.HeaterSteamUsed;
            Drumming = state.Drumming;
            SootDensity = state.SootDensity;
            SmokeDensity = state.SmokeDensity;
            StackSmoke = state.StackSmoke;
            Vapor = state.Vapor;
            PooledOil = state.PooledOil;
            Torch = state.Torch;
            FlueSoot = state.FlueSoot;
            SandBurst = state.SandBurst;
        }

        /// <summary>Throws in lit waste to light the oil.</summary>
        public void Ignite() => Torch = TorchTime;

        /// <summary>Puts the fire out, as the stock extinguish request does.</summary>
        public void Extinguish()
        {
            FireOn = false;
            Torch = 0f;
        }

        /// <summary>Restores a saved fire.</summary>
        public void Restore(bool fireOn, float brickTemperature, float oilTemperature, float flueSoot)
        {
            FireOn = fireOn;
            BrickTemperature = Clean(brickTemperature, ColdBrick);
            OilTemperature = Clean(oilTemperature, WarmOil);
            FlueSoot = Smoothing.Clamp01(flueSoot);
            Vapor = PooledOil = 0f;
            burning = 0f;
        }

        /// <summary>Readies the fire as the shed would: lit, the brick hot, the oil warm.</summary>
        public void PrepareFire()
        {
            FireOn = true;
            BrickTemperature = HotBrick;
            OilTemperature = WarmOil;
            Vapor = PooledOil = 0f;
        }

        /// <summary>
        /// Sand thrown into the firebox, in kg. Under a hard pull it scours the
        /// flues' soot out of the stack as a black cloud; with little draft it
        /// mostly falls in the pan.
        /// </summary>
        public void ThrowSand(float kg, float drawing)
        {
            if (!(kg > 0f))
                return;
            // Only a strong draft carries sand through the flues.
            var pull = Smoothing.Clamp01(drawing);
            var scoured = Math.Min(FlueSoot, kg / SandToScour * pull * pull);
            FlueSoot -= scoured;
            SandBurst = Smoothing.Clamp01(SandBurst + 0.3f * pull + 2f * scoured);
        }

        /// <summary>Steps the fire. Returns the oil drawn from the tender, in firebox kg.</summary>
        public float Step(float dt, OilFireInputs inputs)
        {
            Flareback = 0f;
            if (!(dt > 0f))
                return 0f;
            clock += dt;

            // The burner's steam valves take the boiler's steam, or a shed's
            // steam line's where it is stronger.
            OnSteamLine = inputs.SteamLinePressure > inputs.BoilerPressure;
            var valvePressure = Math.Max(inputs.BoilerPressure, inputs.SteamLinePressure);
            HeatOil(dt, inputs, valvePressure);

            // The oil runs to the burner by gravity through the valve, as freely
            // as its warmth lets it: cold, it does not run at all.
            var cold = Smoothing.Clamp01((WarmOil - OilTemperature) / (WarmOil - AmbientTemperature));
            var hot = Smoothing.Clamp01((OilTemperature - HotOil) / 30f);
            var flow = Smoothing.Clamp01((OilTemperature - OilStops) / (WarmOil - OilStops));
            // Overheated oil sputters as it flashes in the line.
            var sputter = hot > 0f ? 1f + 0.35f * hot * (float)Math.Sin(clock * 7.3) * (float)Math.Sin(clock * 2.9) : 1f;
            var asked = Smoothing.Clamp01(inputs.FiringValve) * MaxFeed;
            var target = asked * flow * sputter;
            target = Math.Min(target, Math.Max(0f, inputs.TenderFuel) / dt);
            FeedRate = Smoothing.SmoothApproach(FeedRate, Math.Max(0f, target), ref responseVelocity, ResponseTime, dt);
            if (FeedRate < 1e-5f)
                FeedRate = 0f;
            var oil = FeedRate;

            // The atomizer: its steam against what the oil needs, more against
            // a hard exhaust; cold, thick oil wants much more, however little of
            // it gets through.
            AtomizerSteam = Smoothing.Clamp01(inputs.Atomizer) * AtomizerCapacity * AtomizingSteamPerKg * MaxFeed
                            * SteamValveShare(valvePressure);
            var need = Math.Max(oil, cold > 0f ? Math.Min(asked, oil / Math.Max(0.01f, flow)) : oil) * AtomizingSteamPerKg
                       * (StandingAtomizing + (1f - StandingAtomizing) * Smoothing.Clamp01(inputs.Exhaust))
                       * (1f + ColdOilAtomizing * cold);
            Atomizing = need > 0f ? AtomizerSteam / need : AtomizerSteam > 0f ? DrummingFull : 0f;

            // The air: the draft's over what the oil needs to burn, and the door's.
            var air = Math.Max(0f, inputs.AirFlow) + 4f * Smoothing.Clamp01(inputs.DoorOpening) * Math.Max(0.2f, inputs.AirFlow);
            ExcessAir = oil > 0f ? air / (oil * FireModel.StoichiometricAirPerCoal) : 0f;

            var drowned = inputs.IntakeWater > 0.5f;
            if (drowned)
                FireOn = false;
            Light(oil);
            Torch = Math.Max(0f, Torch - dt);
            BlowOut(oil, drowned);

            var unburned = 0f;
            if (FireOn && oil > 0f)
            {
                var atomized = Smoothing.Clamp01(Atomizing);
                var poorlyAtomized = UnatomizedLoss * (1f - atomized) * (1f - atomized);
                var shortOfAir = Smoothing.Clamp01(1f - ExcessAir);
                unburned = Smoothing.Clamp01(1f - (1f - poorlyAtomized) * (1f - shortOfAir) * (1f - 0.15f * hot));
                // Oil too poorly atomized to burn drips into the pan.
                if (Atomizing < DripBelow)
                    PooledOil += oil * (DripBelow - Atomizing) / DripBelow * dt;
            }
            else if (oil > 0f)
            {
                // With no fire, oil on hot brick vaporizes; on cold it pools.
                if (BrickTemperature > BrickVaporizes)
                    Vapor += oil * dt;
                else
                    PooledOil += oil * dt;
            }

            // Pooled oil vaporizes off hot brick; the draft clears the vapor.
            if (PooledOil > 0f && BrickTemperature > BrickVaporizes && !FireOn)
            {
                var vaporized = PooledOil * Math.Min(1f, dt / PoolVaporizeTime);
                PooledOil -= vaporized;
                Vapor += vaporized;
            }
            if (Vapor > 0f)
                Vapor -= Vapor * Math.Min(1f, air * dt / BoxAir);

            var fed = FireOn ? oil : 0f;
            burning = fed * (1f - unburned);
            BurnRate = burning;

            // The heat: the stock heat for the oil burned, less what too much
            // air, over-atomizing, an open door, and sooted flues take away.
            var speedup = inputs.StartupMultiplier > 0f ? inputs.StartupMultiplier : 1f;
            var efficiency = Smoothing.Lerp(1f, parameters.EfficiencyAtMaxCombustion, burning / parameters.MaxCombustionRate);
            var excessLoss = Math.Min(MaxExcessAirLoss, ExcessAirLoss * Math.Max(0f, ExcessAir - WastefulExcessAir));
            var gross = burning * FireboxParameters.CoalHeatContent * efficiency * (1f - excessLoss);
            Drumming = FireOn ? Smoothing.Clamp01((Atomizing - DrummingStart) / (DrummingFull - DrummingStart)) : 0f;
            var steamLoss = FireOn ? Math.Max(0f, AtomizerSteam - need * DrummingStart) * SteamHeatCarried : 0f;
            var doorLoss = Smoothing.Clamp01(inputs.DoorOpening) * 0.3f * gross;
            Heat = Math.Max(0f, (gross - steamLoss - doorLoss) * (1f - SootedHeatLoss * FlueSoot)) * speedup;

            // Smoke: black from oil that does not burn, and the stock figure.
            var sootTarget = Smoothing.Clamp01(4f * unburned);
            SootDensity += (sootTarget - SootDensity) * Math.Min(1f, dt / 1f);
            SmokeDensity = 6f * unburned;
            var haze = FireOn && oil > 0f
                ? HazeSmoke * (1f - Smoothing.Clamp01((ExcessAir - WastefulExcessAir) / (ClearStackAir - WastefulExcessAir)))
                : 0f;
            stackSoot += (Smoothing.Clamp01(StackSootGain * unburned) - stackSoot) * Math.Min(1f, dt / StackSootTime);
            StackSmoke = haze + (1f - haze) * stackSoot;

            // The flues soot from smoke, and gum slowly even from a clean fire.
            var share = parameters.MaxCombustionRate > 0f ? Smoothing.Clamp01(fed / parameters.MaxCombustionRate) : 0f;
            FlueSoot = Smoothing.Clamp01(FlueSoot + (SootDensity / SootingTime + 1f / GummingTime) * share * dt);
            SandBurst *= (float)Math.Exp(-dt / SandBurstTime);

            HeatBrick(dt, inputs, air);
            var gasTarget = FireOn && burning > 0f
                ? Smoothing.Lerp(FireModel.MinFireTemperature, FireModel.MaxFireTemperature, Heat / Math.Max(1f, parameters.MaxHeatOutput))
                : Math.Max(inputs.BoilerTemperature, 0.7f * BrickTemperature);
            Temperature = Smoothing.SmoothDamp(Temperature, gasTarget, ref temperatureVelocity, parameters.TemperatureSmoothTime, dt);

            // The stock steam-raising speedup burns the fuel faster, as the coal fire's does.
            return oil * speedup * dt;
        }

        // The lit waste, or brick hot enough, lights atomized oil; whatever
        // vapor and pooled oil have gathered go up with it.
        private void Light(float oil)
        {
            if (FireOn)
                return;
            var lighter = Torch > 0f || BrickTemperature > BrickRelights;
            if (!lighter)
                return;
            var gathered = Vapor + PooledOil;
            var oilToLight = oil > 0f && Atomizing >= FlameAtomizing;
            if (gathered > FlarebackFuel && (oilToLight || Torch > 0f))
            {
                Flareback = Smoothing.Clamp01(gathered / WorstFlareback);
                Vapor = PooledOil = 0f;
            }
            if (oilToLight)
                FireOn = true;
        }

        // Too light a fire for the brick, too strong a pull, or no atomizing, and the fire goes out.
        private void BlowOut(float oil, bool drowned)
        {
            if (!FireOn || drowned)
                return;
            var hot = Smoothing.Clamp01((BrickTemperature - ColdBrick) / (HotBrick - ColdBrick));
            var smallest = Smoothing.Lerp(SmallestFireColdBrick, SmallestFireHotBrick, hot) * parameters.MaxCombustionRate;
            var blowOut = Smoothing.Lerp(BlowOutColdBrick, BlowOutHotBrick, hot);
            if (oil <= 0f || Atomizing < FlameAtomizing || (ExcessAir > blowOut && oil < 3f * smallest) || oil < 0.5f * smallest)
                FireOn = Torch > 0f && oil > 0f && Atomizing >= FlameAtomizing;
        }

        // The brick heats toward the fire's heat and cools toward the boiler,
        // faster with cold air drawn through the box.
        private void HeatBrick(float dt, OilFireInputs inputs, float air)
        {
            var share = parameters.MaxCombustionRate > 0f ? Smoothing.Clamp01(burning / parameters.MaxCombustionRate) : 0f;
            if (FireOn && burning > 0f)
            {
                var target = Smoothing.Lerp(BrickRelights + 50f, HotBrick + 150f, share);
                BrickTemperature += (target - BrickTemperature) * Math.Min(1f, dt / BrickHeatTime);
            }
            else
            {
                var torch = Torch > 0f ? 0.2f : 0f;
                var pull = 1f + 4f * Smoothing.Clamp01(air / Math.Max(0.1f, parameters.MaxCombustionRate * FireModel.AirPerCoal));
                var floor = Math.Max(AmbientTemperature, inputs.BoilerTemperature);
                BrickTemperature += (floor - BrickTemperature) * Math.Min(1f, pull * dt / BrickCoolTime) + torch * dt;
            }
        }

        /// <summary>
        /// A steam valve's flow at a boiler pressure (absolute bar) as a share
        /// of its flow at 14 bar: none without steam, more as the pressure rises
        /// (ICS p. 56).
        /// </summary>
        public static float SteamValveShare(float boilerPressure) =>
            (float)Math.Sqrt(Smoothing.Clamp01((boilerPressure - 1f) / 13f));

        private void HeatOil(float dt, OilFireInputs inputs, float valvePressure)
        {
            var steam = Smoothing.Clamp01(inputs.Heater) * SteamValveShare(valvePressure);
            HeaterSteamUsed = steam * HeaterSteam;
            OilTemperature += (steam * HeaterWarming - (OilTemperature - AmbientTemperature) / OilCoolTime) * dt;
        }

        private static float Clean(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
