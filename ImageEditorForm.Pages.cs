using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Pages: a document can hold several pages, each with its own canvas, layers,
    /// selection and history. The page in front lives in the fields of ImageEditorForm.cs
    /// (exactly like the document in front); the others park in a <see cref="PageState"/>.
    /// The Pages panel left of the canvas shows a thumbnail and the name of each page.
    /// </summary>
    partial class ImageEditorForm
    {
        /// <summary>A page that is not in front. The one in front keeps only its Name and Thumb here.</summary>
        class PageState
        {
            public string Name;
            public List<EditorLayer> Layers;           // null while this page is in front
            public List<Snapshot> Undo, Redo;
            public Size Canvas;
            public Color CanvasBg;
            public int Sel = -1;
            public int NameCounter;
            public EditorSelection Selection, LastSelection;
            public PointF? LastStrokeEnd;
            public Point? CloneSource;
            public Bitmap Thumb;
            public bool ThumbStale = true;
        }

        PagesPanel _pagesPanel;
        bool? _pagesPanelChoice;                       // null: show it only for documents with 2+ pages
        readonly Timer _pageThumbTimer = new Timer { Interval = 600 };
        ToolStripMenuItem _pagesPanelItem;

        List<PageState> Pages { get { return _doc.Pages; } }
        int PageIndex { get { return _doc.PageIndex; } }
        PageState ActivePage { get { return _doc.Pages[_doc.PageIndex]; } }

        void BuildPagesPanel()
        {
            int width = 196;
            try { width = Math.Max(140, Math.Min(480, Properties.Settings.Default.EditorPagesPanelWidth)); } catch { }
            _pagesPanel = new PagesPanel(this) { Dock = DockStyle.Left, Width = width, Visible = false };
            _pageThumbTimer.Tick += delegate
            {
                if (_drag != Drag.None) return;   // wait for the stroke / drag to finish
                _pageThumbTimer.Stop();
                if (_pagesPanel.Visible) _pagesPanel.Invalidate();
            };
        }

        /// <summary>The page in front changed: its thumbnail is redrawn a moment later.</summary>
        void PageContentChanged()
        {
            if (_doc == null || _doc.Pages.Count == 0) return;
            ActivePage.ThumbStale = true;
            if (_pagesPanel != null && _pagesPanel.Visible) { _pageThumbTimer.Stop(); _pageThumbTimer.Start(); }
        }

        bool PagesPanelWanted()
        {
            if (!_hasDoc) return false;
            return _pagesPanelChoice ?? Pages.Count > 1;
        }

        /// <summary>Shows or hides the Pages panel for the document in front; true when that changed the canvas size.</summary>
        bool UpdatePagesPanel()
        {
            if (_pagesPanel == null) return false;
            bool want = PagesPanelWanted();
            if (_pagesPanelItem != null) _pagesPanelItem.Checked = want;
            if (_pagesPanel.Visible == want) { _pagesPanel.Invalidate(); return false; }
            _pagesPanel.Visible = want;
            if (want) _pagesPanel.EnsureVisible(PageIndex);
            return true;
        }

        /// <summary>The Pages panel's grip was dragged: remember the width and re-fit the canvas.</summary>
        void PagesPanelResized(bool done)
        {
            if (_viewFitted) FitView();
            _canvasPanel.Invalidate();
            if (!done) return;
            try
            {
                Properties.Settings.Default.EditorPagesPanelWidth = _pagesPanel.Width;
                Properties.Settings.Default.Save();
            }
            catch { }
            _pagesPanel.EnsureVisible(PageIndex);
        }

        void TogglePagesPanel()
        {
            if (!EnsureDoc()) return;
            _pagesPanelChoice = !_pagesPanel.Visible;
            if (UpdatePagesPanel() && _viewFitted) { FitView(); _canvasPanel.Invalidate(); }
        }

        string NextPageName()
        {
            for (int n = Pages.Count + 1; ; n++)
            {
                string name = "Page " + n;
                if (!Pages.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
            }
        }

        // ------------------------------------------------------------ park / load

        /// <summary>Moves the front page's fields into its PageState.</summary>
        void ParkPage()
        {
            PageState p = ActivePage;
            p.Thumb = MakePageThumb(_hasDoc && _canvas.Width > 0 ? ThumbSource() : null, p.Thumb);
            p.ThumbStale = false;
            p.Layers = new List<EditorLayer>(_layers);
            p.Undo = new List<Snapshot>(_undo);
            p.Redo = new List<Snapshot>(_redo);
            p.Canvas = _canvas;
            p.CanvasBg = _canvasBg;
            p.Sel = _sel;
            p.NameCounter = _nameCounter;
            p.Selection = _selection;
            p.LastSelection = _lastSelection;
            p.LastStrokeEnd = _lastStrokeEnd;
            p.CloneSource = _cloneSource;
            _multi.Clear();
            _layers.Clear();
            _undo.Clear();
            _redo.Clear();
        }

        /// <summary>Brings a parked page of the front document forward (the caller parked the old one).</summary>
        void LoadPage(int index)
        {
            _doc.PageIndex = index;
            PageState p = ActivePage;
            _layers.Clear();
            _undo.Clear();
            _redo.Clear();
            if (p.Layers != null) _layers.AddRange(p.Layers);
            if (p.Undo != null) _undo.AddRange(p.Undo);
            if (p.Redo != null) _redo.AddRange(p.Redo);
            p.Layers = null; p.Undo = null; p.Redo = null;
            _canvas = p.Canvas;
            _canvasBg = p.CanvasBg;
            _sel = Math.Min(p.Sel, _layers.Count - 1);
            _nameCounter = p.NameCounter;
            _selection = p.Selection;
            _lastSelection = p.LastSelection;
            _lastStrokeEnd = p.LastStrokeEnd;
            _cloneSource = p.CloneSource;
            p.Selection = p.LastSelection = null;
            _coalesceKey = null;
            _viewFitted = true;   // every page opens fitted, whatever its size
            ShowActiveDoc();
            _pagesPanel.EnsureVisible(index);
        }

        /// <summary>Clicking a page: its layers, selection and history come forward.</summary>
        void SelectPage(int index)
        {
            if (index < 0 || index >= Pages.Count || index == PageIndex) return;
            if (!SettleForSwitch()) return;
            ParkPage();
            LoadPage(index);
        }

        /// <summary>Every snapshot of a document's parked pages, which are then emptied (for closing it).</summary>
        static List<Snapshot> DetachParkedPages(DocState d)
        {
            var all = new List<Snapshot>();
            foreach (PageState p in d.Pages)
            {
                if (p.Undo != null) all.AddRange(p.Undo);
                if (p.Redo != null) all.AddRange(p.Redo);
                if (p.Layers != null) all.Add(new Snapshot { Name = "closing", Layers = p.Layers.ToArray() });
                p.Layers = null; p.Undo = null; p.Redo = null;
                if (p.Thumb != null) { p.Thumb.Dispose(); p.Thumb = null; }
            }
            return all;
        }

        // ------------------------------------------------------------ commands

        /// <summary>Page &gt; New Page: the same size and background as the page in front, one empty layer.</summary>
        void NewPage()
        {
            if (!EnsureDoc() || !SettleForSwitch()) return;
            Size size = _canvas;
            Color bg = _canvasBg;
            ParkPage();
            var page = new PageState
            {
                Name = NextPageName(),
                Canvas = size,
                CanvasBg = bg,
                Layers = new List<EditorLayer> { new RasterLayer(NewTransparentBitmap(size.Width, size.Height)) { Name = "Layer 1", Bounds = new RectangleF(0, 0, size.Width, size.Height) } },
                Sel = 0,
                NameCounter = 1
            };
            int at = PageIndex + 1;
            Pages.Insert(at, page);
            _dirty = true;
            LoadPage(at);
            Toast.Show(page.Name + " added.");
        }

        /// <summary>Page &gt; Duplicate Page: a copy of the page, layers and all, right after it.</summary>
        void DuplicatePage(int index)
        {
            if (!EnsureDoc() || !SettleForSwitch()) return;
            if (index != PageIndex) SelectPage(index);
            if (index != PageIndex) return;
            PageState src = ActivePage;
            var copy = new PageState
            {
                Name = src.Name + " copy",
                Canvas = _canvas,
                CanvasBg = _canvasBg,
                // a clone shares the bitmap, just as Duplicate Layer does: painting never writes into a shared one
                Layers = _layers.Select(l => { EditorLayer c = l.Clone(); c.DropCache(); return c; }).ToList(),
                Sel = _sel,
                NameCounter = _nameCounter,
                Selection = _selection
            };
            ParkPage();
            Pages.Insert(index + 1, copy);
            _dirty = true;
            LoadPage(index + 1);
        }

        void RenamePage(int index)
        {
            if (index < 0 || index >= Pages.Count) return;
            string name = EditorPrompt.Ask(this, "Rename Page", "Page name:", Pages[index].Name);
            if (string.IsNullOrWhiteSpace(name) || name == Pages[index].Name) return;
            Pages[index].Name = name;
            _dirty = true;
            _pagesPanel.Invalidate();
            UpdateStatus();
        }

        void DeletePage(int index)
        {
            if (index < 0 || index >= Pages.Count) return;
            if (Pages.Count == 1) { Toast.Show("A document keeps at least one page."); return; }
            if (!SettleForSwitch()) return;
            if (!ModernDialog.Confirm("Delete " + Pages[index].Name + "?", "The page and all of its layers are removed. This cannot be undone.", "Delete", "Keep it")) return;

            var dropped = new List<Snapshot>();
            PageState gone = Pages[index];
            if (index == PageIndex)
            {
                dropped.AddRange(_undo);
                dropped.AddRange(_redo);
                dropped.Add(new Snapshot { Name = "deleted", Layers = _layers.ToArray() });
                foreach (EditorLayer l in _layers) l.DropCache();
                _multi.Clear();
                _layers.Clear();
                _undo.Clear();
                _redo.Clear();
                Pages.RemoveAt(index);
                _dirty = true;
                LoadPage(Math.Min(index, Pages.Count - 1));
            }
            else
            {
                if (gone.Undo != null) dropped.AddRange(gone.Undo);
                if (gone.Redo != null) dropped.AddRange(gone.Redo);
                if (gone.Layers != null) dropped.Add(new Snapshot { Name = "deleted", Layers = gone.Layers.ToArray() });
                Pages.RemoveAt(index);
                if (index < PageIndex) _doc.PageIndex--;
                _dirty = true;
                if (UpdatePagesPanel() && _viewFitted) FitView();
                UpdateStatus();
                if (_docTabs != null) _docTabs.Invalidate();
            }
            if (gone.Thumb != null) gone.Thumb.Dispose();
            ReleaseDropped(dropped);   // after the removal: nothing live refers to them any more
            GC.Collect();
        }

        /// <summary>Moves a page to another place in the order (the Pages panel's drag, Move Up / Down).</summary>
        void MovePage(int from, int to)
        {
            to = Math.Max(0, Math.Min(Pages.Count - 1, to));
            if (from < 0 || from >= Pages.Count || from == to) return;
            PageState front = ActivePage;
            PageState p = Pages[from];
            Pages.RemoveAt(from);
            Pages.Insert(to, p);
            _doc.PageIndex = Pages.IndexOf(front);
            _dirty = true;
            _pagesPanel.Invalidate();
            _pagesPanel.EnsureVisible(to);
            UpdateStatus();
            if (_docTabs != null) _docTabs.Invalidate();
        }

        void ShowPageMenu(int index, Point screen)
        {
            ContextMenuStrip menu = NewContextMenu();
            menu.Items.Add(Item("New Page", Keys.None, delegate { NewPage(); }));
            if (index >= 0)
            {
                menu.Items.Add(Item("Duplicate Page", Keys.None, delegate { DuplicatePage(index); }));
                menu.Items.Add(Item("Rename Page…", Keys.None, delegate { RenamePage(index); }));
                var del = Item("Delete Page", Keys.None, delegate { DeletePage(index); });
                del.Enabled = Pages.Count > 1;
                menu.Items.Add(del);
                menu.Items.Add(new ToolStripSeparator());
                var up = Item("Move Up", Keys.None, delegate { MovePage(index, index - 1); });
                up.Enabled = index > 0;
                var down = Item("Move Down", Keys.None, delegate { MovePage(index, index + 1); });
                down.Enabled = index < Pages.Count - 1;
                menu.Items.Add(up);
                menu.Items.Add(down);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Hide Pages Panel", Keys.None, delegate { TogglePagesPanel(); }));
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(screen);
        }

        ToolStripMenuItem BuildPageMenu()
        {
            var page = new ToolStripMenuItem("Page");
            page.DropDownItems.Add(Item("New Page", Keys.None, delegate { NewPage(); }));
            page.DropDownItems.Add(Item("Duplicate Page", Keys.None, delegate { DuplicatePage(PageIndex); }));
            page.DropDownItems.Add(Item("Rename Page…", Keys.None, delegate { RenamePage(PageIndex); }));
            var del = Item("Delete Page", Keys.None, delegate { DeletePage(PageIndex); });
            page.DropDownItems.Add(del);
            page.DropDownItems.Add(new ToolStripSeparator());
            var up = Item("Move Page Up", Keys.None, delegate { MovePage(PageIndex, PageIndex - 1); });
            var down = Item("Move Page Down", Keys.None, delegate { MovePage(PageIndex, PageIndex + 1); });
            page.DropDownItems.Add(up);
            page.DropDownItems.Add(down);
            page.DropDownItems.Add(new ToolStripSeparator());
            var prev = Item("Previous Page", Keys.Control | Keys.PageUp, delegate { SelectPage(PageIndex - 1); });
            var next = Item("Next Page", Keys.Control | Keys.PageDown, delegate { SelectPage(PageIndex + 1); });
            page.DropDownItems.Add(prev);
            page.DropDownItems.Add(next);
            page.DropDownItems.Add(new ToolStripSeparator());
            _pagesPanelItem = Item("Show Pages Panel", Keys.None, delegate { TogglePagesPanel(); });
            page.DropDownItems.Add(_pagesPanelItem);
            page.DropDownOpening += delegate
            {
                bool doc = _hasDoc;
                foreach (ToolStripItem it in page.DropDownItems) it.Enabled = doc;
                del.Enabled = doc && Pages.Count > 1;
                up.Enabled = doc && PageIndex > 0;
                down.Enabled = doc && PageIndex < Pages.Count - 1;
                prev.Enabled = doc && PageIndex > 0;
                next.Enabled = doc && PageIndex < Pages.Count - 1;
                _pagesPanelItem.Checked = _pagesPanel.Visible;
            };
            return page;
        }

        // ------------------------------------------------------------ rendering

        /// <summary>A page flattened for printing (opaque: transparent areas print white).</summary>
        Bitmap RenderPageForPrint(int index)
        {
            if (index == PageIndex) return Flattened(true);
            PageState p = Pages[index];
            Color bg = p.CanvasBg.A < 255 ? Color.White : p.CanvasBg;
            return EditorRender.Flatten(p.Layers, p.Canvas, bg);
        }

        Size PageCanvas(int index)
        {
            return index == PageIndex ? _canvas : Pages[index].Canvas;
        }

        /// <summary>The page's thumbnail, rebuilt when its content changed.</summary>
        Bitmap PageThumb(int index)
        {
            PageState p = Pages[index];
            if (p.Thumb != null && !p.ThumbStale) return p.Thumb;
            if (index == PageIndex)
            {
                if (_drag != Drag.None && p.Thumb != null) return p.Thumb;   // not mid-stroke
                p.Thumb = MakePageThumb(ThumbSource(), p.Thumb);
            }
            else
            {
                using (Bitmap flat = EditorRender.Flatten(p.Layers, p.Canvas, p.CanvasBg))
                    p.Thumb = MakePageThumb(flat, p.Thumb);
            }
            p.ThumbStale = false;
            return p.Thumb;
        }

        const int PageThumbMax = 480;

        /// <summary>The front page's picture for its thumbnail: the zoomed-out view when that is already a big enough shrunk copy.</summary>
        Bitmap ThumbSource()
        {
            return _zoom < 1f && Math.Max(_canvas.Width, _canvas.Height) * _zoom >= PageThumbMax ? ScaledView() : Composite();
        }

        static Bitmap MakePageThumb(Bitmap src, Bitmap old)
        {
            if (old != null) old.Dispose();
            if (src == null) return null;
            float s = Math.Min(1f, (float)PageThumbMax / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)(src.Width * s)), h = Math.Max(1, (int)(src.Height * s));
            return Pixels.Downscale(src, w, h);
        }

        // ------------------------------------------------------------ the panel

        /// <summary>
        /// The Pages panel: a thumbnail and the name of every page. Click to switch,
        /// double-click to rename, drag to reorder, right-click for the page menu.
        /// </summary>
        sealed class PagesPanel : Control
        {
            const int HeaderH = 34, Pad = 14, NameH = 22, Gap = 10, BarW = 4, GripW = 7;
            const int MinWidth = 140, MaxWidth = 480, CanvasKeeps = 360;
            readonly ImageEditorForm _ed;
            readonly List<Rectangle> _items = new List<Rectangle>();   // each page's whole row, in scrolled coords
            int _scroll, _contentH, _hover = -1;
            Rectangle _addBtn, _hideBtn;
            int _hoverBtn;                                           // 1 add, 2 hide
            int _downIndex = -1, _dropAt = -1;
            Point _downPt;
            bool _dragging, _barDrag, _barHover;
            int _barGrab;
            bool _gripHover, _resizing;                              // the grip on the right edge resizes the panel
            int _resizeX0, _resizeW0;

            public PagesPanel(ImageEditorForm ed)
            {
                _ed = ed;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Surface;
            }

            int ThumbBoxW { get { return Width - 2 * Pad - GripW; } }   // the scrollbar overlays the right padding

            bool OnGrip(Point p) { return p.X >= Width - GripW; }

            void LayoutItems()
            {
                _items.Clear();
                int y = HeaderH + 8;
                int boxW = ThumbBoxW;
                for (int i = 0; i < _ed.Pages.Count; i++)
                {
                    Size c = _ed.PageCanvas(i);
                    float aspect = c.Width > 0 ? (float)c.Height / c.Width : 1.4f;
                    int boxH = Math.Max(40, Math.Min((int)(boxW * 1.5f), (int)(boxW * aspect)));
                    _items.Add(new Rectangle(Pad - 6, y, boxW + 12, boxH + NameH + 10));
                    y += boxH + NameH + 10 + Gap;
                }
                _contentH = y;
                ClampScroll();
            }

            int ViewH { get { return Height - HeaderH; } }

            void ClampScroll()
            {
                _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, _contentH - Height)));
            }

            public void EnsureVisible(int index)
            {
                LayoutItems();
                if (index < 0 || index >= _items.Count) return;
                Rectangle r = _items[index];
                if (r.Top - _scroll < HeaderH) _scroll = r.Top - HeaderH - 4;
                else if (r.Bottom - _scroll > Height) _scroll = r.Bottom - Height + 4;
                ClampScroll();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Surface);
                LayoutItems();
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // the list
                GraphicsState st = g.Save();
                g.SetClip(new Rectangle(0, HeaderH, Width, Height - HeaderH));
                using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                using (var border = new Pen(Theme.Border))
                using (var accent = new Pen(Theme.Accent, 2f))
                using (var hover = new Pen(Theme.TextDim))
                using (var hoverFill = new SolidBrush(Theme.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(10, 0, 0, 0)))
                {
                    for (int i = 0; i < _items.Count; i++)
                    {
                        Rectangle row = _items[i];
                        row.Offset(0, -_scroll);
                        if (row.Bottom < HeaderH || row.Top > Height) continue;
                        bool active = i == _ed.PageIndex;
                        if (active || i == _hover) g.FillRectangle(hoverFill, row);

                        var box = new Rectangle(row.X + 6, row.Y + 6, ThumbBoxW, row.Height - NameH - 10);
                        Bitmap thumb = null;
                        try { thumb = _ed.PageThumb(i); } catch { }
                        Rectangle pic = box;
                        if (thumb != null)
                        {
                            float s = Math.Min((float)box.Width / thumb.Width, (float)box.Height / thumb.Height);
                            int w = Math.Max(1, (int)(thumb.Width * s)), h = Math.Max(1, (int)(thumb.Height * s));
                            pic = new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                        }
                        g.FillRectangle(shadow, pic.X + 2, pic.Y + 3, pic.Width, pic.Height);
                        DrawChecker(g, pic);
                        if (thumb != null)
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(thumb, pic);
                        }
                        g.SmoothingMode = SmoothingMode.None;
                        g.DrawRectangle(active ? accent : i == _hover ? hover : border, pic.X - (active ? 1 : 0), pic.Y - (active ? 1 : 0),
                                        pic.Width + (active ? 1 : 0), pic.Height + (active ? 1 : 0));
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        var nameRect = new Rectangle(row.X + 6, row.Bottom - NameH - 4, ThumbBoxW, NameH);
                        string num = (i + 1).ToString();
                        TextRenderer.DrawText(g, num, Theme.Small, new Rectangle(nameRect.X, nameRect.Y, 24, nameRect.Height), Theme.TextDim,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        TextRenderer.DrawText(g, _ed.Pages[i].Name, active ? Theme.Strong : Theme.Base,
                            new Rectangle(nameRect.X + 22, nameRect.Y, nameRect.Width - 22, nameRect.Height), active ? Theme.Accent : Theme.Text,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    }

                    // where a dragged page will land
                    if (_dragging && _dropAt >= 0)
                    {
                        int y = _dropAt < _items.Count ? _items[_dropAt].Top - Gap / 2 : _items[_items.Count - 1].Bottom + Gap / 2;
                        y -= _scroll;
                        using (var line = new Pen(Theme.Accent, 3f)) g.DrawLine(line, Pad - 4, y, Width - GripW - Pad + 4, y);
                    }
                }
                g.Restore(st);

                // a thin overlay scrollbar, only when the pages overflow; a little wider under the mouse
                if (_contentH > Height)
                {
                    Rectangle bar = BarRect();
                    if (_barHover || _barDrag) bar = new Rectangle(bar.X - 2, bar.Y, bar.Width + 2, bar.Height);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = Theme.Round(bar, bar.Width / 2))
                    using (var b = new SolidBrush(Color.FromArgb(_barDrag ? 170 : _barHover ? 130 : 80, Theme.TextDim)))
                        g.FillPath(b, path);
                }

                // the header: title, + (new page), « (hide)
                using (var hb = new SolidBrush(Theme.Surface)) g.FillRectangle(hb, 0, 0, Width, HeaderH);
                TextRenderer.DrawText(g, "PAGES", Theme.Small, new Rectangle(Pad, 0, 100, HeaderH), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, _ed.Pages.Count.ToString(), Theme.Small, new Rectangle(Pad + 46, 0, 40, HeaderH), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                _hideBtn = new Rectangle(Width - GripW - 6 - 26, 4, 26, 26);
                _addBtn = new Rectangle(_hideBtn.X - 28, 4, 26, 26);
                DrawHeaderButton(g, _addBtn, _hoverBtn == 1, true);
                DrawHeaderButton(g, _hideBtn, _hoverBtn == 2, false);
                using (var p = new Pen(Theme.Border))
                {
                    g.SmoothingMode = SmoothingMode.None;
                    g.DrawLine(p, 0, HeaderH - 1, Width - GripW, HeaderH - 1);
                }
                // the resize grip on the right edge, drawn like the splitter beside the Layers panel
                using (var line = new Pen(Theme.Border))
                {
                    g.SmoothingMode = SmoothingMode.None;
                    g.DrawLine(line, Width - GripW, 0, Width - GripW, Height);
                    g.DrawLine(line, Width - 1, 0, Width - 1, Height);
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var dot = new SolidBrush(_gripHover || _resizing ? Theme.Accent : Theme.TextDim))
                {
                    float cx = Width - GripW / 2f, cy = Height / 2f;
                    for (int i = -2; i <= 2; i++) g.FillEllipse(dot, cx - 1.5f, cy + i * 6 - 1.5f, 3, 3);
                }
            }

            static void DrawChecker(Graphics g, Rectangle r)
            {
                g.FillRectangle(Brushes.White, r);
                using (var b = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(230, 230, 230), Color.White))
                    g.FillRectangle(b, r);
            }

            void DrawHeaderButton(Graphics g, Rectangle r, bool hot, bool plus)
            {
                if (hot) using (var b = new SolidBrush(Theme.Dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0))) g.FillRectangle(b, r);
                using (var pen = new Pen(Theme.Text, 1.6f))
                {
                    int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                    if (plus)
                    {
                        g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
                        g.DrawLine(pen, cx, cy - 6, cx, cy + 6);
                    }
                    else
                    {
                        // «: hide the panel
                        g.DrawLines(pen, new[] { new Point(cx + 1, cy - 5), new Point(cx - 4, cy), new Point(cx + 1, cy + 5) });
                        g.DrawLines(pen, new[] { new Point(cx + 6, cy - 5), new Point(cx + 1, cy), new Point(cx + 6, cy + 5) });
                    }
                }
            }

            Rectangle BarRect()
            {
                int track = Height - HeaderH - 4;
                int h = Math.Max(24, (int)((float)track * Height / Math.Max(1, _contentH)));
                int max = Math.Max(1, _contentH - Height);
                int y = HeaderH + 2 + (int)((float)(track - h) * _scroll / max);
                return new Rectangle(Width - GripW - BarW - 3, y, BarW, h);
            }

            int HitItem(Point p)
            {
                if (p.Y < HeaderH) return -1;
                for (int i = 0; i < _items.Count; i++)
                {
                    Rectangle r = _items[i];
                    r.Offset(0, -_scroll);
                    if (r.Contains(p)) return i;
                }
                return -1;
            }

            int DropIndex(Point p)
            {
                int y = p.Y + _scroll;
                for (int i = 0; i < _items.Count; i++)
                    if (y < _items[i].Top + _items[i].Height / 2) return i;
                return _items.Count;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Focus();
                if (e.Button == MouseButtons.Left)
                {
                    if (_addBtn.Contains(e.Location)) { _ed.NewPage(); return; }
                    if (_hideBtn.Contains(e.Location)) { _ed.TogglePagesPanel(); return; }
                    if (OnGrip(e.Location))
                    {
                        _resizing = true;
                        _resizeX0 = Cursor.Position.X;
                        _resizeW0 = Width;
                        Capture = true;
                        Invalidate();
                        return;
                    }
                    if (_contentH > Height && e.X >= Width - GripW - BarW - 10)
                    {
                        Rectangle bar = BarRect();
                        if (bar.Contains(e.Location)) { _barDrag = true; _barGrab = e.Y - bar.Y; Capture = true; return; }
                        _scroll += e.Y < bar.Y ? -ViewH : ViewH;   // a click on the track pages
                        ClampScroll();
                        Invalidate();
                        return;
                    }
                    int hit = HitItem(e.Location);
                    if (hit < 0) return;
                    _downIndex = hit;
                    _downPt = e.Location;
                    Capture = true;
                    if (e.Clicks == 2) { _downIndex = -1; Capture = false; _ed.RenamePage(hit); return; }
                    _ed.SelectPage(hit);
                }
                else if (e.Button == MouseButtons.Right)
                {
                    int hit = HitItem(e.Location);
                    if (hit >= 0 && hit != _ed.PageIndex) _ed.SelectPage(hit);
                    _ed.ShowPageMenu(hit >= 0 ? _ed.PageIndex : -1, PointToScreen(e.Location));
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (_resizing)
                {
                    int room = Parent != null ? Parent.ClientSize.Width - CanvasKeeps : MaxWidth;
                    int w = Math.Max(MinWidth, Math.Min(Math.Min(MaxWidth, Math.Max(MinWidth, room)), _resizeW0 + Cursor.Position.X - _resizeX0));
                    if (w != Width)
                    {
                        Width = w;
                        _ed.PagesPanelResized(false);
                    }
                    return;
                }
                if (_barDrag)
                {
                    int track = Height - HeaderH - 4;
                    int h = BarRect().Height;
                    float t = (float)(e.Y - _barGrab - HeaderH - 2) / Math.Max(1, track - h);
                    _scroll = (int)(t * Math.Max(0, _contentH - Height));
                    ClampScroll();
                    Invalidate();
                    return;
                }
                if (_downIndex >= 0 && e.Button == MouseButtons.Left)
                {
                    if (!_dragging && (Math.Abs(e.Y - _downPt.Y) > 6 || Math.Abs(e.X - _downPt.X) > 6)) _dragging = true;
                    if (_dragging)
                    {
                        _dropAt = DropIndex(e.Location);
                        // scroll while dragging near the edges
                        if (e.Y < HeaderH + 20) { _scroll -= 12; ClampScroll(); }
                        else if (e.Y > Height - 20) { _scroll += 12; ClampScroll(); }
                        Cursor = Cursors.SizeNS;
                        Invalidate();
                    }
                    return;
                }
                bool grip = OnGrip(e.Location);
                bool onBar = !grip && _contentH > Height && e.Y > HeaderH && e.X >= Width - GripW - BarW - 10;
                int hover = onBar || grip ? -1 : HitItem(e.Location);
                int btn = _addBtn.Contains(e.Location) ? 1 : _hideBtn.Contains(e.Location) ? 2 : 0;
                if (hover != _hover || btn != _hoverBtn || onBar != _barHover || grip != _gripHover)
                {
                    _hover = hover;
                    _hoverBtn = btn;
                    _barHover = onBar;
                    _gripHover = grip;
                    Invalidate();
                }
                Cursor = grip ? Cursors.SizeWE : Cursors.Default;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                Capture = false;
                if (_resizing) { _resizing = false; Invalidate(); _ed.PagesPanelResized(true); return; }
                if (_barDrag) { _barDrag = false; Invalidate(); return; }
                if (_dragging && _downIndex >= 0 && _dropAt >= 0)
                {
                    int to = _dropAt > _downIndex ? _dropAt - 1 : _dropAt;
                    _ed.MovePage(_downIndex, to);
                }
                _dragging = false;
                _downIndex = -1;
                _dropAt = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hover != -1 || _hoverBtn != 0 || _barHover || _gripHover) { _hover = -1; _hoverBtn = 0; _barHover = false; _gripHover = false; Invalidate(); }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _scroll -= e.Delta / 120 * 80;
                ClampScroll();
                Invalidate();
            }
        }
    }
}
