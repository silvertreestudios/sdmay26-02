using Game.Creature;
using Game.Strikes;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using Unity.VisualScripting;
using System.Diagnostics.Tracing;

// TODO change to single frame entity action?
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
        Debug.Log(g + " is attempting to use Rage");
        MFInvoke(g);
    }

    public void RageAllowed(GameObject g)
    {
        if (!g.GetComponent<Conditions>().Contains("Fatigued") && !g.GetComponent<Conditions>().Contains("Raging"))
        {
            //AddRageTHP(g);
            //ac.ResetActionPointsEvent.AddListener((Ref<uint> points) => { points.Value -= tier; });
            AddRageDamage(g);
        }
    }

    public void AddRageTHP(GameObject g)
    {
        ActionController ac = g.GetComponent<ActionController>();
        CreatureComponent cc = g.GetComponent<CreatureComponent>();
        /*
        g.addTHP(x);
        OnEndRage.addListener( //Remove THP )
        g.OnAddTHP.addListener(  //endRageRemoveListenerRemoveTHP )
        */
        
        if (cc.tempHp > 0)
        {
            // TODO if creature already has THP, UI prompt to ask if they want to accept rage THP
        }
        // Add THP, if rage hasn't ended with 1 min == 10 turns
        if (true)
        {
            int tempHP = cc.level;
            tempHP += cc.conMod;
            cc.GainTempHp(tempHP);
            if (ac)
            {
                Debug.Log(g + " used Rage");
                PayCost(ac);
                ac.IsTakingAction = false;
            }
        }
    }

    public void AddRageDamage(GameObject g)
    {
        /*
        OnRageDamageBonus.addListener( //apply damage bonus to strikes )
        OnEndRage.addListener( //Remove damage bonus from strikes )
        */
        // Add rage damage bonus to StrikeWeapon and Unarmed actions
        int rageBonus = 2;
        List <EntityAction> actions = g.GetComponent<ActionController>().GetActions();
        foreach (var action in actions)
        {
            if(action is StrikeWeapon)
            {
                Debug.Log(g + " applying Rage bonus to " + ((StrikeWeapon)action).GetWeaponName());
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
                Debug.Log(g + " applying Rage bonus to Unarmed");
                // half bonus for agile/unarmed attacks
                DamageValue dmg = ((Unarmed)action).GetStrike().FlatDamages[0];
                dmg.DamageAmount += rageBonus/2;
                ((Unarmed)action).GetStrike().FlatDamages[0] = dmg;
            }
        }
    }

    protected override IEnumerator MFInvoke(GameObject g)
    {
        ActionController ac = g.GetComponent<ActionController>();

       

        yield break;
    }
}
