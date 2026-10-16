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
    /// Who the Scoutmaster and the tornado may hurt: relic holders and anyone standing within the
    /// aura of one (a veiled holder casts none). With no relics in the squad at all (a dark rite),
    /// everyone on open ground. Veiled scouts are never marked.
    /// </summary>
    public static List<Character> Marked()
    {
        var hunted = Pursued();
        if (!AnyHolders()) return hunted;
        var holders = Holders();
        float aura = Aura;
        return hunted.Where(c => holders.Contains(c) || holders.Any(h => Vector3.Distance(h.Center, c.Center) <= aura)).ToList();
    }

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
}

// =====================================================================================================
// 2 relics - a zombie rises beside every scout carrying a relic, the skull or the dagger
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
    /// One zombie rises right beside every scout out in the open who carries a relic (copies
    /// included), the Cursed Skull or the Ritual Dagger.
    /// </summary>
    public static void Spawn(float seconds)
    {
        var marked = Squad.Alive().Where(c => !Sanctuary.Safe(c) && (Squad.HoldsRelic(c) || Squad.CarriesRite(c))).ToList();
        if (marked.Count == 0) return;
        until = Time.time + seconds;
        float near = Mathf.Max(1f, ModConfig.BmZombieNear.Value) * Squad.M;
        int made = 0;
        foreach (var scout in marked)
        {
            var pos = Squad.GroundNear(scout.Center, near * 0.75f, near * 1.25f, 4f * Squad.M)
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
                break;
            }
        }
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
// 3 relics - a tornado that follows the squad
// =====================================================================================================

/// <summary>
/// The game's Mesa tornado. It normally wanders between Mesa's fixed points; ours rises at the
/// current section's starting campfire, travels up the mountain toward the squad, then slows
/// to a chase once it's close (the host picks where it heads, everyone moves it there). Lives
/// 3 minutes and takes the biome's colour: sand on Mesa, fire in Caldera / Kiln, purple in
/// Gloom / Citadel, snow white everywhere else.
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

    public static void Spawn(float seconds)
    {
        var alive = Squad.Alive();
        if (alive.Count == 0) return;
        Vector3 pos = StartCampfire(alive) is { } fire
            ? Squad.GroundNear(fire, 6f, 16f, 30f) ?? fire
            : FarFrom(alive);
        Look look = LookHere();
        try
        {
            PhotonNetwork.Instantiate("Tornado", pos, Quaternion.identity, 0, new object[] { Tag, (int)look, seconds });
            float d = alive.Min(c => Vector3.Distance(c.Center, pos));
            Plugin.Log.LogInfo($"Blood moon tornado ({look}, {seconds:0}s) at {pos}, {d:0}m from the nearest scout.");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Tornado spawn failed: {e.Message}"); }
    }

    /// <summary>
    /// The campfire this section started from (the crash site on the beach) - as long as it's
    /// actually behind / below the squad.
    /// </summary>
    private static Vector3? StartCampfire(List<Character> alive)
    {
        try
        {
            var map = Zorro.Core.Singleton<MapHandler>.Instance;
            int i = (int)MapHandler.CurrentSegmentNumber;
            Vector3? spot = null;
            if (i > 0 && i - 1 < map.segments.Length && map.segments[i - 1].segmentCampfire is { } fire) spot = fire.transform.position;
            else if (i == 0 && map.segments.Length > 0 && map.segments[0].reconnectSpawnPos is { } crash) spot = crash.position;
            if (spot == null) return null;
            float lowest = alive.Min(c => c.Center.y);
            if (spot.Value.y > lowest + 25f) return null; // the squad fell back below it
            return spot;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"No starting campfire for the tornado: {e.Message}");
            return null;
        }
    }

    /// <summary>No campfire to rise from: well away from the squad, below them if possible.</summary>
    private static Vector3 FarFrom(List<Character> alive)
    {
        var low = alive.OrderBy(c => c.Center.y).First().Center;
        return Squad.GroundNear(low + Vector3.down * 30f, 80f, 110f, 90f)
               ?? Squad.GroundNear(low, 70f, 100f, 90f)
               ?? low + Vector3.forward * 80f;
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

    private void Init(Tornado t, Look look, float seconds)
    {
        tornado = t;
        target = new GameObject("Blood Tornado Target").transform;
        target.position = transform.position;
        tornado.target = target;
        tornado.lifeTime = seconds;
        live[t.view.ViewID] = this;
        switch (look)
        {
            case Look.Snow: Tint.Snow(gameObject); break;
            case Look.Fire: Tint.Fire(gameObject); break;
            case Look.Gloom: Tint.Gloom(gameObject); break;
        }
    }

    private void Update()
    {
        if (tornado == null || !tornado.view.IsMine || Time.time < nextPick) return;
        var alive = Squad.Marked();
        if (alive.Count == 0)
        {
            // No relic holder (or anyone near one) on open ground: it lingers where it is.
            nextPick = Time.time + 2f;
            Net.TornadoTarget(tornado.view.ViewID, transform.position);
            return;
        }
        // Far away it heads straight for the nearest scout; close up it picks prey every few seconds.
        bool far = Nearest(alive) > ModConfig.BmTornadoNear.Value;
        nextPick = Time.time + (far ? 2f : Random.Range(6f, 12f));
        var prey = far ? alive.OrderBy(c => Vector3.Distance(c.Center, transform.position)).First() : alive[Random.Range(0, alive.Count)];
        Vector2 jitter = far ? Vector2.zero : Random.insideUnitCircle * 8f;
        Net.TornadoTarget(tornado.view.ViewID, prey.Center + new Vector3(jitter.x, 0f, jitter.y));
    }

    private float Nearest(List<Character> alive) =>
        alive.Count == 0 ? float.MaxValue : alive.Min(c => Vector3.Distance(c.Center, transform.position));

    private float speed = -1f;
    private float nextSpeedCheck;

    /// <summary>
    /// Fast while it travels up from the campfire, slow once it's on the squad - and slower still
    /// when the relic holder it's nearest to has company.
    /// </summary>
    private float Speed()
    {
        if (speed < 0f || Time.time >= nextSpeedCheck)
        {
            nextSpeedCheck = Time.time + 0.5f;
            var marked = Squad.Marked();
            bool far = Nearest(marked) > ModConfig.BmTornadoNear.Value;
            speed = far ? ModConfig.BmTornadoApproach.Value : VanillaSpeed * ModConfig.BmTornadoChase.Value;
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
        // Look for ground from just above the higher of it / its prey (low enough to stay under the Kiln's roof).
        float top = Mathf.Max(pos.y, target.position.y) + 15f;
        if (Physics.Raycast(new Vector3(pos.x, top, pos.z), Vector3.down, out var hit, top - pos.y + 120f,
                HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
            pos.y = Mathf.MoveTowards(pos.y, hit.point.y, Time.deltaTime * 40f);
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
            __instance.gameObject.AddComponent<BloodTornado>().Init(__instance, look, (float)data[2]);
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
