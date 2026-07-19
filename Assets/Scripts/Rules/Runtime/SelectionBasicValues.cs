using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Contains one selected creature stable ID.</summary>
    public readonly struct CreatureSelection : IEquatable<CreatureSelection>
    {
        /// <summary>Gets the selected creature.</summary>
        public CreatureId Creature { get; }

        /// <summary>Creates a creature selection.</summary>
        /// <param name="creature">The selected creature.</param>
        public CreatureSelection(CreatureId creature)
        {
            if (creature.IsEmpty)
                throw new ArgumentException("A selected creature is required.", nameof(creature));
            Creature = creature;
        }

        /// <inheritdoc/>
        public bool Equals(CreatureSelection other) => Creature.Equals(other.Creature);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is CreatureSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Creature.GetHashCode();
    }

    /// <summary>Contains an ordered, non-empty set of distinct selected creatures.</summary>
    public sealed class MultipleCreatureSelection : IEquatable<MultipleCreatureSelection>
    {
        private readonly IReadOnlyList<CreatureId> creatures;

        /// <summary>Gets selected creatures in the order chosen by the caller.</summary>
        public IReadOnlyList<CreatureId> Creatures => creatures;

        /// <summary>Creates an immutable multiple-creature selection.</summary>
        /// <param name="creatures">Distinct, non-empty creature IDs in selection order.</param>
        public MultipleCreatureSelection(IEnumerable<CreatureId> creatures) =>
            this.creatures = SelectionCollections.CopyDistinct(
                creatures,
                nameof(creatures),
                creature => creature.IsEmpty,
                "Selected creatures cannot contain an empty ID."
            );

        /// <inheritdoc/>
        public bool Equals(MultipleCreatureSelection other) =>
            other != null && creatures.SequenceEqual(other.creatures);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is MultipleCreatureSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => SelectionCollections.OrderedHashCode(creatures);
    }

    /// <summary>Contains one selected item stable ID.</summary>
    public readonly struct ItemSelection : IEquatable<ItemSelection>
    {
        /// <summary>Gets the selected item.</summary>
        public ItemId Item { get; }

        /// <summary>Creates an item selection.</summary>
        /// <param name="item">The selected item.</param>
        public ItemSelection(ItemId item)
        {
            if (item.IsEmpty)
                throw new ArgumentException("A selected item is required.", nameof(item));
            Item = item;
        }

        /// <inheritdoc/>
        public bool Equals(ItemSelection other) => Item.Equals(other.Item);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ItemSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Item.GetHashCode();
    }

    /// <summary>Contains an item ID selected specifically for use as a weapon.</summary>
    public readonly struct WeaponSelection : IEquatable<WeaponSelection>
    {
        /// <summary>Gets the selected weapon item.</summary>
        public ItemId Weapon { get; }

        /// <summary>Creates a weapon selection.</summary>
        /// <param name="weapon">The selected weapon item.</param>
        public WeaponSelection(ItemId weapon)
        {
            if (weapon.IsEmpty)
                throw new ArgumentException("A selected weapon is required.", nameof(weapon));
            Weapon = weapon;
        }

        /// <inheritdoc/>
        public bool Equals(WeaponSelection other) => Weapon.Equals(other.Weapon);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is WeaponSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Weapon.GetHashCode();
    }

    internal static class SelectionCollections
    {
        public static IReadOnlyList<T> CopyDistinct<T>(
            IEnumerable<T> values,
            string parameterName,
            Func<T, bool> isAbsent,
            string absentMessage
        )
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);
            T[] copied = values.ToArray();
            if (copied.Length == 0)
                throw new ArgumentException("At least one value is required.", parameterName);

            HashSet<T> unique = new HashSet<T>();
            foreach (T value in copied)
            {
                if (ReferenceEquals(value, null) || isAbsent(value))
                    throw new ArgumentException(absentMessage, parameterName);
                if (!unique.Add(value))
                    throw new ArgumentException("Duplicate values are not allowed.", parameterName);
            }
            return Array.AsReadOnly(copied);
        }

        public static int OrderedHashCode<T>(IEnumerable<T> values)
        {
            unchecked
            {
                int hash = 17;
                foreach (T value in values)
                    hash = (hash * 31) + EqualityComparer<T>.Default.GetHashCode(value);
                return hash;
            }
        }
    }
}
