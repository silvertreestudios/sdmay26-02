using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Stores immutable operation frames and exposes execution and causal ancestry queries.
    /// </summary>
    /// <remarks>
    /// A dispatcher appends each frame before invoking its resolver. Frames remain available for
    /// the lifetime of the trace, so diagnostics and later handlers can inspect completed roots.
    /// Ancestry queries are strict: an operation is not considered its own ancestor or cause.
    /// </remarks>
    public sealed class ResolutionTrace
    {
        private readonly Dictionary<OpId, IOpFrameView> frames =
            new Dictionary<OpId, IOpFrameView>();

        internal void Add(IOpFrameView frame)
        {
            if (frames.ContainsKey(frame.Id))
                throw new InvalidOperationException($"Duplicate operation ID {frame.Id.Value}.");
            frames.Add(frame.Id, frame);
        }

        /// <summary>
        /// Determines whether a frame with the specified identifier has been recorded.
        /// </summary>
        /// <param name="id">The operation identifier to look up.</param>
        /// <returns><see langword="true"/> when the trace contains the frame; otherwise, <see langword="false"/>.</returns>
        public bool Exists(OpId id) => frames.ContainsKey(id);

        /// <summary>
        /// Gets a recorded frame and verifies its concrete operation type.
        /// </summary>
        /// <typeparam name="TOp">The operation type expected by the caller.</typeparam>
        /// <param name="id">The identifier of the frame to retrieve.</param>
        /// <returns>The recorded frame with its original typed operation.</returns>
        /// <exception cref="InvalidOperationException">
        /// No frame has the identifier, or the recorded operation is not <typeparamref name="TOp"/>.
        /// </exception>
        public OpFrame<TOp> Get<TOp>(OpId id)
            where TOp : IRuleOp
        {
            IOpFrameView view = Require(id);
            if (view.TypedFrame is OpFrame<TOp> typed)
                return typed;
            throw new InvalidOperationException(
                $"Operation {id.Value} is {view.OpType.Name}, not {typeof(TOp).Name}.");
        }

        /// <summary>
        /// Gets the trusted action identity for a recorded action frame.
        /// </summary>
        /// <param name="id">The action operation identifier.</param>
        /// <returns>The identity and provenance used when its profile was resolved.</returns>
        /// <exception cref="InvalidOperationException">
        /// The frame is absent or represents a non-action operation.
        /// </exception>
        public ActionOpInfo GetAction(OpId id)
        {
            IOpFrameView view = Require(id);
            if (!view.IsAction)
                throw new InvalidOperationException($"Operation {id.Value} does not represent an action.");
            return view.ActionInfo;
        }

        /// <summary>
        /// Gets the effective profile frozen on a recorded action frame.
        /// </summary>
        /// <param name="id">The action operation identifier.</param>
        /// <returns>The exact profile shared by validation, costs, lifecycle middleware, and handling.</returns>
        /// <exception cref="InvalidOperationException">
        /// The frame is absent or represents a non-action operation.
        /// </exception>
        public ActionProfile GetActionProfile(OpId id)
        {
            IOpFrameView view = Require(id);
            if (!view.IsAction)
                throw new InvalidOperationException($"Operation {id.Value} does not represent an action.");
            return view.ActionProfile;
        }

        /// <summary>
        /// Determines whether an operation is nested beneath another operation.
        /// </summary>
        /// <param name="candidateId">The possible descendant.</param>
        /// <param name="ancestorId">The possible strict ancestor.</param>
        /// <returns>
        /// <see langword="true"/> when following parent links from the candidate reaches the ancestor;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The candidate is absent, a referenced frame is absent, or the ancestry contains a cycle.
        /// </exception>
        public bool IsDescendantOf(OpId candidateId, OpId ancestorId) =>
            Follows(candidateId, ancestorId, frame => frame.ParentId);

        /// <summary>
        /// Determines whether an operation's causal chain contains another operation.
        /// </summary>
        /// <param name="candidateId">The operation whose causal chain is inspected.</param>
        /// <param name="causeId">The possible strict cause.</param>
        /// <returns>
        /// <see langword="true"/> when following cause links reaches <paramref name="causeId"/>;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The candidate is absent, a referenced frame is absent, or the causal chain contains a cycle.
        /// </exception>
        public bool IsCausedBy(OpId candidateId, OpId causeId) =>
            Follows(candidateId, causeId, frame => frame.CauseId);

        /// <summary>
        /// Finds the closest parent-chain ancestor with the requested operation type.
        /// </summary>
        /// <typeparam name="TOp">The ancestor operation type to find.</typeparam>
        /// <param name="candidateId">The operation where the search begins.</param>
        /// <returns>The nearest matching ancestor frame, or <see langword="null"/> when none exists.</returns>
        /// <exception cref="InvalidOperationException">
        /// The candidate is absent, a referenced frame is absent, or the ancestry contains a cycle.
        /// </exception>
        public OpFrame<TOp> FindNearestAncestor<TOp>(OpId candidateId)
            where TOp : IRuleOp =>
            FindFollowing<TOp>(candidateId, frame => frame.ParentId);

        /// <summary>
        /// Finds the closest causal-chain ancestor with the requested operation type.
        /// </summary>
        /// <typeparam name="TOp">The causing operation type to find.</typeparam>
        /// <param name="candidateId">The operation where the search begins.</param>
        /// <returns>The nearest matching causal frame, or <see langword="null"/> when none exists.</returns>
        /// <exception cref="InvalidOperationException">
        /// The candidate is absent, a referenced frame is absent, or the causal chain contains a cycle.
        /// </exception>
        public OpFrame<TOp> FindCausingAncestor<TOp>(OpId candidateId)
            where TOp : IRuleOp =>
            FindFollowing<TOp>(candidateId, frame => frame.CauseId);

        internal IReadOnlyList<IOpFrameView> OrderedFrames =>
            frames.Values.OrderBy(frame => frame.Id.Value).ToArray();

        internal IOpFrameView Require(OpId id)
        {
            if (!frames.TryGetValue(id, out IOpFrameView frame))
                throw new InvalidOperationException($"Operation {id.Value} is not in this trace.");
            return frame;
        }

        private bool Follows(
            OpId candidateId,
            OpId targetId,
            Func<IOpFrameView, OpId?> next)
        {
            IOpFrameView current = Require(candidateId);
            HashSet<OpId> visited = new HashSet<OpId> { candidateId };
            while (next(current).HasValue)
            {
                OpId nextId = next(current).Value;
                if (nextId == targetId)
                    return true;
                if (!visited.Add(nextId))
                    throw new InvalidOperationException("A cycle exists in operation provenance.");
                current = Require(nextId);
            }
            return false;
        }

        private OpFrame<TOp> FindFollowing<TOp>(
            OpId candidateId,
            Func<IOpFrameView, OpId?> next)
            where TOp : IRuleOp
        {
            IOpFrameView current = Require(candidateId);
            HashSet<OpId> visited = new HashSet<OpId> { candidateId };
            while (next(current).HasValue)
            {
                OpId nextId = next(current).Value;
                if (!visited.Add(nextId))
                    throw new InvalidOperationException("A cycle exists in operation provenance.");
                current = Require(nextId);
                if (current.TypedFrame is OpFrame<TOp> typed)
                    return typed;
            }
            return null;
        }
    }
}
