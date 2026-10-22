using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace PeakRoguelike;

/// <summary>
/// The run report: what each scout did this run (relics carried, badges, XP level, best items,
/// fusions, crates, moons...), counted as it happens and mirrored into a player property, then
/// shown on the end screen as a second page of the scouting report (a button flips between them).
/// </summary>
internal class RunReport : MonoBehaviour
{
    private const string PropKey = "prl.rr";
    private static RunReport? instance;

    // --- this scout's run ------------------------------------------------------------------------
    /// <summary>Every relic bound this run, in order (death drops them, the report remembers).</summary>
    private static readonly List<(RelicGem gem, RelicTrigger trigger, bool cursed)> carried = new();
    private static int crates, used, fires, moons, topTier, glass, furthest = -1;
    private static string furthestName = "";
    private static bool dirty;
    private float publishedAt;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Run report");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<RunReport>();
    }

    internal static void Reset()
    {
        carried.Clear();
        crates = used = fires = moons = topTier = glass = 0;
        furthest = -1;
        furthestName = "";
        dirty = true;
    }

    internal static void RelicBound(RelicGem gem, RelicTrigger trigger, bool cursed)
    {
        // Each variant once; a real relic replaces the cursed copy of its colour.
        int i = carried.FindIndex(r => r.gem == gem && (r.trigger == trigger || r.cursed && !cursed));
        if (i < 0) carried.Add((gem, trigger, cursed));
        else if (carried[i].cursed && !cursed) carried[i] = (gem, trigger, false);
        dirty = true;
    }

    internal static void Crate() { crates++; dirty = true; }
    internal static void ItemUsed() { used++; dirty = true; }
    internal static void RelicFired() { fires++; dirty = true; }

    /// <summary>A blood moon or eclipse set: counted if we lived through it (the Void's minute moons don't count).</summary>
    internal static void MoonOver()
    {
        var me = Character.localCharacter;
        if (me == null || me.data.dead || VoidRun.Active) return;
        moons++;
        dirty = true;
    }

    private void Update()
    {
        try { Track(); } catch (Exception e) { Plugin.Log.LogWarning($"Run report tracking failed: {e.Message}"); }
        if (dirty && Time.unscaledTime - publishedAt > 3f) Publish();
    }

    private static void Track()
    {
        if (!MapHandler.Exists) return;
        var me = Character.localCharacter;
        if (me != null && !me.data.dead && me.data.petrifyAmount > glass) { glass = me.data.petrifyAmount; dirty = true; }
        if (BloodMoon.Active && BloodMoon.Tier > topTier) { topTier = BloodMoon.Tier; dirty = true; }
        int seg = (int)MapHandler.CurrentSegmentNumber;
        if (seg > furthest)
        {
            furthest = seg;
            furthestName = PlaceName(seg, Singleton<MapHandler>.Instance.GetCurrentBiome());
            dirty = true;
        }
    }

    private static string PlaceName(int seg, Biome.BiomeType biome) => (Segment)seg switch
    {
        Segment.Beach => "the Shore",
        Segment.Caldera => "the Caldera",
        Segment.TheKiln => "the Kiln",
        Segment.Peak => "the Peak",
        Segment.Void => "the Void",
        _ => $"the {biome}",
    };

    /// <summary>Send our report now (the end screen asks for it fresh).</summary>
    internal static void Publish()
    {
        dirty = false;
        if (instance != null) instance.publishedAt = Time.unscaledTime;
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
        try { PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [PropKey] = Mine().Encode() }); }
        catch (Exception e) { Plugin.Log.LogWarning($"Couldn't publish the run report: {e.Message}"); }
    }

    // --- one scout's report ------------------------------------------------------------------------

    internal class Card
    {
        public int Level, Crates, Used, Fires, Moons, TopTier, Glass, Furthest = -1;
        public string Place = "";
        public Curio Curio;
        public List<(RelicGem gem, RelicTrigger trigger, bool cursed)> Relics = new();
        public List<BadgeId> Badges = new();
        /// <summary>Best items (up to 5): (name, level), highest first.</summary>
        public List<(string name, int level)> Items = new();
        public List<string> Fusions = new();

        // Fields split by '|', lists by ',', parts by ':'. Names never hold those.
        public string Encode() => string.Join("|", new[]
        {
            $"lv={Level}", $"cr={Crates}", $"use={Used}", $"fire={Fires}", $"moon={Moons}", $"mt={TopTier}", $"gl={Glass}",
            $"far={Furthest}", $"place={Clean(Place)}", $"cur={(int)Curio}",
            "rel=" + string.Join(",", Relics.Select(r => $"{r.gem}:{r.trigger}" + (r.cursed ? ":c" : ""))),
            "bdg=" + string.Join(",", Badges),
            "it=" + string.Join(",", Items.Select(i => $"{Clean(i.name)}:{i.level}")),
            "fz=" + string.Join(",", Fusions.Select(Clean)),
        });

        private static string Clean(string s) => s.Replace("|", "").Replace(",", "").Replace(":", "").Replace("=", "");

        public static Card Decode(string s)
        {
            var c = new Card();
            foreach (string field in s.Split('|'))
            {
                int eq = field.IndexOf('=');
                if (eq < 0) continue;
                string k = field.Substring(0, eq), v = field.Substring(eq + 1);
                var list = v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int.TryParse(v, out int n);
                switch (k)
                {
                    case "lv": c.Level = n; break;
                    case "cr": c.Crates = n; break;
                    case "use": c.Used = n; break;
                    case "fire": c.Fires = n; break;
                    case "moon": c.Moons = n; break;
                    case "mt": c.TopTier = n; break;
                    case "gl": c.Glass = n; break;
                    case "far": c.Furthest = n; break;
                    case "place": c.Place = v; break;
                    case "cur": c.Curio = (Curio)n; break;
                    case "rel":
                        foreach (string r in list)
                        {
                            var bits = r.Split(':');
                            if (bits.Length >= 2 && Enum.TryParse(bits[0], out RelicGem g) && Enum.TryParse(bits[1], out RelicTrigger t))
                                c.Relics.Add((g, t, bits.Length >= 3 && bits[2] == "c"));
                        }
                        break;
                    case "bdg":
                        foreach (string b in list) if (Enum.TryParse(b, out BadgeId id)) c.Badges.Add(id);
                        break;
                    case "it":
                        foreach (string i in list)
                        {
                            var bits = i.Split(':');
                            if (bits.Length == 2 && int.TryParse(bits[1], out int lv)) c.Items.Add((bits[0], lv));
                        }
                        break;
                    case "fz": c.Fusions.AddRange(list); break;
                }
            }
            return c;
        }
    }

    /// <summary>Our own report, fresh.</summary>
    internal static Card Mine()
    {
        var c = new Card
        {
            Level = Xp.Level, Crates = crates, Used = used, Fires = fires, Moons = moons, TopTier = topTier, Glass = glass,
            Furthest = furthest, Place = furthestName, Curio = CurioRelics.Mine,
        };
        c.Relics.AddRange(carried);
        c.Badges.AddRange(PeakRoguelike.Badges.Owned);
        foreach (var tree in Trees.All.Values)
        {
            int lv = RunState.Level(tree.Id);
            if (lv > 0) c.Items.Add((tree.At(lv, RunState.Branch(tree.Id)).Name, lv));
        }
        c.Items = c.Items.OrderByDescending(i => i.level).Take(5).ToList();
        foreach (string id in RunState.Fused)
            c.Fusions.Add(Fusion.Recipes.FirstOrDefault(r => r.Id == id)?.Name ?? id);
        return c;
    }

    /// <summary>Any player's report: ours live, theirs from their property (null = no mod).</summary>
    internal static Card? Of(Photon.Realtime.Player? p)
    {
        if (p == null || p.IsLocal) return Mine();
        return p.CustomProperties.TryGetValue(PropKey, out object raw) && raw is string s ? Card.Decode(s) : null;
    }
}

[HarmonyPatch]
internal static class RunReportPatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(EndScreen), "Start")]
    private static void EndScreenStart(EndScreen __instance)
    {
        try
        {
            RunReport.Publish();
            __instance.gameObject.AddComponent<RunReportPage>();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Run report page failed: {e.Message}"); }
    }
}

/// <summary>
/// The report's page on the end screen: a second sheet laid over the scouting report's paper,
/// in its own font and ink, with a button beside NEXT to flip between the two.
/// </summary>
internal class RunReportPage : MonoBehaviour
{
    private static RunReportPage? current;
    /// <summary>The end screen is up (the cursor is free): the relic tray's hover details work too.</summary>
    internal static bool Open => current != null && current.isActiveAndEnabled && current.screen != null && current.screen.gameObject.activeInHierarchy;

    private EndScreen screen = null!;
    private Button? toggle;
    private TMP_Text? toggleLabel;
    private RectTransform? page;
    private bool showing;

    // The vanilla report's look, borrowed once it exists.
    private TMP_FontAsset? font;
    private Material? fontMat;
    private Color ink = new(0.25f, 0.17f, 0.12f);

    // Hover details for the icons on the page.
    private readonly List<(RectTransform rect, string kind, string title, string body)> tips = new();
    private RectTransform? tipBox;
    private TextMeshProUGUI? tipText;
    private Camera? cam;

    private void Awake()
    {
        screen = GetComponent<EndScreen>();
        current = this;
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
    }

    private void Update()
    {
        // The vanilla sequence ends by showing its buttons: that's when the report is ready to read.
        if (toggle == null && screen != null && screen.buttons != null && screen.buttons.activeInHierarchy)
        {
            try { BuildToggle(); }
            catch (Exception e) { Plugin.Log.LogWarning($"Run report button failed: {e.Message}"); enabled = false; }
        }
        if (showing)
        {
            Hover();
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) Turn(-1);
            if (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) Turn(+1);
        }
    }

    private void Hover()
    {
        if (tipBox == null || tipText == null || page == null || UnityEngine.InputSystem.Mouse.current is not { } mouse) return;
        Vector2 pos = mouse.position.ReadValue();
        foreach (var (rect, kind, title, body) in tips)
        {
            if (rect == null || !RectTransformUtility.RectangleContainsScreenPoint(rect, pos, cam)) continue;
            tipText.text = $"<size=70%><alpha=#AA>{kind.ToUpperInvariant()}</size><alpha=#FF>\n<b>{title}</b>\n<size=85%>{body}</size>";
            tipText.ForceMeshUpdate();
            tipBox.sizeDelta = new Vector2(tipBox.sizeDelta.x, tipText.preferredHeight + 24f * Unit);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(page, pos, cam, out var local);
            // Hang it below-right of the pointer, kept on the page.
            var r = page.rect;
            float x = Mathf.Clamp(local.x + 18f * Unit, r.xMin, r.xMax - tipBox.sizeDelta.x);
            float y = Mathf.Clamp(local.y - 18f * Unit, r.yMin + tipBox.sizeDelta.y, r.yMax);
            tipBox.anchoredPosition = new Vector2(x - r.xMin, y - r.yMax);
            tipBox.gameObject.SetActive(true);
            return;
        }
        tipBox.gameObject.SetActive(false);
    }

    private float Unit = 1f;

    private void BuildToggle()
    {
        var next = screen.nextButton;
        var go = Instantiate(next.gameObject, next.transform.parent);
        go.name = "RunReportButton";
        foreach (var lt in go.GetComponentsInChildren<LocalizedText>(true)) Destroy(lt);
        toggle = go.GetComponent<Button>();
        toggle.onClick = new Button.ButtonClickedEvent();
        toggle.onClick.AddListener(Flip);
        toggleLabel = go.GetComponentInChildren<TMP_Text>(true);
        SetLabel();
        // Beside NEXT, on its left (a layout group, if there is one, places it by itself).
        var rt = (RectTransform)go.transform;
        if (next.transform.parent.GetComponent<LayoutGroup>() == null)
        {
            var nrt = (RectTransform)next.transform;
            rt.anchoredPosition = nrt.anchoredPosition - new Vector2(nrt.rect.width * nrt.localScale.x + 24f, 0f);
        }
        go.transform.SetSiblingIndex(next.transform.GetSiblingIndex());
        Plugin.Log.LogInfo($"Run report: button added beside NEXT ({Path(next.transform)}).");
    }

    private void SetLabel()
    {
        if (toggleLabel == null) return;
        toggleLabel.text = showing ? "TIMELINE" : "RUN REPORT";
        toggleLabel.enableAutoSizing = true;
        toggleLabel.fontSizeMin = 8f;
        toggleLabel.fontSizeMax = Mathf.Max(toggleLabel.fontSizeMax, toggleLabel.fontSize);
    }

    private void Flip()
    {
        showing = !showing;
        try
        {
            if (showing) { if (page != null) Destroy(page.gameObject); page = Build(); } // fresh: late reports arrive
            if (page != null) page.gameObject.SetActive(showing);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Run report page failed: {e.Message}"); showing = false; }
        SetLabel();
        UIInputHandler.SetSelectedObject(null);
    }

    // --- the page ------------------------------------------------------------------------------------

    /// <summary>
    /// The notebook sheet: the smallest picture on the end screen that covers both the time and
    /// the timeline (it needn't be their parent - the sheet is often a sibling drawn behind).
    /// </summary>
    private Image? FindPaper()
    {
        Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }
        Rect timeline = WorldRect(screen.timelinePanel), time = WorldRect(screen.endTime.rectTransform);
        Image? best = null;
        float bestArea = float.MaxValue;
        foreach (var img in screen.GetComponentsInChildren<Image>(true))
        {
            if (!img.enabled || !img.gameObject.activeInHierarchy || img.sprite == null || img.color.a < 0.9f) continue;
            var r = WorldRect(img.rectTransform);
            bool holds = r.Contains(timeline.min) && r.Contains(timeline.max) && r.Contains(time.center);
            if (!holds || r.height < timeline.height * 1.5f) continue;
            float area = r.width * r.height;
            if (area < bestArea) { best = img; bestArea = area; }
        }
        return best;
    }

    /// <summary>A few levels of the end screen's layout, for tuning the page from a bug report.</summary>
    private void LogLayout(Transform t, int depth, List<string> lines)
    {
        if (depth > 4 || lines.Count > 80) return;
        var img = t.GetComponent<Image>();
        lines.Add(new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " (off)")
            + (img != null ? $" [img {(img.sprite != null ? img.sprite.name : "none")} a{img.color.a:0.0}]" : "")
            + (t is RectTransform rt ? $" {rt.rect.size}" : ""));
        foreach (Transform c in t) LogLayout(c, depth + 1, lines);
    }

    private RectTransform Build()
    {
        font = screen.endTime.font;
        fontMat = screen.endTime.fontSharedMaterial;
        ink = screen.endTime.color;
        var canvas = screen.GetComponentInParent<Canvas>()?.rootCanvas;
        cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        tips.Clear();

        var paper = FindPaper();
        RectTransform rt;
        float u;
        if (paper != null)
        {
            var prt = paper.rectTransform;
            Plugin.Log.LogInfo($"Run report: paper is {Path(prt)} ({prt.rect.size}).");
            // A twin of the sheet laid exactly over it, drawn after everything written on it.
            rt = Ui.Rect(prt.parent, "RunReport");
            rt.anchorMin = prt.anchorMin; rt.anchorMax = prt.anchorMax; rt.pivot = prt.pivot;
            rt.anchoredPosition = prt.anchoredPosition; rt.sizeDelta = prt.sizeDelta;
            rt.localRotation = prt.localRotation; rt.localScale = prt.localScale;
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = paper.sprite; bg.type = paper.type; bg.color = paper.color; bg.material = paper.material;
            bg.pixelsPerUnitMultiplier = paper.pixelsPerUnitMultiplier;
            rt.SetAsLastSibling();
            // The banner and the buttons stay on top of our sheet (unless they share a box with the timeline).
            foreach (var keep in new[] { screen.buttons, screen.peakBanner, screen.peakNadirBanner, screen.deadBanner, screen.yourFriendsWonBanner, screen.miniWinBanner })
                if (keep != null && keep.transform.IsChildOf(prt.parent) && ChildOf(prt.parent, keep.transform) is { } top
                    && !screen.timelinePanel.IsChildOf(top) && !screen.endTime.transform.IsChildOf(top))
                    top.SetAsLastSibling();
            u = prt.rect.height / 730f;
        }
        else
        {
            var lines = new List<string>();
            LogLayout(screen.transform, 0, lines);
            Plugin.Log.LogInfo("Run report: no paper found - a plain sheet in the middle. Layout:\n" + string.Join("\n", lines));
            rt = Ui.Rect(transform, "RunReport");
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(660f, 730f);
            var sheet = Ui.Image(rt, "Sheet", UiArt.Glow(128, 96, 6, 18), new Color(0.93f, 0.91f, 0.86f, 1f));
            Ui.Stretch(sheet.rectTransform);
            u = 1f;
        }
        Unit = u;

        // The writing area: clear of the binding (left), the banner (top) and the buttons (bottom).
        var area = Ui.Rect(rt, "Area");
        Ui.Stretch(area);
        area.offsetMin = new Vector2(52f * u, 30f * u);
        area.offsetMax = new Vector2(-36f * u, -140f * u);

        cards = Character.AllCharacters
            .Where(c => c != null && !c.isBot && c.photonView != null && c.photonView.IsOwnerActive && PlayerHandler.TryGetPlayer(c.photonView.OwnerActorNr, out _))
            .Select(c => (c, RunReport.Of(c.photonView.Owner)))
            .ToList();
        // The first time it opens, it opens on your own page.
        if (index < 0) index = Mathf.Max(0, cards.FindIndex(x => x.c.IsLocal));
        index = cards.Count == 0 ? 0 : Mathf.Clamp(index, 0, cards.Count - 1);

        // Heading, then the squad's line (on every page).
        float y = 0f;
        var head = Line(area, "RUN REPORT", 46f * u, ref y, 62f * u);
        head.characterSpacing = 4f;
        var far = cards.Where(x => x.card != null).Select(x => x.card!).OrderByDescending(x => x.Furthest).FirstOrDefault();
        var squad = new List<string>();
        if (far != null && far.Place.Length > 0) squad.Add($"Reached {far.Place}");
        if (RunManager.Instance != null) squad.Add(Clock(RunManager.Instance.TimeSinceRunStarted));
        if (Ascents.currentAscent > 0) squad.Add($"Ascent {Ascents.currentAscent}");
        if (DayNightManager.instance != null) squad.Add($"Day {DayNightManager.instance.dayCount}");
        Line(area, string.Join("  ·  ", squad), 22f * u, ref y, 34f * u).alpha = 0.75f;
        y += 14f * u;

        // One scout's page in the middle, the page turner at the foot.
        const float footH = 44f;
        scoutArea = Ui.Rect(area, "Scout");
        Ui.Stretch(scoutArea);
        scoutArea.offsetMax = new Vector2(0f, -y);
        scoutArea.offsetMin = new Vector2(0f, footH * u);

        var foot = Ui.Rect(area, "Pages");
        foot.anchorMin = new Vector2(0f, 0f);
        foot.anchorMax = new Vector2(1f, 0f);
        foot.pivot = new Vector2(0.5f, 0f);
        foot.sizeDelta = new Vector2(0f, footH * u);
        counter = Text(foot, "", 22f * u, TextAlignmentOptions.Center, FontStyles.Normal);
        Ui.Stretch(counter.rectTransform);
        counter.textWrappingMode = TextWrappingModes.NoWrap;
        if (cards.Count > 1)
        {
            Arrow(foot, "<", -1, 0.22f, u);
            Arrow(foot, ">", +1, 0.78f, u);
        }

        // The hover card: dark ink, cream words, like the relic tray's.
        tipBox = Ui.Rect(rt, "Details");
        tipBox.anchorMin = tipBox.anchorMax = tipBox.pivot = new Vector2(0f, 1f);
        tipBox.sizeDelta = new Vector2(380f * u, 120f * u);
        var ink2 = ChoiceScreen.InkBrown;
        var panel = Ui.Image(tipBox, "Panel", UiArt.Glow(128, 96, 6, 18), new Color(ink2.r, ink2.g, ink2.b, 0.96f));
        Ui.Stretch(panel.rectTransform);
        tipText = Ui.Text(tipBox, "Text", "", UiArt.BodyFont, 22f * u, ChoiceScreen.Cream, TextAlignmentOptions.TopLeft);
        Ui.Stretch(tipText.rectTransform);
        tipText.rectTransform.offsetMin = new Vector2(14f * u, 12f * u);
        tipText.rectTransform.offsetMax = new Vector2(-14f * u, -12f * u);
        tipText.textWrappingMode = TextWrappingModes.Normal;
        tipText.lineSpacing = -4f;
        tipBox.gameObject.SetActive(false);

        ShowScout();
        return rt;
    }

    // --- pages: one scout each ------------------------------------------------------------------------

    private List<(Character c, RunReport.Card? card)> cards = new();
    private int index = -1;
    private RectTransform? scoutArea;
    private TextMeshProUGUI? counter;

    /// <summary>A page-turn arrow in ink: a big character on an invisible button.</summary>
    private void Arrow(RectTransform parent, string glyph, int step, float at, float u)
    {
        var img = Ui.Image(parent, glyph == "<" ? "Prev" : "Next", null, new Color(1f, 1f, 1f, 0f));
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(at, 0.5f);
        rt.sizeDelta = new Vector2(70f * u, 44f * u);
        var label = Text(rt, glyph, 40f * u, TextAlignmentOptions.Center, FontStyles.Normal);
        label.richText = false; // a lone "<" is not a tag
        Ui.Stretch(label.rectTransform);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        var button = img.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Turn(step));
    }

    private void Turn(int step)
    {
        if (cards.Count < 2) return;
        index = (index + step + cards.Count) % cards.Count;
        try { ShowScout(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Run report page turn failed: {e.Message}"); }
        UIInputHandler.SetSelectedObject(null);
    }

    private void ShowScout()
    {
        if (scoutArea == null) return;
        foreach (Transform child in scoutArea) Destroy(child.gameObject);
        tips.Clear();
        if (tipBox != null) tipBox.gameObject.SetActive(false);
        if (counter != null) counter.text = cards.Count > 1 ? $"Scout {index + 1} of {cards.Count}" : "";
        if (cards.Count == 0) return;
        var (c, card) = cards[index];
        if (c == null) return;
        ScoutPage(scoutArea, c, card, Unit);
    }

    private void ScoutPage(RectTransform box, Character c, RunReport.Card? card, float u)
    {
        // The name strip, tinted like the vanilla scout window.
        Color pc = c.refs.customization.PlayerColor;
        var strip = Ui.Image(box, "Strip", null, new Color(pc.r, pc.g, pc.b, 0.3f));
        Top(strip.rectTransform, 0f, 50f * u);
        var name = Text(strip.rectTransform, c.characterName, 32f * u, TextAlignmentOptions.MidlineLeft, c.IsLocal ? FontStyles.Underline : FontStyles.Normal);
        Ui.Stretch(name.rectTransform);
        name.rectTransform.offsetMin = new Vector2(12f * u, 0f);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        var level = Text(strip.rectTransform, card == null ? "NO MOD" : $"LEVEL {card.Level}", 26f * u, TextAlignmentOptions.MidlineRight, FontStyles.Normal);
        Ui.Stretch(level.rectTransform);
        level.rectTransform.offsetMax = new Vector2(-12f * u, 0f);
        level.textWrappingMode = TextWrappingModes.NoWrap;
        if (card == null) return;

        // Everything carried: relics (as held), the starting relic, badges - each with its name under it.
        var icons = new List<(Texture tex, Color tint, string label, string kind, string title, string body)>();
        foreach (var (gem, trigger, cursed) in card.Relics)
            if (Items.Icon(Relics.Amulet(gem)) is { } t)
            {
                var def = Relics.Get(gem, trigger);
                icons.Add((t, cursed ? new Color(0.55f, 0.4f, 0.6f) : Color.white, def.Name,
                    cursed ? $"Cursed copy · {Relics.GemName(gem)}" : Relics.GemName(gem), def.Name, $"{def.Desc}\nFires on: {trigger.Label()}"));
            }
        if (card.Curio != Curio.None && Items.Icon(CurioRelics.ItemOf(card.Curio)) is { } ct)
            icons.Add((ct, Color.white, CurioRelics.Name(card.Curio), "Starting relic", CurioRelics.Name(card.Curio), CurioRelics.Desc(card.Curio)));
        foreach (var b in card.Badges)
            if (Badges.Icon(b) is { } bt) { var d = Badges.Get(b); icons.Add((bt, Color.white, d.Name, "Badge relic", d.Name, d.Desc)); }

        float icon = 62f * u, cell = 82f * u, labelH = 28f * u, rowH = icon + labelH + 8f * u, rowTop = 60f * u;
        float width = box.rect.width > 1f ? box.rect.width : 570f * u;
        int perRow = Mathf.Max(1, Mathf.FloorToInt(width / cell));
        var shown = icons.Take(perRow * 2).ToList(); // two rows at most
        for (int i = 0; i < shown.Count; i++)
        {
            var (tex, tint, label, kind, title, desc) = shown[i];
            var slot = Ui.Rect(box, "Icon");
            slot.anchorMin = slot.anchorMax = slot.pivot = new Vector2(0f, 1f);
            slot.sizeDelta = new Vector2(cell, rowH);
            slot.anchoredPosition = new Vector2(i % perRow * cell, -(rowTop + i / perRow * rowH));
            var pic = new GameObject("Picture", typeof(RectTransform)).AddComponent<RawImage>();
            pic.rectTransform.SetParent(slot, false);
            pic.rectTransform.anchorMin = pic.rectTransform.anchorMax = pic.rectTransform.pivot = new Vector2(0.5f, 1f);
            pic.rectTransform.sizeDelta = new Vector2(icon, icon);
            pic.texture = tex;
            pic.color = tint;
            pic.raycastTarget = false;
            var name2 = Text(slot, label.ToUpperInvariant(), 12f * u, TextAlignmentOptions.Center, FontStyles.Normal);
            var nrt = name2.rectTransform;
            nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(0.5f, 1f);
            nrt.sizeDelta = new Vector2(cell - 4f * u, labelH);
            nrt.anchoredPosition = new Vector2(0f, -icon - 2f * u);
            name2.textWrappingMode = TextWrappingModes.Normal;
            name2.enableAutoSizing = true;
            name2.fontSizeMin = 7f * u;
            name2.fontSizeMax = 12f * u;
            tips.Add((pic.rectTransform, kind, title, desc));
        }
        int rows = shown.Count == 0 ? 0 : (shown.Count + perRow - 1) / perRow;
        float textTop = rowTop + rows * rowH + 10f * u;

        // Two columns: best items (and fusions) on the left, the run's counts on the right.
        var itemLines = new List<string> { "BEST ITEMS" };
        if (card.Items.Count == 0) itemLines.Add("<alpha=#99>none");
        itemLines.AddRange(card.Items.Select(i => $"{i.name}  <alpha=#99>Lv {i.level}<alpha=#FF>"));
        if (card.Fusions.Count > 0) { itemLines.Add(""); itemLines.Add("FUSED"); itemLines.AddRange(card.Fusions); }

        var countLines = new List<string>
        {
            "THE CLIMB",
            $"Crates opened  {card.Crates}",
            $"Items used  {card.Used}",
            $"Relic fires  {card.Fires}",
            $"Moons survived  {card.Moons}" + (card.TopTier > 0 ? $"  <alpha=#99>tier {card.TopTier}" : ""),
            $"Peak glass  {card.Glass}%",
        };

        float colH = box.rect.height > 1f ? box.rect.height - textTop : 200f * u;
        Column(box, string.Join("\n", itemLines), 0f, 0.55f, textTop, colH, u);
        Column(box, string.Join("\n", countLines), 0.57f, 1f, textTop, colH, u);
    }

    private void Column(RectTransform box, string text, float from, float to, float top, float height, float u)
    {
        var t = Text(box, text, 21f * u, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(from, 1f);
        rt.anchorMax = new Vector2(to, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = new Vector2(4f * u, -top);
        t.enableAutoSizing = true;
        t.fontSizeMin = 11f * u;
        t.fontSizeMax = 21f * u;
        t.lineSpacing = 4f;
    }

    private static string Clock(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}";
    }

    private TextMeshProUGUI Line(RectTransform parent, string text, float size, ref float y, float height, FontStyles style = FontStyles.Normal)
    {
        var t = Text(parent, text, size, TextAlignmentOptions.Center, style);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        Top(t.rectTransform, y, height);
        y += height;
        return t;
    }

    private TextMeshProUGUI Text(Transform parent, string text, float size, TextAlignmentOptions align, FontStyles style)
    {
        var t = Ui.Rect(parent, "Text").gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        if (fontMat != null) t.fontSharedMaterial = fontMat;
        t.text = text;
        t.fontSize = size;
        t.color = ink;
        t.alignment = align;
        t.fontStyle = style;
        t.raycastTarget = false;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow; // Ellipsis hid lines a hair too tall for their box
        return t;
    }

    /// <summary>Full width, hung from the parent's top.</summary>
    private static void Top(RectTransform rt, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = new Vector2(0f, -top);
    }

    private static Transform? ChildOf(Transform parent, Transform t)
    {
        while (t != null && t.parent != parent) t = t.parent;
        return t;
    }

    private static string Path(Transform t)
    {
        var names = new List<string>();
        for (var x = t; x != null; x = x.parent) names.Add(x.name);
        names.Reverse();
        return string.Join("/", names);
    }
}
