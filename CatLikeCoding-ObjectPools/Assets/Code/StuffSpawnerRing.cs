using UnityEngine;
using System.Collections;

public class StuffSpawnerRing : MonoBehaviour
{
    public int NumberofSpawners;
    public float radius;
    public float tiltAngle;
    public StuffSpawner SpawnerPrefab;

    void Awake()
    {
        for (int i = 0; i < NumberofSpawners; i++)
        {
            CreateSpawner(i);
        }
    }

    void CreateSpawner(int index)
    {
        Transform rotater = new GameObject("Rotater").transform;
        rotater.SetParent(transform, false);
        rotater.localRotation = Quaternion.Euler(0f, index * 360f / NumberofSpawners, 0f);

        StuffSpawner spawner = Instantiate<StuffSpawner>(SpawnerPrefab);
        spawner.transform.SetParent(rotater, false);
        spawner.transform.localPosition = new Vector3(0f, 0f, radius);
        spawner.transform.localRotation = Quaternion.Euler(tiltAngle, 0f, 0f);
    }
}
