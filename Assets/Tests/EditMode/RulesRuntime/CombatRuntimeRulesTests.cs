using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using NUnit.Framework;

namespace Game.Tests.EditMode.RulesRuntime
{
    public sealed class CombatRuntimeRulesTests
    {
        private static readonly CreatureId Actor = new CreatureId("actor");
        private static readonly CreatureId Other = new CreatureId("other");

        [Test]
        public async Task TurnAndLegacyCostsShareAuthoritativeActionEconomy()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new RulesStateSeed()
                    .SeedActionEconomy(Actor, new ActionEconomyState(0, false))
                    .SeedActionEconomy(Other, new ActionEconomyState(0, true))
            );

            AssertSuccess(await dispatcher.Dispatch(new BeginCombatTurnOp(Actor, 3)));
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ReactionAvailable, Is.True);
            Assert.That(dispatcher.Snapshot.ActionEconomy[Other].ReactionAvailable, Is.True);
            AssertSuccess(await dispatcher.Dispatch(new SpendLegacyActionsOp(Actor, 1)));
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
            AssertSuccess(await dispatcher.Dispatch(new EndCombatTurnOp(Actor)));
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.Zero);
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ReactionAvailable, Is.True);
        }

        [Test]
        public async Task ReinforcementRegistrationAddsCompleteSharedStateSlice()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new RulesStateSeed());
            ActiveRuleBinding initialBinding = new ActiveRuleBinding(
                new BindingId("actor-feature"),
                new RuleDefinitionId("actor-feature"),
                Actor,
                default,
                RuleSource.FromSlug("actor-feature"),
                0
            );
            CombatantRulesState combatant = new CombatantRulesState(
                new CreatureState(Actor, new PlayerId("party")),
                new HealthState(8, 10),
                new GridPosition(2, 0, 3),
                new GridDistance(25),
                new[] { initialBinding }
            );

            AssertSuccess(await dispatcher.Dispatch(new RegisterCombatantOp(combatant)));

            Assert.That(dispatcher.Snapshot.Creatures.Contains(Actor), Is.True);
            Assert.That(dispatcher.Snapshot.Health[Actor], Is.EqualTo(new HealthState(8, 10)));
            Assert.That(
                dispatcher.Snapshot.Positions[Actor],
                Is.EqualTo(new GridPosition(2, 0, 3))
            );
            Assert.That(dispatcher.Snapshot.LandSpeeds[Actor], Is.EqualTo(new GridDistance(25)));
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.Zero);
            Assert.That(
                dispatcher.Snapshot.RuleBindings[initialBinding.Id],
                Is.EqualTo(initialBinding)
            );
        }

        [Test]
        public async Task InitiativeAndEncounterNotificationsCommitTypedFacts()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new RulesStateSeed().SeedCreature(new CreatureState(Actor, new PlayerId("party")))
            );

            ResolvedOpResult<CombatRuntimeOutcome> initiative =
                (ResolvedOpResult<CombatRuntimeOutcome>)
                    await dispatcher.Dispatch(new InitiativeRolledOp(Actor));
            ResolvedOpResult<CombatRuntimeOutcome> encounter =
                (ResolvedOpResult<CombatRuntimeOutcome>)
                    await dispatcher.Dispatch(new EncounterEndedOp(Actor));

            Assert.That(initiative.Facts, Has.Exactly(1).TypeOf<InitiativeRolledFact>());
            Assert.That(encounter.Facts, Has.Exactly(1).TypeOf<EncounterEndedFact>());
        }

        [Test]
        public async Task CombatRuntimeCompositionAdvancesMapWithoutStrikeRules()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new RulesStateSeed()
                    .SeedCreature(new CreatureState(Actor, new PlayerId("party")))
                    .SeedMultipleAttackPenalty(Actor, new MultipleAttackPenaltyState(0))
            );

            OpResult<MultipleAttackPenaltyState> result = await dispatcher.Dispatch(
                new AdvanceMultipleAttackPenaltyOp(Actor)
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<MultipleAttackPenaltyState>>());
            Assert.That(
                ((ResolvedOpResult<MultipleAttackPenaltyState>)result).Value.AttackCount,
                Is.EqualTo(1)
            );
            Assert.That(
                dispatcher.Snapshot.MultipleAttackPenalty[Actor].AttackCount,
                Is.EqualTo(1)
            );
            Assert.That(result.Facts, Has.Exactly(1).TypeOf<MultipleAttackPenaltyAdvancedFact>());
        }

        private static RuleDispatcher CreateDispatcher(RulesStateSeed seed) =>
            new RuleDispatcherBuilder(new InMemoryRulesStore(seed)).UseCombatRuntimeRules().Build();

        private static void AssertSuccess(OpResult<CombatRuntimeOutcome> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<CombatRuntimeOutcome>>());
            Assert.That(((ResolvedOpResult<CombatRuntimeOutcome>)result).Value.Succeeded, Is.True);
        }
    }
}
