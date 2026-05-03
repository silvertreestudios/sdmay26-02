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
        [SerializeField]
        protected float HeightNoiseScale;
        [SerializeField]
        protected float MinHeight = 0.5f;
        [SerializeField]
        protected float MaxHeight = 1.5f;

        [Header("Debug")]
        [SerializeField]
        protected bool DebugHeightColor;
        [SerializeField]
        protected Color DebugLowColor = Color.blue;
        [SerializeField]
        protected Color DebugHighColor = Color.red;

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
                return;
            }

            float heightNoise = Mathf.PerlinNoise(position.x * HeightNoiseScale + 100f, position.z * HeightNoiseScale + 100f);
            Vector3 scale = transform.localScale;
            scale.y = Mathf.Lerp(MinHeight, MaxHeight, heightNoise);
            transform.localScale = scale;

            transform.Rotate(Vector3.up, Random.Range(0f, 360f));

            if (DebugHeightColor)
            {
                MeshRenderer rend = GetComponentInChildren<MeshRenderer>();
                if (rend != null)
                    rend.sharedMaterial = new Material(rend.sharedMaterial) { color = Color.Lerp(DebugLowColor, DebugHighColor, heightNoise) };
            }
        }
    }
}
