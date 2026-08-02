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
        // The encounter turn-start adapter is the sole mutation root. Aura calculation and
        // presentation remain feature-owned while health commits through the active dispatcher.
        internal static async ValueTask<List<CreatureAuraEffectResult>> ApplyTurnStartAurasAwaited(
            ActionController acting,
            IEnumerable<ActionController> combatants,
            Tile[,] tiles,
            Func<
                CreatureComponent,
                IReadOnlyList<CreatureAuraEffectResult>,
                ValueTask<IReadOnlyList<DamageOutcome>>
            > commitDamageBatch,
            Func<CreatureComponent, HealthState> getAuthoritativeHealth,
            Func<CreatureAuraEffectResult, ValueTask> presentResult,
            IPf2eDiceRoller diceRoller = null
        )
        {
            if (commitDamageBatch == null)
                throw new ArgumentNullException(nameof(commitDamageBatch));
            if (getAuthoritativeHealth == null)
                throw new ArgumentNullException(nameof(getAuthoritativeHealth));
            if (presentResult == null)
                throw new ArgumentNullException(nameof(presentResult));
            List<CreatureAuraEffectResult> results = new();
            if (acting == null || combatants == null || tiles == null)
                return results;

            diceRoller ??= new UnityPf2eDiceRoller();
            CreatureComponent targetCreature = acting.GetComponent<CreatureComponent>();
            HealthState projectedHealth = getAuthoritativeHealth(targetCreature);
            int projectedCurrent = projectedHealth.Current;
            int projectedTemporary = projectedHealth.Temporary;
            List<DamageOutcome> projectedOutcomes = new();
            foreach (CreatureAuraInstance instance in GetActiveAuras(combatants))
            {
                if (projectedCurrent == 0)
                    break;
                if (instance.Rule.Timing != CreatureAuraTiming.TurnStart)
                    continue;
                AreaTargetResult area = CreatureAuraArea.EvaluateEmanation(
                    instance.SourceObject,
                    instance.Aura,
                    tiles
                );
                if (!CreatureAuraArea.AffectsCreature(area, acting.gameObject))
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
                if (instance.Rule is not RottingAuraRule rottingAura)
                    throw new InvalidOperationException(
                        $"Aura rule '{instance.Rule.Slug}' has no encounter resolver."
                    );
                CreatureAuraEffectResult result = rottingAura.Resolve(context);
                results.Add(result);
                int requested = Math.Max(0, result.AppliedDamage);
                int appliedToTemporary = Math.Min(projectedTemporary, requested);
                int appliedToCurrent = Math.Min(projectedCurrent, requested - appliedToTemporary);
                projectedOutcomes.Add(
                    new DamageOutcome(requested, appliedToTemporary, appliedToCurrent)
                );
                projectedTemporary -= appliedToTemporary;
                projectedCurrent -= appliedToCurrent;
            }
            if (results.Count == 0)
                return results;

            CreatureComponent target = acting.GetComponent<CreatureComponent>();
            IReadOnlyList<DamageOutcome> outcomes = await commitDamageBatch(target, results);
            if (outcomes == null || outcomes.Count != results.Count)
                throw new InvalidOperationException(
                    "The committed aura damage batch did not return one outcome per result."
                );

            List<CreatureAuraEffectResult> presented = new List<CreatureAuraEffectResult>(
                results.Count
            );
            for (int index = 0; index < results.Count; index++)
            {
                CreatureAuraEffectResult result = results[index];
                DamageOutcome outcome = outcomes[index];
                DamageOutcome projected = projectedOutcomes[index];
                if (
                    outcome.Requested != projected.Requested
                    || outcome.AppliedToTemporary != projected.AppliedToTemporary
                    || outcome.AppliedToCurrent != projected.AppliedToCurrent
                )
                    throw new InvalidOperationException(
                        "The committed aura damage outcome does not match its calculated result."
                    );
                await presentResult(result);
                presented.Add(result);
            }
            return presented;
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
