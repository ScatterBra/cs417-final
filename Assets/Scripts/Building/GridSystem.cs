using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The plot grid. Floors occupy cells; walls occupy cell edges; both exist per level.
/// Edge convention: dir 0 = edge running along X at the south side of cell (x, z);
/// dir 1 = edge running along Z at the west side of cell (x, z).
/// Level L sits at local height L * wallHeight.
/// Edge occupancy is binary; wall style/opening live on the wall's WallPiece.
/// Wall geometry is generated procedurally in a canonical frame (length along
/// local X) and the wall object is rotated for dir 1.
/// </summary>
public class GridSystem : MonoBehaviour
{
    [Header("Plot")]
    public int width = 20;
    public int height = 15;
    public float cellSize = 1f;
    public int maxLevels = 4;

    [Header("Walls")]
    public float wallHeight = 2.5f;
    public float wallThickness = 0.12f;
    public float doorHeight = 2.0f;
    public float windowSillHeight = 0.9f;
    public float windowHeadHeight = 2.1f;

    [Header("Roof")]
    public float roofPitch = 0.6f; // rise per cell of distance from the eave

    [Header("Level fly animation")]
    public float levelFlyHeight = 6f;

    [Header("Prices")]
    public int wallPrice = 500;
    public int doorPrice = 800;
    public int windowPrice = 600;
    public int floorPrice = 200;
    public int stairPrice = 3000;
    public int roofPricePerCell = 300;

    [Header("Wall prefabs (optional; procedural cubes when empty)")]
    [Tooltip("Spec: pivot at bottom center, wall extends along +X, 1.12 long (incl. 0.06 end overlaps) x 2.5 high x 0.12 m thick")]
    public GameObject wallPrefab;
    public GameObject doorWallPrefab;
    public GameObject windowWallPrefab;

    [Header("Materials (assigned in editor; created at runtime if empty)")]
    public Material wallMaterial;
    public Material floorMaterial;
    public Material groundMaterial;
    public Material lineMaterial;
    public Material frameMaterial;
    public Material glassMaterial;
    public Material roofMaterial;

    readonly Dictionary<long, WallPiece> walls = new Dictionary<long, WallPiece>();
    readonly Dictionary<long, FloorPiece> floors = new Dictionary<long, FloorPiece>();
    // Stairs register one entry per occupied cell (two entries per staircase).
    readonly Dictionary<long, StairPiece> stairs = new Dictionary<long, StairPiece>();
    // Roofs register one entry per covered cell.
    readonly Dictionary<long, RoofPiece> roofs = new Dictionary<long, RoofPiece>();
    readonly List<FurniturePiece> furniture = new List<FurniturePiece>();

    // Climb direction per rot index: 0 +X, 1 +Z, 2 -X, 3 -Z
    static readonly int[] StairDX = { 1, 0, -1, 0 };
    static readonly int[] StairDZ = { 0, 1, 0, -1 };

    // Runtime-only state: must NOT survive Unity's domain-reload snapshot, or
    // EnsureInit would skip re-indexing while the (unserializable) dicts are empty.
    [System.NonSerialized] Transform wallRoot;
    [System.NonSerialized] Transform floorRoot;
    [System.NonSerialized] Transform stairRoot;
    [System.NonSerialized] Transform roofRoot;
    [System.NonSerialized] Transform furnRoot;
    [System.NonSerialized] Transform gridLines;
    [System.NonSerialized] bool initialized;
    [System.NonSerialized] FurnitureItem[] catalog;
    [System.NonSerialized] Dictionary<string, FurnitureItem> itemLookup;
    [System.NonSerialized] Dictionary<string, Material> furnMats;
    [System.NonSerialized] SurfaceStyle[] floorStyles;
    [System.NonSerialized] Dictionary<string, SurfaceStyle> floorStyleLookup;
    [System.NonSerialized] Dictionary<string, Material> floorStyleMats;
    [System.NonSerialized] Dictionary<WallOpening, SurfaceStyle[]> kindStyles;
    [System.NonSerialized] Dictionary<WallOpening, Dictionary<string, SurfaceStyle>> kindStyleLookup;
    [System.NonSerialized] Dictionary<string, Material> kindStyleMats;

    /// <summary>Quarter-cell furniture snapping step.</summary>
    public float SubCell => cellSize * 0.25f;

    public int WallCount => walls.Count;
    public int FloorCount => floors.Count;
    public int StairCount => stairs.Count / 2;
    public int RoofCount { get { var set = new HashSet<RoofPiece>(roofs.Values); return set.Count; } }

    static long WallKey(int x, int z, int dir, int level) =>
        ((long)level << 44) | ((long)dir << 40) | ((long)(z + 512) << 20) | (uint)(x + 512);
    static long CellKey(int x, int z, int level) =>
        ((long)level << 44) | ((long)(z + 512) << 20) | (uint)(x + 512);
    static int LevelOfKey(long key) => (int)(key >> 44);
    static int KeyX(long key) => (int)(key & 0xFFFFF) - 512;
    static int KeyZ(long key) => (int)((key >> 20) & 0xFFFFF) - 512;

    static string WallName(int x, int z, int dir, int level) => $"Wall_{x}_{z}_{dir}_{level}";
    static string FloorName(int x, int z, int level) => $"Floor_{x}_{z}_{level}";
    static string StairName(int x, int z, int rot, int level) => $"Stair_{x}_{z}_{rot}_{level}";
    static string RoofName(int level) => $"Roof_{level}";

    public float LevelBaseY(int level) => level * wallHeight;

    void Awake()
    {
        EnsureInit();
    }

    public void EnsureInit()
    {
        if (initialized && wallRoot != null) return;
        initialized = true;

        if (wallMaterial == null) wallMaterial = CreateLitMaterial(new Color(0.93f, 0.90f, 0.84f));
        if (floorMaterial == null) floorMaterial = CreateLitMaterial(new Color(0.83f, 0.67f, 0.50f));
        if (groundMaterial == null) groundMaterial = CreateLitMaterial(new Color(0.55f, 0.72f, 0.45f));
        if (lineMaterial == null) lineMaterial = CreateUnlitMaterial(new Color(0.38f, 0.52f, 0.32f));
        if (frameMaterial == null) frameMaterial = CreateLitMaterial(new Color(0.35f, 0.27f, 0.20f));
        if (glassMaterial == null) glassMaterial = CreateTransparentMaterial(new Color(0.65f, 0.85f, 0.95f, 0.45f));
        if (roofMaterial == null) roofMaterial = CreateLitMaterial(new Color(0.78f, 0.36f, 0.28f));

        wallRoot = GetOrCreateChild("Walls");
        floorRoot = GetOrCreateChild("Floors");
        stairRoot = GetOrCreateChild("Stairs");
        roofRoot = GetOrCreateChild("Roofs");
        furnRoot = GetOrCreateChild("Furniture");
        LoadCatalog();
        LoadFloorStyles();
        LoadWallKindStyles();
        BuildGround();
        BuildGridLines();
        RebuildIndexFromChildren();
    }

    public static Material CreateLitMaterial(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.05f);
        return m;
    }

    public static Material CreateUnlitMaterial(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        return m;
    }

    public static Material CreateTransparentMaterial(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetFloat("_Surface", 1f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.6f);
        return m;
    }

    Transform GetOrCreateChild(string childName)
    {
        var t = transform.Find(childName);
        if (t != null) return t;
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    void BuildGround()
    {
        var t = transform.Find("Ground");
        GameObject go;
        if (t == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ground";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(width * cellSize * 0.5f, -0.05f, height * cellSize * 0.5f);
            go.transform.localScale = new Vector3(width * cellSize + 4f, 0.1f, height * cellSize + 4f);
        }
        else go = t.gameObject;
        go.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
    }

    void BuildGridLines()
    {
        var t = transform.Find("GridLines");
        if (t != null)
        {
            gridLines = t;
            t.GetComponent<MeshRenderer>().sharedMaterial = lineMaterial;
            return;
        }
        var go = new GameObject("GridLines");
        go.transform.SetParent(transform, false);

        int vCount = (width + 1) * 2 + (height + 1) * 2;
        var verts = new Vector3[vCount];
        var idx = new int[vCount];
        int i = 0;
        const float y = 0.02f;
        for (int x = 0; x <= width; x++)
        {
            verts[i] = new Vector3(x * cellSize, y, 0f); idx[i] = i; i++;
            verts[i] = new Vector3(x * cellSize, y, height * cellSize); idx[i] = i; i++;
        }
        for (int z = 0; z <= height; z++)
        {
            verts[i] = new Vector3(0f, y, z * cellSize); idx[i] = i; i++;
            verts[i] = new Vector3(width * cellSize, y, z * cellSize); idx[i] = i; i++;
        }
        var mesh = new Mesh { name = "GridLines", vertices = verts };
        mesh.SetIndices(idx, MeshTopology.Lines, 0);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = lineMaterial;
        gridLines = go.transform;
    }

    /// <summary>Float the grid lines up to the level currently being edited.</summary>
    public void MoveGridLines(int level) => SetGridLinesHeight(LevelBaseY(level));

    public void SetGridLinesHeight(float y)
    {
        if (gridLines == null) gridLines = transform.Find("GridLines");
        if (gridLines != null) gridLines.localPosition = new Vector3(0f, y, 0f);
    }

    public void SetGridLinesVisible(bool visible)
    {
        if (gridLines == null) gridLines = transform.Find("GridLines");
        if (gridLines == null) return;
        var r = gridLines.GetComponent<MeshRenderer>();
        if (r != null && r.enabled != visible) r.enabled = visible;
    }

    /// <summary>
    /// Animated cutaway: a hidden level flies up and away (f = 0 flown/hidden,
    /// 1 = in place). Lift follows p², so hiding accelerates upward and vanishes
    /// at top speed, while revealing drops in fast with a soft landing.
    /// Shapes are never scaled.
    /// </summary>
    public void ApplyLevelReveal(int level, float f)
    {
        bool visible = f > 0.001f;
        float p = 1f - Mathf.Clamp01(f);
        float lift = levelFlyHeight * p * p;
        foreach (var kv in walls)
        {
            if (LevelOfKey(kv.Key) != level) continue;
            var piece = kv.Value;
            if (piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
            var t = piece.transform;
            Vector3 pos = WallCenterLocal(piece.x, piece.z, piece.dir, piece.level);
            t.localPosition = new Vector3(pos.x, pos.y + lift, pos.z);
            t.localScale = Vector3.one;
        }
        foreach (var kv in floors)
        {
            if (LevelOfKey(kv.Key) != level) continue;
            var go = kv.Value.gameObject;
            if (go.activeSelf != visible) go.SetActive(visible);
            var t = go.transform;
            Vector3 pos = t.localPosition;
            t.localPosition = new Vector3(pos.x, LevelBaseY(level) + 0.06f + lift, pos.z);
            t.localScale = FloorScale();
        }
        foreach (var kv in stairs)
        {
            if (LevelOfKey(kv.Key) != level) continue;
            var piece = kv.Value; // visited twice (two cells); idempotent
            if (piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
            var t = piece.transform;
            Vector3 pos = StairCenterLocal(piece.x, piece.z, piece.rot, piece.level);
            t.localPosition = new Vector3(pos.x, pos.y + lift, pos.z);
            t.localScale = Vector3.one;
        }
        foreach (var kv in roofs)
        {
            if (LevelOfKey(kv.Key) != level) continue;
            var piece = kv.Value; // visited once per covered cell; idempotent
            if (piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
            var t = piece.transform;
            t.localPosition = new Vector3(0f, LevelBaseY(level) + lift, 0f);
            t.localScale = Vector3.one;
        }
        foreach (var fp in furniture)
        {
            if (fp.level != level) continue;
            if (fp.gameObject.activeSelf != visible) fp.gameObject.SetActive(visible);
            var item = FindItem(fp.itemName);
            if (item == null) continue;
            var d = FurnFootprint(item, fp.rot);
            fp.transform.localPosition = new Vector3(
                fp.sx * SubCell + d.x * 0.5f,
                LevelBaseY(level) + 0.12f + lift,
                fp.sz * SubCell + d.y * 0.5f);
        }
    }

    /// <summary>Cutaway view: show only levels at or below the given level.</summary>
    public void SetVisibleUpToLevel(int level)
    {
        foreach (var kv in walls) kv.Value.gameObject.SetActive(LevelOfKey(kv.Key) <= level);
        foreach (var kv in floors) kv.Value.gameObject.SetActive(LevelOfKey(kv.Key) <= level);
        foreach (var kv in stairs) kv.Value.gameObject.SetActive(LevelOfKey(kv.Key) <= level);
        foreach (var kv in roofs) kv.Value.gameObject.SetActive(LevelOfKey(kv.Key) <= level);
        foreach (var f in furniture) f.gameObject.SetActive(f.level <= level);
    }

    // Runtime dictionaries are lost on domain reload; recover them from the saved
    // child objects, dedupe strays, and regenerate wall geometry from WallPiece data.
    // Pre-multi-level names (Wall_x_z_dir / Floor_x_z) are migrated to level 0.
    void RebuildIndexFromChildren()
    {
        walls.Clear();
        floors.Clear();
        stairs.Clear();
        roofs.Clear();

        var wallChildren = new List<Transform>();
        foreach (Transform c in wallRoot) wallChildren.Add(c);
        foreach (var c in wallChildren)
        {
            var p = c.name.Split('_');
            int lv = 0;
            bool ok = (p.Length == 4 || p.Length == 5)
                && int.TryParse(p[1], out int x) && int.TryParse(p[2], out int z) && int.TryParse(p[3], out int d)
                && (p.Length == 4 || int.TryParse(p[4], out lv));
            if (!ok) { Kill(c.gameObject); continue; }

            var q = c.name.Split('_');
            int wx = int.Parse(q[1]), wz = int.Parse(q[2]), wd = int.Parse(q[3]);
            long key = WallKey(wx, wz, wd, lv);
            if (walls.ContainsKey(key)) { Kill(c.gameObject); continue; }

            var piece = c.GetComponent<WallPiece>();
            if (piece == null) piece = c.gameObject.AddComponent<WallPiece>();
            piece.x = wx; piece.z = wz; piece.dir = wd; piece.level = lv;
            if (string.IsNullOrEmpty(piece.styleName) || FindWallKindStyle(piece.opening, piece.styleName) == null)
                piece.styleName = DefaultWallKindStyleName(piece.opening);
            c.name = WallName(wx, wz, wd, lv);
            StripLegacyPrimitive(c.gameObject);
            walls[key] = piece;
            RebuildWallGeometry(piece);
        }

        var floorChildren = new List<Transform>();
        foreach (Transform c in floorRoot) floorChildren.Add(c);
        foreach (var c in floorChildren)
        {
            var p = c.name.Split('_');
            int lv = 0;
            bool ok = (p.Length == 3 || p.Length == 4)
                && int.TryParse(p[1], out int x) && int.TryParse(p[2], out int z)
                && (p.Length == 3 || int.TryParse(p[3], out lv));
            if (!ok) { Kill(c.gameObject); continue; }

            var q = c.name.Split('_');
            int fx = int.Parse(q[1]), fz = int.Parse(q[2]);
            long key = CellKey(fx, fz, lv);
            if (floors.ContainsKey(key)) { Kill(c.gameObject); continue; }

            var fpc = c.GetComponent<FloorPiece>();
            if (fpc == null) fpc = c.gameObject.AddComponent<FloorPiece>();
            fpc.x = fx; fpc.z = fz; fpc.level = lv;
            if (string.IsNullOrEmpty(fpc.styleName)) fpc.styleName = DefaultFloorStyleName;
            floors[key] = fpc;
            c.name = FloorName(fx, fz, lv);
            c.GetComponent<MeshRenderer>().sharedMaterial = FloorMaterialFor(fpc.styleName);
            c.localPosition = FloorCenterLocal(fx, fz, lv);
            c.localScale = FloorScale();
        }

        var stairChildren = new List<Transform>();
        foreach (Transform c in stairRoot) stairChildren.Add(c);
        foreach (var c in stairChildren)
        {
            var p = c.name.Split('_');
            bool ok = p.Length == 5
                && int.TryParse(p[1], out int sx) && int.TryParse(p[2], out int sz)
                && int.TryParse(p[3], out int sr) && int.TryParse(p[4], out int sl);
            if (!ok) { Kill(c.gameObject); continue; }

            var q = c.name.Split('_');
            int x = int.Parse(q[1]), z = int.Parse(q[2]), r = int.Parse(q[3]), lv = int.Parse(q[4]);
            int tx = x + StairDX[r], tz = z + StairDZ[r];
            if (stairs.ContainsKey(CellKey(x, z, lv)) || stairs.ContainsKey(CellKey(tx, tz, lv)))
            {
                Kill(c.gameObject);
                continue;
            }
            var piece = c.GetComponent<StairPiece>();
            if (piece == null) piece = c.gameObject.AddComponent<StairPiece>();
            piece.x = x; piece.z = z; piece.rot = r; piece.level = lv;
            stairs[CellKey(x, z, lv)] = piece;
            stairs[CellKey(tx, tz, lv)] = piece;
            RebuildStairGeometry(piece);
        }

        // Roofs are re-derived from walls, so this must run after the wall loop above.
        var roofChildren = new List<Transform>();
        foreach (Transform c in roofRoot) roofChildren.Add(c);
        var seenRoofLevels = new HashSet<int>();
        foreach (var c in roofChildren)
        {
            var p = c.name.Split('_');
            if (p.Length != 2 || !int.TryParse(p[1], out int lv) || !seenRoofLevels.Add(lv))
            {
                Kill(c.gameObject); // malformed, legacy rect-roof name, or duplicate level
                continue;
            }
            var piece = c.GetComponent<RoofPiece>();
            if (piece == null) piece = c.gameObject.AddComponent<RoofPiece>();
            piece.level = lv;
            RebuildRoofGeometry(piece);
        }

        furniture.Clear();
        var furnChildren = new List<Transform>();
        foreach (Transform c in furnRoot) furnChildren.Add(c);
        foreach (var c in furnChildren)
        {
            var fp = c.GetComponent<FurniturePiece>();
            var item = fp != null ? FindItem(fp.itemName) : null;
            if (item == null) { Kill(c.gameObject); continue; }
            furniture.Add(fp);
            RebuildFurnitureVisual(fp, item);
        }
    }

    // ---- Furniture ----

    void LoadCatalog()
    {
        if (catalog != null) return;
        catalog = Resources.LoadAll<FurnitureItem>("Furniture");
        itemLookup = new Dictionary<string, FurnitureItem>();
        foreach (var it in catalog) itemLookup[it.name] = it;
    }

    public FurnitureItem[] GetCatalog()
    {
        EnsureInit();
        return catalog;
    }

    public FurnitureItem FindItem(string itemName)
    {
        LoadCatalog();
        if (string.IsNullOrEmpty(itemName)) return null;
        itemLookup.TryGetValue(itemName, out var it);
        return it;
    }

    Material GetFurnitureMaterial(FurnitureItem item)
    {
        if (furnMats == null) furnMats = new Dictionary<string, Material>();
        if (!furnMats.TryGetValue(item.name, out var m) || m == null)
        {
            m = CreateLitMaterial(item.color);
            furnMats[item.name] = m;
        }
        return m;
    }

    // ---- Floor styles ----

    void LoadFloorStyles()
    {
        if (floorStyles != null) return;
        floorStyles = Resources.LoadAll<SurfaceStyle>("FloorStyles");
        floorStyleLookup = new Dictionary<string, SurfaceStyle>();
        foreach (var s in floorStyles) floorStyleLookup[s.name] = s;
    }

    public SurfaceStyle[] GetFloorStyles()
    {
        EnsureInit();
        return floorStyles;
    }

    public SurfaceStyle FindFloorStyle(string styleName)
    {
        LoadFloorStyles();
        if (string.IsNullOrEmpty(styleName)) return null;
        floorStyleLookup.TryGetValue(styleName, out var s);
        return s;
    }

    public string DefaultFloorStyleName
    {
        get
        {
            LoadFloorStyles();
            var wood = FindFloorStyle("Wood");
            if (wood != null) return wood.name;
            return floorStyles.Length > 0 ? floorStyles[0].name : "";
        }
    }

    Material FloorMaterialFor(string styleName)
    {
        var s = FindFloorStyle(styleName);
        if (s == null) return floorMaterial; // legacy fallback
        if (s.material != null) return s.material;
        if (floorStyleMats == null) floorStyleMats = new Dictionary<string, Material>();
        if (!floorStyleMats.TryGetValue(s.name, out var m) || m == null)
        {
            m = CreateLitMaterial(s.color);
            floorStyleMats[s.name] = m;
        }
        return m;
    }

    // ---- Wall-family styles: one independent catalog per component kind ----
    // Resources/WallStyles = solid wall models, DoorStyles = door wall models,
    // WindowStyles = window wall models. Each SurfaceStyle asset = one look.

    static string KindFolder(WallOpening kind) =>
        kind == WallOpening.Door ? "DoorStyles" : kind == WallOpening.Window ? "WindowStyles" : "WallStyles";

    void LoadWallKindStyles()
    {
        if (kindStyles != null) return;
        kindStyles = new Dictionary<WallOpening, SurfaceStyle[]>();
        kindStyleLookup = new Dictionary<WallOpening, Dictionary<string, SurfaceStyle>>();
        kindStyleMats = new Dictionary<string, Material>();
        foreach (WallOpening k in new[] { WallOpening.None, WallOpening.Door, WallOpening.Window })
        {
            var arr = Resources.LoadAll<SurfaceStyle>(KindFolder(k));
            kindStyles[k] = arr;
            var lookup = new Dictionary<string, SurfaceStyle>();
            foreach (var s in arr) lookup[s.name] = s;
            kindStyleLookup[k] = lookup;
        }
    }

    public SurfaceStyle[] GetWallKindStyles(WallOpening kind)
    {
        EnsureInit();
        return kindStyles[kind];
    }

    public SurfaceStyle FindWallKindStyle(WallOpening kind, string styleName)
    {
        LoadWallKindStyles();
        if (string.IsNullOrEmpty(styleName)) return null;
        kindStyleLookup[kind].TryGetValue(styleName, out var s);
        return s;
    }

    public string DefaultWallKindStyleName(WallOpening kind)
    {
        LoadWallKindStyles();
        string preferred = kind == WallOpening.Door ? "Wood"
            : kind == WallOpening.Window ? "Standard"
            : "White";
        var hit = FindWallKindStyle(kind, preferred);
        if (hit != null) return hit.name;
        var arr = kindStyles[kind];
        return arr.Length > 0 ? arr[0].name : "";
    }

    Material WallKindMaterialFor(WallOpening kind, string styleName)
    {
        var s = FindWallKindStyle(kind, styleName);
        if (s == null) return kind == WallOpening.None ? wallMaterial : frameMaterial;
        if (s.material != null) return s.material;
        string key = (int)kind + ":" + s.name;
        if (!kindStyleMats.TryGetValue(key, out var m) || m == null)
        {
            m = CreateLitMaterial(s.color);
            kindStyleMats[key] = m;
        }
        return m;
    }

    /// <summary>Repaint an existing wall (any opening kind) with another style.</summary>
    public void SetWallStyle(int x, int z, int dir, int level, string styleName)
    {
        if (string.IsNullOrEmpty(styleName)) return;
        if (!TryGetWall(x, z, dir, level, out var piece)) return;
        if (piece.styleName == styleName) return;
        piece.styleName = styleName;
        RebuildWallGeometry(piece);
    }

    /// <summary>Rotated footprint (x = width along X, y = depth along Z) in meters.</summary>
    public static Vector2 FurnFootprint(FurnitureItem item, int rot) =>
        (rot & 1) == 0 ? new Vector2(item.size.x, item.size.z) : new Vector2(item.size.z, item.size.x);

    public bool CanPlaceFurniture(FurnitureItem item, int sx, int sz, int rot, int level)
    {
        if (item == null || level < 0 || level >= maxLevels) return false;
        var fpt = FurnFootprint(item, rot);
        float s = SubCell;
        float x0 = sx * s, z0 = sz * s, x1 = x0 + fpt.x, z1 = z0 + fpt.y;
        const float eps = 0.02f;
        if (x0 < -eps || z0 < -eps || x1 > width * cellSize + eps || z1 > height * cellSize + eps) return false;

        int cx0 = Mathf.FloorToInt((x0 + eps) / cellSize), cx1 = Mathf.FloorToInt((x1 - eps) / cellSize);
        int cz0 = Mathf.FloorToInt((z0 + eps) / cellSize), cz1 = Mathf.FloorToInt((z1 - eps) / cellSize);
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cz = cz0; cz <= cz1; cz++)
            {
                if (level > 0 && !HasFloor(cx, cz, level)) return false; // ground level is free
                if (HasStair(cx, cz, level)) return false;
            }

        // must not cross a wall line (flush against walls is fine)
        for (int gx = cx0 + 1; gx <= cx1; gx++)
            for (int cz = cz0; cz <= cz1; cz++)
                if (HasWall(gx, cz, 1, level)) return false;
        for (int gz = cz0 + 1; gz <= cz1; gz++)
            for (int cx = cx0; cx <= cx1; cx++)
                if (HasWall(cx, gz, 0, level)) return false;

        foreach (var f in furniture)
        {
            if (f.level != level) continue;
            var of = FindItem(f.itemName);
            if (of == null) continue;
            var od = FurnFootprint(of, f.rot);
            float ox0 = f.sx * s, oz0 = f.sz * s, ox1 = ox0 + od.x, oz1 = oz0 + od.y;
            if (x0 < ox1 - eps && x1 > ox0 + eps && z0 < oz1 - eps && z1 > oz0 + eps) return false;
        }
        return true;
    }

    bool WallRunAlongX(int gz, float x0, float x1, int level)
    {
        for (int cx = Mathf.FloorToInt(x0 / cellSize + 0.001f); cx <= Mathf.FloorToInt((x1 - 0.001f) / cellSize); cx++)
            if (HasWall(cx, gz, 0, level)) return true;
        return false;
    }

    bool WallRunAlongZ(int gx, float z0, float z1, int level)
    {
        for (int cz = Mathf.FloorToInt(z0 / cellSize + 0.001f); cz <= Mathf.FloorToInt((z1 - 0.001f) / cellSize); cz++)
            if (HasWall(gx, cz, 1, level)) return true;
        return false;
    }

    /// <summary>Wall magnet: pull a prefers-wall item flush onto the wall line just
    /// behind its back edge (within 0.35 m). Keeps sub-grid alignment.</summary>
    public void SnapFurnitureToWall(FurnitureItem item, ref int sx, ref int sz, int rot, int level)
    {
        if (item == null || !item.prefersWall) return;
        var fpt = FurnFootprint(item, rot);
        float s = SubCell;
        float x0 = sx * s, z0 = sz * s, x1 = x0 + fpt.x, z1 = z0 + fpt.y;
        const float range = 0.35f;
        switch (rot)
        {
            case 0: // faces +Z, back edge at z0
            {
                int g = Mathf.FloorToInt(z0 / cellSize + 0.001f);
                if (g >= 0 && z0 - g * cellSize <= range && WallRunAlongX(g, x0, x1, level)) sz = g * 4;
                break;
            }
            case 2: // faces -Z, back edge at z1
            {
                int g = Mathf.CeilToInt(z1 / cellSize - 0.001f);
                if (g <= height && g * cellSize - z1 <= range && WallRunAlongX(g, x0, x1, level)) sz = g * 4 - Mathf.RoundToInt(fpt.y / s);
                break;
            }
            case 1: // faces +X, back edge at x0
            {
                int g = Mathf.FloorToInt(x0 / cellSize + 0.001f);
                if (g >= 0 && x0 - g * cellSize <= range && WallRunAlongZ(g, z0, z1, level)) sx = g * 4;
                break;
            }
            case 3: // faces -X, back edge at x1
            {
                int g = Mathf.CeilToInt(x1 / cellSize - 0.001f);
                if (g <= width && g * cellSize - x1 <= range && WallRunAlongZ(g, z0, z1, level)) sx = g * 4 - Mathf.RoundToInt(fpt.x / s);
                break;
            }
        }
    }

    public FurniturePiece PlaceFurniture(FurnitureItem item, int sx, int sz, int rot, int level)
    {
        EnsureInit();
        if (!CanPlaceFurniture(item, sx, sz, rot, level)) return null;
        var go = new GameObject("Furn_" + item.name);
        go.transform.SetParent(furnRoot, false);
        var fp = go.AddComponent<FurniturePiece>();
        fp.itemName = item.name; fp.sx = sx; fp.sz = sz; fp.rot = rot; fp.level = level;
        furniture.Add(fp);
        RebuildFurnitureVisual(fp, item);
        return fp;
    }

    public bool TryGetFurnitureAt(Vector3 world, int level, out FurniturePiece hit)
    {
        hit = null;
        Vector3 lp = transform.InverseTransformPoint(world);
        float s = SubCell;
        foreach (var f in furniture)
        {
            if (f.level != level) continue;
            var item = FindItem(f.itemName);
            if (item == null) continue;
            var d = FurnFootprint(item, f.rot);
            float x0 = f.sx * s, z0 = f.sz * s;
            if (lp.x >= x0 && lp.x <= x0 + d.x && lp.z >= z0 && lp.z <= z0 + d.y)
            {
                hit = f;
                return true;
            }
        }
        return false;
    }

    public void RemoveFurniture(FurniturePiece fp)
    {
        if (fp == null) return;
        furniture.Remove(fp);
        Kill(fp.gameObject);
    }

    public IEnumerable<FurniturePiece> EnumerateFurniture()
    {
        EnsureInit();
        return furniture;
    }

    void RebuildFurnitureVisual(FurniturePiece fp, FurnitureItem item)
    {
        var t = fp.transform;
        var d = FurnFootprint(item, fp.rot);
        float s = SubCell;
        t.localPosition = new Vector3(fp.sx * s + d.x * 0.5f, LevelBaseY(fp.level) + 0.12f, fp.sz * s + d.y * 0.5f);
        t.localRotation = Quaternion.Euler(0f, 90f * fp.rot, 0f);
        t.localScale = Vector3.one;

        var old = new List<GameObject>();
        foreach (Transform c in t) old.Add(c.gameObject);
        foreach (var c in old) Kill(c);

        if (item.prefab != null)
        {
            var model = Instantiate(item.prefab, t);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
        }
        else
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Box";
            box.transform.SetParent(t, false);
            box.transform.localPosition = new Vector3(0f, item.size.y * 0.5f, 0f);
            box.transform.localScale = item.size;
            box.GetComponent<MeshRenderer>().sharedMaterial = GetFurnitureMaterial(item);
        }
    }

    // Walls saved before the composite-geometry refactor were primitive cubes;
    // remove their render components so they can act as plain parents.
    static void StripLegacyPrimitive(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Kill(col);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) Kill(mr);
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null) Kill(mf);
    }

    // ---- Coordinate helpers ----

    public Vector3 WallCenterLocal(int x, int z, int dir, int level = 0)
    {
        float y = LevelBaseY(level) + wallHeight * 0.5f;
        return dir == 0
            ? new Vector3((x + 0.5f) * cellSize, y, z * cellSize)
            : new Vector3(x * cellSize, y, (z + 0.5f) * cellSize);
    }

    public Vector3 WallCenterWorld(int x, int z, int dir, int level = 0) =>
        transform.TransformPoint(WallCenterLocal(x, z, dir, level));

    public Vector3 WallScaleFor(int dir) => dir == 0
        ? new Vector3(cellSize + wallThickness, wallHeight, wallThickness)
        : new Vector3(wallThickness, wallHeight, cellSize + wallThickness);

    public Vector3 StairCenterLocal(int x, int z, int rot, int level = 0) => new Vector3(
        (x + 0.5f + StairDX[rot] * 0.5f) * cellSize,
        LevelBaseY(level),
        (z + 0.5f + StairDZ[rot] * 0.5f) * cellSize);

    public Vector3 StairCenterWorld(int x, int z, int rot, int level = 0) =>
        transform.TransformPoint(StairCenterLocal(x, z, rot, level));

    public static float StairYaw(int rot) => -90f * rot;

    public Vector3 FloorCenterLocal(int x, int z, int level = 0) =>
        new Vector3((x + 0.5f) * cellSize, LevelBaseY(level) + 0.06f, (z + 0.5f) * cellSize);
    public Vector3 FloorCenterWorld(int x, int z, int level = 0) =>
        transform.TransformPoint(FloorCenterLocal(x, z, level));
    public Vector3 FloorScale() => new Vector3(cellSize, 0.12f, cellSize);

    /// <summary>Nearest cell edge to a world point (XZ only; caller supplies the level). Returns false when outside the plot (with margin).</summary>
    public bool TryGetEdgeAt(Vector3 world, out int x, out int z, out int dir, out float dist)
    {
        Vector3 lp = transform.InverseTransformPoint(world);
        float gx = lp.x / cellSize, gz = lp.z / cellSize;
        float dxLine = Mathf.Abs(gx - Mathf.Round(gx));
        float dzLine = Mathf.Abs(gz - Mathf.Round(gz));
        if (dzLine <= dxLine)
        {
            dir = 0; x = Mathf.FloorToInt(gx); z = Mathf.RoundToInt(gz); dist = dzLine * cellSize;
        }
        else
        {
            dir = 1; x = Mathf.RoundToInt(gx); z = Mathf.FloorToInt(gz); dist = dxLine * cellSize;
        }
        return gx > -0.5f && gx < width + 0.5f && gz > -0.5f && gz < height + 0.5f;
    }

    public bool TryGetCellAt(Vector3 world, out int x, out int z)
    {
        Vector3 lp = transform.InverseTransformPoint(world);
        x = Mathf.FloorToInt(lp.x / cellSize);
        z = Mathf.FloorToInt(lp.z / cellSize);
        return x >= 0 && x < width && z >= 0 && z < height;
    }

    /// <summary>Cell under a world point without bounds check (for drag rectangles that leave the plot).</summary>
    public void CellAtUnclamped(Vector3 world, out int x, out int z)
    {
        Vector3 lp = transform.InverseTransformPoint(world);
        x = Mathf.FloorToInt(lp.x / cellSize);
        z = Mathf.FloorToInt(lp.z / cellSize);
    }

    // ---- Queries ----

    public bool HasWall(int x, int z, int dir, int level = 0) => walls.ContainsKey(WallKey(x, z, dir, level));
    public bool HasFloor(int x, int z, int level = 0) => floors.ContainsKey(CellKey(x, z, level));
    public bool TryGetWall(int x, int z, int dir, int level, out WallPiece piece) =>
        walls.TryGetValue(WallKey(x, z, dir, level), out piece);

    public bool CanPlaceWall(int x, int z, int dir, int level = 0)
    {
        if (level < 0 || level >= maxLevels) return false;
        if (dir == 0) { if (x < 0 || x >= width || z < 0 || z > height) return false; }
        else { if (x < 0 || x > width || z < 0 || z >= height) return false; }
        return !HasWall(x, z, dir, level);
    }

    public bool CanPlaceFloor(int x, int z, int level = 0) =>
        level >= 0 && level < maxLevels &&
        x >= 0 && x < width && z >= 0 && z < height && !HasFloor(x, z, level);

    public bool HasStair(int x, int z, int level = 0) => stairs.ContainsKey(CellKey(x, z, level));
    public bool TryGetStair(int x, int z, int level, out StairPiece piece) =>
        stairs.TryGetValue(CellKey(x, z, level), out piece);

    public bool CanPlaceStair(int x, int z, int rot, int level = 0)
    {
        if (level < 0 || level >= maxLevels - 1) return false; // top level leads nowhere
        int tx = x + StairDX[rot], tz = z + StairDZ[rot];
        if (x < 0 || x >= width || z < 0 || z >= height) return false;
        if (tx < 0 || tx >= width || tz < 0 || tz >= height) return false;
        return !HasStair(x, z, level) && !HasStair(tx, tz, level);
    }

    // ---- Mutations ----

    public WallPiece PlaceWall(int x, int z, int dir, int level = 0, WallOpening opening = WallOpening.None, string styleName = null)
    {
        EnsureInit();
        if (!CanPlaceWall(x, z, dir, level)) return null;
        var go = new GameObject(WallName(x, z, dir, level));
        go.transform.SetParent(wallRoot, false);
        var piece = go.AddComponent<WallPiece>();
        piece.x = x; piece.z = z; piece.dir = dir; piece.level = level;
        piece.opening = opening;
        piece.styleName = string.IsNullOrEmpty(styleName) ? DefaultWallKindStyleName(opening) : styleName;
        walls[WallKey(x, z, dir, level)] = piece;
        RebuildWallGeometry(piece);
        return piece;
    }

    /// <summary>Set an opening on an existing wall (or build a wall with one on an
    /// empty edge); clicking the same opening again reverts to a solid wall.</summary>
    public void ToggleOpening(int x, int z, int dir, WallOpening opening, int level = 0, string styleName = null)
    {
        if (TryGetWall(x, z, dir, level, out var piece))
        {
            // same kind with a different style selected: repaint instead of revert
            if (piece.opening == opening && !string.IsNullOrEmpty(styleName) && piece.styleName != styleName)
            {
                piece.styleName = styleName;
                RebuildWallGeometry(piece);
                return;
            }
            var newOpening = piece.opening == opening ? WallOpening.None : opening;
            piece.opening = newOpening;
            // styleName is scoped to the piece's kind catalog: apply the tool's
            // selection only when it matches the new kind, else that kind's default
            piece.styleName = (newOpening == opening && !string.IsNullOrEmpty(styleName))
                ? styleName
                : DefaultWallKindStyleName(newOpening);
            RebuildWallGeometry(piece);
        }
        else if (CanPlaceWall(x, z, dir, level))
        {
            PlaceWall(x, z, dir, level, opening, styleName);
        }
    }

    public void SetStyle(int x, int z, int dir, int styleId, int level = 0)
    {
        if (TryGetWall(x, z, dir, level, out var piece))
        {
            piece.styleId = styleId;
            RebuildWallGeometry(piece);
        }
    }

    public FloorPiece PlaceFloor(int x, int z, int level = 0, string styleName = null)
    {
        EnsureInit();
        if (!CanPlaceFloor(x, z, level)) return null;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = FloorName(x, z, level);
        go.transform.SetParent(floorRoot, false);
        go.transform.localPosition = FloorCenterLocal(x, z, level);
        go.transform.localScale = FloorScale();
        var piece = go.AddComponent<FloorPiece>();
        piece.x = x; piece.z = z; piece.level = level;
        piece.styleName = string.IsNullOrEmpty(styleName) ? DefaultFloorStyleName : styleName;
        go.GetComponent<MeshRenderer>().sharedMaterial = FloorMaterialFor(piece.styleName);
        floors[CellKey(x, z, level)] = piece;
        return piece;
    }

    /// <summary>Repaint an existing tile with another style.</summary>
    public void SetFloorStyle(int x, int z, int level, string styleName)
    {
        if (string.IsNullOrEmpty(styleName)) return;
        if (!floors.TryGetValue(CellKey(x, z, level), out var piece)) return;
        if (piece.styleName == styleName) return;
        piece.styleName = styleName;
        piece.GetComponent<MeshRenderer>().sharedMaterial = FloorMaterialFor(styleName);
    }

    public bool HasRoofAt(int x, int z, int level = 0) => roofs.ContainsKey(CellKey(x, z, level));
    public bool TryGetRoofAt(int x, int z, int level, out RoofPiece piece) =>
        roofs.TryGetValue(CellKey(x, z, level), out piece);

    /// <summary>Building footprint at a level: cells enclosed by the storey-below
    /// walls, found by flood-filling from outside the plot. Handles any shape,
    /// including concave outlines and multiple separate buildings.</summary>
    public List<Vector2Int> ComputeRoofFootprint(int level)
    {
        if (level <= 0 || level >= maxLevels) return new List<Vector2Int>();
        return ComputeEnclosedCells(level - 1);
    }

    /// <summary>Cells enclosed by the walls of the given storey.</summary>
    public List<Vector2Int> ComputeEnclosedCells(int wallLevel)
    {
        var cells = new List<Vector2Int>();
        if (wallLevel < 0 || wallLevel >= maxLevels) return cells;
        int wl = wallLevel;

        var outside = new bool[width, height];
        var queue = new Queue<Vector2Int>();
        void Seed(int x, int z)
        {
            if (outside[x, z]) return;
            outside[x, z] = true;
            queue.Enqueue(new Vector2Int(x, z));
        }
        for (int x = 0; x < width; x++)
        {
            if (!HasWall(x, 0, 0, wl)) Seed(x, 0);
            if (!HasWall(x, height, 0, wl)) Seed(x, height - 1);
        }
        for (int z = 0; z < height; z++)
        {
            if (!HasWall(0, z, 1, wl)) Seed(0, z);
            if (!HasWall(width, z, 1, wl)) Seed(width - 1, z);
        }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            void Spread(int nx, int nz, bool blocked)
            {
                if (blocked || nx < 0 || nx >= width || nz < 0 || nz >= height || outside[nx, nz]) return;
                outside[nx, nz] = true;
                queue.Enqueue(new Vector2Int(nx, nz));
            }
            Spread(c.x, c.y + 1, HasWall(c.x, c.y + 1, 0, wl));
            Spread(c.x, c.y - 1, HasWall(c.x, c.y, 0, wl));
            Spread(c.x + 1, c.y, HasWall(c.x + 1, c.y, 1, wl));
            Spread(c.x - 1, c.y, HasWall(c.x, c.y, 1, wl));
        }
        for (int x = 0; x < width; x++)
            for (int z = 0; z < height; z++)
                if (!outside[x, z]) cells.Add(new Vector2Int(x, z));
        return cells;
    }

    /// <summary>Total build cost from the current grid state (recomputed, so
    /// erasing refunds naturally).</summary>
    public int ComputeTotalCost()
    {
        EnsureInit();
        int total = 0;
        foreach (var w in walls.Values)
        {
            total += wallPrice;
            if (w.opening == WallOpening.Door) total += doorPrice;
            else if (w.opening == WallOpening.Window) total += windowPrice;
        }
        total += floors.Count * floorPrice;
        total += StairCount * stairPrice;
        total += roofs.Count * roofPricePerCell; // one entry per covered cell
        foreach (var f in furniture)
        {
            var item = FindItem(f.itemName);
            if (item != null) total += item.price;
        }
        return total;
    }

    /// <summary>Whether a storey has any walls or floors.</summary>
    public bool LevelHasContent(int level)
    {
        foreach (var k in walls.Keys) if (LevelOfKey(k) == level) return true;
        foreach (var k in floors.Keys) if (LevelOfKey(k) == level) return true;
        return false;
    }

    /// <summary>Whether a storey has any floor tiles.</summary>
    public bool LevelHasFloor(int level)
    {
        foreach (var k in floors.Keys) if (LevelOfKey(k) == level) return true;
        return false;
    }

    /// <summary>The floor tile nearest the level's floor centroid — a guaranteed
    /// place to stand when walking an upper storey (terraces have no walls, so the
    /// wall-enclosure spawn can miss).</summary>
    public bool TryGetFloorSpawn(int level, out Vector3 world)
    {
        world = default;
        Vector2 sum = Vector2.zero;
        int n = 0;
        foreach (var kv in floors)
        {
            if (LevelOfKey(kv.Key) != level) continue;
            sum += new Vector2(kv.Value.x, kv.Value.z);
            n++;
        }
        if (n == 0) return false;
        Vector2 centroid = sum / n;
        FloorPiece best = null;
        float bestD = float.MaxValue;
        foreach (var kv in floors)
        {
            if (LevelOfKey(kv.Key) != level) continue;
            float d = (new Vector2(kv.Value.x, kv.Value.z) - centroid).sqrMagnitude;
            if (d < bestD) { bestD = d; best = kv.Value; }
        }
        world = transform.TransformPoint(new Vector3((best.x + 0.5f) * cellSize, LevelBaseY(level), (best.z + 0.5f) * cellSize));
        return true;
    }

    /// <summary>Whether a roof sits at this level.</summary>
    public bool LevelHasRoof(int level)
    {
        foreach (var k in roofs.Keys) if (LevelOfKey(k) == level) return true;
        return false;
    }

    /// <summary>Whether a roof sits at this level or anywhere below it.</summary>
    public bool LevelHasRoofAtOrBelow(int level)
    {
        foreach (var k in roofs.Keys) if (LevelOfKey(k) <= level) return true;
        return false;
    }

    /// <summary>A point inside the enclosed area of a storey (the cell nearest the
    /// enclosure centroid). False when this storey's walls enclose nothing.</summary>
    public bool TryGetInteriorSpawn(int storey, out Vector3 world)
    {
        world = default;
        var cells = ComputeRoofFootprint(storey + 1); // enclosure formed by this storey's walls
        if (cells.Count == 0) return false;
        Vector2 centroid = Vector2.zero;
        foreach (var cell in cells) centroid += new Vector2(cell.x, cell.y);
        centroid /= cells.Count;
        Vector2Int best = cells[0];
        float bestD = float.MaxValue;
        foreach (var cell in cells)
        {
            float d = (new Vector2(cell.x, cell.y) - centroid).sqrMagnitude;
            if (d < bestD) { bestD = d; best = cell; }
        }
        world = transform.TransformPoint(new Vector3((best.x + 0.5f) * cellSize, LevelBaseY(storey), (best.y + 0.5f) * cellSize));
        return true;
    }

    /// <summary>Roofable cells at a level: enclosed by the storey below AND not
    /// intersecting anything at this level or above — cells carrying floors or
    /// stairs, or sitting inside a higher storey's walls, are excluded. This lets
    /// mixed-height buildings roof each wing at its own height.</summary>
    public List<Vector2Int> ComputeRoofableCells(int level)
    {
        var cells = ComputeRoofFootprint(level);
        if (cells.Count == 0) return cells;

        var blocked = new HashSet<Vector2Int>();
        for (int l = level; l < maxLevels; l++)
            foreach (var c in ComputeEnclosedCells(l))
                blocked.Add(c);

        var result = new List<Vector2Int>();
        foreach (var c in cells)
        {
            bool occupied = blocked.Contains(c);
            for (int l = level; l < maxLevels && !occupied; l++)
                if (HasFloor(c.x, c.y, l) || HasStair(c.x, c.y, l))
                    occupied = true;
            if (!occupied) result.Add(c);
        }
        return result;
    }

    public bool CanPlaceAutoRoof(int level) => ComputeRoofableCells(level).Count > 0;

    /// <summary>Generate (or regenerate) the single auto hip roof for a level.</summary>
    public RoofPiece PlaceAutoRoof(int level)
    {
        EnsureInit();
        if (!CanPlaceAutoRoof(level)) return null;
        RoofPiece existing = null;
        foreach (var kv in roofs) if (kv.Value.level == level) { existing = kv.Value; break; }
        if (existing != null) RemoveRoofPiece(existing);

        var go = new GameObject(RoofName(level));
        go.transform.SetParent(roofRoot, false);
        var piece = go.AddComponent<RoofPiece>();
        piece.level = level;
        if (!RebuildRoofGeometry(piece)) return null; // empty footprint
        return piece;
    }

    /// <summary>Remove the roof covering the given cell.</summary>
    public void RemoveRoofAt(int x, int z, int level = 0)
    {
        if (roofs.TryGetValue(CellKey(x, z, level), out var piece)) RemoveRoofPiece(piece);
    }

    void RemoveRoofPiece(RoofPiece piece)
    {
        var stale = new List<long>();
        foreach (var kv in roofs) if (kv.Value == piece) stale.Add(kv.Key);
        foreach (var k in stale) roofs.Remove(k);
        Kill(piece.gameObject);
    }

    public StairPiece PlaceStair(int x, int z, int rot, int level = 0, bool cutOpening = true)
    {
        EnsureInit();
        if (!CanPlaceStair(x, z, rot, level)) return null;
        var go = new GameObject(StairName(x, z, rot, level));
        go.transform.SetParent(stairRoot, false);
        var piece = go.AddComponent<StairPiece>();
        piece.x = x; piece.z = z; piece.rot = rot; piece.level = level;
        int tx = x + StairDX[rot], tz = z + StairDZ[rot];
        stairs[CellKey(x, z, level)] = piece;
        stairs[CellKey(tx, tz, level)] = piece;
        RebuildStairGeometry(piece);
        // Cut the stairwell opening in the slab above (skipped when loading a save,
        // whose floor list already reflects the cut)
        if (cutOpening)
        {
            RemoveFloor(x, z, level + 1);
            RemoveFloor(tx, tz, level + 1);
        }
        return piece;
    }

    /// <summary>Remove the staircase occupying the given cell (either of its two cells).</summary>
    public void RemoveStairAt(int x, int z, int level = 0)
    {
        if (!stairs.TryGetValue(CellKey(x, z, level), out var piece)) return;
        stairs.Remove(CellKey(piece.x, piece.z, piece.level));
        stairs.Remove(CellKey(piece.x + StairDX[piece.rot], piece.z + StairDZ[piece.rot], piece.level));
        Kill(piece.gameObject);
    }

    public void RemoveWall(int x, int z, int dir, int level = 0)
    {
        long key = WallKey(x, z, dir, level);
        if (walls.TryGetValue(key, out var piece))
        {
            walls.Remove(key);
            Kill(piece.gameObject);
        }
    }

    public void RemoveFloor(int x, int z, int level = 0)
    {
        long key = CellKey(x, z, level);
        if (floors.TryGetValue(key, out var piece))
        {
            floors.Remove(key);
            Kill(piece.gameObject);
        }
    }

    // ---- Serialization support ----

    public IEnumerable<WallPiece> EnumerateWalls()
    {
        EnsureInit();
        return walls.Values;
    }

    public IEnumerable<FloorPiece> EnumerateFloors()
    {
        EnsureInit();
        return floors.Values;
    }

    public IEnumerable<StairPiece> EnumerateStairs()
    {
        EnsureInit();
        var seen = new HashSet<StairPiece>();
        foreach (var p in stairs.Values)
            if (seen.Add(p)) yield return p;
    }

    public IEnumerable<int> EnumerateRoofLevels()
    {
        EnsureInit();
        var seen = new HashSet<int>();
        foreach (var p in roofs.Values)
            if (seen.Add(p.level)) yield return p.level;
    }

    /// <summary>Wipe every placed piece (used when applying a save).</summary>
    public void ClearAll()
    {
        EnsureInit();
        void Wipe(Transform root)
        {
            var list = new List<GameObject>();
            foreach (Transform c in root) list.Add(c.gameObject);
            foreach (var go in list) Kill(go);
        }
        Wipe(wallRoot);
        Wipe(floorRoot);
        Wipe(stairRoot);
        Wipe(roofRoot);
        Wipe(furnRoot);
        walls.Clear();
        floors.Clear();
        stairs.Clear();
        roofs.Clear();
        furniture.Clear();
    }

    // ---- Wall geometry ----

    /// <summary>Wipe and regenerate a wall's child cubes from its WallPiece data.</summary>
    public void RebuildWallGeometry(WallPiece piece)
    {
        var t = piece.transform;
        t.localPosition = WallCenterLocal(piece.x, piece.z, piece.dir, piece.level);
        t.localRotation = piece.dir == 1 ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
        t.localScale = Vector3.one;

        var old = new List<GameObject>();
        foreach (Transform c in t) old.Add(c.gameObject);
        foreach (var c in old) Kill(c);

        BuildWallChildren(piece);
    }

    /// <summary>Regenerate an auto roof from the current storey-below walls.
    /// Returns false (and destroys the piece) when the footprint is empty.</summary>
    public bool RebuildRoofGeometry(RoofPiece piece)
    {
        var stale = new List<long>();
        foreach (var kv in roofs) if (kv.Value == piece) stale.Add(kv.Key);
        foreach (var k in stale) roofs.Remove(k);

        var cells = ComputeRoofableCells(piece.level);
        if (cells.Count == 0)
        {
            Kill(piece.gameObject);
            return false;
        }

        var t = piece.transform;
        t.localPosition = new Vector3(0f, LevelBaseY(piece.level), 0f);
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        var old = new List<GameObject>();
        foreach (Transform c in t) old.Add(c.gameObject);
        foreach (var c in old) Kill(c);

        BuildHipRoofMesh(t, cells, roofMaterial);
        foreach (var c in cells)
            roofs[CellKey(c.x, c.y, piece.level)] = piece;
        return true;
    }

    /// <summary>
    /// Discrete hip roof over an arbitrary cell footprint: a Chebyshev distance
    /// transform gives every cell its distance to the outline, corner heights are
    /// distance * roofPitch, and each cell becomes a 4-triangle fan. Ridges and
    /// hip lines emerge from the distance field; unshared vertices keep hard edges.
    /// Coordinates are plot-local (parent sits at the grid origin of its level).
    /// </summary>
    public void BuildHipRoofMesh(Transform parent, List<Vector2Int> cells, Material m)
    {
        var inSet = new HashSet<Vector2Int>(cells);
        var dist = new Dictionary<Vector2Int, int>();
        var q = new Queue<Vector2Int>();
        foreach (var c in cells)
        {
            bool boundary = false;
            for (int dx = -1; dx <= 1 && !boundary; dx++)
                for (int dz = -1; dz <= 1 && !boundary; dz++)
                    if (!inSet.Contains(new Vector2Int(c.x + dx, c.y + dz))) boundary = true;
            if (boundary) { dist[c] = 1; q.Enqueue(c); }
        }
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            int d = dist[c];
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var n = new Vector2Int(c.x + dx, c.y + dz);
                    if (!inSet.Contains(n) || dist.ContainsKey(n)) continue;
                    dist[n] = d + 1;
                    q.Enqueue(n);
                }
        }

        float CornerH(int cx, int cz)
        {
            int min = int.MaxValue;
            for (int dx = -1; dx <= 0; dx++)
                for (int dz = -1; dz <= 0; dz++)
                {
                    var cc = new Vector2Int(cx + dx, cz + dz);
                    int d = inSet.Contains(cc) ? dist[cc] : 0;
                    if (d < min) min = d;
                }
            return min * roofPitch;
        }

        var verts = new List<Vector3>();
        var tris = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        float s = cellSize;
        foreach (var c in cells)
        {
            float x0 = c.x * s, z0 = c.y * s, x1 = x0 + s, z1 = z0 + s;
            var a = new Vector3(x0, CornerH(c.x, c.y), z0);
            var b = new Vector3(x1, CornerH(c.x + 1, c.y), z0);
            var cc = new Vector3(x1, CornerH(c.x + 1, c.y + 1), z1);
            var d = new Vector3(x0, CornerH(c.x, c.y + 1), z1);
            var mid = new Vector3((x0 + x1) * 0.5f, (a.y + b.y + cc.y + d.y) * 0.25f, (z0 + z1) * 0.5f);
            Tri(a, mid, b); Tri(b, mid, cc); Tri(cc, mid, d); Tri(d, mid, a);
            // underside, facing down
            Tri(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1));
            Tri(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1));
        }

        var go = new GameObject("RoofMesh");
        go.transform.SetParent(parent, false);
        var mesh = new Mesh { name = "AutoRoof" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        go.AddComponent<MeshCollider>().sharedMesh = mesh; // geometry-first pointer picking
    }

    /// <summary>Flat ghost plane over a footprint (plot-local, y = 0). Used by the
    /// roof tool preview — no need to show the real roof shape.</summary>
    public void BuildFootprintPlane(Transform parent, List<Vector2Int> cells, Material m)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float s = cellSize;
        foreach (var c in cells)
        {
            int i = verts.Count;
            float x0 = c.x * s, z0 = c.y * s, x1 = x0 + s, z1 = z0 + s;
            verts.Add(new Vector3(x0, 0f, z0));
            verts.Add(new Vector3(x1, 0f, z0));
            verts.Add(new Vector3(x1, 0f, z1));
            verts.Add(new Vector3(x0, 0f, z1));
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
        }
        var go = new GameObject("FootprintPlane");
        go.transform.SetParent(parent, false);
        var mesh = new Mesh { name = "Footprint" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    /// <summary>Wipe and regenerate a staircase's step cubes from its StairPiece data.</summary>
    public void RebuildStairGeometry(StairPiece piece)
    {
        var t = piece.transform;
        t.localPosition = StairCenterLocal(piece.x, piece.z, piece.rot, piece.level);
        t.localRotation = Quaternion.Euler(0f, StairYaw(piece.rot), 0f);
        t.localScale = Vector3.one;

        var old = new List<GameObject>();
        foreach (Transform c in t) old.Add(c.gameObject);
        foreach (var c in old) Kill(c);

        BuildStairChildren(t, floorMaterial);
    }

    /// <summary>Canonical staircase steps climbing along +X over a 2-cell run.
    /// Also used by BuildManager for the placement ghost.</summary>
    public void BuildStairChildren(Transform parent, Material m)
    {
        const int steps = 8;
        float run = 2f * cellSize;
        float stepDepth = run / steps;
        float rise = wallHeight / steps;
        for (int i = 0; i < steps; i++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "Step" + i;
            c.transform.SetParent(parent, false);
            float h = (i + 1) * rise;
            c.transform.localPosition = new Vector3(-cellSize + (i + 0.5f) * stepDepth, h * 0.5f, 0f);
            c.transform.localScale = new Vector3(stepDepth, h, cellSize * 0.96f);
            c.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
    }

    void BuildWallChildren(WallPiece piece)
    {
        float L = cellSize + wallThickness;
        float T = wallThickness;
        float H = wallHeight;

        // Modeled walls: the wall's style provides prefabs first, then the global
        // slots, then procedural cubes tinted with the style color.
        // Prefab convention: pivot at bottom-center of the 1 m segment, run along +X.
        var style = FindWallKindStyle(piece.opening, piece.styleName);
        GameObject prefab = style != null && style.prefab != null ? style.prefab
            : piece.opening == WallOpening.Door ? doorWallPrefab
            : piece.opening == WallOpening.Window ? windowWallPrefab
            : wallPrefab;
        if (prefab != null)
        {
            var model = Instantiate(prefab, piece.transform);
            model.name = "Model";
            model.transform.localPosition = new Vector3(0f, -H * 0.5f, 0f); // parent sits at mid-height
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            if (model.GetComponentInChildren<Collider>() == null)
                AddWallColliders(model, piece.opening, L, T, H);
            return;
        }

        // solid walls take their style's material; door/window walls keep the
        // default wall look for their wall portions — their style tints the
        // panel / frame accents below instead
        Material wallM = piece.opening == WallOpening.None
            ? WallKindMaterialFor(WallOpening.None, piece.styleName)
            : wallMaterial;
        Material accentM = piece.opening == WallOpening.None
            ? frameMaterial
            : WallKindMaterialFor(piece.opening, piece.styleName);

        void Cube(string cubeName, Vector3 center, Vector3 scale, Material m)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = cubeName;
            c.transform.SetParent(piece.transform, false);
            c.transform.localPosition = center;
            c.transform.localScale = scale;
            c.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        switch (piece.opening)
        {
            case WallOpening.None:
                Cube("Body", Vector3.zero, new Vector3(L, H, T), wallM);
                break;

            case WallOpening.Door:
            {
                float doorW = cellSize * 0.85f;
                float sideW = (L - doorW) * 0.5f;
                Cube("JambL", new Vector3(-(doorW + sideW) * 0.5f, 0f, 0f), new Vector3(sideW, H, T), wallM);
                Cube("JambR", new Vector3((doorW + sideW) * 0.5f, 0f, 0f), new Vector3(sideW, H, T), wallM);
                Cube("Lintel", new Vector3(0f, doorHeight * 0.5f, 0f), new Vector3(doorW, H - doorHeight, T), wallM);
                Cube("Panel", new Vector3(0f, (doorHeight - H) * 0.5f, 0f), new Vector3(doorW * 0.94f, doorHeight * 0.97f, T * 0.4f), accentM);
                break;
            }

            case WallOpening.Window:
            {
                float winW = cellSize * 0.7f;
                float winH = windowHeadHeight - windowSillHeight;
                float midY = (windowSillHeight + windowHeadHeight) * 0.5f - H * 0.5f;
                float sideW = (L - winW) * 0.5f;
                Cube("Sill", new Vector3(0f, (windowSillHeight - H) * 0.5f, 0f), new Vector3(L, windowSillHeight, T), wallM);
                Cube("Head", new Vector3(0f, (windowHeadHeight + H) * 0.5f - H * 0.5f, 0f), new Vector3(L, H - windowHeadHeight, T), wallM);
                Cube("SideL", new Vector3(-(winW + sideW) * 0.5f, midY, 0f), new Vector3(sideW, winH, T), wallM);
                Cube("SideR", new Vector3((winW + sideW) * 0.5f, midY, 0f), new Vector3(sideW, winH, T), wallM);
                Cube("Frame", new Vector3(0f, midY, 0f), new Vector3(winW + 0.06f, winH + 0.06f, T * 0.5f), accentM);
                Cube("Glass", new Vector3(0f, midY, 0f), new Vector3(winW, winH, T * 0.3f), glassMaterial);
                break;
            }
        }
    }

    // Collider fallback for wall models that ship without one. Door walls get
    // jambs + lintel boxes so the doorway stays walkable in first person.
    void AddWallColliders(GameObject model, WallOpening opening, float L, float T, float H)
    {
        if (opening == WallOpening.Door)
        {
            float doorW = cellSize * 0.85f;
            float sideW = (L - doorW) * 0.5f;
            var left = model.AddComponent<BoxCollider>();
            left.center = new Vector3(-(doorW + sideW) * 0.5f, H * 0.5f, 0f);
            left.size = new Vector3(sideW, H, T);
            var right = model.AddComponent<BoxCollider>();
            right.center = new Vector3((doorW + sideW) * 0.5f, H * 0.5f, 0f);
            right.size = new Vector3(sideW, H, T);
            var lintel = model.AddComponent<BoxCollider>();
            lintel.center = new Vector3(0f, doorHeight + (H - doorHeight) * 0.5f, 0f);
            lintel.size = new Vector3(doorW, H - doorHeight, T);
        }
        else
        {
            var box = model.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, H * 0.5f, 0f);
            box.size = new Vector3(L, H, T);
        }
    }

    static void Kill(Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
