using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RulesRuntime.EditMode.Tests")]

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Carries the typed operation and dispatcher-owned provenance into a rules reducer.
    /// </summary>
    /// <typeparam name="TOp">The operation being reduced.</typeparam>
    /// <remarks>
    /// Reducers receive mutable state and fact staging through separate parameters. Keeping identity
    /// and provenance in this context prevents feature code from forging operation or rule-source data.
    /// </remarks>
    public sealed class ReductionContext<TOp>
    {
        /// <summary>
        /// Gets the operation being reduced.
        /// </summary>
        public TOp Op { get; }

        /// <summary>
        /// Gets the operation frame that directly requested this reduction.
        /// </summary>
        public OpId SourceOpId { get; }

        /// <summary>
        /// Gets the root operation that owns the complete resolution.
        /// </summary>
        public OpId RootOpId { get; }

        /// <summary>
        /// Gets the rule source recorded with committed Fact delivery provenance.
        /// </summary>
        public RuleSource Source { get; }

        internal ReductionContext(TOp op, OpId sourceOpId, OpId rootOpId, RuleSource source)
        {
            if (ReferenceEquals(op, null))
                throw new ArgumentNullException(nameof(op));
            if (sourceOpId.IsEmpty)
                throw new ArgumentException("A source Op ID is required.", nameof(sourceOpId));
            if (rootOpId.IsEmpty)
                throw new ArgumentException("A root Op ID is required.", nameof(rootOpId));
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));

            Op = op;
            SourceOpId = sourceOpId;
            RootOpId = rootOpId;
            Source = source;
        }
    }

    /// <summary>
    /// Marks an immutable payload that describes a committed rules transition or occurrence.
    /// </summary>
    /// <remarks>
    /// Facts contain domain data only. The dispatcher owns commit, source, root, and delivery
    /// provenance separately so publishing a Fact never mutates the payload observed by clients.
    /// </remarks>
    public abstract class RuleFact { }

    /// <summary>
    /// Stages immutable domain Facts for an atomic reducer commit.
    /// </summary>
    public sealed class FactSink
    {
        private readonly List<RuleFact> stagedFacts = new List<RuleFact>();

        internal FactSink() { }

        internal int Count => stagedFacts.Count;

        public void Stage(RuleFact fact)
        {
            if (fact == null)
                throw new ArgumentNullException(nameof(fact));
            if (stagedFacts.Exists(staged => ReferenceEquals(staged, fact)))
                throw new InvalidOperationException(
                    "The same Rule Fact instance cannot be staged more than once."
                );

            stagedFacts.Add(fact);
        }

        internal RuleFact[] GetStagedFacts()
        {
            return stagedFacts.ToArray();
        }
    }

    public sealed class ReductionResult<TResult>
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts = Array.AsReadOnly(
            Array.Empty<RuleFact>()
        );
        private readonly RulesSnapshot snapshot;

        public bool IsAccepted { get; }
        public bool IsRejected => !IsAccepted;
        public bool DidCommit { get; }
        public TResult Value { get; }
        public string RejectionReason { get; }
        public IReadOnlyList<RuleFact> Facts { get; }

        public RulesSnapshot Snapshot =>
            snapshot
            ?? throw new InvalidOperationException(
                "Only a result returned by IRulesStore has a committed snapshot."
            );

        private ReductionResult(
            bool isAccepted,
            TResult value,
            string rejectionReason,
            bool didCommit,
            RulesSnapshot snapshot,
            IReadOnlyList<RuleFact> facts
        )
        {
            IsAccepted = isAccepted;
            Value = value;
            RejectionReason = rejectionReason;
            DidCommit = didCommit;
            this.snapshot = snapshot;
            Facts = facts ?? NoFacts;
        }

        public static ReductionResult<TResult> Accept(TResult value)
        {
            return new ReductionResult<TResult>(true, value, null, false, null, NoFacts);
        }

        public static ReductionResult<TResult> Reject(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "A rejected reduction requires a reason.",
                    nameof(reason)
                );
            return new ReductionResult<TResult>(false, default, reason, false, null, NoFacts);
        }

        internal ReductionResult<TResult> Complete(
            RulesSnapshot completedSnapshot,
            IReadOnlyList<RuleFact> facts,
            bool didCommit
        )
        {
            return new ReductionResult<TResult>(
                IsAccepted,
                Value,
                RejectionReason,
                didCommit,
                completedSnapshot,
                facts
            );
        }
    }

    /// <summary>
    /// Applies one typed operation to a transactional rules-state draft.
    /// </summary>
    /// <typeparam name="TOp">The operation type accepted by the reducer.</typeparam>
    /// <typeparam name="TResult">The value returned when the reduction is accepted.</typeparam>
    /// <remarks>
    /// A reducer may update <see cref="RulesStateDraft"/> and stage facts, but those changes become
    /// visible only when it returns an accepted <see cref="ReductionResult{TResult}"/>.
    /// </remarks>
    public interface IOpReducer<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        /// <summary>
        /// Validates the operation and stages its state changes and domain facts.
        /// </summary>
        /// <param name="context">The typed operation and trusted dispatch provenance.</param>
        /// <param name="state">An isolated draft of the current rules state.</param>
        /// <param name="facts">The sink for facts that justify an accepted state transition.</param>
        /// <returns>An accepted value or a rejected result with a caller-facing reason.</returns>
        ReductionResult<TResult> Reduce(
            ReductionContext<TOp> context,
            RulesStateDraft state,
            FactSink facts
        );
    }

    /// <summary>
    /// Provides atomic rules snapshots and transactional reducer execution.
    /// </summary>
    public interface IRulesStore
    {
        /// <summary>
        /// Gets the latest immutable committed state.
        /// </summary>
        RulesSnapshot Snapshot { get; }

        /// <summary>
        /// Executes a reducer against an isolated draft and atomically commits accepted changes.
        /// </summary>
        /// <typeparam name="TOp">The operation type consumed by the reducer.</typeparam>
        /// <typeparam name="TResult">The accepted value produced by the reducer.</typeparam>
        /// <param name="context">The typed operation and trusted dispatch provenance.</param>
        /// <param name="reducer">The reducer that validates and stages the transition.</param>
        /// <returns>
        /// The completed reduction, including the committed snapshot and immutable Facts when a
        /// commit occurred.
        /// </returns>
        ReductionResult<TResult> Reduce<TOp, TResult>(
            ReductionContext<TOp> context,
            IOpReducer<TOp, TResult> reducer
        )
            where TOp : IRuleOp<TResult>;
    }

    public sealed class InMemoryRulesStore : IRulesStore
    {
        private readonly object gate = new object();
        private readonly HashSet<RuleFact> committedFactReferences = new HashSet<RuleFact>(
            ReferenceEqualityComparer<RuleFact>.Instance
        );
        private RulesState state;
        private bool isReducing;

        public InMemoryRulesStore()
            : this(new RulesStateSeed()) { }

        public InMemoryRulesStore(RulesStateSeed seed)
        {
            state = new RulesState(seed ?? throw new ArgumentNullException(nameof(seed)));
        }

        public RulesSnapshot Snapshot
        {
            get
            {
                lock (gate)
                    return state.Snapshot;
            }
        }

        /// <inheritdoc/>
        public ReductionResult<TResult> Reduce<TOp, TResult>(
            ReductionContext<TOp> context,
            IOpReducer<TOp, TResult> reducer
        )
            where TOp : IRuleOp<TResult>
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (reducer == null)
                throw new ArgumentNullException(nameof(reducer));

            lock (gate)
            {
                if (isReducing)
                    throw new InvalidOperationException(
                        "A rules store cannot begin a nested reduction while another reduction is in progress."
                    );

                isReducing = true;
                try
                {
                    RulesState startingState = state;
                    RulesStateDraft draft = startingState.CreateDraft();
                    FactSink factSink = new FactSink();
                    ReductionResult<TResult> decision = reducer.Reduce(context, draft, factSink);
                    if (decision == null)
                        throw new InvalidOperationException("A reducer returned null.");

                    if (decision.IsRejected)
                        return decision.Complete(
                            startingState.Snapshot,
                            Array.AsReadOnly(Array.Empty<RuleFact>()),
                            false
                        );

                    if (!draft.IsDirty && factSink.Count == 0)
                        return decision.Complete(
                            startingState.Snapshot,
                            Array.AsReadOnly(Array.Empty<RuleFact>()),
                            false
                        );

                    if (draft.IsDirty && factSink.Count == 0)
                        throw new InvalidOperationException(
                            "A committed state change requires at least one domain Fact."
                        );

                    RuleFact[] committedFacts = factSink.GetStagedFacts();
                    foreach (RuleFact fact in committedFacts)
                    {
                        if (committedFactReferences.Contains(fact))
                            throw new InvalidOperationException(
                                "The same Rule Fact instance cannot commit more than once."
                            );
                    }

                    RulesState committedState = new RulesState(
                        draft.Build(startingState.Version + 1)
                    );
                    state = committedState;
                    foreach (RuleFact fact in committedFacts)
                        committedFactReferences.Add(fact);

                    return decision.Complete(
                        committedState.Snapshot,
                        Array.AsReadOnly(committedFacts),
                        true
                    );
                }
                finally
                {
                    isReducing = false;
                }
            }
        }
    }
}
