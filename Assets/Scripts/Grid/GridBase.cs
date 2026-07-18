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
            Map map = GetComponent<Map>();
            TileType[,] gridData = map.GetMapData();
            bool[,] lineOfSightBlocks = gridData == null ? null : map.GetLineOfSightBlocks();
            if (!TryRebindMapData(gridData, lineOfSightBlocks))
            {
                Debug.LogError("Grid initialization failed: Map did not provide valid grid and line-of-sight data.", this);
                enabled = false;
                GridInput input = GetComponent<GridInput>();
                if (input != null)
                    input.enabled = false;
                return;
            }
        }

        /// <summary>
        /// Checks whether runtime map replacement can bind the supplied arrays without
        /// interrupting an uncancelable action or displacing another grid singleton.
        /// </summary>
        internal bool CanRebindMapData(TileType[,] gridData, bool[,] lineOfSightBlocks)
        {
            if (gridData == null || lineOfSightBlocks == null ||
                gridData.GetLength(0) != lineOfSightBlocks.GetLength(0) ||
                gridData.GetLength(1) != lineOfSightBlocks.GetLength(1))
            {
                return false;
            }

            return Fsm.CanResetForGridRebind &&
                   (!GridAPI.TryGetInstance(out GridAPI activeGrid) || activeGrid == this);
        }

        /// <summary>
        /// Replaces every live grid consumer after validating that the operation can complete.
        /// Duplicate grids retain the singleton base class's destruction behavior.
        /// </summary>
        internal bool TryRebindMapData(TileType[,] gridData, bool[,] lineOfSightBlocks)
        {
            bool hasActiveGrid = GridAPI.TryGetInstance(out GridAPI activeGrid);
            if (hasActiveGrid && activeGrid != this)
            {
                base.Awake();
                return false;
            }
            if (!CanRebindMapData(gridData, lineOfSightBlocks) ||
                !Fsm.TryResetForGridRebind())
                return false;
            if (!hasActiveGrid)
                base.Awake();
            if (!GridAPI.TryGetInstance(out activeGrid) || activeGrid != this)
                return false;

            Tile[,] replacementTiles = new Tile[gridData.GetLength(0), gridData.GetLength(1)];
            for (int x = 0; x < gridData.GetLength(0); x++)
            {
                for (int z = 0; z < gridData.GetLength(1); z++)
                    replacementTiles[x, z] = IsWalkableTile(gridData[x, z]) ? new Tile() : null;
            }

            GridLineOfSightData.Unregister(Tiles);
            GridData = gridData;
            LineOfSightBlocks = lineOfSightBlocks;
            Tiles = replacementTiles;
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

            foreach (Token token in FindObjectsByType<Token>(
                         FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                token.RebindToGrid(this);
            }
            foreach (MindlessController controller in FindObjectsByType<MindlessController>(
                         FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                controller.RebindGrid(this);
            }
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

        public override bool DestroyToken(GameObject token)
        {
            Debug.Log("Destroying: " + token);
            Vector3Int position = Vector3Int.RoundToInt(token.transform.position);
            Tile tile = Tiles[position.x, position.z];
            if (tile == null || tile.Occupants.Count == 0)
                return false;
            tile.Occupants.Remove(token);
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
