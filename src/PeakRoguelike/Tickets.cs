using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

/// <summary>
/// The run settings on the airport ticket, under the relic order: Blood moons (Relay / Stacked),
/// Crates and Relics (Shared / First come), Harsh suns (Shore only / Every biome). The second option of each is the harder one. The host's
/// config decides; the host publishes them ("prl.tk", a bit each) so every ticket and every crate
/// follows the host.
/// </summary>
internal class Tickets : MonoBehaviour
{
    private const string Key = "prl.tk";
    private const int StackedBit = 1, CratesBit = 2, RelicsBit = 4, HarshBit = 8;
    private float next;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Ticket settings");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<Tickets>();
    }

    private static int Own =>
        (ModConfig.BmRelay.Value ? 0 : StackedBit)
        | (ModConfig.CratesFirstCome.Value ? CratesBit : 0)
        | (ModConfig.DevShareRelics.Value ? 0 : RelicsBit)
        | (ModConfig.HarshSunsEverywhere.Value ? HarshBit : 0);

    /// <summary>What's in force: our own settings as host (or offline), else what the host published.</summary>
    private static int Flags
    {
        get
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return Own;
            // A host without this setting (an older build) plays the defaults.
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(Key, out object v) && v is int f ? f : 0;
        }
    }

    internal static bool StackedMoons => (Flags & StackedBit) != 0;
    internal static bool CratesFirstCome => (Flags & CratesBit) != 0;
    internal static bool RelicsFirstCome => (Flags & RelicsBit) != 0;
    /// <summary>Each sun's Shore lesson (glass, gravity, drowsiness, a withered card) in every biome.</summary>
    internal static bool HarshSunsEverywhere => (Flags & HarshBit) != 0;

    internal static bool CanChange => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

    internal static void ToggleMoons() { if (CanChange) ModConfig.BmRelay.Value = !ModConfig.BmRelay.Value; Logged(); }
    internal static void ToggleCrates() { if (CanChange) ModConfig.CratesFirstCome.Value = !ModConfig.CratesFirstCome.Value; Logged(); }
    internal static void ToggleRelics() { if (CanChange) ModConfig.DevShareRelics.Value = !ModConfig.DevShareRelics.Value; Logged(); }
    internal static void ToggleHarshSuns() { if (CanChange) ModConfig.HarshSunsEverywhere.Value = !ModConfig.HarshSunsEverywhere.Value; Logged(); }

    private static void Logged() =>
        Plugin.Log.LogInfo($"Ticket: blood moons {(StackedMoons ? "Stacked" : "Relay")}, crates {(CratesFirstCome ? "First come" : "Shared")}, relics {(RelicsFirstCome ? "First come" : "Shared")}, harsh suns {(HarshSunsEverywhere ? "Every biome" : "Shore only")}.");

    private void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.5f;
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom is not { } room) return;
        int own = Own;
        if (!room.CustomProperties.TryGetValue(Key, out object v) || v is not int f || f != own)
            room.SetCustomProperties(new Hashtable { [Key] = own });
    }
}
