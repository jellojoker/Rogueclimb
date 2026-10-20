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
        if (isCursed) cursed.Add(gem); else cursed.Remove(gem);
        if (isShared && !isCursed) shared.Add(gem); else shared.Remove(gem);
        Publish();
        Changed?.Invoke();
    }

    public static void RemoveRelic(RelicGem gem)
    {
        if (!relics.Remove(gem)) return;
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
            foreach (var kv in relics) list.Add((kv.Key, kv.Value, cursed.Contains(kv.Key)));
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

    /// <summary>Every player carrying relics right now (copies included), with how many.</summary>
    public static List<(Photon.Realtime.Player player, int count)> RelicHolders()
    {
        var list = new List<(Photon.Realtime.Player, int)>();
        if (!PhotonNetwork.InRoom) return list;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            int n = RelicsOf(p).Count;
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

    private static int TierRelics() => relics.Keys.Count(g => !cursed.Contains(g) && !shared.Contains(g) && PeakRoguelike.Relics.Cursed(g));

    /// <summary>Another player's relics with no copy mark (":c" cursed, ":s" shared).</summary>
    private static int TierRelicsOf(Photon.Realtime.Player p) =>
        p.CustomProperties.TryGetValue(RelicsKey, out object raw) && raw is string s
            ? s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Count(part => part.Split(':') is { Length: 2 } bits && (!Enum.TryParse(bits[0], out RelicGem g) || PeakRoguelike.Relics.Cursed(g)))
            : 0;

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
                + (cursed.Contains(kv.Key) ? ":c" : shared.Contains(kv.Key) ? ":s" : "")));
            string cr = string.Join(";", crafts.Select(kv => $"{kv.Key}:{kv.Value}"));
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                [LevelsKey] = lv, [RelicsKey] = rel, [CraftsKey] = cr, [DevPanel.BuildKey] = DevPanel.Build,
            });
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Couldn't publish run state: {e.Message}");
        }
    }
}
