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
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class LightRulesPlayModeTests
{
    private readonly List<GameObject> created = new();
    private int gameplayCommitCount;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        OnGameplayStateCommitted.RemoveListener(CountGameplayCommit);
        foreach (GameObject gameObject in created)
        {
            if (gameObject != null)
                Object.Destroy(gameObject);
        }
        created.Clear();
        ResetCombatLog();
        Pf2eItemCatalog.ResetForTests();
        yield return null;
    }

    [UnityTest]
    public IEnumerator PreparedInitialAndReinforcementCombatantsReceiveOneLightWithLegacySpell()
    {
        CreatureComponent prepared = CreatePreparedCleric("Initial Cleric", 0);
        TestActionController preparedController =
            prepared.gameObject.AddComponent<TestActionController>();
        CreatureComponent unprepared = CreateCreature("Unprepared Creature", 1);
        TestActionController unpreparedController =
            unprepared.gameObject.AddComponent<TestActionController>();
        yield return null;

        Tile[,] tiles = CreateTiles(3);
        Occupy(tiles, prepared.gameObject);
        Occupy(tiles, unprepared.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { preparedController, unpreparedController },
            tiles
        );
        UnityLightActionInstaller.Install(preparedController);
        UnityLightActionInstaller.Install(preparedController);
        UnityLightActionInstaller.Install(unpreparedController);

        EntityAction installedLight = preparedController
            .GetActions()
            .Single(action => action.ActionName == "Light");
        Assert.That(installedLight, Is.TypeOf<RulesLightAction>());
        Assert.That(
            preparedController
                .GetActions()
                .OfType<CastSpellAction>()
                .Any(action => action.Spell.Slug == "light"),
            Is.False
        );
        Assert.That(
            preparedController.GetActions().Any(action => action.ActionName == "Shield"),
            Is.True
        );
        Assert.That(
            unpreparedController.GetActions().Any(action => action.ActionName == "Light"),
            Is.False
        );

        CreatureComponent reinforcement = CreatePreparedCleric("Reinforcement Cleric", 2);
        TestActionController reinforcementController =
            reinforcement.gameObject.AddComponent<TestActionController>();
        yield return null;
        Occupy(tiles, reinforcement.gameObject);

        bridge.RegisterCombatants(new[] { reinforcementController });
        UnityLightActionInstaller.Install(reinforcementController);
        UnityLightActionInstaller.Install(reinforcementController);

        Assert.That(
            reinforcementController.GetActions().Count(action => action.ActionName == "Light"),
            Is.EqualTo(1)
        );
        Assert.That(
            reinforcementController.GetActions().Any(action => action.ActionName == "Shield"),
            Is.True
        );
    }

    [UnityTest]
    public IEnumerator CommittedLightSpendsTwoPresentsOnceNotifiesAndReleasesLock()
    {
        RecordingCombatLog log = InstallCombatLog();
        CreatureComponent cleric = CreatePreparedCleric("Committed Light Cleric", 0);
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        CreatureAnimationController animation = BindAnimatedPresentation(cleric.gameObject);
        yield return null;

        Tile[,] tiles = CreateTiles(1);
        Occupy(tiles, cleric.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(new[] { controller }, tiles);
        controller.StartTurn();
        gameplayCommitCount = 0;
        OnGameplayStateCommitted.AddListener(CountGameplayCommit);
        RulesLightAction light = controller.GetActions().OfType<RulesLightAction>().Single();

        controller.TakeAction(light);

        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(gameplayCommitCount, Is.EqualTo(1));
        Assert.That(log.Messages.Count(message => message.Contains("casts Light")), Is.EqualTo(1));
        Assert.That(
            animation.CurrentClipId,
            Is.EqualTo("animation/combatranged/ranged_magic_shoot")
        );
        Assert.That(
            bridge.Snapshot.ActionEconomy[bridge.GetCreatureId(controller)].ActionsRemaining,
            Is.EqualTo(1)
        );
    }

    [UnityTest]
    public IEnumerator RejectedLightPresentsNothingAndStillReleasesLock()
    {
        RecordingCombatLog log = InstallCombatLog();
        CreatureComponent cleric = CreatePreparedCleric("Rejected Light Cleric", 0);
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        CreatureAnimationController animation = BindAnimatedPresentation(cleric.gameObject);
        yield return null;

        Tile[,] tiles = CreateTiles(1);
        Occupy(tiles, cleric.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(new[] { controller }, tiles);
        CreatureId actor = bridge.GetCreatureId(controller);
        bridge.BeginTurn(actor, 1);
        gameplayCommitCount = 0;
        OnGameplayStateCommitted.AddListener(CountGameplayCommit);
        RulesLightAction light = controller.GetActions().OfType<RulesLightAction>().Single();
        controller.IsTakingAction = true;

        light.Invoke(cleric.gameObject);

        Assert.That(controller.ActionPoints, Is.EqualTo(1));
        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(gameplayCommitCount, Is.Zero);
        Assert.That(log.Messages, Is.Empty);
        Assert.That(animation.CurrentClipId, Is.Null);
    }

    [UnityTest]
    public IEnumerator DistinctFeatureCompositionsRegisterOneResolvedObserver()
    {
        RecordingCombatLog log = InstallCombatLog();
        CreatureComponent cleric = CreatePreparedCleric("Composed Light Cleric", 0);
        CreatureAnimationController animation = BindAnimatedPresentation(cleric.gameObject);
        yield return null;

        CreatureId actor = new CreatureId("composed-light-actor");
        Dictionary<CreatureId, CreatureComponent> creatures = new() { [actor] = cleric };
        LightActionDefinition definition = UnityLightDefinitionLoader.Load(
            new UnityLightActorStateProvider(creatures)
        );
        RuleDispatcher dispatcher = new RuleDispatcherBuilder(
            new InMemoryRulesStore(
                new RulesStateSeed()
                    .SeedCreature(new CreatureState(actor, new PlayerId("composed-light-player")))
                    .SeedActionEconomy(actor, new ActionEconomyState(3, true))
            )
        )
            .UseActionLifecycle(definition)
            .UseLightRules(definition)
            .Build();
        UnityLightFeatureComposition firstComposition = new(dispatcher, creatures);
        UnityLightFeatureComposition secondComposition = new(dispatcher, creatures);
        firstComposition.RegisterPresentation();
        firstComposition.RegisterPresentation();
        secondComposition.RegisterPresentation();
        gameplayCommitCount = 0;
        OnGameplayStateCommitted.AddListener(CountGameplayCommit);
        int initialPlaybackVersion = GetAnimationPlaybackVersion(animation);

        OpResult<LightCastOutcome> result = dispatcher
            .Dispatch(new LightActionOp(actor))
            .GetAwaiter()
            .GetResult();

        Assert.That(result, Is.TypeOf<ResolvedOpResult<LightCastOutcome>>());
        Assert.That(gameplayCommitCount, Is.EqualTo(1));
        Assert.That(log.Messages.Count(message => message.Contains("casts Light")), Is.EqualTo(1));
        Assert.That(GetAnimationPlaybackVersion(animation), Is.EqualTo(initialPlaybackVersion + 1));
    }

    [Test]
    public void MissingRulesAuthorityPresentsNothingAndReleasesLock()
    {
        RecordingCombatLog log = InstallCombatLog();
        CreatureComponent cleric = CreatePreparedCleric("Authorityless Light Cleric", 0);
        TestActionController controller = cleric.gameObject.AddComponent<TestActionController>();
        RulesLightAction light = new RulesLightAction();
        gameplayCommitCount = 0;
        OnGameplayStateCommitted.AddListener(CountGameplayCommit);
        controller.IsTakingAction = true;

        light.Invoke(cleric.gameObject);

        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(gameplayCommitCount, Is.Zero);
        Assert.That(log.Messages, Is.Empty);
    }

    private CreatureComponent CreatePreparedCleric(string name, int x)
    {
        CreatureComponent creature = CreateCreature(name, x);
        creature.level = 1;
        creature.wisMod = 4;
        creature.Build = new CharacterBuild { ClassName = "Cleric" };
        creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
        creature.gameObject.AddComponent<Team>().Name = "players";
        return creature;
    }

    private CreatureComponent CreateCreature(string name, int x)
    {
        GameObject gameObject = new(name);
        created.Add(gameObject);
        gameObject.transform.position = new Vector3(x, 0, 0);
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        creature.InitializeHealthBeforeEncounter(10, 10);
        return creature;
    }

    private static Tile[,] CreateTiles(int width)
    {
        Tile[,] tiles = new Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new Tile();
        return tiles;
    }

    private static void Occupy(Tile[,] tiles, GameObject gameObject)
    {
        int x = Mathf.RoundToInt(gameObject.transform.position.x);
        tiles[x, 0].Occupants.Add(gameObject);
    }

    private CreatureAnimationController BindAnimatedPresentation(GameObject actor)
    {
        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/KayKit/Prefabs/Animated/MageStaffAnimated.prefab"
        );
        GameObject visual = Object.Instantiate(visualPrefab, actor.transform);
        created.Add(visual);
        CreatureAnimationController animation = visual.GetComponent<CreatureAnimationController>();
        actor
            .AddComponent<CreaturePresentation>()
            .Bind(animation, visual.GetComponent<CreatureEquipmentVisuals>());
        return animation;
    }

    private RecordingCombatLog InstallCombatLog()
    {
        GameObject gameObject = new("Light Test Combat Log");
        created.Add(gameObject);
        RecordingCombatLog log = gameObject.AddComponent<RecordingCombatLog>();
        SetCombatLog(log);
        return log;
    }

    private static void ResetCombatLog() => SetCombatLog(null);

    private static void SetCombatLog(CombatLogInterface log)
    {
        FieldInfo field = typeof(SingletonMonoBehaviour<CombatLogInterface>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        field.SetValue(null, log);
    }

    private static int GetAnimationPlaybackVersion(CreatureAnimationController animation)
    {
        FieldInfo field = typeof(CreatureAnimationController).GetField(
            "playbackVersion",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        return (int)field.GetValue(animation);
    }

    private void CountGameplayCommit() => gameplayCommitCount++;

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class RecordingCombatLog : CombatLogInterface
    {
        public List<string> Messages { get; } = new();

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

        public override List<string> GetMessages() => new(Messages);
    }
}
