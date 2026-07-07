using System;
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
            D20Result naturalTwenty = D20.Evaluate(20, -20, 25);
            D20Result naturalOne = D20.Evaluate(1, 30, 20);

            Assert.AreEqual(DegreeOfSuccess.Fail, naturalTwenty.degree);
            Assert.AreEqual(DegreeOfSuccess.Success, naturalOne.degree);
        }

        [Test]
        public void GridDistanceUsesAlternatingDiagonalCosts()
        {
            Assert.AreEqual(5, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(1, 0, 1)));
            Assert.AreEqual(15, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(2, 0, 2)));
            Assert.AreEqual(30, StrikeTargeting.MeasureGridDistanceFeet(Vector3Int.zero, new Vector3Int(4, 0, 4)));
        }

        [Test]
        public void RangedIncrementPenaltyCapsAtSixIncrements()
        {
            Assert.AreEqual(0, StrikeTargeting.CalculateRangePenalty(60, 60));
            Assert.AreEqual(-2, StrikeTargeting.CalculateRangePenalty(65, 60));
            Assert.AreEqual(-10, StrikeTargeting.CalculateRangePenalty(360, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => StrikeTargeting.CalculateRangePenalty(365, 60));
        }

        [Test]
        public void SolidWallBlocksLineOfEffect()
        {
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
    }
}