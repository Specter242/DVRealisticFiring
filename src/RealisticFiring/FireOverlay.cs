using System.Collections.Generic;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring
{
    /// <summary>IMGUI readout of the fire on the player's loco.</summary>
    internal sealed class FireOverlay
    {
        private const int WindowId = 0x5246_4952; // "RFIR"

        private static readonly Color AimedCellColor = new Color(0.2f, 0.75f, 1f);

        // Indexed by grate row: 0 at the door, 2 at the front.
        private static readonly string[] RowNames = { "door", "middle", "front" };

        private Rect window = new Rect(20f, 80f, 500f, 10f);
        private RealisticFirebox shown;
        private float shakeTime = float.NegativeInfinity;
        private float now;
        private GUIStyle cell;

        public void NoteShake(float time) => shakeTime = time;

        public void Draw(RealisticFirebox firebox, float time)
        {
            shown = firebox;
            now = time;
            window = GUILayout.Window(WindowId, window, DrawWindow, "Realistic Firing");
        }

        private void DrawWindow(int id)
        {
            var fire = shown.Model;
            var p = fire.Parameters;
            cell = cell ?? new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };

            GUILayout.Label($"{shown.Car.carLivery.id} {shown.Car.ID}   fire {(fire.FireOn ? "ON" : "out")}" +
                            (shown.StartupMultiplier > 1.01f ? $"   startup ×{shown.StartupMultiplier:F1}" : ""));
            GUILayout.Label($"Heat {fire.Heat / 1e6f:F2} MW ({fire.Heat / p.MaxHeatOutput:P0} of max)   " +
                            $"{fire.HeatPerCoal / 1e6f:F1} MJ/kg   burning {fire.ConsumptionRate * 3600f:F0} kg/h");
            GUILayout.Label($"Temperature {fire.Temperature:F0} °C   smoke {fire.SmokeDensity:P0}   " +
                            $"bed air {fire.OxygenAvailability:P0}   smoke burnt {fire.OverFireBurnout:P0}");
            GUILayout.Label($"Draft {shown.AirFlowIn:F2} kg/s   grate {fire.PrimaryAirFlow:F2}   " +
                            $"door {fire.DoorAirFlow:F2} ({(shown.HasDoorPort ? shown.DoorOpening.ToString("P0") : "no port")})   " +
                            $"excess {fire.ExcessAirFlow:F2}");
            GUILayout.Label($"Coal {fire.CoalMass:F1}/{p.MaxCoalCapacity:F0} kg   ash {fire.AshMass:F2} kg   " +
                            $"clinker {fire.ClinkerMass:F2} kg   dropped {fire.CoalDroppedThroughGrate:F1} kg");
            GUILayout.Label($"Last shovelful {shown.LastDeliveryMass:F2} kg → {(shown.LastDeliveryAim.HasValue ? shown.LastDeliveryAim.Value.ToString() : "following the fire")}" +
                            (now - shakeTime < 3f ? "   grate shaken" : ""));
            if (InjectorRig.TryGetTended(shown.Car, out var rig))
                GUILayout.Label(FireController.Describe(rig));
            var aim = shown.HoverAim;
            var aimed = aim.HasValue && aim.Value.IsLiveAt(now) ? aim.Value.Zone : (GrateZone?)null;
            GUILayout.Label(aimed.HasValue
                ? $"{(aim.Value.Raking ? "Raking" : "Aiming at")} {aim.Value}"
                : "Aiming: point a shovel into the open door (empty to rake)");

            // Front of the firebox at the top, door at the bottom, as seen from the cab.
            GUILayout.Label("Grate as seen from the cab: coal in each zone, and its share of the air");
            var primary = Mathf.Max(fire.PrimaryAirFlow, 1e-6f);
            for (var row = GrateLayout.Rows - 1; row >= 0; row--)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(RowNames[row], GUILayout.Width(44f), GUILayout.Height(38f));
                for (var column = 0; column < GrateLayout.Columns; column++)
                {
                    var zone = fire.Zones[(int)GrateLayout.At(row, column)];
                    var glow = Mathf.Clamp01(zone.CombustionRate / (p.MaxCombustionRate / GrateLayout.ZoneCount));
                    GUI.backgroundColor = aimed == GrateLayout.At(row, column)
                        ? AimedCellColor
                        : Color.Lerp(new Color(0.35f, 0.35f, 0.4f), new Color(1f, 0.45f, 0.1f), glow);
                    GUILayout.Box(CellText(zone, primary), cell, GUILayout.Width(146f), GUILayout.Height(38f));
                }
                GUILayout.EndHorizontal();
            }
            GUI.backgroundColor = Color.white;

            GUI.DragWindow();
        }

        // Coal first, since that is what the fireman manages; unlit coal, ash,
        // and clinker only when there is some; then the zone's share of the air.
        private static string CellText(ZoneState zone, float primaryAir)
        {
            var detail = new List<string>();
            if (zone.Fresh >= 0.05f)
                detail.Add($"{zone.Fresh:F1} unlit");
            if (zone.Ash + zone.Clinker >= 0.05f)
                detail.Add(zone.Clinker >= 0.05f ? $"ash {zone.Ash:F1} + clinker {zone.Clinker:F1}" : $"ash {zone.Ash:F1}");
            detail.Add($"air {zone.AirFlow / primaryAir:P0}");
            return $"{zone.Coal:F1} kg coal\n{string.Join(", ", detail)}";
        }
    }
}
