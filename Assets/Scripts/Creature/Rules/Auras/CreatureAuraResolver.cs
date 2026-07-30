using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Creature.Rules
{
    public static class CreatureAuraResolver
    {
        /// <summary>
        /// Resolves the legacy synchronous aura path when no encounter dispatcher root owns the
        /// boundary.
        /// </summary>
        public static List<CreatureAuraEffectResult> ApplyTurnStartAuras(
            ActionController acting,
            IEnumerable<ActionController> combatants,
            Tile[,] tiles,
            IPf2eDiceRoller diceRoller = null
        ) =>
            ApplyTurnStartAurasAsync(acting, combatants, tiles, diceRoller)
                .AsTask()
                .GetAwaiter()
                .GetResult();

        /// <summary>Resolves turn-start auras and awaits every authoritative health mutation.</summary>
        /// <param name="acting">The creature whose initiative boundary was reached.</param>
        /// <param name="combatants">The encounter roster that may contribute auras.</param>
        /// <param name="tiles">The current grid used to evaluate aura emanations.</param>
        /// <param name="diceRoller">An optional deterministic dice source.</param>
        /// <returns>All aura effects applied in deterministic roster order.</returns>
        public static ValueTask<List<CreatureAuraEffectResult>> ApplyTurnStartAurasAsync(
            ActionController acting,
            IEnumerable<ActionController> combatants,
            Tile[,] tiles,
            IPf2eDiceRoller diceRoller = null
        ) =>
            ApplyTurnStartAurasAwaited(
                acting,
                combatants,
                tiles,
                (target, damage, source) =>
                    new ValueTask<DamageOutcome>(target.ApplyFinalDamage(damage, source)),
                target => target != null && target.hp > 0,
                result =>
                {
                    RottingAuraRule.Present(result);
                    return default;
                },
                diceRoller
            );

        // This adapter preserves the existing Unity aura calculation and presentation while its
        // health write remains a nested, awaited child of the encounter's active dispatcher root.
        // The aura vertical removes this seam when aura effects become native rules operations.
        internal static async ValueTask<List<CreatureAuraEffectResult>> ApplyTurnStartAurasAwaited(
            ActionController acting,
            IEnumerable<ActionController> combatants,
            Tile[,] tiles,
            Func<CreatureComponent, int, RuleSource, ValueTask<DamageOutcome>> applyDamage,
            Func<CreatureComponent, bool> canReceiveAura,
            Func<CreatureAuraEffectResult, ValueTask> presentResult,
            IPf2eDiceRoller diceRoller = null
        )
        {
            if (applyDamage == null)
                throw new ArgumentNullException(nameof(applyDamage));
            if (canReceiveAura == null)
                throw new ArgumentNullException(nameof(canReceiveAura));
            if (presentResult == null)
                throw new ArgumentNullException(nameof(presentResult));
            List<CreatureAuraEffectResult> results = new();
            if (acting == null || combatants == null || tiles == null)
                return results;

            diceRoller ??= new UnityPf2eDiceRoller();
            foreach (CreatureAuraInstance instance in GetActiveAuras(combatants))
            {
                if (instance.Rule.Timing != CreatureAuraTiming.TurnStart)
                    continue;
                AreaTargetResult area = CreatureAuraArea.EvaluateEmanation(
                    instance.SourceObject,
                    instance.Aura,
                    tiles
                );
                if (!CreatureAuraArea.AffectsCreature(area, acting.gameObject))
                    continue;

                CreatureComponent targetCreature = acting.GetComponent<CreatureComponent>();
                if (!canReceiveAura(targetCreature))
                    continue;
                CreatureAuraContext context = new(
                    instance.SourceController,
                    acting,
                    instance.SourceCreature,
                    targetCreature,
                    instance.Aura,
                    tiles,
                    area,
                    diceRoller
                );
                if (!instance.Rule.CanAffect(context))
                    continue;
                if (instance.Rule is RottingAuraRule rottingAura)
                {
                    CreatureAuraEffectResult result = rottingAura.Resolve(context);
                    await applyDamage(
                        targetCreature,
                        Math.Max(0, result.AppliedDamage),
                        RuleSource.FromSlug(RottingAuraRule.RuleSlug)
                    );
                    await presentResult(result);
                    results.Add(result);
                    continue;
                }
            }
            return results;
        }

        public static List<Vector3Int> GetAuraCells(
            IEnumerable<ActionController> combatants,
            Tile[,] tiles
        )
        {
            return CreatureAuraArea.GetCells(GetVisualAuras(combatants), tiles);
        }

        public static List<CreatureAuraInstance> GetVisualAuras(
            IEnumerable<ActionController> combatants
        )
        {
            List<CreatureAuraInstance> visualAuras = new();
            foreach (CreatureAuraInstance instance in GetActiveAuras(combatants))
            {
                if (instance.Rule.HasVisual(instance.Aura))
                    visualAuras.Add(instance);
            }
            return visualAuras;
        }

        private static IEnumerable<CreatureAuraInstance> GetActiveAuras(
            IEnumerable<ActionController> combatants
        )
        {
            if (combatants == null)
                yield break;

            foreach (ActionController controller in combatants)
            {
                GameObject sourceObject = controller == null ? null : controller.gameObject;
                CreatureComponent source =
                    sourceObject == null ? null : sourceObject.GetComponent<CreatureComponent>();
                if (source == null || source.auras == null || !sourceObject.activeInHierarchy)
                    continue;

                foreach (CreatureAura aura in source.auras)
                {
                    ICreatureAuraRule rule = DefinedAuras.TryGet(aura?.slug);
                    if (rule == null || aura.radiusFeet <= 0)
                        continue;

                    yield return new CreatureAuraInstance(controller, source, aura, rule);
                }
            }
        }
    }
}
