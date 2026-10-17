using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Host: relics that fall with a dead scout. The dying scout's game asks the host to drop its
/// amulets (Net.DeathDrop); the host lays them where that scout last stood on solid ground, so
/// they don't vanish off a ledge or out of a tornado. If a dead scout's game never asks (crashed,
/// left, no word in a few seconds), the host drops the relics it last saw them carrying.
/// </summary>
internal class DeathDrops : MonoBehaviour
{
    private sealed class Watch
    {
        public Vector3 ground;
        public bool hasGround, dead, handled;
        public float deadAt;
        public List<(RelicGem gem, RelicTrigger trigger)> relics = new();
    }

    private static readonly Dictionary<int, Watch> watched = new();
    private float nextLook;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Death Drops");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<DeathDrops>();
    }

    internal static void Reset() => watched.Clear();

    private static int ActorOf(Character c) => c.photonView != null && c.photonView.Owner != null ? c.photonView.Owner.ActorNumber : -1;

    private void Update()
    {
        if (!ModConfig.Relics.Value || !ModConfig.RelicDrops.Value || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        if (Time.time < nextLook) return;
        nextLook = Time.time + 0.25f;
        foreach (var c in Character.AllCharacters)
        {
            if (c == null || c.isBot || c.data.isScoutmaster) continue;
            int actor = ActorOf(c);
            if (actor < 0) continue;
            if (!watched.TryGetValue(actor, out var w)) watched[actor] = w = new Watch();
            if (!c.data.dead)
            {
                w.dead = w.handled = false;
                // Their relics as last seen alive (a dying scout clears theirs as they fall).
                if (c.photonView.Owner is { } owner)
                    w.relics = RunState.RelicsOf(owner).Where(r => !r.cursed).Select(r => (r.gem, r.trigger)).ToList();
                var d = c.data;
                if (d.isGrounded && !d.isClimbingAnything && !d.isCarried && !BloodTornado.Carrying(c))
                {
                    w.ground = c.Center;
                    w.hasGround = true;
                }
                continue;
            }
            if (!w.dead) { w.dead = true; w.deadAt = Time.time; }
            // Their own game hasn't asked within a few seconds: drop what they were carrying.
            if (!w.handled && !c.IsLocal && Time.time - w.deadAt > 4f)
            {
                w.handled = true;
                if (w.relics.Count == 0) continue;
                Plugin.Log.LogWarning($"Death drops: no word from {c.characterName}'s game - the host drops their {w.relics.Count} relic(s).");
                foreach (var (gem, trigger) in w.relics)
                {
                    RelicDrops.Add(gem, trigger);
                    Spawn(gem, w, c);
                }
            }
        }
    }

    /// <summary>A dying scout's relics, from their own game: drop them where they last stood.</summary>
    internal static void Received(int actor, int[] gems)
    {
        if (!PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) return;
        var c = Character.AllCharacters.FirstOrDefault(x => x != null && ActorOf(x) == actor);
        if (!watched.TryGetValue(actor, out var w)) watched[actor] = w = new Watch();
        w.handled = true;
        foreach (int g in gems) Spawn((RelicGem)g, w, c);
    }

    private static void Spawn(RelicGem gem, Watch w, Character? c)
    {
        if (Relics.Amulet(gem) is not { } amulet)
        {
            Plugin.Log.LogWarning($"Death drops: no amulet found for {gem}.");
            return;
        }
        Vector3 at = w.hasGround ? w.ground : c != null ? c.Center : Vector3.zero;
        if (at == Vector3.zero) { Plugin.Log.LogWarning($"Death drops: nowhere to drop {gem}."); return; }
        PhotonNetwork.InstantiateItemRoom(amulet.gameObject.name, at + Vector3.up * 0.8f + Random.insideUnitSphere * 0.4f, Quaternion.identity);
        Plugin.Log.LogInfo($"Death drops: {gem} amulet dropped for {(c != null ? c.characterName : "a scout")}{(w.hasGround ? " at their last footing" : "")}.");
    }
}
