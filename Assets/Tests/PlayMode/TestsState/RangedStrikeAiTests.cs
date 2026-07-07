using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Strikes;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

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
            GameObject target = FindPlayerTarget();
            GameObject enemy = CreateRangedEnemy("ranged-ai-test-enemy", CreateShortbow(), 1, 7);
            PlaceOnClearLine(tiles, enemy, target);

            MindlessController controller = enemy.GetComponent<MindlessController>();
            controller.StartTurn();
            controller.StopAllCoroutines();
            EntityAction selected = controller.MindlessDecision();

            Assert.IsInstanceOf<StrikeWeapon>(selected);
            Assert.AreEqual("Shortbow", selected.ActionName);
        }

        [UnityTest]
        public IEnumerator EnemyExecutesLegalRangedStrikeConsumesAmmoAndDamagesTarget()
        {
            // PF2e source for Strike as a one-action ranged attack: https://2e.aonprd.com/Rules.aspx?ID=2343
            yield return base.Setup();

            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            GameObject target = FindPlayerTarget();
            GameObject enemy = CreateRangedEnemy("ranged-ai-execute-test-enemy", CreateShortbow(), 1, 100);
            PlaceOnClearLine(tiles, enemy, target);
            Vector3 startingPosition = enemy.transform.position;

            MindlessController controller = enemy.GetComponent<MindlessController>();
            controller.StartTurn();
            controller.StopAllCoroutines();
            EntityAction selected = controller.MindlessDecision();
            Assert.IsInstanceOf<StrikeWeapon>(selected);
            CreatureComponent selectedTargetCreature = PrepareDurableTarget(controller.BestTarget);

            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(7602);
            controller.TakeAction(selected);
            yield return WaitUntilWithTimeout(timeout, () => !controller.IsTakingAction);
            UnityEngine.Random.state = randomState;

            Assert.AreEqual(startingPosition, enemy.transform.position, "Ranged AI should not move when it can Strike from its current cell.");
            Assert.AreEqual(2u, controller.ActionPoints);
            Assert.AreEqual(1u, controller.StrikePenalty);
            Assert.AreEqual(0, enemy.GetComponent<CreatureComponent>().GetAmmoQuantity("arrows"));
            Assert.Less(selectedTargetCreature.hp, 100);
        }

        [UnityTest]
        public IEnumerator PlayerRangedStrikeButtonExecutesAndConsumesAmmo()
        {
            // PF2e source for Strike as a one-action ranged attack: https://2e.aonprd.com/Rules.aspx?ID=2343
            yield return base.Setup();

            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            GameObject player = null;
            yield return WaitForCurrentPlayerTurn(value => player = value);
            GameObject target = FindHostileTarget(player);
            CreatureComponent targetCreature = PrepareDurableTarget(target);
            SetupRangedAction(player, CreateShortbow(), 1, 100);
            PlaceOnClearLine(tiles, player, target);
            OnNextTurn.Invoke(player);

            Button shortbowButton = null;
            yield return WaitUntilWithTimeout(timeout, () =>
            {
                shortbowButton = root.Q<Button>("ShortbowButton");
                return shortbowButton != null;
            });
            Assert.IsNotNull(shortbowButton, "Shortbow action button was not created for the player.");

            PushButton(shortbowButton);
            yield return WaitUntilWithTimeout(timeout, () => grid.Fsm.CurrentState is StateStrike);
            Assert.IsTrue(grid.Fsm.CurrentState is StateStrike);

            Vector3Int targetCell = Vector3Int.RoundToInt(target.transform.position);
            OnHover.Invoke(new List<Vector3Int> { targetCell });

            ActionController controller = player.GetComponent<ActionController>();
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(7603);
            grid.Fsm.CurrentState.Leftclick();
            yield return WaitUntilWithTimeout(timeout, () => !controller.IsTakingAction && grid.Fsm.CurrentState is StateIdle);
            UnityEngine.Random.state = randomState;

            Assert.IsTrue(grid.Fsm.CurrentState is StateIdle);
            Assert.AreEqual(2u, controller.ActionPoints);
            Assert.AreEqual(1u, controller.StrikePenalty);
            Assert.AreEqual(0, player.GetComponent<CreatureComponent>().GetAmmoQuantity("arrows"));
            Assert.Less(targetCreature.hp, 100);
        }

        [UnityTest]
        public IEnumerator PlayerRangedStrikeBlockedByWallDoesNotConsumeActionOrAmmo()
        {
            // PF2e source for cover and blocked line of effect: https://2e.aonprd.com/Rules.aspx?ID=2372
            yield return base.Setup();

            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            GameObject player = null;
            yield return WaitForCurrentPlayerTurn(value => player = value);
            GameObject target = FindHostileTarget(player);
            CreatureComponent targetCreature = PrepareDurableTarget(target);
            SetupRangedAction(player, CreateShortbow(), 1, 100);
            FindEmptyStraightLine(tiles, 5, out Vector3Int playerCell, out Vector3Int targetCell);
            MoveCombatant(tiles, player, playerCell);
            MoveCombatant(tiles, target, targetCell);
            tiles[playerCell.x + 2, playerCell.z] = null;
            OnNextTurn.Invoke(player);

            Button shortbowButton = null;
            yield return WaitUntilWithTimeout(timeout, () =>
            {
                shortbowButton = root.Q<Button>("ShortbowButton");
                return shortbowButton != null;
            });
            Assert.IsNotNull(shortbowButton, "Shortbow action button was not created for the player.");

            PushButton(shortbowButton);
            yield return WaitUntilWithTimeout(timeout, () => grid.Fsm.CurrentState is StateStrike);
            OnHover.Invoke(new List<Vector3Int> { targetCell });
            grid.Fsm.CurrentState.Leftclick();
            yield return null;

            ActionController controller = player.GetComponent<ActionController>();
            Assert.IsTrue(grid.Fsm.CurrentState is StateStrike, "Blocked line of effect should leave targeting active.");
            Assert.AreEqual(3u, controller.ActionPoints);
            Assert.AreEqual(0u, controller.StrikePenalty);
            Assert.AreEqual(1, player.GetComponent<CreatureComponent>().GetAmmoQuantity("arrows"));
            Assert.AreEqual(100, targetCreature.hp);
        }

        [UnityTest]
        public IEnumerator ReloadActionRestoresLoadedStateAndCostsActionPoint()
        {
            // Reload cost is driven by imported weapon data; the action consumes that many action points.
            yield return base.Setup();

            GameObject player = null;
            yield return WaitForCurrentPlayerTurn(value => player = value);
            ActionController controller = player.GetComponent<ActionController>();
            CreatureComponent creature = player.GetComponent<CreatureComponent>();
            EquipmentWeapon sling = CreateSling();
            creature.SetAmmoQuantity("sling-bullets", 2);
            creature.MarkWeaponFired(sling);
            Assert.IsFalse(creature.IsWeaponLoaded(sling));

            controller.StartTurn();
            uint startingActionPoints = controller.ActionPoints;
            ReloadWeaponAction reload = new ReloadWeaponAction(1, sling);
            controller.TakeAction(reload);
            yield return null;

            Assert.IsTrue(creature.IsWeaponLoaded(sling));
            Assert.AreEqual(startingActionPoints - reload.ActionCost, controller.ActionPoints);
            Assert.IsFalse(controller.IsTakingAction);
        }

        private IEnumerator WaitForCurrentPlayerTurn(System.Action<GameObject> assign)
        {
            GameObject current = null;
            yield return WaitUntilWithTimeout(timeout, () =>
            {
                try
                {
                    current = CombatManagerInterface.GetInstance().WhosTurn();
                }
                catch (System.NullReferenceException)
                {
                    current = null;
                }

                return current != null && current.GetComponent<PlayerActionController>() != null;
            });

            Assert.IsNotNull(current, "Expected an active player turn in UnitTestingScene.");
            assign(current);
        }

        private static GameObject FindPlayerTarget()
        {
            foreach (GameObject combatant in CombatManagerInterface.GetInstance().GetCombatants())
            {
                if (combatant.GetComponent<PlayerActionController>() != null)
                    return combatant;
            }

            Assert.Fail("Expected a player target in UnitTestingScene.");
            return null;
        }

        private static GameObject FindHostileTarget(GameObject actor)
        {
            string actorTeam = actor.GetComponent<Team>().Name;
            foreach (GameObject combatant in CombatManagerInterface.GetInstance().GetCombatants())
            {
                if (combatant == actor)
                    continue;

                Team team = combatant.GetComponent<Team>();
                if (team != null && !TeamRules.GetInstance().IsFriendly(actorTeam, team.Name))
                    return combatant;
            }

            Assert.Fail("Expected a hostile target in UnitTestingScene.");
            return null;
        }

        private static GameObject CreateRangedEnemy(string name, EquipmentWeapon weapon, int ammo, int attackBonus)
        {
            GameObject enemy = new GameObject(name);
            enemy.SetActive(false);
            Team enemyTeam = enemy.AddComponent<Team>();
            enemyTeam.Name = "Ranged AI Test Enemies";
            CreatureComponent enemyCreature = enemy.AddComponent<CreatureComponent>();
            enemyCreature.attackBonus = attackBonus;
            enemy.AddComponent<MindlessController>();
            TeamRules rules = TeamRules.GetInstance();
            if (!rules.Contains(enemyTeam.Name))
            {
                rules.AddHostileTeam(enemyTeam.Name);
                rules.OneWayFriendly(enemyTeam.Name, enemyTeam.Name);
            }
            enemy.SetActive(true);
            SetupRangedAction(enemy, weapon, ammo, attackBonus);
            return enemy;
        }

        private static void SetupRangedAction(GameObject actor, EquipmentWeapon weapon, int ammo, int attackBonus)
        {
            CreatureComponent creature = actor.GetComponent<CreatureComponent>();
            creature.attackBonus = attackBonus;
            creature.SetAmmoQuantity(weapon.ammo, ammo);
            actor.GetComponent<ActionController>().AddAction(new StrikeWeapon(1, weapon, actor));
        }

        private static CreatureComponent PrepareDurableTarget(GameObject target)
        {
            Assert.IsNotNull(target);
            CreatureComponent creature = target.GetComponent<CreatureComponent>();
            creature.maxHp = 100;
            creature.hp = 100;
            creature.ac = 1;
            return creature;
        }

        private static EquipmentWeapon CreateShortbow()
        {
            return new EquipmentWeapon
            {
                name = "Shortbow",
                range = 60,
                reload = "0",
                ammo = "arrows",
                damage = new Dice(1, 6, "piercing"),
                traits = new List<string> { "deadly-d10" }
            };
        }

        private static EquipmentWeapon CreateSling()
        {
            return new EquipmentWeapon
            {
                name = "Sling",
                range = 50,
                reload = "1",
                ammo = "sling-bullets",
                damage = new Dice(1, 6, "bludgeoning"),
                traits = new List<string> { "propulsive" }
            };
        }

        private static void PlaceOnClearLine(Tile[,] tiles, GameObject attacker, GameObject target)
        {
            FindEmptyStraightLine(tiles, 5, out Vector3Int attackerCell, out Vector3Int targetCell);
            MoveCombatant(tiles, attacker, attackerCell);
            MoveCombatant(tiles, target, targetCell);
        }

        private static void FindEmptyStraightLine(Tile[,] tiles, int length, out Vector3Int start, out Vector3Int target)
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x <= tiles.GetLength(0) - length; x++)
                {
                    bool clear = true;
                    for (int offset = 0; offset < length; offset++)
                    {
                        Tile tile = tiles[x + offset, z];
                        if (tile == null || tile.Occupants.Count > 0)
                        {
                            clear = false;
                            break;
                        }
                    }

                    if (!clear)
                        continue;

                    start = new Vector3Int(x, 0, z);
                    target = new Vector3Int(x + length - 1, 0, z);
                    return;
                }
            }

            Assert.Fail("Could not find a clear line in UnitTestingScene.");
            start = Vector3Int.zero;
            target = Vector3Int.zero;
        }

        private static void MoveCombatant(Tile[,] tiles, GameObject combatant, Vector3Int cell)
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0); x++)
                    tiles[x, z]?.Occupants.Remove(combatant);
            }

            Assert.IsNotNull(tiles[cell.x, cell.z]);
            combatant.transform.position = new Vector3(cell.x, cell.y, cell.z);
            if (!tiles[cell.x, cell.z].Occupants.Contains(combatant))
                tiles[cell.x, cell.z].Occupants.Add(combatant);
        }
    }
}
