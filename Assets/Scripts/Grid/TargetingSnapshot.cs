using System;
using System.Collections.Generic;
using Game.KayKit;
using GridPrivate;
using UnityEngine;

namespace GridPublic
{
    /// <summary>
    /// Stores the geometry, occupants, and obstruction needed to evaluate one area placement
    /// without reading the live scene again.
    /// </summary>
    /// <remarks>
    /// Later scene changes cannot alter these values. Create a new capture for each preview and
    /// confirmation because a saved snapshot cannot establish whether a target is still legal.
    /// Use <see cref="AreaTargetCapture"/> when the result must refer to live Unity objects;
    /// it retains the original objects separately from this immutable input.
    /// </remarks>
    public sealed class TargetingSnapshot
    {
        private readonly bool[,] tiles;
        private readonly bool[,] blockers;
        private readonly IReadOnlyList<int>[,] occupants;
        private readonly ushort[,] physicsClearRays;

        /// <summary>Unique identity for this complete capture, including its query parameters.</summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>Number of grid columns along the x axis.</summary>
        public int Width => tiles.GetLength(0);

        /// <summary>Number of grid rows along the z axis.</summary>
        public int Depth => tiles.GetLength(1);

        /// <summary>Source grid cell at capture time, including its elevation.</summary>
        public Vector3Int SourceCell { get; }

        /// <summary>Unity instance ID of the source object, or null when the source is a cell only.</summary>
        public int? SourceEntityId { get; }

        /// <summary>Shape used to calculate area membership and ray origins.</summary>
        public AreaShape Shape { get; }

        /// <summary>Requested area size in feet. Nonpositive values produce no area cells.</summary>
        public int SizeFeet { get; }

        /// <summary>Maximum burst placement distance in feet; nonpositive values impose no limit.</summary>
        public int RangeFeet { get; }

        /// <summary>Requested line width in feet; geometry rounds up to cells with a five-foot minimum.</summary>
        public int LineWidthFeet { get; }

        /// <summary>Whether the source cell belongs to an emanation.</summary>
        public bool IncludeCenter { get; }

        /// <summary>Whether an occupant needs at least one clear ray to be affected by the area.</summary>
        public bool RequiresLineOfEffect { get; }

        /// <summary>Shape reported in the resolved placement; <see cref="Shape"/> controls evaluation.</summary>
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
                occupants[x, z] = CaptureOccupants(tile);
                for (int ray = 0; ray < PhysicsRayCount; ray++)
                {
                    Vector2 start = RayStart(ray);
                    Vector2 end = RayEnd(new Vector3Int(x, 0, z), ray);
                    // Query the actual colliders so rotated and mesh blockers retain their shape.
                    // Store only the answer so later collider changes cannot alter this capture.
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
        /// Copies the current scene and query values for evaluation by <see cref="AreaTargeting"/>.
        /// </summary>
        /// <param name="source">An object-backed source or an explicit grid cell.</param>
        /// <param name="tiles">Grid whose tile presence, obstruction, and occupant order are copied.</param>
        /// <param name="request">Area parameters. Invalid sizes and shapes are left for evaluation.</param>
        /// <param name="placement">Aim and burst origin to capture with the request.</param>
        /// <returns>An independent snapshot with a new <see cref="Id"/>.</returns>
        /// <remarks>
        /// Call on Unity's main thread after movement has updated occupancy. If transforms changed
        /// since the last physics update, call <see cref="Physics.SyncTransforms"/> first, or use
        /// <see cref="AreaTargetCapture.Capture"/>, which synchronizes them for you. This method
        /// performs four physics queries per grid cell for bursts and sixteen for other shapes,
        /// including cells outside the area.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
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

        /// <summary>Returns whether the cell's x/z coordinates are inside the grid; y is ignored.</summary>
        public bool IsInBounds(Vector3Int cell) =>
            cell.x >= 0 && cell.z >= 0 && cell.x < Width && cell.z < Depth;

        /// <summary>Returns whether the cell is in bounds and had a tile when captured.</summary>
        public bool HasTile(Vector3Int cell) => IsInBounds(cell) && tiles[cell.x, cell.z];

        /// <summary>
        /// Returns captured grid obstruction, or true outside the grid. A missing tile can be
        /// transparent when the grid's obstruction registry explicitly allows it.
        /// </summary>
        public bool IsBlocking(Vector3Int cell) => !IsInBounds(cell) || blockers[cell.x, cell.z];

        /// <summary>
        /// Returns captured Unity instance IDs in tile-list order, including duplicates.
        /// Missing tiles, cells with no live occupants, and out-of-bounds cells return an empty list.
        /// The returned collection cannot be changed.
        /// </summary>
        public IReadOnlyList<int> OccupantsAt(Vector3Int cell) =>
            IsInBounds(cell) ? occupants[cell.x, cell.z] : Array.Empty<int>();

        /// <summary>
        /// Returns a bit mask whose set bits identify rays that were clear of map colliders.
        /// Ray indices match <see cref="RayStart"/> and <see cref="RayEnd"/>. Use
        /// <see cref="GridTargeting.CountClearRays"/> to also account for grid obstruction.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The target is outside this capture.</exception>
        public ushort PhysicsClearRayMask(Vector3Int target)
        {
            if (!IsInBounds(target))
                throw new ArgumentOutOfRangeException(nameof(target));
            return physicsClearRays[target.x, target.z];
        }

        /// <summary>
        /// Returns a ray's planar origin: the selected corner for bursts, or a source-cell corner
        /// for other shapes. Each source corner is paired with all four target corners in order.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside <see cref="PhysicsRayCount"/>.</exception>
        public Vector2 RayStart(int ray)
        {
            ValidateRay(ray);
            return Shape == AreaShape.Burst
                ? new Vector2(OriginCorner.x, OriginCorner.y)
                : new Vector2(SourceCell.x + 0.5f, SourceCell.z + 0.5f) + CornerOffset(ray / 4);
        }

        /// <summary>Returns the planar target corner for the ray index. Target bounds are not checked.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside <see cref="PhysicsRayCount"/>.</exception>
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

        private static IReadOnlyList<int> CaptureOccupants(Tile tile)
        {
            if (tile == null)
                return Array.Empty<int>();

            int count = 0;
            foreach (GameObject occupant in tile.Occupants)
                if (occupant != null)
                    count++;
            if (count == 0)
                return Array.Empty<int>();

            // Capture is synchronous on the main thread, so occupancy and Unity object lifetime
            // cannot change between counting and copying. Empty cells need no collection allocation.
            int[] ids = new int[count];
            int index = 0;
            foreach (GameObject occupant in tile.Occupants)
                if (occupant != null)
                    ids[index++] = occupant.GetInstanceID();
            return Array.AsReadOnly(ids);
        }

        // Order corners by z, then x, from the negative to the positive offset on each axis.
        private static Vector2 CornerOffset(int corner) =>
            new((corner % 2 == 0 ? -0.4f : 0.4f), (corner < 2 ? -0.4f : 0.4f));
    }
}
