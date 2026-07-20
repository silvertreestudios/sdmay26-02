using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using Game.KayKit;
using GridPrivate;
using UnityEngine;

namespace Game.Combat.Exploration
{
    /// <summary>
    /// Projects live Unity party/grid state into the pure step planner and commits its ordered
    /// movement one member at a time.
    /// </summary>
    internal sealed class DungeonExplorationMovementRuntime : IExplorationStrideCoordinator
    {
        private readonly ActionController[] party;
        private readonly Func<ActionController> selectedLeader;
        private readonly Func<bool> isCombatActive;
        private readonly Func<ActionController, bool> canParticipate;
        private readonly Func<bool> processEncounterBoundary;

        internal DungeonExplorationMovementRuntime(
            IEnumerable<ActionController> party,
            Func<ActionController> selectedLeader,
            Func<bool> isCombatActive,
            Func<ActionController, bool> canParticipate,
            Func<bool> processEncounterBoundary
        )
        {
            this.party = party?.ToArray() ?? throw new ArgumentNullException(nameof(party));
            this.selectedLeader =
                selectedLeader ?? throw new ArgumentNullException(nameof(selectedLeader));
            this.isCombatActive =
                isCombatActive ?? throw new ArgumentNullException(nameof(isCombatActive));
            this.canParticipate =
                canParticipate ?? throw new ArgumentNullException(nameof(canParticipate));
            this.processEncounterBoundary =
                processEncounterBoundary
                ?? throw new ArgumentNullException(nameof(processEncounterBoundary));
        }

        /// <inheritdoc/>
        public bool Handles(GameObject character)
        {
            ActionController leader = selectedLeader();
            return !isCombatActive()
                && leader != null
                && leader.gameObject == character
                && leader.IsInDungeonExploration;
        }

        /// <inheritdoc/>
        public IEnumerator ExecuteStep(
            GameObject leader,
            Vector3Int destination,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> continuePath
        )
        {
            if (continuePath == null)
                throw new ArgumentNullException(nameof(continuePath));
            continuePath.Value = false;
            if (!Handles(leader) || tiles == null || movement == null)
                yield break;

            ActionController[] livingParty = party.Where(canParticipate).ToArray();
            Dictionary<ExplorationMemberId, ActionController> controllersById = new();
            List<ExplorationPartyMember> members = new(livingParty.Length);
            ExplorationMemberId leaderId = default;
            for (int index = 0; index < livingParty.Length; index++)
            {
                ActionController controller = livingParty[index];
                ExplorationMemberId memberId = new($"party-{Array.IndexOf(party, controller):D4}");
                Vector3Int position = Vector3Int.RoundToInt(controller.transform.position);
                controllersById.Add(memberId, controller);
                members.Add(
                    new ExplorationPartyMember(memberId, new DungeonCell(position.x, position.z))
                );
                if (controller == selectedLeader())
                    leaderId = memberId;
            }
            if (leaderId.IsEmpty)
                yield break;

            ExplorationPartyState partyState;
            try
            {
                partyState = new ExplorationPartyState(members, leaderId);
            }
            catch (ArgumentException)
            {
                // Invalid live occupancy must stop movement rather than compound an overlap.
                yield break;
            }

            ExplorationStepOutcome outcome = ExplorationStepPlanner.Plan(
                new ExplorationStepRequest(
                    partyState,
                    new DungeonCell(destination.x, destination.z),
                    new LiveGridCellAvailability(tiles, livingParty)
                )
            );
            if (outcome is not AcceptedExplorationStepPlan accepted)
                yield break;

            foreach (ExplorationMemberMove plannedMove in accepted.Moves)
            {
                ActionController controller = controllersById[plannedMove.MemberId];
                Ref<bool> moved = new(false);
                yield return MoveMember(
                    controller,
                    plannedMove,
                    tiles,
                    movement,
                    controller == selectedLeader(),
                    moved
                );
                if (!moved.Value || processEncounterBoundary())
                    yield break;
            }

            ActionController currentLeader = selectedLeader();
            continuePath.Value =
                !isCombatActive() && currentLeader != null && currentLeader.IsInDungeonExploration;
        }

        private static IEnumerator MoveMember(
            ActionController controller,
            ExplorationMemberMove plannedMove,
            Tile[,] tiles,
            TokenMovement movement,
            bool isLeader,
            Ref<bool> moved
        )
        {
            moved.Value = false;
            Vector3Int current = Vector3Int.RoundToInt(controller.transform.position);
            Vector3Int expected = new(plannedMove.From.X, current.y, plannedMove.From.Z);
            Vector3Int destination = new(plannedMove.To.X, current.y, plannedMove.To.Z);
            if (
                current != expected
                || !IsInBounds(tiles, current)
                || !IsInBounds(tiles, destination)
            )
            {
                yield break;
            }

            Tile source = tiles[current.x, current.z];
            Tile target = tiles[destination.x, destination.z];
            if (
                source == null
                || target == null
                || !source.Occupants.Contains(controller.gameObject)
                || target.Occupants.Any(occupant => occupant != controller.gameObject)
            )
            {
                yield break;
            }

            Ref<bool> prevented = new(false);
            yield return source.RemoveToken(controller.gameObject, prevented);
            if (prevented.Value)
                yield break;

            CreaturePresentation presentation = controller.GetComponent<CreaturePresentation>();
            float movementSpeed = controller.GetComponent<CreatureComponent>()?.speed ?? 25.0f;
            if (!isLeader)
                presentation?.SetMoving(true, movementSpeed);
            try
            {
                if (presentation?.AnimationController != null)
                    yield return movement.Walk(controller.transform, destination);
                else
                    yield return movement.Hop(controller.transform, destination);
                yield return target.PlaceToken(controller.gameObject);
                moved.Value = true;
            }
            finally
            {
                if (!isLeader)
                    presentation?.SetMoving(false, 0.0f);
            }
        }

        private static bool IsInBounds(Tile[,] tiles, Vector3Int cell) =>
            cell.x >= 0
            && cell.z >= 0
            && cell.x < tiles.GetLength(0)
            && cell.z < tiles.GetLength(1);

        private sealed class LiveGridCellAvailability : IExplorationCellAvailability
        {
            private readonly Tile[,] tiles;
            private readonly HashSet<GameObject> partyObjects;

            internal LiveGridCellAvailability(
                Tile[,] tiles,
                IEnumerable<ActionController> livingParty
            )
            {
                this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
                partyObjects = new HashSet<GameObject>(
                    (livingParty ?? throw new ArgumentNullException(nameof(livingParty))).Select(
                        controller => controller.gameObject
                    )
                );
            }

            public bool CanOccupy(DungeonCell cell)
            {
                if (
                    cell.X < 0
                    || cell.Z < 0
                    || cell.X >= tiles.GetLength(0)
                    || cell.Z >= tiles.GetLength(1)
                )
                {
                    return false;
                }

                Tile tile = tiles[cell.X, cell.Z];
                return tile != null
                    && tile.Occupants.All(occupant =>
                        occupant != null && partyObjects.Contains(occupant)
                    );
            }
        }
    }
}
