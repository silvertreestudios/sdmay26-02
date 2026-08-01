using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ConditionUnityIntegrationTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private UnityEngine.Random.State randomState;

    [SetUp]
    public void SetUp() => randomState = UnityEngine.Random.state;

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Random.state = randomState;
        foreach (GameObject gameObject in created)
        {
            if (gameObject != null)
                Object.DestroyImmediate(gameObject);
        }
        created.Clear();
    }

    [Test]
    public void HauntingHymnCriticalFailureAppliesDeafenedThroughSameBridge()
    {
        CreatureFixture caster = CreateCreature("Caster", "Heroes", 100);
        CreatureFixture target = CreateCreature("Target", "Enemies", 0);
        caster.Creature.Prepared = new PreparedCharacter(new CharacterBuild())
        {
            Spellcasting = new SpellcastingState { SpellAttackModifier = 100 },
        };
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster.Controller, target.Controller },
            CreateTiles()
        );
        CreatureId targetId = bridge.GetCreatureId(target.Creature);
        UnityEngine.Random.InitState(1);

        SpellcastingRuntime.ApplyBasicFortitudeDamage(
            caster.GameObject,
            target.GameObject,
            new DamageValue("sonic", 1),
            new CastSpellResult(),
            applyDeafenedOnCriticalFailure: true,
            RuleSource.FromSlug("haunting-hymn-test")
        );

        Assert.That(
            ConditionSelectors.HasMarker(
                bridge.Snapshot,
                targetId,
                ConditionRuleDefinitions.Deafened
            ),
            Is.True
        );
        Assert.That(target.Conditions.ActiveConditionNames, Does.Contain("deafened"));
    }

    [Test]
    public void PersistenceSeedsInitialAndReinforcementConditionsIntoOneStore()
    {
        CreatureFixture initial = CreateCreature("Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Opponent", "Enemies", 0);
        initial.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    initial.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "initial-fatigue",
                    ConditionMarkerState.Instance
                ),
            }
        );
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { initial.Controller, opponent.Controller },
            CreateTiles()
        );
        bridge.StartEncounter("Heroes");
        CreatureId initialId = bridge.GetCreatureId(initial.Creature);

        CreatureFixture reinforcement = CreateCreature("Reinforcement", "Enemies", -1);
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    reinforcement.GameObject,
                    ConditionRuleDefinitions.Encumbered,
                    "reinforcement-load",
                    ConditionMarkerState.Instance
                ),
            }
        );
        bridge.RegisterCombatants(new[] { reinforcement.Controller });
        CreatureId reinforcementId = bridge.GetCreatureId(reinforcement.Creature);

        Assert.That(
            ConditionSelectors.HasMarker(
                bridge.Snapshot,
                initialId,
                ConditionRuleDefinitions.Fatigued
            ),
            Is.True
        );
        Assert.That(
            ConditionSelectors.HasMarker(
                bridge.Snapshot,
                reinforcementId,
                ConditionRuleDefinitions.Encumbered
            ),
            Is.True
        );
        Assert.That(
            ConditionSelectors
                .GetActiveInstances(
                    bridge.Snapshot,
                    reinforcementId,
                    ConditionRuleDefinitions.Encumbered
                )
                .Count,
            Is.EqualTo(1)
        );
    }

    [Test]
    public void ConsumedRestoreNeverBecomesDetachedAuthorityAndExplicitReRestoreReenrollsMutation()
    {
        CreatureFixture actor = CreateCreature("Actor", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Opponent", "Enemies", 0);
        actor.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    actor.GameObject,
                    ConditionRuleDefinitions.Slowed,
                    "restored-slowed",
                    new SlowedConditionState(1)
                ),
            }
        );
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        CreatureId actorId = first.GetCreatureId(actor.Creature);
        Assert.That(
            ConditionSelectors.TryGetSlowed(first.Snapshot, actorId, out var slowed),
            Is.True
        );
        Assert.That(
            first.Dispatch(
                new CleanupConditionsFromSourceOp(
                    slowed.Source,
                    ConditionCleanupKind.Expire,
                    actorId,
                    ConditionRuleDefinitions.Slowed
                )
            ),
            Is.TypeOf<ResolvedOpResult<ConditionCleanupOutcome>>()
        );
        ConditionApplicationSnapshot[] live = actor.Conditions.CaptureApplications().ToArray();

        first.ReleaseOwnership();

        Assert.That(actor.Conditions.ActiveConditionNames, Is.Empty);
        Assert.Throws<InvalidOperationException>(() => actor.Conditions.CaptureApplications());
        actor.Conditions.RestoreApplications(live);
        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles()
        );
        CreatureId reenrolledId = second.GetCreatureId(actor.Creature);
        Assert.That(
            ConditionSelectors.TryGetSlowed(second.Snapshot, reenrolledId, out _),
            Is.False
        );
        ActiveEffectInstance restored = second.Snapshot.ActiveEffects[slowed.EffectId];
        Assert.That(restored.Status, Is.EqualTo(ActiveEffectStatus.Expired));
        Assert.That(restored.EffectStateVersion.Value, Is.EqualTo(1));
    }

    private static ConditionApplicationSnapshot Persisted(
        GameObject sourceCreature,
        RuleDefinitionId definition,
        string identity,
        IEffectState state
    )
    {
        RuleSource source = RuleSource.FromSlug(identity);
        return new ConditionApplicationSnapshot(
            new ActiveEffectId($"effect-{identity}"),
            new BindingId($"binding-{identity}"),
            definition,
            sourceCreature,
            source,
            EffectDuration.Indefinite,
            EffectStateVersion.Initial,
            state,
            ActiveEffectStatus.Active,
            1,
            true,
            null
        );
    }

    private CreatureFixture CreateCreature(string name, string teamName, int initiative)
    {
        GameObject gameObject = new GameObject(name);
        created.Add(gameObject);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.initiative = initiative;
        creature.InitializeHealthBeforeEncounter(20, 20);
        Conditions conditions = gameObject.AddComponent<Conditions>();
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        ConditionTestActionController controller =
            gameObject.AddComponent<ConditionTestActionController>();
        return new CreatureFixture(gameObject, creature, conditions, controller);
    }

    private static Tile[,] CreateTiles() =>
        new[,]
        {
            { new Tile() },
        };

    private sealed class ConditionTestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class CreatureFixture
    {
        internal CreatureFixture(
            GameObject gameObject,
            CreatureComponent creature,
            Conditions conditions,
            ConditionTestActionController controller
        )
        {
            GameObject = gameObject;
            Creature = creature;
            Conditions = conditions;
            Controller = controller;
        }

        internal GameObject GameObject { get; }
        internal CreatureComponent Creature { get; }
        internal Conditions Conditions { get; }
        internal ConditionTestActionController Controller { get; }
    }
}
