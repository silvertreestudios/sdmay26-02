using UnityEngine;
using System.Collections.Generic;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules;
using System;
using GridPublic;

public class Strike
{
    public List<Dice> Damages;
    public List<DamageValue> FlatDamages;
    public List<string> Traits = new List<string>();
    public AttackSourceInfo SourceInfo { get; set; }
    public int? AttackModifierOverride { get; set; }
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
        this.SourceInfo = strike.SourceInfo == null ? null : new AttackSourceInfo(strike.SourceInfo);
        this.AttackModifierOverride = strike.AttackModifierOverride;
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

        Pf2eRulesEngine.ApplyStrikeDamageModifiers(from_go.GetComponent<CreatureComponent>(), evaluated);
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

        int attackBonus = AttackModifierOverride ?? from.attackBonus;
        Pf2eModifierResolution attackResolution = from.ResolveAttackRoll(AttackModifierOverride, BuildStrikeAttackModifiers(mapPenalty, rangePenalty));
        Pf2eModifierResolution baseAcResolution = to.ResolveArmorClass();
        Pf2eModifierResolution targetAcResolution = to.ResolveArmorClass(BuildStrikeAcModifiers(coverBonus));
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

    private static IEnumerable<Pf2eModifier> BuildStrikeAcModifiers(int coverBonus)
    {
        if (coverBonus != 0)
            // Cover source: https://2e.aonprd.com/Rules.aspx?ID=2372
            yield return new Pf2eModifier(coverBonus, Pf2eModifierType.Circumstance, "Cover", Pf2eStatistic.ArmorClass);
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

public class AttackSourceInfo
{
    public string Name { get; }
    public string Group { get; }
    public string Category { get; }
    public IReadOnlyList<string> Traits { get; }
    public EquipmentWeapon EquipmentWeapon { get; }

    public AttackSourceInfo(string name, string group, string category, IEnumerable<string> traits = null, EquipmentWeapon equipmentWeapon = null)
    {
        Name = name ?? string.Empty;
        Group = group ?? string.Empty;
        Category = category ?? string.Empty;
        Traits = traits == null ? new List<string>() : new List<string>(traits);
        EquipmentWeapon = equipmentWeapon;
    }

    public AttackSourceInfo(AttackSourceInfo sourceInfo)
        : this(sourceInfo?.Name, sourceInfo?.Group, sourceInfo?.Category, sourceInfo?.Traits, sourceInfo?.EquipmentWeapon)
    {
    }

    public static AttackSourceInfo FromWeapon(EquipmentWeapon weapon)
    {
        if (weapon == null)
            return null;

        return new AttackSourceInfo(weapon.name, weapon.group, weapon.category, weapon.traits, weapon);
    }
}

public enum AttackResultEffectPhase
{
    BeforeDamageRoll = 0,
    AfterCriticalDoubling = 1,
    BeforeDefenseAdjustments = 2,
    AfterDamageApplied = 3
}

public interface IAttackResultEffect
{
    AttackResultEffectPhase Phase { get; }
    void Apply(AttackResultContext context);
}

public interface IAttackResultEffectProvider
{
    IEnumerable<IAttackResultEffect> GetAttackResultEffects(AttackResultContext context);
}

public class AttackResultContext
{
    private List<string> explicitTraits = new();
    private List<string> traits = new();
    private AttackSourceInfo sourceInfo;

    public GameObject AttackerObject { get; set; }
    public GameObject TargetObject { get; set; }
    public CreatureComponent AttackerCreature { get; set; }
    public CreatureComponent TargetCreature { get; set; }
    public Strike Strike { get; set; }
    public AttackSourceInfo SourceInfo
    {
        get => sourceInfo;
        set
        {
            sourceInfo = value;
            RefreshTraits();
        }
    }
    public List<string> Traits
    {
        get => traits;
        set
        {
            explicitTraits = value == null ? new List<string>() : new List<string>(value);
            RefreshTraits();
        }
    }
    public D20Result D20Result { get; set; }
    public DegreeOfSuccess Degree { get; set; }
    public List<Dice> DamageDice { get; set; } = new();
    public List<DamageValue> FlatDamages { get; set; } = new();
    public List<DamageValue> DamageValues { get; set; } = new();
    public StrikeTargetResult TargetingResult { get; set; }
    public int BaseArmorClass { get; set; }
    public int TargetArmorClass { get; set; }
    public int AttackBonus { get; set; }
    public int TotalAttackModifier { get; set; }
    public int MultipleAttackPenalty { get; set; }
    public int RangePenalty { get; set; }
    public int CoverAcBonus { get; set; }
    public uint FinalAppliedDamage { get; set; }

    private void RefreshTraits()
    {
        List<string> merged = explicitTraits == null ? new List<string>() : new List<string>(explicitTraits);
        if (sourceInfo?.Traits != null)
        {
            foreach (string trait in sourceInfo.Traits)
            {
                if (!ContainsTrait(merged, trait))
                    merged.Add(trait);
            }
        }
        traits = merged;
    }

    private static bool ContainsTrait(List<string> traits, string candidate)
    {
        foreach (string trait in traits)
        {
            if (string.Equals(trait, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

public static class AttackResultPipeline
{
    public static void ProcessHit(AttackResultContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (context.TargetCreature == null)
            throw new ArgumentException("Attack result context must include a target creature.", nameof(context));

        context.DamageDice ??= new List<Dice>();
        context.FlatDamages ??= new List<DamageValue>();
        context.DamageValues = new List<DamageValue>();

        List<IAttackResultEffect> effects = BuildEffects(context);
        ApplyPhase(effects, AttackResultEffectPhase.BeforeDamageRoll, context);

        if (context.DamageDice.Count > 0)
            OnDamageDealt.Invoke(context.DamageDice[0].damageType);

        context.DamageValues = DamageRoller.RollDamage(context.DamageDice, context.FlatDamages);
        DamageRoller.EvaluateCriticalDamage(context.Degree, context.DamageValues);
        ApplyPhase(effects, AttackResultEffectPhase.AfterCriticalDoubling, context);
        ApplyPhase(effects, AttackResultEffectPhase.BeforeDefenseAdjustments, context);
        DamageRoller.ApplyWeaknessAndResistance(context.DamageValues, context.TargetCreature.weaknesses, context.TargetCreature.resistances);
        int totalDamage = DamageRoller.SumDamage(context.DamageValues);
        context.FinalAppliedDamage = (uint)Mathf.Max(0, totalDamage);
        context.TargetCreature.TakeDamage(context.FinalAppliedDamage);
        ApplyPhase(effects, AttackResultEffectPhase.AfterDamageApplied, context);
    }

    private static List<IAttackResultEffect> BuildEffects(AttackResultContext context)
    {
        List<IAttackResultEffect> effects = new();
        effects.AddRange(AttackTraitEffectResolver.Resolve(context));
        AddProviderEffects(effects, context.AttackerObject, context);
        AddProviderEffects(effects, context.TargetObject, context);
        return effects;
    }

    private static void AddProviderEffects(List<IAttackResultEffect> effects, GameObject owner, AttackResultContext context)
    {
        if (owner == null)
            return;

        foreach (MonoBehaviour component in owner.GetComponents<MonoBehaviour>())
        {
            if (component is not IAttackResultEffectProvider provider)
                continue;

            IEnumerable<IAttackResultEffect> provided = provider.GetAttackResultEffects(context);
            if (provided == null)
                continue;

            foreach (IAttackResultEffect effect in provided)
            {
                if (effect != null)
                    effects.Add(effect);
            }
        }
    }

    private static void ApplyPhase(List<IAttackResultEffect> effects, AttackResultEffectPhase phase, AttackResultContext context)
    {
        foreach (IAttackResultEffect effect in effects)
        {
            if (effect.Phase == phase)
                effect.Apply(context);
        }
    }
}

public sealed class CriticalSpecializationAttackResultEffect : IAttackResultEffect
{
    private readonly string group;
    private readonly Action<AttackResultContext> apply;

    public CriticalSpecializationAttackResultEffect(string group, Action<AttackResultContext> apply)
    {
        this.group = group ?? string.Empty;
        this.apply = apply;
    }

    public AttackResultEffectPhase Phase => AttackResultEffectPhase.AfterDamageApplied;

    public void Apply(AttackResultContext context)
    {
        if (context == null || context.Degree != DegreeOfSuccess.CriticalSuccess || apply == null)
            return;
        if (!string.Equals(context.SourceInfo?.Group, group, StringComparison.OrdinalIgnoreCase))
            return;

        apply(context);
    }
}

internal static class AttackTraitEffectResolver
{
    public static IEnumerable<IAttackResultEffect> Resolve(AttackResultContext context)
    {
        if (context?.Traits == null)
            yield break;

        foreach (string trait in context.Traits)
        {
            if (TryParseTraitDie(trait, "deadly-d", out int deadlySides))
                yield return new DeadlyAttackResultEffect(trait, deadlySides);
            else if (TryParseTraitDie(trait, "fatal-d", out int fatalSides))
            {
                yield return new FatalDamageDieUpgradeEffect(trait, fatalSides);
                yield return new FatalExtraDieEffect(trait, fatalSides);
            }
        }
    }

    private static bool TryParseTraitDie(string trait, string prefix, out int sides)
    {
        sides = 0;
        if (string.IsNullOrWhiteSpace(trait) || !trait.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(trait.Substring(prefix.Length), out sides) && sides > 0;
    }
}

internal sealed class DeadlyAttackResultEffect : IAttackResultEffect
{
    private readonly string trait;
    private readonly int sides;

    public DeadlyAttackResultEffect(string trait, int sides)
    {
        this.trait = trait;
        this.sides = sides;
    }

    public AttackResultEffectPhase Phase => AttackResultEffectPhase.AfterCriticalDoubling;

    public void Apply(AttackResultContext context)
    {
        if (context.Degree != DegreeOfSuccess.CriticalSuccess || context.DamageDice == null || context.DamageDice.Count == 0)
            return;

        AddCriticalTraitDamage(context, trait, sides);
    }

    internal static void AddCriticalTraitDamage(AttackResultContext context, string trait, int sides)
    {
        string damageType = context.DamageDice[0].damageType;
        DamageValue extraDamage = new DamageValue(damageType, UnityEngine.Random.Range(1, sides + 1));
        DamageRoller.AddOrMergeDamage(context.DamageValues, extraDamage);
        CombatLog.GetInstance().Log("  +" + extraDamage.DamageAmount + " " + trait + " critical damage!");
    }
}

internal sealed class FatalDamageDieUpgradeEffect : IAttackResultEffect
{
    private readonly string trait;
    private readonly int sides;

    public FatalDamageDieUpgradeEffect(string trait, int sides)
    {
        this.trait = trait;
        this.sides = sides;
    }

    public AttackResultEffectPhase Phase => AttackResultEffectPhase.BeforeDamageRoll;

    public void Apply(AttackResultContext context)
    {
        if (context.Degree != DegreeOfSuccess.CriticalSuccess || context.DamageDice == null || context.DamageDice.Count == 0)
            return;

        Dice primary = context.DamageDice[0];
        if (primary.sidesPerDie >= sides)
            return;

        context.DamageDice[0] = new Dice(primary.numberOfDice, sides, primary.damageType);
        CombatLog.GetInstance().Log("  " + trait + " upgrades critical damage dice to d" + sides + ".");
    }
}

internal sealed class FatalExtraDieEffect : IAttackResultEffect
{
    private readonly string trait;
    private readonly int sides;

    public FatalExtraDieEffect(string trait, int sides)
    {
        this.trait = trait;
        this.sides = sides;
    }

    public AttackResultEffectPhase Phase => AttackResultEffectPhase.AfterCriticalDoubling;

    public void Apply(AttackResultContext context)
    {
        if (context.Degree != DegreeOfSuccess.CriticalSuccess || context.DamageDice == null || context.DamageDice.Count == 0)
            return;

        DeadlyAttackResultEffect.AddCriticalTraitDamage(context, trait, sides);
    }
}

public class OnStrikeEvent : StaticUnityEvent<OnStrikeEvent, Tuple<Strike, GameObject>>{ }
