using UnityEngine;
using System.Collections;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralGrid : MonoBehaviour
{
	// Size of the grid's x and y dimension
	public int xSize, ySize;

	private Mesh mesh;
	private Vector3[] vertices;

	private void Awake()
	{
		Generate();
	}

	private void Generate()
	{
		GetComponent<MeshFilter>().mesh = mesh = new Mesh();
		mesh.name = "Procedural Grid";

		// We can reuse vertices when tiles share corners,
		// so the number of verex we need is 
		// (#x + 1)(#y + 1)
		// were x & y are the number of tiles in that dimension
		int verticesCount = (xSize + 1) * (ySize + 1);

		vertices = new Vector3[verticesCount];

		Vector2[] uv = new Vector2[vertices.Length];
		Vector4[] tangents = new Vector4[vertices.Length];
		Vector4 tangent = new Vector4(1f, 0f, 0f, -1f);
		for (int i = 0, y = 0; y <= ySize; y++)
		{
			for (int x = 0; x <= xSize; x++, i++)
			{
				vertices[i] = new Vector3(x, y);
				uv[i] = new Vector2((float)x / xSize, (float)y / ySize);
				tangents[i] = tangent;
			}
		}
		mesh.vertices = vertices;
		mesh.uv = uv;
		mesh.tangents = tangents;

		int[] triangles = new int[xSize * ySize * 6];
		for (int ti = 0, vi = 0, y = 0; y < ySize; y++, vi++)
		{
			for (int x = 0; x < xSize; x++, ti += 6, vi++)
			{
				// Order matters because the rendering face is determined
				// by the clockwise order of the vertices
				triangles[ti] = vi;
				triangles[ti + 1] = vi + xSize + 1;
				triangles[ti + 2] = vi + 1;

				triangles[ti + 3] = triangles[ti + 2];
				triangles[ti + 4] = triangles[ti + 1];
				triangles[ti + 5] = vi + xSize + 2;
			}
		}

		mesh.triangles = triangles;
		mesh.RecalculateNormals();
	}
}