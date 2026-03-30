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
    }
}