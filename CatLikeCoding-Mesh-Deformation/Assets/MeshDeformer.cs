using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class MeshDeformer : MonoBehaviour
{
	Mesh deformingMesh;
	Vector3[] originalVerticies;
	Vector3[] displacedVerticies;
	Vector3[] vertexVelocities;

	void Start()
	{
		deformingMesh = GetComponent<MeshFilter>().mesh;
		originalVerticies = deformingMesh.vertices;
		displacedVerticies = new Vector3[originalVerticies.Length];
		vertexVelocities = new Vector3[originalVerticies.Length];

		for (int i = 0; i < originalVerticies.Length; i++)
		{
			displacedVerticies[i] = originalVerticies[i];
		}
	}
}
