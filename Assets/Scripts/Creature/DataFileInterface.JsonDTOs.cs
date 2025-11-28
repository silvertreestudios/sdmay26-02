using UnityEngine;
using System;

namespace Game.Creature
{
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

    [Serializable]
    public class SystemDto
    {
        public AbilitySetDto abilities;
        public AttributesDto attributes;
        public DetailsDto details;
        public InitiativeDto initiative;
        public PerceptionDto perception;
        public SaveSetDto saves;
        public SkillSetDto skills;
        public TraitsDto traits;
        public WeaknessDto[] weaknesses;
        public ResistanceDto[] resistances;
    }

    [Serializable] public class DetailsDto { public LevelDto level; public string blurb; public LanguagesDto languages; public string privateNotes; public string publicNotes; public PublicationDto publication; }
    [Serializable] public class LevelDto { public int value; }
    [Serializable] public class LanguagesDto { public string details; public string[] value; }
    [Serializable] public class PublicationDto { public string license; public bool remaster; public string title; }

    [Serializable]
    public class AttributesDto
    {
        public AcDto ac;
        public AllSavesDto allSaves;
        public HpDto hp;
        public SpeedDto speed;
    }
    [Serializable] public class AcDto { public string details; public int value; }
    [Serializable] public class AllSavesDto { public string value; }
    [Serializable] public class HpDto { public string details; public int max; public int temp; public int value; }
    [Serializable] public class SpeedDto { public string[] otherSpeeds; public int value; }

    [Serializable] public class InitiativeDto { public string statistic; }

    [Serializable] public class PerceptionDto { public string details; public int mod; public SenseDto[] senses; }
    [Serializable] public class SenseDto { public string type; }

    [Serializable] public class AbilitySetDto { public AbilityDto str; public AbilityDto dex; public AbilityDto con; public AbilityDto @int; public AbilityDto wis; public AbilityDto cha; }
    [Serializable] public class AbilityDto { public int mod; }

    [Serializable] public class SaveSetDto { public SaveDto fortitude; public SaveDto reflex; public SaveDto will; }
    [Serializable] public class SaveDto { public string saveDetail; public int value; }

    [Serializable] public class SkillSetDto { /* minimal placeholder; extend if needed */ }

    [Serializable] public class TraitsDto { public string rarity; public SizeDto size; public string[] value; }
    [Serializable] public class SizeDto { public string value; }

    [Serializable] public class WeaknessDto { public string type; public int value; }
    [Serializable] public class ResistanceDto { public string type; public int value; }

    [Serializable]
    public class ItemDto
    {
        public string name;
        public string type;
        public ItemSystemDto system;
    }
    [Serializable]
    public class ItemSystemDto
    {
        public AttackDto attack;
        public AttackEffectsDto attackEffects;
        public BonusDto bonus;
        public DamageRollsDto damageRolls;
        public DescriptionDto description;
        public RangeDto range;
        // other fields omitted for brevity
    }
    [Serializable] public class AttackDto { public string value; }
    [Serializable] public class AttackEffectsDto { public string custom; public string[] value; }
    [Serializable] public class BonusDto { public int value; }
    [Serializable] public class DamageRollsDto { public string damage; public string damageType; }
    [Serializable] public class DescriptionDto { public string value; }
    [Serializable] public class RangeDto { public int? increment; public int? max; }

    [Serializable]
    public class EquipmentDto
    {
        public string name;
        public string type;
        public int quantity;
    }
}