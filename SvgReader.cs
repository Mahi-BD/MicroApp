using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Xml;
using Svg;

namespace MicroApp
{
    /// <summary>
    /// Opens SVG drawings (.svg, and gzip-compressed .svgz) as pictures, drawn by Svg.NET.
    /// SVG has no fixed resolution, so it is drawn at 300 ppi (small icons larger, very
    /// large drawings smaller), on a transparent background. The result is pixels - the
    /// shapes are not editable.
    ///
    /// SVG files come from anywhere, so nothing outside the file is fetched: entities
    /// declared inside the file work (older Illustrator exports use them) but external ones
    /// are never resolved, entity expansion is capped, and no web address is followed -
    /// only images and elements on this computer (a PNG next to the SVG, say).
    /// </summary>
    static class SvgReader
    {
        const float RenderPpi = 300f;          // SVG user units are CSS pixels, 96 to the inch
        const int MinSide = 1024;              // icons are drawn at least this big
        const int MaxSide = 8000;
        const double MaxPixels = 36e6;

        public static bool IsSvg(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ext == ".svg" || ext == ".svgz";
        }

        public static ImportedDocument Load(string path, Action<float, string> report)
        {
            if (report == null) report = delegate { };
            report(0.05f, "Reading the drawing");

            SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
            SvgDocument.ResolveExternalImages = ExternalType.Local;
            SvgDocument.ResolveExternalElements = ExternalType.Local;

            SvgDocument svg;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B) bytes = Gunzip(bytes);   // .svgz
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Parse,      // internal entities only:
                    XmlResolver = null,                       // nothing outside the file is read
                    MaxCharactersFromEntities = 10000000,     // no "billion laughs"
                    IgnoreComments = true
                };
                var xml = new XmlDocument { XmlResolver = null };
                using (var ms = new MemoryStream(bytes))
                using (XmlReader reader = XmlReader.Create(ms, settings))
                    xml.Load(reader);
                svg = SvgDocument.Open(xml);
                // relative references (an image next to the file) resolve against the file's folder
                svg.BaseUri = new Uri(Path.GetFullPath(path));
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                throw new InvalidDataException("This SVG file could not be read (" + ex.Message.Trim() + ").");
            }

            SizeF size = svg.GetDimensions();
            if (size.Width <= 0 || size.Height <= 0 || float.IsNaN(size.Width) || float.IsNaN(size.Height))
            {
                SvgViewBox vb = svg.ViewBox;
                size = vb.Width > 0 && vb.Height > 0 ? new SizeF(vb.Width, vb.Height) : new SizeF(1024, 1024);
            }

            double scale = RenderPpi / 96.0;
            double longest = Math.Max(size.Width, size.Height);
            if (longest * scale < MinSide) scale = MinSide / longest;
            scale = Math.Min(scale, MaxSide / longest);
            scale = Math.Min(scale, Math.Sqrt(MaxPixels / Math.Max(1.0, (double)size.Width * size.Height)));
            int w = Math.Max(1, (int)Math.Round(size.Width * scale));
            int h = Math.Max(1, (int)Math.Round(size.Height * scale));

            report(0.3f, "Drawing it");
            Bitmap drawn;
            try { drawn = svg.Draw(w, h); }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                throw new InvalidDataException("This SVG file could not be drawn (" + ex.Message.Trim() + ").");
            }
            if (drawn == null) throw new InvalidDataException("This SVG file has nothing to draw.");

            // always hand the editor a 32-bit straight-alpha bitmap of the exact size
            var bmp = new Bitmap(drawn.Width, drawn.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(drawn, new Rectangle(0, 0, drawn.Width, drawn.Height));
            drawn.Dispose();

            // print resolution: the drawing's own size, except that enlarged icons print at 300 ppi
            float ppi = (float)Math.Min(RenderPpi, Math.Round(scale * 96));
            bmp.SetResolution(ppi, ppi);
            var doc = new ImportedDocument { Ppi = ppi };
            var page = new ImportedPage { Name = "Page 1", Width = bmp.Width, Height = bmp.Height };
            page.Layers.Add(new ImportedLayer
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Image = bmp,
                Bounds = new Rectangle(0, 0, bmp.Width, bmp.Height)
            });
            doc.Pages.Add(page);
            doc.Notes.Add("The SVG drawing opens as pixels (" + bmp.Width + " × " + bmp.Height + ") - shapes and text are not editable.");
            report(1f, null);
            return doc;
        }

        static byte[] Gunzip(byte[] gz)
        {
            using (var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
