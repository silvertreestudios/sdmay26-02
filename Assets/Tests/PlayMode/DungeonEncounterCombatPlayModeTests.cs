using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Combat.Encounters;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
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

    /// <summary>Verifies host removal does not mutate the authoritative active roster.</summary>
    [Test]
    public void Remove_CurrentTurnOwnerClearsReferenceWithoutReentrantAdvance()
    {
        CombatantFixture current = CreateCombatant("Current", "Players", 100);
        CombatantFixture next = CreateCombatant("Next", "Enemies", 50);
        CombatantFixture ally = CreateCombatant("Ally", "Players", 0);
        manager.StartDungeonCombat(new[] { current.Controller, next.Controller, ally.Controller });
        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));

        manager.Remove(current.Controller);

        Assert.That(manager.WhosTurn(), Is.SameAs(current.GameObject));
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(new[] { current.GameObject, next.GameObject, ally.GameObject })
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
            CombatantFixture survivingEnemy = CreateCombatant("Surviving Enemy", "Enemies", -100);
            DungeonEncounterMember member = enemy.GameObject.AddComponent<DungeonEncounterMember>();
            int deathCalls = 0;
            UnityAction<GameObject> deathListener = defeated =>
            {
                if (defeated == enemy.GameObject)
                    deathCalls++;
            };
            OnDeath.AddListener(deathListener);
            try
            {
                manager.StartDungeonCombat(
                    new[] { player.Controller, enemy.Controller, survivingEnemy.Controller }
                );

                Assert.DoesNotThrow(() =>
                    enemy.Creature.ApplyFinalDamage(10, RuleSource.FromSlug("test-lethal-damage"))
                );
                Assert.DoesNotThrow(() =>
                    enemy.Creature.ApplyFinalDamage(1, RuleSource.FromSlug("test-repeated-damage"))
                );

                Assert.That(member.IsConfigured, Is.False);
                Assert.That(member.DefeatWasReported, Is.False);
                Assert.That(deathCalls, Is.EqualTo(1));
                Assert.That(grid.DestroyedTokens, Contains.Item(enemy.GameObject));
                Assert.That(enemy.Controller.enabled, Is.False);
                Assert.That(enemy.GameObject.activeSelf, Is.False);
                Assert.That(manager.GetCombatants(), Has.No.Member(enemy.GameObject));
            }
            finally
            {
                OnDeath.RemoveListener(deathListener);
            }
        }
        finally
        {
            singletonField.SetValue(null, previousGrid);
        }
    }

    /// <summary>Verifies camera framing excludes defeated roster slots retained for timing.</summary>
    [Test]
    public void getPoistions_ActiveCombatReturnsOnlyLivingEncounterParticipants()
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
            Create("Test GridAPI").AddComponent<TestGridAPI>();
            CombatantFixture player = CreateCombatant("Player", "Players", 100);
            CombatantFixture defeated = CreateCombatant("Defeated Enemy", "Enemies", 50);
            CombatantFixture survivor = CreateCombatant("Surviving Enemy", "Enemies", 0);
            player.GameObject.transform.position = new Vector3(1f, 0f, 1f);
            defeated.GameObject.transform.position = new Vector3(9f, 0f, 9f);
            survivor.GameObject.transform.position = new Vector3(2f, 0f, 2f);
            manager.StartDungeonCombat(
                new[] { player.Controller, defeated.Controller, survivor.Controller }
            );

            defeated.Creature.ApplyFinalDamage(10, RuleSource.FromSlug("camera-framing-test"));

            Assert.That(
                manager.getPoistions(),
                Is.EquivalentTo(
                    new[]
                    {
                        player.GameObject.transform.position,
                        survivor.GameObject.transform.position,
                    }
                )
            );
            Assert.That(defeated.GameObject.activeSelf, Is.False);
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

    /// <summary>Verifies disabled and inactive controllers cannot join an active encounter.</summary>
    [Test]
    public void AddDungeonReinforcements_RejectsDisabledAndInactiveControllers()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        CombatantFixture reinforcement = CreateCombatant("Reinforcement", "Enemies", 50);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });

        reinforcement.Controller.enabled = false;
        Assert.Throws<InvalidOperationException>(() =>
            manager.AddDungeonReinforcements(new[] { reinforcement.Controller })
        );

        reinforcement.Controller.enabled = true;
        reinforcement.GameObject.SetActive(false);
        Assert.Throws<InvalidOperationException>(() =>
            manager.AddDungeonReinforcements(new[] { reinforcement.Controller })
        );
        Assert.That(
            manager.GetCombatants(),
            Is.EquivalentTo(new[] { player.GameObject, enemy.GameObject })
        );
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
        player.Controller.IsTakingAction = true;
        enemy.Controller.ActionPoints = 1;
        enemy.Controller.Reacted = true;
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

    /// <summary>
    /// Verifies suspension releases health ownership for removed combatants without losing any
    /// authoritative health fields.
    /// </summary>
    [Test]
    public void SuspendDungeonCombat_ReleasesRemovedCombatantCompleteHealthState()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        RuleSource temporarySource = RuleSource.FromSlug("test-temporary-health");
        RuleSource immunitySource = RuleSource.FromSlug("test-temporary-immunity");

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        player.Creature.ApplyFinalDamage(2, RuleSource.FromSlug("test-wound"));
        player.Creature.GrantSourceTemporaryHitPoints(temporarySource, 4);
        player.Creature.AddTemporaryHitPointImmunity(immunitySource);
        HealthState expectedHealth = player.Creature.Health;
        manager.Remove(player.Controller);

        manager.SuspendDungeonCombat();

        Assert.That(player.Creature.Health, Is.EqualTo(expectedHealth));
        Assert.DoesNotThrow(() => player.Creature.InitializeHealthBeforeEncounter(expectedHealth));
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
            enemy.Creature.ApplyFinalDamage(enemy.Creature.hp, RuleSource.FromSlug("test-victory"));

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

    /// <summary>
    /// Verifies every completion channel observes inactive combat and cannot strand ended rules
    /// ownership when a callback throws.
    /// </summary>
    [TestCase("DungeonCombatEnded")]
    [TestCase("LegacyCombatEnded")]
    [TestCase("LegacyCombatOutcome")]
    public void ThrowingCompletionCallback_ReleasesCombatBeforePropagating(string channel)
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        Action<string> dungeonEndListener = _ => ThrowFromCompletionCallback();
        UnityAction<string> legacyEndListener = _ => ThrowFromCompletionCallback();
        UnityAction<bool> legacyOutcomeListener = _ => ThrowFromCompletionCallback();

        switch (channel)
        {
            case "DungeonCombatEnded":
                manager.DungeonCombatEnded += dungeonEndListener;
                break;
            case "LegacyCombatEnded":
                OnCombatEnd.AddListener(legacyEndListener);
                break;
            case "LegacyCombatOutcome":
                OnCombatOutcome.AddListener(legacyOutcomeListener);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
        }

        try
        {
            if (channel == "DungeonCombatEnded")
                manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            else
                manager.StartCombat();

            CombatantFixture nextPlayer = CreateCombatant("Next Player", "Players", 100);
            CombatantFixture nextEnemy = CreateCombatant("Next Enemy", "Enemies", 0);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                enemy.Creature.ApplyFinalDamage(
                    enemy.Creature.hp,
                    RuleSource.FromSlug("test-throwing-completion")
                )
            );

            Assert.That(exception.Message, Is.EqualTo("Completion callback failed."));
            Assert.That(manager.IsCombatActive, Is.False);
            Assert.DoesNotThrow(() =>
                manager.StartDungeonCombat(new[] { nextPlayer.Controller, nextEnemy.Controller })
            );
            Assert.That(manager.IsCombatActive, Is.True);
        }
        finally
        {
            manager.DungeonCombatEnded -= dungeonEndListener;
            OnCombatEnd.RemoveListener(legacyEndListener);
            OnCombatOutcome.RemoveListener(legacyOutcomeListener);
        }

        void ThrowFromCompletionCallback()
        {
            Assert.That(manager.IsCombatActive, Is.False);
            throw new InvalidOperationException("Completion callback failed.");
        }
    }

    /// <summary>
    /// Verifies completion callbacks can synchronously begin a new encounter without delayed
    /// cleanup from the ended bridge clearing the replacement encounter.
    /// </summary>
    [Test]
    public void DungeonCompletionCallback_CanSynchronouslyStartNextEncounter()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        CombatantFixture nextPlayer = CreateCombatant("Next Player", "Players", 100);
        CombatantFixture nextEnemy = CreateCombatant("Next Enemy", "Enemies", 0);
        UnityCombatRulesBridge endedBridge = null;
        UnityCombatRulesBridge restartedBridge = null;
        Action<string> listener = _ =>
        {
            Assert.That(manager.IsCombatActive, Is.False);
            manager.StartDungeonCombat(new[] { nextPlayer.Controller, nextEnemy.Controller });
            restartedBridge = GetCombatRules(manager);
        };
        manager.DungeonCombatEnded += listener;

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            endedBridge = GetCombatRules(manager);

            enemy.Creature.ApplyFinalDamage(
                enemy.Creature.hp,
                RuleSource.FromSlug("test-synchronous-restart")
            );

            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(restartedBridge, Is.Not.Null);
            Assert.That(restartedBridge, Is.Not.SameAs(endedBridge));
            Assert.That(
                manager.GetCombatants(),
                Is.EquivalentTo(new[] { nextPlayer.GameObject, nextEnemy.GameObject })
            );
            Assert.That(manager.CheckForEndOfGame(), Is.False);
        }
        finally
        {
            manager.DungeonCombatEnded -= listener;
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
            player.Creature.ApplyFinalDamage(
                player.Creature.hp,
                RuleSource.FromSlug("test-defeat")
            );

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

    /// <summary>Verifies defeat reports the living opposition team rather than an earlier dead team.</summary>
    [Test]
    public void DungeonDefeat_ReportsLivingOppositionAcrossMultipleTeams()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture fallenOpposition = CreateCombatant("Fallen Opposition", "Fallen Foes", 50);
        CombatantFixture livingOpposition = CreateCombatant("Living Opposition", "Living Foes", 0);
        string winner = string.Empty;
        Action<string> listener = winningTeam => winner = winningTeam;
        manager.DungeonCombatEnded += listener;

        try
        {
            manager.StartDungeonCombat(
                new[]
                {
                    player.Controller,
                    fallenOpposition.Controller,
                    livingOpposition.Controller,
                }
            );
            fallenOpposition.Creature.ApplyFinalDamage(
                fallenOpposition.Creature.hp,
                RuleSource.FromSlug("test-first-opposition-defeat")
            );
            player.Creature.ApplyFinalDamage(
                player.Creature.hp,
                RuleSource.FromSlug("test-player-defeat")
            );

            Assert.That(manager.CheckForEndOfGame(), Is.True);
            Assert.That(winner, Is.EqualTo("Living Foes"));
        }
        finally
        {
            manager.DungeonCombatEnded -= listener;
        }
    }

    /// <summary>Verifies an all-zero player defeat reports no surviving opposition winner.</summary>
    [Test]
    public void DungeonDefeat_AllZeroRosterReportsEmptyWinner()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        string winner = "not-reported";
        Action<string> listener = winningTeam => winner = winningTeam;
        manager.DungeonCombatEnded += listener;

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            UnityCombatRulesBridge bridge = GetCombatRules(manager);
            CreatureId playerId = bridge.GetCreatureId(player.Controller);
            CreatureId enemyId = bridge.GetCreatureId(enemy.Controller);
            OpResult<HealthBatchOutcome> result = bridge.Dispatch(
                new ApplyHealthBatchOp(
                    new[]
                    {
                        new HealthBatchChange(
                            HealthBatchChangeKind.Damage,
                            playerId,
                            player.Creature.hp,
                            new HealthChangeOriginId("test-all-zero-player"),
                            RuleSource.FromSlug("test-all-zero")
                        ),
                        new HealthBatchChange(
                            HealthBatchChangeKind.Damage,
                            enemyId,
                            enemy.Creature.hp,
                            new HealthChangeOriginId("test-all-zero-enemy"),
                            RuleSource.FromSlug("test-all-zero")
                        ),
                    }
                )
            );

            Assert.That(result, Is.TypeOf<ResolvedOpResult<HealthBatchOutcome>>());
            Assert.That(manager.CheckForEndOfGame(), Is.True);
            Assert.That(winner, Is.EqualTo(string.Empty));
        }
        finally
        {
            manager.DungeonCombatEnded -= listener;
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
        player.Controller.SetDungeonExploration(true);

        player.Controller.TakeAction(movement);
        player.Controller.TakeAction(movement);
        player.Controller.TakeAction(attack);

        Assert.That(movementCalls, Is.EqualTo(2));
        Assert.That(attackCalls, Is.Zero);
        Assert.That(player.Controller.ActionPoints, Is.EqualTo(2));
        Assert.That(player.Controller.Reacted, Is.True);
        Assert.That(player.Controller.StrikePenalty, Is.Zero);
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

    /// <summary>Verifies legacy scenes derive protagonist identity from registration order.</summary>
    [Test]
    public void LegacyStartCombat_DerivesNonPlayersProtagonistTeam()
    {
        CombatantFixture first = CreateCombatant("First", "TeamA", 300);
        CreateCombatant("Second", "TeamB", 200);

        manager.StartCombat();

        UnityCombatRulesBridge bridge = GetCombatRules(manager);
        CreatureId firstId = bridge.GetCreatureId(first.Creature);
        Assert.That(
            bridge.GetEncounter().ProtagonistTeam,
            Is.EqualTo(bridge.Snapshot.Creatures[firstId].Player)
        );
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(manager.WhosTurn(), Is.SameAs(first.GameObject));
    }

    /// <summary>Verifies a failed committed startup releases ownership so the host can retry.</summary>
    [Test]
    public void LegacyStartCombat_FailedPresentationDoesNotLeaveManagerActive()
    {
        CombatantFixture first = CreateCombatant("First", "TeamA", 300);
        CombatantFixture second = CreateCombatant("Second", "TeamB", 200);
        UnityAction failingPresentation = () =>
            throw new InvalidOperationException("Synthetic encounter-start presentation failure.");
        OnCombatStart.AddListener(failingPresentation);
        try
        {
            Assert.Catch<Exception>(() => manager.StartCombat());
            Assert.That(manager.IsCombatActive, Is.False);
            AssertTransientTurnStateCleared(first.Controller);
            AssertTransientTurnStateCleared(second.Controller);
        }
        finally
        {
            OnCombatStart.RemoveListener(failingPresentation);
        }

        Assert.DoesNotThrow(() => manager.StartCombat());
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(manager.WhosTurn(), Is.SameAs(first.GameObject));
    }

    /// <summary>Verifies legacy combat excludes registered controllers that cannot take turns.</summary>
    [Test]
    public void LegacyStartCombat_ExcludesDisabledAndInactiveControllers()
    {
        CombatantFixture activePlayer = CreateCombatant("Active Player", "Players", 300);
        CombatantFixture activeEnemy = CreateCombatant("Active Enemy", "Enemies", 250);
        CombatantFixture disabled = CreateCombatant("Disabled", "Enemies", 200);
        CombatantFixture inactive = CreateCombatant("Inactive", "Enemies", 100);
        disabled.Controller.enabled = false;
        inactive.GameObject.SetActive(false);

        manager.StartCombat();

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            manager.GetCombatants(),
            Is.EqualTo(new[] { activePlayer.GameObject, activeEnemy.GameObject })
        );
        Assert.That(manager.WhosTurn(), Is.SameAs(activePlayer.GameObject));
        Assert.That(disabled.Controller.StartTurnCount, Is.Zero);
        Assert.That(inactive.Controller.StartTurnCount, Is.Zero);
    }

    /// <summary>Verifies explicit dungeon starts reject disabled and inactive participants.</summary>
    [Test]
    public void StartDungeonCombat_RejectsDisabledAndInactiveParticipants()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);

        enemy.Controller.enabled = false;
        Assert.Throws<InvalidOperationException>(() =>
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller })
        );

        enemy.Controller.enabled = true;
        enemy.GameObject.SetActive(false);
        Assert.Throws<InvalidOperationException>(() =>
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller })
        );
        Assert.That(manager.IsCombatActive, Is.False);
    }

    /// <summary>Verifies a gridless mindless actor explicitly completes its committed turn.</summary>
    [UnityTest]
    public IEnumerator MindlessController_WithoutGridAdvancesToNextCombatant()
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
            GameObject aiObject = Create("Gridless AI");
            CreatureComponent aiCreature = aiObject.AddComponent<CreatureComponent>();
            aiCreature.name = "Gridless AI";
            aiCreature.initiative = 200;
            aiCreature.InitializeHealthBeforeEncounter(10, 10);
            Team aiTeam = aiObject.AddComponent<Team>();
            aiTeam.Name = "Enemies";
            MindlessController ai = aiObject.AddComponent<MindlessController>();
            CombatantFixture player = CreateCombatant("Player", "Players", 100);

            manager.StartDungeonCombat(new ActionController[] { ai, player.Controller });

            Assert.That(manager.WhosTurn(), Is.SameAs(aiObject));
            yield return null;

            Assert.That(manager.WhosTurn(), Is.SameAs(player.GameObject));
            Assert.That(ai.HasTurnAuthority, Is.False);
            Assert.That(player.Controller.StartTurnCount, Is.EqualTo(1));
        }
        finally
        {
            singletonField.SetValue(null, previousGrid);
        }
    }

    /// <summary>
    /// Verifies an authoritative AI turn rejects grid replacement during its startup and decision
    /// delays, before an action marks itself in progress.
    /// </summary>
    [UnityTest]
    public IEnumerator MindlessController_AuthoritativeTurnRejectsRebindDuringDelays()
    {
        GameObject aiObject = Create("Delayed AI");
        CreatureComponent aiCreature = aiObject.AddComponent<CreatureComponent>();
        aiCreature.name = "Delayed AI";
        aiCreature.initiative = 200;
        aiCreature.InitializeHealthBeforeEncounter(10, 10);
        Team aiTeam = aiObject.AddComponent<Team>();
        aiTeam.Name = "Enemies";
        DelayedDecisionMindlessController ai =
            aiObject.AddComponent<DelayedDecisionMindlessController>();
        ai.Configure(new TestGridBinding(), new TestEntityAction("Delayed Decision", 1, () => { }));
        CombatantFixture player = CreateCombatant("Player", "Players", 100);

        manager.StartDungeonCombat(new ActionController[] { ai, player.Controller });

        Assert.That(ai.HasTurnAuthority, Is.True);
        Assert.That(ai.IsTakingAction, Is.False);
        Assert.That(
            ai.CanRebindGrid(),
            Is.False,
            "The initial coroutine yield must retain authoritative turn ownership."
        );

        yield return new WaitUntil(() => ai.DecisionCount == 1);

        Assert.That(ai.HasTurnAuthority, Is.True);
        Assert.That(ai.IsTakingAction, Is.False);
        Assert.That(
            ai.CanRebindGrid(),
            Is.False,
            "The pre-invocation decision delay must retain authoritative turn ownership."
        );
    }

    /// <summary>
    /// Verifies active encounter AI preserves directional TeamRules friendship for distinct teams.
    /// </summary>
    [Test]
    public void MindlessDecision_ActiveEncounterIgnoresDirectionalFriendAndTargetsHostileTeam()
    {
        TeamRules teamRules = Create("Team Rules").AddComponent<TeamRules>();
        teamRules.AddHostileTeam("AI");
        teamRules.AddHostileTeam("Ally");
        teamRules.AddHostileTeam("Players");
        teamRules.OneWayFriendly("AI", "Ally");

        GameObject actor = Create("Mindless Actor");
        CreatureComponent actorCreature = actor.AddComponent<CreatureComponent>();
        actorCreature.name = "Mindless Actor";
        actorCreature.speed = 25;
        actorCreature.InitializeHealthBeforeEncounter(10, 10);
        actor.AddComponent<Conditions>();
        MindlessController ai = actor.AddComponent<MindlessController>();
        Team actorTeam = actor.AddComponent<Team>();
        actorTeam.Name = "AI";
        manager.AddCombatant(ai);

        CombatantFixture ally = CreateCombatant("Directional Ally", "Ally", 0);
        CombatantFixture hostile = CreateCombatant("Hostile Player", "Players", 0);
        actor.transform.position = new Vector3(0, 0, 1);
        ally.GameObject.transform.position = new Vector3(2, 0, 1);
        hostile.GameObject.transform.position = new Vector3(4, 0, 1);
        Tile[,] tiles = new Tile[5, 3];
        for (int x = 0; x < tiles.GetLength(0); x++)
        for (int z = 0; z < tiles.GetLength(1); z++)
            tiles[x, z] = new Tile();
        tiles[0, 1].Occupants.Add(actor);
        tiles[2, 1].Occupants.Add(ally.GameObject);
        tiles[4, 1].Occupants.Add(hostile.GameObject);
        ai.RebindGrid(new MindlessTestGridBinding(tiles));
        manager.StartDungeonCombat(
            new ActionController[] { hostile.Controller, ai, ally.Controller }
        );

        ai.MindlessDecision();

        Assert.That(teamRules.IsFriendly("AI", "Ally"), Is.True);
        Assert.That(teamRules.IsFriendly("Ally", "AI"), Is.False);
        Assert.That(ai.BestTarget, Is.SameAs(hostile.GameObject));
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

    private static UnityCombatRulesBridge GetCombatRules(CombatManager combatManager)
    {
        FieldInfo field = typeof(CombatManager).GetField(
            "combatRules",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (UnityCombatRulesBridge)field.GetValue(combatManager);
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
            if (!HasTurnAuthority)
                return;

            IsTakingAction = false;
            CombatManagerInterface.GetInstance().NextTurn();
        }
    }

    private sealed class DelayedDecisionMindlessController : MindlessController
    {
        private EntityAction decision;

        public int DecisionCount { get; private set; }

        public void Configure(GridAPIPrivate grid, EntityAction nextDecision)
        {
            RebindGrid(grid);
            decision = nextDecision;
        }

        protected override EntityAction SelectNextAction()
        {
            DecisionCount++;
            return decision;
        }
    }

    private sealed class TestGridBinding : GridAPIPrivate
    {
        private readonly Tile[,] tiles =
        {
            { new Tile() },
        };
        private readonly bool[,] lineOfSightBlocks = new bool[1, 1];
        private readonly IPathfinder pathfinder;

        public TestGridBinding()
        {
            pathfinder = new Dijkstra(tiles);
        }

        public Tile[,] GetTiles() => tiles;

        public bool[,] GetLineOfSightBlocks() => lineOfSightBlocks;

        public IPathfinder GetPathfinder() => pathfinder;

        public bool AddToken(GameObject token) => true;

        public bool DestroyToken(GameObject token) => true;
    }

    private sealed class MindlessTestGridBinding : GridAPIPrivate
    {
        private readonly Tile[,] tiles;
        private readonly bool[,] lineOfSightBlocks;
        private readonly IPathfinder pathfinder;

        public MindlessTestGridBinding(Tile[,] tiles)
        {
            this.tiles = tiles;
            lineOfSightBlocks = new bool[tiles.GetLength(0), tiles.GetLength(1)];
            pathfinder = new Dijkstra(tiles);
        }

        public Tile[,] GetTiles() => tiles;

        public bool[,] GetLineOfSightBlocks() => lineOfSightBlocks;

        public IPathfinder GetPathfinder() => pathfinder;

        public bool AddToken(GameObject token) => true;

        public bool DestroyToken(GameObject token) => true;
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
