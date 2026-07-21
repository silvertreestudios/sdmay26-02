using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Creature.Rules;
using Game.KayKit;
using Game.Rules.Unity;
using GridPrivate;
using GridPublic;
using UnityEngine;
using HealthBatchChangeKind = Game.Rules.Runtime.HealthBatchChangeKind;
using HealthBatchOutcome = Game.Rules.Runtime.HealthBatchOutcome;
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
        internal ActionReservationToken OwnedActionReservation { get; }

        /// <summary>
        /// Gets the turn attack count captured before an attack spell commits its MAP increment.
        /// </summary>
        public uint? MultipleAttackCountOverride { get; internal set; }

        public SpellCastContext(
            GameObject caster,
            PreparedSpell spell,
            uint actionCost,
            bool spendActions,
            ISpellDefinition definition
        )
            : this(
                caster,
                spell,
                actionCost,
                spendActions,
                definition,
                ownedActionReservation: default
            ) { }

        internal SpellCastContext(
            GameObject caster,
            PreparedSpell spell,
            uint actionCost,
            bool spendActions,
            ISpellDefinition definition,
            ActionReservationToken ownedActionReservation
        )
        {
            Caster = caster;
            Spell = spell;
            ActionCost = actionCost;
            SpendActions = spendActions;
            Definition = definition;
            OwnedActionReservation = ownedActionReservation;
        }

        public ActionController ActionController =>
            Caster != null ? Caster.GetComponent<ActionController>() : null;
        public CreatureComponent CasterCreature =>
            Caster != null ? Caster.GetComponent<CreatureComponent>() : null;
        public SpellcastingState Spellcasting => CasterCreature?.Prepared?.Spellcasting;

        /// <summary>Applies one completed selection through this cast's awaited runtime context.</summary>
        /// <param name="selection">The selected direct targets or area.</param>
        /// <returns>The settled cast result.</returns>
        public ValueTask<CastSpellResult> CastAsync(SpellTargetSelection selection) =>
            SpellcastingRuntime.CastAsync(this, selection);
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

        /// <summary>Builds and executes one prepared spell cast through its registered definition.</summary>
        /// <param name="caster">The casting creature.</param>
        /// <param name="spell">The prepared spell entry being consumed or invoked.</param>
        /// <param name="actionCost">The selected action-cost variant.</param>
        /// <param name="targets">Optional already-selected direct targets.</param>
        /// <param name="area">Optional already-selected area result.</param>
        /// <param name="spendActions">Whether the cast pays encounter actions on success.</param>
        /// <returns>The settled cast result, including validation failures.</returns>
        public static ValueTask<CastSpellResult> CastAsync(
            GameObject caster,
            PreparedSpell spell,
            uint actionCost,
            IReadOnlyList<GameObject> targets = null,
            AreaTargetResult area = null,
            bool spendActions = true
        )
        {
            if (!SpellRegistry.TryGet(spell?.Slug, out ISpellDefinition definition))
                return new ValueTask<CastSpellResult>(
                    Fail(
                        new CastSpellResult(),
                        spell == null
                            ? "Spell is not prepared."
                            : spell.Name + " is not implemented."
                    )
                );

            SpellCastContext context = new(caster, spell, actionCost, spendActions, definition);
            return CastAsync(context, new SpellTargetSelection(targets, area));
        }

        /// <summary>
        /// Validates selection and active-encounter membership, commits costs and MAP, then awaits
        /// spell effects.
        /// </summary>
        /// <param name="context">The cast context and registered spell definition.</param>
        /// <param name="selection">The completed target selection.</param>
        /// <returns>The settled cast result.</returns>
        /// <remarks>
        /// Direct contexts acquire the controller action reservation here. Contexts created by
        /// <see cref="CastSpellAction"/> borrow its outer reservation, which remains owned by the
        /// enclosing multi-frame lifecycle until selection and cast presentation both finish.
        /// </remarks>
        public static async ValueTask<CastSpellResult> CastAsync(
            SpellCastContext context,
            SpellTargetSelection selection
        )
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
                return Fail(result, "Caster is not ready to cast spells.");
            if (
                creature.TryGetEncounterRulesBridge(out UnityEncounterRulesBridge attachedBridge)
                && !attachedBridge.HasActiveEncounter
            )
                return Fail(result, "The caster's encounter is no longer active.");
            if (!state.TryReserveCast())
                return Fail(result, "The caster is already casting a spell.");

            bool releaseActionReservation = false;
            ActionReservationToken actionReservation = default;
            try
            {
                if (controller != null)
                {
                    if (context.OwnedActionReservation.IsValid)
                    {
                        if (!controller.OwnsActionReservation(context.OwnedActionReservation))
                            return Fail(result, "The spell action no longer owns its reservation.");
                    }
                    else
                    {
                        if (!controller.TryReserveAction(out actionReservation))
                            return Fail(result, "The caster is already taking an action.");
                        releaseActionReservation = true;
                    }
                }
                if (
                    context.ActionCost > 0
                    && controller != null
                    && context.SpendActions
                    && controller.ActionPoints < context.ActionCost
                )
                    return Fail(result, "Not enough actions.");
                if (!state.CanCast(context.Spell))
                    return Fail(result, context.Spell.Name + " has no remaining slot.");

                SpellTargetSelection selected = selection ?? SpellTargetSelection.None;
                if (!context.Definition.IsSelectionValid(context, selected))
                    return Fail(result, "Spell target is invalid.");
                if (!HasValidActiveEncounterTargets(context, selected))
                    return Fail(result, "Spell target is outside the caster's active encounter.");

                bool appliesMap = context.Definition.AppliesMultipleAttackPenalty(context);
                uint attackCount = controller == null ? 0 : controller.StrikePenalty;
                if (controller != null && context.SpendActions)
                    await controller.SpendActionsAsync(context.ActionCost);
                if (!state.Spend(context.Spell))
                    throw new InvalidOperationException(
                        "A validated spell slot became unavailable before commitment."
                    );
                if (controller != null && context.SpendActions && appliesMap)
                {
                    context.MultipleAttackCountOverride = attackCount;
                    await controller.IncrementMultipleAttackPenaltyAsync();
                }

                if (!await context.Definition.Cast(context, selected, result))
                    throw new InvalidOperationException(
                        "A spell rejected a selection after validating and committing its costs."
                    );
                result.Success = true;
                if (!creature.IsDefeated)
                    context
                        .Caster.GetComponent<CreaturePresentation>()
                        ?.PlayAttack(AnimationStyle.Magic);
                CombatLogInterface log =
                    UnityEngine.Object.FindFirstObjectByType<CombatLogInterface>();
                log?.Log("- " + context.Caster.name + " casts " + context.Spell.Name + ".");
                return result;
            }
            finally
            {
                state.ReleaseCast();
                // An enclosing CastSpellAction owns its reservation through the outer coroutine's
                // finally. Only direct casts acquire and release the flag in this runtime.
                if (releaseActionReservation)
                    controller.ReleaseActionReservation(actionReservation);
            }
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

        /// <summary>Rolls damage, resolves a basic Fortitude save, and awaits applied damage.</summary>
        /// <param name="caster">The creature providing the spell DC.</param>
        /// <param name="target">The creature attempting the save.</param>
        /// <param name="dice">The spell's damage dice.</param>
        /// <param name="result">The cast result receiving rolls, targets, and applied amount.</param>
        /// <param name="applyDeafenedOnCriticalFailure">Whether critical failure adds Deafened.</param>
        /// <param name="source">The stable rule source for health provenance.</param>
        /// <returns>A task-like value that completes after damage and conditions settle.</returns>
        public static async ValueTask ApplyBasicFortitudeDamageAsync(
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
            await ApplyBasicFortitudeDamageAsync(
                caster,
                target,
                new DamageValue(dice.damageType, damage.TotalDamage),
                result,
                applyDeafenedOnCriticalFailure,
                source
            );
        }

        /// <summary>Resolves fixed damage through a basic Fortitude save and awaits application.</summary>
        /// <param name="caster">The creature providing the spell DC.</param>
        /// <param name="target">The creature attempting the save.</param>
        /// <param name="damage">The already-rolled typed damage.</param>
        /// <param name="result">The cast result receiving rolls, targets, and applied amount.</param>
        /// <param name="applyDeafenedOnCriticalFailure">Whether critical failure adds Deafened.</param>
        /// <param name="source">The stable rule source for health provenance.</param>
        /// <returns>A task-like value that completes after damage and conditions settle.</returns>
        public static async ValueTask ApplyBasicFortitudeDamageAsync(
            GameObject caster,
            GameObject target,
            DamageValue damage,
            CastSpellResult result,
            bool applyDeafenedOnCriticalFailure,
            RuleSource source
        )
        {
            List<UnityHealthBatchChange> changes = new();
            QueueBasicFortitudeDamage(
                caster,
                target,
                damage,
                result,
                applyDeafenedOnCriticalFailure,
                source,
                changes
            );
            if (changes.Count > 0)
                await ApplyFinalHealthBatchAsync(changes);
        }

        internal static void QueueBasicFortitudeDamage(
            GameObject caster,
            GameObject target,
            Dice dice,
            CastSpellResult result,
            bool applyDeafenedOnCriticalFailure,
            RuleSource source,
            ICollection<UnityHealthBatchChange> changes
        )
        {
            DamageRollResolution damage = DamageRoller.StartDamageResolution(
                new List<Dice> { dice },
                new List<DamageValue>()
            );
            DamageRoller.FinalizeDamageResolution(damage);
            QueueBasicFortitudeDamage(
                caster,
                target,
                new DamageValue(dice.damageType, damage.TotalDamage),
                result,
                applyDeafenedOnCriticalFailure,
                source,
                changes
            );
        }

        internal static void QueueBasicFortitudeDamage(
            GameObject caster,
            GameObject target,
            DamageValue damage,
            CastSpellResult result,
            bool applyDeafenedOnCriticalFailure,
            RuleSource source,
            ICollection<UnityHealthBatchChange> changes
        )
        {
            if (changes == null)
                throw new ArgumentNullException(nameof(changes));
            CreatureComponent casterCreature = caster.GetComponent<CreatureComponent>();
            CreatureComponent targetCreature = target.GetComponent<CreatureComponent>();
            int dc = casterCreature.Prepared.Spellcasting.SpellDc;
            int saveModifier = targetCreature.ResolveFortitudeSave().Total;
            D20Result save = D20.Roll(saveModifier, dc);
            result.Rolls.Add(save);
            int amount = BasicSaveDamage(damage.DamageAmount, save.degree);
            if (amount > 0)
            {
                changes.Add(
                    new UnityHealthBatchChange(
                        HealthBatchChangeKind.Damage,
                        targetCreature,
                        amount,
                        source
                    )
                );
            }
            if (applyDeafenedOnCriticalFailure && save.degree == DegreeOfSuccess.CriticalFail)
                (target.GetComponent<Conditions>() ?? target.AddComponent<Conditions>()).Add(
                    "Deafened",
                    new ConditionSource()
                );
            result.Targets.Add(target);
            result.Amount += amount;
        }

        internal static ValueTask<HealthBatchOutcome> ApplyFinalHealthBatchAsync(
            IReadOnlyList<UnityHealthBatchChange> changes
        )
        {
            if (changes == null)
                throw new ArgumentNullException(nameof(changes));
            if (changes.Count == 0)
                throw new ArgumentException(
                    "A completed spell health batch cannot be empty.",
                    nameof(changes)
                );
            UnityEncounterRulesBridge bridge = changes[0].Target.GetEncounterRulesBridge();
            return bridge.ApplyFinalHealthBatchAsync(changes);
        }

        public static bool IsUndead(CreatureComponent creature)
        {
            return creature.traits != null
                && creature.traits.Any(trait =>
                    string.Equals(trait, "undead", StringComparison.OrdinalIgnoreCase)
                );
        }

        private static bool HasValidActiveEncounterTargets(
            SpellCastContext context,
            SpellTargetSelection selection
        )
        {
            CreatureComponent caster = context.CasterCreature;
            if (
                !caster.TryGetEncounterRulesBridge(out UnityEncounterRulesBridge bridge)
                || !bridge.HasActiveEncounter
            )
                return true;
            if (!bridge.IsActiveEncounterParticipant(caster))
                return false;

            IEnumerable<GameObject> directTargets = selection.Targets ?? Array.Empty<GameObject>();
            IEnumerable<GameObject> areaTargets =
                selection.Area?.Creatures == null
                    ? Array.Empty<GameObject>()
                    : selection
                        .Area.Creatures.Where(affected => affected.IsAffected)
                        .Select(affected => affected.Creature);
            return directTargets
                .Concat(areaTargets)
                .Distinct()
                .All(target =>
                {
                    CreatureComponent targetCreature =
                        target == null ? null : target.GetComponent<CreatureComponent>();
                    return targetCreature != null
                        && bridge.IsActiveEncounterParticipant(targetCreature);
                });
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

        /// <summary>Records a normal spell validation or targeting failure.</summary>
        /// <param name="result">The cast result that should describe the failure.</param>
        /// <param name="message">The player-facing reason the cast did not proceed.</param>
        /// <returns>The supplied result marked unsuccessful.</returns>
        /// <remarks>
        /// This helper does not release action or cast reservations. The lifecycle that acquired
        /// a reservation must release it after all selection or cast work has finished; otherwise
        /// one rejected concurrent caller could clear another cast's reservation.
        /// </remarks>
        public static CastSpellResult Fail(CastSpellResult result, string message)
        {
            result.Success = false;
            result.Message = message;
            return result;
        }
    }
}
