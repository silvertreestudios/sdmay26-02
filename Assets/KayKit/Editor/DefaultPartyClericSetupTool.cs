using System;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using GridPublic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.KayKit.Editor
{
    /// <summary>
    /// Authors the Resources-backed Maren player prefab and installs it into the default party.
    /// </summary>
    /// <remarks>
    /// This focused tool deliberately reuses the existing player presentation hierarchy without
    /// regenerating unrelated dungeon or KayKit assets. The broader generators retain equivalent
    /// Maren entries so later full regeneration cannot remove the integration.
    /// </remarks>
    public static class DefaultPartyClericSetupTool
    {
        /// <summary>The checked-in player prefab produced by this tool.</summary>
        public const string MarenPrefabPath = "Assets/Prefabs/Creatures/Maren.prefab";

        private const string PlayerTemplatePath = "Assets/Prefabs/Creatures/Lena.prefab";
        private const string RosterSlotId = "party-slot-maren";
        private const string CreatureContentId = "player-character-maren";

        /// <summary>
        /// Regenerates the Maren default-party assets after allowing the developer to save or
        /// cancel changes to open scenes.
        /// </summary>
        [MenuItem("Tools/Creatures/Regenerate Default Party Cleric")]
        public static void RegenerateFromMenu()
        {
            TryRegenerateFromMenu(
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo,
                RegenerateBatch
            );
        }

        /// <summary>
        /// Batchmode-safe entry point that regenerates only the assets owned by the Maren
        /// default-party integration.
        /// </summary>
        public static void RegenerateBatch()
        {
            CreatureVisualCatalog catalog = AddMarenVisualMapping();
            GenerateMarenPrefab(catalog);
            AddMarenToProceduralDungeon();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Regenerated Maren default-party assets.");
        }

        private static bool TryRegenerateFromMenu(Func<bool> saveOrDiscardPrompt, Action regenerate)
        {
            if (!saveOrDiscardPrompt())
                return false;

            regenerate();
            return true;
        }

        private static CreatureVisualCatalog AddMarenVisualMapping()
        {
            CreatureVisualCatalog catalog = RequireAsset<CreatureVisualCatalog>(
                KayKitAnimatedCreatureSetupTool.CreatureVisualCatalogPath
            );
            if (!catalog.TryResolve("Cleric", out CreatureVisualCatalogEntry cleric))
                throw new InvalidOperationException(
                    "The creature visual catalog has no approved Cleric presentation."
                );

            CreatureVisualCatalogEntry maren = new(
                "Maren",
                cleric.VisualId,
                cleric.Species,
                cleric.VisualPrefab
            );
            CreatureVisualCatalogEntry[] entries = catalog
                .Entries.Where(entry =>
                    !string.Equals(entry.Key, "Maren", StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
            int insertionIndex = Array.FindIndex(
                entries,
                entry => string.Equals(entry.Key, "Torgrim", StringComparison.OrdinalIgnoreCase)
            );
            insertionIndex = insertionIndex < 0 ? entries.Length : insertionIndex + 1;
            catalog.ReplaceEntries(
                entries
                    .Take(insertionIndex)
                    .Concat(new[] { maren })
                    .Concat(entries.Skip(insertionIndex))
            );
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void GenerateMarenPrefab(CreatureVisualCatalog catalog)
        {
            GameObject template = RequireAsset<GameObject>(PlayerTemplatePath);
            GameObject resourcesObject = CreatureJsonConverter.CreateByName("Maren");
            if (resourcesObject == null)
                throw new InvalidOperationException(
                    "The checked-in Maren Resources definition could not be loaded."
                );
            GameObject instance = Object.Instantiate(template);

            try
            {
                instance.name = "Maren";
                CreatureComponent creature = RequireComponent<CreatureComponent>(instance);
                EditorUtility.CopySerialized(
                    RequireComponent<CreatureComponent>(resourcesObject),
                    creature
                );
                creature.name = "Maren";
                Team team = RequireComponent<Team>(instance);
                team.Name = "Players";

                DungeonPartyMemberIdentity oldIdentity =
                    instance.GetComponent<DungeonPartyMemberIdentity>();
                if (oldIdentity != null)
                    Object.DestroyImmediate(oldIdentity);
                DungeonPartyMemberIdentity identity =
                    instance.AddComponent<DungeonPartyMemberIdentity>();
                identity.Configure(RosterSlotId, CreatureContentId);

                TokenMeshSelection selector = instance.GetComponentInChildren<TokenMeshSelection>(
                    true
                );
                Transform visualRoot = instance.transform.Find("VisualRoot");
                if (selector == null || visualRoot == null)
                    throw new InvalidOperationException(
                        "The player presentation template is missing its animated visual wiring."
                    );
                selector.ConfigureAnimatedCatalog(catalog, visualRoot);

                RequireComponent<PlayerActionController>(instance);
                RequireComponent<Token>(instance);
                Portrait portrait = RequireComponent<Portrait>(instance);
                if (portrait.GetComponentInChildren<Camera>(true) == null)
                    throw new InvalidOperationException(
                        "The player presentation template is missing its portrait camera."
                    );

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, MarenPrefabPath);
                if (saved == null)
                    throw new InvalidOperationException(
                        $"Could not save the Maren player prefab at {MarenPrefabPath}."
                    );
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(resourcesObject);
            }
        }

        private static void AddMarenToProceduralDungeon()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ProceduralDungeonSceneTool.ScenePath,
                OpenSceneMode.Single
            );
            foreach (
                ActionController existing in Object
                    .FindObjectsByType<ActionController>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None
                    )
                    .Where(candidate =>
                        string.Equals(candidate.name, "Maren", StringComparison.Ordinal)
                    )
                    .ToArray()
            )
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            Map map = Object.FindFirstObjectByType<Map>();
            MapSourceValidationResult validation =
                map != null
                    ? map.ValidateSource()
                    : throw new InvalidOperationException(
                        "The procedural dungeon scene has no Map component."
                    );
            if (!validation.IsValid || validation.JsonMap?.LevelDocument == null)
                throw new InvalidOperationException(
                    "The procedural dungeon scene does not expose a valid authored JSON floor."
                );

            DungeonLevelDocument document = validation.JsonMap.LevelDocument;
            DungeonCell[] cells = new[] { document.StartCell }
                .Concat(document.SafeCells)
                .Concat(
                    Enumerable
                        .Range(0, document.Height)
                        .SelectMany(z =>
                            Enumerable.Range(0, document.Width).Select(x => new DungeonCell(x, z))
                        )
                        .Where(cell => IsWalkable(document, cell))
                )
                .Distinct()
                .Take(3)
                .ToArray();
            if (cells.Length != 3)
                throw new InvalidOperationException(
                    "The procedural dungeon scene requires three authored party cells."
                );

            GameObject prefab = RequireAsset<GameObject>(MarenPrefabPath);
            GameObject maren = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            maren.name = "Maren";
            maren.transform.SetPositionAndRotation(
                new Vector3(cells[2].X, 0f, cells[2].Z),
                Quaternion.identity
            );
            maren.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ProceduralDungeonSceneTool.ScenePath, false))
                throw new InvalidOperationException(
                    $"Could not save {ProceduralDungeonSceneTool.ScenePath}."
                );
        }

        private static bool IsWalkable(DungeonLevelDocument document, DungeonCell cell)
        {
            char value = document.Rows[document.Height - 1 - cell.Z][cell.X];
            return value == '.' || value == 'D';
        }

        private static T RequireAsset<T>(string path)
            where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null
                ? asset
                : throw new InvalidOperationException($"Missing required asset: {path}");
        }

        private static T RequireComponent<T>(GameObject root)
            where T : Component
        {
            T component = root.GetComponent<T>();
            return component != null
                ? component
                : throw new InvalidOperationException(
                    $"The Maren player prefab is missing {typeof(T).Name}."
                );
        }
    }
}
