using System;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using STATUS = CharacterAfflictions.STATUSTYPE;

namespace PeakRoguelike;

/// <summary>
/// Food cooked on a Portable Stove (a small campfire in the game's code) heals a little glass
/// when eaten - burnt or not, whoever eats it. The mark lives in room properties keyed by the
/// item's instance guid, like item levels, so it travels with the food.
/// </summary>
[HarmonyPatch]
internal static class Stove
{
    private const string Prefix = "prl.sv.";

    private static readonly AccessTools.FieldRef<Campfire, Item> Cooking = AccessTools.FieldRefAccess<Campfire, Item>("currentlyCookingItem");

    private static bool IsStove(Campfire fire) =>
        fire.gameObject.name.IndexOf("Stove", StringComparison.OrdinalIgnoreCase) >= 0
        || (fire.nameOverride?.IndexOf("Stove", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

    private static string Key(Guid guid) => Prefix + guid.ToString("N");

    /// <summary>The Portable Stove you carry (its card mentions the glass).</summary>
    internal static bool IsStoveItem(Item? item) => item != null && Items.Key(item).Contains("stove");

    private static bool Cooked(Item item) =>
        item.data != null && PhotonNetwork.InRoom
        && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(Key(item.data.guid), out object v) && v is true;

    /// <summary>The cook's game marks the food once the cooking cast finishes on a lit stove.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Campfire), nameof(Campfire.Interact_CastFinished))]
    private static void CookedOn(Campfire __instance)
    {
        try
        {
            if (!__instance.Lit || !IsStove(__instance) || !PhotonNetwork.InRoom) return;
            var item = Cooking(__instance);
            if (item == null || item.data == null || !Relics.IsFood(item)) return;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [Key(item.data.guid)] = true });
            Plugin.Log.LogInfo($"Stove: cooked {item.gameObject.name} - it will heal glass.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Stove cooking hook failed: {e.Message}"); }
    }

    /// <summary>One bite (or the whole thing) of stove food: its share of the glass heal.</summary>
    internal static void Eaten(Item item, Character eater)
    {
        try
        {
            int glass = ModConfig.StoveGlass.Value;
            if (glass <= 0 || item == null || eater == null || !eater.IsLocal || !Cooked(item)) return;
            int bites = Mathf.Max(1, item.totalUses);
            float heal = (float)glass / bites;
            eater.refs.afflictions.SubtractStatus(STATUS.Petrify, heal * 0.01f);
            Plugin.Log.LogInfo($"Stove food: {item.gameObject.name} healed {heal:0.#} glass.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Stove food heal failed: {e.Message}"); }
    }
}

/// <summary>
/// Scout Cookies: every bite melts a little glass and heals a little injury, flat amounts on top of
/// the food. They stay food for relics (the heal is the cookie's own thing).
/// </summary>
internal static class Cookies
{
    internal static bool Is(Item? item) => item != null && Items.Key(item).Contains("cookie");

    /// <summary>One bite (the last one too).</summary>
    internal static void Eaten(Item item, Character eater)
    {
        try
        {
            if (!ModConfig.ItemTrees.Value || !Is(item) || eater == null || !eater.IsLocal) return;
            var aff = eater.refs.afflictions;
            int glass = Mathf.Max(0, ModConfig.CookieGlass.Value);
            float injury = Mathf.Max(0f, ModConfig.CookieInjury.Value) * 0.01f;
            if (glass > 0 && eater.data.petrifyAmount > 0) aff.SubtractStatus(STATUS.Petrify, glass * 0.01f);
            if (injury > 0f) aff.SubtractStatus(STATUS.Injury, injury);
            Plugin.Log.LogInfo($"Scout Cookies bite: {glass} glass, {injury * 100f:0.#} injury.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Cookie heal failed: {e.Message}"); }
    }
}
