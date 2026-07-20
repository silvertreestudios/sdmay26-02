using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct SignEntry
{
    public string Key;

    [TextArea(2, 6)]
    public string Message;
}

public class Sign : MonoBehaviour
{
    [SerializeField]
    public List<SignEntry> Messages = new();

    [SerializeField]
    public string SelectedKey;

    [SerializeField]
    float BubbleHeightOffset = 2f;

    void Update()
    {
        if (!InputCompat.LeftClickDown())
            return;
        Ray ray = Camera.main.ScreenPointToRay(InputCompat.MousePositionScreen());
        if (!Physics.Raycast(ray, out RaycastHit hit))
            return;
        if (hit.collider.gameObject != gameObject)
            return;

        SignEntry entry = Messages.Find(e => e.Key == SelectedKey);
        Debug.Log(
            $"[Sign] Clicked: {gameObject.name}, key='{SelectedKey}', message='{entry.Message}', controller={SignBubbleController.Instance != null}"
        );
        if (!string.IsNullOrEmpty(entry.Message))
            SignBubbleController.Instance.Show(
                entry.Message,
                transform.position + Vector3.up * BubbleHeightOffset
            );
        else
            Debug.LogWarning(
                $"[Sign] No message found for key '{SelectedKey}' on {gameObject.name}"
            );
    }
}
