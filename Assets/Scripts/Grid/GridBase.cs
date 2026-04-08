using System.Collections;
using UnityEngine;
using GridPublic;
using System.Collections.Generic;

namespace GridPrivate
{
    [RequireComponent(typeof(Map))]
    [RequireComponent(typeof(GridInput))]
    public class GridBase : GridAPI, GridAPIPrivate
    {
        public TileType[,] GridData {get; set;}
        protected Tile[,] Tiles;
        protected Dictionary<GameObject, Vector3Int> Tokens = new();
        IPathfinder Pathfinder;

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

        public bool PlaceToken(GameObject token)
        {
            // Token cannot exist in multiple positions
            if (Tokens.ContainsKey(token))
                return false;
            Vector3Int position = Vector3Int.RoundToInt(token.transform.position);
            Tile tile = Tiles[position.x, position.z];
            if(tile == null || tile.Occupant != null)
                return false;
            tile.Occupant = token;
            Tokens.Add(token, position);
            tile.OnEnterTile.Invoke(token, position);
            return true;
        }

        /// <summary>
        /// Removes a token from the grid
        /// </summary>
        /// <param name="token"></param>
        public void RemoveToken(GameObject token) 
        {
            Vector3Int position;
            if(Tokens.TryGetValue(token, out position))
            {
                Tile tile = Tiles[position.x, position.z];
                tile.Occupant = null;
                Tokens.Remove(token);
                tile.OnExitTile.Invoke(token, position);
            }
        }

        /// <summary>
        /// wrapper for stride state, tranistions FSM to stride
        /// </summary>
        public override IEnumerator Stride(GameObject character)
        {

            yield return null;
        }

        public override IEnumerator GetStrikeTarget(GameObject attacker, int range, CoroutineResult<GameObject> target)
        {
            throw new System.NotImplementedException();
        }

        public Tile[,] GetTiles()
        {
            return Tiles;
        }
    }
}