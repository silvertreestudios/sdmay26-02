using System.Collections;
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

        public IPathfinder GetPathfinder()
        {
            return Pathfinder;
        }

        protected override void Awake()
        {
            Map map = GetComponent<Map>();
            GridData = map.GetMapData();
            LineOfSightBlocks = GridData == null ? null : map.GetLineOfSightBlocks();
            if (GridData == null || LineOfSightBlocks == null)
            {
                enabled = false;
                GridInput input = GetComponent<GridInput>();
                if (input != null)
                    input.enabled = false;
                return;
            }

            base.Awake();
            Tiles = new Tile[GridData.GetLength(0), GridData.GetLength(1)];

            for(int x = 0; x < GridData.GetLength(0); x++)
            {
                for (int y = 0; y < GridData.GetLength(1); y++)
                {
                    Tiles[x, y] = IsWalkableTile(GridData[x, y]) ? new Tile() : null;
                }
            }

            Pathfinder = new Dijkstra(Tiles);
            GridLineOfSightData.Register(Tiles, LineOfSightBlocks, GridData);
            foreach (Token token in FindObjectsByType<Token>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                token.TryRegisterWithGrid(this);
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
