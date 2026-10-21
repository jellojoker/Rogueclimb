using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak;
using Photon.Pun;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>Shared helpers for the blood moon waves (host side).</summary>
internal static class Squad
{
    /// <summary>Scouts that can be hunted: real, alive players (no zombies, bots or the Scoutmaster).</summary>
    public static List<Character> Alive() => Character.AllCharacters
        .Where(c => c != null && c.data != null && !c.isBot && !c.isZombie && !c.data.isScoutmaster && !c.data.dead)
        .ToList();

    /// <summary>Scouts out in the open: alive and not on safe ground (lit campfire / summit). Meteors fall on these.</summary>
    private static Vector3? fire, peak;
    private static float upAt = -99f;

    /// <summary>
    /// Uphill: the campfire ending this section, or the peak once past the last one. With
    /// <paramref name="summit"/> the peak itself first (the mountain's top), the campfire if there's none.
    /// </summary>
    public static Vector3? UpMountain(bool summit)
    {
        if (Time.time - upAt > 5f)
        {
            upAt = Time.time;
            fire = peak = null;
            try
            {
                var map = Zorro.Core.Singleton<MapHandler>.Instance;
                int i = (int)MapHandler.CurrentSegmentNumber;
                if (map != null && i < map.segments.Length && map.segments[i].segmentCampfire is { } f) fire = f.transform.position;
                if (UnityEngine.Object.FindFirstObjectByType<PeakSequence>() is { } p) peak = p.transform.position;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"No way up the mountain found: {e.Message}"); }
        }
        return summit ? peak ?? fire : fire ?? peak;
    }

    public static List<Character> Hunted() => Alive().Where(c => !Sanctuary.Safe(c)).ToList();

    /// <summary>Scouts the hunters (Scoutmaster, zombies, tornado) can see: in the open and not veiled by a relic.</summary>
    public static List<Character> Pursued() => Hunted().Where(c => !Veiled(c)).ToList();

    // --- relic veil (host): a scout whose relic just fired is invisible to the hunters for a moment ---

    private static readonly Dictionary<int, float> veiledUntil = new();

    /// <summary>Host: this player's relic fired.</summary>
    public static void Veil(int actor, float seconds)
    {
        if (seconds <= 0f) return;
        veiledUntil[actor] = Time.time + seconds;
        var c = Alive().FirstOrDefault(x => x.photonView != null && x.photonView.Owner != null && x.photonView.Owner.ActorNumber == actor);
        Plugin.Log.LogInfo($"Relic veil: {(c != null ? c.characterName : $"player {actor}")} hidden for {seconds:0.#}s.");
        BloodTornado.RelicFired(actor, seconds);
    }

    public static bool Veiled(Character c) =>
        c.photonView != null && c.photonView.Owner != null
        && veiledUntil.TryGetValue(c.photonView.Owner.ActorNumber, out float until) && Time.time < until;

    /// <summary>Only one scout in the run.</summary>
    public static bool Solo => !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.PlayerCount <= 1;

    public static Character? Of(Photon.Realtime.Player p) => Alive().FirstOrDefault(c => c.photonView != null && c.photonView.Owner == p);

    /// <summary>How close (units) a scout must stand to a relic holder to share their danger.</summary>
    public static float Aura => Mathf.Max(1f, ModConfig.BmRelicAura.Value) * M;

    /// <summary>Does anyone in the squad carry a relic (alive or not, safe or not)?</summary>
    public static bool AnyHolders() => RunState.RelicHolders().Any(h => h.count > 0);

    /// <summary>Relic holders the blood moon may go after (alive, not on safe ground).</summary>
    public static List<Character> Holders() => RunState.RelicHolders()
        .Where(h => h.count > 0)
        .Select(h => Of(h.player))
        .Where(c => c != null && !Sanctuary.Safe(c) && !Veiled(c))
        .Select(c => c!)
        .ToList();

    /// <summary>
    /// Who the Scoutmaster and the tornado may hurt: everyone on open ground who isn't veiled by a
    /// relic - relic holders first (see <see cref="Priority"/>).
    /// </summary>
    public static List<Character> Marked() => Pursued();

    /// <summary>Sort key: relic holders (and skull / dagger carriers) before everyone else.</summary>
    public static int Priority(Character c) => HoldsRelic(c) || CarriesRite(c) ? 0 : 1;

    /// <summary>Carries at least one relic (copies included).</summary>
    public static bool HoldsRelic(Character c) =>
        c.photonView != null && c.photonView.Owner != null && RunState.RelicsOf(c.photonView.Owner).Count > 0;

    /// <summary>Has the Cursed Skull or the Ritual Dagger in their pockets or hand.</summary>
    public static bool CarriesRite(Character c)
    {
        var p = c.player;
        if (p == null || p.itemSlots == null) return false;
        var slots = p.tempFullSlot != null ? p.itemSlots.Append(p.tempFullSlot) : p.itemSlots;
        return slots.Any(s => s != null && !s.IsEmpty() && s.prefab != null && (Items.IsSkull(s.prefab) || Items.IsDagger(s.prefab)));
    }

    /// <summary>A holder with a buddy (any living scout within the aura).</summary>
    public static bool Grouped(Character holder)
    {
        float aura = Aura;
        return Alive().Any(o => o != holder && Vector3.Distance(o.Center, holder.Center) <= aura);
    }

    /// <summary>Metres -> game units.</summary>
    internal const float M = 1f / 1.6f;

    /// <summary>Standable ground near a point: a ring of radius min..max around it, within ±reach units of its height.</summary>
    public static Vector3? GroundNear(Vector3 around, float min, float max, float reach)
    {
        for (int i = 0; i < 16; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(min, max);
            var from = new Vector3(around.x + d.x, around.y + reach, around.z + d.y);
            if (Physics.Raycast(from, Vector3.down, out var hit, reach * 2f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.55f)
                return hit.point + Vector3.up * 1.2f;
        }
        return null;
    }

    /// <summary>Like <see cref="GroundNear"/>, but in one direction (degrees around Y, ± spread).</summary>
    public static Vector3? GroundToward(Vector3 around, float angle, float spread, float min, float max, float reach)
    {
        for (int i = 0; i < 12; i++)
        {
            float a = (angle + Random.Range(-spread, spread)) * Mathf.Deg2Rad;
            float r = Random.Range(min, max);
            var from = new Vector3(around.x + Mathf.Sin(a) * r, around.y + reach, around.z + Mathf.Cos(a) * r);
            if (Physics.Raycast(from, Vector3.down, out var hit, reach * 2f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.55f)
                return hit.point + Vector3.up * 1.2f;
        }
        return null;
    }
}

// =====================================================================================================
// 2 relics - zombies rise beside every scout carrying a relic, the skull or the dagger
// =====================================================================================================

internal static class BloodZombies
{
    private static readonly List<MushroomZombie> spawned = new();
    private static float until;
    private static string? prefab;

    /// <summary>The game's NPC zombie prefab (falls back to the player-zombie one, which also works as an NPC).</summary>
    private static string Prefab()
    {
        if (prefab != null) return prefab;
        foreach (string n in new[] { "MushroomZombie", "MushroomZombie_NPC", "Zombie_NPC", "Zombie" })
        {
            var go = Resources.Load<GameObject>(n);
            if (go != null && go.GetComponent<MushroomZombie>() != null) return prefab = n;
        }
        var spawner = Resources.FindObjectsOfTypeAll<MushroomZombieSpawner>().FirstOrDefault(s => s.mushroomZombiePrefab != null);
        prefab = spawner != null ? spawner.mushroomZombiePrefab.gameObject.name : "MushroomZombie_Player";
        Plugin.Log.LogInfo($"Blood moon zombie prefab: {prefab}");
        return prefab;
    }

    /// <summary>
    /// Zombies rise right beside every scout out in the open (ZombiesPerScout each); scouts
    /// carrying a relic (copies included), the Cursed Skull or the Ritual Dagger get
    /// ZombiesPerCarrier more. Solo, a carrier gets ZombiesSoloCarrier on the zombies' first tier (later tiers
    /// bring the Scoutmaster, tornado and meteors instead).
    /// </summary>
    public static void Spawn(float seconds)
    {
        var marked = Squad.Alive().Where(c => !Sanctuary.Safe(c) && Modless.HasMod(c)).ToList();
        if (marked.Count == 0) return;
        until = Time.time + seconds;
        float near = Mathf.Max(1f, ModConfig.BmZombieNear.Value) * Squad.M;
        int made = 0;
        foreach (var scout in marked)
        {
            int each = Mathf.Max(0, ModConfig.BmZombiesPerScout.Value);
            if (Squad.Priority(scout) == 0)
                each = Squad.Solo && BloodMoon.TierOf(BloodMoon.Hazard.Zombies) <= 1 ? Mathf.Max(0, ModConfig.BmZombiesSoloCarrier.Value)
                       : each + Mathf.Max(0, ModConfig.BmZombiesPerCarrier.Value);
            if (each <= 0) continue;
            // Spread evenly around the scout (two = opposite sides), so they're flanked, not walled in.
            float start = Random.Range(0f, 360f);
            for (int n = 0; n < each; n++)
            {
                float angle = start + n * 360f / each;
                var pos = Squad.GroundToward(scout.Center, angle, 360f / each * 0.4f, near * 0.75f, near * 1.25f, 4f * Squad.M)
                          ?? Squad.GroundNear(scout.Center, near, near * 2f, 8f * Squad.M);
                if (pos == null) continue;
                try
                {
                    var go = PhotonNetwork.Instantiate(Prefab(), pos.Value, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 0);
                    var z = go.GetComponent<MushroomZombie>();
                    if (z == null) continue;
                    spawned.Add(z);
                    z.StartCoroutine(Wake(z));
                    made++;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"Zombie spawn failed: {e.Message}");
                    goto done;
                }
            }
        }
        done:
        Plugin.Log.LogInfo($"Blood moon: {made} zombie(s) rose beside {marked.Count} marked scout(s) ({seconds:0}s).");
    }

    /// <summary>NPC zombies sleep until looked at - blood moon zombies get up and come for you.</summary>
    private static IEnumerator Wake(MushroomZombie z)
    {
        yield return new WaitForSeconds(1.6f);
        if (z == null || z.currentState != MushroomZombie.State.Sleeping) yield break;
        z.WakeUpFromSleep();
        var prey = Squad.Pursued().OrderBy(c => Vector3.Distance(c.Center, z.character.Center)).FirstOrDefault();
        if (prey != null) z.SetCurrentTarget(prey, 15f);
    }

    private static float nextSafeCheck;

    public static void HostTick()
    {
        if (spawned.Count == 0) return;
        if (Time.time >= until) { Clear(); return; }
        if (Time.time < nextSafeCheck) return;
        nextSafeCheck = Time.time + 0.5f;
        // Safe ground: a zombie that stumbles into a campfire's circle crumbles; one chasing a
        // scout who reached it turns on someone still out there.
        var hunted = Squad.Pursued();
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            var z = spawned[i];
            if (z == null) { spawned.RemoveAt(i); continue; }
            if (!z.photonView.IsMine || z.character == null) continue;
            try
            {
                if (Sanctuary.Safe(z.character.Center))
                {
                    z.currentState = MushroomZombie.State.Dead;
                    z.DestroyZombie();
                    spawned.RemoveAt(i);
                    Plugin.Log.LogInfo("Blood moon zombie crumbled on safe ground.");
                    continue;
                }
                if (z.currentTarget != null && (Sanctuary.Safe(z.currentTarget) || Squad.Veiled(z.currentTarget)))
                {
                    var next = hunted.OrderBy(c => Vector3.Distance(c.Center, z.character.Center)).FirstOrDefault();
                    z.SetCurrentTarget(next, next != null ? 15f : 0f);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Zombie safe-ground check failed: {e.Message}"); }
        }
    }

    internal static bool IsOurs(MushroomZombie z) => spawned.Contains(z);

    /// <summary>Time's up: they crumble to bones.</summary>
    public static void Clear()
    {
        foreach (var z in spawned)
        {
            if (z == null || !z.photonView.IsMine) continue;
            try
            {
                z.currentState = MushroomZombie.State.Dead;
                z.DestroyZombie();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Zombie despawn failed: {e.Message}"); }
        }
        if (spawned.Count > 0) Plugin.Log.LogInfo("Blood moon zombies crumbled.");
        spawned.Clear();
    }
}

// =====================================================================================================
// A tornado that follows the squad
// =====================================================================================================

/// <summary>
/// The game's Mesa tornado. It normally wanders between Mesa's fixed points; ours rises at the
/// current section's starting campfire, travels up the mountain toward the squad, then slows
/// to a chase once it's close (the host picks where it heads, everyone moves it there). Lives
/// until the moon sets and takes the biome's colour: sand on Mesa, fire in Caldera / Kiln,
/// purple in Gloom / Citadel, snow white everywhere else. Alone (moon tier 1) it's the game's
/// size and strength; sharing the moon it's thinner and weaker. When the scout it hunts fires
/// a relic it lets go of them and veers away until their veil wears off; with nobody to hunt
/// it wanders.
/// </summary>
internal class BloodTornado : MonoBehaviour
{
    internal const string Tag = "prl.bmt";
    internal enum Look { Sand, Snow, Fire, Gloom }
    private const float VanillaSpeed = 15f;
    private static readonly Dictionary<int, BloodTornado> live = new();

    private Tornado tornado = null!;
    private Transform target = null!;
    private float nextPick;
    /// <summary>Its full size (x and z = width, y = height); it grows toward this like the game's does toward 1.</summary>
    private Vector3 size = Vector3.one;
    /// <summary>Host: the scout it's heading for (actor number, 0 = nobody).</summary>
    private int preyActor;
    /// <summary>Host: veering away (a relic fired) or wandering (nobody to hunt) until then.</summary>
    private float veerUntil;
    private bool wandering;

    internal static Look LookHere()
    {
        if (!MapHandler.Exists) return Look.Snow;
        var seg = MapHandler.CurrentSegmentNumber;
        Biome.BiomeType biome;
        try { biome = MapHandler.CurrentMapSegment.biome; }
        catch { biome = Biome.BiomeType.Shore; }
        if (seg is Segment.Caldera or Segment.TheKiln || biome == Biome.BiomeType.Volcano) return Look.Fire;
        if (biome is Biome.BiomeType.Swamp or Biome.BiomeType.Temple) return Look.Gloom;
        if (biome == Biome.BiomeType.Mesa) return Look.Sand;
        return Look.Snow;
    }

    public static void Spawn(float seconds, bool alone)
    {
        var alive = Squad.Alive();
        if (alive.Count == 0) return;
        // It rises where the squad came into this part of the map (the Kiln / Citadel: a small, gentle one).
        bool kiln = MapHandler.Exists && MapHandler.CurrentSegmentNumber == Segment.TheKiln;
        Vector3 pos = EntrySpot(alive) is { } entry ? Squad.GroundNear(entry, 6f, 16f, 30f) ?? entry : FarFallback(alive);
        Look look = LookHere();
        float width = kiln ? Mathf.Clamp(ModConfig.BmTornadoKilnWidth.Value, 0.1f, 3f)
            : alone ? 1f : Mathf.Clamp(ModConfig.BmTornadoSharedWidth.Value, 0.2f, 3f);
        float pull = kiln ? Mathf.Clamp(ModConfig.BmTornadoKilnPull.Value, 0f, 3f)
            : alone ? 1f : Mathf.Clamp(ModConfig.BmTornadoSharedPull.Value, 0f, 3f);
        try
        {
            PhotonNetwork.Instantiate("Tornado", pos, Quaternion.identity, 0, new object[] { Tag, (int)look, seconds, width, pull });
            float d = alive.Min(c => Vector3.Distance(c.Center, pos));
            Plugin.Log.LogInfo($"Blood moon tornado ({look}, {seconds:0}s, {(kiln ? "kiln" : alone ? "alone" : "shared")}: width x{width:0.##}, pull x{pull:0.##}) at {pos}, {d:0}m from the nearest scout.");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Tornado spawn failed: {e.Message}"); }
    }

    /// <summary>The Void's arrival spot is only used this far (or further) from every scout.</summary>
    private const float VoidArrivalMin = 100f * Squad.M;

    /// <summary>
    /// Where the squad came into this part of the map: the game's own spawn point for the section
    /// (the crash site on the beach, the Kiln's way in), else the campfire the section started
    /// from; in the Void, where the squad arrived. Null when it's missing, sits at the map's origin
    /// (a bogus spot), or the squad fell back below it.
    /// </summary>
    private static Vector3? EntrySpot(List<Character> alive)
    {
        try
        {
            var map = Zorro.Core.Singleton<MapHandler>.Instance;
            int i = (int)MapHandler.CurrentSegmentNumber;
            var tried = new List<string>();
            var spots = VoidRun.Active
                ? new (string, Vector3?)[] { ("Void arrival", VoidRun.Arrival) }
                : new (string, Vector3?)[]
                {
                    ("section spawn", i < map.segments.Length && map.segments[i].reconnectSpawnPos is { } rs ? rs.position : null),
                    ("starting campfire", i > 0 && i - 1 < map.segments.Length ? FirePos(map.segments[i - 1].segmentCampfire) : null),
                };
            foreach (var (name, spot) in spots)
            {
                if (spot is not { } s) { tried.Add($"{name}: none"); continue; }
                float d = alive.Min(c => Vector3.Distance(c.Center, s));
                if (s.sqrMagnitude < 1f) { tried.Add($"{name}: bogus {s}"); continue; }
                if (VoidRun.Active && d < VoidArrivalMin) { tried.Add($"{name}: only {d / Squad.M:0}m away"); continue; }
                if (s.y > alive.Min(c => c.Center.y) + 25f) { tried.Add($"{name}: above the squad"); continue; }
                return s;
            }
            Plugin.Log.LogInfo($"Blood tornado: no entry spot ({string.Join(", ", tried)}) - rising far below the squad.");
            return null;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Blood tornado: no entry spot: {e.Message}");
            return null;
        }
    }

    /// <summary>A section campfire's real spot: its Campfire piece (the object itself can sit at the origin).</summary>
    private static Vector3? FirePos(GameObject? go)
    {
        if (go == null) return null;
        var fire = go.GetComponentInChildren<Campfire>(true);
        return fire != null ? fire.transform.position : go.transform.position;
    }

    /// <summary>No entry spot: 150-250m from the squad, below them and out of everyone's sight if possible.</summary>
    private static Vector3 FarFallback(List<Character> alive)
    {
        var low = alive.OrderBy(c => c.Center.y).First().Center;
        float min = 150f * Squad.M, max = 250f * Squad.M;
        Vector3? seen = null;
        for (int i = 0; i < 6; i++)
        {
            var spot = Squad.GroundNear(low + Vector3.down * (40f * Squad.M), min, max, 90f) ?? Squad.GroundNear(low, min, max, 90f);
            if (spot is not { } s) continue;
            if (!ScoutmasterHunt.SomeoneSees(s + Vector3.up * 3f)) return s;
            seen ??= s;
        }
        return seen ?? low + Vector3.forward * max;
    }

    public static void SetTarget(int viewId, Vector3 pos)
    {
        if (live.TryGetValue(viewId, out var t) && t != null) t.target.position = pos;
    }

    /// <summary>A blood moon tornado is carrying this scout right now.</summary>
    public static bool Carrying(Character c) =>
        live.Values.Any(t => t != null && t.tornado != null && t.tornado.caughtCharacters.Contains(c));

    public static void KillAll()
    {
        foreach (var t in live.Values.ToList())
            if (t != null && t.tornado.view != null && t.tornado.view.IsMine)
                t.tornado.view.RPC("RPCA_TornadoDie", RpcTarget.All);
    }

    /// <summary>
    /// Host: this scout's relic fired. A tornado carrying them throws them out (the game's own
    /// release, with its few seconds of not being caught again); one heading for them veers
    /// away for as long as they stay veiled.
    /// </summary>
    public static void RelicFired(int actor, float seconds)
    {
        foreach (var t in live.Values.ToList())
        {
            if (t == null || t.tornado == null || t.tornado.view == null || !t.tornado.view.IsMine) continue;
            foreach (var c in t.tornado.caughtCharacters.ToList())
                if (c != null && c.photonView != null && c.photonView.Owner != null && c.photonView.Owner.ActorNumber == actor)
                {
                    t.tornado.view.RPC("RPCA_ThrowPlayer", RpcTarget.All, c.photonView.ViewID);
                    Plugin.Log.LogInfo($"Blood tornado: {c.characterName}'s relic threw them out.");
                }
            if (t.kiln || t.preyActor != actor) continue;
            var scout = Squad.Alive().FirstOrDefault(c => c.photonView != null && c.photonView.Owner != null && c.photonView.Owner.ActorNumber == actor);
            t.Veer(scout != null ? scout.Center : (Vector3?)null, seconds);
            Plugin.Log.LogInfo($"Blood tornado: veers away from {(scout != null ? scout.characterName : $"player {actor}")} for {seconds:0.#}s.");
        }
    }

    /// <summary>
    /// Host: head for a spot VeerMin-VeerMax away - angled away from <paramref name="from"/> (up to
    /// 60° either side), or any direction - and hold that course for the given time.
    /// </summary>
    private void Veer(Vector3? from, float seconds)
    {
        Vector3 here = transform.position;
        Vector3 away = from is { } f ? here - f : Vector3.zero;
        away.y = 0f;
        float yaw = away.sqrMagnitude > 0.01f
            ? Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg + Random.Range(-60f, 60f)
            : Random.Range(0f, 360f);
        float lo = Mathf.Max(5f, ModConfig.BmTornadoVeerMin.Value), hi = Mathf.Max(lo, ModConfig.BmTornadoVeerMax.Value);
        float dist = Random.Range(lo, hi) * Squad.M;
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 spot = Squad.GroundToward(here, yaw, 30f, dist * 0.9f, dist * 1.1f, 40f * Squad.M) ?? here + dir * dist;
        preyActor = 0;
        veerUntil = Time.time + seconds;
        nextPick = veerUntil;
        Net.TornadoTarget(tornado.view.ViewID, spot);
    }

    // --- the Kiln / Citadel: a tower, so it keeps to the middle and rises with the squad ----------

    /// <summary>Kiln / Citadel (everyone's copy): no hunting - it drifts around the tower's middle.</summary>
    private bool kiln;
    /// <summary>Its drifting pace in a tower, as a share of the game's tornado speed.</summary>
    private const float TowerDrift = 0.5f;
    /// <summary>How far from the tower's middle it roams, as a share of the tower's radius.</summary>
    private const float TowerRoam = 0.75f;
    private Vector3? towerCentre;
    private float towerRadius, nextScan, nextRoam;
    private Vector2 roam;
    private bool roaming;

    /// <summary>
    /// Kiln / Citadel (everyone's copy): its funnel's middle sits at the lowest scout, so the base is
    /// lowered by half its height - measured from its renderers. The game's pull and catch are
    /// measured from the middle too (Patches.RaiseCore).
    /// </summary>
    private float height, lift, nextMeasure;
    private const float FallbackHeight = 40f;

    private void Measure()
    {
        if (Time.time < nextMeasure) return;
        nextMeasure = Time.time + 1f;
        float top = float.MinValue;
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r != null && r.enabled && r.gameObject.activeInHierarchy) top = Mathf.Max(top, r.bounds.max.y);
        float scale = transform.localScale.y;
        if (top > float.MinValue && scale > 0.5f)
        {
            float h = (top - transform.position.y) / scale;
            if (h > 5f && h < 400f && h > height + 1f)
            {
                height = h;
                Plugin.Log.LogInfo($"Blood tornado (tower): {height:0}m tall - its middle follows the lowest scout.");
            }
        }
        lift = (height > 0f ? height : FallbackHeight) * Mathf.Clamp01(scale) * 0.5f;
    }

    /// <summary>
    /// Host, in a tower: every couple of seconds it measures the tower around itself at the lowest
    /// scout's height and roams across most of it (up to three quarters of its radius from the
    /// middle), a new spot when it gets there. It sends the lowest scout's height; each copy sinks
    /// its own base below that (Move).
    /// </summary>
    private void Circle()
    {
        if (Time.time < nextScan) return;
        nextScan = Time.time + 2f;
        var alive = Squad.Alive();
        if (alive.Count == 0) return;
        var low = alive.OrderBy(c => c.Center.y).First().Center;
        var from = new Vector3(transform.position.x, low.y + 0.5f, transform.position.z);
        if ((TowerScan(from) ?? TowerScan(low + Vector3.up * 0.5f)) is { } scan)
        {
            if (towerCentre == null) Plugin.Log.LogInfo($"Blood tornado (tower): middle {scan.centre}, {scan.radius * 2f / Squad.M:0}m across.");
            towerCentre = scan.centre;
            towerRadius = scan.radius;
        }
        Vector3 mid = towerCentre ?? from;
        Vector2 here = new(transform.position.x, transform.position.z);
        if (!roaming || Time.time >= nextRoam || Vector2.Distance(here, roam) < 3f * Squad.M)
        {
            roam = new Vector2(mid.x, mid.z) + Random.insideUnitCircle * (towerRadius * TowerRoam);
            roaming = true;
            nextRoam = Time.time + 12f;
        }
        Net.TornadoTarget(tornado.view.ViewID, new Vector3(roam.x, low.y, roam.y));
    }

    /// <summary>Rays out to the walls in a full circle: the middle of what they hit and its radius. Null when too few hit (not inside a tower).</summary>
    private static (Vector3 centre, float radius)? TowerScan(Vector3 from)
    {
        const int Rays = 16;
        float reach = 150f * Squad.M;
        var hits = new List<Vector3>();
        for (int i = 0; i < Rays; i++)
        {
            var dir = Quaternion.Euler(0f, i * 360f / Rays, 0f) * Vector3.forward;
            if (Physics.Raycast(from, dir, out var hit, reach, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
                hits.Add(hit.point);
        }
        if (hits.Count < Rays * 0.6f) return null;
        var centre = new Vector3(hits.Average(h => h.x), from.y, hits.Average(h => h.z));
        float radius = hits.Average(h => new Vector2(h.x - centre.x, h.z - centre.z).magnitude);
        return (centre, radius);
    }

    private void Init(Tornado t, Look look, float seconds, float width, float pull)
    {
        tornado = t;
        kiln = MapHandler.Exists && MapHandler.CurrentSegmentNumber == Segment.TheKiln;
        target = new GameObject("Blood Tornado Target").transform;
        target.position = transform.position;
        tornado.target = target;
        tornado.lifeTime = seconds;
        size = new Vector3(width, 1f, width);
        tornado.force *= pull;
        // In a tower: catchable a little further from its middle than its small width allows.
        if (kiln) tornado.captureDistance = Mathf.Max(1f, ModConfig.BmTornadoKilnCatch.Value) / Mathf.Max(0.05f, width);
        live[t.view.ViewID] = this;
        switch (look)
        {
            case Look.Snow: Tint.Snow(gameObject); break;
            case Look.Fire: Tint.Fire(gameObject); break;
            case Look.Gloom: Tint.Gloom(gameObject); break;
        }
    }

    // --- breather: a scout it just threw (or pinned to a wall) is left alone for a while -----------

    /// <summary>Everyone's copy: scouts no blood tornado may pull or catch until then.</summary>
    private static readonly Dictionary<Character, float> spared = new();

    private static float Grace => Mathf.Max(0f, ModConfig.BmTornadoThrowGrace.Value);
    private static bool Spared(Character c) => c != null && spared.TryGetValue(c, out float t) && t > Time.time;

    /// <summary>Who it may hunt: the marked scouts, minus any catching their breath.</summary>
    private static List<Character> Huntable()
    {
        var list = Squad.Marked();
        list.RemoveAll(Spared);
        return list;
    }

    /// <summary>Everyone: it let go of this scout. Host: it goes after someone else, or backs off if there's nobody.</summary>
    private void Thrown(Character? c)
    {
        if (c == null) return;
        if (c.IsLocal && !kiln) ThrowUphill(c); // in a tower "uphill" is straight into the wall
        Forget(c);
        if (Grace <= 0f) return;
        foreach (var gone in spared.Where(kv => kv.Key == null || kv.Value <= Time.time).Select(kv => kv.Key).ToList()) spared.Remove(gone);
        spared[c] = Time.time + Grace;
        if (tornado == null || tornado.view == null || !tornado.view.IsMine) return;
        if (kiln) { Plugin.Log.LogInfo($"Blood tornado (tower): let go of {c.characterName} - spared {Grace:0}s."); return; }
        if (Huntable().Count == 0)
        {
            Veer(c.Center, Grace);
            Plugin.Log.LogInfo($"Blood tornado: let go of {c.characterName} - backs off for {Grace:0}s.");
        }
        else
        {
            nextPick = 0f;
            Plugin.Log.LogInfo($"Blood tornado: let go of {c.characterName} - leaves them alone for {Grace:0}s.");
        }
    }

    /// <summary>
    /// The thrown scout's own game: the throw carries on at the same speed, but toward the campfire
    /// ending this section (uphill) and a little upward, instead of wherever the spin pointed.
    /// </summary>
    private static void ThrowUphill(Character c)
    {
        if (Squad.UpMountain(summit: false) is not { } top || c.refs?.ragdoll?.partList is not { Count: > 0 } parts) return;
        Vector3 dir = top - c.Center;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1f) return;
        dir.Normalize();
        Vector3 avg = Vector3.zero;
        foreach (var p in parts) avg += p.Rig.linearVelocity;
        avg /= parts.Count;
        float speed = new Vector2(avg.x, avg.z).magnitude;
        Vector3 flat = dir * speed;
        float lift = speed * 0.25f;
        foreach (var p in parts)
        {
            var v = p.Rig.linearVelocity;
            p.Rig.linearVelocity = new Vector3(flat.x, Mathf.Max(v.y, avg.y) + lift, flat.z);
        }
        Plugin.Log.LogInfo($"Blood tornado threw you uphill at {speed:0.#} u/s.");
    }

    /// <summary>
    /// Host: nobody gets stuck in it. Thrown loose - the game's own release - with the breather when
    /// it has held a scout too long without a throw (pinned to a wall by the spin), when a scout in
    /// its pull has barely moved for a few seconds, or when they've spent too long in its pull at all.
    /// </summary>
    private void CheckPinned()
    {
        float pullAt = transform.localScale.x * tornado.range;
        Vector3 core = transform.position + Vector3.up * (kiln ? lift : 0f);
        float heldMax = Mathf.Max(1f, ModConfig.BmTornadoHeldMax.Value);
        float pinnedMax = Mathf.Max(0.5f, ModConfig.BmTornadoPinned.Value);
        float nearMax = Mathf.Max(heldMax, ModConfig.BmTornadoNearMax.Value);
        float slow = Mathf.Max(0f, ModConfig.BmTornadoPinnedSpeed.Value);
        float dt = Time.deltaTime;
        foreach (var c in Squad.Alive())
        {
            float h = c.Center.y - core.y;
            bool inPull = !Spared(c) && !Sanctuary.Safe(c) && h > -10f && h < 50f && HelperFunctions.FlatDistance(core, c.Center) < pullAt;
            if (!inPull) { held.Remove(c); still.Remove(c); continue; }
            bool caught = tornado.caughtCharacters.Contains(c);
            float heldFor = caught ? Add(held, c, dt) : Drop(held, c);
            float stillFor = c.data.avarageVelocity.magnitude < slow ? Add(still, c, dt) : Drop(still, c);
            float nearFor = Add(near, c, dt);
            string? why = heldFor >= heldMax ? $"held {heldFor:0.#}s without a throw"
                : stillFor >= pinnedMax ? $"pinned {stillFor:0.#}s{(caught ? " (caught)" : "")}"
                : nearFor >= nearMax ? $"in its pull {nearFor:0.#}s"
                : null;
            if (why == null) continue;
            Forget(c);
            Plugin.Log.LogInfo($"Blood tornado: {c.characterName} {why} - thrown loose.");
            tornado.view.RPC("RPCA_ThrowPlayer", RpcTarget.All, c.photonView.ViewID);
        }
    }

    /// <summary>Host: how long each scout has been held / barely moving in its pull / in its pull at all (since their last throw).</summary>
    private readonly Dictionary<Character, float> held = new(), still = new(), near = new();

    private static float Add(Dictionary<Character, float> d, Character c, float dt)
    {
        d.TryGetValue(c, out float t);
        return d[c] = t + dt;
    }

    private static float Drop(Dictionary<Character, float> d, Character c)
    {
        d.Remove(c);
        return 0f;
    }

    private void Forget(Character c)
    {
        held.Remove(c);
        still.Remove(c);
        near.Remove(c);
    }

    private void Update()
    {
        if (tornado == null || !tornado.view.IsMine) return;
        CheckPinned();
        if (kiln) { Circle(); return; }
        if (Time.time < nextPick) return;
        var alive = Huntable();
        if (alive.Count == 0)
        {
            // Nobody it can hunt (all safe or veiled): it wanders off somewhere, like the game's does.
            if (!wandering || Time.time >= veerUntil || Vector3.Distance(transform.position, target.position) < 10f * Squad.M)
                Veer(null, Random.Range(8f, 15f));
            wandering = true;
            nextPick = Time.time + 1f;
            return;
        }
        wandering = false;
        veerUntil = 0f;
        // Far away it heads straight for the nearest scout; close up it picks prey every few seconds.
        bool far = Far(alive);
        nextPick = Time.time + (far ? 2f : Random.Range(6f, 12f));
        // Relic holders first: it heads for the nearest one, and up close favours them two to one.
        var weighted = alive.SelectMany(c => Squad.Priority(c) == 0 ? new[] { c, c } : new[] { c }).ToList();
        var prey = far ? alive.OrderBy(Squad.Priority).ThenBy(c => Vector3.Distance(c.Center, transform.position)).First()
            : weighted[Random.Range(0, weighted.Count)];
        Vector2 jitter = far ? Vector2.zero : Random.insideUnitCircle * 8f;
        preyActor = prey.photonView != null && prey.photonView.Owner != null ? prey.photonView.Owner.ActorNumber : 0;
        Net.TornadoTarget(tornado.view.ViewID, prey.Center + new Vector3(jitter.x, 0f, jitter.y));
    }

    /// <summary>Veering or wandering (host).</summary>
    private bool Drifting => wandering || Time.time < veerUntil;

    private float Nearest(List<Character> alive) =>
        alive.Count == 0 ? float.MaxValue : alive.Min(c => Vector3.Distance(c.Center, transform.position));

    private float speed = -1f;
    private float nextSpeedCheck;

    /// <summary>
    /// Fast while it travels up from the campfire, slow once it's on the squad - and slower still
    /// when the relic holder it's nearest to has company.
    /// </summary>
    /// <summary>Further than the chase distance from every scout it may hunt: it rushes in.</summary>
    private bool Far(List<Character> marked) => Nearest(marked) > ModConfig.BmTornadoNear.Value * Squad.M;

    /// <summary>
    /// Close up it moves a little slower than a sprinting scout (a sprint gets away, a jog doesn't);
    /// under a red sun sprints are cut, so it slows by the same share.
    /// </summary>
    private static float ChaseSpeed()
    {
        float sprint = Mathf.Max(1f, ModConfig.BmTornadoSprintRef.Value);
        float red = Suns.Red ? Mathf.Clamp01(ModConfig.SunRedSprintSpeed.Value) : 1f;
        return sprint * Mathf.Clamp(ModConfig.BmTornadoSprintShare.Value, 0.1f, 2f) * red;
    }

    private float Speed()
    {
        if (kiln) return VanillaSpeed * TowerDrift;
        if (speed < 0f || Time.time >= nextSpeedCheck)
        {
            nextSpeedCheck = Time.time + 0.5f;
            var marked = Huntable();
            if (Drifting)
            {
                // Veering off or wandering: its chase pace, not the rush up from the campfire.
                speed = ChaseSpeed();
                return speed;
            }
            speed = Far(marked) ? ModConfig.BmTornadoApproach.Value : ChaseSpeed();
            var holder = Squad.Holders().OrderBy(c => Vector3.Distance(c.Center, transform.position)).FirstOrDefault();
            if (holder != null && Squad.Grouped(holder)) speed *= Mathf.Clamp(ModConfig.BmTornadoGrouped.Value, 0.05f, 1f);
        }
        return speed;
    }

    private static readonly AccessTools.FieldRef<Tornado, Vector3> VelRef = AccessTools.FieldRefAccess<Tornado, Vector3>("vel");
    private static readonly AccessTools.FieldRef<Tornado, Vector3> PosRef = AccessTools.FieldRefAccess<Tornado, Vector3>("tornadoPos");

    /// <summary>
    /// The game's movement at our speed. It also follows the ground up and down the mountain
    /// (the vanilla one only moves flat, so it would be left floating far below the squad).
    /// </summary>
    private void Move()
    {
        if (target == null) return;
        ref Vector3 vel = ref VelRef(tornado);
        ref Vector3 pos = ref PosRef(tornado);
        Vector3 flat = target.position - pos;
        flat.y = 0f;
        vel = Vector3.Lerp(vel, flat.normalized * Speed(), 1f - Mathf.Pow(0.85f, Time.deltaTime * 60f));
        pos += vel * Time.deltaTime;
        // It never enters safe ground: held at the edge of the campfire's circle (plus its own width).
        if (Sanctuary.Nearest(pos, out var safe, out float r))
        {
            Vector3 away = pos - safe;
            away.y = 0f;
            float keep = r + 12f * Squad.M;
            if (away.magnitude < keep && Mathf.Abs(pos.y - safe.y) < keep + 20f * Squad.M)
            {
                Vector3 dir = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                pos = new Vector3(safe.x, pos.y, safe.z) + dir * keep;
                vel = Vector3.zero;
            }
        }
        if (kiln)
        {
            // In a tower it floats up (and down) with the squad, wherever the floor is.
            // Its target is the lowest scout's height: the base sinks below it so the funnel's middle is there.
            pos.y = Mathf.MoveTowards(pos.y, target.position.y - lift, Time.deltaTime * 20f);
        }
        else
        {
            // Look for ground from just above the higher of it / its prey (low enough to stay under the Kiln's roof).
            float top = Mathf.Max(pos.y, target.position.y) + 15f;
            if (Physics.Raycast(new Vector3(pos.x, top, pos.z), Vector3.down, out var hit, top - pos.y + 120f,
                    HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
                pos.y = Mathf.MoveTowards(pos.y, hit.point.y, Time.deltaTime * 40f);
        }
        transform.position = pos;
    }

    private void OnDestroy()
    {
        if (target != null) Destroy(target.gameObject);
        if (tornado != null && tornado.view != null) live.Remove(tornado.view.ViewID);
    }

    [HarmonyPatch]
    private static class Patches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(Tornado), "Start")]
        private static void Started(Tornado __instance)
        {
            var data = __instance.view != null ? __instance.view.InstantiationData : null;
            if (data == null || data.Length < 3 || data[0] as string != Tag) return;
            Look look = data[1] is int i ? (Look)i : Look.Snow;
            float width = data.Length > 3 && data[3] is float w ? w : 1f;
            float pull = data.Length > 4 && data[4] is float p ? p : 1f;
            __instance.gameObject.AddComponent<BloodTornado>().Init(__instance, look, (float)data[2], width, pull);
        }

        private static Vector3 grownFrom;

        /// <summary>The game grows every tornado toward scale 1; ours grows at the same pace toward its own size.</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(Tornado), "Update")]
        private static void BeforeGrow(Tornado __instance) => grownFrom = __instance.transform.localScale;

        [HarmonyPostfix, HarmonyPatch(typeof(Tornado), "Update")]
        private static void Grow(Tornado __instance)
        {
            if (__instance == null || __instance.dying) return;
            var bt = __instance.GetComponent<BloodTornado>();
            if (bt == null || bt.size == Vector3.one) return;
            __instance.transform.localScale = Vector3.Lerp(grownFrom, bt.size, Time.deltaTime * 0.25f);
        }

        /// <summary>
        /// In a tower the base sits half a funnel below the lowest scout: for the game's pull, catch
        /// and spin (FixedUpdate) it stands at its middle instead, then goes back before anything draws.
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(Tornado), "FixedUpdate")]
        private static void RaiseCore(Tornado __instance, out float __state)
        {
            __state = 0f;
            var bt = __instance.GetComponent<BloodTornado>();
            if (bt == null || !bt.kiln) return;
            bt.Measure();
            __state = bt.lift;
            __instance.transform.position += Vector3.up * __state;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(Tornado), "FixedUpdate")]
        private static void LowerCore(Tornado __instance, float __state)
        {
            if (__state != 0f && __instance != null) __instance.transform.position -= Vector3.up * __state;
        }

        /// <summary>Every throw (its own, a stuck release, a relic) starts the thrown scout's breather.</summary>
        [HarmonyPostfix, HarmonyPatch(typeof(Tornado), "RPCA_ThrowPlayer")]
        private static void Threw(Tornado __instance, int targetView)
        {
            var bt = __instance.GetComponent<BloodTornado>();
            if (bt == null) return;
            var view = PhotonView.Find(targetView);
            bt.Thrown(view != null ? view.GetComponent<Character>() : null);
        }

        private static readonly List<Character> spareAdded = new();

        /// <summary>
        /// Scouts catching their breath, and scouts on safe ground (a campfire's circle), are neither
        /// pulled nor caught (the game's own ignore list, for longer).
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(Tornado), "AttractCharacters")]
        private static void Spare(Tornado __instance)
        {
            spareAdded.Clear();
            if (__instance.GetComponent<BloodTornado>() == null) return;
            foreach (var c in Character.AllCharacters)
                if (c != null && !__instance.ignoredCharacters.Contains(c)
                    && ((spared.TryGetValue(c, out float until) && until > Time.time) || Sanctuary.Safe(c)))
                {
                    __instance.ignoredCharacters.Add(c);
                    spareAdded.Add(c);
                }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Tornado), "AttractCharacters")]
        private static void Unspare(Tornado __instance)
        {
            foreach (var c in spareAdded) __instance.ignoredCharacters.Remove(c);
            spareAdded.Clear();
        }

        /// <summary>Ours has no Mesa waypoints - the host steers it instead.</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(Tornado), "TargetSelection")]
        private static bool OwnSteering(Tornado __instance) => __instance.GetComponent<BloodTornado>() == null;

        [HarmonyPrefix, HarmonyPatch(typeof(Tornado), "Movement")]
        private static bool OwnMovement(Tornado __instance)
        {
            var bt = __instance.GetComponent<BloodTornado>();
            if (bt == null || bt.tornado == null) return true;
            try { bt.Move(); }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Blood tornado movement failed, using vanilla: {e.Message}");
                Object.Destroy(bt);
                return true;
            }
            return false;
        }
    }
}

/// <summary>
/// Blood moon zombies can't see scouts on safe ground or veiled by a relic: the game's own AI
/// re-picks the nearest scout every few seconds and would walk (and lunge) right back at them.
/// </summary>
[HarmonyPatch]
internal static class BloodZombiePatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(MushroomZombie), "TargetIsValid")]
    private static void NotOnSafeGround(MushroomZombie __instance, Character target, ref bool __result)
    {
        if (!__result || target == null || !BloodZombies.IsOurs(__instance)) return;
        if (Sanctuary.Safe(target) || Squad.Veiled(target)) __result = false;
    }
}
