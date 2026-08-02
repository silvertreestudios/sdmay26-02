using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ConditionLifecycleTests
    {
        private static readonly CreatureId Owner = new CreatureId("condition-owner");
        private static readonly CreatureId SourceCreature = new CreatureId("condition-source");
        private static readonly CreatureId HistoricalSourceCreature = new CreatureId(
            "historical-condition-source"
        );
        private static readonly CreatureId OrphanOwner = new CreatureId("orphan-condition-owner");
        private static readonly PlayerId SourcePlayer = new PlayerId("condition-source-player");
        private static readonly RuleSource Source = RuleSource.FromSlug("condition-test-source");

        [Test]
        public void AdoptionRejectsAnyUnregisteredOwnerAtomicallyWhileAllowingAbsentSources()
        {
            ActiveEffectInstance validEffect = EffectFrom(
                "valid-historical-effect",
                ConditionRuleDefinitions.Fatigued,
                ConditionMarkerState.Instance,
                HistoricalSourceCreature
            );
            ActiveRuleBinding validBinding = Binding(
                "valid-historical-binding",
                validEffect,
                Owner,
                4
            );
            ActiveEffectInstance orphanEffect = EffectFrom(
                "orphan-historical-effect",
                ConditionRuleDefinitions.Deafened,
                ConditionMarkerState.Instance,
                HistoricalSourceCreature
            );
            ActiveRuleBinding orphanBinding = Binding(
                "orphan-historical-binding",
                orphanEffect,
                OrphanOwner,
                5
            );
            InMemoryRulesStore store = new InMemoryRulesStore(RegisteredSourceSeed());

            ReductionResult<ActiveEffectAdoptionOutcome> result = store.Reduce(
                Context(
                    new AdoptActiveEffectRegistrationsOp(
                        new[]
                        {
                            new ActiveEffectRegistration(validEffect, validBinding),
                            new ActiveEffectRegistration(orphanEffect, orphanBinding),
                        },
                        Source
                    )
                ),
                new AdoptActiveEffectRegistrationsReducer(Registry())
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(
                result.RejectionReason,
                Is.EqualTo("An active-effect binding owner is not a registered creature.")
            );
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(result.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(result.Snapshot.RuleBindings, Is.Empty);
            Assert.That(result.Snapshot.ActiveEffectTimings, Is.Empty);
            Assert.That(result.Snapshot.Version, Is.Zero);
        }

        [Test]
        public void UpdateStagesOneGenericConditionFactAndStaleUpdateStagesNone()
        {
            ActiveEffectInstance effect = Effect(
                "slowed-effect",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(1)
            );
            ActiveRuleBinding binding = Binding("slowed-binding", effect, Owner, 0);
            InMemoryRulesStore store = Seeded(effect, binding);
            UpdateActiveEffectStateReducer reducer = new UpdateActiveEffectStateReducer();

            ReductionResult<ActiveEffectStateUpdateOutcome> updated = store.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        effect.Id,
                        EffectStateVersion.Initial,
                        new SlowedConditionState(3),
                        Source
                    )
                ),
                reducer
            );

            Assert.That(updated.IsAccepted, Is.True);
            Assert.That(updated.Value.CurrentVersion, Is.EqualTo(new EffectStateVersion(1)));
            ActiveEffectStateUpdatedFact updateFact = updated
                .Facts.OfType<ActiveEffectStateUpdatedFact>()
                .Single(fact => fact.DefinitionId == ConditionRuleDefinitions.Slowed);
            Assert.That(updateFact.EffectId, Is.EqualTo(effect.Id));
            Assert.That(
                updated.Snapshot.ActiveEffects[effect.Id].GetState<SlowedConditionState>().Value,
                Is.EqualTo(3)
            );

            ReductionResult<ActiveEffectStateUpdateOutcome> stale = store.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        effect.Id,
                        EffectStateVersion.Initial,
                        new SlowedConditionState(4),
                        Source
                    )
                ),
                reducer
            );

            Assert.That(stale.IsRejected, Is.True);
            Assert.That(stale.DidCommit, Is.False);
            Assert.That(stale.Facts, Is.Empty);
            Assert.That(stale.Snapshot.Version, Is.EqualTo(updated.Snapshot.Version));
            Assert.That(
                stale.Snapshot.ActiveEffects[effect.Id].GetState<SlowedConditionState>().Value,
                Is.EqualTo(3)
            );
        }

        [Test]
        public void UpdatePreservesTheGenericExactStateTypeValidation()
        {
            ActiveEffectInstance effect = Effect(
                "stunned-effect",
                ConditionRuleDefinitions.Stunned,
                new ValuedStunnedConditionState(1)
            );
            InMemoryRulesStore store = Seeded(effect, Binding("stunned-binding", effect, Owner, 0));

            ReductionResult<ActiveEffectStateUpdateOutcome> result = store.Reduce(
                Context(
                    UpdateActiveEffectStateOp.Create(
                        effect.Id,
                        EffectStateVersion.Initial,
                        DurationOnlyStunnedConditionState.Instance,
                        Source
                    )
                ),
                new UpdateActiveEffectStateReducer()
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(
                result.RejectionReason,
                Does.Contain(nameof(DurationOnlyStunnedConditionState))
            );
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(result.Snapshot.ActiveEffects[effect.Id], Is.SameAs(effect));
        }

        [Test]
        public void ExpireAndRemoveEachStageOneGenericConditionFactAtomically()
        {
            ActiveEffectInstance effect = Effect(
                "off-guard-effect",
                ConditionRuleDefinitions.OffGuard,
                ConditionMarkerState.Instance
            );
            ActiveRuleBinding binding = Binding("off-guard-binding", effect, Owner, 0);
            InMemoryRulesStore store = Seeded(effect, binding);

            ReductionResult<ActiveEffectExpirationOutcome> expired = store.Reduce(
                Context(
                    new ExpireActiveEffectOp(
                        effect.Id,
                        binding.Id,
                        EffectStateVersion.Initial,
                        Source
                    )
                ),
                new ExpireActiveEffectReducer()
            );

            Assert.That(expired.IsAccepted, Is.True);
            ActiveEffectExpiredFact expirationFact = expired
                .Facts.OfType<ActiveEffectExpiredFact>()
                .Single(fact => fact.DefinitionId == ConditionRuleDefinitions.OffGuard);
            Assert.That(expirationFact.EffectId, Is.EqualTo(effect.Id));
            Assert.That(
                expired.Snapshot.ActiveEffects[effect.Id].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(expired.Snapshot.RuleBindings[binding.Id].IsEnabled, Is.False);

            ReductionResult<ActiveEffectRemovalOutcome> removed = store.Reduce(
                Context(
                    new RemoveActiveEffectOp(effect.Id, binding.Id, expired.Value.Version, Source)
                ),
                new RemoveActiveEffectReducer()
            );

            Assert.That(removed.IsAccepted, Is.True);
            ActiveEffectRemovedFact removalFact = removed
                .Facts.OfType<ActiveEffectRemovedFact>()
                .Single(fact => fact.DefinitionId == ConditionRuleDefinitions.OffGuard);
            Assert.That(removalFact.EffectId, Is.EqualTo(effect.Id));
            Assert.That(removed.Snapshot.ActiveEffects.Contains(effect.Id), Is.False);
            Assert.That(removed.Snapshot.RuleBindings.Contains(binding.Id), Is.False);
        }

        [Test]
        public void MismatchedExpirationRejectsWithoutAnyPartialTransaction()
        {
            ActiveEffectInstance effect = Effect(
                "quickened-effect",
                ConditionRuleDefinitions.Quickened,
                new QuickenedConditionState(new[] { new ActionDefinitionId("stride") })
            );
            ActiveRuleBinding binding = Binding("quickened-binding", effect, Owner, 0);
            ActiveEffectInstance other = Effect(
                "other-effect",
                ConditionRuleDefinitions.Deafened,
                ConditionMarkerState.Instance
            );
            ActiveRuleBinding otherBinding = Binding("other-binding", other, Owner, 1);
            InMemoryRulesStore store = new InMemoryRulesStore(
                RegisteredSourceSeed()
                    .SeedActiveEffect(effect)
                    .SeedRuleBinding(binding)
                    .SeedActiveEffect(other)
                    .SeedRuleBinding(otherBinding)
            );

            ReductionResult<ActiveEffectExpirationOutcome> result = store.Reduce(
                Context(
                    new ExpireActiveEffectOp(
                        effect.Id,
                        otherBinding.Id,
                        EffectStateVersion.Initial,
                        Source
                    )
                ),
                new ExpireActiveEffectReducer()
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(result.Snapshot.Version, Is.Zero);
            Assert.That(
                result.Snapshot.ActiveEffects[effect.Id].Status,
                Is.EqualTo(ActiveEffectStatus.Active)
            );
            Assert.That(result.Snapshot.RuleBindings[binding.Id].IsEnabled, Is.True);
        }

        [Test]
        public void SelectorFiltersNonMatchingAndInactiveCandidatesAndUsesBindingOwner()
        {
            RulesStateSeed seed = RegisteredSourceSeed();
            Add(
                seed,
                "valid",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(2),
                Owner,
                4
            );
            Add(
                seed,
                "wrong-owner",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(9),
                SourceCreature,
                0
            );
            Add(
                seed,
                "wrong-definition",
                ConditionRuleDefinitions.Deafened,
                ConditionMarkerState.Instance,
                Owner,
                0
            );
            Add(
                seed,
                "expired",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(9),
                Owner,
                0,
                isEnabled: false,
                status: ActiveEffectStatus.Expired
            );

            bool found = ConditionSelectors.TryGetSlowed(
                new InMemoryRulesStore(seed).Snapshot,
                Owner,
                out ConditionSelection<SlowedConditionState> selected
            );

            Assert.That(found, Is.True);
            Assert.That(selected.EffectId.Value, Is.EqualTo("valid-effect"));
            Assert.That(selected.Owner, Is.EqualTo(Owner));
            Assert.That(selected.Effect.SourceCreature, Is.EqualTo(SourceCreature));
            Assert.That(selected.State.Value, Is.EqualTo(2));
            Assert.That(selected.Source, Is.EqualTo(Source));
            Assert.That(selected.Version, Is.EqualTo(EffectStateVersion.Initial));
        }

        [Test]
        public void SelectorChoosesHighestValueThenStableCreationAndBindingOrder()
        {
            RulesStateSeed seed = RegisteredSourceSeed();
            Add(
                seed,
                "low",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(2),
                Owner,
                0
            );
            Add(
                seed,
                "late",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(4),
                Owner,
                8
            );
            Add(
                seed,
                "tie-b",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(4),
                Owner,
                3,
                bindingSuffix: "b"
            );
            Add(
                seed,
                "tie-a",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(4),
                Owner,
                3,
                bindingSuffix: "a"
            );

            bool found = ConditionSelectors.TryGetSlowed(
                new InMemoryRulesStore(seed).Snapshot,
                Owner,
                out ConditionSelection<SlowedConditionState> selected
            );

            Assert.That(found, Is.True);
            Assert.That(selected.State.Value, Is.EqualTo(4));
            Assert.That(selected.Binding.CreationOrder, Is.EqualTo(3));
            Assert.That(selected.BindingId.Value, Is.EqualTo("tie-a-binding-a"));
        }

        [Test]
        public void RegistrationAddsConditionApplicationAndCleanupReducers()
        {
            RuleRegistry registry = MarkerRegistry();

            RuleDispatcher dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore())
                .UseConditionRules(registry)
                .Build();

            Assert.That(dispatcher, Is.Not.Null);
        }

        [Test]
        public async Task RuntimeAdoptionAdvancesLaterConditionIdentityPastCreationOrder()
        {
            RuleRegistry registry = MarkerRegistry();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(RegisteredSourceSeed())
            )
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .Build();
            ActiveEffectInstance adoptedEffect = EffectFrom(
                "reinforcement-effect",
                ConditionRuleDefinitions.Fatigued,
                ConditionMarkerState.Instance,
                HistoricalSourceCreature
            );
            ActiveRuleBinding adoptedBinding = Binding(
                "reinforcement-binding",
                adoptedEffect,
                Owner,
                40
            );

            Assert.That(
                await dispatcher.Dispatch(
                    new AdoptActiveEffectRegistrationsOp(
                        new[] { new ActiveEffectRegistration(adoptedEffect, adoptedBinding) },
                        Source
                    )
                ),
                Is.TypeOf<ResolvedOpResult<ActiveEffectAdoptionOutcome>>()
            );
            Assert.That(dispatcher.Snapshot.Creatures.Contains(HistoricalSourceCreature), Is.False);
            Assert.That(
                dispatcher.Snapshot.ActiveEffects[adoptedEffect.Id].SourceCreature,
                Is.EqualTo(HistoricalSourceCreature)
            );
            ResolvedOpResult<ConditionApplicationOutcome> created = RequireResolved(
                await dispatcher.Dispatch(NewMarkerCondition())
            );

            Assert.That(created.Value.EffectId.Value, Is.EqualTo("condition-effect-41"));
            Assert.That(created.Value.BindingId.Value, Is.EqualTo("condition-binding-41"));
            Assert.That(
                dispatcher.Snapshot.RuleBindings[created.Value.BindingId].CreationOrder,
                Is.EqualTo(41)
            );
        }

        [Test]
        public async Task PersistedSuffixCollisionProbesBothConditionIds()
        {
            ActiveEffectInstance collidingEffect = new ActiveEffectInstance(
                new ActiveEffectId("condition-effect-41"),
                ConditionRuleDefinitions.Fatigued,
                SourceCreature,
                Source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            );
            ActiveRuleBinding collidingBinding = new ActiveRuleBinding(
                new BindingId("persisted-binding-with-arbitrary-suffix"),
                collidingEffect.DefinitionId,
                Owner,
                collidingEffect.Id,
                Source,
                40
            );
            RuleRegistry registry = MarkerRegistry();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(
                    new RulesStateSeed()
                        .SeedCreature(new CreatureState(SourceCreature, SourcePlayer))
                        .SeedPreparedInputs(SourceCreature, PreparedCreatureInputs.Empty)
                        .SeedCreature(
                            new CreatureState(Owner, new PlayerId("condition-owner-player"))
                        )
                        .SeedPreparedInputs(Owner, PreparedCreatureInputs.Empty)
                        .SeedActiveEffect(collidingEffect)
                        .SeedRuleBinding(collidingBinding)
                )
            )
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .Build();

            ResolvedOpResult<ConditionApplicationOutcome> created = RequireResolved(
                await dispatcher.Dispatch(NewMarkerCondition())
            );

            Assert.That(created.Value.EffectId.Value, Is.EqualTo("condition-effect-42"));
            Assert.That(created.Value.BindingId.Value, Is.EqualTo("condition-binding-42"));
            Assert.That(
                dispatcher.Snapshot.RuleBindings[created.Value.BindingId].CreationOrder,
                Is.EqualTo(42)
            );
        }

        [Test]
        public async Task RuntimeAdoptionAtMaximumCreationOrderFailsConditionAllocationClosed()
        {
            RuleRegistry registry = MarkerRegistry();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(RegisteredSourceSeed()),
                new SequentialOpIdProvider(1)
            )
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .Build();
            ActiveEffectInstance adoptedEffect = EffectFrom(
                "maximum-runtime-effect",
                ConditionRuleDefinitions.Fatigued,
                ConditionMarkerState.Instance,
                HistoricalSourceCreature
            );
            ActiveRuleBinding adoptedBinding = Binding(
                "maximum-runtime-binding",
                adoptedEffect,
                Owner,
                long.MaxValue
            );
            RequireResolved(
                await dispatcher.Dispatch(
                    new AdoptActiveEffectRegistrationsOp(
                        new[] { new ActiveEffectRegistration(adoptedEffect, adoptedBinding) },
                        Source
                    )
                )
            );
            long version = dispatcher.Snapshot.Version;
            int effects = dispatcher.Snapshot.ActiveEffects.Count;

            InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(NewMarkerCondition())
            );

            Assert.That(
                failure.Message,
                Is.EqualTo("The condition identity sequence is exhausted.")
            );
            Assert.That(dispatcher.Snapshot.Version, Is.EqualTo(version));
            Assert.That(dispatcher.Snapshot.ActiveEffects.Count, Is.EqualTo(effects));
        }

        private static RuleRegistry Registry()
        {
            RuleRegistryBuilder builder = new RuleRegistryBuilder();
            ConditionRuleDefinitions.DefineAll(builder);
            return builder.Build();
        }

        private static RuleRegistry MarkerRegistry()
        {
            RuleRegistryBuilder builder = new RuleRegistryBuilder();
            builder.Define(ConditionRuleDefinitions.Fatigued);
            builder.Define(ConditionRuleDefinitions.Deafened);
            return builder.Build();
        }

        private static ActiveEffectInstance Effect(
            string id,
            RuleDefinitionId definition,
            IEffectState state,
            ActiveEffectStatus status = ActiveEffectStatus.Active
        ) => EffectFrom(id, definition, state, SourceCreature, status);

        private static ActiveEffectInstance EffectFrom(
            string id,
            RuleDefinitionId definition,
            IEffectState state,
            CreatureId sourceCreature,
            ActiveEffectStatus status = ActiveEffectStatus.Active
        ) =>
            new ActiveEffectInstance(
                new ActiveEffectId(id),
                definition,
                sourceCreature,
                Source,
                EffectDuration.Indefinite,
                state,
                EffectStateVersion.Initial,
                status
            );

        private static ActiveRuleBinding Binding(
            string id,
            ActiveEffectInstance effect,
            CreatureId owner,
            long creationOrder,
            bool isEnabled = true,
            bool mismatchSource = false
        ) =>
            new ActiveRuleBinding(
                new BindingId(id),
                effect.DefinitionId,
                owner,
                effect.Id,
                mismatchSource ? RuleSource.FromSlug("mismatched-source") : effect.Source,
                creationOrder,
                isEnabled
            );

        private static InMemoryRulesStore Seeded(
            ActiveEffectInstance effect,
            ActiveRuleBinding binding
        ) =>
            new InMemoryRulesStore(
                RegisteredSourceSeed().SeedActiveEffect(effect).SeedRuleBinding(binding)
            );

        private static RulesStateSeed RegisteredSourceSeed() =>
            new RulesStateSeed()
                .SeedCreature(new CreatureState(SourceCreature, SourcePlayer))
                .SeedPreparedInputs(SourceCreature, PreparedCreatureInputs.Empty)
                .SeedCreature(new CreatureState(Owner, new PlayerId("condition-owner-player")))
                .SeedPreparedInputs(Owner, PreparedCreatureInputs.Empty);

        private static void Add(
            RulesStateSeed seed,
            string prefix,
            RuleDefinitionId definition,
            IEffectState state,
            CreatureId owner,
            long creationOrder,
            bool isEnabled = true,
            ActiveEffectStatus status = ActiveEffectStatus.Active,
            bool mismatchSource = false,
            string bindingSuffix = ""
        )
        {
            ActiveEffectInstance effect = Effect($"{prefix}-effect", definition, state, status);
            ActiveRuleBinding binding = Binding(
                $"{prefix}-binding{(bindingSuffix.Length == 0 ? string.Empty : $"-{bindingSuffix}")}",
                effect,
                owner,
                creationOrder,
                isEnabled,
                mismatchSource
            );
            seed.SeedActiveEffect(effect).SeedRuleBinding(binding);
        }

        private static ReductionContext<TOp> Context<TOp>(TOp op) =>
            new ReductionContext<TOp>(op, new OpId(2), new OpId(1), Source);

        private static ApplyConditionOp NewMarkerCondition() =>
            new ApplyConditionOp(
                "deafened",
                Owner,
                SourceCreature,
                Source,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            );

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(
            OpResult<TResult> result
        ) =>
            result as ResolvedOpResult<TResult>
            ?? throw new AssertionException($"Expected Resolved but received {result.Status}.");
    }
}
