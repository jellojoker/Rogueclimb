using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRoguelike;

/// <summary>
/// The crash-site curios - Flare, Bing Bong, Binoculars, Frisbee and Compass - show a card the
/// first time you pick each kind up in a run. Placeholder for now: the items work as in vanilla;
/// their own relic powers come later. Claim takes it, Back leaves it lying there.
/// </summary>
[HarmonyPatch]
internal static class FindCards
{
    /// <summary>Which curio an item is (null = not one).</summary>
    internal static string? KindOf(Item? item)
    {
        if (item == null) return null;
        if (item.GetComponent<Flare>() != null) return "Flare";
        if (item.GetComponent<BingBong>() != null) return "Bing Bong";
        if (item.GetComponentInChildren<Actions_Binoculars>(true) != null) return "Binoculars";
        if (item.GetComponent<Frisbee>() != null) return "Frisbee";
        if (item.GetComponentInChildren<CompassPointer>(true) != null) return "Compass";
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
            if (picking || !ModConfig.FindCards.Value || interactor == null || !interactor.IsLocal) return true;
            if (__instance.itemState != ItemState.Ground || !started.Contains(__instance.GetInstanceID())) return true;
            if (KindOf(__instance) is not { } kind) return true;
            NewRunCheck();
            if (seen.Contains(kind) || ChoiceScreen.IsOpen) return !ChoiceScreen.IsOpen;
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
        ChoiceScreen.Show(new ScreenSpec
        {
            Title = "SOMETHING FOUND",
            Subtitle = name,
            Confirm = "TAKE",
            BackHint = "Leave it",
            Cards = new List<CardSpec>
            {
                new()
                {
                    Style = CardStyle.Perk,
                    Theme = PerkTheme.Explorer,
                    Title = name,
                    Tag = "Curio",
                    TagColor = ChoiceScreen.ThemeGlow(PerkTheme.Explorer),
                    Body = "It survived the crash.\nThe mountain seems to notice.",
                    Footer = "???",
                    Icon = Items.Icon(item),
                },
            },
            StillValid = () => item != null && item.itemState == ItemState.Ground && Vector3.Distance(who.Center, item.Center()) < 6f * Squad.M,
            OnConfirm = _ =>
            {
                if (item == null || who == null) return;
                seen.Add(kind);
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
