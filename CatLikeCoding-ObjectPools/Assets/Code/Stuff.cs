using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Stuff : PooledObject
{
    public Rigidbody Body
    {
        get;
        set;
    }

    public void SetMaterial(Material material)
    {
        foreach (MeshRenderer render in GetComponentsInChildren<MeshRenderer>())
        {
            render.material = material;
        }
    }

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        FindObjectsOfType<Stuff>();
        FindObjectsOfType<Stuff>();
    }

    void OnTriggerEnter(Collider collider)
    {
        if (collider.CompareTag("KillZone"))
        {
            ReturnToPool();
        }
    }

    void OnLevelWasLoaded()
    {
        ReturnToPool();
    }
}
