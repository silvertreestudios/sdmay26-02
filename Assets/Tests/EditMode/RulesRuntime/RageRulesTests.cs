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
        public async Task InitiativeAssignmentOwnsQuickTemperedRequirementsAndOneShot()
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
        public async Task QuickTemperedRageKeepsPreFirstTurnTimingAndExpiresNormally()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState(ownsQuickTempered: true)),
                new ScriptedRollService(20, 1),
                actorInitiativeModifier: 0,
                enemyInitiativeModifier: 0,
                advanceEncounter: false
            );

            EncounterState initialized = dispatcher.Snapshot.Encounters[Encounter];
            ActiveEffectInstance rage = dispatcher
                .Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Single(effect =>
                    effect.DefinitionId == RageActionDefinition.EffectDefinitionId
                    && effect.SourceCreature == Actor
                );
            ActiveEffectTimingState timing = dispatcher.Snapshot.ActiveEffectTimings[rage.Id];
            Assert.That(initialized.Phase, Is.EqualTo(EncounterPhase.Initialized));
            Assert.That(initialized.CurrentTurn, Is.Null);
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.True);
            Assert.That(timing.RemainingBoundaries, Is.EqualTo(10));

            RequireResolved(await dispatcher.Dispatch(new AdvanceEncounterOp(Encounter)));
            int turnsEnded = 0;
            while (RageRules.IsRaging(dispatcher.Snapshot, Actor) && turnsEnded < 20)
            {
                EncounterState active = dispatcher.Snapshot.Encounters[Encounter];
                RequireResolved(await dispatcher.Dispatch(new EndTurnOp(active.CurrentTurn.Value)));
                turnsEnded++;
            }

            Assert.That(turnsEnded, Is.LessThan(20));
            Assert.That(RageRules.IsRaging(dispatcher.Snapshot, Actor), Is.False);
            Assert.That(dispatcher.Snapshot.ActiveEffectTimings.Contains(rage.Id), Is.False);
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
        public void QuickTemperedSettlesBeforeWinningActorsFirstTurnStartAdapter()
        {
            RageTurnStartProbe adapter = new RageTurnStartProbe(Actor);

            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState(ownsQuickTempered: true)),
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
                new TestRageActorStateProvider(CreateActorState()),
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
                new TestRageActorStateProvider(CreateActorState())
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
                new TestRageActorStateProvider(CreateActorState())
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
        public async Task EncounterSuspensionExpiresRageBeforeTheEncounterClockIsReleased()
        {
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestRageActorStateProvider(CreateActorState())
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
                new TestRageActorStateProvider(CreateActorState())
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
            ActiveEffectRemovedFact removal = ended
                .Facts.OfType<ActiveEffectRemovedFact>()
                .Single();
            Assert.That(removal.Reason, Is.EqualTo(ActiveEffectRemovalReason.Ended));
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
            int enemyInitiativeModifier,
            IEnumerable<IEncounterTurnStartAdapter> turnStartAdapters = null,
            bool advanceEncounter = true
        )
        {
            RageActionDefinition definition = new RageActionDefinition(provider);
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            ActiveRuleBinding[] actorBindings = RageRules
                .CreateInitialBindings(Actor, provider.Get(Actor))
                .ToArray();

            registryBuilder.AddOutcomeRule();
            RuleRegistry registry = registryBuilder.Build();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(new RulesStateSeed()),
                rolls
            )
                .UseHealthRules()
                .UseMultipleAttackPenaltyRules()
                .UseActiveEffectRules(registry)
                .UseMovementBudgetResetRules()
                .UseEncounterRules(
                    registry,
                    turnStartAdapters ?? Array.Empty<IEncounterTurnStartAdapter>()
                )
                .UseActionLifecycle(definition)
                .UseRageRules(definition)
                .Build();
            RequireResolved(
                dispatcher
                    .Dispatch(new InitEncounterOp(Encounter, Party))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
            );
            RequireResolved(
                dispatcher
                    .Dispatch(
                        new AddCombatantsOp(
                            Encounter,
                            new[]
                            {
                                Registration(Actor, Party, actorInitiativeModifier, actorBindings),
                                Registration(
                                    Enemy,
                                    Opposition,
                                    enemyInitiativeModifier,
                                    Array.Empty<ActiveRuleBinding>()
                                ),
                            }
                        )
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult()
            );
            if (advanceEncounter)
                RequireResolved(
                    dispatcher
                        .Dispatch(new AdvanceEncounterOp(Encounter))
                        .AsTask()
                        .GetAwaiter()
                        .GetResult()
                );
            return dispatcher;
        }

        private static CombatantRulesState Registration(
            CreatureId creature,
            PlayerId team,
            int initiativeModifier,
            IReadOnlyList<ActiveRuleBinding> bindings
        ) =>
            new CombatantRulesState(
                new CreatureState(creature, team),
                new HealthState(10, 10),
                new GridPosition(0, 0, 0),
                new GridDistance(25),
                initiativeModifier,
                Array.Empty<SpellSlotState>(),
                bindings,
                Array.Empty<EquipmentState>(),
                Array.Empty<AmmunitionState>(),
                Array.Empty<ActiveEffectInstance>()
            );

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
