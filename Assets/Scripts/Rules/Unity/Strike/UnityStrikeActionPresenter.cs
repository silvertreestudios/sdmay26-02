using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity.Attack;
using Game.Rules.Unity.Vfx;
using UnityEngine;

namespace Game.Rules.Unity.Strike
{
    /// <summary>Supplies immutable Strike presentation data without exposing rules resolution.</summary>
    public interface IStrikePresentationCatalog
    {
        /// <summary>Gets the committed Strike item selected by an operation.</summary>
        StrikeItemDefinition GetStrikeItem(ItemId item);

        /// <summary>Gets the optional Unity weapon used only to choose an animation.</summary>
        bool TryGetWeapon(ItemId item, out EquipmentWeapon weapon);
    }

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
        private readonly Func<CreatureId, GameObject> getAttacker;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly IStrikePresentationCatalog strikeContext;
        private readonly UnityVfxPlayback vfx;

        /// <summary>Creates a presenter over explicit encounter identity mappings.</summary>
        /// <param name="controllers">Rules-to-Unity attacker mappings.</param>
        /// <param name="creatures">Rules-to-Unity creature mappings.</param>
        /// <param name="strikeContext">The feature context owning item and weapon mappings.</param>
        public UnityStrikeActionPresenter(
            IReadOnlyDictionary<CreatureId, ActionController> controllers,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityStrikeContext strikeContext,
            UnityVfxPlayback vfx
        )
            : this(CreateLiveAttackerResolver(controllers), creatures, strikeContext, vfx) { }

        /// <summary>
        /// Creates a presenter over explicit visual actors and immutable item presentation data.
        /// </summary>
        /// <remarks>
        /// Deterministic review fixtures use this boundary to exercise the production presenter
        /// without constructing a second rules authority or a live encounter controller.
        /// </remarks>
        public UnityStrikeActionPresenter(
            IReadOnlyDictionary<CreatureId, GameObject> attackers,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            IStrikePresentationCatalog strikeContext,
            UnityVfxPlayback vfx
        )
            : this(CreateAttackerResolver(attackers), creatures, strikeContext, vfx) { }

        private UnityStrikeActionPresenter(
            Func<CreatureId, GameObject> getAttacker,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            IStrikePresentationCatalog strikeContext,
            UnityVfxPlayback vfx
        )
        {
            this.getAttacker = getAttacker ?? throw new ArgumentNullException(nameof(getAttacker));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.strikeContext =
                strikeContext ?? throw new ArgumentNullException(nameof(strikeContext));
            this.vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
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

            IEnumerator travel = vfx.PlayTransient(
                StrikeVfxCueSelector.GetTravel(item.Label),
                attacker.transform.position + Vector3.up * 0.6f,
                target.transform.position + Vector3.up * 0.6f,
                lifetimeOwner: attacker.transform
            );
            using (travel as IDisposable)
            {
                while (travel.MoveNext())
                    yield return travel.Current;
            }

            if (StrikeVfxCueSelector.TryGetImpact(item.Label, result.Degree, out VfxCueId impact))
            {
                float intensity =
                    result.Degree == Game.Rules.Runtime.DegreeOfSuccess.CriticalSuccess ? 1.5f : 1f;
                IEnumerator impactPlayback = vfx.PlayTransient(
                    impact,
                    target.transform.position + Vector3.up * 0.6f,
                    target.transform.position + Vector3.up * 0.6f,
                    intensity,
                    target.transform
                );
                using (impactPlayback as IDisposable)
                {
                    while (impactPlayback.MoveNext())
                        yield return impactPlayback.Current;
                }
                foreach (VfxCueId accent in StrikeVfxCueSelector.GetContributionAccents(result))
                {
                    IEnumerator accentPlayback = vfx.PlayTransient(
                        accent,
                        target.transform.position + Vector3.up * 0.6f,
                        target.transform.position + Vector3.up * 0.6f,
                        lifetimeOwner: target.transform
                    );
                    using (accentPlayback as IDisposable)
                    {
                        while (accentPlayback.MoveNext())
                            yield return accentPlayback.Current;
                    }
                }
            }

            yield return UnityActionPresentationCoordinator.ReactionBarrier;
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
        }

        private bool TryGetPresentation(
            CreatureId actor,
            CreatureId target,
            out GameObject attackerObject,
            out GameObject targetObject,
            out CreatureComponent defender
        )
        {
            GameObject attacker = getAttacker(actor);
            if (attacker != null && creatures.TryGetValue(target, out defender) && defender != null)
            {
                attackerObject = attacker;
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

        private static Func<CreatureId, GameObject> CreateLiveAttackerResolver(
            IReadOnlyDictionary<CreatureId, ActionController> controllers
        )
        {
            if (controllers == null)
                throw new ArgumentNullException(nameof(controllers));
            return actor =>
                controllers.TryGetValue(actor, out ActionController controller)
                && controller != null
                    ? controller.gameObject
                    : null;
        }

        private static Func<CreatureId, GameObject> CreateAttackerResolver(
            IReadOnlyDictionary<CreatureId, GameObject> attackers
        )
        {
            if (attackers == null)
                throw new ArgumentNullException(nameof(attackers));
            return actor => attackers.TryGetValue(actor, out GameObject attacker) ? attacker : null;
        }

        private static IEnumerable<UnityAttackDamagePart> ToDamage(StrikeResolution resolution)
        {
            foreach (TypedDamagePart part in resolution.Damage)
                yield return new UnityAttackDamagePart(part.DamageType, part.Amount);
        }
    }
}
