using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal static class FireboxCharacters
    {
        internal sealed class Firebox
        {
            public Firebox(string summary, GrateCharacter grate)
            {
                Summary = summary;
                Grate = grate;
            }

            /// <summary>One line for the log: what the loco's firebox is like.</summary>
            public string Summary { get; }

            public GrateCharacter Grate { get; }

            /// <summary>
            /// Known physical grate area, m², when the visible coal mesh does
            /// not stand for it: the QJ's shows only part of it, the 4F's runs
            /// through its box's sides.
            /// </summary>
            public float? PrototypeGrateArea { get; set; }

            /// <summary>The prototype's grate slope in degrees (negative falling to the front), used when the coal pile measures level.</summary>
            public float? PrototypeSlope { get; set; }

            /// <summary>
            /// Whether the prototype's slope stands even where the pile is
            /// tilted: a pile tilted with the sloping front of a grate that
            /// runs level at the back is steeper than the grate averages.
            /// </summary>
            public bool PrototypeSlopeOverPile { get; set; }

            /// <summary>The stoker's back lanes against a typical stoker's: under 1, its back corners run short.</summary>
            public float StokerBackLanes { get; set; } = 1f;

            /// <summary>How much more lump coal the stoker sends left than right.</summary>
            public float StokerLumpsLeft { get; set; }

 public float? ArchLength { get; set; }

 public OverFireJets Jets { get; set; }

            /// <summary>Whether over-fire jets are fitted as built rather than bought as a stock career upgrade.</summary>
            public bool JetsAsBuilt { get; set; }
        }

        // A measured slope this close to level is a coal pile modeled flat.
        private const float LevelSlope = 1f;

        private static readonly Firebox Typical = new Firebox("a typical firebox", GrateCharacter.Typical);

        public static Firebox For(TrainCar car) =>
            ForLivery(car != null && car.carLivery != null ? car.carLivery.id : null);

        internal static Firebox ForLivery(string livery) =>
            LocomotiveProfiles.TryGet(livery, out var profile) && profile.Firebox != null ? profile.Firebox
            : MakerFitRegistry.TryGetFirebox(livery, out var maker) ? maker
            : Typical;

        /// <summary>Physical grate area when known; otherwise the measured mesh area, including 0 before it loads.</summary>
        internal static float GrateAreaForLivery(string livery, float measured) =>
            ForLivery(livery).PrototypeGrateArea ?? measured;

        /// <summary>The grate's slope: as measured from the coal pile, or the prototype's where the pile is modeled level.</summary>
        public static float Slope(TrainCar car, float measured)
        {
            var firebox = For(car);
            var prototype = firebox.PrototypeSlope;
            return prototype.HasValue && (firebox.PrototypeSlopeOverPile || System.Math.Abs(measured) < LevelSlope) ? prototype.Value : measured;
        }
    }
}
