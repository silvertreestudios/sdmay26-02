using System;
using System.Collections.Generic;
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
        internal string PersistentSourceActorId { get; private set; } = string.Empty;
        public virtual bool ExpiresWhenTargetTurnCounterReachesZero => false;

        public bool Matches(ActiveSpellEffect other)
        {
            return other != null
                && GetType() == other.GetType()
                && Source == other.Source
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
            Consumed = false;
        }

        internal void RestorePersistentSource(string sourceActorId, GameObject source)
        {
            string normalized = sourceActorId?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException(
                    "A timed effect source identity is required.",
                    nameof(sourceActorId)
                );
            Source = source;
            PersistentSourceActorId = normalized;
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
            ActionController actionController =
                creature != null ? creature.GetComponent<ActionController>() : null;
            if (actionController != null && actionController.TryGetCombatRules(out _, out _))
                throw new InvalidOperationException(
                    "Encounter spell-effect expiry requires typed active-effect timing."
                );

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
            ActionController actionController = GetComponent<ActionController>();
            if (actionController != null && actionController.TryGetCombatRules(out _, out _))
                throw new InvalidOperationException(
                    "Encounter spell effects require typed active-effect operations."
                );
            ActiveSpellEffect existing = effects.Find(effect.Matches);
            if (existing == null)
                effects.Add(effect);
            else
                existing.RefreshFrom(effect);
        }

        internal void RestoreEffects(IEnumerable<ActiveSpellEffect> restoredEffects)
        {
            if (restoredEffects == null)
                throw new ArgumentNullException(nameof(restoredEffects));
            ActiveSpellEffect[] copied = new List<ActiveSpellEffect>(restoredEffects).ToArray();
            if (Array.Exists(copied, effect => effect == null || effect.Consumed))
                throw new ArgumentException(
                    "Restored spell effects must be active.",
                    nameof(restoredEffects)
                );

            effects.Clear();
            effects.AddRange(copied);
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
