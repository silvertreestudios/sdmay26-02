using System;
using System.Collections.Generic;
using GridPrivate;
using UnityEngine;

namespace GridPublic
{
    /// <summary>Records whether one captured tile occupant is affected by an area and has cover.</summary>
    public readonly struct AreaSelectedEntity
    {
        /// <summary>Unity instance ID of the occupant at capture time.</summary>
        public int EntityId { get; }

        /// <summary>Cell whose occupant list contained this entity when captured.</summary>
        public Vector3Int Cell { get; }

        /// <summary>Number of rays clear of both grid obstruction and map colliders.</summary>
        public int ClearRays { get; }

        /// <summary>Clear when a ray reaches the occupant or the request bypasses line of effect.</summary>
        public StrikeLineOfEffect LineOfEffect { get; }

        /// <summary>
        /// Standard cover when between one and fifteen rays are clear; otherwise no cover.
        /// This threshold also applies to bursts, which have at most four clear rays.
        /// </summary>
        public StrikeCover Cover =>
            ClearRays > 0 && ClearRays < 16 ? StrikeCover.Standard : StrikeCover.None;

        /// <summary>Whether the occupant was affected at capture time, even if it has since been destroyed.</summary>
        public bool IsAffected => LineOfEffect == StrikeLineOfEffect.Clear;

        internal AreaSelectedEntity(
            int entityId,
            Vector3Int cell,
            int clearRays,
            bool requiresLineOfEffect
        )
        {
            EntityId = entityId;
            Cell = cell;
            ClearRays = clearRays;
            LineOfEffect =
                !requiresLineOfEffect || clearRays > 0
                    ? StrikeLineOfEffect.Clear
                    : StrikeLineOfEffect.Blocked;
        }
    }

    /// <summary>
    /// Stores an area's cells and occupant outcomes together with the snapshot used to calculate them.
    /// Later scene changes cannot alter the result.
    /// </summary>
    public sealed class AreaSelectionSnapshot
    {
        /// <summary>Input snapshot used for geometry, occupant membership, and obstruction.</summary>
        public TargetingSnapshot Snapshot { get; }

        /// <summary>Read-only area cells in geometry order, including obstructed cells.</summary>
        public IReadOnlyList<Vector3Int> Cells { get; }

        /// <summary>Read-only occupants in cell and tile-list order, including blocked occupants and duplicates.</summary>
        public IReadOnlyList<AreaSelectedEntity> Creatures { get; }

        /// <summary>Whether the placement produces at least one area cell; an occupant is not required.</summary>
        public bool IsLegal => Cells.Count > 0;

        internal AreaSelectionSnapshot(
            TargetingSnapshot snapshot,
            List<Vector3Int> cells,
            List<AreaSelectedEntity> creatures
        )
        {
            Snapshot = snapshot;
            Cells = cells.AsReadOnly();
            Creatures = creatures.AsReadOnly();
        }
    }

    /// <summary>
    /// Captures an area query and retains the original Unity objects so its result can be returned
    /// as an <see cref="AreaTargetResult"/> for spell, aura, and selection callers.
    /// </summary>
    /// <remarks>
    /// Use on Unity's main thread and keep it within the selection or synchronous query that owns it.
    /// <see cref="Resolve"/> skips destroyed objects but does not recheck movement or obstruction.
    /// Create a new capture when confirming a preview or otherwise needing current legality.
    /// </remarks>
    public sealed class AreaTargetCapture
    {
        private readonly Dictionary<int, GameObject> entities;

        /// <summary>Immutable evaluation input; contains no Unity object references.</summary>
        public TargetingSnapshot Snapshot { get; }

        private AreaTargetCapture(TargetingSnapshot snapshot, Dictionary<int, GameObject> entities)
        {
            Snapshot = snapshot;
            this.entities = entities;
        }

        /// <summary>
        /// Synchronizes physics transforms, captures the area query, and remembers its original occupants.
        /// </summary>
        /// <remarks>
        /// Call on Unity's main thread after movement has updated grid occupancy. This method does
        /// not yield, so the copied IDs and retained objects belong to the same scene state.
        /// See <see cref="TargetingSnapshot.Capture"/> for argument and capture-cost details.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public static AreaTargetCapture Capture(
            AreaTargetSource source,
            Tile[,] tiles,
            AreaTargetRequest request,
            AreaPlacement placement
        )
        {
            Physics.SyncTransforms();
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                source,
                tiles,
                request,
                placement
            );
            Dictionary<int, GameObject> entities = new();
            foreach (Tile tile in tiles)
                if (tile != null)
                    foreach (GameObject occupant in tile.Occupants)
                        if (occupant != null)
                            entities[occupant.GetInstanceID()] = occupant;
            return new AreaTargetCapture(snapshot, entities);
        }

        /// <summary>
        /// Evaluates this capture and returns a mutable result referring to its surviving original objects.
        /// </summary>
        /// <returns>A new result, or null if the captured placement produces no area cells.</returns>
        /// <remarks>
        /// Destroyed occupants are omitted. Moved occupants retain their captured cell; current tile
        /// occupants are never substituted. Changes to the returned result do not alter the capture.
        /// This method does not establish whether the action is still legal in the current scene.
        /// </remarks>
        public AreaTargetResult Resolve()
        {
            AreaSelectionSnapshot selection = AreaTargeting.Evaluate(Snapshot);
            if (!selection.IsLegal)
                return null;
            AreaTargetResult result = new()
            {
                SnapshotId = Snapshot.Id,
                Placement = new AreaPlacement
                {
                    Shape = Snapshot.PlacementShape,
                    OriginCell =
                        Snapshot.Shape == AreaShape.Burst
                            ? Snapshot.PlacementOriginCell
                            : Snapshot.SourceCell,
                    OriginCorner = Snapshot.OriginCorner,
                    Direction = Snapshot.Direction,
                },
                Cells = new List<Vector3Int>(selection.Cells),
            };
            foreach (AreaSelectedEntity entity in selection.Creatures)
                if (
                    entities.TryGetValue(entity.EntityId, out GameObject original)
                    && original != null
                )
                    result.Creatures.Add(
                        new AreaAffectedCreature
                        {
                            Creature = original,
                            Cell = entity.Cell,
                            ClearRays = entity.ClearRays,
                            Cover = entity.Cover,
                            LineOfEffect = entity.LineOfEffect,
                        }
                    );
            return result;
        }
    }
}
