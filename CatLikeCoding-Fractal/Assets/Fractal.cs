using UnityEngine;
using System.Collections;

public class Fractal : MonoBehaviour
{
    public Mesh[] Meshes;
    public Material Material;
    public int MaxDepth = 4;
    public float ChildScale;
    public float SpawnProbibility;
    public float MaxRotationSpeed;
    public float MaxTwist;


    private int depth = 0;
    private float rotationSpeed;

    private Material[,] materials;
    private static Vector3[] childDirections = {
        Vector3.up,
        Vector3.right,
        Vector3.left,
        Vector3.forward,
        Vector3.back,
    };

    private static Quaternion[] childOrientations = {
        Quaternion.identity,
        Quaternion.Euler(0.0f, 0.0f, -90.0f),
        Quaternion.Euler(0.0f, 0.0f, 90.0f),
        Quaternion.Euler(90.0f, 0.0f, 0.0f),
        Quaternion.Euler(-90.0f, 0.0f, 0.0f),
    };

    // Use this for initialization
    void Start()
    {
        if (materials == null)
        {
            InitializeMaterials();
        }

        gameObject.AddComponent<MeshFilter>().mesh = Meshes[Random.Range(0, Meshes.Length)];
        gameObject.AddComponent<MeshRenderer>().material = materials[depth, Random.Range(0, 2)];

        if (depth < MaxDepth)
        {
            StartCoroutine(CreateChildren());
        }

        rotationSpeed = Random.Range(-MaxRotationSpeed, MaxRotationSpeed);
        transform.Rotate(Random.Range(-MaxTwist, MaxTwist), 0.0f, 0.0f);
    }

    // Update is called once per frame
    void Update()
    {
        transform.Rotate(0.0f, rotationSpeed * Time.deltaTime, 0.0f);
    }

    private void Initialize(Fractal parent, int childIndex)
    {
        Meshes = parent.Meshes;
        MaxDepth = parent.MaxDepth;
        materials = parent.materials;
        depth = parent.depth + 1;
        ChildScale = parent.ChildScale;
        SpawnProbibility = parent.SpawnProbibility;
        MaxRotationSpeed = parent.MaxRotationSpeed;
        MaxTwist = parent.MaxTwist;

        transform.parent = parent.transform;
        transform.localScale = Vector3.one * ChildScale;
        transform.localPosition = childDirections[childIndex] * (0.5f + 0.5f * ChildScale);
        transform.localRotation = childOrientations[childIndex];
    }

    private void InitializeMaterials()
    {
        materials = new Material[MaxDepth + 1, 2];

        for (int index = 0; index < MaxDepth; index++)
        {
            float lerp = (float)index / (MaxDepth - 1);
            lerp *= lerp;

            materials[index, 0] = new Material(Material);
            materials[index, 0].color = Color.Lerp(Color.white, Color.yellow, lerp);
            materials[index, 1] = new Material(Material);
            materials[index, 1].color = Color.Lerp(Color.white, Color.cyan, lerp);
        }

        materials[MaxDepth, 0] = new Material(Material);
        materials[MaxDepth, 0].color = Color.magenta;
        materials[MaxDepth, 1] = new Material(Material);
        materials[MaxDepth, 1].color = Color.red;
    }

    private IEnumerator CreateChildren()
    {
        for (int index = 0; index < childDirections.Length; index++)
        {
            if (Random.value < SpawnProbibility)
            {
                yield return new WaitForSeconds(Random.Range(0.1f, 0.5f));

                new GameObject("Fractal Child").AddComponent<Fractal>().Initialize(this, index);
            }
        }
    }
}
