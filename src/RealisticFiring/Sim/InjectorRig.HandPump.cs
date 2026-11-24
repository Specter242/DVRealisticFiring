using System.Linq;
using DV;
using DV.ThingTypes;
using LocoSim.Implementations;
using LocoSim.Resources;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    internal sealed partial class InjectorRig
    {
        // Whether the engine couples to a tender (the game's auto-coupling
        // to one, or no water of its own) and carries its own water; looked
        // for once its sim is built.
        private bool? takesTender;

        /// <summary>
        /// What stops the hand pump now, if anything, by the rules its step
        /// follows (<see cref="Simulate"/>): for the notice when its key is
        /// pressed, and the Water page's row while it is held.
        /// </summary>
        public HandPumpStop HandPumpStop
        {
            get
            {
                var vessel = Vessel(boiler);
                if (vessel == null)
                    return HandPumpStop.None;
                return HandPump.Stop(boiler.isBrokenReadOut.Value != 0f, HoseFilling && CanHoseFill, BoilerPressure, HandPumpMaxPressure,
                    TakesTender && !TenderCoupled, TenderOutletCovered ? TenderWater : 0f, TotalVolume(boiler) - vessel.waterVolume);
            }
        }

        /// <summary>A tank engine, carrying its own water: told to fill its tanks, not its tender.</summary>
        public bool OwnTanks => !TakesTender;

        // The game's own test for a tender (its quick tutorial's): a tender
        // coupled at the rear; or any car there with water for the engine.
        private bool TenderCoupled
        {
            get
            {
                var behind = CoupledTender();
                if (behind == null)
                    return false;
                if (behind.carLivery != null && CarTypes.IsTender(behind.carLivery))
                    return true;
                var flow = behind.SimController != null ? behind.SimController.simFlow : null;
                return flow != null && flow.OrderedSimComps.OfType<ResourceContainer>().Any(c => c.resourceType == ResourceContainerType.WATER);
            }
        }

        private bool TakesTender
        {
            get
            {
                if (takesTender.HasValue)
                    return takesTender.Value;
                if (Car == null)
                    return false;
                if (Car.GetComponent<SteamTenderAutoCoupleMechanism>() != null)
                    return (takesTender = true).Value;
                var flow = Car.SimController != null ? Car.SimController.simFlow : null;
                // Not built yet: never blame a missing tender.
                if (flow == null)
                    return false;
                takesTender = !flow.OrderedSimComps.OfType<ResourceContainer>().Any(c => c.resourceType == ResourceContainerType.WATER);
                return takesTender.Value;
            }
        }
    }
}
