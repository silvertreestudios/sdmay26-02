using System;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    internal sealed class ActionBegunHandler : IOpHandler<ActionBegunOp, ActionStartOutcome>
    {
        public ValueTask<ActionStartOutcome> Handle(
            OpFrame<ActionBegunOp> frame,
            OpHandlerContext context
        ) => new ValueTask<ActionStartOutcome>(ActionStartOutcome.Continue);
    }

    internal sealed class CommitActionCostsReducer
        : IOpReducer<CommitActionCostsOp, ActionCostsOutcome>
    {
        public ReductionResult<ActionCostsOutcome> Reduce(
            ReductionContext<CommitActionCostsOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            bool hasReceiptedAction = context.Op.ReceiptCheckpoint.TryGetOperation(
                out IReceiptedActionOp receiptedAction
            );
            if (hasReceiptedAction && state.ActionReceipts.Contains(receiptedAction.InvocationId))
            {
                return ReductionResult<ActionCostsOutcome>.Reject(
                    ActionReceiptReduction.AlreadyCheckpointedReason
                );
            }

            ActionValidationResult actionCost = SpendActionCost(context.Op, state, facts);
            if (
                actionCost is ActionValidationResult.InvalidActionValidationResult invalidActionCost
            )
                return ReductionResult<ActionCostsOutcome>.Reject(invalidActionCost.Reason);

            foreach (RuleCost cost in context.Op.Profile.AdditionalCosts)
            {
                ActionValidationResult additionalCost = SpendAdditionalCost(
                    context.Op,
                    cost,
                    state,
                    facts
                );
                if (additionalCost is ActionValidationResult.InvalidActionValidationResult invalid)
                    return ReductionResult<ActionCostsOutcome>.Reject(invalid.Reason);
            }

            if (
                hasReceiptedAction
                && !ActionReceiptReduction.TryCheckpointCosts(
                    state,
                    facts,
                    receiptedAction,
                    context.Op.Profile
                )
            )
            {
                return ReductionResult<ActionCostsOutcome>.Reject(
                    ActionReceiptReduction.AlreadyCheckpointedReason
                );
            }

            return ReductionResult<ActionCostsOutcome>.Accept(default);
        }

        private static ActionValidationResult SpendActionCost(
            CommitActionCostsOp op,
            RulesStateDraft state,
            FactSink facts
        )
        {
            ActionCost cost = op.Profile.Cost;
            if (cost.Kind == ActionCostKind.None || cost.Kind == ActionCostKind.FreeAction)
                return ActionValidationResult.Valid;

            if (!state.ActionEconomy.TryGet(op.Actor, out ActionEconomyState economy))
            {
                return ActionValidationResult.Invalid(
                    $"{op.Actor.Value} has no authoritative action-economy state."
                );
            }

            if (
                !ActionResourcePayment.TryPay(
                    economy,
                    op.DefinitionId,
                    op.Profile,
                    out ActionEconomyState remaining,
                    out ActionResourceKind? spentResource
                )
            )
                return ActionValidationResult.Invalid(
                    cost.Kind == ActionCostKind.Reaction
                        ? "The actor's reaction is not available."
                        : "The actor has insufficient actions remaining."
                );

            state.ActionEconomy.Set(op.Actor, remaining);

            facts.Stage(new ActionCostSpentFact(op.ActionOpId, op.Actor, cost));
            if (spentResource.HasValue)
            {
                facts.Stage(
                    new ActionResourceSpentFact(
                        op.ActionOpId,
                        op.Actor,
                        spentResource.Value,
                        cost.Amount,
                        remaining.StandardActionsRemaining,
                        !remaining.OptionalAction.IsNone
                    )
                );
            }
            return ActionValidationResult.Valid;
        }

        private static ActionValidationResult SpendAdditionalCost(
            CommitActionCostsOp op,
            RuleCost cost,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (cost is SpellSlotRuleCost spellSlot)
                return SpendSpellSlot(op, spellSlot, state, facts);
            if (cost is FocusPointRuleCost focusPoint)
                return SpendFocusPoints(op, focusPoint, state, facts);
            if (cost is AmmunitionRuleCost ammunition)
                return SpendAmmunition(op, ammunition, state, facts);
            if (cost is OncePerRoundRuleCost frequency)
                return SpendFrequency(op, frequency, state, facts);

            throw new InvalidOperationException(
                $"Unsupported rule cost type {cost.GetType().Name}."
            );
        }

        private static ActionValidationResult SpendSpellSlot(
            CommitActionCostsOp op,
            SpellSlotRuleCost cost,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!state.SpellSlots.TryGet(cost.Pool, out SpellSlotState slot))
                return ActionValidationResult.Invalid(
                    "The required spell-slot pool is unavailable."
                );
            if (slot.Owner != op.Actor)
                return ActionValidationResult.Invalid(
                    "The acting creature does not own the spell-slot pool."
                );
            if (slot.Remaining < cost.Amount)
                return ActionValidationResult.Invalid(
                    "The spell-slot pool has insufficient uses remaining."
                );

            int remaining = slot.Remaining - cost.Amount;
            state.SpellSlots.Set(
                cost.Pool,
                new SpellSlotState(slot.Id, slot.Owner, remaining, slot.Maximum)
            );
            facts.Stage(
                new SpellSlotSpentFact(op.ActionOpId, op.Actor, cost.Pool, cost.Amount, remaining)
            );
            return ActionValidationResult.Valid;
        }

        private static ActionValidationResult SpendFocusPoints(
            CommitActionCostsOp op,
            FocusPointRuleCost cost,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!state.FocusPoints.TryGet(op.Actor, out FocusPointState focus))
                return ActionValidationResult.Invalid(
                    "The actor has no authoritative Focus Point pool."
                );
            if (focus.Current < cost.Amount)
                return ActionValidationResult.Invalid("The actor has insufficient Focus Points.");

            int remaining = focus.Current - cost.Amount;
            state.FocusPoints.Set(op.Actor, new FocusPointState(remaining, focus.Maximum));
            facts.Stage(new FocusPointsSpentFact(op.ActionOpId, op.Actor, cost.Amount, remaining));
            return ActionValidationResult.Valid;
        }

        private static ActionValidationResult SpendAmmunition(
            CommitActionCostsOp op,
            AmmunitionRuleCost cost,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!state.Ammunition.TryGet(cost.Item, out AmmunitionState ammunition))
                return ActionValidationResult.Invalid("The required ammunition is unavailable.");
            if (ammunition.Owner != op.Actor)
                return ActionValidationResult.Invalid(
                    "The acting creature does not own the ammunition."
                );
            if (ammunition.Remaining < cost.Amount)
                return ActionValidationResult.Invalid(
                    "There is insufficient ammunition remaining."
                );

            int remaining = ammunition.Remaining - cost.Amount;
            state.Ammunition.Set(
                cost.Item,
                new AmmunitionState(ammunition.Item, ammunition.Owner, remaining)
            );
            facts.Stage(
                new AmmunitionSpentFact(op.ActionOpId, op.Actor, cost.Item, cost.Amount, remaining)
            );
            return ActionValidationResult.Valid;
        }

        private static ActionValidationResult SpendFrequency(
            CommitActionCostsOp op,
            OncePerRoundRuleCost cost,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !state.RuleBindings.TryGet(cost.Binding, out ActiveRuleBinding binding)
                || !binding.IsEnabled
            )
            {
                return ActionValidationResult.Invalid(
                    "The once-per-round rule binding is not active."
                );
            }
            if (binding.Owner != op.Actor)
            {
                return ActionValidationResult.Invalid(
                    "The acting creature is not authorized by the frequency binding."
                );
            }

            EncounterState[] actorEncounters = state
                .Encounters.Select(pair => pair.Value)
                .Where(value =>
                    value.Phase == EncounterPhase.Active
                    && value.Roster.Any(entry => entry.Creature == op.Actor)
                )
                .ToArray();
            if (actorEncounters.Length != 1)
                return ActionValidationResult.Invalid(
                    "A once-per-round cost requires exactly one active encounter for the actor."
                );
            EncounterState encounter = actorEncounters[0];
            bool hasCurrent = state.Frequencies.TryGet(cost.Binding, out FrequencyState existing);
            int currentRoundUses =
                hasCurrent
                && existing.Encounter == encounter.Id
                && existing.Round == encounter.Round.Value
                    ? existing.Uses
                    : 0;
            if (currentRoundUses >= 1)
                return ActionValidationResult.Invalid(
                    "The once-per-round use has already been spent."
                );

            FrequencyState spent = new FrequencyState(
                encounter.Id,
                encounter.Round.Value,
                currentRoundUses + 1
            );
            state.Frequencies.Set(cost.Binding, spent);
            facts.Stage(
                new BindingFrequencySpentFact(
                    op.ActionOpId,
                    op.Actor,
                    cost.Binding,
                    spent.Encounter,
                    spent.Round,
                    spent.Uses
                )
            );
            return ActionValidationResult.Valid;
        }
    }

    internal sealed class InterruptReceiptedActionReducer
        : IOpReducer<InterruptReceiptedActionOp, ActionStartOutcome>
    {
        public ReductionResult<ActionStartOutcome> Reduce(
            ReductionContext<InterruptReceiptedActionOp> context,
            RulesStateDraft state,
            FactSink facts
        ) =>
            ActionReceiptReduction.TryInterrupt(state, facts, context.Op.Operation)
                ? ReductionResult<ActionStartOutcome>.Accept(ActionStartOutcome.Interrupted)
                : ReductionResult<ActionStartOutcome>.Reject(
                    ActionReceiptReduction.NotPendingReason
                );
    }
}
