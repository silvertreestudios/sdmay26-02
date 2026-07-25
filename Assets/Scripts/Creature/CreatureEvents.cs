using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Triggered upon reseting action points</summary>
public class OnResetActionPoints : UnityEvent<Ref<uint>> { }

/// <summary>Triggered upon actions retrieval</summary>
public class OnGetActions : UnityEvent<List<EntityAction>> { }

/// <summary>Triggered upon reactions retrieval</summary>
public class OnGetReactions : UnityEvent<List<EntityAction>> { }

//===========================
// Static Events
//===========================

/// <summary>Triggered upon a creature taking a step, returns the position of the step taken</summary>
//could use the position to have spatial audio
public class OnStepEnd : StaticUnityEvent<OnStepEnd, Vector3> { }

/// <summary>Triggered upon a creature dealing damage, passes the attacker GameObject as a parameter</summary>
public class OnDamageDealt : StaticUnityEvent<OnDamageDealt, string> { }

/// <summary>Triggered upon a creature missing an attack, passes the attacker GameObject as a parameter</summary>
public class OnAttackMiss : StaticUnityEvent<OnAttackMiss, GameObject> { }

/// <summary>Triggered upon a creature's death, returns the GameObject that died</summary>
public class OnDeath : StaticUnityEvent<OnDeath, GameObject> { }
