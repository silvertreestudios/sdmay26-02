using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
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
            new ScriptedRollService(20, 10),
            "players"
        );
        CreatureId actor = bridge.GetCreatureId(creature);
        bridge.AdvanceEncounter();
        EncounterState encounter = bridge.GetEncounter();

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
    public void LowercaseImportedConditionsBlockRageAndQuickTempered()
    {
        CreatureComponent fatiguedCreature = CreateBarbarian();
        SetTeam(fatiguedCreature.gameObject, "players");
        Conditions fatiguedConditions = fatiguedCreature.gameObject.AddComponent<Conditions>();
        fatiguedConditions.Add("fatigued", new ConditionSource());
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
            new ScriptedRollService(20, 10),
            "players"
        );
        CreatureId fatiguedActor = bridge.GetCreatureId(fatiguedCreature);
        CreatureId encumberedActor = bridge.GetCreatureId(encumberedCreature);
        bridge.AdvanceEncounter();

        Assert.That(
            bridge.Dispatch(new RageActionOp(fatiguedActor)),
            Is.TypeOf<InvalidOpResult<RageStartOutcome>>()
        );
        Assert.That(RageRules.IsRaging(bridge.Snapshot, encumberedActor), Is.False);
    }

    [Test]
    public void RageReadsSourcedConditionsDespiteStaleDisplayAndAfterRemoval()
    {
        CreatureComponent creature = CreateBarbarian();
        SetTeam(creature.gameObject, "players");
        Conditions display = creature.gameObject.AddComponent<Conditions>();
        display.Add("Fatigued", new ConditionSource());
        RageTestActionController controller =
            creature.gameObject.AddComponent<RageTestActionController>();
        CreatureComponent opponent = CreateBarbarian();
        SetTeam(opponent.gameObject, "enemies");
        RageTestActionController enemyController =
            opponent.gameObject.AddComponent<RageTestActionController>();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, enemyController },
            CreateTiles(),
            new ScriptedRollService(20, 10),
            "players"
        );
        CreatureId actor = bridge.GetCreatureId(creature);
        bridge.AdvanceEncounter();
        var second = bridge.Dispatch(
            new ApplyConditionOp(
                actor,
                new ConditionId("Fatigued"),
                1,
                bridge.GetCreatureId(opponent),
                RuleSource.FromSlug("second-source"),
                EffectDuration.Indefinite
            )
        );
        Assert.That(second, Is.TypeOf<ResolvedOpResult<ActiveEffectCreationOutcome>>());
        Assert.That(ConditionRules.GetApplications(bridge.Snapshot, actor).Count(), Is.EqualTo(2));
        var identities = ConditionRules
            .GetApplications(bridge.Snapshot, actor)
            .Select(effect => effect.Id)
            .ToArray();
        bridge.ReleaseOwnership();
        bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, enemyController },
            CreateTiles(),
            new ScriptedRollService(20, 10),
            "players",
            EncounterConclusionPolicy.VictoryOrDefeat,
            new[] { EffectRemovalTestWorkflow.CreateExtension() }
        );
        actor = bridge.GetCreatureId(creature);
        bridge.AdvanceEncounter();
        Assert.That(
            ConditionRules.GetApplications(bridge.Snapshot, actor).Select(effect => effect.Id),
            Is.EquivalentTo(identities)
        );
        display.Clear("Fatigued");
        Assert.That(new RulesRageAction().IsAvailable(controller), Is.False);
        var effects = ConditionRules.GetApplications(bridge.Snapshot, actor).ToArray();
        for (int index = 0; index < effects.Length; index++)
        {
            ActiveEffectInstance effect = effects[index];
            ActiveRuleBinding binding = bridge
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
            Assert.That(bridge.Snapshot.ActiveEffects.Contains(effect.Id), Is.False);
            Assert.That(
                new RulesRageAction().IsAvailable(controller),
                Is.EqualTo(index == effects.Length - 1)
            );
        }
        display.Add("Fatigued", new ConditionSource());
        Assert.That(
            new RulesRageAction().IsAvailable(controller),
            Is.True,
            "A stale display cannot reapply a condition."
        );
        Assert.That(
            bridge.Dispatch(new RageActionOp(actor)),
            Is.TypeOf<ResolvedOpResult<RageStartOutcome>>()
        );
        bridge.Dispatch(new EndRageOp(actor));
        bridge.ReleaseOwnership();
        bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, enemyController },
            CreateTiles(),
            new ScriptedRollService(20, 10),
            "players"
        );
        actor = bridge.GetCreatureId(creature);
        Assert.That(
            ConditionRules.GetApplications(bridge.Snapshot, actor),
            Is.Empty,
            "Detached transport must preserve absence instead of importing a stale display."
        );
        bridge.ReleaseOwnership();
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

/// <summary>
/// Lets Unity integration fixtures end individual applications through a nested rules workflow.
/// Production removal remains nested-only; this explicit test extension adds no gameplay action.
/// </summary>
internal sealed class EffectRemovalTestWorkflow : IUnityEncounterDispatcherModule, IActionCatalog
{
    internal static UnityEncounterExtension CreateExtension()
    {
        var module = new EffectRemovalTestWorkflow();
        return new UnityEncounterExtension(module, module);
    }

    internal static void Remove(UnityCombatRulesBridge bridge, RemoveActiveEffectOp removal)
    {
        Assert.Throws<InvalidOperationException>(() => bridge.Dispatch(removal));
        var result = bridge.Dispatch(new RemoveOp(removal));
        Assert.That(result, Is.TypeOf<ResolvedOpResult<OpResult<ActiveEffectRemovalOutcome>>>());
        Assert.That(
            ((ResolvedOpResult<OpResult<ActiveEffectRemovalOutcome>>)result).Value,
            Is.TypeOf<ResolvedOpResult<ActiveEffectRemovalOutcome>>()
        );
    }

    /// <inheritdoc/>
    public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
        builder.RegisterHandler<RemoveOp, OpResult<ActiveEffectRemovalOutcome>>(
            new RemoveHandler()
        );

    /// <inheritdoc/>
    public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
        throw new KeyNotFoundException(
            $"The test removal workflow defines no action '{definitionId}'."
        );

    private sealed class RemoveOp : IRuleOp<OpResult<ActiveEffectRemovalOutcome>>
    {
        internal RemoveOp(RemoveActiveEffectOp removal) => Removal = removal;

        internal RemoveActiveEffectOp Removal { get; }
    }

    private sealed class RemoveHandler : IOpHandler<RemoveOp, OpResult<ActiveEffectRemovalOutcome>>
    {
        public async ValueTask<OpResult<ActiveEffectRemovalOutcome>> Handle(
            OpFrame<RemoveOp> frame,
            OpHandlerContext context
        ) => await context.Dispatch(frame.Op.Removal);
    }
}
