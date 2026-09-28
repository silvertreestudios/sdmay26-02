using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Rules;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Runtime facade for shared PF2e rule hooks that are not owned by a single action implementation.
    /// </summary>
    public static class Pf2eRulesEngine
    {
        /// <summary>
        /// Imports passive abilities before the encounter composition captures initial bindings.
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
            }
        }

        private static void ApplyImportedPassiveAbilities(
            ActionController controller,
            CreatureComponent creature
        )
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
        /// <param name="creature">
        /// The creature whose prepared rules and authoritative active effects are evaluated.
        /// </param>
        /// <param name="itemType">The PF2e item type being altered.</param>
        /// <param name="itemSlug">The slug of the item being altered.</param>
        /// <param name="existingTraits">The traits already present on the item.</param>
        /// <returns>A new trait list containing existing traits plus any matching additions.</returns>
        public static List<string> GetAlteredTraits(
            CreatureComponent creature,
            string itemType,
            string itemSlug,
            IEnumerable<string> existingTraits
        )
        {
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(creature);
            List<string> traits = new(existingTraits ?? Enumerable.Empty<string>());

            List<string> itemOptions = BuildItemOptions(itemSlug, null, false, traits, null);
            AddActiveActorOptions(creature.gameObject, itemOptions);
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

        private static List<string> BuildItemOptions(
            string itemSlug,
            string category,
            bool isRanged,
            IEnumerable<string> traits,
            Dice firstDamageDie
        )
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

        private static void AddOption(List<string> options, string option)
        {
            if (!options.Contains(option, StringComparer.OrdinalIgnoreCase))
                options.Add(option);
        }

        private static void AddActiveActorOptions(GameObject actor, List<string> options)
        {
            ActionController controller = actor?.GetComponent<ActionController>();
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId creature
                )
            )
            {
                return;
            }

            foreach (string option in RageRules.GetActiveRollOptions(bridge.Snapshot, creature))
                AddOption(options, option);
        }

        private static bool MatchesAlteration(
            ItemAlterationRule alteration,
            string itemType,
            string property
        )
        {
            return alteration != null
                && string.Equals(alteration.ItemType, itemType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(alteration.Property, property, StringComparison.OrdinalIgnoreCase)
                && string.Equals(alteration.Mode, "add", StringComparison.OrdinalIgnoreCase);
        }
    }
}
