using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Document tabs, the way Photoshop keeps several images open in one window. Every
    /// other part of the editor works on the fields in ImageEditorForm.cs (layers, undo,
    /// view); the documents that are not in front park those fields in a
    /// <see cref="DocState"/> and get them back when their tab is clicked.
    /// </summary>
    partial class ImageEditorForm
    {
        /// <summary>A document that is not in front. The one in front keeps only its Title here.</summary>
        class DocState
        {
            public string Title;
            public List<EditorLayer> Layers;           // null while this document is in front
            public Size Canvas;
            public Color CanvasBg;
            public bool HasDoc;
            public int Sel = -1;
            public bool Dirty;
            public int NameCounter;
            public EditorSelection Selection, LastSelection;
            public List<Snapshot> Undo, Redo;
            public float Zoom = 1f;
            public PointF Origin;
            public bool ViewFitted;
            public PointF? LastStrokeEnd;
            public Point? CloneSource;
            public float Ppi = 72;
        }

        readonly List<DocState> _docs = new List<DocState>();
        DocState _doc;
        int _untitledCounter;
        DocTabStrip _docTabs;
        Panel _docArea;
        float _ppi = 72;          // the front document's print resolution (File > New, or the opened file's)

        void BuildDocTabs()
        {
            _doc = new DocState { Title = NextUntitled() };
            _docs.Add(_doc);
            _docTabs = new DocTabStrip(this) { Dock = DockStyle.Top };
            _docArea = new Panel { Dock = DockStyle.Fill, BackColor = _canvasPanel.BackColor };
            _docArea.Controls.Add(_canvasPanel);
            _docArea.Controls.Add(_docTabs);
        }

        string NextUntitled()
        {
            _untitledCounter++;
            return "Untitled-" + _untitledCounter;
        }

        bool DocDirty(DocState d)
        {
            return d == _doc ? _dirty && _layers.Count > 0 : d.Dirty && d.Layers != null && d.Layers.Count > 0;
        }

        bool DocHasImage(DocState d)
        {
            return d == _doc ? _hasDoc : d.HasDoc;
        }

        string DocCaption(DocState d)
        {
            if (!DocHasImage(d)) return d.Title;
            float zoom = d == _doc ? _zoom : d.Zoom;
            return string.Format("{0} @ {1:0.#}%", d.Title, zoom * 100);
        }

        void UpdateDocChrome()
        {
            Text = "MicroApp Image Editor - " + _doc.Title;
            if (_docTabs != null) _docTabs.Invalidate();
        }

        /// <summary>Finishes whatever is half done, so the document can go to the back. False mid-drag.</summary>
        bool SettleForSwitch()
        {
            if (_drag != Drag.None) return false;
            CommitInlineEdit();
            CommitTransform();
            _cropRect = null;
            _polyPts = null;
            _lassoPts = null;
            _marqueeDraft = null;
            _zoomRect = null;
            _draft = null;
            _penPts = null;
            _coalesceKey = null;
            ClearHolePreview();
            return true;
        }

        /// <summary>Moves the front document's fields into its DocState.</summary>
        void ParkActive()
        {
            DocState d = _doc;
            d.Layers = new List<EditorLayer>(_layers);
            d.Undo = new List<Snapshot>(_undo);
            d.Redo = new List<Snapshot>(_redo);
            d.Canvas = _canvas;
            d.CanvasBg = _canvasBg;
            d.HasDoc = _hasDoc;
            d.Sel = _sel;
            d.Dirty = _dirty;
            d.NameCounter = _nameCounter;
            d.Selection = _selection;
            d.LastSelection = _lastSelection;
            d.Zoom = _zoom;
            d.Origin = _origin;
            d.ViewFitted = _viewFitted;
            d.LastStrokeEnd = _lastStrokeEnd;
            d.CloneSource = _cloneSource;
            d.Ppi = _ppi;
            _layers.Clear();
            _undo.Clear();
            _redo.Clear();
        }

        /// <summary>Brings a parked document to the front (the caller has parked the old one).</summary>
        void LoadParked(DocState d)
        {
            _doc = d;
            _layers.Clear();
            _undo.Clear();
            _redo.Clear();
            if (d.Layers != null) _layers.AddRange(d.Layers);
            if (d.Undo != null) _undo.AddRange(d.Undo);
            if (d.Redo != null) _redo.AddRange(d.Redo);
            d.Layers = null; d.Undo = null; d.Redo = null;
            _canvas = d.Canvas;
            _canvasBg = d.CanvasBg;
            _hasDoc = d.HasDoc;
            _sel = Math.Min(d.Sel, _layers.Count - 1);
            _dirty = d.Dirty;
            _nameCounter = d.NameCounter;
            _selection = d.Selection;
            _lastSelection = d.LastSelection;
            _zoom = d.Zoom;
            _origin = d.Origin;
            _viewFitted = d.ViewFitted;
            _lastStrokeEnd = d.LastStrokeEnd;
            _cloneSource = d.CloneSource;
            _ppi = d.Ppi;
            d.Selection = d.LastSelection = null;
            ShowActiveDoc();
        }

        /// <summary>Every view refreshes for the document now in front.</summary>
        void ShowActiveDoc()
        {
            _antsScreenPath = null;
            _antsFor = null;
            _compositeDirty = true;
            if (_hasDoc && _viewFitted) FitView();
            AfterDocumentChange();
            RefreshHistoryList();
            UpdateDocChrome();
        }

        void ActivateDoc(DocState d)
        {
            if (d == null || d == _doc || !_docs.Contains(d)) return;
            if (!SettleForSwitch()) return;
            ParkActive();
            LoadParked(d);
            _docTabs.EnsureVisible(d);
        }

        void CycleDoc(int step)
        {
            if (_docs.Count < 2) return;
            int i = _docs.IndexOf(_doc);
            ActivateDoc(_docs[((i + step) % _docs.Count + _docs.Count) % _docs.Count]);
        }

        /// <summary>
        /// Makes room for a new document: the front one is kept as it is, unless it is an
        /// empty tab, which the new document simply takes over. False when a drag is running.
        /// </summary>
        bool StartNewDocTab(string title)
        {
            if (!SettleForSwitch()) return false;
            if (_hasDoc)
            {
                ParkActive();
                var d = new DocState();
                _docs.Insert(_docs.IndexOf(_doc) + 1, d);
                _doc = d;
            }
            else
            {
                var old = new List<Snapshot>(_undo);
                old.AddRange(_redo);
                _undo.Clear();
                _redo.Clear();
                ReleaseDropped(old);
            }
            _doc.Title = string.IsNullOrWhiteSpace(title) ? NextUntitled() : title.Trim();
            _layers.Clear();
            _canvas = Size.Empty;
            _canvasBg = Color.Transparent;
            _hasDoc = false;
            _sel = -1;
            _dirty = false;
            _nameCounter = 0;
            _selection = null;
            _lastSelection = null;
            _zoom = 1f;
            _origin = PointF.Empty;
            _viewFitted = false;
            _lastStrokeEnd = null;
            _cloneSource = null;
            _ppi = 72;
            return true;
        }

        /// <summary>Closes one tab (asks first when it has unsaved work). The last tab closing leaves an empty one.</summary>
        bool CloseDoc(DocState d)
        {
            if (d == null || !_docs.Contains(d)) return false;
            if (d == _doc && !SettleForSwitch()) return false;
            if (DocDirty(d))
            {
                if (d != _doc) ActivateDoc(d);   // show what is about to go
                if (!ModernDialog.Confirm("Close " + d.Title + "?", "Its layers were not saved or copied out.", "Close anyway", "Keep it")) return false;
            }

            var dropped = new List<Snapshot>();
            List<EditorLayer> layers;
            if (d == _doc)
            {
                layers = new List<EditorLayer>(_layers);
                dropped.AddRange(_undo);
                dropped.AddRange(_redo);
                _layers.Clear();
                _undo.Clear();
                _redo.Clear();
            }
            else
            {
                layers = d.Layers ?? new List<EditorLayer>();
                if (d.Undo != null) dropped.AddRange(d.Undo);
                if (d.Redo != null) dropped.AddRange(d.Redo);
                d.Layers = null; d.Undo = null; d.Redo = null;
            }
            dropped.Add(new Snapshot { Name = "closed", Layers = layers.ToArray() });
            foreach (EditorLayer l in layers) l.DropCache();

            int index = _docs.IndexOf(d);
            _docs.Remove(d);
            ReleaseDropped(dropped);   // after the removal: nothing live refers to them any more

            if (d == _doc)
            {
                if (_docs.Count == 0)
                {
                    _doc = new DocState { Title = NextUntitled() };
                    _docs.Add(_doc);
                    _hasDoc = false;   // the closed document's; the fresh tab takes over in place
                    StartNewDocTab(_doc.Title);
                    ShowActiveDoc();
                }
                else LoadParked(_docs[Math.Min(index, _docs.Count - 1)]);
            }
            else UpdateDocChrome();
            GC.Collect();
            return true;
        }

        void CloseOtherDocs(DocState keep)
        {
            foreach (DocState d in _docs.ToArray())
                if (d != keep && !CloseDoc(d)) return;
            ActivateDoc(keep);
        }

        void CloseAllDocs()
        {
            foreach (DocState d in _docs.ToArray())
                if (!CloseDoc(d)) return;
        }

        /// <summary>File &gt; Close: the tab, or the window once there is nothing left to close.</summary>
        void CloseCurrent()
        {
            if (_docs.Count == 1 && !_hasDoc) { Close(); return; }
            CloseDoc(_doc);
        }

        int DirtyDocCount()
        {
            return _docs.Count(DocDirty);
        }

        /// <summary>The bitmaps of the documents in the back, which the memory accounting must never free.</summary>
        void AddParkedBitmaps(HashSet<Bitmap> live)
        {
            foreach (DocState d in _docs)
            {
                if (d == _doc) continue;
                if (d.Layers != null) foreach (EditorLayer l in d.Layers) AddRaster(live, l);
                if (d.Undo != null) foreach (Snapshot s in d.Undo) foreach (EditorLayer l in s.Layers) AddRaster(live, l);
                if (d.Redo != null) foreach (Snapshot s in d.Redo) foreach (EditorLayer l in s.Layers) AddRaster(live, l);
            }
        }

        static void AddRaster(HashSet<Bitmap> live, EditorLayer l)
        {
            var r = l as RasterLayer;
            if (r != null && r.Image != null) live.Add(r.Image);
        }

        /// <summary>For the window closing: every snapshot of the parked documents, which are then emptied.</summary>
        List<Snapshot> DetachParkedDocs()
        {
            var all = new List<Snapshot>();
            foreach (DocState d in _docs)
            {
                if (d == _doc) continue;
                if (d.Undo != null) all.AddRange(d.Undo);
                if (d.Redo != null) all.AddRange(d.Redo);
                if (d.Layers != null) all.Add(new Snapshot { Name = "closing", Layers = d.Layers.ToArray() });
                d.Layers = null; d.Undo = null; d.Redo = null;
            }
            return all;
        }

        void ShowDocTabMenu(DocState d, Point screen)
        {
            ContextMenuStrip menu = NewContextMenu();
            menu.Items.Add(Item("Close", Keys.None, delegate { CloseDoc(d); }));
            var others = Item("Close Others", Keys.None, delegate { CloseOtherDocs(d); });
            others.Enabled = _docs.Count > 1;
            menu.Items.Add(others);
            menu.Items.Add(Item("Close All", Keys.None, delegate { CloseAllDocs(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("New Document…", Keys.None, delegate { NewDocument(); }));
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(screen);
        }

        /// <summary>The Window menu's list of open documents, rebuilt each time it opens.</summary>
        void FillWindowMenu(ToolStripMenuItem window)
        {
            window.DropDownItems.Clear();
            var next = Item("Next Document", Keys.None, delegate { CycleDoc(1); });
            next.ShortcutKeyDisplayString = "Ctrl+Tab";
            next.Enabled = _docs.Count > 1;
            var prev = Item("Previous Document", Keys.None, delegate { CycleDoc(-1); });
            prev.ShortcutKeyDisplayString = "Ctrl+Shift+Tab";
            prev.Enabled = _docs.Count > 1;
            window.DropDownItems.Add(next);
            window.DropDownItems.Add(prev);
            window.DropDownItems.Add(new ToolStripSeparator());
            foreach (DocState d in _docs)
            {
                DocState target = d;
                var it = Item(DocCaption(d) + (DocDirty(d) ? " *" : ""), Keys.None, delegate { ActivateDoc(target); });
                it.Checked = d == _doc;
                window.DropDownItems.Add(it);
            }
        }

        /// <summary>
        /// The row of document tabs above the canvas: click to switch, the × or a middle
        /// click to close, right click for Close Others / Close All, + for a new document.
        /// Tabs that do not fit scroll with the wheel or the arrow buttons.
        /// </summary>
        sealed class DocTabStrip : Control
        {
            readonly ImageEditorForm _ed;
            readonly List<Rectangle> _tabRects = new List<Rectangle>();
            Rectangle _plusRect, _leftRect, _rightRect;
            int _scroll;                 // first visible tab
            int _hover = -1, _hoverClose = -1;
            bool _hoverPlus, _hoverLeft, _hoverRight, _overflow;
            const int TabMax = 240, TabMin = 90, CloseSize = 16;

            public DocTabStrip(ImageEditorForm ed)
            {
                _ed = ed;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                         ControlStyles.ResizeRedraw, true);
                Height = 30;
                Font = Theme.Base;
            }

            Color StripBg { get { return Theme.Dark ? Color.FromArgb(20, 20, 23) : Color.FromArgb(222, 222, 228); } }

            void Measure(Graphics g)
            {
                _tabRects.Clear();
                var docs = _ed._docs;
                int arrows = 0;
                int total = 0;
                var widths = new int[docs.Count];
                for (int i = 0; i < docs.Count; i++)
                {
                    int w = TextRenderer.MeasureText(g, Caption(i), Font).Width + 20 + CloseSize + 10;
                    widths[i] = Math.Max(TabMin, Math.Min(TabMax, w));
                    total += widths[i];
                }
                _overflow = total + 34 > Width;
                if (_overflow) arrows = 44;
                _scroll = Math.Max(0, Math.Min(_scroll, docs.Count - 1));
                int x = 0;
                for (int i = 0; i < docs.Count; i++)
                {
                    if (i < _scroll) { _tabRects.Add(Rectangle.Empty); continue; }
                    _tabRects.Add(new Rectangle(x, 0, widths[i], Height));
                    x += widths[i];
                }
                int limit = Width - arrows - 30;
                int plusX = Math.Min(x, limit) + 2;
                _plusRect = new Rectangle(plusX, 3, 26, Height - 6);
                _leftRect = _overflow ? new Rectangle(Width - 44, 0, 22, Height) : Rectangle.Empty;
                _rightRect = _overflow ? new Rectangle(Width - 22, 0, 22, Height) : Rectangle.Empty;
            }

            string Caption(int i)
            {
                DocState d = _ed._docs[i];
                return _ed.DocCaption(d) + (_ed.DocDirty(d) ? " *" : "");
            }

            Rectangle CloseRect(Rectangle tab)
            {
                return new Rectangle(tab.Right - CloseSize - 8, tab.Y + (tab.Height - CloseSize) / 2, CloseSize, CloseSize);
            }

            public void EnsureVisible(DocState d)
            {
                int i = _ed._docs.IndexOf(d);
                if (i < 0) return;
                if (i < _scroll) _scroll = i;
                else
                {
                    using (Graphics g = CreateGraphics()) Measure(g);
                    int limit = Width - (_overflow ? 44 : 0) - 30;
                    while (_scroll < i && i < _tabRects.Count && _tabRects[i].Right > limit)
                    {
                        _scroll++;
                        using (Graphics g = CreateGraphics()) Measure(g);
                    }
                }
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Measure(g);
                using (var bg = new SolidBrush(StripBg)) g.FillRectangle(bg, ClientRectangle);
                int limit = Width - (_overflow ? 44 : 0) - 30;
                var docs = _ed._docs;
                for (int i = 0; i < docs.Count; i++)
                {
                    Rectangle r = _tabRects[i];
                    if (r.IsEmpty || r.Left >= limit) continue;
                    if (r.Right > limit) r.Width = limit - r.Left;
                    bool active = docs[i] == _ed._doc;
                    Color fill = active ? _ed._canvasPanel.BackColor : i == _hover ? Theme.FieldBg : StripBg;
                    using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
                    if (active)
                        using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, r.X, r.Y, r.Width, 2);
                    using (var sep = new Pen(Theme.Border)) g.DrawLine(sep, r.Right - 1, r.Y + 6, r.Right - 1, r.Bottom - 6);

                    Rectangle close = CloseRect(r);
                    var textRect = new Rectangle(r.X + 12, r.Y, Math.Max(0, close.Left - r.X - 16), r.Height);
                    TextRenderer.DrawText(g, Caption(i), Font, textRect, active ? Theme.Text : Theme.TextDim,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (active || i == _hover)
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        if (i == _hoverClose)
                            using (var hb = new SolidBrush(Theme.Dark ? Color.FromArgb(70, 70, 79) : Color.FromArgb(200, 200, 210)))
                                g.FillEllipse(hb, close);
                        using (var p = new Pen(i == _hoverClose ? Theme.Text : Theme.TextDim, 1.4f))
                        {
                            int m = 5;
                            g.DrawLine(p, close.Left + m, close.Top + m, close.Right - m, close.Bottom - m);
                            g.DrawLine(p, close.Right - m, close.Top + m, close.Left + m, close.Bottom - m);
                        }
                        g.SmoothingMode = SmoothingMode.None;
                    }
                }

                // + : new document
                if (_hoverPlus)
                    using (var hb = new SolidBrush(Theme.FieldBg)) g.FillRectangle(hb, _plusRect);
                using (var p = new Pen(_hoverPlus ? Theme.Text : Theme.TextDim, 1.6f))
                {
                    int cx = _plusRect.X + _plusRect.Width / 2, cy = _plusRect.Y + _plusRect.Height / 2;
                    g.DrawLine(p, cx - 5, cy, cx + 5, cy);
                    g.DrawLine(p, cx, cy - 5, cx, cy + 5);
                }

                if (_overflow)
                {
                    DrawArrow(g, _leftRect, true, _hoverLeft, _scroll > 0);
                    DrawArrow(g, _rightRect, false, _hoverRight, _scroll < docs.Count - 1);
                }
                using (var line = new Pen(Theme.Border)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
            }

            static void DrawArrow(Graphics g, Rectangle r, bool left, bool hover, bool enabled)
            {
                if (hover && enabled) using (var hb = new SolidBrush(Theme.FieldBg)) g.FillRectangle(hb, r);
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                Point[] tri = left
                    ? new[] { new Point(cx + 3, cy - 5), new Point(cx - 3, cy), new Point(cx + 3, cy + 5) }
                    : new[] { new Point(cx - 3, cy - 5), new Point(cx + 3, cy), new Point(cx - 3, cy + 5) };
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = new Pen(enabled ? Theme.Text : Theme.Border, 1.6f)) g.DrawLines(p, tri);
                g.SmoothingMode = SmoothingMode.None;
            }

            int HitTab(Point p)
            {
                int limit = Width - (_overflow ? 44 : 0) - 30;
                if (p.X >= limit && !_plusRect.Contains(p)) return -1;
                for (int i = 0; i < _tabRects.Count; i++)
                    if (!_tabRects[i].IsEmpty && _tabRects[i].Contains(p)) return i;
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int hover = HitTab(e.Location);
                int hoverClose = hover >= 0 && CloseRect(_tabRects[hover]).Contains(e.Location) ? hover : -1;
                bool plus = _plusRect.Contains(e.Location), l = _leftRect.Contains(e.Location), r = _rightRect.Contains(e.Location);
                if (hover != _hover || hoverClose != _hoverClose || plus != _hoverPlus || l != _hoverLeft || r != _hoverRight)
                {
                    _hover = hover; _hoverClose = hoverClose; _hoverPlus = plus; _hoverLeft = l; _hoverRight = r;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = _hoverClose = -1;
                _hoverPlus = _hoverLeft = _hoverRight = false;
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (!_overflow) return;
                _scroll = Math.Max(0, Math.Min(_ed._docs.Count - 1, _scroll + (e.Delta > 0 ? -1 : 1)));
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (_overflow && _leftRect.Contains(e.Location)) { if (_scroll > 0) { _scroll--; Invalidate(); } return; }
                if (_overflow && _rightRect.Contains(e.Location)) { if (_scroll < _ed._docs.Count - 1) { _scroll++; Invalidate(); } return; }
                if (e.Button == MouseButtons.Left && _plusRect.Contains(e.Location)) { _ed.NewDocument(); return; }
                int i = HitTab(e.Location);
                if (i < 0) return;
                DocState d = _ed._docs[i];
                if (e.Button == MouseButtons.Middle) { _ed.CloseDoc(d); return; }
                if (e.Button == MouseButtons.Right) { _ed.ShowDocTabMenu(d, PointToScreen(e.Location)); return; }
                if (e.Button != MouseButtons.Left) return;
                if (CloseRect(_tabRects[i]).Contains(e.Location)) { _ed.CloseDoc(d); return; }
                _ed.ActivateDoc(d);
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                // double-clicking the empty part of the strip starts a new document, as in Photoshop
                if (e.Button == MouseButtons.Left && HitTab(e.Location) < 0 && !_plusRect.Contains(e.Location) &&
                    !_leftRect.Contains(e.Location) && !_rightRect.Contains(e.Location))
                    _ed.NewDocument();
            }
        }
    }
}
