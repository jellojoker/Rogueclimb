using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRoguelike;

/// <summary>
/// Cards for the crash-site finds, the first time you pick each kind up in a run (only ones lying
/// in the world - items handed straight to you skip it). The Flare gets its own card, the Lantern
/// a crate's Lantern card; the curios (Bugle, Binoculars, Compass, Frisbee, Bing Bong) offer
/// their starting relic instead.
/// </summary>
[HarmonyPatch]
internal static class FindCards
{
    /// <summary>Which find an item is (null = not one). Curios are handled by <see cref="CurioRelics"/>.</summary>
    internal static string? KindOf(Item? item)
    {
        if (item == null) return null;
        if (item.GetComponent<Flare>() != null) return "Flare";
        if (item.GetComponent<Lantern>() != null && !Items.IsEarnedOnly(item)) return "Lantern";
        return null;
    }

    private static readonly HashSet<string> seen = new();
    private static readonly HashSet<int> started = new();
    private static int scene = -1;
    private static bool picking;

    private static void NewRunCheck()
    {
        int s = SceneManager.GetActiveScene().handle;
        if (s == scene) return;
        scene = s;
        seen.Clear();
    }

    /// <summary>Items made just now (crate claims, gifts) are handed straight over; only ones lying in the world get a card.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(Item), "Start")]
    private static void Started(Item __instance) => started.Add(__instance.GetInstanceID());

    [HarmonyPrefix, HarmonyPatch(typeof(Item), nameof(Item.Interact))]
    private static bool Card(Item __instance, Character interactor)
    {
        try
        {
            if (picking || interactor == null || !interactor.IsLocal) return true;
            if (__instance.itemState != ItemState.Ground || !started.Contains(__instance.GetInstanceID())) return true;
            if (ChoiceScreen.IsOpen) return false;
            NewRunCheck();

            var curio = CurioRelics.KindOf(__instance);
            if (curio != Curio.None && ModConfig.CurioRelics.Value)
            {
                // Unbound: the relic card every time (Back leaves it). Bound: one look at each other curio, then it's just an item.
                var mine = CurioRelics.Mine;
                if (mine == curio || (mine != Curio.None && !seen.Add("relic:" + curio))) return true;
                CurioRelics.ShowCard(__instance, interactor, curio);
                return false;
            }

            if (!ModConfig.FindCards.Value || KindOf(__instance) is not { } kind || seen.Contains(kind)) return true;
            if (!interactor.player.HasEmptySlot(__instance.itemID)) return true; // vanilla says "pockets full"
            Show(__instance, interactor, kind);
            return false;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Find card failed: {e.Message}");
            return true;
        }
    }

    private static void Show(Item item, Character who, string kind)
    {
        string name = item.GetItemName();
        if (string.IsNullOrEmpty(name)) name = kind;
        // The Lantern is a crate's Lantern card: taking it starts the tree the way a crate pick does.
        var offer = kind == "Lantern" ? Offers.ForCrate(item) : null;
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "SOMETHING FOUND",
            Subtitle = name,
            Confirm = "TAKE",
            BackHint = "Leave it",
            Cards = new List<CardSpec>
            {
                offer != null ? CrateCards.ForItem(item) : new()
                {
                    Style = CardStyle.Perk,
                    Theme = PerkTheme.Explorer,
                    Title = name,
                    Tag = "Flare",
                    TagColor = ChoiceScreen.ThemeGlow(PerkTheme.Explorer),
                    Body = "Good for one reroll\nor one emergency drop.",
                    Footer = "Flare",
                    Icon = Items.Icon(item),
                },
            },
            StillValid = () => item != null && item.itemState == ItemState.Ground && Vector3.Distance(who.Center, item.Center()) < 6f * Squad.M,
            OnConfirm = _ =>
            {
                if (item == null || who == null) return;
                seen.Add(kind);
                if (offer is { Kind: OfferKind.TreeLevel, Tree: { } tree }) LuggagePatches.LevelUp(tree, offer.Level, offer.Branch);
                picking = true;
                try { item.Interact(who); }
                finally { picking = false; }
                Plugin.Log.LogInfo($"Find card: took the {kind}.");
            },
            OnBack = () => { },
            OnCancelled = () => { },
        });
    }
}
