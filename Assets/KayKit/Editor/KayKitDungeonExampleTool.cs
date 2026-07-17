using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.KayKit.Editor
{
    public static class KayKitDungeonExampleTool
    {
        public const string JsonPath = "Assets/Maps/KayKit/KayKitDungeonExample.json";
        public const string ScenePath = "Assets/Scenes/KayKitDungeonExample.unity";
        private const string SourceScenePath = "Assets/Scenes/UnitTestingScene.unity";

        [MenuItem("Tools/KayKit/Regenerate Dungeon Example")]
        public static void RegenerateScene()
        {
            TextAsset source = RequireAsset<TextAsset>(JsonPath);
            KayKitDungeonCatalog catalog = RequireAsset<KayKitDungeonCatalog>(
                KayKitSetupTool.DungeonCatalogPath);
            if (!ConfirmSceneTransition(
                    HasDirtyOpenScenes(),
                    EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo))
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);

            Map map = Object.FindFirstObjectByType<Map>();
            if (map == null)
                throw new InvalidOperationException("Source scene does not contain a Map component.");
            map.ClearLegacyBitmapGeneratedContent();
            Undo.RecordObject(map, "Configure KayKit dungeon JSON source");
            map.ConfigureJson(source, catalog);
            EditorUtility.SetDirty(map);
            PrefabUtility.RecordPrefabInstancePropertyModifications(map);
            if (!map.TryGenerate(out MapSourceValidationResult validation))
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));

            RemoveExistingCombatants(scene);
            RemoveRootObjects(scene, "Grass");
            CreateEncounter();
            ConfigureCamera();
            ConfigureLighting();

            if (EditorBuildSettings.scenes.Any(entry =>
                    string.Equals(entry.path, ScenePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "KayKitDungeonExample must remain excluded from EditorBuildSettings.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath, false))
                throw new InvalidOperationException($"Could not save generated scene at {ScenePath}.");
            AssetDatabase.SaveAssets();
            Debug.Log($"Generated standalone KayKit dungeon example at {ScenePath}.");
        }

        private static bool HasDirtyOpenScenes()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                    return true;
            }

            return false;
        }

        private static bool ConfirmSceneTransition(bool hasDirtyScenes, Func<bool> savePrompt)
        {
            return !hasDirtyScenes || savePrompt();
        }

        private static void CreateEncounter()
        {
            InstantiateCreature("Assets/Prefabs/Creatures/Lena.prefab", "Lena", "Players", new Vector3(2f, 0f, 1f));
            InstantiateCreature("Assets/Prefabs/Creatures/Torgrim.prefab", "Torgrim", "Players", new Vector3(2f, 0f, 3f));
            InstantiateCreature(
                "Assets/Prefabs/Creatures/zombie-shambler.prefab",
                "Zombie Shambler A",
                "Enemies",
                new Vector3(13f, 0f, 10f));
            InstantiateCreature(
                "Assets/Prefabs/Creatures/zombie-shambler.prefab",
                "Zombie Shambler B",
                "Enemies",
                new Vector3(12f, 0f, 8f));
            InstantiateCreature(
                "Assets/Prefabs/Creatures/skeleton-guard.prefab",
                "Skeleton Guard A",
                "Enemies",
                new Vector3(13f, 0f, 5f));
            InstantiateCreature(
                "Assets/Prefabs/Creatures/skeleton-guard.prefab",
                "Skeleton Guard B",
                "Enemies",
                new Vector3(6f, 0f, 9f));
        }

        private static void InstantiateCreature(
            string path,
            string name,
            string teamName,
            Vector3 position)
        {
            GameObject prefab = RequireAsset<GameObject>(path);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.identity;
            Team team = instance.GetComponent<Team>();
            if (team == null)
                throw new InvalidOperationException($"Creature prefab is missing Team: {path}");
            team.Name = teamName;
        }

        private static void ConfigureCamera()
        {
            Camera camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null)
                throw new InvalidOperationException("Source scene does not contain a Camera.");

            camera.orthographic = true;
            camera.orthographicSize = 8.25f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(7.5f, 14f, -2.6f);
            camera.transform.LookAt(new Vector3(7.5f, 0f, 5.5f));
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.065f, 1f);
        }

        private static void ConfigureLighting()
        {
            Light light = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.type == LightType.Directional);
            if (light == null)
                throw new InvalidOperationException("Source scene does not contain a Directional Light.");

            light.intensity = 0.9f;
            light.color = new Color(0.82f, 0.88f, 1f);
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.ambientLight = new Color(0.18f, 0.2f, 0.26f);
        }

        private static void RemoveExistingCombatants(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<ActionController>() != null)
                    Object.DestroyImmediate(root);
            }
        }

        private static void RemoveRootObjects(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects()
                         .Where(root => string.Equals(root.name, name, StringComparison.Ordinal))
                         .ToArray())
            {
                Object.DestroyImmediate(root);
            }
        }

        private static T RequireAsset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Missing required asset: {path}");
            return asset;
        }
    }
}
