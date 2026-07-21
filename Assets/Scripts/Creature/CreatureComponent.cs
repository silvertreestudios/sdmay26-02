using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Combat.Encounters;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.KayKit;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
using GridPublic;
using UnityEngine;

namespace Game.Creature
{
    [System.Serializable]
    public struct SkillValue
    {
        public string skillName;
        public int skillMod;
    }

    [System.Serializable]
    public struct WeaponBonus
    {
        public string category;
        public int bonus;
    }

    [System.Serializable]
    public struct ArmorBonus
    {
        public string category;
        public int bonus;
    }

    [System.Serializable]
    public struct WeaponActionBonus
    {
        public string weaponName;
        public int bonus;
    }

    [System.Serializable]
    public struct AmmoCount
    {
        public string ammoName;
        public int quantity;
    }

    [System.Serializable]
    public struct RuleSelectionValue
    {
        public string flag;
        public string selection;
    }

    [System.Serializable]
    public class CreatureAura
    {
        public string name;
        public string slug;
        public int radiusFeet;
        public List<string> traits = new List<string>();
    }

    /// <summary>
    /// Unity creature component that owns mutable gameplay state and exposes narrow state snapshots for PF2e rules.
    /// </summary>
    public class CreatureComponent : MonoBehaviour
    {
        public event Action EquipmentChanged;

        private bool defeated;

        // Basic stats
        [Header("Basic Stats")]
        [SerializeField]
        private string _name; // {get set}

        [SerializeField]
        private int _level;

        [SerializeField]
        private int _initiative;

        [SerializeField]
        private int _speed;

        // TODO fields for other movement type/speeds

        // Combat stats
        // [Header("Combat")]
        // Unity serializes these values into existing prefabs. They seed the authoritative
        // encounter state and then receive Fact-driven projections for Inspector visibility;
        // gameplay reads Health and writes only through dispatcher-backed operations.
        [SerializeField]
        private int _hp;

        [SerializeField]
        private int _maxHp;

        [SerializeField]
        private int _tempHp;
        private UnityEncounterRulesBridge encounterRules;
        private CreatureId healthCreatureId;

        [SerializeField]
        private int _ac;

        [SerializeField]
        private int _attackBonus;

        [SerializeField]
        private int _damageBonus;

        [SerializeField]
        private List<WeaponBonus> _weaponBonuses = new List<WeaponBonus>();

        [SerializeField]
        private List<WeaponActionBonus> _weaponActionBonuses = new List<WeaponActionBonus>();

        [SerializeField]
        private List<ArmorBonus> _armorBonuses = new List<ArmorBonus>();

        [SerializeField]
        private List<DamageValue> _weaknesses = new List<DamageValue>();

        [SerializeField]
        private List<DamageValue> _resistances = new List<DamageValue>();

        // Ability modifiers
        [Header("Ability Modifiers")]
        [SerializeField]
        private int _strMod;

        [SerializeField]
        private int _dexMod;

        [SerializeField]
        private int _conMod;

        [SerializeField]
        private int _intMod;

        [SerializeField]
        private int _wisMod;

        [SerializeField]
        private int _chaMod;

        // Saves
        [Header("Saves")]
        [SerializeField]
        private int _fortitudeSave;

        [SerializeField]
        private int _reflexSave;

        [SerializeField]
        private int _willSave;

        [SerializeField]
        private int _allSaves;

        // Serialized storage for skills and description so Unity can persist them
        [Header("Skills & Description")]
        [SerializeField]
        private List<SkillValue> _skills = new List<SkillValue>();

        [SerializeField]
        [TextArea]
        private string _description;

        // Serialized actions & equipment (previously auto-properties; won't persist)
        [Header("Actions & Abilities")]
        [SerializeField]
        private List<string> _actions = new List<string>(); // standard actions

        [SerializeField]
        private List<string> _reactions = new List<string>(); // reactions

        [SerializeField]
        private List<string> _passives = new List<string>(); // abilities that don't require an action

        [SerializeField]
        private List<CreatureAura> _auras = new List<CreatureAura>();

        // Conditions - commented out until used, uncomment getter/setter and serialized field if needed
        // [Header("Conditions")]
        // [SerializeField] private List<string> _conditions = new List<string>();

        [Header("Equipment")]
        [SerializeField]
        private EquipmentArmor _equippedArmor;

        [SerializeField]
        private EquipmentWeapon _equippedRightHand = null;

        [SerializeField]
        private EquipmentWeapon _equippedLeftHand = null;

        [SerializeField]
        private List<string> _equipment = new List<string>();

        [SerializeField]
        private List<string> _weaponsList = new List<string>(); // Temp to display _weapons in inspector

        [SerializeField]
        private List<EquipmentWeapon> _weapons = new List<EquipmentWeapon>();

        [SerializeField]
        private List<AmmoCount> _ammunition = new List<AmmoCount>();

        [SerializeField]
        private List<string> _unloadedWeapons = new List<string>();

        [SerializeField]
        private List<string> _armorList = new List<string>(); // Temp to display armor in inspector

        [SerializeField]
        private List<EquipmentArmor> _armor = new List<EquipmentArmor>(); // Temp to display armor in inspector

        [Header("PF2e Build")]
        [SerializeField]
        private string _buildClassName;

        [SerializeField]
        private string _buildSubclassName;

        [SerializeField]
        private string _buildClassFeatName;

        [SerializeField]
        private List<string> _buildTrainedSkills = new List<string>();

        [SerializeField]
        private List<RuleSelectionValue> _buildRuleSelections = new List<RuleSelectionValue>();
        private CharacterBuild _build;

        // Public properties for interface
        public bool IsDefeated => defeated;
        public new string name
        {
            get => _name;
            set
            {
                _name = value;
                base.name = value;
            }
        }
        public int level
        {
            get => _level;
            set => _level = value;
        }
        public int initiative
        {
            get => _initiative;
            set => _initiative = value;
        }
        public int speed
        {
            get => _speed;
            set => _speed = value;
        }

        /// <summary>
        /// Gets the complete health snapshot, reading <see cref="RulesState"/> after runtime
        /// ownership begins and serialized initialization fields before that boundary.
        /// </summary>
        public HealthState Health =>
            encounterRules == null
                ? new HealthState(_hp, _maxHp, _tempHp)
                : encounterRules.GetHealth(healthCreatureId);

        /// <summary>Gets authoritative current Hit Points.</summary>
        public int hp => Health.Current;

        /// <summary>Gets authoritative maximum Hit Points.</summary>
        public int maxHp => Health.Maximum;

        /// <summary>Gets authoritative temporary Hit Points.</summary>
        public int tempHp => Health.Temporary;
        public int ac
        {
            get => _ac;
            set => _ac = value;
        }
        public int attackBonus
        {
            get => _attackBonus;
            set => _attackBonus = value;
        }
        public int damageBonus
        {
            get => _damageBonus;
            set => _damageBonus = value;
        }
        public List<WeaponBonus> weaponBonuses
        {
            get => _weaponBonuses;
            set => _weaponBonuses = value ?? new List<WeaponBonus>();
        }
        public List<WeaponActionBonus> weaponActionBonuses
        {
            get => _weaponActionBonuses;
            set => _weaponActionBonuses = value ?? new List<WeaponActionBonus>();
        }
        public List<ArmorBonus> armorBonuses
        {
            get => _armorBonuses;
            set => _armorBonuses = value ?? new List<ArmorBonus>();
        }
        public List<DamageValue> weaknesses
        {
            get => _weaknesses;
            set => _weaknesses = value;
        }
        public List<DamageValue> resistances
        {
            get => _resistances;
            set => _resistances = value;
        }

        public int strMod
        {
            get => _strMod;
            set => _strMod = value;
        }
        public int dexMod
        {
            get => _dexMod;
            set => _dexMod = value;
        }
        public int conMod
        {
            get => _conMod;
            set => _conMod = value;
        }
        public int intMod
        {
            get => _intMod;
            set => _intMod = value;
        }
        public int wisMod
        {
            get => _wisMod;
            set => _wisMod = value;
        }
        public int chaMod
        {
            get => _chaMod;
            set => _chaMod = value;
        }

        public int fortitudeSave
        {
            get => _fortitudeSave;
            set => _fortitudeSave = value;
        }
        public int reflexSave
        {
            get => _reflexSave;
            set => _reflexSave = value;
        }
        public int willSave
        {
            get => _willSave;
            set => _willSave = value;
        }
        public int allSaves
        {
            get => _allSaves;
            set => _allSaves = value;
        }

        // TODO: properly implement
        public List<string> actions
        {
            get => _actions;
            set => _actions = value ?? new List<string>();
        }
        public List<string> reactions
        {
            get => _reactions;
            set => _reactions = value ?? new List<string>();
        }
        public List<string> passives
        {
            get => _passives;
            set => _passives = value ?? new List<string>();
        }

        // public List<string> conditions { get => _conditions; set => _conditions = value ?? new List<string>(); }

        // TODO: properly implement
        public List<string> equipment
        {
            get => _equipment;
            set => _equipment = value ?? new List<string>();
        }
        public EquipmentArmor equippedArmor
        {
            get => _equippedArmor;
            set
            {
                _equippedArmor = value;
                NotifyEquipmentChanged();
            }
        }
        public EquipmentWeapon equippedRightHand
        {
            get => _equippedRightHand;
            set
            {
                _equippedRightHand = value;
                NotifyEquipmentChanged();
            }
        }
        public EquipmentWeapon equippedLeftHand
        {
            get => _equippedLeftHand;
            set
            {
                _equippedLeftHand = value;
                NotifyEquipmentChanged();
            }
        }
        public List<string> weaponsList
        {
            get => _weaponsList;
            set => _weaponsList = value ?? new List<string>();
        }
        public List<EquipmentWeapon> weapons
        {
            get => _weapons;
            set => _weapons = value ?? new List<EquipmentWeapon>();
        }
        public List<AmmoCount> ammunition
        {
            get => _ammunition;
            set => _ammunition = value ?? new List<AmmoCount>();
        }
        public List<string> unloadedWeapons
        {
            get => _unloadedWeapons;
            set => _unloadedWeapons = value ?? new List<string>();
        }
        public List<string> armorList
        {
            get => _armorList;
            set => _armorList = value ?? new List<string>();
        }
        public List<EquipmentArmor> armor
        {
            get => _armor;
            set => _armor = value ?? new List<EquipmentArmor>();
        }

        public int GetAttackBonusForWeapon(EquipmentWeapon weapon)
        {
            if (weapon != null && _weaponActionBonuses != null)
            {
                string weaponKey = NormalizeEquipmentKey(weapon.name);
                foreach (WeaponActionBonus actionBonus in _weaponActionBonuses)
                {
                    if (NormalizeEquipmentKey(actionBonus.weaponName) == weaponKey)
                        return actionBonus.bonus;
                }
            }
            return attackBonus;
        }

        /// <summary>
        /// Resolves an attack roll modifier using the creature's base attack value plus providers and roll-specific modifiers.
        /// CreatureComponent coordinates the calculation but individual rule sources should contribute through IPf2eModifierProvider.
        /// </summary>
        /// <param name="baseAttackOverride">Optional imported weapon/action attack total to use instead of attackBonus.</param>
        /// <param name="additionalModifiers">One-roll modifiers from the immediate action context, such as MAP or range.</param>
        /// <returns>The resolved attack modifier and stacking details.</returns>
        public Pf2eModifierResolution ResolveAttackRoll(
            int? baseAttackOverride = null,
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            List<Pf2eModifier> modifiers = new()
            {
                new Pf2eModifier(
                    baseAttackOverride ?? attackBonus,
                    Pf2eModifierType.Untyped,
                    baseAttackOverride.HasValue ? "Attack modifier override" : "Attack bonus",
                    Pf2eStatistic.AttackRoll
                ),
            };
            AddProvidedModifiers(modifiers, additionalModifiers, Pf2eStatistic.AttackRoll);
            return Pf2eModifierResolver.Resolve(modifiers, Pf2eStatistic.AttackRoll);
        }

        /// <summary>
        /// Resolves an attack roll for a specific weapon, preserving imported weapon action bonuses as the base attack total.
        /// </summary>
        /// <param name="weapon">The weapon whose imported attack bonus should be used when available.</param>
        /// <param name="additionalModifiers">One-roll modifiers from the immediate action context.</param>
        /// <returns>The resolved attack modifier and stacking details.</returns>
        public Pf2eModifierResolution ResolveAttackRollForWeapon(
            EquipmentWeapon weapon,
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            return ResolveAttackRoll(GetAttackBonusForWeapon(weapon), additionalModifiers);
        }

        /// <summary>
        /// Resolves Armor Class from base creature or equipped armor data plus providers and context modifiers.
        /// Armor item bonuses are modeled as item modifiers so they stack according to PF2e rules.
        /// </summary>
        /// <param name="additionalModifiers">One-roll modifiers from the immediate context, such as cover.</param>
        /// <returns>The resolved AC value and stacking details.</returns>
        public Pf2eModifierResolution ResolveArmorClass(
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            List<Pf2eModifier> modifiers = BuildBaseArmorClassModifiers();
            AddProvidedModifiers(modifiers, additionalModifiers, Pf2eStatistic.ArmorClass);
            return Pf2eModifierResolver.Resolve(modifiers, Pf2eStatistic.ArmorClass);
        }

        /// <summary>
        /// Resolves the creature's Fortitude save with all-save bonuses, providers, and context modifiers.
        /// </summary>
        /// <param name="additionalModifiers">One-roll or effect-specific modifiers for this save.</param>
        /// <returns>The resolved Fortitude modifier and stacking details.</returns>
        public Pf2eModifierResolution ResolveFortitudeSave(
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            return ResolveSave(
                fortitudeSave,
                "Fortitude save",
                Pf2eStatistic.FortitudeSave,
                additionalModifiers
            );
        }

        /// <summary>
        /// Resolves the creature's Reflex save with all-save bonuses, providers, and context modifiers.
        /// </summary>
        /// <param name="additionalModifiers">One-roll or effect-specific modifiers for this save.</param>
        /// <returns>The resolved Reflex modifier and stacking details.</returns>
        public Pf2eModifierResolution ResolveReflexSave(
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            return ResolveSave(
                reflexSave,
                "Reflex save",
                Pf2eStatistic.ReflexSave,
                additionalModifiers
            );
        }

        /// <summary>
        /// Resolves the creature's Will save with all-save bonuses, providers, and context modifiers.
        /// </summary>
        /// <param name="additionalModifiers">One-roll or effect-specific modifiers for this save.</param>
        /// <returns>The resolved Will modifier and stacking details.</returns>
        public Pf2eModifierResolution ResolveWillSave(
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            return ResolveSave(willSave, "Will save", Pf2eStatistic.WillSave, additionalModifiers);
        }

        /// <summary>
        /// Resolves a skill check from imported skill data or its fallback ability modifier plus providers and context modifiers.
        /// </summary>
        /// <param name="skillName">The skill name to resolve.</param>
        /// <param name="additionalModifiers">One-roll or effect-specific modifiers for this skill check.</param>
        /// <returns>The resolved skill modifier and stacking details.</returns>
        public Pf2eModifierResolution ResolveSkillCheck(
            string skillName,
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            List<Pf2eModifier> modifiers = new()
            {
                new Pf2eModifier(
                    GetBaseSkillMod(skillName, 0),
                    Pf2eModifierType.Untyped,
                    string.IsNullOrWhiteSpace(skillName)
                        ? "Skill modifier"
                        : skillName.Trim() + " skill",
                    Pf2eStatistic.SkillCheck
                ),
            };
            AddProvidedModifiers(modifiers, additionalModifiers, Pf2eStatistic.SkillCheck);
            return Pf2eModifierResolver.Resolve(modifiers, Pf2eStatistic.SkillCheck);
        }

        /// <summary>
        /// Resolves initiative using the better of imported initiative or Perception, then applies providers and context modifiers.
        /// </summary>
        /// <param name="additionalModifiers">Encounter-start or effect-specific modifiers for initiative.</param>
        /// <returns>The resolved initiative modifier and stacking details.</returns>
        public Pf2eModifierResolution ResolveInitiative(
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            int baseInitiative = Mathf.Max(initiative, GetBaseSkillMod("perception", 0));
            List<Pf2eModifier> modifiers = new()
            {
                new Pf2eModifier(
                    baseInitiative,
                    Pf2eModifierType.Untyped,
                    "Initiative",
                    Pf2eStatistic.Initiative
                ),
            };
            AddProvidedModifiers(modifiers, additionalModifiers, Pf2eStatistic.Initiative);
            return Pf2eModifierResolver.Resolve(modifiers, Pf2eStatistic.Initiative);
        }

        /// <summary>
        /// Resolves a DC from its caller-supplied base value plus providers and context modifiers.
        /// </summary>
        /// <param name="baseDc">The unmodified DC supplied by the action, spell, or other rule source.</param>
        /// <param name="additionalModifiers">Effect-specific modifiers for this DC.</param>
        /// <returns>The resolved DC and stacking details.</returns>
        public Pf2eModifierResolution ResolveDifficultyClass(
            int baseDc,
            IEnumerable<Pf2eModifier> additionalModifiers = null
        )
        {
            List<Pf2eModifier> modifiers = new()
            {
                new Pf2eModifier(
                    baseDc,
                    Pf2eModifierType.Untyped,
                    "Base DC",
                    Pf2eStatistic.DifficultyClass
                ),
            };
            AddProvidedModifiers(modifiers, additionalModifiers, Pf2eStatistic.DifficultyClass);
            return Pf2eModifierResolver.Resolve(modifiers, Pf2eStatistic.DifficultyClass);
        }

        // Fortitude, Reflex, and Will differ only by base value and target statistic.
        private Pf2eModifierResolution ResolveSave(
            int baseSave,
            string source,
            Pf2eStatistic statistic,
            IEnumerable<Pf2eModifier> additionalModifiers
        )
        {
            List<Pf2eModifier> modifiers = new()
            {
                new Pf2eModifier(baseSave, Pf2eModifierType.Untyped, source, statistic),
                new Pf2eModifier(allSaves, Pf2eModifierType.Untyped, "All saves", statistic),
            };
            AddProvidedModifiers(modifiers, additionalModifiers, statistic);
            return Pf2eModifierResolver.Resolve(modifiers, statistic);
        }

        private void AddProvidedModifiers(
            List<Pf2eModifier> modifiers,
            IEnumerable<Pf2eModifier> additionalModifiers,
            Pf2eStatistic statistic
        )
        {
            if (additionalModifiers != null)
                modifiers.AddRange(additionalModifiers);

            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            {
                if (component is IPf2eModifierProvider provider)
                    modifiers.AddRange(provider.GetModifiers(statistic));
            }
        }

        private List<Pf2eModifier> BuildBaseArmorClassModifiers()
        {
            if (_equippedArmor != null && !string.IsNullOrWhiteSpace(_equippedArmor.name))
            {
                return new List<Pf2eModifier>
                {
                    new Pf2eModifier(
                        10,
                        Pf2eModifierType.Untyped,
                        "Base AC",
                        Pf2eStatistic.ArmorClass
                    ),
                    new Pf2eModifier(
                        Mathf.Min(dexMod, _equippedArmor.dexCap),
                        Pf2eModifierType.Untyped,
                        "Dexterity modifier",
                        Pf2eStatistic.ArmorClass
                    ),
                    new Pf2eModifier(
                        GetArmorProficiencyBonus(_equippedArmor.category),
                        Pf2eModifierType.Untyped,
                        _equippedArmor.category + " armor proficiency",
                        Pf2eStatistic.ArmorClass
                    ),
                    // Armor AC bonus is an item bonus. Source: https://2e.aonprd.com/Rules.aspx?ID=2166
                    new Pf2eModifier(
                        _equippedArmor.acBonus,
                        Pf2eModifierType.Item,
                        _equippedArmor.name,
                        Pf2eStatistic.ArmorClass
                    ),
                };
            }

            return new List<Pf2eModifier>
            {
                new Pf2eModifier(
                    ac,
                    Pf2eModifierType.Untyped,
                    "Armor Class",
                    Pf2eStatistic.ArmorClass
                ),
            };
        }

        private int GetArmorProficiencyBonus(string category)
        {
            if (armorBonuses == null || string.IsNullOrWhiteSpace(category))
                return 0;

            foreach (ArmorBonus armorBonus in armorBonuses)
            {
                if (
                    string.Equals(armorBonus.category, category, StringComparison.OrdinalIgnoreCase)
                )
                    return armorBonus.bonus;
            }
            return 0;
        }

        public int GetAmmoQuantity(string ammoName)
        {
            string key = NormalizeEquipmentKey(ammoName);
            for (int i = 0; i < _ammunition.Count; i++)
            {
                if (NormalizeEquipmentKey(_ammunition[i].ammoName) == key)
                    return _ammunition[i].quantity;
            }
            return 0;
        }

        public void SetAmmoQuantity(string ammoName, int quantity)
        {
            string key = NormalizeEquipmentKey(ammoName);
            for (int i = 0; i < _ammunition.Count; i++)
            {
                if (NormalizeEquipmentKey(_ammunition[i].ammoName) == key)
                {
                    _ammunition[i] = new AmmoCount
                    {
                        ammoName = ammoName,
                        quantity = Mathf.Max(0, quantity),
                    };
                    return;
                }
            }
            _ammunition.Add(
                new AmmoCount { ammoName = ammoName, quantity = Mathf.Max(0, quantity) }
            );
        }

        public bool HasAmmoFor(EquipmentWeapon weapon)
        {
            return weapon == null
                || string.IsNullOrWhiteSpace(weapon.ammo)
                || GetAmmoQuantity(weapon.ammo) > 0;
        }

        public bool ConsumeAmmoFor(EquipmentWeapon weapon)
        {
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.ammo))
                return true;

            int quantity = GetAmmoQuantity(weapon.ammo);
            if (quantity <= 0)
                return false;

            SetAmmoQuantity(weapon.ammo, quantity - 1);
            return true;
        }

        public bool IsWeaponLoaded(EquipmentWeapon weapon)
        {
            if (weapon == null || GetReloadCost(weapon) <= 0)
                return true;
            return !_unloadedWeapons.Contains(NormalizeEquipmentKey(weapon.name));
        }

        public void MarkWeaponFired(EquipmentWeapon weapon)
        {
            if (weapon == null || GetReloadCost(weapon) <= 0)
                return;

            string key = NormalizeEquipmentKey(weapon.name);
            if (!_unloadedWeapons.Contains(key))
                _unloadedWeapons.Add(key);
        }

        public bool ReloadWeapon(EquipmentWeapon weapon)
        {
            if (weapon == null || GetReloadCost(weapon) <= 0 || !HasAmmoFor(weapon))
                return false;

            _unloadedWeapons.Remove(NormalizeEquipmentKey(weapon.name));
            return true;
        }

        public int GetReloadCost(EquipmentWeapon weapon)
        {
            if (weapon == null)
                return 0;

            if (
                !string.IsNullOrWhiteSpace(weapon.reload)
                && int.TryParse(weapon.reload, out int cost)
            )
                return Mathf.Max(0, cost);

            if (weapon.traits != null)
            {
                foreach (string trait in weapon.traits)
                {
                    if (
                        trait != null
                        && trait.StartsWith("reload-", System.StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(trait.Substring(7), out cost)
                    )
                        return Mathf.Max(0, cost);
                }
            }

            return 0;
        }

        private static string NormalizeEquipmentKey(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant().Replace(' ', '-');
        }

        // Other attributes
        [SerializeField]
        private List<string> _traits = new List<string>();
        public List<string> traits
        {
            get => _traits;
            set => _traits = value ?? new List<string>();
        }
        public List<CreatureAura> auras
        {
            get => _auras;
            set => _auras = value ?? new List<CreatureAura>();
        }
        public string size { get; set; }
        public List<string> languages { get; set; } = new List<string>();
        public List<string> senses { get; set; } = new List<string>();
        public CharacterBuild Build
        {
            get
            {
                if (_build != null)
                    return _build;
                if (!HasSerializedBuild())
                    return null;

                _build = CreateBuildFromSerializedFields();
                return _build;
            }
            set
            {
                _build = value;
                StoreBuildInSerializedFields(value);
            }
        }
        public PreparedCharacter Prepared { get; set; }

        // expose serialized backing fields via properties used by code
        public List<SkillValue> skills
        {
            get => _skills;
            set => _skills = value ?? new List<SkillValue>();
        }
        public string description
        {
            get => _description;
            set => _description = value;
        }

        private bool runtimeActionsInitialized;

        void Awake() { }

        void Start()
        {
            InitializeRuntimeActions();
        }

        /// <summary>
        /// Prepares derived character state and adds default strikes and spells exactly once.
        /// </summary>
        /// <remarks>
        /// Runtime encounter materialization calls this before initiative can select a newly
        /// instantiated creature. Unity's later <c>Start</c> callback is therefore idempotent.
        /// </remarks>
        public void InitializeRuntimeActions()
        {
            if (runtimeActionsInitialized)
                return;

            if (Prepared == null && Build != null)
                Prepared = Pf2eCharacterPreparer.Prepare(this, Build);

            if (gameObject.GetComponent<ActionController>() == null)
            {
                Debug.LogWarning(
                    $"No ActionController found on {name}, cannot add default strikes"
                );
                return;
            }

            runtimeActionsInitialized = true;
            Unarmed.AddUnarmedStrike(gameObject);
            StrikeWeapon.WeaponStrikeAdderAutomatic(gameObject);
            CastSpellAction.AddSpellActions(gameObject);
        }

        void Update()
        {
            // Per-frame logic here
        }

        /// <summary>
        /// Resolves a skill modifier by name using the shared PF2e modifier pipeline.
        /// </summary>
        /// <param name="skillName">The skill name to resolve.</param>
        /// <returns>The resolved skill modifier, or 0 for blank skill names.</returns>
        public int GetSkillMod(string skillName)
        {
            return GetSkillMod(skillName, 0);
        }

        /// <summary>
        /// Resolves a skill modifier by name, returning a fallback value when no skill name is provided.
        /// </summary>
        /// <param name="skillName">The skill name to resolve.</param>
        /// <param name="defaultValue">The value returned when the skill name is blank.</param>
        /// <returns>The resolved skill modifier or the supplied fallback.</returns>
        public int GetSkillMod(string skillName, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                return defaultValue;
            return ResolveSkillCheck(skillName).Total;
        }

        private int GetBaseSkillMod(string skillName, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                return defaultValue;
            string key = skillName.Trim();

            // Check explicit proficient skill entries first
            if (_skills != null)
            {
                for (int i = 0; i < _skills.Count; i++)
                {
                    if (
                        string.Equals(
                            _skills[i].skillName,
                            key,
                            System.StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
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

        /// <summary>
        /// Supplies imported, fixture, or authoring health before encounter ownership begins.
        /// </summary>
        /// <remarks>
        /// <see cref="CreatureJsonConverter"/> calls this while importing creature data, and test
        /// builders use it before explicitly composing a bridge. Once a bridge owns this component,
        /// health is derived from RulesState and this method rejects later initialization.
        /// </remarks>
        /// <param name="current">Initial current Hit Points.</param>
        /// <param name="maximum">Initial maximum Hit Points.</param>
        /// <param name="temporary">Imported temporary Hit Points with no recoverable source.</param>
        public void InitializeHealthBeforeEncounter(int current, int maximum, int temporary = 0)
        {
            if (encounterRules != null)
                throw new InvalidOperationException(
                    "Health cannot be initialized after RulesState takes ownership."
                );
            HealthState validated = new HealthState(current, maximum, temporary);
            _hp = validated.Current;
            _maxHp = validated.Maximum;
            _tempHp = validated.Temporary;
        }

        /// <summary>
        /// Commits already-final damage and awaits the complete health and encounter causal root.
        /// </summary>
        /// <param name="amount">Damage remaining after all upstream damage calculations.</param>
        /// <param name="source">The existing rules source responsible for the damage.</param>
        /// <returns>The exact temporary- and current-HP amounts committed.</returns>
        public ValueTask<DamageOutcome> ApplyFinalDamageAsync(int amount, RuleSource source) =>
            RequireHealthRules().ApplyFinalDamageAsync(healthCreatureId, amount, source);

        /// <summary>Presents a committed zero-HP transition without changing the rules roster.</summary>
        /// <remarks>
        /// The authoritative encounter retains this creature's initiative entry as an effect-timing
        /// boundary. Presentation marks the component defeated, clears its occupied grid cell, and
        /// deactivates the GameObject only after the outer rules dispatch has fully settled.
        /// </remarks>
        internal void PresentCommittedDefeat()
        {
            if (defeated)
                return;
            defeated = true;

            var ac = gameObject.GetComponent<ActionController>();

            DungeonEncounterMember encounterMember =
                gameObject.GetComponent<DungeonEncounterMember>();
            if (encounterMember != null && encounterMember.IsConfigured)
                encounterMember.ReportDefeated();

            GridAPI grid = UnityEngine.Object.FindFirstObjectByType<GridAPI>();
            if (grid != null)
                grid.DestroyToken(this.gameObject);
            DisableGameplayInteraction(ac);
            OnDeath.Invoke(gameObject); // Trigger the death event
            CombatLog.GetInstance().Log("- " + this.gameObject.name + " was defeated!");

            CreaturePresentation presentation = GetComponent<CreaturePresentation>();
            bool deathStarted =
                presentation != null
                && presentation.PlayDeath(() =>
                {
                    if (this != null && gameObject != null)
                        gameObject.SetActive(false);
                });
            if (!deathStarted)
                gameObject.SetActive(false);
        }

        private void DisableGameplayInteraction(ActionController actionController)
        {
            if (actionController != null)
                actionController.enabled = false;
            foreach (Collider targetCollider in GetComponentsInChildren<Collider>(true))
                targetCollider.enabled = false;
        }

        /// <summary>
        /// Commits healing through the authoritative health dispatcher.
        /// </summary>
        /// <param name="healAmount">The non-negative healing requested.</param>
        /// <param name="source">The existing rules source responsible for the healing.</param>
        /// <returns>The amount committed after maximum-HP clamping.</returns>
        public ValueTask<HealingOutcome> HealAsync(int healAmount, RuleSource source) =>
            RequireHealthRules().ApplyHealingAsync(healthCreatureId, healAmount, source);

        /// <summary>
        /// Grants non-stacking temporary Hit Points owned by one rule source.
        /// </summary>
        /// <param name="source">The rule source that owns the resulting pool.</param>
        /// <param name="amount">The non-negative pool offered by the source.</param>
        /// <returns>Whether the offer replaced the current pool or was blocked.</returns>
        public ValueTask<TemporaryHitPointsGrantOutcome> GrantSourceTemporaryHitPointsAsync(
            RuleSource source,
            int amount
        ) => RequireHealthRules().GrantTemporaryHitPointsAsync(healthCreatureId, amount, source);

        /// <summary>Removes temporary Hit Points still owned by one rule source.</summary>
        /// <param name="source">The source whose remaining pool may be removed.</param>
        /// <returns>The amount removed, or zero when another source owns the pool.</returns>
        public ValueTask<TemporaryHitPointsRemovalOutcome> RemoveSourceTemporaryHitPointsAsync(
            RuleSource source
        ) => RequireHealthRules().RemoveTemporaryHitPointsAsync(healthCreatureId, source);

        /// <summary>
        /// Records that a source cannot grant temporary Hit Points again until game flow resets the immunity set.
        /// </summary>
        /// <param name="source">The source to block from later grants.</param>
        /// <returns>Whether a new immunity was committed.</returns>
        public ValueTask<TemporaryHitPointImmunityOutcome> AddTemporaryHitPointImmunityAsync(
            RuleSource source
        ) => RequireHealthRules().AddTemporaryHitPointImmunityAsync(healthCreatureId, source);

        /// <summary>
        /// Checks whether a source is currently blocked from granting temporary Hit Points.
        /// </summary>
        /// <param name="source">The source key to check.</param>
        /// <returns>True when the source has temporary Hit Point immunity.</returns>
        public bool HasTempHpImmunity(string source) =>
            !string.IsNullOrWhiteSpace(source)
            && Health.HasTemporaryHitPointImmunity(RuleSource.FromSlug(source));

        /// <summary>
        /// Returns a snapshot of temporary Hit Point immunity sources for Unity-free rule evaluation.
        /// </summary>
        /// <returns>The active source keys with temporary Hit Point immunity.</returns>
        public IReadOnlyCollection<string> GetTempHpImmunitySources()
        {
            return Health.TemporaryHitPointImmunities.Select(source => source.Slug).ToArray();
        }

        internal HealthState GetHealthInitializationState()
        {
            if (encounterRules != null)
                return encounterRules.GetHealth(healthCreatureId);
            return new HealthState(_hp, _maxHp, _tempHp);
        }

        internal UnityEncounterRulesBridge GetEncounterRulesBridge() => RequireHealthRules();

        // Standalone and preparation fixtures may legitimately have no health bridge. Callers that
        // only need to enforce active-encounter policy must be able to distinguish that case without
        // starting a mutation or using exception flow.
        internal bool TryGetEncounterRulesBridge(out UnityEncounterRulesBridge bridge)
        {
            bridge = encounterRules;
            return bridge != null;
        }

        internal void AttachEncounterRules(UnityEncounterRulesBridge bridge, CreatureId creatureId)
        {
            if (bridge == null)
                throw new ArgumentNullException(nameof(bridge));
            if (creatureId.IsEmpty)
                throw new ArgumentException(
                    "A health creature ID is required.",
                    nameof(creatureId)
                );
            encounterRules = bridge;
            healthCreatureId = creatureId;
            ProjectCommittedHealth(bridge.GetHealth(creatureId));
        }

        internal void ProjectCommittedHealth(HealthState health)
        {
            _hp = health.Current;
            _maxHp = health.Maximum;
            _tempHp = health.Temporary;
        }

        internal void PresentCommittedHit()
        {
            GetComponent<CreaturePresentation>()?.PlayHit();
        }

        private UnityEncounterRulesBridge RequireHealthRules()
        {
            if (encounterRules == null)
            {
                throw new InvalidOperationException(
                    "Health commands require an encounter health bridge. CombatManager.StartCombat or an explicit test composition must initialize it first."
                );
            }
            return encounterRules;
        }

        public int GetInitiative()
        {
            return ResolveInitiative().Total;
        }

        // ? Instead of disallowing equipping, unequip the other weapon?
        public void EquipWeaponLeft(EquipmentWeapon weapon)
        {
            if (weapon == null)
                return;
            if (equippedRightHand != null && equippedRightHand.hands == 2)
            {
                Debug.Log(
                    $"Cannot equip {weapon.name} in left hand because right hand has a two-handed weapon"
                );
                return;
            }
            equippedLeftHand = weapon;
        }

        public void EquipWeaponRight(EquipmentWeapon weapon)
        {
            if (weapon == null)
                return;
            if (equippedLeftHand != null && equippedLeftHand.hands == 2)
            {
                Debug.Log(
                    $"Cannot equip {weapon.name} in right hand because left hand has a two-handed weapon"
                );
                return;
            }
            equippedRightHand = weapon;
        }

        public void UnequipWeaponLeft()
        {
            equippedLeftHand = null;
        }

        public void UnequipWeaponRight()
        {
            equippedRightHand = null;
        }

        // Helper: check if left hand has a valid equipped weapon
        public bool HasEquippedLeftWeapon()
        {
            return _equippedLeftHand != null
                && !string.IsNullOrWhiteSpace(_equippedLeftHand.name)
                && _equippedLeftHand.damage != null;
        }

        // Helper: check if right hand has a valid equipped weapon
        public bool HasEquippedRightWeapon()
        {
            return _equippedRightHand != null
                && !string.IsNullOrWhiteSpace(_equippedRightHand.name)
                && _equippedRightHand.damage != null;
        }

        public void EquipArmor(EquipmentArmor armor)
        {
            if (armor == null)
                return;
            equippedArmor = armor;
        }

        public void UnequipArmor()
        {
            equippedArmor = null;
        }

        private void NotifyEquipmentChanged()
        {
            EquipmentChanged?.Invoke();
        }

        public void CalculateAC()
        {
            if (_equippedArmor == null || string.IsNullOrWhiteSpace(_equippedArmor.name))
            {
                _ac = 10 + dexMod + GetArmorProficiencyBonus("unarmored");
                return;
            }

            _ac = Pf2eModifierResolver
                .Resolve(BuildBaseArmorClassModifiers(), Pf2eStatistic.ArmorClass)
                .Total;
        }

        private bool HasSerializedBuild()
        {
            return !string.IsNullOrWhiteSpace(_buildClassName)
                || !string.IsNullOrWhiteSpace(_buildSubclassName)
                || !string.IsNullOrWhiteSpace(_buildClassFeatName)
                || (_buildTrainedSkills != null && _buildTrainedSkills.Count > 0)
                || (_buildRuleSelections != null && _buildRuleSelections.Count > 0);
        }

        private CharacterBuild CreateBuildFromSerializedFields()
        {
            CharacterBuild build = new CharacterBuild
            {
                ClassName = _buildClassName,
                SubclassName = _buildSubclassName,
                ClassFeatName = _buildClassFeatName,
            };

            foreach (string skill in _buildTrainedSkills ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(skill))
                    build.TrainedSkills.Add(skill);
            }

            foreach (
                RuleSelectionValue selection in _buildRuleSelections
                    ?? new List<RuleSelectionValue>()
            )
            {
                if (
                    !string.IsNullOrWhiteSpace(selection.flag)
                    && !string.IsNullOrWhiteSpace(selection.selection)
                )
                    build.RuleSelections[selection.flag] = selection.selection;
            }

            return build;
        }

        private void StoreBuildInSerializedFields(CharacterBuild build)
        {
            _buildClassName = build?.ClassName;
            _buildSubclassName = build?.SubclassName;
            _buildClassFeatName = build?.ClassFeatName;
            _buildTrainedSkills = new List<string>();
            _buildRuleSelections = new List<RuleSelectionValue>();

            if (build == null)
                return;

            foreach (string skill in build.TrainedSkills)
            {
                if (!string.IsNullOrWhiteSpace(skill))
                    _buildTrainedSkills.Add(skill);
            }

            foreach (KeyValuePair<string, string> selection in build.RuleSelections)
            {
                if (
                    !string.IsNullOrWhiteSpace(selection.Key)
                    && !string.IsNullOrWhiteSpace(selection.Value)
                )
                    _buildRuleSelections.Add(
                        new RuleSelectionValue { flag = selection.Key, selection = selection.Value }
                    );
            }
        }
    }
}
