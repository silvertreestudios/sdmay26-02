using System.Collections;
using UnityEngine;

// Public specifications
namespace GridPublic
{
    public abstract class GridAPI : SingletonMonoBehaviour<GridAPI>
    {
        /// <summary>
        /// The given character performs a stride action
        /// </summary>
        /// <param name="character"></param>
        /// <returns></returns>
        public abstract IEnumerator Stride(GameObject character);
        
        /// <summary>
        /// The given character performs a strike action
        /// </summary>
        /// <param name="attacker"></param>
        /// <param name="range">of the strike</param>
        /// <param name="target">selected target in range, null if none</param>
        /// <returns></returns>
        public abstract IEnumerator GetStrikeTarget(GameObject attacker, int range, CoroutineResult<GameObject> target);
    }
}

// Private specifications, intended to be inherited with GridAPI
namespace GridPrivate
{
    public interface GridAPIPrivate
    {
        /// <summary>
        /// Returns the tiles that define the grid
        /// </summary>
        /// <returns></returns>
        public Tile[,] GetTiles();

        /// <summary>
        /// Returns the Pathfinder for the grid
        /// </summary>
        /// <returns></returns>
        public IPathfinder GetPathfinder();

        /// <summary>
        /// Places a token on the board. DO NOT USE FOR MOVEMENT
        /// ONLY USE FOR PLACING TOKENS INITIALLY
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public bool AddToken(GameObject token);

        /// <summary>
        /// Removes a token from the board. DO NOT USE FOR MOVEMENT
        /// ONLY USE FOR REMOVING A TOKEN COMPLETELY FROM THE BOARD.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public bool DestroyToken(GameObject token);
    }
}