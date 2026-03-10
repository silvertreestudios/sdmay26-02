using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : SingletonMonoBehaviour<GameManager>
{
    // Whether or not combat is active
    private bool combatMode;

    [SerializeField]
    public TeamRules TeamRelationships { get; private set; }

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
}
