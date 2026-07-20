using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies generic typed workflow execution, composition, and structural outcomes.</summary>
    public sealed class SelectionWorkflowTests
    {
        private static readonly CreatureId Actor = new CreatureId("selection-actor");
        private static readonly CreatureId Target = new CreatureId("selection-target");
        private static readonly CreatureId Other = new CreatureId("selection-other");

        /// <summary>Verifies a one-click definition creates exactly the typed root operation.</summary>
        [Test]
        public async Task OneClickDefinitionCreatesExactImmutableRootOperation()
        {
            TestActionDefinition definition = new TestActionDefinition(Target);
            RulesSnapshot snapshot = new RulesState(new RulesStateSeed()).Snapshot;
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                SelectionOutcome<CreatureId>.Completed(Target)
            );

            ActionAvailability availability = definition.GetAvailability(snapshot, Actor);
            SelectionOutcome<CreatureId> outcome = await definition
                .CreateSelectionWorkflow(snapshot, Actor)
                .Run(resolver);
            TestActionOp operation = definition.CreateOp(Actor, RequireCompleted(outcome));

            Assert.That(availability, Is.SameAs(ActionAvailability.Available));
            Assert.That(operation.Actor, Is.EqualTo(Actor));
            Assert.That(operation.Target, Is.EqualTo(Target));
            Assert.That(resolver.Requests, Has.Count.EqualTo(1));
            Assert.That(resolver.Remaining, Is.Zero);
        }

        /// <summary>Verifies dependent requests run in order and preserve both typed values.</summary>
        [Test]
        public async Task OrderedChoicesRunInSequence()
        {
            TestActionSelectionRequest<int> firstRequest = new TestActionSelectionRequest<int>(
                "positive-number",
                value => value > 0
            );
            TestActionSelectionRequest<string> secondRequest =
                new TestActionSelectionRequest<string>(
                    "matching-label",
                    value => value == "chosen"
                );
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                SelectionOutcome<int>.Completed(3),
                SelectionOutcome<string>.Completed("chosen")
            );
            SelectionWorkflow<OrderedSelection<int, string>> workflow = SelectionWorkflow
                .From(firstRequest)
                .Then(_ => SelectionWorkflow.From(secondRequest));

            OrderedSelection<int, string> selection = RequireCompleted(
                await workflow.Run(resolver)
            );

            Assert.That(selection.First, Is.EqualTo(3));
            Assert.That(selection.Second, Is.EqualTo("chosen"));
            Assert.That(
                resolver.Requests,
                Is.EqualTo(new object[] { firstRequest, secondRequest })
            );
        }

        /// <summary>Verifies projection runs only after a completed selection.</summary>
        [Test]
        public async Task SelectProjectsCompletedValue()
        {
            TestActionSelectionRequest<int> request = new TestActionSelectionRequest<int>(
                "projection-input",
                value => value == 4
            );
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                SelectionOutcome<int>.Completed(4)
            );

            string selection = RequireCompleted(
                await SelectionWorkflow
                    .From(request)
                    .Select(value => $"value-{value}")
                    .Run(resolver)
            );

            Assert.That(selection, Is.EqualTo("value-4"));
        }

        /// <summary>Verifies explicit cancellation discards partial data and skips later work.</summary>
        [Test]
        public async Task CancellationShortCircuitsDependentSelection()
        {
            TestActionSelectionRequest<CreatureId> firstRequest =
                new TestActionSelectionRequest<CreatureId>(
                    "cancel-first",
                    value => value == Target
                );
            TestActionSelectionRequest<bool> secondRequest = new TestActionSelectionRequest<bool>(
                "never-run",
                _ => true
            );
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                SelectionOutcome<CreatureId>.Cancelled,
                SelectionOutcome<bool>.Completed(true)
            );
            SelectionWorkflow<OrderedSelection<CreatureId, bool>> workflow = SelectionWorkflow
                .From(firstRequest)
                .Then(_ => SelectionWorkflow.From(secondRequest));

            SelectionOutcome<OrderedSelection<CreatureId, bool>> outcome = await workflow.Run(
                resolver
            );

            Assert.That(
                outcome,
                Is.TypeOf<CancelledSelectionOutcome<OrderedSelection<CreatureId, bool>>>()
            );
            Assert.That(resolver.Requests, Is.EqualTo(new object[] { firstRequest }));
            Assert.That(resolver.Remaining, Is.EqualTo(1));
        }

        /// <summary>Verifies a resolver cannot complete a request with a value it did not offer.</summary>
        [Test]
        public async Task OutOfRequestValueBecomesInvalidAndSkipsDependentSelection()
        {
            TestActionSelectionRequest<CreatureId> firstRequest =
                new TestActionSelectionRequest<CreatureId>(
                    "restricted-target",
                    value => value == Target
                );
            TestActionSelectionRequest<bool> secondRequest = new TestActionSelectionRequest<bool>(
                "never-run",
                _ => true
            );
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                SelectionOutcome<CreatureId>.Completed(Other),
                SelectionOutcome<bool>.Completed(true)
            );
            SelectionWorkflow<OrderedSelection<CreatureId, bool>> workflow = SelectionWorkflow
                .From(firstRequest)
                .Then(_ => SelectionWorkflow.From(secondRequest));

            SelectionOutcome<OrderedSelection<CreatureId, bool>> outcome = await workflow.Run(
                resolver
            );

            Assert.That(
                outcome,
                Is.TypeOf<InvalidSelectionOutcome<OrderedSelection<CreatureId, bool>>>()
            );
            Assert.That(
                ((InvalidSelectionOutcome<OrderedSelection<CreatureId, bool>>)outcome).Reason,
                Does.Contain("outside the request")
            );
            Assert.That(resolver.Requests, Is.EqualTo(new object[] { firstRequest }));
            Assert.That(resolver.Remaining, Is.EqualTo(1));
        }

        /// <summary>Verifies a resolver's structural invalid result is preserved unchanged.</summary>
        [Test]
        public async Task InvalidOutcomeShortCircuitsDependentSelection()
        {
            TestActionSelectionRequest<int> firstRequest = new TestActionSelectionRequest<int>(
                "invalid-first",
                _ => true
            );
            TestActionSelectionRequest<bool> secondRequest = new TestActionSelectionRequest<bool>(
                "never-run",
                _ => true
            );
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                SelectionOutcome<int>.Invalid("No legal choice remains."),
                SelectionOutcome<bool>.Completed(true)
            );

            SelectionOutcome<OrderedSelection<int, bool>> outcome = await SelectionWorkflow
                .From(firstRequest)
                .Then(_ => SelectionWorkflow.From(secondRequest))
                .Run(resolver);

            Assert.That(
                ((InvalidSelectionOutcome<OrderedSelection<int, bool>>)outcome).Reason,
                Is.EqualTo("No legal choice remains.")
            );
            Assert.That(resolver.Requests, Is.EqualTo(new object[] { firstRequest }));
            Assert.That(resolver.Remaining, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies cancellation discards a late resolver result and never begins a dependent step.
        /// </summary>
        [Test]
        public async Task CancellationTokenDiscardsLateResultAndSkipsDependentSelection()
        {
            TestActionSelectionRequest<int> firstRequest = new TestActionSelectionRequest<int>(
                "pending-first",
                value => value == 1
            );
            TestActionSelectionRequest<bool> secondRequest = new TestActionSelectionRequest<bool>(
                "never-run",
                _ => true
            );
            TaskCompletionSource<SelectionOutcome<int>> pending =
                new TaskCompletionSource<SelectionOutcome<int>>();
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                pending.Task,
                SelectionOutcome<bool>.Completed(true)
            );
            SelectionWorkflow<OrderedSelection<int, bool>> workflow = SelectionWorkflow
                .From(firstRequest)
                .Then(_ => SelectionWorkflow.From(secondRequest));

            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Task<SelectionOutcome<OrderedSelection<int, bool>>> execution = workflow
                    .Run(resolver, cancellation.Token)
                    .AsTask();

                cancellation.Cancel();
                pending.SetResult(SelectionOutcome<int>.Completed(1));
                SelectionOutcome<OrderedSelection<int, bool>> outcome = await execution;

                Assert.That(
                    outcome,
                    Is.TypeOf<CancelledSelectionOutcome<OrderedSelection<int, bool>>>()
                );
                Assert.That(resolver.Requests, Is.EqualTo(new object[] { firstRequest }));
                Assert.That(resolver.Remaining, Is.EqualTo(1));
            }
        }

        /// <summary>Verifies a token cancelled before execution invokes no resolver.</summary>
        [Test]
        public async Task PreCancelledWorkflowDoesNotInvokeResolver()
        {
            TestActionSelectionRequest<int> request = new TestActionSelectionRequest<int>(
                "pre-cancelled",
                _ => true
            );
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                SelectionOutcome<int>.Completed(1)
            );
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                SelectionOutcome<int> outcome = await SelectionWorkflow
                    .From(request)
                    .Run(resolver, cancellation.Token);

                Assert.That(outcome, Is.TypeOf<CancelledSelectionOutcome<int>>());
                Assert.That(resolver.Requests, Is.Empty);
                Assert.That(resolver.Remaining, Is.EqualTo(1));
            }
        }

        /// <summary>Verifies an immediately invalid workflow invokes no resolver.</summary>
        [Test]
        public async Task ImmediatelyInvalidWorkflowInvokesNoResolver()
        {
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver(
                SelectionOutcome<int>.Completed(1)
            );

            SelectionOutcome<int> outcome = await SelectionWorkflow
                .Invalid<int>("Preview cannot produce a legal request.")
                .Run(resolver);

            Assert.That(
                ((InvalidSelectionOutcome<int>)outcome).Reason,
                Is.EqualTo("Preview cannot produce a legal request.")
            );
            Assert.That(resolver.Requests, Is.Empty);
            Assert.That(resolver.Remaining, Is.EqualTo(1));
        }

        /// <summary>Verifies a broken resolver cannot represent absence as a valid outcome.</summary>
        [Test]
        public void MissingResolverOutcomeThrows()
        {
            TestActionSelectionRequest<int> request = new TestActionSelectionRequest<int>(
                "missing-outcome",
                _ => true
            );
            ScriptedSelectionResolver resolver = new ScriptedSelectionResolver((object)null);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await SelectionWorkflow.From(request).Run(resolver)
            );
        }

        private static T RequireCompleted<T>(SelectionOutcome<T> outcome)
        {
            Assert.That(outcome, Is.TypeOf<CompletedSelectionOutcome<T>>());
            return ((CompletedSelectionOutcome<T>)outcome).Selection;
        }

        private readonly struct TestActionOutcome { }

        private sealed class TestActionOp : ActionOp<TestActionOutcome>
        {
            public TestActionOp(CreatureId actor, CreatureId target)
                : base(actor, new ActionDefinitionId("test-one-click")) => Target = target;

            public CreatureId Target { get; }
        }

        private sealed class TestActionDefinition
            : IActionDefinition<CreatureId, TestActionOp, TestActionOutcome>
        {
            private readonly CreatureId target;

            public TestActionDefinition(CreatureId target) => this.target = target;

            public ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor)
            {
                if (snapshot == null)
                    throw new ArgumentNullException(nameof(snapshot));
                if (actor.IsEmpty)
                    throw new ArgumentException("An actor is required.", nameof(actor));
                return ActionAvailability.Available;
            }

            public SelectionWorkflow<CreatureId> CreateSelectionWorkflow(
                RulesSnapshot snapshot,
                CreatureId actor
            ) =>
                SelectionWorkflow.From(
                    new TestActionSelectionRequest<CreatureId>(
                        "test-one-click-target",
                        selection => selection == target
                    )
                );

            public TestActionOp CreateOp(CreatureId actor, CreatureId selection) =>
                new TestActionOp(actor, selection);
        }
    }
}
