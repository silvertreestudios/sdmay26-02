using UnityEngine;

// Can still access like static classes.
// C# doesn't allow static classes to inherit
// from anything other than object.

/// <summary>
/// Triggered after combat startup is durable and before the first turn is presented to Unity.
/// Startup attempts that roll back do not publish this event.
/// </summary>
public class OnCombatStart : StaticUnityEvent<OnCombatStart> { }

/// <summary>Triggered upon ending combat, contains the name of the winning team</summary>
public class OnCombatEnd : StaticUnityEvent<OnCombatEnd, string> { }

/// <summary>Triggered upon a new level request from the HUD controller, true if next level, false if retry</summary>
public class OnNextLevelRequest : StaticUnityEvent<OnNextLevelRequest, bool> { }

/// <summary>Triggered upon it becoming a creature's turn. Returns the creatures's GameObject</summary>
public class OnNextTurn : StaticUnityEvent<OnNextTurn, GameObject> { }

/// <summary>Triggered upon a new combatant joining combat</summary>
public class OnCombatantJoin : StaticUnityEvent<OnCombatantJoin, GameObject> { }

/// <summary>Triggered upon a victory or defeat </summary>
public class OnCombatOutcome : StaticUnityEvent<OnCombatOutcome, bool> { }

/// <summary>UI Triggered Event for selecting an action </summary>
public class OnActionSelect : StaticUnityEvent<OnActionSelect, EntityAction> { }

/// <summary>UI Triggered upon action confirmation (no longer able to cancel action) </summary>
public class OnActionConfirm : StaticUnityEvent<OnActionConfirm> { }

/// <summary>UI Triggered upon action completion </summary>
public class OnActionComplete : StaticUnityEvent<OnActionComplete> { }
