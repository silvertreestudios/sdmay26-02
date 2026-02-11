using Game.Creature;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Creature
{

    // TODO temp location 
    [System.Serializable]
    public struct Condition
    {
        public string name;   // name of the condition
        // public string source; // gameObject ID of GO that applied the condition
    }

    [System.Serializable]
    public struct SkillValue
    {
        public string skillName;
        public int skillMod;
    }

    // Extension as a Unity MonoBehaviour
    public class CreatureComponent : MonoBehaviour 
    {
        // TODO: derive equipment proficiencies?

        // Basic stats
        [Header("Basic Stats")]
        [SerializeField] private string _name; // {get set}
        [SerializeField] private int _level;
        [SerializeField] private int _initiative;
        [SerializeField] private int _speed;
        // TODO other movement type speeds

        // Combat stats
        // [Header("Combat")]
        [SerializeField] private int _hp;
        [SerializeField] private int _maxHp;
        [SerializeField] private int _tempHp;
        [SerializeField] private int _ac;
        [SerializeField] private int _attackBonus;
        [SerializeField] private int _damageBonus;
        [SerializeField] private List<DamageValue> _weaknesses = new List<DamageValue>();
        [SerializeField] private List<DamageValue> _resistances = new List<DamageValue>();
        // hash map or alternative get for modifications?
        // handle conditions/modifiers in CreatureComponent

        // Example for ability modifiers
        [Header("Ability Modifiers")]
        [SerializeField] private int _strMod;
        [SerializeField] private int _dexMod;
        [SerializeField] private int _conMod;
        [SerializeField] private int _intMod;
        [SerializeField] private int _wisMod;
        [SerializeField] private int _chaMod;

        // saves
        [Header("Saves")]
        [SerializeField] private int _fortitudeSave;
        [SerializeField] private int _reflexSave;
        [SerializeField] private int _willSave;
        [SerializeField] private int _allSaves;

        // Serialized storage for skills and description so Unity can persist them
        [Header("Skills & Description")]
        [SerializeField] private List<SkillValue> _skills = new List<SkillValue>();
        [SerializeField][TextArea] private string _description;

        // Serialized actions & equipment (previously auto-properties; won't persist)
        [Header("Actions & Abilities")]
        [SerializeField] private List<string> _actions = new List<string>(); // standard actions
        [SerializeField] private List<string> _reactions = new List<string>();
        [SerializeField] private List<string> _passives = new List<string>();

        [Header("Conditions")]
        [SerializeField] private List<string> _conditions = new List<string>();

        [Header("Equipment")]
        [SerializeField] private string _equippedArmor;
        [SerializeField] private EquipmentWeapon _equippedRightHand;
        [SerializeField] private EquipmentWeapon _equippedLeftHand;
        [SerializeField] private List<string> _equipment = new List<string>();
        [SerializeField] private List<string> _weaponsList = new List<string>();
        [SerializeField] private List<EquipmentWeapon> _weapons = new List<EquipmentWeapon>();


        // NOTES FOR IMPLEMENTING STRIKE VARIANTS
        // Dice damageDice = new Dice(damageRolls.damage);
        // damgeDice.damageType = damageRolls.damageType;
        // DamageValue flatDamage = new DamageValue(damageRolls.damageType, _strMod)
        // Actions.Add(new Strike(1, new List<Dice>() { damageDice }, new List<DamageValue>() { flatDamage }));
        //[SerializeField] private List<EntityAction> _Actions = new List<EntityAction>();
        //public new List<EntityAction> Actions { get => _Actions; set => _Actions = value ?? new List<EntityAction>(); }

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

        // TODO: properly implement
        public List<string> actions { get => _actions; set => _actions = value ?? new List<string>(); }
        public List<string> reactions { get => _reactions; set => _reactions = value ?? new List<string>(); }
        public List<string> passives { get => _passives; set => _passives = value ?? new List<string>(); }
        public List<string> conditions { get => _conditions; set => _conditions = value ?? new List<string>(); }

        // TODO: properly implement
        public List<string> equipment { get => _equipment; set => _equipment = value ?? new List<string>(); }
        public string equippedArmor { get => _equippedArmor; set => _equippedArmor = value; }
        public EquipmentWeapon equippedRightHand { get => _equippedRightHand; set => _equippedRightHand = value; }
        public EquipmentWeapon equippedLeftHand { get => _equippedLeftHand; set => _equippedLeftHand = value; }
        public List<string> weaponsList { get => _weaponsList; set => _weaponsList = value ?? new List<string>(); }
        public List<EquipmentWeapon> weapons { get => _weapons; set => _weapons = value ?? new List<EquipmentWeapon>(); }


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

        // helper: get skill mod by name (case-insensitive). If the skill is present in the serialized
        // skills list we return that value. Otherwise we return the associated ability modifier.
        public int GetSkillMod(string skillName, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(skillName)) return defaultValue;
            string key = skillName.Trim();

            // Check explicit skill entries first
            if (_skills != null)
            {
                for (int i = 0; i < _skills.Count; i++)
                {
                    if (string.Equals(_skills[i].skillName, key, System.StringComparison.OrdinalIgnoreCase))
                        return _skills[i].skillMod;
                }
            }

            // Map skill names to ability modifiers.
            switch (key.ToLowerInvariant())
            {
                // Strength
                case "athletics":
                    return strMod;
                // Dexterity
                case "acrobatics":
                case "stealth":
                case "thievery":
                case "sleight of hand":
                case "sleight":
                case "acro":
                    return dexMod;
                // Constitution N/A
                // Intelligence
                case "arcana":
                case "history":
                case "investigation":
                case "lore":
                case "engineering":
                case "society":
                    return intMod;
                // Wisdom
                case "perception":
                case "insight":
                case "survival":
                case "medicine":
                case "nature":
                    return wisMod;
                // Charisma
                case "deception":
                case "intimidation":
                case "performance":
                case "persuasion":
                case "diplomacy":
                    return chaMod;
                // Unknown skill -> return default
                default:
                    return defaultValue;
            }
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

        public void TakeDamage(uint amount)
        {
            // consume temp HP first
            _tempHp -= (int)amount;
            if (_tempHp < 0)
            {
                _hp += _tempHp;
                _tempHp = 0;
                _hp = Mathf.Max(0, _hp);
            }
            if(_hp == 0)
            {
                this.gameObject.SetActive(false);
            }
        }

        public void Heal(int healAmount)
        {
            _hp += healAmount;
            _hp = Mathf.Clamp(_hp, 0, _maxHp);
        }

        public void equipWeaponLeft(EquipmentWeapon weapon)
        {
            if (weapon == null) return;
            if (equippedRightHand != null && equippedRightHand.hands == 2)
            {
                Debug.Log($"Cannot equip {weapon.name} in left hand because right hand has a two-handed weapon");
                return;
            }
            _equippedLeftHand = weapon;
        }
        public void equipWeaponRight(EquipmentWeapon weapon)
        {
            if (weapon == null) return;
            if (equippedLeftHand != null && equippedLeftHand.hands == 2)
            {
                Debug.Log($"Cannot equip {weapon.name} in right hand because left hand has a two-handed weapon");
                return;
            }
            _equippedRightHand = weapon;
        }
        public void unequipWeaponLeft()
        {
            _equippedLeftHand = null;
        }
        public void unequipWeaponRight()
        {
            _equippedRightHand = null;
        }
        // equipArmor
        // unequipArmor
    }
}