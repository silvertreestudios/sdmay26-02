using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Strike;
using Game.Strikes;
using GridPrivate;
using GridPublic;
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

    [Test]
    public void CatalogExtractionInstallsStableWeaponAndUnarmedActionsOnce()
    {
        CreatureComponent lena = Load("DataFiles/playerCharacters/Lena");
        TestActionController controller = lena.gameObject.AddComponent<TestActionController>();
        UnityCombatRulesBridge.Create(
            new[] { controller },
            CreateTiles(1),
            new ScriptedRollService(10),
            "heroes"
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

    [TestCase(10, true, 0)]
    [TestCase(15, true, -2)]
    [TestCase(60, true, -10)]
    [TestCase(65, false, 0)]
    public void GridPreviewAndEncounterTargetingAgreeAtRangeBoundaries(
        int feet,
        bool legal,
        int penalty
    )
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 18);
        attacker.transform.position = Vector3.zero;
        target.transform.position = new Vector3(feet / 5, 0, 0);
        var actorController = attacker.gameObject.AddComponent<TestActionController>();
        var targetController = target.gameObject.AddComponent<TestActionController>();
        Tile[,] tiles = CreateTiles(14);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { actorController, targetController },
            tiles,
            new ScriptedRollService(10, 10),
            "heroes"
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId defender = bridge.GetCreatureId(target);
        UnityStrikeContext context = new(
            new Dictionary<CreatureId, CreatureComponent>
            {
                [actor] = attacker,
                [defender] = target,
            },
            tiles,
            SamePlayerCombatantFriendshipProvider.Instance
        );
        var item = new StrikeItemDefinition(
            new ItemId("ranged"),
            new ItemDefinitionId("ranged"),
            "Ranged",
            "",
            "martial",
            Array.Empty<Trait>(),
            5,
            new[] { new TypedDamageDice(new DiceExpression(1, 6), "piercing", "ranged") },
            Array.Empty<TypedFlatDamage>(),
            5,
            10,
            0,
            StrikeAmmunitionRequirement.None
        );
        StrikeTargetResult preview = StrikeTargeting.Evaluate(
            attacker.gameObject,
            target.gameObject,
            tiles,
            new StrikeTargetRequest { IsRanged = true, RangeIncrementFeet = 10 }
        );
        StrikeTargetingOutcome execution = context.Evaluate(bridge.Snapshot, actor, item, defender);
        Assert.That(preview != null, Is.EqualTo(legal));
        Assert.That(execution is LegalStrikeTargetingOutcome, Is.EqualTo(legal));
        if (execution is LegalStrikeTargetingOutcome accepted)
        {
            Assert.That(accepted.RangePenalty, Is.EqualTo(penalty));
            Assert.That(accepted.RangePenalty, Is.EqualTo(preview.RangePenalty));
            Assert.That(accepted.CoverBonus, Is.EqualTo(preview.CoverAcBonus));
            Assert.That(accepted.DistanceFeet, Is.EqualTo(preview.DistanceFeet));
        }
        bridge.ReleaseOwnership();
    }

    [Test]
    public void PreparedCaptureFreezesDefinitionsAndUsesConditionAuthority()
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 18);
        var prepared = Pf2eCharacterPreparer.EnsurePrepared(attacker);
        var modifier = new RuleModifier
        {
            Selector = "strike-damage",
            Slug = "context-bonus",
            Value = 5,
            Predicate = Newtonsoft.Json.Linq.JToken.Parse("[\"target:condition:Fatigued\"]"),
        };
        prepared.Modifiers.Add(modifier);
        prepared.Adjustments.Add(
            new RuleAdjustment
            {
                Selector = "strike-damage",
                Slug = "context-bonus",
                Mode = "multiply",
                Value = .5f,
            }
        );
        var actorController = attacker.gameObject.AddComponent<TestActionController>();
        var targetController = target.gameObject.AddComponent<TestActionController>();
        Tile[,] tiles = CreateTiles(2);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { actorController, targetController },
            tiles,
            new ScriptedRollService(10, 10),
            "heroes",
            EncounterConclusionPolicy.VictoryOrDefeat,
            new[] { EffectRemovalTestWorkflow.CreateExtension() }
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId defender = bridge.GetCreatureId(target);
        UnityStrikeContext context = new(
            new Dictionary<CreatureId, CreatureComponent>
            {
                [actor] = attacker,
                [defender] = target,
            },
            tiles,
            SamePlayerCombatantFriendshipProvider.Instance
        );
        using IDisposable registration = context.PrepareCombatant(actor, attacker, out _, out _);
        StrikeItemDefinition item = context
            .GetItems(actor)
            .Single(value => value.Definition.Value == "unarmed");
        bridge.Dispatch(
            new ApplyConditionOp(
                defender,
                new ConditionId("Fatigued"),
                1,
                actor,
                RuleSource.FromSlug("test-condition"),
                EffectDuration.Indefinite
            )
        );
        target.GetComponent<Conditions>().Clear("Fatigued");
        for (int index = 0; index < 3; index++)
            Assert.That(
                context
                    .Capture(
                        bridge.Snapshot,
                        actor,
                        item,
                        defender,
                        StrikeTargetingOutcome.Legal(5, 0, 0, false)
                    )
                    .FlatDamage.Single()
                    .Amount,
                Is.EqualTo(2)
            );
        Assert.That(modifier.Value, Is.EqualTo(5));
        modifier.Value = 99;
        ((Newtonsoft.Json.Linq.JArray)modifier.Predicate)[0] = "never";
        Assert.That(
            context
                .Capture(
                    bridge.Snapshot,
                    actor,
                    item,
                    defender,
                    StrikeTargetingOutcome.Legal(5, 0, 0, false)
                )
                .FlatDamage.Single()
                .Amount,
            Is.EqualTo(2),
            "Enrollment owns a deep immutable definition."
        );
        var effect = ConditionRules.GetApplications(bridge.Snapshot, defender).Single();
        var binding = bridge
            .Snapshot.RuleBindings.Select(pair => pair.Value)
            .Single(value => value.EffectId == effect.Id);
        EffectRemovalTestWorkflow.Remove(
            bridge,
            new RemoveActiveEffectOp(
                effect.Id,
                binding.Id,
                effect.EffectStateVersion,
                ActiveEffectRemovalReason.Ended,
                RuleSource.FromSlug("test-removal")
            )
        );
        Assert.That(ConditionRules.GetApplications(bridge.Snapshot, defender), Is.Empty);
        target.GetComponent<Conditions>().Add("Fatigued", new ConditionSource());
        Assert.That(
            context
                .Capture(
                    bridge.Snapshot,
                    actor,
                    item,
                    defender,
                    StrikeTargetingOutcome.Legal(5, 0, 0, false)
                )
                .FlatDamage,
            Is.Empty
        );
        bridge.ReleaseOwnership();
    }

    [Test]
    public void StrikeCaptureKeepsCoverAsTypedArmorClassCandidate()
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 18);
        TestActionController attackerController =
            attacker.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        Tile[,] tiles = CreateTiles(2);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { attackerController, targetController },
            tiles,
            new ScriptedRollService(10, 10),
            "heroes"
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId targetId = bridge.GetCreatureId(target);
        UnityStrikeContext context = new(
            new Dictionary<CreatureId, CreatureComponent>
            {
                [actor] = attacker,
                [targetId] = target,
            },
            tiles,
            SamePlayerCombatantFriendshipProvider.Instance
        );
        using IDisposable preparation = context.PrepareCombatant(actor, attacker, out _, out _);
        StrikeItemDefinition item = context
            .GetItems(actor)
            .Single(value => value.Item.Value.EndsWith("unarmed"));

        StrikeResolutionData data = context.Capture(
            bridge.Snapshot,
            actor,
            item,
            targetId,
            StrikeTargetingOutcome.Legal(5, 0, 2, false)
        );

        Assert.That(data.BaseArmorClass, Is.EqualTo(18));
        Assert.That(data.ArmorClassModifiers, Has.Count.EqualTo(1));
        Assert.That(data.ArmorClassModifiers[0].Value, Is.EqualTo(2));
        Assert.That(data.ArmorClassModifiers[0].Type, Is.EqualTo(ModifierType.Circumstance));
        Assert.That(data.ArmorClassModifiers[0].Source.Slug, Is.EqualTo("cover"));
    }

    [Test]
    public void RulesStrikeUsesEnrolledGenericAttackAndArmorClassModifiersExactlyOnce()
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 10);
        Pf2eModifierCollection attackModifiers =
            attacker.gameObject.AddComponent<Pf2eModifierCollection>();
        attackModifiers.Add(
            new Pf2eModifier(
                1,
                Pf2eModifierType.Untyped,
                "Enrolled attack modifier",
                Pf2eStatistic.AttackRoll
            )
        );
        Pf2eModifierCollection armorClassModifiers =
            target.gameObject.AddComponent<Pf2eModifierCollection>();
        armorClassModifiers.Add(
            new Pf2eModifier(
                2,
                Pf2eModifierType.Untyped,
                "Enrolled Armor Class modifier",
                Pf2eStatistic.ArmorClass
            )
        );
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
            new ScriptedRollService(20, 10, 10),
            "heroes"
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

        Assert.That(result.Value.AttackModifier, Is.EqualTo(1));
        Assert.That(result.Value.ArmorClass, Is.EqualTo(12));
        Assert.That(result.Value.Hit, Is.False);
    }

    [Test]
    public void CreatureTargetingIncludesTheCasterOnlyWhenExplicitlyRequested()
    {
        CreatureComponent caster = CreateCreature("Caster", "heroes", 20, 10);
        Tile[,] tiles = CreateTiles(1);
        StrikeTargetRequest ordinary = new() { ReachFeet = 5 };
        StrikeTargetRequest selfCapable = new() { ReachFeet = 5, IncludeSelf = true };

        Assert.That(StrikeTargeting.CellsInRange(tiles, Vector3Int.zero, ordinary), Is.Empty);
        Assert.That(
            StrikeTargeting.CellsInRange(tiles, Vector3Int.zero, selfCapable),
            Is.EqualTo(new[] { Vector3Int.zero })
        );
        Assert.That(
            StrikeTargeting.Evaluate(caster.gameObject, caster.gameObject, tiles, ordinary),
            Is.Null
        );
        Assert.That(
            StrikeTargeting.Evaluate(caster.gameObject, caster.gameObject, tiles, selfCapable),
            Is.Not.Null
        );
    }

    [Test]
    public void PreparedRageAndThiefSneakAttackContributeToRulesDamage()
    {
        CreatureComponent torgrim = Load("DataFiles/playerCharacters/Torgrim");
        torgrim.Prepared.OwnedItems.RemoveAll(item =>
            string.Equals(
                item.Item.Slug,
                "quick-tempered",
                System.StringComparison.OrdinalIgnoreCase
            )
        );
        torgrim.Prepared.RollOptions.Remove("feat:quick-tempered");
        CreatureComponent lena = Load("DataFiles/playerCharacters/Lena");
        CreatureComponent target = CreateCreature("Target", "enemy", 100, 10);
        torgrim.gameObject.AddComponent<Conditions>();
        lena.gameObject.AddComponent<Conditions>();
        ConditionEncounterModule.Apply(
            target.gameObject,
            new ConditionState(OffGuardRules.ConditionId, 1),
            RuleSource.FromSlug("strike-test-off-guard"),
            new ConditionSource()
        );
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
            new ScriptedRollService(20, 15, 10, 10, 4, 10, 4, 5, 3),
            "heroes"
        );
        CreatureId torgrimId = bridge.GetCreatureId(torgrim);
        CreatureId lenaId = bridge.GetCreatureId(lena);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(torgrimId, 3);
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
        Assert.That(
            rogueStrike.Value.Damage.Single(part => part.DamageType == "slashing").Amount,
            Is.EqualTo(4 + lena.dexMod)
        );
    }

    /// <summary>
    /// Verifies effective Strike AC using each flanker's own reach and preserves independently
    /// applied Off-Guard when the segment crosses adjacent target edges.
    /// </summary>
    [TestCase(1, 1, 3, 1, false, false, false, true)]
    [TestCase(0, 0, 3, 2, true, false, false, false)]
    [TestCase(0, 0, 3, 1, true, false, false, true)]
    [TestCase(3, 1, 0, 0, false, true, false, true)]
    [TestCase(3, 1, 0, 0, false, false, false, false)]
    [TestCase(0, 0, 3, 2, true, false, true, true)]
    public void RulesStrikeUsesSnapshotFlankingAcrossOppositeTargetEdges(
        int attackerX,
        int attackerZ,
        int allyX,
        int allyZ,
        bool useReachWeapon,
        bool allyUsesReachWeapon,
        bool independentOffGuard,
        bool expectedOffGuard
    )
    {
        CreatureComponent attacker = CreateCreature("Attacker", "heroes", 20, 10);
        CreatureComponent target = CreateCreature("Target", "enemies", 20, 10);
        CreatureComponent ally = CreateCreature("Ally", "heroes", 20, 10);
        if (useReachWeapon)
            EquipReachWeapon(attacker);
        if (allyUsesReachWeapon)
            EquipReachWeapon(ally);
        if (independentOffGuard)
            ConditionEncounterModule.Apply(
                target.gameObject,
                new ConditionState(OffGuardRules.ConditionId, 1),
                RuleSource.FromSlug("flanking-test-independent-off-guard"),
                new ConditionSource()
            );
        TestActionController attackerController =
            attacker.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        TestActionController allyController = ally.gameObject.AddComponent<TestActionController>();
        attacker.transform.position = new Vector3(attackerX, 0, attackerZ);
        target.transform.position = new Vector3(2, 0, 1);
        ally.transform.position = new Vector3(allyX, 0, allyZ);
        Tile[,] tiles = CreateTiles(4, 3);
        Occupy(tiles, attacker.gameObject);
        Occupy(tiles, target.gameObject);
        Occupy(tiles, ally.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { attackerController, targetController, allyController },
            tiles,
            new ScriptedRollService(20, 15, 10, 4),
            "heroes"
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction action = attackerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate =>
                candidate.ActionName == (useReachWeapon ? "Reach weapon" : "Unarmed Strike")
            );

        Assert.That(action.Item.ReachFeet, Is.EqualTo(useReachWeapon ? 10 : 5));
        Assert.That(
            allyController
                .GetActions()
                .OfType<RulesStrikeAction>()
                .Max(candidate => candidate.Item.ReachFeet),
            Is.EqualTo(allyUsesReachWeapon ? 10 : 5)
        );
        Assert.That(
            OffGuardRules.IsOffGuard(bridge.Snapshot, targetId),
            Is.EqualTo(independentOffGuard)
        );

        ResolvedOpResult<StrikeResolution> result = RequireResolved(
            bridge.Dispatch(new StrikeActionOp(actor, action.Item.Item, targetId))
        );

        Assert.That(result.Value.OffGuard, Is.EqualTo(expectedOffGuard));
        Assert.That(result.Value.ArmorClass, Is.EqualTo(expectedOffGuard ? 8 : 10));
        Assert.That(
            OffGuardRules.IsOffGuard(bridge.Snapshot, targetId),
            Is.EqualTo(independentOffGuard),
            "Flanking must neither create nor remove an independent Off-Guard application."
        );
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
            new ScriptedRollService(20, 10, 10, 4),
            "heroes"
        );
        CreatureId actor = bridge.GetCreatureId(archer);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction action = archerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Sling");

        StrikeActionOp operation = new StrikeActionOp(actor, action.Item.Item, targetId);
        RequireResolved(bridge.Dispatch(operation));
        Drain(bridge.DrainActionPresentation(operation));

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
            new ScriptedRollService(20, 10),
            "heroes"
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
            rolls,
            "heroes"
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
            new ScriptedRollService(20, 15, 10, 20),
            "heroes"
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
            new ScriptedRollService(10),
            "heroes"
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
            new ScriptedRollService(20, 10, 2),
            "heroes"
        );
        CreatureId actor = bridge.GetCreatureId(attacker);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction action = attackerController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(candidate => candidate.ActionName == "Unarmed Strike");

        StrikeActionOp operation = new StrikeActionOp(actor, action.Item.Item, targetId);
        ResolvedOpResult<StrikeResolution> result = RequireResolved(bridge.Dispatch(operation));
        Drain(bridge.DrainActionPresentation(operation));

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
            rolls,
            "heroes"
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
        gameObject.AddComponent<Team>().Name = "heroes";
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

    private static void EquipReachWeapon(CreatureComponent creature)
    {
        creature.weapons = new List<EquipmentWeapon>
        {
            new()
            {
                name = "Reach weapon",
                group = "spear",
                category = "martial",
                traits = new List<string> { "reach" },
                damage = new Dice(1, 6, "piercing"),
            },
        };
    }

    private static Tile[,] CreateTiles(int width, int depth = 1)
    {
        Tile[,] tiles = new Tile[width, depth];
        for (int x = 0; x < width; x++)
        for (int z = 0; z < depth; z++)
            tiles[x, z] = new Tile();
        return tiles;
    }

    private static void Place(GameObject gameObject, int x) =>
        gameObject.transform.position = new Vector3(x, 0, 0);

    private static void Occupy(Tile[,] tiles, GameObject gameObject)
    {
        int x = Mathf.RoundToInt(gameObject.transform.position.x);
        int z = Mathf.RoundToInt(gameObject.transform.position.z);
        tiles[x, z].Occupants.Add(gameObject);
    }

    private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
    {
        Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>());
        return (ResolvedOpResult<T>)result;
    }

    private static void Drain(IEnumerator presentation)
    {
        while (presentation.MoveNext()) { }
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
