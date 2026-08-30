using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class FactObserverTests
    {
        private static readonly CreatureId Creature = new("fact-observer-creature");
        private static readonly RuleSource Source = RuleSource.FromSlug("fact-observer-test");
        private static readonly RuleDefinitionId ListenerDefinition = new("fact-observer-listener");

        [Test]
        public async Task ObserverReceivesExactPostCommitSnapshot()
        {
            InMemoryRulesStore store = CreateStore();
            SnapshotObserver observer = new();
            RuleDispatcher dispatcher = CreateBuilder(store).Build();
            dispatcher.RegisterFactObserver(observer);

            OpResult<int> result = await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 1)));

            Assert.That(result, Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(observer.Facts, Has.Count.EqualTo(1));
            Assert.That(observer.Facts.Single().IsStamped, Is.True);
            Assert.That(observer.Facts.Single().Source, Is.EqualTo(Source));
            Assert.That(observer.Snapshots.Single(), Is.SameAs(store.Snapshot));
            Assert.That(observer.Snapshots.Single().Health[Creature].Current, Is.EqualTo(1));
        }

        [Test]
        public async Task MultipleFactsAndObserversUseFactThenRegistrationOrder()
        {
            List<string> deliveries = new();
            RuleDispatcher dispatcher = CreateBuilder(CreateStore()).Build();
            dispatcher.RegisterFactObserver(new RecordingObserver("first", deliveries));
            dispatcher.RegisterFactObserver(new RecordingObserver("second", deliveries));

            await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 10, 11)));

            Assert.That(
                deliveries,
                Is.EqualTo(new[] { "10:first", "10:second", "11:first", "11:second" })
            );
        }

        [Test]
        public async Task RegistrationChangesApplyToLaterNotificationPassesOnly()
        {
            List<string> deliveries = new();
            RuleDispatcher dispatcher = CreateBuilder(CreateStore()).Build();
            RecordingObserver removed = new("removed", deliveries);
            RecordingObserver added = new("added", deliveries);
            dispatcher.RegisterFactObserver(
                new MutatingObserver(dispatcher, removed, added, deliveries)
            );
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
        public async Task ObserverFailuresAreReportedAndDoNotStopObserversOrRulesListeners()
        {
            ActiveRuleBinding binding = new(
                new BindingId("observer-failure-listener"),
                ListenerDefinition,
                Creature,
                default,
                Source,
                0
            );
            InMemoryRulesStore store = CreateStore(binding);
            CapturingFactListener listener = new();
            RecordingExceptionReporter reporter = new();
            RuleRegistryBuilder rules = new();
            rules.Define(ListenerDefinition).FactListener(RuleLifecyclePhase.Observation, listener);
            RuleDispatcher dispatcher = CreateBuilder(store)
                .UseFactObserverExceptionReporter(reporter)
                .UseRuleRegistry(rules.Build())
                .Build();
            InvalidOperationException first = new("first");
            ApplicationException second = new("second");
            SnapshotObserver completed = new();
            dispatcher.RegisterFactObserver(new ThrowingObserver(first));
            dispatcher.RegisterFactObserver(new ThrowingObserver(second));
            dispatcher.RegisterFactObserver(completed);

            OpResult<int> result = await dispatcher.Dispatch(new RootOp(new ChangeOp(1, 1)));

            Assert.That(result, Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(reporter.Exceptions, Is.EqualTo(new Exception[] { first, second }));
            Assert.That(completed.Facts, Has.Count.EqualTo(1));
            Assert.That(listener.Facts.Single(), Is.SameAs(completed.Facts.Single()));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(1));
        }

        [Test]
        public async Task NoCommitReductionsNotifyNobody()
        {
            SnapshotObserver observer = new();
            RuleDispatcher dispatcher = CreateBuilder(CreateStore()).Build();
            dispatcher.RegisterFactObserver(observer);

            OpResult<int> result = await dispatcher.Dispatch(new NoCommitRootOp());

            Assert.That(result, Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(observer.Facts, Is.Empty);
            Assert.That(result.Facts, Is.Empty);
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

        private static RuleDispatcherBuilder CreateBuilder(IRulesStore store) =>
            new RuleDispatcherBuilder(store)
                .RegisterHandler<RootOp, int>(new RootHandler())
                .RegisterHandler<NoCommitRootOp, int>(new NoCommitRootHandler())
                .RegisterReducer<ChangeOp, int>(new ChangeReducer(), Source)
                .RegisterReducer<NoCommitOp, int>(new NoCommitReducer(), Source);

        private sealed class RootOp : IRuleOp<int>
        {
            public RootOp(params ChangeOp[] changes) => Changes = Array.AsReadOnly(changes);

            public IReadOnlyList<ChangeOp> Changes { get; }
        }

        private sealed class RootHandler : IOpHandler<RootOp, int>
        {
            public async ValueTask<int> Handle(OpFrame<RootOp> frame, OpHandlerContext context)
            {
                int current = context.Snapshot.Health[Creature].Current;
                foreach (ChangeOp change in frame.Op.Changes)
                    current = ((ResolvedOpResult<int>)await context.Dispatch(change)).Value;
                return current;
            }
        }

        private sealed class ChangeOp : IRuleOp<int>
        {
            public ChangeOp(int amount, params int[] factSequences)
            {
                Amount = amount;
                FactSequences = Array.AsReadOnly(factSequences);
            }

            public int Amount { get; }
            public IReadOnlyList<int> FactSequences { get; }
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
                    throw new InvalidOperationException("Missing observer test health state.");
                int current = previous.Current + context.Op.Amount;
                state.Health.Set(Creature, new HealthState(current, previous.Maximum));
                foreach (int sequence in context.Op.FactSequences)
                    facts.Stage(new ChangedFact(sequence));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class ChangedFact : RuleFact
        {
            public ChangedFact(int sequence) => Sequence = sequence;

            public int Sequence { get; }
        }

        private sealed class NoCommitRootOp : IRuleOp<int> { }

        private sealed class NoCommitRootHandler : IOpHandler<NoCommitRootOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<NoCommitRootOp> frame,
                OpHandlerContext context
            ) => ((ResolvedOpResult<int>)await context.Dispatch(new NoCommitOp())).Value;
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

        private sealed class SnapshotObserver : IFactObserver<ChangedFact>
        {
            public List<ChangedFact> Facts { get; } = new();
            public List<RulesSnapshot> Snapshots { get; } = new();

            public void OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot)
            {
                Facts.Add(fact);
                Snapshots.Add(currentSnapshot);
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

            public void OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot) =>
                deliveries.Add($"{fact.Sequence}:{name}");
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

            public void OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot)
            {
                deliveries.Add($"{fact.Sequence}:mutating");
                if (changed)
                    return;
                changed = true;
                dispatcher.UnregisterFactObserver(removed);
                dispatcher.RegisterFactObserver(added);
            }
        }

        private sealed class ThrowingObserver : IFactObserver<ChangedFact>
        {
            private readonly Exception exception;

            public ThrowingObserver(Exception exception) => this.exception = exception;

            public void OnFactCommitted(ChangedFact fact, RulesSnapshot currentSnapshot) =>
                throw exception;
        }

        private sealed class RecordingExceptionReporter : IFactObserverExceptionReporter
        {
            public List<Exception> Exceptions { get; } = new();

            public void Report(RuleFact fact, Exception exception) => Exceptions.Add(exception);
        }

        private sealed class CapturingFactListener : IRuleFactListener<ChangedFact>
        {
            public List<ChangedFact> Facts { get; } = new();

            public ValueTask OnFactCommitted(ChangedFact fact, FactContext context)
            {
                Facts.Add(fact);
                return default;
            }
        }
    }
}
