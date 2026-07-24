using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MicroApp
{
    /// <summary>
    /// Text recognition on top of the OCR engine built into Windows 10/11
    /// (Windows.Media.Ocr). Fully offline; the available languages are whatever
    /// language packs the machine has installed.
    /// </summary>
    public static class OcrService
    {
        /// <summary>Small captures recognise far better when scaled up first.</summary>
        private const int MinimumWidthForAccuracy = 1000;
        private const int MaximumScale = 4;

        public static bool IsAvailable
        {
            get
            {
                try { return OcrEngine.AvailableRecognizerLanguages.Count > 0; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Installed recognizer languages, as (tag, display name) pairs.</summary>
        public static IList<KeyValuePair<string, string>> AvailableLanguages()
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
                {
                    list.Add(new KeyValuePair<string, string>(lang.LanguageTag, lang.DisplayName));
                }
            }
            catch (Exception)
            {
                // no OCR packs installed -- caller shows the empty state
            }
            return list;
        }

        /// <summary>
        /// Recognises the bitmap and returns plain text. <paramref name="keepLines"/>
        /// keeps the layout's line breaks; otherwise everything is flowed into one
        /// paragraph (better when pulling a sentence out of a wrapped column).
        /// </summary>
        public static string Recognize(Bitmap source, string languageTag, bool keepLines)
        {
            if (source == null) return string.Empty;

            // WinRT calls are async; this runs off the UI thread so the wait can't deadlock
            return Task.Run(() => RecognizeAsync(source, languageTag, keepLines)).GetAwaiter().GetResult();
        }

        private static async Task<string> RecognizeAsync(Bitmap source, string languageTag, bool keepLines)
        {
            var engine = CreateEngine(languageTag);
            if (engine == null)
            {
                throw new InvalidOperationException(
                    "Windows has no OCR language pack installed. Add one under " +
                    "Settings > Time & language > Language & region.");
            }

            using (var scaled = ScaleForAccuracy(source))
            using (var stream = new MemoryStream())
            {
                scaled.Save(stream, ImageFormat.Bmp);
                stream.Position = 0;

                using (var winStream = new InMemoryRandomAccessStream())
                {
                    await winStream.WriteAsync(stream.ToArray().AsBuffer());
                    winStream.Seek(0);

                    var decoder = await BitmapDecoder.CreateAsync(winStream);
                    using (var software = await decoder.GetSoftwareBitmapAsync(
                               BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))
                    {
                        var result = await engine.RecognizeAsync(software);
                        return Compose(result, keepLines);
                    }
                }
            }
        }

        private static OcrEngine CreateEngine(string languageTag)
        {
            if (!string.IsNullOrEmpty(languageTag))
            {
                try
                {
                    var engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag));
                    if (engine != null) return engine;
                }
                catch (ArgumentException)
                {
                    // bad tag saved in settings -- fall through to the profile default
                }
            }
            return OcrEngine.TryCreateFromUserProfileLanguages()
                   ?? (OcrEngine.AvailableRecognizerLanguages.Count > 0
                           ? OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0])
                           : null);
        }

        private static Bitmap ScaleForAccuracy(Bitmap source)
        {
            int scale = 1;
            while (source.Width * (scale + 1) <= MinimumWidthForAccuracy && scale < MaximumScale)
            {
                scale++;
            }
            if (scale == 1)
            {
                return new Bitmap(source);
            }

            var scaled = new Bitmap(source.Width * scale, source.Height * scale, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, scaled.Width, scaled.Height));
            }
            return scaled;
        }

        private static string Compose(OcrResult result, bool keepLines)
        {
            if (result == null || result.Lines == null) return string.Empty;

            var lines = result.Lines
                .Select(line => string.Join(" ", line.Words.Select(w => w.Text)))
                .Where(text => text.Length > 0)
                .ToList();

            if (lines.Count == 0) return string.Empty;
            if (keepLines) return string.Join("\r\n", lines);

            // one paragraph: join lines with a space, but respect hyphenated wraps
            var flat = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i > 0)
                {
                    if (flat.Length > 0 && flat[flat.Length - 1] == '-')
                    {
                        flat.Length -= 1;
                    }
                    else
                    {
                        flat.Append(' ');
                    }
                }
                flat.Append(line);
            }
            return flat.ToString();
        }
    }
}
