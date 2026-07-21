using System;
using System.Collections.Generic;
using Game.Creature;
using UnityEngine;

namespace Game.Creature
{
    // Define a simple EquipmentWeapon class
    [System.Serializable]
    public class EquipmentWeapon
    {
        internal string DungeonPersistenceInstanceId { get; private set; } = string.Empty;

        public string name; // weapon name
        public string type; // e.g., weapon, armor.  Remove??
        public string group; // such as sword, axe, etc.
        public string category; // e.g., simple, martial
        public int hands; // number of hands required to use
        public Dice damage; // damage dice, e.g., "1 6 Slashing"
        public string description; // text description

        public List<string> traits; // list of traits
        public string materialType;
        public string materialGrade;
        public List<string> runes; // list of runes, if any
        public double price; // <int> <currency> --or-- decimal with 1.0=1gp
        public int range; // for ranged weapons, 0 for melee
        public string reload; // action cost to reload, null or "-" for melee
        public string ammo; // type of ammo used, null for melee
        public double bulk; // look up uses

        // public string publication { get; set; }   // source book reference, unnnecessary for in game use

        // blank constructor
        public EquipmentWeapon() { }

        internal void EnsureDungeonPersistenceIdentity(string instanceId)
        {
            string normalized = instanceId?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException(
                    "A weapon persistence identity is required.",
                    nameof(instanceId)
                );
            if (
                DungeonPersistenceInstanceId.Length > 0
                && DungeonPersistenceInstanceId != normalized
            )
                throw new InvalidOperationException(
                    "A weapon persistence identity cannot be replaced."
                );
            DungeonPersistenceInstanceId = normalized;
        }
    }
}
