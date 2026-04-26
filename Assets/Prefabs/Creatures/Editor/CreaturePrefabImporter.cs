using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.Creature;

public static class CreaturePrefabImporter
{
    [MenuItem("Tools/Creatures/Import JSON to Prefabs")]
    public static void ImportCreaturesToPrefabs()
    {
        string dataRoot = Path.Combine(Application.dataPath, "Resources", "DataFiles");
        if (!Directory.Exists(dataRoot))
        {
            Debug.LogError($"CreaturePrefabImporter: DataFiles directory not found: {dataRoot}");
            return;
        }

        // Restrict import to the pathfinder-monster-core subfolder only
        string monsterCoreRoot = Path.Combine(dataRoot, "pathfinder-monster-core");
        string playerCoreRoot = Path.Combine(dataRoot, "playerCharacters");

        List<string> creatureJsonPaths = new() { monsterCoreRoot, playerCoreRoot };
        foreach (var path in creatureJsonPaths.ToList()) {
            if (!Directory.Exists(path))
            {
                Debug.LogWarning($"CreaturePrefabImporter: directory not found: {path}. No prefabs will be created from this path.");
                creatureJsonPaths.Remove(path);
            }
        }

        // Try to locate a template prefab named "EmptyCreature" anywhere in the project
        const string templateName = "EmptyCreature";
        GameObject templatePrefab = null;
        var templateGuids = AssetDatabase.FindAssets($"{templateName} t:Prefab");
        if (templateGuids != null && templateGuids.Length > 0)
        {
            var templatePath = AssetDatabase.GUIDToAssetPath(templateGuids[0]);
            templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            Debug.Log($"CreaturePrefabImporter: using template prefab at {templatePath}");
        }
        else
        {
            Debug.LogWarning($"CreaturePrefabImporter: template prefab '{templateName}' not found. Instances will be created without a template.");
        }

        string prefabFolder = "Assets/Prefabs/Creatures";
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(prefabFolder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Creatures");

        
        // Only enumerate JSON files under DataFiles/pathfinder-monster-core
        var jsonFiles = new List<string>();
        foreach (var root in creatureJsonPaths) {
            var rootFiles = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                                 .OrderBy(f => f).ToArray();
            jsonFiles.AddRange(rootFiles);
        }

        // Reflection setup: try to find SaveAsPrefabAssetAsVariant method if available
        MethodInfo saveVariantMethod = typeof(PrefabUtility).GetMethod("SaveAsPrefabAssetAsVariant", BindingFlags.Public | BindingFlags.Static);

        int created = 0;
        int skipped = 0;
        int updated = 0;
        foreach (var file in jsonFiles)
        {
            try
            {
                string resourceJsonPath = ToResourcesRelativePath(file);
                if (string.IsNullOrEmpty(resourceJsonPath))
                {
                    Debug.LogWarning($"CreaturePrefabImporter: could not convert file path to Resources-relative path: {file}");
                    continue;
                }

                // Use the template prefab if found; otherwise converter will create a plain GameObject
                GameObject go = CreatureJsonConverter.CreateFromFile(resourceJsonPath, templatePrefab);
                if (go == null)
                {
                    Debug.LogWarning($"CreaturePrefabImporter: converter returned null for {resourceJsonPath} (source: {file})");
                    continue;
                }

                string baseName = Path.GetFileNameWithoutExtension(file);
                string prefabPath = $"{prefabFolder}/{baseName}.prefab";
                var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (existingPrefab != null)
                {
                    int updateResult = UpdateCreatureComponentOnExistingPrefab(prefabPath, go);
                    if (updateResult == 1)
                    {
                        updated++;
                    }
                    else if (updateResult == 0)
                    {
                        skipped++;
                    }
                    else
                    {
                        Debug.LogWarning($"CreaturePrefabImporter: failed to update CreatureComponent for existing prefab at {prefabPath}");
                    }

                    Object.DestroyImmediate(go);
                    continue;
                }

                // If we have a template prefab and runtime supports creating a variant, call it via reflection.
                if (templatePrefab != null && saveVariantMethod != null)
                {
                    try
                    {
                        saveVariantMethod.Invoke(null, new object[] { go, prefabPath });
                    }
                    catch (TargetInvocationException tie)
                    {
                        Debug.LogWarning($"CreaturePrefabImporter: variant save failed (invocation): {tie.InnerException?.Message}. Falling back to standard prefab save.");
                        PrefabUtility.SaveAsPrefabAssetAndConnect(go, prefabPath, InteractionMode.UserAction);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"CreaturePrefabImporter: variant save failed: {ex.Message}. Falling back to standard prefab save.");
                        PrefabUtility.SaveAsPrefabAssetAndConnect(go, prefabPath, InteractionMode.UserAction);
                    }
                }
                else
                {
                    // No variant API available (or no template) � save as a normal prefab.
                    PrefabUtility.SaveAsPrefabAssetAndConnect(go, prefabPath, InteractionMode.UserAction);
                }

                Object.DestroyImmediate(go);
                created++;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"CreaturePrefabImporter: failed to import {file}: {ex.Message}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"CreaturePrefabImporter: processed {jsonFiles.Count} creature(s). Created: {created}, Updated: {updated}, Skipped: {skipped} in {prefabFolder}");
    }

    private static string ToResourcesRelativePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        string normalized = filePath.Replace('\\', '/');
        const string marker = "/Assets/Resources/";
        int markerIndex = normalized.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            return normalized.Substring(markerIndex + marker.Length);

        const string projectRelativePrefix = "Assets/Resources/";
        if (normalized.StartsWith(projectRelativePrefix, System.StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(projectRelativePrefix.Length);

        return null;
    }

    private static int UpdateCreatureComponentOnExistingPrefab(string prefabPath, GameObject sourceObject)
    {
        var sourceComponent = sourceObject.GetComponent<CreatureComponent>();
        if (sourceComponent == null)
        {
            Debug.LogWarning($"CreaturePrefabImporter: source object has no CreatureComponent for {prefabPath}");
            return -1;
        }

        GameObject prefabContents = null;
        try
        {
            prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
            var targetComponent = prefabContents.GetComponent<CreatureComponent>();
            if (targetComponent == null)
            {
                targetComponent = prefabContents.AddComponent<CreatureComponent>();
            }

            // Compare serialized data; only update if different
            if (AreCreatureComponentsIdentical(sourceComponent, targetComponent))
            {
                // Debug.Log($"CreaturePrefabImporter: CreatureComponent unchanged for {prefabPath}, skipping update");
                return 0;
            }

            // Copy only the component's serialized fields so existing prefab structure remains unchanged.
            EditorUtility.CopySerialized(sourceComponent, targetComponent);
            PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
            return 1;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"CreaturePrefabImporter: error updating prefab {prefabPath}: {ex.Message}");
            return -1;
        }
        finally
        {
            if (prefabContents != null)
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }
    }

    private static bool AreCreatureComponentsIdentical(CreatureComponent source, CreatureComponent target)
    {
        // Serialize both components and compare the JSON representations
        string sourceJson = JsonUtility.ToJson(source, false);
        string targetJson = JsonUtility.ToJson(target, false);
        return sourceJson == targetJson;
    }
}