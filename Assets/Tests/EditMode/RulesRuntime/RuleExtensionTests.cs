using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Verifies binding-controlled middleware and post-commit Fact-listener contracts.
    /// </summary>
    public sealed class RuleExtensionTests
    {
        private static readonly CreatureId Creature = new CreatureId("extension-creature");
        private static readonly RuleSource Source = RuleSource.FromSlug("extension-test");
        private static readonly RuleDefinitionId DefinitionA = new RuleDefinitionId("definition-a");
        private static readonly RuleDefinitionId DefinitionB = new RuleDefinitionId("definition-b");
        private static readonly RuleDefinitionId DefinitionC = new RuleDefinitionId("definition-c");

        [Test]
        public void RegistryRejectsDuplicateAndIncompatibleRegistrations()
        {
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RuleDefinitionBuilder definition = registryBuilder.Define(DefinitionA);
            DelegateMiddleware<ValueOp, int> middleware = new DelegateMiddleware<ValueOp, int>(
                (frame, context, next) => next()
            );
            definition.Middleware(RuleLifecyclePhase.Transformation, middleware);

            Assert.Throws<InvalidOperationException>(() => registryBuilder.Define(DefinitionA));
            Assert.Throws<InvalidOperationException>(() =>
                definition.Middleware(RuleLifecyclePhase.Transformation, middleware)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => rulesWithInvalidPhase());

            RuleRegistry registry = registryBuilder.Build();
            Assert.Throws<InvalidOperationException>(() =>
                new RuleDispatcherBuilder(CreateStore()).UseRuleRegistry(registry).Build()
            );

            RuleRegistryBuilder mismatchBuilder = new RuleRegistryBuilder();
            mismatchBuilder
                .Define(DefinitionA)
                .Middleware(
                    RuleLifecyclePhase.Transformation,
                    new DelegateMiddleware<AmbiguousOp, string>((frame, context, next) => next())
                );
            InvalidOperationException mismatch = Assert.Throws<InvalidOperationException>(() =>
                new RuleDispatcherBuilder(CreateStore())
                    .RegisterHandler<AmbiguousOp, int>(new AmbiguousIntHandler())
                    .UseRuleRegistry(mismatchBuilder.Build())
                    .Build()
            );

            Assert.That(mismatch.Message, Does.Contain("expects String"));

            RuleDispatcher unknownDefinition = new RuleDispatcherBuilder(
                CreateStore(Binding("unknown", DefinitionC, 0))
            )
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .UseRuleRegistry(new RuleRegistryBuilder().Build())
                .Build();
            InvalidOperationException unknown = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await unknownDefinition.Dispatch(new ValueOp(1))
            );
            Assert.That(unknown.Message, Does.Contain("unknown rule definition"));

            void rulesWithInvalidPhase() =>
                registryBuilder.Define(DefinitionB).Middleware((RuleLifecyclePhase)999, middleware);
        }

        [Test]
        public void DefinitionsUseOrdinalIdOrderAcrossCultures()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo enUs = CultureInfo.GetCultureInfo("en-US");
                CultureInfo svSe = CultureInfo.GetCultureInfo("sv-SE");
                const string zRule = "z-rule";
                const string aUmlautRule = "ä-rule";

                Assert.That(enUs.CompareInfo.Compare(zRule, aUmlautRule), Is.GreaterThan(0));
                Assert.That(svSe.CompareInfo.Compare(zRule, aUmlautRule), Is.LessThan(0));

                string[] enUsDefinitions = buildDefinitions(enUs);
                string[] svSeDefinitions = buildDefinitions(svSe);
                string[] ordinalDefinitions = { zRule, aUmlautRule };

                Assert.That(enUsDefinitions, Is.EqualTo(ordinalDefinitions));
                Assert.That(svSeDefinitions, Is.EqualTo(ordinalDefinitions));
                Assert.That(svSeDefinitions, Is.EqualTo(enUsDefinitions));

                string[] buildDefinitions(CultureInfo culture)
                {
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = culture;
                    RuleRegistryBuilder builder = new RuleRegistryBuilder();
                    builder.Define(new RuleDefinitionId(aUmlautRule));
                    builder.Define(new RuleDefinitionId(zRule));
                    return builder
                        .Build()
                        .Definitions.Select(definition => definition.Id.Value)
                        .ToArray();
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public async Task MiddlewareUsesStablePhaseCreationAndIdOrderAcrossRuns()
        {
            List<string> calls = new List<string>();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(RuleLifecyclePhase.Prevention, new LoggingMiddleware(calls, 1));
            rules
                .Define(DefinitionB)
                .Middleware(RuleLifecyclePhase.Transformation, new LoggingMiddleware(calls, 1));
            rules
                .Define(DefinitionC)
                .Middleware(RuleLifecyclePhase.Transformation, new LoggingMiddleware(calls, 1));

            ActiveRuleBinding phaseFirst = Binding("z-phase", DefinitionA, 20);
            ActiveRuleBinding idLast = Binding("z-transform", DefinitionB, 1);
            ActiveRuleBinding idFirst = Binding("a-transform", DefinitionC, 1);
            ValueHandler handler = new ValueHandler(calls);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(idLast, phaseFirst, idFirst)
            )
                .RegisterHandler<ValueOp, int>(handler)
                .UseRuleRegistry(rules.Build())
                .Build();

            for (int run = 0; run < 2; run++)
            {
                OpResult<int> result = await dispatcher.Dispatch(new ValueOp(1));
                Assert.That(RequireResolved(result).Value, Is.EqualTo(4));
            }

            string[] oneRun =
            {
                "before:a-transform",
                "before:z-transform",
                "before:z-phase",
                "handler",
                "after:z-phase",
                "after:z-transform",
                "after:a-transform",
            };
            Assert.That(calls.Take(oneRun.Length), Is.EqualTo(oneRun));
            Assert.That(calls.Skip(oneRun.Length), Is.EqualTo(oneRun));
        }

        [Test]
        public async Task ObservationMiddlewareSeesTheSettledTransformedResult()
        {
            List<string> calls = new List<string>();
            ObservingMiddleware observer = new ObservingMiddleware(calls);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(RuleLifecyclePhase.Transformation, new LoggingMiddleware(calls, 3));
            rules.Define(DefinitionB).Middleware(RuleLifecyclePhase.Observation, observer);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(
                    Binding("transform", DefinitionA, 0),
                    Binding("observer", DefinitionB, 1)
                )
            )
                .RegisterHandler<ValueOp, int>(new ValueHandler(calls))
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(2));

            Assert.That(RequireResolved(result).Value, Is.EqualTo(5));
            Assert.That(observer.ObservedValue, Is.EqualTo(5));
            Assert.That(
                calls,
                Is.EqualTo(
                    new[]
                    {
                        "observe:before",
                        "before:transform",
                        "handler",
                        "after:transform",
                        "observe:after:5",
                    }
                )
            );
        }

        [Test]
        public async Task MiddlewareCanDispatchNestedWorkAndAlterTheTypedResult()
        {
            NestedIncrementMiddleware middleware = new NestedIncrementMiddleware(3);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(RuleLifecyclePhase.Transformation, middleware);
            ActiveRuleBinding binding = Binding("nested-binding", DefinitionA, 0);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(binding))
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(2));

            Assert.That(RequireResolved(result).Value, Is.EqualTo(15));
            Assert.That(result.Facts, Has.Count.EqualTo(1));
            Assert.That(middleware.SnapshotAfterChild, Is.EqualTo(13));
            Assert.That(middleware.Binding, Is.SameAs(binding));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(13));
        }

        [Test]
        public async Task MiddlewareCanShortCircuitWithoutInvokingTheHandler()
        {
            ValueHandler handler = new ValueHandler();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(
                    RuleLifecyclePhase.Prevention,
                    new DelegateMiddleware<ValueOp, int>(
                        (frame, context, next) =>
                            new ValueTask<OpResult<int>>(OpResult<int>.Invalid("prevented"))
                    )
                );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("short-circuit", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(handler)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(5));

            Assert.That(result, Is.TypeOf<InvalidOpResult<int>>());
            Assert.That(((InvalidOpResult<int>)result).Reason, Is.EqualTo("prevented"));
            Assert.That(handler.Calls, Is.Zero);
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public async Task DisabledOrRemovedBindingsStopMiddlewareImmediatelyAfterCommit()
        {
            List<string> calls = new List<string>();
            ActiveRuleBinding controller = Binding("controller", DefinitionA, 0);
            ActiveRuleBinding disabledLater = Binding("disabled-later", DefinitionB, 1);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(
                    RuleLifecyclePhase.Observation,
                    new DisableThenContinueMiddleware(disabledLater.Id, calls)
                );
            rules
                .Define(DefinitionB)
                .Middleware(RuleLifecyclePhase.Transformation, new LoggingMiddleware(calls, 100));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(controller, disabledLater)
            )
                .RegisterHandler<ValueOp, int>(new ValueHandler(calls))
                .RegisterReducer<SetBindingEnabledOp, bool>(new SetBindingEnabledReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> first = await dispatcher.Dispatch(new ValueOp(4));
            OpResult<int> second = await dispatcher.Dispatch(new ValueOp(4));

            Assert.That(RequireResolved(first).Value, Is.EqualTo(4));
            Assert.That(RequireResolved(second).Value, Is.EqualTo(4));
            Assert.That(
                calls,
                Is.EqualTo(
                    new[]
                    {
                        "disable:disabled-later",
                        "handler",
                        "disable:disabled-later",
                        "handler",
                    }
                )
            );
            Assert.That(dispatcher.Snapshot.RuleBindings[disabledLater.Id].IsEnabled, Is.False);
        }

        [Test]
        public async Task BindingActivatedByStateParticipatesStartingWithTheNextFrame()
        {
            List<string> calls = new List<string>();
            ActiveRuleBinding controller = Binding("activation-controller", DefinitionA, 0);
            ActiveRuleBinding activated = Binding("activated-rule", DefinitionB, 1, false);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(
                    RuleLifecyclePhase.Prevention,
                    new EnableThenContinueMiddleware(activated.Id)
                );
            rules
                .Define(DefinitionB)
                .Middleware(RuleLifecyclePhase.Transformation, new LoggingMiddleware(calls, 100));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(controller, activated)
            )
                .RegisterHandler<ValueOp, int>(new ValueHandler(calls))
                .RegisterReducer<SetBindingEnabledOp, bool>(new SetBindingEnabledReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> activationFrame = await dispatcher.Dispatch(new ValueOp(1));
            OpResult<int> nextFrame = await dispatcher.Dispatch(new ValueOp(1));

            Assert.That(RequireResolved(activationFrame).Value, Is.EqualTo(1));
            Assert.That(RequireResolved(nextFrame).Value, Is.EqualTo(101));
            Assert.That(
                calls,
                Is.EqualTo(
                    new[] { "handler", "before:activated-rule", "handler", "after:activated-rule" }
                )
            );
        }

        [Test]
        public async Task FactListenersObserveCommittedStateAndInvalidRootsNotifyNone()
        {
            SnapshotFactListener listener = new SnapshotFactListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            RuleDefinitionBuilder definition = rules.Define(DefinitionA);
            definition.FactListener(RuleLifecyclePhase.Observation, listener);
            definition.Middleware(
                RuleLifecyclePhase.Prevention,
                new CommitThenInvalidateMiddleware()
            );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("fact-listener", DefinitionA, 0))
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> committed = await dispatcher.Dispatch(new RootIncrementOp(new[] { 5 }));
            OpResult<int> invalid = await dispatcher.Dispatch(new ValueOp(0));

            Assert.That(RequireResolved(committed).Value, Is.EqualTo(15));
            Assert.That(listener.ObservedValues, Is.EqualTo(new[] { 15 }));
            Assert.That(invalid, Is.TypeOf<InvalidOpResult<int>>());
            Assert.That(
                invalid.Facts,
                Has.Count.EqualTo(1),
                "The nested reducer committed before middleware returned Invalid."
            );
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(16));
            Assert.That(
                listener.ObservedValues,
                Has.Count.EqualTo(1),
                "Invalid roots must not open post-commit listener delivery."
            );
        }

        [Test]
        public async Task ListenerEnabledAfterEarlierFactReceivesOnlyLaterFrames()
        {
            CounterFactRecordingListener listener = new CounterFactRecordingListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(RuleLifecyclePhase.Observation, listener);
            ActiveRuleBinding binding = Binding("enabled-between-facts", DefinitionA, 0, false);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(binding))
                .RegisterHandler<ActivateBindingBetweenIncrementsOp, int>(
                    new ActivateBindingBetweenIncrementsHandler()
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterReducer<SetBindingEnabledOp, bool>(new SetBindingEnabledReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(
                new ActivateBindingBetweenIncrementsOp(binding, false)
            );

            Assert.That(RequireResolved(result).Value, Is.EqualTo(13));
            Assert.That(listener.ObservedValues, Is.EqualTo(new[] { 13 }));
        }

        [Test]
        public async Task ListenerAddedAfterEarlierFactReceivesOnlyLaterFrames()
        {
            CounterFactRecordingListener listener = new CounterFactRecordingListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(RuleLifecyclePhase.Observation, listener);
            ActiveRuleBinding binding = Binding("added-between-facts", DefinitionA, 0);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore())
                .RegisterHandler<ActivateBindingBetweenIncrementsOp, int>(
                    new ActivateBindingBetweenIncrementsHandler()
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterReducer<AddBindingOp, bool>(new AddBindingReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(
                new ActivateBindingBetweenIncrementsOp(binding, true)
            );

            Assert.That(RequireResolved(result).Value, Is.EqualTo(13));
            Assert.That(listener.ObservedValues, Is.EqualTo(new[] { 13 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EligibleListenerDeactivatedBeforeDeliveryIsSkipped(bool remove)
        {
            CounterFactRecordingListener listener = new CounterFactRecordingListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(RuleLifecyclePhase.Observation, listener);
            ActiveRuleBinding binding = Binding("deactivated-before-delivery", DefinitionA, 0);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(binding))
                .RegisterHandler<DeactivateBindingAfterIncrementOp, int>(
                    new DeactivateBindingAfterIncrementHandler()
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterReducer<SetBindingEnabledOp, bool>(new SetBindingEnabledReducer(), Source)
                .RegisterReducer<RemoveBindingOp, bool>(new RemoveBindingReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new DeactivateBindingAfterIncrementOp(binding.Id, remove));

            Assert.That(listener.ObservedValues, Is.Empty);
        }

        [Test]
        public async Task BatchListenerIncludesOnlyFactsFromEligibleSourceFrames()
        {
            BatchRecordingListener listener = new BatchRecordingListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactBatchListener(RuleLifecyclePhase.Observation, listener);
            ActiveRuleBinding binding = Binding(
                "batch-enabled-between-facts",
                DefinitionA,
                0,
                false
            );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(binding))
                .RegisterHandler<ActivateBindingBetweenIncrementsOp, int>(
                    new ActivateBindingBetweenIncrementsHandler()
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterReducer<SetBindingEnabledOp, bool>(new SetBindingEnabledReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new ActivateBindingBetweenIncrementsOp(binding, false));

            Assert.That(listener.Batches, Has.Count.EqualTo(1));
            Assert.That(
                listener.Batches[0].Facts.Select(fact => fact.Current),
                Is.EqualTo(new[] { 13 })
            );
        }

        [Test]
        public async Task FactListenerDispatchStartsANewCausallyLinkedRoot()
        {
            ReactionHandler reactionHandler = new ReactionHandler();
            DispatchingFactListener listener = new DispatchingFactListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(RuleLifecyclePhase.Reaction, listener);
            ActiveRuleBinding binding = Binding("causal-listener", DefinitionA, 0);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(binding),
                new SequentialOpIdProvider(50)
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<ReactionOp, int>(reactionHandler)
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new RootIncrementOp(new[] { 2 }));

            Assert.That(result.Facts, Has.Count.EqualTo(1));
            Assert.That(listener.Binding, Is.SameAs(binding));
            Assert.That(listener.Source, Is.EqualTo(binding.Source));
            Assert.That(listener.DispatchResult, Is.EqualTo(12));
            Assert.That(reactionHandler.SnapshotValue, Is.EqualTo(12));

            RuleFact committedFact = result.Facts[0];
            OpFrame<RootIncrementOp> committedRoot = dispatcher.Trace.Get<RootIncrementOp>(
                new OpId(50)
            );
            OpFrame<ReactionOp> reactionRoot = dispatcher.Trace.Get<ReactionOp>(new OpId(52));
            Assert.That(committedFact.SourceOpId, Is.EqualTo(new OpId(51)));
            Assert.That(committedFact.RootOpId, Is.EqualTo(committedRoot.Id));
            Assert.That(reactionRoot.RootId, Is.EqualTo(reactionRoot.Id));
            Assert.That(reactionRoot.ParentId, Is.Null);
            Assert.That(reactionRoot.CauseId, Is.EqualTo(committedFact.SourceOpId));
            Assert.That(
                dispatcher.Trace.IsCausedBy(reactionRoot.Id, committedFact.SourceOpId),
                Is.True
            );
        }

        [Test]
        public async Task BatchListenersReceiveEachCommittedRootOnceWithoutMixing()
        {
            BatchRecordingListener listener = new BatchRecordingListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactBatchListener(RuleLifecyclePhase.Observation, listener);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("batch-listener", DefinitionA, 0))
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1, 2 }));
            await dispatcher.Dispatch(new RootIncrementOp(new[] { 3 }));

            Assert.That(listener.Batches, Has.Count.EqualTo(2));
            Assert.That(listener.Batches[0].RootId, Is.Not.EqualTo(listener.Batches[1].RootId));
            Assert.That(
                listener.Batches[0].Facts.Select(fact => fact.Current),
                Is.EqualTo(new[] { 11, 13 })
            );
            Assert.That(
                listener.Batches[1].Facts.Select(fact => fact.Current),
                Is.EqualTo(new[] { 16 })
            );
            Assert.That(
                listener.Batches.All(batch =>
                    batch.Facts.All(fact => fact.RootOpId == batch.RootId)
                ),
                Is.True
            );
        }

        [Test]
        public async Task FactListenersUseStablePhaseCreationAndIdOrderAcrossRuns()
        {
            List<string> calls = new List<string>();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .FactListener(RuleLifecyclePhase.Prevention, new LoggingFactListener(calls));
            rules
                .Define(DefinitionB)
                .FactListener(RuleLifecyclePhase.Transformation, new LoggingFactListener(calls));
            rules
                .Define(DefinitionC)
                .FactListener(RuleLifecyclePhase.Transformation, new LoggingFactListener(calls));

            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(
                    Binding("z-transform", DefinitionB, 1),
                    Binding("z-phase", DefinitionA, 20),
                    Binding("a-transform", DefinitionC, 1)
                )
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }));
            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }));

            string[] oneRun = { "z-phase", "a-transform", "z-transform" };
            Assert.That(calls.Take(oneRun.Length), Is.EqualTo(oneRun));
            Assert.That(calls.Skip(oneRun.Length), Is.EqualTo(oneRun));
        }

        [Test]
        public async Task ListenerRemovedBindingIsSkippedLaterInTheSameDeliveryPlan()
        {
            List<string> calls = new List<string>();
            ActiveRuleBinding controller = Binding("controller-listener", DefinitionA, 0);
            ActiveRuleBinding removed = Binding("removed-listener", DefinitionB, 1);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .FactListener(
                    RuleLifecyclePhase.Prevention,
                    new RemovingFactListener(removed.Id, calls)
                );
            rules
                .Define(DefinitionB)
                .FactListener(RuleLifecyclePhase.Observation, new LoggingFactListener(calls));
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(CreateStore(controller, removed))
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<RemoveBindingRootOp, bool>(new RemoveBindingRootHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterReducer<RemoveBindingOp, bool>(new RemoveBindingReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }));

            Assert.That(calls, Is.EqualTo(new[] { "remove:removed-listener" }));
            Assert.That(dispatcher.Snapshot.RuleBindings.Contains(removed.Id), Is.False);
        }

        [Test]
        public async Task ReducerFactsSurviveReplacementAndKeepTheirCommitOrder()
        {
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(
                    RuleLifecyclePhase.Transformation,
                    new PostCommitTransformMiddleware(2)
                );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("fact-transform", DefinitionA, 0)),
                new SequentialOpIdProvider(10)
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .RegisterReducer<FollowUpIncrementOp, int>(new FollowUpIncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }));

            Assert.That(RequireResolved(result).Value, Is.EqualTo(11));
            Assert.That(
                result.Facts.Cast<CounterChangedFact>().Select(fact => fact.Current),
                Is.EqualTo(new[] { 11, 13 })
            );
            Assert.That(
                result.Facts.Select(fact => fact.SourceOpId),
                Is.EqualTo(new[] { new OpId(11), new OpId(12) })
            );
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(13));
        }

        [Test]
        public async Task MiddlewareContinuationCannotEscapeItsCallback()
        {
            CapturingContinuationMiddleware middleware = new CapturingContinuationMiddleware();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(RuleLifecyclePhase.Prevention, middleware);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("capturing-next", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(4));
            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await middleware.Continuation()
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<int>>());
            Assert.That(error.Message, Does.Contain("after its callback returns"));
        }

        [Test]
        public void SynchronouslyIgnoredMiddlewareContinuationSuccessIsRejected()
        {
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(
                    RuleLifecyclePhase.Transformation,
                    new IgnoringContinuationMiddleware()
                );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("ignored-next-success", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .UseRuleRegistry(rules.Build())
                .Build();

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await dispatcher.Dispatch(new ValueOp(4))
            );

            Assert.That(
                error.Message,
                Is.EqualTo("Middleware for ValueOp returned before awaiting its continuation.")
            );
        }

        [Test]
        public void SynchronouslyIgnoredMiddlewareContinuationFailureIsPropagated()
        {
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(
                    RuleLifecyclePhase.Transformation,
                    new IgnoringContinuationMiddleware()
                );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("ignored-next-failure", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(new SynchronouslyFailingValueHandler())
                .UseRuleRegistry(rules.Build())
                .Build();

            ApplicationException error = Assert.ThrowsAsync<ApplicationException>(async () =>
                await dispatcher.Dispatch(new ValueOp(4))
            );

            Assert.That(error.Message, Is.EqualTo("synchronous resolver failure"));
        }

        [Test]
        public void MiddlewareAndIgnoredFailingContinuationAreAggregatedInStableOrder()
        {
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .Middleware(
                    RuleLifecyclePhase.Transformation,
                    new ThrowingIgnoringContinuationMiddleware()
                );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("dual-middleware-failure", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(
                    new SynchronouslyInvalidValueHandler("ignored continuation failure")
                )
                .UseRuleRegistry(rules.Build())
                .Build();

            AggregateException error = Assert.ThrowsAsync<AggregateException>(async () =>
                await dispatcher.Dispatch(new ValueOp(4))
            );

            Assert.That(
                error.Message,
                Does.StartWith("Callback execution and cleanup of its unconsumed work both failed.")
            );
            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ApplicationException>());
            Assert.That(
                error.InnerExceptions[0].Message,
                Is.EqualTo("middleware callback failure")
            );
            Assert.That(error.InnerExceptions[1], Is.TypeOf<InvalidOperationException>());
            Assert.That(
                error.InnerExceptions[1].Message,
                Is.EqualTo("ignored continuation failure")
            );
        }

        [Test]
        public async Task SuspendedContinuationRejectsChildDispatchThenBothPathsRecover()
        {
            SuspendedOnceValueHandler handler = new SuspendedOnceValueHandler();
            ContinuationThenChildOverlapMiddleware middleware =
                new ContinuationThenChildOverlapMiddleware(handler);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(RuleLifecyclePhase.Transformation, middleware);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("continuation-overlap", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(handler)
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> recovered = await dispatcher.Dispatch(new ValueOp(4));
            OpResult<int> laterRoot = await dispatcher.Dispatch(new ValueOp(5));

            Assert.That(RequireResolved(recovered).Value, Is.EqualTo(4));
            Assert.That(RequireResolved(laterRoot).Value, Is.EqualTo(5));
            Assert.That(middleware.OverlapErrors, Has.Count.EqualTo(2));
            Assert.That(
                middleware.OverlapErrors.All(error =>
                    error.Message.Contains("overlapping child dispatch")
                    && error.Message.Contains("middleware continuation is active")
                ),
                Is.True
            );
            Assert.That(middleware.ContinuationReuseErrors, Has.Count.EqualTo(2));
            Assert.That(
                middleware.ContinuationReuseErrors.All(error =>
                    error.Message == "Middleware may invoke its continuation at most once."
                ),
                Is.True
            );
            Assert.That(
                middleware.ContinuationWasCompletedAtOverlap,
                Is.EqualTo(new[] { false, true }),
                "A completed continuation remains owned until its result is consumed."
            );
            Assert.That(middleware.SequentialChildValues, Is.EqualTo(new[] { 11, 12 }));
            Assert.That(
                dispatcher.Snapshot.Health[Creature].Current,
                Is.EqualTo(12),
                "Rejected child dispatches must not mutate state."
            );
            Assert.That(handler.Calls, Is.EqualTo(2));
            Assert.That(
                dispatcher.Trace.OrderedFrames.Count,
                Is.EqualTo(4),
                "Rejected overlap attempts must not allocate operation frames."
            );
        }

        [Test]
        public async Task SuspendedChildRejectsContinuationThenBothPathsRecover()
        {
            SuspendedOnceMiddlewareChildHandler child = new SuspendedOnceMiddlewareChildHandler();
            ChildThenContinuationOverlapMiddleware middleware =
                new ChildThenContinuationOverlapMiddleware(child);
            ValueHandler handler = new ValueHandler();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(RuleLifecyclePhase.Transformation, middleware);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("child-overlap", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(handler)
                .RegisterHandler<MiddlewareChildOp, int>(child, InvocationPolicy.NestedOnly)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> recovered = await dispatcher.Dispatch(new ValueOp(6));
            OpResult<int> laterRoot = await dispatcher.Dispatch(new ValueOp(7));

            Assert.That(RequireResolved(recovered).Value, Is.EqualTo(6));
            Assert.That(RequireResolved(laterRoot).Value, Is.EqualTo(7));
            Assert.That(middleware.OverlapErrors, Has.Count.EqualTo(2));
            Assert.That(
                middleware.OverlapErrors.All(error =>
                    error.Message
                    == "Middleware cannot invoke its continuation while a child dispatch is active. "
                        + "Await the active child before continuing."
                ),
                Is.True
            );
            Assert.That(
                middleware.ChildWasCompletedAtOverlap,
                Is.EqualTo(new[] { false, true }),
                "A synchronously completed child remains owned until its result is consumed."
            );
            Assert.That(middleware.ChildValues, Is.EqualTo(new[] { 3, 3 }));
            Assert.That(child.Calls, Is.EqualTo(2));
            Assert.That(
                handler.Calls,
                Is.EqualTo(2),
                "A rejected first attempt must not consume the one-continuation allowance."
            );
            Assert.That(
                dispatcher.Trace.OrderedFrames.Count,
                Is.EqualTo(4),
                "Rejected continuation attempts must not invoke or trace the remaining chain."
            );
        }

        [Test]
        public async Task HandlerContextClosesBeforeMiddlewarePostProcessing()
        {
            CapturingValueHandler handler = new CapturingValueHandler();
            ProbeHandlerContextMiddleware middleware = new ProbeHandlerContextMiddleware(handler);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(RuleLifecyclePhase.Observation, middleware);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("handler-context", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(handler)
                .RegisterReducer<FollowUpIncrementOp, int>(new FollowUpIncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(4));

            Assert.That(RequireResolved(result).Value, Is.EqualTo(4));
            Assert.That(middleware.DispatchError, Is.Not.Null);
            Assert.That(
                middleware.DispatchError.Message,
                Does.Contain("after its callback returns")
            );
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(10));
        }

        [Test]
        public async Task InnerMiddlewareContextClosesBeforeOuterPostProcessing()
        {
            CapturingPassThroughMiddleware inner = new CapturingPassThroughMiddleware();
            ProbeInnerContextMiddleware outer = new ProbeInnerContextMiddleware(inner);
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).Middleware(RuleLifecyclePhase.Observation, outer);
            rules.Define(DefinitionB).Middleware(RuleLifecyclePhase.Transformation, inner);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(
                    Binding("outer-context", DefinitionA, 0),
                    Binding("inner-context", DefinitionB, 1)
                )
            )
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .RegisterReducer<FollowUpIncrementOp, int>(new FollowUpIncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            OpResult<int> result = await dispatcher.Dispatch(new ValueOp(4));

            Assert.That(RequireResolved(result).Value, Is.EqualTo(4));
            Assert.That(outer.DispatchError, Is.Not.Null);
            Assert.That(outer.DispatchError.Message, Does.Contain("after its callback returns"));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(10));
        }

        [Test]
        public async Task SynchronouslyIgnoredListenerDispatchFailureIsPropagated()
        {
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .FactListener(RuleLifecyclePhase.Reaction, new IgnoringFailingDispatchListener());
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("ignored-dispatch", DefinitionA, 0))
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .RegisterHandler<FailingReactionOp, int>(new FailingReactionHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            ApplicationException error = Assert.ThrowsAsync<ApplicationException>(async () =>
                await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }))
            );
            OpResult<int> recovered = await dispatcher.Dispatch(new ValueOp(7));

            Assert.That(error.Message, Is.EqualTo("reaction failed"));
            Assert.That(
                dispatcher.Snapshot.Health[Creature].Current,
                Is.EqualTo(11),
                "The reducer commit remains durable when post-commit notification fails."
            );
            Assert.That(
                RequireResolved(recovered).Value,
                Is.EqualTo(7),
                "Listener failure must release root ownership."
            );
        }

        [Test]
        public async Task IgnoredListenerDispatchFailureAfterPriorAwaitIsPropagated()
        {
            YieldingIgnoringFailingDispatchListener listener =
                new YieldingIgnoringFailingDispatchListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(RuleLifecyclePhase.Reaction, listener);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("yielding-ignored-dispatch", DefinitionA, 0))
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<FailingReactionOp, int>(new FailingReactionHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            ValueTask<OpResult<int>> dispatch = dispatcher.Dispatch(
                new RootIncrementOp(new[] { 1 })
            );
            Assert.That(
                dispatch.IsCompleted,
                Is.False,
                "The listener must still be awaiting unrelated work."
            );
            listener.Continue();
            ApplicationException error = Assert.ThrowsAsync<ApplicationException>(async () =>
                await dispatch
            );

            Assert.That(error.Message, Is.EqualTo("reaction failed"));
            Assert.That(
                dispatcher.Snapshot.Health[Creature].Current,
                Is.EqualTo(11),
                "Awaiting unrelated work must not hide a later ignored listener dispatch."
            );
        }

        [Test]
        public void ListenerAndIgnoredFailingDispatchAreAggregatedInStableOrder()
        {
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules
                .Define(DefinitionA)
                .FactListener(
                    RuleLifecyclePhase.Reaction,
                    new ThrowingIgnoringValueDispatchListener()
                );
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("dual-listener-failure", DefinitionA, 0))
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterHandler<ValueOp, int>(
                    new SynchronouslyInvalidValueHandler("ignored listener dispatch failure")
                )
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            AggregateException error = Assert.ThrowsAsync<AggregateException>(async () =>
                await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }))
            );

            Assert.That(
                error.Message,
                Does.StartWith("Callback execution and cleanup of its unconsumed work both failed.")
            );
            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ApplicationException>());
            Assert.That(error.InnerExceptions[0].Message, Is.EqualTo("listener callback failure"));
            Assert.That(error.InnerExceptions[1], Is.TypeOf<InvalidOperationException>());
            Assert.That(
                error.InnerExceptions[1].Message,
                Is.EqualTo("ignored listener dispatch failure")
            );
            Assert.That(
                dispatcher.Snapshot.Health[Creature].Current,
                Is.EqualTo(11),
                "The Fact source commit remains durable when listener callback and cleanup fail."
            );
        }

        [Test]
        public async Task ExceptionalHandlerPublishesDurableFactsOnceBeforeRethrowing()
        {
            DispatchingFactListener listener = new DispatchingFactListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(RuleLifecyclePhase.Reaction, listener);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("exceptional-handler-listener", DefinitionA, 0))
            )
                .RegisterHandler<CommitThenThrowRootOp, int>(new CommitThenThrowRootHandler())
                .RegisterHandler<ReactionOp, int>(new ReactionHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            ApplicationException error = Assert.ThrowsAsync<ApplicationException>(async () =>
                await dispatcher.Dispatch(new CommitThenThrowRootOp())
            );
            OpResult<int> recovered = await dispatcher.Dispatch(new ReactionOp(7));

            Assert.That(error.Message, Is.EqualTo("handler resolution failed"));
            Assert.That(listener.Calls, Is.EqualTo(1));
            Assert.That(
                listener.DispatchResult,
                Is.EqualTo(11),
                "The exceptional root must retain ownership for causal listener dispatch."
            );
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(11));
            Assert.That(
                RequireResolved(recovered).Value,
                Is.EqualTo(7),
                "Exceptional publication must release root ownership after listeners finish."
            );
        }

        [Test]
        public void ResolutionAndNotificationFailuresAreAggregatedInStableOrder()
        {
            ThrowingFactListener listener = new ThrowingFactListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            RuleDefinitionBuilder definition = rules.Define(DefinitionA);
            definition.Middleware(
                RuleLifecyclePhase.Transformation,
                new CommitThenThrowMiddleware()
            );
            definition.FactListener(RuleLifecyclePhase.Observation, listener);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("dual-failure", DefinitionA, 0))
            )
                .RegisterHandler<ValueOp, int>(new ValueHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            AggregateException error = Assert.ThrowsAsync<AggregateException>(async () =>
                await dispatcher.Dispatch(new ValueOp(1))
            );

            Assert.That(
                error.Message,
                Does.StartWith(
                    "Operation resolution and post-commit Fact notification both failed."
                )
            );
            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ApplicationException>());
            Assert.That(
                error.InnerExceptions[0].Message,
                Is.EqualTo("middleware resolution failed")
            );
            Assert.That(error.InnerExceptions[1], Is.TypeOf<InvalidOperationException>());
            Assert.That(
                error.InnerExceptions[1].Message,
                Is.EqualTo("listener notification failed")
            );
            Assert.That(listener.Calls, Is.EqualTo(1));
            Assert.That(dispatcher.Snapshot.Health[Creature].Current, Is.EqualTo(11));
        }

        [Test]
        public async Task RetainedFactContextRejectsSnapshotAndTraceReads()
        {
            CapturingFactContextListener listener = new CapturingFactContextListener();
            RuleRegistryBuilder rules = new RuleRegistryBuilder();
            rules.Define(DefinitionA).FactListener(RuleLifecyclePhase.Observation, listener);
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                CreateStore(Binding("retained-fact-context", DefinitionA, 0))
            )
                .RegisterHandler<RootIncrementOp, int>(new RootIncrementHandler())
                .RegisterReducer<IncrementOp, int>(new IncrementReducer(), Source)
                .UseRuleRegistry(rules.Build())
                .Build();

            await dispatcher.Dispatch(new RootIncrementOp(new[] { 1 }));

            InvalidOperationException snapshotError = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = listener.Context.Snapshot;
            });
            InvalidOperationException traceError = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = listener.Context.Trace;
            });

            Assert.That(listener.SnapshotDuringCallback, Is.SameAs(dispatcher.Snapshot));
            Assert.That(listener.TraceDuringCallback, Is.SameAs(dispatcher.Trace));
            Assert.That(
                snapshotError.Message,
                Is.EqualTo("A Fact context cannot be used after its listener returns.")
            );
            Assert.That(traceError.Message, Is.EqualTo(snapshotError.Message));
        }

        private static InMemoryRulesStore CreateStore(params ActiveRuleBinding[] bindings)
        {
            RulesStateSeed seed = new RulesStateSeed().SeedHealth(
                Creature,
                new HealthState(10, 100)
            );
            foreach (ActiveRuleBinding binding in bindings)
                seed.SeedRuleBinding(binding);
            return new InMemoryRulesStore(seed);
        }

        private static ActiveRuleBinding Binding(
            string id,
            RuleDefinitionId definition,
            long creationOrder,
            bool isEnabled = true
        ) =>
            new ActiveRuleBinding(
                new BindingId(id),
                definition,
                Creature,
                null,
                Source,
                creationOrder,
                isEnabled
            );

        private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
            return (ResolvedOpResult<T>)result;
        }

        private sealed class ValueOp : IRuleOp<int>
        {
            public int Value { get; }

            public ValueOp(int value) => Value = value;
        }

        private sealed class ValueHandler : IOpHandler<ValueOp, int>
        {
            private static readonly Action<string> IgnoreCall = _ => { };
            private readonly Action<string> recordCall;

            public int Calls { get; private set; }

            public ValueHandler() => recordCall = IgnoreCall;

            public ValueHandler(List<string> calls)
            {
                if (calls == null)
                    throw new ArgumentNullException(nameof(calls));
                recordCall = calls.Add;
            }

            public ValueTask<int> Handle(OpFrame<ValueOp> frame, OpHandlerContext context)
            {
                Calls++;
                recordCall("handler");
                return new ValueTask<int>(frame.Op.Value);
            }
        }

        private sealed class SuspendedOnceValueHandler : IOpHandler<ValueOp, int>
        {
            private readonly TaskCompletionSource<bool> firstStarted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> firstRelease =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int Calls { get; private set; }
            public Task FirstStarted => firstStarted.Task;

            public void ReleaseFirst() => firstRelease.TrySetResult(true);

            public async ValueTask<int> Handle(OpFrame<ValueOp> frame, OpHandlerContext context)
            {
                Calls++;
                if (Calls == 1)
                {
                    firstStarted.TrySetResult(true);
                    await firstRelease.Task;
                }
                return frame.Op.Value;
            }
        }

        private sealed class ContinuationThenChildOverlapMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly SuspendedOnceValueHandler handler;

            public List<InvalidOperationException> OverlapErrors { get; } =
                new List<InvalidOperationException>();
            public List<bool> ContinuationWasCompletedAtOverlap { get; } = new List<bool>();
            public List<InvalidOperationException> ContinuationReuseErrors { get; } =
                new List<InvalidOperationException>();
            public List<int> SequentialChildValues { get; } = new List<int>();

            public ContinuationThenChildOverlapMiddleware(SuspendedOnceValueHandler handler) =>
                this.handler = handler;

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                ValueTask<OpResult<int>> continuation = next();
                await handler.FirstStarted;
                ContinuationWasCompletedAtOverlap.Add(continuation.IsCompleted);
                try
                {
                    _ = context.Dispatch(new IncrementOp(100));
                }
                catch (InvalidOperationException error)
                {
                    OverlapErrors.Add(error);
                }
                finally
                {
                    handler.ReleaseFirst();
                }

                OpResult<int> result = await continuation;
                try
                {
                    _ = next();
                }
                catch (InvalidOperationException error)
                {
                    ContinuationReuseErrors.Add(error);
                }
                OpResult<int> child = await context.Dispatch(new IncrementOp(1));
                SequentialChildValues.Add(RequireResolved(child).Value);
                return result;
            }
        }

        private sealed class MiddlewareChildOp : IRuleOp<int>
        {
            public int Value { get; }

            public MiddlewareChildOp(int value) => Value = value;
        }

        private sealed class SuspendedOnceMiddlewareChildHandler
            : IOpHandler<MiddlewareChildOp, int>
        {
            private readonly TaskCompletionSource<bool> firstStarted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> firstRelease =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int Calls { get; private set; }
            public Task FirstStarted => firstStarted.Task;

            public void ReleaseFirst() => firstRelease.TrySetResult(true);

            public async ValueTask<int> Handle(
                OpFrame<MiddlewareChildOp> frame,
                OpHandlerContext context
            )
            {
                Calls++;
                if (Calls == 1)
                {
                    firstStarted.TrySetResult(true);
                    await firstRelease.Task;
                }
                return frame.Op.Value;
            }
        }

        private sealed class ChildThenContinuationOverlapMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly SuspendedOnceMiddlewareChildHandler childHandler;

            public List<InvalidOperationException> OverlapErrors { get; } =
                new List<InvalidOperationException>();
            public List<bool> ChildWasCompletedAtOverlap { get; } = new List<bool>();
            public List<int> ChildValues { get; } = new List<int>();

            public ChildThenContinuationOverlapMiddleware(
                SuspendedOnceMiddlewareChildHandler childHandler
            ) => this.childHandler = childHandler;

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                ValueTask<OpResult<int>> child = context.Dispatch(new MiddlewareChildOp(3));
                await childHandler.FirstStarted;
                ChildWasCompletedAtOverlap.Add(child.IsCompleted);
                try
                {
                    _ = next();
                }
                catch (InvalidOperationException error)
                {
                    OverlapErrors.Add(error);
                }
                finally
                {
                    childHandler.ReleaseFirst();
                }

                ChildValues.Add(RequireResolved(await child).Value);
                return await next();
            }
        }

        private sealed class SynchronouslyFailingValueHandler : IOpHandler<ValueOp, int>
        {
            public ValueTask<int> Handle(OpFrame<ValueOp> frame, OpHandlerContext context) =>
                throw new ApplicationException("synchronous resolver failure");
        }

        private sealed class AmbiguousOp : IRuleOp<int>, IRuleOp<string> { }

        private sealed class AmbiguousIntHandler : IOpHandler<AmbiguousOp, int>
        {
            public ValueTask<int> Handle(OpFrame<AmbiguousOp> frame, OpHandlerContext context) =>
                new ValueTask<int>(1);
        }

        private sealed class DelegateMiddleware<TOp, TResult> : IOpMiddleware<TOp, TResult>
            where TOp : IRuleOp<TResult>
        {
            private readonly Func<
                OpFrame<TOp>,
                OpMiddlewareContext,
                OpNext<TResult>,
                ValueTask<OpResult<TResult>>
            > invoke;

            public DelegateMiddleware(
                Func<
                    OpFrame<TOp>,
                    OpMiddlewareContext,
                    OpNext<TResult>,
                    ValueTask<OpResult<TResult>>
                > invoke
            ) => this.invoke = invoke;

            public ValueTask<OpResult<TResult>> Invoke(
                OpFrame<TOp> frame,
                OpMiddlewareContext context,
                OpNext<TResult> next
            ) => invoke(frame, context, next);
        }

        private sealed class CapturingContinuationMiddleware : IOpMiddleware<ValueOp, int>
        {
            public OpNext<int> Continuation { get; private set; }

            public ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                Continuation = next;
                return new ValueTask<OpResult<int>>(OpResult<int>.Invalid("captured"));
            }
        }

        private sealed class CapturingValueHandler : IOpHandler<ValueOp, int>
        {
            public OpHandlerContext Context { get; private set; }

            public ValueTask<int> Handle(OpFrame<ValueOp> frame, OpHandlerContext context)
            {
                Context = context;
                return new ValueTask<int>(frame.Op.Value);
            }
        }

        private sealed class ProbeHandlerContextMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly CapturingValueHandler handler;

            public InvalidOperationException DispatchError { get; private set; }

            public ProbeHandlerContextMiddleware(CapturingValueHandler handler) =>
                this.handler = handler;

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                OpResult<int> result = await next();
                try
                {
                    await handler.Context.Dispatch(new FollowUpIncrementOp(1));
                }
                catch (InvalidOperationException exception)
                {
                    DispatchError = exception;
                }
                return result;
            }
        }

        private sealed class CapturingPassThroughMiddleware : IOpMiddleware<ValueOp, int>
        {
            public OpMiddlewareContext Context { get; private set; }

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                Context = context;
                return await next();
            }
        }

        private sealed class ProbeInnerContextMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly CapturingPassThroughMiddleware inner;

            public InvalidOperationException DispatchError { get; private set; }

            public ProbeInnerContextMiddleware(CapturingPassThroughMiddleware inner) =>
                this.inner = inner;

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                OpResult<int> result = await next();
                try
                {
                    await inner.Context.Dispatch(new FollowUpIncrementOp(1));
                }
                catch (InvalidOperationException exception)
                {
                    DispatchError = exception;
                }
                return result;
            }
        }

        private sealed class LoggingMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly List<string> calls;
            private readonly int addedValue;

            public LoggingMiddleware(List<string> calls, int addedValue)
            {
                this.calls = calls;
                this.addedValue = addedValue;
            }

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                ActiveRuleBinding binding = context.Binding;
                Assert.That(context.Source, Is.EqualTo(binding.Source));
                calls.Add($"before:{binding.Id.Value}");
                OpResult<int> result = await next();
                calls.Add($"after:{binding.Id.Value}");
                if (result is ResolvedOpResult<int> resolved)
                    return OpResult<int>.Resolved(resolved.Value + addedValue);
                return result;
            }
        }

        private sealed class ObservingMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly List<string> calls;

            public int ObservedValue { get; private set; }

            public ObservingMiddleware(List<string> calls) => this.calls = calls;

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                calls.Add("observe:before");
                OpResult<int> result = await next();
                ObservedValue = RequireResolved(result).Value;
                calls.Add($"observe:after:{ObservedValue}");
                return result;
            }
        }

        private sealed class IgnoringContinuationMiddleware : IOpMiddleware<ValueOp, int>
        {
            public ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                _ = next();
                return new ValueTask<OpResult<int>>(OpResult<int>.Resolved(99));
            }
        }

        private sealed class ThrowingIgnoringContinuationMiddleware : IOpMiddleware<ValueOp, int>
        {
            public ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                _ = next();
                throw new ApplicationException("middleware callback failure");
            }
        }

        private sealed class SynchronouslyInvalidValueHandler : IOpHandler<ValueOp, int>
        {
            private readonly string message;

            public SynchronouslyInvalidValueHandler(string message) => this.message = message;

            public ValueTask<int> Handle(OpFrame<ValueOp> frame, OpHandlerContext context) =>
                throw new InvalidOperationException(message);
        }

        private sealed class NestedIncrementMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly int amount;

            public int SnapshotAfterChild { get; private set; }
            public ActiveRuleBinding Binding { get; private set; }

            public NestedIncrementMiddleware(int amount) => this.amount = amount;

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                Binding = context.Binding;
                OpResult<int> changed = await context.Dispatch(new IncrementOp(amount));
                SnapshotAfterChild = context.Snapshot.Health[Creature].Current;
                OpResult<int> current = await next();
                return OpResult<int>.Resolved(
                    RequireResolved(current).Value + RequireResolved(changed).Value
                );
            }
        }

        private sealed class PostCommitTransformMiddleware : IOpMiddleware<IncrementOp, int>
        {
            private readonly int followUpAmount;

            public PostCommitTransformMiddleware(int followUpAmount) =>
                this.followUpAmount = followUpAmount;

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<IncrementOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                OpResult<int> reduced = await next();
                await context.Dispatch(new FollowUpIncrementOp(followUpAmount));
                return OpResult<int>.Resolved(RequireResolved(reduced).Value);
            }
        }

        private sealed class DisableThenContinueMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly BindingId bindingId;
            private readonly List<string> calls;

            public DisableThenContinueMiddleware(BindingId bindingId, List<string> calls)
            {
                this.bindingId = bindingId;
                this.calls = calls;
            }

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                calls.Add($"disable:{bindingId.Value}");
                await context.Dispatch(new SetBindingEnabledOp(bindingId, false));
                return await next();
            }
        }

        private sealed class EnableThenContinueMiddleware : IOpMiddleware<ValueOp, int>
        {
            private readonly BindingId bindingId;

            public EnableThenContinueMiddleware(BindingId bindingId) => this.bindingId = bindingId;

            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                if (!context.Snapshot.RuleBindings[bindingId].IsEnabled)
                    await context.Dispatch(new SetBindingEnabledOp(bindingId, true));
                return await next();
            }
        }

        private sealed class ActivateBindingBetweenIncrementsOp : IRuleOp<int>
        {
            public ActiveRuleBinding Binding { get; }
            public bool AddBinding { get; }

            public ActivateBindingBetweenIncrementsOp(ActiveRuleBinding binding, bool addBinding)
            {
                Binding = binding;
                AddBinding = addBinding;
            }
        }

        private sealed class ActivateBindingBetweenIncrementsHandler
            : IOpHandler<ActivateBindingBetweenIncrementsOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<ActivateBindingBetweenIncrementsOp> frame,
                OpHandlerContext context
            )
            {
                await context.Dispatch(new IncrementOp(1));
                if (frame.Op.AddBinding)
                    await context.Dispatch(new AddBindingOp(frame.Op.Binding));
                else
                    await context.Dispatch(new SetBindingEnabledOp(frame.Op.Binding.Id, true));
                OpResult<int> later = await context.Dispatch(new IncrementOp(2));
                return RequireResolved(later).Value;
            }
        }

        private sealed class DeactivateBindingAfterIncrementOp : IRuleOp<int>
        {
            public BindingId BindingId { get; }
            public bool RemoveBinding { get; }

            public DeactivateBindingAfterIncrementOp(BindingId bindingId, bool removeBinding)
            {
                BindingId = bindingId;
                RemoveBinding = removeBinding;
            }
        }

        private sealed class DeactivateBindingAfterIncrementHandler
            : IOpHandler<DeactivateBindingAfterIncrementOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<DeactivateBindingAfterIncrementOp> frame,
                OpHandlerContext context
            )
            {
                OpResult<int> changed = await context.Dispatch(new IncrementOp(1));
                if (frame.Op.RemoveBinding)
                    await context.Dispatch(new RemoveBindingOp(frame.Op.BindingId));
                else
                    await context.Dispatch(new SetBindingEnabledOp(frame.Op.BindingId, false));
                return RequireResolved(changed).Value;
            }
        }

        private sealed class IncrementOp : IRuleOp<int>
        {
            public int Amount { get; }

            public IncrementOp(int amount) => Amount = amount;
        }

        private sealed class IncrementReducer : IOpReducer<IncrementOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<IncrementOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                HealthState previous = state.Health.TryGet(Creature, out HealthState current)
                    ? current
                    : new HealthState(0, 100);
                HealthState changed = new HealthState(
                    previous.Current + context.Op.Amount,
                    previous.Maximum,
                    previous.Temporary
                );
                state.Health.Set(Creature, changed);
                facts.Stage(new CounterChangedFact(previous.Current, changed.Current));
                return ReductionResult<int>.Accept(changed.Current);
            }
        }

        private sealed class FollowUpIncrementOp : IRuleOp<int>
        {
            public int Amount { get; }

            public FollowUpIncrementOp(int amount) => Amount = amount;
        }

        private sealed class FollowUpIncrementReducer : IOpReducer<FollowUpIncrementOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<FollowUpIncrementOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                if (!state.Health.TryGet(Creature, out HealthState previous))
                    return ReductionResult<int>.Reject("Creature health not found.");
                HealthState changed = new HealthState(
                    previous.Current + context.Op.Amount,
                    previous.Maximum,
                    previous.Temporary
                );
                state.Health.Set(Creature, changed);
                facts.Stage(new CounterChangedFact(previous.Current, changed.Current));
                return ReductionResult<int>.Accept(changed.Current);
            }
        }

        private sealed class AddBindingOp : IRuleOp<bool>
        {
            public ActiveRuleBinding Binding { get; }

            public AddBindingOp(ActiveRuleBinding binding) => Binding = binding;
        }

        private sealed class AddBindingReducer : IOpReducer<AddBindingOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<AddBindingOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                if (state.RuleBindings.Contains(context.Op.Binding.Id))
                    return ReductionResult<bool>.Reject("Binding already exists.");
                state.RuleBindings.Set(context.Op.Binding.Id, context.Op.Binding);
                facts.Stage(new BindingChangedFact(context.Op.Binding.Id, true));
                return ReductionResult<bool>.Accept(true);
            }
        }

        private sealed class SetBindingEnabledOp : IRuleOp<bool>
        {
            public BindingId BindingId { get; }
            public bool IsEnabled { get; }

            public SetBindingEnabledOp(BindingId bindingId, bool isEnabled)
            {
                BindingId = bindingId;
                IsEnabled = isEnabled;
            }
        }

        private sealed class SetBindingEnabledReducer : IOpReducer<SetBindingEnabledOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<SetBindingEnabledOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                if (!state.RuleBindings.TryGet(context.Op.BindingId, out ActiveRuleBinding current))
                {
                    return ReductionResult<bool>.Reject("Binding not found.");
                }

                ActiveRuleBinding changed = new ActiveRuleBinding(
                    current.Id,
                    current.DefinitionId,
                    current.Owner,
                    current.EffectId,
                    current.Source,
                    current.CreationOrder,
                    context.Op.IsEnabled
                );
                state.RuleBindings.Set(changed.Id, changed);
                facts.Stage(new BindingChangedFact(changed.Id, changed.IsEnabled));
                return ReductionResult<bool>.Accept(changed.IsEnabled);
            }
        }

        private sealed class CounterChangedFact : RuleFact
        {
            public int Previous { get; }
            public int Current { get; }

            public CounterChangedFact(int previous, int current)
            {
                Previous = previous;
                Current = current;
            }
        }

        private sealed class BindingChangedFact : RuleFact
        {
            public BindingId BindingId { get; }
            public bool IsEnabled { get; }

            public BindingChangedFact(BindingId bindingId, bool isEnabled)
            {
                BindingId = bindingId;
                IsEnabled = isEnabled;
            }
        }

        private sealed class RootIncrementOp : IRuleOp<int>
        {
            public IReadOnlyList<int> Amounts { get; }

            public RootIncrementOp(IEnumerable<int> amounts) =>
                Amounts = Array.AsReadOnly(amounts.ToArray());
        }

        private sealed class RootIncrementHandler : IOpHandler<RootIncrementOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<RootIncrementOp> frame,
                OpHandlerContext context
            )
            {
                int current = context.Snapshot.Health[Creature].Current;
                foreach (int amount in frame.Op.Amounts)
                {
                    OpResult<int> changed = await context.Dispatch(new IncrementOp(amount));
                    current = RequireResolved(changed).Value;
                }
                return current;
            }
        }

        private sealed class CommitThenThrowRootOp : IRuleOp<int> { }

        private sealed class CommitThenThrowRootHandler : IOpHandler<CommitThenThrowRootOp, int>
        {
            public async ValueTask<int> Handle(
                OpFrame<CommitThenThrowRootOp> frame,
                OpHandlerContext context
            )
            {
                await context.Dispatch(new IncrementOp(1));
                throw new ApplicationException("handler resolution failed");
            }
        }

        private sealed class CommitThenInvalidateMiddleware : IOpMiddleware<ValueOp, int>
        {
            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                await context.Dispatch(new IncrementOp(1));
                return OpResult<int>.Invalid("invalid after committed child");
            }
        }

        private sealed class CommitThenThrowMiddleware : IOpMiddleware<ValueOp, int>
        {
            public async ValueTask<OpResult<int>> Invoke(
                OpFrame<ValueOp> frame,
                OpMiddlewareContext context,
                OpNext<int> next
            )
            {
                await context.Dispatch(new IncrementOp(1));
                throw new ApplicationException("middleware resolution failed");
            }
        }

        private sealed class SnapshotFactListener : IRuleFactListener<CounterChangedFact>
        {
            public List<int> ObservedValues { get; } = new List<int>();

            public ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                int current = context.Snapshot.Health[Creature].Current;
                Assert.That(current, Is.EqualTo(fact.Current));
                Assert.That(context.Source, Is.EqualTo(context.Binding.Source));
                ObservedValues.Add(current);
                return default;
            }
        }

        private sealed class CounterFactRecordingListener : IRuleFactListener<CounterChangedFact>
        {
            public List<int> ObservedValues { get; } = new List<int>();

            public ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                ObservedValues.Add(fact.Current);
                return default;
            }
        }

        private sealed class ReactionOp : IRuleOp<int>
        {
            public int TriggerValue { get; }

            public ReactionOp(int triggerValue) => TriggerValue = triggerValue;
        }

        private sealed class ReactionHandler : IOpHandler<ReactionOp, int>
        {
            public int SnapshotValue { get; private set; }

            public ValueTask<int> Handle(OpFrame<ReactionOp> frame, OpHandlerContext context)
            {
                SnapshotValue = context.Snapshot.Health[Creature].Current;
                return new ValueTask<int>(frame.Op.TriggerValue);
            }
        }

        private sealed class FailingReactionOp : IRuleOp<int> { }

        private sealed class FailingReactionHandler : IOpHandler<FailingReactionOp, int>
        {
            public ValueTask<int> Handle(
                OpFrame<FailingReactionOp> frame,
                OpHandlerContext context
            ) => throw new ApplicationException("reaction failed");
        }

        private sealed class IgnoringFailingDispatchListener : IRuleFactListener<CounterChangedFact>
        {
            public ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                _ = context.Dispatch(new FailingReactionOp());
                return default;
            }
        }

        private sealed class YieldingIgnoringFailingDispatchListener
            : IRuleFactListener<CounterChangedFact>
        {
            private readonly TaskCompletionSource<bool> continuation =
                new TaskCompletionSource<bool>();

            public void Continue() => continuation.TrySetResult(true);

            public async ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                await continuation.Task;
                _ = context.Dispatch(new FailingReactionOp());
            }
        }

        private sealed class ThrowingIgnoringValueDispatchListener
            : IRuleFactListener<CounterChangedFact>
        {
            public ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                _ = context.Dispatch(new ValueOp(fact.Current));
                throw new ApplicationException("listener callback failure");
            }
        }

        private sealed class DispatchingFactListener : IRuleFactListener<CounterChangedFact>
        {
            public ActiveRuleBinding Binding { get; private set; }
            public RuleSource Source { get; private set; }
            public int DispatchResult { get; private set; }
            public int Calls { get; private set; }

            public async ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                Calls++;
                Binding = context.Binding;
                Source = context.Source;
                OpResult<int> result = await context.Dispatch(new ReactionOp(fact.Current));
                DispatchResult = RequireResolved(result).Value;
            }
        }

        private sealed class ThrowingFactListener : IRuleFactListener<CounterChangedFact>
        {
            public int Calls { get; private set; }

            public ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                Calls++;
                throw new InvalidOperationException("listener notification failed");
            }
        }

        private sealed class CapturingFactContextListener : IRuleFactListener<CounterChangedFact>
        {
            public FactContext Context { get; private set; }
            public RulesSnapshot SnapshotDuringCallback { get; private set; }
            public ResolutionTrace TraceDuringCallback { get; private set; }

            public ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                Context = context;
                SnapshotDuringCallback = context.Snapshot;
                TraceDuringCallback = context.Trace;
                return default;
            }
        }

        private sealed class BatchRecordingListener : IRuleFactBatchListener<CounterChangedFact>
        {
            public List<CommittedFactBatch<CounterChangedFact>> Batches { get; } =
                new List<CommittedFactBatch<CounterChangedFact>>();

            public ValueTask OnFactsCommitted(
                CommittedFactBatch<CounterChangedFact> batch,
                FactContext context
            )
            {
                Assert.That(context.CommittedRootId, Is.EqualTo(batch.RootId));
                Batches.Add(batch);
                return default;
            }
        }

        private sealed class LoggingFactListener : IRuleFactListener<CounterChangedFact>
        {
            private readonly List<string> calls;

            public LoggingFactListener(List<string> calls) => this.calls = calls;

            public ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                calls.Add(context.Binding.Id.Value);
                return default;
            }
        }

        private sealed class RemovingFactListener : IRuleFactListener<CounterChangedFact>
        {
            private readonly BindingId removedBinding;
            private readonly List<string> calls;

            public RemovingFactListener(BindingId removedBinding, List<string> calls)
            {
                this.removedBinding = removedBinding;
                this.calls = calls;
            }

            public async ValueTask OnFactCommitted(CounterChangedFact fact, FactContext context)
            {
                calls.Add($"remove:{removedBinding.Value}");
                OpResult<bool> removed = await context.Dispatch(
                    new RemoveBindingRootOp(removedBinding)
                );
                Assert.That(RequireResolved(removed).Value, Is.True);
            }
        }

        private sealed class RemoveBindingRootOp : IRuleOp<bool>
        {
            public BindingId BindingId { get; }

            public RemoveBindingRootOp(BindingId bindingId) => BindingId = bindingId;
        }

        private sealed class RemoveBindingRootHandler : IOpHandler<RemoveBindingRootOp, bool>
        {
            public async ValueTask<bool> Handle(
                OpFrame<RemoveBindingRootOp> frame,
                OpHandlerContext context
            )
            {
                OpResult<bool> removed = await context.Dispatch(
                    new RemoveBindingOp(frame.Op.BindingId)
                );
                return RequireResolved(removed).Value;
            }
        }

        private sealed class RemoveBindingOp : IRuleOp<bool>
        {
            public BindingId BindingId { get; }

            public RemoveBindingOp(BindingId bindingId) => BindingId = bindingId;
        }

        private sealed class RemoveBindingReducer : IOpReducer<RemoveBindingOp, bool>
        {
            public ReductionResult<bool> Reduce(
                ReductionContext<RemoveBindingOp> context,
                RulesStateDraft state,
                FactSink facts
            )
            {
                if (!state.RuleBindings.Remove(context.Op.BindingId))
                    return ReductionResult<bool>.Reject("Binding not found.");
                facts.Stage(new BindingChangedFact(context.Op.BindingId, false));
                return ReductionResult<bool>.Accept(true);
            }
        }
    }
}
