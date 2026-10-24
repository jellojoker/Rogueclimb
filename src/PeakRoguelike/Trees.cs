using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PeakRoguelike;

public enum TreeId { None, Rope, Piton, Glider, Balloons, Bandage, SportsDrink, Food, Light, Antidote }

/// <summary>One step of an item tree. Branch is 0 for shared levels, 1/2 for the two paths of a fork.</summary>
public class LevelDef
{
    public string Name = "";
    public string Desc = "";
    public int Branch;
    /// <summary>Item handed out when this level is reached (see Items.Find).</summary>
    public string? Grants;
}

public class TreeDef
{
    public TreeId Id;
    public string Name = "";
    /// <summary>Levels[i] = the options for level i+1. One option = shared, two = a fork (or the branch-specific follow-up).</summary>
    public LevelDef[][] Levels = null!;
    public int Max => Levels.Length;
    public PerkTheme Theme;

    public LevelDef At(int level, int branch)
    {
        level = Mathf.Clamp(level, 1, MaxFor(branch));
        var opts = Levels[level - 1];
        if (opts.Length == 1) return opts[0];
        return opts.FirstOrDefault(o => o.Branch == branch) ?? opts[0];
    }

    /// <summary>How many levels the given path has (paths may differ in length; 0 = before any fork).</summary>
    public int MaxFor(int branch)
    {
        if (branch == 0) return Max;
        int n = 0;
        foreach (var opts in Levels)
        {
            if (opts.Length == 1 && (opts[0].Branch == 0 || opts[0].Branch == branch)) n++;
            else if (opts.Any(o => o.Branch == branch)) n++;
            else break;
        }
        return n;
    }

    /// <summary>True when reaching `level` asks the player to pick a path.</summary>
    public bool ForksAt(int level, int branch) => level >= 1 && level <= Max && Levels[level - 1].Length > 1 && branch == 0;

    /// <summary>The level at which this tree forks (0 = never).</summary>
    public int ForkLevel
    {
        get
        {
            for (int i = 0; i < Levels.Length; i++)
                if (Levels[i].Length > 1) return i + 1;
            return 0;
        }
    }
}

/// <summary>
/// The roguelike item trees. Every item in a tree has three levels at 33% / 66% / 100% of its
/// vanilla strength (drawbacks 200% / 150% / 100%), then evolves into the next item, which
/// keeps everything the maxed one gave. Trees end in fusions.
/// </summary>
public static class Trees
{
    public static readonly Dictionary<TreeId, TreeDef> All = new();

    // Level colours: worn -> plain -> tempered steel -> gold. The dark medical path is purple.
    public static readonly Color Worn = new Color32(0xA8, 0x8E, 0x74, 0xFF);
    public static readonly Color Plain = new Color32(0xF1, 0xE6, 0xCC, 0xFF);
    public static readonly Color Steel = new Color32(0x8F, 0xD0, 0xFF, 0xFF);
    public static readonly Color Gold = new Color32(0xFF, 0xC8, 0x4F, 0xFF);
    public static readonly Color Saint = new Color32(0xFF, 0xF2, 0xB0, 0xFF);
    public static readonly Color Dark = new Color32(0xB4, 0x6B, 0xFF, 0xFF);

    static LevelDef L(string name, string desc, string grants, int branch = 0) =>
        new() { Name = name, Desc = desc, Branch = branch, Grants = grants };

    static Trees()
    {
        // Rope: spool, then cannon. Floating (anti-) ropes are fusions with a Balloon Bunch.
        Add(TreeId.Rope, "Rope", PerkTheme.Climber,
            new[] { L("Frayed Cord", "A short length of rope.\nSnaps after 2s of climbing.", "RopeSpool") },
            new[] { L("Knotted Line", "A longer length of rope.\nSnaps after 4s of climbing.", "RopeSpool") },
            new[] { L("Rope Spool", "A full spool.\nHolds for good. Fusion-ready.", "RopeSpool") },
            new[] { L("Rope Cannon", "Evolves! Fires an anchored\nrope at full range.", "RopeShooter") },
            new[] { L("Extended Rope Cannon", "Fires an anchored rope\nfurther.", "RopeShooter") },
            new[] { L("Long-Range Rope Cannon", "Fires the furthest.\nFusion-ready.", "RopeShooter") });

        Add(TreeId.Piton, "Piton", PerkTheme.Climber,
            new[] { L("Brittle Piton", "Cracks after 2s\nof hanging.", "Piton") },
            new[] { L("Worn Piton", "Cracks after 4s\nof hanging.", "Piton") },
            new[] { L("Piton", "Holds for good.\nFusion-ready.", "Piton") });

        Add(TreeId.Glider, "Glider", PerkTheme.Climber,
            new[] { L("Fractured Wing", "Weak lift.\nDrains stamina fast.", "Glider") },
            new[] { L("Patched Airfoil", "Steady lift.\nDrains stamina quicker.", "Glider") },
            new[] { L("Glider", "Full lift and stamina use.\nFusion-ready.", "Glider") });

        Add(TreeId.Balloons, "Balloons", PerkTheme.Climber,
            new[] { L("Leaky Balloon", "Weak lift.\nPops early.", "Balloons") },
            new[] { L("Patched Balloon", "Steady lift.\nPops a little early.", "Balloons") },
            new[] { L("Balloon", "Full lift and float time.", "Balloons") },
            new[] { L("Leaky Balloon Bunch", "Evolves! A whole bunch.\nPops early.", "BalloonBunch") },
            new[] { L("Patched Balloon Bunch", "Pops a little early.", "BalloonBunch") },
            new[] { L("Balloon Bunch", "Full float time.\nFusion-ready.", "BalloonBunch") });

        // Bandages fork once they reach vanilla. Both paths are the game's First Aid Kit: the holy
        // one heals now, the dark (purple) one turns injury and glass into ailments that fade.
        // (Which path a kit is lives in its level stamp - see ItemLevels.)
        Add(TreeId.Bandage, "Bandage", PerkTheme.Mountaineer,
            new[] { L("Frayed Gauze", "Heals a little.", "Bandage") },
            new[] { L("Field Gauze", "Heals some.", "Bandage") },
            new[] { L("Bandages", "Full healing.\nThe path forks next.", "Bandage") },
            new[] { L("Blessed Health Pack", "Evolves! Heals 50% of\neverything wrong with you.", "FirstAidKit", 1),
                    L("Murky Medkit", "Evolves! Half your wounds\nand glass turn to poison.", "FirstAidKit", 2) },
            new[] { L("Holy Health Pack", "Heals everything\nwrong with you.", "FirstAidKit", 1),
                    L("Dark Medkit", "Your wounds and glass\nturn to poison.", "FirstAidKit", 2) },
            new[] { L("Radiant Health Pack", "Heals everything, and more.\nFusion-ready.", "FirstAidKit", 1),
                    L("Abyssal Medkit", "Your wounds turn to sleep.\nGlass shatters. Fusion-ready.", "FirstAidKit", 2) });

        // The Sports Drink evolves into the Energy Drink, which keeps a full sports drink inside.
        Add(TreeId.SportsDrink, "Sports Drink", PerkTheme.Mountaineer,
            new[] { L("Flat Sports Drink", "A little stamina\nand cooling.", "SportsDrink") },
            new[] { L("Watered Sports Drink", "Some stamina\nand cooling.", "SportsDrink") },
            new[] { L("Sports Drink", "Full stamina\nand cooling.", "SportsDrink") },
            new[] { L("Fizzled Energy Drink", "Evolves! A short speed boost,\na hard crash. + a sports drink.", "EnergyDrink") },
            new[] { L("Energy Drink", "A longer speed boost.\n+ a sports drink.", "EnergyDrink") },
            new[] { L("Charged Energy Drink", "Full speed boost,\n+ a sports drink. Fusion-ready.", "EnergyDrink") });

        // Food: three steps of trail mix, three of granola (which also gives a full trail mix, and
        // mends a little), then Airline Food - vanilla's meal that heals and mends glass, on its own.
        // These only ever come out of crates. Steps are 50% / 75% / 100% (Stats.FoodShare).
        Add(TreeId.Food, "Food", PerkTheme.Mountaineer,
            new[] { L("Crushed Trail Mix", "Half a handful of energy\nfor the climb ahead.", "TrailMix") },
            new[] { L("Stale Trail Mix", "Most of a handful\nof energy.", "TrailMix") },
            new[] { L("Trail Mix", "A full handful of energy\nfor the climb ahead.", "TrailMix") },
            new[] { L("Crumbled Granola Bar", "Evolves! Half a granola bar,\na full trail mix, and it mends.", "Granola") },
            new[] { L("Stale Granola Bar", "Most of a granola bar,\na full trail mix, and it mends.", "Granola") },
            new[] { L("Granola Bar", "A full granola bar,\na full trail mix, and it mends.", "Granola") },
            new[] { L("Airline Food", "Evolves! A full meal that heals\nand mends glass. Fusion-ready.", "AirlineFood") });

        // Lanterns only - candles stay plain vanilla items (they're a Faerie Lantern ingredient).
        Add(TreeId.Light, "Lantern", PerkTheme.Explorer,
            new[] { L("Faltering Flame", "Dim light.\nBurns out fast.", "Lantern") },
            new[] { L("Guttering Flame", "Fair light.\nBurns a while.", "Lantern") },
            new[] { L("Lantern", "Full brightness and burn time.\nFusion-ready.", "Lantern") });

        // The Antidote: every level adds a cure to the one before (Medicine). Poison is one of the ailments.
        Add(TreeId.Antidote, "Antidote", PerkTheme.Mountaineer,
            new[] { L("Diluted Antidote", "Eases your worst ailments.", "Antidote") },
            new[] { L("Antidote", "Eases your ailments\nand injury.", "Antidote") },
            new[] { L("Potent Antidote", "Eases your ailments, injury\nand hunger. Fusion-ready.", "Antidote") });
    }

    static void Add(TreeId id, string name, PerkTheme theme, params LevelDef[][] levels) =>
        All[id] = new TreeDef { Id = id, Name = name, Theme = theme, Levels = levels };

    public static TreeDef? Get(TreeId id) => All.TryGetValue(id, out var t) ? t : null;

    /// <summary>Card / tint colour for a level.</summary>
    public static Color LevelColor(TreeId id, int level, int branch)
    {
        var tree = Get(id);
        if (tree == null || level <= 0) return Plain;
        if (id == TreeId.Bandage && level >= 4) return branch == 2 ? Dark : Saint;
        if (level >= tree.MaxFor(branch)) return Gold;
        // Each item's own three steps: worn, plain, steel.
        return ((level - 1) % 3) switch { 0 => Worn, 1 => Plain, _ => Steel };
    }

    /// <summary>Path name shown on fork cards and the dev panel.</summary>
    public static string PathName(TreeId id, int branch) => (id, branch) switch
    {
        (TreeId.Bandage, 1) => "Holy path",
        (TreeId.Bandage, 2) => "Dark path",
        _ => $"Path {branch}",
    };

    /// <summary>Which tree an item (prefab or instance) belongs to.</summary>
    public static TreeId Classify(Item? item)
    {
        if (item == null) return TreeId.None;
        string n = Items.Key(item);
        // Floating ropes are fusions: no levels, always vanilla.
        if (item.GetComponent<RopeSpool>() is { isAntiRope: true } || Items.IsAntiRopeShooter(item)) return TreeId.None;
        if (item.GetComponent<RopeSpool>() || item.GetComponent<RopeShooter>()) return TreeId.Rope;
        if (item.GetComponent<ClimbingSpikeComponent>()) return TreeId.Piton;
        if (item.GetComponentInChildren<Glider>(true)) return TreeId.Glider;
        if (item.GetComponent<Balloon>()) return TreeId.Balloons;
        if (n.Contains("bandage") || Items.IsMedKit(item)) return TreeId.Bandage;
        if (n.Contains("sports") || Items.IsEnergyDrink(item)) return TreeId.SportsDrink;
        if (n.Contains("trailmix") || n.Contains("trail mix") || n.Contains("granola")
            || n.Contains("airplane food") || n.Contains("airline food")) return TreeId.Food;
        if (item.GetComponent<Lantern>() && !Items.IsFaerie(item)) return TreeId.Light;
        if (Items.IsAntidote(item) && !Items.IsCureAll(item)) return TreeId.Antidote;
        return TreeId.None;
    }

    /// <summary>The plain item a brand-new tree hands you (Items.Find role) - the tree's one crate entry.</summary>
    public static string BaseRole(TreeId id) => id switch
    {
        TreeId.Rope => "RopeSpool",
        TreeId.Piton => "Piton",
        TreeId.Glider => "Glider",
        TreeId.Balloons => "Balloons",
        TreeId.Bandage => "Bandage",
        TreeId.SportsDrink => "SportsDrink",
        TreeId.Food => "TrailMix",
        TreeId.Antidote => "Antidote",
        _ => "Lantern",
    };

    /// <summary>Is this the base (crate) item of its tree?</summary>
    public static bool IsBase(Item item)
    {
        var id = Classify(item);
        return id != TreeId.None && Items.Find(BaseRole(id)) is { } b && b.itemID == item.itemID;
    }
}
