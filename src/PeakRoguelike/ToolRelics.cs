using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Tool relics fire when you get OFF a tool, not when you put it down: letting go of any
/// rope (the map's too) or chain, a piton (0.3s is enough), stepping off a beanstalk, a glide
/// ending, the last balloon popping, a jetpack / rocket burn ending - after at least a second on it.
///
/// Placed tools belong to whoever placed them: anyone who lets go of one also gets the
/// placer's movement relics (the user takes the glass; the placer pays nothing - see Relics.Fire).
/// Ropes know their creator; pitons and beanstalks are room objects, so their placers are
/// shared as positions (Net.Placed).
/// </summary>
internal class ToolRelics : MonoBehaviour
{
    private const float MinUse = 1f;
    private const float GlideMin = 0.5f, GlideGrace = 0.3f;
    private float glideGap;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Tool Relics");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<ToolRelics>();
    }

    /// <summary>Tools whose relics fire on getting off them (everything else fires on use).</summary>
    internal static bool FiresOnDismount(Item item) =>
        item.GetComponent<RopeSpool>() || item.GetComponent<RopeShooter>() || item.GetComponent<VineShooter>() || item.GetComponent<ClimbingSpikeComponent>()
        || item.GetComponentInChildren<Glider>(true) || item.GetComponent<Balloon>() || item.GetComponent<MagicBean>()
        || item.GetComponent<JetpackItem>();

    // --- who placed what -----------------------------------------------------------------------

    private static readonly List<(Vector3 pos, int actor)> placements = new();

    internal static void AddPlacement(Vector3 pos, int actor)
    {
        placements.Add((pos, actor));
        if (placements.Count > 400) placements.RemoveAt(0);
    }

    private static int PlacerNear(Vector3 pos, float radius)
    {
        int best = 0;
        float bestD = radius;
        foreach (var (p, a) in placements)
        {
            float d = Vector3.Distance(p, pos);
            if (d <= bestD) { bestD = d; best = a; }
        }
        return best;
    }

    // --- watching the local scout ------------------------------------------------------------

    private static int Me => PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;

    private const float PitonMin = 0.3f;
    private Rope? rope; private float ropeTime; private int ropePlacer;
    private JungleVine? vine; private float vineTime; private int vinePlacer;
    private ClimbHandle? handle; private float handleTime; private int handlePlacer;
    private MagicBeanVine? bean; private float beanTime; private float nextBeanCheck; private bool onBean;
    private float glideTime, balloonTime, rocketTime;
    private bool wasRocket;
    internal static bool Jetting; internal static float JetStart, LastJet;

    private void Update()
    {
        var me = Character.localCharacter;
        if (me == null || me.data.dead || !ModConfig.Relics.Value || RunState.Relics.Count == 0 && !AnyOtherRelics())
        {
            rope = null; vine = null; handle = null; bean = null; vineTime = 0f; ropeTime = handleTime = beanTime = glideTime = glideGap = balloonTime = rocketTime = 0f;
            onBean = wasRocket = Jetting = false;
            return;
        }
        float dt = Time.deltaTime;

        // Any rope, the map's own too (a scout's rope also fires its placer's relics on us).
        var held = me.data.isRopeClimbing ? me.data.heldRope : null;
        if (held != rope)
        {
            if (rope != null) Leave("rope", ropeTime, MinUse, ropePlacer);
            rope = held;
            ropeTime = 0f;
            ropePlacer = held != null && held.photonView != null ? held.photonView.CreatorActorNr : 0;
        }
        if (rope != null) ropeTime += dt;

        // Chains (the Chain Launcher's) and vines: the game climbs them as vines, not ropes.
        var v = me.data.isVineClimbing ? me.data.heldVine : null;
        if (v != vine)
        {
            if (vine != null) Leave("chain / vine", vineTime, MinUse, vinePlacer);
            vine = v;
            vineTime = 0f;
            vinePlacer = v != null && v.photonView != null ? v.photonView.CreatorActorNr : 0;
        }
        if (vine != null) vineTime += dt;

        // Pitons: any handle - you're rarely on one for long, so a short hold counts.
        var h = me.data.currentClimbHandle;
        if (h != handle)
        {
            if (handle != null) Leave("piton", handleTime, PitonMin, handlePlacer);
            handle = h;
            handleTime = 0f;
            handlePlacer = h != null ? PlacerNear(h.transform.position, 1.5f) : 0;
        }
        if (handle != null) handleTime += dt;

        // Beanstalks: climbing on the grown vine.
        if (Time.time >= nextBeanCheck)
        {
            nextBeanCheck = Time.time + 0.2f;
            var vine = me.data.isClimbing ? BeanAt(me.data.climbPos) : null;
            if (vine != bean)
            {
                if (bean != null && beanTime >= MinUse) Dismount("beanstalk", PlacerNear(bean.transform.position, 3f), always: true);
                bean = vine;
                beanTime = 0f;
            }
            onBean = bean != null;
        }
        if (onBean) beanTime += dt;

        // Gliding. The glide state can flicker off for a frame or two (brushing terrain): a gap
        // under GlideGrace is still the same glide.
        bool gliding = me.data.currentItem != null && me.data.currentItem.GetComponentInChildren<Glider>() is { } g && g._gliding;
        if (gliding) { glideTime += dt; glideGap = 0f; }
        else if (glideTime > 0f)
        {
            glideGap += dt;
            if (glideGap >= GlideGrace)
            {
                Plugin.Log.LogInfo($"Tool relics: glide ended after {glideTime:0.0}s{(glideTime < GlideMin ? " (too short to count)" : "")}.");
                if (glideTime >= GlideMin) Dismount("glide", 0, always: true);
                glideTime = glideGap = 0f;
            }
        }

        // Balloons tied to you (not the one in your hand).
        var bal = me.refs.balloons;
        bool floating = bal != null && bal.currentBalloonCount - bal.heldBalloonCount > 0;
        if (floating) balloonTime += dt;
        else { if (balloonTime >= MinUse) Dismount("balloons", 0, always: true); balloonTime = 0f; }

        // Jetpack burns (JetpackRPC runs every frame of thrust) and rocket packs.
        if (Jetting && Time.time - LastJet > 0.4f)
        {
            Jetting = false;
            float burn = LastJet - JetStart;
            Plugin.Log.LogInfo($"Tool relics: jetpack burn ended after {burn:0.0}s.");
            if (burn >= MinUse) Dismount("jetpack", 0, always: true);
        }
        bool rocket = me.refs.movement != null && me.refs.movement.rocketActive;
        if (rocket) rocketTime += dt;
        else if (wasRocket) { if (rocketTime >= MinUse) Dismount("rocket", 0, always: true); rocketTime = 0f; }
        wasRocket = rocket;
    }

    /// <summary>Someone else may hold relics that our use of their tool would fire.</summary>
    private static bool AnyOtherRelics() =>
        PhotonNetwork.InRoom && PhotonNetwork.PlayerListOthers.Any(p => RunState.RelicsOf(p).Count > 0);

    private static MagicBeanVine[] vines = new MagicBeanVine[0];
    private static float nextVineScan;

    private static MagicBeanVine? BeanAt(Vector3 pos)
    {
        if (Time.time >= nextVineScan)
        {
            nextVineScan = Time.time + 2f;
            vines = FindObjectsByType<MagicBeanVine>(FindObjectsSortMode.None);
        }
        foreach (var v in vines)
        {
            if (v == null) continue;
            foreach (var c in v.GetComponentsInChildren<Collider>())
                if (c != null && c.enabled && Vector3.Distance(c.ClosestPoint(pos), pos) < 0.8f) return v;
        }
        return null;
    }

    /// <summary>Let go of a rope / chain / piton: logged either way, fires if we were on it long enough.</summary>
    private static void Leave(string what, float time, float min, int placer)
    {
        if (time < min)
        {
            Plugin.Log.LogInfo($"Tool relics: let go of a {what} after {time:0.0}s (under {min:0.#}s - doesn't count).");
            return;
        }
        Plugin.Log.LogInfo($"Tool relics: let go of a {what} after {time:0.0}s ({(placer > 0 ? $"placed by actor {placer}" : "map / unknown placer")}).");
        Dismount(what, placer, always: true);
    }

    /// <summary>Got off a tool: our own tool relics fire, and the placer's fire on us.</summary>
    private static void Dismount(string what, int placer, bool always = false)
    {
        Plugin.Log.LogInfo($"Tool relics: got off a {what}{(placer > 0 ? $" placed by actor {placer}" : "")}.");
        if (placer > 0 || always) Relics.Trigger(RelicTrigger.Movement, null, what);
        if (placer > 0 && placer != Me && PhotonNetwork.InRoom) Net.ToolUsed(placer);
    }
}

[HarmonyPatch]
internal static class ToolRelicPatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(CharacterItems), "HammerClimbingSpike")]
    private static void PitonPlaced(CharacterItems __instance, RaycastHit hit)
    {
        if (__instance.character.IsLocal) Net.Placed(hit.point, PhotonNetwork.LocalPlayer?.ActorNumber ?? -1);
    }

    private static readonly AccessTools.FieldRef<MagicBean, bool> Planted = AccessTools.FieldRefAccess<MagicBean, bool>("isPlanted");
    private static readonly AccessTools.FieldRef<Item, Character> LastHolder = AccessTools.FieldRefAccess<Item, Character>("lastHolderCharacter");

    /// <summary>Host: a bean is about to sprout - remember who threw it.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(MagicBean), nameof(MagicBean.Update))]
    private static void BeanSprouting(MagicBean __instance)
    {
        if (!PhotonNetwork.IsMasterClient || !__instance.photonView.IsMine || !Planted(__instance)) return;
        if (__instance.timeToPlant - Time.deltaTime > 0f) return;
        var thrower = __instance.item != null ? LastHolder(__instance.item) : null;
        if (thrower?.photonView?.Owner is { } owner) Net.Placed(__instance.transform.position, owner.ActorNumber);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.JetpackRPC))]
    private static void Thrust(CharacterMovement __instance, float fuel)
    {
        if (fuel <= 0f || Character.localCharacter == null || Character.localCharacter.refs.movement != __instance) return;
        if (!ToolRelics.Jetting) { ToolRelics.Jetting = true; ToolRelics.JetStart = Time.time; }
        ToolRelics.LastJet = Time.time;
    }
}
