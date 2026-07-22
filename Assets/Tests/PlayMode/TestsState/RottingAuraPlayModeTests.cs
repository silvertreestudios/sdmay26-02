using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.KayKit;
using Game.Rules.Unity;
using GridPrivate;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TestsState
{
    public class RottingAuraPlayModeTests : PlayModeBase
    {
        private readonly List<GameObject> cleanup = new();
        private bool controlledCombatStarted;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (
                HUDController hud in Object.FindObjectsByType<HUDController>(
                    FindObjectsSortMode.None
                )
            )
                hud.enabled = false;

            CombatManager manager = Object.FindFirstObjectByType<CombatManager>();
            if (manager != null)
            {
                if (controlledCombatStarted && manager.IsCombatActive)
                {
                    manager.SuspendDungeonCombat();
                    float deadline = Time.realtimeSinceStartup + 5f;
                    while (manager.IsCombatActive && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(manager.IsCombatActive, Is.False);
                }
                else
                {
                    manager.StopAllCoroutines();
                }
            }

            foreach (GameObject obj in cleanup)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            cleanup.Clear();
            OnCombatStart.RemoveAllListeners();
            OnCombatEnd.RemoveAllListeners();
            OnNextTurn.RemoveAllListeners();
            controlledCombatStarted = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RottingAuraUsesSceneGridForVisualCellsAndDamage()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            FindAdjacentOpenCells(tiles, out Vector3Int zombieCell, out Vector3Int targetCell);

            GameObject zombie = CreatureJsonConverter.CreateFromFile(
                "DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura"
            );
            cleanup.Add(zombie);
            TestActionController zombieController = zombie.AddComponent<TestActionController>();
            MoveCombatant(tiles, zombie, zombieCell);

            GameObject target = CreateTarget("wounded target", 8, 12);
            TestActionController targetController = target.GetComponent<TestActionController>();
            MoveCombatant(tiles, target, targetCell);
            UnityEncounterRulesBridge.CreateHealthTestComposition(
                new CreatureComponent[]
                {
                    zombie.GetComponent<CreatureComponent>(),
                    targetController.GetComponent<CreatureComponent>(),
                }
            );

            AuraGridVisuals auraVisuals = grid.GetComponent<AuraGridVisuals>();
            if (auraVisuals == null)
                auraVisuals = grid.gameObject.AddComponent<AuraGridVisuals>();
            auraVisuals.Refresh();

            Assert.That(auraVisuals.CurrentCells, Does.Contain(zombieCell));
            Assert.That(auraVisuals.CurrentCells, Does.Contain(targetCell));
            Assert.That(auraVisuals.CurrentParticleRadii, Has.Count.EqualTo(1));
            Assert.That(auraVisuals.CurrentParticleRadii[0], Is.EqualTo(2f).Within(0.001f));

            CoroutineResult<List<CreatureAuraEffectResult>> applied =
                new CoroutineResult<List<CreatureAuraEffectResult>>();
            yield return CoroutineRunner.Await(
                CreatureAuraResolver.ApplyTurnStartAurasAsync(
                    targetController,
                    new[] { zombieController, targetController },
                    tiles,
                    new FixedDiceRoller(4)
                ),
                applied
            );
            List<CreatureAuraEffectResult> results = applied.Value;

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

            GameObject zombie = CreatureJsonConverter.CreateFromFile(
                "DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura"
            );
            cleanup.Add(zombie);
            TestActionController zombieController = zombie.AddComponent<TestActionController>();
            AddTeam(zombie, "Zombies");
            MoveCombatant(tiles, zombie, zombieCell);

            GameObject target = CreateTarget("wounded target", 12, 20);
            TestActionController targetController = target.GetComponent<TestActionController>();
            AddTeam(target, "Players");
            MoveCombatant(tiles, target, targetCell);
            CreatureComponent targetCreature = target.GetComponent<CreatureComponent>();

            target.GetComponent<CreatureComponent>().initiative = 1000;
            zombie.GetComponent<CreatureComponent>().initiative = -1000;
            yield return StartControlledCombat(
                manager,
                new List<ActionController> { targetController, zombieController }
            );

            Assert.That(
                targetController.HpAtStartTurn,
                Is.LessThan(12),
                "Rotting Aura should resolve before StartTurn observes creature HP."
            );
            Assert.That(
                targetCreature.hp,
                Is.EqualTo(targetController.HpAtStartTurn),
                "The aura should not apply again after StartTurn in the same turn step."
            );
            int hpAfterCombatantTurn = targetCreature.hp;

            manager.EndCurrentTurn(targetController);
            yield return WaitForActor(manager, zombie);
            Assert.That(
                targetCreature.hp,
                Is.EqualTo(hpAfterCombatantTurn),
                "Advancing to a different actor must not re-apply aura damage to the previous turn taker."
            );
        }

        [UnityTest]
        public IEnumerator CombatManagerDamagesOnlyActingWoundedCreatureWhenMultipleTargetsAreInAura()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            FindOpenLineOfThreeCells(
                tiles,
                out Vector3Int zombieCell,
                out Vector3Int firstTargetCell,
                out Vector3Int secondTargetCell
            );

            CombatManager manager = Object.FindFirstObjectByType<CombatManager>();
            if (manager == null)
            {
                GameObject managerObject = new("CombatManager");
                cleanup.Add(managerObject);
                manager = managerObject.AddComponent<CombatManager>();
            }

            GameObject zombie = CreatureJsonConverter.CreateFromFile(
                "DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura"
            );
            cleanup.Add(zombie);
            TestActionController zombieController = zombie.AddComponent<TestActionController>();
            AddTeam(zombie, "Zombies");
            MoveCombatant(tiles, zombie, zombieCell);

            GameObject firstTarget = CreateTarget("first wounded target", 12, 20);
            TestActionController firstTargetController =
                firstTarget.GetComponent<TestActionController>();
            AddTeam(firstTarget, "Players");
            MoveCombatant(tiles, firstTarget, firstTargetCell);
            CreatureComponent firstTargetCreature = firstTarget.GetComponent<CreatureComponent>();

            GameObject secondTarget = CreateTarget("second wounded target", 12, 20);
            TestActionController secondTargetController =
                secondTarget.GetComponent<TestActionController>();
            AddTeam(secondTarget, "Players");
            MoveCombatant(tiles, secondTarget, secondTargetCell);
            CreatureComponent secondTargetCreature = secondTarget.GetComponent<CreatureComponent>();

            firstTargetCreature.initiative = 1000;
            secondTargetCreature.initiative = 500;
            zombie.GetComponent<CreatureComponent>().initiative = -1000;
            yield return StartControlledCombat(
                manager,
                new List<ActionController>
                {
                    firstTargetController,
                    secondTargetController,
                    zombieController,
                }
            );

            Assert.That(
                firstTargetCreature.hp,
                Is.LessThan(12),
                "The first wounded creature should take aura damage at the beginning of its own turn."
            );
            Assert.That(
                secondTargetCreature.hp,
                Is.EqualTo(12),
                "Other wounded creatures in the same aura should not take damage on another creature's turn."
            );
            int firstTargetHpAfterOwnTurn = firstTargetCreature.hp;

            manager.EndCurrentTurn(firstTargetController);
            yield return WaitForActor(manager, secondTarget);

            Assert.That(
                firstTargetCreature.hp,
                Is.EqualTo(firstTargetHpAfterOwnTurn),
                "A wounded creature should not take a second aura tick when another affected creature starts its turn."
            );
            Assert.That(
                secondTargetCreature.hp,
                Is.LessThan(12),
                "The second wounded creature should take aura damage at the beginning of its own turn."
            );
        }

        [UnityTest]
        public IEnumerator DefeatedAuraOwnerRemainingActiveDoesNotDamageLaterActor()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.That(grid, Is.Not.Null);
            Tile[,] tiles = grid.GetTiles();
            FindOpenLineOfThreeCells(
                tiles,
                out Vector3Int auraCell,
                out Vector3Int currentCell,
                out Vector3Int laterCell
            );
            CombatManager manager = Object.FindFirstObjectByType<CombatManager>();

            GameObject auraOwner = CreatureJsonConverter.CreateFromFile(
                "DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura"
            );
            cleanup.Add(auraOwner);
            auraOwner.name = "defeated active aura owner";
            TestActionController auraController = auraOwner.AddComponent<TestActionController>();
            AddTeam(auraOwner, "Enemies");
            MoveCombatant(tiles, auraOwner, auraCell);
            GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/KayKit/Prefabs/Animated/RangerAnimated.prefab"
            );
            GameObject visual = Object.Instantiate(visualPrefab, auraOwner.transform);
            CreaturePresentation presentation = auraOwner.AddComponent<CreaturePresentation>();
            presentation.Bind(
                visual.GetComponent<CreatureAnimationController>(),
                visual.GetComponent<CreatureEquipmentVisuals>()
            );

            GameObject current = CreateTarget("current player", 20, 20);
            TestActionController currentController = current.GetComponent<TestActionController>();
            AddTeam(current, "Players");
            MoveCombatant(tiles, current, currentCell);

            GameObject later = CreateTarget("later wounded player", 12, 20);
            TestActionController laterController = later.GetComponent<TestActionController>();
            AddTeam(later, "Players");
            MoveCombatant(tiles, later, laterCell);

            GameObject livingEnemy = CreateTarget("living opposition", 10, 10);
            TestActionController livingEnemyController =
                livingEnemy.GetComponent<TestActionController>();
            AddTeam(livingEnemy, "Enemies");

            current.GetComponent<CreatureComponent>().initiative = 1000;
            later.GetComponent<CreatureComponent>().initiative = 500;
            auraOwner.GetComponent<CreatureComponent>().initiative = 0;
            livingEnemy.GetComponent<CreatureComponent>().initiative = -1000;
            yield return StartControlledCombat(
                manager,
                new List<ActionController>
                {
                    currentController,
                    laterController,
                    auraController,
                    livingEnemyController,
                }
            );
            yield return WaitForActor(manager, current);
            CreatureComponent auraCreature = auraOwner.GetComponent<CreatureComponent>();

            yield return CoroutineRunner.Await(
                auraCreature.ApplyFinalDamageAsync(
                    auraCreature.hp,
                    Game.Rules.Runtime.RuleSource.FromSlug("test-defeated-aura-source")
                )
            );

            Assert.That(
                auraOwner.activeSelf,
                Is.True,
                "Death presentation should still be active."
            );
            Assert.That(auraCreature.hp, Is.Zero);
            int hpBeforeLaterTurn = later.GetComponent<CreatureComponent>().hp;
            manager.EndCurrentTurn(currentController);
            yield return WaitForActor(manager, later);

            Assert.That(
                later.GetComponent<CreatureComponent>().hp,
                Is.EqualTo(hpBeforeLaterTurn),
                "A zero-HP aura source must not contribute while its death animation remains active."
            );
        }

        [UnityTest]
        public IEnumerator CombatManagerSkipsAndDoesNotRequeueActorDefeatedDuringTurnStart()
        {
            yield return base.Setup();
            GameManager sceneGameManager = Object.FindFirstObjectByType<GameManager>();
            if (sceneGameManager != null)
            {
                // This test replaces the combat queue below. Prevent the scene's delayed
                // auto-start coroutine from overwriting that controlled state on the next frame.
                sceneGameManager.StopAllCoroutines();
                sceneGameManager.enabled = false;
            }
            foreach (
                HUDController hud in Object.FindObjectsByType<HUDController>(
                    FindObjectsSortMode.None
                )
            )
                hud.enabled = false;

            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindOpenLineOfThreeCells(
                tiles,
                out Vector3Int defeatedCell,
                out Vector3Int survivorCell,
                out Vector3Int hostileCell
            );

            CombatManager manager = Object.FindFirstObjectByType<CombatManager>();
            GameObject defeatedActor = CreateTarget("defeated during turn start", 1, 20);
            TestActionController defeatedController =
                defeatedActor.GetComponent<TestActionController>();
            AddTeam(defeatedActor, "Players");
            MoveCombatant(tiles, defeatedActor, defeatedCell);

            GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/KayKit/Prefabs/Animated/RangerAnimated.prefab"
            );
            GameObject visual = Object.Instantiate(visualPrefab, defeatedActor.transform);
            CreaturePresentation presentation = defeatedActor.AddComponent<CreaturePresentation>();
            presentation.Bind(
                visual.GetComponent<CreatureAnimationController>(),
                visual.GetComponent<CreatureEquipmentVisuals>()
            );

            GameObject survivor = CreateTarget("surviving player", 10, 10);
            TestActionController survivorController = survivor.GetComponent<TestActionController>();
            AddTeam(survivor, "Players");
            MoveCombatant(tiles, survivor, survivorCell);

            GameObject hostile = CreatureJsonConverter.CreateFromFile(
                "DataFiles/pathfinder-monster-core/zombie-shambler-rotting-aura"
            );
            cleanup.Add(hostile);
            hostile.name = "hostile aura actor";
            TestActionController hostileController = hostile.AddComponent<TestActionController>();
            AddTeam(hostile, "Enemies");
            MoveCombatant(tiles, hostile, hostileCell);

            defeatedActor.GetComponent<CreatureComponent>().initiative = 1000;
            survivor.GetComponent<CreatureComponent>().initiative = 500;
            hostile.GetComponent<CreatureComponent>().initiative = -1000;
            yield return StartControlledCombat(
                manager,
                new List<ActionController>
                {
                    defeatedController,
                    survivorController,
                    hostileController,
                }
            );
            yield return WaitForActor(manager, survivor);

            Assert.That(
                defeatedActor.activeSelf,
                Is.True,
                "The death presentation should still be playing."
            );
            Assert.That(defeatedController.enabled, Is.False);
            Assert.That(
                defeatedController.StartTurnCount,
                Is.Zero,
                "A defeated actor must not execute StartTurn."
            );
            Assert.That(
                survivorController.StartTurnCount,
                Is.EqualTo(1),
                "Turn processing should advance to the next eligible actor."
            );

            Assert.That(
                manager.GetCombatants(),
                Has.No.Member(defeatedActor),
                "Gameplay targeting must exclude defeated immutable timing slots."
            );
        }

        private GameObject CreateTarget(string name, int hp, int maxHp)
        {
            GameObject obj = new(name);
            cleanup.Add(obj);
            CreatureComponent creature = obj.AddComponent<CreatureComponent>();
            creature.name = name;
            creature.InitializeHealthBeforeEncounter(hp, maxHp);
            creature.traits = new List<string>();
            creature.weaknesses = new List<DamageValue>();
            creature.resistances = new List<DamageValue>();
            obj.AddComponent<TestActionController>();
            return obj;
        }

        private static void FindAdjacentOpenCells(
            Tile[,] tiles,
            out Vector3Int zombieCell,
            out Vector3Int targetCell
        )
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0) - 1; x++)
                {
                    Tile zombieTile = tiles[x, z];
                    Tile targetTile = tiles[x + 1, z];
                    if (
                        zombieTile != null
                        && targetTile != null
                        && zombieTile.Occupants.Count == 0
                        && targetTile.Occupants.Count == 0
                    )
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

        private static void FindOpenLineOfThreeCells(
            Tile[,] tiles,
            out Vector3Int zombieCell,
            out Vector3Int firstTargetCell,
            out Vector3Int secondTargetCell
        )
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0) - 2; x++)
                {
                    Tile zombieTile = tiles[x, z];
                    Tile firstTargetTile = tiles[x + 1, z];
                    Tile secondTargetTile = tiles[x + 2, z];
                    if (
                        zombieTile != null
                        && firstTargetTile != null
                        && secondTargetTile != null
                        && zombieTile.Occupants.Count == 0
                        && firstTargetTile.Occupants.Count == 0
                        && secondTargetTile.Occupants.Count == 0
                    )
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

        private IEnumerator StartControlledCombat(
            CombatManager manager,
            List<ActionController> combatants
        )
        {
            foreach (
                GameManager gameManager in Object.FindObjectsByType<GameManager>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            )
            {
                gameManager.StopAllCoroutines();
                gameManager.enabled = false;
            }
            Assert.That(
                manager.IsCombatActive,
                Is.False,
                "The scene GameManager bootstrap must be suppressed before controlled combat starts."
            );
            foreach (GameObject existing in manager.GetCombatants().ToArray())
            {
                ActionController controller = existing.GetComponent<ActionController>();
                if (controller != null)
                    manager.Remove(controller);
            }
            foreach (ActionController controller in combatants)
                manager.AddCombatant(controller);
            manager.StartDungeonCombat(combatants);
            controlledCombatStarted = true;
            float startDeadline = Time.realtimeSinceStartup + 5f;
            while (manager.WhosTurn() == null && Time.realtimeSinceStartup < startDeadline)
                yield return null;
            Assert.That(manager.WhosTurn(), Is.Not.Null);
        }

        private static IEnumerator WaitForActor(CombatManager manager, GameObject expected)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (manager.WhosTurn() != expected && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(
                manager.WhosTurn(),
                Is.SameAs(expected),
                $"Timed out waiting for {expected.name} to receive the committed turn."
            );
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
            public int StartTurnCount { get; private set; }

            public override void StartTurn()
            {
                StartTurnCount++;
                CreatureComponent creature = GetComponent<CreatureComponent>();
                HpAtStartTurn = creature == null ? -1 : creature.hp;
                base.StartTurn();
            }

            public override void EndTurn() { }
        }
    }
}
