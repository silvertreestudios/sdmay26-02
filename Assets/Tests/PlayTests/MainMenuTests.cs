using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuTests
{
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
        Button newGameButton = GameObject.Find("NewGameButton")?.GetComponent<Button>();
        Button loadGameButton = GameObject.Find("LoadGameButton")?.GetComponent<Button>();
        Button optionsButton = GameObject.Find("OptionsButton")?.GetComponent<Button>();
        Button quitButton = GameObject.Find("QuitButton")?.GetComponent<Button>();

        Assert.IsNotNull(newGameButton, "New Game button not found");
        Assert.IsNotNull(loadGameButton, "Load Game button not found");
        Assert.IsNotNull(optionsButton, "Options button not found");
        Assert.IsNotNull(quitButton, "Quit button not found");

        yield return null;
    }

    [UnityTest]
    public IEnumerator ButtonsAreInteractable()
    {
        Button newGameButton = GameObject.Find("NewGameButton")?.GetComponent<Button>();
        Button loadGameButton = GameObject.Find("LoadGameButton")?.GetComponent<Button>();
        Button optionsButton = GameObject.Find("OptionsButton")?.GetComponent<Button>();
        Button quitButton = GameObject.Find("QuitButton")?.GetComponent<Button>();

        Assert.IsTrue(newGameButton.interactable, "New Game button should be interactable");
        Assert.IsTrue(loadGameButton.interactable, "Load Game button should be interactable");
        Assert.IsTrue(optionsButton.interactable, "Options button should be interactable");
        Assert.IsTrue(quitButton.interactable, "Quit button should be interactable");

        yield return null;
    }

    [UnityTest]
    public IEnumerator NewGameButtonClick()
    {
        Button newGameButton = GameObject.Find("NewGameButton")?.GetComponent<Button>();
        Assert.IsNotNull(newGameButton, "New Game button not found");

        // Simulate button click
        newGameButton.onClick.Invoke();
        
        // Wait a frame for scene change
        yield return new WaitForSeconds(0.5f);

        // Check if scene changed (adjust expected scene name)
        string currentScene = SceneManager.GetActiveScene().name;
        Assert.AreNotEqual("MainMenuScene", currentScene, "Scene should change after clicking New Game");
    }

    [UnityTest]
    public IEnumerator OptionsButtonClick()
    {
        Button optionsButton = GameObject.Find("OptionsButton")?.GetComponent<Button>();
        Assert.IsNotNull(optionsButton, "Options button not found");

        bool wasClicked = false;
        optionsButton.onClick.AddListener(() => wasClicked = true);
        
        optionsButton.onClick.Invoke();
        yield return null;

        Assert.IsTrue(wasClicked, "Options button click handler was not invoked");
    }


    [UnityTest]
    public IEnumerator ButtonClicksAreRegistered()
    {
        Button[] allButtons = Object.FindObjectsOfType<Button>();
        
        foreach (Button button in allButtons)
        {
            int listenerCount = button.onClick.GetPersistentEventCount();
            Assert.IsTrue(listenerCount > 0, 
                $"Button '{button.gameObject.name}' has no click listeners assigned");
        }

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // Clean up after tests
        yield return null;
    }
}