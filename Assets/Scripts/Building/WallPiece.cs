using UnityEngine;

public enum WallOpening { None, Door, Window }

/// <summary>
/// Per-wall data riding on the wall GameObject. Edge occupancy stays binary in
/// GridSystem; style and opening are orthogonal attributes of an existing wall,
/// so new wall styles and new opening kinds never multiply into combined states.
/// </summary>
public class WallPiece : MonoBehaviour
{
    public int x;
    public int z;
    public int dir;
    public int level;
    public int styleId;      // legacy, unused
    public string styleName; // SurfaceStyle asset name within this piece's kind catalog
    public WallOpening opening = WallOpening.None;
}
