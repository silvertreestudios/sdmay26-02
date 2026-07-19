using System.Collections;
using System.Collections.Generic;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
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
