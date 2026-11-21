using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// Api, the readings for other mods, as one binds it: found by its
    /// assembly-qualified name, Has and Read bound as delegates over base
    /// library types and TrainCar (as a reflection reader such as the Route
    /// Assistant Tablet's does), and no car answered with false and null.
    /// The names it hands out as strings are its enums' names. A lit coal
    /// fire's and a cold oil fire's snapshots, with this mod's boiler water,
    /// carry every key their sections list, of the types listed, every number
    /// finite, and nothing but base library types; the glass and pressure are
    /// the overlay's dials', and both read them, and the next step, through
    /// the same helpers.
    /// </summary>
    internal static class ApiChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var gameParams = new SimGameParams(false, false, false, 1f, 1f);
            var api = Type.GetType("RealisticFiring.Api, RealisticFiring", false);
            check(api != null && api.IsPublic && api.IsAbstract && api.IsSealed, "Api is a public static class found by name");
            if (api == null)
                return;
            var version = api.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            check(version != null && version.IsLiteral && (int)version.GetRawConstantValue() == 1, "Api.Version is 1");
            var has = Bind<Func<TrainCar, bool>>(api, "Has");
            var read = Bind<Func<TrainCar, IDictionary<string, object>>>(api, "Read");
            check(has != null && read != null, "Api.Has(TrainCar) and Api.Read(TrainCar) bind as Func<TrainCar, bool> and Func<TrainCar, IDictionary<string, object>>");
            check(has != null && read != null && !has(null) && read(null) == null, "Api answers no car with false and null");

            check(Names<AdviceLevel>("Ok", "Watch", "Act")
                  && Names<GrateZone>("BackLeft", "UnderDoor", "BackRight", "MidLeft", "Center", "MidRight", "FrontLeft", "Front", "FrontRight")
                  && Names<InjectorState>("Off", "Priming", "Working", "Hot", "LowPressure", "NoWater", "Broken")
                  && Names<FeedPumpState>("Off", "Feeding", "Choked", "Racing", "LostSuction", "Tripped")
                  && Names<BoosterState>("CutOut", "Idling", "CocksOpen", "Working")
                  && Names<FiremanMode>("Off", "KeepAlive", "Shunting", "Road")
                  && Names<FiremanCall>("None", "HeavyAhead", "EasingOff")
                  && Names<ChecklistId>("CoalColdStart", "OilColdStart", "InjectorsAndWater", "CleaningClinkerAndAsh", "StokerSetup", "DriftingAndLayover"),
                "Api's levels, zones, states, and checklists are the names its documentation lists");

            CheckCoal(check, gameParams);
            CheckOil(check, gameParams);
            check(Calls(typeof(Api), "Boiler", nameof(FireOverlay.GlassShown)) && Calls(typeof(Api), "Boiler", nameof(FireOverlay.GaugePressure))
                  && Calls(typeof(Api), "Boiler", nameof(FireOverlay.WorkingPressure)) && Calls(typeof(Api), "ReadCoal", nameof(FireOverlay.CoalAdvice))
                  && Calls(typeof(Api), "ReadOil", nameof(FireOverlay.OilAdvice))
                  && Calls(typeof(FireOverlay), "WaterGauges", nameof(FireOverlay.GlassShown)) && Calls(typeof(FireOverlay), "WaterGauges", nameof(FireOverlay.GaugePressure))
                  && Calls(typeof(FireOverlay), "WaterGauges", nameof(FireOverlay.WorkingPressure)),
                "Api and the overlay read the glass, the boiler's pressures, and the next step through the same helpers");
        }

        private static void CheckCoal(Action<bool, string> check, SimGameParams gameParams)
        {
            var fire = new RealisticFirebox(new FireboxDefinition { ID = "firebox" }, null);
            fire.SetGameParams(gameParams);
            fire.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 10f });
            var water = Water(out var vessel);
            vessel.pressure = 12f;
            vessel.waterTemp = 180f;
            var readings = Api.ReadParts(fire, null, null, water, null);

            check(Keys(readings, "fuel", "fire", "grate", "coal", "boiler", "injectors", "advice", "checklist")
                  && !readings.ContainsKey("burner") && !readings.ContainsKey("overflow") && !readings.ContainsKey("engine")
                  && Equals(readings["fuel"], "Coal") && readings["injectors"] is List<object> injectors && injectors.Count == 0,
                "Api's coal snapshot has its sections, an empty injector list without this mod's injectors, and no oil or engine sections");
            check(Section(readings, "fire", out var lit) && Keys(lit, "lit", "heatPct", "level", "draftPct", "draftLevel", "smokePct", "temperatureC")
                  && Equals(lit["lit"], true) && Level(lit["level"]) && Level(lit["draftLevel"]),
                "Api's coal fire: lit, heat, draft, smoke, temperature, and the dials' levels");
            check(Section(readings, "grate", out var grate)
                  && Keys(grate, "coalKg", "ashpanPct", "ashpanFull", "clinkerKg", "needsCleaning", "thinZone", "zoneReferenceKg", "zones")
                  && !grate.ContainsKey("doorPct") && Math.Abs((float)grate["coalKg"] - fire.Model.CoalMass) < 1e-4f
                  && grate["zones"] is List<object> zones && zones.Count == GrateLayout.ZoneCount
                  && zones.Select((zone, i) => zone is Dictionary<string, object> z && Keys(z, "name", "coalKg", "burningPct", "clinkerKg")
                                               && Equals(z["name"], ((GrateZone)i).ToString())).All(ok => ok)
                  && Math.Abs(zones.Sum(z => (float)((Dictionary<string, object>)z)["coalKg"]) - fire.Model.CoalMass) < 1e-3f,
                "Api's grate: the coal in the box as its zones hold it, in the grate's order, and no door reading without a door");
            check(Section(readings, "coal", out var coal) && Keys(coal, "moisturePct", "condition")
                  && new[] { "Dry", "Damp", "Wet" }.Contains(coal["condition"]),
                "Api's coal: its water and condition");
            check(Section(readings, "advice", out var advice) && Keys(advice, "level", "text") && Level(advice["level"])
                  && Equals(advice["text"], FireOverlay.CoalAdvice(fire, null, water).Text ?? ""),
                "Api's next step is the overlay's");
            check(Section(readings, "checklist", out var coalChecklist)
                  && Keys(coalChecklist, "id", "name", "shortName", "autoSelected", "allDone", "completedPct", "steps")
                  && coalChecklist["steps"] is List<object> coalSteps && coalSteps.Count > 0
                  && coalSteps.All(s => s is Dictionary<string, object> step && Keys(step, "title", "how", "done", "current")),
                "Api's coal checklist carries its id, titles, completion percentage, and live steps");

            // The overlay's glass and boiler dials from the same parts.
            var card = new FireOverlay.Card();
            AccessTools.Method(typeof(FireOverlay), "WaterGauges").Invoke(null, new object[] { card, null, water });
            check(Section(readings, "boiler", out var boiler)
                  && Keys(boiler, "pressureBar", "workingBar", "pressurePct", "pressureLevel", "glassPct", "glassLevel", "waterC", "heating", "sootLossPct", "scaleLossPct",
                      "thermalShockWearPct", "thermalShockLossPct", "warmingTooFast", "tubeLeakKgPerS")
                  && Math.Abs((float)boiler["glassPct"] - card.Gauges[2].Value * 100f) < 1e-3f && Equals(boiler["glassLevel"], card.Gauges[2].Level.ToString())
                  && Math.Abs((float)boiler["pressureBar"] - 11f) < 1e-4f && card.Gauges[3].Text == "11.0 bar"
                  && Math.Abs((float)boiler["pressurePct"] / 110f - card.Gauges[3].Value) < 1e-4f && Equals(boiler["pressureLevel"], card.Gauges[3].Level.ToString()),
                $"Api's boiler reads the overlay's dials: {Describe(readings, "boiler")}");
            check(BaseTypesOnly(readings), "Api's coal snapshot holds only floats, bools, strings, and dictionaries and lists of them, every number finite");
        }

        private static void CheckOil(Action<bool, string> check, SimGameParams gameParams)
        {
            var oil = new RealisticOilFirebox(new FireboxDefinition { ID = "firebox", maxCoalCapacity = 200f, burnTime = 100f, efficiencyAtMaxCombustion = 0.6f, startupMaxPressure = 22f }, null);
            oil.SetGameParams(gameParams);
            var water = Water(out _);
            var readings = Api.ReadParts(null, oil, null, water, null);
            check(Keys(readings, "fuel", "fire", "burner", "boiler", "injectors", "advice", "checklist") && Equals(readings["fuel"], "Oil")
                  && !readings.ContainsKey("grate") && !readings.ContainsKey("coal") && !readings.ContainsKey("stoker"),
                "Api's oil snapshot has its sections and no coal sections");
            check(Section(readings, "fire", out var fire) && Keys(fire, "lit", "heatPct", "level", "draftPct", "draftLevel", "smokePct")
                  && Equals(fire["lit"], false) && Equals(fire["draftPct"], 0f),
                "Api's cold oil fire: out, with no draft");
            check(Section(readings, "burner", out var burner)
                  && Keys(burner, "firingValvePct", "feedKgPerS", "atomizerPct", "atomizingPct", "oilC", "heaterPct", "brickC", "onShedSteam", "steamLinePct", "flueSootPct", "lighting")
                  && burner["lighting"] is List<object> steps && steps.Count > 0
                  && steps.All(s => s is Dictionary<string, object> step && Keys(step, "title", "how", "done", "current"))
                  && steps.Count(s => Equals(((Dictionary<string, object>)s)["current"], true)) == 1,
                $"Api's burner: its valves and temperatures, and while it is out the steps to light it, one of them current ({(burner != null && burner["lighting"] is List<object> l ? l.Count : 0)} steps)");
            check(Section(readings, "advice", out var advice) && Equals(advice["text"], FireOverlay.OilAdvice(FireController.ReadOil(oil), water).Text ?? ""),
                "Api's oil next step is the overlay's");
            check(Section(readings, "checklist", out var oilChecklist)
                  && Keys(oilChecklist, "id", "name", "shortName", "autoSelected", "allDone", "completedPct", "steps")
                  && Equals(oilChecklist["id"], "OilColdStart")
                  && oilChecklist["steps"] is List<object> oilSteps
                  && burner != null && burner["lighting"] is List<object> lightingSteps
                  && oilSteps.Count == lightingSteps.Count + 1,
                "Api's cold oil checklist auto-selects OilColdStart and unifies with burner lighting steps");
            check(BaseTypesOnly(readings), "Api's oil snapshot holds only base library types, every number finite");
        }

        private static BoilerWaterRig Water(out WaterPressureVessel vessel)
        {
            var boiler = (Boiler)new BoilerDefinition
            {
                ID = "boiler", diameter = 1.7f, length = 8.8f,
                spawnPressure = 12f, spawnWaterLevel = 12000f,
                safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                crownSheetNormalizedWaterLevel = 0.6f, steamOutletNormalizedWaterLevel = 0.98f,
            }.InstantiateImplementation();
            boiler.waterLevelReadOut.Value = 0.765f;
            vessel = (WaterPressureVessel)AccessTools.Field(typeof(Boiler), "vessel").GetValue(boiler);
            return BoilerWaterRig.Create(boiler, null, BoilerWaterRig.Guard.None);
        }

        private static T Bind<T>(Type type, string name) where T : Delegate
        {
            var parameters = typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray();
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
            return method == null ? null : (T)Delegate.CreateDelegate(typeof(T), method, false);
        }

        private static bool Names<T>(params string[] names) where T : Enum => Enum.GetNames(typeof(T)).SequenceEqual(names);

        private static bool Keys(IDictionary<string, object> section, params string[] keys) => section != null && keys.All(section.ContainsKey);

        private static bool Section(IDictionary<string, object> readings, string name, out Dictionary<string, object> section)
        {
            section = readings != null && readings.TryGetValue(name, out var value) ? value as Dictionary<string, object> : null;
            return section != null;
        }

        private static bool Level(object level) => level is string text && Enum.GetNames(typeof(AdviceLevel)).Contains(text);

        private static bool BaseTypesOnly(object value)
        {
            switch (value)
            {
                case float number: return !float.IsNaN(number) && !float.IsInfinity(number);
                case bool _:
                case string _:
                    return true;
                case Dictionary<string, object> section: return section.Values.All(BaseTypesOnly);
                case List<object> list: return list.All(BaseTypesOnly);
                default: return false;
            }
        }

        private static string Describe(IDictionary<string, object> readings, string name) =>
            Section(readings, name, out var section) ? string.Join(", ", section.Select(pair => $"{pair.Key} {pair.Value}")) : "missing";

        // Whether a method of the type calls the overlay's helper (its IL), so neither reads the dials its own way.
        private static bool Calls(Type type, string method, string helper)
        {
            var target = AccessTools.Method(typeof(FireOverlay), helper);
            return type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name == method)
                .Any(m => PatchProcessor.GetOriginalInstructions(m).Any(i => Equals(i.operand, target)));
        }
    }
}
