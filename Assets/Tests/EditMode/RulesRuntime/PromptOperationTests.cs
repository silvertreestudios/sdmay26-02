using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies typed prompt outcomes, adapter boundaries, nested provenance, and root serialization.
    /// </summary>
    public sealed class PromptOperationTests
    {
        private static readonly CreatureId Creature = new CreatureId("prompt-creature");
        private static readonly PlayerId Player = new PlayerId("prompt-player");
        private static readonly RuleSource Source = RuleSource.FromSlug("prompt-tests");

        /// <summary>
        /// Verifies that requests own a deterministic copy and model a normal decline as a selected value.
        /// </summary>
        [Test]
        public void ChoiceRequestsOwnTheirChoicesAndRepresentDeclineAsASelection()
        {
            List<string> supplied = new List<string> { "accept", "decline" };
            ChoiceRequest<string> request = new ChoiceRequest<string>(
                new ChoiceRequestId("reaction-answer"),
                supplied);

            supplied[0] = "changed";
            supplied.Add("later");
            SelectedChoiceResult<bool> declined = ChoiceResult<bool>.Selected(false);

            Assert.That(request.Id, Is.EqualTo(new ChoiceRequestId("reaction-answer")));
            Assert.That(request.Choices, Is.EqualTo(new[] { "accept", "decline" }));
            Assert.That(request.Choices, Is.Not.SameAs(supplied));
            Assert.That(declined.Choice, Is.False);
            Assert.That(default(ChoiceRequestId).Value, Is.Empty);
            Assert.That(default(PromptAdapterFailure).Reason, Is.Empty);
            Assert.Throws<ArgumentException>(() =>
                new ChoiceRequest<string>(default, new[] { "accept" }));
            Assert.Throws<ArgumentException>(() =>
                new ChoiceRequest<string>(new ChoiceRequestId("empty"), Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() =>
                new ChoiceRequest<string>(
                    new ChoiceRequestId("duplicate"),
                    new[] { "accept", "accept" }));
        }

        /// <summary>
        /// Verifies all supported prompt outcomes and their ancestry within one serialized root.
        /// </summary>
        [Test]
        public async Task ScriptedAdapterPreservesResolvedFailuresDeclineAndCancellation()
        {
            PromptAdapterFailure timeout = new PromptAdapterFailure(
                PromptAdapterFailureKind.TimedOut,
                "The decision window elapsed.");
            PromptAdapterFailure disconnect = new PromptAdapterFailure(
                PromptAdapterFailureKind.Disconnected,
                "The controller disconnected.");
            ScriptedPromptAdapter<bool> adapter = new ScriptedPromptAdapter<bool>(
                OpResult<ChoiceResult<bool>>.Resolved(ChoiceResult<bool>.Selected(true)),
                OpResult<ChoiceResult<bool>>.Resolved(ChoiceResult<bool>.Selected(false)),
                OpResult<ChoiceResult<bool>>.Resolved(
                    ChoiceResult<bool>.Unavailable("No decision adapter is active.")),
                OpResult<ChoiceResult<bool>>.Resolved(ChoiceResult<bool>.Failed(timeout)),
                OpResult<ChoiceResult<bool>>.Resolved(ChoiceResult<bool>.Failed(disconnect)),
                OpResult<ChoiceResult<bool>>.Cancelled());
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    CreateStore(10),
                    new SequentialOpIdProvider(20))
                .UsePromptAdapter(adapter)
                .RegisterHandler<PromptSequenceOp, IReadOnlyList<OpResult<ChoiceResult<bool>>>>(
                    new PromptSequenceHandler())
                .Build();

            OpResult<IReadOnlyList<OpResult<ChoiceResult<bool>>>> result =
                await dispatcher.Dispatch(new PromptSequenceOp(6));

            IReadOnlyList<OpResult<ChoiceResult<bool>>> outcomes = RequireResolved(result).Value;
            Assert.That(RequireSelected(outcomes[0]).Choice, Is.True);
            Assert.That(RequireSelected(outcomes[1]).Choice, Is.False,
                "Declining a reaction is a normal selected choice, not cancellation.");
            Assert.That(
                RequireChoice<UnavailableChoiceResult<bool>>(outcomes[2]).Reason,
                Is.EqualTo("No decision adapter is active."));
            Assert.That(
                RequireChoice<FailedChoiceResult<bool>>(outcomes[3]).Failure,
                Is.EqualTo(timeout));
            Assert.That(
                RequireChoice<FailedChoiceResult<bool>>(outcomes[4]).Failure,
                Is.EqualTo(disconnect));
            Assert.That(outcomes[5], Is.TypeOf<CancelledOpResult<ChoiceResult<bool>>>());
            Assert.That(adapter.Remaining, Is.Zero);
            Assert.That(result.Facts, Is.Empty);

            OpFrame<PromptSequenceOp> root = dispatcher.Trace.Get<PromptSequenceOp>(new OpId(20));
            Assert.That(root.RootId, Is.EqualTo(root.Id));
            for (long id = 21; id <= 26; id++)
            {
                OpFrame<PromptChoiceOp<bool>> prompt =
                    dispatcher.Trace.Get<PromptChoiceOp<bool>>(new OpId(id));
                Assert.That(prompt.RootId, Is.EqualTo(root.Id));
                Assert.That(prompt.ParentId, Is.EqualTo(root.Id));
                Assert.That(prompt.CauseId, Is.EqualTo(root.Id));
                Assert.That(prompt.InvocationPolicy, Is.EqualTo(InvocationPolicy.NestedOnly));
            }
        }

        /// <summary>
        /// Verifies that prompt registration rejects undeclared choices and non-prompt operation statuses.
        /// </summary>
        [Test]
        public void PromptRegistrationRejectsAdapterContractViolations()
        {
            InvalidOperationException undeclared = ResolveAdapterContractViolation(
                OpResult<ChoiceResult<bool>>.Resolved(ChoiceResult<bool>.Selected(false)),
                new ChoiceRequest<bool>(new ChoiceRequestId("only-true"), new[] { true }));
            InvalidOperationException invalid = ResolveAdapterContractViolation(
                OpResult<ChoiceResult<bool>>.Invalid("Adapters cannot invalidate prompt operations."),
                YesNoRequest());
            InvalidOperationException interrupted = ResolveAdapterContractViolation(
                OpResult<ChoiceResult<bool>>.Interrupted(),
                YesNoRequest());

            Assert.That(undeclared.Message, Does.Contain("not declared"));
            Assert.That(invalid.Message, Does.Contain("resolved choice outcome or explicit cancellation"));
            Assert.That(interrupted.Message, Does.Contain("resolved choice outcome or explicit cancellation"));
        }

        /// <summary>
        /// Verifies that prompt operations cannot start externally or be configured more than once.
        /// </summary>
        [Test]
        public void PromptOperationsAreNestedOnlyAndHaveOneAdapterPerChoiceType()
        {
            ScriptedPromptAdapter<bool> first = new ScriptedPromptAdapter<bool>(
                OpResult<ChoiceResult<bool>>.Resolved(ChoiceResult<bool>.Selected(true)));
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(CreateStore(10))
                .UsePromptAdapter(first);
            Assert.Throws<ArgumentException>(() => new ScriptedPromptAdapter<bool>(
                OpResult<ChoiceResult<bool>>.Invalid("Invalid is not a prompt outcome.")));
            Assert.Throws<InvalidOperationException>(() => builder.UsePromptAdapter(
                new ScriptedPromptAdapter<bool>()));

            RuleDispatcher dispatcher = builder.Build();
            InvalidOperationException external = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new PromptChoiceOp<bool>(Player, YesNoRequest())));

            Assert.That(external.Message, Does.Contain("nested-only"));
            Assert.That(first.Remaining, Is.EqualTo(1),
                "Rejecting an external prompt must not invoke its adapter.");
        }

        /// <summary>
        /// Verifies decline itself commits no feature cost and acceptance spends only a later dispatched cost.
        /// </summary>
        [Test]
        public async Task DeclineDoesNotSpendAParentFeatureCost()
        {
            InMemoryRulesStore store = CreateStore(10);
            ScriptedPromptAdapter<bool> adapter = new ScriptedPromptAdapter<bool>(
                OpResult<ChoiceResult<bool>>.Resolved(ChoiceResult<bool>.Selected(false)),
                OpResult<ChoiceResult<bool>>.Resolved(ChoiceResult<bool>.Selected(true)));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .UsePromptAdapter(adapter)
                .RegisterHandler<PromptThenIncrementOp, int>(new PromptThenIncrementHandler())
                .RegisterReducer<IncrementHealthOp, int>(new IncrementHealthReducer(), Source)
                .Build();

            OpResult<int> declined = await dispatcher.Dispatch(new PromptThenIncrementOp());
            OpResult<int> accepted = await dispatcher.Dispatch(new PromptThenIncrementOp());

            Assert.That(RequireResolved(declined).Value, Is.EqualTo(10));
            Assert.That(declined.Facts, Is.Empty,
                "Resolving the decline must not commit the parent's optional cost.");
            Assert.That(RequireResolved(accepted).Value, Is.EqualTo(11));
            Assert.That(accepted.Facts, Has.Count.EqualTo(1),
                "The parent spends its cost only through the later reducer dispatch.");
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(11));
            Assert.That(adapter.Remaining, Is.Zero);
        }

        /// <summary>
        /// Verifies that a suspended prompt queues unrelated roots while preserving same-root nested work.
        /// </summary>
        [Test]
        [Timeout(10000)]
        public async Task SuspendedPromptSerializesUnrelatedRootsAndAllowsSameRootWork()
        {
            InMemoryRulesStore store = CreateStore(10);
            CountingOpIdProvider ids = new CountingOpIdProvider(100);
            SuspendedPromptAdapter adapter = new SuspendedPromptAdapter();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store, ids)
                .UsePromptAdapter<bool>(adapter)
                .RegisterHandler<PromptThenIncrementOp, int>(new PromptThenIncrementHandler())
                .RegisterHandler<IndependentIncrementOp, int>(new IndependentIncrementHandler())
                .RegisterReducer<IncrementHealthOp, int>(new IncrementHealthReducer(), Source)
                .Build();

            Task<OpResult<int>> promptingRoot = dispatcher.Dispatch(
                new PromptThenIncrementOp()).AsTask();
            await adapter.Started;
            Task<OpResult<int>> unrelatedRoot = dispatcher.Dispatch(
                new IndependentIncrementOp()).AsTask();
            await Task.Yield();

            Assert.That(promptingRoot.IsCompleted, Is.False);
            Assert.That(unrelatedRoot.IsCompleted, Is.False);
            Assert.That(ids.Calls, Is.EqualTo(2),
                "A queued root must not allocate an ID or create a frame.");
            Assert.That(dispatcher.Trace.OrderedFrames.Select(frame => frame.Id),
                Is.EqualTo(new[] { new OpId(100), new OpId(101) }));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(10));

            adapter.Select(true);
            OpResult<int> promptingResult = await promptingRoot;
            OpResult<int> unrelatedResult = await unrelatedRoot;

            Assert.That(RequireResolved(promptingResult).Value, Is.EqualTo(11));
            Assert.That(RequireResolved(unrelatedResult).Value, Is.EqualTo(12));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(12));
            Assert.That(ids.Calls, Is.EqualTo(5));
            Assert.That(dispatcher.Trace.OrderedFrames.Select(frame => frame.Id),
                Is.EqualTo(new[]
                {
                    new OpId(100),
                    new OpId(101),
                    new OpId(102),
                    new OpId(103),
                    new OpId(104)
                }));

            OpFrame<PromptChoiceOp<bool>> prompt =
                dispatcher.Trace.Get<PromptChoiceOp<bool>>(new OpId(101));
            OpFrame<IncrementHealthOp> sameRootWork =
                dispatcher.Trace.Get<IncrementHealthOp>(new OpId(102));
            OpFrame<IndependentIncrementOp> unrelated =
                dispatcher.Trace.Get<IndependentIncrementOp>(new OpId(103));
            Assert.That(prompt.RootId, Is.EqualTo(new OpId(100)));
            Assert.That(prompt.ParentId, Is.EqualTo(new OpId(100)));
            Assert.That(prompt.CauseId, Is.EqualTo(new OpId(100)));
            Assert.That(sameRootWork.RootId, Is.EqualTo(new OpId(100)));
            Assert.That(sameRootWork.ParentId, Is.EqualTo(new OpId(100)));
            Assert.That(unrelated.RootId, Is.EqualTo(new OpId(103)));
            Assert.That(unrelated.ParentId.HasValue, Is.False);
            Assert.That(promptingResult.Facts.Single().RootOpId, Is.EqualTo(new OpId(100)));
            Assert.That(unrelatedResult.Facts.Single().RootOpId, Is.EqualTo(new OpId(103)));
        }

        private static InvalidOperationException ResolveAdapterContractViolation(
            OpResult<ChoiceResult<bool>> adapterResult,
            ChoiceRequest<bool> request)
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(10))
                .UsePromptAdapter(new FixedPromptAdapter(adapterResult))
                .RegisterHandler<SinglePromptOp, OpStatus>(new SinglePromptHandler())
                .Build();
            return Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new SinglePromptOp(request)));
        }

        private static ChoiceRequest<bool> YesNoRequest() =>
            new ChoiceRequest<bool>(new ChoiceRequestId("yes-or-no"), new[] { true, false });

        private static InMemoryRulesStore CreateStore(int health) =>
            new InMemoryRulesStore(new RulesStateSeed()
                .SeedHealth(Creature, new HealthState(health, 100)));

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private static SelectedChoiceResult<TChoice> RequireSelected<TChoice>(
            OpResult<ChoiceResult<TChoice>> result) =>
            RequireChoice<SelectedChoiceResult<TChoice>, TChoice>(result);

        private static TOutcome RequireChoice<TOutcome, TChoice>(
            OpResult<ChoiceResult<TChoice>> result)
            where TOutcome : ChoiceResult<TChoice>
        {
            ResolvedOpResult<ChoiceResult<TChoice>> resolved = RequireResolved(result);
            Assert.That(resolved.Value, Is.TypeOf<TOutcome>());
            return (TOutcome)resolved.Value;
        }

        private static TOutcome RequireChoice<TOutcome>(
            OpResult<ChoiceResult<bool>> result)
            where TOutcome : ChoiceResult<bool> =>
            RequireChoice<TOutcome, bool>(result);

        private sealed class PromptSequenceOp :
            IRuleOp<IReadOnlyList<OpResult<ChoiceResult<bool>>>>
        {
            public int Count { get; }

            public PromptSequenceOp(int count) => Count = count;
        }

        private sealed class PromptSequenceHandler :
            IOpHandler<PromptSequenceOp, IReadOnlyList<OpResult<ChoiceResult<bool>>>>
        {
            public async ValueTask<IReadOnlyList<OpResult<ChoiceResult<bool>>>> Handle(
                OpFrame<PromptSequenceOp> frame,
                OpHandlerContext context)
            {
                List<OpResult<ChoiceResult<bool>>> results =
                    new List<OpResult<ChoiceResult<bool>>>(frame.Op.Count);
                for (int index = 0; index < frame.Op.Count; index++)
                {
                    results.Add(await context.Dispatch(
                        new PromptChoiceOp<bool>(Player, YesNoRequest())));
                }
                return Array.AsReadOnly(results.ToArray());
            }
        }

        private sealed class SinglePromptOp : IRuleOp<OpStatus>
        {
            public ChoiceRequest<bool> Request { get; }

            public SinglePromptOp(ChoiceRequest<bool> request) => Request = request;
        }

        private sealed class SinglePromptHandler : IOpHandler<SinglePromptOp, OpStatus>
        {
            public async ValueTask<OpStatus> Handle(
                OpFrame<SinglePromptOp> frame,
                OpHandlerContext context)
            {
                OpResult<ChoiceResult<bool>> prompt = await context.Dispatch(
                    new PromptChoiceOp<bool>(Player, frame.Op.Request));
                return prompt.Status;
            }
        }

        private sealed class PromptThenIncrementOp : IRuleOp<int>
        {
        }

        private sealed class PromptThenIncrementHandler : IOpHandler<PromptThenIncrementOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<PromptThenIncrementOp> frame,
                OpHandlerContext context)
            {
                OpResult<ChoiceResult<bool>> prompt = await context.Dispatch(
                    new PromptChoiceOp<bool>(Player, YesNoRequest()));
                SelectedChoiceResult<bool> selected = RequireSelected(prompt);
                if (!selected.Choice)
                    return context.Snapshot.Health[Creature].Current;

                OpResult<int> changed = await context.Dispatch(new IncrementHealthOp(1));
                return RequireResolved(changed).Value;
            }
        }

        private sealed class IndependentIncrementOp : IRuleOp<int>
        {
        }

        private sealed class IndependentIncrementHandler : IOpHandler<IndependentIncrementOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<IndependentIncrementOp> frame,
                OpHandlerContext context)
            {
                OpResult<int> changed = await context.Dispatch(new IncrementHealthOp(1));
                return RequireResolved(changed).Value;
            }
        }

        private sealed class IncrementHealthOp : IRuleOp<int>
        {
            public int Amount { get; }

            public IncrementHealthOp(int amount) => Amount = amount;
        }

        private sealed class IncrementHealthReducer : IOpReducer<IncrementHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<IncrementHealthOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                if (!state.Health.TryGet(Creature, out HealthState previous))
                    throw new InvalidOperationException("Missing prompt-test health seed.");
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

        private sealed class SuspendedPromptAdapter : IPromptAdapter<bool>
        {
            private readonly TaskCompletionSource<bool> started =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> selection =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Started => started.Task;

            public void Select(bool choice) => selection.TrySetResult(choice);

            public async ValueTask<OpResult<ChoiceResult<bool>>> Prompt(
                PromptChoiceOp<bool> prompt,
                RulesSnapshot snapshot)
            {
                started.TrySetResult(true);
                bool choice = await selection.Task;
                return OpResult<ChoiceResult<bool>>.Resolved(
                    ChoiceResult<bool>.Selected(choice));
            }
        }

        private sealed class FixedPromptAdapter : IPromptAdapter<bool>
        {
            private readonly OpResult<ChoiceResult<bool>> result;

            public FixedPromptAdapter(OpResult<ChoiceResult<bool>> result) =>
                this.result = result;

            public ValueTask<OpResult<ChoiceResult<bool>>> Prompt(
                PromptChoiceOp<bool> prompt,
                RulesSnapshot snapshot) =>
                new ValueTask<OpResult<ChoiceResult<bool>>>(result);
        }

        private sealed class CountingOpIdProvider : IOpIdProvider
        {
            private readonly long firstValue;
            private int calls;

            public int Calls => Volatile.Read(ref calls);

            public CountingOpIdProvider(long firstValue) => this.firstValue = firstValue;

            public OpId Next() => new OpId(firstValue + Interlocked.Increment(ref calls) - 1);
        }
    }
}
