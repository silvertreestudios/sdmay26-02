using Game.Creature;
using Game.Strikes;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using Unity.VisualScripting;

[System.Serializable]
public class Rage : MultiFrameEntityAction
{
    List<string> Traits = new List<string> {"barbarian", "concentrate", "emotion", "mental"};
    
    public Rage(uint cost) : base(cost)
    {
        // apply abilities that alter rage?
    }

    public void UseRage(GameObject g)
    {
        MFInvoke(g);
    }

    protected override IEnumerator MFInvoke(GameObject g)
    {
        ActionController ac = g.GetComponent<ActionController>();

        // TODO replace with actual raging check, "Raging" is not technically a condition
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

            // Add rage damage bonus to StrikeWeapon actions
            int rageBonus = 2;
            List <EntityAction> actions = g.GetComponent<ActionController>().GetActions();
            foreach (var action in actions)
            {
                if(action is StrikeWeapon)
                {
                    if(((StrikeWeapon)action).GetWeapon().range == 0)
                        {
                        // half bonus for agile/unarmed attacks
                        if (((StrikeWeapon)action).GetWeapon().traits.Contains("agile")){
                            DamageValue dmg = ((StrikeWeapon)action).GetStrike().FlatDamages[0];
                            dmg.DamageAmount += rageBonus/2;
                            ((StrikeWeapon)action).GetStrike().FlatDamages[0] = dmg;
                        }
                        // full bonus
                        else{
                            DamageValue dmg = ((StrikeWeapon)action).GetStrike().FlatDamages[0];
                            dmg.DamageAmount += rageBonus;
                            ((StrikeWeapon)action).GetStrike().FlatDamages[0] = dmg;
                        }
                    }
                }
                else if (action is Unarmed)
                {
                    // half bonus for agile/unarmed attacks
                    DamageValue dmg = ((Unarmed)action).GetStrike().FlatDamages[0];
                    dmg.DamageAmount += rageBonus/2;
                    ((Unarmed)action).GetStrike().FlatDamages[0] = dmg;
                }
            }

            if (ac)
            {
                Debug.Log(g + " used Rage");
                PayCost(ac);
                ac.IsTakingAction = false;
            }
            Debug.Log(g + " is now Raging");

            yield break;
        }
    }
}
