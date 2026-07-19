using UnityEngine;

namespace GridPrivate
{
    public sealed class OpenDoorway : MonoBehaviour, IOnGridGeneration
    {
        public int ResolvedRotation { get; private set; }

        public void OnGeneration(Vector3Int position, in TileType[,] gridData)
        {
            bool northSouth =
                WallStructuralResolver.IsStructure(gridData, position.x, position.z + 1)
                || WallStructuralResolver.IsStructure(gridData, position.x, position.z - 1);
            bool eastWest =
                WallStructuralResolver.IsStructure(gridData, position.x + 1, position.z)
                || WallStructuralResolver.IsStructure(gridData, position.x - 1, position.z);

            ResolvedRotation = northSouth && !eastWest ? 90 : 0;
            transform.rotation = Quaternion.Euler(0f, ResolvedRotation, 0f);
        }
    }
}
