using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Strike;
using Game.Strikes;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class RulesStrikeUnityTests
{
    private readonly List<GameObject> created = new();
    private int damageEventCount;
    private int missEventCount;

    [TearDown]
    public void TearDown()
    {
        OnDamageDealt.RemoveListener(CountDamage);
        OnAttackMiss.RemoveListener(CountMiss);
        foreach (GameObject gameObject in created)
        {
            if (gameObject != null)
                Object.DestroyImmediate(gameObject);
        }
        created.Clear();
        damageEventCount = 0;
        missEventCount = 0;
        Pf2eItemCatalog.ResetForTests();
    }

    [TestCase(2, false)]
    [TestCase(0, true)]
    public void UnityStrikeCaptureReturnsBaseArmorClassForContextualDefense(
        int coverBonus,
        bool offGuard
    )
    {
        CreatureComponent attacker = CreateCreature("Base AC Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature("Base AC Target", "enemies", 20, 15);
        CreatureId actorId = new CreatureId("base-ac-attacker");
        CreatureId targetId = new CreatureId("base-ac-target");
        UnityStrikeContext context = new UnityStrikeContext(
            new Dictionary<CreatureId, CreatureComponent>
            {
                [actorId] = attacker,
                [targetId] = target,
            },
            CreateTiles(2)
        );
        StrikeItemDefinition item = new StrikeItemDefinition(
            new ItemId("base-ac-item"),
            new ItemDefinitionId("base-ac-item"),
            "Base AC Item",
            "sword",
            "martial",
            Array.Empty<Trait>(),
            0,
            new[] { new TypedDamageDice(new DiceExpression(1, 6), "slashing", "Base AC") },
            Array.Empty<TypedFlatDamage>(),
            5,
            0,
            0,
            StrikeAmmunitionRequirement.None
        );

        StrikeResolutionData data = context.Capture(
            new InMemoryRulesStore().Snapshot,
            actorId,
            item,
            targetId,
            (LegalStrikeTargetingOutcome)StrikeTargetingOutcome.Legal(5, 0, coverBonus, offGuard)
        );

        Assert.That(data.ArmorClass, Is.EqualTo(15));
    }

    [Test]
    public void CatalogExtractionInstallsStableWeaponAndUnarmedActionsOnce()
    {
        CreatureComponent lena = Load("DataFiles/playerCharacters/Lena");
        TestActionController controller = lena.gameObject.AddComponent<TestActionController>();
        UnityCombatRulesBridge.Create(
            new[] { controller },
            CreateTiles(1),
            new ScriptedRollService()
        );

        List<RulesStrikeAction> first = controller
            .GetActions()
            .OfType<RulesStrikeAction>()
            .ToList();
        lena.InitializeRuntimeActions();
        List<RulesStrikeAction> second = controller
            .GetActions()
            .OfType<RulesStrikeAction>()
            .ToList();

        Assert.That(first.Select(action => action.ActionName), Does.Contain("Unarmed Strike"));
        Assert.That(first.Select(action => action.ActionName), Does.Contain("Dogslicer"));
        Assert.That(first.Select(action => action.ActionName), Does.Contain("Shortbow"));
        Assert.That(second, Has.Count.EqualTo(first.Count));
        Assert.That(
            second.Select(action => action.Item.Item).Distinct().Count(),
            Is.EqualTo(second.Count)
        );
        RulesStrikeAction shortbow = second.Single(action => action.ActionName == "Shortbow");
        Assert.That(shortbow.IsRanged, Is.True);
        Assert.That(shortbow.Item.RangeIncrementFeet, Is.EqualTo(60));
        Assert.That(lena.GetAmmoQuantity("arrows"), Is.EqualTo(20));
    }

    [Test]
    public void PreparedRageThiefSneakAttackAndInfuseContributeToRulesDamage()
    {
        CreatureComponent torgrim = Load("DataFiles/playerCharacters/Torgrim");
        CreatureComponent lena = Load("DataFiles/playerCharacters/Lena");
        CreatureComponent target = CreateCreature("Target", "enemy", 100, 10);
        torgrim.gameObject.AddComponent<Conditions>();
        lena.gameObject.AddComponent<Conditions>();
        target.gameObject.AddComponent<Conditions>();
        SpellEffectController effects = lena.gameObject.AddComponent<SpellEffectController>();
        effects.AddOrRefresh(new InfuseVitalitySpellEffect(torgrim.gameObject));
        TestActionController torgrimController =
            torgrim.gameObject.AddComponent<TestActionController>();
        TestActionController lenaController = lena.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        Place(torgrim.gameObject, 0);
        Place(target.gameObject, 1);
        Place(lena.gameObject, 2);
        Tile[,] tiles = CreateTiles(3);
        Occupy(tiles, torgrim.gameObject);
        Occupy(tiles, lena.gameObject);
        Occupy(tiles, target.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { torgrimController, lenaController, targetController },
            tiles,
            new ScriptedRollService(20, 15, 10, 10, 4, 10, 4, 4, 3)
        );
        CreatureId torgrimId = bridge.GetCreatureId(torgrim);
        CreatureId lenaId = bridge.GetCreatureId(lena);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.Dispatch(
            new ApplyConditionOp(
                "Off-Guard",
                targetId,
                lenaId,
                RuleSource.FromSlug("strike-test-off-guard"),
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            )
        );
        bridge.BeginTurn(torgrimId, 3);
        if (
            !RuntimeOptionResolver
                .Resolve(bridge.Snapshot, torgrimId, System.Array.Empty<string>())
                .Contains("self:effect:rage")
        )
            Assert.That(
                bridge.Dispatch(new RageActionOp(torgrimId)),
                Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
            );

        RulesStrikeAction torgrimStrike = torgrimController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .First(action => !action.IsRanged && action.ActionName != "Unarmed Strike");
        ResolvedOpResult<StrikeResolution> rageStrike = RequireResolved(
            bridge.Dispatch(new StrikeActionOp(torgrimId, torgrimStrike.Item.Item, targetId))
        );
        bridge.BeginTurn(lenaId, 3);
        RulesStrikeAction dogslicer = lenaController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(action => action.ActionName == "Dogslicer");
        ResolvedOpResult<StrikeResolution> rogueStrike = RequireResolved(
            bridge.Dispatch(new StrikeActionOp(lenaId, dogslicer.Item.Item, targetId))
        );

        Assert.That(
            rageStrike.Value.Damage.Sum(part => part.Amount),
            Is.GreaterThan(torgrimStrike.Item.DamageDice[0].Dice.Count + torgrim.strMod)
        );
        Assert.That(rogueStrike.Value.Damage.Any(part => part.DamageType == "precision"), Is.True);
        Assert.That(rogueStrike.Value.Damage.Any(part => part.DamageType == "vitality"), Is.True);
        Assert.That(
            rogueStrike.Value.Damage.Single(part => part.DamageType == "slashing").Amount,
            Is.EqualTo(4 + lena.dexMod)
        );
    }

    [Test]
    public void StrikeUsesCurrentTargetConditionAndOnlyTargetPreparedDefenses()
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        attacker.attackBonus = 10;
        attacker.weaknesses.Add(new DamageValue("bludgeoning", 50));
        attacker.resistances.Add(new DamageValue("bludgeoning", 50));
        attacker.immunities.Add("bludgeoning");
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 21);
        target.weaknesses.Add(new DamageValue("bludgeoning", 2));
        target.resistances.Add(new DamageValue("bludgeoning", 1));
        target.gameObject.AddComponent<Conditions>();
        TestActionController attackerController =
            attacker.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        Place(attacker.gameObject, 0);
        Place(target.gameObject, 1);
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, attacker.gameObject);
        Occupy(tiles, target.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { attackerController, targetController },
            tiles,
            new ScriptedRollService(20, 10, 10, 3)
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        bridge.Dispatch(
            new ApplyConditionOp(
                "Off-Guard",
                targetId,
                actor,
                RuleSource.FromSlug("strike-off-guard-test"),
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            )
        );
        RulesStrikeAction action = attackerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Unarmed Strike");

        ResolvedOpResult<StrikeResolution> result = RequireResolved(
            bridge.Dispatch(new StrikeActionOp(actor, action.Item.Item, targetId))
        );

        Assert.That(
            bridge
                .Snapshot.PreparedInputs[targetId]
                .StaticOptions.Any(option =>
                    option.StartsWith("self:condition:", StringComparison.Ordinal)
                ),
            Is.False
        );
        Assert.That(result.Value.Hit, Is.True);
        Assert.That(result.Value.Damage.Single().Amount, Is.EqualTo(4));
    }

    [Test]
    public void StrikeUsesPreparedDefensesEnrolledWithReinforcementTarget()
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        attacker.attackBonus = 10;
        CreatureComponent initialEnemy = CreateCreature("Initial Enemy", "enemies", 20, 10);
        TestActionController attackerController =
            attacker.gameObject.AddComponent<TestActionController>();
        TestActionController initialEnemyController =
            initialEnemy.gameObject.AddComponent<TestActionController>();
        Place(attacker.gameObject, 0);
        Place(initialEnemy.gameObject, 2);
        Tile[,] tiles = CreateTiles(3);
        Occupy(tiles, attacker.gameObject);
        Occupy(tiles, initialEnemy.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { attackerController, initialEnemyController },
            tiles,
            new ScriptedRollService(20, 10, 5, 10, 3)
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        bridge.StartEncounter("heroes");

        CreatureComponent reinforcement = CreateCreature("Reinforcement", "enemies", 20, 10);
        reinforcement.weaknesses.Add(new DamageValue("bludgeoning", 1));
        reinforcement.resistances.Add(new DamageValue("bludgeoning", 2));
        reinforcement.immunities.Add("bludgeoning");
        TestActionController reinforcementController =
            reinforcement.gameObject.AddComponent<TestActionController>();
        Place(reinforcement.gameObject, 1);
        bridge.RegisterCombatants(new[] { reinforcementController });
        Occupy(tiles, reinforcement.gameObject);
        bridge.RefreshTopology(tiles);
        CreatureId target = bridge.GetCreatureId(reinforcement);
        RulesStrikeAction action = attackerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Unarmed Strike");

        ResolvedOpResult<StrikeResolution> result = RequireResolved(
            bridge.Dispatch(new StrikeActionOp(actor, action.Item.Item, target))
        );

        Assert.That(bridge.Snapshot.PreparedInputs.Contains(target), Is.True);
        Assert.That(result.Value.Damage.Single().Amount, Is.Zero);
    }

    [Test]
    public void DispatchProjectsHealthAmmoLoadActionsMapAndStructuredLog()
    {
        CreatureComponent archer = CreateCreature("Archer", "heroes", 20, 10);
        EquipmentWeapon sling = new()
        {
            name = "Sling",
            group = "sling",
            category = "simple",
            range = 50,
            reload = "1",
            ammo = "sling-bullets",
            damage = new Dice(1, 6, "bludgeoning"),
        };
        archer.weapons = new List<EquipmentWeapon> { sling };
        archer.SetAmmoQuantity("sling-bullets", 2);
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 10);
        TestActionController archerController =
            archer.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        Place(archer.gameObject, 0);
        Place(target.gameObject, 1);
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, archer.gameObject);
        Occupy(tiles, target.gameObject);
        TestCombatLog log = InstallCombatLog();
        OnDamageDealt.AddListener(CountDamage);
        OnAttackMiss.AddListener(CountMiss);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { archerController, targetController },
            tiles,
            new ScriptedRollService(20, 10, 10, 4)
        );
        CreatureId actor = bridge.GetCreatureId(archer);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction action = archerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Sling");

        RequireResolved(bridge.Dispatch(new StrikeActionOp(actor, action.Item.Item, targetId)));

        Assert.That(archerController.ActionPoints, Is.EqualTo(2));
        Assert.That(archer.GetAmmoQuantity("sling-bullets"), Is.EqualTo(1));
        Assert.That(archer.IsWeaponLoaded(sling), Is.False);
        Assert.That(archerController.StrikePenalty, Is.EqualTo(1));
        Assert.That(target.hp, Is.EqualTo(16));
        Assert.That(log.Messages.Any(message => message.Contains("vs AC 10")), Is.True);
        Assert.That(damageEventCount, Is.EqualTo(1));
        Assert.That(missEventCount, Is.Zero);

        ResolvedOpResult<EquipmentState> reload = RequireResolved(
            bridge.Dispatch(new ReloadActionOp(actor, action.Item.Item))
        );
        Assert.That(reload.Value.IsLoaded, Is.True);
        Assert.That(archerController.ActionPoints, Is.EqualTo(1));
        Assert.That(archer.IsWeaponLoaded(sling), Is.True);
    }

    [Test]
    public void InstalledReloadCompletesWithoutCombatPresentationSingletons()
    {
        CreatureComponent archer = CreateCreature("Archer", "heroes", 20, 10);
        EquipmentWeapon sling = new()
        {
            name = "Sling",
            group = "sling",
            category = "simple",
            range = 50,
            reload = "1",
            ammo = "sling-bullets",
            damage = new Dice(1, 6, "bludgeoning"),
        };
        archer.weapons = new List<EquipmentWeapon> { sling };
        archer.unloadedWeapons = new List<string> { "sling" };
        archer.SetAmmoQuantity("sling-bullets", 1);
        TestActionController controller = archer.gameObject.AddComponent<TestActionController>();
        CreatureComponent opponent = CreateCreature("Opponent", "enemies", 20, 10);
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        Place(archer.gameObject, 0);
        Place(opponent.gameObject, 1);
        Tile[,] reloadTiles = CreateTiles(2);
        Occupy(reloadTiles, archer.gameObject);
        Occupy(reloadTiles, opponent.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            reloadTiles,
            new ScriptedRollService(20, 10)
        );
        CreatureId actor = bridge.GetCreatureId(archer);
        bridge.BeginTurn(actor, 3);
        RulesReloadWeaponAction reload = controller
            .GetActions()
            .OfType<RulesReloadWeaponAction>()
            .Single(candidate => candidate.ActionName == "Reload Sling");
        controller.IsTakingAction = true;

        Assert.That(CombatLog.TryGetInstance(out _), Is.False);
        Assert.That(CombatManagerInterface.TryGetInstance(out _), Is.False);
        Assert.That(reload.IsAvailable(controller), Is.True);
        Assert.DoesNotThrow(() => reload.Invoke(archer.gameObject));

        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(archer.IsWeaponLoaded(sling), Is.True);
        Assert.That(reload.IsAvailable(controller), Is.False);
    }

    [Test]
    public void SameNamedTeamsRejectStrikeWithoutTeamRulesBeforeMutation()
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature("Target", "heroes", 20, 10);
        TestActionController attackerController =
            attacker.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        CreatureComponent opponent = CreateCreature("Opponent", "enemies", 20, 10);
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        Place(attacker.gameObject, 0);
        Place(target.gameObject, 1);
        Place(opponent.gameObject, 2);
        Tile[,] tiles = CreateTiles(3);
        Occupy(tiles, attacker.gameObject);
        Occupy(tiles, target.gameObject);
        Occupy(tiles, opponent.gameObject);
        ScriptedRollService rolls = new(20, 15, 10, 20);

        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { attackerController, targetController, opponentController },
            tiles,
            rolls
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction action = attackerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Unarmed Strike");

        Assert.That(TeamRules.TryGetInstance(out _), Is.False);

        OpResult<StrikeResolution> result = bridge.Dispatch(
            new StrikeActionOp(actor, action.Item.Item, targetId)
        );

        Assert.That(result, Is.TypeOf<InvalidOpResult<StrikeResolution>>());
        Assert.That(
            ((InvalidOpResult<StrikeResolution>)result).Reason,
            Does.Contain("legal enemy")
        );
        Assert.That(attackerController.ActionPoints, Is.EqualTo(3));
        Assert.That(attackerController.StrikePenalty, Is.Zero);
        Assert.That(target.hp, Is.EqualTo(20));
        Assert.That(rolls.Remaining, Is.EqualTo(1));
        Assert.That(result.Facts, Is.Empty);
    }

    [TestCase(0, 10, "defeated")]
    [TestCase(20, 0, "Armor Class")]
    public void AiStrikePreviewRejectsTargetsThatAuthoritativeDispatchRejects(
        int targetHitPoints,
        int targetArmorClass,
        string reason
    )
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature(
            "Target",
            "enemies",
            targetHitPoints,
            targetArmorClass
        );
        TestActionController attackerController =
            attacker.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        CreatureComponent reserveTarget = CreateCreature("Reserve Target", "enemies", 20, 10);
        TestActionController reserveController =
            reserveTarget.gameObject.AddComponent<TestActionController>();
        Place(attacker.gameObject, 0);
        Place(target.gameObject, 1);
        Place(reserveTarget.gameObject, 2);
        Tile[,] tiles = CreateTiles(3);
        Occupy(tiles, attacker.gameObject);
        Occupy(tiles, target.gameObject);
        Occupy(tiles, reserveTarget.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { attackerController, targetController, reserveController },
            tiles,
            new ScriptedRollService(20, 15, 10, 20)
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction action = attackerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Unarmed Strike");

        bool canPreview = action.CanPreviewTarget(bridge.Snapshot, actor, targetId);
        OpResult<StrikeResolution> dispatched = bridge.Dispatch(
            new StrikeActionOp(actor, action.Item.Item, targetId)
        );

        Assert.That(canPreview, Is.False);
        Assert.That(dispatched, Is.TypeOf<InvalidOpResult<StrikeResolution>>());
        Assert.That(((InvalidOpResult<StrikeResolution>)dispatched).Reason, Does.Contain(reason));
        Assert.That(attackerController.ActionPoints, Is.EqualTo(3));
        Assert.That(attackerController.StrikePenalty, Is.Zero);
    }

    [Test]
    public void StrikePenaltyProjectsAttachedMapAndDefaultsToZeroWithoutRules()
    {
        CreatureComponent unattachedCreature = CreateCreature("Unattached", "heroes", 20, 10);
        TestActionController unattached =
            unattachedCreature.gameObject.AddComponent<TestActionController>();
        Assert.That(unattached.StrikePenalty, Is.Zero);

        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        TestActionController attached = attacker.gameObject.AddComponent<TestActionController>();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { attached },
            CreateTiles(1),
            new ScriptedRollService()
        );
        CreatureId actor = bridge.GetCreatureId(attacker);

        Assert.That(attached.StrikePenalty, Is.Zero);
        RequireResolved(bridge.Dispatch(new AdvanceMultipleAttackPenaltyOp(actor)));
        Assert.That(attached.StrikePenalty, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => attached.SetDungeonExploration(true));
        Assert.That(attached.StrikePenalty, Is.EqualTo(1));
    }

    [Test]
    public void ValidMissDispatchPublishesMissWithoutDamageAndEmitsStructuredAttackLog()
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 30);
        TestActionController attackerController =
            attacker.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        Place(attacker.gameObject, 0);
        Place(target.gameObject, 1);
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, attacker.gameObject);
        Occupy(tiles, target.gameObject);
        TestCombatLog log = InstallCombatLog();
        OnDamageDealt.AddListener(CountDamage);
        OnAttackMiss.AddListener(CountMiss);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { attackerController, targetController },
            tiles,
            new ScriptedRollService(20, 10, 2)
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction action = attackerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Unarmed Strike");

        ResolvedOpResult<StrikeResolution> result = RequireResolved(
            bridge.Dispatch(new StrikeActionOp(actor, action.Item.Item, targetId))
        );

        Assert.That(result.Value.Hit, Is.False);
        Assert.That(missEventCount, Is.EqualTo(1));
        Assert.That(damageEventCount, Is.Zero);
        Assert.That(log.Entries, Has.Count.EqualTo(1));
        Assert.That(log.Entries[0].Kind, Is.EqualTo(CombatLogEntryKind.Attack));
    }

    [Test]
    public void InvalidArmorClassRejectsBeforeProjectingAnyStrikeMutation()
    {
        CreatureComponent archer = CreateCreature("Archer", "heroes", 20, 10);
        EquipmentWeapon sling = new()
        {
            name = "Sling",
            group = "sling",
            category = "simple",
            range = 50,
            reload = "1",
            ammo = "sling-bullets",
            damage = new Dice(1, 6, "bludgeoning"),
        };
        archer.weapons = new List<EquipmentWeapon> { sling };
        archer.SetAmmoQuantity("sling-bullets", 2);
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 0);
        TestActionController archerController =
            archer.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        Place(archer.gameObject, 0);
        Place(target.gameObject, 1);
        TestCombatLog log = InstallCombatLog();
        OnDamageDealt.AddListener(CountDamage);
        OnAttackMiss.AddListener(CountMiss);
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, archer.gameObject);
        Occupy(tiles, target.gameObject);
        ScriptedRollService rolls = new(20, 10, 20);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { archerController, targetController },
            tiles,
            rolls
        );
        CreatureId actor = bridge.GetCreatureId(archer);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction action = archerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Sling");

        OpResult<StrikeResolution> result = bridge.Dispatch(
            new StrikeActionOp(actor, action.Item.Item, targetId)
        );

        Assert.That(result, Is.TypeOf<InvalidOpResult<StrikeResolution>>());
        Assert.That(
            ((InvalidOpResult<StrikeResolution>)result).Reason,
            Does.Contain("Armor Class")
        );
        Assert.That(archerController.ActionPoints, Is.EqualTo(3));
        Assert.That(archer.GetAmmoQuantity("sling-bullets"), Is.EqualTo(2));
        Assert.That(archer.IsWeaponLoaded(sling), Is.True);
        Assert.That(target.hp, Is.EqualTo(20));
        Assert.That(archerController.StrikePenalty, Is.Zero);
        Assert.That(rolls.Remaining, Is.EqualTo(1));
        Assert.That(result.Facts, Is.Empty);
        Assert.That(log.Messages, Is.Empty);
        Assert.That(damageEventCount, Is.Zero);
        Assert.That(missEventCount, Is.Zero);
    }

    private void CountDamage(string damageType) => damageEventCount++;

    private void CountMiss(GameObject attacker) => missEventCount++;

    private CreatureComponent Load(string path)
    {
        GameObject gameObject = CreatureJsonConverter.CreateFromFile(path);
        created.Add(gameObject);
        return gameObject.GetComponent<CreatureComponent>();
    }

    private CreatureComponent CreateCreature(string name, string teamName, int hp, int ac)
    {
        GameObject gameObject = new(name);
        created.Add(gameObject);
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.name = name;
        creature.ac = ac;
        creature.InitializeHealthBeforeEncounter(hp, hp);
        return creature;
    }

    private TestCombatLog InstallCombatLog()
    {
        GameObject gameObject = new("Strike Test Combat Log");
        created.Add(gameObject);
        TestCombatLog log = gameObject.AddComponent<TestCombatLog>();
        FieldInfo field = typeof(SingletonMonoBehaviour<CombatLogInterface>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        field.SetValue(null, log);
        return log;
    }

    private static Tile[,] CreateTiles(int width)
    {
        Tile[,] tiles = new Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new Tile();
        return tiles;
    }

    private static void Place(GameObject gameObject, int x) =>
        gameObject.transform.position = new Vector3(x, 0, 0);

    private static void Occupy(Tile[,] tiles, GameObject gameObject)
    {
        int x = Mathf.RoundToInt(gameObject.transform.position.x);
        tiles[x, 0].Occupants.Add(gameObject);
    }

    private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
    {
        Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
        return (ResolvedOpResult<T>)result;
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class TestCombatLog : CombatLogInterface
    {
        public readonly List<string> Messages = new();
        public readonly List<CombatLogEntry> Entries = new();

        public override void DevMode() { }

        public override void ReleaseMode() { }

        public override void AddWhiteList(string tag) { }

        public override void AddBlackList(string tag) { }

        public override void DevLog(string msg) => Messages.Add(msg);

        public override void DevLog(string msg, string tag) => Messages.Add(msg);

        public override void DevLog(string msg, List<string> tags) => Messages.Add(msg);

        public override void Log(string msg) => Messages.Add(msg);

        public override void Log(string msg, string tag) => Messages.Add(msg);

        public override void Log(string msg, List<string> tags) => Messages.Add(msg);

        public override void LogEntry(CombatLogEntry entry)
        {
            Entries.Add(entry);
            base.LogEntry(entry);
        }

        public override List<string> GetMessages() => new(Messages);
    }
}
