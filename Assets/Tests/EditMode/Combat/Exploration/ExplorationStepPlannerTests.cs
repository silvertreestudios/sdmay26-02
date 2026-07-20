using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Exploration;
using Game.DungeonGeneration;
using NUnit.Framework;

namespace Game.Tests.Combat.Exploration
{
    /// <summary>Verifies deterministic leader/follower planning without Unity scene state.</summary>
    public sealed class ExplorationStepPlannerTests
    {
        /// <summary>Verifies an arbitrary larger roster follows in stable roster order.</summary>
        [Test]
        public void Plan_MovesFourMemberPartyInStableRosterOrder()
        {
            ExplorationPartyState party = Party(
                "a",
                Member("a", 0, 0),
                Member("b", -1, 0),
                Member("c", -2, 0),
                Member("d", -3, 0)
            );

            AcceptedExplorationStepPlan plan = Accepted(
                ExplorationStepPlanner.Plan(
                    new ExplorationStepRequest(party, Cell(1, 0), OpenCells())
                )
            );

            Assert.That(
                plan.Moves.Select(move => move.MemberId.Value),
                Is.EqualTo(new[] { "a", "b", "c", "d" })
            );
            Assert.That(Cells(plan.ResultingParty), Is.EqualTo(new[] { 1, 0, 0, 0, -1, 0, -2, 0 }));
            AssertEveryMoveIsCardinal(plan);
        }

        /// <summary>Verifies followers turn a corner by consuming predecessor prior cells.</summary>
        [Test]
        public void Plan_FollowersTrackLeaderAroundCorner()
        {
            ExplorationPartyState party = Party(
                "a",
                Member("a", 0, 0),
                Member("b", -1, 0),
                Member("c", -2, 0)
            );
            AcceptedExplorationStepPlan east = Accepted(
                ExplorationStepPlanner.Plan(
                    new ExplorationStepRequest(party, Cell(1, 0), OpenCells())
                )
            );

            AcceptedExplorationStepPlan north = Accepted(
                ExplorationStepPlanner.Plan(
                    new ExplorationStepRequest(east.ResultingParty, Cell(1, 1), OpenCells())
                )
            );

            Assert.That(Cells(north.ResultingParty), Is.EqualTo(new[] { 1, 1, 1, 0, 0, 0 }));
            AssertEveryMoveIsCardinal(north);
        }

        /// <summary>Verifies changing to any leader never teleports disconnected followers.</summary>
        [Test]
        public void SelectLeader_DisconnectedStableFollowerTailHoldsWithoutTeleporting()
        {
            ExplorationPartyState party = Party(
                    "a",
                    Member("a", 1, 0),
                    Member("b", 0, 0),
                    Member("c", -1, 0)
                )
                .SelectLeader(Id("c"));

            AcceptedExplorationStepPlan plan = Accepted(
                ExplorationStepPlanner.Plan(
                    new ExplorationStepRequest(party, Cell(-1, 1), OpenCells())
                )
            );

            Assert.That(plan.ResultingParty.SelectedLeaderId, Is.EqualTo(Id("c")));
            Assert.That(plan.Moves.Select(move => move.MemberId.Value), Is.EqualTo(new[] { "c" }));
            Assert.That(Cells(plan.ResultingParty), Is.EqualTo(new[] { 1, 0, 0, 0, -1, 1 }));
            AssertEveryMoveIsCardinal(plan);
        }

        /// <summary>Verifies a reserved follower target holds that follower and the remaining tail.</summary>
        [Test]
        public void Plan_ReservedFollowerTargetHoldsFollowerAndDownstreamMembers()
        {
            ExplorationPartyState party = Party(
                "a",
                Member("a", 0, 0),
                Member("b", -1, 0),
                Member("c", -2, 0),
                Member("d", -3, 0)
            );

            AcceptedExplorationStepPlan plan = Accepted(
                ExplorationStepPlanner.Plan(
                    new ExplorationStepRequest(party, Cell(1, 0), OpenCells(Cell(-1, 0)))
                )
            );

            Assert.That(
                plan.Moves.Select(move => move.MemberId.Value),
                Is.EqualTo(new[] { "a", "b" })
            );
            Assert.That(Cells(plan.ResultingParty), Is.EqualTo(new[] { 1, 0, 0, 0, -2, 0, -3, 0 }));
            Assert.That(
                plan.ResultingParty.Members.Select(member => member.Cell).Distinct().Count(),
                Is.EqualTo(4)
            );
            AssertEveryMoveIsCardinal(plan);
        }

        /// <summary>Verifies a closed or otherwise unwalkable leader destination rejects all movement.</summary>
        [Test]
        public void Plan_UnwalkableLeaderDestinationIsRejected()
        {
            ExplorationPartyState party = Party("a", Member("a", 0, 0), Member("b", -1, 0));

            RejectedExplorationStepPlan rejected = Rejected(
                ExplorationStepPlanner.Plan(
                    new ExplorationStepRequest(party, Cell(1, 0), OpenCells(Cell(1, 0)))
                )
            );

            Assert.That(
                rejected.Reason,
                Is.EqualTo(ExplorationStepRejectionReason.LeaderDestinationUnavailable)
            );
            Assert.That(Cells(party), Is.EqualTo(new[] { 0, 0, -1, 0 }));
        }

        /// <summary>Verifies an occupied leader destination cannot produce an overlapping plan.</summary>
        [Test]
        public void Plan_OccupiedLeaderDestinationIsRejected()
        {
            ExplorationPartyState party = Party(
                "a",
                Member("a", 0, 0),
                Member("b", 1, 0),
                Member("c", 2, 0)
            );

            RejectedExplorationStepPlan rejected = Rejected(
                ExplorationStepPlanner.Plan(
                    new ExplorationStepRequest(party, Cell(1, 0), OpenCells())
                )
            );

            Assert.That(
                rejected.Reason,
                Is.EqualTo(ExplorationStepRejectionReason.LeaderDestinationOccupied)
            );
        }

        /// <summary>Verifies non-cardinal leader movement is rejected instead of teleporting.</summary>
        [Test]
        public void Plan_NonCardinalLeaderStepIsRejected()
        {
            ExplorationPartyState party = Party("a", Member("a", 0, 0));

            RejectedExplorationStepPlan rejected = Rejected(
                ExplorationStepPlanner.Plan(
                    new ExplorationStepRequest(party, Cell(1, 1), OpenCells())
                )
            );

            Assert.That(
                rejected.Reason,
                Is.EqualTo(ExplorationStepRejectionReason.LeaderStepIsNotCardinal)
            );
        }

        /// <summary>Verifies party construction rejects every ambiguous or invalid roster shape.</summary>
        [Test]
        public void PartyState_InvalidRosterInputsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new ExplorationPartyState(null, Id("a")));
            Assert.Throws<ArgumentException>(() =>
                new ExplorationPartyState(Array.Empty<ExplorationPartyMember>(), Id("a"))
            );
            Assert.Throws<ArgumentException>(() =>
                new ExplorationPartyState(new[] { Member("a", 0, 0), Member("a", 1, 0) }, Id("a"))
            );
            Assert.Throws<ArgumentException>(() =>
                new ExplorationPartyState(new[] { Member("a", 0, 0), Member("b", 0, 0) }, Id("a"))
            );
            Assert.Throws<ArgumentException>(() =>
                new ExplorationPartyState(new[] { Member("a", 0, 0) }, Id("missing"))
            );
            Assert.Throws<ArgumentException>(() => new ExplorationPartyMember(default, Cell(0, 0)));
        }

        /// <summary>Verifies selection and planning boundaries reject missing required values.</summary>
        [Test]
        public void Planner_MissingRequiredInputsAreRejected()
        {
            ExplorationPartyState party = Party("a", Member("a", 0, 0));

            Assert.Throws<ArgumentException>(() => party.SelectLeader(Id("missing")));
            Assert.Throws<ArgumentNullException>(() =>
                new ExplorationStepRequest(null, Cell(1, 0), OpenCells())
            );
            Assert.Throws<ArgumentNullException>(() =>
                new ExplorationStepRequest(party, Cell(1, 0), null)
            );
            Assert.Throws<ArgumentNullException>(() => ExplorationStepPlanner.Plan(null));
        }

        private static ExplorationMemberId Id(string value) => new(value);

        private static DungeonCell Cell(int x, int z) => new(x, z);

        private static ExplorationPartyMember Member(string id, int x, int z) =>
            new(Id(id), Cell(x, z));

        private static ExplorationPartyState Party(
            string leaderId,
            params ExplorationPartyMember[] members
        ) => new(members, Id(leaderId));

        private static TestCellAvailability OpenCells(params DungeonCell[] unavailable) =>
            new(unavailable);

        private static int[] Cells(ExplorationPartyState party) =>
            party.Members.SelectMany(member => new[] { member.Cell.X, member.Cell.Z }).ToArray();

        private static AcceptedExplorationStepPlan Accepted(ExplorationStepOutcome outcome)
        {
            Assert.That(outcome, Is.TypeOf<AcceptedExplorationStepPlan>());
            return (AcceptedExplorationStepPlan)outcome;
        }

        private static RejectedExplorationStepPlan Rejected(ExplorationStepOutcome outcome)
        {
            Assert.That(outcome, Is.TypeOf<RejectedExplorationStepPlan>());
            return (RejectedExplorationStepPlan)outcome;
        }

        private static void AssertEveryMoveIsCardinal(AcceptedExplorationStepPlan plan)
        {
            Assert.That(
                plan.Moves.All(move =>
                    Math.Abs(move.From.X - move.To.X) + Math.Abs(move.From.Z - move.To.Z) == 1
                ),
                Is.True
            );
        }

        private sealed class TestCellAvailability : IExplorationCellAvailability
        {
            private readonly HashSet<DungeonCell> unavailable;

            public TestCellAvailability(IEnumerable<DungeonCell> unavailable)
            {
                this.unavailable = new HashSet<DungeonCell>(
                    unavailable ?? throw new ArgumentNullException(nameof(unavailable))
                );
            }

            public bool CanOccupy(DungeonCell cell) => !unavailable.Contains(cell);
        }
    }
}
