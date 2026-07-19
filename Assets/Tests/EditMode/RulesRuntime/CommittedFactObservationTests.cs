using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies the immutable post-commit observation seam used by presentation adapters.
    /// </summary>
    public sealed class CommittedFactObservationTests
    {
        private static readonly CreatureId Creature = new CreatureId("observed-creature");
        private static readonly RuleDefinitionId Definition =
            new RuleDefinitionId("observation-rule");
        private static readonly RuleSource Source = RuleSource.FromSlug("observation-test");

        [Test]
        public async Task CommittedFactPublishesExactReductionSnapshotsBeforeRuleListeners()
        {
            List<string> calls = new List<string>();
            SnapshotListener listener = new SnapshotListener(calls);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(Definition).FactListener(
                RuleLifecyclePhase.Observation,
                listener);
            RuleDispatcher dispatcher = CreateDispatcher(
                CreateSeed(includeBinding: true),
                rules.Build());
            List<CommittedRuleFact> observed = new List<CommittedRuleFact>();
            dispatcher.FactCommitted += commit =>
            {
                calls.Add("presentation");
                observed.Add(commit);
            };

            OpResult<int> result = await dispatcher.Dispatch(new AdjustRootOp(-4));

            Assert.That(result, Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(observed, Has.Count.EqualTo(1));
            Assert.That(observed[0].Fact, Is.TypeOf<HealthAdjustedFact>());
            Assert.That(observed[0].PreviousSnapshot.Version, Is.Zero);
            Assert.That(observed[0].CurrentSnapshot.Version, Is.EqualTo(1));
            Assert.That(observed[0].PreviousSnapshot.Health[Creature].Current, Is.EqualTo(12));
            Assert.That(observed[0].CurrentSnapshot.Health[Creature].Current, Is.EqualTo(8));
            Assert.That(calls, Is.EqualTo(new[] { "presentation", "listener" }));
            Assert.That(listener.ObservedHealth, Is.EqualTo(8));
        }

        [Test]
        public async Task ResolvedRootWithoutFactsPublishesNothing()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    new InMemoryRulesStore(CreateSeed(includeBinding: false)))
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .Build();
            int observations = 0;
            dispatcher.FactCommitted += _ => observations++;

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(7));

            Assert.That(result, Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(observations, Is.Zero);
            Assert.That(dispatcher.Snapshot.Version, Is.Zero);
        }

        [Test]
        public async Task ReducerFactUsesSnapshotAfterMiddlewarePreludeCommit()
        {
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(Definition).Middleware(
                RuleLifecyclePhase.Prevention,
                new CommitBeforeObservedReducerMiddleware());
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    new InMemoryRulesStore(CreateSeed(includeBinding: true)))
                .RegisterHandler<ObservedRootOp, int>(new ObservedRootHandler())
                .RegisterReducer<ObservedAdjustHealthOp, int>(
                    new ObservedAdjustHealthReducer(),
                    Source)
                .RegisterReducer<AdjustHealthOp, int>(
                    new AdjustHealthReducer(),
                    Source)
                .UseRuleRegistry(rules.Build())
                .Build();
            List<CommittedRuleFact> observed = new List<CommittedRuleFact>();
            dispatcher.FactCommitted += observed.Add;

            OpResult<int> result = await dispatcher.Dispatch(new ObservedRootOp(-4));

            Assert.That(result, Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(observed, Has.Count.EqualTo(2));
            Assert.That(observed[0].PreviousSnapshot.Version, Is.Zero);
            Assert.That(observed[0].CurrentSnapshot.Version, Is.EqualTo(1));
            Assert.That(observed[1].PreviousSnapshot.Version, Is.EqualTo(1));
            Assert.That(observed[1].CurrentSnapshot.Version, Is.EqualTo(2));
            Assert.That(observed[1].PreviousSnapshot.Health[Creature].Current, Is.EqualTo(11));
            Assert.That(observed[1].CurrentSnapshot.Health[Creature].Current, Is.EqualTo(7));
        }

        [Test]
        public async Task InvalidRootPublishesNoPresentationEvenWhenMiddlewareCommittedEarlierFact()
        {
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(Definition).Middleware(
                RuleLifecyclePhase.Prevention,
                new CommitThenInvalidateMiddleware());
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    new InMemoryRulesStore(CreateSeed(includeBinding: true)))
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .RegisterReducer<AdjustHealthOp, int>(
                    new AdjustHealthReducer(),
                    Source)
                .UseRuleRegistry(rules.Build())
                .Build();
            int observations = 0;
            dispatcher.FactCommitted += _ => observations++;

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(1));

            Assert.That(result, Is.TypeOf<InvalidOpResult<int>>());
            Assert.That(result.Facts, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(11));
            Assert.That(observations, Is.Zero);
        }

        [Test]
        public void ThrowingObserverDoesNotSuppressOtherObserversOrRuleListeners()
        {
            List<string> calls = new List<string>();
            SnapshotListener listener = new SnapshotListener(calls);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(Definition).FactListener(
                RuleLifecyclePhase.Observation,
                listener);
            RuleDispatcher dispatcher = CreateDispatcher(
                CreateSeed(includeBinding: true),
                rules.Build());
            dispatcher.FactCommitted += _ =>
            {
                calls.Add("throwing-observer");
                throw new ApplicationException("Expected presentation failure.");
            };
            dispatcher.FactCommitted += _ => calls.Add("recording-observer");

            InvalidOperationException failure =
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await dispatcher.Dispatch(new AdjustRootOp(-1)));

            Assert.That(failure.Message,
                Does.StartWith("A committed-Fact presentation observer failed."));
            Assert.That(failure.InnerException, Is.TypeOf<ApplicationException>());
            Assert.That(calls, Is.EqualTo(new[]
            {
                "throwing-observer",
                "recording-observer",
                "listener"
            }));
            Assert.That(listener.ObservedHealth, Is.EqualTo(11));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(11));
        }

        private static RuleDispatcher CreateDispatcher(
            RulesStateSeed seed,
            RuleRegistry registry)
        {
            return new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
                .RegisterHandler<AdjustRootOp, int>(new AdjustRootHandler())
                .RegisterReducer<AdjustHealthOp, int>(new AdjustHealthReducer(), Source)
                .UseRuleRegistry(registry)
                .Build();
        }

        private static RulesStateSeed CreateSeed(bool includeBinding)
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedHealth(Creature, new HealthState(12, 12));
            if (includeBinding)
            {
                seed.SeedRuleBinding(new ActiveRuleBinding(
                    new BindingId("observation-binding"),
                    Definition,
                    Creature,
                    default,
                    Source,
                    0));
            }
            return seed;
        }

        private sealed class AdjustRootOp : IRuleOp<int>
        {
            public AdjustRootOp(int delta) => Delta = delta;
            public int Delta { get; }
        }

        private sealed class AdjustHealthOp : IRuleOp<int>
        {
            public AdjustHealthOp(int delta) => Delta = delta;
            public int Delta { get; }
        }

        private sealed class ObservedRootOp : IRuleOp<int>
        {
            public ObservedRootOp(int delta) => Delta = delta;
            public int Delta { get; }
        }

        private sealed class ObservedAdjustHealthOp : IRuleOp<int>
        {
            public ObservedAdjustHealthOp(int delta) => Delta = delta;
            public int Delta { get; }
        }

        private sealed class ValueOp : IRuleOp<int>
        {
            public ValueOp(int value) => Value = value;
            public int Value { get; }
        }

        private sealed class HealthAdjustedFact : RuleFact
        {
            public HealthAdjustedFact(int previous, int current)
            {
                Previous = previous;
                Current = current;
            }

            public int Previous { get; }
            public int Current { get; }
        }

        private sealed class AdjustRootHandler : IOpHandler<AdjustRootOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<AdjustRootOp> frame,
                OpHandlerContext context)
            {
                OpResult<int> result = await context.Dispatch(
                    new AdjustHealthOp(frame.Op.Delta));
                return ((ResolvedOpResult<int>)result).Value;
            }
        }

        private sealed class ValueHandler : IOpHandler<ValueOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<ValueOp> frame,
                OpHandlerContext context) => new ValueTask<int>(frame.Op.Value);
        }

        private sealed class ObservedRootHandler : IOpHandler<ObservedRootOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<ObservedRootOp> frame,
                OpHandlerContext context)
            {
                OpResult<int> result = await context.Dispatch(
                    new ObservedAdjustHealthOp(frame.Op.Delta));
                return ((ResolvedOpResult<int>)result).Value;
            }
        }

        private sealed class AdjustHealthReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                if (!state.Health.TryGet(Creature, out HealthState previous))
                    throw new InvalidOperationException("The test creature health was not seeded.");
                int current = previous.Current + context.Op.Delta;
                state.Health.Set(
                    Creature,
                    new HealthState(current, previous.Maximum, previous.Temporary));
                facts.Stage(new HealthAdjustedFact(previous.Current, current));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class ObservedAdjustHealthReducer :
            IOpReducer<ObservedAdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<ObservedAdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                if (!state.Health.TryGet(Creature, out HealthState previous))
                    throw new InvalidOperationException("The test creature health was not seeded.");
                int current = previous.Current + context.Op.Delta;
                state.Health.Set(
                    Creature,
                    new HealthState(current, previous.Maximum, previous.Temporary));
                facts.Stage(new HealthAdjustedFact(previous.Current, current));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class CommitBeforeObservedReducerMiddleware :
            IOpMiddleware<ObservedAdjustHealthOp, int>
        {
            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ObservedAdjustHealthOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next)
            {
                await context.Dispatch(new AdjustHealthOp(-1));
                return await next();
            }
        }

        private sealed class CommitThenInvalidateMiddleware :
            IOpMiddleware<ValueOp, int>
        {
            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next)
            {
                await context.Dispatch(new AdjustHealthOp(-1));
                return OpResult<int>.Invalid("The root is intentionally invalid.");
            }
        }

        private sealed class SnapshotListener : IFactListener<HealthAdjustedFact>
        {
            private readonly List<string> calls;

            public SnapshotListener(List<string> calls) => this.calls = calls;

            public int ObservedHealth { get; private set; }

            public ValueTask OnFactCommitted(
                HealthAdjustedFact fact,
                FactContext context)
            {
                calls.Add("listener");
                ObservedHealth = context.Snapshot.Health[Creature].Current;
                return default;
            }
        }
    }
}
