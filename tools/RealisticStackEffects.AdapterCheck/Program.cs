using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using DV.Simulation.Controllers;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticStackEffects.Api;

namespace RealisticStackEffects.AdapterCheck
{
    /// <summary>
    /// Checks Realistic Stack Effects' reading of the game's own firebox and
    /// exhaust (<see cref="StockStack"/>) against the installed game's sim,
    /// and that its patches find their targets.
    /// </summary>
    internal static class Program
    {
        private const string Id = "firebox";

        private static int failures;

        private static int Main()
        {
            var gameParams = new SimGameParams(false, false, false, 1f, 1f);
            CheckPatchTargets();
            CheckFirebox(gameParams);
            CheckExhaust(gameParams);
            CheckContract();
            CheckSettings();
            Console.WriteLine(failures == 0 ? "All stack effects adapter checks passed" : $"{failures} stack effects adapter checks failed");
            return failures == 0 ? 0 : 1;
        }

        // The stock smoke reader's methods the mod patches, each found once.
        private static void CheckPatchTargets()
        {
            foreach (var name in new[] { nameof(SteamSmokeParticlePortReader.Init), "UpdateParticles" })
            {
                var found = typeof(SteamSmokeParticlePortReader)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Count(m => m.Name == name);
                Check(found == 1, $"the stock smoke reader's {name} is there to patch ({found} found)");
            }
            Check(typeof(SteamSmokeParticlePortReader).GetField(nameof(SteamSmokeParticlePortReader.fireOnPortId)) != null,
                "the stock smoke reader names its firebox's FIRE_ON port");
            // The coal transfer every shovelful goes through, and what it reads.
            var transfer = typeof(DV.Simulation.Cars.FireboxSimController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == nameof(DV.Simulation.Cars.FireboxSimController.TransferCoal)).ToList();
            Check(transfer.Count == 1 && transfer[0].GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(float) })
                  && transfer[0].GetParameters()[0].Name == "coalMass"
                  && typeof(DV.Simulation.Cars.FireboxSimController).GetField("coalConsumptionMultiplier") != null,
                "the game's coal transfer, TransferCoal(float coalMass), is there for the shovelful's smoke-out");
            var after = typeof(StockStack).Assembly.GetType("RealisticStackEffects.SmokeReaderInitPatch")
                ?.GetCustomAttributes(typeof(HarmonyLib.HarmonyAfter), false).Cast<HarmonyLib.HarmonyAfter>()
                .SelectMany(a => a.info.after ?? new string[0]).ToList() ?? new System.Collections.Generic.List<string>();
            Check(after.Contains("RealisticFiring"),
                "the plume is set up after Realistic Firing's own patch, so an older Realistic Firing keeps its engines' smoke");
        }

        // The stock firebox read through its ports: its heat and the most it
        // makes, its smoke when the draft falls short, and a clear stack
        // when air passes beyond the coal's need.
        private static void CheckFirebox(SimGameParams gameParams)
        {
            var def = new FireboxDefinition { ID = Id, maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f, startupMaxPressure = 13f };
            var firebox = new Firebox(def);
            firebox.SetGameParams(gameParams);
            var air = Wire(firebox, def.airFlow, PortValueType.MASS_RATE, 0.5f);
            Wire(firebox, def.boilerPressure, PortValueType.PRESSURE, 14f);
            Wire(firebox, def.boilerTemperature, PortValueType.TEMPERATURE, 190f);
            Wire(firebox, def.coalDumpControl, PortValueType.CONTROL, 0.5f);
            var stack = new StockStack(null, P(firebox, def.fireOnReadOut), def, port => P(firebox, port), firebox.airFlow);

            P(firebox, def.coalControlExtIn).Value += 60f;
            P(firebox, def.ignitionExtIn).ExternalValueUpdate(1f);
            Run(firebox, 60f);
            var starved = Read(stack, null);
            var oxygen = P(firebox, def.oxygenAvailabilityReadOut).Value;
            var most = (float)typeof(Firebox).GetField("maxHeatOutput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(firebox);
            Check(starved.FireOn && Math.Abs(starved.MostHeat - most) <= 1e-3f * most && starved.Heat == P(firebox, def.heatReadOut).Value,
                $"the stock firebox's heat {starved.Heat / 1e6f:F2} MW of the most it makes, {starved.MostHeat / 1e6f:F2} MW (the game's {most / 1e6f:F2})");
            var density = P(firebox, def.smokeDensityReadOut).Value;
            Check(oxygen < 0.9f && Math.Abs(starved.Soot - StockStack.SootShare * density) < 1e-4f && starved.Soot > 0.3f && starved.ClearStack == 0f,
                $"a fire short of air smokes a heavy grey: oxygen {oxygen:F2}, smoke density {density:F2}, soot {starved.Soot:F2}, clear stack {starved.ClearStack:F2}");

            air.Value = 20f;
            Run(firebox, 60f);
            var drawn = Read(stack, null);
            oxygen = P(firebox, def.oxygenAvailabilityReadOut).Value;
            Check(oxygen == 1f && drawn.Soot < 0.05f && drawn.ClearStack > 0.5f,
                $"a fire with air to spare runs clean and its stack clears: oxygen {oxygen:F2}, soot {drawn.Soot:F2}, clear stack {drawn.ClearStack:F2}");

            // A stock shovelful, delivered as the shovel sends it and told to
            // the stack as the coal transfer's patch tells it: its smoke-out
            // reaches the stack a moment later and clears in seconds.
            Read(stack, null);
            P(firebox, def.coalControlExtIn).Value += 36f;
            stack.Thrown(36f);
            float early = 0f, late = 0f;
            for (var t = 0f; t < 3f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                var smokeOut = Read(stack, null).FireboxSmokeOut;
                if (t < 0.1f)
                    early = smokeOut;
                late = smokeOut;
            }
            for (var t = 0f; t < 40f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                Read(stack, null);
            }
            var cleared = Read(stack, null).FireboxSmokeOut;
            Check(early < 0.1f * late && late > 20f && cleared < 1f,
                $"a stock shovelful smokes out at the stack a moment after it lands: {early:F1} firebox puffs a second at 0.1 s, {late:F0} at 3 s, {cleared:F1} after 40 s");

            // Two smoke readers on one car both take its shovelfuls.
            var shared = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var first = new StockStack(shared, P(firebox, def.fireOnReadOut), def, port => P(firebox, port), firebox.airFlow);
            var second = new StockStack(shared, P(firebox, def.fireOnReadOut), def, port => P(firebox, port), firebox.airFlow);
            foreach (var each in StockStack.On(shared))
                each.Thrown(3f);
            for (var t = 0f; t < 2f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                Read(first, null);
                Read(second, null);
            }
            Check(StockStack.On(shared).Count == 2 && Read(first, null).FireboxSmokeOut > 5f && Read(second, null).FireboxSmokeOut > 5f,
                "two smoke readers on one engine both smoke out its shovelful");

            // The stock fire's heat dies away over its combustion's smoothing time.
            P(firebox, def.extinguishExtIn).ExternalValueUpdate(1f);
            Run(firebox, 60f);
            var dead = Read(stack, null);
            Check(!dead.FireOn && dead.Heat < 0.05f * starved.Heat, $"a fire put out reads as out, its heat dying away ({dead.Heat / 1e6f:F3} MW)");

            // The start-up helper's way: a cold box filled, then lit before
            // the plume reads it again; the cold coal smokes nothing.
            var cold = new StockStack(null, P(firebox, def.fireOnReadOut), def, port => P(firebox, port), firebox.airFlow);
            Read(cold, null);
            P(firebox, def.coalControlExtIn).Value += 100f;
            cold.Thrown(100f);
            P(firebox, def.ignitionExtIn).ExternalValueUpdate(1f);
            firebox.Tick(0.02f);
            var lit = Read(cold, null);
            for (var t = 0f; t < 3f; t += 0.02f)
            {
                firebox.Tick(0.02f);
                lit = Read(cold, null);
            }
            Check(lit.FireOn && lit.FireboxSmokeOut == 0f, "coal thrown on a dead fire smokes nothing once it is lit");
            P(firebox, def.extinguishExtIn).ExternalValueUpdate(1f);
            Run(firebox, 1f);

            var bare = Read(new StockStack(null, P(firebox, def.fireOnReadOut), null, null, null), null);
            Check(bare.MostHeat == 0f && bare.Soot == 0f && !bare.FireOn, "without a firebox's definition only whether the fire is on is read");
        }

        // The stock exhaust's steam up the stack: the blower's as the game
        // works it out, and the natural draft taken off a cold engine's flow.
        private static void CheckExhaust(SimGameParams gameParams)
        {
            var def = new SteamExhaustDefinition { ID = "exhaust", passiveExhaust = 0.2f, entrainmentRatio = 1.95f, maxBlowerFlow = 0.2f, pressureForMaxBlowerFlow = 3f };
            var exhaust = (SteamExhaust)def.InstantiateImplementation();
            exhaust.SetGameParams(gameParams);
            var steam = Wire(exhaust, def.exhaustFlow, PortValueType.MASS_RATE, 0f);
            Wire(exhaust, def.engineMaxFlowReader, PortValueType.MASS_RATE, 2f);
            Wire(exhaust, def.boilerPressure, PortValueType.PRESSURE, 13.4f);
            var blower = Wire(exhaust, def.blowerControl, PortValueType.CONTROL, 0f);
            Wire(exhaust, def.whistleControl, PortValueType.CONTROL, 0f);
            Wire(exhaust, def.damperControl, PortValueType.CONTROL, 1f);
            exhaust.InitializationAfterConnecting();
            var stack = new StockStack(null, null, null, null, null);
            var total = P(exhaust, def.totalFlowNormalizedReadOut);

            exhaust.Tick(0.02f);
            var cold = Read(stack, exhaust);
            Check(total.Value > 0f && Math.Abs(total.Value - cold.NaturalFlow) < 1e-5f && cold.StackSteam == 0f,
                $"a cold engine's natural draft is all of the stock flow ({total.Value:F3}, natural {cold.NaturalFlow:F3}): no stream");

            blower.Value = 0.5f;
            steam.Value = 1.2f;
            exhaust.Tick(0.02f);
            var working = Read(stack, exhaust);
            var used = P(exhaust, def.steamConsumptionReadOut).Value;
            var drawn = P(exhaust, def.airFlowReadOut).Value;
            // The steam meets only the air its blast draws, not the passive draft.
            var blast = def.entrainmentRatio * working.StackSteam;
            Check(Math.Abs(working.BlowerSteam - used) < 1e-5f && Math.Abs(working.StackSteam - (1.2f + used)) < 1e-5f
                  && Math.Abs(working.AirThrough - blast) < 1e-4f && Math.Abs(drawn - def.passiveExhaust - blast) < 1e-4f,
                $"the blower's steam {working.BlowerSteam:F3} kg/s as the game's ({used:F3}), {working.StackSteam:F2} kg/s up the stack, " +
                $"{working.AirThrough:F2} kg/s of the {drawn:F2} kg/s of air with it (the blast's)");
        }

        // Another mod's fire through the public contract: every field of the
        // plume's reading has its own in the contract's, and crosses intact;
        // a fire that throws hands its stack to the game's firebox.
        private static void CheckContract()
        {
            var inner = typeof(StackReading).GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(f => f.Name, f => f.FieldType);
            var outer = typeof(StackFireReading).GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(f => f.Name, f => f.FieldType);
            var unmatched = inner.Where(f => !outer.TryGetValue(f.Key, out var type) || type != f.Value).Select(f => f.Key)
                .Concat(outer.Keys.Where(name => !inner.ContainsKey(name))).ToList();
            Check(unmatched.Count == 0 && StackSources.ApiVersion == 2 && StackSources.EngineSteam(null) == null
                  && typeof(IEngineSteam).GetMethod("Add") != null,
                $"the contract's reading matches the plume's, {inner.Count} fields{(unmatched.Count > 0 ? $" (unmatched: {string.Join(", ", unmatched)})" : "")}, version {StackSources.ApiVersion}");

            StackSources.Register("Check", car => new Marked());
            var fire = StackSources.For(null, () => new StockStack(null, null, null, null, null), out var by);
            var reading = default(StackReading);
            fire.Read(0.02f, null, ref reading);
            var wrong = typeof(StackReading).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(f => !Equals(f.GetValue(reading), Marked.Value(f.Name, f.FieldType))).Select(f => f.Name).ToList();
            Check(by == "Check" && wrong.Count == 0 && fire.Tended,
                $"another mod's fire crosses the contract intact{(wrong.Count > 0 ? $" (lost: {string.Join(", ", wrong)})" : "")}");

            Marked.Throw = true;
            reading = default(StackReading);
            fire.Read(0.02f, null, ref reading);
            Check(!reading.FireOn && reading.Soot == 0f && float.IsNaN(reading.ExhaustWetness) && reading.Cinders,
                "a fire that throws hands its stack to the game's firebox");
        }

        // A fire that sets each field of the contract's reading to its own value.
        private sealed class Marked : IStackFireSource
        {
            public static bool Throw;

            public bool Tended => true;

            public void Read(float dt, SteamExhaust exhaust, ref StackFireReading reading)
            {
                if (Throw)
                    throw new InvalidOperationException("a broken fire");
                object boxed = reading;
                foreach (var field in typeof(StackFireReading).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    field.SetValue(boxed, Value(field.Name, field.FieldType));
                reading = (StackFireReading)boxed;
            }

            public static object Value(string name, Type type) =>
                type == typeof(bool) ? (object)true : 1f + Math.Abs(name.GetHashCode() % 1000) / 7f;
        }

        // The settings as Unity Mod Manager's window draws them (and a settings
        // list such as the Comms Tablet's reads them): one heading, the most
        // used first, short labels with tooltips, the developer settings
        // undrawn; and a Settings.xml saved in the old order loading whole.
        private static void CheckSettings()
        {
            var drawn = typeof(Settings).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => (Field: f, Draw: f.GetCustomAttributes(typeof(UnityModManagerNet.DrawAttribute), false).OfType<UnityModManagerNet.DrawAttribute>().LastOrDefault(),
                    Heading: f.GetCustomAttributes(typeof(UnityEngine.HeaderAttribute), false).OfType<UnityEngine.HeaderAttribute>().FirstOrDefault()?.header))
                .Where(d => d.Draw != null).ToList();
            var order = new[] { "SmokeQuality", "SmokeSeason", "EngineSteam", "HeatShimmer", "CustomSmokeEffects" };
            Check(drawn.Select(d => d.Field.Name).SequenceEqual(order) && drawn[0].Heading == "Chimney smoke" && drawn.Skip(1).All(d => d.Heading == null),
                "the settings show under one heading, Chimney smoke, the most used first, and no developer setting: " + string.Join(", ", drawn.Select(d => d.Field.Name)));
            // Unity Mod Manager's tooltip box (rich text) is one line per line of text: broken at about 80 characters.
            Check(drawn.All(d => !string.IsNullOrEmpty(d.Draw.Label) && d.Draw.Label.Length <= 60 && !string.IsNullOrEmpty(d.Draw.Tooltip) && !d.Draw.Tooltip.Contains("<")
                                 && d.Draw.Tooltip.Split('\n').All(line => line.Length <= 80)),
                $"every setting has a label of at most 60 characters (longest {drawn.Max(d => d.Draw.Label?.Length ?? 0)}) and a plain-text tooltip in lines of at most 80");

            const string old = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Settings>"
                + "<CustomSmokeEffects>false</CustomSmokeEffects><HeatShimmer>false</HeatShimmer><EngineSteam>false</EngineSteam>"
                + "<SmokeQuality>Auto</SmokeQuality><SmokeSeason>Winter</SmokeSeason><LogSetup>false</LogSetup>"
                + "<StatusLogInterval>5</StatusLogInterval><PlumeTestBed>a=1</PlumeTestBed></Settings>";
            var serializer = new XmlSerializer(typeof(Settings));
            bool Kept(Settings s) => !s.CustomSmokeEffects && !s.HeatShimmer && !s.EngineSteam && s.SmokeQuality == Core.SmokeQuality.Auto
                && s.SmokeSeason == Core.SmokeSeason.Winter && s.StatusLogInterval == 5 && s.PlumeTestBed == "a=1";
            Settings loaded;
            using (var reader = new StringReader(old))
                loaded = (Settings)serializer.Deserialize(reader);
            string saved;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, loaded);
                saved = writer.ToString();
            }
            using (var reader = new StringReader(saved))
                Check(Kept(loaded) && Kept((Settings)serializer.Deserialize(reader)),
                    "a Settings.xml in the old order loads every value, and keeps them saved again");
            Check(!saved.Contains("LogSetup") && typeof(Settings).GetField("LogSetup") == null,
                "a Settings.xml with LogSetup loads without it, and it is not saved again");
        }

        private static StackReading Read(StockStack stack, SteamExhaust exhaust)
        {
            var reading = default(StackReading);
            stack.Read(0.02f, exhaust, ref reading);
            return reading;
        }

        private static void Run(SimComponent component, float seconds)
        {
            for (var t = 0f; t < seconds; t += 0.02f)
                component.Tick(0.02f);
        }

        private static Port Wire(SimComponent component, PortReferenceDefinition reference, PortValueType type, float value)
        {
            var port = new Port(reference.ID.ToLowerInvariant(), new PortDefinition(PortType.READONLY_OUT, type, "OUT"), value);
            component.GetAllPortReferences().Single(r => r.id == $"{component.id}.{reference.ID}").SetPortReference(port);
            return port;
        }

        private static Port P(SimComponent component, PortDefinition port) =>
            component.GetAllPorts().Single(p => p.id == $"{component.id}.{port.ID}");

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
    }
}
