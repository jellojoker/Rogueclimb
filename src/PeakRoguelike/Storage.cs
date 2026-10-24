using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.Core;
using Zorro.Core.Serizalization;

namespace PeakRoguelike;

/// <summary>
/// Packs become pockets. Picking up a backpack or fanny pack offers a card: wear it and the pack
/// disappears, giving +2 / +1 pockets for the rest of the run (up to MaxPockets). Dying loses
/// them all: the packs drop where you fell (anyone can pick them up), and so does whatever was
/// in the extra pockets.
///
/// How it fits the game: the inventory is host-authoritative. Each scout publishes their pocket
/// count (player property prl.pk); the host resizes that scout's Player.itemSlots and syncs it,
/// and every client adopts the size from the sync. Slot ids 0-2 are the vanilla pockets, 3 is the
/// back slot (hard-coded all over the game), so extra pockets use ids 4, 5, ... (index + 1).
/// </summary>
internal class Storage : MonoBehaviour
{
    private const string PocketsKey = "prl.pk";
    private const int Vanilla = 3;

    private static Storage? instance;
    private static int backpacks, fannypacks;
    private static readonly HashSet<Guid> declined = new();
    private float nextScan;
    private bool wasDead;
    private int published = -1;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Storage");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Storage>();
    }

    /// <summary>New run: back to three pockets.</summary>
    internal static void Reset()
    {
        backpacks = fannypacks = 0;
        declined.Clear();
        if (instance != null) instance.published = -1;
    }

    /// <summary>11: pockets 1-3 plus one per key 5-9, 0, -, = (4 is the back slot).</summary>
    internal static int Max => Mathf.Clamp(ModConfig.MaxPockets.Value, Vanilla, 11);

    /// <summary>The local scout's pocket count.</summary>
    internal static int Pockets => !ModConfig.Storage.Value ? Vanilla
        : Mathf.Clamp(Vanilla + backpacks * ModConfig.BackpackPockets.Value + fannypacks * ModConfig.FannypackPockets.Value, Vanilla, Max);

    private static int PocketsOf(Photon.Realtime.Player? owner)
    {
        if (!ModConfig.Storage.Value || owner == null) return Vanilla;
        if (owner.CustomProperties.TryGetValue(PocketsKey, out object raw) && raw is int n) return Mathf.Clamp(n, Vanilla, Max);
        return Vanilla;
    }

    internal static byte IdOf(int index) => (byte)(index < Vanilla ? index : index + 1);

    // --- every half second --------------------------------------------------------------------

    private void Update()
    {
        if (Time.time < nextScan) return;
        nextScan = Time.time + 0.5f;
        try
        {
            Publish();
            if (PhotonNetwork.IsMasterClient) HostResize();
            LocalScan();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Storage: {e.Message}");
        }
    }

    private void Publish()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
        int n = Pockets;
        if (n == published) return;
        published = n;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [PocketsKey] = n });
    }

    /// <summary>Host: give every scout the pockets they've earned (and take them away).</summary>
    private static void HostResize()
    {
        foreach (var p in PlayerHandler.GetAllPlayers())
        {
            if (p == null || p.photonView == null || p.itemSlots == null) continue;
            int want = PocketsOf(p.photonView.Owner);
            if (p.itemSlots.Length == want) continue;
            Resize(p, want, host: true);
            var data = IBinarySerializable.ToManagedArray(new InventorySyncData(p.itemSlots, p.backpackSlot, p.tempFullSlot));
            p.photonView.RPC("SyncInventoryRPC", RpcTarget.Others, data, false);
            Plugin.Log.LogInfo($"Storage: {p.photonView.Owner?.NickName} now has {want} pockets.");
        }
    }

    internal static void Resize(Player p, int n, bool host)
    {
        var old = p.itemSlots;
        if (old.Length == n) return;
        var slots = new ItemSlot[n];
        for (int i = 0; i < n; i++) slots[i] = i < old.Length ? old[i] : new ItemSlot(IdOf(i), p);

        // Host: anything still in a pocket that's going away lands at the scout's feet.
        Character? c = null;
        try { c = p.character; } catch { }
        if (host)
        {
            Vector3 pos = (c != null ? c.Center : p.transform.position) + Vector3.up;
            for (int i = n; i < old.Length; i++)
            {
                if (old[i] == null || old[i].IsEmpty()) continue;
                var view = PhotonNetwork.InstantiateItemRoom(old[i].GetPrefabName(), pos, Quaternion.identity).GetComponent<PhotonView>();
                view.RPC("SetItemInstanceDataRPC", RpcTarget.All, old[i].data);
                view.RPC("SetKinematicRPC", RpcTarget.All, false, view.transform.position, view.transform.rotation);
                pos += Vector3.up * 0.5f;
            }
        }
        p.itemSlots = slots;

        if (c != null)
        {
            var items = c.refs.items;
            if (c.IsLocal && items.currentSelectedSlot.IsSome)
            {
                byte sel = items.currentSelectedSlot.Value;
                if (sel != 3 && sel != 250 && p.GetItemSlot(sel) == null) items.EquipSlot(Optionable<byte>.None);
            }
            try { c.refs.afflictions.UpdateWeight(); } catch { }
        }
        p.itemsChangedAction?.Invoke(slots);
        if (GUIManager.instance != null && Character.observedCharacter != null && Character.observedCharacter.player == p)
            GUIManager.instance.UpdateItems();
    }

    // --- local scout: pack pickups and death ----------------------------------------------------

    private void LocalScan()
    {
        Character me = Character.localCharacter;
        Player p = Player.localPlayer;
        if (me == null || p == null) { wasDead = false; return; }

        if (me.data.dead && !wasDead) LoseAll(me);
        wasDead = me.data.dead;
        if (!ModConfig.Storage.Value || ChoiceScreen.IsOpen || me.data.dead || me.data.passedOut) return;

        var slot = p.backpackSlot;
        if (slot == null || slot.IsEmpty() || slot.prefab == null) return;
        var guid = slot.data?.guid ?? Guid.Empty;
        bool fanny = slot.backpackType == BackpackSlot.BackpackType.Fannypack;
        int add = PocketsFor(slot.backpackType);
        if (add <= 0) return; // jetpack, rocketpack: worn as normal

        // Chosen on the ground a moment ago: strap it straight on.
        if (Time.time < approvedUntil && slot.backpackType == approvedType)
        {
            approvedUntil = 0f;
            Absorb(p, slot, guid, fanny);
            return;
        }
        // Worn some other way (e.g. handed over): ask now.
        if (declined.Contains(guid) || Pockets >= Max) return;
        ShowCard(slot.prefab, fanny, add,
            stillValid: () => !slot.IsEmpty() && (slot.data?.guid ?? Guid.Empty) == guid,
            onConfirm: () => Absorb(p, slot, guid, fanny),
            onBack: () => declined.Add(guid));
    }

    private static float approvedUntil;
    private static BackpackSlot.BackpackType approvedType;

    private static int PocketsFor(BackpackSlot.BackpackType t) => t switch
    {
        BackpackSlot.BackpackType.Backpack => ModConfig.BackpackPockets.Value,
        BackpackSlot.BackpackType.Fannypack => ModConfig.FannypackPockets.Value,
        _ => 0,
    };

    /// <summary>
    /// Pressing E on a pack lying on the ground: the card comes up straight away (instead of the
    /// pack's wheel). Strap On picks it up and absorbs it; Back opens the wheel as normal and that
    /// pack stays a normal pack from then on. False = let the game handle it.
    /// </summary>
    internal static bool OfferFromGround(Backpack pack, Character who)
    {
        if (!ModConfig.Storage.Value || who == null || !who.IsLocal || ChoiceScreen.IsOpen || pack == null) return false;
        int add = PocketsFor(pack.backpackType);
        var p = who.player;
        if (add <= 0 || p == null || p.backpackSlot == null || !p.backpackSlot.IsEmpty()) return false;
        bool fanny = pack.backpackType == BackpackSlot.BackpackType.Fannypack;
        // No room left: the card shows, but there's nothing to claim - the pack stays where it lies.
        if (Pockets >= Max)
        {
            ShowCard(pack, fanny, add,
                stillValid: () => pack != null && pack.itemState == ItemState.Ground,
                onConfirm: () => { },
                onBack: () => { },
                full: true);
            return true;
        }
        var guid = pack.data?.guid ?? Guid.Empty;
        if (guid != Guid.Empty && declined.Contains(guid)) return false;
        ShowCard(pack, fanny, add,
            stillValid: () => pack != null && pack.itemState == ItemState.Ground,
            onConfirm: () =>
            {
                approvedType = pack.backpackType;
                approvedUntil = Time.time + 6f;
                pack.Wear(who); // the game's own pickup; the scan absorbs it when it lands on our back
            },
            onBack: () =>
            {
                if (guid != Guid.Empty) declined.Add(guid);
                if (pack != null && GUIManager.instance != null)
                    GUIManager.instance.OpenBackpackWheel(BackpackReference.GetFromBackpackItem(pack), pack.slotCount, pack.backpackType);
            });
        return true;
    }

    private static void ShowCard(Item prefab, bool fanny, int add, Func<bool> stillValid, Action onConfirm, Action onBack, bool full = false)
    {
        string name = fanny ? "Fanny Pack" : "Backpack";
        int from = Pockets, to = Mathf.Min(Max, from + add);
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = fanny ? "FANNY PACK FOUND" : "BACKPACK FOUND",
            Subtitle = "More pockets",
            Confirm = full ? "POCKETS FULL" : "STRAP ON",
            BackHint = full ? "Leave it" : "Keep it as a pack",
            Locked = full,
            Cards = new List<CardSpec>
            {
                new()
                {
                    Style = CardStyle.Perk,
                    Theme = PerkTheme.Mountaineer,
                    Title = name,
                    Tag = $"+{to - from} Pocket{(to - from == 1 ? "" : "s")}",
                    TagColor = ChoiceScreen.ThemeGlow(PerkTheme.Mountaineer),
                    Body = $"The {name.ToLowerInvariant()} becomes part of your gear: {to - from} more pocket{(to - from == 1 ? "" : "s")} for the rest of the run.\nDie and you lose every pack - they drop where you fall.",
                    Footer = $"Pockets {from} → {to}",
                    Icon = Items.Icon(prefab),
                },
            },
            StillValid = stillValid,
            OnConfirm = _ => onConfirm(),
            OnBack = onBack,
            OnCancelled = () => { },
        });
    }

    private static void Absorb(Player p, BackpackSlot slot, Guid guid, bool fanny)
    {
        Character me = Character.localCharacter;
        if (me == null || slot.IsEmpty() || (slot.data?.guid ?? Guid.Empty) != guid) return;

        // Packs are meant to be empty; if someone stashed things in it, they spill out.
        Vector3 pos = me.Center + me.data.lookDirection_Flat * 0.8f + Vector3.up * 0.5f;
        if (slot.data != null && slot.data.TryGetDataEntry<BackpackData>(DataEntryKey.BackpackData, out var contents))
            foreach (var s in contents.itemSlots)
            {
                if (s == null || s.IsEmpty()) continue;
                Net.SpawnItem(s.GetPrefabName(), pos);
                pos += Vector3.up * 0.4f;
            }

        var items = me.refs.items;
        if (items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == 3) items.EquipSlot(Optionable<byte>.None);
        items.DeleteMyBackpack();

        if (fanny) fannypacks++;
        else backpacks++;
        instance?.Publish();
        Plugin.Log.LogInfo($"Storage: absorbed a {(fanny ? "fanny pack" : "backpack")} - {Pockets} pockets.");
    }

    /// <summary>Died: every absorbed pack drops where you fell, and you're back to three pockets.</summary>
    private static void LoseAll(Character me)
    {
        if (backpacks + fannypacks == 0) return;
        Vector3 pos = me.Center + Vector3.up * 1.2f;
        for (int i = 0; i < backpacks; i++) { Net.SpawnItem("Backpack", pos + UnityEngine.Random.insideUnitSphere * 0.5f); pos += Vector3.up * 0.5f; }
        for (int i = 0; i < fannypacks; i++) { Net.SpawnItem("Fannypack", pos + UnityEngine.Random.insideUnitSphere * 0.5f); pos += Vector3.up * 0.5f; }
        Plugin.Log.LogInfo($"Storage: died - dropped {backpacks} backpack(s) and {fannypacks} fanny pack(s).");
        backpacks = fannypacks = 0;
        instance?.Publish();
    }

    // --- HUD: extra pocket boxes ------------------------------------------------------------------

    private GUIManager? hudFor;
    private InventoryItemUI[] vanillaBoxes = new InventoryItemUI[0];
    private readonly List<RectTransform> wrappers = new();
    private readonly List<InventoryItemUI> extraBoxes = new();

    private void LateUpdate()
    {
        var gui = GUIManager.instance;
        if (gui == null || gui.items == null || gui.items.Length < Vanilla) return;
        try { SyncHud(gui); FollowRow(gui); }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Storage HUD: {e.Message}");
            hudFor = null;
        }
    }

    private GUIManager? tempFor;
    private Vector3 tempRest, firstRest;
    private bool tempKnown;

    /// <summary>
    /// The "pockets full" slot (the item riding in your hand) sits where the game puts it for its
    /// 3-pocket row. Extra pockets widen the row and shift it left, so the slot shifts with it:
    /// it keeps the same place relative to pocket 1 as in vanilla.
    /// </summary>
    private void FollowRow(GUIManager gui)
    {
        var temp = gui.temporaryItem;
        if (temp == null || vanillaBoxes.Length == 0 || vanillaBoxes[0] == null) return;
        var trt = temp.rectTransform;
        var parent = trt.parent;
        if (parent == null) return;
        if (tempFor != gui) { tempFor = gui; tempKnown = false; }
        Vector3 first = parent.InverseTransformPoint(vanillaBoxes[0].transform.position);
        if (!tempKnown)
        {
            // Learn the vanilla spot while the row is still vanilla.
            if (extraBoxes.Count != 0) return;
            tempRest = trt.localPosition;
            firstRest = first;
            tempKnown = true;
            Plugin.Log.LogInfo($"Storage HUD: hand slot rests at {tempRest}, pocket 1 at {firstRest}.");
            return;
        }
        trt.localPosition = tempRest + new Vector3(first.x - firstRest.x, 0f, 0f);
    }

    private readonly List<TMPro.TMP_Text?> keyLabels = new();

    /// <summary>
    /// The extra boxes join the game's own row ('Layout') just before the back slot, so the game
    /// lays them out: 1 2 3 [extra pockets] 4. (v0.8.1 positioned them by hand and fought the
    /// row's layout - the "accordion" whenever a box grew on selection.)
    /// </summary>
    private void SyncHud(GUIManager gui)
    {
        if (hudFor != gui || vanillaBoxes.Length < Vanilla || vanillaBoxes[0] == null)
        {
            hudFor = gui;
            vanillaBoxes = gui.items.Take(Vanilla).ToArray();
            wrappers.Clear();
            extraBoxes.Clear();
            keyLabels.Clear();
        }
        var who = Character.observedCharacter;
        int pockets = who != null && who.player != null && who.player.itemSlots != null ? who.player.itemSlots.Length : Vanilla;
        int want = Mathf.Max(0, pockets - Vanilla);
        if (extraBoxes.Count == want) return;

        var proto = vanillaBoxes[0];
        var row = (RectTransform)proto.transform.parent;
        var back = gui.backpack != null && gui.backpack.transform.parent == row ? gui.backpack.transform : null;
        if (extraBoxes.Count == 0)
            Plugin.Log.LogInfo($"Storage HUD: row '{row.name}' laid out by {row.GetComponent<UnityEngine.UI.LayoutGroup>()?.GetType().Name ?? "nothing (!)"}, back slot {(back != null ? "in the row" : "elsewhere")}.");
        // The selected box is drawn 1.2x bigger; its resting size is what Start() recorded.
        Vector2 size = proto.startingSizeDelta.sqrMagnitude > 1f ? proto.startingSizeDelta : proto.rectTransform.sizeDelta;
        while (extraBoxes.Count < want)
        {
            int index = Vanilla + extraBoxes.Count;
            byte id = IdOf(index);
            // The game highlights a box when its sibling index equals the selected slot id, so each
            // extra box sits in its own holder behind `id` empty placeholders.
            var holder = new GameObject($"PEAK Roguelike - Pocket {index + 1}", typeof(RectTransform));
            var hrt = (RectTransform)holder.transform;
            hrt.SetParent(row, false);
            hrt.sizeDelta = size;
            var le = holder.AddComponent<UnityEngine.UI.LayoutElement>();
            le.minWidth = le.preferredWidth = size.x;
            le.minHeight = le.preferredHeight = size.y;
            hrt.SetSiblingIndex(back != null ? back.GetSiblingIndex() : vanillaBoxes[Vanilla - 1].transform.GetSiblingIndex() + 1 + extraBoxes.Count);
            for (int k = 0; k < id; k++)
            {
                var pad = new GameObject("pad", typeof(RectTransform));
                pad.transform.SetParent(hrt, false);
                pad.SetActive(false);
            }
            var box = Instantiate(proto.gameObject, hrt, false).GetComponent<InventoryItemUI>();
            box.name = $"Pocket {index + 1}";
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = size;
            box.Clear();
            wrappers.Add(hrt);
            extraBoxes.Add(box);
            var label = FindKeyLabel(box);
            keyLabels.Add(label);
            if (label != null && index - Vanilla < PocketKeys.Length) SetKey(label, PocketKeys[index - Vanilla]);
        }
        while (extraBoxes.Count > want)
        {
            int last = extraBoxes.Count - 1;
            if (wrappers[last] != null) Destroy(wrappers[last].gameObject);
            wrappers.RemoveAt(last);
            extraBoxes.RemoveAt(last);
            if (last < keyLabels.Count) keyLabels.RemoveAt(last);
        }
        gui.items = vanillaBoxes.Concat(extraBoxes).ToArray();
        gui.UpdateItems();
        UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(row);
    }

    /// <summary>The cloned box's key icon: the game draws it as a TMP sprite (UI_InputIcon, "&lt;sprite=N&gt;").</summary>
    private static TMPro.TMP_Text? FindKeyLabel(InventoryItemUI box)
    {
        var t = box.GetComponentsInChildren<TMPro.TMP_Text>(true)
            .FirstOrDefault(x => x != box.nameText && (x.name.IndexOf("InputIcon", StringComparison.OrdinalIgnoreCase) >= 0
                                                       || (x.text ?? "").Contains("<sprite")));
        if (t == null) return null;
        // Whatever keeps it in sync with the hotbar binding would write pocket 1's key back.
        foreach (var mb in t.GetComponents<MonoBehaviour>())
            if (mb != t && mb is not UnityEngine.UI.Graphic && mb is not UnityEngine.UI.LayoutElement) mb.enabled = false;
        return t;
    }

    private static bool loggedSprites;

    /// <summary>Show the key's own glyph from the game's sprite sheet (plain text if there isn't one).</summary>
    private static void SetKey(TMPro.TMP_Text label, Key key)
    {
        string glyph = key switch { Key.Digit0 => "0", Key.Minus => "minus", Key.Equals => "equals", _ => ((int)(key - Key.Digit1) + 1).ToString() };
        string plain = key switch { Key.Minus => "-", Key.Equals => "=", _ => glyph };
        var sheet = label.spriteAsset != null ? label.spriteAsset : TMPro.TMP_Settings.defaultSpriteAsset;
        var names = new List<string>();
        if (sheet != null)
        {
            foreach (var ch in sheet.spriteCharacterTable) if (!string.IsNullOrEmpty(ch.name)) names.Add(ch.name);
            if (sheet.fallbackSpriteAssets != null)
                foreach (var fb in sheet.fallbackSpriteAssets)
                    if (fb != null) foreach (var ch in fb.spriteCharacterTable) if (!string.IsNullOrEmpty(ch.name)) names.Add(ch.name);
        }
        if (!loggedSprites)
        {
            loggedSprites = true;
            Plugin.Log.LogInfo($"Storage HUD: key icon '{label.text}' from sheet '{sheet?.name}': {string.Join(", ", names.Take(120))}");
        }
        // e.g. "Keyboard_5", "5_Key", "Alpha5", "Digit5", "minus"...
        bool Match(string n)
        {
            string s = n.ToLowerInvariant();
            if (glyph.Length > 1) return s.Contains(glyph) || (glyph == "equals" && s.Contains("equal")) || (glyph == "minus" && s.Contains("dash"));
            return System.Text.RegularExpressions.Regex.IsMatch(s, $@"(^|[^0-9a-z]|alpha|digit|key|num)({glyph})([^0-9a-z]|key|$)")
                   && !s.Contains("numpad") && !s.Contains("f" + glyph);
        }
        string? found = names.FirstOrDefault(Match);
        label.text = found != null ? $"<sprite name=\"{found}\" tint=1>" : plain;
        Plugin.Log.LogInfo($"Storage HUD: key {plain} -> {(found != null ? $"sprite '{found}'" : "plain text")}.");
    }

    // --- keys: 4 stays the back slot; 5-9, 0, -, = pick pockets 4-11 -------------------------------

    private static readonly Key[] PocketKeys =
    {
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.Minus, Key.Equals,
    };

    internal static void HandleKeys(CharacterItems items, Character c)
    {
        int n = c.player.itemSlots.Length;
        if (n <= Vanilla || c.input.itemSwitchBlocked) return;
        if ((c.data.currentItem != null && (c.data.currentItem.progress > 0f || c.data.currentItem.lastFinishedCast + 0.1f > Time.time))
            || !c.data.fullyConscious || !c.CanDoInput() || items.lockedFromSwitching || c.data.isClimbing || c.data.isRopeClimbing
            || c.data.currentStickyItem != null)
            return;

        // Scroll in the order the row is drawn: 1 2 3, the extra pockets, then the back slot.
        bool fwd = c.input.selectSlotForwardWasPressed, bwd = c.input.selectSlotBackwardWasPressed;
        if (fwd || bwd)
        {
            c.input.selectSlotForwardWasPressed = c.input.selectSlotBackwardWasPressed = false;
            var order = new List<byte>();
            for (int i = 0; i < n; i++) order.Add(IdOf(i));
            order.Add(3);
            int at = items.lastSelectedSlot.IsSome ? order.IndexOf(items.lastSelectedSlot.Value) : -1;
            int next = fwd ? (at < 0 ? 0 : (at + 1) % order.Count) : (at < 0 ? order.Count - 1 : (at - 1 + order.Count) % order.Count);
            Equip(items, order[next]);
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;
        for (int index = Vanilla; index < n && index - Vanilla < PocketKeys.Length; index++)
        {
            if (!kb[PocketKeys[index - Vanilla]].wasPressedThisFrame) continue;
            byte id = IdOf(index);
            if (items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == id) Equip(items, null);
            else Equip(items, id);
            return;
        }
    }

    private static void Equip(CharacterItems items, byte? id)
    {
        items.lastSwitched = Time.time;
        items.timesSwitchedRecently++;
        items.EquipSlot(id.HasValue ? Optionable<byte>.Some(id.Value) : Optionable<byte>.None);
    }
}

[HarmonyPatch]
internal static class StoragePatches
{
    /// <summary>E on a pack on the ground: our card first (see Storage.OfferFromGround).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Backpack), nameof(Backpack.Interact))]
    private static bool PackCard(Backpack __instance, Character interactor)
    {
        if (__instance.itemState != ItemState.Ground) return true;
        try { return !Storage.OfferFromGround(__instance, interactor); }
        catch (Exception e) { Plugin.Log.LogWarning($"Storage: pack card failed: {e.Message}"); return true; }
    }

    /// <summary>Extra pockets live at ids 4+ (3 is the back slot). Only pockets we made: another mod's slots answer for themselves.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.GetItemSlot))]
    private static bool ExtraPockets(Player __instance, byte slotID, ref ItemSlot __result)
    {
        if (!ModConfig.Storage.Value || slotID < 4 || slotID >= 250) return true;
        var slots = __instance.itemSlots;
        int i = slotID - 1;
        if (slots == null || i >= slots.Length || slots[i] == null || slots[i].itemSlotID != slotID) return true;
        __result = slots[i];
        return false;
    }

    /// <summary>The same sync as vanilla, but the pocket count comes from the data (the host decides it).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.SyncInventoryRPC))]
    private static bool SizedSync(Player __instance, byte[] data, bool forceSync)
    {
        if (!forceSync && PhotonNetwork.IsMasterClient) return true; // vanilla logs and ignores it
        var sync = IBinarySerializable.GetFromManagedArray<InventorySyncData>(data);
        if (sync.slots == null || sync.slots.Length == 0) return true;
        Storage.Resize(__instance, sync.slots.Length, host: false);
        return true; // vanilla fills the (now matching) slots
    }

    /// <summary>Dying / passing out drops pockets 0-2 (and the back slot); drop the extra ones too.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CharacterItems), "DropAllItems")]
    private static void DropExtras(CharacterItems __instance)
    {
        var c = __instance.character;
        if (c == null || !c.IsLocal || c.player == null) return;
        var slots = c.player.itemSlots;
        Vector3 pos = c.GetBodypart(BodypartType.Hip).transform.position + Vector3.up * 2f;
        for (int i = 3; i < slots.Length; i++)
        {
            var s = slots[i];
            if (s == null || s.IsEmpty() || !s.prefab.UIData.canDrop) continue;
            __instance.photonView.RPC("DropItemFromSlotRPC", RpcTarget.All, Storage.IdOf(i), pos);
            pos += Vector3.up * 0.5f;
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterItems), "DoSwitching")]
    private static void PocketKeys(CharacterItems __instance)
    {
        var c = __instance.character;
        if (c == null || !c.IsLocal || c.player == null) return;
        Storage.HandleKeys(__instance, c);
    }

}
