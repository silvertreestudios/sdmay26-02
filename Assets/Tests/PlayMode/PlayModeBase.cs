using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using NUnit.Framework;


public abstract class PlayModeBase
{
    protected float timeout = 5f; // 5 seconds timeout
    protected Button button = null;
    protected UIDocument doc;
    protected VisualElement root;
    protected GameObject player;

    public void PushButton(Button button)
    {
        using (var evt = NavigationSubmitEvent.GetPooled())
        {
            evt.target = button;
            button.SendEvent(evt);
        }
    }

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

    public IEnumerator WaitUntilWithTimeout(float maxTime, System.Func<bool> condition)
    {
        float timer = 0f;
        while (timer < maxTime && !condition())
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    [UnitySetUp]
    public virtual IEnumerator Setup()
    {
        Time.timeScale = 1f; // Reset time scale to normal for any subsequent tests
        yield return SceneManager.LoadSceneAsync("UnitTestingScene");
        doc = Object.FindFirstObjectByType<UIDocument>();
        root = doc.rootVisualElement;
    }
}