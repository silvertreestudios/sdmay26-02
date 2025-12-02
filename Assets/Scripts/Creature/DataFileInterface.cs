using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Creature
{

    public class DataFileInterface : MonoBehaviour
    {
        //store gameObject reference
        [SerializeField] private GameObject prefab;

        void SpawnNew()
        {
            //spawn object
            GameObject g = new GameObject("NameInEditor");
            //Add Components
            Rigidbody rb = g.AddComponent<Rigidbody>();
            MeshFilter mf = g.AddComponent<MeshFilter>();
        }

        void SpawnFromPrefab()
        {
            // Define the prefab in the scene with scripts attach
            GameObject g = Instantiate(prefab);
        }

        public GameObject CreateCreatureFromJson(string jsonFilePath)
        {
            // Load JSON file
            string jsonContent = File.ReadAllText(jsonFilePath);
            CreatureDto dto = JsonUtility.FromJson<CreatureDto>(jsonContent);

            // Instantiate prefab if available, otherwise create new GameObject
            GameObject creatureGO = prefab != null
                ? Instantiate(prefab)
                : new GameObject(dto?.name ?? "Creature");

            // Get existing CreatureComponent if the prefab has one, otherwise add it.
            var creatureInfo = creatureGO.GetComponent<CreatureComponent>() ?? creatureGO.AddComponent<CreatureComponent>();

            // Assign basic values from DTO (use property access, not indexing)
            creatureInfo.name = dto?.name ?? "";
            creatureInfo.level = dto?.system?.details?.level?.value ?? 0;
            creatureInfo.hp = dto?.system?.attributes?.hp?.value ?? 1;
            creatureInfo.ac = dto?.system?.attributes?.ac?.value ?? 0;
            creatureInfo.speed = dto?.system?.attributes?.speed?.value ?? 0;
            // use perception.mod from sample JSON for initiative
            creatureInfo.initiative = dto?.system?.perception?.mod ?? 0;
            creatureInfo.attackBonus = dto?.items != null && dto.items.Length > 0 ? dto.items[0]?.system?.bonus?.value ?? 0 : 0;
            creatureInfo.damageBonus = 0; // Assign as needed

            // Ability modifiers
            creatureInfo.strMod = dto?.system?.abilities?.str?.mod ?? 0;
            creatureInfo.dexMod = dto?.system?.abilities?.dex?.mod ?? 0;
            creatureInfo.conMod = dto?.system?.abilities?.con?.mod ?? 0;
            creatureInfo.intMod = dto?.system?.abilities?.@int?.mod ?? 0;
            creatureInfo.wisMod = dto?.system?.abilities?.wis?.mod ?? 0;
            creatureInfo.chaMod = dto?.system?.abilities?.cha?.mod ?? 0;

            // Saves
            creatureInfo.fortitudeSave = dto?.system?.saves?.fortitude?.value ?? 0;
            creatureInfo.reflexSave = dto?.system?.saves?.reflex?.value ?? 0;
            creatureInfo.willSave = dto?.system?.saves?.will?.value ?? 0;

            // Weaknesses and Resistances (map DTO arrays to DamageValue)
            if (dto?.system?.weaknesses != null)
            {
                foreach (var w in dto.system.weaknesses)
                {
                    // DamageValue constructor: (string damageType, int damageAmount)
                    creatureInfo.weaknesses.Add(new DamageValue(w.type, w.value));
                }
            }

            if (dto?.system?.resistances != null)
            {
                foreach (var r in dto.system.resistances)
                {
                    creatureInfo.resistances.Add(new DamageValue(r.type, r.value));
                }
            }

            // Actions (items) — store item names
            if (dto?.items != null)
            {
                foreach (var it in dto.items)
                {
                    if (!string.IsNullOrEmpty(it?.name))
                        creatureInfo.actions.Add(it.name);
                }
            }

            // Equipment
            if (dto?.equipment != null)
            {
                foreach (var e in dto.equipment)
                {
                    if (!string.IsNullOrEmpty(e?.name))
                        creatureInfo.equipment.Add(e.name);
                }
            }

            return creatureGO;
        }

        public static GameObject GetCreature(string creatureName)
        {
            // Build data root path under project Assets
            string rootDirectory = Path.Combine(Application.dataPath, "DataFiles");
            if (!Directory.Exists(rootDirectory))
            {
                Debug.LogWarning($"Data files folder not found: {rootDirectory}");
                return null;
            }

            var files = Directory.GetFiles(rootDirectory, "*.json", SearchOption.AllDirectories);
            var match = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(creatureName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                Debug.LogWarning($"Creature JSON not found for name '{creatureName}' under {rootDirectory}");
                return null;
            }

            // Find a scene instance of DataFileInterface
            var instance = UnityEngine.Object.FindObjectOfType<DataFileInterface>();
            if (instance == null)
            {
                Debug.LogError("No DataFileInterface instance found in the scene. Add one to call GetCreature.");
                return null;
            }

            return instance.CreateCreatureFromJson(match);
        }
    }
}