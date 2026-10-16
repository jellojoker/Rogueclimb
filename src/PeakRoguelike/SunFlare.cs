using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakRoguelike;

/// <summary>
/// The game's own sun glare (DayNightManager.lensFlare) tinted to the sky's colour: coloured suns,
/// and blood red on the menu and at the airport. The flare data is a shared asset, so every
/// element's original tint is kept and put back the moment nothing tints it.
/// </summary>
internal static class SunFlare
{
    private static readonly Dictionary<LensFlareDataSRP, Color[]> original = new();
    private static readonly HashSet<LensFlareDataSRP> logged = new();
    private static bool warned;

    /// <summary>k = 0 puts the vanilla glare back.</summary>
    internal static void Tint(DayNightManager? dnm, Color hue, float k)
    {
        try
        {
            if (!ModConfig.SunFlareTint.Value) k = 0f;
            var data = dnm != null && dnm.lensFlare != null ? dnm.lensFlare.lensFlareData : null;
            if (data == null || data.elements == null) return;
            if (!original.TryGetValue(data, out var orig))
            {
                if (k <= 0f) return;
                orig = data.elements.Select(e => e != null ? e.tint : Color.white).ToArray();
                original[data] = orig;
                if (logged.Add(data)) Plugin.Log.LogInfo($"Sun flare: tinting '{data.name}' ({orig.Length} elements).");
            }
            for (int i = 0; i < data.elements.Length && i < orig.Length; i++)
            {
                var e = data.elements[i];
                if (e == null) continue;
                Color o = orig[i];
                Color tinted = hue * Mathf.Max(0.0001f, o.maxColorComponent);
                tinted.a = o.a;
                e.tint = k <= 0f ? o : Color.Lerp(o, tinted, Mathf.Clamp01(k));
            }
            if (k <= 0f) original.Remove(data);
        }
        catch (System.Exception e)
        {
            if (!warned) Plugin.Log.LogWarning($"Sun flare tint failed: {e.Message}");
            warned = true;
        }
    }
}
