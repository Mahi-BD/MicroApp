using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace MicroApp
{
    /// <summary>
    /// The image editor's asset library: logos, stamps, PNGs and vector (WMF/EMF) art the
    /// user wants at hand. It is simply a folder tree under %AppData%\MicroApp\Assets -
    /// every directory is a category (nest them for sub-categories), every image file an
    /// asset. Keeping it as plain files means the user can also fill it from Explorer.
    /// </summary>
    static class AssetStore
    {
        static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".wmf", ".emf" };

        public static string Root
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MicroApp", "Assets");
            }
        }

        public static void EnsureRoot()
        {
            Directory.CreateDirectory(Root);
        }

        public static bool IsSupported(string file)
        {
            return Extensions.Contains(Path.GetExtension(file).ToLowerInvariant());
        }

        /// <summary>All category folders, relative to the root, parents before children.</summary>
        public static List<string> Folders()
        {
            EnsureRoot();
            return Directory.GetDirectories(Root, "*", SearchOption.AllDirectories)
                .Select(d => d.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Asset files directly inside one category ("" = the root).</summary>
        public static List<string> Files(string relFolder)
        {
            string dir = Path.Combine(Root, relFolder ?? "");
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir)
                .Where(IsSupported)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void NewFolder(string relParent, string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            Directory.CreateDirectory(Path.Combine(Root, relParent ?? "", name.Trim()));
        }

        /// <summary>Renames a category folder; returns the new relative path.</summary>
        public static string RenameFolder(string rel, string newName)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) newName = newName.Replace(c, '_');
            newName = newName.Trim();
            if (newName.Length == 0) throw new ArgumentException("The name is empty.");
            string parent = Path.GetDirectoryName(rel) ?? "";
            string newRel = Path.Combine(parent, newName);
            string source = Path.Combine(Root, rel);
            string target = Path.Combine(Root, newRel);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            {
                if (source == target) return rel;      // nothing changed
                Directory.Move(source, target);        // case-only rename
                return newRel;
            }
            if (Directory.Exists(target)) throw new IOException("A category with that name already exists there.");
            Directory.Move(source, target);
            return newRel;
        }

        /// <summary>Copies a file into a category; a name clash gets " (2)", " (3)", ...</summary>
        public static string Import(string sourceFile, string relFolder)
        {
            string dir = Path.Combine(Root, relFolder ?? "");
            Directory.CreateDirectory(dir);
            string baseName = Path.GetFileNameWithoutExtension(sourceFile);
            string ext = Path.GetExtension(sourceFile);
            string target = Path.Combine(dir, baseName + ext);
            int n = 2;
            while (File.Exists(target))
            {
                target = Path.Combine(dir, baseName + " (" + n + ")" + ext);
                n++;
            }
            File.Copy(sourceFile, target);
            return target;
        }

        /// <summary>Saves a bitmap (e.g. a copied layer) into a category as a PNG asset.</summary>
        public static string SaveBitmap(Bitmap bmp, string relFolder, string name)
        {
            string dir = Path.Combine(Root, relFolder ?? "");
            Directory.CreateDirectory(dir);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string target = Path.Combine(dir, name + ".png");
            int n = 2;
            while (File.Exists(target))
            {
                target = Path.Combine(dir, name + " (" + n + ").png");
                n++;
            }
            bmp.Save(target, ImageFormat.Png);
            return target;
        }

        public static void Delete(string file)
        {
            if (File.Exists(file)) File.Delete(file);
        }

        /// <summary>
        /// Loads an asset as a fresh 32-bit bitmap without keeping the file locked.
        /// A metafile (WMF/EMF) is rasterised at twice its nominal size so it stays crisp
        /// when scaled up on the canvas.
        /// </summary>
        public static Bitmap LoadFull(string file)
        {
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Image img = Image.FromStream(fs))
            {
                bool vector = img is Metafile;
                int w = Math.Max(1, vector ? img.Width * 2 : img.Width);
                int h = Math.Max(1, vector ? img.Height * 2 : img.Height);
                Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    EditorRender.Prepare(g);
                    g.DrawImage(img, new Rectangle(0, 0, w, h));
                }
                return bmp;
            }
        }

        /// <summary>A square thumbnail for the asset panel, content centred and letterboxed.</summary>
        public static Bitmap LoadThumb(string file, int size)
        {
            Bitmap thumb = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Image img = Image.FromStream(fs))
                using (Graphics g = Graphics.FromImage(thumb))
                {
                    EditorRender.Prepare(g);
                    float scale = Math.Min((float)size / Math.Max(1, img.Width),
                                           (float)size / Math.Max(1, img.Height));
                    if (scale > 1) scale = 1;
                    int w = Math.Max(1, (int)(img.Width * scale));
                    int h = Math.Max(1, (int)(img.Height * scale));
                    g.DrawImage(img, new Rectangle((size - w) / 2, (size - h) / 2, w, h));
                }
            }
            catch
            {
                using (Graphics g = Graphics.FromImage(thumb))
                using (Pen p = new Pen(Color.Gray))
                {
                    g.DrawRectangle(p, 2, 2, size - 5, size - 5);
                    g.DrawLine(p, 2, 2, size - 3, size - 3);
                }
            }
            return thumb;
        }
    }
}
