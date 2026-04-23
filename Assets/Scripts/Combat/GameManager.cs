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
        CombatManagerInterface.GetInstance().StartCombat();
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
