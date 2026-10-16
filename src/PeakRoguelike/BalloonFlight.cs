using HarmonyLib;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// The balloon tree. From level 2 a tied balloon flies you: straight up at one steady speed (the
/// longest flight rises exactly one max-length vanilla rope), hovering at its height cap, steering
/// more freely, until time runs out and every tied balloon pops. Level 3 ties two balloons.
/// Jumping in the air with balloons tied pops one for a hop (the game resets your fall on a pop).
/// Red sun: flights and hops at its movement cap.
/// </summary>
[HarmonyPatch]
internal static class BalloonFlight
{
    /// <summary>When the local flight ends (-1 = not flying) and the height it can't climb past.</summary>
    private static float until = -1f, ceiling;
    /// <summary>A pop-jump plays out under normal gravity until then.</summary>
    private static float hopUntil;

    private const float Accel = 30f;

    private static bool Active(Character c) => until > 0f && c.IsLocal;

    private static readonly AccessTools.FieldRef<CharacterMovement, Character> MoverOf = AccessTools.FieldRefAccess<CharacterMovement, Character>("character");

    // --- tying ---------------------------------------------------------------------------------

    /// <summary>Before the balloons are tied, so they know to wait for the flight to pop them.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Action_Balloon), nameof(Action_Balloon.RunAction))]
    private static void Tying(Action_Balloon __instance)
    {
        var item = __instance.item;
        var c = item != null ? item.holderCharacter : null;
        if (c == null || !c.IsLocal) return;
        float secs = Stats.BalloonFlight(Items.IsBalloonBunch(item!), Stats.For(item!, TreeId.Balloons).level) * SunCaps.MoveCap;
        if (secs <= 0f) return;
        float top = c.Center.y + Stats.BalloonRiseSpeed * secs;
        if (until < 0f) ceiling = top;
        else ceiling = Mathf.Max(ceiling, top);
        until = Mathf.Max(until, Time.time + secs);
    }

    /// <summary>A level-3 balloon ties a second one.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Action_Balloon), nameof(Action_Balloon.RunAction))]
    private static void Tied(Action_Balloon __instance)
    {
        var item = __instance.item;
        var c = item != null ? item.holderCharacter : null;
        if (c == null || !c.IsLocal || __instance.balloon == null || __instance.balloon.isBunch) return;
        if (Stats.BalloonTies(Stats.For(item!, TreeId.Balloons).level) < 2) return;
        var colours = c.refs.balloons.balloonColors;
        c.refs.balloons.TieNewBalloon(colours != null && colours.Length > 0 ? (__instance.balloon.colorIndex + 1) % colours.Length : 0);
    }

    /// <summary>Balloons tied for a flight wait for it to end (we pop them all together).</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(TiedBalloon), nameof(TiedBalloon.RPC_Init))]
    private static void Lifetime(TiedBalloon __instance)
    {
        if (until > 0f && __instance.photonView.IsMine) __instance.popTime = 9999f;
    }

    // --- flying --------------------------------------------------------------------------------

    /// <summary>No balloon float while flying - the climb below is the whole lift.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CharacterBalloons), "FixedUpdate")]
    private static void NoFloat(CharacterBalloons __instance)
    {
        var c = __instance.character;
        if (c != null && Active(c) && Time.time >= hopUntil) c.refs.movement.balloonFloatMultiplier = 0f;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), "FixedUpdate")]
    private static void Fly(CharacterMovement __instance)
    {
        var c = MoverOf(__instance);
        if (c == null || !Active(c)) return;
        var bal = c.refs.balloons;
        if (Time.time >= until || bal.tiedBalloons.Count == 0 || c.data.dead || !c.data.fullyConscious)
        {
            Land(c);
            return;
        }
        if (c.data.isClimbingAnything || Time.time < hopUntil) return;

        // Climb at the one speed, ease to a hover at the cap (and back down to it after a hop).
        float speed = Stats.BalloonRiseSpeed;
        float target = Mathf.Clamp((ceiling - c.Center.y) * 2f, -speed, speed);
        float step = Accel * Time.fixedDeltaTime;
        foreach (var part in c.refs.ragdoll.partList)
        {
            var rb = part.Rig;
            if (rb == null || rb.isKinematic) continue;
            var v = rb.linearVelocity;
            v.y = Mathf.MoveTowards(v.y, target, step);
            rb.linearVelocity = v;
        }
        // Flying isn't falling (same floor vanilla balloons give the fall timer).
        if (c.data.sinceGrounded > 0.5f) c.data.sinceGrounded = 0.5f;
    }

    /// <summary>Time's up: every balloon we tied pops at once.</summary>
    private static void Land(Character c)
    {
        until = -1f;
        foreach (var b in c.refs.balloons.tiedBalloons.ToArray())
            if (b != null && b.photonView.IsMine) b.Pop();
    }

    /// <summary>Steer more freely while a balloon flies you.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), "GetMovementForce")]
    private static void Steer(CharacterMovement __instance, ref float __result)
    {
        var c = MoverOf(__instance);
        if (c != null && Active(c) && !c.data.isGrounded) __result *= Mathf.Max(0f, ModConfig.BalloonAirControl.Value);
    }

    // --- pop-jump ------------------------------------------------------------------------------

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterInput), "Sample")]
    private static void PopJump(CharacterInput __instance)
    {
        if (!__instance.jumpWasPressed || !Stats.On || ModConfig.BalloonPopJump.Value <= 0f) return;
        var c = Character.localCharacter;
        if (c == null || c.input != __instance || c.data.isGrounded || c.data.isClimbingAnything || !c.data.fullyConscious) return;
        if (c.data.sinceGrounded <= 0.2f) return; // the game's own jump still works
        TiedBalloon? last = null;
        foreach (var b in c.refs.balloons.tiedBalloons)
            if (b != null && b.photonView.IsMine) last = b;
        if (last == null) return;
        last.Pop();
        hopUntil = Time.time + 0.5f;

        float kick = ModConfig.BalloonPopJump.Value * SunCaps.MoveCap;
        foreach (var part in c.refs.ragdoll.partList)
        {
            var rb = part.Rig;
            if (rb == null || rb.isKinematic) continue;
            var v = rb.linearVelocity;
            v.y = Mathf.Max(v.y, kick);
            rb.linearVelocity = v;
        }
    }
}
