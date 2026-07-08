using UnityEngine;
using System;
using System.Collections.Generic;
using Game.Creature;
using GridPublic;

/// <summary>
/// Describes the non-statistical source of an attack, such as its weapon name, group, category, and traits.
/// This keeps attack result effects independent from equipment-only data so later natural or spell attacks can provide the same metadata.
/// </summary>
public class AttackSourceInfo
{
    public static readonly AttackSourceInfo Unspecified = new AttackSourceInfo(string.Empty, string.Empty, string.Empty);

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
            return Unspecified;

        return new AttackSourceInfo(weapon.name, weapon.group, weapon.category, weapon.traits, weapon);
    }
}

/// <summary>
/// Ordered extension points for attack-result effects. The order separates dice changes before rolling, critical-only dice after doubling, defense adjustments, and reactions after damage is applied.
/// </summary>
public enum AttackResultEffectPhase
{
    BeforeDamageRoll = 0,
    AfterCriticalDoubling = 1,
    BeforeDefenseAdjustments = 2,
    AfterDamageApplied = 3
}

/// <summary>
/// Pure C# rule hook that can inspect and mutate an attack result context at a single deterministic phase.
/// </summary>
public interface IAttackResultEffect
{
    AttackResultEffectPhase Phase { get; }
    void Apply(AttackResultContext context);
}

/// <summary>
/// Optional Unity component contract for attacker- or defender-owned rule hooks without adding global registries or Strike-specific dependencies.
/// </summary>
public interface IAttackResultEffectProvider
{
    IEnumerable<IAttackResultEffect> GetAttackResultEffects(AttackResultContext context);
}

/// <summary>
/// Mutable state for a resolved hit as it moves through the attack-result pipeline.
/// GameObjects identify Unity owners for component/provider lookup; CreatureComponents cache gameplay stats and mutable creature state.
/// </summary>
public class AttackResultContext
{
    private List<string> explicitTraits = new();
    private List<string> traits = new();
    private AttackSourceInfo sourceInfo = AttackSourceInfo.Unspecified;

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
            sourceInfo = value ?? AttackSourceInfo.Unspecified;
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
    public DamageRollResolution DamageResolution { get; set; }
    public List<CombatLogDetail> LogDetails { get; } = new();
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

/// <summary>
/// Applies built-in attack traits and discovered effect providers in deterministic phase order for a resolved hit.
/// It currently serves Strikes but is separate from Strike so future spell or ability attacks can reuse the same result flow.
/// </summary>
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

        context.DamageResolution = DamageRoller.StartDamageResolution(context.DamageDice, context.FlatDamages);
        context.DamageValues = context.DamageResolution.DamageValues;
        DamageRoller.ApplyCriticalDamage(context.DamageResolution, context.Degree);
        ApplyPhase(effects, AttackResultEffectPhase.AfterCriticalDoubling, context);
        ApplyPhase(effects, AttackResultEffectPhase.BeforeDefenseAdjustments, context);
        context.DamageResolution.DamageValues = context.DamageValues;
        DamageRoller.ApplyWeaknessAndResistance(context.DamageResolution, context.TargetCreature.weaknesses, context.TargetCreature.resistances);
        DamageRoller.FinalizeDamageResolution(context.DamageResolution);
        context.DamageValues = context.DamageResolution.DamageValues;
        int totalDamage = context.DamageResolution.TotalDamage;
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

/// <summary>
/// Testable critical-specialization adapter that runs caller-supplied behavior only for matching weapon groups on critical hits.
/// Full PF2e group effects can plug in here later without hardcoding group rules in Strike.
/// </summary>
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
        if (!string.Equals(context.SourceInfo.Group, group, StringComparison.OrdinalIgnoreCase))
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
        context.DamageValues = DamageRoller.AddOrMergeDamage(context.DamageValues, extraDamage);
        if (context.DamageResolution != null)
            context.DamageResolution.DamageValues = context.DamageValues;
        context.LogDetails.Add(new CombatLogDetail("Critical Trait", "+" + extraDamage.DamageAmount + " " + trait + " critical damage"));
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
        context.LogDetails.Add(new CombatLogDetail("Critical Trait", trait + " upgrades critical damage dice to d" + sides));
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
