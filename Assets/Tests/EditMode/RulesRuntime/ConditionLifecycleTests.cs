using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class ConditionLifecycleTests
    {
        private static readonly CreatureId Owner = new CreatureId("condition-owner");
        private static readonly CreatureId SourceCreature = new CreatureId("condition-source");
        private static readonly RuleSource Source = RuleSource.FromSlug("condition-test-source");

        [Test]
        public void CreateAtomicallyStagesGenericAndConditionFacts()
        {
            ActiveEffectInstance effect = Effect(
                "slowed-effect",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(1)
            );
            ActiveRuleBinding binding = Binding("slowed-binding", effect, Owner, 4);
            InMemoryRulesStore store = new InMemoryRulesStore();

            ReductionResult<ConditionCreationOutcome> result = store.Reduce(
                Context(new CreateConditionOp(effect, binding)),
                new CreateConditionReducer(Registry())
            );

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.DidCommit, Is.True);
            Assert.That(result.Value.EffectId, Is.EqualTo(effect.Id));
            Assert.That(result.Snapshot.ActiveEffects[effect.Id], Is.EqualTo(effect));
            Assert.That(result.Snapshot.RuleBindings[binding.Id], Is.EqualTo(binding));
            Assert.That(
                result.Facts.Select(fact => fact.GetType()),
                Is.EqualTo(new[] { typeof(ActiveEffectCreatedFact), typeof(ConditionCreatedFact) })
            );
            ConditionCreatedFact conditionFact = (ConditionCreatedFact)result.Facts[1];
            Assert.That(conditionFact.Owner, Is.EqualTo(Owner));
            Assert.That(conditionFact.ConditionSource, Is.EqualTo(Source));
            Assert.That(conditionFact.State, Is.SameAs(effect.State));
            Assert.That(result.Facts.All(fact => fact.RootOpId == new OpId(1)), Is.True);
        }

        [Test]
        public void CreateRejectsWrongDefinitionStateWithoutPartialCommitOrFacts()
        {
            ActiveEffectInstance invalid = Effect(
                "invalid-effect",
                ConditionRuleDefinitions.OffGuard,
                new SlowedConditionState(1)
            );
            ActiveRuleBinding binding = Binding("invalid-binding", invalid, Owner, 0);
            InMemoryRulesStore store = new InMemoryRulesStore();

            ReductionResult<ConditionCreationOutcome> result = store.Reduce(
                Context(new CreateConditionOp(invalid, binding)),
                new CreateConditionReducer(Registry())
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(result.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(result.Snapshot.RuleBindings, Is.Empty);
            Assert.That(result.Snapshot.Version, Is.Zero);
        }

        [Test]
        public void UpdateAtomicallyStagesBothFactsAndStaleUpdateStagesNeither()
        {
            ActiveEffectInstance effect = Effect(
                "slowed-effect",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(1)
            );
            ActiveRuleBinding binding = Binding("slowed-binding", effect, Owner, 0);
            InMemoryRulesStore store = Seeded(effect, binding);
            UpdateConditionStateReducer reducer = new UpdateConditionStateReducer();

            ReductionResult<ConditionStateUpdateOutcome> updated = store.Reduce(
                Context(
                    UpdateConditionStateOp.Create(
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
            Assert.That(
                updated.Facts.Select(fact => fact.GetType()),
                Is.EqualTo(
                    new[]
                    {
                        typeof(ActiveEffectStateUpdatedFact),
                        typeof(ConditionStateUpdatedFact),
                    }
                )
            );
            Assert.That(
                updated.Snapshot.ActiveEffects[effect.Id].GetState<SlowedConditionState>().Value,
                Is.EqualTo(3)
            );

            ReductionResult<ConditionStateUpdateOutcome> stale = store.Reduce(
                Context(
                    UpdateConditionStateOp.Create(
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

            ReductionResult<ConditionStateUpdateOutcome> result = store.Reduce(
                Context(
                    UpdateConditionStateOp.Create(
                        effect.Id,
                        EffectStateVersion.Initial,
                        DurationOnlyStunnedConditionState.Instance,
                        Source
                    )
                ),
                new UpdateConditionStateReducer()
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
        public void ExpireAndRemoveEachStageGenericAndConditionFactsAtomically()
        {
            ActiveEffectInstance effect = Effect(
                "off-guard-effect",
                ConditionRuleDefinitions.OffGuard,
                ConditionMarkerState.Instance
            );
            ActiveRuleBinding binding = Binding("off-guard-binding", effect, Owner, 0);
            InMemoryRulesStore store = Seeded(effect, binding);

            ReductionResult<ConditionExpirationOutcome> expired = store.Reduce(
                Context(
                    new ExpireConditionOp(effect.Id, binding.Id, EffectStateVersion.Initial, Source)
                ),
                new ExpireConditionReducer()
            );

            Assert.That(expired.IsAccepted, Is.True);
            Assert.That(
                expired.Facts.Select(fact => fact.GetType()),
                Is.EqualTo(new[] { typeof(ActiveEffectExpiredFact), typeof(ConditionExpiredFact) })
            );
            Assert.That(
                expired.Snapshot.ActiveEffects[effect.Id].Status,
                Is.EqualTo(ActiveEffectStatus.Expired)
            );
            Assert.That(expired.Snapshot.RuleBindings[binding.Id].IsEnabled, Is.False);

            ReductionResult<ConditionRemovalOutcome> removed = store.Reduce(
                Context(
                    new RemoveConditionOp(effect.Id, binding.Id, expired.Value.Version, Source)
                ),
                new RemoveConditionReducer()
            );

            Assert.That(removed.IsAccepted, Is.True);
            Assert.That(
                removed.Facts.Select(fact => fact.GetType()),
                Is.EqualTo(new[] { typeof(ActiveEffectRemovedFact), typeof(ConditionRemovedFact) })
            );
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
                new RulesStateSeed()
                    .SeedActiveEffect(effect)
                    .SeedRuleBinding(binding)
                    .SeedActiveEffect(other)
                    .SeedRuleBinding(otherBinding)
            );

            ReductionResult<ConditionExpirationOutcome> result = store.Reduce(
                Context(
                    new ExpireConditionOp(
                        effect.Id,
                        otherBinding.Id,
                        EffectStateVersion.Initial,
                        Source
                    )
                ),
                new ExpireConditionReducer()
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
        public void SelectorFiltersInvalidCandidatesAndUsesBindingOwner()
        {
            RulesStateSeed seed = new RulesStateSeed();
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
                "wrong-state",
                ConditionRuleDefinitions.Slowed,
                ConditionMarkerState.Instance,
                Owner,
                0
            );
            Add(
                seed,
                "disabled",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(9),
                Owner,
                0,
                isEnabled: false
            );
            Add(
                seed,
                "expired",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(9),
                Owner,
                0,
                status: ActiveEffectStatus.Expired
            );
            Add(
                seed,
                "wrong-source",
                ConditionRuleDefinitions.Slowed,
                new SlowedConditionState(9),
                Owner,
                0,
                mismatchSource: true
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
            RulesStateSeed seed = new RulesStateSeed();
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
        public void RegistrationAddsAllConditionWrapperReducers()
        {
            RuleRegistry registry = Registry();

            RuleDispatcher dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore())
                .UseConditionRules(registry)
                .Build();

            Assert.That(dispatcher, Is.Not.Null);
        }

        private static RuleRegistry Registry()
        {
            RuleRegistryBuilder builder = new RuleRegistryBuilder();
            ConditionRuleDefinitions.DefineAll(builder);
            return builder.Build();
        }

        private static ActiveEffectInstance Effect(
            string id,
            RuleDefinitionId definition,
            IEffectState state,
            ActiveEffectStatus status = ActiveEffectStatus.Active
        ) =>
            new ActiveEffectInstance(
                new ActiveEffectId(id),
                definition,
                SourceCreature,
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
                new RulesStateSeed().SeedActiveEffect(effect).SeedRuleBinding(binding)
            );

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
    }
}
