using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace TestsState
{
    public class AreaTargetingStateTests : PlayModeBase
    {
        [UnityTest]
        public IEnumerator GridApiAreaTargetPreviewsAndConfirmsLinePlacement()
        {
            yield return base.Setup();

            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            FindClearHorizontalRun(tiles, 4, out Vector3Int start);
            GameObject player = CreateToken("area target actor");
            MoveCombatant(tiles, player, start);

            CoroutineResult<AreaTargetResult> result = new();
            List<Vector3Int> preview = null;
            UnityAction<List<Vector3Int>> previewListener = cells =>
                preview = new List<Vector3Int>(cells);
            OnPreviewArea.AddListener(previewListener);

            try
            {
                grid.StartCoroutine(
                    GridAPI
                        .GetInstance()
                        .GetAreaTarget(
                            player,
                            new AreaTargetRequest { Shape = AreaShape.Line, SizeFeet = 10 },
                            result
                        )
                );

                yield return WaitUntilWithTimeout(
                    timeout,
                    () => grid.Fsm.CurrentState is StateAreaTarget
                );
                Assert.IsTrue(
                    grid.Fsm.CurrentState is StateAreaTarget,
                    "GridAPI should enter StateAreaTarget."
                );

                Vector3Int hoverCell = start + new Vector3Int(1, 0, 0);
                OnGridHover.Invoke(
                    new GridHoverInfo
                    {
                        Cell = hoverCell,
                        WorldPosition = new Vector3(hoverCell.x, hoverCell.y, hoverCell.z),
                        NearestCorner = new Vector2Int(hoverCell.x, hoverCell.z),
                    }
                );
                yield return null;

                CollectionAssert.AreEqual(
                    new[] { start + new Vector3Int(1, 0, 0), start + new Vector3Int(2, 0, 0) },
                    preview
                );

                grid.Fsm.CurrentState.Leftclick();
                yield return WaitUntilWithTimeout(
                    timeout,
                    () => grid.Fsm.CurrentState is StateIdle
                );

                Assert.IsTrue(grid.Fsm.CurrentState is StateIdle);
                Assert.IsNotNull(result.Value);
                CollectionAssert.AreEqual(preview, result.Value.Cells);
            }
            finally
            {
                OnPreviewArea.RemoveListener(previewListener);
                Object.DestroyImmediate(player);
            }
        }

        [UnityTest]
        public IEnumerator GridInputHoverCapturesAreaPreviewOncePerFrame()
        {
            yield return base.Setup();

            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            GridInput input = Object.FindFirstObjectByType<GridInput>();
            Assert.IsNotNull(grid);
            Assert.IsNotNull(input);
            Tile[,] tiles = grid.GetTiles();
            FindClearHorizontalRun(tiles, 4, out Vector3Int start);
            CoroutineResult<AreaTargetResult> result = new();
            int previewCount = 0;
            UnityAction<List<Vector3Int>> previewListener = _ => previewCount++;
            OnPreviewArea.AddListener(previewListener);
            Mouse previousMouse = Mouse.current;
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            try
            {
                grid.StartCoroutine(
                    grid.GetAreaTarget(
                        new AreaTargetSource(start),
                        new AreaTargetRequest { Shape = AreaShape.Line, SizeFeet = 10 },
                        result
                    )
                );
                yield return WaitUntilWithTimeout(
                    timeout,
                    () => grid.Fsm.CurrentState is StateAreaTarget
                );
                Assert.IsInstanceOf<StateAreaTarget>(grid.Fsm.CurrentState);
                Assert.IsFalse(HUDController.IsPointerOverHUD);

                Vector3Int hoverCell = start + Vector3Int.right;
                Vector3 worldPosition = new(
                    hoverCell.x + 0.1f,
                    input.transform.position.y,
                    hoverCell.z + 0.1f
                );
                Vector3 screenPosition = Camera.main.WorldToScreenPoint(worldPosition);
                mouse.MakeCurrent();
                InputState.Change(
                    mouse.position,
                    new Vector2(screenPosition.x, screenPosition.y),
                    InputUpdateType.Dynamic
                );
                MethodInfo update = typeof(GridInput).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.IsNotNull(update);

                update.Invoke(input, null);
                Assert.AreEqual(1, previewCount);

                update.Invoke(input, null);
                Assert.AreEqual(
                    2,
                    previewCount,
                    "A stationary valid-cell frame should produce one precise area preview."
                );
            }
            finally
            {
                bool returnedToIdle =
                    grid.Fsm.CurrentState is StateIdle || grid.Fsm.ChangeState(grid.Fsm.IdleState);
                int previewCountAfterExit = previewCount;
                OnGridHover.Invoke(
                    new GridHoverInfo
                    {
                        Cell = start + Vector3Int.right,
                        WorldPosition = start + Vector3Int.right,
                        NearestCorner = new Vector2Int(start.x + 1, start.z),
                    }
                );
                bool retainedTargetingListener = previewCount != previewCountAfterExit;
                OnPreviewArea.RemoveListener(previewListener);
                InputSystem.RemoveDevice(mouse);
                if (previousMouse != null && previousMouse.added)
                    previousMouse.MakeCurrent();
                Assert.IsTrue(returnedToIdle, "The targeting state should cancel during cleanup.");
                Assert.IsFalse(
                    retainedTargetingListener,
                    "Leaving area targeting should remove its static hover listener."
                );
            }
        }

        [UnityTest]
        public IEnumerator GridApiAreaTargetSupportsSourceCellWithoutActorObject()
        {
            yield return base.Setup();

            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Assert.IsNotNull(grid);
            Tile[,] tiles = grid.GetTiles();
            FindClearHorizontalRun(tiles, 1, out Vector3Int start);
            CoroutineResult<AreaTargetResult> result = new();

            grid.StartCoroutine(
                GridAPI
                    .GetInstance()
                    .GetAreaTarget(
                        new AreaTargetSource(start),
                        new AreaTargetRequest
                        {
                            Shape = AreaShape.Emanation,
                            SizeFeet = 10,
                            IncludeCenter = true,
                        },
                        result
                    )
            );

            yield return WaitUntilWithTimeout(
                timeout,
                () => grid.Fsm.CurrentState is StateAreaTarget
            );
            Assert.IsTrue(grid.Fsm.CurrentState is StateAreaTarget);

            grid.Fsm.CurrentState.Rightclick();
            yield return WaitUntilWithTimeout(timeout, () => grid.Fsm.CurrentState is StateIdle);

            Assert.IsTrue(grid.Fsm.CurrentState is StateIdle);
            Assert.IsNull(result.Value);
        }

        [UnityTest]
        public IEnumerator ConfirmationRefreshesChangedMembershipBeforeAccepting()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindClearHorizontalRun(tiles, 4, out Vector3Int start);
            GameObject target = CreateToken("new candidate after preview");
            CoroutineResult<AreaTargetResult> result = new();
            try
            {
                grid.StartCoroutine(
                    grid.GetAreaTarget(
                        new AreaTargetSource(start),
                        new AreaTargetRequest { Shape = AreaShape.Emanation, SizeFeet = 10 },
                        result
                    )
                );
                yield return WaitUntilWithTimeout(
                    timeout,
                    () => grid.Fsm.CurrentState is StateAreaTarget
                );
                MoveCombatant(tiles, target, start + Vector3Int.right);
                grid.Fsm.CurrentState.Leftclick();
                Assert.IsInstanceOf<StateAreaTarget>(
                    grid.Fsm.CurrentState,
                    "Changed candidates require another confirmation."
                );
                Assert.IsNull(result.Value);
                grid.Fsm.CurrentState.Leftclick();
                Assert.IsInstanceOf<StateIdle>(grid.Fsm.CurrentState);
                Assert.IsNotNull(result.Value);
                Assert.IsTrue(result.Value.Creatures.Exists(entry => entry.Creature == target));
                Assert.AreNotEqual(System.Guid.Empty, result.Value.SnapshotId);
            }
            finally
            {
                foreach (Tile tile in tiles)
                    tile?.Occupants.Remove(target);
                Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator DestroyedSourceCancelsInsteadOfFallingBackToItsOldCell()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindClearHorizontalRun(tiles, 1, out Vector3Int start);
            GameObject actor = CreateToken("vanishing source");
            actor.transform.position = start;
            CoroutineResult<AreaTargetResult> result = new();
            grid.StartCoroutine(
                grid.GetAreaTarget(
                    new AreaTargetSource(actor),
                    new AreaTargetRequest { Shape = AreaShape.Emanation, SizeFeet = 10 },
                    result
                )
            );
            yield return WaitUntilWithTimeout(
                timeout,
                () => grid.Fsm.CurrentState is StateAreaTarget
            );
            Object.DestroyImmediate(actor);
            grid.Fsm.CurrentState.Leftclick();
            Assert.IsInstanceOf<StateIdle>(grid.Fsm.CurrentState);
            Assert.IsNull(result.Value);
        }

        [UnityTest]
        public IEnumerator DestroyedSourceBeforeDeferredStateConstructionCancels()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindClearHorizontalRun(tiles, 1, out Vector3Int start);
            GameObject actor = CreateToken("source destroyed before targeting starts");
            actor.transform.position = start;
            CoroutineResult<AreaTargetResult> result = new();
            IEnumerator targeting = grid.GetAreaTarget(
                actor,
                new AreaTargetRequest
                {
                    Shape = AreaShape.Emanation,
                    SizeFeet = 10,
                    IncludeCenter = true,
                },
                result
            );

            Object.DestroyImmediate(actor);
            grid.StartCoroutine(targeting);
            yield return WaitUntilWithTimeout(
                timeout,
                () => grid.Fsm.CurrentState is StateAreaTarget
            );
            Assert.IsInstanceOf<StateAreaTarget>(grid.Fsm.CurrentState);

            grid.Fsm.CurrentState.Leftclick();

            Assert.IsInstanceOf<StateIdle>(grid.Fsm.CurrentState);
            Assert.IsNull(result.Value);
        }

        [UnityTest]
        public IEnumerator SourceMovementAndCandidateDestructionRequireNewConfirmation()
        {
            yield return base.Setup();
            GridBase grid = Object.FindFirstObjectByType<GridBase>();
            Tile[,] tiles = grid.GetTiles();
            FindClearHorizontalRun(tiles, 4, out Vector3Int start);
            GameObject actor = CreateToken("moving source");
            GameObject target = CreateToken("vanishing candidate");
            actor.transform.position = start;
            MoveCombatant(tiles, target, start + Vector3Int.right);
            CoroutineResult<AreaTargetResult> result = new();
            try
            {
                grid.StartCoroutine(
                    grid.GetAreaTarget(
                        new AreaTargetSource(actor),
                        new AreaTargetRequest { Shape = AreaShape.Emanation, SizeFeet = 10 },
                        result
                    )
                );
                yield return WaitUntilWithTimeout(
                    timeout,
                    () => grid.Fsm.CurrentState is StateAreaTarget
                );
                actor.transform.position = start + Vector3Int.right;
                grid.Fsm.CurrentState.Leftclick();
                Assert.IsInstanceOf<StateAreaTarget>(grid.Fsm.CurrentState);
                Assert.IsNull(result.Value);
                // Move the candidate to a present non-center cell and refresh the preview by click.
                MoveCombatant(tiles, target, start + Vector3Int.right * 2);
                grid.Fsm.CurrentState.Leftclick();
                Assert.IsInstanceOf<StateAreaTarget>(grid.Fsm.CurrentState);
                Object.DestroyImmediate(target);
                grid.Fsm.CurrentState.Leftclick();
                Assert.IsInstanceOf<StateAreaTarget>(grid.Fsm.CurrentState);
                Assert.IsNull(result.Value);
                grid.Fsm.CurrentState.Leftclick();
                Assert.IsInstanceOf<StateIdle>(grid.Fsm.CurrentState);
                Assert.AreEqual(start + Vector3Int.right, result.Value.Placement.OriginCell);
                Assert.IsTrue(result.Value.Creatures.TrueForAll(entry => entry.Creature != null));
            }
            finally
            {
                foreach (Tile tile in tiles)
                    tile?.Occupants.Remove(target);
                if (target != null)
                    Object.DestroyImmediate(target);
                Object.DestroyImmediate(actor);
            }
        }

        private static void FindClearHorizontalRun(Tile[,] tiles, int length, out Vector3Int start)
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

                    if (clear)
                    {
                        start = new Vector3Int(x, 0, z);
                        return;
                    }
                }
            }

            Assert.Fail("Could not find a clear horizontal run in UnitTestingScene.");
            start = Vector3Int.zero;
        }

        private static GameObject CreateToken(string name)
        {
            return new GameObject(name);
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
            tiles[cell.x, cell.z].Occupants.Add(combatant);
        }
    }
}
