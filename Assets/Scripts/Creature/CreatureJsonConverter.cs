using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Game.Creature
{
    // --- DTOs (JsonUtility requires [Serializable] classes with public fields) ---
    [Serializable]
    public class CreatureDto
    {
        public string name;
        public string type;
        public SystemDto system;
        public ItemDto[] items;
        public EquipmentDto[] equipment;
        public string Source;
    }

    // Skill DTO for the array form produced by JsonImporter
    [Serializable]
    public class SkillDto
    {
        public string name;
        public int @base; // matches "base" key in JSON; use @ to escape keyword
    }

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

    [Serializable] public class ItemDto { public string name; public string type; public ItemSystemDto system; }
    [Serializable] public class ItemSystemDto { public BonusDto bonus; public DamageRollsDto damageRolls; }
    [Serializable] public class BonusDto { public int value; }
    [Serializable] public class DamageRollsDto { public string damage; public string damageType; }

    [Serializable] public class EquipmentDto { public string name; public string type; public int quantity; }

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

            // Attack bonus from first item if present
            target.attackBonus = dto.items != null && dto.items.Length > 0
                ? dto.items[0]?.system?.bonus?.value ?? target.attackBonus
                : target.attackBonus;

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

            // Actions (store item names)
            if (target.actions == null) target.actions = new List<string>();
            target.actions.Clear();
            if (dto.items != null)
            {
                foreach (var it in dto.items)
                    if (!string.IsNullOrEmpty(it?.name))
                        target.actions.Add(it.name);
            }

            // Equipment
            if (target.equipment == null) target.equipment = new List<string>();
            target.equipment.Clear();
            if (dto.equipment != null)
            {
                foreach (var e in dto.equipment)
                    if (!string.IsNullOrEmpty(e?.name))
                        target.equipment.Add(e.name);
            }

            // Skills: dto.system.skills is now an array produced by JsonImporter
            target.skills.Clear();
            if (dto.system?.skills != null)
            {
                foreach (var s in dto.system.skills)
                    target.skills.Add(new skillValue { skillName = s.name, skillMod = s.@base });
            }

            // publicNotes (plain/paragraphs) — importer can produce these; map into description
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

            // leave damageBonus and other fields untouched unless DTO provides them
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
    }
}