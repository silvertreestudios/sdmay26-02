using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class StoryBoardControl : MonoBehaviour
{
    private VisualElement ui;
    private Button continueButton;

    private void Awake()
    {
        ui = GetComponent<UIDocument>().rootVisualElement;
        ui.style.display = DisplayStyle.None;
    }

    private void Start()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        TextAsset jsonAsset = Resources.Load<TextAsset>("storyboard");
        if (jsonAsset == null) return;

        StoryBoardData data = JsonUtility.FromJson<StoryBoardData>(jsonAsset.text);
        string message = data.GetMessage(sceneName);
        if (message == null) return;

        ui.Q<Label>("StoryText").text = message;

        continueButton = ui.Q<Button>("ContinueButton");
        continueButton.clicked += Close;

        Time.timeScale = 0f;
        ui.style.display = DisplayStyle.Flex;
    }

    private void OnDestroy()
    {
        if (continueButton != null)
            continueButton.clicked -= Close;
    }

    private void Close()
    {
        ui.style.display = DisplayStyle.None;
        StartCoroutine(ResumeAfterDelay(0.5f));
    }

    private IEnumerator ResumeAfterDelay(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        Time.timeScale = 1f;
    }

    [System.Serializable]
    private class StoryBoardData
    {
        public string Level1;
        public string Level2;
        public string Level3;

        public string GetMessage(string sceneName) => sceneName switch
        {
            "Level1" => Level1,
            "Level2" => Level2,
            "Level3" => Level3,
            _ => null
        };
    }
}
