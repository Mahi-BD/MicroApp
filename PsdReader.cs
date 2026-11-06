using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace MicroApp
{
    /// <summary>
    /// Reads Adobe Photoshop documents (.psd, and the large-document .psb) into raster layers.
    ///
    /// What comes across: every pixel layer with its name, position, opacity, blend mode and
    /// visibility; layer masks and clipping masks are baked into the layer's transparency;
    /// group visibility and opacity are passed down to the layers inside. Text layers and
    /// smart objects arrive as the pixels Photoshop last saved for them. Adjustment layers
    /// and layer styles (shadows, strokes...) have no pixels of their own and are left out;
    /// the importer says so in <see cref="ImportedDocument.Notes"/>.
    ///
    /// RGB, grayscale, indexed, CMYK, Lab, duotone and bitmap documents; 1, 8, 16 and 32 bits
    /// per channel; raw, RLE and ZIP compressed channels. When the layers cannot be used the
    /// flattened copy Photoshop stores at the end of the file is opened instead.
    /// </summary>
    static class PsdReader
    {
        const int MaxSide = 300000;    // PSB's own limit

        public static bool IsPsd(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ext == ".psd" || ext == ".psb";
        }

        // ================================================================ document

        sealed class Ctx
        {
            public bool Big;            // PSB: wider length fields
            public int Depth;           // 1, 8, 16, 32
            public int Mode;            // colour mode, see ColorChannels
            public byte[] Palette;      // indexed: 256 reds, 256 greens, 256 blues
            public int TransparentIndex = -1;
        }

        sealed class LayerRec
        {
            public Rectangle Rect;
            public short[] ChannelIds;
            public long[] ChannelLengths;
            public string BlendKey = "norm";
            public int Opacity = 255;
            public int Fill = 255;
            public bool Clipping, Hidden;
            public string Name = "";
            public int Section;                  // 0 layer, 1/2 group start (open/closed), 3 group end
            public Rectangle MaskRect;
            public int MaskDefault = 255;
            public bool MaskDisabled;
            public byte[] MaskPlane;             // the layer mask's pixels, MaskRect-sized
            public bool HasMaskData;             // a mask block is present (Photoshop's "layer.mask")
            public Rectangle RealMaskRect;       // the "real" mask: Photoshop's rendered result, feather included
            public int RealMaskDefault = 255;
            public byte[] RealMaskPlane;
            public int? UserDensity, VectorDensity;   // mask density, 0..255
            public VectorShape Vector;           // vector mask, null when none
            public bool Text, Smart, Adjustment, Effects;
            public bool FillLayer;               // Solid Color / Gradient / Pattern fill (shape layers are fills too)
            public Color? SolidColor;            // the colour of a Solid Color fill
            public Pixels Px;                    // decoded pixels, null when the layer has none
        }

        /// <summary>A vector mask: subpaths in document-relative coordinates (0..1), combined in order.</summary>
        sealed class VectorShape
        {
            public bool Invert, Disabled;
            public bool StartFilled;             // the "initial fill rule": counts only for a mask with no subpaths
            public readonly List<SubPath> Paths = new List<SubPath>();
        }

        sealed class SubPath
        {
            public int Op = 1;                   // 0 exclude, 1 union, 2 subtract, 3 intersect, -1 part of the previous one
            public bool Closed;
            public readonly List<PointF[]> Knots = new List<PointF[]>();   // preceding control, anchor, leaving control
        }

        public static ImportedDocument Load(string path, Action<float, string> report)
        {
            if (report == null) report = delegate { };
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                var r = new BeReader(fs);
                if (fs.Length < 26 || r.Sig() != "8BPS")
                    throw new InvalidDataException("This is not a Photoshop file.");
                int version = r.U16();
                if (version != 1 && version != 2)
                    throw new InvalidDataException("This Photoshop file version (" + version + ") is not supported.");
                var ctx = new Ctx { Big = version == 2 };
                r.Skip(6);
                int channels = r.U16();
                int height = (int)r.U32();
                int width = (int)r.U32();
                ctx.Depth = r.U16();
                ctx.Mode = r.U16();
                if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide)
                    throw new InvalidDataException("The picture size in this file is not valid.");
                if (ctx.Depth != 1 && ctx.Depth != 8 && ctx.Depth != 16 && ctx.Depth != 32)
                    throw new InvalidDataException("Unsupported bit depth: " + ctx.Depth + " bits per channel.");
                if (ColorChannels(ctx.Mode) == 0)
                    throw new InvalidDataException("Unsupported colour mode in this Photoshop file.");
                if ((long)width * height > 400L * 1000 * 1000)
                    throw new InvalidDataException("This document is " + width + " × " + height +
                        " pixels - too big to open. Reduce the image size in Photoshop first.");

                // colour mode data: the palette of an indexed image
                long cmLen = r.U32();
                long cmEnd = r.Pos + cmLen;
                if (ctx.Mode == 2 && cmLen >= 768) ctx.Palette = r.Bytes(768);
                r.Pos = cmEnd;

                // image resources: resolution, transparent palette index
                long resLen = r.U32();
                long resEnd = r.Pos + resLen;
                float ppi = 72;
                ReadResources(r, resEnd, ctx, ref ppi);
                r.Pos = resEnd;

                var doc = new ImportedDocument { Ppi = ppi };
                var page = new ImportedPage { Name = "Page 1", Width = width, Height = height };
                doc.Pages.Add(page);

                long lmLen = ctx.Big ? r.U64() : r.U32();
                long lmEnd = r.Pos + lmLen;
                List<LayerRec> recs = null;
                bool mergedAlpha = false;
                if (lmLen > 0)
                {
                    try
                    {
                        recs = ReadLayerSection(r, ctx, lmEnd, out mergedAlpha, report);
                    }
                    catch (OutOfMemoryException) { throw; }
                    catch (Exception ex)
                    {
                        recs = null;
                        doc.Notes.Add("The layers could not be read (" + ex.Message + "), so the flattened picture was opened instead.");
                    }
                }

                if (recs != null) BuildLayers(recs, page, doc.Notes, width, height);

                if (page.Layers.Count == 0)
                {
                    // no usable layers: the flattened copy at the end of the file
                    report(0.9f, "Reading the flattened image");
                    r.Pos = lmEnd;
                    Pixels px;
                    try { px = ReadComposite(r, ctx, width, height, channels, mergedAlpha); }
                    catch (EndOfStreamException)
                    {
                        if (recs == null) throw;
                        // the layers were read but none has pixels, and there is no flattened copy: an empty canvas
                        px = new Pixels(width, height);
                        doc.Notes.Add("This document has no pixels to show.");
                    }
                    page.Layers.Add(new ImportedLayer
                    {
                        Name = recs == null ? "Background" : "Layer 1",
                        Image = px.ToBitmap(),
                        Bounds = new Rectangle(0, 0, width, height)
                    });
                }

                foreach (ImportedLayer l in page.Layers)
                    if (l.Image != null) l.Image.SetResolution(ppi, ppi);
                report(1f, null);
                return doc;
            }
        }

        static int ColorChannels(int mode)
        {
            switch (mode)
            {
                case 0: return 1;   // bitmap
                case 1: return 1;   // grayscale
                case 2: return 1;   // indexed
                case 3: return 3;   // RGB
                case 4: return 4;   // CMYK
                case 7: return 1;   // multichannel: first channel shown as gray
                case 8: return 1;   // duotone: shown as gray
                case 9: return 3;   // Lab
                default: return 0;
            }
        }

        static void ReadResources(BeReader r, long end, Ctx ctx, ref float ppi)
        {
            try
            {
                while (r.Pos + 12 <= end)
                {
                    r.Sig();                       // "8BIM" (or another vendor's code)
                    int id = r.U16();
                    int nameLen = r.U8();
                    r.Skip(nameLen + ((nameLen + 1) % 2));   // Pascal name padded to even
                    long size = r.U32();
                    long dataEnd = r.Pos + size;
                    if (dataEnd > end) return;
                    if (id == 0x03ED && size >= 16)
                    {
                        // ResolutionInfo: horizontal resolution, fixed 16.16, unit 1 = per inch, 2 = per cm
                        double h = r.U32() / 65536.0;
                        int unit = r.U16();
                        if (unit == 2) h *= 2.54;
                        if (h >= 1 && h <= 10000) ppi = (float)Math.Round(h, 2);
                    }
                    else if (id == 0x0417 && size >= 2)
                    {
                        ctx.TransparentIndex = r.U16();
                    }
                    r.Pos = dataEnd + (size % 2);
                }
            }
            catch (EndOfStreamException) { }
        }

        // ================================================================ layers

        static List<LayerRec> ReadLayerSection(BeReader r, Ctx ctx, long end, out bool mergedAlpha, Action<float, string> report)
        {
            mergedAlpha = false;
            List<LayerRec> recs = null;

            long liLen = ctx.Big ? r.U64() : r.U32();
            long liEnd = r.Pos + liLen;
            if (liLen > 0) recs = ReadLayerInfo(r, ctx, liEnd, out mergedAlpha, report);
            r.Pos = liEnd;

            // global layer mask info
            if (r.Pos + 4 <= end) r.Skip(r.U32());

            // 16- and 32-bit documents keep their layers in a tagged block here instead
            while (r.Pos + 12 <= end && AlignToSignature(r, end))
            {
                r.Sig();
                string key = r.Sig();
                long len = ctx.Big && IsLongKey(key) ? r.U64() : r.U32();
                long dataStart = r.Pos;
                if (dataStart + len > end) break;
                if ((key == "Lr16" || key == "Lr32" || key == "Layr") && (recs == null || recs.Count == 0))
                {
                    bool alpha;
                    recs = ReadLayerInfo(r, ctx, dataStart + len, out alpha, report);
                    mergedAlpha = alpha;
                }
                r.Pos = dataStart + len;
            }
            return recs;
        }

        static List<LayerRec> ReadLayerInfo(BeReader r, Ctx ctx, long end, out bool mergedAlpha, Action<float, string> report)
        {
            int count = r.I16();
            mergedAlpha = count < 0;     // the first alpha channel of the flattened image is its transparency
            count = Math.Abs(count);
            var recs = new List<LayerRec>(count);
            for (int i = 0; i < count; i++) recs.Add(ReadRecord(r, ctx));

            for (int i = 0; i < recs.Count; i++)
            {
                report(0.05f + 0.85f * i / Math.Max(1, recs.Count), "Reading layer " + (i + 1) + " of " + recs.Count);
                ReadLayerPixels(r, ctx, recs[i], end);
            }
            return recs;
        }

        static LayerRec ReadRecord(BeReader r, Ctx ctx)
        {
            var L = new LayerRec();
            int top = r.I32(), left = r.I32(), bottom = r.I32(), right = r.I32();
            if (right < left || bottom < top || right - left > MaxSide || bottom - top > MaxSide)
                throw new InvalidDataException("a layer has an invalid size");
            L.Rect = Rectangle.FromLTRB(left, top, right, bottom);

            int nch = r.U16();
            if (nch > 64) throw new InvalidDataException("a layer has too many channels");
            L.ChannelIds = new short[nch];
            L.ChannelLengths = new long[nch];
            for (int c = 0; c < nch; c++)
            {
                L.ChannelIds[c] = r.I16();
                L.ChannelLengths[c] = ctx.Big ? r.U64() : r.U32();
            }

            if (r.Sig() != "8BIM") throw new InvalidDataException("a layer record is damaged");
            L.BlendKey = r.Sig();
            L.Opacity = r.U8();
            L.Clipping = r.U8() != 0;
            int flags = r.U8();
            L.Hidden = (flags & 0x02) != 0;
            r.U8();   // filler

            long extraLen = r.U32();
            long extraEnd = r.Pos + extraLen;

            // layer mask
            long maskLen = r.U32();
            long maskEnd = r.Pos + maskLen;
            if (maskLen >= 18)
            {
                int mt = r.I32(), ml = r.I32(), mb = r.I32(), mr = r.I32();
                if (mr >= ml && mb >= mt) L.MaskRect = Rectangle.FromLTRB(ml, mt, mr, mb);
                L.MaskDefault = r.U8();
                int mflags = r.U8();
                L.MaskDisabled = (mflags & 0x02) != 0;
                L.HasMaskData = true;
                // the order below follows real files, not the published spec: the real-mask
                // header (only when the layer stores a -3 channel) comes before the parameters
                if (Array.IndexOf(L.ChannelIds, (short)-3) >= 0 && maskLen >= 36)
                {
                    r.U8();                       // real flags
                    L.RealMaskDefault = r.U8();
                    int rt = r.I32(), rl = r.I32(), rb = r.I32(), rr = r.I32();
                    if (rr >= rl && rb >= rt) L.RealMaskRect = Rectangle.FromLTRB(rl, rt, rr, rb);
                }
                if ((mflags & 0x10) != 0 && r.Pos < maskEnd)
                {
                    int p = r.U8();
                    if ((p & 1) != 0) L.UserDensity = r.U8();
                    if ((p & 2) != 0) r.Skip(8);  // user mask feather (already in the real mask)
                    if ((p & 4) != 0) L.VectorDensity = r.U8();
                    if ((p & 8) != 0) r.Skip(8);  // vector mask feather
                }
            }
            r.Pos = maskEnd;

            // blending ranges
            r.Skip(r.U32());

            // Pascal name, padded to a multiple of 4
            int nameLen = r.U8();
            byte[] nameBytes = r.Bytes(nameLen);
            L.Name = Win1252(nameBytes);
            r.Skip((4 - (1 + nameLen) % 4) % 4);

            // additional layer information
            while (r.Pos + 12 <= extraEnd && AlignToSignature(r, extraEnd))
            {
                r.Sig();
                string key = r.Sig();
                long len = ctx.Big && IsLongKey(key) ? r.U64() : r.U32();
                long ds = r.Pos;
                if (ds + len > extraEnd) break;
                switch (key)
                {
                    case "luni":   // Unicode name
                        if (len >= 4)
                        {
                            long n = r.U32();
                            if (n > 0 && n * 2 <= len - 4)
                            {
                                string uni = Encoding.BigEndianUnicode.GetString(r.Bytes(n * 2)).TrimEnd('\0');
                                if (uni.Length > 0) L.Name = uni;
                            }
                        }
                        break;
                    case "lsct":   // group start / end
                    case "lsdk":
                        if (len >= 4) L.Section = (int)r.U32();
                        break;
                    case "iOpa":   // fill opacity
                        if (len >= 1) L.Fill = r.U8();
                        break;
                    case "TySh": case "tySh":
                        L.Text = true; break;
                    case "SoLd": case "PlLd": case "plLd": case "SoLE":
                        L.Smart = true; break;
                    case "lfx2": case "lrFX": case "lmfx":
                        L.Effects = true; break;
                    case "SoCo":
                        L.FillLayer = true;
                        L.SolidColor = Linear32(ReadSolidColor(r, ds + len), ctx);
                        break;
                    case "GdFl": case "PtFl":
                        L.FillLayer = true; break;
                    case "vscg":   // newer Photoshop: a shape's fill content, tagged with its kind
                        if (len >= 8)
                        {
                            L.FillLayer = true;
                            if (r.Sig() == "SoCo") L.SolidColor = Linear32(ReadSolidColor(r, ds + len), ctx);
                        }
                        break;
                    case "vmsk": case "vsms":
                        if (L.Vector == null) L.Vector = ReadVectorShape(r, ds + len);
                        break;
                    case "levl": case "curv": case "brit": case "hue ": case "hue2": case "blnc":
                    case "selc": case "mixr": case "grdm": case "phfl": case "expA": case "vibA":
                    case "thrs": case "post": case "nvrt": case "blwh": case "clrL":
                        L.Adjustment = true; break;
                }
                r.Pos = ds + len;
            }
            r.Pos = extraEnd;
            return L;
        }

        static void ReadLayerPixels(BeReader r, Ctx ctx, LayerRec L, long end)
        {
            int w = L.Rect.Width, h = L.Rect.Height;
            int colorCh = ColorChannels(ctx.Mode);
            var color = new byte[colorCh][];
            byte[] alpha = null, mask = null;

            for (int c = 0; c < L.ChannelIds.Length; c++)
            {
                long start = r.Pos;
                long length = L.ChannelLengths[c];
                if (start + length > end) throw new InvalidDataException("a layer's pixels run past the end of the file");
                short id = L.ChannelIds[c];
                int cw = w, chh = h;
                if (id == -2) { cw = L.MaskRect.Width; chh = L.MaskRect.Height; }
                if (id == -3) { cw = L.RealMaskRect.Width; chh = L.RealMaskRect.Height; }
                bool wanted = (id >= 0 && id < colorCh) || id == -1 || ((id == -2 || id == -3) && !L.MaskDisabled);
                if (length >= 2 && wanted && cw > 0 && chh > 0)
                {
                    int compression = r.U16();
                    byte[] plane = DecodePlane(r, compression, cw, chh, ctx, start + length, id >= 0 && ctx.Mode != 2);
                    if (id >= 0) color[id] = plane;
                    else if (id == -1) alpha = plane;
                    else if (id == -2) mask = plane;
                    else L.RealMaskPlane = plane;
                }
                r.Pos = start + length;
            }

            if (mask != null && !L.MaskRect.IsEmpty) L.MaskPlane = mask;   // applied in BuildLayers (groups have masks too)
            if (w <= 0 || h <= 0) return;
            L.Px = Compose(w, h, color, alpha, ctx);
        }

        /// <summary>A mask that scales transparency: a MaskRect-sized plane, the default value outside it.</summary>
        sealed class AlphaMask
        {
            public byte[] Plane;
            public Rectangle Rect;
            public int Default;
        }

        /// <summary>What a clipped layer is cut to: the base layer's (or group's) transparency.</summary>
        sealed class ClipBase
        {
            public byte[] Alpha;                 // Rect-sized, null: the base has no pixels
            public Rectangle Rect;
            public float Opacity = 1f;
            public bool Hidden;
        }

        /// <summary>An open group while walking up through its contents.</summary>
        sealed class GroupFrame
        {
            public bool Hidden;
            public float Opacity;
            public readonly List<AlphaMask> Masks = new List<AlphaMask>();   // this group's and every outer group's
            public byte[] Union;                 // canvas-sized, only when a layer above clips to this group
            public ClipBase OuterClip;           // the group is itself clipped: the base it clips to
        }

        /// <summary>
        /// Turns the records (bottom first) into editor layers. Groups hand their visibility,
        /// opacity and masks down to the layers inside; layer masks and vector masks are applied;
        /// clipped layers are cut to the layer or group they clip to; Solid Color fills saved
        /// without pixels are drawn from their colour and shape. Anything else without pixels is
        /// counted for the notes.
        /// </summary>
        static void BuildLayers(List<LayerRec> recs, ImportedPage page, List<string> notes, int docW, int docH)
        {
            int n = recs.Count;
            var canvas = new Rectangle(0, 0, docW, docH);

            // pair each group's bottom divider (3) with its folder record (1/2) above the contents
            var folderOf = new Dictionary<int, int>();
            var open = new Stack<int>();
            for (int i = 0; i < n; i++)
            {
                if (recs[i].Section == 3) open.Push(i);
                else if ((recs[i].Section == 1 || recs[i].Section == 2) && open.Count > 0) folderOf[open.Pop()] = i;
            }

            var frames = new Stack<GroupFrame>();
            ClipBase clipBase = null;
            int text = 0, smart = 0, adjust = 0, effects = 0, skippedFill = 0, unnamed = 0;
            var odd = new SortedSet<string>();

            for (int i = 0; i < n; i++)
            {
                LayerRec L = recs[i];
                GroupFrame outer = frames.Count > 0 ? frames.Peek() : null;

                if (L.Section == 3)
                {
                    // entering a group: its settings live on the folder record above the contents
                    int f;
                    LayerRec folder = folderOf.TryGetValue(i, out f) ? recs[f] : null;
                    var frame = new GroupFrame
                    {
                        Hidden = (outer != null && outer.Hidden) || (folder != null && folder.Hidden),
                        Opacity = (outer != null ? outer.Opacity : 1f) * (folder != null ? folder.Opacity / 255f : 1f)
                    };
                    if (outer != null) frame.Masks.AddRange(outer.Masks);
                    if (folder != null && folder.Clipping && clipBase != null)
                    {
                        // the whole group is clipped to the layer below it
                        frame.OuterClip = clipBase;
                        frame.Masks.Add(clipBase.Alpha != null
                            ? new AlphaMask { Plane = clipBase.Alpha, Rect = clipBase.Rect, Default = 0 }
                            : new AlphaMask { Plane = new byte[0], Rect = Rectangle.Empty, Default = 0 });
                        frame.Hidden = frame.Hidden || clipBase.Hidden;
                        frame.Opacity *= clipBase.Opacity;
                    }
                    if (folder != null)
                    {
                        AlphaMask m = RasterMaskOf(folder);
                        if (m != null) frame.Masks.Add(m);
                        AlphaMask vm = VectorMaskOf(folder, canvas, docW, docH);
                        if (vm != null) frame.Masks.Add(vm);
                        if (f + 1 < n && recs[f + 1].Clipping && recs[f + 1].Section == 0)
                            frame.Union = new byte[(long)docW * docH];
                    }
                    frames.Push(frame);
                    clipBase = null;              // clipping never reaches across a group's edge
                    continue;
                }
                if (L.Section == 1 || L.Section == 2)
                {
                    // leaving a group: it becomes the base for clipped layers right above it
                    GroupFrame done = frames.Count > 0 ? frames.Pop() : null;
                    if (done != null && done.OuterClip != null) { clipBase = done.OuterClip; continue; }   // the clipping run goes on
                    clipBase = new ClipBase
                    {
                        Alpha = done != null ? done.Union : null,
                        Rect = canvas,
                        Opacity = L.Opacity / 255f,
                        Hidden = L.Hidden
                    };
                    continue;
                }

                if (L.Adjustment) adjust++;
                if (L.Effects) effects++;
                if (L.Text) text++;
                if (L.Smart) smart++;

                bool hidden = (outer != null && outer.Hidden) || L.Hidden;
                float opacity = (outer != null ? outer.Opacity : 1f) * (L.Opacity / 255f) * (L.Fill / 255f);

                // a Solid Color fill saved without pixels: draw it from its colour and shape
                bool hadPixels = L.Px != null && L.Rect.Width > 0 && L.Rect.Height > 0;
                if (!hadPixels && L.SolidColor.HasValue && !L.Adjustment)
                {
                    Rectangle area = canvas;
                    if (L.Vector != null && !L.Vector.Disabled && !L.Vector.Invert)
                        area = Rectangle.Intersect(canvas, PathBounds(L.Vector, docW, docH));
                    if (area.Width > 0 && area.Height > 0)
                    {
                        L.Px = Solid(area.Width, area.Height, L.SolidColor.Value);
                        L.Rect = area;
                    }
                }
                else if (!hadPixels && L.FillLayer && !L.Adjustment)
                {
                    skippedFill++;
                }

                bool hasPx = L.Px != null && L.Rect.Width > 0 && L.Rect.Height > 0;
                if (hasPx)
                {
                    // A layer saved without pixels takes its shape from the vector mask. Pixel layers
                    // are saved uncut, so their vector mask is applied here - unless Photoshop saved a
                    // combined ("real") mask, which already includes it. Shape layers save pixels
                    // that are already cut to their path.
                    bool applyVector = L.Vector != null && !L.Vector.Disabled &&
                        (!hadPixels || (!L.FillLayer && L.RealMaskPlane == null));
                    if (applyVector) ApplyVector(L.Px, L.Rect, L.Vector, L.VectorDensity, docW, docH);
                    AlphaMask own = RasterMaskOf(L);
                    if (own != null) ApplyMask(L.Px, L.Rect, own);
                    if (outer != null) foreach (AlphaMask m in outer.Masks) ApplyMask(L.Px, L.Rect, m);
                }

                if (L.Clipping && clipBase != null)
                {
                    // clipped: only where the base has pixels; the base's opacity and visibility apply too
                    if (hasPx)
                    {
                        if (clipBase.Alpha == null) { L.Px = null; hasPx = false; }
                        else ClipTo(L.Px, L.Rect, clipBase.Alpha, clipBase.Rect);
                    }
                    hidden = hidden || clipBase.Hidden;
                    opacity *= clipBase.Opacity;
                }
                else
                {
                    // a possible base for the layers above; keep only its transparency, and only when needed
                    int ff;
                    bool isBase = i + 1 < n &&
                        ((recs[i + 1].Clipping && recs[i + 1].Section == 0) ||
                         (recs[i + 1].Section == 3 && folderOf.TryGetValue(i + 1, out ff) && recs[ff].Clipping));
                    clipBase = null;
                    if (isBase)
                        clipBase = new ClipBase
                        {
                            Alpha = hasPx ? AlphaOf(L.Px) : null,
                            Rect = L.Rect,
                            Opacity = L.Opacity / 255f,
                            Hidden = L.Hidden
                        };
                }

                if (!hasPx) continue;

                if (!hidden)
                {
                    // every enclosing group that a layer above clips to needs this layer's coverage
                    byte[] cover = null;
                    foreach (GroupFrame fr in frames)
                    {
                        if (fr.Union == null) continue;
                        if (cover == null) cover = AlphaOf(L.Px);
                        UnionInto(fr.Union, docW, cover, L.Rect, Math.Max(0f, Math.Min(1f, L.Opacity / 255f * L.Fill / 255f)));
                    }
                }

                BlendMode blend;
                if (!MapBlend(L.BlendKey, out blend)) odd.Add(BlendName(L.BlendKey));

                string name = (L.Name ?? "").Trim();
                if (name.Length == 0) name = "Layer " + (++unnamed);
                page.Layers.Add(new ImportedLayer
                {
                    Name = name,
                    Image = L.Px.ToBitmap(),
                    Bounds = L.Rect,
                    Opacity = (int)Math.Round(Math.Max(0f, Math.Min(1f, opacity)) * 100),
                    Visible = !hidden,
                    Blend = blend
                });
                L.Px = null;                      // the bitmap has it now
                L.MaskPlane = null;
                L.RealMaskPlane = null;
            }

            if (text > 0) notes.Add(Plural(text, "text layer") + " came in as pixels - the text is no longer editable.");
            if (smart > 0) notes.Add(Plural(smart, "smart object") + " came in as pixels.");
            if (adjust > 0) notes.Add(Plural(adjust, "adjustment layer") + " left out - MicroApp cannot apply Photoshop adjustment layers.");
            if (effects > 0) notes.Add("Layer styles (shadows, strokes, glows...) on " + Plural(effects, "layer") + " are not drawn.");
            if (skippedFill > 0) notes.Add(Plural(skippedFill, "gradient or pattern fill layer") + " left out (no pixels saved).");
            if (odd.Count > 0) notes.Add("Shown as Normal: the " + string.Join(", ", odd) + " blend mode" + (odd.Count > 1 ? "s." : "."));
        }

        /// <summary>The layer mask - Photoshop's rendered "real" mask when it saved one - with its density folded in.</summary>
        static AlphaMask RasterMaskOf(LayerRec L)
        {
            if (!L.HasMaskData || L.MaskDisabled) return null;
            AlphaMask m = null;
            if (L.RealMaskPlane != null && !L.RealMaskRect.IsEmpty)
                m = new AlphaMask { Plane = L.RealMaskPlane, Rect = L.RealMaskRect, Default = L.RealMaskDefault };
            else if (L.MaskPlane != null && !L.MaskRect.IsEmpty)
                m = new AlphaMask { Plane = L.MaskPlane, Rect = L.MaskRect, Default = L.MaskDefault };
            if (m == null) return null;
            int density = L.UserDensity ?? L.VectorDensity ?? 255;
            if (density < 255) Densify(m, density);
            return m;
        }

        /// <summary>Density: hidden areas of a mask let <c>255 - density</c> through.</summary>
        static void Densify(AlphaMask m, int density)
        {
            var plane = new byte[m.Plane.Length];
            for (int i = 0; i < plane.Length; i++) plane[i] = (byte)(255 - density * (255 - m.Plane[i]) / 255);
            m.Plane = plane;
            m.Default = 255 - density * (255 - m.Default) / 255;
        }

        /// <summary>A group's vector mask as a raster mask over the path's box (outside it: hidden, or shown when inverted).</summary>
        static AlphaMask VectorMaskOf(LayerRec L, Rectangle canvas, int docW, int docH)
        {
            if (L.Vector == null || L.Vector.Disabled) return null;
            Rectangle box = Rectangle.Intersect(canvas, PathBounds(L.Vector, docW, docH));
            if (box.Width <= 0 || box.Height <= 0)
                return new AlphaMask { Plane = new byte[0], Rect = Rectangle.Empty, Default = L.Vector.Invert ? 255 : 0 };
            var m = new AlphaMask { Plane = RasterizeVector(L.Vector, box, docW, docH), Rect = box, Default = L.Vector.Invert ? 255 : 0 };
            if (L.VectorDensity.HasValue && L.VectorDensity.Value < 255) Densify(m, L.VectorDensity.Value);
            return m;
        }

        /// <summary>Scales a layer's transparency by a mask; outside the mask's box its default value applies.</summary>
        static void ApplyMask(Pixels px, Rectangle layer, AlphaMask m)
        {
            for (int y = 0; y < px.Height; y++)
            {
                int dy = layer.Y + y - m.Rect.Y;
                bool rowIn = dy >= 0 && dy < m.Rect.Height;
                for (int x = 0; x < px.Width; x++)
                {
                    int dx = layer.X + x - m.Rect.X;
                    int v = rowIn && dx >= 0 && dx < m.Rect.Width ? m.Plane[dy * m.Rect.Width + dx] : m.Default;
                    if (v == 255) continue;
                    int i = px.Index(x, y) + 3;
                    px.Data[i] = (byte)(px.Data[i] * v / 255);
                }
            }
        }

        static void ApplyVector(Pixels px, Rectangle rect, VectorShape v, int? density, int docW, int docH)
        {
            var m = new AlphaMask { Plane = RasterizeVector(v, rect, docW, docH), Rect = rect, Default = 0 };
            if (density.HasValue && density.Value < 255) Densify(m, density.Value);
            ApplyMask(px, rect, m);
        }

        static void ClipTo(Pixels px, Rectangle rect, byte[] baseAlpha, Rectangle baseRect)
        {
            for (int y = 0; y < px.Height; y++)
            {
                int by = rect.Y + y - baseRect.Y;
                bool rowIn = by >= 0 && by < baseRect.Height;
                for (int x = 0; x < px.Width; x++)
                {
                    int bx = rect.X + x - baseRect.X;
                    int a = rowIn && bx >= 0 && bx < baseRect.Width ? baseAlpha[by * baseRect.Width + bx] : 0;
                    int i = px.Index(x, y) + 3;
                    px.Data[i] = (byte)(px.Data[i] * a / 255);
                }
            }
        }

        static byte[] AlphaOf(Pixels px)
        {
            var a = new byte[px.Width * px.Height];
            for (int i = 0; i < a.Length; i++) a[i] = px.Data[i * 4 + 3];
            return a;
        }

        /// <summary>Adds a layer's coverage into a canvas-sized union (alpha "over").</summary>
        static void UnionInto(byte[] union, int docW, byte[] alpha, Rectangle rect, float opacity)
        {
            int docH = union.Length / Math.Max(1, docW);
            for (int y = 0; y < rect.Height; y++)
            {
                int cy = rect.Y + y;
                if (cy < 0 || cy >= docH) continue;
                for (int x = 0; x < rect.Width; x++)
                {
                    int cx = rect.X + x;
                    if (cx < 0 || cx >= docW) continue;
                    int a = (int)(alpha[y * rect.Width + x] * opacity);
                    int u = union[cy * docW + cx];
                    union[cy * docW + cx] = (byte)(u + a - u * a / 255);
                }
            }
        }

        /// <summary>In a 32-bit document a fill colour is linear light; bring it to screen gamma like the pixels.</summary>
        static Color? Linear32(Color? c, Ctx ctx)
        {
            if (!c.HasValue || ctx.Depth != 32) return c;
            Func<int, int> g = v => (int)Math.Round(Math.Pow(v / 255.0, 1 / 2.2) * 255);
            return Color.FromArgb(g(c.Value.R), g(c.Value.G), g(c.Value.B));
        }

        static Pixels Solid(int w, int h, Color c)
        {
            var px = new Pixels(w, h);
            for (int i = 0; i < px.Data.Length; i += 4)
            {
                px.Data[i] = c.B; px.Data[i + 1] = c.G; px.Data[i + 2] = c.R; px.Data[i + 3] = 255;
            }
            return px;
        }

        // ================================================================ vector masks

        static VectorShape ReadVectorShape(BeReader r, long end)
        {
            try
            {
                var v = new VectorShape();
                r.U32();                          // version 3
                uint flags = r.U32();
                v.Invert = (flags & 1) != 0;
                v.Disabled = (flags & 4) != 0;
                SubPath cur = null;
                while (r.Pos + 26 <= end)
                {
                    int selector = r.U16();
                    long next = r.Pos + 24;
                    switch (selector)
                    {
                        case 0:   // closed subpath length record
                        case 3:   // open subpath length record
                            r.U16();              // knot count
                            cur = new SubPath { Op = r.I16(), Closed = selector == 0 };
                            v.Paths.Add(cur);
                            break;
                        case 1: case 2: case 4: case 5:   // knots
                            if (cur != null)
                            {
                                var k = new PointF[3];
                                for (int p = 0; p < 3; p++)
                                {
                                    float y = r.I32() / 16777216f;   // fixed 8.24, fraction of the document
                                    float x = r.I32() / 16777216f;
                                    k[p] = new PointF(x, y);
                                }
                                cur.Knots.Add(k);
                            }
                            break;
                        case 8:   // initial fill rule
                            v.StartFilled = r.U16() == 1;
                            break;
                    }
                    r.Pos = next;
                }
                return v.Paths.Count > 0 || v.StartFilled ? v : null;
            }
            catch (EndOfStreamException) { return null; }
        }

        static System.Drawing.Drawing2D.GraphicsPath PathOf(SubPath sp, float ox, float oy, int docW, int docH)
        {
            var gp = new System.Drawing.Drawing2D.GraphicsPath(System.Drawing.Drawing2D.FillMode.Winding);
            int count = sp.Knots.Count;
            Func<PointF, PointF> map = p => new PointF(p.X * docW - ox, p.Y * docH - oy);
            for (int i = 0; i < count; i++)
            {
                int j = i + 1;
                if (j == count) { if (!sp.Closed) break; j = 0; }
                gp.AddBezier(map(sp.Knots[i][1]), map(sp.Knots[i][2]), map(sp.Knots[j][0]), map(sp.Knots[j][1]));
            }
            if (sp.Closed) gp.CloseFigure();
            return gp;
        }

        static Rectangle PathBounds(VectorShape v, int docW, int docH)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (SubPath sp in v.Paths)
                foreach (PointF[] k in sp.Knots)
                    foreach (PointF p in k)
                    {
                        minX = Math.Min(minX, p.X * docW); maxX = Math.Max(maxX, p.X * docW);
                        minY = Math.Min(minY, p.Y * docH); maxY = Math.Max(maxY, p.Y * docH);
                    }
            bool subtractFirst = v.Paths.Count > 0 && v.Paths[0].Op == 2;
            if (minX > maxX || v.Invert || subtractFirst) return new Rectangle(0, 0, docW, docH);
            return Rectangle.FromLTRB((int)Math.Floor(minX) - 1, (int)Math.Floor(minY) - 1, (int)Math.Ceiling(maxX) + 1, (int)Math.Ceiling(maxY) + 1);
        }

        /// <summary>
        /// The vector mask over <paramref name="rect"/> as coverage 0..255, the way Photoshop
        /// combines shapes: subpaths flagged -1 join the one before into a single even-odd shape;
        /// each shape is then united, subtracted, intersected or excluded with what came before.
        /// A first shape that subtracts or intersects starts from a covered surface.
        /// </summary>
        static byte[] RasterizeVector(VectorShape v, Rectangle rect, int docW, int docH)
        {
            int w = Math.Max(1, rect.Width), h = Math.Max(1, rect.Height);
            var acc = new byte[w * h];
            if (v.StartFilled && v.Paths.Count == 0) for (int i = 0; i < acc.Length; i++) acc[i] = 255;

            var shapes = new List<List<SubPath>>();
            foreach (SubPath sp in v.Paths)
            {
                if (sp.Op == -1 && shapes.Count > 0) shapes[shapes.Count - 1].Add(sp);
                else shapes.Add(new List<SubPath> { sp });
            }

            using (var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                var buf = new byte[w * h * 4];
                bool first = true;
                foreach (List<SubPath> shape in shapes)
                {
                    using (var gp = new System.Drawing.Drawing2D.GraphicsPath(System.Drawing.Drawing2D.FillMode.Alternate))
                    {
                        foreach (SubPath sp in shape)
                            if (sp.Knots.Count >= 2)
                                using (var part = PathOf(sp, rect.X, rect.Y, docW, docH)) gp.AddPath(part, false);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                            if (gp.PointCount > 0) g.FillPath(Brushes.White, gp);
                        }
                    }
                    var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int y = 0; y < h; y++)
                            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, buf, y * w * 4, w * 4);
                    }
                    finally { bmp.UnlockBits(data); }

                    int op = shape[0].Op;
                    bool invertFirst = first && (op == 2 || op == 3);
                    for (int i = 0; i < acc.Length; i++)
                    {
                        int s = buf[i * 4 + 3], a = acc[i];
                        if (invertFirst) a = 255 - a;
                        switch (op)
                        {
                            case 0: a = a + s - 2 * a * s / 255; break;      // exclude
                            case 2: a = Math.Max(0, a - s); break;           // subtract
                            case 3: a = a * s / 255; break;                  // intersect
                            default: a = a + s - a * s / 255; break;         // union
                        }
                        acc[i] = (byte)Math.Max(0, Math.Min(255, a));
                    }
                    first = false;
                }
            }
            if (v.Invert) for (int i = 0; i < acc.Length; i++) acc[i] = (byte)(255 - acc[i]);
            return acc;
        }

        // ================================================================ Solid Color fill

        /// <summary>The colour of a Solid Color fill ("SoCo": version, then a descriptor holding 'Clr ').</summary>
        static Color? ReadSolidColor(BeReader r, long end)
        {
            try
            {
                r.U32();                          // version 16
                var d = Descriptor.Read(r, end);
                object clr;
                var c = d.TryGetValue("Clr ", out clr) ? clr as Dictionary<string, object> : null;
                return c == null ? (Color?)null : Descriptor.ToColor(c);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
        }

        /// <summary>
        /// Just enough of Photoshop's action-descriptor format to read a colour: dictionaries,
        /// numbers, text, enums and lists. Anything else stops the read (the caller gives up).
        /// </summary>
        static class Descriptor
        {
            public static Dictionary<string, object> Read(BeReader r, long end)
            {
                UnicodeString(r);                 // class name
                Key(r);                           // class id
                uint count = r.U32();
                if (count > 10000) throw new InvalidDataException("descriptor");
                var d = new Dictionary<string, object>();
                for (uint i = 0; i < count && r.Pos < end; i++)
                {
                    string key = Key(r);
                    d[key] = Value(r, r.Sig(), end);
                }
                return d;
            }

            static object Value(BeReader r, string type, long end)
            {
                switch (type)
                {
                    case "Objc": case "GlbO": return Read(r, end);
                    case "doub": return ReadDouble(r);
                    case "UntF": r.Sig(); return ReadDouble(r);
                    case "long": return (double)r.I32();
                    case "comp": return (double)(long)r.U64();
                    case "bool": return r.U8() != 0;
                    case "TEXT": return UnicodeString(r);
                    case "enum": Key(r); return Key(r);
                    case "type": case "GlbC": UnicodeString(r); return Key(r);
                    case "tdta": case "alis": case "Pth ": return r.Bytes(r.U32());
                    case "VlLs":
                        {
                            uint n = r.U32();
                            if (n > 100000) throw new InvalidDataException("descriptor");
                            var list = new List<object>();
                            for (uint i = 0; i < n; i++) list.Add(Value(r, r.Sig(), end));
                            return list;
                        }
                    case "UnFl":
                        {
                            r.Sig();
                            uint n = r.U32();
                            if (n > 100000) throw new InvalidDataException("descriptor");
                            var list = new List<object>();
                            for (uint i = 0; i < n; i++) list.Add(ReadDouble(r));
                            return list;
                        }
                    default:
                        throw new InvalidDataException("descriptor type " + type);
                }
            }

            static double ReadDouble(BeReader r)
            {
                byte[] b = r.Bytes(8);
                Array.Reverse(b);
                return BitConverter.ToDouble(b, 0);
            }

            static string Key(BeReader r)
            {
                uint len = r.U32();
                if (len > 1000) throw new InvalidDataException("descriptor");
                return Encoding.ASCII.GetString(r.Bytes(len == 0 ? 4 : len));
            }

            static string UnicodeString(BeReader r)
            {
                uint len = r.U32();
                if (len > 100000) throw new InvalidDataException("descriptor");
                return Encoding.BigEndianUnicode.GetString(r.Bytes(len * 2)).TrimEnd('\0');
            }

            static double Num(Dictionary<string, object> d, params string[] keys)
            {
                foreach (string k in keys)
                {
                    object v;
                    if (d.TryGetValue(k, out v) && v is double) return (double)v;
                }
                return double.NaN;
            }

            static int Byte(double v) { return (int)Math.Round(Math.Max(0, Math.Min(255, v))); }

            public static Color? ToColor(Dictionary<string, object> c)
            {
                double r = Num(c, "Rd  "), g = Num(c, "Grn "), b = Num(c, "Bl  ");
                if (!double.IsNaN(r) && !double.IsNaN(g) && !double.IsNaN(b)) return Color.FromArgb(Byte(r), Byte(g), Byte(b));
                r = Num(c, "redFloat"); g = Num(c, "greenFloat"); b = Num(c, "blueFloat");
                if (!double.IsNaN(r) && !double.IsNaN(g) && !double.IsNaN(b)) return Color.FromArgb(Byte(r * 255), Byte(g * 255), Byte(b * 255));
                double gray = Num(c, "Gry ");
                if (!double.IsNaN(gray)) { int v = Byte(255 - gray * 2.55); return Color.FromArgb(v, v, v); }
                double cy = Num(c, "Cyn "), m = Num(c, "Mgnt"), y = Num(c, "Ylw "), k = Num(c, "Blck");
                if (!double.IsNaN(cy) && !double.IsNaN(m) && !double.IsNaN(y) && !double.IsNaN(k))
                {
                    double kk = 1 - k / 100;
                    return Color.FromArgb(Byte(255 * (1 - cy / 100) * kk), Byte(255 * (1 - m / 100) * kk), Byte(255 * (1 - y / 100) * kk));
                }
                double hue = Num(c, "H   "), s = Num(c, "Strt"), br = Num(c, "Brgh");
                if (!double.IsNaN(hue) && !double.IsNaN(s) && !double.IsNaN(br)) return FromHsb(hue, s / 100, br / 100);
                double L = Num(c, "Lmnc"), A = Num(c, "A   "), B = Num(c, "B   ");
                if (!double.IsNaN(L) && !double.IsNaN(A) && !double.IsNaN(B))
                {
                    int rr, gg, bb;
                    LabToRgb(Byte(L * 2.55), Byte(A + 128), Byte(B + 128), out rr, out gg, out bb);
                    return Color.FromArgb(rr, gg, bb);
                }
                return null;
            }

            static Color FromHsb(double h, double s, double v)
            {
                h = ((h % 360) + 360) % 360 / 60;
                int i = (int)Math.Floor(h);
                double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
                double r, g, b;
                switch (i)
                {
                    case 0: r = v; g = t; b = p; break;
                    case 1: r = q; g = v; b = p; break;
                    case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break;
                    case 4: r = t; g = p; b = v; break;
                    default: r = v; g = p; b = q; break;
                }
                return Color.FromArgb(Byte(r * 255), Byte(g * 255), Byte(b * 255));
            }
        }

        static bool MapBlend(string key, out BlendMode mode)
        {
            switch (key)
            {
                case "norm": mode = BlendMode.Normal; return true;
                case "pass": mode = BlendMode.Normal; return true;
                case "dark": mode = BlendMode.Darken; return true;
                case "mul ": mode = BlendMode.Multiply; return true;
                case "idiv": mode = BlendMode.ColorBurn; return true;
                case "lbrn": mode = BlendMode.LinearBurn; return true;
                case "lite": mode = BlendMode.Lighten; return true;
                case "scrn": mode = BlendMode.Screen; return true;
                case "div ": mode = BlendMode.ColorDodge; return true;
                case "lddg": mode = BlendMode.LinearDodge; return true;
                case "over": mode = BlendMode.Overlay; return true;
                case "sLit": mode = BlendMode.SoftLight; return true;
                case "hLit": mode = BlendMode.HardLight; return true;
                case "diff": mode = BlendMode.Difference; return true;
                case "smud": mode = BlendMode.Exclusion; return true;
                case "hue ": mode = BlendMode.Hue; return true;
                case "sat ": mode = BlendMode.Saturation; return true;
                case "colr": mode = BlendMode.Color; return true;
                case "lum ": mode = BlendMode.Luminosity; return true;
                default: mode = BlendMode.Normal; return false;
            }
        }

        static string BlendName(string key)
        {
            switch (key)
            {
                case "diss": return "Dissolve";
                case "dkCl": return "Darker Color";
                case "lgCl": return "Lighter Color";
                case "vLit": return "Vivid Light";
                case "lLit": return "Linear Light";
                case "pLit": return "Pin Light";
                case "hMix": return "Hard Mix";
                case "fsub": return "Subtract";
                case "fdiv": return "Divide";
                default: return "\"" + key.Trim() + "\"";
            }
        }

        // ================================================================ flattened image

        static Pixels ReadComposite(BeReader r, Ctx ctx, int w, int h, int channels, bool hasAlpha)
        {
            int colorCh = ColorChannels(ctx.Mode);
            int want = Math.Min(channels, colorCh + (hasAlpha ? 1 : 0));
            if (r.Pos + 2 > r.Length) throw new InvalidDataException("The file has no flattened image to show.");
            int compression = r.U16();
            int rowBytes = RowBytes(w, ctx.Depth);
            var planes = new byte[want][];

            if (compression == 0)
            {
                long planeSize = (long)rowBytes * h;
                for (int c = 0; c < want; c++)
                {
                    planes[c] = To8Bit(r.Bytes(planeSize), w, h, ctx.Depth, c < colorCh && ctx.Mode != 2);
                }
            }
            else if (compression == 1)
            {
                // the byte counts of every row of every channel come first
                long rows = (long)channels * h;
                var counts = new long[rows];
                for (long i = 0; i < rows; i++) counts[i] = ctx.Big ? r.U32() : r.U16();
                for (int c = 0; c < want; c++)
                {
                    var raw = new byte[(long)rowBytes * h];
                    for (int y = 0; y < h; y++)
                        UnpackBits(r.Bytes(counts[(long)c * h + y]), raw, y * rowBytes, rowBytes);
                    planes[c] = To8Bit(raw, w, h, ctx.Depth, c < colorCh && ctx.Mode != 2);
                }
            }
            else
            {
                throw new InvalidDataException("The flattened image uses a compression MicroApp cannot read.");
            }

            var color = new byte[colorCh][];
            for (int c = 0; c < colorCh && c < want; c++) color[c] = planes[c];
            byte[] alpha = hasAlpha && want > colorCh ? planes[colorCh] : null;
            return Compose(w, h, color, alpha, ctx);
        }

        // ================================================================ pixels

        static int RowBytes(int w, int depth)
        {
            return depth == 1 ? (w + 7) / 8 : w * (depth / 8);
        }

        /// <summary>One channel, decompressed and brought down to 8 bits (one byte per pixel).</summary>
        static byte[] DecodePlane(BeReader r, int compression, int w, int h, Ctx ctx, long end, bool isColor)
        {
            int rowBytes = RowBytes(w, ctx.Depth);
            long total = (long)rowBytes * h;
            if (total > int.MaxValue) throw new InvalidDataException("a layer is too big");
            byte[] raw;
            switch (compression)
            {
                case 0:
                    raw = r.Bytes(total);
                    break;
                case 1:
                    {
                        var counts = new long[h];
                        for (int y = 0; y < h; y++) counts[y] = ctx.Big ? r.U32() : r.U16();
                        raw = new byte[total];
                        for (int y = 0; y < h; y++) UnpackBits(r.Bytes(counts[y]), raw, y * rowBytes, rowBytes);
                        break;
                    }
                case 2:
                case 3:
                    raw = Inflate(r.Bytes(end - r.Pos), (int)total);
                    if (compression == 3) Unpredict(raw, w, h, ctx.Depth);
                    break;
                default:
                    throw new InvalidDataException("unknown compression " + compression);
            }
            return To8Bit(raw, w, h, ctx.Depth, isColor);
        }

        /// <summary>PackBits run-length decoding of one row.</summary>
        static void UnpackBits(byte[] src, byte[] dst, int offset, int length)
        {
            int i = 0, o = offset, end = offset + length;
            while (i < src.Length && o < end)
            {
                int n = (sbyte)src[i++];
                if (n >= 0)
                {
                    int count = Math.Min(n + 1, Math.Min(end - o, src.Length - i));
                    Buffer.BlockCopy(src, i, dst, o, count);
                    i += n + 1;
                    o += count;
                }
                else if (n != -128)
                {
                    if (i >= src.Length) break;
                    byte b = src[i++];
                    int count = Math.Min(1 - n, end - o);
                    for (int k = 0; k < count; k++) dst[o++] = b;
                }
            }
        }

        static byte[] Inflate(byte[] zlib, int size)
        {
            var result = new byte[size];
            if (zlib.Length < 2) return result;
            using (var ms = new MemoryStream(zlib, 2, zlib.Length - 2))   // skip the zlib header
            using (var z = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < size)
                {
                    int n = z.Read(result, read, size - read);
                    if (n <= 0) break;
                    read += n;
                }
            }
            return result;
        }

        /// <summary>Undoes the "ZIP with prediction" delta coding, row by row.</summary>
        static void Unpredict(byte[] raw, int w, int h, int depth)
        {
            if (depth == 8)
            {
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 1; x < w; x++) raw[row + x] = (byte)(raw[row + x] + raw[row + x - 1]);
                }
            }
            else if (depth == 16)
            {
                for (int y = 0; y < h; y++)
                {
                    int row = y * w * 2;
                    int prev = (raw[row] << 8) | raw[row + 1];
                    for (int x = 1; x < w; x++)
                    {
                        int i = row + x * 2;
                        int v = (((raw[i] << 8) | raw[i + 1]) + prev) & 0xFFFF;
                        raw[i] = (byte)(v >> 8);
                        raw[i + 1] = (byte)v;
                        prev = v;
                    }
                }
            }
            else if (depth == 32)
            {
                // bytes are delta-coded across the row, then stored as four byte planes
                int rowBytes = w * 4;
                var tmp = new byte[rowBytes];
                for (int y = 0; y < h; y++)
                {
                    int row = y * rowBytes;
                    for (int i = 1; i < rowBytes; i++) raw[row + i] = (byte)(raw[row + i] + raw[row + i - 1]);
                    for (int x = 0; x < w; x++)
                        for (int k = 0; k < 4; k++) tmp[x * 4 + k] = raw[row + k * w + x];
                    Buffer.BlockCopy(tmp, 0, raw, row, rowBytes);
                }
            }
        }

        static byte[] To8Bit(byte[] raw, int w, int h, int depth, bool isColor)
        {
            if (depth == 8) return raw;
            var o = new byte[(long)w * h];
            if (depth == 16)
            {
                for (long i = 0; i < o.Length; i++) o[i] = raw[i * 2];
            }
            else if (depth == 32)
            {
                // 32-bit documents are linear light; bring colour back to screen gamma
                var f = new byte[4];
                for (long i = 0; i < o.Length; i++)
                {
                    f[3] = raw[i * 4]; f[2] = raw[i * 4 + 1]; f[1] = raw[i * 4 + 2]; f[0] = raw[i * 4 + 3];
                    double v = BitConverter.ToSingle(f, 0);
                    if (double.IsNaN(v) || v < 0) v = 0;
                    if (v > 1) v = 1;
                    if (isColor) v = Math.Pow(v, 1 / 2.2);
                    o[i] = (byte)Math.Round(v * 255);
                }
            }
            else if (depth == 1)
            {
                int rowBytes = (w + 7) / 8;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        o[(long)y * w + x] = (raw[y * rowBytes + x / 8] & (0x80 >> (x % 8))) != 0 ? (byte)0 : (byte)255;   // 1 = black
            }
            return o;
        }

        /// <summary>Channel planes of any colour mode → straight-alpha BGRA.</summary>
        static Pixels Compose(int w, int h, byte[][] color, byte[] alpha, Ctx ctx)
        {
            var px = new Pixels(w, h);
            byte[] d = px.Data;
            int n = w * h;
            byte[] c0 = color.Length > 0 ? color[0] : null;
            byte[] c1 = color.Length > 1 ? color[1] : null;
            byte[] c2 = color.Length > 2 ? color[2] : null;
            byte[] c3 = color.Length > 3 ? color[3] : null;
            for (int i = 0; i < n; i++)
            {
                int r, g, b, a = alpha != null ? alpha[i] : 255;
                switch (ctx.Mode)
                {
                    case 3:   // RGB
                        r = c0 != null ? c0[i] : 0; g = c1 != null ? c1[i] : 0; b = c2 != null ? c2[i] : 0;
                        break;
                    case 4:   // CMYK, stored inverted (255 = no ink)
                        {
                            int k = c3 != null ? c3[i] : 255;
                            r = (c0 != null ? c0[i] : 255) * k / 255;
                            g = (c1 != null ? c1[i] : 255) * k / 255;
                            b = (c2 != null ? c2[i] : 255) * k / 255;
                            break;
                        }
                    case 2:   // indexed
                        {
                            int idx = c0 != null ? c0[i] : 0;
                            if (ctx.Palette != null) { r = ctx.Palette[idx]; g = ctx.Palette[256 + idx]; b = ctx.Palette[512 + idx]; }
                            else r = g = b = idx;
                            if (idx == ctx.TransparentIndex) a = 0;
                            break;
                        }
                    case 9:   // Lab
                        LabToRgb(c0 != null ? c0[i] : 0, c1 != null ? c1[i] : 128, c2 != null ? c2[i] : 128, out r, out g, out b);
                        break;
                    default:  // gray, bitmap, duotone, multichannel
                        r = g = b = c0 != null ? c0[i] : 0;
                        break;
                }
                int o = i * 4;
                d[o] = (byte)b; d[o + 1] = (byte)g; d[o + 2] = (byte)r; d[o + 3] = (byte)a;
            }
            return px;
        }

        static void LabToRgb(int L8, int a8, int b8, out int r, out int g, out int b)
        {
            double L = L8 * 100.0 / 255, A = a8 - 128, B = b8 - 128;
            double fy = (L + 16) / 116, fx = fy + A / 500, fz = fy - B / 200;
            Func<double, double> finv = t => t > 6.0 / 29 ? t * t * t : 3 * (6.0 / 29) * (6.0 / 29) * (t - 4.0 / 29);
            double X = 0.95047 * finv(fx), Y = finv(fy), Z = 1.08883 * finv(fz);
            double rl = 3.2406 * X - 1.5372 * Y - 0.4986 * Z;
            double gl = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
            double bl = 0.0557 * X - 0.2040 * Y + 1.0570 * Z;
            Func<double, int> enc = v =>
            {
                v = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(Math.Max(0, v), 1 / 2.4) - 0.055;
                return (int)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);
            };
            r = enc(rl); g = enc(gl); b = enc(bl);
        }

        // ================================================================ helpers

        static readonly HashSet<string> LongKeys = new HashSet<string>
        {
            "LMsk", "Lr16", "Lr32", "Layr", "Mt16", "Mt32", "Mtrn", "Alph", "FMsk", "lnk2", "FEid", "FXid", "PxSD"
        };

        static bool IsLongKey(string key) { return LongKeys.Contains(key); }

        /// <summary>
        /// Tagged blocks are padded inconsistently between Photoshop versions; step over up to
        /// three pad bytes to the next "8BIM"/"8B64" signature.
        /// </summary>
        static bool AlignToSignature(BeReader r, long end)
        {
            long start = r.Pos;
            for (int k = 0; k < 4 && start + k + 4 <= end; k++)
            {
                r.Pos = start + k;
                string sig = r.Sig();
                if (sig == "8BIM" || sig == "8B64") { r.Pos = start + k; return true; }
            }
            r.Pos = start;
            return false;
        }

        static string Win1252(byte[] bytes)
        {
            try { return Encoding.GetEncoding(1252).GetString(bytes); }
            catch { return Encoding.ASCII.GetString(bytes); }
        }

        static string Plural(int n, string what) { return n + " " + what + (n == 1 ? "" : "s"); }

        /// <summary>Big-endian reader over a seekable stream.</summary>
        sealed class BeReader
        {
            readonly Stream _s;
            readonly byte[] _b = new byte[8];

            public BeReader(Stream s) { _s = s; }

            public long Pos { get { return _s.Position; } set { _s.Position = value; } }
            public long Length { get { return _s.Length; } }

            void Fill(byte[] buf, int n)
            {
                int read = 0;
                while (read < n)
                {
                    int k = _s.Read(buf, read, n - read);
                    if (k <= 0) throw new EndOfStreamException("the file ends early - it may be damaged");
                    read += k;
                }
            }

            public byte U8() { Fill(_b, 1); return _b[0]; }
            public ushort U16() { Fill(_b, 2); return (ushort)((_b[0] << 8) | _b[1]); }
            public short I16() { return (short)U16(); }
            public uint U32() { Fill(_b, 4); return (uint)((_b[0] << 24) | (_b[1] << 16) | (_b[2] << 8) | _b[3]); }
            public int I32() { return (int)U32(); }
            public long U64() { long hi = U32(); long lo = U32(); return (hi << 32) | lo; }
            public string Sig() { Fill(_b, 4); return Encoding.ASCII.GetString(_b, 0, 4); }
            public void Skip(long n) { _s.Position += n; }

            public byte[] Bytes(long n)
            {
                if (n < 0 || n > int.MaxValue || _s.Position + n > _s.Length)
                    throw new EndOfStreamException("the file ends early - it may be damaged");
                var a = new byte[n];
                Fill(a, (int)n);
                return a;
            }
        }
    }
}
