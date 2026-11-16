using UnityEngine;

/// <summary>
/// Catalog entry for one furniture type. Lives under Resources/Furniture so the
/// catalog is data-driven: add an asset, assign a model, no code changes.
/// Model convention for imported prefabs: pivot at bottom-center, +Z = front,
/// real-world meters. Keep footprint (size.x / size.z) in 0.25 m steps so
/// sub-grid snapping stays exact.
/// </summary>
[CreateAssetMenu(menuName = "Building/Furniture Item")]
public class FurnitureItem : ScriptableObject
{
    public string displayName;
    public Vector3 size = new Vector3(1f, 0.75f, 1f); // x = width, y = height, z = depth (meters)
    public Color color = Color.white;                 // placeholder box color
    public bool prefersWall;                          // wall magnet (beds, wardrobes, sofas)
    public GameObject prefab;                         // future model; box placeholder when empty
    public int price = 1000;                          // budget cost
    public string roomHint;                           // room type this implies: bedroom/living/dining
}
