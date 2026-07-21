using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules;
using UnityEngine;

namespace Game.Combat.Spells
{
    public abstract class ActiveSpellEffect
    {
        protected ActiveSpellEffect(
            GameObject source,
            string sourceLabel,
            int remainingTargetTurnStarts = 0,
            bool expiresAtSourceTurnStart = false
        )
        {
            Source = source;
            SourceLabel = sourceLabel ?? string.Empty;
            RemainingTargetTurnStarts = remainingTargetTurnStarts;
            ExpiresAtSourceTurnStart = expiresAtSourceTurnStart;
        }

        public GameObject Source { get; private set; }
        public string SourceLabel { get; private set; }
        public int RemainingTargetTurnStarts { get; set; }
        public bool ExpiresAtSourceTurnStart { get; private set; }
        public bool Consumed { get; protected set; }

        /// <summary>
        /// Gets the stable effect-instance identity after its first persistence capture or restore.
        /// </summary>
        public string PersistentInstanceId { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the monotonic per-target binding order used to preserve deterministic effect order.
        /// </summary>
        public long BindingCreationOrder { get; private set; } = -1;

        /// <summary>
        /// Gets the stable source actor identity retained when a defeated source is not
        /// materialized after loading.
        /// </summary>
        public string PersistentSourceActorId { get; private set; } = string.Empty;
        public virtual bool ExpiresWhenTargetTurnCounterReachesZero => false;

        /// <summary>Restores mutable duration and consumption state after source actors resolve.</summary>
        /// <param name="remainingTargetTurnStarts">The non-negative target-turn starts remaining.</param>
        /// <param name="consumed">Whether this effect was consumed but not yet pruned.</param>
        /// <param name="persistentSourceActorId">
        /// Stable source actor identity, including when <see cref="Source"/> is absent.
        /// </param>
        /// <param name="persistentInstanceId">
        /// Stable effect-instance identity, or empty only for a new effect that has not been captured.
        /// </param>
        /// <param name="bindingCreationOrder">
        /// Monotonic per-target binding order, or -1 only for a new effect.
        /// </param>
        public void RestorePersistenceState(
            int remainingTargetTurnStarts,
            bool consumed,
            string persistentSourceActorId = "",
            string persistentInstanceId = "",
            long bindingCreationOrder = -1
        )
        {
            if (remainingTargetTurnStarts < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingTargetTurnStarts));
            if (bindingCreationOrder < -1)
                throw new ArgumentOutOfRangeException(nameof(bindingCreationOrder));
            string normalizedInstanceId = persistentInstanceId?.Trim() ?? string.Empty;
            if ((normalizedInstanceId.Length == 0) != (bindingCreationOrder < 0))
                throw new ArgumentException(
                    "A restored effect identity and binding order must be supplied together."
                );
            RemainingTargetTurnStarts = remainingTargetTurnStarts;
            Consumed = consumed;
            PersistentSourceActorId = persistentSourceActorId?.Trim() ?? string.Empty;
            if (normalizedInstanceId.Length > 0)
                EnsurePersistenceIdentity(normalizedInstanceId, bindingCreationOrder);
        }

        internal void EnsureBindingCreationOrder(long bindingCreationOrder)
        {
            if (bindingCreationOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(bindingCreationOrder));
            if (BindingCreationOrder >= 0 && BindingCreationOrder != bindingCreationOrder)
                throw new InvalidOperationException(
                    "A timed-effect binding order cannot be replaced."
                );
            BindingCreationOrder = bindingCreationOrder;
        }

        internal void EnsurePersistenceIdentity(string instanceId, long bindingCreationOrder)
        {
            string normalized = instanceId?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException(
                    "A timed-effect persistence identity is required.",
                    nameof(instanceId)
                );
            if (PersistentInstanceId.Length > 0 && PersistentInstanceId != normalized)
                throw new InvalidOperationException(
                    "A timed-effect persistence identity cannot be replaced."
                );
            EnsureBindingCreationOrder(bindingCreationOrder);
            PersistentInstanceId = normalized;
        }

        public bool Matches(ActiveSpellEffect other)
        {
            return other != null
                && GetType() == other.GetType()
                && (
                    (Source != null && other.Source != null && Source == other.Source)
                    || (
                        PersistentSourceActorId.Length > 0
                        && other.PersistentSourceActorId.Length > 0
                        && string.Equals(
                            PersistentSourceActorId,
                            other.PersistentSourceActorId,
                            StringComparison.Ordinal
                        )
                    )
                    || (
                        Source == null
                        && other.Source == null
                        && PersistentSourceActorId.Length == 0
                        && other.PersistentSourceActorId.Length == 0
                    )
                )
                && string.Equals(
                    SourceLabel,
                    other.SourceLabel,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public void RefreshFrom(ActiveSpellEffect other)
        {
            Source = other.Source;
            SourceLabel = other.SourceLabel;
            RemainingTargetTurnStarts = other.RemainingTargetTurnStarts;
            ExpiresAtSourceTurnStart = other.ExpiresAtSourceTurnStart;
            if (other.PersistentSourceActorId.Length > 0)
                PersistentSourceActorId = other.PersistentSourceActorId;
            Consumed = false;
        }

        public virtual IEnumerable<Pf2eModifier> GetModifiers(
            Pf2eStatistic statistic,
            SpellEffectController owner
        )
        {
            yield break;
        }

        public virtual IEnumerable<IStrikeAdjustment> GetStrikeAdjustments(
            StrikeResolutionContext context,
            SpellEffectController owner
        )
        {
            yield break;
        }
    }

    public sealed class ShieldSpellEffect : ActiveSpellEffect
    {
        public ShieldSpellEffect(GameObject source)
            : base(source, "Shield", expiresAtSourceTurnStart: true) { }

        public override IEnumerable<Pf2eModifier> GetModifiers(
            Pf2eStatistic statistic,
            SpellEffectController owner
        )
        {
            if (statistic == Pf2eStatistic.ArmorClass)
                yield return new Pf2eModifier(
                    1,
                    Pf2eModifierType.Circumstance,
                    "Shield",
                    statistic
                );
        }
    }

    public sealed class GuidanceSpellEffect : ActiveSpellEffect
    {
        public GuidanceSpellEffect(GameObject source)
            : base(source, "Guidance", expiresAtSourceTurnStart: true) { }

        public override IEnumerable<Pf2eModifier> GetModifiers(
            Pf2eStatistic statistic,
            SpellEffectController owner
        )
        {
            if (Consumed || !IsGuidanceStatistic(statistic))
                yield break;

            Consumed = true;
            owner.AddOrRefresh(new GuidanceImmunitySpellEffect(owner.gameObject));
            yield return new Pf2eModifier(1, Pf2eModifierType.Status, "Guidance", statistic);
        }

        private static bool IsGuidanceStatistic(Pf2eStatistic statistic)
        {
            return statistic == Pf2eStatistic.AttackRoll
                || statistic == Pf2eStatistic.FortitudeSave
                || statistic == Pf2eStatistic.ReflexSave
                || statistic == Pf2eStatistic.WillSave
                || statistic == Pf2eStatistic.SkillCheck
                || statistic == Pf2eStatistic.Initiative;
        }
    }

    public sealed class GuidanceImmunitySpellEffect : ActiveSpellEffect
    {
        public GuidanceImmunitySpellEffect(GameObject source)
            : base(source, "Guidance Immunity") { }
    }

    public sealed class BlessSpellEffect : ActiveSpellEffect
    {
        public BlessSpellEffect(GameObject source)
            : base(source, "Bless", remainingTargetTurnStarts: 10) { }

        public override bool ExpiresWhenTargetTurnCounterReachesZero => true;

        public override IEnumerable<Pf2eModifier> GetModifiers(
            Pf2eStatistic statistic,
            SpellEffectController owner
        )
        {
            if (statistic == Pf2eStatistic.AttackRoll)
                yield return new Pf2eModifier(1, Pf2eModifierType.Status, "Bless", statistic);
        }
    }

    public sealed class InfuseVitalitySpellEffect : ActiveSpellEffect
    {
        public InfuseVitalitySpellEffect(GameObject source)
            : base(source, "Infuse Vitality", remainingTargetTurnStarts: 10) { }

        public override bool ExpiresWhenTargetTurnCounterReachesZero => true;

        public override IEnumerable<IStrikeAdjustment> GetStrikeAdjustments(
            StrikeResolutionContext context,
            SpellEffectController owner
        )
        {
            if (
                context?.AttackerObject == owner.gameObject
                && context.Profile != null
                && IsWeaponOrUnarmedStrike(context.Profile)
            )
                yield return new InfuseVitalityStrikeAdjustment();
        }

        private static bool IsWeaponOrUnarmedStrike(StrikeProfile strike)
        {
            return string.Equals(
                    strike.WeaponCategory,
                    "unarmed",
                    StringComparison.OrdinalIgnoreCase
                ) || !string.IsNullOrWhiteSpace(strike.WeaponCategory);
        }
    }

    public class SpellEffectController
        : MonoBehaviour,
            IPf2eModifierProvider,
            IStrikeAdjustmentProvider
    {
        private readonly List<ActiveSpellEffect> effects = new();
        private static readonly List<SpellEffectController> instances = new();
        private long nextBindingCreationOrder;

        public IReadOnlyList<ActiveSpellEffect> Effects => effects;

        private void OnEnable()
        {
            if (!instances.Contains(this))
                instances.Add(this);
        }

        private void OnDisable()
        {
            instances.Remove(this);
        }

        public static SpellEffectController GetOrAdd(GameObject target)
        {
            if (target == null)
                return null;
            SpellEffectController controller = target.GetComponent<SpellEffectController>();
            return controller != null ? controller : target.AddComponent<SpellEffectController>();
        }

        public static void ExpireAtStartOfTurn(GameObject creature)
        {
            SpellEffectController ownController = null;
            if (creature != null && creature.TryGetComponent(out ownController))
                ownController.ExpireForTurnStart(creature);

            foreach (SpellEffectController controller in instances.ToArray())
            {
                if (controller != null && controller != ownController)
                    controller.ExpireForTurnStart(creature);
            }
        }

        public void AddOrRefresh(ActiveSpellEffect effect)
        {
            if (effect == null)
                return;
            ActiveSpellEffect existing = effects.Find(effect.Matches);
            if (existing == null)
            {
                if (effect.BindingCreationOrder < 0)
                    effect.EnsureBindingCreationOrder(nextBindingCreationOrder);
                nextBindingCreationOrder = Math.Max(
                    nextBindingCreationOrder,
                    checked(effect.BindingCreationOrder + 1)
                );
                effects.Add(effect);
            }
            else
                existing.RefreshFrom(effect);
        }

        /// <summary>
        /// Atomically replaces all legacy timed effects after their source GameObjects have been
        /// resolved from stable actor identities.
        /// </summary>
        /// <param name="restoredEffects">Complete, source-resolved effect instances.</param>
        public void RestorePersistentEffects(IEnumerable<ActiveSpellEffect> restoredEffects)
        {
            if (restoredEffects == null)
                throw new ArgumentNullException(nameof(restoredEffects));
            ActiveSpellEffect[] copied = new List<ActiveSpellEffect>(restoredEffects).ToArray();
            if (Array.Exists(copied, effect => effect == null))
                throw new ArgumentException(
                    "Restored spell effects cannot contain null.",
                    nameof(restoredEffects)
                );
            for (int left = 0; left < copied.Length; left++)
            {
                if (
                    copied[left].PersistentInstanceId.Length == 0
                    || copied[left].BindingCreationOrder < 0
                )
                    throw new ArgumentException(
                        "Restored spell effects require stable instance identities and binding orders.",
                        nameof(restoredEffects)
                    );
                for (int right = left + 1; right < copied.Length; right++)
                {
                    if (copied[left].Matches(copied[right]))
                        throw new ArgumentException(
                            "Restored spell effects cannot contain duplicate kind/source pairs.",
                            nameof(restoredEffects)
                        );
                    if (
                        copied[left].PersistentInstanceId == copied[right].PersistentInstanceId
                        || copied[left].BindingCreationOrder == copied[right].BindingCreationOrder
                    )
                        throw new ArgumentException(
                            "Restored spell-effect persistence identities and binding orders must be unique.",
                            nameof(restoredEffects)
                        );
                }
            }

            effects.Clear();
            effects.AddRange(copied);
            nextBindingCreationOrder =
                copied.Length == 0
                    ? 0
                    : checked(copied.Max(effect => effect.BindingCreationOrder) + 1);
        }

        public bool HasEffect<T>()
            where T : ActiveSpellEffect
        {
            return effects.Exists(effect => effect is T && !effect.Consumed);
        }

        public IEnumerable<Pf2eModifier> GetModifiers(Pf2eStatistic statistic)
        {
            foreach (ActiveSpellEffect effect in effects.ToArray())
            {
                foreach (Pf2eModifier modifier in effect.GetModifiers(statistic, this))
                    yield return modifier;
            }
            effects.RemoveAll(effect => effect.Consumed);
        }

        public IEnumerable<IStrikeAdjustment> GetStrikeAdjustments(StrikeResolutionContext context)
        {
            foreach (ActiveSpellEffect effect in effects.ToArray())
            {
                foreach (IStrikeAdjustment adjustment in effect.GetStrikeAdjustments(context, this))
                    yield return adjustment;
            }
        }

        private void ExpireForTurnStart(GameObject creature)
        {
            effects.RemoveAll(effect =>
                effect.ExpiresAtSourceTurnStart && effect.Source == creature
            );
            if (creature == gameObject)
            {
                foreach (ActiveSpellEffect effect in effects)
                {
                    if (effect.RemainingTargetTurnStarts > 0)
                        effect.RemainingTargetTurnStarts--;
                }
                effects.RemoveAll(effect =>
                    effect.RemainingTargetTurnStarts == 0
                    && effect.ExpiresWhenTargetTurnCounterReachesZero
                );
            }
        }
    }

    public sealed class InfuseVitalityStrikeAdjustment : StrikeAdjustmentBase
    {
        public InfuseVitalityStrikeAdjustment()
            : base(StrikeAdjustmentPhase.BeforeDamageRoll, 0, "Infuse Vitality") { }

        public override void Apply(StrikeResolutionContext context)
        {
            context.DamageDice.Add(new Dice(1, 4, "vitality"));
            context.LogDetails.Add(new CombatLogDetail("Infuse Vitality", "+1d4 vitality"));
        }
    }
}
