using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace MicroApp
{
    /// <summary>Layer Style: the non-destructive effects hung on a layer (Photoshop's fx).</summary>
    sealed class LayerEffects
    {
        public bool DropShadow;
        public Color ShadowColor = Color.Black;
        public int ShadowOpacity = 75;         // %
        public int ShadowAngle = 120;          // degrees, Photoshop's default light
        public int ShadowDistance = 5;         // px
        public int ShadowSize = 5;             // blur px

        public bool OuterGlow;
        public Color GlowColor = Color.FromArgb(255, 255, 190);
        public int GlowOpacity = 75;
        public int GlowSize = 8;

        public bool Stroke;
        public Color StrokeColor = Color.Red;
        public int StrokeSize = 3;
        public int StrokePosition;             // 0 outside, 1 inside, 2 centre
        public int StrokeOpacity = 100;

        public bool ColorOverlay;
        public Color OverlayColor = Color.Red;
        public int OverlayOpacity = 100;

        public bool Any { get { return DropShadow || OuterGlow || Stroke || ColorOverlay; } }

        public LayerEffects Clone() { return (LayerEffects)MemberwiseClone(); }

        public string Key()
        {
            return string.Concat(DropShadow ? "s" : "-", ShadowColor.ToArgb(), ",", ShadowOpacity, ",", ShadowAngle, ",", ShadowDistance, ",", ShadowSize,
                                 OuterGlow ? "g" : "-", GlowColor.ToArgb(), ",", GlowOpacity, ",", GlowSize,
                                 Stroke ? "k" : "-", StrokeColor.ToArgb(), ",", StrokeSize, ",", StrokePosition, ",", StrokeOpacity,
                                 ColorOverlay ? "o" : "-", OverlayColor.ToArgb(), ",", OverlayOpacity);
        }
    }

    /// <summary>
    /// One layer of the image editor. Layers live in canvas coordinates: Bounds is the
    /// unrotated box the content is drawn into, and rotation, shear and flips happen around
    /// its centre, the way Photoshop transforms a layer. Clone() is used for undo snapshots -
    /// it is a shallow copy, so a RasterLayer's pixels are shared between snapshots and every
    /// destructive edit (paint, filter, adjustment) must REPLACE the bitmap, never draw into
    /// one an older snapshot can still see.
    /// </summary>
    abstract class EditorLayer
    {
        public string Name = "Layer";
        public bool Visible = true;
        public bool Locked;
        public int Opacity = 100;                 // 0..100
        public BlendMode Blend = BlendMode.Normal;
        public RectangleF Bounds;
        public float RotationDeg;
        public float ShearX;                      // Free Transform > Skew, as tangents
        public float ShearY;
        public bool FlipH;
        public bool FlipV;
        public LayerEffects Fx;                   // null: no layer style

        // render cache for layers that need the per-pixel path (blend modes / effects)
        string _cacheKey;
        Bitmap _cache;

        public PointF Center
        {
            get { return new PointF(Bounds.X + Bounds.Width / 2f, Bounds.Y + Bounds.Height / 2f); }
        }

        public abstract EditorLayer Clone();

        /// <summary>A short tag for the layers panel: "Text", "Shape", "Image".</summary>
        public abstract string KindLabel { get; }

        /// <summary>Draw the content into rect r; alpha is 0..255 from the layer opacity.</summary>
        protected abstract void DrawContent(Graphics g, RectangleF r, int alpha);

        /// <summary>Something the render cache must notice beyond geometry (bitmap identity, text...).</summary>
        protected abstract string ContentKey();

        /// <summary>True when compositing must go through the per-pixel path.</summary>
        public bool NeedsPixelPath { get { return Blend != BlendMode.Normal || (Fx != null && Fx.Any); } }

        /// <summary>Plain GDI+ draw: geometry + opacity, no blend mode, no effects.</summary>
        public void Draw(Graphics g)
        {
            Draw(g, Opacity);
        }

        public void Draw(Graphics g, int opacity)
        {
            if (!Visible || opacity <= 0 || Bounds.Width < 0.5f || Bounds.Height < 0.5f) return;
            GraphicsState state = g.Save();
            try
            {
                using (Matrix m = GetMatrix()) g.MultiplyTransform(m, MatrixOrder.Prepend);
                DrawContent(g, Bounds, opacity * 255 / 100);
            }
            finally { g.Restore(state); }
        }

        /// <summary>Layer-local → canvas transform (rotation, shear and flips about the centre).</summary>
        public Matrix GetMatrix()
        {
            Matrix m = new Matrix();
            PointF c = Center;
            m.Translate(c.X, c.Y);
            m.Rotate(RotationDeg);
            if (ShearX != 0 || ShearY != 0) m.Shear(ShearX, ShearY);
            m.Scale(FlipH ? -1 : 1, FlipV ? -1 : 1);
            m.Translate(-c.X, -c.Y);
            return m;
        }

        public PointF ToLocal(PointF canvasPt)
        {
            using (Matrix m = GetMatrix())
            {
                m.Invert();
                PointF[] pts = { canvasPt };
                m.TransformPoints(pts);
                return pts[0];
            }
        }

        public PointF ToCanvas(PointF localPt)
        {
            using (Matrix m = GetMatrix())
            {
                PointF[] pts = { localPt };
                m.TransformPoints(pts);
                return pts[0];
            }
        }

        public bool HitTest(PointF canvasPt)
        {
            PointF l = ToLocal(canvasPt);
            RectangleF r = Bounds;
            r.Inflate(2, 2);        // a couple of pixels of grace on thin layers
            return r.Contains(l);
        }

        /// <summary>The four corners of the rendered (rotated) box, in canvas coordinates: TL, TR, BR, BL.</summary>
        public PointF[] CanvasCorners()
        {
            PointF[] pts =
            {
                new PointF(Bounds.Left, Bounds.Top),
                new PointF(Bounds.Right, Bounds.Top),
                new PointF(Bounds.Right, Bounds.Bottom),
                new PointF(Bounds.Left, Bounds.Bottom)
            };
            using (Matrix m = GetMatrix()) m.TransformPoints(pts);
            return pts;
        }

        /// <summary>Axis-aligned box around the rendered layer, in canvas pixels.</summary>
        public RectangleF CanvasBox()
        {
            PointF[] c = CanvasCorners();
            float minX = c[0].X, minY = c[0].Y, maxX = c[0].X, maxY = c[0].Y;
            for (int i = 1; i < 4; i++)
            {
                if (c[i].X < minX) minX = c[i].X;
                if (c[i].Y < minY) minY = c[i].Y;
                if (c[i].X > maxX) maxX = c[i].X;
                if (c[i].Y > maxY) maxY = c[i].Y;
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        protected void CopyBaseTo(EditorLayer other)
        {
            other.Name = Name;
            other.Visible = Visible;
            other.Locked = Locked;
            other.Opacity = Opacity;
            other.Blend = Blend;
            other.Bounds = Bounds;
            other.RotationDeg = RotationDeg;
            other.ShearX = ShearX;
            other.ShearY = ShearY;
            other.FlipH = FlipH;
            other.FlipV = FlipV;
            other.Fx = Fx == null ? null : Fx.Clone();
            // the render cache stays with this layer: a clone re-renders when it needs to
        }

        protected static Color Fade(Color c, int alpha)
        {
            if (alpha >= 255) return c;
            return Color.FromArgb(c.A * alpha / 255, c.R, c.G, c.B);
        }

        /// <summary>
        /// The layer alone at full opacity, in canvas space (a transparent canvas-sized
        /// bitmap) - the input to blend modes, layer styles, Rasterize and Merge.
        /// </summary>
        public Bitmap RenderAlone(Size canvas, bool withEffects)
        {
            var bmp = new Bitmap(Math.Max(1, canvas.Width), Math.Max(1, canvas.Height), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                EditorRender.Prepare(g);
                bool vis = Visible;
                Visible = true;
                try { Draw(g, 100); }
                finally { Visible = vis; }
            }
            if (withEffects && Fx != null && Fx.Any)
            {
                Bitmap styled = EditorRender.ApplyEffects(bmp, Fx);
                bmp.Dispose();
                return styled;
            }
            return bmp;
        }

        /// <summary>
        /// Cached RenderAlone(withEffects) keyed on everything that changes the pixels, so a
        /// drag that only moves a blend-mode layer does not re-render it every frame.
        /// </summary>
        public Bitmap CachedRender(Size canvas)
        {
            string key = string.Concat(canvas.Width, "x", canvas.Height, "|", Bounds.X, ",", Bounds.Y, ",", Bounds.Width, ",", Bounds.Height,
                                       "|", RotationDeg, "|", ShearX, "|", ShearY, "|", FlipH, FlipV, "|",
                                       Fx == null ? "" : Fx.Key(), "|", ContentKey());
            if (_cache != null && key == _cacheKey) return _cache;
            if (_cache != null) _cache.Dispose();
            _cache = RenderAlone(canvas, true);
            _cacheKey = key;
            return _cache;
        }

        public void DropCache()
        {
            _cacheKey = null;
            if (_cache != null) { try { _cache.Dispose(); } catch { } _cache = null; }
        }

        /// <summary>
        /// The layer as pixels: a RasterLayer covering its rendered box, with the geometry
        /// baked in (rotation, shear and flips reset). Text and shapes become plain pixels.
        /// </summary>
        public RasterLayer Rasterize(Size canvas)
        {
            RectangleF box = CanvasBox();
            int x0 = (int)Math.Floor(box.Left), y0 = (int)Math.Floor(box.Top);
            int w = Math.Max(1, (int)Math.Ceiling(box.Right) - x0), h = Math.Max(1, (int)Math.Ceiling(box.Bottom) - y0);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                EditorRender.Prepare(g);
                g.TranslateTransform(-x0, -y0);
                bool vis = Visible;
                Visible = true;
                try { Draw(g, 100); }
                finally { Visible = vis; }
            }
            var r = new RasterLayer(bmp)
            {
                Name = Name,
                Visible = Visible,
                Locked = Locked,
                Opacity = Opacity,
                Blend = Blend,
                Fx = Fx == null ? null : Fx.Clone(),
                Bounds = new RectangleF(x0, y0, w, h)
            };
            return r;
        }
    }

    /// <summary>A bitmap layer: pasted images, opened files, assets, paint.</summary>
    sealed class RasterLayer : EditorLayer
    {
        public Bitmap Image;      // owned by the document; snapshots share the reference
        public int ContentVersion;   // bumped when Image is painted into in place (brush strokes)

        public RasterLayer(Bitmap image)
        {
            Image = image;
        }

        public override string KindLabel { get { return "Image"; } }

        public override EditorLayer Clone()
        {
            RasterLayer c = new RasterLayer(Image);
            CopyBaseTo(c);
            c.ContentVersion = ContentVersion;
            return c;
        }

        protected override string ContentKey()
        {
            return Image == null ? "null" : RuntimeHelpers.GetHashCode(Image) + ":" + ContentVersion;
        }

        /// <summary>Scale factors from layer-local canvas units to bitmap pixels.</summary>
        public float PixelsPerUnitX { get { return Image == null ? 1 : Image.Width / Math.Max(1f, Bounds.Width); } }
        public float PixelsPerUnitY { get { return Image == null ? 1 : Image.Height / Math.Max(1f, Bounds.Height); } }

        /// <summary>Canvas point → bitmap pixel coordinates (may be outside the bitmap).</summary>
        public PointF ToPixel(PointF canvasPt)
        {
            PointF l = ToLocal(canvasPt);
            return new PointF((l.X - Bounds.X) * PixelsPerUnitX, (l.Y - Bounds.Y) * PixelsPerUnitY);
        }

        protected override void DrawContent(Graphics g, RectangleF r, int alpha)
        {
            if (Image == null) return;
            RectangleF src;
            try { src = new RectangleF(0, 0, Image.Width, Image.Height); }
            catch (ArgumentException ex)
            {
                // a disposed bitmap: a bug upstream, but the picture must keep painting
                Program.LogError("layer '" + Name + "' has a disposed bitmap", ex);
                Image = null;
                return;
            }
            PointF[] dest =
            {
                new PointF(r.Left, r.Top),
                new PointF(r.Right, r.Top),
                new PointF(r.Left, r.Bottom)
            };
            if (alpha >= 255)
            {
                g.DrawImage(Image, dest, src, GraphicsUnit.Pixel);
            }
            else
            {
                using (ImageAttributes attrs = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = alpha / 255f;
                    attrs.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    g.DrawImage(Image, dest, src, GraphicsUnit.Pixel, attrs);
                }
            }
        }
    }

    /// <summary>
    /// A text layer. Bounds is the wrap box; the style knobs mirror the options bar.
    /// Mirrored text is just FlipH - the base transform draws it reversed.
    /// </summary>
    sealed class TextLayer : EditorLayer
    {
        public string Text = "";
        public string FontFamily = "Segoe UI";
        public float FontSize = 28;               // pixels
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public int Align;                         // 0 left, 1 centre, 2 right
        public Color Color = System.Drawing.Color.Black;
        public Color BackColor = System.Drawing.Color.Transparent;   // A=0: no box behind the text
        public Color OutlineColor = System.Drawing.Color.Transparent;// A=0: no outline

        public override string KindLabel { get { return "Text"; } }

        public FontStyle Style
        {
            get
            {
                FontStyle s = FontStyle.Regular;
                if (Bold) s |= FontStyle.Bold;
                if (Italic) s |= FontStyle.Italic;
                if (Underline) s |= FontStyle.Underline;
                return s;
            }
        }

        public override EditorLayer Clone()
        {
            TextLayer c = new TextLayer();
            CopyBaseTo(c);
            c.Text = Text;
            c.FontFamily = FontFamily;
            c.FontSize = FontSize;
            c.Bold = Bold;
            c.Italic = Italic;
            c.Underline = Underline;
            c.Align = Align;
            c.Color = Color;
            c.BackColor = BackColor;
            c.OutlineColor = OutlineColor;
            return c;
        }

        protected override string ContentKey()
        {
            return string.Concat(Text, "|", FontFamily, "|", FontSize, "|", (int)Style, "|", Align, "|", Color.ToArgb(), "|", BackColor.ToArgb(), "|", OutlineColor.ToArgb());
        }

        protected override void DrawContent(Graphics g, RectangleF r, int alpha)
        {
            if (BackColor.A > 0)
            {
                using (SolidBrush b = new SolidBrush(Fade(BackColor, alpha)))
                    g.FillRectangle(b, r);
            }
            if (string.IsNullOrEmpty(Text) || Color.A == 0 && OutlineColor.A == 0) return;

            using (StringFormat sf = new StringFormat())
            {
                sf.Trimming = StringTrimming.None;
                sf.Alignment = Align == 1 ? StringAlignment.Center : Align == 2 ? StringAlignment.Far : StringAlignment.Near;
                if (OutlineColor.A > 0)
                {
                    // outlined text renders through a path so the stroke hugs the glyphs
                    try
                    {
                        using (GraphicsPath path = new GraphicsPath())
                        using (System.Drawing.FontFamily fam = new System.Drawing.FontFamily(FontFamily))
                        {
                            path.AddString(Text, fam, (int)Style, FontSize, r, sf);
                            using (Pen pen = new Pen(Fade(OutlineColor, alpha), Math.Max(1f, FontSize / 12f)))
                            {
                                pen.LineJoin = LineJoin.Round;
                                g.DrawPath(pen, path);
                            }
                            if (Color.A > 0)
                                using (SolidBrush b = new SolidBrush(Fade(Color, alpha)))
                                    g.FillPath(b, path);
                        }
                        return;
                    }
                    catch (ArgumentException) { }   // unknown family: fall through to DrawString
                }
                using (Font font = MakeFont())
                using (SolidBrush b = new SolidBrush(Fade(Color, alpha)))
                    g.DrawString(Text, font, b, r, sf);
            }
        }

        public Font MakeFont()
        {
            try { return new Font(FontFamily, Math.Max(1f, FontSize), Style, GraphicsUnit.Pixel); }
            catch (ArgumentException) { return new Font("Segoe UI", Math.Max(1f, FontSize), FontStyle.Regular, GraphicsUnit.Pixel); }
        }
    }

    enum ShapeKind { Rectangle, Ellipse, Line, Arrow, Freehand, RoundedRectangle, Polygon }

    /// <summary>
    /// A mark: rectangle, rounded rectangle, ellipse, polygon, line, arrow or freehand
    /// stroke. Line/arrow/freehand points are stored normalised (0..1 inside Bounds) so
    /// moving and resizing the layer just works; the closed shapes use Bounds directly.
    /// </summary>
    sealed class ShapeLayer : EditorLayer
    {
        public ShapeKind Kind = ShapeKind.Rectangle;
        public Color Stroke = Color.Red;
        public float StrokeWidth = 3;
        public Color Fill = Color.Transparent;    // A=0: unfilled
        public int CornerRadius = 12;             // rounded rectangle
        public int Sides = 6;                     // polygon
        public List<PointF> Points = new List<PointF>();   // normalised, for Line/Arrow/Freehand

        public override string KindLabel { get { return "Shape"; } }

        public override EditorLayer Clone()
        {
            ShapeLayer c = new ShapeLayer();
            CopyBaseTo(c);
            c.Kind = Kind;
            c.Stroke = Stroke;
            c.StrokeWidth = StrokeWidth;
            c.Fill = Fill;
            c.CornerRadius = CornerRadius;
            c.Sides = Sides;
            c.Points = new List<PointF>(Points);
            return c;
        }

        protected override string ContentKey()
        {
            return string.Concat((int)Kind, "|", Stroke.ToArgb(), "|", StrokeWidth, "|", Fill.ToArgb(), "|", CornerRadius, "|", Sides, "|", Points.Count);
        }

        PointF Denorm(PointF p, RectangleF r)
        {
            return new PointF(r.X + p.X * r.Width, r.Y + p.Y * r.Height);
        }

        /// <summary>The closed outline (rect/rounded/ellipse/polygon) as a path, for fills and strokes.</summary>
        public GraphicsPath ClosedPath(RectangleF r)
        {
            var path = new GraphicsPath();
            switch (Kind)
            {
                case ShapeKind.Ellipse:
                    path.AddEllipse(r);
                    break;
                case ShapeKind.RoundedRectangle:
                {
                    float rad = Math.Max(0, Math.Min(CornerRadius, Math.Min(r.Width, r.Height) / 2f));
                    if (rad <= 0.5f) { path.AddRectangle(r); break; }
                    float d = rad * 2;
                    path.AddArc(r.X, r.Y, d, d, 180, 90);
                    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    break;
                }
                case ShapeKind.Polygon:
                {
                    int n = Math.Max(3, Sides);
                    var pts = new PointF[n];
                    float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                    for (int i = 0; i < n; i++)
                    {
                        double a = -Math.PI / 2 + i * 2 * Math.PI / n;
                        pts[i] = new PointF(cx + (float)Math.Cos(a) * r.Width / 2f, cy + (float)Math.Sin(a) * r.Height / 2f);
                    }
                    path.AddPolygon(pts);
                    break;
                }
                default:
                    path.AddRectangle(r);
                    break;
            }
            return path;
        }

        protected override void DrawContent(Graphics g, RectangleF r, int alpha)
        {
            using (Pen pen = new Pen(Fade(Stroke, alpha), StrokeWidth))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                switch (Kind)
                {
                    case ShapeKind.Rectangle:
                    case ShapeKind.Ellipse:
                    case ShapeKind.RoundedRectangle:
                    case ShapeKind.Polygon:
                        using (GraphicsPath path = ClosedPath(r))
                        {
                            if (Fill.A > 0)
                                using (SolidBrush b = new SolidBrush(Fade(Fill, alpha)))
                                    g.FillPath(b, path);
                            if (Stroke.A > 0 && StrokeWidth > 0)
                                g.DrawPath(pen, path);
                        }
                        break;

                    case ShapeKind.Line:
                        if (Points.Count >= 2)
                            g.DrawLine(pen, Denorm(Points[0], r), Denorm(Points[1], r));
                        break;

                    case ShapeKind.Arrow:
                        if (Points.Count >= 2)
                        {
                            using (AdjustableArrowCap cap = new AdjustableArrowCap(3.5f, 4.5f, true))
                            {
                                pen.CustomEndCap = cap;
                                g.DrawLine(pen, Denorm(Points[0], r), Denorm(Points[1], r));
                            }
                        }
                        break;

                    case ShapeKind.Freehand:
                        if (Points.Count >= 2)
                        {
                            PointF[] pts = new PointF[Points.Count];
                            for (int i = 0; i < Points.Count; i++) pts[i] = Denorm(Points[i], r);
                            g.DrawLines(pen, pts);
                        }
                        break;
                }
            }
        }
    }

    /// <summary>Compositing, layer styles and the legacy blur brush, shared by preview, export and clipboard.</summary>
    static class EditorRender
    {
        /// <summary>
        /// The whole document as one bitmap. Layers with a Normal blend and no style go
        /// straight through GDI+; anything else is rendered alone, styled and blended per
        /// pixel. <paramref name="previewOf"/> lets a tool swap in its own picture of one
        /// layer (a Free Transform mid-drag, say) without touching the document.
        /// </summary>
        public static Bitmap Compose(IList<EditorLayer> layers, Size canvas, Color background,
                                     Func<EditorLayer, Bitmap> previewOf = null)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, canvas.Width), Math.Max(1, canvas.Height), PixelFormat.Format32bppArgb);
            // GDI+ refuses LockBits while a Graphics is attached to the bitmap, so the
            // Graphics is opened for runs of plain layers and closed before any pixel work
            Graphics g = null;
            try
            {
                if (background.A > 0)
                {
                    g = Graphics.FromImage(bmp);
                    Prepare(g);
                    g.Clear(background);
                }
                for (int i = 0; i < layers.Count; i++)
                {
                    EditorLayer layer = layers[i];
                    if (!layer.Visible || layer.Opacity <= 0) continue;
                    Bitmap preview = previewOf != null ? previewOf(layer) : null;

                    if (!layer.NeedsPixelPath)
                    {
                        if (g == null) { g = Graphics.FromImage(bmp); Prepare(g); }
                        if (preview == null) layer.Draw(g);
                        else DrawWithOpacity(g, preview, layer.Opacity);   // a plain preview bitmap: opacity only
                        continue;
                    }

                    if (g != null) { g.Dispose(); g = null; }
                    Bitmap alone = preview != null
                        ? (layer.Fx != null && layer.Fx.Any ? ApplyEffects(preview, layer.Fx) : preview)
                        : layer.CachedRender(canvas);
                    Pixels composite = Pixels.From(bmp);
                    PixelOps.BlendOver(composite, Pixels.From(alone), layer.Blend, layer.Opacity / 100f);
                    composite.WriteTo(bmp);
                    if (alone != preview && preview != null) alone.Dispose();
                }
            }
            finally { if (g != null) g.Dispose(); }
            return bmp;
        }

        public static Bitmap Flatten(IList<EditorLayer> layers, Size canvas, Color background)
        {
            return Compose(layers, canvas, background, null);
        }

        static void DrawWithOpacity(Graphics g, Bitmap img, int opacity)
        {
            if (opacity >= 100) { g.DrawImageUnscaled(img, 0, 0); return; }
            using (ImageAttributes attrs = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = opacity / 100f;
                attrs.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
            }
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        // ============================================================ layer styles

        /// <summary>
        /// Renders a layer's style around its pixels: drop shadow and outer glow go under
        /// the layer, stroke and colour overlay over it. The result is a new bitmap the
        /// size of the input.
        /// </summary>
        public static Bitmap ApplyEffects(Bitmap alone, LayerEffects fx)
        {
            Pixels src = Pixels.From(alone);
            int w = src.Width, h = src.Height;
            byte[] alpha = AlphaOf(src);
            var result = new Pixels(w, h);

            if (fx.DropShadow && fx.ShadowOpacity > 0)
            {
                double ang = fx.ShadowAngle * Math.PI / 180;
                int dx = (int)Math.Round(-Math.Cos(ang) * fx.ShadowDistance);
                int dy = (int)Math.Round(Math.Sin(ang) * fx.ShadowDistance);
                byte[] m = Shift(alpha, w, h, dx, dy);
                if (fx.ShadowSize > 0) BlurMask(m, w, h, fx.ShadowSize);
                Tint(result, m, fx.ShadowColor, fx.ShadowOpacity / 100f);
            }
            if (fx.OuterGlow && fx.GlowOpacity > 0)
            {
                byte[] m = (byte[])alpha.Clone();
                if (fx.GlowSize > 0)
                {
                    BlurMask(m, w, h, fx.GlowSize);
                    // a glow spreads: push the blurred ramp outward so it reads as a halo
                    for (int i = 0; i < m.Length; i++) m[i] = (byte)Math.Min(255, m[i] * 2);
                }
                Tint(result, m, fx.GlowColor, fx.GlowOpacity / 100f);
            }

            // the layer itself
            Pixels body = src;
            if (fx.ColorOverlay && fx.OverlayOpacity > 0)
            {
                body = src.Clone();
                byte[] d = body.Data;
                float k = fx.OverlayOpacity / 100f;
                for (int i = 0; i < d.Length; i += 4)
                {
                    if (d[i + 3] == 0) continue;
                    d[i] = (byte)(d[i] + (fx.OverlayColor.B - d[i]) * k);
                    d[i + 1] = (byte)(d[i + 1] + (fx.OverlayColor.G - d[i + 1]) * k);
                    d[i + 2] = (byte)(d[i + 2] + (fx.OverlayColor.R - d[i + 2]) * k);
                }
            }
            PixelOps.BlendOver(result, body, BlendMode.Normal, 1f);

            if (fx.Stroke && fx.StrokeSize > 0 && fx.StrokeOpacity > 0)
            {
                byte[] dist = EditorSelection.DistanceOutside(alpha, w, h, fx.StrokeSize + 1);
                byte[] distIn = EditorSelection.DistanceInside(alpha, w, h, fx.StrokeSize + 1);
                var m = new byte[w * h];
                float size = fx.StrokeSize;
                for (int i = 0; i < m.Length; i++)
                {
                    float outside = dist[i], inside = distIn[i];
                    float v = 0;
                    switch (fx.StrokePosition)
                    {
                        case 1: v = alpha[i] > 0 ? Ramp(size - inside) : 0; break;                 // inside
                        case 2: v = alpha[i] > 0 ? Ramp(size / 2 - inside) : Ramp(size / 2 - outside + 1); break; // centre
                        default: v = alpha[i] > 0 ? 0 : Ramp(size - outside + 1); break;             // outside
                    }
                    m[i] = (byte)(Math.Max(0, Math.Min(1, v)) * 255);
                }
                var strokeLayer = new Pixels(w, h);
                Tint(strokeLayer, m, fx.StrokeColor, 1f);
                PixelOps.BlendOver(result, strokeLayer, BlendMode.Normal, fx.StrokeOpacity / 100f);
            }
            return result.ToBitmap();
        }

        static float Ramp(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        static byte[] AlphaOf(Pixels p)
        {
            var a = new byte[p.Width * p.Height];
            byte[] d = p.Data;
            for (int i = 0, k = 0; i < d.Length; i += 4, k++) a[k] = d[i + 3];
            return a;
        }

        static byte[] Shift(byte[] m, int w, int h, int dx, int dy)
        {
            var o = new byte[m.Length];
            for (int y = 0; y < h; y++)
            {
                int sy = y - dy;
                if (sy < 0 || sy >= h) continue;
                for (int x = 0; x < w; x++)
                {
                    int sx = x - dx;
                    if (sx < 0 || sx >= w) continue;
                    o[y * w + x] = m[sy * w + sx];
                }
            }
            return o;
        }

        /// <summary>Paints colour x mask x opacity over the pixels (source-over).</summary>
        static void Tint(Pixels dst, byte[] mask, Color color, float opacity)
        {
            byte[] d = dst.Data;
            for (int k = 0, i = 0; k < mask.Length; k++, i += 4)
            {
                float a = mask[k] / 255f * opacity * color.A / 255f;
                if (a <= 0) continue;
                float aD = d[i + 3] / 255f, aO = a + aD * (1 - a);
                d[i] = (byte)((color.B * a + d[i] * aD * (1 - a)) / aO);
                d[i + 1] = (byte)((color.G * a + d[i + 1] * aD * (1 - a)) / aO);
                d[i + 2] = (byte)((color.R * a + d[i + 2] * aD * (1 - a)) / aO);
                d[i + 3] = (byte)(aO * 255);
            }
        }

        /// <summary>Gaussian-ish blur of an 8-bit mask (three box passes).</summary>
        public static void BlurMask(byte[] mask, int w, int h, float radius)
        {
            if (radius < 0.5f) return;
            int r = Math.Max(1, (int)Math.Round(radius / 1.8f));
            for (int pass = 0; pass < 3; pass++) BoxBlurMask(mask, w, h, r);
        }

        public static void BoxBlurMask(byte[] mask, int w, int h, int radius)
        {
            if (radius < 1) return;
            int win = radius * 2 + 1;
            byte[] tmp = new byte[mask.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int s = 0;
                for (int x = -radius; x <= radius; x++)
                {
                    int cx = x < 0 ? 0 : (x >= w ? w - 1 : x);
                    s += mask[row + cx];
                }
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = (byte)(s / win);
                    int add = x + radius + 1; if (add >= w) add = w - 1;
                    int sub = x - radius; if (sub < 0) sub = 0;
                    s += mask[row + add] - mask[row + sub];
                }
            }
            for (int x = 0; x < w; x++)
            {
                int s = 0;
                for (int y = -radius; y <= radius; y++)
                {
                    int cy = y < 0 ? 0 : (y >= h ? h - 1 : y);
                    s += tmp[cy * w + x];
                }
                for (int y = 0; y < h; y++)
                {
                    mask[y * w + x] = (byte)(s / win);
                    int add = y + radius + 1; if (add >= h) add = h - 1;
                    int sub = y - radius; if (sub < 0) sub = 0;
                    s += tmp[add * w + x] - tmp[sub * w + x];
                }
            }
        }

        // ============================================================= blur brush

        /// <summary>
        /// Returns a NEW bitmap: <paramref name="source"/> with a blur painted over the given
        /// dabs (centres in bitmap pixels). Kept for the one-shot blur used by screenshots'
        /// privacy smudges; the interactive Blur tool goes through the brush engine.
        /// </summary>
        public static Bitmap BlurDabs(Bitmap source, List<PointF> dabs, float brushRadius, int blurRadius)
        {
            Pixels p = Pixels.From(source);
            if (dabs == null || dabs.Count == 0 || brushRadius < 1 || blurRadius < 1) return p.ToBitmap();
            Pixels blurred = p.Clone();
            PixelOps.GaussianBlur(blurred, blurRadius, null);
            var mask = new byte[p.Width * p.Height];
            foreach (PointF d in dabs) PixelOps.DabMask(mask, p.Width, p.Height, d.X, d.Y, brushRadius, 60, 100);
            Pixels result = p.Clone();
            PixelOps.MixFiltered(result, p, blurred, mask, 1f, null, new Rectangle(0, 0, p.Width, p.Height));
            return result.ToBitmap();
        }
    }
}
