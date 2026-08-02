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
        private readonly bool installUnityAuthority;

        internal ConditionEncounterModule(
            UnityCombatRulesBridge owner,
            RuleRegistry registry,
            bool installUnityAuthority
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.installUnityAuthority = installUnityAuthority;
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseConditionRules(registry);

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            // A detached exploration action is not an encounter authority. It must neither adopt
            // nor consume the detached snapshot reserved for the next owning encounter.
            if (!installUnityAuthority)
                return;
            Conditions persistence = builder.Controller.GetComponent<Conditions>();
            if (persistence == null)
                return;
            builder.AddOwnershipRelease(
                new ProjectDetachedConditionApplicationsContribution(
                    persistence,
                    owner,
                    builder.CreatureId
                )
            );

            Conditions.ConditionRestoreLease lease = null;
            if (
                persistence.TryPrepareRestore(
                    builder.CreatureId,
                    owner.EncounterId,
                    owner.ResolveDurableActorId,
                    out lease
                )
            )
            {
                if (lease.Registrations.Count > 0)
                    builder.AddActiveEffects(
                        lease.Registrations.Select(registration => new ActiveEffectRegistration(
                            registration.Effect,
                            registration.Binding,
                            registration.Timing
                        ))
                    );
                builder.AddFinalization(
                    new CompleteRestoredConditionEnrollmentContribution(persistence, lease)
                );
            }
            else
                builder.AddFinalization(new CompleteConditionEnrollmentContribution(persistence));
        }
    }

    internal sealed class ProjectDetachedConditionApplicationsContribution
        : IUnityCombatantOwnershipReleaseContribution
    {
        private readonly Conditions conditions;
        private readonly UnityCombatRulesBridge bridge;
        private readonly CreatureId owner;

        internal ProjectDetachedConditionApplicationsContribution(
            Conditions conditions,
            UnityCombatRulesBridge bridge,
            CreatureId owner
        )
        {
            this.conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.owner = owner;
        }

        /// <inheritdoc/>
        public void ProjectBeforeDetach() => conditions.ProjectDetachedApplications(bridge, owner);
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
            conditions.CompleteEnrollment();
            lease.ConsumeValidated();
        }
    }
}
