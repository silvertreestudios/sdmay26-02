using System;
using System.Collections;
using System.Collections.Generic;
using Game.Creature;
using Game.Strikes;
using GridPrivate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TestsState
{
    public class LenaRoguePlayModeTests : PlayModeBase
    {
        [UnityTest]
        public IEnumerator LenaSceneFixtureIsPlayable()
        {
            yield return base.Setup();
            GameObject lena = null;
            yield return WaitForLena(value => lena = value);
            CreatureComponent creature = lena.GetComponent<CreatureComponent>();
            ActionController controller = lena.GetComponent<ActionController>();

            Assert.AreEqual("Players", lena.GetComponent<Team>().Name);
            Assert.IsInstanceOf<PlayerActionController>(controller);
            Assert.That(creature.Prepared.HasOwnedItem("rogue"), Is.True);
            Assert.That(creature.Prepared.HasOwnedItem("sneak-attack"), Is.True);
            Assert.That(creature.Prepared.HasOwnedItem("thief"), Is.True);
            Assert.That(creature.Prepared.HasOwnedItem("nimble-dodge"), Is.True);
            Assert.That(creature.GetAmmoQuantity("arrows"), Is.EqualTo(20));
            AssertHasAction(controller, "Dogslicer");
            AssertHasAction(controller, "Shortbow");
            AssertVisibleToken(lena);
        }

        [UnityTest]
        public IEnumerator LenaDogslicerUsesThiefDexDamage()
        {
            yield return base.Setup();
            GameObject lena = null;
            yield return WaitForLena(value => lena = value);
            GameObject target = FindHostileTarget(lena);
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            FindAdjacentOpenCells(grid.GetTiles(), out Vector3Int lenaCell, out Vector3Int targetCell);
            MoveCombatant(grid.GetTiles(), lena, lenaCell);
            MoveCombatant(grid.GetTiles(), target, targetCell);
            PrepareTarget(target, -10, 100);
            yield return ForceTurnAndClickAction(lena, "DogslicerButton");

            StrikeResolutionContext observed = null;
            yield return ExecuteSelectedStrike(lena, targetCell, value => observed = value);

            CreatureComponent creature = lena.GetComponent<CreatureComponent>();
            Assert.IsNotNull(observed);
            Assert.That(observed.Profile.ItemSlug, Is.EqualTo("dogslicer"));
            Assert.That(observed.Traits, Does.Contain("finesse"));
            Assert.That(observed.FlatDamages[0].DamageAmount, Is.EqualTo(creature.dexMod));
            Assert.That(lena.GetComponent<ActionController>().ActionPoints, Is.EqualTo(2u));
            Assert.That(lena.GetComponent<ActionController>().StrikePenalty, Is.EqualTo(1u));
            Assert.Less(target.GetComponent<CreatureComponent>().hp, 100);
        }

        [UnityTest]
        public IEnumerator LenaSneakAttackRequiresOffGuard()
        {
            yield return base.Setup();
            GameObject lena = null;
            yield return WaitForLena(value => lena = value);
            GameObject target = FindHostileTarget(lena);
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            FindAdjacentOpenCells(grid.GetTiles(), out Vector3Int lenaCell, out Vector3Int targetCell);
            MoveCombatant(grid.GetTiles(), lena, lenaCell);
            MoveCombatant(grid.GetTiles(), target, targetCell);
            PrepareTarget(target, -10, 100);

            yield return ForceTurnAndClickAction(lena, "DogslicerButton");
            StrikeResolutionContext normalStrike = null;
            yield return ExecuteSelectedStrike(lena, targetCell, value => normalStrike = value);
            Assert.That(normalStrike.DamageDice.Count, Is.EqualTo(1));

            target.GetComponent<CreatureComponent>().hp = 100;
            target.GetComponent<Conditions>().Add("Off-Guard", new ConditionSource());
            yield return ForceTurnAndClickAction(lena, "DogslicerButton");
            StrikeResolutionContext offGuardStrike = null;
            yield return ExecuteSelectedStrike(lena, targetCell, value => offGuardStrike = value);

            Assert.That(offGuardStrike.DamageDice.Count, Is.EqualTo(2));
            Assert.That(offGuardStrike.DamageDice[1].numberOfDice, Is.EqualTo(1));
            Assert.That(offGuardStrike.DamageDice[1].sidesPerDie, Is.EqualTo(6));
            Assert.That(offGuardStrike.DamageDice[1].damageType, Is.EqualTo("precision"));
        }

        [UnityTest]
        public IEnumerator LenaDogslicerGetsSneakAttackWhenFlanking()
        {
            yield return base.Setup();
            GameObject lena = null;
            yield return WaitForLena(value => lena = value);
            GameObject target = FindHostileTarget(lena);
            GameObject ally = FindFriendlyAlly(lena);
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindFlankingLine(tiles, out Vector3Int allyCell, out Vector3Int targetCell, out Vector3Int lenaCell);
            MoveCombatant(tiles, ally, allyCell);
            MoveCombatant(tiles, target, targetCell);
            MoveCombatant(tiles, lena, lenaCell);
            PrepareTarget(target, -10, 100);

            yield return ForceTurnAndClickAction(lena, "DogslicerButton");
            StrikeResolutionContext observed = null;
            yield return ExecuteSelectedStrike(lena, targetCell, value => observed = value);

            Assert.IsFalse(target.GetComponent<Conditions>().Contains("Off-Guard"), "Flanking should be contextual to the flankers, not a global target condition.");
            Assert.That(observed.DamageDice.Count, Is.EqualTo(2));
            Assert.That(observed.DamageDice[1].damageType, Is.EqualTo("precision"));
            Assert.Less(target.GetComponent<CreatureComponent>().hp, 100);
        }

        [UnityTest]
        public IEnumerator LenaDogslicerDoesNotGetSneakAttackFromSameSideAlly()
        {
            yield return base.Setup();
            GameObject lena = null;
            yield return WaitForLena(value => lena = value);
            GameObject target = FindHostileTarget(lena);
            GameObject ally = FindFriendlyAlly(lena);
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindSameSideNonFlankingCells(tiles, out Vector3Int allyCell, out Vector3Int targetCell, out Vector3Int lenaCell);
            MoveCombatant(tiles, ally, allyCell);
            MoveCombatant(tiles, target, targetCell);
            MoveCombatant(tiles, lena, lenaCell);
            PrepareTarget(target, -10, 100);

            yield return ForceTurnAndClickAction(lena, "DogslicerButton");
            StrikeResolutionContext observed = null;
            yield return ExecuteSelectedStrike(lena, targetCell, value => observed = value);

            Assert.That(observed.DamageDice.Count, Is.EqualTo(1));
            Assert.IsFalse(target.GetComponent<Conditions>().Contains("Off-Guard"));
        }

        [UnityTest]
        public IEnumerator LenaShortbowDoesNotGetSneakAttackFromFlanking()
        {
            yield return base.Setup();
            GameObject lena = null;
            yield return WaitForLena(value => lena = value);
            GameObject target = FindHostileTarget(lena);
            GameObject ally = FindFriendlyAlly(lena);
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindFlankingLine(tiles, out Vector3Int allyCell, out Vector3Int targetCell, out Vector3Int lenaCell);
            MoveCombatant(tiles, ally, allyCell);
            MoveCombatant(tiles, target, targetCell);
            MoveCombatant(tiles, lena, lenaCell);
            PrepareTarget(target, -10, 100);

            yield return ForceTurnAndClickAction(lena, "ShortbowButton");
            StrikeResolutionContext observed = null;
            yield return ExecuteSelectedStrike(lena, targetCell, value => observed = value);

            Assert.That(observed.Profile.IsRangedAttack, Is.True);
            Assert.That(observed.DamageDice.Count, Is.EqualTo(1));
            Assert.IsFalse(target.GetComponent<Conditions>().Contains("Off-Guard"));
        }

        [UnityTest]
        public IEnumerator LenaShortbowRangedSneakAttackConsumesAmmoWithoutMeleeFlatDamage()
        {
            yield return base.Setup();
            GameObject lena = null;
            yield return WaitForLena(value => lena = value);
            GameObject target = FindHostileTarget(lena);
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            FindEmptyStraightLine(grid.GetTiles(), 5, out Vector3Int lenaCell, out Vector3Int targetCell);
            MoveCombatant(grid.GetTiles(), lena, lenaCell);
            MoveCombatant(grid.GetTiles(), target, targetCell);
            PrepareTarget(target, -10, 100);
            target.GetComponent<Conditions>().Add("Off-Guard", new ConditionSource());

            CreatureComponent creature = lena.GetComponent<CreatureComponent>();
            int startingAmmo = creature.GetAmmoQuantity("arrows");
            yield return ForceTurnAndClickAction(lena, "ShortbowButton");
            StrikeResolutionContext observed = null;
            yield return ExecuteSelectedStrike(lena, targetCell, value => observed = value);

            Assert.IsNotNull(observed);
            Assert.That(observed.Profile.ItemSlug, Is.EqualTo("shortbow"));
            Assert.That(observed.Profile.IsRangedAttack, Is.True);
            Assert.That(observed.FlatDamages, Is.Empty);
            Assert.That(observed.DamageDice.Count, Is.EqualTo(2));
            Assert.That(observed.DamageDice[1].damageType, Is.EqualTo("precision"));
            Assert.That(creature.GetAmmoQuantity("arrows"), Is.EqualTo(startingAmmo - 1));
            Assert.That(lena.GetComponent<ActionController>().ActionPoints, Is.EqualTo(2u));
            Assert.Less(target.GetComponent<CreatureComponent>().hp, 100);
        }

        [UnityTest]
        public IEnumerator LenaShortbowBlockedByWallDoesNotSpendActionOrAmmo()
        {
            yield return base.Setup();
            GameObject lena = null;
            yield return WaitForLena(value => lena = value);
            GameObject target = FindHostileTarget(lena);
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindEmptyStraightLine(tiles, 5, out Vector3Int lenaCell, out Vector3Int targetCell);
            MoveCombatant(tiles, lena, lenaCell);
            MoveCombatant(tiles, target, targetCell);
            PrepareTarget(target, -10, 100);
            tiles[lenaCell.x + 2, lenaCell.z] = null;

            CreatureComponent creature = lena.GetComponent<CreatureComponent>();
            int startingAmmo = creature.GetAmmoQuantity("arrows");
            yield return ForceTurnAndClickAction(lena, "ShortbowButton");
            OnHover.Invoke(new List<Vector3Int> { targetCell });
            grid.Fsm.CurrentState.Leftclick();
            yield return null;

            ActionController controller = lena.GetComponent<ActionController>();
            Assert.IsTrue(grid.Fsm.CurrentState is StateStrike);
            Assert.That(controller.ActionPoints, Is.EqualTo(3u));
            Assert.That(controller.StrikePenalty, Is.EqualTo(0u));
            Assert.That(creature.GetAmmoQuantity("arrows"), Is.EqualTo(startingAmmo));
            Assert.That(target.GetComponent<CreatureComponent>().hp, Is.EqualTo(100));
        }

        private IEnumerator WaitForLena(Action<GameObject> assign)
        {
            GameObject lena = null;
            yield return WaitUntilWithTimeout(timeout, () =>
            {
                foreach (CreatureComponent creature in UnityEngine.Object.FindObjectsByType<CreatureComponent>(FindObjectsSortMode.None))
                {
                    if (creature.name == "Lena")
                    {
                        lena = creature.gameObject;
                        break;
                    }
                }

                return lena != null
                    && lena.GetComponent<PlayerActionController>() != null
                    && lena.GetComponent<CreatureComponent>().Prepared != null
                    && HasAction(lena.GetComponent<ActionController>(), "Dogslicer")
                    && HasAction(lena.GetComponent<ActionController>(), "Shortbow");
            });

            Assert.IsNotNull(lena, "Expected UnitTestingScene to contain playable Lena rogue fixture.");
            assign(lena);
        }

        private IEnumerator ForceTurnAndClickAction(GameObject actor, string buttonName)
        {
            ActionController controller = actor.GetComponent<ActionController>();
            controller.StartTurn();
            OnNextTurn.Invoke(actor);

            Button actionButton = null;
            yield return WaitUntilWithTimeout(timeout, () =>
            {
                actionButton = root.Q<Button>(buttonName);
                return actionButton != null;
            });

            Assert.IsNotNull(actionButton, "Expected action button " + buttonName + " for Lena.");
            PushButton(actionButton);
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            yield return WaitUntilWithTimeout(timeout, () => grid.Fsm.CurrentState is StateStrike);
            Assert.IsTrue(grid.Fsm.CurrentState is StateStrike);
        }

        private IEnumerator ExecuteSelectedStrike(GameObject actor, Vector3Int targetCell, Action<StrikeResolutionContext> assignObservedStrike)
        {
            GridBase grid = UnityEngine.Object.FindFirstObjectByType<GridBase>();
            StrikeResolutionContext observed = null;
            UnityAction<StrikeResolutionContext> listener = context =>
            {
                if (context.AttackerObject == actor)
                    observed = context;
            };

            OnStrikePreparedEvent.AddListener(listener);
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(7604);
            OnHover.Invoke(new List<Vector3Int> { targetCell });
            grid.Fsm.CurrentState.Leftclick();
            yield return WaitUntilWithTimeout(timeout, () => observed != null && !actor.GetComponent<ActionController>().IsTakingAction);
            UnityEngine.Random.state = randomState;
            OnStrikePreparedEvent.RemoveListener(listener);

            Assert.IsNotNull(observed, "Expected Lena's Strike to execute through OnStrikePreparedEvent.");
            assignObservedStrike(observed);
        }

        private static bool HasAction(ActionController controller, string actionName)
        {
            if (controller == null)
                return false;

            foreach (EntityAction action in controller.GetActions())
            {
                if (action.ActionName == actionName)
                    return true;
            }
            return false;
        }

        private static void AssertHasAction(ActionController controller, string actionName)
        {
            Assert.IsTrue(HasAction(controller, actionName), "Expected action " + actionName + ".");
        }

        private static void AssertVisibleToken(GameObject actor)
        {
            MeshFilter[] filters = actor.GetComponentsInChildren<MeshFilter>();
            Assert.IsNotEmpty(filters);
            bool hasMesh = false;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh != null)
                {
                    hasMesh = true;
                    break;
                }
            }
            Assert.IsTrue(hasMesh, "Expected Lena token or base mesh to be assigned.");
        }

        private static GameObject FindHostileTarget(GameObject actor)
        {
            string actorTeam = actor.GetComponent<Team>().Name;
            foreach (ActionController controller in UnityEngine.Object.FindObjectsByType<ActionController>(FindObjectsSortMode.None))
            {
                GameObject candidate = controller.gameObject;
                if (candidate == actor)
                    continue;

                Team team = candidate.GetComponent<Team>();
                if (team != null && !TeamRules.GetInstance().IsFriendly(actorTeam, team.Name))
                    return candidate;
            }

            Assert.Fail("Expected a hostile target for Lena in UnitTestingScene.");
            return null;
        }

        private static GameObject FindFriendlyAlly(GameObject actor)
        {
            string actorTeam = actor.GetComponent<Team>().Name;
            foreach (ActionController controller in UnityEngine.Object.FindObjectsByType<ActionController>(FindObjectsSortMode.None))
            {
                GameObject candidate = controller.gameObject;
                if (candidate == actor)
                    continue;

                Team team = candidate.GetComponent<Team>();
                if (team != null && TeamRules.GetInstance().IsFriendly(actorTeam, team.Name))
                    return candidate;
            }

            return CreateFriendlyAlly(actor);
        }

        private static GameObject CreateFriendlyAlly(GameObject actor)
        {
            GameObject ally = new("Lena Flanking Test Ally");
            Team team = ally.AddComponent<Team>();
            team.Name = actor.GetComponent<Team>().Name;
            CreatureComponent creature = ally.AddComponent<CreatureComponent>();
            creature.strMod = 1;
            creature.hp = 10;
            creature.maxHp = 10;
            ally.AddComponent<Conditions>();
            TestActionController controller = ally.AddComponent<TestActionController>();
            controller.AddAction(new Unarmed(1,
                new List<Dice> { new Dice(1, 3, "Bludgeoning") },
                new List<DamageValue> { new DamageValue("Bludgeoning", creature.strMod) }));
            return ally;
        }

        private static void PrepareTarget(GameObject target, int ac, int hp)
        {
            CreatureComponent creature = target.GetComponent<CreatureComponent>();
            creature.equippedArmor = null;
            creature.armorBonuses = new List<ArmorBonus>();
            creature.ac = ac;
            creature.maxHp = hp;
            creature.hp = hp;
            if (target.GetComponent<Conditions>() == null)
                target.AddComponent<Conditions>();
        }

        private static void FindAdjacentOpenCells(Tile[,] tiles, out Vector3Int actorCell, out Vector3Int targetCell)
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0) - 1; x++)
                {
                    Tile first = tiles[x, z];
                    Tile second = tiles[x + 1, z];
                    if (first != null && second != null && first.Occupants.Count == 0 && second.Occupants.Count == 0)
                    {
                        actorCell = new Vector3Int(x, 0, z);
                        targetCell = new Vector3Int(x + 1, 0, z);
                        return;
                    }
                }
            }

            Assert.Fail("Could not find adjacent open cells in UnitTestingScene.");
            actorCell = Vector3Int.zero;
            targetCell = Vector3Int.zero;
        }

        private static void FindFlankingLine(Tile[,] tiles, out Vector3Int allyCell, out Vector3Int targetCell, out Vector3Int actorCell)
        {
            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                for (int x = 0; x < tiles.GetLength(0) - 2; x++)
                {
                    Tile first = tiles[x, z];
                    Tile second = tiles[x + 1, z];
                    Tile third = tiles[x + 2, z];
                    if (first != null && second != null && third != null
                        && first.Occupants.Count == 0 && second.Occupants.Count == 0 && third.Occupants.Count == 0)
                    {
                        allyCell = new Vector3Int(x, 0, z);
                        targetCell = new Vector3Int(x + 1, 0, z);
                        actorCell = new Vector3Int(x + 2, 0, z);
                        return;
                    }
                }
            }

            Assert.Fail("Could not find three adjacent open cells for flanking in UnitTestingScene.");
            allyCell = Vector3Int.zero;
            targetCell = Vector3Int.zero;
            actorCell = Vector3Int.zero;
        }

        private static void FindSameSideNonFlankingCells(Tile[,] tiles, out Vector3Int allyCell, out Vector3Int targetCell, out Vector3Int actorCell)
        {
            for (int z = 0; z < tiles.GetLength(1) - 1; z++)
            {
                for (int x = 0; x < tiles.GetLength(0) - 1; x++)
                {
                    Tile actorTile = tiles[x, z + 1];
                    Tile allyTile = tiles[x + 1, z];
                    Tile targetTile = tiles[x + 1, z + 1];
                    if (actorTile != null && allyTile != null && targetTile != null
                        && actorTile.Occupants.Count == 0 && allyTile.Occupants.Count == 0 && targetTile.Occupants.Count == 0)
                    {
                        actorCell = new Vector3Int(x, 0, z + 1);
                        allyCell = new Vector3Int(x + 1, 0, z);
                        targetCell = new Vector3Int(x + 1, 0, z + 1);
                        return;
                    }
                }
            }

            Assert.Fail("Could not find a same-side non-flanking block in UnitTestingScene.");
            allyCell = Vector3Int.zero;
            targetCell = Vector3Int.zero;
            actorCell = Vector3Int.zero;
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

        private sealed class TestActionController : ActionController
        {
            public override void EndTurn()
            {
            }
        }
    }
}
