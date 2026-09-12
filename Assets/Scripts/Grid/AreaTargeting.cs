using System.Collections.Generic;
using UnityEngine;

namespace GridPublic
{
    public enum AreaShape
    {
        Burst,
        Cone,
        Emanation,
        Line,
    }

    public enum AreaDirection
    {
        East,
        NorthEast,
        North,
        NorthWest,
        West,
        SouthWest,
        South,
        SouthEast,
    }

    public class AreaTargetRequest
    {
        public AreaShape Shape { get; set; }
        public int SizeFeet { get; set; }
        public int RangeFeet { get; set; }
        public int LineWidthFeet { get; set; } = 5;
        public bool IncludeCenter { get; set; }
        public bool RequiresLineOfEffect { get; set; } = true;
    }

    public class AreaTargetSource
    {
        public AreaTargetSource() { }

        public AreaTargetSource(GameObject sourceObject)
        {
            SourceObject = sourceObject;
            Cell =
                sourceObject == null
                    ? Vector3Int.zero
                    : Vector3Int.RoundToInt(sourceObject.transform.position);
        }

        public AreaTargetSource(Vector3Int cell)
        {
            Cell = cell;
        }

        public GameObject SourceObject { get; set; }
        public Vector3Int Cell { get; set; }
        public Vector3Int OriginCell =>
            SourceObject == null ? Cell : Vector3Int.RoundToInt(SourceObject.transform.position);
    }

    public class AreaPlacement
    {
        public AreaShape Shape { get; set; }
        public Vector3Int OriginCell { get; set; }
        public Vector2Int OriginCorner { get; set; }
        public AreaDirection Direction { get; set; }
    }

    public class AreaAffectedCreature
    {
        public GameObject Creature { get; set; }
        public Vector3Int Cell { get; set; }
        public StrikeLineOfEffect LineOfEffect { get; set; }
        public StrikeCover Cover { get; set; }
        public int ClearRays { get; set; }
        public bool IsAffected => Creature != null && LineOfEffect == StrikeLineOfEffect.Clear;
    }

    public class AreaTargetResult
    {
        /// <summary>Capture provenance, not a freshness or world revision guarantee.</summary>
        public System.Guid SnapshotId { get; set; }
        public AreaPlacement Placement { get; set; }
        public List<Vector3Int> Cells { get; set; } = new();
        public List<AreaAffectedCreature> Creatures { get; set; } = new();
        public bool IsLegal => Placement != null && Cells.Count > 0;
    }

    public class GridHoverInfo
    {
        public Vector3Int Cell { get; set; }
        public Vector3 WorldPosition { get; set; }
        public Vector2Int NearestCorner { get; set; }
    }
}

namespace GridPrivate
{
    public static class AreaTargeting
    {
        private const float AngleEpsilon = 0.01f;

        /// <summary>
        /// Adapts a live actor to the synchronous snapshot capture and Unity resolution boundary.
        /// </summary>
        public static GridPublic.AreaTargetResult Evaluate(
            GameObject actor,
            Tile[,] tiles,
            GridPublic.AreaTargetRequest request,
            GridPublic.AreaPlacement placement
        )
        {
            return Evaluate(new GridPublic.AreaTargetSource(actor), tiles, request, placement);
        }

        /// <summary>
        /// Captures all query inputs, evaluates only that snapshot, then resolves original objects.
        /// Returns null for absent inputs or illegal geometry at this legacy Unity boundary.
        /// </summary>
        public static GridPublic.AreaTargetResult Evaluate(
            GridPublic.AreaTargetSource source,
            Tile[,] tiles,
            GridPublic.AreaTargetRequest request,
            GridPublic.AreaPlacement placement
        )
        {
            if (
                source == null
                || tiles == null
                || request == null
                || placement == null
                || request.SizeFeet <= 0
            )
                return null;
            return GridPublic
                .AreaTargetCapture.Capture(source, tiles, request, placement)
                .Resolve();
        }

        /// <summary>
        /// Computes geometry, ordered candidates and obstruction from one immutable capture.
        /// No Unity object lookup occurs; historical results remain stable after movement or destruction.
        /// </summary>
        public static GridPublic.AreaSelectionSnapshot Evaluate(
            GridPublic.TargetingSnapshot snapshot
        )
        {
            List<Vector3Int> cells = CellsForPlacement(snapshot);
            List<GridPublic.AreaSelectedEntity> creatures = new();
            foreach (Vector3Int cell in cells)
            {
                if (snapshot.OccupantsAt(cell).Count == 0)
                    continue;
                int clearRays = GridTargeting.CountClearRays(snapshot, cell);
                foreach (int entity in snapshot.OccupantsAt(cell))
                    creatures.Add(
                        new GridPublic.AreaSelectedEntity(
                            entity,
                            cell,
                            clearRays,
                            snapshot.RequiresLineOfEffect
                        )
                    );
            }
            return new GridPublic.AreaSelectionSnapshot(snapshot, cells, creatures);
        }

        /// <summary>
        /// Captures the live grid for range highlighting, then delegates to snapshot geometry.
        /// Selection owners with a placed capture should reuse the snapshot overload instead.
        /// </summary>
        public static List<Vector3Int> CellsInPlacementRange(
            Tile[,] tiles,
            Vector3Int start,
            GridPublic.AreaTargetRequest request
        )
        {
            if (tiles == null || request == null)
                return new List<Vector3Int>();
            return CellsInPlacementRange(
                GridPublic.TargetingSnapshot.Capture(
                    new GridPublic.AreaTargetSource(start),
                    tiles,
                    request,
                    new GridPublic.AreaPlacement { Shape = request.Shape, OriginCell = start }
                )
            );
        }

        /// <summary>
        /// Returns present cells in the captured source's planar placement highlight range.
        /// Positive burst range overrides size; otherwise at least five feet is highlighted.
        /// This is not burst-corner legality or obstruction filtering. The fresh list is ordered
        /// by x then z and retains source y, regardless of tile elevations.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">The snapshot is absent.</exception>
        public static List<Vector3Int> CellsInPlacementRange(GridPublic.TargetingSnapshot snapshot)
        {
            if (snapshot == null)
                throw new System.ArgumentNullException(nameof(snapshot));
            List<Vector3Int> result = new();
            Vector3Int start = snapshot.SourceCell;
            int rangeFeet =
                snapshot.Shape == GridPublic.AreaShape.Burst && snapshot.RangeFeet > 0
                    ? snapshot.RangeFeet
                    : Mathf.Max(snapshot.SizeFeet, 5);
            int maxCells = Mathf.CeilToInt(rangeFeet / 5.0f);
            for (int x = start.x - maxCells; x <= start.x + maxCells; x++)
            {
                for (int z = start.z - maxCells; z <= start.z + maxCells; z++)
                {
                    Vector3Int cell = new(x, start.y, z);
                    if (!snapshot.HasTile(cell))
                        continue;
                    if (GridTargeting.MeasureGridDistanceFeet(start, cell) <= rangeFeet)
                        result.Add(cell);
                }
            }
            return result;
        }

        public static GridPublic.AreaPlacement PlacementFromHover(
            GameObject actor,
            GridPublic.AreaTargetRequest request,
            GridPublic.GridHoverInfo hover
        )
        {
            return PlacementFromHover(new GridPublic.AreaTargetSource(actor), request, hover);
        }

        public static GridPublic.AreaPlacement PlacementFromHover(
            GridPublic.AreaTargetSource source,
            GridPublic.AreaTargetRequest request,
            GridPublic.GridHoverInfo hover
        )
        {
            if (source == null || request == null)
                return null;

            Vector3Int sourceCell = source.OriginCell;
            Vector3Int hoverCell = hover?.Cell ?? sourceCell;
            GridPublic.AreaPlacement placement = new()
            {
                Shape = request.Shape,
                OriginCell = sourceCell,
                OriginCorner = hover?.NearestCorner ?? new Vector2Int(hoverCell.x, hoverCell.z),
                Direction = DirectionFromDelta(hoverCell - sourceCell),
            };

            if (request.Shape == GridPublic.AreaShape.Burst)
                placement.OriginCell = hoverCell;
            return placement;
        }

        public static Vector2Int NearestCorner(Vector3Int cell, Vector3 worldPosition)
        {
            int x = worldPosition.x >= cell.x ? cell.x + 1 : cell.x;
            int z = worldPosition.z >= cell.z ? cell.z + 1 : cell.z;
            return new Vector2Int(x, z);
        }

        public static Vector3Int DirectionOffset(GridPublic.AreaDirection direction)
        {
            return direction switch
            {
                GridPublic.AreaDirection.East => new Vector3Int(1, 0, 0),
                GridPublic.AreaDirection.NorthEast => new Vector3Int(1, 0, 1),
                GridPublic.AreaDirection.North => new Vector3Int(0, 0, 1),
                GridPublic.AreaDirection.NorthWest => new Vector3Int(-1, 0, 1),
                GridPublic.AreaDirection.West => new Vector3Int(-1, 0, 0),
                GridPublic.AreaDirection.SouthWest => new Vector3Int(-1, 0, -1),
                GridPublic.AreaDirection.South => new Vector3Int(0, 0, -1),
                GridPublic.AreaDirection.SouthEast => new Vector3Int(1, 0, -1),
                _ => new Vector3Int(1, 0, 0),
            };
        }

        public static GridPublic.AreaDirection DirectionFromDelta(Vector3Int delta)
        {
            if (delta.x == 0 && delta.z == 0)
                return GridPublic.AreaDirection.East;

            float angle = Mathf.Atan2(delta.z, delta.x) * Mathf.Rad2Deg;
            if (angle < 0)
                angle += 360.0f;
            int octant = Mathf.RoundToInt(angle / 45.0f) % 8;
            return (GridPublic.AreaDirection)octant;
        }

        private static bool IsPlacementInRange(GridPublic.TargetingSnapshot snapshot)
        {
            if (snapshot.Shape != GridPublic.AreaShape.Burst || snapshot.RangeFeet <= 0)
                return true;

            return DistanceCellToCornerFeet(snapshot.SourceCell, snapshot.OriginCorner)
                <= snapshot.RangeFeet;
        }

        /// <summary>
        /// Computes area membership from one immutable placed query, without live grid or physics
        /// reads. Returns a fresh list; empty means nonpositive size, out-of-range burst, unsupported
        /// shape, or no present tiles. Burst cells have y=0; other shapes retain source elevation.
        /// Obstruction and occupant filtering are separate from geometry.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">The snapshot is absent.</exception>
        public static List<Vector3Int> CellsForPlacement(GridPublic.TargetingSnapshot snapshot)
        {
            if (snapshot == null)
                throw new System.ArgumentNullException(nameof(snapshot));
            if (snapshot.SizeFeet <= 0 || !IsPlacementInRange(snapshot))
                return new List<Vector3Int>();
            return snapshot.Shape switch
            {
                GridPublic.AreaShape.Burst => BurstCells(snapshot),
                GridPublic.AreaShape.Cone => ConeCells(snapshot),
                GridPublic.AreaShape.Emanation => EmanationCells(snapshot),
                GridPublic.AreaShape.Line => LineCells(snapshot),
                _ => new List<Vector3Int>(),
            };
        }

        private static List<Vector3Int> BurstCells(GridPublic.TargetingSnapshot snapshot)
        {
            List<Vector3Int> cells = new();
            int radiusCells = Mathf.CeilToInt(snapshot.SizeFeet / 5.0f);
            for (
                int x = snapshot.OriginCorner.x - radiusCells - 1;
                x <= snapshot.OriginCorner.x + radiusCells;
                x++
            )
            {
                for (
                    int z = snapshot.OriginCorner.y - radiusCells - 1;
                    z <= snapshot.OriginCorner.y + radiusCells;
                    z++
                )
                {
                    Vector3Int cell = new(x, 0, z);
                    if (!snapshot.HasTile(cell))
                        continue;
                    if (DistanceCornerToCellFeet(snapshot.OriginCorner, cell) <= snapshot.SizeFeet)
                        cells.Add(cell);
                }
            }
            return cells;
        }

        private static List<Vector3Int> ConeCells(GridPublic.TargetingSnapshot snapshot)
        {
            List<Vector3Int> cells = new();
            Vector3Int start = snapshot.SourceCell;
            int radiusCells = Mathf.CeilToInt(snapshot.SizeFeet / 5.0f);
            Vector2 direction = ToVector2(DirectionOffset(snapshot.Direction)).normalized;

            for (int x = start.x - radiusCells; x <= start.x + radiusCells; x++)
            {
                for (int z = start.z - radiusCells; z <= start.z + radiusCells; z++)
                {
                    Vector3Int cell = new(x, start.y, z);
                    if (cell == start || !snapshot.HasTile(cell))
                        continue;
                    if (GridTargeting.MeasureGridDistanceFeet(start, cell) > snapshot.SizeFeet)
                        continue;

                    Vector2 offset = new(cell.x - start.x, cell.z - start.z);
                    if (Vector2.Angle(direction, offset) <= 45.0f + AngleEpsilon)
                        cells.Add(cell);
                }
            }
            return cells;
        }

        private static List<Vector3Int> EmanationCells(GridPublic.TargetingSnapshot snapshot)
        {
            List<Vector3Int> cells = new();
            Vector3Int start = snapshot.SourceCell;
            int radiusCells = Mathf.CeilToInt(snapshot.SizeFeet / 5.0f);
            for (int x = start.x - radiusCells; x <= start.x + radiusCells; x++)
            {
                for (int z = start.z - radiusCells; z <= start.z + radiusCells; z++)
                {
                    Vector3Int cell = new(x, start.y, z);
                    if (!snapshot.IncludeCenter && cell == start)
                        continue;
                    if (!snapshot.HasTile(cell))
                        continue;
                    if (GridTargeting.MeasureGridDistanceFeet(start, cell) <= snapshot.SizeFeet)
                        cells.Add(cell);
                }
            }
            return cells;
        }

        private static List<Vector3Int> LineCells(GridPublic.TargetingSnapshot snapshot)
        {
            List<Vector3Int> cells = new();
            Vector3Int start = snapshot.SourceCell;
            float lengthCells = snapshot.SizeFeet / 5.0f;
            int widthCells = Mathf.Max(
                1,
                Mathf.CeilToInt(Mathf.Max(5, snapshot.LineWidthFeet) / 5.0f)
            );
            int search = Mathf.CeilToInt(lengthCells) + widthCells + 1;
            Vector2 direction = ToVector2(DirectionOffset(snapshot.Direction)).normalized;

            for (int x = start.x - search; x <= start.x + search; x++)
            {
                for (int z = start.z - search; z <= start.z + search; z++)
                {
                    Vector3Int cell = new(x, start.y, z);
                    if (cell == start || !snapshot.HasTile(cell))
                        continue;

                    Vector2 offset = new(cell.x - start.x, cell.z - start.z);
                    float projection = Vector2.Dot(offset, direction);
                    if (projection <= 0.0f || projection > lengthCells + AngleEpsilon)
                        continue;

                    float perpendicular = Mathf.Abs(
                        offset.x * direction.y - offset.y * direction.x
                    );
                    float halfWidth = Mathf.Max(0.01f, (widthCells - 1) * 0.5f + 0.01f);
                    if (perpendicular <= halfWidth)
                        cells.Add(cell);
                }
            }
            cells.Sort(
                (a, b) =>
                    GridTargeting
                        .MeasureGridDistanceFeet(start, a)
                        .CompareTo(GridTargeting.MeasureGridDistanceFeet(start, b))
            );
            return cells;
        }

        private static int DistanceCellToCornerFeet(Vector3Int cell, Vector2Int corner)
        {
            int best = int.MaxValue;
            foreach (Vector2Int cellCorner in CellCorners(cell))
                best = Mathf.Min(
                    best,
                    GridTargeting.MeasureGridDistanceFeet(
                        corner.x - cellCorner.x,
                        corner.y - cellCorner.y
                    )
                );
            return best;
        }

        private static int DistanceCornerToCellFeet(Vector2Int corner, Vector3Int cell)
        {
            int best = int.MaxValue;
            foreach (Vector2Int cellCorner in CellCorners(cell))
                best = Mathf.Min(
                    best,
                    GridTargeting.MeasureGridDistanceFeet(
                        corner.x - cellCorner.x,
                        corner.y - cellCorner.y
                    )
                );
            return best;
        }

        private static IEnumerable<Vector2Int> CellCorners(Vector3Int cell)
        {
            yield return new Vector2Int(cell.x, cell.z);
            yield return new Vector2Int(cell.x + 1, cell.z);
            yield return new Vector2Int(cell.x, cell.z + 1);
            yield return new Vector2Int(cell.x + 1, cell.z + 1);
        }

        private static Vector2 ToVector2(Vector3Int value)
        {
            return new Vector2(value.x, value.z);
        }
    }
}
