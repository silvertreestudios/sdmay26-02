using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public static class DefinedConditions
{
    private static Dictionary<string, Condition> Conditions = new()
    {
        {"Slowed 1", new Condition("Slowed", (GameObject g) => Slowed(g, 1)) },
        {"Slowed 2", new Condition("Slowed", (GameObject g) => Slowed(g, 2)) },
        {"Slowed 3", new Condition("Slowed", (GameObject g) => Slowed(g, 3)) },
    };

    /// <summary>
    /// Attempts to add a condition to the list of conditions
    /// </summary>
    /// <param name="condition"></param>
    /// <returns>False if condition already exists</returns>
    public static bool Add(Condition condition)
    {
        if(!Conditions.TryGetValue(condition.Name, out _))
        {
            return false;
        }
        Conditions.Add(condition.Name, condition);
        return true;
    }

    public static Condition TryGet(string conditionName)
    {
        Condition result;
        if(Conditions.TryGetValue(conditionName, out result))
            return result;
        return null;
    }

    /// <summary>Target can't see; auto-fails vision Perception; vision is difficult terrain, immune to visual effects.</summary>
    public static void Blinded(GameObject target) { }

    /// <summary>Object is broken and can't be used normally; armor still grants bonuses but penalties apply.</summary>
    public static void Broken(GameObject target) { }

    /// <summary>Movements are clumsy; status penalty to Dexterity-based rolls and DCs.</summary>
    public static void Clumsy(GameObject target) { }

    /// <summary>Harder to be seen; creature must succeed at a flat check to target.</summary>
    public static void Concealed(GameObject target) { }

    /// <summary>Act without wits; must attack randomly; restricted actions.</summary>
    public static void Confused(GameObject target) { }

    /// <summary>Controlled by a dominating effect; controller dictates actions.</summary>
    public static void Controlled(GameObject target) { }

    /// <summary>Vision overstimulated; if only precise sense is vision, all is concealed.</summary>
    public static void Dazzled(GameObject target) { }

    /// <summary>Can't hear; critical fails sound-based Perception; penalties to checks involving sound.</summary>
    public static void Deafened(GameObject target) { }

    /// <summary>Soul is gripped toward death; lowers dying threshold; value decreases on rest.</summary>
    public static void Doomed(GameObject target) { }

    /// <summary>Health and vitality depleted; status penalty to Con-based rolls; reduces HP and max HP.</summary>
    public static void Drained(GameObject target) { }

    /// <summary>Bleeding out; unconscious; must check recovery each turn; can die if condition reaches 4.</summary>
    public static void Dying(GameObject target) { }

    /// <summary>Carrying too much; clumsy 1 and –10-foot penalty to all Speeds.</summary>
    public static void Encumbered(GameObject target) { }

    /// <summary>Physically weakened; status penalty to Str-based rolls and DCs.</summary>
    public static void Enfeebled(GameObject target) { }

    /// <summary>Distracted by something; status penalty to Perception and skill checks; limits concentrate actions.</summary>
    public static void Fascinated(GameObject target) { }

    /// <summary>Tired; –1 status penalty to AC and saves; can't use exploration activities.</summary>
    public static void Fatigued(GameObject target) { }

    /// <summary>Forced to run away from source; must use actions to escape.</summary>
    public static void Fleeing(GameObject target) { }

    /// <summary>Creature's disposition is friendly; likely to agree to simple requests.</summary>
    public static void Friendly(GameObject target) { }

    /// <summary>Gripped by fear; status penalty to all checks and DCs; value decreases each turn.</summary>
    public static void Frightened(GameObject target) { }

    /// <summary>Held in place by another; gives off-guard and immobilized.</summary>
    public static void Grabbed(GameObject target) { }

    /// <summary>Creature's disposition is helpful; actively aids character.</summary>
    public static void Helpful(GameObject target) { }

    /// <summary>Target knows space but can't tell precise location; harder to hit.</summary>
    public static void Hidden(GameObject target) { }

    /// <summary>Creature's disposition is hostile; actively seeks to harm character.</summary>
    public static void Hostile(GameObject target) { }

    /// <summary>Incapable of movement; can't take move actions or move traits.</summary>
    public static void Immobilized(GameObject target) { }

    /// <summary>Creature's disposition is indifferent; doesn’t care about character.</summary>
    public static void Indifferent(GameObject target) { }

    /// <summary>Target takes a circumstance penalty to AC; distracted and off-guard.</summary>
    public static void OffGuard(GameObject target) { }

    /// <summary>Frozen in place; off-guard; limited mental actions only.</summary>
    public static void Paralyzed(GameObject target) { }

    /// <summary>Taking recurring damage each turn; condition persists until healed or check ends it.</summary>
    public static void PersistentDamage(GameObject target) { }

    /// <summary>Turned to stone; can't act or sense; becomes an object.</summary>
    public static void Petrified(GameObject target) { }

    /// <summary>Lying on the ground; off-guard; penalty to attack rolls; restricted movement.</summary>
    public static void Prone(GameObject target) { }

    /// <summary>Gains an extra action at start of turn from quickened effects.</summary>
    public static void Quickened(GameObject target) { }

    /// <summary>Restrained; off-guard and immobilized; can try to Escape or Force Open only.</summary>
    public static void Restrained(GameObject target) { }

    /// <summary>Ill; status penalty to all checks and DCs; can't willingly ingest anything.</summary>
    public static void Sickened(GameObject target) { }

    /// <summary>Fewer actions; actions regained reduced by slowed value.</summary>
    public static void Slowed(GameObject target, uint tier) 
    {
        ActionController ac = target.GetComponent<ActionController>();
        ac.GetReactionsEvent.AddListener((List<EntityAction> reactions) => reactions.Clear());
    }

    /// <summary>Senseless; lose actions; stunned may include total actions lost.</summary>
    public static void Stunned(GameObject target) { }

    /// <summary>Thoughts clouded; status penalty to Int, Wis, Cha rolls and DCs.</summary>
    public static void Stupefied(GameObject target) { }

    /// <summary>Unconscious; can't act; takes penalties; drops prone and drops held items.</summary>
    public static void Unconscious(GameObject target) { }

    /// <summary>Creature is undetected by another; completely unaware of presence.</summary>
    public static void Undetected(GameObject target) { }

    /// <summary>Creature's disposition is unfriendly; worse than indifferent.</summary>
    public static void Unfriendly(GameObject target) { }

    /// <summary>Target is unnoticed: known to be present but without precise location.</summary>
    public static void Unnoticed(GameObject target) { }

    /// <summary>Creature has wounds; represents injuries below 0 HP (linked to dying recovery).</summary>
    public static void Wounded(GameObject target) { }
}