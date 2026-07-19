using System.Collections;
using System.Collections.Generic;
using GridPrivate;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public abstract class PlayModeBase
{
    protected float timeout = 5f; // 5 seconds timeout
    protected Button button = null;
    protected UIDocument doc;
    protected VisualElement root;
    protected GameObject player;

    /// <summary>
    /// pushes a buton... nuff said
    /// </summary>
    public void PushButton(Button button)
    {
        using (var evt = NavigationSubmitEvent.GetPooled())
        {
            evt.target = button;
            button.SendEvent(evt);
        }
    }

    /// <summary>
    /// Gets the names of the action buttons that should be present in the UI based on the actions available to the player
    /// </summary>
    public List<string> GetActionButtons(GameObject player)
    {
        List<string> buttonNames = new List<string>();
        ActionController ac = player.GetComponent<ActionController>();
        if (ac != null)
        {
            List<EntityAction> playerActions = ac.GetActions();
            foreach (EntityAction action in playerActions)
            {
                buttonNames.Add(action.ActionName.Replace(" ", "") + "Button");
            }
        }
        return buttonNames;
    }

    /// <summary>
    /// Waits until a condition is true or a timeout is reached, whichever comes first. Used to wait for UI elements to appear or states to change without freezing the test indefinitely.
    /// Use Lambda functions to execute custom code for every loop while waiting; do this when waiting to set a reference for a UI element.
    /// </summary>
    public IEnumerator WaitUntilWithTimeout(float maxTime, System.Func<bool> condition)
    {
        float timer = 0f;
        while (timer < maxTime && !condition())
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// waits to set the active scene to the UnitTestingScene, then finds the UIDocument and root visual element in that scene and sets them for use in the tests. This runs before every test in this class and any subclasses, ensuring a fresh scene and UI for each test.
    /// </summary>
    [UnitySetUp]
    public virtual IEnumerator Setup()
    {
        Time.timeScale = 1f; // Reset time scale to normal for any subsequent tests
        yield return SceneManager.LoadSceneAsync("UnitTestingScene");
        doc = Object.FindFirstObjectByType<UIDocument>();
        root = doc.rootVisualElement;

        // Disable GridInput to prevent real mouse movements from overriding our injected hover events
        GridInput gridInput = Object.FindFirstObjectByType<GridInput>();
        if (gridInput != null)
        {
            gridInput.enabled = false;
        }
    }
}
