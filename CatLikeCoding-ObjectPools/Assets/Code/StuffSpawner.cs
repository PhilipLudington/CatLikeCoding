using UnityEngine;
using System.Collections;

public class StuffSpawner : MonoBehaviour
{
    public FloatRange timeBetweenSpawns;
    public FloatRange scale;
    public FloatRange randomVelocity;
    public FloatRange angularVelocity;
    public float Velocity;
    public Stuff[] StuffPrefabs;
    public Material[] stuffMaterial;

    float currentSpawnDelay;
    float timeSinceLastSpawn;

    void FixedUpdate()
    {
        timeSinceLastSpawn += Time.deltaTime;
        if (timeSinceLastSpawn >= currentSpawnDelay)
        {
            timeSinceLastSpawn -= currentSpawnDelay;
            currentSpawnDelay = timeBetweenSpawns.RandomRange();
            SpawnStuff();
        }
    }

    void SpawnStuff()
    {
        Stuff prefab = StuffPrefabs[Random.Range(0, StuffPrefabs.Length)];
        Stuff spawn = prefab.GetPooledInstance<Stuff>();

        spawn.transform.localPosition = transform.position;
        spawn.transform.localScale = Vector3.one * scale.RandomRange();
        spawn.transform.localRotation = Random.rotation;

        spawn.Body.velocity = transform.up * Velocity +
            Random.onUnitSphere * randomVelocity.RandomRange();
        spawn.Body.angularVelocity = Random.onUnitSphere * angularVelocity.RandomRange();

        spawn.SetMaterial(stuffMaterial[Random.Range(0, stuffMaterial.Length)]);
    }
}
