using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Cutter
{
    static int W, H;
    static int[] px;      // ARGB
    static float[] alpha; // 0..1

    static int R(int c) { return (c >> 16) & 255; }
    static int G(int c) { return (c >> 8) & 255; }
    static int B(int c) { return c & 255; }

    // Checkerboard squares are neutral greys around 73 and 121.
    static bool IsChecker(int c)
    {
        int r = R(c), g = G(c), b = B(c);
        int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        return mx - mn <= 10 && mn >= 52 && mx <= 140;
    }

    public static void Load(string path)
    {
        using (var src = new Bitmap(path))
        {
            W = src.Width; H = src.Height;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, W, H);
                var d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                px = new int[W * H];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                bmp.UnlockBits(d);
            }
        }
        alpha = new float[W * H];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = 1f;
    }

    // Flood fill checker from the image border, plus enclosed checker pockets
    // that lie outside the given card frame rectangles (e.g. inside rope loops).
    public static void RemoveBackground(int[] frames)
    {
        var bg = new bool[W * H];
        var q = new Queue<int>();
        Action<int> seed = i => { if (!bg[i] && IsChecker(px[i])) { bg[i] = true; q.Enqueue(i); } };
        for (int x = 0; x < W; x++) { seed(x); seed((H - 1) * W + x); }
        for (int y = 0; y < H; y++) { seed(y * W); seed(y * W + W - 1); }
        Flood(bg, q);

        var seen = new bool[W * H];
        for (int i = 0; i < W * H; i++)
        {
            if (bg[i] || seen[i] || !IsChecker(px[i])) continue;
            var comp = new List<int>();
            var cq = new Queue<int>(); cq.Enqueue(i); seen[i] = true;
            bool insideFrame = false;
            while (cq.Count > 0)
            {
                int p = cq.Dequeue(); comp.Add(p);
                int x = p % W, y = p / W;
                for (int f = 0; f < frames.Length; f += 4)
                    if (x >= frames[f] && x <= frames[f + 2] && y >= frames[f + 1] && y <= frames[f + 3]) insideFrame = true;
                foreach (int n in Nb(p)) if (!seen[n] && !bg[n] && IsChecker(px[n])) { seen[n] = true; cq.Enqueue(n); }
            }
            if (!insideFrame && comp.Count >= 40) foreach (int p in comp) bg[p] = true;
        }

        // Grow 1px into JPG halo pixels (greyish, between checker and outline darkness).
        var grow = new List<int>();
        for (int i = 0; i < W * H; i++)
        {
            if (bg[i]) continue;
            int c = px[i]; int r = R(c), g = G(c), b = B(c);
            int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            if (mx - mn <= 18 && mn >= 40 && mx <= 150)
                foreach (int n in Nb(i)) if (bg[n]) { grow.Add(i); break; }
        }
        foreach (int i in grow) bg[i] = true;

        for (int i = 0; i < W * H; i++) alpha[i] = bg[i] ? 0f : 1f;
        // Soft 1px edge.
        var a2 = (float[])alpha.Clone();
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                int i = y * W + x;
                if (alpha[i] == 0f) continue;
                float s = 0; int k = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { s += alpha[i + dy * W + dx]; k++; }
                if (s < k) a2[i] = Math.Max(0.35f, s / k);
            }
        alpha = a2;
    }

    static void Flood(bool[] bg, Queue<int> q)
    {
        while (q.Count > 0)
        {
            int p = q.Dequeue();
            foreach (int n in Nb(p)) if (!bg[n] && IsChecker(px[n])) { bg[n] = true; q.Enqueue(n); }
        }
    }

    static IEnumerable<int> Nb(int p)
    {
        int x = p % W, y = p / W;
        if (x > 0) yield return p - 1;
        if (x < W - 1) yield return p + 1;
        if (y > 0) yield return p - W;
        if (y < H - 1) yield return p + W;
    }

    // Erase light (text-coloured) pixels inside a rect / circle and fill the hole by diffusion.
    static int Lum(int c) { return (R(c) * 3 + G(c) * 6 + B(c)) / 10; }
    static bool IsTeal(int c) { return Lum(c) > 60 && G(c) > R(c) + 5 && B(c) > R(c); }

    // Fill a disk with the average colour of its rim, darkening slightly toward the centre.
    public static void FillDisk(int cx, int cy, int radius)
    {
        double r = 0, g = 0, b = 0; int k = 0;
        for (int y = cy - radius - 3; y <= cy + radius + 3; y++)
            for (int x = cx - radius - 3; x <= cx + radius + 3; x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                int c = px[y * W + x];
                if (d > radius - 2 && d <= radius + 1 && Lum(c) < 90) { r += R(c); g += G(c); b += B(c); k++; }
            }
        r /= k; g /= k; b /= k;
        var rnd = new Random(cx);
        for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d > radius) continue;
                double f = 0.82 + 0.18 * (d / radius) * (d / radius);
                double n = (rnd.NextDouble() - 0.5) * 4;
                int i = y * W + x;
                // blend the outermost 2px with the original for a seamless edge
                double t = Math.Min(1.0, (radius - d) / 2.0);
                int c = px[i];
                px[i] = (255 << 24) | (Clamp((float)(R(c) * (1 - t) + (r * f + n) * t)) << 16)
                    | (Clamp((float)(G(c) * (1 - t) + (g * f + n) * t)) << 8) | Clamp((float)(B(c) * (1 - t) + (b * f + n) * t));
            }
    }
    // Cream text colour - excludes oranges/reds from nearby props.
    static bool IsCream(int c, int minLum) { return Lum(c) >= minLum && G(c) * 4 >= R(c) * 3 && R(c) >= B(c) + 30; }

    // srcMode: which surrounding pixels may be used to fill the hole.
    // 0 any, 1 dark only (panels/circle), 2 maroon only (banner).
    static bool IsSource(int c, int srcMode)
    {
        if (srcMode == 1) return Lum(c) < 90;
        if (srcMode == 2) return R(c) > G(c) + 20 && Lum(c) < 140;
        return true;
    }

    // minLum 0 = erase the whole region; otherwise erase cream glyphs plus `grow` px of outline.
    public static void EraseText(int x0, int y0, int x1, int y1, int cx, int cy, int radius, int minLum, int grow, int srcMode)
    {
        var mask = new bool[W * H];
        Func<int, int, bool> inRegion = (x, y) => radius <= 0 || (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!inRegion(x, y)) continue;
                int c = px[y * W + x];
                if (minLum == 0 || IsCream(c, minLum)) mask[y * W + x] = true;
            }
        for (int pass = 0; pass < grow; pass++)
        {
            var m2 = (bool[])mask.Clone();
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * W + x;
                    if (mask[i] || !inRegion(x, y) || (srcMode != 3 && IsTeal(px[i]))) continue;
                    foreach (int n in Nb(i)) if (mask[n]) { m2[i] = true; break; }
                }
            mask = m2;
        }
        Inpaint(mask, x0, y0, x1, y1, srcMode);
    }

    static void Inpaint(bool[] mask, int x0, int y0, int x1, int y1, int srcMode)
    {
        if (srcMode == 3) { InpaintRows(mask, x0, y0, x1, y1); return; }
        var fr = new float[W * H]; var fg = new float[W * H]; var fb = new float[W * H];
        var known = new bool[W * H];
        for (int y = y0 - 1; y <= y1 + 1; y++)
            for (int x = x0 - 1; x <= x1 + 1; x++)
            {
                int i = y * W + x;
                fr[i] = R(px[i]); fg[i] = G(px[i]); fb[i] = B(px[i]);
                known[i] = !mask[i] && IsSource(px[i], srcMode);
            }
        // onion-peel fill from the edges inward, then smooth
        bool any = true;
        while (any)
        {
            any = false;
            var add = new List<int>();
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * W + x;
                    if (known[i] || !mask[i]) continue;
                    float r = 0, g = 0, b = 0; int k = 0;
                    for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                    {
                        int n = i + dy * W + dx;
                        if (known[n]) { r += fr[n]; g += fg[n]; b += fb[n]; k++; }
                    }
                    if (k >= 3) { fr[i] = r / k; fg[i] = g / k; fb[i] = b / k; add.Add(i); }
                }
            foreach (int i in add) known[i] = true;
            any = add.Count > 0;
        }
        for (int it = 0; it < 6; it++)
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * W + x;
                    if (!mask[i]) continue;
                    float r = 0, g = 0, b = 0;
                    int k = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int n = i + dy * W + dx;
                        if (!known[n]) continue;
                        r += fr[n]; g += fg[n]; b += fb[n]; k++;
                    }
                    if (k > 0) { fr[i] = r / k; fg[i] = g / k; fb[i] = b / k; }
                }
        var rnd = new Random(x0 * 31 + y0);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * W + x;
                if (!mask[i]) continue;
                // a touch of grain so the patch matches the painted texture
                float n = (float)(rnd.NextDouble() - 0.5) * 6f;
                px[i] = (255 << 24) | (Clamp(fr[i] + n) << 16) | (Clamp(fg[i] + n) << 8) | Clamp(fb[i] + n);
            }
    }

    // Horizontal-band fill: each masked pixel blends the nearest clean pixels to its left and right
    // on the same row, which keeps the banner's stripes (maroon / teal edge / outline) intact.
    static void InpaintRows(bool[] mask, int x0, int y0, int x1, int y1)
    {
        var outp = (int[])px.Clone();
        var rnd = new Random(x0 + y0 * 7);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * W + x;
                if (!mask[i]) continue;
                int l = x, r = x;
                while (l > x0 - 20 && mask[y * W + l]) l--;
                while (r < x1 + 20 && mask[y * W + r]) r++;
                // average a few clean pixels on each side to kill JPG noise
                float lr = 0, lg = 0, lb = 0, rr = 0, rg = 0, rb = 0;
                for (int s = 0; s < 3; s++)
                {
                    int cl = px[y * W + l - s], cr = px[y * W + r + s];
                    lr += R(cl); lg += G(cl); lb += B(cl); rr += R(cr); rg += G(cr); rb += B(cr);
                }
                float t = (float)(x - l) / (r - l);
                float n = (float)(rnd.NextDouble() - 0.5) * 5f;
                outp[i] = (255 << 24) | (Clamp((lr * (1 - t) + rr * t) / 3 + n) << 16)
                    | (Clamp((lg * (1 - t) + rg * t) / 3 + n) << 8) | Clamp((lb * (1 - t) + rb * t) / 3 + n);
            }
        px = outp;
    }

    static int Clamp(float v) { return v < 0 ? 0 : v > 255 ? 255 : (int)v; }

    // Save the opaque content of a region, trimmed to its bounds plus padding.
    public static string Save(string path, int x0, int y0, int x1, int y1, int pad)
    {
        int bx0 = x1, by0 = y1, bx1 = x0, by1 = y0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (alpha[y * W + x] > 0f) { bx0 = Math.Min(bx0, x); by0 = Math.Min(by0, y); bx1 = Math.Max(bx1, x); by1 = Math.Max(by1, y); }
        bx0 -= pad; by0 -= pad; bx1 += pad; by1 += pad;
        int w = bx1 - bx0 + 1, h = by1 - by0 + 1;
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var o = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = bx0 + x, sy = by0 + y;
                    if (sx < 0 || sy < 0 || sx >= W || sy >= H) continue;
                    int i = sy * W + sx;
                    int a = (int)(alpha[i] * 255);
                    o[y * w + x] = a == 0 ? 0 : (a << 24) | (px[i] & 0xFFFFFF);
                }
            Marshal.Copy(o, 0, d.Scan0, o.Length);
            bmp.UnlockBits(d);
            bmp.Save(path, ImageFormat.Png);
        }
        return string.Format("{0}: origin ({1},{2}) size {3}x{4}", System.IO.Path.GetFileName(path), bx0, by0, w, h);
    }

    // Replace a rectangle with a smooth Coons-patch blend of its four border strips (plus grain),
    // used to blank out text panels without diffusion seams.
    public static void FillPanel(int x0, int y0, int x1, int y1)
    {
        Func<int, int, float[]> avg = (x, y) =>
        {
            float r = 0, g = 0, b = 0; int k = 0;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int c = px[(y + dy) * W + x + dx]; r += R(c); g += G(c); b += B(c); k++;
            }
            return new[] { r / k, g / k, b / k };
        };
        int w = x1 - x0, h = y1 - y0;
        var L = new float[h + 1][]; var Rt = new float[h + 1][]; var T = new float[w + 1][]; var Bt = new float[w + 1][];
        for (int y = 0; y <= h; y++) { L[y] = avg(x0 - 2, y0 + y); Rt[y] = avg(x1 + 2, y0 + y); }
        for (int x = 0; x <= w; x++) { T[x] = avg(x0 + x, y0 - 2); Bt[x] = avg(x0 + x, y1 + 2); }
        // smooth the border strips so JPG noise / stray glyph pixels do not streak across
        Action<float[][]> smooth = a =>
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var c = new float[a.Length][];
                for (int i = 0; i < a.Length; i++)
                {
                    c[i] = new float[3];
                    int k = 0;
                    for (int j = Math.Max(0, i - 4); j <= Math.Min(a.Length - 1, i + 4); j++) { for (int ch = 0; ch < 3; ch++) c[i][ch] += a[j][ch]; k++; }
                    for (int ch = 0; ch < 3; ch++) c[i][ch] /= k;
                }
                Array.Copy(c, a, a.Length);
            }
        };
        smooth(L); smooth(Rt); smooth(T); smooth(Bt);
        float[] p00 = T[0], p10 = T[w], p01 = Bt[0], p11 = Bt[w];
        var rnd = new Random(x0 * 13 + y1);
        for (int y = 0; y <= h; y++)
            for (int x = 0; x <= w; x++)
            {
                float u = (float)x / w, v = (float)y / h;
                int o = 0;
                var col = new int[3];
                float n = (float)(rnd.NextDouble() - 0.5) * 5f;
                for (int ch = 0; ch < 3; ch++)
                {
                    float val = (1 - u) * L[y][ch] + u * Rt[y][ch] + (1 - v) * T[x][ch] + v * Bt[x][ch]
                        - ((1 - u) * (1 - v) * p00[ch] + u * (1 - v) * p10[ch] + (1 - u) * v * p01[ch] + u * v * p11[ch]);
                    col[ch] = Clamp(val + n);
                }
                o = (255 << 24) | (col[0] << 16) | (col[1] << 8) | col[2];
                px[(y0 + y) * W + x0 + x] = o;
            }
    }

    // Make everything transparent except a rounded rectangle (inclusive coords), antialiased.
    // Pixels lighter than `bgLum` with low saturation inside `bgBand` px of the edge also become
    // transparent, so a light backdrop does not show in the corners.
    public static void RoundedMask(int x0, int y0, int x1, int y1, float radius, int bgLum, int bgBand)
    {
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float cx = Math.Max(x0 + radius - (x + 0.5f), Math.Max(0, (x + 0.5f) - (x1 + 1 - radius)));
                float cy = Math.Max(y0 + radius - (y + 0.5f), Math.Max(0, (y + 0.5f) - (y1 + 1 - radius)));
                bool inX = x >= x0 && x <= x1, inY = y >= y0 && y <= y1;
                float a;
                if (!inX || !inY) a = 0f;
                else if (cx > 0 && cy > 0)
                {
                    float d = (float)Math.Sqrt(cx * cx + cy * cy) - radius; // >0 outside the corner arc
                    a = Math.Max(0f, Math.Min(1f, 0.5f - d));
                }
                else a = 1f;
                if (a > 0f && bgLum > 0)
                {
                    int edge = Math.Min(Math.Min(x - x0, x1 - x), Math.Min(y - y0, y1 - y));
                    int c = px[y * W + x];
                    int mx = Math.Max(R(c), Math.Max(G(c), B(c))), mn = Math.Min(R(c), Math.Min(G(c), B(c)));
                    if (edge < bgBand && Lum(c) > bgLum && mx - mn < 40) a = 0f;
                }
                alpha[y * W + x] = Math.Min(alpha[y * W + x], a);
            }
    }

    // Save a disk of the image (centre cx,cy, radius r) scaled to `size` px with a soft edge.
    public static void SaveDisk(string path, int cx, int cy, int r, int size)
    {
        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            var d = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var o = new int[size * size];
            float s = 2f * r / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = (x + 0.5f) * s - r, fy = (y + 0.5f) * s - r;
                    float dist = (float)Math.Sqrt(fx * fx + fy * fy);
                    float a = Math.Max(0f, Math.Min(1f, (r - dist) / (1.5f * s)));
                    if (a <= 0f) continue;
                    // 2x2 supersample
                    float rr = 0, gg = 0, bb = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int sx = (int)(cx + fx + (k % 2 - 0.5f) * s * 0.5f), sy = (int)(cy + fy + (k / 2 - 0.5f) * s * 0.5f);
                        int c = px[Math.Max(0, Math.Min(H - 1, sy)) * W + Math.Max(0, Math.Min(W - 1, sx))];
                        rr += R(c); gg += G(c); bb += B(c);
                    }
                    o[y * size + x] = ((int)(a * 255) << 24) | (Clamp(rr / 4) << 16) | (Clamp(gg / 4) << 8) | Clamp(bb / 4);
                }
            Marshal.Copy(o, 0, d.Scan0, o.Length);
            bmp.UnlockBits(d);
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
