using System;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;

public sealed class DungeonPersistenceTestActionController : ActionController
{
    public override void EndTurn() { }
}

public sealed class DungeonActorStateAdapterTests
{
    private GameObject sourceObject;
    private GameObject restoredObject;
    private GameObject effectSourceObject;

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(sourceObject);
        UnityEngine.Object.DestroyImmediate(restoredObject);
        UnityEngine.Object.DestroyImmediate(effectSourceObject);
    }

    [Test]
    public void CaptureAndRestorePreservesMutableActorStateWithoutSerializingInventory()
    {
        effectSourceObject = new GameObject("Effect Source");
        DungeonPersistenceTestActionController effectSource =
            effectSourceObject.AddComponent<DungeonPersistenceTestActionController>();
        SourceFixture source = CreateFixture("Source", out sourceObject);
        source.Creature.InitializeHealthBeforeEncounter(
            new HealthState(
                7,
                12,
                4,
                RuleSource.FromSlug("blessing"),
                new[] { RuleSource.FromSlug("ward") }
            )
        );
        ConditionSource sharedConditionSource = new();
        source.Conditions.Add("Off-Guard", sharedConditionSource);
        source.Conditions.Add("Slowed", sharedConditionSource);
        SpellEffectController
            .GetOrAdd(sourceObject)
            .AddOrRefresh(new BlessSpellEffect(effectSourceObject));
        source.Creature.Prepared.RestoreActiveEffects(
            new[] { new ActivePf2eEffect("Rage", "rage", "effect-rage") }
        );
        source.Creature.equippedRightHand = source.Weapons[1];
        source.Creature.ammunition = new()
        {
            new AmmoCount { ammoName = "bolt", quantity = 3 },
        };
        source.Creature.unloadedWeapons = new() { "Heavy Crossbow" };

        DungeonActorSaveState captured = DungeonActorStateAdapter.Capture(
            source.Controller,
            actor =>
                actor == effectSourceObject ? "source-slot" : throw new InvalidOperationException()
        );
        SourceFixture restored = CreateFixture("Restored", out restoredObject);
        restored.Creature.Build = new CharacterBuild();
        restored.Creature.Prepared = null;
        Action apply = DungeonActorStateAdapter.PrepareRestore(
            restored.Controller,
            captured,
            currentHitPoints: 7,
            isDefeated: false,
            actorId => actorId == "source-slot" ? effectSourceObject : null
        );

        apply();

        Assert.That(restored.Creature.Health.Current, Is.EqualTo(7));
        Assert.That(restored.Creature.Health.Temporary, Is.EqualTo(4));
        Assert.That(restored.Creature.Health.TemporarySource.Slug, Is.EqualTo("blessing"));
        Assert.That(
            restored.Creature.GetTempHpImmunitySources(),
            Is.EquivalentTo(new[] { "ward" })
        );
        Assert.That(
            restored.Conditions.GetConditionNames(),
            Is.EquivalentTo(new[] { "Off-Guard", "Slowed" })
        );
        ConditionApplicationSnapshot[] restoredConditions = restored
            .Conditions.CaptureApplications()
            .ToArray();
        Assert.That(restoredConditions, Has.Length.EqualTo(2));
        Assert.That(restoredConditions[0].Source, Is.SameAs(restoredConditions[1].Source));
        BlessSpellEffect bless = restoredObject
            .GetComponent<SpellEffectController>()
            .Effects.OfType<BlessSpellEffect>()
            .Single();
        Assert.That(bless.Source, Is.SameAs(effectSourceObject));
        Assert.That(bless.RemainingTargetTurnStarts, Is.EqualTo(10));
        Assert.That(restored.Creature.Prepared.HasActiveEffect("rage"), Is.True);
        Assert.That(restored.Creature.equippedRightHand, Is.SameAs(restored.Weapons[1]));
        Assert.That(restored.Creature.GetAmmoQuantity("bolt"), Is.EqualTo(3));
        Assert.That(restored.Creature.IsWeaponLoaded(restored.Weapons[1]), Is.False);
    }

    [Test]
    public void CaptureResolvesEquivalentSerializedEquipmentByStableName()
    {
        SourceFixture source = CreateFixture("Source", out sourceObject);
        EquipmentArmor authoredArmor = new() { name = "Explorer's Clothing" };
        source.Creature.armor = new() { authoredArmor };
        source.Creature.equippedRightHand = new EquipmentWeapon
        {
            name = "heavy crossbow",
            reload = "1",
            ammo = "bolt",
        };
        source.Creature.equippedArmor = new EquipmentArmor { name = "EXPLORER'S CLOTHING" };
        source.Creature.equippedLeftHand = new EquipmentWeapon { name = string.Empty };

        DungeonActorSaveState captured = DungeonActorStateAdapter.Capture(
            source.Controller,
            _ => "unused"
        );

        Assert.That(captured.Equipment.RightHandId, Is.EqualTo("Heavy Crossbow"));
        Assert.That(captured.Equipment.ArmorId, Is.EqualTo("Explorer's Clothing"));
        Assert.That(captured.Equipment.LeftHandId, Is.Empty);
    }

    [Test]
    public void RageAutosaveRoundTripRemovesTemporaryHitPointsWithoutRestoringTheEffect()
    {
        sourceObject = CreatureJsonConverter.CreateFromFile("DataFiles/playerCharacters/Torgrim");
        CreatureComponent sourceCreature = sourceObject.GetComponent<CreatureComponent>();
        sourceObject.AddComponent<Conditions>();
        DungeonPersistenceTestActionController sourceController =
            sourceObject.AddComponent<DungeonPersistenceTestActionController>();
        effectSourceObject = new GameObject("Encounter Opponent");
        Team opponentTeam = effectSourceObject.AddComponent<Team>();
        opponentTeam.Name = "enemies";
        CreatureComponent opponent = effectSourceObject.AddComponent<CreatureComponent>();
        opponent.InitializeHealthBeforeEncounter(10, 10);
        DungeonPersistenceTestActionController opponentController =
            effectSourceObject.AddComponent<DungeonPersistenceTestActionController>();
        effectSourceObject.transform.position = Vector3.right;
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { sourceController, opponentController },
            CreateTiles(),
            new ScriptedRollService(20, 10)
        );
        CreatureId actor = bridge.GetCreatureId(sourceCreature);
        bridge.BeginTurn(actor, 3);
        if (!RageRules.GetActiveRollOptions(bridge.Snapshot, actor).Contains("self:effect:rage"))
            Assert.That(
                bridge.Dispatch(new RageActionOp(actor)),
                Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
            );

        DungeonActorSaveState captured = DungeonActorStateAdapter.Capture(
            sourceController,
            _ => throw new InvalidOperationException()
        );
        DungeonSaveResult<DungeonActorSaveState> parsed = DungeonSaveJson.ParseActor(
            DungeonSaveJson.SerializeActor(captured)
        );
        Assert.That(parsed.IsSuccess, Is.True);
        Assert.That(parsed.Value.RageWasActive, Is.True);

        restoredObject = CreatureJsonConverter.CreateFromFile("DataFiles/playerCharacters/Torgrim");
        CreatureComponent restoredCreature = restoredObject.GetComponent<CreatureComponent>();
        DungeonPersistenceTestActionController restoredController =
            restoredObject.AddComponent<DungeonPersistenceTestActionController>();
        Action apply = DungeonActorStateAdapter.PrepareRestore(
            restoredController,
            parsed.Value,
            sourceCreature.Health.Current,
            isDefeated: false,
            _ => null
        );

        apply();

        Assert.That(restoredCreature.Health.Temporary, Is.Zero);
        Assert.That(restoredCreature.Health.TemporarySource.IsEmpty, Is.True);
        Assert.That(restoredCreature.HasTempHpImmunity("rage"), Is.True);
        Assert.That(restoredCreature.Prepared.HasActiveEffect("rage"), Is.False);
    }

    [Test]
    public void PrepareRestoreRejectsEquipmentThatAuthoredActorDoesNotContain()
    {
        SourceFixture source = CreateFixture("Source", out sourceObject);
        DungeonActorSaveState captured = DungeonActorStateAdapter.Capture(
            source.Controller,
            _ => "unused"
        );
        SourceFixture restored = CreateFixture("Restored", out restoredObject);
        restored.Creature.weapons = new();

        Assert.Throws<InvalidOperationException>(() =>
            DungeonActorStateAdapter.PrepareRestore(
                restored.Controller,
                new DungeonActorSaveState
                {
                    TemporaryHitPoints = captured.TemporaryHitPoints,
                    TemporaryHitPointSource = captured.TemporaryHitPointSource,
                    TemporaryHitPointImmunities = captured.TemporaryHitPointImmunities,
                    Conditions = captured.Conditions,
                    TimedEffects = captured.TimedEffects,
                    PreparedEffects = captured.PreparedEffects,
                    Equipment = new DungeonEquipmentSaveState
                    {
                        LeftHandId = string.Empty,
                        RightHandId = "Missing Weapon",
                        ArmorId = string.Empty,
                        Ammunition = captured.Equipment.Ammunition,
                        UnloadedWeaponIds = captured.Equipment.UnloadedWeaponIds,
                    },
                },
                12,
                false,
                _ => null
            )
        );
    }

    private static SourceFixture CreateFixture(string name, out GameObject gameObject)
    {
        gameObject = new GameObject(name);
        DungeonPersistenceTestActionController controller =
            gameObject.AddComponent<DungeonPersistenceTestActionController>();
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(12, 12);
        EquipmentWeapon[] weapons =
        {
            new()
            {
                name = "Dagger",
                reload = string.Empty,
                ammo = string.Empty,
            },
            new()
            {
                name = "Heavy Crossbow",
                reload = "1",
                ammo = "bolt",
            },
        };
        creature.weapons = weapons.ToList();
        creature.ammunition = new()
        {
            new AmmoCount { ammoName = "bolt", quantity = 5 },
        };
        creature.Prepared = new PreparedCharacter();
        Conditions conditions = gameObject.AddComponent<Conditions>();
        return new SourceFixture(controller, creature, conditions, weapons);
    }

    private static Tile[,] CreateTiles()
    {
        Tile[,] tiles = new Tile[2, 1];
        tiles[0, 0] = new Tile();
        tiles[1, 0] = new Tile();
        return tiles;
    }

    private sealed class SourceFixture
    {
        internal SourceFixture(
            DungeonPersistenceTestActionController controller,
            CreatureComponent creature,
            Conditions conditions,
            EquipmentWeapon[] weapons
        )
        {
            Controller = controller;
            Creature = creature;
            Conditions = conditions;
            Weapons = weapons;
        }

        internal DungeonPersistenceTestActionController Controller { get; }
        internal CreatureComponent Creature { get; }
        internal Conditions Conditions { get; }
        internal EquipmentWeapon[] Weapons { get; }
    }
}
