using System.Collections.Generic;

namespace RealisticStackEffects
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

        /// <summary>The value kept for <paramref name="seed"/>, kept on; false if none is.</summary>
        public bool TryRecall(uint seed, out T value)
        {
            if (!current.TryGetValue(seed, out value))
                return false;
            next[seed] = value;
            return true;
        }

        /// <summary>
        /// Records <paramref name="value"/> for a particle not yet seen: it
        /// can be recalled at once and still is after the next
        /// <see cref="Forget"/>, though not seen before it.
        /// </summary>
        public void Expect(uint seed, T value)
        {
            current[seed] = value;
            next[seed] = value;
        }

        public void Forget()
        {
            var spent = current;
            current = next;
            next = spent;
            next.Clear();
        }
    }
}
