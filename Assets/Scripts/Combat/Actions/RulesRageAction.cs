using System;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

/// <summary>
/// Presents Rage in Unity while delegating eligibility, costs, effects, and health changes to the
/// rules runtime.
/// </summary>
public sealed class RulesRageAction : EntityAction
{
    private readonly RageActionDefinition definition;

    /// <summary>Creates the one-action Rage action-bar entry.</summary>
    /// <param name="definition">The encounter-owned Rage rules definition.</param>
    public RulesRageAction(RageActionDefinition definition)
        : base(1) =>
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

    /// <inheritdoc/>
    public override string ActionName => "Rage";

    /// <inheritdoc/>
    public override bool IsAvailable(ActionController controller)
    {
        if (
            controller == null
            || !controller.TryGetCombatRules(
                out UnityCombatRulesBridge bridge,
                out CreatureId creature
            )
        )
        {
            return false;
        }

        return definition.GetAvailability(bridge.Snapshot, creature) is AvailableActionAvailability;
    }

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

            OpResult<RageStartOutcome> result = bridge.Dispatch(new RageActionOp(creature));
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
