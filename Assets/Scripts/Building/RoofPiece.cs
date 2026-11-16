using UnityEngine;

/// <summary>
/// An auto-generated hip roof for one level. Its footprint is re-derived from the
/// storey-below walls every rebuild (flood fill from outside), so it adapts to the
/// building shape — rectangular or not. One auto-roof per level.
/// </summary>
public class RoofPiece : MonoBehaviour
{
    public int level;
}
