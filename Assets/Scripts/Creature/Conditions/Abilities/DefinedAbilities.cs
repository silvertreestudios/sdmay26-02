using System.Collections.Generic;
using Game.AbilityActions;
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

            actionController.AddRuleReactionListener(
                (List<EntityAction> reactions) => reactions.Clear()
            );
        }
    );

    private static Ability QuickTempered = new(
        "Quick-Tempered",
        (GameObject g) =>
        {
            // On combat start, IF conditions met, instantly use rage with no action point cost
            Debug.Log("Applying Quick-Tempered to " + g.name);
            // Encounter composition owns the awaited zero-action Rage application.
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
            // On combat start, IF conditions met, add unarmed strike with 1d6 bludgeoning damage instead of 1d4
            // TODO add grapple and move from passives
            CreatureComponent cc = g.GetComponent<CreatureComponent>();
            List<Dice> damageDice = new() { new Dice(1, 6, "Bludgeoning") };
            List<DamageValue> damageFlat = new() { new DamageValue("Bludgeoning", cc.strMod) };
            Unarmed unarmedStrike = new Unarmed(1, damageDice, damageFlat);
            g.GetComponent<ActionController>().AddAction(unarmedStrike);
            Debug.Log("Zombie-Fist added to " + g.name);
        }
    );

    /// <summary>
    /// Keep last in file, needs to be initialized after all referenced abilities
    /// </summary>
    private static Dictionary<string, Ability> Abilities = new()
    {
        { "Slow", Slow },
        { "Quick-Tempered", QuickTempered },
        { "Fury-Instinct", FuryInstinct },
        { "Zombie-Fist", ZombieFist },
    };
}
