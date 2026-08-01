using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Proves that an action or reaction resource was spent for one action invocation.
    /// </summary>
    public sealed class ActionCostSpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame whose cost was paid.</param>
        /// <param name="actor">The creature whose action economy changed.</param>
        /// <param name="cost">The actions or reaction that was spent.</param>
        public ActionCostSpentFact(OpId actionOpId, CreatureId actor, ActionCost cost)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (cost.Kind != ActionCostKind.Actions && cost.Kind != ActionCostKind.Reaction)
            {
                throw new ArgumentException(
                    "Only actions or a reaction produce an action-cost spending Fact.",
                    nameof(cost)
                );
            }
            ActionOpId = actionOpId;
            Actor = actor;
            Cost = cost;
        }

        /// <summary>
        /// Gets the action frame whose cost was paid.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature whose action economy changed.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the exact action or reaction cost that committed.
        /// </summary>
        public ActionCost Cost { get; }
    }

    /// <summary>
    /// Proves that uses were spent from one spell-slot pool.
    /// </summary>
    public sealed class SpellSlotSpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame that paid the slot cost.</param>
        /// <param name="actor">The creature that owns the pool.</param>
        /// <param name="pool">The pool that changed.</param>
        /// <param name="amount">The positive number of uses spent.</param>
        /// <param name="remaining">The pool's remaining uses after the commit.</param>
        public SpellSlotSpentFact(
            OpId actionOpId,
            CreatureId actor,
            SpellSlotPoolId pool,
            int amount,
            int remaining
        )
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (pool.IsEmpty)
                throw new ArgumentException("A spell-slot pool ID is required.", nameof(pool));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (remaining < 0)
                throw new ArgumentOutOfRangeException(nameof(remaining));
            ActionOpId = actionOpId;
            Actor = actor;
            Pool = pool;
            Amount = amount;
            Remaining = remaining;
        }

        /// <summary>
        /// Gets the action frame that paid the slot cost.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature that owns the pool.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the pool that changed.
        /// </summary>
        public SpellSlotPoolId Pool { get; }

        /// <summary>
        /// Gets the number of uses spent.
        /// </summary>
        public int Amount { get; }

        /// <summary>
        /// Gets the remaining uses after the commit.
        /// </summary>
        public int Remaining { get; }
    }

    /// <summary>
    /// Proves that the acting creature spent Focus Points.
    /// </summary>
    public sealed class FocusPointsSpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame that paid the Focus Point cost.</param>
        /// <param name="actor">The creature whose Focus Points changed.</param>
        /// <param name="amount">The positive number of points spent.</param>
        /// <param name="remaining">The remaining points after the commit.</param>
        public FocusPointsSpentFact(OpId actionOpId, CreatureId actor, int amount, int remaining)
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (remaining < 0)
                throw new ArgumentOutOfRangeException(nameof(remaining));
            ActionOpId = actionOpId;
            Actor = actor;
            Amount = amount;
            Remaining = remaining;
        }

        /// <summary>
        /// Gets the action frame that paid the Focus Point cost.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature whose Focus Points changed.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the number of points spent.
        /// </summary>
        public int Amount { get; }

        /// <summary>
        /// Gets the remaining points after the commit.
        /// </summary>
        public int Remaining { get; }
    }

    /// <summary>
    /// Proves that ammunition was spent for one action invocation.
    /// </summary>
    public sealed class AmmunitionSpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame that paid the ammunition cost.</param>
        /// <param name="actor">The creature that owns the ammunition.</param>
        /// <param name="item">The ammunition item or pool that changed.</param>
        /// <param name="amount">The positive amount spent.</param>
        /// <param name="remaining">The remaining ammunition after the commit.</param>
        public AmmunitionSpentFact(
            OpId actionOpId,
            CreatureId actor,
            ItemId item,
            int amount,
            int remaining
        )
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (item.IsEmpty)
                throw new ArgumentException("An ammunition item ID is required.", nameof(item));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (remaining < 0)
                throw new ArgumentOutOfRangeException(nameof(remaining));
            ActionOpId = actionOpId;
            Actor = actor;
            Item = item;
            Amount = amount;
            Remaining = remaining;
        }

        /// <summary>
        /// Gets the action frame that paid the ammunition cost.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature that owns the ammunition.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the ammunition item or pool that changed.
        /// </summary>
        public ItemId Item { get; }

        /// <summary>
        /// Gets the amount spent.
        /// </summary>
        public int Amount { get; }

        /// <summary>
        /// Gets the remaining ammunition after the commit.
        /// </summary>
        public int Remaining { get; }
    }

    /// <summary>
    /// Proves that an active binding spent its once-per-round use.
    /// </summary>
    public sealed class BindingFrequencySpentFact : RuleFact
    {
        /// <summary>
        /// Initializes the immutable domain payload. The store stamps identity and provenance.
        /// </summary>
        /// <param name="actionOpId">The action frame that spent the frequency.</param>
        /// <param name="actor">The creature authorized by the binding.</param>
        /// <param name="binding">The active binding whose use changed.</param>
        /// <param name="round">The round marker retained by frequency state.</param>
        /// <param name="uses">The number of uses recorded after the commit.</param>
        public BindingFrequencySpentFact(
            OpId actionOpId,
            CreatureId actor,
            BindingId binding,
            EncounterId encounter,
            int round,
            int uses
        )
        {
            if (actionOpId.IsEmpty)
                throw new ArgumentException("An action Op ID is required.", nameof(actionOpId));
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (binding.IsEmpty)
                throw new ArgumentException("A binding ID is required.", nameof(binding));
            if (encounter.IsEmpty)
                throw new ArgumentException("An encounter ID is required.", nameof(encounter));
            if (round < 0)
                throw new ArgumentOutOfRangeException(nameof(round));
            if (uses <= 0)
                throw new ArgumentOutOfRangeException(nameof(uses));
            ActionOpId = actionOpId;
            Actor = actor;
            Binding = binding;
            Encounter = encounter;
            Round = round;
            Uses = uses;
        }

        /// <summary>
        /// Gets the action frame that spent the frequency.
        /// </summary>
        public OpId ActionOpId { get; }

        /// <summary>
        /// Gets the creature authorized by the binding.
        /// </summary>
        public CreatureId Actor { get; }

        /// <summary>
        /// Gets the active binding whose frequency changed.
        /// </summary>
        public BindingId Binding { get; }

        /// <summary>Gets the encounter that owns this frequency use.</summary>
        public EncounterId Encounter { get; }

        /// <summary>
        /// Gets the round marker retained by frequency state.
        /// </summary>
        public int Round { get; }

        /// <summary>
        /// Gets the recorded uses after the commit.
        /// </summary>
        public int Uses { get; }
    }
}
