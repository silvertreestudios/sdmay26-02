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
        public async Task FactPayloadAndTraceKeepDomainDataSeparateFromDispatchProvenance()
        {
            RuleDispatcher dispatcher = CreateDispatcher(new HealthState(10, 10));

            OpResult<DamageOutcome> result = await dispatcher.Dispatch(
                Damage(1, Strike, "provenance")
            );
            DamageAppliedFact fact = result.Facts.OfType<DamageAppliedFact>().Single();
            IOpFrameView root = dispatcher.Trace.OrderedFrames.Single(frame =>
                !frame.ParentId.HasValue
            );
            IOpFrameView nested = dispatcher.Trace.OrderedFrames.Single(frame =>
                frame.ParentId.HasValue
            );

            Assert.That(dispatcher.Trace.IsDescendantOf(nested.Id, root.Id), Is.True);
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
    }
}
