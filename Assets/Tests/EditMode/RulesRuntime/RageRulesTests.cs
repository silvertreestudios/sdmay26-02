using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
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
        private static readonly CreatureId SecondReinforcement = new CreatureId(
            "second-reinforcement"
        );
        private static readonly PlayerId Party = new PlayerId("party");
        private static readonly PlayerId Opposition = new PlayerId("opposition");
        private static readonly EncounterId Encounter = new EncounterId("encounter");
        private static readonly OpId PendingRageRoot = new OpId(100);
        private static readonly ActiveEffectId PendingRageEffect = RageEffectState.CreateEffectId(
            PendingRageRoot,
            Actor
        );
        private static readonly BindingId PendingRageBinding = RageEffectState.CreateBindingId(
            PendingRageRoot,
            Actor
        );
        private static readonly RuleDefinitionId GrantFailureDefinition = new RuleDefinitionId(
            "test-rage-grant-failure"
        );
        private static readonly RuleDefinitionId GrantProbeDefinition = new RuleDefinitionId(
            "test-rage-grant-probe"
        );
        private static readonly RuleDefinitionId QuickActionProbeDefinition = new RuleDefinitionId(
            "test-rage-quick-action-probe"
        );
        private static readonly RuleDefinitionId CleanupFailureDefinition = new RuleDefinitionId(
            "test-rage-cleanup-failure"
        );
        private static readonly BindingId CleanupFailureBinding = new BindingId(
            "test-rage-cleanup-failure-binding"
        );
        private static readonly RuleSource OtherTemporaryHitPointSource = RuleSource.FromSlug(
            "test-other-temporary-hit-points"
        );
        private static readonly RuleSource UnrelatedImmunitySource = RuleSource.FromSlug(
            "test-unrelated-immunity"
        );

        /// <summary>Identifies a no-op Rage grant whose health alone cannot prove resolution.</summary>
        public enum NoOpRageGrantCase
        {
            /// <summary>The actor already has exactly the offered amount from another source.</summary>
            EqualForeignPool,

            /// <summary>The actor already has more than the offered amount from another source.</summary>
            HigherForeignPool,

            /// <summary>The actor is immune to Rage temporary Hit Points.</summary>
            RageImmunity,

            /// <summary>Rage offers zero temporary Hit Points.</summary>
            ZeroOffer,
        }

        /// <summary>Identifies a public Rage workflow result blocked before its reducer settles.</summary>
        public enum WorkflowFailureKind
        {
            /// <summary>The public THP middleware returns Invalid without invoking the reducer.</summary>
            GrantInvalid,

            /// <summary>The public THP middleware interrupts without invoking the reducer.</summary>
            GrantInterrupted,

            /// <summary>The public THP middleware throws before invoking the reducer.</summary>
            GrantPreNextThrow,

            /// <summary>The public THP rejects, then pending-effect cleanup throws.</summary>
            GrantInvalidWithCleanupThrow,

            /// <summary>The settlement middleware returns Invalid without invoking the reducer.</summary>
            SettlementInvalid,

            /// <summary>The settlement middleware throws before invoking the reducer.</summary>
            SettlementPreNextThrow,

            /// <summary>The public THP middleware returns Invalid after the reducer commits.</summary>
            GrantPostNextInvalid,

            /// <summary>The public THP middleware interrupts after the reducer commits.</summary>
            GrantPostNextInterrupted,

            /// <summary>
            /// A newer foreign pool commits after Rage, before public THP returns Invalid.
            /// </summary>
            GrantPostNextInvalidWithInterveningPool,

            /// <summary>A newer Rage-owned pool with a different amount commits before failure.</summary>
            GrantPostNextInvalidWithChangedRagePool,

            /// <summary>
            /// The Rage-owned pool is removed and re-granted with identical public values before
            /// the original public THP operation returns Invalid.
            /// </summary>
            GrantPostNextInvalidWithAbaPool,

            /// <summary>The committed Rage pool is fully consumed before failure.</summary>
            GrantPostNextInvalidWithConsumedPool,

            /// <summary>Unrelated healing commits while the exact Rage pool remains current.</summary>
            GrantPostNextInvalidWithHealing,
        }

        /// <summary>Identifies the post-commit boundary interrupted by a retry regression.</summary>
        public enum JoinRetryFailurePoint
        {
            /// <summary>Interrupts delivery immediately after the roster transaction commits.</summary>
            EncounterJoinedObserver,

            /// <summary>Interrupts delivery immediately after the Rage effect commits.</summary>
            ActiveEffectCreatedObserver,

            /// <summary>Interrupts delivery immediately after Rage temporary HP commits.</summary>
            TemporaryHitPointsGrantedObserver,

            /// <summary>Interrupts public THP middleware after the feature commit returns.</summary>
            GrantPostNextMiddleware,

            /// <summary>Interrupts the resolved observer for the public THP operation.</summary>
            GrantResolvedObserver,

            /// <summary>Interrupts Quick-Tempered middleware after the child action returns.</summary>
            QuickActionPostNextMiddleware,

            /// <summary>Interrupts the resolved observer for the Quick-Tempered child action.</summary>
            QuickActionResolvedObserver,

            /// <summary>Interrupts Rage settlement middleware after its receipt commits.</summary>
            SettlementPostNextMiddleware,

            /// <summary>Interrupts the resolved observer for the Rage settlement operation.</summary>
            SettlementResolvedObserver,

            /// <summary>Interrupts trigger middleware after the one-shot is disabled.</summary>
            TriggerPostNextMiddleware,

            /// <summary>Interrupts the resolved observer for trigger consumption.</summary>
            TriggerResolvedObserver,

            /// <summary>Interrupts delivery after the one-shot trigger is disabled.</summary>
            TriggerConsumedObserver,

            /// <summary>Interrupts root settlement after Quick-Tempered finishes its mutations.</summary>
            QuickTemperedRootSettlement,
        }

        /// <summary>Identifies the Rage cleanup operation interrupted by a failure gate.</summary>
        public enum RageCleanupOperation
        {
            /// <summary>The public feature cleanup operation.</summary>
            EndRage,

            /// <summary>The exact atomic cleanup reducer operation.</summary>
            CommitRageEnd,
        }

        /// <summary>Identifies a failure after Rage cleanup has durably committed.</summary>
        public enum RageCleanupPostCommitFailure
        {
            /// <summary>Middleware throws while returning from the cleanup operation.</summary>
            PostNextMiddleware,

            /// <summary>A committed cleanup Fact observer throws.</summary>
            FactObserver,

            /// <summary>The exact cleanup operation's resolved observer throws.</summary>
            ResolvedObserver,
        }

        [Test]
        public void RageEffectStateRestoresPublicMarkerEqualityAndKeepsExactReceipts()
        {
            RageEffectState marker = new RageEffectState(false);
            RageEffectState matchingMarker = new RageEffectState(false);
            RageEffectState quickMarker = new RageEffectState(true);
            RageEffectState receipt = CreateWorkflowReceipt(new OpId(41), 3, 3);
            RageEffectState recreatedReceipt = CreateWorkflowReceipt(new OpId(41), 3, 3);
            RageEffectState changedRoot = CreateWorkflowReceipt(new OpId(42), 3, 3);
            RageEffectState changedGrantPool = CreateWorkflowReceipt(new OpId(41), 3, 4);

            Assert.That(marker.StartedByQuickTempered, Is.False);
            Assert.That(marker.HasWorkflowReceipt, Is.False);
            Assert.That(marker.Phase, Is.EqualTo(RageStartPhase.Settled));
            Assert.That(quickMarker.StartedByQuickTempered, Is.True);
            Assert.That(marker, Is.EqualTo(matchingMarker));
            Assert.That(marker.GetHashCode(), Is.EqualTo(matchingMarker.GetHashCode()));
            Assert.That(marker, Is.Not.EqualTo(quickMarker));
            Assert.That(receipt, Is.EqualTo(changedRoot));
            Assert.That(receipt, Is.EqualTo(changedGrantPool));
            Assert.That(receipt.GetHashCode(), Is.EqualTo(changedRoot.GetHashCode()));
            Assert.That(receipt, Is.EqualTo(marker));
            Assert.That(EffectStateExactEquality.Equals(receipt, recreatedReceipt), Is.True);
            Assert.That(
                EffectStateExactEquality.GetHashCode(receipt),
                Is.EqualTo(EffectStateExactEquality.GetHashCode(recreatedReceipt))
            );
            Assert.That(EffectStateExactEquality.Equals(receipt, changedRoot), Is.False);
            Assert.That(EffectStateExactEquality.Equals(receipt, changedGrantPool), Is.False);
            Assert.That(EffectStateExactEquality.Equals(receipt, marker), Is.False);
            Assert.That(
                EffectStateExactEquality.GetHashCode(receipt),
                Is.Not.EqualTo(EffectStateExactEquality.GetHashCode(changedRoot))
            );
            Assert.That(
                EffectStateExactEquality.GetHashCode(receipt),
                Is.Not.EqualTo(EffectStateExactEquality.GetHashCode(changedGrantPool))
            );
        }

        [Test]
        public void CommitRageEndRejectsForgedPublicMarkerOriginWithoutMutation()
        {
            OpId root = new OpId(211);
            ActiveEffectId effectId = new ActiveEffectId("public-marker-reducer-effect");
            BindingId bindingId = new BindingId("public-marker-reducer-binding");
            RageEffectState marker = new RageEffectState(false);
            ActiveEffectInstance effect = new ActiveEffectInstance(
                effectId,
                RageActionDefinition.EffectDefinitionId,
                Actor,
                RageRules.Source,
                EffectDuration.Rounds(1),
                marker
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                bindingId,
                effect.DefinitionId,
                Actor,
                effectId,
                RageRules.Source,
                20
            );
            ActiveEffectTimingState timing = new ActiveEffectTimingState(
                effectId,
                Encounter,
                bindingId,
                Actor,
                1,
                false,
                binding.CreationOrder
            );
            CommitRageEndOp operation = new CommitRageEndOp(
                Actor,
                effectId,
                bindingId,
                effect.EffectStateVersion,
                marker,
                new HealthChangeOriginId("forged-public-marker-cleanup-origin")
            );

            AssertRejectedRageCleanup(operation, root, effect, binding, timing);
        }

        [TestCase(ActiveEffectStatus.Active)]
        [TestCase(ActiveEffectStatus.Expired)]
        public void CommitRageEndRejectsHiddenSettledReceiptAtWrongLifecycleVersion(
            ActiveEffectStatus status
        )
        {
            OpId receiptRoot = new OpId(41);
            RageEffectState receipt = CreateWorkflowReceipt(receiptRoot, 3, 3);
            EffectStateVersion wrongVersion = new EffectStateVersion(4);
            ActiveEffectInstance effect = new ActiveEffectInstance(
                receipt.EffectId,
                RageActionDefinition.EffectDefinitionId,
                Actor,
                RageRules.Source,
                EffectDuration.Rounds(1),
                receipt,
                wrongVersion,
                status
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                receipt.BindingId,
                effect.DefinitionId,
                Actor,
                effect.Id,
                RageRules.Source,
                receiptRoot.Value,
                status == ActiveEffectStatus.Active
            );
            ActiveEffectTimingState timing =
                status == ActiveEffectStatus.Active
                    ? new ActiveEffectTimingState(
                        effect.Id,
                        Encounter,
                        binding.Id,
                        Actor,
                        1,
                        false,
                        binding.CreationOrder
                    )
                    : null;
            CommitRageEndOp operation = new CommitRageEndOp(
                Actor,
                effect.Id,
                binding.Id,
                wrongVersion,
                receipt,
                receipt.Origin
            );

            AssertRejectedRageCleanup(operation, new OpId(212), effect, binding, timing);
        }

        [Test]
        public void ActionProfilesKeepQuickTemperedTraitsDistinctFromRage()
        {
            RageActionDefinition definition = new RageActionDefinition();

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
        public async Task PublicActiveMarkerIsRagingAndEndsAsCompletedState()
        {
            ActiveEffectRegistration marker = CreateRageRegistration(
                "public-rage-marker",
                new RageEffectState(false),
                RageRules.Source,
                EffectDuration.OneMinute
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                seededActiveEffects: new[] { marker }
            );

            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
            ResolvedOpResult<RageEndOutcome> ended = RequireResolved(
                await dispatcher.Dispatch(new EndRageOp(Actor))
            );

            Assert.That(ended.Value.Ended, Is.True);
            Assert.That(ended.Facts.OfType<ActiveEffectRemovedFact>().Count(), Is.EqualTo(1));
            Assert.That(
                ended.Facts.OfType<TemporaryHitPointImmunityAddedFact>().Count(),
                Is.EqualTo(1),
                "A public marker is completed state, never a pending start receipt."
            );
            Assert.That(dispatcher.Snapshot.ActiveEffects.Contains(marker.Effect.Id), Is.False);
        }

        [Test]
        public async Task PublicMarkerExpirationUsesCompletedCleanup()
        {
            ActiveEffectRegistration marker = CreateRageRegistration(
                "expiring-public-rage-marker",
                new RageEffectState(true),
                RageRules.Source,
                EffectDuration.Rounds(1)
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                seededActiveEffects: new[] { marker }
            );
            EncounterState actorTurn = dispatcher.Snapshot.Encounters[Encounter];

            EncounterState enemyTurn = RequireResolved(
                await dispatcher.Dispatch(new EndTurnOp(actorTurn.CurrentTurn.Value))
            ).Value.State;
            await dispatcher.Dispatch(new EndTurnOp(enemyTurn.CurrentTurn.Value));

            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(dispatcher.Snapshot.ActiveEffects.Contains(marker.Effect.Id), Is.False);
            Assert.That(dispatcher.Snapshot.RuleBindings.Contains(marker.Binding.Id), Is.False);
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings.Contains(marker.Effect.Id),
                Is.False
            );
            Assert.That(
                dispatcher.Snapshot.Health[Actor].HasTemporaryHitPointImmunity(RageRules.Source),
                Is.True
            );
        }

        [Test]
        public void ExpiredRageTombstoneBlocksAnotherStartUntilCleanupSettles()
        {
            ActiveEffectId effectId = new ActiveEffectId("expired-rage-tombstone-effect");
            BindingId bindingId = new BindingId("expired-rage-tombstone-binding");
            ActiveEffectRegistration tombstone = new ActiveEffectRegistration(
                new ActiveEffectInstance(
                    effectId,
                    RageActionDefinition.EffectDefinitionId,
                    Actor,
                    RageRules.Source,
                    EffectDuration.OneMinute,
                    new RageEffectState(false),
                    EffectStateVersion.Initial.Next(),
                    ActiveEffectStatus.Expired
                ),
                new ActiveRuleBinding(
                    bindingId,
                    RageActionDefinition.EffectDefinitionId,
                    Actor,
                    effectId,
                    RageRules.Source,
                    50,
                    false
                )
            );
            RulesSnapshot snapshot = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedCreature(new CreatureState(Actor, Party))
                    .SeedHealth(Actor, new HealthState(10, 10))
                    .SeedActiveEffect(tombstone.Effect)
                    .SeedRuleBinding(tombstone.Binding)
            ).Snapshot;

            ActionValidationResult result = RageRules.Validate(
                snapshot,
                Actor,
                CreateActorState(),
                false
            );

            Assert.That(result, Is.TypeOf<ActionValidationResult.InvalidActionValidationResult>());
            Assert.That(snapshot.ActiveEffects.Contains(effectId), Is.True);
            Assert.That(snapshot.RuleBindings[bindingId].IsEnabled, Is.False);
        }

        [Test]
        public async Task ForeignSourceRageDefinitionNeitherBlocksNorCleansOwnedRage()
        {
            RuleSource foreignSource = RuleSource.FromSlug("foreign-rage-definition-user");
            ActiveEffectRegistration foreign = CreateRageRegistration(
                "foreign-rage-definition-user",
                new RageEffectState(false),
                foreignSource,
                EffectDuration.Rounds(1)
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                seededActiveEffects: new[] { foreign }
            );

            RequireResolved(await dispatcher.Dispatch(new RageActionOp(Actor)));
            ActiveEffectId owned = dispatcher
                .Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Single(effect => effect.Source == RageRules.Source)
                .Id;
            EncounterState actorTurn = dispatcher.Snapshot.Encounters[Encounter];
            EncounterState enemyTurn = RequireResolved(
                await dispatcher.Dispatch(new EndTurnOp(actorTurn.CurrentTurn.Value))
            ).Value.State;
            await dispatcher.Dispatch(new EndTurnOp(enemyTurn.CurrentTurn.Value));

            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[foreign.Effect.Id].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[owned].Status,
                Is.EqualTo(ActiveEffectStatus.Active)
            );
            RequireResolved(await dispatcher.Dispatch(new EndRageOp(Actor)));
            Assert.That(dispatcher.Snapshot.ActiveEffects.Contains(owned), Is.False);
            Assert.That(dispatcher.Snapshot.ActiveEffects.Contains(foreign.Effect.Id), Is.True);
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

        [Test]
        public void EncounterStartFirstQuickTemperedFailureSettlesSiblingWithoutReplay()
        {
            RageActorState quickTempered = CreateActorState(ownsQuickTempered: true);
            ScriptedRollService rolls = new ScriptedRollService(20, 15, 10);
            RuleDispatcher dispatcher = CreateBatchQuickTemperedDispatcher(
                new Dictionary<CreatureId, RageActorState>
                {
                    [Actor] = quickTempered,
                    [Reinforcement] = quickTempered,
                    [Enemy] = CreateActorState(ownsRage: false),
                },
                rolls
            );
            FactStampRecorder recorder = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(recorder);
            InvalidOperationException expected = new InvalidOperationException(
                "Injected first Quick-Tempered start settlement failure."
            );
            using IDisposable failure = dispatcher.RegisterRootSettlementObserver(
                new ThrowOnceQuickTemperedSettlementObserver(Actor, expected)
            );
            StartEncounterOp start = new StartEncounterOp(
                Encounter,
                Party,
                new[]
                {
                    new EncounterParticipant(Actor, Party, 0),
                    new EncounterParticipant(Reinforcement, Party, 0),
                    new EncounterParticipant(Enemy, Opposition, 0),
                }
            );

            InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(start)
            );
            Assert.That(actual, Is.SameAs(expected));
            AssertTwoSettledQuickTemperedRages(dispatcher.Snapshot, Actor, Reinforcement);
            Assert.That(recorder.Count<QuickTemperedTriggerConsumedFact>(), Is.EqualTo(2));
            long committedVersion = dispatcher.Snapshot.Version;
            int committedFacts = recorder.Facts.Count;

            InvalidOperationException retry = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(start)
            );

            Assert.That(retry.Message, Does.Contain("duplicate or another encounter is active"));
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(recorder.Facts, Has.Count.EqualTo(committedFacts));
            Assert.That(recorder.Count<QuickTemperedTriggerConsumedFact>(), Is.EqualTo(2));
            AssertTwoSettledQuickTemperedRages(dispatcher.Snapshot, Actor, Reinforcement);
            Assert.That(rolls.Remaining, Is.Zero);
        }

        [Test]
        public async Task ReinforcementJoinFirstQuickTemperedFailureSettlesSiblingWithoutReplay()
        {
            RageActorState quickTempered = CreateActorState(ownsQuickTempered: true);
            ScriptedRollService rolls = new ScriptedRollService(20, 10, 15, 14);
            RuleDispatcher dispatcher = CreateBatchQuickTemperedDispatcher(
                new Dictionary<CreatureId, RageActorState>
                {
                    [Actor] = CreateActorState(ownsRage: false),
                    [Enemy] = CreateActorState(ownsRage: false),
                },
                rolls
            );
            RequireResolved(
                await dispatcher.Dispatch(
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
            );
            JoinEncounterOp join = new JoinEncounterOp(
                Encounter,
                new[]
                {
                    QuickTemperedJoinParticipant(Reinforcement, quickTempered),
                    QuickTemperedJoinParticipant(SecondReinforcement, quickTempered),
                }
            );
            FactStampRecorder recorder = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(recorder);
            InvalidOperationException expected = new InvalidOperationException(
                "Injected two-actor Quick-Tempered settlement failure."
            );
            using IDisposable failure = dispatcher.RegisterRootSettlementObserver(
                new ThrowOnceQuickTemperedSettlementObserver(Reinforcement, expected)
            );

            InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(join)
            );
            Assert.That(actual, Is.SameAs(expected));
            AssertTwoSettledQuickTemperedRages(
                dispatcher.Snapshot,
                Reinforcement,
                SecondReinforcement
            );
            Assert.That(recorder.Count<QuickTemperedTriggerConsumedFact>(), Is.EqualTo(2));
            long committedVersion = dispatcher.Snapshot.Version;
            int committedFacts = recorder.Facts.Count;

            ResolvedOpResult<EncounterJoinOutcome> retry = RequireResolved(
                await dispatcher.Dispatch(join)
            );

            Assert.That(retry.Facts, Is.Empty);
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(recorder.Facts, Has.Count.EqualTo(committedFacts));
            Assert.That(recorder.Count<QuickTemperedTriggerConsumedFact>(), Is.EqualTo(2));
            Assert.That(rolls.Remaining, Is.Zero);
            AssertTwoSettledQuickTemperedRages(
                dispatcher.Snapshot,
                Reinforcement,
                SecondReinforcement
            );
        }

        [TestCase(JoinRetryFailurePoint.EncounterJoinedObserver)]
        [TestCase(JoinRetryFailurePoint.ActiveEffectCreatedObserver)]
        [TestCase(JoinRetryFailurePoint.TemporaryHitPointsGrantedObserver)]
        [TestCase(JoinRetryFailurePoint.GrantPostNextMiddleware)]
        [TestCase(JoinRetryFailurePoint.GrantResolvedObserver)]
        [TestCase(JoinRetryFailurePoint.QuickActionPostNextMiddleware)]
        [TestCase(JoinRetryFailurePoint.QuickActionResolvedObserver)]
        [TestCase(JoinRetryFailurePoint.TriggerPostNextMiddleware)]
        [TestCase(JoinRetryFailurePoint.TriggerResolvedObserver)]
        [TestCase(JoinRetryFailurePoint.TriggerConsumedObserver)]
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

        [TestCase(JoinRetryFailurePoint.SettlementPostNextMiddleware)]
        [TestCase(JoinRetryFailurePoint.SettlementResolvedObserver)]
        public async Task PostCommitSettlementFailureRecoversOnlyFromExactReceipt(
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

        [TestCase(NoOpRageGrantCase.EqualForeignPool)]
        [TestCase(NoOpRageGrantCase.HigherForeignPool)]
        [TestCase(NoOpRageGrantCase.RageImmunity)]
        [TestCase(NoOpRageGrantCase.ZeroOffer)]
        public async Task PreCommitGrantThrowNeverInfersSettlementFromHealth(
            NoOpRageGrantCase grantCase
        )
        {
            RageActorState quickTempered = CreateActorState(
                ownsQuickTempered: true,
                level: grantCase == NoOpRageGrantCase.ZeroOffer ? 0 : 1,
                constitutionModifier: grantCase == NoOpRageGrantCase.ZeroOffer ? 0 : 2
            );
            HealthState initialHealth = grantCase switch
            {
                NoOpRageGrantCase.EqualForeignPool => new HealthState(
                    10,
                    10,
                    3,
                    OtherTemporaryHitPointSource
                ),
                NoOpRageGrantCase.HigherForeignPool => new HealthState(
                    10,
                    10,
                    5,
                    OtherTemporaryHitPointSource
                ),
                NoOpRageGrantCase.RageImmunity => new HealthState(
                    10,
                    10,
                    0,
                    default,
                    new[] { RageRules.Source }
                ),
                NoOpRageGrantCase.ZeroOffer => new HealthState(10, 10),
                _ => throw new ArgumentOutOfRangeException(nameof(grantCase)),
            };

            await AssertBlockedQuickTemperedWorkflow(
                quickTempered,
                initialHealth,
                WorkflowFailureKind.GrantPreNextThrow,
                injectEffectObserverFailure: true,
                grantCheckpointExpected: false
            );
        }

        [TestCase(WorkflowFailureKind.GrantInvalid)]
        [TestCase(WorkflowFailureKind.GrantInterrupted)]
        [TestCase(WorkflowFailureKind.GrantPreNextThrow)]
        public async Task PreventionGrantFailureDoesNotCommitSettleOrConsumeQuickTempered(
            WorkflowFailureKind failureKind
        )
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(10, 10),
                failureKind,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: false
            );
        }

        [Test]
        public async Task CleanupFailureAggregatesWithOriginalGrantFailure()
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(10, 10),
                WorkflowFailureKind.GrantInvalidWithCleanupThrow,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: false
            );
        }

        [Test]
        public async Task PublicGrantTraversesEveryMiddlewarePhaseExactlyOnce()
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
            GrantPhaseProbe[] probes =
            {
                new GrantPhaseProbe(RuleLifecyclePhase.Prevention),
                new GrantPhaseProbe(RuleLifecyclePhase.Transformation),
                new GrantPhaseProbe(RuleLifecyclePhase.Reaction),
                new GrantPhaseProbe(RuleLifecyclePhase.Observation),
            };
            RuleDispatcher dispatcher = CreateJoinRetryDispatcher(
                provider,
                new ScriptedRollService(20, 10, 5),
                phaseProbes: probes
            );
            ActiveRuleBinding[] phaseBindings = probes
                .Select(
                    (probe, index) =>
                        new ActiveRuleBinding(
                            new BindingId($"test-rage-phase-binding-{index}"),
                            probe.DefinitionId,
                            Reinforcement,
                            default,
                            RuleSource.FromSlug($"test-rage-phase-{index}"),
                            index + 2
                        )
                )
                .ToArray();
            CombatantRulesState registration = new CombatantRulesState(
                new CreatureState(Reinforcement, Opposition),
                new HealthState(10, 10),
                new GridPosition(2, 0, 0),
                new GridDistance(25),
                CreatureStatisticsState.Empty(Reinforcement),
                PreparedInputsFor(quickTempered),
                Array.Empty<SpellSlotState>(),
                RageRules
                    .CreateInitialBindings(Reinforcement, quickTempered)
                    .Concat(phaseBindings)
                    .ToArray()
            );

            RequireResolved(
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Opposition, 0),
                                registration
                            ),
                        }
                    )
                )
            );

            Assert.That(probes.Select(probe => probe.Count), Is.EqualTo(new[] { 1, 1, 1, 1 }));
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Reinforcement), Is.True);
        }

        [TestCase(WorkflowFailureKind.SettlementInvalid)]
        [TestCase(WorkflowFailureKind.SettlementPreNextThrow)]
        public async Task PreCommitSettlementFailureDispatchesOnceAndDoesNotConsume(
            WorkflowFailureKind failureKind
        )
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(10, 10),
                failureKind,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: true
            );
        }

        [TestCase(WorkflowFailureKind.GrantPostNextInvalid)]
        [TestCase(WorkflowFailureKind.GrantPostNextInterrupted)]
        public async Task StructuralPostCommitGrantFailureRestoresDisplacedForeignPool(
            WorkflowFailureKind failureKind
        )
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(10, 10, 2, OtherTemporaryHitPointSource),
                failureKind,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: true
            );
        }

        [Test]
        public async Task StructuralPostCommitGrantFailureDoesNotOverwriteNewerPool()
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(10, 10, 2, OtherTemporaryHitPointSource),
                WorkflowFailureKind.GrantPostNextInvalidWithInterveningPool,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: true
            );
        }

        [Test]
        public async Task StructuralPostCommitGrantFailureDoesNotRestoreAcrossIdenticalLookingPool()
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(10, 10, 2, OtherTemporaryHitPointSource),
                WorkflowFailureKind.GrantPostNextInvalidWithAbaPool,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: true
            );
        }

        [Test]
        public async Task StructuralPostCommitGrantFailureRetainsPendingForChangedRagePool()
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(10, 10, 2, OtherTemporaryHitPointSource),
                WorkflowFailureKind.GrantPostNextInvalidWithChangedRagePool,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: true
            );
        }

        [Test]
        public async Task StructuralPostCommitGrantFailureRemovesPendingAfterPoolConsumption()
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(10, 10, 2, OtherTemporaryHitPointSource),
                WorkflowFailureKind.GrantPostNextInvalidWithConsumedPool,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: true
            );
        }

        [Test]
        public async Task StructuralPostCommitGrantFailureRestoresPoolAroundUnrelatedHealing()
        {
            await AssertBlockedQuickTemperedWorkflow(
                CreateActorState(ownsQuickTempered: true),
                new HealthState(8, 10, 2, OtherTemporaryHitPointSource),
                WorkflowFailureKind.GrantPostNextInvalidWithHealing,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: true
            );
        }

        [TestCase(NoOpRageGrantCase.EqualForeignPool)]
        [TestCase(NoOpRageGrantCase.HigherForeignPool)]
        [TestCase(NoOpRageGrantCase.RageImmunity)]
        [TestCase(NoOpRageGrantCase.ZeroOffer)]
        public async Task SettlementFailureLeavesNoOpGrantHealthUntouched(
            NoOpRageGrantCase grantCase
        )
        {
            RageActorState quickTempered = CreateActorState(
                ownsQuickTempered: true,
                level: grantCase == NoOpRageGrantCase.ZeroOffer ? 0 : 1,
                constitutionModifier: grantCase == NoOpRageGrantCase.ZeroOffer ? 0 : 2
            );
            HealthState initialHealth = grantCase switch
            {
                NoOpRageGrantCase.EqualForeignPool => new HealthState(
                    10,
                    10,
                    3,
                    OtherTemporaryHitPointSource
                ),
                NoOpRageGrantCase.HigherForeignPool => new HealthState(
                    10,
                    10,
                    5,
                    OtherTemporaryHitPointSource
                ),
                NoOpRageGrantCase.RageImmunity => new HealthState(
                    10,
                    10,
                    0,
                    default,
                    new[] { RageRules.Source }
                ),
                NoOpRageGrantCase.ZeroOffer => new HealthState(10, 10),
                _ => throw new ArgumentOutOfRangeException(nameof(grantCase)),
            };

            await AssertBlockedQuickTemperedWorkflow(
                quickTempered,
                initialHealth,
                WorkflowFailureKind.SettlementInvalid,
                injectEffectObserverFailure: false,
                grantCheckpointExpected: true
            );
        }

        [Test]
        public async Task DefeatedActorCannotSpendOrMutateManualRage()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                actorHealth: new HealthState(0, 10).CommitDefeat()
            );
            RulesSnapshot before = dispatcher.Snapshot;

            OpResult<RageStartOutcome> result = await dispatcher.Dispatch(new RageActionOp(Actor));

            Assert.That(result, Is.TypeOf<InvalidOpResult<RageStartOutcome>>());
            Assert.That(dispatcher.Snapshot, Is.SameAs(before));
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
        }

        [Test]
        public async Task DefeatedQuickTemperedReinforcementDoesNotMutateOrConsumeTrigger()
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
            RuleDispatcher dispatcher = CreateJoinRetryDispatcher(
                provider,
                new ScriptedRollService(20, 10, 5)
            );
            ActiveRuleBinding[] initialBindings = RageRules
                .CreateInitialBindings(Reinforcement, quickTempered)
                .ToArray();
            CombatantRulesState registration = new CombatantRulesState(
                new CreatureState(Reinforcement, Opposition),
                new HealthState(0, 10).CommitDefeat(),
                new GridPosition(2, 0, 0),
                new GridDistance(25),
                CreatureStatisticsState.Empty(Reinforcement),
                PreparedInputsFor(quickTempered),
                Array.Empty<SpellSlotState>(),
                initialBindings
            );
            FactStampRecorder recorder = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(recorder);

            RequireResolved(
                await dispatcher.Dispatch(
                    new JoinEncounterOp(
                        Encounter,
                        new[]
                        {
                            new EncounterJoinParticipant(
                                new EncounterParticipant(Reinforcement, Opposition, 0),
                                registration
                            ),
                        }
                    )
                )
            );

            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Reinforcement), Is.False);
            Assert.That(dispatcher.Snapshot.Health[Reinforcement], Is.EqualTo(registration.Health));
            foreach (ActiveRuleBinding expected in initialBindings)
                Assert.That(dispatcher.Snapshot.RuleBindings[expected.Id], Is.EqualTo(expected));
            Assert.That(recorder.Count<ActiveEffectCreatedFact>(), Is.Zero);
            Assert.That(recorder.Count<TemporaryHitPointsGrantedFact>(), Is.Zero);
            Assert.That(recorder.Count<QuickTemperedTriggerConsumedFact>(), Is.Zero);
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
        public async Task EncounterEndRetriesOnlyAfterRageCleanupCompletes()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState())
            );
            RequireResolved(await dispatcher.Dispatch(new RageActionOp(Actor)));
            InvalidOperationException expected = new InvalidOperationException(
                "Rage cleanup fact delivery failed."
            );
            dispatcher.RegisterFactObserver<TemporaryHitPointsRemovedFact>(
                new ThrowOnceFactObserver<TemporaryHitPointsRemovedFact>(expected)
            );

            InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(
                        new ApplyDamageOp(
                            Enemy,
                            10,
                            new HealthChangeOriginId("end-encounter-after-rage-cleanup"),
                            RuleSource.FromSlug("test")
                        )
                    )
            );

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(EncounterPhase.Active)
            );
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(dispatcher.Snapshot.ActiveEffects, Is.Empty);

            RequireResolved(
                await dispatcher.Dispatch(
                    new EndEncounterOp(Encounter, EncounterOutcome.PlayerVictory)
                )
            );
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(EncounterPhase.Ended)
            );
        }

        [TestCase(RageCleanupOperation.EndRage)]
        [TestCase(RageCleanupOperation.CommitRageEnd)]
        public async Task TimedExpirationPreNextFailureRecoversSamePublishedActorBeforeAdapter(
            RageCleanupOperation operation
        )
        {
            InvalidOperationException expected = new InvalidOperationException(
                $"Injected pre-next {operation} cleanup failure."
            );
            RageCleanupFailureMiddleware cleanup = RageCleanupFailureMiddleware.PreNext(
                Actor,
                operation,
                expected
            );
            RageStateTurnStartAdapter adapter = new RageStateTurnStartAdapter();
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                turnStartAdapters: new[] { adapter },
                cleanupFailure: cleanup
            );
            RequireResolved(await dispatcher.Dispatch(new RageActionOp(Actor)));
            FactStampRecorder facts = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(facts);
            ExactEndTurnBridgeHarness bridge = new ExactEndTurnBridgeHarness(dispatcher, Encounter);

            InvalidOperationException failure = await EndTurnsUntilFailure(bridge, expected, 24);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(cleanup.SawExpiredDisabledTombstone, Is.True);
            Assert.That(cleanup.SawRageStartBlocked, Is.True);
            Assert.That(cleanup.Attempts, Is.EqualTo(2));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].CurrentTurn.Value.Actor,
                Is.EqualTo(Actor)
            );
            Assert.That(dispatcher.Snapshot.Encounters[Encounter].IsTurnStartPending, Is.False);
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(HasOwnedRageState(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(adapter.ObservedActors.Last(), Is.EqualTo(Actor));
            Assert.That(adapter.ObservedRaging.Last(), Is.False);
            Assert.That(adapter.ObservedRageState.Last(), Is.False);
            Assert.That(facts.Count<ActiveEffectExpiredFact>(), Is.EqualTo(1));
            Assert.That(facts.Count<ActiveEffectRemovedFact>(), Is.EqualTo(1));
            Assert.That(facts.Count<TemporaryHitPointsRemovedFact>(), Is.EqualTo(1));
            Assert.That(facts.Count<TemporaryHitPointImmunityAddedFact>(), Is.EqualTo(1));
            AssertCurrentTurnBeganOnce(dispatcher.Snapshot, facts);
        }

        [TestCase(RageCleanupPostCommitFailure.PostNextMiddleware)]
        [TestCase(RageCleanupPostCommitFailure.FactObserver)]
        [TestCase(RageCleanupPostCommitFailure.ResolvedObserver)]
        public async Task TimedExpirationPostCommitFailureKeepsCleanupDurableAndStartsBoundaryOnce(
            RageCleanupPostCommitFailure failurePoint
        )
        {
            InvalidOperationException expected = new InvalidOperationException(
                $"Injected {failurePoint} cleanup failure."
            );
            RageCleanupFailureMiddleware cleanup =
                failurePoint == RageCleanupPostCommitFailure.PostNextMiddleware
                    ? RageCleanupFailureMiddleware.PostNext(
                        Actor,
                        RageCleanupOperation.CommitRageEnd,
                        expected
                    )
                    : null;
            RageStateTurnStartAdapter adapter = new RageStateTurnStartAdapter();
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                turnStartAdapters: new[] { adapter },
                cleanupFailure: cleanup
            );
            RequireResolved(await dispatcher.Dispatch(new RageActionOp(Actor)));
            FactStampRecorder facts = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(facts);
            IDisposable failureRegistration = null;
            if (failurePoint == RageCleanupPostCommitFailure.FactObserver)
            {
                failureRegistration = dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(
                    new ThrowOnceFactObserver<ActiveEffectRemovedFact>(expected)
                );
            }
            else if (failurePoint == RageCleanupPostCommitFailure.ResolvedObserver)
            {
                failureRegistration = dispatcher.RegisterResolvedOpObserver<
                    CommitRageEndOp,
                    RageEndOutcome
                >(new ThrowOnceResolvedObserver<CommitRageEndOp, RageEndOutcome>(expected));
            }
            using (failureRegistration)
            {
                ExactEndTurnBridgeHarness bridge = new ExactEndTurnBridgeHarness(
                    dispatcher,
                    Encounter
                );

                InvalidOperationException failure = await EndTurnsUntilFailure(
                    bridge,
                    expected,
                    24
                );

                Assert.That(failure, Is.SameAs(expected));
            }

            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].CurrentTurn.Value.Actor,
                Is.EqualTo(Actor)
            );
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(HasOwnedRageState(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(adapter.ObservedActors.Last(), Is.EqualTo(Actor));
            Assert.That(adapter.ObservedRaging.Last(), Is.False);
            Assert.That(adapter.ObservedRageState.Last(), Is.False);
            Assert.That(facts.Count<ActiveEffectExpiredFact>(), Is.EqualTo(1));
            Assert.That(facts.Count<ActiveEffectRemovedFact>(), Is.EqualTo(1));
            Assert.That(facts.Count<TemporaryHitPointsRemovedFact>(), Is.EqualTo(1));
            Assert.That(facts.Count<TemporaryHitPointImmunityAddedFact>(), Is.EqualTo(1));
            AssertCurrentTurnBeganOnce(dispatcher.Snapshot, facts);
        }

        [Test]
        public async Task FailedHostRecoveryAggregatesAndLaterAdvanceResumesExactCheckpoint()
        {
            InvalidOperationException original = new InvalidOperationException(
                "Injected original Rage cleanup failure."
            );
            NotSupportedException recovery = new NotSupportedException(
                "Injected recovery Rage cleanup failure."
            );
            RageCleanupFailureMiddleware cleanup = RageCleanupFailureMiddleware.PreNext(
                Actor,
                RageCleanupOperation.EndRage,
                original,
                recovery
            );
            RageStateTurnStartAdapter adapter = new RageStateTurnStartAdapter();
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                turnStartAdapters: new[] { adapter },
                cleanupFailure: cleanup
            );
            RequireResolved(await dispatcher.Dispatch(new RageActionOp(Actor)));
            ExactEndTurnBridgeHarness bridge = new ExactEndTurnBridgeHarness(dispatcher, Encounter);

            AggregateException failure = await EndTurnsUntilAggregateFailure(bridge, 24);
            EncounterState checkpoint = dispatcher.Snapshot.Encounters[Encounter];

            Assert.That(
                failure.InnerExceptions,
                Is.EqualTo(new Exception[] { original, recovery })
            );
            Assert.That(checkpoint.IsTurnStartPending, Is.True);
            Assert.That(checkpoint.CurrentTurn, Is.Null);
            CreatureId pendingActor = checkpoint.Roster[checkpoint.Cursor].Creature;
            RoundNumber pendingRound = checkpoint.Round;
            int pendingSlot = checkpoint.Cursor;

            EncounterState resumed = RequireResolved(
                await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter))
            ).Value.State;

            Assert.That(resumed.CurrentTurn.Value.Actor, Is.EqualTo(pendingActor));
            Assert.That(resumed.CurrentTurn.Value.Round, Is.EqualTo(pendingRound));
            Assert.That(resumed.CurrentTurn.Value.RosterIndex, Is.EqualTo(pendingSlot));
            Assert.That(resumed.IsTurnStartPending, Is.False);
            Assert.That(adapter.ObservedRageState.Last(), Is.False);
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

        [Test]
        public async Task StaleSuspensionCannotCleanRageOwnedByNewerActiveEncounter()
        {
            EncounterId newerEncounter = new EncounterId("newer-rage-encounter");
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10, 10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0
            );
            RequireResolved(await dispatcher.Dispatch(new SuspendEncounterOp(Encounter)));
            RequireResolved(
                await dispatcher.Dispatch(
                    new StartEncounterOp(
                        newerEncounter,
                        Party,
                        new[]
                        {
                            new EncounterParticipant(Actor, Party, 10),
                            new EncounterParticipant(Enemy, Opposition, 0),
                        }
                    )
                )
            );
            RequireResolved(await dispatcher.Dispatch(new RageActionOp(Actor)));
            FactStampRecorder facts = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(facts);
            RulesSnapshot before = dispatcher.Snapshot;

            InvalidOperationException stale = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new SuspendEncounterOp(Encounter))
            );

            Assert.That(stale.Message, Does.Contain("Encounter encounter is not active"));
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(dispatcher.Snapshot.Health[Actor], Is.EqualTo(before.Health[Actor]));
            Assert.That(dispatcher.Snapshot.ActiveEffects, Is.EqualTo(before.ActiveEffects));
            Assert.That(dispatcher.Snapshot.RuleBindings, Is.EqualTo(before.RuleBindings));
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings,
                Is.EqualTo(before.ActiveEffectTimings)
            );
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
            Assert.That(
                dispatcher.Snapshot.Encounters[newerEncounter].Phase,
                Is.EqualTo(EncounterPhase.Active)
            );
            Assert.That(facts.Facts, Is.Empty);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task EncounterCloseWaitsForPublicMarkerCleanupAndRetry(
            bool suspend,
            bool postNext
        )
        {
            ActiveEffectRegistration marker = CreateRageRegistration(
                $"public-marker-{suspend}-{postNext}",
                new RageEffectState(false),
                RageRules.Source,
                EffectDuration.Indefinite
            );
            InvalidOperationException expected = new InvalidOperationException(
                "Injected encounter-close Rage cleanup failure."
            );
            RageCleanupFailureMiddleware cleanup = postNext
                ? RageCleanupFailureMiddleware.PostNext(
                    Actor,
                    RageCleanupOperation.EndRage,
                    expected
                )
                : RageCleanupFailureMiddleware.PreNext(
                    Actor,
                    RageCleanupOperation.EndRage,
                    expected
                );
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                seededActiveEffects: new[] { marker },
                cleanupFailure: cleanup
            );

            InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    if (suspend)
                    {
                        await dispatcher.Dispatch(new SuspendEncounterOp(Encounter));
                        return;
                    }
                    await dispatcher.Dispatch(
                        new ApplyDamageOp(
                            Enemy,
                            10,
                            new HealthChangeOriginId("close-public-marker-encounter"),
                            RuleSource.FromSlug("test")
                        )
                    );
                }
            );

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(EncounterPhase.Active)
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffects.Contains(marker.Effect.Id),
                Is.EqualTo(!postNext)
            );

            if (suspend)
            {
                RequireResolved(await dispatcher.Dispatch(new SuspendEncounterOp(Encounter)));
            }
            else
            {
                RequireResolved(
                    await dispatcher.Dispatch(
                        new EndEncounterOp(Encounter, EncounterOutcome.PlayerVictory)
                    )
                );
            }

            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(suspend ? EncounterPhase.Suspended : EncounterPhase.Ended)
            );
            Assert.That(HasOwnedRageState(dispatcher.Snapshot, Actor), Is.False);
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

        [Test]
        public async Task EndingSettledRageCommitsCleanupFactsInOneDeterministicTransaction()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState())
            );
            ResolvedOpResult<RageStartOutcome> started = RequireResolved(
                await dispatcher.Dispatch(new RageActionOp(Actor))
            );
            long versionBeforeEnd = dispatcher.Snapshot.Version;

            ResolvedOpResult<RageEndOutcome> ended = RequireResolved(
                await dispatcher.Dispatch(new EndRageOp(Actor))
            );

            Assert.That(ended.Value.Ended, Is.True);
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(versionBeforeEnd + 1));
            Assert.That(
                ended.Facts.Select(fact => fact.GetType()),
                Is.EqualTo(
                    new[]
                    {
                        typeof(TemporaryHitPointsRemovedFact),
                        typeof(TemporaryHitPointImmunityAddedFact),
                        typeof(ActiveEffectRemovedFact),
                    }
                )
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffects.Contains(started.Value.EffectId),
                Is.False
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffectTimings.Contains(started.Value.EffectId),
                Is.False
            );
        }

        [Test]
        public async Task EndingWithoutAnAuthoritativeRageDoesNotCleanAnOrphanedHealthPool()
        {
            HealthState orphanedPool = new HealthState(10, 10, 3, RageRules.Source);
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                actorHealth: orphanedPool
            );

            ResolvedOpResult<RageEndOutcome> ended = RequireResolved(
                await dispatcher.Dispatch(new EndRageOp(Actor))
            );

            Assert.That(ended.Value.Ended, Is.False);
            Assert.That(ended.Facts, Is.Empty);
            Assert.That(dispatcher.Snapshot.Health[Actor], Is.EqualTo(orphanedPool));
        }

        [Test]
        public async Task ExplicitEndRemovesPendingRageWithoutCompletedConsequences()
        {
            HealthState initialHealth = new HealthState(10, 10, 7, OtherTemporaryHitPointSource);
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                actorHealth: initialHealth,
                seedPendingRage: true
            );
            FactStampRecorder recorder = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(recorder);

            ResolvedOpResult<RageEndOutcome> ended = RequireResolved(
                await dispatcher.Dispatch(new EndRageOp(Actor))
            );

            Assert.That(ended.Value.Ended, Is.True);
            AssertPendingRageCleanup(dispatcher.Snapshot, initialHealth, recorder);
        }

        [Test]
        public async Task ExpirationRemovesPendingRageWithoutCompletedConsequences()
        {
            HealthState initialHealth = new HealthState(10, 10, 7, OtherTemporaryHitPointSource);
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                actorHealth: initialHealth,
                seedPendingRage: true
            );
            FactStampRecorder recorder = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(recorder);

            await AdvanceRageToExpiration(dispatcher);

            Assert.That(recorder.Count<ActiveEffectExpiredFact>(), Is.EqualTo(1));
            AssertPendingRageCleanup(dispatcher.Snapshot, initialHealth, recorder);
        }

        [Test]
        public async Task EncounterEndRemovesPendingRageWithoutCompletedConsequences()
        {
            HealthState initialHealth = new HealthState(10, 10, 7, OtherTemporaryHitPointSource);
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                actorHealth: initialHealth,
                seedPendingRage: true
            );
            FactStampRecorder recorder = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(recorder);

            RequireResolved(
                await dispatcher.Dispatch(
                    new ApplyDamageOp(
                        Enemy,
                        10,
                        new HealthChangeOriginId("end-encounter-with-pending-rage"),
                        RuleSource.FromSlug("test")
                    )
                )
            );

            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(EncounterPhase.Ended)
            );
            AssertPendingRageCleanup(dispatcher.Snapshot, initialHealth, recorder);
        }

        [Test]
        public async Task EncounterSuspendRemovesPendingRageWithoutCompletedConsequences()
        {
            HealthState initialHealth = new HealthState(10, 10, 7, OtherTemporaryHitPointSource);
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState()),
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                actorHealth: initialHealth,
                seedPendingRage: true
            );
            FactStampRecorder recorder = new FactStampRecorder();
            using IDisposable recording = dispatcher.RegisterFactObserver<RuleFact>(recorder);

            RequireResolved(await dispatcher.Dispatch(new SuspendEncounterOp(Encounter)));

            Assert.That(
                dispatcher.Snapshot.Encounters[Encounter].Phase,
                Is.EqualTo(EncounterPhase.Suspended)
            );
            AssertPendingRageCleanup(dispatcher.Snapshot, initialHealth, recorder);
        }

        private static void AssertPendingRageCleanup(
            RulesSnapshot snapshot,
            HealthState initialHealth,
            FactStampRecorder recorder
        )
        {
            Assert.That(snapshot.ActiveEffects.TryGet(PendingRageEffect, out _), Is.False);
            Assert.That(snapshot.RuleBindings.TryGet(PendingRageBinding, out _), Is.False);
            Assert.That(snapshot.Health[Actor], Is.EqualTo(initialHealth));
            Assert.That(recorder.Count<ActiveEffectRemovedFact>(), Is.EqualTo(1));
            Assert.That(recorder.Count<TemporaryHitPointsRemovedFact>(), Is.Zero);
            Assert.That(recorder.Count<TemporaryHitPointImmunityAddedFact>(), Is.Zero);
            Assert.That(recorder.Count<TemporaryHitPointsGrantedFact>(), Is.Zero);
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

        private static bool HasOwnedRageState(RulesSnapshot snapshot, CreatureId actor) =>
            snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                && pair.Value.SourceCreature == actor
                && pair.Value.Source == RageRules.Source
            );

        private static void AssertCurrentTurnBeganOnce(
            RulesSnapshot snapshot,
            FactStampRecorder facts
        )
        {
            TurnIdentity current = snapshot.Encounters[Encounter].CurrentTurn.Value;
            TurnBeganFact[] began = facts.OfType<TurnBeganFact>().ToArray();
            Assert.That(began.Count(fact => fact.Turn == current), Is.EqualTo(1));
            Assert.That(
                began.Select(fact => fact.Turn.Turn).Distinct().Count(),
                Is.EqualTo(began.Length)
            );
        }

        private static async Task<InvalidOperationException> EndTurnsUntilFailure(
            ExactEndTurnBridgeHarness bridge,
            InvalidOperationException expected,
            int maximumTurns
        )
        {
            for (int attempt = 0; attempt < maximumTurns; attempt++)
            {
                CreatureId actor = bridge.Current.CurrentTurn.Value.Actor;
                try
                {
                    await bridge.EndTurn(actor);
                }
                catch (InvalidOperationException failure) when (ReferenceEquals(failure, expected))
                {
                    return failure;
                }
            }
            Assert.Fail("The expected Rage cleanup failure was not reached.");
            return null;
        }

        private static async Task<AggregateException> EndTurnsUntilAggregateFailure(
            ExactEndTurnBridgeHarness bridge,
            int maximumTurns
        )
        {
            for (int attempt = 0; attempt < maximumTurns; attempt++)
            {
                CreatureId actor = bridge.Current.CurrentTurn.Value.Actor;
                try
                {
                    await bridge.EndTurn(actor);
                }
                catch (AggregateException failure)
                {
                    return failure;
                }
            }
            Assert.Fail("The expected aggregate Rage cleanup failure was not reached.");
            return null;
        }

        private static void AssertRejectedRageCleanup(
            CommitRageEndOp operation,
            OpId root,
            ActiveEffectInstance effect,
            ActiveRuleBinding binding,
            ActiveEffectTimingState timing
        )
        {
            HealthState health = new HealthState(10, 10, 3, RageRules.Source);
            FrequencyState frequency = new FrequencyState(Encounter, 1, 1);
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Party))
                .SeedHealth(Actor, health)
                .SeedActiveEffect(effect)
                .SeedRuleBinding(binding)
                .SeedFrequency(binding.Id, frequency);
            if (timing != null)
                seed.SeedActiveEffectTiming(timing);
            InMemoryRulesStore store = new InMemoryRulesStore(seed);
            RulesSnapshot before = store.Snapshot;

            ReductionResult<RageEndOutcome> result = store.Reduce(
                new ReductionContext<CommitRageEndOp>(
                    operation,
                    new OpId(root.Value + 1),
                    root,
                    RageRules.Source
                ),
                new CommitRageEndReducer()
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(before.Version));
            Assert.That(store.Snapshot.Health[Actor], Is.EqualTo(health));
            Assert.That(store.Snapshot.ActiveEffects[effect.Id], Is.EqualTo(effect));
            Assert.That(store.Snapshot.RuleBindings[binding.Id], Is.EqualTo(binding));
            Assert.That(store.Snapshot.Frequencies[binding.Id], Is.EqualTo(frequency));
            Assert.That(
                store.Snapshot.ActiveEffectTimings.Contains(effect.Id),
                Is.EqualTo(timing != null)
            );
            if (timing != null)
                Assert.That(store.Snapshot.ActiveEffectTimings[effect.Id], Is.EqualTo(timing));
        }

        private static RuleDispatcher CreateDispatcher(
            ITestRageStateProvider provider,
            bool isFatigued = false,
            bool isEncumbered = false,
            IEnumerable<ActiveEffectRegistration> seededActiveEffects = null,
            RageCleanupFailureMiddleware cleanupFailure = null
        )
        {
            return CreateDispatcher(
                provider,
                new ScriptedRollService(10, 10),
                actorInitiativeModifier: 10,
                enemyInitiativeModifier: 0,
                isFatigued: isFatigued,
                isEncumbered: isEncumbered,
                seededActiveEffects: seededActiveEffects,
                cleanupFailure: cleanupFailure
            );
        }

        private static RuleDispatcher CreateDispatcher(
            ITestRageStateProvider provider,
            ScriptedRollService rolls,
            int actorInitiativeModifier,
            int enemyInitiativeModifier,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters = null,
            bool isFatigued = false,
            bool isEncumbered = false,
            HealthState? actorHealth = null,
            bool seedPendingRage = false,
            IEnumerable<ActiveEffectRegistration> seededActiveEffects = null,
            RageCleanupFailureMiddleware cleanupFailure = null
        )
        {
            RageActionDefinition definition = new RageActionDefinition();
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            registryBuilder.Define(ConditionRuleDefinitions.Fatigued);
            registryBuilder.Define(ConditionRuleDefinitions.Encumbered);
            if (cleanupFailure != null)
            {
                registryBuilder
                    .Define(CleanupFailureDefinition)
                    .Middleware<EndRageOp, RageEndOutcome>(
                        RuleLifecyclePhase.Prevention,
                        cleanupFailure
                    )
                    .Middleware<CommitRageEndOp, RageEndOutcome>(
                        RuleLifecyclePhase.Prevention,
                        cleanupFailure
                    );
            }
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Party))
                .SeedCreature(new CreatureState(Enemy, Opposition))
                .SeedHealth(Actor, actorHealth ?? new HealthState(10, 10))
                .SeedHealth(Enemy, new HealthState(10, 10))
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true));
            SeedPreparedActor(seed, registryBuilder, Actor, provider.State);
            foreach (
                ActiveRuleBinding binding in RageRules.CreateInitialBindings(Actor, provider.State)
            )
            {
                seed.SeedRuleBinding(binding);
            }
            if (cleanupFailure != null)
            {
                seed.SeedRuleBinding(
                    new ActiveRuleBinding(
                        CleanupFailureBinding,
                        CleanupFailureDefinition,
                        Actor,
                        null,
                        RuleSource.FromSlug("test-rage-cleanup-failure"),
                        500
                    )
                );
            }
            foreach (
                ActiveEffectRegistration registration in seededActiveEffects
                    ?? Array.Empty<ActiveEffectRegistration>()
            )
            {
                seed.SeedActiveEffect(registration.Effect).SeedRuleBinding(registration.Binding);
                if (registration.Timing != null)
                    seed.SeedActiveEffectTiming(registration.Timing);
            }
            if (isFatigued)
                SeedMarker(seed, ConditionRuleDefinitions.Fatigued, "fatigued");
            if (isEncumbered)
                SeedMarker(seed, ConditionRuleDefinitions.Encumbered, "encumbered");
            if (seedPendingRage)
                SeedPendingRage(seed);

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
            CreatureId actor,
            RageActorState state
        )
        {
            List<PreparedBoundOption> options = new List<PreparedBoundOption>();
            List<ActiveRuleBinding> bindings = new List<ActiveRuleBinding>();
            AddOwned("rage", state.OwnsRage);
            AddOwned("quick-tempered", state.OwnsQuickTempered);
            AddOwned("invulnerable-rager", state.HasInvulnerableRager);
            seed.SeedPreparedInputs(
                actor,
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
                        new BindingId($"prepared-test-{actor.Value}-{slug}"),
                        definition,
                        actor,
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
            bool hasInvulnerableRager = false,
            int level = 1,
            int constitutionModifier = 2
        ) =>
            new RageActorState(
                ownsRage,
                ownsQuickTempered,
                wearsHeavyArmor,
                hasInvulnerableRager,
                level,
                constitutionModifier
            );

        private static PreparedCreatureInputs PreparedInputsFor(RageActorState state) =>
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
                new[]
                {
                    state.OwnsRage ? "item:owned:rage" : string.Empty,
                    state.OwnsQuickTempered ? "item:owned:quick-tempered" : string.Empty,
                    state.HasInvulnerableRager ? "item:owned:invulnerable-rager" : string.Empty,
                }.Where(option => !string.IsNullOrEmpty(option))
            );

        private static RuleDispatcher CreateBatchQuickTemperedDispatcher(
            IReadOnlyDictionary<CreatureId, RageActorState> actors,
            ScriptedRollService rolls
        )
        {
            RageActionDefinition definition = new RageActionDefinition();
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            ConditionRuleDefinitions.DefineAll(registryBuilder);
            registryBuilder.AddOutcomeRule();
            RuleRegistry registry = registryBuilder.Build();
            RulesStateSeed seed = new RulesStateSeed();
            foreach (KeyValuePair<CreatureId, RageActorState> pair in actors)
            {
                PlayerId player = pair.Key == Enemy ? Opposition : Party;
                seed.SeedCreature(new CreatureState(pair.Key, player))
                    .SeedPreparedInputs(pair.Key, PreparedInputsFor(pair.Value))
                    .SeedHealth(pair.Key, new HealthState(10, 10))
                    .SeedActionEconomy(pair.Key, new ActionEconomyState(3, true))
                    .SeedMultipleAttackPenalty(pair.Key, new MultipleAttackPenaltyState(0));
                foreach (
                    ActiveRuleBinding binding in RageRules.CreateInitialBindings(
                        pair.Key,
                        pair.Value
                    )
                )
                    seed.SeedRuleBinding(binding);
            }
            return new RuleDispatcherBuilder(new InMemoryRulesStore(seed), rolls)
                .UseCheckResolution()
                .UseHealthRules()
                .UseMultipleAttackPenaltyRules()
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .UseMovementBudgetResetRules()
                .UseEncounterRules(Array.Empty<IEncounterTurnStartAdapter>(), registry)
                .UseActionLifecycle(definition)
                .UseRageRules(definition)
                .Build();
        }

        private static EncounterJoinParticipant QuickTemperedJoinParticipant(
            CreatureId actor,
            RageActorState state
        ) =>
            new EncounterJoinParticipant(
                new EncounterParticipant(actor, Party, 0),
                new CombatantRulesState(
                    new CreatureState(actor, Party),
                    new HealthState(10, 10),
                    new GridPosition(0, 0, 0),
                    new GridDistance(25),
                    CreatureStatisticsState.Empty(actor),
                    PreparedInputsFor(state),
                    Array.Empty<SpellSlotState>(),
                    RageRules.CreateInitialBindings(actor, state)
                )
            );

        private static void AssertTwoSettledQuickTemperedRages(
            RulesSnapshot snapshot,
            CreatureId first,
            CreatureId second
        )
        {
            CreatureId[] actors = { first, second };
            ActiveEffectInstance[] effects = snapshot
                .ActiveEffects.Select(pair => pair.Value)
                .Where(effect =>
                    effect.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && actors.Contains(effect.SourceCreature)
                )
                .ToArray();
            Assert.That(effects, Has.Length.EqualTo(2));
            Assert.That(effects.Select(effect => effect.Id).Distinct().Count(), Is.EqualTo(2));
            RageEffectState[] receipts = effects
                .Select(effect => effect.State)
                .OfType<RageEffectState>()
                .ToArray();
            Assert.That(receipts, Has.Length.EqualTo(2));
            Assert.That(receipts.All(receipt => receipt.HasWorkflowReceipt), Is.True);
            Assert.That(receipts.All(receipt => receipt.Phase == RageStartPhase.Settled), Is.True);
            Assert.That(receipts.All(receipt => receipt.HasGrantOutcome), Is.True);
            Assert.That(
                receipts.Select(receipt => receipt.BindingId).Distinct().Count(),
                Is.EqualTo(2)
            );
            Assert.That(
                receipts.Select(receipt => receipt.Origin).Distinct().Count(),
                Is.EqualTo(2)
            );
            foreach (CreatureId actor in actors)
            {
                Assert.That(RageRules.IsRaging(snapshot, actor), Is.True);
                BindingId triggerId = new BindingId($"quick-tempered-{actor.Value}");
                Assert.That(snapshot.RuleBindings[triggerId].IsEnabled, Is.False);
            }
        }

        private static RageEffectState CreateWorkflowReceipt(
            OpId root,
            int offeredTemporaryHitPoints,
            int committedTemporaryHitPoints
        )
        {
            HealthState before = new HealthState(10, 10);
            HealthState after = new HealthState(
                10,
                10,
                committedTemporaryHitPoints,
                RageRules.Source
            ).WithTemporaryHitPointRevision(1);
            TemporaryHitPointsGrantTransition transition = new TemporaryHitPointsGrantTransition(
                before,
                after,
                new TemporaryHitPointsGrantOutcome(true, false, 0, committedTemporaryHitPoints)
            );
            return RageEffectState
                .CreatePending(Actor, default, root, offeredTemporaryHitPoints)
                .WithGrantTransition(transition)
                .Settle();
        }

        private static ActiveEffectRegistration CreateRageRegistration(
            string identity,
            RageEffectState state,
            RuleSource source,
            EffectDuration duration
        )
        {
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId($"{identity}-effect"),
                RageActionDefinition.EffectDefinitionId,
                Actor,
                source,
                duration,
                state
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId($"{identity}-binding"),
                effect.DefinitionId,
                Actor,
                effect.Id,
                source,
                50
            );
            return new ActiveEffectRegistration(effect, binding);
        }

        private static async Task AssertBlockedQuickTemperedWorkflow(
            RageActorState quickTempered,
            HealthState initialHealth,
            WorkflowFailureKind failureKind,
            bool injectEffectObserverFailure,
            bool grantCheckpointExpected
        )
        {
            RageActorState inactive = CreateActorState(ownsRage: false);
            DictionaryRageActorStateProvider provider = new DictionaryRageActorStateProvider(
                new Dictionary<CreatureId, RageActorState>
                {
                    [Actor] = inactive,
                    [Reinforcement] = quickTempered,
                }
            );
            InvalidOperationException pipelineFailure = new InvalidOperationException(
                $"Injected {failureKind} failure."
            );
            WorkflowFailureMiddleware failureMiddleware = new WorkflowFailureMiddleware(
                Reinforcement,
                failureKind,
                pipelineFailure
            );
            ScriptedRollService rolls = new ScriptedRollService(20, 10, 5);
            RuleDispatcher dispatcher = CreateJoinRetryDispatcher(
                provider,
                rolls,
                failureMiddleware
            );
            ActiveRuleBinding grantFailureBinding = new ActiveRuleBinding(
                new BindingId($"test-rage-grant-failure-{Reinforcement.Value}"),
                GrantFailureDefinition,
                Reinforcement,
                default,
                RuleSource.FromSlug("test-rage-grant-failure"),
                2
            );
            CombatantRulesState registration = new CombatantRulesState(
                new CreatureState(Reinforcement, Opposition),
                initialHealth,
                new GridPosition(2, 0, 0),
                new GridDistance(25),
                CreatureStatisticsState.Empty(Reinforcement),
                PreparedInputsFor(quickTempered),
                Array.Empty<SpellSlotState>(),
                RageRules
                    .CreateInitialBindings(Reinforcement, quickTempered)
                    .Append(grantFailureBinding)
                    .ToArray()
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
            InvalidOperationException effectFailure = new InvalidOperationException(
                "Injected Rage effect observer failure."
            );
            IDisposable effectFailureRegistration = injectEffectObserverFailure
                ? dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(
                    new ThrowOnceFactObserver<ActiveEffectCreatedFact>(effectFailure)
                )
                : null;

            bool hasNewerRagePool =
                failureKind == WorkflowFailureKind.GrantPostNextInvalidWithChangedRagePool
                || failureKind == WorkflowFailureKind.GrantPostNextInvalidWithAbaPool;
            if (injectEffectObserverFailure)
            {
                AggregateException failure = Assert.ThrowsAsync<AggregateException>(async () =>
                    await dispatcher.Dispatch(join)
                );
                Assert.That(
                    failure
                        .Flatten()
                        .InnerExceptions.Any(value => ReferenceEquals(value, effectFailure)),
                    Is.True
                );
                Assert.That(
                    failure
                        .Flatten()
                        .InnerExceptions.Any(value => ReferenceEquals(value, pipelineFailure)),
                    Is.True
                );
            }
            else if (failureKind == WorkflowFailureKind.GrantInvalidWithCleanupThrow)
            {
                AggregateException failure = Assert.ThrowsAsync<AggregateException>(async () =>
                    await dispatcher.Dispatch(join)
                );
                Assert.That(
                    failure.Flatten().InnerExceptions.Select(value => value.Message),
                    Does.Contain(WorkflowFailureMiddleware.InvalidReason)
                );
                Assert.That(
                    failure
                        .Flatten()
                        .InnerExceptions.Any(value => ReferenceEquals(value, pipelineFailure)),
                    Is.True
                );
            }
            else if (hasNewerRagePool)
            {
                AggregateException failure = Assert.ThrowsAsync<AggregateException>(async () =>
                    await dispatcher.Dispatch(join)
                );
                Assert.That(
                    failure.Flatten().InnerExceptions.Select(value => value.Message),
                    Does.Contain(WorkflowFailureMiddleware.InvalidReason)
                );
                Assert.That(
                    failure.Flatten().InnerExceptions.Select(value => value.Message),
                    Does.Contain(
                        "Temporary Hit Point compensation cannot discard a newer pool from the abandoned source."
                    )
                );
            }
            else if (
                failureKind == WorkflowFailureKind.GrantInvalid
                || failureKind == WorkflowFailureKind.SettlementInvalid
                || failureKind == WorkflowFailureKind.GrantPostNextInvalid
                || failureKind == WorkflowFailureKind.GrantPostNextInvalidWithInterveningPool
                || failureKind == WorkflowFailureKind.GrantPostNextInvalidWithConsumedPool
                || failureKind == WorkflowFailureKind.GrantPostNextInvalidWithHealing
            )
                Assert.That(
                    Assert
                        .ThrowsAsync<InvalidOperationException>(async () =>
                            await dispatcher.Dispatch(join)
                        )
                        .Message,
                    Is.EqualTo(WorkflowFailureMiddleware.InvalidReason)
                );
            else if (
                failureKind == WorkflowFailureKind.GrantInterrupted
                || failureKind == WorkflowFailureKind.GrantPostNextInterrupted
            )
                Assert.That(
                    Assert
                        .ThrowsAsync<InvalidOperationException>(async () =>
                            await dispatcher.Dispatch(join)
                        )
                        .Message,
                    Is.EqualTo("Rage temporary Hit Point grant was interrupted.")
                );
            else
                Assert.That(
                    Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await dispatcher.Dispatch(join)
                    ),
                    Is.SameAs(pipelineFailure)
                );

            Assert.That(failureMiddleware.SawCommittedEffectBeforeFailure, Is.True);
            Assert.That(failureMiddleware.SawEnabledTriggerBeforeFailure, Is.True);
            Assert.That(failureMiddleware.GrantAttempts, Is.EqualTo(1));
            bool reachesSettlement =
                failureKind == WorkflowFailureKind.SettlementInvalid
                || failureKind == WorkflowFailureKind.SettlementPreNextThrow;
            Assert.That(
                failureMiddleware.SettlementAttempts,
                Is.EqualTo(reachesSettlement ? 1 : 0)
            );
            Assert.That(failureMiddleware.CleanupAttempts, Is.EqualTo(1));

            RulesSnapshot snapshot = dispatcher.Snapshot;
            int offeredTemporaryHitPoints = Math.Max(
                0,
                quickTempered.Level + quickTempered.ConstitutionModifier
            );
            bool rageGrantChangedHealth =
                !initialHealth.HasTemporaryHitPointImmunity(RageRules.Source)
                && offeredTemporaryHitPoints > initialHealth.Temporary;
            bool hasInterveningPool =
                failureKind == WorkflowFailureKind.GrantPostNextInvalidWithInterveningPool;
            bool hasAbaPool = failureKind == WorkflowFailureKind.GrantPostNextInvalidWithAbaPool;
            bool hasChangedRagePool =
                failureKind == WorkflowFailureKind.GrantPostNextInvalidWithChangedRagePool;
            bool consumedPool =
                failureKind == WorkflowFailureKind.GrantPostNextInvalidWithConsumedPool;
            bool unrelatedHealing =
                failureKind == WorkflowFailureKind.GrantPostNextInvalidWithHealing;
            bool cleanupRejected = hasAbaPool || hasChangedRagePool;
            Assert.That(
                snapshot.ActiveEffects.Any(pair =>
                    pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && pair.Value.SourceCreature == Reinforcement
                ),
                Is.EqualTo(
                    failureKind == WorkflowFailureKind.GrantInvalidWithCleanupThrow
                        || cleanupRejected
                )
            );
            Assert.That(
                snapshot.RuleBindings.Any(pair =>
                    pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && pair.Value.Owner == Reinforcement
                ),
                Is.EqualTo(
                    failureKind == WorkflowFailureKind.GrantInvalidWithCleanupThrow
                        || cleanupRejected
                )
            );
            Assert.That(RageRules.IsRaging(snapshot, Reinforcement), Is.False);
            if (grantCheckpointExpected)
            {
                HealthState expectedHealth =
                    failureKind == WorkflowFailureKind.GrantPostNextInvalidWithInterveningPool
                        ? new HealthState(10, 10, 9, OtherTemporaryHitPointSource)
                    : failureKind == WorkflowFailureKind.GrantPostNextInvalidWithChangedRagePool
                        ? new HealthState(10, 10, 9, RageRules.Source)
                    : failureKind == WorkflowFailureKind.GrantPostNextInvalidWithAbaPool
                        ? new HealthState(10, 10, offeredTemporaryHitPoints, RageRules.Source)
                    : failureKind == WorkflowFailureKind.GrantPostNextInvalidWithConsumedPool
                        ? new HealthState(10, 10)
                    : failureKind == WorkflowFailureKind.GrantPostNextInvalidWithHealing
                        ? new HealthState(
                            initialHealth.Current + 1,
                            initialHealth.Maximum,
                            initialHealth.Temporary,
                            initialHealth.TemporarySource,
                            new[] { UnrelatedImmunitySource }
                        )
                    : initialHealth;
                Assert.That(snapshot.Health[Reinforcement], Is.EqualTo(expectedHealth));
                Assert.That(
                    recorder.Count<TemporaryHitPointsGrantedFact>(),
                    Is.EqualTo(
                        (rageGrantChangedHealth ? 1 : 0)
                            + (hasInterveningPool || hasAbaPool || hasChangedRagePool ? 1 : 0)
                    )
                );
                Assert.That(recorder.Count<ActiveEffectStateUpdatedFact>(), Is.EqualTo(1));
                Assert.That(
                    recorder.Count<TemporaryHitPointsPoolRestoredFact>(),
                    Is.EqualTo(
                        rageGrantChangedHealth
                        && !hasInterveningPool
                        && !hasAbaPool
                        && !hasChangedRagePool
                        && !consumedPool
                            ? 1
                            : 0
                    )
                );
                if (
                    rageGrantChangedHealth
                    && !hasInterveningPool
                    && !hasAbaPool
                    && !hasChangedRagePool
                    && !consumedPool
                )
                {
                    TemporaryHitPointsPoolRestoredFact restored = recorder
                        .OfType<TemporaryHitPointsPoolRestoredFact>()
                        .Single();
                    ActiveEffectRemovedFact removed = recorder
                        .OfType<ActiveEffectRemovedFact>()
                        .Single();
                    Assert.That(restored.SourceOpId, Is.EqualTo(removed.SourceOpId));
                    Assert.That(restored.RootOpId, Is.EqualTo(removed.RootOpId));
                    Assert.That(restored.Source, Is.EqualTo(RageRules.Source));
                    Assert.That(removed.Source, Is.EqualTo(RageRules.Source));
                    Assert.That(restored.AbandonedSource, Is.EqualTo(RageRules.Source));
                    Assert.That(restored.AbandonedAmount, Is.EqualTo(offeredTemporaryHitPoints));
                    Assert.That(restored.RestoredAmount, Is.EqualTo(initialHealth.Temporary));
                    Assert.That(restored.RestoredSource, Is.EqualTo(initialHealth.TemporarySource));
                    Assert.That(
                        snapshot.Health[Reinforcement].TemporaryHitPointRevision,
                        Is.EqualTo(initialHealth.TemporaryHitPointRevision + 2)
                    );
                }
            }
            else
            {
                Assert.That(snapshot.Health[Reinforcement], Is.EqualTo(initialHealth));
                Assert.That(recorder.Count<TemporaryHitPointsGrantedFact>(), Is.Zero);
                Assert.That(recorder.Count<ActiveEffectStateUpdatedFact>(), Is.Zero);
            }
            Assert.That(
                recorder.Count<ActiveEffectRemovedFact>(),
                Is.EqualTo(
                    failureKind == WorkflowFailureKind.GrantInvalidWithCleanupThrow
                    || cleanupRejected
                        ? 0
                        : 1
                )
            );
            Assert.That(
                recorder.Count<TemporaryHitPointsRemovedFact>(),
                Is.EqualTo(hasAbaPool ? 1 : 0)
            );
            Assert.That(
                recorder.Count<TemporaryHitPointImmunityAddedFact>(),
                Is.EqualTo(unrelatedHealing ? 1 : 0)
            );
            Assert.That(
                recorder.Count<TemporaryHitPointsConsumedFact>(),
                Is.EqualTo(consumedPool ? 1 : 0)
            );
            Assert.That(recorder.Count<HealingAppliedFact>(), Is.EqualTo(unrelatedHealing ? 1 : 0));
            Assert.That(
                snapshot
                    .RuleBindings[new BindingId($"quick-tempered-{Reinforcement.Value}")]
                    .IsEnabled,
                Is.True
            );
            Assert.That(recorder.Count<QuickTemperedTriggerConsumedFact>(), Is.Zero);
            Assert.That(rolls.Remaining, Is.Zero);

            long committedVersion = snapshot.Version;
            int committedFactCount = recorder.Facts.Count;
            ResolvedOpResult<EncounterJoinOutcome> retry = RequireResolved(
                await dispatcher.Dispatch(join)
            );
            Assert.That(retry.Facts, Is.Empty);
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(recorder.Facts, Has.Count.EqualTo(committedFactCount));
            Assert.That(failureMiddleware.GrantAttempts, Is.EqualTo(1));
            Assert.That(
                failureMiddleware.SettlementAttempts,
                Is.EqualTo(reachesSettlement ? 1 : 0)
            );
            Assert.That(
                dispatcher.Snapshot.ActiveEffects.Any(pair =>
                    pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && pair.Value.SourceCreature == Reinforcement
                ),
                Is.EqualTo(
                    cleanupRejected
                        || failureKind == WorkflowFailureKind.GrantInvalidWithCleanupThrow
                )
            );
            effectFailureRegistration?.Dispose();
        }

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
            InvalidOperationException expectedFailure = new InvalidOperationException(
                $"Injected {failurePoint} failure."
            );
            QuickWorkflowProbe probe = new QuickWorkflowProbe(
                Reinforcement,
                failurePoint,
                expectedFailure
            );
            RuleDispatcher dispatcher = CreateJoinRetryDispatcher(provider, rolls, null, probe);
            ActiveRuleBinding grantProbeBinding = new ActiveRuleBinding(
                new BindingId($"test-rage-grant-probe-{Reinforcement.Value}"),
                GrantProbeDefinition,
                Reinforcement,
                default,
                RuleSource.FromSlug("test-rage-grant-probe"),
                2
            );
            ActiveRuleBinding quickActionProbeBinding = new ActiveRuleBinding(
                new BindingId($"test-rage-quick-action-probe-{Reinforcement.Value}"),
                QuickActionProbeDefinition,
                Reinforcement,
                default,
                RuleSource.FromSlug("test-rage-quick-action-probe"),
                3
            );
            CombatantRulesState registration = new CombatantRulesState(
                new CreatureState(Reinforcement, Opposition),
                new HealthState(10, 10),
                new GridPosition(2, 0, 0),
                new GridDistance(25),
                CreatureStatisticsState.Empty(Reinforcement),
                PreparedInputsFor(quickTempered),
                Array.Empty<SpellSlotState>(),
                RageRules
                    .CreateInitialBindings(Reinforcement, quickTempered)
                    .Append(grantProbeBinding)
                    .Append(quickActionProbeBinding)
                    .ToArray()
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
            using IDisposable grantObservation = dispatcher.RegisterResolvedOpObserver<
                GrantTemporaryHitPointsOp,
                TemporaryHitPointsGrantOutcome
            >(probe);
            using IDisposable quickActionObservation = dispatcher.RegisterResolvedOpObserver<
                QuickTemperedRageActionOp,
                RageStartOutcome
            >(probe);
            using IDisposable settlementObservation = dispatcher.RegisterResolvedOpObserver<
                SettleRageStartOp,
                RageStartOutcome
            >(probe);
            using IDisposable triggerObservation = dispatcher.RegisterResolvedOpObserver<
                ConsumeQuickTemperedTriggerOp,
                QuickTemperedTriggerConsumedOutcome
            >(probe);
            IDisposable failureRegistration = failurePoint switch
            {
                JoinRetryFailurePoint.EncounterJoinedObserver =>
                    dispatcher.RegisterFactObserver<EncounterJoinedFact>(
                        new ThrowOnceFactObserver<EncounterJoinedFact>(expectedFailure)
                    ),
                JoinRetryFailurePoint.ActiveEffectCreatedObserver =>
                    dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(
                        new ThrowOnceFactObserver<ActiveEffectCreatedFact>(expectedFailure)
                    ),
                JoinRetryFailurePoint.TemporaryHitPointsGrantedObserver =>
                    dispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(
                        new ThrowOnceFactObserver<TemporaryHitPointsGrantedFact>(expectedFailure)
                    ),
                JoinRetryFailurePoint.TriggerConsumedObserver =>
                    dispatcher.RegisterFactObserver<QuickTemperedTriggerConsumedFact>(
                        new ThrowOnceFactObserver<QuickTemperedTriggerConsumedFact>(expectedFailure)
                    ),
                JoinRetryFailurePoint.QuickTemperedRootSettlement =>
                    dispatcher.RegisterRootSettlementObserver(
                        new ThrowOnceQuickTemperedSettlementObserver(Reinforcement, expectedFailure)
                    ),
                _ => null,
            };

            if (failurePoint.HasValue)
            {
                InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
                    async () =>
                        await dispatcher.Dispatch(join)
                );
                Assert.That(actual, Is.SameAs(expectedFailure));
                long committedVersion = dispatcher.Snapshot.Version;
                int committedFactCount = recorder.Facts.Count;
                AssertCompletedQuickTemperedWorkflow(dispatcher.Snapshot, recorder);

                ResolvedOpResult<EncounterJoinOutcome> retry = RequireResolved(
                    await dispatcher.Dispatch(join)
                );

                Assert.That(retry.Facts, Is.Empty);
                Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(committedVersion));
                Assert.That(recorder.Facts, Has.Count.EqualTo(committedFactCount));
                Assert.That(rolls.Remaining, Is.Zero, "A Join retry must not reroll initiative.");
                AssertCompletedQuickTemperedWorkflow(dispatcher.Snapshot, recorder);

                CombatantRulesState conflictingRegistration = new CombatantRulesState(
                    registration.Creature,
                    new HealthState(9, 10),
                    registration.Position,
                    registration.LandSpeed,
                    registration.Statistics,
                    registration.PreparedInputs,
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
                failureRegistration?.Dispose();
            }
            else
            {
                RequireResolved(await dispatcher.Dispatch(join));
                Assert.That(rolls.Remaining, Is.Zero);
            }

            Assert.That(probe.GrantMiddlewareCount, Is.EqualTo(1));
            Assert.That(
                probe.GrantObserverCount,
                Is.EqualTo(
                    failurePoint == JoinRetryFailurePoint.GrantPostNextMiddleware
                    || failurePoint == JoinRetryFailurePoint.TemporaryHitPointsGrantedObserver
                        ? 0
                        : 1
                )
            );
            Assert.That(probe.SettlementMiddlewareCount, Is.EqualTo(1));
            Assert.That(
                probe.SettlementObserverCount,
                Is.EqualTo(
                    failurePoint == JoinRetryFailurePoint.SettlementPostNextMiddleware ? 0 : 1
                )
            );
            Assert.That(probe.QuickActionMiddlewareCount, Is.EqualTo(1));
            Assert.That(
                probe.QuickActionObserverCount,
                Is.EqualTo(
                    failurePoint == JoinRetryFailurePoint.QuickActionPostNextMiddleware
                    || failurePoint == JoinRetryFailurePoint.ActiveEffectCreatedObserver
                    || failurePoint == JoinRetryFailurePoint.TemporaryHitPointsGrantedObserver
                    || failurePoint == JoinRetryFailurePoint.GrantPostNextMiddleware
                    || failurePoint == JoinRetryFailurePoint.GrantResolvedObserver
                    || failurePoint == JoinRetryFailurePoint.SettlementPostNextMiddleware
                    || failurePoint == JoinRetryFailurePoint.SettlementResolvedObserver
                        ? 0
                        : 1
                )
            );
            Assert.That(probe.TriggerMiddlewareCount, Is.EqualTo(1));
            Assert.That(
                probe.TriggerObserverCount,
                Is.EqualTo(
                    failurePoint == JoinRetryFailurePoint.TriggerPostNextMiddleware
                    || failurePoint == JoinRetryFailurePoint.TriggerConsumedObserver
                        ? 0
                        : 1
                )
            );

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

        private static void AssertCompletedQuickTemperedWorkflow(
            RulesSnapshot snapshot,
            FactStampRecorder recorder
        )
        {
            BindingId triggerId = new BindingId($"quick-tempered-{Reinforcement.Value}");
            Assert.That(RageRules.IsRaging(snapshot, Reinforcement), Is.True);
            ActiveEffectInstance effect = snapshot
                .ActiveEffects.Select(pair => pair.Value)
                .Single(value =>
                    value.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && value.SourceCreature == Reinforcement
                );
            Assert.That(effect.State, Is.TypeOf<RageEffectState>());
            RageEffectState receipt = (RageEffectState)effect.State;
            Assert.That(receipt.Phase, Is.EqualTo(RageStartPhase.Settled));
            Assert.That(receipt.HasGrantOutcome, Is.True);
            Assert.That(snapshot.Health[Reinforcement].Temporary, Is.EqualTo(3));
            Assert.That(
                snapshot.Health[Reinforcement].TemporarySource,
                Is.EqualTo(RageRules.Source)
            );
            Assert.That(snapshot.RuleBindings[triggerId].IsEnabled, Is.False);
            Assert.That(recorder.Count<InitiativeAssignedFact>(), Is.EqualTo(1));
            Assert.That(recorder.Count<ActiveEffectCreatedFact>(), Is.EqualTo(1));
            Assert.That(recorder.Count<ActiveEffectStateUpdatedFact>(), Is.EqualTo(2));
            Assert.That(recorder.Count<TemporaryHitPointsGrantedFact>(), Is.EqualTo(1));
            Assert.That(recorder.Count<QuickTemperedTriggerConsumedFact>(), Is.EqualTo(1));
        }

        private static RuleDispatcher CreateJoinRetryDispatcher(
            DictionaryRageActorStateProvider provider,
            ScriptedRollService rolls,
            WorkflowFailureMiddleware failureMiddleware = null,
            QuickWorkflowProbe probe = null,
            IReadOnlyList<GrantPhaseProbe> phaseProbes = null
        )
        {
            RageActionDefinition definition = new RageActionDefinition();
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            registryBuilder.Define(ConditionRuleDefinitions.Fatigued);
            registryBuilder.Define(ConditionRuleDefinitions.Encumbered);
            if (failureMiddleware != null)
            {
                registryBuilder
                    .Define(GrantFailureDefinition)
                    .Middleware<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>(
                        RuleLifecyclePhase.Prevention,
                        failureMiddleware
                    )
                    .Middleware<SettleRageStartOp, RageStartOutcome>(
                        RuleLifecyclePhase.Prevention,
                        failureMiddleware
                    )
                    .Middleware<AbortPendingRageStartOp, ActiveEffectRemovalOutcome>(
                        RuleLifecyclePhase.Prevention,
                        failureMiddleware
                    );
            }
            if (probe != null)
            {
                registryBuilder
                    .Define(GrantProbeDefinition)
                    .Middleware<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>(
                        RuleLifecyclePhase.Observation,
                        probe
                    )
                    .Middleware<SettleRageStartOp, RageStartOutcome>(
                        RuleLifecyclePhase.Observation,
                        probe
                    );
                registryBuilder
                    .Define(QuickActionProbeDefinition)
                    .Middleware<QuickTemperedRageActionOp, RageStartOutcome>(
                        RuleLifecyclePhase.Observation,
                        probe
                    )
                    .Middleware<ConsumeQuickTemperedTriggerOp, QuickTemperedTriggerConsumedOutcome>(
                        RuleLifecyclePhase.Observation,
                        probe
                    );
            }
            if (phaseProbes != null)
            {
                foreach (GrantPhaseProbe phaseProbe in phaseProbes)
                {
                    registryBuilder
                        .Define(phaseProbe.DefinitionId)
                        .Middleware<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>(
                            phaseProbe.Phase,
                            phaseProbe
                        );
                }
            }
            registryBuilder.AddOutcomeRule();
            RuleRegistry registry = registryBuilder.Build();
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Party))
                .SeedCreature(new CreatureState(Enemy, Opposition))
                .SeedHealth(Actor, new HealthState(10, 10))
                .SeedHealth(Enemy, new HealthState(10, 10));
            SeedPreparedActor(seed, registryBuilder, Actor, provider.Get(Actor));
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

        private static void SeedPendingRage(RulesStateSeed seed)
        {
            RageEffectState receipt = RageEffectState.CreatePending(
                Actor,
                default,
                PendingRageRoot,
                3
            );
            seed.SeedActiveEffect(
                    new ActiveEffectInstance(
                        PendingRageEffect,
                        RageActionDefinition.EffectDefinitionId,
                        Actor,
                        RageRules.Source,
                        EffectDuration.OneMinute,
                        receipt
                    )
                )
                .SeedRuleBinding(
                    new ActiveRuleBinding(
                        PendingRageBinding,
                        RageActionDefinition.EffectDefinitionId,
                        Actor,
                        PendingRageEffect,
                        RageRules.Source,
                        PendingRageRoot.Value
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
            private readonly List<RuleFact> committedFacts = new List<RuleFact>();

            internal List<string> Facts { get; } = new List<string>();

            internal int Count<TFact>()
                where TFact : RuleFact => committedFacts.OfType<TFact>().Count();

            internal IEnumerable<TFact> OfType<TFact>()
                where TFact : RuleFact => committedFacts.OfType<TFact>();

            public ValueTask OnFactCommitted(RuleFact fact, RulesSnapshot currentSnapshot)
            {
                committedFacts.Add(fact);
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

        private sealed class ThrowOnceResolvedObserver<TOp, TResult>
            : IResolvedOpObserver<TOp, TResult>
            where TOp : IRuleOp<TResult>
        {
            private readonly Exception failure;
            private bool thrown;

            internal ThrowOnceResolvedObserver(Exception failure) => this.failure = failure;

            public ValueTask OnOperationResolved(
                TOp operation,
                TResult result,
                RulesSnapshot currentSnapshot
            )
            {
                if (!thrown)
                {
                    thrown = true;
                    throw failure;
                }
                return default;
            }
        }

        private sealed class RageCleanupFailureMiddleware
            : IOpMiddleware<EndRageOp, RageEndOutcome>,
                IOpMiddleware<CommitRageEndOp, RageEndOutcome>
        {
            private readonly CreatureId actor;
            private readonly RageCleanupOperation operation;
            private readonly bool postNext;
            private readonly Queue<Exception> failures;

            private RageCleanupFailureMiddleware(
                CreatureId actor,
                RageCleanupOperation operation,
                bool postNext,
                IEnumerable<Exception> failures
            )
            {
                this.actor = actor;
                this.operation = operation;
                this.postNext = postNext;
                this.failures = new Queue<Exception>(failures);
            }

            internal int Attempts { get; private set; }
            internal bool SawExpiredDisabledTombstone { get; private set; }
            internal bool SawRageStartBlocked { get; private set; }

            internal static RageCleanupFailureMiddleware PreNext(
                CreatureId actor,
                RageCleanupOperation operation,
                params Exception[] failures
            ) => new RageCleanupFailureMiddleware(actor, operation, false, failures);

            internal static RageCleanupFailureMiddleware PostNext(
                CreatureId actor,
                RageCleanupOperation operation,
                params Exception[] failures
            ) => new RageCleanupFailureMiddleware(actor, operation, true, failures);

            public async ValueTask<OpResult<RageEndOutcome>> Invoke(
                OpFrame<EndRageOp> frame,
                OpMiddlewareContext context,
                OpNext<RageEndOutcome> next
            )
            {
                if (operation != RageCleanupOperation.EndRage || frame.Op.Actor != actor)
                    return await next();
                return await InvokeSelected(context, next);
            }

            public async ValueTask<OpResult<RageEndOutcome>> Invoke(
                OpFrame<CommitRageEndOp> frame,
                OpMiddlewareContext context,
                OpNext<RageEndOutcome> next
            )
            {
                if (operation != RageCleanupOperation.CommitRageEnd || frame.Op.Actor != actor)
                    return await next();
                return await InvokeSelected(context, next);
            }

            private async ValueTask<OpResult<RageEndOutcome>> InvokeSelected(
                OpMiddlewareContext context,
                OpNext<RageEndOutcome> next
            )
            {
                Attempts++;
                if (!postNext)
                    ThrowNext(context.Snapshot);
                OpResult<RageEndOutcome> result = await next();
                if (postNext)
                    ThrowNext(context.Snapshot);
                return result;
            }

            private void ThrowNext(RulesSnapshot snapshot)
            {
                ActiveEffectInstance tombstone = snapshot
                    .ActiveEffects.Select(pair => pair.Value)
                    .FirstOrDefault(effect =>
                        effect.DefinitionId == RageActionDefinition.EffectDefinitionId
                        && effect.SourceCreature == actor
                        && effect.Source == RageRules.Source
                        && effect.Status == ActiveEffectStatus.Expired
                    );
                SawExpiredDisabledTombstone |=
                    tombstone != null
                    && snapshot.RuleBindings.Any(pair =>
                        pair.Value.EffectId.HasValue
                        && pair.Value.EffectId.Value == tombstone.Id
                        && !pair.Value.IsEnabled
                    );
                SawRageStartBlocked |=
                    RageRules.Validate(snapshot, actor, CreateActorState(), false)
                    is ActionValidationResult.InvalidActionValidationResult;
                if (failures.Count > 0)
                    throw failures.Dequeue();
            }
        }

        /// <summary>
        /// Exercises the same external-root recovery contract as the synchronous Unity bridge
        /// while keeping failure middleware inside the no-engine rules test assembly.
        /// </summary>
        private sealed class ExactEndTurnBridgeHarness
        {
            private readonly RuleDispatcher dispatcher;
            private readonly EncounterId encounter;

            internal ExactEndTurnBridgeHarness(RuleDispatcher dispatcher, EncounterId encounter)
            {
                this.dispatcher = dispatcher;
                this.encounter = encounter;
            }

            internal EncounterState Current => dispatcher.Snapshot.Encounters[encounter];

            internal async ValueTask EndTurn(CreatureId actor)
            {
                EncounterState current = Current;
                if (
                    current.Phase != EncounterPhase.Active
                    || !current.CurrentTurn.HasValue
                    || current.CurrentTurn.Value.Actor != actor
                )
                    throw new InvalidOperationException(
                        "The creature does not own the active turn."
                    );
                TurnIdentity ending = current.CurrentTurn.Value;
                try
                {
                    RequireResolved(await dispatcher.Dispatch(new EndTurnOp(ending)));
                }
                catch (Exception failure)
                {
                    EncounterState latest = Current;
                    if (latest.Phase != EncounterPhase.Active || latest.CurrentTurn.HasValue)
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    ObserverFailureState failures = ObserverFailureState
                        .CreateEmpty("Turn end and authoritative encounter recovery both failed.")
                        .Add(failure);
                    try
                    {
                        RequireResolved(
                            await dispatcher.Dispatch(new AdvanceEncounterOp(ending.Encounter))
                        );
                    }
                    catch (Exception recoveryFailure)
                    {
                        failures = failures.Add(recoveryFailure);
                    }
                    failures.ThrowIfAny();
                }
            }
        }

        private sealed class RageStateTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly List<CreatureId> observedActors = new List<CreatureId>();
            private readonly List<bool> observedRaging = new List<bool>();
            private readonly List<bool> observedRageState = new List<bool>();

            internal IReadOnlyList<CreatureId> ObservedActors => observedActors;
            internal IReadOnlyList<bool> ObservedRaging => observedRaging;
            internal IReadOnlyList<bool> ObservedRageState => observedRageState;

            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                observedActors.Add(context.Actor);
                observedRaging.Add(RageRules.IsRaging(context.Snapshot, context.Actor));
                observedRageState.Add(HasOwnedRageState(context.Snapshot, context.Actor));
                return new ValueTask<TurnStartContribution>(current);
            }
        }

        private sealed class QuickWorkflowProbe
            : IOpMiddleware<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>,
                IOpMiddleware<SettleRageStartOp, RageStartOutcome>,
                IOpMiddleware<QuickTemperedRageActionOp, RageStartOutcome>,
                IOpMiddleware<ConsumeQuickTemperedTriggerOp, QuickTemperedTriggerConsumedOutcome>,
                IResolvedOpObserver<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>,
                IResolvedOpObserver<SettleRageStartOp, RageStartOutcome>,
                IResolvedOpObserver<QuickTemperedRageActionOp, RageStartOutcome>,
                IResolvedOpObserver<
                    ConsumeQuickTemperedTriggerOp,
                    QuickTemperedTriggerConsumedOutcome
                >
        {
            private readonly CreatureId actor;
            private readonly JoinRetryFailurePoint? failurePoint;
            private readonly Exception failure;
            private bool thrown;

            internal QuickWorkflowProbe(
                CreatureId actor,
                JoinRetryFailurePoint? failurePoint,
                Exception failure
            )
            {
                this.actor = actor;
                this.failurePoint = failurePoint;
                this.failure = failure;
            }

            internal int GrantMiddlewareCount { get; private set; }
            internal int GrantObserverCount { get; private set; }
            internal int SettlementMiddlewareCount { get; private set; }
            internal int SettlementObserverCount { get; private set; }
            internal int QuickActionMiddlewareCount { get; private set; }
            internal int QuickActionObserverCount { get; private set; }
            internal int TriggerMiddlewareCount { get; private set; }
            internal int TriggerObserverCount { get; private set; }

            public async ValueTask<OpResult<TemporaryHitPointsGrantOutcome>> Invoke(
                OpFrame<GrantTemporaryHitPointsOp> frame,
                OpMiddlewareContext context,
                OpNext<TemporaryHitPointsGrantOutcome> next
            )
            {
                if (frame.Op.Target != actor)
                    return await next();
                GrantMiddlewareCount++;
                OpResult<TemporaryHitPointsGrantOutcome> result = await next();
                ThrowIfSelected(JoinRetryFailurePoint.GrantPostNextMiddleware);
                return result;
            }

            public async ValueTask<OpResult<RageStartOutcome>> Invoke(
                OpFrame<SettleRageStartOp> frame,
                OpMiddlewareContext context,
                OpNext<RageStartOutcome> next
            )
            {
                SettlementMiddlewareCount++;
                OpResult<RageStartOutcome> result = await next();
                ThrowIfSelected(JoinRetryFailurePoint.SettlementPostNextMiddleware);
                return result;
            }

            public async ValueTask<OpResult<RageStartOutcome>> Invoke(
                OpFrame<QuickTemperedRageActionOp> frame,
                OpMiddlewareContext context,
                OpNext<RageStartOutcome> next
            )
            {
                if (frame.Op.Actor != actor)
                    return await next();
                QuickActionMiddlewareCount++;
                OpResult<RageStartOutcome> result = await next();
                ThrowIfSelected(JoinRetryFailurePoint.QuickActionPostNextMiddleware);
                return result;
            }

            public async ValueTask<OpResult<QuickTemperedTriggerConsumedOutcome>> Invoke(
                OpFrame<ConsumeQuickTemperedTriggerOp> frame,
                OpMiddlewareContext context,
                OpNext<QuickTemperedTriggerConsumedOutcome> next
            )
            {
                if (frame.Op.Actor != actor)
                    return await next();
                TriggerMiddlewareCount++;
                OpResult<QuickTemperedTriggerConsumedOutcome> result = await next();
                ThrowIfSelected(JoinRetryFailurePoint.TriggerPostNextMiddleware);
                return result;
            }

            public ValueTask OnOperationResolved(
                GrantTemporaryHitPointsOp operation,
                TemporaryHitPointsGrantOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                if (operation.Target == actor)
                {
                    GrantObserverCount++;
                    ThrowIfSelected(JoinRetryFailurePoint.GrantResolvedObserver);
                }
                return default;
            }

            public ValueTask OnOperationResolved(
                SettleRageStartOp operation,
                RageStartOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                SettlementObserverCount++;
                ThrowIfSelected(JoinRetryFailurePoint.SettlementResolvedObserver);
                return default;
            }

            public ValueTask OnOperationResolved(
                QuickTemperedRageActionOp operation,
                RageStartOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                if (operation.Actor == actor)
                {
                    QuickActionObserverCount++;
                    ThrowIfSelected(JoinRetryFailurePoint.QuickActionResolvedObserver);
                }
                return default;
            }

            public ValueTask OnOperationResolved(
                ConsumeQuickTemperedTriggerOp operation,
                QuickTemperedTriggerConsumedOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                if (operation.Actor == actor)
                {
                    TriggerObserverCount++;
                    ThrowIfSelected(JoinRetryFailurePoint.TriggerResolvedObserver);
                }
                return default;
            }

            private void ThrowIfSelected(JoinRetryFailurePoint point)
            {
                if (!thrown && failurePoint == point)
                {
                    thrown = true;
                    throw failure;
                }
            }
        }

        private sealed class WorkflowFailureMiddleware
            : IOpMiddleware<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>,
                IOpMiddleware<SettleRageStartOp, RageStartOutcome>,
                IOpMiddleware<AbortPendingRageStartOp, ActiveEffectRemovalOutcome>
        {
            private readonly CreatureId actor;
            private readonly WorkflowFailureKind failureKind;
            private readonly Exception failure;
            private bool committingInterveningPool;

            internal WorkflowFailureMiddleware(
                CreatureId actor,
                WorkflowFailureKind failureKind,
                Exception failure
            )
            {
                this.actor = actor;
                this.failureKind = failureKind;
                this.failure = failure;
            }

            internal const string InvalidReason = "Injected invalid Rage workflow result.";

            internal int GrantAttempts { get; private set; }
            internal int SettlementAttempts { get; private set; }
            internal int CleanupAttempts { get; private set; }
            internal bool SawCommittedEffectBeforeFailure { get; private set; }
            internal bool SawEnabledTriggerBeforeFailure { get; private set; }

            public async ValueTask<OpResult<TemporaryHitPointsGrantOutcome>> Invoke(
                OpFrame<GrantTemporaryHitPointsOp> frame,
                OpMiddlewareContext context,
                OpNext<TemporaryHitPointsGrantOutcome> next
            )
            {
                if (frame.Op.Target != actor)
                    return await next();
                if (committingInterveningPool)
                    return await next();
                GrantAttempts++;
                CapturePreFailureState(context.Snapshot);
                if (
                    failureKind == WorkflowFailureKind.GrantInvalid
                    || failureKind == WorkflowFailureKind.GrantInvalidWithCleanupThrow
                )
                    return OpResult<TemporaryHitPointsGrantOutcome>.Invalid(InvalidReason);
                if (failureKind == WorkflowFailureKind.GrantInterrupted)
                    return OpResult<TemporaryHitPointsGrantOutcome>.Interrupted();
                if (failureKind == WorkflowFailureKind.GrantPreNextThrow)
                    throw failure;
                OpResult<TemporaryHitPointsGrantOutcome> result = await next();
                if (failureKind == WorkflowFailureKind.GrantPostNextInvalid)
                    return OpResult<TemporaryHitPointsGrantOutcome>.Invalid(InvalidReason);
                if (failureKind == WorkflowFailureKind.GrantPostNextInterrupted)
                    return OpResult<TemporaryHitPointsGrantOutcome>.Interrupted();
                if (
                    failureKind == WorkflowFailureKind.GrantPostNextInvalidWithInterveningPool
                    && !committingInterveningPool
                )
                {
                    committingInterveningPool = true;
                    try
                    {
                        RequireResolved(
                            await context.Dispatch(
                                new GrantTemporaryHitPointsOp(
                                    actor,
                                    9,
                                    new HealthChangeOriginId("intervening-foreign-pool"),
                                    OtherTemporaryHitPointSource
                                )
                            )
                        );
                    }
                    finally
                    {
                        committingInterveningPool = false;
                    }
                    return OpResult<TemporaryHitPointsGrantOutcome>.Invalid(InvalidReason);
                }
                if (
                    failureKind == WorkflowFailureKind.GrantPostNextInvalidWithChangedRagePool
                    && !committingInterveningPool
                )
                {
                    committingInterveningPool = true;
                    try
                    {
                        RequireResolved(
                            await context.Dispatch(
                                new GrantTemporaryHitPointsOp(
                                    actor,
                                    9,
                                    new HealthChangeOriginId("intervening-changed-rage-pool"),
                                    RageRules.Source
                                )
                            )
                        );
                    }
                    finally
                    {
                        committingInterveningPool = false;
                    }
                    return OpResult<TemporaryHitPointsGrantOutcome>.Invalid(InvalidReason);
                }
                if (
                    failureKind == WorkflowFailureKind.GrantPostNextInvalidWithAbaPool
                    && !committingInterveningPool
                )
                {
                    committingInterveningPool = true;
                    try
                    {
                        RequireResolved(
                            await context.Dispatch(
                                new RemoveTemporaryHitPointsOp(
                                    actor,
                                    new HealthChangeOriginId("intervening-rage-pool-removal"),
                                    RageRules.Source
                                )
                            )
                        );
                        RequireResolved(
                            await context.Dispatch(
                                new GrantTemporaryHitPointsOp(
                                    actor,
                                    frame.Op.Amount,
                                    new HealthChangeOriginId("intervening-rage-pool-regrant"),
                                    RageRules.Source
                                )
                            )
                        );
                    }
                    finally
                    {
                        committingInterveningPool = false;
                    }
                    return OpResult<TemporaryHitPointsGrantOutcome>.Invalid(InvalidReason);
                }
                if (
                    failureKind == WorkflowFailureKind.GrantPostNextInvalidWithConsumedPool
                    && !committingInterveningPool
                )
                {
                    committingInterveningPool = true;
                    try
                    {
                        RequireResolved(
                            await context.Dispatch(
                                new ApplyDamageOp(
                                    actor,
                                    frame.Op.Amount,
                                    new HealthChangeOriginId("intervening-rage-pool-consumption"),
                                    OtherTemporaryHitPointSource
                                )
                            )
                        );
                    }
                    finally
                    {
                        committingInterveningPool = false;
                    }
                    return OpResult<TemporaryHitPointsGrantOutcome>.Invalid(InvalidReason);
                }
                if (
                    failureKind == WorkflowFailureKind.GrantPostNextInvalidWithHealing
                    && !committingInterveningPool
                )
                {
                    committingInterveningPool = true;
                    try
                    {
                        RequireResolved(
                            await context.Dispatch(
                                new ApplyHealingOp(
                                    actor,
                                    1,
                                    new HealthChangeOriginId("intervening-unrelated-healing"),
                                    OtherTemporaryHitPointSource
                                )
                            )
                        );
                        RequireResolved(
                            await context.Dispatch(
                                new AddTemporaryHitPointImmunityOp(
                                    actor,
                                    new HealthChangeOriginId("intervening-unrelated-immunity"),
                                    UnrelatedImmunitySource
                                )
                            )
                        );
                    }
                    finally
                    {
                        committingInterveningPool = false;
                    }
                    return OpResult<TemporaryHitPointsGrantOutcome>.Invalid(InvalidReason);
                }
                return result;
            }

            public ValueTask<OpResult<RageStartOutcome>> Invoke(
                OpFrame<SettleRageStartOp> frame,
                OpMiddlewareContext context,
                OpNext<RageStartOutcome> next
            )
            {
                SettlementAttempts++;
                CapturePreFailureState(context.Snapshot);
                if (failureKind == WorkflowFailureKind.SettlementInvalid)
                    return new ValueTask<OpResult<RageStartOutcome>>(
                        OpResult<RageStartOutcome>.Invalid(InvalidReason)
                    );
                if (failureKind == WorkflowFailureKind.SettlementPreNextThrow)
                    throw failure;
                return next();
            }

            public ValueTask<OpResult<ActiveEffectRemovalOutcome>> Invoke(
                OpFrame<AbortPendingRageStartOp> frame,
                OpMiddlewareContext context,
                OpNext<ActiveEffectRemovalOutcome> next
            )
            {
                if (frame.Op.Receipt.Actor != actor)
                    return next();
                CleanupAttempts++;
                if (failureKind == WorkflowFailureKind.GrantInvalidWithCleanupThrow)
                    throw failure;
                return next();
            }

            private void CapturePreFailureState(RulesSnapshot snapshot)
            {
                SawCommittedEffectBeforeFailure = snapshot.ActiveEffects.Any(pair =>
                    pair.Value.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && pair.Value.SourceCreature == actor
                    && pair.Value.Status == ActiveEffectStatus.Active
                    && pair.Value.State is RageEffectState receipt
                    && receipt.Phase == RageStartPhase.Pending
                );
                BindingId triggerId = new BindingId($"quick-tempered-{actor.Value}");
                SawEnabledTriggerBeforeFailure =
                    snapshot.RuleBindings.TryGet(triggerId, out ActiveRuleBinding trigger)
                    && trigger.IsEnabled;
            }
        }

        private sealed class GrantPhaseProbe
            : IOpMiddleware<GrantTemporaryHitPointsOp, TemporaryHitPointsGrantOutcome>
        {
            internal GrantPhaseProbe(RuleLifecyclePhase phase)
            {
                Phase = phase;
                DefinitionId = new RuleDefinitionId(
                    $"test-rage-phase-{phase.ToString().ToLowerInvariant()}"
                );
            }

            internal RuleLifecyclePhase Phase { get; }
            internal RuleDefinitionId DefinitionId { get; }
            internal int Count { get; private set; }

            public async ValueTask<OpResult<TemporaryHitPointsGrantOutcome>> Invoke(
                OpFrame<GrantTemporaryHitPointsOp> frame,
                OpMiddlewareContext context,
                OpNext<TemporaryHitPointsGrantOutcome> next
            )
            {
                if (frame.Op.Target == Reinforcement)
                    Count++;
                return await next();
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

        private sealed class DictionaryRageActorStateProvider
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

        private interface ITestRageStateProvider
        {
            RageActorState State { get; }
        }

        private sealed class TestRageActorStateProvider : ITestRageStateProvider
        {
            private readonly RageActorState state;

            public TestRageActorStateProvider(RageActorState state) =>
                this.state = state ?? throw new ArgumentNullException(nameof(state));

            public RageActorState State => state;
        }

        private sealed class TestRageConditionStateProvider : ITestRageStateProvider
        {
            private readonly RageActorState state;

            public TestRageConditionStateProvider(RageActorState state) =>
                this.state = state ?? throw new ArgumentNullException(nameof(state));

            public RageActorState State => state;
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
