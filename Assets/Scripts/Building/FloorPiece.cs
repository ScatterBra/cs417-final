using UnityEngine;

/// <summary>Per-tile data: which surface style this floor tile is painted with.</summary>
public class FloorPiece : MonoBehaviour
{
    public int x;
    public int z;
    public int level;
    public string styleName;
}
