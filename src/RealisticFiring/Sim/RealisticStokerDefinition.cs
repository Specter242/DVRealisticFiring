using LocoSim.Definitions;
using LocoSim.Implementations;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// The mod's own stoker, for locos built without one (the stock S282's
    /// retrofit): the same ports and port references as Custom Car Loader's
    /// stoker, so <see cref="RealisticStoker"/> runs it the same way. Coal
    /// comes from the tender's coal pile when the loco has no tender coal ports.
    /// </summary>
    internal sealed class RealisticStokerDefinition : SimComponentDefinition
    {
        /// <summary>Feed at full speed, in tender kg/s.</summary>
        public float MaxTransferRate;

        /// <summary>Boiler pressure that gives full speed with the throttle open.</summary>
        public float MaxWorkingPressure = 6f;

        /// <summary>Time for the engine to come up to speed, in s.</summary>
        public float SmoothTime = 3f;

        /// <summary>The firebox controller's coal multiplier: tender kg per firebox kg.</summary>
        public float CoalMultiplier = 1f;

        public readonly PortDefinition SteamConsumptionReadOut = new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "STEAM_CONSUMPTION");
        public readonly PortDefinition StokingNormalizedReadOut = new PortDefinition(PortType.READONLY_OUT, PortValueType.STATE, "STOKING_NORMALIZED");
        public readonly PortReferenceDefinition Control = new PortReferenceDefinition(PortValueType.CONTROL, "CONTROL");
        public readonly PortReferenceDefinition SteamPressure = new PortReferenceDefinition(PortValueType.PRESSURE, "STEAM_PRESSURE");
        public readonly PortReferenceDefinition FireboxCoalLevel = new PortReferenceDefinition(PortValueType.COAL, "FIREBOX_COAL_LEVEL");
        public readonly PortReferenceDefinition FireboxCoalCapacity = new PortReferenceDefinition(PortValueType.COAL, "FIREBOX_COAL_CAPACITY");
        public readonly PortReferenceDefinition FireboxCoalControl = new PortReferenceDefinition(PortValueType.CONTROL, "FIREBOX_COAL_CONTROL", writeAllowed: true);
        public readonly PortReferenceDefinition CoalAmount = new PortReferenceDefinition(PortValueType.COAL, "COAL_AMOUNT");
        public readonly PortReferenceDefinition CoalConsume = new PortReferenceDefinition(PortValueType.COAL, "COAL_CONSUMPTION", writeAllowed: true);

        public override SimComponent InstantiateImplementation() =>
            new RealisticStoker(Values(), GetComponentInParent<TrainCar>(), CoalMultiplier, steamWired: false);

        internal StokerDefinitionValues Values() => new StokerDefinitionValues
        {
            Id = ID,
            MaxTransferRate = MaxTransferRate,
            MaxWorkingPressure = MaxWorkingPressure,
            SmoothTime = SmoothTime,
            SteamConsumptionReadOut = SteamConsumptionReadOut,
            StokingNormalizedReadOut = StokingNormalizedReadOut,
            Control = Control,
            SteamPressure = SteamPressure,
            FireboxCoalLevel = FireboxCoalLevel,
            FireboxCoalCapacity = FireboxCoalCapacity,
            FireboxCoalControl = FireboxCoalControl,
            CoalAmount = CoalAmount,
            CoalConsume = CoalConsume,
        };
    }
}
