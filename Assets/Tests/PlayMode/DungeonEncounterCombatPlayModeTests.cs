using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Game.AbilityActions;
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

    /// <summary>
    /// Verifies camera framing excludes defeated timing slots without removing their rules identity.
    /// </summary>
    [UnityTest]
    public IEnumerator DefeatedRetainedParticipant_IsExcludedFromOrderedCameraPositions()
    {
        CombatantFixture player = CreateCombatant("Camera Player", "Players", 300);
        CombatantFixture defeatedEnemy = CreateCombatant("Defeated Camera Enemy", "Enemies", 200);
        CombatantFixture livingEnemy = CreateCombatant("Living Camera Enemy", "Enemies", 100);
        player.GameObject.transform.position = new Vector3(1f, 0f, 2f);
        defeatedEnemy.GameObject.transform.position = new Vector3(50f, 0f, 60f);
        livingEnemy.GameObject.transform.position = new Vector3(3f, 0f, 4f);

        manager.StartDungeonCombat(
            new[] { player.Controller, defeatedEnemy.Controller, livingEnemy.Controller }
        );
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        CreatureId playerId = bridge.GetCreatureId(player.Creature);
        CreatureId defeatedId = bridge.GetCreatureId(defeatedEnemy.Creature);
        CreatureId livingId = bridge.GetCreatureId(livingEnemy.Creature);

        yield return CoroutineRunner.Await(
            defeatedEnemy.Creature.ApplyFinalDamageAsync(
                defeatedEnemy.Creature.hp,
                RuleSource.FromSlug("test-camera-defeat")
            )
        );

        EncounterState encounter = bridge.Snapshot.Encounters[bridge.EncounterId];
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            encounter.Roster.Select(entry => entry.Creature),
            Is.EqualTo(new[] { playerId, defeatedId, livingId }),
            "Defeat must retain the immutable initiative timing slot."
        );
        Assert.That(bridge.GetController(defeatedId), Is.SameAs(defeatedEnemy.Controller));
        Assert.That(defeatedEnemy.GameObject.activeSelf, Is.False);
        Assert.That(
            manager.GetCombatants(),
            Is.EqualTo(new[] { player.GameObject, livingEnemy.GameObject })
        );
        Assert.That(
            manager.getPoistions(),
            Is.EqualTo(
                new[]
                {
                    player.GameObject.transform.position,
                    livingEnemy.GameObject.transform.position,
                }
            ),
            "Camera inputs must preserve living gameplay order and exclude defeated positions."
        );
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

    /// <summary>Verifies affected creatures outside the active roster reject before spell costs.</summary>
    [UnityTest]
    public IEnumerator AreaSpellsRejectOffRosterTargetsBeforeAnyCostOrEffect()
    {
        CombatantFixture player = CreateCombatant("Roster Spell Player", "Players", 200);
        CombatantFixture activeEnemy = CreateCombatant("Roster Spell Enemy", "Enemies", 100);
        CombatantFixture offRoster = CreateCombatant("Suspended Encounter Creature", "Enemies", 0);
        player.Creature.InitializeHealthBeforeEncounter(5, 10);
        player.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        player.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            player.Creature,
            player.Creature.Build
        );
        manager.StartDungeonCombat(new[] { player.Controller, activeEnemy.Controller });
        yield return WaitForTurn(player.GameObject);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        RecordingFactObserver<LegacyActionsSpentFact> spends = new();
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
        SpellcastingState spellcasting = player.Creature.Prepared.Spellcasting;
        int healSlots = spellcasting.Pools["font-heal"].UsesRemaining;
        int playerHealth = player.Creature.hp;
        int activeEnemyHealth = activeEnemy.Creature.hp;
        int offRosterHealth = offRoster.Creature.hp;
        AreaTargetResult area = new()
        {
            Creatures = new List<AreaAffectedCreature>
            {
                new() { Creature = player.GameObject, LineOfEffect = StrikeLineOfEffect.Clear },
                new()
                {
                    Creature = activeEnemy.GameObject,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                },
                new() { Creature = offRoster.GameObject, LineOfEffect = StrikeLineOfEffect.Clear },
            },
        };

        player.Controller.IsTakingAction = true;
        CoroutineResult<CastSpellResult> heal = new();
        yield return CoroutineRunner.Await(
            SpellcastingRuntime.CastAsync(
                player.GameObject,
                spellcasting.GetSpell("heal"),
                3,
                Array.Empty<GameObject>(),
                area,
                spendActions: true
            ),
            heal
        );

        Assert.That(heal.Value.Success, Is.False);
        Assert.That(heal.Value.Message, Does.Contain("active encounter"));
        Assert.That(heal.Value.Targets, Is.Empty);
        Assert.That(heal.Value.Rolls, Is.Empty);
        Assert.That(player.Controller.ActionPoints, Is.EqualTo(3u));
        Assert.That(spellcasting.Pools["font-heal"].UsesRemaining, Is.EqualTo(healSlots));
        Assert.That(player.Creature.hp, Is.EqualTo(playerHealth));
        Assert.That(activeEnemy.Creature.hp, Is.EqualTo(activeEnemyHealth));
        Assert.That(offRoster.Creature.hp, Is.EqualTo(offRosterHealth));
        Assert.That(player.Controller.IsTakingAction, Is.False);
        Assert.That(spends.Facts, Is.Empty);

        player.Controller.IsTakingAction = true;
        CoroutineResult<CastSpellResult> hymn = new();
        yield return CoroutineRunner.Await(
            SpellcastingRuntime.CastAsync(
                player.GameObject,
                spellcasting.GetSpell("haunting-hymn"),
                2,
                Array.Empty<GameObject>(),
                area,
                spendActions: true
            ),
            hymn
        );

        Assert.That(hymn.Value.Success, Is.False);
        Assert.That(hymn.Value.Message, Does.Contain("active encounter"));
        Assert.That(hymn.Value.Targets, Is.Empty);
        Assert.That(hymn.Value.Rolls, Is.Empty);
        Assert.That(player.Controller.ActionPoints, Is.EqualTo(3u));
        Assert.That(player.Creature.hp, Is.EqualTo(playerHealth));
        Assert.That(activeEnemy.Creature.hp, Is.EqualTo(activeEnemyHealth));
        Assert.That(offRoster.Creature.hp, Is.EqualTo(offRosterHealth));
        Assert.That(activeEnemy.Conditions.Contains("Deafened"), Is.False);
        Assert.That(offRoster.Conditions.Contains("Deafened"), Is.False);
        Assert.That(player.Controller.IsTakingAction, Is.False);
        Assert.That(spends.Facts, Is.Empty);
        dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(spends);
    }

    /// <summary>Verifies a rejected queued Rage spend cannot publish prepared or health state.</summary>
    [UnityTest]
    public IEnumerator RageSpendRejectedAfterTurnEndPublishesNoRageState()
    {
        CombatantFixture player = CreateCombatant("Rejected Rage Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Rejected Rage Enemy", "Enemies", 0);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        PrepareBarbarian(player.Creature);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<TurnEndedFact> blocker = new(failAfterRelease: false);
        RecordingFactObserver<LegacyActionsSpentFact> spends = new();
        dispatcher.RegisterFactObserver<TurnEndedFact>(blocker);
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
        Task<EncounterAdvanceOutcome> ending = bridge.EndTurn(bridge.CurrentTurn.Value).AsTask();
        Task<bool> rage = null;

        try
        {
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The turn-end Fact observer did not occupy the dispatcher root."
            );
            rage = new Rage(1).UseRageAsync(player.GameObject).AsTask();
            yield return null;

            Assert.That(rage.IsCompleted, Is.False);
            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.False);
            Assert.That(player.Creature.Health.Temporary, Is.Zero);
            Assert.That(spends.Facts, Is.Empty);

            blocker.Release();
            yield return CoroutineRunner.Await(new ValueTask<EncounterAdvanceOutcome>(ending));
            yield return WaitForCondition(
                () => rage.IsCompleted,
                "The queued rejected Rage spend did not settle."
            );

            Assert.That(rage.IsFaulted, Is.True);
            StringAssert.Contains("insufficient authoritative actions", rage.Exception.ToString());
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.False);
            Assert.That(player.Creature.Prepared.HasActiveEffect("effect-rage"), Is.False);
            Assert.That(player.Creature.Health.Temporary, Is.Zero);
            Assert.That(player.Creature.tempHp, Is.Zero);
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(spends.Facts, Is.Empty);
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<TurnEndedFact>(blocker);
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(spends);
            _ = rage?.Exception;
        }
    }

    /// <summary>Verifies Rage retains its action reservation through awaited health callbacks.</summary>
    [UnityTest]
    public IEnumerator RageHealthFactFailureClearsReservationOnlyAfterAllEffectsSettle()
    {
        CombatantFixture player = CreateCombatant("Awaited Rage Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Awaited Rage Enemy", "Enemies", 0);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        PrepareBarbarian(player.Creature);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<TemporaryHitPointsGrantedFact> blocker = new(failAfterRelease: true);
        RecordingFactObserver<LegacyActionsSpentFact> spends = new();
        dispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
        int competingInvocations = 0;
        TestEntityAction competing = new("Competing Action", 0, () => competingInvocations++);
        Task<bool> rage = new Rage(1).UseRageAsync(player.GameObject).AsTask();

        try
        {
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The Rage temporary-HP Fact observer did not begin."
            );

            Assert.That(rage.IsCompleted, Is.False);
            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(player.Controller.ActionPoints, Is.EqualTo(2u));
            Assert.That(spends.Facts, Has.Count.EqualTo(1));
            Assert.That(spends.Facts[0].Amount, Is.EqualTo(1));
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.True);
            Assert.That(player.Creature.Health.Temporary, Is.EqualTo(2));
            player.Controller.TakeAction(competing);
            yield return null;
            Assert.That(competingInvocations, Is.Zero);
            Assert.That(player.Controller.IsTakingAction, Is.True);

            blocker.Release();
            yield return WaitForCondition(
                () => rage.IsCompleted,
                "The failed Rage health callback did not settle."
            );

            Assert.That(rage.IsFaulted, Is.True);
            StringAssert.Contains("deliberate Rage Fact failure", rage.Exception.ToString());
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(competingInvocations, Is.Zero);
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.True);
            Assert.That(player.Creature.Health.Temporary, Is.EqualTo(2));
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(spends);
            _ = rage.Exception;
        }
    }

    /// <summary>Verifies area-spell outcome presentation waits for every target's health change.</summary>
    [UnityTest]
    public IEnumerator LethalAreaSpellSettlesAllTargetsBeforePresentingDeterministicDefeat()
    {
        CombatantFixture player = CreateCombatant("Area Spell Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Area Spell Enemy", "Enemies", 0);
        player.Creature.InitializeHealthBeforeEncounter(1, 1);
        enemy.Creature.InitializeHealthBeforeEncounter(1, 1);
        player.Creature.level = 20;
        player.Creature.wisMod = 20;
        player.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        player.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            player.Creature,
            player.Creature.Build
        );
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        CreatureId playerId = bridge.GetCreatureId(player.Creature);
        CreatureId enemyId = bridge.GetCreatureId(enemy.Creature);
        int outcomeCalls = 0;
        int playerHealthAtOutcome = -1;
        int enemyHealthAtOutcome = -1;
        EncounterOutcome? presentedOutcome = null;
        Func<EncounterOutcome, ValueTask> recordOutcome = outcome =>
        {
            outcomeCalls++;
            playerHealthAtOutcome = bridge.Snapshot.Health[playerId].Current;
            enemyHealthAtOutcome = bridge.Snapshot.Health[enemyId].Current;
            presentedOutcome = outcome;
            return default;
        };
        bridge.EncounterEnded += recordOutcome;
        PreparedSpell hymn = player.Creature.Prepared.Spellcasting.GetSpell("haunting-hymn");
        AreaTargetResult area = new AreaTargetResult
        {
            Creatures = new List<AreaAffectedCreature>
            {
                new AreaAffectedCreature
                {
                    Creature = player.GameObject,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                },
                new AreaAffectedCreature
                {
                    Creature = enemy.GameObject,
                    LineOfEffect = StrikeLineOfEffect.Clear,
                },
            },
        };
        CoroutineResult<CastSpellResult> completed = new CoroutineResult<CastSpellResult>();
        try
        {
            yield return CoroutineRunner.Await(
                SpellcastingRuntime.CastAsync(
                    player.GameObject,
                    hymn,
                    2,
                    Array.Empty<GameObject>(),
                    area,
                    spendActions: true
                ),
                completed
            );
        }
        finally
        {
            bridge.EncounterEnded -= recordOutcome;
        }

        Assert.That(completed.Value.Success, Is.True);
        Assert.That(
            completed.Value.Targets.Distinct(),
            Is.EquivalentTo(new[] { player.GameObject, enemy.GameObject })
        );
        Assert.That(outcomeCalls, Is.EqualTo(1));
        Assert.That(playerHealthAtOutcome, Is.Zero);
        Assert.That(enemyHealthAtOutcome, Is.Zero);
        Assert.That(presentedOutcome, Is.EqualTo(EncounterOutcome.PlayerDefeat));
        Assert.That(
            bridge.Snapshot.Encounters[bridge.EncounterId].Outcome,
            Is.EqualTo(EncounterOutcome.PlayerDefeat)
        );
        Assert.That(Enum.GetNames(typeof(EncounterOutcome)), Does.Not.Contain("Draw"));
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

    /// <summary>Verifies multiple living opposition teams use deterministic initiative order.</summary>
    [UnityTest]
    public IEnumerator LegacyDefeatReportsFirstLivingOppositionInInitiativeOrder()
    {
        TeamRules rules = TeamRules.GetInstance();
        rules.AddHostileTeam("Goblins");
        rules.AddHostileTeam("Zombies");
        CombatantFixture player = CreateCombatant("Legacy Player", "Players", 300);
        CreateCombatant("Living Goblin", "Goblins", 200);
        CreateCombatant("Living Zombie", "Zombies", 100);
        string reportedWinner = string.Empty;
        int completionCalls = 0;
        UnityAction<string> recordWinner = winner =>
        {
            completionCalls++;
            reportedWinner = winner;
        };
        OnCombatEnd.AddListener(recordWinner);
        try
        {
            manager.StartCombat();
            yield return WaitForTurn(player.GameObject);
            yield return CoroutineRunner.Await(
                player.Creature.ApplyFinalDamageAsync(
                    player.Creature.hp,
                    RuleSource.FromSlug("test-multiple-opposition-defeat")
                )
            );

            Assert.That(completionCalls, Is.EqualTo(1));
            Assert.That(reportedWinner, Is.EqualTo("Goblins"));
            Assert.That(
                GetEncounterBridge().Snapshot.Encounters[GetEncounterBridge().EncounterId].Outcome,
                Is.EqualTo(EncounterOutcome.PlayerDefeat)
            );
        }
        finally
        {
            OnCombatEnd.RemoveListener(recordWinner);
        }
    }

    /// <summary>Verifies a retained defeated opposition slot cannot be reported as winner.</summary>
    [UnityTest]
    public IEnumerator LegacyDefeatReportsLivingOppositionAfterEarlierTeamIsDefeated()
    {
        TeamRules rules = TeamRules.GetInstance();
        rules.AddHostileTeam("Goblins");
        rules.AddHostileTeam("Zombies");
        CombatantFixture player = CreateCombatant("Legacy Player", "Players", 300);
        CombatantFixture goblin = CreateCombatant("Defeated Goblin", "Goblins", 200);
        CombatantFixture zombie = CreateCombatant("Living Zombie", "Zombies", 100);
        string reportedWinner = string.Empty;
        int completionCalls = 0;
        UnityAction<string> recordWinner = winner =>
        {
            completionCalls++;
            reportedWinner = winner;
        };
        OnCombatEnd.AddListener(recordWinner);
        try
        {
            manager.StartCombat();
            yield return WaitForTurn(player.GameObject);
            yield return CoroutineRunner.Await(
                goblin.Creature.ApplyFinalDamageAsync(
                    goblin.Creature.hp,
                    RuleSource.FromSlug("test-defeated-opposition")
                )
            );
            Assert.That(manager.IsCombatActive, Is.True);
            yield return CoroutineRunner.Await(
                player.Creature.ApplyFinalDamageAsync(
                    player.Creature.hp,
                    RuleSource.FromSlug("test-living-opposition-winner")
                )
            );

            UnityEncounterRulesBridge bridge = GetEncounterBridge();
            Assert.That(completionCalls, Is.EqualTo(1));
            Assert.That(reportedWinner, Is.EqualTo("Zombies"));
            Assert.That(bridge.GetHealth(bridge.GetCreatureId(goblin.Creature)).Current, Is.Zero);
            Assert.That(
                bridge.GetHealth(bridge.GetCreatureId(zombie.Creature)).Current,
                Is.Positive
            );
            Assert.That(
                bridge.Snapshot.Encounters[bridge.EncounterId].Outcome,
                Is.EqualTo(EncounterOutcome.PlayerDefeat)
            );
        }
        finally
        {
            OnCombatEnd.RemoveListener(recordWinner);
        }
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

    private static void PrepareBarbarian(CreatureComponent creature)
    {
        creature.level = 1;
        creature.conMod = 1;
        creature.Build = new CharacterBuild
        {
            ClassName = "Barbarian",
            SubclassName = "Fury Instinct",
            ClassFeatName = "Raging Intimidation",
        };
        creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
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

    private sealed class BlockingFactObserver<TFact> : IFactObserver<TFact>
        where TFact : RuleFact
    {
        private readonly bool failAfterRelease;
        private readonly TaskCompletionSource<bool> started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal BlockingFactObserver(bool failAfterRelease) =>
            this.failAfterRelease = failAfterRelease;

        internal Task Started => started.Task;

        internal void Release() => release.TrySetResult(true);

        public async ValueTask OnFactCommitted(TFact fact, RulesSnapshot snapshot)
        {
            started.TrySetResult(true);
            await release.Task;
            if (failAfterRelease)
                throw new InvalidOperationException("deliberate Rage Fact failure");
        }
    }
}
