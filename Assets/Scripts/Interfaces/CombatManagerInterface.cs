using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public abstract class CombatManagerInterface : SingletonMonoBehaviour<CombatManagerInterface>
{
    /// <summary>Raised whenever initiative starts or returns to dungeon exploration.</summary>
    public abstract event Action<bool> CombatActivityChanged;

    /// <summary>Gets whether the manager currently owns an active initiative round.</summary>
    public abstract bool IsCombatActive { get; }

    /// <summary>Starts legacy scene combat with every living registered controller.</summary>
    public abstract void StartCombat();

    /// <summary>
    /// Starts a dungeon-directed combat using only the supplied registered participants.
    /// </summary>
    /// <param name="participants">The living party and active encounter creatures.</param>
    public abstract void StartDungeonCombat(IReadOnlyList<ActionController> participants);

    /// <summary>
    /// Inserts newly activated dungeon creatures into the current initiative lifecycle.
    /// </summary>
    /// <param name="reinforcements">Newly activated registered controllers.</param>
    public abstract void AddDungeonReinforcements(IReadOnlyList<ActionController> reinforcements);

    /// <summary>
    /// Leaves dungeon combat without declaring a winner and clears transient turn state.
    /// </summary>
    public abstract void SuspendDungeonCombat();

    /// <summary>
    /// Refreshes the immutable rules topology after the live grid changes between actions.
    /// </summary>
    public virtual void RefreshRulesTopology() { }

    /// <summary>
    /// Checks whether fewer than two active teams remain and completes the active combat through
    /// its legacy or dungeon-directed outcome channel.
    /// </summary>
    /// <returns>True if combat has ended</returns>
    public abstract bool CheckForEndOfGame();

    /// <summary>
    /// Starts delegating combat turns
    /// </summary>
    public abstract void NextTurn();

    /// <summary>Registers a controller without making a dormant creature initiative-eligible.</summary>
    /// <param name="combatant">The non-null controller to register once.</param>
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

    /// <summary>Returns the current turn owner, or absence while combat is inactive.</summary>
    public abstract GameObject WhosTurn();

    /// <summary>
    /// Retrieves the list of active combatants
    /// </summary>
    /// <returns>GameObjects of the combatants</returns>
    public abstract List<GameObject> GetCombatants();
}
