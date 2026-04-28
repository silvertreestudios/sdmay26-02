using Game.Creature;
using System.Collections.Generic;
using UnityEngine;
using Game.Strikes;
using GridPublic;

namespace Game.Creature
{

    [System.Serializable] public struct SkillValue { public string skillName; public int skillMod; }

    [System.Serializable] public struct WeaponBonus { public string category; public int bonus; }

    [System.Serializable] public struct ArmorBonus { public string category; public int bonus; }


    // Extension as a Unity MonoBehaviour
    public class CreatureComponent : MonoBehaviour 
    {

        // Basic stats
        [Header("Basic Stats")]
        [SerializeField] private string _name; // {get set}
        [SerializeField] private int _level;
        [SerializeField] private int _initiative;
        [SerializeField] private int _speed;
        // TODO fields for other movement type/speeds

        // Combat stats
        // [Header("Combat")]
        [SerializeField] private int _hp;
        [SerializeField] private int _maxHp;
        [SerializeField] private int _tempHp;
        [SerializeField] private int _ac;
        [SerializeField] private int _attackBonus;
        [SerializeField] private int _damageBonus;
        [SerializeField] private List<WeaponBonus> _weaponBonuses = new List<WeaponBonus>();
        [SerializeField] private List<ArmorBonus> _armorBonuses = new List<ArmorBonus>();
        [SerializeField] private List<DamageValue> _weaknesses = new List<DamageValue>();
        [SerializeField] private List<DamageValue> _resistances = new List<DamageValue>();

        // Ability modifiers
        [Header("Ability Modifiers")]
        [SerializeField] private int _strMod;
        [SerializeField] private int _dexMod;
        [SerializeField] private int _conMod;
        [SerializeField] private int _intMod;
        [SerializeField] private int _wisMod;
        [SerializeField] private int _chaMod;

        // Saves
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
        [SerializeField] private List<string> _reactions = new List<string>(); // reactions
        [SerializeField] private List<string> _passives = new List<string>(); // abilities that don't require an action

        // Conditions - commented out until used, uncomment getter/setter and serialized field if needed
        // [Header("Conditions")]
        // [SerializeField] private List<string> _conditions = new List<string>();

        [Header("Equipment")]
        [SerializeField] private EquipmentArmor _equippedArmor;
        [SerializeField] private EquipmentWeapon _equippedRightHand = null;
        [SerializeField] private EquipmentWeapon _equippedLeftHand = null;
        [SerializeField] private List<string> _equipment = new List<string>();
        [SerializeField] private List<string> _weaponsList = new List<string>(); // Temp to display _weapons in inspector
        [SerializeField] private List<EquipmentWeapon> _weapons = new List<EquipmentWeapon>();
        [SerializeField] private List<string> _armorList = new List<string>(); // Temp to display armor in inspector
        [SerializeField] private List<EquipmentArmor> _armor = new List<EquipmentArmor>(); // Temp to display armor in inspector

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
        public List<WeaponBonus> weaponBonuses { get => _weaponBonuses; set => _weaponBonuses = value ?? new List<WeaponBonus>(); }
        public List<ArmorBonus> armorBonuses { get => _armorBonuses; set => _armorBonuses = value ?? new List<ArmorBonus>(); }
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
        // public List<string> conditions { get => _conditions; set => _conditions = value ?? new List<string>(); }


        // TODO: properly implement
        public List<string> equipment { get => _equipment; set => _equipment = value ?? new List<string>(); }
        public EquipmentArmor equippedArmor { get => _equippedArmor; set => _equippedArmor = value; }
        public EquipmentWeapon equippedRightHand { get => _equippedRightHand; set => _equippedRightHand = value; }
        public EquipmentWeapon equippedLeftHand { get => _equippedLeftHand; set => _equippedLeftHand = value; }
        public List<string> weaponsList { get => _weaponsList; set => _weaponsList = value ?? new List<string>(); }
        public List<EquipmentWeapon> weapons { get => _weapons; set => _weapons = value ?? new List<EquipmentWeapon>(); }
        public List<string> armorList { get => _armorList; set => _armorList = value ?? new List<string>(); }
        public List<EquipmentArmor> armor { get => _armor; set => _armor = value ?? new List<EquipmentArmor>(); }


        // Other attributes
        public List<string> traits { get; set; } = new List<string>();
        public string size { get; set; }
        public List<string> languages { get; set; } = new List<string>();
        public List<string> senses { get; set; } = new List<string>();

        // expose serialized backing fields via properties used by code
        public List<SkillValue> skills { get => _skills; set => _skills = value ?? new List<SkillValue>(); }
        public string description { get => _description; set => _description = value; }


        void Awake()
        {
        }

        void Start()
        {
            // Initialization code here
            // Apply passive abilities
            foreach (var a in passives)
            {
                var ability = DefinedAbilities.TryGet(a);
                if (ability != null)
                    ability.Apply(this.gameObject);
                else
                    Debug.LogWarning($"Ability '{a}' not found for {name}");
            }

            // Add initial strike actions
            if(this.gameObject.GetComponent<ActionController>() != null){
                Unarmed.AddUnarmedStrike(this.gameObject);
                StrikeWeapon.WeaponStrikeAdderAutomatic(this.gameObject);
            }else{
                Debug.LogWarning($"No ActionController found on {name}, cannot add default strikes");
            }
        }

        void Update()
        {
            // Per-frame logic here
        }

        // helper: get skill mod by name (case-insensitive). If the skill is present in the serialized
        // skills list we return that value. Otherwise we return the associated ability modifier.
        public int GetSkillMod(string skillName) { return GetSkillMod(skillName, 0);}
        public int GetSkillMod(string skillName, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(skillName)) return defaultValue;
            string key = skillName.Trim();

            // Check explicit proficient skill entries first
            if (_skills != null)
            {
                for (int i = 0; i < _skills.Count; i++)
                {
                    if (string.Equals(_skills[i].skillName, key, System.StringComparison.OrdinalIgnoreCase)){
                        int mod = _skills[i].skillMod;
                        return mod;
                    }
                }
            }

            // Map skill names to ability modifiers, returns corresponding ability modifier.
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
                // Constitution - No Skills
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

        // Method for applying damage to a creature
        // accounts for certain steps of damaging calculation that are best managed by the defender
        public void TakeDamage(List<DamageValue> damageValues, D20Result attackRoll)
        {
            // Applies crit damage if needed
            DamageRoller.EvaluateCriticalDamage(attackRoll.degree, damageValues);
            // Applies weaknesses and resistances
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
            // apply HP loss
            _hp -= remaining;
            _hp = Mathf.Max(0, _hp);
            if (_hp == 0)
                Defeat();
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
            if (_hp == 0)
                Defeat();
        }

        //helper function to signal to CombatManager when a player is defeated, so they can be removed from the turn queue and combat
        //this function also clears the character's position from the grid memory and deactivates their game object
        private void Defeat()
        {
            var ac = gameObject.GetComponent<ActionController>();
            if (ac != null && CombatManagerInterface.GetInstance() != null)
                CombatManagerInterface.GetInstance().Remove(ac);

            GridAPI.GetInstance().DestroyToken(this.gameObject);
            OnDeath.Invoke(gameObject); // Trigger the death event
            CombatLog.GetInstance().Log("- " + this.gameObject.name + " was defeated!");
            
            gameObject.SetActive(false);
        }

        public void Heal(int healAmount)
        {
            _hp += healAmount;
            _hp = Mathf.Clamp(_hp, 0, _maxHp);
        }

        public void GainTempHp(int tempHpAmount, bool overrideExisting)
        {
            // TODO replace with UI prompt for player decision about which 
            if(_tempHp > 0)
            {
                // UI prompt here
            }
            if(tempHpAmount >_tempHp || overrideExisting){
                _tempHp += tempHpAmount;
            }
        }
        public void GainTempHp(int tempHpAmount)
        {
            GainTempHp(tempHpAmount, false);
        }

        public int GetInitiative()
        {
            // initiative is populated by perception by default, this should account for modifications to perception as well as initiative-specific bonuses
            return Mathf.Max(initiative, GetSkillMod("perception", 0));
        }


        // ? Instead of disallowing equipping, unequip the other weapon?
        public void EquipWeaponLeft(EquipmentWeapon weapon)
        {
            if (weapon == null) return;
            if (equippedRightHand != null && equippedRightHand.hands == 2)
            {
                Debug.Log($"Cannot equip {weapon.name} in left hand because right hand has a two-handed weapon");
                return;
            }
            _equippedLeftHand = weapon;
        }
        public void EquipWeaponRight(EquipmentWeapon weapon)
        {
            if (weapon == null) return;
            if (equippedLeftHand != null && equippedLeftHand.hands == 2)
            {
                Debug.Log($"Cannot equip {weapon.name} in right hand because left hand has a two-handed weapon");
                return;
            }
            _equippedRightHand = weapon;
        }
        public void UnequipWeaponLeft()
        {
            _equippedLeftHand = null;
        }
        public void UnequipWeaponRight()
        {
            _equippedRightHand = null;
        }

        // Helper: check if left hand has a valid equipped weapon
        public bool HasEquippedLeftWeapon()
        {
            return _equippedLeftHand != null && 
                   !string.IsNullOrWhiteSpace(_equippedLeftHand.name) && 
                   _equippedLeftHand.damage != null;
        }

        // Helper: check if right hand has a valid equipped weapon
        public bool HasEquippedRightWeapon()
        {
            return _equippedRightHand != null && 
                   !string.IsNullOrWhiteSpace(_equippedRightHand.name) && 
                   _equippedRightHand.damage != null;
        }

        public void EquipArmor(EquipmentArmor armor)
        {
            if (armor == null) return;
            _equippedArmor = armor;
        }

        public void UnequipArmor()
        {
            _equippedArmor = null;
        }

        public void CalculateAC()
        {
            // If armor is equipped
            if (_equippedArmor != null && !string.IsNullOrWhiteSpace(_equippedArmor.name))
            {
                // Add Dex modifier up to the armor's dex cap
                _ac = 10 + _equippedArmor.acBonus + Mathf.Min(dexMod, _equippedArmor.dexCap);
                int armorBonus = armorBonuses.Find(b => b.category == _equippedArmor.category).bonus; // Add armor bonuses based on equipped armor group
                _ac += armorBonus;
                // Debug.Log($"Calculated "+ name +" AC with armor: 10 + " + _equippedArmor.acBonus +" (armor bonus) + min(" + dexMod +" (dex mod) + " + _equippedArmor.dexCap +" (dex cap)) + " + armorBonus +" (armor proficiency bonus for " + _equippedArmor.category + " armor) = " + _ac);
                // Debug.Log($" _equippedArmor.group: {_equippedArmor.category}, armorBonuses: {string.Join(", ", armorBonuses.ConvertAll(b => $"{b.category}: {b.bonus}"))}");
            }else{
                // Unarmored AC calculation
                // TODO: modify to include natural armor or other bonuses
                _ac = 10 + dexMod + armorBonuses.Find(b => b.category == "unarmored").bonus; 
                //_ac += armorBonuses.Find(b => b.category == "unarmored").bonus; // Add unarmored bonus if applicable
            }
        }
    }
}