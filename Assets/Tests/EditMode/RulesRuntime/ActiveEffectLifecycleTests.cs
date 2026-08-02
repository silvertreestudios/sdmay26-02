using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ActiveEffectLifecycleTests
    {
        private static readonly ActiveEffectId EffectId = new ActiveEffectId("effect-1");
        private static readonly BindingId BindingId = new BindingId("binding-1");
        private static readonly RuleDefinitionId DefinitionId = new RuleDefinitionId("test-aura");
        private static readonly CreatureId SourceCreature = new CreatureId("source-creature");
        private static readonly CreatureId Owner = new CreatureId("owner-creature");
        private static readonly RuleSource Source = RuleSource.FromSlug("test-aura-source");

        [Test]
        public void CreateAtomicallyCommitsTypedEffectBindingAndFact()
        {
            RuleRegistry registry = CreateRegistry();
            InMemoryRulesStore store = new InMemoryRulesStore(CreateActiveEncounterSeed());
            RulesSnapshot before = store.Snapshot;
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(2));
            ActiveRuleBinding binding = CreateBinding(effect);

            ReductionResult<ActiveEffectCreationOutcome> result = store.Reduce(
                Context(new CreateActiveEffectOp(effect, binding)),
                new CreateActiveEffectReducer(registry)
            );

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.DidCommit, Is.True);
            Assert.That(before.ActiveEffects, Is.Empty);
            Assert.That(before.RuleBindings, Is.Empty);
            Assert.That(result.Snapshot.ActiveEffects[EffectId], Is.EqualTo(effect));
            Assert.That(
                result.Snapshot.ActiveEffects[EffectId].GetState<AuraEffectState>().Bonus,
                Is.EqualTo(2)
            );
            Assert.That(result.Snapshot.RuleBindings[BindingId], Is.EqualTo(binding));
            Assert.That(result.Facts, Has.Count.EqualTo(1));
            ActiveEffectCreatedFact fact = (ActiveEffectCreatedFact)result.Facts[0];
            Assert.That(fact.EffectId, Is.EqualTo(EffectId));
            Assert.That(fact.BindingId, Is.EqualTo(BindingId));
            Assert.That(fact.Version, Is.EqualTo(EffectStateVersion.Initial));
            Assert.That(fact.Duration, Is.EqualTo(EffectDuration.OneMinute));
        }

        [Test]
        public void CreateEstablishesExactStateTypeOnTheInstance()
        {
            RuleRegistry registry = CreateRegistry();
            InMemoryRulesStore store = new InMemoryRulesStore(CreateActiveEncounterSeed());
            ActiveEffectInstance effect = CreateEffect(new OtherEffectState());

            ReductionResult<ActiveEffectCreationOutcome> result = store.Reduce(
                Context(new CreateActiveEffectOp(effect, CreateBinding(effect))),
                new CreateActiveEffectReducer(registry)
            );

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.DidCommit, Is.True);
            Assert.That(
                result.Snapshot.ActiveEffects[EffectId].GetState<OtherEffectState>(),
                Is.SameAs(effect.State)
            );
        }

        [Test]
        public void AdoptionUsesExactStatusFactAndIsIdempotentWithoutCreation()
        {
            ActiveEffectInstance active = CreateEffect(new AuraEffectState(3));
            ActiveRuleBinding activeBinding = CreateBinding(active);
            ActiveEffectInstance expired = new ActiveEffectInstance(
                new ActiveEffectId("effect-expired"),
                DefinitionId,
                SourceCreature,
                Source,
                EffectDuration.Encounter,
                new AuraEffectState(5),
                new EffectStateVersion(6),
                ActiveEffectStatus.Expired
            );
            ActiveRuleBinding expiredBinding = new ActiveRuleBinding(
                new BindingId("binding-expired"),
                DefinitionId,
                Owner,
                expired.Id,
                Source,
                2,
                isEnabled: false
            );
            AdoptActiveEffectRegistrationsOp operation = new AdoptActiveEffectRegistrationsOp(
                new[]
                {
                    new ActiveEffectRegistration(active, activeBinding),
                    new ActiveEffectRegistration(expired, expiredBinding),
                },
                Source
            );
            InMemoryRulesStore store = new InMemoryRulesStore(CreateActiveEncounterSeed());
            AdoptActiveEffectRegistrationsReducer reducer =
                new AdoptActiveEffectRegistrationsReducer(CreateRegistry());

            ReductionResult<ActiveEffectAdoptionOutcome> first = store.Reduce(
                Context(operation),
                reducer
            );
            long committedVersion = store.Snapshot.Version;
            ReductionResult<ActiveEffectAdoptionOutcome> retry = store.Reduce(
                Context(operation),
                reducer
            );

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(first.Value.Adopted, Is.EqualTo(2));
            Assert.That(first.Facts.OfType<ActiveEffectCreatedFact>(), Is.Empty);
            ActiveEffectAdoptedFact[] facts = first.Facts.Cast<ActiveEffectAdoptedFact>().ToArray();
            Assert.That(facts.Select(fact => fact.Effect), Is.EqualTo(new[] { active, expired }));
            Assert.That(
                facts.Select(fact => fact.Binding),
                Is.EqualTo(new[] { activeBinding, expiredBinding })
            );
            Assert.That(retry.IsAccepted, Is.True);
            Assert.That(retry.Value.Adopted, Is.Zero);
            Assert.That(retry.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(committedVersion));
        }

        [TestCase("root")]
        [TestCase("grant-pool")]
        public void AdoptionUsesExactRageReceiptIdentity(string changedField)
        {
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            registryBuilder.Define(RageActionDefinition.EffectDefinitionId);
            InMemoryRulesStore store = new InMemoryRulesStore(CreateActiveEncounterSeed());
            AdoptActiveEffectRegistrationsReducer reducer =
                new AdoptActiveEffectRegistrationsReducer(registryBuilder.Build());
            RageEffectState originalReceipt = CreateRageReceipt(new OpId(31), 3);
            ActiveEffectInstance originalEffect = CreateRageEffect(originalReceipt);
            ActiveRuleBinding originalBinding = CreateRageBinding(originalEffect);
            ReductionResult<ActiveEffectAdoptionOutcome> first = store.Reduce(
                Context(
                    new AdoptActiveEffectRegistrationsOp(
                        new[] { new ActiveEffectRegistration(originalEffect, originalBinding) },
                        RageRules.Source
                    )
                ),
                reducer
            );
            long committedVersion = store.Snapshot.Version;
            RageEffectState recreatedReceipt = CreateRageReceipt(new OpId(31), 3);
            ActiveEffectInstance recreatedEffect = CreateRageEffect(recreatedReceipt);
            ActiveRuleBinding recreatedBinding = CreateRageBinding(recreatedEffect);
            ReductionResult<ActiveEffectAdoptionOutcome> exactReplay = store.Reduce(
                Context(
                    new AdoptActiveEffectRegistrationsOp(
                        new[] { new ActiveEffectRegistration(recreatedEffect, recreatedBinding) },
                        RageRules.Source
                    )
                ),
                reducer
            );
            RageEffectState changedReceipt =
                changedField == "root"
                    ? CreateRageReceipt(new OpId(32), 3)
                    : CreateRageReceipt(new OpId(31), 4);
            ActiveEffectInstance changedEffect = CreateRageEffect(changedReceipt);
            ReductionResult<ActiveEffectAdoptionOutcome> changedReplay = store.Reduce(
                Context(
                    new AdoptActiveEffectRegistrationsOp(
                        new[]
                        {
                            new ActiveEffectRegistration(
                                changedEffect,
                                CreateRageBinding(changedEffect)
                            ),
                        },
                        RageRules.Source
                    )
                ),
                reducer
            );

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(exactReplay.IsAccepted, Is.True);
            Assert.That(exactReplay.Value.Adopted, Is.Zero);
            Assert.That(exactReplay.Facts, Is.Empty);
            Assert.That(changedEffect, Is.EqualTo(originalEffect));
            Assert.That(changedReplay.IsRejected, Is.True);
            Assert.That(changedReplay.DidCommit, Is.False);
            Assert.That(changedReplay.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(committedVersion));
        }

        [Test]
        public void CreateRejectsUnknownDefinitionWithoutPartialBindingWrite()
        {
            RuleDefinitionId unknownDefinition = new RuleDefinitionId("unknown-effect");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                EffectId,
                unknownDefinition,
                SourceCreature,
                Source,
                EffectDuration.OneMinute,
                new AuraEffectState(1)
            );
            InMemoryRulesStore store = new InMemoryRulesStore();

            ReductionResult<ActiveEffectCreationOutcome> result = store.Reduce(
                Context(new CreateActiveEffectOp(effect, CreateBinding(effect))),
                new CreateActiveEffectReducer(CreateRegistry())
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.RejectionReason, Does.Contain("unknown"));
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(result.Snapshot.RuleBindings, Is.Empty);
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public void RemovalFactRejectsUndefinedLifecycleStatus()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ActiveEffectRemovedFact(
                    EffectId,
                    DefinitionId,
                    BindingId,
                    EffectStateVersion.Initial,
                    (ActiveEffectStatus)99
                )
            );
        }

        [Test]
        public void UpdateUsesExactStateTypeAndOptimisticVersionWithoutMutatingOldSnapshot()
        {
            ActiveEffectInstance original = CreateEffect(new AuraEffectState(1));
            InMemoryRulesStore store = CreateSeededStore(original);
            RulesSnapshot oldSnapshot = store.Snapshot;

            ReductionResult<ActiveEffectStateUpdateOutcome> updated = store.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        EffectId,
                        EffectStateVersion.Initial,
                        new AuraEffectState(3),
                        Source
                    )
                ),
                new UpdateActiveEffectStateReducer()
            );

            Assert.That(updated.IsAccepted, Is.True);
            Assert.That(updated.Value.PreviousVersion, Is.EqualTo(EffectStateVersion.Initial));
            Assert.That(updated.Value.CurrentVersion, Is.EqualTo(new EffectStateVersion(1)));
            Assert.That(
                updated.Snapshot.ActiveEffects[EffectId].GetState<AuraEffectState>().Bonus,
                Is.EqualTo(3)
            );
            Assert.That(oldSnapshot.ActiveEffects[EffectId], Is.SameAs(original));
            Assert.That(
                oldSnapshot.ActiveEffects[EffectId].GetState<AuraEffectState>().Bonus,
                Is.EqualTo(1)
            );
            Assert.That(updated.Facts.Single(), Is.TypeOf<ActiveEffectStateUpdatedFact>());

            ReductionResult<ActiveEffectStateUpdateOutcome> stale = store.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        EffectId,
                        EffectStateVersion.Initial,
                        new AuraEffectState(4),
                        Source
                    )
                ),
                new UpdateActiveEffectStateReducer()
            );

            Assert.That(stale.IsRejected, Is.True);
            Assert.That(stale.RejectionReason, Does.Contain("expected version 0"));
            Assert.That(stale.RejectionReason, Does.Contain("current version is 1"));
            Assert.That(stale.DidCommit, Is.False);
            Assert.That(
                stale.Snapshot.ActiveEffects[EffectId].GetState<AuraEffectState>().Bonus,
                Is.EqualTo(3)
            );
        }

        [Test]
        public void UpdateRejectsUnknownWrongTypeAndExpiredEffectsAsTypedResults()
        {
            ActiveEffectInstance active = CreateEffect(new AuraEffectState(1));
            InMemoryRulesStore store = CreateSeededStore(active);
            UpdateActiveEffectStateReducer reducer = new UpdateActiveEffectStateReducer();

            ReductionResult<ActiveEffectStateUpdateOutcome> unknown = store.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        new ActiveEffectId("missing"),
                        EffectStateVersion.Initial,
                        new AuraEffectState(2),
                        Source
                    )
                ),
                reducer
            );
            ReductionResult<ActiveEffectStateUpdateOutcome> wrongType = store.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        EffectId,
                        EffectStateVersion.Initial,
                        new OtherEffectState(),
                        Source
                    )
                ),
                reducer
            );
            ActiveEffectInstance expired = new ActiveEffectInstance(
                EffectId,
                DefinitionId,
                SourceCreature,
                Source,
                EffectDuration.OneMinute,
                new AuraEffectState(1),
                new EffectStateVersion(4),
                ActiveEffectStatus.Expired
            );
            InMemoryRulesStore expiredStore = CreateSeededStore(
                expired,
                CreateBinding(expired).WithEnabled(false)
            );
            ReductionResult<ActiveEffectStateUpdateOutcome> expiredResult = expiredStore.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        EffectId,
                        new EffectStateVersion(4),
                        new AuraEffectState(2),
                        Source
                    )
                ),
                reducer
            );

            Assert.That(unknown.IsRejected, Is.True);
            Assert.That(unknown.RejectionReason, Does.Contain("unknown"));
            Assert.That(wrongType.IsRejected, Is.True);
            Assert.That(wrongType.RejectionReason, Does.Contain(nameof(OtherEffectState)));
            Assert.That(expiredResult.IsRejected, Is.True);
            Assert.That(expiredResult.RejectionReason, Does.Contain("expired"));
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(expiredStore.Snapshot.Version, Is.Zero);
        }

        [Test]
        public void UpdateRejectsARequestFromTheWrongSourceWithoutMutationOrFacts()
        {
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            InMemoryRulesStore store = CreateSeededStore(effect);
            RulesSnapshot original = store.Snapshot;

            ReductionResult<ActiveEffectStateUpdateOutcome> result = store.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        EffectId,
                        EffectStateVersion.Initial,
                        new AuraEffectState(2),
                        RuleSource.FromSlug("wrong-update-source")
                    )
                ),
                new UpdateActiveEffectStateReducer()
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.RejectionReason, Does.Contain("does not own active effect"));
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot, Is.SameAs(original));
        }

        [Test]
        public void ExpirationRejectsARequestFromTheWrongSourceWithoutMutationOrFacts()
        {
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            InMemoryRulesStore store = CreateSeededStore(effect);
            RulesSnapshot original = store.Snapshot;

            ReductionResult<ActiveEffectExpirationOutcome> result = store.Reduce(
                Context(
                    new ExpireActiveEffectOp(
                        EffectId,
                        BindingId,
                        EffectStateVersion.Initial,
                        RuleSource.FromSlug("wrong-expiration-source")
                    )
                ),
                new ExpireActiveEffectReducer()
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.RejectionReason, Does.Contain("does not own active effect"));
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot, Is.SameAs(original));
        }

        [Test]
        public void RemovalRejectsARequestFromTheWrongSourceWithoutMutationOrFacts()
        {
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            InMemoryRulesStore store = CreateSeededStore(effect);
            RulesSnapshot original = store.Snapshot;

            ReductionResult<ActiveEffectRemovalOutcome> result = store.Reduce(
                Context(
                    new RemoveActiveEffectOp(
                        EffectId,
                        BindingId,
                        EffectStateVersion.Initial,
                        RuleSource.FromSlug("wrong-removal-source")
                    )
                ),
                new RemoveActiveEffectReducer()
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.RejectionReason, Does.Contain("does not own active effect"));
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot, Is.SameAs(original));
        }

        [Test]
        public void ExpireDisablesBindingAndRemoveDeletesBothInAtomicTransactions()
        {
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            ActiveRuleBinding binding = CreateBinding(effect);
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedActiveEffect(effect)
                    .SeedRuleBinding(binding)
                    .SeedFrequency(
                        BindingId,
                        new FrequencyState(new EncounterId("frequency-encounter"), 3, 1)
                    )
            );

            ReductionResult<ActiveEffectExpirationOutcome> expired = store.Reduce(
                Context(
                    new ExpireActiveEffectOp(
                        EffectId,
                        BindingId,
                        EffectStateVersion.Initial,
                        Source
                    )
                ),
                new ExpireActiveEffectReducer()
            );

            Assert.That(expired.IsAccepted, Is.True);
            Assert.That(expired.Value.Version, Is.EqualTo(new EffectStateVersion(1)));
            Assert.That(
                expired.Snapshot.ActiveEffects[EffectId].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(expired.Snapshot.RuleBindings[BindingId].IsEnabled, Is.False);
            Assert.That(expired.Snapshot.Frequencies.Contains(BindingId), Is.True);
            Assert.That(expired.Facts.Single(), Is.TypeOf<ActiveEffectExpiredFact>());

            ReductionResult<ActiveEffectExpirationOutcome> repeatedExpiration = store.Reduce(
                Context(
                    new ExpireActiveEffectOp(EffectId, BindingId, new EffectStateVersion(1), Source)
                ),
                new ExpireActiveEffectReducer()
            );
            Assert.That(repeatedExpiration.IsRejected, Is.True);
            Assert.That(repeatedExpiration.RejectionReason, Does.Contain("expired"));

            ReductionResult<ActiveEffectRemovalOutcome> removed = store.Reduce(
                Context(
                    new RemoveActiveEffectOp(EffectId, BindingId, new EffectStateVersion(1), Source)
                ),
                new RemoveActiveEffectReducer()
            );

            Assert.That(removed.IsAccepted, Is.True);
            Assert.That(removed.Snapshot.ActiveEffects.Contains(EffectId), Is.False);
            Assert.That(removed.Snapshot.RuleBindings.Contains(BindingId), Is.False);
            Assert.That(removed.Snapshot.Frequencies.Contains(BindingId), Is.False);
            ActiveEffectRemovedFact fact = (ActiveEffectRemovedFact)removed.Facts.Single();
            Assert.That(fact.RemovedStatus, Is.EqualTo(ActiveEffectStatus.Expired));
            Assert.That(fact.RemovedVersion, Is.EqualTo(new EffectStateVersion(1)));
        }

        [Test]
        public void LifecycleOperationsAreNestedOnly()
        {
            RuleRegistry registry = CreateRegistry();
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore())
                .UseActiveEffectRules(registry)
                .Build();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(
                        new CreateActiveEffectOp(effect, CreateBinding(effect))
                    )
            );

            Assert.That(error.Message, Does.Contain("nested-only"));
            Assert.That(dispatcher.Trace.OrderedFrames, Is.Empty);
        }

        [Test]
        public void ActiveEffectRulesPreventDefinitionRegistryDrift()
        {
            RuleRegistry registry = CreateRegistry();
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(
                new InMemoryRulesStore()
            ).UseActiveEffectRules(registry);

            Assert.That(builder.UseRuleRegistry(registry), Is.SameAs(builder));
            Assert.Throws<InvalidOperationException>(() =>
                builder.UseRuleRegistry(new RuleRegistryBuilder().Build())
            );
        }

        [Test]
        public async Task NestedLifecycleRejectionsRemainTypedOperationResults()
        {
            RuleRegistry registry = CreateRegistry();
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            InMemoryRulesStore store = CreateSeededStore(effect);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<UpdateWorkflowOp, OpResult<ActiveEffectStateUpdateOutcome>>(
                    new UpdateWorkflowHandler()
                )
                .UseActiveEffectRules(registry)
                .Build();
            UpdateActiveEffectStateOp update = UpdateActiveEffectStateOp.Create(
                EffectId,
                EffectStateVersion.Initial,
                new OtherEffectState(),
                Source
            );

            OpResult<OpResult<ActiveEffectStateUpdateOutcome>> result = await dispatcher.Dispatch(
                new UpdateWorkflowOp(update)
            );

            ResolvedOpResult<OpResult<ActiveEffectStateUpdateOutcome>> workflow =
                (ResolvedOpResult<OpResult<ActiveEffectStateUpdateOutcome>>)result;
            Assert.That(
                workflow.Value,
                Is.TypeOf<InvalidOpResult<ActiveEffectStateUpdateOutcome>>()
            );
            InvalidOpResult<ActiveEffectStateUpdateOutcome> invalid =
                (InvalidOpResult<ActiveEffectStateUpdateOutcome>)workflow.Value;
            Assert.That(invalid.Reason, Does.Contain(nameof(OtherEffectState)));
            Assert.That(invalid.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.Zero);
        }

        private static RuleRegistry CreateRegistry()
        {
            RuleRegistryBuilder builder = new RuleRegistryBuilder();
            builder.Define(DefinitionId);
            return builder.Build();
        }

        private static ActiveEffectInstance CreateEffect(IEffectState state) =>
            new ActiveEffectInstance(
                EffectId,
                DefinitionId,
                SourceCreature,
                Source,
                EffectDuration.OneMinute,
                state
            );

        private static ActiveRuleBinding CreateBinding(ActiveEffectInstance effect) =>
            new ActiveRuleBinding(
                BindingId,
                effect.DefinitionId,
                Owner,
                effect.Id,
                effect.Source,
                1
            );

        private static RageEffectState CreateRageReceipt(OpId root, int committedTemporaryHitPoints)
        {
            HealthState before = new HealthState(10, 10);
            HealthState after = new HealthState(
                10,
                10,
                committedTemporaryHitPoints,
                RageRules.Source
            ).WithTemporaryHitPointRevision(1);
            return RageEffectState
                .CreatePending(SourceCreature, default, root, 3)
                .WithGrantTransition(
                    new TemporaryHitPointsGrantTransition(
                        before,
                        after,
                        new TemporaryHitPointsGrantOutcome(
                            true,
                            false,
                            0,
                            committedTemporaryHitPoints
                        )
                    )
                );
        }

        private static ActiveEffectInstance CreateRageEffect(RageEffectState receipt) =>
            new ActiveEffectInstance(
                EffectId,
                RageActionDefinition.EffectDefinitionId,
                SourceCreature,
                RageRules.Source,
                EffectDuration.OneMinute,
                receipt
            );

        private static ActiveRuleBinding CreateRageBinding(ActiveEffectInstance effect) =>
            new ActiveRuleBinding(
                BindingId,
                effect.DefinitionId,
                Owner,
                effect.Id,
                effect.Source,
                1
            );

        private static InMemoryRulesStore CreateSeededStore(ActiveEffectInstance effect) =>
            CreateSeededStore(effect, CreateBinding(effect));

        private static InMemoryRulesStore CreateSeededStore(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding
        ) =>
            new InMemoryRulesStore(
                new RulesStateSeed().SeedActiveEffect(effect).SeedRuleBinding(binding)
            );

        private static RulesStateSeed CreateActiveEncounterSeed()
        {
            EncounterId encounter = new EncounterId("active-effect-test-encounter");
            PlayerId party = new PlayerId("party");
            InitiativeEntry participant = new InitiativeEntry(
                SourceCreature,
                party,
                10,
                0,
                0,
                RoundNumber.First
            );
            return new RulesStateSeed()
                .SeedCreature(new CreatureState(Owner, party))
                .SeedEncounter(
                    new EncounterState(
                        encounter,
                        EncounterPhase.Active,
                        party,
                        RoundNumber.First,
                        new[] { participant },
                        -1,
                        null,
                        1,
                        null
                    )
                );
        }

        private static ReductionContext<TOp> Context<TOp>(TOp op) =>
            new ReductionContext<TOp>(op, new OpId(2), new OpId(1), Source);

        private sealed class AuraEffectState : IEffectState, IEquatable<AuraEffectState>
        {
            public AuraEffectState(int bonus) => Bonus = bonus;

            public int Bonus { get; }

            public bool Equals(AuraEffectState other) => other != null && Bonus == other.Bonus;

            public override bool Equals(object obj) =>
                obj is AuraEffectState other && Equals(other);

            public override int GetHashCode() => Bonus;
        }

        private sealed class OtherEffectState : IEffectState { }

        private sealed class UpdateWorkflowOp : IRuleOp<OpResult<ActiveEffectStateUpdateOutcome>>
        {
            public UpdateWorkflowOp(UpdateActiveEffectStateOp update) => Update = update;

            public UpdateActiveEffectStateOp Update { get; }
        }

        private sealed class UpdateWorkflowHandler
            : IOpHandler<UpdateWorkflowOp, OpResult<ActiveEffectStateUpdateOutcome>>
        {
            public async ValueTask<OpResult<ActiveEffectStateUpdateOutcome>> Handle(
                OpFrame<UpdateWorkflowOp> frame,
                OpHandlerContext context
            ) => await context.Dispatch(frame.Op.Update);
        }
    }
}
