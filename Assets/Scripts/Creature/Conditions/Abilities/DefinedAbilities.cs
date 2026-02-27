using System.Collections.Generic;
using UnityEngine;
using Game.Creature;
using Game.Strikes;
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

    private static Ability Rage = new("Rage", (GameObject g) =>
    {
        // TODO replace with actual rage check
        if (!g.GetComponent<Conditions>().Contains("Fatigued") && !g.GetComponent<Conditions>().Contains("Raging"))
        {
            //g.GetComponent<Conditions>().Add("Raging", g);

            // Add THP, if rage hasn't ended with 1 min == 10 turns
            if (true)
            {
                int tempHP = g.GetComponent<CreatureComponent>().level;
                tempHP += g.GetComponent<CreatureComponent>().conMod;
                g.GetComponent<CreatureComponent>().GainTempHp(tempHP);
            }

            // Add rage damage bonus
            int rageBonus = 2;
            List <EntityAction> actions = g.GetComponent<ActionController>().GetActions();
            foreach (var action in actions)
            {
                if(action is StrikeWeapon)
                {
                    if(((StrikeWeapon)action).GetWeapon().range == 0)
                        {
                        string damageType = ((StrikeWeapon)action).GetStrike().FlatDamages[0].DamageType;
                        // half bonus
                        if (((StrikeWeapon)action).GetWeapon().traits.Contains("agile") || ((StrikeWeapon)action).GetWeaponName() == "unarmed")
                        {
                        ((StrikeWeapon)action).GetStrike().FlatDamages.Add(new DamageValue(damageType, rageBonus/2));
                        }
                        // full bonus
                        else
                        {
                            ((StrikeWeapon)action).GetStrike().FlatDamages.Add(new DamageValue(damageType, rageBonus));
                        }
                    }
                }
            }
        }
        g.GetComponent<ActionController>();
    });

    /// <summary>
    /// Keep last in file, needs to be initialized after all referenced abilities
    /// </summary>
    private static Dictionary<string, Ability> Abilities = new()
    {
        {"Slow", Slow },
        {"Rage", Rage}
    };

}
