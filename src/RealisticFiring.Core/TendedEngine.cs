namespace RealisticFiring.Core
{
 public static class TendedEngine
    {
        /// <summary>
        /// The engine tended from a car: <paramref name="own"/>, the car itself
        /// when it is one, else <paramref name="front"/> or
        /// <paramref name="rear"/>, the engines coupled at its ends, preferring
        /// <paramref name="lastIn"/> between two. Each is null where there is
        /// none; the result is null when there is no engine.
        /// </summary>
        public static T Pick<T>(T own, T front, T rear, T lastIn) where T : class
        {
            if (own != null)
                return own;
            if (front != null && rear != null && ReferenceEquals(rear, lastIn))
                return rear;
            return front ?? rear;
        }
    }
}
