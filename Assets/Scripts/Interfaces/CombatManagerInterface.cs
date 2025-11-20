using UnityEngine;

public abstract class CombatManagerInterface : SingletonMonoBehaviour<CombatManagerInterface>
{
    public abstract void StartCombat();
    public abstract void NextTurn();
    public abstract void AddCombatant(ActionController combatant);
}
