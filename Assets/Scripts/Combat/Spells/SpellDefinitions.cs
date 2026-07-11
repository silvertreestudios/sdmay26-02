using Game.Creature;
using Game.Creature.Rules;
using GridPublic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.Combat.Spells
{
    public interface ISpellDefinition
    {
        string Slug { get; }
        IReadOnlyList<uint> GetActionCosts(PreparedSpell spell);
        IEnumerator SelectAndCast(SpellCastContext context);
        bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result);
        bool AppliesMultipleAttackPenalty(SpellCastContext context);
    }

    public abstract class SpellDefinition : ISpellDefinition
    {
        public abstract string Slug { get; }

        public virtual IReadOnlyList<uint> GetActionCosts(PreparedSpell spell) => spell?.ActionCosts ?? Array.Empty<uint>();
        public virtual bool AppliesMultipleAttackPenalty(SpellCastContext context) => false;
        public abstract IEnumerator SelectAndCast(SpellCastContext context);
        public abstract bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result);

        protected static IEnumerator CastNow(SpellCastContext context, SpellTargetSelection selection)
        {
            context.Cast(selection);
            yield break;
        }

        protected static IEnumerator SelectFixedRangeTargetAndCast(SpellCastContext context, int rangeFeet)
        {
            CoroutineResult<StrikeTargetResult> target = new();
            yield return GridAPI.GetInstance().GetStrikeTarget(context.Caster, SpellcastingRuntime.FixedRangeTarget(rangeFeet), target);
            if (target.Value != null && target.Value.Target != null)
                context.Cast(SpellTargetSelection.ForTarget(target.Value.Target));
            else
                SpellcastingRuntime.Fail(new CastSpellResult(), "Spell target is invalid.", context.ActionController);
        }

        protected static IEnumerator SelectAreaAndCast(SpellCastContext context, AreaTargetRequest request)
        {
            CoroutineResult<AreaTargetResult> area = new();
            yield return GridAPI.GetInstance().GetAreaTarget(context.Caster, request, area);
            if (area.Value != null)
                context.Cast(SpellTargetSelection.ForArea(area.Value));
            else
                SpellcastingRuntime.Fail(new CastSpellResult(), "Spell target is invalid.", context.ActionController);
        }

        protected static GameObject FirstTarget(SpellTargetSelection selection)
        {
            return selection?.Targets != null && selection.Targets.Count > 0 ? selection.Targets[0] : null;
        }
    }

    public static class SpellRegistry
    {
        private static readonly Dictionary<string, ISpellDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["shield"] = new ShieldSpell(),
            ["guidance"] = new GuidanceSpell(),
            ["divine-lance"] = new DivineLanceSpell(),
            ["haunting-hymn"] = new HauntingHymnSpell(),
            ["light"] = new LightSpell(),
            ["bless"] = new BlessSpell(),
            ["infuse-vitality"] = new InfuseVitalitySpell(),
            ["heal"] = new HealSpell()
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

        public override IEnumerator SelectAndCast(SpellCastContext context) => CastNow(context, SpellTargetSelection.None);

        public override bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result)
        {
            SpellEffectController.GetOrAdd(context.Caster).AddOrRefresh(new ShieldSpellEffect(context.Caster));
            result.Targets.Add(context.Caster);
            return true;
        }
    }

    public sealed class LightSpell : SpellDefinition
    {
        public override string Slug => "light";

        public override IEnumerator SelectAndCast(SpellCastContext context) => CastNow(context, SpellTargetSelection.None);

        public override bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result)
        {
            result.Targets.Add(context.Caster);
            return true;
        }
    }

    public sealed class GuidanceSpell : SpellDefinition
    {
        public override string Slug => "guidance";

        public override IEnumerator SelectAndCast(SpellCastContext context) => SelectFixedRangeTargetAndCast(context, 30);

        public override bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result)
        {
            GameObject target = FirstTarget(selection) ?? context.Caster;
            if (!SpellcastingRuntime.IsFriendly(context.Caster, target) || SpellcastingRuntime.DistanceFeet(context.Caster, target) > 30)
                return false;
            SpellEffectController controller = SpellEffectController.GetOrAdd(target);
            if (controller.HasEffect<GuidanceImmunitySpellEffect>())
                return false;
            controller.AddOrRefresh(new GuidanceSpellEffect(context.Caster));
            result.Targets.Add(target);
            return true;
        }
    }

    public sealed class DivineLanceSpell : SpellDefinition
    {
        public override string Slug => "divine-lance";
        public override bool AppliesMultipleAttackPenalty(SpellCastContext context) => true;

        public override IEnumerator SelectAndCast(SpellCastContext context) => SelectFixedRangeTargetAndCast(context, 60);

        public override bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result)
        {
            GameObject target = FirstTarget(selection);
            if (target == null || SpellcastingRuntime.DistanceFeet(context.Caster, target) > 60)
                return false;
            CreatureComponent casterCreature = context.CasterCreature;
            StrikeProfile profile = new(new List<Dice> { new Dice(2, 4, "spirit") }, new List<DamageValue>())
            {
                AttackModifierOverride = casterCreature.Prepared.Spellcasting.SpellAttackModifier,
                SourceInfo = new AttackSourceInfo("Divine Lance", "spell", "spell", new[] { "attack", "spell", "spirit" }),
                Traits = new List<string> { "attack", "spell", "spirit" },
                ItemSlug = "divine-lance",
                WeaponCategory = string.Empty,
                IsRangedAttack = true,
                ReachFeet = 60
            };
            StrikeTargetResult targeting = new()
            {
                Target = target,
                DistanceFeet = SpellcastingRuntime.DistanceFeet(context.Caster, target),
                LineOfEffect = StrikeLineOfEffect.Clear,
                Cover = StrikeCover.None,
                RangePenalty = 0
            };
            StrikeResolutionPipeline.Resolve(new StrikeResolutionRequest
            {
                Attacker = context.Caster,
                Target = target,
                Profile = profile,
                TargetingResult = targeting
            });
            result.Targets.Add(target);
            return true;
        }
    }

    public sealed class HauntingHymnSpell : SpellDefinition
    {
        public override string Slug => "haunting-hymn";

        public override IEnumerator SelectAndCast(SpellCastContext context)
        {
            return SelectAreaAndCast(context, new AreaTargetRequest { Shape = AreaShape.Cone, SizeFeet = 15 });
        }

        public override bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result)
        {
            if (selection?.Area == null)
                return false;
            foreach (AreaAffectedCreature affected in selection.Area.Creatures.Where(creature => creature.IsAffected))
                SpellcastingRuntime.ApplyBasicFortitudeDamage(context.Caster, affected.Creature, new Dice(1, 8, "sonic"), result, applyDeafenedOnCriticalFailure: true);
            return true;
        }
    }

    public sealed class BlessSpell : SpellDefinition
    {
        public override string Slug => "bless";

        public override IEnumerator SelectAndCast(SpellCastContext context)
        {
            return CastNow(context, SpellTargetSelection.ForTargets(SpellcastingRuntime.FriendlyCreaturesInEmanation(context.Caster, 15)));
        }

        public override bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result)
        {
            IReadOnlyList<GameObject> selected = selection?.Targets == null || selection.Targets.Count == 0
                ? new[] { context.Caster }
                : selection.Targets;
            foreach (GameObject target in selected)
            {
                if (target != null && SpellcastingRuntime.IsFriendly(context.Caster, target) && SpellcastingRuntime.DistanceFeet(context.Caster, target) <= 15)
                {
                    SpellEffectController.GetOrAdd(target).AddOrRefresh(new BlessSpellEffect(context.Caster));
                    result.Targets.Add(target);
                }
            }
            return result.Targets.Count > 0;
        }
    }

    public sealed class InfuseVitalitySpell : SpellDefinition
    {
        public override string Slug => "infuse-vitality";

        public override IEnumerator SelectAndCast(SpellCastContext context) => SelectFixedRangeTargetAndCast(context, 30);

        public override bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result)
        {
            if (selection?.Targets == null || selection.Targets.Count == 0 || selection.Targets.Count > context.ActionCost)
                return false;
            HashSet<GameObject> unique = new();
            foreach (GameObject target in selection.Targets)
            {
                if (target == null || !unique.Add(target) || !SpellcastingRuntime.IsFriendly(context.Caster, target) || SpellcastingRuntime.DistanceFeet(context.Caster, target) > 30)
                    return false;
            }
            foreach (GameObject target in unique)
            {
                SpellEffectController.GetOrAdd(target).AddOrRefresh(new InfuseVitalitySpellEffect(context.Caster));
                result.Targets.Add(target);
            }
            return true;
        }
    }

    public sealed class HealSpell : SpellDefinition
    {
        public override string Slug => "heal";

        public override IEnumerator SelectAndCast(SpellCastContext context)
        {
            if (context.ActionCost == 3)
                return SelectAreaAndCast(context, new AreaTargetRequest { Shape = AreaShape.Emanation, SizeFeet = 30, IncludeCenter = true });
            return SelectFixedRangeTargetAndCast(context, context.ActionCost == 1 ? 5 : 30);
        }

        public override bool Cast(SpellCastContext context, SpellTargetSelection selection, CastSpellResult result)
        {
            List<GameObject> selected = new();
            if (context.ActionCost == 3 && selection?.Area != null)
                selected.AddRange(selection.Area.Creatures.Where(creature => creature.IsAffected).Select(creature => creature.Creature));
            else if (selection?.Targets != null)
                selected.AddRange(selection.Targets.Where(target => target != null));
            if (selected.Count == 0)
                return false;
            foreach (GameObject target in selected.Distinct())
            {
                if (context.ActionCost == 1 && SpellcastingRuntime.DistanceFeet(context.Caster, target) > 5)
                    return false;
                if (context.ActionCost == 2 && SpellcastingRuntime.DistanceFeet(context.Caster, target) > 30)
                    return false;
                CreatureComponent creature = target.GetComponent<CreatureComponent>();
                if (creature == null)
                    continue;
                int amount = new Dice(1, 8, "vitality").Roll() + (context.ActionCost == 2 && !SpellcastingRuntime.IsUndead(creature) ? 8 : 0);
                result.Targets.Add(target);
                if (SpellcastingRuntime.IsUndead(creature))
                    SpellcastingRuntime.ApplyBasicFortitudeDamage(context.Caster, target, new DamageValue("vitality", amount), result, applyDeafenedOnCriticalFailure: false);
                else if (SpellcastingRuntime.IsFriendly(context.Caster, target))
                {
                    creature.Heal(amount);
                    result.Amount += amount;
                }
            }
            return result.Targets.Count > 0;
        }
    }
}
