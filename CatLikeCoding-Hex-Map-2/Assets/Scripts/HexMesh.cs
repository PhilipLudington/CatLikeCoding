using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HexMesh : MonoBehaviour
{

	Mesh hexMesh;
	List<Vector3> vertices;
	List<Color> colors;
	List<int> triangles;

	MeshCollider meshCollider;

	void Awake()
	{
		GetComponent<MeshFilter>().mesh = hexMesh = new Mesh();
		meshCollider = gameObject.AddComponent<MeshCollider>();
		hexMesh.name = "Hex Mesh";
		vertices = new List<Vector3>();
		colors = new List<Color>();
		triangles = new List<int>();
	}

	public void Triangulate(HexCell[] cells)
	{
		hexMesh.Clear();
		vertices.Clear();
		colors.Clear();
		triangles.Clear();
		for (int i = 0; i < cells.Length; i++)
		{
			Triangulate(cells[i]);
		}
		hexMesh.vertices = vertices.ToArray();
		hexMesh.colors = colors.ToArray();
		hexMesh.triangles = triangles.ToArray();
		hexMesh.RecalculateNormals();
		meshCollider.sharedMesh = hexMesh;
	}

	void Triangulate(HexCell cell)
	{
		for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
		{
			Triangulate(d, cell);
		}
	}

	void Triangulate(HexDirection direction, HexCell cell)
	{
		Vector3 center = cell.transform.localPosition;
		for (int i = 0; i < 6; i++)
		{
			AddTriangle(
				center,
				center + HexMetrics.GetFirstCorner(direction),
				center + HexMetrics.GetSecondCorner(direction)
			);

			HexCell prevNeighbor = cell.GetNeighbor(direction.Previous()) ?? cell;
			HexCell neighbor = cell.GetNeighbor(direction) ?? cell;
			HexCell nextNeighbor = cell.GetNeighbor(direction.Previous()) ?? cell;

			Color edgeColor = (cell.color + neighbor.color) * 0.5f;
			AddTriangleColor(cell.color,
			                 (cell.color + prevNeighbor.color + neighbor.color) / 3f,
			                 (cell.color + neighbor.color + nextNeighbor.color) / 3f);
		}
	}

	void AddTriangle(Vector3 v1, Vector3 v2, Vector3 v3)
	{
		int vertexIndex = vertices.Count;
		vertices.Add(v1);
		vertices.Add(v2);
		vertices.Add(v3);
		triangles.Add(vertexIndex);
		triangles.Add(vertexIndex + 1);
		triangles.Add(vertexIndex + 2);
	}

	void AddTriangleColor(Color color)
	{
		colors.Add(color);
		colors.Add(color);
		colors.Add(color);
	}

	void AddTriangleColor(Color color1, Color color2, Color color3)
	{
		colors.Add(color1);
		colors.Add(color2);
		colors.Add(color3);
	}
}