using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Game.Strikes;

namespace Game.Creature
{

    // --- DTOs (JsonUtility requires [Serializable] classes with public fields) ---
    [Serializable]
    public class CreatureDto
    {
        public string name;
        public string type;
        public SystemDto system;
        public WeaponBonusDto[] weaponBonuses; // added for weapon bonus bonuses
        public ArmorBonusDto[] armorBonuses; // added for armor bonus bonuses
        public ItemDto[] items;
        public ItemDto[] reactions;
        public ItemDto[] passives;
        public EquipmentDto[] equipment;
        public WeaponDto[] weapons;
        public ArmorDto[] armor;
        public ConditionsDto[] conditions;
        public string Source;
    }

    // Skill DTO for the array form produced by JsonImporter
    [Serializable]
    public class SkillDto
    {
        public string name;
        public int value; // matches "base" key in JSON; use @ to escape keyword
    }

    [Serializable] public class WeaponBonusDto{ public string category; public int bonus;}
    [Serializable] public class ArmorBonusDto{ public string category; public int bonus;}

    [Serializable] public class SystemDto
    {
        public AbilitySetDto abilities;
        public AttributesDto attributes;
        public DetailsDto details;
        public PerceptionDto perception;
        public SaveSetDto saves;
        public WeaknessDto[] weaknesses;
        public ResistanceDto[] resistances;
        public SkillDto[] skills; // now a typed array (importer produces this)
    }
    [Serializable]
    public class DetailsDto
    {
        public LevelDto level;
        // fields normalized by JsonImporter
        public string publicNotesPlain;
        public string[] publicNotesParagraphs;
    }
    [Serializable] public class LevelDto { public int value; }

    [Serializable] public class AttributesDto { public AcDto ac; public HpDto hp; public SpeedDto speed; }
    [Serializable] public class AcDto { public string details; public int value; }
    [Serializable] public class HpDto { public string details; public int max; public int temp; public int value; }
    [Serializable] public class SpeedDto { public string[] otherSpeeds; public int value; }

    [Serializable] public class PerceptionDto { public string details; public int mod; }
    [Serializable] public class AbilitySetDto { public AbilityDto str; public AbilityDto dex; public AbilityDto con; public AbilityDto @int; public AbilityDto wis; public AbilityDto cha; }
    [Serializable] public class AbilityDto { public int mod; }
    [Serializable] public class SaveSetDto { public SaveDto fortitude; public SaveDto reflex; public SaveDto will; }
    [Serializable] public class SaveDto { public int value; }
    [Serializable] public class WeaknessDto { public string type; public int value; }
    [Serializable] public class ResistanceDto { public string type; public int value; }

    // Item DTOs
    [Serializable] public class ItemDto { public string name; public string type; public ItemSystemDto system; }
    // TODO variants for reactions and passives as needed
    // [Serializable] public class ReactionDto { public string name; public string description; }
    // [Serializable] public class PassiveDto { public string name; public string description; }

    [Serializable]
    public class ItemSystemDto
    {
        public BonusDto bonus;
        public DamageRollsDto[] damageRolls;
        public string[] descriptionParagraphs;
        public RangeDto range;
        public TraitsDto traits;
    }

    [Serializable] public class BonusDto { public int value; }
    [Serializable] public class DamageRollsDto { public string damage; public string damageType; }

    [Serializable] public class RangeDto { public int increment; public int max; }

    [Serializable] public class TraitsDto { public string rarity; public string[] value; }

    [Serializable] public class EquipmentDto { public string name; public string type; public int quantity; }
    [Serializable] 
    public class WeaponDto { 
        public string name;
        public string type;
        public string group;
        public string category;
        public int hands;
        public int damageDice;
        public int damageDie;
        public string damageType;
        public string description;
        public List<string> traits;
        public string materialType;
        public string materialGrade;
        public List<string> runes;
        public string price;
        public int range;
        public string ammo;
        public int bulk;
    }

    [Serializable]
    public class ArmorDto {
        public string name;
        public string type;
        public string category;
        public double price;
        public int acBonus;
        public int dexCap;
        public int checkPenalty;
        public int speedPenalty;
        public int strengthRequirement;
        public string description;
        public double bulk;
        public string group;
        public List<string> armorTraits;
    }

    // Conditions DTOs TODO
    [Serializable] public class ConditionsDto { public string name; public string source; }


    // --- Mapping extension (apply DTO -> CreatureComponent) ---
    public static class CreatureDtoMapper
    {
        public static void ApplyFromDto(this CreatureComponent target, CreatureDto dto)
        {
            if (target == null || dto == null || dto.system == null) return;

            // Basic
            target.name = dto.name ?? target.name;
            target.level = dto.system.details?.level?.value ?? target.level;

            // Attributes
            target.hp = dto.system.attributes?.hp?.value ?? target.hp;
            target.maxHp = dto.system.attributes?.hp?.max ?? target.maxHp;
            target.tempHp = dto.system.attributes?.hp?.temp ?? target.tempHp;

            target.ac = dto.system.attributes?.ac?.value ?? target.ac;
            target.speed = dto.system.attributes?.speed?.value ?? target.speed;

            // Initiative: use perception.mod if present
            target.initiative = dto.system.perception?.mod ?? target.initiative;

            // Attack bonus from first item if present // Temporary
            target.attackBonus = dto.items != null && dto.items.Length > 0
                ? dto.items[0]?.system?.bonus?.value ?? target.attackBonus
                : target.attackBonus;

            // TODO: temporary, damage bonus directly from str mod 
            target.damageBonus = dto.system.abilities?.str?.mod ?? target.strMod;

            // Weapon attack bonuses by proficiency category
            if (target.weaponBonuses == null) target.weaponBonuses = new List<WeaponBonus>();
            target.weaponBonuses.Clear();
            if(dto.weaponBonuses != null)
            {
                foreach(var wb in dto.weaponBonuses)
                {
                    target.weaponBonuses.Add(new WeaponBonus { category = wb.category, bonus = wb.bonus });
                }
            }

            // Armor bonuses by proficiency category
            if (target.armorBonuses == null) target.armorBonuses = new List<ArmorBonus>();
            target.armorBonuses.Clear();
            if(dto.armorBonuses != null)
            {
                foreach(var ab in dto.armorBonuses)
                {
                    target.armorBonuses.Add(new ArmorBonus { category = ab.category, bonus = ab.bonus });
                }
            }

            // Ability modifiers
            target.strMod = dto.system.abilities?.str?.mod ?? target.strMod;
            target.dexMod = dto.system.abilities?.dex?.mod ?? target.dexMod;
            target.conMod = dto.system.abilities?.con?.mod ?? target.conMod;
            target.intMod = dto.system.abilities?.@int?.mod ?? target.intMod;
            target.wisMod = dto.system.abilities?.wis?.mod ?? target.wisMod;
            target.chaMod = dto.system.abilities?.cha?.mod ?? target.chaMod;

            // Saves
            target.fortitudeSave = dto.system.saves?.fortitude?.value ?? target.fortitudeSave;
            target.reflexSave = dto.system.saves?.reflex?.value ?? target.reflexSave;
            target.willSave = dto.system.saves?.will?.value ?? target.willSave;

            // Replace weaknesses/resistances lists
            if (target.weaknesses == null) target.weaknesses = new List<DamageValue>();
            target.weaknesses.Clear();
            if (dto.system.weaknesses != null)
            {
                foreach (var w in dto.system.weaknesses)
                    target.weaknesses.Add(new DamageValue(w.type, w.value));
            }

            if (target.resistances == null) target.resistances = new List<DamageValue>();
            target.resistances.Clear();
            if (dto.system.resistances != null)
            {
                foreach (var r in dto.system.resistances)
                    target.resistances.Add(new DamageValue(r.type, r.value));
            }

            // Actions - Standard (store item names)
            if (target.actions == null) target.actions = new List<string>();
            target.actions.Clear();
            if (dto.items != null)
            {
                foreach (var it in dto.items)
                    if (!string.IsNullOrEmpty(it?.name))
                        target.actions.Add(it.name);
            }
            // Actions - Reactions
            if(target.reactions == null) target.reactions = new List<string>();
            target.reactions.Clear();
            if (dto.reactions != null)
            {
                foreach (var r in dto.reactions)
                    if (!string.IsNullOrEmpty(r?.name))
                        target.reactions.Add(r.name);
            }
            if(target.passives == null) target.passives = new List<string>();
            // Actions - Passives
            target.passives.Clear();
            if (dto.passives != null)
            {
                foreach (var p in dto.passives)
                    if (!string.IsNullOrEmpty(p?.name))
                        target.passives.Add(p.name);
            }

            // Equipment
            // TODO use equipment names from creature JSON as args to look up actual Equipment items from datafiles
            //      -Done for weapons
            //      -TODO armor
            if (target.equipment == null) target.equipment = new List<string>();
            target.equipment.Clear();
            if (dto.equipment != null)
            {
                foreach (var e in dto.equipment)
                    if (!string.IsNullOrEmpty(e?.name))
                        target.equipment.Add(e.name);
            }
            // Weapons
            if (target.weapons == null){
                target.weapons = new List<EquipmentWeapon>();
                target.weapons.Clear();
                target.weaponsList.Clear();
            }
            if (target.armor == null){
                target.armor = new List<EquipmentArmor>();
                target.armor.Clear();
                target.armorList.Clear();
            }
            foreach (var e in dto.equipment)
                if (!string.IsNullOrEmpty(e?.name) && !string.IsNullOrEmpty(e?.type)){
                    // Debug.Log($"CreatureDtoMapper: processing equipment: {e.name} ({e.type})");
                    if (e.type.Equals("weapon", StringComparison.OrdinalIgnoreCase))
                    {
                        EquipmentWeapon temp = CreatureJsonConverter.GetWeaponByName(e.name);
                        if(temp == null){
                            Debug.LogWarning($"CreatureDtoMapper: weapon not found for equipment entry: {e.name}");
                        }
                        else
                        {
                            target.weapons.Add(temp);
                            target.weaponsList.Add(temp.name); // TODO temp for debugging
                        }
                    }
                    else if (e.type.Equals("armor", StringComparison.OrdinalIgnoreCase))
                    {
                        EquipmentArmor temp = CreatureJsonConverter.GetArmorByName(e.name);
                        if(temp == null){
                            Debug.LogWarning($"CreatureDtoMapper: armor not found for equipment entry: {e.name}");
                        }
                        else
                        {
                            target.armor.Add(temp);
                            target.armorList.Add(temp.name); // TODO temp for debugging
                            // target.calculateAC(); // Recalculate AC when armor is added
                        }
                    }
                    if (target.armor.Count > 0)
                    {
                        // For simplicity, assume the first armor in the list is equipped
                        target.equippedArmor = target.armor[0];
                        Debug.Log($"CreatureDtoMapper: equipped armor set to {target.equippedArmor.name} with AC bonus {target.equippedArmor.acBonus}");
                        target.calculateAC(); // Recalculate AC when armor is added
                        Debug.Log($"CreatureDtoMapper: AC after equipping armor: {target.ac}");
                    }
                }
            

            // Conditions
            if (target.conditions == null) target.conditions = new List<string>();
            target.conditions.Clear();
            if (dto.conditions != null)
            {
                foreach (var c in dto.conditions)
                    if (!string.IsNullOrEmpty(c?.name))
                        target.conditions.Add(c.name);
            }

            // Skills: dto.system.skills is now an array produced by JsonImporter
            target.skills.Clear();
            if (dto.system?.skills != null)
            {
                foreach (var s in dto.system.skills)
                    target.skills.Add(new SkillValue { skillName = s.name, skillMod = s.value });
            }

            // publicNotes (plain/paragraphs) � importer can produce these; map into description
            if (dto.system?.details != null)
            {
                var details = dto.system.details;
                if (details.publicNotesParagraphs != null && details.publicNotesParagraphs.Length > 0)
                {
                    // join paragraphs into a single description string separated by blank lines
                    target.description = string.Join("\n\n", details.publicNotesParagraphs);
                }
                else if (!string.IsNullOrEmpty(details.publicNotesPlain))
                {
                    target.description = details.publicNotesPlain;
                }
            }
        }
    }

    // --- Converter helper: parse, map, instantiate ---
    public static class CreatureJsonConverter
    {
        // Create from a file path. Optional prefab to instantiate (if null, plain GameObject is used)
        public static GameObject CreateFromFile(string jsonFilePath, GameObject prefab = null)
        {
            if (string.IsNullOrEmpty(jsonFilePath) || !File.Exists(jsonFilePath))
            {
                Debug.LogError($"CreatureJsonConverter: file not found: {jsonFilePath}");
                return null;
            }

            string json = File.ReadAllText(jsonFilePath);
            CreatureDto dto = null;
            try
            {
                dto = JsonUtility.FromJson<CreatureDto>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"CreatureJsonConverter: failed to parse JSON: {ex.Message}");
                return null;
            }

            GameObject go = prefab != null ? UnityEngine.Object.Instantiate(prefab) : new GameObject(dto?.name ?? "Creature");
            var comp = go.GetComponent<CreatureComponent>() ?? go.AddComponent<CreatureComponent>();
            comp.ApplyFromDto(dto);
            StrikeWeapon.WeaponStrikeAdderTEMP(go); // Temporary: add an arbitrary strike action based on a creature weapon for testing
            return go;
        }

        // Create by name: searches Assets/DataFiles for a matching filename (without extension)
        public static GameObject CreateByName(string creatureName, GameObject prefab = null)
        {
            if (string.IsNullOrEmpty(creatureName)) return null;
            string rootDirectory = Path.Combine(Application.dataPath, "DataFiles");
            if (!Directory.Exists(rootDirectory))
            {
                Debug.LogWarning($"CreatureJsonConverter: DataFiles directory not found: {rootDirectory}");
                return null;
            }

            var files = Directory.GetFiles(rootDirectory, "*.json", SearchOption.AllDirectories);
            var match = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(creatureName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                Debug.LogWarning($"CreatureJsonConverter: creature not found: {creatureName}");
                return null;
            }

            return CreateFromFile(match, prefab);
        }

        // Get EquipmentWeapon by name from DataFiles/equipment
        public static EquipmentWeapon GetWeaponByName(string weaponName)
        {
            // Debug.Log($"CreatureJsonConverter: looking up weapon: {weaponName}");
            if (string.IsNullOrEmpty(weaponName)) return null;
            NormalizeFilename(weaponName);
            string rootDirectory = Path.Combine(Application.dataPath, "DataFiles/equipment");
            if (!Directory.Exists(rootDirectory))
            {
                Debug.LogWarning($"CreatureJsonConverter: DataFiles directory not found: {rootDirectory}");
                return null;
            }
            var files = Directory.GetFiles(rootDirectory, "*.json", SearchOption.AllDirectories);
            files = files.Where(f => Path.GetFileNameWithoutExtension(f).Equals(weaponName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length > 1)
                Debug.LogWarning($"Mutliple instances found for : {weaponName}");
            else if (files.Length == 0)
            {
                Debug.LogWarning($"CreatureJsonConverter: weapon not found: {weaponName}");
                return null;
            }

            foreach (var file in files)
            {
                string json = File.ReadAllText(file);
                WeaponDto dto = null;
                try
                {
                    dto = JsonUtility.FromJson<WeaponDto>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"CreatureJsonConverter: failed to parse JSON in {file}: {ex.Message}");
                    continue;
                }
                EquipmentWeapon weapon = new EquipmentWeapon();
                weapon.name = dto.name;
                weapon.type = dto.type;
                weapon.group = dto.group;   
                weapon.category = dto.category;
                weapon.hands = dto.hands;
                weapon.damage = new Dice(dto.damageDice, dto.damageDie, dto.damageType);
                weapon.description = dto.description;
                weapon.traits = dto.traits;
                weapon.materialType = dto.materialType;
                weapon.materialGrade = dto.materialGrade;
                weapon.runes = dto.runes;
                weapon.price = double.TryParse(dto.price, out double priceValue) ? priceValue : 0.0; // Handle parsing price string to double
                weapon.range = dto.range;
                weapon.ammo = dto.ammo;
                weapon.bulk = dto.bulk;
                return weapon;
            }
            return null;
        }

        public static EquipmentArmor GetArmorByName(string armorName)
        {
            // Similar implementation to GetWeaponByName, but for EquipmentArmor
            // Debug.Log($"CreatureJsonConverter: looking up weapon: {armorName}");
            if (string.IsNullOrEmpty(armorName)) return null;
            armorName = NormalizeFilename(armorName);
            string rootDirectory = Path.Combine(Application.dataPath, "DataFiles/equipment");
            if (!Directory.Exists(rootDirectory))
            {
                Debug.LogWarning($"CreatureJsonConverter: DataFiles directory not found: {rootDirectory}");
                return null;
            }
            var files = Directory.GetFiles(rootDirectory, "*.json", SearchOption.AllDirectories);
            files = files.Where(f => Path.GetFileNameWithoutExtension(f).Equals(armorName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length > 1)
                Debug.LogWarning($"Mutliple instances found for : {armorName}");
            else if (files.Length == 0)
            {
                Debug.LogWarning($"CreatureJsonConverter: armor not found: {armorName}");
                return null;
            }

            foreach (var file in files)
            {
                string json = File.ReadAllText(file);
                ArmorDto dto = null;
                try
                {
                    dto = JsonUtility.FromJson<ArmorDto>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"CreatureJsonConverter: failed to parse JSON in {file}: {ex.Message}");
                    continue;
                }
                EquipmentArmor armor = new EquipmentArmor();
                armor.name = dto.name;
                armor.type = dto.type; 
                armor.category = dto.category;
                armor.price = dto.price;
                armor.acBonus = dto.acBonus;    
                armor.dexCap = dto.dexCap;
                armor.checkPenalty = dto.checkPenalty;
                armor.speedPenalty = dto.speedPenalty;
                armor.strengthRequirement = dto.strengthRequirement;
                armor.description = dto.description;
                armor.bulk = dto.bulk;
                armor.group = dto.group;
                armor.armorTraits = dto.armorTraits;
                return armor;
            }
            return null;
        }

        // helper method to convert multi word equipment names to match filenames e.g. "Leather Armor" -> "leather-armor"
        public static string NormalizeFilename(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return name.Trim().ToLower().Replace(' ', '-');
        }

    }
}