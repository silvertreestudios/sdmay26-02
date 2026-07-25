using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence;
using Game.DungeonPersistence.Repository;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Selects legacy combat startup or the player-approved persistent procedural dungeon launch for
/// the active gameplay scene.
/// </summary>
public class GameManager : SingletonMonoBehaviour<GameManager>
{
    // Whether or not combat is active
    private bool combatMode;

    [SerializeField]
    public TeamRules TeamRelationships { get; private set; }

    private DungeonRunController dungeonRunController;
    private HUDController dungeonHud;

    private void OnEnable()
    {
        OnCombatEnd.AddListener(NextLevel);
    }

    private void OnDisable()
    {
        OnCombatEnd.RemoveListener(NextLevel);
        if (dungeonRunController != null)
            dungeonRunController.CurrentDepthChanged -= OnDungeonDepthChanged;
    }

    private void Start()
    {
        StartCoroutine(StartCombat());
    }

    private IEnumerator StartCombat()
    {
        CombatManagerInterface combatManager = CombatManagerInterface.GetInstance();
        Map[] jsonMaps = Object
            .FindObjectsByType<Map>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(map => map.SourceMode == MapSourceMode.Json)
            .ToArray();
        if (jsonMaps.Length == 0)
        {
            combatManager.StartCombat();
            yield break;
        }
        if (jsonMaps.Length > 1)
            throw new InvalidOperationException(
                "A scene can contain only one active JSON dungeon map."
            );

        Map map = jsonMaps[0];
        MapSourceValidationResult validation = map.ValidateSource();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "The JSON dungeon cannot initialize encounters: "
                    + string.Join(" ", validation.Errors)
            );
        }

        bool hasEncounterPlans = validation.JsonMap.LevelDocument.EncounterPlans.Count > 0;
        if (!hasEncounterPlans)
        {
            ActionController[] legacyControllers = Object
                .FindObjectsByType<ActionController>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.InstanceID
                )
                .ToArray();
            bool hasAuthoredOpposition = legacyControllers.Any(controller =>
                !string.Equals(
                    controller.GetComponent<Team>()?.Name,
                    "Players",
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (hasAuthoredOpposition)
            {
                combatManager.StartCombat();
                yield break;
            }
        }

        if (!SceneTransitionManager.TryConsumeDungeonRunLaunch(out DungeonRunLaunchRequest launch))
        {
            Debug.Log(
                "ProceduralDungeon loaded without a menu launch request; the reusable scene remains passive."
            );
            yield break;
        }

        ActionController[] sceneControllers = Object
            .FindObjectsByType<ActionController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.InstanceID
            )
            .ToArray();
        ActionController[] party = sceneControllers
            .Where(controller =>
                string.Equals(
                    controller.GetComponent<Team>()?.Name,
                    "Players",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToArray();
        if (party.Length == 0)
            throw new InvalidOperationException(
                "A JSON dungeon requires at least one authored Players-team controller."
            );

        GameObject runtimeRoot = new("Dungeon Encounter Runtime");
        runtimeRoot.transform.SetParent(map.transform, false);
        try
        {
            if (!HUDController.TryGetInstance(out HUDController hud))
                throw new InvalidOperationException(
                    "A JSON dungeon with planned encounters requires an active HUDController."
                );
            // Dungeon bootstrap can fail before exploration presentation enables the HUD. Bind
            // its UI first so both success status and blocking diagnostics are always safe.
            hud.EnableUi();
            DungeonEncounterCreatureCatalog encounterCatalog =
                DungeonEncounterCreatureCatalog.LoadDefaultOrThrow();
            DungeonLevelDocument template =
                launch.Mode == DungeonRunLaunchMode.NewRun
                    ? WithRunSeed(validation.JsonMap.LevelDocument, launch.NormalizedSeed)
                    : validation.JsonMap.LevelDocument;
            DungeonRunPersistenceBootstrapResult bootstrap = launch.Mode switch
            {
                DungeonRunLaunchMode.NewRun => DungeonRunPersistenceBootstrap.StartNewRun(
                    map,
                    template,
                    encounterCatalog,
                    combatManager,
                    party,
                    hud,
                    runtimeRoot,
                    launch.AutosaveDirectory
                ),
                DungeonRunLaunchMode.Continue => DungeonRunPersistenceBootstrap.ContinueRun(
                    map,
                    template,
                    encounterCatalog,
                    combatManager,
                    party,
                    hud,
                    runtimeRoot,
                    launch.AutosaveDirectory
                ),
                _ => throw new InvalidOperationException(
                    "The procedural dungeon received no supported launch operation."
                ),
            };
            if (!bootstrap.IsSuccess)
            {
                string message = PlayerFacingFailure(bootstrap.Diagnostics);
                hud.ShowDungeonRunError(message);
                Debug.LogWarning(
                    message
                        + " "
                        + string.Join(" ", bootstrap.Diagnostics.Select(item => item.Message))
                );
                Destroy(runtimeRoot);
                yield break;
            }

            dungeonRunController = bootstrap.Controller;
            dungeonHud = hud;
            dungeonRunController.CurrentDepthChanged += OnDungeonDepthChanged;
            dungeonHud.ShowDungeonRunStatus(
                dungeonRunController.StartingSeed,
                dungeonRunController.CurrentDepth
            );
        }
        catch
        {
            Destroy(runtimeRoot);
            throw;
        }
        yield return null;
    }

    private void OnDungeonDepthChanged(int depth)
    {
        if (dungeonHud != null && dungeonRunController != null)
            dungeonHud.ShowDungeonRunStatus(dungeonRunController.StartingSeed, depth);
    }

    private static DungeonLevelDocument WithRunSeed(DungeonLevelDocument source, int runSeed) =>
        new(
            new DungeonGenerationMetadata(
                source.Generation.Algorithm,
                runSeed,
                source.Generation.Depth,
                source.Generation.TopologyAttempt
            ),
            source.Rows,
            source.Rooms,
            source.Doors,
            source.Stairs,
            source.StartCell,
            source.SafeCells,
            source.Objects,
            source.EncounterPlans,
            source.RuntimeState
        );

    private static string PlayerFacingFailure(IReadOnlyList<DungeonSaveDiagnostic> diagnostics)
    {
        DungeonSaveDiagnosticCode code =
            diagnostics.Count == 0
                ? DungeonSaveDiagnosticCode.InvalidSnapshot
                : diagnostics[0].Code;
        return code switch
        {
            DungeonSaveDiagnosticCode.MissingSave =>
                "No saved dungeon run is available to continue.",
            DungeonSaveDiagnosticCode.CorruptSave =>
                "The dungeon autosave is corrupt. Return to the menu and start a new run.",
            DungeonSaveDiagnosticCode.IncompatibleVersion =>
                "The dungeon autosave is incompatible with this version.",
            DungeonSaveDiagnosticCode.IoFailure => "The dungeon autosave could not be accessed.",
            _ => "The dungeon run could not be started.",
        };
    }

    private void NextLevel(string winningTeam)
    {
        if (winningTeam.ToLower() == "players")
        {
            //Debug.Log("Players win!");
            //next level
            OnNextLevelRequest.Invoke(true);
            //invoke win sfx
        }
        else
        {
            //Debug.Log("You lose NEEEEEEEERRRRRRD!");
            //invoke lose sfx
            //reset scene
            //StartCoroutine(ResetSceneRoutine());
        }
    }

    private IEnumerator ResetSceneRoutine()
    {
        //temporary wait, delete once retry button is implemented
        yield return new WaitForSeconds(3f);
        SceneTransitionManager.FadeAndLoad(SceneManager.GetActiveScene().buildIndex);
    }
}
