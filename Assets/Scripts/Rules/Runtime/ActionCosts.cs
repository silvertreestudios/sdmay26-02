using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Classifies the one action-economy cost paid by a PF2e action invocation.
    /// </summary>
    public enum ActionCostKind
    {
        /// <summary>
        /// The invocation does not participate in action-economy spending.
        /// </summary>
        None,

        /// <summary>
        /// The invocation spends one, two, or three actions.
        /// </summary>
        Actions,

        /// <summary>
        /// The invocation spends the actor's reaction for the current turn cycle.
        /// </summary>
        Reaction,

        /// <summary>
        /// The invocation is explicitly a free action and spends no action points.
        /// </summary>
        FreeAction,
    }

    /// <summary>
    /// Describes the PF2e action-economy portion of an action profile.
    /// </summary>
    /// <remarks>
    /// This value deliberately distinguishes a free action from an operation with no action cost.
    /// Both spend zero action points, but the distinction remains useful to rules that refer to the
    /// type of action being taken. Additional consumables belong in <see cref="RuleCost"/> values.
    /// </remarks>
    public readonly struct ActionCost : IEquatable<ActionCost>
    {
        private ActionCost(ActionCostKind kind, int amount)
        {
            Kind = kind;
            Amount = amount;
        }

        /// <summary>
        /// Gets an action cost for an operation outside the PF2e action economy.
        /// </summary>
        public static ActionCost None { get; } = new ActionCost(ActionCostKind.None, 0);

        /// <summary>
        /// Gets a one-action cost.
        /// </summary>
        public static ActionCost One { get; } = new ActionCost(ActionCostKind.Actions, 1);

        /// <summary>
        /// Gets a two-action cost.
        /// </summary>
        public static ActionCost Two { get; } = new ActionCost(ActionCostKind.Actions, 2);

        /// <summary>
        /// Gets a three-action cost.
        /// </summary>
        public static ActionCost Three { get; } = new ActionCost(ActionCostKind.Actions, 3);

        /// <summary>
        /// Gets a reaction cost.
        /// </summary>
        public static ActionCost Reaction { get; } = new ActionCost(ActionCostKind.Reaction, 1);

        /// <summary>
        /// Gets a free-action cost.
        /// </summary>
        public static ActionCost FreeAction { get; } = new ActionCost(ActionCostKind.FreeAction, 1);

        /// <summary>
        /// Gets the semantic kind of action cost.
        /// </summary>
        public ActionCostKind Kind { get; }

        /// <summary>
        /// Gets the number of units represented by <see cref="Kind"/>.
        /// </summary>
        /// <remarks>
        /// Ordinary actions use one to three units. A reaction and a free action each use one unit
        /// of their own kind, although taking a free action does not consume a tracked resource.
        /// <see cref="ActionCostKind.None"/> is the only zero-unit cost.
        /// </remarks>
        public int Amount { get; }

        /// <summary>
        /// Returns the canonical cost for one, two, or three actions.
        /// </summary>
        /// <param name="actionCount">The number of actions to spend.</param>
        /// <returns>The matching canonical action cost.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="actionCount"/> is outside the supported one-to-three range.
        /// </exception>
        public static ActionCost FromActions(int actionCount)
        {
            switch (actionCount)
            {
                case 1:
                    return One;
                case 2:
                    return Two;
                case 3:
                    return Three;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(actionCount),
                        "An action cost must spend between one and three actions."
                    );
            }
        }

        /// <inheritdoc/>
        public bool Equals(ActionCost other) => Kind == other.Kind && Amount == other.Amount;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ActionCost other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Kind, Amount);

        /// <summary>
        /// Compares two action costs by kind and amount.
        /// </summary>
        public static bool operator ==(ActionCost left, ActionCost right) => left.Equals(right);

        /// <summary>
        /// Compares two action costs by kind and amount.
        /// </summary>
        public static bool operator !=(ActionCost left, ActionCost right) => !left.Equals(right);
    }

    /// <summary>
    /// Represents one immutable, non-action-economy resource cost.
    /// </summary>
    /// <remarks>
    /// Use the factory methods to create the exact resource cost required by an action profile.
    /// Each sealed cost type exposes only the state needed to spend that resource.
    /// </remarks>
    public abstract class RuleCost : IEquatable<RuleCost>
    {
        private protected RuleCost() { }

        /// <summary>
        /// Creates a cost that spends uses from one spell-slot pool.
        /// </summary>
        /// <param name="pool">The authoritative pool to spend.</param>
        /// <param name="amount">The positive number of uses to spend.</param>
        /// <returns>An immutable spell-slot cost.</returns>
        public static SpellSlotRuleCost SpellSlot(SpellSlotPoolId pool, int amount = 1) =>
            new SpellSlotRuleCost(pool, amount);

        /// <summary>
        /// Creates a cost that spends the acting creature's Focus Points.
        /// </summary>
        /// <param name="amount">The positive number of Focus Points to spend.</param>
        /// <returns>An immutable Focus Point cost.</returns>
        public static FocusPointRuleCost FocusPoints(int amount = 1) =>
            new FocusPointRuleCost(amount);

        /// <summary>
        /// Creates a cost that spends ammunition owned by the acting creature.
        /// </summary>
        /// <param name="item">The stable ammunition item or pool identity.</param>
        /// <param name="amount">The positive amount of ammunition to spend.</param>
        /// <returns>An immutable ammunition cost.</returns>
        public static AmmunitionRuleCost Ammunition(ItemId item, int amount = 1) =>
            new AmmunitionRuleCost(item, amount);

        /// <summary>
        /// Creates a cost that spends one use from an active binding's once-per-round frequency.
        /// </summary>
        /// <param name="binding">The active binding authorized to spend the frequency.</param>
        /// <returns>An immutable once-per-round cost.</returns>
        public static OncePerRoundRuleCost OncePerRound(BindingId binding) =>
            new OncePerRoundRuleCost(binding);

        /// <inheritdoc/>
        public abstract bool Equals(RuleCost other);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RuleCost other && Equals(other);

        /// <inheritdoc/>
        public abstract override int GetHashCode();
    }

    /// <summary>
    /// Spends a positive number of uses from one spell-slot pool.
    /// </summary>
    public sealed class SpellSlotRuleCost : RuleCost
    {
        internal SpellSlotRuleCost(SpellSlotPoolId pool, int amount)
        {
            if (pool.IsEmpty)
                throw new ArgumentException("A spell-slot pool ID is required.", nameof(pool));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Pool = pool;
            Amount = amount;
        }

        /// <summary>
        /// Gets the pool whose available uses will be reduced.
        /// </summary>
        public SpellSlotPoolId Pool { get; }

        /// <summary>
        /// Gets the positive number of uses to spend.
        /// </summary>
        public int Amount { get; }

        /// <inheritdoc/>
        public override bool Equals(RuleCost other) =>
            other is SpellSlotRuleCost cost && Pool == cost.Pool && Amount == cost.Amount;

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Pool, Amount);
    }

    /// <summary>
    /// Spends Focus Points from the creature taking the action.
    /// </summary>
    public sealed class FocusPointRuleCost : RuleCost
    {
        internal FocusPointRuleCost(int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Amount = amount;
        }

        /// <summary>
        /// Gets the positive number of Focus Points to spend.
        /// </summary>
        public int Amount { get; }

        /// <inheritdoc/>
        public override bool Equals(RuleCost other) =>
            other is FocusPointRuleCost cost && Amount == cost.Amount;

        /// <inheritdoc/>
        public override int GetHashCode() => Amount;
    }

    /// <summary>
    /// Spends ammunition from one item or ammunition pool owned by the actor.
    /// </summary>
    public sealed class AmmunitionRuleCost : RuleCost
    {
        internal AmmunitionRuleCost(ItemId item, int amount)
        {
            if (item.IsEmpty)
                throw new ArgumentException("An ammunition item ID is required.", nameof(item));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Item = item;
            Amount = amount;
        }

        /// <summary>
        /// Gets the ammunition item or pool to spend.
        /// </summary>
        public ItemId Item { get; }

        /// <summary>
        /// Gets the positive amount of ammunition to spend.
        /// </summary>
        public int Amount { get; }

        /// <inheritdoc/>
        public override bool Equals(RuleCost other) =>
            other is AmmunitionRuleCost cost && Item == cost.Item && Amount == cost.Amount;

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Item, Amount);
    }

    /// <summary>
    /// Spends the current round's one use for an active rule binding.
    /// </summary>
    public sealed class OncePerRoundRuleCost : RuleCost
    {
        internal OncePerRoundRuleCost(BindingId binding)
        {
            if (binding.IsEmpty)
                throw new ArgumentException("A binding ID is required.", nameof(binding));
            Binding = binding;
        }

        /// <summary>
        /// Gets the active binding whose frequency use will be spent.
        /// </summary>
        public BindingId Binding { get; }

        /// <inheritdoc/>
        public override bool Equals(RuleCost other) =>
            other is OncePerRoundRuleCost cost && Binding == cost.Binding;

        /// <inheritdoc/>
        public override int GetHashCode() => Binding.GetHashCode();
    }
}
