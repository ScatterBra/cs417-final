using UnityEngine;

/// <summary>
/// A straight two-cell staircase climbing one storey. (x, z) is the base
/// (bottom-step) cell; rot gives the climb direction: 0 +X, 1 +Z, 2 -X, 3 -Z.
/// </summary>
public class StairPiece : MonoBehaviour
{
    public int x;
    public int z;
    public int level;
    public int rot;
}
