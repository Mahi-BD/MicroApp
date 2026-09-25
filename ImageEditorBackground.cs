using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MicroApp
{
    /// <summary>
    /// Remove Background: finds the subject with U²-Netp (a small salient-object network,
    /// Apache-2.0, embedded in the exe) run offline through ONNX Runtime, and turns
    /// everything else transparent. The network sees the picture at 320 × 320; its
    /// matte is scaled back up to the full size, so the edges are soft rather than
    /// hair-exact.
    /// </summary>
    static class BackgroundRemover
    {
        const int Side = 320;
        static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        static readonly float[] Std = { 0.229f, 0.224f, 0.225f };
        static InferenceSession _session;
        static readonly object _lock = new object();

        static InferenceSession Session()
        {
            lock (_lock)
            {
                if (_session != null) return _session;
                byte[] model;
                using (Stream s = typeof(BackgroundRemover).Assembly.GetManifestResourceStream("MicroApp.u2netp.onnx"))
                {
                    if (s == null) throw new InvalidOperationException("The background model is missing from this build.");
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); model = ms.ToArray(); }
                }
                try
                {
                    var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                    _session = new InferenceSession(model, opts);
                }
                catch (Exception ex) when (ex is DllNotFoundException || ex is TypeInitializationException || ex is BadImageFormatException)
                {
                    throw new InvalidOperationException(
                        "The AI runtime (onnxruntime.dll) could not start. Reinstall MicroApp, or install the " +
                        "Microsoft Visual C++ 2015-2022 Redistributable (x64).\r\n\r\n" + ex.Message);
                }
                return _session;
            }
        }

        /// <summary>
        /// Removes the background inside <paramref name="mask"/> (layer-sized, 0..255; null: the
        /// whole layer). Only the part of the layer the mask covers is shown to the network, so a
        /// selection around one object gives that object's cut-out.
        /// </summary>
        public static void Apply(Pixels px, byte[] mask)
        {
            Rectangle area = mask == null ? new Rectangle(0, 0, px.Width, px.Height) : MaskBounds(mask, px.Width, px.Height);
            if (area.Width < 2 || area.Height < 2) throw new InvalidOperationException("The selection does not cover any of this layer.");

            float[] matte = Matte(px, area);
            byte[] d = px.Data;
            for (int y = 0; y < area.Height; y++)
            {
                int py = area.Y + y;
                for (int x = 0; x < area.Width; x++)
                {
                    int pxX = area.X + x;
                    int k = mask == null ? 255 : mask[py * px.Width + pxX];
                    if (k == 0) continue;
                    int i = px.Index(pxX, py);
                    float keep = matte[y * area.Width + x];
                    // inside the selection the matte decides; a feathered edge blends towards "unchanged"
                    float factor = 1f - (k / 255f) * (1f - keep);
                    d[i + 3] = (byte)Math.Round(d[i + 3] * factor);
                }
            }
        }

        static Rectangle MaskBounds(byte[] mask, int w, int h)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (mask[row + x] == 0) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            }
            return x1 < 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        /// <summary>The foreground probability (0..1) of every pixel of <paramref name="area"/>.</summary>
        static float[] Matte(Pixels px, Rectangle area)
        {
            // 1. the area, flattened over white (transparent pixels carry no usable colour), at 320 × 320
            var input = new DenseTensor<float>(new[] { 1, 3, Side, Side });
            float[] rgb = ResampleRgb(px, area, Side, Side);
            float max = 1e-6f;
            for (int i = 0; i < rgb.Length; i++) if (rgb[i] > max) max = rgb[i];
            for (int y = 0; y < Side; y++)
                for (int x = 0; x < Side; x++)
                {
                    int i = (y * Side + x) * 3;
                    for (int c = 0; c < 3; c++)
                        input[0, c, y, x] = (rgb[i + c] / max - Mean[c]) / Std[c];
                }

            // 2. the network's finest side output (the first one)
            float[] pred;
            InferenceSession s = Session();
            string inputName = s.InputMetadata.Keys.First();
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
                   s.Run(new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, input) }))
            {
                pred = results.First().AsEnumerable<float>().ToArray();
            }

            // 3. stretch to 0..1, clean up the faint haze and the near-certain core, then scale up
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float v in pred) { if (v < lo) lo = v; if (v > hi) hi = v; }
            float span = Math.Max(1e-6f, hi - lo);
            for (int i = 0; i < pred.Length; i++)
            {
                float v = (pred[i] - lo) / span;
                v = (v - 0.12f) / (0.88f - 0.12f);
                pred[i] = v < 0 ? 0 : v > 1 ? 1 : v;
            }
            return Upscale(pred, Side, Side, area.Width, area.Height);
        }

        /// <summary>Area-averaged (box) resample of the RGB of a region, over white, to w × h floats 0..255.</summary>
        static float[] ResampleRgb(Pixels px, Rectangle area, int w, int h)
        {
            var sum = new float[w * h * 3];
            var count = new float[w * h];
            byte[] d = px.Data;
            float sx = (float)w / area.Width, sy = (float)h / area.Height;
            for (int y = 0; y < area.Height; y++)
            {
                int ty = Math.Min(h - 1, (int)(y * sy));
                for (int x = 0; x < area.Width; x++)
                {
                    int tx = Math.Min(w - 1, (int)(x * sx));
                    int i = px.Index(area.X + x, area.Y + y);
                    float a = d[i + 3] / 255f;
                    int t = ty * w + tx;
                    sum[t * 3] += d[i + 2] * a + 255 * (1 - a);       // R
                    sum[t * 3 + 1] += d[i + 1] * a + 255 * (1 - a);   // G
                    sum[t * 3 + 2] += d[i] * a + 255 * (1 - a);       // B
                    count[t]++;
                }
            }
            // an area smaller than 320 on a side leaves gaps: fill them from the nearest source pixel
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int t = y * w + x;
                    if (count[t] > 0) { float n = count[t]; sum[t * 3] /= n; sum[t * 3 + 1] /= n; sum[t * 3 + 2] /= n; continue; }
                    int srcX = area.X + Math.Min(area.Width - 1, (int)((x + 0.5f) / sx));
                    int srcY = area.Y + Math.Min(area.Height - 1, (int)((y + 0.5f) / sy));
                    int i = px.Index(srcX, srcY);
                    float a = d[i + 3] / 255f;
                    sum[t * 3] = d[i + 2] * a + 255 * (1 - a);
                    sum[t * 3 + 1] = d[i + 1] * a + 255 * (1 - a);
                    sum[t * 3 + 2] = d[i] * a + 255 * (1 - a);
                }
            return sum;
        }

        /// <summary>Bilinear scale of a single-channel map (pixel centres aligned).</summary>
        static float[] Upscale(float[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new float[dw * dh];
            float fx = (float)sw / dw, fy = (float)sh / dh;
            for (int y = 0; y < dh; y++)
            {
                float syf = Math.Max(0, Math.Min(sh - 1, (y + 0.5f) * fy - 0.5f));
                int y0 = (int)syf, y1 = Math.Min(sh - 1, y0 + 1);
                float ty = syf - y0;
                for (int x = 0; x < dw; x++)
                {
                    float sxf = Math.Max(0, Math.Min(sw - 1, (x + 0.5f) * fx - 0.5f));
                    int x0 = (int)sxf, x1 = Math.Min(sw - 1, x0 + 1);
                    float tx = sxf - x0;
                    float top = src[y0 * sw + x0] * (1 - tx) + src[y0 * sw + x1] * tx;
                    float bottom = src[y1 * sw + x0] * (1 - tx) + src[y1 * sw + x1] * tx;
                    dst[y * dw + x] = top * (1 - ty) + bottom * ty;
                }
            }
            return dst;
        }
    }
}
