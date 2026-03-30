using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A generic resource that holds inactive
/// resources to be later reused. Basically a
/// tool to drastically reduce frequent
/// construction and deconstruction of objects
/// </summary>
[System.Serializable]
public class GameObjectPool
{
    [SerializeField]
    protected GameObject Prefab;
    protected List<GameObject> Pool = new();

    public GameObjectPool(GameObject prefab)
    {
        if (Prefab)
        {
            Debug.LogError("Cannot change the type of a pool after created.");
        }
        else
            Prefab = prefab;
    }

    public GameObject GetObject()
    {
        foreach(GameObject go in Pool)
        {
            if(!go.activeInHierarchy)
            {
                go.SetActive(true);
                return go;
            }
        }
        GameObject g = UnityEngine.Object.Instantiate(Prefab);
        Pool.Add(g);
        return g;
    }
}
