using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;


public abstract class PlayModeBase
{
    private float timeout = 5f; // 5 seconds timeout
        private Button button = null;
        private UIDocument doc;
        private VisualElement root;
        private GameObject player;

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
        public IEnumerator Setup()
        {
            Time.timeScale = 1f; 
            yield return SceneManager.LoadSceneAsync("UnitTestingScene");
            doc = Object.FindFirstObjectByType<UIDocument>();
            root = doc.rootVisualElement;
        }
}