using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : SingletonMonoBehaviour<GameManager>
{
    // Whether or not combat is active
    private bool combatMode;

    [SerializeField]
    public TeamRules TeamRelationships { get; private set; }

    private void OnEnable()
    {
        OnCombatEnd.AddListener(EndCombat);
    }

    private void OnDisable()
    {
        OnCombatEnd.RemoveListener(EndCombat);
    }

    private void Start()
    {
        StartCoroutine("StartCombat");
    }

    private IEnumerator StartCombat()
    {
        CombatManagerInterface.GetInstance().StartCombat();
        HUDController.Setup();
        yield return null;
    }

    private void EndCombat(string winningTeam)
    {
        int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
            SceneManager.LoadScene(nextSceneIndex);
    }
}
