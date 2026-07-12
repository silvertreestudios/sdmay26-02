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
    protected List<GameObject> Active = new();

    /// <summary>
    /// Constructs the pool for a given prefab
    /// </summary>
    /// <param name="prefab"></param>
    public GameObjectPool(GameObject prefab)
    {
        if (Prefab)
        {
            Debug.LogError("Cannot change the type of a pool after created.");
        }
        else
            Prefab = prefab;
    }

    /// <summary>
    /// Returns an instantiated instance of the prefab
    /// the pool was spawned with. Values on object may
    /// be different if code modifies any of the returned
    /// objects from this function.
    /// </summary>
    /// <returns>instance of the prefab</returns>
    public GameObject GetObject()
    {
        Pool.RemoveAll(go => go == null);
        foreach(GameObject go in Pool)
        {
            if(go != null && !go.activeInHierarchy)
            {
                go.SetActive(true);
                Active.Add(go);
                return go;
            }
        }
        GameObject g = UnityEngine.Object.Instantiate(Prefab);
        Pool.Add(g);
        Active.Add(g);
        return g;
    }


    public List<GameObject> GetMany(int amt)
    {
        List<GameObject> result = new();
        Pool.RemoveAll(go => go == null);
        foreach (GameObject go in Pool)
        {
            if (go != null && !go.activeInHierarchy)
            {
                go.SetActive(true);
                Active.Add(go);
                result.Add(go);
                if (result.Count >= amt)
                    return result;
            }
        }
        while(result.Count < amt)
        {
            GameObject g = UnityEngine.Object.Instantiate(Prefab);
            Pool.Add(g);
            Active.Add(g);
            result.Add(g);
        }
        return result;
    }

    /// <summary>
    /// Return a list of gameobjects that are currently active.
    /// Use this class's Destroy() or Clear() to perform deletions
    /// </summary>
    /// <returns></returns>
    public List<GameObject> CurrentlyActiveList()
    {
        return new List<GameObject>(Active);
    }

    /// <summary>
    /// Removes a gameobject from the scene.
    /// Does nothing if object is not active.
    /// </summary>
    /// <param name="g">Gameobject to destroy</param>
    public void Destroy(GameObject g)
    {
        if (g != null)
            g.SetActive(false);
        Active.Remove(g);
    }

    /// <summary>
    /// Removes all active gameobject allocated
    /// by this pool
    /// </summary>
    public void Clear()
    {
        foreach (GameObject go in Active)
        {
            if (go != null)
                go.SetActive(false);
        }
        Active.Clear();
        Pool.RemoveAll(go => go == null);
    }
}
