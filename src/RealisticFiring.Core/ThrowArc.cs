using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// A hand throw of wood or a rag toward the point the player looks at
    /// (non-VR): launched at about <see cref="Speed"/> on the ballistic arc
    /// that reaches that point, so a throw at the open door goes through it
    /// and a throw elsewhere lands elsewhere. Gravity and collisions do the
    /// rest. The stock throw (2.8 m/s straight along the hand, from the right
    /// of the view) cannot reach a firehole at waist height from the firing
    /// position unless aimed well above and to the left of it.
    /// </summary>
    public static class ThrowArc
    {
        /// <summary>The throw's speed before gravity's correction, m/s: a firm underhand toss.</summary>
        public const float Speed = 4.5f;

        /// <summary>Beyond this the target is not aimed at, only thrown toward, m.</summary>
        public const float Reach = 3.5f;

        /// <summary>
        /// The launch velocity, relative to the ground under the thrower,
        /// from <paramref name="from"/> to land on <paramref name="target"/>
        /// under <paramref name="gravityY"/> (negative), or straight along
        /// the line at <see cref="Speed"/> when the target is beyond reach.
        /// </summary>
        public static CarPoint Launch(CarPoint from, CarPoint target, float gravityY)
        {
            var d = target - from;
            var distance = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (!(distance > 1e-3f)) return new CarPoint(0f, 0f, 0f);
            if (distance > Reach)
                return new CarPoint(d.X / distance * Speed, d.Y / distance * Speed, d.Z / distance * Speed);
            var t = distance / Speed;
            return new CarPoint(d.X / t, d.Y / t - 0.5f * gravityY * t, d.Z / t);
        }
    }
}
