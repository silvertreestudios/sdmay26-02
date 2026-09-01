using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;

public sealed class UnityActionPresentationRegistryTests
{
    private static readonly CreatureId Actor = new("presentation-actor");
    private static readonly ActionDefinitionId PresentedDefinition = new("presented-action");
    private static readonly ActionDefinitionId OtherDefinition = new("other-action");

    [Test]
    public async Task RoutesByDefinitionAndRestoresConcreteActionOutcomePair()
    {
        UnityActionPresentationRegistry registry = new();
        RecordingPresenter presenter = new();
        registry.Register<TestActionOp, TestOutcome>(PresentedDefinition, presenter);
        RuleDispatcher dispatcher = CreateDispatcher();
        dispatcher.RegisterFactObserver<RuleFact>(registry);
        TestActionOp presented = new(PresentedDefinition, 17);

        OpResult<TestOutcome> first = await dispatcher.Dispatch(presented);
        await dispatcher.Dispatch(new TestActionOp(OtherDefinition, 29));
        Drain(registry.Coordinator.Drain(presented));

        Assert.That(first, Is.TypeOf<ResolvedOpResult<TestOutcome>>());
        Assert.That(presenter.Calls, Is.EqualTo(new[] { "begin", "resolved" }));
        Assert.That(presenter.Action, Is.SameAs(presented));
        Assert.That(presenter.Outcome.Value, Is.EqualTo(17));
        Assert.That(presenter.Snapshot, Is.SameAs(dispatcher.Snapshot));
    }

    [Test]
    public void RejectsDuplicateDefinitionRegistration()
    {
        UnityActionPresentationRegistry registry = new();
        registry.Register<TestActionOp, TestOutcome>(PresentedDefinition, new RecordingPresenter());

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register<TestActionOp, TestOutcome>(
                PresentedDefinition,
                new RecordingPresenter()
            )
        );
    }

    [Test]
    public void FirstFailedStepAbortsRemainingPresentationAndReleasesActionAndRoot()
    {
        UnityActionPresentationCoordinator coordinator = new();
        object action = new();
        OpId rootId = new(41);
        int remainingCalls = 0;
        coordinator.Begin(action, rootId);
        coordinator.Enqueue(action, FailPresentation);
        coordinator.Enqueue(action, () => RecordPresentation(() => remainingCalls++));
        ExpectLog(LogType.Exception, new Regex("presentation failed"));

        Drain(coordinator.Drain(action));

        Assert.That(remainingCalls, Is.Zero, "Later visual steps must be abandoned.");
        Assert.That(
            coordinator.TryEnqueue(rootId, () => RecordPresentation(() => remainingCalls++)),
            Is.False,
            "The failed sequence must release its root mapping."
        );
        IEnumerator secondDrain = coordinator.Drain(action);
        Assert.That(secondDrain.MoveNext(), Is.False, "The failed sequence must be released.");
    }

    private static RuleDispatcher CreateDispatcher() =>
        new RuleDispatcherBuilder(new InMemoryRulesStore())
            .RegisterHandler<TestActionOp, TestOutcome>(new TestActionHandler())
            .UseActionLifecycle(new TestActionCatalog())
            .Build();

    private static void Drain(IEnumerator presentation)
    {
        while (presentation.MoveNext()) { }
    }

    private static IEnumerator FailPresentation()
    {
        yield return null;
        throw new InvalidOperationException("presentation failed");
    }

    private static IEnumerator RecordPresentation(Action record)
    {
        record();
        yield break;
    }

    private static void ExpectLog(LogType type, Regex message)
    {
        Type logAssert = AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEngine.TestTools.LogAssert"))
            .First(candidate => candidate != null);
        logAssert
            .GetMethod("Expect", new[] { typeof(LogType), typeof(Regex) })
            .Invoke(null, new object[] { type, message });
    }

    private readonly struct TestOutcome
    {
        public TestOutcome(int value) => Value = value;

        public int Value { get; }
    }

    private sealed class TestActionOp : ActionOp<TestOutcome>
    {
        public TestActionOp(ActionDefinitionId definitionId, int value)
            : base(UnityActionPresentationRegistryTests.Actor, definitionId) => Value = value;

        public int Value { get; }
    }

    private sealed class TestActionHandler : IOpHandler<TestActionOp, TestOutcome>
    {
        public ValueTask<TestOutcome> Handle(
            OpFrame<TestActionOp> frame,
            OpHandlerContext context
        ) => new(new TestOutcome(frame.Op.Value));
    }

    private sealed class TestActionCatalog : IActionCatalog
    {
        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
            ActionProfile.Create(ActionCost.FreeAction, Array.Empty<Trait>());
    }

    private sealed class RecordingPresenter : IUnityActionPresenter<TestActionOp, TestOutcome>
    {
        public List<string> Calls { get; } = new();
        public TestActionOp Action { get; private set; }
        public TestOutcome Outcome { get; private set; }
        public RulesSnapshot Snapshot { get; private set; }

        public IEnumerator PresentBeginning(TestActionOp action, RulesSnapshot currentSnapshot)
        {
            Calls.Add("begin");
            Action = action;
            Snapshot = currentSnapshot;
            yield break;
        }

        public IEnumerator PresentResolved(
            TestActionOp action,
            TestOutcome outcome,
            RulesSnapshot currentSnapshot
        )
        {
            Calls.Add("resolved");
            Action = action;
            Outcome = outcome;
            Snapshot = currentSnapshot;
            yield break;
        }
    }
}
