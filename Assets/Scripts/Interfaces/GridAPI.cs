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
        /// The given character selects a target for a Strike-style action.
        /// </summary>
        /// <param name="attacker"></param>
        /// <param name="request">targeting constraints for the Strike</param>
        /// <param name="target">selected target result, null if canceled or illegal</param>
        /// <returns></returns>
        public abstract IEnumerator GetStrikeTarget(GameObject attacker, StrikeTargetRequest request, CoroutineResult<StrikeTargetResult> target);

        /// <summary>
        /// The given character selects or confirms an area template placement.
        /// </summary>
        /// <param name="actor">character placing the area</param>
        /// <param name="request">area targeting constraints</param>
        /// <param name="target">selected area result, null if canceled or illegal</param>
        /// <returns></returns>
        public virtual IEnumerator GetAreaTarget(GameObject actor, AreaTargetRequest request, CoroutineResult<AreaTargetResult> target)
        {
            return GetAreaTarget(new AreaTargetSource(actor), request, target);
        }

        /// <summary>
        /// Selects or confirms an area template placement from a grid source, such as a trap or environmental effect.
        /// </summary>
        /// <param name="source">source object or source cell placing the area</param>
        /// <param name="request">area targeting constraints</param>
        /// <param name="target">selected area result, null if canceled or illegal</param>
        /// <returns></returns>
        public abstract IEnumerator GetAreaTarget(AreaTargetSource source, AreaTargetRequest request, CoroutineResult<AreaTargetResult> target);

        /// <summary>
        /// Destroys a gameobject from the grid
        /// </summary>
        /// <param name="token">Token to remove</param>
        /// <returns></returns>
        public abstract bool DestroyToken(GameObject token);
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
        /// Returns cells that block line of sight independently from movement walkability.
        /// </summary>
        public bool[,] GetLineOfSightBlocks();

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
