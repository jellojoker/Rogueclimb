using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak;
using Peak.Afflictions;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Safe ground: inside a campfire's circle, lit or not (the one the game uses for "everyone is resting")
/// or up on the summit, the suns and blood moons can't touch you. The sky stays coloured and the
/// moon stays red - only the danger and the music stop.
/// </summary>
internal static class Sanctuary
{
    private static readonly List<Campfire> fires = new();
    private static float nextScan;
    private static Transform? summit;

    /// <summary>The local scout is on safe ground right now (refreshed by <see cref="Tick"/>).</summary>
    internal static bool LocalSafe { get; private set; }
    /// <summary>Today's sun can't touch the local scout: on safe ground, or not on their feet yet.</summary>
    internal static bool SunSpared { get; private set; }
    private static bool loggedSafe;

    internal static void Tick()
    {
        var me = Character.localCharacter;
        bool safe = me != null && !me.data.dead && Safe(me.Center);
        // The suns also leave you alone while you're still lying down (waking up at the crash, being revived).
        // Sunscreen keeps every sun off you while it lasts.
        SunSpared = safe || (me != null && ((ModConfig.SafeZones.Value && !me.data.fullyConscious) || me.data.wearingSunscreen));
        if (safe != loggedSafe)
        {
            loggedSafe = safe;
            Plugin.Log.LogInfo(safe ? "Sanctuary: on safe ground (campfire / summit)." : "Sanctuary: left safe ground.");
        }
        LocalSafe = safe;
    }

    internal static bool Safe(Character c) => c != null && Safe(c.Center);

    internal static bool Safe(Vector3 pos)
    {
        if (!ModConfig.SafeZones.Value || !MapHandler.Exists) return false;
        Rescan();
        foreach (var f in fires)
            if (f != null && Vector3.Distance(f.transform.position, pos) <= Radius(f)) return true;
        if (crash != null && Vector3.Distance(crash.position, pos) <= ModConfig.SpawnRadius.Value * Squad.M) return true;
        return summit != null && Vector3.Distance(summit.position, pos) <= ModConfig.SummitRadius.Value * Squad.M;
    }

    /// <summary>The nearest lit safe spot and its radius (for pushing the tornado back out).</summary>
    internal static bool Nearest(Vector3 pos, out Vector3 centre, out float radius)
    {
        centre = default;
        radius = 0f;
        if (!ModConfig.SafeZones.Value || !MapHandler.Exists) return false;
        Rescan();
        float best = float.MaxValue;
        foreach (var f in fires)
        {
            if (f == null) continue;
            float d = Vector3.Distance(f.transform.position, pos) - Radius(f);
            if (d < best) { best = d; centre = f.transform.position; radius = Radius(f); }
        }
        foreach (var (spot, r) in new[] { (summit, ModConfig.SummitRadius.Value * Squad.M), (crash, ModConfig.SpawnRadius.Value * Squad.M) })
        {
            if (spot == null) continue;
            float d = Vector3.Distance(spot.position, pos) - r;
            if (d < best) { best = d; centre = spot.position; radius = r; }
        }
        return best < float.MaxValue;
    }

    private static float Radius(Campfire f) => f.moraleBoostRadius > 1f ? f.moraleBoostRadius : 15f * Squad.M;

    private static void Rescan()
    {
        if (Time.time < nextScan) return;
        nextScan = Time.time + 3f;
        fires.Clear();
        fires.AddRange(Object.FindObjectsByType<Campfire>(FindObjectsSortMode.None));
        var peak = Object.FindObjectsByType<PeakSequence>(FindObjectsSortMode.None).FirstOrDefault();
        summit = peak != null ? peak.transform : null;
        // The plane crash on the beach, where every run starts (there's no campfire there).
        var map = Zorro.Core.Singleton<MapHandler>.Instance;
        crash = map != null && map.segments.Length > 0 ? map.segments[0].reconnectSpawnPos : null;
    }

    private static Transform? crash;
}

/// <summary>
/// Each sun cuts the thing its relic gives you to 10%: blue = healing items, yellow = infinite
/// stamina, green = food, red = movement items. The lantern, ropes and pitons never; a relic
/// always beats its own sun.
/// </summary>
internal static class SunCaps
{
    private static readonly List<Item> inUse = new();
    /// <summary>A relic is firing: nothing it does is capped.</summary>
    internal static int RelicFiring;

    internal static Item? Using => inUse.Count > 0 ? inUse[inUse.Count - 1] : null;

    internal static void Push(Item item) => inUse.Add(item);
    internal static void Pop() { if (inUse.Count > 0) inUse.RemoveAt(inUse.Count - 1); }

    /// <summary>Every item action's RunAction, so the caps know which item is doing the healing / feeding.</summary>
    internal static void Install(Harmony h)
    {
        var begin = new HarmonyMethod(typeof(SunCaps), nameof(Begin));
        var end = new HarmonyMethod(typeof(SunCaps), nameof(End));
        int n = 0;
        foreach (var t in AccessTools.AllTypes())
        {
            if (t == null || t.IsAbstract || !typeof(ItemActionBase).IsAssignableFrom(t) || t == typeof(ItemActionBase)) continue;
            var m = AccessTools.DeclaredMethod(t, nameof(ItemActionBase.RunAction), System.Type.EmptyTypes);
            if (m == null || m.IsAbstract) continue;
            try { h.Patch(m, prefix: begin, finalizer: end); n++; }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Sun caps: couldn't watch {t.Name}: {e.Message}"); }
        }
        Plugin.Log.LogInfo($"Sun caps: watching {n} item actions.");
    }

    private static void Begin(ItemActionBase __instance, out bool __state)
    {
        __state = false;
        var item = __instance != null ? __instance.GetComponent<Item>() : null;
        if (item == null || item.holderCharacter == null || !item.holderCharacter.IsLocal) return;
        Push(item);
        __state = true;
    }

    private static System.Exception? End(System.Exception? __exception, bool __state)
    {
        if (__state) Pop();
        return __exception;
    }

    private static float Cap(BepInEx.Configuration.ConfigEntry<float> e) => Mathf.Clamp01(e.Value);

    private static bool Live(Character? c) => RelicFiring == 0 && c != null && c.IsLocal && Using is { } item && !Relics.IsLantern(item);

    /// <summary>The cap on what the item in use does for you (1 = uncapped).</summary>
    internal static float ItemCap(Character? c)
    {
        if (!Live(c)) return 1f;
        var item = Using!;
        if (Suns.Blue && Relics.IsHealing(item)) return Cap(ModConfig.SunBlueHealCap);
        return 1f;
    }

    internal static float RushCap(Character? c) =>
        Live(c) && Suns.Yellow ? Cap(ModConfig.SunYellowRushCap) : 1f;

    internal static float MoveCap => Suns.Red ? Mathf.Clamp01(ModConfig.SunRedMoveCap.Value) : 1f;
}

[HarmonyPatch]
internal static class SunCapPatches
{
    // --- blue healing: what an item takes away from you -----------------------------------------

    /// <summary>10% of what it would really have removed (a cure-all "removes 5" of everything).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.SubtractStatus))]
    private static void CapRelief(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount, bool decreasedNaturally)
    {
        if (decreasedNaturally || amount <= 0f || Suns.Sharing) return;
        float cap = SunCaps.ItemCap(__instance.character);
        if (cap >= 1f) return;
        amount = Mathf.Min(amount, __instance.GetCurrentStatus(statusType)) * cap;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Character), nameof(Character.AddExtraStamina))]
    private static void CapExtraStamina(Character __instance, ref float add)
    {
        if (add > 0f) add *= SunCaps.ItemCap(__instance);
    }

    /// <summary>Buffs from the item: healing over time, rushes, speed. The game copies what it's given, so we hand it a scaled copy.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddAffliction))]
    private static void CapBuffs(CharacterAfflictions __instance, ref Affliction affliction, bool fromRPC)
    {
        if (fromRPC || affliction == null) return;
        var c = __instance.character;
        float item = Good(affliction) ? SunCaps.ItemCap(c) : 1f;
        float rush = affliction is Affliction_InfiniteStamina ? SunCaps.RushCap(c) : 1f;
        float k = Mathf.Min(item, rush);
        if (k >= 1f) return;
        var copy = Relics.Copy(affliction);
        if (copy is Affliction_HealAll heal) heal.maxHealing *= k;
        else if (copy.totalTime > 0f) copy.totalTime *= k;
        affliction = copy;
    }

    private static bool Good(Affliction a) => a switch
    {
        Affliction_HealAll => true,
        Affliction_AdjustStatusOverTime o => o.statusPerSecond < 0f,
        Affliction_InfiniteStamina or Affliction_FasterBoi or Affliction_Invincibility => true,
        _ => false,
    };

    // --- red: movement items ------------------------------------------------------------------------

    /// <summary>The glider barely catches the air: 10% of its drag and push.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.ApplyGlider))]
    private static void WeakGlider(CharacterMovement __instance, ref float extraDrag, ref float extraXZDrag, ref float soarForwardForce)
    {
        float k = SunCaps.MoveCap;
        if (k >= 1f || !Mine(__instance)) return;
        extraDrag = 1f - (1f - extraDrag) * k;
        extraXZDrag = 1f - (1f - extraXZDrag) * k;
        soarForwardForce *= k;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ScoutCannon), nameof(ScoutCannon.RPCA_LaunchTarget))]
    private static void WeakCannon(ScoutCannon __instance, int targetID, out float __state)
    {
        __state = __instance.launchForce;
        var view = Photon.Pun.PhotonNetwork.GetPhotonView(targetID);
        if (view != null && view.GetComponent<Character>() is { IsLocal: true }) __instance.launchForce *= SunCaps.MoveCap;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ScoutCannon), nameof(ScoutCannon.RPCA_LaunchTarget))]
    private static void WeakCannonDone(ScoutCannon __instance, float __state) => __instance.launchForce = __state;

    [HarmonyPrefix, HarmonyPatch(typeof(Action_LaunchPlayer), nameof(Action_LaunchPlayer.RunAction))]
    private static void WeakLaunch(Action_LaunchPlayer __instance, out float __state)
    {
        __state = __instance.force;
        __instance.force *= SunCaps.MoveCap;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Action_LaunchPlayer), nameof(Action_LaunchPlayer.RunAction))]
    private static void WeakLaunchDone(Action_LaunchPlayer __instance, float __state) => __instance.force = __state;

    private static readonly HashSet<string> loggedBlasts = new();

    /// <summary>Anti-grav blasts (the Antizooka's shot): 10% of the shove and the float.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static void WeakBlast(AOE __instance, out (float, float, Affliction?)? __state)
    {
        __state = null;
        float k = SunCaps.MoveCap;
        if (k >= 1f) return;
        bool antigrav = (__instance.hasAffliction && __instance.affliction is Affliction_LowGravity)
            || __instance.name.IndexOf("zooka", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (!antigrav) return;
        if (loggedBlasts.Add(__instance.name)) Plugin.Log.LogInfo($"Red sun: weakened anti-grav blast '{__instance.name}'.");
        __state = (__instance.knockback, __instance.additionalVerticalKnockback, __instance.affliction);
        __instance.knockback *= k;
        __instance.additionalVerticalKnockback *= k;
        if (__instance.affliction is Affliction_LowGravity low)
        {
            var copy = Relics.Copy(low);
            copy.totalTime *= k;
            __instance.affliction = copy;
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static void WeakBlastDone(AOE __instance, (float, float, Affliction?)? __state)
    {
        if (__state is not { } s) return;
        (__instance.knockback, __instance.additionalVerticalKnockback, __instance.affliction) = s;
    }

    private static readonly AccessTools.FieldRef<CharacterMovement, Character> MoverOf = AccessTools.FieldRefAccess<CharacterMovement, Character>("character");
    private static bool Mine(CharacterMovement m) => MoverOf(m) is { IsLocal: true };
}

/// <summary>Red: the jetpack and rocket pack sputter (their thrust is read every physics frame).</summary>
internal class MovementCaps : MonoBehaviour
{
    private CharacterMovement? mover;
    private float jet, rocket;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Movement Caps");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<MovementCaps>();
    }

    private void Update()
    {
        Sanctuary.Tick();
        var me = Character.localCharacter;
        var m = me != null ? me.refs.movement : null;
        if (m != mover)
        {
            mover = m;
            if (m != null) { jet = m.jetpackForce; rocket = m.rocketForce; }
        }
        if (m == null) return;
        float k = SunCaps.MoveCap;
        m.jetpackForce = jet * k;
        m.rocketForce = rocket * k;
    }
}
