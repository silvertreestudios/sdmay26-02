using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

namespace TestsUI
{
    public class MainMenuTests
    {
        private UIDocument GetMainMenuDocument()
        {
            return Object.FindFirstObjectByType<UIDocument>();
        }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            // Load the MainMenu scene - add it to Build Settings first!
            SceneManager.LoadScene("MainMenuScene");
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "MainMenuScene");
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTest]
        public IEnumerator MainMenuSceneLoads()
        {
            Assert.AreEqual("MainMenuScene", SceneManager.GetActiveScene().name);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllButtonsExist()
        {
            var doc = GetMainMenuDocument();
            Assert.IsNotNull(doc, "UIDocument not found in scene");
            var root = doc.rootVisualElement;

            Assert.IsNotNull(root.Q<Button>("NewGameButton"), "New Game button not found");
            Assert.IsNotNull(root.Q<Button>("LoadGameButton"), "Load Game button not found");
            Assert.IsNotNull(root.Q<Button>("OptionsButton"), "Options button not found");
            Assert.IsNotNull(root.Q<Button>("ExitButton"), "Exit button not found");

            yield return null;
        }

        [UnityTest]
        public IEnumerator ButtonsAreInteractable()
        {
            var doc = GetMainMenuDocument();
            Assert.IsNotNull(doc, "UIDocument not found in scene");
            var root = doc.rootVisualElement;

            Assert.IsTrue(root.Q<Button>("NewGameButton").enabledSelf, "New Game button should be interactable");
            Assert.IsTrue(root.Q<Button>("LoadGameButton").enabledSelf, "Load Game button should be interactable");
            Assert.IsTrue(root.Q<Button>("OptionsButton").enabledSelf, "Options button should be interactable");
            Assert.IsTrue(root.Q<Button>("ExitButton").enabledSelf, "Exit button should be interactable");

            yield return null;
        }

        [UnityTest]
        public IEnumerator NewGameButtonClick()
        {
            var doc = GetMainMenuDocument();
            Assert.IsNotNull(doc, "UIDocument not found in scene");
            var button = doc.rootVisualElement.Q<Button>("NewGameButton");
            Assert.IsNotNull(button, "New Game button not found");

            // Simulate button click
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
            
            // Wait a frame for scene change
            yield return new WaitForSeconds(0.5f);

            // Check if scene changed according to MainMenuControl.NewGame()
            string currentScene = SceneManager.GetActiveScene().name;
            Assert.AreEqual("CharacterCreationScene", currentScene, "Scene should change to CharacterCreationScene after clicking New Game");
        }

        [UnityTest]
        public IEnumerator LoadGameButtonClick()
        {
            var doc = GetMainMenuDocument();
            Assert.IsNotNull(doc, "UIDocument not found in scene");
            var button = doc.rootVisualElement.Q<Button>("LoadGameButton");
            Assert.IsNotNull(button, "Load Game button not found");

            // Simulate button click
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
            
            // Wait a frame for scene change
            yield return new WaitForSeconds(0.5f);

            // Check if scene changed according to MainMenuControl.LoadGame()
            string currentScene = SceneManager.GetActiveScene().name;
            Assert.AreEqual("Level1", currentScene, "Scene should change to Level1 after clicking Load Game");
        }

        [UnityTest]
        public IEnumerator OptionsButtonClick()
        {
            var doc = GetMainMenuDocument();
            Assert.IsNotNull(doc, "UIDocument not found in scene");
            var button = doc.rootVisualElement.Q<Button>("OptionsButton");
            Assert.IsNotNull(button, "Options button not found");

            bool wasClicked = false;
            button.clicked += () => wasClicked = true;
            
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
            
            yield return null;

            Assert.IsTrue(wasClicked, "Options button click handler was not invoked");
        }

        // TODO test Exit button click - may require mocking Application.Quit() or checking for log output

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Clean up after tests
            yield return null;
        }
    }
}