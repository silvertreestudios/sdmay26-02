using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Strike;
using GridPublic;
using UnityEngine;

namespace Game.Strikes
{
    /// <summary>
    /// Presents one weapon or unarmed action-bar entry while the rules runtime owns all mechanical
    /// validation, costs, rolls, damage, ammunition, loaded state, and MAP.
    /// </summary>
    public sealed class RulesStrikeAction : MultiFrameEntityAction
    {
        private static readonly StrikeActionDefinition strikeActionDefinition = new();
        private readonly StrikeItemDefinition item;
        private readonly UnityStrikeContext strikeContext;

        /// <summary>Creates one rules-backed Strike entry.</summary>
        public RulesStrikeAction(StrikeItemDefinition item, UnityStrikeContext strikeContext)
            : base(1)
        {
            this.item = item ?? throw new ArgumentNullException(nameof(item));
            this.strikeContext =
                strikeContext ?? throw new ArgumentNullException(nameof(strikeContext));
        }

        /// <inheritdoc/>
        public override string ActionName => item.Label;

        /// <summary>Gets the selected rules item for AI and presentation adapters.</summary>
        public StrikeItemDefinition Item => item;

        /// <summary>Gets whether this entry is a ranged Strike.</summary>
        public bool IsRanged => item.IsRanged;

        /// <summary>Gets the entry's expected base damage for tactical comparison.</summary>
        public double AverageDamage => item.AverageDamage;

        /// <inheritdoc/>
        public override bool IsAvailable(ActionController controller)
        {
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
                return false;
            return strikeActionDefinition.GetAvailability(bridge.Snapshot, actor, item)
                is AvailableActionAvailability;
        }

        /// <summary>
        /// Checks the full side-effect-free Strike validation path used by authoritative dispatch.
        /// </summary>
        public bool CanPreviewTarget(RulesSnapshot snapshot, CreatureId actor, CreatureId target) =>
            strikeActionDefinition.Validate(
                snapshot,
                new StrikeActionOp(actor, item.Item, target),
                strikeContext,
                strikeContext,
                strikeContext
            ) is ActionValidationResult.ValidActionValidationResult;

        /// <summary>Creates the existing grid selection request used by player and AI presentation.</summary>
        public StrikeTargetRequest GetTargetRequest() =>
            new StrikeTargetRequest
            {
                ReachFeet = item.ReachFeet,
                RangeIncrementFeet = item.RangeIncrementFeet,
                IsRanged = item.IsRanged,
                RequiresLineOfEffect = true,
            };

        /// <inheritdoc/>
        protected override IEnumerator MFInvoke(GameObject attacker)
        {
            ActionController controller = attacker.GetComponent<ActionController>();
            try
            {
                if (
                    controller == null
                    || !controller.TryGetCombatRules(
                        out UnityCombatRulesBridge bridge,
                        out CreatureId actor
                    )
                )
                {
                    Debug.LogWarning("Strike requires active combat rules authority.", attacker);
                    yield break;
                }

                CoroutineResult<StrikeTargetResult> selection = new();
                yield return GridAPI
                    .GetInstance()
                    .GetStrikeTarget(attacker, GetTargetRequest(), selection);
                if (selection.Value?.Target == null)
                    yield break;

                CreatureComponent targetCreature =
                    selection.Value.Target.GetComponent<CreatureComponent>();
                if (targetCreature == null)
                    yield break;
                try
                {
                    CreatureId target = bridge.GetCreatureId(targetCreature);
                    StrikeActionOp operation = new StrikeActionOp(actor, item.Item, target);
                    OpResult<StrikeResolution> result = bridge.Dispatch(operation);
                    if (result is InvalidOpResult<StrikeResolution> invalid)
                        Debug.LogWarning($"Strike was rejected: {invalid.Reason}", attacker);
                    else if (result is InterruptedOpResult<StrikeResolution>)
                        Debug.LogWarning("Strike was interrupted.", attacker);
                    else if (result is CancelledOpResult<StrikeResolution>)
                        Debug.LogWarning("Strike was cancelled.", attacker);
                    else if (result is not ResolvedOpResult<StrikeResolution>)
                        Debug.LogWarning("Strike returned an unknown structural result.", attacker);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, attacker);
                }
            }
            finally
            {
                if (controller != null)
                    controller.IsTakingAction = false;
                OnActionComplete.Invoke();
            }
        }
    }

    /// <summary>Presents feature-owned Reload through the generic rules dispatch boundary.</summary>
    public sealed class RulesReloadWeaponAction : EntityAction
    {
        private static readonly ReloadActionDefinition reloadActionDefinition = new();
        private readonly StrikeItemDefinition item;

        /// <summary>Creates a Reload entry for a reload-required Strike item.</summary>
        public RulesReloadWeaponAction(StrikeItemDefinition item)
            : base(
                checked((uint)(item ?? throw new ArgumentNullException(nameof(item))).ReloadActions)
            )
        {
            this.item = item;
        }

        /// <inheritdoc/>
        public override string ActionName => $"Reload {item.Label}";

        /// <inheritdoc/>
        public override bool IsAvailable(ActionController controller)
        {
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
            )
                return false;
            return reloadActionDefinition.GetAvailability(bridge.Snapshot, actor, item)
                is AvailableActionAvailability;
        }

        /// <inheritdoc/>
        public override void Invoke(GameObject target)
        {
            ActionController controller = target.GetComponent<ActionController>();
            try
            {
                if (
                    controller == null
                    || !controller.TryGetCombatRules(
                        out UnityCombatRulesBridge bridge,
                        out CreatureId actor
                    )
                )
                    return;
                OpResult<EquipmentState> result = bridge.Dispatch(
                    new ReloadActionOp(actor, item.Item)
                );
                if (
                    result is ResolvedOpResult<EquipmentState>
                    && CombatLog.TryGetInstance(out CombatLogInterface log)
                )
                    log.Log($"- {target.name} reloads {item.Label}.");
                else if (result is InvalidOpResult<EquipmentState> invalid)
                    Debug.LogWarning($"Reload was rejected: {invalid.Reason}", target);
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
                if (CombatManagerInterface.TryGetInstance(out CombatManagerInterface combatManager))
                    combatManager.CheckForEndOfGame();
                OnGameplayStateCommitted.Invoke();
            }
        }
    }

    /// <summary>Installs rules-backed Strike and Reload entries at encounter composition.</summary>
    public static class UnityStrikeActionInstaller
    {
        /// <summary>Replaces legacy entries with one action per registered Strike item.</summary>
        public static void Install(
            ActionController controller,
            CreatureId actor,
            UnityStrikeContext strikeContext
        )
        {
            Prepare(controller, actor, strikeContext).Apply();
        }

        /// <summary>Prepares every fallible action-list read and Strike action construction.</summary>
        internal static UnityStrikeActionInstallationPlan Prepare(
            ActionController controller,
            CreatureId actor,
            UnityStrikeContext strikeContext
        )
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (strikeContext == null)
                throw new ArgumentNullException(nameof(strikeContext));

            List<EntityAction> removals = new();
            foreach (EntityAction action in controller.GetActions())
            {
                if (action is RulesStrikeAction || action is RulesReloadWeaponAction)
                    removals.Add(action);
            }
            List<EntityAction> additions = new();
            foreach (StrikeItemDefinition item in strikeContext.GetItems(actor))
            {
                additions.Add(new RulesStrikeAction(item, strikeContext));
                if (item.ReloadActions > 0)
                    additions.Add(new RulesReloadWeaponAction(item));
            }
            return new UnityStrikeActionInstallationPlan(controller, removals, additions);
        }
    }

    /// <summary>Applies a fully prepared Strike action-list reconciliation without recomputing it.</summary>
    internal sealed class UnityStrikeActionInstallationPlan
    {
        private readonly ActionController controller;
        private readonly IReadOnlyList<EntityAction> removals;
        private readonly IReadOnlyList<EntityAction> additions;

        internal UnityStrikeActionInstallationPlan(
            ActionController controller,
            IReadOnlyList<EntityAction> removals,
            IReadOnlyList<EntityAction> additions
        )
        {
            this.controller = controller;
            this.removals = removals;
            this.additions = additions;
        }

        internal void Apply()
        {
            foreach (EntityAction action in removals)
                controller.RemoveAction(action);
            foreach (EntityAction action in additions)
                controller.AddAction(action);
        }
    }
}
