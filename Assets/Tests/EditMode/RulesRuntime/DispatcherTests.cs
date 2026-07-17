using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class DispatcherTests
    {
        private static readonly CreatureId Creature = new CreatureId("dispatcher-creature");
        private static readonly RuleSource Source = RuleSource.FromSlug("dispatcher-test");

        [Test]
        public void ResultContractsRepresentEveryStatusExactly()
        {
            OpResult<int> resolvedFailure = OpResult<int>.Resolved(-1);
            OpResult<int> invalid = OpResult<int>.Invalid("could not begin");
            OpResult<int> interrupted = OpResult<int>.Interrupted();
            OpResult<int> cancelled = OpResult<int>.Cancelled();

            Assert.That(resolvedFailure.Status, Is.EqualTo(OpStatus.Resolved));
            Assert.That(resolvedFailure.Value, Is.EqualTo(-1),
                "A failed domain check is still a legal resolution.");
            Assert.That(invalid.Status, Is.EqualTo(OpStatus.Invalid));
            Assert.That(invalid.InvalidReason, Is.EqualTo("could not begin"));
            Assert.That(interrupted.Status, Is.EqualTo(OpStatus.Interrupted));
            Assert.That(cancelled.Status, Is.EqualTo(OpStatus.Cancelled));
            Assert.Throws<ArgumentException>(() => OpResult<int>.Invalid(" "));
        }

        [Test]
        public async Task TypedRootAndNestedHandlerReducerPathsRefreshSnapshotsAndAggregateFactsOnce()
        {
            InMemoryRulesStore store = CreateStore(10);
            RootHandler handler = new RootHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    store,
                    new SequentialOpIdProvider(40))
                .RegisterHandler<RootOp, int>(handler)
                .RegisterHandler<NestedHandlerOp, int>(
                    new NestedHandler(),
                    InvocationPolicy.NestedOnly)
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new RootOp(2));

            Assert.That(result.Status, Is.EqualTo(OpStatus.Resolved));
            Assert.That(result.Value, Is.EqualTo(14));
            Assert.That(result.Facts, Has.Count.EqualTo(2));
            Assert.That(result.Facts.Select(fact => fact.Id),
                Is.EqualTo(new[] { new FactId(1), new FactId(2) }));
            Assert.That(result.Facts.Distinct().Count(), Is.EqualTo(2));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(14));
            Assert.That(handler.StartVersion, Is.Zero);
            Assert.That(handler.AfterFirstVersion, Is.EqualTo(1));
            Assert.That(handler.AfterNestedVersion, Is.EqualTo(2));

            OpFrame<RootOp> root = dispatcher.Trace.Get<RootOp>(new OpId(40));
            OpFrame<IncrementOp> first = dispatcher.Trace.Get<IncrementOp>(new OpId(41));
            OpFrame<NestedHandlerOp> nested = dispatcher.Trace.Get<NestedHandlerOp>(new OpId(42));
            OpFrame<IncrementOp> grandchild = dispatcher.Trace.Get<IncrementOp>(new OpId(43));

            AssertFrame(root, 40, 40, null, null, InvocationPolicy.ExternalAllowed, 0);
            AssertFrame(first, 41, 40, 40, 40, InvocationPolicy.NestedOnly, 0);
            AssertFrame(nested, 42, 40, 40, 40, InvocationPolicy.NestedOnly, 1);
            AssertFrame(grandchild, 43, 40, 42, 42, InvocationPolicy.NestedOnly, 1);
            Assert.That(dispatcher.Trace.IsDescendantOf(grandchild.Id, root.Id), Is.True);
            Assert.That(dispatcher.Trace.IsCausedBy(grandchild.Id, nested.Id), Is.True);
            Assert.That(dispatcher.Trace.FindNearestAncestor<RootOp>(grandchild.Id), Is.SameAs(root));
            Assert.That(dispatcher.Trace.FindCausingAncestor<NestedHandlerOp>(grandchild.Id), Is.SameAs(nested));
            Assert.That(dispatcher.Trace.IsDescendantOf(root.Id, root.Id), Is.False);
            Assert.That(dispatcher.Trace.IsCausedBy(root.Id, root.Id), Is.False);

            Assert.That(result.Facts.All(fact => fact.RootOpId == root.Id), Is.True);
            Assert.That(result.Facts[0].SourceOpId, Is.EqualTo(first.Id));
            Assert.That(result.Facts[1].SourceOpId, Is.EqualTo(grandchild.Id));
        }

        [Test]
        public async Task ReducerRejectionBecomesExpectedInvalidResultWithoutFacts()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(10))
                .RegisterHandler<RejectRootOp, OpStatus>(new RejectRootHandler())
                .RegisterReducer<RejectOp, int>(new RejectReducer(), Source)
                .Build();

            OpResult<OpStatus> result = await dispatcher.Dispatch(new RejectRootOp());

            Assert.That(result.Status, Is.EqualTo(OpStatus.Resolved));
            Assert.That(result.Value, Is.EqualTo(OpStatus.Invalid));
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public void RegistrationOwnsNestedAuthorizationAndRejectsExternalReducers()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(10))
                .RegisterHandler<NestedHandlerOp, int>(
                    new NestedHandler(),
                    InvocationPolicy.NestedOnly)
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            InvalidOperationException handlerError = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new NestedHandlerOp(1)));
            InvalidOperationException reducerError = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new IncrementOp(1)));

            Assert.That(handlerError.Message, Does.Contain("nested-only"));
            Assert.That(reducerError.Message, Does.Contain("nested-only"));
            Assert.That(dispatcher.Trace.OrderedFrames, Is.Empty);
        }

        [Test]
        public void MissingDuplicateAndImpossibleTypeRegistrationsThrowProgrammerErrors()
        {
            RuleDispatcher missing = new RuleDispatcherBuilder(CreateStore(10)).Build();
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await missing.Dispatch(new RootOp(1)));

            RuleDispatcherBuilder duplicate = new RuleDispatcherBuilder(CreateStore(10))
                .RegisterHandler<RootOp, int>(new RootHandler());
            Assert.Throws<InvalidOperationException>(() => duplicate
                .RegisterHandler<RootOp, int>(new RootHandler()));

            RuleDispatcher mismatch = new RuleDispatcherBuilder(CreateStore(10))
                .RegisterHandler<AmbiguousOp, int>(new AmbiguousIntHandler())
                .Build();
            IRuleOp<string> wrongContract = new AmbiguousOp();
            InvalidOperationException mismatchError = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await mismatch.Dispatch(wrongContract));
            Assert.That(mismatchError.Message, Does.Contain("not String"));
        }

        [Test]
        public void DuplicateIdsBreakTheTraceInvariant()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(10),
                    new ConstantIdProvider())
                .RegisterHandler<RootOp, int>(new RootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterHandler<NestedHandlerOp, int>(new NestedHandler(), InvocationPolicy.NestedOnly)
                .Build();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new RootOp(1)));

            Assert.That(error.Message, Does.Contain("Duplicate operation ID"));
        }

        [Test]
        public void FramesAreEngineOwnedAndReserveOnlyANullActionProfileSlot()
        {
            ConstructorInfo[] constructors = typeof(OpFrame<RootOp>).GetConstructors();
            Assert.That(constructors, Is.Empty);
            Assert.That(typeof(OpFrame<RootOp>).GetProperty(nameof(OpFrame<RootOp>.ActionProfile)), Is.Not.Null);
        }

        [Test]
        public async Task DeterministicIdsTraceAndDiagnosticsNeverUseArbitraryToString()
        {
            string first = await ResolveDiagnostic();
            string second = await ResolveDiagnostic();

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.Contain("[op 7 root] PoisonToStringOp -> Resolved"));
            Assert.That(first, Does.Contain("[op 8 parent=7 cause=7] IncrementOp -> Resolved"));
            Assert.That(first, Does.Contain("[fact 1] HealthChangedFact source=8 root=7"));
            Assert.That(first, Does.Not.Contain("System."));
        }

        [Test]
        public void CrossRootFactsFromABrokenStoreAreRejected()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(new CrossRootStore())
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new SingleIncrementRootOp()));

            Assert.That(error.Message, Does.Contain("across resolution roots"));
        }

        private static async Task<string> ResolveDiagnostic()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(10),
                    new SequentialOpIdProvider(7))
                .RegisterHandler<PoisonToStringOp, int>(new PoisonHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();
            await dispatcher.Dispatch(new PoisonToStringOp());
            return dispatcher.Diagnostics.Compact;
        }

        private static InMemoryRulesStore CreateStore(int health) =>
            new InMemoryRulesStore(new RulesStateSeed()
                .SeedHealth(Creature, new HealthState(health, 100)));

        private static void AssertFrame<TOp>(
            OpFrame<TOp> frame,
            long id,
            long root,
            long? parent,
            long? cause,
            InvocationPolicy policy,
            long startVersion)
            where TOp : IRuleOp
        {
            Assert.That(frame.Id, Is.EqualTo(new OpId(id)));
            Assert.That(frame.RootId, Is.EqualTo(new OpId(root)));
            Assert.That(frame.ParentId, Is.EqualTo(parent.HasValue ? new OpId(parent.Value) : (OpId?)null));
            Assert.That(frame.CauseId, Is.EqualTo(cause.HasValue ? new OpId(cause.Value) : (OpId?)null));
            Assert.That(frame.InvocationPolicy, Is.EqualTo(policy));
            Assert.That(frame.StartSnapshot.Version, Is.EqualTo(startVersion));
            Assert.That(frame.ActionProfile, Is.Null);
        }

        private sealed class RootOp : IRuleOp<int>
        {
            public int Amount { get; }
            public RootOp(int amount) => Amount = amount;
        }

        private sealed class RootHandler : IOpHandler<RootOp, int>
        {
            public long StartVersion { get; private set; }
            public long AfterFirstVersion { get; private set; }
            public long AfterNestedVersion { get; private set; }

            public async ValueTask<int> Handle(OpFrame<RootOp> frame, OpContext context)
            {
                StartVersion = context.Snapshot.Version;
                await context.Dispatch(new IncrementOp(frame.Op.Amount));
                AfterFirstVersion = context.Snapshot.Version;
                OpResult<int> nested = await context.Dispatch(new NestedHandlerOp(frame.Op.Amount));
                AfterNestedVersion = context.Snapshot.Version;
                return nested.Value;
            }
        }

        private sealed class NestedHandlerOp : IRuleOp<int>
        {
            public int Amount { get; }
            public NestedHandlerOp(int amount) => Amount = amount;
        }

        private sealed class NestedHandler : IOpHandler<NestedHandlerOp, int>
        {
            public async ValueTask<int> Handle(OpFrame<NestedHandlerOp> frame, OpContext context)
            {
                OpResult<int> changed = await context.Dispatch(new IncrementOp(frame.Op.Amount));
                return changed.Value;
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
                if (!state.Health.TryGet(Creature, out HealthState previous))
                    throw new InvalidOperationException("Missing dispatcher health seed.");
                int current = previous.Current + context.Op.Amount;
                state.Health.Set(Creature, new HealthState(current, previous.Maximum));
                facts.Stage(new HealthChangedFact(previous.Current, current));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class HealthChangedFact : RuleFact
        {
            public int Previous { get; }
            public int Current { get; }

            public HealthChangedFact(int previous, int current)
            {
                Previous = previous;
                Current = current;
            }
        }

        private sealed class RejectRootOp : IRuleOp<OpStatus>
        {
        }

        private sealed class RejectRootHandler : IOpHandler<RejectRootOp, OpStatus>
        {
            public async ValueTask<OpStatus> Handle(OpFrame<RejectRootOp> frame, OpContext context)
            {
                OpResult<int> rejected = await context.Dispatch(new RejectOp());
                return rejected.Status;
            }
        }

        private sealed class RejectOp : IRuleOp<int>
        {
        }

        private sealed class RejectReducer : IOpReducer<RejectOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<RejectOp> context,
                RulesStateDraft state,
                FactSink facts) =>
                ReductionResult<int>.Reject("expected rule failure");
        }

        private sealed class AmbiguousOp : IRuleOp<int>, IRuleOp<string>
        {
        }

        private sealed class AmbiguousIntHandler : IOpHandler<AmbiguousOp, int>
        {
            public ValueTask<int> Handle(OpFrame<AmbiguousOp> frame, OpContext context) =>
                new ValueTask<int>(1);
        }

        private sealed class ConstantIdProvider : IOpIdProvider
        {
            public OpId Next() => new OpId(1);
        }

        private sealed class PoisonToStringOp : IRuleOp<int>
        {
            public override string ToString() =>
                throw new InvalidOperationException("Diagnostics called arbitrary ToString().");
        }

        private sealed class PoisonHandler : IOpHandler<PoisonToStringOp, int>
        {
            public async ValueTask<int> Handle(OpFrame<PoisonToStringOp> frame, OpContext context)
            {
                OpResult<int> changed = await context.Dispatch(new IncrementOp(1));
                return changed.Value;
            }
        }

        private sealed class SingleIncrementRootOp : IRuleOp<int>
        {
        }

        private sealed class SingleIncrementRootHandler : IOpHandler<SingleIncrementRootOp, int>
        {
            public async ValueTask<int> Handle(OpFrame<SingleIncrementRootOp> frame, OpContext context)
            {
                OpResult<int> changed = await context.Dispatch(new IncrementOp(1));
                return changed.Value;
            }
        }

        private sealed class CrossRootStore : IRulesStore
        {
            private readonly InMemoryRulesStore inner = CreateStore(10);

            public RulesSnapshot Snapshot => inner.Snapshot;

            public ReductionResult<TResult> Reduce<TOp, TResult>(
                ReductionContext<TOp> context,
                IOpReducer<TOp, TResult> reducer)
                where TOp : IRuleOp<TResult>
            {
                ReductionResult<TResult> reduced = inner.Reduce(context, reducer);
                foreach (RuleFact fact in reduced.Facts)
                {
                    typeof(RuleFact)
                        .GetField("<RootOpId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(fact, new OpId(context.RootOpId.Value + 100));
                }
                return reduced;
            }
        }
    }
}
