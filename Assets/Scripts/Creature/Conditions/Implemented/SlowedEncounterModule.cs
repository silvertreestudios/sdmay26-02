using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Projects authoritative Slowed state into the transitional turn-start contribution.</summary>
    internal sealed class SlowedEncounterModule
        : IUnityEncounterTurnStartModule,
            IUnityCombatantEnrollmentModule
    {
        private const string AuthoredPassiveName = "Slow";
        private readonly UnityCombatRulesBridge owner;
        private readonly Dictionary<CreatureId, AuthoredPassiveSlowedIdentity> authoredPassives =
            new();

        internal SlowedEncounterModule(UnityCombatRulesBridge owner) =>
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

        /// <inheritdoc/>
        public IEncounterTurnStartAdapter CreateTurnStartAdapter() =>
            new TurnStartAdapter(authoredPassives);

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
            if (!authoredPassives.TryAdd(builder.CreatureId, identity))
                throw new InvalidOperationException(
                    "The authored Slow passive was prepared more than once for one combatant."
                );
            builder.Own(new RegistrationToken(() => authoredPassives.Remove(builder.CreatureId)));
            builder.AddActiveEffects(new[] { identity.Registration });
        }

        private sealed class TurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly IReadOnlyDictionary<
                CreatureId,
                AuthoredPassiveSlowedIdentity
            > authoredPassives;

            internal TurnStartAdapter(
                IReadOnlyDictionary<CreatureId, AuthoredPassiveSlowedIdentity> authoredPassives
            ) =>
                this.authoredPassives =
                    authoredPassives ?? throw new ArgumentNullException(nameof(authoredPassives));

            /// <inheritdoc/>
            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                int slowed = ConditionSelectors.TryGetSlowed(
                    context.Snapshot,
                    context.Actor,
                    out ConditionSelection<SlowedConditionState> selected
                )
                    ? selected.State.Value
                    : 0;
                bool suppressesReaction =
                    authoredPassives.TryGetValue(
                        context.Actor,
                        out AuthoredPassiveSlowedIdentity authored
                    ) && authored.IsExactActiveRegistration(context.Snapshot);
                return new ValueTask<TurnStartContribution>(
                    new TurnStartContribution(
                        Math.Max(0, current.Actions - slowed),
                        current.ReactionAvailable && !suppressesReaction
                    )
                );
            }
        }
    }

    /// <summary>
    /// Owns the one stable registration identity reserved for an authored passive Slowed 1.
    /// </summary>
    internal sealed class AuthoredPassiveSlowedIdentity
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("authored-passive-slow");

        private AuthoredPassiveSlowedIdentity(
            CreatureId owner,
            ActiveEffectRegistration registration
        )
        {
            Owner = owner;
            Registration = registration;
        }

        internal CreatureId Owner { get; }
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
            return new AuthoredPassiveSlowedIdentity(
                owner,
                new ActiveEffectRegistration(effect, binding)
            );
        }

        internal bool IsExactActiveRegistration(RulesSnapshot snapshot)
        {
            ActiveEffectInstance expectedEffect = Registration.Effect;
            ActiveRuleBinding expectedBinding = Registration.Binding;
            return snapshot.ActiveEffects.TryGet(expectedEffect.Id, out ActiveEffectInstance effect)
                && snapshot.RuleBindings.TryGet(expectedBinding.Id, out ActiveRuleBinding binding)
                && effect.Status == ActiveEffectStatus.Active
                && binding.IsEnabled
                && Owner == expectedBinding.Owner
                && Registration.HasSameStructure(new ActiveEffectRegistration(effect, binding));
        }
    }
}
