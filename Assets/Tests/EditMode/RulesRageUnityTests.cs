using System.Collections.Generic;
using System.Linq;
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
        CreatureComponent opponent = CreateBarbarian();
        RageTestActionController opponentController =
            opponent.gameObject.AddComponent<RageTestActionController>();

        UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            CreateTiles(),
            new ScriptedRollService(10)
        );

        Assert.That(
            controller.GetActions().FindAll(action => action is RulesRageAction),
            Has.Count.EqualTo(1)
        );
    }

    [Test]
    public void RageListenersOwnQuickTemperedCleanupOneShotAndLaterRageCost()
    {
        CreatureComponent creature = CreateBarbarian();
        SetTeam(creature.gameObject, "players");
        creature.gameObject.AddComponent<Conditions>();
        RageTestActionController controller =
            creature.gameObject.AddComponent<RageTestActionController>();
        CreatureComponent opponent = CreateBarbarian();
        SetTeam(opponent.gameObject, "enemies");
        RageTestActionController opponentController =
            opponent.gameObject.AddComponent<RageTestActionController>();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            CreateTiles(),
            new ScriptedRollService(20, 10)
        );
        CreatureId actor = bridge.GetCreatureId(creature);
        EncounterState encounter = bridge.StartEncounter("players");

        Assert.That(encounter.CurrentTurn.Value.Actor, Is.EqualTo(actor));
        Assert.That(RageRules.IsRaging(bridge.Snapshot, actor), Is.True);
        Assert.That(controller.ActionPoints, Is.EqualTo(3));
        Assert.That(creature.tempHp, Is.EqualTo(creature.level + creature.conMod));
        Assert.That(
            creature.Prepared.HasActiveEffect("rage"),
            Is.False,
            "PreparedCharacter must not become a second active-effect authority."
        );

        OpResult<RageEndOutcome> ended = bridge.Dispatch(new EndRageOp(actor));
        OpResult<RageStartOutcome> ordinary = bridge.Dispatch(new RageActionOp(actor));

        Assert.That(ended, Is.TypeOf<ResolvedOpResult<RageEndOutcome>>());
        Assert.That(((ResolvedOpResult<RageEndOutcome>)ended).Value.Ended, Is.True);
        Assert.That(ordinary, Is.TypeOf<ResolvedOpResult<RageStartOutcome>>());
        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        Assert.That(creature.tempHp, Is.Zero);
        Assert.That(creature.HasTempHpImmunity("rage"), Is.True);
    }

    [Test]
    public void ConditionsAddedAfterEnrollmentBlockRageAndQuickTemperedWithoutStaticOptions()
    {
        CreatureComponent fatiguedCreature = CreateBarbarian();
        SetTeam(fatiguedCreature.gameObject, "players");
        Conditions fatiguedConditions = fatiguedCreature.gameObject.AddComponent<Conditions>();
        RageTestActionController fatiguedController =
            fatiguedCreature.gameObject.AddComponent<RageTestActionController>();
        CreatureComponent encumberedCreature = CreateBarbarian();
        SetTeam(encumberedCreature.gameObject, "enemies");
        Conditions encumberedConditions = encumberedCreature.gameObject.AddComponent<Conditions>();
        encumberedConditions.Add("encumbered", new ConditionSource());
        RageTestActionController encumberedController =
            encumberedCreature.gameObject.AddComponent<RageTestActionController>();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { fatiguedController, encumberedController },
            CreateTiles(),
            new ScriptedRollService(20, 10)
        );
        CreatureId fatiguedActor = bridge.GetCreatureId(fatiguedCreature);
        CreatureId encumberedActor = bridge.GetCreatureId(encumberedCreature);
        ConditionSource fatiguedSource = new ConditionSource();
        fatiguedConditions.Add("fatigued", fatiguedSource);
        bridge.StartEncounter("players");

        Assert.That(
            bridge
                .Snapshot.PreparedInputs[fatiguedActor]
                .StaticOptions.Any(option =>
                    option.StartsWith("self:condition:", System.StringComparison.Ordinal)
                ),
            Is.False
        );
        Assert.That(
            bridge
                .Snapshot.PreparedInputs[encumberedActor]
                .StaticOptions.Any(option =>
                    option.StartsWith("self:condition:", System.StringComparison.Ordinal)
                ),
            Is.False
        );
        Assert.That(
            bridge.Dispatch(new RageActionOp(fatiguedActor)),
            Is.TypeOf<InvalidOpResult<RageStartOutcome>>()
        );
        Assert.That(RageRules.IsRaging(bridge.Snapshot, encumberedActor), Is.False);

        fatiguedConditions.Remove("fatigued", fatiguedSource);
        Assert.That(
            bridge.Dispatch(new RageActionOp(fatiguedActor)),
            Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
        );
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

    private static void SetTeam(GameObject creature, string name)
    {
        Team team = creature.AddComponent<Team>();
        team.Name = name;
    }

    private sealed class RageTestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
