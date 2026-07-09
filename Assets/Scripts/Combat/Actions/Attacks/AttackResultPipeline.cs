using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Rules;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules;

/// <summary>
/// Resolves Strikes through deterministic phases. The invoking action owns target selection, resource checks, action cost, and MAP increment.
/// </summary>
public static class StrikeResolutionPipeline
{
    public static StrikeResolutionResult Resolve(StrikeResolutionRequest request)
    {
        StrikeResolutionContext context = StrikeResolutionContext.FromRequest(request);
        if (context.AttackerCreature == null)
            throw new ArgumentException("Strike resolution requires an attacker CreatureComponent.", nameof(request));
        if (context.TargetCreature == null)
            throw new ArgumentException("Strike resolution requires a target CreatureComponent.", nameof(request));

        List<IStrikeAdjustment> adjustments = BuildAdjustments(context);

        ApplyPhase(adjustments, StrikeAdjustmentPhase.PrepareProfile, context);
        OnStrikePreparedEvent.Invoke(context);

        ApplyPhase(adjustments, StrikeAdjustmentPhase.BeforeAttackRoll, context);
        ApplyPhase(adjustments, StrikeAdjustmentPhase.BeforeArmorClassResolution, context);
        ApplyPhase(adjustments, StrikeAdjustmentPhase.ResolveAttackRoll, context);
        ApplyPhase(adjustments, StrikeAdjustmentPhase.AfterAttackRoll, context);

        if (context.IsHit)
        {
            ApplyPhase(adjustments, StrikeAdjustmentPhase.BeforeDamageRoll, context);
            ApplyPhase(adjustments, StrikeAdjustmentPhase.RollDamage, context);
            ApplyPhase(adjustments, StrikeAdjustmentPhase.AfterCriticalDoubling, context);
            ApplyPhase(adjustments, StrikeAdjustmentPhase.BeforeDefenseAdjustments, context);
            ApplyPhase(adjustments, StrikeAdjustmentPhase.ApplyDefenseAndDamage, context);
            ApplyPhase(adjustments, StrikeAdjustmentPhase.AfterDamageApplied, context);
        }

        return new StrikeResolutionResult(context);
    }

    private static List<IStrikeAdjustment> BuildAdjustments(StrikeResolutionContext context)
    {
        List<IStrikeAdjustment> adjustments = new()
        {
            new PreparedCharacterStrikeAdjustment(),
            new MultipleAttackAndRangePenaltyAdjustment(),
            new ArmorClassContextAdjustment(),
            new ResolveAttackRollAdjustment(),
            new AttackOutcomeLogAdjustment(),
            new RollDamageAdjustment(),
            new CriticalDoublingAdjustment(),
            new ApplyDefenseAndDamageAdjustment()
        };

        adjustments.AddRange(AttackTraitStrikeAdjustmentResolver.Resolve(context));
        AddProviderAdjustments(adjustments, context.AttackerObject, context);
        AddProviderAdjustments(adjustments, context.TargetObject, context);
        return adjustments
            .OrderBy(adjustment => adjustment.Phase)
            .ThenBy(adjustment => adjustment.Order)
            .ThenBy(adjustment => adjustment.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddProviderAdjustments(List<IStrikeAdjustment> adjustments, GameObject owner, StrikeResolutionContext context)
    {
        if (owner == null)
            return;

        foreach (MonoBehaviour component in owner.GetComponents<MonoBehaviour>())
        {
            if (component is not IStrikeAdjustmentProvider provider)
                continue;

            IEnumerable<IStrikeAdjustment> provided = provider.GetStrikeAdjustments(context);
            if (provided == null)
                continue;

            foreach (IStrikeAdjustment adjustment in provided)
            {
                if (adjustment != null)
                    adjustments.Add(adjustment);
            }
        }
    }

    private static void ApplyPhase(List<IStrikeAdjustment> adjustments, StrikeAdjustmentPhase phase, StrikeResolutionContext context)
    {
        foreach (IStrikeAdjustment adjustment in adjustments)
            if (adjustment.Phase == phase)
                adjustment.Apply(context);
    }
}

public abstract class StrikeAdjustmentBase : IStrikeAdjustment
{
    protected StrikeAdjustmentBase(StrikeAdjustmentPhase phase, int order, string source)
    {
        Phase = phase;
        Order = order;
        Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
    }

    public StrikeAdjustmentPhase Phase { get; }
    public int Order { get; }
    public string Source { get; }
    public abstract void Apply(StrikeResolutionContext context);
}

internal sealed class PreparedCharacterStrikeAdjustment : StrikeAdjustmentBase
{
    public PreparedCharacterStrikeAdjustment() : base(StrikeAdjustmentPhase.PrepareProfile, 0, "Prepared character rules") { }

    public override void Apply(StrikeResolutionContext context)
    {
        Pf2eRulesEngine.ApplyPreparedStrikeAdjustments(context);
    }
}

internal sealed class MultipleAttackAndRangePenaltyAdjustment : StrikeAdjustmentBase
{
    public MultipleAttackAndRangePenaltyAdjustment() : base(StrikeAdjustmentPhase.BeforeAttackRoll, 0, "Strike penalties") { }

    public override void Apply(StrikeResolutionContext context)
    {
        uint strikePenaltyCount = context.AttackerObject.GetComponent<ActionController>()?.StrikePenalty ?? 0;
        context.MultipleAttackPenalty = CalculateMultipleAttackPenalty(context, strikePenaltyCount);
        context.RangePenalty = context.TargetingResult?.RangePenalty ?? 0;

        if (context.MultipleAttackPenalty != 0)
            context.AttackModifiers.Add(new Pf2eModifier(-context.MultipleAttackPenalty, Pf2eModifierType.Untyped, "Multiple attack penalty", Pf2eStatistic.AttackRoll));
        if (context.RangePenalty != 0)
            context.AttackModifiers.Add(new Pf2eModifier(context.RangePenalty, Pf2eModifierType.Untyped, "Range penalty", Pf2eStatistic.AttackRoll));
    }

    private static int CalculateMultipleAttackPenalty(StrikeResolutionContext context, uint strikePenaltyCount)
    {
        if (strikePenaltyCount == 0)
            return 0;

        bool agile = context.Traits.Any(trait => string.Equals(trait, "agile", StringComparison.OrdinalIgnoreCase));
        if (strikePenaltyCount == 1)
            return agile ? 4 : 5;
        return agile ? 8 : 10;
    }
}

internal sealed class ArmorClassContextAdjustment : StrikeAdjustmentBase
{
    public ArmorClassContextAdjustment() : base(StrikeAdjustmentPhase.BeforeArmorClassResolution, 0, "Strike target defenses") { }

    public override void Apply(StrikeResolutionContext context)
    {
        context.CoverAcBonus = context.TargetingResult?.CoverAcBonus ?? 0;
        context.FlankedOffGuard = FlankingRule.GrantsOffGuardToMeleeAttack(context.AttackerObject, context.TargetObject, context.Profile);

        if (context.CoverAcBonus != 0)
            context.ArmorClassModifiers.Add(new Pf2eModifier(context.CoverAcBonus, Pf2eModifierType.Circumstance, "Cover", Pf2eStatistic.ArmorClass));
        if (context.FlankedOffGuard)
            context.ArmorClassModifiers.Add(new Pf2eModifier(-2, Pf2eModifierType.Circumstance, "Off-Guard", Pf2eStatistic.ArmorClass));
    }
}

internal sealed class ResolveAttackRollAdjustment : StrikeAdjustmentBase
{
    public ResolveAttackRollAdjustment() : base(StrikeAdjustmentPhase.ResolveAttackRoll, 0, "Attack roll") { }

    public override void Apply(StrikeResolutionContext context)
    {
        context.AttackBonus = context.Profile.AttackModifierOverride ?? context.AttackerCreature.attackBonus;
        context.AttackResolution = context.AttackerCreature.ResolveAttackRoll(context.Profile.AttackModifierOverride, context.AttackModifiers);
        context.BaseArmorClassResolution = context.TargetCreature.ResolveArmorClass();
        context.TargetArmorClassResolution = context.TargetCreature.ResolveArmorClass(context.ArmorClassModifiers);
        context.BaseArmorClass = context.BaseArmorClassResolution.Total;
        context.TargetArmorClass = context.TargetArmorClassResolution.Total;
        context.TotalAttackModifier = context.AttackResolution.Total;
        context.D20Result = D20.Roll(context.TotalAttackModifier, context.TargetArmorClass);
        context.Degree = context.D20Result.degree;
        context.IsHit = context.Degree == DegreeOfSuccess.Success || context.Degree == DegreeOfSuccess.CriticalSuccess;
    }
}

internal sealed class AttackOutcomeLogAdjustment : StrikeAdjustmentBase
{
    public AttackOutcomeLogAdjustment() : base(StrikeAdjustmentPhase.AfterAttackRoll, 0, "Attack outcome log") { }

    public override void Apply(StrikeResolutionContext context)
    {
        string log = "Attack:\n  AC: " + context.TargetArmorClass;
        if (context.CoverAcBonus != 0)
            log += " (" + context.BaseArmorClass + " + " + context.CoverAcBonus + " cover)";
        log += "\n  Attack Roll: " + context.D20Result.total
            + " (" + context.D20Result.roll + " + " + context.AttackBonus + " - " + context.MultipleAttackPenalty;
        if (context.RangePenalty != 0)
            log += " " + context.RangePenalty;
        log += ")\n  Result: " + context.Degree;
        log += "\n  Attack Modifiers: " + FormatResolution(context.AttackResolution);
        log += "\n  AC Modifiers: " + FormatResolution(context.TargetArmorClassResolution);

        if (context.IsHit)
        {
            CombatLog.GetInstance().Log(log);
        }
        else
        {
            OnAttackMiss.Invoke(context.AttackerObject);
            log += "\nAttack Missed!";
            CombatLog.GetInstance().Log(log);
        }
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
}

internal sealed class RollDamageAdjustment : StrikeAdjustmentBase
{
    public RollDamageAdjustment() : base(StrikeAdjustmentPhase.RollDamage, 0, "Damage roll") { }

    public override void Apply(StrikeResolutionContext context)
    {
        if (context.DamageDice.Count > 0)
            OnDamageDealt.Invoke(context.DamageDice[0].damageType);

        context.DamageValues = DamageRoller.RollDamage(context.DamageDice, context.FlatDamages);
    }
}

internal sealed class CriticalDoublingAdjustment : StrikeAdjustmentBase
{
    public CriticalDoublingAdjustment() : base(StrikeAdjustmentPhase.AfterCriticalDoubling, 0, "Critical damage") { }

    public override void Apply(StrikeResolutionContext context)
    {
        DamageRoller.EvaluateCriticalDamage(context.Degree, context.DamageValues);
    }
}

internal sealed class ApplyDefenseAndDamageAdjustment : StrikeAdjustmentBase
{
    public ApplyDefenseAndDamageAdjustment() : base(StrikeAdjustmentPhase.ApplyDefenseAndDamage, 0, "Defense and damage") { }

    public override void Apply(StrikeResolutionContext context)
    {
        DamageRoller.ApplyWeaknessAndResistance(context.DamageValues, context.TargetCreature.weaknesses, context.TargetCreature.resistances);
        int totalDamage = DamageRoller.SumDamage(context.DamageValues);
        context.FinalAppliedDamage = (uint)Mathf.Max(0, totalDamage);
        context.TargetCreature.TakeDamage(context.FinalAppliedDamage);
    }
}

/// <summary>
/// Testable critical-specialization adapter that runs caller-supplied behavior only for matching weapon groups on critical hits.
/// </summary>
public sealed class CriticalSpecializationStrikeAdjustment : StrikeAdjustmentBase
{
    private readonly string group;
    private readonly Action<StrikeResolutionContext> apply;

    public CriticalSpecializationStrikeAdjustment(string group, Action<StrikeResolutionContext> apply)
        : base(StrikeAdjustmentPhase.AfterDamageApplied, 0, "Critical specialization")
    {
        this.group = group ?? string.Empty;
        this.apply = apply;
    }

    public override void Apply(StrikeResolutionContext context)
    {
        if (context == null || context.Degree != DegreeOfSuccess.CriticalSuccess || apply == null)
            return;
        if (!string.Equals(context.SourceInfo.Group, group, StringComparison.OrdinalIgnoreCase))
            return;

        apply(context);
    }
}

internal static class AttackTraitStrikeAdjustmentResolver
{
    public static IEnumerable<IStrikeAdjustment> Resolve(StrikeResolutionContext context)
    {
        if (context?.Traits == null)
            yield break;

        foreach (string trait in context.Traits)
        {
            if (TryParseTraitDie(trait, "deadly-d", out int deadlySides))
                yield return new DeadlyStrikeAdjustment(trait, deadlySides);
            else if (TryParseTraitDie(trait, "fatal-d", out int fatalSides))
            {
                yield return new FatalDamageDieUpgradeStrikeAdjustment(trait, fatalSides);
                yield return new FatalExtraDieStrikeAdjustment(trait, fatalSides);
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

internal sealed class DeadlyStrikeAdjustment : StrikeAdjustmentBase
{
    private readonly string trait;
    private readonly int sides;

    public DeadlyStrikeAdjustment(string trait, int sides)
        : base(StrikeAdjustmentPhase.AfterCriticalDoubling, 100, trait)
    {
        this.trait = trait;
        this.sides = sides;
    }

    public override void Apply(StrikeResolutionContext context)
    {
        if (context.Degree != DegreeOfSuccess.CriticalSuccess || context.DamageDice == null || context.DamageDice.Count == 0)
            return;

        AddCriticalTraitDamage(context, trait, sides);
    }

    internal static void AddCriticalTraitDamage(StrikeResolutionContext context, string trait, int sides)
    {
        string damageType = context.DamageDice[0].damageType;
        DamageValue extraDamage = new DamageValue(damageType, UnityEngine.Random.Range(1, sides + 1));
        context.DamageValues = DamageRoller.AddOrMergeDamage(context.DamageValues, extraDamage);
        CombatLog.GetInstance().Log("  +" + extraDamage.DamageAmount + " " + trait + " critical damage!");
    }
}

internal sealed class FatalDamageDieUpgradeStrikeAdjustment : StrikeAdjustmentBase
{
    private readonly string trait;
    private readonly int sides;

    public FatalDamageDieUpgradeStrikeAdjustment(string trait, int sides)
        : base(StrikeAdjustmentPhase.BeforeDamageRoll, 0, trait)
    {
        this.trait = trait;
        this.sides = sides;
    }

    public override void Apply(StrikeResolutionContext context)
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

internal sealed class FatalExtraDieStrikeAdjustment : StrikeAdjustmentBase
{
    private readonly string trait;
    private readonly int sides;

    public FatalExtraDieStrikeAdjustment(string trait, int sides)
        : base(StrikeAdjustmentPhase.AfterCriticalDoubling, 100, trait)
    {
        this.trait = trait;
        this.sides = sides;
    }

    public override void Apply(StrikeResolutionContext context)
    {
        if (context.Degree != DegreeOfSuccess.CriticalSuccess || context.DamageDice == null || context.DamageDice.Count == 0)
            return;

        DeadlyStrikeAdjustment.AddCriticalTraitDamage(context, trait, sides);
    }
}
