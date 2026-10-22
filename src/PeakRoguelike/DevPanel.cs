using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PeakRoguelike;

/// <summary>
/// Dev panel (F8 by default). Everyone sees which build of the mod each player runs (each client
/// publishes "version (build time)" as a player property; the host gets a toast on a mismatch).
/// The host also gets clickable dev tools: Player (fly, god, heal, revive, warp), Items (tree
/// levels, give items/amulets, fusion, blessing) and World (time, blood moons, next biome).
/// </summary>
internal class DevPanel : MonoBehaviour
{
    public const string BuildKey = "prl.build";

    /// <summary>"0.4.1 · Oct 13 23:02" - the build time tells two builds of the same version apart.</summary>
    public static readonly string Build = MakeBuild();

    private static string MakeBuild()
    {
        string stamp = "";
        try
        {
            string path = typeof(DevPanel).Assembly.Location;
            if (!string.IsNullOrEmpty(path)) stamp = File.GetLastWriteTime(path).ToString("MMM d HH:mm");
        }
        catch { /* no file time - version alone */ }
        return stamp.Length > 0 ? $"{Plugin.Version} · {stamp}" : Plugin.Version;
    }

    private enum Tab { Build, Player, Items, World }

    private static DevPanel? instance;
    private CanvasGroup? group;
    private RectTransform? tabs, content;
    private TextMeshProUGUI? text;
    private Tab tab;
    private bool open, toolsShown;
    private float alpha, nextRefresh;
    private readonly HashSet<int> warned = new();

    private static readonly Color ButtonColor = new(0.16f, 0.17f, 0.2f, 0.95f);
    private static readonly Color OnColor = new(0.2f, 0.42f, 0.22f, 0.95f);
    private static readonly Color RedColor = new(0.45f, 0.08f, 0.06f, 0.95f);
    private static readonly Color TabColor = new(0.32f, 0.26f, 0.16f, 0.95f);

    /// <summary>Host (or offline) with dev tools switched on in the config.</summary>
    internal static bool ToolsAllowed => ModConfig.DevTools.Value && (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient);

    /// <summary>The panel is open with clickable tools: free the cursor, block game input.</summary>
    internal static bool Interactive => instance != null && instance.open && (ToolsAllowed || BugReports.PanelClickable);

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Dev Panel");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DevPanel>();
    }

    private void Build_()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 950;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var panel = Ui.Rect(transform, "Panel");
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-24, -24);
        panel.sizeDelta = new Vector2(640, 800);
        group = panel.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var bg = Ui.Image(panel, "Background", UiArt.Glow(160, 120, 24, 18), new Color(0.03f, 0.04f, 0.06f, 0.92f));
        Ui.Stretch(bg.rectTransform);
        bg.raycastTarget = true;

        tabs = Ui.Rect(panel, "Tabs");
        tabs.anchorMin = new Vector2(0f, 1f);
        tabs.anchorMax = new Vector2(1f, 1f);
        tabs.pivot = new Vector2(0.5f, 1f);
        tabs.anchoredPosition = new Vector2(0f, -16f);
        tabs.sizeDelta = new Vector2(-32f, 40f);

        content = Ui.Rect(panel, "Content");
        Ui.Stretch(content);
        content.offsetMin = new Vector2(22, 18);
        content.offsetMax = new Vector2(-22, -66);
        Rebuild();
    }

    private void Update()
    {
        if (group == null)
        {
            if (GUIManager.instance == null) return; // wait for the game's fonts
            Build_();
        }

        var kb = Keyboard.current;
        if (kb != null && kb[ModConfig.DevPanelKey.Value].wasPressedThisFrame) { open = !open; if (open) BugReports.AskCounts(); }
        if (kb != null && ToolsAllowed && kb[ModConfig.FlyKey.Value].wasPressedThisFrame && !ChoiceScreen.IsOpen)
            DevCheats.SetFly(!DevCheats.Fly);
        if (DevCheats.Fly && !ToolsAllowed) DevCheats.SetFly(false); // lost host
        DevCheats.Tick();

        alpha = Mathf.MoveTowards(alpha, open ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        group!.alpha = alpha;
        group.blocksRaycasts = group.interactable = Interactive;

        // The host's tabs appear/disappear with host status (and the config switch).
        if (toolsShown != ToolsAllowed)
        {
            if (!ToolsAllowed) tab = Tab.Build;
            Rebuild();
        }

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        if (tab == Tab.Build && bugKey != BugReports.PanelKey) Rebuild(); // bug report buttons changed
        WarnHost();
        if (alpha > 0f) Refresh();
    }

    // --- layout helpers -------------------------------------------------------------------------

    private float cursorY;

    private void Rebuild()
    {
        if (tabs == null || content == null) return;
        toolsShown = ToolsAllowed;
        foreach (Transform c in tabs) Destroy(c.gameObject);
        foreach (Transform c in content) Destroy(c.gameObject);
        text = null;

        var names = toolsShown ? (Tab[])Enum.GetValues(typeof(Tab)) : new[] { Tab.Build };
        float w = (640f - 32f - (names.Length - 1) * 8f) / names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            var t = names[i];
            Button(tabs, t.ToString().ToUpperInvariant(), () => { tab = t; Rebuild(); }, i * (w + 8f), 0f, w, 40f,
                t == tab ? TabColor : ButtonColor, 20);
        }

        cursorY = 0f;
        switch (tab)
        {
            case Tab.Build: BuildTab(); break;
            case Tab.Player: PlayerTab(); break;
            case Tab.Items: ItemsTab(); break;
            case Tab.World: WorldTab(); break;
        }
        Refresh();
    }

    private void Refresh()
    {
        if (text == null) return;
        text.text = tab switch
        {
            Tab.Build => Describe(),
            Tab.World => WorldStatus(),
            Tab.Player => PlayerStatus(),
            _ => "",
        };
    }

    private TextMeshProUGUI Label(string s, float height, float size = 18)
    {
        var t = Ui.Text(content!, "Label", s, UiArt.BodyFont, size, ChoiceScreen.Cream, TextAlignmentOptions.TopLeft);
        Place(t.rectTransform, 0f, cursorY, 596f, height);
        t.richText = true;
        t.enableWordWrapping = false;
        cursorY += height;
        return t;
    }

    private static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void Button(Transform parent, string label, Action onClick, float x, float y, float w, float h, Color color, float size = 17)
    {
        var img = Ui.Image(parent, "Button " + label, UiArt.Glow(64, 32, 8, 6), color);
        img.type = UnityEngine.UI.Image.Type.Sliced;
        img.raycastTarget = true;
        Place(img.rectTransform, x, y, w, h);
        var b = img.gameObject.AddComponent<UnityEngine.UI.Button>();
        b.targetGraphic = img;
        var colors = b.colors;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        b.colors = colors;
        b.onClick.AddListener(() =>
        {
            try { onClick(); }
            catch (Exception e) { Hud.Toast($"Dev tool failed: {e.Message}", BloodMoon.Blood); Plugin.Log.LogWarning($"Dev tool '{label}' failed: {e}"); }
        });
        var t = Ui.Text(img.rectTransform, "Label", label, UiArt.BodyFont, size, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        Ui.Stretch(t.rectTransform);
        t.enableWordWrapping = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = 10;
        t.fontSizeMax = size;
    }

    /// <summary>A row of equal buttons across the panel.</summary>
    private void Row(params (string label, Action onClick, Color color)[] buttons)
    {
        float w = (596f - (buttons.Length - 1) * 8f) / buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
            Button(content!, buttons[i].label, buttons[i].onClick, i * (w + 8f), cursorY, w, 36f, buttons[i].color);
        cursorY += 44f;
    }

    private static (string, Action, Color) B(string label, Action onClick, Color? color = null) => (label, onClick, color ?? ButtonColor);

    /// <summary>An on/off config switch: red when on.</summary>
    private (string, Action, Color) Toggle(string label, BepInEx.Configuration.ConfigEntry<bool> entry) =>
        B($"{label}: {(entry.Value ? "ON" : "off")}", () => { entry.Value = !entry.Value; Rebuild(); }, entry.Value ? RedColor : ButtonColor);
    private static (string, Action, Color) T(string label, bool on, Action onClick) => ($"{label}: {(on ? "ON" : "OFF")}", onClick, on ? OnColor : ButtonColor);

    private static bool InGame(bool needRun = false)
    {
        if (Character.localCharacter == null) { Hud.Toast("No character yet", ChoiceScreen.Cream); return false; }
        if (needRun && (GameUtils.instance == null || GameUtils.instance.m_inAirport || !MapHandler.Exists))
        { Hud.Toast("Start a run first", ChoiceScreen.Cream); return false; }
        return true;
    }

    // --- Build tab --------------------------------------------------------------------------------

    private string bugKey = "";

    private void BuildTab()
    {
        text = Label("", 560f, 20);
        bugKey = BugReports.PanelKey;
        if (!PhotonNetwork.InRoom) return;
        if (BugReports.IsHost)
            Row(BugReports.Collecting ? B("Stop collecting", BugReports.StopCollect, RedColor) : B("Collect bug reports", BugReports.Collect, OnColor));
        else if (BugReports.Sending)
            Row(B("Sending reports...", () => { }));
        else
            Row(BugReports.InAirport ? B("Send my bug reports", BugReports.SendMine, OnColor) : B("Send my bug reports (airport only)", () => { }));
    }

    private static string BuildOf(Photon.Realtime.Player p) =>
        p.CustomProperties.TryGetValue(BuildKey, out object raw) && raw is string s ? s : "";

    private static string VersionOf(string build) => build.Split(' ')[0];

    private string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("<b>PEAK ROGUELIKE  ·  BUILDS</b>\n");
        sb.Append($"<size=16><color=#D9C9A3>You: {Build}     [{ModConfig.DevPanelKey.Value}] close</color></size>\n\n");
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            sb.Append("<color=#D9C9A3>Not in a lobby.</color>");
            return sb.ToString();
        }

        foreach (var p in PhotonNetwork.CurrentRoom.Players.Values.OrderBy(x => x.ActorNumber))
        {
            string build = BuildOf(p);
            string who = string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName;
            string tags = (p.IsMasterClient ? " <color=#FFC84F>(host)</color>" : "") + (p.IsLocal ? " <color=#8FD0FF>(you)</color>" : "");
            string shown = build.Length == 0 ? "<color=#FF6345>no mod / not reported</color>"
                : build == Build ? $"<color=#9BE07A>{build}</color>"
                : VersionOf(build) == Plugin.Version ? $"<color=#FFD24A>{build}  (same version, other build)</color>"
                : $"<color=#FF6345>{build}  (MISMATCH)</color>";
            int waiting = PhotonNetwork.IsMasterClient && !p.IsLocal ? BugReports.Waiting(p.ActorNumber) : 0;
            string bugs = waiting > 0 ? $"  <color=#FFD24A>· {waiting} bug report{(waiting == 1 ? "" : "s")} waiting</color>" : "";
            sb.Append($"{who}{tags}{bugs}\n    {shown}\n");
        }

        sb.Append($"\n<size=16><color=#D9C9A3>Bug reports: [{ModConfig.BugReportKey.Value}] saves one on everyone's PC.");
        if (!PhotonNetwork.IsMasterClient)
        {
            int mine = BugReports.MyUnsent;
            sb.Append(mine > 0 ? $" You have {mine} unsent." : " Nothing unsent.");
        }
        if (BugReports.Status.Length > 0) sb.Append($"\n<color=#9BE07A>{BugReports.Status}</color>");
        sb.Append("</color></size>");
        return sb.ToString();
    }

    // --- Player tab -------------------------------------------------------------------------------

    private void PlayerTab()
    {
        Row(T($"Fly / noclip [{ModConfig.FlyKey.Value}]", DevCheats.Fly, () => { if (InGame()) DevCheats.SetFly(!DevCheats.Fly); Rebuild(); }));
        Row(T("God mode", DevCheats.God, () => { if (InGame()) DevCheats.SetGod(!DevCheats.God); Rebuild(); }),
            T("Infinite stamina", DevCheats.InfiniteStamina, () => { if (InGame()) DevCheats.SetInfiniteStamina(!DevCheats.InfiniteStamina); Rebuild(); }));
        Row(B("Heal + clear ailments", () => { if (InGame()) DevCheats.Heal(); }),
            B("Revive me", () => { if (InGame()) DevCheats.ReviveMe(); }),
            B("Revive everyone", () => { if (InGame()) DevCheats.ReviveAll(); }));
        Row(B("Warp to campfire", () => { if (InGame(true)) DevCheats.WarpToCampfire(); }));

        var others = Character.AllCharacters.Where(c => c != null && !c.isBot && !c.IsLocal && !c.data.dead).ToList();
        if (others.Count > 0)
        {
            Label("<color=#D9C9A3>Warp to a scout:</color>", 26f);
            foreach (var chunk in others.Select((c, i) => (c, i)).GroupBy(x => x.i / 3))
                Row(chunk.Select(x => B(x.c.characterName ?? "Scout", () => { var c = x.c; if (c != null && InGame()) DevCheats.WarpTo(c.Center + Vector3.up * 1.5f); })).ToArray());
        }
        cursorY += 8f;
        text = Label("", 200f, 16);
    }

    private static string PlayerStatus() =>
        "<color=#D9C9A3>Fly: look where you want to go · WASD · Space up · Ctrl down · Shift fast.\n" +
        "Turning fly off inside rock lifts you onto the surface.\n" +
        "Cheats switch off when you change level or respawn.</color>";

    // --- Items tab --------------------------------------------------------------------------------

    private void ItemsTab()
    {
        foreach (var tree in Trees.All.Values)
        {
            var id = tree.Id;
            int lv = RunState.Level(id), br = RunState.Branch(id);
            int fork = ForkLevel(tree);
            string path = fork > 0 && br > 0 ? $"  <color=#B49BFF>{PathName(tree, br)}</color>" : "";
            string name = lv > 0 ? tree.At(lv, br).Name : "-";
            TreeRow($"<b>{tree.Name}</b> {lv}/{tree.MaxFor(br)}  <size=14><color=#D9C9A3>{name}</color></size>{path}",
                () => SetTree(tree, lv - 1, br),
                () => SetTree(tree, lv + 1, br),
                fork > 0 ? () => SetTree(tree, lv, br == 1 ? 2 : 1) : null,
                () => Give(lv > 0 ? tree.At(lv, br).Grants ?? Trees.BaseRole(id) : Trees.BaseRole(id)));
        }
        cursorY += 6f;
        // Every fusion's finished item (Sugar-Rush comes out marked, like a real fusion).
        foreach (var chunk in Fusion.Recipes.Where(r => r.Result != null).Select((r, i) => (r, i)).GroupBy(x => x.i / 3))
            Row(chunk.Select(x => B(x.r.Name, () => { if (InGame()) Fusion.Give(x.r); }, Color.Lerp(ButtonColor, ChoiceScreen.FusionGlow, 0.25f))).ToArray());
        Row(B("Max all", MaxAll, OnColor), B("Reset all", ResetAll, RedColor));
        Row(Enum.GetValues(typeof(RelicGem)).Cast<RelicGem>()
            .Select(g => B($"Amulet: {g}", () => { var a = Relics.Amulet(g); if (a != null) GiveItem(a); }, Color.Lerp(ButtonColor, Relics.GemColor(g), 0.35f)))
            .ToArray());
        Row(B("Give backpack", () => GivePrefab("Backpack")), B("Give fanny pack", () => GivePrefab("Fannypack")));
        Row(new[] { Curio.Bugle, Curio.Binoculars, Curio.Compass, Curio.Frisbee, Curio.BingBong }
            .Select(c => B($"Relic: {CurioRelics.Name(c)}", () => { if (InGame()) CurioRelics.DevBind(c); })).ToArray());
        Row(B("Bing Bong prank", () => { if (InGame()) BingBongPranks.DevPrank(); }));
        Row(BingBongPranks.DevNames.Select(n => B(n, () => { if (InGame()) BingBongPranks.DevPlay(n); })).ToArray());
        Row(B("Give flare", () => GivePrefab("Flare")), B("Flare drop", () => { if (InGame()) SupplyDrop.DevFlare(); }), B("Timed drop", () => { if (InGame()) SupplyDrop.DevTimed(); }));
        Row(B("Open fusion screen", () => { if (InGame()) Fusion.DevOpen(); }),
            B("Statue blessing", () => { if (InGame()) Blessing.DevBless(); }));
    }

    private void TreeRow(string label, Action minus, Action plus, Action? path, Action? give)
    {
        var t = Ui.Text(content!, "Label", label, UiArt.BodyFont, 17, ChoiceScreen.Cream, TextAlignmentOptions.Left);
        Place(t.rectTransform, 0f, cursorY, 330f, 32f);
        t.richText = true;
        t.enableWordWrapping = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = 11f;
        t.fontSizeMax = 17f;
        t.overflowMode = TextOverflowModes.Ellipsis;
        float x = 336f;
        Button(content!, "-", () => { minus(); Rebuild(); }, x, cursorY, 40f, 30f, ButtonColor, 20);
        Button(content!, "+", () => { plus(); Rebuild(); }, x + 46f, cursorY, 40f, 30f, ButtonColor, 20);
        if (path != null) Button(content!, "Path", () => { path(); Rebuild(); }, x + 92f, cursorY, 70f, 30f, ButtonColor, 15);
        if (give != null) Button(content!, "Give", give, x + 168f, cursorY, 92f, 30f, ButtonColor, 15);
        cursorY += 36f;
    }

    /// <summary>The level at which a tree forks into two paths (0 = no fork).</summary>
    private static int ForkLevel(TreeDef tree)
    {
        for (int i = 0; i < tree.Levels.Length; i++)
            if (tree.Levels[i].Length > 1) return i + 1;
        return 0;
    }

    private static string PathName(TreeDef tree, int branch)
    {
        int fork = ForkLevel(tree);
        return fork > 0 ? tree.Levels[fork - 1].FirstOrDefault(o => o.Branch == branch)?.Name ?? $"path {branch}" : "";
    }

    private static void SetTree(TreeDef tree, int level, int branch)
    {
        int fork = ForkLevel(tree);
        if (fork > 0 && level >= fork && branch == 0) branch = 1; // reaching the fork: default to the first path
        level = Mathf.Clamp(level, 0, tree.MaxFor(branch));
        if (level == 0) branch = 0;
        RunState.SetLevel(tree.Id, level, branch);
    }

    private void MaxAll()
    {
        foreach (var tree in Trees.All.Values)
        {
            int br = RunState.Branch(tree.Id);
            if (ForkLevel(tree) > 0 && br == 0) br = 1;
            RunState.SetLevel(tree.Id, tree.MaxFor(br), br);
        }
        Hud.Toast("Every item tree maxed", Trees.Gold);
        Rebuild();
    }

    private void ResetAll()
    {
        foreach (var tree in Trees.All.Values) RunState.SetLevel(tree.Id, 0, 0);
        RunState.Fused.Clear();
        Hud.Toast("Item levels reset", ChoiceScreen.Cream);
        Rebuild();
    }

    private static void Give(string role)
    {
        var item = Items.Find(role);
        if (item == null) { Hud.Toast($"No item for '{role}'", ChoiceScreen.Cream); return; }
        GiveItem(item);
    }

    private static void GivePrefab(string prefab)
    {
        var item = Items.Database.FirstOrDefault(i => i != null && i.gameObject.name == prefab);
        if (item != null) GiveItem(item);
        else Hud.Toast($"No item '{prefab}' in this game version", ChoiceScreen.Cream);
    }

    private static void GiveItem(Item item)
    {
        var me = Character.localCharacter;
        if (!InGame() || me == null) return;
        ItemDatabase.Add(item, me.Center + me.data.lookDirection_Flat * 0.8f + Vector3.up * 0.5f);
        Hud.Toast($"Gave {Items.DisplayName(item)}", ChoiceScreen.Cream);
    }

    // --- World tab --------------------------------------------------------------------------------

    private void WorldTab()
    {
        Label("<color=#D9C9A3>Time of day</color>", 26f);
        Row(B("Morning", () => { if (InGame(true)) DevCheats.SetTime(8f); }),
            B("Dusk (night in ~10s)", () => { if (InGame(true)) DevCheats.SetTime(DayNightManager.instance.dayEnd - 0.4f); }),
            B("Night", () => { if (InGame(true)) DevCheats.SetTime(DayNightManager.instance.dayEnd + 0.05f); }));
        Label("<color=#FF5A4A>Blood moon (starts now, makes it night)</color>", 26f);
        Row(Enumerable.Range(1, 4).Select(t => B($"Tier {t}", () => { if (InGame(true)) BloodMoon.DevStart(t); }, RedColor))
            .Append(B("End", () => BloodMoon.DevStart(0))).ToArray());
        Label($"<color=#FF5A4A>One hazard alone (tier 1 now; later moons keep it first) - {(BloodMoon.DevFirst is { } first ? $"{first} first" : "shuffled")}</color>", 26f);
        Row(new[] { BloodMoon.Hazard.Zombies, BloodMoon.Hazard.Scoutmaster, BloodMoon.Hazard.Tornado, BloodMoon.Hazard.Meteors }
            .Select(h => B(h.ToString(), () => { if (InGame(true)) { BloodMoon.DevAlone(h); Rebuild(); } }, BloodMoon.DevFirst == h ? OnColor : RedColor))
            .Append(B("Shuffle", () => { BloodMoon.DevAlone(null); Rebuild(); })).ToArray());
        Label("<color=#FF5A4A>Testing (saved to the config)</color>", 26f);
        // Blood moons, crates and relics (Shared / First come) live on the airport ticket.
        Row(Toggle("Moon every night", ModConfig.DevMoonEveryNight),
            Toggle("Moon on pickup", ModConfig.DevMoonOnPickup));
        Row(B($"Relic order: {RelicShuffle.Label(ModConfig.StatueOrder.Value)}", () => { RelicShuffle.Cycle(1); Rebuild(); },
            ModConfig.StatueOrder.Value == StatueOrder.Off ? ButtonColor : RedColor));
        Label("<color=#FFD86A>XP (gold bar)</color>", 26f);
        Row(B("+1 level", () => { if (InGame(true)) Xp.DevAdd(1000f); }), B("+250 XP", () => { if (InGame(true)) Xp.DevAdd(250f); }));
        MusicRow();
        Label($"<color=#FFD24A>Sun (now: {(Suns.Current is { } sun ? Suns.ColorName(sun) : "normal")}{(Suns.DevForce != null ? ", forced" : "")})</color>", 26f);
        Row(new[] { RelicGem.Ambition, RelicGem.Generosity, RelicGem.Initiative, RelicGem.Tenacity }
            .Select(g => B(Suns.ColorName(g), () => { Suns.DevForce = (int)g; Rebuild(); }, Color.Lerp(ButtonColor, Relics.GemColor(g), 0.35f)))
            .Append(B("None", () => { Suns.DevForce = -1; Rebuild(); }))
            .Append(B("Auto", () => { Suns.DevForce = null; Rebuild(); })).ToArray());
        Row(B("Refill parasol shade", () => { if (InGame()) Suns.DevRefillParasol(); }));
        PercentRow("Sunlight colour", ModConfig.SunLight, 1.5f);
        PercentRow("Sun brightness (makes up for the colour)", ModConfig.SunBrightness, 1.5f);
        PercentRow("Shade boost under a sun", ModConfig.SunShade, 0.5f);
        PercentRow("Sky colour", ModConfig.SunSkyBlend);
        PercentRow("Blood moon darkness", ModConfig.BmNightDarkness);
        PercentRow("Eclipse darkness", ModConfig.BmEclipseDarkness);
        // (These used to step the old RedSprintCut / RedLungeCut, unused since 0.12.25-26.)
        PercentRow("Red sprint cut", ModConfig.SunRedSprintSpeed, cut: true);
        PercentRow("Red lunge cut", ModConfig.SunRedLunge, cut: true);
        PercentRow("Yellow Shore drowsy per try", ModConfig.SunYellowDrowsy, 0.3f);
        Row(Toggle("Harsh suns: every biome (ticket)", ModConfig.HarshSunsEverywhere));
        Label("<color=#D9C9A3>Jackpots (next blessing / next revive)</color>", 26f);
        Row(B("Next blessing: fusion", () => { Blessing.DevJackpot = false; Hud.Toast("Next blessing: fusion jackpot", ChoiceScreen.Muted); }),
            B("Next blessing: dark", () => { Blessing.DevJackpot = true; Hud.Toast("Next blessing: dark jackpot", ChoiceScreen.Muted); }),
            B("Next revive: dark offer", () => { if (InGame(true)) { Blessing.DevForceRevive(); Hud.Toast("Next revive offers the dark items", ChoiceScreen.Muted); } }));
        Label("<color=#D9C9A3>Squad</color>", 26f);
        Row(B("Jump squad to next biome", () => { if (InGame(true)) DevCheats.NextBiome(); }));
        Label($"<color=#F2DB9E>Badge relics ({Badges.Owned.Count} owned)</color>", 26f);
        Row(B("Badge pick (statue)", () => { if (InGame()) Badges.OfferPick("Dev", UnityEngine.Random.Range(1, int.MaxValue), () => Rebuild(), () => { }); }),
            B("Random badge", () => { if (InGame()) { Badges.GiveRandom(); Rebuild(); } }));
        Label($"<color=#D9C9A3>Void{(VoidRun.Active ? $" (sun now: {(Suns.Current is { } vs ? Suns.ColorName(vs) : "none")})" : "")}</color>", 26f);
        Row(B("Give Scout's Honor (use it = Void)", () => { if (InGame()) VoidRun.DevGiveHonor(); }),
            B("Next Void sun", () => { if (InGame(true)) { VoidRun.DevNextSun(); Rebuild(); } }),
            B("Next Void moon", () => { if (InGame(true)) { VoidRun.DevNextMoon(); Rebuild(); } }));
        cursorY += 8f;
        text = Label("", 200f, 17);
    }

    /// <summary>Blood moon music volume: -/+ in 5% steps (saved to the config), and a 10s test play.</summary>
    private void MusicRow()
    {
        int pct = Mathf.RoundToInt(ModConfig.BmMusicVolume.Value * 100f);
        var t = Ui.Text(content!, "Label", $"<b>Music volume</b> {pct}%", UiArt.BodyFont, 17, ChoiceScreen.Cream, TextAlignmentOptions.Left);
        Place(t.rectTransform, 0f, cursorY, 330f, 32f);
        t.richText = true;
        t.enableWordWrapping = false;
        float x = 336f;
        Button(content!, "-", () => SetMusicVolume(-0.05f), x, cursorY, 40f, 30f, ButtonColor, 20);
        Button(content!, "+", () => SetMusicVolume(0.05f), x + 46f, cursorY, 40f, 30f, ButtonColor, 20);
        Button(content!, "Test 10s", BloodMoon.DevPreviewMusic, x + 92f, cursorY, 168f, 30f, ButtonColor, 15);
        cursorY += 40f;
    }

    /// <summary>A 0-100% setting: -/+ in 10% steps, saved to the config and seen in game at once.</summary>
    /// <param name="cut">Show and step the setting as a cut (1 - value): a 0.7 speed reads "30%".</param>
    private void PercentRow(string name, BepInEx.Configuration.ConfigEntry<float> entry, float max = 1f, bool cut = false)
    {
        int pct = Mathf.RoundToInt((cut ? 1f - entry.Value : entry.Value) * 100f);
        // The name shrinks to fit its own space; the value has a column of its own before the buttons.
        var t = Ui.Text(content!, "Label", $"<b>{name}</b>", UiArt.BodyFont, 17, ChoiceScreen.Cream, TextAlignmentOptions.Left);
        Place(t.rectTransform, 0f, cursorY, 400f, 32f);
        t.richText = true;
        t.enableWordWrapping = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = 11f;
        t.fontSizeMax = 17f;
        var v = Ui.Text(content!, "Value", $"{pct}%", UiArt.BodyFont, 17, ChoiceScreen.Cream, TextAlignmentOptions.Right);
        Place(v.rectTransform, 404f, cursorY, 64f, 32f);
        float x = 478f;
        void Step(float d)
        {
            entry.Value = Mathf.Clamp(Mathf.Round((entry.Value + (cut ? -d : d)) * 10f) / 10f, 0f, max);
            Rebuild();
        }
        Button(content!, "-", () => Step(-0.1f), x, cursorY, 40f, 30f, ButtonColor, 20);
        Button(content!, "+", () => Step(0.1f), x + 46f, cursorY, 40f, 30f, ButtonColor, 20);
        cursorY += 40f;
    }

    private void SetMusicVolume(float delta)
    {
        // Saved straight to the config file; the music picks it up on the next frame.
        ModConfig.BmMusicVolume.Value = Mathf.Clamp01(Mathf.Round((ModConfig.BmMusicVolume.Value + delta) * 20f) / 20f);
        Rebuild();
    }

    private static string WorldStatus()
    {
        var sb = new StringBuilder();
        var dnm = DayNightManager.instance;
        if (dnm != null) sb.Append($"<color=#D9C9A3>Day {dnm.dayCount} · {dnm.TimeOfDayString()} · {(dnm.isDay > 0.5f ? "day" : "night")}</color>\n");
        if (MapHandler.Exists) sb.Append($"<color=#D9C9A3>Section: {MapHandler.CurrentSegmentNumber}</color>\n");
        if (Suns.SunGlass > 0) sb.Append($"<color=#74B4FF>Blue sun glass: {Suns.SunGlass}% of your glass</color>\n");
        sb.Append(BloodMoon.Active
            ? $"<color=#FF5A4A>Blood moon tier {BloodMoon.Tier}: {Mathf.FloorToInt(BloodMoon.Elapsed)}s of {BloodMoon.Length:0}s\nOrder: {BloodMoon.OrderLabel}</color>"
            : "<color=#D9C9A3>No blood moon</color>");
        if (!BloodMoon.Active && MapHandler.Exists && (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient))
            sb.Append($"\n<color=#D9C9A3>Next moon order: {string.Join(" > ", RelicShuffle.MoonOrder())}</color>");
        return sb.ToString();
    }

    /// <summary>The host hears about anyone on a different build as soon as they're seen.</summary>
    private void WarnHost()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return;
        foreach (var p in PhotonNetwork.CurrentRoom.Players.Values)
        {
            if (p.IsLocal || warned.Contains(p.ActorNumber)) continue;
            string build = BuildOf(p);
            if (build.Length == 0) continue; // not reported yet (or no mod) - shown in the panel
            warned.Add(p.ActorNumber);
            if (VersionOf(build) != Plugin.Version)
                Hud.Toast($"{(string.IsNullOrEmpty(p.NickName) ? "A player" : p.NickName)} is on Roguelike {VersionOf(build)} (you: {Plugin.Version}) - {ModConfig.DevPanelKey.Value} for details", new Color(1f, 0.45f, 0.35f));
        }
    }
}
