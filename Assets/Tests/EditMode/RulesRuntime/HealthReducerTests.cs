using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class HealthReducerTests
    {
        [Test]
        public void HealthStateHashIncludesTemporaryHitPointImmunities()
        {
            HealthState withoutImmunity = new HealthState(10, 10);
            HealthState withImmunity = new HealthState(
                10,
                10,
                0,
                default,
                new[] { RuleSource.FromSlug("rage") }
            );
            HealthState matching = new HealthState(
                10,
                10,
                0,
                default,
                new[] { RuleSource.FromSlug("rage") }
            );

            Assert.That(withImmunity, Is.EqualTo(matching));
            Assert.That(withImmunity.GetHashCode(), Is.EqualTo(matching.GetHashCode()));
            int distinctHashes = new[]
            {
                withoutImmunity,
                withImmunity,
                new HealthState(10, 10, 0, default, new[] { RuleSource.FromSlug("bless") }),
            }
                .Select(value => value.GetHashCode())
                .Distinct()
                .Count();
            Assert.That(distinctHashes, Is.GreaterThan(1));
        }

        [Test]
        public void HealthStatePublicEqualityAndHashIgnoreTemporaryHitPointRevision()
        {
            HealthState visible = new HealthState(10, 10, 4, Rage);
            HealthState revised = visible.WithTemporaryHitPointRevision(37);

            Assert.That(revised.TemporaryHitPointRevision, Is.EqualTo(37));
            Assert.That(revised, Is.EqualTo(visible));
            Assert.That(revised.GetHashCode(), Is.EqualTo(visible.GetHashCode()));
            Assert.That(revised == visible, Is.True);
        }

        [Test]
        public void HealthDraftPreservesPublicSliceApiAndExactRevisionAcrossRemoveReadd()
        {
            HealthState initial = new HealthState(10, 10, 2, Other);
            RulesState state = new RulesState(new RulesStateSeed().SeedHealth(Creature, initial));
            RulesStateDraft draft = state.CreateDraft();

            SetHealthThroughPublicSlice(draft.Health, new HealthState(9, 10, 2, Other));
            Assert.That(
                RequireHealth(draft).TemporaryHitPointRevision,
                Is.EqualTo(initial.TemporaryHitPointRevision)
            );

            SetHealthThroughPublicSlice(draft.Health, new HealthState(9, 10, 3, Rage));
            Assert.That(RequireHealth(draft).TemporaryHitPointRevision, Is.EqualTo(1));

            SetHealthThroughPublicSlice(draft.Health, new HealthState(9, 10, 2, Other));
            Assert.That(RequireHealth(draft).TemporaryHitPointRevision, Is.EqualTo(2));
            Assert.That(
                draft.IsDirty,
                Is.True,
                "An away-and-back public pool value must still commit its exact revision."
            );
            Assert.That(draft.Health.Remove(new CreatureId("missing-health")), Is.False);
            HealthState beforeRemoval = RequireHealth(draft);
            Assert.That(draft.Health.Remove(Creature), Is.True);
            Assert.That(draft.Health.Contains(Creature), Is.False);
            Assert.That(draft.Health.Remove(Creature), Is.False);
            Assert.That(draft.Health.Set(Creature, beforeRemoval), Is.True);
            HealthState restored = RequireHealth(draft);
            Assert.That(restored, Is.EqualTo(beforeRemoval));
            Assert.That(restored.TemporaryHitPointRevision, Is.EqualTo(3));
            Assert.That(draft.IsDirty, Is.True);
            AssertOldExactRestorationRejected(draft, beforeRemoval);
        }

        [Test]
        public void HealthDraftCarriesRevisionTombstoneAcrossCommittedTransactions()
        {
            HealthState initial = new HealthState(10, 10, 4, Rage).WithTemporaryHitPointRevision(
                12
            );
            RulesState original = new RulesState(
                new RulesStateSeed().SeedHealth(Creature, initial)
            );
            RulesStateDraft removal = original.CreateDraft();
            Assert.That(removal.Health.Remove(Creature), Is.True);
            RulesState removed = new RulesState(removal.Build(1));
            RulesStateDraft readd = removed.CreateDraft();

            Assert.That(readd.Health.Set(Creature, initial), Is.True);

            HealthState restored = RequireHealth(readd);
            Assert.That(restored, Is.EqualTo(initial));
            Assert.That(restored.TemporaryHitPointRevision, Is.EqualTo(13));
            AssertOldExactRestorationRejected(readd, initial);
        }

        [Test]
        public void AcceptedAddThenRemoveCommitsRevisionTombstoneFromEmptyHealth()
        {
            HealthState initial = new HealthState(10, 10, 4, Rage);
            InMemoryRulesStore store = new InMemoryRulesStore();
            HealthDraftMutationReducer reducer = new HealthDraftMutationReducer();

            ReductionResult<bool> removed = store.Reduce(
                HealthDraftContext(
                    new HealthDraftMutationOp(HealthDraftMutation.AddThenRemove, initial)
                ),
                reducer
            );
            ReductionResult<bool> readded = store.Reduce(
                HealthDraftContext(new HealthDraftMutationOp(HealthDraftMutation.Readd, initial)),
                reducer
            );

            Assert.That(removed.IsAccepted, Is.True);
            Assert.That(removed.Value, Is.True);
            Assert.That(removed.DidCommit, Is.True);
            Assert.That(removed.Facts, Has.Count.EqualTo(1));
            Assert.That(removed.Snapshot.Health, Is.Empty);
            Assert.That(readded.IsAccepted, Is.True);
            Assert.That(readded.DidCommit, Is.True);
            Assert.That(readded.Snapshot.Health[Creature], Is.EqualTo(initial));
            Assert.That(readded.Snapshot.Health[Creature].TemporaryHitPointRevision, Is.EqualTo(1));
        }

        [Test]
        public void HealthDraftFailsClosedBeforeReaddingMaximumRevisionTombstone()
        {
            HealthState exhausted = new HealthState(10, 10, 2, Other).WithTemporaryHitPointRevision(
                long.MaxValue
            );
            RulesState original = new RulesState(
                new RulesStateSeed().SeedHealth(Creature, exhausted)
            );
            RulesStateDraft removal = original.CreateDraft();
            Assert.That(removal.Health.Remove(Creature), Is.True);
            RulesState removed = new RulesState(removal.Build(1));
            RulesStateDraft readd = removed.CreateDraft();

            Assert.That(readd.Health.Remove(Creature), Is.False);
            Assert.Throws<InvalidOperationException>(() => readd.Health.Set(Creature, exhausted));
            Assert.That(readd.Health.Contains(Creature), Is.False);
            Assert.Throws<InvalidOperationException>(() => readd.Health.Set(Creature, exhausted));
            Assert.That(readd.Health.Contains(Creature), Is.False);
        }

        [Test]
        public void RejectedReductionDoesNotLeakHealthOrRevisionTombstoneMutation()
        {
            HealthState initial = new HealthState(10, 10, 3, Rage).WithTemporaryHitPointRevision(8);
            InMemoryRulesStore store = new InMemoryRulesStore(
                new RulesStateSeed().SeedHealth(Creature, initial)
            );
            HealthDraftMutationReducer reducer = new HealthDraftMutationReducer();

            ReductionResult<bool> removed = store.Reduce(
                HealthDraftContext(new HealthDraftMutationOp(HealthDraftMutation.Remove, initial)),
                reducer
            );
            RulesSnapshot removedSnapshot = store.Snapshot;
            ReductionResult<bool> rejected = store.Reduce(
                HealthDraftContext(
                    new HealthDraftMutationOp(HealthDraftMutation.ReaddThenReject, initial)
                ),
                reducer
            );
            ReductionResult<bool> readded = store.Reduce(
                HealthDraftContext(new HealthDraftMutationOp(HealthDraftMutation.Readd, initial)),
                reducer
            );

            Assert.That(removed.IsAccepted, Is.True);
            Assert.That(removed.Facts, Has.Count.EqualTo(1));
            Assert.That(rejected.IsRejected, Is.True);
            Assert.That(rejected.Facts, Is.Empty);
            Assert.That(rejected.Snapshot, Is.SameAs(removedSnapshot));
            Assert.That(removedSnapshot.Health.Contains(Creature), Is.False);
            Assert.That(readded.IsAccepted, Is.True);
            Assert.That(readded.Facts, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.Health[Creature], Is.EqualTo(initial));
            Assert.That(store.Snapshot.Health[Creature].TemporaryHitPointRevision, Is.EqualTo(9));
            Assert.That(
                typeof(RulesSnapshot).GetProperties().Select(property => property.Name),
                Does.Not.Contain("TemporaryHitPointRevisionTombstones")
            );
            Assert.That(
                typeof(RulesStateSeed).GetProperties().Select(property => property.Name),
                Does.Not.Contain("TemporaryHitPointRevisionTombstones")
            );
        }

        [Test]
        public void ExactRestorationRejectsSameRevisionWithDifferentPoolValues()
        {
            HealthState malformedCurrent = new HealthState(
                10,
                10,
                4,
                Rage
            ).WithTemporaryHitPointRevision(7);
            RulesStateDraft draft = new RulesState(
                new RulesStateSeed().SeedHealth(Creature, malformedCurrent)
            ).CreateDraft();

            bool prepared = TemporaryHitPointsGrantReduction.TryPrepareExactRestoration(
                draft,
                Creature,
                Rage,
                new TemporaryHitPointsPoolState(3, Rage, 7),
                new TemporaryHitPointsPoolState(2, Other, 6),
                out _,
                out string rejection
            );

            Assert.That(prepared, Is.False);
            Assert.That(rejection, Does.Contain("conflicting pool values"));
            Assert.That(RequireHealth(draft), Is.EqualTo(malformedCurrent));
            Assert.That(draft.IsDirty, Is.False);
        }

        [Test]
        public void HealthDraftFailsClosedBeforeMutatingAnExhaustedPoolRevision()
        {
            HealthState exhausted = new HealthState(10, 10, 2, Other).WithTemporaryHitPointRevision(
                long.MaxValue
            );
            RulesStateDraft draft = new RulesState(
                new RulesStateSeed().SeedHealth(Creature, exhausted)
            ).CreateDraft();

            Assert.Throws<InvalidOperationException>(() =>
                draft.Health.Set(Creature, new HealthState(10, 10, 3, Rage))
            );

            HealthState retained = RequireHealth(draft);
            Assert.That(retained.Temporary, Is.EqualTo(2));
            Assert.That(retained.TemporarySource, Is.EqualTo(Other));
            Assert.That(retained.TemporaryHitPointRevision, Is.EqualTo(long.MaxValue));
        }

        private static readonly CreatureId Creature = new CreatureId("health-target");
        private static readonly RuleSource Strike = RuleSource.FromSlug("strike");
        private static readonly RuleSource Rage = RuleSource.FromSlug("rage");
        private static readonly RuleSource Other = RuleSource.FromSlug("other-temp-hp");

        [Test]
        public void HealthBatchRejectsRepeatedTargetBeforeDispatch()
        {
            HealthBatchChange damage = new HealthBatchChange(
                HealthBatchChangeKind.Damage,
                Creature,
                10,
                new HealthChangeOriginId("batch-lethal"),
                Strike
            );
            HealthBatchChange healing = new HealthBatchChange(
                HealthBatchChangeKind.Healing,
                Creature,
                1,
                new HealthChangeOriginId("batch-healing"),
                RuleSource.FromSlug("heal")
            );

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                new ApplyHealthBatchOp(new[] { damage, healing })
            );

            Assert.That(error.ParamName, Is.EqualTo("changes"));
            Assert.That(error.Message, Does.Contain("cannot repeat a target"));
        }

        [Test]
        public void HealthCompositionCompletesSynchronouslyWithSynchronousCallbacks()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(10, 10));

            ValueTask<OpResult<DamageOutcome>> pending = dispatcher.Dispatch(
                Damage(1, Strike, "synchronous-contract")
            );

            Assert.That(pending.IsCompletedSuccessfully, Is.True);
            Assert.That(RequireResolved(pending.Result).Value.Applied, Is.EqualTo(1));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(9));
        }

        [Test]
        public async Task DamageConsumesTemporaryFirstAndReportsExactClampedBreakdown()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(5, 10, 3, Rage));

            OpResult<DamageOutcome> result = await dispatcher.Dispatch(
                Damage(12, Strike, "damage-clamp")
            );
            ResolvedOpResult<DamageOutcome> resolved = RequireResolved(result);

            Assert.That(resolved.Value.Requested, Is.EqualTo(12));
            Assert.That(resolved.Value.AppliedToTemporary, Is.EqualTo(3));
            Assert.That(resolved.Value.AppliedToCurrent, Is.EqualTo(5));
            Assert.That(resolved.Value.Applied, Is.EqualTo(8));
            HealthState defeated = dispatcher.Snapshot.Health[Creature];
            Assert.That(defeated.Current, Is.Zero);
            Assert.That(defeated.Maximum, Is.EqualTo(10));
            Assert.That(defeated.Temporary, Is.Zero);
            Assert.That(defeated.IsCommittedDefeated, Is.True);
            DamageAppliedFact damage = resolved.Facts.OfType<DamageAppliedFact>().Single();
            Assert.That(damage.AppliedToTemporary, Is.EqualTo(3));
            Assert.That(damage.AppliedToCurrent, Is.EqualTo(5));
            Assert.That(
                resolved.Facts.OfType<TemporaryHitPointsConsumedFact>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(resolved.Facts.OfType<CreatureReducedToZeroFact>().Count(), Is.EqualTo(1));
            Assert.That(
                resolved.Facts.OfType<CreatureDefeatCommittedFact>().Count(),
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task ZeroAndAlreadyZeroDamageEmitNoDamageOrZeroFacts()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(0, 10));

            OpResult<DamageOutcome> zero = await dispatcher.Dispatch(
                Damage(0, Strike, "zero-damage")
            );
            OpResult<DamageOutcome> alreadyZero = await dispatcher.Dispatch(
                Damage(4, Strike, "already-zero")
            );

            Assert.That(RequireResolved(zero).Value.Applied, Is.Zero);
            Assert.That(RequireResolved(alreadyZero).Value.Applied, Is.Zero);
            Assert.That(zero.Facts, Is.Empty);
            Assert.That(alreadyZero.Facts, Is.Empty);
        }

        [Test]
        public async Task PositiveToZeroEmitsOnceAndAlreadyZeroNeverRepeats()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(2, 10));

            OpResult<DamageOutcome> lethal = await dispatcher.Dispatch(Damage(2, Strike, "lethal"));
            OpResult<DamageOutcome> repeated = await dispatcher.Dispatch(
                Damage(2, Strike, "repeated")
            );

            Assert.That(lethal.Facts.OfType<CreatureReducedToZeroFact>().Count(), Is.EqualTo(1));
            Assert.That(repeated.Facts.OfType<CreatureReducedToZeroFact>(), Is.Empty);
            Assert.That(repeated.Facts.OfType<DamageAppliedFact>(), Is.Empty);
        }

        [Test]
        public async Task ReductionToZeroCommitsDefeatWhenNoEncounterOwnsReactions()
        {
            GridPosition occupiedPosition = new GridPosition(2, 0, 3);
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Creature, new PlayerId("party")))
                .SeedHealth(Creature, new HealthState(2, 10))
                .SeedPosition(Creature, occupiedPosition);
            RuleDispatcher dispatcher = CreateDispatcher(seed);

            OpResult<DamageOutcome> result = await dispatcher.Dispatch(
                Damage(2, Strike, "vacate-on-defeat")
            );

            Assert.That(RequireResolved(result).Value.AppliedToCurrent, Is.EqualTo(2));
            Assert.That(dispatcher.Snapshot.Positions.Contains(Creature), Is.False);
            HealthState defeated = dispatcher.Snapshot.Health[Creature];
            Assert.That(defeated.Current, Is.Zero);
            Assert.That(dispatcher.Snapshot.Creatures[Creature].Id, Is.EqualTo(Creature));
            Assert.That(result.Facts.OfType<CreatureReducedToZeroFact>().Count(), Is.EqualTo(1));
            Assert.That(defeated.IsCommittedDefeated, Is.True);
            Assert.That(result.Facts.OfType<CreatureDefeatCommittedFact>().Count(), Is.EqualTo(1));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(
                    new ApplyHealingOp(
                        Creature,
                        1,
                        new HealthChangeOriginId("heal-committed-defeat"),
                        RuleSource.FromSlug("healing")
                    )
                )
            );
        }

        [Test]
        public async Task HealingCommitsOnlyAmountToMaximumAndThenBecomesNoOp()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(8, 10));
            HealthChangeOriginId origin = new HealthChangeOriginId("heal-origin");
            RuleSource source = RuleSource.FromSlug("heal");

            OpResult<HealingOutcome> healed = await dispatcher.Dispatch(
                new ApplyHealingOp(Creature, 9, origin, source)
            );
            OpResult<HealingOutcome> full = await dispatcher.Dispatch(
                new ApplyHealingOp(Creature, 1, new HealthChangeOriginId("full-origin"), source)
            );

            Assert.That(RequireResolved(healed).Value.Applied, Is.EqualTo(2));
            Assert.That(healed.Facts.OfType<HealingAppliedFact>().Single().Applied, Is.EqualTo(2));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(10));
            Assert.That(RequireResolved(full).Value.Applied, Is.Zero);
            Assert.That(full.Facts, Is.Empty);
        }

        [Test]
        public async Task SourceTemporaryHitPointsGrantConsumeRemoveAndRespectImmunity()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(10, 10));

            OpResult<TemporaryHitPointsGrantOutcome> granted = await dispatcher.Dispatch(
                Grant(5, Rage, "grant-rage")
            );
            OpResult<TemporaryHitPointsGrantOutcome> lower = await dispatcher.Dispatch(
                Grant(3, Other, "grant-lower")
            );
            OpResult<DamageOutcome> consumed = await dispatcher.Dispatch(
                Damage(2, Strike, "consume")
            );
            OpResult<TemporaryHitPointsRemovalOutcome> wrongRemoval = await dispatcher.Dispatch(
                new RemoveTemporaryHitPointsOp(
                    Creature,
                    new HealthChangeOriginId("wrong-removal"),
                    Other
                )
            );
            OpResult<TemporaryHitPointsRemovalOutcome> removed = await dispatcher.Dispatch(
                new RemoveTemporaryHitPointsOp(
                    Creature,
                    new HealthChangeOriginId("rage-removal"),
                    Rage
                )
            );
            OpResult<TemporaryHitPointImmunityOutcome> immunity = await dispatcher.Dispatch(
                new AddTemporaryHitPointImmunityOp(
                    Creature,
                    new HealthChangeOriginId("rage-immunity"),
                    Rage
                )
            );
            OpResult<TemporaryHitPointsGrantOutcome> immuneGrant = await dispatcher.Dispatch(
                Grant(6, Rage, "immune-grant")
            );

            Assert.That(RequireResolved(granted).Value.Granted, Is.True);
            Assert.That(
                granted.Facts.OfType<TemporaryHitPointsGrantedFact>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(RequireResolved(lower).Value.Granted, Is.False);
            Assert.That(lower.Facts, Is.Empty);
            Assert.That(RequireResolved(consumed).Value.AppliedToTemporary, Is.EqualTo(2));
            Assert.That(
                consumed.Facts.OfType<TemporaryHitPointsConsumedFact>().Single().Amount,
                Is.EqualTo(2)
            );
            Assert.That(RequireResolved(wrongRemoval).Value.Removed, Is.Zero);
            Assert.That(RequireResolved(removed).Value.Removed, Is.EqualTo(3));
            Assert.That(
                removed.Facts.OfType<TemporaryHitPointsRemovedFact>().Count(),
                Is.EqualTo(1)
            );
            Assert.That(RequireResolved(immunity).Value.Added, Is.True);
            Assert.That(RequireResolved(immuneGrant).Value.Immune, Is.True);
            Assert.That(immuneGrant.Facts, Is.Empty);
            Assert.That(dispatcher.Snapshot.Health[Creature].Temporary, Is.Zero);
            Assert.That(
                dispatcher.Snapshot.Health[Creature].HasTemporaryHitPointImmunity(Rage),
                Is.True
            );
            Assert.That(
                dispatcher.Snapshot.Health[Creature].TemporaryHitPointRevision,
                Is.EqualTo(3),
                "Grant, temporary-HP damage, and removal are the only pool mutations."
            );
        }

        [Test]
        public void ReducerOperationsRejectExternalDispatch()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(10, 10));

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(
                        new CommitDamageOp(
                            Creature,
                            1,
                            new HealthChangeOriginId("forged-origin"),
                            Strike
                        )
                    )
            );

            StringAssert.Contains("nested-only", error.Message);
        }

        [Test]
        public async Task FactsCarryRuleSourceAndDispatcherOwnedNestedProvenance()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(10, 10));

            OpResult<DamageOutcome> result = await dispatcher.Dispatch(
                Damage(1, Strike, "provenance")
            );
            RuleFact fact = result.Facts.Single();

            Assert.That(fact.Source, Is.EqualTo(Strike));
            Assert.That(fact.SourceOpId, Is.Not.EqualTo(fact.RootOpId));
            Assert.That(dispatcher.Trace.IsDescendantOf(fact.SourceOpId, fact.RootOpId), Is.True);
            Assert.That(fact.Id.IsEmpty, Is.False);
        }

        private static RuleDispatcher CreateDispatcher(HealthState health) =>
            CreateDispatcher(new RulesStateSeed().SeedHealth(Creature, health));

        private static RuleDispatcher CreateDispatcher(RulesStateSeed seed) =>
            new RuleDispatcherBuilder(new InMemoryRulesStore(seed)).UseHealthRules().Build();

        private static ApplyDamageOp Damage(int amount, RuleSource source, string origin) =>
            new ApplyDamageOp(Creature, amount, new HealthChangeOriginId(origin), source);

        private static GrantTemporaryHitPointsOp Grant(
            int amount,
            RuleSource source,
            string origin
        ) =>
            new GrantTemporaryHitPointsOp(
                Creature,
                amount,
                new HealthChangeOriginId(origin),
                source
            );

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(
            OpResult<TResult> result
        ) =>
            result as ResolvedOpResult<TResult>
            ?? throw new AssertionException($"Expected Resolved but received {result.Status}.");

        private static HealthState RequireHealth(RulesStateDraft draft)
        {
            Assert.That(draft.Health.TryGet(Creature, out HealthState health), Is.True);
            return health;
        }

        private static void SetHealthThroughPublicSlice(
            StateSliceDraft<CreatureId, HealthState> health,
            HealthState value
        ) => health.Set(Creature, value);

        private static void AssertOldExactRestorationRejected(
            RulesStateDraft draft,
            HealthState oldHealth
        )
        {
            TemporaryHitPointsPoolState oldPool = TemporaryHitPointsPoolState.Capture(oldHealth);
            bool prepared = TemporaryHitPointsGrantReduction.TryPrepareExactRestoration(
                draft,
                Creature,
                oldHealth.TemporarySource,
                oldPool,
                oldPool,
                out _,
                out string rejection
            );

            Assert.That(prepared, Is.False);
            Assert.That(rejection, Does.Contain("newer pool"));
        }

        private static ReductionContext<HealthDraftMutationOp> HealthDraftContext(
            HealthDraftMutationOp operation
        ) =>
            new ReductionContext<HealthDraftMutationOp>(operation, new OpId(2), new OpId(1), Other);

        private enum HealthDraftMutation
        {
            AddThenRemove,
            Remove,
            Readd,
            ReaddThenReject,
        }

        private sealed class HealthDraftMutationOp : IRuleOp<bool>
        {
            internal HealthDraftMutationOp(HealthDraftMutation mutation, HealthState value)
            {
                Mutation = mutation;
                Value = value;
            }

            internal HealthDraftMutation Mutation { get; }

            internal HealthState Value { get; }
        }

        private sealed class HealthDraftMutationFact : RuleFact { }

        private sealed class HealthDraftMutationReducer : IOpReducer<HealthDraftMutationOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<HealthDraftMutationOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                bool changed;
                if (context.Op.Mutation == HealthDraftMutation.AddThenRemove)
                {
                    state.Health.Set(Creature, context.Op.Value);
                    state.Health.Remove(Creature);
                    changed = state.IsDirty;
                }
                else
                {
                    changed =
                        context.Op.Mutation == HealthDraftMutation.Remove
                            ? state.Health.Remove(Creature)
                            : state.Health.Set(Creature, context.Op.Value);
                }
                if (changed)
                    facts.Stage(new HealthDraftMutationFact());
                return context.Op.Mutation == HealthDraftMutation.ReaddThenReject
                    ? ReductionResult<bool>.Reject("rejected health draft mutation")
                    : ReductionResult<bool>.Accept(changed);
            }
        }
    }
}
