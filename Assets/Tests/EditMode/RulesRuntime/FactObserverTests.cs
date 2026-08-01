using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies reduction-scoped, awaited delivery to dynamically registered Fact observers.
    /// </summary>
    public sealed class FactObserverTests
    {
        private static readonly CreatureId Creature = new CreatureId("fact-observer-creature");
        private static readonly RuleSource Source = RuleSource.FromSlug("fact-observer-test");
        private static readonly RuleDefinitionId ListenerDefinition = new RuleDefinitionId(
            "fact-observer-listener"
        );

        [Test]
        public async Task ObserverReceivesExactCommittedSnapshotAndPacesTheNextReduction()
        {
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            FirstDeliveryGateObserver observer = new FirstDeliveryGateObserver();
            dispatcher.RegisterFactObserver(observer);

            Task<OpResult<int>> dispatch = dispatcher
                .Dispatch(new RootOp(new ChangeOp(1, 1), new ChangeOp(1, 2)))
                .AsTask();

            await observer.FirstStarted;

            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(1));
            Assert.That(observer.Snapshots.Single(), Is.SameAs(store.Snapshot));
            Assert.That(observer.Snapshots.Single().Version, Is.EqualTo(1));
            Assert.That(observer.Facts.Single().IsStamped, Is.True);
            Assert.That(observer.Facts.Single().Id.IsEmpty, Is.False);
            Assert.That(observer.Facts.Single().SourceOpId.IsEmpty, Is.False);
            Assert.That(observer.Facts.Single().RootOpId.IsEmpty, Is.False);
            Assert.That(observer.Facts.Single().Source, Is.EqualTo(Source));
            Assert.That(dispatch.IsCompleted, Is.False);

            observer.ReleaseFirst();
            OpResult<int> result = await dispatch;

            Assert.That(((ResolvedOpResult<int>)result).Value, Is.EqualTo(2));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(2));
            Assert.That(observer.Sequences, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(observer.Snapshots[1], Is.SameAs(store.Snapshot));
        }

        [Test]
        public async Task RejectedAndAcceptedNoCommitReductionsNotifyNobody()
        {
            InMemoryRulesStore store = CreateStore();
            CountingObserver observer = new CountingObserver();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            dispatcher.RegisterFactObserver(observer);

            OpResult<int> result = await dispatcher.Dispatch(new RejectionAndNoCommitRootOp());

            Assert.That(result, Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(observer.Count, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public async Task RegistrationTokenUnregistersObserverExactlyOnce()
        {
            CountingObserver observer = new CountingObserver();
            RuleDispatcher dispatcher = CreateDispatcher(CreateStore());
            IDisposable registration = dispatcher.RegisterFactObserver(observer);

            await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 1)));
            registration.Dispose();
            registration.Dispose();
            await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 2)));

            Assert.That(observer.Count, Is.EqualTo(1));
            Assert.That(dispatcher.UnregisterFactObserver(observer), Is.False);
        }

        [Test]
        public async Task MultipleFactsAndObserversUseFactThenRegistrationOrder()
        {
            List<string> deliveries = new List<string>();
            RuleDispatcher dispatcher = CreateDispatcher(CreateStore());
            dispatcher.RegisterFactObserver(new RecordingObserver("first", deliveries));
            dispatcher.RegisterFactObserver(new RecordingObserver("second", deliveries));

            await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 10, 11)));

            Assert.That(
                deliveries,
                Is.EqualTo(new[] { "10:first", "10:second", "11:first", "11:second" })
            );
        }

        [Test]
        public async Task RegistrationChangesDuringCallbackApplyToLaterNotificationsOnly()
        {
            List<string> deliveries = new List<string>();
            RuleDispatcher dispatcher = CreateDispatcher(CreateStore());
            RecordingObserver removed = new RecordingObserver("removed", deliveries);
            RecordingObserver added = new RecordingObserver("added", deliveries);
            MutatingObserver mutating = new MutatingObserver(
                dispatcher,
                removed,
                added,
                deliveries
            );
            dispatcher.RegisterFactObserver(mutating);
            dispatcher.RegisterFactObserver(removed);

            await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 1, 2)));
            await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 3)));

            Assert.That(
                deliveries,
                Is.EqualTo(
                    new[]
                    {
                        "1:mutating",
                        "1:removed",
                        "2:mutating",
                        "2:removed",
                        "3:mutating",
                        "3:added",
                    }
                )
            );
        }

        [Test]
        public void ObserverFailuresRunEveryDeliveryAndAggregateInDeliveryOrderAfterCommit()
        {
            InMemoryRulesStore store = CreateStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            InvalidOperationException first = new InvalidOperationException("first");
            ApplicationException second = new ApplicationException("second");
            CountingObserver completed = new CountingObserver();
            dispatcher.RegisterFactObserver(new ThrowingObserver(first));
            dispatcher.RegisterFactObserver(new ThrowingObserver(second));
            dispatcher.RegisterFactObserver(completed);

            AggregateException failure = Assert.ThrowsAsync<AggregateException>(async () =>
                await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 1)))
            );

            Assert.That(
                failure.Message,
                Does.StartWith("Multiple Fact observers failed after the reduction committed.")
            );
            Assert.That(failure.InnerExceptions, Is.EqualTo(new Exception[] { first, second }));
            Assert.That(completed.Count, Is.EqualTo(1));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(1));
            Assert.That(store.Snapshot.Version, Is.EqualTo(1));
        }

        [Test]
        public void OneObserverFailurePropagatesTheOriginalExceptionAfterCommit()
        {
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("observer-failure-listener"),
                ListenerDefinition,
                Creature,
                default,
                Source,
                0
            );
            InMemoryRulesStore store = CreateStore(binding);
            CapturingFactListener listener = new CapturingFactListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(ListenerDefinition).FactListener(RuleLifecyclePhase.Observation, listener);
            RuleDispatcher dispatcher = CreateDispatcherBuilder(store)
                .UseRuleRegistry(rules.Build())
                .Build();
            InvalidOperationException expected = new InvalidOperationException("single");
            ThrowingObserver observer = new ThrowingObserver(expected);
            dispatcher.RegisterFactObserver(observer);

            InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 1)))
            );

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(1));
            Assert.That(observer.Facts, Has.Count.EqualTo(1));
            Assert.That(listener.Facts, Has.Count.EqualTo(1));
            Assert.That(
                listener.Facts.Single(),
                Is.SameAs(observer.Facts.Single()),
                "Observer failure must not discard the durable Fact before root listeners run."
            );
        }

        private static InMemoryRulesStore CreateStore(params ActiveRuleBinding[] bindings)
        {
            RulesStateSeed seed = new RulesStateSeed().SeedHealth(
                Creature,
                new HealthState(0, 100)
            );
            foreach (ActiveRuleBinding binding in bindings)
                seed.SeedRuleBinding(binding);
            return new InMemoryRulesStore(seed);
        }

        private static RuleDispatcher CreateDispatcher(IRulesStore store) =>
            CreateDispatcherBuilder(store).Build();

        private static RuleDispatcherBuilder CreateDispatcherBuilder(IRulesStore store) =>
            new RuleDispatcherBuilder(store)
                .RegisterHandler<RootOp, int>(new RootHandler())
                .RegisterHandler<RejectionAndNoCommitRootOp, int>(
                    new RejectionAndNoCommitRootHandler()
                )
                .RegisterReducer<ChangeOp, int>(new ChangeReducer(), Source)
                .RegisterReducer<RejectOp, int>(new RejectReducer(), Source)
                .RegisterReducer<NoCommitOp, int>(new NoCommitReducer(), Source);

        private sealed class RootOp : IRuleOp<int>
        {
            public IReadOnlyList<ChangeOp> Changes { get; }

            public RootOp(params ChangeOp[] changes)
            {
                Changes = Array.AsReadOnly(changes);
            }
        }

        private sealed class RootHandler : IOpHandler<RootOp, int>
        {
            public async ValueTask<int> Handle(OpFrame<RootOp> frame, OpHandlerContext context)
            {
                int current = context.Snapshot.Health[Creature].Current;
                foreach (ChangeOp change in frame.Op.Changes)
                {
                    OpResult<int> result = await context.Dispatch(change);
                    current = ((ResolvedOpResult<int>)result).Value;
                }

                return current;
            }
        }

        private sealed class ChangeOp : IRuleOp<int>
        {
            public int Amount { get; }
            public IReadOnlyList<int> FactSequences { get; }

            public ChangeOp(int amount, params int[] factSequences)
            {
                Amount = amount;
                FactSequences = Array.AsReadOnly(factSequences);
            }
        }

        private sealed class ChangeReducer : IOpReducer<ChangeOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<ChangeOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                if (!state.Health.TryGet(Creature, out HealthState previous))
                    throw new InvalidOperationException("Missing Fact-observer health seed.");
                int current = previous.Current + context.Op.Amount;
                state.Health.Set(Creature, new HealthState(current, previous.Maximum));
                foreach (int sequence in context.Op.FactSequences)
                    facts.Stage(new ChangedFact(sequence, previous.Current, current));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class ChangedFact : RuleFact
        {
            public int Sequence { get; }
            public int Previous { get; }
            public int Current { get; }

            public ChangedFact(int sequence, int previous, int current)
            {
                Sequence = sequence;
                Previous = previous;
                Current = current;
            }
        }

        private sealed class RejectionAndNoCommitRootOp : IRuleOp<int> { }

        private sealed class RejectionAndNoCommitRootHandler
            : IOpHandler<RejectionAndNoCommitRootOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<RejectionAndNoCommitRootOp> frame,
                OpHandlerContext context
            )
            {
                OpResult<int> rejected = await context.Dispatch(new RejectOp());
                OpResult<int> noCommit = await context.Dispatch(new NoCommitOp());
                Assert.That(rejected, Is.TypeOf<InvalidOpResult<int>>());
                Assert.That(noCommit, Is.TypeOf<ResolvedOpResult<int>>());
                return ((ResolvedOpResult<int>)noCommit).Value;
            }
        }

        private sealed class RejectOp : IRuleOp<int> { }

        private sealed class RejectReducer : IOpReducer<RejectOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<RejectOp> context,
                RulesStateDraft state,
                FactSink facts
            ) => ReductionResult<int>.Reject("rejected");
        }

        private sealed class NoCommitOp : IRuleOp<int> { }

        private sealed class NoCommitReducer : IOpReducer<NoCommitOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<NoCommitOp> context,
                RulesStateDraft state,
                FactSink facts
            ) => ReductionResult<int>.Accept(0);
        }

        private sealed class FirstDeliveryGateObserver : IFactObserver<ChangedFact>
        {
            private readonly TaskCompletionSource<bool> firstStarted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> firstRelease =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task FirstStarted => firstStarted.Task;
            public List<ChangedFact> Facts { get; } = new List<ChangedFact>();
            public List<int> Sequences { get; } = new List<int>();
            public List<RulesSnapshot> Snapshots { get; } = new List<RulesSnapshot>();

            public void ReleaseFirst() => firstRelease.TrySetResult(true);

            public async ValueTask OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot)
            {
                Facts.Add(fact);
                Sequences.Add(fact.Sequence);
                Snapshots.Add(currentSnapshot);
                if (Sequences.Count != 1)
                    return;

                firstStarted.TrySetResult(true);
                await firstRelease.Task;
            }
        }

        private sealed class CountingObserver : IFactObserver<ChangedFact>
        {
            public int Count { get; private set; }

            public ValueTask OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot)
            {
                Count++;
                return default;
            }
        }

        private sealed class RecordingObserver : IFactObserver<ChangedFact>
        {
            private readonly string name;
            private readonly List<string> deliveries;

            public RecordingObserver(string name, List<string> deliveries)
            {
                this.name = name;
                this.deliveries = deliveries;
            }

            public ValueTask OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot)
            {
                deliveries.Add($"{fact.Sequence}:{name}");
                return default;
            }
        }

        private sealed class MutatingObserver : IFactObserver<ChangedFact>
        {
            private readonly RuleDispatcher dispatcher;
            private readonly RecordingObserver removed;
            private readonly RecordingObserver added;
            private readonly List<string> deliveries;
            private bool changed;

            public MutatingObserver(
                RuleDispatcher dispatcher,
                RecordingObserver removed,
                RecordingObserver added,
                List<string> deliveries
            )
            {
                this.dispatcher = dispatcher;
                this.removed = removed;
                this.added = added;
                this.deliveries = deliveries;
            }

            public ValueTask OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot)
            {
                deliveries.Add($"{fact.Sequence}:mutating");
                if (!changed)
                {
                    changed = true;
                    dispatcher.UnregisterFactObserver(removed);
                    dispatcher.RegisterFactObserver(added);
                }

                return default;
            }
        }

        private sealed class ThrowingObserver : IFactObserver<ChangedFact>
        {
            private readonly Exception exception;

            public ThrowingObserver(Exception exception)
            {
                this.exception = exception;
            }

            public List<ChangedFact> Facts { get; } = new List<ChangedFact>();

            public ValueTask OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot)
            {
                Facts.Add(fact);
                throw exception;
            }
        }

        private sealed class CapturingFactListener : IRuleFactListener<ChangedFact>
        {
            public List<ChangedFact> Facts { get; } = new List<ChangedFact>();

            public ValueTask OnFactCommitted(ChangedFact fact, FactContext context)
            {
                Facts.Add(fact);
                return default;
            }
        }
    }
}
