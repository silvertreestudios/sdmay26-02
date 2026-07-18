using System;
using Game.DungeonGeneration;
using UnityEngine;

namespace Game.KayKit
{
    /// <summary>
    /// Owns one generated door's stable identity, visual state, and synchronized grid state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonDoorController : MonoBehaviour
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private int cellX;
        [SerializeField] private int cellZ;
        [SerializeField] private Map map;
        [SerializeField] private GameObject closedVisual;
        [SerializeField] private GameObject openVisual;
        [SerializeField] private bool isOpen;

        /// <summary>Gets the stable JSON door ID used by persistence and later interaction systems.</summary>
        public string StableId => stableId;

        /// <summary>Gets the grid cell occupied by this door.</summary>
        public DungeonCell Cell => new(cellX, cellZ);

        /// <summary>Gets whether the open doorway representation is active.</summary>
        public bool IsOpen => isOpen;

        /// <summary>
        /// Binds this runtime component to its required map and visual representations.
        /// </summary>
        /// <param name="id">The non-empty stable ID from serialized JSON.</param>
        /// <param name="doorCell">The door's grid cell.</param>
        /// <param name="isOpen">The state already projected into map data.</param>
        /// <param name="owningMap">The map whose navigation and line-of-sight arrays this door mutates.</param>
        /// <param name="closedRepresentation">The blocking closed-door wrapper.</param>
        /// <param name="openRepresentation">The passable open-doorway wrapper.</param>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException">A required Unity object is absent.</exception>
        public void Configure(
            string id,
            DungeonCell doorCell,
            bool isOpen,
            Map owningMap,
            GameObject closedRepresentation,
            GameObject openRepresentation)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A generated door requires a stable ID.", nameof(id));
            map = owningMap != null
                ? owningMap
                : throw new ArgumentNullException(nameof(owningMap));
            closedVisual = closedRepresentation != null
                ? closedRepresentation
                : throw new ArgumentNullException(nameof(closedRepresentation));
            openVisual = openRepresentation != null
                ? openRepresentation
                : throw new ArgumentNullException(nameof(openRepresentation));
            stableId = id;
            cellX = doorCell.X;
            cellZ = doorCell.Z;
            this.isOpen = isOpen;
            ApplyVisualState();
        }

        /// <summary>
        /// Opens or closes the door while updating visuals, movement, pathfinding, and line of sight atomically.
        /// </summary>
        /// <param name="isOpen">The desired door state.</param>
        /// <returns>
        /// <see langword="true"/> when the state is applied or already current; otherwise
        /// <see langword="false"/>, such as when a creature occupies a door being closed.
        /// </returns>
        public bool TrySetOpen(bool isOpen)
        {
            if (isOpen == IsOpen)
                return true;
            if (!map.TrySetDoorState(Cell, isOpen))
                return false;

            this.isOpen = isOpen;
            ApplyVisualState();
            if (Application.isPlaying)
                Physics.SyncTransforms();
            return true;
        }

        /// <summary>Opens this door idempotently.</summary>
        /// <returns><see langword="true"/> when the doorway is open after the call.</returns>
        public bool TryOpen()
        {
            return TrySetOpen(true);
        }

        private void ApplyVisualState()
        {
            closedVisual.SetActive(!IsOpen);
            openVisual.SetActive(IsOpen);
        }
    }
}
