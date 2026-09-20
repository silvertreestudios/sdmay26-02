using System;
using System.Collections;
using System.Linq;
using Game.Creature;
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
                    SourceActorId = "hero",
                    BindingOwnerActorId = "hero",
                    RuleSource = "light",
                    DurationKind = EffectDurationKind.Indefinite,
                    EffectStateVersion = 1,
                    BindingEnabled = true,
                    StateKind = "spell",
                    StatePayload = "{\"Spell\":\"light\",\"Rank\":1,\"TargetActorId\":\"hero\"}",
                },
            };
            Func<string, GameObject> resolve = actorId =>
                actorId == "hero" ? hero
                : actorId == "enemy" ? enemy
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
                actor => actor == hero ? "hero" : "enemy"
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

    private static DungeonActorSaveState EmptyActorState() =>
        new()
        {
            TemporaryHitPointSource = string.Empty,
            TemporaryHitPointImmunities = Array.Empty<string>(),
            RulesEffects = Array.Empty<DungeonRulesEffectSaveState>(),
            PreparedEffects = Array.Empty<DungeonPreparedEffectSaveState>(),
            Equipment = new DungeonEquipmentSaveState
            {
                LeftHandId = string.Empty,
                RightHandId = string.Empty,
                ArmorId = string.Empty,
                Ammunition = Array.Empty<AmmoCount>(),
                UnloadedWeaponIds = Array.Empty<string>(),
            },
        };
}
