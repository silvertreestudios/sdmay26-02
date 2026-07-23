using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Provides the complete initial rules state for one encounter participant.</summary>
    public sealed class CombatantRulesState
    {
        /// <summary>Creates one immutable participant registration.</summary>
        /// <param name="creature">The stable creature identity and controlling side.</param>
        /// <param name="health">The participant's current authoritative health.</param>
        /// <param name="position">The participant's current grid position.</param>
        /// <param name="landSpeed">The participant's land Speed.</param>
        public CombatantRulesState(
            CreatureState creature,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed
        )
        {
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            Health = health;
            Position = position;
            LandSpeed = landSpeed;
        }

        /// <summary>Gets the participant's stable identity and controlling side.</summary>
        public CreatureState Creature { get; }

        /// <summary>Gets the participant's health at registration time.</summary>
        public HealthState Health { get; }

        /// <summary>Gets the participant's grid position at registration time.</summary>
        public GridPosition Position { get; }

        /// <summary>Gets the participant's authoritative land Speed.</summary>
        public GridDistance LandSpeed { get; }
    }

    /// <summary>Registers a reinforcement without replacing the encounter rules store.</summary>
    public sealed class RegisterCombatantOp : IRuleOp<CombatRuntimeOutcome>
    {
        /// <summary>Creates a participant-registration request.</summary>
        /// <param name="combatant">The complete immutable state to add.</param>
        public RegisterCombatantOp(CombatantRulesState combatant) =>
            Combatant = combatant ?? throw new ArgumentNullException(nameof(combatant));

        /// <summary>Gets the participant state to register.</summary>
        public CombatantRulesState Combatant { get; }
    }

    /// <summary>Starts one scheduled combat turn with an authoritative action count.</summary>
    public sealed class BeginCombatTurnOp : IRuleOp<CombatRuntimeOutcome>
    {
        /// <summary>Creates a turn-start request.</summary>
        /// <param name="actor">The creature receiving turn authority.</param>
        /// <param name="actions">The non-negative action count after start-of-turn modifiers.</param>
        public BeginCombatTurnOp(CreatureId actor, int actions)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A turn actor is required.", nameof(actor));
            if (actions < 0)
                throw new ArgumentOutOfRangeException(nameof(actions));
            Actor = actor;
            Actions = actions;
        }

        /// <summary>Gets the creature receiving turn authority.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the number of actions granted.</summary>
        public int Actions { get; }
    }

    /// <summary>Ends one creature's scheduled combat turn.</summary>
    public sealed class EndCombatTurnOp : IRuleOp<CombatRuntimeOutcome>
    {
        /// <summary>Creates a turn-end request.</summary>
        /// <param name="actor">The creature losing turn authority.</param>
        public EndCombatTurnOp(CreatureId actor)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A turn actor is required.", nameof(actor));
            Actor = actor;
        }

        /// <summary>Gets the creature losing turn authority.</summary>
        public CreatureId Actor { get; }
    }

    /// <summary>Spends actions for a legacy feature that has not moved to an action operation.</summary>
    public sealed class SpendLegacyActionsOp : IRuleOp<CombatRuntimeOutcome>
    {
        /// <summary>Creates a legacy action-cost request.</summary>
        /// <param name="actor">The creature paying the cost.</param>
        /// <param name="amount">The positive number of actions to spend.</param>
        public SpendLegacyActionsOp(CreatureId actor, int amount)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("An action actor is required.", nameof(actor));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Actor = actor;
            Amount = amount;
        }

        /// <summary>Gets the creature paying the cost.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the positive action count to spend.</summary>
        public int Amount { get; }
    }

    /// <summary>Reports whether a combat-runtime state transition committed.</summary>
    public sealed class CombatRuntimeOutcome
    {
        private CombatRuntimeOutcome(bool succeeded, string reason)
        {
            Succeeded = succeeded;
            Reason = reason;
        }

        /// <summary>Gets whether the requested state transition committed.</summary>
        public bool Succeeded { get; }

        /// <summary>Gets an empty string on success or the rejection reason.</summary>
        public string Reason { get; }

        internal static CombatRuntimeOutcome Success { get; } =
            new CombatRuntimeOutcome(true, string.Empty);

        internal static CombatRuntimeOutcome Rejected(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A rejection reason is required.", nameof(reason));
            return new CombatRuntimeOutcome(false, reason);
        }
    }

    /// <summary>Identifies the shared combat-state transition proven by a committed Fact.</summary>
    public enum CombatRuntimeChangeKind
    {
        /// <summary>A new encounter participant was registered.</summary>
        CombatantRegistered,

        /// <summary>A scheduled turn granted actions and reset transient movement state.</summary>
        TurnBegan,

        /// <summary>A scheduled turn cleared actions and transient movement state.</summary>
        TurnEnded,

        /// <summary>A legacy feature spent actions through the shared store.</summary>
        LegacyActionsSpent,
    }

    /// <summary>Proves that one minimal shared combat-state transition committed.</summary>
    public sealed class CombatRuntimeChangedFact : RuleFact
    {
        internal CombatRuntimeChangedFact(CreatureId creature, CombatRuntimeChangeKind kind)
        {
            if (creature.IsEmpty)
                throw new ArgumentException("A changed creature is required.", nameof(creature));
            Creature = creature;
            Kind = kind;
        }

        /// <summary>Gets the participant whose registration, turn, or actions changed.</summary>
        public CreatureId Creature { get; }

        /// <summary>Gets the committed transition category.</summary>
        public CombatRuntimeChangeKind Kind { get; }
    }

    /// <summary>Registers the minimum shared encounter-state transitions used by Unity.</summary>
    public static class CombatRuntimeRuleDispatcherExtensions
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("combat-runtime");

        /// <summary>Adds participant, turn-boundary, and legacy-cost operations.</summary>
        /// <param name="builder">The dispatcher builder being composed.</param>
        /// <returns>The supplied builder for fluent composition.</returns>
        public static RuleDispatcherBuilder UseCombatRuntimeRules(
            this RuleDispatcherBuilder builder
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            return builder
                .RegisterHandler<RegisterCombatantOp, CombatRuntimeOutcome>(
                    new CombatRuntimeRootHandler<RegisterCombatantOp>()
                )
                .RegisterHandler<BeginCombatTurnOp, CombatRuntimeOutcome>(
                    new CombatRuntimeRootHandler<BeginCombatTurnOp>()
                )
                .RegisterHandler<EndCombatTurnOp, CombatRuntimeOutcome>(
                    new CombatRuntimeRootHandler<EndCombatTurnOp>()
                )
                .RegisterHandler<SpendLegacyActionsOp, CombatRuntimeOutcome>(
                    new CombatRuntimeRootHandler<SpendLegacyActionsOp>()
                )
                .RegisterEngineReducer<CommitCombatRuntimeOp, CombatRuntimeOutcome>(
                    new CommitCombatRuntimeReducer(),
                    Source
                );
        }
    }

    internal sealed class CombatRuntimeRootHandler<TOp> : IOpHandler<TOp, CombatRuntimeOutcome>
        where TOp : IRuleOp<CombatRuntimeOutcome>
    {
        public async ValueTask<CombatRuntimeOutcome> Handle(
            OpFrame<TOp> frame,
            OpHandlerContext context
        )
        {
            OpResult<CombatRuntimeOutcome> result = await context.Dispatch(
                new CommitCombatRuntimeOp(frame.Op)
            );
            if (result is ResolvedOpResult<CombatRuntimeOutcome> resolved)
                return resolved.Value;
            throw new InvalidOperationException(
                "A combat-runtime state transition did not resolve."
            );
        }
    }

    internal sealed class CommitCombatRuntimeOp : IRuleOp<CombatRuntimeOutcome>
    {
        public CommitCombatRuntimeOp(IRuleOp<CombatRuntimeOutcome> requested) =>
            Requested = requested ?? throw new ArgumentNullException(nameof(requested));

        public IRuleOp<CombatRuntimeOutcome> Requested { get; }
    }

    internal sealed class CommitCombatRuntimeReducer
        : IOpReducer<CommitCombatRuntimeOp, CombatRuntimeOutcome>
    {
        public ReductionResult<CombatRuntimeOutcome> Reduce(
            ReductionContext<CommitCombatRuntimeOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            CombatRuntimeOutcome outcome = context.Op.Requested switch
            {
                RegisterCombatantOp register => Register(register.Combatant, state),
                BeginCombatTurnOp begin => BeginTurn(begin, state),
                EndCombatTurnOp end => EndTurn(end.Actor, state),
                SpendLegacyActionsOp spend => Spend(spend, state),
                _ => CombatRuntimeOutcome.Rejected("Unknown combat-runtime transition."),
            };
            if (outcome.Succeeded)
                facts.Stage(CreateFact(context.Op.Requested));
            return ReductionResult<CombatRuntimeOutcome>.Accept(outcome);
        }

        private static CombatRuntimeChangedFact CreateFact(
            IRuleOp<CombatRuntimeOutcome> requested
        ) =>
            requested switch
            {
                RegisterCombatantOp register => new CombatRuntimeChangedFact(
                    register.Combatant.Creature.Id,
                    CombatRuntimeChangeKind.CombatantRegistered
                ),
                BeginCombatTurnOp begin => new CombatRuntimeChangedFact(
                    begin.Actor,
                    CombatRuntimeChangeKind.TurnBegan
                ),
                EndCombatTurnOp end => new CombatRuntimeChangedFact(
                    end.Actor,
                    CombatRuntimeChangeKind.TurnEnded
                ),
                SpendLegacyActionsOp spend => new CombatRuntimeChangedFact(
                    spend.Actor,
                    CombatRuntimeChangeKind.LegacyActionsSpent
                ),
                _ => throw new InvalidOperationException(
                    "A successful combat-runtime transition requires a known Fact payload."
                ),
            };

        private static CombatRuntimeOutcome Register(
            CombatantRulesState combatant,
            RulesStateDraft state
        )
        {
            CreatureId id = combatant.Creature.Id;
            if (
                state.Creatures.Contains(id)
                || state.Health.Contains(id)
                || state.Positions.Contains(id)
                || state.LandSpeeds.Contains(id)
                || state.ActionEconomy.Contains(id)
            )
            {
                return CombatRuntimeOutcome.Rejected("The combatant is already registered.");
            }

            state.Creatures.Set(id, combatant.Creature);
            state.Health.Set(id, combatant.Health);
            state.Positions.Set(id, combatant.Position);
            state.LandSpeeds.Set(id, combatant.LandSpeed);
            state.ActionEconomy.Set(id, new ActionEconomyState(0, false));
            return CombatRuntimeOutcome.Success;
        }

        private static CombatRuntimeOutcome BeginTurn(BeginCombatTurnOp op, RulesStateDraft state)
        {
            if (!state.ActionEconomy.Contains(op.Actor))
                return CombatRuntimeOutcome.Rejected("The turn actor is not registered.");

            List<CreatureId> participants = new List<CreatureId>();
            foreach (KeyValuePair<CreatureId, ActionEconomyState> pair in state.ActionEconomy)
                participants.Add(pair.Key);
            foreach (CreatureId participant in participants)
            {
                state.ActionEconomy.Set(participant, new ActionEconomyState(0, false));
                state.MovementBudgets.Remove(participant);
            }
            state.ActionEconomy.Set(op.Actor, new ActionEconomyState(op.Actions, true));
            return CombatRuntimeOutcome.Success;
        }

        private static CombatRuntimeOutcome EndTurn(CreatureId actor, RulesStateDraft state)
        {
            if (!state.ActionEconomy.Contains(actor))
                return CombatRuntimeOutcome.Rejected("The turn actor is not registered.");
            state.ActionEconomy.Set(actor, new ActionEconomyState(0, false));
            state.MovementBudgets.Remove(actor);
            return CombatRuntimeOutcome.Success;
        }

        private static CombatRuntimeOutcome Spend(SpendLegacyActionsOp op, RulesStateDraft state)
        {
            if (!state.ActionEconomy.TryGet(op.Actor, out ActionEconomyState economy))
                return CombatRuntimeOutcome.Rejected("The action actor is not registered.");
            if (economy.ActionsRemaining < op.Amount)
                return CombatRuntimeOutcome.Rejected("The actor cannot afford the action cost.");
            state.ActionEconomy.Set(
                op.Actor,
                new ActionEconomyState(
                    economy.ActionsRemaining - op.Amount,
                    economy.ReactionAvailable
                )
            );
            return CombatRuntimeOutcome.Success;
        }
    }
}
