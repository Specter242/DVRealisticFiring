using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
    /// <summary>
    /// The grate as a 3 × 3 grid seen from the cab. Row 0 is the back of the
    /// firebox at the door; row 2 is the front, under the tube plate.
    /// </summary>
    public enum GrateZone
    {
        BackLeft,
        UnderDoor,
        BackRight,
        MidLeft,
        Center,
        MidRight,
        FrontLeft,
        Front,
        FrontRight,
    }

    public static class GrateLayout
    {
        public const int Rows = 3;
        public const int Columns = 3;
        public const int ZoneCount = Rows * Columns;

        private static readonly IReadOnlyList<GrateZone>[] NeighborTable = BuildNeighbors();

        public static int Row(GrateZone zone) => (int)zone / Columns;

        public static int Column(GrateZone zone) => (int)zone % Columns;

        public static GrateZone At(int row, int column)
        {
            if (row < 0 || row >= Rows)
                throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0 || column >= Columns)
                throw new ArgumentOutOfRangeException(nameof(column));
            return (GrateZone)(row * Columns + column);
        }

        /// <summary>A plain name for <paramref name="zone"/>, as a fireman would say it.</summary>
        public static string Describe(GrateZone zone)
        {
            switch (zone)
            {
                case GrateZone.BackLeft: return "back left";
                case GrateZone.UnderDoor: return "under the door";
                case GrateZone.BackRight: return "back right";
                case GrateZone.MidLeft: return "left side";
                case GrateZone.Center: return "center";
                case GrateZone.MidRight: return "right side";
                case GrateZone.FrontLeft: return "front left";
                case GrateZone.Front: return "front";
                case GrateZone.FrontRight: return "front right";
                default: throw new ArgumentOutOfRangeException(nameof(zone));
            }
        }

        /// <summary>Zones sharing an edge with <paramref name="zone"/>.</summary>
        public static IReadOnlyList<GrateZone> Neighbors(GrateZone zone) => NeighborTable[(int)zone];

        private static IReadOnlyList<GrateZone>[] BuildNeighbors()
        {
            var table = new IReadOnlyList<GrateZone>[ZoneCount];
            for (var i = 0; i < ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                int row = Row(zone), column = Column(zone);
                var list = new List<GrateZone>(4);
                if (row > 0) list.Add(At(row - 1, column));
                if (row < Rows - 1) list.Add(At(row + 1, column));
                if (column > 0) list.Add(At(row, column - 1));
                if (column < Columns - 1) list.Add(At(row, column + 1));
                table[i] = list;
            }
            return table;
        }
    }

    /// <summary>
    /// Maps where the fireman points into the firebox to a grate zone, in
    /// thirds. <c>across</c> runs from 0 at the left to 1 at the right, as
    /// seen from the cab; <c>depth</c> from 0 at the back, by the door, to 1
    /// at the front: on the drawn bed, or up the door's coal target where a
    /// cab has no bed (pointing low drops coal just inside the door, high
    /// throws it to the front). Points outside clamp to the nearest zone.
    /// </summary>
    public static class DoorAim
    {
        /// <summary>
        /// The zone at a place across (0 left, 1 right) and deep (0 the back,
        /// at the door, 1 the front): on the bed, or up the door, where a
        /// higher throw goes further.
        /// </summary>
        public static GrateZone ZoneFor(float across, float depth) => GrateLayout.At(Band(depth), Band(across));

        // Thirds; NaN, from a degenerate target, picks the middle.
        private static int Band(float t) => float.IsNaN(t) ? 1 : t < 1f / 3f ? 0 : t < 2f / 3f ? 1 : 2;
    }
}
