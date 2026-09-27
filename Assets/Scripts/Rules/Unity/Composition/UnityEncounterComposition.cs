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

    /// <summary>
    /// Carries one explicitly installed feature module and its action catalog into encounter
    /// construction without teaching the combat manager or bridge the feature's semantics.
    /// </summary>
    internal sealed class UnityEncounterExtension
    {
        internal UnityEncounterExtension(IUnityEncounterModule module, IActionCatalog actionCatalog)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            ActionCatalog = actionCatalog ?? throw new ArgumentNullException(nameof(actionCatalog));
        }

        internal IUnityEncounterModule Module { get; }

        internal IActionCatalog ActionCatalog { get; }
    }

    /// <summary>Owns exact, explicitly registered feature extensions for future encounters.</summary>
    internal interface IUnityEncounterExtensionHost
    {
        /// <summary>Registers one extension until the returned exact-identity token is disposed.</summary>
        IDisposable RegisterEncounterExtension(UnityEncounterExtension extension);
    }

    /// <summary>Contributes feature-owned resolvers to the encounter dispatcher.</summary>
    internal interface IUnityEncounterDispatcherModule : IUnityEncounterModule
    {
        /// <summary>Adds this module's rules registrations to the shared builder.</summary>
        void ConfigureDispatcher(RuleDispatcherBuilder builder);
    }

    /// <summary>Registers encounter-owned observers or disposable runtime adapters.</summary>
    internal interface IUnityEncounterRuntimeModule : IUnityEncounterModule
    {
        /// <summary>Registers this module and transfers every token to the encounter lifetime.</summary>
        void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime);
    }

    /// <summary>Contributes typed feature presentation to the shared action registry.</summary>
    internal interface IUnityEncounterActionPresentationModule : IUnityEncounterModule
    {
        /// <summary>Registers this feature's concrete action and outcome presenters.</summary>
        void ConfigureActionPresentation(UnityActionPresentationRegistry registry);
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

    /// <summary>Applies one precomputed Unity installation after authoritative state commits.</summary>
    internal interface IUnityCombatantInstallationContribution
    {
        /// <summary>Applies the contribution without repeating fallible preparation reads.</summary>
        void Apply();
    }

    /// <summary>Freezes Unity-authored base statistics before rules enrollment commits.</summary>
    /// <remarks>
    /// This adapter captures immutable creature inputs and explicitly supplied generic modifier
    /// collections. Conditions, active effects, cover, multiple attack penalty, and other
    /// situational contributions remain owned by their rule modules and are not copied into the
    /// base-statistics slice. In particular, this must not enumerate every
    /// <see cref="IPf2eModifierProvider"/> because conditions also implement that legacy live-read
    /// boundary and would otherwise be frozen and applied a second time by rules middleware.
    /// </remarks>
    internal static class UnityCreatureStatisticsAdapter
    {
        internal static CreatureStatisticsState Capture(
            CreatureComponent creature,
            CreatureId creatureId
        )
        {
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            if (creatureId.IsEmpty)
                throw new ArgumentException(
                    "A creature ID is required for statistics capture.",
                    nameof(creatureId)
                );

            Dictionary<Skill, int> skills = new();
            foreach (SkillValue value in creature.skills ?? new List<SkillValue>())
                AddSkill(skills, value.skillName, value.skillMod);

            AddSkill(skills, "athletics", creature.strMod);
            AddSkill(skills, "acrobatics", creature.dexMod);
            AddSkill(skills, "stealth", creature.dexMod);
            AddSkill(skills, "thievery", creature.dexMod);
            AddSkill(skills, "sleight of hand", creature.dexMod);
            AddSkill(skills, "sleight", creature.dexMod);
            AddSkill(skills, "acro", creature.dexMod);
            AddSkill(skills, "arcana", creature.intMod);
            AddSkill(skills, "crafting", creature.intMod);
            AddSkill(skills, "history", creature.intMod);
            AddSkill(skills, "investigation", creature.intMod);
            AddSkill(skills, "lore", creature.intMod);
            AddSkill(skills, "engineering", creature.intMod);
            AddSkill(skills, "occultism", creature.intMod);
            AddSkill(skills, "society", creature.intMod);
            AddSkill(skills, "perception", creature.wisMod);
            AddSkill(skills, "insight", creature.wisMod);
            AddSkill(skills, "survival", creature.wisMod);
            AddSkill(skills, "medicine", creature.wisMod);
            AddSkill(skills, "nature", creature.wisMod);
            AddSkill(skills, "religion", creature.wisMod);
            AddSkill(skills, "deception", creature.chaMod);
            AddSkill(skills, "intimidation", creature.chaMod);
            AddSkill(skills, "performance", creature.chaMod);
            AddSkill(skills, "persuasion", creature.chaMod);
            AddSkill(skills, "diplomacy", creature.chaMod);

            return new CreatureStatisticsState(
                creatureId,
                creature.attackBonus,
                creature.ac,
                creature.fortitudeSave + creature.allSaves,
                creature.reflexSave + creature.allSaves,
                creature.willSave + creature.allSaves,
                skills,
                CaptureGenericModifiers(creature)
            );
        }

        private static IReadOnlyList<Modifier> CaptureGenericModifiers(
            CreatureComponent creature
        ) =>
            creature
                .GetComponents<Pf2eModifierCollection>()
                .SelectMany(collection => collection.Modifiers)
                .Select(ConvertModifier)
                .ToArray();

        private static Modifier ConvertModifier(Pf2eModifier modifier) =>
            new(
                modifier.Value,
                ConvertModifierType(modifier.Type),
                RuleSource.FromName(modifier.Source),
                ConvertStatistic(modifier.TargetStatistic)
            );

        private static ModifierType ConvertModifierType(Pf2eModifierType type) =>
            type switch
            {
                Pf2eModifierType.Untyped => ModifierType.Untyped,
                Pf2eModifierType.Circumstance => ModifierType.Circumstance,
                Pf2eModifierType.Item => ModifierType.Item,
                Pf2eModifierType.Status => ModifierType.Status,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };

        private static Statistic ConvertStatistic(Pf2eStatistic statistic) =>
            statistic switch
            {
                Pf2eStatistic.AttackRoll => Statistic.AttackRoll,
                Pf2eStatistic.ArmorClass => Statistic.ArmorClass,
                Pf2eStatistic.FortitudeSave => Statistic.FortitudeSave,
                Pf2eStatistic.ReflexSave => Statistic.ReflexSave,
                Pf2eStatistic.WillSave => Statistic.WillSave,
                Pf2eStatistic.SkillCheck => Statistic.SkillCheck,
                Pf2eStatistic.Initiative => Statistic.Initiative,
                Pf2eStatistic.DifficultyClass => Statistic.DifficultyClass,
                _ => throw new ArgumentOutOfRangeException(nameof(statistic), statistic, null),
            };

        private static void AddSkill(IDictionary<Skill, int> skills, string skillName, int modifier)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                return;
            Skill skill = Skill.FromName(skillName);
            if (!skills.ContainsKey(skill))
                skills.Add(skill, modifier);
        }
    }

    /// <summary>Collects typed feature contributions while enrollment remains reversible.</summary>
    internal sealed class UnityCombatantEnrollmentBuilder
    {
        private readonly List<IUnityCombatantInstallationContribution> installations = new();
        private readonly List<SpellSlotState> spellSlots = new();
        private readonly List<ActiveRuleBinding> ruleBindings = new();
        private readonly List<EquipmentState> equipment = new();
        private readonly List<AmmunitionState> ammunition = new();
        private readonly List<ActiveEffectInstance> activeEffects = new();
        private readonly List<ActiveEffectTimingRestore> activeEffectTimings = new();
        private readonly List<Modifier> initiativeModifiers = new();
        private readonly HashSet<CreatureId> externalEffectReferences = new();
        private readonly List<RegistrationToken> durableReservations = new();
        private readonly CompositeLifetime preparationLifetime;
        private readonly CreatureState creatureState;
        private readonly CreatureStatisticsState statistics;
        private readonly HealthState health;
        private readonly GridPosition position;
        private readonly GridDistance landSpeed;
        private readonly ActiveEffectIdentityScope activeEffectIdentities;

        internal UnityCombatantEnrollmentBuilder(
            ActionController controller,
            CreatureComponent creature,
            CreatureState creatureState,
            CreatureStatisticsState statistics,
            HealthState health,
            GridPosition position,
            GridDistance landSpeed,
            ActiveEffectIdentityScope activeEffectIdentities,
            CompositeLifetime preparationLifetime
        )
        {
            Controller = controller ?? throw new ArgumentNullException(nameof(controller));
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            this.creatureState =
                creatureState ?? throw new ArgumentNullException(nameof(creatureState));
            this.statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            if (statistics.Creature != creatureState.Id)
                throw new ArgumentException(
                    "The statistics state must describe the enrollment creature.",
                    nameof(statistics)
                );
            this.health = health;
            this.position = position;
            this.landSpeed = landSpeed;
            this.activeEffectIdentities =
                activeEffectIdentities
                ?? throw new ArgumentNullException(nameof(activeEffectIdentities));
            this.preparationLifetime =
                preparationLifetime ?? throw new ArgumentNullException(nameof(preparationLifetime));
        }

        internal ActionController Controller { get; }
        internal CreatureComponent Creature { get; }
        internal CreatureId CreatureId => creatureState.Id;

        /// <summary>Creates a new effect identity in the owning bridge's global namespace.</summary>
        internal (ActiveEffectId EffectId, BindingId BindingId) CreateActiveEffectIdentity(
            string kind,
            string localIdentity
        ) => activeEffectIdentities.Create(kind, localIdentity);

        /// <summary>Retains reversible feature preparation until success or rollback.</summary>
        internal TResource Own<TResource>(TResource resource)
            where TResource : IDisposable => preparationLifetime.Add(resource);

        /// <summary>
        /// Registers a provisional association that becomes durable with the combatant addition.
        /// </summary>
        /// <remarks>
        /// The rollback runs when any later preparation or addition step fails. The enrollment
        /// plan retains the token only after the rules snapshot proves that the atomic batch
        /// committed, including when a post-commit notification subsequently throws.
        /// </remarks>
        internal void Reserve(Action rollback)
        {
            RegistrationToken reservation = preparationLifetime.Add(
                new RegistrationToken(rollback)
            );
            durableReservations.Add(reservation);
        }

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

        /// <summary>Adds feature-owned equipment to the atomic combatant registration.</summary>
        internal void AddEquipment(IEnumerable<EquipmentState> states)
        {
            if (states == null)
                throw new ArgumentNullException(nameof(states));
            equipment.AddRange(states);
        }

        /// <summary>Adds feature-owned ammunition to the atomic combatant registration.</summary>
        internal void AddAmmunition(IEnumerable<AmmunitionState> states)
        {
            if (states == null)
                throw new ArgumentNullException(nameof(states));
            ammunition.AddRange(states);
        }

        /// <summary>Adds restored active effects to the atomic combatant registration.</summary>
        internal void AddActiveEffects(IEnumerable<ActiveEffectInstance> effects)
        {
            if (effects == null)
                throw new ArgumentNullException(nameof(effects));
            activeEffects.AddRange(effects);
        }

        /// <summary>Adds exact remaining schedules for restored active effects.</summary>
        internal void AddActiveEffectTimings(IEnumerable<ActiveEffectTimingRestore> timings)
        {
            if (timings == null)
                throw new ArgumentNullException(nameof(timings));
            activeEffectTimings.AddRange(timings);
        }

        /// <summary>
        /// Adds stable effect actor references that are not live combatants in this encounter.
        /// </summary>
        internal void AddExternalEffectReferences(IEnumerable<CreatureId> references)
        {
            if (references == null)
                throw new ArgumentNullException(nameof(references));
            foreach (CreatureId reference in references)
                externalEffectReferences.Add(reference);
        }

        /// <summary>Adds one fully prepared Unity installation.</summary>
        internal void AddInstallation(IUnityCombatantInstallationContribution contribution) =>
            installations.Add(
                contribution ?? throw new ArgumentNullException(nameof(contribution))
            );

        internal IReadOnlyList<IUnityCombatantInstallationContribution> Installations =>
            installations;

        internal IReadOnlyList<RegistrationToken> DurableReservations => durableReservations;

        /// <summary>
        /// Gets the prepared bindings so a feature can derive pre-commit enrollment inputs from
        /// its own restored state.
        /// </summary>
        internal IReadOnlyList<ActiveRuleBinding> RuleBindings => ruleBindings;

        /// <summary>
        /// Gets the prepared effects so a feature can derive pre-commit enrollment inputs from
        /// its own restored state.
        /// </summary>
        internal IReadOnlyList<ActiveEffectInstance> ActiveEffects => activeEffects;

        /// <summary>
        /// Adds feature-owned candidates to the single initiative calculation performed after all
        /// enrollment modules have prepared their state and before the atomic registration commit.
        /// </summary>
        internal void AddInitiativeModifiers(IEnumerable<Modifier> modifiers)
        {
            if (modifiers == null)
                throw new ArgumentNullException(nameof(modifiers));
            initiativeModifiers.AddRange(modifiers);
        }

        /// <summary>
        /// Resolves the enrollment initiative modifier once, including prepared feature candidates
        /// and the creature's ordinary typed-stacking inputs.
        /// </summary>
        internal int ResolveInitiative() =>
            InitiativeRules.ResolveModifier(Creature.initiative, statistics, initiativeModifiers);

        /// <summary>Freezes the prepared base and feature contributions into one immutable state.</summary>
        internal CombatantRulesState BuildState(int initiativeModifier) =>
            new(
                creatureState,
                statistics,
                health,
                position,
                landSpeed,
                initiativeModifier,
                spellSlots,
                ruleBindings,
                equipment,
                ammunition,
                activeEffects,
                activeEffectTimings,
                externalEffectReferences.ToArray()
            );
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

        /// <summary>Composes typed action presenters in exact module order.</summary>
        internal void ConfigureActionPresentation(UnityActionPresentationRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            foreach (
                IUnityEncounterActionPresentationModule module in modules.OfType<IUnityEncounterActionPresentationModule>()
            )
                module.ConfigureActionPresentation(registry);
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
