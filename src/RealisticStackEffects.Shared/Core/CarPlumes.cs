using System;
using System.Collections.Generic;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// Tracks every chimney plume attached to one locomotive car (including
    /// articulated or double-chimney locomotives with multiple smoke readers),
    /// keeps engine-wide steam ownership (<c>SteamVents</c>) and stock steam
    /// sorting ownership each on a single active plume per car, and coordinates
    /// handoff when an owning plume is released while another plume remains
    /// active on the same car.
    /// </summary>
    public sealed class CarPlumes<TPlume> where TPlume : class
    {
        private readonly List<TPlume> plumes = new List<TPlume>();
        private bool needsVentsHandoff;
        private bool needsSortingHandoff;

        /// <summary>Every plume currently registered on this car, in attachment order.</summary>
        public IReadOnlyList<TPlume> All => plumes;

        /// <summary>Number of plumes currently registered on this car.</summary>
        public int Count => plumes.Count;

        /// <summary>
        /// True after a <c>SteamVents</c>-owning plume leaves this car while at
        /// least one other plume remains, until a surviving active plume takes
        /// over or another plume acquires vents ownership.
        /// </summary>
        public bool NeedsVentsHandoff => needsVentsHandoff;

        /// <summary>
        /// True after a stock-steam-sorting-owning plume leaves this car while
        /// at least one other plume remains, until a surviving active plume
        /// takes over or another plume acquires sorting ownership.
        /// </summary>
        public bool NeedsSortingHandoff => needsSortingHandoff;

        /// <summary>True when either <see cref="NeedsVentsHandoff"/> or <see cref="NeedsSortingHandoff"/> is pending.</summary>
        public bool NeedsAnyHandoff => needsVentsHandoff || needsSortingHandoff;

        /// <summary>
        /// Registers <paramref name="plume"/> on this car if it is not already
        /// present. Returns true if it was added.
        /// </summary>
        public bool Register(TPlume plume)
        {
            if (plume == null)
                return false;
            for (var i = 0; i < plumes.Count; i++)
            {
                if (ReferenceEquals(plumes[i], plume))
                    return false;
            }
            plumes.Add(plume);
            return true;
        }

        /// <summary>
        /// Removes dead entries (for which <paramref name="isAlive"/> returns false).
        /// Clears any pending handoffs if no plumes remain.
        /// </summary>
        public int Prune(Func<TPlume, bool> isAlive)
        {
            if (isAlive == null)
                return 0;
            var removed = 0;
            for (var i = plumes.Count - 1; i >= 0; i--)
            {
                if (plumes[i] == null || !isAlive(plumes[i]))
                {
                    plumes.RemoveAt(i);
                    removed++;
                }
            }
            if (plumes.Count == 0)
            {
                needsVentsHandoff = false;
                needsSortingHandoff = false;
            }
            return removed;
        }

        /// <summary>
        /// Removes <paramref name="plume"/> from this car. When <paramref name="ownedVents"/>
        /// or <paramref name="ownedSorting"/> is true and at least one other plume remains
        /// on the car, marks the corresponding handoff pending so a surviving active plume
        /// can take over safely. Returns true if <paramref name="plume"/> was present.
        /// </summary>
        public bool Remove(TPlume plume, bool ownedVents, bool ownedSorting = false)
        {
            if (plume == null)
                return false;
            var index = -1;
            for (var i = 0; i < plumes.Count; i++)
            {
                if (ReferenceEquals(plumes[i], plume))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
                return false;
            plumes.RemoveAt(index);
            if (plumes.Count == 0)
            {
                needsVentsHandoff = false;
                needsSortingHandoff = false;
            }
            else
            {
                if (ownedVents)
                    needsVentsHandoff = true;
                if (ownedSorting)
                    needsSortingHandoff = true;
            }
            return true;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> may create and own the car's
        /// engine steam: true only when <paramref name="candidate"/> is active
        /// and no other active plume on this car already owns it.
        /// </summary>
        public bool CanOwnVents(TPlume candidate, Func<TPlume, bool> isActive, Func<TPlume, bool> hasVents) =>
            CanOwn(candidate, isActive, hasVents);

        /// <summary>
        /// Whether <paramref name="candidate"/> may record and restore the car's
        /// stock steam sorting: true only when <paramref name="candidate"/> is
        /// active and no other active plume on this car already owns it.
        /// </summary>
        public bool CanOwnSorting(TPlume candidate, Func<TPlume, bool> isActive, Func<TPlume, bool> hasSorting) =>
            CanOwn(candidate, isActive, hasSorting);

        private bool CanOwn(TPlume candidate, Func<TPlume, bool> isActive, Func<TPlume, bool> hasOwnership)
        {
            if (candidate == null || isActive == null || hasOwnership == null || !isActive(candidate))
                return false;
            for (var i = 0; i < plumes.Count; i++)
            {
                var other = plumes[i];
                if (!ReferenceEquals(other, candidate) && isActive(other) && hasOwnership(other))
                    return false;
            }
            return true;
        }

        /// <summary>Clears any pending vents handoff once a plume on this car has taken ownership.</summary>
        public void ClearVentsHandoff() => needsVentsHandoff = false;

        /// <summary>Clears any pending sorting handoff once a plume on this car has taken ownership.</summary>
        public void ClearSortingHandoff() => needsSortingHandoff = false;

        /// <summary>
        /// Claims a pending vents handoff for <paramref name="candidate"/> on its next
        /// update. Returns true once, for the first eligible active plume, when no
        /// other active plume already owns vents. If <paramref name="canHostVents"/>
        /// is supplied and <paramref name="candidate"/> cannot host vents while
        /// another active plume on the car can, yields without clearing the handoff.
        /// </summary>
        public bool TryConsumeVentsHandoff(TPlume candidate, Func<TPlume, bool> isActive, Func<TPlume, bool> hasVents,
            Func<TPlume, bool> canHostVents = null) =>
            TryConsumeHandoff(ref needsVentsHandoff, candidate, isActive, hasVents, canHostVents);

        /// <summary>
        /// Claims a pending stock-steam-sorting handoff for <paramref name="candidate"/>
        /// on its next update. Returns true once, for the first eligible active plume,
        /// when no active plume already owns sorting. If <paramref name="preferOwner"/>
        /// is supplied and <paramref name="candidate"/> does not satisfy it while
        /// another active plume on the car does, yields without clearing the handoff.
        /// </summary>
        public bool TryConsumeSortingHandoff(TPlume candidate, Func<TPlume, bool> isActive, Func<TPlume, bool> hasSorting,
            Func<TPlume, bool> preferOwner = null) =>
            TryConsumeHandoff(ref needsSortingHandoff, candidate, isActive, hasSorting, preferOwner);

        private bool TryConsumeHandoff(ref bool pending, TPlume candidate, Func<TPlume, bool> isActive,
            Func<TPlume, bool> hasOwnership, Func<TPlume, bool> preferCandidate)
        {
            if (!pending || candidate == null || isActive == null || hasOwnership == null)
                return false;
            if (!isActive(candidate))
                return false;
            if (hasOwnership(candidate))
            {
                pending = false;
                return false;
            }
            var registered = false;
            var otherPreferred = false;
            var candidatePreferred = preferCandidate == null || preferCandidate(candidate);
            for (var i = 0; i < plumes.Count; i++)
            {
                var other = plumes[i];
                if (ReferenceEquals(other, candidate))
                {
                    registered = true;
                    continue;
                }
                if (other == null || !isActive(other))
                    continue;
                if (hasOwnership(other))
                {
                    pending = false;
                    return false;
                }
                if (!candidatePreferred && preferCandidate(other))
                    otherPreferred = true;
            }
            if (!registered)
                return false;
            if (!candidatePreferred && otherPreferred)
                return false;
            pending = false;
            return true;
        }

        /// <summary>
        /// Returns the car's primary active plume: the active plume that owns
        /// vents first, or else the first active plume on the car; null if none
        /// is active.
        /// </summary>
        public TPlume Primary(Func<TPlume, bool> isActive, Func<TPlume, bool> hasVents) =>
            CarPlumes.SelectPrimary(plumes, isActive, hasVents);

        /// <summary>
        /// Computes the maximum <paramref name="value"/> across every active
        /// plume on this car. Returns false if no active plume is registered.
        /// </summary>
        public bool TryGetMax(Func<TPlume, bool> isActive, Func<TPlume, float> value, out float max)
        {
            max = 0f;
            if (isActive == null || value == null)
                return false;
            var found = false;
            for (var i = 0; i < plumes.Count; i++)
            {
                var plume = plumes[i];
                if (plume == null || !isActive(plume))
                    continue;
                var current = value(plume);
                if (float.IsNaN(current))
                    current = 0f;
                if (!found || current > max)
                {
                    max = current;
                    found = true;
                }
            }
            return found;
        }
    }

    /// <summary>Selection helpers for multi-stack locomotives shared across plume lookups.</summary>
    public static class CarPlumes
    {
        /// <summary>
        /// Selects the primary active plume from <paramref name="candidates"/>:
        /// prefers the first active plume that owns vents, falling back to the
        /// first active plume if none owns vents; null if none is active.
        /// </summary>
        public static TPlume SelectPrimary<TPlume>(IReadOnlyList<TPlume> candidates, Func<TPlume, bool> isActive, Func<TPlume, bool> hasVents)
            where TPlume : class
        {
            if (candidates == null || isActive == null)
                return null;
            TPlume fallback = null;
            for (var i = 0; i < candidates.Count; i++)
            {
                var plume = candidates[i];
                if (plume == null || !isActive(plume))
                    continue;
                if (hasVents != null && hasVents(plume))
                    return plume;
                if (fallback == null)
                    fallback = plume;
            }
            return fallback;
        }

        /// <summary>
        /// Gathers every renderer belonging to any chimney stack on a car so
        /// stock steam sorting excludes sibling smoke and ember systems
        /// (including readers that have not attached yet) and inactive plume
        /// FX children (such as a disabled heat shimmer) without touching
        /// genuine engine steam renderers.
        /// </summary>
        public static HashSet<TRenderer> CollectStackRenderers<TRenderer, TPlume, TReader, TObject>(
            IEnumerable<TRenderer> ownRenderers,
            IEnumerable<TPlume> plumes,
            IEnumerable<TReader> readers,
            Func<TPlume, IEnumerable<TRenderer>> activeRenderersOf,
            Func<TPlume, TObject> effectsRootOf,
            Func<TReader, TObject> smokeParentOf,
            Func<TReader, TObject> emberParentOf,
            Func<TObject, IEnumerable<TRenderer>> renderersUnder,
            IEqualityComparer<TRenderer> comparer = null)
            where TRenderer : class
            where TPlume : class
            where TReader : class
            where TObject : class
        {
            var excluded = new HashSet<TRenderer>(comparer);
            AddAll(ownRenderers, excluded);
            if (plumes != null)
            {
                foreach (var plume in plumes)
                {
                    if (plume == null)
                        continue;
                    if (activeRenderersOf != null)
                        AddAll(activeRenderersOf(plume), excluded);
                    if (effectsRootOf != null && renderersUnder != null)
                        AddAll(renderersUnder(effectsRootOf(plume)), excluded);
                }
            }
            if (readers != null && renderersUnder != null)
            {
                foreach (var reader in readers)
                {
                    if (reader == null)
                        continue;
                    if (smokeParentOf != null)
                        AddAll(renderersUnder(smokeParentOf(reader)), excluded);
                    if (emberParentOf != null)
                        AddAll(renderersUnder(emberParentOf(reader)), excluded);
                }
            }
            return excluded;
        }

        private static void AddAll<TRenderer>(IEnumerable<TRenderer> items, HashSet<TRenderer> into)
            where TRenderer : class
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                if (item != null)
                    into.Add(item);
            }
        }
    }
}
