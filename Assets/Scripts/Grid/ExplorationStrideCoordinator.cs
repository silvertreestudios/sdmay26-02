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

        /// <summary>
        /// Projects one already-committed leader step and any eligible follower steps, then
        /// reports whether the queued leader path may continue.
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
        /// <returns>A coroutine that completes after all committed member movement.</returns>
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

    internal sealed class NoExplorationStrideCoordinator : IExplorationStrideCoordinator
    {
        internal static NoExplorationStrideCoordinator Instance { get; } = new();

        private NoExplorationStrideCoordinator() { }

        public bool Handles(GameObject character) => false;

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
