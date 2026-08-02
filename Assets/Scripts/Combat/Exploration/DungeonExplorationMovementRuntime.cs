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
    /// Projects rules-committed leader steps into one action-scoped logical trail while presenting
    /// each token on an independent movement channel.
    /// </summary>
    internal sealed class DungeonExplorationMovementRuntime
        : IExplorationStrideCoordinator,
            IExplorationPresentationDrain
    {
        private readonly ActionController[] party;
        private readonly Func<ActionController> selectedLeader;
        private readonly Func<bool> isCombatActive;
        private readonly Func<ActionController, bool> canParticipate;
        private readonly Func<bool> processEncounterBoundary;
        private readonly Func<DungeonCell, DungeonCell, bool> requiresEncounterBoundarySettlement;
        private readonly Func<bool> shouldInterruptStrideSuffix;
        private ExplorationPresentationState activePresentation;

        internal DungeonExplorationMovementRuntime(
            IEnumerable<ActionController> party,
            Func<ActionController> selectedLeader,
            Func<bool> isCombatActive,
            Func<ActionController, bool> canParticipate,
            Func<bool> processEncounterBoundary,
            Func<DungeonCell, DungeonCell, bool> requiresEncounterBoundarySettlement,
            Func<bool> shouldInterruptStrideSuffix
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
            this.requiresEncounterBoundarySettlement =
                requiresEncounterBoundarySettlement
                ?? throw new ArgumentNullException(nameof(requiresEncounterBoundarySettlement));
            this.shouldInterruptStrideSuffix =
                shouldInterruptStrideSuffix
                ?? throw new ArgumentNullException(nameof(shouldInterruptStrideSuffix));
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
        public bool IsPartyMember(GameObject character) =>
            character != null
            && party.Any(controller =>
                controller != null
                && controller.gameObject == character
                && canParticipate(controller)
            );

        /// <inheritdoc/>
        public bool TryCancelActiveTravel() => false;

        /// <inheritdoc/>
        public IEnumerator ProjectCommittedStep(
            GameObject leader,
            Vector3Int from,
            Vector3Int destination,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> continuePath,
            Ref<bool> pathInterrupted
        )
        {
            if (continuePath == null)
                throw new ArgumentNullException(nameof(continuePath));
            if (pathInterrupted == null)
                throw new ArgumentNullException(nameof(pathInterrupted));
            continuePath.Value = false;
            pathInterrupted.Value = false;
            if (!Handles(leader) || tiles == null || movement == null)
                yield break;

            if (
                activePresentation != null
                && activePresentation.Controllers.Any(controller => !canParticipate(controller))
            )
            {
                yield return DrainPendingFollowers(activePresentation);
                activePresentation = null;
            }
            if (activePresentation == null)
            {
                if (!TryCreatePresentationState(leader, out activePresentation))
                    yield break;
            }
            if (!activePresentation.MatchesLeaderAndOrigin(leader, from))
                yield break;

            ExplorationStepOutcome outcome = ExplorationStepPlanner.Plan(
                new ExplorationStepRequest(
                    activePresentation.Party,
                    new DungeonCell(destination.x, destination.z),
                    new LiveGridCellAvailability(tiles, activePresentation.Controllers)
                )
            );
            if (outcome is not AcceptedExplorationStepPlan accepted)
                yield break;

            if (accepted.IsLeaderSwap)
            {
                Ref<bool> swapped = new(false);
                yield return DrainPendingFollowers(activePresentation);
                yield return SwapMembers(
                    accepted,
                    activePresentation.ControllersById,
                    tiles,
                    movement,
                    swapped
                );
                if (!swapped.Value)
                    yield break;
                activePresentation.Party = accepted.ResultingParty;
                if (processEncounterBoundary())
                {
                    pathInterrupted.Value = true;
                    yield break;
                }
            }
            else
            {
                ExplorationMemberMove leaderMove = accepted.Moves[0];
                ActionController leaderController = activePresentation.ControllersById[
                    leaderMove.MemberId
                ];
                if (
                    !TryProjectCommittedLeader(
                        leaderController,
                        leaderMove,
                        tiles,
                        movement,
                        out TokenMovement.ExplorationMovementOperation leaderPresentation
                    )
                )
                {
                    yield break;
                }

                // Queue the next leader segment before settling the prior follower batch. This
                // keeps the leader continuous while preserving the invariant that boundary
                // observation never sees half-presented followers from the preceding cell.
                yield return DrainPendingFollowers(activePresentation);
                yield return leaderPresentation;
                if (leaderController == null)
                {
                    pathInterrupted.Value = true;
                    yield break;
                }
                if (processEncounterBoundary())
                {
                    pathInterrupted.Value = true;
                    yield break;
                }

                List<PendingFollowerPresentation> followers = new();
                bool settleEncounterBoundary = false;
                for (int moveIndex = 1; moveIndex < accepted.Moves.Count; moveIndex++)
                {
                    ExplorationMemberMove plannedMove = accepted.Moves[moveIndex];
                    ActionController controller = activePresentation.ControllersById[
                        plannedMove.MemberId
                    ];
                    PendingFollowerPresentation pending = new(controller);
                    Ref<bool> prepared = new(false);
                    yield return PrepareFollower(pending, plannedMove, tiles, movement, prepared);
                    if (!prepared.Value)
                    {
                        activePresentation.PendingFollowers = followers;
                        pathInterrupted.Value = true;
                        yield break;
                    }
                    followers.Add(pending);
                    settleEncounterBoundary |= requiresEncounterBoundarySettlement(
                        plannedMove.From,
                        plannedMove.To
                    );
                }
                activePresentation.PendingFollowers = followers;
                activePresentation.Party = accepted.ResultingParty;
                if (settleEncounterBoundary)
                {
                    // Only a follower batch that can activate an encounter blocks this root. Its
                    // committed moves must settle before encounter construction samples Unity,
                    // while ordinary batches remain pipelined with the next leader segment.
                    yield return DrainPendingFollowers(activePresentation);
                    if (processEncounterBoundary())
                    {
                        pathInterrupted.Value = true;
                        yield break;
                    }
                }
            }

            ActionController currentLeader = selectedLeader();
            if (shouldInterruptStrideSuffix())
            {
                pathInterrupted.Value = true;
                yield break;
            }
            continuePath.Value =
                !isCombatActive() && currentLeader != null && currentLeader.IsInDungeonExploration;
        }

        /// <inheritdoc/>
        public IEnumerator DrainPresentation(GameObject leader)
        {
            if (activePresentation == null || activePresentation.Leader != leader)
                yield break;

            yield return DrainPendingFollowers(activePresentation);
            activePresentation = null;
            processEncounterBoundary();
        }

        private bool TryCreatePresentationState(
            GameObject leader,
            out ExplorationPresentationState state
        )
        {
            state = null;
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
                if (controller.gameObject == leader)
                    leaderId = memberId;
            }
            if (leaderId.IsEmpty)
                return false;

            try
            {
                state = new ExplorationPresentationState(
                    leader,
                    new ExplorationPartyState(members, leaderId),
                    controllersById
                );
                return true;
            }
            catch (ArgumentException)
            {
                // Invalid live occupancy must stop movement rather than compound an overlap.
                return false;
            }
        }

        private static IEnumerator SwapMembers(
            AcceptedExplorationStepPlan plan,
            IReadOnlyDictionary<ExplorationMemberId, ActionController> controllersById,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> swapped
        )
        {
            swapped.Value = false;
            ExplorationMemberMove leaderMove = plan.Moves[0];
            ExplorationMemberMove allyMove = plan.Moves[1];
            ActionController leader = controllersById[leaderMove.MemberId];
            ActionController ally = controllersById[allyMove.MemberId];
            Vector3Int leaderPosition = Vector3Int.RoundToInt(leader.transform.position);
            Vector3Int allyPosition = Vector3Int.RoundToInt(ally.transform.position);
            Vector3Int leaderSourcePosition = new(
                leaderMove.From.X,
                leaderPosition.y,
                leaderMove.From.Z
            );
            Vector3Int allySourcePosition = new(allyMove.From.X, allyPosition.y, allyMove.From.Z);
            if (
                leaderPosition != leaderSourcePosition
                || allyPosition != allySourcePosition
                || !IsInBounds(tiles, leaderSourcePosition)
                || !IsInBounds(tiles, allySourcePosition)
            )
            {
                yield break;
            }

            Tile leaderSource = tiles[leaderSourcePosition.x, leaderSourcePosition.z];
            Tile allySource = tiles[allySourcePosition.x, allySourcePosition.z];
            if (
                leaderSource == null
                || allySource == null
                || !leaderSource.Occupants.Contains(leader.gameObject)
                || leaderSource.Occupants.Any(occupant => occupant != leader.gameObject)
                || !allySource.Occupants.Contains(ally.gameObject)
                || allySource.Occupants.Any(occupant => occupant != ally.gameObject)
            )
            {
                yield break;
            }

            Ref<bool> prevented = new(false);
            yield return allySource.RemoveToken(ally.gameObject, prevented);
            if (prevented.Value)
                yield break;
            if (!leaderSource.ProjectCommittedDeparture(leader.gameObject))
                yield break;
            allySource.ProjectCommittedArrival(leader.gameObject);

            yield return QueueMemberPresentation(leader, allySourcePosition, movement);
            CreaturePresentation allyPresentation = BeginFollowerPresentation(ally);
            yield return QueueMemberPresentation(ally, leaderSourcePosition, movement);
            EndFollowerPresentation(allyPresentation);
            yield return leaderSource.PlaceToken(ally.gameObject);
            swapped.Value = true;
        }

        private static bool TryProjectCommittedLeader(
            ActionController controller,
            ExplorationMemberMove plannedMove,
            Tile[,] tiles,
            TokenMovement movement,
            out TokenMovement.ExplorationMovementOperation operation
        )
        {
            operation = TokenMovement.ExplorationMovementOperation.Completed;
            if (controller == null)
                return false;
            Vector3Int current = Vector3Int.RoundToInt(controller.transform.position);
            Vector3Int expected = new(plannedMove.From.X, current.y, plannedMove.From.Z);
            Vector3Int destination = new(plannedMove.To.X, current.y, plannedMove.To.Z);
            if (
                current != expected
                || !IsInBounds(tiles, current)
                || !IsInBounds(tiles, destination)
            )
                return false;

            Tile source = tiles[current.x, current.z];
            Tile target = tiles[destination.x, destination.z];
            if (
                source == null
                || target == null
                || !source.Occupants.Contains(controller.gameObject)
                || target.Occupants.Any(occupant => occupant != controller.gameObject)
            )
                return false;

            if (!source.ProjectCommittedDeparture(controller.gameObject))
                return false;
            target.ProjectCommittedArrival(controller.gameObject);
            operation = QueueMemberPresentation(controller, destination, movement);
            return true;
        }

        private static IEnumerator PrepareFollower(
            PendingFollowerPresentation pending,
            ExplorationMemberMove plannedMove,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> prepared
        )
        {
            prepared.Value = false;
            ActionController controller = pending.Controller;
            if (controller == null)
                yield break;

            Vector3Int current = Vector3Int.RoundToInt(controller.transform.position);
            Vector3Int expected = new(plannedMove.From.X, current.y, plannedMove.From.Z);
            Vector3Int destination = new(plannedMove.To.X, current.y, plannedMove.To.Z);
            if (
                current != expected
                || !IsInBounds(tiles, current)
                || !IsInBounds(tiles, destination)
            )
                yield break;

            Tile source = tiles[current.x, current.z];
            Tile target = tiles[destination.x, destination.z];
            if (
                source == null
                || target == null
                || !source.Occupants.Contains(controller.gameObject)
                || target.Occupants.Any(occupant => occupant != controller.gameObject)
            )
                yield break;

            Ref<bool> prevented = new(false);
            yield return source.RemoveToken(controller.gameObject, prevented);
            if (prevented.Value || controller == null)
                yield break;

            CreaturePresentation presentation = BeginFollowerPresentation(controller);
            pending.Begin(
                target,
                presentation,
                QueueMemberPresentation(controller, destination, movement)
            );
            prepared.Value = true;
        }

        private static TokenMovement.ExplorationMovementOperation QueueMemberPresentation(
            ActionController controller,
            Vector3Int destination,
            TokenMovement movement
        )
        {
            CreaturePresentation presentation = controller.GetComponent<CreaturePresentation>();
            return presentation?.AnimationController != null
                ? movement.QueueExplorationWalk(controller.transform, destination)
                : movement.QueueExplorationHop(controller.transform, destination);
        }

        private static CreaturePresentation BeginFollowerPresentation(ActionController controller)
        {
            CreaturePresentation presentation = controller.GetComponent<CreaturePresentation>();
            float movementSpeed = controller.GetComponent<CreatureComponent>()?.speed ?? 25.0f;
            presentation?.SetMoving(true, movementSpeed);
            return presentation;
        }

        private static void EndFollowerPresentation(CreaturePresentation presentation) =>
            presentation?.SetMoving(false, 0.0f);

        private static IEnumerator DrainPendingFollowers(ExplorationPresentationState state)
        {
            IReadOnlyList<PendingFollowerPresentation> pendingFollowers = state.PendingFollowers;
            state.PendingFollowers = Array.Empty<PendingFollowerPresentation>();
            foreach (PendingFollowerPresentation pending in pendingFollowers)
                yield return pending.Operation;

            foreach (PendingFollowerPresentation pending in pendingFollowers)
                EndFollowerPresentation(pending.Presentation);

            foreach (PendingFollowerPresentation pending in pendingFollowers)
            {
                if (pending.Controller == null)
                    continue;
                if (
                    pending.Target.Occupants.Any(occupant =>
                        occupant != pending.Controller.gameObject
                    )
                )
                {
                    throw new InvalidOperationException(
                        "A queued follower destination became occupied before presentation settled."
                    );
                }
                yield return pending.Target.PlaceToken(pending.Controller.gameObject);
            }
        }

        private static bool IsInBounds(Tile[,] tiles, Vector3Int cell) =>
            cell.x >= 0
            && cell.z >= 0
            && cell.x < tiles.GetLength(0)
            && cell.z < tiles.GetLength(1);

        private sealed class ExplorationPresentationState
        {
            internal ExplorationPresentationState(
                GameObject leader,
                ExplorationPartyState party,
                IReadOnlyDictionary<ExplorationMemberId, ActionController> controllersById
            )
            {
                Leader = leader;
                Party = party;
                ControllersById = controllersById;
                Controllers = controllersById.Values.ToArray();
            }

            internal GameObject Leader { get; }

            internal ExplorationPartyState Party { get; set; }

            internal IReadOnlyDictionary<
                ExplorationMemberId,
                ActionController
            > ControllersById { get; }

            internal IReadOnlyList<ActionController> Controllers { get; }

            internal IReadOnlyList<PendingFollowerPresentation> PendingFollowers { get; set; } =
                Array.Empty<PendingFollowerPresentation>();

            internal bool MatchesLeaderAndOrigin(GameObject leader, Vector3Int origin)
            {
                if (Leader != leader || leader == null)
                    return false;
                ExplorationPartyMember selected = Party.SelectedLeader;
                return selected.Cell.X == origin.x && selected.Cell.Z == origin.z;
            }
        }

        private sealed class PendingFollowerPresentation
        {
            internal PendingFollowerPresentation(ActionController controller) =>
                Controller = controller;

            internal ActionController Controller { get; }

            internal Tile Target { get; private set; }

            internal CreaturePresentation Presentation { get; private set; }

            internal TokenMovement.ExplorationMovementOperation Operation { get; private set; } =
                TokenMovement.ExplorationMovementOperation.Completed;

            internal void Begin(
                Tile target,
                CreaturePresentation presentation,
                TokenMovement.ExplorationMovementOperation operation
            )
            {
                Target = target;
                Presentation = presentation;
                Operation = operation;
            }
        }

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
