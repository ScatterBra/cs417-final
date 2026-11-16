using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class CheckResult
{
    public string item;
    public bool pass;
    public string detail;
    public int points;
    public int maxPoints;
}

/// <summary>
/// Objective acceptance checks against the brief. Runs entirely locally; the
/// result list later rides along in the AI-review prompt so the model never has
/// to guess objective facts.
/// </summary>
public static class RuleChecker
{
    public static List<CheckResult> Check(GridSystem grid, Brief brief, out int score, out int maxScore)
    {
        var results = new List<CheckResult>();
        void Add(string item, bool pass, string detail, int maxPts)
        {
            results.Add(new CheckResult { item = item, pass = pass, detail = detail, points = pass ? maxPts : 0, maxPoints = maxPts });
        }

        var allRooms = new List<RoomInfo>();
        for (int l = 0; l < grid.maxLevels; l++)
            allRooms.AddRange(RoomAnalyzer.AnalyzeLevel(grid, l));

        // budget (20)
        int cost = grid.ComputeTotalCost();
        Add("Budget", cost <= brief.budget, $"spent ${cost:N0} / budget ${brief.budget:N0}", 20);

        // storeys (10)
        int used = 0;
        for (int l = 0; l < grid.maxLevels; l++)
            if (grid.LevelHasContent(l)) used = l + 1;
        Add("Floor limit", used >= 1 && used <= brief.maxLevels, $"used {used} / allowed {brief.maxLevels}", 10);

        // rooms (30, split across requirements)
        int reqCount = Mathf.Max(1, brief.requiredRooms.Count);
        for (int i = 0; i < brief.requiredRooms.Count; i++)
        {
            var req = brief.requiredRooms[i];
            int pts = 30 / reqCount + (i == 0 ? 30 % reqCount : 0);
            int have = allRooms.Count(r => r.inferredType == req.type);
            Add($"{BriefGenerator.TypeName(req.type)} × {req.count}", have >= req.count, $"found {have}", pts);
        }

        // lighting (20, split)
        if (brief.lighting.Count > 0)
        {
            int lc = brief.lighting.Count;
            for (int i = 0; i < brief.lighting.Count; i++)
            {
                var lr = brief.lighting[i];
                int pts = 20 / lc + (i == 0 ? 20 % lc : 0);
                bool ok = allRooms.Any(r => r.inferredType == lr.room && r.WindowsFacing(lr.facing) > 0);
                Add($"{BriefGenerator.TypeName(lr.room)} with {BriefGenerator.FacingName(lr.facing)}-facing window", ok,
                    ok ? "met" : "no matching window found", pts);
            }
        }

        // vertical circulation (10)
        bool stairsOk = true;
        string stairDetail = "single storey, no stairs needed";
        if (used > 1)
        {
            var stairLevels = new HashSet<int>(grid.EnumerateStairs().Select(s => s.level));
            for (int s = 0; s < used - 1; s++)
                if (!stairLevels.Contains(s)) stairsOk = false;
            stairDetail = stairsOk ? "all floors connected by stairs" : "some floors have no stair access";
        }
        Add("Stair access", stairsOk, stairDetail, 10);

        // entrance door (5)
        bool hasDoor = grid.EnumerateWalls().Any(w => w.opening == WallOpening.Door);
        Add("Entrance", hasDoor, hasDoor ? "has a door" : "not a single door in the house", 5);

        // roof (5)
        bool hasRoof = grid.RoofCount > 0;
        Add("Roof", hasRoof, hasRoof ? "covered" : "still open to the sky", 5);

        score = results.Sum(r => r.points);
        maxScore = results.Sum(r => r.maxPoints);
        return results;
    }
}
