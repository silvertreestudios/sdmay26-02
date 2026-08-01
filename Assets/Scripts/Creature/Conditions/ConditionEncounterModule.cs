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
        private readonly UnityCombatRulesBridge owner;
        private readonly RuleRegistry registry;

        internal ConditionEncounterModule(UnityCombatRulesBridge owner, RuleRegistry registry)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseConditionRules(registry);

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            Conditions persistence = builder.Controller.GetComponent<Conditions>();
            if (persistence == null)
                return;

            Conditions.ConditionRestoreLease lease = null;
            if (
                persistence.TryPrepareRestore(
                    builder.CreatureId,
                    owner.EncounterId,
                    source => owner.GetCreatureId(source.GetComponent<CreatureComponent>()),
                    out lease
                )
            )
            {
                builder.AddState(new ConditionEnrollmentContribution(lease.Registrations));
                builder.AddFinalization(
                    new CompleteRestoredConditionEnrollmentContribution(persistence, lease)
                );
            }
            else
                builder.AddFinalization(new CompleteConditionEnrollmentContribution(persistence));
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
            {
                seed.SeedActiveEffect(registration.Effect).SeedRuleBinding(registration.Binding);
                if (registration.Timing != null)
                    seed.SeedActiveEffectTiming(registration.Timing);
            }
        }

        /// <inheritdoc/>
        public void Register(UnityCombatRulesBridge bridge) =>
            bridge.DispatchRequired(new AdoptConditionRegistrationsOp(registrations));
    }

    internal sealed class CompleteConditionEnrollmentContribution
        : IUnityCombatantBatchFinalizationContribution
    {
        private readonly Conditions conditions;

        internal CompleteConditionEnrollmentContribution(Conditions conditions) =>
            this.conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));

        /// <inheritdoc/>
        public void Validate() { }

        /// <inheritdoc/>
        public void Apply() => conditions.CompleteEnrollment();
    }

    internal sealed class CompleteRestoredConditionEnrollmentContribution
        : IUnityCombatantBatchFinalizationContribution
    {
        private readonly Conditions conditions;
        private readonly Conditions.ConditionRestoreLease lease;

        internal CompleteRestoredConditionEnrollmentContribution(
            Conditions conditions,
            Conditions.ConditionRestoreLease lease
        )
        {
            this.conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            this.lease = lease ?? throw new ArgumentNullException(nameof(lease));
        }

        /// <inheritdoc/>
        public void Validate() => lease.Validate();

        /// <inheritdoc/>
        public void Apply()
        {
            lease.ConsumeValidated();
            conditions.CompleteEnrollment();
        }
    }
}
