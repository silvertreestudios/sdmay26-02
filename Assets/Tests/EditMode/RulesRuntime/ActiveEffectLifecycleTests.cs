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
        public void RemovalFactRejectsUndefinedReason()
        {
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ActiveEffectRemovedFact(
                    effect,
                    CreateBinding(effect),
                    (ActiveEffectRemovalReason)99
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
        public void UpdateRejectsUnknownAndWrongStateTypeAsTypedResults()
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
            Assert.That(unknown.IsRejected, Is.True);
            Assert.That(unknown.RejectionReason, Does.Contain("unknown"));
            Assert.That(wrongType.IsRejected, Is.True);
            Assert.That(wrongType.RejectionReason, Does.Contain(nameof(OtherEffectState)));
            Assert.That(store.Snapshot.Version, Is.Zero);
        }

        [Test]
        public void RemoveDeletesAssociatedStateAndEmitsSelfContainedFact()
        {
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            ActiveRuleBinding binding = CreateBinding(effect);
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedActiveEffect(effect)
                    .SeedRuleBinding(binding)
                    .SeedActiveEffectTiming(
                        new ActiveEffectTimingState(
                            EffectId,
                            new EncounterId("timing-encounter"),
                            BindingId,
                            SourceCreature,
                            1,
                            false,
                            1
                        )
                    )
                    .SeedFrequency(
                        BindingId,
                        new FrequencyState(new EncounterId("frequency-encounter"), 3, 1)
                    )
            );

            ReductionResult<ActiveEffectRemovalOutcome> stale = store.Reduce(
                Context(
                    new RemoveActiveEffectOp(
                        EffectId,
                        BindingId,
                        new EffectStateVersion(1),
                        ActiveEffectRemovalReason.Ended,
                        Source
                    )
                ),
                new RemoveActiveEffectReducer()
            );

            Assert.That(stale.IsRejected, Is.True);
            Assert.That(stale.DidCommit, Is.False);
            Assert.That(stale.Facts, Is.Empty);
            Assert.That(stale.Snapshot.ActiveEffects.Contains(EffectId), Is.True);
            Assert.That(stale.Snapshot.RuleBindings.Contains(BindingId), Is.True);
            Assert.That(stale.Snapshot.ActiveEffectTimings.Contains(EffectId), Is.True);
            Assert.That(stale.Snapshot.Frequencies.Contains(BindingId), Is.True);

            ReductionResult<ActiveEffectRemovalOutcome> removed = store.Reduce(
                Context(
                    new RemoveActiveEffectOp(
                        EffectId,
                        BindingId,
                        EffectStateVersion.Initial,
                        ActiveEffectRemovalReason.Ended,
                        Source
                    )
                ),
                new RemoveActiveEffectReducer()
            );

            Assert.That(removed.IsAccepted, Is.True);
            Assert.That(removed.Snapshot.ActiveEffects.Contains(EffectId), Is.False);
            Assert.That(removed.Snapshot.RuleBindings.Contains(BindingId), Is.False);
            Assert.That(removed.Snapshot.ActiveEffectTimings.Contains(EffectId), Is.False);
            Assert.That(removed.Snapshot.Frequencies.Contains(BindingId), Is.False);
            ActiveEffectRemovedFact fact = (ActiveEffectRemovedFact)removed.Facts.Single();
            Assert.That(fact.Effect, Is.SameAs(effect));
            Assert.That(fact.Binding, Is.SameAs(binding));
            Assert.That(fact.Reason, Is.EqualTo(ActiveEffectRemovalReason.Ended));
            Assert.That(fact.RemovedVersion, Is.EqualTo(EffectStateVersion.Initial));
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
            return new RulesStateSeed().SeedEncounter(
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
