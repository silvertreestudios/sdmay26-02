using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Registers the complete health command and reducer slice.</summary>
    public static class HealthRuleDispatcherExtensions
    {
        private static readonly RuleSource HealthReducerSource = RuleSource.FromSlug("health");

        /// <summary>
        /// Adds externally dispatchable health requests and their nested-only authoritative reducers.
        /// </summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseHealthRules(this RuleDispatcherBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            return builder
                .RegisterHandler<ApplyHealthBatchOp, HealthBatchOutcome>(
                    new ApplyHealthBatchHandler()
                )
                .RegisterHandler<ApplyDamageOp, DamageOutcome>(new ApplyDamageHandler())
                .RegisterReducer<CommitDamageOp, DamageOutcome>(
                    new CommitDamageReducer(),
                    HealthReducerSource
                )
                .RegisterHandler<ApplyHealingOp, HealingOutcome>(new ApplyHealingHandler())
                .RegisterReducer<CommitHealingOp, HealingOutcome>(
                    new CommitHealingReducer(),
                    HealthReducerSource
                )
                .RegisterHandler<FinalizeCreatureDefeatOp, bool>(
                    new FinalizeCreatureDefeatHandler()
                )
                .RegisterReducer<CommitCreatureDefeatOp, bool>(
                    new CommitCreatureDefeatReducer(),
                    HealthReducerSource
                )
                .RegisterHandler<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>(
                    new GrantTemporaryHitPointsHandler()
                )
                .RegisterReducer<CommitTemporaryHitPointsGrantOp, TemporaryHitPointsGrantOutcome>(
                    new CommitTemporaryHitPointsGrantReducer(),
                    HealthReducerSource
                )
                .RegisterHandler<RemoveTemporaryHitPointsOp, TemporaryHitPointsRemovalOutcome>(
                    new RemoveTemporaryHitPointsHandler()
                )
                .RegisterReducer<
                    CommitTemporaryHitPointsRemovalOp,
                    TemporaryHitPointsRemovalOutcome
                >(new CommitTemporaryHitPointsRemovalReducer(), HealthReducerSource)
                .RegisterHandler<AddTemporaryHitPointImmunityOp, TemporaryHitPointImmunityOutcome>(
                    new AddTemporaryHitPointImmunityHandler()
                )
                .RegisterReducer<
                    CommitTemporaryHitPointImmunityOp,
                    TemporaryHitPointImmunityOutcome
                >(new CommitTemporaryHitPointImmunityReducer(), HealthReducerSource);
        }
    }

    internal sealed class ApplyHealthBatchHandler
        : IOpHandler<ApplyHealthBatchOp, HealthBatchOutcome>
    {
        public async ValueTask<HealthBatchOutcome> Handle(
            OpFrame<ApplyHealthBatchOp> frame,
            OpHandlerContext context
        )
        {
            foreach (HealthBatchChange change in frame.Op.Changes)
            {
                if (!context.Snapshot.Health.TryGet(change.Target, out HealthState health))
                    throw new InvalidOperationException(
                        $"Creature {change.Target.Value} has no authoritative health state."
                    );
                if (change.Kind == HealthBatchChangeKind.Healing && health.IsCommittedDefeated)
                    throw new InvalidOperationException(
                        $"Creature {change.Target.Value} has a committed defeat and cannot be healed."
                    );
            }

            List<HealthBatchChangeOutcome> outcomes = new(frame.Op.Changes.Count);
            foreach (HealthBatchChange change in frame.Op.Changes)
            {
                int applied;
                if (change.Kind == HealthBatchChangeKind.Damage)
                {
                    DamageOutcome damage = await HealthHandlerResult.RequireResolved(
                        context.Dispatch(
                            new ApplyDamageOp(
                                change.Target,
                                change.Amount,
                                change.Origin,
                                change.Source
                            )
                        )
                    );
                    applied = damage.Applied;
                }
                else
                {
                    HealingOutcome healing = await HealthHandlerResult.RequireResolved(
                        context.Dispatch(
                            new ApplyHealingOp(
                                change.Target,
                                change.Amount,
                                change.Origin,
                                change.Source
                            )
                        )
                    );
                    applied = healing.Applied;
                }
                outcomes.Add(new HealthBatchChangeOutcome(change, applied));
            }
            return new HealthBatchOutcome(outcomes);
        }
    }

    internal sealed class ApplyDamageHandler : IOpHandler<ApplyDamageOp, DamageOutcome>
    {
        public async ValueTask<DamageOutcome> Handle(
            OpFrame<ApplyDamageOp> frame,
            OpHandlerContext context
        ) =>
            await HealthHandlerResult.RequireResolved(
                context.Dispatch(
                    new CommitDamageOp(
                        frame.Op.Target,
                        frame.Op.FinalDamage,
                        frame.Op.Origin,
                        frame.Op.Source
                    )
                )
            );
    }

    internal sealed class ApplyHealingHandler : IOpHandler<ApplyHealingOp, HealingOutcome>
    {
        public async ValueTask<HealingOutcome> Handle(
            OpFrame<ApplyHealingOp> frame,
            OpHandlerContext context
        ) =>
            await HealthHandlerResult.RequireResolved(
                context.Dispatch(
                    new CommitHealingOp(
                        frame.Op.Target,
                        frame.Op.Healing,
                        frame.Op.Origin,
                        frame.Op.Source
                    )
                )
            );
    }

    internal sealed class FinalizeCreatureDefeatHandler : IOpHandler<FinalizeCreatureDefeatOp, bool>
    {
        public async ValueTask<bool> Handle(
            OpFrame<FinalizeCreatureDefeatOp> frame,
            OpHandlerContext context
        ) =>
            await HealthHandlerResult.RequireResolved(
                context.Dispatch(new CommitCreatureDefeatOp(frame.Op.Target))
            );
    }

    internal sealed class GrantTemporaryHitPointsHandler
        : IOpHandler<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>
    {
        public async ValueTask<TemporaryHitPointsGrantOutcome> Handle(
            OpFrame<GrantTemporaryHitPointsOp> frame,
            OpHandlerContext context
        ) =>
            await HealthHandlerResult.RequireResolved(
                context.Dispatch(frame.Op.Intent.CreateCommitOperation(frame.Op))
            );
    }

    internal sealed class RemoveTemporaryHitPointsHandler
        : IOpHandler<RemoveTemporaryHitPointsOp, TemporaryHitPointsRemovalOutcome>
    {
        public async ValueTask<TemporaryHitPointsRemovalOutcome> Handle(
            OpFrame<RemoveTemporaryHitPointsOp> frame,
            OpHandlerContext context
        ) =>
            await HealthHandlerResult.RequireResolved(
                context.Dispatch(
                    new CommitTemporaryHitPointsRemovalOp(
                        frame.Op.Target,
                        frame.Op.Origin,
                        frame.Op.Source
                    )
                )
            );
    }

    internal sealed class AddTemporaryHitPointImmunityHandler
        : IOpHandler<AddTemporaryHitPointImmunityOp, TemporaryHitPointImmunityOutcome>
    {
        public async ValueTask<TemporaryHitPointImmunityOutcome> Handle(
            OpFrame<AddTemporaryHitPointImmunityOp> frame,
            OpHandlerContext context
        ) =>
            await HealthHandlerResult.RequireResolved(
                context.Dispatch(
                    new CommitTemporaryHitPointImmunityOp(
                        frame.Op.Target,
                        frame.Op.Origin,
                        frame.Op.Source
                    )
                )
            );
    }

    internal static class HealthHandlerResult
    {
        public static async ValueTask<TResult> RequireResolved<TResult>(
            ValueTask<OpResult<TResult>> pending
        )
        {
            OpResult<TResult> result = await pending;
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException(
                "A health reducer cannot be interrupted or cancelled."
            );
        }
    }
}
