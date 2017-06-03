using UnityEngine;
using System.Collections;

[System.Serializable]
public class FloatRange
{
    public float min;
    public float max;

    public float RandomRange()
    {
        return Random.Range(min, max);
    }
}
