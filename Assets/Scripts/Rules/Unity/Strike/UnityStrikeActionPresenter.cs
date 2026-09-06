using System;
using System.Collections;
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
    /// events. Presenter exceptions flow to the action presentation coordinator, which logs the
    /// first failure, abandons the remaining action visuals, and releases the caller without
    /// affecting committed rules state.
    /// </remarks>
    public sealed class UnityStrikeActionPresenter
        : IUnityActionPresenter<StrikeActionOp, StrikeResolution>
    {
        private readonly IReadOnlyDictionary<CreatureId, ActionController> controllers;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly UnityStrikeContext strikeContext;

        /// <summary>Creates a presenter over explicit encounter identity mappings.</summary>
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
        public IEnumerator PresentBeginning(StrikeActionOp operation, RulesSnapshot currentSnapshot)
        {
            if (
                !TryGetPresentation(
                    operation.Actor,
                    operation.Target,
                    out GameObject attacker,
                    out GameObject target,
                    out _
                )
            )
                yield break;

            StrikeItemDefinition item = strikeContext.GetStrikeItem(operation.Item);
            if (CombatLog.TryGetInstance(out CombatLogInterface log))
                log.Log($"- {attacker.name} strikes {target.name} with {item.Label}.");
            CreatureAnimationController animation = null;
            bool animationStarted = PlayAttack(attacker, target, item, out animation);
            while (
                animationStarted
                && animation != null
                && animation.isActiveAndEnabled
                && animation.IsActionPlaying
            )
                yield return null;
        }

        /// <inheritdoc/>
        public IEnumerator PresentResolved(
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
                    out _
                )
            )
                yield break;

            StrikeItemDefinition item = strikeContext.GetStrikeItem(operation.Item);

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
            yield break;
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

        private bool PlayAttack(
            GameObject attacker,
            GameObject target,
            StrikeItemDefinition item,
            out CreatureAnimationController animation
        )
        {
            CreaturePresentation presentation = attacker.GetComponent<CreaturePresentation>();
            animation = presentation?.AnimationController;
            if (presentation == null || target == null)
                return false;
            if (strikeContext.TryGetWeapon(item.Item, out EquipmentWeapon weapon))
                return presentation.PlayAttack(weapon, target.transform.position);
            return presentation.PlayAttack(AnimationStyle.Unarmed, target.transform.position);
        }

        private static IEnumerable<UnityAttackDamagePart> ToDamage(StrikeResolution resolution)
        {
            foreach (TypedDamagePart part in resolution.Damage)
                yield return new UnityAttackDamagePart(part.DamageType, part.Amount);
        }
    }
}
