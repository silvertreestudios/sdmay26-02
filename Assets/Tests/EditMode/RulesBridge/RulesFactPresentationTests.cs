using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;

namespace Game.Rules.Unity.Tests
{
    /// <summary>
    /// Verifies typed Fact presentation, structured logging, idempotency, and push invalidation.
    /// </summary>
    public sealed class RulesFactPresentationTests
    {
        private static readonly CreatureId FirstCreature = new CreatureId("first-creature");
        private static readonly CreatureId SecondCreature = new CreatureId("second-creature");
        private static readonly RuleSource Source = RuleSource.FromSlug("presentation-test");

        [Test]
        public async Task CommittedFactPresentsLogsAndRefreshesOnlyAffectedCreatureOnce()
        {
            CommittedRuleFact commit = await CommitHealthChange(-3);
            RecordingPresenter presenter = new RecordingPresenter();
            RecordingCombatLogSink combatLog = new RecordingCombatLogSink();
            RecordingInvalidator invalidator = new RecordingInvalidator();
            RecordingProjectionSource projectionSource = new RecordingProjectionSource();
            RecordingProjectionSink projectionSink = new RecordingProjectionSink();
            RulesFactPresentation presentation = new RulesFactPresentation(
                new UnityFactPresenterRegistry().Register(presenter),
                new CombatLogFactProjector().Register(new HealthLogProjector()),
                combatLog,
                new VisibleEffectInvalidatorRegistry().Register(invalidator),
                new VisibleEffectProjectionSelector().Register(projectionSource),
                projectionSink);

            bool first = presentation.Present(commit);
            bool duplicate = presentation.Present(commit);

            Assert.That(first, Is.True);
            Assert.That(duplicate, Is.False);
            Assert.That(presenter.Values, Is.EqualTo(new[] { 7 }));
            Assert.That(combatLog.Entries, Has.Count.EqualTo(1));
            Assert.That(combatLog.Entries[0].Kind, Is.EqualTo(CombatLogEntryKind.Damage));
            Assert.That(combatLog.Entries[0].Outcome, Is.EqualTo(CombatLogOutcome.Damage));
            Assert.That(combatLog.Entries[0].Actor, Is.EqualTo(FirstCreature.Value));
            Assert.That(combatLog.Entries[0].Target, Is.EqualTo(FirstCreature.Value));
            Assert.That(combatLog.Entries[0].Action, Is.EqualTo("Test health adjustment"));
            Assert.That(combatLog.Entries[0].Tags, Does.Contain("rules"));
            Assert.That(invalidator.PreviousHealth, Is.EqualTo(10));
            Assert.That(invalidator.CurrentHealth, Is.EqualTo(7));
            Assert.That(projectionSource.RequestedCreatures, Is.EqualTo(new[] { FirstCreature }));
            Assert.That(projectionSink.RefreshedCreatures, Is.EqualTo(new[] { FirstCreature }));
            Assert.That(projectionSink.LastEffects, Has.Count.EqualTo(1));
            Assert.That(projectionSink.LastEffects[0].IsDerived, Is.True);
            Assert.That(projectionSource.RequestedCreatures, Has.No.Member(SecondCreature));
        }

        [Test]
        public async Task PresentationQueuePreservesCommitOrderAndUnregisteredFactsStaySilent()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            List<CommittedRuleFact> committed = new List<CommittedRuleFact>();
            dispatcher.FactCommitted += committed.Add;
            await dispatcher.Dispatch(new AdjustRootOp(-1));
            await dispatcher.Dispatch(new AdjustRootOp(-2));

            RecordingPresenter presenter = new RecordingPresenter();
            RecordingCombatLogSink log = new RecordingCombatLogSink();
            RulesFactPresentation presentation = new RulesFactPresentation(
                new UnityFactPresenterRegistry().Register(presenter),
                new CombatLogFactProjector(),
                log,
                new VisibleEffectInvalidatorRegistry(),
                new VisibleEffectProjectionSelector(),
                new RecordingProjectionSink());
            RulesFactPresentationQueue queue = new RulesFactPresentationQueue();
            foreach (CommittedRuleFact commit in committed)
                queue.Enqueue(commit);

            int drained = queue.Drain(presentation);

            Assert.That(drained, Is.EqualTo(2));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(presenter.Values, Is.EqualTo(new[] { 9, 7 }));
            Assert.That(log.Entries, Is.Empty);
        }

        [Test]
        public async Task PresentationQueueDefersFactsEnqueuedWhileDraining()
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            List<CommittedRuleFact> committed = new List<CommittedRuleFact>();
            dispatcher.FactCommitted += committed.Add;
            await dispatcher.Dispatch(new AdjustRootOp(-1));
            await dispatcher.Dispatch(new AdjustRootOp(-1));
            Assert.That(committed, Has.Count.EqualTo(2));

            RulesFactPresentationQueue queue = new RulesFactPresentationQueue();
            EnqueueingPresenter presenter = new EnqueueingPresenter(queue, committed[1]);
            RulesFactPresentation presentation = new RulesFactPresentation(
                new UnityFactPresenterRegistry().Register(presenter),
                new CombatLogFactProjector(),
                new RecordingCombatLogSink(),
                new VisibleEffectInvalidatorRegistry(),
                new VisibleEffectProjectionSelector(),
                new RecordingProjectionSink());
            queue.Enqueue(committed[0]);

            int firstDrain = queue.Drain(presentation);

            Assert.That(firstDrain, Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(presenter.Values, Is.EqualTo(new[] { 9 }));

            int secondDrain = queue.Drain(presentation);

            Assert.That(secondDrain, Is.EqualTo(1));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(presenter.Values, Is.EqualTo(new[] { 9, 8 }));
        }

        private static async Task<CommittedRuleFact> CommitHealthChange(int delta)
        {
            RuleDispatcher dispatcher = CreateDispatcher();
            List<CommittedRuleFact> committed = new List<CommittedRuleFact>();
            dispatcher.FactCommitted += committed.Add;

            await dispatcher.Dispatch(new AdjustRootOp(delta));

            Assert.That(committed, Has.Count.EqualTo(1));
            return committed[0];
        }

        private static RuleDispatcher CreateDispatcher()
        {
            RulesStateSeed seed = new RulesStateSeed()
                .SeedHealth(FirstCreature, new HealthState(10, 10))
                .SeedHealth(SecondCreature, new HealthState(10, 10));
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

        private sealed class HealthAdjustedFact : RuleFact
        {
            public HealthAdjustedFact(int previous, int current)
            {
                Previous = previous;
                Current = current;
            }

            public int Previous { get; }
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

        private sealed class AdjustHealthReducer : IOpReducer<AdjustHealthOp, int>
        {
            public ReductionResult<int> Reduce(
                ReductionContext<AdjustHealthOp> context,
                RulesStateDraft state,
                FactSink facts)
            {
                if (!state.Health.TryGet(FirstCreature, out HealthState previous))
                    throw new InvalidOperationException("The first creature health was not seeded.");
                int current = previous.Current + context.Op.Delta;
                state.Health.Set(
                    FirstCreature,
                    new HealthState(current, previous.Maximum, previous.Temporary));
                facts.Stage(new HealthAdjustedFact(previous.Current, current));
                return ReductionResult<int>.Accept(current);
            }
        }

        private sealed class RecordingPresenter : IUnityFactPresenter<HealthAdjustedFact>
        {
            public List<int> Values { get; } = new List<int>();

            public void Present(HealthAdjustedFact fact, CommittedRuleFact commit) =>
                Values.Add(fact.Current);
        }

        private sealed class EnqueueingPresenter : IUnityFactPresenter<HealthAdjustedFact>
        {
            private readonly CommittedRuleFact deferred;
            private readonly RulesFactPresentationQueue queue;
            private bool hasEnqueued;

            public EnqueueingPresenter(
                RulesFactPresentationQueue queue,
                CommittedRuleFact deferred)
            {
                this.queue = queue;
                this.deferred = deferred;
            }

            public List<int> Values { get; } = new List<int>();

            public void Present(HealthAdjustedFact fact, CommittedRuleFact commit)
            {
                Values.Add(fact.Current);
                if (hasEnqueued)
                    return;
                hasEnqueued = true;
                queue.Enqueue(deferred);
            }
        }

        private sealed class HealthLogProjector :
            ICombatLogFactProjector<HealthAdjustedFact>
        {
            public CombatLogEntry Project(
                HealthAdjustedFact fact,
                CommittedRuleFact commit)
            {
                CombatLogEntry entry = new CombatLogEntry
                {
                    Kind = CombatLogEntryKind.Damage,
                    Outcome = CombatLogOutcome.Damage,
                    Actor = FirstCreature.Value,
                    Target = FirstCreature.Value,
                    Action = "Test health adjustment",
                    Message = $"Health changed from {fact.Previous} to {fact.Current}."
                };
                entry.Tags.Add("rules");
                return entry;
            }
        }

        private sealed class RecordingCombatLogSink : ICombatLogSink
        {
            public List<CombatLogEntry> Entries { get; } = new List<CombatLogEntry>();

            public void Log(CombatLogEntry entry) => Entries.Add(entry);
        }

        private sealed class RecordingInvalidator :
            IVisibleEffectInvalidator<HealthAdjustedFact>
        {
            public int PreviousHealth { get; private set; }
            public int CurrentHealth { get; private set; }

            public IReadOnlyCollection<CreatureId> GetAffectedCreatures(
                HealthAdjustedFact fact,
                RulesSnapshot previousSnapshot,
                RulesSnapshot currentSnapshot)
            {
                PreviousHealth = previousSnapshot.Health[FirstCreature].Current;
                CurrentHealth = currentSnapshot.Health[FirstCreature].Current;
                return Array.AsReadOnly(new[] { FirstCreature, FirstCreature });
            }
        }

        private sealed class RecordingProjectionSource : IVisibleEffectProjectionSource
        {
            public List<CreatureId> RequestedCreatures { get; } = new List<CreatureId>();

            public IReadOnlyList<VisibleEffectProjection> Select(
                RulesSnapshot snapshot,
                CreatureId creature)
            {
                RequestedCreatures.Add(creature);
                return Array.AsReadOnly(new[]
                {
                    new VisibleEffectProjection(
                        new RuleDefinitionId("derived-test-effect"),
                        Source,
                        "Derived test effect",
                        true)
                });
            }
        }

        private sealed class RecordingProjectionSink : IVisibleEffectProjectionSink
        {
            public List<CreatureId> RefreshedCreatures { get; } = new List<CreatureId>();
            public IReadOnlyList<VisibleEffectProjection> LastEffects { get; private set; } =
                Array.AsReadOnly(Array.Empty<VisibleEffectProjection>());

            public void Refresh(
                CreatureId creature,
                IReadOnlyList<VisibleEffectProjection> effects)
            {
                RefreshedCreatures.Add(creature);
                LastEffects = effects;
            }
        }
    }
}
