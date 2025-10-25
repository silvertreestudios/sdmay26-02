using UnityEngine;
using System.Collections.Generic;
using Game.Equipment;

// Define an enum for actions
public enum CreatureAction
{
    Move,
    Strike
}

public enum AttackStatus
{
    Miss,
    Hit,
    CriticalMiss,
    CriticalHit
}

// Update the interface to use the specific enum type for Actions
public interface ICreature
{
    // Basic properties
    string Name { get; set; }
    int Level { get; set; }

    // Combat properties
    int HP { get; set; }
    int AC { get; set; }
    int Speed { get; set; }
    int attackBonus { get; set; } // Temporary
    int damageBonus { get; set; } // Temporary

    // Actions and Equipment
    List<CreatureAction> Actions { get; set; }
    List<Equipment> Equipment { get; set; }
}

// Extension as a Unity MonoBehaviour
// TODO : Use [SerializeField] for properties to edit in Inspector
public class CreatureComponent : MonoBehaviour, ICreature
{
    public string Name { get; set; }
    public int HP { get; set; }
    public int AC { get; set; }
    public int Level { get; set; }
    public int Speed { get; set; }
    public int attackBonus { get; set; }
    public int damageBonus { get; set; }
    public List<CreatureAction> Actions { get; set; } = new List<CreatureAction>();
    public List<Equipment> Equipment { get; set; } = new List<Equipment>();

    void Start()
    {
        // Example initialization
        Name = "Test Dummy";
        HP = 10;
        AC = 12;
        Level = 1;
        Speed = 30;
        attackBonus = 4; // Temporary
        damageBonus = 3; // Temporary
        Actions.Add(CreatureAction.Move);
        Actions.Add(CreatureAction.Strike);
        Equipment.Add(ShortSword);
    }

    void Update()
    {
        // Per-frame logic here
    }

    public void TakeDamage(int damage, DamageType damageType)
    {
        Debug.Log($"{Name} takes {damage} damage of type {damageType?.ToString() ?? "Unknown"}.");
        HP -= damage;
        if (HP < 0) HP = 0;
        Debug.Log($"{Name} now has {HP} HP remaining.");
    }

    public void Move()
    {
        CreatureActions.Move(this);
    }
    
    public void Strike(Equipment weapon, ICreature target)
    {
        CreatureActions.Strike(this, weapon, target);
    }
}
