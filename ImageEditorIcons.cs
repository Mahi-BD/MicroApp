using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MicroApp
{
    /// <summary>The editor's tools, grouped the way Photoshop's toolbar groups them.</summary>
    enum Tool
    {
        Move,
        MarqueeRect, MarqueeEllipse,
        Lasso, PolyLasso,
        Wand,
        Crop,
        Eyedropper,
        Brush, Pencil,
        Eraser,
        Clone,
        Bucket, Gradient,
        Blur, Sharpen,
        Dodge, Burn,
        Text,
        ShapeRect, ShapeRoundRect, ShapeEllipse, ShapePolygon, ShapeLine, ShapeArrow,
        Hand,
        Zoom
    }

    /// <summary>
    /// Hand-drawn tool glyphs for the tool rail. Font glyphs and emoji come out as hollow
    /// boxes on some machines, so every icon is a few strokes of GDI+ instead.
    /// </summary>
    static class EditorIcons
    {
        public static void Draw(Graphics g, Tool tool, Rectangle box, Color ink)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // work in a 20x20 square centred in the box
            float s = Math.Min(box.Width, box.Height) / 20f;
            g.TranslateTransform(box.X + (box.Width - 20 * s) / 2f, box.Y + (box.Height - 20 * s) / 2f);
            g.ScaleTransform(s, s);
            using (var p = new Pen(ink, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var thin = new Pen(ink, 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round, DashStyle = DashStyle.Dash })
            using (var fill = new SolidBrush(ink))
            {
                switch (tool)
                {
                    case Tool.Move:
                        // an arrow cursor with a four-way move mark
                        g.FillPolygon(fill, new[] { new PointF(3, 2), new PointF(3, 13), new PointF(6, 10.5f), new PointF(8.2f, 15), new PointF(10.2f, 14.1f), new PointF(8, 9.8f), new PointF(11.5f, 9.6f) });
                        g.DrawLine(p, 15, 11, 15, 18); g.DrawLine(p, 11.5f, 14.5f, 18.5f, 14.5f);
                        g.DrawLine(p, 13.5f, 12.5f, 15, 11); g.DrawLine(p, 16.5f, 12.5f, 15, 11);
                        g.DrawLine(p, 13.5f, 16.5f, 15, 18); g.DrawLine(p, 16.5f, 16.5f, 15, 18);
                        break;
                    case Tool.MarqueeRect:
                        g.DrawRectangle(thin, 3, 4, 14, 12);
                        break;
                    case Tool.MarqueeEllipse:
                        g.DrawEllipse(thin, 3, 4, 14, 12);
                        break;
                    case Tool.Lasso:
                        g.DrawClosedCurve(p, new[] { new PointF(6, 4), new PointF(13, 3), new PointF(17, 8), new PointF(12, 13), new PointF(5, 12), new PointF(3, 8) }, 0.6f, FillMode.Alternate);
                        g.DrawLine(p, 7, 12, 6, 17);
                        break;
                    case Tool.PolyLasso:
                        g.DrawPolygon(p, new[] { new PointF(4, 5), new PointF(11, 3), new PointF(17, 8), new PointF(14, 13), new PointF(6, 12) });
                        g.DrawLine(p, 6, 12, 5, 17);
                        break;
                    case Tool.Wand:
                        g.DrawLine(p, 4, 16, 12, 8);
                        Star(g, fill, 14, 6, 3.4f);
                        g.FillEllipse(fill, 16.5f, 12, 1.8f, 1.8f);
                        g.FillEllipse(fill, 9, 3, 1.6f, 1.6f);
                        break;
                    case Tool.Crop:
                        g.DrawLine(p, 6, 2, 6, 14); g.DrawLine(p, 6, 14, 18, 14);
                        g.DrawLine(p, 2, 6, 14, 6); g.DrawLine(p, 14, 6, 14, 18);
                        break;
                    case Tool.Eyedropper:
                        g.DrawLine(p, 4, 16, 11, 9);
                        using (var wide = new Pen(ink, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(wide, 12, 8, 15.5f, 4.5f);
                        g.DrawLine(p, 10, 7, 13, 10);
                        break;
                    case Tool.Brush:
                        g.DrawLine(p, 16, 3, 8, 11);
                        g.FillClosedCurve(fill, new[] { new PointF(7.5f, 10.5f), new PointF(9.5f, 12.5f), new PointF(8, 16), new PointF(4, 17), new PointF(3.5f, 14), new PointF(5, 11.5f) });
                        break;
                    case Tool.Pencil:
                        g.DrawPolygon(p, new[] { new PointF(4, 16), new PointF(5, 12), new PointF(14, 3), new PointF(17, 6), new PointF(8, 15) });
                        g.DrawLine(p, 5, 12, 8, 15);
                        break;
                    case Tool.Eraser:
                        g.DrawPolygon(p, new[] { new PointF(3, 12), new PointF(11, 4), new PointF(17, 10), new PointF(11, 16), new PointF(7, 16) });
                        g.DrawLine(p, 7, 8, 13, 14);
                        break;
                    case Tool.Clone:
                        g.DrawEllipse(p, 4, 8, 12, 7);
                        g.DrawLine(p, 10, 8, 10, 3); g.DrawLine(p, 7, 3, 13, 3);
                        g.DrawLine(p, 4, 17, 16, 17);
                        break;
                    case Tool.Bucket:
                        g.DrawPolygon(p, new[] { new PointF(4, 9), new PointF(10, 3), new PointF(16, 9), new PointF(10, 15) });
                        g.DrawLine(p, 7, 6, 4, 3);
                        g.FillClosedCurve(fill, new[] { new PointF(16, 11), new PointF(18.5f, 15), new PointF(16, 17), new PointF(13.5f, 15) });
                        break;
                    case Tool.Gradient:
                        using (var lg = new LinearGradientBrush(new Rectangle(3, 4, 14, 12), ink, Color.FromArgb(0, ink), LinearGradientMode.Horizontal))
                            g.FillRectangle(lg, 3, 4, 14, 12);
                        g.DrawRectangle(p, 3, 4, 14, 12);
                        break;
                    case Tool.Blur:
                        g.FillClosedCurve(fill, new[] { new PointF(10, 2.5f), new PointF(15, 10), new PointF(13, 16), new PointF(7, 16), new PointF(5, 10) }, FillMode.Alternate, 0.5f);
                        break;
                    case Tool.Sharpen:
                        g.FillPolygon(fill, new[] { new PointF(10, 2.5f), new PointF(15.5f, 16.5f), new PointF(10, 13.5f), new PointF(4.5f, 16.5f) });
                        break;
                    case Tool.Dodge:
                        g.DrawEllipse(p, 4, 4, 9, 9);
                        g.DrawLine(p, 11.5f, 11.5f, 17, 17);
                        g.DrawLine(p, 10, 6.5f, 7, 10.5f); g.DrawLine(p, 6.5f, 8, 10.5f, 9);
                        break;
                    case Tool.Burn:
                        g.DrawEllipse(p, 4, 4, 9, 9);
                        g.DrawLine(p, 11.5f, 11.5f, 17, 17);
                        g.FillPie(fill, 5.5f, 5.5f, 6, 6, 90, 180);
                        break;
                    case Tool.Text:
                        using (var f = new Font("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("T", f, fill, new RectangleF(0, 0, 20, 20), sf);
                        g.DrawLine(p, 4, 17.5f, 16, 17.5f);
                        break;
                    case Tool.ShapeRect:
                        g.DrawRectangle(p, 3.5f, 4.5f, 13, 11);
                        break;
                    case Tool.ShapeRoundRect:
                        using (GraphicsPath rr = Theme.Round(new Rectangle(3, 4, 14, 12), 4)) g.DrawPath(p, rr);
                        break;
                    case Tool.ShapeEllipse:
                        g.DrawEllipse(p, 3.5f, 4.5f, 13, 11);
                        break;
                    case Tool.ShapePolygon:
                        g.DrawPolygon(p, new[] { new PointF(10, 3), new PointF(16.5f, 7.7f), new PointF(14, 15.5f), new PointF(6, 15.5f), new PointF(3.5f, 7.7f) });
                        break;
                    case Tool.ShapeLine:
                        g.DrawLine(p, 4, 16, 16, 4);
                        break;
                    case Tool.ShapeArrow:
                        g.DrawLine(p, 4, 16, 15, 5);
                        g.DrawLine(p, 9, 4.5f, 15.5f, 4.5f); g.DrawLine(p, 15.5f, 4.5f, 15.5f, 11);
                        break;
                    case Tool.Hand:
                        g.DrawLine(p, 6, 10, 6, 5); g.DrawLine(p, 9, 9, 9, 3.5f);
                        g.DrawLine(p, 12, 9, 12, 4); g.DrawLine(p, 15, 10, 15, 6);
                        g.DrawLine(p, 6, 10, 6, 13); g.DrawLine(p, 15, 10, 15, 13);
                        g.DrawBezier(p, 6, 13, 6, 18, 15, 18, 15, 13);
                        g.DrawLine(p, 6, 10, 3.5f, 8);
                        break;
                    case Tool.Zoom:
                        g.DrawEllipse(p, 3, 3, 10, 10);
                        g.DrawLine(p, 11.5f, 11.5f, 17, 17);
                        g.DrawLine(p, 6, 8, 10, 8); g.DrawLine(p, 8, 6, 8, 10);
                        break;
                }
            }
            g.Restore(st);
        }

        static void Star(Graphics g, Brush b, float cx, float cy, float r)
        {
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                float rr = i % 2 == 0 ? r : r * 0.42f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
            }
            g.FillPolygon(b, pts);
        }

        /// <summary>A little glyph for non-tool buttons: "fx", new-layer, trash, etc.</summary>
        public static void DrawGlyph(Graphics g, string glyph, Rectangle box, Color ink)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(box.Width, box.Height) / 20f;
            g.TranslateTransform(box.X + (box.Width - 20 * s) / 2f, box.Y + (box.Height - 20 * s) / 2f);
            g.ScaleTransform(s, s);
            using (var p = new Pen(ink, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(ink))
            {
                switch (glyph)
                {
                    case "new":     // a page with a plus
                        g.DrawRectangle(p, 5, 3, 10, 14);
                        g.DrawLine(p, 10, 7, 10, 13); g.DrawLine(p, 7, 10, 13, 10);
                        break;
                    case "trash":
                        g.DrawLine(p, 4, 6, 16, 6); g.DrawLine(p, 8, 6, 8, 4); g.DrawLine(p, 8, 4, 12, 4); g.DrawLine(p, 12, 4, 12, 6);
                        g.DrawLine(p, 6, 6, 7, 17); g.DrawLine(p, 14, 6, 13, 17); g.DrawLine(p, 7, 17, 13, 17);
                        break;
                    case "dup":
                        g.DrawRectangle(p, 3, 6, 10, 11); g.DrawLine(p, 7, 3, 17, 3); g.DrawLine(p, 17, 3, 17, 13);
                        break;
                    case "up":
                        g.DrawLine(p, 10, 15, 10, 5); g.DrawLine(p, 5, 10, 10, 5); g.DrawLine(p, 15, 10, 10, 5);
                        break;
                    case "down":
                        g.DrawLine(p, 10, 5, 10, 15); g.DrawLine(p, 5, 10, 10, 15); g.DrawLine(p, 15, 10, 10, 15);
                        break;
                    case "merge":
                        g.DrawLine(p, 10, 3, 10, 11); g.DrawLine(p, 6, 8, 10, 12); g.DrawLine(p, 14, 8, 10, 12);
                        g.DrawLine(p, 4, 16, 16, 16);
                        break;
                    case "fx":
                        using (var f = new Font("Segoe UI Semibold", 11f, FontStyle.Italic, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("fx", f, fill, new RectangleF(0, 0, 20, 20), sf);
                        break;
                    case "lock":
                        g.DrawRectangle(p, 5, 9, 10, 8);
                        g.DrawArc(p, 6.5f, 3, 7, 9, 180, 180);
                        break;
                    case "unlock":
                        g.DrawRectangle(p, 5, 9, 10, 8);
                        g.DrawArc(p, 9, 3, 7, 9, 180, 150);
                        break;
                    case "swap":
                        g.DrawArc(p, 3, 3, 12, 12, 200, 130);
                        g.DrawLine(p, 4, 6, 4, 9); g.DrawLine(p, 4, 9, 7, 9);
                        g.DrawLine(p, 14, 12, 14, 9); g.DrawLine(p, 14, 9, 11, 9);
                        break;
                    case "ruler":   // a ruler, corner to corner, with its ticks
                        g.DrawLine(p, 3, 14, 14, 3); g.DrawLine(p, 14, 3, 17, 6); g.DrawLine(p, 17, 6, 6, 17); g.DrawLine(p, 6, 17, 3, 14);
                        g.DrawLine(p, 6.5f, 10.5f, 8.5f, 12.5f); g.DrawLine(p, 9, 8, 10.5f, 9.5f); g.DrawLine(p, 11.5f, 5.5f, 13.5f, 7.5f);
                        break;
                    case "removebg":   // a subject cut out of a checkerboard, with a spark for "automatic"
                        using (var dim = new SolidBrush(Color.FromArgb(90, ink)))
                        {
                            g.FillRectangle(dim, 2, 9, 3, 3); g.FillRectangle(dim, 2, 15, 3, 3);
                            g.FillRectangle(dim, 5, 12, 3, 3); g.FillRectangle(dim, 15, 12, 3, 3);
                        }
                        g.DrawEllipse(p, 7.5f, 4.5f, 5, 5);
                        g.DrawArc(p, 5, 11, 10, 12, 180, 180);
                        g.DrawLine(p, 16, 1.5f, 16, 5.5f); g.DrawLine(p, 14, 3.5f, 18, 3.5f);
                        break;
                    case "reset":
                        g.FillRectangle(fill, 3, 3, 9, 9);
                        g.DrawRectangle(p, 8, 8, 9, 9);
                        break;
                    case "folder":
                        g.DrawLine(p, 3, 6, 8, 6); g.DrawLine(p, 8, 6, 10, 8); g.DrawLine(p, 10, 8, 17, 8);
                        g.DrawLine(p, 17, 8, 17, 16); g.DrawLine(p, 17, 16, 3, 16); g.DrawLine(p, 3, 16, 3, 6);
                        break;
                    case "import":
                        g.DrawLine(p, 10, 3, 10, 12); g.DrawLine(p, 6, 8, 10, 12); g.DrawLine(p, 14, 8, 10, 12);
                        g.DrawLine(p, 4, 16, 16, 16);
                        break;
                    case "check":
                        g.DrawLine(p, 4, 10.5f, 8.5f, 15); g.DrawLine(p, 8.5f, 15, 16.5f, 5.5f);
                        break;
                    case "cancel":
                        g.DrawEllipse(p, 3.5f, 3.5f, 13, 13);
                        g.DrawLine(p, 6, 14, 14, 6);
                        break;
                    case "link":
                        g.DrawArc(p, 3, 7, 7, 6, 90, 180); g.DrawArc(p, 10, 7, 7, 6, 270, 180);
                        g.DrawLine(p, 7, 10, 13, 10);
                        break;
                    case "unlink":
                        g.DrawArc(p, 3, 7, 7, 6, 90, 180); g.DrawArc(p, 10, 7, 7, 6, 270, 180);
                        break;
                    case "menu":
                        g.DrawLine(p, 4, 6, 16, 6); g.DrawLine(p, 4, 10, 16, 10); g.DrawLine(p, 4, 14, 16, 14);
                        break;

                    // selection modes: two overlapping squares
                    case "selnew":
                        g.DrawRectangle(p, 4, 4, 12, 12);
                        break;
                    case "seladd":
                        g.DrawRectangle(p, 3, 3, 9, 9);
                        g.DrawRectangle(p, 8, 8, 9, 9);
                        g.DrawLine(p, 12.5f, 10, 12.5f, 15); g.DrawLine(p, 10, 12.5f, 15, 12.5f);
                        break;
                    case "selsub":
                        g.DrawRectangle(p, 3, 3, 9, 9);
                        g.DrawRectangle(p, 8, 8, 9, 9);
                        g.DrawLine(p, 10, 12.5f, 15, 12.5f);
                        break;
                    case "selint":
                        g.DrawRectangle(p, 3, 3, 9, 9);
                        g.DrawRectangle(p, 8, 8, 9, 9);
                        g.FillRectangle(fill, 8, 8, 4, 4);
                        break;

                    // align: an edge line with two bars against it
                    case "alignl":
                        g.DrawLine(p, 4, 3, 4, 17); g.FillRectangle(fill, 6, 5, 10, 4); g.FillRectangle(fill, 6, 11, 6, 4);
                        break;
                    case "alignc":
                        g.DrawLine(p, 10, 3, 10, 17); g.FillRectangle(fill, 4, 5, 12, 4); g.FillRectangle(fill, 6.5f, 11, 7, 4);
                        break;
                    case "alignr":
                        g.DrawLine(p, 16, 3, 16, 17); g.FillRectangle(fill, 4, 5, 10, 4); g.FillRectangle(fill, 8, 11, 6, 4);
                        break;
                    case "alignt":
                        g.DrawLine(p, 3, 4, 17, 4); g.FillRectangle(fill, 5, 6, 4, 10); g.FillRectangle(fill, 11, 6, 4, 6);
                        break;
                    case "alignm":
                        g.DrawLine(p, 3, 10, 17, 10); g.FillRectangle(fill, 5, 4, 4, 12); g.FillRectangle(fill, 11, 6.5f, 4, 7);
                        break;
                    case "alignb":
                        g.DrawLine(p, 3, 16, 17, 16); g.FillRectangle(fill, 5, 4, 4, 10); g.FillRectangle(fill, 11, 8, 4, 6);
                        break;
                }
            }
            g.Restore(st);
        }
    }
}
