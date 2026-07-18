using UnityEngine;

public abstract class SingletonMonoBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T Instance;

    public static T GetInstance()
    {
        if (Instance)
        {
            return Instance;
        }
        else 
        {
            Debug.LogError($"Required instance of {typeof(T).FullName} not found in the scene.");
        }
        return null;
    }

    public static bool TryGetInstance(out T instance)
    {
        instance = Instance != null ? Instance : null;
        return instance != null;
    }

    protected virtual void Awake()
    {
        if (!Instance)
        {
            Instance = this as T;
        }
        else if (Instance != this)
        {
            Debug.LogWarning($"Duplicate instance of {typeof(T)} destroyed.");
            Destroy(gameObject);
        }
    }
}
