using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace MicroApp
{
    /// <summary>
    /// Opening other programs' files: Photoshop (.psd/.psb) comes in layer by layer,
    /// Illustrator (.ai) one page per artboard, SVG and EPS as one picture. All are
    /// read-only imports - the result saves as a MicroApp project or a picture, never back
    /// to the original format.
    /// </summary>
    partial class ImageEditorForm
    {
        const string ImportPatterns = "*.psd;*.psb;*.ai;*.svg;*.svgz;*.eps;*.epsf;*.epsi";
        const string ImportFilters = "Photoshop (*.psd, *.psb)|*.psd;*.psb|Illustrator (*.ai)|*.ai|" +
                                     "SVG (*.svg, *.svgz)|*.svg;*.svgz|EPS (*.eps)|*.eps;*.epsf;*.epsi";

        static bool IsImportFile(string file)
        {
            return PsdReader.IsPsd(file) || AiReader.IsAi(file) || SvgReader.IsSvg(file) || EpsReader.IsEps(file);
        }

        static ImportedDocument LoadImport(string file, Action<float, string> report)
        {
            if (AiReader.IsAi(file)) return AiReader.Load(file, report);
            if (SvgReader.IsSvg(file)) return SvgReader.Load(file, report);
            if (EpsReader.IsEps(file)) return EpsReader.Load(file, report);
            return PsdReader.Load(file, report);
        }

        /// <summary>A Photoshop or Illustrator file in a tab of its own, with its layers (and artboards as pages).</summary>
        void OpenImported(string file)
        {
            string full = Path.GetFullPath(file);
            string title = Path.GetFileName(full);
            ImportedDocument d;
            try { d = ProgressDialog.Run(this, "Opening " + title, report => LoadImport(full, report)); }
            catch (Exception ex)
            {
                ModernDialog.Info("Could not open it", title + "\r\n" + ex.Message);
                return;
            }

            if (!StartNewDocTab(title)) { d.Dispose(); return; }
            _ppi = d.Ppi >= 1 ? d.Ppi : 72;
            _doc.Pages = d.Pages.Select((pg, i) =>
            {
                List<EditorLayer> layers = pg.Layers
                    .Where(l => l.Image != null)
                    .Select(l => (EditorLayer)new RasterLayer(l.Image)
                    {
                        Name = l.Name,
                        Visible = l.Visible,
                        Opacity = l.Opacity,
                        Blend = l.Blend,
                        Bounds = new RectangleF(l.Bounds.X, l.Bounds.Y, l.Bounds.Width, l.Bounds.Height)
                    })
                    .ToList();
                return new PageState
                {
                    Name = string.IsNullOrWhiteSpace(pg.Name) ? "Page " + (i + 1) : pg.Name,
                    Canvas = new Size(Math.Max(1, pg.Width), Math.Max(1, pg.Height)),
                    CanvasBg = Color.Transparent,
                    Layers = layers,
                    Sel = layers.Count - 1,
                    NameCounter = 1,
                    Undo = new List<Snapshot>(),
                    Redo = new List<Snapshot>()
                };
            }).ToList();
            _hasDoc = true;
            _dirty = false;
            LoadPage(0);

            if (d.Notes.Count > 0) Toast.Show(string.Join("\r\n", d.Notes));
        }

        /// <summary>File &gt; Add Image with a Photoshop or Illustrator file: its first page, flattened into one picture.</summary>
        Bitmap LoadImportFlat(string file)
        {
            using (ImportedDocument d = ProgressDialog.Run(this, "Reading " + Path.GetFileName(file), report => LoadImport(file, report)))
            {
                ImportedPage pg = d.Pages[0];
                if (pg.Layers.Count == 1 && pg.Layers[0].Image != null && pg.Layers[0].Visible && pg.Layers[0].Opacity == 100 &&
                    pg.Layers[0].Bounds == new Rectangle(0, 0, pg.Width, pg.Height))
                {
                    Bitmap only = pg.Layers[0].Image;
                    pg.Layers[0].Image = null;   // hand it over instead of copying
                    return only;
                }
                var layers = pg.Layers.Where(l => l.Image != null).Select(l => (EditorLayer)new RasterLayer(l.Image)
                {
                    Visible = l.Visible,
                    Opacity = l.Opacity,
                    Blend = l.Blend,
                    Bounds = new RectangleF(l.Bounds.X, l.Bounds.Y, l.Bounds.Width, l.Bounds.Height)
                }).ToList();
                Bitmap flat = EditorRender.Compose(layers, new Size(pg.Width, pg.Height), Color.Transparent);
                flat.SetResolution(d.Ppi, d.Ppi);
                return flat;
            }
        }
    }
}
