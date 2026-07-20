using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class RulesRuntimeTests
    {
        private static readonly CreatureId Creature = new CreatureId("creature-1");
        private static readonly RuleSource TestSource = RuleSource.FromSlug("test-source");

        private sealed class TestEffectState : IEffectState { }

        [Test]
        public void RuntimeAssemblyHasNoUnityOrMainGameDependency()
        {
            string[] references = typeof(RulesState)
                .Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.That(references, Does.Not.Contain("UnityEngine"));
            Assert.That(references, Does.Not.Contain("UnityEngine.CoreModule"));
            Assert.That(references, Does.Not.Contain("MainGameAssembly"));
        }

        [Test]
        public void OpenValuesUseCanonicalSlugsAndUnityFreeGridValues()
        {
            Assert.That(Pf2eSlug.FromName("  Dragon's Rage! "), Is.EqualTo("dragons-rage"));
            Assert.That(
                Trait.FromName("Attack of Opportunity").Slug,
                Is.EqualTo("attack-of-opportunity")
            );
            Assert.That(RuleSource.FromName("Bless Aura").Slug, Is.EqualTo("bless-aura"));
            Assert.That(new GridPosition(2, 1, -3), Is.EqualTo(new GridPosition(2, 1, -3)));
            Assert.That(new GridDistance(15).Feet, Is.EqualTo(15));
        }

        [Test]
        public void SeedProvidesEveryNamedSliceWithoutSharingCallerCollections()
        {
            List<Trait> callerTraits = new List<Trait> { Trait.FromSlug("humanoid") };

            PlayerId player = new PlayerId("player-1");
            ConditionState condition = new ConditionState(
                new ConditionId("condition-1"),
                new RuleDefinitionId("frightened"),
                Creature,
                1,
                TestSource
            );
            EquipmentState item = new EquipmentState(
                new ItemId("item-1"),
                new ItemDefinitionId("longsword"),
                Creature,
                true
            );
            ActiveEffectInstance effect = new ActiveEffectInstance(
                new ActiveEffectId("effect-1"),
                new RuleDefinitionId("bless-aura"),
                Creature,
                TestSource,
                EffectDuration.OneMinute,
                new TestEffectState()
            );
            ActiveRuleBinding binding = new ActiveRuleBinding(
                new BindingId("binding-1"),
                effect.DefinitionId,
                Creature,
                effect.Id,
                TestSource,
                1
            );
            SpellSlotState spellSlot = new SpellSlotState(
                new SpellSlotPoolId("rank-one"),
                Creature,
                1,
                1
            );
            AmmunitionState ammunition = new AmmunitionState(
                new ItemId("ammunition-1"),
                Creature,
                10
            );

            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Creature, player, callerTraits))
                .SeedHealth(Creature, new HealthState(20, 20))
                .SeedPosition(Creature, new GridPosition(1, 0, 2))
                .SeedActionEconomy(Creature, new ActionEconomyState(3, true))
                .SeedSpellSlot(spellSlot)
                .SeedFocusPoints(Creature, new FocusPointState(1, 3))
                .SeedAmmunition(ammunition)
                .SeedMultipleAttackPenalty(Creature, new MultipleAttackPenaltyState(0))
                .SeedCondition(condition)
                .SeedEquipment(item)
                .SeedActiveEffect(effect)
                .SeedRuleBinding(binding)
                .SeedFrequency(binding.Id, new FrequencyState(2, 0));

            InMemoryRulesStore store = new InMemoryRulesStore(seed);
            callerTraits.Add(Trait.FromSlug("added-later"));
            seed.SeedHealth(Creature, new HealthState(1, 20));

            RulesSnapshot snapshot = store.Snapshot;
            Assert.That(snapshot.Creatures[Creature].Traits, Has.Count.EqualTo(1));
            Assert.That(snapshot.Health[Creature].Current, Is.EqualTo(20));
            Assert.That(snapshot.Positions[Creature], Is.EqualTo(new GridPosition(1, 0, 2)));
            Assert.That(snapshot.ActionEconomy[Creature].ActionsRemaining, Is.EqualTo(3));
            Assert.That(snapshot.SpellSlots[spellSlot.Id], Is.EqualTo(spellSlot));
            Assert.That(snapshot.FocusPoints[Creature], Is.EqualTo(new FocusPointState(1, 3)));
            Assert.That(snapshot.Ammunition[ammunition.Item], Is.EqualTo(ammunition));
            Assert.That(snapshot.MultipleAttackPenalty[Creature].AttackCount, Is.Zero);
            Assert.That(snapshot.Conditions[condition.Id], Is.EqualTo(condition));
            Assert.That(snapshot.Equipment[item.Id], Is.EqualTo(item));
            Assert.That(snapshot.ActiveEffects[effect.Id], Is.EqualTo(effect));
            Assert.That(snapshot.RuleBindings[binding.Id], Is.EqualTo(binding));
            Assert.That(snapshot.Frequencies[binding.Id], Is.EqualTo(new FrequencyState(2, 0)));
        }

        [Test]
        public void SuccessfulReductionAtomicallyCommitsStateAndStampedFact()
        {
            InMemoryRulesStore store = CreateStore(20);
            ReductionContext<AdjustHealthOp> context = Context(new AdjustHealthOp(Creature, -5));

            ReductionResult<int> result = store.Reduce(context, new AdjustHealthReducer());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.DidCommit, Is.True);
            Assert.That(result.Value, Is.EqualTo(15));
            Assert.That(result.Snapshot.Health[Creature].Current, Is.EqualTo(15));
            Assert.That(result.Facts, Has.Count.EqualTo(1));

            HealthAdjustedFact fact = (HealthAdjustedFact)result.Facts[0];
            Assert.That(fact.IsStamped, Is.True);
            Assert.That(fact.Id, Is.EqualTo(new FactId(1)));
            Assert.That(fact.SourceOpId, Is.EqualTo(context.SourceOpId));
            Assert.That(fact.RootOpId, Is.EqualTo(context.RootOpId));
            Assert.That(fact.Source, Is.EqualTo(TestSource));
            Assert.That(fact.Previous, Is.EqualTo(20));
            Assert.That(fact.Current, Is.EqualTo(15));
        }

        [Test]
        public void RejectedReductionRollsBackEverySliceAndNeverStampsFacts()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;
            RejectAfterWritesReducer reducer = new RejectAfterWritesReducer();

            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, -20)),
                reducer
            );

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.RejectionReason, Is.EqualTo("rejected for test"));
            Assert.That(result.Facts, Is.Empty);
            Assert.That(reducer.StagedFact.IsStamped, Is.False);
            Assert.That(store.Snapshot, Is.SameAs(before));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(20));
            Assert.That(store.Snapshot.Positions[Creature], Is.EqualTo(new GridPosition(0, 0, 0)));
        }

        [Test]
        public void AcceptedNoFactNoOpPreservesSnapshotAndVersion()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;

            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, 0)),
                new NoOpHealthReducer()
            );

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(result.Snapshot, Is.SameAs(before));
            Assert.That(result.Snapshot.Version, Is.Zero);
        }

        [Test]
        public void WriteThenRevertWithoutFactPreservesSnapshotAndVersion()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;

            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, -5)),
                new RevertDraftReducer()
            );

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(result.Snapshot, Is.SameAs(before));
            Assert.That(result.Snapshot.Version, Is.Zero);
        }

        [Test]
        public void StateChangeWithoutFactFailsInvariantAndRollsBack()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                store.Reduce(
                    Context(new AdjustHealthOp(Creature, -1)),
                    new UnreportedHealthReducer()
                )
            );

            Assert.That(exception.Message, Does.Contain("requires at least one domain Fact"));
            Assert.That(store.Snapshot, Is.SameAs(before));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(20));

            ReductionResult<int> recovered = store.Reduce(
                Context(new AdjustHealthOp(Creature, -1)),
                new AdjustHealthReducer()
            );

            Assert.That(recovered.Snapshot.Version, Is.EqualTo(1));
            Assert.That(recovered.Snapshot.Health[Creature].Current, Is.EqualTo(19));
            Assert.That(recovered.Facts[0].Id, Is.EqualTo(new FactId(1)));
        }

        [Test]
        public void NestedSameStoreReductionIsRejectedBeforeReducerRunsAndOuterRollsBack()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;
            CountingAdjustHealthReducer nested = new CountingAdjustHealthReducer();
            ReentrantReducer outer = new ReentrantReducer(store, nested);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                store.Reduce(Context(new AdjustHealthOp(Creature, -5)), outer)
            );

            Assert.That(exception.Message, Does.Contain("nested reduction"));
            Assert.That(nested.InvocationCount, Is.Zero);
            Assert.That(outer.ObservedSnapshotVersion, Is.Zero);
            Assert.That(outer.StagedFact.IsStamped, Is.False);
            Assert.That(store.Snapshot, Is.SameAs(before));
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(20));

            ReductionResult<int> recovered = store.Reduce(
                Context(new AdjustHealthOp(Creature, -1)),
                new AdjustHealthReducer()
            );

            Assert.That(recovered.Snapshot.Version, Is.EqualTo(1));
            Assert.That(recovered.Snapshot.Health[Creature].Current, Is.EqualTo(19));
            Assert.That(recovered.Facts[0].Id, Is.EqualTo(new FactId(1)));
        }

        [Test]
        public void DuplicateFactInstanceFailsBeforeStampingAndStoreRemainsUsable()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;
            DuplicateFactReducer reducer = new DuplicateFactReducer();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                store.Reduce(Context(new AdjustHealthOp(Creature, -5)), reducer)
            );

            Assert.That(exception.Message, Does.Contain("same Rule Fact instance"));
            Assert.That(reducer.StagedFact.IsStamped, Is.False);
            Assert.That(store.Snapshot, Is.SameAs(before));
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(20));

            ReductionResult<int> recovered = store.Reduce(
                Context(new AdjustHealthOp(Creature, -1)),
                new AdjustHealthReducer()
            );

            Assert.That(recovered.Snapshot.Version, Is.EqualTo(1));
            Assert.That(recovered.Snapshot.Health[Creature].Current, Is.EqualTo(19));
            Assert.That(recovered.Facts[0].Id, Is.EqualTo(new FactId(1)));
        }

        [Test]
        public void DistinctValueEqualFactInstancesCanBeStagedTogether()
        {
            InMemoryRulesStore store = CreateStore(20);
            DistinctValueEqualFactsReducer reducer = new DistinctValueEqualFactsReducer();

            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, -1)),
                reducer
            );

            Assert.That(result.DidCommit, Is.True);
            Assert.That(result.Facts, Has.Count.EqualTo(2));
            Assert.That(result.Facts[0], Is.Not.SameAs(result.Facts[1]));
            Assert.That(result.Facts[0].Id, Is.EqualTo(new FactId(1)));
            Assert.That(result.Facts[1].Id, Is.EqualTo(new FactId(2)));
        }

        [Test]
        public void ReductionProvenanceCanOnlyBeConstructedInsideRuntimeAssembly()
        {
            ConstructorInfo[] publicConstructors =
                typeof(ReductionContext<AdjustHealthOp>).GetConstructors();
            ConstructorInfo[] internalConstructors =
                typeof(ReductionContext<AdjustHealthOp>).GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

            Assert.That(publicConstructors, Is.Empty);
            Assert.That(internalConstructors, Has.Length.EqualTo(1));
            Assert.That(
                typeof(IRulesStore).Assembly.GetType("Game.Rules.Runtime.IRuleOp"),
                Is.Not.Null
            );
            Assert.That(
                typeof(FactSink).GetMethod(nameof(FactSink.Stage)).GetParameters()[0].ParameterType,
                Is.EqualTo(typeof(RuleFact))
            );
        }

        [Test]
        public void StateValueConstructorsRejectDefaultRequiredIdsAndSources()
        {
            PlayerId player = new PlayerId("player-1");
            RuleDefinitionId definition = new RuleDefinitionId("definition-1");
            ActiveEffectId effect = new ActiveEffectId("effect-1");

            Assert.Throws<ArgumentException>(() => new CreatureState(default, player));
            Assert.Throws<ArgumentException>(() => new CreatureState(Creature, default));
            Assert.Throws<ArgumentException>(() =>
                new CreatureState(Creature, player, new[] { default(Trait) })
            );
            Assert.Throws<ArgumentException>(() =>
                new ConditionState(default, definition, Creature, 1, TestSource)
            );
            Assert.Throws<ArgumentException>(() =>
                new ConditionState(new ConditionId("condition-1"), default, Creature, 1, TestSource)
            );
            Assert.Throws<ArgumentException>(() =>
                new ConditionState(
                    new ConditionId("condition-1"),
                    definition,
                    default,
                    1,
                    TestSource
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ConditionState(new ConditionId("condition-1"), definition, Creature, 1, default)
            );
            Assert.Throws<ArgumentException>(() =>
                new EquipmentState(
                    default,
                    new ItemDefinitionId("item-definition-1"),
                    Creature,
                    true
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new EquipmentState(new ItemId("item-1"), default, Creature, true)
            );
            Assert.Throws<ArgumentException>(() =>
                new EquipmentState(
                    new ItemId("item-1"),
                    new ItemDefinitionId("item-definition-1"),
                    default,
                    true
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveEffectInstance(
                    default,
                    definition,
                    Creature,
                    TestSource,
                    EffectDuration.Indefinite,
                    new TestEffectState()
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveEffectInstance(
                    effect,
                    default,
                    Creature,
                    TestSource,
                    EffectDuration.Indefinite,
                    new TestEffectState()
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveEffectInstance(
                    effect,
                    definition,
                    default,
                    TestSource,
                    EffectDuration.Indefinite,
                    new TestEffectState()
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveEffectInstance(
                    effect,
                    definition,
                    Creature,
                    default,
                    EffectDuration.Indefinite,
                    new TestEffectState()
                )
            );
            Assert.Throws<ArgumentNullException>(() =>
                new ActiveEffectInstance(
                    effect,
                    definition,
                    Creature,
                    TestSource,
                    EffectDuration.Indefinite,
                    null
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveRuleBinding(default, definition, Creature, effect, TestSource, 0)
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveRuleBinding(
                    new BindingId("binding-1"),
                    default,
                    Creature,
                    effect,
                    TestSource,
                    0
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveRuleBinding(
                    new BindingId("binding-1"),
                    definition,
                    default,
                    effect,
                    TestSource,
                    0
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveRuleBinding(
                    new BindingId("binding-1"),
                    definition,
                    Creature,
                    default(ActiveEffectId),
                    TestSource,
                    0
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new ActiveRuleBinding(
                    new BindingId("binding-1"),
                    definition,
                    Creature,
                    effect,
                    default,
                    0
                )
            );
            Assert.Throws<ArgumentException>(() => new SpellSlotState(default, Creature, 1, 1));
            Assert.Throws<ArgumentException>(() =>
                new SpellSlotState(new SpellSlotPoolId("slot"), default, 1, 1)
            );
            Assert.Throws<ArgumentException>(() => new AmmunitionState(default, Creature, 1));
            Assert.Throws<ArgumentException>(() =>
                new AmmunitionState(new ItemId("ammunition"), default, 1)
            );
        }

        [Test]
        public void SeedAndDraftBoundariesRejectDefaultKeys()
        {
            RulesStateSeed seed = new RulesStateSeed();

            Assert.Throws<ArgumentException>(() => seed.SeedHealth(default, new HealthState(1, 1)));
            Assert.Throws<ArgumentException>(() =>
                seed.SeedPosition(default, new GridPosition(0, 0, 0))
            );
            Assert.Throws<ArgumentException>(() =>
                seed.SeedActionEconomy(default, new ActionEconomyState(3, true))
            );
            Assert.Throws<ArgumentException>(() => seed.SeedSpellSlot(default));
            Assert.Throws<ArgumentException>(() =>
                seed.SeedFocusPoints(default, new FocusPointState(1, 1))
            );
            Assert.Throws<ArgumentException>(() => seed.SeedAmmunition(default));
            Assert.Throws<ArgumentException>(() =>
                seed.SeedMultipleAttackPenalty(default, new MultipleAttackPenaltyState(0))
            );
            Assert.Throws<ArgumentException>(() =>
                seed.SeedFrequency(default, new FrequencyState(0, 0))
            );

            InMemoryRulesStore store = CreateStore(20);
            Assert.Throws<ArgumentException>(() =>
                store.Reduce(Context(new AdjustHealthOp(Creature, 0)), new EmptyIdDraftReducer())
            );
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(20));
        }

        [Test]
        public void SnapshotsAndFactOutputsCannotBeMutatedByAdapters()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;
            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, -2)),
                new AdjustHealthReducer()
            );

            Assert.That(before.Health[Creature].Current, Is.EqualTo(20));
            Assert.That(result.Snapshot.Health[Creature].Current, Is.EqualTo(18));
            Assert.That(before.Health, Is.Not.InstanceOf<IDictionary<CreatureId, HealthState>>());

            IList<RuleFact> facts = (IList<RuleFact>)result.Facts;
            Assert.Throws<NotSupportedException>(() =>
                facts[0] = new HealthAdjustedFact(Creature, 0, 0)
            );
        }

        [Test]
        public void ReducerCannotMutateCommittedStateThroughRetainedDraft()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesStateDraft retainedDraft = null;
            CaptureDraftReducer reducer = new CaptureDraftReducer(draft => retainedDraft = draft);

            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, -1)),
                reducer
            );
            retainedDraft.Health.Set(Creature, new HealthState(1, 20));

            Assert.That(result.Snapshot.Health[Creature].Current, Is.EqualTo(19));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(19));
        }

        [Test]
        public void EquivalentReductionsAreDeterministic()
        {
            InMemoryRulesStore left = CreateStore(20);
            InMemoryRulesStore right = CreateStore(20);
            ReductionContext<AdjustHealthOp> leftContext = Context(
                new AdjustHealthOp(Creature, -4)
            );
            ReductionContext<AdjustHealthOp> rightContext = Context(
                new AdjustHealthOp(Creature, -4)
            );

            ReductionResult<int> leftResult = left.Reduce(leftContext, new AdjustHealthReducer());
            ReductionResult<int> rightResult = right.Reduce(
                rightContext,
                new AdjustHealthReducer()
            );

            Assert.That(leftResult.Value, Is.EqualTo(rightResult.Value));
            Assert.That(leftResult.Snapshot.Version, Is.EqualTo(rightResult.Snapshot.Version));
            Assert.That(
                leftResult.Snapshot.Health[Creature],
                Is.EqualTo(rightResult.Snapshot.Health[Creature])
            );
            Assert.That(leftResult.Facts[0].Id, Is.EqualTo(rightResult.Facts[0].Id));
            Assert.That(
                leftResult.Facts[0].SourceOpId,
                Is.EqualTo(rightResult.Facts[0].SourceOpId)
            );
            Assert.That(
                ((HealthAdjustedFact)leftResult.Facts[0]).Current,
                Is.EqualTo(((HealthAdjustedFact)rightResult.Facts[0]).Current)
            );
        }

        [Test]
        public void RejectedFactsDoNotConsumeStoreStampedIdentity()
        {
            InMemoryRulesStore store = CreateStore(20);
            store.Reduce(
                Context(new AdjustHealthOp(Creature, -20)),
                new RejectAfterWritesReducer()
            );

            ReductionResult<int> committed = store.Reduce(
                Context(new AdjustHealthOp(Creature, -1)),
                new AdjustHealthReducer()
            );

            Assert.That(committed.Facts[0].Id, Is.EqualTo(new FactId(1)));
        }

        private static InMemoryRulesStore CreateStore(int hitPoints)
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedHealth(Creature, new HealthState(hitPoints, hitPoints))
                .SeedPosition(Creature, new GridPosition(0, 0, 0));
            return new InMemoryRulesStore(seed);
        }

        private static ReductionContext<AdjustHealthOp> Context(AdjustHealthOp op)
        {
            return new ReductionContext<AdjustHealthOp>(op, new OpId(2), new OpId(1), TestSource);
        }

        private sealed class AdjustHealthOp : IRuleOp<int>
        {
            public CreatureId Creature { get; }
            public int Delta { get; }

            public AdjustHealthOp(CreatureId creature, int delta)
            {
                Creature = creature;
                Delta = delta;
            }
        }

        private sealed class HealthAdjustedFact : RuleFact
        {
            public CreatureId Creature { get; }
            public int Previous { get; }
            public int Current { get; }

            public HealthAdjustedFact(CreatureId creature, int previous, int current)
            {
                Creature = creature;
                Previous = previous;
                Current = current;
            }
        }

        private sealed class ValueEqualFact : RuleFact
        {
            public int Value { get; }

            public ValueEqualFact(int value)
            {
                Value = value;
            }

            public override bool Equals(object obj) =>
                obj is ValueEqualFact other && Value == other.Value;

            public override int GetHashCode() => Value;
        }

        private sealed class AdjustHealthReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                HealthState previous = state.Health.TryGet(
                    context.Op.Creature,
                    out HealthState health
                )
                    ? health
                    : throw new InvalidOperationException("Missing health seed.");
                int current = previous.Current + context.Op.Delta;
                state.Health.Set(
                    context.Op.Creature,
                    new HealthState(current, previous.Maximum, previous.Temporary)
                );
                facts.Stage(new HealthAdjustedFact(context.Op.Creature, previous.Current, current));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class NoOpHealthReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                state.Health.Set(context.Op.Creature, health);
                return ReductionResult<int>.Accept(health.Current);
            }
        }

        private sealed class CountingAdjustHealthReducer : IOpReducer<AdjustHealthOp, int>
        {
            public int InvocationCount { get; private set; }

            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                InvocationCount++;
                return new AdjustHealthReducer().Reduce(context, state, facts);
            }
        }

        private sealed class ReentrantReducer : IOpReducer<AdjustHealthOp, int>
        {
            private readonly InMemoryRulesStore store;
            private readonly CountingAdjustHealthReducer nested;

            public HealthAdjustedFact StagedFact { get; private set; }
            public long ObservedSnapshotVersion { get; private set; }

            public ReentrantReducer(InMemoryRulesStore store, CountingAdjustHealthReducer nested)
            {
                this.store = store;
                this.nested = nested;
            }

            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                int current = health.Current + context.Op.Delta;
                state.Health.Set(context.Op.Creature, new HealthState(current, health.Maximum));
                StagedFact = new HealthAdjustedFact(context.Op.Creature, health.Current, current);
                facts.Stage(StagedFact);
                ObservedSnapshotVersion = store.Snapshot.Version;

                store.Reduce(Context(new AdjustHealthOp(context.Op.Creature, -1)), nested);

                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class DuplicateFactReducer : IOpReducer<AdjustHealthOp, int>
        {
            public HealthAdjustedFact StagedFact { get; private set; }

            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                int current = health.Current + context.Op.Delta;
                state.Health.Set(context.Op.Creature, new HealthState(current, health.Maximum));
                StagedFact = new HealthAdjustedFact(context.Op.Creature, health.Current, current);
                facts.Stage(StagedFact);
                facts.Stage(StagedFact);
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class DistinctValueEqualFactsReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                int current = health.Current + context.Op.Delta;
                state.Health.Set(context.Op.Creature, new HealthState(current, health.Maximum));
                facts.Stage(new ValueEqualFact(current));
                facts.Stage(new ValueEqualFact(current));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class RejectAfterWritesReducer : IOpReducer<AdjustHealthOp, int>
        {
            public HealthAdjustedFact StagedFact { get; private set; }

            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                state.Health.Set(context.Op.Creature, new HealthState(0, health.Maximum));
                state.Positions.Set(context.Op.Creature, new GridPosition(9, 0, 9));
                StagedFact = new HealthAdjustedFact(context.Op.Creature, health.Current, 0);
                facts.Stage(StagedFact);
                return ReductionResult<int>.Reject("rejected for test");
            }
        }

        private sealed class RevertDraftReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                state.Positions.TryGet(context.Op.Creature, out GridPosition position);
                state.Health.Set(
                    context.Op.Creature,
                    new HealthState(health.Current + context.Op.Delta, health.Maximum)
                );
                state.Health.Set(context.Op.Creature, health);
                state.Positions.Remove(context.Op.Creature);
                state.Positions.Set(context.Op.Creature, position);
                return ReductionResult<int>.Accept(health.Current);
            }
        }

        private sealed class UnreportedHealthReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                int current = health.Current + context.Op.Delta;
                state.Health.Set(context.Op.Creature, new HealthState(current, health.Maximum));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class EmptyIdDraftReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.Set(default, new HealthState(1, 1));
                return ReductionResult<int>.Accept(1);
            }
        }

        private sealed class CaptureDraftReducer : IOpReducer<AdjustHealthOp, int>
        {
            private readonly Action<RulesStateDraft> capture;

            public CaptureDraftReducer(Action<RulesStateDraft> capture)
            {
                this.capture = capture;
            }

            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                int current = health.Current + context.Op.Delta;
                state.Health.Set(context.Op.Creature, new HealthState(current, health.Maximum));
                facts.Stage(new HealthAdjustedFact(context.Op.Creature, health.Current, current));
                capture(state);
                return ReductionResult<int>.Accept(current);
            }
        }
    }
}
