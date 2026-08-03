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
    public void EnrollmentReplacesDuplicateStaleRageActionsAndPreservesOtherActions()
    {
        CreatureComponent creature = CreateBarbarian();
        RageTestActionController controller =
            creature.gameObject.AddComponent<RageTestActionController>();
        CreatureComponent opponent = CreateBarbarian();
        RageTestActionController opponentController =
            opponent.gameObject.AddComponent<RageTestActionController>();
        RulesRageAction staleFirst = new RulesRageAction(new RageActionDefinition());
        RulesRageAction staleSecond = new RulesRageAction(new RageActionDefinition());
        MarkerAction marker = new MarkerAction();
        controller.AddAction(staleFirst);
        controller.AddAction(marker);
        controller.AddAction(staleSecond);

        UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            CreateTiles(),
            new ScriptedRollService(10)
        );

        RulesRageAction installed = controller.GetActions().OfType<RulesRageAction>().Single();
        Assert.That(installed, Is.Not.SameAs(staleFirst));
        Assert.That(installed, Is.Not.SameAs(staleSecond));
        Assert.That(controller.GetActions(), Does.Contain(marker));
        Assert.That(controller.GetActions().OfType<Game.Strikes.RulesStrikeAction>(), Is.Not.Empty);
    }

    [Test]
    public void ReusedControllerReplacesOrRemovesEncounterOwnedRageAction()
    {
        CreatureComponent creature = CreateBarbarian();
        RageTestActionController controller =
            creature.gameObject.AddComponent<RageTestActionController>();
        CreatureComponent opponent = CreateBarbarian();
        RageTestActionController opponentController =
            opponent.gameObject.AddComponent<RageTestActionController>();

        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            CreateTiles(),
            new ScriptedRollService(10)
        );
        CreatureId firstId = first.GetCreatureId(controller);
        RulesRageAction firstAction = controller.GetActions().OfType<RulesRageAction>().Single();
        first.ReleaseOwnership();

        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new ActionController[] { opponentController, controller },
            CreateTiles(),
            new ScriptedRollService(10)
        );
        CreatureId secondId = second.GetCreatureId(controller);
        RulesRageAction secondAction = controller.GetActions().OfType<RulesRageAction>().Single();

        Assert.That(secondId, Is.Not.EqualTo(firstId));
        Assert.That(secondAction, Is.Not.SameAs(firstAction));
        second.ReleaseOwnership();

        creature.Build = new CharacterBuild
        {
            ClassName = "Rogue",
            SubclassName = "Thief",
            ClassFeatName = "Nimble Dodge",
        };
        UnityCombatRulesBridge third = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            CreateTiles(),
            new ScriptedRollService(10)
        );

        Assert.That(controller.GetActions().OfType<RulesRageAction>(), Is.Empty);
        Assert.That(
            third
                .Snapshot.PreparedInputs[third.GetCreatureId(controller)]
                .BoundOptions.Any(option => option.Option == "item:owned:rage"),
            Is.False
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
        EncounterState encounter = bridge.StartEncounter(
            "players",
            EncounterConclusionPolicy.VictoryOrDefeat
        );

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
        fatiguedCreature.gameObject.AddComponent<Conditions>();
        RageTestActionController fatiguedController =
            fatiguedCreature.gameObject.AddComponent<RageTestActionController>();
        CreatureComponent encumberedCreature = CreateBarbarian();
        SetTeam(encumberedCreature.gameObject, "enemies");
        encumberedCreature.gameObject.AddComponent<Conditions>();
        RageTestActionController encumberedController =
            encumberedCreature.gameObject.AddComponent<RageTestActionController>();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { fatiguedController, encumberedController },
            CreateTiles(),
            new ScriptedRollService(20, 10)
        );
        CreatureId fatiguedActor = bridge.GetCreatureId(fatiguedCreature);
        CreatureId encumberedActor = bridge.GetCreatureId(encumberedCreature);
        RuleSource fatiguedSource = RuleSource.FromSlug("rage-test-fatigued");
        bridge.Dispatch(
            new ApplyConditionOp(
                "fatigued",
                fatiguedActor,
                fatiguedActor,
                fatiguedSource,
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            )
        );
        bridge.Dispatch(
            new ApplyConditionOp(
                "encumbered",
                encumberedActor,
                encumberedActor,
                RuleSource.FromSlug("rage-test-encumbered"),
                EffectDuration.Indefinite,
                ConditionMarkerState.Instance
            )
        );
        bridge.StartEncounter("players", EncounterConclusionPolicy.VictoryOrDefeat);

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

        bridge.Dispatch(
            new CleanupConditionsFromSourceOp(
                fatiguedActor,
                ConditionRuleDefinitions.Fatigued,
                fatiguedSource,
                ConditionCleanupKind.Remove
            )
        );
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

    private sealed class MarkerAction : EntityAction
    {
        internal MarkerAction()
            : base(0) { }

        public override string ActionName => "Marker";
    }
}
