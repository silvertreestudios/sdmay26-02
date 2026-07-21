using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonPersistence.Repository;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.DungeonPersistence.Actors
{
    /// <summary>
    /// Prevalidates and commits actor transforms together with authoritative grid occupancy.
    /// Removing every registered mover before placing any destination makes swaps atomic.
    /// </summary>
    internal sealed class DungeonActorGridRestorePlan
    {
        private readonly GridBase grid;
        private readonly IReadOnlyList<ActorPositionRestore> actors;

        private DungeonActorGridRestorePlan(
            GridBase grid,
            IReadOnlyList<ActorPositionRestore> actors
        )
        {
            this.grid = grid;
            this.actors = actors;
        }

        internal static DungeonActorGridRestorePlan Preflight(
            IEnumerable<DungeonActorRestoreTarget> targets
        )
        {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));
            DungeonActorRestoreTarget[] copied = targets.ToArray();
            if (copied.Any(target => target == null))
                throw new ArgumentException(
                    "Restore targets cannot contain null.",
                    nameof(targets)
                );
            GridBase activeGrid = ResolveActiveGrid();
            if (activeGrid == null)
            {
                return new DungeonActorGridRestorePlan(
                    null,
                    Array.AsReadOnly(
                        copied.Select(target => new ActorPositionRestore(target)).ToArray()
                    )
                );
            }

            Tile[,] tiles = activeGrid.GetTiles();
            List<ActorPositionRestore> actors = copied
                .Select(target => new ActorPositionRestore(target))
                .ToList();
            HashSet<GameObject> relinquishingOccupants = new();
            foreach (ActorPositionRestore actor in actors)
            {
                if (!actor.IsRegisteredToken)
                    continue;
                actor.PreflightRegisteredOrigin(tiles);
                relinquishingOccupants.Add(actor.GameObject);
            }

            Dictionary<Vector2Int, ActorPositionRestore> livingDestinations = new();
            foreach (ActorPositionRestore actor in actors)
            {
                actor.PreflightDestination(tiles);
                if (actor.IsDefeated)
                    continue;
                Vector2Int destination = new(actor.SavedCell.X, actor.SavedCell.Z);
                if (livingDestinations.TryGetValue(destination, out ActorPositionRestore existing))
                    throw new InvalidOperationException(
                        $"Actors '{existing.InstanceId}' and '{actor.InstanceId}' both restore to cell "
                            + $"({destination.x}, {destination.y})."
                    );
                livingDestinations.Add(destination, actor);
            }

            foreach (ActorPositionRestore actor in livingDestinations.Values)
            {
                foreach (GameObject occupant in actor.DestinationTile.Occupants)
                {
                    if (occupant == null || !relinquishingOccupants.Contains(occupant))
                        throw new InvalidOperationException(
                            $"Actor '{actor.InstanceId}' restore destination "
                                + $"({actor.SavedCell.X}, {actor.SavedCell.Z}) is occupied by an actor outside the restore batch."
                        );
                }
            }

            return new DungeonActorGridRestorePlan(activeGrid, actors.AsReadOnly());
        }

        internal void Apply()
        {
            if (grid != null)
            {
                foreach (ActorPositionRestore actor in actors)
                {
                    if (actor.IsRegisteredToken)
                        actor.OriginTile.Occupants.Remove(actor.GameObject);
                }
                foreach (ActorPositionRestore actor in actors)
                {
                    if (actor.IsRegisteredToken && actor.IsDefeated)
                        actor.Token.DetachFromGrid(grid);
                }
            }

            foreach (ActorPositionRestore actor in actors)
                actor.ApplyTransform();

            if (grid == null)
                return;
            foreach (ActorPositionRestore actor in actors)
            {
                if (actor.IsRegisteredToken && !actor.IsDefeated)
                    actor.DestinationTile.Occupants.Add(actor.GameObject);
            }
        }

        private static GridBase ResolveActiveGrid()
        {
            if (
                !GridAPI.TryGetInstance(out GridAPI activeGrid)
                || activeGrid is not GridBase gridBase
                || !gridBase.IsInitialized
            )
                return null;
            return gridBase;
        }

        private sealed class ActorPositionRestore
        {
            private readonly float preservedY;

            internal ActorPositionRestore(DungeonActorRestoreTarget target)
            {
                GameObject = target.Controller.gameObject;
                InstanceId = target.State.InstanceId;
                SavedCell = target.State.Cell;
                IsDefeated = target.State.IsDefeated;
                Token = target.Controller.GetComponent<Token>();
                IsRegisteredToken = Token != null && Token.IsRegistered;
                preservedY = target.Controller.transform.position.y;
            }

            internal GameObject GameObject { get; }
            internal string InstanceId { get; }
            internal DungeonSaveCell SavedCell { get; }
            internal bool IsDefeated { get; }
            internal Token Token { get; }
            internal bool IsRegisteredToken { get; }
            internal Tile OriginTile { get; private set; }
            internal Tile DestinationTile { get; private set; }

            internal void PreflightRegisteredOrigin(Tile[,] tiles)
            {
                Vector3Int origin = Vector3Int.RoundToInt(GameObject.transform.position);
                if (!IsInBounds(tiles, origin.x, origin.z))
                    throw new InvalidOperationException(
                        $"Registered actor '{InstanceId}' has an out-of-bounds origin "
                            + $"({origin.x}, {origin.z})."
                    );
                OriginTile = tiles[origin.x, origin.z];
                if (
                    OriginTile == null
                    || OriginTile.Occupants.Count != 1
                    || OriginTile.Occupants[0] != GameObject
                )
                    throw new InvalidOperationException(
                        $"Registered actor '{InstanceId}' does not exclusively own its recorded grid origin."
                    );
                if (!Token.IsRegistered)
                    throw new InvalidOperationException(
                        $"Actor '{InstanceId}' stopped being registered during restore preflight."
                    );
            }

            internal void PreflightDestination(Tile[,] tiles)
            {
                if (!IsInBounds(tiles, SavedCell.X, SavedCell.Z))
                    throw new InvalidOperationException(
                        $"Actor '{InstanceId}' restore destination ({SavedCell.X}, {SavedCell.Z}) is outside the active grid."
                    );
                DestinationTile = tiles[SavedCell.X, SavedCell.Z];
                if (DestinationTile == null)
                    throw new InvalidOperationException(
                        $"Actor '{InstanceId}' restore destination ({SavedCell.X}, {SavedCell.Z}) is blocked."
                    );
            }

            internal void ApplyTransform()
            {
                GameObject.transform.position = new Vector3(SavedCell.X, preservedY, SavedCell.Z);
            }

            private static bool IsInBounds(Tile[,] tiles, int x, int z) =>
                tiles != null
                && x >= 0
                && z >= 0
                && x < tiles.GetLength(0)
                && z < tiles.GetLength(1);
        }
    }
}
