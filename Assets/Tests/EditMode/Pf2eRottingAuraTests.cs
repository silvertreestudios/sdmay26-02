using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Creature.Rules;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class Pf2eRottingAuraTests
    {
        private readonly List<GameObject> cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject obj in cleanup)
            {
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }
            cleanup.Clear();
        }

        [Test]
        public void ImportedRottingAuraZombieDropsBiteAndAddsAuraRuleData()
        {
            GameObject zombieObject = CreatureJsonConverter.CreateFromFile(
                "DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura"
            );
            cleanup.Add(zombieObject);
            CreatureComponent zombie = zombieObject.GetComponent<CreatureComponent>();

            Assert.AreEqual("Zombie Shambler (Rotting Aura)", zombie.name);
            CollectionAssert.Contains(zombie.actions, "Fist");
            CollectionAssert.Contains(zombie.actions, "Grab");
            CollectionAssert.DoesNotContain(zombie.actions, "Jaws");
            CollectionAssert.Contains(zombie.passives, "Rotting Aura");
            CollectionAssert.Contains(zombie.traits, "undead");
            CollectionAssert.Contains(zombie.traits, "zombie");

            CreatureAura aura = zombie.auras.Single(a => a.slug == RottingAuraRule.RuleSlug);
            Assert.AreEqual(10, aura.radiusFeet);
            CollectionAssert.Contains(aura.traits, "disease");
            CollectionAssert.Contains(aura.traits, "void");
        }

        [Test]
        public async Task RottingAuraDamagesOnlyWoundedLivingTargetsInsideEmanation()
        {
            Tile[,] tiles = BuildTiles(8, 8);
            TestActionController source = CreateCombatant(
                "aura zombie",
                3,
                3,
                20,
                20,
                new[] { "undead", "zombie" }
            );
            source
                .GetComponent<CreatureComponent>()
                .auras.Add(
                    new CreatureAura
                    {
                        name = "Rotting Aura",
                        slug = RottingAuraRule.RuleSlug,
                        radiusFeet = 10,
                        traits = new List<string> { "disease", "void" },
                    }
                );
            TestActionController wounded = CreateCombatant("wounded hero", 5, 3, 7, 10);
            TestActionController healthy = CreateCombatant("healthy hero", 4, 3, 10, 10);
            TestActionController outside = CreateCombatant("outside hero", 7, 3, 7, 10);
            Place(tiles, source.gameObject);
            Place(tiles, wounded.gameObject);
            Place(tiles, healthy.gameObject);
            Place(tiles, outside.gameObject);

            List<CreatureAuraEffectResult> woundedResults =
                await CreatureAuraResolver.ApplyTurnStartAurasAsync(
                    wounded,
                    new[] { source, wounded, healthy, outside },
                    tiles,
                    new FixedDiceRoller(3)
                );
            List<CreatureAuraEffectResult> healthyResults =
                await CreatureAuraResolver.ApplyTurnStartAurasAsync(
                    healthy,
                    new[] { source, wounded, healthy, outside },
                    tiles,
                    new FixedDiceRoller(3)
                );
            List<CreatureAuraEffectResult> outsideResults =
                await CreatureAuraResolver.ApplyTurnStartAurasAsync(
                    outside,
                    new[] { source, wounded, healthy, outside },
                    tiles,
                    new FixedDiceRoller(3)
                );

            Assert.AreEqual(1, woundedResults.Count);
            CreatureComponent woundedCreature = wounded.GetComponent<CreatureComponent>();
            Assert.AreEqual(4, woundedCreature.hp);
            Assert.AreEqual(4, woundedCreature.Health.Current);
            Assert.AreEqual(0, healthyResults.Count);
            Assert.AreEqual(10, healthy.GetComponent<CreatureComponent>().hp);
            Assert.AreEqual(0, outsideResults.Count);
            Assert.AreEqual(7, outside.GetComponent<CreatureComponent>().hp);
        }

        [Test]
        public async Task RottingAuraExcludesUndeadAndConstructTargets()
        {
            Tile[,] tiles = BuildTiles(8, 8);
            TestActionController source = CreateAuraZombie(tiles, 3, 3, level: 0);
            TestActionController undead = CreateCombatant(
                "undead target",
                4,
                3,
                7,
                10,
                new[] { "undead" }
            );
            TestActionController construct = CreateCombatant(
                "construct target",
                5,
                3,
                7,
                10,
                new[] { "construct" }
            );
            Place(tiles, undead.gameObject);
            Place(tiles, construct.gameObject);

            Assert.AreEqual(
                0,
                (
                    await CreatureAuraResolver.ApplyTurnStartAurasAsync(
                        undead,
                        new[] { source, undead },
                        tiles,
                        new FixedDiceRoller(6)
                    )
                ).Count
            );
            Assert.AreEqual(
                0,
                (
                    await CreatureAuraResolver.ApplyTurnStartAurasAsync(
                        construct,
                        new[] { source, construct },
                        tiles,
                        new FixedDiceRoller(6)
                    )
                ).Count
            );
            Assert.AreEqual(7, undead.GetComponent<CreatureComponent>().hp);
            Assert.AreEqual(7, construct.GetComponent<CreatureComponent>().hp);
        }

        [Test]
        public async Task RottingAuraUsesVoidWeaknessResistanceAndLevelScaling()
        {
            Tile[,] tiles = BuildTiles(8, 8);
            TestActionController source = CreateAuraZombie(tiles, 3, 3, level: 6);
            TestActionController target = CreateCombatant("wounded hero", 5, 3, 12, 20);
            CreatureComponent targetCreature = target.GetComponent<CreatureComponent>();
            targetCreature.weaknesses.Add(new DamageValue("void", 2));
            targetCreature.resistances.Add(new DamageValue("void", 1));
            Place(tiles, target.gameObject);

            List<CreatureAuraEffectResult> results =
                await CreatureAuraResolver.ApplyTurnStartAurasAsync(
                    target,
                    new[] { source, target },
                    tiles,
                    new FixedDiceRoller(2)
                );

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(4, results[0].RolledDamage, "Level 6 aura should roll 2d6.");
            Assert.AreEqual(5, results[0].AppliedDamage, "4 void +2 weakness -1 resistance.");
            Assert.AreEqual(7, targetCreature.hp);
        }

        [Test]
        public void RottingAuraVisualCellsComeFromEmanationTargeting()
        {
            Tile[,] tiles = BuildTiles(8, 8);
            TestActionController source = CreateAuraZombie(tiles, 3, 3, level: 0);

            List<Vector3Int> cells = CreatureAuraResolver.GetAuraCells(new[] { source }, tiles);

            CollectionAssert.Contains(cells, Cell(3, 3));
            CollectionAssert.Contains(cells, Cell(5, 3));
            CollectionAssert.DoesNotContain(cells, Cell(6, 3));
        }

        private TestActionController CreateAuraZombie(Tile[,] tiles, int x, int z, int level)
        {
            TestActionController controller = CreateCombatant(
                "aura zombie",
                x,
                z,
                20,
                20,
                new[] { "undead", "zombie" }
            );
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            creature.level = level;
            creature.auras.Add(
                new CreatureAura
                {
                    name = "Rotting Aura",
                    slug = RottingAuraRule.RuleSlug,
                    radiusFeet = 10,
                    traits = new List<string> { "disease", "void" },
                }
            );
            Place(tiles, controller.gameObject);
            return controller;
        }

        private TestActionController CreateCombatant(
            string name,
            int x,
            int z,
            int hp,
            int maxHp,
            IEnumerable<string> traits = null
        )
        {
            GameObject obj = new(name);
            cleanup.Add(obj);
            obj.transform.position = new Vector3(x, 0, z);
            CreatureComponent creature = obj.AddComponent<CreatureComponent>();
            creature.name = name;
            creature.InitializeHealthBeforeEncounter(hp, maxHp);
            creature.traits = traits == null ? new List<string>() : new List<string>(traits);
            creature.weaknesses = new List<DamageValue>();
            creature.resistances = new List<DamageValue>();
            TestActionController controller = obj.AddComponent<TestActionController>();
            obj.AddComponent<Team>().Name = "Players";
            Game.Rules.Unity.UnityEncounterRulesBridge.Create(
                new ActionController[] { controller },
                "Players"
            );
            return controller;
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

        private static void Place(Tile[,] tiles, GameObject obj)
        {
            Vector3Int cell = Vector3Int.RoundToInt(obj.transform.position);
            tiles[cell.x, cell.z].Occupants.Add(obj);
        }

        private static Vector3Int Cell(int x, int z)
        {
            return new Vector3Int(x, 0, z);
        }

        private sealed class FixedDiceRoller : IPf2eDiceRoller
        {
            private readonly int valuePerDie;

            public FixedDiceRoller(int valuePerDie)
            {
                this.valuePerDie = valuePerDie;
            }

            public int Roll(int numberOfDice, int sidesPerDie)
            {
                return numberOfDice * valuePerDie;
            }
        }

        private sealed class TestActionController : ActionController
        {
            public override void EndTurn() { }
        }
    }
}
