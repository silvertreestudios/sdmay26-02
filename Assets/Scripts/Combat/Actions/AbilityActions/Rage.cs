using Game.Creature;
using Game.Strikes;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using Unity.VisualScripting;
using System.Diagnostics.Tracing;
using System;
//using System.Diagnostics;

namespace Game.AbilityActions{

// TODO change to single frame entity action?
[System.Serializable]
public class Rage : MultiFrameEntityAction
{
    List<string> Traits = new List<string> {"barbarian", "concentrate", "emotion", "mental"};
    int rageBonus = 2;
    
    public Rage(uint cost) : base(cost)
    {
        // apply abilities that alter rage?
    }

    public void UseRage(GameObject g)
    {
        Debug.Log(g + " is attempting to use Rage");
        if (RageAllowed(g)){
            // Check for rage modifying abilities
            if (g.GetComponent<CreatureComponent>().passives.Contains("Fury-Instinct")) rageBonus = 3;
            // Apply THP from rage
            AddRageTHP(g);
            // Add listener to trigger bonus rage damage
            OnStrikeEvent.AddListener((Tuple<Strike, GameObject> tuple) => { if (tuple.Item2 == g) { AddRageDamage(tuple.Item1); }});
            MFInvoke(g);
        }
    }

    public bool RageAllowed(GameObject g)
    {
        // Check for conditions that would prevent raging, and return false if any are present
        bool allowed =true;
        if (g.GetComponent<Conditions>().Contains("Fatigued")){
            allowed = false;
            Debug.Log(g + " cannot Rage while Fatigued");
        }
        if (g.GetComponent<Conditions>().Contains("Raging")){ // TODO Update to look for an actual sign of rage
            allowed = false;
            Debug.Log(g + " cannot Rage while Raging");
        }
        if(g.GetComponent<CreatureComponent>().equippedArmor != null && g.GetComponent<CreatureComponent>().equippedArmor.category == "heavy")
        {
            allowed = false;
            Debug.Log(g + " cannot Rage while wearing heavy armor");
        }
        return allowed;
    }

    public void AddRageTHP(GameObject g)
    {
        ActionController ac = g.GetComponent<ActionController>();
        CreatureComponent cc = g.GetComponent<CreatureComponent>();
        
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


    public void AddRageDamage(Strike action)
    {
        // TODO make rageBonus not hardcoded
        // int rageBonus = 2;
        Debug.Log("Adding Rage damage to strike");

        if (action.getTraits().Contains("agile") || action.getTraits().Contains("unarmed"))
        {
            DamageValue dmg = action.FlatDamages[0];
            dmg.DamageAmount = rageBonus/2;
            action.FlatDamages.Add(dmg);  
        }
        else
        {
            //Debug.Log("Rage damage not applicable to this action");
            DamageValue dmg = action.FlatDamages[0];
            dmg.DamageAmount = rageBonus;
            action.FlatDamages.Add(dmg);  
        }
    }

    protected override IEnumerator MFInvoke(GameObject g)
    {
        ActionController ac = g.GetComponent<ActionController>();
        yield break;
    }
}

}
