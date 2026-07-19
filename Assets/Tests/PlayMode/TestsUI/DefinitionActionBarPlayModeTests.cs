using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UniversalEvents;

namespace TestsUI
{
    /// <summary>
    /// Verifies the live UI Toolkit action bar can present legacy and typed definition entries
    /// together while each entry keeps its own authoritative execution model.
    /// </summary>
    public sealed class DefinitionActionBarPlayModeTests : PlayModeBase
    {
        private static readonly CreatureId Actor = new CreatureId("hud-definition-actor");
        private static readonly ActionDefinitionId DefinitionId = new ActionDefinitionId(
            "hud-definition-action"
        );

        /// <summary>
        /// Verifies stable-key replacement, mixed layout, typed dispatch, and snapshot-driven
        /// availability in the real combat HUD.
        /// </summary>
        [UnityTest]
        public IEnumerator MixedEntriesRenderAndDefinitionAvailabilityRefreshesAfterFact()
        {
            Button initialAction = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    player = CombatManagerInterface.GetInstance().WhosTurn();
                    initialAction = root.Q<Button>("UnarmedStrikeButton");
                    return player != null && initialAction != null;
                }
            );

            Assert.That(player, Is.Not.Null, "The current player was not ready.");
            Assert.That(initialAction, Is.Not.Null, "The initial legacy action bar was not ready.");
            ActionController controller = player.GetComponent<ActionController>();
            Assert.That(controller, Is.Not.Null, "The current player has no ActionController.");

            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(1, true))
            );
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();
            TestDefinition definition = new TestDefinition();
            AiSelectionAdapter adapter = new AiSelectionAdapter(new ConfirmationPlanner());
            ActionBarEntryKey replacementKey = new ActionBarEntryKey("test/shared-action");
            ActionBarEntryKey retainedLegacyKey = new ActionBarEntryKey(
                "test/retained-legacy-action"
            );

            controller.AddAction(replacementKey, new TestLegacyAction("Legacy duplicate"));
            controller.AddAction(retainedLegacyKey, new TestLegacyAction("Legacy retained"));
            controller.AddDefinitionAction(
                CreateEntry(replacementKey, "Rules replacement", definition, dispatcher, adapter)
            );
            controller.AddDefinitionAction(
                CreateEntry(
                    new ActionBarEntryKey("test/additional-definition"),
                    "Rules extra",
                    definition,
                    dispatcher,
                    adapter
                )
            );

            uint legacyActionPointsBefore = controller.ActionPoints;
            OnNextTurn.Invoke(player);

            Button replacementButton = null;
            Button retainedLegacyButton = null;
            Button extraDefinitionButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    replacementButton = root.Q<Button>("RulesreplacementButton");
                    retainedLegacyButton = root.Q<Button>("LegacyretainedButton");
                    extraDefinitionButton = root.Q<Button>("RulesextraButton");
                    return replacementButton != null
                        && retainedLegacyButton != null
                        && extraDefinitionButton != null;
                }
            );

            Assert.That(replacementButton, Is.Not.Null);
            Assert.That(retainedLegacyButton, Is.Not.Null);
            Assert.That(extraDefinitionButton, Is.Not.Null);
            Assert.That(root.Q<Button>("LegacyDuplicateButton"), Is.Null);
            Assert.That(CountButtonsNamed("RulesreplacementButton"), Is.EqualTo(1));
            Assert.That(CountButtonsNamed("LegacyretainedButton"), Is.EqualTo(1));
            Assert.That(CountButtonsNamed("RulesextraButton"), Is.EqualTo(1));
            Assert.That(
                root.Q<VisualElement>("ButtonGrid")
                    .Query<VisualElement>(className: "btn-row")
                    .ToList()
                    .All(row => row.childCount <= 2),
                Is.True,
                "Mixed entries must preserve the HUD's two-buttons-per-row layout."
            );
            Assert.That(extraDefinitionButton.enabledSelf, Is.True);
            Assert.That(extraDefinitionButton.tooltip, Is.EqualTo(string.Empty));

            PushButton(extraDefinitionButton);
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                    handler.Operations.Count == 1
                    && store.Snapshot.ActionEconomy[Actor].ActionsRemaining == 0
                    && !controller.IsTakingAction
            );
            yield return null;

            Assert.That(handler.Operations, Has.Count.EqualTo(1));
            Assert.That(handler.Operations.Single().Actor, Is.EqualTo(Actor));
            Assert.That(definition.CreateOpCalls, Is.EqualTo(1));
            Assert.That(store.Snapshot.Version, Is.EqualTo(1));
            Assert.That(controller.ActionPoints, Is.EqualTo(legacyActionPointsBefore));
            Assert.That(extraDefinitionButton.enabledSelf, Is.False);
            Assert.That(extraDefinitionButton.tooltip, Is.EqualTo("No rules actions remain"));
            Assert.That(
                extraDefinitionButton.ClassListContains(HUDController.DisabledHudButtonClass),
                Is.True
            );
        }

        private int CountButtonsNamed(string buttonName) =>
            root.Query<Button>().ToList().Count(button => button.name == buttonName);

        private static DefinitionActionBarEntry<
            ConfirmationSelection,
            TestActionOp,
            TestActionResult
        > CreateEntry(
            ActionBarEntryKey key,
            string displayName,
            TestDefinition definition,
            RuleDispatcher dispatcher,
            ISelectionAdapter adapter
        ) =>
            new DefinitionActionBarEntry<ConfirmationSelection, TestActionOp, TestActionResult>(
                key,
                displayName,
                definition,
                Actor,
                dispatcher,
                adapter
            );

        private sealed class TestLegacyAction : EntityAction
        {
            private readonly string actionName;

            public TestLegacyAction(string actionName)
                : base(1) => this.actionName = actionName;

            public override string ActionName => actionName;
        }

        private sealed class TestDefinition
            : IActionDefinition<ConfirmationSelection, TestActionOp, TestActionResult>
        {
            public int CreateOpCalls { get; private set; }

            public ActionAvailability GetAvailability(RulesSnapshot snapshot, CreatureId actor)
            {
                if (
                    snapshot.ActionEconomy.TryGet(actor, out ActionEconomyState economy)
                    && economy.ActionsRemaining > 0
                )
                {
                    return ActionAvailability.Available;
                }

                return ActionAvailability.Unavailable("No rules actions remain");
            }

            public SelectionWorkflow<ConfirmationSelection> CreateSelectionWorkflow(
                RulesSnapshot snapshot,
                CreatureId actor
            ) =>
                SelectionWorkflow.From(
                    new ConfirmationSelectionRequest(
                        new SelectionRequestId("confirm-hud-definition-action")
                    )
                );

            public TestActionOp CreateOp(CreatureId actor, ConfirmationSelection selection)
            {
                CreateOpCalls++;
                return new TestActionOp(actor, selection);
            }
        }

        private sealed class TestActionOp : ActionOp<TestActionResult>
        {
            public TestActionOp(CreatureId actor, ConfirmationSelection selection)
                : base(actor, DefinitionActionBarPlayModeTests.DefinitionId) =>
                Selection = selection;

            public ConfirmationSelection Selection { get; }
        }

        private sealed class TestActionResult { }

        private sealed class RecordingHandler : IOpHandler<TestActionOp, TestActionResult>
        {
            public List<TestActionOp> Operations { get; } = new List<TestActionOp>();

            public ValueTask<TestActionResult> Handle(
                OpFrame<TestActionOp> frame,
                OpHandlerContext context
            )
            {
                Operations.Add(frame.Op);
                return new ValueTask<TestActionResult>(new TestActionResult());
            }
        }

        private sealed class FixedActionCatalog : IActionCatalog
        {
            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
            {
                Assert.That(definitionId, Is.EqualTo(DefinitionId));
                return ActionProfile.OneAction(Array.Empty<Trait>());
            }
        }

        private sealed class ConfirmationPlanner : IAiSelectionPlanner
        {
            public SelectionOutcome<ConfirmationSelection> Confirm(
                ConfirmationSelectionRequest request
            ) => SelectionOutcome<ConfirmationSelection>.Completed(new ConfirmationSelection(true));

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
    }
}
