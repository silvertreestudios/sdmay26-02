using System;
using System.Collections.Generic;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Applies one committed Fact to typed presenters, structured logging, and selective projections.
    /// </summary>
    /// <remarks>
    /// One coordinator belongs to one rules-runtime encounter. Its Fact-ID guard prevents duplicate
    /// animation, logging, and HUD refresh if an envelope is enqueued more than once. Presentation
    /// has read-only snapshots and cannot write back into <see cref="RulesState"/>.
    /// </remarks>
    public sealed class RulesFactPresentation
    {
        private readonly HashSet<FactId> presentedFacts = new HashSet<FactId>();
        private readonly UnityFactPresenterRegistry presenters;
        private readonly CombatLogFactProjector combatLogProjector;
        private readonly ICombatLogSink combatLog;
        private readonly VisibleEffectInvalidatorRegistry invalidators;
        private readonly VisibleEffectProjectionSelector visibleEffects;
        private readonly IVisibleEffectProjectionSink visibleEffectSink;

        /// <summary>
        /// Initializes the complete Fact-driven presentation pipeline.
        /// </summary>
        /// <param name="presenters">Typed Unity side-effect presenters.</param>
        /// <param name="combatLogProjector">Typed structured-log projectors.</param>
        /// <param name="combatLog">The structured combat-log sink.</param>
        /// <param name="invalidators">Typed push-based visible-effect invalidators.</param>
        /// <param name="visibleEffects">The complete stored-and-derived effect selector.</param>
        /// <param name="visibleEffectSink">The HUD or token projection sink.</param>
        /// <exception cref="ArgumentNullException">Any dependency is <see langword="null"/>.</exception>
        public RulesFactPresentation(
            UnityFactPresenterRegistry presenters,
            CombatLogFactProjector combatLogProjector,
            ICombatLogSink combatLog,
            VisibleEffectInvalidatorRegistry invalidators,
            VisibleEffectProjectionSelector visibleEffects,
            IVisibleEffectProjectionSink visibleEffectSink)
        {
            this.presenters = presenters ?? throw new ArgumentNullException(nameof(presenters));
            this.combatLogProjector = combatLogProjector ??
                throw new ArgumentNullException(nameof(combatLogProjector));
            this.combatLog = combatLog ?? throw new ArgumentNullException(nameof(combatLog));
            this.invalidators = invalidators ?? throw new ArgumentNullException(nameof(invalidators));
            this.visibleEffects = visibleEffects ??
                throw new ArgumentNullException(nameof(visibleEffects));
            this.visibleEffectSink = visibleEffectSink ??
                throw new ArgumentNullException(nameof(visibleEffectSink));
        }

        /// <summary>
        /// Presents one committed Fact at most once for this encounter.
        /// </summary>
        /// <param name="commit">The required committed Fact and snapshot pair.</param>
        /// <returns>
        /// <see langword="true"/> when presentation began; <see langword="false"/> when the Fact ID
        /// was already presented.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="commit"/> is <see langword="null"/>.</exception>
        public bool Present(CommittedRuleFact commit)
        {
            if (commit == null)
                throw new ArgumentNullException(nameof(commit));
            if (!presentedFacts.Add(commit.Fact.Id))
                return false;

            presenters.Present(commit);
            foreach (CombatLogEntry entry in combatLogProjector.Project(commit))
                combatLog.Log(entry);

            foreach (CreatureId creature in invalidators.GetAffectedCreatures(commit))
            {
                visibleEffectSink.Refresh(
                    creature,
                    visibleEffects.Select(commit.CurrentSnapshot, creature));
            }
            return true;
        }
    }

    /// <summary>
    /// Buffers committed Facts so dispatcher callbacks never wait for Unity presentation work.
    /// </summary>
    /// <remarks>
    /// Enqueue is safe when a rules runtime completes work away from Unity's main thread. Draining
    /// is performed by <see cref="RulesUnityBridge"/> on Unity's update loop and preserves commit order.
    /// The queue does not poll snapshots or visible-effect selectors when it is empty.
    /// </remarks>
    public sealed class RulesFactPresentationQueue
    {
        private readonly object gate = new object();
        private readonly Queue<CommittedRuleFact> pending = new Queue<CommittedRuleFact>();

        /// <summary>
        /// Gets the number of committed Facts awaiting presentation.
        /// </summary>
        public int Count
        {
            get
            {
                lock (gate)
                    return pending.Count;
            }
        }

        /// <summary>
        /// Adds one immutable committed Fact to the end of the presentation queue.
        /// </summary>
        /// <param name="commit">The required dispatcher-owned commit envelope.</param>
        /// <exception cref="ArgumentNullException"><paramref name="commit"/> is <see langword="null"/>.</exception>
        public void Enqueue(CommittedRuleFact commit)
        {
            if (commit == null)
                throw new ArgumentNullException(nameof(commit));
            lock (gate)
                pending.Enqueue(commit);
        }

        /// <summary>
        /// Drains the currently queued Facts in commit order through one presentation coordinator.
        /// </summary>
        /// <param name="presentation">The required encounter presentation coordinator.</param>
        /// <returns>The number of queued envelopes consumed, including duplicate Fact IDs.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="presentation"/> is <see langword="null"/>.</exception>
        public int Drain(RulesFactPresentation presentation)
        {
            if (presentation == null)
                throw new ArgumentNullException(nameof(presentation));

            int drained = 0;
            while (true)
            {
                CommittedRuleFact commit;
                lock (gate)
                {
                    if (pending.Count == 0)
                        break;
                    commit = pending.Dequeue();
                }
                presentation.Present(commit);
                drained++;
            }
            return drained;
        }
    }

    /// <summary>
    /// Creates the default presentation pipeline for the current scene adapters.
    /// </summary>
    public static class RulesPresentationComposition
    {
        /// <summary>
        /// Builds a presentation coordinator that logs available foundation Facts and leaves
        /// feature-specific presenters and effect projections for their vertical migrations.
        /// </summary>
        /// <returns>An encounter-scoped Fact presentation coordinator.</returns>
        public static RulesFactPresentation CreateDefault()
        {
            CombatLogFactProjector combatLog = new CombatLogFactProjector()
                .Register(new ActionCostSpentFactProjector());
            return new RulesFactPresentation(
                new UnityFactPresenterRegistry(),
                combatLog,
                new SceneCombatLogSink(),
                new VisibleEffectInvalidatorRegistry(),
                new VisibleEffectProjectionSelector(),
                NoVisibleEffectProjectionSink.Instance);
        }

        private sealed class ActionCostSpentFactProjector :
            ICombatLogFactProjector<ActionCostSpentFact>
        {
            public CombatLogEntry Project(
                ActionCostSpentFact fact,
                CommittedRuleFact commit)
            {
                string cost = fact.Cost.Kind == ActionCostKind.Reaction
                    ? "their reaction"
                    : fact.Cost.Amount == 1
                        ? "1 action"
                        : $"{fact.Cost.Amount} actions";
                CombatLogEntry entry = new CombatLogEntry
                {
                    Kind = CombatLogEntryKind.System,
                    Outcome = CombatLogOutcome.System,
                    Actor = fact.Actor.Value,
                    Action = "Spend action cost",
                    Message = $"{fact.Actor.Value} spent {cost}.",
                };
                entry.Tags.Add("rules");
                entry.Tags.Add("action-cost");
                return entry;
            }
        }

        private sealed class SceneCombatLogSink : ICombatLogSink
        {
            public void Log(CombatLogEntry entry)
            {
                if (entry == null)
                    throw new ArgumentNullException(nameof(entry));
                if (CombatLogInterface.TryGetInstance(out CombatLogInterface value))
                    value.LogEntry(entry);
            }
        }

        private sealed class NoVisibleEffectProjectionSink : IVisibleEffectProjectionSink
        {
            public static NoVisibleEffectProjectionSink Instance { get; } =
                new NoVisibleEffectProjectionSink();

            private NoVisibleEffectProjectionSink()
            {
            }

            public void Refresh(
                CreatureId creature,
                IReadOnlyList<VisibleEffectProjection> effects)
            {
            }
        }
    }
}
