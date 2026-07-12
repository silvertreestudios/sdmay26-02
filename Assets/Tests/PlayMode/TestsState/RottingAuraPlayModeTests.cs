using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Creature;
using Game.Creature.Rules;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TestsState
{
    public class RottingAuraPlayModeTests : PlayModeBase
    {
        private readonly List<GameObject> cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (HUDController hud in Object.FindObjectsByType<HUDController>(FindObjectsSortMode.None))
                hud.enabled = false;

            CombatManager manager = Object.FindFirstObjectByType<CombatManager>();
            if (manager != null)
                SetCombatState(manager, new List<ActionController>(), new List<TurnStep>(), null);

            foreach (GameObject obj in cleanup)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            cleanup.Clear();
            OnCombatStart.RemoveAllListeners();
            OnCombatEnd.RemoveAllListeners();
            OnNextTurn.RemoveAllListeners();
        }

        [UnityTest]
        public IEnumerator RottingAuraUsesSceneGridForVisualCellsAndDamage()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            FindAdjacentOpenCells(tiles, out Vector3Int zombieCell, out Vector3Int targetCell);

            GameObject zombie = CreatureJsonConverter.CreateFromFile("DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura");
            cleanup.Add(zombie);
            TestActionController zombieController = zombie.AddComponent<TestActionController>();
            MoveCombatant(tiles, zombie, zombieCell);

            GameObject target = CreateTarget("wounded target", 8, 12);
            TestActionController targetController = target.GetComponent<TestActionController>();
            MoveCombatant(tiles, target, targetCell);

            AuraGridVisuals auraVisuals = grid.GetComponent<AuraGridVisuals>();
            if (auraVisuals == null)
                auraVisuals = grid.gameObject.AddComponent<AuraGridVisuals>();
            auraVisuals.Refresh();

            Assert.That(auraVisuals.CurrentCells, Does.Contain(zombieCell));
            Assert.That(auraVisuals.CurrentCells, Does.Contain(targetCell));
            Assert.That(auraVisuals.CurrentParticleRadii, Has.Count.EqualTo(1));
            Assert.That(auraVisuals.CurrentParticleRadii[0], Is.EqualTo(2f).Within(0.001f));

            List<CreatureAuraEffectResult> results = CreatureAuraResolver.ApplyTurnStartAuras(
                targetController,
                new[] { zombieController, targetController },
                tiles,
                new FixedDiceRoller(4));

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(4, results[0].AppliedDamage);
            Assert.AreEqual(4, target.GetComponent<CreatureComponent>().hp);
        }

        [UnityTest]
        public IEnumerator CombatManagerAppliesRottingAuraBeforeStartTurnAndSkipsEventSteps()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            FindAdjacentOpenCells(tiles, out Vector3Int zombieCell, out Vector3Int targetCell);

            CombatManager manager = Object.FindFirstObjectByType<CombatManager>();
            if (manager == null)
            {
                GameObject managerObject = new("CombatManager");
                cleanup.Add(managerObject);
                manager = managerObject.AddComponent<CombatManager>();
            }

            GameObject zombie = CreatureJsonConverter.CreateFromFile("DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura");
            cleanup.Add(zombie);
            TestActionController zombieController = zombie.AddComponent<TestActionController>();
            AddTeam(zombie, "Zombies");
            MoveCombatant(tiles, zombie, zombieCell);

            GameObject target = CreateTarget("wounded target", 12, 20);
            TestActionController targetController = target.GetComponent<TestActionController>();
            AddTeam(target, "Players");
            MoveCombatant(tiles, target, targetCell);
            CreatureComponent targetCreature = target.GetComponent<CreatureComponent>();

            SetCombatState(
                manager,
                new List<ActionController> { targetController, zombieController },
                new List<TurnStep> { new TurnStep(targetController) },
                null);

            manager.NextTurn();

            Assert.That(targetController.HpAtStartTurn, Is.LessThan(12), "Rotting Aura should resolve before StartTurn observes creature HP.");
            Assert.That(targetCreature.hp, Is.EqualTo(targetController.HpAtStartTurn), "The aura should not apply again after StartTurn in the same turn step.");
            int hpAfterCombatantTurn = targetCreature.hp;

            bool eventRan = false;
            SetCombatState(
                manager,
                new List<ActionController> { targetController, zombieController },
                new List<TurnStep> { new TurnStep(() => eventRan = true) },
                targetController);

            manager.NextTurn();

            Assert.IsTrue(eventRan);
            Assert.That(targetCreature.hp, Is.EqualTo(hpAfterCombatantTurn), "Non-combatant TurnStep events must not re-apply aura damage to the previous turn taker.");
        }

        [UnityTest]
        public IEnumerator CombatManagerDamagesOnlyActingWoundedCreatureWhenMultipleTargetsAreInAura()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            FindOpenLineOfThreeCells(tiles, out Vector3Int zombieCell, out Vector3Int firstTargetCell, out Vector3Int secondTargetCell);

            CombatManager manager = Object.FindFirstObjectByType<CombatManager>();
            if (manager == null)
            {
                GameObject managerObject = new("CombatManager");
                cleanup.Add(managerObject);
                manager = managerObject.AddComponent<CombatManager>();
            }

            GameObject zombie = CreatureJsonConverter.CreateFromFile("DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura");
            cleanup.Add(zombie);
            TestActionController zombieController = zombie.AddComponent<TestActionController>();
            AddTeam(zombie, "Zombies");
            MoveCombatant(tiles, zombie, zombieCell);

            GameObject firstTarget = CreateTarget("first wounded target", 12, 20);
            TestActionController firstTargetController = firstTarget.GetComponent<TestActionController>();
            AddTeam(firstTarget, "Players");
            MoveCombatant(tiles, firstTarget, firstTargetCell);
            CreatureComponent firstTargetCreature = firstTarget.GetComponent<CreatureComponent>();

            GameObject secondTarget = CreateTarget("second wounded target", 12, 20);
            TestActionController secondTargetController = secondTarget.GetComponent<TestActionController>();
            AddTeam(secondTarget, "Players");
            MoveCombatant(tiles, secondTarget, secondTargetCell);
            CreatureComponent secondTargetCreature = secondTarget.GetComponent<CreatureComponent>();

            SetCombatState(
                manager,
                new List<ActionController> { firstTargetController, secondTargetController, zombieController },
                new List<TurnStep> { new TurnStep(firstTargetController), new TurnStep(secondTargetController) },
                null);

            manager.NextTurn();

            Assert.That(firstTargetCreature.hp, Is.LessThan(12), "The first wounded creature should take aura damage at the beginning of its own turn.");
            Assert.That(secondTargetCreature.hp, Is.EqualTo(12), "Other wounded creatures in the same aura should not take damage on another creature's turn.");
            int firstTargetHpAfterOwnTurn = firstTargetCreature.hp;

            manager.NextTurn();

            Assert.That(firstTargetCreature.hp, Is.EqualTo(firstTargetHpAfterOwnTurn), "A wounded creature should not take a second aura tick when another affected creature starts its turn.");
            Assert.That(secondTargetCreature.hp, Is.LessThan(12), "The second wounded creature should take aura damage at the beginning of its own turn.");
        }

        private GameObject CreateTarget(string name, int hp, int maxHp)
        {
            GameObject obj = new(name);
            cleanup.Add(obj);
            CreatureComponent creature = obj.AddComponent<CreatureComponent>();
            creature.name = name;
            creature.hp = hp;
            creature.maxHp = maxHp;
            creature.traits = new List<string>();
            creature.weaknesses = new List<DamageValue>();
            creature.resistances = new List<DamageValue>();
            obj.AddComponent<TestActionController>();
            return obj;
        }

        private static void FindAdjacentOpenCells(Tile[,] tiles, out Vector3Int zombieCell, out Vector3Int targetCell)
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0) - 1; x++)
                {
                    Tile zombieTile = tiles[x, z];
                    Tile targetTile = tiles[x + 1, z];
                    if (zombieTile != null && targetTile != null && zombieTile.Occupants.Count == 0 && targetTile.Occupants.Count == 0)
                    {
                        zombieCell = new Vector3Int(x, 0, z);
                        targetCell = new Vector3Int(x + 1, 0, z);
                        return;
                    }
                }
            }

            Assert.Fail("Could not find adjacent open cells in UnitTestingScene.");
            zombieCell = Vector3Int.zero;
            targetCell = Vector3Int.zero;
        }

        private static void FindOpenLineOfThreeCells(Tile[,] tiles, out Vector3Int zombieCell, out Vector3Int firstTargetCell, out Vector3Int secondTargetCell)
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0) - 2; x++)
                {
                    Tile zombieTile = tiles[x, z];
                    Tile firstTargetTile = tiles[x + 1, z];
                    Tile secondTargetTile = tiles[x + 2, z];
                    if (zombieTile != null
                        && firstTargetTile != null
                        && secondTargetTile != null
                        && zombieTile.Occupants.Count == 0
                        && firstTargetTile.Occupants.Count == 0
                        && secondTargetTile.Occupants.Count == 0)
                    {
                        zombieCell = new Vector3Int(x, 0, z);
                        firstTargetCell = new Vector3Int(x + 1, 0, z);
                        secondTargetCell = new Vector3Int(x + 2, 0, z);
                        return;
                    }
                }
            }

            Assert.Fail("Could not find three open cells in a line in UnitTestingScene.");
            zombieCell = Vector3Int.zero;
            firstTargetCell = Vector3Int.zero;
            secondTargetCell = Vector3Int.zero;
        }

        private static void MoveCombatant(Tile[,] tiles, GameObject combatant, Vector3Int cell)
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0); x++)
                    tiles[x, z]?.Occupants.Remove(combatant);
            }

            combatant.transform.position = new Vector3(cell.x, cell.y, cell.z);
            tiles[cell.x, cell.z].Occupants.Add(combatant);
        }

        private static void AddTeam(GameObject obj, string teamName)
        {
            Team team = obj.GetComponent<Team>();
            if (team == null)
                team = obj.AddComponent<Team>();
            team.Name = teamName;
        }

        private static void SetCombatState(
            CombatManager manager,
            List<ActionController> combatants,
            List<TurnStep> turnQueue,
            ActionController turnTaker)
        {
            SetPrivateField(manager, "Combatants", combatants);
            SetPrivateField(manager, "TurnQueue", turnQueue);
            SetPrivateField(manager, "TurnTaker", turnTaker);
        }

        private static void SetPrivateField<T>(CombatManager manager, string fieldName, T value)
        {
            FieldInfo field = typeof(CombatManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Could not find CombatManager field " + fieldName);
            field.SetValue(manager, value);
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
            public int HpAtStartTurn { get; private set; } = -1;

            public override void StartTurn()
            {
                CreatureComponent creature = GetComponent<CreatureComponent>();
                HpAtStartTurn = creature == null ? -1 : creature.hp;
                base.StartTurn();
            }

            public override void EndTurn()
            {
            }
        }
    }
}
