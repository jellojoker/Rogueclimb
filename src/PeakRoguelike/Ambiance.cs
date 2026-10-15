using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeakRoguelike;

/// <summary>
/// Blood moon mood outside the run (only for players with the mod): the main menu and the view
/// out of the airport windows get a blood moon sky (red sky + red moon; the scouts and the
/// buildings stay vanilla, the sunlight turns red), and the PEAK logo on the menu glows red.
/// The sky colours themselves are drawn by BloodMoon.Visuals when a day/night manager exists,
/// and from here when it doesn't.
/// </summary>
internal class Ambiance : MonoBehaviour
{
    private const string MenuScene = "Title", AirportScene = "Airport";

    internal static bool InAirport { get; private set; }
    internal static bool InMenu { get; private set; }

    /// <summary>How red the sky should be in this scene (0 = leave it alone).</summary>
    internal static float SkyStrength =>
        Mathf.Clamp01(InAirport ? ModConfig.AirportSky.Value : InMenu ? ModConfig.MenuSky.Value : 0f);

    /// <summary>How red the sunlight should be in this scene (0 = vanilla).</summary>
    internal static float LightStrength =>
        Mathf.Clamp01(InAirport ? ModConfig.AirportLight.Value : InMenu ? ModConfig.MenuLight.Value : 0f);

    internal static readonly Color SkyTop = new(0.30f, 0.02f, 0.03f);
    internal static readonly Color SkyMid = new(0.72f, 0.08f, 0.05f);
    internal static readonly Color SkyBottom = new(0.95f, 0.22f, 0.09f);

    private Image? tint, vignette;
    private Graphic? logo;
    private Image? glow;
    private Color logoColor;
    private bool logoTried;
    private float alpha, glowAlpha;
    private float nextPoll;
    private float flicker = 1f, nextFlicker;
    private Vector3? menuMoonDir;

    internal static void Install()
    {
        var go = new GameObject("PEAK Roguelike - Ambiance");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        var a = go.AddComponent<Ambiance>();
        SceneManager.sceneLoaded += a.OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        InAirport = scene.name == AirportScene;
        InMenu = scene.name == MenuScene;
        menuMoonDir = null;
        if (InAirport || InMenu) Invoke(nameof(RedSkybox), 1f);
    }

    private void Update()
    {
        if (Time.time >= nextPoll)
        {
            nextPoll = Time.time + 0.5f;
            string place = SceneManager.GetActiveScene().name;
            InAirport = place == AirportScene;
            InMenu = place == MenuScene;
            if (!InMenu) { logo = null; glow = null; logoTried = false; }
            if (InMenu && !logoTried) TryBuildGlow();
        }

        alpha = Mathf.MoveTowards(alpha, InMenu ? ModConfig.MenuFilter.Value : 0f, Time.deltaTime / 1.5f);
        EnsureOverlay();
        bool on = alpha > 0.001f;
        tint!.enabled = vignette!.enabled = on;
        tint.color = new Color(0.62f, 0.02f, 0.01f, alpha);
        vignette.color = new Color(0.45f, 0f, 0f, Mathf.Clamp01(alpha * 3.5f));

        if (logo != null) AnimateLogo();
    }

    /// <summary>No day/night manager in this scene (the menu): paint the sky ourselves.</summary>
    private void LateUpdate()
    {
        float k = SkyStrength;
        if (k <= 0f || DayNightManager.instance != null) return;
        Shader.SetGlobalColor("SkyTopColor", SkyTop);
        Shader.SetGlobalColor("SkyMidColor", SkyMid);
        Shader.SetGlobalColor("SkyBottomColor", SkyBottom);
        var cam = Meteors.Eye();
        if (cam == null) return;
        // The menu camera barely moves: hang the moon up and to the side of the view, once.
        menuMoonDir ??= (Quaternion.AngleAxis(-24f, Vector3.up) * Quaternion.AngleAxis(-14f, cam.transform.right) * cam.transform.forward).normalized;
        BloodMoon.Visuals.ShowDisc(menuMoonDir.Value, k);
    }

    // --- red filter (menu, off by default) ---------------------------------------------------------

    private void EnsureOverlay()
    {
        if (tint != null) return;
        var go = new GameObject("PEAK Roguelike - Ambiance Filter");
        DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -60; // under the menus
        tint = Full(go.transform, "Tint", null);
        vignette = Full(go.transform, "Vignette", Vignette());
    }

    private static Image Full(Transform parent, string name, Sprite? sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.enabled = false;
        return img;
    }

    private static Sprite Vignette()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d))));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    // --- skybox -------------------------------------------------------------------------------------

    /// <summary>
    /// If the scene draws its sky with a skybox material, push its colours towards blood red (on a
    /// copy, so the original asset is untouched). Logs what's there for tuning.
    /// </summary>
    private void RedSkybox()
    {
        float k = SkyStrength;
        var sky = RenderSettings.skybox;
        string scene = InMenu ? "Menu" : "Airport";
        if (sky == null || sky.shader == null)
        {
            Plugin.Log.LogInfo($"{scene} sky: day/night manager {(DayNightManager.instance != null ? "found" : "none")}, no skybox.");
            return;
        }
        var colors = Enumerable.Range(0, sky.shader.GetPropertyCount())
            .Where(i => sky.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
            .Select(i => sky.shader.GetPropertyName(i))
            .ToList();
        Plugin.Log.LogInfo($"{scene} sky: day/night manager {(DayNightManager.instance != null ? "found" : "none")}, skybox '{sky.name}' ({sky.shader.name}), colours: {string.Join(", ", colors)}");
        if (k <= 0f || colors.Count == 0) return;
        var copy = new Material(sky) { name = sky.name + " (blood moon)" };
        foreach (string p in colors)
        {
            var c = copy.GetColor(p);
            // Keep each colour's brightness, shift its hue to blood red.
            float v = Mathf.Max(c.maxColorComponent, 0.05f);
            var red = new Color(v, v * 0.12f, v * 0.08f, c.a);
            copy.SetColor(p, Color.Lerp(c, red, k));
        }
        RenderSettings.skybox = copy;
    }

    // --- menu logo glow ----------------------------------------------------------------------------

    /// <summary>
    /// The PEAK logo (MainMenu/Canvas/MainPage/Logo/Logo): tint it towards blood red and put a soft
    /// red halo - a blurred copy of the logo's own shape - behind it.
    /// </summary>
    private void TryBuildGlow()
    {
        var scene = SceneManager.GetActiveScene();
        var graphics = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Graphic>(false))
            .Where(g => g.isActiveAndEnabled && g.canvas != null)
            .ToList();
        if (graphics.Count == 0) return; // menu not built yet - try again next poll
        logoTried = true;

        // The biggest UI image named (or using a sprite named) "logo"; its drop shadow only if that's all there is.
        logo = graphics.Where(g => (g is Image or RawImage) && Named(g, "logo"))
            .OrderBy(g => Named(g, "shadow") || Named(g, "black") ? 1 : 0)
            .ThenByDescending(g => g.rectTransform.rect.width * g.rectTransform.rect.height)
            .FirstOrDefault();
        if (logo == null)
        {
            Plugin.Log.LogInfo("Menu: no PEAK logo found - no logo glow.");
            return;
        }
        logoColor = logo.color;
        glowAlpha = 0f;

        // The halo came out as a solid orange box (the logo texture reads back without alpha) - it
        // stays off unless MenuLogoGlow is turned on; the logo itself is only tinted.
        var halo = ModConfig.MenuLogoGlow.Value > 0f ? BlurredShape(logo) : null;
        if (halo != null)
        {
            var go = new GameObject("PEAK Roguelike - Logo Glow", typeof(RectTransform));
            go.transform.SetParent(logo.transform.parent, false);
            go.transform.SetSiblingIndex(logo.transform.GetSiblingIndex()); // drawn just behind the logo
            var rt = (RectTransform)go.transform;
            var src = logo.rectTransform;
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.anchoredPosition = src.anchoredPosition;
            rt.sizeDelta = src.sizeDelta;
            rt.localRotation = src.localRotation;
            rt.localScale = src.localScale * 1.04f;
            glow = go.AddComponent<Image>();
            glow.sprite = halo;
            glow.preserveAspect = logo is Image { preserveAspect: true };
            glow.raycastTarget = false;
            glow.color = Color.clear;
        }
        Plugin.Log.LogInfo($"Menu: red glow on '{logo.name}' (halo {(halo != null ? "built" : "unavailable - tint only")}).");
    }

    private static bool Named(Graphic g, string word)
    {
        bool Has(string? s) => s != null && s.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0;
        return Has(g.name) || (g is Image i && i.sprite != null && Has(i.sprite.name))
                           || (g is RawImage r && r.texture != null && Has(r.texture.name));
    }

    /// <summary>
    /// A white, blurred, slightly fattened copy of the image's alpha (tinted red by the Image colour).
    /// The game's textures aren't CPU-readable, so it's copied through a render texture first.
    /// </summary>
    private static Sprite? BlurredShape(Graphic g)
    {
        Texture? tex;
        Rect src;
        if (g is Image { sprite: { } s }) { tex = s.texture; src = s.textureRect; }
        else if (g is RawImage { texture: { } t } raw)
        {
            tex = t;
            src = new Rect(raw.uvRect.x * t.width, raw.uvRect.y * t.height, raw.uvRect.width * t.width, raw.uvRect.height * t.height);
        }
        else return null;
        if (tex == null) return null;
        try
        {
            // Small working copy: blur is cheap and soft at this size.
            float scale = Mathf.Min(1f, 256f / Mathf.Max(src.width, src.height));
            int w = Mathf.Max(8, Mathf.RoundToInt(src.width * scale)), h = Mathf.Max(8, Mathf.RoundToInt(src.height * scale));
            var rtex = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var uvScale = new Vector2(src.width / tex.width, src.height / tex.height);
            var uvOffset = new Vector2(src.x / tex.width, src.y / tex.height);
            Graphics.Blit(tex, rtex, uvScale, uvOffset);
            var prev = RenderTexture.active;
            RenderTexture.active = rtex;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rtex);
            var px = read.GetPixels32();
            Object.Destroy(read);

            var a = new float[w * h];
            for (int i = 0; i < a.Length; i++) a[i] = px[i].a / 255f;
            int r = Mathf.Max(2, w / 40);
            for (int pass = 0; pass < 3; pass++) a = BoxBlur(a, w, h, r);

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var outPx = new Color32[w * h];
            for (int i = 0; i < a.Length; i++)
                outPx[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a[i] * 2.2f) * 255));
            outTex.SetPixels32(outPx);
            outTex.Apply();
            return Sprite.Create(outTex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Couldn't build the logo glow: {e.Message}");
            return null;
        }
    }

    private static float[] BoxBlur(float[] src, int w, int h, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        float norm = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += src[y * w + Mathf.Clamp(x + k, 0, w - 1)];
                tmp[y * w + x] = sum * norm;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                dst[y * w + x] = sum * norm;
            }
        return dst;
    }

    /// <summary>Fades in, then a slow ember pulse with the odd flicker.</summary>
    private void AnimateLogo()
    {
        glowAlpha = Mathf.MoveTowards(glowAlpha, 1f, Time.deltaTime / 1.5f);
        if (Time.time >= nextFlicker)
        {
            flicker = Random.value < 0.2f ? Random.Range(0.55f, 0.8f) : 1f;
            nextFlicker = Time.time + (flicker < 1f ? Random.Range(0.05f, 0.12f) : Random.Range(0.8f, 3.5f));
        }
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.6f);

        var red = new Color(1f, 0.32f, 0.26f, logoColor.a);
        logo!.color = Color.Lerp(logoColor, red * logoColor, Mathf.Clamp01(ModConfig.MenuLogoTint.Value) * glowAlpha);
        if (glow == null) return;
        var c = Color.Lerp(new Color(0.65f, 0.02f, 0.01f), new Color(1f, 0.12f, 0.04f), pulse);
        c.a = glowAlpha * flicker * Mathf.Clamp01(ModConfig.MenuLogoGlow.Value) * Mathf.Lerp(0.7f, 1f, pulse);
        glow.color = c;
    }
}
