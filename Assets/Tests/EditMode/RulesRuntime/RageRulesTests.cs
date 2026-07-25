using System;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using NUnit.Framework;

namespace Game.Tests.EditMode.RulesRuntime
{
    public sealed class RageRulesTests
    {
        private static readonly CreatureId Actor = new CreatureId("actor");
        private static readonly PlayerId Party = new PlayerId("party");

        [Test]
        public void ActionProfilesKeepQuickTemperedTraitsDistinctFromRage()
        {
            RageActionDefinition definition = new RageActionDefinition(
                new TestRageActorStateProvider(CreateActorState())
            );

            ActionProfile rage = definition.GetBaseProfile(RageActionDefinition.DefinitionId);
            ActionProfile quickTempered = definition.GetBaseProfile(
                RageActionDefinition.QuickTemperedDefinitionId
            );

            Assert.That(
                rage.Traits.Select(trait => trait.Slug),
                Is.EquivalentTo(new[] { "barbarian", "concentrate", "emotion", "mental" })
            );
            Assert.That(
                quickTempered.Traits.Select(trait => trait.Slug),
                Is.EqualTo(new[] { "barbarian" })
            );
            Assert.That(quickTempered.Cost, Is.EqualTo(ActionCost.FreeAction));
        }

        [Test]
        public void RestoreNormalizationEndsAnOrphanedRagePool()
        {
            HealthState restored = RageRules.NormalizeRestoredHealth(
                new HealthState(7, 10, 3, RuleSource.FromSlug("rage"), Array.Empty<RuleSource>()),
                rageWasActive: true
            );

            Assert.That(restored.Current, Is.EqualTo(7));
            Assert.That(restored.Temporary, Is.Zero);
            Assert.That(restored.TemporarySource.IsEmpty, Is.True);
            Assert.That(
                restored.HasTemporaryHitPointImmunity(RuleSource.FromSlug("rage")),
                Is.True
            );
        }

        [Test]
        public void RestoreNormalizationRecordsEndedRageAfterItsPoolWasConsumed()
        {
            HealthState restored = RageRules.NormalizeRestoredHealth(
                new HealthState(7, 10),
                rageWasActive: true
            );

            Assert.That(restored.Temporary, Is.Zero);
            Assert.That(
                restored.HasTemporaryHitPointImmunity(RuleSource.FromSlug("rage")),
                Is.True
            );
        }

        [Test]
        public async Task OrdinaryRageOwnsActionCostEffectAndTemporaryHitPoints()
        {
            TestRageActorStateProvider provider = new TestRageActorStateProvider(
                CreateActorState()
            );
            RuleDispatcher dispatcher = CreateDispatcher(provider);

            ResolvedOpResult<RageStartOutcome> result = RequireResolved(
                await dispatcher.Dispatch(new RageActionOp(Actor))
            );

            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.EqualTo(3));
            Assert.That(
                dispatcher.Snapshot.Health[Actor].TemporarySource,
                Is.EqualTo(RageRules.Source)
            );
            Assert.That(result.Value.TemporaryHitPointsGranted, Is.True);
            Assert.That(result.Value.TemporaryHitPoints, Is.EqualTo(3));
            Assert.That(result.Value.StartedByQuickTempered, Is.False);
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<ActiveEffectCreatedFact>().Count(), Is.EqualTo(1));
            Assert.That(
                result.Facts.OfType<TemporaryHitPointsGrantedFact>().Count(),
                Is.EqualTo(1)
            );

            OpResult<RageStartOutcome> duplicate = await dispatcher.Dispatch(
                new RageActionOp(Actor)
            );
            Assert.That(duplicate, Is.TypeOf<InvalidOpResult<RageStartOutcome>>());
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
        }

        [Test]
        public async Task OrdinaryRageIgnoresQuickTemperedMovementRequirements()
        {
            TestRageActorStateProvider provider = new TestRageActorStateProvider(
                CreateActorState(isEncumbered: true, wearsHeavyArmor: true)
            );
            RuleDispatcher dispatcher = CreateDispatcher(provider);

            OpResult<RageStartOutcome> result = await dispatcher.Dispatch(new RageActionOp(Actor));

            Assert.That(result, Is.TypeOf<ResolvedOpResult<RageStartOutcome>>());
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
        }

        [Test]
        public async Task FatiguedOrUnownedRageIsRejectedBeforeCost()
        {
            TestRageActorStateProvider fatiguedProvider = new TestRageActorStateProvider(
                CreateActorState(isFatigued: true)
            );
            RuleDispatcher fatigued = CreateDispatcher(fatiguedProvider);
            TestRageActorStateProvider unownedProvider = new TestRageActorStateProvider(
                CreateActorState(ownsRage: false)
            );
            RuleDispatcher unowned = CreateDispatcher(unownedProvider);

            Assert.That(
                await fatigued.Dispatch(new RageActionOp(Actor)),
                Is.TypeOf<InvalidOpResult<RageStartOutcome>>()
            );
            Assert.That(
                await unowned.Dispatch(new RageActionOp(Actor)),
                Is.TypeOf<InvalidOpResult<RageStartOutcome>>()
            );
            Assert.That(fatigued.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(unowned.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
        }

        [Test]
        public async Task InitiativeFactOwnsQuickTemperedRequirementsAndOneShot()
        {
            TestRageActorStateProvider allowedProvider = new TestRageActorStateProvider(
                CreateActorState()
            );
            RuleDispatcher allowed = CreateDispatcher(allowedProvider);
            RuleDispatcher encumbered = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState(isEncumbered: true))
            );
            RuleDispatcher heavy = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState(wearsHeavyArmor: true))
            );
            RuleDispatcher armoredException = CreateDispatcher(
                new TestRageActorStateProvider(
                    CreateActorState(wearsHeavyArmor: true, hasInvulnerableRager: true)
                )
            );

            await allowed.Dispatch(new InitiativeRolledOp(Actor));
            await encumbered.Dispatch(new InitiativeRolledOp(Actor));
            await heavy.Dispatch(new InitiativeRolledOp(Actor));
            await armoredException.Dispatch(new InitiativeRolledOp(Actor));

            Assert.That(RageRules.IsRaging(allowed.Snapshot, Actor), Is.True);
            Assert.That(allowed.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(RageRules.IsRaging(encumbered.Snapshot, Actor), Is.False);
            Assert.That(RageRules.IsRaging(heavy.Snapshot, Actor), Is.False);
            Assert.That(RageRules.IsRaging(armoredException.Snapshot, Actor), Is.True);

            await allowed.Dispatch(new EndRageOp(Actor));
            await allowed.Dispatch(new InitiativeRolledOp(Actor));
            Assert.That(
                RageRules.IsRaging(allowed.Snapshot, Actor),
                Is.False,
                "The consumed Quick-Tempered binding must not react twice."
            );
            Assert.That(
                await allowed.Dispatch(new RageActionOp(Actor)),
                Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
            );
            Assert.That(allowed.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
        }

        [Test]
        public async Task EncounterEndedFactLetsRageOwnItsCleanup()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState())
            );
            await dispatcher.Dispatch(new RageActionOp(Actor));

            OpResult<CombatRuntimeOutcome> result = await dispatcher.Dispatch(
                new EncounterEndedOp(Actor)
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CombatRuntimeOutcome>>());
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.Zero);
            Assert.That(
                dispatcher.Snapshot.Health[Actor].HasTemporaryHitPointImmunity(RageRules.Source),
                Is.True
            );
        }

        [Test]
        public async Task EndingRageRemovesItsStateAndPreventsLaterTemporaryHitPoints()
        {
            TestRageActorStateProvider provider = new TestRageActorStateProvider(
                CreateActorState()
            );
            RuleDispatcher dispatcher = CreateDispatcher(provider);
            await dispatcher.Dispatch(new RageActionOp(Actor));

            ResolvedOpResult<RageEndOutcome> ended = RequireResolved(
                await dispatcher.Dispatch(new EndRageOp(Actor))
            );
            ResolvedOpResult<RageStartOutcome> restarted = RequireResolved(
                await dispatcher.Dispatch(new RageActionOp(Actor))
            );

            Assert.That(ended.Value.Ended, Is.True);
            Assert.That(ended.Facts.OfType<TemporaryHitPointsRemovedFact>().Count(), Is.EqualTo(1));
            Assert.That(
                ended.Facts.OfType<TemporaryHitPointImmunityAddedFact>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(ended.Facts.OfType<ActiveEffectRemovedFact>().Count(), Is.EqualTo(1));
            Assert.That(restarted.Value.TemporaryHitPointsGranted, Is.False);
            Assert.That(restarted.Value.TemporaryHitPoints, Is.Zero);
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.Zero);
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
        }

        private static RuleDispatcher CreateDispatcher(IRageActorStateProvider provider)
        {
            RageActionDefinition definition = new RageActionDefinition(provider);
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Party))
                .SeedHealth(Actor, new HealthState(10, 10))
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true));
            foreach (
                ActiveRuleBinding binding in RageRules.CreateInitialBindings(
                    Actor,
                    provider.Get(Actor)
                )
            )
            {
                seed.SeedRuleBinding(binding);
            }

            return new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
                .UseHealthRules()
                .UseCombatRuntimeRules()
                .UseActiveEffectRules(registryBuilder.Build())
                .UseActionLifecycle(definition)
                .UseRageRules(definition)
                .Build();
        }

        private static RageActorState CreateActorState(
            bool ownsRage = true,
            bool ownsQuickTempered = true,
            bool isFatigued = false,
            bool isEncumbered = false,
            bool wearsHeavyArmor = false,
            bool hasInvulnerableRager = false
        ) =>
            new RageActorState(
                ownsRage,
                ownsQuickTempered,
                isFatigued,
                isEncumbered,
                wearsHeavyArmor,
                hasInvulnerableRager,
                1,
                2
            );

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            string failure = result is InvalidOpResult<TResult> invalid
                ? invalid.Reason
                : "The operation did not resolve.";
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>(), failure);
            return (ResolvedOpResult<TResult>)result;
        }

        private sealed class TestRageActorStateProvider : IRageActorStateProvider
        {
            private readonly RageActorState state;

            public TestRageActorStateProvider(RageActorState state) =>
                this.state = state ?? throw new ArgumentNullException(nameof(state));

            public RageActorState Get(CreatureId actor)
            {
                if (actor != Actor)
                    throw new InvalidOperationException("Unknown Rage test actor.");
                return state;
            }
        }
    }
}
