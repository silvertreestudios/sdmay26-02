using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Actors;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class RulesSpellEffectPersistenceTests
{
    private static readonly SpellReference Light = new(new SpellId("light"), 1);
    private readonly List<GameObject> created = new();
    private UnityEngine.Random.State randomState;

    [SetUp]
    public void SetUp() => randomState = UnityEngine.Random.state;

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Random.state = randomState;
        foreach (GameObject value in created)
        {
            if (value != null)
                Object.DestroyImmediate(value);
        }
        created.Clear();
    }

    [Test]
    public void LiveLightReleasesToPendingSkipsExplorationAndReenrollsExactly()
    {
        TestActionController caster = CreateActor(
            "Light Caster",
            "light-caster",
            "Players",
            preparedCleric: true
        );
        TestActionController opponent = CreateActor(
            "Light Opponent",
            "light-opponent",
            "Enemies",
            preparedCleric: false
        );
        Tile[,] tiles = CreateTiles(2);
        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new ActionController[] { caster, opponent },
            tiles
        );
        CreatureId casterId = first.GetCreatureId(caster);

        CastLight(first, casterId, "live-light-persistence");

        RulesSpellEffectPersistence persistence =
            caster.GetComponent<RulesSpellEffectPersistence>();
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();
        RulesSpellEffectSnapshot captured = persistence.CaptureEffects(catalog).Single();
        ActiveEffectInstance authoritative = first.Snapshot.ActiveEffects[captured.EffectId];
        ActiveRuleBinding authoritativeBinding = first.Snapshot.RuleBindings[captured.BindingId];
        Assert.That(caster.GetComponent<SpellEffectController>(), Is.Null);
        Assert.That(authoritative.GetState<SpellEffectState>().Spell, Is.EqualTo(Light));

        first.ReleaseOwnership();

        Assert.That(persistence.HasPendingRestore, Is.True);
        Assert.That(
            persistence.CaptureEffects(catalog).Single().EffectId,
            Is.EqualTo(captured.EffectId)
        );
        UnityCombatRulesBridge exploration = UnityCombatRulesBridge.CreateExplorationStride(
            caster,
            tiles
        );
        Assert.That(exploration.Snapshot.ActiveEffects, Is.Empty);
        exploration.ReleaseOwnership();
        Assert.That(persistence.HasPendingRestore, Is.True);

        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new ActionController[] { caster, opponent },
            tiles
        );

        Assert.That(persistence.HasPendingRestore, Is.False);
        Assert.That(second.Snapshot.ActiveEffects[captured.EffectId], Is.EqualTo(authoritative));
        Assert.That(
            second.Snapshot.RuleBindings[captured.BindingId],
            Is.EqualTo(authoritativeBinding)
        );
        Assert.That(second.Snapshot.ActiveEffectTimings.Contains(captured.EffectId), Is.False);
        second.ReleaseOwnership();
    }

    [Test]
    public void ReinforcementFailureLeavesPendingUntilExactAdoptionRetryFinalizes()
    {
        TestActionController reinforcement = CreateActor(
            "Reinforcement Caster",
            "reinforcement-caster",
            "Enemies",
            preparedCleric: true
        );
        TestActionController seedOpponent = CreateActor(
            "Seed Opponent",
            "seed-opponent",
            "Players",
            preparedCleric: false
        );
        Tile[,] seedTiles = CreateTiles(2);
        UnityCombatRulesBridge seed = UnityCombatRulesBridge.Create(
            new ActionController[] { reinforcement, seedOpponent },
            seedTiles
        );
        CastLight(seed, seed.GetCreatureId(reinforcement), "reinforcement-light");
        seed.ReleaseOwnership();
        RulesSpellEffectPersistence persistence =
            reinforcement.GetComponent<RulesSpellEffectPersistence>();
        ActiveEffectId expectedEffect = persistence
            .CaptureEffects(UnitySpellDefinitionCatalog.Load())
            .Single()
            .EffectId;

        TestActionController host = CreateActor(
            "Reinforcement Host",
            "reinforcement-host",
            "Players",
            preparedCleric: false
        );
        TestActionController anchor = CreateActor(
            "Reinforcement Anchor",
            "reinforcement-anchor",
            "Enemies",
            preparedCleric: false
        );
        UnityCombatRulesBridge encounter = UnityCombatRulesBridge.Create(
            new ActionController[] { host, anchor },
            CreateTiles(3)
        );
        encounter.StartEncounter("Players");
        InvalidOperationException expectedFailure = new("injected adopted-fact failure");
        ThrowOnceAdoptedObserver observer = new(expectedFailure);
        using IDisposable registration = GetDispatcher(encounter)
            .RegisterFactObserver<ActiveEffectAdoptedFact>(observer);

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            encounter.RegisterCombatants(new[] { reinforcement })
        );

        Assert.That(actual, Is.SameAs(expectedFailure));
        Assert.That(observer.Count, Is.EqualTo(1));
        Assert.That(persistence.HasPendingRestore, Is.True);
        Assert.That(encounter.Snapshot.ActiveEffects.Contains(expectedEffect), Is.True);

        Assert.DoesNotThrow(() => encounter.RegisterCombatants(new[] { reinforcement }));

        Assert.That(observer.Count, Is.EqualTo(1));
        Assert.That(persistence.HasPendingRestore, Is.False);
        Assert.That(
            encounter.Snapshot.ActiveEffects.Count(pair => pair.Key == expectedEffect),
            Is.EqualTo(1)
        );
        encounter.ReleaseOwnership();
    }

    [Test]
    public void NondurableOwnerCapturesCanonicalEmptyWithoutInspectingItsLight()
    {
        TestActionController caster = CreateActor(
            "Nondurable Light Caster",
            string.Empty,
            "Players",
            preparedCleric: true
        );
        TestActionController opponent = CreateActor(
            "Nondurable Opponent",
            "durable-opponent",
            "Enemies",
            preparedCleric: false
        );
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { caster, opponent },
            CreateTiles(2)
        );
        CastLight(bridge, bridge.GetCreatureId(caster), "nondurable-light");
        RulesSpellEffectPersistence persistence =
            caster.GetComponent<RulesSpellEffectPersistence>();

        Assert.That(persistence.CaptureEffects(UnitySpellDefinitionCatalog.Load()), Is.Empty);
        Assert.DoesNotThrow(() => bridge.ReleaseOwnership());
        Assert.That(persistence.HasPendingRestore, Is.True);
        Assert.That(persistence.CaptureEffects(UnitySpellDefinitionCatalog.Load()), Is.Empty);
    }

    [Test]
    public void ForeignSourceSelfLightRejectsBeforeSourceLookupAndCorrectedInputCanRetry()
    {
        TestActionController owner = CreateActor(
            "Restored Light Owner",
            "restored-light-owner",
            "Players",
            preparedCleric: false
        );
        TestActionController opponent = CreateActor(
            "Restored Light Opponent",
            "restored-light-opponent",
            "Enemies",
            preparedCleric: false
        );
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();
        Assert.That(
            catalog.TryGetSpell(Light, out Game.Rules.Runtime.SpellDefinition definition),
            Is.True
        );
        SpellEffectDirective directive = definition.Effects.Single();
        RulesSpellEffectPersistence persistence =
            owner.gameObject.AddComponent<RulesSpellEffectPersistence>();
        persistence.RestoreEffects(
            new[]
            {
                new RulesSpellEffectSnapshot(
                    new ActiveEffectId("missing-source-light"),
                    new BindingId("missing-source-light-binding"),
                    directive.DefinitionId,
                    "missing-light-source",
                    "restored-light-owner",
                    RuleSource.FromSlug("light"),
                    directive.Duration,
                    EffectStateVersion.Initial,
                    ActiveEffectStatus.Active,
                    0,
                    true,
                    Light,
                    null
                ),
            }
        );

        InvalidOperationException missingSourceFailure = Assert.Throws<InvalidOperationException>(
            () =>
                UnityCombatRulesBridge.Create(
                    new ActionController[] { owner, opponent },
                    CreateTiles(2)
                )
        );

        string targetContractFailure =
            $"Rules-native spell effect {directive.DefinitionId.Value} does not exactly match the self-target catalog contract for {Light}: source durable actor 'missing-light-source' does not match target durable actor 'restored-light-owner'.";
        Assert.That(missingSourceFailure.Message, Is.EqualTo(targetContractFailure));
        Assert.That(persistence.HasPendingRestore, Is.True);
        Assert.That(owner.TryGetCombatRules(out _, out _), Is.False);

        TestActionController source = CreateActor(
            "Restored Light Source",
            "missing-light-source",
            "Players",
            preparedCleric: false
        );
        InvalidOperationException foreignFailure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new ActionController[] { owner, source, opponent },
                CreateTiles(3)
            )
        );

        Assert.That(foreignFailure.Message, Is.EqualTo(targetContractFailure));
        Assert.That(persistence.HasPendingRestore, Is.True);
        Assert.That(owner.TryGetCombatRules(out _, out _), Is.False);

        persistence.RestoreEffects(
            new[]
            {
                new RulesSpellEffectSnapshot(
                    new ActiveEffectId("missing-source-light"),
                    new BindingId("missing-source-light-binding"),
                    directive.DefinitionId,
                    "restored-light-owner",
                    "restored-light-owner",
                    RuleSource.FromSlug("light"),
                    directive.Duration,
                    EffectStateVersion.Initial,
                    ActiveEffectStatus.Active,
                    0,
                    true,
                    Light,
                    null
                ),
            }
        );
        UnityCombatRulesBridge retry = UnityCombatRulesBridge.Create(
            new ActionController[] { owner, source, opponent },
            CreateTiles(3)
        );

        Assert.That(persistence.HasPendingRestore, Is.False);
        ActiveEffectInstance restored = retry.Snapshot.ActiveEffects[
            new ActiveEffectId("missing-source-light")
        ];
        Assert.That(restored.SourceCreature, Is.EqualTo(retry.GetCreatureId(owner)));
        Assert.That(
            restored.GetState<SpellEffectState>().Target,
            Is.EqualTo(retry.GetCreatureId(owner))
        );
        retry.ReleaseOwnership();
    }

    [Test]
    public void OverCapPendingLightRestoreFailsBeforeEnrollmentAndPreservesInput()
    {
        TestActionController owner = CreateActor(
            "Over-Cap Light Owner",
            "over-cap-light-owner",
            "Players",
            preparedCleric: false
        );
        TestActionController opponent = CreateActor(
            "Over-Cap Light Opponent",
            "over-cap-light-opponent",
            "Enemies",
            preparedCleric: false
        );
        RulesSpellEffectPersistence persistence =
            owner.gameObject.AddComponent<RulesSpellEffectPersistence>();
        persistence.RestoreEffects(CreateLightSnapshots("over-cap-light-owner", 5));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            UnityCombatRulesBridge.Create(
                new ActionController[] { owner, opponent },
                CreateTiles(2)
            )
        );

        Assert.That(failure.Message, Does.Contain("active-instance invariant violated"));
        Assert.That(failure.Message, Does.Contain("found 5"));
        Assert.That(failure.Message, Does.Contain("catalog maximum 4"));
        Assert.That(persistence.HasPendingRestore, Is.True);
        Assert.That(owner.TryGetCombatRules(out _, out _), Is.False);
    }

    [Test]
    public void PendingLightRestoreAcceptsCatalogCapAcrossRanks()
    {
        TestActionController owner = CreateActor(
            "At-Cap Light Owner",
            "at-cap-light-owner",
            "Players",
            preparedCleric: false
        );
        TestActionController opponent = CreateActor(
            "At-Cap Light Opponent",
            "at-cap-light-opponent",
            "Enemies",
            preparedCleric: false
        );
        RulesSpellEffectPersistence persistence =
            owner.gameObject.AddComponent<RulesSpellEffectPersistence>();
        persistence.RestoreEffects(CreateLightSnapshots("at-cap-light-owner", 4));

        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { owner, opponent },
            CreateTiles(2)
        );

        CreatureId ownerId = bridge.GetCreatureId(owner);
        Assert.That(persistence.HasPendingRestore, Is.False);
        Assert.That(
            bridge
                .Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Count(effect =>
                    effect.SourceCreature == ownerId
                    && effect.GetState<SpellEffectState>().Spell.Spell == Light.Spell
                ),
            Is.EqualTo(4)
        );
        Assert.That(
            bridge
                .Snapshot.ActiveEffects.Select(pair =>
                    pair.Value.GetState<SpellEffectState>().Spell.Rank
                )
                .Distinct(),
            Is.EquivalentTo(new[] { 1, 2 })
        );
        bridge.ReleaseOwnership();
    }

    private TestActionController CreateActor(
        string name,
        string durableId,
        string teamName,
        bool preparedCleric
    )
    {
        GameObject value = new(name);
        created.Add(value);
        if (durableId.Length > 0)
            value
                .AddComponent<DungeonPartyMemberIdentity>()
                .Configure(durableId, name + "-content");
        CreatureComponent creature = value.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(10, 10);
        creature.speed = 25;
        if (preparedCleric)
        {
            creature.level = 1;
            creature.wisMod = 4;
            creature.Build = new CharacterBuild { ClassName = "Cleric" };
            creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
        }
        value.AddComponent<Team>().Name = teamName;
        return value.AddComponent<TestActionController>();
    }

    private static void CastLight(
        UnityCombatRulesBridge bridge,
        CreatureId caster,
        string invocation
    )
    {
        bridge.BeginTurn(caster, 3);
        OpResult<CastSpellOutcome> result = bridge.Dispatch(
            new CastSpellActionOp(
                new ActionInvocationId(invocation),
                caster,
                Light,
                new SpellActionVariant(2),
                SpellCastSelection.Empty
            )
        );
        Assert.That(result.Status, Is.EqualTo(OpStatus.Resolved));
    }

    private static RulesSpellEffectSnapshot[] CreateLightSnapshots(string ownerId, int count)
    {
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();
        Assert.That(
            catalog.TryGetSpell(Light, out Game.Rules.Runtime.SpellDefinition definition),
            Is.True
        );
        SpellEffectDirective directive = definition.Effects.Single();
        return Enumerable
            .Range(0, count)
            .Select(index => new RulesSpellEffectSnapshot(
                new ActiveEffectId($"persisted-light-effect-{index}"),
                new BindingId($"persisted-light-binding-{index}"),
                directive.DefinitionId,
                ownerId,
                ownerId,
                RuleSource.FromSlug("light"),
                directive.Duration,
                EffectStateVersion.Initial,
                ActiveEffectStatus.Active,
                index,
                true,
                new SpellReference(Light.Spell, (index % 2) + 1),
                null
            ))
            .ToArray();
    }

    private static Tile[,] CreateTiles(int width)
    {
        Tile[,] tiles = new Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new Tile();
        return tiles;
    }

    private static RuleDispatcher GetDispatcher(UnityCombatRulesBridge bridge) =>
        (RuleDispatcher)
            typeof(UnityCombatRulesBridge)
                .GetField("dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(bridge);

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class ThrowOnceAdoptedObserver : IFactObserver<ActiveEffectAdoptedFact>
    {
        private readonly Exception failure;

        internal ThrowOnceAdoptedObserver(Exception failure) => this.failure = failure;

        internal int Count { get; private set; }

        public System.Threading.Tasks.ValueTask OnFactCommitted(
            ActiveEffectAdoptedFact fact,
            RulesSnapshot currentSnapshot
        )
        {
            Count++;
            if (Count == 1)
                throw failure;
            return default;
        }
    }
}
