using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies binding-controlled middleware and post-commit Fact-listener contracts.
    /// </summary>
    public sealed class RuleExtensionTests
    {
        private static readonly CreatureId Creature = new CreatureId("extension-creature");
        private static readonly RuleSource Source = RuleSource.FromSlug("extension-test");
        private static readonly RuleDefinitionId DefinitionA = new RuleDefinitionId("definition-a");
        private static readonly RuleDefinitionId DefinitionB = new RuleDefinitionId("definition-b");
        private static readonly RuleDefinitionId DefinitionC = new RuleDefinitionId("definition-c");

        [Test]
        public void RegistryRejectsDuplicateAndIncompatibleRegistrations()
        {
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RuleDefinitionBuilder definition = registryBuilder.Define(DefinitionA);
            DelegateMiddleware<ValueOp, int> middleware =
                new DelegateMiddleware<ValueOp, int>((binding, frame, context, next) => next());
            definition.Middleware(RuleLifecyclePhase.Transformation, middleware);

            Assert.Throws<InvalidOperationException>(() =>
                registryBuilder.Define(DefinitionA));
            Assert.Throws<InvalidOperationException>(() =>
                definition.Middleware(RuleLifecyclePhase.Transformation, middleware));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                rulesWithInvalidPhase());

            RuleRegistry registry = registryBuilder.Build();
            Assert.Throws<InvalidOperationException>(() => new RuleDispatcherBuilder(CreateStore())
                .UseRuleRegistry(registry)
                .Build());

            RuleRegistryBuilder mismatchBuilder = new RuleRegistryBuilder();
            mismatchBuilder.Define(DefinitionA).Middleware(
                RuleLifecyclePhase.Transformation,
                new DelegateMiddleware<AmbiguousOp, string>(
                    (binding, frame, context, next) => next()));
            InvalidOperationException mismatch = Assert.Throws<InvalidOperationException>(() =>
                new RuleDispatcherBuilder(CreateStore())
                    .RegisterHandler<AmbiguousOp, int>(new AmbiguousIntHandler())
                    .UseRuleRegistry(mismatchBuilder.Build())
                    .Build());

            Assert.That(mismatch.Message, Does.Contain("expects String"));

            RuleDispatcher unknownDefinition = new RuleDispatcherBuilder(
                    CreateStore(Binding("unknown", DefinitionC, 0)))
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .UseRuleRegistry(new RuleRegistryBuilder().Build())
                .Build();
            InvalidOperationException unknown = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await unknownDefinition.Dispatch(new ValueOp(1)));
            Assert.That(unknown.Message, Does.Contain("unknown rule definition"));

            void rulesWithInvalidPhase() => registryBuilder.Define(DefinitionB).Middleware(
                (RuleLifecyclePhase)999,
                middleware);
        }

        [Test]
        public async Task MiddlewareUsesStablePhaseCreationAndIdOrderAcrossRuns()
        {
            List<string> calls = new List<string>();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(
                RuleLifecyclePhase.Prevention,
                new LoggingMiddleware(calls, 1));
            rules.Define(DefinitionB).Middleware(
                RuleLifecyclePhase.Transformation,
                new LoggingMiddleware(calls, 1));
            rules.Define(DefinitionC).Middleware(
                RuleLifecyclePhase.Transformation,
                new LoggingMiddleware(calls, 1));

            ActiveRuleBinding phaseFirst = Binding("z-phase", DefinitionA, 20);
            ActiveRuleBinding idLast = Binding("z-transform", DefinitionB, 1);
            ActiveRuleBinding idFirst = Binding("a-transform", DefinitionC, 1);
            ValueHandler handler = new ValueHandler(calls);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(
                    idLast,
                    phaseFirst,
                    idFirst))
                .RegisterHandler<ValueOp, int>(handler)
                .UseRuleRegistry(rules.Build())
                .Build();

            for (int run = 0; run < 2; run++)
            {
                OpResult<int> result = await dispatcher.Dispatch(new ValueOp(1));
                Assert.That(RequireResolved(result).Value, Is.EqualTo(4));
            }

            string[] oneRun =
            {
                "before:z-phase",
                "before:a-transform",
                "before:z-transform",
                "handler",
                "after:z-transform",
                "after:a-transform",
                "after:z-phase"
            };
            Assert.That(calls.Take(oneRun.Length), Is.EqualTo(oneRun));
            Assert.That(calls.Skip(oneRun.Length), Is.EqualTo(oneRun));
        }

        [Test]
        public async Task MiddlewareCanDispatchNestedWorkAndAlterTheTypedResult()
        {
            NestedIncrementMiddleware middleware = new NestedIncrementMiddleware(3);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(
                RuleLifecyclePhase.Transformation,
                middleware);
            ActiveRuleBinding binding = Binding("nested-binding", DefinitionA, 0);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(binding))
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(2));

            Assert.That(RequireResolved(result).Value, Is.EqualTo(15));
            Assert.That(result.Facts, Has.Count.EqualTo(1));
            Assert.That(middleware.SnapshotAfterChild, Is.EqualTo(13));
            Assert.That(middleware.Binding, Is.SameAs(binding));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(13));
        }

        [Test]
        public async Task MiddlewareCanShortCircuitWithoutInvokingTheHandler()
        {
            ValueHandler handler = new ValueHandler();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(
                RuleLifecyclePhase.Prevention,
                new DelegateMiddleware<ValueOp, int>((binding, frame, context, next) =>
                    new ValueTask<OpResult<int>>(OpResult<int>.Invalid("prevented"))));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(Binding("short-circuit", DefinitionA, 0)))
                .RegisterHandler<ValueOp, int>(handler)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(5));

            Assert.That(result, Is.TypeOf<InvalidOpResult<int>>());
            Assert.That(((InvalidOpResult<int>)result).Reason, Is.EqualTo("prevented"));
            Assert.That(handler.Calls, Is.Zero);
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public async Task DisabledOrRemovedBindingsStopMiddlewareImmediatelyAfterCommit()
        {
            List<string> calls = new List<string>();
            ActiveRuleBinding controller = Binding("controller", DefinitionA, 0);
            ActiveRuleBinding disabledLater = Binding("disabled-later", DefinitionB, 1);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(
                RuleLifecyclePhase.Prevention,
                new DisableThenContinueMiddleware(disabledLater.Id, calls));
            rules.Define(DefinitionB).Middleware(
                RuleLifecyclePhase.Transformation,
                new LoggingMiddleware(calls, 100));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(controller, disabledLater))
                .RegisterHandler<ValueOp, int>(new ValueHandler(calls))
                .RegisterReducer<SetBindingEnabledOp, bool>(new SetBindingEnabledReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> first = await dispatcher.Dispatch(new ValueOp(4));
            OpResult<int> second = await dispatcher.Dispatch(new ValueOp(4));

            Assert.That(RequireResolved(first).Value, Is.EqualTo(4));
            Assert.That(RequireResolved(second).Value, Is.EqualTo(4));
            Assert.That(calls, Is.EqualTo(new[]
            {
                "disable:disabled-later", "handler",
                "disable:disabled-later", "handler"
            }));
            Assert.That(dispatcher.Snapshot.RuleBindings[disabledLater.Id].IsEnabled, Is.False);
        }

        [Test]
        public async Task BindingActivatedByStateParticipatesStartingWithTheNextFrame()
        {
            List<string> calls = new List<string>();
            ActiveRuleBinding controller = Binding("activation-controller", DefinitionA, 0);
            ActiveRuleBinding activated = Binding("activated-rule", DefinitionB, 1, false);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(
                RuleLifecyclePhase.Prevention,
                new EnableThenContinueMiddleware(activated.Id));
            rules.Define(DefinitionB).Middleware(
                RuleLifecyclePhase.Transformation,
                new LoggingMiddleware(calls, 100));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(controller, activated))
                .RegisterHandler<ValueOp, int>(new ValueHandler(calls))
                .RegisterReducer<SetBindingEnabledOp, bool>(new SetBindingEnabledReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> activationFrame = await dispatcher.Dispatch(new ValueOp(1));
            OpResult<int> nextFrame = await dispatcher.Dispatch(new ValueOp(1));

            Assert.That(RequireResolved(activationFrame).Value, Is.EqualTo(1));
            Assert.That(RequireResolved(nextFrame).Value, Is.EqualTo(101));
            Assert.That(calls, Is.EqualTo(new[]
            {
                "handler",
                "before:activated-rule", "handler", "after:activated-rule"
            }));
        }

        [Test]
        public async Task FactListenersObserveCommittedStateAndInvalidRootsNotifyNone()
        {
            SnapshotFactListener listener = new SnapshotFactListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            RuleDefinitionBuilder definition = rules.Define(DefinitionA);
            definition.FactListener(RuleLifecyclePhase.Observation, listener);
            definition.Middleware(
                RuleLifecyclePhase.Prevention,
                new CommitThenInvalidateMiddleware());
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(Binding("fact-listener", DefinitionA, 0)))
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> committed = await dispatcher.Dispatch(
                new RootIncrementOp(new[] { 5 }));
            OpResult<int> invalid = await dispatcher.Dispatch(new ValueOp(0));

            Assert.That(RequireResolved(committed).Value, Is.EqualTo(15));
            Assert.That(listener.ObservedValues, Is.EqualTo(new[] { 15 }));
            Assert.That(invalid, Is.TypeOf<InvalidOpResult<int>>());
            Assert.That(invalid.Facts, Has.Count.EqualTo(1),
                "The nested reducer committed before middleware returned Invalid.");
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(16));
            Assert.That(listener.ObservedValues, Has.Count.EqualTo(1),
                "Invalid roots must not open post-commit listener delivery.");
        }

        [Test]
        public async Task FactListenerDispatchStartsANewCausallyLinkedRoot()
        {
            ReactionHandler reactionHandler = new ReactionHandler();
            DispatchingFactListener listener = new DispatchingFactListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(
                RuleLifecyclePhase.Reaction,
                listener);
            ActiveRuleBinding binding = Binding("causal-listener", DefinitionA, 0);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(binding),
                    new SequentialOpIdProvider(50))
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<ReactionOp, int>(reactionHandler)
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(
                new RootIncrementOp(new[] { 2 }));

            Assert.That(result.Facts, Has.Count.EqualTo(1));
            Assert.That(listener.Binding, Is.SameAs(binding));
            Assert.That(listener.Source, Is.EqualTo(binding.Source));
            Assert.That(listener.DispatchResult, Is.EqualTo(12));
            Assert.That(reactionHandler.SnapshotValue, Is.EqualTo(12));

            RuleFact committedFact = result.Facts[0];
            OpFrame<RootIncrementOp> committedRoot =
                dispatcher.Trace.Get<RootIncrementOp>(new OpId(50));
            OpFrame<ReactionOp> reactionRoot =
                dispatcher.Trace.Get<ReactionOp>(new OpId(52));
            Assert.That(committedFact.SourceOpId, Is.EqualTo(new OpId(51)));
            Assert.That(committedFact.RootOpId, Is.EqualTo(committedRoot.Id));
            Assert.That(reactionRoot.RootId, Is.EqualTo(reactionRoot.Id));
            Assert.That(reactionRoot.ParentId, Is.Null);
            Assert.That(reactionRoot.CauseId, Is.EqualTo(committedFact.SourceOpId));
            Assert.That(dispatcher.Trace.IsCausedBy(
                reactionRoot.Id,
                committedFact.SourceOpId), Is.True);
        }

        [Test]
        public async Task BatchListenersReceiveEachCommittedRootOnceWithoutMixing()
        {
            BatchRecordingListener listener = new BatchRecordingListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactBatchListener(
                RuleLifecyclePhase.Observation,
                listener);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(Binding("batch-listener", DefinitionA, 0)))
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1, 2 }));
            await dispatcher.Dispatch(new RootIncrementOp(new[] { 3 }));

            Assert.That(listener.Batches, Has.Count.EqualTo(2));
            Assert.That(listener.Batches[0].RootId, Is.Not.EqualTo(listener.Batches[1].RootId));
            Assert.That(listener.Batches[0].Facts.Select(fact => fact.Current),
                Is.EqualTo(new[] { 11, 13 }));
            Assert.That(listener.Batches[1].Facts.Select(fact => fact.Current),
                Is.EqualTo(new[] { 16 }));
            Assert.That(listener.Batches.All(batch =>
                batch.Facts.All(fact => fact.RootOpId == batch.RootId)), Is.True);
        }

        [Test]
        public async Task FactListenersUseStablePhaseCreationAndIdOrderAcrossRuns()
        {
            List<string> calls = new List<string>();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(
                RuleLifecyclePhase.Prevention,
                new LoggingFactListener(calls));
            rules.Define(DefinitionB).FactListener(
                RuleLifecyclePhase.Transformation,
                new LoggingFactListener(calls));
            rules.Define(DefinitionC).FactListener(
                RuleLifecyclePhase.Transformation,
                new LoggingFactListener(calls));

            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(
                    Binding("z-transform", DefinitionB, 1),
                    Binding("z-phase", DefinitionA, 20),
                    Binding("a-transform", DefinitionC, 1)))
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }));
            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }));

            string[] oneRun = { "z-phase", "a-transform", "z-transform" };
            Assert.That(calls.Take(oneRun.Length), Is.EqualTo(oneRun));
            Assert.That(calls.Skip(oneRun.Length), Is.EqualTo(oneRun));
        }

        [Test]
        public async Task ListenerRemovedBindingIsSkippedLaterInTheSameDeliveryPlan()
        {
            List<string> calls = new List<string>();
            ActiveRuleBinding controller = Binding("controller-listener", DefinitionA, 0);
            ActiveRuleBinding removed = Binding("removed-listener", DefinitionB, 1);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(
                RuleLifecyclePhase.Prevention,
                new RemovingFactListener(removed.Id, calls));
            rules.Define(DefinitionB).FactListener(
                RuleLifecyclePhase.Observation,
                new LoggingFactListener(calls));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(controller, removed))
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<RemoveBindingRootOp, bool>(new RemoveBindingRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterReducer<RemoveBindingOp, bool>(new RemoveBindingReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }));

            Assert.That(calls, Is.EqualTo(new[] { "remove:removed-listener" }));
            Assert.That(dispatcher.Snapshot.RuleBindings.Contains(removed.Id), Is.False);
        }

        private static InMemoryRulesStore CreateStore(params ActiveRuleBinding[] bindings)
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedHealth(Creature, new HealthState(10, 100));
            foreach (ActiveRuleBinding binding in bindings)
                seed.SeedRuleBinding(binding);
            return new InMemoryRulesStore(seed);
        }

        private static ActiveRuleBinding Binding(
            string id,
            RuleDefinitionId definition,
            long creationOrder,
            bool isEnabled = true) =>
            new ActiveRuleBinding(
                new BindingId(id),
                definition,
                Creature,
                null,
                Source,
                creationOrder,
                isEnabled);

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private sealed class ValueOp : IRuleOp<int>
        {
            public int Value { get; }

            public ValueOp(int value) => Value = value;
        }

        private sealed class ValueHandler : IOpHandler<ValueOp, int>
        {
            private readonly List<string> calls;

            public int Calls { get; private set; }

            public ValueHandler(List<string> calls = null) => this.calls = calls;

            public ValueTask<int> Handle(OpFrame<ValueOp> frame, OpContext context)
            {
                Calls++;
                calls?.Add("handler");
                return new ValueTask<int>(frame.Op.Value);
            }
        }

        private sealed class AmbiguousOp : IRuleOp<int>, IRuleOp<string>
        {
        }

        private sealed class AmbiguousIntHandler : IOpHandler<AmbiguousOp, int>
        {
            public ValueTask<int> Handle(OpFrame<AmbiguousOp> frame, OpContext context) =>
                new ValueTask<int>(1);
        }

        private sealed class DelegateMiddleware<TOp, TResult> : IOpMiddleware<TOp, TResult>
            where TOp : IRuleOp<TResult>
        {
            private readonly Func<ActiveRuleBinding, OpFrame<TOp>, OpContext,
                OpNext<TResult>, ValueTask<OpResult<TResult>>> invoke;

            public DelegateMiddleware(
                Func<ActiveRuleBinding, OpFrame<TOp>, OpContext,
                    OpNext<TResult>, ValueTask<OpResult<TResult>>> invoke) =>
                this.invoke = invoke;

            public ValueTask<OpResult<TResult>> Invoke(
                ActiveRuleBinding binding,
                OpFrame<TOp> frame,
                OpContext context,
                OpNext<TResult> next) =>
                invoke(binding, frame, context, next);
        }

        private sealed class LoggingMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly List<string> calls;
            private readonly int addedValue;

            public LoggingMiddleware(List<string> calls, int addedValue)
            {
                this.calls = calls;
                this.addedValue = addedValue;
            }

            public async ValueTask<OpResult<int>> Invoke(
                ActiveRuleBinding binding,
                OpFrame<ValueOp> frame,
                OpContext context,
                OpNext<int> next)
            {
                Assert.That(context.ActiveBinding, Is.SameAs(binding));
                Assert.That(context.Source, Is.EqualTo(binding.Source));
                calls.Add($"before:{binding.Id.Value}");
                OpResult<int> result = await next();
                calls.Add($"after:{binding.Id.Value}");
                if (result is ResolvedOpResult<int> resolved)
                    return OpResult<int>.Resolved(resolved.Value + addedValue);
                return result;
            }
        }

        private sealed class NestedIncrementMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly int amount;

            public int SnapshotAfterChild { get; private set; }
            public ActiveRuleBinding Binding { get; private set; }

            public NestedIncrementMiddleware(int amount) => this.amount = amount;

            public async ValueTask<OpResult<int>> Invoke(
                ActiveRuleBinding binding,
                OpFrame<ValueOp> frame,
                OpContext context,
                OpNext<int> next)
            {
                Binding = binding;
                OpResult<int> changed = await context.Dispatch(new IncrementOp(amount));
                SnapshotAfterChild = context.Snapshot.Health[Creature].Current;
                OpResult<int> current = await next();
                return OpResult<int>.Resolved(
                    RequireResolved(current).Value + RequireResolved(changed).Value);
            }
        }

        private sealed class DisableThenContinueMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly BindingId bindingId;
            private readonly List<string> calls;

            public DisableThenContinueMiddleware(BindingId bindingId, List<string> calls)
            {
                this.bindingId = bindingId;
                this.calls = calls;
            }

            public async ValueTask<OpResult<int>> Invoke(
                ActiveRuleBinding binding,
                OpFrame<ValueOp> frame,
                OpContext context,
                OpNext<int> next)
            {
                calls.Add($"disable:{bindingId.Value}");
                await context.Dispatch(new SetBindingEnabledOp(bindingId, false));
                return await next();
            }
        }

        private sealed class EnableThenContinueMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly BindingId bindingId;

            public EnableThenContinueMiddleware(BindingId bindingId) => this.bindingId = bindingId;

            public async ValueTask<OpResult<int>> Invoke(
                ActiveRuleBinding binding,
                OpFrame<ValueOp> frame,
                OpContext context,
                OpNext<int> next)
            {
                if (!context.Snapshot.RuleBindings[bindingId].IsEnabled)
                    await context.Dispatch(new SetBindingEnabledOp(bindingId, true));
                return await next();
            }
        }

        private sealed class IncrementOp : IRuleOp<int>
        {
            public int Amount { get; }

            public IncrementOp(int amount) => Amount = amount;
        }

        private sealed class IncrementReducer : IOpReducer<IncrementOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<IncrementOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                HealthState previous = state.Health.TryGet(Creature, out HealthState current)
                    ? current
                    : new HealthState(0, 100);
                HealthState changed = new HealthState(
                    previous.Current + context.Op.Amount,
                    previous.Maximum,
                    previous.Temporary);
                state.Health.Set(Creature, changed);
                facts.Stage(new CounterChangedFact(previous.Current, changed.Current));
                return ReductionResult<int>.Accept(changed.Current);
            }
        }

        private sealed class SetBindingEnabledOp : IRuleOp<bool>
        {
            public BindingId BindingId { get; }
            public bool IsEnabled { get; }

            public SetBindingEnabledOp(BindingId bindingId, bool isEnabled)
            {
                BindingId = bindingId;
                IsEnabled = isEnabled;
            }
        }

        private sealed class SetBindingEnabledReducer : IOpReducer<SetBindingEnabledOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<SetBindingEnabledOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                if (!state.RuleBindings.TryGet(
                    context.Op.BindingId,
                    out ActiveRuleBinding current))
                {
                    return ReductionResult<bool>.Reject("Binding not found.");
                }

                ActiveRuleBinding changed = new ActiveRuleBinding(
                    current.Id,
                    current.DefinitionId,
                    current.Owner,
                    current.EffectId,
                    current.Source,
                    current.CreationOrder,
                    context.Op.IsEnabled);
                state.RuleBindings.Set(changed.Id, changed);
                facts.Stage(new BindingChangedFact(changed.Id, changed.IsEnabled));
                return ReductionResult<bool>.Accept(changed.IsEnabled);
            }
        }

        private sealed class CounterChangedFact : RuleFact
        {
            public int Previous { get; }
            public int Current { get; }

            public CounterChangedFact(int previous, int current)
            {
                Previous = previous;
                Current = current;
            }
        }

        private sealed class BindingChangedFact : RuleFact
        {
            public BindingId BindingId { get; }
            public bool IsEnabled { get; }

            public BindingChangedFact(BindingId bindingId, bool isEnabled)
            {
                BindingId = bindingId;
                IsEnabled = isEnabled;
            }
        }

        private sealed class RootIncrementOp : IRuleOp<int>
        {
            public IReadOnlyList<int> Amounts { get; }

            public RootIncrementOp(IEnumerable<int> amounts) =>
                Amounts = Array.AsReadOnly(amounts.ToArray());
        }

        private sealed class RootIncrementHandler : IOpHandler<RootIncrementOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<RootIncrementOp> frame,
                OpContext context)
            {
                int current = context.Snapshot.Health[Creature].Current;
                foreach (int amount in frame.Op.Amounts)
                {
                    OpResult<int> changed = await context.Dispatch(new IncrementOp(amount));
                    current = RequireResolved(changed).Value;
                }
                return current;
            }
        }

        private sealed class CommitThenInvalidateMiddleware : IOpMiddleware<ValueOp, int>
        {
            public async ValueTask<OpResult<int>> Invoke(
                ActiveRuleBinding binding,
                OpFrame<ValueOp> frame,
                OpContext context,
                OpNext<int> next)
            {
                await context.Dispatch(new IncrementOp(1));
                return OpResult<int>.Invalid("invalid after committed child");
            }
        }

        private sealed class SnapshotFactListener : IFactListener<CounterChangedFact>
        {
            public List<int> ObservedValues { get; } = new List<int>();

            public ValueTask OnFactCommitted(
                ActiveRuleBinding binding,
                CounterChangedFact fact,
                FactContext context)
            {
                int current = context.Snapshot.Health[Creature].Current;
                Assert.That(current, Is.EqualTo(fact.Current));
                Assert.That(context.Binding, Is.SameAs(binding));
                Assert.That(context.Source, Is.EqualTo(binding.Source));
                ObservedValues.Add(current);
                return default;
            }
        }

        private sealed class ReactionOp : IRuleOp<int>
        {
            public int TriggerValue { get; }

            public ReactionOp(int triggerValue) => TriggerValue = triggerValue;
        }

        private sealed class ReactionHandler : IOpHandler<ReactionOp, int>
        {
            public int SnapshotValue { get; private set; }

            public ValueTask<int> Handle(OpFrame<ReactionOp> frame, OpContext context)
            {
                SnapshotValue = context.Snapshot.Health[Creature].Current;
                return new ValueTask<int>(frame.Op.TriggerValue);
            }
        }

        private sealed class DispatchingFactListener : IFactListener<CounterChangedFact>
        {
            public ActiveRuleBinding Binding { get; private set; }
            public RuleSource Source { get; private set; }
            public int DispatchResult { get; private set; }

            public async ValueTask OnFactCommitted(
                ActiveRuleBinding binding,
                CounterChangedFact fact,
                FactContext context)
            {
                Binding = context.Binding;
                Source = context.Source;
                OpResult<int> result = await context.Dispatch(new ReactionOp(fact.Current));
                DispatchResult = RequireResolved(result).Value;
            }
        }

        private sealed class BatchRecordingListener : IFactBatchListener<CounterChangedFact>
        {
            public List<CommittedFactBatch<CounterChangedFact>> Batches { get; } =
                new List<CommittedFactBatch<CounterChangedFact>>();

            public ValueTask OnFactsCommitted(
                ActiveRuleBinding binding,
                CommittedFactBatch<CounterChangedFact> batch,
                FactContext context)
            {
                Assert.That(context.CommittedRootId, Is.EqualTo(batch.RootId));
                Batches.Add(batch);
                return default;
            }
        }

        private sealed class LoggingFactListener : IFactListener<CounterChangedFact>
        {
            private readonly List<string> calls;

            public LoggingFactListener(List<string> calls) => this.calls = calls;

            public ValueTask OnFactCommitted(
                ActiveRuleBinding binding,
                CounterChangedFact fact,
                FactContext context)
            {
                calls.Add(binding.Id.Value);
                return default;
            }
        }

        private sealed class RemovingFactListener : IFactListener<CounterChangedFact>
        {
            private readonly BindingId removedBinding;
            private readonly List<string> calls;

            public RemovingFactListener(BindingId removedBinding, List<string> calls)
            {
                this.removedBinding = removedBinding;
                this.calls = calls;
            }

            public async ValueTask OnFactCommitted(
                ActiveRuleBinding binding,
                CounterChangedFact fact,
                FactContext context)
            {
                calls.Add($"remove:{removedBinding.Value}");
                OpResult<bool> removed = await context.Dispatch(
                    new RemoveBindingRootOp(removedBinding));
                Assert.That(RequireResolved(removed).Value, Is.True);
            }
        }

        private sealed class RemoveBindingRootOp : IRuleOp<bool>
        {
            public BindingId BindingId { get; }

            public RemoveBindingRootOp(BindingId bindingId) => BindingId = bindingId;
        }

        private sealed class RemoveBindingRootHandler : IOpHandler<RemoveBindingRootOp, bool>
        {
            public async ValueTask<bool> Handle(
                OpFrame<RemoveBindingRootOp> frame,
                OpContext context)
            {
                OpResult<bool> removed = await context.Dispatch(
                    new RemoveBindingOp(frame.Op.BindingId));
                return RequireResolved(removed).Value;
            }
        }

        private sealed class RemoveBindingOp : IRuleOp<bool>
        {
            public BindingId BindingId { get; }

            public RemoveBindingOp(BindingId bindingId) => BindingId = bindingId;
        }

        private sealed class RemoveBindingReducer : IOpReducer<RemoveBindingOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<RemoveBindingOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                if (!state.RuleBindings.Remove(context.Op.BindingId))
                    return ReductionResult<bool>.Reject("Binding not found.");
                facts.Stage(new BindingChangedFact(context.Op.BindingId, false));
                return ReductionResult<bool>.Accept(true);
            }
        }
    }
}
