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

        /// <summary>
        /// Places a token in the grid if a tile exists there
        /// and its unoccupied. Token should be as close to
        /// grid aligned as possible
        /// </summary>
        /// <param name="token"></param>
        /// <param name="position"></param>
        /// <returns>True if placed</returns>
        public bool TryPlaceToken(GameObject token)
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

        public override IEnumerator Stride(GameObject character)
        {
            throw new System.NotImplementedException();
        }

        public override IEnumerator GetStrikeTarget(GameObject attacker, int range, CoroutineResult<GameObject> target)
        {
            throw new System.NotImplementedException();
        }

        public Tile[,] GetTiles()
        {
            return Tiles;
        }

        public bool PlaceToken(GameObject token)
        {
            Vector3Int cell = Vector3Int.RoundToInt(token.transform.position);
            if(Tiles.GetLength(0) > cell.x && Tiles.GetLength(1) > cell.z)
            {
                Tile t = Tiles[cell.x, cell.z];
                if (t == null)
                    return false;
                t.Occupant = token;
                return true;
            }
            return false;
        }
    }
}