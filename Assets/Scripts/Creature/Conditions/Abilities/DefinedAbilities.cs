using System.Collections.Generic;
using UnityEngine;
using Game.Creature;
using Game.Strikes;
using UnityEngine.Events;
using Unity.VisualScripting;

using Game.AbilityActions;

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

    private static Ability Slow = new("Slow", (GameObject g) =>
    {
        Condition slow;
        if ((slow = DefinedConditions.TryGet("Slowed 1")) != null)
        {
            slow.Apply(Slow, g);
        }
        g.GetComponent<ActionController>().GetReactionsEvent.AddListener(
            (List<EntityAction> reactions) => reactions.Clear()
        );
    });

    private static Ability QuickTempered = new("Quick-Tempered", (GameObject g) =>
    {
        // On combat start, IF conditions met, instantly use rage with no action point cost
        Debug.Log("Applying Quick-Tempered to " + g.name);
        Rage rageAction = new Rage(0);
        rageAction.UseRage(g);
    });

    private static Ability FuryInstinct = new("Fury-Instinct", (GameObject g) =>
    {
        // On combat start, IF conditions met, instantly use rage with no action point cost
        Debug.Log("Applying Fury-Instinct to " + g.name);
        // Currently handled in Rage
    });

    /// <summary>
    /// Keep last in file, needs to be initialized after all referenced abilities
    /// </summary>
    private static Dictionary<string, Ability> Abilities = new()
    {
        {"Slow", Slow },
        {"Quick-Tempered", QuickTempered},
        {"Fury-Instinct", FuryInstinct }
    };

}
