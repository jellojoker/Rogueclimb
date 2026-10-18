using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>
/// The flare. Carried unlit, it rerolls a whole crate (the crate screen gets a REROLL button and
/// the flare is spent). Lit anywhere but the peak, a big crate slams down from the sky beside you -
/// sometimes an Ancient Luggage. Lit at the peak it calls the helicopter, as always.
/// </summary>
[HarmonyPatch]
internal static class Flares
{
    private static bool IsFlare(ItemSlot? s) =>
        s != null && !s.IsEmpty() && s.prefab != null && s.prefab.GetComponent<Flare>() != null && !Lit(s.data);

    private static bool Lit(ItemInstanceData? data) =>
        data != null && data.TryGetDataEntry<BoolItemData>(DataEntryKey.FlareActive, out var lit) && lit.Value;

    /// <summary>An unlit flare in your pockets (or riding in your hand with full pockets).</summary>
    internal static bool Carrying() =>
        ModConfig.FlareReroll.Value && Player.localPlayer is { } p && p.itemSlots.Append(p.tempFullSlot).Any(IsFlare);

    /// <summary>Spends one unlit flare - one you aren't holding, if you can.</summary>
    internal static bool Spend()
    {
        var p = Player.localPlayer;
        var me = Character.localCharacter;
        if (p == null || me == null) return false;
        var sel = me.refs.items.currentSelectedSlot;
        var slots = p.itemSlots.Append(p.tempFullSlot).Where(IsFlare).ToList();
        var s = slots.FirstOrDefault(x => !(sel.IsSome && sel.Value == x.itemSlotID)) ?? slots.FirstOrDefault();
        if (s == null) return false;
        if (Stacks.TakeOne(p, s.itemSlotID)) return true;
        bool held = sel.IsSome && sel.Value == s.itemSlotID;
        p.EmptySlot(Optionable<byte>.Some(s.itemSlotID));
        if (held) me.refs.items.EquipSlot(Optionable<byte>.None);
        return true;
    }

    /// <summary>The flare's own smoke and sparks (for marking a landed supply drop).</summary>
    internal static GameObject? SmokePrefab()
    {
        if (smoke != null) return smoke;
        var flare = Items.Database.Select(i => i != null ? i.GetComponent<Flare>() : null).FirstOrDefault(f => f != null && f.flareVFXPrefab != null);
        smoke = flare?.flareVFXPrefab.gameObject;
        return smoke;
    }

    private static GameObject? smoke;

    // --- lit: a supply drop --------------------------------------------------------------------

    private static readonly HashSet<Flare> answered = new();

    [HarmonyPostfix, HarmonyPatch(typeof(Flare), nameof(Flare.LightFlare))]
    private static void OnLit(Flare __instance)
    {
        try
        {
            if (!ModConfig.FlareSupplyDrop.Value || __instance == null || __instance.item == null) return;
            var me = __instance.item.holderCharacter;
            if (me == null || !me.IsLocal) return;
            if (Singleton<MountainProgressHandler>.Instance is { } mp && mp.IsAtPeak(me.Center)) return; // the helicopter's flare
            if (!answered.Add(__instance)) return;
            Plugin.Log.LogInfo($"Flare lit by {me.characterName}: calling a supply drop.");
            SupplyDrop.Request(me.Center, me.data.lookDirection_Flat, me.characterName);
        }
        catch (System.Exception e) { Plugin.Log.LogError($"Flare supply drop failed: {e}"); }
    }
}
