using System.Collections.Generic;
using UnityEngine;

public abstract class CombatManagerInterface : SingletonMonoBehaviour<CombatManagerInterface>
{
    /// <summary>
    /// Adds a combatant to the action queue
    /// </summary>
    /// <param name="combatant"> The action controller to add</param>
    public abstract void StartCombat();

    /// <summary>
    /// Starts delegating combat turns
    /// </summary>
    public abstract void NextTurn();

    /// <summary>
    /// Starts the next Combatant's turn
    /// </summary>
    public abstract void AddCombatant(ActionController combatant);

    /// <summary>
    /// Returns the GameObject Who's Turn it is
    /// </summary>
    public abstract GameObject WhosTurn();

    /// <summary>
    /// Retrieves the list of active combatants
    /// </summary>
    /// <returns>GameObjects of the combatants</returns>
    public abstract List<GameObject> GetCombatants();

    // Temporary function
    public abstract GameObject GetTarget(GameObject attacker);
}
