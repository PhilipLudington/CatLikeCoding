using UnityEngine;
using System.Collections.Generic;

public class ObjectPool : MonoBehaviour
{
    public List<PooledObject> availableObjects = new List<PooledObject>();

    PooledObject prefab;

    public PooledObject GetObject()
    {
        PooledObject obj;
        int takingIndex = availableObjects.Count - 1;
        if (takingIndex >= 0)
        {
            obj = availableObjects[takingIndex];
            availableObjects.RemoveAt(takingIndex);
            obj.gameObject.SetActive(true);
        }
        else
        {
            obj = Instantiate<PooledObject>(prefab);
            obj.transform.SetParent(transform, false);
            obj.Pool = this;
        }

        return obj;
    }

    public void AddObject(PooledObject obj)
    {
        obj.gameObject.SetActive(false);
        availableObjects.Add(obj);
    }

    public static ObjectPool GetPool(PooledObject prefab)
    {
        string poolName = string.Format("{0}Pool", prefab.name);
        GameObject obj;
        ObjectPool pool;
        if (Application.isEditor)
        {
            obj = GameObject.Find(poolName);
            if (obj)
            {
                pool = obj.GetComponent<ObjectPool>();
                if (pool != null)
                {
                    return pool;
                }
            }
        }

        obj = new GameObject(poolName);
        DontDestroyOnLoad(obj);
        pool = obj.AddComponent<ObjectPool>();
        pool.prefab = prefab;

        return pool;
    }
}
