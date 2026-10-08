using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// Turns the game's coarse coal deliveries (up to 36 kg per shovelful on
    /// the S282) into pieces sized to the firebox, as a round of real
    /// shovelfuls would land. Unaimed pieces go where the fire needs them
    /// most (<see cref="FireModel.NeediestZone"/>), as a fireman following
    /// the fire would put them; aimed pieces alternate between the aimed zone
    /// and its neighbors. An aimed piece is at least one real shovelful, so a
    /// real aimed throw lands whole where it was aimed.
    /// </summary>
    public sealed class ShovelDelivery
    {
        /// <summary>Piece mass as a share of one zone's even share of capacity.</summary>
        public const float PieceShareOfZone = 0.25f;

        /// <summary>The most one real shovel throws, in kg.</summary>
        public const float MaxThrowMass = 5f;

        public ShovelDelivery(FireboxParameters parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            PieceMass = PieceShareOfZone * parameters.MaxCoalCapacity / GrateLayout.ZoneCount;
        }

        /// <summary>Coal per piece, in kg.</summary>
        public float PieceMass { get; }

        /// <summary>Adds <paramref name="mass"/> kg of fresh coal; returns the mass accepted.</summary>
        public float Deliver(FireModel fire, float mass, GrateZone? aim)
        {
            if (fire == null)
                throw new ArgumentNullException(nameof(fire));

            var accepted = 0f;
            var remaining = mass;
            var size = aim.HasValue ? Math.Max(PieceMass, MaxThrowMass) : PieceMass;
            for (var piece = 0; remaining > 1e-6f; piece++)
            {
                var pieceMass = Math.Min(size, remaining);
                var zone = aim.HasValue ? AimedZone(aim.Value, piece) : fire.NeediestZone();
                var added = fire.AddCoal(pieceMass, zone);
                accepted += added;
                remaining -= pieceMass;
                if (added < pieceMass)
                    break;
            }
            return accepted;
        }

        // aim, first neighbor, aim, second neighbor, ...
        private static GrateZone AimedZone(GrateZone aim, int piece)
        {
            if (piece % 2 == 0)
                return aim;
            var neighbors = GrateLayout.Neighbors(aim);
            return neighbors[(piece / 2) % neighbors.Count];
        }
    }
}
