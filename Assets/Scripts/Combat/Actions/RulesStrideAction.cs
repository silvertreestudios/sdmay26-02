using System;
using System.Collections;
using System.Threading.Tasks;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using GridPublic;
using UnityEngine;

/// <summary>Runs Stride through its typed rules workflow and committed-Fact projection.</summary>
public sealed class RulesStrideAction : EntityAction, ISelectionDrivenEntityAction
{
    /// <summary>Creates the one-action Stride action-bar entry.</summary>
    public RulesStrideAction()
        : base(1) { }

    /// <inheritdoc/>
    public override string ActionName => "Stride";

    /// <inheritdoc/>
    public override EntityActionPresentation Presentation => EntityActionPresentation.Movement;

    /// <inheritdoc/>
    public override bool IsExplorationAction => true;

    /// <inheritdoc/>
    public override bool IsAvailable(ActionController controller)
    {
        if (controller == null)
            return false;
        if (controller.IsInDungeonExploration)
        {
            return GridAPI.TryGetInstance(out GridAPI gridApi)
                && gridApi is GridBase grid
                && grid.ExplorationStrideCoordinator.Handles(controller.gameObject);
        }
        return controller.TryGetCombatRules(
                out UnityCombatRulesBridge bridge,
                out CreatureId creature
            )
            && bridge.GetStrideAvailability(creature) is AvailableActionAvailability;
    }

    /// <inheritdoc/>
    public override void Invoke(GameObject target)
    {
        if (!GridAPI.TryGetInstance(out GridAPI grid))
        {
            FinishWithoutDispatch(target, "Stride requires an initialized grid.");
            return;
        }
        Invoke(target, new PlayerStrideSelectionResolver(target, grid));
    }

    /// <inheritdoc/>
    public void Invoke(GameObject target, ISelectionResolver resolver)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));
        CoroutineRunner.Run(Run(target, resolver));
    }

    private IEnumerator Run(GameObject target, ISelectionResolver resolver)
    {
        ActionController controller = target.GetComponent<ActionController>();
        GridBase grid = GridAPI.GetInstance() as GridBase;
        if (controller == null || grid == null)
        {
            FinishWithoutDispatch(target, "Stride requires a controller and initialized grid.");
            yield break;
        }

        bool startedInExploration = controller.IsInDungeonExploration;
        UnityCombatRulesBridge bridge;
        CreatureId creature;
        if (startedInExploration)
        {
            bridge = UnityCombatRulesBridge.CreateExplorationStride(
                controller,
                grid.GetTiles(),
                grid.ExplorationStrideCoordinator
            );
            creature = bridge.GetCreatureId(controller);
        }
        else if (!controller.TryGetCombatRules(out bridge, out creature))
        {
            FinishWithoutDispatch(target, "Stride requires active combat rules authority.");
            yield break;
        }

        CreaturePresentation presentation = target.GetComponent<CreaturePresentation>();
        float movementSpeed = target.GetComponent<CreatureComponent>()?.speed ?? 25.0f;
        try
        {
            SelectionWorkflow<MovementPath> workflow = bridge.CreateStrideSelectionWorkflow(
                creature
            );
            ValueTask<SelectionOutcome<MovementPath>> pendingSelection = workflow.Run(resolver);
            while (!pendingSelection.IsCompleted)
                yield return null;

            SelectionOutcome<MovementPath> selection;
            try
            {
                selection = pendingSelection.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, target);
                yield break;
            }

            if (selection is InvalidSelectionOutcome<MovementPath> invalid)
            {
                Debug.LogWarning($"Stride selection failed: {invalid.Reason}", target);
                yield break;
            }
            if (selection is not CompletedSelectionOutcome<MovementPath> completed)
                yield break;

            CombatLog.GetInstance().Log("- " + target.name + " used Stride");
            presentation?.SetMoving(true, movementSpeed);
            UnityStrideProjectionObserver projection = new UnityStrideProjectionObserver(
                target,
                creature,
                grid,
                startedInExploration
            );
            ValueTask<bool> pendingDispatch = bridge.DispatchProjectedStride(
                creature,
                completed.Selection,
                projection
            );
            while (!pendingDispatch.IsCompleted)
                yield return null;

            bool mechanicsResolved = false;
            try
            {
                mechanicsResolved = pendingDispatch.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, target);
            }

            bool presentationSucceeded = true;
            ValueTask pendingPresentation = UnityCoroutineTask.Run(projection.DrainPresentation());
            while (!pendingPresentation.IsCompleted)
                yield return null;
            try
            {
                pendingPresentation.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, target);
                presentationSucceeded = false;
            }

            if (resolver is IProjectedStrideContinuationReceiver receiver)
            {
                receiver.RecordMayContinueRoute(
                    mechanicsResolved && presentationSucceeded && !projection.WasRouteInterrupted
                );
            }
            if (!mechanicsResolved)
                Debug.LogWarning("Stride was rejected by current rules state.", target);
        }
        finally
        {
            presentation?.SetMoving(false, 0.0f);
            controller.IsTakingAction = false;
            OnActionComplete.Invoke();
            CombatManager.GetInstance().CheckForEndOfGame();
            OnGameplayStateCommitted.Invoke();
        }
    }

    private static void FinishWithoutDispatch(GameObject target, string reason)
    {
        Debug.LogWarning(reason, target);
        ActionController controller =
            target == null ? null : target.GetComponent<ActionController>();
        if (controller != null)
            controller.IsTakingAction = false;
        OnActionComplete.Invoke();
    }
}
