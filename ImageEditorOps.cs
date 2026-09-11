using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MicroApp
{
    /// <summary>Photoshop's blend modes, in Photoshop's menu order.</summary>
    enum BlendMode
    {
        Normal,
        Darken, Multiply, ColorBurn, LinearBurn,
        Lighten, Screen, ColorDodge, LinearDodge,
        Overlay, SoftLight, HardLight,
        Difference, Exclusion,
        Hue, Saturation, Color, Luminosity
    }

    /// <summary>
    /// A 32-bit BGRA pixel buffer lifted out of a Bitmap (straight alpha, row stride =
    /// width * 4). Every adjustment, filter and blend in the editor works on one of
    /// these with plain array maths - no unsafe code, no per-pixel GetPixel.
    /// </summary>
    sealed class Pixels
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int Stride;
        public byte[] Data;

        public Pixels(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            Stride = Width * 4;
            Data = new byte[Stride * Height];
        }

        public int Index(int x, int y) { return y * Stride + x * 4; }

        public static Pixels From(Bitmap bmp)
        {
            var p = new Pixels(bmp.Width, bmp.Height);
            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData d = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (d.Stride == p.Stride)
                    Marshal.Copy(d.Scan0, p.Data, 0, p.Data.Length);
                else
                    for (int y = 0; y < p.Height; y++)
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), p.Data, y * p.Stride, p.Stride);
            }
            finally { bmp.UnlockBits(d); }
            return p;
        }

        public Bitmap ToBitmap()
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            WriteTo(bmp);
            return bmp;
        }

        /// <summary>Copies the buffer back into a bitmap of the same size.</summary>
        public void WriteTo(Bitmap bmp)
        {
            Rectangle r = new Rectangle(0, 0, Width, Height);
            BitmapData d = bmp.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (d.Stride == Stride)
                    Marshal.Copy(Data, 0, d.Scan0, Data.Length);
                else
                    for (int y = 0; y < Height; y++)
                        Marshal.Copy(Data, y * Stride, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), Stride);
            }
            finally { bmp.UnlockBits(d); }
        }

        /// <summary>
        /// Copies just one rectangle of the buffer into the bitmap (brush strokes repaint
        /// only what changed). The bitmap is locked whole and only the rectangle's rows are
        /// written: locking a sub-rectangle looks tidier but GDI+ implementations differ on
        /// where that buffer maps back to - libgdiplus writes it to the image origin, which
        /// stamps the dab in the wrong corner. Locking the whole image costs nothing (the
        /// lock is in place) and lands every byte where it belongs.
        /// </summary>
        public void WriteTo(Bitmap bmp, Rectangle roi)
        {
            roi.Intersect(new Rectangle(0, 0, Width, Height));
            if (roi.Width < 1 || roi.Height < 1) return;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = roi.Top; y < roi.Bottom; y++)
                    Marshal.Copy(Data, y * Stride + roi.X * 4,
                                 new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride + roi.X * 4), roi.Width * 4);
            }
            finally { bmp.UnlockBits(d); }
        }

        public Pixels Clone()
        {
            var c = new Pixels(Width, Height);
            Buffer.BlockCopy(Data, 0, c.Data, 0, Data.Length);
            return c;
        }
    }

    /// <summary>
    /// The pixel engine: blend modes, Image &gt; Adjustments, the Filter menu, flood
    /// fills, perspective warps. Everything takes a <see cref="Pixels"/> and an optional
    /// selection <c>mask</c> (one byte per pixel, 255 = fully selected) and edits in place,
    /// mixing the result back through the mask so a feathered selection fades the effect.
    /// </summary>
    static class PixelOps
    {
        static readonly Random Rng = new Random();

        // ================================================================ helpers

        static byte Clamp(int v) { return v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v; }
        static byte Clamp(float v) { return v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)(v + 0.5f); }
        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        /// <summary>Rec. 601 luma - what Photoshop's Desaturate/Threshold see as "brightness".</summary>
        static float Luma(float r, float g, float b) { return 0.299f * r + 0.587f * g + 0.114f * b; }

        /// <summary>result = orig + (result - orig) * mask/255, so unselected pixels stay put.</summary>
        public static void MixByMask(Pixels orig, Pixels result, byte[] mask)
        {
            if (mask == null) return;
            byte[] o = orig.Data, r = result.Data;
            int n = orig.Width * orig.Height;
            for (int i = 0, p = 0; i < n; i++, p += 4)
            {
                int m = mask[i];
                if (m == 255) continue;
                if (m == 0)
                {
                    r[p] = o[p]; r[p + 1] = o[p + 1]; r[p + 2] = o[p + 2]; r[p + 3] = o[p + 3];
                    continue;
                }
                int inv = 255 - m;
                r[p] = (byte)((o[p] * inv + r[p] * m) / 255);
                r[p + 1] = (byte)((o[p + 1] * inv + r[p + 1] * m) / 255);
                r[p + 2] = (byte)((o[p + 2] * inv + r[p + 2] * m) / 255);
                r[p + 3] = (byte)((o[p + 3] * inv + r[p + 3] * m) / 255);
            }
        }

        /// <summary>Runs an in-place edit and mixes it back through the selection mask.</summary>
        static void Masked(Pixels px, byte[] mask, Action<Pixels> edit)
        {
            if (mask == null) { edit(px); return; }
            Pixels orig = px.Clone();
            edit(px);
            MixByMask(orig, px, mask);
        }

        static void ApplyLut(Pixels px, byte[] lutR, byte[] lutG, byte[] lutB)
        {
            byte[] d = px.Data;
            for (int p = 0; p < d.Length; p += 4)
            {
                if (d[p + 3] == 0) continue;
                d[p] = lutB[d[p]];
                d[p + 1] = lutG[d[p + 1]];
                d[p + 2] = lutR[d[p + 2]];
            }
        }

        static void ApplyLut(Pixels px, byte[] lut) { ApplyLut(px, lut, lut, lut); }

        // ================================================================= blending

        /// <summary>
        /// Composites <paramref name="src"/> over <paramref name="dst"/> (same size) with a
        /// Photoshop blend mode and a layer opacity, following the W3C compositing formulas
        /// on straight alpha.
        /// </summary>
        public static void BlendOver(Pixels dst, Pixels src, BlendMode mode, float opacity)
        {
            byte[] d = dst.Data, s = src.Data;
            int n = Math.Min(d.Length, s.Length);
            bool separable = mode != BlendMode.Hue && mode != BlendMode.Saturation &&
                             mode != BlendMode.Color && mode != BlendMode.Luminosity;
            for (int p = 0; p < n; p += 4)
            {
                float aS = s[p + 3] / 255f * opacity;
                if (aS <= 0) continue;
                float aD = d[p + 3] / 255f;
                float bB = d[p] / 255f, gB = d[p + 1] / 255f, rB = d[p + 2] / 255f;
                float bS = s[p] / 255f, gS = s[p + 1] / 255f, rS = s[p + 2] / 255f;

                float rX, gX, bX;   // B(backdrop, source)
                if (mode == BlendMode.Normal || aD <= 0) { rX = rS; gX = gS; bX = bS; }
                else if (separable)
                {
                    rX = BlendChannel(mode, rB, rS);
                    gX = BlendChannel(mode, gB, gS);
                    bX = BlendChannel(mode, bB, bS);
                }
                else
                {
                    BlendNonSeparable(mode, rB, gB, bB, rS, gS, bS, out rX, out gX, out bX);
                }

                // Cr = (1 - aD) * Cs + aD * B(Cb, Cs)
                float rR = (1 - aD) * rS + aD * rX;
                float gR = (1 - aD) * gS + aD * gX;
                float bR = (1 - aD) * bS + aD * bX;

                float aO = aS + aD * (1 - aS);
                if (aO <= 0) continue;
                float rO = (aS * rR + aD * (1 - aS) * rB) / aO;
                float gO = (aS * gR + aD * (1 - aS) * gB) / aO;
                float bO = (aS * bR + aD * (1 - aS) * bB) / aO;
                d[p] = Clamp(bO * 255f);
                d[p + 1] = Clamp(gO * 255f);
                d[p + 2] = Clamp(rO * 255f);
                d[p + 3] = Clamp(aO * 255f);
            }
        }

        static float BlendChannel(BlendMode mode, float cb, float cs)
        {
            switch (mode)
            {
                case BlendMode.Darken: return Math.Min(cb, cs);
                case BlendMode.Multiply: return cb * cs;
                case BlendMode.ColorBurn: return cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - Math.Min(1, (1 - cb) / cs);
                case BlendMode.LinearBurn: return Clamp01(cb + cs - 1);
                case BlendMode.Lighten: return Math.Max(cb, cs);
                case BlendMode.Screen: return cb + cs - cb * cs;
                case BlendMode.ColorDodge: return cb <= 0 ? 0 : cs >= 1 ? 1 : Math.Min(1, cb / (1 - cs));
                case BlendMode.LinearDodge: return Clamp01(cb + cs);
                case BlendMode.Overlay: return HardLight(cs, cb);
                case BlendMode.HardLight: return HardLight(cb, cs);
                case BlendMode.SoftLight:
                {
                    if (cs <= 0.5f) return cb - (1 - 2 * cs) * cb * (1 - cb);
                    float dcb = cb <= 0.25f ? ((16 * cb - 12) * cb + 4) * cb : (float)Math.Sqrt(cb);
                    return cb + (2 * cs - 1) * (dcb - cb);
                }
                case BlendMode.Difference: return Math.Abs(cb - cs);
                case BlendMode.Exclusion: return cb + cs - 2 * cb * cs;
                default: return cs;
            }
        }

        static float HardLight(float cb, float cs)
        {
            return cs <= 0.5f ? cb * 2 * cs : cb + (2 * cs - 1) - cb * (2 * cs - 1);   // multiply / screen
        }

        static void BlendNonSeparable(BlendMode mode, float rb, float gb, float bb, float rs, float gs, float bs,
                                      out float r, out float g, out float b)
        {
            switch (mode)
            {
                case BlendMode.Hue:
                    SetSat(rs, gs, bs, Sat(rb, gb, bb), out r, out g, out b);
                    SetLum(r, g, b, Lum(rb, gb, bb), out r, out g, out b);
                    break;
                case BlendMode.Saturation:
                    SetSat(rb, gb, bb, Sat(rs, gs, bs), out r, out g, out b);
                    SetLum(r, g, b, Lum(rb, gb, bb), out r, out g, out b);
                    break;
                case BlendMode.Color:
                    SetLum(rs, gs, bs, Lum(rb, gb, bb), out r, out g, out b);
                    break;
                default:   // Luminosity
                    SetLum(rb, gb, bb, Lum(rs, gs, bs), out r, out g, out b);
                    break;
            }
        }

        static float Lum(float r, float g, float b) { return 0.3f * r + 0.59f * g + 0.11f * b; }
        static float Sat(float r, float g, float b) { return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)); }

        static void SetLum(float r, float g, float b, float l, out float ro, out float go, out float bo)
        {
            float d = l - Lum(r, g, b);
            r += d; g += d; b += d;
            // clip colour back into gamut
            float lum = Lum(r, g, b);
            float n = Math.Min(r, Math.Min(g, b)), x = Math.Max(r, Math.Max(g, b));
            if (n < 0 && lum - n > 0.00001f)
            {
                r = lum + (r - lum) * lum / (lum - n);
                g = lum + (g - lum) * lum / (lum - n);
                b = lum + (b - lum) * lum / (lum - n);
            }
            if (x > 1 && x - lum > 0.00001f)
            {
                r = lum + (r - lum) * (1 - lum) / (x - lum);
                g = lum + (g - lum) * (1 - lum) / (x - lum);
                b = lum + (b - lum) * (1 - lum) / (x - lum);
            }
            ro = r; go = g; bo = b;
        }

        static void SetSat(float r, float g, float b, float s, out float ro, out float go, out float bo)
        {
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max - min <= 0.00001f) { ro = go = bo = 0; return; }
            // scale so the max becomes s, the min 0, the middle in proportion
            ro = r == max ? s : r == min ? 0 : (r - min) * s / (max - min);
            go = g == max ? s : g == min ? 0 : (g - min) * s / (max - min);
            bo = b == max ? s : b == min ? 0 : (b - min) * s / (max - min);
        }

        // ============================================================ adjustments

        public static void BrightnessContrast(Pixels px, int brightness, int contrast, byte[] mask)
        {
            // Photoshop's "legacy" curve: brightness shifts, contrast pivots around 128
            var lut = new byte[256];
            float c = contrast <= 0 ? 1 + contrast / 100f : 1 + contrast / 100f * 2f;
            for (int i = 0; i < 256; i++)
                lut[i] = Clamp((i - 128) * c + 128 + brightness);
            Masked(px, mask, p => ApplyLut(p, lut));
        }

        /// <summary>Levels: input black/white/gamma, output black/white; channel 0 = RGB, 1..3 = R/G/B.</summary>
        public static void Levels(Pixels px, int inBlack, int inWhite, float gamma, int outBlack, int outWhite, int channel, byte[] mask)
        {
            byte[] lut = LevelsLut(inBlack, inWhite, gamma, outBlack, outWhite);
            byte[] id = IdentityLut();
            Masked(px, mask, p => ApplyLut(p,
                channel == 0 || channel == 1 ? lut : id,
                channel == 0 || channel == 2 ? lut : id,
                channel == 0 || channel == 3 ? lut : id));
        }

        public static byte[] LevelsLut(int inBlack, int inWhite, float gamma, int outBlack, int outWhite)
        {
            var lut = new byte[256];
            float range = Math.Max(1, inWhite - inBlack);
            float g = 1f / Math.Max(0.01f, gamma);
            for (int i = 0; i < 256; i++)
            {
                float v = Clamp01((i - inBlack) / range);
                v = (float)Math.Pow(v, g);
                lut[i] = Clamp(outBlack + v * (outWhite - outBlack));
            }
            return lut;
        }

        static byte[] IdentityLut()
        {
            var lut = new byte[256];
            for (int i = 0; i < 256; i++) lut[i] = (byte)i;
            return lut;
        }

        /// <summary>
        /// Curves: a 256-entry LUT from control points (x,y in 0..255) through a monotone
        /// cubic spline, so the curve never overshoots between points the way Photoshop's does not.
        /// </summary>
        public static byte[] CurveLut(IList<PointF> points)
        {
            var lut = new byte[256];
            if (points == null || points.Count == 0) return IdentityLut();
            var pts = new List<PointF>(points);
            pts.Sort((a, b) => a.X.CompareTo(b.X));
            if (pts.Count == 1)
            {
                for (int i = 0; i < 256; i++) lut[i] = Clamp(pts[0].Y);
                return lut;
            }
            int n = pts.Count;
            var m = new float[n];
            var delta = new float[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                float dx = Math.Max(0.0001f, pts[i + 1].X - pts[i].X);
                delta[i] = (pts[i + 1].Y - pts[i].Y) / dx;
            }
            m[0] = delta[0];
            m[n - 1] = delta[n - 2];
            for (int i = 1; i < n - 1; i++)
                m[i] = delta[i - 1] * delta[i] <= 0 ? 0 : (delta[i - 1] + delta[i]) / 2f;
            for (int i = 0; i < n - 1; i++)
            {
                if (delta[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
                float a = m[i] / delta[i], b = m[i + 1] / delta[i];
                float h = a * a + b * b;
                if (h > 9)
                {
                    float t = 3f / (float)Math.Sqrt(h);
                    m[i] = t * a * delta[i];
                    m[i + 1] = t * b * delta[i];
                }
            }
            for (int x = 0; x < 256; x++)
            {
                float y;
                if (x <= pts[0].X) y = pts[0].Y;
                else if (x >= pts[n - 1].X) y = pts[n - 1].Y;
                else
                {
                    int i = 0;
                    while (i < n - 2 && x > pts[i + 1].X) i++;
                    float h = Math.Max(0.0001f, pts[i + 1].X - pts[i].X);
                    float t = (x - pts[i].X) / h;
                    float t2 = t * t, t3 = t2 * t;
                    float h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t, h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
                    y = h00 * pts[i].Y + h10 * h * m[i] + h01 * pts[i + 1].Y + h11 * h * m[i + 1];
                }
                lut[x] = Clamp(y);
            }
            return lut;
        }

        public static void Curves(Pixels px, byte[] lutRgb, byte[] lutR, byte[] lutG, byte[] lutB, byte[] mask)
        {
            // the RGB curve applies first, then each channel's own curve - Photoshop's order
            byte[] r = new byte[256], g = new byte[256], b = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                int v = lutRgb == null ? i : lutRgb[i];
                r[i] = lutR == null ? (byte)v : lutR[v];
                g[i] = lutG == null ? (byte)v : lutG[v];
                b[i] = lutB == null ? (byte)v : lutB[v];
            }
            Masked(px, mask, p => ApplyLut(p, r, g, b));
        }

        public static void Exposure(Pixels px, float exposure, float offset, float gamma, byte[] mask)
        {
            var lut = new byte[256];
            float mul = (float)Math.Pow(2, exposure);
            float g = 1f / Math.Max(0.01f, gamma);
            for (int i = 0; i < 256; i++)
            {
                float v = i / 255f * mul + offset;
                v = (float)Math.Pow(Clamp01(v), g);
                lut[i] = Clamp(v * 255f);
            }
            Masked(px, mask, p => ApplyLut(p, lut));
        }

        public static void Invert(Pixels px, byte[] mask)
        {
            var lut = new byte[256];
            for (int i = 0; i < 256; i++) lut[i] = (byte)(255 - i);
            Masked(px, mask, p => ApplyLut(p, lut));
        }

        public static void Posterize(Pixels px, int levels, byte[] mask)
        {
            levels = Math.Max(2, Math.Min(255, levels));
            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                int step = (int)(i * levels / 256f);
                lut[i] = Clamp((int)Math.Round(step * 255f / (levels - 1)));
            }
            Masked(px, mask, p => ApplyLut(p, lut));
        }

        public static void Threshold(Pixels px, int level, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                byte[] d = p.Data;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    byte v = Luma(d[i + 2], d[i + 1], d[i]) >= level ? (byte)255 : (byte)0;
                    d[i] = d[i + 1] = d[i + 2] = v;
                }
            });
        }

        public static void Desaturate(Pixels px, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                byte[] d = p.Data;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    // Photoshop's Desaturate is the HSL lightness: (max + min) / 2
                    int max = Math.Max(d[i], Math.Max(d[i + 1], d[i + 2]));
                    int min = Math.Min(d[i], Math.Min(d[i + 1], d[i + 2]));
                    byte v = (byte)((max + min) / 2);
                    d[i] = d[i + 1] = d[i + 2] = v;
                }
            });
        }

        /// <summary>Hue/Saturation: hue -180..180, saturation and lightness -100..100; colorize paints one hue.</summary>
        public static void HueSaturation(Pixels px, int hue, int saturation, int lightness, bool colorize, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                byte[] d = p.Data;
                float satF = saturation / 100f, lightF = lightness / 100f;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    float r = d[i + 2] / 255f, g = d[i + 1] / 255f, b = d[i] / 255f;
                    float h, s, l;
                    RgbToHsl(r, g, b, out h, out s, out l);
                    if (colorize)
                    {
                        h = (hue + 360) % 360;
                        s = Clamp01(Math.Abs(saturation) / 100f);   // colorize: the slider reads 0..100
                    }
                    else
                    {
                        h = (h + hue + 360) % 360;
                        s = satF >= 0 ? s + (1 - s) * satF : s * (1 + satF);
                    }
                    l = lightF >= 0 ? l + (1 - l) * lightF : l * (1 + lightF);
                    HslToRgb(h, Clamp01(s), Clamp01(l), out r, out g, out b);
                    d[i + 2] = Clamp(r * 255f); d[i + 1] = Clamp(g * 255f); d[i] = Clamp(b * 255f);
                }
            });
        }

        public static void Vibrance(Pixels px, int vibrance, int saturation, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                byte[] d = p.Data;
                float vib = vibrance / 100f, sat = saturation / 100f;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    float r = d[i + 2], g = d[i + 1], b = d[i];
                    float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    float avg = (r + g + b) / 3f;
                    // vibrance: boost the least saturated pixels most, protect skin (reds)
                    float curSat = max <= 0 ? 0 : (max - min) / max;
                    float amt = vib * (1 - curSat) * (1 - Math.Max(0, (r - g) / 255f) * 0.5f);
                    amt += sat;
                    r = avg + (r - avg) * (1 + amt);
                    g = avg + (g - avg) * (1 + amt);
                    b = avg + (b - avg) * (1 + amt);
                    d[i + 2] = Clamp(r); d[i + 1] = Clamp(g); d[i] = Clamp(b);
                }
            });
        }

        /// <summary>Colour balance: three -100..100 triplets (cyan-red, magenta-green, yellow-blue) per tonal range.</summary>
        public static void ColorBalance(Pixels px, int[] shadows, int[] midtones, int[] highlights, bool preserveLum, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                byte[] d = p.Data;
                var wS = new float[256]; var wM = new float[256]; var wH = new float[256];
                for (int i = 0; i < 256; i++)
                {
                    float v = i / 255f;
                    wS[i] = Clamp01(1 - v * 2.5f);          // full below ~0.4, gone by 0.4
                    wH[i] = Clamp01((v - 0.6f) * 2.5f);
                    wM[i] = 1 - Math.Abs(v - 0.5f) * 2f;    // a tent peaking at the midpoint
                }
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    int r = d[i + 2], g = d[i + 1], b = d[i];
                    int l = (int)Luma(r, g, b);
                    float s = wS[l], m = wM[l], h = wH[l];
                    float nr = r + (shadows[0] * s + midtones[0] * m + highlights[0] * h) * 0.5f;
                    float ng = g + (shadows[1] * s + midtones[1] * m + highlights[1] * h) * 0.5f;
                    float nb = b + (shadows[2] * s + midtones[2] * m + highlights[2] * h) * 0.5f;
                    if (preserveLum)
                    {
                        float before = Luma(r, g, b), after = Luma(nr, ng, nb);
                        float diff = before - after;
                        nr += diff; ng += diff; nb += diff;
                    }
                    d[i + 2] = Clamp(nr); d[i + 1] = Clamp(ng); d[i] = Clamp(nb);
                }
            });
        }

        /// <summary>Black &amp; White: per-hue weights (reds, yellows, greens, cyans, blues, magentas, -200..300) and an optional tint.</summary>
        public static void BlackWhite(Pixels px, int[] weights, bool tint, Color tintColor, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                byte[] d = p.Data;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    float r = d[i + 2] / 255f, g = d[i + 1] / 255f, b = d[i] / 255f;
                    float h, s, l;
                    RgbToHsl(r, g, b, out h, out s, out l);
                    // the six hue sectors, blended by how close the hue sits to each: a
                    // neutral pixel keeps its value, a saturated one is lifted or dropped
                    // by its hue's weight - the way the Black & White sliders behave
                    float w = HueWeight(h, weights);
                    float min = Math.Min(r, Math.Min(g, b)), max = Math.Max(r, Math.Max(g, b));
                    float v = Clamp01(min + (max - min) * w);
                    if (tint)
                    {
                        float tr = tintColor.R / 255f, tg = tintColor.G / 255f, tb = tintColor.B / 255f;
                        float tl = Luma(tr, tg, tb);
                        float rr = v <= tl ? tr * v / Math.Max(0.001f, tl) : tr + (1 - tr) * (v - tl) / Math.Max(0.001f, 1 - tl);
                        float gg = v <= tl ? tg * v / Math.Max(0.001f, tl) : tg + (1 - tg) * (v - tl) / Math.Max(0.001f, 1 - tl);
                        float bb = v <= tl ? tb * v / Math.Max(0.001f, tl) : tb + (1 - tb) * (v - tl) / Math.Max(0.001f, 1 - tl);
                        d[i + 2] = Clamp(rr * 255f); d[i + 1] = Clamp(gg * 255f); d[i] = Clamp(bb * 255f);
                    }
                    else
                    {
                        byte gv = Clamp(v * 255f);
                        d[i] = d[i + 1] = d[i + 2] = gv;
                    }
                }
            });
        }

        static float HueWeight(float h, int[] weights)
        {
            // sectors centred on 0 red, 60 yellow, 120 green, 180 cyan, 240 blue, 300 magenta
            float pos = h / 60f;
            int a = (int)Math.Floor(pos) % 6, b = (a + 1) % 6;
            float t = pos - (float)Math.Floor(pos);
            float wa = weights[a] / 100f, wb = weights[b] / 100f;
            return wa + (wb - wa) * t;
        }

        /// <summary>Photo Filter: tints toward a colour by density (0..100), like a lens filter.</summary>
        public static void PhotoFilter(Pixels px, Color filter, int density, bool preserveLum, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                byte[] d = p.Data;
                float k = density / 100f;
                float fr = filter.R / 255f, fg = filter.G / 255f, fb = filter.B / 255f;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    float r = d[i + 2] / 255f, g = d[i + 1] / 255f, b = d[i] / 255f;
                    // multiply toward the filter colour, blended by density
                    float nr = r * (1 - k + k * fr), ng = g * (1 - k + k * fg), nb = b * (1 - k + k * fb);
                    if (preserveLum)
                    {
                        float before = Luma(r, g, b), after = Luma(nr, ng, nb);
                        if (after > 0.0001f) { float f = before / after; nr *= f; ng *= f; nb *= f; }
                    }
                    d[i + 2] = Clamp(nr * 255f); d[i + 1] = Clamp(ng * 255f); d[i] = Clamp(nb * 255f);
                }
            });
        }

        /// <summary>Auto Tone: stretch each channel so 0.1% of pixels clip at either end.</summary>
        public static void AutoTone(Pixels px, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                int[][] hist = Histograms(p);
                byte[][] luts = new byte[3][];
                for (int c = 0; c < 3; c++)
                {
                    int lo, hi;
                    ClipPoints(hist[c], p.Width * p.Height, 0.001f, out lo, out hi);
                    luts[c] = LevelsLut(lo, hi, 1f, 0, 255);
                }
                ApplyLut(p, luts[2], luts[1], luts[0]);
            });
        }

        /// <summary>Auto Contrast: one stretch for all channels, so colours do not shift.</summary>
        public static void AutoContrast(Pixels px, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                int[][] hist = Histograms(p);
                var lum = new int[256];
                for (int i = 0; i < 256; i++) lum[i] = hist[0][i] + hist[1][i] + hist[2][i];
                int lo, hi;
                ClipPoints(lum, p.Width * p.Height * 3, 0.001f, out lo, out hi);
                ApplyLut(p, LevelsLut(lo, hi, 1f, 0, 255));
            });
        }

        /// <summary>Auto Color: per-channel stretch, then pull the midtones toward neutral grey.</summary>
        public static void AutoColor(Pixels px, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                AutoTone(p, null);
                long sr = 0, sg = 0, sb = 0, n = 0;
                byte[] d = p.Data;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    sb += d[i]; sg += d[i + 1]; sr += d[i + 2]; n++;
                }
                if (n == 0) return;
                float mr = sr / (float)n, mg = sg / (float)n, mb = sb / (float)n;
                float target = (mr + mg + mb) / 3f;
                byte[] lr = GammaLutToMean(mr, target), lg = GammaLutToMean(mg, target), lb = GammaLutToMean(mb, target);
                ApplyLut(p, lr, lg, lb);
            });
        }

        static byte[] GammaLutToMean(float mean, float target)
        {
            // choose a gamma that maps the channel mean onto the target grey
            float m = Clamp01(mean / 255f), t = Clamp01(target / 255f);
            float gamma = m <= 0.001f || t <= 0.001f ? 1f : (float)(Math.Log(t) / Math.Log(m));
            gamma = Math.Max(0.5f, Math.Min(2f, gamma));
            var lut = new byte[256];
            for (int i = 0; i < 256; i++) lut[i] = Clamp((float)Math.Pow(i / 255f, gamma) * 255f);
            return lut;
        }

        public static void Equalize(Pixels px, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                int[][] hist = Histograms(p);
                byte[][] luts = new byte[3][];
                int total = p.Width * p.Height;
                for (int c = 0; c < 3; c++)
                {
                    luts[c] = new byte[256];
                    long cum = 0;
                    for (int i = 0; i < 256; i++)
                    {
                        cum += hist[c][i];
                        luts[c][i] = Clamp((int)(cum * 255L / Math.Max(1, total)));
                    }
                }
                ApplyLut(p, luts[2], luts[1], luts[0]);
            });
        }

        /// <summary>Histograms of B, G, R (index 0..2 = channel offset in the buffer).</summary>
        public static int[][] Histograms(Pixels p)
        {
            var hist = new[] { new int[256], new int[256], new int[256] };
            byte[] d = p.Data;
            for (int i = 0; i < d.Length; i += 4)
            {
                if (d[i + 3] == 0) continue;
                hist[0][d[i]]++; hist[1][d[i + 1]]++; hist[2][d[i + 2]]++;
            }
            return hist;
        }

        /// <summary>Luminance histogram, for the Levels dialog.</summary>
        public static int[] LumaHistogram(Pixels p)
        {
            var hist = new int[256];
            byte[] d = p.Data;
            for (int i = 0; i < d.Length; i += 4)
            {
                if (d[i + 3] == 0) continue;
                hist[(int)Luma(d[i + 2], d[i + 1], d[i])]++;
            }
            return hist;
        }

        static void ClipPoints(int[] hist, int total, float clip, out int lo, out int hi)
        {
            long target = (long)(total * clip);
            long acc = 0;
            lo = 0;
            for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc > target) { lo = i; break; } }
            acc = 0;
            hi = 255;
            for (int i = 255; i >= 0; i--) { acc += hist[i]; if (acc > target) { hi = i; break; } }
            if (hi <= lo) { lo = 0; hi = 255; }
        }

        // ============================================================ colour maths

        public static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
        {
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2f;
            if (max == min) { h = 0; s = 0; return; }
            float d = max - min;
            s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60f;
        }

        public static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
        {
            if (s <= 0) { r = g = b = l; return; }
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            float p = 2 * l - q;
            float hk = ((h % 360) + 360) % 360 / 360f;
            r = HueToRgb(p, q, hk + 1f / 3f);
            g = HueToRgb(p, q, hk);
            b = HueToRgb(p, q, hk - 1f / 3f);
        }

        static float HueToRgb(float p, float q, float t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1f / 6f) return p + (q - p) * 6 * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
            return p;
        }

        // ================================================================= filters

        /// <summary>Gaussian blur (three box passes on premultiplied colour so edges stay clean).</summary>
        public static void GaussianBlur(Pixels px, float radius, byte[] mask)
        {
            if (radius < 0.3f) return;
            Masked(px, mask, p =>
            {
                Premultiply(p);
                int[] boxes = BoxesForGauss(radius, 3);
                for (int i = 0; i < 3; i++) BoxBlur(p, (boxes[i] - 1) / 2);
                Unpremultiply(p);
            });
        }

        static int[] BoxesForGauss(float sigma, int n)
        {
            float wIdeal = (float)Math.Sqrt(12 * sigma * sigma / n + 1);
            int wl = (int)Math.Floor(wIdeal);
            if (wl % 2 == 0) wl--;
            int wu = wl + 2;
            float mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4f * wl - 4);
            int m = (int)Math.Round(mIdeal);
            var sizes = new int[n];
            for (int i = 0; i < n; i++) sizes[i] = i < m ? wl : wu;
            return sizes;
        }

        public static void BoxBlur(Pixels p, int radius)
        {
            if (radius < 1) return;
            int w = p.Width, h = p.Height, stride = p.Stride;
            byte[] src = p.Data;
            var tmp = new byte[src.Length];
            int win = radius * 2 + 1;
            // horizontal
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int sb = 0, sg = 0, sr = 0, sa = 0;
                for (int x = -radius; x <= radius; x++)
                {
                    int cx = x < 0 ? 0 : x >= w ? w - 1 : x;
                    int i = row + cx * 4;
                    sb += src[i]; sg += src[i + 1]; sr += src[i + 2]; sa += src[i + 3];
                }
                for (int x = 0; x < w; x++)
                {
                    int o = row + x * 4;
                    tmp[o] = (byte)(sb / win); tmp[o + 1] = (byte)(sg / win); tmp[o + 2] = (byte)(sr / win); tmp[o + 3] = (byte)(sa / win);
                    int add = x + radius + 1; if (add >= w) add = w - 1;
                    int sub = x - radius; if (sub < 0) sub = 0;
                    int ia = row + add * 4, isub = row + sub * 4;
                    sb += src[ia] - src[isub]; sg += src[ia + 1] - src[isub + 1];
                    sr += src[ia + 2] - src[isub + 2]; sa += src[ia + 3] - src[isub + 3];
                }
            }
            // vertical
            for (int x = 0; x < w; x++)
            {
                int col = x * 4;
                int sb = 0, sg = 0, sr = 0, sa = 0;
                for (int y = -radius; y <= radius; y++)
                {
                    int cy = y < 0 ? 0 : y >= h ? h - 1 : y;
                    int i = cy * stride + col;
                    sb += tmp[i]; sg += tmp[i + 1]; sr += tmp[i + 2]; sa += tmp[i + 3];
                }
                for (int y = 0; y < h; y++)
                {
                    int o = y * stride + col;
                    src[o] = (byte)(sb / win); src[o + 1] = (byte)(sg / win); src[o + 2] = (byte)(sr / win); src[o + 3] = (byte)(sa / win);
                    int add = y + radius + 1; if (add >= h) add = h - 1;
                    int sub = y - radius; if (sub < 0) sub = 0;
                    int ia = add * stride + col, isub = sub * stride + col;
                    sb += tmp[ia] - tmp[isub]; sg += tmp[ia + 1] - tmp[isub + 1];
                    sr += tmp[ia + 2] - tmp[isub + 2]; sa += tmp[ia + 3] - tmp[isub + 3];
                }
            }
        }

        public static void Premultiply(Pixels p)
        {
            byte[] d = p.Data;
            for (int i = 0; i < d.Length; i += 4)
            {
                int a = d[i + 3];
                if (a == 255) continue;
                d[i] = (byte)(d[i] * a / 255); d[i + 1] = (byte)(d[i + 1] * a / 255); d[i + 2] = (byte)(d[i + 2] * a / 255);
            }
        }

        public static void Unpremultiply(Pixels p)
        {
            byte[] d = p.Data;
            for (int i = 0; i < d.Length; i += 4)
            {
                int a = d[i + 3];
                if (a == 255 || a == 0) continue;
                d[i] = Clamp(d[i] * 255 / a); d[i + 1] = Clamp(d[i + 1] * 255 / a); d[i + 2] = Clamp(d[i + 2] * 255 / a);
            }
        }

        /// <summary>Motion blur: averages samples along a line of the given angle and length.</summary>
        public static void MotionBlur(Pixels px, float angleDeg, int distance, byte[] mask)
        {
            if (distance < 1) return;
            Masked(px, mask, p =>
            {
                Premultiply(p);
                int w = p.Width, h = p.Height, stride = p.Stride;
                byte[] src = (byte[])p.Data.Clone();
                byte[] dst = p.Data;
                double dx = Math.Cos(angleDeg * Math.PI / 180), dy = -Math.Sin(angleDeg * Math.PI / 180);
                int half = distance / 2;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int sb = 0, sg = 0, sr = 0, sa = 0, n = 0;
                        for (int k = -half; k <= half; k++)
                        {
                            int sx = x + (int)Math.Round(dx * k), sy = y + (int)Math.Round(dy * k);
                            if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                            int i = sy * stride + sx * 4;
                            sb += src[i]; sg += src[i + 1]; sr += src[i + 2]; sa += src[i + 3]; n++;
                        }
                        if (n == 0) continue;
                        int o = y * stride + x * 4;
                        dst[o] = (byte)(sb / n); dst[o + 1] = (byte)(sg / n); dst[o + 2] = (byte)(sr / n); dst[o + 3] = (byte)(sa / n);
                    }
                }
                Unpremultiply(p);
            });
        }

        /// <summary>3x3 convolution with a divisor and offset; alpha is left alone.</summary>
        public static void Convolve3(Pixels px, float[] k, float divisor, int offset, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                int w = p.Width, h = p.Height, stride = p.Stride;
                byte[] src = (byte[])p.Data.Clone();
                byte[] dst = p.Data;
                float inv = 1f / (divisor == 0 ? 1 : divisor);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sb = 0, sg = 0, sr = 0;
                        int ki = 0;
                        for (int ky = -1; ky <= 1; ky++)
                        {
                            int sy = y + ky; if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
                            for (int kx = -1; kx <= 1; kx++, ki++)
                            {
                                int sx = x + kx; if (sx < 0) sx = 0; else if (sx >= w) sx = w - 1;
                                int i = sy * stride + sx * 4;
                                float kv = k[ki];
                                sb += src[i] * kv; sg += src[i + 1] * kv; sr += src[i + 2] * kv;
                            }
                        }
                        int o = y * stride + x * 4;
                        dst[o] = Clamp(sb * inv + offset); dst[o + 1] = Clamp(sg * inv + offset); dst[o + 2] = Clamp(sr * inv + offset);
                    }
                }
            });
        }

        public static void Sharpen(Pixels px, float amount, byte[] mask)
        {
            // unsharp-style 3x3: centre 1 + 4a, neighbours -a
            float a = amount;
            Convolve3(px, new[] { 0, -a, 0, -a, 1 + 4 * a, -a, 0, -a, 0 }, 1, 0, mask);
        }

        /// <summary>Unsharp Mask: amount %, radius px, threshold levels - Photoshop's three knobs.</summary>
        public static void UnsharpMask(Pixels px, int amount, float radius, int threshold, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                Pixels blurred = p.Clone();
                GaussianBlur(blurred, radius, null);
                byte[] d = p.Data, b = blurred.Data;
                float k = amount / 100f;
                for (int i = 0; i < d.Length; i += 4)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int diff = d[i + c] - b[i + c];
                        if (Math.Abs(diff) < threshold) continue;
                        d[i + c] = Clamp(d[i + c] + diff * k);
                    }
                }
            });
        }

        public static void HighPass(Pixels px, float radius, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                Pixels blurred = p.Clone();
                GaussianBlur(blurred, radius, null);
                byte[] d = p.Data, b = blurred.Data;
                for (int i = 0; i < d.Length; i += 4)
                    for (int c = 0; c < 3; c++)
                        d[i + c] = Clamp(d[i + c] - b[i + c] + 128);
            });
        }

        public static void AddNoise(Pixels px, int amount, bool monochrome, bool gaussian, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                byte[] d = p.Data;
                float k = amount / 100f * 255f;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    if (monochrome)
                    {
                        float n = (gaussian ? Gauss() : (float)(Rng.NextDouble() * 2 - 1)) * k;
                        d[i] = Clamp(d[i] + n); d[i + 1] = Clamp(d[i + 1] + n); d[i + 2] = Clamp(d[i + 2] + n);
                    }
                    else
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            float n = (gaussian ? Gauss() : (float)(Rng.NextDouble() * 2 - 1)) * k;
                            d[i + c] = Clamp(d[i + c] + n);
                        }
                    }
                }
            });
        }

        static float Gauss()
        {
            double u1 = 1.0 - Rng.NextDouble(), u2 = Rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2) * 0.5);
        }

        /// <summary>Median (noise reduction) with a running histogram per channel.</summary>
        public static void Median(Pixels px, int radius, byte[] mask)
        {
            radius = Math.Max(1, Math.Min(8, radius));
            Masked(px, mask, p =>
            {
                int w = p.Width, h = p.Height, stride = p.Stride;
                byte[] src = (byte[])p.Data.Clone();
                byte[] dst = p.Data;
                int win = (radius * 2 + 1) * (radius * 2 + 1);
                int half = win / 2;
                var hist = new int[3][] { new int[256], new int[256], new int[256] };
                for (int y = 0; y < h; y++)
                {
                    Array.Clear(hist[0], 0, 256); Array.Clear(hist[1], 0, 256); Array.Clear(hist[2], 0, 256);
                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        int sy = Math.Max(0, Math.Min(h - 1, y + ky));
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int sx = Math.Max(0, Math.Min(w - 1, kx));
                            int i = sy * stride + sx * 4;
                            hist[0][src[i]]++; hist[1][src[i + 1]]++; hist[2][src[i + 2]]++;
                        }
                    }
                    for (int x = 0; x < w; x++)
                    {
                        int o = y * stride + x * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            int acc = 0, v = 0;
                            int[] hc = hist[c];
                            for (; v < 256; v++) { acc += hc[v]; if (acc > half) break; }
                            dst[o + c] = (byte)v;
                        }
                        // slide: drop column x-radius, add column x+radius+1
                        int outX = Math.Max(0, Math.Min(w - 1, x - radius));
                        int inX = Math.Max(0, Math.Min(w - 1, x + radius + 1));
                        for (int ky = -radius; ky <= radius; ky++)
                        {
                            int sy = Math.Max(0, Math.Min(h - 1, y + ky));
                            int io = sy * stride + outX * 4, ii = sy * stride + inX * 4;
                            hist[0][src[io]]--; hist[1][src[io + 1]]--; hist[2][src[io + 2]]--;
                            hist[0][src[ii]]++; hist[1][src[ii + 1]]++; hist[2][src[ii + 2]]++;
                        }
                    }
                }
            });
        }

        public static void Mosaic(Pixels px, int cell, byte[] mask)
        {
            cell = Math.Max(2, cell);
            Masked(px, mask, p =>
            {
                int w = p.Width, h = p.Height, stride = p.Stride;
                byte[] d = p.Data;
                for (int by = 0; by < h; by += cell)
                {
                    for (int bx = 0; bx < w; bx += cell)
                    {
                        int sb = 0, sg = 0, sr = 0, sa = 0, n = 0;
                        int y1 = Math.Min(h, by + cell), x1 = Math.Min(w, bx + cell);
                        for (int y = by; y < y1; y++)
                            for (int x = bx; x < x1; x++)
                            {
                                int i = y * stride + x * 4;
                                sb += d[i]; sg += d[i + 1]; sr += d[i + 2]; sa += d[i + 3]; n++;
                            }
                        byte vb = (byte)(sb / n), vg = (byte)(sg / n), vr = (byte)(sr / n), va = (byte)(sa / n);
                        for (int y = by; y < y1; y++)
                            for (int x = bx; x < x1; x++)
                            {
                                int i = y * stride + x * 4;
                                d[i] = vb; d[i + 1] = vg; d[i + 2] = vr; d[i + 3] = va;
                            }
                    }
                }
            });
        }

        public static void Emboss(Pixels px, float angleDeg, int height, int amount, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                int w = p.Width, h = p.Height, stride = p.Stride;
                byte[] src = (byte[])p.Data.Clone();
                byte[] dst = p.Data;
                double dx = Math.Cos(angleDeg * Math.PI / 180) * height, dy = -Math.Sin(angleDeg * Math.PI / 180) * height;
                int ox = (int)Math.Round(dx), oy = (int)Math.Round(dy);
                float k = amount / 100f;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int ax = Math.Max(0, Math.Min(w - 1, x + ox)), ay = Math.Max(0, Math.Min(h - 1, y + oy));
                        int bx = Math.Max(0, Math.Min(w - 1, x - ox)), by = Math.Max(0, Math.Min(h - 1, y - oy));
                        int ia = ay * stride + ax * 4, ib = by * stride + bx * 4;
                        float la = Luma(src[ia + 2], src[ia + 1], src[ia]);
                        float lb = Luma(src[ib + 2], src[ib + 1], src[ib]);
                        byte v = Clamp(128 + (lb - la) * k);
                        int o = y * stride + x * 4;
                        dst[o] = dst[o + 1] = dst[o + 2] = v;
                    }
                }
            });
        }

        public static void FindEdges(Pixels px, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                int w = p.Width, h = p.Height, stride = p.Stride;
                byte[] src = (byte[])p.Data.Clone();
                byte[] dst = p.Data;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            float gx = 0, gy = 0;
                            for (int ky = -1; ky <= 1; ky++)
                            {
                                int sy = Math.Max(0, Math.Min(h - 1, y + ky));
                                for (int kx = -1; kx <= 1; kx++)
                                {
                                    int sx = Math.Max(0, Math.Min(w - 1, x + kx));
                                    float v = src[sy * stride + sx * 4 + c];
                                    gx += v * kx * (ky == 0 ? 2 : 1);
                                    gy += v * ky * (kx == 0 ? 2 : 1);
                                }
                            }
                            float mag = (float)Math.Sqrt(gx * gx + gy * gy) / 4f;
                            dst[y * stride + x * 4 + c] = Clamp(255 - mag);
                        }
                    }
                }
            });
        }

        /// <summary>Vignette: darkens (or lightens, negative amount) toward the corners.</summary>
        public static void Vignette(Pixels px, int amount, int midpoint, byte[] mask)
        {
            Masked(px, mask, p =>
            {
                int w = p.Width, h = p.Height, stride = p.Stride;
                byte[] d = p.Data;
                float cx = w / 2f, cy = h / 2f;
                float maxR = (float)Math.Sqrt(cx * cx + cy * cy);
                float mid = Math.Max(1, midpoint) / 100f;
                float k = amount / 100f;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float r = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxR;
                        float t = Clamp01((r - mid * 0.5f) / Math.Max(0.01f, 1 - mid * 0.5f));
                        t = t * t * (3 - 2 * t);
                        float f = 1 - k * t;
                        int i = y * stride + x * 4;
                        d[i] = Clamp(d[i] * f); d[i + 1] = Clamp(d[i + 1] * f); d[i + 2] = Clamp(d[i + 2] * f);
                    }
                }
            });
        }

        public static void Solarize(Pixels px, byte[] mask)
        {
            var lut = new byte[256];
            for (int i = 0; i < 256; i++) lut[i] = (byte)(i < 128 ? i : 255 - i);
            Masked(px, mask, p => ApplyLut(p, lut));
        }

        // ============================================================== flood fill

        /// <summary>
        /// The Magic Wand / Paint Bucket mask: every pixel within <paramref name="tolerance"/>
        /// (0..255, max channel difference) of the seed colour - flood-filled from the seed
        /// when contiguous, or anywhere in the image when not.
        /// </summary>
        public static byte[] FloodMask(Pixels p, int seedX, int seedY, int tolerance, bool contiguous)
        {
            int w = p.Width, h = p.Height, stride = p.Stride;
            var mask = new byte[w * h];
            if (seedX < 0 || seedY < 0 || seedX >= w || seedY >= h) return mask;
            byte[] d = p.Data;
            int si = seedY * stride + seedX * 4;
            int sb = d[si], sg = d[si + 1], sr = d[si + 2], sa = d[si + 3];

            if (!contiguous)
            {
                for (int y = 0, k = 0; y < h; y++)
                    for (int x = 0; x < w; x++, k++)
                    {
                        int i = y * stride + x * 4;
                        if (Within(d, i, sb, sg, sr, sa, tolerance)) mask[k] = 255;
                    }
                return mask;
            }

            // scanline flood fill
            var stack = new Stack<int>();
            stack.Push(seedY * w + seedX);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                int y = k / w, x = k % w;
                if (mask[k] != 0) continue;
                int i = y * stride + x * 4;
                if (!Within(d, i, sb, sg, sr, sa, tolerance)) continue;
                int x0 = x, x1 = x;
                while (x0 > 0 && mask[y * w + x0 - 1] == 0 && Within(d, y * stride + (x0 - 1) * 4, sb, sg, sr, sa, tolerance)) x0--;
                while (x1 < w - 1 && mask[y * w + x1 + 1] == 0 && Within(d, y * stride + (x1 + 1) * 4, sb, sg, sr, sa, tolerance)) x1++;
                for (int xx = x0; xx <= x1; xx++)
                {
                    mask[y * w + xx] = 255;
                    if (y > 0 && mask[(y - 1) * w + xx] == 0) stack.Push((y - 1) * w + xx);
                    if (y < h - 1 && mask[(y + 1) * w + xx] == 0) stack.Push((y + 1) * w + xx);
                }
            }
            return mask;
        }

        static bool Within(byte[] d, int i, int b, int g, int r, int a, int tol)
        {
            int db = d[i] - b; if (db < 0) db = -db;
            int dg = d[i + 1] - g; if (dg < 0) dg = -dg;
            int dr = d[i + 2] - r; if (dr < 0) dr = -dr;
            int da = d[i + 3] - a; if (da < 0) da = -da;
            int m = db > dg ? db : dg;
            if (dr > m) m = dr;
            if (da > m) m = da;
            return m <= tol;
        }

        // =================================================================== warp

        /// <summary>
        /// Draws <paramref name="src"/> into the quadrilateral <paramref name="quad"/> (TL, TR,
        /// BR, BL in canvas pixels) with a projective warp - the engine behind Free Transform's
        /// Distort and Perspective. Returns a bitmap covering the quad's bounding box and the
        /// box's origin.
        /// </summary>
        public static Bitmap WarpQuad(Bitmap src, PointF[] quad, out Point origin, bool fast)
        {
            float minX = Math.Min(Math.Min(quad[0].X, quad[1].X), Math.Min(quad[2].X, quad[3].X));
            float minY = Math.Min(Math.Min(quad[0].Y, quad[1].Y), Math.Min(quad[2].Y, quad[3].Y));
            float maxX = Math.Max(Math.Max(quad[0].X, quad[1].X), Math.Max(quad[2].X, quad[3].X));
            float maxY = Math.Max(Math.Max(quad[0].Y, quad[1].Y), Math.Max(quad[2].Y, quad[3].Y));
            origin = new Point((int)Math.Floor(minX), (int)Math.Floor(minY));
            int w = Math.Max(1, (int)Math.Ceiling(maxX) - origin.X + 1);
            int h = Math.Max(1, (int)Math.Ceiling(maxY) - origin.Y + 1);
            if ((long)w * h > 40_000_000L) { w = Math.Min(w, 6000); h = Math.Min(h, 6000); }

            Pixels sp = Pixels.From(src);
            double[] H = SquareToQuad(quad);
            double[] inv = Invert3(H);
            var outp = new Pixels(w, h);
            byte[] o = outp.Data, s = sp.Data;
            int sw = sp.Width, sh = sp.Height, sstride = sp.Stride;
            int step = fast ? 2 : 1;
            for (int y = 0; y < h; y += step)
            {
                double py = origin.Y + y + 0.5;
                for (int x = 0; x < w; x += step)
                {
                    double px = origin.X + x + 0.5;
                    double den = inv[6] * px + inv[7] * py + inv[8];
                    if (Math.Abs(den) < 1e-9) continue;
                    double u = (inv[0] * px + inv[1] * py + inv[2]) / den;
                    double v = (inv[3] * px + inv[4] * py + inv[5]) / den;
                    if (u < 0 || v < 0 || u >= 1 || v >= 1) continue;
                    double fx = u * sw - 0.5, fy = v * sh - 0.5;
                    int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                    float tx = (float)(fx - x0), ty = (float)(fy - y0);
                    int x1 = x0 + 1, y1 = y0 + 1;
                    if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0;
                    if (x1 >= sw) x1 = sw - 1; if (y1 >= sh) y1 = sh - 1;
                    if (x0 >= sw) x0 = sw - 1; if (y0 >= sh) y0 = sh - 1;
                    int i00 = y0 * sstride + x0 * 4, i10 = y0 * sstride + x1 * 4, i01 = y1 * sstride + x0 * 4, i11 = y1 * sstride + x1 * 4;
                    int oi = y * outp.Stride + x * 4;
                    // premultiplied bilinear so transparent neighbours do not darken the edge
                    float a00 = s[i00 + 3], a10 = s[i10 + 3], a01 = s[i01 + 3], a11 = s[i11 + 3];
                    float wa = (1 - tx) * (1 - ty), wb = tx * (1 - ty), wc = (1 - tx) * ty, wd = tx * ty;
                    float a = a00 * wa + a10 * wb + a01 * wc + a11 * wd;
                    if (a <= 0) continue;
                    for (int c = 0; c < 3; c++)
                    {
                        float v2 = (s[i00 + c] * a00 * wa + s[i10 + c] * a10 * wb + s[i01 + c] * a01 * wc + s[i11 + c] * a11 * wd) / a;
                        o[oi + c] = Clamp(v2);
                    }
                    o[oi + 3] = Clamp(a);
                    if (step == 2)
                    {
                        // fast preview: fill the 2x2 block
                        if (x + 1 < w) { o[oi + 4] = o[oi]; o[oi + 5] = o[oi + 1]; o[oi + 6] = o[oi + 2]; o[oi + 7] = o[oi + 3]; }
                        if (y + 1 < h)
                        {
                            int oj = oi + outp.Stride;
                            o[oj] = o[oi]; o[oj + 1] = o[oi + 1]; o[oj + 2] = o[oi + 2]; o[oj + 3] = o[oi + 3];
                            if (x + 1 < w) { o[oj + 4] = o[oi]; o[oj + 5] = o[oi + 1]; o[oj + 6] = o[oi + 2]; o[oj + 7] = o[oi + 3]; }
                        }
                    }
                }
            }
            return outp.ToBitmap();
        }

        public static double[] SquareToQuadPublic(PointF[] q) { return SquareToQuad(q); }

        /// <summary>Homography taking the unit square (0,0)-(1,1) to the quad TL,TR,BR,BL (Heckbert).</summary>
        static double[] SquareToQuad(PointF[] q)
        {
            double x0 = q[0].X, y0 = q[0].Y, x1 = q[1].X, y1 = q[1].Y, x2 = q[2].X, y2 = q[2].Y, x3 = q[3].X, y3 = q[3].Y;
            double dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
            double dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
            double g, hh;
            if (Math.Abs(dx3) < 1e-9 && Math.Abs(dy3) < 1e-9)
            {
                g = 0; hh = 0;
                return new[] { x1 - x0, x2 - x1, x0, y1 - y0, y2 - y1, y0, 0, 0, 1 };
            }
            double den = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(den) < 1e-12) den = 1e-12;
            g = (dx3 * dy2 - dx2 * dy3) / den;
            hh = (dx1 * dy3 - dx3 * dy1) / den;
            double a = x1 - x0 + g * x1, b = x3 - x0 + hh * x3, c = x0;
            double d = y1 - y0 + g * y1, e = y3 - y0 + hh * y3, f = y0;
            return new[] { a, b, c, d, e, f, g, hh, 1 };
        }

        static double[] Invert3(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-12) det = 1e-12;
            return new[]
            {
                A / det, -(b * i - c * h) / det, (b * f - c * e) / det,
                B / det, (a * i - c * g) / det, -(a * f - c * d) / det,
                C / det, -(a * h - b * g) / det, (a * e - b * d) / det
            };
        }

        // =============================================================== painting

        /// <summary>
        /// Stamps a soft round dab into an 8-bit stroke mask (over-compositing, so
        /// overlapping soft edges build up the way brush spacing does in Photoshop).
        /// hardness 0..100: the fraction of the radius that is fully opaque.
        /// </summary>
        public static void DabMask(byte[] mask, int w, int h, float cx, float cy, float radius, int hardness, int flow)
        {
            int x0 = Math.Max(0, (int)Math.Floor(cx - radius) - 1);
            int x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + radius) + 1);
            int y0 = Math.Max(0, (int)Math.Floor(cy - radius) - 1);
            int y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + radius) + 1);
            float hard = Math.Max(0, Math.Min(1, hardness / 100f));
            float inner = radius * hard;
            float flowF = Math.Max(0, Math.Min(1, flow / 100f));
            for (int y = y0; y <= y1; y++)
            {
                float dy = y + 0.5f - cy;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (dist <= inner) a = 1;
                    else if (dist >= radius + 0.5f) continue;
                    else
                    {
                        float t = (dist - inner) / Math.Max(0.001f, radius + 0.5f - inner);
                        a = 1 - t;
                        a = a * a * (3 - 2 * a);   // smooth falloff
                    }
                    a *= flowF;
                    int k = y * w + x;
                    int cur = mask[k];
                    int add = (int)(a * 255);
                    mask[k] = (byte)(cur + add - cur * add / 255);
                }
            }
        }

        /// <summary>Colours <paramref name="target"/> through a stroke mask (times opacity and the selection).</summary>
        public static void PaintColor(Pixels target, Pixels original, byte[] stroke, Color color, float opacity, byte[] selection, Rectangle roi)
        {
            int w = target.Width, stride = target.Stride;
            byte[] t = target.Data, o = original.Data;
            int cr = color.R, cg = color.G, cb = color.B;
            for (int y = roi.Top; y < roi.Bottom; y++)
            {
                for (int x = roi.Left; x < roi.Right; x++)
                {
                    int k = y * w + x;
                    float a = stroke[k] / 255f * opacity;
                    if (selection != null) a *= selection[k] / 255f;
                    int i = y * stride + x * 4;
                    if (a <= 0)
                    {
                        t[i] = o[i]; t[i + 1] = o[i + 1]; t[i + 2] = o[i + 2]; t[i + 3] = o[i + 3];
                        continue;
                    }
                    // source-over of a solid colour with alpha a onto the original pixel
                    float aD = o[i + 3] / 255f;
                    float aO = a + aD * (1 - a);
                    if (aO <= 0) continue;
                    t[i] = Clamp((cb * a + o[i] * aD * (1 - a)) / aO);
                    t[i + 1] = Clamp((cg * a + o[i + 1] * aD * (1 - a)) / aO);
                    t[i + 2] = Clamp((cr * a + o[i + 2] * aD * (1 - a)) / aO);
                    t[i + 3] = Clamp(aO * 255f);
                }
            }
        }

        /// <summary>Erases through a stroke mask: alpha drops by mask x opacity.</summary>
        public static void Erase(Pixels target, Pixels original, byte[] stroke, float opacity, byte[] selection, Rectangle roi)
        {
            int w = target.Width, stride = target.Stride;
            byte[] t = target.Data, o = original.Data;
            for (int y = roi.Top; y < roi.Bottom; y++)
            {
                for (int x = roi.Left; x < roi.Right; x++)
                {
                    int k = y * w + x;
                    float a = stroke[k] / 255f * opacity;
                    if (selection != null) a *= selection[k] / 255f;
                    int i = y * stride + x * 4;
                    t[i] = o[i]; t[i + 1] = o[i + 1]; t[i + 2] = o[i + 2];
                    t[i + 3] = Clamp(o[i + 3] * (1 - a));
                }
            }
        }

        /// <summary>Clone stamp: copies pixels from (x+dx, y+dy) of a frozen source through the stroke mask.</summary>
        public static void CloneStamp(Pixels target, Pixels original, Pixels source, int dx, int dy, byte[] stroke, float opacity, byte[] selection, Rectangle roi)
        {
            int w = target.Width, h = target.Height, stride = target.Stride;
            byte[] t = target.Data, o = original.Data, s = source.Data;
            for (int y = roi.Top; y < roi.Bottom; y++)
            {
                for (int x = roi.Left; x < roi.Right; x++)
                {
                    int k = y * w + x;
                    float a = stroke[k] / 255f * opacity;
                    if (selection != null) a *= selection[k] / 255f;
                    int i = y * stride + x * 4;
                    int sx = x + dx, sy = y + dy;
                    if (a <= 0 || sx < 0 || sy < 0 || sx >= w || sy >= h)
                    {
                        t[i] = o[i]; t[i + 1] = o[i + 1]; t[i + 2] = o[i + 2]; t[i + 3] = o[i + 3];
                        continue;
                    }
                    int j = sy * stride + sx * 4;
                    float aS = s[j + 3] / 255f * a, aD = o[i + 3] / 255f;
                    float aO = aS + aD * (1 - aS);
                    if (aO <= 0) { t[i + 3] = 0; continue; }
                    t[i] = Clamp((s[j] * aS + o[i] * aD * (1 - aS)) / aO);
                    t[i + 1] = Clamp((s[j + 1] * aS + o[i + 1] * aD * (1 - aS)) / aO);
                    t[i + 2] = Clamp((s[j + 2] * aS + o[i + 2] * aD * (1 - aS)) / aO);
                    t[i + 3] = Clamp(aO * 255f);
                }
            }
        }

        /// <summary>Blur / Sharpen / Dodge / Burn brushes: mixes a filtered copy in through the stroke mask.</summary>
        public static void MixFiltered(Pixels target, Pixels original, Pixels filtered, byte[] stroke, float opacity, byte[] selection, Rectangle roi)
        {
            int w = target.Width, stride = target.Stride;
            byte[] t = target.Data, o = original.Data, f = filtered.Data;
            for (int y = roi.Top; y < roi.Bottom; y++)
            {
                for (int x = roi.Left; x < roi.Right; x++)
                {
                    int k = y * w + x;
                    float a = stroke[k] / 255f * opacity;
                    if (selection != null) a *= selection[k] / 255f;
                    int i = y * stride + x * 4;
                    for (int c = 0; c < 4; c++) t[i + c] = Clamp(o[i + c] + (f[i + c] - o[i + c]) * a);
                }
            }
        }

        /// <summary>Linear or radial gradient between two colours, written through the selection.</summary>
        public static void Gradient(Pixels target, PointF a, PointF b, Color c1, Color c2, bool radial, bool reverse, float opacity, byte[] selection)
        {
            int w = target.Width, h = target.Height, stride = target.Stride;
            byte[] t = target.Data;
            if (reverse) { Color tmp = c1; c1 = c2; c2 = tmp; }
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = Math.Max(0.0001f, dx * dx + dy * dy);
            float len = (float)Math.Sqrt(len2);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f - a.X, py = y + 0.5f - a.Y;
                    float tt = radial ? (float)Math.Sqrt(px * px + py * py) / len : (px * dx + py * dy) / len2;
                    tt = Clamp01(tt);
                    float cr = c1.R + (c2.R - c1.R) * tt, cg = c1.G + (c2.G - c1.G) * tt, cb = c1.B + (c2.B - c1.B) * tt;
                    float ca = (c1.A + (c2.A - c1.A) * tt) / 255f * opacity;
                    if (selection != null) ca *= selection[y * w + x] / 255f;
                    if (ca <= 0) continue;
                    int i = y * stride + x * 4;
                    float aD = t[i + 3] / 255f;
                    float aO = ca + aD * (1 - ca);
                    t[i] = Clamp((cb * ca + t[i] * aD * (1 - ca)) / aO);
                    t[i + 1] = Clamp((cg * ca + t[i + 1] * aD * (1 - ca)) / aO);
                    t[i + 2] = Clamp((cr * ca + t[i + 2] * aD * (1 - ca)) / aO);
                    t[i + 3] = Clamp(aO * 255f);
                }
            }
        }

        /// <summary>Fills with a solid colour through a mask (Edit &gt; Fill, Paint Bucket).</summary>
        public static void Fill(Pixels target, Color color, float opacity, byte[] mask, BlendMode mode)
        {
            if (mode == BlendMode.Normal)
            {
                int w = target.Width, h = target.Height, stride = target.Stride;
                byte[] t = target.Data;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float a = color.A / 255f * opacity;
                        if (mask != null) a *= mask[y * w + x] / 255f;
                        if (a <= 0) continue;
                        int i = y * stride + x * 4;
                        float aD = t[i + 3] / 255f, aO = a + aD * (1 - a);
                        t[i] = Clamp((color.B * a + t[i] * aD * (1 - a)) / aO);
                        t[i + 1] = Clamp((color.G * a + t[i + 1] * aD * (1 - a)) / aO);
                        t[i + 2] = Clamp((color.R * a + t[i + 2] * aD * (1 - a)) / aO);
                        t[i + 3] = Clamp(aO * 255f);
                    }
                return;
            }
            var layer = new Pixels(target.Width, target.Height);
            byte[] l = layer.Data;
            for (int y = 0, k = 0; y < target.Height; y++)
                for (int x = 0; x < target.Width; x++, k++)
                {
                    int i = y * target.Stride + x * 4;
                    l[i] = color.B; l[i + 1] = color.G; l[i + 2] = color.R;
                    l[i + 3] = (byte)(color.A * (mask == null ? 255 : mask[k]) / 255);
                }
            BlendOver(target, layer, mode, opacity);
        }

        /// <summary>The colour under a point of a bitmap, or Transparent outside it.</summary>
        public static Color Sample(Bitmap bmp, int x, int y, int size)
        {
            if (bmp == null) return Color.Transparent;
            if (size <= 1)
            {
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) return Color.Transparent;
                return bmp.GetPixel(x, y);
            }
            int half = size / 2;
            long r = 0, g = 0, b = 0, a = 0, n = 0;
            for (int yy = y - half; yy <= y + half; yy++)
                for (int xx = x - half; xx <= x + half; xx++)
                {
                    if (xx < 0 || yy < 0 || xx >= bmp.Width || yy >= bmp.Height) continue;
                    Color c = bmp.GetPixel(xx, yy);
                    r += c.R; g += c.G; b += c.B; a += c.A; n++;
                }
            if (n == 0) return Color.Transparent;
            return Color.FromArgb((int)(a / n), (int)(r / n), (int)(g / n), (int)(b / n));
        }
    }
}
