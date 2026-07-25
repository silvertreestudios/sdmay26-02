using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Selects the deterministic walkable region used both to place a party at a stair and to
    /// decide whether that party has gathered closely enough to depart.
    /// </summary>
    /// <remarks>
    /// The region contains at most the requested number of ground cells. Cells must be reachable
    /// from the stair endpoint without crossing a supplied blocked cell and are ordered by shortest
    /// orthogonal topology distance, then Z, then X. Door cells may connect that topology but are
    /// never selected because a closed generated door cannot host a token. This lets narrow stair
    /// runways expand into their nearest room without imposing a fixed party-size limit.
    /// </remarks>
    public static class DungeonStairInteractionRegion
    {
        private static readonly DungeonCell[] Directions =
        {
            new(0, 1),
            new(1, 0),
            new(0, -1),
            new(-1, 0),
        };

        /// <summary>Selects the nearest deterministic non-blocked cells for one stair.</summary>
        /// <param name="document">The floor containing the stair and walkable topology.</param>
        /// <param name="stair">The stair whose endpoint anchors the region.</param>
        /// <param name="blockedCells">
        /// Cells reserved by objects or creatures. Blocked cells are neither selected nor crossed.
        /// </param>
        /// <param name="requestedCellCount">
        /// The number of living party cells the caller needs; zero returns an empty region.
        /// </param>
        /// <returns>
        /// Up to <paramref name="requestedCellCount"/> reachable cells in the documented stable
        /// order. A shorter result means the floor cannot accommodate the requested party.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The document, stair, or blocked-cell sequence is absent.
        /// </exception>
        /// <exception cref="ArgumentException">The stair is not part of the document.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="requestedCellCount"/> is negative.
        /// </exception>
        public static IReadOnlyList<DungeonCell> SelectCells(
            DungeonLevelDocument document,
            DungeonStair stair,
            IEnumerable<DungeonCell> blockedCells,
            int requestedCellCount
        )
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (stair == null)
                throw new ArgumentNullException(nameof(stair));
            if (blockedCells == null)
                throw new ArgumentNullException(nameof(blockedCells));
            if (requestedCellCount < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedCellCount));
            if (
                !document.Stairs.Any(candidate =>
                    string.Equals(candidate.Id, stair.Id, StringComparison.Ordinal)
                    && candidate.Kind == stair.Kind
                    && candidate.Cell == stair.Cell
                    && candidate.ArrivalCell == stair.ArrivalCell
                )
            )
            {
                throw new ArgumentException(
                    "The stair must belong to the supplied dungeon document.",
                    nameof(stair)
                );
            }
            if (requestedCellCount == 0)
                return Array.Empty<DungeonCell>();

            HashSet<DungeonCell> blocked = new(blockedCells);
            Dictionary<DungeonCell, int> distances = new();
            if (blocked.Contains(stair.Cell) || !IsWalkable(document.Rows, stair.Cell))
                return Array.Empty<DungeonCell>();

            Queue<DungeonCell> pending = new();
            distances.Add(stair.Cell, 0);
            pending.Enqueue(stair.Cell);
            while (pending.Count > 0)
            {
                DungeonCell current = pending.Dequeue();
                foreach (DungeonCell direction in Directions)
                {
                    DungeonCell next = new(current.X + direction.X, current.Z + direction.Z);
                    if (
                        distances.ContainsKey(next)
                        || blocked.Contains(next)
                        || !IsWalkable(document.Rows, next)
                    )
                    {
                        continue;
                    }

                    distances.Add(next, distances[current] + 1);
                    pending.Enqueue(next);
                }
            }

            return Array.AsReadOnly(
                distances
                    .Where(entry => IsOccupiable(document.Rows, entry.Key))
                    .OrderBy(entry => entry.Value)
                    .ThenBy(entry => entry.Key.Z)
                    .ThenBy(entry => entry.Key.X)
                    .Take(requestedCellCount)
                    .Select(entry => entry.Key)
                    .ToArray()
            );
        }

        private static bool IsWalkable(IReadOnlyList<string> rows, DungeonCell cell)
        {
            if (rows.Count == 0 || cell.Z < 0 || cell.Z >= rows.Count || cell.X < 0)
                return false;
            string row = rows[rows.Count - 1 - cell.Z];
            return cell.X < row.Length && (row[cell.X] == '.' || row[cell.X] == 'D');
        }

        private static bool IsOccupiable(IReadOnlyList<string> rows, DungeonCell cell)
        {
            if (rows.Count == 0 || cell.Z < 0 || cell.Z >= rows.Count || cell.X < 0)
                return false;
            string row = rows[rows.Count - 1 - cell.Z];
            return cell.X < row.Length && row[cell.X] == '.';
        }
    }
}
