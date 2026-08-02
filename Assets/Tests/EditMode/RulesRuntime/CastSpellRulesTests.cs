using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class CastSpellRulesTests
    {
        private static readonly CreatureId Actor = new("spell-actor");
        private static readonly PlayerId Player = new("spell-player");
        private static readonly SpellReference Light = new(new SpellId("light"), 1);
        private static readonly SpellActionVariant TwoActions = new(2);
        private static readonly SpellSlotPoolId RankedPool = new("spell-actor:rank-1");
        private static readonly SpellSlotPoolId AlternateRankedPool = new(
            "spell-actor:alternate-rank-1"
        );
        private static readonly RuleDefinitionId EffectDefinition = new("spell-effect-light");
        private static readonly RuleDefinitionId InterruptionDefinition = new(
            "spell-test-interruption"
        );
        private static readonly RuleSource TestSource = RuleSource.FromSlug("spell-test");

        [Test]
        public void OperationFreezesDefinitionVariantTraitsAndCantripCost()
        {
            CastSpellActionOp operation = new(
                new ActionInvocationId("light-profile"),
                Actor,
                Light,
                TwoActions,
                SpellCastSelection.Empty
            );

            ActionProfile profile = operation.GetBaseProfile(new TestCatalog(new TestBook(true)));

            Assert.That(operation.Selection.Creatures, Is.Empty);
            Assert.That(profile.Cost, Is.EqualTo(ActionCost.Two));
            Assert.That(profile.AdditionalCosts, Is.Empty);
            Assert.That(
                profile.Traits.Select(trait => trait.Slug),
                Is.EqualTo(new[] { "cantrip", "concentrate", "light", "manipulate" })
            );
        }

        [Test]
        public async Task ValidCastSpendsTwoActionsAndCreatesProvenancedEffectExactlyOnce()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true));
            CountingObserver observer = new();
            dispatcher.RegisterResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>(observer);
            ActionInvocationId invocation = new("light-resolves");

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    invocation,
                    Actor,
                    Light,
                    TwoActions,
                    SpellCastSelection.Empty
                )
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>());
            CastSpellOutcome outcome = ((ResolvedOpResult<CastSpellOutcome>)result).Value;
            Assert.That(outcome.CreatedEffects, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            ActiveEffectInstance effect = store.Snapshot.ActiveEffects[
                outcome.CreatedEffects.Single()
            ];
            Assert.That(effect.Duration, Is.EqualTo(EffectDuration.Indefinite));
            Assert.That(effect.Source, Is.EqualTo(RuleSource.FromSlug("light")));
            Assert.That(effect.GetState<SpellEffectState>().Spell, Is.EqualTo(Light));
            Assert.That(effect.GetState<SpellEffectState>().Target, Is.EqualTo(Actor));
            Assert.That(result.Facts.OfType<ActiveEffectCreatedFact>().Count(), Is.EqualTo(1));
            Assert.That(observer.Calls, Is.EqualTo(1));
            Assert.That(
                dispatcher.Trace.OrderedFrames.Count(frame =>
                    frame.OpType == typeof(CommitPreparedSpellCastOp)
                ),
                Is.EqualTo(1)
            );

            long committedVersion = store.Snapshot.Version;
            OpResult<CastSpellOutcome> exactRetry = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    invocation,
                    Actor,
                    Light,
                    TwoActions,
                    SpellCastSelection.Empty
                )
            );

            Assert.That(exactRetry, Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>());
            Assert.That(((ResolvedOpResult<CastSpellOutcome>)exactRetry).Value, Is.SameAs(outcome));
            Assert.That(exactRetry.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(committedVersion));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.ActiveEffects, Has.Count.EqualTo(1));
            Assert.That(observer.Calls, Is.EqualTo(1));
        }

        [Test]
        public void CostCheckpointRetrySkipsProfileValidationAndDoubleSpend()
        {
            InMemoryRulesStore store = CreateStore(3, slotUses: 1);
            RuleRegistry registry = CreateRegistryBuilder().Build();
            TestCatalog catalog = new(new SlotTestBook());
            CountingProfileResolver resolver = new();
            CountingCastValidator validator = new();
            ThrowOnceCostsCommittedObserver failure = new();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .UseActiveEffectRules(registry)
                .UseActionLifecycle(catalog, resolver)
                .RegisterActionValidator<CastSpellActionOp>(validator)
                .UseSpellcastingRules(catalog, registry)
                .Build();
            dispatcher.RegisterFactObserver<ActionCostsCommittedFact>(failure);
            ActionInvocationId invocation = new("light-cost-checkpoint");
            CastSpellActionOp operation = new(
                invocation,
                Actor,
                Light,
                TwoActions,
                SpellCastSelection.Empty
            );

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(operation)
            );

            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.Zero);
            Assert.That(
                store.Snapshot.ActionReceipts[invocation],
                Is.TypeOf<CostsCommittedActionReceipt>()
            );
            Assert.That(resolver.Calls, Is.EqualTo(1));
            Assert.That(validator.Calls, Is.EqualTo(1));

            OpResult<CastSpellOutcome> conflict = dispatcher
                .Dispatch(
                    new CastSpellActionOp(
                        invocation,
                        Actor,
                        Light,
                        TwoActions,
                        new SpellCastSelection(new[] { Actor })
                    )
                )
                .GetAwaiter()
                .GetResult();

            Assert.That(conflict, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                ((InvalidOpResult<CastSpellOutcome>)conflict).Reason,
                Is.EqualTo(ActionReceiptReduction.ConflictingIntentReason)
            );
            Assert.That(resolver.Calls, Is.EqualTo(1));
            Assert.That(validator.Calls, Is.EqualTo(1));

            ResolvedOpResult<CastSpellOutcome> retry = RequireResolved(
                dispatcher.Dispatch(operation).GetAwaiter().GetResult()
            );

            Assert.That(retry.Value.CreatedEffects, Has.Count.EqualTo(1));
            Assert.That(retry.Facts.OfType<ActionCostsCommittedFact>(), Is.Empty);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.Zero);
            Assert.That(
                store.Snapshot.ActionReceipts[invocation],
                Is.TypeOf<ResolvedActionReceipt>()
            );
            Assert.That(resolver.Calls, Is.EqualTo(1));
            Assert.That(validator.Calls, Is.EqualTo(1));
            Assert.That(failure.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ResolvedObserverBatchClaimPreventsPartialReplay()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true));
            CountingObserver first = new();
            ThrowingObserver second = new();
            dispatcher.RegisterResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>(first);
            dispatcher.RegisterResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>(second);
            CastSpellActionOp operation = new(
                new ActionInvocationId("light-observer-claim"),
                Actor,
                Light,
                TwoActions,
                SpellCastSelection.Empty
            );

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(operation)
            );
            ResolvedOpResult<CastSpellOutcome> retry = RequireResolved(
                dispatcher.Dispatch(operation).GetAwaiter().GetResult()
            );

            Assert.That(retry.Facts, Is.Empty);
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(second.Calls, Is.EqualTo(1));
        }

        [Test]
        public void FinalFactFailureLeavesRootPresentationUnclaimedUntilRetry()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true));
            ThrowOnceReceiptObserver factObserver = new();
            CountingObserver presentation = new();
            dispatcher.RegisterFactObserver<ActionReceiptCommittedFact>(factObserver);
            dispatcher.RegisterResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>(
                presentation
            );
            CastSpellActionOp operation = new(
                new ActionInvocationId("light-final-fact-failure"),
                Actor,
                Light,
                TwoActions,
                SpellCastSelection.Empty
            );

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(operation)
            );
            Assert.That(presentation.Calls, Is.Zero);

            RequireResolved(dispatcher.Dispatch(operation).GetAwaiter().GetResult());
            RequireResolved(dispatcher.Dispatch(operation).GetAwaiter().GetResult());

            Assert.That(factObserver.Calls, Is.EqualTo(1));
            Assert.That(presentation.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task SelfTargetMetadataRejectsSelectedCreatureIdsBeforeCostsEffectsFactsOrRolls()
        {
            SpellCastSelection forgedSelection = new(new[] { new CreatureId("forged-target") });
            await AssertSelfTargetSelectionRejectsBeforeWork(
                "light-forged-target",
                forgedSelection,
                "cannot carry selected creatures"
            );
        }

        [Test]
        public async Task SelfTargetMetadataRejectsAreaPlacementBeforeCostsEffectsFactsOrRolls()
        {
            SpellAreaPlacement forgedPlacement = new(
                SpellAreaShape.Cone,
                new GridPosition(0, 0, 0),
                0,
                0,
                SpellAreaDirection.North
            );
            SpellCastSelection forgedSelection = new(forgedPlacement, Array.Empty<CreatureId>());

            await AssertSelfTargetSelectionRejectsBeforeWork(
                "light-forged-area",
                forgedSelection,
                "cannot carry an area placement"
            );
        }

        private static async Task AssertSelfTargetSelectionRejectsBeforeWork(
            string invocationId,
            SpellCastSelection forgedSelection,
            string expectedReason
        )
        {
            InMemoryRulesStore store = CreateStore(3);
            CountingRollService rolls = new();
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true), rolls);
            long initialVersion = store.Snapshot.Version;

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    new ActionInvocationId(invocationId),
                    Actor,
                    Light,
                    TwoActions,
                    forgedSelection
                )
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(
                ((InvalidOpResult<CastSpellOutcome>)result).Reason,
                Does.Contain(expectedReason)
            );
            Assert.That(store.Snapshot.Version, Is.EqualTo(initialVersion));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(store.Snapshot.ActionReceipts, Is.Empty);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(rolls.Calls, Is.Zero);
        }

        [Test]
        public async Task UnknownRankRejectsBeforeAnyCostAndOperationCannotForgeResource()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, new TestBook(true));

            OpResult<CastSpellOutcome> wrongRank = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    new ActionInvocationId("light-wrong-rank"),
                    Actor,
                    new SpellReference(new SpellId("light"), 2),
                    TwoActions,
                    SpellCastSelection.Empty
                )
            );
            Assert.That(wrongRank, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(wrongRank.Facts, Is.Empty);
            Assert.That(typeof(CastSpellActionOp).GetProperty("Authorization"), Is.Null);
            Assert.That(typeof(CastSpellActionOp).GetProperty("SlotPool"), Is.Null);
        }

        [Test]
        public async Task InsufficientActionsOrUnpreparedSpellRejectAtomically()
        {
            InMemoryRulesStore shortStore = CreateStore(1);
            RuleDispatcher shortDispatcher = CreateDispatcher(shortStore, new TestBook(true));
            InMemoryRulesStore unpreparedStore = CreateStore(3);
            RuleDispatcher unpreparedDispatcher = CreateDispatcher(
                unpreparedStore,
                new TestBook(false)
            );

            OpResult<CastSpellOutcome> shortResult = await shortDispatcher.Dispatch(Cast());
            OpResult<CastSpellOutcome> unprepared = await unpreparedDispatcher.Dispatch(Cast());

            Assert.That(shortResult, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(unprepared, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(shortStore.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(
                unpreparedStore.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(shortResult.Facts, Is.Empty);
            Assert.That(unprepared.Facts, Is.Empty);
        }

        [Test]
        public async Task RankedCastAtomicallySpendsDefinitionBoundSlotAndActions()
        {
            InMemoryRulesStore store = CreateStore(3, slotUses: 1);
            RuleDispatcher dispatcher = CreateDispatcher(store, new SlotTestBook());

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(
                new CastSpellActionOp(
                    new ActionInvocationId("light-ranked"),
                    Actor,
                    Light,
                    TwoActions,
                    SpellCastSelection.Empty
                )
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.Zero);
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<SpellSlotSpentFact>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task MismatchedBoundAndAuthorizedSlotsRejectBeforeAnyCostsOrEffects()
        {
            InMemoryRulesStore store = CreateStore(3, slotUses: 1, alternateSlotUses: 1);
            RuleDispatcher dispatcher = CreateDispatcher(store, new MismatchedSlotTestBook());

            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(Cast());

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.SpellSlots[AlternateRankedPool].Remaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public async Task InterruptedCastRetainsCommittedCostButCreatesNoEffect()
        {
            InterruptingActionMiddleware middleware = new();
            RuleRegistryBuilder registryBuilder = CreateRegistryBuilder();
            registryBuilder
                .Define(InterruptionDefinition)
                .Middleware(RuleLifecyclePhase.Reaction, middleware);
            InMemoryRulesStore store = CreateStore(
                3,
                new ActiveRuleBinding(
                    new BindingId("spell-test-interruption-binding"),
                    InterruptionDefinition,
                    Actor,
                    default,
                    TestSource,
                    0
                ),
                slotUses: 1
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                new SlotTestBook(),
                registryBuilder.Build()
            );

            ActionInvocationId invocation = new("light-interrupted");
            CastSpellActionOp operation = new(
                invocation,
                Actor,
                Light,
                TwoActions,
                SpellCastSelection.Empty
            );
            OpResult<CastSpellOutcome> result = await dispatcher.Dispatch(operation);

            Assert.That(result, Is.TypeOf<InterruptedOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.ActiveEffects.Count, Is.Zero);
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.Zero);
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(result.Facts.OfType<SpellSlotSpentFact>().Count(), Is.EqualTo(1));
            ActionCostsCommittedFact costsCommitted = result
                .Facts.OfType<ActionCostsCommittedFact>()
                .Single();
            Assert.That(costsCommitted.Actor, Is.EqualTo(Actor));
            Assert.That(
                costsCommitted.DefinitionId,
                Is.EqualTo(CastSpellActionDefinition.DefinitionId)
            );
            ActionInterruptedFact interrupted = result
                .Facts.OfType<ActionInterruptedFact>()
                .Single();
            Assert.That(interrupted.Actor, Is.EqualTo(Actor));
            Assert.That(
                interrupted.DefinitionId,
                Is.EqualTo(CastSpellActionDefinition.DefinitionId)
            );
            Assert.That(result.Facts.OfType<ActionReceiptCommittedFact>(), Is.Empty);
            Assert.That(middleware.Calls, Is.EqualTo(1));
            Assert.That(
                store.Snapshot.ActionReceipts[invocation],
                Is.TypeOf<InterruptedActionReceipt>()
            );
            long interruptedVersion = store.Snapshot.Version;

            OpResult<CastSpellOutcome> retry = await dispatcher.Dispatch(operation);

            Assert.That(retry, Is.TypeOf<InterruptedOpResult<CastSpellOutcome>>());
            Assert.That(retry.Facts, Is.Empty);
            Assert.That(store.Snapshot.Version, Is.EqualTo(interruptedVersion));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.SpellSlots[RankedPool].Remaining, Is.Zero);
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(middleware.Calls, Is.EqualTo(1));
            Assert.That(
                dispatcher.Trace.OrderedFrames.Count(frame =>
                    frame.OpType == typeof(CommitPreparedSpellCastOp)
                ),
                Is.Zero
            );
        }

        private static CastSpellActionOp Cast() =>
            new(
                new ActionInvocationId($"test-light-{Guid.NewGuid():N}"),
                Actor,
                Light,
                TwoActions,
                SpellCastSelection.Empty
            );

        private static InMemoryRulesStore CreateStore(
            int actions,
            ActiveRuleBinding binding = null,
            int? slotUses = null,
            int? alternateSlotUses = null
        )
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Player))
                .SeedHealth(Actor, new HealthState(10, 10))
                .SeedActionEconomy(Actor, new ActionEconomyState(actions, true))
                .SeedStatistics(CreatureStatisticsState.Empty(Actor));
            if (binding != null)
                seed.SeedRuleBinding(binding);
            if (slotUses.HasValue)
                seed.SeedSpellSlot(new SpellSlotState(RankedPool, Actor, slotUses.Value, 1));
            if (alternateSlotUses.HasValue)
                seed.SeedSpellSlot(
                    new SpellSlotState(AlternateRankedPool, Actor, alternateSlotUses.Value, 1)
                );
            return new InMemoryRulesStore(seed);
        }

        private static RuleRegistryBuilder CreateRegistryBuilder()
        {
            RuleRegistryBuilder builder = new();
            builder.Define(EffectDefinition);
            return builder;
        }

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            ISpellBook book,
            RuleRegistry registry = null
        )
        {
            RuleRegistry effectiveRegistry = registry ?? CreateRegistryBuilder().Build();
            TestCatalog catalog = new(book);
            return new RuleDispatcherBuilder(store)
                .UseActiveEffectRules(effectiveRegistry)
                .UseActionLifecycle(catalog)
                .UseSpellcastingRules(catalog, effectiveRegistry)
                .Build();
        }

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            ISpellBook book,
            IRollService rolls
        )
        {
            RuleRegistry registry = CreateRegistryBuilder().Build();
            TestCatalog catalog = new(book);
            return new RuleDispatcherBuilder(store, rolls)
                .UseActiveEffectRules(registry)
                .UseActionLifecycle(catalog)
                .UseSpellcastingRules(catalog, registry)
                .Build();
        }

        [Test]
        public async Task InvalidDefinitionVariantRejectsWithoutActionsOrEffects()
        {
            InMemoryRulesStore store = CreateStore(3);
            TestBook book = new(true);
            TestCatalog catalog = new(book);
            CastSpellActionDefinition definition = new(catalog);
            SpellActionVariant invalidVariant = new(1);

            Assert.That(
                definition.GetAvailability(store.Snapshot, Actor, Light, invalidVariant),
                Is.TypeOf<UnavailableActionAvailability>()
            );

            OpResult<CastSpellOutcome> result = await CreateDispatcher(store, book)
                .Dispatch(
                    definition.CreateOp(
                        new ActionInvocationId("light-invalid-variant"),
                        Actor,
                        Light,
                        invalidVariant,
                        SpellCastSelection.Empty
                    )
                );

            Assert.That(result, Is.TypeOf<InvalidOpResult<CastSpellOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(store.Snapshot.ActiveEffects, Is.Empty);
            Assert.That(result.Facts, Is.Empty);
        }

        private sealed class TestCatalog : ISpellActionCatalog
        {
            private readonly ISpellBook book;
            private readonly SpellDefinition definition = new(
                new SpellId("light"),
                "Light",
                1,
                new[] { TwoActions },
                new[]
                {
                    Trait.FromSlug("cantrip"),
                    Trait.FromSlug("concentrate"),
                    Trait.FromSlug("light"),
                    Trait.FromSlug("manipulate"),
                },
                new[]
                {
                    new SpellEffectDirective(EffectDefinition, EffectDuration.Indefinite, "self"),
                },
                Array.Empty<SpellAttackDefinition>(),
                Array.Empty<SpellSaveDefinition>()
            );

            public TestCatalog(ISpellBook book) => this.book = book;

            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
                throw new KeyNotFoundException();

            public bool TryGetSpell(SpellReference reference, out SpellDefinition value)
            {
                if (reference.Spell == Light.Spell && reference.Rank >= 1)
                {
                    value = definition;
                    return true;
                }
                value = null;
                return false;
            }

            public ISpellBook GetSpellBook(CreatureId creature) => book;
        }

        private sealed class TestBook : ISpellBook
        {
            private readonly bool prepared;

            public TestBook(bool prepared) => this.prepared = prepared;

            public IReadOnlyList<SpellReference> CastableSpells =>
                prepared ? new[] { Light } : Array.Empty<SpellReference>();
            public int SpellAttackModifier => 0;
            public int SpellDc => 10;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                Array.Empty<SpellSlotState>();

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            ) =>
                prepared && spell == Light
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("The exact spell is not prepared.");

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
                prepared && spell == Light
                    ? SpellCastAuthorization.Cantrip
                    : SpellCastAuthorization.Unavailable("The exact spell is not prepared.");
        }

        private sealed class SlotTestBook : ISpellBook
        {
            public IReadOnlyList<SpellReference> CastableSpells => new[] { Light };
            public int SpellAttackModifier => 0;
            public int SpellDc => 10;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                new[] { new SpellSlotState(RankedPool, owner, 1, 1) };

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            ) =>
                spell == Light
                && slots.TryGet(RankedPool, out SpellSlotState state)
                && state.Owner == owner
                && state.Remaining > 0
                    ? SpellCastAuthorization.FromPool(RankedPool)
                    : SpellCastAuthorization.Unavailable("The ranked slot is unavailable.");

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
                spell == Light
                    ? SpellCastAuthorization.FromPool(RankedPool)
                    : SpellCastAuthorization.Unavailable("The ranked slot is unavailable.");
        }

        private sealed class MismatchedSlotTestBook : ISpellBook
        {
            public IReadOnlyList<SpellReference> CastableSpells => new[] { Light };
            public int SpellAttackModifier => 0;
            public int SpellDc => 10;

            public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
                new[]
                {
                    new SpellSlotState(RankedPool, owner, 1, 1),
                    new SpellSlotState(AlternateRankedPool, owner, 1, 1),
                };

            public SpellCastAuthorization Authorize(
                CreatureId owner,
                SpellReference spell,
                ISpellSlotStateReader slots
            ) =>
                spell == Light
                && slots.TryGet(AlternateRankedPool, out SpellSlotState state)
                && state.Owner == owner
                && state.Remaining > 0
                    ? SpellCastAuthorization.FromPool(AlternateRankedPool)
                    : SpellCastAuthorization.Unavailable("The alternate slot is unavailable.");

            public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
                spell == Light
                    ? SpellCastAuthorization.FromPool(RankedPool)
                    : SpellCastAuthorization.Unavailable("The ranked slot is unavailable.");
        }

        private sealed class CountingObserver
            : IResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>
        {
            public int Calls { get; private set; }

            public ValueTask OnOperationResolved(
                CastSpellActionOp operation,
                CastSpellOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                Calls++;
                return default;
            }
        }

        private sealed class ThrowingObserver
            : IResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>
        {
            public int Calls { get; private set; }

            public ValueTask OnOperationResolved(
                CastSpellActionOp operation,
                CastSpellOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                Calls++;
                throw new InvalidOperationException("injected resolved observer failure");
            }
        }

        private sealed class CountingProfileResolver : IActionProfileResolver
        {
            public int Calls { get; private set; }

            public ActionProfile Resolve(
                ActionOpInfo action,
                ActionProfile baseProfile,
                RulesSnapshot snapshot
            )
            {
                Calls++;
                return baseProfile;
            }
        }

        private sealed class CountingCastValidator : IActionValidator<CastSpellActionOp>
        {
            public int Calls { get; private set; }

            public ActionValidationResult Validate(
                OpFrame<CastSpellActionOp> frame,
                RulesSnapshot snapshot
            )
            {
                Calls++;
                return ActionValidationResult.Valid;
            }
        }

        private sealed class ThrowOnceCostsCommittedObserver
            : IFactObserver<ActionCostsCommittedFact>
        {
            public int Calls { get; private set; }

            public ValueTask OnFactCommitted(
                ActionCostsCommittedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                Calls++;
                if (Calls == 1)
                    throw new InvalidOperationException("injected cost checkpoint failure");
                return default;
            }
        }

        private sealed class ThrowOnceReceiptObserver : IFactObserver<ActionReceiptCommittedFact>
        {
            public int Calls { get; private set; }

            public ValueTask OnFactCommitted(
                ActionReceiptCommittedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                Calls++;
                if (Calls == 1)
                    throw new InvalidOperationException("injected final receipt observer failure");
                return default;
            }
        }

        private sealed class CountingRollService : IRollService
        {
            public int Calls { get; private set; }

            public RollResult Roll(DiceExpression dice)
            {
                Calls++;
                return new RollResult(dice, Enumerable.Repeat(1, dice.Count));
            }
        }

        private sealed class InterruptingActionMiddleware
            : IOpMiddleware<ActionBegunOp, ActionStartOutcome>
        {
            public int Calls { get; private set; }

            public ValueTask<OpResult<ActionStartOutcome>> Invoke(
                OpFrame<ActionBegunOp> frame,
                OpMiddlewareContext context,
                OpNext<ActionStartOutcome> next
            )
            {
                Calls++;
                return new ValueTask<OpResult<ActionStartOutcome>>(
                    OpResult<ActionStartOutcome>.Resolved(ActionStartOutcome.Interrupted)
                );
            }
        }
    }
}
