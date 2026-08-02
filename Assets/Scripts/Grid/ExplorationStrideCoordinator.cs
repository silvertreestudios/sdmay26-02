using System;
using System.Collections;
using UnityEngine;

namespace GridPrivate
{
    /// <summary>
    /// Coordinates one selected leader's grid step when dungeon exploration owns movement.
    /// </summary>
    public interface IExplorationStrideCoordinator
    {
        /// <summary>Gets whether this coordinator owns movement for the supplied character.</summary>
        /// <param name="character">The character whose Stride is executing.</param>
        /// <returns><see langword="true"/> only for the current exploration leader.</returns>
        bool Handles(GameObject character);

        /// <summary>Gets whether the supplied character occupies a cell as a party member.</summary>
        /// <param name="character">The candidate occupant.</param>
        /// <returns><see langword="true"/> for a living member of the exploration party.</returns>
        bool IsPartyMember(GameObject character);

        /// <summary>Cancels active destination travel at its next committed boundary.</summary>
        /// <returns><see langword="true"/> when active travel accepted cancellation.</returns>
        bool TryCancelActiveTravel();

        /// <summary>
        /// Projects one already-committed leader step, queues its eligible follower trail, then
        /// reports whether the queued leader path may continue. A later step settles the prior
        /// follower batch before observing its boundary; the owning action drains the final batch.
        /// </summary>
        /// <param name="leader">The current exploration leader.</param>
        /// <param name="from">The leader's committed departure cell.</param>
        /// <param name="destination">The leader's next cardinal grid cell.</param>
        /// <param name="tiles">The live grid occupancy and walkability array.</param>
        /// <param name="movement">The scene's serialized token movement presenter.</param>
        /// <param name="continuePath">
        /// Set to <see langword="true"/> only when the projected path suffix may continue.
        /// </param>
        /// <param name="pathInterrupted">
        /// Set to <see langword="true"/> when the committed leader step projected successfully but
        /// a follower projection or other interruption requires the remaining temporary
        /// exploration path to be abandoned. A failure before leader projection leaves both result
        /// references <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A coroutine that completes after the leader reaches the committed cell and the current
        /// follower batch is safely queued.
        /// </returns>
        IEnumerator ProjectCommittedStep(
            GameObject leader,
            Vector3Int from,
            Vector3Int destination,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> continuePath,
            Ref<bool> pathInterrupted
        );
    }

    /// <summary>
    /// Drains action-scoped exploration presentation without expanding the public coordinator
    /// contract used by grid and rules code.
    /// </summary>
    internal interface IExplorationPresentationDrain
    {
        /// <summary>Completes every queued follower segment owned by the supplied leader action.</summary>
        IEnumerator DrainPresentation(GameObject leader);
    }

    internal sealed class NoExplorationStrideCoordinator : IExplorationStrideCoordinator
    {
        internal static NoExplorationStrideCoordinator Instance { get; } = new();

        private NoExplorationStrideCoordinator() { }

        public bool Handles(GameObject character) => false;

        public bool IsPartyMember(GameObject character) => false;

        public bool TryCancelActiveTravel() => false;

        public IEnumerator ProjectCommittedStep(
            GameObject leader,
            Vector3Int from,
            Vector3Int destination,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> continuePath,
            Ref<bool> pathInterrupted
        ) =>
            throw new InvalidOperationException(
                "The null exploration coordinator cannot execute movement."
            );
    }
}
