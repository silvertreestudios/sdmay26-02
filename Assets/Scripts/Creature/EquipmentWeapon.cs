using System;
using Game.Creature;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Creature
{
    // Define a simple EquipmentWeapon class
    public class EquipmentWeapon
    {
        public string name { get; set; }          // weapon name
        public string type { get; set; }          // e.g., weapon, armor.  Remove??
        public string group { get; set; }         // such as sword, axe, etc.
        public string category { get; set; }      // e.g., simple, martial
        public int hands { get; set; }            // number of hands required to use
        public Dice damage { get; set; }          // damage dice, e.g., "1 6 Slashing"  
        public string description { get; set; }   // text description

        public List<string> traits { get; set; }  // list of traits
        public string materialType { get; set; }
        public string materialGrade { get; set; }
        public List<string> runes { get; set; }   // list of runes, if any
        public double price { get; set; }         // <int> <currency> --or-- decimal with 1.0=1gp
        public int range { get; set; }            // for ranged weapons, 0 for melee
        public string ammo { get; set; }          // type of ammo used, null for melee
        public double bulk { get; set; }             // look up uses
        // public string publication { get; set; }   // source book reference, unnnecessary for in game use

        // blank constructor
        public EquipmentWeapon(){}
    }
}
