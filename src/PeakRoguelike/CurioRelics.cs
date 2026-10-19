using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;
using STATUS = CharacterAfflictions.STATUSTYPE;

namespace PeakRoguelike;

public enum Curio { None, Bugle, Binoculars, Compass, Frisbee, BingBong }

/// <summary>
/// Starting relics: the crash-site curios. Each scout binds one (the curio stays where it lies, so
/// everyone can pick from all of them). Normal relics: they never raise or add to a blood moon.
///   Bugle      - helping hand reaches twice as far, carried scouts weigh half, more healing in crates
///   Binoculars - pings the nearest item lying in the world, more food in crates
///   Compass    - pings the nearest unopened crate, more tools in crates (the Pirate Compass too)
///   Frisbee    - everything you throw knocks back what it hits
///   Bing Bong  - chaos: every few minutes he plays a prank on you
/// </summary>
[HarmonyPatch]
internal class CurioRelics : MonoBehaviour
{
    private const string PropKey = "prl.cr";
    private const float M = 1f / 1.6f;

    internal static Curio Mine { get; private set; }

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Curio relics");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<CurioRelics>();
    }

    internal static void Reset()
    {
        Mine = Curio.None;
        Publish();
    }

    // --- what is what ---------------------------------------------------------------------------

    internal static Curio KindOf(Item? item)
    {
        if (item == null) return Curio.None;
        if (item.GetComponent<BugleSFX>() != null && item.GetComponent<MagicBugle>() == null) return Curio.Bugle;
        if (item.GetComponentInChildren<Actions_Binoculars>(true) != null) return Curio.Binoculars;
        if (item.GetComponentInChildren<CompassPointer>(true) is { } c && c.compassType != CompassPointer.CompassType.Warp) return Curio.Compass;
        if (item.GetComponent<Frisbee>() != null) return Curio.Frisbee;
        if (item.GetComponent<BingBong>() != null) return Curio.BingBong;
        return Curio.None;
    }

    internal static string Name(Curio c) => c switch
    {
        Curio.Bugle => "Bugle",
        Curio.Binoculars => "Binoculars",
        Curio.Compass => "Compass",
        Curio.Frisbee => "Frisbee",
        Curio.BingBong => "Bing Bong",
        _ => "",
    };

    internal static string Desc(Curio c) => c switch
    {
        Curio.Bugle => "Your helping hand reaches twice as far.\nScouts you carry weigh half as much.\nMore healing in your crates.",
        Curio.Binoculars => $"Every {ModConfig.CurioPingSeconds.Value:0} seconds, you sense the nearest useful thing lying out in the world.\nMore food in your crates.",
        Curio.Compass => $"Every {ModConfig.CurioPingSeconds.Value:0} seconds, you sense the nearest unopened crate.\nMore tools in your crates.",
        Curio.Frisbee => "Everything you throw knocks back whatever it hits.",
        Curio.BingBong => "He wants to play.",
        _ => "",
    };

    /// <summary>The curio's item (for its icon on the card and the relic tray).</summary>
    internal static Item? ItemOf(Curio c) =>
        c == Curio.None ? null : Items.Database.FirstOrDefault(i => i != null && KindOf(i) == c
            && !(c == Curio.Compass && i.GetComponentInChildren<CompassPointer>(true)?.compassType == CompassPointer.CompassType.Pirate));

    /// <summary>Does this scout carry this starting relic? (Others' come from their player properties.)</summary>
    internal static bool Holds(Character? c, Curio kind)
    {
        if (c == null || kind == Curio.None) return false;
        if (c.IsLocal) return Mine == kind;
        var owner = c.photonView?.Owner;
        return owner != null && owner.CustomProperties.TryGetValue(PropKey, out object raw) && raw is int v && v == (int)kind;
    }

    private static void Publish()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
        try { PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [PropKey] = (int)Mine }); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't share the starting relic: {e.Message}"); }
    }

    // --- binding -------------------------------------------------------------------------------

    internal static void Bind(Curio c)
    {
        Mine = c;
        Publish();
        RunState.Touch();
        nextPing = Time.time + 1f;
        nextPrank = Time.time + Random.Range(ModConfig.BingBongMinSeconds.Value, ModConfig.BingBongMaxSeconds.Value);
        Hud.Banner("STARTING RELIC", Name(c), ChoiceScreen.Cream);
        Plugin.Log.LogInfo($"Curio relic: bound the {Name(c)}.");
    }

    /// <summary>The relic card for a curio picked up off the ground (the curio stays where it lies).</summary>
    internal static void ShowCard(Item item, Character who, Curio c)
    {
        bool locked = Mine != Curio.None;
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "STARTING RELIC",
            Subtitle = locked ? $"You carry the {Name(Mine)}" : "Choose one",
            Confirm = locked ? "ONE RELIC ONLY" : "BIND",
            BackHint = "Leave it",
            Locked = locked,
            Cards = new List<CardSpec>
            {
                new()
                {
                    Style = CardStyle.Perk,
                    Theme = PerkTheme.Explorer,
                    Title = Name(c),
                    Tag = "Relic",
                    TagColor = ChoiceScreen.ThemeGlow(PerkTheme.Explorer),
                    Body = Desc(c),
                    Footer = "Lasts the whole run",
                    Icon = Items.Icon(item),
                },
            },
            StillValid = () => item != null && who != null && Vector3.Distance(who.Center, item.Center()) < 6f * M,
            OnConfirm = _ => { if (Mine == Curio.None) Bind(c); },
            OnBack = () => { },
            OnCancelled = () => { },
        });
    }

    internal static void DevBind(Curio c)
    {
        Mine = Curio.None;
        Bind(c);
    }

    // --- per-frame: the marker and pranks -------------------------------------------------------

    private static float nextPing, nextPrank;
    private static Luggage? markedCrate;
    private static Item? markedItem;
    private static string markedName = "";

    private void Update()
    {
        if (!ModConfig.CurioRelics.Value || Mine == Curio.None || Character.localCharacter is not { } me || me.data.dead) { Marker.Hide(); return; }
        try
        {
            if (Mine == Curio.Binoculars || Mine == Curio.Compass)
            {
                if (Time.time >= nextPing || !StillMarked())
                {
                    nextPing = Time.time + Mathf.Max(2f, ModConfig.CurioPingSeconds.Value);
                    FindTarget(me);
                }
                ShowMarker(me);
            }
            else Marker.Hide();
            if (Mine == Curio.BingBong && Time.time >= nextPrank) BingBongPranks.Tick(me, ref nextPrank);
        }
        catch (System.Exception e)
        {
            nextPing = nextPrank = Time.time + 30f;
            Marker.Hide();
            Plugin.Log.LogError($"Curio relic failed: {e}");
        }
    }

    private const float NearM = 10f, FarM = 250f;

    private static float MetresTo(Character me, Vector3 p) => Vector3.Distance(me.Center, p) / M;

    private static bool InRange(Character me, Vector3 p) => MetresTo(me, p) is var d && d >= NearM && d <= FarM;

    /// <summary>The Binoculars only find helpful things: food, healing, beans and fungi - never explosives.</summary>
    internal static bool Helpful(Item i)
    {
        if (i.GetComponentInChildren<Dynamite>(true) != null || i.GetComponentInChildren<ExplosionEffect>(true) != null) return false;
        string n = i.gameObject.name.ToLowerInvariant();
        if (n.Contains("dynamite") || n.Contains("bomb") || n.Contains("explos")) return false;
        return Relics.IsFood(i) || Relics.IsHealing(i)
            || i.GetComponent<MagicBean>() != null || i.GetComponentInChildren<CloudFungus>(true) != null || i.GetComponentInChildren<ShelfShroom>(true) != null;
    }

    private static bool StillMarked() =>
        Mine == Curio.Compass
            ? markedCrate != null && markedCrate.state == Luggage.LuggageState.Closed
            : markedItem != null && markedItem.itemState == ItemState.Ground && markedItem.gameObject.activeInHierarchy;

    private static void FindTarget(Character me)
    {
        markedCrate = null;
        markedItem = null;
        if (Mine == Curio.Compass)
        {
            markedCrate = Luggage.ALL_LUGGAGE
                .Where(l => l != null && l is not RespawnChest && l.state == Luggage.LuggageState.Closed && !SkyDrop.Falling(l) && InRange(me, l.Center()))
                .OrderBy(l => Vector3.Distance(l.Center(), me.Center))
                .FirstOrDefault();
            markedName = "Crate";
            return;
        }
        markedItem = Item.ALL_ACTIVE_ITEMS
            .Where(i => i != null && i.itemState == ItemState.Ground && i.gameObject.activeInHierarchy && i.holderCharacter == null && InRange(me, i.Center()) && Helpful(i))
            .OrderBy(i => Vector3.Distance(i.Center(), me.Center))
            .FirstOrDefault();
        markedName = markedItem != null ? markedItem.GetItemName() : "";
    }

    private static void ShowMarker(Character me)
    {
        Vector3? at = Mine == Curio.Compass ? markedCrate?.Center() : markedItem?.Center();
        if (at == null || !StillMarked() || ChoiceScreen.IsOpen || !InRange(me, at.Value)) { Marker.Hide(); return; }
        var colour = Mine == Curio.Compass ? new Color(0.92f, 0.78f, 0.55f) : new Color(0.6f, 0.95f, 0.55f);
        Marker.Show(at.Value + Vector3.up * 1.2f * M, $"{markedName} {MetresTo(me, at.Value):0}m", colour);
    }

    // --- crates: more of your relic's kind ------------------------------------------------------

    /// <summary>Whoever rolls a crate: each card has a chance to become their relic's kind, from the same crate's pool.</summary>
    internal static void Boost(Luggage luggage, List<Item> cards)
    {
        System.Func<Item, bool>? kind = Mine switch
        {
            Curio.Bugle => Relics.IsHealing,
            Curio.Binoculars => Relics.IsFood,
            Curio.Compass => Relics.IsMovementItem,
            _ => null,
        };
        if (!ModConfig.CurioRelics.Value || kind == null) return;
        for (int i = 0; i < cards.Count; i++)
        {
            if (kind(cards[i]) || Random.value >= ModConfig.CurioCrateBoost.Value) continue;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var pick = luggage.GetObjectsToSpawn(1, true)?.Where(o => o != null).Select(o => o.GetComponent<Item>()).FirstOrDefault(x => x != null);
                if (pick == null || !kind(pick) || (ModConfig.Leveling.Value && Items.IsEarnedOnly(pick)) || cards.Any(c => c.itemID == pick.itemID)) continue;
                Plugin.Log.LogInfo($"{Name(Mine)} relic: card {cards[i].gameObject.name} -> {pick.gameObject.name}.");
                cards[i] = pick;
                break;
            }
        }
    }

    // --- Bugle: longer helping hand, lighter carries -------------------------------------------

    internal static float ReachFor(CharacterGrabbing g) =>
        ModConfig.CurioRelics.Value && Holds(g.character, Curio.Bugle) ? 8f : 4f;

    [HarmonyTranspiler, HarmonyPatch(typeof(CharacterGrabbing), "Reach")]
    private static IEnumerable<CodeInstruction> LongerReach(IEnumerable<CodeInstruction> code)
    {
        bool done = false;
        foreach (var c in code)
        {
            if (!done && c.opcode == OpCodes.Ldc_R4 && c.operand is float f && f == 4f)
            {
                done = true;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CurioRelics), nameof(ReachFor)));
                continue;
            }
            yield return c;
        }
        if (!done) Plugin.Log.LogWarning("Bugle relic: couldn't find the helping hand's reach.");
    }

    internal static int CarryWeight(CharacterAfflictions a) =>
        ModConfig.CurioRelics.Value && Holds(a.character, Curio.Bugle) ? 4 : 8;

    [HarmonyTranspiler, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateWeight))]
    private static IEnumerable<CodeInstruction> LighterCarry(IEnumerable<CodeInstruction> code)
    {
        var carried = AccessTools.Field(typeof(CharacterData), nameof(CharacterData.carriedPlayer));
        bool seen = false, done = false;
        foreach (var c in code)
        {
            if (c.LoadsField(carried)) seen = true;
            if (seen && !done && c.opcode == OpCodes.Ldc_I4_8)
            {
                done = true;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CurioRelics), nameof(CarryWeight)));
                continue;
            }
            yield return c;
        }
        if (!done) Plugin.Log.LogWarning("Bugle relic: couldn't find the carried scout's weight.");
    }

    // --- Frisbee: throws knock back ------------------------------------------------------------

    [HarmonyPostfix, HarmonyPatch(typeof(GlobalEvents), nameof(GlobalEvents.TriggerItemThrown))]
    private static void Thrown(Item item)
    {
        if (!ModConfig.CurioRelics.Value || Mine != Curio.Frisbee || item == null) return;
        if (item.lastThrownCharacter is { } thrower && !thrower.IsLocal) return;
        var k = item.GetComponent<FrisbeeKnock>() ?? item.gameObject.AddComponent<FrisbeeKnock>();
        k.Arm();
    }

    // --- relic tray ----------------------------------------------------------------------------

    /// <summary>The tray medallion's icon (null = no starting relic).</summary>
    internal static Texture2D? TrayIcon() => Mine == Curio.None ? null : Items.Icon(ItemOf(Mine));
}

/// <summary>
/// The relic's marker: "Crate 42m" floating over its target on your screen, seen through terrain and
/// fog. Off screen (or behind you) it waits at the screen edge on the target's side.
/// </summary>
internal class Marker : MonoBehaviour
{
    private static Marker? instance;
    private RectTransform root = null!, box = null!;
    private TextMeshProUGUI label = null!;
    private Image dot = null!;
    private CanvasGroup group = null!;
    private float alpha, want;

    private static Marker Get()
    {
        if (instance != null) return instance;
        var go = new GameObject("PEAK Roguelike - Relic marker");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Marker>();
        instance.Build();
        return instance;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 840;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)transform;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        box = Ui.Rect(root, "Marker");
        box.anchorMin = box.anchorMax = Vector2.zero;
        box.sizeDelta = new Vector2(300, 60);
        dot = Ui.Image(box, "Dot", null, Color.white);
        dot.rectTransform.sizeDelta = new Vector2(12, 12);
        dot.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        dot.rectTransform.anchoredPosition = new Vector2(0, -10);
        dot.raycastTarget = false;
        label = Ui.Text(box, "Label", "", Hud.PromptFont ?? UiArt.BodyFont, 22, Color.white, TextAlignmentOptions.Center);
        label.rectTransform.sizeDelta = new Vector2(300, 30);
        label.rectTransform.anchoredPosition = new Vector2(0, 12);
        label.characterSpacing = 2;
        label.outlineWidth = 0.18f;
        label.outlineColor = new Color32(0, 0, 0, 200);
        label.raycastTarget = false;
    }

    internal static void Hide()
    {
        if (instance != null) instance.want = 0f;
    }

    internal static void Show(Vector3 world, string text, Color colour)
    {
        var m = Get();
        var cam = Camera.main;
        if (cam == null) { m.want = 0f; return; }
        m.want = 1f;
        m.label.text = text;
        m.label.color = colour;
        m.dot.color = colour;

        // World point -> canvas point, pinned inside the screen edges when off screen or behind.
        Vector3 s = cam.WorldToScreenPoint(world);
        float scale = m.root.rect.width / Mathf.Max(1f, Screen.width);
        Vector2 size = m.root.rect.size;
        Vector2 p = new Vector2(s.x, s.y) * scale;
        bool behind = s.z < 0f;
        if (behind) p = size - p;
        const float pad = 60f;
        bool off = behind || p.x < pad || p.x > size.x - pad || p.y < pad || p.y > size.y - pad;
        if (off)
        {
            Vector2 c = size / 2f, d = p - c;
            if (behind && d.sqrMagnitude < 1f) d = Vector2.down;
            float k = Mathf.Min((c.x - pad) / Mathf.Max(0.001f, Mathf.Abs(d.x)), (c.y - pad) / Mathf.Max(0.001f, Mathf.Abs(d.y)));
            p = c + d * Mathf.Min(1f, k);
        }
        m.box.anchoredPosition = p;
    }

    private void Update()
    {
        alpha = Mathf.MoveTowards(alpha, want, Time.unscaledDeltaTime * 4f);
        group.alpha = alpha;
    }
}

/// <summary>Rides a thrown item for a few seconds: the first scout or creature it hits gets knocked back.</summary>
public class FrisbeeKnock : MonoBehaviour
{
    private float until;
    private Item item = null!;

    internal void Arm()
    {
        item = GetComponent<Item>();
        until = Time.time + 4f;
        enabled = true;
    }

    private void Update()
    {
        if (Time.time > until) enabled = false;
    }

    private void OnCollisionEnter(Collision coll)
    {
        if (!enabled || item == null || Time.time > until) return;
        var c = coll.gameObject.GetComponentInParent<Character>();
        if (c == null || c.IsLocal || c.data.dead) return;
        bool enemy = c.isBot || c.isZombie || c.isScoutmaster;
        if (!enemy && !ModConfig.FrisbeeHitsScouts.Value) return;
        float speed = coll.relativeVelocity.magnitude;
        if (speed < 4f) return;
        enabled = false;
        Vector3 dir = (-coll.relativeVelocity.normalized + Vector3.up * 0.35f).normalized;
        float force = ModConfig.FrisbeeForce.Value * Mathf.Clamp(speed / 15f, 0.5f, 1.5f);
        c.Fall(1.2f);
        c.AddForceAtPosition(dir * force, coll.contacts.Length > 0 ? coll.contacts[0].point : c.Center, 3f);
        Plugin.Log.LogInfo($"Frisbee relic: {item.gameObject.name} knocked back {c.characterName}.");
    }
}

/// <summary>
/// The Bing Bong relic's pranks on its carrier, every couple of minutes: swap an item with another
/// scout, a random level-up, a nap, swap places, or swap every status bar with another scout.
/// Never while climbing, at a campfire or during a blood moon.
/// </summary>
internal static class BingBongPranks
{
    private const float M = 1f / 1.6f;

    private enum Prank { SwapItem, LevelUp, Nap, SwapPlace, SwapStatus }

    private static bool Steady(Character c) =>
        !c.data.dead && !c.data.passedOut && c.data.fullyConscious && !c.data.isClimbingAnything && !c.data.isCarried && !BloodTornado.Carrying(c);

    internal static void Tick(Character me, ref float next)
    {
        if (!Steady(me) || Sanctuary.LocalSafe || BloodMoon.Active || ChoiceScreen.IsOpen) { next = Time.time + 5f; return; }
        var others = Squad.Alive().Where(o => o != me && Modless.HasMod(o) && Steady(o)).ToList();
        var options = new List<Prank> { Prank.Nap };
        if (Offers.Carried().Count > 0) options.Add(Prank.LevelUp);
        if (others.Count > 0)
        {
            options.Add(Prank.SwapStatus);
            if (Pocketed(Player.localPlayer).Any() || others.Any(o => Pocketed(o.player).Any())) options.Add(Prank.SwapItem);
            if (me.data.isGrounded && others.Any(o => o.data.isGrounded)) options.Add(Prank.SwapPlace);
        }
        next = Time.time + Random.Range(ModConfig.BingBongMinSeconds.Value, ModConfig.BingBongMaxSeconds.Value);
        Play(me, options[Random.Range(0, options.Count)], others);
    }

    internal static void DevPrank()
    {
        if (Character.localCharacter is not { } me) return;
        float next = 0f;
        Tick(me, ref next);
    }

    private static void Play(Character me, Prank p, List<Character> others)
    {
        Plugin.Log.LogInfo($"Bing Bong: {p}.");
        switch (p)
        {
            case Prank.Nap:
                me.refs.afflictions.AddStatus(STATUS.Drowsy, 0.15f);
                me.Fall(6f);
                Hud.Banner("BING BONG", "Nap time", ChoiceScreen.Cream);
                break;
            case Prank.LevelUp:
            {
                var offers = Offers.Carried();
                var o = offers[Random.Range(0, offers.Count)];
                int branch = o.Branch;
                if (o.Kind == OfferKind.Fork)
                {
                    var paths = Offers.Paths(o.Tree!, o.Level);
                    branch = paths[Random.Range(0, paths.Length)].Branch;
                }
                Offers.ApplyCarried(o, branch);
                Hud.Banner("BING BONG", $"{o.Tree!.Name} level {o.Level}", Trees.Gold);
                break;
            }
            case Prank.SwapItem:
            {
                var pool = Pocketed(Player.localPlayer).Any() ? others : others.Where(o => Pocketed(o.player).Any()).ToList();
                var t = pool[Random.Range(0, pool.Count)];
                string mine = Take(Player.localPlayer, me);
                Net.Bing(t.photonView.Owner.ActorNumber, 0, mine);
                break;
            }
            case Prank.SwapPlace:
            {
                var pool = others.Where(o => o.data.isGrounded).ToList();
                var t = pool[Random.Range(0, pool.Count)];
                Vector3 mine = me.Center, theirs = t.Center;
                Net.Bing(t.photonView.Owner.ActorNumber, 2, mine);
                me.photonView.RPC("WarpPlayerRPC", RpcTarget.All, theirs, true);
                Hud.Banner("BING BONG", $"Swapped places with {t.characterName}", ChoiceScreen.Cream);
                break;
            }
            case Prank.SwapStatus:
            {
                var t = others[Random.Range(0, others.Count)];
                Net.Bing(t.photonView.Owner.ActorNumber, 3, (float[])me.refs.afflictions.currentStatuses.Clone());
                break;
            }
        }
    }

    // --- the other half of a swap (both scouts run the mod) -------------------------------------

    internal static void Receive(int fromActor, int kind, object payload)
    {
        var me = Character.localCharacter;
        if (me == null) return;
        var from = Squad.Alive().FirstOrDefault(c => c.photonView?.Owner?.ActorNumber == fromActor);
        string who = from != null ? from.characterName : "Someone";
        switch (kind)
        {
            case 0: // their item for one of mine
            {
                string mine = Take(Player.localPlayer, me);
                Give(payload as string, me);
                Net.Bing(fromActor, 1, mine);
                Hud.Banner("BING BONG", $"{who} swapped an item with you", ChoiceScreen.Cream);
                break;
            }
            case 1: // my swap's return
                Give(payload as string, me);
                Hud.Banner("BING BONG", $"Swapped an item with {who}", ChoiceScreen.Cream);
                break;
            case 2: // swap places
                if (payload is Vector3 at) me.photonView.RPC("WarpPlayerRPC", RpcTarget.All, at, true);
                Hud.Banner("BING BONG", $"Swapped places with {who}", ChoiceScreen.Cream);
                break;
            case 3: // their status bars for mine
                if (payload is float[] theirs)
                {
                    Net.Bing(fromActor, 4, (float[])me.refs.afflictions.currentStatuses.Clone());
                    SetStatuses(me, theirs);
                    Hud.Banner("BING BONG", $"{who} swapped bodies with you", ChoiceScreen.Cream);
                }
                break;
            case 4: // my status swap's return
                if (payload is float[] back)
                {
                    SetStatuses(me, back);
                    Hud.Banner("BING BONG", $"Swapped bodies with {who}", ChoiceScreen.Cream);
                }
                break;
        }
    }

    private static IEnumerable<ItemSlot> Pocketed(Player? p) =>
        p?.itemSlots?.Where(s => s != null && !s.IsEmpty() && s.prefab != null && !SugarRush.IsMarked(s.data)) ?? Enumerable.Empty<ItemSlot>();

    /// <summary>One copy of a random pocket item leaves your pockets ("" = nothing to give).</summary>
    private static string Take(Player? p, Character me)
    {
        var slots = Pocketed(p).ToList();
        if (p == null || slots.Count == 0) return "";
        var s = slots[Random.Range(0, slots.Count)];
        string name = s.prefab.gameObject.name;
        if (Stacks.TakeOne(p, s.itemSlotID)) return name;
        var sel = me.refs.items.currentSelectedSlot;
        bool held = sel.IsSome && sel.Value == s.itemSlotID;
        p.EmptySlot(Optionable<byte>.Some(s.itemSlotID));
        if (held) me.refs.items.EquipSlot(Optionable<byte>.None);
        return name;
    }

    private static void Give(string? prefab, Character me)
    {
        if (string.IsNullOrEmpty(prefab)) return;
        var item = Items.Database.FirstOrDefault(i => i != null && i.gameObject.name == prefab);
        if (item != null) GameUtils.instance.InstantiateAndGrab(item, me);
    }

    // Weight, thorns and arrows come from what you carry; glass stays yours.
    private static readonly HashSet<int> Kept = new() { (int)STATUS.Weight, (int)STATUS.Thorns, (int)STATUS.Arrow, (int)STATUS.Petrify };

    private static void SetStatuses(Character me, float[] to)
    {
        var aff = me.refs.afflictions;
        var now = aff.currentStatuses;
        for (int k = 0; k < now.Length && k < to.Length; k++)
        {
            if (Kept.Contains(k)) continue;
            float d = to[k] - now[k];
            if (d > 0f) aff.AddStatus((STATUS)k, d, ignoreInvincibility: true);
            else if (d < 0f) aff.SubtractStatus((STATUS)k, -d);
        }
    }
}
