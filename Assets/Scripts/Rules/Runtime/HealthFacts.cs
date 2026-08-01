using System;

namespace Game.Rules.Runtime
{
    /// <summary>Base payload for a committed health transition.</summary>
    public abstract class HealthFact : RuleFact
    {
        public CreatureId Creature { get; }
        public HealthChangeOriginId Origin { get; }

        /// <summary>Initializes provenance shared by a committed health Fact.</summary>
        /// <param name="creature">The creature whose health changed.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        protected HealthFact(CreatureId creature, HealthChangeOriginId origin)
        {
            Creature = HealthOperationValidation.RequireCreature(creature);
            Origin = HealthOperationValidation.RequireOrigin(origin);
        }
    }

    /// <summary>Records the exact final damage committed after upstream calculation.</summary>
    public sealed class DamageAppliedFact : HealthFact
    {
        public int Requested { get; }
        public int AppliedToTemporary { get; }
        public int AppliedToCurrent { get; }
        public int Applied => AppliedToTemporary + AppliedToCurrent;

        /// <summary>Initializes the exact committed final-damage breakdown.</summary>
        /// <param name="creature">The damaged creature.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="requested">The already-final damage requested.</param>
        /// <param name="appliedToTemporary">The amount consumed from temporary HP.</param>
        /// <param name="appliedToCurrent">The amount consumed from current HP.</param>
        public DamageAppliedFact(
            CreatureId creature,
            HealthChangeOriginId origin,
            int requested,
            int appliedToTemporary,
            int appliedToCurrent
        )
            : base(creature, origin)
        {
            Requested = requested;
            AppliedToTemporary = appliedToTemporary;
            AppliedToCurrent = appliedToCurrent;
        }
    }

    /// <summary>Records source-owned temporary Hit Points consumed by committed damage.</summary>
    public sealed class TemporaryHitPointsConsumedFact : HealthFact
    {
        public RuleSource TemporaryHitPointSource { get; }
        public int Amount { get; }

        /// <summary>Initializes a record of source-owned temporary HP consumed by damage.</summary>
        /// <param name="creature">The damaged creature.</param>
        /// <param name="origin">The encounter-stable damage cause.</param>
        /// <param name="temporaryHitPointSource">The source that owned the consumed pool.</param>
        /// <param name="amount">The exact amount consumed.</param>
        public TemporaryHitPointsConsumedFact(
            CreatureId creature,
            HealthChangeOriginId origin,
            RuleSource temporaryHitPointSource,
            int amount
        )
            : base(creature, origin)
        {
            TemporaryHitPointSource = temporaryHitPointSource;
            Amount = amount;
        }
    }

    /// <summary>Records the exact healing committed after maximum-HP clamping.</summary>
    public sealed class HealingAppliedFact : HealthFact
    {
        public int Requested { get; }
        public int Applied { get; }

        /// <summary>Initializes the exact committed healing record.</summary>
        /// <param name="creature">The healed creature.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="requested">The healing offered.</param>
        /// <param name="applied">The healing committed after clamping.</param>
        public HealingAppliedFact(
            CreatureId creature,
            HealthChangeOriginId origin,
            int requested,
            int applied
        )
            : base(creature, origin)
        {
            Requested = requested;
            Applied = applied;
        }
    }

    /// <summary>Records a source-aware temporary Hit Point grant replacing a lower pool.</summary>
    public sealed class TemporaryHitPointsGrantedFact : HealthFact
    {
        public RuleSource TemporaryHitPointSource { get; }
        public int PreviousAmount { get; }
        public int CurrentAmount { get; }

        /// <summary>Initializes a record of an accepted source-owned temporary-HP pool.</summary>
        /// <param name="creature">The creature receiving the pool.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="temporaryHitPointSource">The source that owns the accepted pool.</param>
        /// <param name="previousAmount">The pool replaced by the grant.</param>
        /// <param name="currentAmount">The newly authoritative pool.</param>
        public TemporaryHitPointsGrantedFact(
            CreatureId creature,
            HealthChangeOriginId origin,
            RuleSource temporaryHitPointSource,
            int previousAmount,
            int currentAmount
        )
            : base(creature, origin)
        {
            TemporaryHitPointSource = temporaryHitPointSource;
            PreviousAmount = previousAmount;
            CurrentAmount = currentAmount;
        }
    }

    /// <summary>Records removal of remaining temporary Hit Points owned by one source.</summary>
    public sealed class TemporaryHitPointsRemovedFact : HealthFact
    {
        public RuleSource TemporaryHitPointSource { get; }
        public int Removed { get; }

        /// <summary>Initializes a record of source-owned temporary HP removed by cleanup.</summary>
        /// <param name="creature">The creature whose pool was removed.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="temporaryHitPointSource">The source that owned the removed pool.</param>
        /// <param name="removed">The exact amount removed.</param>
        public TemporaryHitPointsRemovedFact(
            CreatureId creature,
            HealthChangeOriginId origin,
            RuleSource temporaryHitPointSource,
            int removed
        )
            : base(creature, origin)
        {
            TemporaryHitPointSource = temporaryHitPointSource;
            Removed = removed;
        }
    }

    /// <summary>Records that one source became unable to grant temporary Hit Points.</summary>
    public sealed class TemporaryHitPointImmunityAddedFact : HealthFact
    {
        public RuleSource TemporaryHitPointSource { get; }

        /// <summary>Initializes a record of newly committed source-specific immunity.</summary>
        /// <param name="creature">The creature receiving the immunity.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="temporaryHitPointSource">The source now blocked from granting.</param>
        public TemporaryHitPointImmunityAddedFact(
            CreatureId creature,
            HealthChangeOriginId origin,
            RuleSource temporaryHitPointSource
        )
            : base(creature, origin) => TemporaryHitPointSource = temporaryHitPointSource;
    }

    /// <summary>Records the single positive-current-HP to zero-current-HP transition.</summary>
    /// <remarks>
    /// The committing transaction removes any authoritative position entry without removing the
    /// zero-HP health or creature-identity entries used by Fact and presentation consumers.
    /// </remarks>
    public sealed class CreatureReducedToZeroFact : HealthFact
    {
        /// <summary>Initializes the committed positive-to-zero transition record.</summary>
        /// <param name="creature">The creature reduced to zero current HP.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        public CreatureReducedToZeroFact(CreatureId creature, HealthChangeOriginId origin)
            : base(creature, origin) { }
    }

    /// <summary>
    /// Records that a zero-hit-point creature crossed the authoritative defeat boundary.
    /// </summary>
    /// <remarks>
    /// Reaching zero hit points and committing defeat are separate lifecycle steps so reactions to
    /// the damage Fact settle before encounter outcome observes the committed state.
    /// </remarks>
    public sealed class CreatureDefeatCommittedFact : RuleFact
    {
        /// <summary>Gets the creature whose defeat was committed.</summary>
        public CreatureId Creature { get; }

        /// <summary>Initializes an authoritative defeat transition record.</summary>
        /// <param name="creature">The creature whose defeat was committed.</param>
        public CreatureDefeatCommittedFact(CreatureId creature) =>
            Creature = creature.IsEmpty
                ? throw new ArgumentException("Creature is required.", nameof(creature))
                : creature;
    }

    /// <summary>Describes the exact damage distribution committed by the health reducer.</summary>
    public readonly struct DamageOutcome
    {
        public int Requested { get; }
        public int AppliedToTemporary { get; }
        public int AppliedToCurrent { get; }
        public int Applied => AppliedToTemporary + AppliedToCurrent;

        /// <summary>Initializes an exact final-damage outcome.</summary>
        /// <param name="requested">The already-final damage requested.</param>
        /// <param name="appliedToTemporary">The amount consumed from temporary HP.</param>
        /// <param name="appliedToCurrent">The amount consumed from current HP.</param>
        public DamageOutcome(int requested, int appliedToTemporary, int appliedToCurrent)
        {
            Requested = requested;
            AppliedToTemporary = appliedToTemporary;
            AppliedToCurrent = appliedToCurrent;
        }
    }

    /// <summary>Describes healing that survived maximum-HP clamping.</summary>
    public readonly struct HealingOutcome
    {
        public int Requested { get; }
        public int Applied { get; }

        /// <summary>Initializes a healing outcome.</summary>
        /// <param name="requested">The healing offered.</param>
        /// <param name="applied">The healing committed after clamping.</param>
        public HealingOutcome(int requested, int applied)
        {
            Requested = requested;
            Applied = applied;
        }
    }

    /// <summary>Describes whether and how a temporary Hit Point grant changed state.</summary>
    public readonly struct TemporaryHitPointsGrantOutcome
    {
        public bool Granted { get; }
        public bool Immune { get; }
        public int PreviousAmount { get; }
        public int CurrentAmount { get; }

        /// <summary>Initializes the result of one temporary-HP offer.</summary>
        /// <param name="granted">Whether the offered pool replaced the current pool.</param>
        /// <param name="immune">Whether source-specific immunity blocked the offer.</param>
        /// <param name="previousAmount">The pool before the offer.</param>
        /// <param name="currentAmount">The pool after the offer.</param>
        public TemporaryHitPointsGrantOutcome(
            bool granted,
            bool immune,
            int previousAmount,
            int currentAmount
        )
        {
            Granted = granted;
            Immune = immune;
            PreviousAmount = previousAmount;
            CurrentAmount = currentAmount;
        }
    }

    /// <summary>Describes a committed or no-op temporary Hit Point removal.</summary>
    public readonly struct TemporaryHitPointsRemovalOutcome
    {
        public int Removed { get; }

        /// <summary>Initializes a source-owned temporary-HP removal outcome.</summary>
        /// <param name="removed">The exact pool removed.</param>
        public TemporaryHitPointsRemovalOutcome(int removed) => Removed = removed;
    }

    /// <summary>Describes whether a new temporary Hit Point immunity was committed.</summary>
    public readonly struct TemporaryHitPointImmunityOutcome
    {
        public bool Added { get; }

        /// <summary>Initializes a source-specific immunity outcome.</summary>
        /// <param name="added">Whether a new immunity was committed.</param>
        public TemporaryHitPointImmunityOutcome(bool added) => Added = added;
    }
}
