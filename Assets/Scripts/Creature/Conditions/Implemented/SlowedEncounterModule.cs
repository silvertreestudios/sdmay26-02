using System;
using System.Linq;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Seeds the authored passive Slowed identity for later resource integration.</summary>
    internal sealed class SlowedEncounterModule : IUnityCombatantEnrollmentModule
    {
        private const string AuthoredPassiveName = "Slow";
        private readonly UnityCombatRulesBridge owner;

        internal SlowedEncounterModule(UnityCombatRulesBridge owner) =>
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            int matches = builder.Creature.passives.Count(passive =>
                string.Equals(passive, AuthoredPassiveName, StringComparison.OrdinalIgnoreCase)
            );
            if (matches == 0)
                return;
            if (matches != 1)
                throw new InvalidOperationException(
                    "The authored Slow passive must occur exactly once."
                );

            AuthoredPassiveSlowedIdentity identity = AuthoredPassiveSlowedIdentity.Create(
                builder.CreatureId,
                owner.GetDurableActorId(builder.CreatureId)
            );
            builder.AddActiveEffects(new[] { identity.Registration });
        }
    }

    /// <summary>
    /// Owns the one stable registration identity reserved for an authored passive Slowed 1.
    /// </summary>
    internal sealed class AuthoredPassiveSlowedIdentity
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("authored-passive-slow");

        private AuthoredPassiveSlowedIdentity(ActiveEffectRegistration registration) =>
            Registration = registration;

        internal ActiveEffectRegistration Registration { get; }

        internal static AuthoredPassiveSlowedIdentity Create(CreatureId owner, string durableOwner)
        {
            string stableOwner = string.IsNullOrEmpty(durableOwner)
                ? owner.Value
                : DurableActorSourceIdentity.Reserve(durableOwner).Value;
            string identity = $"authored-passive-slow-{stableOwner}";
            ActiveEffectId effectId = new($"{identity}-effect");
            BindingId bindingId = new($"{identity}-binding");
            ActiveEffectInstance effect = new(
                effectId,
                ConditionRuleDefinitions.Slowed,
                owner,
                Source,
                EffectDuration.Indefinite,
                new SlowedConditionState(1)
            );
            ActiveRuleBinding binding = new(
                bindingId,
                effect.DefinitionId,
                owner,
                effectId,
                Source,
                0
            );
            return new AuthoredPassiveSlowedIdentity(new ActiveEffectRegistration(effect, binding));
        }
    }
}
