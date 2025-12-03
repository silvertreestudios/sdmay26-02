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

    // Extension as a Unity MonoBehaviour
    public class CreatureComponent : MonoBehaviour, ICreature
    {
        // TODO: derive equipment proficiencies?
        // Basic stats
        [SerializeField] private string _name;
        [SerializeField] private int _level;
        [SerializeField] private int _initiative;
        [SerializeField] private int _speed;
        // TODO other movement type speeds
        // combat stats
        [SerializeField] private int _hp;
        [SerializeField] private int _maxHp;
        [SerializeField] private int _tempHp;
        [SerializeField] private int _ac;
        [SerializeField] private int _attackBonus;
        [SerializeField] private int _damageBonus;
        [SerializeField] private List<DamageValue> _weaknesses = new List<DamageValue>();
        [SerializeField] private List<DamageValue> _resistances = new List<DamageValue>();
        // Example for ability modifiers
        [SerializeField] private int _strMod;
        [SerializeField] private int _dexMod;
        [SerializeField] private int _conMod;
        [SerializeField] private int _intMod;
        [SerializeField] private int _wisMod;
        [SerializeField] private int _chaMod;
        // saves
        [SerializeField] private int _fortitudeSave;
        [SerializeField] private int _reflexSave;
        [SerializeField] private int _willSave;
        [SerializeField] private int _allSaves;

        // Serialized storage for skills and description so Unity can persist them
        [SerializeField] private List<SkillValue> _skills = new List<SkillValue>();
        [SerializeField] [TextArea] private string _description;

        // Serialized actions & equipment (previously auto-properties; won't persist)
        [SerializeField] private List<string> _actions = new List<string>();
        [SerializeField] private List<string> _equipment = new List<string>();

        // Public properties for interface
        public new string name { get => _name; set { _name = value; base.name = value; } }
        public int level { get => _level; set => _level = value; }
        public int initiative { get => _initiative; set => _initiative = value; }
        public int speed { get => _speed; set => _speed = value; }

        public int hp { get => _hp; set => _hp = value; }
        public int maxHp { get => _maxHp; set => _maxHp = value; }
        public int tempHp { get => _tempHp; set => _tempHp = value; }
        public int ac { get => _ac; set => _ac = value; }
        public int attackBonus { get => _attackBonus; set => _attackBonus = value; }
        public int damageBonus { get => _damageBonus; set => _damageBonus = value; }
        public List<DamageValue> weaknesses { get => _weaknesses; set => _weaknesses = value; }
        public List<DamageValue> resistances { get => _resistances; set => _resistances = value; }

        public int strMod { get => _strMod; set => _strMod = value; }
        public int dexMod { get => _dexMod; set => _dexMod = value; }
        public int conMod { get => _conMod; set => _conMod = value; }
        public int intMod { get => _intMod; set => _intMod = value; }
        public int wisMod { get => _wisMod; set => _wisMod = value; }
        public int chaMod { get => _chaMod; set => _chaMod = value; }

        public int fortitudeSave { get => _fortitudeSave; set => _fortitudeSave = value; }
        public int reflexSave { get => _reflexSave; set => _reflexSave = value; }
        public int willSave { get => _willSave; set => _willSave = value; }
        public int allSaves { get => _allSaves; set => _allSaves = value; }


        // serialized backing fields exposed via properties
        public List<string> actions { get => _actions; set => _actions = value ?? new List<string>(); }
        public List<string> equipment { get => _equipment; set => _equipment = value ?? new List<string>(); }

        // TODO: properly implement        
        public List<string> traits { get; set; } = new List<string>();
        public string size { get; set; }
        public List<string> languages { get; set; } = new List<string>();
        public List<string> senses { get; set; } = new List<string>();

        // expose serialized backing fields via properties used by code
        public List<SkillValue> skills { get => _skills; set => _skills = value ?? new List<SkillValue>(); }
        public string description { get => _description; set => _description = value; }


        void Start()
        {
            // Initialization code here
        }

        void Update()
        {
            // Per-frame logic here
        }

        // helper: get skill mod by name (case-insensitive)
        public int GetSkillMod(string skillName, int defaultValue = 0)
        {
            if (_skills == null) return defaultValue;
            for (int i = 0; i < _skills.Count; i++)
            {
                if (string.Equals(_skills[i].skillName, skillName, System.StringComparison.OrdinalIgnoreCase))
                    return _skills[i].skillMod;
            }
            return defaultValue;
        }

        public void TakeDamage(List<DamageValue> damageValues, D20Result attackRoll)
        {
            // TODO : call function to apply resistances, immunities, vulnerabilities against damageValues
            DamageRoller.EvaluateCriticalDamage(attackRoll.degree, damageValues);
            DamageRoller.ApplyWeaknessAndResistance(damageValues, _weaknesses, _resistances);
            int damage = DamageRoller.SumDamage(damageValues);

            // consume temp HP first
            int remaining = damage;
            if (_tempHp > 0)
            {
                int used = Mathf.Min(_tempHp, remaining);
                _tempHp -= used;
                remaining -= used;
            }

            _hp -= remaining;
            _hp = Mathf.Max(0, _hp);
        }

        public void HealDamage(int healAmount)
        {
            _hp += healAmount;
            _hp = Mathf.Clamp(_hp, 0, _maxHp);
        }
    }
}
