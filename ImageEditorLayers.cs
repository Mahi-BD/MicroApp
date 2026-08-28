using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MicroApp
{
    /// <summary>
    /// One layer of the image editor. Layers live in canvas coordinates: Bounds is the
    /// unrotated box the content is drawn into, and rotation/flips happen around its centre,
    /// the way Photoshop transforms a layer. Clone() is used for undo snapshots - it is a
    /// shallow copy, so a RasterLayer's pixels are shared between snapshots and every
    /// destructive edit (blur) must REPLACE the bitmap, never draw into it.
    /// </summary>
    abstract class EditorLayer
    {
        public string Name = "Layer";
        public bool Visible = true;
        public int Opacity = 100;                 // 0..100
        public RectangleF Bounds;
        public float RotationDeg;
        public bool FlipH;
        public bool FlipV;

        public PointF Center
        {
            get { return new PointF(Bounds.X + Bounds.Width / 2f, Bounds.Y + Bounds.Height / 2f); }
        }

        public abstract EditorLayer Clone();

        /// <summary>Draw the content into rect r; alpha is 0..255 from the layer opacity.</summary>
        protected abstract void DrawContent(Graphics g, RectangleF r, int alpha);

        public void Draw(Graphics g)
        {
            if (!Visible || Opacity <= 0 || Bounds.Width < 0.5f || Bounds.Height < 0.5f) return;
            GraphicsState state = g.Save();
            try
            {
                PointF c = Center;
                g.TranslateTransform(c.X, c.Y);
                if (RotationDeg != 0) g.RotateTransform(RotationDeg);
                if (FlipH || FlipV) g.ScaleTransform(FlipH ? -1 : 1, FlipV ? -1 : 1);
                g.TranslateTransform(-c.X, -c.Y);
                DrawContent(g, Bounds, Opacity * 255 / 100);
            }
            finally { g.Restore(state); }
        }

        /// <summary>Layer-local → canvas transform (rotation and flips about the centre).</summary>
        public Matrix GetMatrix()
        {
            Matrix m = new Matrix();
            PointF c = Center;
            m.Translate(c.X, c.Y);
            m.Rotate(RotationDeg);
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

        /// <summary>The four corners of the rendered (rotated) box, in canvas coordinates.</summary>
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

        protected void CopyBaseTo(EditorLayer other)
        {
            other.Name = Name;
            other.Visible = Visible;
            other.Opacity = Opacity;
            other.Bounds = Bounds;
            other.RotationDeg = RotationDeg;
            other.FlipH = FlipH;
            other.FlipV = FlipV;
        }

        protected static Color Fade(Color c, int alpha)
        {
            if (alpha >= 255) return c;
            return Color.FromArgb(c.A * alpha / 255, c.R, c.G, c.B);
        }
    }

    /// <summary>A bitmap layer: pasted images, opened files, assets.</summary>
    sealed class RasterLayer : EditorLayer
    {
        public Bitmap Image;      // owned by the document; snapshots share the reference

        public RasterLayer(Bitmap image)
        {
            Image = image;
        }

        public override EditorLayer Clone()
        {
            RasterLayer c = new RasterLayer(Image);
            CopyBaseTo(c);
            return c;
        }

        protected override void DrawContent(Graphics g, RectangleF r, int alpha)
        {
            if (Image == null) return;
            PointF[] dest =
            {
                new PointF(r.Left, r.Top),
                new PointF(r.Right, r.Top),
                new PointF(r.Left, r.Bottom)
            };
            RectangleF src = new RectangleF(0, 0, Image.Width, Image.Height);
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
        public Color Color = System.Drawing.Color.Black;
        public Color BackColor = System.Drawing.Color.Transparent;   // A=0: no box behind the text
        public Color OutlineColor = System.Drawing.Color.Transparent;// A=0: no outline

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
            c.Color = Color;
            c.BackColor = BackColor;
            c.OutlineColor = OutlineColor;
            return c;
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

    enum ShapeKind { Rectangle, Ellipse, Line, Arrow, Freehand }

    /// <summary>
    /// A mark: rectangle, ellipse, line, arrow or freehand stroke. Line/arrow/freehand
    /// points are stored normalised (0..1 inside Bounds) so moving and resizing the layer
    /// just works; rectangle and ellipse use Bounds directly.
    /// </summary>
    sealed class ShapeLayer : EditorLayer
    {
        public ShapeKind Kind = ShapeKind.Rectangle;
        public Color Stroke = Color.Red;
        public float StrokeWidth = 3;
        public Color Fill = Color.Transparent;    // A=0: unfilled
        public List<PointF> Points = new List<PointF>();   // normalised, for Line/Arrow/Freehand

        public override EditorLayer Clone()
        {
            ShapeLayer c = new ShapeLayer();
            CopyBaseTo(c);
            c.Kind = Kind;
            c.Stroke = Stroke;
            c.StrokeWidth = StrokeWidth;
            c.Fill = Fill;
            c.Points = new List<PointF>(Points);
            return c;
        }

        PointF Denorm(PointF p, RectangleF r)
        {
            return new PointF(r.X + p.X * r.Width, r.Y + p.Y * r.Height);
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
                        if (Fill.A > 0)
                            using (SolidBrush b = new SolidBrush(Fade(Fill, alpha)))
                                g.FillRectangle(b, r.X, r.Y, r.Width, r.Height);
                        if (Stroke.A > 0)
                            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                        break;

                    case ShapeKind.Ellipse:
                        if (Fill.A > 0)
                            using (SolidBrush b = new SolidBrush(Fade(Fill, alpha)))
                                g.FillEllipse(b, r);
                        if (Stroke.A > 0)
                            g.DrawEllipse(pen, r);
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

    /// <summary>Flattening and the blur brush, shared by preview, export and clipboard.</summary>
    static class EditorRender
    {
        public static Bitmap Flatten(IList<EditorLayer> layers, Size canvas, Color background)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, canvas.Width), Math.Max(1, canvas.Height), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);
                if (background.A > 0) g.Clear(background);
                for (int i = 0; i < layers.Count; i++) layers[i].Draw(g);
            }
            return bmp;
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        /// <summary>
        /// Returns a NEW bitmap: <paramref name="source"/> with a blur painted over the given
        /// dabs (centres in bitmap pixels). The brushed area is blurred with an iterated box
        /// blur - visually a gaussian - and blended back through a feathered mask, so the
        /// edge of every stroke fades out instead of ending in a hard seam.
        /// </summary>
        public static Bitmap BlurDabs(Bitmap source, List<PointF> dabs, float brushRadius, int blurRadius)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                            0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            }
            if (dabs == null || dabs.Count == 0 || brushRadius < 1 || blurRadius < 1) return result;

            // region of interest: the dabs, grown by brush + blur so samples do not clip
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (PointF p in dabs)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            int grow = (int)Math.Ceiling(brushRadius) + blurRadius * 3 + 2;
            Rectangle roi = Rectangle.Intersect(
                new Rectangle((int)minX - grow, (int)minY - grow,
                              (int)(maxX - minX) + grow * 2, (int)(maxY - minY) + grow * 2),
                new Rectangle(0, 0, result.Width, result.Height));
            if (roi.Width < 1 || roi.Height < 1) return result;

            int w = roi.Width, h = roi.Height;
            BitmapData data = result.LockBits(roi, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] orig = new byte[stride * h];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, orig, 0, orig.Length);

                byte[] blurred = (byte[])orig.Clone();
                BoxBlur(blurred, w, h, stride, blurRadius);
                BoxBlur(blurred, w, h, stride, blurRadius);
                BoxBlur(blurred, w, h, stride, blurRadius);

                // mask: filled circles, then softened so strokes feather out
                byte[] mask = new byte[w * h];
                foreach (PointF p in dabs)
                    FillCircle(mask, w, h, p.X - roi.X, p.Y - roi.Y, brushRadius);
                BoxBlurMask(mask, w, h, Math.Max(1, (int)(brushRadius / 4)));

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    int mrow = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int m = mask[mrow + x];
                        if (m == 0) continue;
                        int i = row + x * 4;
                        if (m == 255)
                        {
                            orig[i] = blurred[i];
                            orig[i + 1] = blurred[i + 1];
                            orig[i + 2] = blurred[i + 2];
                            orig[i + 3] = blurred[i + 3];
                        }
                        else
                        {
                            int inv = 255 - m;
                            orig[i] = (byte)((orig[i] * inv + blurred[i] * m) / 255);
                            orig[i + 1] = (byte)((orig[i + 1] * inv + blurred[i + 1] * m) / 255);
                            orig[i + 2] = (byte)((orig[i + 2] * inv + blurred[i + 2] * m) / 255);
                            orig[i + 3] = (byte)((orig[i + 3] * inv + blurred[i + 3] * m) / 255);
                        }
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(orig, 0, data.Scan0, orig.Length);
            }
            finally
            {
                result.UnlockBits(data);
            }
            return result;
        }

        static void FillCircle(byte[] mask, int w, int h, float cx, float cy, float radius)
        {
            int x0 = Math.Max(0, (int)(cx - radius));
            int x1 = Math.Min(w - 1, (int)(cx + radius) + 1);
            int y0 = Math.Max(0, (int)(cy - radius));
            int y1 = Math.Min(h - 1, (int)(cy + radius) + 1);
            float r2 = radius * radius;
            for (int y = y0; y <= y1; y++)
            {
                float dy = y - cy;
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cx;
                    if (dx * dx + dy * dy <= r2) mask[row + x] = 255;
                }
            }
        }

        /// <summary>One separable box-blur pass over BGRA pixels, sliding-window accumulators.</summary>
        static void BoxBlur(byte[] px, int w, int h, int stride, int radius)
        {
            if (radius < 1) return;
            int win = radius * 2 + 1;
            byte[] tmp = new byte[px.Length];

            // horizontal
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int sb = 0, sg = 0, sr = 0, sa = 0;
                for (int x = -radius; x <= radius; x++)
                {
                    int cx = x < 0 ? 0 : (x >= w ? w - 1 : x);
                    int i = row + cx * 4;
                    sb += px[i]; sg += px[i + 1]; sr += px[i + 2]; sa += px[i + 3];
                }
                for (int x = 0; x < w; x++)
                {
                    int o = row + x * 4;
                    tmp[o] = (byte)(sb / win); tmp[o + 1] = (byte)(sg / win);
                    tmp[o + 2] = (byte)(sr / win); tmp[o + 3] = (byte)(sa / win);
                    int add = x + radius + 1; if (add >= w) add = w - 1;
                    int sub = x - radius; if (sub < 0) sub = 0;
                    int ia = row + add * 4, isub = row + sub * 4;
                    sb += px[ia] - px[isub]; sg += px[ia + 1] - px[isub + 1];
                    sr += px[ia + 2] - px[isub + 2]; sa += px[ia + 3] - px[isub + 3];
                }
            }

            // vertical
            for (int x = 0; x < w; x++)
            {
                int col = x * 4;
                int sb = 0, sg = 0, sr = 0, sa = 0;
                for (int y = -radius; y <= radius; y++)
                {
                    int cy = y < 0 ? 0 : (y >= h ? h - 1 : y);
                    int i = cy * stride + col;
                    sb += tmp[i]; sg += tmp[i + 1]; sr += tmp[i + 2]; sa += tmp[i + 3];
                }
                for (int y = 0; y < h; y++)
                {
                    int o = y * stride + col;
                    px[o] = (byte)(sb / win); px[o + 1] = (byte)(sg / win);
                    px[o + 2] = (byte)(sr / win); px[o + 3] = (byte)(sa / win);
                    int add = y + radius + 1; if (add >= h) add = h - 1;
                    int sub = y - radius; if (sub < 0) sub = 0;
                    int ia = add * stride + col, isub = sub * stride + col;
                    sb += tmp[ia] - tmp[isub]; sg += tmp[ia + 1] - tmp[isub + 1];
                    sr += tmp[ia + 2] - tmp[isub + 2]; sa += tmp[ia + 3] - tmp[isub + 3];
                }
            }
        }

        static void BoxBlurMask(byte[] mask, int w, int h, int radius)
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
    }
}
