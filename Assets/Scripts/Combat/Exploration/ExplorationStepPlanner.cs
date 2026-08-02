using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Game.DungeonGeneration;

namespace Game.Combat.Exploration
{
    /// <summary>
    /// Reports structural walkability and external reservations without treating current party
    /// occupancy as blocked; the planner owns party-occupancy validation.
    /// </summary>
    public interface IExplorationCellAvailability
    {
        /// <summary>Checks whether a party member may occupy a structurally valid, unreserved cell.</summary>
        /// <param name="cell">The candidate destination.</param>
        /// <returns><see langword="true"/> when terrain and external reservations permit entry.</returns>
        bool CanOccupy(DungeonCell cell);
    }

    /// <summary>Supplies every value required to plan one deterministic leader step.</summary>
    public sealed class ExplorationStepRequest
    {
        /// <summary>Creates an immutable one-step request.</summary>
        /// <param name="party">The required current living party state.</param>
        /// <param name="leaderDestination">The selected leader's proposed destination.</param>
        /// <param name="availability">The required terrain and reservation policy.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="party"/> or <paramref name="availability"/> is null.
        /// </exception>
        public ExplorationStepRequest(
            ExplorationPartyState party,
            DungeonCell leaderDestination,
            IExplorationCellAvailability availability
        )
        {
            Party = party ?? throw new ArgumentNullException(nameof(party));
            Availability = availability ?? throw new ArgumentNullException(nameof(availability));
            LeaderDestination = leaderDestination;
        }

        /// <summary>Gets the current living party state.</summary>
        public ExplorationPartyState Party { get; }

        /// <summary>Gets the selected leader's proposed destination.</summary>
        public DungeonCell LeaderDestination { get; }

        /// <summary>Gets the structural walkability and reservation policy.</summary>
        public IExplorationCellAvailability Availability { get; }
    }

    /// <summary>Identifies why a proposed leader step cannot begin.</summary>
    public enum ExplorationStepRejectionReason
    {
        /// <summary>The leader destination is not exactly one adjacent cell away.</summary>
        LeaderStepIsNotAdjacent,

        /// <summary>The terrain or an external reservation prevents leader entry.</summary>
        LeaderDestinationUnavailable,
    }

    /// <summary>Describes one adjacent member move in deterministic execution order.</summary>
    public readonly struct ExplorationMemberMove
    {
        internal ExplorationMemberMove(
            ExplorationMemberId memberId,
            DungeonCell from,
            DungeonCell to
        )
        {
            MemberId = memberId;
            From = from;
            To = to;
        }

        /// <summary>Gets the moving member's stable identity.</summary>
        public ExplorationMemberId MemberId { get; }

        /// <summary>Gets the member's cell before this party step.</summary>
        public DungeonCell From { get; }

        /// <summary>Gets the member's adjacent destination.</summary>
        public DungeonCell To { get; }
    }

    /// <summary>Represents either an accepted immutable step plan or an explicit rejection.</summary>
    public abstract class ExplorationStepOutcome
    {
        private protected ExplorationStepOutcome() { }
    }

    /// <summary>
    /// Contains a non-empty ordered move list and the complete non-overlapping party state after
    /// those moves execute.
    /// </summary>
    public sealed class AcceptedExplorationStepPlan : ExplorationStepOutcome
    {
        private readonly ReadOnlyCollection<ExplorationMemberMove> moves;

        internal AcceptedExplorationStepPlan(
            IEnumerable<ExplorationMemberMove> moves,
            ExplorationPartyState resultingParty
        )
        {
            ExplorationMemberMove[] copiedMoves = moves.ToArray();
            if (copiedMoves.Length == 0)
                throw new ArgumentException(
                    "An accepted step must move its leader.",
                    nameof(moves)
                );

            this.moves = Array.AsReadOnly(copiedMoves);
            ResultingParty =
                resultingParty ?? throw new ArgumentNullException(nameof(resultingParty));
        }

        /// <summary>
        /// Gets moved members in logical order: leader first, then either the ally swapping into
        /// the leader's prior cell or followers in trail order. Adjacent followers consume their
        /// predecessor's prior cell; a separated follower takes one adjacent catch-up step toward
        /// that trail, with stable roster order breaking ties.
        /// </summary>
        public IReadOnlyList<ExplorationMemberMove> Moves => moves;

        /// <summary>Gets every member's immutable state after the planned moves.</summary>
        public ExplorationPartyState ResultingParty { get; }

        /// <summary>
        /// Gets whether the leader and exactly one ally exchange their starting cells. Unity must
        /// project these logical moves atomically because neither destination starts empty.
        /// </summary>
        internal bool IsLeaderSwap =>
            moves.Count == 2 && moves[0].From == moves[1].To && moves[0].To == moves[1].From;
    }

    /// <summary>Reports an explicit reason why the selected leader cannot take the proposed step.</summary>
    public sealed class RejectedExplorationStepPlan : ExplorationStepOutcome
    {
        internal RejectedExplorationStepPlan(ExplorationStepRejectionReason reason)
        {
            Reason = reason;
        }

        /// <summary>Gets the structural reason the leader step was rejected.</summary>
        public ExplorationStepRejectionReason Reason { get; }
    }

    /// <summary>
    /// Plans one adjacent leader move followed by deterministic follower steps. Connected followers
    /// consume predecessor cells, while followers separated during combat advance one adjacent cell
    /// toward the trail. Cardinal proximity and then stable roster order resolve connected ties.
    /// </summary>
    public static class ExplorationStepPlanner
    {
        /// <summary>Plans one leader/follower party step without mutating the supplied state.</summary>
        /// <param name="request">The complete non-null planning request.</param>
        /// <returns>
        /// An accepted plan when the leader can move, even if a follower tail must hold; otherwise
        /// an explicit rejection that contains no partial movement.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
        public static ExplorationStepOutcome Plan(ExplorationStepRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ExplorationPartyState party = request.Party;
            ExplorationPartyMember leader = party.SelectedLeader;
            DungeonCell destination = request.LeaderDestination;
            if (!AreAdjacent(leader.Cell, destination))
            {
                return new RejectedExplorationStepPlan(
                    ExplorationStepRejectionReason.LeaderStepIsNotAdjacent
                );
            }
            if (!request.Availability.CanOccupy(destination))
            {
                return new RejectedExplorationStepPlan(
                    ExplorationStepRejectionReason.LeaderDestinationUnavailable
                );
            }

            ExplorationPartyMember[] resultingMembers = party.Members.ToArray();
            ExplorationPartyMember destinationOccupant = party.Members.FirstOrDefault(member =>
                member.Id != leader.Id && member.Cell == destination
            );
            if (!destinationOccupant.Id.IsEmpty)
            {
                resultingMembers[IndexOf(resultingMembers, leader.Id)] = new ExplorationPartyMember(
                    leader.Id,
                    destination
                );
                resultingMembers[IndexOf(resultingMembers, destinationOccupant.Id)] =
                    new ExplorationPartyMember(destinationOccupant.Id, leader.Cell);
                return new AcceptedExplorationStepPlan(
                    new[]
                    {
                        new ExplorationMemberMove(leader.Id, leader.Cell, destination),
                        new ExplorationMemberMove(
                            destinationOccupant.Id,
                            destinationOccupant.Cell,
                            leader.Cell
                        ),
                    },
                    new ExplorationPartyState(resultingMembers, party.SelectedLeaderId)
                );
            }

            List<ExplorationMemberMove> moves = new() { new(leader.Id, leader.Cell, destination) };
            int leaderIndex = IndexOf(resultingMembers, leader.Id);
            resultingMembers[leaderIndex] = new ExplorationPartyMember(leader.Id, destination);

            DungeonCell predecessorPriorCell = leader.Cell;
            List<ExplorationPartyMember> followers = party
                .Members.Where(member => member.Id != leader.Id)
                .ToList();
            while (followers.Count > 0)
            {
                int followerIndex = FindFollowerIndex(followers, predecessorPriorCell);
                if (followerIndex < 0)
                    followerIndex = FindClosestFollowerIndex(followers, predecessorPriorCell);

                ExplorationPartyMember follower = followers[followerIndex];
                DungeonCell followerDestination = predecessorPriorCell;
                if (
                    !AreAdjacent(follower.Cell, followerDestination)
                    && !TryFindCatchUpStep(
                        follower,
                        predecessorPriorCell,
                        request.Availability,
                        resultingMembers,
                        out followerDestination
                    )
                )
                {
                    break;
                }
                if (!request.Availability.CanOccupy(followerDestination))
                    break;

                moves.Add(
                    new ExplorationMemberMove(follower.Id, follower.Cell, followerDestination)
                );
                int rosterIndex = IndexOf(resultingMembers, follower.Id);
                resultingMembers[rosterIndex] = new ExplorationPartyMember(
                    follower.Id,
                    followerDestination
                );
                predecessorPriorCell = follower.Cell;
                followers.RemoveAt(followerIndex);
            }

            return new AcceptedExplorationStepPlan(
                moves,
                new ExplorationPartyState(resultingMembers, party.SelectedLeaderId)
            );
        }

        private static int IndexOf(
            IReadOnlyList<ExplorationPartyMember> members,
            ExplorationMemberId memberId
        )
        {
            for (int index = 0; index < members.Count; index++)
            {
                if (members[index].Id == memberId)
                    return index;
            }

            throw new InvalidOperationException(
                "The selected exploration leader disappeared from the party state."
            );
        }

        private static bool AreAdjacent(DungeonCell first, DungeonCell second)
        {
            long xDistance = Math.Abs((long)first.X - second.X);
            long zDistance = Math.Abs((long)first.Z - second.Z);
            return xDistance <= 1 && zDistance <= 1 && xDistance + zDistance > 0;
        }

        private static bool AreCardinalNeighbors(DungeonCell first, DungeonCell second)
        {
            long xDistance = Math.Abs((long)first.X - second.X);
            long zDistance = Math.Abs((long)first.Z - second.Z);
            return xDistance + zDistance == 1;
        }

        private static int FindFollowerIndex(
            IReadOnlyList<ExplorationPartyMember> followers,
            DungeonCell predecessorPriorCell
        )
        {
            for (int index = 0; index < followers.Count; index++)
            {
                if (AreCardinalNeighbors(followers[index].Cell, predecessorPriorCell))
                    return index;
            }
            for (int index = 0; index < followers.Count; index++)
            {
                if (AreAdjacent(followers[index].Cell, predecessorPriorCell))
                    return index;
            }
            return -1;
        }

        private static int FindClosestFollowerIndex(
            IReadOnlyList<ExplorationPartyMember> followers,
            DungeonCell target
        )
        {
            int selectedIndex = 0;
            long selectedDistance = ChebyshevDistance(followers[0].Cell, target);
            for (int index = 1; index < followers.Count; index++)
            {
                long distance = ChebyshevDistance(followers[index].Cell, target);
                if (distance < selectedDistance)
                {
                    selectedIndex = index;
                    selectedDistance = distance;
                }
            }
            return selectedIndex;
        }

        private static bool TryFindCatchUpStep(
            ExplorationPartyMember follower,
            DungeonCell target,
            IExplorationCellAvailability availability,
            IReadOnlyList<ExplorationPartyMember> resultingMembers,
            out DungeonCell destination
        )
        {
            long currentDistance = ChebyshevDistance(follower.Cell, target);
            DungeonCell[] candidates =
            {
                new(follower.Cell.X, follower.Cell.Z + 1),
                new(follower.Cell.X + 1, follower.Cell.Z),
                new(follower.Cell.X, follower.Cell.Z - 1),
                new(follower.Cell.X - 1, follower.Cell.Z),
                new(follower.Cell.X + 1, follower.Cell.Z + 1),
                new(follower.Cell.X + 1, follower.Cell.Z - 1),
                new(follower.Cell.X - 1, follower.Cell.Z - 1),
                new(follower.Cell.X - 1, follower.Cell.Z + 1),
            };
            DungeonCell[] selected = candidates
                .Where(candidate =>
                    ChebyshevDistance(candidate, target) < currentDistance
                    && availability.CanOccupy(candidate)
                    && resultingMembers.All(member =>
                        member.Id == follower.Id || member.Cell != candidate
                    )
                )
                .OrderBy(candidate => ChebyshevDistance(candidate, target))
                .ThenBy(candidate => ManhattanDistance(candidate, target))
                .Take(1)
                .ToArray();
            if (selected.Length == 0)
            {
                destination = default;
                return false;
            }

            destination = selected[0];
            return true;
        }

        private static long ChebyshevDistance(DungeonCell first, DungeonCell second) =>
            Math.Max(Math.Abs((long)first.X - second.X), Math.Abs((long)first.Z - second.Z));

        private static long ManhattanDistance(DungeonCell first, DungeonCell second) =>
            Math.Abs((long)first.X - second.X) + Math.Abs((long)first.Z - second.Z);
    }

    /// <summary>Contains one immutable complete destination route, including its origin.</summary>
    public sealed class ExplorationTravelPlan
    {
        private readonly ReadOnlyCollection<DungeonCell> cells;

        internal ExplorationTravelPlan(IEnumerable<DungeonCell> cells) =>
            this.cells = Array.AsReadOnly(cells.ToArray());

        /// <summary>Gets the complete ordered route from origin through destination.</summary>
        public IReadOnlyList<DungeonCell> Cells => cells;

        /// <summary>Gets the selected destination.</summary>
        public DungeonCell Destination => cells[cells.Count - 1];
    }

    /// <summary>Validates and freezes a pathfinder-produced exploration destination route.</summary>
    public static class ExplorationTravelPlanner
    {
        /// <summary>Creates a complete route plan without mutating or retaining the supplied list.</summary>
        /// <param name="route">Ordered adjacent cells including both origin and destination.</param>
        /// <returns>The immutable complete travel plan.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="route"/> is null.</exception>
        /// <exception cref="ArgumentException">The route is empty or contains a non-adjacent step.</exception>
        public static ExplorationTravelPlan Plan(IEnumerable<DungeonCell> route)
        {
            DungeonCell[] copied =
                route?.ToArray() ?? throw new ArgumentNullException(nameof(route));
            if (copied.Length < 2)
                throw new ArgumentException(
                    "Destination travel requires an origin and a different destination.",
                    nameof(route)
                );
            for (int index = 1; index < copied.Length; index++)
            {
                long x = Math.Abs((long)copied[index].X - copied[index - 1].X);
                long z = Math.Abs((long)copied[index].Z - copied[index - 1].Z);
                if (x > 1 || z > 1 || x + z == 0)
                    throw new ArgumentException(
                        "Every destination route step must enter one adjacent cell.",
                        nameof(route)
                    );
            }
            return new ExplorationTravelPlan(copied);
        }
    }
}
