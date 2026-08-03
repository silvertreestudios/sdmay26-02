using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal readonly struct TemporaryHitPointsPoolState : IEquatable<TemporaryHitPointsPoolState>
    {
        internal TemporaryHitPointsPoolState(int amount, RuleSource source, long revision)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0 && !source.IsEmpty)
                throw new ArgumentException(
                    "An empty temporary Hit Point pool cannot retain a source.",
                    nameof(source)
                );
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));
            Amount = amount;
            Source = source;
            Revision = revision;
        }

        internal int Amount { get; }
        internal RuleSource Source { get; }
        internal long Revision { get; }

        internal static TemporaryHitPointsPoolState Capture(HealthState health) =>
            new TemporaryHitPointsPoolState(
                health.Temporary,
                health.TemporarySource,
                health.TemporaryHitPointRevision
            );

        public bool Equals(TemporaryHitPointsPoolState other) =>
            Amount == other.Amount && Source == other.Source && Revision == other.Revision;

        public override bool Equals(object obj) =>
            obj is TemporaryHitPointsPoolState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Amount, Source, Revision);
    }

    internal readonly struct TemporaryHitPointsGrantTransition
    {
        internal TemporaryHitPointsGrantTransition(
            HealthState before,
            HealthState after,
            TemporaryHitPointsGrantOutcome outcome
        )
        {
            Before = before;
            After = after;
            Outcome = outcome;
        }

        internal HealthState Before { get; }
        internal HealthState After { get; }
        internal TemporaryHitPointsGrantOutcome Outcome { get; }
        internal bool ChangesHealth => !Before.Equals(After);
        internal TemporaryHitPointsPoolState BeforePool =>
            TemporaryHitPointsPoolState.Capture(Before);
        internal TemporaryHitPointsPoolState AfterPool =>
            TemporaryHitPointsPoolState.Capture(After);
    }

    internal readonly struct TemporaryHitPointsPoolRestorationTransition
    {
        internal TemporaryHitPointsPoolRestorationTransition(
            HealthState before,
            HealthState after,
            TemporaryHitPointsPoolState restoredPool
        )
        {
            Before = before;
            After = after;
            RestoredPool = restoredPool;
        }

        internal HealthState Before { get; }
        internal HealthState After { get; }
        internal TemporaryHitPointsPoolState RestoredPool { get; }
        internal bool ChangesHealth => !Before.Equals(After);
    }

    internal static class TemporaryHitPointsGrantReduction
    {
        internal static bool TryPrepare(
            RulesStateDraft state,
            CreatureId target,
            int amount,
            RuleSource source,
            out TemporaryHitPointsGrantTransition transition,
            out string rejection
        )
        {
            transition = default;
            if (!HealthReducerState.TryGet(state, target, out HealthState health))
            {
                rejection = "Target has no authoritative health state.";
                return false;
            }
            if (health.IsCommittedDefeated)
            {
                rejection = "A committed-defeated creature cannot receive temporary Hit Points.";
                return false;
            }

            HealthState after = health;
            TemporaryHitPointsGrantOutcome outcome;
            if (health.HasTemporaryHitPointImmunity(source))
            {
                outcome = new TemporaryHitPointsGrantOutcome(
                    false,
                    true,
                    health.Temporary,
                    health.Temporary
                );
            }
            else if (amount <= health.Temporary)
            {
                outcome = new TemporaryHitPointsGrantOutcome(
                    false,
                    false,
                    health.Temporary,
                    health.Temporary
                );
            }
            else
            {
                after = HealthReducerState.With(health, health.Current, amount, source);
                outcome = new TemporaryHitPointsGrantOutcome(true, false, health.Temporary, amount);
            }

            transition = new TemporaryHitPointsGrantTransition(health, after, outcome);
            rejection = string.Empty;
            return true;
        }

        internal static TemporaryHitPointsGrantTransition Commit(
            RulesStateDraft state,
            FactSink facts,
            CreatureId target,
            HealthChangeOriginId origin,
            RuleSource source,
            TemporaryHitPointsGrantTransition transition
        )
        {
            if (!transition.ChangesHealth)
                return transition;
            state.Health.Set(target, transition.After);
            if (!state.Health.TryGet(target, out HealthState committed))
                throw new InvalidOperationException(
                    "A committed temporary Hit Point grant lost authoritative health state."
                );
            facts.Stage(
                new TemporaryHitPointsGrantedFact(
                    target,
                    origin,
                    source,
                    transition.Before.Temporary,
                    transition.After.Temporary
                )
            );
            return new TemporaryHitPointsGrantTransition(
                transition.Before,
                committed,
                transition.Outcome
            );
        }

        internal static bool TryPrepareExactRestoration(
            RulesStateDraft state,
            CreatureId target,
            RuleSource abandonedSource,
            TemporaryHitPointsPoolState expectedAbandonedPool,
            TemporaryHitPointsPoolState priorPool,
            out TemporaryHitPointsPoolRestorationTransition transition,
            out string rejection
        )
        {
            transition = default;
            if (expectedAbandonedPool.Source != abandonedSource)
            {
                rejection = "Temporary Hit Point compensation requires its exact abandoned source.";
                return false;
            }
            if (!HealthReducerState.TryGet(state, target, out HealthState current))
            {
                rejection = "Target has no authoritative health state.";
                return false;
            }

            TemporaryHitPointsPoolState currentPool = TemporaryHitPointsPoolState.Capture(current);
            HealthState after = current;
            if (currentPool.Revision == expectedAbandonedPool.Revision)
            {
                if (
                    currentPool.Amount != expectedAbandonedPool.Amount
                    || currentPool.Source != expectedAbandonedPool.Source
                )
                {
                    rejection =
                        "Temporary Hit Point compensation found conflicting pool values at the expected revision.";
                    return false;
                }
                after = HealthReducerState.With(
                    current,
                    current.Current,
                    priorPool.Amount,
                    priorPool.Source
                );
            }
            else if (currentPool.Amount > 0 && currentPool.Source == abandonedSource)
            {
                rejection =
                    "Temporary Hit Point compensation cannot discard a newer pool from the abandoned source.";
                return false;
            }
            transition = new TemporaryHitPointsPoolRestorationTransition(current, after, priorPool);
            rejection = string.Empty;
            return true;
        }

        internal static void CommitExactRestoration(
            RulesStateDraft state,
            FactSink facts,
            CreatureId target,
            HealthChangeOriginId origin,
            RuleSource abandonedSource,
            TemporaryHitPointsPoolRestorationTransition transition
        )
        {
            if (!transition.ChangesHealth)
                return;
            state.Health.Set(target, transition.After);
            facts.Stage(
                new TemporaryHitPointsPoolRestoredFact(
                    target,
                    origin,
                    abandonedSource,
                    transition.Before.Temporary,
                    transition.RestoredPool.Source,
                    transition.RestoredPool.Amount
                )
            );
        }
    }

    internal static class HealthReducerState
    {
        public static bool TryGet(
            RulesStateDraft state,
            CreatureId creature,
            out HealthState health
        ) => state.Health.TryGet(creature, out health);

        public static HealthState With(
            HealthState previous,
            int current,
            int temporary,
            RuleSource temporarySource
        ) => previous.WithValues(current, temporary, temporarySource);
    }

    /// <summary>Commits the shared health and occupancy state for an unrecoverable defeat.</summary>
    internal static class HealthDefeatState
    {
        internal static bool Commit(RulesStateDraft state, CreatureId creature, FactSink facts)
        {
            if (!state.Health.TryGet(creature, out HealthState health))
                throw new InvalidOperationException(
                    "Defeat finalization requires authoritative health state."
                );
            if (health.IsCommittedDefeated)
                return false;
            state.Health.Set(creature, health.CommitDefeat());
            state.Positions.Remove(creature);
            facts.Stage(new CreatureDefeatCommittedFact(creature));
            return true;
        }
    }

    internal static class DamageReduction
    {
        internal static ReductionResult<DamageOutcome> Commit(
            RulesStateDraft state,
            FactSink facts,
            CreatureId target,
            int finalDamage,
            HealthChangeOriginId origin
        )
        {
            if (!HealthReducerState.TryGet(state, target, out HealthState health))
                return ReductionResult<DamageOutcome>.Reject(
                    "Target has no authoritative health state."
                );

            int appliedToTemporary = Math.Min(health.Temporary, finalDamage);
            int remaining = finalDamage - appliedToTemporary;
            int appliedToCurrent = Math.Min(health.Current, remaining);
            DamageOutcome outcome = new DamageOutcome(
                finalDamage,
                appliedToTemporary,
                appliedToCurrent
            );
            if (outcome.Applied == 0)
                return ReductionResult<DamageOutcome>.Accept(outcome);

            int current = health.Current - appliedToCurrent;
            int temporary = health.Temporary - appliedToTemporary;
            RuleSource temporarySource = temporary == 0 ? default : health.TemporarySource;
            state.Health.Set(
                target,
                HealthReducerState.With(health, current, temporary, temporarySource)
            );
            facts.Stage(
                new DamageAppliedFact(
                    target,
                    origin,
                    finalDamage,
                    appliedToTemporary,
                    appliedToCurrent
                )
            );
            if (appliedToTemporary > 0)
            {
                facts.Stage(
                    new TemporaryHitPointsConsumedFact(
                        target,
                        origin,
                        health.TemporarySource,
                        appliedToTemporary
                    )
                );
            }
            if (health.Current > 0 && current == 0)
            {
                facts.Stage(new CreatureReducedToZeroFact(target, origin));
                if (!EncounterDefeatAuthority.Owns(state, target))
                    HealthDefeatState.Commit(state, target, facts);
            }
            return ReductionResult<DamageOutcome>.Accept(outcome);
        }
    }

    internal sealed class CommitDamageReducer : IOpReducer<CommitDamageOp, DamageOutcome>
    {
        public ReductionResult<DamageOutcome> Reduce(
            ReductionContext<CommitDamageOp> context,
            RulesStateDraft state,
            FactSink facts
        ) =>
            DamageReduction.Commit(
                state,
                facts,
                context.Op.Target,
                context.Op.FinalDamage,
                context.Op.Origin
            );
    }

    internal sealed class CommitHealingReducer : IOpReducer<CommitHealingOp, HealingOutcome>
    {
        public ReductionResult<HealingOutcome> Reduce(
            ReductionContext<CommitHealingOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!HealthReducerState.TryGet(state, context.Op.Target, out HealthState health))
                return ReductionResult<HealingOutcome>.Reject(
                    "Target has no authoritative health state."
                );
            if (health.IsCommittedDefeated)
                return ReductionResult<HealingOutcome>.Reject(
                    "A committed-defeated creature cannot be healed."
                );

            int applied = Math.Min(context.Op.Healing, health.Maximum - health.Current);
            HealingOutcome outcome = new HealingOutcome(context.Op.Healing, applied);
            if (applied == 0)
                return ReductionResult<HealingOutcome>.Accept(outcome);

            state.Health.Set(
                context.Op.Target,
                HealthReducerState.With(
                    health,
                    health.Current + applied,
                    health.Temporary,
                    health.TemporarySource
                )
            );
            facts.Stage(
                new HealingAppliedFact(
                    context.Op.Target,
                    context.Op.Origin,
                    context.Op.Healing,
                    applied
                )
            );
            return ReductionResult<HealingOutcome>.Accept(outcome);
        }
    }

    internal sealed class CommitTemporaryHitPointsGrantReducer
        : IOpReducer<CommitTemporaryHitPointsGrantOp, TemporaryHitPointsGrantOutcome>
    {
        public ReductionResult<TemporaryHitPointsGrantOutcome> Reduce(
            ReductionContext<CommitTemporaryHitPointsGrantOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !TemporaryHitPointsGrantReduction.TryPrepare(
                    state,
                    context.Op.Target,
                    context.Op.Amount,
                    context.Op.Source,
                    out TemporaryHitPointsGrantTransition transition,
                    out string rejection
                )
            )
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Reject(rejection);
            TemporaryHitPointsGrantReduction.Commit(
                state,
                facts,
                context.Op.Target,
                context.Op.Origin,
                context.Op.Source,
                transition
            );
            return ReductionResult<TemporaryHitPointsGrantOutcome>.Accept(transition.Outcome);
        }
    }

    internal sealed class CommitTemporaryHitPointsRemovalReducer
        : IOpReducer<CommitTemporaryHitPointsRemovalOp, TemporaryHitPointsRemovalOutcome>
    {
        public ReductionResult<TemporaryHitPointsRemovalOutcome> Reduce(
            ReductionContext<CommitTemporaryHitPointsRemovalOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!HealthReducerState.TryGet(state, context.Op.Target, out HealthState health))
            {
                return ReductionResult<TemporaryHitPointsRemovalOutcome>.Reject(
                    "Target has no authoritative health state."
                );
            }
            if (health.Temporary == 0 || health.TemporarySource != context.Op.Source)
            {
                return ReductionResult<TemporaryHitPointsRemovalOutcome>.Accept(
                    new TemporaryHitPointsRemovalOutcome(0)
                );
            }

            state.Health.Set(
                context.Op.Target,
                HealthReducerState.With(health, health.Current, 0, default)
            );
            facts.Stage(
                new TemporaryHitPointsRemovedFact(
                    context.Op.Target,
                    context.Op.Origin,
                    context.Op.Source,
                    health.Temporary
                )
            );
            return ReductionResult<TemporaryHitPointsRemovalOutcome>.Accept(
                new TemporaryHitPointsRemovalOutcome(health.Temporary)
            );
        }
    }

    internal sealed class CommitTemporaryHitPointImmunityReducer
        : IOpReducer<CommitTemporaryHitPointImmunityOp, TemporaryHitPointImmunityOutcome>
    {
        public ReductionResult<TemporaryHitPointImmunityOutcome> Reduce(
            ReductionContext<CommitTemporaryHitPointImmunityOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!HealthReducerState.TryGet(state, context.Op.Target, out HealthState health))
            {
                return ReductionResult<TemporaryHitPointImmunityOutcome>.Reject(
                    "Target has no authoritative health state."
                );
            }
            if (health.HasTemporaryHitPointImmunity(context.Op.Source))
            {
                return ReductionResult<TemporaryHitPointImmunityOutcome>.Accept(
                    new TemporaryHitPointImmunityOutcome(false)
                );
            }

            List<RuleSource> immunities = health.TemporaryHitPointImmunities.ToList();
            immunities.Add(context.Op.Source);
            state.Health.Set(context.Op.Target, health.WithTemporaryHitPointImmunities(immunities));
            facts.Stage(
                new TemporaryHitPointImmunityAddedFact(
                    context.Op.Target,
                    context.Op.Origin,
                    context.Op.Source
                )
            );
            return ReductionResult<TemporaryHitPointImmunityOutcome>.Accept(
                new TemporaryHitPointImmunityOutcome(true)
            );
        }
    }

    internal sealed class CommitCreatureDefeatReducer : IOpReducer<CommitCreatureDefeatOp, bool>
    {
        public ReductionResult<bool> Reduce(
            ReductionContext<CommitCreatureDefeatOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!HealthReducerState.TryGet(state, context.Op.Target, out HealthState health))
                return ReductionResult<bool>.Reject("Target has no authoritative health state.");
            if (health.Current > 0)
                return ReductionResult<bool>.Reject(
                    "A living creature cannot commit authoritative defeat."
                );
            if (health.IsCommittedDefeated)
                return ReductionResult<bool>.Accept(false);

            return ReductionResult<bool>.Accept(
                HealthDefeatState.Commit(state, context.Op.Target, facts)
            );
        }
    }
}
