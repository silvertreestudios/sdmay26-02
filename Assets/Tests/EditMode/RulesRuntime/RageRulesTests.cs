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
        private static readonly CreatureId Enemy = new CreatureId("enemy");
        private static readonly PlayerId Party = new PlayerId("party");
        private static readonly PlayerId Opposition = new PlayerId("opposition");
        private static readonly EncounterId Encounter = new EncounterId("encounter");

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
        public async Task EncounterStartFactOwnsQuickTemperedRequirementsAndOneShot()
        {
            TestRageActorStateProvider allowedProvider = new TestRageActorStateProvider(
                CreateActorState(ownsQuickTempered: true)
            );
            RuleDispatcher allowed = CreateDispatcher(allowedProvider);
            RuleDispatcher encumbered = CreateDispatcher(
                new TestRageActorStateProvider(
                    CreateActorState(ownsQuickTempered: true, isEncumbered: true)
                )
            );
            RuleDispatcher heavy = CreateDispatcher(
                new TestRageActorStateProvider(
                    CreateActorState(ownsQuickTempered: true, wearsHeavyArmor: true)
                )
            );
            RuleDispatcher armoredException = CreateDispatcher(
                new TestRageActorStateProvider(
                    CreateActorState(
                        ownsQuickTempered: true,
                        wearsHeavyArmor: true,
                        hasInvulnerableRager: true
                    )
                )
            );

            Assert.That(RageRules.IsRaging(allowed.Snapshot, Actor), Is.True);
            Assert.That(allowed.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(RageRules.IsRaging(encumbered.Snapshot, Actor), Is.False);
            Assert.That(RageRules.IsRaging(heavy.Snapshot, Actor), Is.False);
            Assert.That(RageRules.IsRaging(armoredException.Snapshot, Actor), Is.True);

            await allowed.Dispatch(new EndRageOp(Actor));
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
        public void EncounterStartAppliesQuickTemperedBeforeHigherInitiativeTurn()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState(ownsQuickTempered: true)),
                new ScriptedRollService(1, 20),
                actorInitiativeModifier: 0,
                enemyInitiativeModifier: 0
            );

            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].CurrentTurn.Value.Actor,
                Is.EqualTo(Enemy)
            );
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.EqualTo(3));
            Assert.That(
                dispatcher.Snapshot.Health[Actor].TemporarySource,
                Is.EqualTo(RageRules.Source)
            );
        }

        [Test]
        public async Task EncounterEndedFactLetsRageOwnItsCleanup()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState())
            );
            await dispatcher.Dispatch(new RageActionOp(Actor));

            OpResult<DamageOutcome> result = await dispatcher.Dispatch(
                new ApplyDamageOp(
                    Enemy,
                    10,
                    new HealthChangeOriginId("finish"),
                    RuleSource.FromSlug("test")
                )
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<DamageOutcome>>());
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(EncounterPhase.Ended)
            );
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
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));

            ResolvedOpResult<RageEndOutcome> ended = RequireResolved(
                await dispatcher.Dispatch(new EndRageOp(Actor))
            );
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
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
            return CreateDispatcher(
                provider,
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0
            );
        }

        private static RuleDispatcher CreateDispatcher(
            IRageActorStateProvider provider,
            ScriptedRollService rolls,
            int actorInitiativeModifier,
            int enemyInitiativeModifier
        )
        {
            RageActionDefinition definition = new RageActionDefinition(provider);
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Party))
                .SeedCreature(new CreatureState(Enemy, Opposition))
                .SeedHealth(Actor, new HealthState(10, 10))
                .SeedHealth(Enemy, new HealthState(10, 10))
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

            registryBuilder.AddOutcomeRule();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                rolls
            )
                .UseHealthRules()
                .UseCombatRuntimeRules()
                .UseActiveEffectRules(registryBuilder.Build())
                .UseMovementBudgetResetRules()
                .UseEncounterRules()
                .UseActionLifecycle(definition)
                .UseRageRules(definition)
                .Build();
            RequireResolved(
                dispatcher
                    .Dispatch(
                        new StartEncounterOp(
                            Encounter,
                            Party,
                            new[]
                            {
                                new EncounterParticipant(Actor, Party, actorInitiativeModifier),
                                new EncounterParticipant(
                                    Enemy,
                                    Opposition,
                                    enemyInitiativeModifier
                                ),
                            }
                        )
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
            );
            return dispatcher;
        }

        private static RageActorState CreateActorState(
            bool ownsRage = true,
            bool ownsQuickTempered = false,
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
