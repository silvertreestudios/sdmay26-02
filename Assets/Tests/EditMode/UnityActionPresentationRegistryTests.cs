using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;

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

        Assert.That(first, Is.TypeOf<ResolvedOpResult<TestOutcome>>());
        Assert.That(presenter.Calls, Is.EqualTo(1));
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

    private static RuleDispatcher CreateDispatcher() =>
        new RuleDispatcherBuilder(new InMemoryRulesStore())
            .RegisterHandler<TestActionOp, TestOutcome>(new TestActionHandler())
            .UseActionLifecycle(new TestActionCatalog())
            .Build();

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
        public int Calls { get; private set; }
        public TestActionOp Action { get; private set; }
        public TestOutcome Outcome { get; private set; }
        public RulesSnapshot Snapshot { get; private set; }

        public void Present(TestActionOp action, TestOutcome outcome, RulesSnapshot currentSnapshot)
        {
            Calls++;
            Action = action;
            Outcome = outcome;
            Snapshot = currentSnapshot;
        }
    }
}
