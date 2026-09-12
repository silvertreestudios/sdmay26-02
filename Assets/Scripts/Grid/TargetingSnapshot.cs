using System;
using System.Collections.Generic;
using Game.KayKit;
using GridPrivate;
using UnityEngine;

namespace GridPublic
{
    /// <summary>
    /// Immutable inputs for one placed area query. Capture on Unity's main thread after spatial
    /// projections settle; evaluate only this object, never its original Unity sources.
    /// </summary>
    /// <remarks>
    /// Identity denotes a capture, not a world revision. There is no complete world revision source
    /// in the current grid. Recapture for each preview and again before confirmation. Entity IDs
    /// are Unity instance IDs, valid only in this scene/session; resolve them outside evaluation
    /// against the still-live selection owner, never by name or by a replacement at the same cell.
    /// </remarks>
    public sealed class TargetingSnapshot
    {
        private readonly bool[,] tiles;
        private readonly bool[,] blockers;
        private readonly IReadOnlyList<int>[,] occupants;
        private readonly ushort[,] physicsClearRays;

        /// <summary>Unique identity for this complete capture, including its query parameters.</summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>Grid extent along x; elevation is not an array dimension.</summary>
        public int Width => tiles.GetLength(0);

        /// <summary>Grid extent along z; preserves the existing planar grid convention.</summary>
        public int Depth => tiles.GetLength(1);

        /// <summary>Rounded source position captured once, including its existing y coordinate.</summary>
        public Vector3Int SourceCell { get; }

        /// <summary>Scene-local source identity, or absent for a cell-only source.</summary>
        public int? SourceEntityId { get; }

        /// <summary>Requested geometry shape, independent of the placement's reported shape.</summary>
        public AreaShape Shape { get; }

        /// <summary>Unmodified requested size in feet; legality remains the evaluator's concern.</summary>
        public int SizeFeet { get; }

        /// <summary>Unmodified placement range in feet.</summary>
        public int RangeFeet { get; }

        /// <summary>Unmodified line width; evaluators retain the existing minimum-width rule.</summary>
        public int LineWidthFeet { get; }

        /// <summary>Whether the source cell belongs to an emanation.</summary>
        public bool IncludeCenter { get; }

        /// <summary>Whether obstruction excludes occupants rather than only reporting cover.</summary>
        public bool RequiresLineOfEffect { get; }

        /// <summary>Placement shape copied separately to preserve existing result semantics.</summary>
        public AreaShape PlacementShape { get; }

        /// <summary>Requested placement cell; non-burst results use the source cell instead.</summary>
        public Vector3Int PlacementOriginCell { get; }

        /// <summary>Grid corner used for burst distance and point-origin rays.</summary>
        public Vector2Int OriginCorner { get; }

        /// <summary>One of the existing eight planar aiming directions.</summary>
        public AreaDirection Direction { get; }

        /// <summary>Four point-origin rays for bursts, otherwise sixteen corner pairs.</summary>
        public int PhysicsRayCount => Shape == AreaShape.Burst ? 4 : 16;

        private TargetingSnapshot(
            AreaTargetSource source,
            Tile[,] sourceTiles,
            AreaTargetRequest request,
            AreaPlacement placement
        )
        {
            SourceCell = source.OriginCell;
            SourceEntityId =
                source.SourceObject != null ? source.SourceObject.GetInstanceID() : (int?)null;
            Shape = request.Shape;
            SizeFeet = request.SizeFeet;
            RangeFeet = request.RangeFeet;
            LineWidthFeet = request.LineWidthFeet;
            IncludeCenter = request.IncludeCenter;
            RequiresLineOfEffect = request.RequiresLineOfEffect;
            PlacementShape = placement.Shape;
            PlacementOriginCell = placement.OriginCell;
            OriginCorner = placement.OriginCorner;
            Direction = placement.Direction;
            int width = sourceTiles.GetLength(0);
            int depth = sourceTiles.GetLength(1);
            tiles = new bool[width, depth];
            blockers = new bool[width, depth];
            occupants = new IReadOnlyList<int>[width, depth];
            physicsClearRays = new ushort[width, depth];
            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
            {
                Tile tile = sourceTiles[x, z];
                tiles[x, z] = tile != null;
                blockers[x, z] = GridLineOfSightData.IsBlocking(
                    sourceTiles,
                    new Vector3Int(x, 0, z)
                );
                List<int> ids = new();
                if (tile != null)
                    foreach (GameObject occupant in tile.Occupants)
                        // Destroyed Unity objects are absent at the capture boundary.
                        if (occupant != null)
                            ids.Add(occupant.GetInstanceID());
                occupants[x, z] = ids.AsReadOnly();
                for (int ray = 0; ray < PhysicsRayCount; ray++)
                {
                    Vector2 start = RayStart(ray);
                    Vector2 end = RayEnd(new Vector3Int(x, 0, z), ray);
                    // Preserve exact collider, layer, trigger and endpoint semantics. A bounds
                    // approximation would change mesh/rotated collider behavior. Only booleans
                    // escape the synchronous physics boundary; no collider/delegate is retained.
                    if (
                        !MapLineOfSightBlocker.BlocksSegment(
                            new Vector3(start.x, 0.75f, start.y),
                            new Vector3(end.x, 0.75f, end.y)
                        )
                    )
                        physicsClearRays[x, z] |= (ushort)(1 << ray);
                }
            }
        }

        /// <summary>
        /// Copies current grid, occupancy and query values synchronously. Null Unity-boundary
        /// containers are rejected; a cell-only source is explicitly supported. The caller must
        /// synchronize physics transforms first if it has edited transforms since the last physics
        /// update. Capture costs four or sixteen physics queries per grid cell, regardless of area.
        /// </summary>
        public static TargetingSnapshot Capture(
            AreaTargetSource source,
            Tile[,] tiles,
            AreaTargetRequest request,
            AreaPlacement placement
        )
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (tiles == null)
                throw new ArgumentNullException(nameof(tiles));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (placement == null)
                throw new ArgumentNullException(nameof(placement));
            return new TargetingSnapshot(source, tiles, request, placement);
        }

        /// <summary>Tests x/z bounds, intentionally ignoring elevation like the current grid.</summary>
        public bool IsInBounds(Vector3Int cell) =>
            cell.x >= 0 && cell.z >= 0 && cell.x < Width && cell.z < Depth;

        /// <summary>Whether geometry may include the cell, independent of transparency.</summary>
        public bool HasTile(Vector3Int cell) => IsInBounds(cell) && tiles[cell.x, cell.z];

        /// <summary>Captured obstruction, including transparent null tiles and opaque boundaries.</summary>
        public bool IsBlocking(Vector3Int cell) => !IsInBounds(cell) || blockers[cell.x, cell.z];

        /// <summary>Captured ordered scene-local occupant IDs; no live object references escape.</summary>
        public IReadOnlyList<int> OccupantsAt(Vector3Int cell) =>
            IsInBounds(cell) ? occupants[cell.x, cell.z] : Array.Empty<int>();

        /// <summary>
        /// Returns bits for physics-clear rays, not grid-clear rays. The line-of-effect evaluator
        /// must intersect these bits with its captured-grid ray tests before counting clear rays.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The target is outside this capture.</exception>
        public ushort PhysicsClearRayMask(Vector3Int target)
        {
            if (!IsInBounds(target))
                throw new ArgumentOutOfRangeException(nameof(target));
            return physicsClearRays[target.x, target.z];
        }

        /// <summary>
        /// Returns the grid-space ray origin. Burst rays have no exempt source cell; other rays
        /// exempt <see cref="SourceCell"/> during grid sampling, as in the existing evaluator.
        /// </summary>
        public Vector2 RayStart(int ray)
        {
            ValidateRay(ray);
            return Shape == AreaShape.Burst
                ? new Vector2(OriginCorner.x, OriginCorner.y)
                : new Vector2(SourceCell.x + 0.5f, SourceCell.z + 0.5f) + CornerOffset(ray / 4);
        }

        /// <summary>Returns the target corner point; the target cell is exempt from grid sampling.</summary>
        public Vector2 RayEnd(Vector3Int target, int ray)
        {
            ValidateRay(ray);
            return new Vector2(target.x + 0.5f, target.z + 0.5f) + CornerOffset(ray % 4);
        }

        private void ValidateRay(int ray)
        {
            if (ray < 0 || ray >= PhysicsRayCount)
                throw new ArgumentOutOfRangeException(nameof(ray));
        }

        // Matches GridTargeting's nested start/target order: --, +-, -+, ++.
        private static Vector2 CornerOffset(int corner) =>
            new((corner % 2 == 0 ? -0.4f : 0.4f), (corner < 2 ? -0.4f : 0.4f));
    }
}
