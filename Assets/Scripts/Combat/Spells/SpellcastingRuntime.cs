using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Spells;
using GridPrivate;
using GridPublic;
using UnityEngine;
using AdvanceMultipleAttackPenaltyOp = Game.Rules.Runtime.AdvanceMultipleAttackPenaltyOp;
using CreatureId = Game.Rules.Runtime.CreatureId;
using DegreeOfSuccess = Game.Creature.DegreeOfSuccess;
using InvalidMapOpResult = Game.Rules.Runtime.InvalidOpResult<Game.Rules.Runtime.MultipleAttackPenaltyState>;
using MapOpResult = Game.Rules.Runtime.OpResult<Game.Rules.Runtime.MultipleAttackPenaltyState>;
using MultipleAttackPenaltyState = Game.Rules.Runtime.MultipleAttackPenaltyState;
using ResolvedMapOpResult = Game.Rules.Runtime.ResolvedOpResult<Game.Rules.Runtime.MultipleAttackPenaltyState>;
using RuleSource = Game.Rules.Runtime.RuleSource;

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
            ActionController controller = caster?.GetComponent<ActionController>();
            if (
                controller != null
                && controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
                return CastEncounter(
                    bridge,
                    actor,
                    controller,
                    spell,
                    actionCost,
                    new SpellTargetSelection(targets, area),
                    spendActions,
                    CreateInvocationId("programmatic-cast")
                );
            if (!SpellRegistry.TryGet(spell?.Slug, out ISpellDefinition definition))
                return Fail(
                    new CastSpellResult(),
                    spell == null ? "Spell is not prepared." : spell.Name + " is not implemented.",
                    controller
                );

            SpellCastContext context = new(caster, spell, actionCost, spendActions, definition);
            return Cast(context, new SpellTargetSelection(targets, area));
        }

        /// <summary>
        /// Attempts one encounter-authoritative cast using a caller-owned identity that can be
        /// supplied again after an uncertain post-commit failure.
        /// </summary>
        /// <param name="invocationId">The stable identity shared by every exact retry.</param>
        /// <param name="caster">The rules-enrolled Unity caster.</param>
        /// <param name="spell">The exact prepared spell and rank.</param>
        /// <param name="actionCost">The definition-owned action variant.</param>
        /// <param name="targets">Player-selected creatures for a targeted spell.</param>
        /// <param name="area">The authoritative placement and affected set for an area spell.</param>
        /// <returns>The structural cast result projected for existing Unity callers.</returns>
        public static CastSpellResult CastEncounterAttempt(
            ActionInvocationId invocationId,
            GameObject caster,
            PreparedSpell spell,
            uint actionCost,
            IReadOnlyList<GameObject> targets = null,
            AreaTargetResult area = null
        )
        {
            if (invocationId.IsEmpty)
                throw new ArgumentException(
                    "An encounter cast invocation identity is required.",
                    nameof(invocationId)
                );
            ActionController controller = caster?.GetComponent<ActionController>();
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
                return Fail(
                    new CastSpellResult(),
                    "Encounter spellcasting requires active combat rules authority.",
                    controller
                );
            return CastEncounter(
                bridge,
                actor,
                controller,
                spell,
                actionCost,
                new SpellTargetSelection(targets, area),
                spendActions: true,
                invocationId: invocationId
            );
        }

        public static CastSpellResult Cast(SpellCastContext context, SpellTargetSelection selection)
        {
            CastSpellResult result = new();
            CreatureComponent creature = context.CasterCreature;
            ActionController controller = context.ActionController;
            SpellcastingState state = context.Spellcasting;
            if (
                controller != null
                && controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
                return CastEncounter(
                    bridge,
                    actor,
                    controller,
                    context.Spell,
                    context.ActionCost,
                    selection,
                    context.SpendActions,
                    CreateInvocationId("programmatic-cast")
                );
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
                                out UnityCombatRulesBridge mapBridge,
                                out CreatureId mapActor
                            )
                        )
                        {
                            MapOpResult map = mapBridge.Dispatch(
                                new AdvanceMultipleAttackPenaltyOp(mapActor)
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

        internal static bool TryCreateRulesSelection(
            UnityCombatRulesBridge bridge,
            IEnumerable<GameObject> targets,
            out SpellCastSelection selection,
            out string reason
        )
        {
            if (bridge == null)
                throw new ArgumentNullException(nameof(bridge));
            List<CreatureId> ids = new();
            HashSet<CreatureId> selected = new();
            foreach (GameObject target in targets ?? Array.Empty<GameObject>())
            {
                CreatureComponent creature = target?.GetComponent<CreatureComponent>();
                if (creature == null || !bridge.TryGetCreatureId(creature, out CreatureId id))
                {
                    selection = SpellCastSelection.Empty;
                    reason = "A selected spell target is not registered in the active encounter.";
                    return false;
                }
                if (!selected.Add(id))
                {
                    selection = SpellCastSelection.Empty;
                    reason = "A spell request cannot select the same creature more than once.";
                    return false;
                }
                ids.Add(id);
            }
            selection = new SpellCastSelection(ids);
            reason = string.Empty;
            return true;
        }

        internal static bool TryCreateRulesAreaSelection(
            UnityCombatRulesBridge bridge,
            AreaTargetResult area,
            out SpellCastSelection selection,
            out string reason
        )
        {
            if (bridge == null)
                throw new ArgumentNullException(nameof(bridge));
            if (area?.Placement == null)
            {
                selection = SpellCastSelection.Empty;
                reason = "An authoritative area placement is required.";
                return false;
            }
            if (
                !TryCreateRulesSelection(
                    bridge,
                    area.Creatures.Where(value => value.IsAffected).Select(value => value.Creature),
                    out SpellCastSelection targets,
                    out reason
                )
            )
            {
                selection = SpellCastSelection.Empty;
                return false;
            }
            SpellAreaPlacement rulesPlacement = UnitySpellAreaAdapter.ToRulesPlacement(
                area.Placement
            );
            selection = new SpellCastSelection(
                rulesPlacement,
                targets.Creatures.OrderBy(id => id.Value, StringComparer.Ordinal)
            );
            reason = string.Empty;
            return true;
        }

        private static CastSpellResult CastEncounter(
            UnityCombatRulesBridge bridge,
            CreatureId actor,
            ActionController controller,
            PreparedSpell spell,
            uint actionCost,
            SpellTargetSelection selection,
            bool spendActions,
            ActionInvocationId invocationId
        )
        {
            CastSpellResult result = new();
            if (!spendActions)
                throw new InvalidOperationException(
                    "Encounter spellcasting cannot bypass authoritative action costs."
                );
            if (spell == null || actionCost == 0 || actionCost > 3)
                return Fail(result, "The encounter spell request is incomplete.", controller);
            SpellTargetSelection requested = selection ?? SpellTargetSelection.None;
            if (requested.Area != null && requested.Targets.Count != 0)
                return Fail(
                    result,
                    "A spell request cannot combine creature targets with an area selection.",
                    controller
                );
            GameObject[] targets =
                requested.Area == null
                    ? requested.Targets.ToArray()
                    : requested
                        .Area.Creatures.Where(creature => creature.IsAffected)
                        .Select(creature => creature.Creature)
                        .ToArray();
            SpellCastSelection rules;
            string reason;
            bool selected =
                requested.Area != null
                    ? TryCreateRulesAreaSelection(bridge, requested.Area, out rules, out reason)
                    : TryCreateRulesSelection(bridge, targets, out rules, out reason);
            if (!selected)
                return Fail(result, reason, controller);
            CastSpellActionOp operation = new(
                invocationId,
                actor,
                new SpellReference(new SpellId(spell.Slug), spell.Rank),
                new SpellActionVariant((int)actionCost),
                rules
            );
            OpResult<CastSpellOutcome> dispatched = bridge.Dispatch(operation);
            if (dispatched is InvalidOpResult<CastSpellOutcome> invalid)
                return Fail(result, invalid.Reason, controller);
            if (dispatched is InterruptedOpResult<CastSpellOutcome>)
                return Fail(result, "Cast a Spell was interrupted.", controller);
            if (dispatched is CancelledOpResult<CastSpellOutcome>)
                return Fail(result, "Cast a Spell was cancelled.", controller);
            if (dispatched is not ResolvedOpResult<CastSpellOutcome> resolved)
                throw new InvalidOperationException(
                    "Cast a Spell returned an unknown structural result."
                );

            result.Success = true;
            result.Targets.AddRange(targets);
            foreach (SpellAttackResolution attack in resolved.Value.Attacks)
            {
                result.Amount += attack.FinalDamage;
                result.Rolls.Add(
                    new D20Result
                    {
                        roll = attack.AttackRoll.Values[0],
                        total = checked(attack.AttackRoll.Total + attack.AttackModifier),
                        degree = ToLegacyDegree(attack.Degree),
                    }
                );
            }
            foreach (SpellSaveResolution save in resolved.Value.Saves)
            {
                result.Amount += save.FinalDamage;
                result.Rolls.Add(
                    new D20Result
                    {
                        roll = save.Check.Roll.Values[0],
                        total = save.Check.Total,
                        degree = ToLegacyDegree(save.Check.Degree),
                    }
                );
            }
            if (controller != null)
                controller.IsTakingAction = false;
            return result;
        }

        private static ActionInvocationId CreateInvocationId(string prefix) =>
            new($"{prefix}-{Guid.NewGuid():N}");

        private static DegreeOfSuccess ToLegacyDegree(Game.Rules.Runtime.DegreeOfSuccess degree) =>
            degree switch
            {
                Game.Rules.Runtime.DegreeOfSuccess.CriticalFailure => DegreeOfSuccess.CriticalFail,
                Game.Rules.Runtime.DegreeOfSuccess.Failure => DegreeOfSuccess.Fail,
                Game.Rules.Runtime.DegreeOfSuccess.Success => DegreeOfSuccess.Success,
                Game.Rules.Runtime.DegreeOfSuccess.CriticalSuccess =>
                    DegreeOfSuccess.CriticalSuccess,
                _ => throw new ArgumentOutOfRangeException(nameof(degree)),
            };

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
                source
            );
        }

        public static void ApplyBasicFortitudeDamage(
            GameObject caster,
            GameObject target,
            DamageValue damage,
            CastSpellResult result,
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
