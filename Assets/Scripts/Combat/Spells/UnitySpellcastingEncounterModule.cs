using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Spells;
using GridPrivate;

namespace Game.Combat.Spells
{
    /// <summary>Owns generic spellcasting rules, presentation, and action installation composition.</summary>
    internal sealed class UnitySpellcastingEncounterModule
        : IUnityEncounterDispatcherModule,
            IUnityEncounterRuntimeModule,
            IUnityEncounterActionPresentationModule,
            IUnityEncounterTopologyModule,
            IUnityCombatantEnrollmentModule
    {
        internal static readonly RuleDefinitionId RestoredTimedEffectDefinitionId = new(
            "restored-spell-effect-timing"
        );

        private readonly UnityCombatRulesBridge owner;
        private readonly ISpellActionCatalog catalog;
        private readonly UnitySpellAttackContext attackContext;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly bool installUnityAuthority;
        private readonly Dictionary<ActiveEffectId, RestoredSpellEffectProjection> restoredEffects =
            new();

        internal UnitySpellcastingEncounterModule(
            UnityCombatRulesBridge owner,
            ISpellActionCatalog catalog,
            UnitySpellAttackContext attackContext,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            bool installUnityAuthority
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.attackContext =
                attackContext ?? throw new ArgumentNullException(nameof(attackContext));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.installUnityAuthority = installUnityAuthority;
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder)
        {
            builder
                .UseSpellcastingRules(catalog, attackContext)
                .RegisterHandler<AdoptRestoredSpellEffectsOp, bool>(
                    new AdoptRestoredSpellEffectsHandler()
                );
        }

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            RestoredSpellEffectTimingObserver restored = new(restoredEffects);
            lifetime.Add(dispatcher.RegisterFactObserver<InitiativeBoundaryReachedFact>(restored));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectExpiredFact>(restored));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(restored));
        }

        /// <inheritdoc/>
        public void ConfigureActionPresentation(UnityActionPresentationRegistry registry)
        {
            if (!installUnityAuthority)
                return;
            registry.Register<CastSpellActionOp, CastSpellOutcome>(
                CastSpellActionDefinition.DefinitionId,
                new UnitySpellActionPresenter(creatures, catalog)
            );
        }

        /// <inheritdoc/>
        public void RefreshTopology(Tile[,] tiles) => attackContext.ReplaceTiles(tiles);

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            SpellEffectController controller =
                builder.Controller.GetComponent<SpellEffectController>();
            if (installUnityAuthority && controller != null)
            {
                RestoredSpellEffectProjection[] projections = controller
                    .Effects.Select(
                        (effect, index) =>
                            RestoredSpellEffectProjection.TryCreate(
                                controller,
                                builder.CreatureId,
                                effect,
                                index
                            )
                    )
                    .Where(projection => projection != null)
                    .ToArray();
                if (projections.Length > 0)
                    builder.AddState(
                        builder.Own(
                            new RestoredSpellEffectContribution(owner, restoredEffects, projections)
                        )
                    );
            }
            builder.AddSpellSlots(
                catalog.GetSpellBook(builder.CreatureId).CreateInitialSlotStates(builder.CreatureId)
            );
            if (!installUnityAuthority)
                return;
            builder.AddInstallation(
                UnitySpellActionInstaller.Prepare(builder.Controller, builder.CreatureId, catalog)
            );
        }
    }

    /// <summary>
    /// Carries restored finite spell effects through the feature-owned root workflow used for
    /// reinforcement enrollment.
    /// </summary>
    internal sealed class AdoptRestoredSpellEffectsOp : IRuleOp<bool>
    {
        internal AdoptRestoredSpellEffectsOp(
            IEnumerable<RestoredSpellEffectRegistration> registrations
        )
        {
            Registrations =
                registrations?.ToArray() ?? throw new ArgumentNullException(nameof(registrations));
        }

        internal IReadOnlyList<RestoredSpellEffectRegistration> Registrations { get; }
    }

    internal sealed class AdoptRestoredSpellEffectsHandler
        : IOpHandler<AdoptRestoredSpellEffectsOp, bool>
    {
        public async ValueTask<bool> Handle(
            OpFrame<AdoptRestoredSpellEffectsOp> frame,
            OpHandlerContext context
        )
        {
            foreach (RestoredSpellEffectRegistration registration in frame.Op.Registrations)
            {
                OpResult<ActiveEffectCreationOutcome> result = await context.Dispatch(
                    new CreateActiveEffectOp(registration.Effect, registration.Binding)
                );
                if (result is not ResolvedOpResult<ActiveEffectCreationOutcome>)
                    throw new InvalidOperationException(
                        "Restored spell-effect adoption did not resolve."
                    );
            }
            return true;
        }
    }

    /// <summary>
    /// Owns pre-encounter restored spell-effect state until the encounter releases its complete
    /// composition.
    /// </summary>
    internal sealed class RestoredSpellEffectContribution
        : IUnityCombatantStateContribution,
            IDisposable
    {
        private readonly UnityCombatRulesBridge owner;
        private readonly IDictionary<ActiveEffectId, RestoredSpellEffectProjection> projections;
        private readonly IReadOnlyList<RestoredSpellEffectProjection> owned;
        private bool isDisposed;

        internal RestoredSpellEffectContribution(
            UnityCombatRulesBridge owner,
            IDictionary<ActiveEffectId, RestoredSpellEffectProjection> projections,
            IEnumerable<RestoredSpellEffectProjection> owned
        )
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.projections = projections ?? throw new ArgumentNullException(nameof(projections));
            RestoredSpellEffectProjection[] copied =
                owned?.ToArray() ?? throw new ArgumentNullException(nameof(owned));
            if (
                copied.Any(projection => projection == null)
                || copied.Select(projection => projection.EffectId).Distinct().Count()
                    != copied.Length
                || copied.Any(projection => projections.ContainsKey(projection.EffectId))
            )
                throw new InvalidOperationException(
                    "Restored spell effects require unique encounter identities."
                );
            this.owned = copied;
            foreach (RestoredSpellEffectProjection projection in copied)
                projections.Add(projection.EffectId, projection);
        }

        /// <inheritdoc/>
        public void Seed(RulesStateSeed seed)
        {
            if (seed == null)
                throw new ArgumentNullException(nameof(seed));
            foreach (RestoredSpellEffectProjection projection in owned)
            {
                RestoredSpellEffectRegistration registration = projection.CreateRegistration(owner);
                seed.SeedActiveEffect(registration.Effect).SeedRuleBinding(registration.Binding);
            }
        }

        /// <inheritdoc/>
        public void Register(UnityCombatRulesBridge bridge)
        {
            if (bridge == null)
                throw new ArgumentNullException(nameof(bridge));
            RestoredSpellEffectRegistration[] registrations = owned
                .Select(projection => projection.CreateRegistration(owner))
                .ToArray();
            OpResult<bool> result = bridge.Dispatch(new AdoptRestoredSpellEffectsOp(registrations));
            if (result is ResolvedOpResult<bool>)
                return;
            if (result is InvalidOpResult<bool> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException(
                "Restored spell-effect enrollment did not resolve."
            );
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (isDisposed)
                return;
            isDisposed = true;
            foreach (RestoredSpellEffectProjection projection in owned)
            {
                if (
                    projections.TryGetValue(
                        projection.EffectId,
                        out RestoredSpellEffectProjection current
                    ) && ReferenceEquals(current, projection)
                )
                    projections.Remove(projection.EffectId);
            }
        }
    }

    internal sealed class RestoredSpellEffectProjection
    {
        private RestoredSpellEffectProjection(
            SpellEffectController controller,
            CreatureId target,
            ActiveSpellEffect effect,
            int index,
            string spellSlug,
            EffectDuration duration
        )
        {
            Controller = controller;
            Target = target;
            Effect = effect;
            SpellSlug = spellSlug;
            Duration = duration;
            EffectId = new ActiveEffectId(
                $"restored-spell-effect-{target.Value}-{spellSlug}-{index}"
            );
            BindingId = new BindingId($"restored-spell-binding-{target.Value}-{spellSlug}-{index}");
            CreationOrder = index;
        }

        internal ActiveEffectId EffectId { get; }
        internal BindingId BindingId { get; }
        internal ActiveSpellEffect Effect { get; }
        private SpellEffectController Controller { get; }
        private CreatureId Target { get; }
        private string SpellSlug { get; }
        private EffectDuration Duration { get; }
        private long CreationOrder { get; }

        internal static RestoredSpellEffectProjection TryCreate(
            SpellEffectController controller,
            CreatureId target,
            ActiveSpellEffect effect,
            int index
        )
        {
            if (effect is ShieldSpellEffect)
                return new RestoredSpellEffectProjection(
                    controller,
                    target,
                    effect,
                    index,
                    "shield",
                    EffectDuration.Rounds(1)
                );
            if (effect is BlessSpellEffect)
                return CreateCounted(controller, target, effect, index, "bless");
            if (effect is InfuseVitalitySpellEffect)
                return CreateCounted(controller, target, effect, index, "infuse-vitality");
            return null;
        }

        internal RestoredSpellEffectRegistration CreateRegistration(UnityCombatRulesBridge owner)
        {
            if (Effect.Source == null)
                throw new InvalidOperationException(
                    $"Restored {Effect.SourceLabel} has no live source creature."
                );
            CreatureComponent source = Effect.Source.GetComponent<CreatureComponent>();
            if (source == null || !owner.TryGetCreatureId(source, out CreatureId sourceId))
                throw new InvalidOperationException(
                    $"Restored {Effect.SourceLabel} source is not enrolled in this encounter."
                );
            RuleSource ruleSource = RuleSource.FromSlug(SpellSlug);
            ActiveEffectInstance active = new(
                EffectId,
                UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId,
                sourceId,
                ruleSource,
                Duration,
                new SpellEffectState(new SpellReference(new SpellId(SpellSlug), 1), Target)
            );
            ActiveRuleBinding binding = new(
                BindingId,
                UnitySpellcastingEncounterModule.RestoredTimedEffectDefinitionId,
                sourceId,
                EffectId,
                ruleSource,
                CreationOrder
            );
            return new RestoredSpellEffectRegistration(active, binding);
        }

        internal void ProjectRemaining(RulesSnapshot snapshot)
        {
            if (snapshot.ActiveEffectTimings.TryGet(EffectId, out ActiveEffectTimingState timing))
                Effect.RemainingTargetTurnStarts = timing.RemainingBoundaries;
        }

        internal void Remove() => Controller?.Remove(Effect);

        private static RestoredSpellEffectProjection CreateCounted(
            SpellEffectController controller,
            CreatureId target,
            ActiveSpellEffect effect,
            int index,
            string spellSlug
        )
        {
            if (effect.RemainingTargetTurnStarts <= 0)
                throw new InvalidOperationException(
                    $"Restored {effect.SourceLabel} requires a positive remaining duration."
                );
            return new RestoredSpellEffectProjection(
                controller,
                target,
                effect,
                index,
                spellSlug,
                EffectDuration.Rounds(effect.RemainingTargetTurnStarts)
            );
        }
    }

    internal sealed class RestoredSpellEffectRegistration
    {
        internal RestoredSpellEffectRegistration(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding
        )
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }

        internal ActiveEffectInstance Effect { get; }
        internal ActiveRuleBinding Binding { get; }
    }

    internal sealed class RestoredSpellEffectTimingObserver
        : IFactObserver<InitiativeBoundaryReachedFact>,
            IFactObserver<ActiveEffectExpiredFact>,
            IFactObserver<ActiveEffectRemovedFact>
    {
        private readonly IReadOnlyDictionary<
            ActiveEffectId,
            RestoredSpellEffectProjection
        > projections;

        internal RestoredSpellEffectTimingObserver(
            IReadOnlyDictionary<ActiveEffectId, RestoredSpellEffectProjection> projections
        ) => this.projections = projections ?? throw new ArgumentNullException(nameof(projections));

        public void OnFactCommitted(
            InitiativeBoundaryReachedFact fact,
            RulesSnapshot currentSnapshot
        )
        {
            foreach (RestoredSpellEffectProjection projection in projections.Values)
                projection.ProjectRemaining(currentSnapshot);
        }

        public void OnFactCommitted(ActiveEffectExpiredFact fact, RulesSnapshot currentSnapshot)
        {
            Remove(fact.EffectId);
        }

        public void OnFactCommitted(ActiveEffectRemovedFact fact, RulesSnapshot currentSnapshot)
        {
            Remove(fact.EffectId);
        }

        private void Remove(ActiveEffectId effect)
        {
            if (projections.TryGetValue(effect, out RestoredSpellEffectProjection projection))
                projection.Remove();
        }
    }
}
