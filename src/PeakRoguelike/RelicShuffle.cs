using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zorro.Core;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

internal enum StatueOrder { Off, Daily, EveryRun }

/// <summary>
/// The statues' amulets come baked into each map (and PEAK currently bakes the same order into
/// every daily map). The host's [Relics] StatueOrder decides: Off = the game's order, Daily =
/// shuffled by the day's map (same for everyone that day), EveryRun = a fresh order each run.
/// The host publishes the mode ("prl.so", shown read-only on everyone's ticket) and the order
/// for the current map ("prl.gems" = "scene|run|Gem,Gem,Gem,Gem", what each colour in RelicGem
/// order becomes); everyone swaps their statues to match - what you pick up, its look, the sun.
/// </summary>
internal class RelicShuffle : MonoBehaviourPunCallbacks
{
    private const string ModeKey = "prl.so";
    private const string OrderKey = "prl.gems";
    private static readonly RelicGem[] Gems = { RelicGem.Ambition, RelicGem.Generosity, RelicGem.Initiative, RelicGem.Tenacity };

    private float next;
    private MapHandler? appliedFor, publishedFor;
    private string? appliedValue;
    private StatueOrder publishedMode = (StatueOrder)(-1);
    /// <summary>This run's dice for EveryRun (a new map - including after a wipe - rolls again).</summary>
    private static int run;
    /// <summary>Each statue amulet's baked colour, so a new order never stacks on the last one.</summary>
    private readonly Dictionary<FakeItem, RelicGem> baked = new();

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Relic Shuffle");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<RelicShuffle>();
    }

    private static bool InRun =>
        DayNightManager.instance != null && GameUtils.instance != null && !GameUtils.instance.m_inAirport && MapHandler.Exists;

    /// <summary>The mode in force: the host's own setting, or what the host published.</summary>
    internal static StatueOrder Mode
    {
        get
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return ModConfig.StatueOrder.Value;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(ModeKey, out object v) && v is int m ? (StatueOrder)m : StatueOrder.Off;
        }
    }

    internal static bool CanChange => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

    internal static string Label(StatueOrder m) => m switch
    {
        StatueOrder.Daily => "Daily",
        StatueOrder.EveryRun => "Every run",
        _ => "Off",
    };

    /// <summary>Host: step to the next mode (ticket arrows, F8). It applies to the map right away.</summary>
    internal static void Cycle(int step)
    {
        if (!CanChange) return;
        int n = System.Enum.GetValues(typeof(StatueOrder)).Length;
        ModConfig.StatueOrder.Value = (StatueOrder)((((int)ModConfig.StatueOrder.Value + step) % n + n) % n);
        Plugin.Log.LogInfo($"Relic order: {Label(ModConfig.StatueOrder.Value)}.");
    }

    private void Update()
    {
        if (!ModConfig.Relics.Value || Time.time < next) return;
        next = Time.time + 0.5f;
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        var room = PhotonNetwork.CurrentRoom;
        var mode = ModConfig.StatueOrder.Value;
        if (PhotonNetwork.IsMasterClient && (!room.CustomProperties.TryGetValue(ModeKey, out object pm) || pm is not int pmi || pmi != (int)mode))
            room.SetCustomProperties(new Hashtable { [ModeKey] = (int)mode });
        if (!InRun)
        {
            if (PhotonNetwork.IsMasterClient && room.CustomProperties.ContainsKey(OrderKey) && room.CustomProperties[OrderKey] != null)
                room.SetCustomProperties(new Hashtable { [OrderKey] = null });
            return;
        }

        string scene = SceneManager.GetActiveScene().name;
        var map = Singleton<MapHandler>.Instance;
        string? raw = room.CustomProperties.TryGetValue(OrderKey, out object v) ? v as string : null;
        // Host: a new map (a new run) or a new mode gets a new order.
        if (PhotonNetwork.IsMasterClient && (publishedFor != map || publishedMode != mode || raw == null || !raw.StartsWith(scene + "|")))
        {
            if (publishedFor != map) run = Random.Range(1, int.MaxValue);
            publishedFor = map;
            publishedMode = mode;
            var order = mode switch
            {
                StatueOrder.Daily => Shuffled(Seed(scene)),
                StatueOrder.EveryRun => Shuffled(run),
                _ => Gems,
            };
            raw = $"{scene}|{run}|{string.Join(",", order)}";
            room.SetCustomProperties(new Hashtable { [OrderKey] = raw });
        }
        if (raw == null || !raw.StartsWith(scene + "|")) return;
        if (appliedFor == map && appliedValue == raw) return;
        var parts = raw.Split('|');
        var names = parts.Length == 3 ? parts[2].Split(',') : new string[0];
        if (names.Length != Gems.Length) return;
        var to = new Dictionary<RelicGem, RelicGem>();
        for (int i = 0; i < Gems.Length; i++)
            if (System.Enum.TryParse(names[i], out RelicGem g)) to[Gems[i]] = g;
        if (to.Count != Gems.Length) return;
        if (appliedFor != map) baked.Clear();
        if (Apply(to) == 0) return; // statues not spawned yet: try again shortly
        appliedFor = map;
        appliedValue = raw;
        Suns.ForgetGems();
    }

    /// <summary>The day's level (same for everyone today, different tomorrow), mixed with the map's name.</summary>
    private static int Seed(string scene)
    {
        int day;
        try { day = GameHandler.GetService<NextLevelService>().NextLevelIndexOrFallback; }
        catch { day = 0; }
        int h = 17;
        foreach (char c in scene) h = h * 31 + c;
        return day * 7919 + h;
    }

    private static IEnumerable<RelicGem> Shuffled(int seed) => Shuffled(Gems, seed);

    private static List<T> Shuffled<T>(IEnumerable<T> items, int seed)
    {
        var rng = new System.Random(seed);
        var list = items.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>
    /// Host: the blood moon's hazards in the order its tiers add them, from the same setting as
    /// the statues. Off = shuffled by the game's daily map (the same until PEAK's daily changes),
    /// Daily = a second shuffle of that day, never the same as Off's, EveryRun = new each run.
    /// Hazards switched off in the config go to the back, so tier 1 always brings something.
    /// </summary>
    internal static List<BloodMoon.Hazard> MoonOrder() => MoonOrder(out _);

    /// <param name="how">For the log: which setting and seed the order came from.</param>
    internal static List<BloodMoon.Hazard> MoonOrder(out string how)
    {
        string scene = SceneManager.GetActiveScene().name;
        int day = Seed(scene);
        var off = Shuffled(BloodMoon.DefaultOrder, day * 31 + 1);
        List<BloodMoon.Hazard> order;
        switch (ModConfig.StatueOrder.Value)
        {
            case StatueOrder.Daily:
            {
                // Its own shuffle of the day - re-rolled until it differs from Off's.
                int tries = 0;
                do order = Shuffled(BloodMoon.DefaultOrder, day * 31 + 2 + tries * 7);
                while (order.SequenceEqual(off) && ++tries < 50);
                how = $"Daily, day seed {day}{(tries > 0 ? $", re-rolled {tries}x to differ from Off" : "")}";
                break;
            }
            case StatueOrder.EveryRun:
                order = Shuffled(BloodMoon.DefaultOrder, run * 31 + 3);
                how = $"Every run, run seed {run}";
                break;
            default:
                order = off;
                how = $"Off, day seed {day}";
                break;
        }
        return order.OrderBy(h => On(h) ? 0 : 1).ToList();
    }

    private static bool On(BloodMoon.Hazard h) => h switch
    {
        BloodMoon.Hazard.Zombies => ModConfig.BmZombies.Value,
        BloodMoon.Hazard.Scoutmaster => ModConfig.BmScoutmaster.Value,
        BloodMoon.Hazard.Tornado => ModConfig.BmTornado.Value,
        _ => BloodMoon.GloomRun ? ModConfig.BmGlassMeteors.Value : ModConfig.BmMeteors.Value,
    };

    /// <summary>Swap every statue amulet to its colour in this order: the item it gives, its name, its look. Returns statues seen.</summary>
    private int Apply(Dictionary<RelicGem, RelicGem> to)
    {
        int seen = 0;
        var moves = new List<string>();
        foreach (var fake in FindObjectsByType<FakeItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (fake == null || fake.realItemPrefab == null) continue;
            if (!baked.TryGetValue(fake, out var from))
            {
                if (!Items.IsAmulet(fake.realItemPrefab) || Items.AmuletGem(fake.realItemPrefab) is not { } g) continue;
                baked[fake] = from = g;
            }
            seen++;
            var target = Relics.Amulet(to[from]);
            if (target == null || target == fake.realItemPrefab) continue;
            fake.realItemPrefab = target;
            fake.itemName = target.UIData.itemName;
            Restyle(fake, target);
            moves.Add($"{from}->{to[from]}");
        }
        if (seen > 0) Plugin.Log.LogInfo($"Relic order ({Label(Mode)}): {seen} statue amulet(s){(moves.Count > 0 ? ": " + string.Join(", ", moves) : ", as baked")}.");
        return seen;
    }

    private static bool styleLogged;

    private static bool IsHand(MeshFilter m) =>
        m.sharedMesh.name.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0
        || m.name.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>The statue shows the new amulet: its meshes and materials copied onto the display piece.</summary>
    private static void Restyle(FakeItem fake, Item target)
    {
        var mine = fake.GetComponentsInChildren<MeshFilter>(true);
        // The amulet item also carries the first-person hands that hold it ("Hand, Hand,
        // Medallion_Turtle"): only the medallion goes on the statue, never a giant hand.
        var theirs = target.GetComponentsInChildren<MeshFilter>(true)
            .Where(m => m.sharedMesh != null && !IsHand(m)).ToArray();
        int n = Mathf.Min(mine.Length, theirs.Length);
        for (int i = 0; i < n; i++)
        {
            mine[i].sharedMesh = theirs[i].sharedMesh;
            var mr = mine[i].GetComponent<Renderer>();
            var tr = theirs[i].GetComponent<Renderer>();
            if (mr != null && tr != null) mr.sharedMaterials = tr.sharedMaterials;
        }
        if (n == 0)
        {
            var mr = fake.GetComponentInChildren<Renderer>(true);
            var tr = theirs.Length > 0 ? theirs[0].GetComponent<Renderer>() : null;
            if (mr != null && tr != null) mr.sharedMaterials = tr.sharedMaterials;
        }
        if (!styleLogged)
        {
            styleLogged = true;
            Plugin.Log.LogInfo($"Relic order look: statue piece has {mine.Length} mesh(es) [{string.Join(", ", mine.Select(m => m.name))}], "
                + $"amulet {target.name} has {theirs.Length} [{string.Join(", ", theirs.Select(m => m.name))}].");
        }
    }
}

/// <summary>
/// The run settings on the airport ticket, under the ascent, in the ticket's own lettering and
/// arrows: a 2 x 2 grid - RELIC ORDER | BLOOD MOONS, CRATES | RELICS - each a small caption over
/// "◀ choice ▶". The host's arrows change them; everyone else sees what the host picked. A harder
/// choice (Stacked, First come) is written in blood red.
/// </summary>
[HarmonyPatch]
internal class TicketRelicOrder : MonoBehaviour
{
    private const string GridName = "PEAK Roguelike - Run settings";

    /// <summary>One cell of the grid.</summary>
    private class Cell
    {
        public TMP_Text Label = null!;
        public Button Left = null!, Right = null!;
        public System.Func<string> Value = null!;
        public System.Func<bool> Hard = () => false;
        public System.Func<bool> CanChange = () => true;
    }

    private readonly List<Cell> cells = new();
    private Color plain;
    private static bool logged;

    private static readonly Color Blood = new(0.78f, 0.12f, 0.1f);

    [HarmonyPostfix, HarmonyPatch(typeof(BoardingPass), "OnOpen")]
    private static void AddRow(BoardingPass __instance)
    {
        try
        {
            var desc = __instance.ascentDesc;
            if (desc == null || desc.transform.parent == null) return;
            var existing = desc.transform.parent.Find(GridName);
            if (existing != null) { existing.GetComponent<TicketRelicOrder>()?.Refresh(); return; }
            Build(__instance, (RectTransform)desc.transform);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Ticket settings failed: {e.Message}"); }
    }

    private const float CellHeight = 70f, CaptionHeight = 24f, ArrowSize = 36f, ArrowGap = 98f, RowGap = 6f;

    private static void Build(BoardingPass bp, RectTransform desc)
    {
        var settings = new List<(string caption, System.Func<string> value, System.Func<bool> hard, System.Action<int> step, System.Func<bool> can)>();
        if (ModConfig.Relics.Value)
            settings.Add(("Relic order", () => RelicShuffle.Label(RelicShuffle.Mode), () => false, s => RelicShuffle.Cycle(s), () => RelicShuffle.CanChange));
        if (ModConfig.BloodMoon.Value)
            settings.Add(("Blood moons", () => Tickets.StackedMoons ? "Stacked" : "Relay", () => Tickets.StackedMoons, _ => Tickets.ToggleMoons(), () => Tickets.CanChange));
        settings.Add(("Crates", () => Tickets.CratesFirstCome ? "First come" : "Shared", () => Tickets.CratesFirstCome, _ => Tickets.ToggleCrates(), () => Tickets.CanChange));
        if (ModConfig.Relics.Value)
            settings.Add(("Relics", () => Tickets.RelicsFirstCome ? "First come" : "Shared", () => Tickets.RelicsFirstCome, _ => Tickets.ToggleRelics(), () => Tickets.CanChange));

        int rows = (settings.Count + 1) / 2;
        var grid = new GameObject(GridName, typeof(RectTransform));
        var rt = (RectTransform)grid.transform;
        rt.SetParent(desc.parent, false);
        rt.anchorMin = desc.anchorMin;
        rt.anchorMax = desc.anchorMax;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(desc.rect.width, rows * CellHeight + (rows - 1) * RowGap);
        // Just under the ascent's description box.
        rt.localPosition = desc.localPosition + new Vector3(desc.rect.center.x, desc.rect.yMin - 4f, 0f);

        var me = grid.AddComponent<TicketRelicOrder>();
        var ink = bp.ascentDesc.color;
        me.plain = bp.ascentTitle != null ? bp.defaultColor : ink;

        // A faint rule, like the ticket's own section breaks, and a fainter one between the columns.
        Rule(rt, ink, 1f, 0.2f, 0.8f, true);
        if (settings.Count > 1) Rule(rt, ink, 0.5f, 0.12f, 0.88f, false);

        float body = Mathf.Max(14f, bp.ascentDesc.fontSize);
        var proto = bp.ascentTitle != null ? bp.ascentTitle : bp.ascentDesc;
        for (int i = 0; i < settings.Count; i++)
        {
            var (caption, value, hard, step, can) = settings[i];
            int row = i / 2, col = i % 2;
            // A lone last setting sits in the middle.
            float x = i == settings.Count - 1 && col == 0 ? 0.5f : col == 0 ? 0.25f : 0.75f;
            var cellGo = new GameObject(caption, typeof(RectTransform));
            var cr = (RectTransform)cellGo.transform;
            cr.SetParent(rt, false);
            cr.anchorMin = cr.anchorMax = new Vector2(x, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.sizeDelta = new Vector2(desc.rect.width * 0.5f, CellHeight);
            cr.anchoredPosition = new Vector2(0f, -row * (CellHeight + RowGap));

            // Small spaced caption in the description's ink.
            var cap = Text(bp.ascentDesc, cr, "Caption", 1f, CaptionHeight);
            cap.fontSize = body * 0.62f;
            cap.characterSpacing = 6f;
            cap.fontStyle = FontStyles.UpperCase;
            cap.color = new Color(ink.r, ink.g, ink.b, ink.a * 0.65f);
            cap.text = caption;

            var cell = new Cell { Value = value, Hard = hard, CanChange = can };
            // The choice itself, in the ascent title's lettering, flanked by the ticket's own arrows.
            cell.Label = Text(proto, cr, "Value", 0f, CellHeight - CaptionHeight - 4f);
            cell.Label.fontSize = body;
            cell.Left = Arrow(bp.decrementAscentButton, cr, -1f, () => { step(-1); me.Refresh(); });
            cell.Right = Arrow(bp.incrementAscentButton, cr, 1f, () => { step(1); me.Refresh(); });
            me.cells.Add(cell);
        }
        me.Refresh();

        if (!logged)
        {
            logged = true;
            Plugin.Log.LogInfo($"Ticket: {settings.Count} run settings in {rows} row(s) under '{desc.name}' (parent '{desc.parent.name}', desc rect {desc.rect}, desc font {bp.ascentDesc.fontSize}, "
                + $"title '{proto.name}' font {proto.fontSize}, arrows '{bp.decrementAscentButton.name}' {((RectTransform)bp.decrementAscentButton.transform).rect.size}).");
        }
    }

    /// <summary>A faint line in the ticket's ink: across the top (horizontal) or down the middle.</summary>
    private static void Rule(RectTransform parent, Color ink, float at, float from, float to, bool horizontal)
    {
        var rule = new GameObject("Rule", typeof(RectTransform), typeof(Image));
        var rr = (RectTransform)rule.transform;
        rr.SetParent(parent, false);
        rr.anchorMin = horizontal ? new Vector2(from, at) : new Vector2(at, from);
        rr.anchorMax = horizontal ? new Vector2(to, at) : new Vector2(at, to);
        rr.pivot = new Vector2(0.5f, horizontal ? 1f : 0.5f);
        rr.sizeDelta = horizontal ? new Vector2(0f, 2f) : new Vector2(2f, 0f);
        rr.anchoredPosition = Vector2.zero;
        var ri = rule.GetComponent<Image>();
        ri.color = new Color(ink.r, ink.g, ink.b, ink.a * (horizontal ? 0.25f : 0.15f));
        ri.raycastTarget = false;
    }

    /// <summary>A copy of one of the ticket's texts, stretched across the cell at its top (1) or bottom (0).</summary>
    private static TMP_Text Text(TMP_Text proto, RectTransform row, string name, float edge, float height)
    {
        var t = Instantiate(proto, row, false);
        t.name = name;
        // Drop the game's own scripts on the copy (localisation would overwrite our text).
        foreach (var mb in t.GetComponents<MonoBehaviour>())
        {
            if (mb == t) continue;
            var ns = mb.GetType().Namespace ?? "";
            if (!ns.StartsWith("UnityEngine") && !ns.StartsWith("TMPro")) Destroy(mb);
        }
        foreach (Transform child in t.transform) Destroy(child.gameObject);
        var r = t.rectTransform;
        r.anchorMin = new Vector2(0f, edge);
        r.anchorMax = new Vector2(1f, edge);
        r.pivot = new Vector2(0.5f, edge);
        r.sizeDelta = new Vector2(0f, height);
        r.anchoredPosition = new Vector2(0f, edge > 0.5f ? -6f : 0f);
        t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>A copy of the ticket's own ascent arrow, either side of the choice.</summary>
    private static Button Arrow(Button proto, RectTransform cell, float side, UnityEngine.Events.UnityAction onClick)
    {
        var b = Instantiate(proto, cell, false);
        b.name = side < 0f ? "Left" : "Right";
        b.onClick = new Button.ButtonClickedEvent();
        b.onClick.AddListener(onClick);
        var r = (RectTransform)b.transform;
        var size = ((RectTransform)proto.transform).rect.size;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size;
        r.localScale = Vector3.one * (ArrowSize / Mathf.Max(1f, size.y));
        r.anchoredPosition = new Vector2(side * ArrowGap, (CellHeight - CaptionHeight - 4f) * 0.5f);
        return b;
    }

    private void Refresh()
    {
        foreach (var c in cells)
        {
            if (c.Label == null) continue;
            c.Label.text = c.Value();
            c.Label.color = c.Hard() ? Blood : plain;
            bool can = c.CanChange();
            if (c.Left != null) { c.Left.interactable = can; c.Left.gameObject.SetActive(can); }
            if (c.Right != null) { c.Right.interactable = can; c.Right.gameObject.SetActive(can); }
        }
    }

    private float nextRefresh;

    private void Update()
    {
        // Others' tickets follow the host's choices while open.
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        Refresh();
    }
}
