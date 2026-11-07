using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MicroApp
{
    /// <summary>
    /// Opens Encapsulated PostScript (.eps). EPS is a PostScript program, and only a
    /// PostScript interpreter draws it properly, so:
    ///   1. when Ghostscript is installed (free; its licence does not allow bundling it
    ///      with MicroApp) it draws the file at 300 ppi on a transparent background;
    ///   2. otherwise the preview picture many EPS files carry is opened (a TIFF or WMF in
    ///      a "DOS EPS" header, or an EPSI bitmap in the text) and the user is told it is
    ///      only a preview;
    ///   3. with neither, the message says how to get Ghostscript.
    /// Ghostscript always runs with -dSAFER: an EPS cannot touch files or run programs.
    /// </summary>
    static class EpsReader
    {
        const float RenderPpi = 300f;
        const int MaxSide = 8000;
        const double MaxPixels = 36e6;
        const int TimeoutMs = 120000;

        public static bool IsEps(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ext == ".eps" || ext == ".epsf" || ext == ".epsi";
        }

        public static ImportedDocument Load(string path, Action<float, string> report)
        {
            if (report == null) report = delegate { };
            byte[] bytes = File.ReadAllBytes(path);
            var parts = Split(bytes);
            if (parts.PostScript == null)
                throw new InvalidDataException("This is not an EPS file MicroApp can read.");

            string gs = FindGhostscript();
            string gsError = null;
            if (gs != null)
            {
                report(0.1f, "Drawing it with Ghostscript");
                try
                {
                    float ppi;
                    Bitmap bmp = Render(gs, path, BoundingBox(parts.PostScript), out ppi);
                    var doc = Single(bmp, ppi, path);
                    doc.Notes.Add("The EPS drawing opens as pixels at " + ppi + " ppi (drawn by Ghostscript) - shapes and text are not editable.");
                    report(1f, null);
                    return doc;
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    gsError = ex.Message.Trim();
                }
            }

            report(0.5f, "Opening the preview picture");
            Bitmap preview = Preview(parts);
            if (preview != null)
            {
                float ppi = PreviewPpi(preview, BoundingBox(parts.PostScript));
                var doc = Single(preview, ppi, path);
                doc.Notes.Add(gs == null
                    ? "Only the small preview picture inside the EPS (" + preview.Width + " × " + preview.Height + " px) was opened. " +
                      "Install Ghostscript (free, ghostscript.com) and open it again for the full drawing."
                    : "Ghostscript could not draw this file (" + gsError + "), so only its preview picture (" +
                      preview.Width + " × " + preview.Height + " px) was opened.");
                report(1f, null);
                return doc;
            }

            if (gs != null)
                throw new InvalidDataException("Ghostscript could not draw this EPS file:\r\n" + gsError);
            throw new InvalidDataException(
                "MicroApp needs Ghostscript to draw this EPS file, and the file has no preview picture inside.\r\n" +
                "Install Ghostscript (free, from ghostscript.com) and open it again - or save it from your drawing " +
                "program as PDF, SVG or PNG.");
        }

        // ================================================================ file layout

        sealed class Parts
        {
            public byte[] PostScript, Tiff, Wmf;
        }

        /// <summary>A "DOS EPS" file starts with a binary header pointing at the PostScript and its TIFF/WMF previews.</summary>
        static Parts Split(byte[] b)
        {
            var p = new Parts();
            if (b.Length >= 30 && b[0] == 0xC5 && b[1] == 0xD0 && b[2] == 0xD3 && b[3] == 0xC6)
            {
                p.PostScript = Slice(b, BitConverter.ToUInt32(b, 4), BitConverter.ToUInt32(b, 8));
                p.Wmf = Slice(b, BitConverter.ToUInt32(b, 12), BitConverter.ToUInt32(b, 16));
                p.Tiff = Slice(b, BitConverter.ToUInt32(b, 20), BitConverter.ToUInt32(b, 24));
                return p;
            }
            string head = Encoding.ASCII.GetString(b, 0, Math.Min(b.Length, 64));
            if (head.StartsWith("%!PS", StringComparison.Ordinal)) p.PostScript = b;
            return p;
        }

        static byte[] Slice(byte[] b, uint offset, uint length)
        {
            if (length == 0 || offset >= b.Length || offset + (long)length > b.Length) return null;
            var r = new byte[length];
            Buffer.BlockCopy(b, (int)offset, r, 0, (int)length);
            return r;
        }

        /// <summary>The drawing's box in points (%%HiResBoundingBox, else %%BoundingBox), or empty.</summary>
        static RectangleF BoundingBox(byte[] ps)
        {
            string text = Encoding.ASCII.GetString(ps, 0, Math.Min(ps.Length, 256 * 1024));
            foreach (string key in new[] { "%%HiResBoundingBox:", "%%BoundingBox:" })
            {
                foreach (Match m in Regex.Matches(text, Regex.Escape(key) + @"[ \t]*([-\d.]+)[ \t]+([-\d.]+)[ \t]+([-\d.]+)[ \t]+([-\d.]+)"))
                {
                    double x0, y0, x1, y1;
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x0) &&
                        double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y0) &&
                        double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x1) &&
                        double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y1) &&
                        x1 > x0 && y1 > y0)
                        return new RectangleF((float)x0, (float)y0, (float)(x1 - x0), (float)(y1 - y0));
                }
            }
            return RectangleF.Empty;
        }

        // ================================================================ Ghostscript

        /// <summary>
        /// The Ghostscript console program: MICROAPP_GHOSTSCRIPT (a portable copy), the
        /// installer's registry keys, Program Files\gs, then the PATH. Newest version wins.
        /// </summary>
        public static string FindGhostscript()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("MICROAPP_GHOSTSCRIPT");
                if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

                var found = new List<string>();
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    foreach (string vendor in new[] { "GPL Ghostscript", "Artifex Ghostscript", "AFPL Ghostscript" })
                    {
                        try
                        {
                            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                            using (var root = hklm.OpenSubKey(@"SOFTWARE\" + vendor))
                            {
                                if (root == null) continue;
                                foreach (string ver in root.GetSubKeyNames())
                                    using (var k = root.OpenSubKey(ver))
                                    {
                                        string dll = k == null ? null : k.GetValue("GS_DLL") as string;
                                        if (string.IsNullOrEmpty(dll)) continue;
                                        string dir = Path.GetDirectoryName(dll);
                                        foreach (string exe in new[] { "gswin64c.exe", "gswin32c.exe" })
                                            if (File.Exists(Path.Combine(dir, exe))) found.Add(Path.Combine(dir, exe));
                                    }
                            }
                        }
                        catch { }
                    }

                foreach (Environment.SpecialFolder pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                {
                    string gsRoot = Path.Combine(Environment.GetFolderPath(pf), "gs");
                    if (!Directory.Exists(gsRoot)) continue;
                    foreach (string dir in Directory.GetDirectories(gsRoot))
                        foreach (string exe in new[] { "gswin64c.exe", "gswin32c.exe" })
                        {
                            string f = Path.Combine(dir, "bin", exe);
                            if (File.Exists(f)) found.Add(f);
                        }
                }
                if (found.Count > 0)
                    return found.OrderByDescending(f => VersionOf(f)).ThenByDescending(f => f.Contains("64")).First();

                foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                    foreach (string exe in new[] { "gswin64c.exe", "gswin32c.exe", "gs" })
                    {
                        try
                        {
                            string f = Path.Combine(dir.Trim().Trim('"'), exe);
                            if (File.Exists(f)) return f;
                        }
                        catch (ArgumentException) { }
                    }
            }
            catch { }
            return null;
        }

        static Version VersionOf(string exe)
        {
            // ...\gs\gs10.04.0\bin\gswin64c.exe
            Match m = Regex.Match(exe, @"gs(\d+(\.\d+)+)", RegexOptions.IgnoreCase);
            Version v;
            return m.Success && Version.TryParse(m.Groups[1].Value, out v) ? v : new Version(0, 0);
        }

        /// <summary>Draws one page of PostScript with Ghostscript into a transparent bitmap.</summary>
        public static Bitmap Render(string gs, string path, RectangleF box, out float ppi)
        {
            ppi = RenderPpi;
            if (!box.IsEmpty)
            {
                double w = box.Width / 72.0 * RenderPpi, h = box.Height / 72.0 * RenderPpi;
                double fit = Math.Min(1.0, MaxSide / Math.Max(w, h));
                fit = Math.Min(fit, Math.Sqrt(MaxPixels / Math.Max(1.0, w * h)));
                ppi = (float)Math.Max(1, Math.Floor(RenderPpi * fit));
            }

            string dir = Path.Combine(Path.GetTempPath(), "MicroApp-eps-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string png = Path.Combine(dir, "page.png");
                var args = new StringBuilder();
                args.Append("-dSAFER -dBATCH -dNOPAUSE -dQUIET -dEPSCrop -dFirstPage=1 -dLastPage=1 ");
                args.Append("-dTextAlphaBits=4 -dGraphicsAlphaBits=4 -sDEVICE=pngalpha ");
                args.Append("-r").Append(ppi.ToString(CultureInfo.InvariantCulture)).Append(' ');
                args.Append("\"-sOutputFile=").Append(png).Append("\" ");
                args.Append("\"").Append(path).Append("\"");

                var psi = new ProcessStartInfo(gs, args.ToString())
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = dir
                };
                var output = new StringBuilder();
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(TimeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        throw new TimeoutException("it took longer than " + TimeoutMs / 1000 + " seconds");
                    }
                    p.WaitForExit();
                    if (p.ExitCode == unchecked((int)0xC0000135) || p.ExitCode == unchecked((int)0xC000007B))
                        throw new InvalidDataException(
                            "Ghostscript could not start - a Windows component it needs is missing. Reinstall Ghostscript");
                    if (p.ExitCode != 0 || !File.Exists(png))
                    {
                        string msg;
                        lock (output) msg = output.ToString().Trim();
                        // "Error: /undefined in ..." says more than the closing "Unrecoverable error" line
                        var lines = msg.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                        string best = lines.FirstOrDefault(l => l.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                                      ?? lines.FirstOrDefault();
                        throw new InvalidDataException(best ?? ("Ghostscript stopped with code " + p.ExitCode));
                    }
                }

                using (var fs = new FileStream(png, FileMode.Open, FileAccess.Read))
                using (Image img = Image.FromStream(fs))
                {
                    var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
                    if (IsBlank(bmp))
                    {
                        // e.g. an older .ai that leans on Illustrator's own PostScript routines
                        bmp.Dispose();
                        throw new InvalidDataException("Ghostscript drew an empty page - the file needs the program that made it");
                    }
                    return bmp;
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>
        /// Any PostScript file (an older .ai, say) drawn by Ghostscript; null when Ghostscript
        /// is not installed. Throws when Ghostscript cannot draw it.
        /// </summary>
        public static Bitmap RenderPostScriptFile(string path, out float ppi)
        {
            ppi = RenderPpi;
            string gs = FindGhostscript();
            if (gs == null) return null;
            byte[] bytes = File.ReadAllBytes(path);
            Parts parts = Split(bytes);
            return Render(gs, path, BoundingBox(parts.PostScript ?? bytes), out ppi);
        }

        /// <summary>True when every pixel is fully transparent.</summary>
        static bool IsBlank(Bitmap bmp)
        {
            Pixels px = Pixels.From(bmp);
            for (int i = 3; i < px.Data.Length; i += 4)
                if (px.Data[i] != 0) return false;
            return true;
        }

        // ================================================================ previews

        static Bitmap Preview(Parts parts)
        {
            Bitmap b = null;
            if (parts.Tiff != null) b = FromImageBytes(parts.Tiff);
            if (b == null && parts.Wmf != null) b = FromImageBytes(parts.Wmf);
            if (b == null) b = Epsi(parts.PostScript);
            return b;
        }

        static Bitmap FromImageBytes(byte[] data)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (Image img = Image.FromStream(ms))
                {
                    bool vector = img is Metafile;
                    int w = Math.Max(1, vector ? img.Width * 2 : img.Width);
                    int h = Math.Max(1, vector ? img.Height * 2 : img.Height);
                    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        if (vector) g.Clear(Color.White);
                        g.DrawImage(img, new Rectangle(0, 0, w, h));
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// An EPSI preview: "%%BeginPreview: width height depth lines" followed by hex lines,
        /// each starting with "%"; rows run top to bottom, 0 is white.
        /// </summary>
        static Bitmap Epsi(byte[] ps)
        {
            try
            {
                string text = Encoding.ASCII.GetString(ps, 0, Math.Min(ps.Length, 8 * 1024 * 1024));
                Match m = Regex.Match(text, @"%%BeginPreview:[ \t]*(\d+)[ \t]+(\d+)[ \t]+(\d+)");
                if (!m.Success) return null;
                int w = int.Parse(m.Groups[1].Value), h = int.Parse(m.Groups[2].Value), depth = int.Parse(m.Groups[3].Value);
                if (w <= 0 || h <= 0 || w > 10000 || h > 10000 || (depth != 1 && depth != 2 && depth != 4 && depth != 8)) return null;
                int end = text.IndexOf("%%EndPreview", m.Index, StringComparison.Ordinal);
                if (end < 0) return null;
                int lineEnd = text.IndexOf('\n', m.Index);
                var hex = new StringBuilder();
                foreach (string line in text.Substring(lineEnd + 1, end - lineEnd - 1).Split('\n'))
                {
                    string l = line.Trim();
                    if (l.StartsWith("%")) hex.Append(l.Substring(1).Trim());
                }
                string hs = Regex.Replace(hex.ToString(), "[^0-9A-Fa-f]", "");
                int rowBytes = (w * depth + 7) / 8;
                if (hs.Length / 2 < rowBytes * h) return null;

                var px = new Pixels(w, h);
                int max = (1 << depth) - 1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int bit = x * depth;
                        int byteIndex = y * rowBytes + bit / 8;
                        int b = Convert.ToInt32(hs.Substring(byteIndex * 2, 2), 16);
                        int v = (b >> (8 - depth - bit % 8)) & max;
                        byte gray = (byte)(255 - v * 255 / max);      // 0 is white
                        int i = px.Index(x, y);
                        px.Data[i] = px.Data[i + 1] = px.Data[i + 2] = gray;
                        px.Data[i + 3] = 255;
                    }
                return px.ToBitmap();
            }
            catch { return null; }
        }

        static float PreviewPpi(Bitmap preview, RectangleF box)
        {
            if (box.IsEmpty) return 72;
            return (float)Math.Max(1, Math.Round(preview.Width / (box.Width / 72.0)));
        }

        static ImportedDocument Single(Bitmap bmp, float ppi, string path)
        {
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
            return doc;
        }
    }
}
