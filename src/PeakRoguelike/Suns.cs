using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>
/// Coloured suns. Each biome's amulet statue decides its sun (the colour of that run's amulet).
/// Until the squad holds that biome's relic, its days burn that colour and its nights are calm;
/// once they hold it, the days clear and the nights become blood moons. From the Kiln on, the sun
/// cycles day by day through the colours of the relics the squad carries (none carried, no sun).
///
///   yellow (Ambition)   stamina drains faster, comes back slower
///   green  (Generosity) food barely fills you, hunger climbs faster
///   red    (Initiative) heavy: low jumps, hard landings
///   blue   (Tenacity)   every ailment hits double, items wear twice as fast, ropes and pitons give
///
/// The host decides the sun (room property prl.sun = gem, or -1); every client applies the effects
/// to its own scout. The sky is coloured through the game's own "special day" sky blend.
/// Announced with one line - what it does is for the scouts to find out.
/// </summary>
internal class Suns : MonoBehaviour
{
    private const string RoomKey = "prl.sun";
    private static Suns? instance;

    /// <summary>Today's sun (everyone, from the room property).</summary>
    internal static RelicGem? Current { get; private set; }
    internal static float Blend { get; private set; }

    // What today's sun does to the local scout: nothing while they're on safe ground. (A relic's
    // own effect is never cut by its sun - see SunCaps.RelicFiring.)
    internal static bool Yellow => Hits(RelicGem.Ambition);
    internal static bool Green => Hits(RelicGem.Generosity);
    internal static bool Red => Hits(RelicGem.Initiative);
    internal static bool Blue => Hits(RelicGem.Tenacity);
    private static bool On => ModConfig.Suns.Value;

    // A relic beats its own sun: carrying it, that sun can't touch you.
    private static bool Hits(RelicGem gem) => On && Current == gem && !Sanctuary.SunSpared && !(ModConfig.Relics.Value && RunState.Relics.ContainsKey(gem));

    /// <summary>The sun the sky is showing: stays on after the sun clears, while the colour fades out.</summary>
    internal static RelicGem? Shown { get; private set; }

    private float nextPoll;
    private int seen = -2;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Suns");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<Suns>();
    }

    private static float Fade => Mathf.Max(0.1f, ModConfig.SunFadeSeconds.Value);

    private static bool InRun =>
        DayNightManager.instance != null && GameUtils.instance != null && !GameUtils.instance.m_inAirport && MapHandler.Exists;

    private static bool IsDay => InRun && DayNightManager.instance.isDay >= 0.5f;

    private void Update()
    {
        // The colour comes and goes over SunFadeSeconds (the effects themselves switch at once).
        if (Current != null && Current != Shown && Blend > 0f)
            Blend = Mathf.MoveTowards(Blend, 0f, Time.deltaTime / Fade); // another colour: fade the old one out first
        else
        {
            if (Current != null) Shown = Current;
            Blend = Mathf.MoveTowards(Blend, Current != null && IsDay ? 1f : 0f, Time.deltaTime / Fade);
        }
        if (Blend <= 0f && Current == null) Shown = null;
        try { Wash(); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Suns wash: {e.Message}"); }
        try { ColdLight(); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Suns cold light: {e.Message}"); }
        if (Time.time < nextPoll) return;
        nextPoll = Time.time + 0.5f;
        try
        {
            ReadRoom();
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient) HostTick();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Suns: {e.Message}");
        }
    }

    // --- everyone -----------------------------------------------------------------------------

    private void ReadRoom()
    {
        int v = -1;
        if (InRun && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKey, out object raw) && raw is int i)
            v = i;
        Current = v >= 0 ? (RelicGem)v : null;
        if (v == seen) return;
        seen = v;
        if (Current is { } gem)
        {
            Plugin.Log.LogInfo($"Sun: {ColorName(gem)}.");
            Hud.Banner("", $"THE SUN BURNS {ColorName(gem).ToUpperInvariant()}", Relics.GemColor(gem));
        }
        else Plugin.Log.LogInfo("Sun: normal.");
    }

    internal static string ColorName(RelicGem g) => g switch
    {
        RelicGem.Ambition => "Yellow",
        RelicGem.Generosity => "Green",
        RelicGem.Initiative => "Red",
        _ => "Blue",
    };

    // --- host -----------------------------------------------------------------------------------

    /// <summary>Dev panel: force a sun (-1 = none) or hand control back (null).</summary>
    internal static int? DevForce;

    private void HostTick()
    {
        int want = -1;
        if (DevForce is { } forced) want = forced;
        else if (On && IsDay && !BloodMoon.Active && TodaysSun() is { } gem) want = (int)gem;
        int now = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKey, out object raw) && raw is int i ? i : -1;
        if (now != want)
            PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [RoomKey] = want });
        FoodRain();
    }

    // --- green: it rains the biome's wild food (host) ------------------------------------------------

    private float nextRain;
    private readonly List<(Item item, float at)> rained = new();

    private const SpawnPool Wild = SpawnPool.BerryBushBeach | SpawnPool.BerryBushJungle | SpawnPool.SpikyVine | SpawnPool.CoconutTree
        | SpawnPool.WillowTreeJungle | SpawnPool.JungleVine | SpawnPool.WinterberryTree | SpawnPool.Nest | SpawnPool.Cactus | SpawnPool.Redwood;

    /// <summary>The trees and bushes that grow in a biome (mushroom clusters left out - they're a gamble).</summary>
    private static SpawnPool WildIn(Biome.BiomeType b) => b switch
    {
        Biome.BiomeType.Shore => SpawnPool.BerryBushBeach | SpawnPool.CoconutTree,
        Biome.BiomeType.Tropics => SpawnPool.BerryBushJungle | SpawnPool.JungleVine | SpawnPool.SpikyVine | SpawnPool.WillowTreeJungle | SpawnPool.CoconutTree,
        Biome.BiomeType.Roots => SpawnPool.Redwood | SpawnPool.Nest,
        Biome.BiomeType.Alpine => SpawnPool.WinterberryTree | SpawnPool.Nest,
        Biome.BiomeType.Mesa => SpawnPool.Cactus | SpawnPool.Nest,
        _ => Wild,
    };

    private static List<Item> WildFood(SpawnPool pools) => Items.Database
        .Where(i => i != null && i.GetComponent<LootData>() is { } l && (l.spawnLocations & pools) != 0
            && i.GetComponents<Action_RestoreHunger>().Length > 0)
        .ToList();

    private float showerUntil;
    private List<Item> showerFood = new();
    private int showerDropped;

    /// <summary>The wild food of every biome climbed so far (this one included).</summary>
    private static List<Item> FoodSoFar()
    {
        var map = Singleton<MapHandler>.Instance;
        SpawnPool pools = 0;
        for (int i = 0; i <= Mathf.Min(map.currentSegment, map.segments.Length - 1); i++)
            pools |= WildIn(map.segments[i].biome);
        var food = WildFood(pools);
        return food.Count > 0 ? food : WildFood(Wild);
    }

    /// <summary>
    /// Green: every so often a storm of food - a few seconds of it falling around every scout.
    /// Food left on the ground is gone before the next storm (pick it up to keep it).
    /// </summary>
    private void FoodRain()
    {
        float life = Mathf.Max(5f, ModConfig.SunGreenFoodLife.Value);
        for (int i = rained.Count - 1; i >= 0; i--)
        {
            var (item, at) = rained[i];
            if (item == null) { rained.RemoveAt(i); continue; } // eaten or pocketed
            if (Time.time - at < life || item.holderCharacter != null) continue;
            if (item.photonView != null && item.photonView.IsMine) PhotonNetwork.Destroy(item.gameObject);
            rained.RemoveAt(i);
        }

        float every = ModConfig.SunGreenRainEvery.Value;
        if (!On || Current != RelicGem.Generosity || !IsDay || every <= 0f) { nextRain = Time.time + 8f; showerUntil = 0f; return; }

        if (Time.time >= nextRain)
        {
            nextRain = Time.time + every * Random.Range(0.9f, 1.1f);
            showerUntil = Time.time + Mathf.Max(0.5f, ModConfig.SunGreenShowerSeconds.Value);
            showerFood = FoodSoFar();
            showerDropped = 0;
            Plugin.Log.LogInfo($"Suns: food storm - {showerFood.Count} wild foods: {string.Join(", ", showerFood.Select(f => f.gameObject.name))}.");
        }
        if (Time.time >= showerUntil || showerFood.Count == 0) return;

        // HostTick runs twice a second: spread each scout's share over the storm.
        float ticks = Mathf.Max(1f, ModConfig.SunGreenShowerSeconds.Value / 0.5f);
        int per = Mathf.CeilToInt(ModConfig.SunGreenRainCount.Value / ticks);
        foreach (var c in Character.AllCharacters)
        {
            if (c == null || c.isBot || c.data.dead || Sanctuary.Safe(c)) continue;
            for (int n = 0; n < per; n++)
                if (DropFood(showerFood[Random.Range(0, showerFood.Count)], c.Center)) showerDropped++;
        }
        if (Time.time + 0.5f >= showerUntil) Plugin.Log.LogInfo($"Suns: food storm over - {showerDropped} piece(s).");
    }

    private bool DropFood(Item prefab, Vector3 near)
    {
        var flat = Random.insideUnitCircle.normalized * Random.Range(2f, 7f);
        var from = near + new Vector3(flat.x, 0.5f, flat.y);
        // Under an overhang, drop from just below it; never through rock.
        var up = HelperFunctions.LineCheck(from, from + Vector3.up * 18f, HelperFunctions.LayerType.AllPhysical);
        var pos = up.transform != null ? up.point - Vector3.up * 1f : from + Vector3.up * 18f;
        if (pos.y < from.y) return false;
        var down = HelperFunctions.LineCheck(pos, pos - Vector3.up * 60f, HelperFunctions.LayerType.AllPhysical);
        if (down.transform == null) return false;
        var view = PhotonNetwork.InstantiateItemRoom(prefab.gameObject.name, pos, Random.rotation).GetComponent<PhotonView>();
        view.RPC("SetKinematicRPC", RpcTarget.All, false, view.transform.position, view.transform.rotation);
        if (view.GetComponent<Item>() is { } item) rained.Add((item, Time.time));
        return true;
    }

    // --- blue on the Shore: the sunlight is freezing ---------------------------------------------------
    // The Mesa's burning sun, mirrored: same amount every half second, same shade / parasol rules,
    // and the sun's cold fades the way the Mesa's heat does. A lit lantern, candle or campfire
    // keeps it off you. The blue sun doesn't double its own cold.

    private float coldTick;
    private static float sunCold;
    private static float lastSunCold = -999f;
    internal static bool AddingSunCold;

    private void ColdLight()
    {
        var me = Character.localCharacter;
        if (me == null || me.data.dead) { sunCold = 0f; return; }
        var aff = me.refs.afflictions;
        bool shore = InRun && Singleton<MapHandler>.Instance.GetCurrentBiome() == Biome.BiomeType.Shore;
        bool lit = Blue && shore && IsDay && Blend > 0.5f && ModConfig.SunBlueFreezeSeconds.Value > 0f && InSunlight(me) && !Warmed(me);
        if (lit)
        {
            coldTick += Time.deltaTime;
            if (coldTick < 0.5f) return;
            coldTick = 0f;
            float before = aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold);
            AddingSunCold = true;
            // The game banks it and adds a tick (1/40 of the bar) whenever enough has built up.
            try { aff.AddStatus(CharacterAfflictions.STATUSTYPE.Cold, 0.5f / Mathf.Max(1f, ModConfig.SunBlueFreezeSeconds.Value)); }
            finally { AddingSunCold = false; }
            sunCold += Mathf.Max(0f, aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold) - before);
            lastSunCold = Time.time;
            return;
        }
        coldTick = 0f;
        // Out of the light, the sun's cold (and only that) fades like the Mesa's heat.
        if (sunCold <= 0f || Time.time - lastSunCold < aff.hotReductionCooldown) return;
        float melt = Mathf.Min(sunCold, Mathf.Max(0.005f, aff.hotReductionPerSecond) * Time.deltaTime);
        sunCold -= melt;
        if (aff.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold) > 0f) aff.SubtractStatus(CharacterAfflictions.STATUSTYPE.Cold, melt, false, true);
        else sunCold = 0f;
    }

    /// <summary>Inside a lit lantern's / candle's / campfire's glow.</summary>
    private static bool Warmed(Character me) =>
        me.data.wearingSunscreen || Peak.GloomSafeZone.CharacterProtectedFromStatus(me, out _, out _);

    /// <summary>Like the Mesa's hot sun: nothing between you and the sun, no open parasol, not gliding.</summary>
    private static bool InSunlight(Character me)
    {
        var sun = DayNightManager.instance.sun;
        if (sun == null) return false;
        var item = me.data.currentItem;
        if (item != null && ((item.TryGetComponent<Parasol>(out var parasol) && parasol.isOpen) || item.gliderHold)) return false;
        var hit = HelperFunctions.LineCheck(me.Center - sun.transform.forward * 1000f, me.Center, HelperFunctions.LayerType.AllPhysical);
        return hit.transform == null || hit.transform.root == me.transform.root;
    }

    // --- a faint wash of the sun's colour over the screen ---------------------------------------------

    private static UnityEngine.UI.Image? wash;

    private void Wash()
    {
        float a = Blend * Mathf.Clamp01(ModConfig.SunWash.Value) * (1f - BloodMoon.Blend);
        if (wash == null)
        {
            if (a <= 0f) return;
            var go = new GameObject("PEAK Roguelike - Sun Wash");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -51; // under the HUD and the blood filter
            var img = new GameObject("Wash", typeof(RectTransform));
            img.transform.SetParent(go.transform, false);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            wash = img.AddComponent<UnityEngine.UI.Image>();
            wash.raycastTarget = false;
        }
        wash.enabled = a > 0.001f && Shown != null;
        if (wash.enabled) { var c = Palette(Shown!.Value).sun; c.a = a; wash.color = c; }
    }

    private static int Segment => MapHandler.Exists ? Singleton<MapHandler>.Instance.currentSegment : 0;
    private static bool FinalStretch => Segment >= (int)global::Segment.TheKiln;

    private static RelicGem? TodaysSun()
    {
        if (FinalStretch)
        {
            // Peak / Citadel / Kiln: a different held relic's colour each day.
            var held = RunState.SquadGems().OrderBy(g => (int)g).ToList();
            if (held.Count == 0) return null;
            int day = DayNightManager.instance != null ? DayNightManager.instance.dayCount : 0;
            return held[Mathf.Abs(day) % held.Count];
        }
        var biome = BiomeGem(Segment);
        return biome is { } g2 && !Cleared(g2) ? g2 : null;
    }

    /// <summary>Has the squad got this colour's relic?</summary>
    internal static bool Cleared(RelicGem gem) => RunState.SquadGems().Contains(gem);

    /// <summary>
    /// Host, at nightfall: may a blood moon rise here? Only once this biome's relic is held
    /// (from the Kiln on: whenever relics are held). A biome without a statue keeps the old rule.
    /// </summary>
    internal static bool NightsUnlocked
    {
        get
        {
            if (!On) return true;
            if (FinalStretch) return RunState.SquadGems().Count > 0;
            return BiomeGem(Segment) is not { } gem || Cleared(gem);
        }
    }

    // --- which amulet each biome's statue holds -------------------------------------------------

    private static MapHandler? gemsFor;
    private static readonly Dictionary<int, RelicGem?> biomeGems = new();

    /// <summary>
    /// The amulet statue in a section: a PropSpawner_AmuletStatues whose spawned statue carries a
    /// SingleItemSpawner for one of the four amulets (which one is shuffled per daily map).
    /// </summary>
    internal static RelicGem? BiomeGem(int segment)
    {
        if (!MapHandler.Exists) return null;
        var map = Singleton<MapHandler>.Instance;
        if (gemsFor != map) { gemsFor = map; biomeGems.Clear(); nextLook.Clear(); missLogged.Clear(); }
        if (biomeGems.TryGetValue(segment, out var cached)) return cached;
        if (segment < 0 || segment >= map.segments.Length) return null;
        // A miss is looked at again every few seconds (the amulet may spawn after the statue).
        if (Time.time < nextLook.GetValueOrDefault(segment)) return null;
        nextLook[segment] = Time.time + 5f;
        var seg = map.segments[segment];
        var parent = seg.segmentParent;
        // The section's statues: under its parent, else any statue sitting in a biome of its type.
        var statues = parent != null ? parent.GetComponentsInChildren<Peak.PropSpawner_AmuletStatues>(true).ToList() : new List<Peak.PropSpawner_AmuletStatues>();
        string how = "section";
        if (statues.Count == 0)
        {
            statues = FindObjectsByType<Peak.PropSpawner_AmuletStatues>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(s => s.GetComponentInParent<Biome>() is { } b && b.biomeType == seg.biome).ToList();
            how = "biome";
        }
        RelicGem? found = null;
        foreach (var statue in statues)
        {
            found = StatueGem(statue);
            if (found != null) break;
        }
        // Only a hit is kept; a statue with no amulet in sight is checked again later.
        if (found != null)
        {
            biomeGems[segment] = found;
            Plugin.Log.LogInfo($"Suns: section {(global::Segment)segment} ({seg.biome}): {statues.Count} statue(s) by {how} -> {ColorName(found.Value)} sun.");
        }
        else if (missLogged.Add(segment))
        {
            Plugin.Log.LogInfo($"Suns: section {(global::Segment)segment} ({seg.biome}): {statues.Count} statue(s) by {how} -> no amulet found yet, still looking.");
            foreach (var statue in statues) DumpStatue(statue);
        }
        return found;
    }

    private static readonly Dictionary<int, float> nextLook = new();
    private static readonly HashSet<int> missLogged = new();

    /// <summary>The amulet on a statue: its item spawner's prefab, an amulet built into it, or one lying on it.</summary>
    private static RelicGem? StatueGem(Peak.PropSpawner_AmuletStatues statue)
    {
        // The statue shows a FakeItem; it hands out its real amulet only when picked up.
        foreach (var fake in statue.GetComponentsInChildren<FakeItem>(true))
            if (fake.realItemPrefab != null && Items.IsAmulet(fake.realItemPrefab)) return Items.AmuletGem(fake.realItemPrefab);
        foreach (var spawner in statue.GetComponentsInChildren<SingleItemSpawner>(true))
        {
            var item = spawner.prefab != null ? spawner.prefab.GetComponent<Item>() : null;
            if (item != null && Items.IsAmulet(item)) return Items.AmuletGem(item);
        }
        foreach (var spawner in statue.GetComponentsInChildren<Spawner>(true))
        {
            var prefabs = new List<GameObject?> { spawner.spawnedObjectPrefab, spawner.fallbackSpawn };
            if (spawner.spawns != null && spawner.spawns.items != null) prefabs.AddRange(spawner.spawns.items.Select(e => e.prefab));
            foreach (var prefab in prefabs)
                if (prefab != null && prefab.GetComponent<Item>() is { } item && Items.IsAmulet(item)) return Items.AmuletGem(item);
        }
        foreach (var item in statue.GetComponentsInChildren<Item>(true))
            if (Items.IsAmulet(item)) return Items.AmuletGem(item);
        // The amulet lying on the statue: the nearest loose amulet within reach of the statue's bounds.
        var bounds = StatueBounds(statue);
        Item? nearest = null;
        float best = 12f;
        foreach (var item in FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (item == null || item.holderCharacter != null || !Items.IsAmulet(item)) continue;
            float d = Mathf.Sqrt(bounds.SqrDistance(item.transform.position));
            if (d < best) { best = d; nearest = item; }
        }
        return nearest != null ? Items.AmuletGem(nearest) : null;
    }

    private static Bounds StatueBounds(Peak.PropSpawner_AmuletStatues statue)
    {
        var root = statue.transform.childCount > 0 ? statue.transform.GetChild(0) : statue.transform;
        var b = new Bounds(root.position, Vector3.one);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Log what a statue is made of, so a missed amulet can be tracked down.</summary>
    private static void DumpStatue(Peak.PropSpawner_AmuletStatues statue)
    {
        var bounds = StatueBounds(statue);
        var parts = statue.GetComponentsInChildren<Component>(true)
            .Where(c => c != null && c is not Transform && c is not Renderer && c is not MeshFilter && c is not Collider)
            .Select(c => $"{c.gameObject.name}:{c.GetType().Name}").Distinct().Take(40);
        Plugin.Log.LogInfo($"Suns: statue '{statue.name}' child '{(statue.transform.childCount > 0 ? statue.transform.GetChild(0).name : "-")}' "
            + $"at {bounds.center} parts [{string.Join(", ", parts)}]");
        foreach (var item in FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (item != null && item.itemTags.HasFlag(Item.ItemTags.ScoutAmulet))
                Plugin.Log.LogInfo($"Suns:   amulet '{item.name}' ({Items.AmuletGem(item)?.ToString() ?? "?"}) "
                    + $"{Mathf.Sqrt(bounds.SqrDistance(item.transform.position)):0}m from it, held={(item.holderCharacter != null)}");
    }

    // --- sky: the game's own special-day blend -----------------------------------------------------

    private static (Color sun, Color top, Color mid, Color bottom) Palette(RelicGem g) => g switch
    {
        // Saturated: lemon yellow, vivid green, hot red-orange (never the blood eclipse's dark red disc), deep blue.
        RelicGem.Ambition => (new Color(1f, 0.85f, 0f), new Color(0.75f, 0.6f, 0f), new Color(1f, 0.85f, 0.05f), new Color(1f, 0.93f, 0.3f)),
        RelicGem.Generosity => (new Color(0.2f, 1f, 0.1f), new Color(0.02f, 0.45f, 0.05f), new Color(0.15f, 0.9f, 0.1f), new Color(0.45f, 1f, 0.3f)),
        RelicGem.Initiative => (new Color(1f, 0.32f, 0f), new Color(0.65f, 0.12f, 0f), new Color(1f, 0.3f, 0.02f), new Color(1f, 0.55f, 0.15f)),
        _ => (new Color(0.1f, 0.35f, 1f), new Color(0f, 0.08f, 0.55f), new Color(0.08f, 0.3f, 1f), new Color(0.35f, 0.6f, 1f)),
    };

    [HarmonyPatch]
    private static class SkyPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(DayNightManager), nameof(DayNightManager.UpdateCycle))]
        private static void Tint(DayNightManager __instance, out (float, Color, float, Color, Color, Color)? __state)
        {
            __state = null;
            if (!Application.isPlaying || Blend <= 0f || Shown is not { } gem) return;
            var d = __instance;
            __state = (d.specialDaySunBlend, d.specialSunColor, d.specialDaySkyBlend, d.specialTopColor, d.specialMidColor, d.specialBottomColor);
            var p = Palette(gem);
            float k = Blend * Mathf.Clamp01(ModConfig.SunSky.Value);
            // Sun blend must stay under 0.5 - at 0.5 the game switches the sun off.
            d.specialDaySunBlend = Mathf.Max(d.specialDaySunBlend, 0.45f * k);
            d.specialSunColor = p.sun;
            d.specialDaySkyBlend = Mathf.Max(d.specialDaySkyBlend, Mathf.Clamp01(ModConfig.SunSkyBlend.Value) * k);
            d.specialTopColor = p.top;
            d.specialMidColor = p.mid;
            d.specialBottomColor = p.bottom;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(DayNightManager), nameof(DayNightManager.UpdateCycle))]
        private static void Restore(DayNightManager __instance, (float, Color, float, Color, Color, Color)? __state)
        {
            if (__state is not { } s) return;
            var d = __instance;
            (d.specialDaySunBlend, d.specialSunColor, d.specialDaySkyBlend, d.specialTopColor, d.specialMidColor, d.specialBottomColor) = s;
        }
    }

    // --- green / Generosity: shared meals ------------------------------------------------------------

    internal static int Eating;
    internal static float Eaten;
    internal static bool Sharing;

    /// <summary>Someone nearby ate while holding Generosity: you get a share.</summary>
    internal static void ReceiveShare(Vector3 origin, float radius, float amount)
    {
        var me = Character.localCharacter;
        if (me == null || me.data.dead || Vector3.Distance(me.Center, origin) > radius) return;
        Sharing = true;
        try { me.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Hunger, amount); }
        finally { Sharing = false; }
    }
}

[HarmonyPatch]
internal static class SunPatches
{
    private static bool Local(Character? c) => c != null && c.IsLocal;

    // --- yellow: stamina ----------------------------------------------------------------------------

    [HarmonyPrefix, HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    private static void Drain(Character __instance, ref float usage)
    {
        if (Suns.Yellow && usage > 0f && __instance.IsLocal) usage *= ModConfig.SunYellowDrain.Value;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Character), "UpdateVariablesFixed")]
    private static void SlowRegen(Character __instance)
    {
        if (!Suns.Yellow || !__instance.IsLocal || !__instance.CanRegenStamina()) return;
        float take = Time.fixedDeltaTime * 0.2f * (1f - Mathf.Clamp01(ModConfig.SunYellowRegen.Value));
        __instance.data.currentStamina = Mathf.Max(0f, __instance.data.currentStamina - take);
    }

    private static bool stumbled;

    /// <summary>Yellow: run yourself dry on the ground and your legs give out for a moment.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Character), "UpdateVariablesFixed")]
    private static void Stumble(Character __instance)
    {
        if (!__instance.IsLocal) return;
        var d = __instance.data;
        if (d.outOfStaminaFor <= 0f) { stumbled = false; return; }
        float secs = ModConfig.SunYellowStumble.Value;
        if (stumbled || !Suns.Yellow || secs <= 0f || d.dead || !d.isGrounded || d.isClimbing || d.isRopeClimbing || d.isVineClimbing || d.fallSeconds > 0f) return;
        stumbled = true;
        __instance.photonView.RPC("RPCA_Fall", RpcTarget.All, secs, 0.4f);
    }

    // --- green: hunger ------------------------------------------------------------------------------

    [HarmonyPostfix, HarmonyPatch(typeof(Ascents), nameof(Ascents.hungerRateMultiplier), MethodType.Getter)]
    private static void Hungrier(ref float __result)
    {
        if (Suns.Green) __result *= ModConfig.SunGreenHunger.Value;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Action_RestoreHunger), nameof(Action_RestoreHunger.RunAction))]
    private static void EatStart(ItemAction __instance) => BeginMeal(__instance);
    [HarmonyPostfix, HarmonyPatch(typeof(Action_RestoreHunger), nameof(Action_RestoreHunger.RunAction))]
    private static void EatEnd(ItemAction __instance) => EndMeal(__instance);
    [HarmonyPrefix, HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static void ModStart(ItemAction __instance) => BeginMeal(__instance);
    [HarmonyPostfix, HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static void ModEnd(ItemAction __instance) => EndMeal(__instance);

    private static void BeginMeal(ItemAction a)
    {
        if (!Local(a.character)) return;
        if (Suns.Eating++ == 0) Suns.Eaten = 0f;
    }

    private static void EndMeal(ItemAction a)
    {
        if (!Local(a.character) || Suns.Eating <= 0) return;
        if (--Suns.Eating > 0 || Suns.Eaten <= 0f) return;
        // Generosity: the meal feeds the scouts around you too.
        if (ModConfig.Relics.Value && RunState.Relics.ContainsKey(RelicGem.Generosity))
            Net.ShareMeal(a.character.Center, ModConfig.AuraRadius.Value, Suns.Eaten * ModConfig.GenerosityShare.Value);
        Suns.Eaten = 0f;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.SubtractStatus))]
    private static void Meal(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount, out float __state)
    {
        __state = -1f;
        if (statusType != CharacterAfflictions.STATUSTYPE.Hunger || Suns.Sharing || Suns.Eating <= 0 || !Local(__instance.character)) return;
        // (the green sun's cut on food is SunCapPatches.CapRelief)
        __state = __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hunger);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.SubtractStatus))]
    private static void MealDone(CharacterAfflictions __instance, float __state)
    {
        if (__state < 0f) return;
        Suns.Eaten += Mathf.Max(0f, __state - __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hunger));
    }

    // --- red: heavy -------------------------------------------------------------------------------------

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.JumpRpc))]
    private static void LowJump(CharacterMovement __instance, out float __state)
    {
        __state = __instance.jumpImpulse;
        // Jump height goes with the square of the launch speed.
        if (Suns.Red) __instance.jumpImpulse *= Mathf.Sqrt(Mathf.Clamp(ModConfig.SunRedJump.Value, 0.05f, 1f));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.JumpRpc))]
    private static void LowJumpDone(CharacterMovement __instance, float __state) => __instance.jumpImpulse = __state;

    private static readonly AccessTools.FieldRef<CharacterMovement, Character> MoverOf = AccessTools.FieldRefAccess<CharacterMovement, Character>("character");

    private static bool RedFor(CharacterMovement m) => Suns.Red && Local(MoverOf(m));

    /// <summary>Red: sprinting barely helps, so no long sprint-jumps. Walking and climbing stay vanilla.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), "GetMovementForce")]
    private static void HeavySprint(CharacterMovement __instance, out float __state)
    {
        __state = __instance.sprintMultiplier;
        if (__instance.sprintMultiplier > 1f && RedFor(__instance))
            __instance.sprintMultiplier = 1f + (__state - 1f) * (1f - Mathf.Clamp01(ModConfig.SunRedSprintCut.Value));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), "GetMovementForce")]
    private static void HeavySprintDone(CharacterMovement __instance, float __state) => __instance.sprintMultiplier = __state;

    /// <summary>Red: everything pulls down harder - the jump's lift is weaker, the drop is stronger.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), "GetGravityForce")]
    private static void Heavy(CharacterMovement __instance, ref Vector3 __result)
    {
        if (__result.y == 0f || !RedFor(__instance)) return;
        float g = Mathf.Max(1f, ModConfig.SunRedGravity.Value);
        __result.y = __result.y < 0f ? __result.y * g : __result.y / g;
    }

    // --- blue: brittle (damage stays vanilla - ascents already scale it) ----------------------------------

    [HarmonyPrefix, HarmonyPatch(typeof(Action_ReduceUses), nameof(Action_ReduceUses.RunAction))]
    private static void WearStart(Action_ReduceUses __instance, out int __state)
    {
        __state = -1;
        if (!Suns.Blue || !ModConfig.SunBlueWear.Value || !Local(__instance.character)) return;
        var data = __instance.item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (data.HasData) __state = data.Value;
    }

    /// <summary>Blue: every use costs two.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Action_ReduceUses), nameof(Action_ReduceUses.RunAction))]
    private static void Wear(Action_ReduceUses __instance, int __state)
    {
        if (__state <= 1) return; // none tracked, or that use was its last
        var item = __instance.item;
        item.photonView.RPC("ReduceUsesRPC", RpcTarget.All);
        var data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (data.HasData && data.Value == 0 && __instance.consumeOnFullyUsed) item.StartCoroutine(item.ConsumeDelayed());
    }
}
