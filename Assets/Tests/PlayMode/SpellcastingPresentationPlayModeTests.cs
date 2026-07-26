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

public sealed class SpellcastingPresentationPlayModeTests
{
    private readonly List<GameObject> created = new();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject value in created)
            if (value != null)
                Object.Destroy(value);
        created.Clear();
        Pf2eItemCatalog.ResetForTests();
        yield return null;
    }

    [Test]
    public void PreStartInstallationIsIdempotentAndUsesCastSpellActionForLight()
    {
        CreatureComponent cleric = CreateCreature("Pre-Start Cleric", 0, prepared: false);
        cleric.level = 1;
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        UnitySpellDefinitionCatalog catalog = UnitySpellDefinitionCatalog.Load();

        cleric.InitializeRuntimeActions();
        UnitySpellActionInstaller.Install(controller, catalog);
        UnitySpellActionInstaller.Install(controller, catalog);

        CastSpellAction[] light = controller
            .GetActions()
            .OfType<CastSpellAction>()
            .Where(action => action.Spell == Reference("light"))
            .ToArray();
        Assert.That(light, Has.Length.EqualTo(1));
        Assert.That(light[0].Variant, Is.EqualTo(new SpellActionVariant(2)));
        Assert.That(
            controller.GetActions().Count(action => action.ActionName == "Shield"),
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

        CreatureComponent reinforcement = CreateCreature("Reinforcement", 2, prepared: true);
        TestActionController reinforcementController =
            reinforcement.gameObject.AddComponent<TestActionController>();
        yield return null;
        Occupy(tiles, reinforcement.gameObject);
        bridge.RegisterCombatants(new[] { reinforcementController });
        UnitySpellActionInstaller.Install(
            reinforcementController,
            UnitySpellDefinitionCatalog.Load()
        );

        Assert.That(LightActions(reinforcementController), Has.Count.EqualTo(1));
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
        CastSpellAction light = LightActions(controller).Single();

        bridge.BeginTurn(bridge.GetCreatureId(controller), 1);
        controller.IsTakingAction = true;
        light.Invoke(cleric.gameObject);
        yield return null;

        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(VisualLights(cleric), Is.Empty);

        bridge.BeginTurn(bridge.GetCreatureId(controller), 3);
        controller.IsTakingAction = true;
        light.Invoke(cleric.gameObject);
        yield return null;

        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        Assert.That(controller.IsTakingAction, Is.False);
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

    private static List<CastSpellAction> LightActions(ActionController controller) =>
        controller
            .GetActions()
            .OfType<CastSpellAction>()
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

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }
}
