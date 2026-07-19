using System;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules;
using GridPublic;
using UnityEngine;

/// <summary>
/// Describes the non-statistical source of an attack, such as its weapon name, group, category, and traits.
/// This keeps strike resolution independent from equipment-only data so natural, spell, and item attacks can share provenance.
/// </summary>
public class AttackSourceInfo
{
    public static readonly AttackSourceInfo Unspecified = new AttackSourceInfo(
        string.Empty,
        string.Empty,
        string.Empty
    );

    public string Name { get; }
    public string Group { get; }
    public string Category { get; }
    public IReadOnlyList<string> Traits { get; }
    public EquipmentWeapon EquipmentWeapon { get; }

    public AttackSourceInfo(
        string name,
        string group,
        string category,
        IEnumerable<string> traits = null,
        EquipmentWeapon equipmentWeapon = null
    )
    {
        Name = name ?? string.Empty;
        Group = group ?? string.Empty;
        Category = category ?? string.Empty;
        Traits = traits == null ? new List<string>() : new List<string>(traits);
        EquipmentWeapon = equipmentWeapon;
    }

    public AttackSourceInfo(AttackSourceInfo sourceInfo)
        : this(
            sourceInfo?.Name,
            sourceInfo?.Group,
            sourceInfo?.Category,
            sourceInfo?.Traits,
            sourceInfo?.EquipmentWeapon
        ) { }

    public static AttackSourceInfo FromWeapon(EquipmentWeapon weapon)
    {
        if (weapon == null)
            return Unspecified;

        return new AttackSourceInfo(
            weapon.name,
            weapon.group,
            weapon.category,
            weapon.traits,
            weapon
        );
    }
}

/// <summary>
/// Reusable Strike data supplied by a weapon, unarmed attack, or later Strike-like source.
/// Runtime resolution clones this profile so per-Strike adjustments never mutate action-owned data.
/// </summary>
public class StrikeProfile
{
    private AttackSourceInfo sourceInfo = AttackSourceInfo.Unspecified;

    public List<Dice> DamageDice { get; set; }
    public List<DamageValue> FlatDamages { get; set; }
    public List<string> Traits { get; set; } = new();
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

    public StrikeProfile(List<Dice> damageDice, List<DamageValue> flatDamages)
    {
        DamageDice = CloneDice(damageDice);
        FlatDamages =
            flatDamages == null ? new List<DamageValue>() : new List<DamageValue>(flatDamages);
    }

    public StrikeProfile(StrikeProfile profile)
    {
        if (profile == null)
        {
            DamageDice = new List<Dice>();
            FlatDamages = new List<DamageValue>();
            SourceInfo = AttackSourceInfo.Unspecified;
            return;
        }

        DamageDice = CloneDice(profile.DamageDice);
        FlatDamages = new List<DamageValue>(profile.FlatDamages ?? new List<DamageValue>());
        Traits = new List<string>(profile.Traits ?? new List<string>());
        SourceInfo = new AttackSourceInfo(profile.SourceInfo);
        AttackModifierOverride = profile.AttackModifierOverride;
        ItemSlug = profile.ItemSlug;
        WeaponCategory = profile.WeaponCategory;
        IsRangedAttack = profile.IsRangedAttack;
        ReachFeet = profile.ReachFeet;
    }

    public float GetAverageDamage()
    {
        float average = 0;
        foreach (Dice dice in DamageDice ?? new List<Dice>())
            average += dice.numberOfDice * ((float)dice.sidesPerDie + 1) / 2;
        foreach (DamageValue damageValue in FlatDamages ?? new List<DamageValue>())
            average += damageValue.DamageAmount;
        return average;
    }

    public override string ToString()
    {
        string traits = string.Join(" ", Traits ?? new List<string>());
        return "Strike Profile: "
            + (DamageDice?.Count ?? 0)
            + " damage rolls, "
            + (FlatDamages?.Count ?? 0)
            + " flat damages, Traits: "
            + traits;
    }

    internal static List<Dice> CloneDice(IEnumerable<Dice> diceList)
    {
        List<Dice> clone = new();
        if (diceList == null)
            return clone;

        foreach (Dice dice in diceList)
            clone.Add(new Dice(dice.numberOfDice, dice.sidesPerDie, dice.damageType));
        return clone;
    }
}

/// <summary>
/// Immutable request data for resolving one selected Strike.
/// Target selection, ammo checks, action cost, and MAP increment remain owned by the invoking action.
/// </summary>
public class StrikeResolutionRequest
{
    public GameObject Attacker { get; set; }
    public GameObject Target { get; set; }
    public StrikeProfile Profile { get; set; }
    public StrikeTargetResult TargetingResult { get; set; }
}

/// <summary>
/// Ordered extension points for Strike resolution. Phases are intentionally broad so rule sources can plug in without growing Strike action classes.
/// </summary>
public enum StrikeAdjustmentPhase
{
    PrepareProfile = 0,
    BeforeAttackRoll = 100,
    BeforeArmorClassResolution = 200,
    ResolveAttackRoll = 300,
    AfterAttackRoll = 400,
    BeforeDamageRoll = 500,
    RollDamage = 600,
    AfterCriticalDoubling = 700,
    BeforeDefenseAdjustments = 800,
    ApplyDefenseAndDamage = 900,
    AfterDamageApplied = 1000,
}

/// <summary>
/// Mutable state for a Strike as it moves through resolution.
/// </summary>
public class StrikeResolutionContext
{
    private List<string> explicitTraits = new();
    private List<string> traits = new();
    private AttackSourceInfo sourceInfo = AttackSourceInfo.Unspecified;

    public GameObject AttackerObject { get; set; }
    public GameObject TargetObject { get; set; }
    public CreatureComponent AttackerCreature { get; set; }
    public CreatureComponent TargetCreature { get; set; }
    public StrikeProfile Profile { get; set; }
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
    public List<string> ItemOptions { get; set; } = new();
    public List<Dice> DamageDice { get; set; } = new();
    public List<DamageValue> FlatDamages { get; set; } = new();
    public List<DamageValue> DamageValues { get; set; } = new();
    public DamageRollResolution DamageResolution { get; set; }
    public List<CombatLogDetail> LogDetails { get; } = new();
    public List<Pf2eModifier> AttackModifiers { get; } = new();
    public List<Pf2eModifier> ArmorClassModifiers { get; } = new();
    public StrikeTargetResult TargetingResult { get; set; }
    public Pf2eModifierResolution AttackResolution { get; set; }
    public Pf2eModifierResolution BaseArmorClassResolution { get; set; }
    public Pf2eModifierResolution TargetArmorClassResolution { get; set; }
    public D20Result D20Result { get; set; }
    public DegreeOfSuccess Degree { get; set; }
    public int BaseArmorClass { get; set; }
    public int TargetArmorClass { get; set; }
    public int AttackBonus { get; set; }
    public int TotalAttackModifier { get; set; }
    public int MultipleAttackPenalty { get; set; }
    public int RangePenalty { get; set; }
    public int CoverAcBonus { get; set; }
    public bool FlankedOffGuard { get; set; }
    public bool IsHit { get; set; }
    public uint FinalAppliedDamage { get; set; }

    public static StrikeResolutionContext FromRequest(StrikeResolutionRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.Attacker == null)
            throw new ArgumentException("Strike resolution requires an attacker.", nameof(request));
        if (request.Target == null)
            throw new ArgumentException("Strike resolution requires a target.", nameof(request));
        if (request.Profile == null)
            throw new ArgumentException(
                "Strike resolution requires a Strike profile.",
                nameof(request)
            );

        StrikeProfile profile = new(request.Profile);
        return new StrikeResolutionContext
        {
            AttackerObject = request.Attacker,
            TargetObject = request.Target,
            AttackerCreature = request.Attacker.GetComponent<CreatureComponent>(),
            TargetCreature = request.Target.GetComponent<CreatureComponent>(),
            Profile = profile,
            SourceInfo = profile.SourceInfo,
            Traits = profile.Traits,
            DamageDice = StrikeProfile.CloneDice(profile.DamageDice),
            FlatDamages = new List<DamageValue>(profile.FlatDamages ?? new List<DamageValue>()),
            DamageValues = new List<DamageValue>(),
            TargetingResult = request.TargetingResult,
        };
    }

    private void RefreshTraits()
    {
        List<string> merged =
            explicitTraits == null ? new List<string>() : new List<string>(explicitTraits);
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
            if (string.Equals(trait, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

/// <summary>
/// Final outcome of a resolved Strike.
/// </summary>
public class StrikeResolutionResult
{
    public StrikeResolutionContext Context { get; }
    public bool Hit => Context?.IsHit ?? false;
    public bool CriticalHit => Context?.Degree == DegreeOfSuccess.CriticalSuccess;
    public uint FinalAppliedDamage => Context?.FinalAppliedDamage ?? 0;
    public DamageRollResolution DamageResolution => Context?.DamageResolution;
    public IReadOnlyList<CombatLogDetail> LogDetails =>
        Context != null ? Context.LogDetails : Array.Empty<CombatLogDetail>();

    public StrikeResolutionResult(StrikeResolutionContext context)
    {
        Context = context;
    }
}

/// <summary>
/// A deterministic rule hook that can inspect and mutate Strike resolution state at one phase.
/// </summary>
public interface IStrikeAdjustment
{
    StrikeAdjustmentPhase Phase { get; }
    int Order { get; }
    string Source { get; }
    void Apply(StrikeResolutionContext context);
}

/// <summary>
/// Optional Unity component contract for attacker- or defender-owned Strike rule hooks.
/// </summary>
public interface IStrikeAdjustmentProvider
{
    IEnumerable<IStrikeAdjustment> GetStrikeAdjustments(StrikeResolutionContext context);
}

public class OnStrikePreparedEvent
    : StaticUnityEvent<OnStrikePreparedEvent, StrikeResolutionContext> { }
