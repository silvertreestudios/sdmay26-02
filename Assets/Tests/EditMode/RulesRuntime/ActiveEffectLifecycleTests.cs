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
        public void DefinitionDeclaresOneExactEffectStateType()
        {
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RuleDefinitionBuilder definition = registryBuilder.Define(DefinitionId);
            definition.EffectState<AuraEffectState>();
            RuleDefinition built = registryBuilder.Build().Definitions[0];

            Assert.That(built.SupportsActiveEffects, Is.True);
            Assert.That(built.EffectStateType, Is.EqualTo(typeof(AuraEffectState)));
            Assert.That(built.AcceptsEffectState(new AuraEffectState(1)), Is.True);
            Assert.That(built.AcceptsEffectState(new OtherEffectState()), Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                definition.EffectState<OtherEffectState>()
            );
            Assert.Throws<InvalidOperationException>(() =>
                new RuleRegistryBuilder()
                    .Define(new RuleDefinitionId("interface-state"))
                    .EffectState<IEffectState>()
            );
            Assert.Throws<InvalidOperationException>(() =>
                new RuleRegistryBuilder()
                    .Define(new RuleDefinitionId("abstract-state"))
                    .EffectState<AbstractEffectState>()
            );
        }

        [Test]
        public void CreateAtomicallyCommitsTypedEffectBindingAndFact()
        {
            RuleRegistry registry = CreateRegistry();
            InMemoryRulesStore store = new InMemoryRulesStore();
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
            Assert.That(fact.StateType, Is.EqualTo(typeof(AuraEffectState)));
            Assert.That(fact.Duration, Is.EqualTo(EffectDuration.OneMinute));
        }

        [Test]
        public void CreateRejectsWrongStateTypeWithoutPartialBindingWrite()
        {
            RuleRegistry registry = CreateRegistry();
            InMemoryRulesStore store = new InMemoryRulesStore();
            ActiveEffectInstance effect = CreateEffect(new OtherEffectState());

            ReductionResult<ActiveEffectCreationOutcome> result = store.Reduce(
                Context(new CreateActiveEffectOp(effect, CreateBinding(effect))),
                new CreateActiveEffectReducer(registry)
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.RejectionReason, Does.Contain(nameof(AuraEffectState)));
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(result.Snapshot.RuleBindings, Is.Empty);
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public void UpdateUsesExactStateTypeAndOptimisticVersionWithoutMutatingOldSnapshot()
        {
            RuleRegistry registry = CreateRegistry();
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
                new UpdateActiveEffectStateReducer(registry)
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
                new UpdateActiveEffectStateReducer(registry)
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
            RuleRegistry registry = CreateRegistry();
            ActiveEffectInstance active = CreateEffect(new AuraEffectState(1));
            InMemoryRulesStore store = CreateSeededStore(active);
            UpdateActiveEffectStateReducer reducer = new UpdateActiveEffectStateReducer(registry);

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
        public void ExpireDisablesBindingAndRemoveDeletesBothInAtomicTransactions()
        {
            ActiveEffectInstance effect = CreateEffect(new AuraEffectState(1));
            ActiveRuleBinding binding = CreateBinding(effect);
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedActiveEffect(effect)
                    .SeedRuleBinding(binding)
                    .SeedFrequency(BindingId, new FrequencyState(3, 1))
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
            builder.Define(DefinitionId).EffectState<AuraEffectState>();
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

        private abstract class AbstractEffectState : IEffectState { }

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
