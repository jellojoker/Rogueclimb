using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakRoguelike;

/// <summary>
/// Always-on overlay: relic medallions (top-left), the "fusion ready" prompt, level-up
/// banners and short toasts.
/// </summary>
internal class Hud : MonoBehaviour
{
    private static Hud? instance;

    private RectTransform? root;
    private RectTransform tray = null!;
    private readonly Dictionary<RelicGem, Medallion> medallions = new();
    private CanvasGroup fusionPrompt = null!;
    private TextMeshProUGUI fusionLabel = null!;
    private float fusionAlpha;
    private CanvasGroup stackPrompt = null!;
    private TextMeshProUGUI stackLabel = null!;
    private float stackAlpha;
    private CanvasGroup relicPrompt = null!;
    private TextMeshProUGUI relicLabel = null!;
    private Image relicGlow = null!;
    private float relicAlpha;
    private TextMeshProUGUI ropeMeter = null!;
    private float ropeAlpha;

    private class Medallion
    {
        public RectTransform Rect = null!;
        public Image Glow = null!;
        public float PulseAt = -10f;
    }

    private class Popup
    {
        public RectTransform Rect = null!;
        public CanvasGroup Group = null!;
        public float Start, Hold;
        public bool Big, Calm;
    }

    /// <summary>Prompt = the game's fog prompt; Blood = the same in red; Pick = a calm fade for perk picks.</summary>
    private enum BannerKind { Prompt, Blood, Pick }

    private readonly List<Popup> popups = new();
    private const float ToastHeight = 60f, ToastGap = 66f;
    private const int MaxToasts = 4;
    private readonly Queue<(string head, string title, Color color, BannerKind kind, string foot)> banners = new();
    private float bannerBusyUntil;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - HUD");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Hud>();
        RunState.Changed += () => instance?.RebuildTray();
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 850;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)transform;

        tray = Ui.Rect(root, "Relics");
        tray.anchorMin = tray.anchorMax = tray.pivot = new Vector2(0f, 1f);
        tray.anchoredPosition = new Vector2(34, -30);
        tray.sizeDelta = new Vector2(400, 110);

        // "Fusion ready" pill, under the relic tray.
        var pill = Ui.Rect(root, "FusionReady");
        pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0f, 1f);
        pill.anchoredPosition = new Vector2(30, -128);
        pill.sizeDelta = new Vector2(250, 40);
        fusionPrompt = pill.gameObject.AddComponent<CanvasGroup>();
        fusionPrompt.alpha = 0f;
        var bg = Ui.Image(pill, "Glow", UiArt.Glow(96, 40, 18, 18), new Color(0.45f, 0.25f, 0.75f, 0.75f));
        bg.rectTransform.sizeDelta = new Vector2(290, 70);
        bg.rectTransform.anchoredPosition = new Vector2(125, -20);
        fusionLabel = Ui.Text(pill, "Label", "", UiArt.BodyFont, 19, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        Ui.Stretch(fusionLabel.rectTransform);
        fusionLabel.characterSpacing = 3;

        // "Stack ready" pill (gold), under the fusion one.
        var stackPill = Ui.Rect(root, "StackReady");
        stackPill.anchorMin = stackPill.anchorMax = stackPill.pivot = new Vector2(0f, 1f);
        stackPill.anchoredPosition = new Vector2(30, -172);
        stackPill.sizeDelta = new Vector2(250, 40);
        stackPrompt = stackPill.gameObject.AddComponent<CanvasGroup>();
        stackPrompt.alpha = 0f;
        var stackBg = Ui.Image(stackPill, "Glow", UiArt.Glow(96, 40, 18, 18), new Color(0.75f, 0.55f, 0.15f, 0.75f));
        stackBg.rectTransform.sizeDelta = new Vector2(290, 70);
        stackBg.rectTransform.anchoredPosition = new Vector2(125, -20);
        stackLabel = Ui.Text(stackPill, "Label", "", UiArt.BodyFont, 19, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        Ui.Stretch(stackLabel.rectTransform);
        stackLabel.characterSpacing = 3;

        // "Relic waiting" pill (the waiting relic's colour), under the others.
        var relicPill = Ui.Rect(root, "RelicWaiting");
        relicPill.anchorMin = relicPill.anchorMax = relicPill.pivot = new Vector2(0f, 1f);
        relicPill.anchoredPosition = new Vector2(30, -216);
        relicPill.sizeDelta = new Vector2(250, 40);
        relicPrompt = relicPill.gameObject.AddComponent<CanvasGroup>();
        relicPrompt.alpha = 0f;
        relicGlow = Ui.Image(relicPill, "Glow", UiArt.Glow(96, 40, 18, 18), new Color(0.75f, 0.55f, 0.15f, 0.75f));
        relicGlow.rectTransform.sizeDelta = new Vector2(290, 70);
        relicGlow.rectTransform.anchoredPosition = new Vector2(125, -20);
        relicLabel = Ui.Text(relicPill, "Label", "", UiArt.BodyFont, 19, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        Ui.Stretch(relicLabel.rectTransform);
        relicLabel.characterSpacing = 3;

        // Rope readout above the item bar while a spool is in hand.
        ropeMeter = Ui.Text(root, "RopeMeter", "", UiArt.BodyFont, 22, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        ropeMeter.rectTransform.anchorMin = ropeMeter.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        ropeMeter.rectTransform.anchoredPosition = new Vector2(0, 205);
        ropeMeter.rectTransform.sizeDelta = new Vector2(400, 30);
        ropeMeter.characterSpacing = 3;
        ropeMeter.alpha = 0f;

        BuildTip();
    }

    private void UpdateRopeMeter()
    {
        Character me = Character.localCharacter;
        var spool = me != null && me.data.currentItem != null ? me.data.currentItem.GetComponent<RopeSpool>() : null;
        bool show = spool != null && spool.rope != null && spool.photonView.IsMine && !ChoiceScreen.IsOpen;
        ropeAlpha = Mathf.MoveTowards(ropeAlpha, show ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        ropeMeter.alpha = ropeAlpha;
        if (!show) return;
        float sp = TreePatches.RopeSpacing(spool!);
        float outM = spool!.rope.Segments * sp, maxM = spool.ropeFuel * sp;
        ropeMeter.text = $"ROPE  <color=#FFFFFF>{outM:0.0}m</color> / {maxM:0.0}m";
    }

    // --- relic tray ------------------------------------------------------------------------

    private void RebuildTray()
    {
        if (root == null) return;
        foreach (Transform child in tray) Destroy(child.gameObject);
        medallions.Clear();
        tips.Clear();
        int i = 0;
        foreach (var kv in RunState.Relics.OrderBy(k => k.Key))
        {
            var def = Relics.Get(kv.Key, kv.Value);
            bool cursedCopy = RunState.IsCursed(kv.Key);
            int stack = RunState.Stacks(kv.Key);
            var variants = RunState.Variants(kv.Key);
            bool complete = RunState.IsComplete(kv.Key);
            // A cursed copy is a darker, bruised version of the gem.
            Color c = cursedCopy ? Color.Lerp(Relics.GemColor(kv.Key), new Color(0.25f, 0.05f, 0.3f), 0.55f) : Relics.GemColor(kv.Key);
            var m = new Medallion();
            m.Rect = TraySlot(def.Name, i);
            // Only shows when the relic fires (Pulse).
            m.Glow = Ui.Image(m.Rect, "Glow", UiArt.Radial(128, 0.2f, 1.6f), new Color(c.r, c.g, c.b, 0.0f));
            m.Glow.rectTransform.sizeDelta = new Vector2(150, 150);
            // A complete colour wears a gold ring.
            if (complete)
            {
                var ring = Ui.Image(m.Rect, "Complete", UiArt.Ring(128, 0.78f), Trees.Gold);
                ring.rectTransform.sizeDelta = new Vector2(84, 84);
                ring.raycastTarget = false;
            }
            // The real amulet's icon; the mockup gem only if the amulet can't be found.
            if (Items.Icon(Relics.Amulet(kv.Key)) is { } icon)
            {
                var gem = TrayPicture(m.Rect, icon);
                if (cursedCopy) gem.color = new Color(0.55f, 0.4f, 0.6f);
            }
            else
            {
                var gem = Ui.Image(m.Rect, "Gem", UiArt.Embedded($"gem_{kv.Key.ToString().ToLowerInvariant()}"), Color.white);
                gem.rectTransform.sizeDelta = new Vector2(72, 72);
            }
            TrayName(m.Rect, def.Name.ToUpperInvariant() + (variants.Count > 1 ? $"  +{variants.Count - 1}" : "") + (stack > 0 ? $"  x{stack + 1}" : ""),
                complete ? Color.Lerp(Trees.Gold, Color.white, 0.3f) : Color.Lerp(c, Color.white, 0.55f));
            medallions[kv.Key] = m;
            // The hover card says what its relic cards said - a reminder, never more.
            var foot = new List<string> { $"Fires on: {string.Join(", ", variants.Select(t => t.Label()))}" };
            if (stack > 0) foot.Add($"x{stack + 1}: recharges faster");
            if (cursedCopy) foot.Add("Shatters if you die");
            tips.Add((m.Rect, new Tip
            {
                Title = def.Name,
                Kind = cursedCopy ? $"Cursed copy · {Relics.GemName(kv.Key)}" : complete ? $"{Relics.GemName(kv.Key)} · Complete" : Relics.GemName(kv.Key),
                Body = string.Join("\n", variants.Select(t => Relics.Get(kv.Key, t).Desc)),
                Foot = string.Join("  ·  ", foot), Color = complete ? Trees.Gold : c,
            }));
            i++;
        }

        // The starting relic: the curio's own picture (it never touches the moon).
        if (CurioRelics.TrayIcon() is { } curio)
        {
            var rect = TraySlot(CurioRelics.Name(CurioRelics.Mine), i);
            TrayPicture(rect, curio);
            var tint = new Color(0.88f, 0.9f, 0.94f);
            TrayName(rect, CurioRelics.Name(CurioRelics.Mine).ToUpperInvariant(), tint);
            tips.Add((rect, new Tip
            {
                Title = CurioRelics.Name(CurioRelics.Mine), Kind = "Starting relic",
                Body = CurioRelics.Desc(CurioRelics.Mine), Foot = "Lasts the whole run", Color = tint,
            }));
            i++;
        }

        // Badge relics: the game's badge picture, its name underneath.
        foreach (var id in Badges.Owned)
        {
            var def = Badges.Get(id);
            var rect = TraySlot(def.Name, i);
            if (Badges.Icon(id) is { } pic) TrayPicture(rect, pic);
            var tint = new Color(0.95f, 0.86f, 0.62f);
            TrayName(rect, def.Name.ToUpperInvariant(), tint);
            tips.Add((rect, new Tip { Title = def.Name, Kind = "Badge relic", Body = def.Desc, Foot = "Lasts the whole run", Color = tint }));
            i++;
        }
    }

    // --- hover details (pause menu) ----------------------------------------------------------

    private class Tip
    {
        public string Title = "", Kind = "", Body = "", Foot = "";
        public Color Color = Color.white;
    }

    private readonly List<(RectTransform rect, Tip tip)> tips = new();
    private RectTransform tipPanel = null!;
    private CanvasGroup tipGroup = null!;
    private Image tipGlow = null!, tipRule = null!;
    private TextMeshProUGUI tipKind = null!, tipTitle = null!, tipBody = null!, tipFoot = null!;
    private Tip? tipShown;
    private float tipAlpha;
    private const float TipWidth = 380f;

    /// <summary>The hover card: the relic card's look in small - a dark panel lit in the relic's colour.</summary>
    private void BuildTip()
    {
        tipPanel = Ui.Rect(root!, "RelicDetails");
        tipPanel.anchorMin = tipPanel.anchorMax = tipPanel.pivot = new Vector2(0f, 1f);
        tipPanel.sizeDelta = new Vector2(TipWidth, 170f);
        tipGroup = tipPanel.gameObject.AddComponent<CanvasGroup>();
        tipGroup.alpha = 0f;
        tipGroup.blocksRaycasts = false;
        tipGroup.interactable = false;

        // A soft glow in the relic's colour behind a dark ink panel.
        tipGlow = Ui.Image(tipPanel, "Glow", UiArt.Glow(128, 96, 28, 24), new Color(1f, 1f, 1f, 0.35f));
        Ui.Stretch(tipGlow.rectTransform);
        tipGlow.rectTransform.offsetMin = new Vector2(-26f, -26f);
        tipGlow.rectTransform.offsetMax = new Vector2(26f, 26f);
        var ink = ChoiceScreen.InkBrown;
        var panel = Ui.Image(tipPanel, "Panel", UiArt.Glow(128, 96, 6, 18), new Color(ink.r, ink.g, ink.b, 0.94f));
        Ui.Stretch(panel.rectTransform);

        tipKind = TipText("Kind", 13f, ChoiceScreen.Muted, -16f, 18f);
        tipKind.characterSpacing = 5f;
        tipKind.fontStyle = FontStyles.UpperCase;
        tipTitle = TipText("Title", 26f, ChoiceScreen.Cream, -36f, 32f);
        tipTitle.fontStyle = FontStyles.Bold;
        tipRule = Ui.Image(tipPanel, "Rule", UiArt.FadeLine(), new Color(1f, 1f, 1f, 0.5f));
        tipRule.rectTransform.anchorMin = tipRule.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        tipRule.rectTransform.sizeDelta = new Vector2(TipWidth - 60f, 2f);
        tipRule.rectTransform.anchoredPosition = new Vector2(0f, -74f);
        tipBody = TipText("Body", 18f, ChoiceScreen.Cream, -86f, 60f);
        tipBody.textWrappingMode = TextWrappingModes.Normal;
        tipBody.lineSpacing = -8f;
        tipFoot = TipText("Foot", 13f, ChoiceScreen.Muted, 0f, 20f);
        tipFoot.characterSpacing = 2f;
    }

    private TextMeshProUGUI TipText(string name, float size, Color color, float top, float height)
    {
        var t = Ui.Text(tipPanel, name, "", UiArt.BodyFont, size, color, TextAlignmentOptions.Center);
        t.rectTransform.anchorMin = new Vector2(0f, 1f);
        t.rectTransform.anchorMax = new Vector2(1f, 1f);
        t.rectTransform.pivot = new Vector2(0.5f, 1f);
        t.rectTransform.sizeDelta = new Vector2(-36f, height);
        t.rectTransform.anchoredPosition = new Vector2(0f, top);
        t.raycastTarget = false;
        return t;
    }

    /// <summary>While the pause menu or the end screen is up (the cursor is free), hovering a tray relic shows what it does.</summary>
    private void UpdateTip()
    {
        if (tipPanel == null) return;
        Tip? over = null;
        RectTransform? slot = null;
        if ((GUIManager.InPauseMenu || RunReportPage.Open) && UnityEngine.InputSystem.Mouse.current is { } mouse)
        {
            var pos = mouse.position.ReadValue();
            foreach (var (rect, tip) in tips)
                if (rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, pos, null)) { over = tip; slot = rect; break; }
        }
        if (over != null && over != tipShown) ShowTip(over, slot!);
        tipAlpha = Mathf.MoveTowards(tipAlpha, over != null ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        tipGroup.alpha = tipAlpha;
        if (tipAlpha <= 0f) tipShown = null;
    }

    private void ShowTip(Tip tip, RectTransform slot)
    {
        tipShown = tip;
        tipKind.text = tip.Kind;
        tipTitle.text = tip.Title;
        tipTitle.color = Color.Lerp(tip.Color, Color.white, 0.45f);
        tipBody.text = tip.Body;
        tipFoot.text = tip.Foot;
        tipGlow.color = new Color(tip.Color.r, tip.Color.g, tip.Color.b, 0.35f);
        tipRule.color = new Color(tip.Color.r, tip.Color.g, tip.Color.b, 0.55f);
        // Fit the body, then hang the panel just under the hovered relic.
        tipBody.ForceMeshUpdate();
        float body = Mathf.Max(24f, tipBody.preferredHeight);
        tipBody.rectTransform.sizeDelta = new Vector2(-36f, body);
        float height = 86f + body + 14f + 20f + 16f;
        tipPanel.sizeDelta = new Vector2(TipWidth, height);
        tipFoot.rectTransform.anchoredPosition = new Vector2(0f, -(86f + body + 14f));
        var corners = new Vector3[4];
        slot.GetWorldCorners(corners);
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root!, new Vector2(corners[0].x, corners[0].y), null, out local);
        // Root is centred: convert to its top-left (the panel's anchor).
        var size = root!.rect.size;
        float x = Mathf.Clamp(local.x + size.x * 0.5f - 20f, 16f, size.x - TipWidth - 16f);
        float y = local.y - size.y * 0.5f - 34f;
        tipPanel.anchoredPosition = new Vector2(x, y);
    }

    // A relic in the tray: just its picture, its name underneath.
    private RectTransform TraySlot(string name, int i)
    {
        var rect = Ui.Rect(tray, name);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(76, 76);
        rect.anchoredPosition = new Vector2(44 + (i % 10) * 96, -38 - (i / 10) * 100); // ten to a row
        return rect;
    }

    private static RawImage TrayPicture(RectTransform slot, Texture icon)
    {
        var pic = new GameObject("Picture", typeof(RectTransform)).AddComponent<RawImage>();
        pic.rectTransform.SetParent(slot, false);
        pic.rectTransform.sizeDelta = new Vector2(72, 72);
        pic.texture = icon;
        pic.raycastTarget = false;
        return pic;
    }

    private static void TrayName(RectTransform slot, string text, Color color)
    {
        var label = Ui.Text(slot, "Name", text, UiArt.BodyFont, 13, color, TextAlignmentOptions.Center);
        label.rectTransform.sizeDelta = new Vector2(96, 18);
        label.rectTransform.anchoredPosition = new Vector2(0, -46);
        label.enableAutoSizing = true;
        label.fontSizeMin = 9;
        label.fontSizeMax = 13;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.characterSpacing = 2;
        label.fontStyle = FontStyles.Bold;
    }

    public static void Pulse(RelicGem gem)
    {
        if (instance != null && instance.medallions.TryGetValue(gem, out var m)) m.PulseAt = Time.unscaledTime;
    }

    // --- toasts + banners -------------------------------------------------------------------

    // --- the game's own prompt look ("THE FOG RISES") ----------------------------------------------

    /// <summary>How long the game shows its "THE FOG RISES" prompt (GUIManager.TheFogRises).</summary>
    private const float PromptSeconds = 4f;
    private static bool loggedPrompt;

    private static TMP_Text? VanillaPrompt()
    {
        var gui = GUIManager.instance;
        return gui != null && gui.fogRises != null ? gui.fogRises.GetComponentInChildren<TMP_Text>(true) : null;
    }

    /// <summary>The "THE FOG RISES" prompt's font (null before the HUD exists).</summary>
    internal static TMP_FontAsset? PromptFont => VanillaPrompt()?.font;

    /// <summary>The fog prompt's font, material and colour on one of our texts (our own outline/shadow made them look murky).</summary>
    private static void VanillaLook(TextMeshProUGUI t)
    {
        var v = VanillaPrompt();
        if (v == null || v.font == null)
        {
            return;
        }
        t.font = v.font;
        t.fontSharedMaterial = v.fontSharedMaterial;
        t.fontStyle = v.fontStyle;
        t.characterSpacing = v.characterSpacing;
        Color c = v.color;
        t.color = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.5f ? Color.white : new Color(c.r, c.g, c.b, 1f);
    }

    public static void Toast(string text, Color color)
    {
        if (instance?.root == null) return;
        var rt = Ui.Rect(instance.root, "Toast");
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(1700, ToastHeight);
        var t = Ui.Text(rt, "Text", text.ToUpperInvariant(), UiArt.BodyFont, 40, Color.white, TextAlignmentOptions.Center);
        Ui.Stretch(t.rectTransform);
        VanillaLook(t);
        // Long lines shrink to fit one row rather than spill into the toast below.
        t.enableWordWrapping = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = 22;
        t.fontSizeMax = 40;
        var g = rt.gameObject.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        instance.popups.Add(new Popup { Rect = rt, Group = g, Start = Time.unscaledTime, Hold = 2.6f });
    }

    /// <summary>
    /// A copy of the game's "THE FOG RISES" prompt with our words: same font, look and animation.
    /// The title is the big line, the head a smaller line above it.
    /// </summary>
    private bool ShowVanillaBanner(string head, string title, Color? tint = null, string foot = "")
    {
        var src = GUIManager.instance != null ? GUIManager.instance.fogRises : null;
        if (src == null) return false;
        GameObject? clone = null;
        try
        {
            clone = Instantiate(src, src.transform.parent, false);
            clone.SetActive(false);
            clone.name = "PEAK Roguelike - Prompt";
            // Its localisation would put "THE FOG RISES" back.
            foreach (var lt in clone.GetComponentsInChildren<LocalizedText>(true)) DestroyImmediate(lt);
            var texts = clone.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length == 0) { Destroy(clone); return false; }
            string body = head.Length > 0
                ? $"<size=40%>{head.ToUpperInvariant()}</size>\n{title.ToUpperInvariant()}"
                : title.ToUpperInvariant();
            if (foot.Length > 0) body += $"\n<size=40%>{foot.ToUpperInvariant()}</size>";
            foreach (var t in texts)
            {
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
                t.text = body;
                // Tint this copy's own material: the prompt's animation may drive the text's
                // vertex colour, and the face colour multiplies with whatever it sets.
                if (tint is { } c) t.fontMaterial.SetColor(ShaderUtilities.ID_FaceColor, c);
            }
            if (!loggedPrompt)
            {
                loggedPrompt = true;
                Plugin.Log.LogInfo($"Prompts use the game's '{src.name}' ({texts.Length} text(s), font {texts[0].font?.name}, {texts[0].fontSize:0}pt).");
            }
            clone.transform.SetAsLastSibling();
            clone.SetActive(true);
            Destroy(clone, PromptSeconds);
            return true;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Couldn't copy the game's prompt, using ours: {e.Message}");
            if (clone != null) Destroy(clone);
            return false;
        }
    }

    /// <summary>Big centred announcement ("ROPE Â· LEVEL 2" / "Reinforced Line").</summary>
    /// <param name="foot">An optional smaller line under the title (a sun's hint).</param>
    public static void Banner(string head, string title, Color color, string foot = "")
    {
        instance?.banners.Enqueue((head, title, color, BannerKind.Prompt, foot));
    }

    /// <summary>A blood moon banner: the game's fog prompt, in blood red.</summary>
    public static void BloodBanner(string head, string title, Color color)
    {
        instance?.banners.Enqueue((head, title, color, BannerKind.Blood, ""));
    }

    /// <summary>A perk you just picked: PEAK lettering that fades in and out quietly (no spooky prompt).</summary>
    public static void PickBanner(string head, string title, Color color)
    {
        instance?.banners.Enqueue((head, title, PickTint ?? color, BannerKind.Pick, ""));
    }

    /// <summary>While set, pick banners use this colour instead (XP level-ups are gold).</summary>
    internal static Color? PickTint;

    private static bool boostLogged;

    /// <summary>The pal-jump boost whoosh, when the level-up cards open (only you hear it).</summary>
    public static void LevelUpSound()
    {
        var me = Character.localCharacter;
        if (me == null) return;
        try
        {
            var boost = me.refs.movement != null ? me.refs.movement.boostPlayer : null;
            if (boost == null || boost.Length == 0) { if (!boostLogged) Plugin.Log.LogInfo("Level-up sound: no boost sound on the character."); boostLogged = true; return; }
            foreach (var s in boost) if (s != null) s.Play(me.Center);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Level-up sound failed: {e.Message}"); }
    }

    private static SFX_Instance[]? chime;
    private static bool chimeLogged;

    /// <summary>The campfire's warm stamina chime, for the scout who picked (nobody else hears it).</summary>
    public static void PickChime()
    {
        var me = Character.localCharacter;
        if (me == null) return;
        try
        {
            if (chime == null || chime.Length == 0)
            {
                var fire = FindObjectsOfType<Campfire>(true).FirstOrDefault(c => c.moraleBoost != null && c.moraleBoost.Length > 0);
                if (fire != null) chime = fire.moraleBoost;
            }
            if (chime == null) { if (!chimeLogged) Plugin.Log.LogInfo("Pick chime: no campfire found yet."); chimeLogged = true; return; }
            foreach (var s in chime) if (s != null) s.Play(me.Center);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning($"Pick chime failed: {e.Message}"); }
    }

    private void ShowBanner(string head, string title, Color color, BannerKind kind, string foot = "")
    {
        bool blood = kind == BannerKind.Blood;
        // The game's own prompt, one after another (the blood moon's in red). Picks get the calm one below.
        if (kind != BannerKind.Pick && ShowVanillaBanner(head, title, blood ? BloodMoon.Blood : null, foot))
        {
            bannerBusyUntil = Time.unscaledTime + PromptSeconds + 0.2f;
            return;
        }
        var rt = Ui.Rect(root!, "Banner");
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -250);
        rt.sizeDelta = new Vector2(1000, 140);

        var h = Ui.Text(rt, "Head", head, UiArt.BodyFont, 32, Color.Lerp(color, Color.white, 0.3f), TextAlignmentOptions.Center);
        h.rectTransform.sizeDelta = new Vector2(1200, 40);
        h.rectTransform.anchoredPosition = new Vector2(0, 40);
        if (blood)
        {
            h.characterSpacing = 12;
        }
        else VanillaLook(h);
        if (blood)
        {
            // The blood moon's line lists every wave: keep it on one line.
            h.characterSpacing = 4;
            h.enableWordWrapping = false;
            h.enableAutoSizing = true;
            h.fontSizeMin = 16;
            h.fontSizeMax = 32;
        }

        var tt = Ui.Text(rt, "Title", title.ToUpperInvariant(), UiArt.TitleFont, blood ? 76 : 58, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        tt.rectTransform.sizeDelta = new Vector2(1000, blood ? 90 : 70);
        tt.rectTransform.anchoredPosition = new Vector2(0, blood ? -22 : -12);
        tt.characterSpacing = blood ? 8 : 3;
        VanillaLook(tt);
        if (blood) tt.color = BloodMoon.Blood;
        if (foot.Length > 0)
        {
            var ft = Ui.Text(rt, "Foot", foot, UiArt.BodyFont, 28, Color.Lerp(color, Color.white, 0.3f), TextAlignmentOptions.Center);
            ft.rectTransform.sizeDelta = new Vector2(1200, 40);
            ft.rectTransform.anchoredPosition = new Vector2(0, -68);
            VanillaLook(ft);
        }
        else if (kind == BannerKind.Pick) tt.color = Color.Lerp(color, Color.white, 0.25f);

        float half = Mathf.Max(140f, h.GetPreferredValues().x / 2f + 24f);
        foreach (int side in new[] { -1, 1 })
        {
            var line = Ui.Image(rt, "Rule", UiArt.FadeLine(), new Color(color.r, color.g, color.b, 0.8f));
            line.rectTransform.sizeDelta = new Vector2(200, 3);
            line.rectTransform.anchoredPosition = new Vector2(side * (half + 100), 40);
        }

        var g = rt.gameObject.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        // Banners share one spot: the next waits until this one has faded (sooner if more are queued).
        float hold = banners.Count > 0 ? 1.8f : 2.4f;
        popups.Add(new Popup { Rect = rt, Group = g, Start = Time.unscaledTime, Hold = hold, Big = true, Calm = kind == BannerKind.Pick });
        bannerBusyUntil = Time.unscaledTime + hold + 0.5f;
    }

    private void Update()
    {
        // Build once the game's UI (and its fonts) exist.
        if (root == null)
        {
            if (GUIManager.instance == null) return;
            Build();
            RebuildTray();
        }
        UpdateTip();
        float now = Time.unscaledTime;
        if (banners.Count > 0 && now >= bannerBusyUntil)
        {
            var (head, title, color, kind, foot) = banners.Dequeue();
            ShowBanner(head, title, color, kind, foot);
        }

        // Toasts stack downward under any banner.
        int toastIndex = 0;
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            float age = now - p.Start;
            float a = p.Calm
                ? Mathf.Clamp01(age / 0.5f) * (1f - Mathf.Clamp01((age - p.Hold) / 0.5f)) // slower, gentler fade in
                : Mathf.Clamp01(age / 0.2f) * (1f - Mathf.Clamp01((age - p.Hold) / 0.5f));
            p.Group.alpha = a;
            if (p.Big)
            {
                // Picks just fade; other banners pop in.
                float pop = p.Calm ? 1f : Ease.OutBack(Mathf.Clamp01(age / 0.35f));
                p.Rect.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, pop);
            }
            else
            {
                // Too many at once: the oldest start fading early so the column never runs on.
                if (toastIndex >= MaxToasts && age < p.Hold) p.Hold = age;
                float slide = (1f - Ease.OutQuad(Mathf.Clamp01(age / 0.25f))) * 16f;
                p.Rect.anchoredPosition = new Vector2(0, -370 - toastIndex * ToastGap - slide);
                toastIndex++;
            }
            if (age > p.Hold + 0.5f)
            {
                Destroy(p.Rect.gameObject);
                popups.RemoveAt(i);
            }
        }

        // Relic pulses.
        foreach (var m in medallions.Values)
        {
            float t = now - m.PulseAt;
            float pulse = t < 0.9f ? Mathf.Sin(Mathf.Clamp01(t / 0.9f) * Mathf.PI) : 0f;
            m.Rect.localScale = Vector3.one * (1f + 0.22f * pulse);
            var c = m.Glow.color;
            m.Glow.color = new Color(c.r, c.g, c.b, 0.15f + 0.75f * pulse);
        }

        UpdateRopeMeter();

        // Fusion prompt.
        bool show = Fusion.Ready && !ChoiceScreen.IsOpen;
        fusionAlpha = Mathf.MoveTowards(fusionAlpha, show ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        fusionPrompt.alpha = fusionAlpha * (0.75f + 0.25f * Mathf.Sin(now * 3f));
        if (show) fusionLabel.text = $"FUSION READY  <color=#E2CCFF>[{ModConfig.FusionKey.Value}]</color>";
        float trayHeight = medallions.Count > 0 ? 0f : 92f;
        fusionPrompt.GetComponent<RectTransform>().anchoredPosition = new Vector2(30, -128 + trayHeight);

        // Stack level-up prompt: below the fusion pill when both show.
        bool stack = StackMerge.Ready && !ChoiceScreen.IsOpen;
        stackAlpha = Mathf.MoveTowards(stackAlpha, stack ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        stackPrompt.alpha = stackAlpha * (0.75f + 0.25f * Mathf.Sin(now * 3f));
        if (stack) stackLabel.text = $"STACK READY  <color=#FFE3A0>[{ModConfig.StackKey.Value}]</color>";
        stackPrompt.GetComponent<RectTransform>().anchoredPosition = new Vector2(30, -128 + trayHeight - 44f * fusionAlpha);

        // Shared relic left for later: below the fusion and stack pills.
        // (Or the statue's dark gift, put off - same key, the relic goes first.)
        var waiting = Relics.Waiting;
        bool dark = waiting == null && Blessing.DarkWaiting;
        bool relic = (waiting != null || dark) && !ChoiceScreen.IsOpen;
        relicAlpha = Mathf.MoveTowards(relicAlpha, relic ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        relicPrompt.alpha = relicAlpha * (0.75f + 0.25f * Mathf.Sin(now * 3f));
        if (relic)
        {
            Color c = dark ? (Color)new Color32(0x9A, 0x6B, 0xC8, 0xFF) : Relics.GemColor(waiting!.Value);
            relicGlow.color = new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, 0.75f);
            string what = dark ? "GIFT WAITING" : Relics.WaitingUnread ? "RELIC UNREAD" : "RELIC WAITING";
            relicLabel.text = $"{what}  <color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(c, Color.white, 0.55f))}>[{ModConfig.RelicKey.Value}]</color>";
        }
        relicPrompt.GetComponent<RectTransform>().anchoredPosition = new Vector2(30, -128 + trayHeight - 44f * fusionAlpha - 44f * stackAlpha);
    }
}
