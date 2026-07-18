using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.KayKit.Editor
{
    [CustomEditor(typeof(Map))]
    public sealed class MapEditor : UnityEditor.Editor
    {
        private string[] validationErrors;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty sourceMode = serializedObject.FindProperty("sourceMode");
            EditorGUILayout.PropertyField(sourceMode);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spacing"));

            if ((MapSourceMode)sourceMode.enumValueIndex == MapSourceMode.Bitmap)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ImageMap"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Settings"));
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("jsonSource"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("dungeonCatalog"));
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate"))
                    Validate((Map)target);
                if (GUILayout.Button("Generate"))
                    Generate((Map)target);
            }
            if (GUILayout.Button("Clear Generated Content"))
                Clear((Map)target);

            if (validationErrors == null)
                return;
            if (validationErrors.Length == 0)
            {
                EditorGUILayout.HelpBox("Map source is valid.", MessageType.Info);
                return;
            }

            foreach (string error in validationErrors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void Validate(Map map)
        {
            MapSourceValidationResult result = map.ValidateSource();
            validationErrors = result.Errors.ToArray();
            if (result.IsValid)
                Debug.Log("Map validation passed.", map);
            else
                Debug.LogError("Map validation failed:\n" + string.Join("\n", result.Errors), map);
        }

        private void Generate(Map map)
        {
            Undo.RegisterFullObjectHierarchyUndo(map.gameObject, "Generate Map");
            if (!map.TryGenerate(out MapSourceValidationResult result))
            {
                validationErrors = result.Errors.ToArray();
                Debug.LogError("Map generation failed:\n" + string.Join("\n", result.Errors), map);
                return;
            }

            validationErrors = System.Array.Empty<string>();
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Debug.Log("Map generation completed.", map);
        }

        private void Clear(Map map)
        {
            Undo.RegisterFullObjectHierarchyUndo(map.gameObject, "Clear Generated Map Content");
            map.ClearGeneratedContent();
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            validationErrors = null;
        }
    }
}
