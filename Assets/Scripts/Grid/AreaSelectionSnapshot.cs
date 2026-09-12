using System;
using System.Collections.Generic;
using GridPrivate;
using UnityEngine;

namespace GridPublic
{
    /// <summary>Immutable occupant outcome. Identity and cell belong to the captured tile membership.</summary>
    public readonly struct AreaSelectedEntity
    {
        /// <summary>Scene-local ID, never a lookup for a replacement object.</summary>
        public int EntityId { get; }

        /// <summary>Captured membership cell, not a live transform read.</summary>
        public Vector3Int Cell { get; }

        /// <summary>Raw clear rays, retained even when obstruction is ignored.</summary>
        public int ClearRays { get; }

        /// <summary>Effective eligibility under the captured request.</summary>
        public StrikeLineOfEffect LineOfEffect { get; }

        /// <summary>Existing area cover policy, including the sixteen-ray burst threshold.</summary>
        public StrikeCover Cover =>
            ClearRays > 0 && ClearRays < 16 ? StrikeCover.Standard : StrikeCover.None;

        /// <summary>Historical eligibility, independent of the object's current lifetime.</summary>
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

    /// <summary>Pure immutable area output, retaining its exact query capture for provenance.</summary>
    public sealed class AreaSelectionSnapshot
    {
        /// <summary>All inputs used to compute this result; identity does not establish freshness.</summary>
        public TargetingSnapshot Snapshot { get; }

        /// <summary>Ordered geometry, including obstructed cells.</summary>
        public IReadOnlyList<Vector3Int> Cells { get; }

        /// <summary>Ordered candidates with eligibility and cover; blocked candidates remain reportable.</summary>
        public IReadOnlyList<AreaSelectedEntity> Creatures { get; }

        /// <summary>Whether the placed area contains legal geometry.</summary>
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
    /// Main-thread Unity boundary owning a capture and its exact original objects. Keep local to a
    /// selection; discard on exit. Historical resolution checks lifetime only, not current legality.
    /// </summary>
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
        /// Synchronizes physics and captures inputs plus exact-object mappings without yielding.
        /// Grid occupancy must already reflect committed movement. Recapture for every preview and confirmation.
        /// </summary>
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
        /// Adapts historical output to the existing Unity DTO. Missing originals are omitted, never
        /// replaced by current occupants. Null denotes illegal geometry at this legacy boundary.
        /// </summary>
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
