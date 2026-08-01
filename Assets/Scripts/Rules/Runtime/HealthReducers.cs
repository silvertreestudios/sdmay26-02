using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
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

    internal sealed class CommitDamageReducer : IOpReducer<CommitDamageOp, DamageOutcome>
    {
        public ReductionResult<DamageOutcome> Reduce(
            ReductionContext<CommitDamageOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!HealthReducerState.TryGet(state, context.Op.Target, out HealthState health))
                return ReductionResult<DamageOutcome>.Reject(
                    "Target has no authoritative health state."
                );

            int appliedToTemporary = Math.Min(health.Temporary, context.Op.FinalDamage);
            int remaining = context.Op.FinalDamage - appliedToTemporary;
            int appliedToCurrent = Math.Min(health.Current, remaining);
            DamageOutcome outcome = new DamageOutcome(
                context.Op.FinalDamage,
                appliedToTemporary,
                appliedToCurrent
            );
            if (outcome.Applied == 0)
                return ReductionResult<DamageOutcome>.Accept(outcome);

            int current = health.Current - appliedToCurrent;
            int temporary = health.Temporary - appliedToTemporary;
            RuleSource temporarySource = temporary == 0 ? default : health.TemporarySource;
            state.Health.Set(
                context.Op.Target,
                HealthReducerState.With(health, current, temporary, temporarySource)
            );
            facts.Stage(
                new DamageAppliedFact(
                    context.Op.Target,
                    context.Op.Origin,
                    context.Op.FinalDamage,
                    appliedToTemporary,
                    appliedToCurrent
                )
            );
            if (appliedToTemporary > 0)
            {
                facts.Stage(
                    new TemporaryHitPointsConsumedFact(
                        context.Op.Target,
                        context.Op.Origin,
                        health.TemporarySource,
                        appliedToTemporary
                    )
                );
            }
            if (health.Current > 0 && current == 0)
            {
                facts.Stage(new CreatureReducedToZeroFact(context.Op.Target, context.Op.Origin));
                if (!EncounterDefeatAuthority.Owns(state, context.Op.Target))
                    HealthDefeatState.Commit(state, context.Op.Target, facts);
            }
            return ReductionResult<DamageOutcome>.Accept(outcome);
        }
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
            if (!HealthReducerState.TryGet(state, context.Op.Target, out HealthState health))
            {
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Reject(
                    "Target has no authoritative health state."
                );
            }
            if (health.IsCommittedDefeated)
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Reject(
                    "A committed-defeated creature cannot receive temporary Hit Points."
                );
            if (health.HasTemporaryHitPointImmunity(context.Op.Source))
            {
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Accept(
                    new TemporaryHitPointsGrantOutcome(
                        false,
                        true,
                        health.Temporary,
                        health.Temporary
                    )
                );
            }
            if (context.Op.Amount <= health.Temporary)
            {
                return ReductionResult<TemporaryHitPointsGrantOutcome>.Accept(
                    new TemporaryHitPointsGrantOutcome(
                        false,
                        false,
                        health.Temporary,
                        health.Temporary
                    )
                );
            }

            state.Health.Set(
                context.Op.Target,
                HealthReducerState.With(
                    health,
                    health.Current,
                    context.Op.Amount,
                    context.Op.Source
                )
            );
            facts.Stage(
                new TemporaryHitPointsGrantedFact(
                    context.Op.Target,
                    context.Op.Origin,
                    context.Op.Source,
                    health.Temporary,
                    context.Op.Amount
                )
            );
            return ReductionResult<TemporaryHitPointsGrantOutcome>.Accept(
                new TemporaryHitPointsGrantOutcome(true, false, health.Temporary, context.Op.Amount)
            );
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
