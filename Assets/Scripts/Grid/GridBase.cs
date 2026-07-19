using System.Collections;
using Game.DungeonGeneration;
using UnityEngine;
using GridPublic;
using System.Collections.Generic;
using UnityEngine.TextCore.Text;

namespace GridPrivate
{
    [RequireComponent(typeof(Map))]
    [RequireComponent(typeof(GridInput))]
    public class GridBase : GridAPI, GridAPIPrivate
    {
        private readonly struct PreparedTokenRebind
        {
            public Token Token { get; }
            public bool RegistersImmediately { get; }

            public PreparedTokenRebind(Token token, bool registersImmediately)
            {
                Token = token;
                RegistersImmediately = registersImmediately;
            }
        }

        private sealed class GridRebindPlan
        {
            public static GridRebindPlan Invalid { get; } = new(
                new TileType[0, 0],
                new bool[0, 0],
                new Tile[0, 0],
                new PreparedTokenRebind[0],
                new MindlessController[0]);

            public TileType[,] GridData { get; }
            public bool[,] LineOfSightBlocks { get; }
            public Tile[,] Tiles { get; }
            public IReadOnlyList<PreparedTokenRebind> TokenRebinds { get; }
            public IReadOnlyList<MindlessController> Controllers { get; }

            public GridRebindPlan(
                TileType[,] gridData,
                bool[,] lineOfSightBlocks,
                Tile[,] tiles,
                IEnumerable<PreparedTokenRebind> tokenRebinds,
                IEnumerable<MindlessController> controllers)
            {
                GridData = gridData;
                LineOfSightBlocks = lineOfSightBlocks;
                Tiles = tiles;
                TokenRebinds = new List<PreparedTokenRebind>(tokenRebinds);
                Controllers = new List<MindlessController>(controllers);
            }
        }

        public TileType[,] GridData {get; set;}
        public bool[,] LineOfSightBlocks { get; private set; }
        protected Tile[,] Tiles;
        IPathfinder Pathfinder;
        public GridFSM Fsm { get; private set; } = new GridFSM();

        /// <summary>Gets whether grid tiles and pathfinding have been initialized from map data.</summary>
        public bool IsInitialized => Tiles != null && GridData != null && LineOfSightBlocks != null;

        public IPathfinder GetPathfinder()
        {
            return Pathfinder;
        }

        protected override void Awake()
        {
            if (GridAPI.TryGetInstance(out GridAPI activeGrid) && activeGrid != this)
            {
                base.Awake();
                return;
            }

            Map map = GetComponent<Map>();
            TileType[,] gridData = map.GetMapData();
            bool[,] lineOfSightBlocks = gridData == null ? null : map.GetLineOfSightBlocks();
            if (!TryRebindMapData(gridData, lineOfSightBlocks, out string failure))
            {
                Debug.LogError($"Grid initialization failed: {failure}", this);
                enabled = false;
                GridInput input = GetComponent<GridInput>();
                if (input != null)
                    input.enabled = false;
                return;
            }
        }

        private bool TryPrepareGridRebind(
            TileType[,] gridData,
            bool[,] lineOfSightBlocks,
            out GridRebindPlan plan,
            out string failure)
        {
            failure = string.Empty;
            if (gridData == null)
            {
                plan = GridRebindPlan.Invalid;
                failure = "Replacement grid data is missing.";
                return false;
            }
            if (lineOfSightBlocks == null)
            {
                plan = GridRebindPlan.Invalid;
                failure = "Replacement line-of-sight data is missing.";
                return false;
            }
            if (gridData.GetLength(0) != lineOfSightBlocks.GetLength(0) ||
                gridData.GetLength(1) != lineOfSightBlocks.GetLength(1))
            {
                plan = GridRebindPlan.Invalid;
                failure =
                    $"Replacement grid dimensions {gridData.GetLength(0)}x{gridData.GetLength(1)} " +
                    $"do not match line-of-sight dimensions " +
                    $"{lineOfSightBlocks.GetLength(0)}x{lineOfSightBlocks.GetLength(1)}.";
                return false;
            }

            if (!Fsm.CanResetForGridRebind)
            {
                plan = GridRebindPlan.Invalid;
                failure = "The grid is processing an action and cannot be reset for replacement.";
                return false;
            }

            Tile[,] replacementTiles = new Tile[gridData.GetLength(0), gridData.GetLength(1)];
            for (int x = 0; x < gridData.GetLength(0); x++)
            {
                for (int z = 0; z < gridData.GetLength(1); z++)
                    replacementTiles[x, z] = IsWalkableTile(gridData[x, z]) ? new Tile() : null;
            }

            List<PreparedTokenRebind> tokenRebinds = new();
            HashSet<Vector2Int> occupiedCells = new();
            foreach (Token token in FindObjectsByType<Token>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (!token.TryGetRebindCell(this, out Vector3Int cell))
                    continue;
                if (cell.x < 0 || cell.z < 0 ||
                    cell.x >= gridData.GetLength(0) || cell.z >= gridData.GetLength(1))
                {
                    plan = GridRebindPlan.Invalid;
                    failure =
                        $"Token '{token.name}' at cell ({cell.x}, {cell.z}) is outside " +
                        $"replacement bounds {gridData.GetLength(0)}x{gridData.GetLength(1)}.";
                    return false;
                }
                if (!IsWalkableTile(gridData[cell.x, cell.z]))
                {
                    plan = GridRebindPlan.Invalid;
                    failure =
                        $"Token '{token.name}' cannot occupy non-walkable replacement cell " +
                        $"({cell.x}, {cell.z}).";
                    return false;
                }
                if (!occupiedCells.Add(new Vector2Int(cell.x, cell.z)))
                {
                    plan = GridRebindPlan.Invalid;
                    failure =
                        $"Token '{token.name}' cannot occupy replacement cell " +
                        $"({cell.x}, {cell.z}) because another token already reserves it.";
                    return false;
                }

                bool registersImmediately = token.isActiveAndEnabled;
                if (registersImmediately)
                    replacementTiles[cell.x, cell.z].Occupants.Add(token.gameObject);
                tokenRebinds.Add(new PreparedTokenRebind(token, registersImmediately));
            }

            List<MindlessController> controllers = new();
            foreach (MindlessController controller in FindObjectsByType<MindlessController>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                // A controller already owned by another grid is outside this transaction and
                // must neither block preparation nor receive the commit.
                if (!controller.CanBindToGrid(this))
                    continue;
                if (!controller.CanRebindGrid())
                {
                    plan = GridRebindPlan.Invalid;
                    failure =
                        $"AI controller '{controller.name}' has pending turn or action work " +
                        "and cannot rebind to the replacement grid.";
                    return false;
                }
                controllers.Add(controller);
            }

            plan = new GridRebindPlan(
                gridData,
                lineOfSightBlocks,
                replacementTiles,
                tokenRebinds,
                controllers);
            return true;
        }

        /// <summary>
        /// Replaces every live grid consumer after validating and preparing every operation
        /// that could otherwise fail after the live arrays are swapped.
        /// </summary>
        /// <param name="gridData">The replacement movement topology.</param>
        /// <param name="lineOfSightBlocks">The matching replacement visibility topology.</param>
        /// <param name="failure">
        /// A specific pre-commit reason when the replacement is rejected; otherwise an empty string.
        /// </param>
        /// <returns>
        /// <see langword="false"/> before any live grid state changes when preparation is unsafe;
        /// otherwise <see langword="true"/> after the non-failing commit completes.
        /// </returns>
        internal bool TryRebindMapData(
            TileType[,] gridData,
            bool[,] lineOfSightBlocks,
            out string failure)
        {
            failure = string.Empty;
            bool hasActiveGrid = GridAPI.TryGetInstance(out GridAPI activeGrid);
            if (hasActiveGrid && activeGrid != this)
            {
                failure = "A different grid is already active and owns the live grid binding.";
                return false;
            }
            if (!TryPrepareGridRebind(
                    gridData,
                    lineOfSightBlocks,
                    out GridRebindPlan plan,
                    out failure))
            {
                return false;
            }
            if (!Fsm.TryResetForGridRebind())
            {
                failure = "The grid stopped being resettable before replacement could commit.";
                return false;
            }
            if (!hasActiveGrid)
                base.Awake();

            GridLineOfSightData.Unregister(Tiles);
            GridData = plan.GridData;
            LineOfSightBlocks = plan.LineOfSightBlocks;
            Tiles = plan.Tiles;
            Pathfinder = new Dijkstra(Tiles);
            GridLineOfSightData.Register(Tiles, LineOfSightBlocks, GridData);

            GridInput input = GetComponent<GridInput>();
            if (input != null)
            {
                input.RebindTiles(Tiles);
                input.enabled = true;
            }
            enabled = true;
            GetComponent<GridVisuals>()?.RebindTiles(Tiles);

            foreach (PreparedTokenRebind tokenRebind in plan.TokenRebinds)
            {
                tokenRebind.Token.CommitPreparedGridRebind(
                    this,
                    tokenRebind.RegistersImmediately);
            }
            foreach (MindlessController controller in plan.Controllers)
                controller.RebindGrid(this);
            GetComponent<AuraGridVisuals>()?.Refresh();
            return true;
        }

        protected void OnDestroy()
        {
            GridLineOfSightData.Unregister(Tiles);
        }

        public void Update()
        {
            Fsm.InputUpdate();
        }

        public bool AddToken(GameObject token)
        {
            Vector3Int position = Vector3Int.RoundToInt(token.transform.position);
            Tile tile = Tiles[position.x, position.z];
            if(tile == null || tile.Occupants.Count > 0)
                return false;
            tile.Occupants.Add(token);
            return true;
        }

        /// <inheritdoc/>
        public override bool DestroyToken(GameObject token)
        {
            Debug.Log("Destroying: " + token);
            Vector3Int position = Vector3Int.RoundToInt(token.transform.position);
            Tile tile = Tiles[position.x, position.z];
            if (tile == null || tile.Occupants.Count == 0)
                return false;
            if (!tile.Occupants.Remove(token))
                return false;

            token.GetComponent<Token>()?.DetachFromGrid(this);
            return true;
        }

        /// <summary>
        /// wrapper for stride state, tranistions FSM to stride
        /// </summary>
        public override IEnumerator Stride(GameObject character)
        {
            if (Fsm.ChangeState(new StateStride(character, Fsm)))
                yield return new WaitUntil(() => Fsm.CurrentState is StateIdle);
        }

        public override IEnumerator GetStrikeTarget(GameObject attacker, StrikeTargetRequest request, CoroutineResult<StrikeTargetResult> target)
        {
            if (Fsm.ChangeState(new StateStrike(attacker, request, target, Fsm)))
                yield return new WaitUntil(() => Fsm.CurrentState is StateIdle);
        }


        public override IEnumerator GetAreaTarget(AreaTargetSource source, AreaTargetRequest request, CoroutineResult<AreaTargetResult> target)
        {
            if (Fsm.ChangeState(new StateAreaTarget(source, request, target, Fsm)))
                yield return new WaitUntil(() => Fsm.CurrentState is StateIdle);
        }

        public Tile[,] GetTiles()
        {
            return Tiles;
        }

        /// <summary>
        /// Applies a generated door state to the shared navigation and line-of-sight arrays.
        /// </summary>
        /// <param name="cell">The validated generated door cell.</param>
        /// <param name="isOpen">Whether the cell should be passable and transparent.</param>
        /// <returns>
        /// <see langword="true"/> when the state is applied; otherwise <see langword="false"/>
        /// when the cell is invalid, is not a door, or an occupant prevents closing it.
        /// </returns>
        public bool TrySetDoorState(DungeonCell cell, bool isOpen)
        {
            if (GridData == null || LineOfSightBlocks == null ||
                cell.X < 0 || cell.Z < 0 ||
                cell.X >= GridData.GetLength(0) || cell.Z >= GridData.GetLength(1))
            {
                return false;
            }

            TileType current = GridData[cell.X, cell.Z];
            if (current != TileType.Door && current != TileType.ClosedDoor)
                return false;

            if (Tiles != null && !isOpen)
            {
                Tile tile = Tiles[cell.X, cell.Z];
                if (tile != null && tile.Occupants.Count > 0)
                    return false;
            }

            GridData[cell.X, cell.Z] = isOpen ? TileType.Door : TileType.ClosedDoor;
            LineOfSightBlocks[cell.X, cell.Z] = !isOpen;
            if (Tiles != null)
            {
                if (isOpen && Tiles[cell.X, cell.Z] == null)
                    Tiles[cell.X, cell.Z] = new Tile();
                else if (!isOpen)
                    Tiles[cell.X, cell.Z] = null;
            }
            if (Pathfinder is Dijkstra dijkstra)
                dijkstra.InvalidateTopology();

            return true;
        }

        public bool[,] GetLineOfSightBlocks()
        {
            return LineOfSightBlocks;
        }

        public static bool IsWalkableTile(TileType tile)
        {
            return tile == TileType.Ground || tile == TileType.Door;
        }
    }
}
