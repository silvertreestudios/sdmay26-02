using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class GameManager : SingletonMonoBehaviour<GameManager>
{
    // Whether or not combat is active
    private bool combatMode;

    [SerializeField]
    public TeamRules TeamRelationships { get; private set; }

    private void OnEnable()
    {
        OnCombatEnd.AddListener(NextLevel);
    }

    private void OnDisable()
    {
        OnCombatEnd.RemoveListener(NextLevel);
    }

    private void Start()
    {
        StartCoroutine("StartCombat");
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

        ActionController[] sceneControllers = Object
            .FindObjectsByType<ActionController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.InstanceID
            )
            .ToArray();
        bool hasEncounterPlans = validation.JsonMap.LevelDocument.EncounterPlans.Count > 0;
        if (!hasEncounterPlans)
        {
            bool hasAuthoredOpposition = sceneControllers.Any(controller =>
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
                "A JSON dungeon requires at least one active Players-team controller."
            );

        GameObject runtimeRoot = new("Dungeon Encounter Runtime");
        runtimeRoot.transform.SetParent(map.transform, false);
        try
        {
            if (!HUDController.TryGetInstance(out HUDController hud))
                throw new InvalidOperationException(
                    "A JSON dungeon with planned encounters requires an active HUDController."
                );
            DungeonEncounterRuntimeController runtime =
                runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
            DungeonEncounterCreatureCatalog encounterCatalog =
                DungeonEncounterCreatureCatalog.LoadDefaultOrThrow();
            if (validation.JsonMap.LevelDocument.RuntimeState == null)
            {
                runtime.InitializePristine(
                    validation.JsonMap.LevelDocument,
                    encounterCatalog,
                    combatManager,
                    party,
                    hud
                );
            }
            else
            {
                runtime.InitializePersisted(
                    validation.JsonMap.LevelDocument,
                    encounterCatalog,
                    combatManager,
                    party,
                    hud
                );
            }
        }
        catch
        {
            Destroy(runtimeRoot);
            throw;
        }
        yield return null;
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
