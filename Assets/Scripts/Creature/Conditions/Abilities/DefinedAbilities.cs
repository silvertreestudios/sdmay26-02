using System.Collections.Generic;
using Game.Creature;
using Game.Strikes;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

public static class DefinedAbilities
{
    /// <summary>
    /// Attempts to add a condition to the list of conditions
    /// </summary>
    /// <param name="ability"></param>
    /// <returns>False if condition already exists</returns>
    public static bool Add(Ability ability)
    {
        if (!Abilities.TryGetValue(ability.Name, out _))
        {
            return false;
        }
        Abilities.Add(ability.Name, ability);
        return true;
    }

    /// <summary>
    /// Attempts to retrieve a condition from the list of defined conditions
    /// </summary>
    /// <param name="abilityName"></param>
    /// <returns></returns>
    public static Ability TryGet(string abilityName)
    {
        Ability result;
        if (Abilities.TryGetValue(abilityName, out result))
            return result;
        return null;
    }

    private static Ability Slow = new(
        "Slow",
        (GameObject g) =>
        {
            ActionController actionController = g.GetComponent<ActionController>();
            if (actionController == null)
                return;

            Conditions conditions = g.GetComponent<Conditions>() ?? g.AddComponent<Conditions>();
            if (conditions.Contains("Slowed", Slow))
                return;

            Condition slow;
            if ((slow = DefinedConditions.TryGet("Slowed 1")) != null)
            {
                slow.Apply(Slow, g);
            }

            actionController.GetReactionsEvent.AddListener(
                (List<EntityAction> reactions) => reactions.Clear()
            );
        }
    );

    private static Ability FuryInstinct = new(
        "Fury-Instinct",
        (GameObject g) =>
        {
            // On combat start, IF conditions met, instantly use rage with no action point cost
            Debug.Log("Applying Fury-Instinct to " + g.name);
            // Currently handled in Rage
        }
    );

    private static Ability ZombieFist = new(
        "Zombie-Fist",
        (GameObject g) =>
        {
            // Strike extraction reads this imported passive while creating the authoritative
            // unarmed definition. Combat-start processing therefore has no mutable action to add.
            Debug.Log("Zombie-Fist prepared for " + g.name);
        }
    );

    /// <summary>
    /// Keep last in file, needs to be initialized after all referenced abilities
    /// </summary>
    private static Dictionary<string, Ability> Abilities = new()
    {
        { "Slow", Slow },
        { "Fury-Instinct", FuryInstinct },
        { "Zombie-Fist", ZombieFist },
    };
}
