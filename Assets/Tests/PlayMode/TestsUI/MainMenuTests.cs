using System;
using System.Collections;
using System.IO;
using Game.DungeonPersistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace TestsUI
{
    public class MainMenuTests : PlayModeBase
    {
        private string autosaveDirectory;
        private DungeonRunLaunchRequest capturedLaunch = DungeonRunLaunchRequest.None;
        private int launchCount;
        private MainMenuControl menu;

        [UnitySetUp]
        public override IEnumerator Setup()
        {
            Time.timeScale = 1f;
            launchCount = 0;
            capturedLaunch = DungeonRunLaunchRequest.None;
            yield return SceneManager.LoadSceneAsync("MainMenuScene");

            doc = Object.FindFirstObjectByType<UIDocument>();
            Assert.IsNotNull(doc, "UIDocument not found in MainMenuScene");

            root = doc.rootVisualElement;
            Assert.IsNotNull(root, "Root VisualElement not found in UIDocument");

            autosaveDirectory = Path.Combine(
                Application.temporaryCachePath,
                "issue-154-menu-" + Guid.NewGuid().ToString("N")
            );
            menu = Object.FindFirstObjectByType<MainMenuControl>();
            Assert.That(menu, Is.Not.Null);
            menu.ConfigureForTests(
                new DungeonRunMenuService(autosaveDirectory, () => 0x00000001FFFFFFFFL),
                request =>
                {
                    launchCount++;
                    capturedLaunch = request;
                }
            );
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
            if (Directory.Exists(autosaveDirectory))
                Directory.Delete(autosaveDirectory, recursive: true);
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
            Assert.IsNotNull(root.Q<TextField>("SeedField"), "Seed field not found");
            Assert.IsNotNull(root.Q<Button>("NewRunButton"), "New Run button not found");
            Assert.IsNotNull(root.Q<Button>("ContinueButton"), "Continue button not found");
            Assert.IsNotNull(root.Q<Label>("MenuStatusLabel"), "Menu status not found");
            Assert.IsNotNull(root.Q("OverwriteConfirmationOverlay"), "Confirmation not found");
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
            Assert.IsTrue(
                root.Q<Button>("NewRunButton").enabledSelf,
                "New Run button should be interactable"
            );
            Assert.IsFalse(
                root.Q<Button>("ContinueButton").enabledSelf,
                "Continue must be disabled without an autosave"
            );
            Assert.IsTrue(
                root.Q<Button>("OptionsButton").enabledSelf,
                "Options button should be interactable"
            );
            Assert.IsTrue(
                root.Q<Button>("ExitButton").enabledSelf,
                "Exit button should be interactable"
            );

            yield return null;
        }

        /// <summary>
        /// Tests that an explicit signed 64-bit seed is normalized before launch.
        /// </summary>
        [UnityTest]
        public IEnumerator ExplicitSeedNewRunCreatesLaunchRequest()
        {
            root.Q<TextField>("SeedField").value = "9223372036854775807";

            PushButton(root.Q<Button>("NewRunButton"));
            yield return null;

            Assert.That(capturedLaunch.Mode, Is.EqualTo(DungeonRunLaunchMode.NewRun));
            Assert.That(capturedLaunch.NormalizedSeed, Is.EqualTo(int.MinValue));
            Assert.That(root.Q<Label>("MenuStatusLabel").text, Does.Contain("-2147483648"));
        }

        /// <summary>
        /// Tests that a blank field uses the injected entropy source.
        /// </summary>
        [UnityTest]
        public IEnumerator BlankSeedNewRunUsesAutomaticSeed()
        {
            root.Q<TextField>("SeedField").value = string.Empty;

            PushButton(root.Q<Button>("NewRunButton"));
            yield return null;

            Assert.That(capturedLaunch.Mode, Is.EqualTo(DungeonRunLaunchMode.NewRun));
            Assert.That(capturedLaunch.NormalizedSeed, Is.EqualTo(-2));
        }

        /// <summary>Tests that malformed and overflowing seeds remain on the menu.</summary>
        [UnityTest]
        public IEnumerator InvalidSeedShowsPlayerFacingMessage()
        {
            root.Q<TextField>("SeedField").value = "9223372036854775808";

            PushButton(root.Q<Button>("NewRunButton"));
            yield return null;

            Assert.That(capturedLaunch.Mode, Is.EqualTo(DungeonRunLaunchMode.None));
            Label status = root.Q<Label>("MenuStatusLabel");
            Assert.That(status.text, Does.Contain("whole-number seed"));
            Assert.That(status.ClassListContains("menu-status--error"), Is.True);
        }

        /// <summary>Tests that repeated launch input cannot enqueue multiple scene loads.</summary>
        [UnityTest]
        public IEnumerator RepeatedNewRunInputLaunchesOnlyOnce()
        {
            root.Q<TextField>("SeedField").value = "154";

            menu.StartNewRun();
            menu.StartNewRun();
            yield return null;

            Assert.That(launchCount, Is.EqualTo(1));
            Assert.That(root.enabledSelf, Is.False);
        }

        /// <summary>Tests that a rejected scene transition restores the menu for another attempt.</summary>
        [UnityTest]
        public IEnumerator RejectedDungeonLaunchRestoresInteractiveMenu()
        {
            int attempts = 0;
            menu.ConfigureLaunchResultForTests(
                new DungeonRunMenuService(autosaveDirectory, () => 154L),
                _ =>
                {
                    attempts++;
                    return false;
                }
            );
            root.Q<TextField>("SeedField").value = "154";

            menu.StartNewRun();
            yield return null;

            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(root.enabledSelf, Is.True);
            Label status = root.Q<Label>("MenuStatusLabel");
            Assert.That(status.text, Does.Contain("transition"));
            Assert.That(status.ClassListContains("menu-status--error"), Is.True);

            menu.StartNewRun();
            yield return null;

            Assert.That(attempts, Is.EqualTo(2));
        }

        /// <summary>
        /// Tests that an existing invalid autosave still requires explicit replacement and that
        /// cancel leaves the file untouched.
        /// </summary>
        [UnityTest]
        public IEnumerator ExistingAutosaveRequiresConfirmationBeforeReplacement()
        {
            Directory.CreateDirectory(autosaveDirectory);
            string autosavePath = Path.Combine(autosaveDirectory, "autosave.json");
            File.WriteAllText(autosavePath, "{}");
            menu.ConfigureForTests(
                new DungeonRunMenuService(autosaveDirectory, () => 41L),
                request => capturedLaunch = request
            );

            PushButton(root.Q<Button>("NewRunButton"));
            yield return null;

            VisualElement overlay = root.Q("OverwriteConfirmationOverlay");
            Assert.That(overlay.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(capturedLaunch.Mode, Is.EqualTo(DungeonRunLaunchMode.None));

            PushButton(root.Q<Button>("CancelOverwriteButton"));
            yield return null;
            Assert.That(File.Exists(autosavePath), Is.True);
            Assert.That(overlay.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            PushButton(root.Q<Button>("NewRunButton"));
            PushButton(root.Q<Button>("ConfirmOverwriteButton"));
            yield return null;
            Assert.That(capturedLaunch.Mode, Is.EqualTo(DungeonRunLaunchMode.NewRun));
            Assert.That(capturedLaunch.NormalizedSeed, Is.EqualTo(41));
        }

        /// <summary>Tests that a corrupt autosave disables Continue with a concise explanation.</summary>
        [UnityTest]
        public IEnumerator CorruptAutosaveDisablesContinue()
        {
            Directory.CreateDirectory(autosaveDirectory);
            File.WriteAllText(Path.Combine(autosaveDirectory, "autosave.json"), "{}");
            menu.ConfigureForTests(
                new DungeonRunMenuService(autosaveDirectory, () => 41L),
                request => capturedLaunch = request
            );

            yield return null;

            Assert.That(root.Q<Button>("ContinueButton").enabledSelf, Is.False);
            Assert.That(root.Q<Label>("MenuStatusLabel").text, Does.Contain("corrupt"));
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
            LogAssert.Expect(LogType.Log, "Clicked Exit button");

            // Simulate button click
            PushButton(button);
            yield return null;
        }
    }
}
