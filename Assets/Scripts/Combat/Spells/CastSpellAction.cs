using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using UnityEngine;

namespace Game.Combat.Spells
{
    /// <summary>
    /// Presents one spell through the legacy Unity-owned spellcasting pipeline.
    /// </summary>
    /// <remarks>
    /// New and migrated spells should use <see cref="RulesCastSpellAction"/> so validation,
    /// costs, and effects remain authoritative in the rules runtime.
    /// </remarks>
    [Obsolete(
        "Use RulesCastSpellAction for migrated spells; CastSpellAction is retained only for legacy non-Light spells.",
        false
    )]
    [Serializable]
    public class CastSpellAction : MultiFrameEntityAction
    {
        private readonly PreparedSpell spell;
        private readonly uint variantActionCost;
        private readonly ISpellDefinition definition;

        /// <summary>Gets the legacy prepared spell represented by this action.</summary>
        public PreparedSpell Spell => spell;

        /// <inheritdoc/>
        public override string ActionName => BuildActionName(spell, variantActionCost);

        /// <summary>Creates an action for one legacy prepared spell and action-cost variant.</summary>
        public CastSpellAction(PreparedSpell spell, uint actionCost, ISpellDefinition definition)
            : base(actionCost)
        {
            this.spell = spell;
            variantActionCost = actionCost;
            this.definition = definition;
        }

        /// <summary>
        /// Adds the deprecated non-Light spell actions prepared for the supplied caster.
        /// </summary>
        public static void AddSpellActions(GameObject caster)
        {
            CreatureComponent creature =
                caster != null ? caster.GetComponent<CreatureComponent>() : null;
            ActionController controller =
                caster != null ? caster.GetComponent<ActionController>() : null;
#pragma warning disable CS0618 // Intentional legacy bootstrap until each remaining spell migrates.
            SpellcastingState state = creature?.Prepared?.Spellcasting;
#pragma warning restore CS0618
            if (controller == null || state == null)
                return;

            HashSet<string> existing = controller
                .GetActions()
                .Select(action => action.ActionName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (PreparedSpell preparedSpell in state.PreparedSpells)
            {
#pragma warning disable CS0618 // Intentional registry lookup for legacy-only action installation.
                bool implemented = SpellRegistry.TryGet(
                    preparedSpell.Slug,
                    out ISpellDefinition spellDefinition
                );
#pragma warning restore CS0618
                if (!implemented)
                    continue;

                foreach (uint cost in spellDefinition.GetActionCosts(preparedSpell))
                {
                    CastSpellAction action = new(preparedSpell, cost, spellDefinition);
                    if (!existing.Contains(action.ActionName))
                    {
                        controller.AddAction(action);
                        existing.Add(action.ActionName);
                        if (!creature.actions.Contains(action.ActionName))
                            creature.actions.Add(action.ActionName);
                    }
                }
            }
        }

        /// <summary>Executes this legacy action against an already selected target payload.</summary>
        public CastSpellResult Cast(
            GameObject caster,
            IReadOnlyList<GameObject> targets = null,
            GridPublic.AreaTargetResult area = null
        )
        {
#pragma warning disable CS0618 // The deprecated action intentionally delegates to its legacy runtime.
            return SpellcastingRuntime.Cast(
                caster,
                spell,
                variantActionCost,
                targets,
                area,
                spendActions: true
            );
#pragma warning restore CS0618
        }

        /// <inheritdoc/>
        protected override IEnumerator MFInvoke(GameObject caster)
        {
            ActionController actionController = caster.GetComponent<ActionController>();
            if (definition == null)
            {
                if (actionController != null)
                    actionController.IsTakingAction = false;
                yield break;
            }

            SpellCastContext context = new(
                caster,
                spell,
                variantActionCost,
                spendActions: true,
                definition
            );
            yield return definition.SelectAndCast(context);
        }

        private static string BuildActionName(PreparedSpell spell, uint actionCost)
        {
            if (spell == null)
                return "Cast Spell";
            if (spell.ActionCosts.Count > 1)
                return spell.Name + " " + actionCost + "A";
            return spell.Name;
        }
    }
}
