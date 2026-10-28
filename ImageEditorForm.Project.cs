using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace MicroApp
{
    /// <summary>
    /// MicroApp projects (.ma): one file that keeps the whole document - every layer with
    /// its settings and styles, editable text and shapes, the selection and the guides -
    /// so it opens again exactly as it was saved. Save and open run behind a progress bar.
    /// </summary>
    partial class ImageEditorForm
    {
        /// <summary>File &gt; Save: back to the project it came from, or Save As for anything else.</summary>
        void Save()
        {
            if (!EnsureDoc()) return;
            if (_doc.ProjectPath != null) SaveProject(_doc.ProjectPath);
            else SaveAs();
        }

        bool SaveProject(string path)
        {
            if (!SettleForSwitch()) return false;
            if (!EnsureDoc()) return false;

            var project = new EditorProject
            {
                Ppi = _ppi,
                PageIndex = PageIndex,
                Guides = _guides.Select(g => new ProjectGuide { Vertical = g.Vertical, Pos = g.Pos, Locked = g.Locked, Color = g.Color }).ToList(),
                RulerZero = _rulerZero
            };
            for (int i = 0; i < Pages.Count; i++)
            {
                PageState ps = Pages[i];
                bool front = i == PageIndex;
                var page = new ProjectPage
                {
                    Name = ps.Name,
                    Canvas = front ? _canvas : ps.Canvas,
                    CanvasBg = front ? _canvasBg : ps.CanvasBg,
                    Sel = front ? _sel : ps.Sel,
                    NameCounter = front ? _nameCounter : ps.NameCounter,
                    Layers = (front ? _layers : ps.Layers).Select(l => l.Clone()).ToList(),
                    Selection = front ? _selection : ps.Selection,
                    LastSelection = front ? _lastSelection : ps.LastSelection
                };
                // the worker asks for one layer's pixels at a time; each copy is made on the UI
                // thread (GDI+ bitmaps are not shared across threads - the canvas may repaint
                // from them meanwhile) and dropped once it is packed
                var images = new List<Bitmap>();
                foreach (EditorLayer l in page.Layers)
                {
                    var r = l as RasterLayer;
                    images.Add(r != null ? r.Image : null);
                    page.PixelCounts.Add(r != null && r.Image != null ? (long)r.Image.Width * r.Image.Height : 0);
                }
                page.PixelsOf = layerIndex =>
                {
                    Bitmap img = images[layerIndex];
                    if (img == null) return null;
                    return (ProjectFile.PixelCopy)Invoke(new Func<ProjectFile.PixelCopy>(() => ProjectFile.CopyPixels(img)));
                };
                project.Pages.Add(page);
            }

            try
            {
                ProgressDialog.Run(this, "Saving " + Path.GetFileName(path), report =>
                {
                    ProjectFile.Save(path, project, report);
                    return true;
                });
            }
            catch (Exception ex)
            {
                ModernDialog.Info("Could not save it", Path.GetFileName(path) + "\r\n" + ex.Message);
                return false;
            }
            _doc.ProjectPath = path;
            _doc.Title = Path.GetFileName(path);
            _dirty = false;
            UpdateDocChrome();
            Toast.Show("Project saved.\r\n" + path);
            return true;
        }

        /// <summary>Opens a .ma project in a tab of its own (or brings its tab forward when it is open already).</summary>
        void OpenProject(string path)
        {
            string full = Path.GetFullPath(path);
            DocState open = _docs.FirstOrDefault(d => d.ProjectPath != null && string.Equals(d.ProjectPath, full, StringComparison.OrdinalIgnoreCase));
            if (open != null) { ActivateDoc(open); return; }

            EditorProject p;
            try { p = ProgressDialog.Run(this, "Opening " + Path.GetFileName(full), report => ProjectFile.Load(full, report)); }
            catch (Exception ex)
            {
                ModernDialog.Info("Could not open it", Path.GetFileName(full) + "\r\n" + ex.Message);
                return;
            }

            if (!StartNewDocTab(Path.GetFileName(full)))
            {
                foreach (ProjectPage page in p.Pages)
                    foreach (EditorLayer l in page.Layers) { var r = l as RasterLayer; if (r != null && r.Image != null) r.Image.Dispose(); }
                return;
            }
            _doc.ProjectPath = full;
            _ppi = p.Ppi >= 1 ? p.Ppi : 72;
            _guides = p.Guides.Select(g => new Guide { Vertical = g.Vertical, Pos = g.Pos, Locked = g.Locked, Color = g.Color }).ToList();
            _rulerZero = p.RulerZero;
            // every page parks in its PageState; the saved front page then comes forward
            _doc.Pages = p.Pages.Select(pg => new PageState
            {
                Name = string.IsNullOrWhiteSpace(pg.Name) ? "Page" : pg.Name,
                Canvas = pg.Canvas,
                CanvasBg = pg.CanvasBg,
                Sel = pg.Sel,
                NameCounter = pg.NameCounter,
                Layers = pg.Layers,
                Undo = new List<Snapshot>(),
                Redo = new List<Snapshot>(),
                Selection = pg.Selection,
                LastSelection = pg.LastSelection
            }).ToList();
            _hasDoc = true;
            _dirty = false;
            LoadPage(p.PageIndex);
        }
    }
}
