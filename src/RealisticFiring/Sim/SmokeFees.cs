using System;
using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using LocoSim.Implementations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>One engine's town-smoke allowance and stock environmental account.</summary>
    internal sealed class SmokeFees
    {
        internal const string SaveKey = "rfSmokeNuisance";
        internal const string WarningNotice = "Heavy smoke in a populated area. Clear the stack; continued smoke can add an environmental fee.";
        internal const string ChargeNotice = "Continued heavy smoke in a populated area is adding an environmental fee. Clear the stack to stop it.";
        internal SmokeNuisance Model { get; } = new SmokeNuisance();

        private TrackID lastTrack;
        private SmokeDistrict district;
        private TrackID lastLightUpTrack;
        private bool lightUpAllowed;
        private float basePrice;

        // Explicit town passenger districts, not a station's job-generation
        // radius or every track belonging to an industrial station. Unknown
        // and main-line tracks remain free. No scene queries or moving-origin
        // coordinates are involved.
        internal static SmokeDistrict DistrictFor(TrackID track)
        {
            if (track == null || track.IsGeneric()) return SmokeDistrict.None;
            var id = track.FullID;
            if (!id.EndsWith("-LP", StringComparison.Ordinal) && !id.EndsWith("-SP", StringComparison.Ordinal))
                return SmokeDistrict.None;
            if (id.StartsWith("HB-F-", StringComparison.Ordinal)) return SmokeDistrict.HarborTown;
            if (id.StartsWith("CW-B-", StringComparison.Ordinal)) return SmokeDistrict.CityWest;
            if (id.StartsWith("CS-A-", StringComparison.Ordinal)) return SmokeDistrict.CitySouth;
            if (id.StartsWith("MF-D-", StringComparison.Ordinal)) return SmokeDistrict.MachineFactoryTown;
            if (id.StartsWith("FF-B-", StringComparison.Ordinal)) return SmokeDistrict.FoodFactoryTown;
            return SmokeDistrict.None;
        }

        internal static bool IsLightUpTrack(TrackID track) => Shed.ShedStands.IsAshDisposalTrack(track)
            || (DistrictFor(track) != SmokeDistrict.None && track.FullID.EndsWith("-SP", StringComparison.Ordinal));

        internal bool LightUpAllowed(TrackID track)
        {
            if (!ReferenceEquals(lastLightUpTrack, track))
            {
                lastLightUpTrack = track;
                lightUpAllowed = IsLightUpTrack(track);
            }
            return lightUpAllowed;
        }

        internal void Tick(float seconds, Port meter, TrainCar car, float soot, bool burning, bool lightingUp)
        {
            if (Net.IsClient) return;
            // The chimney is at the front; a tender entering town does not
            // place the engine's stack there. Cache classification by track.
            var track = car?.logicCar?.FrontBogieTrack?.ID;
            if (!ReferenceEquals(lastTrack, track))
            {
                lastTrack = track;
                district = DistrictFor(track);
            }
            var heavy = Heavy(district, soot, burning);
            var exempt = false;
            if (heavy)
            {
                // No service-track scans for dead fires, clean stacks or
                // engines outside town. Even here, scan only on track changes.
                exempt = lightingUp && LightUpAllowed(car?.logicCar?.RearBogieTrack?.ID);
                if (!exempt && !PriceValid(basePrice)) basePrice = ResourceType.EnvironmentDamageCoal.ToV2()?.price ?? 0f;
            }
            var flow = Assess(seconds, meter, district, soot, burning, exempt, basePrice);
            if (flow.FirstCharge || flow.Warning)
            {
                var kind = flow.FirstCharge ? FireEvent.SmokeFeeStarted : FireEvent.SmokeWarning;
                ShowNotice(car, kind);
                Net.Broadcast(car, kind);
            }
        }

        private static bool Heavy(SmokeDistrict place, float soot, bool burning) => place != SmokeDistrict.None
            && burning && !float.IsNaN(soot) && !float.IsInfinity(soot) && soot >= SmokeNuisance.HeavySoot;

        private static bool PriceValid(float price) => price > 0 && !float.IsNaN(price) && !float.IsInfinity(price);

        internal SmokeNuisanceFlow Assess(float seconds, Port meter, SmokeDistrict place, float soot,
            bool burning, bool exempt, float unitPrice)
        {
            if (Net.IsClient) return default;
            if (Heavy(place, soot, burning) && !exempt && (!PriceValid(unitPrice) || meter == null
                || float.IsNaN(meter.Value) || float.IsInfinity(meter.Value))) return default;
            var before = Model.State;
            var flow = Model.Step(seconds, place, soot, burning, exempt);
            if (flow.Fee > 0 && !Apply(meter, flow.Fee, unitPrice))
            {
                Model.Restore(before);
                return default;
            }
            return flow;
        }

        // Base-price dollars become the same resource units the stock
        // environmental ledger already uses. Its difficulty modifier,
        // ownership rules, save/reset path and payment remain in charge.
        internal static bool Apply(Port meter, double fee, float unitPrice)
        {
            if (Net.IsClient || meter == null || !(fee > 0) || double.IsNaN(fee) || double.IsInfinity(fee)
                || !PriceValid(unitPrice) || float.IsNaN(meter.Value) || float.IsInfinity(meter.Value)) return false;
            var value = meter.Value + (float)(fee / unitPrice);
            if (float.IsInfinity(value) || !(value > meter.Value)) return false;
            meter.Value = value;
            return true;
        }

        internal static void ShowNotice(TrainCar car, FireEvent kind)
        {
            if (car == null) return;
            var here = PlayerManager.Car;
            if ((FireboxRegistry.TryGetTended(here, out var coal) && coal.Car == car)
                || (RealisticOilFirebox.TryGetTended(here, out var oil) && oil.Car == car))
                FireController.Announce(kind == FireEvent.SmokeFeeStarted ? ChargeNotice : WarningNotice);
        }

        internal void Save(JObject data)
        {
            if (Model.State.District != SmokeDistrict.None)
                data[SaveKey] = JObject.FromObject(Model.State);
        }

        internal void Restore(JObject data)
        {
            lastTrack = null;
            district = SmokeDistrict.None;
            lastLightUpTrack = null;
            lightUpAllowed = false;
            basePrice = 0;
            Model.Restore(default);
            if (!(data?[SaveKey] is JObject saved)) return;
            try { Model.Restore(saved.ToObject<SmokeNuisanceState>()); }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is FormatException || e is OverflowException)
            {
                // Old or unreadable data grants a fresh allowance; it never
                // creates debt or prevents the engine's fire from loading.
            }
        }
    }
}
