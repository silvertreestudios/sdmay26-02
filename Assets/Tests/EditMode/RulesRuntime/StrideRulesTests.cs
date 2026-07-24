using System;
using System.Linq;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using NUnit.Framework;

namespace Game.Tests.EditMode.RulesRuntime
{
    public sealed class StrideRulesTests
    {
        private static readonly CreatureId Actor = new CreatureId("actor");
        private static readonly CreatureId Other = new CreatureId("other");
        private static readonly CreatureId SecondOther = new CreatureId("second-other");
        private static readonly PlayerId Party = new PlayerId("party");

        [Test]
        public async Task ValidStrideSpendsOneActionAndCommitsThePath()
        {
            TestTopologyProvider topology = new TestTopologyProvider(CreateTopology());
            RuleDispatcher dispatcher = CreateDispatcher(topology, SeedActor());

            OpResult<MovePathOutcome> result = await dispatcher.Dispatch(
                new StrideActionOp(
                    Actor,
                    new MovementPath(
                        new GridPosition(0, 0, 0),
                        new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
                    )
                )
            );

            ResolvedOpResult<MovePathOutcome> resolved = RequireResolved(result);
            Assert.That(resolved.Value.ReachedDestination, Is.True);
            Assert.That(
                dispatcher.Snapshot.Positions[Actor],
                Is.EqualTo(new GridPosition(2, 0, 0))
            );
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
            Assert.That(resolved.Facts.OfType<ActionCostSpentFact>().Count(), Is.EqualTo(1));
            Assert.That(resolved.Facts.OfType<TokenMovedFact>().Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task InvalidPathIsRejectedBeforeActionCost()
        {
            TestTopologyProvider topology = new TestTopologyProvider(
                CreateTopology(new GridCell(new GridPosition(1, 0, 0), true, TerrainCost.Normal))
            );
            RuleDispatcher dispatcher = CreateDispatcher(topology, SeedActor());

            OpResult<MovePathOutcome> result = await dispatcher.Dispatch(
                new StrideActionOp(
                    Actor,
                    new MovementPath(new GridPosition(0, 0, 0), new[] { new GridPosition(1, 0, 0) })
                )
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<MovePathOutcome>>());
            Assert.That(
                dispatcher.Snapshot.Positions[Actor],
                Is.EqualTo(new GridPosition(0, 0, 0))
            );
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
            Assert.That(result.Facts, Is.Empty);
        }

        [Test]
        public async Task FriendlyCreatureCanBeCrossedButEnemyCannot()
        {
            RulesStateSeed friendlySeed = SeedActor()
                .SeedCreature(new CreatureState(Other, Party))
                .SeedPosition(Other, new GridPosition(1, 0, 0));
            RuleDispatcher friendly = CreateDispatcher(
                new TestTopologyProvider(CreateTopology()),
                friendlySeed
            );
            MovementPath path = new MovementPath(
                new GridPosition(0, 0, 0),
                new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
            );

            ResolvedOpResult<MovePathOutcome> friendlyResult = RequireResolved(
                await friendly.Dispatch(new StrideActionOp(Actor, path))
            );
            Assert.That(friendlyResult.Value.ReachedDestination, Is.True);
            Assert.That(
                friendlyResult.Facts.OfType<OccupiedSpaceTraversedFact>().Count(),
                Is.EqualTo(1)
            );

            RulesStateSeed enemySeed = SeedActor()
                .SeedCreature(new CreatureState(Other, new PlayerId("enemy")))
                .SeedPosition(Other, new GridPosition(1, 0, 0));
            RuleDispatcher enemy = CreateDispatcher(
                new TestTopologyProvider(CreateTopology()),
                enemySeed
            );
            OpResult<MovePathOutcome> enemyResult = await enemy.Dispatch(
                new StrideActionOp(Actor, path)
            );
            Assert.That(enemyResult, Is.TypeOf<InvalidOpResult<MovePathOutcome>>());
            Assert.That(enemy.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(3));
        }

        [TestCase(2)]
        [TestCase(3)]
        public async Task OneStrideCrossesEveryFriendlyOccupiedSquare(int secondOccupiedX)
        {
            RulesStateSeed seed = SeedActor(30)
                .SeedCreature(new CreatureState(Other, Party))
                .SeedPosition(Other, new GridPosition(1, 0, 0))
                .SeedCreature(new CreatureState(SecondOther, Party))
                .SeedPosition(SecondOther, new GridPosition(secondOccupiedX, 0, 0));
            RuleDispatcher dispatcher = CreateDispatcher(
                new TestTopologyProvider(CreateTopology()),
                seed
            );
            GridPosition[] steps = Enumerable
                .Range(1, secondOccupiedX + 1)
                .Select(x => new GridPosition(x, 0, 0))
                .ToArray();

            ResolvedOpResult<MovePathOutcome> result = RequireResolved(
                await dispatcher.Dispatch(
                    new StrideActionOp(Actor, new MovementPath(new GridPosition(0, 0, 0), steps))
                )
            );

            Assert.That(result.Value.ReachedDestination, Is.True);
            Assert.That(
                dispatcher.Snapshot.Positions[Actor],
                Is.EqualTo(new GridPosition(secondOccupiedX + 1, 0, 0))
            );
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(2));
            Assert.That(
                result.Facts.OfType<OccupiedSpaceTraversedFact>().Select(fact => fact.Occupant),
                Is.EqualTo(new[] { Other, SecondOther })
            );
            Assert.That(
                result.Facts.OfType<TokenMovedFact>().Count(),
                Is.EqualTo(secondOccupiedX + 1)
            );
        }

        [Test]
        public async Task ReplacementTopologyAppliesOnlyToLaterRoots()
        {
            TestTopologyProvider topology = new TestTopologyProvider(
                CreateTopology(new GridCell(new GridPosition(1, 0, 0), true, TerrainCost.Normal))
            );
            RuleDispatcher dispatcher = CreateDispatcher(topology, SeedActor());
            StrideActionOp stride = new StrideActionOp(
                Actor,
                new MovementPath(new GridPosition(0, 0, 0), new[] { new GridPosition(1, 0, 0) })
            );

            Assert.That(
                await dispatcher.Dispatch(stride),
                Is.TypeOf<InvalidOpResult<MovePathOutcome>>()
            );
            topology.Replace(CreateTopology());
            Assert.That(
                await dispatcher.Dispatch(stride),
                Is.TypeOf<ResolvedOpResult<MovePathOutcome>>()
            );
        }

        [Test]
        public async Task ConsecutiveStridesPreserveAlternatingDiagonalCost()
        {
            TestTopologyProvider topology = new TestTopologyProvider(CreateTopology());
            RuleDispatcher dispatcher = CreateDispatcher(topology, SeedActor(speedFeet: 10));

            ResolvedOpResult<MovePathOutcome> first = RequireResolved(
                await dispatcher.Dispatch(
                    new StrideActionOp(
                        Actor,
                        new MovementPath(
                            new GridPosition(0, 0, 0),
                            new[] { new GridPosition(1, 0, 1) }
                        )
                    )
                )
            );
            ResolvedOpResult<MovePathOutcome> second = RequireResolved(
                await dispatcher.Dispatch(
                    new StrideActionOp(
                        Actor,
                        new MovementPath(
                            new GridPosition(1, 0, 1),
                            new[] { new GridPosition(2, 0, 2) }
                        )
                    )
                )
            );

            Assert.That(first.Value.DistanceSpent, Is.EqualTo(new GridDistance(5)));
            Assert.That(second.Value.DistanceSpent, Is.EqualTo(new GridDistance(10)));
            Assert.That(dispatcher.Snapshot.ActionEconomy[Actor].ActionsRemaining, Is.EqualTo(1));
        }

        private static RulesStateSeed SeedActor(int speedFeet = 25) =>
            new RulesStateSeed()
                .SeedCreature(new CreatureState(Actor, Party))
                .SeedPosition(Actor, new GridPosition(0, 0, 0))
                .SeedLandSpeed(Actor, new GridDistance(speedFeet))
                .SeedActionEconomy(Actor, new ActionEconomyState(3, true));

        private static RuleDispatcher CreateDispatcher(
            IGridTopologyProvider topology,
            RulesStateSeed seed
        )
        {
            StrideActionDefinition definition = new StrideActionDefinition(topology);
            return new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
                .UseActionLifecycle(definition)
                .UseMovementRules(topology)
                .UseStrideRules(definition)
                .Build();
        }

        private static GridTopology CreateTopology(params GridCell[] cells) =>
            new GridTopology(
                new GridBounds(new GridPosition(0, 0, 0), new GridPosition(4, 0, 4)),
                cells
            );

        private static ResolvedOpResult<TResult> RequireResolved<TResult>(OpResult<TResult> result)
        {
            string failure = result is InvalidOpResult<TResult> invalid
                ? invalid.Reason
                : "The operation did not resolve.";
            Assert.That(result, Is.TypeOf<ResolvedOpResult<TResult>>(), failure);
            return (ResolvedOpResult<TResult>)result;
        }

        private sealed class TestTopologyProvider : IGridTopologyProvider
        {
            public TestTopologyProvider(GridTopology topology) =>
                Current = topology ?? throw new ArgumentNullException(nameof(topology));

            public GridTopology Current { get; private set; }

            public void Replace(GridTopology topology) =>
                Current = topology ?? throw new ArgumentNullException(nameof(topology));
        }
    }
}
