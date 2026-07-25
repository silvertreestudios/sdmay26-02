using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Combat.Encounters;
using Game.Creature;
using Game.Rules.Runtime;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

/// <summary>
/// Verifies that the combat manager can scope initiative to active dungeon encounters without
/// changing the legacy all-registered-combat path.
/// </summary>
public sealed class DungeonEncounterCombatPlayModeTests
{
    private readonly List<GameObject> createdObjects = new();
    private CombatManager manager;
    private UnityEngine.Random.State randomState;

    /// <summary>Creates isolated combat manager and log singletons with deterministic randomness.</summary>
    [SetUp]
    public void SetUp()
    {
        DestroyExistingRuntime();
        randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(158);

        Create("TestCombatLog").AddComponent<TestCombatLog>();
        manager = Create("CombatManager").AddComponent<CombatManager>();
    }

    /// <summary>Restores global randomness and destroys every synthetic test object.</summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        UnityEngine.Random.state = randomState;

        for (int index = createdObjects.Count - 1; index >= 0; index--)
        {
            if (createdObjects[index] != null)
                UnityEngine.Object.DestroyImmediate(createdObjects[index]);
        }

        createdObjects.Clear();
        manager = null;
        yield return null;
    }

    /// <summary>Verifies dungeon combat excludes registered controllers outside its explicit roster.</summary>
    [Test]
    public void StartDungeonCombat_UsesOnlyExplicitRegisteredParticipants()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 200);
        CombatantFixture activeEnemy = CreateCombatant("Active Enemy", "Enemies", 100);
        CombatantFixture dormantEnemy = CreateCombatant("Dormant Enemy", "Enemies", 1000);

        manager.StartDungeonCombat(new[] { player.Controller, activeEnemy.Controller });

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(new[] { player.GameObject, activeEnemy.GameObject })
        );
        Assert.That(manager.WhosTurn(), Is.Not.SameAs(dormantEnemy.GameObject));
        Assert.That(dormantEnemy.Controller.StartTurnCount, Is.Zero);
        Assert.That(dormantEnemy.Controller.HasTurnAuthority, Is.False);
    }

    /// <summary>Verifies removing the active controller clears its stale turn-owner reference.</summary>
    [Test]
    public void Remove_CurrentTurnOwnerClearsReferenceWithoutReentrantAdvance()
    {
        CombatantFixture current = CreateCombatant("Current", "Players", 100);
        CombatantFixture next = CreateCombatant("Next", "Enemies", 50);
        CombatantFixture ally = CreateCombatant("Ally", "Players", 0);
        manager.StartDungeonCombat(new[] { current.Controller, next.Controller, ally.Controller });
        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));

        manager.Remove(current.Controller);

        Assert.That(manager.WhosTurn(), Is.Null);
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(new[] { next.GameObject, ally.GameObject })
        );
        Assert.That(next.Controller.StartTurnCount, Is.Zero);

        manager.NextTurn();

        Assert.That(manager.WhosTurn(), Is.SameAs(next.GameObject));
        Assert.That(next.Controller.StartTurnCount, Is.EqualTo(1));
    }

    /// <summary>Verifies malformed encounter identity cannot interrupt committed defeat cleanup.</summary>
    [Test]
    public void LethalDamage_UnconfiguredEncounterMemberStillCompletesDefeatPresentation()
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(singletonField, Is.Not.Null);
        object previousGrid = singletonField.GetValue(null);
        try
        {
            singletonField.SetValue(null, null);
            TestGridAPI grid = Create("Test GridAPI").AddComponent<TestGridAPI>();
            CombatantFixture player = CreateCombatant("Player", "Players", 100);
            CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
            DungeonEncounterMember member = enemy.GameObject.AddComponent<DungeonEncounterMember>();
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });

            Assert.DoesNotThrow(() =>
                enemy.Creature.ApplyFinalDamage(10, RuleSource.FromSlug("test-lethal-damage"))
            );

            Assert.That(member.IsConfigured, Is.False);
            Assert.That(member.DefeatWasReported, Is.False);
            Assert.That(grid.DestroyedTokens, Contains.Item(enemy.GameObject));
            Assert.That(enemy.Controller.enabled, Is.False);
            Assert.That(enemy.GameObject.activeSelf, Is.False);
            Assert.That(manager.GetCombatants(), Has.No.Member(enemy.GameObject));
        }
        finally
        {
            singletonField.SetValue(null, previousGrid);
        }
    }

    /// <summary>Verifies a reinforcement before the current initiative waits for the next round.</summary>
    [Test]
    public void AddDungeonReinforcements_HigherInitiativeWaitsUntilNextRound()
    {
        CombatantFixture current = CreateCombatant("Current", "Players", 100);
        CombatantFixture later = CreateCombatant("Later", "Enemies", 0);
        CombatantFixture reinforcement = CreateCombatant(
            "High Initiative Reinforcement",
            "Enemies",
            200
        );
        manager.StartDungeonCombat(new[] { current.Controller, later.Controller });
        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));
        current.Creature.ApplyFinalDamage(2, RuleSource.FromSlug("test-current-damage"));

        manager.AddDungeonReinforcements(new[] { reinforcement.Controller });

        Assert.That(
            current.Creature.hp,
            Is.EqualTo(8),
            "Rebuilding health ownership for reinforcements must preserve current combatant state."
        );
        reinforcement.Creature.ApplyFinalDamage(
            1,
            RuleSource.FromSlug("test-reinforcement-damage")
        );
        Assert.That(
            reinforcement.Creature.hp,
            Is.EqualTo(9),
            "A reinforcement must join the authoritative encounter health bridge."
        );
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(
                new[] { current.GameObject, later.GameObject, reinforcement.GameObject }
            )
        );
        Assert.That(reinforcement.Controller.StartTurnCount, Is.Zero);
        Assert.That(reinforcement.Controller.HasTurnAuthority, Is.False);

        current.Controller.EndTurn();

        Assert.That(manager.WhosTurn(), Is.SameAs(later.GameObject));
        Assert.That(reinforcement.Controller.StartTurnCount, Is.Zero);

        later.Controller.EndTurn();

        Assert.That(manager.WhosTurn(), Is.SameAs(reinforcement.GameObject));
        Assert.That(reinforcement.Controller.StartTurnCount, Is.EqualTo(1));
    }

    /// <summary>Verifies suspension resets turn economy without changing durable creature state.</summary>
    [Test]
    public void SuspendDungeonCombat_ClearsTurnStateAndPreservesCreatureState()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        Vector3 preservedPosition = new(4f, 0f, 7f);
        ConditionSource preservedSource = new();
        player.GameObject.transform.position = preservedPosition;
        player.Creature.InitializeHealthBeforeEncounter(7, 10);
        player.Conditions.Add("Off-Guard", preservedSource);

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        player.Controller.ActionPoints = 2;
        player.Controller.Reacted = true;
        player.Controller.StrikePenalty = 2;
        player.Controller.IsTakingAction = true;
        enemy.Controller.ActionPoints = 1;
        enemy.Controller.Reacted = true;
        enemy.Controller.StrikePenalty = 1;
        enemy.Controller.IsTakingAction = true;

        manager.SuspendDungeonCombat();

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(manager.WhosTurn(), Is.Null);
        AssertTransientTurnStateCleared(player.Controller);
        AssertTransientTurnStateCleared(enemy.Controller);
        Assert.That(player.Creature.hp, Is.EqualTo(7));
        Assert.That(player.GameObject.transform.position, Is.EqualTo(preservedPosition));
        Assert.That(player.Conditions.Contains("Off-Guard", preservedSource), Is.True);
    }

    /// <summary>Verifies dungeon victory uses its dedicated event instead of legacy completion events.</summary>
    [Test]
    public void DungeonVictory_EmitsOnlyDungeonCompletionEvent()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        int dungeonEndCalls = 0;
        int legacyEndCalls = 0;
        int legacyOutcomeCalls = 0;
        string winner = string.Empty;
        Action<string> dungeonEndListener = winningTeam =>
        {
            dungeonEndCalls++;
            winner = winningTeam;
        };
        UnityAction<string> legacyEndListener = _ => legacyEndCalls++;
        UnityAction<bool> legacyOutcomeListener = _ => legacyOutcomeCalls++;
        manager.DungeonCombatEnded += dungeonEndListener;
        OnCombatEnd.AddListener(legacyEndListener);
        OnCombatOutcome.AddListener(legacyOutcomeListener);

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            enemy.GameObject.SetActive(false);

            Assert.That(manager.CheckForEndOfGame(), Is.True);

            Assert.That(dungeonEndCalls, Is.EqualTo(1));
            Assert.That(winner, Is.EqualTo("Players"));
            Assert.That(legacyEndCalls, Is.Zero);
            Assert.That(legacyOutcomeCalls, Is.Zero);
            Assert.That(manager.IsCombatActive, Is.False);
        }
        finally
        {
            manager.DungeonCombatEnded -= dungeonEndListener;
            OnCombatEnd.RemoveListener(legacyEndListener);
            OnCombatOutcome.RemoveListener(legacyOutcomeListener);
        }
    }

    /// <summary>Verifies dungeon party defeat also reaches the normal loss presentation channel.</summary>
    [Test]
    public void DungeonDefeatEmitsDungeonCompletionAndNormalLossOutcome()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        int dungeonEndCalls = 0;
        int lossCalls = 0;
        string winner = string.Empty;
        Action<string> dungeonEndListener = winningTeam =>
        {
            dungeonEndCalls++;
            winner = winningTeam;
        };
        UnityAction<bool> outcomeListener = playersWon =>
        {
            if (!playersWon)
                lossCalls++;
        };
        manager.DungeonCombatEnded += dungeonEndListener;
        OnCombatOutcome.AddListener(outcomeListener);

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            player.GameObject.SetActive(false);

            Assert.That(manager.CheckForEndOfGame(), Is.True);
            Assert.That(dungeonEndCalls, Is.EqualTo(1));
            Assert.That(winner, Is.EqualTo("Enemies"));
            Assert.That(lossCalls, Is.EqualTo(1));
        }
        finally
        {
            manager.DungeonCombatEnded -= dungeonEndListener;
            OnCombatOutcome.RemoveListener(outcomeListener);
        }
    }

    /// <summary>Verifies exploration permits only repeatable movement without spending actions.</summary>
    [Test]
    public void DungeonExplorationAllowsOnlyRepeatableMovementActions()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        int movementCalls = 0;
        int attackCalls = 0;
        TestEntityAction movement = new("Stride", 1, () => movementCalls++, true);
        TestEntityAction attack = new("Strike", 1, () => attackCalls++);
        player.Controller.AddAction(movement);
        player.Controller.AddAction(attack);
        player.Controller.ActionPoints = 2;
        player.Controller.Reacted = true;
        player.Controller.StrikePenalty = 1;
        player.Controller.SetDungeonExploration(true);

        player.Controller.TakeAction(movement);
        player.Controller.TakeAction(movement);
        player.Controller.TakeAction(attack);

        Assert.That(movementCalls, Is.EqualTo(2));
        Assert.That(attackCalls, Is.Zero);
        Assert.That(player.Controller.ActionPoints, Is.EqualTo(2));
        Assert.That(player.Controller.Reacted, Is.True);
        Assert.That(player.Controller.StrikePenalty, Is.EqualTo(1));
        Assert.That(player.Controller.HasTurnAuthority, Is.False);

        player.Controller.SetDungeonExploration(false);
        player.Controller.TakeAction(movement);
        Assert.That(movementCalls, Is.EqualTo(2));
    }

    /// <summary>Verifies legacy combat still activates every registered controller.</summary>
    [Test]
    public void LegacyStartCombat_ActivatesEveryRegisteredController()
    {
        CombatantFixture first = CreateCombatant("First", "Players", 300);
        CombatantFixture second = CreateCombatant("Second", "Enemies", 200);
        CombatantFixture third = CreateCombatant("Third", "Enemies", 100);

        manager.StartCombat();

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(new[] { first.GameObject, second.GameObject, third.GameObject })
        );
        Assert.That(manager.WhosTurn(), Is.SameAs(first.GameObject));
        Assert.That(first.Controller.StartTurnCount, Is.EqualTo(1));
    }

    private CombatantFixture CreateCombatant(string name, string teamName, int initiative)
    {
        GameObject gameObject = Create(name);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.name = name;
        creature.initiative = initiative;
        creature.InitializeHealthBeforeEncounter(10, 10);
        Conditions conditions = gameObject.AddComponent<Conditions>();
        TestActionController controller = gameObject.AddComponent<TestActionController>();
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        manager.AddCombatant(controller);
        return new CombatantFixture(gameObject, creature, conditions, controller);
    }

    private GameObject Create(string name)
    {
        GameObject gameObject = new(name);
        createdObjects.Add(gameObject);
        return gameObject;
    }

    private static void DestroyExistingRuntime()
    {
        foreach (
            GameManager gameManager in UnityEngine.Object.FindObjectsByType<GameManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            UnityEngine.Object.DestroyImmediate(gameManager.gameObject);
        foreach (
            CombatManagerInterface combatManager in UnityEngine.Object.FindObjectsByType<CombatManagerInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            UnityEngine.Object.DestroyImmediate(combatManager.gameObject);
        foreach (
            CombatLogInterface combatLog in UnityEngine.Object.FindObjectsByType<CombatLogInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            UnityEngine.Object.DestroyImmediate(combatLog.gameObject);
    }

    private static void AssertTransientTurnStateCleared(TestActionController controller)
    {
        Assert.That(controller.ActionPoints, Is.Zero);
        Assert.That(controller.Reacted, Is.False);
        Assert.That(controller.StrikePenalty, Is.Zero);
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(controller.HasTurnAuthority, Is.False);
    }

    private sealed class CombatantFixture
    {
        public CombatantFixture(
            GameObject gameObject,
            CreatureComponent creature,
            Conditions conditions,
            TestActionController controller
        )
        {
            GameObject = gameObject;
            Creature = creature;
            Conditions = conditions;
            Controller = controller;
        }

        public GameObject GameObject { get; }
        public CreatureComponent Creature { get; }
        public Conditions Conditions { get; }
        public TestActionController Controller { get; }
    }

    private sealed class TestActionController : ActionController
    {
        public int StartTurnCount { get; private set; }

        public override void StartTurn()
        {
            StartTurnCount++;
            base.StartTurn();
        }

        public override void EndTurn()
        {
            if (!IsTurn)
                return;

            IsTurn = false;
            IsTakingAction = false;
            CombatManagerInterface.GetInstance().NextTurn();
        }
    }

    private sealed class TestEntityAction : EntityAction
    {
        private readonly Action invocation;
        private readonly bool isExplorationAction;

        public TestEntityAction(
            string name,
            uint cost,
            Action invocation,
            bool isExplorationAction = false
        )
            : base(cost)
        {
            ActionName = name;
            this.invocation = invocation;
            this.isExplorationAction = isExplorationAction;
        }

        public override string ActionName { get; }

        public override bool IsExplorationAction => isExplorationAction;

        /// <inheritdoc/>
        public override void Invoke(GameObject target)
        {
            ActionController controller = target.GetComponent<ActionController>();
            invocation();
            PayCost(controller);
            controller.IsTakingAction = false;
        }
    }

    private sealed class TestCombatLog : CombatLogInterface
    {
        private readonly List<string> messages = new();

        public override void DevMode() { }

        public override void ReleaseMode() { }

        public override void AddWhiteList(string tag) { }

        public override void AddBlackList(string tag) { }

        public override void Log(string msg) => messages.Add(msg);

        public override void DevLog(string msg) => messages.Add(msg);

        public override void DevLog(string msg, string tag) => messages.Add(msg);

        public override void DevLog(string msg, List<string> tags) => messages.Add(msg);

        public override void Log(string msg, string tag) => messages.Add(msg);

        public override void Log(string msg, List<string> tags) => messages.Add(msg);

        public override List<string> GetMessages() => new(messages);
    }

    private sealed class TestGridAPI : GridAPI
    {
        public List<GameObject> DestroyedTokens { get; } = new();

        public override IEnumerator SelectStridePath(
            GameObject character,
            StridePathSelectionRequest request,
            CoroutineResult<SelectionOutcome<MovementPath>> selection
        )
        {
            selection.Value = SelectionOutcome<MovementPath>.Cancelled;
            yield break;
        }

        public override IEnumerator GetStrikeTarget(
            GameObject attacker,
            StrikeTargetRequest request,
            CoroutineResult<StrikeTargetResult> target
        )
        {
            yield break;
        }

        public override IEnumerator GetAreaTarget(
            AreaTargetSource source,
            AreaTargetRequest request,
            CoroutineResult<AreaTargetResult> target
        )
        {
            yield break;
        }

        public override bool DestroyToken(GameObject token)
        {
            DestroyedTokens.Add(token);
            return true;
        }
    }
}
