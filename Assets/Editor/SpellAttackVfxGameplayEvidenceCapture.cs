#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Vfx;
using Game.Strikes;
using GridPrivate;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using RulesDegreeOfSuccess = Game.Rules.Runtime.DegreeOfSuccess;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>Captures full Game View evidence from authoritative actions on the production test grid.</summary>
[InitializeOnLoad]
public static class SpellAttackVfxGameplayEvidenceCapture
{
    private const string ActiveKey = "SpellAttackVfxGameplayEvidenceCapture.Active";

    static SpellAttackVfxGameplayEvidenceCapture()
    {
        if (SessionState.GetBool(ActiveKey, false))
            Register();
    }

    /// <summary>Opens the real gameplay scene and captures one committed Strike and spell.</summary>
    public static void Begin()
    {
        SessionState.SetBool(ActiveKey, true);
        Register();
        EditorSceneManager.OpenScene("Assets/Scenes/UnitTestingScene.unity");
        GameManager sceneGameManager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        if (sceneGameManager != null)
            sceneGameManager.enabled = false;
        EditorApplication.EnterPlaymode();
    }

    private static void Register()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Screen.SetResolution(1920, 1080, false);
            GameManager sceneGameManager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
            if (sceneGameManager != null)
                sceneGameManager.enabled = false;
            new GameObject(
                "Gameplay VFX Evidence Driver"
            ).AddComponent<GameplayVfxEvidenceDriver>();
        }
        else if (
            state == PlayModeStateChange.EnteredEditMode
            && SessionState.GetBool(ActiveKey, false)
        )
        {
            GameManager sceneGameManager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
            if (sceneGameManager != null)
                sceneGameManager.enabled = true;
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.Exit(0);
        }
    }
}

/// <summary>Runs deterministic actions while retaining production rules and Unity presentation.</summary>
public sealed class GameplayVfxEvidenceDriver : MonoBehaviour
{
    private UnityCombatRulesBridge bridge;

    private IEnumerator Start()
    {
        Time.timeScale = 1f;
        yield return null;
        GridBase grid = FindFirstObjectByType<GridBase>();
        if (grid == null)
            throw new InvalidOperationException("UnitTestingScene has no production GridBase.");
        Tile[,] tiles = grid.GetTiles();
        (Vector3Int sourceCell, Vector3Int targetCell) = FindClearRun(tiles, 4);

        CreatureComponent maren = CreateCreature(
            "DataFiles/playerCharacters/Maren",
            "Assets/Prefabs/Creatures/Maren.prefab"
        );
        CreatureComponent goblin = CreateCreature(
            "DataFiles/pathfinder-monster-core/goblin-warrior",
            "Assets/Prefabs/Creatures/goblin-warrior.prefab"
        );
        maren.gameObject.name = "Maren — VFX Evidence";
        goblin.gameObject.name = "Goblin Warrior — VFX Evidence";
        maren.GetComponent<Team>().Name = "Players";
        goblin.GetComponent<Team>().Name = "Enemies";
        goblin.InitializeHealthBeforeEncounter(100, 100);
        goblin.ac = 1;
        Place(tiles, maren.gameObject, sourceCell);
        Place(tiles, goblin.gameObject, targetCell);
        FaceEachOther(maren.transform, goblin.transform);
        FrameCamera(maren.transform.position, goblin.transform.position);

        Game.KayKit.CreatureAnimationController marenAnimation = maren
            .GetComponent<Game.KayKit.CreaturePresentation>()
            ?.AnimationController;
        if (marenAnimation != null)
            marenAnimation.enabled = false;

        ActionController marenController = maren.GetComponent<ActionController>();
        ActionController goblinController = goblin.GetComponent<ActionController>();
        bridge = UnityCombatRulesBridge.Create(
            new[] { marenController, goblinController },
            tiles,
            new ScriptedRollService(20, 1, 20, 6, 20, 4, 4, 4, 4, 4, 4, 4, 4),
            "Players"
        );
        CreatureId actor = bridge.GetCreatureId(maren);
        CreatureId target = bridge.GetCreatureId(goblin);
        string evidenceRoot = Path.GetFullPath(
            ".agent-temp/delivery/feat-spell-attack-vfx-gallery/screenshots"
        );
        Directory.CreateDirectory(evidenceRoot);

        Move(tiles, goblin.gameObject, sourceCell + Vector3Int.right);
        FaceEachOther(maren.transform, goblin.transform);
        bridge.RefreshTopology(tiles);
        BeginTurn(actor);
        RulesStrikeAction mace = marenController
            .GetActions()
            .OfType<RulesStrikeAction>()
            .Single(action => action.ActionName == "Mace");
        StrikeActionOp strike = new(actor, mace.Item.Item, target);
        long strikeMemory = Profiler.GetTotalAllocatedMemoryLong();
        Stopwatch strikeClock = Stopwatch.StartNew();
        ResolvedOpResult<StrikeResolution> strikeResult = RequireResolved(
            bridge.Dispatch(strike),
            "Mace Strike"
        );
        Debug.Log($"VFX_GAMEPLAY_STRIKE resolved={strikeResult.Value.Degree}");
        bool strikeComplete = false;
        StartCoroutine(
            Drain(
                bridge.DrainActionPresentation(strike),
                "Mace Strike",
                () =>
                {
                    strikeComplete = true;
                    strikeClock.Stop();
                    Debug.Log(
                        $"VFX_GAMEPLAY_PROFILE action=strike elapsed={strikeClock.Elapsed.TotalMilliseconds:F2}ms "
                            + $"allocatedDelta={Profiler.GetTotalAllocatedMemoryLong() - strikeMemory}B"
                    );
                }
            )
        );
        string strikeCue =
            strikeResult.Value.Degree == RulesDegreeOfSuccess.CriticalSuccess
                ? "strike/bludgeoning/critical"
                : "strike/bludgeoning/hit";
        yield return WaitForCue(strikeCue, marenAnimation, 12f);
        yield return Capture(Path.Combine(evidenceRoot, "gameplay-strike.png"));
        while (!strikeComplete)
            yield return null;

        Move(tiles, goblin.gameObject, targetCell);
        FaceEachOther(maren.transform, goblin.transform);
        FrameCamera(maren.transform.position, goblin.transform.position);
        bridge.RefreshTopology(tiles);
        BeginTurn(actor);
        CastSpellActionOp spell = new(
            actor,
            new SpellReference(new SpellId("divine-lance"), 1),
            new SpellActionVariant(2),
            new SpellCastSelection(new[] { target })
        );
        long spellMemory = Profiler.GetTotalAllocatedMemoryLong();
        Stopwatch spellClock = Stopwatch.StartNew();
        ResolvedOpResult<CastSpellOutcome> spellResult = RequireResolved(
            bridge.Dispatch(spell),
            "Divine Lance"
        );
        Debug.Log(
            $"VFX_GAMEPLAY_SPELL resolved={spellResult.Value.AttackResolutions.Single().Degree}"
        );
        bool spellComplete = false;
        StartCoroutine(
            Drain(
                bridge.DrainActionPresentation(spell),
                "Divine Lance",
                () =>
                {
                    spellComplete = true;
                    spellClock.Stop();
                    Debug.Log(
                        $"VFX_GAMEPLAY_PROFILE action=spell elapsed={spellClock.Elapsed.TotalMilliseconds:F2}ms "
                            + $"allocatedDelta={Profiler.GetTotalAllocatedMemoryLong() - spellMemory}B"
                    );
                }
            )
        );
        RulesDegreeOfSuccess spellDegree = spellResult.Value.AttackResolutions.Single().Degree;
        string spellCue =
            spellDegree == RulesDegreeOfSuccess.CriticalSuccess
                ? "spell/divine-lance/critical"
                : "spell/divine-lance/hit";
        yield return WaitForCue(spellCue, marenAnimation, 12f);
        yield return Capture(Path.Combine(evidenceRoot, "gameplay-spell.png"));
        while (!spellComplete)
            yield return null;

        bridge.ReleaseOwnership();
        bridge = null;
        EditorApplication.ExitPlaymode();
    }

    private void OnDestroy()
    {
        bridge?.ReleaseOwnership();
        bridge = null;
    }

    private static CreatureComponent CreateCreature(string jsonPath, string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Missing production creature prefab " + prefabPath);
        return CreatureJsonConverter
            .CreateFromFile(jsonPath, prefab)
            .GetComponent<CreatureComponent>();
    }

    private void BeginTurn(CreatureId actor)
    {
        EncounterState encounter = bridge.GetEncounter();
        bool startedNow = encounter.Phase == EncounterPhase.Initialized;
        if (startedNow)
        {
            bridge.AdvanceEncounter();
            encounter = bridge.GetEncounter();
        }
        int remaining = bridge.Snapshot.Creatures.Count + 1;
        if (
            !startedNow
            && encounter.Phase == EncounterPhase.Active
            && encounter.CurrentTurn.HasValue
            && encounter.CurrentTurn.Value.Actor == actor
        )
        {
            bridge.EndTurn(actor);
            encounter = bridge.GetEncounter();
        }
        while (
            encounter.Phase == EncounterPhase.Active
            && encounter.CurrentTurn.HasValue
            && encounter.CurrentTurn.Value.Actor != actor
            && remaining-- > 0
        )
        {
            bridge.EndTurn(encounter.CurrentTurn.Value.Actor);
            encounter = bridge.GetEncounter();
        }
        if (!encounter.CurrentTurn.HasValue || encounter.CurrentTurn.Value.Actor != actor)
            throw new InvalidOperationException(
                "Maren could not obtain authoritative turn ownership."
            );
    }

    private static ResolvedOpResult<T> RequireResolved<T>(OpResult<T> result, string label)
    {
        if (result is ResolvedOpResult<T> resolved)
            return resolved;
        string reason = result is InvalidOpResult<T> invalid
            ? invalid.Reason
            : result.GetType().Name;
        throw new InvalidOperationException(label + " did not resolve: " + reason);
    }

    private static IEnumerator Drain(IEnumerator routine, string label, Action complete)
    {
        Debug.Log("VFX_GAMEPLAY_DRAIN begin=" + label);
        while (routine.MoveNext())
            yield return routine.Current;
        Debug.Log("VFX_GAMEPLAY_DRAIN complete=" + label);
        complete();
    }

    private static IEnumerator WaitForCue(
        string cue,
        Game.KayKit.CreatureAnimationController animation,
        float timeoutSeconds
    )
    {
        float started = Time.realtimeSinceStartup;
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        HashSet<string> observed = new(StringComparer.Ordinal);
        while (Time.realtimeSinceStartup < deadline)
        {
            UnityVfxInstance[] instances = FindObjectsByType<UnityVfxInstance>(
                FindObjectsSortMode.None
            );
            foreach (UnityVfxInstance instance in instances)
                observed.Add(instance.name);
            if (instances.Any(instance => instance.name.Contains(cue, StringComparison.Ordinal)))
                yield break;
            if (
                animation != null
                && animation.isActiveAndEnabled
                && Time.realtimeSinceStartup - started > 3.5f
            )
            {
                animation.StopAction();
                animation.enabled = false;
            }
            yield return null;
        }
        string live = string.Join(
            ", ",
            FindObjectsByType<UnityVfxInstance>(FindObjectsSortMode.None)
                .Select(value => value.name)
        );
        EditorApplication.ExitPlaymode();
        throw new TimeoutException(
            "Production presentation never emitted "
                + cue
                + ". Observed cues: "
                + string.Join(", ", observed)
                + ". Live cues: "
                + live
        );
    }

    private static IEnumerator Capture(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!File.Exists(path) && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (!File.Exists(path))
            throw new IOException("Unity did not write gameplay evidence " + path);
        yield return new WaitForSecondsRealtime(0.3f);
    }

    private static (Vector3Int Source, Vector3Int Target) FindClearRun(Tile[,] tiles, int distance)
    {
        for (int z = 2; z < tiles.GetLength(1) - 2; z++)
        for (int x = 2; x + distance < tiles.GetLength(0) - 2; x++)
        {
            bool clear = true;
            for (int offset = 0; offset <= distance; offset++)
            {
                Tile tile = tiles[x + offset, z];
                if (tile == null || tile.Occupants.Count > 0)
                {
                    clear = false;
                    break;
                }
            }
            if (clear)
                return (new Vector3Int(x, 0, z), new Vector3Int(x + distance, 0, z));
        }
        throw new InvalidOperationException("The production test grid has no clear five-cell run.");
    }

    private static void Move(Tile[,] tiles, GameObject creature, Vector3Int cell)
    {
        foreach (Tile tile in tiles)
            tile?.Occupants.Remove(creature);
        Place(tiles, creature, cell);
    }

    private static void Place(Tile[,] tiles, GameObject creature, Vector3Int cell)
    {
        creature.transform.position = cell;
        tiles[cell.x, cell.z].Occupants.Add(creature);
        Physics.SyncTransforms();
    }

    private static void FaceEachOther(Transform source, Transform target)
    {
        source.rotation = Quaternion.LookRotation(target.position - source.position);
        target.rotation = Quaternion.LookRotation(source.position - target.position);
    }

    private static void FrameCamera(Vector3 source, Vector3 target)
    {
        Camera camera = Camera.main;
        if (camera == null)
            throw new InvalidOperationException("UnitTestingScene has no gameplay camera.");
        Vector3 midpoint = Vector3.Lerp(source, target, 0.5f);
        camera.transform.position = midpoint + new Vector3(0f, 7f, -8f);
        camera.transform.LookAt(midpoint + Vector3.up * 0.7f);
        camera.fieldOfView = 48f;
    }
}
#endif
