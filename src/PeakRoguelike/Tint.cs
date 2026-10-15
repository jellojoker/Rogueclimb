using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakRoguelike;

/// <summary>
/// Recolours a spawned game object in place (its own material copies, particles, lights,
/// trails): blue ghosts become blood red, desert tornadoes become snow white.
/// Works on hue, so shading and glow strength are kept.
/// </summary>
internal static class Tint
{
    private static readonly HashSet<string> logged = new();

    public static Color ToRed(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        Color o = s < 0.08f
            ? new Color(v, v * 0.32f, v * 0.28f)      // white/grey parts: pale blood
            : Color.HSVToRGB(0.99f, Mathf.Max(s, 0.75f), v, hdr: true);
        o.a = c.a;
        return o;
    }

    public static Color ToSnow(Color c)
    {
        Color.RGBToHSV(c, out _, out float s, out float v);
        Color o = Color.HSVToRGB(0.58f, s * 0.08f, Mathf.Max(v, 0.88f), hdr: true);
        o.a = c.a;
        return o;
    }

    /// <summary>Fire: orange flame colours, brighter at the core.</summary>
    public static Color ToFire(Color c)
    {
        Color.RGBToHSV(c, out _, out float s, out float v);
        float hue = Mathf.Lerp(0.02f, 0.09f, v); // dark parts ember red, light parts orange
        Color o = Color.HSVToRGB(hue, Mathf.Max(s, 0.85f), Mathf.Max(v, 0.6f) * 1.15f, hdr: true);
        o.a = c.a;
        return o;
    }

    /// <summary>Gloom: deep violet.</summary>
    public static Color ToGloom(Color c)
    {
        Color.RGBToHSV(c, out _, out float s, out float v);
        Color o = Color.HSVToRGB(0.78f, Mathf.Max(s, 0.65f), Mathf.Max(v, 0.45f), hdr: true);
        o.a = c.a;
        return o;
    }

    public static void Red(GameObject root) => Apply(root, ToRed, "red");
    public static void Snow(GameObject root) => Apply(root, ToSnow, "snow");
    public static void Fire(GameObject root) => Apply(root, ToFire, "fire");
    public static void Gloom(GameObject root) => Apply(root, ToGloom, "gloom");

    public static void Apply(GameObject root, Func<Color, Color> f, string why)
    {
        if (root == null) return;
        try
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.materials; // per-object copies, the prefab stays untouched
                foreach (var m in mats) if (m != null) TintMaterial(m, f);
                r.materials = mats;
            }
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = Map(main.startColor, f);
                var col = ps.colorOverLifetime;
                if (col.enabled) col.color = Map(col.color, f);
                var speed = ps.colorBySpeed;
                if (speed.enabled) speed.color = Map(speed.color, f);
                var trails = ps.trails;
                if (trails.enabled) trails.colorOverLifetime = Map(trails.colorOverLifetime, f);
            }
            foreach (var l in root.GetComponentsInChildren<Light>(true)) l.color = f(l.color);
            foreach (var t in root.GetComponentsInChildren<TrailRenderer>(true)) t.colorGradient = Map(t.colorGradient, f);
            foreach (var l in root.GetComponentsInChildren<LineRenderer>(true)) l.colorGradient = Map(l.colorGradient, f);

            // First time we tint a kind of object, log what it's made of (to tune tints after playtests).
            string key = why + ":" + root.name.Replace("(Clone)", "");
            if (logged.Add(key))
                Plugin.Log.LogInfo($"Tint {key}: " + string.Join(", ", renderers.Select(r =>
                    $"{r.GetType().Name}[{string.Join("/", r.sharedMaterials.Where(m => m != null).Select(m => $"{m.name}<{m.shader.name}>"))}]")));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Couldn't tint {root.name}: {e.Message}");
        }
    }

    private static void TintMaterial(Material m, Func<Color, Color> f)
    {
        var sh = m.shader;
        int n = sh.GetPropertyCount();
        for (int i = 0; i < n; i++)
        {
            if (sh.GetPropertyType(i) != ShaderPropertyType.Color) continue;
            int id = sh.GetPropertyNameId(i);
            m.SetColor(id, f(m.GetColor(id)));
        }
    }

    private static ParticleSystem.MinMaxGradient Map(ParticleSystem.MinMaxGradient g, Func<Color, Color> f)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(f(g.color));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(f(g.colorMin), f(g.colorMax));
            case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Map(g.gradient, f));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Map(g.gradientMin, f), Map(g.gradientMax, f));
            case ParticleSystemGradientMode.RandomColor:
                var r = new ParticleSystem.MinMaxGradient(Map(g.gradient, f)) { mode = ParticleSystemGradientMode.RandomColor };
                return r;
            default: return g;
        }
    }

    private static Gradient Map(Gradient g, Func<Color, Color> f)
    {
        if (g == null) return g!;
        var o = new Gradient { mode = g.mode };
        o.SetKeys(g.colorKeys.Select(k => new GradientColorKey(f(k.color), k.time)).ToArray(), g.alphaKeys);
        return o;
    }
}
