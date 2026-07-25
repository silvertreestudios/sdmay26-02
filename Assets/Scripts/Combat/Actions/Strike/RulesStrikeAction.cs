using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.KayKit;
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
            return new StrikeActionDefinition().GetAvailability(bridge.Snapshot, actor, item)
                is AvailableActionAvailability;
        }

        /// <summary>Checks current Unity geometry for AI preview only.</summary>
        public bool CanPreviewTarget(RulesSnapshot snapshot, CreatureId actor, CreatureId target) =>
            strikeContext.Evaluate(snapshot, actor, item, target) is LegalStrikeTargetingOutcome;

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
                    OpResult<StrikeOutcome> result = bridge.Dispatch(
                        new StrikeActionOp(actor, item.Item, target)
                    );
                    if (result is ResolvedOpResult<StrikeOutcome> resolved)
                    {
                        PresentAttack(attacker, selection.Value.Target);
                        PresentResolvedOutcome(attacker, selection.Value.Target, resolved.Value);
                    }
                    else if (result is InvalidOpResult<StrikeOutcome> invalid)
                    {
                        Debug.LogWarning($"Strike was rejected: {invalid.Reason}", attacker);
                    }
                    else
                    {
                        Debug.LogWarning("Strike did not complete.", attacker);
                    }
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

        private void PresentAttack(GameObject attacker, GameObject target)
        {
            CombatLog
                .GetInstance()
                .Log($"- {attacker.name} strikes {target.name} with {item.Label}.");
            CreaturePresentation presentation = attacker.GetComponent<CreaturePresentation>();
            if (strikeContext.TryGetWeapon(item.Item, out EquipmentWeapon weapon))
                presentation?.PlayAttack(weapon, target.transform.position);
            else
                presentation?.PlayAttack(AnimationStyle.Unarmed, target.transform.position);
        }

        /// <summary>
        /// Projects a committed Strike outcome into combat events and structured logging.
        /// </summary>
        /// <param name="attacker">The Unity attacker presentation root.</param>
        /// <param name="target">The Unity target presentation root.</param>
        /// <param name="outcome">The already committed rules-owned outcome.</param>
        public void PresentResolvedOutcome(
            GameObject attacker,
            GameObject target,
            StrikeOutcome outcome
        )
        {
            StrikeResolution resolution = outcome.Resolution;
            if (resolution.Hit)
            {
                string damageType =
                    resolution.Damage.Count == 0 ? "untyped" : resolution.Damage[0].DamageType;
                OnDamageDealt.Invoke(damageType);
            }
            else
            {
                OnAttackMiss.Invoke(attacker);
            }

            CombatLogEntry entry = new CombatLogEntry
            {
                Kind = CombatLogEntryKind.Attack,
                Outcome = ToCombatLogOutcome(resolution.Degree),
                Actor = attacker.name,
                Target = target.name,
                Action = item.Label,
                Roll = new CombatLogRoll
                {
                    NaturalRoll = resolution.AttackRoll.Values[0],
                    TotalModifier = resolution.AttackModifier,
                    Total = resolution.AttackRoll.Total + resolution.AttackModifier,
                    DifficultyClass = resolution.ArmorClass,
                },
                Damage = BuildDamage(resolution),
            };
            entry.Tags.Add("attack");
            entry.Details.Add(
                new CombatLogDetail(
                    "D20 Roll",
                    $"{entry.Roll.Total} ({entry.Roll.NaturalRoll} + {entry.Roll.TotalModifier})"
                )
            );
            entry.Details.Add(new CombatLogDetail("Target AC", resolution.ArmorClass.ToString()));
            entry.Details.Add(
                new CombatLogDetail(
                    "MAP",
                    resolution.MultipleAttackPenalty == 0
                        ? "none"
                        : resolution.MultipleAttackPenalty.ToString()
                )
            );
            entry.Details.Add(
                new CombatLogDetail(
                    "Range Penalty",
                    resolution.RangePenalty == 0 ? "none" : resolution.RangePenalty.ToString()
                )
            );
            entry.Details.Add(
                new CombatLogDetail(
                    "Cover",
                    resolution.CoverBonus == 0 ? "none" : $"+{resolution.CoverBonus} AC"
                )
            );
            entry.Details.Add(new CombatLogDetail("Result", resolution.Degree.ToString()));
            entry.Details.Add(
                new CombatLogDetail("Total Damage", $"{resolution.FinalDamage} damage")
            );
            CombatLog.GetInstance().LogEntry(entry);
        }

        private static CombatLogDamage BuildDamage(StrikeResolution resolution)
        {
            CombatLogDamage damage = new CombatLogDamage { Total = resolution.FinalDamage };
            foreach (StrikeDamagePart part in resolution.Damage)
                damage.Parts.Add(
                    new CombatLogDamagePart(part.DamageType.ToLowerInvariant(), part.Amount)
                );
            return damage;
        }

        private static CombatLogOutcome ToCombatLogOutcome(
            Game.Rules.Runtime.DegreeOfSuccess degree
        ) =>
            degree switch
            {
                Game.Rules.Runtime.DegreeOfSuccess.CriticalSuccess =>
                    CombatLogOutcome.CriticalSuccess,
                Game.Rules.Runtime.DegreeOfSuccess.Success => CombatLogOutcome.Success,
                Game.Rules.Runtime.DegreeOfSuccess.CriticalFailure =>
                    CombatLogOutcome.CriticalFailure,
                _ => CombatLogOutcome.Failure,
            };
    }

    /// <summary>Presents feature-owned Reload through the generic rules dispatch boundary.</summary>
    public sealed class RulesReloadWeaponAction : EntityAction
    {
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
            return new ReloadActionDefinition().GetAvailability(bridge.Snapshot, actor, item)
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
                OpResult<ReloadOutcome> result = bridge.Dispatch(
                    new ReloadActionOp(actor, item.Item)
                );
                if (result is ResolvedOpResult<ReloadOutcome>)
                    CombatLog.GetInstance().Log($"- {target.name} reloads {item.Label}.");
                else if (result is InvalidOpResult<ReloadOutcome> invalid)
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
                CombatManager.GetInstance().CheckForEndOfGame();
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
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (strikeContext == null)
                throw new ArgumentNullException(nameof(strikeContext));

            foreach (EntityAction action in controller.GetActions())
            {
                if (action is RulesStrikeAction || action is RulesReloadWeaponAction)
                    controller.RemoveAction(action);
            }
            foreach (StrikeItemDefinition item in strikeContext.GetItems(actor))
            {
                controller.AddAction(new RulesStrikeAction(item, strikeContext));
                if (item.ReloadActions > 0)
                    controller.AddAction(new RulesReloadWeaponAction(item));
            }
        }
    }
}
