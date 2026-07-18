using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies the engine-owned ActionOp lifecycle, frozen profiles, and atomic resource costs.
    /// </summary>
    public sealed class ActionLifecycleTests
    {
        private static readonly CreatureId Actor = new CreatureId("action-actor");
        private static readonly ActionDefinitionId ActionDefinition =
            new ActionDefinitionId("test-action");
        private static readonly SpellSlotPoolId SpellPool =
            new SpellSlotPoolId("rank-one-prepared");
        private static readonly ItemId Ammunition = new ItemId("sling-bullets");
        private static readonly BindingId Binding = new BindingId("test-binding");
        private static readonly RuleDefinitionId BindingDefinition =
            new RuleDefinitionId("test-binding-definition");
        private static readonly RuleSource Source = RuleSource.FromSlug("action-test");

        [Test]
        public async Task ValidActionCommitsEveryCostBeforeHandlerAndEmitsDeterministicFacts()
        {
            ActionProfile profile = new ActionProfile(
                ActionCost.One,
                new RuleCost[]
                {
                    RuleCost.SpellSlot(SpellPool),
                    RuleCost.FocusPoints(),
                    RuleCost.Ammunition(Ammunition, 2),
                    RuleCost.OncePerRound(Binding)
                },
                new[] { Trait.FromSlug("manipulate"), Trait.FromSlug("concentrate") });
            InMemoryRulesStore store = CreateFullySeededStore();
            RecordingActionHandler handler = new RecordingActionHandler(false);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                    store,
                    new SequentialOpIdProvider(10))
                .RegisterHandler<TestActionOp, TestActionOutcome>(handler)
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .UseRuleRegistry(CreateRuleRegistry())
                .Build();

            OpResult<TestActionOutcome> result =
                await dispatcher.Dispatch(new TestActionOp());

            ResolvedOpResult<TestActionOutcome> resolved = RequireResolved(result);
            Assert.That(resolved.Value.DomainSucceeded, Is.False,
                "A legal feature-level failure remains a resolved action outcome.");
            Assert.That(handler.WasCalled, Is.True);
            Assert.That(handler.Profile, Is.SameAs(profile));
            Assert.That(handler.ActionsRemaining, Is.EqualTo(2));
            Assert.That(handler.SpellSlotsRemaining, Is.EqualTo(1));
            Assert.That(handler.FocusPointsRemaining, Is.EqualTo(1));
            Assert.That(handler.AmmunitionRemaining, Is.EqualTo(3));
            Assert.That(handler.FrequencyUses, Is.EqualTo(1));

            Assert.That(result.Facts.Select(fact => fact.GetType()), Is.EqualTo(new[]
            {
                typeof(ActionCostSpentFact),
                typeof(SpellSlotSpentFact),
                typeof(FocusPointsSpentFact),
                typeof(AmmunitionSpentFact),
                typeof(BindingFrequencySpentFact)
            }));
            Assert.That(result.Facts.Select(fact => fact.Id), Is.EqualTo(new[]
            {
                new FactId(1),
                new FactId(2),
                new FactId(3),
                new FactId(4),
                new FactId(5)
            }));
            Assert.That(result.Facts.All(fact => fact.SourceOpId == new OpId(11)), Is.True);
            Assert.That(result.Facts.All(fact => fact.RootOpId == new OpId(10)), Is.True);
            Assert.That(store.Snapshot.Version, Is.EqualTo(1),
                "All resources must commit in one reducer transaction.");

            OpFrame<TestActionOp> action = dispatcher.Trace.Get<TestActionOp>(new OpId(10));
            Assert.That(action.IsAction, Is.True);
            Assert.That(action.ActionProfile, Is.SameAs(profile));
            Assert.That(action.ActionInfo.Actor, Is.EqualTo(Actor));
            Assert.That(action.ActionInfo.DefinitionId, Is.EqualTo(ActionDefinition));
            Assert.That(dispatcher.Trace.GetAction(action.Id), Is.SameAs(action.ActionInfo));
            Assert.That(dispatcher.Trace.GetActionProfile(action.Id), Is.SameAs(profile));
            Assert.That(dispatcher.Trace.Get<CommitActionCostsOp>(new OpId(11)).ParentId,
                Is.EqualTo(action.Id));
            Assert.That(dispatcher.Trace.Get<ActionBegunOp>(new OpId(12)).ParentId,
                Is.EqualTo(action.Id));
            Assert.That(dispatcher.Diagnostics.Compact,
                Does.Contain("profile: 1 action(s); 4 additional cost(s); concentrate,manipulate"));
        }

        [Test]
        public async Task InvalidActionStopsBeforeCostsLifecycleMiddlewareAndFacts()
        {
            ActionProfile profile = ActionProfile.OneAction(
                new[] { Trait.FromSlug("attack") });
            InMemoryRulesStore store = CreateFullySeededStore();
            RecordingActionHandler handler = new RecordingActionHandler(true);
            RejectingValidator validator = new RejectingValidator("target is not legal");
            ActionBegunMiddleware middleware = new ActionBegunMiddleware(false);
            CountingActionCostListener listener = new CountingActionCostListener();
            RuleRegistry registry = CreateRuleRegistry(middleware, listener);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionOutcome>(handler)
                .RegisterActionValidator<TestActionOp>(validator)
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .UseRuleRegistry(registry)
                .Build();

            OpResult<TestActionOutcome> result =
                await dispatcher.Dispatch(new TestActionOp());

            Assert.That(result, Is.TypeOf<InvalidOpResult<TestActionOutcome>>());
            Assert.That(((InvalidOpResult<TestActionOutcome>)result).Reason,
                Is.EqualTo("target is not legal"));
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(handler.WasCalled, Is.False);
            Assert.That(middleware.Calls, Is.Zero);
            Assert.That(listener.Calls, Is.Zero);
            Assert.That(dispatcher.Trace.OrderedFrames.Count(), Is.EqualTo(1));
            Assert.That(dispatcher.Trace.OrderedFrames.Single().OpType, Is.EqualTo(typeof(TestActionOp)));
        }

        [Test]
        public async Task FailedLateCostRecheckRollsBackEarlierDraftChangesAtomically()
        {
            ActionProfile profile = new ActionProfile(
                ActionCost.One,
                new RuleCost[] { RuleCost.SpellSlot(SpellPool, 2) },
                Array.Empty<Trait>());
            InMemoryRulesStore store = new InMemoryRulesStore(new RulesStateSeed()
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true))
                .SeedSpellSlot(new SpellSlotState(SpellPool, Actor, 1, 1)));
            RecordingActionHandler handler = new RecordingActionHandler(true);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionOutcome>(handler)
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .Build();

            OpResult<TestActionOutcome> result =
                await dispatcher.Dispatch(new TestActionOp());

            Assert.That(result, Is.TypeOf<InvalidOpResult<TestActionOutcome>>());
            Assert.That(((InvalidOpResult<TestActionOutcome>)result).Reason,
                Does.Contain("insufficient uses"));
            Assert.That(result.Facts, Is.Empty);
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3),
                "An earlier draft action spend must roll back with the later slot rejection.");
            Assert.That(store.Snapshot.SpellSlots[SpellPool].Remaining, Is.EqualTo(1));
            Assert.That(store.Snapshot.Version, Is.Zero);
            Assert.That(handler.WasCalled, Is.False);
            Assert.That(dispatcher.Trace.OrderedFrames.Select(frame => frame.OpType), Is.EqualTo(new[]
            {
                typeof(TestActionOp),
                typeof(CommitActionCostsOp)
            }));
        }

        [Test]
        public void CostCommitRejectsOrdinaryMiddlewareConfiguration()
        {
            ActionProfile profile = ActionProfile.OneAction(Array.Empty<Trait>());
            ShortCircuitingCostMiddleware middleware = new ShortCircuitingCostMiddleware();
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry.Define(BindingDefinition).Middleware(
                RuleLifecyclePhase.Transformation,
                middleware);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                new RuleDispatcherBuilder(CreateFullySeededStore())
                .RegisterHandler<TestActionOp, TestActionOutcome>(
                    new RecordingActionHandler(true))
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .UseRuleRegistry(registry.Build())
                .Build());

            Assert.That(error.Message, Does.Contain("not allowed by its resolver registration"));
            Assert.That(middleware.Calls, Is.Zero,
                "Invalid middleware configuration must fail before a rule can execute.");
        }

        [Test]
        public async Task ActionBegunInterruptionPreservesCostsAndSkipsFeatureHandler()
        {
            ActionProfile profile = ActionProfile.OneAction(Array.Empty<Trait>());
            InMemoryRulesStore store = CreateFullySeededStore();
            RecordingActionHandler handler = new RecordingActionHandler(true);
            ActionBegunMiddleware middleware = new ActionBegunMiddleware(true);
            CountingActionCostListener listener = new CountingActionCostListener();
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionOutcome>(handler)
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .UseRuleRegistry(CreateRuleRegistry(middleware, listener))
                .Build();

            OpResult<TestActionOutcome> result =
                await dispatcher.Dispatch(new TestActionOp());

            Assert.That(result, Is.TypeOf<InterruptedOpResult<TestActionOutcome>>());
            Assert.That(result.Facts, Has.Count.EqualTo(1));
            Assert.That(result.Facts.Single(), Is.TypeOf<ActionCostSpentFact>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
            Assert.That(store.Snapshot.Version, Is.EqualTo(1));
            Assert.That(handler.WasCalled, Is.False);
            Assert.That(middleware.Calls, Is.EqualTo(1));
            Assert.That(listener.Calls, Is.EqualTo(1),
                "Post-commit listeners still observe durable costs after disruption.");
        }

        [Test]
        public async Task FeatureLevelFailureRemainsResolved()
        {
            ActionProfile profile = ActionProfile.Create(
                ActionCost.FreeAction,
                Array.Empty<Trait>());
            RecordingActionHandler handler = new RecordingActionHandler(false);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateFullySeededStore())
                .RegisterHandler<TestActionOp, TestActionOutcome>(handler)
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .UseRuleRegistry(CreateRuleRegistry())
                .Build();

            OpResult<TestActionOutcome> result =
                await dispatcher.Dispatch(new TestActionOp());

            Assert.That(result, Is.TypeOf<ResolvedOpResult<TestActionOutcome>>());
            Assert.That(((ResolvedOpResult<TestActionOutcome>)result).Value.DomainSucceeded, Is.False);
            Assert.That(result.Facts, Is.Empty);
            Assert.That(handler.WasCalled, Is.True);
            Assert.That(handler.ActionsRemaining, Is.EqualTo(3));
        }

        [Test]
        public async Task EffectiveProfileIsResolvedOnceAndFrozenAcrossCostStateChanges()
        {
            ActionProfile baseProfile = ActionProfile.Create(
                ActionCost.Two,
                Array.Empty<Trait>());
            ActionProfile effectiveProfile = ActionProfile.OneAction(
                new[] { Trait.FromSlug("flourish") });
            CountingProfileResolver resolver = new CountingProfileResolver(effectiveProfile);
            CapturingValidator validator = new CapturingValidator();
            RecordingActionHandler handler = new RecordingActionHandler(true);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateFullySeededStore())
                .RegisterHandler<TestActionOp, TestActionOutcome>(handler)
                .RegisterActionValidator<TestActionOp>(validator)
                .UseActionLifecycle(new FixedActionCatalog(baseProfile), resolver)
                .UseRuleRegistry(CreateRuleRegistry())
                .Build();

            await dispatcher.Dispatch(new TestActionOp());

            Assert.That(resolver.Calls, Is.EqualTo(1));
            Assert.That(resolver.StartingActions, Is.EqualTo(3));
            Assert.That(validator.Profile, Is.SameAs(effectiveProfile));
            Assert.That(handler.Profile, Is.SameAs(effectiveProfile));
            Assert.That(handler.ActionsRemaining, Is.EqualTo(2));
        }

        [Test]
        public async Task ProfileResolutionRunsOutsideTheDispatcherGate()
        {
            object observedGate = new object();
            LockObservingProfileResolver resolver = new LockObservingProfileResolver(
                () => Monitor.IsEntered(observedGate));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateFullySeededStore())
                .RegisterHandler<TestActionOp, TestActionOutcome>(
                    new RecordingActionHandler(true))
                .UseActionLifecycle(
                    new FixedActionCatalog(ActionProfile.OneAction(Array.Empty<Trait>())),
                    resolver)
                .UseRuleRegistry(CreateRuleRegistry())
                .Build();
            observedGate = typeof(RuleDispatcher)
                .GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(dispatcher) ??
                throw new InvalidOperationException("The dispatcher gate could not be inspected.");

            OpResult<TestActionOutcome> result =
                await dispatcher.Dispatch(new TestActionOp());

            Assert.That(result, Is.TypeOf<ResolvedOpResult<TestActionOutcome>>());
            Assert.That(resolver.WasDispatcherGateHeld, Is.False);
        }

        [Test]
        public async Task ActionEconomySupportsNoneOneToThreeReactionAndFreeAction()
        {
            Assert.That(ActionCost.None.Amount, Is.Zero);
            Assert.That(ActionCost.One.Amount, Is.EqualTo(1));
            Assert.That(ActionCost.Two.Amount, Is.EqualTo(2));
            Assert.That(ActionCost.Three.Amount, Is.EqualTo(3));
            Assert.That(ActionCost.Reaction.Amount, Is.EqualTo(1));
            Assert.That(ActionCost.FreeAction.Amount, Is.EqualTo(1));

            ActionCostExpectation[] cases =
            {
                new ActionCostExpectation(ActionCost.None, 3, true, false),
                new ActionCostExpectation(ActionCost.One, 2, true, true),
                new ActionCostExpectation(ActionCost.Two, 1, true, true),
                new ActionCostExpectation(ActionCost.Three, 0, true, true),
                new ActionCostExpectation(ActionCost.Reaction, 3, false, true),
                new ActionCostExpectation(ActionCost.FreeAction, 3, true, false)
            };

            foreach (ActionCostExpectation expectation in cases)
            {
                InMemoryRulesStore store = new InMemoryRulesStore(new RulesStateSeed()
                    .SeedActionEconomy(Actor, new ActionEconomyState(3, true)));
                ActionProfile profile = ActionProfile.Create(
                    expectation.Cost,
                    Array.Empty<Trait>());
                RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                    .RegisterHandler<TestActionOp, TestActionOutcome>(
                        new RecordingActionHandler(true))
                    .UseActionLifecycle(new FixedActionCatalog(profile))
                    .Build();

                OpResult<TestActionOutcome> result =
                    await dispatcher.Dispatch(new TestActionOp());

                Assert.That(result, Is.TypeOf<ResolvedOpResult<TestActionOutcome>>(),
                    $"{expectation.Cost.Kind} should resolve.");
                Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                    Is.EqualTo(expectation.ActionsRemaining));
                Assert.That(store.Snapshot.ActionEconomy[Actor].ReactionAvailable,
                    Is.EqualTo(expectation.ReactionAvailable));
                Assert.That(result.Facts.OfType<ActionCostSpentFact>().Any(),
                    Is.EqualTo(expectation.EmitsFact));
            }
        }

        [Test]
        public async Task StepStyleProfileStillOpensIdentityWindowWithoutReactionEligibility()
        {
            ActionProfile profile = ActionProfile.OneAction(
                new[] { Trait.FromSlug("move") },
                canTriggerReactions: false);
            ActionBegunMiddleware observer = new ActionBegunMiddleware(false);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateFullySeededStore())
                .RegisterHandler<TestActionOp, TestActionOutcome>(
                    new RecordingActionHandler(true))
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .UseRuleRegistry(CreateRuleRegistry(observer))
                .Build();

            OpResult<TestActionOutcome> result =
                await dispatcher.Dispatch(new TestActionOp());

            Assert.That(result, Is.TypeOf<ResolvedOpResult<TestActionOutcome>>());
            Assert.That(observer.Calls, Is.EqualTo(1));
            Assert.That(observer.CanTriggerReactions, Is.False);
            Assert.That(observer.WouldPromptReaction, Is.False);
            Assert.That(observer.ObservedActionId, Is.EqualTo(new OpId(1)));
        }

        [Test]
        public void LifecycleOperationsAreNestedOnlyAndEngineConstructed()
        {
            ActionProfile profile = ActionProfile.OneAction(Array.Empty<Trait>());
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateFullySeededStore())
                .RegisterHandler<TestActionOp, TestActionOutcome>(
                    new RecordingActionHandler(true))
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .UseRuleRegistry(CreateRuleRegistry())
                .Build();

            InvalidOperationException begunError = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new ActionBegunOp(new OpId(500))));
            InvalidOperationException costsError = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new CommitActionCostsOp(new OpId(500), Actor, profile)));

            Assert.That(begunError.Message, Does.Contain("nested-only"));
            Assert.That(costsError.Message, Does.Contain("nested-only"));
            Assert.That(typeof(ActionBegunOp).GetConstructors(), Is.Empty);
            Assert.That(typeof(CommitActionCostsOp).GetConstructors(), Is.Empty);
            Assert.That(dispatcher.Trace.OrderedFrames, Is.Empty);
        }

        [Test]
        public void BuilderRequiresCompleteActionLifecycleConfiguration()
        {
            RecordingActionHandler handler = new RecordingActionHandler(true);
            Assert.Throws<InvalidOperationException>(() => new RuleDispatcherBuilder(
                    CreateFullySeededStore())
                .RegisterHandler<TestActionOp, TestActionOutcome>(handler)
                .Build());

            Assert.Throws<InvalidOperationException>(() => new RuleDispatcherBuilder(
                    CreateFullySeededStore())
                .RegisterActionValidator<TestActionOp>(new CapturingValidator())
                .UseActionLifecycle(new FixedActionCatalog(
                    ActionProfile.OneAction(Array.Empty<Trait>())))
                .Build());

            RuleDispatcherBuilder duplicateConfiguration = new RuleDispatcherBuilder(
                    CreateFullySeededStore())
                .UseActionLifecycle(new FixedActionCatalog(
                    ActionProfile.OneAction(Array.Empty<Trait>())));
            Assert.Throws<InvalidOperationException>(() => duplicateConfiguration
                .UseActionLifecycle(new FixedActionCatalog(
                    ActionProfile.OneAction(Array.Empty<Trait>()))));
        }

        [Test]
        public void ProfilesAndCostsCopyCallerCollectionsAndRejectInvalidValues()
        {
            List<RuleCost> costs = new List<RuleCost> { RuleCost.FocusPoints() };
            List<Trait> traits = new List<Trait> { Trait.FromSlug("manipulate") };
            ActionProfile profile = new ActionProfile(ActionCost.One, costs, traits);

            costs.Add(RuleCost.FocusPoints());
            traits.Add(Trait.FromSlug("attack"));

            Assert.That(profile.AdditionalCosts, Has.Count.EqualTo(1));
            Assert.That(profile.Traits.Select(trait => trait.Slug),
                Is.EqualTo(new[] { "manipulate" }));
            Assert.That(profile.HasTrait(Trait.FromSlug("manipulate")), Is.True);
            Assert.That(profile.HasTrait(Trait.FromSlug("attack")), Is.False);
            Assert.That(ActionValidationResult.Valid,
                Is.SameAs(ActionValidationResult.Valid));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionCost.FromActions(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionCost.FromActions(4));
            Assert.Throws<ArgumentException>(() => RuleCost.SpellSlot(default));
            Assert.Throws<ArgumentOutOfRangeException>(() => RuleCost.FocusPoints(0));
            Assert.Throws<ArgumentException>(() => RuleCost.Ammunition(default));
            Assert.Throws<ArgumentException>(() => RuleCost.OncePerRound(default));
            Assert.Throws<ArgumentException>(() => new ActionProfile(
                ActionCost.One,
                new RuleCost[] { null },
                Array.Empty<Trait>()));
        }

        [Test]
        public void FeatureMiddlewareCannotRewriteABegunActionAsInvalid()
        {
            ActionProfile profile = ActionProfile.OneAction(Array.Empty<Trait>());
            InvalidatingActionMiddleware middleware = new InvalidatingActionMiddleware();
            CountingActionCostListener listener = new CountingActionCostListener();
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry.Define(BindingDefinition)
                .Middleware(RuleLifecyclePhase.Transformation, middleware)
                .FactListener(RuleLifecyclePhase.Observation, listener);
            InMemoryRulesStore store = CreateFullySeededStore();
            RecordingActionHandler handler = new RecordingActionHandler(true);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
                .RegisterHandler<TestActionOp, TestActionOutcome>(handler)
                .UseActionLifecycle(new FixedActionCatalog(profile))
                .UseRuleRegistry(registry.Build())
                .Build();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await dispatcher.Dispatch(new TestActionOp()));

            Assert.That(error.Message, Does.Contain("cannot replace a begun action"));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
            Assert.That(handler.WasCalled, Is.False);
            Assert.That(listener.Calls, Is.EqualTo(1),
                "Durable costs must still notify listeners when later middleware violates the contract.");
        }

        private static InMemoryRulesStore CreateFullySeededStore()
        {
            ActiveRuleBinding binding = new ActiveRuleBinding(
                Binding,
                BindingDefinition,
                Actor,
                default(ActiveEffectId?),
                Source,
                0);
            return new InMemoryRulesStore(new RulesStateSeed()
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true))
                .SeedSpellSlot(new SpellSlotState(SpellPool, Actor, 2, 2))
                .SeedFocusPoints(Actor, new FocusPointState(2, 3))
                .SeedAmmunition(new AmmunitionState(Ammunition, Actor, 5))
                .SeedRuleBinding(binding)
                .SeedFrequency(Binding, new FrequencyState(4, 0)));
        }

        private static RuleRegistry CreateRuleRegistry()
        {
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry.Define(BindingDefinition);
            return registry.Build();
        }

        private static RuleRegistry CreateRuleRegistry(ActionBegunMiddleware middleware)
        {
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry.Define(BindingDefinition).Middleware(
                RuleLifecyclePhase.Reaction,
                middleware);
            return registry.Build();
        }

        private static RuleRegistry CreateRuleRegistry(
            ActionBegunMiddleware middleware,
            CountingActionCostListener listener)
        {
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry.Define(BindingDefinition)
                .Middleware(RuleLifecyclePhase.Reaction, middleware)
                .FactListener(RuleLifecyclePhase.Observation, listener);
            return registry.Build();
        }

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>());
            return (ResolvedOpResult<TResult>)result;
        }

        private readonly struct ActionCostExpectation
        {
            public ActionCostExpectation(
                ActionCost cost,
                int actionsRemaining,
                bool reactionAvailable,
                bool emitsFact)
            {
                Cost = cost;
                ActionsRemaining = actionsRemaining;
                ReactionAvailable = reactionAvailable;
                EmitsFact = emitsFact;
            }

            public ActionCost Cost { get; }
            public int ActionsRemaining { get; }
            public bool ReactionAvailable { get; }
            public bool EmitsFact { get; }
        }

        private readonly struct TestActionOutcome
        {
            public TestActionOutcome(bool domainSucceeded) =>
                DomainSucceeded = domainSucceeded;

            public bool DomainSucceeded { get; }
        }

        private sealed class TestActionOp : ActionOp<TestActionOutcome>
        {
            public TestActionOp()
                : base(ActionLifecycleTests.Actor, ActionDefinition)
            {
            }
        }

        private sealed class FixedActionCatalog : IActionCatalog
        {
            private readonly ActionProfile profile;

            public FixedActionCatalog(ActionProfile profile) =>
                this.profile = profile ?? throw new ArgumentNullException(nameof(profile));

            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
            {
                Assert.That(definitionId, Is.EqualTo(ActionDefinition));
                return profile;
            }
        }

        private sealed class CountingProfileResolver : IActionProfileResolver
        {
            private readonly ActionProfile effectiveProfile;

            public CountingProfileResolver(ActionProfile effectiveProfile) =>
                this.effectiveProfile = effectiveProfile;

            public int Calls { get; private set; }
            public int StartingActions { get; private set; }

            public ActionProfile Resolve(
                ActionOpInfo action,
                ActionProfile baseProfile,
                RulesSnapshot snapshot)
            {
                Calls++;
                StartingActions = snapshot.ActionEconomy[action.Actor].ActionsRemaining;
                return effectiveProfile;
            }
        }

        private sealed class CapturingValidator : IActionValidator<TestActionOp>
        {
            public ActionProfile Profile { get; private set; }

            public ActionValidationResult Validate(
                OpFrame<TestActionOp> frame,
                RulesSnapshot snapshot)
            {
                Profile = frame.ActionProfile;
                return ActionValidationResult.Valid;
            }
        }

        private sealed class LockObservingProfileResolver : IActionProfileResolver
        {
            private readonly Func<bool> isDispatcherGateHeld;

            public LockObservingProfileResolver(Func<bool> isDispatcherGateHeld) =>
                this.isDispatcherGateHeld = isDispatcherGateHeld ??
                    throw new ArgumentNullException(nameof(isDispatcherGateHeld));

            public bool WasDispatcherGateHeld { get; private set; }

            public ActionProfile Resolve(
                ActionOpInfo action,
                ActionProfile baseProfile,
                RulesSnapshot snapshot)
            {
                WasDispatcherGateHeld = isDispatcherGateHeld();
                return baseProfile;
            }
        }

        private sealed class RejectingValidator : IActionValidator<TestActionOp>
        {
            private readonly string reason;

            public RejectingValidator(string reason) => this.reason = reason;

            public ActionValidationResult Validate(
                OpFrame<TestActionOp> frame,
                RulesSnapshot snapshot) =>
                ActionValidationResult.Invalid(reason);
        }

        private sealed class RecordingActionHandler :
            IOpHandler<TestActionOp, TestActionOutcome>
        {
            private readonly bool domainSucceeded;

            public RecordingActionHandler(bool domainSucceeded) =>
                this.domainSucceeded = domainSucceeded;

            public bool WasCalled { get; private set; }
            public ActionProfile Profile { get; private set; }
            public int ActionsRemaining { get; private set; }
            public int SpellSlotsRemaining { get; private set; }
            public int FocusPointsRemaining { get; private set; }
            public int AmmunitionRemaining { get; private set; }
            public int FrequencyUses { get; private set; }

            public ValueTask<TestActionOutcome> Handle(
                OpFrame<TestActionOp> frame,
                OpHandlerContext context)
            {
                WasCalled = true;
                Profile = frame.ActionProfile;
                ActionsRemaining = context.Snapshot.ActionEconomy.TryGet(
                    Actor,
                    out ActionEconomyState economy)
                    ? economy.ActionsRemaining
                    : -1;
                SpellSlotsRemaining = context.Snapshot.SpellSlots.TryGet(
                    SpellPool,
                    out SpellSlotState slot)
                    ? slot.Remaining
                    : -1;
                FocusPointsRemaining = context.Snapshot.FocusPoints.TryGet(
                    Actor,
                    out FocusPointState focus)
                    ? focus.Current
                    : -1;
                AmmunitionRemaining = context.Snapshot.Ammunition.TryGet(
                    Ammunition,
                    out AmmunitionState ammunition)
                    ? ammunition.Remaining
                    : -1;
                FrequencyUses = context.Snapshot.Frequencies.TryGet(
                    Binding,
                    out FrequencyState frequency)
                    ? frequency.Uses
                    : -1;
                return new ValueTask<TestActionOutcome>(
                    new TestActionOutcome(domainSucceeded));
            }
        }

        private sealed class ActionBegunMiddleware :
            IOpMiddleware<ActionBegunOp, ActionStartOutcome>
        {
            private readonly bool interrupt;

            public ActionBegunMiddleware(bool interrupt) => this.interrupt = interrupt;

            public int Calls { get; private set; }
            public bool CanTriggerReactions { get; private set; }
            public bool WouldPromptReaction { get; private set; }
            public OpId ObservedActionId { get; private set; }

            public ValueTask<OpResult<ActionStartOutcome>> Invoke(
                OpFrame<ActionBegunOp> frame,
                OpMiddlewareContext context,
                OpNext<ActionStartOutcome> next)
            {
                Calls++;
                ObservedActionId = frame.Op.ActionOpId;
                CanTriggerReactions = context.Trace
                    .GetActionProfile(frame.Op.ActionOpId)
                    .CanTriggerReactions;
                WouldPromptReaction = CanTriggerReactions;
                return interrupt
                    ? new ValueTask<OpResult<ActionStartOutcome>>(
                        OpResult<ActionStartOutcome>.Resolved(ActionStartOutcome.Interrupted))
                    : next();
            }
        }

        private sealed class CountingActionCostListener :
            IFactListener<ActionCostSpentFact>
        {
            public int Calls { get; private set; }

            public ValueTask OnFactCommitted(
                ActionCostSpentFact fact,
                FactContext context)
            {
                Calls++;
                return default;
            }
        }

        private sealed class ShortCircuitingCostMiddleware :
            IOpMiddleware<CommitActionCostsOp, ActionCostsOutcome>
        {
            public int Calls { get; private set; }

            public ValueTask<OpResult<ActionCostsOutcome>> Invoke(
                OpFrame<CommitActionCostsOp> frame,
                OpMiddlewareContext context,
                OpNext<ActionCostsOutcome> next)
            {
                Calls++;
                return new ValueTask<OpResult<ActionCostsOutcome>>(
                    OpResult<ActionCostsOutcome>.Resolved(default));
            }
        }

        private sealed class InvalidatingActionMiddleware :
            IOpMiddleware<TestActionOp, TestActionOutcome>
        {
            public ValueTask<OpResult<TestActionOutcome>> Invoke(
                OpFrame<TestActionOp> frame,
                OpMiddlewareContext context,
                OpNext<TestActionOutcome> next) =>
                new ValueTask<OpResult<TestActionOutcome>>(
                    OpResult<TestActionOutcome>.Invalid("too late to invalidate"));
        }
    }
}
