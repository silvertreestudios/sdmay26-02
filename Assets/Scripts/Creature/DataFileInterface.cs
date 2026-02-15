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

        void SpawnFromPrefab()
        {
            // Define the prefab in the scene with scripts attach
            GameObject g = Instantiate(prefab);
        }

        // Create a creature GameObject from a JSON file path
        public GameObject CreateCreatureFromJson(string jsonFilePath)
        {
            // Delegate parsing + mapping to the central converter
            return CreatureJsonConverter.CreateFromFile(jsonFilePath, prefab);
        }

        // Called by CreatureLoader to get a creature by name
        public static GameObject GetCreature(string creatureName)
        {
            // Prefer using an instance's prefab if present
            var instance = UnityEngine.Object.FindObjectOfType<DataFileInterface>();
            GameObject prefab = instance != null ? instance.prefab : null;

            // Delegate lookup + creation to the converter (it will search Assets/DataFiles)
            return CreatureJsonConverter.CreateByName(creatureName, prefab);
        }

        // Get weapon data from data file by name
        public static EquipmentWeapon GetWeapon(string weaponName)
        {
            // Delegate lookup + creation to the converter (it will search Assets/DataFiles)
            return CreatureJsonConverter.GetWeaponByName(weaponName);
        }
    }
}