using System;
using Game.Creature;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Creature
{
    // Define a simple EquipmentArmor class
    public class EquipmentArmor
    {
        public string name { get; set; }          // armor name
        public string type { get; set; }          // e.g., weapon, armor.  Remove??
        public string category { get; set; }      // e.g., light, medium, heavy, shield, etc.
        public double price { get; set; }         // <int> <currency> --or-- decimal with 1.0=1gp
        public int acBonus { get; set; }          // armor class bonus provided by the armor
        public int dexCap { get; set; }           // maximum Dexterity modifier that can be applied to AC when wearing this armor
        public int checkPenalty { get; set; }      // penalty to certain checks (e.g., stealth) when wearing this armor
        public int speedPenalty { get; set; }      // penalty to movement speed when wearing this armor
        public int strengthRequirement { get; set; } // minimum Strength score required to wear this armor without penalty
        public string description { get; set; }   // text description
        public double bulk { get; set; }              // look up uses
        public string group { get; set; }         // 
        public List<string> armorTraits { get; set; }  // list of traits

        public EquipmentArmor(){}
    }
}