using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>
/// Log only: how often the game's storms (rain, snow, wind) come and go. Their timings live in the
/// map data, so the log is the way to read them.
/// </summary>
[HarmonyPatch]
internal static class Weather
{
    private static string Kind(WindChillZone z)
    {
        var kinds = z.GetComponentsInChildren<StormVisual>(true).Select(v => v.stormType.ToString()).Distinct().ToArray();
        return kinds.Length > 0 ? string.Join("/", kinds) : "no visual";
    }

    [HarmonyPostfix, HarmonyPatch(typeof(WindChillZone), "Awake")]
    private static void Found(WindChillZone __instance)
    {
        try
        {
            Plugin.Log.LogInfo($"Weather: storm zone '{__instance.name}' ({Kind(__instance)}) - on {__instance.windTimeRangeOn.x:0}-{__instance.windTimeRangeOn.y:0}s, off {__instance.windTimeRangeOff.x:0}-{__instance.windTimeRangeOff.y:0}s.");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Weather log failed: {e.Message}"); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(WindChillZone), "RPCA_ToggleWind")]
    private static void Toggled(WindChillZone __instance, bool set, float untilSwitch)
    {
        Plugin.Log.LogInfo($"Weather: '{__instance.name}' ({Kind(__instance)}) {(set ? "starts" : "stops")} for {untilSwitch:0}s.");
    }
}
