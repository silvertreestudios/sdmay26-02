using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Creature.Rules;
using UnityEngine;

namespace Game.Combat.Spells
{
    [Serializable]
    public class CastSpellAction : MultiFrameEntityAction
    {
        private readonly PreparedSpell spell;
        private readonly uint variantActionCost;
        private readonly ISpellDefinition definition;

        public PreparedSpell Spell => spell;
        public override string ActionName => BuildActionName(spell, variantActionCost);

        public CastSpellAction(PreparedSpell spell, uint actionCost, ISpellDefinition definition)
            : base(actionCost)
        {
            this.spell = spell;
            variantActionCost = actionCost;
            this.definition = definition;
        }

        public static void AddSpellActions(GameObject caster)
        {
            CreatureComponent creature =
                caster != null ? caster.GetComponent<CreatureComponent>() : null;
            ActionController controller =
                caster != null ? caster.GetComponent<ActionController>() : null;
            SpellcastingState state = creature?.Prepared?.Spellcasting;
            if (controller == null || state == null)
                return;

            HashSet<string> existing = controller
                .GetActions()
                .Select(action => action.ActionName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (PreparedSpell preparedSpell in state.PreparedSpells)
            {
                if (!SpellRegistry.TryGet(preparedSpell.Slug, out ISpellDefinition spellDefinition))
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

        /// <summary>Casts this prepared action variant and awaits effects, costs, and MAP.</summary>
        /// <param name="caster">The creature invoking this prepared spell action.</param>
        /// <param name="targets">Optional already-selected direct targets.</param>
        /// <param name="area">Optional already-selected area targeting result.</param>
        /// <returns>The complete cast result after the causal root settles.</returns>
        public ValueTask<CastSpellResult> CastAsync(
            GameObject caster,
            IReadOnlyList<GameObject> targets = null,
            GridPublic.AreaTargetResult area = null
        )
        {
            return SpellcastingRuntime.CastAsync(
                caster,
                spell,
                variantActionCost,
                targets,
                area,
                spendActions: true
            );
        }

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
                definition,
                actionReservationAlreadyOwned: true
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
