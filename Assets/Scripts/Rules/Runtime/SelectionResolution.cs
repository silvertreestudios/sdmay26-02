using System.Threading;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Describes one Unity-free typed choice required before an action can create its root
    /// operation, without prescribing how a player, AI, replay, or test supplies the answer.
    /// </summary>
    /// <typeparam name="TSelection">The complete value produced by this request.</typeparam>
    public abstract class ActionSelectionRequest<TSelection>
    {
        /// <summary>
        /// Initializes the common request contract for a feature-defined immutable request.
        /// </summary>
        protected ActionSelectionRequest() { }

        /// <summary>
        /// Determines whether a completed answer belongs to the immutable choices represented by
        /// this request.
        /// </summary>
        /// <param name="selection">The non-null completed answer returned by a resolver.</param>
        /// <returns>
        /// <see langword="true"/> when the answer is structurally permitted by this request;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// This boundary check detects a broken or stale presentation response. It does not replace
        /// authoritative validation by the root operation created from the completed workflow.
        /// </remarks>
        public abstract bool Accepts(TSelection selection);
    }

    /// <summary>
    /// Resolves any typed action selection request through a presentation, planner, replay, or
    /// test boundary.
    /// </summary>
    /// <remarks>
    /// Feature work introduces concrete <see cref="ActionSelectionRequest{TSelection}"/> types only
    /// when their real interaction is implemented. This generic contract therefore remains stable
    /// as new actions require different choices. Resolvers return plain Unity-free values and do
    /// not dispatch operations or mutate rules state.
    /// </remarks>
    public interface ISelectionResolver
    {
        /// <summary>Resolves one concrete request as a structural workflow outcome.</summary>
        /// <typeparam name="TSelection">The complete answer type required by the request.</typeparam>
        /// <param name="request">The required immutable request.</param>
        /// <param name="cancellationToken">
        /// Cancels presentation work that is no longer allowed to create an operation.
        /// </param>
        /// <returns>A completed, cancelled, or invalid outcome.</returns>
        ValueTask<SelectionOutcome<TSelection>> Select<TSelection>(
            ActionSelectionRequest<TSelection> request,
            CancellationToken cancellationToken
        );
    }
}
