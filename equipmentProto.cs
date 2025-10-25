using System;

namespace Game.Equipment
{
    // Define an enum for equipment types
    public enum EquipmentType
    {
        Weapon,
        Armor,
        Shield
    }

    // Struct for weapon damage
    public struct DamageInfo
    {
        public int DiceCount;      // Number of dice to roll
        public int DiceSides;      // Sides per die
        public string DamageType;  // e.g., "Piercing", "Slashing"

        public DamageInfo(int diceCount, int diceSides, string damageType)
        {
            DiceCount = diceCount;
            DiceSides = diceSides;
            DamageType = damageType;
        }

        public DamageInfo(int flatDamage, string damageType)
        {
            DiceCount = 1;
            DiceSides = flatDamanage;
            DamageType = damageType;
        }
    }

    // Define a simple Equipment class
    public class Equipment
    {
        public string Name { get; set; }
        public EquipmentType Type { get; set; }
        public DamageInfo Damage { get; set; }

        // Static property for a Short Sword
        public static Equipment ShortSword =>
            new Equipment
            {
                Name = "Short Sword",
                Type = EquipmentType.Weapon,
                Damage = new DamageInfo(1, 6, "Piercing")
            };

        // Static property for chainsaw test weapon
        // TODO : Remove after testing
        public static Equipment Chainsaw =>
            new Equipment
            {
                Name = "Chainsaw",
                Type = EquipmentType.Weapon,
                Damage = new DamageInfo(8, 6, "Slashing")
            };
    }
}