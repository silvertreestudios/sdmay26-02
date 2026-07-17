using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class RulesRuntimeTests
    {
        private static readonly CreatureId Creature = new CreatureId("creature-1");
        private static readonly RuleSource TestSource = RuleSource.FromSlug("test-source");

        [Test]
        public void RuntimeAssemblyHasNoUnityOrMainGameDependency()
        {
            string[] references = typeof(RulesState).Assembly
                .GetReferencedAssemblies()
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
            Assert.That(Trait.FromName("Attack of Opportunity").Slug, Is.EqualTo("attack-of-opportunity"));
            Assert.That(RuleSource.FromName("Bless Aura").Slug, Is.EqualTo("bless-aura"));
            Assert.That(new GridPosition(2, 1, -3), Is.EqualTo(new GridPosition(2, 1, -3)));
            Assert.That(new GridDistance(15).Feet, Is.EqualTo(15));
        }

        [Test]
        public void SeedProvidesEveryNamedSliceWithoutSharingCallerCollections()
        {
            List<Trait> callerTraits = new List<Trait> { Trait.FromSlug("humanoid") };
            Dictionary<RuleValueKey, RuleValue> callerValues = new Dictionary<RuleValueKey, RuleValue>
            {
                [RuleValueKey.FromName("radius feet")] = RuleValue.FromInt(15)
            };

            PlayerId player = new PlayerId("player-1");
            ConditionState condition = new ConditionState(
                new ConditionId("condition-1"),
                new RuleDefinitionId("frightened"),
                Creature,
                1,
                TestSource);
            EquipmentState item = new EquipmentState(
                new ItemId("item-1"),
                new ItemDefinitionId("longsword"),
                Creature,
                true);
            ActiveEffectState effect = new ActiveEffectState(
                new ActiveEffectId("effect-1"),
                new RuleDefinitionId("bless-aura"),
                Creature,
                TestSource,
                0,
                new RuleValueMap(callerValues));
            RuleBindingState binding = new RuleBindingState(
                new BindingId("binding-1"),
                effect.DefinitionId,
                Creature,
                effect.Id,
                TestSource,
                1);

            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Creature, player, callerTraits))
                .SeedHealth(Creature, new HealthState(20, 20))
                .SeedPosition(Creature, new GridPosition(1, 0, 2))
                .SeedActionEconomy(Creature, new ActionEconomyState(3, true))
                .SeedMultipleAttackPenalty(Creature, new MultipleAttackPenaltyState(0))
                .SeedCondition(condition)
                .SeedEquipment(item)
                .SeedActiveEffect(effect)
                .SeedRuleBinding(binding)
                .SeedFrequency(binding.Id, new FrequencyState(2, 0));

            InMemoryRulesStore store = new InMemoryRulesStore(seed);
            callerTraits.Add(Trait.FromSlug("added-later"));
            callerValues[RuleValueKey.FromName("radius feet")] = RuleValue.FromInt(99);
            seed.SeedHealth(Creature, new HealthState(1, 20));

            RulesSnapshot snapshot = store.Snapshot;
            Assert.That(snapshot.Creatures[Creature].Traits, Has.Count.EqualTo(1));
            Assert.That(snapshot.Health[Creature].Current, Is.EqualTo(20));
            Assert.That(snapshot.Positions[Creature], Is.EqualTo(new GridPosition(1, 0, 2)));
            Assert.That(snapshot.ActionEconomy[Creature].ActionsRemaining, Is.EqualTo(3));
            Assert.That(snapshot.MultipleAttackPenalty[Creature].AttackCount, Is.Zero);
            Assert.That(snapshot.Conditions[condition.Id], Is.EqualTo(condition));
            Assert.That(snapshot.Equipment[item.Id], Is.EqualTo(item));
            Assert.That(snapshot.ActiveEffects[effect.Id].Values[RuleValueKey.FromName("radius feet")], Is.EqualTo(RuleValue.FromInt(15)));
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
        public void RejectedReductionRollsBackEverySliceAndNeverMaterializesFacts()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;
            int factoryCalls = 0;
            RejectAfterWritesReducer reducer = new RejectAfterWritesReducer(() => factoryCalls++);

            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, -20)),
                reducer);

            Assert.That(result.IsRejected, Is.True);
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.RejectionReason, Is.EqualTo("rejected for test"));
            Assert.That(result.Facts, Is.Empty);
            Assert.That(factoryCalls, Is.Zero);
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
                new NoOpHealthReducer());

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.DidCommit, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(result.Snapshot, Is.SameAs(before));
            Assert.That(result.Snapshot.Version, Is.Zero);
        }

        [Test]
        public void SnapshotsAndFactOutputsCannotBeMutatedByAdapters()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesSnapshot before = store.Snapshot;
            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, -2)),
                new AdjustHealthReducer());

            Assert.That(before.Health[Creature].Current, Is.EqualTo(20));
            Assert.That(result.Snapshot.Health[Creature].Current, Is.EqualTo(18));
            Assert.That(before.Health, Is.Not.InstanceOf<IDictionary<CreatureId, HealthState>>());

            IList<RuleFact> facts = (IList<RuleFact>)result.Facts;
            Assert.Throws<NotSupportedException>(() => facts[0] = new HealthAdjustedFact(Creature, 0, 0));
        }

        [Test]
        public void ReducerCannotMutateCommittedStateThroughRetainedDraft()
        {
            InMemoryRulesStore store = CreateStore(20);
            RulesStateDraft retainedDraft = null;
            CaptureDraftReducer reducer = new CaptureDraftReducer(draft => retainedDraft = draft);

            ReductionResult<int> result = store.Reduce(
                Context(new AdjustHealthOp(Creature, -1)),
                reducer);
            retainedDraft.Health.Set(Creature, new HealthState(1, 20));

            Assert.That(result.Snapshot.Health[Creature].Current, Is.EqualTo(19));
            Assert.That(store.Snapshot.Health[Creature].Current, Is.EqualTo(19));
        }

        [Test]
        public void RuleValueMapDefensivelyCopiesCallerOwnedOperationData()
        {
            RuleValueKey key = RuleValueKey.FromName("selected mode");
            Dictionary<RuleValueKey, RuleValue> callerValues = new Dictionary<RuleValueKey, RuleValue>
            {
                [key] = new RuleValue("normal")
            };
            DataOp op = new DataOp(new RuleValueMap(callerValues));

            callerValues[key] = new RuleValue("caller-mutated");

            Assert.That(op.Values[key], Is.EqualTo(new RuleValue("normal")));
        }

        [Test]
        public void EquivalentReductionsAreDeterministic()
        {
            InMemoryRulesStore left = CreateStore(20);
            InMemoryRulesStore right = CreateStore(20);
            ReductionContext<AdjustHealthOp> leftContext = Context(new AdjustHealthOp(Creature, -4));
            ReductionContext<AdjustHealthOp> rightContext = Context(new AdjustHealthOp(Creature, -4));

            ReductionResult<int> leftResult = left.Reduce(leftContext, new AdjustHealthReducer());
            ReductionResult<int> rightResult = right.Reduce(rightContext, new AdjustHealthReducer());

            Assert.That(leftResult.Value, Is.EqualTo(rightResult.Value));
            Assert.That(leftResult.Snapshot.Version, Is.EqualTo(rightResult.Snapshot.Version));
            Assert.That(leftResult.Snapshot.Health[Creature], Is.EqualTo(rightResult.Snapshot.Health[Creature]));
            Assert.That(leftResult.Facts[0].Id, Is.EqualTo(rightResult.Facts[0].Id));
            Assert.That(leftResult.Facts[0].SourceOpId, Is.EqualTo(rightResult.Facts[0].SourceOpId));
            Assert.That(((HealthAdjustedFact)leftResult.Facts[0]).Current,
                Is.EqualTo(((HealthAdjustedFact)rightResult.Facts[0]).Current));
        }

        [Test]
        public void RejectedFactsDoNotConsumeStoreStampedIdentity()
        {
            InMemoryRulesStore store = CreateStore(20);
            store.Reduce(
                Context(new AdjustHealthOp(Creature, -20)),
                new RejectAfterWritesReducer(() => { }));

            ReductionResult<int> committed = store.Reduce(
                Context(new AdjustHealthOp(Creature, -1)),
                new AdjustHealthReducer());

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

        private sealed class DataOp : IRuleOp<bool>
        {
            public RuleValueMap Values { get; }
            public DataOp(RuleValueMap values) => Values = values ?? throw new ArgumentNullException(nameof(values));
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

        private sealed class AdjustHealthReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                HealthState previous = state.Health.TryGet(context.Op.Creature, out HealthState health)
                    ? health
                    : throw new InvalidOperationException("Missing health seed.");
                int current = previous.Current + context.Op.Delta;
                state.Health.Set(context.Op.Creature, new HealthState(current, previous.Maximum, previous.Temporary));
                facts.Stage(() => new HealthAdjustedFact(context.Op.Creature, previous.Current, current));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class NoOpHealthReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                state.Health.Set(context.Op.Creature, health);
                return ReductionResult<int>.Accept(health.Current);
            }
        }

        private sealed class RejectAfterWritesReducer : IOpReducer<AdjustHealthOp, int>
        {
            private readonly Action onFactMaterialized;

            public RejectAfterWritesReducer(Action onFactMaterialized)
            {
                this.onFactMaterialized = onFactMaterialized;
            }

            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                state.Health.Set(context.Op.Creature, new HealthState(0, health.Maximum));
                state.Positions.Set(context.Op.Creature, new GridPosition(9, 0, 9));
                facts.Stage(() =>
                {
                    onFactMaterialized();
                    return new HealthAdjustedFact(context.Op.Creature, health.Current, 0);
                });
                return ReductionResult<int>.Reject("rejected for test");
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
                FactSink facts)
            {
                state.Health.TryGet(context.Op.Creature, out HealthState health);
                int current = health.Current + context.Op.Delta;
                state.Health.Set(context.Op.Creature, new HealthState(current, health.Maximum));
                capture(state);
                return ReductionResult<int>.Accept(current);
            }
        }
    }
}
