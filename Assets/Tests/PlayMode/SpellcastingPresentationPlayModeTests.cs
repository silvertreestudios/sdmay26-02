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
using Game.Strikes;
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
    public void PreStartInitializationDoesNotInstallAnySpellAuthority()
    {
        CreatureComponent cleric = CreateCreature("Pre-Start Cleric", 0, prepared: false);
        cleric.level = 1;
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        cleric.InitializeRuntimeActions();

        Assert.That(cleric.Prepared.Spellcasting, Is.Null);
        RulesCastSpellAction[] light = controller
            .GetActions()
            .OfType<RulesCastSpellAction>()
            .Where(action => action.Spell == Reference("light"))
            .ToArray();
        Assert.That(light, Is.Empty);
        Assert.That(RulesActions(controller, "divine-lance"), Is.Empty);
    }

    [UnityTest]
    public IEnumerator CheckedInClericEnrollsCompleteRulesAndMaceStrike()
    {
        GameObject maren = CreatureJsonConverter.CreateByName("Maren");
        Assert.That(maren, Is.Not.Null);
        created.Add(maren);
        maren.transform.position = Vector3.zero;
        CreatureComponent cleric = maren.GetComponent<CreatureComponent>();
        maren.AddComponent<Team>().Name = "players";
        TestActionController controller = maren.AddComponent<TestActionController>();
        CreatureComponent opponent = CreateCreature("Maren Spell Opponent", 1, prepared: false);
        opponent.gameObject.AddComponent<Team>().Name = "enemies";
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, maren);
        Occupy(tiles, opponent.gameObject);

        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            tiles,
            "players"
        );
        bridge.AdvanceEncounter();
        CreatureId actor = bridge.GetCreatureId(cleric);
        CreatureStatisticsState statistics = bridge.Snapshot.Statistics[actor];
        RulesStrikeAction mace = controller
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(action => action.ActionName == "Mace");

        Assert.That(cleric.Build.ClassName, Is.EqualTo("Cleric"));
        Assert.That(cleric.Prepared.HasOwnedItem("cleric"), Is.True);
        Assert.That(statistics.WillModifier, Is.EqualTo(9));
        Assert.That(statistics.GetSkillModifier(Skill.Religion), Is.EqualTo(7));
        Assert.That(mace.Item.Category, Is.EqualTo("simple"));
        Assert.That(mace.Item.Group, Is.EqualTo("club"));
        Assert.That(mace.Item.AttackModifier, Is.EqualTo(4));
        Assert.That(mace.Item.DamageDice.Single().Dice, Is.EqualTo(new DiceExpression(1, 6)));
        Assert.That(mace.Item.DamageDice.Single().DamageType, Is.EqualTo("bludgeoning"));
        Assert.That(mace.Item.Traits, Does.Contain(Trait.FromSlug("shove")));
        AssertMigratedClericSpellActions(controller);
        AssertNoDuplicateSpellActions(controller.GetActions().OfType<RulesCastSpellAction>());
        bridge.ReleaseOwnership();
    }

    [UnityTest]
    public IEnumerator CheckedInMarenCastsAllSixMigratedSpellsThroughProductionActions()
    {
        InstallCoroutineRunner();
        SelectingGridApi grid = InstallGrid();
        GameObject maren = CreatureJsonConverter.CreateByName("Maren");
        Assert.That(maren, Is.Not.Null);
        created.Add(maren);
        maren.transform.position = Vector3.zero;
        CreatureComponent cleric = maren.GetComponent<CreatureComponent>();
        maren.AddComponent<Team>().Name = "players";
        TestActionController controller = maren.AddComponent<TestActionController>();
        CreatureComponent opponent = CreateCreature("Maren Spell Opponent", 1, prepared: false, 20);
        opponent.gameObject.AddComponent<Team>().Name = "enemies";
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, maren);
        Occupy(tiles, opponent.gameObject);

        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            tiles,
            new ScriptedRollService(20, 10, 10, 4, 5),
            "players"
        );
        CreatureId actor = bridge.GetCreatureId(cleric);

        yield return InvokeSpell(
            bridge,
            actor,
            controller,
            RulesActions(controller, "shield").Single(),
            maren
        );
        Assert.That(
            bridge.Snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == SpellFeatureRules.ShieldEffect
            ),
            Is.True
        );

        grid.Target = maren;
        yield return InvokeSpell(
            bridge,
            actor,
            controller,
            RulesActions(controller, "guidance").Single(),
            maren
        );
        Assert.That(
            bridge.Snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == SpellFeatureRules.GuidanceEffect
            ),
            Is.True
        );

        grid.AreaResult = AreaTargeting.Evaluate(
            maren,
            tiles,
            new AreaTargetRequest
            {
                Shape = AreaShape.Cone,
                SizeFeet = 15,
                RequiresLineOfEffect = true,
            },
            new AreaPlacement
            {
                Shape = AreaShape.Cone,
                OriginCell = Vector3Int.zero,
                Direction = AreaDirection.East,
            }
        );
        int opponentHealth = opponent.Health.Current;
        yield return InvokeSpell(
            bridge,
            actor,
            controller,
            RulesActions(controller, "haunting-hymn").Single(),
            maren
        );
        Assert.That(opponent.Health.Current, Is.LessThan(opponentHealth));

        grid.AreaResult = AreaTargeting.Evaluate(
            maren,
            tiles,
            new AreaTargetRequest
            {
                Shape = AreaShape.Emanation,
                SizeFeet = 15,
                IncludeCenter = true,
                RequiresLineOfEffect = true,
            },
            new AreaPlacement
            {
                Shape = AreaShape.Emanation,
                OriginCell = Vector3Int.zero,
                Direction = AreaDirection.East,
            }
        );
        yield return InvokeSpell(
            bridge,
            actor,
            controller,
            RulesActions(controller, "bless").Single(),
            maren
        );
        Assert.That(
            bridge.Snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == SpellFeatureRules.BlessEffect
            ),
            Is.True
        );

        grid.Target = maren;
        yield return InvokeSpell(
            bridge,
            actor,
            controller,
            RulesActions(controller, "infuse-vitality")
                .Single(action => action.Variant.Actions == 1),
            maren
        );
        Assert.That(
            bridge.Snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == SpellFeatureRules.InfuseVitalityEffect
            ),
            Is.True
        );

        cleric.ApplyFinalDamage(2, RuleSource.FromSlug("maren-production-casting-test"));
        int damagedHealth = cleric.Health.Current;
        yield return InvokeSpell(
            bridge,
            actor,
            controller,
            RulesActions(controller, "heal").Single(action => action.Variant.Actions == 1),
            maren
        );
        Assert.That(cleric.Health.Current, Is.GreaterThan(damagedHealth));
        Assert.That(
            bridge
                .Snapshot
                .SpellSlots[new SpellSlotPoolId($"{actor.Value}:rank-1-bless")]
                .Remaining,
            Is.Zero
        );
        Assert.That(
            bridge
                .Snapshot
                .SpellSlots[new SpellSlotPoolId($"{actor.Value}:rank-1-infuse-vitality")]
                .Remaining,
            Is.Zero
        );
        Assert.That(FontUses(bridge, actor), Is.EqualTo(3));
        bridge.ReleaseOwnership();
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
            tiles,
            "players"
        );
        bridge.AdvanceEncounter();

        Assert.That(LightActions(initialController), Has.Count.EqualTo(1));
        Assert.That(RulesActions(initialController, "divine-lance"), Has.Count.EqualTo(1));
        AssertMigratedClericSpellActions(initialController);
        Assert.That(LightActions(noncasterController), Is.Empty);
        Assert.That(RulesActions(noncasterController, "divine-lance"), Is.Empty);

        CreatureComponent reinforcement = CreateCreature("Reinforcement", 2, prepared: true);
        TestActionController reinforcementController =
            reinforcement.gameObject.AddComponent<TestActionController>();
        yield return null;
        Occupy(tiles, reinforcement.gameObject);
        bridge.AddCombatants(new[] { reinforcementController });
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
        AssertMigratedClericSpellActions(reinforcementController);
    }

    [UnityTest]
    public IEnumerator ConsecutiveEncountersRebindSpellActionsAndPreserveResourcesWithoutDuplicates()
    {
        InstallCoroutineRunner();
        SelectingGridApi grid = InstallGrid();
        CreatureComponent cleric = CreateCreature("Consecutive Cleric", 0, prepared: true);
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        CreatureComponent opponent = CreateCreature("Consecutive Opponent", 1, prepared: false);
        opponent.gameObject.AddComponent<Team>().Name = "enemies";
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, opponent.gameObject);

        UnityCombatRulesBridge first = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            tiles,
            new ScriptedRollService(20, 10, 4),
            "players"
        );
        CreatureId firstActor = first.GetCreatureId(cleric);
        RulesCastSpellAction[] firstActions = controller
            .GetActions()
            .OfType<RulesCastSpellAction>()
            .ToArray();
        RulesCastSpellAction firstHeal = firstActions.Single(action =>
            action.Spell == Reference("heal") && action.Variant.Actions == 1
        );
        first.BeginTurn(firstActor, 3);
        Assert.That(firstHeal.IsAvailable(controller), Is.True);
        AssertMigratedClericSpellActions(controller);
        Assert.That(LightActions(controller), Has.Count.EqualTo(1));
        Assert.That(RulesActions(controller, "divine-lance"), Has.Count.EqualTo(1));
        AssertNoDuplicateSpellActions(firstActions);
        Assert.That(FontUses(first, firstActor), Is.EqualTo(4));

        grid.Target = cleric.gameObject;
        controller.IsTakingAction = true;
        firstHeal.Invoke(cleric.gameObject);
        for (int frame = 0; frame < 10 && controller.IsTakingAction; frame++)
            yield return null;

        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(FontUses(first, firstActor), Is.EqualTo(3));
        first.ReleaseOwnership();
        Assert.That(firstHeal.IsAvailable(controller), Is.False);

        UnityCombatRulesBridge second = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            tiles,
            new ScriptedRollService(20, 10, 5),
            "players"
        );
        CreatureId secondActor = second.GetCreatureId(cleric);
        RulesCastSpellAction[] secondActions = controller
            .GetActions()
            .OfType<RulesCastSpellAction>()
            .ToArray();
        RulesCastSpellAction secondHeal = secondActions.Single(action =>
            action.Spell == Reference("heal") && action.Variant.Actions == 1
        );
        second.BeginTurn(secondActor, 3);

        Assert.That(secondHeal, Is.Not.SameAs(firstHeal));
        Assert.That(secondActions.Intersect(firstActions), Is.Empty);
        Assert.DoesNotThrow(() => secondHeal.IsAvailable(controller));
        Assert.That(secondHeal.IsAvailable(controller), Is.True);
        AssertMigratedClericSpellActions(controller);
        Assert.That(LightActions(controller), Has.Count.EqualTo(1));
        Assert.That(RulesActions(controller, "divine-lance"), Has.Count.EqualTo(1));
        AssertNoDuplicateSpellActions(secondActions);
        Assert.That(FontUses(second, secondActor), Is.EqualTo(3));

        controller.IsTakingAction = true;
        secondHeal.Invoke(cleric.gameObject);
        for (int frame = 0; frame < 10 && controller.IsTakingAction; frame++)
            yield return null;

        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(FontUses(second, secondActor), Is.EqualTo(2));
        second.ReleaseOwnership();
    }

    [UnityTest]
    public IEnumerator ProductionShieldActionCastsThroughRulesAndCompletesPresentation()
    {
        InstallCoroutineRunner();
        CreatureComponent cleric = CreateCreature("Shield Cleric", 0, prepared: true);
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        CreatureComponent opponent = CreateCreature("Shield Opponent", 1, prepared: false);
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, opponent.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            tiles,
            "players"
        );
        CreatureId actor = bridge.GetCreatureId(cleric);
        RulesCastSpellAction shield = RulesActions(controller, "shield").Single();
        actionCompleteCount = 0;
        OnActionComplete.AddListener(CountActionComplete);

        bridge.BeginTurn(actor, 3);
        controller.IsTakingAction = true;
        shield.Invoke(cleric.gameObject);
        for (int frame = 0; frame < 10 && actionCompleteCount == 0; frame++)
            yield return null;

        Assert.That(actionCompleteCount, Is.EqualTo(1));
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        ActiveEffectInstance effect = bridge
            .Snapshot.ActiveEffects.Select(pair => pair.Value)
            .Single(value => value.DefinitionId == SpellFeatureRules.ShieldEffect);
        Assert.That(effect.SourceCreature, Is.EqualTo(actor));
        Assert.That(effect.GetState<SpellEffectState>().Target, Is.EqualTo(actor));
        Assert.That(effect.GetState<SpellEffectState>().Spell, Is.EqualTo(Reference("shield")));
    }

    [UnityTest]
    public IEnumerator SingleCreatureSpellSelectionCanTargetTheCaster()
    {
        InstallCoroutineRunner();
        SelectingGridApi grid = InstallGrid();
        CreatureComponent cleric = CreateCreature("Self Guidance Cleric", 0, prepared: true);
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        CreatureComponent opponent = CreateCreature("Self Guidance Opponent", 1, prepared: false);
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, opponent.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { controller, opponentController },
            tiles,
            "players"
        );
        CreatureId actor = bridge.GetCreatureId(cleric);
        RulesCastSpellAction guidance = RulesActions(controller, "guidance").Single();
        grid.Target = cleric.gameObject;
        bridge.BeginTurn(actor, 3);
        controller.IsTakingAction = true;

        guidance.Invoke(cleric.gameObject);
        for (int frame = 0; frame < 10 && controller.IsTakingAction; frame++)
            yield return null;

        Assert.That(grid.LastStrikeRequest, Is.Not.Null);
        Assert.That(grid.LastStrikeRequest.IncludeSelf, Is.True);
        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        ActiveEffectInstance effect = bridge
            .Snapshot.ActiveEffects.Select(pair => pair.Value)
            .Single(value => value.DefinitionId == SpellFeatureRules.GuidanceEffect);
        Assert.That(effect.GetState<SpellEffectState>().Target, Is.EqualTo(actor));
    }

    [UnityTest]
    public IEnumerator MultiTargetInfuseVitalityRepromptsDuplicatesAndPreservesCancellation()
    {
        InstallCoroutineRunner();
        SelectingGridApi grid = InstallGrid();
        CreatureComponent cleric = CreateCreature("Infuse Vitality Cleric", 0, prepared: true);
        CreatureComponent firstAlly = CreateCreature(
            "Infuse Vitality First Ally",
            1,
            prepared: false
        );
        firstAlly.gameObject.AddComponent<Team>().Name = "players";
        CreatureComponent secondAlly = CreateCreature(
            "Infuse Vitality Second Ally",
            2,
            prepared: false
        );
        secondAlly.gameObject.AddComponent<Team>().Name = "players";
        CreatureComponent opponent = CreateCreature("Infuse Vitality Opponent", 3, prepared: false);
        opponent.gameObject.AddComponent<Team>().Name = "enemies";
        TestActionController clericController =
            cleric.gameObject.AddComponent<TestActionController>();
        TestActionController firstController =
            firstAlly.gameObject.AddComponent<TestActionController>();
        TestActionController secondController =
            secondAlly.gameObject.AddComponent<TestActionController>();
        TestActionController opponentController =
            opponent.gameObject.AddComponent<TestActionController>();
        yield return null;
        Tile[,] tiles = CreateTiles(4);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, firstAlly.gameObject);
        Occupy(tiles, secondAlly.gameObject);
        Occupy(tiles, opponent.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[]
            {
                clericController,
                firstController,
                secondController,
                opponentController,
            },
            tiles,
            "players"
        );
        CreatureId actor = bridge.GetCreatureId(cleric);
        CreatureId firstTarget = bridge.GetCreatureId(firstAlly);
        CreatureId secondTarget = bridge.GetCreatureId(secondAlly);
        RulesCastSpellAction action = RulesActions(clericController, "infuse-vitality")
            .Single(candidate => candidate.Variant.Actions == 2);
        SpellSlotPoolId pool = new($"{actor.Value}:rank-1-infuse-vitality");

        grid.QueueTargets(firstAlly.gameObject, firstAlly.gameObject, null);
        bridge.BeginTurn(actor, 3);
        yield return InvokeSpell(bridge, actor, clericController, action, cleric.gameObject);

        Assert.That(grid.StrikeSelectionCount, Is.EqualTo(3));
        Assert.That(clericController.ActionPoints, Is.EqualTo(3));
        Assert.That(bridge.Snapshot.SpellSlots[pool].Remaining, Is.EqualTo(1));
        Assert.That(
            bridge.Snapshot.ActiveEffects.Any(pair =>
                pair.Value.DefinitionId == SpellFeatureRules.InfuseVitalityEffect
            ),
            Is.False
        );

        grid.QueueTargets(firstAlly.gameObject, firstAlly.gameObject, secondAlly.gameObject);
        bridge.BeginTurn(actor, 3);
        yield return InvokeSpell(bridge, actor, clericController, action, cleric.gameObject);

        Assert.That(grid.StrikeSelectionCount, Is.EqualTo(6));
        Assert.That(clericController.ActionPoints, Is.EqualTo(1));
        Assert.That(bridge.Snapshot.SpellSlots[pool].Remaining, Is.Zero);
        Assert.That(
            bridge
                .Snapshot.ActiveEffects.Select(pair => pair.Value)
                .Where(effect => effect.DefinitionId == SpellFeatureRules.InfuseVitalityEffect)
                .Select(effect => effect.GetState<SpellEffectState>().Target),
            Is.EquivalentTo(new[] { firstTarget, secondTarget })
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
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { controller },
            tiles,
            "players"
        );
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
            new ScriptedRollService(20, 10),
            "players"
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
            new ScriptedRollService(20, 10, 10, 2, 3, 1),
            "players"
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
        yield return null;

        Assert.That(target.hp, Is.EqualTo(5), "Rules health must commit synchronously.");
        Assert.That(target.Health.Current, Is.EqualTo(5));
        Assert.That(clericController.IsTakingAction, Is.True);
        Assert.That(animation.IsActionPlaying, Is.True);

        yield return new WaitForSeconds(5.1f);
        yield return null;

        Assert.That(clericController.IsTakingAction, Is.False);
        Assert.That(clericController.ActionPoints, Is.EqualTo(1));
        Assert.That(target.hp, Is.EqualTo(5));
        Assert.That(damageEventCount, Is.EqualTo(1));
        Assert.That(missEventCount, Is.Zero);
        Assert.That(animation.CurrentClipId, Is.Null);
        Assert.That(log.Messages.Any(message => message.Contains("casts Divine Lance")), Is.True);
        Assert.That(log.Entries, Has.Count.EqualTo(1));
        Assert.That(log.Entries.Single().Kind, Is.EqualTo(CombatLogEntryKind.Attack));
        Assert.That(log.Entries.Single().Action, Is.EqualTo("Divine Lance"));

        bridge.BeginTurn(actor, 3);
        clericController.IsTakingAction = true;
        action.Invoke(cleric.gameObject);
        yield return new WaitForSeconds(5.1f);
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
    public IEnumerator LethalDivineLanceOrdersDefeatAfterAttackBeginningAndResult()
    {
        InstallCoroutineRunner();
        SelectingGridApi grid = InstallGrid();
        CreatureComponent cleric = CreateCreature("Lethal Divine Lance Cleric", 0, prepared: true);
        cleric.ac = 10;
        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/KayKit/Prefabs/Animated/MageStaffAnimated.prefab"
        );
        GameObject actorVisual = Object.Instantiate(visualPrefab, cleric.transform);
        CreatureAnimationController actorAnimation =
            actorVisual.GetComponent<CreatureAnimationController>();
        cleric
            .gameObject.AddComponent<CreaturePresentation>()
            .Bind(actorAnimation, actorVisual.GetComponent<CreatureEquipmentVisuals>());
        CreatureComponent target = CreateCreature(
            "Lethal Divine Lance Target",
            1,
            prepared: false,
            hitPoints: 1
        );
        target.ac = 10;
        GameObject targetVisual = Object.Instantiate(visualPrefab, target.transform);
        CreatureAnimationController targetAnimation =
            targetVisual.GetComponent<CreatureAnimationController>();
        target
            .gameObject.AddComponent<CreaturePresentation>()
            .Bind(targetAnimation, targetVisual.GetComponent<CreatureEquipmentVisuals>());
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
            new ScriptedRollService(20, 10, 10, 1, 1),
            "players"
        );
        RulesCastSpellAction action = RulesActions(clericController, "divine-lance").Single();
        grid.Target = target.gameObject;
        bridge.BeginTurn(bridge.GetCreatureId(cleric), 3);
        List<string> presentationOrder = new();
        void CaptureResult(string _) => presentationOrder.Add("result");
        OnDamageDealt.AddListener(CaptureResult);
        clericController.IsTakingAction = true;

        action.Invoke(cleric.gameObject);
        yield return null;

        Assert.That(target.Health.Current, Is.Zero, "Rules health must commit immediately.");
        Assert.That(actorAnimation.IsActionPlaying, Is.True);
        Assert.That(
            targetAnimation.IsDeathPlaying,
            Is.False,
            "Defeat presentation must wait behind the spell's attack-begin step."
        );
        presentationOrder.Add("begin");
        bool sawDeathPresentation = false;
        float deadline = Time.realtimeSinceStartup + 10.5f;
        while (clericController.IsTakingAction && Time.realtimeSinceStartup < deadline)
        {
            if (!sawDeathPresentation && targetAnimation.IsDeathPlaying)
            {
                sawDeathPresentation = true;
                presentationOrder.Add("defeat");
            }
            yield return null;
        }

        Assert.That(clericController.IsTakingAction, Is.False);
        Assert.That(sawDeathPresentation, Is.True);
        Assert.That(
            presentationOrder,
            Is.EqualTo(new[] { "begin", "result", "defeat" }),
            "Lethal spell defeat must follow the action's queued beginning and result."
        );
        OnDamageDealt.RemoveListener(CaptureResult);
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
            new ScriptedRollService(20, 10, 20),
            "players"
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
            new ScriptedRollService(20, 10, 20),
            "players"
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
    public IEnumerator GenericRemovalReasonsAndDisposeAreIdempotentAndIsolated()
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
            new OpId(1),
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
            new OpId(1),
            snapshot
        );
        Assert.That(VisualLights(owner), Has.Count.EqualTo(1));

        observer.OnFactCommitted(
            new ActiveEffectRemovedFact(
                effect,
                new ActiveRuleBinding(
                    new BindingId("binding-light"),
                    effect.DefinitionId,
                    ownerId,
                    effect.Id,
                    effect.Source,
                    1
                ),
                ActiveEffectRemovalReason.Expired
            ),
            new OpId(1),
            snapshot
        );
        observer.OnFactCommitted(
            new ActiveEffectRemovedFact(
                effect,
                new ActiveRuleBinding(
                    new BindingId("binding-light"),
                    effect.DefinitionId,
                    ownerId,
                    effect.Id,
                    effect.Source,
                    1
                ),
                ActiveEffectRemovalReason.Ended
            ),
            new OpId(1),
            snapshot
        );
        yield return null;
        Assert.That(VisualLights(owner), Is.Empty);

        observer.OnFactCommitted(
            new ActiveEffectCreatedFact(effect, new BindingId("binding-light")),
            new OpId(1),
            snapshot
        );
        observer.Dispose();
        observer.Dispose();
        yield return null;
        Assert.That(VisualLights(owner), Is.Empty);
    }

    private CreatureComponent CreateCreature(string name, int x, bool prepared, int hitPoints = 10)
    {
        GameObject value = new(name);
        created.Add(value);
        value.transform.position = new Vector3(x, 0, 0);
        CreatureComponent creature = value.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(hitPoints, hitPoints);
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

    private static IEnumerator InvokeSpell(
        UnityCombatRulesBridge bridge,
        CreatureId actor,
        ActionController controller,
        RulesCastSpellAction action,
        GameObject caster
    )
    {
        bridge.BeginTurn(actor, 3);
        controller.IsTakingAction = true;
        action.Invoke(caster);
        for (int frame = 0; frame < 10 && controller.IsTakingAction; frame++)
            yield return null;
        Assert.That(controller.IsTakingAction, Is.False, action.ActionName);
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

    private static void AssertMigratedClericSpellActions(ActionController controller)
    {
        Assert.That(RulesActions(controller, "shield"), Has.Count.EqualTo(1));
        Assert.That(RulesActions(controller, "guidance"), Has.Count.EqualTo(1));
        Assert.That(RulesActions(controller, "haunting-hymn"), Has.Count.EqualTo(1));
        Assert.That(RulesActions(controller, "bless"), Has.Count.EqualTo(1));
        Assert.That(RulesActions(controller, "infuse-vitality"), Has.Count.EqualTo(3));
        Assert.That(RulesActions(controller, "heal"), Has.Count.EqualTo(3));
    }

    private static void AssertNoDuplicateSpellActions(IEnumerable<RulesCastSpellAction> actions) =>
        Assert.That(
            actions
                .GroupBy(action => (action.Spell, action.Variant))
                .All(group => group.Count() == 1),
            Is.True
        );

    private static int FontUses(UnityCombatRulesBridge bridge, CreatureId actor) =>
        bridge.Snapshot.SpellSlots[new SpellSlotPoolId($"{actor.Value}:font-heal")].Remaining;

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class SelectingGridApi : GridAPI
    {
        private readonly Queue<GameObject> targets = new();

        public GameObject Target { get; set; }
        public AreaTargetResult AreaResult { get; set; }
        public System.Action AfterSelection { get; set; }
        public StrikeTargetRequest LastStrikeRequest { get; private set; }
        public int StrikeSelectionCount { get; private set; }

        public void QueueTargets(params GameObject[] values)
        {
            foreach (GameObject value in values)
                targets.Enqueue(value);
        }

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
            LastStrikeRequest = request;
            StrikeSelectionCount++;
            GameObject selected = targets.Count > 0 ? targets.Dequeue() : Target;
            target.Value = selected == null ? null : new StrikeTargetResult { Target = selected };
            AfterSelection?.Invoke();
            yield break;
        }

        public override IEnumerator GetAreaTarget(
            AreaTargetSource source,
            AreaTargetRequest request,
            CoroutineResult<AreaTargetResult> target
        )
        {
            target.Value = AreaResult;
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
        private readonly IReadOnlyDictionary<SpellId, ISpellCastRule> rules;
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
            rules = SpellFeatureRules.CreateCatalog(new PresentationSpellCreatureData());
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

        public bool TryGetCastRule(SpellId spell, out ISpellCastRule rule) =>
            rules.TryGetValue(spell, out rule);

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

        public bool TryGetCastRule(SpellId spell, out ISpellCastRule rule)
        {
            rule = null;
            return false;
        }
    }

    private sealed class PresentationSpellCreatureData : ISpellCreatureDataProvider
    {
        public bool IsUndead(CreatureId creature) => false;

        public IReadOnlyList<TypedDefenseAdjustment> GetWeaknesses(CreatureId creature) =>
            Array.Empty<TypedDefenseAdjustment>();

        public IReadOnlyList<TypedDefenseAdjustment> GetResistances(CreatureId creature) =>
            Array.Empty<TypedDefenseAdjustment>();
    }
}
