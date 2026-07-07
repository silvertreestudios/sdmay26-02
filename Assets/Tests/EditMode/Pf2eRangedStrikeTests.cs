using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.AbilityActions;
using Game.Creature;
using Game.Strikes;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class Pf2eRangedStrikeTests
    {
        [Test]
        public void NaturalTwentyAndOneAdjustDegreeByOneStep()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2287
            D20Result naturalTwenty = D20.Evaluate(20, -20, 25);
            D20Result naturalOne = D20.Evaluate(1, 30, 20);

            Assert.AreEqual(DegreeOfSuccess.Fail, naturalTwenty.degree);
            Assert.AreEqual(DegreeOfSuccess.Success, naturalOne.degree);
        }

        [Test]
        public void D20DegreeBoundariesUseDcAndTenOverUnder()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2286
            Assert.AreEqual(DegreeOfSuccess.CriticalFail, D20.Evaluate(1, 8, 20).degree);
            Assert.AreEqual(DegreeOfSuccess.Fail, D20.Evaluate(10, 9, 20).degree);
            Assert.AreEqual(DegreeOfSuccess.Success, D20.Evaluate(10, 10, 20).degree);
            Assert.AreEqual(DegreeOfSuccess.CriticalSuccess, D20.Evaluate(20, 10, 20).degree);
        }

        [Test]
        public void GridDistanceUsesAlternatingDiagonalCosts()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2357
            Assert.AreEqual(5, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(1, 0, 1)));
            Assert.AreEqual(15, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(2, 0, 2)));
            Assert.AreEqual(30, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(4, 0, 4)));
        }

        [Test]
        public void RangedIncrementPenaltyCapsAtSixIncrements()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2288
            Assert.AreEqual(0, StrikeTargeting.CalculateRangePenalty(60, 60));
            Assert.AreEqual(-2, StrikeTargeting.CalculateRangePenalty(65, 60));
            Assert.AreEqual(-10, StrikeTargeting.CalculateRangePenalty(360, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => StrikeTargeting.CalculateRangePenalty(365, 60));
        }

        [Test]
        public void EvaluateEnforcesSixRangedIncrements()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2288
            Tile[,] tiles = BuildTiles(75, 1);
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(72, 0, 0);

            StrikeTargetResult legal = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            });

            Assert.IsNotNull(legal);
            Assert.AreEqual(360, legal.DistanceFeet);
            Assert.AreEqual(-10, legal.RangePenalty);

            target.transform.position = new Vector3(73, 0, 0);
            Assert.IsNull(StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            }));

            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void SolidWallBlocksLineOfEffect()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2372
            Tile[,] tiles = BuildTiles(5, 1);
            tiles[2, 0] = null;
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(4, 0, 0);

            StrikeTargetResult result = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            });

            Assert.IsNull(result);
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void PartialWallGivesStandardCover()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2372
            Tile[,] tiles = BuildTiles(3, 2);
            tiles[1, 0] = null;
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(2, 0, 1);

            StrikeTargetResult result = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            });

            Assert.IsNotNull(result);
            Assert.AreEqual(StrikeCover.Standard, result.Cover);
            Assert.AreEqual(2, result.CoverAcBonus);
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void ClearLineHasNoCover()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2372
            Tile[,] tiles = BuildTiles(5, 1);
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(4, 0, 0);

            StrikeTargetResult result = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = true,
                RangeIncrementFeet = 60
            });

            Assert.IsNotNull(result);
            Assert.AreEqual(StrikeCover.None, result.Cover);
            Assert.AreEqual(0, result.CoverAcBonus);
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void BlockedCornerPreventsLineOfEffect()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2372
            Tile[,] tiles = BuildTiles(2, 2);
            tiles[0, 1] = null;
            tiles[1, 0] = null;
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.transform.position = new Vector3(0, 0, 0);
            target.transform.position = new Vector3(1, 0, 1);

            StrikeTargetResult result = StrikeTargeting.Evaluate(attacker, target, tiles, new StrikeTargetRequest
            {
                IsRanged = false,
                ReachFeet = 5
            });

            Assert.IsNull(result);
            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void MultipleAttackPenaltyUsesAgileValues()
        {
            // PF2e source: https://2e.aonprd.com/Rules.aspx?ID=2288
            MethodInfo method = typeof(Strike).GetMethod("CalculateMultipleAttackPenalty", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            Strike normal = new Strike(new List<Dice> { new Dice(1, 6, "slashing") }, new List<DamageValue>());
            Strike agile = new Strike(new List<Dice> { new Dice(1, 6, "piercing") }, new List<DamageValue>())
            {
                Traits = new List<string> { "agile" }
            };

            Assert.AreEqual(0, (int)method.Invoke(normal, new object[] { 0u }));
            Assert.AreEqual(5, (int)method.Invoke(normal, new object[] { 1u }));
            Assert.AreEqual(10, (int)method.Invoke(normal, new object[] { 2u }));
            Assert.AreEqual(4, (int)method.Invoke(agile, new object[] { 1u }));
            Assert.AreEqual(8, (int)method.Invoke(agile, new object[] { 2u }));
        }

        [Test]
        public void StrikeAttackLogIncludesMapRangePenaltyAndCoverAc()
        {
            // PF2e sources:
            // Attack rolls, MAP, and range penalty: https://2e.aonprd.com/Rules.aspx?ID=2288
            // Cover AC bonus: https://2e.aonprd.com/Rules.aspx?ID=2372
            GameObject logObject = new GameObject("test-combat-log");
            TestCombatLog log = InstallTestCombatLog(logObject);
            GameObject attacker = new GameObject("attacker");
            GameObject target = new GameObject("target");
            attacker.AddComponent<TestActionController>().StrikePenalty = 1;
            CreatureComponent attackerCreature = attacker.AddComponent<CreatureComponent>();
            CreatureComponent targetCreature = target.AddComponent<CreatureComponent>();
            attackerCreature.attackBonus = 7;
            targetCreature.ac = 100;
            targetCreature.hp = 100;

            Strike strike = new Strike(new List<Dice> { new Dice(1, 6, "piercing") }, new List<DamageValue>());
            strike.Damage(attacker, target, new StrikeTargetResult
            {
                Target = target,
                LineOfEffect = StrikeLineOfEffect.Clear,
                Cover = StrikeCover.Standard,
                RangePenalty = -2
            });

            string attackLog = log.Messages.FirstOrDefault(message => message.StartsWith("Attack:", StringComparison.Ordinal));
            Assert.IsNotNull(attackLog);
            StringAssert.Contains("AC: 102 (100 + 2 cover)", attackLog);
            StringAssert.Contains("+ 7 - 5 -2", attackLog);

            UnityEngine.Object.DestroyImmediate(attacker);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(logObject);
        }

        [Test]
        public void DeadlyTraitAddsExtraDieOnlyOnCritical()
        {
            // PF2e sources:
            // Strike critical damage: https://2e.aonprd.com/Rules.aspx?ID=2343
            // Deadly trait: https://2e.aonprd.com/Traits.aspx?ID=570
            GameObject logObject = new GameObject("test-combat-log");
            InstallTestCombatLog(logObject);
            MethodInfo method = typeof(Strike).GetMethod("ApplyDeadlyDamage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            Strike strike = new Strike(new List<Dice> { new Dice(1, 6, "piercing") }, new List<DamageValue>())
            {
                Traits = new List<string> { "deadly-d10" }
            };
            List<DamageValue> normalDamage = new() { new DamageValue("piercing", 6) };
            List<DamageValue> criticalDamage = new() { new DamageValue("piercing", 12) };
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(76);

            method.Invoke(strike, new object[] { DegreeOfSuccess.Success, normalDamage });
            method.Invoke(strike, new object[] { DegreeOfSuccess.CriticalSuccess, criticalDamage });

            UnityEngine.Random.state = randomState;
            Assert.AreEqual(6, normalDamage[0].DamageAmount);
            Assert.Greater(criticalDamage[0].DamageAmount, 12);
            Assert.LessOrEqual(criticalDamage[0].DamageAmount, 22);
            UnityEngine.Object.DestroyImmediate(logObject);
        }

        [Test]
        public void AmmoAndReloadStateAreEnforced()
        {
            GameObject creature = new GameObject("archer");
            CreatureComponent component = creature.AddComponent<CreatureComponent>();
            EquipmentWeapon sling = new EquipmentWeapon { name = "Sling", range = 50, reload = "1", ammo = "sling-bullets", damage = new Dice(1, 6, "bludgeoning") };

            component.SetAmmoQuantity("Sling Bullets", 2);

            Assert.IsTrue(component.HasAmmoFor(sling));
            Assert.IsTrue(component.IsWeaponLoaded(sling));
            Assert.IsTrue(component.ConsumeAmmoFor(sling));
            Assert.AreEqual(1, component.GetAmmoQuantity("sling-bullets"));

            component.MarkWeaponFired(sling);
            Assert.IsFalse(component.IsWeaponLoaded(sling));
            Assert.IsTrue(component.ReloadWeapon(sling));
            Assert.IsTrue(component.IsWeaponLoaded(sling));
            UnityEngine.Object.DestroyImmediate(creature);
        }

        [Test]
        public void CreatureJsonImportsRangedWeaponBonusAmmoAndReload()
        {
            GameObject goblin = CreatureJsonConverter.CreateFromFile("DataFiles/pathfinder-monster-core/goblin-warrior");
            Assert.IsNotNull(goblin);
            CreatureComponent component = goblin.GetComponent<CreatureComponent>();
            EquipmentWeapon shortbow = component.weapons.FirstOrDefault(weapon => weapon.name == "Shortbow");

            Assert.IsNotNull(shortbow);
            Assert.AreEqual(60, shortbow.range);
            Assert.AreEqual("0", shortbow.reload);
            Assert.AreEqual("arrows", shortbow.ammo);
            Assert.AreEqual(10, component.GetAmmoQuantity("arrows"));
            Assert.AreEqual(7, component.GetAttackBonusForWeapon(shortbow));

            UnityEngine.Object.DestroyImmediate(goblin);
        }

        [Test]
        public void WeaponStrikeAdderAutomaticAddsRangedAndReloadActionsWithoutDuplicates()
        {
            GameObject kobold = CreatureJsonConverter.CreateFromFile("DataFiles/pathfinder-monster-core/kobold-warrior");
            Assert.IsNotNull(kobold);
            kobold.AddComponent<TestActionController>();

            StrikeWeapon.WeaponStrikeAdderAutomatic(kobold);
            StrikeWeapon.WeaponStrikeAdderAutomatic(kobold);

            List<EntityAction> actions = kobold.GetComponent<ActionController>().GetActions();
            Assert.AreEqual(1, actions.OfType<StrikeWeapon>().Count(action => action.ActionName == "Sling"));
            Assert.AreEqual(1, actions.Count(action => action.ActionName == "Reload Sling"));
            Assert.AreEqual(20, kobold.GetComponent<CreatureComponent>().GetAmmoQuantity("sling-bullets"));

            UnityEngine.Object.DestroyImmediate(kobold);
        }

        [Test]
        public void ProjectileRangedWeaponDoesNotAddMeleeFlatDamage()
        {
            GameObject creature = new GameObject("archer");
            CreatureComponent component = creature.AddComponent<CreatureComponent>();
            component.damageBonus = 4;
            EquipmentWeapon shortbow = new EquipmentWeapon
            {
                name = "Shortbow",
                range = 60,
                reload = "0",
                ammo = "arrows",
                damage = new Dice(1, 6, "piercing"),
                traits = new System.Collections.Generic.List<string> { "deadly-d10" }
            };

            StrikeWeapon action = new StrikeWeapon(1, shortbow, creature);

            Assert.AreEqual(0, action.GetStrike().FlatDamages.Count);
            Assert.AreEqual(3.5f, action.GetStrike().GetAvgDmg());
            UnityEngine.Object.DestroyImmediate(creature);
        }

        [Test]
        public void RageDamageIgnoresProjectileStrikeWithoutMeleeFlatDamage()
        {
            // PF2e source for Rage applying additional damage only to melee Strikes: https://2e.aonprd.com/Actions.aspx?ID=2802
            Strike projectileStrike = new Strike(new List<Dice> { new Dice(1, 6, "piercing") }, new List<DamageValue>());
            Rage rage = new Rage(1);

            Assert.DoesNotThrow(() => rage.AddRageDamage(projectileStrike));
            Assert.AreEqual(0, projectileStrike.FlatDamages.Count);
        }

        private static Tile[,] BuildTiles(int width, int height)
        {
            Tile[,] tiles = new Tile[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                    tiles[x, z] = new Tile();
            }
            return tiles;
        }

        private static TestCombatLog InstallTestCombatLog(GameObject logObject)
        {
            TestCombatLog log = logObject.AddComponent<TestCombatLog>();
            FieldInfo field = typeof(SingletonMonoBehaviour<CombatLogInterface>).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(null, log);
            return log;
        }

        private class TestActionController : ActionController
        {
            public override void EndTurn() { }
        }

        private class TestCombatLog : CombatLogInterface
        {
            public readonly List<string> Messages = new();

            public override void DevMode() { }
            public override void ReleaseMode() { }
            public override void AddWhiteList(string tag) { }
            public override void AddBlackList(string tag) { }
            public override void DevLog(string msg) => Messages.Add(msg);
            public override void DevLog(string msg, string tag) => Messages.Add(msg);
            public override void DevLog(string msg, List<string> tags) => Messages.Add(msg);
            public override void Log(string msg) => Messages.Add(msg);
            public override void Log(string msg, string tag) => Messages.Add(msg);
            public override void Log(string msg, List<string> tags) => Messages.Add(msg);
            public override List<string> GetMessages() => new(Messages);
        }
    }
}
