using Game.Creature;
using System.Collections.Generic;
using UnityEngine;
using Game.Strikes;
using System;

namespace Game.Creature
{
    //public class TeamRules : SingletonMonoBehaviour<TeamRules>

    // Extension as a Unity MonoBehaviour
    [System.Serializable]
    public class Armory : SingletonMonoBehaviour<Armory>
    {
        [SerializeField] List<EquipmentWeapon> weapons;
        [SerializeField] List<EquipmentArmor> armors;

        [SerializeField] int weaponsCount;
        [SerializeField] int armorsCount;
        [SerializeField] List<string> weaponNames;
        [SerializeField] List<string> armorNames;


        protected override void Awake()
        {
            base.Awake();
            // Debug.Log("Armory weapon count: " +weapons.Count);
            // Debug.Log("Armory armor count: " +armors.Count);
        }

        // Get EquipmentWeapon from Armory by name
        public EquipmentWeapon GetWeapon(string weaponName){
            EquipmentWeapon weapon = null;
            // Debug.Log("Armory GetWeapon weapon count: " +weapons.Count);
            foreach(EquipmentWeapon w in weapons){
                // Debug.Log("Checking weapon: " + w.name);
                // Debug.Log("             vs: " +weaponName);
                if(string.Equals(w.name, weaponName, StringComparison.OrdinalIgnoreCase)){
                    weapon = w;
                    break;
                }
            }
             if(weapon == null){
                Debug.LogWarning($"Weapon {weaponName} not found in armory");
            }
            return weapon; 
        }

        // Get EquipmentArmor from Armory by name
        public EquipmentArmor GetArmor(string armorName){
            EquipmentArmor armor =null;
            // Debug.Log("Armory armor count: " +armors.Count);
            foreach(EquipmentArmor a in armors){
                // Debug.Log("Checking armor: " + a.name);
                // Debug.Log("            vs: " +armorName);
                if(string.Equals(a.name, armorName, StringComparison.OrdinalIgnoreCase)){
                    armor = a;
                    break;
                }
            }
             if(armor == null){
                Debug.LogWarning($"Armor {armorName} not found in armory");
            }
            return armor; 
        }

        // Populate set of EquipmentWeapon
        public void AddWeapons(List<EquipmentWeapon> newWeapons){
            weapons = new List<EquipmentWeapon>();
            if(weaponNames == null)
                weaponNames = new List<string>();
            String log = "Adding weapons to armory: ";
            foreach(EquipmentWeapon w in newWeapons){
                weapons.Add(w);
                log+= w.name + ", ";
            }
            Debug.Log(log);
            weaponsCount = weapons.Count;
            foreach(EquipmentWeapon w in newWeapons){
                weaponNames.Add(w.name);
            }
        }

        // Populate set of EquipmentArmor
        public void AddArmors(List<EquipmentArmor> newArmors){
            armors = new List<EquipmentArmor>();
            if(armorNames == null)
                armorNames = new List<string>();
            armors.AddRange(newArmors);
            armorsCount = armors.Count;
            foreach(EquipmentArmor a in newArmors){
                armorNames.Add(a.name);
            }
        }
    }
}