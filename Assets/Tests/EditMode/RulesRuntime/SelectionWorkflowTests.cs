using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies typed workflow execution, composition, and structural outcomes.</summary>
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
            ScriptedSelectionAdapter adapter = new ScriptedSelectionAdapter(
                SelectionOutcome<CreatureSelection>.Completed(new CreatureSelection(Target))
            );

            ActionAvailability availability = definition.GetAvailability(snapshot, Actor);
            SelectionOutcome<CreatureSelection> outcome = await definition
                .CreateSelectionWorkflow(snapshot, Actor)
                .Run(adapter);
            CreatureSelection selection = RequireCompleted(outcome);
            TestActionOp op = definition.CreateOp(Actor, selection);

            Assert.That(availability, Is.SameAs(ActionAvailability.Available));
            Assert.That(op.Actor, Is.EqualTo(Actor));
            Assert.That(op.Target, Is.EqualTo(Target));
            Assert.That(adapter.Requests, Has.Count.EqualTo(1));
            Assert.That(adapter.Remaining, Is.Zero);
        }

        /// <summary>Verifies a path can determine a later target step and preserve both values.</summary>
        [Test]
        public async Task PathPlusTargetRunsInOrderAndBuildsTumbleThroughSelection()
        {
            GridPosition start = new GridPosition(0, 0, 0);
            GridPosition middle = new GridPosition(1, 0, 0);
            GridPosition destination = new GridPosition(2, 0, 0);
            SelectionRequestId pathId = new SelectionRequestId("tumble-path");
            SelectionRequestId targetId = new SelectionRequestId("tumble-enemy");
            PathSelection path = new PathSelection(new[] { start, middle, destination });
            ScriptedSelectionAdapter adapter = new ScriptedSelectionAdapter(
                SelectionOutcome<PathSelection>.Completed(path),
                SelectionOutcome<CreatureSelection>.Completed(new CreatureSelection(Target))
            );
            SelectionWorkflow<TumbleThroughSelection> workflow = SelectionWorkflow
                .From(new PathSelectionRequest(pathId, Actor, start, new[] { destination }, 3))
                .Then(_ =>
                    SelectionWorkflow.From(new CreatureSelectionRequest(targetId, new[] { Target }))
                )
                .Select(ordered => new TumbleThroughSelection(
                    ordered.First.Positions,
                    ordered.Second.Creature,
                    new MovementMode("land")
                ));

            TumbleThroughSelection selection = RequireCompleted(await workflow.Run(adapter));

            Assert.That(selection.Path, Is.EqualTo(new[] { start, middle, destination }));
            Assert.That(selection.Enemy, Is.EqualTo(Target));
            Assert.That(selection.Mode, Is.EqualTo(new MovementMode("land")));
            Assert.That(adapter.Requests, Is.EqualTo(new[] { pathId, targetId }));
        }

        /// <summary>Verifies area selection carries its template, origin, and facing without Unity.</summary>
        [Test]
        public async Task AreaWorkflowPreservesTemplateAndOrientation()
        {
            AreaTemplateId template = new AreaTemplateId("fifteen-foot-cone");
            GridPosition origin = new GridPosition(4, 0, 2);
            AreaOrientation orientation = new AreaOrientation(origin, new GridPosition(5, 0, 2));
            AreaSelection expected = new AreaSelection(template, orientation);
            ScriptedSelectionAdapter adapter = new ScriptedSelectionAdapter(
                SelectionOutcome<AreaSelection>.Completed(expected)
            );
            SelectionWorkflow<AreaSelection> workflow = SelectionWorkflow.From(
                new AreaSelectionRequest(
                    new SelectionRequestId("spell-area"),
                    new[] { template },
                    new[] { origin }
                )
            );

            AreaSelection actual = RequireCompleted(await workflow.Run(adapter));

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actual.Orientation.Facing, Is.EqualTo(new GridPosition(5, 0, 2)));
        }

        /// <summary>Verifies three ordered choices project into one structurally targeted spell.</summary>
        [Test]
        public async Task OrderedMultiChoiceBuildsStructurallyTargetedSpellSelection()
        {
            SpellSlotPoolId slot = new SpellSlotPoolId("wizard-rank-one");
            SpellVariantId variant = new SpellVariantId("force-barrage-one-action");
            MultipleCreatureSelection targets = new MultipleCreatureSelection(
                new[] { Target, Other }
            );
            SelectionRequestId slotId = new SelectionRequestId("spell-slot");
            SelectionRequestId variantId = new SelectionRequestId("spell-variant");
            SelectionRequestId targetsId = new SelectionRequestId("spell-targets");
            ScriptedSelectionAdapter adapter = new ScriptedSelectionAdapter(
                SelectionOutcome<SpellSlotSelection>.Completed(new SpellSlotSelection(slot)),
                SelectionOutcome<SpellVariantSelection>.Completed(
                    new SpellVariantSelection(variant)
                ),
                SelectionOutcome<MultipleCreatureSelection>.Completed(targets)
            );
            SelectionWorkflow<CastSpellSelection> workflow = SelectionWorkflow
                .From(new SpellSlotSelectionRequest(slotId, new[] { slot }))
                .Then(_ =>
                    SelectionWorkflow.From(
                        new SpellVariantSelectionRequest(variantId, new[] { variant })
                    )
                )
                .Then(_ =>
                    SelectionWorkflow.From(
                        new MultipleCreatureSelectionRequest(
                            targetsId,
                            new[] { Target, Other },
                            1,
                            2
                        )
                    )
                )
                .Select(ordered => new CastSpellSelection(
                    ordered.First.First.Pool,
                    ordered.First.Second.Variant,
                    new MultipleCreatureSpellTargetSelection(ordered.Second.Creatures)
                ));

            CastSpellSelection selection = RequireCompleted(await workflow.Run(adapter));

            Assert.That(selection.SlotPool, Is.EqualTo(slot));
            Assert.That(selection.Variant, Is.EqualTo(variant));
            Assert.That(selection.Targets, Is.TypeOf<MultipleCreatureSpellTargetSelection>());
            Assert.That(
                ((MultipleCreatureSpellTargetSelection)selection.Targets).Targets,
                Is.EqualTo(new[] { Target, Other })
            );
            Assert.That(adapter.Requests, Is.EqualTo(new[] { slotId, variantId, targetsId }));
        }

        /// <summary>Verifies cancellation discards partial data and skips every later step.</summary>
        [Test]
        public async Task CancellationShortCircuitsDependentSelection()
        {
            SelectionRequestId firstId = new SelectionRequestId("cancel-first");
            ScriptedSelectionAdapter adapter = new ScriptedSelectionAdapter(
                SelectionOutcome<CreatureSelection>.Cancelled,
                SelectionOutcome<ConfirmationSelection>.Completed(new ConfirmationSelection(true))
            );
            SelectionWorkflow<OrderedSelection<CreatureSelection, ConfirmationSelection>> workflow =
                SelectionWorkflow
                    .From(new CreatureSelectionRequest(firstId, new[] { Target }))
                    .Then(_ =>
                        SelectionWorkflow.From(
                            new ConfirmationSelectionRequest(
                                new SelectionRequestId("never-confirm")
                            )
                        )
                    );

            SelectionOutcome<OrderedSelection<CreatureSelection, ConfirmationSelection>> outcome =
                await workflow.Run(adapter);

            Assert.That(
                outcome,
                Is.TypeOf<
                    CancelledSelectionOutcome<
                        OrderedSelection<CreatureSelection, ConfirmationSelection>
                    >
                >()
            );
            Assert.That(adapter.Requests, Is.EqualTo(new[] { firstId }));
            Assert.That(adapter.Remaining, Is.EqualTo(1));
        }

        /// <summary>Verifies undeclared adapter values become invalid and skip later steps.</summary>
        [Test]
        public async Task InvalidSelectionShortCircuitsDependentSelection()
        {
            ScriptedSelectionAdapter adapter = new ScriptedSelectionAdapter(
                SelectionOutcome<CreatureSelection>.Completed(new CreatureSelection(Other)),
                SelectionOutcome<ConfirmationSelection>.Completed(new ConfirmationSelection(true))
            );
            SelectionWorkflow<OrderedSelection<CreatureSelection, ConfirmationSelection>> workflow =
                SelectionWorkflow
                    .From(
                        new CreatureSelectionRequest(
                            new SelectionRequestId("restricted-target"),
                            new[] { Target }
                        )
                    )
                    .Then(_ =>
                        SelectionWorkflow.From(
                            new ConfirmationSelectionRequest(
                                new SelectionRequestId("never-confirm")
                            )
                        )
                    );

            SelectionOutcome<OrderedSelection<CreatureSelection, ConfirmationSelection>> outcome =
                await workflow.Run(adapter);

            Assert.That(
                outcome,
                Is.TypeOf<
                    InvalidSelectionOutcome<
                        OrderedSelection<CreatureSelection, ConfirmationSelection>
                    >
                >()
            );
            Assert.That(
                (
                    (InvalidSelectionOutcome<
                        OrderedSelection<CreatureSelection, ConfirmationSelection>
                    >)
                        outcome
                ).Reason,
                Does.Contain("restricted-target")
            );
            Assert.That(adapter.Requests, Has.Count.EqualTo(1));
            Assert.That(adapter.Remaining, Is.EqualTo(1));
        }

        /// <summary>Verifies an explicit no is completed data rather than workflow cancellation.</summary>
        [Test]
        public async Task ConfirmationDeclineIsCompletedSelection()
        {
            ScriptedSelectionAdapter adapter = new ScriptedSelectionAdapter(
                SelectionOutcome<ConfirmationSelection>.Completed(new ConfirmationSelection(false))
            );

            ConfirmationSelection selection = RequireCompleted(
                await SelectionWorkflow
                    .From(
                        new ConfirmationSelectionRequest(new SelectionRequestId("confirm-action"))
                    )
                    .Run(adapter)
            );

            Assert.That(selection.IsConfirmed, Is.False);
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
            : IActionDefinition<CreatureSelection, TestActionOp, TestActionOutcome>
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

            public SelectionWorkflow<CreatureSelection> CreateSelectionWorkflow(
                RulesSnapshot snapshot,
                CreatureId actor
            ) =>
                SelectionWorkflow.From(
                    new CreatureSelectionRequest(
                        new SelectionRequestId("test-one-click-target"),
                        new[] { target }
                    )
                );

            public TestActionOp CreateOp(CreatureId actor, CreatureSelection selection) =>
                new TestActionOp(actor, selection.Creature);
        }
    }
}
