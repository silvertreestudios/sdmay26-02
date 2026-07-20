#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Sign))]
public class SignEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "SelectedKey");

        Sign sign = (Sign)target;
        string[] keys = sign
            .Messages.Select(m => m.Key)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToArray();

        if (keys.Length > 0)
        {
            int current = Mathf.Max(0, System.Array.IndexOf(keys, sign.SelectedKey));
            int selected = EditorGUILayout.Popup("Selected Message", current, keys);
            if (selected != current || sign.SelectedKey != keys[selected])
            {
                Undo.RecordObject(sign, "Change Sign Message");
                sign.SelectedKey = keys[selected];
                EditorUtility.SetDirty(sign);
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Add entries to Messages first, then keys will appear here.",
                MessageType.Info
            );
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
