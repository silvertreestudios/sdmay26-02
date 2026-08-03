using System;
using System.Text;

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

    /// <summary>Identifies one authoritative encounter within a rules store.</summary>
    public readonly struct EncounterId : IEquatable<EncounterId>
    {
        /// <summary>Gets the stable serialized value.</summary>
        public string Value { get; }

        /// <summary>Gets whether this value is unallocated.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>Initializes a non-empty encounter identifier.</summary>
        /// <param name="value">The stable encounter value.</param>
        public EncounterId(string value) => Value = StableId.Require(value, nameof(value));

        /// <inheritdoc/>
        public bool Equals(EncounterId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EncounterId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>Compares two encounter identifiers by stable value.</summary>
        public static bool operator ==(EncounterId left, EncounterId right) => left.Equals(right);

        /// <summary>Compares two encounter identifiers by stable value.</summary>
        public static bool operator !=(EncounterId left, EncounterId right) => !left.Equals(right);
    }

    /// <summary>Identifies one exact turn within an encounter.</summary>
    public readonly struct TurnId : IEquatable<TurnId>, IComparable<TurnId>
    {
        /// <summary>Gets the positive encounter-local sequence.</summary>
        public long Value { get; }

        /// <summary>Gets whether this value is unallocated.</summary>
        public bool IsEmpty => Value == 0;

        /// <summary>Initializes a positive turn identifier.</summary>
        /// <param name="value">The positive encounter-local sequence.</param>
        public TurnId(long value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        /// <inheritdoc/>
        public int CompareTo(TurnId other) => Value.CompareTo(other.Value);

        /// <inheritdoc/>
        public bool Equals(TurnId other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is TurnId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => Value.ToString();

        /// <summary>Compares two turn identifiers by encounter-local sequence.</summary>
        public static bool operator ==(TurnId left, TurnId right) => left.Equals(right);

        /// <summary>Compares two turn identifiers by encounter-local sequence.</summary>
        public static bool operator !=(TurnId left, TurnId right) => !left.Equals(right);
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

    /// <summary>
    /// Provides a collision-safe stable namespace for identities generated from authoritative
    /// creature state.
    /// </summary>
    /// <remarks>
    /// Namespace values are restricted to an ASCII token so they can be embedded in generated
    /// stable IDs without escaping or delimiter ambiguity. Persistable adapters should supply a
    /// namespace derived from the actor's durable identity. Pure-rules and intentionally
    /// nondurable callers can use <see cref="ForCreature"/>.
    /// </remarks>
    public readonly struct GeneratedIdentityNamespace : IEquatable<GeneratedIdentityNamespace>
    {
        private const string CreaturePrefix = "creature-identity-v1-";

        /// <summary>Gets the safe stable token used inside generated identities.</summary>
        public string Value { get; }

        /// <summary>Gets whether this value is the uninitialized namespace.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>Initializes a nonempty ASCII namespace token.</summary>
        /// <param name="value">
        /// A canonical token containing only ASCII letters, digits, hyphens, or underscores.
        /// </param>
        public GeneratedIdentityNamespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "A generated identity namespace cannot be blank.",
                    nameof(value)
                );
            foreach (char character in value)
            {
                if (
                    !(character >= 'A' && character <= 'Z')
                    && !(character >= 'a' && character <= 'z')
                    && !(character >= '0' && character <= '9')
                    && character != '-'
                    && character != '_'
                )
                    throw new ArgumentException(
                        "A generated identity namespace must be a canonical ASCII token.",
                        nameof(value)
                    );
            }
            Value = value;
        }

        /// <summary>
        /// Creates the deterministic namespace used by pure-rules or intentionally nondurable
        /// creature state.
        /// </summary>
        /// <param name="creature">The store-local creature identity to encode reversibly.</param>
        /// <returns>A safe namespace distinct from adapter-owned durable namespace formats.</returns>
        public static GeneratedIdentityNamespace ForCreature(CreatureId creature)
        {
            if (creature.IsEmpty)
                throw new ArgumentException("A creature ID is required.", nameof(creature));
            try
            {
                string payload = Convert
                    .ToBase64String(new UTF8Encoding(false, true).GetBytes(creature.Value))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                return new GeneratedIdentityNamespace(CreaturePrefix + payload);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "A creature ID must contain valid Unicode.",
                    nameof(creature),
                    exception
                );
            }
        }

        /// <inheritdoc/>
        public bool Equals(GeneratedIdentityNamespace other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is GeneratedIdentityNamespace other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>Compares two generated identity namespaces by stable value.</summary>
        public static bool operator ==(
            GeneratedIdentityNamespace left,
            GeneratedIdentityNamespace right
        ) => left.Equals(right);

        /// <summary>Compares two generated identity namespaces by stable value.</summary>
        public static bool operator !=(
            GeneratedIdentityNamespace left,
            GeneratedIdentityNamespace right
        ) => !left.Equals(right);
    }

    /// <summary>
    /// Identifies one Unity-originated health request within an encounter.
    /// </summary>
    /// <remarks>
    /// The encounter composition root allocates this stable value independently of object names
    /// and Unity instance IDs so Facts remain deterministic and Unity-free.
    /// </remarks>
    public readonly struct HealthChangeOriginId : IEquatable<HealthChangeOriginId>
    {
        /// <summary>Gets the stable serialized value.</summary>
        public string Value { get; }

        /// <summary>Gets whether this value is the default, unallocated identifier.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>Initializes an allocated health-change origin identifier.</summary>
        /// <param name="value">A non-empty encounter-stable value.</param>
        public HealthChangeOriginId(string value) => Value = StableId.Require(value, nameof(value));

        /// <inheritdoc/>
        public bool Equals(HealthChangeOriginId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is HealthChangeOriginId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(HealthChangeOriginId left, HealthChangeOriginId right) =>
            left.Equals(right);

        public static bool operator !=(HealthChangeOriginId left, HealthChangeOriginId right) =>
            !left.Equals(right);
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
