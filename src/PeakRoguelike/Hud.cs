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
    private readonly Queue<(string head, string title, Color color, BannerKind kind)> banners = new();
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

        // Rope readout above the item bar while a spool is in hand.
        ropeMeter = Ui.Text(root, "RopeMeter", "", UiArt.BodyFont, 22, ChoiceScreen.Cream, TextAlignmentOptions.Center);
        ropeMeter.rectTransform.anchorMin = ropeMeter.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        ropeMeter.rectTransform.anchoredPosition = new Vector2(0, 205);
        ropeMeter.rectTransform.sizeDelta = new Vector2(400, 30);
        ropeMeter.characterSpacing = 3;
        ropeMeter.alpha = 0f;
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
        int i = 0;
        foreach (var kv in RunState.Relics.OrderBy(k => k.Key))
        {
            var def = Relics.Get(kv.Key, kv.Value);
            bool cursedCopy = RunState.IsCursed(kv.Key);
            int stack = RunState.Stacks(kv.Key);
            // A cursed copy is a darker, bruised version of the gem.
            Color c = cursedCopy ? Color.Lerp(Relics.GemColor(kv.Key), new Color(0.25f, 0.05f, 0.3f), 0.55f) : Relics.GemColor(kv.Key);
            var m = new Medallion();
            m.Rect = Ui.Rect(tray, def.Name);
            m.Rect.anchorMin = m.Rect.anchorMax = new Vector2(0f, 1f);
            m.Rect.sizeDelta = new Vector2(76, 76);
            m.Rect.anchoredPosition = new Vector2(38 + i * 90, -38);

            m.Glow = Ui.Image(m.Rect, "Glow", UiArt.Radial(128, 0.2f, 1.6f), new Color(c.r, c.g, c.b, 0.0f));
            m.Glow.rectTransform.sizeDelta = new Vector2(150, 150);
            var shadow = Ui.Image(m.Rect, "Shadow", UiArt.Radial(128, 0.55f, 1.2f), new Color(0, 0, 0, 0.6f));
            shadow.rectTransform.sizeDelta = new Vector2(100, 100);
            var ring = Ui.Image(m.Rect, "Ring", UiArt.Ring(128, 0.82f), Color.Lerp(c, Color.black, 0.45f));
            ring.rectTransform.sizeDelta = new Vector2(80, 80);
            // The real amulet's icon; the mockup gem only if the amulet can't be found.
            if (Items.Icon(Relics.Amulet(kv.Key)) is { } icon)
            {
                var gem = new GameObject("Gem", typeof(RectTransform)).AddComponent<RawImage>();
                gem.rectTransform.SetParent(m.Rect, false);
                gem.rectTransform.sizeDelta = new Vector2(64, 64);
                gem.texture = icon;
                gem.raycastTarget = false;
                if (cursedCopy) gem.color = new Color(0.55f, 0.4f, 0.6f);
            }
            else
            {
                var gem = Ui.Image(m.Rect, "Gem", UiArt.Embedded($"gem_{kv.Key.ToString().ToLowerInvariant()}"), Color.white);
                gem.rectTransform.sizeDelta = new Vector2(68, 68);
            }
            var rim = Ui.Image(m.Rect, "Rim", UiArt.Ring(128, 0.9f), Color.Lerp(c, Color.white, 0.35f));
            rim.rectTransform.sizeDelta = new Vector2(78, 78);

            string text = kv.Value.ToString().ToUpperInvariant() + (stack > 0 ? $"  x{stack + 1}" : "");
            var label = Ui.Text(m.Rect, "Trigger", text, UiArt.BodyFont, 12, Color.Lerp(c, Color.white, 0.5f), TextAlignmentOptions.Center);
            label.rectTransform.sizeDelta = new Vector2(90, 18);
            label.rectTransform.anchoredPosition = new Vector2(0, -48);
            label.characterSpacing = 4;
            label.fontStyle = FontStyles.Bold;

            medallions[kv.Key] = m;
            i++;
        }
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
    private bool ShowVanillaBanner(string head, string title, Color? tint = null)
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
    public static void Banner(string head, string title, Color color)
    {
        instance?.banners.Enqueue((head, title, color, BannerKind.Prompt));
    }

    /// <summary>A blood moon banner: the game's fog prompt, in blood red.</summary>
    public static void BloodBanner(string head, string title, Color color)
    {
        instance?.banners.Enqueue((head, title, color, BannerKind.Blood));
    }

    /// <summary>A perk you just picked: PEAK lettering that fades in and out quietly (no spooky prompt).</summary>
    public static void PickBanner(string head, string title, Color color)
    {
        instance?.banners.Enqueue((head, title, PickTint ?? color, BannerKind.Pick));
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

    private void ShowBanner(string head, string title, Color color, BannerKind kind)
    {
        bool blood = kind == BannerKind.Blood;
        // The game's own prompt, one after another (the blood moon's in red). Picks get the calm one below.
        if (kind != BannerKind.Pick && ShowVanillaBanner(head, title, blood ? BloodMoon.Blood : null))
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
        float now = Time.unscaledTime;
        if (banners.Count > 0 && now >= bannerBusyUntil)
        {
            var (head, title, color, kind) = banners.Dequeue();
            ShowBanner(head, title, color, kind);
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
    }
}
