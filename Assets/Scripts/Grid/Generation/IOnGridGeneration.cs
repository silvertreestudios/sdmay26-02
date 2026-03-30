using UnityEngine;

namespace GridPrivate
{
    /// <summary>
    /// Interface for objects that need special
    /// functionality on spawning in the grid
    /// </summary>
    public interface IOnGridGeneration
    {
        public void OnGeneration(Vector3Int position, in TileType[,] gridData);
    }
}
