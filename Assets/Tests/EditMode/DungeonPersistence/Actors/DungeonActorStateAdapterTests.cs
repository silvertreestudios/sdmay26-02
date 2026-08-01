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
        CreatureComponent effectSourceCreature =
            effectSourceObject.AddComponent<CreatureComponent>();
        effectSourceCreature.InitializeHealthBeforeEncounter(10, 10);
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
        source.Conditions.RestoreApplications(
            new[]
            {
                PersistedCondition(
                    "off-guard-save",
                    ConditionRuleDefinitions.OffGuard,
                    effectSourceObject,
                    RuleSource.FromSlug("shared-test-source"),
                    ConditionMarkerState.Instance,
                    EffectDuration.Indefinite,
                    3
                ),
                PersistedCondition(
                    "slowed-save",
                    ConditionRuleDefinitions.Slowed,
                    effectSourceObject,
                    RuleSource.FromSlug("shared-test-source"),
                    new SlowedConditionState(2),
                    EffectDuration.Rounds(3),
                    4,
                    new ConditionTimingSnapshot(2, false)
                ),
            }
        );
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
        UnityCombatRulesBridge sourceBridge = UnityCombatRulesBridge.Create(
            new[] { source.Controller, effectSource },
            CreateTiles()
        );

        DungeonActorSaveState captured = DungeonActorStateAdapter.Capture(
            source.Controller,
            actor =>
                actor == effectSourceObject ? "source-slot"
                : actor == sourceObject ? "actor-slot"
                : throw new InvalidOperationException()
        );
        sourceBridge.ReleaseOwnership();
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
        UnityCombatRulesBridge restoredBridge = UnityCombatRulesBridge.Create(
            new[] { restored.Controller, effectSource },
            CreateTiles()
        );

        Assert.That(restored.Creature.Health.Current, Is.EqualTo(7));
        Assert.That(restored.Creature.Health.Temporary, Is.EqualTo(4));
        Assert.That(restored.Creature.Health.TemporarySource.Slug, Is.EqualTo("blessing"));
        Assert.That(
            restored.Creature.GetTempHpImmunitySources(),
            Is.EquivalentTo(new[] { "ward" })
        );
        Assert.That(
            restored.Conditions.ActiveConditionNames,
            Is.EquivalentTo(new[] { "off-guard", "slowed" })
        );
        ConditionApplicationSnapshot[] restoredConditions = restored
            .Conditions.CaptureApplications()
            .ToArray();
        Assert.That(restoredConditions, Has.Length.EqualTo(2));
        Assert.That(
            restoredConditions.Select(entry => entry.Source.Slug).Distinct().Count(),
            Is.EqualTo(1)
        );
        ConditionSelection<SlowedConditionState> slowed = ConditionSelectors.TryGetSlowed(
            restoredBridge.Snapshot,
            restoredBridge.GetCreatureId(restored.Creature),
            out var selectedSlowed
        )
            ? selectedSlowed
            : throw new AssertionException("Restored Slowed condition was unavailable.");
        Assert.That(slowed.State.Value, Is.EqualTo(2));
        Assert.That(
            restoredBridge.Snapshot.ActiveEffectTimings[slowed.EffectId].RemainingBoundaries,
            Is.EqualTo(2)
        );
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
    public void ConditionPersistenceRoundTripsExactTypedLifecycleAndStableElection()
    {
        effectSourceObject = new GameObject("Condition Source");
        DungeonPersistenceTestActionController effectSource =
            effectSourceObject.AddComponent<DungeonPersistenceTestActionController>();
        CreatureComponent effectCreature = effectSourceObject.AddComponent<CreatureComponent>();
        effectCreature.InitializeHealthBeforeEncounter(10, 10);
        SourceFixture source = CreateFixture("Condition Owner", out sourceObject);
        RuleSource shared = RuleSource.FromSlug("shared-condition-source");
        source.Conditions.RestoreApplications(
            new[]
            {
                PersistedCondition(
                    "marker",
                    ConditionRuleDefinitions.OffGuard,
                    effectSourceObject,
                    shared,
                    ConditionMarkerState.Instance,
                    EffectDuration.Indefinite,
                    1
                ),
                PersistedCondition(
                    "slowed-first",
                    ConditionRuleDefinitions.Slowed,
                    effectSourceObject,
                    RuleSource.FromSlug("slowed-first-source"),
                    new SlowedConditionState(2),
                    EffectDuration.Rounds(4),
                    2,
                    new ConditionTimingSnapshot(3, false),
                    version: 5
                ),
                PersistedCondition(
                    "slowed-equal",
                    ConditionRuleDefinitions.Slowed,
                    effectSourceObject,
                    RuleSource.FromSlug("slowed-equal-source"),
                    new SlowedConditionState(2),
                    EffectDuration.Minutes(1),
                    3,
                    new ConditionTimingSnapshot(7, false),
                    version: 6
                ),
                PersistedCondition(
                    "stunned-valued",
                    ConditionRuleDefinitions.Stunned,
                    effectSourceObject,
                    shared,
                    new ValuedStunnedConditionState(4),
                    EffectDuration.Indefinite,
                    4
                ),
                PersistedCondition(
                    "stunned-duration",
                    ConditionRuleDefinitions.Stunned,
                    effectSourceObject,
                    shared,
                    DurationOnlyStunnedConditionState.Instance,
                    EffectDuration.Encounter,
                    5,
                    new ConditionTimingSnapshot(0, true)
                ),
                PersistedCondition(
                    "quickened-restricted",
                    ConditionRuleDefinitions.Quickened,
                    effectSourceObject,
                    shared,
                    new QuickenedConditionState(
                        new[] { new ActionDefinitionId("strike"), new ActionDefinitionId("stride") }
                    ),
                    EffectDuration.Rounds(2),
                    6,
                    new ConditionTimingSnapshot(1, false)
                ),
                PersistedCondition(
                    "quickened-unrestricted",
                    ConditionRuleDefinitions.Quickened,
                    effectSourceObject,
                    shared,
                    QuickenedConditionState.Unrestricted,
                    EffectDuration.Indefinite,
                    7
                ),
                PersistedCondition(
                    "expired-marker",
                    ConditionRuleDefinitions.Fatigued,
                    effectSourceObject,
                    shared,
                    ConditionMarkerState.Instance,
                    EffectDuration.Rounds(1),
                    8,
                    status: ActiveEffectStatus.Expired,
                    version: 9,
                    bindingEnabled: false
                ),
            }
        );
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { source.Controller, effectSource },
            CreateTiles()
        );
        Assert.That(
            ConditionSelectors.TryGetSlowed(
                first.Snapshot,
                first.GetCreatureId(source.Creature),
                out var selectedBefore
            ),
            Is.True
        );
        Assert.That(selectedBefore.EffectId.Value, Is.EqualTo("effect-slowed-first"));
        DungeonActorSaveState captured = DungeonActorStateAdapter.Capture(
            source.Controller,
            actor => actor == effectSourceObject ? "source-slot" : "owner-slot"
        );
        DungeonSaveResult<DungeonActorSaveState> parsed = DungeonSaveJson.ParseActor(
            DungeonSaveJson.SerializeActor(captured)
        );
        Assert.That(parsed.IsSuccess, Is.True);
        first.ReleaseOwnership();

        SourceFixture restored = CreateFixture("Restored Condition Owner", out restoredObject);
        Action apply = DungeonActorStateAdapter.PrepareRestore(
            restored.Controller,
            parsed.Value,
            12,
            false,
            actorId => actorId == "source-slot" ? effectSourceObject : restoredObject
        );
        apply();
        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new[] { restored.Controller, effectSource },
            CreateTiles()
        );
        DungeonActorSaveState recaptured = DungeonActorStateAdapter.Capture(
            restored.Controller,
            actor => actor == effectSourceObject ? "source-slot" : "owner-slot"
        );

        Assert.That(recaptured.Conditions, Has.Length.EqualTo(captured.Conditions.Length));
        Assert.That(
            recaptured.Conditions.Select(item => JsonUtility.ToJson(item)),
            Is.EqualTo(captured.Conditions.Select(item => JsonUtility.ToJson(item)))
        );
        Assert.That(
            ConditionSelectors.TryGetSlowed(
                second.Snapshot,
                second.GetCreatureId(restored.Creature),
                out var selectedAfter
            ),
            Is.True
        );
        Assert.That(selectedAfter.EffectId, Is.EqualTo(selectedBefore.EffectId));
    }

    [Test]
    public void CaptureResolvesEquivalentSerializedEquipmentByStableName()
    {
        SourceFixture source = CreateFixture("Source", out sourceObject);
        UnityEngine.Object.DestroyImmediate(source.Conditions);
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
        UnityEngine.Object.DestroyImmediate(source.Conditions);
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

    private static ConditionApplicationSnapshot PersistedCondition(
        string identity,
        RuleDefinitionId definition,
        GameObject sourceCreature,
        RuleSource source,
        IEffectState state,
        EffectDuration duration,
        long creationOrder,
        ConditionTimingSnapshot timing = null,
        ActiveEffectStatus status = ActiveEffectStatus.Active,
        long version = 2,
        bool bindingEnabled = true
    ) =>
        new ConditionApplicationSnapshot(
            new ActiveEffectId($"effect-{identity}"),
            new BindingId($"binding-{identity}"),
            definition,
            sourceCreature,
            source,
            duration,
            new EffectStateVersion(version),
            state,
            status,
            creationOrder,
            bindingEnabled,
            timing
        );

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
