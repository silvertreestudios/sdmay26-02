using System;
using Game.Creature;

namespace Game.Creature
{
    // Define a simple Equipment class
    public class Equipment
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string category { get; set; }
        public Dice Damage { get; set; }
        // TODO: traits, usage, description

        // Static property for a Short Sword
        public static Equipment scimitar =>
            new Equipment
            {
                Name = "Scimitar",
                Type = "weapon",
                category = "martial",
                Damage = new Dice(1,"d6", "slashing")
            };
    }
}