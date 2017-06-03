using UnityEngine;
using System.Collections;

public class PooledObject : MonoBehaviour
{
    [System.NonSerialized]
    public ObjectPool poolInstanceForPrefab;

    public ObjectPool Pool
    {
        get;
        set;
    }

    public void ReturnToPool()
    {
        if (Pool)
        {
            Pool.AddObject(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public T GetPooledInstance<T>() where T : PooledObject
    {
        if (poolInstanceForPrefab == null)
        {
            poolInstanceForPrefab = ObjectPool.GetPool(this);
        }

        return (T)poolInstanceForPrefab.GetObject();
    }
}
