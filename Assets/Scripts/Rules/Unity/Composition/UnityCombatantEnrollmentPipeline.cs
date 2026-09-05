using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;

namespace Game.Rules.Unity.Composition
{
    /// <summary>
    /// Prepares every combatant through one ordered, reversible path before the common addition.
    /// </summary>
    internal sealed class UnityCombatantEnrollmentPipeline
    {
        private readonly UnityCombatRulesBridge owner;
        private readonly UnityEncounterComposition composition;
        private readonly bool installUnityAuthority;

        internal UnityCombatantEnrollmentPipeline(
            UnityCombatRulesBridge owner,
            UnityEncounterComposition composition,
            bool installUnityAuthority
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.composition = composition ?? throw new ArgumentNullException(nameof(composition));
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
                // All stable mappings exist before feature preparation so restored cross-creature
                // references can be frozen directly into the common registration batch.
                foreach (UnityCombatantEnrollmentBuilder builder in builders)
                {
                    composition.PrepareCombatant(builder);
                    int initiativeModifier = builder.Creature.GetInitiative();
                    CombatantRegistration registration = new(
                        builder.Controller,
                        builder.Creature,
                        builder.BuildState(initiativeModifier)
                    );
                    combatants.Add(
                        new PreparedCombatantEnrollment(registration, builder.Installations)
                    );
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
        }

        /// <summary>Commits every prepared batch through the common rules-owned addition.</summary>
        internal void Commit() =>
            owner.DispatchRequired(
                new AddCombatantsOp(
                    owner.EncounterId,
                    combatants.Select(combatant => combatant.Registration.State)
                )
            );

        /// <summary>Seeds the isolated non-encounter Stride composition.</summary>
        internal void SeedExploration(RulesStateSeed seed)
        {
            if (installUnityAuthority)
                throw new InvalidOperationException(
                    "Combat enrollment cannot use the exploration seed boundary."
                );
            if (seed == null)
                throw new ArgumentNullException(nameof(seed));
            foreach (PreparedCombatantEnrollment combatant in combatants)
            {
                CombatantRulesState state = combatant.Registration.State;
                UnityCombatRulesBridge.SeedExploration(seed, state);
            }
        }

        /// <summary>Attaches exact Unity authority and applies precomputed installations once.</summary>
        internal void AttachAndInstall()
        {
            if (!installUnityAuthority)
                return;
            foreach (PreparedCombatantEnrollment combatant in combatants)
            {
                CombatantRegistration registration = combatant.Registration;
                registration.Creature.AttachHealthRules(owner, registration.State.Creature.Id);
                lifetime.Add(
                    new RegistrationToken(() =>
                        registration.Creature.DetachHealthRules(
                            owner,
                            owner.GetHealth(registration.State.Creature.Id)
                        )
                    )
                );
                registration.Controller.AttachCombatRules(owner, registration.State.Creature.Id);
                lifetime.Add(
                    new RegistrationToken(() => registration.Controller.DetachCombatRules(owner))
                );
                foreach (
                    IUnityCombatantInstallationContribution installation in combatant.Installations
                )
                    installation.Apply();
            }
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
            encounterLifetime.Add(lifetime);
            foreach (RegistrationToken reservation in durableReservations)
                reservation.Retain();
            isTransferred = true;
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
            IReadOnlyList<IUnityCombatantInstallationContribution> installations
        )
        {
            Registration = registration;
            Installations = installations;
        }

        internal CombatantRegistration Registration { get; }

        internal IReadOnlyList<IUnityCombatantInstallationContribution> Installations { get; }
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
