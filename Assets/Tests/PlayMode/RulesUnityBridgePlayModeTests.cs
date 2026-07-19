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
/// Verifies encounter identity composition and bridge subscription lifecycle in Unity PlayMode.
/// </summary>
public sealed class RulesUnityBridgePlayModeTests
{
    private static readonly CreatureId Creature = new CreatureId("bridge-creature");
    private static readonly RuleSource Source = RuleSource.FromSlug("bridge-test");

    [UnityTest]
    public IEnumerator CompositionMapsMultipleCombatantsWithoutSeedingLegacyOwnedState()
    {
        GameObject first = new GameObject("first mapped combatant");
        GameObject second = new GameObject("second mapped combatant");
        try
        {
            RulesCombatService service = new RulesCombatService(
                new RuleDispatcherBuilder(new InMemoryRulesStore()).Build(),
                new UnityRulesIdentityMap());
            CreatureId firstId = new CreatureId("fixture-creature-a");
            CreatureId secondId = new CreatureId("fixture-creature-b");

            service.Identities.RegisterCreature(first, firstId);
            service.Identities.RegisterCreature(second, secondId);

            yield return null;

            Assert.That(service.Identities.GetCreatureId(first), Is.EqualTo(firstId));
            Assert.That(service.Identities.GetCreatureObject(secondId), Is.SameAs(second));
            Assert.That(service.Snapshot.Creatures, Is.Empty);
            Assert.That(service.Snapshot.Health, Is.Empty);
            Assert.That(service.Snapshot.Positions, Is.Empty);
            Assert.That(service.Snapshot.ActionEconomy, Is.Empty);
            Assert.That(service.Snapshot.Equipment, Is.Empty);
            Assert.That(service.Snapshot.Conditions, Is.Empty);
            Assert.That(service.Snapshot.ActiveEffects, Is.Empty);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }

    [UnityTest]
    public IEnumerator BridgePresentsOnceAndUnsubscribesAcrossDisableAndDestroy()
    {
        GameObject host = new GameObject("rules bridge host");
        host.SetActive(false);
        RulesUnityBridge bridge = host.AddComponent<RulesUnityBridge>();
        RuleDispatcher dispatcher = CreateDispatcher();
        RecordingPresenter presenter = new RecordingPresenter();
        RecordingCombatLogSink combatLog = new RecordingCombatLogSink();
        RulesFactPresentation presentation = new RulesFactPresentation(
            new UnityFactPresenterRegistry().Register(presenter),
            new CombatLogFactProjector().Register(new HealthLogProjector()),
            combatLog,
            new VisibleEffectInvalidatorRegistry(),
            new VisibleEffectProjectionSelector(),
            new RecordingProjectionSink());
        bridge.Configure(dispatcher, presentation);
        host.SetActive(true);

        Task first = dispatcher.Dispatch(new AdjustRootOp(-1)).AsTask();
        yield return Await(first);
        yield return null;

        Assert.That(presenter.Values, Is.EqualTo(new[] { 9 }));
        Assert.That(combatLog.Entries, Has.Count.EqualTo(1));
        Assert.That(combatLog.Entries[0].Kind, Is.EqualTo(CombatLogEntryKind.Damage));
        Assert.That(combatLog.Entries[0].Actor, Is.EqualTo(Creature.Value));
        Assert.That(combatLog.Entries[0].Target, Is.EqualTo(Creature.Value));
        Assert.That(combatLog.Entries[0].Message, Is.EqualTo("Health changed from 10 to 9."));
        Assert.That(bridge.PendingPresentationCount, Is.Zero);

        host.SetActive(false);
        Task whileDisabled = dispatcher.Dispatch(new AdjustRootOp(-1)).AsTask();
        yield return Await(whileDisabled);
        host.SetActive(true);
        yield return null;

        Assert.That(presenter.Values, Is.EqualTo(new[] { 9 }),
            "A disabled bridge must not retain a runtime event subscription.");
        Assert.That(combatLog.Entries, Has.Count.EqualTo(1));

        Task afterReenable = dispatcher.Dispatch(new AdjustRootOp(-1)).AsTask();
        yield return Await(afterReenable);
        yield return null;

        Assert.That(presenter.Values, Is.EqualTo(new[] { 9, 7 }),
            "Re-enabling must restore exactly one subscription.");
        Assert.That(combatLog.Entries, Has.Count.EqualTo(2));
        Assert.That(combatLog.Entries[1].Message, Is.EqualTo("Health changed from 8 to 7."));

        UnityEngine.Object.Destroy(host);
        yield return null;
        Task afterDestroy = dispatcher.Dispatch(new AdjustRootOp(-1)).AsTask();
        yield return Await(afterDestroy);
        yield return null;

        Assert.That(presenter.Values, Is.EqualTo(new[] { 9, 7 }),
            "Destroying the bridge must remove its runtime event subscription.");
        Assert.That(combatLog.Entries, Has.Count.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator ResolvedNoFactRootProducesNoPresentationSideEffects()
    {
        GameObject host = new GameObject("no Fact bridge host");
        host.SetActive(false);
        RulesUnityBridge bridge = host.AddComponent<RulesUnityBridge>();
        RuleDispatcher dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore())
            .RegisterHandler<NoFactOp, int>(new NoFactHandler())
            .Build();
        RecordingCombatLogSink combatLog = new RecordingCombatLogSink();
        RecordingProjectionSink projections = new RecordingProjectionSink();
        bridge.Configure(
            dispatcher,
            new RulesFactPresentation(
                new UnityFactPresenterRegistry(),
                new CombatLogFactProjector(),
                combatLog,
                new VisibleEffectInvalidatorRegistry(),
                new VisibleEffectProjectionSelector(),
                projections));
        host.SetActive(true);

        Task task = dispatcher.Dispatch(new NoFactOp()).AsTask();
        yield return Await(task);
        yield return null;

        Assert.That(bridge.PendingPresentationCount, Is.Zero);
        Assert.That(combatLog.EntryCount, Is.Zero);
        Assert.That(projections.RefreshCount, Is.Zero);
        UnityEngine.Object.Destroy(host);
    }

    private static IEnumerator Await(Task task)
    {
        while (!task.IsCompleted)
            yield return null;
        if (task.IsFaulted)
        {
            if (task.Exception is Exception failure)
                throw failure;
            throw new InvalidOperationException("The rules task failed without an exception.");
        }
    }

    private static RuleDispatcher CreateDispatcher()
    {
        RulesStateSeed seed = new RulesStateSeed()
            .SeedHealth(Creature, new HealthState(10, 10));
        return new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
            .RegisterHandler<AdjustRootOp, int>(new AdjustRootHandler())
            .RegisterReducer<AdjustHealthOp, int>(new AdjustHealthReducer(), Source)
            .Build();
    }

    private sealed class AdjustRootOp : IRuleOp<int>
    {
        public AdjustRootOp(int delta) => Delta = delta;
        public int Delta { get; }
    }

    private sealed class AdjustHealthOp : IRuleOp<int>
    {
        public AdjustHealthOp(int delta) => Delta = delta;
        public int Delta { get; }
    }

    private sealed class NoFactOp : IRuleOp<int>
    {
    }

    private sealed class HealthAdjustedFact : RuleFact
    {
        public HealthAdjustedFact(int current) => Current = current;
        public int Current { get; }
    }

    private sealed class AdjustRootHandler : IOpHandler<AdjustRootOp, int>
    {
        public async ValueTask<int> Handle(
            OpFrame<AdjustRootOp> frame,
            OpHandlerContext context)
        {
            OpResult<int> result = await context.Dispatch(
                new AdjustHealthOp(frame.Op.Delta));
            return ((ResolvedOpResult<int>)result).Value;
        }
    }

    private sealed class NoFactHandler : IOpHandler<NoFactOp, int>
    {
        public ValueTask<int> Handle(
            OpFrame<NoFactOp> frame,
            OpHandlerContext context) => new ValueTask<int>(1);
    }

    private sealed class AdjustHealthReducer : IOpReducer<AdjustHealthOp, int>
    {
        public ReductionResult<int> Reduce(
            ReductionContext<AdjustHealthOp> context,
            RulesStateDraft state,
            FactSink facts)
        {
            if (!state.Health.TryGet(Creature, out HealthState previous))
                throw new InvalidOperationException("The bridge test health was not seeded.");
            int current = previous.Current + context.Op.Delta;
            state.Health.Set(
                Creature,
                new HealthState(current, previous.Maximum, previous.Temporary));
            facts.Stage(new HealthAdjustedFact(current));
            return ReductionResult<int>.Accept(current);
        }
    }

    private sealed class RecordingPresenter : IUnityFactPresenter<HealthAdjustedFact>
    {
        public List<int> Values { get; } = new List<int>();

        public void Present(HealthAdjustedFact fact, CommittedRuleFact commit) =>
            Values.Add(fact.Current);
    }

    private sealed class HealthLogProjector :
        ICombatLogFactProjector<HealthAdjustedFact>
    {
        public CombatLogEntry Project(
            HealthAdjustedFact fact,
            CommittedRuleFact commit)
        {
            int previous = commit.PreviousSnapshot.Health[Creature].Current;
            return new CombatLogEntry
            {
                Kind = CombatLogEntryKind.Damage,
                Outcome = CombatLogOutcome.Damage,
                Actor = Creature.Value,
                Target = Creature.Value,
                Action = "Test health adjustment",
                Message = $"Health changed from {previous} to {fact.Current}."
            };
        }
    }

    private sealed class RecordingCombatLogSink : ICombatLogSink
    {
        public List<CombatLogEntry> Entries { get; } = new List<CombatLogEntry>();

        public int EntryCount => Entries.Count;

        public void Log(CombatLogEntry entry) => Entries.Add(entry);
    }

    private sealed class RecordingProjectionSink : IVisibleEffectProjectionSink
    {
        public int RefreshCount { get; private set; }

        public void Refresh(
            CreatureId creature,
            IReadOnlyList<VisibleEffectProjection> effects) => RefreshCount++;
    }
}
