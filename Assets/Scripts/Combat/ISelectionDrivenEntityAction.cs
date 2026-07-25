using Game.Rules.Runtime;
using UnityEngine;

/// <summary>
/// Marks an action-bar entry whose immutable operation is created from a typed selection workflow.
/// </summary>
public interface ISelectionDrivenEntityAction
{
    /// <summary>Invokes the action using the supplied player, AI, replay, or test resolver.</summary>
    /// <param name="target">The controller GameObject taking the action.</param>
    /// <param name="resolver">The boundary that supplies the action's typed selection.</param>
    void Invoke(GameObject target, ISelectionResolver resolver);
}
