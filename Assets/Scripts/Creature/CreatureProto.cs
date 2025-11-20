using UnityEngine;
using System.Collections.Generic;
/*
// Define an enum for actions
public enum CreatureAction
{
    Move,
    Strike
}

// Update the interface to use the specific enum type for Actions
public interface ICreature
{
    // Basic properties
    string name { get; set; }
    int level { get; set; }

    // Combat properties
    int hp { get; set; }
    int ac { get; set; }
    int speed { get; set; }
    int attackBonus { get; set; } // Temporary
    int damageBonus { get; set; } // Temporary
    public List<DamageValues> weaknesses {get; set; }
    public List<DamageValues> resistances {get; set; }

    // Actions and Equipment
    List<CreatureAction> actions { get; set; }
    List<Equipment> equipment { get; set; }
}

// Extension as a Unity MonoBehaviour
// TODO : Use [SerializeField] for properties to edit in Inspector
public class CreatureComponent : MonoBehaviour, ICreature
{
    public string name { get; set; }
    public int hp { get; set; }
    public int ac { get; set; }
    public int level { get; set; }
    public int speed { get; set; }
    public int attackBonus { get; set; } // Temporary
    public int damageBonus { get; set; } // Temporary
    public List<DamageValues> weaknesses {get; set; } = new List<DamageValues>();
    public List<DamageValues> resistances {get; set; } = new List<DamageValues>();
    public List<CreatureAction> actions { get; set; } = new List<CreatureAction>();
    public List<Equipment> equipment { get; set; } = new List<Equipment>();

    void Start()
    {
        // Example initialization
        name = "Test Dummy";
        hp = 10;
        ac = 12;
        level = 1;
        speed = 25;
        attackBonus = 5; // Temporary
        damageBonus = 3; // Temporary
        actions.Add(CreatureAction.Move);
        actions.Add(CreatureAction.Strike);
        equipment.Add(ShortSword);
    }

    void Update()
    {
        // Per-frame logic here
    }

    public void TakeDamage(List<DamageValue> damageValues, D20Result attackRoll)
    {
        // TODO : call function to apply resistances, immunities, vulnerabilities against damageValues
        DamageRoller.EvaluateCriticalDamage(attackRoll.DegreeOfSuccess, damageValues);
        ApplyWeaknessAndResitance(damageValues, weaknesses, resistances);
        int damage = DamageRoller.SumDamage(damageValues);
        HP -= damage;
        if (HP < 0) HP = 0;
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
*/