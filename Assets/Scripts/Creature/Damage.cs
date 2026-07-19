using System.Collections.Generic;
using UnityEngine;

namespace Game.Creature
{
    [System.Serializable]
    public struct DamageValue
    {
        public string DamageType;
        public int DamageAmount;

        public DamageValue(string damageType, int damageAmount)
        {
            DamageType = damageType;
            DamageAmount = damageAmount;
        }
    }

    public sealed class DamageRollResolution
    {
        public List<DamageValue> DamageValues { get; set; } = new();
        public CombatLogDamage Damage { get; set; } = new();
        public List<CombatLogDetail> Details { get; } = new();
        public int TotalDamage { get; set; }
    }

    public class DamageRoller
    {
        public static List<DamageValue> RollDamage(Dice dice)
        {
            return RollDamage(new List<Dice> { dice }, new List<DamageValue>());
        }

        public static List<DamageValue> RollDamage(Dice dice, DamageValue damageFlat)
        {
            return RollDamage(new List<Dice> { dice }, new List<DamageValue> { damageFlat });
        }

        public static List<DamageValue> RollDamage(
            List<Dice> damageRolls,
            List<DamageValue> damageFlats
        )
        {
            DamageRollResolution resolution = RollBaseDamage(damageRolls, damageFlats);
            string log =
                "  Damage: \n  Rolled "
                + DetailValue(resolution.Details, "Rolled")
                + "\n       "
                + DetailValue(resolution.Details, "Flat Damage");
            Debug.Log(log);
            CombatLog.GetInstance().Log(log);
            return resolution.DamageValues;
        }

        public static DamageRollResolution ResolveDamage(
            List<Dice> damageRolls,
            List<DamageValue> damageFlats,
            DegreeOfSuccess degree,
            List<DamageValue> weaknesses,
            List<DamageValue> resistances
        )
        {
            DamageRollResolution resolution = RollBaseDamage(damageRolls, damageFlats);
            ApplyCriticalDamage(degree, resolution.DamageValues, resolution.Details);
            ApplyWeaknessAndResistance(
                resolution.DamageValues,
                weaknesses,
                resistances,
                resolution.Details
            );
            resolution.TotalDamage = SumDamageWithoutLogging(resolution.DamageValues);
            resolution.Damage = BuildCombatLogDamage(
                resolution.DamageValues,
                resolution.TotalDamage
            );
            resolution.Details.Add(
                new CombatLogDetail("Total Damage", resolution.TotalDamage + " damage")
            );
            return resolution;
        }

        public static DamageRollResolution StartDamageResolution(
            List<Dice> damageRolls,
            List<DamageValue> damageFlats
        )
        {
            return RollBaseDamage(damageRolls, damageFlats);
        }

        public static void ApplyCriticalDamage(
            DamageRollResolution resolution,
            DegreeOfSuccess degree
        )
        {
            if (resolution == null)
                return;
            ApplyCriticalDamage(degree, resolution.DamageValues, resolution.Details);
        }

        public static void ApplyWeaknessAndResistance(
            DamageRollResolution resolution,
            List<DamageValue> weaknesses,
            List<DamageValue> resistances
        )
        {
            if (resolution == null)
                return;
            ApplyWeaknessAndResistance(
                resolution.DamageValues,
                weaknesses,
                resistances,
                resolution.Details
            );
        }

        public static void FinalizeDamageResolution(DamageRollResolution resolution)
        {
            if (resolution == null)
                return;
            resolution.TotalDamage = SumDamageWithoutLogging(resolution.DamageValues);
            resolution.Damage = BuildCombatLogDamage(
                resolution.DamageValues,
                resolution.TotalDamage
            );
            resolution.Details.Add(
                new CombatLogDetail("Total Damage", resolution.TotalDamage + " damage")
            );
        }

        public static List<DamageValue> AddOrMergeDamage(
            IEnumerable<DamageValue> damageValues,
            DamageValue damageValue
        )
        {
            List<DamageValue> merged =
                damageValues == null
                    ? new List<DamageValue>()
                    : new List<DamageValue>(damageValues);
            int index = merged.FindIndex(damage => damage.DamageType == damageValue.DamageType);
            if (index >= 0)
            {
                DamageValue existing = merged[index];
                existing.DamageAmount += damageValue.DamageAmount;
                merged[index] = existing;
            }
            else
            {
                merged.Add(damageValue);
            }
            return merged;
        }

        public static int SumDamage(List<DamageValue> damageValues)
        {
            int totalDamage = SumDamageWithoutLogging(damageValues);
            CombatLog.GetInstance().Log("  Total: " + totalDamage + " Damage!");
            return totalDamage;
        }

        public static void EvaluateCriticalDamage(
            DegreeOfSuccess attackRoll,
            List<DamageValue> damageValues
        )
        {
            List<CombatLogDetail> details = new();
            ApplyCriticalDamage(attackRoll, damageValues, details);
            if (attackRoll == DegreeOfSuccess.CriticalSuccess)
                CombatLog.GetInstance().Log("  x2 for Critical Hit!");
        }

        public static void ApplyWeaknessAndResistance(
            List<DamageValue> incoming,
            List<DamageValue> weaknesses,
            List<DamageValue> resistances
        )
        {
            List<CombatLogDetail> details = new();
            ApplyWeaknessAndResistance(incoming, weaknesses, resistances, details);
            foreach (CombatLogDetail detail in details)
            {
                if (detail.Label == "Weakness")
                    CombatLog.GetInstance().Log("  " + detail.Value + " for weakness!");
                else if (detail.Label == "Resistance")
                    CombatLog.GetInstance().Log("  " + detail.Value + " for resistance!");
            }
        }

        private static DamageRollResolution RollBaseDamage(
            List<Dice> damageRolls,
            List<DamageValue> damageFlats
        )
        {
            DamageRollResolution resolution = new();
            List<string> rollParts = new();
            List<string> flatParts = new();

            if (damageRolls != null)
            {
                foreach (Dice dice in damageRolls)
                {
                    DamageValue damageValue = new DamageValue(dice.damageType, dice.Roll());
                    rollParts.Add(
                        dice.numberOfDice
                            + "d"
                            + dice.sidesPerDie
                            + " = "
                            + damageValue.DamageAmount
                            + " "
                            + FormatDamageType(damageValue.DamageType)
                    );
                    resolution.DamageValues = AddOrMergeDamage(
                        resolution.DamageValues,
                        damageValue
                    );
                }
            }

            if (damageFlats != null)
            {
                foreach (DamageValue damageFlat in damageFlats)
                {
                    flatParts.Add(
                        FormatSigned(damageFlat.DamageAmount)
                            + " "
                            + FormatDamageType(damageFlat.DamageType)
                    );
                    resolution.DamageValues = AddOrMergeDamage(resolution.DamageValues, damageFlat);
                }
            }

            resolution.Details.Add(
                new CombatLogDetail(
                    "Rolled",
                    rollParts.Count == 0 ? "none" : string.Join(", ", rollParts)
                )
            );
            resolution.Details.Add(
                new CombatLogDetail(
                    "Flat Damage",
                    flatParts.Count == 0 ? "none" : string.Join(", ", flatParts)
                )
            );
            return resolution;
        }

        private static void ApplyCriticalDamage(
            DegreeOfSuccess attackRoll,
            List<DamageValue> damageValues,
            List<CombatLogDetail> details
        )
        {
            if (attackRoll != DegreeOfSuccess.CriticalSuccess)
                return;

            details.Add(
                new CombatLogDetail("Critical", "x2 damage before weakness and resistance")
            );
            for (int i = 0; i < damageValues.Count; i++)
            {
                var dv = damageValues[i];
                dv.DamageAmount *= 2;
                damageValues[i] = dv;
            }
        }

        private static void ApplyWeaknessAndResistance(
            List<DamageValue> incoming,
            List<DamageValue> weaknesses,
            List<DamageValue> resistances,
            List<CombatLogDetail> details
        )
        {
            weaknesses ??= new List<DamageValue>();
            resistances ??= new List<DamageValue>();
            for (int i = 0; i < incoming.Count; i++)
            {
                var inc = incoming[i];
                if (weaknesses.Exists(di => SameDamageType(di.DamageType, inc.DamageType)))
                {
                    var existingInstance = weaknesses.Find(di =>
                        SameDamageType(di.DamageType, inc.DamageType)
                    );
                    inc.DamageAmount += existingInstance.DamageAmount;
                    details.Add(
                        new CombatLogDetail(
                            "Weakness",
                            "+"
                                + existingInstance.DamageAmount
                                + " "
                                + FormatDamageType(existingInstance.DamageType)
                        )
                    );
                }
                if (resistances.Exists(di => SameDamageType(di.DamageType, inc.DamageType)))
                {
                    var existingInstance = resistances.Find(di =>
                        SameDamageType(di.DamageType, inc.DamageType)
                    );
                    inc.DamageAmount -= existingInstance.DamageAmount;
                    details.Add(
                        new CombatLogDetail(
                            "Resistance",
                            "-"
                                + existingInstance.DamageAmount
                                + " "
                                + FormatDamageType(existingInstance.DamageType)
                        )
                    );
                }
                if (inc.DamageAmount < 0)
                    inc.DamageAmount = 0;
                incoming[i] = inc;
            }
        }

        private static CombatLogDamage BuildCombatLogDamage(
            List<DamageValue> damageValues,
            int total
        )
        {
            CombatLogDamage damage = new() { Total = total };
            foreach (DamageValue damageValue in damageValues)
                damage.Parts.Add(
                    new CombatLogDamagePart(
                        damageValue.DamageType.ToLowerInvariant(),
                        damageValue.DamageAmount
                    )
                );
            return damage;
        }

        private static int SumDamageWithoutLogging(List<DamageValue> damageValues)
        {
            int totalDamage = 0;
            if (damageValues == null)
                return totalDamage;
            foreach (DamageValue dv in damageValues)
                totalDamage += dv.DamageAmount;
            return totalDamage;
        }

        private static string DetailValue(List<CombatLogDetail> details, string label)
        {
            CombatLogDetail detail = details.Find(item => item.Label == label);
            return detail == null ? "none" : detail.Value;
        }

        private static bool SameDamageType(string left, string right)
        {
            return string.Equals(left, right, System.StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatDamageType(string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType))
                return string.Empty;
            return char.ToUpper(damageType[0]) + damageType.Substring(1).ToLowerInvariant();
        }

        private static string FormatSigned(int value)
        {
            return value >= 0 ? "+" + value : value.ToString();
        }
    }
}
