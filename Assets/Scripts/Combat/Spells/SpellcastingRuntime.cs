using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.KayKit;
using Game.Rules.Unity;
using GridPrivate;
using GridPublic;
using UnityEngine;
using AdvanceMultipleAttackPenaltyOp = Game.Rules.Runtime.AdvanceMultipleAttackPenaltyOp;
using CreatureId = Game.Rules.Runtime.CreatureId;
using InvalidMapOpResult = Game.Rules.Runtime.InvalidOpResult<Game.Rules.Runtime.MultipleAttackPenaltyState>;
using MapOpResult = Game.Rules.Runtime.OpResult<Game.Rules.Runtime.MultipleAttackPenaltyState>;
using MultipleAttackPenaltyState = Game.Rules.Runtime.MultipleAttackPenaltyState;
using ResolvedMapOpResult = Game.Rules.Runtime.ResolvedOpResult<Game.Rules.Runtime.MultipleAttackPenaltyState>;
using RuleSource = Game.Rules.Runtime.RuleSource;

#pragma warning disable CS0618 // This file is the intentionally isolated legacy runtime.

namespace Game.Combat.Spells
{
    public sealed class CastSpellResult
    {
        public bool Success;
        public string Message;
        public List<GameObject> Targets { get; } = new();
        public List<D20Result> Rolls { get; } = new();
        public int Amount;
    }

    public sealed class SpellTargetSelection
    {
        public IReadOnlyList<GameObject> Targets { get; }
        public AreaTargetResult Area { get; }

        public SpellTargetSelection(
            IReadOnlyList<GameObject> targets = null,
            AreaTargetResult area = null
        )
        {
            Targets = targets ?? Array.Empty<GameObject>();
            Area = area;
        }

        public static SpellTargetSelection None { get; } = new();

        public static SpellTargetSelection ForTarget(GameObject target) =>
            new(target == null ? Array.Empty<GameObject>() : new[] { target });

        public static SpellTargetSelection ForTargets(IReadOnlyList<GameObject> targets) =>
            new(targets);

        public static SpellTargetSelection ForArea(AreaTargetResult area) => new(null, area);
    }

    public sealed class SpellCastContext
    {
        public GameObject Caster { get; }
        public PreparedSpell Spell { get; }
        public uint ActionCost { get; }
        public bool SpendActions { get; }
        public ISpellDefinition Definition { get; }

        public SpellCastContext(
            GameObject caster,
            PreparedSpell spell,
            uint actionCost,
            bool spendActions,
            ISpellDefinition definition
        )
        {
            Caster = caster;
            Spell = spell;
            ActionCost = actionCost;
            SpendActions = spendActions;
            Definition = definition;
        }

        public ActionController ActionController =>
            Caster != null ? Caster.GetComponent<ActionController>() : null;
        public CreatureComponent CasterCreature =>
            Caster != null ? Caster.GetComponent<CreatureComponent>() : null;
        public SpellcastingState Spellcasting => CasterCreature?.Prepared?.Spellcasting;

        public CastSpellResult Cast(SpellTargetSelection selection) =>
            SpellcastingRuntime.Cast(this, selection);
    }

    /// <summary>
    /// Executes spells through the deprecated Unity-owned non-Light spellcasting pipeline.
    /// </summary>
    /// <remarks>
    /// New and migrated spells should dispatch <see cref="Game.Rules.Runtime.CastSpellActionOp"/>.
    /// </remarks>
    [Obsolete(
        "Dispatch CastSpellActionOp through the rules runtime for migrated spells; SpellcastingRuntime is retained only for legacy non-Light spells.",
        false
    )]
    public static class SpellcastingRuntime
    {
        public static StrikeTargetRequest FixedRangeTarget(int rangeFeet)
        {
            return new StrikeTargetRequest
            {
                IsRanged = true,
                FixedRangeFeet = rangeFeet,
                RequiresLineOfEffect = true,
            };
        }

        public static CastSpellResult Cast(
            GameObject caster,
            PreparedSpell spell,
            uint actionCost,
            IReadOnlyList<GameObject> targets = null,
            AreaTargetResult area = null,
            bool spendActions = true
        )
        {
#pragma warning disable CS0618 // This deprecated runtime intentionally resolves the legacy registry.
            bool implemented = SpellRegistry.TryGet(spell?.Slug, out ISpellDefinition definition);
#pragma warning restore CS0618
            if (!implemented)
                return Fail(
                    new CastSpellResult(),
                    spell == null ? "Spell is not prepared." : spell.Name + " is not implemented.",
                    caster != null ? caster.GetComponent<ActionController>() : null
                );

            SpellCastContext context = new(caster, spell, actionCost, spendActions, definition);
            return Cast(context, new SpellTargetSelection(targets, area));
        }

        public static CastSpellResult Cast(SpellCastContext context, SpellTargetSelection selection)
        {
            CastSpellResult result = new();
            CreatureComponent creature = context.CasterCreature;
            ActionController controller = context.ActionController;
            SpellcastingState state = context.Spellcasting;
            if (
                context.Caster == null
                || creature == null
                || state == null
                || context.Spell == null
            )
                return Fail(result, "Caster is not ready to cast spells.", controller);
            if (
                context.ActionCost > 0
                && controller != null
                && context.SpendActions
                && controller.ActionPoints < context.ActionCost
            )
                return Fail(result, "Not enough actions.", controller);
            if (!state.CanCast(context.Spell))
                return Fail(result, context.Spell.Name + " has no remaining slot.", controller);

            if (!context.Definition.Cast(context, selection ?? SpellTargetSelection.None, result))
                return Fail(result, "Spell target is invalid.", controller);
            if (!state.Spend(context.Spell))
                return Fail(result, context.Spell.Name + " has no remaining slot.", controller);
            if (controller != null && context.SpendActions)
            {
                try
                {
                    controller.SpendActions(context.ActionCost);
                    if (context.Definition.AppliesMultipleAttackPenalty(context))
                    {
                        if (
                            controller.TryGetCombatRules(
                                out UnityCombatRulesBridge bridge,
                                out CreatureId actor
                            )
                        )
                        {
                            MapOpResult map = bridge.Dispatch(
                                new AdvanceMultipleAttackPenaltyOp(actor)
                            );
                            if (map is InvalidMapOpResult invalid)
                                throw new InvalidOperationException(invalid.Reason);
                            if (map is not ResolvedMapOpResult)
                                throw new InvalidOperationException(
                                    "MAP advancement did not resolve."
                                );
                        }
                    }
                }
                finally
                {
                    controller.IsTakingAction = false;
                }
            }
            result.Success = true;
            if (!creature.IsDefeated)
                context
                    .Caster.GetComponent<CreaturePresentation>()
                    ?.PlayAttack(AnimationStyle.Magic);
            CombatLogInterface log = UnityEngine.Object.FindFirstObjectByType<CombatLogInterface>();
            log?.Log("- " + context.Caster.name + " casts " + context.Spell.Name + ".");
            return result;
        }

        public static int SpellAttackModifier(CreatureComponent caster)
        {
            if (caster == null)
                return 0;
            const int trainedProficiency = 2;
            return caster.level + trainedProficiency + caster.wisMod;
        }

        public static bool IsFriendly(GameObject caster, GameObject target)
        {
            if (caster == null || target == null)
                return false;
            if (caster == target)
                return true;
            Team casterTeam = caster.GetComponent<Team>();
            Team targetTeam = target.GetComponent<Team>();
            if (
                casterTeam == null
                || targetTeam == null
                || string.IsNullOrWhiteSpace(casterTeam.Name)
                || string.IsNullOrWhiteSpace(targetTeam.Name)
            )
                return true;
            TeamRules rules = TeamRules.GetInstance();
            return rules.Contains(casterTeam.Name)
                && rules.Contains(targetTeam.Name)
                && rules.IsFriendly(casterTeam.Name, targetTeam.Name);
        }

        public static int DistanceFeet(GameObject left, GameObject right)
        {
            if (left == null || right == null)
                return int.MaxValue;
            return StrikeTargeting.MeasureGridDistanceFeet(
                Vector3Int.RoundToInt(left.transform.position),
                Vector3Int.RoundToInt(right.transform.position)
            );
        }

        public static IReadOnlyList<GameObject> FriendlyCreaturesInEmanation(
            GameObject caster,
            int rangeFeet
        )
        {
            if (caster == null)
                return Array.Empty<GameObject>();
            List<GameObject> targets = new() { caster };
            Vector3Int start = Vector3Int.RoundToInt(caster.transform.position);
            foreach (
                CreatureComponent creature in UnityEngine.Object.FindObjectsByType<CreatureComponent>(
                    FindObjectsSortMode.None
                )
            )
            {
                if (creature == null || creature.gameObject == caster)
                    continue;
                int distance = StrikeTargeting.MeasureGridDistanceFeet(
                    start,
                    Vector3Int.RoundToInt(creature.transform.position)
                );
                if (distance <= rangeFeet && IsFriendly(caster, creature.gameObject))
                    targets.Add(creature.gameObject);
            }
            return targets;
        }

        public static void ApplyBasicFortitudeDamage(
            GameObject caster,
            GameObject target,
            Dice dice,
            CastSpellResult result,
            bool applyDeafenedOnCriticalFailure,
            RuleSource source
        )
        {
            DamageRollResolution damage = DamageRoller.StartDamageResolution(
                new List<Dice> { dice },
                new List<DamageValue>()
            );
            DamageRoller.FinalizeDamageResolution(damage);
            ApplyBasicFortitudeDamage(
                caster,
                target,
                new DamageValue(dice.damageType, damage.TotalDamage),
                result,
                applyDeafenedOnCriticalFailure,
                source
            );
        }

        public static void ApplyBasicFortitudeDamage(
            GameObject caster,
            GameObject target,
            DamageValue damage,
            CastSpellResult result,
            bool applyDeafenedOnCriticalFailure,
            RuleSource source
        )
        {
            CreatureComponent casterCreature = caster.GetComponent<CreatureComponent>();
            CreatureComponent targetCreature = target.GetComponent<CreatureComponent>();
            int dc = casterCreature.Prepared.Spellcasting.SpellDc;
            int saveModifier = targetCreature.ResolveFortitudeSave().Total;
            D20Result save = D20.Roll(saveModifier, dc);
            result.Rolls.Add(save);
            int amount = BasicSaveDamage(damage.DamageAmount, save.degree);
            if (amount > 0)
                targetCreature.ApplyFinalDamage(amount, source);
            if (applyDeafenedOnCriticalFailure && save.degree == DegreeOfSuccess.CriticalFail)
                (target.GetComponent<Conditions>() ?? target.AddComponent<Conditions>()).Add(
                    "Deafened",
                    new ConditionSource()
                );
            result.Targets.Add(target);
            result.Amount += amount;
        }

        public static bool IsUndead(CreatureComponent creature)
        {
            return creature.traits != null
                && creature.traits.Any(trait =>
                    string.Equals(trait, "undead", StringComparison.OrdinalIgnoreCase)
                );
        }

        private static int BasicSaveDamage(int amount, DegreeOfSuccess degree)
        {
            return degree switch
            {
                DegreeOfSuccess.CriticalSuccess => 0,
                DegreeOfSuccess.Success => Mathf.FloorToInt(amount / 2.0f),
                DegreeOfSuccess.CriticalFail => amount * 2,
                _ => amount,
            };
        }

        public static CastSpellResult Fail(
            CastSpellResult result,
            string message,
            ActionController controller
        )
        {
            result.Success = false;
            result.Message = message;
            if (controller != null)
                controller.IsTakingAction = false;
            return result;
        }
    }
}

#pragma warning restore CS0618
