using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The client's requirement sheet, generated from templates. The same
/// JSON later feeds the AI reviewer.</summary>
[Serializable]
public class Brief
{
    public string title;
    public string persona;
    public int budget;
    public int maxLevels;
    public List<RoomRequirement> requiredRooms = new List<RoomRequirement>();
    public List<LightingRequirement> lighting = new List<LightingRequirement>();
    public List<string> special = new List<string>();
}

[Serializable] public class RoomRequirement { public string type; public int count; }
[Serializable] public class LightingRequirement { public string room; public string facing; }

/// <summary>Procedural brief generation: random assembly from pools, difficulty
/// scales rooms/levels and tightens budget.</summary>
public static class BriefGenerator
{
    static readonly string[] Personas =
    {
        "The Newlywed Couple", "The Cat-Loving Illustrator", "The Retired Teachers",
        "The Remote-Work Programmer", "The Family of Four", "The Band Frontman"
    };

    static readonly string[] SpecialPool =
    {
        "Wants a sunny rooftop terrace", "Loves big windows", "Hates seeing the bedroom right from the entrance",
        "Wants an open dining area", "No staircase in the middle of the living room, please", "Wants an east-facing room for sunrise"
    };

    public static string TypeName(string type)
    {
        switch (type)
        {
            case "bedroom": return "bedroom";
            case "living": return "living room";
            case "dining": return "dining room";
            default: return type;
        }
    }

    public static string FacingName(string facing)
    {
        switch (facing)
        {
            case "south": return "south";
            case "north": return "north";
            case "east": return "east";
            case "west": return "west";
            default: return facing;
        }
    }

    public static Brief Generate(int difficulty)
    {
        int d = Mathf.Clamp(difficulty, 0, 3);
        var rng = new System.Random();
        var b = new Brief();
        b.persona = Personas[rng.Next(Personas.Length)];
        // plural personas ("The Retired Teachers") take a bare apostrophe
        b.title = b.persona + (b.persona.EndsWith("s") ? "' New Home" : "'s New Home");
        b.maxLevels = d == 0 ? 1 : (d == 3 ? 3 : 2);

        int bedrooms = d == 0 ? 1 : (d <= 2 ? 2 : 3);
        if (d == 2 && rng.Next(2) == 0) bedrooms = 3;
        b.requiredRooms.Add(new RoomRequirement { type = "bedroom", count = bedrooms });
        b.requiredRooms.Add(new RoomRequirement { type = "living", count = 1 });
        if (d >= 2 || (d == 1 && rng.Next(2) == 0))
            b.requiredRooms.Add(new RoomRequirement { type = "dining", count = 1 });

        if (d >= 1)
            b.lighting.Add(new LightingRequirement { room = "living", facing = "south" });
        if (d >= 2)
            b.lighting.Add(new LightingRequirement { room = "bedroom", facing = "east" });

        int roomsTotal = 0;
        foreach (var r in b.requiredRooms) roomsTotal += r.count;
        b.budget = 26000 + roomsTotal * 9000 + b.maxLevels * 6000 + rng.Next(0, 6000);

        int specials = Mathf.Min(1 + d / 2 + (d == 3 ? 1 : 0), 3);
        var pool = new List<string>(SpecialPool);
        for (int i = 0; i < specials && pool.Count > 0; i++)
        {
            int k = rng.Next(pool.Count);
            b.special.Add(pool[k]);
            pool.RemoveAt(k);
        }
        return b;
    }

    /// <summary>Template verdict in the client's voice; the AI reviewer will
    /// replace this with real commentary later.</summary>
    public static string Verdict(float pct)
    {
        if (pct >= 0.9f) return "Perfect — this is the house from my dreams! Hand over the keys!";
        if (pct >= 0.75f) return "Quite satisfied overall. A few small regrets, but I can live with them.";
        if (pct >= 0.6f) return "Livable, I suppose... but did you actually read my requirements?";
        if (pct >= 0.4f) return "This is far from what I asked for. It needs rework.";
        return "THIS is your proposal? Tear it down and start over!";
    }
}
