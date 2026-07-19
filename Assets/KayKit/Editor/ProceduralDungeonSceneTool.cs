using System;
using System.IO;
using System.Linq;
using Game.DungeonGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.KayKit.Editor
{
    /// <summary>
    /// Rebuilds the deterministic generated-floor fixture and reusable procedural dungeon scene.
    /// </summary>
    public static class ProceduralDungeonSceneTool
    {
        /// <summary>The current-schema fixture used by the reusable scene and PlayMode coverage.</summary>
        public const string FixturePath = "Assets/Maps/KayKit/GeneratedDungeonFixture.json";

        /// <summary>The reusable gameplay scene populated from serialized JSON.</summary>
        public const string ScenePath = "Assets/Scenes/ProceduralDungeon.unity";

        private const string SourceScenePath = "Assets/Scenes/UnitTestingScene.unity";
        private const int FixtureSeed = 156;
        private const int FixtureSize = 31;
        private const int FixtureMinimumRoomSize = 9;
        private const int FixtureMaximumRoomSize = 11;

        /// <summary>Regenerates the fixture and scene after offering to save dirty open scenes.</summary>
        [MenuItem("Tools/KayKit/Regenerate Procedural Dungeon Scene")]
        public static void RegenerateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Regenerate();
        }

        /// <summary>
        /// Batchmode-safe entry point that regenerates required wrappers, catalog data, fixture JSON, and scene.
        /// </summary>
        public static void RegenerateBatch()
        {
            Regenerate();
        }

        private static void Regenerate()
        {
            KayKitSetupTool.RegenerateDungeonAssets();
            DungeonGenerationResult generation = new DeterministicDungeonGenerator().Generate(
                new DungeonGenerationRequest
                {
                    RunSeed = FixtureSeed,
                    Width = FixtureSize,
                    Height = FixtureSize,
                    Layout = DungeonLayout.Box,
                    RoomLayout = DungeonRoomLayout.Packed,
                    CorridorLayout = DungeonCorridorLayout.Straight,
                    MinimumRoomSize = FixtureMinimumRoomSize,
                    MaximumRoomSize = FixtureMaximumRoomSize,
                    MinimumRoomCount = 3,
                    StairCount = 2,
                    DeadEndRemovalPercent = 100
                });
            if (!generation.IsSuccess)
            {
                throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    generation.Diagnostics.Select(diagnostic => diagnostic.Message)));
            }
            if (generation.Document.Objects.Count == 0)
                throw new InvalidOperationException("The procedural fixture seed produced no wall decorations.");

            WriteFixture(DungeonLevelJsonSerializer.Serialize(generation.Document));
            Scene scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            TextAsset fixture = RequireAsset<TextAsset>(FixturePath);
            KayKitDungeonCatalog catalog = RequireAsset<KayKitDungeonCatalog>(
                KayKitSetupTool.DungeonCatalogPath);
            Map map = Object.FindFirstObjectByType<Map>();
            if (map == null)
                throw new InvalidOperationException("The source scene does not contain a Map component.");

            map.ClearLegacyBitmapGeneratedContent();
            map.ConfigureJson(fixture, catalog);
            if (!map.TryGenerate(out MapSourceValidationResult validation))
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));

            RemoveCombatants(scene);
            RemoveNamedRoot(scene, "GameManager");
            RemoveNamedRoot(scene, "UIDocument");
            RemoveNamedRoot(scene, "Grass");
            ConfigureCamera(generation.Document.Width, generation.Document.Height);
            ConfigureLighting();
            if (EditorBuildSettings.scenes.Any(entry =>
                    string.Equals(entry.path, ScenePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "ProceduralDungeon remains excluded from build settings until production-flow integration.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath, false))
                throw new InvalidOperationException($"Could not save generated scene at {ScenePath}.");
            AssetDatabase.SaveAssets();
            Debug.Log($"Generated reusable procedural dungeon scene at {ScenePath}.");
        }

        private static void WriteFixture(string serializedJson)
        {
            string absolutePath = Path.GetFullPath(FixturePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ??
                                      throw new InvalidOperationException("Fixture path has no parent directory."));
            File.WriteAllText(absolutePath, serializedJson);
            AssetDatabase.ImportAsset(FixturePath, ImportAssetOptions.ForceUpdate);
        }

        private static void RemoveCombatants(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<ActionController>() != null)
                    Object.DestroyImmediate(root);
            }
        }

        private static void RemoveNamedRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects()
                         .Where(root => string.Equals(root.name, name, StringComparison.Ordinal))
                         .ToArray())
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureCamera(int width, int height)
        {
            Camera camera = Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Single(candidate =>
                    candidate.enabled &&
                    candidate.gameObject.activeInHierarchy &&
                    candidate.targetTexture == null &&
                    candidate.CompareTag("MainCamera"));
            float centerX = (width - 1) * 0.5f;
            float centerZ = (height - 1) * 0.5f;
            camera.orthographic = false;
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 120f;
            camera.transform.position = new Vector3(centerX, 32f, centerZ - 30f);
            camera.transform.rotation = Quaternion.Euler(48f, 0f, 0f);
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f, 1f);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.transform);

            CameraManager manager = Object.FindFirstObjectByType<CameraManager>();
            if (manager != null)
            {
                manager.enabled = false;
                manager.minCamearYLimit = 6f;
                manager.maxCameraYLimit = 35f;
                EditorUtility.SetDirty(manager);
            }
        }

        private static void ConfigureLighting()
        {
            Light light = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .First(candidate => candidate.type == LightType.Directional);
            light.intensity = 0.8f;
            light.color = new Color(0.78f, 0.84f, 1f);
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.15f, 0.17f, 0.22f);
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(light.transform);
        }

        private static T RequireAsset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null
                ? asset
                : throw new InvalidOperationException($"Missing required asset: {path}");
        }
    }
}
