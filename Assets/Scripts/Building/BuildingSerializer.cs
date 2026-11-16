using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The save-file / AI-scoring data model. Roof shapes are not stored — they are
/// re-derived from walls on load, so the file stays minimal and human-readable.
/// </summary>
[Serializable]
public class BuildingData
{
    public int version = 1;
    public string savedAt;
    public int width;
    public int height;
    public int maxLevels;
    public float cellSize;
    public float wallHeight;
    public List<WallEntry> walls = new List<WallEntry>();
    public List<FloorEntry> floors = new List<FloorEntry>();
    public List<StairEntry> stairs = new List<StairEntry>();
    public List<int> roofLevels = new List<int>();
    public List<FurnEntry> furniture = new List<FurnEntry>();
}

[Serializable] public class WallEntry { public int x, z, dir, level; public string opening; public string styleName; }
[Serializable] public class FloorEntry { public int x, z, level; public string style; }
[Serializable] public class StairEntry { public int x, z, rot, level; }
[Serializable] public class FurnEntry { public string item; public int sx, sz, rot, level; }

/// <summary>Capture/apply the plot state as JSON; save/load it on disk.</summary>
public static class BuildingSerializer
{
    public static BuildingData Capture(GridSystem grid)
    {
        var d = new BuildingData
        {
            width = grid.width,
            height = grid.height,
            maxLevels = grid.maxLevels,
            cellSize = grid.cellSize,
            wallHeight = grid.wallHeight
        };
        foreach (var w in grid.EnumerateWalls())
            d.walls.Add(new WallEntry { x = w.x, z = w.z, dir = w.dir, level = w.level, styleName = w.styleName, opening = w.opening.ToString() });
        foreach (var f in grid.EnumerateFloors())
            d.floors.Add(new FloorEntry { x = f.x, z = f.z, level = f.level, style = f.styleName });
        foreach (var s in grid.EnumerateStairs())
            d.stairs.Add(new StairEntry { x = s.x, z = s.z, rot = s.rot, level = s.level });
        foreach (var lv in grid.EnumerateRoofLevels())
            d.roofLevels.Add(lv);
        foreach (var f in grid.EnumerateFurniture())
            d.furniture.Add(new FurnEntry { item = f.itemName, sx = f.sx, sz = f.sz, rot = f.rot, level = f.level });
        return d;
    }

    public static void Apply(GridSystem grid, BuildingData data)
    {
        grid.ClearAll();
        foreach (var w in data.walls)
        {
            var opening = WallOpening.None;
            Enum.TryParse(w.opening, true, out opening);
            grid.PlaceWall(w.x, w.z, w.dir, w.level, opening, w.styleName);
        }
        foreach (var f in data.floors)
            grid.PlaceFloor(f.x, f.z, f.level, f.style);
        // cutOpening false: the saved floor list already reflects any stairwell cuts
        foreach (var s in data.stairs)
            grid.PlaceStair(s.x, s.z, s.rot, s.level, false);
        foreach (var lv in data.roofLevels)
            grid.PlaceAutoRoof(lv);
        foreach (var f in data.furniture)
            grid.PlaceFurniture(grid.FindItem(f.item), f.sx, f.sz, f.rot, f.level);
    }

    public static string ToJson(GridSystem grid) => JsonUtility.ToJson(Capture(grid), true);

    static string PathFor(string slot) => Path.Combine(Application.persistentDataPath, slot + ".json");

    public static void SaveToFile(GridSystem grid, string slot)
    {
        var data = Capture(grid);
        data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string path = PathFor(slot);
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        Debug.Log($"Building saved to {path}");
    }

    public static bool LoadFromFile(GridSystem grid, string slot)
    {
        string path = PathFor(slot);
        if (!File.Exists(path)) return false;
        var data = JsonUtility.FromJson<BuildingData>(File.ReadAllText(path));
        if (data == null) return false;
        Apply(grid, data);
        return true;
    }
}
