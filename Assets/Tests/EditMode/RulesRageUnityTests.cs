using System.Collections.Generic;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class RulesRageUnityTests
{
    private readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject gameObject in created)
        {
            if (gameObject != null)
                Object.DestroyImmediate(gameObject);
        }
        created.Clear();
        Pf2eItemCatalog.ResetForTests();
    }

    [Test]
    public void PreparedBarbarianReceivesOneRulesRageAction()
    {
        CreatureComponent creature = CreateBarbarian();
        RageTestActionController controller =
            creature.gameObject.AddComponent<RageTestActionController>();

        creature.InitializeRuntimeActions();
        creature.InitializeRuntimeActions();

        Assert.That(
            controller.GetActions().FindAll(action => action is RulesRageAction),
            Has.Count.EqualTo(1)
        );
    }

    [Test]
    public void BridgeOwnsQuickTemperedRageCleanupOneShotAndLaterRageCost()
    {
        CreatureComponent creature = CreateBarbarian();
        creature.gameObject.AddComponent<Conditions>();
        RageTestActionController controller =
            creature.gameObject.AddComponent<RageTestActionController>();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { controller },
            CreateTiles()
        );
        CreatureId actor = bridge.GetCreatureId(creature);
        bridge.BeginTurn(actor, 3);

        OpResult<RageStartOutcome> quickTempered = bridge.ResolveInitiativeRollRage(actor);

        Assert.That(quickTempered, Is.TypeOf<ResolvedOpResult<RageStartOutcome>>());
        Assert.That(bridge.IsRaging(actor), Is.True);
        Assert.That(controller.ActionPoints, Is.EqualTo(3));
        Assert.That(creature.tempHp, Is.EqualTo(creature.level + creature.conMod));
        Assert.That(
            creature.Prepared.HasActiveEffect("rage"),
            Is.False,
            "PreparedCharacter must not become a second active-effect authority."
        );

        RageEndOutcome ended = bridge.EndRage(actor);
        OpResult<RageStartOutcome> repeatedTrigger = bridge.ResolveInitiativeRollRage(actor);
        OpResult<RageStartOutcome> ordinary = bridge.DispatchRage(actor);

        Assert.That(ended.Ended, Is.True);
        Assert.That(repeatedTrigger, Is.TypeOf<InvalidOpResult<RageStartOutcome>>());
        Assert.That(ordinary, Is.TypeOf<ResolvedOpResult<RageStartOutcome>>());
        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        Assert.That(creature.tempHp, Is.Zero);
        Assert.That(creature.HasTempHpImmunity("rage"), Is.True);
    }

    [Test]
    public void NewEncounterClearsRageTemporaryHitPointImmunity()
    {
        CreatureComponent creature = CreateBarbarian();
        creature.gameObject.AddComponent<Conditions>();
        RageTestActionController controller =
            creature.gameObject.AddComponent<RageTestActionController>();
        UnityCombatRulesBridge firstEncounter = UnityCombatRulesBridge.Create(
            new[] { controller },
            CreateTiles()
        );
        CreatureId firstActor = firstEncounter.GetCreatureId(creature);
        firstEncounter.BeginTurn(firstActor, 3);
        Assert.That(
            firstEncounter.DispatchRage(firstActor),
            Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
        );
        firstEncounter.EndRage(firstActor);
        Assert.That(creature.HasTempHpImmunity("rage"), Is.True);

        controller.ResetEncounterTurnState();
        UnityCombatRulesBridge nextEncounter = UnityCombatRulesBridge.Create(
            new[] { controller },
            CreateTiles()
        );
        CreatureId nextActor = nextEncounter.GetCreatureId(creature);
        nextEncounter.BeginTurn(nextActor, 3);
        OpResult<RageStartOutcome> restarted = nextEncounter.DispatchRage(nextActor);

        Assert.That(restarted, Is.TypeOf<ResolvedOpResult<RageStartOutcome>>());
        Assert.That(
            ((ResolvedOpResult<RageStartOutcome>)restarted).Value.TemporaryHitPointsGranted,
            Is.True
        );
        Assert.That(creature.tempHp, Is.EqualTo(creature.level + creature.conMod));
        Assert.That(creature.HasTempHpImmunity("rage"), Is.False);
    }

    private CreatureComponent CreateBarbarian()
    {
        GameObject barbarian = CreatureJsonConverter.CreateFromFile(
            "DataFiles/playerCharacters/Torgrim"
        );
        created.Add(barbarian);
        return barbarian.GetComponent<CreatureComponent>();
    }

    private static Tile[,] CreateTiles()
    {
        Tile[,] tiles = new Tile[1, 1];
        tiles[0, 0] = new Tile();
        return tiles;
    }

    private sealed class RageTestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
