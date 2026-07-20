using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class CombatLogEntryFormatterTests
    {
        [Test]
        public void AttackSummaryIsOutcomeFirstAndCompact()
        {
            CombatLogEntry entry = new CombatLogEntry
            {
                Kind = CombatLogEntryKind.Attack,
                Outcome = CombatLogOutcome.CriticalSuccess,
                Actor = "Lena",
                Target = "Zombie",
                Action = "Longsword",
                Roll = new CombatLogRoll { Total = 23, DifficultyClass = 18 },
                Damage = new CombatLogDamage { Total = 18 },
            };
            entry.Damage.Parts.Add(new CombatLogDamagePart("slashing", 18));

            Assert.AreEqual(
                "Lena -> Zombie | Longsword | 23 vs AC 18 | Critical Hit | 18 slashing",
                CombatLogEntryFormatter.ToSummary(entry)
            );
        }

        [Test]
        public void PlainTextIncludesExpandedAttackDetails()
        {
            CombatLogEntry entry = new CombatLogEntry
            {
                Kind = CombatLogEntryKind.Attack,
                Outcome = CombatLogOutcome.Success,
                Actor = "Archer",
                Target = "Kobold",
                Action = "Shortbow",
                Roll = new CombatLogRoll { Total = 17, DifficultyClass = 16 },
            };
            entry.Details.Add(new CombatLogDetail("MAP", "-5"));
            entry.Details.Add(new CombatLogDetail("Cover", "+2 AC"));

            string text = CombatLogEntryFormatter.ToPlainText(entry);

            StringAssert.Contains(
                "Archer -> Kobold | Shortbow | 17 vs AC 16 | Hit | No damage",
                text
            );
            StringAssert.Contains("MAP: -5", text);
            StringAssert.Contains("Cover: +2 AC", text);
        }

        [Test]
        public void TagsArePreservedOnMessageEntries()
        {
            CombatLogEntry entry = CombatLogEntry.FromMessage("hidden", new[] { "Dev", "Rules" });

            CollectionAssert.AreEquivalent(new[] { "dev", "rules" }, entry.Tags.ToArray());
        }

        [Test]
        public void PlainTextFallbackUsesMessage()
        {
            CombatLogEntry entry = CombatLogEntry.FromMessage("UI System Test Log");

            Assert.AreEqual("UI System Test Log", CombatLogEntryFormatter.ToPlainText(entry));
        }
    }

    public class DamageRollerStructuredResolutionTests
    {
        [Test]
        public void StructuredDamageResolutionIncludesCritWeaknessResistanceAndTotal()
        {
            Random.State state = Random.state;
            Random.InitState(1);

            DamageRollResolution resolution = DamageRoller.ResolveDamage(
                new List<Dice> { new Dice(1, 1, "slashing") },
                new List<DamageValue> { new DamageValue("slashing", 2) },
                DegreeOfSuccess.CriticalSuccess,
                new List<DamageValue> { new DamageValue("slashing", 3) },
                new List<DamageValue> { new DamageValue("slashing", 1) }
            );

            Assert.AreEqual(8, resolution.TotalDamage);
            Assert.AreEqual(8, resolution.Damage.Total);
            Assert.AreEqual("slashing", resolution.Damage.Parts[0].DamageType);
            Assert.IsTrue(resolution.Details.Any(detail => detail.Label == "Critical"));
            Assert.IsTrue(
                resolution.Details.Any(detail =>
                    detail.Label == "Weakness" && detail.Value.Contains("+3")
                )
            );
            Assert.IsTrue(
                resolution.Details.Any(detail =>
                    detail.Label == "Resistance" && detail.Value.Contains("-1")
                )
            );

            Random.state = state;
        }
    }
}
