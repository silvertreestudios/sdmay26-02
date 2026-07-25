using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Creature;
using Game.KayKit;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Rules.Unity.Strike
{
    /// <summary>
    /// Projects resolved Strike operations into feature-owned Unity animation, events, and logs.
    /// </summary>
    /// <remarks>
    /// The adapter intentionally contains the legacy combat-log singleton and static creature
    /// events. Every cosmetic callback is exception-contained so presentation cannot prevent
    /// authoritative damage, load-state changes, or MAP advancement.
    /// </remarks>
    public sealed class UnityStrikePresentationObserver
        : IResolvedOpObserver<ResolveStrikeOp, StrikeResolution>,
            IResolvedOpObserver<StrikeActionOp, StrikeOutcome>
    {
        private readonly IReadOnlyDictionary<CreatureId, ActionController> controllers;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly UnityStrikeContext strikeContext;

        /// <summary>Creates an observer over explicit encounter identity mappings.</summary>
        /// <param name="controllers">Rules-to-Unity attacker mappings.</param>
        /// <param name="creatures">Rules-to-Unity creature mappings.</param>
        /// <param name="strikeContext">The feature context owning item and weapon mappings.</param>
        public UnityStrikePresentationObserver(
            IReadOnlyDictionary<CreatureId, ActionController> controllers,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityStrikeContext strikeContext
        )
        {
            this.controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.strikeContext =
                strikeContext ?? throw new ArgumentNullException(nameof(strikeContext));
        }

        /// <inheritdoc/>
        public ValueTask OnOperationResolved(
            ResolveStrikeOp operation,
            StrikeResolution result,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                !TryGetPresentation(
                    operation.Actor,
                    operation.Target,
                    out GameObject attacker,
                    out GameObject target
                )
            )
                return default;

            StrikeItemDefinition item;
            try
            {
                item = strikeContext.GetStrikeItem(operation.Item);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, attacker);
                return default;
            }

            PresentSafely(
                () =>
                {
                    if (CombatLog.TryGetInstance(out CombatLogInterface log))
                        log.Log($"- {attacker.name} strikes {target.name} with {item.Label}.");
                },
                attacker
            );
            PresentSafely(() => PlayAttack(attacker, target, item), attacker);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnOperationResolved(
            StrikeActionOp operation,
            StrikeOutcome result,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                !TryGetPresentation(
                    operation.Actor,
                    operation.Target,
                    out GameObject attacker,
                    out GameObject target
                )
            )
                return default;

            PresentSafely(() => PublishOutcomeEvent(attacker, result.Resolution), attacker);
            PresentSafely(
                () =>
                {
                    if (CombatLog.TryGetInstance(out CombatLogInterface log))
                    {
                        log.LogEntry(
                            BuildCombatLogEntry(
                                attacker,
                                target,
                                strikeContext.GetStrikeItem(operation.Item),
                                result.Resolution
                            )
                        );
                    }
                },
                attacker
            );
            return default;
        }

        private bool TryGetPresentation(
            CreatureId actor,
            CreatureId target,
            out GameObject attackerObject,
            out GameObject targetObject
        )
        {
            if (
                controllers.TryGetValue(actor, out ActionController attacker)
                && attacker != null
                && creatures.TryGetValue(target, out CreatureComponent defender)
                && defender != null
            )
            {
                attackerObject = attacker.gameObject;
                targetObject = defender.gameObject;
                return true;
            }

            attackerObject = null;
            targetObject = null;
            return false;
        }

        private void PlayAttack(GameObject attacker, GameObject target, StrikeItemDefinition item)
        {
            CreaturePresentation presentation = attacker.GetComponent<CreaturePresentation>();
            if (strikeContext.TryGetWeapon(item.Item, out EquipmentWeapon weapon))
                presentation?.PlayAttack(weapon, target.transform.position);
            else
                presentation?.PlayAttack(AnimationStyle.Unarmed, target.transform.position);
        }

        private static void PublishOutcomeEvent(GameObject attacker, StrikeResolution resolution)
        {
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
        }

        private static CombatLogEntry BuildCombatLogEntry(
            GameObject attacker,
            GameObject target,
            StrikeItemDefinition item,
            StrikeResolution resolution
        )
        {
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
            return entry;
        }

        private static CombatLogDamage BuildDamage(StrikeResolution resolution)
        {
            CombatLogDamage damage = new CombatLogDamage { Total = resolution.FinalDamage };
            foreach (StrikeDamagePart part in resolution.Damage)
            {
                damage.Parts.Add(
                    new CombatLogDamagePart(part.DamageType.ToLowerInvariant(), part.Amount)
                );
            }
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

        private static void PresentSafely(Action presentation, UnityEngine.Object context)
        {
            try
            {
                presentation();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, context);
            }
        }
    }
}
