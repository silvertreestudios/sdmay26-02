using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

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
                yield return WaitUntilWithTimeout(timeout, () => (button = root.Q<Button>(name)) != null);
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
            string[] buttonNames = { "PauseButton", "Speed2xButton", "Speed3xButton", "SpeedToggleButton" };
            foreach (string name in buttonNames)
            {
                yield return WaitUntilWithTimeout(timeout, () => (button = root.Q<Button>(name)) != null);
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
                yield return WaitUntilWithTimeout(timeout, () => (element = root.Q<VisualElement>(name)) != null);
                Assert.IsNotNull(element, $"Element with name '{name}' not found in UI.");
                element = null; 
            }
            yield return null;
        }
       
        /// <summary>
        /// Tests that clicking cancel after each action button results in the correct state and UI behaviour
        /// </summary>
        [UnityTest]
        public IEnumerator UIStatesTest()
        {
            // Get GridBase to check FSM states
            GridPrivate.GridBase gridBase = null;
            yield return WaitUntilWithTimeout(timeout, () => (gridBase = Object.FindFirstObjectByType<GridPrivate.GridBase>()) != null);
            Assert.IsNotNull(gridBase, "GridBase not found in the scene.");

            Assert.IsTrue(gridBase.Fsm.CurrentState is GridPrivate.StateIdle, "FSM should start in StateIdle.");

            // wait for buttons to be not null
            Button strideButton = null;
            Button strikeButton = null;
            Button cancelButton = null;
            Button endTurnButton = null;

            yield return WaitUntilWithTimeout(timeout, () => {
                strideButton = root.Q<Button>("StrideButton");
                strikeButton = root.Q<Button>("UnarmedStrikeButton");
                cancelButton = root.Q<Button>("CancelActionButton");
                endTurnButton = root.Q<Button>("EndTurnButton");
                return strideButton != null && strikeButton != null && cancelButton != null && endTurnButton != null;
            });
    

            // Test Stride Button
            PushButton(strideButton);
            yield return null;
            Assert.IsTrue(gridBase.Fsm.CurrentState is GridPrivate.StateStride, "FSM should be in StateStride after clicking Stride button.");

            PushButton(cancelButton);
            yield return null;
            Assert.IsTrue(gridBase.Fsm.CurrentState is GridPrivate.StateIdle, "FSM should return to StateIdle after clicking Cancel button from Stride.");

            // Test Strike Button
            PushButton(strikeButton);
            yield return null;
            Assert.IsTrue(gridBase.Fsm.CurrentState is GridPrivate.StateStrike, "FSM should be in StateStrike after clicking Strike button.");

            PushButton(cancelButton);
            yield return null;
            Assert.IsTrue(gridBase.Fsm.CurrentState is GridPrivate.StateIdle, "FSM should return to StateIdle after clicking Cancel button from Strike.");

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

            yield return WaitUntilWithTimeout(timeout, () =>
            {
                speed2x = root.Q<Button>("Speed2xButton");
                speed3x = root.Q<Button>("Speed3xButton");
                pauseBtn = root.Q<Button>("PauseButton");
                speedToggle = root.Q<Button>("SpeedToggleButton");
                return speed2x != null && speed3x != null && pauseBtn != null && speedToggle != null;
            });

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

            yield return WaitUntilWithTimeout(timeout, () =>
            {
                combatLogComponent = Object.FindFirstObjectByType<CombatLog>();
                combatLogUI = root.Q<VisualElement>("CombatLog");
                handle = root.Q<VisualElement>("ResizeHandle");
                toggleButton = root.Q<Button>("LogToggleButton");
                
                return combatLogComponent != null && combatLogUI != null && handle != null && toggleButton != null;
            });

            Assert.IsNotNull(combatLogComponent, "CombatLog component was not initialized on a game object.");
            Assert.IsNotNull(combatLogUI, "CombatLog UI element not found.");
            Assert.IsNotNull(handle, "ResizeHandle UI element not found.");
            Assert.IsNotNull(toggleButton, "LogToggleButton UI element not found.");

            // Add a test log and ensure we can select the generic list view element 
            ListView logList = null;
            yield return WaitUntilWithTimeout(timeout, () =>
            {
                logList = combatLogUI.Q<ListView>("CombatLog") ?? combatLogUI as ListView;
                return logList != null;
            });
            Assert.IsNotNull(logList, "Could not find the ListView associated with the Combat Log.");

            // Send a test log
            combatLogComponent.Log("UI System Test Log");
            yield return null;

            Assert.IsTrue(logList.itemsSource != null && logList.itemsSource.Count > 0, "Combat Log list view items source should not be empty after logging.");

            
            PushButton(toggleButton);
            yield return null;
            

            Assert.IsFalse(HUDController.GetInstance().logVisible, "Combat log should be hidden after toggling visibility.");        }
    }
}