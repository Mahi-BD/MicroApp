using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MicroApp
{
    /// <summary>How a new marquee / lasso / wand combines with the selection that is already there.</summary>
    enum SelectionMode { New, Add, Subtract, Intersect }

    /// <summary>
    /// A selection, Photoshop style: an 8-bit mask over the canvas (255 = fully selected,
    /// in-between values come from feathering) plus the marching-ants outline traced along
    /// the pixel edges of the mask. Selections are immutable - every operation returns a
    /// new one - so undo snapshots can share them freely.
    /// </summary>
    sealed class EditorSelection
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Mask;
        public readonly int Feather;

        Rectangle _bounds;
        bool _boundsKnown;
        GraphicsPath _outline;
        bool _empty;
        bool _emptyKnown;

        EditorSelection(byte[] mask, int width, int height, int feather)
        {
            Mask = mask;
            Width = width;
            Height = height;
            Feather = feather;
        }

        public Size Size { get { return new Size(Width, Height); } }

        public bool IsEmpty
        {
            get
            {
                if (!_emptyKnown)
                {
                    _empty = true;
                    for (int i = 0; i < Mask.Length; i++) if (Mask[i] != 0) { _empty = false; break; }
                    _emptyKnown = true;
                }
                return _empty;
            }
        }

        /// <summary>True when the whole canvas is selected at full strength.</summary>
        public bool IsAll
        {
            get
            {
                for (int i = 0; i < Mask.Length; i++) if (Mask[i] != 255) return false;
                return true;
            }
        }

        /// <summary>The box around every selected pixel (mask &gt; 0).</summary>
        public Rectangle Bounds
        {
            get
            {
                if (!_boundsKnown)
                {
                    int minX = Width, minY = Height, maxX = -1, maxY = -1;
                    for (int y = 0; y < Height; y++)
                    {
                        int row = y * Width;
                        for (int x = 0; x < Width; x++)
                        {
                            if (Mask[row + x] == 0) continue;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                    _bounds = maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                    _boundsKnown = true;
                }
                return _bounds;
            }
        }

        public bool Contains(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height && Mask[y * Width + x] >= 128;
        }

        // ============================================================== creation

        public static EditorSelection All(Size canvas)
        {
            var m = new byte[canvas.Width * canvas.Height];
            for (int i = 0; i < m.Length; i++) m[i] = 255;
            return new EditorSelection(m, canvas.Width, canvas.Height, 0);
        }

        public static EditorSelection FromMask(byte[] mask, Size canvas, int feather)
        {
            return new EditorSelection(mask, canvas.Width, canvas.Height, feather);
        }

        /// <summary>Rasterises a path (canvas coordinates) into a selection; antialiased edges for curves and lassos.</summary>
        public static EditorSelection FromPath(GraphicsPath path, Size canvas, bool antialias)
        {
            var m = new byte[canvas.Width * canvas.Height];
            if (path == null || path.PointCount == 0) return new EditorSelection(m, canvas.Width, canvas.Height, 0);
            using (var bmp = new Bitmap(Math.Max(1, canvas.Width), Math.Max(1, canvas.Height), PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = antialias ? SmoothingMode.AntiAlias : SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.FillPath(Brushes.White, path);
                }
                Pixels p = Pixels.From(bmp);
                byte[] d = p.Data;
                for (int i = 0, k = 0; k < m.Length; i += 4, k++) m[k] = d[i + 3];
            }
            return new EditorSelection(m, canvas.Width, canvas.Height, 0);
        }

        public static EditorSelection FromRect(RectangleF r, Size canvas)
        {
            using (var path = new GraphicsPath())
            {
                path.AddRectangle(RectangleF.FromLTRB((float)Math.Round(r.Left), (float)Math.Round(r.Top),
                                                      (float)Math.Round(r.Right), (float)Math.Round(r.Bottom)));
                return FromPath(path, canvas, false);
            }
        }

        public static EditorSelection FromEllipse(RectangleF r, Size canvas)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r);
                return FromPath(path, canvas, true);
            }
        }

        public static EditorSelection FromPolygon(IList<PointF> pts, Size canvas)
        {
            if (pts == null || pts.Count < 3) return new EditorSelection(new byte[canvas.Width * canvas.Height], canvas.Width, canvas.Height, 0);
            using (var path = new GraphicsPath())
            {
                var arr = new PointF[pts.Count];
                pts.CopyTo(arr, 0);
                path.AddPolygon(arr);
                return FromPath(path, canvas, true);
            }
        }

        // ============================================================ set algebra

        public EditorSelection Combine(EditorSelection other, SelectionMode mode)
        {
            if (other == null) return this;
            if (mode == SelectionMode.New || IsEmpty && mode == SelectionMode.Add) return other;
            var m = new byte[Mask.Length];
            byte[] a = Mask, b = other.Mask;
            switch (mode)
            {
                case SelectionMode.Add:
                    for (int i = 0; i < m.Length; i++) m[i] = a[i] > b[i] ? a[i] : b[i];
                    break;
                case SelectionMode.Subtract:
                    for (int i = 0; i < m.Length; i++) { int v = 255 - b[i]; m[i] = a[i] < v ? a[i] : (byte)v; }
                    break;
                case SelectionMode.Intersect:
                    for (int i = 0; i < m.Length; i++) m[i] = a[i] < b[i] ? a[i] : b[i];
                    break;
            }
            return new EditorSelection(m, Width, Height, Math.Max(Feather, other.Feather));
        }

        public EditorSelection Invert()
        {
            var m = new byte[Mask.Length];
            for (int i = 0; i < m.Length; i++) m[i] = (byte)(255 - Mask[i]);
            return new EditorSelection(m, Width, Height, Feather);
        }

        /// <summary>Select &gt; Modify &gt; Feather: softens the edge over the given radius.</summary>
        public EditorSelection Feathered(int radius)
        {
            if (radius < 1) return this;
            var m = (byte[])Mask.Clone();
            EditorRender.BlurMask(m, Width, Height, radius);
            return new EditorSelection(m, Width, Height, radius);
        }

        /// <summary>Select &gt; Modify &gt; Expand.</summary>
        public EditorSelection Expand(int px)
        {
            if (px < 1) return this;
            byte[] dist = DistanceOutside(Hard(), Width, Height, px + 2);
            var m = new byte[Mask.Length];
            for (int i = 0; i < m.Length; i++) m[i] = Mask[i] >= 128 ? (byte)255 : dist[i] <= px ? (byte)255 : (byte)0;
            return new EditorSelection(m, Width, Height, 0);
        }

        /// <summary>Select &gt; Modify &gt; Contract.</summary>
        public EditorSelection Contract(int px)
        {
            if (px < 1) return this;
            byte[] dist = DistanceInside(Hard(), Width, Height, px + 2);
            var m = new byte[Mask.Length];
            for (int i = 0; i < m.Length; i++) m[i] = Mask[i] >= 128 && dist[i] > px ? (byte)255 : (byte)0;
            return new EditorSelection(m, Width, Height, 0);
        }

        /// <summary>Select &gt; Modify &gt; Border: a band of the given width straddling the edge.</summary>
        public EditorSelection Border(int px)
        {
            if (px < 1) return this;
            byte[] hard = Hard();
            byte[] dOut = DistanceOutside(hard, Width, Height, px + 2);
            byte[] dIn = DistanceInside(hard, Width, Height, px + 2);
            var m = new byte[Mask.Length];
            int half = Math.Max(1, px / 2);
            for (int i = 0; i < m.Length; i++)
            {
                int d = hard[i] > 0 ? dIn[i] : dOut[i];
                m[i] = d <= half ? (byte)255 : (byte)0;
            }
            return new EditorSelection(m, Width, Height, 0);
        }

        /// <summary>Select &gt; Modify &gt; Smooth: rounds off jaggies and drops specks.</summary>
        public EditorSelection Smooth(int px)
        {
            if (px < 1) return this;
            var m = (byte[])Mask.Clone();
            EditorRender.BoxBlurMask(m, Width, Height, px);
            for (int i = 0; i < m.Length; i++) m[i] = m[i] >= 128 ? (byte)255 : (byte)0;
            return new EditorSelection(m, Width, Height, 0);
        }

        /// <summary>The selection moved by whole pixels (arrow keys with a marquee tool).</summary>
        public EditorSelection Offset(int dx, int dy)
        {
            var m = new byte[Mask.Length];
            for (int y = 0; y < Height; y++)
            {
                int sy = y - dy;
                if (sy < 0 || sy >= Height) continue;
                for (int x = 0; x < Width; x++)
                {
                    int sx = x - dx;
                    if (sx < 0 || sx >= Width) continue;
                    m[y * Width + x] = Mask[sy * Width + sx];
                }
            }
            return new EditorSelection(m, Width, Height, Feather);
        }

        /// <summary>Same mask on a canvas of a new size (after a crop: shift by the crop origin).</summary>
        public EditorSelection Rebase(Size newCanvas, int originX, int originY)
        {
            var m = new byte[newCanvas.Width * newCanvas.Height];
            for (int y = 0; y < newCanvas.Height; y++)
            {
                int sy = y + originY;
                if (sy < 0 || sy >= Height) continue;
                for (int x = 0; x < newCanvas.Width; x++)
                {
                    int sx = x + originX;
                    if (sx < 0 || sx >= Width) continue;
                    m[y * newCanvas.Width + x] = Mask[sy * Width + sx];
                }
            }
            return new EditorSelection(m, newCanvas.Width, newCanvas.Height, Feather);
        }

        byte[] Hard()
        {
            var h = new byte[Mask.Length];
            for (int i = 0; i < h.Length; i++) h[i] = Mask[i] >= 128 ? (byte)255 : (byte)0;
            return h;
        }

        // ============================================================= distances

        /// <summary>
        /// For every pixel OUTSIDE the mask, the (chamfer 3-4, so near-Euclidean) distance to
        /// the nearest pixel inside; 0 inside. Capped at <paramref name="maxDist"/>.
        /// </summary>
        public static byte[] DistanceOutside(byte[] mask, int w, int h, int maxDist)
        {
            return Chamfer(mask, w, h, maxDist, true);
        }

        /// <summary>For every pixel INSIDE the mask, the distance to the nearest pixel outside; 0 outside.</summary>
        public static byte[] DistanceInside(byte[] mask, int w, int h, int maxDist)
        {
            return Chamfer(mask, w, h, maxDist, false);
        }

        static byte[] Chamfer(byte[] mask, int w, int h, int maxDist, bool outside)
        {
            int inf = (maxDist + 2) * 3;
            var d = new int[w * h];
            for (int i = 0; i < d.Length; i++)
            {
                bool inside = mask[i] > 0;
                // seeds (distance 0) are the pixels of the side we measure FROM: for an
                // outside distance every inside pixel, for an inside distance every outside one
                d[i] = inside == outside ? 0 : inf;
            }
            // the image border counts as "outside" for inside distances
            // forward pass
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int v = d[i];
                    if (v == 0) continue;
                    if (x > 0) v = Math.Min(v, d[i - 1] + 3);
                    if (y > 0)
                    {
                        v = Math.Min(v, d[i - w] + 3);
                        if (x > 0) v = Math.Min(v, d[i - w - 1] + 4);
                        if (x < w - 1) v = Math.Min(v, d[i - w + 1] + 4);
                    }
                    else if (!outside) v = Math.Min(v, 3);
                    if (x == 0 && !outside) v = Math.Min(v, 3);
                    d[i] = v;
                }
            }
            // backward pass
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x;
                    int v = d[i];
                    if (v == 0) continue;
                    if (x < w - 1) v = Math.Min(v, d[i + 1] + 3);
                    if (y < h - 1)
                    {
                        v = Math.Min(v, d[i + w] + 3);
                        if (x < w - 1) v = Math.Min(v, d[i + w + 1] + 4);
                        if (x > 0) v = Math.Min(v, d[i + w - 1] + 4);
                    }
                    else if (!outside) v = Math.Min(v, 3);
                    if (x == w - 1 && !outside) v = Math.Min(v, 3);
                    d[i] = v;
                }
            }
            var r = new byte[w * h];
            for (int i = 0; i < r.Length; i++)
            {
                int v = (d[i] + 1) / 3;
                r[i] = v > 255 ? (byte)255 : (byte)v;
            }
            return r;
        }

        // ============================================================== outline

        /// <summary>
        /// The marching-ants path: closed polygons along the pixel edges of the (hard)
        /// mask, in canvas pixel coordinates. Built once per selection.
        /// </summary>
        public GraphicsPath Outline
        {
            get
            {
                if (_outline == null) _outline = TraceOutline();
                return _outline;
            }
        }

        GraphicsPath TraceOutline()
        {
            var path = new GraphicsPath();
            int w = Width, h = Height;
            byte[] m = Mask;
            // directed boundary edges, clockwise around filled pixels, keyed by start vertex
            // vertex (x, y) with 0 <= x <= w, 0 <= y <= h -> key y * (w + 1) + x
            int vw = w + 1;
            var starts = new Dictionary<int, List<int>>();
            var startOf = new List<int>();
            var ends = new List<int>();
            var used = new List<bool>();
            int edgeCount = 0;
            Action<int, int> addEdge = delegate(int from, int to)
            {
                List<int> list;
                if (!starts.TryGetValue(from, out list)) { list = new List<int>(2); starts[from] = list; }
                list.Add(edgeCount);
                startOf.Add(from);
                ends.Add(to);
                used.Add(false);
                edgeCount++;
            };
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (m[row + x] < 128) continue;
                    bool up = y > 0 && m[row - w + x] >= 128;
                    bool down = y < h - 1 && m[row + w + x] >= 128;
                    bool left = x > 0 && m[row + x - 1] >= 128;
                    bool right = x < w - 1 && m[row + x + 1] >= 128;
                    if (!up) addEdge(y * vw + x, y * vw + x + 1);                 // top: left → right
                    if (!right) addEdge(y * vw + x + 1, (y + 1) * vw + x + 1);    // right: top → bottom
                    if (!down) addEdge((y + 1) * vw + x + 1, (y + 1) * vw + x);   // bottom: right → left
                    if (!left) addEdge((y + 1) * vw + x, y * vw + x);             // left: bottom → top
                    if (edgeCount > 4_000_000) { y = h; break; }   // absurdly noisy mask: stop tracing
                }
            }
            if (edgeCount == 0) return path;

            var loop = new List<PointF>();
            for (int e = 0; e < edgeCount; e++)
            {
                if (used[e]) continue;
                loop.Clear();
                // walk edge to edge until we are back at this edge's start vertex; every
                // vertex has as many outgoing as incoming edges, so the walk always closes
                int home = startOf[e];
                int cur = e;
                while (cur >= 0 && !used[cur])
                {
                    used[cur] = true;
                    int v = ends[cur];
                    loop.Add(new PointF(v % vw, v / vw));
                    if (v == home) break;
                    List<int> next;
                    cur = -1;
                    if (starts.TryGetValue(v, out next))
                    {
                        for (int k = 0; k < next.Count; k++)
                            if (!used[next[k]]) { cur = next[k]; break; }
                    }
                }
                if (loop.Count < 3) continue;
                // drop collinear points so the path stays light
                var slim = new List<PointF>(loop.Count);
                for (int i = 0; i < loop.Count; i++)
                {
                    PointF p0 = loop[(i + loop.Count - 1) % loop.Count], p1 = loop[i], p2 = loop[(i + 1) % loop.Count];
                    bool collinear = (p1.X == p0.X && p1.X == p2.X) || (p1.Y == p0.Y && p1.Y == p2.Y);
                    if (!collinear) slim.Add(p1);
                }
                if (slim.Count >= 3)
                {
                    path.StartFigure();
                    path.AddPolygon(slim.ToArray());
                    path.CloseFigure();
                }
            }
            return path;
        }

        // ============================================================ layer masks

        /// <summary>
        /// The selection in a raster layer's own bitmap pixels - the mask handed to every
        /// adjustment, filter and paint stroke. Untransformed layers are cropped straight
        /// out of the canvas mask; anything rotated, sheared, flipped or scaled is resampled.
        /// Returns null when the selection covers the layer completely.
        /// </summary>
        public byte[] MaskForLayer(RasterLayer layer)
        {
            if (layer.Image == null) return null;
            int iw = layer.Image.Width, ih = layer.Image.Height;
            bool plain = layer.RotationDeg == 0 && layer.ShearX == 0 && layer.ShearY == 0 && !layer.FlipH && !layer.FlipV &&
                         Math.Abs(layer.Bounds.Width - iw) < 0.01f && Math.Abs(layer.Bounds.Height - ih) < 0.01f &&
                         Math.Abs(layer.Bounds.X - Math.Round(layer.Bounds.X)) < 0.01f &&
                         Math.Abs(layer.Bounds.Y - Math.Round(layer.Bounds.Y)) < 0.01f;
            var m = new byte[iw * ih];
            if (plain)
            {
                int ox = (int)Math.Round(layer.Bounds.X), oy = (int)Math.Round(layer.Bounds.Y);
                bool full = true;
                for (int y = 0; y < ih; y++)
                {
                    int cy = y + oy;
                    for (int x = 0; x < iw; x++)
                    {
                        int cx = x + ox;
                        byte v = cx >= 0 && cy >= 0 && cx < Width && cy < Height ? Mask[cy * Width + cx] : (byte)0;
                        m[y * iw + x] = v;
                        if (v != 255) full = false;
                    }
                }
                return full ? null : m;
            }

            using (Bitmap canvasMask = MaskBitmap())
            using (var target = new Bitmap(iw, ih, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(target))
                using (Matrix mx = layer.GetMatrix())
                {
                    // pixel → canvas: layer matrix ∘ translate(bounds) ∘ scale(units per pixel)
                    mx.Translate(layer.Bounds.X, layer.Bounds.Y);
                    mx.Scale(1f / layer.PixelsPerUnitX, 1f / layer.PixelsPerUnitY);
                    mx.Invert();
                    g.Transform = mx;
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(canvasMask, 0, 0, Width, Height);
                }
                Pixels p = Pixels.From(target);
                bool full = true;
                for (int i = 0, k = 0; k < m.Length; i += 4, k++)
                {
                    m[k] = p.Data[i + 3];
                    if (m[k] != 255) full = false;
                }
                return full ? null : m;
            }
        }

        /// <summary>The mask as a white bitmap whose alpha is the selection.</summary>
        public Bitmap MaskBitmap()
        {
            var p = new Pixels(Width, Height);
            byte[] d = p.Data;
            for (int i = 0, k = 0; k < Mask.Length; i += 4, k++)
            {
                d[i] = d[i + 1] = d[i + 2] = 255;
                d[i + 3] = Mask[k];
            }
            return p.ToBitmap();
        }
    }
}
