using System;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Describes one immutable Fact at the exact rules-state transition that committed it.
    /// </summary>
    /// <remarks>
    /// Presentation adapters use this envelope to compare state immediately before and after the
    /// reducer commit without reading Unity objects or becoming rules authorities. All Facts from
    /// one atomic reduction share the same snapshot pair. For presentation-eligible roots, the
    /// enclosing dispatcher publishes envelopes in commit order before rule listeners may begin
    /// causally linked roots. A structurally invalid root is not presentation-eligible.
    /// </remarks>
    public sealed class CommittedRuleFact
    {
        /// <summary>
        /// Initializes a committed-Fact envelope from dispatcher-owned commit data.
        /// </summary>
        /// <param name="fact">The stamped domain Fact produced by the accepted reduction.</param>
        /// <param name="previousSnapshot">The immutable state immediately before the reduction.</param>
        /// <param name="currentSnapshot">The immutable state immediately after the reduction.</param>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="fact"/> is unstamped or the snapshots do not describe an advancing commit.
        /// </exception>
        internal CommittedRuleFact(
            RuleFact fact,
            RulesSnapshot previousSnapshot,
            RulesSnapshot currentSnapshot)
        {
            Fact = fact ?? throw new ArgumentNullException(nameof(fact));
            PreviousSnapshot = previousSnapshot ??
                throw new ArgumentNullException(nameof(previousSnapshot));
            CurrentSnapshot = currentSnapshot ??
                throw new ArgumentNullException(nameof(currentSnapshot));
            if (!fact.IsStamped)
                throw new ArgumentException("A presentation envelope requires a stamped Fact.", nameof(fact));
            if (currentSnapshot.Version <= previousSnapshot.Version)
            {
                throw new ArgumentException(
                    "A committed Fact requires a current snapshot newer than its previous snapshot.",
                    nameof(currentSnapshot));
            }
        }

        /// <summary>
        /// Gets the committed, dispatcher-stamped domain Fact.
        /// </summary>
        public RuleFact Fact { get; }

        /// <summary>
        /// Gets the immutable rules state immediately before the Fact's atomic reduction committed.
        /// </summary>
        public RulesSnapshot PreviousSnapshot { get; }

        /// <summary>
        /// Gets the immutable rules state immediately after the Fact's atomic reduction committed.
        /// </summary>
        public RulesSnapshot CurrentSnapshot { get; }
    }

    /// <summary>
    /// Exposes the narrow rules-runtime surface used by Unity, AI, and other external adapters.
    /// </summary>
    /// <remarks>
    /// Adapters may dispatch externally allowed root operations and observe immutable committed
    /// data. They cannot access reducer drafts, nested-only operations, or any state mutation API.
    /// </remarks>
    public interface IRulesRuntime
    {
        /// <summary>
        /// Gets the latest immutable committed rules state.
        /// </summary>
        RulesSnapshot Snapshot { get; }

        /// <summary>
        /// Occurs once for each presentation-eligible committed Fact, in commit order.
        /// </summary>
        /// <remarks>
        /// Subscribers must return promptly; Unity subscribers should enqueue presentation work
        /// instead of waiting for animation. Facts from a structurally invalid root are not
        /// published, matching the runtime's post-commit listener policy. The event belongs to this
        /// runtime instance and must be unsubscribed when an adapter is disabled or destroyed.
        /// </remarks>
        event Action<CommittedRuleFact> FactCommitted;

        /// <summary>
        /// Dispatches an externally allowed operation as a serialized root resolution.
        /// </summary>
        /// <typeparam name="TResult">The successful value type declared by the operation.</typeparam>
        /// <param name="op">The immutable operation to resolve.</param>
        /// <returns>The structural result after rules resolution and post-commit listeners finish.</returns>
        ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op);
    }
}
