using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace PeakRoguelike;

/// <summary>Card art (embedded PNGs cut from the mockup), procedural glow sprites and the game's fonts.</summary>
internal static class UiArt
{
    private static readonly Dictionary<string, Sprite> cache = new();

    public static Sprite Embedded(string name)
    {
        if (cache.TryGetValue(name, out var s) && s) return s;
        using Stream? stream = typeof(UiArt).Assembly.GetManifestResourceStream($"PeakRoguelike.Assets.{name}.png");
        if (stream == null)
        {
            Plugin.Log.LogError($"Missing embedded art: {name}");
            return Glow(32, 32, 4); // visible placeholder rather than a crash
        }
        var bytes = new byte[stream.Length];
        stream.Read(bytes, 0, bytes.Length);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = name };
        tex.LoadImage(bytes); // stays readable so cards can use alpha hit-testing
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 4;
        s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        s.name = name;
        cache[name] = s;
        return s;
    }

    public static Sprite CardSprite(PerkTheme theme) => Embedded(theme switch
    {
        PerkTheme.Climber => "card_climber",
        PerkTheme.Mountaineer => "card_mountaineer",
        _ => "card_explorer",
    });

    /// <summary>Soft rounded-rectangle glow: opaque core fading out over `soft` px.</summary>
    public static Sprite Glow(int w, int h, int soft, float corner = 40f)
    {
        string key = $"glow{w}x{h}s{soft}c{corner}";
        if (cache.TryGetValue(key, out var s) && s) return s;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        float hx = w / 2f - soft, hy = h / 2f - soft;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // signed distance to a rounded box
                float qx = Mathf.Abs(x + 0.5f - w / 2f) - (hx - corner);
                float qy = Mathf.Abs(y + 0.5f - h / 2f) - (hy - corner);
                float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - corner;
                float a = 1f - Mathf.Clamp01(d / soft);
                a = a * a * (3f - 2f * a);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        cache[key] = s;
        return s;
    }

    /// <summary>Radial falloff, used for the screen vignette and icon backlight.</summary>
    public static Sprite Radial(int size, float inner, float power)
    {
        string key = $"radial{size}i{inner}p{power}";
        if (cache.TryGetValue(key, out var s) && s) return s;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                float a = 1f - Mathf.Clamp01((d - inner) / (1f - inner));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Pow(a, power) * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        cache[key] = s;
        return s;
    }

    /// <summary>An up-pointing chevron with a soft glow round it (the level-up sparkles' rising arrows).</summary>
    public static Sprite Chevron(int size)
    {
        string key = $"chevron{size}";
        if (cache.TryGetValue(key, out var s) && s) return s;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        Vector2 left = new(size * 0.2f, size * 0.32f), apex = new(size * 0.5f, size * 0.68f), right = new(size * 0.8f, size * 0.32f);
        float half = size * 0.065f, edge = 1.5f, glow = size * 0.2f;
        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(Seg(p, left, apex), Seg(p, apex, right));
                float core = Mathf.Clamp01((half - d) / edge + 0.5f);
                float halo = Mathf.Pow(1f - Mathf.Clamp01((d - half) / glow), 2f) * 0.35f;
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Max(core, halo) * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        cache[key] = s;
        return s;
    }

    /// <summary>Antialiased ring; `inner` is the hole radius as a fraction of the outer radius.</summary>
    public static Sprite Ring(int size, float inner)
    {
        string key = $"ring{size}i{inner}";
        if (cache.TryGetValue(key, out var s) && s) return s;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float r = size / 2f, edge = 1.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude;
                float a = Mathf.Clamp01((r - d) / edge) * Mathf.Clamp01((d - inner * r) / edge);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        cache[key] = s;
        return s;
    }

    /// <summary>Horizontal line that fades out at both ends.</summary>
    public static Sprite FadeLine()
    {
        const string key = "fadeline";
        if (cache.TryGetValue(key, out var s) && s) return s;
        const int w = 256, h = 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float t = Mathf.Sin(Mathf.PI * (x + 0.5f) / w);
                float v = y == 0 || y == h - 1 ? 0.5f : 1f;
                px[y * w + x] = new Color32(255, 255, 255, (byte)(t * v * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        cache[key] = s;
        return s;
    }

    /// <summary>Titles use the same PEAK lettering as everything else (one font across the mod).</summary>
    public static TMP_FontAsset? TitleFont => BodyFont;

    private static TMP_Text? prompt;

    /// <summary>The game's "THE FOG RISES" prompt text: the font and look every one of our texts borrows.</summary>
    private static TMP_Text? Prompt
    {
        get
        {
            if (prompt != null) return prompt;
            var gui = GUIManager.instance;
            if (gui == null || gui.fogRises == null) return null;
            prompt = gui.fogRises.GetComponentInChildren<TMP_Text>(true);
            return prompt;
        }
    }

    /// <summary>The prompt's material when `font` is the prompt's font (else the asset's default is fine).</summary>
    public static Material? PeakMaterial(TMP_FontAsset font)
    {
        var p = Prompt;
        return p != null && p.font == font && p.fontSharedMaterial != null ? p.fontSharedMaterial : null;
    }

    /// <summary>The PEAK font: the fog prompt's (else the interaction prompts', else TMP's default).</summary>
    public static TMP_FontAsset? BodyFont
    {
        get
        {
            var p = Prompt;
            if (p != null && p.font != null) return p.font;
            var gui = GUIManager.instance;
            if (gui != null && gui.interactNameText != null && gui.interactNameText.font != null) return gui.interactNameText.font;
            return TMP_Settings.defaultFontAsset;
        }
    }
}
