using UnityEngine;
using System.Collections;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralCubeSlow : MonoBehaviour
{
	// Size of the grid's x and y dimension
	public int xSize, ySize, zSize;
	[Range(0, 1)]
	public float waitInSeconds = 0.05f;

	private Mesh mesh;
	private Vector3[] vertices;
	private static WaitForSeconds wait;
	private static int t = 0;

	private void Awake()
	{
		if (waitInSeconds == 0)
		{
			//Generate();
			StartCoroutine(GenerateSlow());
		}
		else
		{
			StartCoroutine(GenerateSlow());
		}
	}

	private void OnEnable()
	{
		wait = new WaitForSeconds(waitInSeconds);
	}

	private void OnDrawGizmos()
	{
		if (vertices == null)
		{
			return;
		}

		Gizmos.color = Color.black;
		for (int i = 0; i < vertices.Length; i++)
		{
			Gizmos.DrawSphere(transform.TransformPoint(vertices[i]), 0.1f);
		}
	}

	private IEnumerator GenerateSlow()
	{
		GetComponent<MeshFilter>().mesh = mesh = new Mesh();
		mesh.name = "Procedural Cube";

		yield return CreateVerticiesSlow();
		yield return CreateTrianglesSlow();
	}

	private IEnumerator CreateVerticiesSlow()
	{
		int cornerVertices = 8;
		int edgeVertices = (xSize + ySize + zSize - 3) * 4;
		int faceVertices = ((xSize - 1) * (ySize - 1) +
		                   (xSize - 1) * (zSize - 1) +
		                   (ySize - 1) * (zSize - 1)) * 2;
		int verticesCount = cornerVertices + edgeVertices + faceVertices;

		vertices = new Vector3[verticesCount];

		int v = 0;
		for (int y = 0; y <= ySize; y++)
		{
			for (int x = 0; x <= xSize; x++)
			{
				vertices[v++] = new Vector3(x, y, 0);
				yield return wait;
			}
			for (int z = 1; z <= zSize; z++)
			{
				vertices[v++] = new Vector3(xSize, y, z);
				yield return wait;
			}
			for (int x = xSize - 1; x >= 0; x--)
			{
				vertices[v++] = new Vector3(x, y, zSize);
				yield return wait;
			}
			for (int z = zSize - 1; z > 0; z--)
			{
				vertices[v++] = new Vector3(0, y, z);
				yield return wait;
			}
		}
		
		for (int z = 1; z < zSize; z++)
		{
			for (int x = 1; x < xSize; x++)
			{
				vertices[v++] = new Vector3(x, ySize, z);
				yield return wait;
			}
		}
		
		for (int z = 1; z < zSize; z++)
		{
			for (int x = 1; x < xSize; x++)
			{
				vertices[v++] = new Vector3(x, 0, z);
				yield return wait;
			}
		}

		mesh.vertices = vertices;
	}

	private IEnumerator CreateTrianglesSlow()
	{
		int quads = (xSize * ySize + xSize * zSize + ySize * zSize) * 2;
		int[] triangles = new int[quads * 6];

		int ring = (xSize + zSize) * 2;
		int v = 0;
		t = 0;

		for (int y = 0; y < ySize; y++, v++)
		{
			for (int q = 0; q < ring - 1; q++, v++)
			{
				yield return SetQuad(triangles, t, v, v + 1, v + ring, v + ring + 1);
			}
			yield return SetQuad(triangles, t, v, v - ring + 1, v + ring, v + 1);
		}

		yield return CreateTopFace(triangles, ring);
		yield return CreateBottomFace(triangles, ring);

		mesh.triangles = triangles;
	}

	private IEnumerator CreateTopFace(int[] triangles, int ring)
	{
		int v = ring * ySize;
		for (int x = 0; x < xSize - 1; x++, v++)
		{
			yield return SetQuad(triangles, t, v, v + 1, v + ring - 1, v + ring);
		}
		yield return SetQuad(triangles, t, v, v + 1, v + ring - 1, v + 2);

		int vMin = ring * (ySize + 1) - 1;
		int vMid = vMin + 1;
		int vMax = v + 2;

		for (int z = 1; z < zSize - 1; z++, vMin--, vMid++, vMax++)
		{
			yield return SetQuad(triangles, t, vMin, vMid, vMin - 1, vMid + xSize - 1);

			for (int x = 1; x < xSize - 1; x++, vMid++)
			{
				yield return SetQuad(triangles, t, vMid, vMid + 1, vMid + xSize - 1, vMid + xSize);
			}
			yield return SetQuad(triangles, t, vMid, vMax, vMid + xSize - 1, vMax + 1);
		}
		int vTop = vMin - 2;

		yield return SetQuad(triangles, t, vMin, vMid, vTop + 1, vTop);
		for (int x = 1; x < xSize - 1; x++, vTop--, vMid++)
		{
			yield return SetQuad(triangles, t, vMid, vMid + 1, vTop, vTop - 1);
		}
		yield return SetQuad(triangles, t, vMid, vTop - 2, vTop, vTop - 1);
	}

	private IEnumerator CreateBottomFace(int[] triangles, int ring)
	{
		int v = 1;
		int vMid = vertices.Length - (xSize - 1) * (zSize - 1);
		yield return SetQuad(triangles, t, ring - 1, vMid, 0, 1);

		for (int x = 1; x < xSize - 1; x++, v++, vMid++)
		{
			yield return SetQuad(triangles, t, vMid, vMid + 1, v, v + 1);
		}
		yield return SetQuad(triangles, t, vMid, v + 2, v, v + 1);

		int vMin = ring - 2;
		vMid -= xSize - 2;
		int vMax = v + 2;

		for (int z = 1; z < zSize - 1; z++, vMin--, vMid++, vMax++)
		{
			yield return SetQuad(triangles, t, vMin, vMid + xSize - 1, vMin + 1, vMid);

			for (int x = 1; x < xSize - 1; x++, vMid++)
			{
				yield return SetQuad(triangles, t, vMid + xSize - 1, vMid + xSize, vMid, vMid + 1);
			}
			yield return SetQuad(triangles, t, vMid + xSize - 1, vMax + 1, vMid, vMax);
		}

		int vTop = vMin - 1;
		yield return SetQuad(triangles, t, vTop + 1, vTop, vTop + 2, vMid);
		for (int x = 1; x < xSize - 1; x++, vTop--, vMid++)
		{
			yield return SetQuad(triangles, t, vTop, vTop - 1, vMid, vMid + 1);
		}
		yield return SetQuad(triangles, t, vTop, vTop - 1, vMid, vTop - 2);
	}

	private IEnumerator SetQuad(int[] triangles, int i, int v00, int v10, int v01, int v11)
	{
		triangles[i] = v00;
		triangles[i + 1] = v01;
		triangles[i + 2] = v10;

		if (waitInSeconds > 0)
		{
			mesh.triangles = triangles;
			mesh.RecalculateNormals();
			yield return wait;
		}
			
		triangles[i + 4] = v01;
		triangles[i + 3] = v10;
		triangles[i + 5] = v11;

		if (waitInSeconds > 0)
		{
			mesh.triangles = triangles;
			mesh.RecalculateNormals();
			yield return wait;
		}

		t = t + 6;
	}
}