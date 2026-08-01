using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ConditionAuthoritativeRuntimeTests
    {
        private static readonly CreatureId Owner = new CreatureId("condition-owner");
        private static readonly CreatureId SourceCreature = new CreatureId("condition-source");

        [Test]
        public async Task AliasApplyCommitsCanonicalGenericAndConditionFactsTogether()
        {
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);

            OpResult<ConditionCreationOutcome> result = await dispatcher.Dispatch(
                Apply("Flat-Footed", "alias-source", ConditionMarkerState.Instance)
            );

            ResolvedOpResult<ConditionCreationOutcome> resolved = RequireResolved(result);
            Assert.That(
                resolved.Facts.Select(fact => fact.GetType()),
                Is.EqualTo(new[] { typeof(ActiveEffectCreatedFact), typeof(ConditionCreatedFact) })
            );
            Assert.That(
                resolved.Facts.Select(fact => fact.RootOpId).Distinct().Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                store.Snapshot.ActiveEffects[resolved.Value.EffectId].DefinitionId,
                Is.EqualTo(ConditionRuleDefinitions.OffGuard)
            );
            Assert.That(
                ConditionSelectors.HasMarker(
                    store.Snapshot,
                    Owner,
                    ConditionRuleDefinitions.OffGuard
                ),
                Is.True
            );
        }

        [Test]
        public async Task AdoptionIsIdempotentWithoutDuplicateStateOrFacts()
        {
            RuleSource source = RuleSource.FromSlug("persisted-source");
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId("persisted-effect"),
                ConditionRuleDefinitions.Deafened,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("persisted-binding"),
                effect.DefinitionId,
                Owner,
                effect.Id,
                source,
                4
            );
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            AdoptConditionRegistrationsOp adopt = new AdoptConditionRegistrationsOp(
                new[] { new ConditionRegistration(effect, binding) }
            );

            ResolvedOpResult<ConditionAdoptionOutcome> first = RequireResolved(
                await dispatcher.Dispatch(adopt)
            );
            long committedVersion = store.Snapshot.Version;
            ResolvedOpResult<ConditionAdoptionOutcome> repeated = RequireResolved(
                await dispatcher.Dispatch(adopt)
            );

            Assert.That(first.Value.Adopted, Is.EqualTo(1));
            Assert.That(first.Facts, Has.Count.EqualTo(1));
            Assert.That(first.Facts.Single(), Is.TypeOf<ActiveEffectAdoptedFact>());
            Assert.That(repeated.Value.Adopted, Is.Zero);
            Assert.That(repeated.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(store.Snapshot.ActiveEffects[effect.Id], Is.EqualTo(effect));
        }

        [Test]
        public async Task AdoptionPublishesExactActiveAndExpiredProvenanceWithoutCreationFacts()
        {
            RuleSource source = RuleSource.FromSlug("restored-stunned-source");
            ActiveEffectInstance active = new ActiveEffectInstance(
                new ActiveEffectId("active-stunned-effect"),
                ConditionRuleDefinitions.Stunned,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                new ValuedStunnedConditionState(2),
                new EffectStateVersion(4),
                ActiveEffectStatus.Active
            );
            ActiveRuleBinding activeBinding = new ActiveRuleBinding(
                new BindingId("active-stunned-binding"),
                active.DefinitionId,
                Owner,
                active.Id,
                source,
                3
            );
            ActiveEffectInstance expired = new ActiveEffectInstance(
                new ActiveEffectId("expired-stunned-effect"),
                ConditionRuleDefinitions.Stunned,
                SourceCreature,
                source,
                EffectDuration.Encounter,
                DurationOnlyStunnedConditionState.Instance,
                new EffectStateVersion(7),
                ActiveEffectStatus.Expired
            );
            ActiveRuleBinding expiredBinding = new ActiveRuleBinding(
                new BindingId("expired-stunned-binding"),
                expired.DefinitionId,
                Owner,
                expired.Id,
                source,
                8,
                isEnabled: false
            );
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            CountingConditionCreatedObserver created = new CountingConditionCreatedObserver();
            using IDisposable registration = dispatcher.RegisterFactObserver<ConditionCreatedFact>(
                created
            );

            ResolvedOpResult<ConditionAdoptionOutcome> result = RequireResolved(
                await dispatcher.Dispatch(
                    new AdoptConditionRegistrationsOp(
                        new[]
                        {
                            new ConditionRegistration(active, activeBinding),
                            new ConditionRegistration(expired, expiredBinding),
                        }
                    )
                )
            );

            Assert.That(result.Value.Adopted, Is.EqualTo(2));
            Assert.That(created.Count, Is.Zero, "Restore must not trigger creation listeners.");
            Assert.That(result.Facts.OfType<ActiveEffectCreatedFact>(), Is.Empty);
            Assert.That(result.Facts.OfType<ConditionCreatedFact>(), Is.Empty);
            ActiveEffectAdoptedFact[] adopted = result
                .Facts.Cast<ActiveEffectAdoptedFact>()
                .ToArray();
            Assert.That(adopted.Select(fact => fact.Effect), Is.EqualTo(new[] { active, expired }));
            Assert.That(
                adopted.Select(fact => fact.Binding),
                Is.EqualTo(new[] { activeBinding, expiredBinding })
            );
            Assert.That(adopted[0].Effect.Status, Is.EqualTo(ActiveEffectStatus.Active));
            Assert.That(adopted[1].Effect.Status, Is.EqualTo(ActiveEffectStatus.Expired));
        }

        [Test]
        public async Task AdoptionRejectsLaterLifecycleConflictWithoutStateOrFacts()
        {
            RuleSource source = RuleSource.FromSlug("atomic-adoption");
            ConditionRegistration valid = Registration(
                "valid",
                Owner,
                ConditionRuleDefinitions.Deafened,
                source,
                ConditionMarkerState.Instance,
                1
            );
            ActiveEffectInstance conflictingEffect = new ActiveEffectInstance(
                new ActiveEffectId("effect-conflicting"),
                ConditionRuleDefinitions.Fatigued,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance,
                new EffectStateVersion(2),
                ActiveEffectStatus.Expired
            );
            ConditionRegistration conflicting = new ConditionRegistration(
                conflictingEffect,
                new ActiveRuleBinding(
                    new BindingId("binding-conflicting"),
                    conflictingEffect.DefinitionId,
                    Owner,
                    conflictingEffect.Id,
                    source,
                    2,
                    isEnabled: true
                )
            );
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);

            OpResult<ConditionAdoptionOutcome> result = await dispatcher.Dispatch(
                new AdoptConditionRegistrationsOp(new[] { valid, conflicting })
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<ConditionAdoptionOutcome>>());
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(store.Snapshot.RuleBindings, Is.Empty);
        }

        [Test]
        public async Task CleanupRejectsLaterConflictWithoutPartialCommitOrFacts()
        {
            RuleSource source = RuleSource.FromSlug("atomic-cleanup");
            ConditionRegistration valid = Registration(
                "cleanup-valid",
                Owner,
                ConditionRuleDefinitions.OffGuard,
                source,
                ConditionMarkerState.Instance,
                1
            );
            ConditionRegistration conflicting = Registration(
                "cleanup-conflict",
                Owner,
                ConditionRuleDefinitions.Deafened,
                source,
                ConditionMarkerState.Instance,
                2,
                isEnabled: false
            );
            RulesStateSeed seed = new RulesStateSeed()
                .SeedActiveEffect(valid.Effect)
                .SeedRuleBinding(valid.Binding)
                .SeedActiveEffect(conflicting.Effect)
                .SeedRuleBinding(conflicting.Binding);
            InMemoryRulesStore store = new InMemoryRulesStore(seed);
            RuleDispatcher dispatcher = CreateDispatcher(store);

            OpResult<ConditionCleanupOutcome> result = await dispatcher.Dispatch(
                new CleanupConditionsFromSourceOp(source, ConditionCleanupKind.Remove)
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<ConditionCleanupOutcome>>());
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.ActiveEffects.Contains(valid.Effect.Id), Is.True);
            Assert.That(store.Snapshot.RuleBindings.Contains(valid.Binding.Id), Is.True);
        }

        [Test]
        public async Task SourceWideCleanupSpansOwnersAndDefinitionsInStableOrder()
        {
            CreatureId otherOwner = new CreatureId("other-condition-owner");
            RuleSource source = RuleSource.FromSlug("source-wide-cleanup");
            ConditionRegistration later = Registration(
                "later",
                Owner,
                ConditionRuleDefinitions.Slowed,
                source,
                new SlowedConditionState(1),
                9
            );
            ConditionRegistration first = Registration(
                "first",
                otherOwner,
                ConditionRuleDefinitions.Fatigued,
                source,
                ConditionMarkerState.Instance,
                2
            );
            ConditionRegistration middle = Registration(
                "middle",
                Owner,
                ConditionRuleDefinitions.Deafened,
                source,
                ConditionMarkerState.Instance,
                5
            );
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            RequireResolved(
                await dispatcher.Dispatch(
                    new AdoptConditionRegistrationsOp(new[] { later, first, middle })
                )
            );

            ResolvedOpResult<ConditionCleanupOutcome> cleanup = RequireResolved(
                await dispatcher.Dispatch(
                    new CleanupConditionsFromSourceOp(source, ConditionCleanupKind.Remove)
                )
            );

            Assert.That(
                cleanup.Value.Affected,
                Is.EqualTo(new[] { first.Effect.Id, middle.Effect.Id, later.Effect.Id })
            );
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
        }

        [Test]
        public async Task SourceCleanupUsesStableOrderAndExposesLowerSlowedSource()
        {
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            ResolvedOpResult<ConditionCreationOutcome> low = RequireResolved(
                await dispatcher.Dispatch(
                    Apply("Slowed", "low-source", new SlowedConditionState(1))
                )
            );
            ResolvedOpResult<ConditionCreationOutcome> highFirst = RequireResolved(
                await dispatcher.Dispatch(
                    Apply("Slowed", "high-source", new SlowedConditionState(2))
                )
            );
            Assert.That(
                ConditionSelectors.TryGetSlowed(store.Snapshot, Owner, out var selected),
                Is.True
            );
            Assert.That(selected.State.Value, Is.EqualTo(2), "Slowed sources must not sum.");
            ResolvedOpResult<ConditionCreationOutcome> highSecond = RequireResolved(
                await dispatcher.Dispatch(
                    Apply("Slowed", "high-source", new SlowedConditionState(3))
                )
            );

            Assert.That(
                ConditionSelectors.TryGetSlowed(store.Snapshot, Owner, out selected),
                Is.True
            );
            Assert.That(selected.State.Value, Is.EqualTo(3));

            ResolvedOpResult<ConditionCleanupOutcome> cleanup = RequireResolved(
                await dispatcher.Dispatch(
                    new CleanupConditionsFromSourceOp(
                        Owner,
                        ConditionRuleDefinitions.Slowed,
                        RuleSource.FromSlug("high-source"),
                        ConditionCleanupKind.Remove
                    )
                )
            );

            Assert.That(
                cleanup.Value.Affected,
                Is.EqualTo(new[] { highFirst.Value.EffectId, highSecond.Value.EffectId })
            );
            Assert.That(
                ConditionSelectors.TryGetSlowed(store.Snapshot, Owner, out selected),
                Is.True
            );
            Assert.That(selected.EffectId, Is.EqualTo(low.Value.EffectId));
            Assert.That(selected.State.Value, Is.EqualTo(1));
        }

        [Test]
        public async Task StunnedQuickenedAndMarkerSelectorsComposeActiveSources()
        {
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            await dispatcher.Dispatch(
                Apply("Stunned", "valued-one", new ValuedStunnedConditionState(1))
            );
            await dispatcher.Dispatch(
                Apply("Stunned", "valued-three", new ValuedStunnedConditionState(3))
            );

            Assert.That(
                ConditionSelectors.TryGetStunned(store.Snapshot, Owner, out var stunned),
                Is.True
            );
            Assert.That(stunned.State, Is.TypeOf<ValuedStunnedConditionState>());
            Assert.That(((ValuedStunnedConditionState)stunned.State).Value, Is.EqualTo(3));

            await dispatcher.Dispatch(
                Apply("Stunned", "duration", DurationOnlyStunnedConditionState.Instance)
            );
            await dispatcher.Dispatch(
                Apply(
                    "Quickened",
                    "stride-source",
                    new QuickenedConditionState(new[] { new ActionDefinitionId("stride") })
                )
            );
            await dispatcher.Dispatch(
                Apply(
                    "Quickened",
                    "strike-source",
                    new QuickenedConditionState(new[] { new ActionDefinitionId("strike") })
                )
            );
            await dispatcher.Dispatch(
                Apply("Fatigued", "fatigued-source", ConditionMarkerState.Instance)
            );
            await dispatcher.Dispatch(
                Apply("Encumbered", "encumbered-source", ConditionMarkerState.Instance)
            );

            ConditionSelectors.TryGetStunned(store.Snapshot, Owner, out stunned);
            QuickenedAllowance restricted = ConditionSelectors.GetQuickenedAllowance(
                store.Snapshot,
                Owner
            );
            Assert.That(stunned.State, Is.TypeOf<DurationOnlyStunnedConditionState>());
            Assert.That(restricted.IsUnrestricted, Is.False);
            Assert.That(
                restricted.AllowedActions,
                Is.EqualTo(
                    new[] { new ActionDefinitionId("stride"), new ActionDefinitionId("strike") }
                )
            );
            Assert.That(
                ConditionSelectors.HasMarker(
                    store.Snapshot,
                    Owner,
                    ConditionRuleDefinitions.Fatigued
                ),
                Is.True
            );
            Assert.That(
                ConditionSelectors.HasMarker(
                    store.Snapshot,
                    Owner,
                    ConditionRuleDefinitions.Encumbered
                ),
                Is.True
            );

            await dispatcher.Dispatch(
                Apply("Quickened", "unrestricted-source", QuickenedConditionState.Unrestricted)
            );
            Assert.That(
                ConditionSelectors.GetQuickenedAllowance(store.Snapshot, Owner).IsUnrestricted,
                Is.True
            );
        }

        [Test]
        public async Task MultipleOffGuardSourcesResolveOnePenaltyUntilLastSourceExpires()
        {
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);
            await dispatcher.Dispatch(
                Apply("Off-Guard", "first-off-guard", ConditionMarkerState.Instance)
            );
            await dispatcher.Dispatch(
                Apply("Off-Guard", "second-off-guard", ConditionMarkerState.Instance)
            );

            ModifierCollection both = RequireResolved(
                await dispatcher.Dispatch(new DefenseWorkflowOp(Owner))
            ).Value;
            Assert.That(both.Total, Is.EqualTo(18));
            Assert.That(both.Applied.Count(modifier => modifier.Value == -2), Is.EqualTo(1));

            await dispatcher.Dispatch(
                new CleanupConditionsFromSourceOp(
                    Owner,
                    ConditionRuleDefinitions.OffGuard,
                    RuleSource.FromSlug("first-off-guard"),
                    ConditionCleanupKind.Expire
                )
            );
            Assert.That(
                RequireResolved(
                    await dispatcher.Dispatch(new DefenseWorkflowOp(Owner))
                ).Value.Total,
                Is.EqualTo(18)
            );

            await dispatcher.Dispatch(
                new CleanupConditionsFromSourceOp(
                    Owner,
                    ConditionRuleDefinitions.OffGuard,
                    RuleSource.FromSlug("second-off-guard"),
                    ConditionCleanupKind.Expire
                )
            );
            Assert.That(
                RequireResolved(
                    await dispatcher.Dispatch(new DefenseWorkflowOp(Owner))
                ).Value.Total,
                Is.EqualTo(20)
            );
        }

        [Test]
        public async Task ContextualFlankingModifierDoesNotPersistACondition()
        {
            InMemoryRulesStore store = new InMemoryRulesStore();
            RuleDispatcher dispatcher = CreateDispatcher(store);

            ModifierCollection result = RequireResolved(
                await dispatcher.Dispatch(new DefenseWorkflowOp(Owner, includeFlanking: true))
            ).Value;

            Assert.That(result.Total, Is.EqualTo(18));
            Assert.That(
                ConditionSelectors.HasMarker(
                    store.Snapshot,
                    Owner,
                    ConditionRuleDefinitions.OffGuard
                ),
                Is.False
            );
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
        }

        private static ApplyConditionOp Apply(
            string condition,
            string source,
            IEffectState state
        ) =>
            new ApplyConditionOp(
                condition,
                Owner,
                SourceCreature,
                RuleSource.FromSlug(source),
                EffectDuration.Indefinite,
                state
            );

        private static ConditionRegistration Registration(
            string identity,
            CreatureId owner,
            RuleDefinitionId definition,
            RuleSource source,
            IEffectState state,
            long order,
            bool isEnabled = true
        )
        {
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId($"effect-{identity}"),
                definition,
                SourceCreature,
                source,
                EffectDuration.Indefinite,
                state
            );
            return new ConditionRegistration(
                effect,
                new ActiveRuleBinding(
                    new BindingId($"binding-{identity}"),
                    definition,
                    owner,
                    effect.Id,
                    source,
                    order,
                    isEnabled
                )
            );
        }

        private static RuleDispatcher CreateDispatcher(InMemoryRulesStore store)
        {
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            ConditionRuleDefinitions.DefineAll(registryBuilder);
            RuleRegistry registry = registryBuilder.Build();
            return new RuleDispatcherBuilder(store)
                .RegisterHandler<DefenseWorkflowOp, ModifierCollection>(
                    new DefenseWorkflowHandler()
                )
                .UseCheckResolution()
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .Build();
        }

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private sealed class DefenseWorkflowOp : IRuleOp<ModifierCollection>
        {
            internal DefenseWorkflowOp(CreatureId defender, bool includeFlanking = false)
            {
                Defender = defender;
                IncludeFlanking = includeFlanking;
            }

            internal CreatureId Defender { get; }
            internal bool IncludeFlanking { get; }
        }

        private sealed class CountingConditionCreatedObserver : IFactObserver<ConditionCreatedFact>
        {
            internal int Count { get; private set; }

            public ValueTask OnFactCommitted(
                ConditionCreatedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                Count++;
                return default;
            }
        }

        private sealed class DefenseWorkflowHandler
            : IOpHandler<DefenseWorkflowOp, ModifierCollection>
        {
            public async ValueTask<ModifierCollection> Handle(
                OpFrame<DefenseWorkflowOp> frame,
                OpHandlerContext context
            )
            {
                List<Modifier> modifiers = new List<Modifier>
                {
                    Modifier.Untyped(
                        20,
                        RuleSource.FromSlug("base-armor-class"),
                        Statistic.ArmorClass
                    ),
                };
                if (frame.Op.IncludeFlanking)
                    modifiers.Add(
                        new Modifier(
                            -2,
                            ModifierType.Circumstance,
                            RuleSource.FromSlug("flanking"),
                            Statistic.ArmorClass
                        )
                    );
                return RequireResolved(
                    await context.Dispatch(
                        new CollectDefenseModifiersOp(
                            frame.Op.Defender,
                            modifiers,
                            CheckSource.From(frame.Id)
                        )
                    )
                ).Value;
            }
        }
    }
}
