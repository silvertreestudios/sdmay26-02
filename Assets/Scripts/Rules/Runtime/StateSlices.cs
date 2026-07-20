using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Rules.Runtime
{
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

    public sealed class StateSliceDraft<TKey, TValue>
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

        internal bool IsDirty
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

        public bool Set(TKey key, TValue value)
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

        public bool Remove(TKey key)
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
}
