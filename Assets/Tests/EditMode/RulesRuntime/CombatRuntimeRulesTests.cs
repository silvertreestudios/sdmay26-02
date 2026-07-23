using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using NUnit.Framework;

namespace Game.Tests.EditMode.RulesRuntime
{
    public sealed class CombatRuntimeRulesTests
    {
        private static readonly CreatureId Actor = new CreatureId("actor");

        [Test]
        public async Task TurnAndLegacyCostsShareAuthoritativeActionEconomy()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(0, false))
            );

            AssertSuccess(await dispatcher.Dispatch(new BeginCombatTurnOp(Actor, 3)));
            AssertSuccess(await dispatcher.Dispatch(new SpendLegacyActionsOp(Actor, 1)));
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
            AssertSuccess(await dispatcher.Dispatch(new EndCombatTurnOp(Actor)));
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.Zero);
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ReactionAvailable, Is.False);
        }

        [Test]
        public async Task ReinforcementRegistrationAddsOnlyTheStrideStateSlice()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new RulesStateSeed());
            CombatantRulesState combatant = new CombatantRulesState(
                new CreatureState(Actor, new PlayerId("party")),
                new HealthState(8, 10),
                new GridPosition(2, 0, 3),
                new GridDistance(25)
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
