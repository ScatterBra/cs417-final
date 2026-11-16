using UnityEngine;

/// <summary>
/// A placed furniture instance. (sx, sz) is the min corner of the rotated
/// footprint in quarter-cell sub-grid units; rot 0..3 with forward = +Z
/// rotated by 90° * rot.
/// </summary>
public class FurniturePiece : MonoBehaviour
{
    public string itemName;
    public int sx;
    public int sz;
    public int rot;
    public int level;
}
