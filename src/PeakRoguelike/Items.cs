using System.Collections.Generic;
using System.Linq;
using Peak;
using UnityEngine;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>Finding vanilla item prefabs by role, and small item helpers.</summary>
internal static class Items
{
    private static readonly Dictionary<string, Item?> found = new();

    public static IEnumerable<Item> Database
    {
        get
        {
            var db = SingletonAsset<ItemDatabase>.Instance;
            return db == null ? Enumerable.Empty<Item>() : db.itemLookup.Values.Where(i => i != null);
        }
    }

    /// <summary>Lower-case prefab + display key used for name matching ("rope spool|rope").</summary>
    public static string Key(Item item)
    {
        string prefab = item.gameObject.name.Replace("(Clone)", "").Trim();
        return (prefab + "|" + (item.UIData?.itemName ?? "")).ToLowerInvariant();
    }

    public static string DisplayName(Item item)
    {
        try { return item.GetName(); }
        catch { return item.UIData?.itemName ?? item.gameObject.name; }
    }

    public static Texture2D? Icon(Item? item)
    {
        if (item == null) return null;
        try { return item.UIData.GetIcon(); } catch { return item.UIData?.icon; }
    }

    public static bool IsFaerie(Item item) { string k = Key(item); return k.Contains("faerie") || k.Contains("fairy"); }
    // The Book of Bones carries the same cursed glow, and there's a plain prop skull - neither counts.
    public static bool IsSkull(Item item) => item.GetComponentInChildren<CursedSkullVFX>(true) && !IsBookOfBones(item);
    public static bool IsBookOfBones(Item item) => item.gameObject.name.Replace("(Clone)", "").Trim() == "BookOfBones" || Key(item).Contains("book of bones");
    public static bool IsCandle(Item item) => item.GetComponent<Candle>();
    public static bool IsEnergyDrink(Item item) => Key(item).Contains("energy drink");
    public static bool IsMedKit(Item item) => Key(item).Replace(" ", "").Contains("firstaid");
    public static bool IsLunchbox(Item item) { string k = Key(item); return k.Contains("pandora") || k.Contains("lunchbox"); }
    public static bool IsDagger(Item item) => item.GetComponent<RitualDaggerFeedBehavior>() || Key(item).Contains("dagger");
    public static bool IsLollipop(Item item) => Key(item).Contains("lollipop") && !Key(item).Contains("prop");
    public static bool IsMilk(Item item) => Key(item).Contains("fortified") || Key(item).Contains("milk");
    public static bool IsSunscreen(Item item) => item != null && Key(item).Contains("sunscreen");
    public static bool IsBalloonBunch(Item item) => item.GetComponent<Balloon>() is { isBunch: true };
    public static bool IsChainLauncher(Item item) => item.GetComponent<VineShooter>();
    public static bool IsRescueClaw(Item item) => item.GetComponent<RescueHook>() && !Key(item).Contains("infinite");
    public static bool IsScoutCannon(Item item) => item.GetComponent<Action_ConstructableScoutCannonScroll>() || Key(item).Replace(" ", "").Contains("scoutcannon");
    public static bool IsAntiRopeShooter(Item item)
    {
        var s = item.GetComponent<RopeShooter>();
        if (!s) return false;
        return Key(item).Contains("anti") || (s.ropeAnchorWithRopePref && s.ropeAnchorWithRopePref.name.ToLowerInvariant().Contains("anti"));
    }

    /// <summary>Find a prefab by role name. Results are cached per session.</summary>
    public static Item? Find(string role)
    {
        if (found.TryGetValue(role, out var hit) && (hit != null || Database.Any())) return hit;
        hit = role switch
        {
            "RopeSpool" => Database.FirstOrDefault(i => i.GetComponent<RopeSpool>() is { isAntiRope: false }),
            "AntiRopeSpool" => Database.FirstOrDefault(i => i.GetComponent<RopeSpool>() is { isAntiRope: true }),
            "RopeShooter" => Database.FirstOrDefault(i => i.GetComponent<RopeShooter>() && !IsAntiRopeShooter(i)),
            "AntiRopeShooter" => Database.FirstOrDefault(IsAntiRopeShooter) ?? Find("AntiRopeSpool"),
            "Jetpack" => Database.FirstOrDefault(i => i.GetComponent<JetpackItem>()),
            "FaerieLantern" => Database.FirstOrDefault(i => i.GetComponent<Lantern>() && IsFaerie(i)),
            "ScoutEffigy" => Database.FirstOrDefault(i => i.GetComponent<ScoutEffigy>() || Key(i).Contains("effigy")),
            "Pandora" => Database.FirstOrDefault(IsLunchbox),
            "SportsDrink" => Database.FirstOrDefault(i => Trees.Classify(i) == TreeId.SportsDrink && Key(i).Contains("sports")),
            "EnergyDrink" => Database.FirstOrDefault(IsEnergyDrink),
            "Piton" => Database.FirstOrDefault(i => i.GetComponent<ClimbingSpikeComponent>()),
            "Balloons" => Database.FirstOrDefault(i => i.GetComponent<Balloon>() is { isBunch: false }),
            "BalloonBunch" => Database.FirstOrDefault(IsBalloonBunch),
            "Lollipop" => Database.FirstOrDefault(IsLollipop),
            "Milk" => Database.FirstOrDefault(i => Key(i).Contains("fortified")) ?? Database.FirstOrDefault(IsMilk),
            "ChainShooter" => Database.FirstOrDefault(IsChainLauncher),
            "RescueHook" => Database.FirstOrDefault(IsRescueClaw),
            "AntiZooka" => Database.FirstOrDefault(i => i.GetComponent<Action_Antizooka>()),
            "Candle" => Database.FirstOrDefault(IsCandle),
            "ScoutCannon" => Database.FirstOrDefault(IsScoutCannon),
            "TrailMix" => Database.FirstOrDefault(i => Key(i).Contains("trailmix") || Key(i).Contains("trail mix")),
            "Granola" => Database.FirstOrDefault(i => Key(i).Contains("granola")),
            "AirlineFood" => Database.FirstOrDefault(i => Key(i).Contains("airplane food") || Key(i).Contains("airline food")),
            "FirstAidKit" => Database.FirstOrDefault(IsMedKit),
            "BookOfBones" => Database.FirstOrDefault(IsBookOfBones),
            "Bandage" => Database.FirstOrDefault(i => Trees.Classify(i) == TreeId.Bandage),
            "Glider" => Database.FirstOrDefault(i => Trees.Classify(i) == TreeId.Glider),
            "Lantern" => Database.FirstOrDefault(i => Trees.Classify(i) == TreeId.Light),
            "CursedSkull" => Database.FirstOrDefault(IsSkull),
            "RitualDagger" => Database.FirstOrDefault(i => i.GetComponent<RitualDaggerFeedBehavior>()),
            "Napberry" => Database.FirstOrDefault(i => Key(i).Contains("napberry")),
            _ => null,
        };
        if (hit == null) Plugin.Log.LogWarning($"Couldn't find an item for '{role}'.");
        found[role] = hit;
        return hit;
    }

    public static bool TryGet(ushort id, out Item item) => ItemDatabase.TryGetItem(id, out item) && item != null;

    // --- earned-only items --------------------------------------------------------------

    private static HashSet<ushort>? earned;

    /// <summary>
    /// Items you only get by fusing, evolving or reaching a tree level - never from the world.
    /// Worked out from the recipes, tree unlocks and crafted items themselves, so anything new
    /// added there is blocked automatically. Plus the game's own late-game tools that our
    /// recipes will make (Chain Launcher, Rescue Claw, Anti-Zooka).
    /// </summary>
    public static bool IsEarnedOnly(Item item)
    {
        if (item == null) return false;
        if (earned == null)
        {
            if (!Database.Any()) return false;
            earned = BuildEarned();
            ApplyFusionTwists();
        }
        return earned.Contains(item.itemID);
    }

    private static bool twisted;

    /// <summary>
    /// Fusion twists that live on the items themselves: the Chain Launcher gets 2 uses, the
    /// Rescue Claw and the Anti-Grav Bazooka 6. Set on the prefabs once, so every copy has them.
    /// </summary>
    public static void ApplyFusionTwists()
    {
        if (twisted || !ModConfig.ItemTrees.Value || !Database.Any()) return;
        twisted = true;
        void Uses(string role, int uses)
        {
            if (Find(role) is not { } prefab || prefab.totalUses <= 0) return;
            Plugin.Log.LogInfo($"Fusion twist: {prefab.gameObject.name} {prefab.totalUses} -> {uses} uses.");
            prefab.totalUses = uses;
        }
        Uses("ChainShooter", ModConfig.ChainLauncherUses.Value);
        Uses("RescueHook", ModConfig.RescueClawUses.Value);
        Uses("AntiZooka", ModConfig.AntiGravUses.Value);
    }

    /// <summary>Food-tree snacks: offered by crates, never lying around the mountain.</summary>
    public static bool IsCrateOnly(Item item) => item != null && Trees.Classify(item) == TreeId.Food;

    private static HashSet<ushort> BuildEarned()
    {
        var roles = new HashSet<string>();
        foreach (var r in Fusion.Recipes) if (r.Result != null) roles.Add(r.Result);
        foreach (var tree in Trees.All.Values)
            foreach (var opts in tree.Levels)
                foreach (var lv in opts)
                    if (lv.Grants != null) roles.Add(lv.Grants);
        foreach (var c in Crafts.All.Values) roles.Add(c.Role);
        // Each tree's one crate entry stays (every higher item of the tree is earned).
        foreach (var id in Trees.All.Keys) roles.Remove(Trees.BaseRole(id));

        var set = new HashSet<ushort>();
        foreach (string role in roles) if (Find(role) is { } prefab) set.Add(prefab.itemID);
        foreach (var i in Database)
            if (i.GetComponent<VineShooter>() || i.GetComponent<RescueHook>() || i.GetComponent<Action_Antizooka>() || IsEnergyDrink(i)
                || i.GetComponent<RopeShooter>() || i.GetComponent<RopeSpool>() is { isAntiRope: true }
                || IsLollipop(i) || IsBalloonBunch(i) || IsMedKit(i) || IsLunchbox(i)
                || (Trees.Classify(i) == TreeId.Food && !Trees.IsBase(i)))
                set.Add(i.itemID);

        Plugin.Log.LogInfo("Earned-only (never spawn, swapped for their base ingredient): "
            + string.Join(", ", set.Select(id => TryGet(id, out var it) ? it.gameObject.name : id.ToString()).OrderBy(n => n)));
        return set;
    }

    // --- amulets --------------------------------------------------------------------------

    /// <summary>One of the four statue amulets (not Scout's Honor or the Strange Gem).</summary>
    public static bool IsAmulet(Item item) =>
        item.itemTags.HasFlag(Item.ItemTags.ScoutAmulet) && !item.GetComponent<ScoutsHonor>() && AmuletGem(item) != null;

    /// <summary>Which relic an amulet binds, by what it does (names follow the game's amulets).</summary>
    public static RelicGem? AmuletGem(Item item)
    {
        if (item.GetComponent<Action_HealingGem>()) return RelicGem.Tenacity;                 // blue: heal + invincible
        if (item.GetComponent<Action_CloneSelectedItem>()) return RelicGem.Generosity;        // green: clone
        if (item.GetComponent<Action_SuperJumpAmulet>()) return RelicGem.Initiative;          // red: team super jump
        if (item.GetComponents<Action_ApplyAffliction>().Any(a => a.affliction is Peak.Afflictions.Affliction_RadiateInfiniteStam))
            return RelicGem.Ambition;                                                         // yellow: infinite stamina
        string k = Key(item);
        if (k.Contains("tenacity")) return RelicGem.Tenacity;
        if (k.Contains("generosity")) return RelicGem.Generosity;
        if (k.Contains("initiative")) return RelicGem.Initiative;
        if (k.Contains("ambition")) return RelicGem.Ambition;
        return null;
    }

    /// <summary>
    /// What an earned-only item turns into when the world tries to spawn it: its base ingredient.
    /// </summary>
    public static Item? BaseIngredient(Item earned)
    {
        if (earned.GetComponent<RopeShooter>() || earned.GetComponent<RopeSpool>() || earned.GetComponent<RescueHook>()) return Find("RopeSpool");
        if (earned.GetComponent<VineShooter>()) return Find("Piton");
        if (earned.GetComponent<Action_Antizooka>() || IsBalloonBunch(earned)) return Find("Balloons");
        if (IsLollipop(earned) || Trees.Classify(earned) == TreeId.Food) return Find("TrailMix");
        if (IsEnergyDrink(earned)) return Find("SportsDrink");
        if (IsMedKit(earned) || IsLunchbox(earned)) return Find("Bandage");
        if (earned.GetComponent<JetpackItem>()) return Find("Glider");
        if (IsFaerie(earned)) return Find("Lantern");
        if (IsBookOfBones(earned)) return Find("RitualDagger");
        if (earned.GetComponent<ScoutEffigy>() || Key(earned).Contains("effigy")) return Find("CursedSkull");
        return null;
    }
}
