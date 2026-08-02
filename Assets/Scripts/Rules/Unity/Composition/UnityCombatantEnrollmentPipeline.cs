using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;

namespace Game.Rules.Unity.Composition
{
    /// <summary>
    /// Prepares every combatant through one ordered, reversible path before choosing an initial
    /// seed or reinforcement commit.
    /// </summary>
    internal sealed class UnityCombatantEnrollmentPipeline
    {
        private readonly UnityCombatRulesBridge owner;
        private readonly UnityEncounterComposition composition;
        private readonly RuleRegistry registry;
        private readonly bool installUnityAuthority;

        internal UnityCombatantEnrollmentPipeline(
            UnityCombatRulesBridge owner,
            UnityEncounterComposition composition,
            RuleRegistry registry,
            bool installUnityAuthority
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.composition = composition ?? throw new ArgumentNullException(nameof(composition));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.installUnityAuthority = installUnityAuthority;
        }

        /// <summary>Performs all fallible Unity reads and module preparation before state commit.</summary>
        internal UnityCombatantEnrollmentPlan Prepare(
            IEnumerable<ActionController> controllers,
            string parameterName
        )
        {
            if (controllers == null)
                throw new ArgumentNullException(parameterName);
            ActionController[] copied = controllers.ToArray();
            UnityCombatRulesBridge.ValidateControllers(copied, parameterName);
            if (
                copied.Any(owner.IsControllerRegistered)
                || copied
                    .Select(controller => controller.GetComponent<CreatureComponent>())
                    .Any(owner.IsCreatureRegistered)
            )
                throw new InvalidOperationException("A combatant is already registered.");

            CompositeLifetime preparation = new();
            List<RegistrationToken> durableReservations = new()
            {
                preparation.Add(owner.CreateIdentityReservation()),
            };
            List<PreparedCombatantEnrollment> combatants = new();
            try
            {
                List<UnityCombatantEnrollmentBuilder> builders = new();
                foreach (ActionController controller in copied)
                {
                    UnityCombatantEnrollmentBuilder builder =
                        owner.CreateCombatantEnrollmentBuilder(controller, preparation);
                    if (installUnityAuthority)
                    {
                        builder.Controller.ValidateCombatRulesAttachment(owner, builder.CreatureId);
                        builder.Creature.ValidateHealthRulesAttachment(owner, builder.CreatureId);
                    }

                    owner.AddRegistrationMaps(
                        builder.Controller,
                        builder.Creature,
                        builder.CreatureId
                    );
                    durableReservations.Add(
                        preparation.Add(
                            new RegistrationToken(() =>
                                owner.RemoveRegistrationMaps(
                                    builder.Controller,
                                    builder.Creature,
                                    builder.CreatureId
                                )
                            )
                        )
                    );
                    builders.Add(builder);
                }
                // Every identity is reserved before feature preparation so persisted cross-creature
                // sources resolve independently of combatant enumeration order.
                foreach (UnityCombatantEnrollmentBuilder builder in builders)
                {
                    composition.PrepareCombatant(builder);
                    int initiativeModifier = builder.Creature.GetInitiative();
                    CombatantRegistration registration = new(
                        builder.Controller,
                        builder.Creature,
                        builder.BuildState()
                    );
                    combatants.Add(
                        new PreparedCombatantEnrollment(
                            registration,
                            initiativeModifier,
                            builder.StateContributions,
                            builder.Installations,
                            builder.ReleaseContributions,
                            builder.Finalizations
                        )
                    );
                }
                HashSet<CreatureId> preparedCreatureIds = new HashSet<CreatureId>(
                    combatants.Select(combatant => combatant.Registration.State.Creature.Id)
                );
                // Registration construction owns structural invariants. Resolve the remaining
                // encounter-specific invariants against the complete batch while preparation is
                // still reversible and before the caller can create an initial seed.
                foreach (PreparedCombatantEnrollment combatant in combatants)
                {
                    foreach (
                        ActiveEffectRegistration registration in combatant
                            .Registration
                            .State
                            .ActiveEffects
                    )
                    {
                        if (!registry.ContainsDefinition(registration.Effect.DefinitionId))
                            throw new InvalidOperationException(
                                $"Rule definition {registration.Effect.DefinitionId.Value} is unknown."
                            );
                        if (
                            registration.Effect.Status != ActiveEffectStatus.Active
                            || registration.Effect.Duration.Kind == EffectDurationKind.Indefinite
                        )
                            continue;
                        if (!preparedCreatureIds.Contains(registration.Effect.SourceCreature))
                        {
                            try
                            {
                                // Reinforcements may reference an already-enrolled source.
                                owner.GetController(registration.Effect.SourceCreature);
                            }
                            catch (InvalidOperationException exception)
                            {
                                throw new InvalidOperationException(
                                    $"Active finite effect {registration.Effect.Id.Value} has source creature {registration.Effect.SourceCreature.Value}, which is not registered in this encounter.",
                                    exception
                                );
                            }
                        }
                        if (
                            registration.Timing != null
                            && registration.Timing.Encounter != owner.EncounterId
                        )
                            throw new InvalidOperationException(
                                $"Active finite effect {registration.Effect.Id.Value} has timing for encounter {registration.Timing.Encounter.Value}, not owning encounter {owner.EncounterId.Value}."
                            );
                    }
                }
                return new UnityCombatantEnrollmentPlan(
                    owner,
                    combatants,
                    preparation,
                    durableReservations,
                    installUnityAuthority
                );
            }
            catch (Exception preparationFailure)
            {
                try
                {
                    preparation.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Combatant preparation and rollback both failed.",
                        preparationFailure,
                        cleanupFailure
                    );
                }
                throw;
            }
        }
    }

    /// <summary>Owns one prepared enrollment batch until rollback or encounter-lifetime transfer.</summary>
    internal sealed class UnityCombatantEnrollmentPlan : IDisposable
    {
        private readonly UnityCombatRulesBridge owner;
        private readonly IReadOnlyList<PreparedCombatantEnrollment> combatants;
        private readonly CompositeLifetime lifetime;
        private readonly IReadOnlyList<RegistrationToken> durableReservations;
        private readonly bool installUnityAuthority;
        private readonly bool[] healthAttached;
        private readonly bool[] controllersAttached;
        private readonly bool[] releasesAttached;
        private readonly int[] nextInstallation;
        private int stateCombatantIndex;
        private int stateContributionIndex;
        private bool joinCommitted;
        private bool reinforcementCommitStarted;
        private bool reinforcementsCommitted;
        private IReadOnlyList<IUnityCombatantBatchFinalizationContribution> validatedFinalizations;
        private bool isTransferred;

        internal UnityCombatantEnrollmentPlan(
            UnityCombatRulesBridge owner,
            IReadOnlyList<PreparedCombatantEnrollment> combatants,
            CompositeLifetime lifetime,
            IReadOnlyList<RegistrationToken> durableReservations,
            bool installUnityAuthority
        )
        {
            this.owner = owner;
            this.combatants = combatants;
            this.lifetime = lifetime;
            this.durableReservations = durableReservations;
            this.installUnityAuthority = installUnityAuthority;
            healthAttached = new bool[combatants.Count];
            controllersAttached = new bool[combatants.Count];
            releasesAttached = new bool[combatants.Count];
            nextInstallation = new int[combatants.Count];
        }

        internal bool ReinforcementCommitStarted
        {
            get
            {
                if (joinCommitted)
                    return true;
                if (
                    !reinforcementCommitStarted
                    || !owner.Snapshot.Encounters.TryGet(
                        owner.EncounterId,
                        out EncounterState encounter
                    )
                )
                    return false;
                return combatants.Any(combatant =>
                    encounter.HasReinforcementRegistration(combatant.Registration.State.Creature.Id)
                );
            }
        }

        /// <summary>Gets whether this plan owns exactly the supplied retry batch.</summary>
        internal bool Matches(IEnumerable<ActionController> controllers)
        {
            if (controllers == null)
                return false;
            return controllers.SequenceEqual(
                combatants.Select(combatant => combatant.Registration.Controller)
            );
        }

        /// <summary>Adds base and feature-owned state for constructor-time participants.</summary>
        internal void SeedInitial(RulesStateSeed seed)
        {
            if (seed == null)
                throw new ArgumentNullException(nameof(seed));
            foreach (PreparedCombatantEnrollment combatant in combatants)
            {
                UnityCombatRulesBridge.Seed(seed, combatant.Registration.State);
                if (!installUnityAuthority)
                {
                    seed.SeedActionEconomy(
                        combatant.Registration.State.Creature.Id,
                        new ActionEconomyState(1, false)
                    );
                }
                foreach (IUnityCombatantStateContribution contribution in combatant.State)
                    contribution.Seed(seed);
            }
        }

        /// <summary>Commits prepared reinforcements, then their already validated feature state.</summary>
        internal void CommitReinforcements()
        {
            if (reinforcementsCommitted)
                return;
            if (!reinforcementCommitStarted)
            {
                reinforcementCommitStarted = true;
            }
            if (!joinCommitted)
            {
                owner.DispatchEnrollmentRequired(
                    new JoinEncounterOp(
                        owner.EncounterId,
                        combatants.Select(combatant => new EncounterJoinParticipant(
                            new EncounterParticipant(
                                combatant.Registration.State.Creature.Id,
                                combatant.Registration.State.Creature.Player,
                                combatant.InitiativeModifier
                            ),
                            combatant.Registration.State
                        ))
                    )
                );
                joinCommitted = true;
            }

            while (stateCombatantIndex < combatants.Count)
            {
                IReadOnlyList<IUnityCombatantStateContribution> contributions = combatants[
                    stateCombatantIndex
                ].State;
                while (stateContributionIndex < contributions.Count)
                {
                    owner.EnsureEnrollmentCanContinue();
                    contributions[stateContributionIndex].Register(owner);
                    owner.EnsureEnrollmentCanContinue();
                    stateContributionIndex++;
                }
                stateCombatantIndex++;
                stateContributionIndex = 0;
            }
            reinforcementsCommitted = true;
        }

        /// <summary>Attaches exact Unity authority and applies precomputed installations once.</summary>
        internal void AttachAndInstall()
        {
            if (!installUnityAuthority)
                return;
            for (int index = 0; index < combatants.Count; index++)
            {
                PreparedCombatantEnrollment combatant = combatants[index];
                CombatantRegistration registration = combatant.Registration;
                if (!healthAttached[index])
                {
                    owner.EnsureEnrollmentCanContinue();
                    registration.Creature.AttachHealthRules(owner, registration.State.Creature.Id);
                    lifetime.Add(
                        new RegistrationToken(() =>
                            registration.Creature.DetachHealthRules(
                                owner,
                                owner.GetHealth(registration.State.Creature.Id)
                            )
                        )
                    );
                    healthAttached[index] = true;
                }
                if (!controllersAttached[index])
                {
                    owner.EnsureEnrollmentCanContinue();
                    registration.Controller.AttachCombatRules(
                        owner,
                        registration.State.Creature.Id
                    );
                    lifetime.Add(
                        new RegistrationToken(() =>
                            registration.Controller.DetachCombatRules(owner)
                        )
                    );
                    controllersAttached[index] = true;
                }
                if (!releasesAttached[index])
                {
                    foreach (
                        IUnityCombatantOwnershipReleaseContribution release in combatant.Releases
                    )
                        lifetime.Add(new RegistrationToken(release.ProjectBeforeDetach));
                    releasesAttached[index] = true;
                }
                while (nextInstallation[index] < combatant.Installations.Count)
                {
                    owner.EnsureEnrollmentCanContinue();
                    combatant.Installations[nextInstallation[index]].Reconcile();
                    owner.EnsureEnrollmentCanContinue();
                    nextInstallation[index]++;
                }
            }
        }

        /// <summary>Finalizes one-shot inputs only after the complete batch is ready to transfer.</summary>
        internal void FinalizeBatch()
        {
            if (validatedFinalizations != null)
                return;
            if (
                installUnityAuthority
                && combatants
                    .Select(
                        (combatant, index) =>
                            healthAttached[index]
                            && controllersAttached[index]
                            && releasesAttached[index]
                            && nextInstallation[index] == combatant.Installations.Count
                    )
                    .Any(complete => !complete)
            )
                throw new InvalidOperationException(
                    "Combatant enrollment cannot finalize before every installation succeeds."
                );
            IUnityCombatantBatchFinalizationContribution[] finalizations = combatants
                .SelectMany(combatant => combatant.Finalizations)
                .ToArray();
            foreach (IUnityCombatantBatchFinalizationContribution finalization in finalizations)
                finalization.Validate();
            validatedFinalizations = Array.AsReadOnly(finalizations);
        }

        /// <summary>Transfers this complete batch to the encounter's sole composite lifetime.</summary>
        internal void TransferTo(CompositeLifetime encounterLifetime)
        {
            if (encounterLifetime == null)
                throw new ArgumentNullException(nameof(encounterLifetime));
            if (isTransferred)
                throw new InvalidOperationException(
                    "Combatant enrollment was already transferred."
                );
            if (validatedFinalizations == null)
                throw new InvalidOperationException(
                    "Combatant enrollment must finalize before ownership transfer."
                );
            encounterLifetime.Add(lifetime);
            foreach (RegistrationToken reservation in durableReservations)
                reservation.Retain();
            isTransferred = true;
            foreach (
                IUnityCombatantBatchFinalizationContribution finalization in validatedFinalizations
            )
                finalization.Apply();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!isTransferred)
                lifetime.Dispose();
        }
    }

    internal sealed class PreparedCombatantEnrollment
    {
        internal PreparedCombatantEnrollment(
            CombatantRegistration registration,
            int initiativeModifier,
            IReadOnlyList<IUnityCombatantStateContribution> state,
            IReadOnlyList<IUnityCombatantInstallationContribution> installations,
            IReadOnlyList<IUnityCombatantOwnershipReleaseContribution> releases,
            IReadOnlyList<IUnityCombatantBatchFinalizationContribution> finalizations
        )
        {
            Registration = registration;
            InitiativeModifier = initiativeModifier;
            State = state;
            Installations = installations;
            Releases = releases;
            Finalizations = finalizations;
        }

        internal CombatantRegistration Registration { get; }

        /// <summary>Gets the initiative modifier captured during fallible Unity preparation.</summary>
        internal int InitiativeModifier { get; }
        internal IReadOnlyList<IUnityCombatantStateContribution> State { get; }
        internal IReadOnlyList<IUnityCombatantInstallationContribution> Installations { get; }
        internal IReadOnlyList<IUnityCombatantOwnershipReleaseContribution> Releases { get; }
        internal IReadOnlyList<IUnityCombatantBatchFinalizationContribution> Finalizations { get; }
    }

    internal sealed class CombatantRegistration
    {
        internal CombatantRegistration(
            ActionController controller,
            CreatureComponent creature,
            CombatantRulesState state
        )
        {
            Controller = controller;
            Creature = creature;
            State = state;
        }

        internal ActionController Controller { get; }
        internal CreatureComponent Creature { get; }
        internal CombatantRulesState State { get; }
    }

    /// <summary>Idempotently releases one reversible registration action.</summary>
    internal sealed class RegistrationToken : IDisposable
    {
        private readonly Action unregister;
        private bool isDisposed;
        private bool isRetained;

        internal RegistrationToken(Action unregister) =>
            this.unregister = unregister ?? throw new ArgumentNullException(nameof(unregister));

        /// <summary>Converts provisional ownership into durable state before transfer.</summary>
        internal void Retain()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(RegistrationToken));
            isRetained = true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (isDisposed)
                return;
            isDisposed = true;
            if (!isRetained)
                unregister();
        }
    }
}
