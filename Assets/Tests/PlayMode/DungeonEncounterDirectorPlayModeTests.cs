using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.Rules.Runtime;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Verifies the complete room-entry, reinforcement, retreat, resume, casualty, and victory flow.
/// </summary>
public sealed class DungeonEncounterDirectorPlayModeTests
{
    private const string GoblinJson = "DataFiles/pathfinder-monster-core/goblin-warrior";
    private readonly List<Object> cleanup = new();
    private CombatManager manager;
    private RecordingCombatLog combatLog;
    private DungeonEncounterCreatureCatalog catalog;
    private GameObject creaturePrefab;
    private GameObject encounterRoot;
    private DirectorTestActionController player;
    private DirectorTestCreatureFactory factory;
    private DungeonEncounterDirector director;
    private UnityEngine.Random.State randomState;

    /// <summary>Creates isolated runtime dependencies and deterministic combat randomness.</summary>
    [SetUp]
    public void SetUp()
    {
        DestroyExistingRuntime();
        randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(158);

        combatLog = Track(new GameObject("TestCombatLog")).AddComponent<RecordingCombatLog>();
        manager = Track(new GameObject("CombatManager")).AddComponent<CombatManager>();
        player = CreateCombatant("Player", "Players", 100);
        manager.AddCombatant(player);
        encounterRoot = Track(new GameObject("Encounter root"));
        creaturePrefab = Track(new GameObject("Encounter creature prefab"));
        creaturePrefab.SetActive(false);
        creaturePrefab.AddComponent<DirectorTestActionController>();
        creaturePrefab.AddComponent<Token>();
        Team encounterTeam = creaturePrefab.AddComponent<Team>();
        encounterTeam.Name = DungeonEncounterCreatureCatalog.HostileTeamName;
        catalog = Track(ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>());
        catalog.ReplaceEntries(
            new[]
            {
                new DungeonEncounterCreatureCatalogEntry("goblin-a", GoblinJson, creaturePrefab),
                new DungeonEncounterCreatureCatalogEntry("goblin-b", GoblinJson, creaturePrefab),
            }
        );
        factory = new DirectorTestCreatureFactory();
        DungeonEncounterMaterializer materializer = new(
            catalog,
            factory,
            new DirectorTestRuntimeRegistration()
        );
        director = new DungeonEncounterDirector(
            new DungeonEncounterStateMachine(CreatePlans()),
            new[] { player },
            manager,
            materializer,
            encounterRoot.transform,
            CreateRooms()
        );
    }

    /// <summary>Disposes subscriptions and destroys every synthetic test object.</summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        director?.Dispose();
        UnityEngine.Random.state = randomState;
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
                Object.DestroyImmediate(cleanup[index]);
        }
        cleanup.Clear();
        yield return null;
    }

    /// <summary>Verifies first entry materializes only that room and scopes initiative accordingly.</summary>
    [Test]
    public void FirstEntryMaterializesOnlyEnteredRoom()
    {
        Assert.That(encounterRoot.transform.childCount, Is.Zero);
        Assert.That(manager.IsCombatActive, Is.False);

        DungeonRoomEntryResult result = director.EnterRoom(1);

        Assert.That(result.Transition, Is.EqualTo(DungeonRoomEntryTransition.FirstActivation));
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(encounterRoot.transform.childCount, Is.EqualTo(2));
        Assert.That(
            Members().Select(member => member.InstanceId),
            Is.EquivalentTo(new[] { "encounter-a/creature-0000", "encounter-a/creature-0001" })
        );
        Assert.That(manager.GetCombatants(), Has.Count.EqualTo(3));
        Assert.That(
            director.Lifecycle.GetRoomEncounter(2).State,
            Is.EqualTo(DungeonEncounterGroupState.Dormant)
        );
    }

    /// <summary>Verifies a second entered room materializes and joins the running fight.</summary>
    [Test]
    public void ChainedRoomEntryAddsReinforcements()
    {
        director.EnterRoom(1);

        DungeonRoomEntryResult result = director.EnterRoom(2);

        Assert.That(result.Transition, Is.EqualTo(DungeonRoomEntryTransition.Reinforcement));
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(encounterRoot.transform.childCount, Is.EqualTo(3));
        Assert.That(manager.GetCombatants(), Has.Count.EqualTo(4));
        Assert.That(
            director.Lifecycle.ActiveEncounterIds,
            Is.EqualTo(new[] { "encounter-a", "encounter-b" })
        );
    }

    /// <summary>Verifies an active persisted group resumes through fresh initiative after load.</summary>
    [Test]
    public void RestoredActiveGroupNormalizesAndResumesFromExploration()
    {
        director.Dispose();
        DungeonEncounterStateMachine source = new(CreatePlans());
        source.EnterRoom(1);
        DungeonEncounterStateMachine restored = DungeonEncounterStateMachine.Restore(
            CreatePlans(),
            source.CaptureSnapshot()
        );
        director = new DungeonEncounterDirector(
            restored,
            new[] { player },
            manager,
            new DungeonEncounterMaterializer(
                catalog,
                factory,
                new DirectorTestRuntimeRegistration()
            ),
            encounterRoot.transform,
            CreateRooms()
        );

        Assert.That(
            director.Lifecycle.GetRoomEncounter(1).State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );
        Assert.That(
            encounterRoot.GetComponentsInChildren<DungeonEncounterMember>(),
            Has.Length.EqualTo(2),
            "Restored suspended survivors should exist before their source room is revisited."
        );

        DungeonRoomEntryResult result = director.EnterRoom(1);

        Assert.That(result.Transition, Is.EqualTo(DungeonRoomEntryTransition.Resume));
        Assert.That(manager.IsCombatActive, Is.True);
    }

    /// <summary>
    /// Verifies retreat and return preserve durable survivor state while resetting turn state and
    /// rolling a fresh initiative order.
    /// </summary>
    [Test]
    public void RetreatAndResumePreservePartialCasualtiesAndDurableState()
    {
        director.EnterRoom(1);
        DungeonEncounterMember defeated = Member("encounter-a/creature-0000");
        DungeonEncounterMember survivor = Member("encounter-a/creature-0001");
        Defeat(defeated);
        CreatureComponent survivorCreature = survivor.GetComponent<CreatureComponent>();
        DirectorTestActionController survivorController =
            survivor.GetComponent<DirectorTestActionController>();
        survivorCreature.ApplyFinalDamage(6, RuleSource.FromSlug("test-survivor-damage"));
        survivor.transform.position = new Vector3(4f, 0f, 3f);
        survivorController.ActionPoints = 2;
        survivorController.Reacted = true;
        survivorController.IsTakingAction = false;

        DungeonEncounterSuspensionResult suspension = director.EvaluatePartyRegions(
            1,
            Array.Empty<int>()
        );

        Assert.That(
            suspension.Transition,
            Is.EqualTo(DungeonEncounterSuspensionTransition.Suspended)
        );
        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(survivorController.ActionPoints, Is.Zero);
        Assert.That(survivorController.Reacted, Is.False);
        Assert.That(survivorController.StrikePenalty, Is.Zero);
        Assert.That(survivorController.IsTakingAction, Is.False);

        DungeonRoomEntryResult resumed = director.EnterRoom(1);

        Assert.That(resumed.Transition, Is.EqualTo(DungeonRoomEntryTransition.Resume));
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(factory.CreateCount, Is.EqualTo(2));
        Assert.That(survivorCreature.hp, Is.EqualTo(4));
        Assert.That(survivor.transform.position, Is.EqualTo(new Vector3(4f, 0f, 3f)));
        Assert.That(defeated.DefeatWasReported, Is.True);
        Assert.That(
            director.Lifecycle.GetEncounter("encounter-a").LivingCreatures,
            Has.Count.EqualTo(1)
        );
        Assert.That(
            combatLog.Messages.Count(message => message.StartsWith("Initiative Order")),
            Is.EqualTo(2)
        );
    }

    /// <summary>
    /// Verifies an enemy that leaves its source room keeps combat authority instead of becoming
    /// an inactive blocking occupant.
    /// </summary>
    [Test]
    public void DisplacedLivingEnemyPreventsEncounterSuspension()
    {
        director.EnterRoom(1);
        DungeonEncounterMember displaced = Member("encounter-a/creature-0001");
        displaced.transform.position = new Vector3(5f, 0f, 3f);

        DungeonEncounterSuspensionResult result = director.EvaluatePartyRegions(
            1,
            Array.Empty<int>()
        );

        Assert.That(
            result.Transition,
            Is.EqualTo(DungeonEncounterSuspensionTransition.RemainedActive)
        );
        Assert.That(result.SuspendedEncounterIds, Is.Empty);
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(manager.GetCombatants(), Does.Contain(displaced.gameObject));
        Assert.That(
            director.Lifecycle.GetEncounter("encounter-a").State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );
    }

    /// <summary>Verifies missing party-room observations fail at the director boundary.</summary>
    [Test]
    public void EvaluatePartyRegions_NullRoomIdsThrowsArgumentNullException()
    {
        director.EnterRoom(1);

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            director.EvaluatePartyRegions(1, null)
        );

        Assert.That(exception.ParamName, Is.EqualTo("livingPcRoomIds"));
        Assert.That(manager.IsCombatActive, Is.True);
    }

    /// <summary>Verifies a targetable suspended enemy can die during another resumed fight.</summary>
    [Test]
    public void SuspendedMaterializedEnemyDefeatPersistsWithoutInterruptingActiveGroup()
    {
        director.EnterRoom(1);
        director.EnterRoom(2);
        director.EvaluatePartyRegions(1, Array.Empty<int>());
        director.EnterRoom(1);
        DungeonEncounterMember suspended = Member("encounter-b/creature-0000");

        Assert.DoesNotThrow(() => Defeat(suspended));

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            director.Lifecycle.GetEncounter("encounter-b").State,
            Is.EqualTo(DungeonEncounterGroupState.Cleared)
        );
        Assert.That(
            director
                .CaptureSnapshot()
                .Groups.Single(group => group.EncounterId == "encounter-b")
                .DefeatedCreatureInstanceIds,
            Is.EqualTo(new[] { "encounter-b/creature-0000" })
        );
    }

    /// <summary>Verifies only clearing every active group returns the game to exploration.</summary>
    [Test]
    public void FinalActiveGroupVictoryClearsGroupsAndEndsCombat()
    {
        director.EnterRoom(1);
        director.EnterRoom(2);

        Defeat(Member("encounter-a/creature-0000"));
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            director.Lifecycle.GetEncounter("encounter-a").State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );

        Defeat(Member("encounter-a/creature-0001"));
        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            director.Lifecycle.GetEncounter("encounter-a").State,
            Is.EqualTo(DungeonEncounterGroupState.Cleared)
        );

        Defeat(Member("encounter-b/creature-0000"));

        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(
            director.Lifecycle.Encounters.Select(encounter => encounter.State),
            Is.All.EqualTo(DungeonEncounterGroupState.Cleared)
        );
        Assert.That(
            director.CaptureSnapshot().Groups.Select(group => group.State),
            Is.All.EqualTo(DungeonEncounterGroupState.Cleared)
        );
    }

    private void Defeat(DungeonEncounterMember member)
    {
        manager.Remove(member.GetComponent<ActionController>());
        member.ReportDefeated();
        member.gameObject.SetActive(false);
    }

    private DungeonEncounterMember Member(string instanceId) =>
        Members().Single(member => string.Equals(member.InstanceId, instanceId));

    private DungeonEncounterMember[] Members() =>
        encounterRoot.GetComponentsInChildren<DungeonEncounterMember>(true);

    private DirectorTestActionController CreateCombatant(
        string name,
        string teamName,
        int initiative
    )
    {
        GameObject owner = Track(new GameObject(name));
        CreatureComponent creature = owner.AddComponent<CreatureComponent>();
        creature.name = name;
        creature.InitializeHealthBeforeEncounter(10, 10);
        creature.initiative = initiative;
        owner.AddComponent<Conditions>();
        Team team = owner.AddComponent<Team>();
        team.Name = teamName;
        return owner.AddComponent<DirectorTestActionController>();
    }

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    private static void DestroyExistingRuntime()
    {
        foreach (
            GameManager gameManager in Object.FindObjectsByType<GameManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            Object.DestroyImmediate(gameManager.gameObject);
        foreach (
            CombatManagerInterface combatManager in Object.FindObjectsByType<CombatManagerInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            Object.DestroyImmediate(combatManager.gameObject);
        foreach (
            CombatLogInterface combatLog in Object.FindObjectsByType<CombatLogInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            Object.DestroyImmediate(combatLog.gameObject);
    }

    private static DungeonEncounterPlan[] CreatePlans() =>
        new[]
        {
            new DungeonEncounterPlan(
                "encounter-a",
                1,
                DungeonEncounterThreat.Low,
                60,
                new[] { new DungeonCell(2, 2), new DungeonCell(3, 2) },
                new[] { "goblin-a", "goblin-a" }
            ),
            new DungeonEncounterPlan(
                "encounter-b",
                2,
                DungeonEncounterThreat.Trivial,
                40,
                new[] { new DungeonCell(8, 8) },
                new[] { "goblin-b" }
            ),
        };

    private static DungeonRoom[] CreateRooms() =>
        new[] { new DungeonRoom(1, 1, 1, 4, 4), new DungeonRoom(2, 7, 7, 9, 9) };

    private sealed class DirectorTestCreatureFactory : IDungeonEncounterCreatureFactory
    {
        /// <summary>Gets the number of creatures created by this test factory.</summary>
        public int CreateCount { get; private set; }

        /// <inheritdoc/>
        public GameObject Create(
            DungeonEncounterCreatureCatalogEntry definition,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Transform parent
        )
        {
            CreateCount++;
            GameObject instance = new(definition.ContentId);
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(worldPosition, worldRotation);
            CreatureComponent creature = instance.AddComponent<CreatureComponent>();
            creature.name = definition.ContentId;
            creature.InitializeHealthBeforeEncounter(10, 10);
            creature.initiative = CreateCount * 10;
            instance.AddComponent<Conditions>();
            Team team = instance.AddComponent<Team>();
            team.Name = "Enemies";
            instance.AddComponent<DirectorTestActionController>();
            return instance;
        }

        /// <inheritdoc/>
        public void Destroy(GameObject instance) => Object.DestroyImmediate(instance);
    }

    private sealed class DirectorTestRuntimeRegistration : IDungeonEncounterRuntimeRegistration
    {
        /// <inheritdoc/>
        public DungeonCell ResolveAvailable(
            DungeonCell preferred,
            DungeonRoom room,
            IReadOnlyCollection<DungeonCell> reserved
        ) => preferred;

        /// <inheritdoc/>
        public void RequireAvailable(DungeonCell cell) { }

        /// <inheritdoc/>
        public void ValidateCreated(GameObject instance, DungeonCell cell) { }

        /// <inheritdoc/>
        public void Rollback(GameObject instance) { }
    }

    private sealed class DirectorTestActionController : ActionController
    {
        /// <inheritdoc/>
        public override void EndTurn()
        {
            ResetEncounterTurnState();
            CombatManagerInterface.GetInstance().NextTurn();
        }
    }

    private sealed class RecordingCombatLog : CombatLogInterface
    {
        /// <summary>Gets captured messages in emission order.</summary>
        public List<string> Messages { get; } = new();

        /// <inheritdoc/>
        public override void DevMode() { }

        /// <inheritdoc/>
        public override void ReleaseMode() { }

        /// <inheritdoc/>
        public override void AddWhiteList(string tag) { }

        /// <inheritdoc/>
        public override void AddBlackList(string tag) { }

        /// <inheritdoc/>
        public override void Log(string msg) => Messages.Add(msg);

        /// <inheritdoc/>
        public override void DevLog(string msg) => Messages.Add(msg);

        /// <inheritdoc/>
        public override void DevLog(string msg, string tag) => Messages.Add(msg);

        /// <inheritdoc/>
        public override void DevLog(string msg, List<string> tags) => Messages.Add(msg);

        /// <inheritdoc/>
        public override void Log(string msg, string tag) => Messages.Add(msg);

        /// <inheritdoc/>
        public override void Log(string msg, List<string> tags) => Messages.Add(msg);

        /// <inheritdoc/>
        public override List<string> GetMessages() => new(Messages);
    }
}
