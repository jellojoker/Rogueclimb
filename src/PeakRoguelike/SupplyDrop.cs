using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRoguelike;

public enum DropKind { Plain, Big, Ancient }
public enum DropStyle { Slam, Float }

/// <summary>
/// Supply crates from the sky. Every few minutes out on the mountain (sooner in bigger lobbies) one floats down on balloons
/// near the scout carrying the least (fewest items, then lowest levels); a lit flare slams a big
/// one down beside you (sometimes an Ancient Luggage). The crate is a copy of a crate already in
/// the level, so it carries that crate's loot. Landing spots are picked first: flat ground with
/// open sky above, so the crate never bounces anywhere - it follows a set path down.
/// </summary>
[HarmonyPatch]
internal class SupplyDrop : IPunPrefabPool
{
    private const string Prefix = "prl.SupplyDrop.";
    private const float M = 1f / 1.6f;

    private readonly IPunPrefabPool inner;
    private SupplyDrop(IPunPrefabPool inner) => this.inner = inner;

    /// <summary>Everyone wraps Photon's prefab pool, so the host's crate can be built on every machine.</summary>
    internal static void Install()
    {
        var current = PhotonNetwork.PrefabPool ?? new DefaultPool();
        if (current is SupplyDrop) return;
        PhotonNetwork.PrefabPool = new SupplyDrop(current);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Luggage), "Awake")]
    private static void KeepPool() => Install();

    public GameObject Instantiate(string prefabId, Vector3 position, Quaternion rotation)
    {
        if (!prefabId.StartsWith(Prefix)) return inner.Instantiate(prefabId, position, rotation);
        if (!System.Enum.TryParse(prefabId.Substring(Prefix.Length), out DropKind kind)) kind = DropKind.Plain;
        var template = Template(kind, position);
        if (template == null)
        {
            Plugin.Log.LogWarning("Supply drop: no crate in this level to copy.");
            return null!;
        }
        bool was = template.gameObject.activeSelf;
        if (was) template.gameObject.SetActive(false);
        var go = Object.Instantiate(template.gameObject, position, rotation);
        if (was) template.gameObject.SetActive(true);
        go.name = $"Supply Drop ({template.displayName})";
        // A scene crate's view ids come along with the copy; Photon hands out new ones.
        var idField = AccessTools.Field(typeof(PhotonView), "viewIdField");
        foreach (var v in go.GetComponentsInChildren<PhotonView>(true))
        {
            idField.SetValue(v, 0);
            v.sceneViewId = 0;
        }
        var lug = go.GetComponent<Luggage>();
        lug.state = Luggage.LuggageState.Closed;
        if (lug.activateOnOpen != null) lug.activateOnOpen.SetActive(false);
        go.AddComponent<SkyDrop>();
        Plugin.Log.LogInfo($"Supply drop: built from {template.displayName} ({template.gameObject.name}).");
        return go;
    }

    public void Destroy(GameObject gameObject)
    {
        if (gameObject != null && gameObject.GetComponent<SkyDrop>() != null) Object.Destroy(gameObject);
        else inner.Destroy(gameObject);
    }

    // --- which crate to copy ---------------------------------------------------------------------

    private static int Spots(Luggage l)
    {
        // Not GetSpawnSpots(): weighted crates pick a list at random, and every machine must agree.
        try
        {
            return l.spawnPointMode == Spawner.SpawnPointMode.SingleList
                ? l.spawnSpots?.Count ?? 0
                : l.weightedSpawnSpots?.Select(w => w.spawnSpots?.Count ?? 0).DefaultIfEmpty(0).Max() ?? 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// The crate to copy. Plain: an ordinary Luggage. Big: the kind with the most loot spots.
    /// Ancient: an Ancient Luggage. Nearest first; every machine picks the same one.
    /// </summary>
    private static Luggage? Template(DropKind kind, Vector3 near)
    {
        var all = Object.FindObjectsOfType<Luggage>(true)
            .Where(l => l != null && l.GetType() == typeof(Luggage) && l.GetComponent<SkyDrop>() == null)
            .ToList();
        var plain = all.Where(l => !Blessing.IsAncient(l)).ToList();
        IEnumerable<Luggage> pick = kind switch
        {
            DropKind.Ancient => all.Where(Blessing.IsAncient),
            DropKind.Big => plain.Where(l => Spots(l) == plain.Select(Spots).DefaultIfEmpty(0).Max()),
            _ => plain.Any(l => l.displayName == "Luggage") ? plain.Where(l => l.displayName == "Luggage") : plain,
        };
        var found = pick.OrderBy(l => Vector3.Distance(l.transform.position, near)).FirstOrDefault();
        if (found == null && kind == DropKind.Ancient) return Template(DropKind.Big, near);
        if (found == null && kind == DropKind.Big) return Template(DropKind.Plain, near);
        return found;
    }

    // --- landing spots ---------------------------------------------------------------------------

    /// <summary>
    /// Flat ground (under 30 degrees) with open sky above it, min..max from a point and near its
    /// height. Toward a direction (± 60 degrees) when one is given.
    /// </summary>
    internal static Vector3? Spot(Vector3 around, Vector3 toward, float min, float max)
    {
        toward = Vector3.ProjectOnPlane(toward, Vector3.up);
        bool aimed = toward.sqrMagnitude > 0.01f;
        float baseAngle = aimed ? Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg : 0f;
        float sky = 80f * M;
        for (int i = 0; i < 24; i++)
        {
            float a = (aimed ? baseAngle + Random.Range(-60f, 60f) : Random.Range(0f, 360f)) * Mathf.Deg2Rad;
            float r = Random.Range(min, max);
            var from = new Vector3(around.x + Mathf.Sin(a) * r, around.y + sky, around.z + Mathf.Cos(a) * r);
            // Cast from high above: the first thing hit is the top surface, so the way down is clear.
            if (!Physics.Raycast(from, Vector3.down, out var hit, sky * 2f, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.866f) continue;
            if (Mathf.Abs(hit.point.y - around.y) > 25f * M) continue;
            int layer = hit.collider.gameObject.layer;
            if (layer != LayerMask.NameToLayer("Terrain") && layer != LayerMask.NameToLayer("Map")) continue; // not a tree, a scout or a prop
            return hit.point;
        }
        return null;
    }

    // --- the flare's drop -----------------------------------------------------------------------

    /// <summary>A lit flare at this spot: the host drops a crate beside it.</summary>
    internal static void Request(Vector3 at, Vector3 look, string caller)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient) FlareDrop(at, look, caller);
        else Net.SupplyDrop(at, look, caller);
    }

    internal static void FlareDrop(Vector3 at, Vector3 look, string caller)
    {
        Install();
        bool ancient = Random.value < ModConfig.FlareAncientChance.Value && Template(DropKind.Ancient, at) is { } t && Blessing.IsAncient(t);
        var kind = ancient ? DropKind.Ancient : DropKind.Big;
        if (Template(kind, at) == null) { Hud.Toast("The flare went unanswered", ChoiceScreen.Muted); return; }
        Vector3 ground = Spot(at, look, 6f * M, 10f * M) ?? Below(at + Vector3.ProjectOnPlane(look, Vector3.up).normalized * 8f * M);
        Spawn(kind, DropStyle.Float, ground, caller);
    }

    private static Vector3 Below(Vector3 at) =>
        Physics.Raycast(at + Vector3.up * 2f * M, Vector3.down, out var hit, 40f * M, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore)
            ? hit.point : at;

    private static void Spawn(DropKind kind, DropStyle style, Vector3 ground, string caller, int[]? pickers = null)
    {
        var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        PhotonNetwork.InstantiateRoomObject(Prefix + kind, ground, rot, 0,
            new object[] { PhotonNetwork.Time + SkyDrop.PlaneLead, caller ?? "", (int)style, (int)kind, pickers ?? new int[0] });
        Plugin.Log.LogInfo($"Supply drop: {kind} ({style}) for {(string.IsNullOrEmpty(caller) ? "the squad" : caller)} at {ground}.");
    }

    // --- the timed drop --------------------------------------------------------------------------

    /// <summary>Items carried and their total level: the scout with the least gets the drop.</summary>
    private static (int count, int levels) Carried(Character c)
    {
        var p = c.player;
        if (p == null || p.itemSlots == null) return (0, 0);
        var full = p.itemSlots.Where(s => s != null && !s.IsEmpty()).ToList();
        return (full.Count, full.Sum(ItemLevels.LevelOf));
    }

    /// <summary>
    /// Host: a crate floats down 15-40m ahead of the scout carrying the least. In a big lobby
    /// (GroupDropsFrom+) every group of scouts far from the others gets its own, picked by that
    /// group only. False = no spot yet.
    /// </summary>
    internal static bool TimedDrop()
    {
        Install();
        var alive = Squad.Alive();
        int lobby = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
        if (lobby < Mathf.Max(2, ModConfig.DropClusterScouts.Value)) return DropFor(alive, null);
        var groups = Groups(alive, Mathf.Max(10f, ModConfig.DropClusterMetres.Value) * M);
        if (groups.Count == 1) return DropFor(alive, null);
        Plugin.Log.LogInfo($"Supply drop: {groups.Count} groups - " + string.Join(" / ", groups.Select(g => string.Join(", ", g.Select(c => c.characterName)))) + ".");
        bool any = false;
        foreach (var g in groups)
        {
            // Resting at a campfire: that group's crate waits for the next drop.
            if (g.All(Sanctuary.Safe)) continue;
            var actors = g.Where(c => c.photonView != null && c.photonView.Owner != null).Select(c => c.photonView.Owner.ActorNumber).ToArray();
            any |= DropFor(g, actors);
        }
        return any;
    }

    /// <summary>
    /// Everyone in a group is within this distance of everyone else in it (no chains: a long line
    /// up the slope splits), so a group's crate is never far from any of them. Each group starts
    /// from the scout with the most others close by, then takes the nearest that still fit.
    /// </summary>
    private static List<List<Character>> Groups(List<Character> scouts, float apart)
    {
        bool Near(Character a, Character b) => Vector3.Distance(a.Center, b.Center) <= apart;
        var left = new List<Character>(scouts);
        var groups = new List<List<Character>>();
        while (left.Count > 0)
        {
            var seed = left.OrderByDescending(c => left.Count(o => o != c && Near(c, o))).First();
            var g = new List<Character> { seed };
            left.Remove(seed);
            foreach (var c in left.OrderBy(c => Vector3.Distance(c.Center, seed.Center)).ToList())
                if (g.All(m => Near(m, c))) { g.Add(c); left.Remove(c); }
            groups.Add(g);
        }
        return groups;
    }

    /// <summary>One crate for these scouts. Pickers = who may pick from it (null = the whole lobby).</summary>
    private static bool DropFor(List<Character> scouts, int[]? pickers)
    {
        var outside = scouts.Where(c => !Sanctuary.Safe(c)).ToList();
        if (outside.Count == 0) outside = scouts;
        var target = outside.OrderBy(c => Carried(c).count).ThenBy(c => Carried(c).levels).FirstOrDefault();
        if (target == null || Template(DropKind.Plain, target.Center) == null) return false;
        Vector3 ahead = Vector3.zero;
        if (Singleton<MapHandler>.Instance is { } map && map.segments != null)
        {
            int seg = Mathf.Clamp(map.currentSegment, 0, map.segments.Length - 1);
            if (map.segments[seg].segmentCampfire is { } fire) ahead = fire.transform.position - target.Center;
        }
        var spot = Spot(target.Center, ahead, 15f * M, 40f * M) ?? Spot(target.Center, Vector3.zero, 10f * M, 40f * M);
        if (spot == null) { Plugin.Log.LogInfo($"Supply drop: no clear landing near {target.characterName} yet."); return false; }
        var (n, lv) = Carried(target);
        Plugin.Log.LogInfo($"Supply drop: helping {target.characterName} ({n} items, levels {lv})"
            + (pickers != null ? $", picks for {pickers.Length} scout(s) in that group." : "."));
        Spawn(DropKind.Plain, DropStyle.Float, spot.Value, "", pickers);
        return true;
    }

    /// <summary>F8: the next timed drop, now.</summary>
    internal static void DevTimed()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;
        if (!TimedDrop()) Hud.Toast("No clear landing spot nearby", ChoiceScreen.Muted);
        else DropClock.Reset();
    }

    /// <summary>F8: a flare's drop in front of you.</summary>
    internal static void DevFlare()
    {
        if (Character.localCharacter is not { } me) return;
        Request(me.Center, me.data.lookDirection_Flat, me.characterName);
    }

    // --- the crate keeps quiet while it falls ----------------------------------------------------

    [HarmonyPrefix, HarmonyPatch(typeof(Luggage), nameof(Luggage.Interact))]
    private static bool NoInteractFalling(Luggage __instance) => !SkyDrop.Falling(__instance);

    [HarmonyPrefix, HarmonyPatch(typeof(Luggage), nameof(Luggage.Interact_CastFinished))]
    private static bool NoOpenFalling(Luggage __instance) => !SkyDrop.Falling(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(Luggage), nameof(Luggage.GetName))]
    private static void Name(Luggage __instance, ref string __result)
    {
        if (__instance != null && __instance.GetComponent<SkyDrop>() != null && !Blessing.IsAncient(__instance)) __result = "Supply Drop";
    }
}

/// <summary>
/// Host: counts time out on the mountain (not at a campfire, the crash site or a blood moon) until
/// the next drop. The wait is rolled fresh after every drop from the lobby size's range (Supply config).
/// </summary>
internal class DropClock : MonoBehaviour
{
    private static float clock;
    private static float next = -1f;
    private static int scene = -1;

    /// <summary>"180-360" or "180" for this many scouts in the lobby.</summary>
    private static string RangeFor(int scouts) => scouts switch
    {
        <= 1 => ModConfig.DropSolo.Value,
        2 => ModConfig.DropDuo.Value,
        <= 4 => ModConfig.DropSmall.Value,
        _ => ModConfig.DropBig.Value,
    };

    private static float Roll()
    {
        int scouts = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
        string range = RangeFor(scouts);
        var bits = range.Split('-');
        float min = 180f, max = 180f;
        if (bits.Length > 0 && float.TryParse(bits[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float a)) min = max = a;
        if (bits.Length > 1 && float.TryParse(bits[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float b)) max = b;
        if (max < min) (min, max) = (max, min);
        float s = Mathf.Max(20f, Random.Range(min, max));
        Plugin.Log.LogInfo($"Supply drop: next in {s:0}s ({scouts} scout(s), range {range}).");
        return s;
    }

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Supply drops");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<DropClock>();
    }

    internal static void Reset() { clock = 0f; next = -1f; }

    private void Update()
    {
        int s = SceneManager.GetActiveScene().handle;
        if (s != scene) { scene = s; Reset(); }
        if (!ModConfig.TimedDrops.Value || !MapHandler.Exists || Character.localCharacter == null) return;
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;
        if (BloodMoon.Active) return;
        if (!Squad.Alive().Any(c => !Sanctuary.Safe(c))) return;
        if (next < 0f) next = Roll();
        clock += Time.deltaTime;
        if (clock < next) return;
        try
        {
            // No clear landing yet: try again in a few seconds.
            if (SupplyDrop.TimedDrop()) Reset();
            else clock = next - 5f;
        }
        catch (System.Exception e)
        {
            Reset();
            Plugin.Log.LogError($"Supply drop failed: {e}");
        }
    }
}

/// <summary>
/// Lives on a supply crate: comes down on every machine at the same moment - slammed in by a
/// flare, or floating on balloons - then smokes (an Ancient Luggage glows) until you've opened it.
/// </summary>
public class SkyDrop : MonoBehaviour, IPunInstantiateMagicCallback
{
    private const float M = 1f / 1.6f;

    private Vector3 ground;
    private Quaternion rest;
    private double start;
    private DropStyle style;
    private DropKind kind;
    private bool landed;
    private float height, seconds;
    private Rigidbody[] bodies = new Rigidbody[0];
    private readonly List<bool> wasKinematic = new();
    private Luggage luggage = null!;
    private readonly List<GameObject> balloons = new();
    private readonly List<LineRenderer> strings = new();
    private float top = 1f;
    private GameObject? marker;
    private Light? glow;
    private bool markerOff;

    /// <summary>Seconds of plane overhead before the crate is let go.</summary>
    internal const float PlaneLead = 3f;
    /// <summary>A group's crate: only these scouts pick from it (null = everyone).</summary>
    internal int[]? Pickers { get; private set; }
    private Renderer[] shown = new Renderer[0];
    private bool hidden;

    internal static bool Falling(Luggage? l) => l != null && l.GetComponent<SkyDrop>() is { landed: false };

    /// <summary>Empty pockets at a sky crate: one more card.</summary>
    internal static bool EmptyHanded(Luggage? l)
    {
        if (!ModConfig.EmptyHandedPicks.Value || l == null || l.GetComponent<SkyDrop>() == null || Player.localPlayer is not { } p) return false;
        return p.itemSlots.All(s => s == null || s.IsEmpty()) && (p.tempFullSlot == null || p.tempFullSlot.IsEmpty());
    }

    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        luggage = GetComponent<Luggage>();
        ground = transform.position;
        rest = transform.rotation;
        var data = GetComponent<PhotonView>()?.InstantiationData;
        start = data is { Length: > 0 } && data[0] is double t ? t : PhotonNetwork.Time;
        string caller = data is { Length: > 1 } && data[1] is string s ? s : "";
        style = data is { Length: > 2 } && data[2] is int st ? (DropStyle)st : DropStyle.Slam;
        kind = data is { Length: > 3 } && data[3] is int k ? (DropKind)k : DropKind.Plain;
        Pickers = data is { Length: > 4 } && data[4] is int[] who && who.Length > 0 ? who : null;
        height = style == DropStyle.Float ? 45f * M : 60f * M;
        seconds = style == DropStyle.Float ? 9f : 3.5f;
        var r = GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToList();
        if (r.Count > 0) top = r.Select(x => x.bounds.max.y).Max() - ground.y;

        bodies = GetComponentsInChildren<Rigidbody>(true);
        foreach (var b in bodies) { wasKinematic.Add(b.isKinematic); b.isKinematic = true; }
        // Late joiners see it already down.
        if (PhotonNetwork.Time - start > seconds) { Land(quiet: true); return; }
        if (PhotonNetwork.Time < start)
        {
            // Not let go yet: the plane is still on its way.
            shown = GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray();
            foreach (var x in shown) x.enabled = false;
            hidden = true;
        }
        Flyover.Play(ground + Vector3.up * height, (float)(start - PhotonNetwork.Time));
        if (style == DropStyle.Float) TieBalloons();
        Place(0f);

        string title = kind == DropKind.Ancient ? "Ancient Luggage" : "Supply Drop";
        string head = string.IsNullOrEmpty(caller) ? "FROM THE SKY" : $"{caller.ToUpperInvariant()}'S FLARE WAS ANSWERED";
        Hud.Banner(head, title, kind == DropKind.Ancient ? Trees.Gold : ChoiceScreen.Cream);
        if (style == DropStyle.Slam) PlayAt(Meteors.Clips("Au_Passby_Debris1", "Au_Passby_Debris3"), ground + Vector3.up * height * 0.5f, 0.6f, 0.8f);
    }

    private void Update()
    {
        if (!landed)
        {
            if (hidden && PhotonNetwork.Time >= start)
            {
                hidden = false;
                foreach (var x in shown) if (x != null) x.enabled = true;
                foreach (var b in balloons) if (b != null) b.SetActive(true);
                foreach (var s in strings) if (s != null) s.enabled = true;
            }
            float k = Mathf.Clamp01((float)(PhotonNetwork.Time - start) / seconds);
            Place(k);
            if (k >= 1f) Land(quiet: false);
            return;
        }
        UpdateMarker();
    }

    private void Place(float k)
    {
        if (style == DropStyle.Slam)
        {
            // Falls like a dropped thing: slow at the top, fastest at the ground, with a lazy tumble.
            float drop = 1f - k * k;
            transform.position = ground + Vector3.up * height * drop;
            transform.rotation = rest * Quaternion.Euler(Mathf.Sin(k * 7f) * 14f * drop, k * 90f * drop, Mathf.Cos(k * 5f) * 10f * drop);
        }
        else
        {
            // Floats down under its balloons, swaying, settling softly at the end.
            float drop = 1f - Ease.OutQuad(k);
            float t = (float)(PhotonNetwork.Time - start);
            var sway = new Vector3(Mathf.Sin(t * 0.9f), 0f, Mathf.Cos(t * 0.7f)) * 1.2f * M * drop;
            transform.position = ground + Vector3.up * height * drop + sway;
            transform.rotation = rest * Quaternion.Euler(Mathf.Sin(t * 1.3f) * 8f * drop, 0f, Mathf.Cos(t * 1.1f) * 8f * drop);
            UpdateBalloons(t);
        }
    }

    private void Land(bool quiet)
    {
        landed = true;
        transform.position = ground;
        transform.rotation = rest;
        for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = wasKinematic[i];
        PopBalloons(quiet);
        AddMarker();
        if (quiet) return;
        PlayAt(Meteors.Clips("Au_Destroy_Stone2_1", "Au_Destroy_Stone2_2", "Au_explosionRock1"), ground + Vector3.up, style == DropStyle.Slam ? 0.7f : 0.35f, 1.2f);
        GamefeelHandler.instance?.AddPerlinShakeProximity(ground, style == DropStyle.Slam ? 6f : 2f, 0.35f);
        if (Templates.Bridge?.breakParticles is { } dust)
            RopeSnapFx.Puff(dust.gameObject, ground + Vector3.up * 0.3f, style == DropStyle.Slam ? 1.4f : 0.9f, new Color(0.72f, 0.66f, 0.56f, 1f), 1.6f, 60);
    }

    // --- balloons ---------------------------------------------------------------------------------

    private void TieBalloons()
    {
        try
        {
            var tied = Resources.Load<GameObject>("TiedBalloon")?.GetComponent<TiedBalloon>();
            var colours = Character.localCharacter?.refs.balloons.balloonColors;
            var mesh = tied?.balloonRenderer != null ? tied.balloonRenderer.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (tied == null || mesh == null || colours == null || colours.Length == 0) { Plugin.Log.LogInfo("Supply drop: no balloon art found - it floats down bare."); return; }
            Vector3 scale = tied.balloonRenderer.transform.lossyScale * 1.6f;
            for (int i = 0; i < 5; i++)
            {
                var b = new GameObject("Supply Drop Balloon");
                b.AddComponent<MeshFilter>().sharedMesh = mesh;
                b.AddComponent<MeshRenderer>().sharedMaterial = colours[(i * 3 + Random.Range(0, colours.Length)) % colours.Length];
                b.transform.localScale = scale;
                b.SetActive(!hidden);
                balloons.Add(b);
                var lr = new GameObject("Supply Drop String").AddComponent<LineRenderer>();
                if (tied.lr != null) { lr.sharedMaterial = tied.lr.sharedMaterial; lr.widthMultiplier = tied.lr.widthMultiplier; lr.colorGradient = tied.lr.colorGradient; }
                else lr.widthMultiplier = 0.02f;
                lr.positionCount = 2;
                lr.enabled = !hidden;
                strings.Add(lr);
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Supply drop balloons failed: {e.Message}"); }
    }

    private void UpdateBalloons(float t)
    {
        Vector3 knot = transform.position + Vector3.up * top;
        for (int i = 0; i < balloons.Count; i++)
        {
            float a = i * 72f * Mathf.Deg2Rad + Mathf.Sin(t * 0.6f + i) * 0.2f;
            var p = knot + new Vector3(Mathf.Sin(a) * 0.9f * M, (3.2f + 0.35f * Mathf.Sin(t * 1.7f + i * 1.3f)) * M, Mathf.Cos(a) * 0.9f * M);
            balloons[i].transform.position = p;
            balloons[i].transform.rotation = Quaternion.Euler(Mathf.Sin(t + i) * 10f, i * 40f, Mathf.Cos(t * 0.8f + i) * 10f);
            strings[i].SetPosition(0, knot);
            strings[i].SetPosition(1, p);
        }
    }

    private void PopBalloons(bool quiet)
    {
        var pop = Character.localCharacter?.refs.balloons.popParticle;
        foreach (var b in balloons)
        {
            if (b == null) continue;
            if (!quiet && pop != null) Destroy(Instantiate(pop, b.transform.position, Quaternion.identity), 4f);
            Destroy(b);
        }
        foreach (var s in strings) if (s != null) Destroy(s.gameObject);
        balloons.Clear();
        strings.Clear();
    }

    private void OnDestroy() => PopBalloons(quiet: true);

    // --- smoke (or the ancient glow) until you've opened it ---------------------------------------

    private void AddMarker()
    {
        try
        {
            if (kind == DropKind.Ancient)
            {
                var go = new GameObject("Supply Drop Glow");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up * top * 0.6f;
                glow = go.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = AncientGlow();
                glow.range = 9f * M;
                glow.intensity = 6f;
                glow.shadows = LightShadows.None;
                marker = go;
                return;
            }
            var prefab = Flares.SmokePrefab();
            if (prefab == null) { Plugin.Log.LogInfo("Supply drop: no flare smoke to borrow."); return; }
            var hold = new GameObject("hold");
            hold.SetActive(false);
            var fx = Instantiate(prefab, hold.transform);
            foreach (var tr in fx.GetComponentsInChildren<TrackNetworkedObject>(true)) DestroyImmediate(tr);
            fx.transform.SetParent(transform, false);
            fx.transform.localPosition = Vector3.up * top;
            fx.transform.localRotation = Quaternion.identity;
            Destroy(hold);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = new Color(1f, 0.42f, 0.12f, 1f);
            }
            marker = fx;
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Supply drop marker failed: {e.Message}"); }
    }

    /// <summary>The statue amulet's own light colour, else a warm gold.</summary>
    private static Color AncientGlow()
    {
        var amulet = Items.Database.FirstOrDefault(Items.IsAmulet);
        var light = amulet != null ? amulet.GetComponentInChildren<Light>(true) : null;
        return light != null ? light.color : new Color(1f, 0.82f, 0.45f);
    }

    private void UpdateMarker()
    {
        if (marker == null || markerOff) return;
        if (glow != null) glow.intensity = 5f + 2.5f * Mathf.Sin(Time.time * 2.2f);
        if (luggage == null || luggage.state != Luggage.LuggageState.Open) return;
        // Opened (for you, or for everyone): the smoke thins out, the glow fades.
        markerOff = true;
        foreach (var ps in marker.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (glow != null) glow.enabled = false;
        Destroy(marker, 8f);
    }

    private static void PlayAt(AudioClip[] clips, Vector3 at, float volume, float pitch)
    {
        if (clips.Length == 0) return;
        var go = new GameObject("Supply Drop Sound");
        go.transform.position = at;
        var src = go.AddComponent<AudioSource>();
        src.clip = clips[Random.Range(0, clips.Length)];
        src.spatialBlend = 1f;
        src.dopplerLevel = 0f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 12f * M;
        src.maxDistance = 300f * M;
        src.volume = volume;
        src.pitch = pitch;
        src.outputAudioMixerGroup = Meteors.MixerGroup("SFX");
        src.Play();
        Destroy(go, src.clip.length / Mathf.Max(0.1f, pitch) + 0.5f);
    }
}

/// <summary>
/// The plane from the loading screen passing over a supply drop: its sound crosses the sky above
/// the landing spot, overhead just as the crate is let go.
/// </summary>
internal class Flyover : MonoBehaviour
{
    private const float M = 1f / 1.6f;
    private const float Seconds = 9f;
    private const float Span = 260f * M;

    private Vector3 from, to;
    private float age, overheadAt;
    private AudioSource src = null!;

    private static AudioClip? clip;
    private static bool looked;

    /// <summary>The loading screen plane's sound, else any loaded clip named like a plane.</summary>
    private static AudioClip? Clip()
    {
        if (clip != null || looked) return clip;
        looked = true;
        try
        {
            var handler = RetrievableResourceSingleton<LoadingScreenHandler>.Instance;
            var plane = handler != null ? handler.loadingScreenPrefabPlane : null;
            if (plane != null)
                clip = plane.GetComponentsInChildren<AudioSource>(true).Select(a => a.clip).Where(c => c != null).OrderByDescending(c => c.length).FirstOrDefault();
            if (clip == null)
                clip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c != null && (c.name.ToLowerInvariant().Contains("airplane") || c.name.ToLowerInvariant().Contains("plane")));
            Plugin.Log.LogInfo(clip != null ? $"Supply drop: plane sound is '{clip.name}' ({clip.length:0.0}s)." : "Supply drop: no plane sound found - drops come in quiet.");
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Supply drop: plane sound lookup failed: {e.Message}"); }
        return clip;
    }

    /// <summary>A pass over this point, overhead in this many seconds.</summary>
    internal static void Play(Vector3 over, float overheadIn)
    {
        var c = Clip();
        if (c == null || overheadIn < -2f) return;
        var dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        var go = new GameObject("Supply Drop Plane");
        var f = go.AddComponent<Flyover>();
        f.overheadAt = Mathf.Max(0f, overheadIn);
        float speed = Span / Seconds;
        f.from = over + Vector3.up * 30f * M - dir * speed * f.overheadAt;
        f.to = f.from + dir * Span;
        f.src = go.AddComponent<AudioSource>();
        f.src.clip = c;
        f.src.loop = true;
        f.src.spatialBlend = 1f;
        f.src.dopplerLevel = 0.6f;
        f.src.rolloffMode = AudioRolloffMode.Linear;
        f.src.minDistance = 40f * M;
        f.src.maxDistance = 500f * M;
        f.src.volume = 0f;
        f.src.outputAudioMixerGroup = Meteors.MixerGroup("SFX");
        go.transform.position = f.from;
        f.src.Play();
    }

    private void Update()
    {
        age += Time.deltaTime;
        float k = Mathf.Clamp01(age / Seconds);
        transform.position = Vector3.Lerp(from, to, k);
        // Fades in from far off, loudest overhead, fades away.
        src.volume = 0.9f * Mathf.Min(Mathf.Clamp01(age / 2f), Mathf.Clamp01((Seconds - age) / 2.5f));
        if (age >= Seconds) Destroy(gameObject);
    }
}
