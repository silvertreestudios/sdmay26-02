using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    /// <summary>Contains the exact weapon and creature selected for one Strike.</summary>
    public sealed class StrikeSelection : IEquatable<StrikeSelection>
    {
        /// <summary>Gets the selected weapon item.</summary>
        public ItemId Weapon { get; }

        /// <summary>Gets the selected target creature.</summary>
        public CreatureId Target { get; }

        /// <summary>Creates a complete Strike selection.</summary>
        /// <param name="weapon">The non-empty weapon item ID.</param>
        /// <param name="target">The non-empty target creature ID.</param>
        public StrikeSelection(ItemId weapon, CreatureId target)
        {
            if (weapon.IsEmpty)
                throw new ArgumentException("A Strike weapon is required.", nameof(weapon));
            if (target.IsEmpty)
                throw new ArgumentException("A Strike target is required.", nameof(target));
            Weapon = weapon;
            Target = target;
        }

        /// <inheritdoc/>
        public bool Equals(StrikeSelection other) =>
            other != null && Weapon.Equals(other.Weapon) && Target.Equals(other.Target);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is StrikeSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Weapon, Target);
    }

    /// <summary>Contains the path, crossed enemy, and movement mode for Tumble Through.</summary>
    public sealed class TumbleThroughSelection : IEquatable<TumbleThroughSelection>
    {
        private readonly PathSelection path;

        /// <summary>Gets the immutable path from starting cell through destination.</summary>
        public IReadOnlyList<GridPosition> Path => path.Positions;

        /// <summary>Gets the enemy whose space is crossed.</summary>
        public CreatureId Enemy { get; }

        /// <summary>Gets the movement mode used along the path.</summary>
        public MovementMode Mode { get; }

        /// <summary>Creates a complete Tumble Through selection.</summary>
        /// <param name="path">At least a starting cell and destination.</param>
        /// <param name="enemy">The non-empty crossed enemy ID.</param>
        /// <param name="mode">The non-empty data-defined movement mode.</param>
        public TumbleThroughSelection(
            IEnumerable<GridPosition> path,
            CreatureId enemy,
            MovementMode mode
        )
        {
            this.path = new PathSelection(path);
            if (this.path.Positions.Count < 2)
                throw new ArgumentException(
                    "Tumble Through requires movement to a destination.",
                    nameof(path)
                );
            if (enemy.IsEmpty)
                throw new ArgumentException("A crossed enemy is required.", nameof(enemy));
            if (mode.IsEmpty)
                throw new ArgumentException("A movement mode is required.", nameof(mode));
            Enemy = enemy;
            Mode = mode;
        }

        /// <inheritdoc/>
        public bool Equals(TumbleThroughSelection other) =>
            other != null
            && path.Equals(other.path)
            && Enemy.Equals(other.Enemy)
            && Mode.Equals(other.Mode);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is TumbleThroughSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(path, Enemy, Mode);
    }
}
