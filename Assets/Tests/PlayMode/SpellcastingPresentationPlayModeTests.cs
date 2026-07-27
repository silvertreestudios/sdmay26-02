using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Light;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#pragma warning disable CS0618 // This fixture verifies the explicit legacy/rules-native split.

public sealed class SpellcastingPresentationPlayModeTests
{
    private readonly List<GameObject> created = new();
    private int gameplayCommitCount;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        OnGameplayStateCommitted.RemoveListener(CountGameplayCommit);
        foreach (GameObject value in created)
            if (value != null)
                Object.Destroy(value);
        created.Clear();
        Pf2eItemCatalog.ResetForTests();
        yield return null;
    }

    [Test]
    public void PreStartInitializationAddsLegacySpellsButDoesNotAddLight()
    {
        CreatureComponent cleric = CreateCreature("Pre-Start Cleric", 0, prepared: false);
        cleric.level = 1;
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        cleric.InitializeRuntimeActions();

        RulesCastSpellAction[] light = controller
            .GetActions()
            .OfType<RulesCastSpellAction>()
            .Where(action => action.Spell == Reference("light"))
            .ToArray();
        Assert.That(light, Is.Empty);
        Assert.That(
            controller
                .GetActions()
                .OfType<CastSpellAction>()
                .Any(action => action.Spell.Slug == "light"),
            Is.False
        );
        Assert.That(
            controller
                .GetActions()
                .OfType<CastSpellAction>()
                .Count(action => action.ActionName == "Shield"),
            Is.EqualTo(1)
        );
    }

    [UnityTest]
    public IEnumerator InitialReinforcementAndUnpreparedInstallationReconcileExactlyOnce()
    {
        CreatureComponent initial = CreateCreature("Initial Cleric", 0, prepared: true);
        TestActionController initialController =
            initial.gameObject.AddComponent<TestActionController>();
        CreatureComponent noncaster = CreateCreature("Noncaster", 1, prepared: false);
        TestActionController noncasterController =
            noncaster.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(3);
        Occupy(tiles, initial.gameObject);
        Occupy(tiles, noncaster.gameObject);

        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { initialController, noncasterController },
            tiles
        );

        Assert.That(LightActions(initialController), Has.Count.EqualTo(1));
        Assert.That(LightActions(noncasterController), Is.Empty);
        Assert.That(
            initialController
                .GetActions()
                .OfType<CastSpellAction>()
                .Any(action => action.Spell.Slug == "shield"),
            Is.True
        );

        CreatureComponent reinforcement = CreateCreature("Reinforcement", 2, prepared: true);
        TestActionController reinforcementController =
            reinforcement.gameObject.AddComponent<TestActionController>();
        yield return null;
        Occupy(tiles, reinforcement.gameObject);
        bridge.RegisterCombatants(new[] { reinforcementController });
        CreatureId reinforcementId = bridge.GetCreatureId(reinforcementController);
        TestSpellActionCatalog repeatCatalog = new(
            UnitySpellDefinitionCatalog.Load(),
            reinforcementId,
            reinforcement.Prepared.SpellBook
        );
        UnitySpellActionInstaller.Install(reinforcementController, repeatCatalog);
        UnitySpellActionInstaller.Install(reinforcementController, repeatCatalog);

        Assert.That(LightActions(reinforcementController), Has.Count.EqualTo(1));
        Assert.That(
            reinforcementController
                .GetActions()
                .OfType<CastSpellAction>()
                .Any(action => action.Spell.Slug == "shield"),
            Is.True
        );
    }

    [UnityTest]
    public IEnumerator ResolvedAndInvalidLightCastsReleaseLockAndOnlyResolvedCreatesVisual()
    {
        CreatureComponent cleric = CreateCreature("Casting Cleric", 0, prepared: true);
        InstallCoroutineRunner();
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(1);
        Occupy(tiles, cleric.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(new[] { controller }, tiles);
        RulesCastSpellAction light = LightActions(controller).Single();

        bridge.BeginTurn(bridge.GetCreatureId(controller), 1);
        controller.IsTakingAction = true;
        light.Invoke(cleric.gameObject);
        yield return null;

        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(VisualLights(cleric), Is.Empty);

        bridge.BeginTurn(bridge.GetCreatureId(controller), 3);
        gameplayCommitCount = 0;
        OnGameplayStateCommitted.AddListener(CountGameplayCommit);
        controller.IsTakingAction = true;
        light.Invoke(cleric.gameObject);
        yield return null;

        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(gameplayCommitCount, Is.EqualTo(1));
        Assert.That(VisualLights(cleric), Has.Count.EqualTo(1));
        Assert.That(VisualLights(cleric).Single().range, Is.EqualTo(4f));

        bridge.ReleaseOwnership();
        yield return null;
        Assert.That(VisualLights(cleric), Is.Empty);
    }

    [UnityTest]
    public IEnumerator GenericExpirationThenRemovalAndDisposeAreIdempotentAndIsolated()
    {
        CreatureComponent owner = CreateCreature("Effect Owner", 0, prepared: false);
        CreatureId ownerId = new("effect-owner");
        RuleDefinitionId lightDefinition = new("spell-effect-light");
        ActiveEffectInstance effect = CreateEffect(
            new ActiveEffectId("effect-light"),
            lightDefinition,
            ownerId
        );
        RulesSnapshot snapshot = new InMemoryRulesStore(
            new RulesStateSeed().SeedActiveEffect(effect)
        ).Snapshot;
        Dictionary<CreatureId, CreatureComponent> creatures = new() { [ownerId] = owner };
        UnityLightEffectPresentationObserver observer = new(lightDefinition, creatures);

        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, new BindingId("binding-light")),
            snapshot
        );
        Assert.That(VisualLights(owner), Has.Count.EqualTo(1));

        ActiveEffectInstance unrelated = CreateEffect(
            new ActiveEffectId("effect-unrelated"),
            new RuleDefinitionId("unrelated"),
            ownerId
        );
        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(unrelated, new BindingId("binding-unrelated")),
            snapshot
        );
        Assert.That(VisualLights(owner), Has.Count.EqualTo(1));

        observer.OnFactCommitted(
            new ActiveEffectExpiredFact(
                effect.Id,
                effect.DefinitionId,
                new BindingId("binding-light"),
                EffectStateVersion.Initial,
                EffectStateVersion.Initial.Next()
            ),
            snapshot
        );
        observer.OnFactCommitted(
            new ActiveEffectRemovedFact(
                effect.Id,
                effect.DefinitionId,
                new BindingId("binding-light"),
                EffectStateVersion.Initial.Next(),
                ActiveEffectStatus.Expired
            ),
            snapshot
        );
        yield return null;
        Assert.That(VisualLights(owner), Is.Empty);

        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, new BindingId("binding-light")),
            snapshot
        );
        observer.Dispose();
        observer.Dispose();
        yield return null;
        Assert.That(VisualLights(owner), Is.Empty);
    }

    private CreatureComponent CreateCreature(string name, int x, bool prepared)
    {
        GameObject value = new(name);
        created.Add(value);
        value.transform.position = new Vector3(x, 0, 0);
        CreatureComponent creature = value.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(10, 10);
        if (prepared)
        {
            creature.level = 1;
            creature.wisMod = 4;
            creature.Build = new CharacterBuild { ClassName = "Cleric" };
            creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
            value.AddComponent<Team>().Name = "players";
        }
        return creature;
    }

    private static List<RulesCastSpellAction> LightActions(ActionController controller) =>
        controller
            .GetActions()
            .OfType<RulesCastSpellAction>()
            .Where(action => action.Spell == Reference("light"))
            .ToList();

    private static List<UnityEngine.Light> VisualLights(CreatureComponent owner) =>
        owner.GetComponentsInChildren<UnityEngine.Light>(includeInactive: true).ToList();

    private static SpellReference Reference(string slug) => new(new SpellId(slug), 1);

    private static ActiveEffectInstance CreateEffect(
        ActiveEffectId id,
        RuleDefinitionId definition,
        CreatureId owner
    ) =>
        new(
            id,
            definition,
            owner,
            RuleSource.FromSlug("test-spell"),
            EffectDuration.Indefinite,
            new SpellEffectState(Reference("light"), owner)
        );

    private static Tile[,] CreateTiles(int width)
    {
        Tile[,] tiles = new Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new Tile();
        return tiles;
    }

    private static void Occupy(Tile[,] tiles, GameObject value)
    {
        int x = Mathf.RoundToInt(value.transform.position.x);
        tiles[x, 0].Occupants.Add(value);
    }

    private void InstallCoroutineRunner()
    {
        GameObject gameObject = new("Spellcasting PlayMode Coroutine Runner");
        created.Add(gameObject);
        gameObject.AddComponent<CoroutineRunner>();
    }

    private void CountGameplayCommit()
    {
        gameplayCommitCount++;
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class TestSpellActionCatalog : ISpellActionCatalog
    {
        private readonly UnitySpellDefinitionCatalog definitions;
        private readonly CreatureId owner;
        private readonly ISpellBook book;

        public TestSpellActionCatalog(
            UnitySpellDefinitionCatalog definitions,
            CreatureId owner,
            ISpellBook book
        )
        {
            this.definitions = definitions;
            this.owner = owner;
            this.book = book;
        }

        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
            definitions.GetBaseProfile(definitionId);

        public bool TryGetSpell(
            SpellReference reference,
            out Game.Rules.Runtime.SpellDefinition definition
        ) => definitions.TryGetSpell(reference, out definition);

        public ISpellBook GetSpellBook(CreatureId creature) =>
            creature == owner ? book : EmptySpellBook.Instance;
    }
}

#pragma warning restore CS0618
