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
/// Each sun works against its own relic, and the Shore is where you learn it (hardest first):
///   blue   (Tenacity: heal + invincible)  every hit lands x1.5, healing items heal half;
///                                         on the Shore its light slowly turns you to glass
///   red    (Initiative: super jump)       sprint and climbing lunge at 75%, the glider and
///                                         balloons at half, the jetpack burns 2x; on the Shore heavy, low jumps
///   yellow (Ambition: infinite stamina)   stamina drains faster, stamina rushes are short;
///                                         on the Shore, pushing on with none left makes you drowsy
///   green  (Generosity: an extra pick)    items wear twice as fast, ropes fray, pitons crack,
///                                         nothing new stacks; on the Shore a crate card withers
///
/// Spared: carrying that sun's relic, sunscreen, an open parasol (a minute of shield, then it
/// burns), safe ground, lying down. The host decides the sun (room property prl.sun = gem, or -1);
/// every client applies the effects to its own scout. The sky is coloured through the game's own
/// "special day" sky blend. Announced with one line - what it does is for the scouts to find out.
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
    /// <summary>The Shore teaches each sun: blue glass, yellow drowsiness, a withered green card. The Void tests it, and Harsh suns (ticket) keep teaching it in every biome.</summary>
    internal static bool OnShore => InRun && (VoidRun.Active || Tickets.HarshSunsEverywhere || Singleton<MapHandler>.Instance.GetCurrentBiome() == Biome.BiomeType.Shore);

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

    private static bool IsDay => InRun && (VoidRun.Active || DayNightManager.instance.isDay >= 0.5f);

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
        try { GlassLight(); ParasolTick(); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Suns glass / parasol: {e.Message}"); }
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
        if (v != seen)
        {
            seen = v;
            announce = Current;
            Plugin.Log.LogInfo(Current is { } gem ? $"Sun: {ColorName(gem)}." : "Sun: normal.");
        }
        Announce();
    }

    private RelicGem? announce;

    /// <summary>
    /// The sun is named the first time it can touch you: awake, off safe ground, no sunscreen.
    /// (It used to play while the scouts still lay unconscious at the crash, and nobody saw it.)
    /// </summary>
    private void Announce()
    {
        if (announce is not { } gem) return;
        if (Current != gem) { announce = null; return; }
        var me = Character.localCharacter;
        if (me == null || me.data.dead || Sanctuary.SunSpared || !IsDay) return;
        announce = null;
        Plugin.Log.LogInfo($"Sun: announced {ColorName(gem)} (first time it reaches you).");
        Hud.Banner("", $"THE SUN BURNS {ColorName(gem).ToUpperInvariant()}", Relics.GemColor(gem), Hint(gem));
    }

    /// <summary>The one-line hint under the sun's banner: how it feels, never what it does.</summary>
    internal static string Hint(RelicGem g) => g switch
    {
        RelicGem.Ambition => "You feel drained.",
        RelicGem.Generosity => "You feel wasteful.",
        RelicGem.Initiative => "You feel heavier.",
        _ => "You feel fragile.",
    };

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
    }

    // --- blue on the Shore: its light turns you to glass -----------------------------------------------
    // In direct blue sunlight (the Mesa's shade rules) the glass bar fills one percent at a time. Shade,
    // sunscreen, an open parasol or safe ground stop it, but only nightfall or the blue relic undoes it -
    // and then only the sun's own share: glass from relics stays.

    private float glassTick;
    /// <summary>How much of our glass (percent) the blue sun put there.</summary>
    internal static int SunGlass { get; private set; }

    private void GlassLight()
    {
        var me = Character.localCharacter;
        if (me == null || me.data.dead) { SunGlass = 0; return; }
        // Healed by a campfire, revived, a lunchbox: the sun's share can't be more than what's there.
        SunGlass = Mathf.Min(SunGlass, me.data.petrifyAmount);
        float full = Mathf.Max(10f, ModConfig.SunBlueGlassSeconds.Value);
        if (Blue && OnShore && IsDay && Blend > 0.5f && InSunlight(me))
        {
            glassTick += Time.deltaTime;
            float every = full / 100f;
            if (glassTick < every) return;
            glassTick -= every;
            me.refs.afflictions.AddPetrify(1);
            SunGlass++;
            return;
        }
        glassTick = 0f;
        // Night, a new sun, or the squad took the blue relic: the sun's glass melts away over a few seconds.
        if (SunGlass > 0 && Current != RelicGem.Tenacity)
        {
            glassTick = 0f;
            int melt = Mathf.Min(SunGlass, Mathf.Max(1, Mathf.CeilToInt(Time.deltaTime * 20f)));
            me.refs.afflictions.AddPetrify(-melt);
            SunGlass -= melt;
            if (SunGlass == 0) Plugin.Log.LogInfo("Blue sun: its glass has melted away.");
        }
    }

    // --- the parasol: a minute of shade from every coloured sun, then it burns -------------------------

    /// <summary>The open parasol in our hands, if it still has shield left.</summary>
    internal static bool ParasolShade(Character? me)
    {
        var item = me == null ? null : me.data.currentItem;
        if (item == null || !item.TryGetComponent<Parasol>(out var p) || !p.isOpen) return false;
        return !Burnt(item) && ParasolLeft(item) > 0f;
    }

    private static bool Burnt(Item item) => item.GetData<IntItemData>(DataEntryKey.CookedAmount).Value > 2;

    private static float ParasolMax => Mathf.Max(1f, ModConfig.ParasolShieldSeconds.Value);

    internal static float ParasolLeft(Item item) =>
        item.GetData(DataEntryKey.Fuel, () => new FloatItemData { Value = ParasolMax }).Value;

    private static Item? shownParasol;

    /// <summary>Open under a coloured sun, the shield runs down; empty, the parasol burns (the campfire's burnt look).</summary>
    private void ParasolTick()
    {
        var me = Character.localCharacter;
        var item = me != null ? me.data.currentItem : null;
        if (item == null || !item.TryGetComponent<Parasol>(out var p) || Burnt(item)) { shownParasol = null; return; }
        var fuel = item.GetData(DataEntryKey.Fuel, () => new FloatItemData { Value = ParasolMax });
        if (shownParasol != item) { shownParasol = item; item.SetUseRemainingPercentage(fuel.Value / ParasolMax); }
        if (!p.isOpen || Current == null || !IsDay || !On) return;
        fuel.Value = Mathf.Max(0f, fuel.Value - Time.deltaTime);
        item.SetUseRemainingPercentage(fuel.Value / ParasolMax);
        if (fuel.Value > 0f) return;
        // The game's own cooking, three times over: past "cooked" into burnt black.
        if (item.TryGetComponent<ItemCooking>(out var cook))
            for (int i = 0; i < 3; i++) cook.photonView.RPC("SyncCookingStateRPC", RpcTarget.All, ItemCooking.NetworkedCooking.FinishCooking);
        Plugin.Log.LogInfo($"Parasol: its shade is spent - it burns{(cook == null ? " (no cooking component: shade gone, look unchanged)" : "")}.");
    }

    /// <summary>Dev panel: the parasol in hand gets its full minute back (not unburnt).</summary>
    internal static void DevRefillParasol()
    {
        var item = Character.localCharacter?.data.currentItem;
        if (item == null || !item.TryGetComponent<Parasol>(out _)) { Hud.Toast("Hold a parasol", ChoiceScreen.Muted); return; }
        item.GetData(DataEntryKey.Fuel, () => new FloatItemData { Value = ParasolMax }).Value = ParasolMax;
        item.SetUseRemainingPercentage(1f);
        Hud.Toast("Parasol shade refilled", ChoiceScreen.Cream);
    }

    // --- green on the Shore: one crate card withers ------------------------------------------------------

    private static readonly Dictionary<int, (string cards, int index)> withered = new();

    /// <summary>
    /// The crate card the green sun takes from us (null = none): one of the crate's own cards, the
    /// same one each time we look until its cards change. Never the only card.
    /// </summary>
    internal static int? WitheredCard(Luggage luggage, List<Item> items)
    {
        if (!Green || !OnShore || items.Count < 2 || luggage == null) return null;
        string sig = string.Join(",", items.Select(i => i.itemID));
        int id = luggage.GetInstanceID();
        if (!withered.TryGetValue(id, out var w) || w.cards != sig)
        {
            withered[id] = w = (sig, Random.Range(0, items.Count));
            Plugin.Log.LogInfo($"Green sun: {luggage.displayName}'s {items[w.index].gameObject.name} card withers.");
        }
        return w.index;
    }

    /// <summary>Like the Mesa's hot sun: nothing between you and the sun, not gliding.</summary>
    private static bool InSunlight(Character me)
    {
        var sun = DayNightManager.instance.sun;
        if (sun == null) return false;
        var item = me.data.currentItem;
        if (item != null && item.gliderHold) return false; // (an open parasol spares you through SunSpared - a burnt one doesn't)
        if (VoidRun.Active) return true; // no shade in the Void
        var hit = HelperFunctions.LineCheck(me.Center - sun.transform.forward * 1000f, me.Center, HelperFunctions.LayerType.AllPhysical);
        return hit.transform == null || hit.transform.root == me.transform.root;
    }

    private static int Segment => MapHandler.Exists ? Singleton<MapHandler>.Instance.currentSegment : 0;
    private static bool FinalStretch => Segment >= (int)global::Segment.TheKiln;

    private static RelicGem? TodaysSun()
    {
        if (VoidRun.Active) return VoidRun.Sun();
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

    /// <summary>The statues' amulets were swapped (relic shuffle): look at them again.</summary>
    internal static void ForgetGems()
    {
        biomeGems.Clear();
        nextLook.Clear();
        missLogged.Clear();
    }

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
        // The whole sky at full saturation, top to horizon, in bright hues (never murky). The light on the
        // world is only a moderate tint of the hue (no screen filter - shadows stay real shadows). Lemon
        // yellow, green, red-orange (never the blood eclipse's dark red disc), blue.
        RelicGem.Ambition => (new Color(1f, 0.85f, 0.4f), new Color(1f, 0.72f, 0f), new Color(1f, 0.8f, 0.02f), new Color(1f, 0.88f, 0.12f)),
        RelicGem.Generosity => (new Color(0.62f, 1f, 0.48f), new Color(0.05f, 0.82f, 0.02f), new Color(0.15f, 1f, 0.06f), new Color(0.35f, 1f, 0.2f)),
        RelicGem.Initiative => (new Color(1f, 0.6f, 0.45f), new Color(1f, 0.22f, 0f), new Color(1f, 0.34f, 0.02f), new Color(1f, 0.48f, 0.12f)),
        _ => (new Color(0.55f, 0.72f, 1f), new Color(0f, 0.28f, 1f), new Color(0.04f, 0.45f, 1f), new Color(0.25f, 0.62f, 1f)),
    };

    private const float MaxBoost = 2.5f;
    private static RelicGem? boostLogged;

    /// <summary>How bright a colour looks (Rec. 709 weights).</summary>
    private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    // --- the shade under a coloured sun ------------------------------------------------------------
    // PEAK's ambient light (its light volume's _AmbienceStrength - see LightsOut) never takes the sun's
    // colour, so shade looks murky next to the coloured light: it's raised a little while a sun shows.
    // The game's own value is remembered per scene and put back as the sun fades.

    private static readonly int AmbStrength = Shader.PropertyToID("_AmbienceStrength");
    private float shadeBase = -1f;
    private int shadeScene = -1;

    private void LateUpdate()
    {
        if (LightsOut.Active) return; // lights out owns the ambient while it lasts
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (scene != shadeScene) { shadeScene = scene; shadeBase = -1f; }
        float lift = Shown != null && IsDay ? Mathf.Clamp(ModConfig.SunShade.Value, 0f, 0.5f) * Blend : 0f;
        // A blood moon / eclipse darkens the shade instead (its night look).
        float dim = BloodMoon.Visuals.AmbientDim;
        if (lift <= 0f && dim <= 0f)
        {
            if (shadeBase >= 0f) { Shader.SetGlobalFloat(AmbStrength, shadeBase); shadeBase = -1f; }
            return;
        }
        if (shadeBase < 0f) shadeBase = Shader.GetGlobalFloat(AmbStrength);
        Shader.SetGlobalFloat(AmbStrength, shadeBase * (1f + lift) * (1f - dim));
    }

    [HarmonyPatch]
    private static class SkyPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(DayNightManager), nameof(DayNightManager.UpdateCycle))]
        private static void Tint(DayNightManager __instance, out (float, Color, float, Color, Color, Color)? __state)
        {
            __state = null;
            if (!Application.isPlaying || Blend <= 0f || Shown is not { } gem)
            {
                SunFlare.Tint(__instance, default, 0f);
                return;
            }
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
            SunFlare.Tint(d, p.mid, k);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(DayNightManager), nameof(DayNightManager.UpdateCycle))]
        private static void Restore(DayNightManager __instance, (float, Color, float, Color, Color, Color)? __state)
        {
            if (__state is not { } s) return;
            var d = __instance;
            (d.specialDaySunBlend, d.specialSunColor, d.specialDaySkyBlend, d.specialTopColor, d.specialMidColor, d.specialBottomColor) = s;
            // The sunlight itself, past the game's cap (its special-day blend switches the sun off at
            // 0.5): recolour the light after the game sets it. Brightness and day/night (alpha) kept.
            if (Shown is { } gem && d.sun != null && d.sun.enabled)
            {
                float k = Mathf.Clamp(ModConfig.SunLight.Value, 0f, 1.5f) * Blend * Mathf.Clamp01(ModConfig.SunSky.Value);
                if (k <= 0f) return;
                Color c = d.sun.color;
                Color hue = Palette(gem).mid * c.maxColorComponent;
                hue.a = c.a;
                // Past 1 the light overshoots the sun's colour (more saturated), never below black.
                Color lit = Color.LerpUnclamped(c, hue, k);
                d.sun.color = new Color(Mathf.Max(0f, lit.r), Mathf.Max(0f, lit.g), Mathf.Max(0f, lit.b), c.a);
                // A deep colour is a dim light (blue keeps about a third of white's brightness): the
                // sun shines harder to make up what the colour took, so the day stays as bright.
                float before = Luma(c), after = Luma(d.sun.color);
                if (before > 0.001f && after > 0.001f)
                {
                    float ratio = Mathf.Clamp(before / after, 1f, MaxBoost);
                    float boost = Mathf.Clamp(Mathf.LerpUnclamped(1f, ratio, Mathf.Clamp(ModConfig.SunBrightness.Value, 0f, 1.5f)), 0.1f, MaxBoost);
                    d.sun.intensity *= boost;
                    if (gem != boostLogged && Blend >= 1f)
                    {
                        boostLogged = gem;
                        Plugin.Log.LogInfo($"Sun: {ColorName(gem)} light is {after / before:P0} as bright - boosted x{boost:0.00}.");
                    }
                }
            }
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

    // Runs before the game's own ascent multiplier, so the two stack (x1.4 on Peak, ~x1.96 on Ascent 8).
    [HarmonyPrefix, HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    private static void Drain(Character __instance, ref float usage)
    {
        if (Suns.Yellow && usage > 0f && __instance.IsLocal) usage *= ModConfig.SunYellowDrain.Value;
    }

    private static float nextDrowsy;

    /// <summary>
    /// Yellow on the Shore: pushing on with no stamina left (sprinting, climbing, jumping) makes you
    /// drowsy - a little each try. It wears off like any drowsiness; push too long and you pass out
    /// the game's own way.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Character), "UpdateVariablesFixed")]
    private static void Drowsy(Character __instance)
    {
        if (!__instance.IsLocal) return;
        var d = __instance.data;
        float amount = ModConfig.SunYellowDrowsy.Value;
        if (d.outOfStaminaFor <= 0f || amount <= 0f || !Suns.Yellow || !Suns.OnShore || d.dead || d.passedOut || Time.time < nextDrowsy) return;
        var input = __instance.input;
        bool moving = input.movementInput.magnitude > 0.1f;
        bool trying = (input.sprintIsPressed || input.sprintToggleIsPressed) && moving
                      || d.isClimbingAnything && moving
                      || input.jumpIsPressed || input.jumpWasPressed;
        if (!trying) return;
        nextDrowsy = Time.time + 0.5f;
        __instance.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, amount);
    }

    // --- blue: everything hurts more ----------------------------------------------------------------

    /// <summary>
    /// Blue: every hit and every tick of harm lands SunBlueDamage times harder - the game's own list
    /// of what difficulty scales (injury and falls, poison, cold, heat, spores, drowsiness, curse).
    /// What items and sources hand out is the same; it's what reaches you.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    private static void Harder(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount)
    {
        if (amount <= 0f || !Suns.Blue || __instance.character == null || !__instance.character.IsLocal) return;
        if (!__instance.StatusAffectedByDifficultyMod(statusType)) return;
        amount *= Mathf.Max(0f, ModConfig.SunBlueDamage.Value);
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

    // --- red: a slower sprint, a shorter lunge; on the Shore heavy and low-jumping ----------------------

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.JumpRpc))]
    private static void LowJump(CharacterMovement __instance, out float __state)
    {
        __state = __instance.jumpImpulse;
        // Shore only. Peak height goes with launch speed squared over gravity: launch so the heavier
        // gravity still lands the jump at RedShoreJumpHeight of vanilla.
        if (RedFor(__instance) && Suns.OnShore)
            __instance.jumpImpulse *= Mathf.Sqrt(Mathf.Clamp(ModConfig.SunRedShoreJump.Value, 0.05f, 1f) * ShoreGravity);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.JumpRpc))]
    private static void LowJumpDone(CharacterMovement __instance, float __state) => __instance.jumpImpulse = __state;

    private static readonly AccessTools.FieldRef<CharacterMovement, Character> MoverOf = AccessTools.FieldRefAccess<CharacterMovement, Character>("character");

    private static bool RedFor(CharacterMovement m) => Suns.Red && Local(MoverOf(m));

    private static float ShoreGravity => Mathf.Max(1f, ModConfig.SunRedGravity.Value);

    /// <summary>Red on the Shore: everything pulls down harder - you drop faster and land harder.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), "GetGravityForce")]
    private static void Heavy(CharacterMovement __instance, ref Vector3 __result)
    {
        if (__result.y == 0f || !RedFor(__instance) || !Suns.OnShore) return;
        __result.y = __result.y < 0f ? __result.y * ShoreGravity : __result.y / ShoreGravity;
    }

    private static bool sprintLogged;

    /// <summary>Red: sprinting runs at a share of its vanilla speed (never slower than walking). Walking and climbing stay vanilla.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), "GetMovementForce")]
    private static void HeavySprint(CharacterMovement __instance, out float __state)
    {
        __state = __instance.sprintMultiplier;
        if (__instance.sprintMultiplier <= 1f || !RedFor(__instance)) return;
        __instance.sprintMultiplier = Mathf.Max(1f, __state * Mathf.Clamp01(ModConfig.SunRedSprintSpeed.Value));
        if (!sprintLogged)
        {
            sprintLogged = true;
            Plugin.Log.LogInfo($"Red sun: sprint x{__state:0.##} -> x{__instance.sprintMultiplier:0.##} of walking.");
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), "GetMovementForce")]
    private static void HeavySprintDone(CharacterMovement __instance, float __state) => __instance.sprintMultiplier = __state;

    private static readonly AccessTools.FieldRef<CharacterClimbing, Character> ClimberOf = AccessTools.FieldRefAccess<CharacterClimbing, Character>("character");

    /// <summary>Red: the climbing lunge carries you a shorter way (stamina cost unchanged).</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.RPCA_ClimbJump))]
    private static void ShortLungeStart(CharacterClimbing __instance, out Vector2 __state) => __state = __instance.playerSlide;

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.RPCA_ClimbJump))]
    private static void ShortLunge(CharacterClimbing __instance, Vector2 __state)
    {
        if (!Suns.Red || !Local(ClimberOf(__instance))) return;
        float keep = Mathf.Clamp01(ModConfig.SunRedLunge.Value);
        __instance.playerSlide = __state + (__instance.playerSlide - __state) * keep;
    }

    // --- green: brittle - items wear out twice as fast --------------------------------------------------

    [HarmonyPrefix, HarmonyPatch(typeof(Action_ReduceUses), nameof(Action_ReduceUses.RunAction))]
    private static void WearStart(Action_ReduceUses __instance, out int __state)
    {
        __state = -1;
        if (!Suns.Green || !ModConfig.SunBlueWear.Value || !Local(__instance.character)) return;
        var data = __instance.item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (data.HasData) __state = data.Value;
    }

    /// <summary>Green: every use costs two.</summary>
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

/// <summary>
/// Items today's sun weakens show their name in red (hover and pockets), so new scouts can see
/// what not to lean on: blue - healing items, red - glider, balloons and jetpack, yellow - stamina
/// rushes, green - ropes, pitons and anything with limited uses. Only while the sun can touch you.
/// </summary>
[HarmonyPatch]
internal static class SunMarks
{
    private static readonly Dictionary<ushort, RelicGem?> kinds = new();

    /// <summary>Which sun weakens this kind of item (null = none).</summary>
    private static RelicGem? KindOf(Item item)
    {
        if (kinds.TryGetValue(item.itemID, out var k)) return k;
        var tree = Trees.Classify(item);
        if (Relics.IsHealing(item) && !Relics.IsLantern(item)) k = RelicGem.Tenacity;
        else if (tree is TreeId.Glider or TreeId.Balloons || item.GetComponent<JetpackItem>() != null) k = RelicGem.Initiative;
        else if (item.GetComponents<Action_ApplyAffliction>().Any(a => a.affliction is Peak.Afflictions.Affliction_InfiniteStamina
                 || (a.extraAfflictions != null && a.extraAfflictions.Any(x => x is Peak.Afflictions.Affliction_InfiniteStamina)))) k = RelicGem.Ambition;
        else if (tree is TreeId.Rope or TreeId.Piton || item.totalUses > 1) k = RelicGem.Generosity;
        else k = null;
        if (Items.Find(Trees.BaseRole(TreeId.Rope)) != null) kinds[item.itemID] = k; // only once the item database is up
        return k;
    }

    internal static bool Weakened(Item item) => KindOf(item) switch
    {
        RelicGem.Tenacity => Suns.Blue,
        RelicGem.Initiative => Suns.Red,
        RelicGem.Ambition => Suns.Yellow,
        RelicGem.Generosity => Suns.Green,
        _ => false,
    };

    /// <summary>
    /// A bold red line on any card offering an item today's sun weakens - safe ground or not (you
    /// pick it now, carry it out). Not while you hold that sun's relic: it can't touch you.
    /// The item is found by the card's icon. Relic cards and curios are never marked.
    /// </summary>
    internal static void MarkCard(CardSpec card)
    {
        if (card.Style == CardStyle.Relic || card.Icon == null || card.Withered || card.Body.Contains(CardLine)) return;
        if (!ModConfig.Suns.Value || Suns.Current is not { } sun || (ModConfig.Relics.Value && RunState.Relics.ContainsKey(sun))) return;
        var item = Items.Database.FirstOrDefault(i => i != null && Items.Icon(i) == card.Icon);
        if (item == null || CurioRelics.KindOf(item) != Curio.None || KindOf(item) != sun) return;
        card.Body += $"\n<b><color=#FF4A3A>The {Suns.ColorName(sun).ToLowerInvariant()} {CardLine}</color></b>";
    }

    private const string CardLine = "sun weakens this.";

    [HarmonyPostfix, HarmonyPatch(typeof(Item), nameof(Item.GetItemName)), HarmonyPriority(Priority.Last)]
    private static void Red(Item __instance, ref string __result)
    {
        try
        {
            if (string.IsNullOrEmpty(__result) || __result.StartsWith("<color=#FF4A3A>") || !Weakened(__instance)) return;
            __result = $"<color=#FF4A3A>{__result}</color>";
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Sun mark: {e.Message}"); }
    }
}
