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
        StoryEntry entry = data.GetEntry(sceneName);
        if (entry == null) return;

        ui.Q<Label>("StoryBoardTitle").text = entry.Title;
        ui.Q<Label>("StoryText").text = entry.Message;

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
    private class StoryEntry
    {
        public string Title;
        public string Message;
    }

    [System.Serializable]
    private class StoryBoardData
    {
        public StoryEntry Level1;
        public StoryEntry Level2;
        public StoryEntry Level3;

        public StoryEntry GetEntry(string sceneName) => sceneName switch
        {
            "Level1" => Level1,
            "Level2" => Level2,
            "Level3" => Level3,
            _ => null
        };
    }
}
