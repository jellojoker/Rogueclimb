using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

/// <summary>How a second copy of an item joins the first.</summary>
internal enum StackKind : byte
{
    None,   // never stacks (tools, fusions, packs, flares)
    Count,  // copies: when the one in hand is spent (eaten, placed, burnt out, emptied) the next takes its place
    Glider, // never spent: each copy makes it glide longer
}

/// <summary>
/// Stacking: the same item picked up again joins the pocket that already holds it, up to
/// <see cref="Limit"/> copies (a tree item only as many as its level). The box shows the count.
/// Using up the one in hand spends a copy instead of emptying the pocket; a fresh copy comes
/// with full uses / fuel. Dropping or throwing lets go of one copy; a stack dropped whole (death)
/// spills into a pile of single items. A whole stack is
/// one item with one level stamp, so it levels up together.
///
/// Counts live in room properties keyed by the item's instance guid (like level stamps), so
/// every scout sees them and they survive drops into backpacks.
/// </summary>
[HarmonyPatch] // without it PatchAll skips every hook below (stacking never ran before v0.11.8)
internal class Stacks : MonoBehaviourPunCallbacks
{
    private const string Prefix = "prl.st.";
    internal const int Limit = 3;
    internal const int MinCap = 2;

    private static readonly Dictionary<Guid, int> counts = new();
    private static readonly Dictionary<ushort, StackKind> kinds = new();
    private static HashSet<ushort>? fusionIds;
    private float nextSpill;

    internal static bool On => ModConfig.Stacking.Value;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Stacks");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Stacks>();
    }

    private static Stacks? instance;

    // --- what stacks ----------------------------------------------------------------------------

    internal static StackKind KindOf(Item? prefab)
    {
        if (prefab == null) return StackKind.None;
        if (kinds.TryGetValue(prefab.itemID, out var k)) return k;
        k = StackKind.None;
        // Airline Food is too good to carry three of: one per pocket.
        if (prefab is Backpack || prefab.GetComponent<Flare>() || Stats.IsAirlineFood(prefab)) k = StackKind.None;
        else if (prefab.GetComponent<Glider>()) k = StackKind.Glider;
        else if (prefab.GetComponent<ClimbingSpikeComponent>() || prefab.GetComponent<RopeSpool>() || prefab.GetComponent<Lantern>()
                 || prefab.GetComponent<Action_Consume>() || prefab.GetComponent<Action_ConsumeAndSpawn>()
                 || prefab.GetComponents<Action_ReduceUses>().Any(a => a.consumeOnFullyUsed))
            k = StackKind.Count;
        // Only what a crate can give stacks: wild food (berries, coconuts, mushrooms) never does.
        if (k != StackKind.None && ModConfig.StackCrateOnly.Value && !FromCrates(prefab)) k = StackKind.None;
        if (Items.Find(Trees.BaseRole(TreeId.Rope)) != null)
        {
            kinds[prefab.itemID] = k; // only once the item database is up
            if (!stackListLogged && Items.Database.Any())
            {
                stackListLogged = true;
                var all = Items.Database.Where(i => i != null && i != prefab).Select(i => (i, KindOf(i))).Append((prefab, k));
                Plugin.Log.LogInfo("Stackable: " + string.Join(", ", all.Where(x => x.Item2 != StackKind.None).Select(x => x.Item1.gameObject.name).OrderBy(n => n)));
            }
        }
        return k;
    }

    private static bool stackListLogged;

    private static readonly SpawnPool Luggage = ((SpawnPool[])Enum.GetValues(typeof(SpawnPool)))
        .Where(p => p.ToString().StartsWith("Luggage")).Aggregate((SpawnPool)0, (a, b) => a | b);

    /// <summary>Can come out of a crate or luggage: in a luggage spawn pool, a tree item, an earned item, sunscreen or antidote.</summary>
    private static bool FromCrates(Item prefab) =>
        (prefab.GetComponent<LootData>() is { } loot && (loot.spawnLocations & Luggage) != 0)
        || ItemLevels.KindOf(prefab) != null || Items.IsEarnedOnly(prefab) || Items.IsSunscreen(prefab) || Items.IsAntidote(prefab);

    // --- the green sun: nothing new joins a stack ------------------------------------------------

    /// <summary>
    /// The green sun is on and this player doesn't carry Generosity: nothing new stacks. Sunscreen and
    /// an open parasol with shade left spare it, as they do every sun. Safe ground doesn't (picking
    /// things up at a campfire would dodge it).
    /// </summary>
    internal static bool GreenBlocks(Player? p)
    {
        if (p == null || !ModConfig.Suns.Value || !ModConfig.SunGreenNoStack.Value || Suns.Current != RelicGem.Generosity) return false;
        var owner = p.photonView != null ? p.photonView.Owner : null;
        if (ModConfig.Relics.Value && owner != null && RunState.RelicsOf(owner).Any(r => r.gem == RelicGem.Generosity)) return false;
        Character? c = null;
        try { c = p.character; }
        catch { /* no character yet */ }
        return !(c != null && (c.data.wearingSunscreen || Suns.ParasolShade(c)));
    }

    private bool wasGreen;
    private float nextGreenCheck;

    /// <summary>The green sun let go (sunset, night, its relic taken): same items in your pockets merge back.</summary>
    private void GreenTick()
    {
        if (Time.time < nextGreenCheck) return;
        nextGreenCheck = Time.time + 0.5f;
        bool green = GreenBlocks(Player.localPlayer);
        if (wasGreen && !green) MergeBack();
        wasGreen = green;
    }

    private static void MergeBack()
    {
        var p = Player.localPlayer;
        var me = Character.localCharacter;
        if (!On || p == null || me == null || me.data.dead) return;
        try
        {
            byte? held = me.refs.items.currentSelectedSlot.IsSome ? me.refs.items.currentSelectedSlot.Value : null;
            int merged = 0;
            for (int i = 0; i < p.itemSlots.Length; i++)
            for (int j = i + 1; j < p.itemSlots.Length; j++)
            {
                ItemSlot a = p.itemSlots[i], b = p.itemSlots[j];
                if (a == null || b == null || a.IsEmpty() || b.IsEmpty() || a.prefab == null || b.prefab == null) continue;
                if (a.prefab.itemID != b.prefab.itemID || !Stackable(a.prefab, a.data) || !Stackable(b.prefab, b.data)) continue;
                if (ItemLevels.IsSecondPath(a.data) != ItemLevels.IsSecondPath(b.data)) continue;
                // Keep the one in hand; the other pocket empties.
                var (keep, gone) = held == b.itemSlotID ? (b, a) : (a, b);
                int sum = Count(keep.data) + Count(gone.data);
                if (sum > Cap(keep)) continue;
                SetCount(keep.data.guid, sum, $"{keep.prefab.gameObject.name} merged back after the green sun");
                int mine = ItemLevels.Stamp(keep.data), theirs = ItemLevels.Stamp(gone.data);
                if (theirs > mine && mine > 0) ItemLevels.StampAs(keep.data.guid, theirs, "merged stack took the better level");
                p.EmptySlot(Optionable<byte>.Some(gone.itemSlotID));
                merged++;
            }
            if (merged > 0) Plugin.Log.LogInfo($"Stack: green sun gone - merged {merged} pocket(s) back into stacks.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Stack merge-back failed: {e.Message}"); }
    }

    /// <summary>Fusions are one-of-a-kind prizes: they never stack (Sugar-Rush is a marked Energy Drink).</summary>
    private static bool IsFusion(Item prefab, ItemInstanceData? data)
    {
        if (fusionIds == null && Items.Find(Trees.BaseRole(TreeId.Rope)) != null)
            fusionIds = new HashSet<ushort>(Fusion.Recipes
                .Where(r => r.Result != null && r.Craft != CraftId.SugarRush)
                .Select(r => Items.Find(r.Result!)).Where(i => i != null).Select(i => i!.itemID));
        if (fusionIds != null && fusionIds.Contains(prefab.itemID)) return true;
        return SugarRush.IsMarked(data);
    }

    internal static bool Stackable(Item? prefab, ItemInstanceData? data) =>
        On && prefab != null && data != null && KindOf(prefab) != StackKind.None && !IsFusion(prefab, data);

    /// <summary>
    /// How many copies this pocket may hold: a tree item only as many as its level, but always at
    /// least <see cref="MinCap"/>; evolved items and everything else up to <see cref="Limit"/>.
    /// </summary>
    internal static int Cap(ItemSlot slot)
    {
        if (!ModConfig.StackByLevel.Value || !ModConfig.ItemTrees.Value) return Limit;
        int lv;
        if (ItemLevels.KindOf(slot.prefab) is { } k)
        {
            if (k.Min > 1) return Limit;
            lv = ItemLevels.LevelOf(slot);
        }
        else lv = ItemLevels.Shown(slot.prefab, slot.data); // snacks: each is its own level of the food tree
        return lv <= 0 ? Limit : Mathf.Clamp(lv, MinCap, Limit);
    }

    // --- counts -------------------------------------------------------------------------------

    internal static int Count(ItemInstanceData? data)
    {
        if (data == null) return 1;
        if (counts.TryGetValue(data.guid, out int n)) return n;
        n = 1;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(Prefix + data.guid.ToString("N"), out object raw) && raw is int v)
            n = Mathf.Max(1, v);
        counts[data.guid] = n;
        return n;
    }

    internal static void SetCount(Guid guid, int n, string why)
    {
        n = Mathf.Max(1, n);
        counts[guid] = n;
        if (PhotonNetwork.InRoom)
        {
            try { PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [Prefix + guid.ToString("N")] = n }); }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't share a stack count: {e.Message}"); }
        }
        Plugin.Log.LogInfo($"Stack: {why} -> x{n}.");
    }

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        foreach (var key in changed.Keys)
        {
            if (key is not string s || !s.StartsWith(Prefix) || !Guid.TryParseExact(s.Substring(Prefix.Length), "N", out var g)) continue;
            counts[g] = changed[key] is int v ? Mathf.Max(1, v) : 1;
        }
    }

    public override void OnJoinedRoom() => counts.Clear();
    public override void OnLeftRoom() => counts.Clear();

    /// <summary>Copies a spend takes: the green sun wears through two of a single-use item at once.</summary>
    private static int Take(Item item) =>
        Suns.Green && ModConfig.SunBlueWear.Value && item.totalUses <= 0 ? 2 : 1;

    /// <summary>
    /// The copy in hand (or in this pocket) is spent: if another is left, it takes the spent one's
    /// place (true). False = the last one, let the game remove it.
    /// </summary>
    private static bool Spend(Item prefab, ItemInstanceData? data, Item? live, string why)
    {
        if (!Stackable(prefab, data)) return false;
        int n = Count(data), left = n - (live != null ? Take(live) : 1);
        if (n <= 1 || left <= 0) return false;
        SetCount(data!.guid, left, $"{prefab.gameObject.name} {why}");
        return true;
    }

    /// <summary>Fusing takes one copy of a stacked ingredient (true) instead of the whole pocket.</summary>
    internal static bool TakeOne(Player p, byte slotId)
    {
        var s = p.GetItemSlot(slotId);
        if (s == null || s.IsEmpty() || s.prefab == null) return false;
        return Spend(s.prefab, s.data, null, "went into a fusion");
    }

    // --- joining a stack ------------------------------------------------------------------------

    /// <summary>A pocket this item could join, or null.</summary>
    private static ItemSlot? JoinSlot(Player p, ushort itemID, int adding, ItemInstanceData? incoming = null)
    {
        foreach (var s in p.itemSlots.Append(p.tempFullSlot))
        {
            if (s == null || s.IsEmpty() || s.prefab == null || s.prefab.itemID != itemID || !Stackable(s.prefab, s.data)) continue;
            // A holy pack and a dark medkit are the same prefab but never one stack.
            if (incoming != null && ItemLevels.IsSecondPath(incoming) != ItemLevels.IsSecondPath(s.data)) continue;
            if (Count(s.data) + adding <= Cap(s)) return s;
        }
        return null;
    }

    /// <summary>Host: a picked-up item joins the pocket already holding it instead of taking a new one.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.AddItem))]
    private static bool Join(Player __instance, ushort itemID, ItemInstanceData instanceData, ref ItemSlot slot, ref bool __result)
    {
        try
        {
            // A fresh item (crate pick, most ground items) has no instance data yet: it counts as one
            // copy. (Stackable() wants data, so it used to turn every fresh item away.)
            if (!On || !PhotonNetwork.IsMasterClient) return true;
            if (!ItemDatabase.TryGetItem(itemID, out var prefab) || prefab == null || KindOf(prefab) == StackKind.None
                || IsFusion(prefab, instanceData)) return true;
            if (GreenBlocks(__instance))
            {
                if (JoinSlot(__instance, itemID, 1, instanceData) != null)
                    Plugin.Log.LogInfo($"Stack: green sun - {prefab.gameObject.name} takes its own pocket.");
                return true;
            }
            int adding = Count(instanceData);
            var into = JoinSlot(__instance, itemID, adding, instanceData);
            if (into == null)
            {
                var same = __instance.itemSlots.Append(__instance.tempFullSlot)
                    .FirstOrDefault(s => s != null && !s.IsEmpty() && s.prefab != null && s.prefab.itemID == itemID);
                if (same != null)
                    Plugin.Log.LogInfo($"Stack: {prefab.gameObject.name} didn't join (pocket has x{Count(same.data)} of {Cap(same)}"
                        + $"{(Stackable(same.prefab, same.data) ? "" : ", that pocket can't stack")}, adding {adding}).");
                return true;
            }
            SetCount(into.data.guid, Count(into.data) + adding, $"{prefab.gameObject.name} joined a stack");
            // The stack keeps the better of the two levels.
            int mine = ItemLevels.Stamp(into.data), theirs = ItemLevels.Stamp(instanceData);
            if (theirs > mine && mine > 0) ItemLevels.StampAs(into.data.guid, theirs, $"{prefab.gameObject.name} stack took the picked-up level");
            slot = into;
            __result = true;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Stack join failed: {e.Message}");
            return true;
        }
    }

    /// <summary>Full pockets still let you pick up something you can stack.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Player), nameof(Player.HasEmptySlot))]
    private static void RoomInStack(Player __instance, ushort itemID, ref bool __result)
    {
        if (__result || !On || GreenBlocks(__instance) || !ItemDatabase.TryGetItem(itemID, out var prefab) || prefab is Backpack) return;
        try { __result = JoinSlot(__instance, itemID, 1) != null; }
        catch { /* leave it to the game */ }
    }

    // --- spending a copy ----------------------------------------------------------------------

    /// <summary>Eaten / drunk / built / emptied: with copies left, the item stays in hand as the next one.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Item), nameof(Item.ConsumeDelayed))]
    private static bool KeepStack(Item __instance, bool ignoreActions, ref IEnumerator __result)
    {
        try
        {
            var holder = __instance.holderCharacter;
            if (!On || holder == null || !holder.IsLocal || __instance.data == null || !Stackable(__instance, __instance.data)) return true;
            int n = Count(__instance.data), left = n - Take(__instance);
            if (n <= 1 || left <= 0) return true;
            __result = SpendInHand(__instance, ignoreActions, left);
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Stack spend failed: {e.Message}");
            return true;
        }
    }

    private static IEnumerator SpendInHand(Item item, bool ignoreActions, int left)
    {
        item.consuming = true;
        if (!ignoreActions) item.OnConsumed?.Invoke();
        yield return null;
        if (item == null || item.data == null) yield break;
        SetCount(item.data.guid, left, $"{item.gameObject.name} used");
        int consumer = item.holderCharacter != null ? item.holderCharacter.photonView.ViewID : -1;
        if (PhotonNetwork.InRoom) Net.StackSpent(item.photonView.ViewID, consumer);
        else Spent(item.photonView.ViewID, consumer);
    }

    /// <summary>Everyone: a copy was spent - the game's "consumed" moment without the item vanishing.</summary>
    internal static void Spent(int itemView, int consumerView)
    {
        var view = PhotonView.Find(itemView);
        var item = view != null ? view.GetComponent<Item>() : null;
        if (item == null) return;
        var cv = PhotonView.Find(consumerView);
        var consumer = cv != null ? cv.GetComponent<Character>() : null;
        if (consumer != null) GlobalEvents.TriggerItemConsumed(item, consumer);
        if (item.holderCharacter != null) item.holderCharacter.data.lastConsumedItem = Time.time;
        item.consuming = false;
        // The next copy is fresh: full uses.
        if (item.totalUses > 0)
        {
            var uses = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (uses.HasData) uses.Value = item.totalUses;
            item.SetUseRemainingPercentage(1f);
        }
    }

    private static ItemSlot? hammering;

    /// <summary>Pitons: hammering one in from a stack keeps the pocket.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterItems), "HammerClimbingSpike")]
    private static void HammerStart(CharacterItems __instance) =>
        hammering = On && __instance.character != null && __instance.character.IsLocal ? __instance.currentClimbingSpikeItemSlot : null;

    [HarmonyFinalizer, HarmonyPatch(typeof(CharacterItems), "HammerClimbingSpike")]
    private static void HammerEnd() => hammering = null;

    [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.EmptySlot))]
    private static bool KeepPiton(Player __instance, Optionable<byte> slot)
    {
        var h = hammering;
        if (h == null || slot.IsNone || slot.Value != h.itemSlotID || h.prefab == null) return true;
        hammering = null;
        try { return !Spend(h.prefab, h.data, h.prefab, "hammered in"); }
        catch { return true; }
    }

    private static byte? placing;
    private static bool placedOne;

    /// <summary>Placed things (Scout Cannon, flag, effigy): building one from a stack keeps the pocket.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Constructable), nameof(Constructable.FinishConstruction))]
    private static void PlaceStart(Constructable __instance)
    {
        placing = null;
        placedOne = false;
        var me = Character.localCharacter;
        if (!On || me == null || me.data.currentItem != __instance.item || me.refs.items.currentSelectedSlot.IsNone) return;
        placing = me.refs.items.currentSelectedSlot.Value;
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(Constructable), nameof(Constructable.FinishConstruction))]
    private static void PlaceEnd()
    {
        var slot = placing;
        placing = null;
        if (slot == null || !placedOne) return;
        placedOne = false;
        if (Character.localCharacter is { } me) instance?.StartCoroutine(Reequip(me, slot.Value));
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.EmptySlot))]
    private static bool KeepPlaced(Player __instance, Optionable<byte> slot)
    {
        if (placing is not { } p || slot.IsNone || slot.Value != p || __instance != Player.localPlayer) return true;
        var s = __instance.GetItemSlot(p);
        if (s == null || s.IsEmpty() || s.prefab == null) return true;
        try { placedOne = Spend(s.prefab, s.data, null, "placed"); }
        catch { placedOne = false; }
        return !placedOne;
    }

    /// <summary>Rope spools: running out of rope with spools left starts the next one.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(RopeSpool), nameof(RopeSpool.RopeFuel), MethodType.Setter)]
    private static void NextSpool(RopeSpool __instance, ref float value)
    {
        try
        {
            if (value > 2f || !On || !__instance.photonView.IsMine) return;
            var item = __instance.item;
            if (item == null || item.holderCharacter == null || !item.holderCharacter.IsLocal) return;
            if (Spend(item, item.data, item, "spool ran out")) value += __instance.ropeStartFuel;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Stack spool failed: {e.Message}"); }
    }

    /// <summary>Lanterns: burning out with lanterns left lights the next one.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Lantern), "UpdateFuel")]
    private static void NextLantern(Lantern __instance)
    {
        try
        {
            if (!On || !__instance.lit || !__instance.photonView.IsMine || __instance.fuel - Time.deltaTime > 0f) return;
            var item = __instance.item;
            if (item != null && Spend(item, item.data, item, "burnt out")) __instance.fuel += __instance.startingFuel;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Stack lantern failed: {e.Message}"); }
    }

    // --- gliders: every copy is more glide time -------------------------------------------------

    private static int gliding = 1;

    [HarmonyPrefix, HarmonyPatch(typeof(Glider), "FixedUpdate")]
    private static void GlideStart(Glider __instance)
    {
        var item = __instance.item;
        gliding = On && item != null && item.holderCharacter != null && item.holderCharacter.IsLocal && Stackable(item, item.data)
            ? Count(item.data) : 1;
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(Glider), "FixedUpdate")]
    private static void GlideEnd() => gliding = 1;

    [HarmonyPrefix, HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    private static void GlideLonger(ref float usage)
    {
        if (gliding > 1 && usage > 0f) usage /= gliding;
    }

    // --- dropping or throwing one copy -----------------------------------------------------------

    /// <summary>The pocket a drop is splitting off one copy from (it stays in the pocket).</summary>
    private static (Player player, byte slot)? splitting;

    /// <summary>
    /// Everyone: a dropped or thrown stack lets go of ONE copy; the rest stay in hand. The host
    /// spawns that copy as a fresh single item at the stack's level. Dying and the game's other
    /// drop-everything moments (cacheToDroppedItems) still drop the whole stack, which spills.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.DropItemRpc))]
    private static void DropOne(CharacterItems __instance, byte slotID, ref ItemInstanceData itemInstanceData, bool cacheToDroppedItems)
    {
        splitting = null;
        try
        {
            if (!On || cacheToDroppedItems || slotID == 250 || itemInstanceData == null) return;
            var ch = __instance.character;
            var player = ch != null ? ch.player : null;
            if (player == null || ch!.data.currentItem == null) return;
            var slot = player.GetItemSlot(slotID);
            if (slot == null || slot.IsEmpty() || slot.prefab == null || slot.data == null || slot.data.guid != itemInstanceData.guid) return;
            if (!Stackable(slot.prefab, slot.data)) return;
            int n = Count(slot.data);
            if (n <= 1) return;
            splitting = (player, slotID);
            var stack = itemInstanceData;
            if (PhotonNetwork.IsMasterClient)
            {
                var single = new ItemInstanceData(Guid.NewGuid());
                ItemInstanceDataHandler.AddInstanceData(single);
                int lv = ItemLevels.Stamp(stack);
                if (lv > 0) ItemLevels.StampPath(single.guid, lv, ItemLevels.IsSecondPath(stack) ? 2 : 1, $"{slot.prefab.gameObject.name} dropped from a stack");
                itemInstanceData = single;
            }
            if (ch.IsLocal)
            {
                SetCount(stack.guid, n - 1, $"{slot.prefab.gameObject.name} dropped one");
                instance?.StartCoroutine(Reequip(ch, slotID));
            }
        }
        catch (Exception e)
        {
            splitting = null;
            Plugin.Log.LogWarning($"Stack drop-one failed: {e.Message}");
        }
    }

    [HarmonyFinalizer, HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.DropItemRpc))]
    private static void DropOneEnd() => splitting = null;

    /// <summary>The pocket being split keeps the rest of its stack.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.EmptySlot))]
    private static bool KeepRest(Player __instance, Optionable<byte> slot) =>
        !(splitting is { } s && s.player == __instance && slot.IsSome && slot.Value == s.slot);

    /// <summary>The game un-equips right after a drop: a frame later the rest of the stack goes back in hand.</summary>
    private static IEnumerator Reequip(Character ch, byte slotID)
    {
        yield return null;
        if (ch == null || ch.data.dead || ch.data.currentItem != null || ch.player == null) yield break;
        var slot = ch.player.GetItemSlot(slotID);
        if (slot == null || slot.IsEmpty()) yield break;
        ch.refs.items.EquipSlot(Optionable<byte>.Some(slotID));
    }

    // --- dropped stacks spill -----------------------------------------------------------------

    /// <summary>Host: a stack on the ground (dropped, thrown, left by the dead) becomes a pile of single items.</summary>
    private void Update()
    {
        try { GreenTick(); } catch (Exception e) { Plugin.Log.LogWarning($"Stack green check failed: {e.Message}"); }
        if (!On || !PhotonNetwork.IsMasterClient || Time.time < nextSpill) return;
        nextSpill = Time.time + 0.5f;
        try
        {
            foreach (var item in Item.ALL_ACTIVE_ITEMS.ToList())
            {
                if (item == null || item.itemState != ItemState.Ground || item.data == null) continue;
                int n = Count(item.data);
                if (n <= 1) continue;
                SetCount(item.data.guid, 1, $"{item.gameObject.name} stack spilled on the ground");
                int lv = ItemLevels.Stamp(item.data);
                int path = ItemLevels.IsSecondPath(item.data) ? 2 : 1;
                string name = item.gameObject.name.Replace("(Clone)", "").Trim();
                for (int i = 1; i < n; i++)
                {
                    Vector3 pos = item.transform.position + Vector3.up * (0.25f * i) + UnityEngine.Random.insideUnitSphere * 0.3f;
                    var view = PhotonNetwork.InstantiateItemRoom(name, pos, UnityEngine.Random.rotation)?.GetComponent<PhotonView>();
                    if (view == null) continue;
                    var data = new ItemInstanceData(Guid.NewGuid());
                    ItemInstanceDataHandler.AddInstanceData(data);
                    view.RPC("SetItemInstanceDataRPC", RpcTarget.All, data);
                    if (lv > 0) ItemLevels.StampPath(data.guid, lv, path, $"{name} spilled from a stack");
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Stack spill failed: {e.Message}"); }
    }

    // --- the count on the box -----------------------------------------------------------------

    private const string BadgeName = "PEAK Roguelike - Stack";

    private void LateUpdate()
    {
        var gui = GUIManager.instance;
        if (gui == null || gui.items == null) return;
        try
        {
            foreach (var box in gui.items) Badge(gui, box);
            if (gui.temporaryItem != null) Badge(gui, gui.temporaryItem);
        }
        catch (Exception e)
        {
            if (!badgeWarned) Plugin.Log.LogWarning($"Stack badge failed: {e.Message}");
            badgeWarned = true;
        }
    }

    private static bool badgeWarned, badgeLogged;

    private static void Badge(GUIManager gui, InventoryItemUI? box)
    {
        if (box == null) return;
        int n = On && box._itemPrefab != null && box._itemData != null && Stackable(box._itemPrefab, box._itemData) ? Count(box._itemData) : 1;
        var t = box.transform.Find(BadgeName);
        if (t == null)
        {
            if (n <= 1) return;
            t = MakeBadge(gui, box);
            if (t == null) return;
        }
        bool show = n > 1;
        if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
        if (show && t.GetComponentInChildren<TMPro.TMP_Text>(true) is { } label && label.text != n.ToString()) label.text = n.ToString();
    }

    /// <summary>The game's own number badge (the one the backpack slot uses), copied onto the box.</summary>
    private static Transform? MakeBadge(GUIManager gui, InventoryItemUI box)
    {
        GameObject go;
        var proto = gui.backpack != null ? gui.backpack.backpackFilledSlotsObject : null;
        if (proto != null)
        {
            go = Instantiate(proto, box.transform, false);
            var prt = (RectTransform)proto.transform;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = prt.anchorMin;
            rt.anchorMax = prt.anchorMax;
            rt.pivot = prt.pivot;
            rt.anchoredPosition = prt.anchoredPosition;
            rt.sizeDelta = prt.sizeDelta;
            if (!badgeLogged) Plugin.Log.LogInfo($"Stack badge: copied the backpack's '{proto.name}'.");
        }
        else
        {
            go = new GameObject(BadgeName, typeof(RectTransform));
            go.transform.SetParent(box.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-6f, 4f);
            rt.sizeDelta = new Vector2(40f, 30f);
            var txt = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (box.nameText != null) txt.font = box.nameText.font;
            txt.fontSize = 24;
            txt.alignment = TMPro.TextAlignmentOptions.BottomRight;
            txt.raycastTarget = false;
            if (!badgeLogged) Plugin.Log.LogInfo("Stack badge: no backpack badge to copy, using plain text.");
        }
        badgeLogged = true;
        go.name = BadgeName;
        go.transform.SetAsLastSibling();
        return go.transform;
    }
}

/// <summary>
/// The game only refreshes an item box when the item KIND changes - so a stack of pitons traded
/// up into one better piton kept the old stack's data (its count badge, its level in the name).
/// </summary>
[HarmonyPatch]
internal static class FreshBoxData
{
    [HarmonyPostfix, HarmonyPatch(typeof(InventoryItemUI), nameof(InventoryItemUI.SetItem))]
    private static void Refresh(InventoryItemUI __instance, ItemSlot slot)
    {
        try
        {
            if (__instance.isBackpack || slot == null || slot.IsEmpty() || __instance._itemData == slot.data) return;
            __instance._itemData = slot.data;
            __instance.UpdateNameText();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Item box refresh failed: {e.Message}"); }
    }
}
