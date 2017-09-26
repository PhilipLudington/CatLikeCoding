using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(HexCoordinates))]
public class HexCoordinatesDrawer : PropertyDrawer
{

	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
	{
		HexCoordinates coordinates = new HexCoordinates(
			                             property.FindPropertyRelative("x").intValue,
			                             property.FindPropertyRelative("z").intValue);

		GUI.Label(position, coordinates.ToString());
	}
	
	// Update is called once per frame
	void Update()
	{
		
	}
}
