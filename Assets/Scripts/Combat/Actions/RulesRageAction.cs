using System;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

/// <summary>
/// Presents Rage in Unity while delegating eligibility, costs, effects, and health changes to the
/// rules runtime.
/// </summary>
public sealed class RulesRageAction : EntityAction
{
    /// <summary>Creates the one-action Rage action-bar entry.</summary>
    public RulesRageAction()
        : base(1) { }

    /// <inheritdoc/>
    public override string ActionName => "Rage";

    /// <inheritdoc/>
    public override bool IsAvailable(ActionController controller) =>
        controller != null
        && controller.TryGetCombatRules(out UnityCombatRulesBridge bridge, out CreatureId creature)
        && bridge.GetRageAvailability(creature) is AvailableActionAvailability;

    /// <inheritdoc/>
    public override void Invoke(GameObject target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        ActionController controller = target.GetComponent<ActionController>();
        try
        {
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId creature
                )
            )
            {
                Debug.LogWarning("Rage requires active combat rules authority.", target);
                return;
            }

            OpResult<RageStartOutcome> result = bridge.DispatchRage(creature);
            if (result is ResolvedOpResult<RageStartOutcome>)
            {
                CombatLog.GetInstance().Log("- " + target.name + " used Rage");
            }
            else if (result is InvalidOpResult<RageStartOutcome> invalid)
            {
                Debug.LogWarning($"Rage was rejected: {invalid.Reason}", target);
            }
            else
            {
                Debug.LogWarning("Rage did not complete.", target);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, target);
        }
        finally
        {
            if (controller != null)
                controller.IsTakingAction = false;
            OnActionComplete.Invoke();
            CombatManager.GetInstance().CheckForEndOfGame();
            OnGameplayStateCommitted.Invoke();
        }
    }
}
