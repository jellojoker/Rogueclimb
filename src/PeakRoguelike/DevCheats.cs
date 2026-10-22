using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakRoguelike;

/// <summary>
/// Host dev cheats behind the F8 panel: noclip flight, god mode, infinite stamina, healing,
/// reviving and warping. Flight: look where you want to go, WASD, Space up, Ctrl down,
/// Shift = fast. Turning it off inside a wall lifts you out onto the surface.
/// </summary>
[HarmonyPatch]
internal static class DevCheats
{
    internal static bool Fly { get; private set; }
    internal static bool God { get; private set; }
    internal static bool InfiniteStamina { get; private set; }
    private static Character? flyer;

    internal static void SetFly(bool on)
    {
        var me = Character.localCharacter;
        if (me == null) { Fly = false; return; }
        if (on)
        {
            if (me.data.dead) return;
            me.refs.climbing.StopAnyClimbing();
            // No stumble on take-off: the game would read the lost ground as a fall.
            me.data.fallSeconds = 0f;
            me.data.sinceGrounded = 0f;
            me.refs.ragdoll.ToggleCollision(false);
            flyer = me;
            Fly = true;
            Hud.Toast("Fly ON - WASD, Space up, Ctrl down, Shift fast", ChoiceScreen.Cream);
            return;
        }
        Fly = false;
        flyer = null;
        foreach (var part in me.refs.ragdoll.partList) part.Rig.linearVelocity = Vector3.zero;
        LiftOutOfWalls(me); // before collision comes back, so our own body doesn't count
        me.refs.ragdoll.ToggleCollision(true);
        Hud.Toast("Fly OFF", ChoiceScreen.Cream);
    }

    internal static void SetGod(bool on)
    {
        var me = Character.localCharacter;
        if (me == null) return;
        God = on;
        if (on) me.refs.afflictions.ClearAllStatus(excludeCurse: false, excludePetrify: false);
        me.statusesLocked = on;
        Hud.Toast(on ? "God mode ON - no damage or ailments" : "God mode OFF", ChoiceScreen.Cream);
    }

    internal static void SetInfiniteStamina(bool on)
    {
        var me = Character.localCharacter;
        if (me == null) return;
        InfiniteStamina = on;
        if (on) me.data.currentStamina = 1f;
        me.infiniteStam = on;
        Hud.Toast(on ? "Infinite stamina ON" : "Infinite stamina OFF", ChoiceScreen.Cream);
    }

    /// <summary>New character (new level / respawn): the cheats don't carry over.</summary>
    internal static void Tick()
    {
        var me = Character.localCharacter;
        if (Fly && (me == null || me != flyer || me.data.dead))
        {
            Fly = false;
            flyer = null;
            if (me != null) me.refs.ragdoll.ToggleCollision(true);
        }
        if (me == null) { God = InfiniteStamina = false; return; }
        if (God && !me.statusesLocked) God = false;
        if (InfiniteStamina && !me.infiniteStam) InfiniteStamina = false;
        if (Fly && (me.data.isClimbing || me.data.isRopeClimbing || me.data.isVineClimbing)) me.refs.climbing.StopAnyClimbing();
    }

    internal static void Heal()
    {
        var me = Character.localCharacter;
        if (me == null) return;
        me.refs.afflictions.ClearAllStatus(excludeCurse: false, excludePetrify: false);
        me.AddStamina(1f);
        Hud.Toast("Healed - every ailment cleared", ChoiceScreen.Cream);
    }

    internal static void ReviveMe()
    {
        var me = Character.localCharacter;
        if (me == null) return;
        if (!me.data.dead && !me.data.fullyPassedOut) { Hud.Toast("You're not down", ChoiceScreen.Cream); return; }
        Character.Revive();
    }

    /// <summary>Everyone who's dead or passed out gets up next to you.</summary>
    internal static void ReviveAll()
    {
        var me = Character.localCharacter;
        if (me == null) return;
        int n = 0;
        foreach (var c in Character.AllCharacters.Where(c => c != null && !c.isBot && (c.data.dead || c.data.fullyPassedOut)))
        {
            Vector3 at = c == me ? me.debugLastDied : me.Center + Quaternion.Euler(0f, n * 70f, 0f) * Vector3.forward * 1.5f + Vector3.up;
            c.view.RPC("RPCA_ReviveAtPosition", RpcTarget.All, at, true, -1);
            n++;
        }
        Hud.Toast(n == 0 ? "Nobody needs reviving" : $"Revived {n}", ChoiceScreen.Cream);
    }

    internal static void WarpTo(Vector3 pos)
    {
        var me = Character.localCharacter;
        if (me == null) return;
        me.photonView.RPC("WarpPlayerRPC", RpcTarget.All, pos, true);
    }

    internal static void WarpToCampfire()
    {
        try
        {
            var fire = MapHandler.CurrentCampfire;
            if (fire == null) { Hud.Toast("No campfire on this section", ChoiceScreen.Cream); return; }
            WarpTo(fire.transform.position + fire.transform.forward * 2.5f + Vector3.up * 1.5f);
        }
        catch (System.Exception e)
        {
            Hud.Toast("Couldn't find the campfire", ChoiceScreen.Cream);
            Plugin.Log.LogWarning($"Warp to campfire failed: {e.Message}");
        }
    }

    /// <summary>Host: the whole squad jumps to the next biome (the game's own debug jump).</summary>
    internal static void NextBiome()
    {
        if (!MapHandler.Exists) return;
        int next = (int)MapHandler.CurrentSegmentNumber + 1;
        if (next > (int)Segment.Peak) { Hud.Toast("Already at the peak", ChoiceScreen.Cream); return; }
        MapHandler.JumpToSegment((Segment)next);
        Hud.Toast($"Squad jumps to {(Segment)next}", ChoiceScreen.Cream);
    }

    /// <summary>Set the clock (and sync it to everyone).</summary>
    internal static void SetTime(float hour)
    {
        var dnm = DayNightManager.instance;
        if (dnm == null) return;
        dnm.setTimeOfDay(hour);
        dnm.photonView.RPC("RPCA_SyncTime", RpcTarget.All, dnm.dayCount, hour);
    }

    /// <summary>Ended up inside rock: put us on top of whatever we're in.</summary>
    private static void LiftOutOfWalls(Character me)
    {
        Vector3 c = me.Center;
        bool inside = Physics.OverlapSphere(c, 0.35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            .Any(col => col.GetComponentInParent<Character>() == null);
        if (!inside) return;
        var hit = Physics.RaycastAll(c + Vector3.up * 80f, Vector3.down, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            .Where(h => h.collider.GetComponentInParent<Character>() == null)
            .OrderBy(h => h.distance)
            .FirstOrDefault();
        if (hit.collider == null) return;
        WarpTo(hit.point + Vector3.up * 1.2f);
        Hud.Toast("You were inside a wall - lifted out", ChoiceScreen.Cream);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterMovement), "FixedUpdate")]
    private static void Flight(CharacterMovement __instance)
    {
        if (!Fly || flyer == null || __instance.gameObject != flyer.gameObject) return;
        var kb = Keyboard.current;
        var cam = MainCamera.instance != null ? MainCamera.instance.transform : null;
        Vector3 v = Vector3.zero;
        if (kb != null && cam != null && !ChoiceScreen.IsOpen && !DevPanel.Interactive)
        {
            Vector3 fwd = cam.forward, right = cam.right;
            right.y = 0f;
            if (kb.wKey.isPressed) v += fwd;
            if (kb.sKey.isPressed) v -= fwd;
            if (kb.dKey.isPressed) v += right.normalized;
            if (kb.aKey.isPressed) v -= right.normalized;
            if (kb.spaceKey.isPressed) v += Vector3.up;
            if (kb.leftCtrlKey.isPressed) v -= Vector3.up;
            v = v.normalized * ModConfig.FlySpeed.Value * (kb.leftShiftKey.isPressed ? 3f : 1f);
        }
        foreach (var part in flyer.refs.ragdoll.partList)
        {
            part.Rig.linearVelocity = v;
            part.Rig.angularVelocity = Vector3.zero;
        }
        flyer.data.sinceGrounded = 0f;
        flyer.data.sinceJump = 0f;
        flyer.data.fallSeconds = 0f;
    }

    /// <summary>
    /// Flying: the game never knocks you over or hurts you for "falling" (with the panel closed its own
    /// movement still runs, and the first frame without collision read as a fall - a second's freeze).
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Character), nameof(Character.RPCA_Fall))]
    private static bool NoFallWhileFlying(Character __instance) => !(Fly && __instance == flyer);

    [HarmonyPrefix, HarmonyPatch(typeof(CharacterMovement), "CheckFallDamage")]
    private static bool NoFallDamageWhileFlying(CharacterMovement __instance) =>
        !(Fly && flyer != null && __instance.gameObject == flyer.gameObject);
}
