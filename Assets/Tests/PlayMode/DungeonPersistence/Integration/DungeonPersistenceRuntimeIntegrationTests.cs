using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Floors;
using Game.DungeonPersistence.Repository;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Verifies current-floor capture and load restoration through real dungeon runtime components.
/// </summary>
public sealed class DungeonPersistenceRuntimeIntegrationTests
{
    private const int RunSeed = 159;
    private const string GeneratorVersion = "persistence-integration-test";
    private const string EncounterId = "encounter-restored";
    private const string CreatureContentId = "goblin-warrior";
    private const string CreatureResourcePath = "DataFiles/pathfinder-monster-core/goblin-warrior";
    private readonly List<Object> cleanup = new();
    private CombatManager manager;
    private UnityEngine.Random.State randomState;

    /// <summary>Creates isolated runtime singletons and deterministic initiative randomness.</summary>
    [SetUp]
    public void SetUp()
    {
        DestroyExistingRuntime();
        randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(RunSeed);
        Track(new GameObject("Persistence integration combat log"))
            .AddComponent<RuntimeTestCombatLog>();
        Track(new GameObject("Persistence integration team rules")).AddComponent<TeamRules>();
        manager = Track(new GameObject("Persistence integration combat manager"))
            .AddComponent<CombatManager>();
    }

    /// <summary>Destroys synthetic scene objects and restores global random state.</summary>
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        UnityEngine.Random.state = randomState;
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
                Object.DestroyImmediate(cleanup[index]);
        }
        cleanup.Clear();
        yield return null;
    }

    /// <summary>Verifies current-floor capture rejects a party whose stable identity is absent.</summary>
    [Test]
    public void CaptureRequiresConfiguredStablePartyIdentity()
    {
        RuntimeTestActionController partyMember = CreatePartyMember(
            "Unidentified party member",
            new Vector3Int(0, 0, 0),
            configureIdentity: false,
            "",
            "",
            ""
        );
        DungeonEncounterRuntimeController runtime = CreateRuntime();
        DungeonLevelDocument document = CreateEmptyDocument();
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        runtime.InitializePristine(
            document,
            catalog,
            manager,
            new[] { partyMember },
            new RecordingExplorationPresentation()
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DungeonCurrentFloorCaptureService.CaptureNew(
                DungeonLevelJsonSerializer.Serialize(document),
                runtime
            )
        );

        Assert.That(exception.Message, Does.Contain("stable dungeon identity"));
    }

    /// <summary>
    /// Verifies grouped party and enemy capture retains durable actor state and a defeated enemy
    /// while excluding encounter-scoped turn economy.
    /// </summary>
    [Test]
    public void CaptureExistingPreservesDurableActorsAndExcludesTurnEconomy()
    {
        LoadedFixture fixture = CreateLoadedFixture(canonicalEnemyTokens: true);
        DungeonEncounterMember enemyMember = fixture
            .Runtime.GetComponentsInChildren<DungeonEncounterMember>(true)
            .Single();
        DungeonEncounterCreatureSaveState expectedEnemy =
            fixture.Plan.CurrentFloor.Creatures.Single(creature => !creature.Creature.IsDefeated);
        Assert.That(
            enemyMember.PersistentState,
            Is.EqualTo(DungeonSaveJsonCodec.SerializeCreature(expectedEnemy.Creature))
        );

        Assert.That(
            fixture.Runtime.TryCaptureExplorationLeader(out ActionController initialLeader),
            Is.True
        );
        Assert.That(initialLeader, Is.SameAs(fixture.Party[0]));

        DungeonRunActorRestorePlan restore = fixture.Plan.PreflightActors(fixture.Runtime);
        restore.Apply();

        Assert.That(
            fixture.Runtime.TryCaptureExplorationLeader(out ActionController restoredLeader),
            Is.True
        );
        Assert.That(restoredLeader, Is.SameAs(fixture.Party[1]));
        Assert.That(fixture.Party[0].transform.position, Is.EqualTo(new Vector3(0f, 0f, 0f)));
        Assert.That(fixture.Party[1].transform.position, Is.EqualTo(new Vector3(0f, 0f, 1f)));
        Assert.That(fixture.Party[0].GetComponent<CreatureComponent>().hp, Is.EqualTo(7));
        Assert.That(fixture.Party[1].GetComponent<CreatureComponent>().hp, Is.EqualTo(5));
        Assert.That(enemyMember.GetComponent<CreatureComponent>().hp, Is.EqualTo(4));
        Assert.That(enemyMember.transform.position, Is.EqualTo(new Vector3(4f, 0f, 4f)));

        IReadOnlyList<RuntimeTestActionController> controllers = fixture
            .Party.Append(enemyMember.GetComponent<RuntimeTestActionController>())
            .ToArray();
        SeedTransientState(controllers, firstPattern: true);
        DungeonCurrentFloorCapture first = DungeonCurrentFloorCaptureService.CaptureExisting(
            fixture.Plan.CurrentFloor.StaticFloorJson,
            fixture.Runtime,
            fixture.Plan.CurrentFloor
        );

        SeedTransientState(controllers, firstPattern: false);
        DungeonCurrentFloorCapture second = DungeonCurrentFloorCaptureService.CaptureExisting(
            fixture.Plan.CurrentFloor.StaticFloorJson,
            fixture.Runtime,
            first.Floor
        );

        Assert.That(first.Party.LeaderRosterSlotId, Is.EqualTo("roster-scout"));
        Assert.That(
            first.Party.Members.Select(member => member.RosterSlotId),
            Is.EqualTo(new[] { "roster-front", "roster-scout" })
        );
        Assert.That(
            first.Party.Members.Select(member => member.Creature.Cell),
            Is.EqualTo(new[] { new DungeonSaveCell(0, 0), new DungeonSaveCell(0, 1) })
        );
        Assert.That(
            first.Party.Members.Select(member => member.Creature.Health.CurrentHitPoints),
            Is.EqualTo(new[] { 7, 5 })
        );

        DungeonEncounterCreatureSaveState living = first.Floor.Creatures.Single(creature =>
            !creature.Creature.IsDefeated
        );
        DungeonEncounterCreatureSaveState defeated = first.Floor.Creatures.Single(creature =>
            creature.Creature.IsDefeated
        );
        Assert.That(living.Creature.Cell, Is.EqualTo(new DungeonSaveCell(4, 4)));
        Assert.That(living.Creature.Health.CurrentHitPoints, Is.EqualTo(4));
        Assert.That(defeated.Creature.InstanceId, Is.EqualTo(CreatureInstanceId(1)));
        Assert.That(defeated.Creature.Cell, Is.EqualTo(new DungeonSaveCell(5, 4)));
        Assert.That(defeated.Creature.Health.CurrentHitPoints, Is.Zero);
        Assert.That(
            first.Floor.Encounters.Single().Status,
            Is.EqualTo(DungeonEncounterSaveStatus.Suspended)
        );

        IReadOnlyDictionary<string, string> firstActors = CanonicalActorJson(first);
        IReadOnlyDictionary<string, string> secondActors = CanonicalActorJson(second);
        Assert.That(secondActors.Keys, Is.EquivalentTo(firstActors.Keys));
        foreach (KeyValuePair<string, string> actor in firstActors)
        {
            Assert.That(
                secondActors[actor.Key],
                Is.EqualTo(actor.Value),
                $"Encounter-scoped turn state leaked into actor '{actor.Key}'."
            );
        }
    }

    /// <summary>Verifies actor preflight rejects an enemy whose canonical restore token was lost.</summary>
    [Test]
    public void LoadPreflightRejectsMaterializedEnemyWithoutCanonicalToken()
    {
        LoadedFixture fixture = CreateLoadedFixture(canonicalEnemyTokens: false);
        DungeonEncounterMember enemy = fixture
            .Runtime.GetComponentsInChildren<DungeonEncounterMember>(true)
            .Single();
        Assert.That(enemy.PersistentState, Is.Empty);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Plan.PreflightActors(fixture.Runtime)
        );

        Assert.That(exception.Message, Does.Contain("canonical restore token"));
        Assert.That(fixture.Party[0].GetComponent<CreatureComponent>().hp, Is.EqualTo(10));
        Assert.That(fixture.Party[1].GetComponent<CreatureComponent>().hp, Is.EqualTo(10));
    }

    /// <summary>
    /// Verifies a roster slot and actor ID cannot disguise the wrong materialized party content.
    /// </summary>
    [Test]
    public void LoadPreflightRejectsWrongPartyCreatureContentBeforeMutation()
    {
        LoadedFixture fixture = CreateLoadedFixture(
            canonicalEnemyTokens: true,
            materializedScoutContentId: "party-wrong-content"
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Plan.PreflightActors(fixture.Runtime)
        );

        Assert.That(exception.Message, Does.Contain("materialized creature content"));
        Assert.That(fixture.Party[0].GetComponent<CreatureComponent>().hp, Is.EqualTo(10));
        Assert.That(fixture.Party[1].GetComponent<CreatureComponent>().hp, Is.EqualTo(10));
    }

    /// <summary>
    /// Verifies an unfinished saved encounter restores as suspended exploration and starts a fresh
    /// initiative round instead of retaining action points, reactions, or attack penalty.
    /// </summary>
    [UnityTest]
    public IEnumerator RestoredUnfinishedEncounterStartsFreshInitiativeAndTurnState()
    {
        LoadedFixture fixture = CreateLoadedFixture(canonicalEnemyTokens: true);
        fixture.Plan.PreflightActors(fixture.Runtime).Apply();
        Assert.That(manager.IsCombatActive, Is.False);
        Assert.That(
            fixture.Runtime.Lifecycle.GetEncounter(EncounterId).State,
            Is.EqualTo(DungeonEncounterGroupState.Suspended)
        );

        RuntimeTestActionController enemy = fixture
            .Runtime.GetComponentsInChildren<DungeonEncounterMember>(true)
            .Single()
            .GetComponent<RuntimeTestActionController>();
        RuntimeTestActionController[] participants = fixture.Party.Append(enemy).ToArray();
        SeedTransientState(participants, firstPattern: true);

        yield return null;
        fixture.Party[1].transform.position = new Vector3(3f, 0f, 3f);
        yield return null;

        Assert.That(manager.IsCombatActive, Is.True);
        Assert.That(
            fixture.Runtime.Lifecycle.GetEncounter(EncounterId).State,
            Is.EqualTo(DungeonEncounterGroupState.Active)
        );
        Assert.That(participants.Count(controller => controller.HasTurnAuthority), Is.EqualTo(1));
        Assert.That(participants.Count(controller => controller.ActionPoints == 3), Is.EqualTo(1));
        foreach (RuntimeTestActionController participant in participants)
        {
            if (!participant.HasTurnAuthority)
                Assert.That(participant.ActionPoints, Is.Zero, participant.name);
            Assert.That(participant.Reacted, Is.False, participant.name);
            Assert.That(participant.StrikePenalty, Is.Zero, participant.name);
            Assert.That(participant.IsTakingAction, Is.False, participant.name);
        }
        Assert.That(participants.Sum(controller => controller.StartTurnCount), Is.EqualTo(1));
    }

    private LoadedFixture CreateLoadedFixture(
        bool canonicalEnemyTokens,
        string materializedScoutContentId = "party-scout"
    )
    {
        DungeonRunSave save = CreateRunSave();
        DungeonRunLoadPreparationResult preparation = DungeonRunLoadPlan.Prepare(save);
        Assert.That(preparation, Is.TypeOf<DungeonRunLoadPreparationSuccess>());
        DungeonRunLoadPlan plan = ((DungeonRunLoadPreparationSuccess)preparation).Plan;

        RuntimeTestActionController front = CreatePartyMember(
            "Front party member",
            new Vector3Int(6, 0, 0),
            configureIdentity: true,
            "roster-front",
            "party-0000",
            "party-fighter"
        );
        RuntimeTestActionController scout = CreatePartyMember(
            "Scout party member",
            new Vector3Int(6, 0, 1),
            configureIdentity: true,
            "roster-scout",
            "party-0001",
            materializedScoutContentId
        );
        DungeonEncounterCreatureCatalog catalog = CreateCatalog();
        DungeonEncounterRuntimeController runtime = CreateRuntime();
        DungeonLevelDocument population = canonicalEnemyTokens
            ? plan.PopulationDocument
            : RemoveCreatureRestoreTokens(plan.PopulationDocument);
        runtime.InitializePersisted(
            population,
            catalog,
            manager,
            new[] { front, scout },
            new RecordingExplorationPresentation()
        );
        Assert.That(runtime.CaptureMaterializedCreatures(), Has.Count.EqualTo(1));
        return new LoadedFixture(plan, runtime, new[] { front, scout });
    }

    private RuntimeTestActionController CreatePartyMember(
        string name,
        Vector3Int cell,
        bool configureIdentity,
        string rosterSlotId,
        string actorInstanceId,
        string creatureContentId
    )
    {
        GameObject owner = Track(new GameObject(name));
        owner.transform.position = cell;
        CreatureComponent creature = owner.AddComponent<CreatureComponent>();
        creature.name = name;
        creature.initiative = name.Length;
        creature.InitializeHealthBeforeEncounter(10, 10);
        owner.AddComponent<Conditions>();
        Team team = owner.AddComponent<Team>();
        team.Name = "Players";
        RuntimeTestActionController controller = owner.AddComponent<RuntimeTestActionController>();
        if (configureIdentity)
        {
            owner
                .AddComponent<DungeonPartyMemberIdentity>()
                .Configure(rosterSlotId, actorInstanceId, creatureContentId);
        }
        manager.AddCombatant(controller);
        return controller;
    }

    private DungeonEncounterCreatureCatalog CreateCatalog()
    {
        GameObject prefab = Track(new GameObject("Persistence encounter creature prefab"));
        prefab.AddComponent<RuntimeTestActionController>();
        Team team = prefab.AddComponent<Team>();
        team.Name = DungeonEncounterCreatureCatalog.HostileTeamName;
        prefab.AddComponent<Token>();
        DungeonEncounterCreatureCatalog catalog = Track(
            ScriptableObject.CreateInstance<DungeonEncounterCreatureCatalog>()
        );
        catalog.ReplaceEntries(
            new[]
            {
                new DungeonEncounterCreatureCatalogEntry(
                    CreatureContentId,
                    CreatureResourcePath,
                    prefab
                ),
            }
        );
        return catalog;
    }

    private DungeonEncounterRuntimeController CreateRuntime() =>
        Track(new GameObject("Persistence dungeon runtime"))
            .AddComponent<DungeonEncounterRuntimeController>();

    private static DungeonRunSave CreateRunSave()
    {
        DungeonLevelDocument document = CreateStaticDocument();
        DungeonPartySaveState party = new(
            "roster-scout",
            new[]
            {
                new DungeonPartyMemberSaveState(
                    "roster-front",
                    Actor("party-0000", "party-fighter", new DungeonSaveCell(0, 0), 7, 10)
                ),
                new DungeonPartyMemberSaveState(
                    "roster-scout",
                    Actor("party-0001", "party-scout", new DungeonSaveCell(0, 1), 5, 12)
                ),
            }
        );
        DungeonFloorSaveState floor = new(
            DungeonSaveSchema.FloorStateVersion,
            0,
            DungeonLevelJsonSerializer.Serialize(document),
            Array.Empty<DungeonDoorSaveState>(),
            new[] { new DungeonEncounterSaveState(EncounterId, DungeonEncounterSaveStatus.Active) },
            new[]
            {
                new DungeonEncounterCreatureSaveState(
                    EncounterId,
                    Actor(
                        CreatureInstanceId(0),
                        CreatureContentId,
                        new DungeonSaveCell(4, 4),
                        4,
                        9,
                        hasGoblinEquipment: true
                    )
                ),
                new DungeonEncounterCreatureSaveState(
                    EncounterId,
                    Actor(
                        CreatureInstanceId(1),
                        CreatureContentId,
                        new DungeonSaveCell(5, 4),
                        0,
                        9,
                        hasGoblinEquipment: true
                    )
                ),
            }
        );
        DungeonRunSaveManifest manifest = new(
            DungeonSaveSchema.RunManifestVersion,
            RunSeed,
            GeneratorVersion,
            0,
            party,
            new[] { DungeonFloorSaveReference.Current(0) }
        );
        return new DungeonRunSave(manifest, new[] { floor });
    }

    private static DungeonCreatureSaveState Actor(
        string instanceId,
        string contentId,
        DungeonSaveCell cell,
        int currentHitPoints,
        int maximumHitPoints,
        bool hasGoblinEquipment = false
    ) =>
        new(
            instanceId,
            contentId,
            cell,
            new DungeonHealthSaveState(
                currentHitPoints,
                maximumHitPoints,
                0,
                string.Empty,
                Array.Empty<string>()
            ),
            currentHitPoints == 0,
            Array.Empty<DungeonConditionSaveState>(),
            Array.Empty<DungeonTimedEffectSaveState>(),
            new DungeonPreparedRuleSaveState(
                Array.Empty<string>(),
                Array.Empty<DungeonPreparedEffectSaveState>(),
                Array.Empty<DungeonSpellPoolSaveState>()
            ),
            hasGoblinEquipment
                ? GoblinEquipment(instanceId)
                : new DungeonEquipmentSaveState(
                    Array.Empty<DungeonInventoryItemSaveState>(),
                    Array.Empty<DungeonAmmunitionSaveState>()
                )
        );

    private static DungeonEquipmentSaveState GoblinEquipment(string instanceId) =>
        new(
            new[]
            {
                new DungeonInventoryItemSaveState(
                    instanceId + "/inventory/0000",
                    "dogslicer",
                    1,
                    DungeonEquipmentSlot.Carried,
                    isLoaded: true
                ),
                new DungeonInventoryItemSaveState(
                    instanceId + "/inventory/0001",
                    "shortbow",
                    1,
                    DungeonEquipmentSlot.Carried,
                    isLoaded: true
                ),
                new DungeonInventoryItemSaveState(
                    instanceId + "/inventory/0002",
                    "leather-armor",
                    1,
                    DungeonEquipmentSlot.Armor,
                    isLoaded: true
                ),
            },
            new[] { new DungeonAmmunitionSaveState("arrows", 10) }
        );

    private static DungeonLevelDocument CreateStaticDocument()
    {
        DungeonEncounterPlan encounter = new(
            EncounterId,
            1,
            DungeonEncounterThreat.Trivial,
            40,
            new[] { new DungeonCell(4, 4), new DungeonCell(5, 4) },
            new[] { CreatureContentId, CreatureContentId }
        );
        return new DungeonLevelDocument(
            new DungeonGenerationMetadata(GeneratorVersion, RunSeed, 0, 0),
            Enumerable.Repeat(".......", 7),
            new[] { new DungeonRoom(1, 3, 3, 5, 5) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(0, 0),
            new[] { new DungeonCell(0, 0), new DungeonCell(0, 1) },
            Array.Empty<DungeonObjectPlacement>(),
            new[] { encounter }
        );
    }

    private static DungeonLevelDocument CreateEmptyDocument() =>
        new(
            new DungeonGenerationMetadata(GeneratorVersion, RunSeed, 0, 0),
            new[] { "...", "...", "..." },
            new[] { new DungeonRoom(1, 1, 1, 1, 1) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(0, 0),
            new[] { new DungeonCell(0, 0) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>()
        );

    private static DungeonLevelDocument RemoveCreatureRestoreTokens(DungeonLevelDocument document)
    {
        DungeonRuntimeState source = document.RuntimeState;
        DungeonRuntimeState withoutTokens = new(
            source.OpenDoorIds,
            source.ResolvedEncounterIds,
            source.DefeatedCreatureIds,
            source.Creatures.Select(creature => new DungeonCreatureRuntimeState(
                creature.InstanceId,
                creature.CreatureId,
                creature.EncounterId,
                creature.Cell,
                creature.HitPoints,
                string.Empty
            ))
        );
        return new DungeonLevelDocument(
            document.Generation,
            document.Rows,
            document.Rooms,
            document.Doors,
            document.Stairs,
            document.StartCell,
            document.SafeCells,
            document.Objects,
            document.EncounterPlans,
            withoutTokens
        );
    }

    private static void SeedTransientState(
        IReadOnlyList<RuntimeTestActionController> controllers,
        bool firstPattern
    )
    {
        for (int index = 0; index < controllers.Count; index++)
        {
            controllers[index]
                .SeedTransientState(
                    hasTurnAuthority: firstPattern == (index % 2 == 0),
                    actionPoints: firstPattern ? (uint)(index + 1) : (uint)(8 - index),
                    reacted: firstPattern != (index % 2 == 0),
                    strikePenalty: firstPattern ? (uint)(index + 1) : (uint)(3 - index)
                );
        }
    }

    private static IReadOnlyDictionary<string, string> CanonicalActorJson(
        DungeonCurrentFloorCapture capture
    ) =>
        capture
            .Party.Members.Select(member => member.Creature)
            .Concat(capture.Floor.Creatures.Select(creature => creature.Creature))
            .ToDictionary(
                actor => actor.InstanceId,
                DungeonSaveJsonCodec.SerializeCreature,
                StringComparer.Ordinal
            );

    private static string CreatureInstanceId(int index) =>
        DungeonCreatureInstanceIdentity.Create(EncounterId, index);

    private T Track<T>(T value)
        where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    private static void DestroyExistingRuntime()
    {
        HashSet<GameObject> owners = new();
        foreach (
            GameManager component in Object.FindObjectsByType<GameManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            CombatManagerInterface component in Object.FindObjectsByType<CombatManagerInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            CombatLogInterface component in Object.FindObjectsByType<CombatLogInterface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            TeamRules component in Object.FindObjectsByType<TeamRules>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (
            GridAPI component in Object.FindObjectsByType<GridAPI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            owners.Add(component.gameObject);
        foreach (GameObject owner in owners)
            Object.DestroyImmediate(owner);
    }

    private sealed class LoadedFixture
    {
        internal LoadedFixture(
            DungeonRunLoadPlan plan,
            DungeonEncounterRuntimeController runtime,
            IReadOnlyList<RuntimeTestActionController> party
        )
        {
            Plan = plan;
            Runtime = runtime;
            Party = party;
        }

        internal DungeonRunLoadPlan Plan { get; }

        internal DungeonEncounterRuntimeController Runtime { get; }

        internal IReadOnlyList<RuntimeTestActionController> Party { get; }
    }

    private sealed class RuntimeTestActionController : ActionController
    {
        internal int StartTurnCount { get; private set; }

        internal void SeedTransientState(
            bool hasTurnAuthority,
            uint actionPoints,
            bool reacted,
            uint strikePenalty
        )
        {
            IsTurn = hasTurnAuthority;
            ActionPoints = actionPoints;
            Reacted = reacted;
            StrikePenalty = strikePenalty;
            IsTakingAction = false;
        }

        /// <inheritdoc/>
        public override void StartTurn()
        {
            StartTurnCount++;
            base.StartTurn();
        }

        /// <inheritdoc/>
        public override void EndTurn()
        {
            if (!HasTurnAuthority)
                return;
            ResetEncounterTurnState();
            CombatManagerInterface.GetInstance().NextTurn();
        }
    }

    private sealed class RecordingExplorationPresentation : IDungeonExplorationPresentation
    {
        /// <inheritdoc/>
        public void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        ) { }

        /// <inheritdoc/>
        public void HideExploration() { }
    }

    private sealed class RuntimeTestCombatLog : CombatLogInterface
    {
        /// <inheritdoc/>
        public override void DevMode() { }

        /// <inheritdoc/>
        public override void ReleaseMode() { }

        /// <inheritdoc/>
        public override void AddWhiteList(string tag) { }

        /// <inheritdoc/>
        public override void AddBlackList(string tag) { }

        /// <inheritdoc/>
        public override void Log(string msg) { }

        /// <inheritdoc/>
        public override void DevLog(string msg) { }

        /// <inheritdoc/>
        public override void DevLog(string msg, string tag) { }

        /// <inheritdoc/>
        public override void DevLog(string msg, List<string> tags) { }

        /// <inheritdoc/>
        public override void Log(string msg, string tag) { }

        /// <inheritdoc/>
        public override void Log(string msg, List<string> tags) { }

        /// <inheritdoc/>
        public override List<string> GetMessages() => new();
    }
}
