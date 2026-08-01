using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Composes condition workflows and immutable persistence enrollment.</summary>
    internal sealed class ConditionEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly RuleRegistry registry;

        internal ConditionEncounterModule(RuleRegistry registry) =>
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseConditionRules(registry);

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            List<ConditionRegistration> registrations = new List<ConditionRegistration>();
            Conditions persistence = builder.Controller.GetComponent<Conditions>();
            if (persistence != null)
                registrations.AddRange(persistence.CreateRegistrations(builder.CreatureId));

            if (registrations.Count > 0)
                builder.AddState(new ConditionEnrollmentContribution(registrations));
        }
    }

    internal sealed class ConditionEnrollmentContribution : IUnityCombatantStateContribution
    {
        private readonly IReadOnlyList<ConditionRegistration> registrations;

        internal ConditionEnrollmentContribution(
            IEnumerable<ConditionRegistration> registrations
        ) => this.registrations = Array.AsReadOnly(registrations.ToArray());

        /// <inheritdoc/>
        public void Seed(RulesStateSeed seed)
        {
            foreach (ConditionRegistration registration in registrations)
                seed.SeedActiveEffect(registration.Effect).SeedRuleBinding(registration.Binding);
        }

        /// <inheritdoc/>
        public void Register(UnityCombatRulesBridge bridge) =>
            bridge.DispatchRequired(new AdoptConditionRegistrationsOp(registrations));
    }
}
