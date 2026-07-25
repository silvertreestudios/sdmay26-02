using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Strikes;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public sealed class RulesStrikeIntegrationPlayModeTests
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
        Pf2eItemCatalog.ResetForTests();
        yield return null;
    }

    [UnityTest]
    public IEnumerator CanceledStrikeEmitsOneGameplayCommitAndCompletesController()
    {
        InstallCombatManager();
        InstallCoroutineRunner();
        InstallCancelingGrid();
        CreatureComponent actorCreature = CreateCreature("Actor", "players", 20, 10);
        TestActionController controller =
            actorCreature.gameObject.AddComponent<TestActionController>();
        Place(actorCreature.gameObject, 0);
        Tile[,] tiles = CreateTiles(1);
        Occupy(tiles, actorCreature.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new[] { controller },
            tiles,
            new ScriptedRollService()
        );
        CreatureId actor = bridge.GetCreatureId(actorCreature);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction strike = controller.GetActions().OfType<RulesStrikeAction>().First();
        gameplayCommitCount = 0;
        OnGameplayStateCommitted.AddListener(CountGameplayCommit);
        controller.IsTakingAction = true;

        strike.Invoke(actorCreature.gameObject);
        for (int frame = 0; frame < 10 && gameplayCommitCount == 0; frame++)
            yield return null;
        yield return null;

        Assert.That(controller.IsTakingAction, Is.False);
        Assert.That(controller.ActionPoints, Is.EqualTo(3));
        Assert.That(gameplayCommitCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator AiRangedStrikeRejectsInvalidThenProjectsValidAmmoLoadHealthAndReload()
    {
        InstallCombatManager();
        CreatureComponent archer = CreateCreature("Archer", "strike-test-ai", 20, 10);
        EquipmentWeapon sling = new()
        {
            name = "Sling",
            group = "sling",
            category = "simple",
            range = 50,
            reload = "1",
            ammo = "sling-bullets",
            damage = new Dice(1, 6, "bludgeoning"),
        };
        archer.weapons = new List<EquipmentWeapon> { sling };
        archer.ammunition = new List<AmmoCount>
        {
            new AmmoCount { ammoName = "sling-bullets", quantity = 2 },
        };
        CreatureComponent target = CreateCreature("Target", "strike-test-target", 20, 10);
        TestAiController ai = archer.gameObject.AddComponent<TestAiController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        Place(archer.gameObject, 1000);
        Place(target.gameObject, 1061);
        Tile[,] tiles = CreateTiles(1062);
        Occupy(tiles, archer.gameObject);
        Occupy(tiles, target.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { ai, targetController },
            tiles,
            new ScriptedRollService(10, 4)
        );
        CreatureId actor = bridge.GetCreatureId(archer);
        CreatureId targetId = bridge.GetCreatureId(target);
        bridge.BeginTurn(actor, 3);
        RulesStrikeAction strike = (RulesStrikeAction)ai.BestStrike();

        Assert.That(strike.ActionName, Is.EqualTo("Sling"));
        Assert.That(
            bridge.Dispatch(new StrikeActionOp(actor, strike.Item.Item, targetId)),
            Is.TypeOf<InvalidOpResult<StrikeOutcome>>()
        );
        Assert.That(ai.ActionPoints, Is.EqualTo(3));
        Assert.That(ai.StrikePenalty, Is.Zero);
        Assert.That(archer.GetAmmoQuantity("sling-bullets"), Is.EqualTo(2));

        Move(tiles, target.gameObject, 1001);
        ResolvedOpResult<StrikeOutcome> result = RequireResolved(
            bridge.Dispatch(new StrikeActionOp(actor, strike.Item.Item, targetId))
        );
        Assert.That(result.Value.Resolution.Hit, Is.True);
        Assert.That(ai.ActionPoints, Is.EqualTo(2));
        Assert.That(ai.StrikePenalty, Is.EqualTo(1));
        Assert.That(archer.GetAmmoQuantity("sling-bullets"), Is.EqualTo(1));
        Assert.That(archer.IsWeaponLoaded(sling), Is.False);
        Assert.That(target.hp, Is.EqualTo(16));

        Assert.That(
            bridge.Dispatch(new ReloadActionOp(actor, strike.Item.Item)),
            Is.TypeOf<ResolvedOpResult<ReloadOutcome>>()
        );
        Assert.That(ai.ActionPoints, Is.EqualTo(1));
        Assert.That(archer.IsWeaponLoaded(sling), Is.True);
        yield return null;
    }

    [UnityTest]
    public IEnumerator NormalStrikeAndLegacySpellAttackShareMapInBothOrders()
    {
        InstallCombatLog();
        CreatureComponent cleric = CreateCreature("Cleric", "player", 100, 10);
        cleric.level = 1;
        cleric.wisMod = 4;
        cleric.Build = new CharacterBuild { ClassName = "Cleric" };
        cleric.Prepared = Pf2eCharacterPreparer.Prepare(cleric, cleric.Build);
        CreatureComponent target = CreateCreature("Target", "enemy", 100, 10);
        TestActionController clericController =
            cleric.gameObject.AddComponent<TestActionController>();
        TestActionController targetController =
            target.gameObject.AddComponent<TestActionController>();
        Place(cleric.gameObject, 0);
        Place(target.gameObject, 1);
        Tile[,] tiles = CreateTiles(2);
        Occupy(tiles, cleric.gameObject);
        Occupy(tiles, target.gameObject);
        UnityCombatRulesBridge bridge = UnityCombatRulesBridge.Create(
            new ActionController[] { clericController, targetController },
            tiles,
            new ScriptedRollService(10, 2, 10, 2)
        );
        CreatureId actor = bridge.GetCreatureId(cleric);
        CreatureId targetId = bridge.GetCreatureId(target);
        RulesStrikeAction unarmed = clericController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(action => action.ActionName == "Unarmed Strike");
        PreparedSpell divineLance = cleric.Prepared.Spellcasting.GetSpell("divine-lance");
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(7113);

        bridge.BeginTurn(actor, 3);
        RequireResolved(bridge.Dispatch(new StrikeActionOp(actor, unarmed.Item.Item, targetId)));
        CastSpellResult spellSecond = SpellcastingRuntime.Cast(
            cleric.gameObject,
            divineLance,
            2,
            new[] { target.gameObject }
        );
        Assert.That(spellSecond.Success, Is.True, spellSecond.Message);
        Assert.That(clericController.StrikePenalty, Is.EqualTo(2));

        bridge.BeginTurn(actor, 3);
        CastSpellResult spellFirst = SpellcastingRuntime.Cast(
            cleric.gameObject,
            divineLance,
            2,
            new[] { target.gameObject }
        );
        Assert.That(spellFirst.Success, Is.True, spellFirst.Message);
        ResolvedOpResult<StrikeOutcome> strikeSecond = RequireResolved(
            bridge.Dispatch(new StrikeActionOp(actor, unarmed.Item.Item, targetId))
        );
        Assert.That(strikeSecond.Value.Resolution.MultipleAttackPenalty, Is.EqualTo(-4));
        Assert.That(clericController.StrikePenalty, Is.EqualTo(2));
        UnityEngine.Random.state = randomState;
        yield return null;
    }

    private CreatureComponent CreateCreature(string name, string teamName, int hp, int ac)
    {
        GameObject gameObject = new(name);
        created.Add(gameObject);
        Team team = gameObject.AddComponent<Team>();
        team.Name = teamName;
        CreatureComponent creature = gameObject.AddComponent<CreatureComponent>();
        gameObject.AddComponent<Conditions>();
        creature.name = name;
        creature.ac = ac;
        creature.InitializeHealthBeforeEncounter(hp, hp);
        return creature;
    }

    private void InstallCombatLog()
    {
        GameObject gameObject = new("Strike PlayMode Combat Log");
        created.Add(gameObject);
        TestCombatLog log = gameObject.AddComponent<TestCombatLog>();
        FieldInfo field = typeof(SingletonMonoBehaviour<CombatLogInterface>).GetField(
            "Instance",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        field.SetValue(null, log);
    }

    private void InstallCombatManager()
    {
        GameObject gameObject = new("Strike PlayMode Combat Manager");
        created.Add(gameObject);
        gameObject.AddComponent<CombatManager>();
    }

    private void InstallCoroutineRunner()
    {
        GameObject gameObject = new("Strike PlayMode Coroutine Runner");
        created.Add(gameObject);
        gameObject.AddComponent<CoroutineRunner>();
    }

    private void InstallCancelingGrid()
    {
        if (GridAPI.TryGetInstance(out GridAPI activeGrid))
            Object.DestroyImmediate(activeGrid.gameObject);
        GameObject gameObject = new("Strike PlayMode Canceling Grid");
        created.Add(gameObject);
        gameObject.AddComponent<CancelingGridApi>();
    }

    private void CountGameplayCommit() => gameplayCommitCount++;

    private static Tile[,] CreateTiles(int width)
    {
        Tile[,] tiles = new Tile[width, 1];
        for (int x = 0; x < width; x++)
            tiles[x, 0] = new Tile();
        return tiles;
    }

    private static void Place(GameObject gameObject, int x) =>
        gameObject.transform.position = new Vector3(x, 0, 0);

    private static void Occupy(Tile[,] tiles, GameObject gameObject)
    {
        int x = Mathf.RoundToInt(gameObject.transform.position.x);
        tiles[x, 0].Occupants.Add(gameObject);
    }

    private static void Move(Tile[,] tiles, GameObject gameObject, int x)
    {
        foreach (Tile tile in tiles)
            tile?.Occupants.Remove(gameObject);
        Place(gameObject, x);
        Occupy(tiles, gameObject);
    }

    private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result)
    {
        string failure = result is InvalidOpResult<T> invalid
            ? invalid.Reason
            : "Operation did not resolve.";
        Assert.That(result, Is.TypeOf<ResolvedOpResult<T>>(), failure);
        return (ResolvedOpResult<T>)result;
    }

    private sealed class TestActionController : ActionController
    {
        public override void EndTurn() { }
    }

    private sealed class TestAiController : AIActionController { }

    private sealed class TestCombatLog : CombatLogInterface
    {
        public override void DevMode() { }

        public override void ReleaseMode() { }

        public override void AddWhiteList(string tag) { }

        public override void AddBlackList(string tag) { }

        public override void DevLog(string msg) { }

        public override void DevLog(string msg, string tag) { }

        public override void DevLog(string msg, List<string> tags) { }

        public override void Log(string msg) { }

        public override void Log(string msg, string tag) { }

        public override void Log(string msg, List<string> tags) { }

        public override List<string> GetMessages() => new();
    }

    private sealed class CancelingGridApi : GridAPI
    {
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
}

public sealed class RulesStrikeScenePlayModeTests : PlayModeBase
{
    [UnityTest]
    public IEnumerator PlayerRangedStrikeCompletesFsmAndProjectsHudAmmoMapAndHealth()
    {
        GameObject lena = null;
        yield return WaitUntilWithTimeout(
            timeout,
            () =>
            {
                lena = Object
                    .FindObjectsByType<CreatureComponent>(FindObjectsSortMode.None)
                    .FirstOrDefault(creature => creature.name == "Lena")
                    ?.gameObject;
                return lena != null
                    && lena.GetComponent<PlayerActionController>() != null
                    && lena.GetComponent<CreatureComponent>().Prepared != null
                    && lena.GetComponent<ActionController>()
                        .GetActions()
                        .Any(action => action.ActionName == "Shortbow");
            }
        );
        Assert.That(lena, Is.Not.Null);
        GameObject target = FindHostileTarget(lena);
        GridBase grid = Object.FindFirstObjectByType<GridBase>();
        FindEmptyStraightLine(
            grid.GetTiles(),
            5,
            out Vector3Int lenaCell,
            out Vector3Int targetCell
        );
        MoveCombatant(grid.GetTiles(), lena, lenaCell);
        MoveCombatant(grid.GetTiles(), target, targetCell);
        CreatureComponent targetCreature = target.GetComponent<CreatureComponent>();
        targetCreature.ac = 1;
        targetCreature.GrantSourceTemporaryHitPoints(RuleSource.FromSlug("strike-scene-test"), 100);
        CreatureComponent lenaCreature = lena.GetComponent<CreatureComponent>();
        Pf2eModifierCollection modifiers = lena.AddComponent<Pf2eModifierCollection>();
        modifiers.Add(
            new Pf2eModifier(
                100,
                Pf2eModifierType.Untyped,
                "Strike scene test",
                Pf2eStatistic.AttackRoll
            )
        );
        ActionController controller = lena.GetComponent<ActionController>();
        controller.StartTurn();
        OnNextTurn.Invoke(lena);
        Button shortbow = null;
        yield return WaitUntilWithTimeout(
            timeout,
            () =>
            {
                shortbow = root.Q<Button>("ShortbowButton");
                return shortbow != null;
            }
        );
        Assert.That(shortbow, Is.Not.Null);
        int startingAmmo = lenaCreature.GetAmmoQuantity("arrows");

        PushButton(shortbow);
        yield return WaitUntilWithTimeout(timeout, () => grid.Fsm.CurrentState is StateStrike);
        OnHover.Invoke(new List<Vector3Int> { targetCell });
        grid.Fsm.CurrentState.Leftclick();
        yield return WaitUntilWithTimeout(
            timeout,
            () => !controller.IsTakingAction && grid.Fsm.CurrentState is StateIdle
        );

        Assert.That(grid.Fsm.CurrentState, Is.TypeOf<StateIdle>());
        Assert.That(controller.ActionPoints, Is.EqualTo(2));
        Assert.That(controller.StrikePenalty, Is.EqualTo(1));
        Assert.That(lenaCreature.GetAmmoQuantity("arrows"), Is.EqualTo(startingAmmo - 1));
        Assert.That(targetCreature.tempHp, Is.LessThan(100));
    }

    private static GameObject FindHostileTarget(GameObject actor)
    {
        string actorTeam = actor.GetComponent<Team>().Name;
        GameObject target = Object
            .FindObjectsByType<ActionController>(FindObjectsSortMode.None)
            .Select(controller => controller.gameObject)
            .FirstOrDefault(candidate =>
                candidate != actor
                && candidate.TryGetComponent(out Team team)
                && !TeamRules.GetInstance().IsFriendly(actorTeam, team.Name)
            );
        Assert.That(target, Is.Not.Null);
        return target;
    }

    private static void FindEmptyStraightLine(
        Tile[,] tiles,
        int length,
        out Vector3Int start,
        out Vector3Int target
    )
    {
        for (int z = 0; z < tiles.GetLength(1); z++)
        {
            for (int x = 0; x <= tiles.GetLength(0) - length; x++)
            {
                bool clear = true;
                for (int offset = 0; offset < length; offset++)
                {
                    Tile tile = tiles[x + offset, z];
                    if (tile == null || tile.Occupants.Count > 0)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear)
                    continue;
                start = new Vector3Int(x, 0, z);
                target = new Vector3Int(x + length - 1, 0, z);
                return;
            }
        }
        Assert.Fail("Could not find a clear ranged Strike lane.");
        start = Vector3Int.zero;
        target = Vector3Int.zero;
    }

    private static void MoveCombatant(Tile[,] tiles, GameObject combatant, Vector3Int cell)
    {
        foreach (Tile tile in tiles)
            tile?.Occupants.Remove(combatant);
        combatant.transform.position = cell;
        tiles[cell.x, cell.z].Occupants.Add(combatant);
    }
}
