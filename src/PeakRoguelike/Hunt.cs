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
        instance.until = Mathf.Max(instance.until, Time.time + seconds);
        instance.last = null;
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
        if (wasBusy && !busy) Recover(sm, "threw his prey");
        wasBusy = busy;
        if (busy) return;
        // Catching his breath after a throw or a miss: the scouts get a gap.
        if (restUntil > 0f)
        {
            if (Time.time < restUntil) return;
            restUntil = 0f;
            if (sm.currentTarget != null) ResetStuck(sm, sm.currentTarget);
        }

        // He hunts relic holders; anyone standing near one shares the danger. Scouts on safe
        // ground (lit campfire / summit) are left alone. No relics in the squad (a dark rite): everyone.
        bool awake(Character c) => !c.data.fullyPassedOut;
        var prey = (Squad.AnyHolders() ? Squad.Holders() : Squad.Pursued()).Where(awake).ToList();
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
                Plugin.Log.LogInfo("Scoutmaster: no relic holder in sight (safe ground / relic veil) - he backs off.");
            }
            return;
        }

        // A bodyguard he turned on stays his prey only until the next throw.
        bool hunting = sm.targetForced && current != null
            && (prey.Contains(current) || (current == standIn && marked.Contains(current)));
        if (hunting)
        {
            CloserPrey(sm, current!, marked);
            Unstick(sm, current!);
            return;
        }

        // Just threw someone (or lost them): move on to the next scout, nearest first.
        var next = prey.Where(c => c != last || prey.Count == 1)
            .OrderBy(c => Vector3.Distance(c.Center, Pos(sm)))
            .FirstOrDefault();
        if (next == null) return;
        last = next;
        // Straight to the RPC: SetCurrentTarget ignores "same scout again", and clearing first
        // would null his target for a moment.
        sm.view.RPC("RPCA_SetCurrentTarget", Photon.Pun.RpcTarget.All, next.photonView.ViewID, until - Time.time);
        HuntPatches.Target(sm) = next; // the RPC's setter skips it while the old target is still forced
        Plugin.Log.LogInfo($"Scoutmaster hunting {next.characterName}.");
        ResetStuck(sm, next);
        // Far off (or still in his hiding spot): he steps out of cover close by instead of 90m away.
        if (Vector3.Distance(Pos(sm), next.Center) > 60f * Squad.M) WarpNear(sm, next);
    }

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
        float s = ModConfig.BmScoutmasterRecover.Value;
        if (s <= 0f) return;
        sm.chillForSeconds = s;
        TargetForcedUntil(sm) = Time.time; // free to pick someone new once he's recovered
        restUntil = Time.time + s;
        Plugin.Log.LogInfo($"Scoutmaster {why} - recovering for {s:0}s.");
    }

    /// <summary>Host: a lunge (grab attempt) caught nobody.</summary>
    internal static void MissedLunge(Scoutmaster sm)
    {
        if (instance == null || !Hunting || instance.restUntil > 0f) return;
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
    private float stuckDist, stuckSince;

    private void ResetStuck(Scoutmaster sm, Character prey)
    {
        stuckPos = Pos(sm);
        stuckDist = Vector3.Distance(stuckPos, prey.Center);
        stuckSince = Time.time;
    }

    /// <summary>
    /// Hasn't moved or closed in for a while (caught on a ledge, lost below a wall): he
    /// silently reappears out of sight, a fair way from his prey.
    /// </summary>
    private void Unstick(Scoutmaster sm, Character prey)
    {
        float limit = ModConfig.BmScoutmasterStuck.Value;
        var me = sm.GetComponent<Character>();
        if (limit <= 0f || me == null) return;
        Vector3 pos = Pos(sm);
        float dist = Vector3.Distance(pos, prey.Center);
        if (Busy(sm) || me.warping || dist < 4f * Squad.M
            || Vector3.Distance(pos, stuckPos) > 2f * Squad.M || dist < stuckDist - 2f * Squad.M)
        {
            ResetStuck(sm, prey);
            return;
        }
        if (Time.time - stuckSince < limit) return;
        Plugin.Log.LogInfo($"Scoutmaster stuck for {limit:0}s ({dist / Squad.M:0}m from {prey.characterName}) - reappearing out of sight.");
        WarpAround(sm, prey, ModConfig.BmScoutmasterStuckDistance.Value);
        ResetStuck(sm, prey);
    }

    /// <summary>Out of sight, about ScoutmasterReturnDistance from the prey, near their height.</summary>
    internal static bool WarpNear(Scoutmaster sm, Character prey) => WarpAround(sm, prey, ModConfig.BmScoutmasterNear.Value);

    /// <summary>Out of sight, about this far (m, +-10%) from the prey, near their height.</summary>
    private static bool WarpAround(Scoutmaster sm, Character prey, float metres)
    {
        float d = Mathf.Max(8f, metres) * Squad.M;
        return WarpTo(sm, prey, d * 0.9f, d * 1.1f, -15f * Squad.M, 15f * Squad.M);
    }

    private static bool WarpTo(Scoutmaster sm, Character prey, float minFlat, float maxFlat, float minUp, float maxUp)
    {
        Vector3 c = prey.Center;
        Vector3? seen = null;
        for (int i = 0; i < 48; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(minFlat, maxFlat);
            float top = c.y + maxUp + 30f * Squad.M;
            if (!Physics.Raycast(new Vector3(c.x + d.x, top, c.z + d.y), Vector3.down, out var hit, top - (c.y + minUp) + 1f,
                    HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)) continue;
            float up = hit.point.y - c.y;
            if (up < minUp || up > maxUp || hit.normal.y < 0.45f) continue;
            Vector3 spot = hit.point + Vector3.up;
            if (SomeoneSees(spot)) { seen ??= spot; continue; }
            Warp(sm, spot);
            return true;
        }
        if (seen == null) return false;
        Warp(sm, seen.Value); // nowhere hidden: better seen than stuck
        return true;
    }

    private static void Warp(Scoutmaster sm, Vector3 spot)
    {
        if (Busy(sm)) return; // never teleport away with a scout in his hand
        sm.view.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, spot, false);
        sm.view.RPC("StopClimbingRpc", Photon.Pun.RpcTarget.All, 0f);
        HuntPatches.Discovered(sm) = null!;
    }

    private static bool SomeoneSees(Vector3 pos)
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
        if (me == null) return true;
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
    /// distance" this sets on his target). Under a blood moon our song plays alone: the static
    /// fades out and the distance is never set, so his music fades too. Vanilla otherwise.
    /// </summary>
    [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "DoVisuals")]
    private static bool QuietUnderBloodMoon(Scoutmaster __instance)
    {
        if (!BloodMoon.Active) return true;
        var mat = __instance.mat;
        if (mat != null) mat.SetFloat(StaticStrength, Mathf.MoveTowards(mat.GetFloat(StaticStrength), 0f, Time.deltaTime));
        return false;
    }

    /// <summary>
    /// His pace on a hunt: a holder with company (or a bodyguard) only ever sees him walk; a
    /// holder alone gets sprint bursts with winded gaps. Only ever holds him back - where vanilla
    /// wouldn't sprint, he doesn't.
    /// </summary>
    [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "Chase")]
    private static void Pace(Scoutmaster __instance)
    {
        if (!ScoutmasterHunt.Hunting || !__instance.character.input.sprintIsPressed) return;
        var prey = __instance.currentTarget;
        if (prey == null) return;
        if (Squad.Grouped(prey)) { __instance.character.input.sprintIsPressed = false; return; }
        bool solo = Squad.Solo;
        float burst = Mathf.Max(0.5f, solo ? ModConfig.SoloSprint.Value : ModConfig.BmScoutmasterSprint.Value);
        float rest = solo ? ModConfig.SoloWinded.Value : ModConfig.BmScoutmasterWinded.Value;
        if (rest > 0f && Time.time % (burst + rest) >= burst) __instance.character.input.sprintIsPressed = false;
    }

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

    /// <summary>His "too far, teleport closer" lands 50-70 units out; during a hunt it's ScoutmasterSpawnDistance.</summary>
    [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch(typeof(Scoutmaster), "TeleportCloseToTarget")]
    private static bool CloserDuringHunts(Scoutmaster __instance)
    {
        if (!ScoutmasterHunt.Hunting || __instance.currentTarget == null) return true;
        ref float tp = ref TpCounter(__instance);
        if (tp < 5f) return false;
        if (!ScoutmasterHunt.WarpNear(__instance, __instance.currentTarget)) return true; // no spot: vanilla's
        tp = 0f;
        return false;
    }
}
