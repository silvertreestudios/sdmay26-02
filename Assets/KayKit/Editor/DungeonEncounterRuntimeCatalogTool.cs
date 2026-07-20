using System;
using System.Collections.Generic;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using UnityEditor;
using UnityEngine;

namespace Game.KayKit.Editor
{
    /// <summary>Builds the runtime encounter catalog from the project-owned enemy manifest.</summary>
    public static class DungeonEncounterRuntimeCatalogTool
    {
        /// <summary>The generated catalog asset loaded by JSON dungeon runtime composition.</summary>
        public const string CatalogAssetPath =
            "Assets/Resources/DataFiles/dungeon/DungeonEncounterCreatureCatalog.asset";

        /// <summary>The strict project-owned enemy manifest used by planning and runtime catalogs.</summary>
        public const string EncounterManifestPath =
            "Assets/Resources/DataFiles/dungeon/encounter-enemies.json";

        /// <summary>Regenerates the runtime catalog from the strict encounter manifest.</summary>
        [MenuItem("Tools/KayKit/Regenerate Runtime Encounter Catalog")]
        public static void Regenerate()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(EncounterManifestPath);
            if (source == null)
                throw new InvalidOperationException(
                    $"Missing required asset: {EncounterManifestPath}"
                );

            IReadOnlyList<DungeonEncounterCandidate> candidates = DungeonEncounterCatalogJson.Parse(
                source.text
            );
            List<DungeonEncounterCreatureCatalogEntry> entries = new(candidates.Count);
            foreach (DungeonEncounterCandidate candidate in candidates)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(candidate.PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Encounter creature '{candidate.Id}' is missing prefab '{candidate.PrefabPath}'."
                    );
                }
                Team team = prefab.GetComponent<Team>();
                if (team == null)
                    throw new InvalidOperationException(
                        $"Encounter creature '{candidate.Id}' has no root Team component."
                    );
                if (
                    !string.Equals(
                        team.Name,
                        DungeonEncounterCreatureCatalog.HostileTeamName,
                        StringComparison.Ordinal
                    )
                )
                {
                    team.Name = DungeonEncounterCreatureCatalog.HostileTeamName;
                    EditorUtility.SetDirty(team);
                    PrefabUtility.SavePrefabAsset(prefab);
                }
                entries.Add(
                    new DungeonEncounterCreatureCatalogEntry(
                        candidate.Id,
                        candidate.ResourcePath,
                        prefab
                    )
                );
            }

            DungeonEncounterCreatureCatalog catalog =
                AssetDatabase.LoadAssetAtPath<DungeonEncounterCreatureCatalog>(CatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            }
            catalog.ReplaceEntries(entries);
            catalog.ValidateOrThrow();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"Generated runtime encounter catalog at {CatalogAssetPath}.");
        }

        /// <summary>Batchmode-safe alias for <see cref="Regenerate"/>.</summary>
        public static void RegenerateBatch()
        {
            Regenerate();
        }
    }
}
