using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public abstract class CombatManagerInterface : SingletonMonoBehaviour<CombatManagerInterface>
{
    /// <summary>
    /// Adds a combatant to the action queue
    /// </summary>
    /// <param name="combatant"> The action controller to add</param>
    public abstract void StartCombat();

    /// <summary>
    /// Checks all conditions for the end of the game, fires the OnCombatEnd event
    /// </summary>
    /// <returns>True if combat has ended</returns>
    public abstract bool CheckForEndOfGame();

    /// <summary>
    /// Starts delegating combat turns
    /// </summary>
    public abstract void NextTurn();

    /// <summary>
    /// Adds a combatant
    /// </summary>
    public abstract void AddCombatant(ActionController combatant);

    /// <summary>
    /// Adds an event to the TurnQueue
    /// </summary>
    public abstract void AddEvent(TurnStep ts);

    /// <summary>
    /// Removes a combatant from the CombatManager
    /// </summary>
    /// <param name="combatant"></param>
    public abstract void Remove(ActionController combatant);

    /// <summary>
    /// Removes a TurnStep from the combat TurnQueue
    /// </summary>
    /// <param name="e"></param>
    public abstract void Remove(TurnStep e);

    /// <summary>
    /// Removes an Event from everywhere in the combat queue
    /// </summary>
    /// <typeparam name="T">The parameter type for the event</typeparam>
    /// <param name="e">the event to remove</param>
    public abstract void Remove(UnityAction e);

    /// <summary>
    /// Returns the GameObject Who's Turn it is
    /// </summary>
    public abstract GameObject WhosTurn();

    /// <summary>
    /// Retrieves the list of active combatants
    /// </summary>
    /// <returns>GameObjects of the combatants</returns>
    public abstract List<GameObject> GetCombatants();
}
