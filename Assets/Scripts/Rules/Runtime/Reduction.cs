using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RulesRuntime.EditMode.Tests")]

namespace Game.Rules.Runtime
{
    /// <summary>
    /// The trusted reducer boundary supplied by the dispatcher.
    /// </summary>
    public sealed class ReductionContext<TOp>
    {
        public TOp Op { get; }
        public OpId SourceOpId { get; }
        public OpId RootOpId { get; }
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

    public abstract class RuleFact
    {
        public FactId Id { get; private set; }
        public OpId SourceOpId { get; private set; }
        public OpId RootOpId { get; private set; }
        public RuleSource Source { get; private set; }
        public bool IsStamped { get; private set; }

        internal void Stamp(FactId id, OpId sourceOpId, OpId rootOpId, RuleSource source)
        {
            if (IsStamped)
                throw new InvalidOperationException("A Rule Fact cannot be stamped more than once.");

            Id = id;
            SourceOpId = sourceOpId;
            RootOpId = rootOpId;
            Source = source;
            IsStamped = true;
        }
    }

    /// <summary>
    /// Stages immutable domain Facts. Identity and provenance are added only after a reduction is accepted.
    /// </summary>
    public sealed class FactSink
    {
        private readonly List<RuleFact> stagedFacts = new List<RuleFact>();

        internal FactSink()
        {
        }

        internal int Count => stagedFacts.Count;

        public void Stage(RuleFact fact)
        {
            if (fact == null)
                throw new ArgumentNullException(nameof(fact));
            if (fact.IsStamped)
                throw new InvalidOperationException("Feature code cannot stage a pre-stamped Fact.");
            if (stagedFacts.Exists(staged => ReferenceEquals(staged, fact)))
                throw new InvalidOperationException("The same Rule Fact instance cannot be staged more than once.");

            stagedFacts.Add(fact);
        }

        internal RuleFact[] GetStagedFacts()
        {
            return stagedFacts.ToArray();
        }
    }

    public sealed class ReductionResult<TResult>
    {
        private static readonly IReadOnlyList<RuleFact> NoFacts = Array.AsReadOnly(Array.Empty<RuleFact>());
        private readonly RulesSnapshot snapshot;

        public bool IsAccepted { get; }
        public bool IsRejected => !IsAccepted;
        public bool DidCommit { get; }
        public TResult Value { get; }
        public string RejectionReason { get; }
        public IReadOnlyList<RuleFact> Facts { get; }

        public RulesSnapshot Snapshot => snapshot ?? throw new InvalidOperationException(
            "Only a result returned by IRulesStore has a committed snapshot.");

        private ReductionResult(
            bool isAccepted,
            TResult value,
            string rejectionReason,
            bool didCommit,
            RulesSnapshot snapshot,
            IReadOnlyList<RuleFact> facts)
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
                throw new ArgumentException("A rejected reduction requires a reason.", nameof(reason));
            return new ReductionResult<TResult>(false, default, reason, false, null, NoFacts);
        }

        internal ReductionResult<TResult> Complete(
            RulesSnapshot completedSnapshot,
            IReadOnlyList<RuleFact> facts,
            bool didCommit)
        {
            return new ReductionResult<TResult>(
                IsAccepted,
                Value,
                RejectionReason,
                didCommit,
                completedSnapshot,
                facts);
        }
    }

    public interface IOpReducer<TOp, TResult>
        where TOp : IRuleOp<TResult>
    {
        ReductionResult<TResult> Reduce(
            ReductionContext<TOp> context,
            RulesStateDraft state,
            FactSink facts);
    }

    public interface IRulesStore
    {
        RulesSnapshot Snapshot { get; }

        ReductionResult<TResult> Reduce<TOp, TResult>(
            ReductionContext<TOp> context,
            IOpReducer<TOp, TResult> reducer)
            where TOp : IRuleOp<TResult>;
    }

    public sealed class InMemoryRulesStore : IRulesStore
    {
        private readonly object gate = new object();
        private RulesState state;
        private long nextFactId = 1;
        private bool isReducing;

        public InMemoryRulesStore()
            : this(new RulesStateSeed())
        {
        }

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

        public ReductionResult<TResult> Reduce<TOp, TResult>(
            ReductionContext<TOp> context,
            IOpReducer<TOp, TResult> reducer)
            where TOp : IRuleOp<TResult>
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (reducer == null)
                throw new ArgumentNullException(nameof(reducer));

            lock (gate)
            {
                if (isReducing)
                    throw new InvalidOperationException("A rules store cannot begin a nested reduction while another reduction is in progress.");

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
                        return decision.Complete(startingState.Snapshot, Array.AsReadOnly(Array.Empty<RuleFact>()), false);

                    if (!draft.IsDirty && factSink.Count == 0)
                        return decision.Complete(startingState.Snapshot, Array.AsReadOnly(Array.Empty<RuleFact>()), false);

                    if (draft.IsDirty && factSink.Count == 0)
                        throw new InvalidOperationException("A committed state change requires at least one domain Fact.");

                    RuleFact[] committedFacts = factSink.GetStagedFacts();
                    long pendingFactId = nextFactId;
                    foreach (RuleFact fact in committedFacts)
                    {
                        fact.Stamp(
                            new FactId(pendingFactId++),
                            context.SourceOpId,
                            context.RootOpId,
                            context.Source);
                    }

                    RulesState committedState = new RulesState(draft.Build(startingState.Version + 1));
                    state = committedState;
                    nextFactId = pendingFactId;

                    return decision.Complete(
                        committedState.Snapshot,
                        Array.AsReadOnly(committedFacts),
                        true);
                }
                finally
                {
                    isReducing = false;
                }
            }
        }
    }
}
