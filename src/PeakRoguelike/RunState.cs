using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine.SceneManagement;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

/// <summary>
/// This player's roguelike progress for the current run: item tree levels, chosen paths,
/// relics, and catalyst flags. Everything resets when a new run starts.
/// Levels and relics are mirrored into Photon player properties so other clients can
/// show the right colours / names for items you hold.
/// </summary>
internal static class RunState
{
    private const string LevelsKey = "prl.lv";
    private const string RelicsKey = "prl.rel";
    private const string CraftsKey = "prl.cr";

    private static readonly Dictionary<TreeId, (int level, int branch)> levels = new();
    private static readonly Dictionary<CraftId, int> crafts = new();
    private static readonly Dictionary<RelicGem, RelicTrigger> relics = new();
    /// <summary>Relics that are cursed copies (from a skull or a dagger): they shatter on death and don't raise the moon.</summary>
    private static readonly HashSet<RelicGem> cursed = new();
    /// <summary>Relics handed out by the shared-pickups testing option: real relics, but they don't raise the moon's tier.</summary>
    private static readonly HashSet<RelicGem> shared = new();
    /// <summary>Extra copies stacked onto a relic by the Ritual Dagger (each one shortens its cooldown).</summary>
    private static readonly Dictionary<RelicGem, int> stacks = new();
    /// <summary>More variants of a colour you hold, learned from a second amulet of it (food / healing / tools beside the first).</summary>
    private static readonly Dictionary<RelicGem, HashSet<RelicTrigger>> extras = new();
    /// <summary>Statue amulets in our pockets not read yet: they already count as relics for the moon.</summary>
    public static int Unread { get; private set; }
    private const string UnreadKey = "prl.unr";
    /// <summary>Fusions this player has made this run: crates then offer them as an extra card.</summary>
    public static readonly HashSet<string> Fused = new();
    public static bool UsedSkull, UsedDagger;

    public static bool IsCursed(RelicGem gem) => cursed.Contains(gem);
    public static bool IsShared(RelicGem gem) => shared.Contains(gem);
    public static int Stacks(RelicGem gem) => stacks.TryGetValue(gem, out int n) ? n : 0;

    public static void AddStack(RelicGem gem)
    {
        stacks[gem] = Stacks(gem) + 1;
        Publish();
        Changed?.Invoke();
    }

    /// <summary>Every variant of a colour we hold: the first one bound, then the ones learned since.</summary>
    public static List<RelicTrigger> Variants(RelicGem gem)
    {
        var list = new List<RelicTrigger>();
        if (!relics.TryGetValue(gem, out var first)) return list;
        list.Add(first);
        if (extras.TryGetValue(gem, out var more)) list.AddRange(more.Where(t => t != first));
        return list;
    }

    /// <summary>Every (colour, trigger) we hold, extra variants included.</summary>
    public static IEnumerable<(RelicGem gem, RelicTrigger trigger)> AllVariants() =>
        relics.Keys.ToList().SelectMany(g => Variants(g).Select(t => (g, t)));

    /// <summary>All of a colour's variants on a real relic: its triggers cost no glass.</summary>
    public static bool IsComplete(RelicGem gem) =>
        relics.ContainsKey(gem) && !cursed.Contains(gem) && !shared.Contains(gem)
        && Variants(gem).Count >= PeakRoguelike.Relics.All.Count(r => r.Gem == gem);

    /// <summary>Learn another variant of a real relic we hold.</summary>
    public static void AddVariant(RelicGem gem, RelicTrigger trigger)
    {
        if (!relics.ContainsKey(gem)) { SetRelic(gem, trigger); return; }
        if (!extras.TryGetValue(gem, out var set)) extras[gem] = set = new HashSet<RelicTrigger>();
        set.Add(trigger);
        RunReport.RelicBound(gem, trigger, false);
        Publish();
        Changed?.Invoke();
    }

    public static void SetUnread(int n)
    {
        if (n == Unread) return;
        Unread = n;
        Publish();
    }

    /// <summary>A player's unread amulets (theirs from the property).</summary>
    public static int UnreadOf(Photon.Realtime.Player p) =>
        p.IsLocal ? Unread : p.CustomProperties.TryGetValue(UnreadKey, out object raw) && raw is int n ? n : 0;

    public static event Action? Changed;

    /// <summary>Something the relic tray shows changed outside RunState (a starting relic).</summary>
    internal static void Touch() => Changed?.Invoke();

    public static IReadOnlyDictionary<RelicGem, RelicTrigger> Relics => relics;

    public static void Install()
    {
        SceneManager.sceneLoaded += (scene, _) =>
        {
            string n = scene.name.ToLowerInvariant();
            if (n == "airport" || n.Contains("title") || n.Contains("menu")) Reset();
        };
    }

    public static void Reset()
    {
        levels.Clear();
        relics.Clear();
        cursed.Clear();
        shared.Clear();
        stacks.Clear();
        extras.Clear();
        Unread = 0;
        Fused.Clear();
        crafts.Clear();
        UsedSkull = UsedDagger = false;
        RelicDrops.Clear();
        Blessing.Reset();
        Xp.Reset();
        Luck.Reset();
        Storage.Reset();
        ItemLevels.Reset();
        PerkLuggage.ClearRoom();
        DeathDrops.Reset();
        CurioRelics.Reset();
        Badges.Reset();
        RunReport.Reset();
        PeakRoguelike.Relics.ResetOffers();
        Publish();
        Changed?.Invoke();
        Plugin.Log.LogInfo("Run progress reset.");
    }

    // --- levels --------------------------------------------------------------------------

    public static int Level(TreeId id) => levels.TryGetValue(id, out var v) ? v.level : 0;
    public static int Branch(TreeId id) => levels.TryGetValue(id, out var v) ? v.branch : 0;

    /// <summary>Maxed on the path you took (the bandage paths have different lengths).</summary>
    public static bool IsMaxed(TreeId id)
    {
        var tree = Trees.Get(id);
        return tree != null && Level(id) >= tree.MaxFor(Branch(id));
    }

    // --- crafted items ---------------------------------------------------------------------

    public static int CraftLevel(CraftId id) => crafts.TryGetValue(id, out int v) ? v : 0;

    public static void SetCraftLevel(CraftId id, int level)
    {
        crafts[id] = level;
        Publish();
        Changed?.Invoke();
    }

    /// <summary>Crafted-item level for any character (0 = never crafted).</summary>
    public static int CraftLevelOf(Character? c, CraftId id)
    {
        if (c == null || id == CraftId.None) return 0;
        if (c.IsLocal) return CraftLevel(id);
        var owner = c.photonView?.Owner;
        if (owner == null || !owner.CustomProperties.TryGetValue(CraftsKey, out object raw) || raw is not string s) return 0;
        foreach (string part in s.Split(';'))
        {
            var bits = part.Split(':');
            if (bits.Length == 2 && bits[0] == id.ToString() && int.TryParse(bits[1], out int lv)) return lv;
        }
        return 0;
    }

    public static void SetLevel(TreeId id, int level, int branch)
    {
        levels[id] = (level, branch);
        Publish();
        Changed?.Invoke();
    }

    public static void SetRelic(RelicGem gem, RelicTrigger trigger, bool isCursed = false, bool isShared = false)
    {
        relics[gem] = trigger;
        extras.Remove(gem); // a fresh (or traded-up) relic starts its set again
        if (isCursed) cursed.Add(gem); else cursed.Remove(gem);
        if (isShared && !isCursed) shared.Add(gem); else shared.Remove(gem);
        RunReport.RelicBound(gem, trigger, isCursed);
        Publish();
        Changed?.Invoke();
    }

    public static void RemoveRelic(RelicGem gem)
    {
        if (!relics.Remove(gem)) return;
        extras.Remove(gem);
        cursed.Remove(gem);
        shared.Remove(gem);
        stacks.Remove(gem);
        Publish();
        Changed?.Invoke();
    }

    /// <summary>One player's relics as published: (gem, trigger, cursed copy).</summary>
    public static List<(RelicGem gem, RelicTrigger trigger, bool cursed)> RelicsOf(Photon.Realtime.Player p)
    {
        var list = new List<(RelicGem, RelicTrigger, bool)>();
        if (p.IsLocal)
        {
            foreach (var (g, t) in AllVariants()) list.Add((g, t, cursed.Contains(g)));
            return list;
        }
        if (!p.CustomProperties.TryGetValue(RelicsKey, out object raw) || raw is not string s) return list;
        foreach (string part in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(':');
            if (bits.Length >= 2 && Enum.TryParse(bits[0], out RelicGem g) && Enum.TryParse(bits[1], out RelicTrigger t))
                list.Add((g, t, bits.Length >= 3 && bits[2] == "c"));
        }
        return list;
    }

    /// <summary>Every player carrying relics right now (copies and unread amulets included), with how many.</summary>
    public static List<(Photon.Realtime.Player player, int count)> RelicHolders()
    {
        var list = new List<(Photon.Realtime.Player, int)>();
        if (!PhotonNetwork.InRoom) return list;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            int n = RelicsOf(p).Count + UnreadOf(p);
            if (n > 0) list.Add((p, n));
        }
        return list;
    }

    /// <summary>Every relic colour the squad holds right now (yours and the other scouts').</summary>
    public static HashSet<RelicGem> SquadGems()
    {
        var set = new HashSet<RelicGem>(relics.Keys);
        if (!PhotonNetwork.InRoom) return set;
        foreach (var p in PhotonNetwork.PlayerListOthers)
            if (p.CustomProperties.TryGetValue(RelicsKey, out object raw) && raw is string s)
                foreach (string part in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    if (Enum.TryParse(part.Split(':')[0], out RelicGem g)) set.Add(g);
        return set;
    }

    /// <summary>How many real relics the whole squad holds right now: the blood moon's tier. Cursed copies and shared (testing) relics don't count.</summary>
    public static int SquadRelics() =>
        !PhotonNetwork.InRoom ? TierRelics()
            : PhotonNetwork.PlayerList.Sum(p => p.IsLocal ? TierRelics() : TierRelicsOf(p));

    // Every variant of a real relic counts, and so does every unread amulet (the moon rose when it was taken).
    private static int TierRelics() =>
        relics.Keys.Where(g => !cursed.Contains(g) && !shared.Contains(g) && PeakRoguelike.Relics.Cursed(g)).Sum(g => Variants(g).Count) + Unread;

    /// <summary>Another player's relics with no copy mark (":c" cursed, ":s" shared; ":x" = an extra variant, which counts), plus their unread amulets.</summary>
    private static int TierRelicsOf(Photon.Realtime.Player p) => UnreadOf(p) +
        (p.CustomProperties.TryGetValue(RelicsKey, out object raw) && raw is string s
            ? s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Count(part => part.Split(':') is { } bits
                && (bits.Length == 2 || bits.Length == 3 && bits[2] == "x")
                && (!Enum.TryParse(bits[0], out RelicGem g) || PeakRoguelike.Relics.Cursed(g)))
            : 0);

    /// <summary>Level of a tree for any character (local state for us, player properties for others).</summary>
    public static (int level, int branch) LevelOf(Character? c, TreeId id)
    {
        if (c == null || id == TreeId.None) return (0, 0);
        if (c.IsLocal) return (Level(id), Branch(id));
        var owner = c.photonView?.Owner;
        if (owner == null || !owner.CustomProperties.TryGetValue(LevelsKey, out object raw) || raw is not string s) return (0, 0);
        return Parse(owner.ActorNumber, s).TryGetValue(id, out var v) ? v : (0, 0);
    }

    // --- network mirror ------------------------------------------------------------------

    private static readonly Dictionary<int, (string raw, Dictionary<TreeId, (int, int)> parsed)> remote = new();

    private static Dictionary<TreeId, (int, int)> Parse(int actor, string s)
    {
        if (remote.TryGetValue(actor, out var cached) && cached.raw == s) return cached.parsed;
        var d = new Dictionary<TreeId, (int, int)>();
        foreach (string part in s.Split(';'))
        {
            var bits = part.Split(':');
            if (bits.Length == 3 && Enum.TryParse(bits[0], out TreeId id) && int.TryParse(bits[1], out int lv) && int.TryParse(bits[2], out int br))
                d[id] = (lv, br);
        }
        remote[actor] = (s, d);
        return d;
    }

    public static void Republish() => Publish();

    private static void Publish()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
        try
        {
            string lv = string.Join(";", levels.Select(kv => $"{kv.Key}:{kv.Value.level}:{kv.Value.branch}"));
            string rel = string.Join(";", relics.Select(kv => $"{kv.Key}:{kv.Value}"
                + (cursed.Contains(kv.Key) ? ":c" : shared.Contains(kv.Key) ? ":s" : ""))
                .Concat(extras.Where(kv => relics.ContainsKey(kv.Key)).SelectMany(kv => kv.Value.Select(t => $"{kv.Key}:{t}:x"))));
            string cr = string.Join(";", crafts.Select(kv => $"{kv.Key}:{kv.Value}"));
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                [LevelsKey] = lv, [RelicsKey] = rel, [CraftsKey] = cr, [UnreadKey] = Unread, [DevPanel.BuildKey] = DevPanel.Build,
            });
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Couldn't publish run state: {e.Message}");
        }
    }
}
