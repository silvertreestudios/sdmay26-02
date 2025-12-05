using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Game.Creature;

public static class CreaturePrefabImporter
{
    [MenuItem("Tools/Creatures/Import JSON to Prefabs")]
    public static void ImportCreaturesToPrefabs()
    {
        string dataRoot = Path.Combine(Application.dataPath, "DataFiles");
        if (!Directory.Exists(dataRoot))
        {
            Debug.LogError($"CreaturePrefabImporter: DataFiles directory not found: {dataRoot}");
            return;
        }

        // Restrict import to the pathfinder-monster-core subfolder only
        string monsterCoreRoot = Path.Combine(dataRoot, "pathfinder-monster-core");
        if (!Directory.Exists(monsterCoreRoot))
        {
            Debug.LogWarning($"CreaturePrefabImporter: monster core directory not found: {monsterCoreRoot}. No prefabs will be created.");
            return;
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
        var jsonFiles = Directory.GetFiles(monsterCoreRoot, "*.json", SearchOption.AllDirectories)
                                 .OrderBy(f => f).ToArray();

        // Reflection setup: try to find SaveAsPrefabAssetAsVariant method if available
        MethodInfo saveVariantMethod = typeof(PrefabUtility).GetMethod("SaveAsPrefabAssetAsVariant", BindingFlags.Public | BindingFlags.Static);

        int success = 0;
        foreach (var file in jsonFiles)
        {
            try
            {
                // Use the template prefab if found; otherwise converter will create a plain GameObject
                GameObject go = CreatureJsonConverter.CreateFromFile(file, templatePrefab);
                if (go == null)
                {
                    Debug.LogWarning($"CreaturePrefabImporter: converter returned null for {file}");
                    continue;
                }

                string baseName = Path.GetFileNameWithoutExtension(file);
                string prefabPath = $"{prefabFolder}/{baseName}.prefab";

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
                    // No variant API available (or no template) — save as a normal prefab.
                    PrefabUtility.SaveAsPrefabAssetAndConnect(go, prefabPath, InteractionMode.UserAction);
                }

                Object.DestroyImmediate(go);
                success++;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"CreaturePrefabImporter: failed to import {file}: {ex.Message}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"CreaturePrefabImporter: imported {success}/{jsonFiles.Length} creature(s) to {prefabFolder}");
    }
}