using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Provides the complete initial rules state for one encounter participant.</summary>
    public sealed class CombatantRulesState
    {
        /// <summary>Creates one immutable participant registration.</summary>
        public CombatantRulesState(
            CreatureState creature,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed,
            IReadOnlyList<SpellSlotState> spellSlots,
            IReadOnlyList<ActiveRuleBinding> ruleBindings
        )
        {
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            SpellSlots = CopyOwned(spellSlots, creature.Id);
            RuleBindings = CopyOwned(ruleBindings, creature.Id);
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

        /// <summary>Gets participant-owned initial spell-slot pools.</summary>
        public IReadOnlyList<SpellSlotState> SpellSlots { get; }

        /// <summary>Gets participant-owned rule bindings activated by registration.</summary>
        public IReadOnlyList<ActiveRuleBinding> RuleBindings { get; }

        private static IReadOnlyList<SpellSlotState> CopyOwned(
            IReadOnlyList<SpellSlotState> values,
            CreatureId owner
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Any(value => value.Owner != owner))
                throw new ArgumentException(
                    "Every spell-slot pool must be owned by the combatant."
                );
            if (values.Select(value => value.Id).Distinct().Count() != values.Count)
                throw new ArgumentException("Spell-slot pool IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }

        private static IReadOnlyList<ActiveRuleBinding> CopyOwned(
            IReadOnlyList<ActiveRuleBinding> values,
            CreatureId owner
        )
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Any(value => value == null || value.Owner != owner))
                throw new ArgumentException("Every rule binding must be owned by the combatant.");
            if (values.Select(value => value.Id).Distinct().Count() != values.Count)
                throw new ArgumentException("Rule binding IDs must be unique.");
            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>Registers a reinforcement in the existing authoritative rules store.</summary>
    public sealed class RegisterCombatantOp : IRuleOp<CombatRuntimeOutcome>
    {
        /// <summary>Creates a participant-registration request.</summary>
        public RegisterCombatantOp(CombatantRulesState combatant) =>
            Combatant = combatant ?? throw new ArgumentNullException(nameof(combatant));

        /// <summary>Gets the participant state to register.</summary>
        public CombatantRulesState Combatant { get; }
    }

    /// <summary>Reports whether one combatant registration committed.</summary>
    public readonly struct CombatRuntimeOutcome
    {
        private CombatRuntimeOutcome(bool succeeded, string reason)
        {
            Succeeded = succeeded;
            Reason = reason;
        }

        /// <summary>Gets whether registration committed.</summary>
        public bool Succeeded { get; }

        /// <summary>Gets an empty string on success or the rejection reason.</summary>
        public string Reason { get; }

        internal static CombatRuntimeOutcome Success =>
            new CombatRuntimeOutcome(true, string.Empty);

        internal static CombatRuntimeOutcome Rejected(string reason) =>
            new CombatRuntimeOutcome(false, reason);
    }

    /// <summary>Proves that one combatant was added atomically to the shared store.</summary>
    public sealed class CombatantRegisteredFact : RuleFact
    {
        internal CombatantRegisteredFact(CreatureId creature) => Creature = creature;

        /// <summary>Gets the registered combatant.</summary>
        public CreatureId Creature { get; }
    }

    /// <summary>Registers the same-store participant transition used before encounter joining.</summary>
    public static class CombatRuntimeRuleDispatcherExtensions
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("combatant-registration");

        /// <summary>Adds atomic participant registration to a dispatcher composition.</summary>
        public static RuleDispatcherBuilder UseCombatRuntimeRules(
            this RuleDispatcherBuilder builder
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            return builder
                .RegisterHandler<RegisterCombatantOp, CombatRuntimeOutcome>(
                    new RegisterCombatantHandler()
                )
                .RegisterEngineReducer<CommitCombatantRegistrationOp, CombatRuntimeOutcome>(
                    new RegisterCombatantReducer(),
                    Source
                )
                .UseMultipleAttackPenaltyRules();
        }
    }

    internal sealed class RegisterCombatantHandler
        : IOpHandler<RegisterCombatantOp, CombatRuntimeOutcome>
    {
        public async ValueTask<CombatRuntimeOutcome> Handle(
            OpFrame<RegisterCombatantOp> frame,
            OpHandlerContext context
        )
        {
            OpResult<CombatRuntimeOutcome> result = await context.Dispatch(
                new CommitCombatantRegistrationOp(frame.Op.Combatant)
            );
            return result is ResolvedOpResult<CombatRuntimeOutcome> resolved
                ? resolved.Value
                : throw new InvalidOperationException("Combatant registration did not resolve.");
        }
    }

    internal sealed class CommitCombatantRegistrationOp : IRuleOp<CombatRuntimeOutcome>
    {
        public CommitCombatantRegistrationOp(CombatantRulesState combatant) =>
            Combatant = combatant;

        public CombatantRulesState Combatant { get; }
    }

    internal sealed class RegisterCombatantReducer
        : IOpReducer<CommitCombatantRegistrationOp, CombatRuntimeOutcome>
    {
        public ReductionResult<CombatRuntimeOutcome> Reduce(
            ReductionContext<CommitCombatantRegistrationOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            CombatantRulesState combatant = context.Op.Combatant;
            CreatureId id = combatant.Creature.Id;
            if (
                state.Creatures.Contains(id)
                || state.Health.Contains(id)
                || state.Positions.Contains(id)
                || state.LandSpeeds.Contains(id)
                || state.ActionEconomy.Contains(id)
                || combatant.SpellSlots.Any(slot => state.SpellSlots.Contains(slot.Id))
                || combatant.RuleBindings.Any(binding => state.RuleBindings.Contains(binding.Id))
            )
                return ReductionResult<CombatRuntimeOutcome>.Accept(
                    CombatRuntimeOutcome.Rejected("The combatant is already registered.")
                );

            state.Creatures.Set(id, combatant.Creature);
            state.Health.Set(id, combatant.Health);
            state.Positions.Set(id, combatant.Position);
            state.LandSpeeds.Set(id, combatant.LandSpeed);
            state.ActionEconomy.Set(id, new ActionEconomyState(0, false));
            state.MultipleAttackPenalty.Set(id, new MultipleAttackPenaltyState(0));
            foreach (SpellSlotState slot in combatant.SpellSlots)
                state.SpellSlots.Set(slot.Id, slot);
            foreach (ActiveRuleBinding binding in combatant.RuleBindings)
                state.RuleBindings.Set(binding.Id, binding);
            facts.Stage(new CombatantRegisteredFact(id));
            return ReductionResult<CombatRuntimeOutcome>.Accept(CombatRuntimeOutcome.Success);
        }
    }
}
