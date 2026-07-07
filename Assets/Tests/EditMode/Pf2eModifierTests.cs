using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class Pf2eModifierTests
    {
        [Test]
        public void SameTypeBonusesAndPenaltiesDoNotStack()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2278
            Pf2eModifierResolution result = Pf2eModifierResolver.Resolve(new[]
            {
                new Pf2eModifier(1, Pf2eModifierType.Circumstance, "Lesser cover", Pf2eStatistic.ArmorClass),
                new Pf2eModifier(2, Pf2eModifierType.Circumstance, "Standard cover", Pf2eStatistic.ArmorClass),
                new Pf2eModifier(-1, Pf2eModifierType.Circumstance, "Minor opening", Pf2eStatistic.ArmorClass),
                new Pf2eModifier(-2, Pf2eModifierType.Circumstance, "Off-Guard", Pf2eStatistic.ArmorClass)
            }, Pf2eStatistic.ArmorClass);

            Assert.AreEqual(0, result.Total);
            AssertApplied(result, "Standard cover", "Off-Guard");
            AssertSuppressed(result, "Lesser cover", "Minor opening");
        }

        [Test]
        public void DifferentTypedAndUntypedModifiersStack()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2278
            Pf2eModifierResolution result = Pf2eModifierResolver.Resolve(new[]
            {
                new Pf2eModifier(1, Pf2eModifierType.Circumstance, "Cover", Pf2eStatistic.AttackRoll),
                new Pf2eModifier(2, Pf2eModifierType.Item, "Rune", Pf2eStatistic.AttackRoll),
                new Pf2eModifier(3, Pf2eModifierType.Status, "Bless", Pf2eStatistic.AttackRoll),
                new Pf2eModifier(4, Pf2eModifierType.Untyped, "Imported total", Pf2eStatistic.AttackRoll),
                new Pf2eModifier(-5, Pf2eModifierType.Untyped, "MAP", Pf2eStatistic.AttackRoll)
            }, Pf2eStatistic.AttackRoll);

            Assert.AreEqual(5, result.Total);
            Assert.AreEqual(5, result.AppliedModifiers.Count);
            Assert.AreEqual(0, result.SuppressedModifiers.Count);
        }

        [Test]
        public void ResolverFiltersByTargetStatistic()
        {
            Pf2eModifierResolution result = Pf2eModifierResolver.Resolve(new[]
            {
                new Pf2eModifier(7, Pf2eModifierType.Untyped, "Attack", Pf2eStatistic.AttackRoll),
                new Pf2eModifier(2, Pf2eModifierType.Circumstance, "Cover", Pf2eStatistic.ArmorClass)
            }, Pf2eStatistic.ArmorClass);

            Assert.AreEqual(2, result.Total);
            AssertApplied(result, "Cover");
            Assert.IsFalse(result.AppliedModifiers.Any(modifier => modifier.Source == "Attack"));
        }

        [Test]
        public void CoverAndOffGuardBothApplyAsCircumstanceAcModifiers()
        {
            // Cover AC bonus: https://2e.aonprd.com/Rules.aspx?ID=2372
            // Off-Guard AC penalty: https://2e.aonprd.com/Conditions.aspx?ID=58
            GameObject target = new GameObject("target");
            CreatureComponent creature = target.AddComponent<CreatureComponent>();
            target.AddComponent<Conditions>().Add("Off-Guard", null);
            creature.ac = 100;

            Pf2eModifierResolution result = creature.ResolveArmorClass(new[]
            {
                new Pf2eModifier(2, Pf2eModifierType.Circumstance, "Cover", Pf2eStatistic.ArmorClass)
            });

            Assert.AreEqual(100, result.Total);
            AssertApplied(result, "Armor Class", "Cover", "Off-Guard");
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void LowerCircumstanceAcBonusIsSuppressedByCover()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2278
            GameObject target = new GameObject("target");
            CreatureComponent creature = target.AddComponent<CreatureComponent>();
            creature.ac = 100;
            Pf2eModifierCollection modifiers = target.AddComponent<Pf2eModifierCollection>();
            modifiers.Add(new Pf2eModifier(1, Pf2eModifierType.Circumstance, "Lesser cover", Pf2eStatistic.ArmorClass));

            Pf2eModifierResolution result = creature.ResolveArmorClass(new[]
            {
                new Pf2eModifier(2, Pf2eModifierType.Circumstance, "Standard cover", Pf2eStatistic.ArmorClass)
            });

            Assert.AreEqual(102, result.Total);
            AssertApplied(result, "Armor Class", "Standard cover");
            AssertSuppressed(result, "Lesser cover");
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void EquippedArmorItemBonusSuppressesLowerItemAcBonus()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2278
            GameObject target = new GameObject("armored target");
            CreatureComponent creature = target.AddComponent<CreatureComponent>();
            creature.dexMod = 3;
            creature.armorBonuses = new List<ArmorBonus> { new ArmorBonus { category = "light", bonus = 4 } };
            creature.equippedArmor = new EquipmentArmor { name = "Leather Armor", category = "light", acBonus = 2, dexCap = 4 };

            Pf2eModifierResolution result = creature.ResolveArmorClass(new[]
            {
                new Pf2eModifier(1, Pf2eModifierType.Item, "Lesser item AC", Pf2eStatistic.ArmorClass)
            });

            Assert.AreEqual(19, result.Total);
            AssertApplied(result, "Base AC", "Dexterity modifier", "light armor proficiency", "Leather Armor");
            AssertSuppressed(result, "Lesser item AC");
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void SaveSkillInitiativeAndDcHelpersUseSharedResolver()
        {
            GameObject actor = new GameObject("actor");
            CreatureComponent creature = actor.AddComponent<CreatureComponent>();
            creature.fortitudeSave = 5;
            creature.allSaves = 1;
            creature.strMod = 4;
            creature.wisMod = 4;
            creature.initiative = 2;

            Assert.AreEqual(8, creature.ResolveFortitudeSave(new[]
            {
                new Pf2eModifier(2, Pf2eModifierType.Status, "Save spell", Pf2eStatistic.FortitudeSave),
                new Pf2eModifier(1, Pf2eModifierType.Status, "Lesser save spell", Pf2eStatistic.FortitudeSave)
            }).Total);
            Assert.AreEqual(5, creature.ResolveSkillCheck("athletics", new[]
            {
                new Pf2eModifier(1, Pf2eModifierType.Item, "Athletics item", Pf2eStatistic.SkillCheck)
            }).Total);
            Assert.AreEqual(5, creature.ResolveInitiative(new[]
            {
                new Pf2eModifier(1, Pf2eModifierType.Status, "Initiative status", Pf2eStatistic.Initiative)
            }).Total);
            Assert.AreEqual(23, creature.ResolveDifficultyClass(20, new[]
            {
                new Pf2eModifier(2, Pf2eModifierType.Circumstance, "DC circumstance", Pf2eStatistic.DifficultyClass),
                new Pf2eModifier(1, Pf2eModifierType.Item, "DC item", Pf2eStatistic.DifficultyClass)
            }).Total);

            UnityEngine.Object.DestroyImmediate(actor);
        }

        [Test]
        public void ImportedWeaponActionBonusRemainsUntypedBaseAttackTotal()
        {
            GameObject actor = new GameObject("actor");
            CreatureComponent creature = actor.AddComponent<CreatureComponent>();
            EquipmentWeapon shortbow = new EquipmentWeapon { name = "Shortbow" };
            creature.attackBonus = 3;
            creature.weaponActionBonuses = new List<WeaponActionBonus> { new WeaponActionBonus { weaponName = "Shortbow", bonus = 7 } };

            Pf2eModifierResolution result = creature.ResolveAttackRollForWeapon(shortbow, new[]
            {
                new Pf2eModifier(-5, Pf2eModifierType.Untyped, "Multiple attack penalty", Pf2eStatistic.AttackRoll)
            });

            Assert.AreEqual(2, result.Total);
            AssertApplied(result, "Attack modifier override", "Multiple attack penalty");
            UnityEngine.Object.DestroyImmediate(actor);
        }

        private static void AssertApplied(Pf2eModifierResolution result, params string[] sources)
        {
            foreach (string source in sources)
                Assert.IsTrue(result.AppliedModifiers.Any(modifier => modifier.Source == source), source + " should be applied.");
        }

        private static void AssertSuppressed(Pf2eModifierResolution result, params string[] sources)
        {
            foreach (string source in sources)
                Assert.IsTrue(result.SuppressedModifiers.Any(modifier => modifier.Source == source), source + " should be suppressed.");
        }
    }
}