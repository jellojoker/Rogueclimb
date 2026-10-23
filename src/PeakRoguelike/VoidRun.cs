using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>
/// The hidden ending. At the Scout's Honor statue a scout gives back a relic (its amulet goes into
/// the statue the game's own way, so four of them make the Scout's Honor). In the Void the statue
/// amulets' relics are gone (the starting relic stays), its crates hold fusions only, and the
/// four suns take turns at Shore strength, 30s each, with a 30s tier 1 blood moon (one hazard,
/// each in turn) after every sun.
/// </summary>
[HarmonyPatch]
internal class VoidRun : MonoBehaviour
{

    private static bool inVoid;
    private static float since;
    private static readonly List<RelicGem> order = new();

    internal static bool Active => inVoid;
    /// <summary>Where this scout arrived in the Void (the blood moon tornado rises there).</summary>
    internal static Vector3? Arrival { get; private set; }

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Void");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<VoidRun>();
    }

    private void Update()
    {
        bool now = false;
        try { now = MapHandler.Exists && VoidBiome.VoidBiomeActive; }
        catch { /* no map yet */ }
        if (now == inVoid)
        {
            if (inVoid && Arrival == null && Time.time - since > 3f && Character.localCharacter is { } me && !me.data.dead)
            {
                Arrival = me.Center;
                Plugin.Log.LogInfo($"Void: arrived at {me.Center}.");
            }
            TickMoons();
            return;
        }
        inVoid = now;
        if (now) Enter();
        else Plugin.Log.LogInfo("Void: left.");
    }

    private static void Enter()
    {
        since = Time.time;
        order.Clear();
        order.AddRange(new[] { RelicGem.Tenacity, RelicGem.Initiative, RelicGem.Ambition, RelicGem.Generosity }.OrderBy(_ => Random.value));
        moonOrder.Clear();
        moonOrder.AddRange(BloodMoon.DefaultOrder.OrderBy(_ => Random.value));
        moonTurn = -1;
        Arrival = null; // taken a few seconds in, once the trip there has landed us
        var gone = RunState.Relics.Keys.ToList();
        foreach (var gem in gone) RunState.RemoveRelic(gem);
        Plugin.Log.LogInfo($"Void: entered - amulet relics wiped ({(gone.Count > 0 ? string.Join(", ", gone) : "none")}; the starting relic stays); "
            + $"sun order {string.Join(" > ", order.Select(Suns.ColorName))}{(Moons ? $", moons {string.Join(" > ", moonOrder)} ({SunFor:0}s sun, {MoonFor:0}s moon)" : "")} (only the host's order is used).");
        Hud.Banner("THE VOID", "Your amulets are gone.", ChoiceScreen.Muted);
    }

    // --- the clock: a sun, then (moons on) a tier 1 blood moon, then the next sun ... --------------

    private static bool Moons => ModConfig.VoidMoons.Value && ModConfig.BloodMoon.Value;
    private static float SunFor => Mathf.Max(5f, ModConfig.VoidSunSeconds.Value);
    private static float MoonFor => Moons ? Mathf.Max(5f, ModConfig.VoidMoonSeconds.Value) : 0f;
    private static float Turn => SunFor + MoonFor;
    private static int TurnNow => Mathf.FloorToInt((Time.time - since) / Turn);
    private static bool InMoon => Moons && (Time.time - since) - TurnNow * Turn >= SunFor;

    private static T Pick<T>(List<T> list, int turn) => list[((turn % list.Count) + list.Count) % list.Count];

    /// <summary>The host's sun for the Void right now: each colour in this run's order, none while a moon is up.</summary>
    internal static RelicGem? Sun()
    {
        if (!inVoid || order.Count == 0 || InMoon) return null;
        return Pick(order, TurnNow);
    }

    /// <summary>The moons' hazards in this Void's order: each one once before any repeats.</summary>
    private static readonly List<BloodMoon.Hazard> moonOrder = new();
    private static int moonTurn = -1;

    /// <summary>Host: each turn's moon rises once, when its sun is done.</summary>
    private void TickMoons()
    {
        if (!inVoid || !InMoon || moonOrder.Count == 0 || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        int turn = TurnNow;
        if (turn == moonTurn) return;
        moonTurn = turn;
        float left = Turn * (turn + 1) - (Time.time - since);
        BloodMoon.VoidMoon(Pick(moonOrder, turn), Mathf.Max(5f, left));
    }

    /// <summary>F8: skip to the next sun (or the next moon, from a sun).</summary>
    internal static void DevNextSun() => DevSkip(moon: false);

    /// <summary>F8: skip to the next moon.</summary>
    internal static void DevNextMoon() => DevSkip(moon: true);

    private static void DevSkip(bool moon)
    {
        if (!inVoid) { Hud.Toast("Not in the Void", ChoiceScreen.Muted); return; }
        if (moon && !Moons) { Hud.Toast("Void moons are off", ChoiceScreen.Muted); return; }
        float into = (Time.time - since) - TurnNow * Turn;
        // The next start: the moon of this turn (if its sun is still up) or the next turn's sun / moon.
        float to = moon ? (into < SunFor ? SunFor : Turn + SunFor) : Turn;
        if (!moon && InMoon) BloodMoon.DevStart(0);
        since -= to - into;
        Hud.Toast(moon ? "Void: next moon" : $"Void sun: {(Sun() is { } g ? Suns.ColorName(g) : "none")}", ChoiceScreen.Muted);
    }

    /// <summary>F8: a Scout's Honor in your hands - use it to go to the Void the game's own way.</summary>
    internal static void DevGiveHonor()
    {
        var me = Character.localCharacter;
        var honor = Items.Database.FirstOrDefault(i => i != null && i.GetComponent<ScoutsHonor>() != null);
        if (me == null || honor == null) { Hud.Toast("No Scout's Honor found", ChoiceScreen.Muted); return; }
        GameUtils.instance.InstantiateAndGrab(honor, me);
    }

    /// <summary>F8: fill every slot of the nearest Scout's Honor statue (the game makes the Scout's Honor).</summary>
    internal static void DevFillStatue()
    {
        var me = Character.localCharacter;
        var statue = me == null ? null : FindObjectsByType<ScoutStatue>(FindObjectsSortMode.None)
            .OrderBy(s => Vector3.Distance(s.transform.position, me.Center)).FirstOrDefault();
        var view = statue != null ? statue.GetComponent<PhotonView>() : null;
        if (me == null || statue == null || view == null) { Hud.Toast("No Scout's Honor statue here", ChoiceScreen.Muted); return; }
        for (int i = 0; i < 4; i++) view.RPC("RPC_InsertAmulet", RpcTarget.MasterClient, i);
        Plugin.Log.LogInfo($"Statue: F8 filled all slots ({Vector3.Distance(statue.transform.position, me.Center):0}u away).");
        Hud.Toast("Statue filled", ChoiceScreen.Cream);
    }

    private static readonly AccessTools.FieldRef<ScoutStatue, int[]> StatueSlots = AccessTools.FieldRefAccess<ScoutStatue, int[]>("hasAmulets");

    [HarmonyPrefix, HarmonyPatch(typeof(ScoutStatue), "RPC_InsertAmulet")]
    private static void LogInsert(int amuletType)
    {
        if (amuletType == -1) Plugin.Log.LogInfo("Statue: all four in - the Scout's Honor forms.");
        else if (amuletType == -2) Plugin.Log.LogInfo("Statue: Scout's Honor made.");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ScoutStatue), "RPC_InsertAmulet")]
    private static void LogFilled(ScoutStatue __instance, int amuletType)
    {
        if (amuletType < 0 || amuletType >= 4 || !PhotonNetwork.IsMasterClient) return;
        var slots = StatueSlots(__instance);
        Plugin.Log.LogInfo($"Statue: host got amulet {amuletType} - {slots.Count(x => x != -1)}/4 filled [{string.Join(",", slots)}].");
    }

    // --- the statue: give a relic back -------------------------------------------------------------

    /// <summary>The statue's slot for a relic's amulet (-1 = none).</summary>
    private static int AmuletIndex(RelicGem gem)
    {
        var amulet = Relics.Amulet(gem);
        var b = amulet != null ? amulet.GetComponent<AmuletBase>() : null;
        return b != null && b.amuletIndex >= 0 && b.amuletIndex < 4 ? b.amuletIndex : -1;
    }

    /// <summary>
    /// The relic you'd give back: your own or a cursed copy, never one only shared with you (the
    /// statue turns a second copy of a colour into a missing one).
    /// </summary>
    private static RelicGem? Returnable() =>
        ModConfig.Relics.Value
            ? RunState.Relics.Keys.Where(g => !RunState.IsShared(g) && AmuletIndex(g) >= 0).OrderBy(g => (int)g).Cast<RelicGem?>().FirstOrDefault()
            : null;

    private static bool HoldingAmulet(Character c) =>
        c.data.currentItem != null && c.data.currentItem.itemTags.HasFlag(Item.ItemTags.ScoutAmulet);

    [HarmonyPostfix, HarmonyPatch(typeof(ScoutStatue), nameof(ScoutStatue.IsConstantlyInteractable))]
    private static void CanReturn(Character interactor, ref bool __result)
    {
        if (!__result && interactor != null && interactor.IsLocal && Returnable() != null) __result = true;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ScoutStatue), nameof(ScoutStatue.GetInteractionText))]
    private static void ReturnText(ref string __result)
    {
        var me = Character.localCharacter;
        if (me == null || HoldingAmulet(me) || Returnable() is not { } gem) return;
        __result = $"Return {Relics.GemName(gem)}";
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ScoutStatue), nameof(ScoutStatue.Interact_CastFinished))]
    private static bool Return(ScoutStatue __instance, Character interactor)
    {
        if (interactor != null && interactor.IsLocal && HoldingAmulet(interactor))
        {
            var held = interactor.data.currentItem;
            var b = held.GetComponent<AmuletBase>();
            Plugin.Log.LogInfo($"Statue: {interactor.characterName} puts in {held.gameObject.name.Replace("(Clone)", "").Trim()} "
                + $"(amulet {(b != null ? b.amuletIndex : -99)}, x{Stacks.Count(held.data)}).");
            return true;
        }
        if (interactor == null || !interactor.IsLocal) return true;
        if (Returnable() is not { } gem) return true;
        var view = __instance.GetComponent<PhotonView>();
        if (view == null) return true;
        int index = AmuletIndex(gem);
        view.RPC("RPC_InsertAmulet", RpcTarget.MasterClient, index);
        RunState.RemoveRelic(gem);
        Hud.Banner("RELIC RETURNED", Relics.GemName(gem), ChoiceScreen.Muted);
        Plugin.Log.LogInfo($"Statue: returned {Relics.GemName(gem)} (amulet slot {index}).");
        return false;
    }
}
