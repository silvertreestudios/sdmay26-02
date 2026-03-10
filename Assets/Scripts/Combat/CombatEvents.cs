using UnityEngine;


// Can still access like static classes.
// C# doesn't allow static classes to inherit
// from anything other than object.

/// <summary>Triggered upon starting combat</summary>
public class OnCombatStart : StaticUnityEvent<OnCombatStart> { }

/// <summary>Triggered upon ending combat, contains the name of the winning team</summary>
public class OnCombatEnd : StaticUnityEvent<OnCombatEnd, string> {}

/// <summary>Triggered upon it becoming a creature's turn. Returns the creatures's GameObject</summary>
public class OnNextTurn : StaticUnityEvent<OnNextTurn, GameObject> {}

/// <summary>Triggered upon a new combatant joining combat</summary>
public class OnCombatantJoin : StaticUnityEvent<OnCombatantJoin, GameObject> {}