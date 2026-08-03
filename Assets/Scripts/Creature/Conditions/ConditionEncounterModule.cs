using System;
using System.Collections.Generic;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;

namespace Game.Creature.Rules
{
    /// <summary>Composes condition workflows and immutable persistence enrollment.</summary>
    internal sealed class ConditionEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityCombatantEnrollmentModule,
            IUnityEncounterTurnResourceModule
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
            builder
                .UseConditionRules(registry)
                .RegisterActionPermission(
                    ConditionTurnResourceComposition.CreateActionPermission()
                );

        /// <inheritdoc/>
        public ITurnResourceContributionProvider CreateTurnResourceProvider() =>
            ConditionTurnResourceComposition.CreateProvider();

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            builder.AddRuleBindings(
                new[] { ConditionTurnResourceComposition.CreateListenerBinding(builder.CreatureId) }
            );
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

            PendingImmutableValueLease<IReadOnlyList<ConditionApplicationSnapshot>> lease;
            if (
                persistence.TryPrepareRestore(
                    builder.CreatureId,
                    owner.EncounterId,
                    owner.ResolveDurableActorId,
                    out lease,
                    out IReadOnlyList<ActiveEffectRegistration> registrations
                )
            )
            {
                if (registrations.Count > 0)
                    builder.AddActiveEffects(registrations);
                builder.AddFinalization(lease);
            }
            else
                builder.AddFinalization(persistence.CreateEnrollmentFinalization());
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
}
