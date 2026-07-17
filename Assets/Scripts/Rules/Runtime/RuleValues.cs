using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// An open, data-backed PF2e trait slug. New data does not require an enum change.
    /// </summary>
    public readonly struct Trait : IEquatable<Trait>
    {
        public string Slug { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Slug);

        private Trait(string slug)
        {
            Slug = StableId.Require(slug, nameof(slug));
        }

        public static Trait FromName(string value) => FromSlug(Pf2eSlug.FromName(value));
        public static Trait FromSlug(string value) => new Trait(Pf2eSlug.FromName(value));
        public bool Equals(Trait other) => string.Equals(Slug, other.Slug, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is Trait other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Slug ?? string.Empty);
        public override string ToString() => Slug ?? string.Empty;
        public static bool operator ==(Trait left, Trait right) => left.Equals(right);
        public static bool operator !=(Trait left, Trait right) => !left.Equals(right);
    }

    /// <summary>
    /// Identifies a rule's data source without a closed source-kind enum.
    /// </summary>
    public readonly struct RuleSource : IEquatable<RuleSource>
    {
        public string Slug { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Slug);

        private RuleSource(string slug)
        {
            Slug = StableId.Require(slug, nameof(slug));
        }

        public static RuleSource FromName(string value) => FromSlug(Pf2eSlug.FromName(value));
        public static RuleSource FromSlug(string value) => new RuleSource(Pf2eSlug.FromName(value));
        public bool Equals(RuleSource other) => string.Equals(Slug, other.Slug, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RuleSource other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Slug ?? string.Empty);
        public override string ToString() => Slug ?? string.Empty;
        public static bool operator ==(RuleSource left, RuleSource right) => left.Equals(right);
        public static bool operator !=(RuleSource left, RuleSource right) => !left.Equals(right);
    }

    public readonly struct RuleValueKey : IEquatable<RuleValueKey>
    {
        public string Slug { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Slug);

        private RuleValueKey(string slug)
        {
            Slug = StableId.Require(slug, nameof(slug));
        }

        public static RuleValueKey FromName(string value) => new RuleValueKey(Pf2eSlug.FromName(value));
        public bool Equals(RuleValueKey other) => string.Equals(Slug, other.Slug, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RuleValueKey other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Slug ?? string.Empty);
        public override string ToString() => Slug ?? string.Empty;
        public static bool operator ==(RuleValueKey left, RuleValueKey right) => left.Equals(right);
        public static bool operator !=(RuleValueKey left, RuleValueKey right) => !left.Equals(right);
    }

    /// <summary>
    /// An immutable scalar value used for data-backed rule state during incremental seeding.
    /// </summary>
    public readonly struct RuleValue : IEquatable<RuleValue>
    {
        public string Value { get; }

        public RuleValue(string value)
        {
            Value = value ?? string.Empty;
        }

        public static RuleValue FromInt(int value) => new RuleValue(value.ToString(CultureInfo.InvariantCulture));
        public static RuleValue FromBool(bool value) => new RuleValue(value ? "true" : "false");
        public bool TryGetInt(out int value) => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        public bool TryGetBool(out bool value) => bool.TryParse(Value, out value);
        public bool Equals(RuleValue other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RuleValue other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(RuleValue left, RuleValue right) => left.Equals(right);
        public static bool operator !=(RuleValue left, RuleValue right) => !left.Equals(right);
    }

    /// <summary>
    /// Defensively copied property data suitable for Ops, state seeds, and snapshots.
    /// </summary>
    public sealed class RuleValueMap : IReadOnlyDictionary<RuleValueKey, RuleValue>, IEquatable<RuleValueMap>
    {
        private readonly IReadOnlyDictionary<RuleValueKey, RuleValue> values;

        public static RuleValueMap Empty { get; } = new RuleValueMap(Array.Empty<KeyValuePair<RuleValueKey, RuleValue>>());

        public RuleValueMap(IEnumerable<KeyValuePair<RuleValueKey, RuleValue>> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            this.values = new ReadOnlyDictionary<RuleValueKey, RuleValue>(
                values.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        public RuleValue this[RuleValueKey key] => values[key];
        public IEnumerable<RuleValueKey> Keys => values.Keys;
        public IEnumerable<RuleValue> Values => values.Values;
        public int Count => values.Count;
        public bool ContainsKey(RuleValueKey key) => values.ContainsKey(key);
        public bool TryGetValue(RuleValueKey key, out RuleValue value) => values.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<RuleValueKey, RuleValue>> GetEnumerator() => values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(RuleValueMap other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null || Count != other.Count)
                return false;

            return values.All(pair => other.TryGetValue(pair.Key, out RuleValue value) && value == pair.Value);
        }

        public override bool Equals(object obj) => obj is RuleValueMap other && Equals(other);

        public override int GetHashCode()
        {
            int hash = 17;
            foreach (KeyValuePair<RuleValueKey, RuleValue> pair in values.OrderBy(pair => pair.Key.Slug, StringComparer.Ordinal))
            {
                hash = (hash * 31) + pair.Key.GetHashCode();
                hash = (hash * 31) + pair.Value.GetHashCode();
            }
            return hash;
        }
    }

    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public GridPosition(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(GridPosition other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is GridPosition other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
        public static bool operator ==(GridPosition left, GridPosition right) => left.Equals(right);
        public static bool operator !=(GridPosition left, GridPosition right) => !left.Equals(right);
    }

    public readonly struct GridDistance : IEquatable<GridDistance>, IComparable<GridDistance>
    {
        public int Feet { get; }

        public GridDistance(int feet)
        {
            if (feet < 0)
                throw new ArgumentOutOfRangeException(nameof(feet));
            Feet = feet;
        }

        public int CompareTo(GridDistance other) => Feet.CompareTo(other.Feet);
        public bool Equals(GridDistance other) => Feet == other.Feet;
        public override bool Equals(object obj) => obj is GridDistance other && Equals(other);
        public override int GetHashCode() => Feet;
        public override string ToString() => $"{Feet} feet";
        public static bool operator ==(GridDistance left, GridDistance right) => left.Equals(right);
        public static bool operator !=(GridDistance left, GridDistance right) => !left.Equals(right);
    }
}
