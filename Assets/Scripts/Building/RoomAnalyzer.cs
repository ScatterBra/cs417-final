using System.Collections.Generic;
using UnityEngine;

/// <summary>A detected room: a connected component of enclosed cells on one storey.</summary>
public class RoomInfo
{
    public int level;
    public List<Vector2Int> cells = new List<Vector2Int>();
    public int Area => cells.Count; // m², cell = 1 m
    public Dictionary<string, int> windowsByFacing = new Dictionary<string, int>();
    public int doorCount;
    public List<string> furnitureNames = new List<string>();
    public string inferredType = "room";

    public int WindowsFacing(string facing) =>
        windowsByFacing.TryGetValue(facing, out var n) ? n : 0;
}

/// <summary>
/// Splits each storey's enclosed area into rooms (flood fill blocked by walls),
/// counts windows per facing and doors on the bounding walls, and infers the
/// room type from the furniture inside (bed → bedroom, sofa → living, ...).
/// </summary>
public static class RoomAnalyzer
{
    public static List<RoomInfo> AnalyzeLevel(GridSystem grid, int level)
    {
        var rooms = new List<RoomInfo>();
        var cells = grid.ComputeEnclosedCells(level);
        if (cells.Count == 0) return rooms;
        var cellSet = new HashSet<Vector2Int>(cells);
        var visited = new HashSet<Vector2Int>();

        foreach (var start in cells)
        {
            if (visited.Contains(start)) continue;
            var room = new RoomInfo { level = level };
            var q = new Queue<Vector2Int>();
            q.Enqueue(start);
            visited.Add(start);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                room.cells.Add(c);
                void Spread(int nx, int nz, bool blocked)
                {
                    var n = new Vector2Int(nx, nz);
                    if (blocked || !cellSet.Contains(n) || visited.Contains(n)) return;
                    visited.Add(n);
                    q.Enqueue(n);
                }
                Spread(c.x, c.y + 1, grid.HasWall(c.x, c.y + 1, 0, level));
                Spread(c.x, c.y - 1, grid.HasWall(c.x, c.y, 0, level));
                Spread(c.x + 1, c.y, grid.HasWall(c.x + 1, c.y, 1, level));
                Spread(c.x - 1, c.y, grid.HasWall(c.x, c.y, 1, level));
            }

            foreach (var c in room.cells)
            {
                CountEdge(grid, room, c.x, c.y, 0, level, "south");
                CountEdge(grid, room, c.x, c.y + 1, 0, level, "north");
                CountEdge(grid, room, c.x, c.y, 1, level, "west");
                CountEdge(grid, room, c.x + 1, c.y, 1, level, "east");
            }
            rooms.Add(room);
        }

        // furniture membership by footprint center cell
        foreach (var f in grid.EnumerateFurniture())
        {
            if (f.level != level) continue;
            var item = grid.FindItem(f.itemName);
            if (item == null) continue;
            var d = GridSystem.FurnFootprint(item, f.rot);
            int cx = Mathf.FloorToInt((f.sx * grid.SubCell + d.x * 0.5f) / grid.cellSize);
            int cz = Mathf.FloorToInt((f.sz * grid.SubCell + d.y * 0.5f) / grid.cellSize);
            var cc = new Vector2Int(cx, cz);
            foreach (var room in rooms)
            {
                if (!room.cells.Contains(cc)) continue;
                room.furnitureNames.Add(item.name);
                break;
            }
        }

        foreach (var room in rooms)
            room.inferredType = InferType(grid, room);
        return rooms;
    }

    static void CountEdge(GridSystem grid, RoomInfo room, int x, int z, int dir, int level, string facing)
    {
        if (!grid.TryGetWall(x, z, dir, level, out var wall)) return;
        if (wall.opening == WallOpening.Window)
            room.windowsByFacing[facing] = room.WindowsFacing(facing) + 1;
        else if (wall.opening == WallOpening.Door)
            room.doorCount++;
    }

    static string InferType(GridSystem grid, RoomInfo room)
    {
        // priority: bedroom > living > dining (a bed defines a bedroom even with a sofa)
        var hints = new HashSet<string>();
        foreach (var n in room.furnitureNames)
        {
            var item = grid.FindItem(n);
            if (item != null && !string.IsNullOrEmpty(item.roomHint)) hints.Add(item.roomHint);
        }
        if (hints.Contains("bedroom")) return "bedroom";
        if (hints.Contains("living")) return "living";
        if (hints.Contains("dining")) return "dining";
        return "room";
    }
}
