using Game.AbilityActions;
using Game.Creature;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Runtime facade for shared PF2e rule hooks that are not owned by a single action implementation.
    /// </summary>
    public static class Pf2eRulesEngine
    {
        /// <summary>
        /// Applies prepared strike-damage modifiers, damage dice, and adjustments to a Strike before damage is resolved.
        /// </summary>
        /// <param name="attacker">The creature making the Strike and owning the prepared rule state.</param>
        /// <param name="strike">The Strike being modified by matching PF2e rule elements.</param>
        /// <param name="target">Optional target creature for target-scoped predicates.</param>
        public static void ApplyStrikeDamageModifiers(CreatureComponent attacker, Strike strike, CreatureComponent target = null)
        {
            PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(attacker);
            List<string> itemOptions = BuildStrikeItemOptions(prepared, strike, target);
            ApplyAbilityDamageModifiers(attacker, strike, prepared, itemOptions);
            ApplyFlatStrikeDamageModifiers(strike, prepared, itemOptions);
            ApplyStrikeDamageDice(strike, prepared, itemOptions);
        }


        /// <summary>
        /// Runs encounter-cleanup rule hooks for each combatant; action-specific details stay in their own rule classes.
        /// </summary>
        /// <param name="combatants">The combatants leaving encounter state.</param>
        public static void EndEncounter(IEnumerable<ActionController> combatants)
        {
            if (combatants == null)
                return;

            foreach (ActionController controller in combatants)
                new Rage(0).EndRage(controller?.gameObject);
        }

        /// <summary>
        /// Applies combat-start rule hooks such as auto-starting Rage for matching prepared character options.
        /// </summary>
        /// <param name="combatants">The combatants entering encounter state.</param>
        public static void ApplyCombatStartRules(IEnumerable<ActionController> combatants)
        {
            if (combatants == null)
                return;

            foreach (ActionController controller in combatants)
            {
                CreatureComponent creature = controller?.GetComponent<CreatureComponent>();
                if (creature == null)
                    continue;

                ApplyImportedPassiveAbilities(controller, creature);

                PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(creature);
                if (prepared.HasOwnedItem("quick-tempered"))
                    new Rage(0).UseRage(controller.gameObject);
            }
        }

        private static void ApplyImportedPassiveAbilities(ActionController controller, CreatureComponent creature)
        {
            if (controller == null || creature?.passives == null)
                return;

            foreach (string passive in creature.passives)
            {
                if (string.IsNullOrWhiteSpace(passive))
                    continue;

                Ability ability = DefinedAbilities.TryGet(passive);
                ability?.Apply(controller.gameObject);
            }
        }

        /// <summary>
        /// Applies supported PF2e item trait alteration rules without modifying the item data itself.
        /// </summary>
        /// <param name="prepared">The prepared character whose item alteration rules are evaluated.</param>
        /// <param name="itemType">The PF2e item type being altered.</param>
        /// <param name="itemSlug">The slug of the item being altered.</param>
        /// <param name="existingTraits">The traits already present on the item.</param>
        /// <returns>A new trait list containing existing traits plus any matching additions.</returns>
        public static List<string> GetAlteredTraits(PreparedCharacter prepared, string itemType, string itemSlug, IEnumerable<string> existingTraits)
        {
            List<string> traits = new(existingTraits ?? Enumerable.Empty<string>());
            if (prepared == null)
                return traits;

            List<string> itemOptions = BuildItemOptions(itemSlug, null, false, traits, null);
            foreach (ItemAlterationRule alteration in prepared.ItemAlterations)
            {
                if (!MatchesAlteration(alteration, itemType, "traits"))
                    continue;
                if (!Pf2ePredicate.Evaluate(alteration.Predicate, prepared, itemOptions))
                    continue;
                if (!traits.Contains(alteration.Value))
                    traits.Add(alteration.Value);
            }

            return traits;
        }

        private static void ApplyFlatStrikeDamageModifiers(Strike strike, PreparedCharacter prepared, List<string> itemOptions)
        {
            List<RuleModifier> modifiers = prepared.Modifiers
                .Where(m => string.Equals(m.Selector, "strike-damage", StringComparison.OrdinalIgnoreCase))
                .Where(m => Pf2ePredicate.Evaluate(m.Predicate, prepared, itemOptions))
                .GroupBy(m => m.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            foreach (RuleAdjustment adjustment in prepared.Adjustments
                .Where(a => string.Equals(a.Selector, "strike-damage", StringComparison.OrdinalIgnoreCase))
                .Where(a => Pf2ePredicate.Evaluate(a.Predicate, prepared, itemOptions))
                .OrderBy(a => a.Priority))
            {
                RuleModifier modifier = modifiers.LastOrDefault(m => string.Equals(m.Slug, adjustment.Slug, StringComparison.OrdinalIgnoreCase));
                if (modifier == null)
                    continue;

                if (string.Equals(adjustment.Mode, "upgrade", StringComparison.OrdinalIgnoreCase))
                    modifier.Value = Math.Max(modifier.Value, Mathf.RoundToInt(adjustment.Value));
                else if (string.Equals(adjustment.Mode, "multiply", StringComparison.OrdinalIgnoreCase))
                    modifier.Value = Mathf.FloorToInt(modifier.Value * adjustment.Value);
            }

            foreach (RuleModifier modifier in modifiers)
            {
                if (modifier.Value == 0)
                    continue;

                string damageType = strike.FlatDamages.Count > 0 ? strike.FlatDamages[0].DamageType : strike.Damages.FirstOrDefault()?.damageType ?? "Untyped";
                strike.FlatDamages.Add(new DamageValue(damageType, modifier.Value));
            }
        }

        private static void ApplyAbilityDamageModifiers(CreatureComponent attacker, Strike strike, PreparedCharacter prepared, List<string> itemOptions)
        {
            if (attacker == null || strike == null || strike.IsRangedAttack)
                return;

            foreach (RuleModifier modifier in prepared.Modifiers
                .Where(m => string.Equals(m.Selector, "melee-strike-damage", StringComparison.OrdinalIgnoreCase))
                .Where(m => !string.IsNullOrWhiteSpace(m.Ability))
                .Where(m => Pf2ePredicate.Evaluate(m.Predicate, prepared, itemOptions)))
            {
                int abilityModifier = GetAbilityModifier(attacker, modifier.Ability);
                string damageType = strike.FlatDamages.Count > 0 ? strike.FlatDamages[0].DamageType : strike.Damages.FirstOrDefault()?.damageType ?? "Untyped";
                if (strike.FlatDamages.Count == 0)
                    strike.FlatDamages.Add(new DamageValue(damageType, abilityModifier));
                else
                    strike.FlatDamages[0] = new DamageValue(damageType, abilityModifier);
            }
        }

        private static void ApplyStrikeDamageDice(Strike strike, PreparedCharacter prepared, List<string> itemOptions)
        {
            foreach (RuleDamageDice damageDice in prepared.DamageDice
                .Where(d => string.Equals(d.Selector, "strike-damage", StringComparison.OrdinalIgnoreCase))
                .Where(d => d.DiceNumber > 0 && d.DieSize > 0)
                .Where(d => Pf2ePredicate.Evaluate(d.Predicate, prepared, itemOptions)))
            {
                strike.Damages.Add(new Dice(damageDice.DiceNumber, damageDice.DieSize, damageDice.Category ?? "precision"));
            }
        }

        private static List<string> BuildStrikeItemOptions(PreparedCharacter prepared, Strike strike, CreatureComponent target)
        {
            List<string> options = BuildItemOptions(strike?.ItemSlug, strike?.WeaponCategory, strike?.IsRangedAttack ?? false, strike?.Traits, strike?.Damages.FirstOrDefault());
            AddAlteredItemTags(prepared, options);
            AddTargetConditionOptions(target, options);
            return options;
        }

        private static List<string> BuildItemOptions(string itemSlug, string category, bool isRanged, IEnumerable<string> traits, Dice firstDamageDie)
        {
            List<string> options = new();
            foreach (string trait in traits ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(trait))
                    continue;

                options.Add($"item:trait:{trait}");
                if (string.Equals(trait, "ranged", StringComparison.OrdinalIgnoreCase))
                    options.Add("item:ranged");
                if (trait.StartsWith("thrown", StringComparison.OrdinalIgnoreCase))
                    options.Add("item:thrown");
            }

            if (!string.IsNullOrWhiteSpace(itemSlug))
                options.Add($"item:slug:{itemSlug}");
            if (!string.IsNullOrWhiteSpace(category))
                options.Add($"item:category:{category}");
            if (firstDamageDie != null)
                options.Add($"item:damage:die:faces:{firstDamageDie.sidesPerDie}");
            if (isRanged)
                options.Add("item:ranged");
            if (options.Contains("item:trait:unarmed", StringComparer.OrdinalIgnoreCase))
                options.Add("item:category:unarmed");

            return options;
        }

        private static void AddAlteredItemTags(PreparedCharacter prepared, List<string> options)
        {
            if (prepared == null)
                return;

            foreach (ItemAlterationRule alteration in prepared.ItemAlterations)
            {
                if (!MatchesAlteration(alteration, "weapon", "other-tags"))
                    continue;
                if (!Pf2ePredicate.Evaluate(alteration.Predicate, prepared, options))
                    continue;

                string option = $"item:tag:{alteration.Value}";
                if (!options.Contains(option, StringComparer.OrdinalIgnoreCase))
                    options.Add(option);
            }
        }

        private static void AddTargetConditionOptions(CreatureComponent target, List<string> options)
        {
            Conditions conditions = target?.GetComponent<Conditions>();
            if (conditions == null)
                return;

            foreach (string condition in conditions.GetConditionNames())
            {
                string slug = Pf2eSlug.FromName(condition);
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

                AddOption(options, $"target:condition:{slug}");
                if (string.Equals(slug, "flat-footed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(slug, "offguard", StringComparison.OrdinalIgnoreCase))
                {
                    AddOption(options, "target:condition:off-guard");
                }
            }
        }

        private static void AddOption(List<string> options, string option)
        {
            if (!options.Contains(option, StringComparer.OrdinalIgnoreCase))
                options.Add(option);
        }

        private static bool MatchesAlteration(ItemAlterationRule alteration, string itemType, string property)
        {
            return alteration != null
                && string.Equals(alteration.ItemType, itemType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(alteration.Property, property, StringComparison.OrdinalIgnoreCase)
                && string.Equals(alteration.Mode, "add", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetAbilityModifier(CreatureComponent creature, string ability)
        {
            return ability?.ToLowerInvariant() switch
            {
                "str" => creature.strMod,
                "dex" => creature.dexMod,
                "con" => creature.conMod,
                "int" => creature.intMod,
                "wis" => creature.wisMod,
                "cha" => creature.chaMod,
                _ => 0
            };
        }

    }
}
