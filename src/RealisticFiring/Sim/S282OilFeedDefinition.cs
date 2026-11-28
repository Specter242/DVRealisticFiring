using LocoSim.Definitions;
using LocoSim.Implementations;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// The oil feed of an S282 converted to oil (<see cref="S282OilConversion"/>):
    /// the ports and port references of the stoker a CCL oil burner fakes its
    /// feed with, so <see cref="OilFeed"/> runs it the same way. Its control
    /// is the firing valve (<see cref="S282OilConversion.FiringValve"/>); with
    /// no tender ports on the engine, the oil comes from the tender's coal
    /// pile, which holds it (<see cref="S282OilConversion.TenderPile"/>).
    /// </summary>
    internal sealed class S282OilFeedDefinition : SimComponentDefinition
    {
        /// <summary>Tender units (litres of oil) per firebox kg.</summary>
        public float TenderPerFireboxKg = 1f;

        public readonly PortDefinition SteamConsumptionReadOut = new PortDefinition(PortType.READONLY_OUT, PortValueType.MASS_RATE, "STEAM_CONSUMPTION");
        public readonly PortDefinition StokingNormalizedReadOut = new PortDefinition(PortType.READONLY_OUT, PortValueType.STATE, "STOKING_NORMALIZED");
        public readonly PortReferenceDefinition Control = new PortReferenceDefinition(PortValueType.CONTROL, "CONTROL");
        public readonly PortReferenceDefinition SteamPressure = new PortReferenceDefinition(PortValueType.PRESSURE, "STEAM_PRESSURE");
        public readonly PortReferenceDefinition FireboxCoalLevel = new PortReferenceDefinition(PortValueType.COAL, "FIREBOX_COAL_LEVEL");
        public readonly PortReferenceDefinition FireboxCoalCapacity = new PortReferenceDefinition(PortValueType.COAL, "FIREBOX_COAL_CAPACITY");
        public readonly PortReferenceDefinition FireboxCoalControl = new PortReferenceDefinition(PortValueType.CONTROL, "FIREBOX_COAL_CONTROL", writeAllowed: true);
        public readonly PortReferenceDefinition CoalAmount = new PortReferenceDefinition(PortValueType.COAL, "COAL_AMOUNT");
        public readonly PortReferenceDefinition CoalConsume = new PortReferenceDefinition(PortValueType.COAL, "COAL_CONSUMPTION", writeAllowed: true);

        public override SimComponent InstantiateImplementation()
        {
            var feed = new OilFeed(new StokerDefinitionValues
            {
                Id = ID,
                SteamConsumptionReadOut = SteamConsumptionReadOut,
                StokingNormalizedReadOut = StokingNormalizedReadOut,
                Control = Control,
                SteamPressure = SteamPressure,
                FireboxCoalLevel = FireboxCoalLevel,
                FireboxCoalCapacity = FireboxCoalCapacity,
                FireboxCoalControl = FireboxCoalControl,
                CoalAmount = CoalAmount,
                CoalConsume = CoalConsume,
            }, GetComponentInParent<TrainCar>(), TenderPerFireboxKg, steamWired: false);
            feed.JoinFirebox();
            return feed;
        }
    }
}
