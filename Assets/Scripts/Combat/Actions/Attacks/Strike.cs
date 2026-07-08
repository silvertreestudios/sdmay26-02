using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using Game.Creature.Rules;
using Game.Combat.Rules;
using Game.Rules;
using System;
using GridPublic;

public class Strike
{
    public List<Dice> Damages;
    public List<DamageValue> FlatDamages;
    public List<string> Traits = new List<string>();
    private AttackSourceInfo sourceInfo = AttackSourceInfo.Unspecified;
    public AttackSourceInfo SourceInfo
    {
        get => sourceInfo;
        set => sourceInfo = value ?? AttackSourceInfo.Unspecified;
    }
    public int? AttackModifierOverride { get; set; }
    public string ItemSlug { get; set; }
    public string WeaponCategory { get; set; }
    public bool IsRangedAttack { get; set; }
    public int ReachFeet { get; set; } = 5;
    public GameObject From = null;
    public GameObject To = null;
    private StrikeTargetResult TargetingResult = null;

    public Strike(List<Dice> damages, List<DamageValue> flatDamages)
    {
        Damages = damages ?? new();
        FlatDamages = flatDamages ?? new();
    }

    public Strike(Strike strike)
    {
        this.Damages = new List<Dice>();
        foreach (Dice dice in strike.Damages)
            this.Damages.Add(new Dice(dice.numberOfDice, dice.sidesPerDie, dice.damageType));
        this.FlatDamages = new List<DamageValue>(strike.FlatDamages);
        this.Traits = new List<string>(strike.Traits);
        this.SourceInfo = new AttackSourceInfo(strike.SourceInfo);
        this.AttackModifierOverride = strike.AttackModifierOverride;
        this.ItemSlug = strike.ItemSlug;
        this.WeaponCategory = strike.WeaponCategory;
        this.IsRangedAttack = strike.IsRangedAttack;
        this.ReachFeet = strike.ReachFeet;
    }

    public void Damage(GameObject from_go, GameObject to_go)
    {
        Damage(from_go, to_go, null);
    }

    public void Damage(GameObject from_go, GameObject to_go, StrikeTargetResult targetingResult)
    {
        Strike evaluated = new Strike(this);
        evaluated.From = from_go;
        evaluated.To = to_go;
        evaluated.TargetingResult = targetingResult;

        Pf2eRulesEngine.ApplyStrikeDamageModifiers(from_go.GetComponent<CreatureComponent>(), evaluated, to_go.GetComponent<CreatureComponent>());
        OnStrikeEvent.Invoke(new(evaluated, from_go));
        evaluated.DamageEvaluate();
    }

    protected void DamageEvaluate()
    {
        CreatureComponent from = From.GetComponent<CreatureComponent>();
        CreatureComponent to = To.GetComponent<CreatureComponent>();
        uint strikePenaltyCount = From.GetComponent<ActionController>()?.StrikePenalty ?? 0;
        int mapPenalty = CalculateMultipleAttackPenalty(strikePenaltyCount);
        int rangePenalty = TargetingResult?.RangePenalty ?? 0;
        int coverBonus = TargetingResult?.CoverAcBonus ?? 0;
        bool flankedOffGuard = FlankingRule.GrantsOffGuardToMeleeAttack(From, To, this);

        int attackBonus = AttackModifierOverride ?? from.attackBonus;
        Pf2eModifierResolution attackResolution = from.ResolveAttackRoll(AttackModifierOverride, BuildStrikeAttackModifiers(mapPenalty, rangePenalty));
        Pf2eModifierResolution baseAcResolution = to.ResolveArmorClass();
        Pf2eModifierResolution targetAcResolution = to.ResolveArmorClass(BuildStrikeAcModifiers(coverBonus, flankedOffGuard));
        int targetAc = targetAcResolution.Total;
        int totalModifier = attackResolution.Total;

        D20Result attackRoll = D20.Roll(totalModifier, targetAc);

        string log = "Attack:\n  AC: " + targetAc;
        if (coverBonus != 0)
            log += " (" + baseAcResolution.Total + " + " + coverBonus + " cover)";
        log += "\n  Attack Roll: " + attackRoll.total
            + " (" + attackRoll.roll + " + " + attackBonus + " - " + mapPenalty;
        if (rangePenalty != 0)
            log += " " + rangePenalty;
        log += ")\n  Result: " + attackRoll.degree;
        log += "\n  Attack Modifiers: " + FormatResolution(attackResolution);
        log += "\n  AC Modifiers: " + FormatResolution(targetAcResolution);

        if (attackRoll.degree == DegreeOfSuccess.Success || attackRoll.degree == DegreeOfSuccess.CriticalSuccess)
        {
            CombatLog.GetInstance().Log(log);
            AttackResultContext context = new AttackResultContext
            {
                AttackerObject = From,
                TargetObject = To,
                AttackerCreature = from,
                TargetCreature = to,
                Strike = this,
                SourceInfo = SourceInfo,
                Traits = Traits,
                D20Result = attackRoll,
                Degree = attackRoll.degree,
                DamageDice = CloneDamageDice(Damages),
                FlatDamages = new List<DamageValue>(FlatDamages),
                DamageValues = new List<DamageValue>(),
                TargetingResult = TargetingResult,
                BaseArmorClass = baseAcResolution.Total,
                TargetArmorClass = targetAc,
                AttackBonus = attackBonus,
                TotalAttackModifier = totalModifier,
                MultipleAttackPenalty = mapPenalty,
                RangePenalty = rangePenalty,
                CoverAcBonus = coverBonus
            };
            AttackResultPipeline.ProcessHit(context);
        }
        else
        {
            OnAttackMiss.Invoke(From);
            log += "\nAttack Missed!";
            CombatLog.GetInstance().Log(log);
        }
    }

    private static List<Dice> CloneDamageDice(List<Dice> diceList)
    {
        List<Dice> clone = new();
        if (diceList == null)
            return clone;

        foreach (Dice dice in diceList)
            clone.Add(new Dice(dice.numberOfDice, dice.sidesPerDie, dice.damageType));
        return clone;
    }

    private static IEnumerable<Pf2eModifier> BuildStrikeAttackModifiers(int mapPenalty, int rangePenalty)
    {
        if (mapPenalty != 0)
            // Multiple attack penalty source: https://2e.aonprd.com/Rules.aspx?ID=2288
            yield return new Pf2eModifier(-mapPenalty, Pf2eModifierType.Untyped, "Multiple attack penalty", Pf2eStatistic.AttackRoll);
        if (rangePenalty != 0)
            // Range penalty source: https://2e.aonprd.com/Rules.aspx?ID=2288
            yield return new Pf2eModifier(rangePenalty, Pf2eModifierType.Untyped, "Range penalty", Pf2eStatistic.AttackRoll);
    }

    private static IEnumerable<Pf2eModifier> BuildStrikeAcModifiers(int coverBonus, bool flankedOffGuard)
    {
        if (coverBonus != 0)
            // Cover source: https://2e.aonprd.com/Rules.aspx?ID=2372
            yield return new Pf2eModifier(coverBonus, Pf2eModifierType.Circumstance, "Cover", Pf2eStatistic.ArmorClass);
        if (flankedOffGuard)
            // Flanking source: https://2e.aonprd.com/Rules.aspx?ID=2375
            yield return new Pf2eModifier(-2, Pf2eModifierType.Circumstance, "Off-Guard", Pf2eStatistic.ArmorClass);
    }

    private static string FormatResolution(Pf2eModifierResolution resolution)
    {
        return "total " + resolution.Total + "; applied [" + FormatModifiers(resolution.AppliedModifiers) + "]; suppressed [" + FormatModifiers(resolution.SuppressedModifiers) + "]";
    }

    private static string FormatModifiers(IReadOnlyList<Pf2eModifier> modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
            return "none";

        List<string> parts = new();
        foreach (Pf2eModifier modifier in modifiers)
            parts.Add(modifier.Source + " " + FormatSigned(modifier.Value) + " " + modifier.Type);
        return string.Join(", ", parts);
    }

    private static string FormatSigned(int value)
    {
        return value >= 0 ? "+" + value : value.ToString();
    }

    private int CalculateMultipleAttackPenalty(uint strikePenaltyCount)
    {
        if (strikePenaltyCount == 0)
            return 0;

        bool agile = Traits.Contains("agile");
        if (strikePenaltyCount == 1)
            return agile ? 4 : 5;
        return agile ? 8 : 10;
    }

    public List<string> getTraits()
    {
        return Traits;
    }

    public override string ToString()
    {
        string traits = "";
        foreach (string trait in Traits)
        {
            traits += trait + " ";
        }
        return "Strike Action: " + Damages.Count + " damage rolls, " + FlatDamages.Count + " flat damages, Traits: " + traits;
    }

    public float GetAvgDmg()
    {
        float avg = 0;
        foreach (Dice dice in Damages)
        {
            avg += dice.numberOfDice * ((float)dice.sidesPerDie + 1) / 2;
        }
        foreach (DamageValue damageValue in FlatDamages)
        {
            avg += damageValue.DamageAmount;
        }
        return avg;
    }
}

public class OnStrikeEvent : StaticUnityEvent<OnStrikeEvent, Tuple<Strike, GameObject>>{ }
