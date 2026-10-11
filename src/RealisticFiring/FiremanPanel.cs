using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring
{
 internal sealed class FiremanPanel
    {
        private const int WindowId = 0x5246_4D4E; // "RFMN"

        private static readonly (FiremanMode Mode, string Name)[] Modes =
        {
            (FiremanMode.Off, "Off"),
            (FiremanMode.KeepAlive, "Keep it alive"),
            (FiremanMode.Shunting, "Shunting"),
            (FiremanMode.Road, "Road"),
        };

        private static readonly Color Chosen = new Color(1f, 0.6f, 0.25f);
        private static readonly Color Warning = new Color(1f, 0.85f, 0.5f);

        private Rect window = new Rect(Screen.width - 440f, 80f, 420f, 10f);
        private FiremanRig shown;
        private GUIStyle status;

        // On a multiplayer client, the orders last sent to the host's fireman.
        private readonly Dictionary<TrainCar, FiremanMode> sentModes = new Dictionary<TrainCar, FiremanMode>();

        public bool Open { get; private set; }

        public void Toggle() => SetOpen(!Open);

        public void SetOpen(bool open) => Open = open;

        public void Draw(FiremanRig rig)
        {
            shown = rig;
            if (Open)
                window = GUILayout.Window(WindowId, window, DrawWindow, "Fireman");
            else if (Main.Settings.ShowFiremanStatus && ModeOf(rig) != FiremanMode.Off)
                DrawStatus(rig);
        }

        private FiremanMode ModeOf(FiremanRig rig) =>
            Net.IsClient ? sentModes.TryGetValue(rig.Car, out var mode) ? mode : FiremanMode.Off : rig.Fireman.Mode;

        private void DrawWindow(int id)
        {
            var rig = shown;
            var mode = ModeOf(rig);
            GUILayout.Label($"{rig.Car.carLivery.id} {rig.Car.ID}");

            GUILayout.BeginHorizontal();
            foreach (var (each, name) in Modes)
            {
                GUI.backgroundColor = each == mode ? Chosen : Color.white;
                if (GUILayout.Button(name))
                    Order(rig, each);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Label("The engineer's calls:");
            GUILayout.BeginHorizontal();
            Call(rig, FiremanCall.HeavyAhead, "Heavy work ahead");
            Call(rig, FiremanCall.EasingOff, "Easing off");
            GUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;

            if (FiremanRig.Rivals != null)
                GUILayout.Label($"{FiremanRig.Rivals} is running too: two firemen will fight; use one.");
            if (Net.IsClient)
                GUILayout.Label("In multiplayer the host's fireman does the work.");
            else
                foreach (var line in Describe(rig))
                    GUILayout.Label(line);

            if (GUILayout.Button("Close"))
                SetOpen(false);
            GUI.DragWindow();
        }

        private void Call(FiremanRig rig, FiremanCall call, string name)
        {
            var active = !Net.IsClient && rig.Fireman.Call == call;
            GUI.backgroundColor = active ? Chosen : Color.white;
            var label = active ? $"{name} ({rig.Fireman.CallLeft:F0} s)" : name;
            if (!GUILayout.Button(label))
                return;
            if (!Net.TrySend(rig.Car, FireAction.FiremanCall, value: (float)call))
                rig.Fireman.Call = active ? FiremanCall.None : call;
        }

        private void Order(FiremanRig rig, FiremanMode mode)
        {
            if (Net.TrySend(rig.Car, FireAction.FiremanMode, value: (float)mode))
                sentModes[rig.Car] = mode;
            else
                rig.SetMode(mode);
        }

        /// <summary>What he is doing, a line at a time.</summary>
        internal static IEnumerable<string> Describe(FiremanRig rig)
        {
            var man = rig.Fireman;
            if (man.Mode == FiremanMode.Off)
            {
                yield return "Off: he leaves the fire and the controls to you.";
                yield break;
            }
            var r = rig.Readings;
            yield return $"Pressure {r.Pressure:F1} bar, holding {man.PressureTarget:F1} (safety valve {r.SafetyValve:F1})";
            if (rig.CoalFireman is AutoFireman coal)
            {
                var fire = rig.Firebox.Model;
                yield return $"Fire {fire.CoalMass:F0} kg, wants {coal.CoalTarget:F0} kg for {man.PlannedSteam:F1} kg/s of steam" +
                             (coal.RoundLeft > 0 ? ", firing" : coal.WaitingOnSmoke ? ", waiting for the smoke to clear" : "");
                if (r.HasStoker)
                    yield return r.StokerJammed ? "Stoker jammed: he is reversing it to clear the screw"
                        : coal.Stoking ? $"Stoker running at {rig.Actions.StokerThrottle:P0}, jets: front {rig.Actions.FrontJets:P0}, back {rig.Actions.BackJets:P0}"
                        : "Stoker shut, the master jet cracked to keep the plate cool";
            }
            else if (rig.OilFireman is AutoOilFireman oil)
            {
                var fire = rig.OilFirebox.Model;
                yield return $"Firing valve {oil.Valve:P0} for {man.PlannedSteam:F1} kg/s of steam, keeping at least {oil.LeastFire:P0} on the brick at {fire.BrickTemperature:F0} C; " +
                             (oil.StackBlack ? "black at the stack: less oil, more air" : oil.StackClear ? "clear at the stack, less air" : "a light haze at the stack");
                yield return $"Atomizer {rig.Actions.Atomizer:P0} ({fire.Atomizing:F1}x the oil's need); oil {fire.OilTemperature:F0} C, heater {rig.Actions.Heater:P0}; flue soot {fire.FlueSoot:P0}";
            }
            yield return $"Glass {r.Glass:P0}, holding {man.GlassTarget:P0}; " +
                         (man.OnPump ? $"feed pump {r.Pump.ToString().ToLowerInvariant()}" : $"injector {r.Injector.ToString().ToLowerInvariant()}") +
                         (man.BlowingDown ? $"; blowing down ({r.Solids:F0} ppm)" : "");
            yield return DescribeRoad(rig.Road);
            var held = rig.Hands.Where(h => h.YieldLeft > 0f).Select(h => $"{h.Name} ({h.YieldLeft:F0} s)").ToList();
            if (held.Count > 0)
                yield return "Yours for now: " + string.Join(", ", held);
            foreach (var notice in Notices(rig.Actions.Notices))
                yield return notice;
        }

        /// <summary>The road as he reads it: the grade he is on, the hardest ahead for the fire and the water, and the train.</summary>
        internal static string DescribeRoad(FiremanRig.RoadView road)
        {
            if (!road.Known)
                return "Road: not read (standing, or off the track's end)";
            var ahead = float.IsNaN(road.GradeAhead) ? "" : $", {Grade(road.GradeAhead)} by {road.WaterLead / 1000f:F1} km";
            return $"Road: {Grade(road.GradeNow)} now, hardest {Grade(road.GradeSoon)} within {road.FireLead / 1000f:F1} km{ahead}; " +
                   $"train {road.Mass / 1000f:F0} t; work soon {road.WorkSoon:P0}, ahead {road.WorkAhead:P0}";
        }

        private static string Grade(float grade) => $"{grade * 100f:+0.0;-0.0;0.0} %";

        internal static IEnumerable<string> Notices(FiremanNotice notices)
        {
            if (notices.HasFlag(FiremanNotice.FireOut))
                yield return "The fire is out: light it (your lighter, lit, at the open fire door), and he will tend it.";
            if (notices.HasFlag(FiremanNotice.TenderWaterOut))
                yield return "The tender is dry: he has stopped firing.";
            if (notices.HasFlag(FiremanNotice.LowWater))
                yield return "The water is out of sight in the glass!";
            if (notices.HasFlag(FiremanNotice.TenderCoalOut))
                yield return "The tender is out of fuel.";
            if (notices.HasFlag(FiremanNotice.CleanFire))
                yield return "Clinker: the fire wants cleaning at the next stop.";
            if (notices.HasFlag(FiremanNotice.EmptyAshpan))
                yield return "The ashpan is half full: empty it at the next stop.";
            if (notices.HasFlag(FiremanNotice.ColdOil))
                yield return "The oil is cold: he has the heater open; it fires poorly until it warms.";
            if (notices.HasFlag(FiremanNotice.NoSand))
                yield return "The flues want sanding and the sand is out.";
        }

        private void DrawStatus(FiremanRig rig)
        {
            status = status ?? new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontSize = 14 };
            var man = rig.Fireman;
            var mode = Modes.First(m => m.Mode == man.Mode).Name;
            var r = rig.Readings;
            var text = $"Fireman: {mode}, {r.Pressure:F1} of {man.PressureTarget:F1} bar, glass {r.Glass:P0}" +
                       (man.Call == FiremanCall.HeavyAhead ? ", building for heavy work" : man.Call == FiremanCall.EasingOff ? ", easing off" : "");
            var notices = Notices(rig.Actions.Notices).ToList();
            var area = new Rect(0f, 8f, Screen.width - 16f, 22f);
            Shadowed(area, text, Color.white);
            foreach (var notice in notices)
            {
                area.y += 20f;
                Shadowed(area, notice, Warning);
            }
        }

        private void Shadowed(Rect area, string text, Color color)
        {
            status.normal.textColor = Color.black;
            GUI.Label(new Rect(area.x + 1f, area.y + 1f, area.width, area.height), text, status);
            status.normal.textColor = color;
            GUI.Label(area, text, status);
        }
    }
}
