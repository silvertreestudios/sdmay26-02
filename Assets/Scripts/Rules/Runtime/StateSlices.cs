using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Provides common, feature-agnostic queries over authoritative creature state slices.
    /// </summary>
    public static class StateSliceSnapshotExtensions
    {
        /// <summary>
        /// Determines whether a creature has authoritative health state with at least one Hit Point.
        /// </summary>
        /// <param name="health">The health slice from an authoritative rules snapshot.</param>
        /// <param name="creature">The creature whose ability to act is being queried.</param>
        /// <returns>
        /// <see langword="true"/> when the creature has health state and its current Hit Points are
        /// greater than zero; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool IsAlive(
            this StateSliceSnapshot<CreatureId, HealthState> health,
            CreatureId creature
        ) => health != null && health.TryGet(creature, out HealthState state) && state.Current > 0;

        /// <summary>
        /// Determines whether a creature can pay a requested number of actions from current state.
        /// </summary>
        /// <param name="actionEconomy">The action-economy slice from an authoritative snapshot.</param>
        /// <param name="creature">The creature that would spend the actions.</param>
        /// <param name="actions">The non-negative number of actions required.</param>
        /// <returns>
        /// <see langword="true"/> when the request is non-negative, the creature has action-economy
        /// state, and enough actions remain; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool CanSpendActions(
            this StateSliceSnapshot<CreatureId, ActionEconomyState> actionEconomy,
            CreatureId creature,
            int actions
        ) =>
            actions >= 0
            && actionEconomy != null
            && actionEconomy.TryGet(creature, out ActionEconomyState state)
            && state.ActionsRemaining >= actions;
    }

    public sealed class StateSliceSnapshot<TKey, TValue>
        : IReadOnlyCollection<KeyValuePair<TKey, TValue>>
    {
        private readonly IReadOnlyDictionary<TKey, TValue> values;

        internal StateSliceSnapshot(Dictionary<TKey, TValue> values)
        {
            this.values = new ReadOnlyDictionary<TKey, TValue>(values);
        }

        public int Count => values.Count;
        public TValue this[TKey key] => values[key];

        public bool Contains(TKey key) => values.ContainsKey(key);

        public bool TryGet(TKey key, out TValue value) => values.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class StateSliceDraft<TKey, TValue> : IReadOnlyCollection<KeyValuePair<TKey, TValue>>
    {
        private readonly Dictionary<TKey, TValue> original;
        private readonly Func<TKey, TValue, bool> isValidEntry;
        private Dictionary<TKey, TValue> writable;

        internal StateSliceDraft(
            Dictionary<TKey, TValue> original,
            Func<TKey, TValue, bool> isValidEntry = null
        )
        {
            this.original = original;
            this.isValidEntry = isValidEntry;
        }

        internal virtual bool IsDirty
        {
            get
            {
                if (writable == null || writable.Count != original.Count)
                    return writable != null;

                foreach (KeyValuePair<TKey, TValue> pair in writable)
                {
                    if (
                        !original.TryGetValue(pair.Key, out TValue originalValue)
                        || !EqualityComparer<TValue>.Default.Equals(pair.Value, originalValue)
                    )
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        private Dictionary<TKey, TValue> Current => writable ?? original;

        public int Count => Current.Count;

        public bool Contains(TKey key) => Current.ContainsKey(key);

        public bool TryGet(TKey key, out TValue value) => Current.TryGetValue(key, out value);

        /// <summary>Enumerates the transaction's current values without exposing its backing map.</summary>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Current.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public virtual bool Set(TKey key, TValue value)
        {
            if (isValidEntry != null && !isValidEntry(key, value))
                throw new ArgumentException(
                    "The state value does not match its slice key.",
                    nameof(value)
                );

            if (
                Current.TryGetValue(key, out TValue existing)
                && EqualityComparer<TValue>.Default.Equals(existing, value)
            )
            {
                return false;
            }

            EnsureWritable();
            writable[key] = value;
            return true;
        }

        public virtual bool Remove(TKey key)
        {
            if (!Current.ContainsKey(key))
                return false;

            EnsureWritable();
            return writable.Remove(key);
        }

        private void EnsureWritable()
        {
            if (writable == null)
                writable = new Dictionary<TKey, TValue>(original);
        }

        internal Dictionary<TKey, TValue> BuildCommittedValues()
        {
            // A reducer can retain the draft reference. Never let that reference
            // share a writable dictionary with committed state or old snapshots.
            return writable == null ? original : new Dictionary<TKey, TValue>(writable);
        }
    }

    /// <summary>
    /// Provides reducer-scoped health writes while preserving an internal exact-mutation stamp for
    /// temporary-Hit-Point pools.
    /// </summary>
    /// <remarks>
    /// A pool change receives a new stamp even when a later write restores the same public amount
    /// and source. Other health changes preserve the current stamp. Removed entries leave a hidden
    /// revision tombstone so a later re-add cannot recreate an earlier exact pool identity.
    /// </remarks>
    internal sealed class HealthStateDraft : StateSliceDraft<CreatureId, HealthState>
    {
        private readonly Dictionary<CreatureId, long> originalTemporaryHitPointRevisionTombstones;
        private Dictionary<CreatureId, long> writableTemporaryHitPointRevisionTombstones;
        private bool temporaryHitPointRevisionChanged;

        internal HealthStateDraft(
            Dictionary<CreatureId, HealthState> original,
            Dictionary<CreatureId, long> temporaryHitPointRevisionTombstones
        )
            : base(original, (id, value) => !id.IsEmpty) =>
            originalTemporaryHitPointRevisionTombstones =
                temporaryHitPointRevisionTombstones
                ?? throw new ArgumentNullException(nameof(temporaryHitPointRevisionTombstones));

        /// <summary>
        /// Writes health, stamping the value when its temporary-Hit-Point amount or source changed.
        /// </summary>
        /// <returns><see langword="true"/> when the transaction's health value changed.</returns>
        /// <exception cref="InvalidOperationException">
        /// The temporary-Hit-Point revision sequence is exhausted.
        /// </exception>
        public override bool Set(CreatureId key, HealthState value)
        {
            if (TryGet(key, out HealthState current))
            {
                bool poolChanged =
                    current.Temporary != value.Temporary
                    || current.TemporarySource != value.TemporarySource;
                long revision = current.TemporaryHitPointRevision;
                if (poolChanged)
                {
                    if (revision == long.MaxValue)
                        throw new InvalidOperationException(
                            "The temporary Hit Point revision sequence is exhausted."
                        );
                    revision++;
                    temporaryHitPointRevisionChanged = true;
                }
                value = value.WithTemporaryHitPointRevision(revision);
            }
            else
            {
                if (
                    CurrentTemporaryHitPointRevisionTombstones.TryGetValue(
                        key,
                        out long removedRevision
                    )
                )
                {
                    if (removedRevision == long.MaxValue)
                        throw new InvalidOperationException(
                            "The temporary Hit Point revision sequence is exhausted."
                        );
                    value = value.WithTemporaryHitPointRevision(removedRevision + 1);
                    bool changed = base.Set(key, value);
                    EnsureWritableTemporaryHitPointRevisionTombstones();
                    writableTemporaryHitPointRevisionTombstones.Remove(key);
                    temporaryHitPointRevisionChanged = true;
                    return changed;
                }
                value = value.WithTemporaryHitPointRevision(0);
            }

            return base.Set(key, value);
        }

        /// <summary>
        /// Removes existing health while retaining the last exact temporary-Hit-Point revision.
        /// </summary>
        public override bool Remove(CreatureId key)
        {
            if (!TryGet(key, out HealthState current))
                return false;
            EnsureWritableTemporaryHitPointRevisionTombstones();
            writableTemporaryHitPointRevisionTombstones[key] = current.TemporaryHitPointRevision;
            temporaryHitPointRevisionChanged = true;
            return base.Remove(key);
        }

        internal override bool IsDirty => base.IsDirty || temporaryHitPointRevisionChanged;

        internal Dictionary<CreatureId, long> BuildCommittedTemporaryHitPointRevisionTombstones() =>
            new Dictionary<CreatureId, long>(CurrentTemporaryHitPointRevisionTombstones);

        private Dictionary<CreatureId, long> CurrentTemporaryHitPointRevisionTombstones =>
            writableTemporaryHitPointRevisionTombstones
            ?? originalTemporaryHitPointRevisionTombstones;

        private void EnsureWritableTemporaryHitPointRevisionTombstones()
        {
            if (writableTemporaryHitPointRevisionTombstones == null)
            {
                writableTemporaryHitPointRevisionTombstones = new Dictionary<CreatureId, long>(
                    originalTemporaryHitPointRevisionTombstones
                );
            }
        }
    }
}
