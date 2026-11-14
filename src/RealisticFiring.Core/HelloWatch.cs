using System.Collections.Generic;
using System.Linq;

namespace RealisticFiring.Core
{
 public sealed class HelloWatch<TPlayer>
    {
        private readonly Dictionary<byte, KeyValuePair<TPlayer, uint>> unheard = new Dictionary<byte, KeyValuePair<TPlayer, uint>>();
        private readonly HashSet<byte> heard = new HashSet<byte>();

        /// <summary>Guests still awaited.</summary>
        public int Waiting => unheard.Count;

        /// <summary>
        /// A guest joined: wait for their hello until the session's
        /// <paramref name="dueTick"/>, unless it has already come.
        /// </summary>
        public void Joined(byte id, TPlayer player, uint dueTick)
        {
            if (!heard.Contains(id))
                unheard[id] = new KeyValuePair<TPlayer, uint>(player, dueTick);
        }

        /// <summary>A guest's hello arrived, before or after their join.</summary>
        public void Heard(byte id)
        {
            heard.Add(id);
            unheard.Remove(id);
        }

        /// <summary>A guest left: their ID may come back as someone else.</summary>
        public void Left(byte id)
        {
            heard.Remove(id);
            unheard.Remove(id);
        }

        /// <summary>The session ended or a new one began: nobody is awaited or known.</summary>
        public void Clear()
        {
            heard.Clear();
            unheard.Clear();
        }

        /// <summary>
        /// The guests not heard from by <paramref name="tick"/>, each told of
        /// once. Outside a hosted session (<paramref name="hosting"/> false)
        /// nobody is, and anything still awaited is forgotten.
        /// </summary>
        public List<TPlayer> Overdue(uint tick, bool hosting)
        {
            var due = new List<TPlayer>();
            if (!hosting)
            {
                Clear();
                return due;
            }
            foreach (var id in unheard.Where(p => tick >= p.Value.Value).Select(p => p.Key).ToList())
            {
                due.Add(unheard[id].Key);
                unheard.Remove(id);
            }
            return due;
        }
    }
}
