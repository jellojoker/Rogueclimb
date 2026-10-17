using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Scouts without Rogue Climb in the lobby. They're told about (every modded scout gets a toast
/// naming the Thunderstore package), left out of crate drafts, relic sharing and zombie spawns, and
/// their vanilla crate opens are ignored - otherwise their crates would spill loot for everyone.
/// A scout counts as modded once their build player property shows up.
/// </summary>
[HarmonyPatch]
internal class Modless : MonoBehaviourPunCallbacks
{
    public const string Package = "TheJelloJokers-Rogue_Climb";

    private static readonly Dictionary<int, float> firstSeen = new();
    private static readonly HashSet<int> warned = new();
    private float nextLook;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Modless");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<Modless>();
    }

    internal static bool HasMod(Photon.Realtime.Player? p) =>
        p == null || p.IsLocal || !PhotonNetwork.InRoom || p.CustomProperties.ContainsKey(DevPanel.BuildKey);

    internal static bool HasMod(Character? c) =>
        c == null || c.photonView == null || HasMod(c.photonView.Owner);

    private void Update()
    {
        if (Time.time < nextLook || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        nextLook = Time.time + 2f;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (!firstSeen.ContainsKey(p.ActorNumber)) firstSeen[p.ActorNumber] = Time.time;
            // Give their properties time to arrive before calling them out.
            if (HasMod(p) || warned.Contains(p.ActorNumber) || Time.time - firstSeen[p.ActorNumber] < 15f) continue;
            warned.Add(p.ActorNumber);
            string who = string.IsNullOrEmpty(p.NickName) ? "A scout" : p.NickName;
            Plugin.Log.LogWarning($"{who} (actor {p.ActorNumber}) doesn't have Rogue Climb.");
            Hud.Toast($"{who} doesn't have Rogue Climb\n{Package} on Thunderstore", ChoiceScreen.Muted);
        }
    }

    public override void OnJoinedRoom() { firstSeen.Clear(); warned.Clear(); }
    public override void OnLeftRoom() { firstSeen.Clear(); warned.Clear(); }

    /// <summary>
    /// A vanilla crate open: modded scouts never send it for a crate with loot (they draft instead),
    /// so it came from someone without the mod - nothing spawns, and the crate stays shut for us.
    /// </summary>
    [HarmonyPrefix, HarmonyPatch(typeof(Luggage), "OpenLuggageRPC")]
    private static bool IgnoreVanillaOpen(Luggage __instance, bool spawnItems)
    {
        if (!spawnItems || __instance is RespawnChest || LuggagePatches.OpeningVanilla || __instance.GetComponent<PerkLuggage>() == null) return true;
        if ((__instance.GetSpawnSpots()?.Count ?? 0) == 0) return true;
        Plugin.Log.LogInfo($"{__instance.displayName}: ignored a vanilla open (a scout without the mod).");
        return false;
    }
}
