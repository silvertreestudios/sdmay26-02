using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Creature;
using Game.Rules.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TestsUI
{
    public class GameUITests : PlayModeBase
    {
        [UnitySetUp]
        public override IEnumerator Setup()
        {
            yield return base.Setup();
        }

        /// <summary>
        /// Tests that all the expected UI actions are present upon the start of a turn
        /// </summary>
        [UnityTest]
        public IEnumerator UIActionsPresentTest()
        {
            //check that all buttons are present
            player = CombatManagerInterface.GetInstance().WhosTurn();
            List<string> buttonNames = GetActionButtons(player);
            foreach (string name in buttonNames)
            {
                yield return WaitUntilWithTimeout(
                    timeout,
                    () => (button = root.Q<Button>(name)) != null
                );
                Assert.IsNotNull(button, $"Button with name '{name}' not found in UI.");
                button = null; // Reset button for the next search
            }
            yield return null;
        }

        /// <summary>
        /// Tests that the fast forward buttons and pause button are present in the UI
        /// </summary>
        [UnityTest]
        public IEnumerator FastForwardButtonsPresentTest()
        {
            string[] buttonNames =
            {
                "PauseButton",
                "Speed2xButton",
                "Speed3xButton",
                "SpeedToggleButton",
            };
            foreach (string name in buttonNames)
            {
                yield return WaitUntilWithTimeout(
                    timeout,
                    () => (button = root.Q<Button>(name)) != null
                );
                Assert.IsNotNull(button, $"Button with name '{name}' not found in UI.");
                button = null;
            }
            yield return null;
        }

        /// <summary>
        /// Tests that the combat log and its related UI elements are present in the UI
        /// </summary>
        [UnityTest]
        public IEnumerator CombatLogElementsPresentTest()
        {
            string[] elementNames = { "CombatLog", "LogToggleButton", "ResizeHandle" };
            VisualElement element = null;
            foreach (string name in elementNames)
            {
                yield return WaitUntilWithTimeout(
                    timeout,
                    () => (element = root.Q<VisualElement>(name)) != null
                );
                Assert.IsNotNull(element, $"Element with name '{name}' not found in UI.");
                element = null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerCardsShowActionPointMedallions()
        {
            VisualElement cardHolder = null;
            Button strideButton = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    cardHolder = root.Q<VisualElement>("CardHolder");
                    strideButton = root.Q<Button>("StrideButton");
                    player = CombatManagerInterface.GetInstance().WhosTurn();
                    return cardHolder != null
                        && cardHolder.childCount > 0
                        && strideButton != null
                        && player != null;
                }
            );
            Assert.IsNotNull(cardHolder, "CardHolder not found in UI.");
            Assert.IsNotNull(strideButton, "Player turn action buttons were not ready.");

            ActionController actionController = player.GetComponent<ActionController>();
            Assert.IsNotNull(actionController, "Current player has no ActionController.");

            List<GameObject> combatants = CombatManagerInterface.GetInstance().GetCombatants();
            int playerCardIndex = combatants.IndexOf(player);
            Assert.GreaterOrEqual(
                playerCardIndex,
                0,
                "Current player was not found in the combatant queue."
            );
            Assert.Less(
                playerCardIndex,
                cardHolder.childCount,
                "Current player's card was not found in CardHolder."
            );

            VisualElement card = cardHolder.ElementAt(playerCardIndex);
            List<VisualElement> medallions = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    medallions = card.Query<VisualElement>(className: "action-medallion").ToList();
                    return medallions.Count == 3;
                }
            );

            Assert.AreEqual(
                3,
                medallions.Count,
                "Player card should show exactly three action medallions."
            );
            Assert.IsNull(
                card.Q<Label>("DESC"),
                "Temporary DESC action point label should be removed."
            );
            foreach (Label label in card.Query<Label>().ToList())
                Assert.IsFalse(
                    (label.text ?? "").Contains("AP:"),
                    "Player card should not show textual AP."
                );

            float containerWidth = card.Q<VisualElement>(
                "ActionPointContainer"
            ).resolvedStyle.width;
            float containerHeight = card.Q<VisualElement>(
                "ActionPointContainer"
            ).resolvedStyle.height;

            uint[] actionPointStates = { 3, 2, 1, 0 };
            uint previousActionPoints = actionController.ActionPoints;
            foreach (uint actionPoints in actionPointStates)
            {
                if (previousActionPoints > actionPoints)
                    actionController.SpendActions(previousActionPoints - actionPoints);
                previousActionPoints = actionPoints;

                yield return WaitUntilWithTimeout(
                    timeout,
                    () =>
                    {
                        medallions = card.Query<VisualElement>(className: "action-medallion")
                            .ToList();
                        return CountMedallionsWithClass(medallions, "action-medallion--filled")
                            == (int)actionPoints;
                    }
                );

                int filledCount = CountMedallionsWithClass(medallions, "action-medallion--filled");
                int emptyCount = CountMedallionsWithClass(medallions, "action-medallion--empty");
                Assert.AreEqual(
                    (int)actionPoints,
                    filledCount,
                    $"Expected {actionPoints} filled action medallions."
                );
                Assert.AreEqual(
                    3 - (int)actionPoints,
                    emptyCount,
                    $"Expected {3 - (int)actionPoints} empty action medallions."
                );
                Assert.AreEqual(
                    containerWidth,
                    card.Q<VisualElement>("ActionPointContainer").resolvedStyle.width,
                    "Action medallion container width should not shift as AP changes."
                );
                Assert.AreEqual(
                    containerHeight,
                    card.Q<VisualElement>("ActionPointContainer").resolvedStyle.height,
                    "Action medallion container height should not shift as AP changes."
                );
            }
        }

        [UnityTest]
        public IEnumerator ExplorationPlayerCardsRefreshHealth()
        {
            List<GameObject> combatants = CombatManagerInterface.GetInstance().GetCombatants();
            player = combatants.Find(combatant =>
                combatant.GetComponent<PlayerActionController>() != null
            );
            Assert.IsNotNull(player, "Expected a player combatant in UnitTestingScene.");

            ActionController controller = player.GetComponent<ActionController>();
            CreatureComponent creature = player.GetComponent<CreatureComponent>();
            VisualElement existingCardHolder = root.Q<VisualElement>("CardHolder");
            VisualElement previousCard =
                existingCardHolder != null && existingCardHolder.childCount > 0
                    ? existingCardHolder.ElementAt(0)
                    : null;
            HUDController
                .GetInstance()
                .ShowExploration(new[] { controller }, controller, candidate => candidate != null);

            VisualElement cardHolder = null;
            Label healthLabel = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    cardHolder = root.Q<VisualElement>("CardHolder");
                    if (cardHolder == null || cardHolder.childCount != 1)
                        return false;

                    VisualElement currentCard = cardHolder.ElementAt(0);
                    if (currentCard == previousCard)
                        return false;
                    healthLabel = currentCard.Q<Label>("HealthBarLabel");
                    return healthLabel != null;
                }
            );
            Assert.IsNotNull(healthLabel, "Exploration player card health label was not created.");

            int startingDisplayedHitPoints = creature.hp + creature.tempHp;
            Assert.Greater(
                startingDisplayedHitPoints,
                1,
                "The UI fixture player must survive test damage."
            );
            creature.ApplyFinalDamage(1, RuleSource.FromSlug("test-exploration-ui-damage"));
            string expectedHealth =
                $"{creature.hp + creature.tempHp}/{creature.maxHp + creature.tempHp}";
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    if (cardHolder.childCount != 1)
                        return false;

                    healthLabel = cardHolder.ElementAt(0).Q<Label>("HealthBarLabel");
                    return healthLabel != null && healthLabel.text == expectedHealth;
                }
            );

            Assert.AreEqual(
                startingDisplayedHitPoints - 1,
                creature.hp + creature.tempHp,
                "The authoritative health state should retain the committed exploration damage."
            );

            Assert.AreEqual(
                expectedHealth,
                healthLabel.text,
                "Exploration cards should refresh after the creature's health changes."
            );
        }

        [UnityTest]
        public IEnumerator ExplorationGuidanceIsOwnedByExplorationMode()
        {
            player = CombatManagerInterface
                .GetInstance()
                .GetCombatants()
                .Find(combatant => combatant.GetComponent<PlayerActionController>() != null);
            ActionController controller = player.GetComponent<ActionController>();
            HUDController hud = HUDController.GetInstance();

            hud.ShowExploration(new[] { controller }, controller, candidate => candidate != null);
            yield return WaitUntilWithTimeout(
                timeout,
                () => root.Q<Label>("ExplorationGuidance") != null
            );

            Assert.That(root.Q<Button>("StrideButton"), Is.Null);
            Assert.That(root.Q<Button>("EndTurnButton"), Is.Null);
            Assert.That(
                root.Query<Label>(className: "exploration-guidance").ToList(),
                Has.Count.EqualTo(1)
            );

            hud.HideExploration();
            Assert.That(root.Q<Label>("ExplorationGuidance"), Is.Null);

            hud.ShowExploration(new[] { controller }, controller, candidate => candidate != null);
            yield return WaitUntilWithTimeout(
                timeout,
                () => root.Q<Label>("ExplorationGuidance") != null
            );
            Assert.That(
                root.Query<Label>(className: "exploration-guidance").ToList(),
                Has.Count.EqualTo(1)
            );
        }

        [UnityTest]
        public IEnumerator TacticsModeControlIsReusedAfterHudReenable()
        {
            HUDController hud = HUDController.GetInstance();
            Button original = root.Q<Button>("TacticsModeButton");
            Assert.That(original, Is.Not.Null);
            Assert.That(original.ClassListContains("btn-tactics-mode"), Is.True);
            int returnRequests = 0;
            hud.ConfigureTacticsControl(
                () => Assert.Fail("The tactics callback should not run in Tactics."),
                () =>
                {
                    returnRequests++;
                    return true;
                }
            );
            Assert.That(original.text, Is.EqualTo("Enter Tactics"));
            hud.ShowTactics();

            hud.enabled = false;
            yield return null;
            hud.enabled = true;
            yield return null;

            Assert.That(root.Q<Button>("TacticsModeButton"), Is.SameAs(original));
            Assert.That(
                root.Query<Button>(name: "TacticsModeButton").ToList(),
                Has.Count.EqualTo(1)
            );
            Assert.That(original.text, Is.EqualTo("Return to Exploration"));
            Assert.That(original.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

            PushButton(original);
            yield return null;
            Assert.That(returnRequests, Is.EqualTo(1));

            GameObject currentTurn = CombatManagerInterface.GetInstance().WhosTurn();
            Assert.That(currentTurn, Is.Not.Null);
            root.Q<VisualElement>("ButtonGrid").Clear();
            OnNextTurn.Invoke(currentTurn);
            yield return WaitUntilWithTimeout(
                timeout,
                () => root.Q<Button>("EndTurnButton") != null
            );
        }

        /// <summary>
        /// Verifies the unavailable mode control is inert, hidden, disabled, and lifecycle-stable.
        /// </summary>
        [UnityTest]
        public IEnumerator UnavailableTacticsModeControlClearsBothModeCallbacks()
        {
            HUDController hud = HUDController.GetInstance();
            Button tacticsControl = root.Q<Button>("TacticsModeButton");
            Assert.That(tacticsControl, Is.Not.Null);
            int enterRequests = 0;
            int returnRequests = 0;
            System.Action enter = () => enterRequests++;
            System.Func<bool> returnToExploration = () =>
            {
                returnRequests++;
                return true;
            };

            hud.ConfigureTacticsControl(enter, returnToExploration);
            Assert.That(tacticsControl.text, Is.EqualTo("Enter Tactics"));
            Assert.That(tacticsControl.enabledSelf, Is.True);
            hud.ShowTacticsUnavailable();
            yield return null;

            Assert.That(tacticsControl.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(tacticsControl.enabledSelf, Is.False);
            PushButton(tacticsControl);
            Assert.That(enterRequests, Is.Zero);
            Assert.That(returnRequests, Is.Zero);

            hud.enabled = false;
            yield return null;
            hud.enabled = true;
            yield return null;

            Assert.That(root.Q<Button>("TacticsModeButton"), Is.SameAs(tacticsControl));
            Assert.That(tacticsControl.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(tacticsControl.enabledSelf, Is.False);

            hud.ConfigureTacticsControl(enter, returnToExploration);
            hud.ShowTactics();
            Assert.That(tacticsControl.text, Is.EqualTo("Return to Exploration"));
            Assert.That(tacticsControl.enabledSelf, Is.True);
            hud.ShowTacticsUnavailable();
            PushButton(tacticsControl);

            Assert.That(enterRequests, Is.Zero);
            Assert.That(returnRequests, Is.Zero);
        }

        [UnityTest]
        public IEnumerator TacticsModePointerGuardIsLifecycleSafeAcrossHudReenable()
        {
            HUDController hud = HUDController.GetInstance();
            Button tacticsControl = root.Q<Button>("TacticsModeButton");
            Assert.That(tacticsControl, Is.Not.Null);
            FieldInfo hoverCount = typeof(HUDController).GetField(
                "_hudHoverCount",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(hoverCount, Is.Not.Null);

            using (PointerEnterEvent pointerEnter = PointerEnterEvent.GetPooled())
            {
                pointerEnter.target = tacticsControl;
                tacticsControl.SendEvent(pointerEnter);
            }
            Assert.That(HUDController.IsPointerOverHUD, Is.True);
            Assert.That(hoverCount.GetValue(hud), Is.EqualTo(1));

            using (PointerLeaveEvent pointerLeave = PointerLeaveEvent.GetPooled())
            {
                pointerLeave.target = tacticsControl;
                tacticsControl.SendEvent(pointerLeave);
            }
            Assert.That(HUDController.IsPointerOverHUD, Is.False);
            Assert.That(hoverCount.GetValue(hud), Is.EqualTo(0));

            using (PointerEnterEvent pointerEnter = PointerEnterEvent.GetPooled())
            {
                pointerEnter.target = tacticsControl;
                tacticsControl.SendEvent(pointerEnter);
            }
            hud.enabled = false;
            yield return null;
            Assert.That(HUDController.IsPointerOverHUD, Is.False);
            Assert.That(hoverCount.GetValue(hud), Is.EqualTo(0));

            hud.enabled = true;
            yield return null;
            Assert.That(root.Q<Button>("TacticsModeButton"), Is.SameAs(tacticsControl));
            using (PointerEnterEvent pointerEnter = PointerEnterEvent.GetPooled())
            {
                pointerEnter.target = tacticsControl;
                tacticsControl.SendEvent(pointerEnter);
            }
            Assert.That(HUDController.IsPointerOverHUD, Is.True);
            Assert.That(
                hoverCount.GetValue(hud),
                Is.EqualTo(1),
                "Re-enabling the HUD must register the tactics hover callback exactly once."
            );

            using (PointerLeaveEvent pointerLeave = PointerLeaveEvent.GetPooled())
            {
                pointerLeave.target = tacticsControl;
                tacticsControl.SendEvent(pointerLeave);
            }
            Assert.That(HUDController.IsPointerOverHUD, Is.False);
            Assert.That(hoverCount.GetValue(hud), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator PlayerCardsShowRenderedCreaturePortraits()
        {
            VisualElement cardHolder = null;
            List<GameObject> combatants = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    cardHolder = root.Q<VisualElement>("CardHolder");
                    combatants = CombatManagerInterface.GetInstance().GetCombatants();
                    if (
                        cardHolder == null
                        || combatants == null
                        || cardHolder.childCount != combatants.Count
                    )
                        return false;
                    for (int i = 0; i < cardHolder.childCount; i++)
                    {
                        Image image = cardHolder.ElementAt(i).Q<Image>("PortraitImage");
                        if (image == null || image.image == null)
                            return false;
                    }
                    return true;
                }
            );

            Assert.IsNotNull(cardHolder, "CardHolder not found in UI.");
            Assert.IsNotNull(combatants, "Combatants were not ready.");
            Assert.AreEqual(
                combatants.Count,
                cardHolder.childCount,
                "Every combatant should have an initiative card."
            );

            for (int i = 0; i < combatants.Count; i++)
            {
                Image portraitImage = cardHolder.ElementAt(i).Q<Image>("PortraitImage");
                Portrait portrait = combatants[i].GetComponent<Portrait>();
                Texture2D snapshot = portrait != null ? portrait.GetPortraitSnapshot() : null;
                Texture2D displayedPortrait =
                    portraitImage != null ? portraitImage.image as Texture2D : null;
                Assert.IsNotNull(
                    portraitImage,
                    combatants[i].name + " card is missing its portrait element."
                );
                Assert.AreEqual(
                    ScaleMode.ScaleToFit,
                    portraitImage.scaleMode,
                    combatants[i].name + " portrait should preserve its aspect ratio."
                );
                Assert.IsNotNull(
                    snapshot,
                    combatants[i].name + " did not capture a portrait texture."
                );
                Assert.IsNotNull(
                    displayedPortrait,
                    "Initiative card " + i + " is not displaying a portrait texture."
                );

                bool hasVisiblePixel = false;
                foreach (Color32 pixel in displayedPortrait.GetPixels32())
                {
                    if (pixel.a == 0)
                        continue;
                    hasVisiblePixel = true;
                    break;
                }
                Assert.IsTrue(
                    hasVisiblePixel,
                    combatants[i].name + " portrait is fully transparent."
                );
            }
        }

        [UnityTest]
        public IEnumerator CombatTrackerShowsReducedActionsForSlowedCreature()
        {
            VisualElement cardHolder = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    cardHolder = root.Q<VisualElement>("CardHolder");
                    player = CombatManagerInterface.GetInstance().WhosTurn();
                    return cardHolder != null && cardHolder.childCount > 0 && player != null;
                }
            );
            Assert.IsNotNull(cardHolder, "CardHolder not found in UI.");
            Assert.IsNotNull(player, "Current player was not ready.");

            ActionController actionController = player.GetComponent<ActionController>();
            Assert.IsNotNull(actionController, "Current combatant has no ActionController.");

            List<GameObject> combatants = CombatManagerInterface.GetInstance().GetCombatants();

            Conditions conditions =
                player.GetComponent<Conditions>() ?? player.AddComponent<Conditions>();
            Condition slowed = DefinedConditions.TryGet("Slowed 1");
            Assert.IsNotNull(slowed, "Slowed 1 condition definition was not found.");

            slowed.Apply(new ConditionSource(), player);
            CombatManagerInterface combatManager = CombatManagerInterface.GetInstance();
            int remainingTurns = combatants.Count + 1;
            do
            {
                combatManager.NextTurn();
            } while (combatManager.WhosTurn() != player && remainingTurns-- > 0);

            int matchingCards = 0;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    matchingCards = CountCardsWithMedallionState(cardHolder, 2, 1);
                    return matchingCards == 1;
                }
            );

            Assert.IsTrue(
                conditions.Contains("Slowed"),
                "Combatant should have the Slowed condition."
            );
            Assert.AreEqual(
                2u,
                actionController.ActionPoints,
                "Slowed 1 should leave the combatant with two actions at turn start."
            );
            Assert.AreEqual(
                1,
                matchingCards,
                "Combat tracker should show exactly one card with two available actions for Slowed 1."
            );
        }

        private int CountCardsWithMedallionState(
            VisualElement cardHolder,
            int expectedFilled,
            int expectedEmpty
        )
        {
            int matchingCards = 0;
            for (int i = 0; i < cardHolder.childCount; i++)
            {
                List<VisualElement> medallions = cardHolder
                    .ElementAt(i)
                    .Query<VisualElement>(className: "action-medallion")
                    .ToList();
                if (
                    medallions.Count == 3
                    && CountMedallionsWithClass(medallions, "action-medallion--filled")
                        == expectedFilled
                    && CountMedallionsWithClass(medallions, "action-medallion--empty")
                        == expectedEmpty
                )
                {
                    matchingCards++;
                }
            }
            return matchingCards;
        }

        private int CountMedallionsWithClass(List<VisualElement> medallions, string className)
        {
            int count = 0;
            foreach (VisualElement medallion in medallions)
            {
                if (medallion.ClassListContains(className))
                    count++;
            }
            return count;
        }

        [UnityTest]
        public IEnumerator ActionButtonsGreyOutInsteadOfDisappearingWhenUnavailable()
        {
            Button strideButton = null;
            Button strikeButton = null;
            Button endTurnButton = null;

            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    player = CombatManagerInterface.GetInstance().WhosTurn();
                    strideButton = root.Q<Button>("StrideButton");
                    strikeButton = root.Q<Button>("UnarmedStrikeButton");
                    endTurnButton = root.Q<Button>("EndTurnButton");
                    return player != null
                        && strideButton != null
                        && strikeButton != null
                        && endTurnButton != null;
                }
            );

            Assert.IsNotNull(player, "Current player was not ready.");
            Assert.IsNotNull(strideButton, "Stride button was not ready.");
            Assert.IsNotNull(strikeButton, "Unarmed Strike button was not ready.");
            Assert.IsNotNull(endTurnButton, "End Turn button was not ready.");

            ActionController actionController = player.GetComponent<ActionController>();
            Assert.IsNotNull(actionController, "Current player has no ActionController.");

            actionController.SpendActions(actionController.ActionPoints);
            yield return null;

            Assert.AreEqual(
                DisplayStyle.Flex,
                strideButton.resolvedStyle.display,
                "Stride should stay visible when unavailable."
            );
            Assert.AreEqual(
                DisplayStyle.Flex,
                strikeButton.resolvedStyle.display,
                "Strike should stay visible when unavailable."
            );
            Assert.IsFalse(
                strideButton.enabledSelf,
                "Stride should be disabled when AP is too low."
            );
            Assert.IsFalse(
                strikeButton.enabledSelf,
                "Strike should be disabled when AP is too low."
            );
            Assert.IsTrue(
                strideButton.ClassListContains(HUDController.DisabledHudButtonClass),
                "Stride should use the HUD disabled style."
            );
            Assert.IsTrue(
                strikeButton.ClassListContains(HUDController.DisabledHudButtonClass),
                "Strike should use the HUD disabled style."
            );
            Assert.IsTrue(
                endTurnButton.enabledSelf,
                "End Turn should remain available when no action is running."
            );

            CombatManagerInterface combatManager = CombatManagerInterface.GetInstance();
            int remainingTurns = combatManager.GetCombatants().Count + 1;
            do
            {
                combatManager.NextTurn();
            } while (combatManager.WhosTurn() != player && remainingTurns-- > 0);
            actionController.SpendActions(2);
            yield return null;

            Assert.IsTrue(
                strideButton.enabledSelf,
                "Stride should re-enable when enough AP is restored."
            );
            Assert.IsTrue(
                strikeButton.enabledSelf,
                "Strike should re-enable when enough AP is restored."
            );
            Assert.IsFalse(
                strideButton.ClassListContains(HUDController.DisabledHudButtonClass),
                "Stride disabled style should be removed when available."
            );
            Assert.IsFalse(
                strikeButton.ClassListContains(HUDController.DisabledHudButtonClass),
                "Strike disabled style should be removed when available."
            );
        }

        /// <summary>
        /// Tests that clicking cancel after each action button results in the correct state and UI behaviour
        /// </summary>
        [UnityTest]
        public IEnumerator UIStatesTest()
        {
            // Get GridBase to check FSM states
            GridPrivate.GridBase gridBase = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () => (gridBase = Object.FindFirstObjectByType<GridPrivate.GridBase>()) != null
            );
            Assert.IsNotNull(gridBase, "GridBase not found in the scene.");

            Assert.IsTrue(
                gridBase.Fsm.CurrentState is GridPrivate.StateIdle,
                "FSM should start in StateIdle."
            );

            // wait for buttons to be not null
            Button strideButton = null;
            Button strikeButton = null;
            Button cancelButton = null;
            Button endTurnButton = null;

            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    strideButton = root.Q<Button>("StrideButton");
                    strikeButton = root.Q<Button>("UnarmedStrikeButton");
                    cancelButton = root.Q<Button>("CancelActionButton");
                    endTurnButton = root.Q<Button>("EndTurnButton");
                    return strideButton != null
                        && strikeButton != null
                        && cancelButton != null
                        && endTurnButton != null;
                }
            );

            // Test Stride Button
            PushButton(strideButton);
            yield return null;
            Assert.IsTrue(
                gridBase.Fsm.CurrentState is GridPrivate.StateStride,
                "FSM should be in StateStride after clicking Stride button."
            );

            PushButton(cancelButton);
            yield return null;
            Assert.IsTrue(
                gridBase.Fsm.CurrentState is GridPrivate.StateIdle,
                "FSM should return to StateIdle after clicking Cancel button from Stride."
            );

            // Test Strike Button
            PushButton(strikeButton);
            yield return null;
            Assert.IsTrue(
                gridBase.Fsm.CurrentState is GridPrivate.StateStrike,
                "FSM should be in StateStrike after clicking Strike button."
            );

            PushButton(cancelButton);
            yield return null;
            Assert.IsTrue(
                gridBase.Fsm.CurrentState is GridPrivate.StateIdle,
                "FSM should return to StateIdle after clicking Cancel button from Strike."
            );

            // Test End Turn Button last
            PushButton(endTurnButton);
            yield return null;
            // Depending on implementation, ending the turn might put FSM into a transitional state or idle for the next unit.
            // We just ensure it doesn't crash and processes the turn change.
            Assert.Pass("UI States test passed successfully.");
        }

        /// <summary>
        /// Tests that the fast forward buttons and pause button correctly modify Time.timeScale when clicked
        /// </summary>
        [UnityTest]
        public IEnumerator FastForwardButtonsActionsTest()
        {
            Button speed2x = null;
            Button speed3x = null;
            Button pauseBtn = null;
            Button speedToggle = null;

            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    speed2x = root.Q<Button>("Speed2xButton");
                    speed3x = root.Q<Button>("Speed3xButton");
                    pauseBtn = root.Q<Button>("PauseButton");
                    speedToggle = root.Q<Button>("SpeedToggleButton");
                    return speed2x != null
                        && speed3x != null
                        && pauseBtn != null
                        && speedToggle != null;
                }
            );

            Assert.IsNotNull(speed2x, "Speed2xButton not found.");
            Assert.IsNotNull(speed3x, "Speed3xButton not found.");
            Assert.IsNotNull(pauseBtn, "PauseButton not found.");
            Assert.IsNotNull(speedToggle, "SpeedToggleButton not found.");

            // Check Time.timeScale modifications
            PushButton(speed2x);
            yield return null;
            Assert.AreEqual(2f, Time.timeScale, "Time scale should be 2 after clicking 2x speed.");

            PushButton(speed3x);
            yield return null;
            Assert.AreEqual(3f, Time.timeScale, "Time scale should be 3 after clicking 3x speed.");

            PushButton(pauseBtn);
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, "Time scale should be 0 after clicking Pause.");
        }

        /// <summary>
        /// Tests the functionality of the combat log by sending a test log message and verifying it appears in the log list, as well as testing the toggle button for visibility
        /// </summary>
        [UnityTest]
        public IEnumerator CombatLogFunctionalityTest()
        {
            CombatLog combatLogComponent = null;
            VisualElement combatLogUI = null;
            VisualElement handle = null;
            Button toggleButton = null;

            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    combatLogComponent = Object.FindFirstObjectByType<CombatLog>();
                    combatLogUI = root.Q<VisualElement>("CombatLog");
                    handle = root.Q<VisualElement>("ResizeHandle");
                    toggleButton = root.Q<Button>("LogToggleButton");

                    return combatLogComponent != null
                        && combatLogUI != null
                        && handle != null
                        && toggleButton != null;
                }
            );

            Assert.IsNotNull(
                combatLogComponent,
                "CombatLog component was not initialized on a game object."
            );
            Assert.IsNotNull(combatLogUI, "CombatLog UI element not found.");
            Assert.IsNotNull(handle, "ResizeHandle UI element not found.");
            Assert.IsNotNull(toggleButton, "LogToggleButton UI element not found.");

            // Add a test log and ensure we can select the generic list view element
            ListView logList = null;
            yield return WaitUntilWithTimeout(
                timeout,
                () =>
                {
                    logList = combatLogUI.Q<ListView>("CombatLog") ?? combatLogUI as ListView;
                    return logList != null;
                }
            );
            Assert.IsNotNull(
                logList,
                "Could not find the ListView associated with the Combat Log."
            );

            CombatLogEntry entry = new CombatLogEntry
            {
                Kind = CombatLogEntryKind.Attack,
                Outcome = CombatLogOutcome.Success,
                Actor = "UI Tester",
                Target = "Training Dummy",
                Action = "Longsword",
                Roll = new CombatLogRoll { Total = 23, DifficultyClass = 18 },
                Damage = new CombatLogDamage { Total = 7 },
            };
            entry.Damage.Parts.Add(new CombatLogDamagePart("slashing", 7));
            entry.Details.Add(new CombatLogDetail("D20 Roll", "23 (15 + 8)"));

            combatLogComponent.LogEntry(entry);
            yield return null;

            Assert.IsTrue(
                logList.itemsSource != null && logList.itemsSource.Count > 0,
                "Combat Log list view items source should not be empty after structured logging."
            );
            Assert.IsInstanceOf<CombatLogEntry>(
                logList.itemsSource[logList.itemsSource.Count - 1],
                "Combat Log list view should be backed by structured entries."
            );

            PushButton(toggleButton);
            yield return null;

            Assert.IsFalse(
                HUDController.GetInstance().logVisible,
                "Combat log should be hidden after toggling visibility."
            );
        }
    }
}
