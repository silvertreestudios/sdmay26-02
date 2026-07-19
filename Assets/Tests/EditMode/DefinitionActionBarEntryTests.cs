using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;

namespace Game.Rules.Unity.Tests
{
    /// <summary>
    /// Verifies the type-erased definition entry without weakening its typed selection-to-Op path.
    /// </summary>
    public sealed class DefinitionActionBarEntryTests
    {
        private static readonly CreatureId Actor = new CreatureId("action-entry-actor");
        private static readonly ActionDefinitionId DefinitionId = new ActionDefinitionId(
            "action-entry-test"
        );
        private static readonly ActionDefinitionId CompetingDefinitionId = new ActionDefinitionId(
            "competing-action-entry-test"
        );
        private static readonly SelectionRequestId ConfirmationRequestId = new SelectionRequestId(
            "confirm-action-entry"
        );

        /// <summary>
        /// Verifies completed AI selection creates and dispatches the definition's exact Op once.
        /// </summary>
        [Test]
        public async Task CompletedAiSelectionCreatesAndDispatchesExactOperationOnce()
        {
            InMemoryRulesStore store = CreateStore();
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = BuildDispatcher(store, handler);
            RecordingDefinition definition = new RecordingDefinition();
            TestAiPlanner planner = new TestAiPlanner(() =>
                SelectionOutcome<ConfirmationSelection>.Completed(new ConfirmationSelection(true))
            );
            DefinitionActionBarEntry<TestSelection, TestActionOp, TestActionResult> entry =
                CreateEntry(definition, dispatcher, new AiSelectionAdapter(planner));

            ActionBarExecutionOutcome outcome = await entry.Execute();

            Assert.That(outcome, Is.TypeOf<DispatchedActionBarExecutionOutcome>());
            Assert.That(
                ((DispatchedActionBarExecutionOutcome)outcome).Status,
                Is.EqualTo(OpStatus.Resolved)
            );
            Assert.That(definition.CreateOpCalls, Is.EqualTo(1));
            Assert.That(handler.Calls, Is.EqualTo(1));
            Assert.That(
                handler.Operations.Single(),
                Is.SameAs(definition.CreatedOperations.Single())
            );
            Assert.That(handler.Operations.Single().Selection.Confirmed, Is.True);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies cancellation never creates a root operation or changes authoritative state.
        /// </summary>
        [Test]
        public async Task CancelledSelectionDoesNotCreateOrDispatchOperation()
        {
            InMemoryRulesStore store = CreateStore();
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = BuildDispatcher(store, handler);
            RecordingDefinition definition = new RecordingDefinition();
            DefinitionActionBarEntry<TestSelection, TestActionOp, TestActionResult> entry =
                CreateEntry(
                    definition,
                    dispatcher,
                    new AiSelectionAdapter(
                        new TestAiPlanner(() => SelectionOutcome<ConfirmationSelection>.Cancelled)
                    )
                );

            ActionBarExecutionOutcome outcome = await entry.Execute();

            Assert.That(outcome, Is.TypeOf<CancelledActionBarExecutionOutcome>());
            Assert.That(definition.CreateOpCalls, Is.Zero);
            Assert.That(handler.Calls, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies invalid selection stops before operation creation and root dispatch.
        /// </summary>
        [Test]
        public async Task InvalidSelectionDoesNotCreateOrDispatchOperation()
        {
            InMemoryRulesStore store = CreateStore();
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = BuildDispatcher(store, handler);
            RecordingDefinition definition = new RecordingDefinition();
            DefinitionActionBarEntry<TestSelection, TestActionOp, TestActionResult> entry =
                CreateEntry(
                    definition,
                    dispatcher,
                    new AiSelectionAdapter(
                        new TestAiPlanner(() =>
                            SelectionOutcome<ConfirmationSelection>.Invalid(
                                "AI could not complete the choice"
                            )
                        )
                    )
                );

            ActionBarExecutionOutcome outcome = await entry.Execute();

            Assert.That(outcome, Is.TypeOf<InvalidActionBarExecutionOutcome>());
            InvalidActionBarExecutionOutcome invalid = (InvalidActionBarExecutionOutcome)outcome;
            Assert.That(invalid.Source, Is.EqualTo(ActionBarInvalidSource.Selection));
            Assert.That(invalid.Reason, Is.EqualTo("AI could not complete the choice"));
            Assert.That(definition.CreateOpCalls, Is.Zero);
            Assert.That(handler.Calls, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
        }

        /// <summary>
        /// Verifies dispatcher validation remains authoritative when preview state becomes stale during selection.
        /// </summary>
        [Test]
        public async Task StalePreviewReturnsAuthoritativeInvalidWithoutPayingCost()
        {
            InMemoryRulesStore store = CreateStore(1);
            RecordingHandler handler = new RecordingHandler();
            CompetingActionHandler competingHandler = new CompetingActionHandler();
            RuleDispatcher dispatcher = BuildDispatcher(store, handler, competingHandler);
            RecordingDefinition definition = new RecordingDefinition();
            DefinitionActionBarEntry<TestSelection, TestActionOp, TestActionResult> entry =
                CreateEntry(
                    definition,
                    dispatcher,
                    new CompetingDispatchSelectionAdapter(dispatcher)
                );

            Assert.That(entry.GetAvailability(), Is.TypeOf<AvailableActionAvailability>());
            ActionBarExecutionOutcome outcome = await entry.Execute();

            Assert.That(outcome, Is.TypeOf<InvalidActionBarExecutionOutcome>());
            InvalidActionBarExecutionOutcome invalid = (InvalidActionBarExecutionOutcome)outcome;
            Assert.That(invalid.Source, Is.EqualTo(ActionBarInvalidSource.Dispatcher));
            Assert.That(invalid.Reason, Does.Contain("insufficient actions"));
            Assert.That(definition.CreateOpCalls, Is.EqualTo(1));
            Assert.That(handler.Calls, Is.Zero);
            Assert.That(competingHandler.Calls, Is.EqualTo(1));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies availability is recomputed from the current dispatcher snapshot after a cost Fact commits.
        /// </summary>
        [Test]
        public async Task AvailabilityRefreshesFromSnapshotAfterCompetingActionCommits()
        {
            InMemoryRulesStore store = CreateStore(1);
            RecordingHandler handler = new RecordingHandler();
            CompetingActionHandler competingHandler = new CompetingActionHandler();
            RuleDispatcher dispatcher = BuildDispatcher(store, handler, competingHandler);
            RecordingDefinition definition = new RecordingDefinition();
            DefinitionActionBarEntry<TestSelection, TestActionOp, TestActionResult> entry =
                CreateEntry(
                    definition,
                    dispatcher,
                    new AiSelectionAdapter(
                        new TestAiPlanner(() =>
                            SelectionOutcome<ConfirmationSelection>.Completed(
                                new ConfirmationSelection(true)
                            )
                        )
                    )
                );

            Assert.That(entry.GetAvailability(), Is.TypeOf<AvailableActionAvailability>());
            OpResult<CompetingActionResult> drain = await dispatcher.Dispatch(
                new CompetingActionOp(Actor)
            );
            ActionAvailability refreshed = entry.GetAvailability();

            Assert.That(drain, Is.TypeOf<ResolvedOpResult<CompetingActionResult>>());
            Assert.That(refreshed, Is.TypeOf<UnavailableActionAvailability>());
            Assert.That(
                ((UnavailableActionAvailability)refreshed).Reason,
                Is.EqualTo("No actions remain")
            );
            Assert.That(store.Snapshot.Version, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an unavailable current preview stops before asking the selection adapter for input.
        /// </summary>
        [Test]
        public async Task UnavailablePreviewStopsBeforeSelection()
        {
            InMemoryRulesStore store = CreateStore();
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = BuildDispatcher(store, handler);
            RecordingDefinition definition = new RecordingDefinition(
                ActionAvailability.Unavailable("no legal target")
            );
            TestAiPlanner planner = new TestAiPlanner(() =>
                SelectionOutcome<ConfirmationSelection>.Completed(new ConfirmationSelection(true))
            );
            DefinitionActionBarEntry<TestSelection, TestActionOp, TestActionResult> entry =
                CreateEntry(definition, dispatcher, new AiSelectionAdapter(planner));

            ActionBarExecutionOutcome outcome = await entry.Execute();

            Assert.That(outcome, Is.TypeOf<UnavailableActionBarExecutionOutcome>());
            Assert.That(
                ((UnavailableActionBarExecutionOutcome)outcome).Reason,
                Is.EqualTo("no legal target")
            );
            Assert.That(planner.ConfirmationCalls, Is.Zero);
            Assert.That(definition.CreateOpCalls, Is.Zero);
        }

        private static DefinitionActionBarEntry<
            TestSelection,
            TestActionOp,
            TestActionResult
        > CreateEntry(
            RecordingDefinition definition,
            RuleDispatcher dispatcher,
            ISelectionAdapter adapter
        ) =>
            new DefinitionActionBarEntry<TestSelection, TestActionOp, TestActionResult>(
                new ActionBarEntryKey("test-action"),
                "Test action",
                definition,
                Actor,
                dispatcher,
                adapter
            );

        private static InMemoryRulesStore CreateStore(int actions = 3) =>
            new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(actions, true))
            );

        private static RuleDispatcher BuildDispatcher(
            InMemoryRulesStore store,
            RecordingHandler handler,
            CompetingActionHandler competingHandler
        ) =>
            new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .RegisterHandler<CompetingActionOp, CompetingActionResult>(competingHandler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();

        private static RuleDispatcher BuildDispatcher(
            InMemoryRulesStore store,
            RecordingHandler handler
        ) =>
            new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();

        private sealed class TestSelection
        {
            public TestSelection(bool confirmed) => Confirmed = confirmed;

            public bool Confirmed { get; }
        }

        private sealed class TestActionResult { }

        private sealed class CompetingActionResult { }

        private sealed class TestActionOp : ActionOp<TestActionResult>
        {
            public TestActionOp(CreatureId actor, TestSelection selection)
                : base(actor, DefinitionActionBarEntryTests.DefinitionId) =>
                Selection = selection ?? throw new ArgumentNullException(nameof(selection));

            public TestSelection Selection { get; }
        }

        private sealed class CompetingActionOp : ActionOp<CompetingActionResult>
        {
            public CompetingActionOp(CreatureId actor)
                : base(actor, CompetingDefinitionId) { }
        }

        private sealed class RecordingDefinition
            : IActionDefinition<TestSelection, TestActionOp, TestActionResult>
        {
            private readonly ActionAvailability availability;

            public RecordingDefinition()
                : this(ActionAvailability.Available) { }

            public RecordingDefinition(ActionAvailability availability) =>
                this.availability =
                    availability ?? throw new ArgumentNullException(nameof(availability));

            public int CreateOpCalls { get; private set; }

            public List<TestActionOp> CreatedOperations { get; } = new List<TestActionOp>();

            public ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor)
            {
                if (availability is UnavailableActionAvailability)
                    return availability;

                return
                    snapshot.ActionEconomy.TryGet(actor, out ActionEconomyState economy)
                    && economy.ActionsRemaining > 0
                    ? ActionAvailability.Available
                    : ActionAvailability.Unavailable("No actions remain");
            }

            public SelectionWorkflow<TestSelection> CreateSelectionWorkflow(
                RulesSnapshot snapshot,
                CreatureId actor
            ) =>
                SelectionWorkflow
                    .From(new ConfirmationSelectionRequest(ConfirmationRequestId))
                    .Select(selection => new TestSelection(selection.IsConfirmed));

            public TestActionOp CreateOp(CreatureId actor, TestSelection selection)
            {
                CreateOpCalls++;
                TestActionOp operation = new TestActionOp(actor, selection);
                CreatedOperations.Add(operation);
                return operation;
            }
        }

        private sealed class RecordingHandler : IOpHandler<TestActionOp, TestActionResult>
        {
            public int Calls { get; private set; }

            public List<TestActionOp> Operations { get; } = new List<TestActionOp>();

            public ValueTask<TestActionResult> Handle(
                OpFrame<TestActionOp> frame,
                OpHandlerContext context
            )
            {
                Calls++;
                Operations.Add(frame.Op);
                return new ValueTask<TestActionResult>(new TestActionResult());
            }
        }

        private sealed class CompetingActionHandler
            : IOpHandler<CompetingActionOp, CompetingActionResult>
        {
            public int Calls { get; private set; }

            public ValueTask<CompetingActionResult> Handle(
                OpFrame<CompetingActionOp> frame,
                OpHandlerContext context
            )
            {
                Calls++;
                return new ValueTask<CompetingActionResult>(new CompetingActionResult());
            }
        }

        private sealed class FixedActionCatalog : IActionCatalog
        {
            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
            {
                Assert.That(
                    definitionId,
                    Is.EqualTo(DefinitionId).Or.EqualTo(CompetingDefinitionId)
                );
                return ActionProfile.OneAction(Array.Empty<Trait>());
            }
        }

        private sealed class TestAiPlanner : IAiSelectionPlanner
        {
            private readonly Func<SelectionOutcome<ConfirmationSelection>> confirm;

            public TestAiPlanner(Func<SelectionOutcome<ConfirmationSelection>> confirm) =>
                this.confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));

            public int ConfirmationCalls { get; private set; }

            public SelectionOutcome<ConfirmationSelection> Confirm(
                ConfirmationSelectionRequest request
            )
            {
                ConfirmationCalls++;
                return confirm();
            }

            public SelectionOutcome<CreatureSelection> SelectCreature(
                CreatureSelectionRequest request
            ) => SelectionOutcome<CreatureSelection>.Invalid("unused");

            public SelectionOutcome<MultipleCreatureSelection> SelectCreatures(
                MultipleCreatureSelectionRequest request
            ) => SelectionOutcome<MultipleCreatureSelection>.Invalid("unused");

            public SelectionOutcome<ItemSelection> SelectItem(ItemSelectionRequest request) =>
                SelectionOutcome<ItemSelection>.Invalid("unused");

            public SelectionOutcome<WeaponSelection> SelectWeapon(WeaponSelectionRequest request) =>
                SelectionOutcome<WeaponSelection>.Invalid("unused");

            public SelectionOutcome<PathSelection> SelectPath(PathSelectionRequest request) =>
                SelectionOutcome<PathSelection>.Invalid("unused");

            public SelectionOutcome<GridCellSelection> SelectGridCell(
                GridCellSelectionRequest request
            ) => SelectionOutcome<GridCellSelection>.Invalid("unused");

            public SelectionOutcome<AreaSelection> SelectArea(AreaSelectionRequest request) =>
                SelectionOutcome<AreaSelection>.Invalid("unused");

            public SelectionOutcome<SpellVariantSelection> SelectSpellVariant(
                SpellVariantSelectionRequest request
            ) => SelectionOutcome<SpellVariantSelection>.Invalid("unused");

            public SelectionOutcome<SpellSlotSelection> SelectSpellSlot(
                SpellSlotSelectionRequest request
            ) => SelectionOutcome<SpellSlotSelection>.Invalid("unused");
        }

        private sealed class CompetingDispatchSelectionAdapter : ISelectionAdapter
        {
            private readonly RuleDispatcher dispatcher;

            public CompetingDispatchSelectionAdapter(RuleDispatcher dispatcher) =>
                this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            public async ValueTask<SelectionOutcome<ConfirmationSelection>> Confirm(
                ConfirmationSelectionRequest request
            )
            {
                OpResult<CompetingActionResult> result = await dispatcher.Dispatch(
                    new CompetingActionOp(Actor)
                );
                Assert.That(result, Is.TypeOf<ResolvedOpResult<CompetingActionResult>>());
                return SelectionOutcome<ConfirmationSelection>.Completed(
                    new ConfirmationSelection(true)
                );
            }

            public ValueTask<SelectionOutcome<CreatureSelection>> SelectCreature(
                CreatureSelectionRequest request
            ) => Unused<CreatureSelection>();

            public ValueTask<SelectionOutcome<MultipleCreatureSelection>> SelectCreatures(
                MultipleCreatureSelectionRequest request
            ) => Unused<MultipleCreatureSelection>();

            public ValueTask<SelectionOutcome<ItemSelection>> SelectItem(
                ItemSelectionRequest request
            ) => Unused<ItemSelection>();

            public ValueTask<SelectionOutcome<WeaponSelection>> SelectWeapon(
                WeaponSelectionRequest request
            ) => Unused<WeaponSelection>();

            public ValueTask<SelectionOutcome<PathSelection>> SelectPath(
                PathSelectionRequest request
            ) => Unused<PathSelection>();

            public ValueTask<SelectionOutcome<GridCellSelection>> SelectGridCell(
                GridCellSelectionRequest request
            ) => Unused<GridCellSelection>();

            public ValueTask<SelectionOutcome<AreaSelection>> SelectArea(
                AreaSelectionRequest request
            ) => Unused<AreaSelection>();

            public ValueTask<SelectionOutcome<SpellVariantSelection>> SelectSpellVariant(
                SpellVariantSelectionRequest request
            ) => Unused<SpellVariantSelection>();

            public ValueTask<SelectionOutcome<SpellSlotSelection>> SelectSpellSlot(
                SpellSlotSelectionRequest request
            ) => Unused<SpellSlotSelection>();

            private static ValueTask<SelectionOutcome<TSelection>> Unused<TSelection>() =>
                new ValueTask<SelectionOutcome<TSelection>>(
                    SelectionOutcome<TSelection>.Invalid("unused")
                );
        }
    }
}
