using System;
using System.Linq;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Seeds the authored zombie Slow condition and its separate no-reactions rule.</summary>
    internal sealed class SlowedEncounterModule
        : IUnityCombatantEnrollmentModule,
            IUnityEncounterTurnResourceModule,
            IUnityEncounterDispatcherModule
    {
        private const string AuthoredPassiveName = "Slow";
        internal static readonly RuleDefinitionId NoReactionsDefinitionId = new(
            "authored-passive-slow-no-reactions"
        );
        private static readonly RuleSource NoReactionsSource = RuleSource.FromSlug(
            "authored-passive-slow-no-reactions"
        );
        private readonly UnityCombatRulesBridge owner;

        internal SlowedEncounterModule(UnityCombatRulesBridge owner) =>
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

        internal static RuleRegistryBuilder DefineRules(RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            builder.Define(NoReactionsDefinitionId);
            return builder;
        }

        /// <summary>Creates the feature-owned permission that enforces No Reactions.</summary>
        internal static IActionPermission CreateNoReactionsActionPermission() =>
            new NoReactionsActionPermission();

        /// <inheritdoc/>
        public ITurnResourceContributionProvider CreateTurnResourceProvider() =>
            new NoReactionsTurnResourceProvider();

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.RegisterActionPermission(CreateNoReactionsActionPermission());

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
            builder.AddRuleBindings(
                new[] { CreateNoReactionsBinding(builder.CreatureId, identity.StableOwner) }
            );
        }

        private static ActiveRuleBinding CreateNoReactionsBinding(
            CreatureId actor,
            string stableOwner
        ) =>
            new(
                new BindingId($"authored-passive-slow-{stableOwner}-no-reactions-binding"),
                NoReactionsDefinitionId,
                actor,
                default,
                NoReactionsSource,
                0
            );

        private sealed class NoReactionsTurnResourceProvider : ITurnResourceContributionProvider
        {
            public TurnResourceContributionBatch GetContributions(
                RulesSnapshot snapshot,
                CreatureId actor
            )
            {
                bool active = snapshot.RuleBindings.Any(pair =>
                    pair.Value.Owner == actor
                    && pair.Value.DefinitionId == NoReactionsDefinitionId
                    && pair.Value.IsEnabled
                    && !pair.Value.EffectId.HasValue
                );
                return new TurnResourceContributionBatch(
                    active
                        ? new[] { TurnResourceContribution.SuppressReaction() }
                        : Array.Empty<TurnResourceContribution>()
                );
            }
        }

        private sealed class NoReactionsActionPermission : IActionPermission
        {
            public ActionValidationResult Validate(
                ActionOpInfo action,
                ActionProfile profile,
                RulesSnapshot snapshot
            )
            {
                if (profile.Cost.Kind != ActionCostKind.Reaction)
                    return ActionValidationResult.Valid;
                return snapshot.RuleBindings.Any(pair =>
                    pair.Value.Owner == action.Actor
                    && pair.Value.DefinitionId == NoReactionsDefinitionId
                    && pair.Value.IsEnabled
                    && !pair.Value.EffectId.HasValue
                )
                    ? ActionValidationResult.Invalid("The actor cannot use reactions.")
                    : ActionValidationResult.Valid;
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
            ActiveEffectRegistration registration,
            string stableOwner
        )
        {
            Registration = registration;
            StableOwner = stableOwner;
        }

        internal ActiveEffectRegistration Registration { get; }
        internal string StableOwner { get; }

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
                new ActiveEffectRegistration(effect, binding),
                stableOwner
            );
        }
    }
}
