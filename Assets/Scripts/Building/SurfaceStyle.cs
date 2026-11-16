using UnityEngine;

/// <summary>
/// A paintable surface style (floors now; walls later). Lives under
/// Resources/FloorStyles so the palette is data-driven: add an asset, done.
/// Color is used for the generated flat material; assign an optional material
/// for textured styles.
/// </summary>
[CreateAssetMenu(menuName = "Building/Surface Style")]
public class SurfaceStyle : ScriptableObject
{
    public string displayName;
    public Color color = Color.white;
    public Material material;  // optional; flat color material generated when empty
    public GameObject prefab;  // optional model (walls: pivot bottom-center, run along +X)
}
