using System;
using System.Collections.Generic;
using System.Linq;
using Game.KayKit;
using Game.Rules.Runtime;
using UnityEngine;
using CreatureComponent = Game.Creature.CreatureComponent;

namespace Game.Rules.Unity.Attack
{
    /// <summary>Contains presentation-only data shared by weapon and spell attack results.</summary>
    internal sealed class UnityAttackResult
    {
        public UnityAttackResult(
            RollResult roll,
            int attackModifier,
            int armorClass,
            DegreeOfSuccess degree,
            IEnumerable<UnityAttackDamagePart> damage,
            int finalDamage,
            int multipleAttackPenalty,
            int rangePenalty,
            int coverBonus
        )
        {
            Roll = roll ?? throw new ArgumentNullException(nameof(roll));
            AttackModifier = attackModifier;
            ArmorClass = armorClass;
            Degree = degree;
            Damage = (damage ?? throw new ArgumentNullException(nameof(damage))).ToArray();
            FinalDamage = finalDamage;
            MultipleAttackPenalty = multipleAttackPenalty;
            RangePenalty = rangePenalty;
            CoverBonus = coverBonus;
        }

        public RollResult Roll { get; }
        public int AttackModifier { get; }
        public int ArmorClass { get; }
        public DegreeOfSuccess Degree { get; }
        public IReadOnlyList<UnityAttackDamagePart> Damage { get; }
        public int FinalDamage { get; }
        public int MultipleAttackPenalty { get; }
        public int RangePenalty { get; }
        public int CoverBonus { get; }
        public bool Hit =>
            Degree == DegreeOfSuccess.Success || Degree == DegreeOfSuccess.CriticalSuccess;
    }

    internal readonly struct UnityAttackDamagePart
    {
        public UnityAttackDamagePart(string damageType, int amount)
        {
            DamageType = damageType;
            Amount = amount;
        }

        public string DamageType { get; }
        public int Amount { get; }
    }

    /// <summary>Projects shared hit/miss events and structured combat-log attack entries.</summary>
    internal static class UnityAttackResultPresentation
    {
        public static void Present(
            GameObject attacker,
            GameObject target,
            string action,
            UnityAttackResult result
        )
        {
            if (attacker == null || target == null || result == null)
                return;
            if (result.Hit)
            {
                string damageType =
                    result.Damage.Count == 0 ? "untyped" : result.Damage[0].DamageType;
                OnDamageDealt.Invoke(damageType);
            }
            else
            {
                OnAttackMiss.Invoke(attacker);
            }
            if (CombatLog.TryGetInstance(out CombatLogInterface log))
                log.LogEntry(BuildEntry(attacker, target, action, result));
        }

        private static CombatLogEntry BuildEntry(
            GameObject attacker,
            GameObject target,
            string action,
            UnityAttackResult result
        )
        {
            CombatLogEntry entry = new()
            {
                Kind = CombatLogEntryKind.Attack,
                Outcome = ToCombatLogOutcome(result.Degree),
                Actor = attacker.name,
                Target = target.name,
                Action = action,
                Roll = new CombatLogRoll
                {
                    NaturalRoll = result.Roll.Values[0],
                    TotalModifier = result.AttackModifier,
                    Total = result.Roll.Total + result.AttackModifier,
                    DifficultyClass = result.ArmorClass,
                },
                Damage = BuildDamage(result),
            };
            entry.Tags.Add("attack");
            entry.Details.Add(
                new CombatLogDetail(
                    "D20 Roll",
                    $"{entry.Roll.Total} ({entry.Roll.NaturalRoll} + {entry.Roll.TotalModifier})"
                )
            );
            entry.Details.Add(new CombatLogDetail("Target AC", result.ArmorClass.ToString()));
            entry.Details.Add(
                new CombatLogDetail(
                    "MAP",
                    result.MultipleAttackPenalty == 0
                        ? "none"
                        : result.MultipleAttackPenalty.ToString()
                )
            );
            entry.Details.Add(
                new CombatLogDetail(
                    "Range Penalty",
                    result.RangePenalty == 0 ? "none" : result.RangePenalty.ToString()
                )
            );
            entry.Details.Add(
                new CombatLogDetail(
                    "Cover",
                    result.CoverBonus == 0 ? "none" : $"+{result.CoverBonus} AC"
                )
            );
            entry.Details.Add(new CombatLogDetail("Result", result.Degree.ToString()));
            entry.Details.Add(new CombatLogDetail("Total Damage", $"{result.FinalDamage} damage"));
            return entry;
        }

        private static CombatLogDamage BuildDamage(UnityAttackResult result)
        {
            CombatLogDamage damage = new() { Total = result.FinalDamage };
            foreach (UnityAttackDamagePart part in result.Damage)
                damage.Parts.Add(
                    new CombatLogDamagePart(part.DamageType.ToLowerInvariant(), part.Amount)
                );
            return damage;
        }

        private static CombatLogOutcome ToCombatLogOutcome(DegreeOfSuccess degree) =>
            degree switch
            {
                DegreeOfSuccess.CriticalSuccess => CombatLogOutcome.CriticalSuccess,
                DegreeOfSuccess.Success => CombatLogOutcome.Success,
                DegreeOfSuccess.CriticalFailure => CombatLogOutcome.CriticalFailure,
                _ => CombatLogOutcome.Failure,
            };
    }
}
