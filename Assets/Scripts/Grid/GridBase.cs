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
        protected Tile[,] Tiles;
        IPathfinder Pathfinder;
        GridFSM Fsm = new GridFSM();

        public IPathfinder GetPathfinder()
        {
            return Pathfinder;
        }

        protected override void Awake()
        {
            base.Awake();
            Map map = GetComponent<Map>();
            GridData = map.GetMapData();
            Tiles = new Tile[GridData.GetLength(0), GridData.GetLength(1)];

            for(int x = 0; x < GridData.GetLength(0); x++) 
            {
                for (int y = 0; y < GridData.GetLength(1); y++)
                {
                    switch(GridData[x,y])
                    {
                        case TileType.Ground:
                        case TileType.Door:
                            Tiles[x, y] = new Tile();
                            break;
                        default: // Forever uninhabitable, purely cosmetic and/or padding
                            Tiles[x, y] = null;
                            break;
                    }
                }
            }

            Pathfinder = new Dijkstra(Tiles);
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

        public override IEnumerator GetStrikeTarget(GameObject attacker, float range, CoroutineResult<GameObject> target)
        {
            if (Fsm.ChangeState(new StateStrike(attacker, range, target, Fsm)))
                yield return new WaitUntil(() => Fsm.CurrentState is StateIdle);
        }

        public Tile[,] GetTiles()
        {
            return Tiles;
        }
    }
}