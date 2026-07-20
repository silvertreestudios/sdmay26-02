using System;
using Game.DungeonGeneration;
using UnityEngine;

namespace Game.KayKit
{
    /// <summary>
    /// Exposes a generated stair's stable identity and same-floor arrival contract to later traversal code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonStairMarker : MonoBehaviour
    {
        [SerializeField]
        private string stableId = string.Empty;

        [SerializeField]
        private DungeonStairKind kind;

        [SerializeField]
        private int cellX;

        [SerializeField]
        private int cellZ;

        [SerializeField]
        private int arrivalCellX;

        [SerializeField]
        private int arrivalCellZ;

        /// <summary>Gets the stable JSON stair ID.</summary>
        public string StableId => stableId;

        /// <summary>Gets whether this stair leads toward the previous or next depth.</summary>
        public DungeonStairKind Kind => kind;

        /// <summary>Gets the walkable stair endpoint.</summary>
        public DungeonCell Cell => new(cellX, cellZ);

        /// <summary>Gets the adjacent walkable cell used when a party arrives at this stair.</summary>
        public DungeonCell ArrivalCell => new(arrivalCellX, arrivalCellZ);

        /// <summary>Initializes this marker from a validated serialized stair record.</summary>
        /// <param name="id">The non-empty stable stair ID.</param>
        /// <param name="kind">The traversal direction.</param>
        /// <param name="cell">The stair endpoint.</param>
        /// <param name="arrivalCell">The adjacent same-floor arrival cell.</param>
        /// <exception cref="ArgumentException">
        /// The ID is empty or the arrival cell is not orthogonally adjacent to the endpoint.
        /// </exception>
        public void Configure(
            string id,
            DungeonStairKind kind,
            DungeonCell cell,
            DungeonCell arrivalCell
        )
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A generated stair requires a stable ID.", nameof(id));
            int distance = Math.Abs(cell.X - arrivalCell.X) + Math.Abs(cell.Z - arrivalCell.Z);
            if (distance != 1)
            {
                throw new ArgumentException(
                    "A stair arrival cell must be orthogonally adjacent to its endpoint.",
                    nameof(arrivalCell)
                );
            }

            stableId = id;
            this.kind = kind;
            cellX = cell.X;
            cellZ = cell.Z;
            arrivalCellX = arrivalCell.X;
            arrivalCellZ = arrivalCell.Z;
        }
    }
}
