using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
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
    public void InitialInstallationFailurePreservesRestoreUntilWholeBatchRetryFinalizes()
    {
        CreatureFixture actor = CreateCreature("Initial Restore Actor", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Initial Restore Opponent", "Enemies", 0);
        actor.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    actor.GameObject,
                    ConditionRuleDefinitions.Stunned,
                    "initial-failed-stunned",
                    new ValuedStunnedConditionState(2)
                ),
            }
        );
        ControllableInstallationModule installer = new ControllableInstallationModule
        {
            TargetName = actor.GameObject.name,
            FailuresRemaining = 1,
        };

        Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.CreateForTests(
                new[] { actor.Controller, opponent.Controller },
                CreateTiles(),
                new RandomRollService(),
                new IUnityEncounterModule[] { installer }
            )
        );

        Assert.That(actor.Conditions.HasPendingRestore, Is.True);
        UnityCombatRulesBridge retry = UnityCombatRulesBridge.CreateForTests(
            new[] { actor.Controller, opponent.Controller },
            CreateTiles(),
            new RandomRollService(),
            new IUnityEncounterModule[] { installer }
        );
        CreatureId actorId = retry.GetCreatureId(actor.Creature);
        Assert.That(actor.Conditions.HasPendingRestore, Is.False);
        Assert.That(
            ConditionSelectors.TryGetStunned(retry.Snapshot, actorId, out var stunned),
            Is.True
        );
        Assert.That(stunned.State, Is.TypeOf<ValuedStunnedConditionState>());
        Assert.That(((ValuedStunnedConditionState)stunned.State).Value, Is.EqualTo(2));
        retry.ReleaseOwnership();
    }

    [Test]
    public void ReinforcementInstallationFailureRetriesCommittedBatchWithoutDuplicateStateOrFacts()
    {
        CreatureFixture initial = CreateCreature("Retry Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Retry Opponent", "Enemies", 0);
        ControllableInstallationModule installer = new ControllableInstallationModule();
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.CreateForTests(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles(),
            new RandomRollService(),
            new IUnityEncounterModule[] { installer }
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature("Retry Reinforcement", "Enemies", -1);
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    reinforcement.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "reinforcement-failed-fatigue",
                    ConditionMarkerState.Instance
                ),
            }
        );
        installer.TargetName = reinforcement.GameObject.name;
        installer.FailuresRemaining = 1;
        CountingFactObserver<EncounterJoinedFact> joined =
            new CountingFactObserver<EncounterJoinedFact>();
        CountingFactObserver<ActiveEffectAdoptedFact> adopted =
            new CountingFactObserver<ActiveEffectAdoptedFact>();
        CountingFactObserver<ActiveEffectCreatedFact> created =
            new CountingFactObserver<ActiveEffectCreatedFact>();
        RuleDispatcher dispatcher = GetDispatcher(bridge);
        using IDisposable joinedRegistration = dispatcher.RegisterFactObserver<EncounterJoinedFact>(
            joined
        );
        using IDisposable adoptedRegistration =
            dispatcher.RegisterFactObserver<ActiveEffectAdoptedFact>(adopted);
        using IDisposable createdRegistration =
            dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(created);

        Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { reinforcement.Controller })
        );

        ActiveEffectId effectId = new ActiveEffectId("effect-reinforcement-failed-fatigue");
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.True);
        Assert.That(bridge.Snapshot.ActiveEffects.Contains(effectId), Is.True);
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(adopted.Count, Is.EqualTo(1));
        Assert.That(created.Count, Is.Zero);
        long committedVersion = bridge.Snapshot.Version;

        Assert.DoesNotThrow(() => bridge.RegisterCombatants(new[] { reinforcement.Controller }));

        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.False);
        Assert.That(
            bridge.Snapshot.ActiveEffects.Count(pair => pair.Key == effectId),
            Is.EqualTo(1)
        );
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(committedVersion));
        Assert.That(bridge.GetEncounter().Roster, Has.Count.EqualTo(3));
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(adopted.Count, Is.EqualTo(1));
        Assert.That(created.Count, Is.Zero);
        bridge.ReleaseOwnership();
    }

    [Test]
    public void ConditionAdoptionPostCommitFailureRetriesWithoutDuplicateStateOrFacts()
    {
        CreatureFixture initial = CreateCreature("Adoption Retry Initial", "Heroes", 100);
        CreatureFixture opponent = CreateCreature("Adoption Retry Opponent", "Enemies", 0);
        ScriptedRollService rolls = new ScriptedRollService(20, 10, 1);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { initial.Controller, opponent.Controller },
            CreateTiles(),
            rolls
        );
        bridge.StartEncounter("Heroes");
        CreatureFixture reinforcement = CreateCreature(
            "Adoption Retry Reinforcement",
            "Enemies",
            -1
        );
        reinforcement.Conditions.RestoreApplications(
            new[]
            {
                Persisted(
                    reinforcement.GameObject,
                    ConditionRuleDefinitions.Fatigued,
                    "condition-post-commit-retry",
                    ConditionMarkerState.Instance
                ),
            }
        );
        InvalidOperationException expected = new InvalidOperationException(
            "Injected condition adoption observer failure."
        );
        ThrowOnceFactObserver<ActiveEffectAdoptedFact> observer = new(expected);
        using IDisposable registration = GetDispatcher(bridge)
            .RegisterFactObserver<ActiveEffectAdoptedFact>(observer);

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            bridge.RegisterCombatants(new[] { reinforcement.Controller })
        );

        Assert.That(actual, Is.SameAs(expected));
        Assert.That(observer.Count, Is.EqualTo(1));
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.True);
        Assert.That(rolls.Remaining, Is.Zero);
        long failedVersion = bridge.Snapshot.Version;

        Assert.DoesNotThrow(() => bridge.RegisterCombatants(new[] { reinforcement.Controller }));

        ActiveEffectId effect = new ActiveEffectId("effect-condition-post-commit-retry");
        Assert.That(observer.Count, Is.EqualTo(1));
        Assert.That(bridge.Snapshot.ActiveEffects.Count(pair => pair.Key == effect), Is.EqualTo(1));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(failedVersion + 1));
        Assert.That(reinforcement.Conditions.HasPendingRestore, Is.False);
        Assert.That(rolls.Remaining, Is.Zero);
        bridge.ReleaseOwnership();
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

    private static RuleDispatcher GetDispatcher(UnityCombatRulesBridge bridge)
    {
        var field = typeof(UnityCombatRulesBridge).GetField(
            "dispatcher",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        return (RuleDispatcher)field.GetValue(bridge);
    }

    private sealed class ControllableInstallationModule : IUnityCombatantEnrollmentModule
    {
        internal string TargetName { get; set; } = string.Empty;
        internal int FailuresRemaining { get; set; }

        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            bool targeted = builder.Controller.gameObject.name == TargetName;
            builder.AddInstallation(new ControllableInstallation(this, targeted));
        }

        private sealed class ControllableInstallation : IUnityCombatantInstallationContribution
        {
            private readonly ControllableInstallationModule owner;
            private readonly bool targeted;

            internal ControllableInstallation(ControllableInstallationModule owner, bool targeted)
            {
                this.owner = owner;
                this.targeted = targeted;
            }

            public void Reconcile()
            {
                if (!targeted || owner.FailuresRemaining == 0)
                    return;
                owner.FailuresRemaining--;
                throw new InvalidOperationException("Injected late installation failure.");
            }
        }
    }

    private sealed class CountingFactObserver<TFact> : IFactObserver<TFact>
        where TFact : RuleFact
    {
        internal int Count { get; private set; }

        public ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot)
        {
            Count++;
            return default;
        }
    }

    private sealed class ThrowOnceFactObserver<TFact> : IFactObserver<TFact>
        where TFact : RuleFact
    {
        private readonly Exception failure;

        internal ThrowOnceFactObserver(Exception failure) => this.failure = failure;

        internal int Count { get; private set; }

        public ValueTask OnFactCommitted(TFact fact, RulesSnapshot currentSnapshot)
        {
            Count++;
            if (Count == 1)
                throw failure;
            return default;
        }
    }

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
