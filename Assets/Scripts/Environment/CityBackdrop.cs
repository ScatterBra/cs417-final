using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural low-poly city backdrop: a ring of box buildings around the plot,
/// Apple-Maps style. Everything is written into ONE mesh with ONE URP/Lit
/// material — per-face colors come from a tiny generated palette texture
/// (URP/Lit ignores vertex colors), so the whole city is a single draw call
/// with shadows off and no colliders.
/// Building heights rise with distance from the plot (basin shape) and are
/// capped near it so the 30°-pitch orthographic camera keeps line of sight to
/// the plot from all four yaw steps. Deterministic per seed; the mission flow
/// feeds a brief-derived seed so each contract gets its own city.
/// </summary>
public class CityBackdrop : MonoBehaviour
{
    public GridSystem grid;

    [Header("Ring layout")]
    public float streetBuffer = 3.5f;   // empty street between plot and first ring
    public float ringRadius = 60f;      // city extent beyond the plot edge
    public float minBlock = 6f;
    public float maxBlock = 14f;
    public float streetWidth = 3f;
    public float sidewalk = 0.6f;       // building inset from block edge

    [Header("Buildings")]
    public float minHeight = 3f;
    public float maxHeight = 18f;
    public float storey = 2.5f;         // heights snap to this: same rhythm as player walls
    [Range(0f, 1f)] public float landmarkChance = 0.08f;

    [Header("Fog")]
    public bool controlFog = true;
    public float fogStart = 45f;
    public float fogEnd = 110f;
    public Color fogColor = new Color(0.78f, 0.82f, 0.87f);

    public int defaultSeed = 12345;

    // Palette texel layout (4x4): 0-4 walls, 5-7 roofs, 8 plinth/AO, 9 street.
    // All tones deliberately desaturated so the player's building stays the focus.
    static readonly Color[] Palette =
    {
        new Color(0.78f, 0.77f, 0.75f),
        new Color(0.72f, 0.74f, 0.78f),
        new Color(0.80f, 0.78f, 0.72f),
        new Color(0.70f, 0.72f, 0.70f),
        new Color(0.75f, 0.73f, 0.76f),
        new Color(0.58f, 0.58f, 0.60f),
        new Color(0.55f, 0.57f, 0.62f),
        new Color(0.62f, 0.59f, 0.55f),
        new Color(0.42f, 0.44f, 0.48f),
        new Color(0.63f, 0.65f, 0.64f),
    };
    const int WallTones = 5;
    const int RoofTone0 = 5, RoofTones = 3;
    const int PlinthTone = 8, StreetTone = 9;

    Mesh mesh;
    Material mat;
    static Mesh cubeMesh;

    static Mesh Cube => cubeMesh != null ? cubeMesh : cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    static Vector2 TexelUV(int i) => new Vector2((i % 4 + 0.5f) / 4f, (i / 4 + 0.5f) / 4f);
    static float RangeF(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    void Start()
    {
        if (mesh == null) Generate(defaultSeed);
    }

    public void GenerateForBrief(Brief brief) =>
        Generate(brief == null ? defaultSeed : StableHash($"{brief.title}|{brief.persona}|{brief.budget}"));

    [ContextMenu("Generate")]
    public void GenerateDefault() => Generate(defaultSeed);

    public void SetVisible(bool visible)
    {
        var r = GetComponent<MeshRenderer>();
        if (r != null) r.enabled = visible;
    }

    public void Generate(int seed)
    {
        if (grid == null) grid = FindFirstObjectByType<GridSystem>();
        if (grid == null) return;
        transform.SetPositionAndRotation(grid.transform.position, grid.transform.rotation);

        var rng = new System.Random(seed);
        float plotW = grid.width * grid.cellSize;
        float plotD = grid.height * grid.cellSize;
        var plotRect = new Rect(0f, 0f, plotW, plotD);
        var forbidden = new Rect(-streetBuffer, -streetBuffer, plotW + 2f * streetBuffer, plotD + 2f * streetBuffer);

        var verts = new List<Vector3>(16384);
        var normals = new List<Vector3>(16384);
        var uvs = new List<Vector2>(16384);
        var tris = new List<int>(32768);

        // street ground: one flat slab under everything; the plot's own Ground
        // (top at y=0) covers the center, so no ring shape needed
        AddBox(verts, normals, uvs, tris,
            new Vector3(plotW * 0.5f, -0.06f, plotD * 0.5f),
            new Vector3(plotW + 2f * ringRadius + 20f, 0.06f, plotD + 2f * ringRadius + 20f),
            StreetTone, StreetTone);

        foreach (var block in CarveBlocks(rng, plotRect, forbidden))
        {
            var lots = new List<Rect>();
            SplitLots(block, rng, 0, lots);
            foreach (var lot in lots)
            {
                float w = lot.width - 2f * sidewalk;
                float d = lot.height - 2f * sidewalk;
                if (w < 2.5f || d < 2.5f) continue;
                float h = PickHeight(rng, plotRect, lot.center);
                float cx = lot.center.x, cz = lot.center.y;
                // building body (slightly sunk so no gap shows above the street slab)
                AddBox(verts, normals, uvs, tris,
                    new Vector3(cx, h * 0.5f - 0.05f, cz), new Vector3(w, h, d),
                    WallTones == 0 ? 0 : rng.Next(WallTones), RoofTone0 + rng.Next(RoofTones));
                // dark plinth ring: zero-cost fake ambient occlusion at the base
                AddBox(verts, normals, uvs, tris,
                    new Vector3(cx, 0.2f, cz), new Vector3(w + 0.3f, 0.5f, d + 0.3f),
                    PlinthTone, PlinthTone);
            }
        }

        ApplyMesh(verts, normals, uvs, tris);
        if (controlFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.fogColor = fogColor;
        }
    }

    // ---- Layout ----

    // Irregular street grid: random-width column/row strips over the whole ring
    // area; anything inside the plot+buffer rect is clipped away, so blocks hug
    // the plot with exactly one street of separation.
    List<Rect> CarveBlocks(System.Random rng, Rect plotRect, Rect forbidden)
    {
        var blocks = new List<Rect>();
        var cols = CutStrips(rng, -ringRadius, plotRect.width + ringRadius);
        var rows = CutStrips(rng, -ringRadius, plotRect.height + ringRadius);
        foreach (var (x, w) in cols)
            foreach (var (z, d) in rows)
            {
                var r = new Rect(x, z, w, d);
                if (DistToRect(plotRect, r.center) > ringRadius) continue; // round off corners
                foreach (var piece in Subtract(r, forbidden))
                    if (piece.width >= 4f && piece.height >= 4f)
                        blocks.Add(piece);
            }
        return blocks;
    }

    List<(float pos, float size)> CutStrips(System.Random rng, float min, float max)
    {
        var strips = new List<(float, float)>();
        float x = min;
        while (x < max)
        {
            float w = Mathf.Min(RangeF(rng, minBlock, maxBlock), max - x);
            if (w >= 4f) strips.Add((x, w));
            x += w + streetWidth;
        }
        return strips;
    }

    static IEnumerable<Rect> Subtract(Rect r, Rect f)
    {
        if (!r.Overlaps(f)) { yield return r; yield break; }
        if (r.xMin < f.xMin) yield return Rect.MinMaxRect(r.xMin, r.yMin, f.xMin, r.yMax);
        if (r.xMax > f.xMax) yield return Rect.MinMaxRect(f.xMax, r.yMin, r.xMax, r.yMax);
        float mx0 = Mathf.Max(r.xMin, f.xMin), mx1 = Mathf.Min(r.xMax, f.xMax);
        if (r.yMin < f.yMin) yield return Rect.MinMaxRect(mx0, r.yMin, mx1, f.yMin);
        if (r.yMax > f.yMax) yield return Rect.MinMaxRect(mx0, f.yMax, mx1, r.yMax);
    }

    // Big blocks split into 2-4 lots with alley gaps, so building sizes vary
    // without ever overlapping.
    void SplitLots(Rect block, System.Random rng, int depth, List<Rect> lots)
    {
        float longSide = Mathf.Max(block.width, block.height);
        if (depth < 2 && longSide > 9f && rng.NextDouble() < 0.8)
        {
            const float gap = 1.2f;
            float t = RangeF(rng, 0.35f, 0.65f);
            Rect a, b;
            if (block.width >= block.height)
            {
                float cut = block.xMin + block.width * t;
                a = Rect.MinMaxRect(block.xMin, block.yMin, cut - gap * 0.5f, block.yMax);
                b = Rect.MinMaxRect(cut + gap * 0.5f, block.yMin, block.xMax, block.yMax);
            }
            else
            {
                float cut = block.yMin + block.height * t;
                a = Rect.MinMaxRect(block.xMin, block.yMin, block.xMax, cut - gap * 0.5f);
                b = Rect.MinMaxRect(block.xMin, cut + gap * 0.5f, block.xMax, block.yMax);
            }
            SplitLots(a, rng, depth + 1, lots);
            SplitLots(b, rng, depth + 1, lots);
        }
        else lots.Add(block);
    }

    float PickHeight(System.Random rng, Rect plotRect, Vector2 at)
    {
        float dist = DistToRect(plotRect, at);
        float t = Mathf.Clamp01(dist / ringRadius);
        float h = Mathf.Lerp(minHeight, maxHeight, Mathf.Pow(t, 0.9f)) * RangeF(rng, 0.75f, 1.3f);
        if (t > 0.4f && rng.NextDouble() < landmarkChance) h *= 1.6f;
        // cap so a building between camera and plot never hides the plot floor:
        // at 30° pitch the view ray clears height 0.577*dist, keep a margin
        h = Mathf.Min(h, Mathf.Max(storey, 0.55f * dist));
        return Mathf.Clamp(Mathf.Round(h / storey) * storey, storey, 20f);
    }

    static float DistToRect(Rect r, Vector2 p)
    {
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ---- Mesh ----

    // Clones the built-in cube (24 verts, correct winding/normals), scaled and
    // recolored per box: top face gets the roof texel, the rest the wall texel.
    static void AddBox(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t,
        Vector3 center, Vector3 size, int wallTone, int roofTone)
    {
        var cv = Cube.vertices;
        var cn = Cube.normals;
        var ct = Cube.triangles;
        int b = v.Count;
        for (int i = 0; i < cv.Length; i++)
        {
            v.Add(center + Vector3.Scale(cv[i], size));
            n.Add(cn[i]);
            uv.Add(TexelUV(cn[i].y > 0.5f ? roofTone : wallTone));
        }
        for (int i = 0; i < ct.Length; i++) t.Add(b + ct[i]);
    }

    void ApplyMesh(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
    {
        if (mesh == null)
            mesh = new Mesh { name = "CityBackdrop", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var mf = GetComponent<MeshFilter>();
        if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = GetComponent<MeshRenderer>();
        if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
        if (mat == null) mat = CreatePaletteMaterial();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    static Material CreatePaletteMaterial()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
        {
            name = "CityPalette",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = i < Palette.Length ? Palette[i] : Color.magenta;
        tex.SetPixels(px);
        tex.Apply();

        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CityBackdrop" };
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.05f);
        m.SetTexture("_BaseMap", tex);
        return m;
    }

    static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)h;
        }
    }
}
