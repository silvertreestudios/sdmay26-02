using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Stores the authoritative remaining uses for one spell-slot pool.
    /// </summary>
    public readonly struct SpellSlotState : IEquatable<SpellSlotState>
    {
        /// <summary>
        /// Initializes one immutable spell-slot pool state.
        /// </summary>
        /// <param name="id">The stable pool identity.</param>
        /// <param name="owner">The creature authorized to spend the pool.</param>
        /// <param name="remaining">The currently available uses.</param>
        /// <param name="maximum">The maximum uses represented by this pool.</param>
        public SpellSlotState(SpellSlotPoolId id, CreatureId owner, int remaining, int maximum)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A spell-slot pool ID is required.", nameof(id));
            if (owner.IsEmpty)
                throw new ArgumentException("A spell-slot owner is required.", nameof(owner));
            if (maximum < 0)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (remaining < 0 || remaining > maximum)
                throw new ArgumentOutOfRangeException(nameof(remaining));
            Id = id;
            Owner = owner;
            Remaining = remaining;
            Maximum = maximum;
        }

        /// <summary>
        /// Gets the stable pool identity.
        /// </summary>
        public SpellSlotPoolId Id { get; }

        /// <summary>
        /// Gets the creature authorized to spend the pool.
        /// </summary>
        public CreatureId Owner { get; }

        /// <summary>
        /// Gets the currently available uses.
        /// </summary>
        public int Remaining { get; }

        /// <summary>
        /// Gets the pool's maximum uses.
        /// </summary>
        public int Maximum { get; }

        /// <inheritdoc/>
        public bool Equals(SpellSlotState other) =>
            Id == other.Id
            && Owner == other.Owner
            && Remaining == other.Remaining
            && Maximum == other.Maximum;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellSlotState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Id, Owner, Remaining, Maximum);

        /// <summary>
        /// Compares two spell-slot states by value.
        /// </summary>
        public static bool operator ==(SpellSlotState left, SpellSlotState right) =>
            left.Equals(right);

        /// <summary>
        /// Compares two spell-slot states by value.
        /// </summary>
        public static bool operator !=(SpellSlotState left, SpellSlotState right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Stores one creature's authoritative Focus Point pool.
    /// </summary>
    public readonly struct FocusPointState : IEquatable<FocusPointState>
    {
        /// <summary>
        /// Initializes an immutable Focus Point pool.
        /// </summary>
        /// <param name="current">The currently available points.</param>
        /// <param name="maximum">The maximum points in the pool.</param>
        public FocusPointState(int current, int maximum)
        {
            if (maximum < 0)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (current < 0 || current > maximum)
                throw new ArgumentOutOfRangeException(nameof(current));
            Current = current;
            Maximum = maximum;
        }

        /// <summary>
        /// Gets the currently available Focus Points.
        /// </summary>
        public int Current { get; }

        /// <summary>
        /// Gets the pool's maximum Focus Points.
        /// </summary>
        public int Maximum { get; }

        /// <inheritdoc/>
        public bool Equals(FocusPointState other) =>
            Current == other.Current && Maximum == other.Maximum;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is FocusPointState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Current, Maximum);

        /// <summary>
        /// Compares two Focus Point states by value.
        /// </summary>
        public static bool operator ==(FocusPointState left, FocusPointState right) =>
            left.Equals(right);

        /// <summary>
        /// Compares two Focus Point states by value.
        /// </summary>
        public static bool operator !=(FocusPointState left, FocusPointState right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Stores authoritative ammunition ownership and remaining quantity.
    /// </summary>
    public readonly struct AmmunitionState : IEquatable<AmmunitionState>
    {
        /// <summary>
        /// Initializes one immutable ammunition pool.
        /// </summary>
        /// <param name="item">The stable item or ammunition-pool identity.</param>
        /// <param name="owner">The creature authorized to spend the ammunition.</param>
        /// <param name="remaining">The non-negative remaining quantity.</param>
        public AmmunitionState(ItemId item, CreatureId owner, int remaining)
        {
            if (item.IsEmpty)
                throw new ArgumentException("An ammunition item ID is required.", nameof(item));
            if (owner.IsEmpty)
                throw new ArgumentException("An ammunition owner is required.", nameof(owner));
            if (remaining < 0)
                throw new ArgumentOutOfRangeException(nameof(remaining));
            Item = item;
            Owner = owner;
            Remaining = remaining;
        }

        /// <summary>
        /// Gets the stable item or ammunition-pool identity.
        /// </summary>
        public ItemId Item { get; }

        /// <summary>
        /// Gets the creature authorized to spend the ammunition.
        /// </summary>
        public CreatureId Owner { get; }

        /// <summary>
        /// Gets the remaining quantity.
        /// </summary>
        public int Remaining { get; }

        /// <inheritdoc/>
        public bool Equals(AmmunitionState other) =>
            Item == other.Item && Owner == other.Owner && Remaining == other.Remaining;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is AmmunitionState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Item, Owner, Remaining);

        /// <summary>
        /// Compares two ammunition states by value.
        /// </summary>
        public static bool operator ==(AmmunitionState left, AmmunitionState right) =>
            left.Equals(right);

        /// <summary>
        /// Compares two ammunition states by value.
        /// </summary>
        public static bool operator !=(AmmunitionState left, AmmunitionState right) =>
            !left.Equals(right);
    }
}
