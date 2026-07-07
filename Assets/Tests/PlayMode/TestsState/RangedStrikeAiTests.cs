using System.Collections;
using Game.Creature;
using Game.Strikes;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TestsState
{
    public class RangedStrikeAiTests : PlayModeBase
    {
        [UnityTest]
        public IEnumerator EnemyCanChooseLegalRangedStrike()
        {
            yield return base.Setup();

            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();

            GameObject target = null;
            foreach (GameObject combatant in CombatManagerInterface.GetInstance().GetCombatants())
            {
                if (target == null && combatant.GetComponent<PlayerActionController>() != null)
                    target = combatant;
            }

            Assert.IsNotNull(target, "Expected a player target in UnitTestingScene.");

            GameObject enemy = new GameObject("ranged-ai-test-enemy");
            enemy.SetActive(false);
            Team enemyTeam = enemy.AddComponent<Team>();
            enemyTeam.Name = "Ranged AI Test Enemies";
            enemy.AddComponent<CreatureComponent>();
            enemy.AddComponent<MindlessController>();
            TeamRules rules = TeamRules.GetInstance();
            if (!rules.Contains(enemyTeam.Name))
            {
                rules.AddHostileTeam(enemyTeam.Name);
                rules.OneWayFriendly(enemyTeam.Name, enemyTeam.Name);
            }
            enemy.SetActive(true);

            PlaceOnClearLine(tiles, enemy, target);

            CreatureComponent enemyCreature = enemy.GetComponent<CreatureComponent>();
            EquipmentWeapon shortbow = new EquipmentWeapon
            {
                name = "Shortbow",
                range = 60,
                reload = "0",
                ammo = "arrows",
                damage = new Dice(1, 6, "piercing"),
                traits = new System.Collections.Generic.List<string> { "deadly-d10" }
            };
            enemyCreature.SetAmmoQuantity("arrows", 1);
            enemy.GetComponent<ActionController>().AddAction(new StrikeWeapon(1, shortbow, enemy));

            MindlessController controller = enemy.GetComponent<MindlessController>();
            controller.StartTurn();
            controller.StopAllCoroutines();
            EntityAction selected = controller.MindlessDecision();

            Assert.IsInstanceOf<StrikeWeapon>(selected);
            Assert.AreEqual("Shortbow", selected.ActionName);
        }

        private static void PlaceOnClearLine(Tile[,] tiles, GameObject enemy, GameObject target)
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0) - 4; x++)
                {
                    bool clear = true;
                    for (int offset = 0; offset <= 4; offset++)
                    {
                        if (tiles[x + offset, z] == null)
                        {
                            clear = false;
                            break;
                        }
                    }

                    if (!clear)
                        continue;

                    enemy.transform.position = new Vector3(x, 0, z);
                    target.transform.position = new Vector3(x + 4, 0, z);
                    return;
                }
            }

            Assert.Fail("Could not find a clear line for ranged Strike AI test.");
        }
    }
}