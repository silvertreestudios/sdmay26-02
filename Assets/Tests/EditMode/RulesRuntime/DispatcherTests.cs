using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies dispatcher ownership, trace, result, cleanup, and failure invariants.
    /// </summary>
    public sealed class DispatcherTests
    {
        private static readonly CreatureId Creature = new CreatureId("dispatcher-creature");
        private static readonly RuleSource Source = RuleSource.FromSlug("dispatcher-test");

        [Test]
        public void ResultContractsUseFourStructuralCasesAndPreserveFacts()
        {
            ResolvedOpResult<int> resolvedFailure = OpResult<int>.Resolved(-1);
            InvalidOpResult<int> invalid = OpResult<int>.Invalid("could not begin");
            InterruptedOpResult<int> interrupted = OpResult<int>.Interrupted();
            CancelledOpResult<int> cancelled = OpResult<int>.Cancelled();

            Assert.That(resolvedFailure.Status, Is.EqualTo(OpStatus.Resolved));
            Assert.That(
                resolvedFailure.Value,
                Is.EqualTo(-1),
                "A failed domain check is still a legal resolution."
            );
            Assert.That(invalid.Status, Is.EqualTo(OpStatus.Invalid));
            Assert.That(invalid.Reason, Is.EqualTo("could not begin"));
            Assert.That(interrupted.Status, Is.EqualTo(OpStatus.Interrupted));
            Assert.That(cancelled.Status, Is.EqualTo(OpStatus.Cancelled));
            Assert.That(typeof(OpResult<int>).IsAbstract, Is.True);
            Assert.That(typeof(ResolvedOpResult<int>).IsSealed, Is.True);
            Assert.That(typeof(InvalidOpResult<int>).IsSealed, Is.True);
            Assert.That(typeof(InterruptedOpResult<int>).IsSealed, Is.True);
            Assert.That(typeof(CancelledOpResult<int>).IsSealed, Is.True);
            Assert.That(typeof(OpResult<int>).GetProperty("Value"), Is.Null);
            Assert.That(typeof(OpResult<int>).GetProperty("Reason"), Is.Null);
            Assert.Throws<ArgumentException>(() => OpResult<int>.Invalid(" "));

            IReadOnlyList<RuleFact> facts = Array.AsReadOnly(
                new RuleFact[] { new HealthChangedFact(1, 2) }
            );
            OpResult<int>[] completed =
            {
                resolvedFailure.WithFacts(facts),
                invalid.WithFacts(facts),
                interrupted.WithFacts(facts),
                cancelled.WithFacts(facts),
            };

            Assert.That(completed[0], Is.TypeOf<ResolvedOpResult<int>>());
            Assert.That(completed[1], Is.TypeOf<InvalidOpResult<int>>());
            Assert.That(completed[2], Is.TypeOf<InterruptedOpResult<int>>());
            Assert.That(completed[3], Is.TypeOf<CancelledOpResult<int>>());
            Assert.That(completed.All(result => ReferenceEquals(result.Facts, facts)), Is.True);
        }

        [Test]
        public void CompositeLifetimeCleansUpInReverseOrderOnceAndRejectsLateResources()
        {
            List<string> cleanupOrder = new List<string>();
            TrackingDisposable first = new TrackingDisposable("first", cleanupOrder);
            TrackingDisposable second = new TrackingDisposable("second", cleanupOrder);
            TrackingDisposable late = new TrackingDisposable("late", cleanupOrder);
            CompositeLifetime lifetime = new CompositeLifetime();

            Assert.That(lifetime.Add(first), Is.SameAs(first));
            lifetime.Add(second);
            lifetime.Dispose();
            lifetime.Dispose();

            Assert.That(cleanupOrder, Is.EqualTo(new[] { "second", "first" }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => lifetime.Add(late));
            Assert.That(late.DisposeCount, Is.Zero);
        }

        [Test]
        public async Task SettlementRegistrationTokensUnregisterEachObserverExactlyOnce()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(10))
                .RegisterHandler<SettlementTokenOp, int>(new SettlementTokenHandler())
                .Build();
            RecordingSettlementObserver observer = new RecordingSettlementObserver();
            IDisposable rootRegistration = dispatcher.RegisterRootSettlementObserver(observer);
            IDisposable treeRegistration = dispatcher.RegisterCausalTreeSettlementObserver(
                observer
            );

            await dispatcher.Dispatch(new SettlementTokenOp());
            treeRegistration.Dispose();
            treeRegistration.Dispose();
            rootRegistration.Dispose();
            rootRegistration.Dispose();
            await dispatcher.Dispatch(new SettlementTokenOp());

            Assert.That(observer.RootSettlements, Is.EqualTo(1));
            Assert.That(observer.TreeSettlements, Is.EqualTo(1));
            Assert.That(dispatcher.UnregisterRootSettlementObserver(observer), Is.False);
            Assert.That(dispatcher.UnregisterCausalTreeSettlementObserver(observer), Is.False);
        }

        [Test]
        public async Task TypedRootAndNestedHandlerReducerPathsRefreshSnapshotsAndAggregateFactsOnce()
        {
            InMemoryRulesStore store = CreateStore(10);
            RootHandler handler = new RootHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                store,
                new SequentialOpIdProvider(40)
            )
                .RegisterHandler<RootOp, int>(handler)
                .RegisterHandler<NestedHandlerOp, int>(
                    new NestedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new RootOp(2));

            ResolvedOpResult<int> resolved = RequireResolved(result);
            Assert.That(resolved.Status, Is.EqualTo(OpStatus.Resolved));
            Assert.That(resolved.Value, Is.EqualTo(14));
            Assert.That(result.Facts, Has.Count.EqualTo(2));
            Assert.That(
                result.Facts.Select(fact => fact.Id),
                Is.EqualTo(new[] { new FactId(1), new FactId(2) })
            );
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
            Assert.That(
                dispatcher.Trace.FindNearestAncestor<RootOp>(grandchild.Id),
                Is.SameAs(root)
            );
            Assert.That(
                dispatcher.Trace.FindCausingAncestor<NestedHandlerOp>(grandchild.Id),
                Is.SameAs(nested)
            );
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

            ResolvedOpResult<OpStatus> resolved = RequireResolved(result);
            Assert.That(resolved.Status, Is.EqualTo(OpStatus.Resolved));
            Assert.That(resolved.Value, Is.EqualTo(OpStatus.Invalid));
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public void RegistrationOwnsNestedAuthorizationAndRejectsExternalReducers()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(10))
                .RegisterHandler<NestedHandlerOp, int>(
                    new NestedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            InvalidOperationException handlerError = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new NestedHandlerOp(1))
            );
            InvalidOperationException reducerError = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new IncrementOp(1))
            );

            Assert.That(handlerError.Message, Does.Contain("nested-only"));
            Assert.That(reducerError.Message, Does.Contain("nested-only"));
            Assert.That(dispatcher.Trace.OrderedFrames, Is.Empty);
        }

        [Test]
        public void MissingDuplicateAndImpossibleTypeRegistrationsThrowProgrammerErrors()
        {
            RuleDispatcher missing = new RuleDispatcherBuilder(CreateStore(10)).Build();
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await missing.Dispatch(new RootOp(1))
            );

            RuleDispatcherBuilder duplicate = new RuleDispatcherBuilder(
                CreateStore(10)
            ).RegisterHandler<RootOp, int>(new RootHandler());
            Assert.Throws<InvalidOperationException>(() =>
                duplicate.RegisterHandler<RootOp, int>(new RootHandler())
            );

            RuleDispatcher mismatch = new RuleDispatcherBuilder(CreateStore(10))
                .RegisterHandler<AmbiguousOp, int>(new AmbiguousIntHandler())
                .Build();
            IRuleOp<string> wrongContract = new AmbiguousOp();
            InvalidOperationException mismatchError = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await mismatch.Dispatch(wrongContract)
            );
            Assert.That(mismatchError.Message, Does.Contain("not String"));
        }

        [Test]
        public void DuplicateIdsBreakTheTraceInvariant()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(10),
                new ConstantIdProvider()
            )
                .RegisterHandler<RootOp, int>(new RootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterHandler<NestedHandlerOp, int>(
                    new NestedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .Build();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new RootOp(1))
            );

            Assert.That(error.Message, Does.Contain("Duplicate operation ID"));
        }

        [Test]
        public void FramesAreEngineOwnedAndRepresentNonActionsWithoutANullProfile()
        {
            ConstructorInfo[] constructors = typeof(OpFrame<RootOp>).GetConstructors();
            Assert.That(constructors, Is.Empty);
            Assert.That(
                typeof(OpFrame<RootOp>).GetProperty(nameof(OpFrame<RootOp>.ActionProfile)),
                Is.Not.Null
            );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(10))
                .RegisterHandler<RootOp, int>(new RootHandler())
                .RegisterHandler<NestedHandlerOp, int>(
                    new NestedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            Assert.DoesNotThrowAsync(async () => await dispatcher.Dispatch(new RootOp(1)));
            OpFrame<RootOp> frame = dispatcher.Trace.Get<RootOp>(new OpId(1));
            Assert.That(frame.IsAction, Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = frame.ActionProfile);
            Assert.Throws<InvalidOperationException>(() => _ = frame.ActionInfo);
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

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new SingleIncrementRootOp())
            );

            Assert.That(error.Message, Does.Contain("across resolution roots"));
        }

        [Test]
        public async Task StaleContextIsRejectedBeforeAllocatingOrMutatingWhileAnotherRootIsSuspended()
        {
            InMemoryRulesStore store = CreateStore(10);
            CountingOpIdProvider ids = new CountingOpIdProvider(500);
            ContextCapturingHandler capturing = new ContextCapturingHandler();
            SuspendedRootHandler suspended = new SuspendedRootHandler();
            CountingIncrementReducer reducer = new CountingIncrementReducer();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store, ids)
                .RegisterHandler<CaptureContextOp, int>(capturing)
                .RegisterHandler<SuspendedRootOp, int>(suspended)
                .RegisterReducer<IncrementOp, int>(reducer, Source)
                .Build();

            await dispatcher.Dispatch(new CaptureContextOp());
            Task<OpResult<int>> activeRoot = dispatcher.Dispatch(new SuspendedRootOp()).AsTask();
            await suspended.Started;

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await capturing.Context.Dispatch(new IncrementOp(100))
            );

            Assert.That(error.Message, Does.Contain("not actively executing"));
            Assert.That(
                ids.Calls,
                Is.EqualTo(2),
                "A rejected stale context must not allocate a frame ID."
            );
            Assert.That(
                reducer.Calls,
                Is.Zero,
                "A rejected stale context must not invoke its reducer."
            );
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(10));
            Assert.That(dispatcher.Trace.OrderedFrames.Count, Is.EqualTo(2));
            Assert.That(
                dispatcher.Trace.OrderedFrames.All(frame =>
                    frame.ParentId == null && frame.RootId == frame.Id
                ),
                Is.True
            );

            suspended.Release();
            OpResult<int> completedRoot = await activeRoot;

            Assert.That(completedRoot.Facts, Is.Empty);
            Assert.That(ids.Calls, Is.EqualTo(2));
            Assert.That(reducer.Calls, Is.Zero);
        }

        [Test]
        public async Task OverlappingSiblingIsRejectedThenSequentialSiblingsAggregateOnlyTheirOwnFacts()
        {
            InMemoryRulesStore store = CreateStore(10);
            SuspendedNestedHandler suspended = new SuspendedNestedHandler();
            OverlappingRootHandler handler = new OverlappingRootHandler(suspended);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                store,
                new SequentialOpIdProvider(600)
            )
                .RegisterHandler<OverlappingRootOp, int>(handler)
                .RegisterHandler<SuspendedNestedOp, int>(suspended, InvocationPolicy.NestedOnly)
                .RegisterHandler<NestedHandlerOp, int>(
                    new NestedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new OverlappingRootOp());

            Assert.That(handler.OverlapError, Is.Not.Null);
            Assert.That(handler.OverlapError.Message, Does.Contain("overlapping child dispatch"));
            Assert.That(RequireResolved(result).Value, Is.EqualTo(13));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(13));
            Assert.That(result.Facts, Has.Count.EqualTo(2));
            Assert.That(result.Facts.Distinct().Count(), Is.EqualTo(2));
            Assert.That(
                result.Facts.Select(fact => fact.Id),
                Is.EqualTo(new[] { new FactId(1), new FactId(2) })
            );

            Assert.That(handler.FirstChildResult.Facts, Has.Count.EqualTo(1));
            Assert.That(handler.SequentialSiblingResult.Facts, Has.Count.EqualTo(1));
            Assert.That(handler.FirstChildResult.Facts[0], Is.SameAs(result.Facts[0]));
            Assert.That(handler.SequentialSiblingResult.Facts[0], Is.SameAs(result.Facts[1]));
            Assert.That(handler.FirstChildResult.Facts[0].SourceOpId, Is.EqualTo(new OpId(602)));
            Assert.That(
                handler.SequentialSiblingResult.Facts[0].SourceOpId,
                Is.EqualTo(new OpId(604))
            );
            Assert.That(result.Facts.All(fact => fact.RootOpId == new OpId(600)), Is.True);
            Assert.That(
                dispatcher.Trace.OrderedFrames.Count,
                Is.EqualTo(5),
                "The rejected overlapping sibling must not allocate or trace a frame."
            );
            Assert.That(
                dispatcher.Trace.Get<SuspendedNestedOp>(new OpId(601)).ParentId,
                Is.EqualTo(new OpId(600))
            );
            Assert.That(
                dispatcher.Trace.Get<NestedHandlerOp>(new OpId(603)).ParentId,
                Is.EqualTo(new OpId(600))
            );
        }

        [Test]
        public async Task ExceptionalChildReleasesFrameAndReservationForLaterDispatchesAndRoots()
        {
            InMemoryRulesStore store = CreateStore(10);
            RecoveringRootHandler handler = new RecoveringRootHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                store,
                new SequentialOpIdProvider(700)
            )
                .RegisterHandler<RecoveringRootOp, int>(handler)
                .RegisterHandler<ThrowingNestedOp, int>(
                    new ThrowingNestedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            OpResult<int> recovered = await dispatcher.Dispatch(new RecoveringRootOp());
            OpResult<int> laterRoot = await dispatcher.Dispatch(new SingleIncrementRootOp());

            Assert.That(handler.ChildError, Is.Not.Null);
            Assert.That(handler.ChildError.Message, Does.Contain("expected nested failure"));
            Assert.That(RequireResolved(recovered).Value, Is.EqualTo(11));
            Assert.That(recovered.Facts, Has.Count.EqualTo(1));
            Assert.That(recovered.Facts[0].SourceOpId, Is.EqualTo(new OpId(702)));
            Assert.That(recovered.Facts[0].RootOpId, Is.EqualTo(new OpId(700)));
            Assert.That(RequireResolved(laterRoot).Value, Is.EqualTo(12));
            Assert.That(laterRoot.Facts, Has.Count.EqualTo(1));
            Assert.That(laterRoot.Facts[0].SourceOpId, Is.EqualTo(new OpId(704)));
            Assert.That(laterRoot.Facts[0].RootOpId, Is.EqualTo(new OpId(703)));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(12));
        }

        /// <summary>
        /// Verifies concurrent external roots queue and execute without overlapping ownership.
        /// </summary>
        [Test]
        [Timeout(10000)]
        public async Task ConcurrentRootsSerializeWithoutOverlappingOwnership()
        {
            InMemoryRulesStore store = CreateStore(10);
            CountingOpIdProvider ids = new CountingOpIdProvider(800);
            RacingRootHandler handler = new RacingRootHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store, ids)
                .RegisterHandler<RacingRootOp, int>(handler)
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            using (CountdownEvent ready = new CountdownEvent(2))
            using (ManualResetEventSlim start = new ManualResetEventSlim())
            {
                Task<OpResult<int>> first = RaceRoot(dispatcher, ready, start);
                Task<OpResult<int>> second = RaceRoot(dispatcher, ready, start);
                Assert.That(ready.Wait(TimeSpan.FromSeconds(5)), Is.True);
                start.Set();
                await handler.Started;

                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(handler.Calls, Is.EqualTo(1));
                Assert.That(
                    ids.Calls,
                    Is.EqualTo(1),
                    "The queued caller must not allocate a root ID."
                );
                Assert.That(
                    dispatcher.Trace.OrderedFrames.Select(frame => frame.Id),
                    Is.EqualTo(new[] { new OpId(800) })
                );

                handler.Release();
                OpResult<int>[] results = await Task.WhenAll(first, second);
                Assert.That(results.All(result => result is ResolvedOpResult<int>), Is.True);
                Assert.That(handler.Calls, Is.EqualTo(2));
                Assert.That(handler.MaximumConcurrency, Is.EqualTo(1));
                Assert.That(ids.Calls, Is.EqualTo(2));
            }

            OpResult<int> laterRoot = await dispatcher.Dispatch(new SingleIncrementRootOp());

            Assert.That(RequireResolved(laterRoot).Value, Is.EqualTo(11));
            Assert.That(ids.Calls, Is.EqualTo(4));
            Assert.That(
                dispatcher.Trace.OrderedFrames.Select(frame => frame.Id),
                Is.EqualTo(new[] { new OpId(800), new OpId(801), new OpId(802), new OpId(803) })
            );
            Assert.That(
                dispatcher.Trace.OrderedFrames.Select(frame => frame.Id).Distinct().Count(),
                Is.EqualTo(4)
            );
            Assert.That(laterRoot.Facts.Single().RootOpId, Is.EqualTo(new OpId(802)));
        }

        /// <summary>
        /// Verifies a callback cannot deadlock its active root by reentering the public dispatch API.
        /// </summary>
        [Test]
        [Timeout(10000)]
        public void ActiveResolutionRejectsReentrantPublicRootDispatch()
        {
            TaskCompletionSource<RuleDispatcher> dispatcherReference =
                new TaskCompletionSource<RuleDispatcher>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(10),
                new SequentialOpIdProvider(825)
            )
                .RegisterHandler<ReentrantRootOp, int>(
                    new ReentrantRootHandler(dispatcherReference.Task)
                )
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();
            dispatcherReference.SetResult(dispatcher);

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new ReentrantRootOp())
            );

            Assert.That(error.Message, Does.Contain("public root Dispatch API"));
            Assert.That(
                dispatcher.Trace.OrderedFrames.Select(frame => frame.Id),
                Is.EqualTo(new[] { new OpId(825) })
            );
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(10));
        }

        /// <summary>
        /// Verifies a delayed continuation may start a new root after its originating root releases ownership.
        /// </summary>
        [Test]
        [Timeout(10000)]
        public async Task CompletedResolutionExpiresCapturedReentrancyMarker()
        {
            TaskCompletionSource<RuleDispatcher> dispatcherReference =
                new TaskCompletionSource<RuleDispatcher>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
            TaskCompletionSource<bool> releaseDelayedRoot = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<OpResult<int>> delayedResult = new TaskCompletionSource<
                OpResult<int>
            >(TaskCreationOptions.RunContinuationsAsynchronously);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(10),
                new SequentialOpIdProvider(850)
            )
                .RegisterHandler<DelayedRootOp, int>(
                    new DelayedRootHandler(
                        dispatcherReference.Task,
                        releaseDelayedRoot.Task,
                        delayedResult
                    )
                )
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();
            dispatcherReference.SetResult(dispatcher);

            await dispatcher.Dispatch(new DelayedRootOp());
            releaseDelayedRoot.SetResult(true);
            OpResult<int> laterRoot = await delayedResult.Task;

            Assert.That(laterRoot.Status, Is.EqualTo(OpStatus.Resolved));
            Assert.That(RequireResolved(laterRoot).Value, Is.EqualTo(11));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(11));
            Assert.That(
                dispatcher.Trace.OrderedFrames.Select(frame => frame.Id),
                Is.EqualTo(new[] { new OpId(850), new OpId(851), new OpId(852) })
            );
        }

        private static Task<OpResult<int>> RaceRoot(
            RuleDispatcher dispatcher,
            CountdownEvent ready,
            ManualResetEventSlim start
        )
        {
            TaskCompletionSource<OpResult<int>> completion = new TaskCompletionSource<
                OpResult<int>
            >(TaskCreationOptions.RunContinuationsAsynchronously);
            Thread thread = new Thread(() =>
            {
                ready.Signal();
                start.Wait();
                try
                {
                    completion.TrySetResult(
                        dispatcher.Dispatch(new RacingRootOp()).AsTask().GetAwaiter().GetResult()
                    );
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return completion.Task;
        }

        private static Task<DispatchAttempt> DispatchOnBackgroundThread(
            Func<Task<OpResult<int>>> dispatch
        )
        {
            TaskCompletionSource<DispatchAttempt> completion =
                new TaskCompletionSource<DispatchAttempt>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
            Thread thread = new Thread(() =>
            {
                try
                {
                    completion.TrySetResult(
                        new DispatchAttempt(dispatch().GetAwaiter().GetResult(), null)
                    );
                }
                catch (InvalidOperationException error)
                {
                    completion.TrySetResult(new DispatchAttempt(null, error));
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return completion.Task;
        }

        [Test]
        public async Task IgnoredSynchronouslyCompletedChildIsRejected()
        {
            InMemoryRulesStore store = CreateStore(10);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                store,
                new SequentialOpIdProvider(850)
            )
                .RegisterHandler<IgnoredSynchronousChildRootOp, int>(
                    new IgnoredSynchronousChildRootHandler()
                )
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new IgnoredSynchronousChildRootOp())
            );
            OpResult<int> recovered = await dispatcher.Dispatch(new SingleIncrementRootOp());

            Assert.That(
                error.Message,
                Is.EqualTo("Operation 850 returned before awaiting its active child dispatch.")
            );
            Assert.That(
                store.Snapshot.Health[Creature].Current,
                Is.EqualTo(12),
                "The ignored child commits before rejection and the later root must still run."
            );
            Assert.That(RequireResolved(recovered).Value, Is.EqualTo(12));
        }

        [Test]
        public async Task IgnoredSynchronouslyFailedChildPropagatesItsFailure()
        {
            InMemoryRulesStore store = CreateStore(10);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<IgnoredFailingChildRootOp, int>(
                    new IgnoredFailingChildRootHandler()
                )
                .RegisterHandler<SynchronouslyFailingNestedOp, int>(
                    new SynchronouslyFailingNestedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            ApplicationException error = Assert.ThrowsAsync<ApplicationException>(async () =>
                await dispatcher.Dispatch(new IgnoredFailingChildRootOp())
            );
            OpResult<int> recovered = await dispatcher.Dispatch(new SingleIncrementRootOp());

            Assert.That(error.Message, Is.EqualTo("synchronous child failure"));
            Assert.That(
                RequireResolved(recovered).Value,
                Is.EqualTo(11),
                "A propagated ignored failure must still release root ownership."
            );
        }

        /// <summary>
        /// Verifies an unawaited child retains root ownership until cleanup, then releases a queued root.
        /// </summary>
        [Test]
        public async Task UnawaitedSuspendedChildKeepsRootOwnedUntilItSettlesThenFailsClearly()
        {
            InMemoryRulesStore store = CreateStore(10);
            SuspendedNestedHandler child = new SuspendedNestedHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                store,
                new SequentialOpIdProvider(900)
            )
                .RegisterHandler<UnawaitedChildRootOp, int>(new UnawaitedChildRootHandler())
                .RegisterHandler<SuspendedNestedOp, int>(child, InvocationPolicy.NestedOnly)
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            Task<OpResult<int>> root = dispatcher.Dispatch(new UnawaitedChildRootOp()).AsTask();
            await child.Started;

            Assert.That(root.IsCompleted, Is.False);
            Task<OpResult<int>> queuedRoot = dispatcher
                .Dispatch(new SingleIncrementRootOp())
                .AsTask();
            Assert.That(queuedRoot.IsCompleted, Is.False);

            child.Release();
            InvalidOperationException unawaited = await RequireFailure<InvalidOperationException>(
                root
            );
            OpResult<int> queuedResult = await queuedRoot;

            Assert.That(
                unawaited.Message,
                Does.Contain("returned before awaiting its active child dispatch")
            );
            Assert.That(RequireResolved(queuedResult).Value, Is.EqualTo(12));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(12));
            Assert.That(
                dispatcher.Trace.Get<IncrementOp>(new OpId(902)).RootId,
                Is.EqualTo(new OpId(900))
            );
            Assert.That(
                dispatcher.Diagnostics.Compact,
                Does.Contain("[op 901 parent=900 cause=900] SuspendedNestedOp -> Resolved")
            );
            Assert.That(
                dispatcher.Diagnostics.Compact,
                Does.Not.Contain("[op 900 root] UnawaitedChildRootOp -> Resolved")
            );
            Assert.That(queuedResult.Facts.Single().RootOpId, Is.EqualTo(new OpId(903)));
        }

        /// <summary>
        /// Verifies child cleanup preserves a handler exception before releasing a queued root.
        /// </summary>
        [Test]
        public async Task HandlerExceptionWaitsForActiveChildAndPreservesOriginalFailure()
        {
            InMemoryRulesStore store = CreateStore(10);
            SuspendedNestedHandler child = new SuspendedNestedHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                store,
                new SequentialOpIdProvider(950)
            )
                .RegisterHandler<ThrowingUnawaitedChildRootOp, int>(
                    new ThrowingUnawaitedChildRootHandler()
                )
                .RegisterHandler<SuspendedNestedOp, int>(child, InvocationPolicy.NestedOnly)
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            Task<OpResult<int>> root = dispatcher
                .Dispatch(new ThrowingUnawaitedChildRootOp())
                .AsTask();
            await child.Started;

            Assert.That(root.IsCompleted, Is.False);
            Task<OpResult<int>> queuedRoot = dispatcher
                .Dispatch(new SingleIncrementRootOp())
                .AsTask();
            Assert.That(queuedRoot.IsCompleted, Is.False);

            child.Release();
            ApplicationException original = await RequireFailure<ApplicationException>(root);
            OpResult<int> queuedResult = await queuedRoot;

            Assert.That(original.Message, Is.EqualTo("original handler failure"));
            Assert.That(RequireResolved(queuedResult).Value, Is.EqualTo(12));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(12));
            Assert.That(
                dispatcher.Trace.Get<IncrementOp>(new OpId(952)).RootId,
                Is.EqualTo(new OpId(950))
            );
            Assert.That(queuedResult.Facts.Single().RootOpId, Is.EqualTo(new OpId(953)));
        }

        [Test]
        public async Task HandlerAndIgnoredFailingChildFailuresAreAggregatedInStableOrder()
        {
            InMemoryRulesStore store = CreateStore(10);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<ThrowingIgnoredFailingChildRootOp, int>(
                    new ThrowingIgnoredFailingChildRootHandler()
                )
                .RegisterHandler<SynchronouslyInvalidNestedOp, int>(
                    new SynchronouslyInvalidNestedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            AggregateException error = Assert.ThrowsAsync<AggregateException>(async () =>
                await dispatcher.Dispatch(new ThrowingIgnoredFailingChildRootOp())
            );
            OpResult<int> recovered = await dispatcher.Dispatch(new SingleIncrementRootOp());

            Assert.That(
                error.Message,
                Does.StartWith("Callback execution and cleanup of its unconsumed work both failed.")
            );
            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ApplicationException>());
            Assert.That(error.InnerExceptions[0].Message, Is.EqualTo("handler callback failure"));
            Assert.That(error.InnerExceptions[1], Is.TypeOf<InvalidOperationException>());
            Assert.That(error.InnerExceptions[1].Message, Is.EqualTo("ignored child failure"));
            Assert.That(
                RequireResolved(recovered).Value,
                Is.EqualTo(11),
                "Aggregating callback cleanup failures must still release root ownership."
            );
        }

        [Test]
        [Timeout(10000)]
        public async Task ReturningFrameRejectsRetainedContextAfterChildReservationSettles()
        {
            InMemoryRulesStore store = CreateStore(10);
            CountingOpIdProvider ids = new CountingOpIdProvider(1000);
            SettlementRaceRootHandler handler = new SettlementRaceRootHandler();
            GatedNestedHandler<SettlementRaceFirstChildOp> firstChild =
                new GatedNestedHandler<SettlementRaceFirstChildOp>();
            GatedNestedHandler<SettlementRaceLateChildOp> lateChild =
                new GatedNestedHandler<SettlementRaceLateChildOp>();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store, ids)
                .RegisterHandler<SettlementRaceRootOp, int>(handler)
                .RegisterHandler<SettlementRaceFirstChildOp, int>(
                    firstChild,
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<SettlementRaceLateChildOp, int>(
                    lateChild,
                    InvocationPolicy.NestedOnly
                )
                .RegisterHandler<SingleIncrementRootOp, int>(new SingleIncrementRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();

            using (
                ControlledDispatchThread controlled = new ControlledDispatchThread(() =>
                    dispatcher.Dispatch(new SettlementRaceRootOp()).AsTask()
                )
            )
            {
                await firstChild.Started;
                firstChild.Release();
                Assert.That(
                    controlled.WaitForContinuation(TimeSpan.FromSeconds(5)),
                    Is.True,
                    "The returning parent must be paused after its child reservation settles."
                );
                Assert.That(controlled.RootTask.IsCompleted, Is.False);

                Task<DispatchAttempt> lateAttempt = DispatchOnBackgroundThread(() =>
                    handler.Context.Dispatch(new SettlementRaceLateChildOp()).AsTask()
                );
                Task firstOutcome = await Task.WhenAny(lateAttempt, lateChild.Started);
                if (firstOutcome == lateChild.Started)
                    lateChild.Release();
                DispatchAttempt late = await lateAttempt;

                controlled.ReleaseContinuations();
                InvalidOperationException unawaited = null;
                try
                {
                    await controlled.RootTask;
                }
                catch (InvalidOperationException error)
                {
                    unawaited = error;
                }

                Assert.That(late.Error, Is.Not.Null);
                Assert.That(late.Error.Message, Does.Contain("not actively executing"));
                Assert.That(
                    lateChild.Started.IsCompleted,
                    Is.False,
                    "The retained context must not replace the settled child reservation."
                );
                Assert.That(unawaited, Is.Not.Null);
                Assert.That(
                    unawaited.Message,
                    Does.Contain("returned before awaiting its active child dispatch")
                );
                Assert.That(
                    ids.Calls,
                    Is.EqualTo(2),
                    "The rejected retained context must not allocate a child frame."
                );
                Assert.That(dispatcher.Trace.OrderedFrames.Count, Is.EqualTo(2));
            }

            OpResult<int> laterRoot = await dispatcher.Dispatch(new SingleIncrementRootOp());

            Assert.That(RequireResolved(laterRoot).Value, Is.EqualTo(11));
            Assert.That(laterRoot.Facts.Single().RootOpId, Is.EqualTo(new OpId(1002)));
            Assert.That(ids.Calls, Is.EqualTo(4));
        }

        private static async Task<string> ResolveDiagnostic()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(10),
                new SequentialOpIdProvider(7)
            )
                .RegisterHandler<PoisonToStringOp, int>(new PoisonHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .Build();
            await dispatcher.Dispatch(new PoisonToStringOp());
            return dispatcher.Diagnostics.Compact;
        }

        private static InMemoryRulesStore CreateStore(int health) =>
            new InMemoryRulesStore(
                new RulesStateSeed().SeedHealth(Creature, new HealthState(health, 100))
            );

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private static async Task<TException> RequireFailure<TException>(Task task)
            where TException : Exception
        {
            try
            {
                await task;
            }
            catch (TException error)
            {
                return error;
            }

            throw new AssertionException(
                $"Expected {typeof(TException).Name}, but the task completed successfully."
            );
        }

        private static void AssertFrame<TOp>(
            OpFrame<TOp> frame,
            long id,
            long root,
            long? parent,
            long? cause,
            InvocationPolicy policy,
            long startVersion
        )
            where TOp : IRuleOp
        {
            Assert.That(frame.Id, Is.EqualTo(new OpId(id)));
            Assert.That(frame.RootId, Is.EqualTo(new OpId(root)));
            Assert.That(
                frame.ParentId,
                Is.EqualTo(parent.HasValue ? new OpId(parent.Value) : (OpId?)null)
            );
            Assert.That(
                frame.CauseId,
                Is.EqualTo(cause.HasValue ? new OpId(cause.Value) : (OpId?)null)
            );
            Assert.That(frame.InvocationPolicy, Is.EqualTo(policy));
            Assert.That(frame.StartSnapshot.Version, Is.EqualTo(startVersion));
            Assert.That(frame.IsAction, Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = frame.ActionProfile);
        }

        private sealed class RootOp : IRuleOp<int>
        {
            public int Amount { get; }

            public RootOp(int amount) => Amount = amount;
        }

        private sealed class SettlementTokenOp : IRuleOp<int> { }

        private sealed class SettlementTokenHandler : IOpHandler<SettlementTokenOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<SettlementTokenOp> frame,
                OpHandlerContext context
            ) => new ValueTask<int>(1);
        }

        private sealed class RecordingSettlementObserver
            : IRootSettlementObserver,
                ICausalTreeSettlementObserver
        {
            public int RootSettlements { get; private set; }
            public int TreeSettlements { get; private set; }

            public ValueTask OnRootSettled(
                OpId rootId,
                OpId? causalParentRootId,
                RulesSnapshot snapshot
            )
            {
                RootSettlements++;
                return default;
            }

            public ValueTask OnCausalTreeSettled(OpId rootId, RulesSnapshot snapshot)
            {
                TreeSettlements++;
                return default;
            }
        }

        private sealed class TrackingDisposable : IDisposable
        {
            private readonly string name;
            private readonly List<string> cleanupOrder;

            public TrackingDisposable(string name, List<string> cleanupOrder)
            {
                this.name = name;
                this.cleanupOrder = cleanupOrder;
            }

            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                cleanupOrder.Add(name);
            }
        }

        private sealed class RootHandler : IOpHandler<RootOp, int>
        {
            public long StartVersion { get; private set; }
            public long AfterFirstVersion { get; private set; }
            public long AfterNestedVersion { get; private set; }

            public async ValueTask<int> Handle(OpFrame<RootOp> frame, OpHandlerContext context)
            {
                StartVersion = context.Snapshot.Version;
                await context.Dispatch(new IncrementOp(frame.Op.Amount));
                AfterFirstVersion = context.Snapshot.Version;
                OpResult<int> nested = await context.Dispatch(new NestedHandlerOp(frame.Op.Amount));
                AfterNestedVersion = context.Snapshot.Version;
                return RequireResolved(nested).Value;
            }
        }

        private sealed class NestedHandlerOp : IRuleOp<int>
        {
            public int Amount { get; }

            public NestedHandlerOp(int amount) => Amount = amount;
        }

        private sealed class NestedHandler : IOpHandler<NestedHandlerOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<NestedHandlerOp> frame,
                OpHandlerContext context
            )
            {
                OpResult<int> changed = await context.Dispatch(new IncrementOp(frame.Op.Amount));
                return RequireResolved(changed).Value;
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
                FactSink facts
            )
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

        private sealed class CaptureContextOp : IRuleOp<int> { }

        private sealed class ContextCapturingHandler : IOpHandler<CaptureContextOp, int>
        {
            public OpHandlerContext Context { get; private set; }

            public ValueTask<int> Handle(OpFrame<CaptureContextOp> frame, OpHandlerContext context)
            {
                Context = context;
                return new ValueTask<int>(0);
            }
        }

        private sealed class SuspendedRootOp : IRuleOp<int> { }

        private sealed class SuspendedRootHandler : IOpHandler<SuspendedRootOp, int>
        {
            private readonly TaskCompletionSource<bool> started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            private readonly TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            public Task Started => started.Task;

            public void Release() => release.TrySetResult(true);

            public async ValueTask<int> Handle(
                OpFrame<SuspendedRootOp> frame,
                OpHandlerContext context
            )
            {
                started.TrySetResult(true);
                await release.Task;
                return 0;
            }
        }

        private sealed class RacingRootOp : IRuleOp<int> { }

        private sealed class RacingRootHandler : IOpHandler<RacingRootOp, int>
        {
            private readonly TaskCompletionSource<bool> started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            private readonly TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            private int calls;
            private int active;
            private int maximumConcurrency;

            public Task Started => started.Task;
            public int Calls => Volatile.Read(ref calls);
            public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

            public void Release() => release.TrySetResult(true);

            public async ValueTask<int> Handle(
                OpFrame<RacingRootOp> frame,
                OpHandlerContext context
            )
            {
                Interlocked.Increment(ref calls);
                int current = Interlocked.Increment(ref active);
                UpdateMaximum(current);
                started.TrySetResult(true);
                try
                {
                    await release.Task;
                    return 0;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }

            private void UpdateMaximum(int candidate)
            {
                int observed = Volatile.Read(ref maximumConcurrency);
                while (candidate > observed)
                {
                    int previous = Interlocked.CompareExchange(
                        ref maximumConcurrency,
                        candidate,
                        observed
                    );
                    if (previous == observed)
                        return;
                    observed = previous;
                }
            }
        }

        private sealed class ReentrantRootOp : IRuleOp<int> { }

        private sealed class ReentrantRootHandler : IOpHandler<ReentrantRootOp, int>
        {
            private readonly Task<RuleDispatcher> dispatcher;

            public ReentrantRootHandler(Task<RuleDispatcher> dispatcher) =>
                this.dispatcher = dispatcher;

            public async ValueTask<int> Handle(
                OpFrame<ReentrantRootOp> frame,
                OpHandlerContext context
            )
            {
                RuleDispatcher activeDispatcher = await dispatcher;
                await activeDispatcher.Dispatch(new SingleIncrementRootOp());
                return 0;
            }
        }

        private sealed class DelayedRootOp : IRuleOp<int> { }

        private sealed class DelayedRootHandler : IOpHandler<DelayedRootOp, int>
        {
            private readonly Task<RuleDispatcher> dispatcher;
            private readonly Task release;
            private readonly TaskCompletionSource<OpResult<int>> result;

            public DelayedRootHandler(
                Task<RuleDispatcher> dispatcher,
                Task release,
                TaskCompletionSource<OpResult<int>> result
            )
            {
                this.dispatcher = dispatcher;
                this.release = release;
                this.result = result;
            }

            public ValueTask<int> Handle(OpFrame<DelayedRootOp> frame, OpHandlerContext context)
            {
                _ = DispatchAfterRelease();
                return new ValueTask<int>(0);
            }

            private async Task DispatchAfterRelease()
            {
                try
                {
                    await release;
                    RuleDispatcher activeDispatcher = await dispatcher;
                    result.TrySetResult(
                        await activeDispatcher.Dispatch(new SingleIncrementRootOp())
                    );
                }
                catch (Exception error)
                {
                    result.TrySetException(error);
                }
            }
        }

        private sealed class DispatchAttempt
        {
            public OpResult<int> Result { get; }
            public InvalidOperationException Error { get; }

            public DispatchAttempt(OpResult<int> result, InvalidOperationException error)
            {
                Result = result;
                Error = error;
            }
        }

        private sealed class ControlledDispatchThread : SynchronizationContext, IDisposable
        {
            private readonly Func<Task<OpResult<int>>> dispatch;
            private readonly Queue<Continuation> continuations = new Queue<Continuation>();
            private readonly ManualResetEventSlim dispatchStarted = new ManualResetEventSlim();
            private readonly ManualResetEventSlim continuationQueued = new ManualResetEventSlim();
            private readonly ManualResetEventSlim releaseContinuations = new ManualResetEventSlim();
            private readonly Thread thread;
            private Exception startError;

            public Task<OpResult<int>> RootTask { get; private set; }

            public ControlledDispatchThread(Func<Task<OpResult<int>>> dispatch)
            {
                this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
                thread = new Thread(Run) { IsBackground = true };
                thread.Start();
                if (!dispatchStarted.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The controlled dispatcher thread did not start.");
                if (startError != null)
                    throw new InvalidOperationException(
                        "The controlled dispatcher thread failed to start.",
                        startError
                    );
            }

            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (continuations)
                    continuations.Enqueue(new Continuation(callback, state));
                continuationQueued.Set();
            }

            public bool WaitForContinuation(TimeSpan timeout) => continuationQueued.Wait(timeout);

            public void ReleaseContinuations() => releaseContinuations.Set();

            public void Dispose()
            {
                releaseContinuations.Set();
                continuationQueued.Set();
                if (!thread.Join(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The controlled dispatcher thread did not stop.");
                dispatchStarted.Dispose();
                continuationQueued.Dispose();
                releaseContinuations.Dispose();
            }

            private void Run()
            {
                SynchronizationContext.SetSynchronizationContext(this);
                try
                {
                    RootTask = dispatch();
                }
                catch (Exception error)
                {
                    startError = error;
                }
                finally
                {
                    dispatchStarted.Set();
                }

                if (RootTask == null)
                    return;

                releaseContinuations.Wait();
                while (!RootTask.IsCompleted)
                {
                    continuationQueued.Wait();
                    DrainContinuations();
                }
                DrainContinuations();
            }

            private void DrainContinuations()
            {
                while (true)
                {
                    Continuation continuation;
                    lock (continuations)
                    {
                        if (continuations.Count == 0)
                        {
                            continuationQueued.Reset();
                            return;
                        }
                        continuation = continuations.Dequeue();
                    }
                    continuation.Callback(continuation.State);
                }
            }

            private sealed class Continuation
            {
                public SendOrPostCallback Callback { get; }
                public object State { get; }

                public Continuation(SendOrPostCallback callback, object state)
                {
                    Callback = callback;
                    State = state;
                }
            }
        }

        private sealed class OverlappingRootOp : IRuleOp<int> { }

        private sealed class OverlappingRootHandler : IOpHandler<OverlappingRootOp, int>
        {
            private readonly SuspendedNestedHandler suspended;

            public InvalidOperationException OverlapError { get; private set; }
            public OpResult<int> FirstChildResult { get; private set; }
            public OpResult<int> SequentialSiblingResult { get; private set; }

            public OverlappingRootHandler(SuspendedNestedHandler suspended) =>
                this.suspended = suspended;

            public async ValueTask<int> Handle(
                OpFrame<OverlappingRootOp> frame,
                OpHandlerContext context
            )
            {
                Task<OpResult<int>> firstChild = context
                    .Dispatch(new SuspendedNestedOp(1))
                    .AsTask();
                await suspended.Started;
                try
                {
                    await context.Dispatch(new IncrementOp(100));
                }
                catch (InvalidOperationException error)
                {
                    OverlapError = error;
                }
                finally
                {
                    suspended.Release();
                }

                FirstChildResult = await firstChild;
                SequentialSiblingResult = await context.Dispatch(new NestedHandlerOp(2));
                return RequireResolved(SequentialSiblingResult).Value;
            }
        }

        private sealed class SuspendedNestedOp : IRuleOp<int>
        {
            public int Amount { get; }

            public SuspendedNestedOp(int amount) => Amount = amount;
        }

        private sealed class SuspendedNestedHandler : IOpHandler<SuspendedNestedOp, int>
        {
            private readonly TaskCompletionSource<bool> started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            private readonly TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            public Task Started => started.Task;

            public void Release() => release.TrySetResult(true);

            public async ValueTask<int> Handle(
                OpFrame<SuspendedNestedOp> frame,
                OpHandlerContext context
            )
            {
                started.TrySetResult(true);
                await release.Task;
                OpResult<int> changed = await context.Dispatch(new IncrementOp(frame.Op.Amount));
                return RequireResolved(changed).Value;
            }
        }

        private sealed class IgnoredSynchronousChildRootOp : IRuleOp<int> { }

        private sealed class IgnoredSynchronousChildRootHandler
            : IOpHandler<IgnoredSynchronousChildRootOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<IgnoredSynchronousChildRootOp> frame,
                OpHandlerContext context
            )
            {
                _ = context.Dispatch(new IncrementOp(1));
                return new ValueTask<int>(0);
            }
        }

        private sealed class IgnoredFailingChildRootOp : IRuleOp<int> { }

        private sealed class IgnoredFailingChildRootHandler
            : IOpHandler<IgnoredFailingChildRootOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<IgnoredFailingChildRootOp> frame,
                OpHandlerContext context
            )
            {
                _ = context.Dispatch(new SynchronouslyFailingNestedOp());
                return new ValueTask<int>(0);
            }
        }

        private sealed class SynchronouslyFailingNestedOp : IRuleOp<int> { }

        private sealed class SynchronouslyFailingNestedHandler
            : IOpHandler<SynchronouslyFailingNestedOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<SynchronouslyFailingNestedOp> frame,
                OpHandlerContext context
            ) => throw new ApplicationException("synchronous child failure");
        }

        private sealed class UnawaitedChildRootOp : IRuleOp<int> { }

        private sealed class UnawaitedChildRootHandler : IOpHandler<UnawaitedChildRootOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<UnawaitedChildRootOp> frame,
                OpHandlerContext context
            )
            {
                _ = context.Dispatch(new SuspendedNestedOp(1));
                return new ValueTask<int>(0);
            }
        }

        private sealed class ThrowingUnawaitedChildRootOp : IRuleOp<int> { }

        private sealed class ThrowingUnawaitedChildRootHandler
            : IOpHandler<ThrowingUnawaitedChildRootOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<ThrowingUnawaitedChildRootOp> frame,
                OpHandlerContext context
            )
            {
                _ = context.Dispatch(new SuspendedNestedOp(1));
                throw new ApplicationException("original handler failure");
            }
        }

        private sealed class ThrowingIgnoredFailingChildRootOp : IRuleOp<int> { }

        private sealed class ThrowingIgnoredFailingChildRootHandler
            : IOpHandler<ThrowingIgnoredFailingChildRootOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<ThrowingIgnoredFailingChildRootOp> frame,
                OpHandlerContext context
            )
            {
                _ = context.Dispatch(new SynchronouslyInvalidNestedOp());
                throw new ApplicationException("handler callback failure");
            }
        }

        private sealed class SynchronouslyInvalidNestedOp : IRuleOp<int> { }

        private sealed class SynchronouslyInvalidNestedHandler
            : IOpHandler<SynchronouslyInvalidNestedOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<SynchronouslyInvalidNestedOp> frame,
                OpHandlerContext context
            ) => throw new InvalidOperationException("ignored child failure");
        }

        private sealed class SettlementRaceRootOp : IRuleOp<int> { }

        private sealed class SettlementRaceRootHandler : IOpHandler<SettlementRaceRootOp, int>
        {
            public OpHandlerContext Context { get; private set; }

            public ValueTask<int> Handle(
                OpFrame<SettlementRaceRootOp> frame,
                OpHandlerContext context
            )
            {
                Context = context;
                SynchronizationContext previous = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    _ = context.Dispatch(new SettlementRaceFirstChildOp());
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
                return new ValueTask<int>(0);
            }
        }

        private sealed class SettlementRaceFirstChildOp : IRuleOp<int> { }

        private sealed class SettlementRaceLateChildOp : IRuleOp<int> { }

        private sealed class GatedNestedHandler<TOp> : IOpHandler<TOp, int>
            where TOp : IRuleOp<int>
        {
            private readonly TaskCompletionSource<bool> started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            private readonly TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            public Task Started => started.Task;

            public void Release() => release.TrySetResult(true);

            public async ValueTask<int> Handle(OpFrame<TOp> frame, OpHandlerContext context)
            {
                started.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return 0;
            }
        }

        private sealed class RecoveringRootOp : IRuleOp<int> { }

        private sealed class RecoveringRootHandler : IOpHandler<RecoveringRootOp, int>
        {
            public InvalidOperationException ChildError { get; private set; }

            public async ValueTask<int> Handle(
                OpFrame<RecoveringRootOp> frame,
                OpHandlerContext context
            )
            {
                try
                {
                    await context.Dispatch(new ThrowingNestedOp());
                }
                catch (InvalidOperationException error)
                {
                    ChildError = error;
                }

                OpResult<int> changed = await context.Dispatch(new IncrementOp(1));
                return RequireResolved(changed).Value;
            }
        }

        private sealed class ThrowingNestedOp : IRuleOp<int> { }

        private sealed class ThrowingNestedHandler : IOpHandler<ThrowingNestedOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<ThrowingNestedOp> frame,
                OpHandlerContext context
            )
            {
                await Task.Yield();
                throw new InvalidOperationException("expected nested failure");
            }
        }

        private sealed class RejectRootOp : IRuleOp<OpStatus> { }

        private sealed class RejectRootHandler : IOpHandler<RejectRootOp, OpStatus>
        {
            public async ValueTask<OpStatus> Handle(
                OpFrame<RejectRootOp> frame,
                OpHandlerContext context
            )
            {
                OpResult<int> rejected = await context.Dispatch(new RejectOp());
                return rejected.Status;
            }
        }

        private sealed class RejectOp : IRuleOp<int> { }

        private sealed class RejectReducer : IOpReducer<RejectOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<RejectOp> context,
                RulesStateDraft state,
                FactSink facts
            ) => ReductionResult<int>.Reject("expected rule failure");
        }

        private sealed class AmbiguousOp : IRuleOp<int>, IRuleOp<string> { }

        private sealed class AmbiguousIntHandler : IOpHandler<AmbiguousOp, int>
        {
            public ValueTask<int> Handle(OpFrame<AmbiguousOp> frame, OpHandlerContext context) =>
                new ValueTask<int>(1);
        }

        private sealed class ConstantIdProvider : IOpIdProvider
        {
            public OpId Next() => new OpId(1);
        }

        private sealed class CountingOpIdProvider : IOpIdProvider
        {
            private readonly long firstValue;

            public int Calls { get; private set; }

            public CountingOpIdProvider(long firstValue) => this.firstValue = firstValue;

            public OpId Next() => new OpId(firstValue + Calls++);
        }

        private sealed class CountingIncrementReducer : IOpReducer<IncrementOp, int>
        {
            private readonly IncrementReducer inner = new IncrementReducer();

            public int Calls { get; private set; }

            public ReductionResult<int> Reduce(
                ReductionContext<IncrementOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                Calls++;
                return inner.Reduce(context, state, facts);
            }
        }

        private sealed class PoisonToStringOp : IRuleOp<int>
        {
            public override string ToString() =>
                throw new InvalidOperationException("Diagnostics called arbitrary ToString().");
        }

        private sealed class PoisonHandler : IOpHandler<PoisonToStringOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<PoisonToStringOp> frame,
                OpHandlerContext context
            )
            {
                OpResult<int> changed = await context.Dispatch(new IncrementOp(1));
                return RequireResolved(changed).Value;
            }
        }

        private sealed class SingleIncrementRootOp : IRuleOp<int> { }

        private sealed class SingleIncrementRootHandler : IOpHandler<SingleIncrementRootOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<SingleIncrementRootOp> frame,
                OpHandlerContext context
            )
            {
                OpResult<int> changed = await context.Dispatch(new IncrementOp(1));
                return RequireResolved(changed).Value;
            }
        }

        private sealed class CrossRootStore : IRulesStore
        {
            private readonly InMemoryRulesStore inner = CreateStore(10);

            public RulesSnapshot Snapshot => inner.Snapshot;

            public ReductionResult<TResult> Reduce<TOp, TResult>(
                ReductionContext<TOp> context,
                IOpReducer<TOp, TResult> reducer
            )
                where TOp : IRuleOp<TResult>
            {
                ReductionResult<TResult> reduced = inner.Reduce(context, reducer);
                foreach (RuleFact fact in reduced.Facts)
                {
                    typeof(RuleFact)
                        .GetField(
                            "<RootOpId>k__BackingField",
                            BindingFlags.Instance | BindingFlags.NonPublic
                        )
                        .SetValue(fact, new OpId(context.RootOpId.Value + 100));
                }
                return reduced;
            }
        }
    }
}
