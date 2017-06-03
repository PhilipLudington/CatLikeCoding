using UnityEngine;
using System.Collections;

public class NucleonSpawner : MonoBehaviour
{

    public float timeBetweenSpawns;
    public float spawnDistance;
    public Nucleon[] nucleonPrefabs;

    float timeSinceSpawn;

    // Use this for initialization
    void Start()
    {

    }

    void FixedUpdate()
    {
        timeSinceSpawn += Time.deltaTime;

        if (timeSinceSpawn >= timeBetweenSpawns)
        {
            timeSinceSpawn -= timeBetweenSpawns;
            SpawnNucleon();
        }
    }

    void SpawnNucleon()
    {
        Nucleon prefab = nucleonPrefabs[Random.Range(0, 2)];
        Nucleon spawn = Instantiate(prefab);
        spawn.transform.localPosition = Random.onUnitSphere * spawnDistance;
    }
}
