using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>
/// Loot rolls, reweighted every time the game draws from a spawn pool:
///  - crates offer each tree through one entry, its base item, all equally likely (Glider and
///    Lantern join every crate), and never roll the higher items of a tree;
///  - everything else follows the run: on the Shore, crate items outside the trees are rare;
///    later on, rarer items get more likely the higher the team climbs.
/// </summary>
[HarmonyPatch]
internal static class LootCurve
{
    // Weight multiplier per rarity, by biome reached (0 Shore, 1 Tropics/Roots, 2 Alpine/Mesa, 3 Caldera, 4+ Kiln/Peak).
    private static readonly Dictionary<Rarity, float[]> Curve = new()
    {
        // The higher the climb, the less rare the rare things: a lucky run reaches a fusion or two.
        [Rarity.Common] = new[] { 1.3f, 1.1f, 1.0f, 0.8f, 0.6f },
        [Rarity.Uncommon] = new[] { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f },
        [Rarity.Rare] = new[] { 0.5f, 0.8f, 1.2f, 1.6f, 2.0f },
        [Rarity.Epic] = new[] { 0.3f, 0.6f, 1.2f, 1.8f, 2.5f },
        [Rarity.Legendary] = new[] { 0.15f, 0.4f, 1.2f, 2.0f, 3.0f },
        [Rarity.Mythic] = new[] { 0.05f, 0.2f, 1.0f, 1.8f, 2.5f },
        [Rarity.RidiculouslyRare] = new[] { 0.05f, 0.2f, 1.0f, 1.8f, 2.5f },
    };

    /// <summary>The regular crates along the climb (not cursed, clown or ancient luggage).</summary>
    internal static readonly HashSet<SpawnPool> Crates = new()
    {
        SpawnPool.LuggageBeach, SpawnPool.LuggageJungle, SpawnPool.LuggageRoots, SpawnPool.LuggageTundra,
        SpawnPool.LuggageMesa, SpawnPool.LuggageCaldera, SpawnPool.LuggageGloom, SpawnPool.LuggageCitadel, SpawnPool.LuggageClimber,
    };

    /// <summary>How far up the mountain the run is (the segment you're in, 0-based).</summary>
    public static int Stage
    {
        get
        {
            try
            {
                var map = Singleton<MapHandler>.Instance;
                return map != null ? Mathf.Clamp(map.currentSegment, 0, 4) : 0;
            }
            catch { return 0; }
        }
    }

    public static float Multiplier(Item item)
    {
        var loot = item.GetComponent<LootData>();
        // The Antidote counts as uncommon (vanilla rare).
        var rarity = Items.IsAntidote(item) ? Rarity.Uncommon : loot != null ? loot.Rarity : (Rarity?)null;
        return rarity is { } r && Curve.TryGetValue(r, out var row) ? row[Stage] : 1f;
    }

    /// <summary>Which pool the game is drawing from right now (ValidateSpawnPoolDictionary isn't told).</summary>
    private static SpawnPool drawing;

    [HarmonyPrefix, HarmonyPatch(typeof(LootData), nameof(LootData.GetRandomItems))]
    private static void Drawing(SpawnPool spawnPool) => drawing = spawnPool;

    private static List<Item>? bases;

    /// <summary>The eight trees' crate items.</summary>
    private static List<Item> Bases()
    {
        if (bases != null && bases.All(b => b != null)) return bases;
        bases = Trees.All.Keys.Select(id => Items.Find(Trees.BaseRole(id))).Where(i => i != null).Select(i => i!).ToList();
        return bases;
    }

    /// <summary>The game copies a spawn pool's weights, then validates the copy - we reweight it right after.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(LootData), "ValidateSpawnPoolDictionary")]
    private static void Reweight(Dictionary<ushort, int> spawnPoolDictionary)
    {
        if (!ModConfig.Leveling.Value || spawnPoolDictionary == null) return;
        bool crate = Crates.Contains(drawing);
        bool luggage = drawing.ToString().StartsWith("Luggage");
        bool equal = crate && ModConfig.EqualTreeItems.Value;
        var baseIds = equal ? Bases().Select(b => b.itemID).ToHashSet() : new HashSet<ushort>();

        // One even weight for every tree: what Bandages weigh in this crate (or the pool's middle).
        int even = 0;
        if (crate)
        {
            var bandage = Items.Find("Bandage");
            even = bandage != null && spawnPoolDictionary.TryGetValue(bandage.itemID, out int w) ? w
                : spawnPoolDictionary.Count > 0 ? spawnPoolDictionary.Values.OrderBy(v => v).ElementAt(spawnPoolDictionary.Count / 2) : 50;
        }

        // Sunscreen (keeps the suns off you) turns up in every biome's crates, rarely.
        var sunscreen = crate ? Items.Database.FirstOrDefault(Items.IsSunscreen) : null;
        if (sunscreen != null && !spawnPoolDictionary.ContainsKey(sunscreen.itemID))
            spawnPoolDictionary[sunscreen.itemID] = Mathf.Max(1, Mathf.RoundToInt(even * ModConfig.SunscreenWeight.Value));

        // The Antidote: a middle-of-the-pack find in every biome's crates.
        var antidote = crate ? Items.Find("Antidote") : null;
        if (antidote != null) spawnPoolDictionary[antidote.itemID] = Mathf.Max(1, Mathf.RoundToInt(even * ModConfig.AntidoteWeight.Value));

        foreach (ushort id in spawnPoolDictionary.Keys.ToList())
        {
            if (!Items.TryGet(id, out var item)) continue;
            // Crates never roll a tree's higher items (or any earned item): they'd only be rerolled.
            if (luggage && Items.IsEarnedOnly(item)) { spawnPoolDictionary.Remove(id); continue; }
            if (equal && baseIds.Contains(id)) continue; // set below
            float m = ModConfig.RarityCurve.Value ? Multiplier(item) : 1f;
            if (crate && Stage == 0 && Trees.Classify(item) == TreeId.None && !Items.IsSunscreen(item) && !Items.IsAntidote(item)) m *= Mathf.Max(0f, ModConfig.ShoreOtherItems.Value);
            // A step rarer than the Skull and the Dagger it lies beside.
            if (Items.IsBookOfBones(item)) m *= Mathf.Max(0f, ModConfig.BookOfBonesWeight.Value);
            // Weights are ints: scale everything by 10 so small multipliers don't round to zero.
            int scaled = Mathf.RoundToInt(spawnPoolDictionary[id] * 10f * m);
            if (scaled <= 0) spawnPoolDictionary.Remove(id);
            else spawnPoolDictionary[id] = scaled;
        }
        // Every tree's base item at the same weight - added where the crate didn't have it. A tree
        // the rolling scout has already maxed comes up less, so what they still need shows more.
        if (equal)
            foreach (ushort id in baseIds)
            {
                float m = Items.TryGet(id, out var b) && RunState.IsMaxed(Trees.Classify(b)) ? Mathf.Clamp01(ModConfig.MaxedTreeWeight.Value) : 1f;
                spawnPoolDictionary[id] = Mathf.Max(1, Mathf.RoundToInt(even * 10 * m));
            }
    }
}
