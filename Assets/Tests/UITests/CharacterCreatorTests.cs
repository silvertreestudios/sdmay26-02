using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEditor.Experimental.GraphView;

namespace TestsUI
{
    public class CharacterCreatorTests
    {
        private UIDocument doc;
        private VisualElement root;

        public void PushButton(Button button)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
        }
        [UnitySetUp]
        public IEnumerator Setup()
        {
            // Load the CharacterCreator scene - add it to Build Settings first!
            yield return SceneManager.LoadSceneAsync("CharacterCreationScene");
            
            doc = Object.FindFirstObjectByType<UIDocument>();
            Assert.IsNotNull(doc, "UIDocument not found in scene");
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
        /// Tests that the tutorial can be completed by simulating clicks on the "Next" button
        /// </summary>
        [UnityTest]
        public IEnumerator CharacterCreatorTutorialCompletes()
        {
            //check if next button still exists, press and wait a frame, reapeat until it dosen't exist anymore
            Button nextButton = root.Q<Button>("NextTutorialButton");
            var script = Object.FindFirstObjectByType<CharacterCreationScript>();
            int steps = script.tutorial.StepCount;
            int clicks = 0;
            while (nextButton.parent.parent.resolvedStyle.display != DisplayStyle.None && clicks < steps)
            {
                nextButton = root.Q<Button>("NextTutorialButton");
                PushButton(nextButton);
                yield return null; // Wait a frame for the UI to update
                
            }
            Assert.IsTrue(nextButton.parent.parent.resolvedStyle.display == DisplayStyle.None, "Tutorial overlay should not be displayed after completing the tutorial");
        }

        /// <summary>
        /// Tests that the tutorial can be skipped by simulating a click on the "Skip" button
        /// </summary>
        [UnityTest]
        public IEnumerator CharacterCreatorTutorialSkips()
        {
            //check if skip button still exists, press and wait a frame, check that the tutorial is gone
            Button skipButton = root.Q<Button>("SkipTutorialButton");
            Assert.IsNotNull(skipButton, "Skip button not found in tutorial");
            PushButton(skipButton);
            yield return null; // Wait a frame for the UI to update
            //check that the tutorial overlay is gone
            Assert.IsTrue(skipButton.parent.parent.resolvedStyle.display == DisplayStyle.None, "Tutorial overlay should not be displayed after skipping the tutorial");
        }

        /// <summary>
        /// Tests that selecting the "Default Barbarian" option and clicking "Finish" transitions to the Level1 scene
        /// </summary>
        [UnityTest]
        public IEnumerator DefaultBarbarianTest()
        {
            Button button = root.Q<Button>("SkipTutorialButton");
            PushButton(button);
            yield return null; // Wait a frame for the UI to update

            button = root.Q<Button>("DefaultBarbarianButton");
            PushButton(button);
            yield return null; // Wait a frame for the UI to update

            button = root.Q<Button>("FinishCharacterCreationButton");
            PushButton(button);
            yield return null; // Wait a frame for the UI to update

            //check that the scene has changed to level 1, wait for transition to complete
            float timeoutTime = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() => SceneManager.GetActiveScene().name == "Level1" || Time.realtimeSinceStartup > timeoutTime);
            Assert.AreEqual("Level1", SceneManager.GetActiveScene().name, "Scene should transition to Level1 after finishing character creation");
        }
    }
}