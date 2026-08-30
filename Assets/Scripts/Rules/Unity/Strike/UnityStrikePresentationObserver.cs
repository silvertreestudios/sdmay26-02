using System;
using System.Collections.Generic;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity.Attack;
using UnityEngine;

namespace Game.Rules.Unity.Strike
{
    /// <summary>
    /// Projects a committed resolved Strike action into Unity animation, events, and logs.
    /// </summary>
    /// <remarks>
    /// The adapter intentionally contains the Unity combat-log singleton and static creature
    /// events. Every cosmetic callback is exception-contained so presentation cannot prevent
    /// authoritative damage, load-state changes, or MAP advancement.
    /// </remarks>
    public sealed class UnityStrikeActionPresenter
        : IUnityActionPresenter<StrikeActionOp, StrikeResolution>
    {
        private readonly IReadOnlyDictionary<CreatureId, ActionController> controllers;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly UnityStrikeContext strikeContext;

        /// <summary>Creates an observer over explicit encounter identity mappings.</summary>
        /// <param name="controllers">Rules-to-Unity attacker mappings.</param>
        /// <param name="creatures">Rules-to-Unity creature mappings.</param>
        /// <param name="strikeContext">The feature context owning item and weapon mappings.</param>
        public UnityStrikeActionPresenter(
            IReadOnlyDictionary<CreatureId, ActionController> controllers,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityStrikeContext strikeContext
        )
        {
            this.controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.strikeContext =
                strikeContext ?? throw new ArgumentNullException(nameof(strikeContext));
        }

        /// <inheritdoc/>
        public void Present(
            StrikeActionOp operation,
            StrikeResolution result,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                !TryGetPresentation(
                    operation.Actor,
                    operation.Target,
                    out GameObject attacker,
                    out GameObject target,
                    out CreatureComponent defender
                )
            )
                return;

            StrikeItemDefinition item;
            try
            {
                item = strikeContext.GetStrikeItem(operation.Item);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, attacker);
                return;
            }

            PresentSafely(
                () =>
                {
                    if (CombatLog.TryGetInstance(out CombatLogInterface log))
                        log.Log($"- {attacker.name} strikes {target.name} with {item.Label}.");
                },
                attacker
            );
            PresentSafely(() => PlayAttack(attacker, target, item), attacker);

            UnityAttackResultPresentation.Present(
                attacker,
                target,
                item.Label,
                new UnityAttackResult(
                    result.AttackRoll,
                    result.AttackModifier,
                    result.ArmorClass,
                    result.Degree,
                    ToDamage(result),
                    result.FinalDamage,
                    result.MultipleAttackPenalty,
                    result.RangePenalty,
                    result.CoverBonus
                )
            );
            if (currentSnapshot.Health.TryGet(operation.Target, out HealthState health))
                UnityAttackResultPresentation.PresentTargetReaction(
                    defender,
                    result.FinalDamage,
                    health
                );
        }

        private bool TryGetPresentation(
            CreatureId actor,
            CreatureId target,
            out GameObject attackerObject,
            out GameObject targetObject,
            out CreatureComponent defender
        )
        {
            if (
                controllers.TryGetValue(actor, out ActionController attacker)
                && attacker != null
                && creatures.TryGetValue(target, out defender)
                && defender != null
            )
            {
                attackerObject = attacker.gameObject;
                targetObject = defender.gameObject;
                return true;
            }

            attackerObject = null;
            targetObject = null;
            defender = null;
            return false;
        }

        private void PlayAttack(GameObject attacker, GameObject target, StrikeItemDefinition item)
        {
            CreaturePresentation presentation = attacker.GetComponent<CreaturePresentation>();
            if (strikeContext.TryGetWeapon(item.Item, out EquipmentWeapon weapon))
                presentation?.PlayAttack(weapon, target.transform.position);
            else
                presentation?.PlayAttack(AnimationStyle.Unarmed, target.transform.position);
        }

        private static IEnumerable<UnityAttackDamagePart> ToDamage(StrikeResolution resolution)
        {
            foreach (TypedDamagePart part in resolution.Damage)
                yield return new UnityAttackDamagePart(part.DamageType, part.Amount);
        }

        private static void PresentSafely(Action presentation, UnityEngine.Object context)
        {
            try
            {
                presentation();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, context);
            }
        }
    }
}
