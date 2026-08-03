using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Light;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SpellcastingPresentationPlayModeTests
{
    private readonly List<GameObject> created = new();
    private int gameplayCommitCount;
    private int actionCompleteCount;
    private int damageEventCount;
    private int missEventCount;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        if (!CombatManagerInterface.TryGetInstance(out _))
        {
            GameObject manager = new("Spellcasting PlayMode Combat Manager");
            created.Add(manager);
            manager.AddComponent<CombatManager>();
        }
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        OnGameplayStateCommitted.RemoveListener(CountGameplayCommit);
        OnActionComplete.RemoveListener(CountActionComplete);
        OnDamageDealt.RemoveListener(CountDamageEvent);
        OnAttackMiss.RemoveListener(CountMissEvent);
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
        Assert.That(RulesActions(controller, "divine-lance"), Is.Empty);
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
                .Any(action => action.Spell.Slug == "divine-lance"),
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
        noncaster.gameObject.AddComponent<Team>().Name = "enemies";
        yield return null;
        Tile[,] tiles = CreateTiles(3);
        Occupy(tiles, initial.gameObject);
        Occupy(tiles, noncaster.gameObject);

        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { initialController, noncasterController },
            tiles
        );
        bridge.StartEncounter("players");

        Assert.That(LightActions(initialController), Has.Count.EqualTo(1));
        Assert.That(RulesActions(initialController, "divine-lance"), Has.Count.EqualTo(1));
        Assert.That(LightActions(noncasterController), Is.Empty);
        Assert.That(RulesActions(noncasterController, "divine-lance"), Is.Empty);
        Assert.That(
            initialController.GetActions().OfType<CastSpellAction>(),
            Is.Empty,
            "Encounter composition must remove Shield and every other legacy spell action."
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
        UnitySpellActionInstaller.Install(reinforcementController, reinforcementId, repeatCatalog);
        UnitySpellActionInstaller.Install(reinforcementController, reinforcementId, repeatCatalog);

        Assert.That(LightActions(reinforcementController), Has.Count.EqualTo(1));
        Assert.That(RulesActions(reinforcementController, "divine-lance"), Has.Count.EqualTo(1));
        Assert.That(
            reinforcementController.GetActions().OfType<CastSpellAction>(),
            Is.Empty,
            "Reinforcement composition must remove Shield and every other legacy spell action."
        );
    }

    [Test]
    public void SpellInstallationOrdersActionsBySlugRankAndActionCost()
    {
        CreatureComponent caster = CreateCreature("Ordered Spell Caster", 0, prepared: false);
        TestActionController controller = caster.gameObject.AddComponent<TestActionController>();
        CreatureId owner = new("ordered-spell-caster");
        SpellReference alphaRankOne = new(new SpellId("alpha"), 1);
        SpellReference alphaRankTwo = new(new SpellId("alpha"), 2);
        SpellReference zetaRankOne = new(new SpellId("zeta"), 1);
        SpellReference zetaRankTwo = new(new SpellId("zeta"), 2);
        SpellEffectDirective effect = new(
            new RuleDefinitionId("test-installation-order-effect"),
            EffectDuration.Indefinite,
            "self"
        );
        UnitySpellDefinitionCatalog definitions = new(
            new[]
            {
                new Game.Rules.Runtime.SpellDefinition(
                    zetaRankOne.Spell,
                    "Zeta",
                    1,
                    new[] { new SpellActionVariant(3), new SpellActionVariant(1) },
                    Array.Empty<Trait>(),
                    new[] { effect },
                    Array.Empty<SpellAttackDefinition>()
                ),
                new Game.Rules.Runtime.SpellDefinition(
                    alphaRankOne.Spell,
                    "Alpha",
                    1,
                    new[] { new SpellActionVariant(2), new SpellActionVariant(1) },
                    Array.Empty<Trait>(),
                    new[] { effect },
                    Array.Empty<SpellAttackDefinition>()
                ),
            }
        );
        ISpellBook book = new PreparedSpellBook(
            new[]
            {
                PreparedSpellEntry.Cantrip(zetaRankTwo),
                PreparedSpellEntry.Cantrip(alphaRankTwo),
                PreparedSpellEntry.Cantrip(zetaRankOne),
                PreparedSpellEntry.Cantrip(alphaRankOne),
            },
            Array.Empty<PreparedSpellSlotPool>(),
            7
        );
        TestSpellActionCatalog catalog = new(definitions, owner, book);

        UnitySpellActionInstaller.Install(controller, owner, catalog);

        Assert.That(
            controller
                .GetActions()
                .OfType<RulesCastSpellAction>()
                .Select(action =>
                    (action.Spell.Spell.Value, action.Spell.Rank, action.Variant.Actions)
                ),
            Is.EqualTo(
                new[]
                {
                    ("alpha", 1, 1),
                    ("alpha", 1, 2),
                    ("alpha", 2, 1),
                    ("alpha", 2, 2),
                    ("zeta", 1, 1),
                    ("zeta", 1, 3),
                    ("zeta", 2, 1),
                    ("zeta", 2, 3),
                }
            )
        );
    }

    [Test]
    public void PreparedSpellMissingCatalogDefinitionFailsInstallation()
    {
        CreatureComponent caster = CreateCreature("Missing Definition Caster", 0, prepared: false);
        TestActionController controller = caster.gameObject.AddComponent<TestActionController>();
        CreatureId owner = new("missing-definition-caster");
        SpellReference missing = Reference("missing-prepared-spell");
        ISpellBook book = new PreparedSpellBook(
            new[] { PreparedSpellEntry.Cantrip(missing) },
            Array.Empty<PreparedSpellSlotPool>(),
            7
        );
        TestSpellActionCatalog catalog = new(UnitySpellDefinitionCatalog.Load(), owner, book);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellActionInstaller.Install(controller, owner, catalog)
        );

        Assert.That(error.Message, Does.Contain(missing.ToString()));
        Assert.That(error.Message, Does.Contain("no catalog definition"));
    }

    [Test]
    public void SpellBookProviderRequiresMappingButAllowsMappedNoncaster()
    {
        CreatureId missing = new("missing-spellbook-creature");
        CreatureId nullMapped = new("null-spellbook-creature");
        CreatureId noncasterId = new("mapped-noncaster");
        CreatureComponent noncaster = CreateCreature("Mapped Noncaster", 0, prepared: false);
        Dictionary<CreatureId, CreatureComponent> creatures = new()
        {
            [nullMapped] = null,
            [noncasterId] = noncaster,
        };
        UnitySpellBookProvider provider = new(creatures);

        InvalidOperationException missingError = Assert.Throws<InvalidOperationException>(() =>
            provider.GetSpellBook(missing)
        );
        InvalidOperationException nullError = Assert.Throws<InvalidOperationException>(() =>
            provider.GetSpellBook(nullMapped)
        );

        Assert.That(missingError.Message, Does.Contain(missing.Value));
        Assert.That(nullError.Message, Does.Contain(nullMapped.Value));
        Assert.That(provider.GetSpellBook(noncasterId), Is.SameAs(EmptySpellBook.Instance));
    }

    [Test]
    public void InstalledSpellActionRequiresDefinitionButDetachedAvailabilityIsFalse()
    {
        CreatureComponent caster = CreateCreature("Detached Rules Caster", 0, prepared: false);
        TestActionController controller = caster.gameObject.AddComponent<TestActionController>();
        CreatureId owner = new("detached-rules-caster");
        SpellReference light = Reference("light");
        ISpellBook book = new PreparedSpellBook(
            new[] { PreparedSpellEntry.Cantrip(light) },
            Array.Empty<PreparedSpellSlotPool>(),
            7
        );
        TestSpellActionCatalog catalog = new(UnitySpellDefinitionCatalog.Load(), owner, book);
        UnitySpellActionInstaller.Install(controller, owner, catalog);
        RulesCastSpellAction action = LightActions(controller).Single();

        catalog.RemoveDefinitions();

        Assert.That(action.IsAvailable(controller), Is.False);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = action.ActionName;
        });
        Assert.That(error.Message, Does.Contain(light.ToString()));
        Assert.That(error.Message, Does.Contain("no longer has a catalog definition"));
    }

    [UnityTest]
    public IEnumerator PreparedRulesNativeSpellWithoutSupportedBehaviorFailsInstallation()
    {
        CreatureComponent caster = CreateCreature("Unsupported Native Caster", 0, prepared: true);
        TestActionController controller = caster.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(1);
        Occupy(tiles, caster.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(new[] { controller }, tiles);
        CreatureId owner = bridge.GetCreatureId(controller);
        SpellReference unsupported = Reference("unsupported-native");
        Game.Rules.Runtime.SpellDefinition definition = new(
            unsupported.Spell,
            "Unsupported Native",
            1,
            new[] { new SpellActionVariant(2) },
            Array.Empty<Trait>(),
            Array.Empty<SpellEffectDirective>(),
            Array.Empty<SpellAttackDefinition>()
        );
        ISpellBook book = new PreparedSpellBook(
            new[] { PreparedSpellEntry.Cantrip(unsupported) },
            Array.Empty<PreparedSpellSlotPool>(),
            7
        );
        UnsupportedSpellActionCatalog catalog = new(definition, owner, book);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            UnitySpellActionInstaller.Install(controller, owner, catalog)
        );

        Assert.That(error.Message, Does.Contain("no supported effect or attack"));
    }

    [UnityTest]
    public IEnumerator ResolvedAndInvalidLightCastsReleaseLockAndOnlyResolvedCreatesVisual()
    {
        CreatureComponent cleric = CreateCreature("Casting Cleric", 0, prepared: true);
        InstallCoroutineRunner();
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        CreatureComponent opponent = CreateCreature("Light Opponent", 1, prepared: false);
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, opponent.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            tiles,
            new ScriptedRollService(20, 10)
        );
        RulesCastSpellAction light = LightActions(controller).Single();
        CreatureId actor = bridge.GetCreatureId(controller);

        bridge.BeginTurn(actor, 3);
        bridge.SpendEncounterActions(actor, 2);
        controller.IsTakingAction = true;
        light.Invoke(cleric.gameObject);
        yield return null;

        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(VisualLights(cleric), Is.Empty);

        bridge.BeginTurn(actor, 3);
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
    public IEnumerator DivineLanceSelectionCancellationAndSuccessReleaseLockAndProjectOutcome()
    {
        InstallCoroutineRunner();
        SelectingGridApi grid = InstallGrid();
        CapturingCombatLog log = InstallCombatLog();
        CreatureComponent cleric = CreateCreature("Divine Lance Cleric", 0, prepared: true);
        cleric.ac = 10;
        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/KayKit/Prefabs/Animated/MageStaffAnimated.prefab"
        );
        GameObject visual = Object.Instantiate(visualPrefab, cleric.transform);
        CreatureAnimationController animation = visual.GetComponent<CreatureAnimationController>();
        CreaturePresentation presentation = cleric.gameObject.AddComponent<CreaturePresentation>();
        presentation.Bind(animation, visual.GetComponent<CreatureEquipmentVisuals>());
        CreatureComponent target = CreateCreature("Divine Lance Target", 1, prepared: false);
        target.ac = 10;
        TestActionController clericController =
            cleric.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, target.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { clericController, targetController },
            tiles,
            new ScriptedRollService(20, 10, 10, 2, 3, 1)
        );
        RulesCastSpellAction action = RulesActions(clericController, "divine-lance").Single();
        CreatureId actor = bridge.GetCreatureId(cleric);
        OnDamageDealt.AddListener(CountDamageEvent);
        OnAttackMiss.AddListener(CountMissEvent);

        bridge.BeginTurn(actor, 3);
        clericController.IsTakingAction = true;
        action.Invoke(cleric.gameObject);
        yield return null;

        Assert.That(clericController.IsTakingAction, Is.False);
        Assert.That(clericController.ActionPoints, Is.EqualTo(3));
        Assert.That(target.hp, Is.EqualTo(10));
        Assert.That(damageEventCount, Is.Zero);
        Assert.That(missEventCount, Is.Zero);
        Assert.That(animation.CurrentClipId, Is.Null);

        grid.Target = target.gameObject;
        clericController.IsTakingAction = true;
        action.Invoke(cleric.gameObject);
        for (int frame = 0; frame < 10 && clericController.IsTakingAction; frame++)
            yield return null;

        Assert.That(clericController.IsTakingAction, Is.False);
        Assert.That(clericController.ActionPoints, Is.EqualTo(1));
        Assert.That(target.hp, Is.EqualTo(5));
        Assert.That(damageEventCount, Is.EqualTo(1));
        Assert.That(missEventCount, Is.Zero);
        Assert.That(
            animation.CurrentClipId,
            Is.EqualTo("animation/combatranged/ranged_magic_shoot")
        );
        Assert.That(log.Messages.Any(message => message.Contains("casts Divine Lance")), Is.True);
        Assert.That(log.Entries, Has.Count.EqualTo(1));
        Assert.That(log.Entries.Single().Kind, Is.EqualTo(CombatLogEntryKind.Attack));
        Assert.That(log.Entries.Single().Action, Is.EqualTo("Divine Lance"));

        bridge.BeginTurn(actor, 3);
        clericController.IsTakingAction = true;
        action.Invoke(cleric.gameObject);
        for (int frame = 0; frame < 10 && clericController.IsTakingAction; frame++)
            yield return null;

        Assert.That(clericController.IsTakingAction, Is.False);
        Assert.That(clericController.ActionPoints, Is.EqualTo(1));
        Assert.That(target.hp, Is.EqualTo(5));
        Assert.That(damageEventCount, Is.EqualTo(1));
        Assert.That(missEventCount, Is.EqualTo(1));
        Assert.That(log.Entries, Has.Count.EqualTo(2));
        Assert.That(log.Entries.Last().Outcome, Is.EqualTo(CombatLogOutcome.CriticalFailure));
    }

    [UnityTest]
    public IEnumerator DivineLanceRejectsTargetThatBecomesStaleAfterSelection()
    {
        InstallCoroutineRunner();
        SelectingGridApi grid = InstallGrid();
        CreatureComponent cleric = CreateCreature("Stale Caster", 0, prepared: true);
        CreatureComponent target = CreateCreature("Stale Target", 1, prepared: false);
        target.ac = 10;
        TestActionController clericController =
            cleric.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(21);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, target.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { clericController, targetController },
            tiles,
            new ScriptedRollService(20, 10, 20)
        );
        RulesCastSpellAction action = RulesActions(clericController, "divine-lance").Single();
        grid.Target = target.gameObject;
        grid.AfterSelection = () => target.transform.position = new Vector3(20, 0, 0);
        bridge.BeginTurn(bridge.GetCreatureId(cleric), 3);
        clericController.IsTakingAction = true;

        action.Invoke(cleric.gameObject);
        for (int frame = 0; frame < 10 && clericController.IsTakingAction; frame++)
            yield return null;

        Assert.That(clericController.IsTakingAction, Is.False);
        Assert.That(clericController.ActionPoints, Is.EqualTo(3));
        Assert.That(target.hp, Is.EqualTo(10));
        Assert.That(clericController.StrikePenalty, Is.Zero);
    }

    [UnityTest]
    public IEnumerator DivineLanceRejectsSelectedCreatureMissingFromCombatRegistration()
    {
        InstallCoroutineRunner();
        SelectingGridApi grid = InstallGrid();
        CreatureComponent cleric = CreateCreature("Registered Caster", 0, prepared: true);
        CreatureComponent unregisteredTarget = CreateCreature(
            "Unregistered Grid Target",
            1,
            prepared: false
        );
        TestActionController clericController =
            cleric.gameObject.AddComponent<TestActionController>();
        CreatureComponent registeredOpponent = CreateCreature(
            "Registered Opponent",
            2,
            prepared: false
        );
        TestActionController opponentController =
            registeredOpponent.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(3);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, unregisteredTarget.gameObject);
        Occupy(tiles, registeredOpponent.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { clericController, opponentController },
            tiles,
            new ScriptedRollService(20, 10, 20)
        );
        RulesCastSpellAction action = RulesActions(clericController, "divine-lance").Single();
        CreatureId actor = bridge.GetCreatureId(cleric);
        grid.Target = unregisteredTarget.gameObject;
        bridge.BeginTurn(actor, 3);
        RulesSnapshot snapshotBeforeSelection = bridge.Snapshot;
        actionCompleteCount = 0;
        damageEventCount = 0;
        gameplayCommitCount = 0;
        OnActionComplete.AddListener(CountActionComplete);
        OnDamageDealt.AddListener(CountDamageEvent);
        OnGameplayStateCommitted.AddListener(CountGameplayCommit);
        clericController.IsTakingAction = true;
        LogAssert.Expect(
            LogType.Warning,
            "Cast a Spell was rejected: Selected target is not registered in the active combat encounter."
        );

        action.Invoke(cleric.gameObject);
        for (int frame = 0; frame < 10 && gameplayCommitCount == 0; frame++)
            yield return null;

        Assert.That(gameplayCommitCount, Is.EqualTo(1), "Coroutine wrapper did not complete.");
        Assert.That(actionCompleteCount, Is.EqualTo(1));
        Assert.That(clericController.IsTakingAction, Is.False);
        Assert.That(clericController.ActionPoints, Is.EqualTo(3));
        Assert.That(unregisteredTarget.hp, Is.EqualTo(10));
        Assert.That(damageEventCount, Is.Zero);
        Assert.That(clericController.StrikePenalty, Is.Zero);
        Assert.That(bridge.Snapshot, Is.SameAs(snapshotBeforeSelection));
        Assert.That(bridge.Snapshot.Version, Is.EqualTo(snapshotBeforeSelection.Version));
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
        RulesActions(controller, "light");

    private static List<RulesCastSpellAction> RulesActions(
        ActionController controller,
        string slug
    ) =>
        controller
            .GetActions()
            .OfType<RulesCastSpellAction>()
            .Where(action => action.Spell == Reference(slug))
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

    private SelectingGridApi InstallGrid()
    {
        if (GridAPI.TryGetInstance(out GridAPI active))
            Object.DestroyImmediate(active.gameObject);
        GameObject gameObject = new("Spellcasting Selecting Grid");
        created.Add(gameObject);
        return gameObject.AddComponent<SelectingGridApi>();
    }

    private CapturingCombatLog InstallCombatLog()
    {
        if (CombatLog.TryGetInstance(out CombatLogInterface active))
            Object.DestroyImmediate(active.gameObject);
        GameObject gameObject = new("Spellcasting Combat Log");
        created.Add(gameObject);
        CapturingCombatLog log = gameObject.AddComponent<CapturingCombatLog>();
        FieldInfo field = typeof(SingletonMonoBehaviour<CombatLogInterface>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        field.SetValue(null, log);
        return log;
    }

    private void CountGameplayCommit()
    {
        gameplayCommitCount++;
    }

    private void CountActionComplete()
    {
        actionCompleteCount++;
    }

    private void CountDamageEvent(string damageType) => damageEventCount++;

    private void CountMissEvent(GameObject attacker) => missEventCount++;

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class SelectingGridApi : GridAPI
    {
        public GameObject Target { get; set; }
        public System.Action AfterSelection { get; set; }

        public override IEnumerator SelectStridePath(
            GameObject character,
            StridePathSelectionRequest request,
            CoroutineResult<SelectionOutcome<MovementPath>> selection
        )
        {
            yield break;
        }

        public override IEnumerator GetStrikeTarget(
            GameObject attacker,
            StrikeTargetRequest request,
            CoroutineResult<StrikeTargetResult> target
        )
        {
            target.Value = Target == null ? null : new StrikeTargetResult { Target = Target };
            AfterSelection?.Invoke();
            yield break;
        }

        public override IEnumerator GetAreaTarget(
            AreaTargetSource source,
            AreaTargetRequest request,
            CoroutineResult<AreaTargetResult> target
        )
        {
            yield break;
        }

        public override bool DestroyToken(GameObject token) => false;
    }

    private sealed class CapturingCombatLog : CombatLogInterface
    {
        public List<string> Messages { get; } = new();
        public List<CombatLogEntry> Entries { get; } = new();

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

        public override List<string> GetMessages() => Messages;

        public override void LogEntry(CombatLogEntry entry)
        {
            Entries.Add(entry);
            base.LogEntry(entry);
        }
    }

    private sealed class TestSpellActionCatalog : ISpellActionCatalog
    {
        private readonly UnitySpellDefinitionCatalog definitions;
        private readonly CreatureId owner;
        private readonly ISpellBook book;
        private bool definitionsAvailable = true;

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
        )
        {
            if (definitionsAvailable)
                return definitions.TryGetSpell(reference, out definition);
            definition = null;
            return false;
        }

        public ISpellBook GetSpellBook(CreatureId creature) =>
            creature == owner ? book : EmptySpellBook.Instance;

        public void RemoveDefinitions() => definitionsAvailable = false;
    }

    private sealed class UnsupportedSpellActionCatalog : ISpellActionCatalog
    {
        private readonly Game.Rules.Runtime.SpellDefinition definition;
        private readonly CreatureId owner;
        private readonly ISpellBook book;

        public UnsupportedSpellActionCatalog(
            Game.Rules.Runtime.SpellDefinition definition,
            CreatureId owner,
            ISpellBook book
        )
        {
            this.definition = definition;
            this.owner = owner;
            this.book = book;
        }

        public ActionProfile GetBaseProfile(ActionDefinitionId definitionId) =>
            throw new KeyNotFoundException();

        public bool TryGetSpell(
            SpellReference reference,
            out Game.Rules.Runtime.SpellDefinition value
        )
        {
            if (reference.Spell == definition.Id && reference.Rank == definition.MinimumRank)
            {
                value = definition;
                return true;
            }
            value = null;
            return false;
        }

        public ISpellBook GetSpellBook(CreatureId creature) =>
            creature == owner ? book : EmptySpellBook.Instance;
    }
}
