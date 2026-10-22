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
///   Bugle      - helping hand reaches twice as far, pulls (and is pulled) twice as hard, carried scouts weigh half, more healing in crates
///   Binoculars - pings the nearest item lying in the world, more food in crates
///   Compass    - pings the nearest unopened crate, more tools in crates (the Pirate Compass too)
///   Frisbee    - everything you throw knocks back what it hits
///   Bing Bong  - chaos: every few minutes he plays a prank on you
/// </summary>
[HarmonyPatch]
internal class CurioRelics : MonoBehaviour
{
    private const string PropKey = "prl.cur"; // not "prl.cr": RunState publishes crafted levels there
    private const float M = 1f / 1.6f;

    internal static Curio Mine { get; private set; }
    private static CurioRelics? self;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Curio relics");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        self = go.AddComponent<CurioRelics>();
    }

    internal static void Reset()
    {
        Mine = Curio.None;
        wakeNoticed = titleStarted = titleDone = sawShore = false;
        awakeSince = flatSince = -1f;
        declinedAt = -1;
        BingBongPranks.Reset();
        LightsOut.End();
        Publish();
    }

    /// <summary>The Void takes the starting relic too (the run's other state stays).</summary>
    internal static void Wipe()
    {
        if (Mine == Curio.None) return;
        Plugin.Log.LogInfo($"Curio relic: the Void took the {Name(Mine)}.");
        Mine = Curio.None;
        BingBongPranks.Reset();
        LightsOut.End();
        Marker.Hide();
        Publish();
        RunState.Touch();
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
        Curio.Bugle => "Your helping hand reaches twice as far and pulls twice as hard.\nHelping hands pull you up twice as hard.\nScouts you carry weigh half as much.\nMore healing in your crates.",
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
    /// <summary>A player's starting relic, from their player properties (for bug reports).</summary>
    internal static Curio Of(Photon.Realtime.Player p) =>
        p.IsLocal ? Mine : p.CustomProperties.TryGetValue(PropKey, out object raw) && raw is int v ? (Curio)v : Curio.None;

    /// <summary>This scout has a starting relic's effects: their own, or a badge relic copying it.</summary>
    internal static bool Has(Curio kind) =>
        kind != Curio.None && (Mine == kind || Badges.GrantsCurio(PhotonNetwork.LocalPlayer, kind));

    private static readonly Curio[] Kinds = { Curio.Bugle, Curio.Binoculars, Curio.Compass, Curio.Frisbee, Curio.BingBong };

    /// <summary>Any starting-relic effect at all (own or from a badge).</summary>
    private static bool AnyActive => Mine != Curio.None || Kinds.Any(Has);

    /// <summary>A badge relic just gave you a starting relic's effects: its clocks start now.</summary>
    internal static void BadgeBound(Curio c)
    {
        nextCratePing = nextItemPing = Time.time + 1f;
        if (c == Curio.BingBong) nextPrank = Time.time + Random.Range(ModConfig.BingBongMinSeconds.Value, ModConfig.BingBongMaxSeconds.Value);
    }

    internal static bool Holds(Character? c, Curio kind)
    {
        if (c == null || kind == Curio.None) return false;
        if (c.IsLocal) return Has(kind);
        var owner = c.photonView?.Owner;
        return owner != null && ((owner.CustomProperties.TryGetValue(PropKey, out object raw) && raw is int v && v == (int)kind) || Badges.GrantsCurio(owner, kind));
    }

    /// <summary>On the Shore, while some scout with the mod has yet to bind a starting relic.</summary>
    internal static bool StillChoosing
    {
        get
        {
            if (!MapHandler.Exists || Singleton<MapHandler>.Instance.currentSegment != 0) return false;
            if (Mine == Curio.None) return true;
            if (!PhotonNetwork.InRoom) return false;
            return PhotonNetwork.PlayerList.Any(p => !p.IsLocal && Modless.HasMod(p)
                && !(p.CustomProperties.TryGetValue(PropKey, out object raw) && raw is int v && v != (int)Curio.None));
        }
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
        nextCratePing = nextItemPing = Time.time + 1f;
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
                Card(c, Items.Icon(item)),
            },
            StillValid = () => item != null && who != null && Vector3.Distance(who.Center, item.Center()) < 6f * M,
            OnConfirm = _ => { if (Mine == Curio.None) Bind(c); },
            OnBack = () => { },
            OnCancelled = () => { },
        });
    }

    private static CardSpec Card(Curio c, Texture? icon) => new()
    {
        Style = CardStyle.Perk,
        Theme = PerkTheme.Explorer,
        Title = Name(c),
        Tag = "Relic",
        TagColor = ChoiceScreen.ThemeGlow(PerkTheme.Explorer),
        Body = Desc(c),
        Footer = "Lasts the whole run",
        Icon = icon,
    };

    internal static void DevBind(Curio c)
    {
        Mine = Curio.None;
        Bind(c);
    }

    // --- per-frame: the marker and pranks -------------------------------------------------------

    private static float nextCratePing, nextItemPing, nextPrank;
    private static Luggage? markedCrate;
    private static Item? markedItem;
    private static string markedName = "";

    // --- waking at the crash ---------------------------------------------------------------------

    private static bool wakeNoticed, titleStarted, titleDone, sawShore;
    private static float awakeSince = -1f;

    /// <summary>
    /// Once per run, right after the game's "SHORE / DAY 1" title fades: the curios are out there.
    /// (Earlier, it played behind the black wake-up screen.) Scouts who never see that title - a late
    /// join on the Shore - get it after 10 seconds awake. Announced, never explained. Not for scouts
    /// who already carry one, or who join after the Shore (they get the pick instead: LatePick).
    /// </summary>
    private static void WakeNotice(Character? me)
    {
        if (wakeNoticed || !ModConfig.CurioRelics.Value) return;
        if (me == null || me.data.dead || !MapHandler.Exists) { awakeSince = -1f; return; }
        if (Singleton<MapHandler>.Instance.currentSegment != 0) { wakeNoticed = true; return; }
        if (!me.data.fullyConscious || me.data.passedOut || LoadingScreenHandler.loading) { awakeSince = -1f; return; }
        if (awakeSince < 0f) awakeSince = Time.time;
        float awake = Time.time - awakeSince;
        if (awake >= 5f) sawShore = true;
        if (Mine != Curio.None || !ModConfig.CurioWakeNotice.Value) { if (sawShore) wakeNoticed = true; return; }
        if (awake < 1f || (titleStarted ? !titleDone : awake < 10f)) return;
        wakeNoticed = sawShore = true;
        Hud.Banner("", "Something survived the crash", ChoiceScreen.LevelGold, "It's waiting for you.");
        Plugin.Log.LogInfo($"Curio relic: wake notice shown ({(titleDone ? "after the Shore title" : "no title seen")}).");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GUIManager), nameof(GUIManager.SetHeroTitle))]
    private static void HeroTitle(GUIManager __instance)
    {
        if (self == null || !MapHandler.Exists || Singleton<MapHandler>.Instance.currentSegment != 0 || titleStarted) return;
        titleStarted = true;
        self.StartCoroutine(WatchTitle(__instance));
    }

    private static System.Collections.IEnumerator WatchTitle(GUIManager gui)
    {
        yield return null;
        while (gui != null && gui.heroCanvasObject != null && gui.heroCanvasObject.activeSelf) yield return null;
        titleDone = true;
    }

    // --- joining after the Shore -----------------------------------------------------------------

    private static int declinedAt = -1;
    private static float flatSince = -1f;

    /// <summary>
    /// A scout who never stood on the Shore this run (joined late) gets every starting relic on one
    /// screen, once on flat ground. Back puts it off to the next biome.
    /// </summary>
    private static void LatePick(Character? me)
    {
        if (!ModConfig.CurioRelics.Value || Mine != Curio.None || sawShore || ChoiceScreen.IsOpen || VoidRun.Active) { flatSince = -1f; return; }
        if (me == null || !MapHandler.Exists || LoadingScreenHandler.loading) { flatSince = -1f; return; }
        int seg = Singleton<MapHandler>.Instance.currentSegment;
        if (seg == 0 || seg == declinedAt || !Xp.OnFlatGround(me)) { flatSince = -1f; return; }
        if (flatSince < 0f) flatSince = Time.time;
        if (Time.time - flatSince < 1f) return;
        flatSince = -1f;
        var kinds = new[] { Curio.Bugle, Curio.Binoculars, Curio.Compass, Curio.Frisbee, Curio.BingBong };
        bool shown = ChoiceScreen.Show(new ScreenSpec
        {
            Title = "STARTING RELIC",
            Subtitle = "Choose one",
            Confirm = "BIND",
            BackHint = "Later",
            Cards = kinds.Select(k => Card(k, Items.Icon(ItemOf(k)))).ToList(),
            StillValid = () => Mine == Curio.None,
            OnConfirm = i => { if (Mine == Curio.None) Bind(kinds[i]); },
            OnBack = () => declinedAt = seg,
            OnCancelled = () => declinedAt = seg,
        });
        if (shown) Plugin.Log.LogInfo($"Curio relic: late-join pick shown (biome {seg}).");
    }

    private void Update()
    {
        try { WakeNotice(Character.localCharacter); }
        catch (System.Exception e) { wakeNoticed = true; Plugin.Log.LogError($"Wake notice failed: {e}"); }
        try { LatePick(Character.localCharacter); }
        catch (System.Exception e) { sawShore = true; Plugin.Log.LogError($"Late relic pick failed: {e}"); }
        if (Character.localCharacter is { } self)
        {
            try { BingBongPranks.TickHidden(self); }
            catch (System.Exception e) { Plugin.Log.LogError($"Bing Bong hidden item failed: {e}"); }
        }
        if (!ModConfig.CurioRelics.Value || !AnyActive || Character.localCharacter is not { } me || me.data.dead) { Marker.Hide(); return; }
        try
        {
            // The Compass and the Binoculars each keep their own marker - both show if you have both.
            TickCompass(me);
            TickBinoculars(me);
            if (Has(Curio.BingBong) && Time.time >= nextPrank) BingBongPranks.Tick(me, ref nextPrank);
        }
        catch (System.Exception e)
        {
            nextCratePing = nextItemPing = nextPrank = Time.time + 30f;
            Marker.Hide();
            Plugin.Log.LogError($"Curio relic failed: {e}");
        }
    }

    private const float NearM = 10f, FarM = 250f;

    private static float MetresTo(Character me, Vector3 p) => Vector3.Distance(me.Center, p) / M;

    /// <summary>The Compass looks out to FarM; the Binoculars see everywhere.</summary>
    private static bool InRange(Character me, Vector3 p, Curio kind) => MetresTo(me, p) is var d && d >= NearM && (kind == Curio.Binoculars || d <= FarM);

    /// <summary>The Binoculars only find helpful things: food, healing, beans and fungi - never explosives.</summary>
    internal static bool Helpful(Item i)
    {
        if (i.GetComponentInChildren<Dynamite>(true) != null || i.GetComponentInChildren<ExplosionEffect>(true) != null) return false;
        string n = i.gameObject.name.ToLowerInvariant();
        if (n.Contains("dynamite") || n.Contains("bomb") || n.Contains("explos")) return false;
        return Relics.IsFood(i) || Relics.IsHealing(i)
            || i.GetComponent<MagicBean>() != null || i.GetComponentInChildren<CloudFungus>(true) != null || i.GetComponentInChildren<ShelfShroom>(true) != null;
    }

    /// <summary>The Compass (own or a badge's): the nearest unopened crate, on its own marker.</summary>
    private static void TickCompass(Character me)
    {
        if (!Has(Curio.Compass)) { Marker.Hide(Marker.CrateMark); return; }
        bool still = markedCrate != null && markedCrate.state == Luggage.LuggageState.Closed;
        if (Time.time >= nextCratePing || !still)
        {
            nextCratePing = Time.time + Mathf.Max(2f, ModConfig.CurioPingSeconds.Value);
            markedCrate = Luggage.ALL_LUGGAGE
                .Where(l => l != null && l is not RespawnChest && l.state == Luggage.LuggageState.Closed && !SkyDrop.Falling(l) && InRange(me, l.Center(), Curio.Compass))
                .OrderBy(l => Vector3.Distance(l.Center(), me.Center))
                .FirstOrDefault();
        }
        Show(me, Marker.CrateMark, markedCrate != null && markedCrate.state == Luggage.LuggageState.Closed ? markedCrate.Center() : null,
            "Crate", Curio.Compass, new Color(0.92f, 0.78f, 0.55f));
    }

    /// <summary>The Binoculars (own or a badge's): the nearest helpful item, on its own marker.</summary>
    private static void TickBinoculars(Character me)
    {
        if (!Has(Curio.Binoculars)) { Marker.Hide(Marker.FindMark); return; }
        bool still = markedItem != null && markedItem.itemState == ItemState.Ground && markedItem.gameObject.activeInHierarchy;
        if (Time.time >= nextItemPing || !still)
        {
            nextItemPing = Time.time + Mathf.Max(2f, ModConfig.CurioPingSeconds.Value);
            var before = markedItem;
            // Every item in the level: Item.ALL_ACTIVE_ITEMS only holds held / moving ones, so the
            // berries on bushes, coconuts in trees and food the map placed were never in it.
            var helpful = FindObjectsByType<Item>(FindObjectsSortMode.None)
                .Where(i => i != null && i.itemState == ItemState.Ground && i.gameObject.activeInHierarchy && i.holderCharacter == null && InRange(me, i.Center(), Curio.Binoculars) && Helpful(i))
                .ToList();
            markedItem = helpful.OrderBy(i => Vector3.Distance(i.Center(), me.Center)).FirstOrDefault();
            markedName = markedItem != null ? markedItem.GetItemName() : "";
            if (markedItem != before) // a new mark (or none) - not every 10s ping
                Plugin.Log.LogInfo(markedItem != null
                ? $"Binoculars: {helpful.Count} helpful item(s), marked {markedItem.gameObject.name.Replace("(Clone)", "").Trim()} at {MetresTo(me, markedItem.Center()):0}m."
                : "Binoculars: nothing helpful out there.");
        }
        bool ok = markedItem != null && markedItem.itemState == ItemState.Ground && markedItem.gameObject.activeInHierarchy;
        Show(me, Marker.FindMark, ok ? markedItem!.Center() : null, markedName, Curio.Binoculars, new Color(0.6f, 0.95f, 0.55f));
    }

    private static void Show(Character me, int slot, Vector3? at, string name, Curio kind, Color colour)
    {
        if (at == null || ChoiceScreen.IsOpen || !InRange(me, at.Value, kind)) { Marker.Hide(slot); return; }
        Marker.Show(slot, at.Value + Vector3.up * 1.2f * M, $"{name} {MetresTo(me, at.Value):0}m", colour);
    }

    // --- crates: more of your relic's kind ------------------------------------------------------

    /// <summary>Whoever rolls a crate: each card has a chance to become each of their relics' kinds, from the same crate's pool.</summary>
    internal static void Boost(Luggage luggage, List<Item> cards)
    {
        if (!ModConfig.CurioRelics.Value) return;
        // Every crate-boosting relic you have gets its own chance on each card (own relic and badge copies alike).
        foreach (var (relic, kind) in new (Curio, System.Func<Item, bool>)[]
                 { (Curio.Bugle, Relics.IsHealing), (Curio.Binoculars, Relics.IsFood), (Curio.Compass, Relics.IsMovementItem) })
        {
            if (!Has(relic)) continue;
            for (int i = 0; i < cards.Count; i++)
            {
                if (kind(cards[i]) || Random.value >= ModConfig.CurioCrateBoost.Value) continue;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    var pick = luggage.GetObjectsToSpawn(1, true)?.Where(o => o != null).Select(o => o.GetComponent<Item>()).FirstOrDefault(x => x != null);
                    if (pick == null || !kind(pick) || (ModConfig.Leveling.Value && Items.IsEarnedOnly(pick)) || cards.Any(c => c.itemID == pick.itemID)) continue;
                    Plugin.Log.LogInfo($"{Name(relic)} relic: card {cards[i].gameObject.name} -> {pick.gameObject.name}.");
                    cards[i] = pick;
                    break;
                }
            }
        }
    }

    // --- Bugle: longer, stronger helping hand, lighter carries ----------------------------------

    internal static float ReachFor(CharacterGrabbing g) =>
        ModConfig.CurioRelics.Value && Holds(g.character, Curio.Bugle) ? 8f : 4f;

    /// <summary>Whose helping hand is pulling right now (set around CharacterGrabbing.Reach).</summary>
    private static Character? helping;
    private static float nextPullLog;

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterGrabbing), "Reach")]
    private static void ReachStart(CharacterGrabbing __instance) => helping = __instance.character;

    [HarmonyFinalizer, HarmonyPatch(typeof(CharacterGrabbing), "Reach")]
    private static void ReachEnd() => helping = null;

    /// <summary>A Bugle on either end of the helping hand pulls twice as hard.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Character), "DragTowards")]
    private static void StrongerPull(Character __instance, ref float force)
    {
        var helper = helping;
        if (helper == null || !ModConfig.CurioRelics.Value) return;
        bool helperHas = Holds(helper, Curio.Bugle), pulledHas = Holds(__instance, Curio.Bugle);
        if (!helperHas && !pulledHas) return;
        force *= 2f;
        if (Time.time < nextPullLog) return;
        nextPullLog = Time.time + 3f;
        float d = Vector3.Distance(helper.Center, __instance.Center);
        Plugin.Log.LogInfo($"Bugle pull: {helper.characterName} pulled {__instance.characterName} from {d / M:0.0}m ({d:0.0}u{(d > 4f ? ", past vanilla reach" : "")}), "
            + $"bugle on {(helperHas && pulledHas ? "both" : helperHas ? "helper" : "pulled scout")}.");
    }

    private static readonly AccessTools.FieldRef<CharacterAnimations, Character> AnimChar =
        AccessTools.FieldRefAccess<CharacterAnimations, Character>("character");

    internal static float HelpArmFor(CharacterAnimations a) =>
        ModConfig.CurioRelics.Value && Holds(AnimChar(a), Curio.Bugle) ? 7.5f : 3.5f;

    /// <summary>The help pose shows out to the Bugle's full reach (vanilla shows it within 3.5u).</summary>
    [HarmonyTranspiler, HarmonyPatch(typeof(CharacterAnimations), "Update")]
    private static IEnumerable<CodeInstruction> LongerHelpArm(IEnumerable<CodeInstruction> code)
    {
        bool done = false;
        foreach (var c in code)
        {
            if (!done && c.opcode == OpCodes.Ldc_R4 && c.operand is float f && f == 3.5f)
            {
                done = true;
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CurioRelics), nameof(HelpArmFor)));
                continue;
            }
            yield return c;
        }
        if (!done) Plugin.Log.LogWarning("Bugle relic: couldn't find the help pose's distance.");
    }

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
        if (!ModConfig.CurioRelics.Value || !Has(Curio.Frisbee) || item == null) return;
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
    /// <summary>Marker slots: the Compass's crate and the Binoculars' find can both show at once.</summary>
    internal const int CrateMark = 0, FindMark = 1;
    private static readonly Marker?[] instances = new Marker?[2];
    private RectTransform root = null!, box = null!;
    private TextMeshProUGUI label = null!;
    private Image dot = null!;
    private CanvasGroup group = null!;
    private float alpha, want;

    private static Marker Get(int slot)
    {
        if (instances[slot] is { } m) return m;
        var go = new GameObject($"PEAK Roguelike - Relic marker {slot}");
        DontDestroyOnLoad(go);
        m = go.AddComponent<Marker>();
        m.Build();
        return instances[slot] = m;
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
        label.outlineWidth = 0f; // an outline read as a thick see-through halo
        label.raycastTarget = false;
    }

    internal static void Hide()
    {
        foreach (var m in instances) if (m != null) m.want = 0f;
    }

    internal static void Hide(int slot)
    {
        if (instances[slot] is { } m) m.want = 0f;
    }

    internal static void Show(int slot, Vector3 world, string text, Color colour)
    {
        var m = Get(slot);
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
            if (behind) d.y = -Mathf.Abs(d.y) - 1f; // behind reads as "turn around": bottom edge, never the top
            float k = Mathf.Min((c.x - pad) / Mathf.Max(0.001f, Mathf.Abs(d.x)), (c.y - pad) / Mathf.Max(0.001f, Mathf.Abs(d.y)));
            // Behind us: always pin to the edge, or a target straight behind floats mid-screen as if ahead.
            p = c + d * (behind ? k : Mathf.Min(1f, k));
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
/// The Bing Bong relic's pranks on its carrier, every 2-4 minutes: a nap, a random level-up, an
/// item hidden for a while, Pandora's roll, a blink to flat ground nearby, lights out, or a gift
/// in your hands - and with other scouts around, swapping an item, places or every status bar
/// with one of them. Never while climbing, at a campfire or during a blood moon.
/// </summary>
internal static class BingBongPranks
{
    private const float M = 1f / 1.6f;

    private enum Prank { SwapItem, LevelUp, Nap, SwapPlace, SwapStatus, Hide, Pandora, Teleport, LightsOut, Gift }

    /// <summary>Bing Bong's Pandora roll is under way: the lunchbox's glass wipe and sharing stay out of it.</summary>
    internal static bool Rolling { get; private set; }

    private static bool Steady(Character c) =>
        !c.data.dead && !c.data.passedOut && c.data.fullyConscious && !c.data.isClimbingAnything && !c.data.isCarried && !BloodTornado.Carrying(c);

    internal static void Tick(Character me, ref float next)
    {
        if (!Steady(me) || Sanctuary.LocalSafe || BloodMoon.Active || ChoiceScreen.IsOpen) { next = Time.time + 5f; return; }
        var others = Squad.Alive().Where(o => o != me && Modless.HasMod(o) && Steady(o)).ToList();
        var options = new List<Prank> { Prank.Nap, Prank.Pandora, Prank.LightsOut };
        if (Offers.Carried().Count > 0) options.Add(Prank.LevelUp);
        if (hidden == null && Hideable().Any()) options.Add(Prank.Hide);
        if (BlinkSpot(me) != null) options.Add(Prank.Teleport);
        if (GiftPool().Count > 0) options.Add(Prank.Gift);
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

    /// <summary>F8: the solo pranks one at a time ("Nap", "LevelUp", "Hide", "Pandora", "Teleport", "LightsOut", "Gift").</summary>
    internal static readonly string[] DevNames = { "Nap", "LevelUp", "Hide", "Pandora", "Teleport", "LightsOut", "Gift" };

    internal static void DevPlay(string name)
    {
        if (Character.localCharacter is not { } me || !System.Enum.TryParse(name, out Prank p)) return;
        if (p == Prank.LevelUp && Offers.Carried().Count == 0) { Hud.Toast("Nothing to level up", ChoiceScreen.Muted); return; }
        if (p == Prank.Hide && (hidden != null || !Hideable().Any())) { Hud.Toast("Nothing to hide (or one is hidden)", ChoiceScreen.Muted); return; }
        if (p == Prank.Teleport && BlinkSpot(me) == null) { Hud.Toast("No flat ground nearby", ChoiceScreen.Muted); return; }
        if (p == Prank.Gift && GiftPool().Count == 0) { Hud.Toast("No crate pool here", ChoiceScreen.Muted); return; }
        Play(me, p, new List<Character>());
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
            case Prank.Hide:
                Hide(me);
                break;
            case Prank.Pandora:
                PandoraRoll(me);
                Hud.Banner("BING BONG", "Lunch time", ChoiceScreen.Cream);
                break;
            case Prank.Teleport:
                if (BlinkSpot(me) is { } spot)
                {
                    me.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot, true);
                    Hud.Banner("BING BONG", "Over here!", ChoiceScreen.Cream);
                    Plugin.Log.LogInfo($"Bing Bong: blinked {Vector3.Distance(me.Center, spot) / M:0}m to {spot}.");
                }
                break;
            case Prank.LightsOut:
                LightsOut.Begin(Mathf.Max(5f, ModConfig.BingBongDarkSeconds.Value));
                Hud.Banner("BING BONG", "Lights out", ChoiceScreen.Cream);
                break;
            case Prank.Gift:
                Gift(me);
                break;
        }
    }

    // --- hide and seek: one copy of a pocket item is gone for 30-60s --------------------------------

    private sealed class Hidden
    {
        public string Prefab = "";
        public int Level, Branch;
        public float Until;
    }

    private static Hidden? hidden;
    private static Vector3 lastAlive;

    /// <summary>Pocket items he may hide: anything but Bing Bong himself.</summary>
    private static IEnumerable<ItemSlot> Hideable() =>
        Pocketed(Player.localPlayer).Where(s => CurioRelics.KindOf(s.prefab) != Curio.BingBong);

    private static void Hide(Character me)
    {
        var p = Player.localPlayer;
        var slots = Hideable().ToList();
        if (p == null || slots.Count == 0) return;
        var s = slots[Random.Range(0, slots.Count)];
        var (level, branch) = ItemLevels.Current(s);
        string name = s.prefab.gameObject.name, shown = Items.DisplayName(s.prefab);
        if (!Stacks.TakeOne(p, s.itemSlotID))
        {
            var sel = me.refs.items.currentSelectedSlot;
            bool held = sel.IsSome && sel.Value == s.itemSlotID;
            p.EmptySlot(Optionable<byte>.Some(s.itemSlotID));
            if (held) me.refs.items.EquipSlot(Optionable<byte>.None);
        }
        float lo = Mathf.Max(1f, ModConfig.BingBongHideMin.Value), hi = Mathf.Max(lo, ModConfig.BingBongHideMax.Value);
        hidden = new Hidden { Prefab = name, Level = level, Branch = branch, Until = Time.time + Random.Range(lo, hi) };
        Hud.Banner("BING BONG", "Hide and seek", ChoiceScreen.Cream);
        Plugin.Log.LogInfo($"Bing Bong: hid {shown} (level {level}) for {hidden.Until - Time.time:0}s.");
    }

    /// <summary>
    /// Every frame (whoever holds Bing Bong or not): the hidden item comes back when its time is up -
    /// to your pockets, or where you fell if you died. Passed out, it waits until you're up.
    /// </summary>
    internal static void TickHidden(Character me)
    {
        if (hidden == null) return;
        if (!me.data.dead) lastAlive = me.Center;
        if (me.data.dead)
        {
            Net.SpawnItem(hidden.Prefab, lastAlive + Vector3.up * 0.5f);
            Plugin.Log.LogInfo($"Bing Bong: you died - {hidden.Prefab} dropped where you fell.");
            hidden = null;
            return;
        }
        if (Time.time < hidden.Until || !me.data.fullyConscious || me.data.passedOut) return;
        var item = Items.Database.FirstOrDefault(i => i != null && i.gameObject.name == hidden.Prefab);
        if (item != null)
        {
            if (hidden.Level > 0) ItemLevels.Expect(item, hidden.Level, hidden.Branch);
            GameUtils.instance.InstantiateAndGrab(item, me);
            Hud.Banner("BING BONG", "Found it!", ChoiceScreen.Cream);
            Plugin.Log.LogInfo($"Bing Bong: {hidden.Prefab} came back.");
        }
        hidden = null;
    }

    /// <summary>New run: a hidden item from the last one is gone for good.</summary>
    internal static void Reset() => hidden = null;

    // --- Pandora's roll: the lunchbox's own odds, glass left alone ----------------------------------

    private static void PandoraRoll(Character me)
    {
        // Pandora's Lunchbox's own numbers (its prefab's Affliction_Chaos): ailments ~N(0.4, 0.3), stamina ~N(0.3, 0.25).
        var box = Items.Database.FirstOrDefault(i => i != null && Items.IsLunchbox(i));
        var chaos = box?.GetComponentsInChildren<Action_ApplyAffliction>(true)
            .Select(a => a.affliction).OfType<Peak.Afflictions.Affliction_Chaos>().FirstOrDefault();
        var roll = new Peak.Afflictions.Affliction_Chaos(
            chaos?.statusAmountAverage ?? 0.4f, chaos?.statusAmountStandardDeviation ?? 0.3f,
            chaos?.averageBonusStamina ?? 0.3f, chaos?.standardDeviationBonusStamina ?? 0.25f);
        Rolling = true;
        try { me.refs.afflictions.AddAffliction(roll); }
        finally { Rolling = false; }
    }

    // --- teleport: flat, safe ground within 30m, above or below ---------------------------------------

    private static Vector3? BlinkSpot(Character me)
    {
        float r = Mathf.Max(5f, ModConfig.BingBongBlinkRange.Value) * M;
        Vector3 at = me.Center;
        for (int i = 0; i < 24; i++)
        {
            Vector2 d = Random.insideUnitCircle * r;
            if (d.magnitude < 5f * M) continue;
            float top = at.y + r + 5f * M;
            var from = new Vector3(at.x + d.x, top, at.z + d.y);
            if (!Physics.Raycast(from, Vector3.down, out var hit, r * 2f + 10f * M, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.866f || Mathf.Abs(hit.point.y - at.y) > r) continue;
            int layer = hit.collider.gameObject.layer;
            if (layer != LayerMask.NameToLayer("Terrain") && layer != LayerMask.NameToLayer("Map")) continue;
            string n = hit.collider.name.ToLowerInvariant();
            if (n.Contains("lava") || n.Contains("water") || n.Contains("ocean")) continue;
            Vector3 p = hit.point + Vector3.up * 1.2f;
            if (InFog(p)) continue;
            return p;
        }
        return null;
    }

    private static FogSphere? fog;

    private static bool InFog(Vector3 p)
    {
        if (fog == null) fog = Object.FindFirstObjectByType<FogSphere>();
        return fog != null && Mathf.Approximately(fog.ENABLE, 1f) && Vector3.Distance(fog.fogPoint, p) > fog.currentSize;
    }

    // --- a gift in your hands: this biome's crate pool, level 1 (1 in 10: max level) -----------------

    private static List<Item> GiftPool()
    {
        var me = Character.localCharacter;
        var crate = me == null ? null : Object.FindObjectsByType<Luggage>(FindObjectsSortMode.None)
            .Where(l => l != null && l is not RespawnChest)
            .OrderBy(l => Vector3.Distance(l.transform.position, me.Center)).FirstOrDefault();
        var pool = new List<Item>();
        if (crate == null) return pool;
        try
        {
            for (int i = 0; i < 12 && pool.Count < 6; i++)
            {
                var item = crate.GetObjectsToSpawn(1, true)?.Where(o => o != null).Select(o => o.GetComponent<Item>()).FirstOrDefault(x => x != null);
                if (item == null || pool.Contains(item) || Items.IsEarnedOnly(item) || Items.IsAmulet(item)
                    || Items.IsSkull(item) || Items.IsDagger(item) || Items.IsBookOfBones(item) || CurioRelics.KindOf(item) != Curio.None) continue;
                pool.Add(item);
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Bing Bong: no gift pool ({e.Message})."); }
        return pool;
    }

    private static void Gift(Character me)
    {
        var pool = GiftPool();
        if (pool.Count == 0) return;
        var item = pool[Random.Range(0, pool.Count)];
        bool max = Random.value < Mathf.Clamp01(ModConfig.BingBongGiftMaxChance.Value);
        // Whatever you hold drops at your feet first.
        var sel = me.refs.items.currentSelectedSlot;
        if (sel.IsSome && Player.localPlayer?.GetItemSlot(sel.Value) is { } held && !held.IsEmpty())
        {
            me.refs.items.EquipSlot(Optionable<byte>.None);
            me.refs.items.photonView.RPC("DropItemFromSlotRPC", RpcTarget.All, sel.Value, me.Center + me.data.lookDirection_Flat * 0.6f);
        }
        int level = 0, branch = 0;
        if (ItemLevels.KindOf(item) is { } k)
        {
            level = max ? k.Max : k.Min;
            branch = k.BothPaths ? Random.Range(1, 3) : k.Branch;
            ItemLevels.Expect(item, level, branch);
        }
        GameUtils.instance.InstantiateAndGrab(item, me);
        me.StartCoroutine(HoldWhenItArrives(me, item.itemID));
        if (max) Hud.Banner("BING BONG", "A GIFT", Trees.Gold);
        else Hud.Banner("BING BONG", "A gift", ChoiceScreen.Cream);
        Plugin.Log.LogInfo($"Bing Bong: gave {Items.DisplayName(item)}{(level > 0 ? $" at level {level}" : "")}{(max ? " (max)" : "")}.");
    }

    /// <summary>The gift goes straight into your hand once it's in your pockets.</summary>
    private static System.Collections.IEnumerator HoldWhenItArrives(Character me, ushort id)
    {
        var p = Player.localPlayer;
        var before = p == null ? new HashSet<byte>() : new HashSet<byte>(p.itemSlots.Where(s => s != null && !s.IsEmpty() && s.prefab?.itemID == id).Select(s => s.itemSlotID));
        for (float t = 0f; t < 5f; t += 0.1f)
        {
            yield return new WaitForSeconds(0.1f);
            if (me == null || p == null) yield break;
            var slot = p.itemSlots.FirstOrDefault(s => s != null && !s.IsEmpty() && s.prefab?.itemID == id && !before.Contains(s.itemSlotID))
                       ?? p.itemSlots.FirstOrDefault(s => s != null && !s.IsEmpty() && s.prefab?.itemID == id);
            if (slot == null) continue;
            if (t < 0.5f && before.Contains(slot.itemSlotID)) continue; // give a stack a moment to show the new copy
            me.refs.items.EquipSlot(Optionable<byte>.Some(slot.itemSlotID));
            yield break;
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

/// <summary>
/// Bing Bong's lights out: the world's own light goes out for you alone, real lights keep working.
/// The sun, moon, sky, fog and PEAK's ambient light (its light volume's _AmbienceStrength /
/// _AmbienceMin - found by reading True Final Ascent's darkness, thanks CptPICHU) fade to black;
/// campfires, lanterns, lava and flares light the world as they always do. No camera exposure, so
/// nothing real is dimmed. F shines a real flashlight from your eyes. Sun effects keep working:
/// they read the sun's direction and the time of day, and the sun light is never switched off.
/// </summary>
[HarmonyPatch]
internal class LightsOut : MonoBehaviour
{
    private static LightsOut? instance;
    private TextMeshProUGUI hint = null!;
    private Light? torch;
    private AudioSource? click;
    private float until;
    private bool on;

    /// <summary>How dark the world is right now (0 = normal, 1 = lights out).</summary>
    internal static float Dark { get; private set; }

    private static readonly int SkyTop = Shader.PropertyToID("SkyTopColor"),
        SkyMid = Shader.PropertyToID("SkyMidColor"), SkyBottom = Shader.PropertyToID("SkyBottomColor"),
        AmbStrength = Shader.PropertyToID("_AmbienceStrength"), AmbMin = Shader.PropertyToID("_AmbienceMin");

    internal static bool Active => instance != null;

    internal static void Begin(float seconds)
    {
        if (instance == null)
        {
            var go = new GameObject("PEAK Roguelike - Lights Out");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LightsOut>();
            instance.Build();
        }
        instance.until = Time.time + seconds;
        Plugin.Log.LogInfo($"Bing Bong: lights out for {seconds:0}s.");
    }

    internal static void End()
    {
        if (instance != null) instance.until = 0f;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -50; // under every HUD
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        var root = (RectTransform)transform;
        hint = Ui.Text(root, "Hint", "", UiArt.BodyFont, 20, ChoiceScreen.Muted, TextAlignmentOptions.Center);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, 150f);
        hint.rectTransform.sizeDelta = new Vector2(600f, 30f);
        hint.raycastTarget = false;
        Remember();
    }

    private void Update()
    {
        var me = Character.localCharacter;
        bool live = Time.time < until && me != null && !me.data.dead;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (live && kb != null && !ChoiceScreen.IsOpen && !DevPanel.Interactive && kb[ModConfig.FlashlightKey.Value].wasPressedThisFrame)
        {
            on = !on;
            Click(on);
        }
        Dark = Mathf.MoveTowards(Dark, live ? 1f : 0f, Time.deltaTime / (live ? 1f : 1.2f));
        hint.text = live ? $"{ModConfig.FlashlightKey.Value}  Flashlight" : "";
        hint.alpha = Dark * 0.55f;
        Torch(live && on);
        if (!live && Dark <= 0f) Destroy(gameObject);
    }

    // --- the world's light ---------------------------------------------------------------------
    // The day/night values the game sets every frame are dimmed right after it sets them (no restore
    // needed). The rest are set now and then, so they're remembered once and put back at the end.

    private float strengthWas, minWas, intensityWas, reflectWas;
    private UnityEngine.Rendering.AmbientMode modeWas;
    private Color fogWas, lightWas;

    private void Remember()
    {
        strengthWas = Shader.GetGlobalFloat(AmbStrength);
        minWas = Shader.GetGlobalFloat(AmbMin);
        modeWas = RenderSettings.ambientMode;
        intensityWas = RenderSettings.ambientIntensity;
        reflectWas = RenderSettings.reflectionIntensity;
        fogWas = RenderSettings.fogColor;
        lightWas = RenderSettings.ambientLight;
        var d = DayNightManager.instance;
        Plugin.Log.LogInfo($"Lights out: the game's light was ambience {strengthWas:0.###} (min {minWas:0.###}), "
            + $"ambient {modeWas} x{intensityWas:0.##}, reflections x{reflectWas:0.##}, fog {fogWas}, "
            + (d != null && d.sun != null ? $"sun {d.sun.intensity:0.###}, moon {(d.moon != null ? d.moon.intensity : 0f):0.###}." : "no day/night."));
    }

    private void LateUpdate()
    {
        if (Dark <= 0f) return;
        float k = 1f - Dark;
        Shader.SetGlobalFloat(AmbStrength, strengthWas * k);
        Shader.SetGlobalFloat(AmbMin, minWas * k);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientIntensity = intensityWas * k;
        RenderSettings.ambientLight = Dim(lightWas, k);
        RenderSettings.reflectionIntensity = reflectWas * k;
        RenderSettings.fogColor = Dim(fogWas, k);
        Sky(Dark >= 0.8f);
    }

    // The clouds live in the skybox (the game puts it back every frame): once it's nearly dark the
    // sky is swapped for plain black. Ambient is flat by then, so the lighting doesn't change.
    private Material? skyWas;
    private Camera? skyCam;
    private CameraClearFlags clearWas;
    private Color backWas;

    private void Sky(bool black)
    {
        if (!black)
        {
            if (skyCam != null) { skyCam.clearFlags = clearWas; skyCam.backgroundColor = backWas; skyCam = null; }
            if (skyWas != null && RenderSettings.skybox == null) RenderSettings.skybox = skyWas;
            return;
        }
        if (RenderSettings.skybox != null) skyWas = RenderSettings.skybox;
        RenderSettings.skybox = null;
        var cam = Meteors.Eye();
        if (cam != skyCam)
        {
            if (skyCam != null) { skyCam.clearFlags = clearWas; skyCam.backgroundColor = backWas; }
            skyCam = cam;
            if (cam != null) { clearWas = cam.clearFlags; backWas = cam.backgroundColor; }
        }
        if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; }
    }

    /// <summary>Sun, moon, flare and sky, after the game (and our suns and moons) set them this frame.</summary>
    [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(DayNightManager), nameof(DayNightManager.UpdateCycle))]
    private static void DimCycle(DayNightManager __instance)
    {
        if (Dark <= 0f) return;
        float k = 1f - Dark;
        var d = __instance;
        // Brightness only: the sun stays enabled so the sun checks that look at it keep working.
        if (d.sun != null) { d.sun.intensity *= k; d.sun.color = Dim(d.sun.color, k); }
        if (d.moon != null) { d.moon.intensity *= k; d.moon.color = Dim(d.moon.color, k); }
        if (d.lensFlare != null) d.lensFlare.intensity *= k;
        Shader.SetGlobalColor(SkyTop, Dim(Shader.GetGlobalColor(SkyTop), k));
        Shader.SetGlobalColor(SkyMid, Dim(Shader.GetGlobalColor(SkyMid), k));
        Shader.SetGlobalColor(SkyBottom, Dim(Shader.GetGlobalColor(SkyBottom), k));
    }

    private static Color Dim(Color c, float k) => new(c.r * k, c.g * k, c.b * k, c.a);

    private void Restore()
    {
        Sky(false);
        Shader.SetGlobalFloat(AmbStrength, strengthWas);
        Shader.SetGlobalFloat(AmbMin, minWas);
        RenderSettings.ambientMode = modeWas;
        RenderSettings.ambientIntensity = intensityWas;
        RenderSettings.reflectionIntensity = reflectWas;
        RenderSettings.fogColor = fogWas;
        RenderSettings.ambientLight = lightWas;
    }

    // --- the flashlight --------------------------------------------------------------------------

    /// <summary>A real light from your eyes: soft edge, rocks cast shadows, your own head doesn't.</summary>
    private void Torch(bool want)
    {
        if (!want) { if (torch != null) torch.enabled = false; return; }
        var cam = Meteors.Eye();
        if (cam == null) return;
        if (torch == null || torch.transform.parent != cam.transform)
        {
            if (torch != null) Destroy(torch.gameObject);
            var go = new GameObject("Bing Bong Flashlight");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.05f, 0.3f);
            torch = go.AddComponent<Light>();
            torch.type = LightType.Spot;
            torch.spotAngle = 55f;
            torch.innerSpotAngle = 24f;
            torch.range = 45f;
            torch.intensity = 10f;
            torch.color = new Color(1f, 0.95f, 0.85f);
            torch.shadows = LightShadows.Soft;
            torch.shadowNearPlane = 0.5f;
        }
        torch.enabled = true;
    }

    private static AudioClip? clickClip;
    private static bool clickLooked;

    /// <summary>The game's own UI click: higher switching on, lower switching off.</summary>
    private void Click(bool turnedOn)
    {
        if (!clickLooked)
        {
            clickLooked = true;
            clickClip = Resources.FindObjectsOfTypeAll<AudioClip>().Where(c => c != null && c.name.ToLowerInvariant().Contains("click"))
                .OrderBy(c => c.length).FirstOrDefault();
            Plugin.Log.LogInfo(clickClip != null ? $"Lights out: flashlight click is '{clickClip.name}'." : "Lights out: no click sound found.");
        }
        if (clickClip == null) return;
        if (click == null) click = gameObject.AddComponent<AudioSource>();
        click.spatialBlend = 0f;
        click.pitch = turnedOn ? 1.15f : 0.9f;
        click.PlayOneShot(clickClip, 0.5f);
    }

    private void OnDestroy()
    {
        Dark = 0f;
        Restore();
        if (torch != null) Destroy(torch.gameObject);
        if (instance == this) instance = null;
    }
}
