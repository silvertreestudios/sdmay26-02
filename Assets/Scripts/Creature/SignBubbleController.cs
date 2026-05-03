using UnityEngine;
using UnityEngine.UIElements;

public class SignBubbleController : MonoBehaviour
{
    public static SignBubbleController Instance { get; private set; }

    [SerializeField] UIDocument Document;

    VisualElement _root;
    Label _text;
    bool _open;
    bool _skipFrame;
    float _timer;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        _root = Document.rootVisualElement.Q("bubble-root");
        _text = Document.rootVisualElement.Q<Label>("bubble-text");
        _root.style.display = DisplayStyle.None;
    }

    public void Show(string message, Vector3 worldPos)
    {
        transform.position = worldPos;
        _text.text = message;
        _root.style.display = DisplayStyle.Flex;
        _open = true;
        _skipFrame = true;
        _timer = 5f;
    }

    void Update()
    {
        if (!_open) return;

        transform.rotation = Camera.main.transform.rotation;

        _timer -= Time.deltaTime;
        if (_timer <= 0f) { Hide(); return; }

        if (_skipFrame) { _skipFrame = false; return; }
        if (InputCompat.LeftClickDown())
            Hide();
    }

    void Hide()
    {
        _root.style.display = DisplayStyle.None;
        _open = false;
    }
}
