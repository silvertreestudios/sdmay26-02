using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Game.AbilityActions;
using Game.Combat.Encounters;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
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
        Create("Stable Action Coroutine Runner").AddComponent<CoroutineRunner>();
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
        player.GameObject.transform.position = new Vector3(1f, 0f, 0f);
        activeEnemy.GameObject.transform.position = new Vector3(2f, 0f, 0f);
        dormantEnemy.GameObject.transform.position = new Vector3(3f, 0f, 0f);
        List<GameObject> synchronousStartRoster = null;
        UnityAction captureStartRoster = () => synchronousStartRoster = manager.GetCombatants();
        OnCombatStart.AddListener(captureStartRoster);

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, activeEnemy.Controller });
            Assert.That(
                synchronousStartRoster,
                Is.EqualTo(new[] { player.GameObject, activeEnemy.GameObject }),
                "Synchronous startup consumers must see only the explicit encounter subset."
            );
            Assert.That(GetStartupCombatants(), Is.Empty);
            yield return WaitForTurn();

            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(
                manager.GetCombatants(),
                Is.EquivalentTo(new[] { player.GameObject, activeEnemy.GameObject })
            );
            Assert.That(
                manager.getPoistions(),
                Is.EqualTo(
                    new[]
                    {
                        player.GameObject.transform.position,
                        activeEnemy.GameObject.transform.position,
                    }
                )
            );
            Assert.That(manager.WhosTurn(), Is.Not.SameAs(dormantEnemy.GameObject));
            Assert.That(dormantEnemy.Controller.StartTurnCount, Is.Zero);
            Assert.That(dormantEnemy.Controller.HasTurnAuthority, Is.False);

            yield return CoroutineRunner.Await(
                activeEnemy.Creature.ApplyFinalDamageAsync(
                    activeEnemy.Creature.hp,
                    RuleSource.FromSlug("test-selected-subset-end")
                )
            );
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The selected-subset encounter did not finish."
            );

            Assert.That(
                manager.GetCombatants(),
                Is.EqualTo(new[] { player.GameObject, dormantEnemy.GameObject }),
                "A closed encounter must restore every living registered exploration actor."
            );
            Assert.That(
                manager.getPoistions(),
                Is.EqualTo(
                    new[]
                    {
                        player.GameObject.transform.position,
                        dormantEnemy.GameObject.transform.position,
                    }
                )
            );
        }
        finally
        {
            OnCombatStart.RemoveListener(captureStartRoster);
        }
    }

    /// <summary>Verifies a failed synchronous startup event cannot retain its selected projection.</summary>
    [UnityTest]
    public IEnumerator StartEventFailureClearsBootstrapRosterAndRestoresRegistrationView()
    {
        CombatantFixture player = CreateCombatant("Failed Start Player", "Players", 200);
        CombatantFixture activeEnemy = CreateCombatant("Failed Start Enemy", "Enemies", 100);
        CombatantFixture dormantEnemy = CreateCombatant("Failed Start Dormant", "Enemies", 0);
        UnityAction failStart = () =>
            throw new InvalidOperationException("deliberate synchronous startup failure");
        OnCombatStart.AddListener(failStart);

        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                manager.StartDungeonCombat(new[] { player.Controller, activeEnemy.Controller })
            );

            Assert.That(error.Message, Does.Contain("deliberate synchronous startup failure"));
            Assert.That(manager.IsCombatActive, Is.False);
            Assert.That(GetStartupCombatants(), Is.Empty);
            Assert.That(GetPublishedActiveCombatants(), Is.Empty);
            Assert.That(
                manager.GetCombatants(),
                Is.EqualTo(
                    new[] { player.GameObject, activeEnemy.GameObject, dormantEnemy.GameObject }
                ),
                "Without an encounter, gameplay must return to the ordinary living registration view."
            );
            Assert.That(
                manager.getPoistions(),
                Is.EqualTo(
                    new[]
                    {
                        player.GameObject.transform.position,
                        activeEnemy.GameObject.transform.position,
                        dormantEnemy.GameObject.transform.position,
                    }
                )
            );
        }
        finally
        {
            OnCombatStart.RemoveListener(failStart);
        }

        manager.StartDungeonCombat(new[] { player.Controller, activeEnemy.Controller });
        yield return WaitForTurn(player.GameObject);
        Assert.That(manager.IsCombatActive, Is.True, "A failed startup must not block a retry.");
    }

    /// <summary>Verifies legacy all-registered combat deterministically supports arbitrary teams.</summary>
    [UnityTest]
    public IEnumerator StartCombatWithoutPlayersTeamUsesFirstSelectedTeamAsProtagonists()
    {
        CombatantFixture teamA = CreateCombatant("Legacy Team A", "TeamA", 200);
        CombatantFixture teamB = CreateCombatant("Legacy Team B", "TeamB", 100);

        manager.StartCombat();
        yield return WaitForTurn(teamA.GameObject);

        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        EncounterState encounter = bridge.Snapshot.Encounters[bridge.EncounterId];
        Assert.That(bridge.GetTeamDisplayName(encounter.ProtagonistTeam), Is.EqualTo("TeamA"));
        Assert.That(
            manager.GetCombatants(),
            Is.EqualTo(new[] { teamA.GameObject, teamB.GameObject })
        );
    }

    /// <summary>Verifies an awaited startup hook fault fully rolls back and permits a restart.</summary>
    [UnityTest]
    public IEnumerator AsynchronousStartHookFailureLeavesCombatInactiveAndRestartable()
    {
        CombatantFixture player = CreateCombatant("Failed Async Start Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Failed Async Start Enemy", "Enemies", 100);
        PrepareBarbarian(player.Creature);
        player.Creature.passives.Add("Zombie-Fist");
        PreparedCharacter preparedBefore = player.Creature.Prepared;
        int actionsBefore = player.Controller.GetActions().Count;
        BlockingFactObserver<TemporaryHitPointsGrantedFact> blocker = new(failAfterRelease: true);
        RuleDispatcher failedDispatcher = null;
        UnityEncounterRulesBridge failedBridge = null;
        UnityAction installFailure = () =>
        {
            failedBridge = GetEncounterBridge();
            failedDispatcher = GetEncounterDispatcher();
            failedDispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        };
        OnCombatStart.AddListener(installFailure);

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The startup hook did not reach its awaited health Fact."
            );
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.True);
            Assert.That(player.Creature.Health.Temporary, Is.GreaterThan(0));
            Assert.That(player.Controller.GetActions().OfType<Unarmed>(), Has.Count.EqualTo(1));

            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate Rage Fact failure")
            );
            blocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The failed asynchronous startup did not roll combat state back."
            );

            Assert.That(GetPublishedActiveCombatants(), Is.Empty);
            Assert.That(GetStartupCombatants(), Is.Empty);
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(player.Creature.Prepared, Is.SameAs(preparedBefore));
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.False);
            Assert.That(player.Creature.Health.Temporary, Is.Zero);
            Assert.That(player.Creature.HasTempHpImmunity("rage"), Is.False);
            Assert.That(player.Controller.GetActions(), Has.Count.EqualTo(actionsBefore));
            Assert.That(GetCreatureEncounterBridge(player.Creature), Is.Null);
            Assert.Throws<InvalidOperationException>(() =>
                failedBridge.GetCreatureId(player.Creature)
            );
            Assert.That(
                manager.GetCombatants(),
                Is.EqualTo(new[] { player.GameObject, enemy.GameObject })
            );
        }
        finally
        {
            OnCombatStart.RemoveListener(installFailure);
            blocker.Release();
            failedDispatcher?.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        }

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(player.Creature.Prepared, Is.SameAs(preparedBefore));
        Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.True);
        Assert.That(player.Creature.Health.Temporary, Is.EqualTo(2));
        Assert.That(player.Controller.GetActions().OfType<Unarmed>(), Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Verifies failed first-turn rules discard every buffered Unity presentation side effect.
    /// </summary>
    [UnityTest]
    public IEnumerator FirstTurnAuraFailureDiscardsStartupPresentationAndRetriesCleanly()
    {
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(singletonField, Is.Not.Null);
        object previousGrid = singletonField.GetValue(null);
        StartupTestGridAPI grid = Create("Startup Transaction Grid")
            .AddComponent<StartupTestGridAPI>();
        singletonField.SetValue(null, grid);

        CombatantFixture player = CreateCombatant("Startup Aura Player", "Players", 200);
        CombatantFixture auraSource = CreateCombatant("Startup Aura Source", "Enemies", 100);
        player.GameObject.transform.position = new Vector3(2f, 0f, 2f);
        auraSource.GameObject.transform.position = new Vector3(3f, 0f, 2f);
        player.Creature.InitializeHealthBeforeEncounter(1, 10);
        auraSource.Creature.auras.Add(
            new CreatureAura
            {
                name = "Rotting Aura",
                slug = RottingAuraRule.RuleSlug,
                radiusFeet = 10,
                traits = new List<string> { "aura", "disease", "void" },
            }
        );
        Token playerToken = player.GameObject.AddComponent<Token>();
        auraSource.GameObject.AddComponent<Token>();
        GameObject colliderOwner = Create("Startup Aura Player Collider");
        colliderOwner.transform.SetParent(player.GameObject.transform);
        BoxCollider targetCollider = colliderOwner.AddComponent<BoxCollider>();
        ConditionSource preservedCondition = new();
        player.Conditions.Add("Off-Guard", preservedCondition);
        SpellEffectController spellEffects = SpellEffectController.GetOrAdd(player.GameObject);
        spellEffects.AddOrRefresh(new ShieldSpellEffect(player.GameObject));

        int deathPresentations = 0;
        int turnPresentations = 0;
        UnityAction<GameObject> observeDeath = defeated =>
        {
            if (defeated == player.GameObject)
                deathPresentations++;
        };
        UnityAction<GameObject> observeTurn = actor =>
        {
            if (actor == player.GameObject)
                turnPresentations++;
        };
        OnDeath.AddListener(observeDeath);
        OnNextTurn.AddListener(observeTurn);

        BlockingFactObserver<DamageAppliedFact> blocker = new(
            failAfterRelease: true,
            "deliberate startup aura observer failure"
        );
        UnityEncounterRulesBridge failedBridge = null;
        RuleDispatcher failedDispatcher = null;
        UnityAction installFailure = () =>
        {
            failedBridge = GetEncounterBridge();
            failedDispatcher = GetEncounterDispatcher();
            failedDispatcher.RegisterFactObserver<DamageAppliedFact>(blocker);
        };
        OnCombatStart.AddListener(installFailure);
        bool firstAttemptCompleted = false;

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, auraSource.Controller });
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "Rotting Aura did not reach the awaited startup health observer."
            );

            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(player.Creature.hp, Is.EqualTo(1));
            Assert.That(player.Creature.IsDefeated, Is.False);
            Assert.That(player.GameObject.activeSelf, Is.True);
            Assert.That(player.Controller.enabled, Is.True);
            Assert.That(targetCollider.enabled, Is.True);
            Assert.That(playerToken.IsRegistered, Is.True);
            Assert.That(grid.Contains(player.GameObject), Is.True);
            Assert.That(grid.DestroyedTokens, Is.Empty);
            Assert.That(spellEffects.HasEffect<ShieldSpellEffect>(), Is.True);
            Assert.That(player.Conditions.Contains("Off-Guard", preservedCondition), Is.True);
            Assert.That(deathPresentations, Is.Zero);
            Assert.That(turnPresentations, Is.Zero);

            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate startup aura observer failure")
            );
            blocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The failed first-turn transaction did not roll startup back."
            );

            Assert.That(player.Creature.hp, Is.EqualTo(1));
            Assert.That(player.Creature.IsDefeated, Is.False);
            Assert.That(player.GameObject.activeSelf, Is.True);
            Assert.That(player.Controller.enabled, Is.True);
            Assert.That(targetCollider.enabled, Is.True);
            Assert.That(playerToken.IsRegistered, Is.True);
            Assert.That(grid.Contains(player.GameObject), Is.True);
            Assert.That(grid.DestroyedTokens, Is.Empty);
            Assert.That(spellEffects.HasEffect<ShieldSpellEffect>(), Is.True);
            Assert.That(player.Conditions.Contains("Off-Guard", preservedCondition), Is.True);
            Assert.That(deathPresentations, Is.Zero);
            Assert.That(turnPresentations, Is.Zero);
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(GetPendingEncounterCompletion(), Is.Null);
            Assert.That(GetCreatureEncounterBridge(player.Creature), Is.Null);
            firstAttemptCompleted = true;
        }
        finally
        {
            blocker.Release();
            OnCombatStart.RemoveListener(installFailure);
            failedDispatcher?.UnregisterFactObserver<DamageAppliedFact>(blocker);
            if (!firstAttemptCompleted)
            {
                OnDeath.RemoveListener(observeDeath);
                OnNextTurn.RemoveListener(observeTurn);
                singletonField.SetValue(null, previousGrid);
            }
        }

        try
        {
            auraSource.Creature.auras.Clear();
            manager.StartDungeonCombat(new[] { player.Controller, auraSource.Controller });
            yield return WaitForTurn(player.GameObject);

            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(player.Creature.hp, Is.EqualTo(1));
            Assert.That(player.Creature.IsDefeated, Is.False);
            Assert.That(player.GameObject.activeSelf, Is.True);
            Assert.That(player.Controller.enabled, Is.True);
            Assert.That(targetCollider.enabled, Is.True);
            Assert.That(playerToken.IsRegistered, Is.True);
            Assert.That(grid.Contains(player.GameObject), Is.True);
            Assert.That(spellEffects.HasEffect<ShieldSpellEffect>(), Is.False);
            Assert.That(player.Conditions.Contains("Off-Guard", preservedCondition), Is.True);
            Assert.That(deathPresentations, Is.Zero);
            Assert.That(turnPresentations, Is.EqualTo(1));
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(GetEncounterBridge(), Is.Not.SameAs(failedBridge));
        }
        finally
        {
            OnDeath.RemoveListener(observeDeath);
            OnNextTurn.RemoveListener(observeTurn);
            singletonField.SetValue(null, previousGrid);
        }
    }

    /// <summary>Verifies a player controller revokes locally owned authority outside encounters.</summary>
    [Test]
    public void PlayerEndTurnClearsStandaloneAuthorityAndActions()
    {
        PlayerActionController player = CreateLifecycleController<PlayerActionController>(
            "Standalone Player",
            "Players",
            100
        );

        player.StartTurn();
        Assert.That(player.HasTurnAuthority, Is.True);
        Assert.That(player.ActionPoints, Is.EqualTo(3));

        player.EndTurn();

        Assert.That(player.HasTurnAuthority, Is.False);
        Assert.That(player.ActionPoints, Is.Zero);
        Assert.That(manager.IsCombatActive, Is.False);
    }

    /// <summary>Verifies an AI controller revokes locally owned authority outside encounters.</summary>
    [Test]
    public void AiEndTurnClearsStandaloneAuthorityAndActions()
    {
        StandaloneTestAIActionController enemy =
            CreateLifecycleController<StandaloneTestAIActionController>(
                "Standalone AI",
                "Enemies",
                100
            );

        enemy.StartTurn();
        Assert.That(enemy.HasTurnAuthority, Is.True);
        Assert.That(enemy.ActionPoints, Is.EqualTo(3));

        enemy.EndTurn();

        Assert.That(enemy.HasTurnAuthority, Is.False);
        Assert.That(enemy.ActionPoints, Is.Zero);
        Assert.That(manager.IsCombatActive, Is.False);
    }

    /// <summary>Verifies attached player and AI turns still close through the encounter reducer.</summary>
    [UnityTest]
    public IEnumerator AttachedPlayerEndTurnAdvancesToAiThroughAuthoritativeLifecycle()
    {
        PlayerActionController player = CreateLifecycleController<PlayerActionController>(
            "Attached Player",
            "Players",
            200
        );
        StandaloneTestAIActionController enemy =
            CreateLifecycleController<StandaloneTestAIActionController>(
                "Attached AI",
                "Enemies",
                100
            );

        manager.StartDungeonCombat(new ActionController[] { player, enemy });
        yield return WaitForCondition(
            () => player.HasTurnAuthority && player.ActionPoints == 3,
            "The authoritative player turn did not begin."
        );

        player.EndTurn();
        yield return WaitForCondition(
            () => enemy.HasTurnAuthority && enemy.ActionPoints == 3,
            "The authoritative lifecycle did not advance to the AI."
        );

        Assert.That(player.HasTurnAuthority, Is.False);
        Assert.That(player.ActionPoints, Is.Zero);
        Assert.That(manager.WhosTurn(), Is.SameAs(enemy.gameObject));
        Assert.That(manager.IsCombatActive, Is.True);
    }

    /// <summary>
    /// Verifies external presentation begins only after startup is durable, so a later callback
    /// fault cannot convert already-presented work into a partially rolled-back encounter.
    /// </summary>
    [UnityTest]
    public IEnumerator StartupPresentationFaultLeavesAcceptedEncounterUsableAndRetryPresentsOnce()
    {
        CombatantFixture player = CreateCombatant("Accepted Presentation Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Accepted Presentation Enemy", "Enemies", 100);
        SpellEffectController spellEffects = SpellEffectController.GetOrAdd(player.GameObject);
        spellEffects.AddOrRefresh(new ShieldSpellEffect(player.GameObject));
        int failedTurnCallbacks = 0;
        int inactivePublications = 0;
        UnityAction<GameObject> failTurnPresentation = actor =>
        {
            if (actor != player.GameObject)
                return;
            failedTurnCallbacks++;
            throw new InvalidOperationException("deliberate accepted startup presentation failure");
        };
        Action<bool> observeActivity = active =>
        {
            if (!active)
                inactivePublications++;
        };
        OnNextTurn.AddListener(failTurnPresentation);
        manager.CombatActivityChanged += observeActivity;

        try
        {
            LogAssert.Expect(
                LogType.Exception,
                new Regex(
                    "InvalidOperationException: deliberate accepted startup presentation failure"
                )
            );
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForCondition(
                () => failedTurnCallbacks == 1,
                "The accepted first-turn presentation callback did not run."
            );

            UnityEncounterRulesBridge acceptedBridge = GetEncounterBridge();
            EncounterState acceptedEncounter = acceptedBridge.Snapshot.Encounters[
                acceptedBridge.EncounterId
            ];
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(acceptedEncounter.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(acceptedEncounter.CurrentTurn.HasValue, Is.True);
            Assert.That(player.Controller.HasTurnAuthority, Is.True);
            Assert.That(player.Controller.ActionPoints, Is.EqualTo(3));
            Assert.That(player.Controller.StartTurnCount, Is.Zero);
            Assert.That(spellEffects.HasEffect<ShieldSpellEffect>(), Is.False);
            Assert.That(inactivePublications, Is.Zero);

            OnNextTurn.RemoveListener(failTurnPresentation);
            manager.SuspendDungeonCombat();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "A post-acceptance presentation fault left startup requests permanently blocked."
            );
            Assert.That(inactivePublications, Is.EqualTo(1));

            int successfulTurnCallbacks = 0;
            UnityAction<GameObject> observeSuccessfulTurn = actor =>
            {
                if (actor == player.GameObject)
                    successfulTurnCallbacks++;
            };
            spellEffects.AddOrRefresh(new ShieldSpellEffect(player.GameObject));
            OnNextTurn.AddListener(observeSuccessfulTurn);
            try
            {
                manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
                yield return WaitForCondition(
                    () => player.Controller.StartTurnCount == 1,
                    "The successful retry did not complete first-turn presentation."
                );

                Assert.That(manager.IsCombatActive, Is.True);
                Assert.That(successfulTurnCallbacks, Is.EqualTo(1));
                Assert.That(spellEffects.HasEffect<ShieldSpellEffect>(), Is.False);
                Assert.That(player.Controller.HasTurnAuthority, Is.True);
                Assert.That(player.Controller.ActionPoints, Is.EqualTo(3));
            }
            finally
            {
                OnNextTurn.RemoveListener(observeSuccessfulTurn);
            }
        }
        finally
        {
            OnNextTurn.RemoveListener(failTurnPresentation);
            manager.CombatActivityChanged -= observeActivity;
        }
    }

    /// <summary>Verifies rollback never recreates an exploration reservation that already settled.</summary>
    [UnityTest]
    public IEnumerator StartupFailureAfterExplorationStrideSettlesLeavesNoReservation()
    {
        CombatantFixture player = CreateCombatant("Settled Startup Stride Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Settled Startup Stride Enemy", "Enemies", 100);
        PrepareBarbarian(player.Creature);
        Stride stride = new(1);
        player.Controller.AddTestMovement(stride);
        player.Controller.SetDungeonExploration(true);
        BlockingFactObserver<TemporaryHitPointsGrantedFact> blocker = new(
            failAfterRelease: true,
            "deliberate settled-Stride startup failure"
        );
        RuleDispatcher dispatcher = null;
        UnityAction installFailure = () =>
        {
            dispatcher = GetEncounterDispatcher();
            dispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        };
        OnCombatStart.AddListener(installFailure);

        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        object previousGrid = singletonField.GetValue(null);
        TestGridAPI grid = Create("Settled Startup Stride Grid").AddComponent<TestGridAPI>();
        bool releaseStride = false;
        IEnumerator StartCombatAndWait(GameObject _)
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            while (!releaseStride)
                yield return null;
        }
        grid.StrideRoutine = StartCombatAndWait;
        singletonField.SetValue(null, grid);

        try
        {
            player.Controller.TakeAction(stride);
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted && grid.StrideCalls == 1,
                "Exploration Stride did not pause the startup transaction."
            );

            releaseStride = true;
            yield return WaitForCondition(
                () => !player.Controller.IsTakingAction,
                "The exploration Stride did not settle before startup rollback."
            );
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate settled-Stride startup failure")
            );
            blocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The failed startup did not restore exploration."
            );

            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(player.Controller.IsInDungeonExploration, Is.True);
            grid.StrideRoutine = null;
            player.Controller.TakeAction(stride);
            yield return WaitForCondition(
                () => grid.StrideCalls == 2 && !player.Controller.IsTakingAction,
                "An ownerless restored reservation blocked the next exploration action."
            );
        }
        finally
        {
            releaseStride = true;
            blocker.Release();
            OnCombatStart.RemoveListener(installFailure);
            dispatcher?.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
            singletonField.SetValue(null, previousGrid);
        }
    }

    /// <summary>Verifies rollback preserves only the still-live exploration action owner.</summary>
    [UnityTest]
    public IEnumerator StartupFailureWhileExplorationStrideIsLivePreservesExactOwner()
    {
        CombatantFixture player = CreateCombatant("Live Startup Stride Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Live Startup Stride Enemy", "Enemies", 100);
        PrepareBarbarian(player.Creature);
        Stride stride = new(1);
        player.Controller.AddTestMovement(stride);
        player.Controller.SetDungeonExploration(true);
        BlockingFactObserver<TemporaryHitPointsGrantedFact> blocker = new(
            failAfterRelease: true,
            "deliberate live-Stride startup failure"
        );
        RuleDispatcher dispatcher = null;
        UnityAction installFailure = () =>
        {
            dispatcher = GetEncounterDispatcher();
            dispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        };
        OnCombatStart.AddListener(installFailure);

        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        object previousGrid = singletonField.GetValue(null);
        TestGridAPI grid = Create("Live Startup Stride Grid").AddComponent<TestGridAPI>();
        bool releaseStride = false;
        IEnumerator StartCombatAndWait(GameObject _)
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            while (!releaseStride)
                yield return null;
        }
        grid.StrideRoutine = StartCombatAndWait;
        singletonField.SetValue(null, grid);

        try
        {
            player.Controller.TakeAction(stride);
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted && grid.StrideCalls == 1,
                "Exploration Stride did not remain live during startup."
            );
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate live-Stride startup failure")
            );
            blocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The failed startup did not restore its still-running Stride."
            );

            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(player.Controller.IsInDungeonExploration, Is.True);
            player.Controller.TakeAction(stride);
            yield return null;
            Assert.That(grid.StrideCalls, Is.EqualTo(1), "A live owner must block a competitor.");

            releaseStride = true;
            yield return WaitForCondition(
                () => !player.Controller.IsTakingAction,
                "The original Stride owner did not release its exact reservation."
            );
            grid.StrideRoutine = null;
            player.Controller.TakeAction(stride);
            yield return WaitForCondition(
                () => grid.StrideCalls == 2 && !player.Controller.IsTakingAction,
                "The original finalizer interfered with the next exploration action."
            );
        }
        finally
        {
            releaseStride = true;
            blocker.Release();
            OnCombatStart.RemoveListener(installFailure);
            dispatcher?.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
            singletonField.SetValue(null, previousGrid);
        }
    }

    /// <summary>Verifies delayed lifecycle requests cannot cross a failed-start generation.</summary>
    [UnityTest]
    public IEnumerator FailedStartupCancelsStaleReinforcementAndSuspensionRequests()
    {
        CombatantFixture player = CreateCombatant("Stale Request Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Stale Request Enemy", "Enemies", 100);
        CombatantFixture reinforcement = CreateCombatant(
            "Stale Request Reinforcement",
            "Enemies",
            50
        );
        PrepareBarbarian(player.Creature);
        BlockingFactObserver<TemporaryHitPointsGrantedFact> blocker = new(
            failAfterRelease: true,
            "deliberate stale-request startup failure"
        );
        RuleDispatcher failedDispatcher = null;
        UnityEncounterRulesBridge failedBridge = null;
        UnityAction installFailure = () =>
        {
            failedBridge = GetEncounterBridge();
            failedDispatcher = GetEncounterDispatcher();
            failedDispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        };
        int inactiveCalls = 0;
        Action<bool> observeActivity = active =>
        {
            if (!active)
                inactiveCalls++;
        };
        OnCombatStart.AddListener(installFailure);
        manager.CombatActivityChanged += observeActivity;

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "Startup did not pause before delayed requests were queued."
            );
            long failedGeneration = GetEncounterGeneration();
            IEnumerator delayedJoin = InvokeManagerRoutine(
                "AddDungeonReinforcementsRoutine",
                new[] { reinforcement.Controller },
                failedBridge,
                failedGeneration
            );
            IEnumerator delayedSuspend = InvokeManagerRoutine(
                "SuspendDungeonCombatRoutine",
                failedBridge,
                failedGeneration
            );
            Assert.That(delayedJoin.MoveNext(), Is.True);
            Assert.That(delayedSuspend.MoveNext(), Is.True);
            manager.AddDungeonReinforcements(new[] { reinforcement.Controller });
            manager.SuspendDungeonCombat();

            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate stale-request startup failure")
            );
            blocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The faulted startup did not release its manager generation."
            );
            Assert.That(inactiveCalls, Is.EqualTo(1));

            OnCombatStart.RemoveListener(installFailure);
            failedDispatcher.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForTurn(player.GameObject);
            UnityEncounterRulesBridge retryBridge = GetEncounterBridge();
            Assert.That(retryBridge, Is.Not.SameAs(failedBridge));
            Assert.That(GetEncounterGeneration(), Is.GreaterThan(failedGeneration));

            Assert.That(delayedJoin.MoveNext(), Is.False);
            Assert.That(delayedSuspend.MoveNext(), Is.False);
            yield return null;
            yield return null;

            EncounterState retry = retryBridge.Snapshot.Encounters[retryBridge.EncounterId];
            Assert.That(retry.Phase, Is.EqualTo(EncounterPhase.Active));
            Assert.That(
                retry.Roster.Select(entry => retryBridge.GetController(entry.Creature)),
                Is.EqualTo(new[] { player.Controller, enemy.Controller })
            );
            Assert.That(
                GetPublishedActiveCombatants(),
                Is.EqualTo(new[] { player.Controller, enemy.Controller })
            );
            Assert.That(GetCreatureEncounterBridge(reinforcement.Creature), Is.Null);
            Assert.That(inactiveCalls, Is.EqualTo(1));
        }
        finally
        {
            blocker.Release();
            OnCombatStart.RemoveListener(installFailure);
            manager.CombatActivityChanged -= observeActivity;
            failedDispatcher?.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        }
    }

    /// <summary>Verifies same-generation startup waits still join and suspend normally.</summary>
    [UnityTest]
    public IEnumerator SameGenerationStartupWaitsExecuteForReinforcementAndSuspension()
    {
        CombatantFixture joinPlayer = CreateCombatant("Waiting Join Player", "Players", 300);
        CombatantFixture joinEnemy = CreateCombatant("Waiting Join Enemy", "Enemies", 200);
        CombatantFixture reinforcement = CreateCombatant(
            "Waiting Join Reinforcement",
            "Enemies",
            100
        );
        PrepareBarbarian(joinPlayer.Creature);
        BlockingFactObserver<TemporaryHitPointsGrantedFact> joinBlocker = new(
            failAfterRelease: false
        );
        RuleDispatcher joinDispatcher = null;
        UnityAction installJoinBlocker = () =>
        {
            joinDispatcher = GetEncounterDispatcher();
            joinDispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(joinBlocker);
        };
        OnCombatStart.AddListener(installJoinBlocker);

        try
        {
            manager.StartDungeonCombat(new[] { joinPlayer.Controller, joinEnemy.Controller });
            yield return WaitForCondition(
                () => joinBlocker.Started.IsCompleted,
                "Same-generation join startup did not pause."
            );
            manager.AddDungeonReinforcements(new[] { reinforcement.Controller });
            joinBlocker.Release();
            yield return WaitForCondition(
                () => GetPublishedActiveCombatants().Contains(reinforcement.Controller),
                "A valid same-generation reinforcement wait did not execute."
            );
            Assert.That(
                GetEncounterBridge()
                    .Snapshot.Encounters[GetEncounterBridge().EncounterId]
                    .Roster.Select(entry => GetEncounterBridge().GetController(entry.Creature)),
                Has.Member(reinforcement.Controller)
            );
        }
        finally
        {
            joinBlocker.Release();
            OnCombatStart.RemoveListener(installJoinBlocker);
            joinDispatcher?.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(joinBlocker);
        }

        manager.SuspendDungeonCombat();
        yield return WaitForCondition(
            () => !manager.IsCombatActive,
            "The joined encounter did not suspend before the second scenario."
        );

        CombatantFixture suspendPlayer = CreateCombatant("Waiting Suspend Player", "Players", 300);
        CombatantFixture suspendEnemy = CreateCombatant("Waiting Suspend Enemy", "Enemies", 200);
        PrepareBarbarian(suspendPlayer.Creature);
        BlockingFactObserver<TemporaryHitPointsGrantedFact> suspendBlocker = new(
            failAfterRelease: false
        );
        RuleDispatcher suspendDispatcher = null;
        UnityEncounterRulesBridge suspendingBridge = null;
        UnityAction installSuspendBlocker = () =>
        {
            suspendingBridge = GetEncounterBridge();
            suspendDispatcher = GetEncounterDispatcher();
            suspendDispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(suspendBlocker);
        };
        OnCombatStart.AddListener(installSuspendBlocker);

        try
        {
            manager.StartDungeonCombat(new[] { suspendPlayer.Controller, suspendEnemy.Controller });
            yield return WaitForCondition(
                () => suspendBlocker.Started.IsCompleted,
                "Same-generation suspension startup did not pause."
            );
            manager.SuspendDungeonCombat();
            suspendBlocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "A valid same-generation suspension wait did not execute."
            );
            Assert.That(
                suspendingBridge.Snapshot.Encounters[suspendingBridge.EncounterId].Phase,
                Is.EqualTo(EncounterPhase.Suspended)
            );
        }
        finally
        {
            suspendBlocker.Release();
            OnCombatStart.RemoveListener(installSuspendBlocker);
            suspendDispatcher?.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(
                suspendBlocker
            );
        }
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

    /// <summary>
    /// Verifies disabled participants retain timing slots without leaking into gameplay projections.
    /// </summary>
    [UnityTest]
    public IEnumerator DisabledRetainedParticipant_IsSkippedAndRejoinsGameplayProjection()
    {
        CombatantFixture current = CreateCombatant("Projection Current", "Players", 300);
        CombatantFixture skipped = CreateCombatant("Projection Disabled", "Enemies", 200);
        CombatantFixture next = CreateCombatant("Projection Next", "Players", 100);
        current.GameObject.transform.position = new Vector3(1f, 0f, 1f);
        skipped.GameObject.transform.position = new Vector3(20f, 0f, 20f);
        next.GameObject.transform.position = new Vector3(2f, 0f, 2f);

        manager.StartDungeonCombat(
            new[] { current.Controller, skipped.Controller, next.Controller }
        );
        yield return WaitForTurn(current.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        CreatureId skippedId = bridge.GetCreatureId(skipped.Creature);
        RecordingFactObserver<TurnEndedFact> ended = new();
        GetEncounterDispatcher().RegisterFactObserver<TurnEndedFact>(ended);

        skipped.Controller.enabled = false;
        Assert.That(
            bridge.Snapshot.Encounters[bridge.EncounterId].Roster.Select(entry => entry.Creature),
            Has.Member(skippedId),
            "Participation changes must not remove immutable initiative slots."
        );
        Assert.That(manager.GetCombatants(), Has.No.Member(skipped.GameObject));
        Assert.That(manager.getPoistions(), Has.No.Member(skipped.GameObject.transform.position));
        Assert.That(
            manager
                .GetCombatants()
                .Where(combatant => combatant.GetComponent<Team>().Name == "Enemies"),
            Is.Empty,
            "The gameplay projection consumed by MindlessController must exclude disabled targets."
        );

        skipped.Controller.enabled = true;
        skipped.GameObject.SetActive(false);
        Assert.That(manager.GetCombatants(), Has.No.Member(skipped.GameObject));
        Assert.That(manager.getPoistions(), Has.No.Member(skipped.GameObject.transform.position));
        skipped.GameObject.SetActive(true);
        skipped.Controller.enabled = false;

        current.Controller.EndTurn();
        yield return WaitForTurn(next.GameObject);

        Assert.That(skipped.Controller.StartTurnCount, Is.Zero);
        Assert.That(
            ended.Facts.Count(fact => fact.Turn.Actor == skippedId),
            Is.EqualTo(1),
            "The disabled actor's exact committed timing boundary must close once."
        );

        skipped.Controller.enabled = true;
        Assert.That(
            manager.GetCombatants(),
            Is.EqualTo(new[] { next.GameObject, current.GameObject, skipped.GameObject }),
            "A living participant must rejoin in deterministic gameplay order when enabled."
        );
        Assert.That(
            manager.getPoistions(),
            Is.EqualTo(
                new[]
                {
                    next.GameObject.transform.position,
                    current.GameObject.transform.position,
                    skipped.GameObject.transform.position,
                }
            )
        );
    }

    /// <summary>Verifies an occupied dispatcher cannot accept duplicate work for a pending turn end.</summary>
    [UnityTest]
    public IEnumerator PendingEndTurn_ReservesActorAndAdvancesExactlyOnce()
    {
        CombatantFixture current = CreateCombatant("Pending End Current", "Players", 200);
        CombatantFixture next = CreateCombatant("Pending End Next", "Enemies", 100);
        CombatantFixture ally = CreateCombatant("Pending End Ally", "Players", 0);
        manager.StartDungeonCombat(new[] { current.Controller, next.Controller, ally.Controller });
        yield return WaitForTurn(current.GameObject);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<HealthFact> blocker = new(failAfterRelease: false);
        RecordingFactObserver<TurnEndedFact> ended = new();
        dispatcher.RegisterFactObserver<HealthFact>(blocker);
        dispatcher.RegisterFactObserver<TurnEndedFact>(ended);
        Task damage = current
            .Creature.ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-pending-end-blocker"))
            .AsTask();
        int competingInvocations = 0;
        TestEntityAction competing = new("Queued After End", 1, () => competingInvocations++);

        try
        {
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The health observer did not occupy the dispatcher root."
            );

            manager.EndCurrentTurn(current.Controller);
            manager.EndCurrentTurn(current.Controller);
            current.Controller.TakeAction(competing);
            yield return null;

            Assert.That(current.Controller.IsTakingAction, Is.True);
            Assert.That(current.Controller.HasTurnAuthority, Is.True);
            Assert.That(current.Controller.ActionPoints, Is.EqualTo(3u));
            Assert.That(competingInvocations, Is.Zero);
            Assert.That(ended.Facts, Is.Empty);

            blocker.Release();
            yield return CoroutineRunner.Await(new ValueTask(damage));
            yield return WaitForTurn(next.GameObject);

            Assert.That(ended.Facts, Has.Count.EqualTo(1));
            Assert.That(competingInvocations, Is.Zero);
            Assert.That(current.Controller.HasTurnAuthority, Is.False);
            Assert.That(current.Controller.ActionPoints, Is.Zero);
            Assert.That(current.Controller.IsTakingAction, Is.False);
            Assert.That(next.Controller.HasTurnAuthority, Is.True);
            Assert.That(next.Controller.ActionPoints, Is.EqualTo(3u));
            Assert.That(next.Controller.IsTakingAction, Is.False);
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<HealthFact>(blocker);
            dispatcher.UnregisterFactObserver<TurnEndedFact>(ended);
        }
    }

    /// <summary>Verifies an attack cannot recreate MAP after a queued exact turn end wins.</summary>
    [UnityTest]
    public IEnumerator QueuedStrikeMapAfterTurnLossRejectsAndReleasesActionReservation()
    {
        CombatantFixture player = CreateCombatant("Stale MAP Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Stale MAP Enemy", "Enemies", 100);
        CombatantFixture ally = CreateCombatant("Stale MAP Ally", "Players", 0);
        player.Creature.attackBonus = 100;
        enemy.Creature.ac = -100;
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        object previousGrid = singletonField.GetValue(null);
        TestGridAPI grid = Create("Stale MAP GridAPI").AddComponent<TestGridAPI>();
        grid.StrikeTarget = enemy.GameObject;
        singletonField.SetValue(null, grid);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller, ally.Controller });
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<LegacyActionsSpentFact> blocker = new(failAfterRelease: false);
        RecordingFactObserver<LegacyActionsSpentFact> spends = new();
        RecordingFactObserver<LegacyMapIncrementedFact> maps = new();
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(blocker);
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
        dispatcher.RegisterFactObserver<LegacyMapIncrementedFact>(maps);
        int enemyHealth = enemy.Creature.hp;
        Task<EncounterAdvanceOutcome> ending = null;

        try
        {
            singletonField.SetValue(null, grid);
            player.Controller.TakeAction(
                new Unarmed(
                    1,
                    new List<Dice> { new Dice(1, 1, "bludgeoning") },
                    new List<DamageValue>()
                )
            );
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The action spend did not occupy the dispatcher before MAP."
            );
            ending = bridge.EndTurn(bridge.CurrentTurn.Value).AsTask();
            LogAssert.Expect(
                LogType.Exception,
                new Regex(
                    "InvalidOperationException: .*The actor does not own an active current turn\\."
                )
            );

            blocker.Release();
            yield return CoroutineRunner.Await(new ValueTask<EncounterAdvanceOutcome>(ending));
            yield return WaitForTurn(enemy.GameObject);
            yield return WaitForCondition(
                () => !player.Controller.IsTakingAction,
                "The rejected MAP left the attack reservation active."
            );

            Assert.That(spends.Facts, Has.Count.EqualTo(1));
            Assert.That(spends.Facts[0].Amount, Is.EqualTo(1));
            Assert.That(maps.Facts, Is.Empty);
            Assert.That(player.Controller.StrikePenalty, Is.Zero);
            Assert.That(player.Controller.HasTurnAuthority, Is.False);
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(enemy.Creature.hp, Is.EqualTo(enemyHealth));
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(blocker);
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(spends);
            dispatcher.UnregisterFactObserver<LegacyMapIncrementedFact>(maps);
            singletonField.SetValue(null, previousGrid);
            _ = ending?.Exception;
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

    /// <summary>Verifies concurrent queued reinforcement groups reserve distinct unpublished identities.</summary>
    [UnityTest]
    public IEnumerator ConcurrentReinforcementJoins_AcceptBothReservedIdentityGroups()
    {
        CombatantFixture current = CreateCombatant("Join Current", "Players", 300);
        CombatantFixture later = CreateCombatant("Join Later", "Enemies", 0);
        CombatantFixture first = CreateCombatant("First Pending Join", "Enemies", 200);
        CombatantFixture second = CreateCombatant("Second Pending Join", "Enemies", 100);
        manager.StartDungeonCombat(new[] { current.Controller, later.Controller });
        yield return WaitForTurn(current.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<TurnEndedFact> blocker = new(failAfterRelease: false);
        dispatcher.RegisterFactObserver<TurnEndedFact>(blocker);
        Task<EncounterAdvanceOutcome> ending = bridge.EndTurn(bridge.CurrentTurn.Value).AsTask();
        TestCombatLog combatLog = UnityEngine.Object.FindFirstObjectByType<TestCombatLog>();

        try
        {
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The turn-end observer did not occupy the dispatcher root."
            );

            manager.AddDungeonReinforcements(new[] { first.Controller });
            manager.AddDungeonReinforcements(new[] { second.Controller });
            yield return null;

            Assert.Throws<InvalidOperationException>(() => bridge.GetCreatureId(first.Controller));
            Assert.Throws<InvalidOperationException>(() => bridge.GetCreatureId(second.Controller));
            Assert.That(manager.GetCombatants(), Has.No.Member(first.GameObject));
            Assert.That(manager.GetCombatants(), Has.No.Member(second.GameObject));

            blocker.Release();
            yield return CoroutineRunner.Await(new ValueTask<EncounterAdvanceOutcome>(ending));
            yield return WaitForCondition(
                () =>
                    manager.GetCombatants().Count == 4
                    && combatLog
                        .GetMessages()
                        .Count(message =>
                            message.StartsWith("Reinforcements:\n", StringComparison.Ordinal)
                        ) == 2,
                "Both queued reinforcement lifecycle groups were not accepted."
            );

            CreatureId firstId = bridge.GetCreatureId(first.Controller);
            CreatureId secondId = bridge.GetCreatureId(second.Controller);
            Assert.That(firstId, Is.Not.EqualTo(secondId));
            Assert.That(firstId.Value, Is.EqualTo("encounter-creature-3"));
            Assert.That(secondId.Value, Is.EqualTo("encounter-creature-4"));
            Assert.That(bridge.GetController(firstId), Is.SameAs(first.Controller));
            Assert.That(bridge.GetController(secondId), Is.SameAs(second.Controller));
            Assert.That(
                bridge
                    .Snapshot.Encounters[bridge.EncounterId]
                    .Roster.Where(entry => entry.Creature == firstId || entry.Creature == secondId)
                    .Select(entry => entry.Creature),
                Is.EqualTo(new[] { firstId, secondId })
            );
            Assert.That(
                manager.GetCombatants(),
                Has.Member(first.GameObject).And.Member(second.GameObject)
            );
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<TurnEndedFact>(blocker);
            _ = ending.Exception;
        }
    }

    /// <summary>Verifies accepted reinforcement publication and initialization share the join root.</summary>
    [UnityTest]
    public IEnumerator QueuedJoinPublishesIdentityAndInitializationBeforeEligibleTurn()
    {
        CombatantFixture current = CreateCombatant("Join Boundary Current", "Players", 300);
        CombatantFixture reinforcement = CreateCombatant(
            "Join Boundary Reinforcement",
            "Enemies",
            200
        );
        CombatantFixture later = CreateCombatant("Join Boundary Later", "Enemies", 0);
        PrepareBarbarian(reinforcement.Creature);
        Assert.That(reinforcement.Creature.Prepared.HasOwnedItem("quick-tempered"), Is.True);
        manager.StartDungeonCombat(new[] { current.Controller, later.Controller });
        yield return WaitForTurn(current.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<HealthFact> rootBlocker = new(failAfterRelease: false);
        BlockingFactObserver<TemporaryHitPointsGrantedFact> initializationBlocker = new(
            failAfterRelease: false
        );
        dispatcher.RegisterFactObserver<HealthFact>(rootBlocker);
        dispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(initializationBlocker);
        Task occupied = current
            .Creature.ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-join-root-blocker"))
            .AsTask();
        Task<EncounterAdvanceOutcome> ending = null;
        int reinforcementTurnPresentations = 0;
        bool initializedAtPresentation = false;
        UnityAction<GameObject> observeTurn = actor =>
        {
            if (actor != reinforcement.GameObject)
                return;
            reinforcementTurnPresentations++;
            initializedAtPresentation =
                reinforcement.Creature.Prepared.HasActiveEffect("rage")
                && reinforcement.Creature.Health.Temporary > 0;
        };
        OnNextTurn.AddListener(observeTurn);

        try
        {
            yield return WaitForCondition(
                () => rootBlocker.Started.IsCompleted,
                "The health root did not occupy reinforcement serialization."
            );
            manager.AddDungeonReinforcements(new[] { reinforcement.Controller });
            ending = bridge.EndTurn(bridge.CurrentTurn.Value).AsTask();
            yield return null;

            Assert.Throws<InvalidOperationException>(() =>
                bridge.GetCreatureId(reinforcement.Controller)
            );
            Assert.That(GetPublishedActiveCombatants(), Has.No.Member(reinforcement.Controller));

            rootBlocker.Release();
            yield return WaitForCondition(
                () => initializationBlocker.Started.IsCompleted,
                "Quick-Tempered initialization did not begin inside the accepted join root."
            );

            CreatureId reinforcementId = bridge.GetCreatureId(reinforcement.Controller);
            Assert.That(
                bridge.GetController(reinforcementId),
                Is.SameAs(reinforcement.Controller),
                "Accepted identity publication must precede any causal initialization dispatch."
            );
            Assert.That(
                bridge.CurrentTurn.Value.Actor,
                Is.EqualTo(bridge.GetCreatureId(current.Controller))
            );
            Assert.That(reinforcement.Controller.StartTurnCount, Is.Zero);
            Assert.That(
                GetPublishedActiveCombatants(),
                Has.Member(reinforcement.Controller),
                "A durably accepted reinforcement must be in host cleanup before hooks await."
            );

            initializationBlocker.Release();
            yield return CoroutineRunner.Await(new ValueTask(occupied));
            yield return CoroutineRunner.Await(new ValueTask<EncounterAdvanceOutcome>(ending));
            yield return WaitForTurn(reinforcement.GameObject);

            Assert.That(reinforcementTurnPresentations, Is.EqualTo(1));
            Assert.That(initializedAtPresentation, Is.True);
            Assert.That(reinforcement.Controller.StartTurnCount, Is.EqualTo(1));
            Assert.That(GetPublishedActiveCombatants(), Has.Member(reinforcement.Controller));
        }
        finally
        {
            rootBlocker.Release();
            initializationBlocker.Release();
            OnNextTurn.RemoveListener(observeTurn);
            dispatcher.UnregisterFactObserver<HealthFact>(rootBlocker);
            dispatcher.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(initializationBlocker);
            _ = ending?.Exception;
        }
    }

    /// <summary>Verifies a faulting join initializer cannot split rules and manager membership.</summary>
    [UnityTest]
    public IEnumerator AcceptedJoinInitializerFailureKeepsRulesAndManagerRosterReconciled()
    {
        CombatantFixture current = CreateCombatant("Faulted Join Current", "Players", 300);
        CombatantFixture reinforcement = CreateCombatant(
            "Faulted Join Reinforcement",
            "Enemies",
            200
        );
        CombatantFixture later = CreateCombatant("Faulted Join Later", "Enemies", 0);
        PrepareBarbarian(reinforcement.Creature);
        manager.StartDungeonCombat(new[] { current.Controller, later.Controller });
        yield return WaitForTurn(current.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<TemporaryHitPointsGrantedFact> blocker = new(failAfterRelease: true);
        dispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        Task<EncounterAdvanceOutcome> ending = null;

        try
        {
            manager.AddDungeonReinforcements(new[] { reinforcement.Controller });
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The accepted reinforcement initializer did not await its health Fact."
            );

            CreatureId reinforcementId = bridge.GetCreatureId(reinforcement.Controller);
            Assert.That(bridge.GetController(reinforcementId), Is.SameAs(reinforcement.Controller));
            Assert.That(
                bridge
                    .Snapshot.Encounters[bridge.EncounterId]
                    .Roster.Select(entry => entry.Creature),
                Has.Member(reinforcementId)
            );
            Assert.That(GetPublishedActiveCombatants(), Has.Member(reinforcement.Controller));
            Assert.That(reinforcement.Controller.StartTurnCount, Is.Zero);

            ending = bridge.EndTurn(bridge.CurrentTurn.Value).AsTask();
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate Rage Fact failure")
            );
            blocker.Release();
            yield return CoroutineRunner.Await(new ValueTask<EncounterAdvanceOutcome>(ending));
            yield return WaitForTurn(reinforcement.GameObject);

            Assert.That(reinforcement.Controller.StartTurnCount, Is.EqualTo(1));
            Assert.That(manager.GetCombatants(), Has.Member(reinforcement.GameObject));

            yield return CoroutineRunner.Await(
                current.Creature.ApplyFinalDamageAsync(
                    current.Creature.hp,
                    RuleSource.FromSlug("test-faulted-join-cleanup")
                )
            );
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "Encounter cleanup did not settle after the faulted accepted join."
            );
            Assert.That(GetPublishedActiveCombatants(), Is.Empty);
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
            _ = ending?.Exception;
        }
    }

    /// <summary>Verifies a completed join continuation cannot restore manager state after defeat.</summary>
    [UnityTest]
    public IEnumerator CombatEndingBehindJoinLeavesPublishedActiveCombatantsCleared()
    {
        CombatantFixture player = CreateCombatant("Ending Join Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Ending Join Enemy", "Enemies", 100);
        CombatantFixture reinforcement = CreateCombatant("Ending Join Reinforcement", "Enemies", 0);
        PrepareBarbarian(reinforcement.Creature);
        player.Creature.InitializeHealthBeforeEncounter(1, 1);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<TemporaryHitPointsGrantedFact> blocker = new(failAfterRelease: false);
        dispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        Task lethal = null;

        try
        {
            manager.AddDungeonReinforcements(new[] { reinforcement.Controller });
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The reinforcement start hook did not hold the accepted join root."
            );
            lethal = player
                .Creature.ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-ending-behind-join"))
                .AsTask();

            blocker.Release();
            yield return CoroutineRunner.Await(new ValueTask(lethal));
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The queued lethal root did not settle the encounter."
            );
            yield return null;
            yield return null;

            Assert.That(GetPublishedActiveCombatants(), Is.Empty);
            Assert.That(manager.IsCombatActive, Is.False);
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
            _ = lethal?.Exception;
        }
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

    /// <summary>Verifies lethal Strike completion waits for post-damage weapon work.</summary>
    [UnityTest]
    public IEnumerator LethalWeaponDefersHostCompletionUntilStrikeReservationSettles()
    {
        CombatantFixture player = CreateCombatant("Deferred Weapon Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Deferred Weapon Enemy", "Enemies", 100);
        PrepareBarbarian(player.Creature);
        player.Creature.attackBonus = 100;
        enemy.Creature.ac = -100;
        enemy.Creature.InitializeHealthBeforeEncounter(1, 1);
        EquipmentWeapon weapon = new()
        {
            name = "Deferred Lifecycle Crossbow",
            reload = "1",
            damage = new Dice(1, 1, "piercing"),
            traits = new List<string>(),
        };
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        object previousGrid = singletonField.GetValue(null);
        TestGridAPI grid = Create("Deferred Weapon GridAPI").AddComponent<TestGridAPI>();
        grid.StrikeTarget = enemy.GameObject;
        singletonField.SetValue(null, grid);
        int inactiveCalls = 0;
        int outcomeCalls = 0;
        bool actionSettledAtInactive = false;
        Action<bool> observeActivity = active =>
        {
            if (active)
                return;
            inactiveCalls++;
            actionSettledAtInactive =
                !player.Controller.IsTakingAction && !player.Creature.IsWeaponLoaded(weapon);
        };
        manager.CombatActivityChanged += observeActivity;
        manager.DungeonCombatEnded += _ => outcomeCalls++;

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        TaskCompletionSource<bool> endStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource<bool> releaseEnd = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Func<EncounterOutcome, ValueTask> blockEnd = async _ =>
        {
            endStarted.TrySetResult(true);
            await releaseEnd.Task;
        };
        SelectedFactFailureObserver<TemporaryHitPointsRemovedFact> cleanupFailure = new(
            fact => fact.Creature == bridge.GetCreatureId(player.Creature),
            "deferred weapon cleanup failure"
        );
        bridge.EncounterEnded += blockEnd;
        dispatcher.RegisterFactObserver<TemporaryHitPointsRemovedFact>(cleanupFailure);
        TestCombatLog log = UnityEngine.Object.FindFirstObjectByType<TestCombatLog>();

        try
        {
            singletonField.SetValue(null, grid);
            player.Controller.TakeAction(new StrikeWeapon(1, weapon, player.GameObject));
            yield return WaitForCondition(
                () => endStarted.Task.IsCompleted,
                "The lethal Strike did not reach committed encounter-end presentation."
            );

            int messagesBeforeAfterDamage = log.GetMessages().Count;
            Assert.That(enemy.Creature.IsDefeated, Is.True);
            Assert.That(enemy.GameObject.activeSelf, Is.False);
            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(player.Creature.IsWeaponLoaded(weapon), Is.True);
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(inactiveCalls, Is.Zero);
            Assert.That(outcomeCalls, Is.Zero);

            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deferred weapon cleanup failure")
            );
            releaseEnd.TrySetResult(true);
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "Host completion did not follow the settled lethal Strike."
            );
            yield return null;

            Assert.That(log.GetMessages().Count, Is.GreaterThan(messagesBeforeAfterDamage));
            Assert.That(player.Creature.IsWeaponLoaded(weapon), Is.False);
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(actionSettledAtInactive, Is.True);
            Assert.That(cleanupFailure.Calls, Is.EqualTo(1));
            Assert.That(inactiveCalls, Is.EqualTo(1));
            Assert.That(outcomeCalls, Is.EqualTo(1));
        }
        finally
        {
            releaseEnd.TrySetResult(true);
            bridge.EncounterEnded -= blockEnd;
            dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(cleanupFailure);
            manager.CombatActivityChanged -= observeActivity;
            singletonField.SetValue(null, previousGrid);
        }
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

    /// <summary>Verifies both Strike paths reject non-roster targets before any resource or roll.</summary>
    [UnityTest]
    public IEnumerator WeaponAndUnarmedStrikeRejectOutsideEncounterBeforeCostsMapOrRolls()
    {
        CombatantFixture player = CreateCombatant("Membership Strike Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Membership Strike Enemy", "Enemies", 100);
        CombatantFixture outsider = CreateCombatant("Membership Strike Outsider", "Enemies", 0);
        FieldInfo singletonField = typeof(SingletonMonoBehaviour<GridAPI>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        object previousGrid = singletonField.GetValue(null);
        TestGridAPI grid = Create("Membership Strike GridAPI").AddComponent<TestGridAPI>();
        grid.StrikeTarget = outsider.GameObject;
        singletonField.SetValue(null, grid);

        try
        {
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForTurn(player.GameObject);
            RuleDispatcher dispatcher = GetEncounterDispatcher();
            RecordingFactObserver<LegacyActionsSpentFact> actions = new();
            RecordingFactObserver<LegacyMapIncrementedFact> maps = new();
            dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(actions);
            dispatcher.RegisterFactObserver<LegacyMapIncrementedFact>(maps);
            int startingHealth = outsider.Creature.hp;
            UnityEngine.Random.State beforeWeapon = UnityEngine.Random.state;
            EquipmentWeapon weapon = new()
            {
                name = "Membership Test Sword",
                damage = new Dice(1, 6, "slashing"),
                traits = new List<string>(),
            };

            singletonField.SetValue(null, grid);
            player.Controller.TakeAction(new StrikeWeapon(1, weapon, player.GameObject));
            yield return WaitForCondition(
                () => !player.Controller.IsTakingAction,
                "The rejected off-roster weapon Strike did not release its reservation."
            );

            Assert.That(player.Controller.ActionPoints, Is.EqualTo(3u));
            Assert.That(player.Controller.StrikePenalty, Is.Zero);
            Assert.That(actions.Facts, Is.Empty);
            Assert.That(maps.Facts, Is.Empty);
            Assert.That(outsider.Creature.hp, Is.EqualTo(startingHealth));
            Assert.That(UnityEngine.Random.state, Is.EqualTo(beforeWeapon));

            UnityEncounterRulesBridge.CreateHealthTestComposition(new[] { outsider.Creature });
            UnityEngine.Random.State beforeUnarmed = UnityEngine.Random.state;
            singletonField.SetValue(null, grid);
            player.Controller.TakeAction(
                new Unarmed(
                    1,
                    new List<Dice> { new Dice(1, 6, "bludgeoning") },
                    new List<DamageValue>()
                )
            );
            yield return WaitForCondition(
                () => !player.Controller.IsTakingAction,
                "The rejected differently attached unarmed Strike did not release its reservation."
            );

            Assert.That(player.Controller.ActionPoints, Is.EqualTo(3u));
            Assert.That(player.Controller.StrikePenalty, Is.Zero);
            Assert.That(actions.Facts, Is.Empty);
            Assert.That(maps.Facts, Is.Empty);
            Assert.That(outsider.Creature.hp, Is.EqualTo(startingHealth));
            Assert.That(UnityEngine.Random.state, Is.EqualTo(beforeUnarmed));
        }
        finally
        {
            singletonField.SetValue(null, previousGrid);
        }
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
        bool castSettledAtCompletion = false;
        Task<CastSpellResult> castTask = null;
        manager.DungeonCombatEnded += _ =>
        {
            completionCalls++;
            castSettledAtCompletion =
                castTask != null
                && castTask.IsCompletedSuccessfully
                && !player.Controller.IsTakingAction
                && UnityEngine
                    .Object.FindFirstObjectByType<TestCombatLog>()
                    .GetMessages()
                    .Any(message => message.Contains(" casts Divine Lance."));
        };
        CoroutineResult<CastSpellResult> cast = new();

        castTask = SpellcastingRuntime
            .CastAsync(player.GameObject, spell, 2, new[] { enemy.GameObject })
            .AsTask();
        yield return CoroutineRunner.Await(new ValueTask<CastSpellResult>(castTask), cast);
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
        Assert.That(castSettledAtCompletion, Is.True);
        Assert.That(player.Controller.IsTakingAction, Is.False);
    }

    /// <summary>Verifies an outer spell action settles success and logging before legacy completion.</summary>
    [UnityTest]
    public IEnumerator LethalCastSpellActionDefersLegacyCompletionUntilOuterFinally()
    {
        CombatantFixture player = CreateCombatant("Deferred Spell Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Deferred Spell Enemy", "Enemies", 100);
        player.Creature.level = 1;
        player.Creature.wisMod = 4;
        player.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        player.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            player.Creature,
            player.Creature.Build
        );
        PreparedSpell spell = player.Creature.Prepared.Spellcasting.GetSpell("light");
        enemy.Creature.InitializeHealthBeforeEncounter(1, 1);
        PausedLethalSpellDefinition definition = new(enemy.GameObject);
        CastSpellAction action = new(spell, 1, definition);
        int competingInvocations = 0;
        TestEntityAction competing = new(
            "Outcome-Pending Competing Action",
            0,
            () => competingInvocations++
        );
        TestCombatLog log = UnityEngine.Object.FindFirstObjectByType<TestCombatLog>();
        int inactiveCalls = 0;
        int legacyEndCalls = 0;
        int legacyOutcomeCalls = 0;
        bool actionSettledAtNotification = false;
        Action<bool> observeActivity = active =>
        {
            if (active)
                return;
            inactiveCalls++;
            actionSettledAtNotification =
                definition.CastBodyFinished
                && !player.Controller.IsTakingAction
                && log.GetMessages().Any(message => message.Contains(" casts " + spell.Name + "."));
        };
        UnityAction<string> observeEnd = _ => legacyEndCalls++;
        UnityAction<bool> observeOutcome = _ => legacyOutcomeCalls++;
        manager.CombatActivityChanged += observeActivity;
        OnCombatEnd.AddListener(observeEnd);
        OnCombatOutcome.AddListener(observeOutcome);

        try
        {
            manager.StartCombat();
            yield return WaitForTurn(player.GameObject);
            player.Controller.TakeAction(action);
            yield return WaitForCondition(
                () => definition.DamageSettled,
                "The lethal spell did not reach its post-damage action boundary."
            );

            Assert.That(enemy.Creature.IsDefeated, Is.True);
            Assert.That(enemy.GameObject.activeSelf, Is.False);
            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(definition.CastBodyFinished, Is.False);
            Assert.That(log.GetMessages(), Has.None.Contains(" casts " + spell.Name + "."));
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(inactiveCalls, Is.Zero);
            Assert.That(legacyEndCalls, Is.Zero);
            Assert.That(legacyOutcomeCalls, Is.Zero);
            Assert.That(
                GetEncounterBridge().Snapshot.Encounters[GetEncounterBridge().EncounterId].Phase,
                Is.EqualTo(EncounterPhase.Ended)
            );
            player.Controller.TakeAction(competing);
            manager.EndCurrentTurn(player.Controller);
            Assert.That(competingInvocations, Is.Zero);
            Assert.That(manager.IsCombatActive, Is.True);

            definition.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "Legacy host completion did not follow the settled spell action."
            );

            Assert.That(definition.CastBodyFinished, Is.True);
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(actionSettledAtNotification, Is.True);
            Assert.That(inactiveCalls, Is.EqualTo(1));
            Assert.That(legacyEndCalls, Is.EqualTo(1));
            Assert.That(legacyOutcomeCalls, Is.EqualTo(1));
            Assert.That(competingInvocations, Is.Zero);
        }
        finally
        {
            definition.Release();
            manager.CombatActivityChanged -= observeActivity;
            OnCombatEnd.RemoveListener(observeEnd);
            OnCombatOutcome.RemoveListener(observeOutcome);
        }
    }

    /// <summary>Verifies self-defeat cannot stop the reservation-owning spell coroutine.</summary>
    [UnityTest]
    public IEnumerator SelfLethalSpellActionSettlesOnStableHostAfterActorDeactivation()
    {
        CombatantFixture player = CreateCombatant("Self-Lethal Spell Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Self-Lethal Spell Enemy", "Enemies", 100);
        player.Creature.InitializeHealthBeforeEncounter(1, 1);
        player.Creature.level = 1;
        player.Creature.wisMod = 4;
        player.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        player.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            player.Creature,
            player.Creature.Build
        );
        PreparedSpell spell = player.Creature.Prepared.Spellcasting.GetSpell("light");
        PausedLethalSpellDefinition definition = new(player.GameObject);
        CastSpellAction action = new(spell, 1, definition);
        TestCombatLog log = UnityEngine.Object.FindFirstObjectByType<TestCombatLog>();
        int inactiveCalls = 0;
        int dungeonEndCalls = 0;
        int outcomeCalls = 0;
        bool settledAtInactive = false;
        Action<bool> observeActivity = active =>
        {
            if (active)
                return;
            inactiveCalls++;
            settledAtInactive =
                definition.CastBodyFinished
                && !player.Controller.IsTakingAction
                && log.GetMessages().Any(message => message.Contains(" casts " + spell.Name + "."));
        };
        Action<EncounterOutcome> observeDungeonEnd = _ => dungeonEndCalls++;
        UnityAction<bool> observeOutcome = _ => outcomeCalls++;
        manager.CombatActivityChanged += observeActivity;
        manager.DungeonCombatEnded += observeDungeonEnd;
        OnCombatOutcome.AddListener(observeOutcome);

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        RecordingFactObserver<LegacyActionsSpentFact> actions = new();
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(actions);

        try
        {
            player.Controller.TakeAction(action);
            yield return WaitForCondition(
                () => definition.DamageSettled,
                "The self-lethal spell did not settle its committed damage."
            );

            Assert.That(player.Creature.IsDefeated, Is.True);
            Assert.That(player.GameObject.activeSelf, Is.False);
            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(definition.CastBodyFinished, Is.False);
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(inactiveCalls, Is.Zero);
            Assert.That(dungeonEndCalls, Is.Zero);
            Assert.That(outcomeCalls, Is.Zero);
            Assert.That(GetPendingEncounterCompletion(), Is.Not.Null);

            definition.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "Self-lethal spell completion remained stranded after actor deactivation."
            );

            Assert.That(definition.CastBodyFinished, Is.True);
            Assert.That(actions.Facts, Has.Count.EqualTo(1));
            Assert.That(actions.Facts[0].Amount, Is.EqualTo(1));
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(settledAtInactive, Is.True);
            Assert.That(inactiveCalls, Is.EqualTo(1));
            Assert.That(dungeonEndCalls, Is.EqualTo(1));
            Assert.That(outcomeCalls, Is.EqualTo(1));
            Assert.That(GetPendingEncounterCompletion(), Is.Null);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<CoroutineRunner>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                ),
                Has.Length.EqualTo(1)
            );
        }
        finally
        {
            definition.Release();
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(actions);
            manager.CombatActivityChanged -= observeActivity;
            manager.DungeonCombatEnded -= observeDungeonEnd;
            OnCombatOutcome.RemoveListener(observeOutcome);
        }
    }

    /// <summary>Verifies a post-defeat action fault remains visible without stranding completion.</summary>
    [UnityTest]
    public IEnumerator SelfLethalSpellFaultReleasesReservationAndFinalizesEncounter()
    {
        CombatantFixture player = CreateCombatant("Faulting Self-Lethal Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Faulting Self-Lethal Enemy", "Enemies", 100);
        player.Creature.InitializeHealthBeforeEncounter(1, 1);
        player.Creature.level = 1;
        player.Creature.wisMod = 4;
        player.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        player.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            player.Creature,
            player.Creature.Build
        );
        PreparedSpell spell = player.Creature.Prepared.Spellcasting.GetSpell("light");
        FaultingSelfLethalSpellDefinition definition = new(player.GameObject);
        CastSpellAction action = new(spell, 1, definition);
        int inactiveCalls = 0;
        int dungeonEndCalls = 0;
        int outcomeCalls = 0;
        Action<bool> observeActivity = active => inactiveCalls += active ? 0 : 1;
        Action<EncounterOutcome> observeDungeonEnd = _ => dungeonEndCalls++;
        UnityAction<bool> observeOutcome = _ => outcomeCalls++;
        manager.CombatActivityChanged += observeActivity;
        manager.DungeonCombatEnded += observeDungeonEnd;
        OnCombatOutcome.AddListener(observeOutcome);

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        RecordingFactObserver<LegacyActionsSpentFact> actions = new();
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(actions);

        try
        {
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate post-defeat spell failure")
            );
            player.Controller.TakeAction(action);
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "A visible post-defeat spell fault stranded encounter completion."
            );

            Assert.That(definition.DamageSettled, Is.True);
            Assert.That(player.Creature.IsDefeated, Is.True);
            Assert.That(player.GameObject.activeSelf, Is.False);
            Assert.That(actions.Facts, Has.Count.EqualTo(1));
            Assert.That(actions.Facts[0].Amount, Is.EqualTo(1));
            Assert.That(player.Controller.IsTakingAction, Is.False);
            Assert.That(inactiveCalls, Is.EqualTo(1));
            Assert.That(dungeonEndCalls, Is.EqualTo(1));
            Assert.That(outcomeCalls, Is.EqualTo(1));
            Assert.That(GetPendingEncounterCompletion(), Is.Null);
            Assert.That(
                UnityEngine.Object.FindFirstObjectByType<TestCombatLog>().GetMessages(),
                Has.None.Contains(" casts " + spell.Name + ".")
            );
        }
        finally
        {
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(actions);
            manager.CombatActivityChanged -= observeActivity;
            manager.DungeonCombatEnded -= observeDungeonEnd;
            OnCombatOutcome.RemoveListener(observeOutcome);
        }
    }

    /// <summary>Verifies direct casts reserve their final slot before an awaited action spend.</summary>
    [UnityTest]
    public IEnumerator ConcurrentDirectCastsSpendOneActionSlotAndEffect()
    {
        CombatantFixture player = CreateCombatant("Concurrent Spell Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Concurrent Spell Enemy", "Enemies", 100);
        player.Creature.level = 1;
        player.Creature.wisMod = 4;
        player.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        player.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            player.Creature,
            player.Creature.Build
        );
        SpellcastingState spellcasting = player.Creature.Prepared.Spellcasting;
        PreparedSpell spell = spellcasting.GetSpell("infuse-vitality");
        SpellSlotPool pool = spellcasting.Pools["rank-1-infuse-vitality"];

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<LegacyActionsSpentFact> blocker = new(failAfterRelease: false);
        RecordingFactObserver<LegacyActionsSpentFact> spends = new();
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(blocker);
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
        Task<CastSpellResult> first = null;

        try
        {
            first = SpellcastingRuntime
                .CastAsync(player.GameObject, spell, 1, new[] { player.GameObject })
                .AsTask();
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The first cast did not pause while its action spend was settling."
            );

            CoroutineResult<CastSpellResult> rejected = new();
            yield return CoroutineRunner.Await(
                SpellcastingRuntime.CastAsync(
                    player.GameObject,
                    spell,
                    1,
                    new[] { player.GameObject }
                ),
                rejected
            );

            Assert.That(rejected.Value.Success, Is.False);
            Assert.That(rejected.Value.Message, Does.Contain("already casting"));
            Assert.That(rejected.Value.Targets, Is.Empty);
            Assert.That(rejected.Value.Rolls, Is.Empty);
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(player.Controller.ActionPoints, Is.EqualTo(2u));
            Assert.That(pool.UsesRemaining, Is.EqualTo(1));
            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(player.GameObject.GetComponent<SpellEffectController>(), Is.Null);

            blocker.Release();
            CoroutineResult<CastSpellResult> accepted = new();
            yield return CoroutineRunner.Await(new ValueTask<CastSpellResult>(first), accepted);

            SpellEffectController effects = player.GameObject.GetComponent<SpellEffectController>();
            Assert.That(accepted.Value.Success, Is.True);
            Assert.That(accepted.Value.Targets, Is.EqualTo(new[] { player.GameObject }));
            Assert.That(accepted.Value.Rolls, Is.Empty);
            Assert.That(spends.Facts, Has.Count.EqualTo(1));
            Assert.That(spends.Facts[0].Amount, Is.EqualTo(1));
            Assert.That(player.Controller.ActionPoints, Is.EqualTo(2u));
            Assert.That(pool.UsesRemaining, Is.Zero);
            Assert.That(effects, Is.Not.Null);
            Assert.That(
                effects.Effects.Count(effect => effect is InfuseVitalitySpellEffect),
                Is.EqualTo(1)
            );
            Assert.That(player.Controller.IsTakingAction, Is.False);
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(blocker);
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(spends);
            _ = first?.Exception;
        }
    }

    /// <summary>Verifies a prepared spell action retains its outer reservation through completion.</summary>
    [UnityTest]
    public IEnumerator CastSpellActionRetainsOuterReservationUntilCoroutineFinally()
    {
        CombatantFixture player = CreateCombatant("Reserved Spell Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Reserved Spell Enemy", "Enemies", 100);
        player.Creature.level = 1;
        player.Creature.wisMod = 4;
        player.Creature.Build = new CharacterBuild { ClassName = "Cleric" };
        player.Creature.Prepared = Pf2eCharacterPreparer.Prepare(
            player.Creature,
            player.Creature.Build
        );
        PreparedSpell spell = player.Creature.Prepared.Spellcasting.GetSpell("light");
        PausedCompletionSpellDefinition definition = new();
        CastSpellAction action = new(spell, 1, definition);
        int competingInvocations = 0;
        TestEntityAction competing = new("Competing Spell Action", 0, () => competingInvocations++);

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        TurnIdentity reservedTurn = GetEncounterBridge().CurrentTurn.Value;
        player.Controller.TakeAction(action);
        yield return WaitForCondition(
            () => definition.CastSettled,
            "The spell action did not reach its post-cast completion boundary."
        );

        Assert.That(player.Controller.IsTakingAction, Is.True);
        Assert.That(player.Controller.ActionPoints, Is.EqualTo(2u));
        player.Controller.TakeAction(competing);
        manager.EndCurrentTurn(player.Controller);
        Assert.That(competingInvocations, Is.Zero);
        Assert.That(GetEncounterBridge().CurrentTurn, Is.EqualTo(reservedTurn));

        definition.ReleaseCompletion();
        yield return WaitForCondition(
            () => definition.OuterSelectionFinishing,
            "The spell selection coroutine did not resume after its cast settled."
        );
        Assert.That(
            player.Controller.IsTakingAction,
            Is.True,
            "Only the outer MultiFrameEntityAction finally may release this reservation."
        );
        player.Controller.TakeAction(competing);
        manager.EndCurrentTurn(player.Controller);
        Assert.That(competingInvocations, Is.Zero);
        Assert.That(GetEncounterBridge().CurrentTurn, Is.EqualTo(reservedTurn));

        yield return WaitForCondition(
            () => !player.Controller.IsTakingAction,
            "The outer spell action did not release its reservation."
        );
        GatedEntityAction later = new("Later Reserved Action");
        player.Controller.TakeAction(later);
        yield return WaitForCondition(
            () => later.Started,
            "A later action could not reserve the actor after spell completion."
        );
        yield return null;
        Assert.That(player.Controller.IsTakingAction, Is.True);
        Assert.That(competingInvocations, Is.Zero);

        later.Release();
        yield return WaitForCondition(
            () => !player.Controller.IsTakingAction,
            "The later action did not release its own reservation."
        );
    }

    /// <summary>Verifies a queued reload cannot publish after an earlier queued turn end wins.</summary>
    [UnityTest]
    public IEnumerator ReloadSpendRejectedAfterTurnEnd_LeavesWeaponUnloadedAndUnlogged()
    {
        CombatantFixture player = CreateCombatant("Rejected Reload Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Rejected Reload Enemy", "Enemies", 100);
        CombatantFixture ally = CreateCombatant("Rejected Reload Ally", "Players", 0);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller, ally.Controller });
        yield return WaitForTurn(player.GameObject);
        EquipmentWeapon weapon = CreateReloadTestWeapon();
        player.Creature.SetAmmoQuantity(weapon.ammo, 2);
        player.Creature.MarkWeaponFired(weapon);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<HealthFact> blocker = new(failAfterRelease: false);
        RecordingFactObserver<LegacyActionsSpentFact> spends = new();
        dispatcher.RegisterFactObserver<HealthFact>(blocker);
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
        Task damage = player
            .Creature.ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-reload-rejection-blocker"))
            .AsTask();
        Task<EncounterAdvanceOutcome> ending = null;
        TestCombatLog combatLog = UnityEngine.Object.FindFirstObjectByType<TestCombatLog>();

        try
        {
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The health observer did not occupy the reload dispatcher root."
            );
            ending = bridge.EndTurn(bridge.CurrentTurn.Value).AsTask();
            player.Controller.TakeAction(new ReloadWeaponAction(1, weapon));
            yield return null;

            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(player.Creature.IsWeaponLoaded(weapon), Is.False);
            Assert.That(player.Controller.ActionPoints, Is.EqualTo(3u));
            Assert.That(spends.Facts, Is.Empty);
            Assert.That(
                combatLog.GetMessages().Count(message => message.Contains("reloads Review Sling")),
                Is.Zero
            );

            LogAssert.Expect(
                LogType.Exception,
                new Regex(
                    "InvalidOperationException: The actor has insufficient authoritative actions\\."
                )
            );
            blocker.Release();
            yield return CoroutineRunner.Await(new ValueTask(damage));
            yield return CoroutineRunner.Await(new ValueTask<EncounterAdvanceOutcome>(ending));
            yield return null;
            yield return null;

            Assert.That(player.Creature.IsWeaponLoaded(weapon), Is.False);
            Assert.That(spends.Facts, Is.Empty);
            Assert.That(
                combatLog.GetMessages().Count(message => message.Contains("reloads Review Sling")),
                Is.Zero
            );
            Assert.That(player.Controller.IsTakingAction, Is.False);
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<HealthFact>(blocker);
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(spends);
            _ = ending?.Exception;
        }
    }

    /// <summary>Verifies a delayed reload publishes once only after its action spend commits.</summary>
    [UnityTest]
    public IEnumerator DelayedReload_SpendsBeforePublishingLoadedState()
    {
        CombatantFixture player = CreateCombatant("Delayed Reload Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Delayed Reload Enemy", "Enemies", 100);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        EquipmentWeapon weapon = CreateReloadTestWeapon();
        player.Creature.SetAmmoQuantity(weapon.ammo, 2);
        player.Creature.MarkWeaponFired(weapon);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<HealthFact> blocker = new(failAfterRelease: false);
        RecordingFactObserver<LegacyActionsSpentFact> spends = new();
        dispatcher.RegisterFactObserver<HealthFact>(blocker);
        dispatcher.RegisterFactObserver<LegacyActionsSpentFact>(spends);
        Task damage = player
            .Creature.ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-reload-success-blocker"))
            .AsTask();
        TestCombatLog combatLog = UnityEngine.Object.FindFirstObjectByType<TestCombatLog>();

        try
        {
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The health observer did not occupy the delayed reload dispatcher root."
            );
            player.Controller.TakeAction(new ReloadWeaponAction(1, weapon));
            yield return null;

            Assert.That(player.Controller.IsTakingAction, Is.True);
            Assert.That(player.Creature.IsWeaponLoaded(weapon), Is.False);
            Assert.That(player.Controller.ActionPoints, Is.EqualTo(3u));
            Assert.That(spends.Facts, Is.Empty);

            blocker.Release();
            yield return CoroutineRunner.Await(new ValueTask(damage));
            yield return WaitForCondition(
                () => player.Creature.IsWeaponLoaded(weapon) && !player.Controller.IsTakingAction,
                "The accepted delayed reload did not settle."
            );

            Assert.That(player.Controller.ActionPoints, Is.EqualTo(2u));
            Assert.That(spends.Facts, Has.Count.EqualTo(1));
            Assert.That(spends.Facts[0].Amount, Is.EqualTo(1));
            Assert.That(
                combatLog.GetMessages().Count(message => message.Contains("reloads Review Sling")),
                Is.EqualTo(1)
            );
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<HealthFact>(blocker);
            dispatcher.UnregisterFactObserver<LegacyActionsSpentFact>(spends);
        }
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
        int hostOutcomeCalls = 0;
        bool castAndTargetsSettledAtHostOutcome = false;
        Task<CastSpellResult> castTask = null;
        manager.DungeonCombatEnded += _ =>
        {
            hostOutcomeCalls++;
            castAndTargetsSettledAtHostOutcome =
                castTask != null
                && castTask.IsCompletedSuccessfully
                && !player.Controller.IsTakingAction
                && bridge.Snapshot.Health[playerId].Current == 0
                && bridge.Snapshot.Health[enemyId].Current == 0;
        };
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
            castTask = SpellcastingRuntime
                .CastAsync(
                    player.GameObject,
                    hymn,
                    2,
                    Array.Empty<GameObject>(),
                    area,
                    spendActions: true
                )
                .AsTask();
            yield return CoroutineRunner.Await(new ValueTask<CastSpellResult>(castTask), completed);
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "Multi-target host completion did not follow the settled cast."
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
        Assert.That(hostOutcomeCalls, Is.EqualTo(1));
        Assert.That(castAndTargetsSettledAtHostOutcome, Is.True);
        Assert.That(playerHealthAtOutcome, Is.Zero);
        Assert.That(enemyHealthAtOutcome, Is.Zero);
        Assert.That(presentedOutcome, Is.EqualTo(EncounterOutcome.PlayerDefeat));
        Assert.That(
            bridge.Snapshot.Encounters[bridge.EncounterId].Outcome,
            Is.EqualTo(EncounterOutcome.PlayerDefeat)
        );
        Assert.That(Enum.GetNames(typeof(EncounterOutcome)), Does.Not.Contain("Draw"));
    }

    /// <summary>Verifies encounter-end host finalization survives a cleanup observer fault.</summary>
    [UnityTest]
    public IEnumerator EncounterEndCleanupFailureStillFinalizesHostAndAllowsRestart()
    {
        CombatantFixture player = CreateCombatant("Faulted End Cleanup Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Faulted End Cleanup Enemy", "Enemies", 100);
        CombatantFixture dormant = CreateCombatant("Faulted End Cleanup Dormant", "Enemies", -100);
        PrepareBarbarian(player.Creature);
        int inactiveEvents = 0;
        int dungeonEndCalls = 0;
        int throwingNotificationCalls = 0;
        EncounterOutcome? publishedOutcome = null;
        Action<bool> observeActivity = active =>
        {
            if (!active)
                inactiveEvents++;
        };
        Action<EncounterOutcome> recordOutcome = outcome =>
        {
            dungeonEndCalls++;
            publishedOutcome = outcome;
        };
        Action<EncounterOutcome> failOutcomeNotification = _ =>
        {
            throwingNotificationCalls++;
            throw new InvalidOperationException("deliberate outcome notification failure");
        };
        manager.CombatActivityChanged += observeActivity;
        manager.DungeonCombatEnded += recordOutcome;
        manager.DungeonCombatEnded += failOutcomeNotification;
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<TemporaryHitPointsRemovedFact> blocker = new(failAfterRelease: true);
        dispatcher.RegisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
        bool observerRegistered = true;
        Task lethal = null;

        try
        {
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.True);
            Assert.That(player.Creature.Health.Temporary, Is.GreaterThan(0));
            lethal = enemy
                .Creature.ApplyFinalDamageAsync(
                    enemy.Creature.hp,
                    RuleSource.FromSlug("test-faulted-end-cleanup")
                )
                .AsTask();
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "Encounter-end Rage cleanup did not reach its temporary-HP Fact."
            );

            Assert.That(
                bridge.Snapshot.Encounters[bridge.EncounterId].Phase,
                Is.EqualTo(EncounterPhase.Ended)
            );
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(inactiveEvents, Is.Zero);

            blocker.Release();
            yield return WaitForCondition(
                () => lethal.IsCompleted,
                "The faulted encounter-end cleanup did not settle."
            );
            Assert.That(lethal.IsFaulted, Is.True);
            Exception[] failures = lethal.Exception.Flatten().InnerExceptions.ToArray();
            Assert.That(failures, Has.Length.EqualTo(2));
            Assert.That(
                failures[0].Message,
                Does.Contain("deliberate Rage Fact failure"),
                "The cleanup fault must remain the primary reported failure."
            );
            Assert.That(
                failures[1].Message,
                Does.Contain("deliberate outcome notification failure")
            );
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The committed encounter end did not finalize its Unity host after cleanup failed."
            );

            Assert.That(inactiveEvents, Is.EqualTo(1));
            Assert.That(dungeonEndCalls, Is.EqualTo(1));
            Assert.That(throwingNotificationCalls, Is.EqualTo(1));
            Assert.That(publishedOutcome, Is.EqualTo(EncounterOutcome.PlayerVictory));
            Assert.That(manager.WhosTurn(), Is.Null);
            Assert.That(GetPublishedActiveCombatants(), Is.Empty);
            AssertTransientTurnStateCleared(player.Controller);
            AssertTransientTurnStateCleared(enemy.Controller);
            Assert.That(player.Creature.Health.Temporary, Is.Zero);
            Assert.That(
                manager.GetCombatants(),
                Is.EqualTo(new[] { player.GameObject, dormant.GameObject })
            );

            dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
            observerRegistered = false;
            manager.StartDungeonCombat(new[] { player.Controller, dormant.Controller });
            yield return WaitForTurn(player.GameObject);
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(inactiveEvents, Is.EqualTo(1));
        }
        finally
        {
            blocker.Release();
            if (observerRegistered)
                dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
            manager.CombatActivityChanged -= observeActivity;
            manager.DungeonCombatEnded -= recordOutcome;
            manager.DungeonCombatEnded -= failOutcomeNotification;
            _ = lethal?.Exception;
        }
    }

    /// <summary>Verifies legacy outcome channels survive a committed cleanup failure.</summary>
    [UnityTest]
    public IEnumerator LegacyEndCleanupFailureStillPublishesWinnerAndAllowsRestart()
    {
        CombatantFixture player = CreateCombatant("Legacy Fault Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Legacy Fault Enemy", "Enemies", 100);
        PrepareBarbarian(player.Creature);
        int inactiveEvents = 0;
        int dungeonEndCalls = 0;
        int legacyEndCalls = 0;
        int legacyOutcomeCalls = 0;
        string winner = string.Empty;
        bool? playersWon = null;
        Action<bool> observeActivity = active =>
        {
            if (!active)
                inactiveEvents++;
        };
        Action<EncounterOutcome> observeDungeonEnd = _ => dungeonEndCalls++;
        UnityAction<string> observeLegacyEnd = value =>
        {
            legacyEndCalls++;
            winner = value;
        };
        UnityAction<bool> observeLegacyOutcome = value =>
        {
            legacyOutcomeCalls++;
            playersWon = value;
        };
        manager.CombatActivityChanged += observeActivity;
        manager.DungeonCombatEnded += observeDungeonEnd;
        OnCombatEnd.AddListener(observeLegacyEnd);
        OnCombatOutcome.AddListener(observeLegacyOutcome);
        manager.StartCombat();
        yield return WaitForTurn(player.GameObject);
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<TemporaryHitPointsRemovedFact> blocker = new(failAfterRelease: true);
        dispatcher.RegisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
        bool observerRegistered = true;
        Task lethal = null;

        try
        {
            lethal = enemy
                .Creature.ApplyFinalDamageAsync(
                    enemy.Creature.hp,
                    RuleSource.FromSlug("test-legacy-faulted-end-cleanup")
                )
                .AsTask();
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "Legacy cleanup did not reach its temporary-HP Fact."
            );
            blocker.Release();
            yield return WaitForCondition(
                () => lethal.IsCompleted,
                "The legacy cleanup failure did not settle."
            );

            Assert.That(lethal.IsFaulted, Is.True);
            StringAssert.Contains("deliberate Rage Fact failure", lethal.Exception.ToString());
            Assert.That(manager.IsCombatActive, Is.False);
            Assert.That(inactiveEvents, Is.EqualTo(1));
            Assert.That(dungeonEndCalls, Is.Zero);
            Assert.That(legacyEndCalls, Is.EqualTo(1));
            Assert.That(winner, Is.EqualTo("Players"));
            Assert.That(legacyOutcomeCalls, Is.EqualTo(1));
            Assert.That(playersWon, Is.True);

            dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
            observerRegistered = false;
            CombatantFixture retryEnemy = CreateCombatant(
                "Legacy Fault Retry Enemy",
                "Enemies",
                100
            );
            manager.StartCombat();
            yield return WaitForTurn(player.GameObject);
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(manager.GetCombatants(), Has.Member(retryEnemy.GameObject));
            Assert.That(inactiveEvents, Is.EqualTo(1));
            Assert.That(legacyEndCalls, Is.EqualTo(1));
            Assert.That(legacyOutcomeCalls, Is.EqualTo(1));
        }
        finally
        {
            blocker.Release();
            if (observerRegistered)
                dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
            manager.CombatActivityChanged -= observeActivity;
            manager.DungeonCombatEnded -= observeDungeonEnd;
            OnCombatEnd.RemoveListener(observeLegacyEnd);
            OnCombatOutcome.RemoveListener(observeLegacyOutcome);
            _ = lethal?.Exception;
        }
    }

    /// <summary>Verifies suspension finalizes its host when post-commit cleanup faults.</summary>
    [UnityTest]
    public IEnumerator SuspensionCleanupFailureStillFinalizesHostAndAllowsRestart()
    {
        CombatantFixture player = CreateCombatant(
            "Faulted Suspension Cleanup Player",
            "Players",
            200
        );
        CombatantFixture enemy = CreateCombatant(
            "Faulted Suspension Cleanup Enemy",
            "Enemies",
            100
        );
        CombatantFixture dormant = CreateCombatant(
            "Faulted Suspension Cleanup Dormant",
            "Enemies",
            -100
        );
        PrepareBarbarian(player.Creature);
        int inactiveEvents = 0;
        Action<bool> observeActivity = active =>
        {
            if (!active)
                inactiveEvents++;
        };
        manager.CombatActivityChanged += observeActivity;
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<TemporaryHitPointsRemovedFact> blocker = new(failAfterRelease: true);
        dispatcher.RegisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
        bool observerRegistered = true;

        try
        {
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.True);
            Assert.That(player.Creature.Health.Temporary, Is.GreaterThan(0));
            player.Controller.IsTakingAction = true;
            enemy.Controller.IsTakingAction = true;
            manager.SuspendDungeonCombat();
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "Suspension Rage cleanup did not reach its temporary-HP Fact."
            );

            Assert.That(
                bridge.Snapshot.Encounters[bridge.EncounterId].Phase,
                Is.EqualTo(EncounterPhase.Suspended)
            );
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(inactiveEvents, Is.Zero);

            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate Rage Fact failure")
            );
            blocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "The committed suspension did not finalize its Unity host after cleanup failed."
            );
            yield return null;

            Assert.That(inactiveEvents, Is.EqualTo(1));
            Assert.That(manager.WhosTurn(), Is.Null);
            Assert.That(GetPublishedActiveCombatants(), Is.Empty);
            AssertTransientTurnStateCleared(player.Controller);
            AssertTransientTurnStateCleared(enemy.Controller);
            Assert.That(player.Creature.Health.Temporary, Is.Zero);
            Assert.That(
                manager.GetCombatants(),
                Is.EqualTo(new[] { player.GameObject, enemy.GameObject, dormant.GameObject })
            );

            dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
            observerRegistered = false;
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForTurn(player.GameObject);
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(inactiveEvents, Is.EqualTo(1));
        }
        finally
        {
            blocker.Release();
            if (observerRegistered)
                dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(blocker);
            manager.CombatActivityChanged -= observeActivity;
        }
    }

    /// <summary>Verifies suspension cleanup includes a reinforcement accepted ahead of it.</summary>
    [UnityTest]
    public IEnumerator QueuedJoinBeforeSuspensionCleansAcceptedQuickTemperedReinforcement()
    {
        CombatantFixture player = CreateCombatant("Queued Suspension Player", "Players", 300);
        CombatantFixture enemy = CreateCombatant("Queued Suspension Enemy", "Enemies", 100);
        CombatantFixture reinforcement = CreateCombatant(
            "Queued Suspension Reinforcement",
            "Enemies",
            200
        );
        PrepareBarbarian(reinforcement.Creature);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<HealthFact> blocker = new(failAfterRelease: false);
        dispatcher.RegisterFactObserver<HealthFact>(blocker);
        Task occupied = player
            .Creature.ApplyFinalDamageAsync(
                1,
                RuleSource.FromSlug("test-queued-join-suspension-blocker")
            )
            .AsTask();

        try
        {
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The dispatcher root did not pause queued join and suspension work."
            );
            manager.AddDungeonReinforcements(new[] { reinforcement.Controller });
            manager.SuspendDungeonCombat();
            yield return null;

            Assert.Throws<InvalidOperationException>(() =>
                bridge.GetCreatureId(reinforcement.Controller)
            );
            Assert.That(GetPublishedActiveCombatants(), Has.No.Member(reinforcement.Controller));

            blocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "Suspension did not settle after the preceding reinforcement join."
            );
            yield return CoroutineRunner.Await(new ValueTask(occupied));

            EncounterState suspended = bridge.Snapshot.Encounters[bridge.EncounterId];
            Assert.That(suspended.Phase, Is.EqualTo(EncounterPhase.Suspended));
            Assert.That(
                suspended.Roster.Select(entry => bridge.GetController(entry.Creature)),
                Is.EqualTo(new[] { player.Controller, reinforcement.Controller, enemy.Controller })
            );
            Assert.That(
                bridge.GetController(bridge.GetCreatureId(reinforcement.Controller)),
                Is.SameAs(reinforcement.Controller)
            );
            Assert.That(reinforcement.Creature.Prepared.HasActiveEffect("rage"), Is.False);
            Assert.That(reinforcement.Creature.Health.Temporary, Is.Zero);
            Assert.That(reinforcement.Creature.HasTempHpImmunity("rage"), Is.True);
            AssertTransientTurnStateCleared(reinforcement.Controller);
            Assert.That(GetPublishedActiveCombatants(), Is.Empty);
            Assert.That(
                manager.GetCombatants(),
                Is.EqualTo(new[] { player.GameObject, enemy.GameObject, reinforcement.GameObject })
            );
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<HealthFact>(blocker);
            _ = occupied.Exception;
        }
    }

    /// <summary>Verifies a join rejected behind suspension is neither published nor cleaned.</summary>
    [UnityTest]
    public IEnumerator QueuedJoinRejectedAfterSuspensionPublishesNoController()
    {
        CombatantFixture player = CreateCombatant("Rejected Suspension Player", "Players", 300);
        CombatantFixture enemy = CreateCombatant("Rejected Suspension Enemy", "Enemies", 100);
        CombatantFixture rejected = CreateCombatant(
            "Rejected Suspension Reinforcement",
            "Enemies",
            200
        );
        PrepareBarbarian(rejected.Creature);
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        BlockingFactObserver<HealthFact> blocker = new(failAfterRelease: false);
        dispatcher.RegisterFactObserver<HealthFact>(blocker);
        Task occupied = player
            .Creature.ApplyFinalDamageAsync(
                1,
                RuleSource.FromSlug("test-rejected-join-suspension-blocker")
            )
            .AsTask();
        Task suspension = null;
        Task<EncounterJoinOutcome> rejectedJoin = null;

        try
        {
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "The dispatcher root did not pause the rejected join scenario."
            );
            suspension = CompleteDungeonSuspensionAsync(bridge);
            rejectedJoin = bridge.JoinEncounter(new[] { rejected.Controller }).AsTask();
            blocker.Release();
            yield return WaitForCondition(
                () => suspension.IsCompleted && rejectedJoin.IsCompleted,
                "Queued suspension and rejected join did not settle."
            );
            yield return CoroutineRunner.Await(new ValueTask(occupied));

            Assert.That(suspension.IsCompletedSuccessfully, Is.True);
            Assert.That(rejectedJoin.IsFaulted, Is.True);
            Assert.That(
                bridge.Snapshot.Encounters[bridge.EncounterId].Phase,
                Is.EqualTo(EncounterPhase.Suspended)
            );
            Assert.That(
                bridge
                    .Snapshot.Encounters[bridge.EncounterId]
                    .Roster.Select(entry => bridge.GetController(entry.Creature)),
                Is.EqualTo(new[] { player.Controller, enemy.Controller })
            );
            Assert.Throws<InvalidOperationException>(() =>
                bridge.GetCreatureId(rejected.Controller)
            );
            Assert.That(GetCreatureEncounterBridge(rejected.Creature), Is.Null);
            Assert.That(rejected.Creature.Prepared.HasActiveEffect("rage"), Is.False);
            Assert.That(rejected.Creature.Health.Temporary, Is.Zero);
            Assert.That(GetPublishedActiveCombatants(), Is.Empty);
        }
        finally
        {
            blocker.Release();
            dispatcher.UnregisterFactObserver<HealthFact>(blocker);
            _ = occupied.Exception;
            _ = suspension?.Exception;
            _ = rejectedJoin?.Exception;
        }
    }

    /// <summary>Verifies a committed suspension fault still runs complete ordered cleanup.</summary>
    [UnityTest]
    public IEnumerator PostCommitSuspensionFaultStillCleansAndPreservesPrimaryFailure()
    {
        CombatantFixture player = CreateCombatant("Post-Commit Suspension Player", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Post-Commit Suspension Enemy", "Enemies", 100);
        PrepareBarbarian(player.Creature);
        int inactiveEvents = 0;
        Action<bool> observeActivity = active =>
        {
            if (!active)
                inactiveEvents++;
        };
        manager.CombatActivityChanged += observeActivity;
        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        CreatureId playerId = bridge.GetCreatureId(player.Creature);
        BlockingFactObserver<EncounterSuspendedFact> suspensionFailure = new(
            failAfterRelease: true,
            "deliberate suspension settlement failure"
        );
        SelectedFactFailureObserver<TemporaryHitPointsRemovedFact> cleanupFailure = new(
            fact => fact.Creature == playerId,
            "deliberate suspension cleanup failure"
        );
        dispatcher.RegisterFactObserver<EncounterSuspendedFact>(suspensionFailure);
        dispatcher.RegisterFactObserver<TemporaryHitPointsRemovedFact>(cleanupFailure);
        Task suspension = CompleteDungeonSuspensionAsync(bridge);

        try
        {
            yield return WaitForCondition(
                () => suspensionFailure.Started.IsCompleted,
                "The suspension did not reach its post-commit Fact observer."
            );
            Assert.That(
                bridge.Snapshot.Encounters[bridge.EncounterId].Phase,
                Is.EqualTo(EncounterPhase.Suspended)
            );
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.True);
            Assert.That(player.Creature.Health.Temporary, Is.GreaterThan(0));

            suspensionFailure.Release();
            yield return WaitForCondition(
                () => suspension.IsCompleted,
                "The post-commit suspension failure did not settle cleanup."
            );

            Assert.That(suspension.IsFaulted, Is.True);
            Exception[] failures = suspension.Exception.Flatten().InnerExceptions.ToArray();
            Assert.That(failures, Has.Length.EqualTo(2));
            Assert.That(
                failures[0].Message,
                Does.Contain("deliberate suspension settlement failure")
            );
            Assert.That(failures[1].Message, Does.Contain("deliberate suspension cleanup failure"));
            Assert.That(cleanupFailure.Calls, Is.EqualTo(1));
            Assert.That(manager.IsCombatActive, Is.False);
            Assert.That(inactiveEvents, Is.EqualTo(1));
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.False);
            Assert.That(player.Creature.Health.Temporary, Is.Zero);
            Assert.That(player.Creature.HasTempHpImmunity("rage"), Is.True);
            AssertTransientTurnStateCleared(player.Controller);
            AssertTransientTurnStateCleared(enemy.Controller);

            dispatcher.UnregisterFactObserver<EncounterSuspendedFact>(suspensionFailure);
            dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(cleanupFailure);
            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForTurn(player.GameObject);
            Assert.That(manager.IsCombatActive, Is.True);
            Assert.That(player.Creature.Prepared.HasActiveEffect("rage"), Is.True);
            Assert.That(
                player.Creature.Prepared.ActiveEffects.Count(effect => effect.SourceSlug == "rage"),
                Is.EqualTo(1)
            );
            Assert.That(player.Creature.Health.Temporary, Is.Zero);
            Assert.That(inactiveEvents, Is.EqualTo(1));
        }
        finally
        {
            suspensionFailure.Release();
            dispatcher.UnregisterFactObserver<EncounterSuspendedFact>(suspensionFailure);
            dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(cleanupFailure);
            manager.CombatActivityChanged -= observeActivity;
            _ = suspension.Exception;
        }
    }

    /// <summary>Verifies one participant's cleanup fault cannot strand later participants.</summary>
    [UnityTest]
    public IEnumerator EndCleanupAttemptsEveryRagingParticipantInStableOrder()
    {
        CombatantFixture first = CreateCombatant("First Cleanup Barbarian", "Players", 300);
        CombatantFixture second = CreateCombatant("Second Cleanup Barbarian", "Players", 200);
        CombatantFixture enemy = CreateCombatant("Multi-Cleanup Enemy", "Enemies", 100);
        PrepareBarbarian(first.Creature);
        PrepareBarbarian(second.Creature);
        int inactiveEvents = 0;
        int outcomeCalls = 0;
        EncounterOutcome? publishedOutcome = null;
        Action<bool> observeActivity = active =>
        {
            if (!active)
                inactiveEvents++;
        };
        Action<EncounterOutcome> observeOutcome = outcome =>
        {
            outcomeCalls++;
            publishedOutcome = outcome;
        };
        manager.CombatActivityChanged += observeActivity;
        manager.DungeonCombatEnded += observeOutcome;
        manager.StartDungeonCombat(new[] { first.Controller, second.Controller, enemy.Controller });
        yield return WaitForTurn(first.GameObject);
        UnityEncounterRulesBridge bridge = GetEncounterBridge();
        RuleDispatcher dispatcher = GetEncounterDispatcher();
        CreatureId firstId = bridge.GetCreatureId(first.Creature);
        CreatureId secondId = bridge.GetCreatureId(second.Creature);
        SelectedFactFailureObserver<TemporaryHitPointsRemovedFact> firstFailure = new(
            fact => fact.Creature == firstId,
            "first participant cleanup failure"
        );
        SelectedFactFailureObserver<TemporaryHitPointImmunityAddedFact> secondFailure = new(
            fact => fact.Creature == secondId,
            "second participant cleanup failure"
        );
        dispatcher.RegisterFactObserver<TemporaryHitPointsRemovedFact>(firstFailure);
        dispatcher.RegisterFactObserver<TemporaryHitPointImmunityAddedFact>(secondFailure);
        Task lethal = null;

        try
        {
            Assert.That(first.Creature.Health.Temporary, Is.GreaterThan(0));
            Assert.That(second.Creature.Health.Temporary, Is.GreaterThan(0));
            lethal = enemy
                .Creature.ApplyFinalDamageAsync(
                    enemy.Creature.hp,
                    RuleSource.FromSlug("test-multiple-cleanup-failures")
                )
                .AsTask();
            yield return WaitForCondition(
                () => lethal.IsCompleted,
                "The multi-participant cleanup did not settle."
            );

            Assert.That(lethal.IsFaulted, Is.True);
            Exception[] failures = lethal.Exception.Flatten().InnerExceptions.ToArray();
            Assert.That(failures, Has.Length.EqualTo(2));
            Assert.That(failures[0].Message, Does.Contain("first participant cleanup failure"));
            Assert.That(failures[1].Message, Does.Contain("second participant cleanup failure"));
            Assert.That(firstFailure.Calls, Is.EqualTo(1));
            Assert.That(secondFailure.Calls, Is.EqualTo(1));
            Assert.That(manager.IsCombatActive, Is.False);
            Assert.That(inactiveEvents, Is.EqualTo(1));
            Assert.That(outcomeCalls, Is.EqualTo(1));
            Assert.That(publishedOutcome, Is.EqualTo(EncounterOutcome.PlayerVictory));

            foreach (CombatantFixture barbarian in new[] { first, second })
            {
                Assert.That(barbarian.Creature.Prepared.HasActiveEffect("rage"), Is.False);
                Assert.That(barbarian.Creature.Health.Temporary, Is.Zero);
                Assert.That(barbarian.Creature.HasTempHpImmunity("rage"), Is.True);
            }

            dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(firstFailure);
            dispatcher.UnregisterFactObserver<TemporaryHitPointImmunityAddedFact>(secondFailure);
            CombatantFixture retryEnemy = CreateCombatant(
                "Multi-Cleanup Retry Enemy",
                "Enemies",
                100
            );
            manager.StartDungeonCombat(
                new[] { first.Controller, second.Controller, retryEnemy.Controller }
            );
            yield return WaitForTurn(first.GameObject);
            foreach (CombatantFixture barbarian in new[] { first, second })
            {
                Assert.That(barbarian.Creature.Prepared.HasActiveEffect("rage"), Is.True);
                Assert.That(
                    barbarian.Creature.Prepared.ActiveEffects.Count(effect =>
                        effect.SourceSlug == "rage"
                    ),
                    Is.EqualTo(1)
                );
                Assert.That(barbarian.Creature.Health.Temporary, Is.Zero);
            }
            Assert.That(inactiveEvents, Is.EqualTo(1));
            Assert.That(outcomeCalls, Is.EqualTo(1));
        }
        finally
        {
            dispatcher.UnregisterFactObserver<TemporaryHitPointsRemovedFact>(firstFailure);
            dispatcher.UnregisterFactObserver<TemporaryHitPointImmunityAddedFact>(secondFailure);
            manager.CombatActivityChanged -= observeActivity;
            manager.DungeonCombatEnded -= observeOutcome;
            _ = lethal?.Exception;
        }
    }

    /// <summary>
    /// Verifies failed startup removes transaction-created condition state and listeners.
    /// </summary>
    [UnityTest]
    public IEnumerator StartupFailureAfterSlowRestoresMissingConditionsAndRetriesOnce()
    {
        CombatantFixture player = CreateCombatant(
            "Failed Slow Startup Player",
            "Players",
            200,
            addConditions: false
        );
        CombatantFixture enemy = CreateCombatant("Failed Slow Startup Enemy", "Enemies", 100);
        PrepareBarbarian(player.Creature);
        player.Creature.passives.Add("Slow");
        BlockingFactObserver<TemporaryHitPointsGrantedFact> blocker = new(failAfterRelease: true);
        RuleDispatcher failedDispatcher = null;
        UnityAction installFailure = () =>
        {
            failedDispatcher = GetEncounterDispatcher();
            failedDispatcher.RegisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        };
        OnCombatStart.AddListener(installFailure);

        try
        {
            Assert.That(player.GameObject.GetComponent<Conditions>(), Is.Null);
            Assert.That(
                GetManagedListenerCount(player.Controller, "managedActionResetListeners"),
                Is.Zero
            );
            Assert.That(
                GetManagedListenerCount(player.Controller, "managedReactionListeners"),
                Is.Zero
            );

            manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
            yield return WaitForCondition(
                () => blocker.Started.IsCompleted,
                "Slow startup did not reach the later awaited Rage Fact."
            );
            Conditions provisional = player.GameObject.GetComponent<Conditions>();
            Assert.That(provisional, Is.Not.Null);
            Assert.That(provisional.Contains("Slowed"), Is.True);

            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: deliberate Rage Fact failure")
            );
            blocker.Release();
            yield return WaitForCondition(
                () => !manager.IsCombatActive,
                "Failed Slow startup did not roll back."
            );

            Assert.That(
                player.GameObject.GetComponent<Conditions>(),
                Is.Null,
                "A component created inside the failed startup transaction must be removed."
            );
            Assert.That(
                GetManagedListenerCount(player.Controller, "managedActionResetListeners"),
                Is.Zero
            );
            Assert.That(
                GetManagedListenerCount(player.Controller, "managedReactionListeners"),
                Is.Zero
            );
        }
        finally
        {
            OnCombatStart.RemoveListener(installFailure);
            blocker.Release();
            failedDispatcher?.UnregisterFactObserver<TemporaryHitPointsGrantedFact>(blocker);
        }

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn(player.GameObject);
        Conditions restored = player.GameObject.GetComponent<Conditions>();
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored.Contains("Slowed"), Is.True);
        Assert.That(
            GetManagedListenerCount(player.Controller, "managedActionResetListeners"),
            Is.EqualTo(1)
        );
        Assert.That(
            GetManagedListenerCount(player.Controller, "managedReactionListeners"),
            Is.EqualTo(1)
        );
        Assert.That(player.Controller.ActionPoints, Is.EqualTo(2));
    }

    /// <summary>Verifies suspension resets turn economy without changing durable creature state.</summary>
    [UnityTest]
    public IEnumerator SuspendDungeonCombat_ClearsTurnStateAndPreservesCreatureState()
    {
        CombatantFixture player = CreateCombatant("Player", "Players", 100);
        CombatantFixture enemy = CreateCombatant("Enemy", "Enemies", 0);
        CombatantFixture dormant = CreateCombatant("Suspended Dormant", "Enemies", -100);
        Vector3 preservedPosition = new(4f, 0f, 7f);
        ConditionSource preservedSource = new();
        player.GameObject.transform.position = preservedPosition;
        enemy.GameObject.transform.position = new Vector3(5f, 0f, 7f);
        dormant.GameObject.transform.position = new Vector3(9f, 0f, 7f);
        player.Creature.InitializeHealthBeforeEncounter(7, 10);
        player.Conditions.Add("Off-Guard", preservedSource);

        manager.StartDungeonCombat(new[] { player.Controller, enemy.Controller });
        yield return WaitForTurn();
        Assert.That(
            manager.GetCombatants(),
            Is.EqualTo(new[] { player.GameObject, enemy.GameObject })
        );
        Assert.That(manager.getPoistions(), Has.No.Member(dormant.GameObject.transform.position));
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
        Assert.That(
            manager.GetCombatants(),
            Is.EqualTo(new[] { player.GameObject, enemy.GameObject, dormant.GameObject })
        );
        Assert.That(
            manager.getPoistions(),
            Is.EqualTo(
                new[]
                {
                    player.GameObject.transform.position,
                    enemy.GameObject.transform.position,
                    dormant.GameObject.transform.position,
                }
            )
        );
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
            bool defeatPresentedAtCompletion = false;
            bool actionSettledAtCompletion = false;
            manager.DungeonCombatEnded += _ =>
            {
                completionCalls++;
                defeatPresentedAtCompletion =
                    enemy.Creature.IsDefeated && !enemy.GameObject.activeSelf;
                actionSettledAtCompletion = !player.Controller.IsTakingAction;
            };

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
            Assert.That(defeatPresentedAtCompletion, Is.True);
            Assert.That(actionSettledAtCompletion, Is.True);
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

    private Task CompleteDungeonSuspensionAsync(UnityEncounterRulesBridge bridge)
    {
        MethodInfo method = typeof(CombatManager).GetMethod(
            "CompleteDungeonSuspensionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null);
        return (
            (ValueTask)method.Invoke(manager, new object[] { bridge, GetEncounterGeneration() })
        ).AsTask();
    }

    private long GetEncounterGeneration()
    {
        FieldInfo field = typeof(CombatManager).GetField(
            "encounterGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (long)field.GetValue(manager);
    }

    private IEnumerator InvokeManagerRoutine(string name, params object[] arguments)
    {
        MethodInfo method = typeof(CombatManager).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null);
        return (IEnumerator)method.Invoke(manager, arguments);
    }

    private static UnityEncounterRulesBridge GetCreatureEncounterBridge(CreatureComponent creature)
    {
        FieldInfo field = typeof(CreatureComponent).GetField(
            "encounterRules",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return field.GetValue(creature) as UnityEncounterRulesBridge;
    }

    private IReadOnlyList<ActionController> GetPublishedActiveCombatants()
    {
        FieldInfo field = typeof(CombatManager).GetField(
            "activeCombatants",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return ((IEnumerable<ActionController>)field.GetValue(manager)).ToArray();
    }

    private IReadOnlyList<ActionController> GetStartupCombatants()
    {
        FieldInfo field = typeof(CombatManager).GetField(
            "startupCombatants",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (IReadOnlyList<ActionController>)field.GetValue(manager);
    }

    private object GetPendingEncounterCompletion()
    {
        FieldInfo field = typeof(CombatManager).GetField(
            "pendingEncounterCompletion",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return field.GetValue(manager);
    }

    private static IEnumerator WaitForCondition(Func<bool> condition, string timeoutMessage)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(condition(), Is.True, timeoutMessage);
    }

    private static int GetManagedListenerCount(ActionController controller, string fieldName)
    {
        FieldInfo field = typeof(ActionController).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return ((ICollection)field.GetValue(controller)).Count;
    }

    private CombatantFixture CreateCombatant(
        string name,
        string teamName,
        int initiative,
        bool addConditions = true
    )
    {
        GameObject gameObject = Create(name);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.name = name;
        creature.initiative = initiative;
        creature.InitializeHealthBeforeEncounter(10, 10);
        Conditions conditions = addConditions ? gameObject.AddComponent<Conditions>() : null;
        TestActionController controller = gameObject.AddComponent<TestActionController>();
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        manager.AddCombatant(controller);
        return new CombatantFixture(gameObject, creature, conditions, controller);
    }

    private T CreateLifecycleController<T>(string name, string teamName, int initiative)
        where T : ActionController
    {
        GameObject gameObject = Create(name);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.name = name;
        creature.initiative = initiative;
        creature.InitializeHealthBeforeEncounter(10, 10);
        gameObject.AddComponent<Conditions>();
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        return gameObject.AddComponent<T>();
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

    private static EquipmentWeapon CreateReloadTestWeapon()
    {
        return new EquipmentWeapon
        {
            name = "Review Sling",
            range = 50,
            reload = "1",
            ammo = "review-sling-bullets",
            damage = new Dice(1, 6, "bludgeoning"),
            traits = new List<string> { "propulsive" },
        };
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
            CoroutineRunner runner in UnityEngine.Object.FindObjectsByType<CoroutineRunner>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            UnityEngine.Object.DestroyImmediate(runner.gameObject);
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

    private sealed class StandaloneTestAIActionController : AIActionController { }

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
        }
    }

    private sealed class GatedEntityAction : MultiFrameEntityAction
    {
        private bool released;

        internal GatedEntityAction(string name)
            : base(0) => ActionName = name;

        public override string ActionName { get; }
        internal bool Started { get; private set; }

        internal void Release() => released = true;

        /// <inheritdoc/>
        protected override IEnumerator MFInvoke(GameObject target)
        {
            Started = true;
            while (!released)
                yield return null;
        }
    }

    private sealed class PausedCompletionSpellDefinition : ISpellDefinition
    {
        private bool completionReleased;

        public string Slug => "light";
        internal bool CastSettled { get; private set; }
        internal bool OuterSelectionFinishing { get; private set; }

        public IReadOnlyList<uint> GetActionCosts(PreparedSpell spell) => new[] { 1u };

        public IEnumerator SelectAndCast(SpellCastContext context)
        {
            yield return CoroutineRunner.Await(context.CastAsync(SpellTargetSelection.None));
            CastSettled = true;
            while (!completionReleased)
                yield return null;
            OuterSelectionFinishing = true;
            yield return null;
        }

        public bool IsSelectionValid(SpellCastContext context, SpellTargetSelection selection) =>
            true;

        public ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            result.Targets.Add(context.Caster);
            return new ValueTask<bool>(true);
        }

        public bool AppliesMultipleAttackPenalty(SpellCastContext context) => false;

        internal void ReleaseCompletion() => completionReleased = true;
    }

    private sealed class PausedLethalSpellDefinition : ISpellDefinition
    {
        private readonly GameObject target;
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal PausedLethalSpellDefinition(GameObject target) => this.target = target;

        public string Slug => "light";
        internal bool DamageSettled { get; private set; }
        internal bool CastBodyFinished { get; private set; }

        public IReadOnlyList<uint> GetActionCosts(PreparedSpell spell) => new[] { 1u };

        public IEnumerator SelectAndCast(SpellCastContext context)
        {
            yield return CoroutineRunner.Await(
                context.CastAsync(SpellTargetSelection.ForTarget(target))
            );
        }

        public bool IsSelectionValid(SpellCastContext context, SpellTargetSelection selection) =>
            selection.Targets.Count == 1 && selection.Targets[0] == target;

        public async ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            await target
                .GetComponent<CreatureComponent>()
                .ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-paused-lethal-spell"));
            DamageSettled = true;
            await release.Task;
            result.Targets.Add(target);
            CastBodyFinished = true;
            return true;
        }

        public bool AppliesMultipleAttackPenalty(SpellCastContext context) => false;

        internal void Release() => release.TrySetResult(true);
    }

    private sealed class FaultingSelfLethalSpellDefinition : ISpellDefinition
    {
        private readonly GameObject target;

        internal FaultingSelfLethalSpellDefinition(GameObject target) => this.target = target;

        public string Slug => "light";
        internal bool DamageSettled { get; private set; }

        public IReadOnlyList<uint> GetActionCosts(PreparedSpell spell) => new[] { 1u };

        public IEnumerator SelectAndCast(SpellCastContext context)
        {
            yield return CoroutineRunner.Await(
                context.CastAsync(SpellTargetSelection.ForTarget(target))
            );
        }

        public bool IsSelectionValid(SpellCastContext context, SpellTargetSelection selection) =>
            selection.Targets.Count == 1 && selection.Targets[0] == target;

        public async ValueTask<bool> Cast(
            SpellCastContext context,
            SpellTargetSelection selection,
            CastSpellResult result
        )
        {
            await target
                .GetComponent<CreatureComponent>()
                .ApplyFinalDamageAsync(1, RuleSource.FromSlug("test-faulting-self-lethal-spell"));
            DamageSettled = true;
            throw new InvalidOperationException("deliberate post-defeat spell failure");
        }

        public bool AppliesMultipleAttackPenalty(SpellCastContext context) => false;
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
        public Func<GameObject, IEnumerator> StrideRoutine { get; set; }
        public int StrideCalls { get; private set; }

        public override IEnumerator Stride(GameObject character)
        {
            StrideCalls++;
            if (StrideRoutine != null)
                yield return StrideRoutine(character);
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

    private sealed class StartupTestGridAPI : GridAPI, GridAPIPrivate
    {
        private readonly Tile[,] tiles = CreateTiles(8, 8);

        public List<GameObject> DestroyedTokens { get; } = new();

        public bool Contains(GameObject token) =>
            tiles.Cast<Tile>().Any(tile => tile.Occupants.Contains(token));

        public Tile[,] GetTiles() => tiles;

        public bool[,] GetLineOfSightBlocks() => new bool[tiles.GetLength(0), tiles.GetLength(1)];

        public IPathfinder GetPathfinder() => null;

        public bool AddToken(GameObject token)
        {
            Vector3Int position = Vector3Int.RoundToInt(token.transform.position);
            if (!GridTargeting.IsInBounds(tiles, position))
                return false;
            Tile tile = tiles[position.x, position.z];
            if (!tile.Occupants.Contains(token))
                tile.Occupants.Add(token);
            return true;
        }

        public override bool DestroyToken(GameObject token)
        {
            DestroyedTokens.Add(token);
            foreach (Tile tile in tiles)
                tile.Occupants.Remove(token);
            return true;
        }

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

        private static Tile[,] CreateTiles(int width, int height)
        {
            Tile[,] created = new Tile[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                    created[x, z] = new Tile();
            }
            return created;
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
        private readonly string failureMessage;
        private readonly TaskCompletionSource<bool> started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal BlockingFactObserver(
            bool failAfterRelease,
            string failureMessage = "deliberate Rage Fact failure"
        )
        {
            this.failAfterRelease = failAfterRelease;
            this.failureMessage = failureMessage;
        }

        internal Task Started => started.Task;

        internal void Release() => release.TrySetResult(true);

        public async ValueTask OnFactCommitted(TFact fact, RulesSnapshot snapshot)
        {
            started.TrySetResult(true);
            await release.Task;
            if (failAfterRelease)
                throw new InvalidOperationException(failureMessage);
        }
    }

    private sealed class SelectedFactFailureObserver<TFact> : IFactObserver<TFact>
        where TFact : RuleFact
    {
        private readonly Func<TFact, bool> shouldFail;
        private readonly string failureMessage;

        internal SelectedFactFailureObserver(Func<TFact, bool> shouldFail, string failureMessage)
        {
            this.shouldFail = shouldFail;
            this.failureMessage = failureMessage;
        }

        internal int Calls { get; private set; }

        public ValueTask OnFactCommitted(TFact fact, RulesSnapshot snapshot)
        {
            if (!shouldFail(fact))
                return default;
            Calls++;
            throw new InvalidOperationException(failureMessage);
        }
    }
}
