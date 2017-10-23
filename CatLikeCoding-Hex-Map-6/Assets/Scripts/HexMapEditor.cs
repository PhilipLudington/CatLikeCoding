using UnityEngine;
using UnityEngine.EventSystems;

public class HexMapEditor : MonoBehaviour
{
	enum OptionalToggle
	{
		Ignore,
		Yes,
		No
	}

	OptionalToggle riverMode;

	public Color[] colors;

	public HexGrid hexGrid;

	int activeElevation;

	Color activeColor;

	int brushSize;

	bool applyColor;
	bool applyElevation = true;
	bool isDrag;
	HexDirection dragDirection;
	HexCell previousCell;

	public void SelectColor(int index)
	{
		applyColor = index >= 0;
		if (applyColor)
		{
			activeColor = colors[index];
		}
	}

	public void SetApplyElevation(bool toggle)
	{
		applyElevation = toggle;
	}

	public void SetElevation(float elevation)
	{
		activeElevation = (int)elevation;
	}

	public void SetBrushSize(float size)
	{
		brushSize = (int)size;
	}

	public void ShowUI(bool visible)
	{
		hexGrid.ShowUI(visible);
	}

	void Awake()
	{
		SelectColor(0);
	}

	void Update()
	{
		if (
			Input.GetMouseButton(0) &&
			!EventSystem.current.IsPointerOverGameObject())
		{
			HandleInput();
		}
		else
		{
			previousCell = null;
		}
	}

	void HandleInput()
	{
		Ray inputRay = Camera.main.ScreenPointToRay(Input.mousePosition);
		RaycastHit hit;
		if (Physics.Raycast(inputRay, out hit))
		{
			HexCell currectCell = hexGrid.GetCell(hit.point);
			if (previousCell && previousCell != currectCell)
			{
				ValidateDrag(currectCell);
			}
			else
			{
				isDrag = false;
			}
			EditCells(currectCell);
			previousCell = currectCell;
			isDrag = true;
		}
		else
		{
			previousCell = null;
		}
	}

	void ValidateDrag(HexCell currentCell)
	{
		for (dragDirection = HexDirection.NE; dragDirection < HexDirection.NW; dragDirection++)
		{
			if (previousCell.GetNeighbor(dragDirection) == currentCell)
			{
				isDrag = true;
				return;
			}
		}
		isDrag = false;
	}

	void EditCells(HexCell center)
	{
		int centerX = center.coordinates.X;
		int centerZ = center.coordinates.Z;

		for (int r = 0, z = centerZ - brushSize; z <= centerZ; z++, r++)
		{
			for (int x = centerX - r; x <= centerX + brushSize; x++)
			{
				EditCell(hexGrid.GetCell(new HexCoordinates(x, z)));
			}
		}
		for (int r = 0, z = centerZ + brushSize; z > centerZ; z--, r++)
		{
			for (int x = centerX - brushSize; x <= centerX + r; x++)
			{
				EditCell(hexGrid.GetCell(new HexCoordinates(x, z)));
			}
		}
	}

	void EditCell(HexCell cell)
	{
		if (cell)
		{
			if (applyColor)
			{
				cell.Color = activeColor;
			}
			if (applyElevation)
			{
				cell.Elevation = activeElevation;
			}
			if (riverMode == OptionalToggle.No)
			{
				cell.RemoveRiver();
			}
			else if (isDrag && riverMode == OptionalToggle.Yes)
			{
				previousCell.SetOutgoingRiver(dragDirection);
			}
		}
	}

	public void SetRiverMode(int mode)
	{
		riverMode = (OptionalToggle)mode;
	}
}