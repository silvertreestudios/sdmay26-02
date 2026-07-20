using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        /// <summary>
        /// Verifies rows from the preceding turn lose authority before their slide-out animation finishes.
        /// </summary>
        [UnityTest]
        public IEnumerator StaleTurnDefinitionButtonCannotDispatch()
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

            ActionController controller = player.GetComponent<ActionController>();
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(1, true))
            );
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();
            TestDefinition definition = new TestDefinition();
            controller.AddDefinitionAction(
                CreateEntry(
                    new ActionBarEntryKey("test/stale-turn-definition"),
                    "Stale definition",
                    definition,
                    dispatcher,
                    new AiSelectionAdapter(new ConfirmationPlanner())
                )
            );
            OnNextTurn.Invoke(player);

            Button staleButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    staleButton = root.Q<Button>("StaledefinitionButton");
                    return staleButton != null && staleButton.enabledSelf;
                }
            );
            Assert.That(staleButton, Is.Not.Null);

            OnNextTurn.Invoke(player);

            Assert.That(
                staleButton.enabledSelf,
                Is.False,
                "The preceding turn's row must be disabled synchronously, before it slides out."
            );
            staleButton.SetEnabled(true);
            PushButton(staleButton);
            yield return null;

            Assert.That(handler.Operations, Is.Empty);
            Assert.That(definition.CreateOpCalls, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(controller.IsTakingAction, Is.False);
        }

        /// <summary>
        /// Verifies cancelling pending selection does not release the controller until the
        /// selection source returns, and that its late completed value cannot dispatch.
        /// </summary>
        [UnityTest]
        public IEnumerator CancelKeepsControllerLockedUntilDefinitionExecutionReturns()
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

            ActionController controller = player.GetComponent<ActionController>();
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(1, true))
            );
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();
            TestDefinition definition = new TestDefinition();
            BlockingConfirmationAdapter adapter = new BlockingConfirmationAdapter();
            controller.AddDefinitionAction(
                CreateEntry(
                    new ActionBarEntryKey("test/delayed-definition"),
                    "Delayed definition",
                    definition,
                    dispatcher,
                    adapter
                )
            );
            OnNextTurn.Invoke(player);

            Button definitionButton = null;
            Button cancelButton = null;
            Button endTurnButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    definitionButton = root.Q<Button>("DelayeddefinitionButton");
                    cancelButton = root.Q<Button>("CancelActionButton");
                    endTurnButton = root.Q<Button>("EndTurnButton");
                    return definitionButton != null
                        && cancelButton != null
                        && endTurnButton != null
                        && definitionButton.enabledSelf;
                }
            );

            PushButton(definitionButton);
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                    adapter.SelectionRequested
                    && controller.IsTakingAction
                    && cancelButton.enabledSelf
            );
            Assert.That(controller.IsTakingAction, Is.True);
            Assert.That(cancelButton.enabledSelf, Is.True);

            PushButton(cancelButton);
            yield return null;

            Assert.That(
                controller.IsTakingAction,
                Is.True,
                "Cancellation may signal pending selection, but only Execute completion releases the action lock."
            );
            Assert.That(definitionButton.enabledSelf, Is.False);
            Assert.That(endTurnButton.enabledSelf, Is.False);
            Assert.That(cancelButton.enabledSelf, Is.False);
            Assert.That(handler.Operations, Is.Empty);
            Assert.That(definition.CreateOpCalls, Is.Zero);

            adapter.CompleteWithLateSelection();
            yield return WaitUntilWithTimeout(timeout, () => !controller.IsTakingAction);
            yield return null;

            Assert.That(controller.IsTakingAction, Is.False);
            Assert.That(handler.Operations, Is.Empty);
            Assert.That(definition.CreateOpCalls, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(definitionButton.enabledSelf, Is.True);
            Assert.That(endTurnButton.enabledSelf, Is.True);
        }

        /// <summary>
        /// Verifies a turn change retires even an already-cancel-requested selection immediately,
        /// and that its eventual adapter result cannot affect the incoming turn or rules state.
        /// </summary>
        [UnityTest]
        public IEnumerator TurnChangeRetiresPendingSelectionAndRejectsLateResult()
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

            ActionController controller = player.GetComponent<ActionController>();
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(1, true))
            );
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();
            TestDefinition definition = new TestDefinition();
            BlockingConfirmationAdapter adapter = new BlockingConfirmationAdapter();
            controller.AddDefinitionAction(
                CreateEntry(
                    new ActionBarEntryKey("test/revoked-definition"),
                    "Revoked definition",
                    definition,
                    dispatcher,
                    adapter
                )
            );
            OnNextTurn.Invoke(player);

            Button outgoingButton = null;
            Button cancelButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    outgoingButton = root.Q<Button>("RevokeddefinitionButton");
                    cancelButton = root.Q<Button>("CancelActionButton");
                    return outgoingButton != null
                        && cancelButton != null
                        && outgoingButton.enabledSelf;
                }
            );

            PushButton(outgoingButton);
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                    adapter.SelectionRequested
                    && controller.IsTakingAction
                    && cancelButton.enabledSelf
            );
            PushButton(cancelButton);
            yield return null;
            Assert.That(
                controller.IsTakingAction,
                Is.True,
                "A user cancel request alone keeps authority locked until selection returns."
            );

            OnNextTurn.Invoke(player);

            Assert.That(
                controller.IsTakingAction,
                Is.False,
                "Turn revocation must release a pending selection owner synchronously."
            );
            Assert.That(outgoingButton.enabledSelf, Is.False);

            Button incomingButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    Button candidate = root.Q<Button>("RevokeddefinitionButton");
                    if (
                        candidate == null
                        || ReferenceEquals(candidate, outgoingButton)
                        || !candidate.enabledSelf
                    )
                    {
                        return false;
                    }

                    incomingButton = candidate;
                    return true;
                }
            );
            Assert.That(incomingButton, Is.Not.Null);
            Assert.That(incomingButton.enabledSelf, Is.True);

            adapter.CompleteWithLateSelection();
            yield return null;
            yield return null;

            Assert.That(handler.Operations, Is.Empty);
            Assert.That(definition.CreateOpCalls, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(controller.IsTakingAction, Is.False);
            Assert.That(
                incomingButton.enabledSelf,
                Is.True,
                "The retired execution's finally block must not clobber the incoming turn."
            );
        }

        /// <summary>
        /// Verifies disabling the HUD retires presentation-owned selection while allowing its
        /// unfinished adapter task to unwind safely without dispatching a late result, and that
        /// re-enabling restores both the current row and the authoritative turn subscription.
        /// </summary>
        [UnityTest]
        public IEnumerator HudDisableRetiresPendingSelectionAndRejectsLateResult()
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

            ActionController controller = player.GetComponent<ActionController>();
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(1, true))
            );
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();
            TestDefinition definition = new TestDefinition();
            BlockingConfirmationAdapter adapter = new BlockingConfirmationAdapter();
            controller.AddDefinitionAction(
                CreateEntry(
                    new ActionBarEntryKey("test/disabled-hud-definition"),
                    "Disabled HUD definition",
                    definition,
                    dispatcher,
                    adapter
                )
            );
            OnNextTurn.Invoke(player);

            Button definitionButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    definitionButton = root.Q<Button>("DisabledHUDdefinitionButton");
                    return definitionButton != null && definitionButton.enabledSelf;
                }
            );
            PushButton(definitionButton);
            yield return WaitUntilWithTimeout(
                timeout,
                () => adapter.SelectionRequested && controller.IsTakingAction
            );

            HUDController hud = UnityEngine.Object.FindFirstObjectByType<HUDController>();
            Assert.That(hud, Is.Not.Null);
            hud.enabled = false;

            Assert.That(
                controller.IsTakingAction,
                Is.False,
                "A disabled presentation owner must release pending selection authority immediately."
            );
            adapter.CompleteWithLateSelection();
            yield return null;
            yield return null;

            Assert.That(handler.Operations, Is.Empty);
            Assert.That(definition.CreateOpCalls, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(controller.IsTakingAction, Is.False);

            hud.enabled = true;

            Button rebuiltButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    Button candidate = root.Q<Button>("DisabledHUDdefinitionButton");
                    if (candidate == null || ReferenceEquals(candidate, definitionButton))
                        return false;
                    rebuiltButton = candidate;
                    return candidate.enabledSelf;
                }
            );
            Assert.That(CountButtonsNamed("DisabledHUDdefinitionButton"), Is.EqualTo(1));

            OnNextTurn.Invoke(player);

            Button eventRebuiltButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    Button candidate = root.Q<Button>("DisabledHUDdefinitionButton");
                    if (candidate == null || ReferenceEquals(candidate, rebuiltButton))
                        return false;
                    eventRebuiltButton = candidate;
                    return candidate.enabledSelf;
                }
            );
            Assert.That(eventRebuiltButton, Is.Not.Null);
            Assert.That(CountButtonsNamed("DisabledHUDdefinitionButton"), Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies HUD disable and re-enable cannot retire authoritative dispatch, while the rebuilt
        /// row stays locked until completion releases the dispatch owner and refreshes presentation.
        /// </summary>
        [UnityTest]
        public IEnumerator HudDisableDuringDispatchKeepsOwnerLockedUntilDispatchReturns()
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

            ActionController controller = player.GetComponent<ActionController>();
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(2, true))
            );
            BlockingDispatchHandler handler = new BlockingDispatchHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();
            TestDefinition definition = new TestDefinition();
            controller.AddDefinitionAction(
                CreateEntry(
                    new ActionBarEntryKey("test/dispatching-disabled-hud"),
                    "Dispatching disabled HUD",
                    definition,
                    dispatcher,
                    new AiSelectionAdapter(new ConfirmationPlanner())
                )
            );
            OnNextTurn.Invoke(player);

            Button definitionButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    definitionButton = root.Q<Button>("DispatchingdisabledHUDButton");
                    return definitionButton != null && definitionButton.enabledSelf;
                }
            );
            PushButton(definitionButton);
            yield return WaitUntilWithTimeout(
                timeout,
                () => handler.Calls == 1 && controller.IsTakingAction
            );

            HUDController hud = UnityEngine.Object.FindFirstObjectByType<HUDController>();
            Assert.That(hud, Is.Not.Null);
            hud.enabled = false;

            Assert.That(
                controller.IsTakingAction,
                Is.True,
                "Authoritative dispatch must retain its owner lock when presentation is disabled."
            );
            Assert.That(handler.Operations, Has.Count.EqualTo(1));

            hud.enabled = true;

            Button rebuiltButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    Button candidate = root.Q<Button>("DispatchingdisabledHUDButton");
                    if (candidate == null || ReferenceEquals(candidate, definitionButton))
                        return false;
                    rebuiltButton = candidate;
                    return !candidate.enabledSelf;
                }
            );
            Assert.That(CountButtonsNamed("DispatchingdisabledHUDButton"), Is.EqualTo(1));
            Assert.That(
                controller.IsTakingAction,
                Is.True,
                "Rebuilding presentation must not retire an authoritative dispatch owner."
            );

            handler.Complete();
            yield return WaitUntilWithTimeout(timeout, () => !controller.IsTakingAction);
            yield return WaitUntilWithTimeout(timeout, () => rebuiltButton.enabledSelf);

            Assert.That(controller.IsTakingAction, Is.False);
            Assert.That(handler.Operations, Has.Count.EqualTo(1));
            Assert.That(definition.CreateOpCalls, Is.EqualTo(1));
            Assert.That(store.Snapshot.Version, Is.EqualTo(1));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(hud.enabled, Is.True);
        }

        /// <summary>
        /// Verifies a definition row consults live controller authority when a turn ends without a
        /// HUD turn event, so even a forcibly re-enabled stale button cannot launch selection.
        /// </summary>
        [UnityTest]
        public IEnumerator DefinitionRowRejectsEndedTurnWithoutTurnEvent()
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

            ActionController controller = player.GetComponent<ActionController>();
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(1, true))
            );
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();
            TestDefinition definition = new TestDefinition();
            BlockingConfirmationAdapter adapter = new BlockingConfirmationAdapter();
            controller.AddDefinitionAction(
                CreateEntry(
                    new ActionBarEntryKey("test/ended-turn-without-event"),
                    "Ended turn without event",
                    definition,
                    dispatcher,
                    adapter
                )
            );
            OnNextTurn.Invoke(player);

            Button definitionButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    definitionButton = root.Q<Button>("EndedturnwithouteventButton");
                    return definitionButton != null && definitionButton.enabledSelf;
                }
            );

            // Model paths such as an empty turn queue, where EndTurn revokes controller authority
            // but no subsequent turn notification reaches presentation.
            OnNextTurn.RemoveAllListeners();
            controller.EndTurn();
            yield return WaitUntilWithTimeout(timeout, () => !definitionButton.enabledSelf);

            Assert.That(definitionButton.enabledSelf, Is.False);
            definitionButton.SetEnabled(true);
            PushButton(definitionButton);
            yield return null;
            yield return null;

            Assert.That(adapter.SelectionRequested, Is.False);
            Assert.That(handler.Operations, Is.Empty);
            Assert.That(definition.CreateOpCalls, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(controller.IsTakingAction, Is.False);
        }

        /// <summary>
        /// Verifies combat end while the HUD is disabled invalidates detached button callbacks and
        /// keeps a later re-enable from restoring the ended combat's last reported turn.
        /// </summary>
        [UnityTest]
        public IEnumerator CombatEndInvalidatesDefinitionRowsAndPreventsRestore()
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

            HUDController hud = UnityEngine.Object.FindFirstObjectByType<HUDController>();
            Assert.That(hud, Is.Not.Null);

            // Disabling only the level coordinator removes its combat-end listener without
            // disturbing the HUD listener whose disabled-lifecycle behavior this test exercises.
            GameManager gameManager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
            Assert.That(gameManager, Is.Not.Null);
            gameManager.enabled = false;

            ActionController controller = player.GetComponent<ActionController>();
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedActionEconomy(Actor, new ActionEconomyState(1, true))
            );
            RecordingHandler handler = new RecordingHandler();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionResult>(handler)
                .UseActionLifecycle(new FixedActionCatalog())
                .Build();
            TestDefinition definition = new TestDefinition();
            BlockingConfirmationAdapter adapter = new BlockingConfirmationAdapter();
            controller.AddDefinitionAction(
                CreateEntry(
                    new ActionBarEntryKey("test/combat-ended-definition"),
                    "Combat ended definition",
                    definition,
                    dispatcher,
                    adapter
                )
            );
            OnNextTurn.Invoke(player);

            Button definitionButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    definitionButton = root.Q<Button>("CombatendeddefinitionButton");
                    return definitionButton != null && definitionButton.enabledSelf;
                }
            );

            hud.enabled = false;
            OnCombatEnd.Invoke("Players");

            Assert.That(hud.enabled, Is.False);
            Assert.That(root.Q<Button>("CombatendeddefinitionButton"), Is.Null);
            definitionButton.SetEnabled(true);
            PushButton(definitionButton);
            yield return null;
            yield return null;

            Assert.That(adapter.SelectionRequested, Is.False);
            Assert.That(handler.Operations, Is.Empty);
            Assert.That(definition.CreateOpCalls, Is.Zero);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(controller.IsTakingAction, Is.False);

            hud.enabled = true;
            yield return null;

            Assert.That(root.Q<Button>("CombatendeddefinitionButton"), Is.Null);
        }

        /// <summary>
        /// Verifies repeated HUD enable cycles retain exactly one hover callback pair per element,
        /// so one pointer transition produces one matching change in hover ownership state.
        /// </summary>
        [UnityTest]
        public IEnumerator HudHoverCallbacksRemainSingleAcrossRepeatedReenable()
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

            HUDController hud = UnityEngine.Object.FindFirstObjectByType<HUDController>();
            VisualElement panelElement = root.Q<VisualElement>("Panel");
            Assert.That(hud, Is.Not.Null);
            Assert.That(panelElement, Is.Not.Null);

            hud.enabled = false;
            hud.enabled = true;
            hud.enabled = false;
            hud.enabled = true;

            FieldInfo hoverCountField = typeof(HUDController).GetField(
                "_hudHoverCount",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(hoverCountField, Is.Not.Null);

            using (MouseEnterEvent enterEvent = MouseEnterEvent.GetPooled())
            {
                enterEvent.target = panelElement;
                panelElement.SendEvent(enterEvent);
            }

            Assert.That(hoverCountField.GetValue(hud), Is.EqualTo(1));
            Assert.That(HUDController.IsPointerOverHUD, Is.True);

            using (MouseLeaveEvent leaveEvent = MouseLeaveEvent.GetPooled())
            {
                leaveEvent.target = panelElement;
                panelElement.SendEvent(leaveEvent);
            }

            Assert.That(hoverCountField.GetValue(hud), Is.Zero);
            Assert.That(HUDController.IsPointerOverHUD, Is.False);
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

        private sealed class BlockingDispatchHandler : IOpHandler<TestActionOp, TestActionResult>
        {
            private readonly TaskCompletionSource<TestActionResult> completion =
                new TaskCompletionSource<TestActionResult>();

            public int Calls { get; private set; }

            public List<TestActionOp> Operations { get; } = new List<TestActionOp>();

            public ValueTask<TestActionResult> Handle(
                OpFrame<TestActionOp> frame,
                OpHandlerContext context
            )
            {
                Calls++;
                Operations.Add(frame.Op);
                return new ValueTask<TestActionResult>(completion.Task);
            }

            public void Complete() => completion.SetResult(new TestActionResult());
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

        private sealed class BlockingConfirmationAdapter : ISelectionAdapter
        {
            private readonly TaskCompletionSource<
                SelectionOutcome<ConfirmationSelection>
            > completion = new TaskCompletionSource<SelectionOutcome<ConfirmationSelection>>();

            public bool SelectionRequested { get; private set; }

            public ValueTask<SelectionOutcome<ConfirmationSelection>> Confirm(
                ConfirmationSelectionRequest request
            )
            {
                SelectionRequested = true;
                return new ValueTask<SelectionOutcome<ConfirmationSelection>>(completion.Task);
            }

            public void CompleteWithLateSelection() =>
                completion.SetResult(
                    SelectionOutcome<ConfirmationSelection>.Completed(
                        new ConfirmationSelection(true)
                    )
                );

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
