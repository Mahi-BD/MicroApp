using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace MicroApp
{
    /// <summary>
    /// Opens Adobe Illustrator files (.ai) as pictures. Illustrator saves a PDF copy of the
    /// artwork inside every .ai when "Create PDF Compatible File" is ticked (the default);
    /// that copy is drawn with the PDF renderer built into Windows (Windows.Data.Pdf),
    /// one page per artboard. The shapes become pixels - they are not editable as vectors.
    /// </summary>
    static class AiReader
    {
        public const float RenderPpi = 300f;
        const int MaxSide = 8000;              // longest side of one artboard, in pixels
        const double MaxPixels = 36e6;         // and its area (6000 x 6000): big artboards are drawn at a lower ppi

        public static bool IsAi(string file)
        {
            return Path.GetExtension(file).Equals(".ai", StringComparison.OrdinalIgnoreCase);
        }

        public static ImportedDocument Load(string path, Action<float, string> report)
        {
            // runs on a worker thread (ProgressDialog), so blocking on the WinRT calls is safe
            return LoadAsync(path, report ?? delegate { }).GetAwaiter().GetResult();
        }

        static async Task<ImportedDocument> LoadAsync(string path, Action<float, string> report)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
            if (head.IndexOf("%PDF-", StringComparison.Ordinal) < 0)
            {
                if (head.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidDataException(
                        "This file is an SVG picture with an .ai name, not an Illustrator file.\r\n" +
                        "Open it in a browser or Inkscape and export a PNG, then open that.");
                if (head.IndexOf("%!PS-Adobe", StringComparison.Ordinal) >= 0 || (bytes.Length > 4 && bytes[0] == 0xC5 && bytes[1] == 0xD0))
                    throw new InvalidDataException(
                        "This Illustrator file has no PDF copy inside (it is an older or PostScript-only .ai).\r\n" +
                        "In Illustrator choose File > Save As > Adobe Illustrator (.ai), tick \"Create PDF Compatible File\", then open it again.");
                throw new InvalidDataException("This is not an Illustrator file MicroApp can read.");
            }

            report(0.05f, "Reading the artwork");
            var doc = new ImportedDocument { Ppi = RenderPpi };
            try
            {
                using (var input = new InMemoryRandomAccessStream())
                {
                    await input.WriteAsync(bytes.AsBuffer());
                    input.Seek(0);
                    PdfDocument pdf;
                    try { pdf = await PdfDocument.LoadFromStreamAsync(input); }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("Windows could not read the artwork in this file (" + ex.Message.Trim() + ").");
                    }

                    uint count = pdf.PageCount;
                    float ppi = RenderPpi;
                    for (uint i = 0; i < count; i++)
                    {
                        report(0.1f + 0.85f * i / Math.Max(1u, count), count > 1 ? "Drawing artboard " + (i + 1) + " of " + count : "Drawing the artwork");
                        using (PdfPage page = pdf.GetPage(i))
                        {
                            // page sizes come in DIPs (1/96 inch)
                            double w = page.Size.Width / 96.0 * RenderPpi;
                            double h = page.Size.Height / 96.0 * RenderPpi;
                            double fit = Math.Min(1.0, MaxSide / Math.Max(1.0, Math.Max(w, h)));
                            fit = Math.Min(fit, Math.Sqrt(MaxPixels / Math.Max(1.0, w * h)));
                            ppi = (float)Math.Min(ppi, RenderPpi * fit);
                            int pw = Math.Max(1, (int)Math.Round(w * fit));
                            int ph = Math.Max(1, (int)Math.Round(h * fit));

                            var options = new PdfPageRenderOptions
                            {
                                DestinationWidth = (uint)pw,
                                DestinationHeight = (uint)ph,
                                BackgroundColor = Windows.UI.Color.FromArgb(0, 255, 255, 255)   // keep the artboard see-through
                            };
                            Bitmap bmp;
                            using (var output = new InMemoryRandomAccessStream())
                            {
                                await page.RenderToStreamAsync(output, options);
                                output.Seek(0);
                                using (Stream s = output.AsStreamForRead())
                                using (Image img = Image.FromStream(s))
                                {
                                    bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                                    using (Graphics g = Graphics.FromImage(bmp))
                                        g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
                                }
                            }

                            string name = count > 1 ? "Artboard " + (i + 1) : "Artboard";
                            var p = new ImportedPage { Name = name, Width = bmp.Width, Height = bmp.Height };
                            p.Layers.Add(new ImportedLayer
                            {
                                Name = count > 1 ? name : Path.GetFileNameWithoutExtension(path),
                                Image = bmp,
                                Bounds = new Rectangle(0, 0, bmp.Width, bmp.Height)
                            });
                            doc.Pages.Add(p);
                        }
                    }
                    doc.Ppi = (float)Math.Round(ppi);
                }
            }
            catch
            {
                doc.Dispose();
                throw;
            }

            if (doc.Pages.Count == 0) throw new InvalidDataException("This file has no artwork to show.");
            foreach (ImportedPage p in doc.Pages)
                foreach (ImportedLayer l in p.Layers) l.Image.SetResolution(doc.Ppi, doc.Ppi);
            doc.Notes.Add("Illustrator artwork opens as pixels at " + Math.Round(doc.Ppi) + " ppi - shapes and text are not editable." +
                          (doc.Pages.Count > 1 ? " Each artboard is a page." : ""));
            report(1f, null);
            return doc;
        }
    }
}
