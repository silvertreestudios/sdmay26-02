using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Composes the feature-owned rules for the migrated pre-built-character spells.</summary>
    public static class SpellFeatureRules
    {
        /// <summary>Gets Shield's active-effect definition.</summary>
        public static RuleDefinitionId ShieldEffect { get; } = new("spell-shield-effect");

        /// <summary>Gets Guidance's consumable bonus definition.</summary>
        public static RuleDefinitionId GuidanceEffect { get; } = new("spell-guidance-effect");

        /// <summary>Gets Guidance's one-hour immunity definition.</summary>
        public static RuleDefinitionId GuidanceImmunity { get; } = new("spell-guidance-immunity");

        /// <summary>Gets Bless's active-effect definition.</summary>
        public static RuleDefinitionId BlessEffect { get; } = new("spell-bless-effect");

        /// <summary>Gets Infuse Vitality's active-effect definition.</summary>
        public static RuleDefinitionId InfuseVitalityEffect { get; } =
            new("spell-infuse-vitality-effect");

        /// <summary>Gets every lasting spell definition that uses <see cref="SpellEffectState"/>.</summary>
        public static IReadOnlyList<RuleDefinitionId> PersistentDefinitionIds { get; } =
            Array.AsReadOnly(
                new[]
                {
                    ShieldEffect,
                    GuidanceEffect,
                    GuidanceImmunity,
                    BlessEffect,
                    InfuseVitalityEffect,
                }
            );

        /// <summary>
        /// Determines whether one Infuse Vitality binding is the duplicate effect that currently
        /// applies to its owner.
        /// </summary>
        /// <remarks>
        /// PF2e duplicate effects use the highest spell rank, then the newest binding when ranks
        /// tie. Other still-active instances retain their independent source clocks and can become
        /// applicable after the winner expires, but they never contribute damage simultaneously.
        /// </remarks>
        internal static bool IsApplicableInfuseVitalityBinding(
            RulesSnapshot snapshot,
            ActiveRuleBinding candidate
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            ActiveRuleBinding applicable = snapshot
                .RuleBindings.Select(pair => pair.Value)
                .Where(binding =>
                    binding.IsEnabled
                    && binding.DefinitionId == InfuseVitalityEffect
                    && binding.Owner == candidate.Owner
                    && binding.EffectId.HasValue
                    && snapshot.ActiveEffects.Contains(binding.EffectId.Value)
                )
                .Select(binding => new
                {
                    Binding = binding,
                    Rank = snapshot
                        .ActiveEffects[binding.EffectId.Value]
                        .GetState<SpellEffectState>()
                        .Spell.Rank,
                })
                .OrderByDescending(value => value.Rank)
                .ThenByDescending(value => value.Binding.CreationOrder)
                .ThenByDescending(value => value.Binding.Id.Value, StringComparer.Ordinal)
                .Select(value => value.Binding)
                .FirstOrDefault();
            return applicable != null && applicable.Id == candidate.Id;
        }

        /// <summary>Creates the explicit spell-to-rule catalog used by encounter composition.</summary>
        public static IReadOnlyDictionary<SpellId, ISpellCastRule> CreateCatalog(
            ISpellCreatureDataProvider creatureData
        )
        {
            if (creatureData == null)
                throw new ArgumentNullException(nameof(creatureData));
            ISpellCastRule[] rules =
            {
                new ShieldSpellRule(),
                new GuidanceSpellRule(),
                new HauntingHymnSpellRule(creatureData),
                new BlessSpellRule(),
                new InfuseVitalitySpellRule(),
                new HealSpellRule(creatureData),
            };
            return rules.ToDictionary(rule => rule.Spell);
        }

        /// <summary>Defines all lasting spell bindings in the encounter registry.</summary>
        public static void DefineRuleBindings(RuleRegistryBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            builder
                .Define(ShieldEffect)
                .EffectState<SpellEffectState>()
                .Middleware<AdjustArmorClassOp, ModifierCollection>(
                    RuleLifecyclePhase.Transformation,
                    new ShieldArmorClassMiddleware()
                );
            builder
                .Define(GuidanceEffect)
                .EffectState<SpellEffectState>()
                .Middleware<CollectAttackModifiersOp, ModifierCollection>(
                    RuleLifecyclePhase.Transformation,
                    new GuidanceAttackMiddleware()
                )
                .Middleware<CollectSkillCheckModifiersOp, ModifierCollection>(
                    RuleLifecyclePhase.Transformation,
                    new GuidanceSkillMiddleware()
                )
                .Middleware<CollectSavingThrowModifiersOp, ModifierCollection>(
                    RuleLifecyclePhase.Transformation,
                    new GuidanceSaveMiddleware()
                )
                .FactListener<InitiativeAssignedFact>(
                    RuleLifecyclePhase.Observation,
                    new GuidanceInitiativeAssignedListener()
                )
                .FactListener<InitiativeBoundaryReachedFact>(
                    RuleLifecyclePhase.Observation,
                    new GuidanceInitiativeBoundaryListener()
                );
            builder.Define(GuidanceImmunity).EffectState<SpellEffectState>();
            builder
                .Define(BlessEffect)
                .EffectState<SpellEffectState>()
                .Middleware<CollectAttackModifiersOp, ModifierCollection>(
                    RuleLifecyclePhase.Transformation,
                    new BlessAttackMiddleware()
                );
            builder
                .Define(InfuseVitalityEffect)
                .EffectState<SpellEffectState>()
                .Middleware<CollectStrikeDamageDiceOp, IReadOnlyList<TypedDamageDice>>(
                    RuleLifecyclePhase.Transformation,
                    new InfuseVitalityDamageMiddleware()
                );
        }

        /// <summary>Registers feature workflows dispatched by spell binding listeners.</summary>
        /// <param name="builder">The encounter dispatcher under construction.</param>
        public static void ConfigureDispatcher(RuleDispatcherBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            builder.RegisterHandler<ExpireGuidanceOp, bool>(new ExpireGuidanceHandler());
        }

        /// <summary>
        /// Collects Guidance initiative candidates from a combatant's prepared restored state.
        /// </summary>
        /// <remarks>
        /// Enrollment calls this before the registration commit because the restored bindings are
        /// not yet visible through a dispatcher snapshot. The later initiative-assignment listener
        /// consumes every Guidance candidate through the ordinary active-effect lifecycle.
        /// </remarks>
        /// <param name="actor">The combatant whose initiative is being prepared.</param>
        /// <param name="bindings">The complete prepared binding collection for that combatant.</param>
        /// <param name="effects">The complete prepared restored-effect collection.</param>
        /// <returns>Guidance candidates to include in the actor's typed initiative resolution.</returns>
        /// <exception cref="ArgumentException"><paramref name="actor"/> is empty.</exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bindings"/> or <paramref name="effects"/> is <see langword="null"/>.
        /// </exception>
        public static IReadOnlyList<Modifier> CollectInitiativeModifiers(
            CreatureId actor,
            IReadOnlyList<ActiveRuleBinding> bindings,
            IReadOnlyList<ActiveEffectInstance> effects
        )
        {
            if (actor.IsEmpty)
                throw new ArgumentException("An initiative actor is required.", nameof(actor));
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));
            if (effects == null)
                throw new ArgumentNullException(nameof(effects));

            Dictionary<ActiveEffectId, ActiveEffectInstance> effectsById = effects.ToDictionary(
                effect => effect.Id
            );
            List<Modifier> modifiers = new();
            foreach (
                ActiveRuleBinding binding in bindings.Where(binding =>
                    binding.IsEnabled
                    && binding.DefinitionId == GuidanceEffect
                    && binding.Owner == actor
                    && binding.EffectId.HasValue
                )
            )
            {
                if (
                    !effectsById.TryGetValue(
                        binding.EffectId.Value,
                        out ActiveEffectInstance effect
                    )
                    || effect.DefinitionId != GuidanceEffect
                )
                    continue;
                effect.GetState<SpellEffectState>();
                modifiers.Add(Modifier.StatusBonus(1, binding.Source, Statistic.Initiative));
            }
            return modifiers.AsReadOnly();
        }

        private sealed class ExpireGuidanceOp : IRuleOp<bool>
        {
            public ExpireGuidanceOp(
                ActiveEffectId effect,
                BindingId binding,
                EffectStateVersion expectedVersion,
                ActiveEffectRemovalReason removalReason
            )
            {
                Effect = effect;
                Binding = binding;
                ExpectedVersion = expectedVersion;
                RemovalReason = removalReason;
            }

            public ActiveEffectId Effect { get; }
            public BindingId Binding { get; }
            public EffectStateVersion ExpectedVersion { get; }
            public ActiveEffectRemovalReason RemovalReason { get; }
        }

        private sealed class ExpireGuidanceHandler : IOpHandler<ExpireGuidanceOp, bool>
        {
            public async ValueTask<bool> Handle(
                OpFrame<ExpireGuidanceOp> frame,
                OpHandlerContext context
            )
            {
                if (
                    !context.Snapshot.RuleBindings.TryGet(
                        frame.Op.Binding,
                        out ActiveRuleBinding binding
                    )
                    || !binding.IsEnabled
                    || binding.DefinitionId != GuidanceEffect
                    || binding.EffectId != frame.Op.Effect
                    || !context.Snapshot.ActiveEffects.TryGet(
                        frame.Op.Effect,
                        out ActiveEffectInstance effect
                    )
                    || effect.EffectStateVersion != frame.Op.ExpectedVersion
                )
                    return false;
                SpellEffectState state = effect.GetState<SpellEffectState>();
                await SpellRuleSupport.RequireResolved(
                    context.Dispatch(
                        new RemoveActiveEffectOp(
                            effect.Id,
                            binding.Id,
                            effect.EffectStateVersion,
                            frame.Op.RemovalReason,
                            binding.Source
                        )
                    )
                );
                await SpellRuleSupport.CreateEffect(
                    context,
                    state.Spell,
                    effect.SourceCreature,
                    binding.Owner,
                    GuidanceImmunity,
                    EffectDuration.Minutes(60),
                    binding.Source,
                    frame.Id.Value,
                    string.Concat(
                        "unused-immunity:",
                        frame.Id.Value.ToString(CultureInfo.InvariantCulture)
                    ),
                    ResolveImmunityTimingSource(
                        context.Snapshot,
                        effect.SourceCreature,
                        binding.Owner
                    )
                );
                return true;
            }
        }

        private static CreatureId ResolveImmunityTimingSource(
            RulesSnapshot snapshot,
            CreatureId source,
            CreatureId owner
        )
        {
            EncounterState encounter = snapshot
                .Encounters.Select(pair => pair.Value)
                .FirstOrDefault(value =>
                    value.Phase == EncounterPhase.Initialized
                    || value.Phase == EncounterPhase.Active
                );
            return encounter == null || encounter.Roster.Any(entry => entry.Creature == source)
                ? source
                : owner;
        }

        private sealed class ShieldArmorClassMiddleware
            : IOpMiddleware<AdjustArmorClassOp, ModifierCollection>
        {
            public async ValueTask<OpResult<ModifierCollection>> Invoke(
                OpFrame<AdjustArmorClassOp> frame,
                OpMiddlewareContext context,
                OpNext<ModifierCollection> next
            )
            {
                OpResult<ModifierCollection> result = await next();
                return
                    context.Binding.Owner == frame.Op.Target
                    && result is ResolvedOpResult<ModifierCollection> resolved
                    ? OpResult<ModifierCollection>.Resolved(
                        resolved.Value.Add(
                            new Modifier(
                                1,
                                ModifierType.Circumstance,
                                context.Source,
                                Statistic.ArmorClass
                            )
                        )
                    )
                    : result;
            }
        }

        private sealed class BlessAttackMiddleware
            : IOpMiddleware<CollectAttackModifiersOp, ModifierCollection>
        {
            public async ValueTask<OpResult<ModifierCollection>> Invoke(
                OpFrame<CollectAttackModifiersOp> frame,
                OpMiddlewareContext context,
                OpNext<ModifierCollection> next
            )
            {
                OpResult<ModifierCollection> result = await next();
                return
                    context.Binding.Owner == frame.Op.Attacker
                    && result is ResolvedOpResult<ModifierCollection> resolved
                    ? OpResult<ModifierCollection>.Resolved(
                        resolved.Value.Add(
                            Modifier.StatusBonus(1, context.Source, Statistic.AttackRoll)
                        )
                    )
                    : result;
            }
        }

        private sealed class InfuseVitalityDamageMiddleware
            : IOpMiddleware<CollectStrikeDamageDiceOp, IReadOnlyList<TypedDamageDice>>
        {
            public async ValueTask<OpResult<IReadOnlyList<TypedDamageDice>>> Invoke(
                OpFrame<CollectStrikeDamageDiceOp> frame,
                OpMiddlewareContext context,
                OpNext<IReadOnlyList<TypedDamageDice>> next
            )
            {
                OpResult<IReadOnlyList<TypedDamageDice>> result = await next();
                if (
                    context.Binding.Owner != frame.Op.Attacker
                    || !IsApplicableInfuseVitalityBinding(context.Snapshot, context.Binding)
                    || !context
                        .Snapshot.Creatures[frame.Op.Target]
                        .Traits.Contains(Trait.FromSlug("undead"))
                    || result is not ResolvedOpResult<IReadOnlyList<TypedDamageDice>> resolved
                )
                    return result;
                ActiveEffectInstance effect = context.Snapshot.ActiveEffects[
                    context.Binding.EffectId.Value
                ];
                int rank = effect.GetState<SpellEffectState>().Spell.Rank;
                int dice =
                    rank >= 5 ? 3
                    : rank >= 3 ? 2
                    : 1;
                return OpResult<IReadOnlyList<TypedDamageDice>>.Resolved(
                    resolved
                        .Value.Concat(
                            new[]
                            {
                                new TypedDamageDice(
                                    new DiceExpression(dice, 4),
                                    "vitality",
                                    "infuse-vitality"
                                ),
                            }
                        )
                        .ToArray()
                );
            }
        }

        private abstract class GuidanceModifierMiddleware<TOp>
            : IOpMiddleware<TOp, ModifierCollection>
            where TOp : IRuleOp<ModifierCollection>
        {
            protected abstract bool Matches(TOp operation, CreatureId owner);
            protected abstract Statistic GetStatistic(TOp operation);

            public async ValueTask<OpResult<ModifierCollection>> Invoke(
                OpFrame<TOp> frame,
                OpMiddlewareContext context,
                OpNext<ModifierCollection> next
            )
            {
                OpResult<ModifierCollection> result = await next();
                if (
                    !Matches(frame.Op, context.Binding.Owner)
                    || result is not ResolvedOpResult<ModifierCollection> resolved
                )
                    return result;
                ActiveEffectInstance effect = context.Snapshot.ActiveEffects[
                    context.Binding.EffectId.Value
                ];
                await SpellRuleSupport.RequireResolved(
                    context.Dispatch(
                        new RemoveActiveEffectOp(
                            effect.Id,
                            context.Binding.Id,
                            effect.EffectStateVersion,
                            ActiveEffectRemovalReason.Ended,
                            context.Source
                        )
                    )
                );
                await SpellRuleSupport.CreateEffect(
                    context,
                    effect.GetState<SpellEffectState>().Spell,
                    effect.SourceCreature,
                    context.Binding.Owner,
                    GuidanceImmunity,
                    EffectDuration.Minutes(60),
                    context.Source,
                    frame.Id.Value,
                    string.Concat(
                        "immunity:",
                        frame.Id.Value.ToString(CultureInfo.InvariantCulture)
                    ),
                    ResolveImmunityTimingSource(
                        context.Snapshot,
                        effect.SourceCreature,
                        context.Binding.Owner
                    )
                );
                return OpResult<ModifierCollection>.Resolved(
                    resolved.Value.Add(
                        Modifier.StatusBonus(1, context.Source, GetStatistic(frame.Op))
                    )
                );
            }
        }

        private sealed class GuidanceAttackMiddleware
            : GuidanceModifierMiddleware<CollectAttackModifiersOp>
        {
            protected override bool Matches(CollectAttackModifiersOp operation, CreatureId owner) =>
                operation.Attacker == owner;

            protected override Statistic GetStatistic(CollectAttackModifiersOp operation) =>
                Statistic.AttackRoll;
        }

        private sealed class GuidanceSkillMiddleware
            : GuidanceModifierMiddleware<CollectSkillCheckModifiersOp>
        {
            protected override bool Matches(
                CollectSkillCheckModifiersOp operation,
                CreatureId owner
            ) => operation.Actor == owner;

            protected override Statistic GetStatistic(CollectSkillCheckModifiersOp operation) =>
                Statistic.SkillCheck;
        }

        private sealed class GuidanceSaveMiddleware
            : GuidanceModifierMiddleware<CollectSavingThrowModifiersOp>
        {
            protected override bool Matches(
                CollectSavingThrowModifiersOp operation,
                CreatureId owner
            ) => operation.Actor == owner;

            protected override Statistic GetStatistic(CollectSavingThrowModifiersOp operation) =>
                StatisticFor(operation.Save);

            private static Statistic StatisticFor(SaveKind save) =>
                save switch
                {
                    SaveKind.Fortitude => Statistic.FortitudeSave,
                    SaveKind.Reflex => Statistic.ReflexSave,
                    SaveKind.Will => Statistic.WillSave,
                    _ => throw new ArgumentOutOfRangeException(nameof(save)),
                };
        }

        private sealed class GuidanceInitiativeAssignedListener
            : IRuleFactListener<InitiativeAssignedFact>
        {
            public async ValueTask OnFactCommitted(InitiativeAssignedFact fact, FactContext context)
            {
                if (
                    fact.Entry.Creature != context.Binding.Owner
                    || !context.Binding.EffectId.HasValue
                    || !context.Snapshot.ActiveEffects.TryGet(
                        context.Binding.EffectId.Value,
                        out ActiveEffectInstance effect
                    )
                )
                    return;
                await SpellRuleSupport.RequireResolved(
                    context.Dispatch(
                        new ExpireGuidanceOp(
                            effect.Id,
                            context.Binding.Id,
                            effect.EffectStateVersion,
                            ActiveEffectRemovalReason.Ended
                        )
                    )
                );
            }
        }

        private sealed class GuidanceInitiativeBoundaryListener
            : IRuleFactListener<InitiativeBoundaryReachedFact>
        {
            public async ValueTask OnFactCommitted(
                InitiativeBoundaryReachedFact fact,
                FactContext context
            )
            {
                if (!context.Binding.EffectId.HasValue)
                    return;
                ActiveEffectInstance effect = context.Snapshot.ActiveEffects[
                    context.Binding.EffectId.Value
                ];
                bool reachedSource = fact.Creature == effect.SourceCreature;
                bool reachedNewRoundWithoutSource =
                    context.Snapshot.Encounters.TryGet(fact.Encounter, out EncounterState encounter)
                    && encounter.Round == fact.Round
                    && encounter.Cursor == 0
                    && !encounter.Roster.Any(entry => entry.Creature == effect.SourceCreature);
                if (!reachedSource && !reachedNewRoundWithoutSource)
                    return;
                await SpellRuleSupport.RequireResolved(
                    context.Dispatch(
                        new ExpireGuidanceOp(
                            effect.Id,
                            context.Binding.Id,
                            effect.EffectStateVersion,
                            ActiveEffectRemovalReason.Expired
                        )
                    )
                );
            }
        }
    }

    internal abstract class SpellCastRuleBase : ISpellCastRule
    {
        protected static readonly IRulesSelectors Selectors = new RulesSelectors();

        public abstract SpellId Spell { get; }
        public abstract SpellSelectionProfile GetSelection(SpellActionVariant variant);
        public abstract ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CastSpellActionOp operation
        );
        public abstract ValueTask<SpellFeatureOutcome> Resolve(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context,
            ISpellActionCatalog catalog
        );

        protected static ActionValidationResult ValidateTargets(
            RulesSnapshot snapshot,
            CastSpellActionOp operation,
            int minimum,
            int maximum,
            int rangeFeet,
            bool requireFriendly
        )
        {
            IReadOnlyList<CreatureId> targets = operation.Selection.Creatures;
            if (targets.Count < minimum || targets.Count > maximum)
                return ActionValidationResult.Invalid("The spell has an invalid target count.");
            if (targets.Distinct().Count() != targets.Count)
                return ActionValidationResult.Invalid(
                    "A spell cannot select the same creature twice."
                );
            foreach (CreatureId target in targets)
            {
                if (
                    !snapshot.Creatures.Contains(target)
                    || !snapshot.Health.TryGet(target, out HealthState health)
                    || !health.IsLiving
                )
                    return ActionValidationResult.Invalid(
                        "A selected spell target is unavailable."
                    );
                if (requireFriendly && Selectors.IsEnemy(snapshot, operation.Actor, target))
                    return ActionValidationResult.Invalid("The spell requires a willing ally.");
                if (Selectors.Distance(snapshot, operation.Actor, target).Feet > rangeFeet)
                    return ActionValidationResult.Invalid(
                        "A selected spell target is out of range."
                    );
            }
            return ActionValidationResult.Valid;
        }
    }

    internal sealed class ShieldSpellRule : SpellCastRuleBase
    {
        public override SpellId Spell { get; } = new("shield");

        public override SpellSelectionProfile GetSelection(SpellActionVariant variant) =>
            SpellSelectionProfile.None;

        public override ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CastSpellActionOp operation
        ) =>
            operation.Selection.Creatures.Count == 0
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid("Shield does not select a target.");

        public override async ValueTask<SpellFeatureOutcome> Resolve(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context,
            ISpellActionCatalog catalog
        )
        {
            ActiveEffectId effect = await SpellRuleSupport.ReplaceEffect(
                context,
                frame.Op.Spell,
                frame.Op.Actor,
                frame.Op.Actor,
                SpellFeatureRules.ShieldEffect,
                EffectDuration.Rounds(1),
                frame.Id,
                "shield"
            );
            return SpellRuleSupport.EffectsOnly(effect);
        }
    }

    internal sealed class GuidanceSpellRule : SpellCastRuleBase
    {
        public override SpellId Spell { get; } = new("guidance");

        public override SpellSelectionProfile GetSelection(SpellActionVariant variant) =>
            new(SpellSelectionKind.SingleCreature, rangeFeet: 30, exactCreatureCount: 1);

        public override ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CastSpellActionOp operation
        )
        {
            ActionValidationResult targets = ValidateTargets(snapshot, operation, 1, 1, 30, true);
            if (targets is not ActionValidationResult.ValidActionValidationResult)
                return targets;
            CreatureId target = operation.Selection.Creatures[0];
            bool guided = snapshot.RuleBindings.Any(pair =>
                pair.Value.IsEnabled
                && pair.Value.DefinitionId == SpellFeatureRules.GuidanceEffect
                && pair.Value.Owner == target
            );
            if (guided)
                return ActionValidationResult.Invalid("The target already has active Guidance.");
            bool immune = snapshot.RuleBindings.Any(pair =>
                pair.Value.IsEnabled
                && pair.Value.DefinitionId == SpellFeatureRules.GuidanceImmunity
                && pair.Value.Owner == target
            );
            return immune
                ? ActionValidationResult.Invalid("The target is temporarily immune to Guidance.")
                : ActionValidationResult.Valid;
        }

        public override async ValueTask<SpellFeatureOutcome> Resolve(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context,
            ISpellActionCatalog catalog
        )
        {
            CreatureId target = frame.Op.Selection.Creatures[0];
            ActiveEffectId effect = await SpellRuleSupport.ReplaceEffect(
                context,
                frame.Op.Spell,
                frame.Op.Actor,
                target,
                SpellFeatureRules.GuidanceEffect,
                EffectDuration.Indefinite,
                frame.Id,
                "guidance"
            );
            return SpellRuleSupport.EffectsOnly(effect);
        }
    }

    internal sealed class BlessSpellRule : SpellCastRuleBase
    {
        public override SpellId Spell { get; } = new("bless");

        public override SpellSelectionProfile GetSelection(SpellActionVariant variant) =>
            new(
                SpellSelectionKind.Emanation,
                areaFeet: 15,
                includeCaster: true,
                friendlyOnly: true
            );

        public override ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CastSpellActionOp operation
        ) => ValidateTargets(snapshot, operation, 1, int.MaxValue, 15, true);

        public override async ValueTask<SpellFeatureOutcome> Resolve(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context,
            ISpellActionCatalog catalog
        )
        {
            List<ActiveEffectId> effects = new();
            foreach (CreatureId target in frame.Op.Selection.Creatures)
            {
                effects.Add(
                    await SpellRuleSupport.ReplaceEffect(
                        context,
                        frame.Op.Spell,
                        frame.Op.Actor,
                        target,
                        SpellFeatureRules.BlessEffect,
                        EffectDuration.OneMinute,
                        frame.Id,
                        string.Concat("bless:", target.Value)
                    )
                );
            }
            return new SpellFeatureOutcome(effects, Array.Empty<SpellTargetResolution>());
        }
    }

    internal sealed class InfuseVitalitySpellRule : SpellCastRuleBase
    {
        public override SpellId Spell { get; } = new("infuse-vitality");

        public override SpellSelectionProfile GetSelection(SpellActionVariant variant) =>
            new(
                SpellSelectionKind.ExactCreatureCount,
                rangeFeet: 30,
                exactCreatureCount: variant.Actions
            );

        public override ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CastSpellActionOp operation
        ) =>
            ValidateTargets(
                snapshot,
                operation,
                operation.Variant.Actions,
                operation.Variant.Actions,
                30,
                true
            );

        public override async ValueTask<SpellFeatureOutcome> Resolve(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context,
            ISpellActionCatalog catalog
        )
        {
            List<ActiveEffectId> effects = new();
            foreach (CreatureId target in frame.Op.Selection.Creatures)
            {
                effects.Add(
                    await SpellRuleSupport.CreateEffect(
                        context,
                        frame.Op.Spell,
                        frame.Op.Actor,
                        target,
                        SpellFeatureRules.InfuseVitalityEffect,
                        EffectDuration.OneMinute,
                        RuleSource.FromSlug(frame.Op.Spell.Spell.Value),
                        frame.Id.Value,
                        string.Concat(
                            "infuse-vitality:",
                            target.Value,
                            ":",
                            frame.Id.Value.ToString(CultureInfo.InvariantCulture)
                        )
                    )
                );
            }
            return new SpellFeatureOutcome(effects, Array.Empty<SpellTargetResolution>());
        }
    }

    internal abstract class BasicFortitudeSpellRule : SpellCastRuleBase
    {
        private readonly ISpellCreatureDataProvider creatureData;

        protected BasicFortitudeSpellRule(ISpellCreatureDataProvider creatureData) =>
            this.creatureData =
                creatureData ?? throw new ArgumentNullException(nameof(creatureData));

        protected async ValueTask<DegreeOfSuccess> ResolveSave(
            CreatureId target,
            int dc,
            OpId frameId,
            OpHandlerContext context
        )
        {
            CheckOutcome save = await SpellRuleSupport.RequireResolved(
                context.Dispatch(
                    new SavingThrowOp(target, SaveKind.Fortitude, dc, CheckSource.From(frameId))
                )
            );
            return save.Degree;
        }

        protected TypedDamagePart ResolveDamage(
            CreatureId target,
            DegreeOfSuccess degree,
            int rolled,
            string damageType,
            RuleSource source
        )
        {
            int amount = degree switch
            {
                DegreeOfSuccess.CriticalSuccess => 0,
                DegreeOfSuccess.Success => rolled / 2,
                DegreeOfSuccess.Failure => rolled,
                DegreeOfSuccess.CriticalFailure => checked(rolled * 2),
                _ => throw new ArgumentOutOfRangeException(),
            };
            TypedDefenseAdjustment weakness = creatureData
                .GetWeaknesses(target)
                .FirstOrDefault(value =>
                    string.Equals(value.DamageType, damageType, StringComparison.OrdinalIgnoreCase)
                );
            TypedDefenseAdjustment resistance = creatureData
                .GetResistances(target)
                .FirstOrDefault(value =>
                    string.Equals(value.DamageType, damageType, StringComparison.OrdinalIgnoreCase)
                );
            if (weakness != null && amount > 0)
                amount = checked(amount + weakness.Amount);
            if (resistance != null)
                amount = Math.Max(0, amount - resistance.Amount);
            return new TypedDamagePart(damageType, amount, new[] { source.Slug });
        }
    }

    internal sealed class HauntingHymnSpellRule : BasicFortitudeSpellRule
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("haunting-hymn");

        public HauntingHymnSpellRule(ISpellCreatureDataProvider creatureData)
            : base(creatureData) { }

        public override SpellId Spell { get; } = new("haunting-hymn");

        public override SpellSelectionProfile GetSelection(SpellActionVariant variant) =>
            new(SpellSelectionKind.Cone, areaFeet: 15);

        public override ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CastSpellActionOp operation
        ) => ValidateTargets(snapshot, operation, 0, int.MaxValue, 15, false);

        public override async ValueTask<SpellFeatureOutcome> Resolve(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context,
            ISpellActionCatalog catalog
        )
        {
            int dc = catalog.GetSpellBook(frame.Op.Actor).SpellDc;
            int dice = 1 + ((frame.Op.Spell.Rank - 1) / 2);
            List<(CreatureId Target, DegreeOfSuccess Degree)> saves = new();
            foreach (CreatureId target in frame.Op.Selection.Creatures)
            {
                DegreeOfSuccess degree = await ResolveSave(target, dc, frame.Id, context);
                saves.Add((target, degree));
            }
            int sharedRoll =
                saves.Count == 0 ? 0 : context.Rolls.Roll(new DiceExpression(dice, 8)).Total;
            List<(CreatureId Target, DegreeOfSuccess Degree, TypedDamagePart Damage)> rolled = saves
                .Select(value =>
                    (
                        value.Target,
                        value.Degree,
                        ResolveDamage(value.Target, value.Degree, sharedRoll, "sonic", Source)
                    )
                )
                .ToList();
            if (rolled.Count > 0)
            {
                await SpellRuleSupport.RequireResolved(
                    context.Dispatch(
                        new ApplyHealthBatchOp(
                            rolled.Select(
                                (value, index) =>
                                    new HealthBatchChange(
                                        HealthBatchChangeKind.Damage,
                                        value.Target,
                                        value.Damage.Amount,
                                        new HealthChangeOriginId(
                                            $"haunting-hymn-{frame.RootId.Value}-{index}"
                                        ),
                                        Source
                                    )
                            )
                        )
                    )
                );
            }
            List<SpellTargetResolution> outcomes = new();
            foreach (var value in rolled)
            {
                bool deafened = value.Degree == DegreeOfSuccess.CriticalFailure;
                if (deafened)
                {
                    await SpellRuleSupport.RequireResolved(
                        context.Dispatch(
                            new ApplyConditionOp(
                                value.Target,
                                new ConditionId("Deafened"),
                                1,
                                frame.Op.Actor,
                                Source,
                                EffectDuration.OneMinute
                            )
                        )
                    );
                }
                outcomes.Add(
                    new SpellTargetResolution(
                        value.Target,
                        value.Degree,
                        new[] { value.Damage },
                        0,
                        deafened
                    )
                );
            }
            return new SpellFeatureOutcome(Array.Empty<ActiveEffectId>(), outcomes);
        }
    }

    internal sealed class HealSpellRule : BasicFortitudeSpellRule
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("heal");
        private readonly ISpellCreatureDataProvider creatureData;

        public HealSpellRule(ISpellCreatureDataProvider creatureData)
            : base(creatureData) => this.creatureData = creatureData;

        public override SpellId Spell { get; } = new("heal");

        public override SpellSelectionProfile GetSelection(SpellActionVariant variant) =>
            variant.Actions switch
            {
                1 => new SpellSelectionProfile(
                    SpellSelectionKind.SingleCreature,
                    rangeFeet: 5,
                    exactCreatureCount: 1
                ),
                2 => new SpellSelectionProfile(
                    SpellSelectionKind.SingleCreature,
                    rangeFeet: 30,
                    exactCreatureCount: 1
                ),
                3 => new SpellSelectionProfile(
                    SpellSelectionKind.Emanation,
                    areaFeet: 30,
                    includeCaster: true
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(variant)),
            };

        public override ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CastSpellActionOp operation
        )
        {
            int range = operation.Variant.Actions == 1 ? 5 : 30;
            int minimum = operation.Variant.Actions == 3 ? 0 : 1;
            int maximum = operation.Variant.Actions == 3 ? int.MaxValue : 1;
            ActionValidationResult targets = ValidateTargets(
                snapshot,
                operation,
                minimum,
                maximum,
                range,
                false
            );
            if (targets is not ActionValidationResult.ValidActionValidationResult)
                return targets;
            foreach (CreatureId target in operation.Selection.Creatures)
            {
                if (
                    operation.Variant.Actions != 3
                    && !creatureData.IsUndead(target)
                    && Selectors.IsEnemy(snapshot, operation.Actor, target)
                )
                    return ActionValidationResult.Invalid(
                        "Heal requires a willing living target or an undead target."
                    );
            }
            return ActionValidationResult.Valid;
        }

        public override async ValueTask<SpellFeatureOutcome> Resolve(
            OpFrame<CastSpellActionOp> frame,
            OpHandlerContext context,
            ISpellActionCatalog catalog
        )
        {
            int dc = catalog.GetSpellBook(frame.Op.Actor).SpellDc;
            Dictionary<CreatureId, DegreeOfSuccess> undeadSaves = new();
            foreach (CreatureId target in frame.Op.Selection.Creatures.Where(creatureData.IsUndead))
                undeadSaves.Add(target, await ResolveSave(target, dc, frame.Id, context));
            int sharedRoll =
                frame.Op.Selection.Creatures.Count == 0
                    ? 0
                    : context.Rolls.Roll(new DiceExpression(frame.Op.Spell.Rank, 8)).Total;
            List<SpellTargetResolution> outcomes = new();
            foreach (CreatureId target in frame.Op.Selection.Creatures)
            {
                if (creatureData.IsUndead(target))
                {
                    DegreeOfSuccess degree = undeadSaves[target];
                    TypedDamagePart damage = ResolveDamage(
                        target,
                        degree,
                        sharedRoll,
                        "vitality",
                        Source
                    );
                    await SpellRuleSupport.RequireResolved(
                        context.Dispatch(
                            new ApplyDamageOp(
                                target,
                                damage.Amount,
                                new HealthChangeOriginId(
                                    $"heal-{frame.RootId.Value}-{target.Value}"
                                ),
                                Source
                            )
                        )
                    );
                    outcomes.Add(
                        new SpellTargetResolution(target, degree, new[] { damage }, 0, false)
                    );
                }
                else
                {
                    int amount = sharedRoll;
                    if (frame.Op.Variant.Actions == 2)
                        amount = checked(amount + (8 * frame.Op.Spell.Rank));
                    HealingOutcome healing = await SpellRuleSupport.RequireResolved(
                        context.Dispatch(
                            new ApplyHealingOp(
                                target,
                                amount,
                                new HealthChangeOriginId(
                                    $"heal-{frame.RootId.Value}-{target.Value}"
                                ),
                                Source
                            )
                        )
                    );
                    outcomes.Add(
                        new SpellTargetResolution(
                            target,
                            null,
                            Array.Empty<TypedDamagePart>(),
                            healing.Applied,
                            false
                        )
                    );
                }
            }
            return new SpellFeatureOutcome(Array.Empty<ActiveEffectId>(), outcomes);
        }
    }

    internal static class SpellRuleSupport
    {
        public static SpellFeatureOutcome EffectsOnly(ActiveEffectId effect) =>
            new(new[] { effect }, Array.Empty<SpellTargetResolution>());

        public static async ValueTask<ActiveEffectId> ReplaceEffect(
            OpHandlerContext context,
            SpellReference spell,
            CreatureId caster,
            CreatureId target,
            RuleDefinitionId definition,
            EffectDuration duration,
            OpId frameId,
            string localIdentity
        )
        {
            ActiveRuleBinding existing = context
                .Snapshot.RuleBindings.Select(pair => pair.Value)
                .FirstOrDefault(binding =>
                    binding.IsEnabled
                    && binding.DefinitionId == definition
                    && binding.Owner == target
                    && binding.EffectId.HasValue
                );
            if (existing != null)
            {
                ActiveEffectInstance old = context.Snapshot.ActiveEffects[existing.EffectId.Value];
                await RequireResolved(
                    context.Dispatch(
                        new RemoveActiveEffectOp(
                            old.Id,
                            existing.Id,
                            old.EffectStateVersion,
                            ActiveEffectRemovalReason.Ended,
                            RuleSource.FromSlug(spell.Spell.Value)
                        )
                    )
                );
            }
            return await CreateEffect(
                context,
                spell,
                caster,
                target,
                definition,
                duration,
                RuleSource.FromSlug(spell.Spell.Value),
                frameId.Value,
                string.Concat(
                    localIdentity,
                    ":",
                    frameId.Value.ToString(CultureInfo.InvariantCulture)
                )
            );
        }

        public static ValueTask<ActiveEffectId> CreateEffect(
            OpCallbackContext context,
            SpellReference spell,
            CreatureId caster,
            CreatureId target,
            RuleDefinitionId definition,
            EffectDuration duration,
            RuleSource source,
            long creationOrder,
            string localIdentity
        ) =>
            CreateEffect(
                context,
                spell,
                caster,
                target,
                definition,
                duration,
                source,
                creationOrder,
                localIdentity,
                caster
            );

        public static async ValueTask<ActiveEffectId> CreateEffect(
            OpCallbackContext context,
            SpellReference spell,
            CreatureId caster,
            CreatureId target,
            RuleDefinitionId definition,
            EffectDuration duration,
            RuleSource source,
            long creationOrder,
            string localIdentity,
            CreatureId timingSourceCreature
        )
        {
            var identity = context.CreateActiveEffectIdentity("spell", localIdentity);
            ActiveEffectInstance effect = new(
                identity.EffectId,
                definition,
                caster,
                source,
                duration,
                new SpellEffectState(spell, target)
            );
            ActiveRuleBinding binding = new(
                identity.BindingId,
                definition,
                target,
                identity.EffectId,
                source,
                creationOrder
            );
            await RequireResolved(
                context.Dispatch(new CreateActiveEffectOp(effect, binding, timingSourceCreature))
            );
            return identity.EffectId;
        }

        public static async ValueTask<TResult> RequireResolved<TResult>(
            ValueTask<OpResult<TResult>> pending
        )
        {
            OpResult<TResult> result = await pending;
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException("A nested spell operation did not resolve.");
        }
    }
}
