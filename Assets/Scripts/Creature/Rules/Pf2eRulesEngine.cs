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
        /// Applies prepared strike-damage modifiers and adjustments to a Strike before damage is resolved.
        /// </summary>
        /// <param name="attacker">The creature making the Strike and owning the prepared rule state.</param>
        /// <param name="strike">The Strike being modified by matching PF2e rule elements.</param>
        public static void ApplyStrikeDamageModifiers(CreatureComponent attacker, Strike strike)
        {
            PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(attacker);
            List<string> itemOptions = BuildStrikeItemOptions(strike);
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

                PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(creature);
                if (prepared.HasOwnedItem("quick-tempered"))
                    new Rage(0).UseRage(controller.gameObject);
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

            List<string> itemOptions = traits.Select(trait => $"item:trait:{trait}").ToList();
            itemOptions.Add($"item:slug:{itemSlug}");

            foreach (ItemAlterationRule alteration in prepared.ItemAlterations)
            {
                if (!string.Equals(alteration.ItemType, itemType, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(alteration.Property, "traits", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(alteration.Mode, "add", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Pf2ePredicate.Evaluate(alteration.Predicate, prepared, itemOptions))
                    continue;
                if (!traits.Contains(alteration.Value))
                    traits.Add(alteration.Value);
            }

            return traits;
        }

        private static List<string> BuildStrikeItemOptions(Strike strike)
        {
            List<string> options = new();
            foreach (string trait in strike.Traits ?? new List<string>())
                options.Add($"item:trait:{trait}");

            if (strike.Traits != null && strike.Traits.Contains("thrown"))
                options.Add("item:thrown");
            if (strike.Traits != null && strike.Traits.Contains("ranged"))
                options.Add("item:ranged");
            if (strike.Traits != null && strike.Traits.Contains("unarmed"))
                options.Add("item:category:unarmed");

            return options;
        }
    }
}
