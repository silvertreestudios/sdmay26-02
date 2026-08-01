using System;
using System.Collections.Generic;
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
        private static readonly CreatureId Reinforcement = new CreatureId("reinforcement");
        private static readonly PlayerId Party = new PlayerId("party");
        private static readonly PlayerId Opposition = new PlayerId("opposition");
        private static readonly EncounterId Encounter = new EncounterId("encounter");

        /// <summary>Identifies the post-commit boundary interrupted by a retry regression.</summary>
        public enum JoinRetryFailurePoint
        {
            /// <summary>Interrupts delivery immediately after the roster transaction commits.</summary>
            EncounterJoinedObserver,

            /// <summary>Interrupts root settlement after Quick-Tempered finishes its mutations.</summary>
            QuickTemperedRootSettlement,
        }

        [Test]
        public void ActionProfilesKeepQuickTemperedTraitsDistinctFromRage()
        {
            RageActionDefinition definition = new RageActionDefinition(
                new TestRageConditionStateProvider(CreateActorState())
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
            TestRageConditionStateProvider provider = new TestRageConditionStateProvider(
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
            TestRageConditionStateProvider provider = new TestRageConditionStateProvider(
                CreateActorState(wearsHeavyArmor: true)
            );
            RuleDispatcher dispatcher = CreateDispatcher(provider, isEncumbered: true);

            OpResult<RageStartOutcome> result = await dispatcher.Dispatch(new RageActionOp(Actor));

            Assert.That(result, Is.TypeOf<ResolvedOpResult<RageStartOutcome>>());
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
        }

        [Test]
        public async Task FatiguedOrUnownedRageIsRejectedBeforeCost()
        {
            TestRageConditionStateProvider fatiguedProvider = new TestRageConditionStateProvider(
                CreateActorState()
            );
            RuleDispatcher fatigued = CreateDispatcher(fatiguedProvider, isFatigued: true);
            TestRageConditionStateProvider unownedProvider = new TestRageConditionStateProvider(
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
        public async Task InitiativeAssignmentOwnsQuickTemperedRequirementsAndOneShot()
        {
            TestRageConditionStateProvider allowedProvider = new TestRageConditionStateProvider(
                CreateActorState(ownsQuickTempered: true)
            );
            RuleDispatcher allowed = CreateDispatcher(allowedProvider);
            RuleDispatcher encumbered = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState(ownsQuickTempered: true)),
                isEncumbered: true
            );
            RuleDispatcher heavy = CreateDispatcher(
                new TestRageConditionStateProvider(
                    CreateActorState(ownsQuickTempered: true, wearsHeavyArmor: true)
                )
            );
            RuleDispatcher armoredException = CreateDispatcher(
                new TestRageConditionStateProvider(
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

        [TestCase(JoinRetryFailurePoint.EncounterJoinedObserver)]
        [TestCase(JoinRetryFailurePoint.QuickTemperedRootSettlement)]
        public async Task ReinforcementJoinRetryConvergesToOneSuccessfulQuickTemperedWorkflow(
            JoinRetryFailurePoint failurePoint
        )
        {
            JoinRetrySignature expected = await RunQuickTemperedJoinScenario(null);
            JoinRetrySignature retried = await RunQuickTemperedJoinScenario(failurePoint);

            Assert.That(retried.Version, Is.EqualTo(expected.Version));
            Assert.That(retried.Encounter, Is.EqualTo(expected.Encounter));
            Assert.That(retried.Health, Is.EqualTo(expected.Health));
            Assert.That(retried.ActionEconomy, Is.EqualTo(expected.ActionEconomy));
            Assert.That(retried.AttackPenalty, Is.EqualTo(expected.AttackPenalty));
            Assert.That(retried.ActiveEffects, Is.EqualTo(expected.ActiveEffects));
            Assert.That(retried.RuleBindings, Is.EqualTo(expected.RuleBindings));
            Assert.That(retried.Facts, Is.EqualTo(expected.Facts));
        }

        [Test]
        public void EncounterStartAppliesQuickTemperedBeforeHigherInitiativeTurn()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState(ownsQuickTempered: true)),
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
        public void QuickTemperedSettlesBeforeWinningActorsFirstTurnStartAdapter()
        {
            RageTurnStartProbe adapter = new RageTurnStartProbe(Actor);

            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState(ownsQuickTempered: true)),
                new ScriptedRollService(20, 1),
                actorInitiativeModifier: 0,
                enemyInitiativeModifier: 0,
                turnStartAdapters: new[] { adapter }
            );

            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].CurrentTurn.Value.Actor,
                Is.EqualTo(Actor)
            );
            Assert.That(adapter.Calls, Is.EqualTo(1));
            Assert.That(adapter.WasRaging, Is.True);
            Assert.That(adapter.TemporaryHitPoints, Is.EqualTo(3));
        }

        [Test]
        public async Task RageExpirationCleansTemporaryHitPointsBeforeTenthTurnStartDamage()
        {
            TenthActorTurnDamageAdapter adapter = new TenthActorTurnDamageAdapter(Actor);
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState()),
                new ScriptedRollService(20, 1),
                actorInitiativeModifier: 0,
                enemyInitiativeModifier: 0,
                turnStartAdapters: new[] { adapter }
            );
            await dispatcher.Dispatch(new RageActionOp(Actor));
            adapter.Enable();

            EncounterState turn = dispatcher.Snapshot.Encounters[Encounter];
            for (int round = 0; round < 10; round++)
            {
                turn = RequireResolved(
                    await dispatcher.Dispatch(new EndTurnOp(turn.CurrentTurn.Value))
                ).Value.State;
                Assert.That(turn.CurrentTurn.Value.Actor, Is.EqualTo(Enemy));
                turn = RequireResolved(
                    await dispatcher.Dispatch(new EndTurnOp(turn.CurrentTurn.Value))
                ).Value.State;
                Assert.That(turn.CurrentTurn.Value.Actor, Is.EqualTo(Actor));
            }

            Assert.That(adapter.ActorTurnCalls, Is.EqualTo(10));
            Assert.That(adapter.TemporaryHitPointsBeforeDamage, Is.Zero);
            Assert.That(dispatcher.Snapshot.Health[Actor].Current, Is.EqualTo(9));
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.Zero);
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
        }

        /// <summary>
        /// Verifies timed expiration records Rage-source immunity after damage consumed the
        /// original temporary Hit Points.
        /// </summary>
        [Test]
        public async Task TimedRageExpiration_ConsumedPoolRecordsImmunity()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState())
            );
            await dispatcher.Dispatch(new RageActionOp(Actor));
            await dispatcher.Dispatch(
                new ApplyDamageOp(
                    Actor,
                    3,
                    new HealthChangeOriginId("consume-rage-pool"),
                    RuleSource.FromSlug("test")
                )
            );
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.Zero);

            await AdvanceRageToExpiration(dispatcher);

            HealthState expired = dispatcher.Snapshot.Health[Actor];
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(expired.Temporary, Is.Zero);
            Assert.That(expired.HasTemporaryHitPointImmunity(RageRules.Source), Is.True);
            ResolvedOpResult<RageStartOutcome> restarted = RequireResolved(
                await dispatcher.Dispatch(new RageActionOp(Actor))
            );
            Assert.That(restarted.Value.TemporaryHitPointsGranted, Is.False);
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.Zero);
        }

        /// <summary>
        /// Verifies timed expiration preserves foreign temporary Hit Points while preventing a
        /// later Rage from replacing that pool.
        /// </summary>
        [Test]
        public async Task TimedRageExpiration_ReplacedPoolPreservesForeignOwnerAndRecordsImmunity()
        {
            RuleSource otherSource = RuleSource.FromSlug("larger-temporary-hit-points");
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState())
            );
            await dispatcher.Dispatch(new RageActionOp(Actor));
            await dispatcher.Dispatch(
                new GrantTemporaryHitPointsOp(
                    Actor,
                    8,
                    new HealthChangeOriginId("replace-rage-pool-before-expiration"),
                    otherSource
                )
            );

            await AdvanceRageToExpiration(dispatcher);

            HealthState expired = dispatcher.Snapshot.Health[Actor];
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(expired.Temporary, Is.EqualTo(8));
            Assert.That(expired.TemporarySource, Is.EqualTo(otherSource));
            Assert.That(expired.HasTemporaryHitPointImmunity(RageRules.Source), Is.True);
            ResolvedOpResult<RageStartOutcome> restarted = RequireResolved(
                await dispatcher.Dispatch(new RageActionOp(Actor))
            );
            Assert.That(restarted.Value.TemporaryHitPointsGranted, Is.False);
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.EqualTo(8));
            Assert.That(dispatcher.Snapshot.Health[Actor].TemporarySource, Is.EqualTo(otherSource));
        }

        [Test]
        public async Task EncounterOutcomeCommittedFactLetsRageOwnItsCleanup()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState())
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
        public async Task EncounterSuspensionExpiresRageBeforeTheEncounterClockIsReleased()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState())
            );
            await dispatcher.Dispatch(new RageActionOp(Actor));

            ResolvedOpResult<EncounterSuspensionOutcome> suspended = RequireResolved(
                await dispatcher.Dispatch(new SuspendEncounterOp(Encounter))
            );

            Assert.That(suspended.Value.State.Phase, Is.EqualTo(EncounterPhase.Suspended));
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.Zero);
            Assert.That(
                dispatcher.Snapshot.Health[Actor].HasTemporaryHitPointImmunity(RageRules.Source),
                Is.True
            );
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings, Is.Empty);
        }

        /// <summary>
        /// Verifies expiration retains a larger foreign pool while permanently retiring Rage's
        /// temporary-Hit-Point source.
        /// </summary>
        [Test]
        public async Task ExpiredRageRecordsImmunityWhileRetainingAnotherTemporaryHitPointPool()
        {
            RuleSource otherSource = RuleSource.FromSlug("larger-temporary-hit-points");
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageConditionStateProvider(CreateActorState())
            );
            await dispatcher.Dispatch(new RageActionOp(Actor));
            await dispatcher.Dispatch(
                new GrantTemporaryHitPointsOp(
                    Actor,
                    8,
                    new HealthChangeOriginId("replace-rage-pool"),
                    otherSource
                )
            );

            await dispatcher.Dispatch(
                new ApplyDamageOp(
                    Enemy,
                    10,
                    new HealthChangeOriginId("expire-rage-with-encounter"),
                    RuleSource.FromSlug("test")
                )
            );

            HealthState retained = dispatcher.Snapshot.Health[Actor];
            Assert.That(retained.Temporary, Is.EqualTo(8));
            Assert.That(retained.TemporarySource, Is.EqualTo(otherSource));
            Assert.That(retained.HasTemporaryHitPointImmunity(RageRules.Source), Is.True);
            ResolvedOpResult<TemporaryHitPointsGrantOutcome> laterRageGrant = RequireResolved(
                await dispatcher.Dispatch(
                    new GrantTemporaryHitPointsOp(
                        Actor,
                        20,
                        new HealthChangeOriginId("later-rage-grant"),
                        RageRules.Source
                    )
                )
            );
            Assert.That(laterRageGrant.Value.Granted, Is.False);
            Assert.That(dispatcher.Snapshot.Health[Actor].Temporary, Is.EqualTo(8));
            Assert.That(dispatcher.Snapshot.Health[Actor].TemporarySource, Is.EqualTo(otherSource));
        }

        [Test]
        public async Task EndingRageRemovesItsStateAndPreventsLaterTemporaryHitPoints()
        {
            TestRageConditionStateProvider provider = new TestRageConditionStateProvider(
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

        private static async Task AdvanceRageToExpiration(RuleDispatcher dispatcher)
        {
            EncounterState turn = dispatcher.Snapshot.Encounters[Encounter];
            for (int round = 0; round < 10; round++)
            {
                turn = RequireResolved(
                    await dispatcher.Dispatch(new EndTurnOp(turn.CurrentTurn.Value))
                ).Value.State;
                Assert.That(turn.CurrentTurn.Value.Actor, Is.EqualTo(Enemy));
                turn = RequireResolved(
                    await dispatcher.Dispatch(new EndTurnOp(turn.CurrentTurn.Value))
                ).Value.State;
                Assert.That(turn.CurrentTurn.Value.Actor, Is.EqualTo(Actor));
            }
        }

        private static RuleDispatcher CreateDispatcher(
            TestRageConditionStateProvider provider,
            bool isFatigued = false,
            bool isEncumbered = false
        )
        {
            return CreateDispatcher(
                provider,
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                isFatigued: isFatigued,
                isEncumbered: isEncumbered
            );
        }

        private static RuleDispatcher CreateDispatcher(
            TestRageConditionStateProvider provider,
            ScriptedRollService rolls,
            int actorInitiativeModifier,
            int enemyInitiativeModifier,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters = null,
            bool isFatigued = false,
            bool isEncumbered = false
        )
        {
            RageActionDefinition definition = new RageActionDefinition(provider);
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            registryBuilder.Define(ConditionRuleDefinitions.Fatigued);
            registryBuilder.Define(ConditionRuleDefinitions.Encumbered);
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Party))
                .SeedCreature(new CreatureState(Enemy, Opposition))
                .SeedHealth(Actor, new HealthState(10, 10))
                .SeedHealth(Enemy, new HealthState(10, 10))
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true));
            SeedPreparedActor(seed, registryBuilder, provider.State);
            foreach (
                ActiveRuleBinding binding in RageRules.CreateInitialBindings(Actor, provider.State)
            )
            {
                seed.SeedRuleBinding(binding);
            }
            if (isFatigued)
                SeedMarker(seed, ConditionRuleDefinitions.Fatigued, "fatigued");
            if (isEncumbered)
                SeedMarker(seed, ConditionRuleDefinitions.Encumbered, "encumbered");

            registryBuilder.AddOutcomeRule();
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                rolls
            )
                .UseHealthRules()
                .UseMultipleAttackPenaltyRules()
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .UseMovementBudgetResetRules()
                .UseEncounterRules(turnStartAdapters ?? Array.Empty<IEncounterTurnStartAdapter>())
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

        private static void SeedPreparedActor(
            RulesStateSeed seed,
            RuleRegistryBuilder registry,
            RageActorState state
        )
        {
            List<PreparedBoundOption> options = new List<PreparedBoundOption>();
            List<ActiveRuleBinding> bindings = new List<ActiveRuleBinding>();
            AddOwned("rage", state.OwnsRage);
            AddOwned("quick-tempered", state.OwnsQuickTempered);
            AddOwned("invulnerable-rager", state.HasInvulnerableRager);
            seed.SeedPreparedInputs(
                Actor,
                new PreparedCreatureInputs(
                    state.Level,
                    new PreparedAbilityModifiers(0, 0, state.ConstitutionModifier, 0, 0, 0),
                    Array.Empty<KeyValuePair<string, int>>(),
                    Array.Empty<string>(),
                    state.WearsHeavyArmor ? "heavy" : string.Empty,
                    Array.Empty<string>(),
                    Array.Empty<PreparedDefenseDescriptor>(),
                    Array.Empty<PreparedDefenseDescriptor>(),
                    Array.Empty<PreparedImmunityDescriptor>(),
                    Array.Empty<string>(),
                    options,
                    Array.Empty<KeyValuePair<string, int>>()
                )
            );
            foreach (ActiveRuleBinding binding in bindings)
                seed.SeedRuleBinding(binding);

            void AddOwned(string slug, bool owned)
            {
                if (!owned)
                    return;
                RuleDefinitionId definition = new RuleDefinitionId($"prepared:test:{slug}");
                RuleSource source = RuleSource.FromSlug(slug);
                registry.Define(definition);
                options.Add(
                    new PreparedBoundOption(
                        definition,
                        $"item:owned:{slug}",
                        PreparedPredicate.Always
                    )
                );
                bindings.Add(
                    new ActiveRuleBinding(
                        new BindingId($"prepared-test-{slug}"),
                        definition,
                        Actor,
                        null,
                        source,
                        1000 + bindings.Count
                    )
                );
            }
        }

        private static RageActorState CreateActorState(
            bool ownsRage = true,
            bool ownsQuickTempered = false,
            bool wearsHeavyArmor = false,
            bool hasInvulnerableRager = false
        ) =>
            new RageActorState(
                ownsRage,
                ownsQuickTempered,
                wearsHeavyArmor,
                hasInvulnerableRager,
                1,
                2
            );

        private static async Task<JoinRetrySignature> RunQuickTemperedJoinScenario(
            JoinRetryFailurePoint? failurePoint
        )
        {
            RageActorState inactive = CreateActorState(ownsRage: false);
            RageActorState quickTempered = CreateActorState(ownsQuickTempered: true);
            DictionaryRageActorStateProvider provider = new DictionaryRageActorStateProvider(
                new Dictionary<CreatureId, RageActorState>
                {
                    [Actor] = inactive,
                    [Reinforcement] = quickTempered,
                }
            );
            ScriptedRollService rolls = new ScriptedRollService(20, 10, 5);
            RuleDispatcher dispatcher = CreateJoinRetryDispatcher(provider, rolls);
            CombatantRulesState registration = new CombatantRulesState(
                new CreatureState(Reinforcement, Opposition),
                new HealthState(10, 10),
                new GridPosition(2, 0, 0),
                new GridDistance(25),
                Array.Empty<SpellSlotState>(),
                RageRules.CreateInitialBindings(Reinforcement, quickTempered)
            );
            JoinEncounterOp join = new JoinEncounterOp(
                Encounter,
                new[]
                {
                    new EncounterJoinParticipant(
                        new EncounterParticipant(Reinforcement, Opposition, 0),
                        registration
                    ),
                }
            );
            FactStampRecorder recorder = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(recorder);
            InvalidOperationException expectedFailure = new InvalidOperationException(
                $"Injected {failurePoint} failure."
            );
            IDisposable failureRegistration = null;
            if (failurePoint == JoinRetryFailurePoint.EncounterJoinedObserver)
            {
                failureRegistration = dispatcher.RegisterFactObserver<EncounterJoinedFact>(
                    new ThrowOnceFactObserver<EncounterJoinedFact>(expectedFailure)
                );
            }
            else if (failurePoint == JoinRetryFailurePoint.QuickTemperedRootSettlement)
            {
                failureRegistration = dispatcher.RegisterRootSettlementObserver(
                    new ThrowOnceQuickTemperedSettlementObserver(Reinforcement, expectedFailure)
                );
            }

            if (failurePoint.HasValue)
            {
                InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
                    async () =>
                        await dispatcher.Dispatch(join)
                );
                Assert.That(actual, Is.SameAs(expectedFailure));
                long committedVersion = dispatcher.Snapshot.Version;
                int committedFactCount = recorder.Facts.Count;

                ResolvedOpResult<EncounterJoinOutcome> retry = RequireResolved(
                    await dispatcher.Dispatch(join)
                );

                Assert.That(retry.Facts, Is.Empty);
                Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
                Assert.That(recorder.Facts, Has.Count.EqualTo(committedFactCount));
                Assert.That(rolls.Remaining, Is.Zero, "A Join retry must not reroll initiative.");

                CombatantRulesState conflictingRegistration = new CombatantRulesState(
                    registration.Creature,
                    new HealthState(9, 10),
                    registration.Position,
                    registration.LandSpeed,
                    registration.SpellSlots,
                    registration.RuleBindings
                );
                JoinEncounterOp conflict = new JoinEncounterOp(
                    Encounter,
                    new[]
                    {
                        new EncounterJoinParticipant(
                            new EncounterParticipant(Reinforcement, Opposition, 0),
                            conflictingRegistration
                        ),
                    }
                );
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await dispatcher.Dispatch(conflict)
                );
                Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
                failureRegistration.Dispose();
            }
            else
            {
                RequireResolved(await dispatcher.Dispatch(join));
                Assert.That(rolls.Remaining, Is.Zero);
            }

            RulesSnapshot snapshot = dispatcher.Snapshot;
            return new JoinRetrySignature(
                snapshot.Version,
                snapshot.Encounters[Encounter],
                snapshot.Health[Reinforcement],
                snapshot.ActionEconomy[Reinforcement],
                snapshot.MultipleAttackPenalty[Reinforcement],
                snapshot
                    .ActiveEffects.Select(pair => pair.Value)
                    .OrderBy(effect => effect.Id.Value, StringComparer.Ordinal)
                    .ToArray(),
                snapshot
                    .RuleBindings.Select(pair => pair.Value)
                    .OrderBy(binding => binding.Id.Value, StringComparer.Ordinal)
                    .ToArray(),
                recorder.Facts.ToArray()
            );
        }

        private static RuleDispatcher CreateJoinRetryDispatcher(
            IRageActorStateProvider provider,
            ScriptedRollService rolls
        )
        {
            RageActionDefinition definition = new RageActionDefinition(provider);
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            registryBuilder.Define(ConditionRuleDefinitions.Fatigued);
            registryBuilder.Define(ConditionRuleDefinitions.Encumbered);
            registryBuilder.AddOutcomeRule();
            RuleRegistry registry = registryBuilder.Build();
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Party))
                .SeedCreature(new CreatureState(Enemy, Opposition))
                .SeedHealth(Actor, new HealthState(10, 10))
                .SeedHealth(Enemy, new HealthState(10, 10));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                rolls
            )
                .UseHealthRules()
                .UseMultipleAttackPenaltyRules()
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .UseMovementBudgetResetRules()
                .UseEncounterRules(Array.Empty<IEncounterTurnStartAdapter>())
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
                                new EncounterParticipant(Actor, Party, 0),
                                new EncounterParticipant(Enemy, Opposition, 0),
                            }
                        )
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
            );
            return dispatcher;
        }

        private static void SeedMarker(
            RulesStateSeed seed,
            RuleDefinitionId definition,
            string slug
        )
        {
            RuleSource source = RuleSource.FromSlug(slug);
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId($"{slug}-effect"),
                definition,
                Actor,
                source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            );
            seed.SeedActiveEffect(effect)
                .SeedRuleBinding(
                    new ActiveRuleBinding(
                        new BindingId($"{slug}-binding"),
                        definition,
                        Actor,
                        effect.Id,
                        source,
                        0
                    )
                );
        }

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            string failure = result is InvalidOpResult<TResult> invalid
                ? invalid.Reason
                : "The operation did not resolve.";
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>(), failure);
            return (ResolvedOpResult<TResult>)result;
        }

        private sealed class JoinRetrySignature
        {
            internal JoinRetrySignature(
                long version,
                EncounterState encounter,
                HealthState health,
                ActionEconomyState actionEconomy,
                MultipleAttackPenaltyState attackPenalty,
                ActiveEffectInstance[] activeEffects,
                ActiveRuleBinding[] ruleBindings,
                string[] facts
            )
            {
                Version = version;
                Encounter = encounter;
                Health = health;
                ActionEconomy = actionEconomy;
                AttackPenalty = attackPenalty;
                ActiveEffects = activeEffects;
                RuleBindings = ruleBindings;
                Facts = facts;
            }

            internal long Version { get; }
            internal EncounterState Encounter { get; }
            internal HealthState Health { get; }
            internal ActionEconomyState ActionEconomy { get; }
            internal MultipleAttackPenaltyState AttackPenalty { get; }
            internal ActiveEffectInstance[] ActiveEffects { get; }
            internal ActiveRuleBinding[] RuleBindings { get; }
            internal string[] Facts { get; }
        }

        private sealed class FactStampRecorder : IFactObserver<RuleFact>
        {
            internal List<string> Facts { get; } = new List<string>();

            public ValueTask OnFactCommitted(RuleFact fact, RulesSnapshot currentSnapshot)
            {
                Facts.Add(
                    $"{fact.GetType().Name}:{fact.Id.Value}:{fact.SourceOpId.Value}:{fact.RootOpId.Value}:{fact.Source.Slug}"
                );
                return default;
            }
        }

        private sealed class ThrowOnceFactObserver<TFact> : IFactObserver<TFact>
            where TFact : RuleFact
        {
            private readonly Exception failure;
            private bool thrown;

            internal ThrowOnceFactObserver(Exception failure) => this.failure = failure;

            public ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot)
            {
                if (!thrown)
                {
                    thrown = true;
                    throw failure;
                }
                return default;
            }
        }

        private sealed class ThrowOnceQuickTemperedSettlementObserver : IRootSettlementObserver
        {
            private readonly CreatureId actor;
            private readonly Exception failure;
            private bool thrown;

            internal ThrowOnceQuickTemperedSettlementObserver(CreatureId actor, Exception failure)
            {
                this.actor = actor;
                this.failure = failure;
            }

            public ValueTask OnRootSettled(
                OpId rootId,
                OpId? causalParentRootId,
                RulesSnapshot snapshot
            )
            {
                BindingId quickTempered = new BindingId($"quick-tempered-{actor.Value}");
                if (
                    !thrown
                    && RageRules.IsRaging(snapshot, actor)
                    && snapshot.RuleBindings.TryGet(quickTempered, out ActiveRuleBinding binding)
                    && !binding.IsEnabled
                )
                {
                    thrown = true;
                    throw failure;
                }
                return default;
            }
        }

        private sealed class DictionaryRageActorStateProvider : IRageActorStateProvider
        {
            private readonly IReadOnlyDictionary<CreatureId, RageActorState> states;

            internal DictionaryRageActorStateProvider(
                IReadOnlyDictionary<CreatureId, RageActorState> states
            ) => this.states = states;

            public RageActorState Get(CreatureId actor) =>
                states.TryGetValue(actor, out RageActorState state)
                    ? state
                    : throw new InvalidOperationException("Unknown Rage test actor.");
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

        private sealed class TestRageConditionStateProvider : IRageConditionStateProvider
        {
            private readonly RageActorState state;

            public TestRageConditionStateProvider(RageActorState state) =>
                this.state = state ?? throw new ArgumentNullException(nameof(state));

            public RageActorState State => state;

            public RageConditionState Get(CreatureId actor)
            {
                if (actor != Actor)
                    throw new InvalidOperationException("Unknown Rage test actor.");
                return new RageConditionState(state.IsFatigued, state.IsEncumbered);
            }
        }

        private sealed class RageTurnStartProbe : IEncounterTurnStartAdapter
        {
            private readonly CreatureId actor;

            public RageTurnStartProbe(CreatureId actor) => this.actor = actor;

            public int Calls { get; private set; }
            public bool WasRaging { get; private set; }
            public int TemporaryHitPoints { get; private set; } = -1;

            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                if (context.Actor != actor)
                    return new ValueTask<TurnStartContribution>(current);
                Calls++;
                WasRaging = RageRules.IsRaging(context.Snapshot, actor);
                TemporaryHitPoints = context.Snapshot.Health[actor].Temporary;
                return new ValueTask<TurnStartContribution>(current);
            }
        }

        private sealed class TenthActorTurnDamageAdapter : IEncounterTurnStartAdapter
        {
            private readonly CreatureId actor;
            private bool enabled;

            public TenthActorTurnDamageAdapter(CreatureId actor) => this.actor = actor;

            public int ActorTurnCalls { get; private set; }
            public int TemporaryHitPointsBeforeDamage { get; private set; } = -1;

            public void Enable() => enabled = true;

            public async ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                if (!enabled || context.Actor != actor)
                    return current;
                ActorTurnCalls++;
                if (ActorTurnCalls != 10)
                    return current;

                TemporaryHitPointsBeforeDamage = context.Snapshot.Health[actor].Temporary;
                await context.ApplyFinalDamage(
                    actor,
                    1,
                    new HealthChangeOriginId("rage-expiration-turn-start"),
                    RuleSource.FromSlug("rage-expiration-turn-start-test")
                );
                return current;
            }
        }
    }
}
