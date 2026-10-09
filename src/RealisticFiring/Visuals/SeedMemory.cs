using System.Collections.Generic;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Remembers values by particle seed across frames and forgets the
    /// particles that have died: whatever was not recalled or kept since the
    /// last <see cref="Forget"/> is dropped.
    /// </summary>
    internal sealed class SeedMemory<T>
    {
        private Dictionary<uint, T> current = new Dictionary<uint, T>();
        private Dictionary<uint, T> next = new Dictionary<uint, T>();

        /// <summary>The value kept for <paramref name="seed"/>, recording <paramref name="seen"/> if new.</summary>
        public T Recall(uint seed, T seen)
        {
            if (!current.TryGetValue(seed, out var value))
                value = seen;
            next[seed] = value;
            return value;
        }

        /// <summary>Keeps <paramref name="value"/> for <paramref name="seed"/> from now on.</summary>
        public void Keep(uint seed, T value) => next[seed] = value;

        public void Forget()
        {
            var spent = current;
            current = next;
            next = spent;
            next.Clear();
        }
    }
}
