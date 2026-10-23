using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

namespace PeakRoguelike;

/// <summary>
/// Picks saved for later - every card that didn't come from a crate (level-ups, relics, statue and
/// campfire blessings, gifts...). The relic key reopens them one at a time, oldest first; each
/// stays saved, however often it's put off, until it's picked or skipped. The HUD shows "SAVED [L]".
/// </summary>
internal static class SavedPicks
{
    private sealed class Entry
    {
        public string Id = "", Name = "";
        public Func<bool>? Valid;
        public Action Open = null!;
    }

    private static readonly List<Entry> list = new();

    private static float nextPrune;

    /// <summary>How many wait (the HUD asks every frame; stale ones are dropped twice a second).</summary>
    internal static int Count
    {
        get
        {
            if (UnityEngine.Time.unscaledTime >= nextPrune) { nextPrune = UnityEngine.Time.unscaledTime + 0.5f; Prune(); }
            return list.Count;
        }
    }

    /// <summary>Save a pick (or re-save it: the same id keeps its place in line).</summary>
    internal static void Save(string id, string name, Action open, Func<bool>? valid = null)
    {
        if (list.FirstOrDefault(e => e.Id == id) is { } had)
        {
            had.Name = name;
            had.Open = open;
            had.Valid = valid;
            return;
        }
        list.Add(new Entry { Id = id, Name = name, Open = open, Valid = valid });
        Plugin.Log.LogInfo($"Saved for later: {name} ({list.Count} waiting).");
    }

    internal static void Drop(string id) => list.RemoveAll(e => e.Id == id);

    internal static bool Has(string id) => list.Any(e => e.Id == id);

    internal static void Reset() => list.Clear();

    /// <summary>Every frame: the relic key opens the oldest saved pick. True = it opened one.</summary>
    internal static bool Tick()
    {
        if (list.Count == 0 || ChoiceScreen.IsOpen || Keyboard.current is not { } kb || !kb[ModConfig.RelicKey.Value].wasPressedThisFrame) return false;
        var me = Character.localCharacter;
        if (me == null || me.data.dead || me.data.passedOut) return false;
        Prune();
        if (list.Count == 0) return false;
        var e = list.OrderBy(Rank).First(); // stable: oldest first within a rank
        list.Remove(e);
        Plugin.Log.LogInfo($"Reopened saved pick: {e.Name} ({list.Count} still waiting).");
        try { e.Open(); }
        catch (Exception ex) { Plugin.Log.LogError($"Saved pick {e.Name} failed to open: {ex}"); }
        return true;
    }

    /// <summary>Relics first, then level-ups, then the rest (fusions keep their own key).</summary>
    private static int Rank(Entry e) =>
        e.Id.StartsWith("shared:") || e.Id.StartsWith("random:") ? 0 : e.Id == "xp" ? 1 : 2;

    /// <summary>Gone stale (a relic of that colour bound since, the run moved on...): dropped quietly.</summary>
    private static void Prune() => list.RemoveAll(e =>
    {
        bool stale;
        try { stale = e.Valid != null && !e.Valid(); }
        catch { stale = true; }
        if (stale) Plugin.Log.LogInfo($"Saved pick {e.Name} no longer applies - dropped.");
        return stale;
    });
}
