using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

/// <summary>
/// Items carry their own level. A player's tree level is only their "discovery level" (what
/// crates offer next); the item's stamp decides how it works, whoever holds it.
///
/// Stamps live in room properties keyed by the item's instance guid, so any scout can write
/// them and late joiners get them too. An item without a stamp takes its first holder's
/// discovery level. Levelling a tree up raises the lower items of that tree in your pockets
/// at that moment (never lowers them); each item stays within its own kind's levels.
/// </summary>
internal class ItemLevels : MonoBehaviourPunCallbacks
{
    private const string Prefix = "prl.il.";

    /// <summary>The levels of a tree one item covers (Rope Spool 1-3, Rope Cannon 4-6 on path 1, ...).</summary>
    internal sealed class Kind
    {
        public TreeId Tree;
        public int Min, Max, Branch;
    }

    private static readonly Dictionary<ushort, Kind?> kinds = new();
    /// <summary>Known stamps; 0 = looked it up, nobody has stamped it.</summary>
    private static readonly Dictionary<Guid, int> stamps = new();
    private static readonly Dictionary<TreeId, int> lastLevels = new();
    private float nextScan;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Item Levels");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<ItemLevels>();
        RunState.Changed += RaisePockets;
    }

    // --- kinds ----------------------------------------------------------------------------

    internal static Kind? KindOf(Item? item)
    {
        if (item == null) return null;
        if (kinds.TryGetValue(item.itemID, out var cached)) return cached;
        Kind? kind = null;
        var id = Trees.Classify(item);
        var tree = id is TreeId.None or TreeId.Food ? null : Trees.Get(id);
        if (tree != null)
        {
            for (int i = 0; i < tree.Levels.Length; i++)
                foreach (var def in tree.Levels[i])
                {
                    if (def.Grants == null || Items.Find(def.Grants) is not { } g || g.itemID != item.itemID) continue;
                    if (kind == null) kind = new Kind { Tree = id, Min = i + 1, Max = i + 1, Branch = def.Branch };
                    else kind.Max = i + 1;
                }
            // In a tree but never handed out by a level (a variant): treat it as the tree's first item.
            kind ??= new Kind { Tree = id, Min = 1, Max = Mathf.Min(3, tree.Max), Branch = 0 };
        }
        if (Items.Find(Trees.BaseRole(TreeId.Rope)) != null) kinds[item.itemID] = kind; // only once the item database is up
        return kind;
    }

    /// <summary>The level an item of this kind gets from a scout at this discovery level.</summary>
    internal static int Clamp(Kind k, int level, int branch) =>
        k.Branch != 0 && branch != k.Branch ? k.Min : Mathf.Clamp(Mathf.Max(1, level), k.Min, k.Max);

    private static int Discovered(Kind k) => Clamp(k, RunState.Level(k.Tree), RunState.Branch(k.Tree));

    // --- stamps ---------------------------------------------------------------------------

    internal static int Stamp(ItemInstanceData? data)
    {
        if (data == null) return 0;
        if (stamps.TryGetValue(data.guid, out int lv)) return lv;
        lv = 0;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(Prefix + data.guid.ToString("N"), out object raw) && raw is int v)
            lv = v;
        stamps[data.guid] = lv;
        return lv;
    }

    private static void SetStamp(Guid guid, int level, string why)
    {
        stamps[guid] = level;
        if (PhotonNetwork.InRoom)
        {
            try { PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [Prefix + guid.ToString("N")] = level }); }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't share an item level: {e.Message}"); }
        }
        Plugin.Log.LogInfo($"Item level: {why} -> level {level}.");
    }

    /// <summary>An item's own (level, branch), or null if nobody has stamped it yet.</summary>
    internal static (int level, int branch)? Of(Item? item)
    {
        if (item == null || item.data == null || KindOf(item) is not { } k) return null;
        int lv = Stamp(item.data);
        return lv > 0 ? (lv, k.Branch) : null;
    }

    /// <summary>A pocket item's level: its stamp, else what our discovery level would stamp it.</summary>
    internal static int LevelOf(ItemSlot slot)
    {
        if (slot?.prefab == null || KindOf(slot.prefab) is not { } k) return 0;
        int lv = Stamp(slot.data);
        return lv > 0 ? lv : Discovered(k);
    }

    /// <summary>Is this pocket item at the top level of its kind (a fusion ingredient)?</summary>
    internal static bool IsMaxed(ItemSlot slot) =>
        slot?.prefab != null && KindOf(slot.prefab) is { } k && LevelOf(slot) >= k.Max;

    // --- pockets ------------------------------------------------------------------------------

    private static IEnumerable<ItemSlot> Pockets()
    {
        var p = Player.localPlayer;
        if (p == null) yield break;
        foreach (var s in p.itemSlots)
            if (s != null && !s.IsEmpty() && s.prefab != null && s.data != null) yield return s;
        if (p.tempFullSlot is { } t && !t.IsEmpty() && t.prefab != null && t.data != null) yield return t;
    }

    /// <summary>New items in our pockets (crate picks, world finds) take our discovery level.</summary>
    private void Update()
    {
        if (Time.time < nextScan) return;
        nextScan = Time.time + 0.25f;
        if (!ModConfig.ItemTrees.Value) return;
        foreach (var s in Pockets())
        {
            if (KindOf(s.prefab) is not { } k || Stamp(s.data) > 0) continue;
            SetStamp(s.data.guid, Discovered(k), $"{s.prefab.gameObject.name} picked up (unstamped)");
        }
    }

    /// <summary>A tree went up: the lower items of it in our pockets right now go up with it.</summary>
    private static void RaisePockets()
    {
        var up = new List<TreeId>();
        foreach (var tree in Trees.All.Keys)
        {
            int now = RunState.Level(tree);
            int before = lastLevels.TryGetValue(tree, out int b) ? b : 0;
            lastLevels[tree] = now;
            if (now > before) up.Add(tree);
        }
        if (up.Count == 0 || !ModConfig.ItemTrees.Value) return;
        foreach (var s in Pockets().ToList())
        {
            if (KindOf(s.prefab) is not { } k || !up.Contains(k.Tree)) continue;
            int target = Discovered(k), cur = Stamp(s.data);
            if (cur > 0 && target > cur) SetStamp(s.data.guid, target, $"{s.prefab.gameObject.name} levelled up with you ({cur})");
        }
    }

    // --- room sync ---------------------------------------------------------------------------

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        foreach (var key in changed.Keys)
        {
            if (key is not string s || !s.StartsWith(Prefix) || !Guid.TryParseExact(s.Substring(Prefix.Length), "N", out var g)) continue;
            stamps[g] = changed[key] is int v ? v : 0;
        }
    }

    public override void OnJoinedRoom() => stamps.Clear();
    public override void OnLeftRoom() => stamps.Clear();

    /// <summary>New run: forget every stamp (the host also clears them from the room).</summary>
    internal static void Reset()
    {
        stamps.Clear();
        lastLevels.Clear();
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        var clear = new Hashtable();
        foreach (var key in PhotonNetwork.CurrentRoom.CustomProperties.Keys)
            if (key is string s && s.StartsWith(Prefix)) clear[s] = null;
        if (clear.Count > 0)
        {
            try { PhotonNetwork.CurrentRoom.SetCustomProperties(clear); }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't clear item levels: {e.Message}"); }
        }
    }
}
