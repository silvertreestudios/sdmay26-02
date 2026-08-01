using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Stores the number of attacks a creature has made during its current turn.</summary>
    public readonly struct MultipleAttackPenaltyState : IEquatable<MultipleAttackPenaltyState>
    {
        /// <summary>Gets the non-negative number of prior attacks this turn.</summary>
        public int AttackCount { get; }

        /// <summary>Creates a multiple-attack-penalty state value.</summary>
        /// <param name="attackCount">The non-negative number of prior attacks this turn.</param>
        public MultipleAttackPenaltyState(int attackCount)
        {
            if (attackCount < 0)
                throw new ArgumentOutOfRangeException(nameof(attackCount));
            AttackCount = attackCount;
        }

        /// <inheritdoc/>
        public bool Equals(MultipleAttackPenaltyState other) => AttackCount == other.AttackCount;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is MultipleAttackPenaltyState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => AttackCount;

        /// <summary>Checks whether two MAP states have the same prior-attack count.</summary>
        public static bool operator ==(
            MultipleAttackPenaltyState left,
            MultipleAttackPenaltyState right
        ) => left.Equals(right);

        /// <summary>Checks whether two MAP states have different prior-attack counts.</summary>
        public static bool operator !=(
            MultipleAttackPenaltyState left,
            MultipleAttackPenaltyState right
        ) => !left.Equals(right);
    }

    /// <summary>Resolves the signed multiple attack penalty for a normal or agile attack.</summary>
    public static class MultipleAttackPenaltyResolver
    {
        /// <summary>
        /// Gets the penalty for the next attack from the number of attacks already made this turn.
        /// </summary>
        /// <param name="attackCount">The non-negative number of prior attacks.</param>
        /// <param name="isAgile">Whether the attack uses the reduced agile penalties.</param>
        /// <returns>Zero, -5/-10 for normal attacks, or -4/-8 for agile attacks.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="attackCount"/> is negative.</exception>
        public static int Resolve(int attackCount, bool isAgile)
        {
            if (attackCount < 0)
                throw new ArgumentOutOfRangeException(nameof(attackCount));
            if (attackCount == 0)
                return 0;
            if (attackCount == 1)
                return isAgile ? -4 : -5;
            return isAgile ? -8 : -10;
        }
    }

    /// <summary>Advances the rules-owned MAP shared by every attack action.</summary>
    public sealed class AdvanceMultipleAttackPenaltyOp : IRuleOp<MultipleAttackPenaltyState>
    {
        /// <summary>Creates a MAP advancement request for a resolved attack.</summary>
        /// <param name="actor">The creature whose attack count advances.</param>
        public AdvanceMultipleAttackPenaltyOp(CreatureId actor)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("An attack actor is required.", nameof(actor));
            Actor = actor;
        }

        /// <summary>Gets the attacking creature.</summary>
        public CreatureId Actor { get; }
    }

    /// <summary>Reports a committed shared MAP advancement.</summary>
    public sealed class MultipleAttackPenaltyAdvancedFact : RuleFact
    {
        internal MultipleAttackPenaltyAdvancedFact(CreatureId actor, int attackCount)
        {
            Actor = actor;
            AttackCount = attackCount;
        }

        /// <summary>Gets the attacking creature.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the committed attack count.</summary>
        public int AttackCount { get; }
    }

    /// <summary>Installs shared multiple-attack-penalty advancement behavior.</summary>
    public static class MultipleAttackPenaltyRuleDispatcherExtensions
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("multiple-attack-penalty");

        /// <summary>Adds the shared MAP operation and its authoritative state transition.</summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseMultipleAttackPenaltyRules(
            this RuleDispatcherBuilder builder
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            return builder
                .RegisterHandler<AdvanceMultipleAttackPenaltyOp, MultipleAttackPenaltyState>(
                    new AdvanceMultipleAttackPenaltyHandler()
                )
                .RegisterReducer<CommitMultipleAttackPenaltyAdvanceOp, MultipleAttackPenaltyState>(
                    new AdvanceMultipleAttackPenaltyReducer(),
                    Source
                );
        }
    }

    internal sealed class AdvanceMultipleAttackPenaltyHandler
        : IOpHandler<AdvanceMultipleAttackPenaltyOp, MultipleAttackPenaltyState>
    {
        public async ValueTask<MultipleAttackPenaltyState> Handle(
            OpFrame<AdvanceMultipleAttackPenaltyOp> frame,
            OpHandlerContext context
        )
        {
            OpResult<MultipleAttackPenaltyState> result = await context.Dispatch(
                new CommitMultipleAttackPenaltyAdvanceOp(frame.Op.Actor)
            );
            if (result is ResolvedOpResult<MultipleAttackPenaltyState> resolved)
                return resolved.Value;
            throw new InvalidOperationException("MAP commitment did not resolve.");
        }
    }

    internal sealed class CommitMultipleAttackPenaltyAdvanceOp : IRuleOp<MultipleAttackPenaltyState>
    {
        public CommitMultipleAttackPenaltyAdvanceOp(CreatureId actor) => Actor = actor;

        public CreatureId Actor { get; }
    }

    internal sealed class AdvanceMultipleAttackPenaltyReducer
        : IOpReducer<CommitMultipleAttackPenaltyAdvanceOp, MultipleAttackPenaltyState>
    {
        public ReductionResult<MultipleAttackPenaltyState> Reduce(
            ReductionContext<CommitMultipleAttackPenaltyAdvanceOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!state.Creatures.Contains(context.Op.Actor))
                return ReductionResult<MultipleAttackPenaltyState>.Reject(
                    "The attack actor is not registered."
                );
            if (
                !state.MultipleAttackPenalty.TryGet(
                    context.Op.Actor,
                    out MultipleAttackPenaltyState current
                )
            )
                return ReductionResult<MultipleAttackPenaltyState>.Reject(
                    "The attack actor has no authoritative multiple-attack-penalty state."
                );
            int previous = current.AttackCount;
            MultipleAttackPenaltyState advanced = new MultipleAttackPenaltyState(
                checked(previous + 1)
            );
            state.MultipleAttackPenalty.Set(context.Op.Actor, advanced);
            facts.Stage(
                new MultipleAttackPenaltyAdvancedFact(context.Op.Actor, advanced.AttackCount)
            );
            return ReductionResult<MultipleAttackPenaltyState>.Accept(advanced);
        }
    }
}
