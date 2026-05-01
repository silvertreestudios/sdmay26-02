using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

namespace TestsUI
{
    public class MainMenuTests : PlayModeBase
    {

        [UnitySetUp]
        public override IEnumerator Setup()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("MainMenuScene");
            
            doc = Object.FindFirstObjectByType<UIDocument>();
            Assert.IsNotNull(doc, "UIDocument not found in MainMenuScene");
            
            root = doc.rootVisualElement;
            Assert.IsNotNull(root, "Root VisualElement not found in UIDocument");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Destroy the persistent SceneTransitionManager if it was spawned by a button click
            var transitionManager = GameObject.Find("SceneTransitionManager");
            if (transitionManager != null)
            {
                Object.Destroy(transitionManager);
                yield return null; // Wait a frame for destruction to apply
            }
        }

        /// <summary>
        /// Tests that the main menu scene loads correctly
        /// </summary>
        [UnityTest]
        public IEnumerator MainMenuSceneLoads()
        {
            Assert.AreEqual("MainMenuScene", SceneManager.GetActiveScene().name);
            yield return null;
        }

        /// <summary>
        /// Tests that all buttons are present in the main menu
        /// </summary>
        [UnityTest]
        public IEnumerator AllButtonsExist()
        {
            Assert.IsNotNull(root.Q<Button>("CharacterCreationButton"), "Character Creation button not found");
            Assert.IsNotNull(root.Q<Button>("QuickPlayButton"), "Quick Play button not found");
            Assert.IsNotNull(root.Q<Button>("OptionsButton"), "Options button not found");
            Assert.IsNotNull(root.Q<Button>("ExitButton"), "Exit button not found");

            yield return null;
        }

        /// <summary>
        /// Tests that all buttons are interactable in the main menu
        /// </summary>
        [UnityTest]
        public IEnumerator ButtonsAreInteractable()
        {
            Assert.IsTrue(root.Q<Button>("CharacterCreationButton").enabledSelf, "Character Creation button should be interactable");
            Assert.IsTrue(root.Q<Button>("QuickPlayButton").enabledSelf, "Quick Play button should be interactable");
            Assert.IsTrue(root.Q<Button>("OptionsButton").enabledSelf, "Options button should be interactable");
            Assert.IsTrue(root.Q<Button>("ExitButton").enabledSelf, "Exit button should be interactable");

            yield return null;
        }

        /// <summary>
        /// Tests that clicking the Character Creation button loads the CharacterCreationScene
        /// </summary>
        [UnityTest]
        public IEnumerator CharacterCreationButtonClick()
        {
            var button = root.Q<Button>("CharacterCreationButton");
            Assert.IsNotNull(button, "Character Creation button not found");

            // Simulate button click
            PushButton(button);
            
            // Wait until the scene changes or it times out
            float timeoutTime = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "CharacterCreationScene" || Time.realtimeSinceStartup > timeoutTime);

            // Check if scene changed according to MainMenuControl.NewGame()
            string currentScene = SceneManager.GetActiveScene().name;
            Assert.AreEqual("CharacterCreationScene", currentScene, "Scene should change to CharacterCreationScene after clicking New Game");
        }

        /// <summary>
        /// Tests that clicking the Quick Play button loads the Level1 scene
        /// </summary>
        [UnityTest]
        public IEnumerator QuickPlayButtonClick()
        {
            var button = root.Q<Button>("QuickPlayButton");
            Assert.IsNotNull(button, "Quick Play button not found");

            // Simulate button click
            PushButton(button);
            
            // Wait until the scene changes or it times out
            float timeoutTime = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Level1" || Time.realtimeSinceStartup > timeoutTime);

            // Check if scene changed according to MainMenuControl.LoadGame()
            string currentScene = SceneManager.GetActiveScene().name;
            Assert.AreEqual("Level1", currentScene, "Scene should change to Level1 after clicking Load Game");
        }

        /// <summary>
        /// Tests that clicking the Options button invokes its click handler
        /// </summary>
        [UnityTest]
        public IEnumerator OptionsButtonClick()
        {
            var button = root.Q<Button>("OptionsButton");
            Assert.IsNotNull(button, "Options button not found");

            bool wasClicked = false;
            button.clicked += () => wasClicked = true;
            
            // Simulate button click
            PushButton(button);
            
            yield return null;

            Assert.IsTrue(wasClicked, "Options button click handler was not invoked");
        }

        /// <summary>
        /// Tests that clicking the Exit button invokes its click handler
        /// </summary>
        [UnityTest]
        public IEnumerator ExitButtonClick()
        {
            var button = root.Q<Button>("ExitButton");
            Assert.IsNotNull(button, "Exit button not found");
            // Simulate button click
            PushButton(button);
            yield return null;

            // Expect the specific debug log message
            LogAssert.Expect(LogType.Log, "Clicked Exit button");
        }
    }
}