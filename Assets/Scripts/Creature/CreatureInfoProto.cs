using UnityEngine;
using System.Collections.Generic;
// using Game.Combat;

// ACTION CONTROLLER HERE
// actionController = new ActionController();
// check pre-release
// postpone, just going to be hardcoded for now

// Update the interface to use the specific enum type for Actions
namespace Game.Creature
{

    [System.Serializable]
    public struct SkillValue
    {
        public string skillName;
        public int skillMod;
    }

    public interface ICreature
    {
        // Basic properties
        string name { get; set; }
        int level { get; set; }
        int initiative { get; set; }
        int speed { get; set; }
        // Combat properties
        int hp { get; set; }
        int ac { get; set; }
        int attackBonus { get; set; } // Temporary
        int damageBonus { get; set; } // Temporary
        List<DamageValue> weaknesses { get; set; }
        List<DamageValue> resistances { get; set; }
        // Ability modifiers
        int strMod { get; set; }
        int dexMod { get; set; }
        int conMod { get; set; }
        int intMod { get; set; }
        int wisMod { get; set; }
        int chaMod { get; set; }
        // Saves
        int fortitudeSave { get; set; }
        int reflexSave { get; set; }
        int willSave { get; set; }

        // Skills & description (added to keep interface aligned with component)
        List<SkillValue> skills { get; set; }
        string description { get; set; }

        // TODO ActionController actionController;

        // Actions and Equipment, saved as string names only for the time being
        // List<CreatureAction> actions { get; set; }
        // List<Equipment> equipment { get; set; }
        // short term: comment out equipment, weapon values hardcoded to strike action
        // long term: equipment to separate script?
    }
}
