using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRoguelike;

/// <summary>
/// Design-data export: writes every item's rarity, spawn pools, weight, uses and all
/// serialized gameplay values (actions, afflictions, components) to
/// BepInEx/PeakRoguelike/items.json, plus icons and the crates seen in each level.
/// Runs automatically once per game launch; F9 re-runs it.
/// </summary>
internal class ItemDump : MonoBehaviour
{
    private static string OutDir => Path.Combine(Paths.BepInExRootPath, "PeakRoguelike");
    private bool dumpedItems;
    private readonly Dictionary<string, string> crates = new();

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Item Dump");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<ItemDump>();
    }

    private void Awake() => SceneManager.sceneLoaded += (_, _) => StartCoroutine(AfterSceneLoad());

    private IEnumerator AfterSceneLoad()
    {
        yield return new WaitForSeconds(3f);
        if (!dumpedItems) TryDumpItems();
        TryDumpCrates();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
        {
            TryDumpItems();
            TryDumpCrates();
        }
    }

    private void TryDumpItems()
    {
        try
        {
            var db = SingletonAsset<ItemDatabase>.Instance;
            if (db == null || db.itemLookup.Count == 0) return;
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(Path.Combine(OutDir, "icons"));
            LootData.PopulateLootData();

            var w = new Json();
            w.BeginArray();
            foreach (var kv in db.itemLookup.OrderBy(k => k.Key))
            {
                try { WriteItem(w, kv.Value); }
                catch (Exception e) { Plugin.Log.LogWarning($"Dump of item {kv.Key} failed: {e.Message}"); }
            }
            w.EndArray();
            File.WriteAllText(Path.Combine(OutDir, "items.json"), w.ToString());
            dumpedItems = true;
            Plugin.Log.LogInfo($"Item dump: wrote {db.itemLookup.Count} items to {OutDir}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Item dump failed: {e}");
        }
    }

    private void TryDumpCrates()
    {
        try
        {
            string scene = SceneManager.GetActiveScene().name;
            foreach (var l in FindObjectsByType<Luggage>(FindObjectsSortMode.None))
            {
                int spots = 0;
                try { spots = l.isWeightedSpawnPoints ? l.weightedSpawnSpots.Max(x => x.spawnSpots.Count) : l.spawnSpots.Count; } catch { }
                string pools = l.spawnMode == Spawner.SpawnMode.HeightBasedSpawnPools
                    ? string.Join(" / ", l.heightBasedSpawnPools.Select(h => h.spawnPool.ToString()))
                    : l.spawnMode == Spawner.SpawnMode.SingleItem ? $"single:{(l.spawnedObjectPrefab ? l.spawnedObjectPrefab.name : "?")}" : l.spawnPool.ToString();
                string key = $"{l.GetType().Name}|{l.displayName}|{pools}|{spots}";
                if (!crates.ContainsKey(key)) crates[key] = scene;
            }
            if (crates.Count == 0) return;
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder("type\tdisplayName\tspawnPools\titemSlots\tfirstSeenScene\n");
            foreach (var kv in crates.OrderBy(k => k.Key)) sb.Append(kv.Key.Replace('|', '\t')).Append('\t').Append(kv.Value).Append('\n');
            File.WriteAllText(Path.Combine(OutDir, "crates.tsv"), sb.ToString());
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Crate dump failed: {e.Message}");
        }
    }

    private static void WriteItem(Json w, Item item)
    {
        w.BeginObject();
        w.Prop("id", item.itemID);
        w.Prop("prefab", item.gameObject.name);
        string display = item.UIData?.itemName ?? "";
        try { display = item.GetName(); } catch { }
        w.Prop("name", display);
        w.Prop("nameKey", item.UIData?.itemName ?? "");

        var loot = item.GetComponent<LootData>();
        w.Prop("rarity", loot ? loot.Rarity.ToString() : "none (not in loot tables)");
        if (loot)
        {
            w.Key("spawnPools"); w.BeginObject();
            foreach (SpawnPool p in Enum.GetValues(typeof(SpawnPool)))
                if (p != SpawnPool.None && loot.spawnLocations.HasFlag(p))
                    w.Prop(p.ToString(), LootData.GetPercentageOdds(item.itemID, p));
            w.EndObject();
            w.Prop("bannedInSolo", loot.banInSolo);
        }

        w.Prop("carryWeight", item.carryWeight);
        w.Prop("totalUses", item.totalUses);
        w.Prop("useTimePrimary", item.usingTimePrimary);
        w.Prop("mass", item.mass);
        w.Prop("tags", item.itemTags.ToString());
        if (item.UIData != null)
        {
            w.Prop("canPocket", item.UIData.canPocket);
            w.Prop("canBackpack", item.UIData.canBackpack);
            w.Prop("primaryUse", item.UIData.hasMainInteract ? item.UIData.mainInteractPrompt : "");
            w.Prop("secondaryUse", item.UIData.hasSecondInteract ? item.UIData.secondaryInteractPrompt : "");
        }
        w.Prop("icon", SaveIcon(item));

        // Every gameplay component on the item, with its serialized values.
        w.Key("components"); w.BeginArray();
        foreach (var c in item.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (c == null || c is Item || c is LootData) continue;
            Type t = c.GetType();
            if (t.Assembly != typeof(Item).Assembly) continue; // only the game's own scripts
            if (IsNoise(t)) continue;
            w.BeginObject();
            w.Prop("type", t.Name);
            if (c.transform != item.transform) w.Prop("onChild", c.gameObject.name);
            WriteFields(w, c, 0);
            w.EndObject();
        }
        w.EndArray();
        w.EndObject();
    }

    private static readonly string[] NoiseWords = { "SFX", "Audio", "Visual", "Render", "Particle", "Anim", "Sound", "Photon", "Sync", "Mesh", "Light" };
    private static bool IsNoise(Type t) => NoiseWords.Any(n => t.Name.Contains(n)) && !t.Name.StartsWith("Action_");

    private static IEnumerable<FieldInfo> SerializedFields(Type t)
    {
        for (Type? cur = t; cur != null && cur != typeof(MonoBehaviour) && cur != typeof(object); cur = cur.BaseType)
            foreach (var f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsNotSerialized) continue;
                if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null && f.GetCustomAttribute<SerializeReference>() == null) continue;
                yield return f;
            }
    }

    private static void WriteFields(Json w, object obj, int depth)
    {
        w.Key("values"); w.BeginObject();
        foreach (var f in SerializedFields(obj.GetType()))
        {
            object? v;
            try { v = f.GetValue(obj); } catch { continue; }
            WriteValue(w, f.Name, v, depth);
        }
        w.EndObject();
    }

    private static void WriteValue(Json w, string? name, object? v, int depth)
    {
        void K() { if (name != null) w.Key(name); }
        switch (v)
        {
            case null: return;
            case string s: if (s.Length > 0) { K(); w.Value(s); } return;
            case bool b: K(); w.Value(b); return;
            case Enum e: K(); w.Value(e.ToString()); return;
            case float or double or int or uint or short or ushort or byte or long:
                K(); w.Value(Convert.ToDouble(v, CultureInfo.InvariantCulture)); return;
            case Vector2 or Vector3 or Color or Vector4:
                K(); w.Value(v.ToString()); return;
            case AnimationCurve curve:
                K(); w.Value(string.Join(" ", curve.keys.Select(k => $"({k.time:0.##},{k.value:0.##})"))); return;
            case UnityEngine.Object uo:
                if (uo) { K(); w.Value("→ " + uo.name); }
                return;
            case IList list:
                if (list.Count == 0 || depth > 2) return;
                K(); w.BeginArray();
                foreach (var x in list.Cast<object>().Take(30)) WriteValue(w, null, x, depth + 1);
                w.EndArray();
                return;
        }
        // Plain serializable class (e.g. an Affliction): recurse.
        Type t = v.GetType();
        if (depth > 2 || t.IsPrimitive || t.Namespace?.StartsWith("System") == true) return;
        K(); w.BeginObject();
        w.Prop("type", t.Name);
        WriteFields(w, v, depth + 1);
        w.EndObject();
    }

    private static string SaveIcon(Item item)
    {
        Texture2D? tex = item.UIData?.icon;
        if (tex == null) return "";
        string file = $"{item.itemID:000}_{Sanitize(item.gameObject.name)}.png";
        try
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(OutDir, "icons", file), copy.EncodeToPNG());
            Destroy(copy);
            return "icons/" + file;
        }
        catch { return ""; }
    }

    private static string Sanitize(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());

    /// <summary>Minimal JSON writer (pretty-printed, no dependencies).</summary>
    private class Json
    {
        private readonly StringBuilder sb = new();
        private readonly Stack<bool> first = new();
        private bool afterKey;

        private void Sep()
        {
            if (afterKey) { afterKey = false; return; }
            if (first.Count > 0)
            {
                if (!first.Peek()) sb.Append(',');
                first.Pop(); first.Push(false);
                sb.Append('\n').Append(' ', first.Count * 2);
            }
        }
        public void BeginObject() { Sep(); sb.Append('{'); first.Push(true); }
        public void EndObject() { bool empty = first.Pop(); if (!empty) sb.Append('\n').Append(' ', first.Count * 2); sb.Append('}'); }
        public void BeginArray() { Sep(); sb.Append('['); first.Push(true); }
        public void EndArray() { bool empty = first.Pop(); if (!empty) sb.Append('\n').Append(' ', first.Count * 2); sb.Append(']'); }
        public void Key(string k) { Sep(); Str(k); sb.Append(": "); afterKey = true; }
        public void Value(string s) { Sep(); Str(s); }
        public void Value(bool b) { Sep(); sb.Append(b ? "true" : "false"); }
        public void Value(double d) { Sep(); sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("0.####", CultureInfo.InvariantCulture)); }
        public void Prop(string k, string v) { Key(k); Value(v); }
        public void Prop(string k, bool v) { Key(k); Value(v); }
        public void Prop(string k, double v) { Key(k); Value(v); }
        private void Str(string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (c < 32) sb.Append(' '); else sb.Append(c); break;
                }
            }
            sb.Append('"');
        }
        public override string ToString() => sb.ToString();
    }
}
