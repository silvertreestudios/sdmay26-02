using System;
using Game.Creature;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Creature
{
    // Define a simple EquipmentWeapon class
    [System.Serializable]
    public class EquipmentWeapon
    {
        public string name;          // weapon name
        public string type;          // e.g., weapon, armor.  Remove??
        public string group;         // such as sword, axe, etc.
        public string category;      // e.g., simple, martial
        public int hands;            // number of hands required to use
        public Dice damage;          // damage dice, e.g., "1 6 Slashing"  
        public string description;   // text description

        public List<string> traits;  // list of traits
        public string materialType;
        public string materialGrade;
        public List<string> runes;   // list of runes, if any
        public double price;         // <int> <currency> --or-- decimal with 1.0=1gp
        public int range;            // for ranged weapons, 0 for melee
        public string ammo;          // type of ammo used, null for melee
        public double bulk;             // look up uses
        // public string publication { get; set; }   // source book reference, unnnecessary for in game use

        // blank constructor
        public EquipmentWeapon(){}
    }
}
