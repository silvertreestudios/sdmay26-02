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
    private GameObject restoredOpponentObject;
    private UnityCombatRulesBridge activeBridge;

    [TearDown]
    public void TearDown()
    {
        activeBridge?.ReleaseOwnership();
        UnityEngine.Object.DestroyImmediate(sourceObject);
        UnityEngine.Object.DestroyImmediate(restoredObject);
        UnityEngine.Object.DestroyImmediate(effectSourceObject);
        UnityEngine.Object.DestroyImmediate(restoredOpponentObject);
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
        Assert.That(captured.RulesEffects, Is.Empty);
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
    public void RulesEffectsRoundTripPreservesRageLightConditionsTimingAndPresentationRepeatedly()
    {
        sourceObject = CreatureJsonConverter.CreateFromFile("DataFiles/playerCharacters/Torgrim");
        CreatureComponent sourceCreature = sourceObject.GetComponent<CreatureComponent>();
        sourceCreature.Prepared.OwnedItems.RemoveAll(item =>
            string.Equals(item.Item.Slug, "quick-tempered", StringComparison.OrdinalIgnoreCase)
        );
        sourceCreature.Prepared.RollOptions.Remove("feat:quick-tempered");
        SpellReference light = new(new SpellId("light"), 1);
        sourceCreature.Prepared.SpellBook = new PreparedSpellBook(
            new[] { PreparedSpellEntry.Cantrip(light) },
            Array.Empty<PreparedSpellSlotPool>(),
            0
        );
        sourceObject.AddComponent<Conditions>();
        sourceObject.AddComponent<Team>().Name = "players";
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
        activeBridge = UnityCombatRulesBridge.Create(
            new ActionController[] { sourceController, opponentController },
            CreateTiles(),
            new ScriptedRollService(20, 10),
            "players"
        );
        CreatureId actor = activeBridge.GetCreatureId(sourceCreature);
        CreatureId enemy = activeBridge.GetCreatureId(opponent);
        activeBridge.BeginTurn(actor, 3);
        Assert.That(
            activeBridge.Dispatch(new RageActionOp(actor)),
            Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
        );
        Assert.That(
            activeBridge.Dispatch(
                new ApplyConditionOp(
                    actor,
                    SlowedRules.ConditionId,
                    1,
                    actor,
                    RuleSource.FromSlug("self-slowed"),
                    EffectDuration.Indefinite
                )
            ),
            Is.TypeOf<ResolvedOpResult<ActiveEffectCreationOutcome>>()
        );
        Assert.That(
            activeBridge.Dispatch(
                new ApplyConditionOp(
                    actor,
                    SlowedRules.ConditionId,
                    2,
                    enemy,
                    RuleSource.FromSlug("enemy-slowed"),
                    EffectDuration.Rounds(2)
                )
            ),
            Is.TypeOf<ResolvedOpResult<ActiveEffectCreationOutcome>>()
        );
        Assert.That(
            activeBridge.Dispatch(
                new CastSpellActionOp(
                    actor,
                    light,
                    new SpellActionVariant(2),
                    SpellCastSelection.Empty
                )
            ),
            Is.TypeOf<ResolvedOpResult<CastSpellOutcome>>()
        );
        activeBridge.BeginTurn(actor, 1);

        Func<GameObject, string> identify = value =>
            value == sourceObject ? "hero"
            : value == effectSourceObject ? "enemy"
            : throw new InvalidOperationException();
        DungeonActorSaveState captured = DungeonActorStateAdapter.Capture(
            sourceController,
            identify
        );
        DungeonActorSaveState capturedOpponent = DungeonActorStateAdapter.Capture(
            opponentController,
            identify
        );
        DungeonSaveResult<DungeonActorSaveState> parsed = DungeonSaveJson.ParseActor(
            DungeonSaveJson.SerializeActor(captured)
        );
        Assert.That(parsed.IsSuccess, Is.True);
        Assert.That(parsed.Value.RulesEffects, Has.Length.EqualTo(4));
        DungeonRulesEffectSaveState slowedTwo = parsed.Value.RulesEffects.Single(effect =>
            effect.StateKind == "condition" && effect.StatePayload.Contains("\"Value\":2")
        );
        int slowedTwoRemaining = slowedTwo.RemainingBoundaries;
        string slowedTwoEffectId = slowedTwo.EffectId;
        Assert.That(slowedTwo.SourceActorId, Is.EqualTo("enemy"));
        Assert.That(slowedTwo.BindingOwnerActorId, Is.EqualTo("hero"));
        Assert.That(slowedTwo.DurationKind, Is.EqualTo(EffectDurationKind.Rounds));
        Assert.That(slowedTwo.DurationAmount, Is.EqualTo(2));
        Assert.That(slowedTwo.EffectStateVersion, Is.EqualTo(0));
        Assert.That(slowedTwoRemaining, Is.EqualTo(1));

        restoredObject = CreatureJsonConverter.CreateFromFile("DataFiles/playerCharacters/Torgrim");
        CreatureComponent restoredCreature = restoredObject.GetComponent<CreatureComponent>();
        restoredObject.AddComponent<Conditions>();
        restoredObject.AddComponent<Team>().Name = "players";
        DungeonPersistenceTestActionController restoredController =
            restoredObject.AddComponent<DungeonPersistenceTestActionController>();
        restoredOpponentObject = new GameObject("Restored Encounter Opponent");
        restoredOpponentObject.transform.position = Vector3.right;
        restoredOpponentObject.AddComponent<Team>().Name = "enemies";
        CreatureComponent restoredOpponent =
            restoredOpponentObject.AddComponent<CreatureComponent>();
        restoredOpponent.InitializeHealthBeforeEncounter(10, 10);
        DungeonPersistenceTestActionController restoredOpponentController =
            restoredOpponentObject.AddComponent<DungeonPersistenceTestActionController>();
        Func<string, GameObject> resolve = actorId =>
            actorId == "hero" ? restoredObject
            : actorId == "enemy" ? restoredOpponentObject
            : null;
        Action apply = DungeonActorStateAdapter.PrepareRestore(
            restoredController,
            parsed.Value,
            sourceCreature.Health.Current,
            isDefeated: false,
            resolve
        );
        Action applyOpponent = DungeonActorStateAdapter.PrepareRestore(
            restoredOpponentController,
            capturedOpponent,
            10,
            isDefeated: false,
            resolve
        );

        apply();
        applyOpponent();
        activeBridge.ReleaseOwnership();
        activeBridge = UnityCombatRulesBridge.Create(
            new ActionController[] { restoredController, restoredOpponentController },
            CreateTiles(),
            new ScriptedRollService(20, 10),
            "players"
        );
        CreatureId restoredActor = activeBridge.GetCreatureId(restoredCreature);

        Assert.That(activeBridge.Snapshot.ActiveEffects.Count(), Is.EqualTo(4));
        Assert.That(
            activeBridge.Snapshot.ActiveEffects.Select(pair => pair.Key.Value),
            Is.EquivalentTo(parsed.Value.RulesEffects.Select(effect => effect.EffectId))
        );
        Assert.That(
            activeBridge
                .Snapshot.RuleBindings.Select(pair => pair.Value)
                .Where(binding => binding.EffectId.HasValue)
                .Select(binding => binding.Id.Value),
            Is.EquivalentTo(parsed.Value.RulesEffects.Select(effect => effect.BindingId))
        );
        Assert.That(
            activeBridge
                .Snapshot.RuleBindings.Select(pair => pair.Value)
                .Count(binding => binding.EffectId.HasValue),
            Is.EqualTo(4)
        );
        Assert.That(RageRules.IsRaging(activeBridge.Snapshot, restoredActor), Is.True);
        Assert.That(restoredCreature.Health.Temporary, Is.EqualTo(sourceCreature.Health.Temporary));
        Assert.That(restoredCreature.HasTempHpImmunity("rage"), Is.False);
        Assert.That(
            ConditionRules.GetValue(activeBridge.Snapshot, restoredActor, SlowedRules.ConditionId),
            Is.EqualTo(2)
        );
        Assert.That(
            activeBridge
                .Snapshot
                .ActiveEffectTimings[new ActiveEffectId(slowedTwoEffectId)]
                .RemainingBoundaries,
            Is.EqualTo(slowedTwoRemaining)
        );
        Assert.That(
            restoredObject
                .GetComponentsInChildren<UnityEngine.Light>(true)
                .Count(light => light.gameObject.name == "Spell Effect Light"),
            Is.EqualTo(1)
        );

        DungeonActorSaveState secondCapture = DungeonActorStateAdapter.Capture(
            restoredController,
            value => value == restoredObject ? "hero" : "enemy"
        );
        activeBridge.ReleaseOwnership();
        DungeonActorStateAdapter.PrepareRestore(
            restoredController,
            secondCapture,
            restoredCreature.Health.Current,
            isDefeated: false,
            resolve
        )();
        DungeonActorStateAdapter.PrepareRestore(
            restoredOpponentController,
            capturedOpponent,
            restoredOpponent.Health.Current,
            isDefeated: false,
            resolve
        )();
        activeBridge = UnityCombatRulesBridge.Create(
            new ActionController[] { restoredController, restoredOpponentController },
            CreateTiles(),
            new ScriptedRollService(20, 10),
            "players"
        );
        Assert.That(activeBridge.Snapshot.ActiveEffects.Count(), Is.EqualTo(4));
        Assert.That(
            restoredObject
                .GetComponentsInChildren<UnityEngine.Light>(true)
                .Count(light => light.gameObject.name == "Spell Effect Light"),
            Is.EqualTo(1)
        );
        CreatureId restoredEnemy = activeBridge.GetCreatureId(restoredOpponent);
        restoredActor = activeBridge.GetCreatureId(restoredCreature);
        ActiveEffectInstance restoredSlowed = activeBridge.Snapshot.ActiveEffects[
            new ActiveEffectId(slowedTwoEffectId)
        ];
        ActiveRuleBinding restoredSlowedBinding = activeBridge
            .Snapshot.RuleBindings.Select(pair => pair.Value)
            .Single(binding => binding.EffectId == restoredSlowed.Id);
        Assert.That(restoredSlowed.SourceCreature, Is.EqualTo(restoredEnemy));
        Assert.That(restoredSlowedBinding.Owner, Is.EqualTo(restoredActor));
        Assert.That(restoredSlowedBinding.Id.Value, Is.EqualTo(slowedTwo.BindingId));
        Assert.That(
            restoredSlowed.EffectStateVersion.Value,
            Is.EqualTo(slowedTwo.EffectStateVersion)
        );
        activeBridge.BeginTurn(restoredEnemy, 3);
        Assert.That(
            activeBridge.Snapshot.ActiveEffects.Contains(new ActiveEffectId(slowedTwoEffectId)),
            Is.False
        );
        Assert.That(
            ConditionRules.GetValue(activeBridge.Snapshot, restoredActor, SlowedRules.ConditionId),
            Is.EqualTo(1)
        );
        Assert.That(
            activeBridge.Dispatch(new EndRageOp(restoredActor)),
            Is.TypeOf<ResolvedOpResult<RageEndOutcome>>()
        );
        Assert.That(RageRules.IsRaging(activeBridge.Snapshot, restoredActor), Is.False);
        Assert.That(restoredCreature.Health.Temporary, Is.Zero);
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
                    RulesEffects = captured.RulesEffects,
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
        creature.Prepared = new PreparedCharacter(new CharacterBuild());
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
