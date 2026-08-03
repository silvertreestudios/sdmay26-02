using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class SpellSaveRulesTests
    {
        private static readonly CreatureId Caster = new("save-spell-caster");
        private static readonly CreatureId Target = new("save-spell-target");
        private static readonly CreatureId OtherTarget = new("save-spell-other-target");
        private static readonly SpellReference Hymn = new(new SpellId("haunting-hymn"), 1);
        private static readonly SpellActionVariant TwoActions = new(2);
        private static readonly SpellAreaPlacement Placement = new(
            SpellAreaShape.Cone,
            new GridPosition(0, 0, 0),
            0,
            0,
            SpellAreaDirection.North
        );

        [Test]
        public void ZeroResolutionCategoriesAreRejectedAtDefinitionConstruction()
        {
            ArgumentException failure = Assert.Throws<ArgumentException>(() =>
                _ = new SpellDefinition(
                    Hymn.Spell,
                    "Haunting Hymn",
                    1,
                    new[] { TwoActions },
                    new[] { Trait.FromSlug("cantrip") },
                    Array.Empty<SpellEffectDirective>(),
                    Array.Empty<SpellAttackDefinition>(),
                    Array.Empty<SpellSaveDefinition>()
                )
            );

            Assert.That(failure.Message, Does.Contain("exactly one modeled resolution category"));
        }

        [Test]
        public void MixedEffectsAndSavesAreRejectedAtDefinitionConstruction()
        {
            ArgumentException failure = Assert.Throws<ArgumentException>(() =>
                _ = new SpellDefinition(
                    new SpellId("unsafe-mixed-spell"),
                    "Unsafe Mixed Spell",
                    1,
                    new[] { TwoActions },
                    new[] { Trait.FromSlug("cantrip") },
                    new[]
                    {
                        new SpellEffectDirective(
                            new RuleDefinitionId("unsafe-effect"),
                            EffectDuration.Indefinite,
                            "self"
                        ),
                    },
                    Array.Empty<SpellAttackDefinition>(),
                    new[]
                    {
                        new SpellSaveDefinition(
                            SaveKind.Fortitude,
                            true,
                            new SpellAreaTarget(SpellAreaShape.Cone, 15),
                            new[]
                            {
                                new TypedDamageDice(
                                    new DiceExpression(1, 4),
                                    "sonic",
                                    "unsafe-mixed-spell"
                                ),
                            },
                            Array.Empty<SpellSaveConditionDirective>()
                        ),
                    }
                )
            );

            Assert.That(failure.Message, Does.Contain("exactly one modeled resolution category"));
        }

        [Test]
        public async Task CriticalFailureAppliesDoubleDamageConditionAndActionCosts()
        {
            TestRuntime runtime = CreateRuntime(conditionImmune: false, 1, 4);

            ResolvedOpResult<CastSpellOutcome> resolved = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast())
            );

            SpellSaveResolution save = resolved.Value.Saves.Single();
            Assert.That(save.Target, Is.EqualTo(Target));
            Assert.That(save.Check.Degree, Is.EqualTo(DegreeOfSuccess.CriticalFailure));
            Assert.That(save.FinalDamage, Is.EqualTo(8));
            Assert.That(save.Conditions, Has.Count.EqualTo(1));
            Assert.That(
                save.Conditions.Single().Application.Status,
                Is.EqualTo(ConditionApplicationStatus.Applied)
            );
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(12));
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(1)
            );
            Assert.That(resolved.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(resolved.Facts.OfType<ActionReceiptCommittedFact>().Count(), Is.EqualTo(1));
            Assert.That(resolved.Facts.OfType<SpellSlotSpentFact>(), Is.Empty);
            Assert.That(
                resolved
                    .Facts.OfType<ActiveEffectCreatedFact>()
                    .Count(fact => fact.DefinitionId == ConditionRuleDefinitions.Deafened),
                Is.EqualTo(1)
            );
            Assert.That(
                runtime.Dispatcher.Trace.OrderedFrames.Count(frame =>
                    frame.OpType == typeof(CommitPreparedSpellCastOp)
                ),
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task CriticalFailureImmunityIsBlockedWithoutConditionFactsOrRollback()
        {
            TestRuntime runtime = CreateRuntime(conditionImmune: true, 1, 4);

            ResolvedOpResult<CastSpellOutcome> resolved = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast())
            );

            SpellSaveResolution save = resolved.Value.Saves.Single();
            ConditionApplicationOutcome condition = save.Conditions.Single().Application;
            Assert.That(save.Check.Degree, Is.EqualTo(DegreeOfSuccess.CriticalFailure));
            Assert.That(save.FinalDamage, Is.EqualTo(8));
            Assert.That(condition.Status, Is.EqualTo(ConditionApplicationStatus.Blocked));
            Assert.That(condition.BlockedReason, Does.Contain("immune to deafened"));
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(12));
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(1)
            );
            Assert.That(runtime.Store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(
                resolved
                    .Facts.OfType<ActiveEffectCreatedFact>()
                    .Where(fact => fact.DefinitionId == ConditionRuleDefinitions.Deafened),
                Is.Empty
            );
        }

        [Test]
        public async Task OrdinaryFailureDealsNormalDamageWithoutCriticalFailureCondition()
        {
            TestRuntime runtime = CreateRuntime(conditionImmune: false, 15, 4);

            ResolvedOpResult<CastSpellOutcome> resolved = RequireResolved(
                await runtime.Dispatcher.Dispatch(Cast())
            );

            SpellSaveResolution save = resolved.Value.Saves.Single();
            Assert.That(save.Check.Degree, Is.EqualTo(DegreeOfSuccess.Failure));
            Assert.That(save.FinalDamage, Is.EqualTo(4));
            Assert.That(save.Conditions, Is.Empty);
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(16));
            Assert.That(runtime.Store.Snapshot.ActiveEffects, Is.Empty);
        }

        [Test]
        public async Task SuccessDealsHalfDamageRoundedDown()
        {
            TestRuntime runtime = CreateRuntimeForTargets(
                conditionImmune: false,
                defineConditionRules: true,
                targetFortitude: 10,
                new[] { Target },
                10,
                5
            );

            SpellSaveResolution save = RequireResolved(await runtime.Dispatcher.Dispatch(Cast()))
                .Value.Saves.Single();

            Assert.That(save.Check.Degree, Is.EqualTo(DegreeOfSuccess.Success));
            Assert.That(save.RequestedDamage, Is.EqualTo(2));
            Assert.That(save.FinalDamage, Is.EqualTo(2));
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(18));
        }

        [Test]
        public async Task TwoTargetsShareOneDamageRollInDeterministicSelectionOrder()
        {
            TestRuntime runtime = CreateRuntimeForTargets(
                conditionImmune: false,
                defineConditionRules: true,
                targetFortitude: 0,
                new[] { Target, OtherTarget },
                15,
                20,
                6,
                2
            );
            CastSpellActionOp operation = new(
                new ActionInvocationId("hymn-shared-damage"),
                Caster,
                Hymn,
                TwoActions,
                new SpellCastSelection(Placement, new[] { Target, OtherTarget })
            );

            CastSpellOutcome outcome = RequireResolved(
                await runtime.Dispatcher.Dispatch(operation)
            ).Value;

            Assert.That(
                outcome.Saves.Select(save => save.Target),
                Is.EqualTo(new[] { Target, OtherTarget })
            );
            Assert.That(outcome.Saves[0].Check.Degree, Is.EqualTo(DegreeOfSuccess.Failure));
            Assert.That(outcome.Saves[0].FinalDamage, Is.EqualTo(6));
            Assert.That(outcome.Saves[1].Check.Degree, Is.EqualTo(DegreeOfSuccess.CriticalSuccess));
            Assert.That(outcome.Saves[1].FinalDamage, Is.Zero);
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(1));
        }

        [Test]
        public async Task OverkillReportsRequestedAndActuallyAppliedDamageSeparately()
        {
            TestRuntime runtime = CreateRuntimeWithHealth(3, 0, 1, 4);

            SpellSaveResolution save = RequireResolved(await runtime.Dispatcher.Dispatch(Cast()))
                .Value.Saves.Single();

            Assert.That(save.RequestedDamage, Is.EqualTo(8));
            Assert.That(save.FinalDamage, Is.EqualTo(3));
            Assert.That(save.DamageOutcome.AppliedToTemporary, Is.Zero);
            Assert.That(save.DamageOutcome.AppliedToCurrent, Is.EqualTo(3));
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.Zero);
        }

        [Test]
        public async Task TemporaryHitPointsAreIncludedInCommittedDamageOutcome()
        {
            TestRuntime runtime = CreateRuntimeWithHealth(20, 5, 1, 4);

            SpellSaveResolution save = RequireResolved(await runtime.Dispatcher.Dispatch(Cast()))
                .Value.Saves.Single();

            Assert.That(save.RequestedDamage, Is.EqualTo(8));
            Assert.That(save.FinalDamage, Is.EqualTo(8));
            Assert.That(save.DamageOutcome.AppliedToTemporary, Is.EqualTo(5));
            Assert.That(save.DamageOutcome.AppliedToCurrent, Is.EqualTo(3));
            Assert.That(runtime.Store.Snapshot.Health[Target].Temporary, Is.Zero);
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(17));
        }

        [Test]
        public async Task CriticalSuccessCommitsAZeroDamageNoOpOutcome()
        {
            TestRuntime runtime = CreateRuntime(false, 20, 4);
            CastSpellActionOp operation = Cast();

            ResolvedOpResult<CastSpellOutcome> first = RequireResolved(
                await runtime.Dispatcher.Dispatch(operation)
            );
            SpellSaveResolution save = first.Value.Saves.Single();

            Assert.That(save.Check.Degree, Is.EqualTo(DegreeOfSuccess.CriticalSuccess));
            Assert.That(save.RequestedDamage, Is.Zero);
            Assert.That(save.FinalDamage, Is.Zero);
            Assert.That(save.DamageOutcome.Applied, Is.Zero);
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(20));
            Assert.That(first.Facts.OfType<DamageAppliedFact>(), Is.Empty);
            Assert.That(
                first
                    .Facts.OfType<ActiveEffectCreatedFact>()
                    .Where(fact => fact.DefinitionId == ConditionRuleDefinitions.Deafened),
                Is.Empty
            );
            ActionReceiptCommittedFact receipt = first
                .Facts.OfType<ActionReceiptCommittedFact>()
                .Single();
            Assert.That(receipt.Actor, Is.EqualTo(Caster));
            Assert.That(receipt.DefinitionId, Is.EqualTo(operation.DefinitionId));

            long committedVersion = runtime.Store.Snapshot.Version;
            ResolvedOpResult<CastSpellOutcome> replay = RequireResolved(
                await runtime.Dispatcher.Dispatch(operation)
            );

            Assert.That(replay.Value, Is.SameAs(first.Value));
            Assert.That(replay.Facts, Is.Empty);
            Assert.That(runtime.Store.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(20));
            Assert.That(runtime.Rolls.Remaining, Is.Zero);
        }

        [Test]
        public void ConditionObserverFailureCommitsOnceAndRetryCannotReplayDamageOrCosts()
        {
            TestRuntime runtime = CreateRuntime(conditionImmune: false, 1, 4);
            InvalidOperationException expected = new("injected condition observer failure");
            ThrowOnceConditionObserver observer = new(expected);
            runtime.Dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(observer);

            InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await runtime.Dispatcher.Dispatch(Cast())
            );

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(12));
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(1)
            );
            Assert.That(runtime.Store.Snapshot.ActiveEffects, Has.Count.EqualTo(1));
            Assert.That(observer.ObservedHealth, Is.EqualTo(12));
            Assert.That(observer.ObservedConditions, Is.EqualTo(1));

            OpResult<CastSpellOutcome> retry = runtime
                .Dispatcher.Dispatch(Cast())
                .GetAwaiter()
                .GetResult();

            ResolvedOpResult<CastSpellOutcome> retried = RequireResolved(retry);
            Assert.That(retried.Value.Saves.Single().FinalDamage, Is.EqualTo(8));
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(12));
            Assert.That(runtime.Store.Snapshot.ActiveEffects, Has.Count.EqualTo(1));
            Assert.That(observer.Calls, Is.EqualTo(1));
            Assert.That(runtime.Rolls.Remaining, Is.Zero);
        }

        [Test]
        public void MissingSecondaryEffectDefinitionFailsBeforeAnyCastStateCommits()
        {
            TestRuntime runtime = CreateRuntime(false, false, 1, 4);

            InvalidOperationException failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await runtime.Dispatcher.Dispatch(Cast())
            );

            Assert.That(failure.Message, Does.Contain("absent from the encounter registry"));
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(20));
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(runtime.Store.Snapshot.Version, Is.Zero);
        }

        [Test]
        public async Task ExtraAreaTargetRejectsBeforeCostsOrRolls()
        {
            TestRuntime runtime = CreateRuntime(false, 1, 4);

            OpResult<CastSpellOutcome> result = await runtime.Dispatcher.Dispatch(
                new CastSpellActionOp(
                    new ActionInvocationId("hymn-extra-target"),
                    Caster,
                    Hymn,
                    TwoActions,
                    new SpellCastSelection(Placement, new[] { Target, OtherTarget })
                )
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(20));
            Assert.That(runtime.Store.Snapshot.Health[OtherTarget].Current, Is.EqualTo(20));
            Assert.That(runtime.Store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(runtime.Store.Snapshot.RuleBindings, Is.Empty);
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(2));
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public async Task DuplicateAreaTargetRejectsBeforeCostsOrRolls()
        {
            TestRuntime runtime = CreateRuntime(false, 1, 4);
            CastSpellActionOp operation = new(
                new ActionInvocationId("hymn-duplicate-target"),
                Caster,
                Hymn,
                TwoActions,
                new SpellCastSelection(Placement, new[] { Target, Target })
            );

            OpResult<CastSpellOutcome> result = await runtime.Dispatcher.Dispatch(operation);

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(2));
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public async Task OmittedAreaTargetRejectsBeforeCostsOrRolls()
        {
            TestRuntime runtime = CreateRuntime(false, 1, 4);
            CastSpellActionOp operation = new(
                new ActionInvocationId("hymn-omitted-target"),
                Caster,
                Hymn,
                TwoActions,
                new SpellCastSelection(Placement, Array.Empty<CreatureId>())
            );

            OpResult<CastSpellOutcome> result = await runtime.Dispatcher.Dispatch(operation);

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(2));
        }

        [Test]
        public async Task OffConePlacementRejectsBeforeCostsOrRolls()
        {
            TestRuntime runtime = CreateRuntime(false, 1, 4);
            SpellAreaPlacement offCone = new(
                SpellAreaShape.Cone,
                Placement.OriginCell,
                Placement.OriginCornerX,
                Placement.OriginCornerZ,
                SpellAreaDirection.East
            );

            OpResult<CastSpellOutcome> result = await runtime.Dispatcher.Dispatch(
                new CastSpellActionOp(
                    new ActionInvocationId("hymn-off-cone"),
                    Caster,
                    Hymn,
                    TwoActions,
                    new SpellCastSelection(offCone, new[] { Target })
                )
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(2));
        }

        [Test]
        public async Task ConflictingReuseOfCommittedInvocationIdRejectsWithoutReplay()
        {
            TestRuntime runtime = CreateRuntime(false, 1, 4);
            CastSpellActionOp first = Cast();
            RequireResolved(await runtime.Dispatcher.Dispatch(first));
            long committedVersion = runtime.Store.Snapshot.Version;

            OpResult<CastSpellOutcome> conflict = await runtime.Dispatcher.Dispatch(
                new CastSpellActionOp(
                    first.InvocationId,
                    Caster,
                    Hymn,
                    TwoActions,
                    new SpellCastSelection(Placement, Array.Empty<CreatureId>())
                )
            );

            Assert.That(conflict, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                ((InvalidOpResult<CastSpellOutcome>)conflict).Reason,
                Does.Contain("different intent")
            );
            Assert.That(runtime.Store.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(runtime.Store.Snapshot.Health[Target].Current, Is.EqualTo(12));
            Assert.That(runtime.Rolls.Remaining, Is.Zero);
            Assert.That(conflict.Facts, Is.Empty);
        }

        [Test]
        public async Task PrecommitInvalidIntentCanRetrySameInvocationAfterCorrection()
        {
            TestRuntime runtime = CreateRuntime(false, 1, 4);
            ActionInvocationId invocation = new("hymn-corrected-precommit");
            OpResult<CastSpellOutcome> invalid = await runtime.Dispatcher.Dispatch(
                new CastSpellActionOp(
                    invocation,
                    Caster,
                    Hymn,
                    TwoActions,
                    new SpellCastSelection(Placement, Array.Empty<CreatureId>())
                )
            );

            ResolvedOpResult<CastSpellOutcome> corrected = RequireResolved(
                await runtime.Dispatcher.Dispatch(
                    new CastSpellActionOp(
                        invocation,
                        Caster,
                        Hymn,
                        TwoActions,
                        new SpellCastSelection(Placement, new[] { Target })
                    )
                )
            );

            Assert.That(invalid, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(corrected.Value.Saves.Single().FinalDamage, Is.EqualTo(8));
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(1)
            );
        }

        [Test]
        public async Task MissingCasterStatisticsRejectsBeforeCostsOrRolls()
        {
            TestRuntime runtime = CreateRuntimeCore(
                false,
                true,
                0,
                new[] { Target },
                20,
                0,
                false,
                1,
                4
            );

            OpResult<CastSpellOutcome> result = await runtime.Dispatcher.Dispatch(Cast());

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                ((InvalidOpResult<CastSpellOutcome>)result).Reason,
                Does.Contain("no authoritative statistics")
            );
            Assert.That(
                runtime.Store.Snapshot.ActionEconomy[Caster].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(runtime.Rolls.Remaining, Is.EqualTo(2));
            Assert.That(runtime.Store.Snapshot.Version, Is.Zero);
        }

        private static CastSpellActionOp Cast() =>
            new(
                new ActionInvocationId("hymn-primary"),
                Caster,
                Hymn,
                TwoActions,
                new SpellCastSelection(Placement, new[] { Target })
            );

        private static TestRuntime CreateRuntime(bool conditionImmune, params int[] rolls) =>
            CreateRuntime(conditionImmune, true, rolls);

        private static TestRuntime CreateRuntime(
            bool conditionImmune,
            bool defineConditionRules,
            params int[] rolls
        ) =>
            CreateRuntimeForTargets(
                conditionImmune,
                defineConditionRules,
                0,
                new[] { Target },
                rolls
            );

        private static TestRuntime CreateRuntimeForTargets(
            bool conditionImmune,
            bool defineConditionRules,
            int targetFortitude,
            IReadOnlyList<CreatureId> expectedTargets,
            params int[] rolls
        ) =>
            CreateRuntimeCore(
                conditionImmune,
                defineConditionRules,
                targetFortitude,
                expectedTargets,
                20,
                0,
                true,
                rolls
            );

        private static TestRuntime CreateRuntimeWithHealth(
            int targetCurrent,
            int targetTemporary,
            params int[] rolls
        ) =>
            CreateRuntimeCore(
                false,
                true,
                0,
                new[] { Target },
                targetCurrent,
                targetTemporary,
                true,
                rolls
            );

        private static TestRuntime CreateRuntimeCore(
            bool conditionImmune,
            bool defineConditionRules,
            int targetFortitude,
            IReadOnlyList<CreatureId> expectedTargets,
            int targetCurrent,
            int targetTemporary,
            bool seedCasterStatistics,
            params int[] rolls
        )
        {
            PreparedCreatureInputs targetInputs = conditionImmune
                ? InputsWithConditionImmunity("deafened")
                : PreparedCreatureInputs.Empty;
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Caster, new PlayerId("save-spell-players")))
                .SeedCreature(new CreatureState(Target, new PlayerId("save-spell-enemies")))
                .SeedCreature(new CreatureState(OtherTarget, new PlayerId("save-spell-enemies")))
                .SeedPreparedInputs(Caster, PreparedCreatureInputs.Empty)
                .SeedPreparedInputs(Target, targetInputs)
                .SeedPreparedInputs(OtherTarget, PreparedCreatureInputs.Empty)
                .SeedHealth(Caster, new HealthState(20, 20))
                .SeedHealth(
                    Target,
                    new HealthState(
                        targetCurrent,
                        20,
                        targetTemporary,
                        targetTemporary > 0
                            ? RuleSource.FromSlug("test-temporary-hit-points")
                            : default
                    )
                )
                .SeedHealth(OtherTarget, new HealthState(20, 20))
                .SeedActionEconomy(Caster, new ActionEconomyState(3, true))
                .SeedStatistics(
                    new CreatureStatisticsState(
                        Target,
                        0,
                        10,
                        targetFortitude,
                        0,
                        0,
                        new Dictionary<Skill, int>(),
                        Array.Empty<Modifier>()
                    )
                )
                .SeedStatistics(
                    new CreatureStatisticsState(
                        OtherTarget,
                        0,
                        10,
                        0,
                        0,
                        0,
                        new Dictionary<Skill, int>(),
                        Array.Empty<Modifier>()
                    )
                );
            if (seedCasterStatistics)
                seed.SeedStatistics(CreatureStatisticsState.Empty(Caster));
            InMemoryRulesStore store = new(seed);
            TestCatalog catalog = new();
            RuleRegistryBuilder registryBuilder = new();
            if (defineConditionRules)
                ConditionRuleDefinitions.DefineAll(registryBuilder);
            RuleRegistry registry = registryBuilder.Build();
            ScriptedRollService rollService = new(rolls);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store, rollService)
                .UseHealthRules()
                .UseCheckResolution()
                .UseActiveEffectRules(registry)
                .UseConditionRules(registry)
                .UseActionLifecycle(catalog)
                .UseSpellcastingRules(
                    catalog,
                    registry,
                    UnsupportedSpellAttackResolutionDataProvider.Instance,
                    new ExactAreaProvider(Placement, expectedTargets)
                )
                .Build();
            return new TestRuntime(store, dispatcher, rollService);
        }

        private static PreparedCreatureInputs InputsWithConditionImmunity(string condition) =>
            new(
                0,
                default,
                Array.Empty<KeyValuePair<string, int>>(),
                Array.Empty<string>(),
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<PreparedDefenseDescriptor>(),
                Array.Empty<PreparedDefenseDescriptor>(),
                new[] { new PreparedImmunityDescriptor(condition, PreparedImmunityKind.Condition) },
                Array.Empty<string>()
            );

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private sealed class TestRuntime
        {
            internal TestRuntime(
                InMemoryRulesStore store,
                RuleDispatcher dispatcher,
                ScriptedRollService rolls
            )
            {
                Store = store;
                Dispatcher = dispatcher;
                Rolls = rolls;
            }

            internal InMemoryRulesStore Store { get; }
            internal RuleDispatcher Dispatcher { get; }
            internal ScriptedRollService Rolls { get; }
        }

        private sealed class TestCatalog : ISpellActionCatalog
        {
            private readonly SpellDefinition definition = new(
                Hymn.Spell,
                "Haunting Hymn",
                1,
                new[] { TwoActions },
                new[] { Trait.FromSlug("cantrip"), Trait.FromSlug("sonic") },
                Array.Empty<SpellEffectDirective>(),
                Array.Empty<SpellAttackDefinition>(),
                new[]
                {
                    new SpellSaveDefinition(
                        SaveKind.Fortitude,
                        true,
                        new SpellAreaTarget(SpellAreaShape.Cone, 15),
                        new[]
                        {
                            new TypedDamageDice(new DiceExpression(1, 8), "sonic", "haunting-hymn"),
                        },
                        new[]
                        {
                            new SpellSaveConditionDirective(
                                ConditionRuleDefinitions.Deafened,
                                DegreeOfSuccess.CriticalFailure,
                                EffectDuration.OneMinute,
                                ConditionMarkerState.Instance
                            ),
                        }
                    ),
                }
            );
            private readonly ISpellBook book = new TestBook();

            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
                throw new KeyNotFoundException();

            public bool TryGetSpell(SpellReference reference, out SpellDefinition value)
            {
                value = reference == Hymn ? definition : null;
                return value != null;
            }

            public ISpellBook GetSpellBook(CreatureId creature) => book;
        }

        private sealed class TestBook : ISpellBook
        {
            public IReadOnlyList<SpellReference> CastableSpells => new[] { Hymn };
            public int SpellAttackModifier => 10;
            public int SpellDc => 20;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                Array.Empty<SpellSlotState>();

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            ) =>
                spell == Hymn
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("The Haunting Hymn slot is unavailable.");

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
                spell == Hymn
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("Haunting Hymn is not prepared.");
        }

        private sealed class ExactAreaProvider : ISpellSaveTargetingProvider
        {
            private readonly SpellAreaPlacement placement;
            private readonly IReadOnlyList<CreatureId> targets;

            internal ExactAreaProvider(
                SpellAreaPlacement placement,
                IEnumerable<CreatureId> targets
            )
            {
                this.placement = placement;
                this.targets = targets.ToArray();
            }

            public ActionValidationResult Validate(
                RulesSnapshot snapshot,
                CreatureId actor,
                SpellSaveDefinition save,
                SpellAreaPlacement proposedPlacement,
                IReadOnlyList<CreatureId> selectedCreatures
            ) =>
                proposedPlacement.Equals(placement) && selectedCreatures.SequenceEqual(targets)
                    ? ActionValidationResult.Valid
                    : ActionValidationResult.Invalid(
                        "The area placement or affected creature set is not authoritative."
                    );
        }

        private sealed class ThrowOnceConditionObserver : IFactObserver<ActiveEffectCreatedFact>
        {
            private readonly Exception failure;

            internal ThrowOnceConditionObserver(Exception failure) => this.failure = failure;

            internal int Calls { get; private set; }
            internal int ObservedHealth { get; private set; }
            internal int ObservedConditions { get; private set; }

            public ValueTask OnFactCommitted(
                ActiveEffectCreatedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                if (fact.DefinitionId != ConditionRuleDefinitions.Deafened)
                    return default;
                Calls++;
                ObservedHealth = currentSnapshot.Health[Target].Current;
                ObservedConditions = currentSnapshot.ActiveEffects.Count;
                if (Calls == 1)
                    throw failure;
                return default;
            }
        }
    }
}
