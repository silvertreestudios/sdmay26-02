using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPublic;
using UnityEngine;

namespace Game.Combat.Spells
{
    public interface ISpellDefinition
    {
        string Slug { get; }
        IReadOnlyList<uint> GetActionCosts(PreparedSpell spell);
        IEnumerator SelectAndCast(SpellCastContext context);

        /// <summary>Checks target and range legality without applying effects or spending costs.</summary>
        /// <param name="context">The prepared cast and selected action-cost variant.</param>
        /// <param name="selection">The completed direct-target or area selection.</param>
        /// <returns>Whether costs may commit and spell effects may begin.</returns>
        bool IsSelectionValid(SpellCastContext context, SpellTargetSelection selection);

        /// <summary>Applies the selected spell effects and awaits every causal mutation.</summary>
        /// <param name="context">The validated caster, prepared spell, and action cost.</param>
        /// <param name="selection">The targets or area chosen by the selection coroutine.</param>
        /// <param name="result">The shared result populated by the spell implementation.</param>
        /// <returns>Whether spell-specific effects completed successfully.</returns>
        ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        );
        bool AppliesMultipleAttackPenalty(SpellCastContext context);
    }

    public abstract class SpellDefinition : ISpellDefinition
    {
        public abstract string Slug { get; }

        public virtual IReadOnlyList<uint> GetActionCosts(PreparedSpell spell) =>
            spell?.ActionCosts ?? Array.Empty<uint>();

        public virtual bool AppliesMultipleAttackPenalty(SpellCastContext context) => false;

        public abstract IEnumerator SelectAndCast(SpellCastContext context);

        /// <inheritdoc/>
        public abstract bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        );

        /// <inheritdoc/>
        public abstract ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        );

        protected static IEnumerator CastNow(
            SpellCastContext context,
            SpellTargetSelection selection
        )
        {
            yield return CoroutineRunner.Await(context.CastAsync(selection));
        }

        protected static IEnumerator SelectFixedRangeTargetAndCast(
            SpellCastContext context,
            int rangeFeet
        )
        {
            CoroutineResult<StrikeTargetResult> target = new();
            yield return GridAPI
                .GetInstance()
                .GetStrikeTarget(
                    context.Caster,
                    SpellcastingRuntime.FixedRangeTarget(rangeFeet),
                    target
                );
            if (target.Value != null && target.Value.Target != null)
                yield return CoroutineRunner.Await(
                    context.CastAsync(SpellTargetSelection.ForTarget(target.Value.Target))
                );
            else
                SpellcastingRuntime.Fail(new CastSpellResult(), "Spell target is invalid.");
        }

        protected static IEnumerator SelectAreaAndCast(
            SpellCastContext context,
            AreaTargetRequest request
        )
        {
            CoroutineResult<AreaTargetResult> area = new();
            yield return GridAPI.GetInstance().GetAreaTarget(context.Caster, request, area);
            if (area.Value != null)
                yield return CoroutineRunner.Await(
                    context.CastAsync(SpellTargetSelection.ForArea(area.Value))
                );
            else
                SpellcastingRuntime.Fail(new CastSpellResult(), "Spell target is invalid.");
        }

        protected static GameObject FirstTarget(SpellTargetSelection selection)
        {
            return selection?.Targets != null && selection.Targets.Count > 0
                ? selection.Targets[0]
                : null;
        }
    }

    public static class SpellRegistry
    {
        private static readonly Dictionary<string, ISpellDefinition> Definitions = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["shield"] = new ShieldSpell(),
            ["guidance"] = new GuidanceSpell(),
            ["divine-lance"] = new DivineLanceSpell(),
            ["haunting-hymn"] = new HauntingHymnSpell(),
            ["light"] = new LightSpell(),
            ["bless"] = new BlessSpell(),
            ["infuse-vitality"] = new InfuseVitalitySpell(),
            ["heal"] = new HealSpell(),
        };

        public static bool TryGet(string slug, out ISpellDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                definition = null;
                return false;
            }
            return Definitions.TryGetValue(slug, out definition);
        }
    }

    public sealed class ShieldSpell : SpellDefinition
    {
        public override string Slug => "shield";

        public override IEnumerator SelectAndCast(SpellCastContext context) =>
            CastNow(context, SpellTargetSelection.None);

        /// <inheritdoc/>
        public override bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        ) => true;

        public override ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            SpellEffectController
                .GetOrAdd(context.Caster)
                .AddOrRefresh(new ShieldSpellEffect(context.Caster));
            result.Targets.Add(context.Caster);
            return new ValueTask<bool>(true);
        }
    }

    public sealed class LightSpell : SpellDefinition
    {
        public override string Slug => "light";

        public override IEnumerator SelectAndCast(SpellCastContext context) =>
            CastNow(context, SpellTargetSelection.None);

        /// <inheritdoc/>
        public override bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        ) => true;

        public override ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            result.Targets.Add(context.Caster);
            return new ValueTask<bool>(true);
        }
    }

    public sealed class GuidanceSpell : SpellDefinition
    {
        public override string Slug => "guidance";

        public override IEnumerator SelectAndCast(SpellCastContext context) =>
            SelectFixedRangeTargetAndCast(context, 30);

        /// <inheritdoc/>
        public override bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        )
        {
            GameObject target = FirstTarget(selection) ?? context.Caster;
            return SpellcastingRuntime.IsFriendly(context.Caster, target)
                && SpellcastingRuntime.DistanceFeet(context.Caster, target) <= 30
                && !(
                    target
                        .GetComponent<SpellEffectController>()
                        ?.HasEffect<GuidanceImmunitySpellEffect>()
                    ?? false
                );
        }

        public override ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            GameObject target = FirstTarget(selection) ?? context.Caster;
            if (
                !SpellcastingRuntime.IsFriendly(context.Caster, target)
                || SpellcastingRuntime.DistanceFeet(context.Caster, target) > 30
            )
                return new ValueTask<bool>(false);
            SpellEffectController controller = SpellEffectController.GetOrAdd(target);
            if (controller.HasEffect<GuidanceImmunitySpellEffect>())
                return new ValueTask<bool>(false);
            controller.AddOrRefresh(new GuidanceSpellEffect(context.Caster));
            result.Targets.Add(target);
            return new ValueTask<bool>(true);
        }
    }

    public sealed class DivineLanceSpell : SpellDefinition
    {
        public override string Slug => "divine-lance";

        public override bool AppliesMultipleAttackPenalty(SpellCastContext context) => true;

        public override IEnumerator SelectAndCast(SpellCastContext context) =>
            SelectFixedRangeTargetAndCast(context, 60);

        /// <inheritdoc/>
        public override bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        )
        {
            GameObject target = FirstTarget(selection);
            return target != null
                && target.GetComponent<CreatureComponent>() != null
                && SpellcastingRuntime.DistanceFeet(context.Caster, target) <= 60;
        }

        public override async ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            GameObject target = FirstTarget(selection);
            if (target == null || SpellcastingRuntime.DistanceFeet(context.Caster, target) > 60)
                return false;
            CreatureComponent casterCreature = context.CasterCreature;
            StrikeProfile profile = new(
                new List<Dice> { new Dice(2, 4, "spirit") },
                new List<DamageValue>()
            )
            {
                AttackModifierOverride = casterCreature.Prepared.Spellcasting.SpellAttackModifier,
                SourceInfo = new AttackSourceInfo(
                    "Divine Lance",
                    "spell",
                    "spell",
                    new[] { "attack", "spell", "spirit" }
                ),
                Traits = new List<string> { "attack", "spell", "spirit" },
                ItemSlug = "divine-lance",
                WeaponCategory = string.Empty,
                IsRangedAttack = true,
                ReachFeet = 60,
            };
            StrikeTargetResult targeting = new()
            {
                Target = target,
                DistanceFeet = SpellcastingRuntime.DistanceFeet(context.Caster, target),
                LineOfEffect = StrikeLineOfEffect.Clear,
                Cover = StrikeCover.None,
                RangePenalty = 0,
            };
            await StrikeResolutionPipeline.ResolveAsync(
                new StrikeResolutionRequest
                {
                    Attacker = context.Caster,
                    Target = target,
                    Profile = profile,
                    TargetingResult = targeting,
                    MultipleAttackCountOverride = context.MultipleAttackCountOverride,
                }
            );
            result.Targets.Add(target);
            return true;
        }
    }

    public sealed class HauntingHymnSpell : SpellDefinition
    {
        public override string Slug => "haunting-hymn";

        public override IEnumerator SelectAndCast(SpellCastContext context)
        {
            return SelectAreaAndCast(
                context,
                new AreaTargetRequest { Shape = AreaShape.Cone, SizeFeet = 15 }
            );
        }

        /// <inheritdoc/>
        public override bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        ) => selection?.Area != null;

        public override async ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            if (selection?.Area == null)
                return false;
            List<UnityHealthBatchChange> changes = new();
            foreach (
                AreaAffectedCreature affected in selection.Area.Creatures.Where(creature =>
                    creature.IsAffected
                )
            )
                SpellcastingRuntime.QueueBasicFortitudeDamage(
                    context.Caster,
                    affected.Creature,
                    new Dice(1, 8, "sonic"),
                    result,
                    applyDeafenedOnCriticalFailure: true,
                    source: RuleSource.FromSlug(Slug),
                    changes: changes
                );
            if (changes.Count > 0)
                await SpellcastingRuntime.ApplyFinalHealthBatchAsync(changes);
            return true;
        }
    }

    public sealed class BlessSpell : SpellDefinition
    {
        public override string Slug => "bless";

        public override IEnumerator SelectAndCast(SpellCastContext context)
        {
            return CastNow(
                context,
                SpellTargetSelection.ForTargets(
                    SpellcastingRuntime.FriendlyCreaturesInEmanation(context.Caster, 15)
                )
            );
        }

        /// <inheritdoc/>
        public override bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        )
        {
            IReadOnlyList<GameObject> selected =
                selection?.Targets == null || selection.Targets.Count == 0
                    ? new[] { context.Caster }
                    : selection.Targets;
            return selected.Any(target =>
                target != null
                && SpellcastingRuntime.IsFriendly(context.Caster, target)
                && SpellcastingRuntime.DistanceFeet(context.Caster, target) <= 15
            );
        }

        public override ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            IReadOnlyList<GameObject> selected =
                selection?.Targets == null || selection.Targets.Count == 0
                    ? new[] { context.Caster }
                    : selection.Targets;
            foreach (GameObject target in selected)
            {
                if (
                    target != null
                    && SpellcastingRuntime.IsFriendly(context.Caster, target)
                    && SpellcastingRuntime.DistanceFeet(context.Caster, target) <= 15
                )
                {
                    SpellEffectController
                        .GetOrAdd(target)
                        .AddOrRefresh(new BlessSpellEffect(context.Caster));
                    result.Targets.Add(target);
                }
            }
            return new ValueTask<bool>(result.Targets.Count > 0);
        }
    }

    public sealed class InfuseVitalitySpell : SpellDefinition
    {
        public override string Slug => "infuse-vitality";

        public override IEnumerator SelectAndCast(SpellCastContext context) =>
            SelectFixedRangeTargetAndCast(context, 30);

        /// <inheritdoc/>
        public override bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        )
        {
            if (
                selection?.Targets == null
                || selection.Targets.Count == 0
                || selection.Targets.Count > context.ActionCost
            )
                return false;
            HashSet<GameObject> unique = new();
            return selection.Targets.All(target =>
                target != null
                && unique.Add(target)
                && SpellcastingRuntime.IsFriendly(context.Caster, target)
                && SpellcastingRuntime.DistanceFeet(context.Caster, target) <= 30
            );
        }

        public override ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            if (
                selection?.Targets == null
                || selection.Targets.Count == 0
                || selection.Targets.Count > context.ActionCost
            )
                return new ValueTask<bool>(false);
            HashSet<GameObject> unique = new();
            foreach (GameObject target in selection.Targets)
            {
                if (
                    target == null
                    || !unique.Add(target)
                    || !SpellcastingRuntime.IsFriendly(context.Caster, target)
                    || SpellcastingRuntime.DistanceFeet(context.Caster, target) > 30
                )
                    return new ValueTask<bool>(false);
            }
            foreach (GameObject target in unique)
            {
                SpellEffectController
                    .GetOrAdd(target)
                    .AddOrRefresh(new InfuseVitalitySpellEffect(context.Caster));
                result.Targets.Add(target);
            }
            return new ValueTask<bool>(true);
        }
    }

    public sealed class HealSpell : SpellDefinition
    {
        public override string Slug => "heal";

        public override IEnumerator SelectAndCast(SpellCastContext context)
        {
            if (context.ActionCost == 3)
                return SelectAreaAndCast(
                    context,
                    new AreaTargetRequest
                    {
                        Shape = AreaShape.Emanation,
                        SizeFeet = 30,
                        IncludeCenter = true,
                    }
                );
            return SelectFixedRangeTargetAndCast(context, context.ActionCost == 1 ? 5 : 30);
        }

        /// <inheritdoc/>
        public override bool IsSelectionValid(
            SpellCastContext context,
            SpellTargetSelection selection
        )
        {
            List<GameObject> selected = SelectedTargets(context, selection);
            if (
                selected.Count == 0
                || selected.All(target => target.GetComponent<CreatureComponent>() == null)
            )
                return false;
            int maximumRange = context.ActionCost == 1 ? 5 : 30;
            return selected.All(target =>
                SpellcastingRuntime.DistanceFeet(context.Caster, target) <= maximumRange
            );
        }

        public override async ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            List<GameObject> selected = SelectedTargets(context, selection);
            if (selected.Count == 0)
                return false;
            List<UnityHealthBatchChange> changes = new();
            foreach (GameObject target in selected.Distinct())
            {
                if (
                    context.ActionCost == 1
                    && SpellcastingRuntime.DistanceFeet(context.Caster, target) > 5
                )
                    return false;
                if (
                    context.ActionCost == 2
                    && SpellcastingRuntime.DistanceFeet(context.Caster, target) > 30
                )
                    return false;
                CreatureComponent creature = target.GetComponent<CreatureComponent>();
                if (creature == null)
                    continue;
                int amount =
                    new Dice(1, 8, "vitality").Roll()
                    + (context.ActionCost == 2 && !SpellcastingRuntime.IsUndead(creature) ? 8 : 0);
                result.Targets.Add(target);
                if (SpellcastingRuntime.IsUndead(creature))
                    SpellcastingRuntime.QueueBasicFortitudeDamage(
                        context.Caster,
                        target,
                        new DamageValue("vitality", amount),
                        result,
                        applyDeafenedOnCriticalFailure: false,
                        source: RuleSource.FromSlug(Slug),
                        changes: changes
                    );
                else if (SpellcastingRuntime.IsFriendly(context.Caster, target))
                {
                    changes.Add(
                        new UnityHealthBatchChange(
                            HealthBatchChangeKind.Healing,
                            creature,
                            amount,
                            RuleSource.FromSlug(Slug)
                        )
                    );
                }
            }
            if (changes.Count > 0)
            {
                HealthBatchOutcome health = await SpellcastingRuntime.ApplyFinalHealthBatchAsync(
                    changes
                );
                for (int index = 0; index < changes.Count; index++)
                {
                    if (changes[index].Kind == HealthBatchChangeKind.Healing)
                        result.Amount += health.Changes[index].Applied;
                }
            }
            return result.Targets.Count > 0;
        }

        private static List<GameObject> SelectedTargets(
            SpellCastContext context,
            SpellTargetSelection selection
        )
        {
            List<GameObject> selected = new();
            if (context.ActionCost == 3 && selection?.Area != null)
                selected.AddRange(
                    selection
                        .Area.Creatures.Where(creature => creature.IsAffected)
                        .Select(creature => creature.Creature)
                        .Where(target => target != null)
                );
            else if (selection?.Targets != null)
                selected.AddRange(selection.Targets.Where(target => target != null));
            return selected.Distinct().ToList();
        }
    }
}
