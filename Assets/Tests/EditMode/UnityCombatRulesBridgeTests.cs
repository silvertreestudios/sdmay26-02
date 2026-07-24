using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class UnityCombatRulesBridgeTests
{
    [Test]
    public void BridgeRejectsEmptyEncounterWithSpecificError()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            UnityCombatRulesBridge.CreateHealthTestComposition(Array.Empty<CreatureComponent>())
        );

        StringAssert.Contains("requires at least one creature", error.Message);
    }

    [Test]
    public void BridgeRejectsNullEncounterCreatureWithSpecificError()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            UnityCombatRulesBridge.CreateHealthTestComposition(new CreatureComponent[] { null })
        );

        StringAssert.Contains("cannot contain a null creature", error.Message);
    }

    [Test]
    public void CreatureHealthCommandsRejectMissingEncounterBridge()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                creature.ApplyFinalDamage(1, RuleSource.FromSlug("test-damage"))
            );

            StringAssert.Contains("require an encounter health bridge", error.Message);
            Assert.That(creature.Health, Is.EqualTo(new HealthState(10, 10)));
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void BridgeOwnsHealthAndProjectsCommittedFactsBackToComponents()
    {
        GameObject firstObject = new GameObject("first");
        GameObject secondObject = new GameObject("second");
        try
        {
            CreatureComponent first = firstObject.AddComponent<CreatureComponent>();
            CreatureComponent second = secondObject.AddComponent<CreatureComponent>();
            first.InitializeHealthBeforeEncounter(10, 12);
            second.InitializeHealthBeforeEncounter(7, 7);
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateHealthTestComposition(
                new[] { first, second }
            );

            CreatureId firstId = bridge.GetCreatureId(first);
            CreatureId secondId = bridge.GetCreatureId(second);
            DamageOutcome damage = bridge.ApplyFinalDamage(
                firstId,
                4,
                RuleSource.FromSlug("test-strike")
            );
            HealingOutcome healing = bridge.ApplyHealing(
                firstId,
                2,
                RuleSource.FromSlug("test-heal")
            );

            Assert.That(firstId.Value, Is.EqualTo("combat-creature-1"));
            Assert.That(secondId.Value, Is.EqualTo("combat-creature-2"));
            Assert.That(damage.Applied, Is.EqualTo(4));
            Assert.That(healing.Applied, Is.EqualTo(2));
            Assert.That(first.hp, Is.EqualTo(8));
            Assert.That(first.maxHp, Is.EqualTo(12));
            Assert.That(bridge.Snapshot.Health[firstId].Current, Is.EqualTo(first.hp));
            Assert.That(
                bridge.TryGetOriginSource(
                    new HealthChangeOriginId("health-origin-1"),
                    out RuleSource originSource
                ),
                Is.True
            );
            Assert.That(originSource, Is.EqualTo(RuleSource.FromSlug("test-strike")));
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(secondObject);
        }
    }

    [Test]
    public void BridgeProjectsSourceTemporaryHitPointStateAndImmunity()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateHealthTestComposition(
                new[] { creature }
            );
            CreatureId id = bridge.GetCreatureId(creature);
            RuleSource rage = RuleSource.FromSlug("rage");

            creature.GrantSourceTemporaryHitPoints(rage, 4);
            creature.ApplyFinalDamage(1, RuleSource.FromSlug("test-damage"));
            creature.RemoveSourceTemporaryHitPoints(rage);
            creature.AddTemporaryHitPointImmunity(rage);
            TemporaryHitPointsGrantOutcome blocked = creature.GrantSourceTemporaryHitPoints(
                rage,
                5
            );

            Assert.That(blocked.Immune, Is.True);
            Assert.That(creature.tempHp, Is.Zero);
            Assert.That(bridge.Snapshot.Health[id].Temporary, Is.Zero);
            Assert.That(creature.HasTempHpImmunity("rage"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void BridgePropagatesCompletedDispatcherFailure()
    {
        GameObject creatureObject = new GameObject("creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateHealthTestComposition(
                new[] { creature }
            );
            InvalidOperationException expected = new InvalidOperationException(
                "completed observer failure"
            );
            GetDispatcher(bridge)
                .RegisterFactObserver<HealthFact>(new CompletedFailureObserver(expected));

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyFinalDamage(
                    bridge.GetCreatureId(creature),
                    1,
                    RuleSource.FromSlug("test-damage")
                )
            );

            Assert.That(actual, Is.SameAs(expected));
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public void BridgeRejectsIncompleteDispatcherWork()
    {
        GameObject creatureObject = new GameObject("creature");
        IncompleteObserver observer = new IncompleteObserver();
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateHealthTestComposition(
                new[] { creature }
            );
            GetDispatcher(bridge).RegisterFactObserver<HealthFact>(observer);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyFinalDamage(
                    bridge.GetCreatureId(creature),
                    1,
                    RuleSource.FromSlug("test-damage")
                )
            );

            StringAssert.Contains("cannot contain asynchronous callbacks", error.Message);
        }
        finally
        {
            observer.Complete();
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public async Task CombatCompositionDispatchesDormantStrideThroughSharedState()
    {
        GameObject creatureObject = new GameObject("stride-creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            creature.speed = 25;
            BridgeTestActionController controller =
                creatureObject.AddComponent<BridgeTestActionController>();
            GridPrivate.Tile[,] tiles = new GridPrivate.Tile[3, 1];
            for (int x = 0; x < tiles.GetLength(0); x++)
                tiles[x, 0] = new GridPrivate.Tile();
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new[] { controller },
                tiles
            );
            CreatureId id = bridge.GetCreatureId(creature);
            bridge.BeginTurn(id, 3);

            OpResult<MovePathOutcome> result = await bridge.DispatchStride(
                id,
                new MovementPath(
                    new GridPosition(0, 0, 0),
                    new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
                )
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<MovePathOutcome>>());
            Assert.That(bridge.Snapshot.Positions[id], Is.EqualTo(new GridPosition(2, 0, 0)));
            Assert.That(bridge.Snapshot.ActionEconomy[id].ActionsRemaining, Is.EqualTo(2));
            Assert.That(controller.ActionPoints, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [Test]
    public async Task ExplorationStrideProjectsRulesFactsWithoutClaimingCombatActionPoints()
    {
        GameObject creatureObject = new GameObject("exploration-stride-creature");
        try
        {
            CreatureComponent creature = creatureObject.AddComponent<CreatureComponent>();
            creature.InitializeHealthBeforeEncounter(10, 10);
            creature.speed = 25;
            BridgeTestActionController controller =
                creatureObject.AddComponent<BridgeTestActionController>();
            controller.ActionPoints = 7;
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateExplorationStride(
                controller,
                CreateTiles(3)
            );
            CreatureId id = bridge.GetCreatureId(controller);
            RecordingMovementObserver observer = new RecordingMovementObserver();

            bool resolved = await bridge.DispatchProjectedStride(
                id,
                new MovementPath(
                    new GridPosition(0, 0, 0),
                    new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
                ),
                observer
            );

            Assert.That(resolved, Is.True);
            Assert.That(observer.Facts, Has.Count.EqualTo(2));
            Assert.That(observer.Facts[0].From, Is.EqualTo(new GridPosition(0, 0, 0)));
            Assert.That(observer.Facts[0].To, Is.EqualTo(new GridPosition(1, 0, 0)));
            Assert.That(observer.Facts[1].From, Is.EqualTo(new GridPosition(1, 0, 0)));
            Assert.That(observer.Facts[1].To, Is.EqualTo(new GridPosition(2, 0, 0)));
            Assert.That(bridge.Snapshot.Positions[id], Is.EqualTo(new GridPosition(2, 0, 0)));
            Assert.That(bridge.Snapshot.ActionEconomy[id].ActionsRemaining, Is.Zero);
            Assert.That(
                controller.ActionPoints,
                Is.EqualTo(7),
                "Exploration Stride must not overwrite the controller's combat AP projection."
            );
        }
        finally
        {
            Object.DestroyImmediate(creatureObject);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CombatStridePreservesDirectionalFriendshipAcrossRegistrationOrder(
        bool moverRegisteredFirst
    )
    {
        GameObject teamRulesObject = new GameObject("team-rules");
        GameObject moverObject = new GameObject("directional-mover");
        GameObject occupantObject = new GameObject("directional-occupant");
        try
        {
            TeamRules teamRules = InitializeTeamRules(teamRulesObject);
            teamRules.AddHostileTeam("mover-team");
            teamRules.AddHostileTeam("occupant-team");
            teamRules.OneWayFriendly("mover-team", "occupant-team");

            BridgeTestActionController mover = ConfigureCombatant(
                moverObject,
                "mover-team",
                new Vector3(0, 0, 0)
            );
            BridgeTestActionController occupant = ConfigureCombatant(
                occupantObject,
                "occupant-team",
                new Vector3(1, 0, 0)
            );
            ActionController[] registrations = moverRegisteredFirst
                ? new ActionController[] { mover, occupant }
                : new ActionController[] { occupant, mover };
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                registrations,
                CreateTiles(4)
            );
            CreatureId moverId = bridge.GetCreatureId(
                moverObject.GetComponent<CreatureComponent>()
            );
            CreatureId occupantId = bridge.GetCreatureId(
                occupantObject.GetComponent<CreatureComponent>()
            );
            PlayerId moverPlayer = bridge.Snapshot.Creatures[moverId].Player;
            PlayerId occupantPlayer = bridge.Snapshot.Creatures[occupantId].Player;

            Assert.That(TeamRules.TryGetInstance(out TeamRules activeRules), Is.True);
            Assert.That(activeRules, Is.SameAs(teamRules));
            Assert.That(teamRules.IsFriendly("mover-team", "occupant-team"), Is.True);
            Assert.That(moverPlayer, Is.Not.EqualTo(occupantPlayer));

            bridge.BeginTurn(moverId, 3);
            OpResult<MovePathOutcome> forward = await bridge.DispatchStride(
                moverId,
                new MovementPath(
                    new GridPosition(0, 0, 0),
                    new[] { new GridPosition(1, 0, 0), new GridPosition(2, 0, 0) }
                )
            );

            string forwardFailure = forward is InvalidOpResult<MovePathOutcome> invalidForward
                ? invalidForward.Reason
                : string.Empty;
            Assert.That(forward, Is.TypeOf<ResolvedOpResult<MovePathOutcome>>(), forwardFailure);
            Assert.That(bridge.Snapshot.Positions[moverId], Is.EqualTo(new GridPosition(2, 0, 0)));
            Assert.That(bridge.Snapshot.ActionEconomy[moverId].ActionsRemaining, Is.EqualTo(2));

            bridge.BeginTurn(occupantId, 3);
            OpResult<MovePathOutcome> reverse = await bridge.DispatchStride(
                occupantId,
                new MovementPath(
                    new GridPosition(1, 0, 0),
                    new[] { new GridPosition(2, 0, 0), new GridPosition(3, 0, 0) }
                )
            );

            Assert.That(reverse, Is.TypeOf<InvalidOpResult<MovePathOutcome>>());
            Assert.That(
                bridge.Snapshot.Positions[occupantId],
                Is.EqualTo(new GridPosition(1, 0, 0))
            );
            Assert.That(bridge.Snapshot.ActionEconomy[occupantId].ActionsRemaining, Is.EqualTo(3));
            Assert.That(occupant.ActionPoints, Is.EqualTo(3));
        }
        finally
        {
            Object.DestroyImmediate(moverObject);
            Object.DestroyImmediate(occupantObject);
            Object.DestroyImmediate(teamRulesObject);
        }
    }

    [Test]
    public async Task CombatStrideDoesNotTreatDirectionalFriendshipAsTransitive()
    {
        GameObject teamRulesObject = new GameObject("team-rules");
        GameObject moverObject = new GameObject("non-transitive-mover");
        GameObject middleObject = new GameObject("non-transitive-middle");
        GameObject lastObject = new GameObject("non-transitive-last");
        try
        {
            TeamRules teamRules = InitializeTeamRules(teamRulesObject);
            teamRules.AddHostileTeam("mover-team");
            teamRules.AddHostileTeam("middle-team");
            teamRules.AddHostileTeam("last-team");
            teamRules.OneWayFriendly("mover-team", "middle-team");
            teamRules.OneWayFriendly("middle-team", "last-team");

            BridgeTestActionController mover = ConfigureCombatant(
                moverObject,
                "mover-team",
                new Vector3(0, 0, 0)
            );
            BridgeTestActionController middle = ConfigureCombatant(
                middleObject,
                "middle-team",
                new Vector3(1, 0, 0)
            );
            BridgeTestActionController last = ConfigureCombatant(
                lastObject,
                "last-team",
                new Vector3(2, 0, 0)
            );
            UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
                new ActionController[] { last, middle, mover },
                CreateTiles(4)
            );
            CreatureId moverId = bridge.GetCreatureId(
                moverObject.GetComponent<CreatureComponent>()
            );
            bridge.BeginTurn(moverId, 3);

            OpResult<MovePathOutcome> result = await bridge.DispatchStride(
                moverId,
                new MovementPath(
                    new GridPosition(0, 0, 0),
                    new[]
                    {
                        new GridPosition(1, 0, 0),
                        new GridPosition(2, 0, 0),
                        new GridPosition(3, 0, 0),
                    }
                )
            );

            Assert.That(result, Is.TypeOf<InvalidOpResult<MovePathOutcome>>());
            Assert.That(bridge.Snapshot.Positions[moverId], Is.EqualTo(new GridPosition(0, 0, 0)));
            Assert.That(bridge.Snapshot.ActionEconomy[moverId].ActionsRemaining, Is.EqualTo(3));
            Assert.That(mover.ActionPoints, Is.EqualTo(3));
            Assert.That(result.Facts, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(moverObject);
            Object.DestroyImmediate(middleObject);
            Object.DestroyImmediate(lastObject);
            Object.DestroyImmediate(teamRulesObject);
        }
    }

    private static BridgeTestActionController ConfigureCombatant(
        GameObject combatant,
        string teamName,
        Vector3 position
    )
    {
        combatant.transform.position = position;
        CreatureComponent creature = combatant.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(10, 10);
        creature.speed = 25;
        Team team = combatant.AddComponent<Team>();
        team.Name = teamName;
        return combatant.AddComponent<BridgeTestActionController>();
    }

    private static GridPrivate.Tile[,] CreateTiles(int width)
    {
        GridPrivate.Tile[,] tiles = new GridPrivate.Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new GridPrivate.Tile();
        return tiles;
    }

    private static TeamRules InitializeTeamRules(GameObject owner)
    {
        TeamRules rules = owner.AddComponent<TeamRules>();
        MethodInfo awake = typeof(TeamRules).BaseType.GetMethod(
            "Awake",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(awake, Is.Not.Null);
        awake.Invoke(rules, null);
        return rules;
    }

    private static RuleDispatcher GetDispatcher(UnityCombatRulesBridge bridge)
    {
        FieldInfo field = typeof(UnityCombatRulesBridge).GetField(
            "dispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (RuleDispatcher)field.GetValue(bridge);
    }

    private sealed class CompletedFailureObserver : IFactObserver<HealthFact>
    {
        private readonly Exception failure;

        public CompletedFailureObserver(Exception failure) => this.failure = failure;

        public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot) =>
            new ValueTask(Task.FromException(failure));
    }

    private sealed class IncompleteObserver : IFactObserver<HealthFact>
    {
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot) =>
            new ValueTask(completion.Task);

        public void Complete() => completion.TrySetResult(true);
    }

    private sealed class RecordingMovementObserver : IFactObserver<TokenMovedFact>
    {
        public List<TokenMovedFact> Facts { get; } = new List<TokenMovedFact>();

        public ValueTask OnFactCommitted(TokenMovedFact fact, RulesSnapshot currentSnapshot)
        {
            Facts.Add(fact);
            return default;
        }
    }

    private sealed class BridgeTestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
