using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;
using GridPrivate;

namespace Game.Rules.Unity.Composition
{
    /// <summary>Marks one explicitly supplied module in a Unity encounter composition.</summary>
    /// <remarks>
    /// Modules are invoked in supplied order. Optional capability interfaces keep a
    /// presentation-only module from implementing unrelated dispatcher or enrollment callbacks.
    /// </remarks>
    internal interface IUnityEncounterModule { }

    /// <summary>Contributes static definitions before the encounter's single registry build.</summary>
    internal interface IUnityEncounterRegistryModule : IUnityEncounterModule
    {
        /// <summary>Adds feature definitions to the shared registry builder.</summary>
        void ConfigureRegistry(RuleRegistryBuilder builder);
    }

    /// <summary>Contributes feature-owned resolvers to the encounter dispatcher.</summary>
    internal interface IUnityEncounterDispatcherModule : IUnityEncounterModule
    {
        /// <summary>Adds this module's rules registrations to the shared builder.</summary>
        void ConfigureDispatcher(RuleDispatcherBuilder builder);
    }

    /// <summary>Contributes one transitional turn-start adapter owned by its feature.</summary>
    internal interface IUnityEncounterTurnStartModule : IUnityEncounterModule
    {
        /// <summary>Creates the adapter installed at this module's deterministic position.</summary>
        IEncounterTurnStartAdapter CreateTurnStartAdapter();
    }

    /// <summary>Registers encounter-owned observers or disposable runtime adapters.</summary>
    internal interface IUnityEncounterRuntimeModule : IUnityEncounterModule
    {
        /// <summary>Registers this module and transfers every token to the encounter lifetime.</summary>
        void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime);
    }

    /// <summary>Refreshes feature-owned Unity topology adapters after a live grid mutation.</summary>
    internal interface IUnityEncounterTopologyModule : IUnityEncounterModule
    {
        /// <summary>Replaces this module's live grid boundary.</summary>
        void RefreshTopology(Tile[,] tiles);
    }

    /// <summary>Prepares feature-owned state and Unity installations for one combatant.</summary>
    internal interface IUnityCombatantEnrollmentModule : IUnityEncounterModule
    {
        /// <summary>Prepares contributions without committing state or attaching authority.</summary>
        void PrepareCombatant(UnityCombatantEnrollmentBuilder builder);
    }

    /// <summary>Reconciles one precomputed Unity installation after state commits.</summary>
    /// <remarks>
    /// Enrollment may invoke <see cref="Reconcile"/> again when an earlier call changed Unity
    /// state and then threw. Implementations must converge their feature-owned projection to the
    /// prepared result on every call, without duplicating entries or disturbing other features.
    /// </remarks>
    internal interface IUnityCombatantInstallationContribution
    {
        /// <summary>Idempotently reconciles the prepared projection without new Unity reads.</summary>
        void Reconcile();
    }

    /// <summary>Projects final feature state immediately before Unity authority detaches.</summary>
    /// <remarks>
    /// Contributions run once during enrollment-lifetime cleanup while the exact controller and
    /// rules bridge are still attached. Cleanup continues and aggregates failures if projection
    /// fails; a feature must not retain the bridge or snapshot after this callback.
    /// </remarks>
    internal interface IUnityCombatantOwnershipReleaseContribution
    {
        /// <summary>Projects the final immutable feature state needed after detachment.</summary>
        void ProjectBeforeDetach();
    }

    /// <summary>Completes one-shot enrollment input after the entire batch is installed.</summary>
    /// <remarks>
    /// <see cref="Validate"/> performs every fallible check for the contribution. After every
    /// contribution in the batch validates, <see cref="Apply"/> must complete without throwing.
    /// This keeps one-shot input available when attachment or installation fails earlier.
    /// </remarks>
    internal interface IUnityCombatantBatchFinalizationContribution
    {
        /// <summary>Validates that finalization can complete without changing state.</summary>
        void Validate();

        /// <summary>Applies the already validated, non-failing finalization.</summary>
        void Apply();
    }

    /// <summary>Provides feature state for initial seed or reinforcement registration.</summary>
    /// <remarks>
    /// A reinforcement <see cref="Register"/> call may commit and then surface an observer
    /// failure. Retrying that call with the same prepared contribution must resolve as an exact
    /// no-op with no new Facts or version; different committed state must still reject.
    /// </remarks>
    internal interface IUnityCombatantStateContribution
    {
        /// <summary>Adds initial feature state to the store seed.</summary>
        void Seed(RulesStateSeed seed);

        /// <summary>Idempotently registers the same feature state for a reinforcement.</summary>
        void Register(UnityCombatRulesBridge bridge);
    }

    /// <summary>Collects typed feature contributions while enrollment remains reversible.</summary>
    internal sealed class UnityCombatantEnrollmentBuilder
    {
        private readonly List<IUnityCombatantStateContribution> stateContributions = new();
        private readonly List<IUnityCombatantInstallationContribution> installations = new();
        private readonly List<IUnityCombatantOwnershipReleaseContribution> releaseContributions =
            new();
        private readonly List<IUnityCombatantBatchFinalizationContribution> finalizations = new();
        private readonly List<SpellSlotState> spellSlots = new();
        private readonly List<ActiveRuleBinding> ruleBindings = new();
        private readonly List<ActiveEffectRegistration> activeEffects = new();
        private readonly CompositeLifetime preparationLifetime;
        private readonly CreatureState creatureState;
        private readonly HealthState health;
        private readonly GridPosition position;
        private readonly GridDistance landSpeed;
        private readonly CreatureStatisticsState statistics;
        private PreparedCreatureInputs preparedInputs;

        internal UnityCombatantEnrollmentBuilder(
            ActionController controller,
            CreatureComponent creature,
            CreatureState creatureState,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed,
            CreatureStatisticsState statistics,
            CompositeLifetime preparationLifetime,
            string durableActorId = ""
        )
        {
            Controller = controller ?? throw new ArgumentNullException(nameof(controller));
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            this.creatureState =
                creatureState ?? throw new ArgumentNullException(nameof(creatureState));
            this.health = health;
            this.position = position;
            this.landSpeed = landSpeed;
            this.statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            DurableActorId =
                durableActorId ?? throw new ArgumentNullException(nameof(durableActorId));
            this.preparationLifetime =
                preparationLifetime ?? throw new ArgumentNullException(nameof(preparationLifetime));
        }

        internal ActionController Controller { get; }
        internal CreatureComponent Creature { get; }
        internal CreatureId CreatureId => creatureState.Id;
        internal string DurableActorId { get; }

        /// <summary>Gets the compiled inputs after the prepared-rules module contributes them.</summary>
        internal PreparedCreatureInputs PreparedInputs =>
            preparedInputs
            ?? throw new InvalidOperationException("Prepared inputs have not been compiled.");

        /// <summary>Adds the one immutable prepared-rules compilation for this combatant.</summary>
        internal void AddPreparedRules(
            PreparedCreatureInputs inputs,
            IEnumerable<ActiveRuleBinding> bindings
        )
        {
            if (preparedInputs != null)
                throw new InvalidOperationException("Prepared inputs were already contributed.");
            preparedInputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            AddRuleBindings(bindings);
        }

        /// <summary>Retains reversible feature preparation until success or rollback.</summary>
        internal TResource Own<TResource>(TResource resource)
            where TResource : IDisposable => preparationLifetime.Add(resource);

        /// <summary>Adds one typed authoritative state contribution.</summary>
        internal void AddState(IUnityCombatantStateContribution contribution) =>
            stateContributions.Add(
                contribution ?? throw new ArgumentNullException(nameof(contribution))
            );

        /// <summary>Adds feature-owned spell-slot state to the atomic combatant registration.</summary>
        internal void AddSpellSlots(IEnumerable<SpellSlotState> states)
        {
            if (states == null)
                throw new ArgumentNullException(nameof(states));
            spellSlots.AddRange(states);
        }

        /// <summary>Adds feature-owned rule bindings to the atomic combatant registration.</summary>
        internal void AddRuleBindings(IEnumerable<ActiveRuleBinding> bindings)
        {
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));
            ruleBindings.AddRange(bindings);
        }

        /// <summary>Adds prepared active effects to the atomic combatant registration.</summary>
        internal void AddActiveEffects(IEnumerable<ActiveEffectRegistration> registrations)
        {
            if (registrations == null)
                throw new ArgumentNullException(nameof(registrations));
            foreach (ActiveEffectRegistration registration in registrations)
            {
                if (registration == null)
                    throw new ArgumentException(
                        "Active-effect registrations cannot contain null.",
                        nameof(registrations)
                    );
                ActiveEffectRegistration[] collisions = activeEffects
                    .Where(existing =>
                        existing.Effect.Id == registration.Effect.Id
                        || existing.Binding.Id == registration.Binding.Id
                    )
                    .ToArray();
                if (collisions.Length == 0)
                {
                    activeEffects.Add(registration);
                    continue;
                }
                if (collisions.Length != 1 || !collisions[0].HasSameStructure(registration))
                    throw new InvalidOperationException(
                        "Prepared active-effect identities collide with different registration state."
                    );
            }
        }

        /// <summary>Adds one fully prepared Unity installation.</summary>
        internal void AddInstallation(IUnityCombatantInstallationContribution contribution) =>
            installations.Add(
                contribution ?? throw new ArgumentNullException(nameof(contribution))
            );

        /// <summary>Adds final projection that runs while exact rules authority is attached.</summary>
        internal void AddOwnershipRelease(
            IUnityCombatantOwnershipReleaseContribution contribution
        ) =>
            releaseContributions.Add(
                contribution ?? throw new ArgumentNullException(nameof(contribution))
            );

        /// <summary>Adds one finalization that runs only after the complete batch is installed.</summary>
        internal void AddFinalization(IUnityCombatantBatchFinalizationContribution contribution) =>
            finalizations.Add(
                contribution ?? throw new ArgumentNullException(nameof(contribution))
            );

        internal IReadOnlyList<IUnityCombatantStateContribution> StateContributions =>
            stateContributions;
        internal IReadOnlyList<IUnityCombatantInstallationContribution> Installations =>
            installations;
        internal IReadOnlyList<IUnityCombatantOwnershipReleaseContribution> ReleaseContributions =>
            releaseContributions;
        internal IReadOnlyList<IUnityCombatantBatchFinalizationContribution> Finalizations =>
            finalizations;

        /// <summary>Freezes the prepared base and feature contributions into one immutable state.</summary>
        internal CombatantRulesState BuildState() =>
            new(
                creatureState,
                health,
                position,
                landSpeed,
                statistics,
                PreparedInputs,
                spellSlots,
                ruleBindings,
                activeEffects
            );
    }

    /// <summary>Captures raw Unity creature values without resolving live rule providers.</summary>
    internal static class UnityCreatureStatisticsAdapter
    {
        private static readonly RuleSource AllSavesSource = RuleSource.FromSlug(
            "creature-base-all-saves"
        );

        internal static CreatureStatisticsState Capture(
            CreatureId creatureId,
            CreatureComponent creature
        )
        {
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            Dictionary<Skill, int> skills = new();
            foreach (SkillValue value in creature.skills)
            {
                Skill skill = Skill.FromName(value.skillName);
                if (!skills.TryAdd(skill, value.skillMod))
                    throw new InvalidOperationException(
                        $"Creature '{creature.name}' defines base skill '{skill.Slug}' more than once."
                    );
            }
            List<Modifier> modifiers = new();
            if (creature.allSaves != 0)
            {
                modifiers.Add(
                    Modifier.Untyped(creature.allSaves, AllSavesSource, Statistic.FortitudeSave)
                );
                modifiers.Add(
                    Modifier.Untyped(creature.allSaves, AllSavesSource, Statistic.ReflexSave)
                );
                modifiers.Add(
                    Modifier.Untyped(creature.allSaves, AllSavesSource, Statistic.WillSave)
                );
            }
            return new CreatureStatisticsState(
                creatureId,
                creature.attackBonus,
                Math.Max(0, creature.ac),
                creature.fortitudeSave,
                creature.reflexSave,
                creature.willSave,
                skills,
                modifiers
            );
        }
    }

    /// <summary>Invokes explicitly supplied encounter modules without discovering features.</summary>
    internal sealed class UnityEncounterComposition
    {
        private readonly IReadOnlyList<IUnityEncounterModule> modules;

        internal UnityEncounterComposition(IEnumerable<IUnityEncounterModule> modules)
        {
            IUnityEncounterModule[] copied =
                modules?.ToArray() ?? throw new ArgumentNullException(nameof(modules));
            if (copied.Any(module => module == null))
                throw new ArgumentException(
                    "Encounter modules cannot contain null.",
                    nameof(modules)
                );
            this.modules = Array.AsReadOnly(copied);
        }

        /// <summary>Gets turn-start adapters in exact module order.</summary>
        internal IReadOnlyList<IEncounterTurnStartAdapter> CreateTurnStartAdapters() =>
            modules
                .OfType<IUnityEncounterTurnStartModule>()
                .Select(module => module.CreateTurnStartAdapter())
                .ToArray();

        /// <summary>Configures dispatcher modules in exact module order.</summary>
        internal void ConfigureDispatcher(RuleDispatcherBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            foreach (
                IUnityEncounterDispatcherModule module in modules.OfType<IUnityEncounterDispatcherModule>()
            )
                module.ConfigureDispatcher(builder);
        }

        /// <summary>Registers runtime modules in exact module order.</summary>
        internal void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (lifetime == null)
                throw new ArgumentNullException(nameof(lifetime));
            foreach (
                IUnityEncounterRuntimeModule module in modules.OfType<IUnityEncounterRuntimeModule>()
            )
                module.RegisterRuntime(dispatcher, lifetime);
        }

        /// <summary>Refreshes topology modules in exact supplied-module order.</summary>
        internal void RefreshTopology(Tile[,] tiles)
        {
            if (tiles == null)
                throw new ArgumentNullException(nameof(tiles));
            foreach (
                IUnityEncounterTopologyModule module in modules.OfType<IUnityEncounterTopologyModule>()
            )
                module.RefreshTopology(tiles);
        }

        /// <summary>Prepares enrollment modules in exact module order.</summary>
        internal void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            foreach (
                IUnityCombatantEnrollmentModule module in modules.OfType<IUnityCombatantEnrollmentModule>()
            )
                module.PrepareCombatant(builder);
        }
    }
}
