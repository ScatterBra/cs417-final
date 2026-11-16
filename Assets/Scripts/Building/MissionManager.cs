using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Mission loop state: current brief, difficulty progression, submit/verdict.
/// Persists to mission.json so the current contract survives restarts.
/// AI review plugs in at Submit() later; for now scoring is fully local.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public Brief Current { get; private set; }
    public int difficulty;
    public List<CheckResult> lastResults;
    public int lastScore;
    public int lastMaxScore;
    public bool HasResult => lastResults != null;

    GridSystem grid;
    bool passedCurrent;

    [Serializable]
    class MissionSave
    {
        public int difficulty;
        public bool passedCurrent;
        public bool hasBrief;
        public Brief brief;
    }

    static string SavePath => Path.Combine(Application.persistentDataPath, "mission.json");

    public bool StoryActive { get; private set; }

    void Start()
    {
        grid = FindFirstObjectByType<GridSystem>();
        // Briefs only exist in story mode; BeginSession decides.
    }

    /// <summary>Called by the main menu. Story mode restores/creates a brief;
    /// free mode runs without missions or budget.</summary>
    public void BeginSession(bool story)
    {
        StoryActive = story;
        if (!story)
        {
            Current = null;
            lastResults = null;
            return;
        }
        if (Current == null && File.Exists(SavePath))
        {
            var s = JsonUtility.FromJson<MissionSave>(File.ReadAllText(SavePath));
            if (s != null)
            {
                difficulty = s.difficulty;
                passedCurrent = s.passedCurrent;
                if (s.hasBrief) Current = s.brief;
            }
        }
        if (Current == null) NewBrief();
        else NotifyBackdrop();
    }

    public void NewBrief()
    {
        if (passedCurrent) difficulty++;
        passedCurrent = false;
        Current = BriefGenerator.Generate(difficulty);
        lastResults = null;
        Persist();
        NotifyBackdrop();
    }

    // Each contract gets its own procedural city around the plot.
    void NotifyBackdrop()
    {
        var cb = FindFirstObjectByType<CityBackdrop>();
        if (cb != null) cb.GenerateForBrief(Current);
    }

    public void Submit()
    {
        if (grid == null || Current == null) return;
        lastResults = RuleChecker.Check(grid, Current, out lastScore, out lastMaxScore);
        passedCurrent = lastScore >= lastMaxScore * 0.6f;
        Persist();
    }

    public string VerdictText()
    {
        if (!HasResult) return "";
        float pct = lastMaxScore > 0 ? (float)lastScore / lastMaxScore : 0f;
        return BriefGenerator.Verdict(pct);
    }

    void Persist()
    {
        var s = new MissionSave { difficulty = difficulty, passedCurrent = passedCurrent, hasBrief = Current != null, brief = Current };
        File.WriteAllText(SavePath, JsonUtility.ToJson(s, true));
    }
}
