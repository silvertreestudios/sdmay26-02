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
        /// Executes one leader step and any eligible follower steps, then reports whether the
        /// queued leader path may continue.
        /// </summary>
        /// <param name="leader">The current exploration leader.</param>
        /// <param name="destination">The leader's next cardinal grid cell.</param>
        /// <param name="tiles">The live grid occupancy and walkability array.</param>
        /// <param name="movement">The scene's serialized token movement presenter.</param>
        /// <param name="continuePath">
        /// Set to <see langword="false"/> when movement is rejected, blocked, or interrupted by
        /// encounter activation.
        /// </param>
        /// <returns>A coroutine that completes after all committed member movement.</returns>
        IEnumerator ExecuteStep(
            GameObject leader,
            Vector3Int destination,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> continuePath
        );
    }

    internal sealed class NoExplorationStrideCoordinator : IExplorationStrideCoordinator
    {
        internal static NoExplorationStrideCoordinator Instance { get; } = new();

        private NoExplorationStrideCoordinator() { }

        public bool Handles(GameObject character) => false;

        public IEnumerator ExecuteStep(
            GameObject leader,
            Vector3Int destination,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> continuePath
        ) =>
            throw new InvalidOperationException(
                "The null exploration coordinator cannot execute movement."
            );
    }
}
