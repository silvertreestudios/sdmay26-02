using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    public sealed class LightRulesTests
    {
        private static readonly CreatureId Actor = new CreatureId("light-actor");
        private static readonly CreatureId UnknownActor = new CreatureId("unknown-light-actor");
        private static readonly PlayerId Player = new PlayerId("light-player");
        private static readonly RuleDefinitionId InterruptionDefinition = new RuleDefinitionId(
            "light-test-interruption"
        );
        private static readonly BindingId InterruptionBinding = new BindingId(
            "light-test-interruption-binding"
        );
        private static readonly RuleSource TestSource = RuleSource.FromSlug("light-test");
        private static readonly Trait[] ExpectedTraits =
        {
            Trait.FromSlug("cantrip"),
            Trait.FromSlug("concentrate"),
            Trait.FromSlug("light"),
            Trait.FromSlug("manipulate"),
        };

        [Test]
        public void DefinitionFreezesTwoActionProfileWithExactDefinitionTraits()
        {
            LightActionDefinition definition = CreateDefinition(isPrepared: true);

            ActionProfile profile = definition.GetBaseProfile(LightActionDefinition.DefinitionId);

            Assert.That(profile.Cost, Is.EqualTo(ActionCost.Two));
            Assert.That(profile.AdditionalCosts, Is.Empty);
            Assert.That(
                profile.Traits.Select(trait => trait.Slug),
                Is.EqualTo(new[] { "cantrip", "concentrate", "light", "manipulate" })
            );
        }

        [Test]
        public void AvailabilityRequiresPreparationRegistrationAndTwoActions()
        {
            LightActionDefinition prepared = CreateDefinition(isPrepared: true);
            LightActionDefinition unprepared = CreateDefinition(isPrepared: false);
            RulesSnapshot threeActions = CreateStore(3).Snapshot;
            RulesSnapshot oneAction = CreateStore(1).Snapshot;

            Assert.That(
                prepared.GetAvailability(threeActions, Actor),
                Is.TypeOf<AvailableActionAvailability>()
            );
            Assert.That(
                unprepared.GetAvailability(threeActions, Actor),
                Is.TypeOf<UnavailableActionAvailability>()
            );
            Assert.That(
                prepared.GetAvailability(oneAction, Actor),
                Is.TypeOf<UnavailableActionAvailability>()
            );
            Assert.That(
                prepared.GetAvailability(threeActions, UnknownActor),
                Is.TypeOf<UnavailableActionAvailability>()
            );
        }

        [Test]
        public async Task ValidCastAtomicallySpendsTwoActionsAndResolves()
        {
            InMemoryRulesStore store = CreateStore(3);
            RuleDispatcher dispatcher = CreateDispatcher(store, CreateDefinition(isPrepared: true));

            OpResult<LightCastOutcome> result = await dispatcher.Dispatch(new LightActionOp(Actor));

            ResolvedOpResult<LightCastOutcome> resolved = RequireResolved(result);
            Assert.That(resolved.Value, Is.EqualTo(new LightCastOutcome(Actor)));
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(resolved.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));

            IOpFrameView actionFrame = dispatcher.Trace.OrderedFrames.Single(frame =>
                frame.OpType == typeof(LightActionOp)
            );
            ActionProfile frozen = dispatcher.Trace.GetActionProfile(actionFrame.Id);
            Assert.That(frozen.Cost, Is.EqualTo(ActionCost.Two));
            Assert.That(
                frozen.Traits.Select(trait => trait.Slug),
                Is.EqualTo(new[] { "cantrip", "concentrate", "light", "manipulate" })
            );
        }

        [Test]
        public async Task InsufficientActionsRejectBeforeCostAndResolvedPresentation()
        {
            InMemoryRulesStore store = CreateStore(1);
            RuleDispatcher dispatcher = CreateDispatcher(store, CreateDefinition(isPrepared: true));
            CountingObserver observer = new CountingObserver();
            dispatcher.RegisterResolvedOpObserver<LightActionOp, LightCastOutcome>(observer);

            OpResult<LightCastOutcome> result = await dispatcher.Dispatch(new LightActionOp(Actor));

            Assert.That(result, Is.TypeOf<InvalidOpResult<LightCastOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(result.Facts, Is.Empty);
            Assert.That(observer.Calls, Is.Zero);
        }

        [Test]
        public async Task MissingPreparationAndUnknownActorRejectWithoutMutation()
        {
            InMemoryRulesStore unpreparedStore = CreateStore(3);
            RuleDispatcher unpreparedDispatcher = CreateDispatcher(
                unpreparedStore,
                CreateDefinition(isPrepared: false)
            );
            OpResult<LightCastOutcome> unprepared = await unpreparedDispatcher.Dispatch(
                new LightActionOp(Actor)
            );

            InMemoryRulesStore unknownStore = CreateStore(3);
            RuleDispatcher unknownDispatcher = CreateDispatcher(
                unknownStore,
                CreateDefinition(isPrepared: true, unknownIsPrepared: true)
            );
            OpResult<LightCastOutcome> unknown = await unknownDispatcher.Dispatch(
                new LightActionOp(UnknownActor)
            );

            Assert.That(unprepared, Is.TypeOf<InvalidOpResult<LightCastOutcome>>());
            Assert.That(
                unpreparedStore.Snapshot.ActionEconomy[Actor].ActionsRemaining,
                Is.EqualTo(3)
            );
            Assert.That(unprepared.Facts, Is.Empty);
            Assert.That(unknown, Is.TypeOf<InvalidOpResult<LightCastOutcome>>());
            Assert.That(unknownStore.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(unknown.Facts, Is.Empty);
        }

        [Test]
        public async Task InterruptedCastSpendsCommittedCostButSkipsResolvedPresentation()
        {
            InterruptingActionMiddleware middleware = new InterruptingActionMiddleware();
            RuleRegistryBuilder registry = new RuleRegistryBuilder();
            registry
                .Define(InterruptionDefinition)
                .Middleware(RuleLifecyclePhase.Reaction, middleware);
            InMemoryRulesStore store = CreateStoreWithBinding(
                3,
                new ActiveRuleBinding(
                    InterruptionBinding,
                    InterruptionDefinition,
                    Actor,
                    default,
                    TestSource,
                    0
                )
            );
            RuleDispatcher dispatcher = CreateDispatcher(
                store,
                CreateDefinition(isPrepared: true),
                registry.Build()
            );
            CountingObserver observer = new CountingObserver();
            dispatcher.RegisterResolvedOpObserver<LightActionOp, LightCastOutcome>(observer);

            OpResult<LightCastOutcome> result = await dispatcher.Dispatch(new LightActionOp(Actor));

            Assert.That(result, Is.TypeOf<InterruptedOpResult<LightCastOutcome>>());
            Assert.That(store.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
            Assert.That(result.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(middleware.Calls, Is.EqualTo(1));
            Assert.That(observer.Calls, Is.Zero);
        }

        private static LightActionDefinition CreateDefinition(
            bool isPrepared,
            bool unknownIsPrepared = false
        ) =>
            new LightActionDefinition(
                new DictionaryLightActorStateProvider(
                    new Dictionary<CreatureId, LightActorState>
                    {
                        [Actor] = new LightActorState(isPrepared),
                        [UnknownActor] = new LightActorState(unknownIsPrepared),
                    }
                ),
                ExpectedTraits
            );

        private static InMemoryRulesStore CreateStore(int actions) =>
            new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedCreature(new CreatureState(Actor, Player))
                    .SeedActionEconomy(Actor, new ActionEconomyState(actions, true))
            );

        private static InMemoryRulesStore CreateStoreWithBinding(
            int actions,
            ActiveRuleBinding binding
        )
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Player))
                .SeedActionEconomy(Actor, new ActionEconomyState(actions, true))
                .SeedRuleBinding(binding);
            return new InMemoryRulesStore(seed);
        }

        private static RuleDispatcher CreateDispatcher(
            InMemoryRulesStore store,
            LightActionDefinition definition,
            RuleRegistry registry = null
        )
        {
            RuleDispatcherBuilder builder = new RuleDispatcherBuilder(store)
                .UseActionLifecycle(definition)
                .UseLightRules(definition);
            if (registry != null)
                builder.UseRuleRegistry(registry);
            return builder.Build();
        }

        private static ResolvedOpResult<LightCastOutcome> RequireResolved(
            OpResult<LightCastOutcome> result
        )
        {
            Assert.That(result, Is.TypeOf<ResolvedOpResult<LightCastOutcome>>());
            return (ResolvedOpResult<LightCastOutcome>)result;
        }

        private sealed class DictionaryLightActorStateProvider : ILightActorStateProvider
        {
            private readonly IReadOnlyDictionary<CreatureId, LightActorState> states;

            public DictionaryLightActorStateProvider(
                IReadOnlyDictionary<CreatureId, LightActorState> states
            ) => this.states = states;

            public LightActorState Get(CreatureId actor) =>
                states.TryGetValue(actor, out LightActorState state)
                    ? state
                    : new LightActorState(false);
        }

        private sealed class CountingObserver : IResolvedOpObserver<LightActionOp, LightCastOutcome>
        {
            public int Calls { get; private set; }

            public ValueTask OnOperationResolved(
                LightActionOp operation,
                LightCastOutcome result,
                RulesSnapshot currentSnapshot
            )
            {
                Calls++;
                return default;
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
