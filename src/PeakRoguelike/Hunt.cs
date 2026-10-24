using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Binding a relic wakes the Scoutmaster: for a minute he hunts the whole team, one scout
/// after another (after each throw he picks the next). The host drives him - he is the
/// host's object - by forcing his target, which overrides his usual "only the climber who
/// ran ahead" rules.
/// </summary>
internal class ScoutmasterHunt : MonoBehaviour
{
    private static ScoutmasterHunt? instance;
    private float until;
    private float nextTick;
    private Character? last;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Scoutmaster Hunt");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<ScoutmasterHunt>();
    }

    /// <summary>Everyone gets the warning; only the host's copy actually steers him.</summary>
    internal static void Begin(float seconds, string binder)
    {
        Steer(seconds); // unannounced: he simply comes
    }

    /// <summary>Host only: set him loose for this long (no banner - the caller announces it).</summary>
    internal static void Steer(float seconds)
    {
        if (instance == null || !Photon.Pun.PhotonNetwork.IsMasterClient) return;
        if (!Hunting)
        {
            // A new moon: everyone is fair game again.
            instance.spent.Clear();
            instance.throws.Clear();
            instance.stares.Clear();
            instance.lungeAt = instance.holding = null;
            Mode = HuntMode.Hunter;
            instance.ShareSpent();
        }
        instance.until = Mathf.Max(instance.until, Time.time + seconds);
        instance.last = null;
        instance.arrive = true; // he steps in from out of sight on the first target
        Plugin.Log.LogInfo($"Scoutmaster hunt: {seconds}s.");
    }

    internal static void Stop()
    {
        if (instance != null) instance.until = 0f;
    }

    /// <summary>Host: a hunt is running right now.</summary>
    internal static bool Hunting => instance != null && instance.until > 0f && Time.time < instance.until;

    // The speed boost we gave him (movement is simulated by his owner, the host).
    private Character? boosted;
    private float boostMove, boostClimb;

    private void Boost(Character? who)
    {
        if (boosted == who) return;
        if (boosted != null)
        {
            boosted.refs.movement.movementModifier -= boostMove;
            boosted.refs.climbing.climbSpeedMod -= boostClimb;
        }
        boosted = who;
        if (who == null) return;
        float extra = Mathf.Max(0f, ModConfig.BmScoutmasterSpeed.Value - 1f);
        boostMove = who.refs.movement.movementModifier * extra;
        boostClimb = who.refs.climbing.climbSpeedMod * extra;
        who.refs.movement.movementModifier += boostMove;
        who.refs.climbing.climbSpeedMod += boostClimb;
        Plugin.Log.LogInfo($"Scoutmaster sped up x{ModConfig.BmScoutmasterSpeed.Value:0.##} for the hunt.");
    }

    /// <summary>T1-2: he hunts (GrabsPerScout each). T3+: he only watches. (Lunge = the old T2 stare charge, unused.)</summary>
    internal enum HuntMode { Hunter, Lunge, Observer }

    /// <summary>Host: what he's doing right now (read by the Chase patches).</summary>
    internal static HuntMode Mode { get; private set; } = HuntMode.Hunter;

    /// <summary>Scouts he has thrown (or charged at) this moon: he only watches them from now on.</summary>
    private readonly HashSet<Character> spent = new();
    private Character? holding, lungeAt;
    private float lungeUntil;
    private readonly Dictionary<Character, float> stares = new();

    private void Update()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + 0.5f;
        Scoutmaster? sm = null;
        bool mine = Photon.Pun.PhotonNetwork.IsMasterClient && Scoutmaster.GetPrimaryScoutmaster(out sm)
            && sm != null && sm.view != null && sm.view.IsMine;
        if (mine) Watchdog(sm!);

        if (until <= 0f) { if (boosted != null) Boost(null); wasBusy = false; restUntil = 0f; return; }
        if (!Photon.Pun.PhotonNetwork.IsMasterClient || Time.time >= until) { until = 0f; Boost(null); return; }
        if (!mine) return;
        Boost(sm!.GetComponent<Character>());
        // Mid-grab or mid-throw: never swap or clear his target - his throw coroutine reads it
        // every frame and dies on a null, leaving him clutching his prey forever.
        bool busy = Busy(sm);
        if (busy && sm.character.data.grabbedPlayer != null) holding = sm.character.data.grabbedPlayer;
        if (wasBusy && !busy)
        {
            if (holding != null) Spend(holding, "was thrown");
            if (holding == lungeAt) lungeAt = null;
            holding = null;
            Recover(sm, "threw his prey");
        }
        wasBusy = busy;
        if (busy) return;
        // Catching his breath after a throw or a miss: the scouts get a gap.
        if (restUntil > 0f)
        {
            if (Time.time < restUntil) return;
            restUntil = 0f;
            if (sm.currentTarget != null) ResetStuck(sm, sm.currentTarget);
        }

        // He goes after everyone in the open, relic holders first. Scouts on safe ground (lit
        // campfire / summit) or veiled by a relic that just fired are left alone - he moves on.
        bool awake(Character c) => !c.data.fullyPassedOut && !c.data.dead;
        var prey = Squad.Pursued().Where(awake).ToList();
        var marked = Squad.Marked().Where(awake).ToList();
        var current = sm.currentTarget;
        if (prey.Count == 0)
        {
            // Every holder is safe or veiled by a relic: he loses them for now (the hunt resumes when one shows).
            if (current != null)
            {
                sm!.view.RPC("RPCA_SetCurrentTarget", Photon.Pun.RpcTarget.All, -1, 0f);
                HuntPatches.Target(sm) = null!;
                last = null;
                Plugin.Log.LogInfo("Scoutmaster: nobody in the open (safe ground / relic veil) - he backs off.");
            }
            return;
        }

        // What the moon makes him tonight.
        if (lungeAt != null && (Time.time >= lungeUntil || !marked.Contains(lungeAt)))
        {
            Spend(lungeAt, "outran his charge");
            lungeAt = null;
        }
        var fresh = prey.Where(c => !spent.Contains(c)).ToList();
        var mode = BloodMoon.ScoutmasterTier <= 2 && fresh.Count > 0 ? HuntMode.Hunter : HuntMode.Observer;
        if (mode != Mode) Plugin.Log.LogInfo($"Scoutmaster: {Mode} -> {mode} (tier {BloodMoon.ScoutmasterTier}).");
        Mode = mode;
        var pool = mode == HuntMode.Hunter ? fresh : mode == HuntMode.Lunge ? new List<Character> { lungeAt! } : prey;

        // A bodyguard he turned on stays his prey only until the next throw.
        bool onIt = sm.targetForced && current != null
            && (pool.Contains(current) || (mode == HuntMode.Hunter && current == standIn && marked.Contains(current)));
        if (onIt)
        {
            // His moonrise appearance found no ground: keep trying until he's in.
            if (arrive && Appear(sm, current!)) { arrive = false; ResetStuck(sm, current!); return; }
            if (mode == HuntMode.Hunter) CloserPrey(sm, current!, marked);
            Unstick(sm, current!);
            return;
        }

        // Just threw someone (or lost them): on to the next scout, nearest first.
        // A holder whose relic just fired is veiled (not in the pool): he turns to whoever is left.
        var next = pool.Where(c => c != last || pool.Count == 1)
            .OrderBy(Squad.Priority)
            .ThenBy(c => Vector3.Distance(c.Center, Pos(sm)))
            .FirstOrDefault();
        if (next == null) return;
        last = next;
        // Straight to the RPC: SetCurrentTarget ignores "same scout again", and clearing first
        // would null his target for a moment.
        sm.view.RPC("RPCA_SetCurrentTarget", Photon.Pun.RpcTarget.All, next.photonView.ViewID, until - Time.time);
        HuntPatches.Target(sm) = next; // the RPC's setter skips it while the old target is still forced
        Plugin.Log.LogInfo($"Scoutmaster {(mode == HuntMode.Observer ? "watching" : "hunting")} {next.characterName}.");
        // The moon just rose: he appears where they'll see him.
        if (arrive)
        {
            if (Appear(sm, next)) arrive = false;
            else Plugin.Log.LogWarning($"Scoutmaster: no ground to appear on near {next.characterName} - trying again.");
        }
        ResetStuck(sm, next);
    }

    /// <summary>Throws per scout this moon; at the tier's grab count he only watches them.</summary>
    private readonly Dictionary<Character, int> throws = new();

    private static int Grabs => Mathf.Max(1, BloodMoon.ScoutmasterTier <= 1 ? ModConfig.BmGrabsTier1.Value : ModConfig.BmGrabsTier2.Value);

    private void Spend(Character c, string why)
    {
        int n = (throws.TryGetValue(c, out int t) ? t : 0) + 1;
        throws[c] = n;
        if (n < Grabs) { Plugin.Log.LogInfo($"Scoutmaster: {c.characterName} {why} ({n}/{Grabs})."); return; }
        if (!spent.Add(c)) return;
        stares.Remove(c);
        ShareSpent();
        Plugin.Log.LogInfo($"Scoutmaster: {c.characterName} {why} - he only watches them for the rest of the moon.");
    }

    private const string SpentKey = "prl.sm.spent";

    /// <summary>Host: tell everyone who he's done with this moon (their screens stop building static).</summary>
    private void ShareSpent()
    {
        if (!Photon.Pun.PhotonNetwork.InRoom) return;
        var actors = spent.Where(c => c != null && c.photonView?.Owner != null).Select(c => c.photonView.Owner.ActorNumber).ToArray();
        try { Photon.Pun.PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { [SpentKey] = actors }); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Scoutmaster: couldn't share who's spent: {e.Message}"); }
    }

    /// <summary>Everyone: has he already had his one go at the local scout this moon?</summary>
    internal static bool LocalSpent()
    {
        if (!Photon.Pun.PhotonNetwork.InRoom) return instance != null && Character.localCharacter is { } me && instance.spent.Contains(me);
        return Photon.Pun.PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(SpentKey, out object raw)
               && raw is int[] actors && actors.Contains(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
    }

    /// <summary>
    /// T2, host, every frame: a scout who looks straight at him for StareLunge seconds draws his
    /// charge (looking away starts the count over). He runs from where he stands - dodgeable.
    /// </summary>
    private void Stares()
    {
        if (BloodMoon.ScoutmasterTier != 2 || lungeAt != null || restUntil > 0f || !Scoutmaster.GetPrimaryScoutmaster(out var sm)
            || sm == null || sm.character == null || Busy(sm))
        {
            stares.Clear();
            return;
        }
        foreach (var c in Squad.Marked())
        {
            if (spent.Contains(c) || c.data.fullyPassedOut || !StaringAt(c, sm.character)) { stares.Remove(c); continue; }
            float t = (stares.TryGetValue(c, out var s) ? s : 0f) + Time.deltaTime;
            stares[c] = t;
            if (t < ModConfig.BmStareLunge.Value) continue;
            stares.Clear();
            lungeAt = c;
            lungeUntil = Time.time + ModConfig.BmLungeTime.Value;
            nextTick = 0f; // retarget now
            Plugin.Log.LogInfo($"Scoutmaster: {c.characterName} stared at him for {t:0.#}s - he charges.");
            return;
        }
    }

    /// <summary>Looking straight at him (near the middle of the screen, nothing in between).</summary>
    internal static bool StaringAt(Character scout, Character him) =>
        Vector3.Distance(scout.Head, him.Center) < 90f * Squad.M
        && Vector3.Angle(scout.data.lookDirection, him.Center - scout.Head) < ModConfig.BmStareAngle.Value
        && HelperFunctions.LineCheck(scout.Head, him.Center, HelperFunctions.LayerType.TerrainMap).transform == null;

    /// <summary>He's in the scout's field of view with nothing in between.</summary>
    internal static bool InView(Character scout, Character him) =>
        Vector3.Angle(scout.data.lookDirection, him.Center - scout.Head) < 70f
        && HelperFunctions.LineCheck(scout.Head, him.Center, HelperFunctions.LayerType.TerrainMap).transform == null;

    /// <summary>Any living scout has him in view right now (he freezes).</summary>
    internal static bool Watched(Character him) =>
        Squad.Alive().Any(c => !c.data.fullyPassedOut && InView(c, him));

    /// <summary>Nothing solid between the scout and that spot (whether or not they're facing it).</summary>
    private static bool ClearLine(Character scout, Vector3 spot) =>
        HelperFunctions.LineCheck(scout.Head, spot + Vector3.up * 1.2f, HelperFunctions.LayerType.TerrainMap).transform == null;

    private bool arrive;

    /// <summary>
    /// His body (the ragdoll) is where he really is; the root transform stays behind - which is
    /// why the log said he was 3500m away and he got "unstuck" every 10 seconds.
    /// </summary>
    private static Vector3 Pos(Scoutmaster sm) => sm.character != null ? sm.character.Center : sm.transform.position;

    private bool wasBusy;
    private float restUntil;
    private Character? standIn;

    /// <summary>
    /// Closing in on a group, he goes for whoever is nearest him - a bodyguard can take the
    /// grab meant for the holder. (Host only; his target is forced, so set it underneath.)
    /// </summary>
    private void CloserPrey(Scoutmaster sm, Character current, System.Collections.Generic.List<Character> marked)
    {
        Vector3 me = Pos(sm);
        float now = Vector3.Distance(me, current.Center);
        if (now > 12f) return;
        var nearest = marked.OrderBy(c => Vector3.Distance(me, c.Center)).First();
        if (nearest == current || Vector3.Distance(me, nearest.Center) > now - 1.5f) return;
        HuntPatches.Target(sm) = nearest;
        standIn = nearest;
        ResetStuck(sm, nearest);
        Plugin.Log.LogInfo($"Scoutmaster turns on {nearest.characterName}, who stood closer.");
    }

    /// <summary>
    /// He just threw someone or lunged at nothing: he stands where he is for a few seconds, then
    /// locks onto his next target. He never leaves.
    /// </summary>
    private void Recover(Scoutmaster sm, string why)
    {
        standIn = null;
        HuntPatches.EndCloseLunge();
        float s = BloodMoon.ScoutmasterTier <= 2 ? ModConfig.BmHunterRecover.Value : ModConfig.BmScoutmasterRecover.Value;
        if (s <= 0f) return;
        sm.chillForSeconds = s;
        TargetForcedUntil(sm) = Time.time; // free to pick someone new once he's recovered
        restUntil = Time.time + s;
        Plugin.Log.LogInfo($"Scoutmaster {why} - recovering for {s:0}s.");
    }

    /// <summary>Host: he's had his throws at this scout this moon (he only watches them).</summary>
    internal static bool IsSpent(Character c) => instance != null && instance.spent.Contains(c);

    /// <summary>Host: switch his hunt onto this scout now (a close stare). False = he can't.</summary>
    internal static bool TurnOn(Scoutmaster sm, Character c)
    {
        if (instance == null || c.photonView == null || Busy(sm)) return false;
        instance.last = c;
        instance.standIn = null;
        sm.view.RPC("RPCA_SetCurrentTarget", Photon.Pun.RpcTarget.All, c.photonView.ViewID, instance.until - Time.time);
        HuntPatches.Target(sm) = c;
        return true;
    }

    /// <summary>Host: a lunge (grab attempt) caught nobody.</summary>
    internal static void MissedLunge(Scoutmaster sm)
    {
        if (instance == null || !Hunting || instance.restUntil > 0f) return;
        if (Mode == HuntMode.Lunge && instance.lungeAt != null)
        {
            instance.Spend(instance.lungeAt, "dodged his charge");
            instance.lungeAt = null;
        }
        instance.Recover(sm, "missed his lunge");
    }

    private static readonly HarmonyLib.AccessTools.FieldRef<Scoutmaster, float> TargetForcedUntil =
        HarmonyLib.AccessTools.FieldRefAccess<Scoutmaster, float>("targetForcedUntil");

    /// <summary>Holding someone or winding up a throw.</summary>
    internal static bool Busy(Scoutmaster sm) =>
        sm.isThrowing || (sm.character != null && sm.character.data.grabbedPlayer != null);

    // --- grab watchdog (always on, host) ------------------------------------------------------

    private float throwingSince, badGripSince;

    /// <summary>
    /// A throw takes 3.2s. If his throw ever breaks (target lost mid-throw) he keeps the grab,
    /// never throws again, and keeps resetting his victim's "on the ground" timer - so they can
    /// jump endlessly in mid-air. Catch that and make him let go.
    /// </summary>
    private void Watchdog(Scoutmaster sm)
    {
        var me = sm.character;
        if (me == null) return;
        var held = me.data.grabbedPlayer;

        if (sm.isThrowing) { if (throwingSince <= 0f) throwingSince = Time.time; }
        else throwingSince = 0f;

        bool badGrip = held != null && (me.data.grabJoint == null || held.data.dead
            || Vector3.Distance(held.Center, me.Center) > 6f * Squad.M);
        if (badGrip) { if (badGripSince <= 0f) badGripSince = Time.time; }
        else badGripSince = 0f;

        bool stuckThrow = throwingSince > 0f && Time.time - throwingSince > 6f;
        bool stuckGrip = badGripSince > 0f && Time.time - badGripSince > 1f;
        if (!stuckThrow && !stuckGrip) return;
        Plugin.Log.LogWarning($"Scoutmaster {(stuckThrow ? "throw stuck" : "lost his grip")} on {(held != null ? held.characterName : "nobody")} - making him let go.");
        Release(sm);
        throwingSince = badGripSince = 0f;
    }

    internal static void Release(Scoutmaster sm)
    {
        sm.StopAllCoroutines();
        sm.isThrowing = false;
        sm.chillForSeconds = 2f;
        var me = sm.character;
        if (me != null && me.data.grabbedPlayer != null)
            me.refs.view.RPC("RPCA_GrabUnattach", Photon.Pun.RpcTarget.All);
    }

    // --- getting him to his prey -------------------------------------------------------------

    private Vector3 stuckPos;
    private float stuckSince;

    private float farSince;

    private void ResetStuck(Scoutmaster sm, Character prey)
    {
        stuckPos = Pos(sm);
        stuckSince = Time.time;
        farSince = Time.time;
    }

    /// <summary>
    /// He's only frightening if he's seen, so he stays where his prey could see him: out of their
    /// line of sight for ScoutmasterUnseen seconds (or too far off for ScoutmasterFarTime) he's
    /// somewhere else in the open - picked while nobody is looking at it, so he's never seen moving.
    /// </summary>
    private void Unstick(Scoutmaster sm, Character prey)
    {
        var me = sm.character;
        if (me == null) return;
        if (Busy(sm) || me.warping) { ResetStuck(sm, prey); return; }
        Vector3 pos = Pos(sm);
        float d = Vector3.Distance(pos, prey.Center);
        bool inOpen = HelperFunctions.LineCheck(prey.Head, me.Center, HelperFunctions.LayerType.TerrainMap).transform == null;
        float tooFar = Mode == HuntMode.Observer ? Band().max * 1.5f : ModConfig.BmScoutmasterFarDistance.Value * Squad.M;
        if (inOpen) stuckSince = Time.time;
        if (d <= tooFar) farSince = Time.time;
        float unseen = ModConfig.BmScoutmasterUnseen.Value, far = ModConfig.BmScoutmasterFarTime.Value;
        bool hidden = unseen > 0f && Time.time - stuckSince >= unseen;
        bool away = far > 0f && Time.time - farSince >= (Mode == HuntMode.Observer ? unseen : far);
        if (!hidden && !away) return;
        if (Mode == HuntMode.Lunge) return; // mid-charge he runs it out
        if (SomeoneSees(pos)) return;       // never vanish while someone is looking
        Plugin.Log.LogInfo($"Scoutmaster {(hidden ? $"out of {prey.characterName}'s sight" : $"{d * CharacterStats.unitsToMeters:0}m from {prey.characterName}")} - appearing in the open.");
        if (!Appear(sm, prey, near: true)) Plugin.Log.LogWarning("Scoutmaster: no ground to appear on - trying again.");
        ResetStuck(sm, prey);
    }

    /// <summary>How far from his prey he shows himself (game units).</summary>
    private static (float min, float max) Band(bool near = false) =>
        Mode == HuntMode.Hunter
            ? near ? (ModConfig.BmHunterAppearMin.Value * 0.6f * Squad.M, ModConfig.BmHunterAppearMin.Value * Squad.M)
                   : (ModConfig.BmHunterAppearMin.Value * Squad.M, ModConfig.BmHunterAppearMax.Value * Squad.M)
            : (ModConfig.BmWatchMin.Value * Squad.M, ModConfig.BmWatchMax.Value * Squad.M);

    /// <summary>He has his prey in his line of sight (nothing solid between his eyes and them).</summary>
    internal static bool CanSee(Scoutmaster sm, Character prey) =>
        sm.character != null && HelperFunctions.LineCheck(sm.character.Head, prey.Center, HelperFunctions.LayerType.TerrainMap).transform == null;

    /// <summary>
    /// He steps into the open: a spot his prey has a clear line to (high ground preferred) that
    /// nobody happens to be looking at right now - turn around and he's simply there.
    /// Within ScoutmasterSpawnHeight of their height.
    /// </summary>
    internal static bool Appear(Scoutmaster sm, Character prey, bool near = false)
    {
        var (min, max) = Band(near);
        min = Mathf.Max(5f * Squad.M, min);
        max = Mathf.Max(min + 1f, max);
        float h = Mathf.Max(2f, ModConfig.BmScoutmasterSpawnHeight.Value) * Squad.M;
        // Never above his prey: he has to come to them on their level.
        bool ok = WarpTo(sm, prey, min, max, -h, HuntPatches.LevelGap * Squad.M);
        if (ok && Mode == HuntMode.Hunter) HuntPatches.Appeared();
        return ok;
    }

    private static bool WarpTo(Scoutmaster sm, Character prey, float minFlat, float maxFlat, float minUp, float maxUp)
    {
        Vector3 c = prey.Center;
        Vector3? open = null, hidden = null, seen = null;
        float openUp = float.MinValue;
        int found = 0;
        for (int i = 0; i < 64 && found < 6; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(minFlat, maxFlat);
            float top = c.y + maxUp + 30f * Squad.M;
            if (!Physics.Raycast(new Vector3(c.x + d.x, top, c.z + d.y), Vector3.down, out var hit, top - (c.y + minUp) + 1f,
                    HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)) continue;
            float up = hit.point.y - c.y;
            if (up < minUp || up > maxUp || hit.normal.y < 0.45f) continue;
            Vector3 spot = hit.point + Vector3.up;
            if (SomeoneSees(spot)) { seen ??= spot; continue; }
            if (!ClearLine(prey, spot)) { hidden ??= spot; continue; }
            found++;
            if (up > openUp) { open = spot; openUp = up; }
        }
        // In the open if we can; else out of sight (he'll step out); else even in plain view rather than stuck.
        var pick = open ?? hidden ?? seen;
        if (pick == null) return false;
        Warp(sm, pick.Value);
        Plugin.Log.LogInfo($"Scoutmaster appears {(open != null ? "in the open" : hidden != null ? "out of sight (no open ground)" : "in plain view (nowhere else)")}, {Vector3.Distance(pick.Value, c) * CharacterStats.unitsToMeters:0}m from {prey.characterName}.");
        return true;
    }

    private static void Warp(Scoutmaster sm, Vector3 spot)
    {
        if (Busy(sm)) return; // never teleport away with a scout in his hand
        sm.view.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, spot, false);
        sm.view.RPC("StopClimbingRpc", Photon.Pun.RpcTarget.All, 0f);
        HuntPatches.Discovered(sm) = null!;
    }

    internal static bool SomeoneSees(Vector3 pos)
    {
        foreach (var c in Character.AllCharacters)
        {
            if (c == null || c.isBot || c.data.isScoutmaster) continue;
            if (Vector3.Angle(pos - c.Head, c.data.lookDirection) > 80f) continue;
            if (HelperFunctions.LineCheck(c.Head, pos, HelperFunctions.LayerType.TerrainMap).transform == null) return true;
        }
        return false;
    }
}

[HarmonyLib.HarmonyPatch]
internal static class HuntPatches
{
    /// <summary>
    /// His own checks drop the target whenever another scout is within 15m of it, it isn't the
    /// highest climber, or anyone is above 825m (every biome after the first two) - which is why
    /// he vanished in later blood moons. During a hunt the host keeps him on the chosen prey.
    /// </summary>
    [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "VerifyTarget")]
    private static bool KeepHunting(Scoutmaster __instance) => !ScoutmasterHunt.Hunting && !ScoutmasterHunt.Busy(__instance);

    /// <summary>
    /// HoldPlayer runs every frame he "holds" someone and zeroes their time-since-grounded (so
    /// they can jump in mid-air). Only let it run while the grip is real and on his target.
    /// </summary>
    [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "HoldPlayer")]
    private static bool OnlyRealGrips(Scoutmaster __instance)
    {
        var me = __instance.character;
        // Outside our hunts his grips are vanilla's (or another mod's Scoutmasters).
        if (me == null || (!ScoutmasterHunt.Hunting && !ScoutmasterHunt.Busy(__instance))) return true;
        var held = me.data.grabbedPlayer;
        if (held != null && me.data.grabJoint != null && held == __instance.currentTarget) return true;
        // During a hunt he may catch someone next to his prey: they become the one he throws.
        if (held != null && me.data.grabJoint != null && ScoutmasterHunt.Hunting && Squad.Marked().Contains(held))
        {
            Target(__instance) = held;
            Plugin.Log.LogInfo($"Scoutmaster caught {held.characterName} instead.");
            return true;
        }
        Plugin.Log.LogWarning("Scoutmaster was holding nobody (or the wrong scout) - letting go.");
        ScoutmasterHunt.Release(__instance);
        return false;
    }

    private static readonly int StaticStrength = Shader.PropertyToID("_Strength");

    /// <summary>
    /// His static filter and fear music both come from here (the music follows the "Myers
    /// distance" this sets on his target). The static is gone for good - vanilla's can stick on
    /// screen. Under a blood moon our song plays alone: the distance is never set, so his music
    /// fades; otherwise his music is vanilla.
    /// </summary>
    [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "DoVisuals")]
    private static bool QuietUnderBloodMoon(Scoutmaster __instance)
    {
        if (!BloodMoon.Active) return true;
        NoStatic(__instance);
        return false;
    }

    [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "DoVisuals")]
    private static void NoStatic(Scoutmaster __instance)
    {
        var mat = __instance.mat;
        if (mat != null && mat.GetFloat(StaticStrength) != 0f) mat.SetFloat(StaticStrength, 0f);
    }

    /// <summary>His throws carry him less far under a blood moon (damage untouched: the landing decides).</summary>
    [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch(typeof(CharacterGrabbing), "Throw")]
    private static void ShorterThrows(Character ___character, ref Vector3 force)
    {
        if (!BloodMoon.Active || ___character == null || !___character.data.isScoutmaster) return;
        force *= Mathf.Max(0f, ModConfig.BmScoutmasterThrowForce.Value);
    }

    private static Character? sightOf;

    private static Character? closeStarer, closeLungeAt;
    private static float closeStare, closeLungeUntil, closeCooldownUntil;

    /// <summary>He threw someone or missed: any close lunge is over.</summary>
    internal static void EndCloseLunge() => closeLungeAt = null;

    /// <summary>
    /// Hunting, frozen: a scout within CloseStareDistance who stares straight at him for
    /// CloseStareSeconds draws a short lunge (he turns on them if they weren't his prey).
    /// </summary>
    private static void CloseStare(Scoutmaster sm, Character me, Character prey)
    {
        if (Time.time < closeCooldownUntil) { closeStare = 0f; return; }
        float range = Mathf.Max(0f, ModConfig.BmCloseStareDistance.Value) * Squad.M;
        var starer = Squad.Alive()
            .Where(c => !c.data.fullyPassedOut && !ScoutmasterHunt.IsSpent(c) && !Sanctuary.Safe(c) && !BloodTornado.Carrying(c)
                        && Vector3.Distance(c.Center, me.Center) <= range && Level(me, c) && ScoutmasterHunt.StaringAt(c, me))
            .OrderBy(c => c == prey ? 0 : 1)
            .FirstOrDefault();
        if (starer == null || starer != closeStarer) { closeStarer = starer; closeStare = 0f; return; }
        closeStare += Time.deltaTime;
        if (closeStare < ModConfig.BmCloseStareSeconds.Value) return;
        closeStare = 0f;
        closeStarer = null;
        if (starer != prey && !ScoutmasterHunt.TurnOn(sm, starer)) return;
        closeLungeAt = starer;
        closeLungeUntil = Time.time + Mathf.Max(0.5f, ModConfig.BmCloseLungeTime.Value);
        closeCooldownUntil = closeLungeUntil + Mathf.Max(0f, ModConfig.BmCloseLungeCooldown.Value);
        Plugin.Log.LogInfo($"Scoutmaster: {starer.characterName} stared too close ({Vector3.Distance(starer.Center, me.Center) / Squad.M:0.0}m) - he lunges.");
    }

    /// <summary>He just stepped in from out of sight (the grab clock starts over).</summary>
    internal static void Appeared()
    {
        reachSince = -1f;
    }

    /// <summary>
    /// His pace on a hunt. Watching (T3+, or once he's spent his grabs) he stands and stares.
    /// Hunting (T1-2) he freezes whenever anyone looks at him, sprints in from afar, walks the last
    /// stretch, and grabs only after a moment right on his prey with nobody looking - never while
    /// a tornado has them or they're on safe ground. Runs before <see cref="Lunge"/> so a held-back
    /// reach isn't a miss.
    /// </summary>
    [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPriority(HarmonyLib.Priority.High), HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "Chase")]
    private static void Pace(Scoutmaster __instance)
    {
        var prey = __instance.currentTarget;
        var me = __instance.character;
        if (!ScoutmasterHunt.Hunting || prey == null || me == null || ScoutmasterHunt.Busy(__instance)) { sightOf = null; return; }
        var input = me.input;
        void Stand()
        {
            input.movementInput = Vector2.zero;
            input.sprintIsPressed = false;
            input.useSecondaryIsPressed = false;
            input.jumpWasPressed = false;
        }
        switch (ScoutmasterHunt.Mode)
        {
            case ScoutmasterHunt.HuntMode.Observer:
                Stand(); // Chase already turned him to face them
                closeStare = 0f;
                return;
            case ScoutmasterHunt.HuntMode.Lunge:
                input.sprintIsPressed = true;
                if (BloodTornado.Carrying(prey) || !Level(me, prey)) input.useSecondaryIsPressed = false;
                return;
        }

        // Stared down from up close he doesn't stay frozen: he lunges at whoever is staring.
        if (closeLungeAt != null)
        {
            if (Time.time < closeLungeUntil && prey == closeLungeAt && !closeLungeAt.data.dead && !Sanctuary.Safe(prey))
            {
                input.sprintIsPressed = true;
                input.useSecondaryIsPressed = Vector3.Distance(me.Center, prey.Center) < Reach && Level(me, prey) && !BloodTornado.Carrying(prey);
                return;
            }
            Plugin.Log.LogInfo($"Scoutmaster: close lunge at {closeLungeAt.characterName} ran out.");
            closeLungeAt = null;
        }
        CloseStare(__instance, me, prey);
        if (closeLungeAt != null) return;

        // Hunting: while ANY scout looks at him he freezes. Unwatched, he sprints in from afar and
        // walks the last SprintDistance. Right on his prey he stares a moment, then grabs once
        // nobody has looked at him for a second - turn to run and that's your second.
        if (prey != sightOf) { sightOf = prey; reachSince = -1f; }
        bool watched = ScoutmasterHunt.Watched(me);
        if (watched) lastWatched = Time.time;
        float d = Vector3.Distance(me.Center, prey.Center);
        // Only level with his prey: no dropping on someone from a ledge above (or snatching from below).
        bool atReach = d < Reach && Level(me, prey);
        if (!atReach) reachSince = -1f;
        else if (reachSince < 0f) reachSince = Time.time;

        if (atReach || watched) Stand();
        else input.sprintIsPressed = d > Mathf.Max(0f, ModConfig.BmScoutmasterSprintDistance.Value) * Squad.M;

        bool grab = atReach && !watched
                    && Time.time - reachSince >= Mathf.Max(0f, ModConfig.BmGrabPause.Value)
                    && Time.time - lastWatched >= Mathf.Max(0f, ModConfig.BmGrabDelay.Value)
                    && !BloodTornado.Carrying(prey) && !Sanctuary.Safe(prey);
        input.useSecondaryIsPressed = grab;
    }

    /// <summary>The game's grab range (units).</summary>
    private const float Reach = 3f;

    /// <summary>Within LevelGap (m) up or down: high enough above or below, he can't grab.</summary>
    private static bool Level(Character me, Character prey) =>
        Mathf.Abs(me.Center.y - prey.Center.y) <= LevelGap * Squad.M;

    internal const float LevelGap = 2f;
    private static float reachSince = -1f, lastWatched = -100f;

    private static float lungeSince = -1f;

    /// <summary>
    /// Within 3m he reaches to grab. Reaching for a second (or giving up the reach) without
    /// catching anyone is a miss.
    /// </summary>
    [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "Chase")]
    private static void Lunge(Scoutmaster __instance)
    {
        var me = __instance.character;
        if (!ScoutmasterHunt.Hunting || me == null || ScoutmasterHunt.Busy(__instance)) { lungeSince = -1f; return; }
        bool reaching = me.input.useSecondaryIsPressed;
        if (reaching && lungeSince < 0f) lungeSince = Time.time;
        if (lungeSince < 0f) return;
        if (!reaching || Time.time - lungeSince > 1f)
        {
            lungeSince = -1f;
            ScoutmasterHunt.MissedLunge(__instance);
        }
    }

    /// <summary>His real target field: the property ignores changes while his target is forced.</summary>
    internal static readonly HarmonyLib.AccessTools.FieldRef<Scoutmaster, Character> Target =
        HarmonyLib.AccessTools.FieldRefAccess<Scoutmaster, Character>("_currentTarget");

    internal static readonly HarmonyLib.AccessTools.FieldRef<Scoutmaster, Character> Discovered =
        HarmonyLib.AccessTools.FieldRefAccess<Scoutmaster, Character>("discovered");
    private static readonly HarmonyLib.AccessTools.FieldRef<Scoutmaster, float> TpCounter =
        HarmonyLib.AccessTools.FieldRefAccess<Scoutmaster, float>("tpCounter");

    /// <summary>His own "too far / lost them, teleport closer": during a hunt he appears like at moonrise.</summary>
    [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "TeleportCloseToTarget")]
    private static bool CloserDuringHunts(Scoutmaster __instance)
    {
        if (!ScoutmasterHunt.Hunting || __instance.currentTarget == null) return true;
        ref float tp = ref TpCounter(__instance);
        if (tp < 5f) return false;
        if (!ScoutmasterHunt.Appear(__instance, __instance.currentTarget)) return true; // no spot: vanilla's
        tp = 0f;
        return false;
    }
}
