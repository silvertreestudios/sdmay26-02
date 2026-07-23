using System;
using System.Collections.Generic;
using Game.Creature;
using UnityEngine;

namespace Game.Creature
{
    // Define a simple EquipmentArmor class
    [System.Serializable]
    public class EquipmentArmor
    {
        public string name; // armor name
        public string type; // e.g., weapon, armor.  Remove??
        public string category; // e.g., light, medium, heavy, shield, etc.
        public double price; // <int> <currency> --or-- decimal with 1.0=1gp
        public int acBonus; // armor class bonus provided by the armor
        public int dexCap; // maximum Dexterity modifier that can be applied to AC when wearing this armor
        public int checkPenalty; // penalty to certain checks (e.g., stealth) when wearing this armor
        public int speedPenalty; // penalty to movement speed when wearing this armor
        public int strengthRequirement; // minimum Strength score required to wear this armor without penalty
        public string description; // text description
        public double bulk; // look up uses
        public string group; //
        public List<string> armorTraits; // list of traits

        public EquipmentArmor() { }
    }
}
