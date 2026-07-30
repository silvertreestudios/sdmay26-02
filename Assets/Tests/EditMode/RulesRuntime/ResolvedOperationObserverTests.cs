using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ResolvedOperationObserverTests
    {
        private static readonly CreatureId Owner = new CreatureId("resolved-observer-owner");
        private static readonly RuleDefinitionId Definition = new RuleDefinitionId(
            "resolved-observer-rule"
        );
        private static readonly RuleSource Source = RuleSource.FromSlug("resolved-observer-test");

        [Test]
        public async Task DeliversResolvedOnlyAndMatchesBothOperationAndResultType()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            RecordingObserver observer = new RecordingObserver("matching", new List<string>());
            dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(observer);

            await dispatcher.Dispatch(new ObservedOp(1, OpStatus.Resolved));
            await dispatcher.Dispatch(new ObservedOp(2, OpStatus.Invalid));
            await dispatcher.Dispatch(new ObservedOp(3, OpStatus.Interrupted));
            await dispatcher.Dispatch(new ObservedOp(4, OpStatus.Cancelled));
            await dispatcher.Dispatch(new OtherOp(5));

            Assert.That(observer.Values, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public async Task RegistrationTokenUnregistersObserverExactlyOnce()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            CountingObserver observer = new CountingObserver();
            IDisposable registration = dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(
                observer
            );

            await dispatcher.Dispatch(new ObservedOp(1, OpStatus.Resolved));
            registration.Dispose();
            registration.Dispose();
            await dispatcher.Dispatch(new ObservedOp(2, OpStatus.Resolved));

            Assert.That(observer.Count, Is.EqualTo(1));
            Assert.That(
                dispatcher.UnregisterResolvedOpObserver<ObservedOp, int>(observer),
                Is.False
            );
        }

        [Test]
        public async Task NestedObserverFinishesBeforeParentContinuationAndParentObservation()
        {
            List<string> order = new List<string>();
            RuleDispatcher dispatcher = CreateDispatcher(order);
            dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(
                new RecordingObserver("child-observer", order)
            );
            dispatcher.RegisterResolvedOpObserver<ParentOp, int>(new ParentObserver(order));

            await dispatcher.Dispatch(new ParentOp());

            Assert.That(
                order,
                Is.EqualTo(
                    new[]
                    {
                        "child-handler",
                        "child-observer:7",
                        "parent-after-child",
                        "parent-observer:7",
                    }
                )
            );
        }

        [Test]
        public async Task RegistrationChangesApplyToLaterNotificationPassesOnly()
        {
            List<string> order = new List<string>();
            RuleDispatcher dispatcher = CreateDispatcher();
            RecordingObserver removed = new RecordingObserver("removed", order);
            RecordingObserver added = new RecordingObserver("added", order);
            MutatingObserver mutating = new MutatingObserver(dispatcher, removed, added, order);
            dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(mutating);
            dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(removed);

            await dispatcher.Dispatch(new ObservedOp(1, OpStatus.Resolved));
            await dispatcher.Dispatch(new ObservedOp(2, OpStatus.Resolved));

            Assert.That(
                order,
                Is.EqualTo(new[] { "mutating:1", "removed:1", "mutating:2", "added:2" })
            );
        }

        [Test]
        public void FailuresRunEveryObserverAndPreserveRegistrationExceptionOrder()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            InvalidOperationException first = new InvalidOperationException("first");
            ApplicationException second = new ApplicationException("second");
            CountingObserver completed = new CountingObserver();
            dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(new ThrowingObserver(first));
            dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(new ThrowingObserver(second));
            dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(completed);

            AggregateException failure = Assert.ThrowsAsync<AggregateException>(async () =>
                await dispatcher.Dispatch(new ObservedOp(1, OpStatus.Resolved))
            );

            Assert.That(
                failure.Message,
                Does.StartWith(
                    "Multiple resolved-operation observers failed after the operation resolved."
                )
            );
            Assert.That(failure.InnerExceptions, Is.EqualTo(new Exception[] { first, second }));
            Assert.That(completed.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task CallbackReceivesCurrentSnapshotAndCannotDispatchThroughTheActiveRoot()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            DispatchAttemptObserver observer = new DispatchAttemptObserver(dispatcher);
            dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(observer);

            await dispatcher.Dispatch(new ObservedOp(1, OpStatus.Resolved));

            Assert.That(observer.Snapshot, Is.SameAs(dispatcher.Snapshot));
            Assert.That(observer.DispatchFailure, Is.Not.Null);
            Assert.That(observer.DispatchFailure.Message, Does.Contain("active resolution"));
        }

        private static RuleDispatcher CreateDispatcher(List<string> order = null)
        {
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("resolved-observer-binding"),
                Definition,
                Owner,
                default,
                Source,
                0
            );
            RulesStateSeed seed = new RulesStateSeed().SeedRuleBinding(binding);
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry
                .Define(Definition)
                .Middleware(RuleLifecyclePhase.Transformation, new StatusMiddleware());
            return new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
                .UseRuleRegistry(registry.Build())
                .RegisterHandler<ObservedOp, int>(new ObservedHandler(order))
                .RegisterHandler<OtherOp, int>(new OtherHandler())
                .RegisterHandler<ParentOp, int>(new ParentHandler(order))
                .Build();
        }

        private sealed class ObservedOp : IRuleOp<int>
        {
            public ObservedOp(int value, OpStatus status)
            {
                Value = value;
                Status = status;
            }

            public int Value { get; }
            public OpStatus Status { get; }
        }

        private sealed class OtherOp : IRuleOp<int>
        {
            public OtherOp(int value) => Value = value;

            public int Value { get; }
        }

        private sealed class ParentOp : IRuleOp<int> { }

        private sealed class ObservedHandler : IOpHandler<ObservedOp, int>
        {
            private readonly List<string> order;

            public ObservedHandler(List<string> order) => this.order = order;

            public ValueTask<int> Handle(OpFrame<ObservedOp> frame, OpHandlerContext context)
            {
                order?.Add("child-handler");
                return new ValueTask<int>(frame.Op.Value);
            }
        }

        private sealed class OtherHandler : IOpHandler<OtherOp, int>
        {
            public ValueTask<int> Handle(OpFrame<OtherOp> frame, OpHandlerContext context) =>
                new ValueTask<int>(frame.Op.Value);
        }

        private sealed class ParentHandler : IOpHandler<ParentOp, int>
        {
            private readonly List<string> order;

            public ParentHandler(List<string> order) => this.order = order;

            public async ValueTask<int> Handle(OpFrame<ParentOp> frame, OpHandlerContext context)
            {
                ResolvedOpResult<int> child =
                    (ResolvedOpResult<int>)
                        await context.Dispatch(new ObservedOp(7, OpStatus.Resolved));
                order?.Add("parent-after-child");
                return child.Value;
            }
        }

        private sealed class StatusMiddleware : IOpMiddleware<ObservedOp, int>
        {
            public ValueTask<OpResult<int>> Invoke(
                OpFrame<ObservedOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            ) =>
                frame.Op.Status switch
                {
                    OpStatus.Invalid => new ValueTask<OpResult<int>>(
                        OpResult<int>.Invalid("invalid")
                    ),
                    OpStatus.Interrupted => new ValueTask<OpResult<int>>(
                        OpResult<int>.Interrupted()
                    ),
                    OpStatus.Cancelled => new ValueTask<OpResult<int>>(OpResult<int>.Cancelled()),
                    _ => next(),
                };
        }

        private sealed class RecordingObserver : IResolvedOpObserver<ObservedOp, int>
        {
            private readonly string name;
            private readonly List<string> order;

            public RecordingObserver(string name, List<string> order)
            {
                this.name = name;
                this.order = order;
            }

            public List<int> Values { get; } = new List<int>();

            public ValueTask OnOperationResolved(
                ObservedOp operation,
                int result,
                RulesSnapshot currentSnapshot
            )
            {
                Values.Add(result);
                order.Add($"{name}:{result}");
                return default;
            }
        }

        private sealed class ParentObserver : IResolvedOpObserver<ParentOp, int>
        {
            private readonly List<string> order;

            public ParentObserver(List<string> order) => this.order = order;

            public ValueTask OnOperationResolved(
                ParentOp operation,
                int result,
                RulesSnapshot currentSnapshot
            )
            {
                order.Add($"parent-observer:{result}");
                return default;
            }
        }

        private sealed class MutatingObserver : IResolvedOpObserver<ObservedOp, int>
        {
            private readonly RuleDispatcher dispatcher;
            private readonly RecordingObserver removed;
            private readonly RecordingObserver added;
            private readonly List<string> order;
            private bool changed;

            public MutatingObserver(
                RuleDispatcher dispatcher,
                RecordingObserver removed,
                RecordingObserver added,
                List<string> order
            )
            {
                this.dispatcher = dispatcher;
                this.removed = removed;
                this.added = added;
                this.order = order;
            }

            public ValueTask OnOperationResolved(
                ObservedOp operation,
                int result,
                RulesSnapshot currentSnapshot
            )
            {
                order.Add($"mutating:{result}");
                if (!changed)
                {
                    changed = true;
                    dispatcher.UnregisterResolvedOpObserver<ObservedOp, int>(removed);
                    dispatcher.RegisterResolvedOpObserver<ObservedOp, int>(added);
                }
                return default;
            }
        }

        private sealed class ThrowingObserver : IResolvedOpObserver<ObservedOp, int>
        {
            private readonly Exception exception;

            public ThrowingObserver(Exception exception) => this.exception = exception;

            public ValueTask OnOperationResolved(
                ObservedOp operation,
                int result,
                RulesSnapshot currentSnapshot
            ) => throw exception;
        }

        private sealed class CountingObserver : IResolvedOpObserver<ObservedOp, int>
        {
            public int Count { get; private set; }

            public ValueTask OnOperationResolved(
                ObservedOp operation,
                int result,
                RulesSnapshot currentSnapshot
            )
            {
                Count++;
                return default;
            }
        }

        private sealed class DispatchAttemptObserver : IResolvedOpObserver<ObservedOp, int>
        {
            private readonly RuleDispatcher dispatcher;

            public DispatchAttemptObserver(RuleDispatcher dispatcher) =>
                this.dispatcher = dispatcher;

            public RulesSnapshot Snapshot { get; private set; }
            public InvalidOperationException DispatchFailure { get; private set; }

            public async ValueTask OnOperationResolved(
                ObservedOp operation,
                int result,
                RulesSnapshot currentSnapshot
            )
            {
                Snapshot = currentSnapshot;
                try
                {
                    await dispatcher.Dispatch(new OtherOp(2));
                }
                catch (InvalidOperationException exception)
                {
                    DispatchFailure = exception;
                }
            }
        }
    }
}
