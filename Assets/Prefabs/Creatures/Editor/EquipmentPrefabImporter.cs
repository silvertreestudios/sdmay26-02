// using System.IO;
// using System.Linq;
// using System.Reflection;
// using System.Collections.Generic;
// using UnityEditor;
// using UnityEngine;
// using Game.Creature;

// public static class EquipmentPrefabImporter
// {
//     // Commented out until a need for the armory class arises, as it is currently unused and may not be needed in the future.
//     // [MenuItem("Tools/Equipment/Import JSON to Prefab Armory")]
//     public static void ImportEquipmentToPrefabs()
//     {
//         GameObject templatePrefab = new GameObject("Armory");

//         templatePrefab.AddComponent<Armory>();
//         templatePrefab.GetComponent<Armory>().AddWeapons(CreatureJsonConverter.GetAllWeapons());
//         templatePrefab.GetComponent<Armory>().AddArmors(CreatureJsonConverter.GetAllArmors());

//         string prefabFolder = "Assets/Prefabs/Equipment";
//         if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
//             AssetDatabase.CreateFolder("Assets", "Prefabs");
//         if (!AssetDatabase.IsValidFolder(prefabFolder))
//             AssetDatabase.CreateFolder("Assets/Prefabs", "Equipment");
//         string prefabPath = Path.Combine(prefabFolder, "Armory.prefab");
//         if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
//         {
//             Debug.LogWarning($"EquipmentPrefabImporter: prefab already exists at {prefabPath}, it will be overwritten.");
//         }
//         PrefabUtility.SaveAsPrefabAsset(templatePrefab, prefabPath);
//         Debug.Log($"EquipmentPrefabImporter: Armory prefab created at {prefabPath}");   

        
//     }
// }