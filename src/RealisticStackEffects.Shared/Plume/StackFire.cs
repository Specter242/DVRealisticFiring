using LocoSim.Implementations;

namespace RealisticStackEffects
{
 internal struct StackReading
    {
        public bool FireOn;

        /// <summary>The fire's heat and the most it makes, W.</summary>
        public float Heat;
        public float MostHeat;

        /// <summary>Visible smoke leaving the fire, 0 to 1 (<see cref="Core.PlumeInputs.Soot"/>).</summary>
        public float Soot;

        /// <summary>
        /// The firebox's gas and a shovelful's thick smoke reaching the stack,
        /// in firebox puffs a second (<see cref="Core.PlumeInputs.FireboxGas"/>).
        /// </summary>
        public float FireboxGas;
        public float FireboxSmokeOut;

        /// <summary>How clear the stack is between beats, 0 to 1 (<see cref="Core.PlumeInputs.ClearStack"/>).</summary>
        public float ClearStack;

        /// <summary>The air drawn through the fire, kg/s.</summary>
        public float AirThrough;

        /// <summary>Whether the fire throws cinders at all: an oil burner throws none.</summary>
        public bool Cinders;

        /// <summary>
        /// Cinders bound for the stack this frame, kg: a stoker's lost fines,
        /// which go out with the next beat, and a shaken grate's live coal,
        /// which goes out over the next few.
        /// </summary>
        public float Fines;
        public float ShakenCoal;

        /// <summary>A melted fusible plug's steam finding its way out of the stack, kg/s.</summary>
        public float PlugSteam;

        /// <summary>
        /// The share of the exhaust that is still water as it leaves, 0 to
        /// 0.3, or NaN to keep the last (the engine takes no steam).
        /// </summary>
        public float ExhaustWetness;

        /// <summary>
        /// The steam going up the stack, kg/s: the engine's exhaust, the
        /// blower's, and the air pump's; and the blower's and pump's alone.
        /// </summary>
        public float StackSteam;
        public float BlowerSteam;

        /// <summary>
        /// The stack's natural draft as a share of the smokebox's stock total
        /// flow, which counts it even through a dead engine; NaN without a
        /// draft to read.
        /// </summary>
        public float NaturalFlow;
    }

 internal interface IStackFire
    {
        /// <summary>
        /// Reads this frame's fire and draft, <paramref name="dt"/> s on from
        /// the last, into <paramref name="reading"/>. The engine's exhaust is
        /// null where the plume found none.
        /// </summary>
        void Read(float dt, SteamExhaust exhaust, ref StackReading reading);

        /// <summary>Whether the player is on this engine or its tender.</summary>
        bool Tended { get; }
    }
}
