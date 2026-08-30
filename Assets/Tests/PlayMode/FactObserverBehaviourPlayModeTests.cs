using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Verifies the Unity Fact-observer helper's explicit configuration and component lifecycle.
/// </summary>
public sealed class FactObserverBehaviourPlayModeTests
{
    private static readonly CreatureId Creature = new CreatureId("fact-observer-playmode");
    private static readonly RuleSource Source = RuleSource.FromSlug("fact-observer-playmode-test");

    [UnityTest]
    public IEnumerator ConfigurationTracksEnableDisableAndDestroy()
    {
        InMemoryRulesStore store = new InMemoryRulesStore(
            new RulesStateSeed().SeedHealth(Creature, new HealthState(0, 100))
        );
        RuleDispatcher dispatcher = new RuleDispatcherBuilder(store)
            .RegisterHandler<ObserverRootOp, int>(new ObserverRootHandler())
            .RegisterReducer<ObserverChangeOp, int>(new ObserverChangeReducer(), Source)
            .Build();
        GameObject gameObject = new GameObject("Fact observer lifecycle test");
        gameObject.SetActive(false);
        TestFactObserverBehaviour observer = gameObject.AddComponent<TestFactObserverBehaviour>();
        observer.Configure(dispatcher);

        Task<OpResult<int>> inactiveDispatch = dispatcher.Dispatch(new ObserverRootOp()).AsTask();
        yield return AwaitCompletion(inactiveDispatch);
        Assert.That(observer.DeliveryCount, Is.Zero);

        gameObject.SetActive(true);
        Assert.That(observer.LifecycleCalls, Is.EqualTo(new[] { "enabled" }));
        Task<OpResult<int>> observedDispatch = dispatcher.Dispatch(new ObserverRootOp()).AsTask();
        yield return AwaitCompletion(observedDispatch);
        Assert.That(observer.DeliveryCount, Is.EqualTo(1));
        observer.enabled = false;
        Assert.That(observer.LifecycleCalls, Is.EqualTo(new[] { "enabled", "disabled" }));

        Task<OpResult<int>> disabledDispatch = dispatcher.Dispatch(new ObserverRootOp()).AsTask();
        yield return AwaitCompletion(disabledDispatch);
        Assert.That(observer.DeliveryCount, Is.EqualTo(1));

        observer.enabled = true;
        Assert.That(
            observer.LifecycleCalls,
            Is.EqualTo(new[] { "enabled", "disabled", "enabled" })
        );
        Task<OpResult<int>> reenabledDispatch = dispatcher.Dispatch(new ObserverRootOp()).AsTask();
        yield return AwaitCompletion(reenabledDispatch);
        Assert.That(observer.DeliveryCount, Is.EqualTo(2));

        int deliveryCountBeforeDestroy = observer.DeliveryCount;
        List<string> lifecycleCalls = observer.LifecycleCalls;
        UnityEngine.Object.Destroy(gameObject);
        yield return null;
        Task<OpResult<int>> destroyedDispatch = dispatcher.Dispatch(new ObserverRootOp()).AsTask();
        yield return AwaitCompletion(destroyedDispatch);
        Assert.That(observer.DeliveryCount, Is.EqualTo(deliveryCountBeforeDestroy));
        Assert.That(
            lifecycleCalls,
            Is.EqualTo(new[] { "enabled", "disabled", "enabled", "disabled", "destroyed" })
        );
        Assert.That(observer == null, Is.True);
    }

    private static IEnumerator AwaitCompletion(Task task)
    {
        while (!task.IsCompleted)
            yield return null;
        task.GetAwaiter().GetResult();
    }

    private sealed class ObserverRootOp : IRuleOp<int> { }

    private sealed class ObserverRootHandler : IOpHandler<ObserverRootOp, int>
    {
        public async ValueTask<int> Handle(OpFrame<ObserverRootOp> frame, OpHandlerContext context)
        {
            OpResult<int> changed = await context.Dispatch(new ObserverChangeOp());
            return ((ResolvedOpResult<int>)changed).Value;
        }
    }

    private sealed class ObserverChangeOp : IRuleOp<int> { }

    private sealed class ObserverChangeReducer : IOpReducer<ObserverChangeOp, int>
    {
        public ReductionResult<int> Reduce(
            ReductionContext<ObserverChangeOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (!state.Health.TryGet(Creature, out HealthState previous))
                throw new InvalidOperationException("Missing PlayMode Fact-observer health seed.");
            int current = previous.Current + 1;
            state.Health.Set(Creature, new HealthState(current, previous.Maximum));
            facts.Stage(new ObserverChangedFact(current));
            return ReductionResult<int>.Accept(current);
        }
    }

    internal sealed class ObserverChangedFact : RuleFact
    {
        public ObserverChangedFact(int current)
        {
            Current = current;
        }

        public int Current { get; }
    }

    internal sealed class TestFactObserverBehaviour : FactObserverBehaviour<ObserverChangedFact>
    {
        public int DeliveryCount { get; private set; }
        public List<string> LifecycleCalls { get; } = new List<string>();

        public override void OnFactCommitted(
            ObserverChangedFact fact,
            RulesSnapshot currentSnapshot
        ) => DeliveryCount++;

        protected override void OnEnable()
        {
            base.OnEnable();
            LifecycleCalls.Add("enabled");
        }

        protected override void OnDisable()
        {
            LifecycleCalls.Add("disabled");
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            LifecycleCalls.Add("destroyed");
            base.OnDestroy();
        }
    }
}
