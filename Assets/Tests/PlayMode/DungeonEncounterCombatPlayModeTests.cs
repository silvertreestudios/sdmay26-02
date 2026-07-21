using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Game.Combat.Encounters;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
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
        TeamRules teamRules = Create("TeamRules").AddComponent<TeamRules>();
        teamRules.AddFriendlyTeam("Players");
        teamRules.AddHostileTeam("Enemies");
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
    [UnityTest]
    public IEnumerator StartDungeonCombat_UsesOnlyExplicitRegisteredParticipants()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 200);
        CombatantFixture activeEnemy = CreateCombatant("Active Enemy", "Enemies", 100);
        CombatantFixture dormantEnemy = CreateCombatant("Dormant Enemy", "Enemies", 1000);

        manager.StartDungeonCombat(new[] { player.Controller, activeEnemy.Controller });
        yield return WaitForTurn();

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(new[] { player.GameObject, activeEnemy.GameObject })
        );
        Assert.That(manager.WhosTurn(), Is.Not.SameAs(dormantEnemy.GameObject));
        Assert.That(dormantEnemy.Controller.StartTurnCount, Is.Zero);
        Assert.That(dormantEnemy.Controller.HasTurnAuthority, Is.False);
    }

    /// <summary>Verifies an active encounter roster cannot be mutated outside its reducer.</summary>
    [UnityTest]
    public IEnumerator Remove_CurrentTurnOwnerIsIgnoredUntilEncounterEnds()
    {
        CombatantFixture current = CreateCombatant("Current", "Players", 100);
        CombatantFixture next = CreateCombatant("Next", "Enemies", 50);
        CombatantFixture ally = CreateCombatant("Ally", "Players", 0);
        manager.StartDungeonCombat(new[] { current.Controller, next.Controller, ally.Controller });
        yield return WaitForTurn(current.GameObject);
        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));

        manager.Remove(current.Controller);

        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(new[] { current.GameObject, next.GameObject, ally.GameObject })
        );
        Assert.That(next.Controller.StartTurnCount, Is.Zero);

        current.Controller.EndTurn();
        yield return WaitForTurn(next.GameObject);

        Assert.That(manager.WhosTurn(), Is.SameAs(next.GameObject));
        Assert.That(next.Controller.StartTurnCount, Is.EqualTo(1));
    }

    /// <summary>Verifies presentation-disabled actors close their exact committed turn once.</summary>
    [UnityTest]
    public IEnumerator TurnPresentationDisablesActor_ClosesExactTurnAndAdvancesOnce()
    {
        CombatantFixture first = CreateCombatant("Disabled First", "Players", 200);
        CombatantFixture next = CreateCombatant("Eligible Next", "Enemies", 100);
        CombatantFixture ally = CreateCombatant("Living Ally", "Players", 0);
        TurnIdentity? disabledTurn = null;
        UnityAction<GameObject> disableFirst = actor =>
        {
            if (actor != first.GameObject)
                return;
            disabledTurn = GetEncounterBridge().CurrentTurn;
            first.Controller.enabled = false;
        };
        OnNextTurn.AddListener(disableFirst);
        try
        {
            manager.StartDungeonCombat(
                new[] { first.Controller, next.Controller, ally.Controller }
            );
            RuleDispatcher dispatcher = GetEncounterDispatcher();
            RecordingFactObserver<TurnEndedFact> ended = new();
            dispatcher.RegisterFactObserver<TurnEndedFact>(ended);

            yield return WaitForCondition(
                () => manager.WhosTurn() == next.GameObject && next.Controller.StartTurnCount == 1,
                "A presentation-disabled actor left its committed turn active."
            );

            Assert.That(disabledTurn.HasValue, Is.True);
            Assert.That(first.Controller.StartTurnCount, Is.Zero);
            Assert.That(first.Controller.enabled, Is.False);
            Assert.That(next.Controller.HasTurnAuthority, Is.True);
            Assert.That(ended.Facts, Has.Count.EqualTo(1));
            Assert.That(ended.Facts[0].Turn, Is.EqualTo(disabledTurn.Value));
        }
        finally
        {
            OnNextTurn.RemoveListener(disableFirst);
        }
    }

    /// <summary>Verifies malformed encounter identity cannot interrupt committed defeat cleanup.</summary>
    [UnityTest]
    public IEnumerator LethalDamage_UnconfiguredEncounterMemberStillCompletesDefeatPresentation()
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
            yield return WaitForTurn();

            yield return CoroutineRunner.Await(
                enemy.Creature.ApplyFinalDamageAsync(10, RuleSource.FromSlug("test-lethal-damage"))
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
    [UnityTest]
    public IEnumerator AddDungeonReinforcements_HigherInitiativeWaitsUntilNextRound()
    {
        CombatantFixture current = CreateCombatant("Current", "Players", 100);
        CombatantFixture later = CreateCombatant("Later", "Enemies", 0);
        CombatantFixture reinforcement = CreateCombatant(
            "High Initiative Reinforcement",
            "Enemies",
            200
        );
        manager.StartDungeonCombat(new[] { current.Controller, later.Controller });
        yield return WaitForTurn(current.GameObject);
        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));
        yield return CoroutineRunner.Await(
            current.Creature.ApplyFinalDamageAsync(2, RuleSource.FromSlug("test-current-damage"))
        );

        manager.AddDungeonReinforcements(new[] { reinforcement.Controller });
        yield return WaitForCondition(
            () => manager.GetCombatants().Count == 3,
            "Timed out waiting for the reinforcement roster commit."
        );

        Assert.That(
            current.Creature.hp,
            Is.EqualTo(8),
            "Rebuilding health ownership for reinforcements must preserve current combatant state."
        );
        yield return CoroutineRunner.Await(
            reinforcement.Creature.ApplyFinalDamageAsync(
                1,
                RuleSource.FromSlug("test-reinforcement-damage")
            )
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
        yield return WaitForTurn(later.GameObject);

        Assert.That(manager.WhosTurn(), Is.SameAs(later.GameObject));
        Assert.That(reinforcement.Controller.StartTurnCount, Is.Zero);

        later.Controller.EndTurn();
        yield return WaitForTurn(reinforcement.GameObject);

        Assert.That(manager.WhosTurn(), Is.SameAs(reinforcement.GameObject));
        Assert.That(reinforcement.Controller.StartTurnCount, Is.EqualTo(1));
    }

    /// <summary>Verifies reinforcement numbering follows the reducer-sorted accepted roster.</summary>
    [UnityTest]
    public IEnumerator AddDungeonReinforcements_LogsAcceptedInitiativeOrder()
    {
        CombatantFixture current = CreateCombatant("Current", "Players", 100);
        CombatantFixture later = CreateCombatant("Later", "Enemies", 0);
        CombatantFixture low = CreateCombatant("Low Reinforcement", "Enemies", -1000);
        CombatantFixture high = CreateCombatant("High Reinforcement", "Enemies", 1000);
        manager.StartDungeonCombat(new[] { current.Controller, later.Controller });
        yield return WaitForTurn(current.GameObject);
        TestCombatLog combatLog = UnityEngine.Object.FindFirstObjectByType<TestCombatLog>();
        Assert.That(combatLog, Is.Not.Null);

        manager.AddDungeonReinforcements(new[] { low.Controller, high.Controller });
        yield return WaitForCondition(
            () =>
                combatLog
                    .GetMessages()
                    .Any(message =>
                        message.StartsWith("Reinforcements:\n", StringComparison.Ordinal)
                    ),
            "Timed out waiting for the accepted reinforcement initiative log."
        );

        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        HashSet<ActionController> additions = new() { low.Controller, high.Controller };
        string[] committedOrder = bridge
            .Snapshot.Encounters[bridge.EncounterId]
            .Roster.Select(entry => bridge.GetController(entry.Creature))
            .Where(additions.Contains)
            .Select(controller => controller.gameObject.name)
            .ToArray();
        string reinforcementLog = combatLog
            .GetMessages()
            .Single(message => message.StartsWith("Reinforcements:\n", StringComparison.Ordinal));
        string[] lines = reinforcementLog.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );

        Assert.That(
            committedOrder,
            Is.EqualTo(new[] { high.GameObject.name, low.GameObject.name })
        );
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[1], Does.StartWith($"  1. {committedOrder[0]} "));
        Assert.That(lines[2], Does.StartWith($"  2. {committedOrder[1]} "));
    }

    /// <summary>Verifies a lethal final-opponent weapon Strike commits before its effect ends combat.</summary>
    [UnityTest]
    public IEnumerator LethalWeaponStrikeCommitsCostAndAlwaysCompletesActionLifecycle()
    {
        CombatantFixture player = CreateCombatant("Weapon Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Weapon Enemy", "Enemies", 0);
        player.Creature.attackBonus = 100;
        enemy.Creature.ac = -100;
        enemy.Creature.InitializeHealthBeforeEncounter(1, 1);
        EquipmentWeapon weapon = new()
        {
            name = "Lifecycle Test Sword",
            damage = new Dice(1, 1, "slashing"),
            traits = new List<string>(),
        };
        StrikeWeapon strike = new(1, weapon, player.GameObject);

        yield return AssertLethalActionLifecycle(
            player,
            enemy,
            strike,
            expectedActionCost: 1,
            configureGridTarget: true
        );
    }

    /// <summary>Verifies a lethal final-opponent unarmed Strike commits before damage.</summary>
    [UnityTest]
    public IEnumerator LethalUnarmedStrikeCommitsCostAndAlwaysCompletesActionLifecycle()
    {
        CombatantFixture player = CreateCombatant("Unarmed Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Unarmed Enemy", "Enemies", 0);
        player.Creature.attackBonus = 100;
        enemy.Creature.ac = -100;
        enemy.Creature.InitializeHealthBeforeEncounter(1, 1);
        Unarmed strike = new(
            1,
            new List<Dice> { new Dice(1, 1, "bludgeoning") },
            new List<DamageValue>()
        );

        yield return AssertLethalActionLifecycle(
            player,
            enemy,
            strike,
            expectedActionCost: 1,
            configureGridTarget: true
        );
    }

    /// <summary>Verifies a lethal attack spell commits actions and MAP before damage.</summary>
    [UnityTest]
    public IEnumerator LethalSpellCommitsCostAndAlwaysCompletesActionLifecycle()
    {
        CombatantFixture player = CreateCombatant("Spell Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Spell Enemy", "Enemies", 0);
        player.Creature.level = 20;
        player.Creature.wisMod = 20;
        player.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        player.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            player.Creature,
            player.Creature.Build
        );
        enemy.Creature.ac = -100;
        enemy.Creature.InitializeHealthBeforeEncounter(1, 1);
        enemy.GameObject.transform.position = new Vector3(1, 0, 0);
        PreparedSpell spell = player.Creature.Prepared.Spellcasting.GetSpell("divine-lance");
        Assert.That(spell, Is.Not.Null);

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        RecordingFactObserver<LegacyActionsSpentFact> actions = new();
        RecordingFactObserver<LegacyMapIncrementedFact> map = new();
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(actions);
        dispatcher.RegisterFactObserver<LegacyMapIncrementedFact>(map);
        int completionCalls = 0;
        manager.DungeonCombatEnded += _ => completionCalls++;
        player.Controller.IsTakingAction = true;
        CoroutineResult<CastSpellResult> cast = new();

        yield return CoroutineRunner.Await(
            SpellcastingRuntime.CastAsync(player.GameObject, spell, 2, new[] { enemy.GameObject }),
            cast
        );
        yield return WaitForCondition(
            () => !manager.IsCombatActive && !player.Controller.IsTakingAction,
            "Lethal spell completion left combat or action presentation deferred."
        );

        Assert.That(cast.Value.Success, Is.True);
        Assert.That(actions.Facts, Has.Count.EqualTo(1));
        Assert.That(actions.Facts[0].Amount, Is.EqualTo(2));
        Assert.That(map.Facts, Has.Count.EqualTo(1));
        Assert.That(map.Facts[0].AttackCount, Is.EqualTo(1));
        Assert.That(completionCalls, Is.EqualTo(1));
        Assert.That(player.Controller.IsTakingAction, Is.False);
    }

    /// <summary>Verifies suspension resets turn economy without changing durable creature state.</summary>
    [UnityTest]
    public IEnumerator SuspendDungeonCombat_ClearsTurnStateAndPreservesCreatureState()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        Vector3 preservedPosition = new(4f, 0f, 7f);
        ConditionSource preservedSource = new();
        player.GameObject.transform.position = preservedPosition;
        player.Creature.InitializeHealthBeforeEncounter(7, 10);
        player.Conditions.Add("Off-Guard", preservedSource);

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn();
        player.Controller.IsTakingAction = true;
        enemy.Controller.IsTakingAction = true;

        manager.SuspendDungeonCombat();
        yield return WaitForCondition(
            () => !manager.IsCombatActive,
            "Timed out waiting for encounter suspension."
        );

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(manager.WhosTurn(), Is.Null);
        AssertTransientTurnStateCleared(player.Controller);
        AssertTransientTurnStateCleared(enemy.Controller);
        Assert.That(player.Creature.hp, Is.EqualTo(7));
        Assert.That(player.GameObject.transform.position, Is.EqualTo(preservedPosition));
        Assert.That(player.Conditions.Contains("Off-Guard", preservedSource), Is.True);
    }

    /// <summary>Verifies dungeon victory uses its dedicated event instead of legacy completion events.</summary>
    [UnityTest]
    public IEnumerator DungeonVictory_EmitsOnlyDungeonCompletionEvent()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        int dungeonEndCalls = 0;
        int legacyEndCalls = 0;
        int legacyOutcomeCalls = 0;
        EncounterOutcome? winner = null;
        Action<EncounterOutcome> dungeonEndListener = outcome =>
        {
            dungeonEndCalls++;
            winner = outcome;
        };
        UnityAction<string> legacyEndListener = _ => legacyEndCalls++;
        UnityAction<bool> legacyOutcomeListener = _ => legacyOutcomeCalls++;
        manager.DungeonCombatEnded += dungeonEndListener;
        OnCombatEnd.AddListener(legacyEndListener);
        OnCombatOutcome.AddListener(legacyOutcomeListener);

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForTurn();
            yield return CoroutineRunner.Await(
                enemy.Creature.ApplyFinalDamageAsync(10, RuleSource.FromSlug("test-victory"))
            );

            Assert.That(manager.CheckForEndOfGame(), Is.True);

            Assert.That(dungeonEndCalls, Is.EqualTo(1));
            Assert.That(winner, Is.EqualTo(EncounterOutcome.PlayerVictory));
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
    [UnityTest]
    public IEnumerator DungeonDefeatEmitsDungeonCompletionAndNormalLossOutcome()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        int dungeonEndCalls = 0;
        int lossCalls = 0;
        EncounterOutcome? winner = null;
        Action<EncounterOutcome> dungeonEndListener = outcome =>
        {
            dungeonEndCalls++;
            winner = outcome;
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
            yield return WaitForTurn();
            yield return CoroutineRunner.Await(
                player.Creature.ApplyFinalDamageAsync(10, RuleSource.FromSlug("test-defeat"))
            );

            Assert.That(manager.CheckForEndOfGame(), Is.True);
            Assert.That(dungeonEndCalls, Is.EqualTo(1));
            Assert.That(winner, Is.EqualTo(EncounterOutcome.PlayerDefeat));
            Assert.That(lossCalls, Is.EqualTo(1));
        }
        finally
        {
            manager.DungeonCombatEnded -= dungeonEndListener;
            OnCombatOutcome.RemoveListener(outcomeListener);
        }
    }

    /// <summary>Verifies exploration permits only repeatable movement without spending actions.</summary>
    [UnityTest]
    public IEnumerator DungeonExplorationAllowsOnlyRepeatableMovementActions()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        int movementCalls = 0;
        int attackCalls = 0;
        TestEntityAction movement = new("Stride", 1, () => movementCalls++);
        TestEntityAction attack = new("Strike", 1, () => attackCalls++);
        player.Controller.AddTestMovement(movement);
        player.Controller.AddAction(attack);
        player.Controller.ActionPoints = 2;
        player.Controller.Reacted = true;
        player.Controller.StrikePenalty = 1;
        player.Controller.SetDungeonExploration(true);

        player.Controller.TakeAction(movement);
        yield return WaitForCondition(
            () => !player.Controller.IsTakingAction,
            "Timed out waiting for the first exploration action."
        );
        player.Controller.TakeAction(movement);
        yield return WaitForCondition(
            () => !player.Controller.IsTakingAction,
            "Timed out waiting for the repeated exploration action."
        );
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
    [UnityTest]
    public IEnumerator LegacyStartCombat_ActivatesEveryRegisteredController()
    {
        CombatantFixture first = CreateCombatant("First", "Players", 300);
        CombatantFixture second = CreateCombatant("Second", "Enemies", 200);
        CombatantFixture third = CreateCombatant("Third", "Enemies", 100);

        manager.StartCombat();
        yield return WaitForTurn(first.GameObject);

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(new[] { first.GameObject, second.GameObject, third.GameObject })
        );
        Assert.That(manager.WhosTurn(), Is.SameAs(first.GameObject));
        Assert.That(first.Controller.StartTurnCount, Is.EqualTo(1));
    }

    private IEnumerator WaitForTurn(GameObject expected = null)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (
            (manager.WhosTurn() == null || (expected != null && manager.WhosTurn() != expected))
            && Time.realtimeSinceStartup < deadline
        )
            yield return null;
        Assert.That(manager.WhosTurn(), expected == null ? Is.Not.Null : Is.SameAs(expected));
    }

    private IEnumerator AssertLethalActionLifecycle(
        CombatantFixture player,
        CombatantFixture enemy,
        EntityAction action,
        int expectedActionCost,
        bool configureGridTarget
    )
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        object previousGrid = singletonField.GetValue(null);
        TestGridAPI grid = Create("Lifecycle Test GridAPI").AddComponent<TestGridAPI>();
        if (configureGridTarget)
            grid.StrikeTarget = enemy.GameObject;
        singletonField.SetValue(null, grid);
        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForTurn(player.GameObject);
            RuleDispatcher dispatcher = GetEncounterDispatcher();
            RecordingFactObserver<LegacyActionsSpentFact> actions = new();
            RecordingFactObserver<LegacyMapIncrementedFact> map = new();
            dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(actions);
            dispatcher.RegisterFactObserver<LegacyMapIncrementedFact>(map);
            int completionCalls = 0;
            manager.DungeonCombatEnded += _ => completionCalls++;

            // A prior fixture's delayed OnDestroy can clear the generic singleton after this
            // helper creates its replacement. Publish the selected grid at the invocation edge.
            singletonField.SetValue(null, grid);
            player.Controller.TakeAction(action);
            yield return WaitForCondition(
                () => !manager.IsCombatActive && !player.Controller.IsTakingAction,
                "Lethal Strike completion left combat or action presentation deferred."
            );

            Assert.That(actions.Facts, Has.Count.EqualTo(1));
            Assert.That(actions.Facts[0].Amount, Is.EqualTo(expectedActionCost));
            Assert.That(map.Facts, Has.Count.EqualTo(1));
            Assert.That(map.Facts[0].AttackCount, Is.EqualTo(1));
            Assert.That(completionCalls, Is.EqualTo(1));
            Assert.That(player.Controller.IsTakingAction, Is.False);
        }
        finally
        {
            singletonField.SetValue(null, previousGrid);
        }
    }

    private RuleDispatcher GetEncounterDispatcher()
    {
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        FieldInfo dispatcherField = typeof(UnityEncounterRulesBridge).GetField(
            "dispatcher",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        RuleDispatcher dispatcher = dispatcherField.GetValue(bridge) as RuleDispatcher;
        Assert.That(dispatcher, Is.Not.Null);
        return dispatcher;
    }

    private UnityEncounterRulesBridge GetEncounterBridge()
    {
        FieldInfo bridgeField = typeof(CombatManager).GetField(
            "encounterRules",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        UnityEncounterRulesBridge bridge =
            bridgeField.GetValue(manager) as UnityEncounterRulesBridge;
        Assert.That(bridge, Is.Not.Null);
        return bridge;
    }

    private static IEnumerator WaitForCondition(Func<bool> condition, string timeoutMessage)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(condition(), Is.True, timeoutMessage);
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
            GridAPI grid in UnityEngine.Object.FindObjectsByType<GridAPI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            UnityEngine.Object.DestroyImmediate(grid.gameObject);
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
        Assert.That(controller.Reacted, Is.True);
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

        public void AddTestMovement(EntityAction movement) => Movements.Add(movement);

        public override void StartTurn()
        {
            StartTurnCount++;
            base.StartTurn();
        }

        public override void EndTurn()
        {
            if (!HasTurnAuthority)
                return;

            IsTakingAction = false;
            CombatManagerInterface.GetInstance().NextTurn();
        }
    }

    private sealed class TestEntityAction : MultiFrameEntityAction
    {
        private readonly Action invocation;

        public TestEntityAction(string name, uint cost, Action invocation)
            : base(cost)
        {
            ActionName = name;
            this.invocation = invocation;
        }

        public override string ActionName { get; }

        /// <inheritdoc/>
        protected override IEnumerator MFInvoke(GameObject target)
        {
            ActionController controller = target.GetComponent<ActionController>();
            invocation();
            yield return CoroutineRunner.Await(PayCostAsync(controller));
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
        public GameObject StrikeTarget { get; set; }

        public override IEnumerator Stride(GameObject character)
        {
            yield break;
        }

        public override IEnumerator GetStrikeTarget(
            GameObject attacker,
            StrikeTargetRequest request,
            CoroutineResult<StrikeTargetResult> target
        )
        {
            if (StrikeTarget != null)
            {
                target.Value = new StrikeTargetResult
                {
                    Target = StrikeTarget,
                    DistanceFeet = 5,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                    Cover = StrikeCover.None,
                    RangePenalty = 0,
                };
            }
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

    private sealed class RecordingFactObserver<TFact> : IFactObserver<TFact>
        where TFact : RuleFact
    {
        private readonly List<TFact> facts = new();

        public IReadOnlyList<TFact> Facts => facts;

        public ValueTask OnFactCommitted(TFact fact, RulesSnapshot snapshot)
        {
            facts.Add(fact);
            return default;
        }
    }
}
