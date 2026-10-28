using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;

namespace MicroApp
{
    /// <summary>
    /// Everything a .ma project keeps: every page with its canvas and every layer with all
    /// of its settings (image layers as lossless PNG, text and shapes still editable), the
    /// layer styles, the selections, guides and ruler origin. The undo history is not kept.
    /// </summary>
    sealed class EditorProject
    {
        public float Ppi = 72;
        public List<ProjectGuide> Guides = new List<ProjectGuide>();
        public PointF RulerZero;
        public int PageIndex;
        public List<ProjectPage> Pages = new List<ProjectPage>();
    }

    sealed class ProjectPage
    {
        public string Name = "Page 1";
        public Size Canvas;
        public Color CanvasBg;
        public int Sel = -1;
        public int NameCounter;
        public List<EditorLayer> Layers = new List<EditorLayer>();
        public EditorSelection Selection, LastSelection;
        public Func<int, ProjectFile.PixelCopy> PixelsOf;   // save only: a copy of layer i's pixels (null for text / shapes); called from the worker
        public List<long> PixelCounts = new List<long>();   // save only: per layer, for the progress weighting
    }

    sealed class ProjectGuide
    {
        public bool Vertical;
        public float Pos;
        public bool Locked;
        public Color Color;
    }

    /// <summary>
    /// The .ma file: "MAPROJ" + format version, a document header, then per page a page
    /// header and one length-prefixed record per layer (so a newer reader can skip kinds it
    /// does not know), then "END". Version 1 files hold a single page and still open.
    /// Image layers are PNG: measured against row-delta + Deflate on Windows, GDI+'s PNG
    /// encoder was just as fast and packed tighter, so there was no case for a format of
    /// its own.
    /// Little-endian BinaryWriter throughout; strings are UTF-8.
    /// </summary>
    static class ProjectFile
    {
        public const string Extension = ".ma";
        public const string Filter = "MicroApp Project (*.ma)|*.ma";
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("MAPROJ");
        const int FormatVersion = 2;
        const byte KindRaster = 0, KindText = 1, KindShape = 2;

        public static bool IsProject(string file)
        {
            return string.Equals(Path.GetExtension(file), Extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Copies a layer bitmap's pixels on the calling (UI) thread, so the save can encode
        /// them on a worker while the canvas keeps painting from the original.
        /// </summary>
        public static PixelCopy CopyPixels(Bitmap bmp)
        {
            if (bmp == null) return null;
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var copy = new PixelCopy { Width = bmp.Width, Height = bmp.Height, Pixels = new byte[bmp.Width * 4 * bmp.Height] };
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, copy.Pixels, y * bmp.Width * 4, bmp.Width * 4);
                return copy;
            }
            finally { bmp.UnlockBits(data); }
        }

        public sealed class PixelCopy
        {
            public int Width, Height;
            public byte[] Pixels;   // 32bpp ARGB, rows packed

            public Bitmap ToBitmap()
            {
                var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < Height; y++)
                        Marshal.Copy(Pixels, y * Width * 4, data.Scan0 + y * data.Stride, Width * 4);
                }
                finally { bmp.UnlockBits(data); }
                return bmp;
            }
        }

        // ================================================================== save

        /// <summary>
        /// Writes the project. <paramref name="pixels"/> holds, per layer, the copied pixels of
        /// an image layer (null for text / shapes). Written to a temp file first, so a failed
        /// save never damages the previous copy.
        /// </summary>
        public static void Save(string path, EditorProject p, Action<float, string> progress)
        {
            // progress is weighted by pixel count: encoding the images is where the time goes
            double total = 1, done = 0;
            foreach (ProjectPage page in p.Pages)
                for (int i = 0; i < page.Layers.Count; i++) total += Weight(page, i);
            int layerTotal = p.Pages.Sum(pg => pg.Layers.Count);

            string tmp = path + ".saving";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var w = new BinaryWriter(fs, Encoding.UTF8))
                {
                    w.Write(Magic);
                    w.Write(FormatVersion);
                    w.Write(typeof(ProjectFile).Assembly.GetName().Version.ToString());
                    w.Write(p.Ppi);
                    w.Write(p.RulerZero.X);
                    w.Write(p.RulerZero.Y);
                    w.Write(p.Guides.Count);
                    foreach (ProjectGuide g in p.Guides)
                    {
                        w.Write(g.Vertical);
                        w.Write(g.Pos);
                        w.Write(g.Locked);
                        w.Write(g.Color.ToArgb());
                    }
                    w.Write(p.PageIndex);
                    w.Write(p.Pages.Count);
                    int n = 0;
                    for (int pi = 0; pi < p.Pages.Count; pi++)
                    {
                        ProjectPage page = p.Pages[pi];
                        w.Write(page.Name ?? "");
                        w.Write(page.Canvas.Width);
                        w.Write(page.Canvas.Height);
                        w.Write(page.CanvasBg.ToArgb());
                        w.Write(page.Sel);
                        w.Write(page.NameCounter);
                        WriteSelection(w, page.Selection);
                        WriteSelection(w, page.LastSelection);
                        w.Write(page.Layers.Count);
                        for (int i = 0; i < page.Layers.Count; i++)
                        {
                            EditorLayer layer = page.Layers[i];
                            n++;
                            progress(Fraction(done, total), (p.Pages.Count > 1 ? page.Name + " - " : "") +
                                     "layer " + n + " of " + layerTotal + ": " + layer.Name);
                            // one layer's pixels at a time: fetched, encoded, forgotten
                            PixelCopy px = layer is RasterLayer && page.PixelsOf != null ? page.PixelsOf(i) : null;
                            byte[] record = LayerRecord(layer, px);
                            px = null;
                            w.Write(KindOf(layer));
                            w.Write(record.Length);
                            w.Write(record);
                            done += Weight(page, i);
                        }
                    }
                    w.Write(Encoding.ASCII.GetBytes("END"));
                    progress(Fraction(done, total), "Writing the file…");
                }
                if (!File.Exists(path)) File.Move(tmp, path);
                else
                {
                    try { File.Replace(tmp, path, null); }
                    catch (IOException) { File.Delete(path); File.Move(tmp, path); }   // e.g. a share that cannot replace in place
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            progress(1f, "Saved.");
        }

        static double Weight(ProjectPage page, int i)
        {
            return i < page.PixelCounts.Count && page.PixelCounts[i] > 0 ? page.PixelCounts[i] : 1000;
        }

        /// <summary>
        /// PNG-encodes a pixel copy: lossless, keeps the alpha channel. The bitmap is laid
        /// over the copy's own buffer (pinned) rather than copied into a second one.
        /// </summary>
        static byte[] Png(PixelCopy px)
        {
            GCHandle pin = GCHandle.Alloc(px.Pixels, GCHandleType.Pinned);
            try
            {
                using (var bmp = new Bitmap(px.Width, px.Height, px.Width * 4, PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject()))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
            finally { pin.Free(); }
        }

        static float Fraction(double done, double total)
        {
            return (float)Math.Min(0.99, done / total);
        }

        static byte KindOf(EditorLayer layer)
        {
            if (layer is TextLayer) return KindText;
            if (layer is ShapeLayer) return KindShape;
            return KindRaster;
        }

        static byte[] LayerRecord(EditorLayer layer, PixelCopy px)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(layer.Name ?? "");
                w.Write(layer.Visible);
                w.Write(layer.Locked);
                w.Write(layer.Opacity);
                w.Write((int)layer.Blend);
                w.Write(layer.Bounds.X);
                w.Write(layer.Bounds.Y);
                w.Write(layer.Bounds.Width);
                w.Write(layer.Bounds.Height);
                w.Write(layer.RotationDeg);
                w.Write(layer.ShearX);
                w.Write(layer.ShearY);
                w.Write(layer.FlipH);
                w.Write(layer.FlipV);
                w.Write(layer.Floating);
                WriteEffects(w, layer.Fx);

                var text = layer as TextLayer;
                var shape = layer as ShapeLayer;
                if (text != null)
                {
                    w.Write(text.Text ?? "");
                    w.Write(text.FontFamily ?? "Segoe UI");
                    w.Write(text.FontSize);
                    w.Write(text.Bold);
                    w.Write(text.Italic);
                    w.Write(text.Underline);
                    w.Write(text.Align);
                    w.Write(text.Color.ToArgb());
                    w.Write(text.BackColor.ToArgb());
                    w.Write(text.OutlineColor.ToArgb());
                }
                else if (shape != null)
                {
                    w.Write((int)shape.Kind);
                    w.Write(shape.Stroke.ToArgb());
                    w.Write(shape.StrokeWidth);
                    w.Write(shape.Fill.ToArgb());
                    w.Write(shape.CornerRadius);
                    w.Write(shape.Sides);
                    w.Write(shape.Points.Count);
                    foreach (PointF pt in shape.Points) { w.Write(pt.X); w.Write(pt.Y); }
                }
                else
                {
                    var raster = (RasterLayer)layer;
                    w.Write(raster.ContentVersion);
                    w.Write(px != null);
                    if (px != null)
                    {
                        byte[] png = Png(px);
                        w.Write(png.Length);
                        w.Write(png);
                    }
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        static void WriteEffects(BinaryWriter w, LayerEffects fx)
        {
            w.Write(fx != null);
            if (fx == null) return;
            w.Write(fx.DropShadow);
            w.Write(fx.ShadowColor.ToArgb());
            w.Write(fx.ShadowOpacity);
            w.Write(fx.ShadowAngle);
            w.Write(fx.ShadowDistance);
            w.Write(fx.ShadowSize);
            w.Write(fx.OuterGlow);
            w.Write(fx.GlowColor.ToArgb());
            w.Write(fx.GlowOpacity);
            w.Write(fx.GlowSize);
            w.Write(fx.Stroke);
            w.Write(fx.StrokeColor.ToArgb());
            w.Write(fx.StrokeSize);
            w.Write(fx.StrokePosition);
            w.Write(fx.StrokeOpacity);
            w.Write(fx.ColorOverlay);
            w.Write(fx.OverlayColor.ToArgb());
            w.Write(fx.OverlayOpacity);
        }

        static void WriteSelection(BinaryWriter w, EditorSelection sel)
        {
            w.Write(sel != null);
            if (sel == null) return;
            w.Write(sel.Width);
            w.Write(sel.Height);
            w.Write(sel.Feather);
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, CompressionLevel.Fastest, true)) z.Write(sel.Mask, 0, sel.Mask.Length);
                w.Write((int)ms.Length);
                ms.WriteTo(w.BaseStream);
            }
        }

        // ================================================================== load

        /// <summary>Reads a project. Runs on a worker: the bitmaps it makes are not shared yet.</summary>
        public static EditorProject Load(string path, Action<float, string> progress)
        {
            var p = new EditorProject();
            var made = new List<Bitmap>();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                using (var r = new BinaryReader(fs, Encoding.UTF8))
                {
                    byte[] magic = r.ReadBytes(Magic.Length);
                    if (magic.Length != Magic.Length || Encoding.ASCII.GetString(magic) != "MAPROJ")
                        throw new InvalidDataException("This is not a MicroApp project file.");
                    int version = r.ReadInt32();
                    if (version > FormatVersion)
                        throw new InvalidDataException("This project was saved by a newer MicroApp (" + r.ReadString() + "). Update MicroApp to open it.");
                    r.ReadString();   // the app version that wrote it

                    if (version < 2)
                    {
                        // version 1: one page; its header came before the guides
                        var page = new ProjectPage();
                        ReadPageHeader(r, page, false);
                        p.Ppi = r.ReadSingle();
                        page.Sel = r.ReadInt32();
                        page.NameCounter = r.ReadInt32();
                        ReadGuides(r, p);
                        page.Selection = ReadSelection(r, page.Canvas);
                        page.LastSelection = ReadSelection(r, page.Canvas);
                        ReadLayers(r, fs, page, made, progress, 0, 1);
                        p.Pages.Add(page);
                    }
                    else
                    {
                        p.Ppi = r.ReadSingle();
                        ReadGuides(r, p);
                        p.PageIndex = r.ReadInt32();
                        int pages = r.ReadInt32();
                        if (pages < 1 || pages > 10000) throw new InvalidDataException("The page count in the file is not valid.");
                        for (int i = 0; i < pages; i++)
                        {
                            var page = new ProjectPage { Name = r.ReadString() };
                            ReadPageHeader(r, page, true);
                            page.Selection = ReadSelection(r, page.Canvas);
                            page.LastSelection = ReadSelection(r, page.Canvas);
                            ReadLayers(r, fs, page, made, progress, i, pages);
                            p.Pages.Add(page);
                        }
                    }
                    if (Encoding.ASCII.GetString(r.ReadBytes(3)) != "END")
                        throw new InvalidDataException("The file is damaged (no end marker).");
                }
            }
            catch
            {
                foreach (Bitmap b in made) b.Dispose();
                throw;
            }
            foreach (ProjectPage page in p.Pages) page.Sel = Math.Min(page.Sel, page.Layers.Count - 1);
            p.PageIndex = Math.Max(0, Math.Min(p.Pages.Count - 1, p.PageIndex));
            progress(1f, "Done.");
            return p;
        }

        static void ReadPageHeader(BinaryReader r, ProjectPage page, bool full)
        {
            page.Canvas = new Size(r.ReadInt32(), r.ReadInt32());
            if (page.Canvas.Width < 1 || page.Canvas.Height < 1 || page.Canvas.Width > 100000 || page.Canvas.Height > 100000)
                throw new InvalidDataException("The canvas size in the file is not valid.");
            page.CanvasBg = Color.FromArgb(r.ReadInt32());
            if (!full) return;
            page.Sel = r.ReadInt32();
            page.NameCounter = r.ReadInt32();
        }

        static void ReadGuides(BinaryReader r, EditorProject p)
        {
            p.RulerZero = new PointF(r.ReadSingle(), r.ReadSingle());
            int guides = r.ReadInt32();
            if (guides < 0 || guides > 100000) throw new InvalidDataException("The guide count in the file is not valid.");
            for (int i = 0; i < guides; i++)
                p.Guides.Add(new ProjectGuide { Vertical = r.ReadBoolean(), Pos = r.ReadSingle(), Locked = r.ReadBoolean(), Color = Color.FromArgb(r.ReadInt32()) });
        }

        static void ReadLayers(BinaryReader r, Stream fs, ProjectPage page, List<Bitmap> made, Action<float, string> progress, int pageIndex, int pageCount)
        {
            long length = Math.Max(1, fs.Length);
            int count = r.ReadInt32();
            if (count < 0 || count > 100000) throw new InvalidDataException("The layer count in the file is not valid.");
            for (int i = 0; i < count; i++)
            {
                progress((float)Math.Min(0.99, (double)fs.Position / length),
                         (pageCount > 1 ? "Page " + (pageIndex + 1) + " of " + pageCount + " - " : "") + "layer " + (i + 1) + " of " + count + "…");
                byte kind = r.ReadByte();
                int size = r.ReadInt32();
                if (size < 0) throw new InvalidDataException("The file is damaged.");
                byte[] record = r.ReadBytes(size);
                if (record.Length != size) throw new EndOfStreamException("The file is cut short.");
                EditorLayer layer = ReadLayer(kind, record, made);
                if (layer != null) page.Layers.Add(layer);   // an unknown kind from a newer version is skipped
            }
        }

        static EditorLayer ReadLayer(byte kind, byte[] record, List<Bitmap> made)
        {
            if (kind != KindRaster && kind != KindText && kind != KindShape) return null;
            using (var r = new BinaryReader(new MemoryStream(record), Encoding.UTF8))
            {
                string name = r.ReadString();
                bool visible = r.ReadBoolean(), locked = r.ReadBoolean();
                int opacity = r.ReadInt32();
                var blend = (BlendMode)r.ReadInt32();
                var bounds = new RectangleF(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                float rot = r.ReadSingle(), shearX = r.ReadSingle(), shearY = r.ReadSingle();
                bool flipH = r.ReadBoolean(), flipV = r.ReadBoolean(), floating = r.ReadBoolean();
                LayerEffects fx = ReadEffects(r);

                EditorLayer layer;
                if (kind == KindText)
                {
                    layer = new TextLayer
                    {
                        Text = r.ReadString(),
                        FontFamily = r.ReadString(),
                        FontSize = r.ReadSingle(),
                        Bold = r.ReadBoolean(),
                        Italic = r.ReadBoolean(),
                        Underline = r.ReadBoolean(),
                        Align = r.ReadInt32(),
                        Color = Color.FromArgb(r.ReadInt32()),
                        BackColor = Color.FromArgb(r.ReadInt32()),
                        OutlineColor = Color.FromArgb(r.ReadInt32())
                    };
                }
                else if (kind == KindShape)
                {
                    var shape = new ShapeLayer
                    {
                        Kind = (ShapeKind)r.ReadInt32(),
                        Stroke = Color.FromArgb(r.ReadInt32()),
                        StrokeWidth = r.ReadSingle(),
                        Fill = Color.FromArgb(r.ReadInt32()),
                        CornerRadius = r.ReadInt32(),
                        Sides = r.ReadInt32()
                    };
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++) shape.Points.Add(new PointF(r.ReadSingle(), r.ReadSingle()));
                    layer = shape;
                }
                else
                {
                    int contentVersion = r.ReadInt32();
                    Bitmap bmp = null;
                    if (r.ReadBoolean())
                    {
                        bmp = DecodePng(r.ReadBytes(r.ReadInt32()));
                        made.Add(bmp);
                    }
                    layer = new RasterLayer(bmp) { ContentVersion = contentVersion };
                }

                layer.Name = name;
                layer.Visible = visible;
                layer.Locked = locked;
                layer.Opacity = opacity;
                layer.Blend = blend;
                layer.Bounds = bounds;
                layer.RotationDeg = rot;
                layer.ShearX = shearX;
                layer.ShearY = shearY;
                layer.FlipH = flipH;
                layer.FlipV = flipV;
                layer.Floating = floating;
                layer.Fx = fx;
                return layer;
            }
        }

        /// <summary>Decodes into a fresh 32bpp ARGB bitmap that does not hold on to the stream.</summary>
        static Bitmap DecodePng(byte[] png)
        {
            using (var ms = new MemoryStream(png))
            using (var src = new Bitmap(ms))
            {
                var copy = new PixelCopy { Width = src.Width, Height = src.Height, Pixels = new byte[src.Width * 4 * src.Height] };
                BitmapData data = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < src.Height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, copy.Pixels, y * src.Width * 4, src.Width * 4);
                }
                finally { src.UnlockBits(data); }
                return copy.ToBitmap();
            }
        }

        static LayerEffects ReadEffects(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            return new LayerEffects
            {
                DropShadow = r.ReadBoolean(),
                ShadowColor = Color.FromArgb(r.ReadInt32()),
                ShadowOpacity = r.ReadInt32(),
                ShadowAngle = r.ReadInt32(),
                ShadowDistance = r.ReadInt32(),
                ShadowSize = r.ReadInt32(),
                OuterGlow = r.ReadBoolean(),
                GlowColor = Color.FromArgb(r.ReadInt32()),
                GlowOpacity = r.ReadInt32(),
                GlowSize = r.ReadInt32(),
                Stroke = r.ReadBoolean(),
                StrokeColor = Color.FromArgb(r.ReadInt32()),
                StrokeSize = r.ReadInt32(),
                StrokePosition = r.ReadInt32(),
                StrokeOpacity = r.ReadInt32(),
                ColorOverlay = r.ReadBoolean(),
                OverlayColor = Color.FromArgb(r.ReadInt32()),
                OverlayOpacity = r.ReadInt32()
            };
        }

        static EditorSelection ReadSelection(BinaryReader r, Size canvas)
        {
            if (!r.ReadBoolean()) return null;
            int w = r.ReadInt32(), h = r.ReadInt32(), feather = r.ReadInt32();
            byte[] packed = r.ReadBytes(r.ReadInt32());
            var mask = new byte[w * h];
            using (var z = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress))
            {
                int got = 0, n;
                while (got < mask.Length && (n = z.Read(mask, got, mask.Length - got)) > 0) got += n;
            }
            // a selection only makes sense on the canvas it was made for
            if (w != canvas.Width || h != canvas.Height) return null;
            return EditorSelection.FromMask(mask, canvas, feather);
        }
    }
}
