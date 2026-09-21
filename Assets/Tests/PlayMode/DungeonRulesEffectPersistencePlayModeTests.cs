using System;
using System.Collections;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class DungeonRulesPersistencePlayModeController : ActionController
{
    public override void EndTurn() { }
}

public sealed class DungeonRulesEffectPersistencePlayModeTests
{
    [UnityTest]
    public IEnumerator IndependentFloorBridgesKeepGeneratedEffectIdentitiesRunGlobal()
    {
        const string reusedLocalId = "encounter-shared/creature-0000";
        DungeonRulesActorReference heroReference = DungeonRulesActorReference.Party("hero");
        GameObject firstHero = CreateActor("First Floor Hero", "players", Vector3.zero);
        GameObject firstEnemy = CreateActor("First Floor Enemy", "enemies", Vector3.right);
        GameObject secondHero = CreateActor("Second Floor Hero", "players", Vector3.zero);
        GameObject secondEnemy = CreateActor("Second Floor Enemy", "enemies", Vector3.right);
        UnityCombatRulesBridge firstBridge = null;
        UnityCombatRulesBridge secondBridge = null;
        try
        {
            firstBridge = CreateBridge(firstHero, firstEnemy);
            CreatureId firstEnemyId = firstBridge.GetCreatureId(
                firstEnemy.GetComponent<CreatureComponent>()
            );
            Assert.That(
                firstBridge.Dispatch(
                    new ApplyConditionOp(
                        firstEnemyId,
                        SlowedRules.ConditionId,
                        1,
                        firstEnemyId,
                        SlowedRules.Source,
                        EffectDuration.Indefinite
                    )
                ),
                Is.TypeOf<ResolvedOpResult<ActiveEffectCreationOutcome>>()
            );
            firstBridge.ReleaseOwnership();
            firstBridge = null;
            DungeonActorSaveState firstEnemyState = DungeonActorStateAdapter.Capture(
                firstEnemy.GetComponent<ActionController>(),
                actor =>
                    actor == firstEnemy
                        ? DungeonRulesActorReference.Floor(0, reusedLocalId)
                        : heroReference
            );

            secondBridge = CreateBridge(secondHero, secondEnemy);
            CreatureId secondEnemyId = secondBridge.GetCreatureId(
                secondEnemy.GetComponent<CreatureComponent>()
            );
            Assert.That(
                secondBridge.Dispatch(
                    new ApplyConditionOp(
                        secondEnemyId,
                        SlowedRules.ConditionId,
                        1,
                        secondEnemyId,
                        SlowedRules.Source,
                        EffectDuration.Indefinite
                    )
                ),
                Is.TypeOf<ResolvedOpResult<ActiveEffectCreationOutcome>>()
            );
            secondBridge.ReleaseOwnership();
            secondBridge = null;
            DungeonActorSaveState secondEnemyState = DungeonActorStateAdapter.Capture(
                secondEnemy.GetComponent<ActionController>(),
                actor =>
                    actor == secondEnemy
                        ? DungeonRulesActorReference.Floor(1, reusedLocalId)
                        : heroReference
            );

            DungeonRulesEffectSaveState firstEffect = firstEnemyState.RulesEffects.Single();
            DungeonRulesEffectSaveState secondEffect = secondEnemyState.RulesEffects.Single();
            Assert.That(secondEffect.EffectId, Is.Not.EqualTo(firstEffect.EffectId));
            Assert.That(secondEffect.BindingId, Is.Not.EqualTo(firstEffect.BindingId));

            DungeonPartyMemberSaveState[] party =
            {
                new()
                {
                    RosterSlotId = heroReference.ActorId,
                    CreatureContentId = "hero-content",
                    CellX = 1,
                    CellZ = 1,
                    CurrentHitPoints = 10,
                    IsDefeated = false,
                    State = EmptyActorState(),
                },
            };
            DungeonRunSave run = DungeonRunSave
                .CreateNew(
                    party,
                    Floor(0, reusedLocalId, sourceDefeated: false, livingState: firstEnemyState)
                )
                .WithAddedAndSelectedFloor(
                    party,
                    Floor(1, reusedLocalId, sourceDefeated: false, livingState: secondEnemyState)
                );

            DungeonSaveResult<DungeonRunSave> parsed = DungeonSaveJson.Parse(
                DungeonSaveJson.Serialize(run),
                "memory"
            );
            Assert.That(parsed.IsSuccess, Is.True, parsed.Diagnostics.FirstOrDefault()?.Message);
        }
        finally
        {
            firstBridge?.ReleaseOwnership();
            secondBridge?.ReleaseOwnership();
            UnityEngine.Object.Destroy(firstHero);
            UnityEngine.Object.Destroy(firstEnemy);
            UnityEngine.Object.Destroy(secondHero);
            UnityEngine.Object.Destroy(secondEnemy);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator CrossFloorArrivalSaveLoadKeepsAbsentSourceDistinctFromReusedLocalId()
    {
        const string reusedLocalId = "encounter-shared/creature-0000";
        DungeonRulesActorReference heroReference = DungeonRulesActorReference.Party("hero");
        DungeonRulesActorReference priorSource = DungeonRulesActorReference.Floor(0, reusedLocalId);
        DungeonRulesActorReference currentEnemy = DungeonRulesActorReference.Floor(
            1,
            reusedLocalId
        );
        DungeonActorSaveState heroState = EmptyActorState();
        heroState.RulesEffects = new[]
        {
            new DungeonRulesEffectSaveState
            {
                EffectId = "cross-floor-slowed-effect",
                BindingId = "cross-floor-slowed-binding",
                DefinitionId = ConditionRules.DefinitionId.Value,
                SourceActor = priorSource,
                BindingOwnerActor = heroReference,
                RuleSource = "prior-floor-slowed",
                DurationKind = EffectDurationKind.Indefinite,
                BindingEnabled = true,
                StateKind = "condition",
                StatePayload = "{\"Condition\":\"slowed\",\"Value\":1}",
            },
        };
        DungeonPartyMemberSaveState[] arrivalParty =
        {
            new()
            {
                RosterSlotId = heroReference.ActorId,
                CreatureContentId = "hero-content",
                CellX = 1,
                CellZ = 1,
                CurrentHitPoints = 10,
                IsDefeated = false,
                State = heroState,
            },
        };
        DungeonRunSave arrived = DungeonRunSave
            .CreateNew(arrivalParty, Floor(0, reusedLocalId, sourceDefeated: true))
            .WithAddedAndSelectedFloor(
                arrivalParty,
                Floor(1, reusedLocalId, sourceDefeated: false)
            );
        DungeonSaveResult<DungeonRunSave> loaded = DungeonSaveJson.Parse(
            DungeonSaveJson.Serialize(arrived),
            "memory"
        );
        Assert.That(loaded.IsSuccess, Is.True, loaded.Diagnostics.FirstOrDefault()?.Message);
        DungeonRulesEffectSaveState loadedEffect = loaded
            .Value.Manifest.Party.Single()
            .State.RulesEffects.Single();
        Assert.That(loadedEffect.SourceActor, Is.EqualTo(priorSource));

        GameObject hero = CreateActor("Cross-Floor Hero", "players", Vector3.zero);
        GameObject enemy = CreateActor("Same Local ID Enemy", "enemies", Vector3.right);
        UnityCombatRulesBridge bridge = null;
        try
        {
            Func<DungeonRulesActorReference, GameObject> resolve = actor =>
                actor.Equals(heroReference) ? hero
                : actor.Equals(currentEnemy) ? enemy
                : null;
            DungeonActorStateAdapter.PrepareRestore(
                hero.GetComponent<ActionController>(),
                loaded.Value.Manifest.Party.Single().State,
                10,
                false,
                resolve
            )();
            DungeonActorStateAdapter.PrepareRestore(
                enemy.GetComponent<ActionController>(),
                EmptyActorState(),
                10,
                false,
                resolve
            )();
            Tile[,] tiles = new Tile[2, 1];
            tiles[0, 0] = new Tile();
            tiles[1, 0] = new Tile();
            tiles[0, 0].Occupants.Add(hero);
            tiles[1, 0].Occupants.Add(enemy);
            bridge = UnityCombatRulesBridge.Create(
                new[]
                {
                    hero.GetComponent<ActionController>(),
                    enemy.GetComponent<ActionController>(),
                },
                tiles,
                new ScriptedRollService(20, 10),
                "players"
            );
            yield return null;

            ActiveEffectInstance restored = bridge.Snapshot.ActiveEffects[
                new ActiveEffectId("cross-floor-slowed-effect")
            ];
            CreatureId enemyId = bridge.GetCreatureId(enemy.GetComponent<CreatureComponent>());
            Assert.That(restored.SourceCreature, Is.Not.EqualTo(enemyId));
            Assert.That(
                restored.SourceCreature.Value,
                Is.EqualTo($"dungeon-external:{priorSource.StableKey}")
            );

            bridge.ReleaseOwnership();
            bridge = null;
            DungeonActorSaveState recaptured = DungeonActorStateAdapter.Capture(
                hero.GetComponent<ActionController>(),
                actor => actor == hero ? heroReference : currentEnemy
            );
            Assert.That(recaptured.RulesEffects.Single().SourceActor, Is.EqualTo(priorSource));
            Assert.That(
                DungeonSaveJson.ParseActor(DungeonSaveJson.SerializeActor(recaptured)).IsSuccess,
                Is.True
            );
        }
        finally
        {
            bridge?.ReleaseOwnership();
            UnityEngine.Object.Destroy(hero);
            UnityEngine.Object.Destroy(enemy);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator RestoredLightCommitsBeforePresentationAndDoesNotDuplicate()
    {
        GameObject hero = CreateActor("Restored Light Hero", "players", Vector3.zero);
        GameObject enemy = CreateActor("Restored Light Enemy", "enemies", Vector3.right);
        UnityCombatRulesBridge bridge = null;
        try
        {
            DungeonActorSaveState saved = EmptyActorState();
            saved.RulesEffects = new[]
            {
                new DungeonRulesEffectSaveState
                {
                    EffectId = "saved-light-effect",
                    BindingId = "saved-light-binding",
                    DefinitionId = "spell-effect-light",
                    SourceActor = DungeonRulesActorReference.Party("hero"),
                    BindingOwnerActor = DungeonRulesActorReference.Party("hero"),
                    RuleSource = "light",
                    DurationKind = EffectDurationKind.Indefinite,
                    EffectStateVersion = 1,
                    BindingEnabled = true,
                    StateKind = "spell",
                    StatePayload =
                        "{\"Spell\":\"light\",\"Rank\":1,\"TargetActor\":{\"Scope\":0,\"FloorDepth\":-1,\"ActorId\":\"hero\"}}",
                },
            };
            Func<DungeonRulesActorReference, GameObject> resolve = actor =>
                actor.Equals(DungeonRulesActorReference.Party("hero")) ? hero
                : actor.Equals(DungeonRulesActorReference.Floor(0, "enemy")) ? enemy
                : null;
            DungeonActorStateAdapter.PrepareRestore(
                hero.GetComponent<ActionController>(),
                saved,
                10,
                false,
                resolve
            )();
            DungeonActorStateAdapter.PrepareRestore(
                enemy.GetComponent<ActionController>(),
                EmptyActorState(),
                10,
                false,
                resolve
            )();
            Tile[,] tiles = new Tile[2, 1];
            tiles[0, 0] = new Tile();
            tiles[1, 0] = new Tile();
            tiles[0, 0].Occupants.Add(hero);
            tiles[1, 0].Occupants.Add(enemy);

            bridge = UnityCombatRulesBridge.Create(
                new[]
                {
                    hero.GetComponent<ActionController>(),
                    enemy.GetComponent<ActionController>(),
                },
                tiles,
                new ScriptedRollService(20, 10),
                "players"
            );
            yield return null;

            Assert.That(bridge.Snapshot.ActiveEffects.Count(), Is.EqualTo(1));
            Assert.That(
                bridge.Snapshot.ActiveEffects.Contains(new ActiveEffectId("saved-light-effect")),
                Is.True
            );
            Assert.That(
                bridge
                    .Snapshot
                    .ActiveEffects[new ActiveEffectId("saved-light-effect")]
                    .EffectStateVersion,
                Is.EqualTo(new EffectStateVersion(1))
            );
            Assert.That(
                hero.GetComponentsInChildren<UnityEngine.Light>(true)
                    .Count(light => light.gameObject.name == "Spell Effect Light"),
                Is.EqualTo(1)
            );

            bridge.ReleaseOwnership();
            DungeonActorSaveState detached = DungeonActorStateAdapter.Capture(
                hero.GetComponent<ActionController>(),
                actor =>
                    actor == hero
                        ? DungeonRulesActorReference.Party("hero")
                        : DungeonRulesActorReference.Floor(0, "enemy")
            );
            Assert.That(detached.RulesEffects, Has.Length.EqualTo(1));
            bridge = UnityCombatRulesBridge.Create(
                new[]
                {
                    hero.GetComponent<ActionController>(),
                    enemy.GetComponent<ActionController>(),
                },
                tiles,
                new ScriptedRollService(20, 10),
                "players"
            );
            yield return null;

            Assert.That(bridge.Snapshot.ActiveEffects.Count(), Is.EqualTo(1));
            Assert.That(
                hero.GetComponentsInChildren<UnityEngine.Light>(true)
                    .Count(light => light.gameObject.name == "Spell Effect Light"),
                Is.EqualTo(1)
            );
        }
        finally
        {
            bridge?.ReleaseOwnership();
            UnityEngine.Object.Destroy(hero);
            UnityEngine.Object.Destroy(enemy);
        }
        yield return null;
    }

    private static GameObject CreateActor(string name, string teamName, Vector3 position)
    {
        GameObject actor = new(name);
        actor.transform.position = position;
        CreatureComponent creature = actor.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(10, 10);
        actor.AddComponent<Team>().Name = teamName;
        actor.AddComponent<DungeonRulesPersistencePlayModeController>();
        return actor;
    }

    private static UnityCombatRulesBridge CreateBridge(GameObject hero, GameObject enemy)
    {
        Tile[,] tiles = new Tile[2, 1];
        tiles[0, 0] = new Tile();
        tiles[1, 0] = new Tile();
        tiles[0, 0].Occupants.Add(hero);
        tiles[1, 0].Occupants.Add(enemy);
        return UnityCombatRulesBridge.Create(
            new[] { hero.GetComponent<ActionController>(), enemy.GetComponent<ActionController>() },
            tiles,
            new ScriptedRollService(20, 10),
            "players"
        );
    }

    private static DungeonActorSaveState EmptyActorState() =>
        new()
        {
            TemporaryHitPointSource = string.Empty,
            TemporaryHitPointImmunities = Array.Empty<string>(),
            RulesEffects = Array.Empty<DungeonRulesEffectSaveState>(),
            PreparedEffects = Array.Empty<DungeonPreparedEffectSaveState>(),
            SpellSlots = Array.Empty<DungeonSpellSlotSaveState>(),
            Equipment = new DungeonEquipmentSaveState
            {
                LeftHandId = string.Empty,
                RightHandId = string.Empty,
                ArmorId = string.Empty,
                Ammunition = Array.Empty<AmmoCount>(),
                UnloadedWeaponIds = Array.Empty<string>(),
            },
        };

    private static DungeonLevelDocument Floor(
        int depth,
        string reusedLocalId,
        bool sourceDefeated
    ) => Floor(depth, reusedLocalId, sourceDefeated, EmptyActorState());

    private static DungeonLevelDocument Floor(
        int depth,
        string reusedLocalId,
        bool sourceDefeated,
        DungeonActorSaveState livingState
    )
    {
        const string encounterId = "encounter-shared";
        DungeonEncounterPlan plan = new(
            encounterId,
            1,
            DungeonEncounterThreat.Low,
            40,
            new[] { new DungeonCell(2, 1) },
            new[] { "enemy-content" },
            sourceDefeated
        );
        DungeonRuntimeState runtime = new(
            Array.Empty<string>(),
            sourceDefeated ? new[] { encounterId } : Array.Empty<string>(),
            sourceDefeated ? new[] { reusedLocalId } : Array.Empty<string>(),
            sourceDefeated
                ? Array.Empty<DungeonCreatureRuntimeState>()
                : new[]
                {
                    new DungeonCreatureRuntimeState(
                        reusedLocalId,
                        "enemy-content",
                        encounterId,
                        new DungeonCell(2, 1),
                        10,
                        DungeonSaveJson.SerializeActor(livingState)
                    ),
                }
        );
        return new DungeonLevelDocument(
            new DungeonGenerationMetadata("test-generator", 42, depth, depth + 1),
            new[] { "###", "...", "###" },
            new[] { new DungeonRoom(1, 0, 1, 2, 1) },
            Array.Empty<DungeonDoor>(),
            Array.Empty<DungeonStair>(),
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1) },
            Array.Empty<DungeonObjectPlacement>(),
            new[] { plan },
            runtime
        );
    }
}
