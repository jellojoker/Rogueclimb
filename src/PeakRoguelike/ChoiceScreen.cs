using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PeakRoguelike;

public enum CardStyle { Perk, Relic, Fusion }

/// <summary>A slow uneasy pulse with the odd stutter, like a dying candle.</summary>
internal class Flicker : MonoBehaviour
{
    private TMP_Text text = null!;
    private void Awake() => text = GetComponent<TMP_Text>();

    private void Update()
    {
        float t = Time.unscaledTime;
        float a = 0.78f + 0.17f * Mathf.Sin(t * 2.1f) + 0.05f * Mathf.Sin(t * 7.3f);
        if (Mathf.PerlinNoise(t * 3f, 0.37f) > 0.82f) a *= 0.45f;
        text.alpha = a;
    }
}

/// <summary>What one card shows. Which fields are used depends on the style.</summary>
public class CardSpec
{
    public CardStyle Style;
    public PerkTheme Theme;          // Perk
    public RelicGem Gem;             // Relic
    public string Title = "";
    public string Tag = "";          // Perk: tier tab. Relic: trigger banner. Fusion: unused.
    public Color TagColor = Color.white;
    public string Heading = "";      // Relic / Fusion: first line of the text panel
    public string Body = "";
    public string Footer = "";       // Perk only
    public Texture? Icon;            // Perk: medallion. Fusion: the result.
    public Texture?[] Ingredients = new Texture?[0]; // Fusion
}

/// <summary>A full-screen "pick one card" screen: perks, paths, fusions and relics all use it.</summary>
public class ScreenSpec
{
    public string Title = "CHOOSE YOUR PERK";
    public string Subtitle = "";
    /// <summary>A red line under the header, above the cards (the relic screen's omen).</summary>
    public string Warning = "";
    public string Confirm = "CLAIM";
    public string BackHint = "Back";
    /// <summary>Look but don't take: the confirm button stays greyed out (e.g. a pack with no room for it).</summary>
    public bool Locked;
    public List<CardSpec> Cards = new();
    /// <summary>Checked every frame; returning false closes the screen (OnCancelled, else OnBack).</summary>
    public Func<bool>? StillValid;
    public Action<int>? OnConfirm;
    public Action? OnBack;
    /// <summary>The world closed the screen (item dropped, knocked out...) - not the player's choice.</summary>
    public Action? OnCancelled;
}

/// <summary>
/// Full-screen choice UI. Layout coordinates for the perk cards are in mockup pixels
/// (Perk Choices mock ups.jpg, 1365x768) so the art and the text slots line up with it.
/// </summary>
public class ChoiceScreen : MonoBehaviour
{
    public static bool IsOpen => instance != null;
    private static ChoiceScreen? instance;

    // --- palette -----------------------------------------------------------------------
    internal static readonly Color Cream = new Color32(0xFB, 0xEB, 0xC4, 0xFF);
    internal static readonly Color InkBrown = new Color32(0x24, 0x10, 0x0C, 0xFF);
    internal static readonly Color Muted = new Color32(0xD9, 0xC9, 0xA3, 0xFF);
    internal static readonly Color FusionGlow = new Color32(0xC9, 0x9C, 0xFF, 0xFF);

    internal static Color ThemeGlow(PerkTheme t) => t switch
    {
        PerkTheme.Climber => new Color32(0xF2, 0xD2, 0x8A, 0xFF),     // warm lantern gold
        PerkTheme.Mountaineer => new Color32(0x96, 0xE6, 0xFF, 0xFF), // glacier blue
        _ => new Color32(0xFF, 0xB3, 0x6B, 0xFF),                      // sunset orange
    };

    // --- state ---------------------------------------------------------------------------
    private ScreenSpec spec = null!;
    private readonly List<CardView> cards = new();
    private CardView? selected;
    private CanvasGroup rootGroup = null!;
    private MenuButtonView confirmButton = null!;
    private float openTime;
    private bool closing;
    private float closeTime;
    private bool claimOnClose;
    private bool cancelled;

    public static bool Show(ScreenSpec spec)
    {
        if (instance != null || spec.Cards.Count == 0) return false;
        var go = new GameObject($"PEAK Roguelike - {spec.Title}");
        instance = go.AddComponent<ChoiceScreen>();
        instance.Build(spec);
        return true;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // ======================================================================================
    // Construction
    // ======================================================================================

    private void Build(ScreenSpec s)
    {
        spec = s;
        openTime = Time.unscaledTime;
        EnsureEventSystem();

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        rootGroup = gameObject.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;

        var root = (RectTransform)transform;

        // Backdrop: dark wash + vignette so the cards pop against any scene.
        var backdrop = Ui.Image(root, "Backdrop", null, new Color(0.03f, 0.04f, 0.06f, 0.62f));
        Ui.Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true; // swallow clicks on empty space
        var vignette = Ui.Image(root, "Vignette", InvertedRadial(), new Color(0, 0, 0, 0.75f));
        Ui.Stretch(vignette.rectTransform);
        vignette.raycastTarget = false;

        BuildHeader(root);

        // Card rows: up to 5 cards a row, more start a new row underneath (shrunk to fit).
        var row = Ui.Rect(root, "Cards");
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.anchoredPosition = new Vector2(0, 10);
        int n = s.Cards.Count;
        const int perRow = 5;
        float spacing = 430f, rowHeight = 560f;
        int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)perRow));
        float width = Mathf.Min(n, perRow) * spacing;
        float scale = Mathf.Min(1f, 1760f / width);
        if (rows > 1) scale = Mathf.Min(scale, 720f / (rows * rowHeight));
        row.localScale = Vector3.one * scale;
        for (int i = 0; i < n; i++)
        {
            int r = i / perRow, inRow = Mathf.Min(perRow, n - r * perRow);
            var card = CardView.Create(row, s.Cards[i], i, this);
            card.Home = new Vector2((i % perRow - (inRow - 1) / 2f) * spacing, ((rows - 1) / 2f - r) * rowHeight);
            cards.Add(card);
        }

        BuildButtons(root, n);
        if (n == 1) Select(cards[0]);
    }

    private void BuildHeader(RectTransform root)
    {
        var header = Ui.Rect(root, "Header");
        header.anchorMin = header.anchorMax = new Vector2(0.5f, 1f);
        header.anchoredPosition = new Vector2(0, -95);
        header.sizeDelta = new Vector2(1200, 150);

        var title = Ui.Text(header, "Title", spec.Title, UiArt.TitleFont, 66, Cream, TextAlignmentOptions.Center);
        title.rectTransform.anchoredPosition = new Vector2(0, 18);
        title.rectTransform.sizeDelta = new Vector2(1400, 80);
        title.characterSpacing = 4;

        if (!string.IsNullOrEmpty(spec.Warning))
        {
            // The fog prompt's lettering, in blood red.
            var warn = Ui.Text(header, "Warning", spec.Warning, Hud.PromptFont ?? UiArt.BodyFont, 30, BloodMoon.Blood, TextAlignmentOptions.Center);
            warn.rectTransform.anchoredPosition = new Vector2(0, -84);
            warn.rectTransform.sizeDelta = new Vector2(1200, 40);
            warn.characterSpacing = 3;
            warn.gameObject.AddComponent<Flicker>();
        }

        if (string.IsNullOrEmpty(spec.Subtitle)) return;
        var sub = Ui.Text(header, "Subtitle", spec.Subtitle.ToUpperInvariant(), UiArt.BodyFont, 22, Muted, TextAlignmentOptions.Center);
        sub.rectTransform.anchoredPosition = new Vector2(0, -38);
        sub.rectTransform.sizeDelta = new Vector2(900, 32);
        sub.characterSpacing = 12;
        sub.alpha = 0.85f;

        float half = Mathf.Max(120f, sub.GetPreferredValues().x / 2f + 26f);
        foreach (int side in new[] { -1, 1 })
        {
            var line = Ui.Image(header, "Rule", UiArt.FadeLine(), new Color(Muted.r, Muted.g, Muted.b, 0.55f));
            line.rectTransform.sizeDelta = new Vector2(220, 3);
            line.rectTransform.anchoredPosition = new Vector2(side * (half + 110), -38);
            line.raycastTarget = false;
            var gem = Ui.Image(header, "Gem", null, new Color(Muted.r, Muted.g, Muted.b, 0.8f));
            gem.rectTransform.sizeDelta = new Vector2(9, 9);
            gem.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            gem.rectTransform.anchoredPosition = new Vector2(side * half, -38);
            gem.raycastTarget = false;
        }
    }

    private void BuildButtons(RectTransform root, int cardCount)
    {
        var bar = Ui.Rect(root, "Buttons");
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0, 112);

        var back = MenuButtonView.Create(bar, "Back", UiArt.Embedded("button_back"), 1.35f, null, Back);
        back.Rect.anchoredPosition = new Vector2(-190, 0);

        confirmButton = MenuButtonView.Create(bar, "Confirm", UiArt.Embedded("button_confirm"), 1.35f, spec.Confirm, Confirm);
        confirmButton.Rect.anchoredPosition = new Vector2(150, 0);
        confirmButton.Interactable = false;

        string keys = cardCount >= 10 ? "1-0" : cardCount > 1 ? $"1-{cardCount}" : "1";
        var hint = Ui.Text(root, "Hint",
            $"<color=#FBEBC4>{keys}</color>  Select        <color=#FBEBC4>ENTER</color>  {Title(spec.Confirm)}        <color=#FBEBC4>ESC</color>  {spec.BackHint}",
            UiArt.BodyFont, 17, Muted, TextAlignmentOptions.Center);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        hint.rectTransform.anchoredPosition = new Vector2(0, 40);
        hint.rectTransform.sizeDelta = new Vector2(900, 28);
        hint.alpha = 0.6f;
        hint.characterSpacing = 2;
    }

    private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

    private static Sprite? invertedRadial;
    private static Sprite InvertedRadial()
    {
        if (invertedRadial) return invertedRadial!;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                float a = Mathf.Clamp01((d - 0.45f) / 0.75f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        invertedRadial = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return invertedRadial;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem (PEAK Roguelike)");
        es.AddComponent<EventSystem>();
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    // ======================================================================================
    // Interaction
    // ======================================================================================

    internal void Select(CardView card)
    {
        if (closing) return;
        selected = selected == card && cards.Count > 1 ? null : card;
        foreach (var c in cards) c.SetSelected(c == selected, selected != null);
        confirmButton.Interactable = selected != null && !spec.Locked;
    }

    internal void Confirm()
    {
        if (closing || selected == null || spec.Locked) return;
        claimOnClose = true;
        BeginClose();
    }

    internal void Back()
    {
        if (closing) return;
        BeginClose();
    }

    private void BeginClose()
    {
        closing = true;
        closeTime = Time.unscaledTime;
        foreach (var c in cards) c.BeginExit(c == selected && claimOnClose);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        float t = Time.unscaledTime;

        if (closing)
        {
            float k = Mathf.Clamp01((t - closeTime) / 0.32f);
            rootGroup.alpha = 1f - Ease.InQuad(k);
            if (k >= 1f) Finish();
            return;
        }

        rootGroup.alpha = Mathf.Clamp01((t - openTime) / 0.18f);

        // Bail out if the world changed under us.
        Character me = Character.localCharacter;
        bool valid = me != null && !me.data.dead && !me.data.passedOut;
        try { valid &= spec.StillValid?.Invoke() ?? true; } catch { valid = false; }
        if (!valid)
        {
            cancelled = true;
            Back();
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;
        for (int i = 0; i < cards.Count && i < 10; i++)
        {
            // 1-9, then 0 for the tenth card.
            bool hit = i < 9
                ? kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame
                : kb[Key.Digit0].wasPressedThisFrame || kb[Key.Numpad0].wasPressedThisFrame;
            if (hit && selected != cards[i]) Select(cards[i]);
        }
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Confirm();
        if (kb.backspaceKey.wasPressedThisFrame) Back();
    }

    private void Finish()
    {
        // Destroy first so a callback can open the next screen (e.g. perk -> path).
        int pick = claimOnClose && selected != null ? cards.IndexOf(selected) : -1;
        instance = null;
        Destroy(gameObject);
        try
        {
            if (pick >= 0) spec.OnConfirm?.Invoke(pick);
            else if (cancelled && spec.OnCancelled != null) spec.OnCancelled();
            else spec.OnBack?.Invoke();
        }
        catch (Exception e) { Plugin.Log.LogError($"Choice callback failed: {e}"); }
    }

    // ======================================================================================
    // Game integration: free the mouse, freeze the scout, Esc closes us instead of pausing.
    // ======================================================================================

    [HarmonyPatch]
    private static class GuiPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(GUIManager), nameof(GUIManager.UpdateWindowStatus))]
        private static void BlockInput(GUIManager __instance)
        {
            if (!IsOpen && !DevPanel.Interactive && !BugReports.Typing) return;
            __instance.windowShowingCursor = true;
            __instance.windowBlockingInput = true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GUIManager), nameof(GUIManager.UpdatePaused))]
        private static bool EscapeCloses()
        {
            var ch = Character.localCharacter;
            if (!IsOpen)
            {
                // Esc closes the bug note box (it handles the key itself), not the pause menu.
                if (!BugReports.Typing) return true;
                if (ch != null) ch.input.pauseWasPressed = false;
                return false;
            }
            if (ch != null && ch.input.pauseWasPressed)
            {
                ch.input.pauseWasPressed = false;
                instance!.Back();
            }
            return false;
        }
    }
}

/// <summary>One card: art, icon, texts, hover/select/enter/exit animation.</summary>
public class CardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public CardSpec Spec = null!;
    public Vector2 Home;
    public RectTransform Rect = null!;

    private ChoiceScreen owner = null!;
    private int index;
    private CanvasGroup group = null!;
    private Image glow = null!, art = null!;
    private Image? iconLight;
    private RawImage? icon;
    private Vector2 iconHome;
    private readonly List<(Graphic g, Color c)> dimmable = new();
    private Color glowColor;

    private bool hovered, isSelected, otherSelected, exiting, exitAsClaim;
    private float appearAt, exitAt;
    private float scale = 0.9f, lift, glowAlpha, dim, iconBob;

    public static CardView Create(RectTransform parent, CardSpec spec, int index, ChoiceScreen owner)
    {
        var rt = Ui.Rect(parent, $"Card {index + 1} - {spec.Title}");
        rt.sizeDelta = new Vector2(350, 515);
        var view = rt.gameObject.AddComponent<CardView>();
        view.Rect = rt;
        view.Spec = spec;
        view.owner = owner;
        view.index = index;
        view.group = rt.gameObject.AddComponent<CanvasGroup>();
        view.group.alpha = 0f;
        view.appearAt = Time.unscaledTime + 0.08f + index * 0.09f;
        view.glowColor = spec.Style switch
        {
            CardStyle.Relic => Relics.GemColor(spec.Gem),
            CardStyle.Fusion => ChoiceScreen.FusionGlow,
            _ => ChoiceScreen.ThemeGlow(spec.Theme),
        };
        view.BuildFrame();
        switch (spec.Style)
        {
            case CardStyle.Relic: view.BuildRelic(); break;
            case CardStyle.Fusion: view.BuildFusion(); break;
            default: view.BuildPerk(); break;
        }
        view.BuildKey();
        return view;
    }

    private void BuildFrame()
    {
        // Drop shadow + glow behind the frame.
        var shadow = Ui.Image(Rect, "Shadow", UiArt.Glow(160, 220, 50, 30), new Color(0, 0, 0, 0.55f));
        shadow.rectTransform.sizeDelta = new Vector2(430, 600);
        shadow.rectTransform.anchoredPosition = new Vector2(6, -26);
        shadow.raycastTarget = false;

        glow = Ui.Image(Rect, "Glow", UiArt.Glow(160, 220, 56, 30), new Color(glowColor.r, glowColor.g, glowColor.b, 0f));
        glow.rectTransform.sizeDelta = new Vector2(470, 640);
        glow.raycastTarget = false;
    }

    private Image AddArt(Sprite sprite, Vector2 size, Vector2 pos)
    {
        art = Ui.Image(Rect, "Art", sprite, Color.white);
        art.rectTransform.sizeDelta = size;
        art.rectTransform.anchoredPosition = pos;
        art.raycastTarget = true;
        try { art.alphaHitTestMinimumThreshold = 0.1f; } catch { /* texture not readable - rect hit test is fine */ }
        dimmable.Add((art, art.color));
        return art;
    }

    private void AddIcon(Texture? tex, Vector2 pos, float size, float lightSize)
    {
        iconLight = Ui.Image(Rect, "IconLight", UiArt.Radial(128, 0f, 2.2f), new Color(glowColor.r, glowColor.g, glowColor.b, 0.32f));
        iconLight.rectTransform.sizeDelta = new Vector2(lightSize, lightSize);
        iconLight.rectTransform.anchoredPosition = pos;
        iconLight.raycastTarget = false;

        icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<RawImage>();
        icon.rectTransform.SetParent(Rect, false);
        icon.rectTransform.sizeDelta = new Vector2(size, size);
        icon.rectTransform.anchoredPosition = pos;
        icon.raycastTarget = false;
        iconHome = pos;
        if (tex != null) icon.texture = tex;
        else icon.color = Color.clear;
    }

    private TextMeshProUGUI AddText(string name, string text, TMP_FontAsset? font, float maxSize, Color color, Vector2 pos, Vector2 size, float minSize = 11)
    {
        var t = Ui.Text(Rect, name, text, font, maxSize, color, TextAlignmentOptions.Center);
        t.rectTransform.sizeDelta = size;
        t.rectTransform.anchoredPosition = pos;
        t.enableAutoSizing = true;
        t.fontSizeMin = minSize;
        t.fontSizeMax = maxSize;
        return t;
    }

    // --- perk (original three themes) ------------------------------------------------------

    // Per-theme placement of the cut-out PNG inside the mockup, and the card's x offset
    // (cards are 418px apart in the mockup; all slot coordinates below are for card 1).
    private static (Vector2 origin, float offset) Layout(PerkTheme t) => t switch
    {
        PerkTheme.Climber => (new Vector2(73, 56), 0f),
        PerkTheme.Mountaineer => (new Vector2(488, 36), 418f),
        _ => (new Vector2(902, 38), 846f),
    };

    private static Vector2 P(float mx, float my) => new(mx - 272f, -(my - 352f));
    private static Vector2 S(float x0, float y0, float x1, float y1) => new(x1 - x0, y1 - y0);
    private static Vector2 C(float x0, float y0, float x1, float y1) => P((x0 + x1) / 2f, (y0 + y1) / 2f);

    private void BuildPerk()
    {
        var (origin, offset) = Layout(Spec.Theme);
        Sprite sprite = UiArt.CardSprite(Spec.Theme);
        AddArt(sprite, new Vector2(sprite.rect.width, sprite.rect.height),
            P(origin.x - offset + sprite.rect.width / 2f, origin.y + sprite.rect.height / 2f));

        // Icon in the central medallion (circle centre 272,316 r69).
        AddIcon(Spec.Icon, P(272, 316), 112, 132);

        var title = AddText("Title", Spec.Title.ToUpperInvariant(), UiArt.TitleFont, 32, ChoiceScreen.Cream, C(162, 100, 383, 136), S(162, 100, 383, 136), 14);
        title.textWrappingMode = TextWrappingModes.NoWrap;

        var tier = AddText("Tier", Spec.Tag.ToUpperInvariant(), UiArt.BodyFont, 19, Spec.TagColor, C(208, 417, 336, 446), S(208, 417, 336, 446));
        tier.characterSpacing = 6;
        tier.fontStyle = FontStyles.Bold;

        var desc = AddText("Description", Spec.Body, UiArt.BodyFont, 20, new Color32(0xEE, 0xE4, 0xCF, 0xFF), C(160, 462, 384, 530), S(160, 462, 384, 530), 12);
        desc.lineSpacing = -6;
        desc.textWrappingMode = TextWrappingModes.Normal;

        var footer = AddText("Footer", Spec.Footer.ToUpperInvariant(), UiArt.BodyFont, 13, ChoiceScreen.Muted, C(160, 532, 384, 552), S(160, 532, 384, 552), 9);
        footer.characterSpacing = 4;
        footer.alpha = 0.6f;
    }

    // --- relic (four amulet colours) ------------------------------------------------------

    private struct RelicLayout { public string Sprite; public Rect Title, Panel, Banner; public Vector2 Gem; public float GemRadius; }

    // Card-local pixel rects (from the top-left of each cut-out PNG). Gem = the painted amulet,
    // which the real in-game amulet icon is laid over.
    private static RelicLayout RelicSlots(RelicGem gem) => gem switch
    {
        RelicGem.Generosity => new RelicLayout { Sprite = "relic_generosity", Title = R(35, 28, 283, 64), Panel = R(40, 357, 288, 468), Banner = R(90, 482, 230, 506), Gem = new(158, 185), GemRadius = 100 },
        RelicGem.Ambition => new RelicLayout { Sprite = "relic_ambition", Title = R(40, 28, 280, 64), Panel = R(32, 394, 288, 510), Banner = R(85, 526, 235, 550), Gem = new(160, 225), GemRadius = 108 },
        RelicGem.Tenacity => new RelicLayout { Sprite = "relic_tenacity", Title = R(40, 28, 280, 64), Panel = R(30, 353, 288, 453), Banner = R(95, 476, 225, 500), Gem = new(162, 195), GemRadius = 104 },
        _ => new RelicLayout { Sprite = "relic_initiative", Title = R(40, 28, 280, 64), Panel = R(32, 441, 288, 552), Banner = R(90, 570, 230, 594), Gem = new(163, 240), GemRadius = 116 },
    };

    private static Rect R(float x0, float y0, float x1, float y1) => UnityEngine.Rect.MinMaxRect(x0, y0, x1, y1);

    private void BuildRelic()
    {
        var slots = RelicSlots(Spec.Gem);
        Sprite sprite = UiArt.Embedded(slots.Sprite);
        float w = sprite.rect.width, h = sprite.rect.height;
        float s = 560f / h > 1.18f ? 1.18f : 560f / h; // keep every gem card about the same size
        Vector2 Local(float x, float y) => new((x - w / 2f) * s, -(y - h / 2f) * s);
        Vector2 Center(Rect r) => Local(r.center.x, r.center.y);
        Vector2 Size(Rect r) => r.size * s;

        AddArt(sprite, new Vector2(w, h) * s, Vector2.zero);
        Color gem = Relics.GemColor(Spec.Gem);
        Color ink = new Color32(0x2B, 0x1D, 0x14, 0xFF);

        // The real amulet replaces the painted one: a dark gem-tinted disc hides the painting.
        if (Spec.Icon != null)
        {
            Vector2 gemPos = Local(slots.Gem.x, slots.Gem.y);
            float gr = slots.GemRadius * s;
            var cover = Ui.Image(Rect, "GemCover", UiArt.Radial(128, 0.62f, 1.4f), Color.Lerp(new Color(0.04f, 0.03f, 0.03f), gem, 0.16f));
            cover.rectTransform.sizeDelta = Vector2.one * gr * 2.5f;
            cover.rectTransform.anchoredPosition = gemPos;
            cover.raycastTarget = false;
            dimmable.Add((cover, cover.color));
            AddIcon(Spec.Icon, gemPos, gr * 1.6f, gr * 2.4f);
        }

        var title = AddText("Title", Spec.Title.ToUpperInvariant(), UiArt.TitleFont, 30, ChoiceScreen.Cream, Center(slots.Title), Size(slots.Title) - new Vector2(16, 4), 12);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.characterSpacing = 2;

        // Text panel: heading, divider, body.
        Rect p = slots.Panel;
        float headH = 30f;
        var heading = AddText("Heading", Spec.Heading.ToUpperInvariant(), UiArt.TitleFont, 21, Color.Lerp(gem, ink, 0.55f),
            Local(p.center.x, p.yMin + 6 + headH / 2f), new Vector2(p.width - 20, headH) * s, 11);
        heading.textWrappingMode = TextWrappingModes.NoWrap;
        heading.characterSpacing = 3;

        var rule = Ui.Image(Rect, "Rule", UiArt.FadeLine(), new Color(ink.r, ink.g, ink.b, 0.35f));
        rule.rectTransform.sizeDelta = new Vector2((p.width - 50) * s, 2.5f);
        rule.rectTransform.anchoredPosition = Local(p.center.x, p.yMin + 8 + headH);
        rule.raycastTarget = false;
        dimmable.Add((rule, rule.color));

        float bodyTop = p.yMin + 14 + headH;
        var body = AddText("Body", Spec.Body, UiArt.BodyFont, 21, ink, Local(p.center.x, (bodyTop + p.yMax - 4) / 2f),
            new Vector2(p.width - 22, p.yMax - 4 - bodyTop) * s, 11);
        body.lineSpacing = -4;
        body.textWrappingMode = TextWrappingModes.Normal;

        var banner = AddText("Trigger", Spec.Tag.ToUpperInvariant(), UiArt.BodyFont, 19, Color.Lerp(gem, Color.white, 0.35f), Center(slots.Banner), Size(slots.Banner), 10);
        banner.characterSpacing = 6;
        banner.fontStyle = FontStyles.Bold;
    }

    // --- fusion (iridescent frame) --------------------------------------------------------

    private void BuildFusion()
    {
        Sprite sprite = UiArt.Embedded("card_fusion");
        float w = sprite.rect.width, h = sprite.rect.height, s = 0.8f;
        Vector2 Local(float x, float y) => new((x - w / 2f) * s, -(y - h / 2f) * s);

        AddArt(sprite, new Vector2(w, h) * s, Vector2.zero);

        var title = AddText("Title", Spec.Title.ToUpperInvariant(), UiArt.TitleFont, 30, new Color32(0xF4, 0xEC, 0xFF, 0xFF), Local(234.5f, 60), new Vector2(370, 34) * s, 12);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.characterSpacing = 3;

        // Ingredients orbit the swirl, the result sits in its eye.
        Vector2 eye = Local(236, 268);
        for (int i = 0; i < Spec.Ingredients.Length && i < 3; i++)
        {
            float side = i == 0 ? -1 : i == 1 ? 1 : 0;
            Vector2 pos = eye + new Vector2(side * 122f, i == 2 ? -120f : -6f) * s;
            var halo = Ui.Image(Rect, "IngredientHalo", UiArt.Radial(128, 0f, 1.6f), new Color(0.15f, 0.08f, 0.3f, 0.55f));
            halo.rectTransform.sizeDelta = new Vector2(104, 104) * s;
            halo.rectTransform.anchoredPosition = pos;
            halo.raycastTarget = false;
            var ing = new GameObject("Ingredient", typeof(RectTransform)).AddComponent<RawImage>();
            ing.rectTransform.SetParent(Rect, false);
            ing.rectTransform.sizeDelta = new Vector2(80, 80) * s;
            ing.rectTransform.anchoredPosition = pos;
            ing.raycastTarget = false;
            if (Spec.Ingredients[i] != null) ing.texture = Spec.Ingredients[i]; else ing.color = Color.clear;
            dimmable.Add((ing, ing.color));
        }
        AddIcon(Spec.Icon, eye, 128 * s, 190 * s);

        Color ink = new Color32(0x2A, 0x1C, 0x3D, 0xFF);
        var heading = AddText("Recipe", Spec.Heading, UiArt.BodyFont, 17, new Color32(0x5B, 0x3E, 0x8A, 0xFF), Local(236, 497), new Vector2(350, 26) * s, 10);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 2;
        heading.textWrappingMode = TextWrappingModes.NoWrap;

        var rule = Ui.Image(Rect, "Rule", UiArt.FadeLine(), new Color(ink.r, ink.g, ink.b, 0.3f));
        rule.rectTransform.sizeDelta = new Vector2(300 * s, 2.5f);
        rule.rectTransform.anchoredPosition = Local(236, 514);
        rule.raycastTarget = false;
        dimmable.Add((rule, rule.color));

        var body = AddText("Body", Spec.Body, UiArt.BodyFont, 22, ink, Local(236, 563), new Vector2(345, 88) * s, 11);
        body.lineSpacing = -4;
        body.textWrappingMode = TextWrappingModes.Normal;
    }

    private void BuildKey()
    {
        // Keyboard number under the card.
        var key = Ui.Text(Rect, "Key", (index + 1).ToString(), UiArt.TitleFont, 22, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        key.rectTransform.sizeDelta = new Vector2(40, 40);
        key.rectTransform.anchoredPosition = new Vector2(0, Spec.Style == CardStyle.Perk ? -288 : -305);
        key.alpha = 0.75f;
    }

    public void SetSelected(bool selected, bool anySelected)
    {
        isSelected = selected;
        otherSelected = anySelected && !selected;
    }

    public void BeginExit(bool asClaim)
    {
        exiting = true;
        exitAsClaim = asClaim;
        exitAt = Time.unscaledTime;
    }

    public void OnPointerEnter(PointerEventData e) => hovered = true;
    public void OnPointerExit(PointerEventData e) => hovered = false;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (e.clickCount >= 2 && isSelected) { owner.Confirm(); return; }
        owner.Select(this); // click again to deselect
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float k = 1f - Mathf.Exp(-14f * dt);

        // Entrance: rise from below with a little overshoot, staggered per card.
        float appear = Mathf.Clamp01((now - appearAt) / 0.45f);
        float rise = (1f - Ease.OutBack(appear)) * -110f;

        float targetScale = isSelected ? 1.07f : hovered ? 1.045f : otherSelected ? 0.95f : 1f;
        float targetLift = isSelected ? 22f : hovered ? 14f : 0f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(now * 3.2f);
        float targetGlow = isSelected ? 0.75f + 0.25f * pulse : hovered ? 0.5f : 0f;
        float targetDim = otherSelected && !hovered ? 0.42f : 0f;

        scale = Mathf.Lerp(scale, targetScale, k);
        lift = Mathf.Lerp(lift, targetLift, k);
        glowAlpha = Mathf.Lerp(glowAlpha, targetGlow, k);
        dim = Mathf.Lerp(dim, targetDim, k);
        iconBob = hovered || isSelected ? Mathf.Sin(now * 2.6f) * 4f : Mathf.Lerp(iconBob, 0f, k);

        float alpha = Ease.OutQuad(appear);
        float extraY = 0f, extraScale = 1f;
        if (exiting)
        {
            float x = Mathf.Clamp01((now - exitAt) / 0.32f);
            if (exitAsClaim) { extraScale = 1f + 0.12f * Ease.OutQuad(x); extraY = 30f * x; glowAlpha = 1f; }
            else { extraY = -140f * Ease.InQuad(x); alpha *= 1f - x; }
        }

        Rect.anchoredPosition = Home + new Vector2(0, rise + lift + extraY);
        Rect.localScale = Vector3.one * (scale * extraScale * Mathf.Lerp(0.92f, 1f, Ease.OutQuad(appear)));
        group.alpha = alpha;
        glow.color = new Color(glowColor.r, glowColor.g, glowColor.b, glowAlpha);
        float b = 1f - dim;
        foreach (var (g, c) in dimmable) if (g != null) g.color = new Color(c.r * b, c.g * b, c.b * b, c.a);
        if (icon != null)
        {
            icon.color = icon.texture != null ? new Color(b, b, b, 1f) : Color.clear;
            icon.rectTransform.anchoredPosition = iconHome + new Vector2(0, iconBob);
        }
        if (iconLight != null) iconLight.color = new Color(glowColor.r, glowColor.g, glowColor.b, (0.3f + 0.35f * glowAlpha) * b);
    }
}

/// <summary>Image button from the mockup art, with hover/press feedback.</summary>
public class MenuButtonView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public RectTransform Rect = null!;
    private Image image = null!;
    private TextMeshProUGUI? label;
    private Action onClick = null!;
    private Image glow = null!;
    private bool hovered, pressed, interactable = true;
    private float scale = 1f, glowA;

    public bool Interactable
    {
        get => interactable;
        set => interactable = value;
    }

    public static MenuButtonView Create(RectTransform parent, string name, Sprite sprite, float size, string? text, Action onClick)
    {
        var rt = Ui.Rect(parent, name);
        rt.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * size;
        var v = rt.gameObject.AddComponent<MenuButtonView>();
        v.Rect = rt;
        v.onClick = onClick;

        v.glow = Ui.Image(rt, "Glow", UiArt.Glow(96, 48, 20, 12), new Color(1f, 0.7f, 0.4f, 0f));
        v.glow.rectTransform.sizeDelta = rt.sizeDelta + new Vector2(60, 50);
        v.glow.raycastTarget = false;

        v.image = Ui.Image(rt, "Art", sprite, Color.white);
        Ui.Stretch(v.image.rectTransform);
        try { v.image.alphaHitTestMinimumThreshold = 0.1f; } catch { }

        if (text != null)
        {
            // The confirm art has the mountain on the left; label sits in the empty right half.
            v.label = Ui.Text(rt, "Label", text, UiArt.TitleFont, 30, new Color32(0x4A, 0x16, 0x14, 0xFF), TextAlignmentOptions.Center);
            v.label.rectTransform.anchorMin = new Vector2(0.38f, 0.12f);
            v.label.rectTransform.anchorMax = new Vector2(0.92f, 0.92f);
            v.label.rectTransform.offsetMin = v.label.rectTransform.offsetMax = Vector2.zero;
            v.label.fontStyle = FontStyles.Italic;
            v.label.characterSpacing = 3;
            v.label.enableAutoSizing = true;
            v.label.fontSizeMin = 14;
            v.label.fontSizeMax = 30;
            v.label.raycastTarget = false;
        }
        return v;
    }

    public void OnPointerEnter(PointerEventData e) => hovered = true;
    public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
    public void OnPointerDown(PointerEventData e) => pressed = true;
    public void OnPointerUp(PointerEventData e) => pressed = false;
    public void OnPointerClick(PointerEventData e)
    {
        if (interactable && e.button == PointerEventData.InputButton.Left) onClick();
    }

    private void Update()
    {
        float k = 1f - Mathf.Exp(-16f * Mathf.Min(Time.unscaledDeltaTime, 0.05f));
        bool live = interactable && hovered;
        scale = Mathf.Lerp(scale, pressed && interactable ? 0.96f : live ? 1.07f : 1f, k);
        glowA = Mathf.Lerp(glowA, live ? 0.45f : 0f, k);
        Rect.localScale = Vector3.one * scale;
        glow.color = new Color(1f, 0.72f, 0.45f, glowA);
        float b = interactable ? (live ? 1.08f : 1f) : 0.55f;
        image.color = new Color(Mathf.Min(b, 1f), Mathf.Min(b, 1f), Mathf.Min(b, 1f), interactable ? 1f : 0.6f);
        if (label != null) label.alpha = interactable ? 1f : 0.55f;
    }
}

internal static class Ease
{
    public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);
    public static float InQuad(float t) => t * t;
    public static float OutBack(float t)
    {
        const float c1 = 1.4f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
    }
}

/// <summary>Tiny uGUI construction helpers.</summary>
internal static class Ui
{
    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    public static Image Image(Transform parent, string name, Sprite? sprite, Color color)
    {
        var img = Rect(parent, name).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        return img;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset? font, float size, Color color, TextAlignmentOptions align)
    {
        var t = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            t.font = font;
            // The game's own look for its font (the fog prompt's material), not the asset's default.
            if (UiArt.PeakMaterial(font) is { } mat) t.fontSharedMaterial = mat;
        }
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.richText = true;
        return t;
    }

}
