using System;

namespace Game.Rules.Runtime
{
    internal static class StableId
    {
        public static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A stable ID cannot be blank.", parameterName);

            return value.Trim();
        }
    }

    public readonly struct CreatureId : IEquatable<CreatureId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public CreatureId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(CreatureId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is CreatureId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(CreatureId left, CreatureId right) => left.Equals(right);

        public static bool operator !=(CreatureId left, CreatureId right) => !left.Equals(right);
    }

    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public PlayerId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(PlayerId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);

        public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);
    }

    public readonly struct ItemId : IEquatable<ItemId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public ItemId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(ItemId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ItemId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ItemId left, ItemId right) => left.Equals(right);

        public static bool operator !=(ItemId left, ItemId right) => !left.Equals(right);
    }

    public readonly struct BindingId : IEquatable<BindingId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public BindingId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(BindingId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is BindingId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(BindingId left, BindingId right) => left.Equals(right);

        public static bool operator !=(BindingId left, BindingId right) => !left.Equals(right);
    }

    public readonly struct ActiveEffectId : IEquatable<ActiveEffectId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public ActiveEffectId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(ActiveEffectId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ActiveEffectId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ActiveEffectId left, ActiveEffectId right) =>
            left.Equals(right);

        public static bool operator !=(ActiveEffectId left, ActiveEffectId right) =>
            !left.Equals(right);
    }

    public readonly struct ConditionId : IEquatable<ConditionId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public ConditionId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(ConditionId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ConditionId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ConditionId left, ConditionId right) => left.Equals(right);

        public static bool operator !=(ConditionId left, ConditionId right) => !left.Equals(right);
    }

    public readonly struct ActionDefinitionId : IEquatable<ActionDefinitionId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public ActionDefinitionId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(ActionDefinitionId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ActionDefinitionId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ActionDefinitionId left, ActionDefinitionId right) =>
            left.Equals(right);

        public static bool operator !=(ActionDefinitionId left, ActionDefinitionId right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Identifies one authoritative spell-slot pool in rules state.
    /// </summary>
    /// <remarks>
    /// A pool can represent a prepared slot, spontaneous repertoire rank, focus-like spell
    /// allocation, or another data-defined source. The pool's behavior belongs to its definition;
    /// this value supplies only stable identity for costs and state lookup.
    /// </remarks>
    public readonly struct SpellSlotPoolId : IEquatable<SpellSlotPoolId>
    {
        /// <summary>
        /// Gets the stable, non-empty pool identifier.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Gets whether this value is the uninitialized default identifier.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>
        /// Initializes a spell-slot pool identifier.
        /// </summary>
        /// <param name="value">The stable identifier text.</param>
        public SpellSlotPoolId(string value) => Value = StableId.Require(value, nameof(value));

        /// <inheritdoc/>
        public bool Equals(SpellSlotPoolId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellSlotPoolId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>
        /// Compares two pool identifiers by stable value.
        /// </summary>
        public static bool operator ==(SpellSlotPoolId left, SpellSlotPoolId right) =>
            left.Equals(right);

        /// <summary>
        /// Compares two pool identifiers by stable value.
        /// </summary>
        public static bool operator !=(SpellSlotPoolId left, SpellSlotPoolId right) =>
            !left.Equals(right);
    }

    public readonly struct RuleDefinitionId : IEquatable<RuleDefinitionId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public RuleDefinitionId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(RuleDefinitionId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is RuleDefinitionId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(RuleDefinitionId left, RuleDefinitionId right) =>
            left.Equals(right);

        public static bool operator !=(RuleDefinitionId left, RuleDefinitionId right) =>
            !left.Equals(right);
    }

    public readonly struct ItemDefinitionId : IEquatable<ItemDefinitionId>
    {
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public ItemDefinitionId(string value) => Value = StableId.Require(value, nameof(value));

        public bool Equals(ItemDefinitionId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ItemDefinitionId other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ItemDefinitionId left, ItemDefinitionId right) =>
            left.Equals(right);

        public static bool operator !=(ItemDefinitionId left, ItemDefinitionId right) =>
            !left.Equals(right);
    }

    public readonly struct OpId : IEquatable<OpId>, IComparable<OpId>
    {
        public long Value { get; }
        public bool IsEmpty => Value == 0;

        public OpId(long value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "An Op ID must be positive.");
            Value = value;
        }

        public int CompareTo(OpId other) => Value.CompareTo(other.Value);

        public bool Equals(OpId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is OpId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(OpId left, OpId right) => left.Equals(right);

        public static bool operator !=(OpId left, OpId right) => !left.Equals(right);
    }

    public readonly struct FactId : IEquatable<FactId>, IComparable<FactId>
    {
        public long Value { get; }
        public bool IsEmpty => Value == 0;

        public FactId(long value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "A Fact ID must be positive.");
            Value = value;
        }

        public int CompareTo(FactId other) => Value.CompareTo(other.Value);

        public bool Equals(FactId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is FactId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(FactId left, FactId right) => left.Equals(right);

        public static bool operator !=(FactId left, FactId right) => !left.Equals(right);
    }
}
