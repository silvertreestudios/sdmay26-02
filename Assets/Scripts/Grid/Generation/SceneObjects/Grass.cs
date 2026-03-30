using UnityEngine;
using GridPrivate;
using UnityEngine.UIElements;

namespace GridPrivate
{
    public class Grass : MonoBehaviour, IOnGridGeneration
    {
        [SerializeField]
        protected float Scale;
        [SerializeField]
        protected float Threshold;

        public void OnGeneration(Vector3Int position, in TileType[,] gridData)
        {

            float noiseValue = Mathf.PerlinNoise(position.x * Scale, position.z * Scale);
            if (noiseValue <= Threshold)
            {

                #if UNITY_EDITOR
                DestroyImmediate(this.gameObject);
                #else
                Destroy(this.gameObject);
                #endif
            }
        }
    }
}
