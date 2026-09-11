using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// The image editor: a Photoshop-style window with a two-column tool rail on the left,
    /// an options bar under the menu, the canvas in the middle, and layers / history / the
    /// asset library on the right. It opens from the tray menu or its hot key, starts from a
    /// clipboard paste, a file or a blank canvas, and every mark, text box and pasted image
    /// stays its own movable layer until export.
    ///
    /// The class is split across files: this one holds the document, undo, the view and
    /// the painting; Menus.cs the menu bar and context menus; Tools.cs the mouse machine
    /// for every tool; Transform.cs Free Transform; Panels.cs the right-hand panels.
    /// </summary>
    partial class ImageEditorForm : PixelPerfectForm
    {
        // ---- the document ----------------------------------------------------------
        readonly List<EditorLayer> _layers = new List<EditorLayer>();   // bottom → top
        Size _canvas = Size.Empty;
        Color _canvasBg = Color.Transparent;
        bool _hasDoc;
        int _sel = -1;
        bool _dirty;
        int _nameCounter;
        EditorSelection _selection;        // null: nothing selected
        EditorSelection _lastSelection;    // for Select > Reselect

        // ---- undo ------------------------------------------------------------------
        class Snapshot
        {
            public string Name;
            public EditorLayer[] Layers;
            public Size Canvas;
            public Color CanvasBg;
            public int Sel;
            public EditorSelection Selection;
        }
        readonly List<Snapshot> _undo = new List<Snapshot>();
        readonly List<Snapshot> _redo = new List<Snapshot>();
        string _coalesceKey;
        DateTime _coalesceAt;
        const int UndoLimit = 50;

        // ---- view ------------------------------------------------------------------
        float _zoom = 1f;
        PointF _origin;                    // canvas (0,0) in panel client pixels
        bool _viewFitted;
        bool _showRulers;
        bool _showExtras = true;           // Ctrl+H: selection edges, transform box
        Bitmap _composite;                 // the document at 1:1, rebuilt when dirty
        bool _compositeDirty = true;
        readonly Timer _antsTimer = new Timer { Interval = 90 };
        int _antsPhase;
        GraphicsPath _antsScreenPath;      // Outline transformed to the screen, cached
        EditorSelection _antsFor;
        float _antsZoom;
        PointF _antsOrigin;
        const int RulerSize = 20;

        // ---- colours ---------------------------------------------------------------
        Color _fg = Color.Black;
        Color _bg = Color.White;

        // ---- tools -----------------------------------------------------------------
        Tool _tool = Tool.Move;
        Tool _toolBeforeSpace;
        bool _spaceDown;

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        /// <summary>
        /// Is the space bar physically down right now? The _spaceDown flag alone can stick:
        /// its KeyUp goes elsewhere when Space clicked a focused button that opened a
        /// dialog, or when the window lost activation - and then every drag would pan.
        /// </summary>
        static bool SpaceHeld()
        {
            try { return (GetAsyncKeyState(0x20) & 0x8000) != 0; }
            catch { return false; }
        }

        // shape / text defaults
        Color _stroke = Color.FromArgb(244, 63, 94);
        Color _fill = Color.Transparent;
        float _strokeW = 4;
        int _cornerRadius = 12;
        int _polySides = 6;
        string _fontFamily = "Segoe UI";
        float _fontSize = 32;
        bool _bold, _italic, _underline;
        int _textAlign;
        Color _textColor = Color.FromArgb(244, 63, 94);
        Color _textBack = Color.Transparent;
        Color _textOutline = Color.Transparent;

        // brushes
        int _brushSize = 40;
        int _brushHardness = 60;
        int _brushOpacity = 100;
        int _brushFlow = 100;
        int _blurStrength = 8;
        int _exposure = 30;                // dodge/burn strength %
        int _wandTolerance = 32;
        bool _wandContiguous = true;
        bool _sampleAllLayers = true;
        int _bucketTolerance = 32;
        int _gradientKind;                 // 0 linear, 1 radial
        bool _gradientReverse;
        bool _gradientToTransparent;
        int _marqueeFeather;
        bool _marqueeAntialias = true;
        int _sampleSize;                   // eyedropper: 0 point, 1 3x3, 2 5x5
        bool _autoSelect = true;
        bool _showTransformControls = true;
        int _cropRatio;                    // 0 free, 1 1:1, 2 4:3, 3 16:9, 4 3:2, 5 original
        bool _cropDeletePixels = true;

        // ---- interaction state -----------------------------------------------------
        enum Drag
        {
            None, Pan, Move, Handle, Rotate, Draw, Crop, CropAdjust, Paint, Marquee, Lasso, Gradient, Zoom, FloatMove, SelectionMove
        }
        Drag _drag = Drag.None;
        bool _dragUndoPushed;
        Point _mouseScreen;                // last mouse position, panel coords
        PointF _downCanvas;                // mouse-down, canvas coords
        Point _downScreen;
        PointF _panOrigin0;
        bool _mouseInside;

        RectangleF _bounds0;               // layer geometry at drag start
        Matrix _matrix0;

        ShapeLayer _draft;                 // shape being drawn
        List<PointF> _penPts;              // freehand, canvas coords

        RectangleF? _cropRect;
        int _cropHandle = -1;

        TextLayer _editing;                // inline text edit
        bool _editingWasVisible;

        // ---- ui --------------------------------------------------------------------
        MenuStrip _menu;
        Panel _optionsBar, _rightSide;
        ToolRail _toolRail;
        CanvasPanel _canvasPanel;
        Panel _status;
        Label _statusLeft, _statusRight, _statusColor;
        ModernNumber _zoomBox;
        bool _syncingZoom;
        Label _optToolLbl;
        readonly List<Control> _optionOrder = new List<Control>();
        readonly ToolTip _tips = new ToolTip();
        bool _syncingOptions;
        TextBox _inlineEdit;
        readonly Cursor _rotateCursor;
        readonly Cursor _skewCursor;

        static ImageEditorForm _open;

        /// <summary>Opens the editor (or brings the open one forward). Seeds from the clipboard.</summary>
        public static void Open()
        {
            if (_open != null && !_open.IsDisposed)
            {
                if (_open.WindowState == FormWindowState.Minimized) _open.WindowState = FormWindowState.Normal;
                _open.Activate();
                Native.SetForegroundWindow(_open.Handle);
                return;
            }
            var form = new ImageEditorForm();
            _open = form;
            form.TopMost = true;      // the hot key fires from another app: make sure we surface
            form.Show();
            Native.SetForegroundWindow(form.Handle);
            form.BeginInvoke(new Action(() =>
            {
                form.TopMost = false;
                form.PasteFromClipboard(true, false);   // quiet: a text-only clipboard is not an error
            }));
        }

        ImageEditorForm()
        {
            Theme.Init(ThemeHelper.IsDarkMode);

            Text = "MicroApp Image Editor";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1360, 840);
            MinimumSize = new Size(980, 620);
            BackColor = Theme.Bg;
            KeyPreview = true;
            AllowDrop = true;
            try { Icon = Properties.Resources.AppIcon; } catch { }

            _rotateCursor = MakeCursor("rotate");
            _skewCursor = MakeCursor("skew");

            BuildMenu();
            BuildOptionsBar();
            BuildToolRail();
            BuildRightSide();
            BuildCanvas();
            BuildStatus();

            // docking order decides the layout: menu on top, options under it, status at
            // the bottom, tool rail left, panels right, canvas fills the rest
            Controls.Add(_canvasPanel);
            Controls.Add(_toolRail);
            Controls.Add(_rightSide);
            Controls.Add(_status);
            Controls.Add(_optionsBar);
            Controls.Add(_menu);
            MainMenuStrip = _menu;

            DragEnter += Form_DragEnter;
            DragDrop += Form_DragDrop;
            FormClosing += Form_FormClosing;
            Deactivate += delegate { _spaceDown = false; if (_canvasPanel != null && _drag == Drag.None) _canvasPanel.Cursor = ToolCursor(_tool); };
            FormClosed += delegate
            {
                _antsTimer.Stop();
                if (_open == this) _open = null;
            };
            Shown += delegate
            {
                Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode);
                LayoutRightSide();
                RelayoutOptions();
                RefreshAssetTree();
            };
            _antsTimer.Tick += delegate
            {
                if (_selection != null && !_selection.IsEmpty && _showExtras)
                {
                    _antsPhase = (_antsPhase + 1) % 8;
                    _canvasPanel.Invalidate();
                }
            };
            _antsTimer.Start();

            SelectTool(Tool.Move);
            UpdateStatus();
            RefreshHistoryList();
        }

        // ============================================================== document ops

        EditorLayer SelectedLayer()
        {
            return _sel >= 0 && _sel < _layers.Count ? _layers[_sel] : null;
        }

        RasterLayer SelectedRaster()
        {
            return SelectedLayer() as RasterLayer;
        }

        string NextName(string kind)
        {
            _nameCounter++;
            return kind + " " + _nameCounter;
        }

        bool HasSelection { get { return _selection != null && !_selection.IsEmpty; } }

        void SetSelection(EditorSelection sel)
        {
            if (_selection != null && !_selection.IsEmpty) _lastSelection = _selection;
            _selection = sel != null && sel.IsEmpty ? null : sel;
            _antsScreenPath = null;
            RelayoutOptions();
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        void NewDocument()
        {
            CommitInlineEdit();
            CommitTransform();
            int w = _hasDoc ? _canvas.Width : 1200, h = _hasDoc ? _canvas.Height : 800;
            int background = 0;
            if (!CanvasSizeDialog.Ask(this, "New", ref w, ref h, true, ref background)) return;
            if (_hasDoc && _dirty &&
                !ModernDialog.Confirm("Start over?", "The current layers will be discarded.", "Start new", "Keep working")) return;

            _layers.Clear();
            _undo.Clear();
            _redo.Clear();
            _canvas = new Size(w, h);
            _canvasBg = background == 0 ? Color.White : background == 1 ? Color.Transparent : _bg;
            _hasDoc = true;
            _dirty = false;
            _sel = -1;
            _cropRect = null;
            _selection = null;
            _lastSelection = null;
            // an empty layer to paint on, like a fresh Photoshop document's Background
            var first = new RasterLayer(NewTransparentBitmap(w, h)) { Name = "Layer 1", Bounds = new RectangleF(0, 0, w, h) };
            _layers.Add(first);
            _sel = 0;
            _nameCounter = 1;
            FitView();
            AfterDocumentChange();
            RefreshHistoryList();
        }

        static Bitmap NewTransparentBitmap(int w, int h)
        {
            return new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        }

        void OpenFile()
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.wmf;*.emf|All files|*.*",
                Title = "Open"
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try { AddBitmapLayer(AssetStore.LoadFull(ofd.FileName), Path.GetFileNameWithoutExtension(ofd.FileName)); }
                catch (Exception ex) { ModernDialog.Info("Could not open it", ex.Message); }
            }
        }

        /// <summary>Paste: becomes the document when there is none, a new layer otherwise.</summary>
        void PasteFromClipboard(bool quiet, bool inPlace)
        {
            Bitmap bmp = null;
            PointF? at = null;
            try
            {
                Image img = Clipboard.GetImage();
                if (img != null)
                {
                    // our own copy carries alpha and a position; the DIB Windows hands back does not
                    if (_clipBitmap != null && img.Width == _clipBitmap.Width && img.Height == _clipBitmap.Height)
                    {
                        bmp = new Bitmap(_clipBitmap);
                        if (inPlace) at = _clipPos;
                    }
                    else
                    {
                        bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                    }
                    img.Dispose();
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    foreach (string file in Clipboard.GetFileDropList())
                    {
                        if (AssetStore.IsSupported(file)) { bmp = AssetStore.LoadFull(file); break; }
                    }
                }
            }
            catch (System.Runtime.InteropServices.ExternalException) { }

            if (bmp == null)
            {
                if (!quiet) Toast.Show("No image on the clipboard.");
                return;
            }
            AddBitmapLayer(bmp, NextName("Layer"), at);
        }

        /// <summary>Adds a bitmap as a layer: centred (or at <paramref name="at"/>), scaled down when it would not fit.</summary>
        void AddBitmapLayer(Bitmap bmp, string name, PointF? at = null)
        {
            CommitInlineEdit();
            CommitTransform();
            if (!_hasDoc)
            {
                _canvas = bmp.Size;
                _canvasBg = Color.Transparent;
                _hasDoc = true;
                var first = new RasterLayer(bmp) { Name = name, Bounds = new RectangleF(0, 0, bmp.Width, bmp.Height) };
                _layers.Add(first);
                _sel = 0;
                FitView();
                AfterDocumentChange();
                RefreshHistoryList();
                return;
            }

            PushUndo("Paste");
            float scale = at.HasValue ? 1f : Math.Min(1f, Math.Min((float)_canvas.Width / bmp.Width, (float)_canvas.Height / bmp.Height));
            float w = bmp.Width * scale, h = bmp.Height * scale;
            RectangleF bounds = at.HasValue
                ? new RectangleF(at.Value.X, at.Value.Y, w, h)
                : new RectangleF((_canvas.Width - w) / 2f, (_canvas.Height - h) / 2f, w, h);
            if (!at.HasValue && HasSelection)
            {
                // Photoshop pastes into the middle of the selection
                Rectangle sb = _selection.Bounds;
                bounds = new RectangleF(sb.X + (sb.Width - w) / 2f, sb.Y + (sb.Height - h) / 2f, w, h);
            }
            var layer = new RasterLayer(bmp) { Name = name, Bounds = bounds };
            int insertAt = _sel >= 0 ? _sel + 1 : _layers.Count;
            _layers.Insert(insertAt, layer);
            _sel = insertAt;
            SelectTool(Tool.Move);
            AfterDocumentChange();
        }

        void SaveAs()
        {
            CommitInlineEdit();
            CommitTransform();
            if (!EnsureDoc()) return;
            using (var sfd = new SaveFileDialog
            {
                Filter = "PNG (keeps transparency)|*.png|JPEG|*.jpg|Bitmap|*.bmp|TIFF|*.tif",
                Title = "Save As",
                FileName = "MicroApp " + DateTime.Now.ToString("yyyy-MM-dd HHmmss")
            })
            {
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                    bool opaque = ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
                    using (Bitmap flat = Flattened(opaque))
                    {
                        if (ext == ".jpg" || ext == ".jpeg") SaveJpeg(flat, sfd.FileName, 92);
                        else if (ext == ".bmp") flat.Save(sfd.FileName, ImageFormat.Bmp);
                        else if (ext == ".tif" || ext == ".tiff") flat.Save(sfd.FileName, ImageFormat.Tiff);
                        else flat.Save(sfd.FileName, ImageFormat.Png);
                    }
                    _dirty = false;
                    Toast.Show("Saved.\r\n" + sfd.FileName);
                }
                catch (Exception ex) { ModernDialog.Info("Could not save it", ex.Message); }
            }
        }

        static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            if (codec == null) { bmp.Save(path, ImageFormat.Jpeg); return; }
            using (var p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bmp.Save(path, codec, p);
            }
        }

        /// <summary>Copy Merged: the flattened picture to the clipboard (with a PNG for apps that keep alpha).</summary>
        void CopyResult()
        {
            CommitInlineEdit();
            CommitTransform();
            if (!EnsureDoc()) return;
            try
            {
                using (Bitmap flat = Flattened(false))
                {
                    if (HasSelection)
                    {
                        using (Bitmap cut = CutOut(flat, _selection)) PutOnClipboard(cut, _selection.Bounds.Location);
                    }
                    else PutOnClipboard(flat, Point.Empty);
                }
                Toast.Show("Copied to the clipboard.");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Toast.Show("The clipboard is busy - try again.");
            }
        }

        // the editor's own clipboard: keeps alpha and the original position for Paste in Place
        Bitmap _clipBitmap;
        PointF _clipPos;

        void PutOnClipboard(Bitmap bmp, PointF position)
        {
            if (_clipBitmap != null) _clipBitmap.Dispose();
            _clipBitmap = new Bitmap(bmp);
            _clipPos = position;
            var data = new DataObject();
            using (Bitmap opaque = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(opaque))
                {
                    g.Clear(Color.White);
                    g.DrawImageUnscaled(bmp, 0, 0);
                }
                data.SetData(DataFormats.Bitmap, true, opaque);
                var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                data.SetData("PNG", false, ms);
                Clipboard.SetDataObject(data, true);
            }
        }

        /// <summary>The finished picture. opaque: composed over white for JPG.</summary>
        Bitmap Flattened(bool opaque)
        {
            Color bg = _canvasBg;
            if (opaque && bg.A < 255) bg = Color.White;
            return EditorRender.Flatten(_layers, _canvas, bg);
        }

        /// <summary>The selected part of a canvas-sized bitmap, cropped to the selection's box.</summary>
        static Bitmap CutOut(Bitmap canvasBmp, EditorSelection sel)
        {
            Rectangle b = sel.Bounds;
            if (b.Width < 1 || b.Height < 1) b = new Rectangle(0, 0, 1, 1);
            Pixels src = Pixels.From(canvasBmp);
            var outp = new Pixels(b.Width, b.Height);
            for (int y = 0; y < b.Height; y++)
            {
                int cy = b.Y + y;
                if (cy < 0 || cy >= src.Height) continue;
                for (int x = 0; x < b.Width; x++)
                {
                    int cx = b.X + x;
                    if (cx < 0 || cx >= src.Width) continue;
                    int m = sel.Mask[cy * sel.Width + cx];
                    if (m == 0) continue;
                    int si = src.Index(cx, cy), oi = outp.Index(x, y);
                    outp.Data[oi] = src.Data[si]; outp.Data[oi + 1] = src.Data[si + 1]; outp.Data[oi + 2] = src.Data[si + 2];
                    outp.Data[oi + 3] = (byte)(src.Data[si + 3] * m / 255);
                }
            }
            return outp.ToBitmap();
        }

        bool EnsureDoc()
        {
            if (_hasDoc) return true;
            Toast.Show("Nothing here yet - paste an image or use File > New.");
            return false;
        }

        void Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_dirty && _layers.Count > 0 &&
                !ModernDialog.Confirm("Close the editor?", "The layers were not saved or copied out.", "Close anyway", "Keep editing"))
            {
                e.Cancel = true;
            }
        }

        void Form_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        void Form_DragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (string file in files)
            {
                if (!AssetStore.IsSupported(file)) continue;
                try { AddBitmapLayer(AssetStore.LoadFull(file), Path.GetFileNameWithoutExtension(file)); }
                catch (Exception ex) { ModernDialog.Info("Could not open it", ex.Message); }
            }
        }

        // ===================================================================== undo

        Snapshot TakeSnapshot(string name)
        {
            return new Snapshot
            {
                Name = name,
                Layers = _layers.Select(l => l.Clone()).ToArray(),
                Canvas = _canvas,
                CanvasBg = _canvasBg,
                Sel = _sel,
                Selection = _selection
            };
        }

        void RestoreSnapshot(Snapshot s)
        {
            _layers.Clear();
            _layers.AddRange(s.Layers.Select(l => l.Clone()));
            _canvas = s.Canvas;
            _canvasBg = s.CanvasBg;
            _sel = Math.Min(s.Sel, _layers.Count - 1);
            _selection = s.Selection;
            _antsScreenPath = null;
            _cropRect = null;
            AfterDocumentChange();
        }

        /// <summary>Records the state BEFORE a change, under the name the History panel shows for it.</summary>
        void PushUndo(string name)
        {
            if (!_hasDoc) return;
            _undo.Add(TakeSnapshot(name));
            if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
            _redo.Clear();
            _coalesceKey = null;
            _dirty = true;
            RefreshHistoryList();
        }

        /// <summary>
        /// Undo for rapid-fire tweaks (opacity slider, spinner arrows): the first change of
        /// a burst snapshots, the rest within a second ride on it, so undo steps back over
        /// the whole adjustment instead of one click at a time.
        /// </summary>
        void PushUndoCoalesced(string key, string name)
        {
            if (!_hasDoc) return;
            if (_coalesceKey == key && (DateTime.UtcNow - _coalesceAt).TotalSeconds < 1.2)
            {
                _coalesceAt = DateTime.UtcNow;
                _dirty = true;
                return;
            }
            PushUndo(name);
            _coalesceKey = key;
            _coalesceAt = DateTime.UtcNow;
        }

        void PopUndo()
        {
            if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
            RefreshHistoryList();
        }

        void DoUndo()
        {
            if (_undo.Count == 0) return;
            CancelInlineEdit(false);
            CancelTransform();
            Snapshot s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(TakeSnapshot(s.Name));
            RestoreSnapshot(s);
            _coalesceKey = null;
            RefreshHistoryList();
        }

        void DoRedo()
        {
            if (_redo.Count == 0) return;
            CancelInlineEdit(false);
            CancelTransform();
            Snapshot s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(TakeSnapshot(s.Name));
            RestoreSnapshot(s);
            _coalesceKey = null;
            RefreshHistoryList();
        }

        /// <summary>One call after anything that changed layers/canvas: refresh every view.</summary>
        void AfterDocumentChange()
        {
            foreach (EditorLayer l in _layers) if (l.Bounds.Width < 0) l.Bounds = new RectangleF(l.Bounds.X, l.Bounds.Y, 1, l.Bounds.Height);
            RefreshLayerList();
            SyncOptionsFromSelection();
            RelayoutOptions();
            UpdateStatus();
            InvalidateDoc();
        }

        /// <summary>The pixels changed: rebuild the composite on the next paint.</summary>
        void InvalidateDoc()
        {
            _compositeDirty = true;
            _canvasPanel.Invalidate();
        }

        Bitmap Composite()
        {
            if (!_compositeDirty && _composite != null) return _composite;
            if (_composite != null) _composite.Dispose();
            _composite = EditorRender.Compose(_layers, _canvas, _canvasBg, TransformPreviewOf);
            _compositeDirty = false;
            return _composite;
        }

        // ================================================================= view math

        PointF CanvasToScreen(PointF p)
        {
            return new PointF(_origin.X + p.X * _zoom, _origin.Y + p.Y * _zoom);
        }

        PointF ScreenToCanvas(Point p)
        {
            return new PointF((p.X - _origin.X) / _zoom, (p.Y - _origin.Y) / _zoom);
        }

        Rectangle CanvasScreenRect()
        {
            return new Rectangle((int)Math.Round(_origin.X), (int)Math.Round(_origin.Y),
                                 (int)Math.Round(_canvas.Width * _zoom), (int)Math.Round(_canvas.Height * _zoom));
        }

        Rectangle ViewArea()
        {
            Rectangle r = _canvasPanel.ClientRectangle;
            if (_showRulers) { r.X += RulerSize; r.Y += RulerSize; r.Width -= RulerSize; r.Height -= RulerSize; }
            return r;
        }

        void FitView()
        {
            if (!_hasDoc || _canvas.Width < 1) return;
            Rectangle area = ViewArea();
            float z = Math.Min((area.Width - 48f) / _canvas.Width, (area.Height - 48f) / _canvas.Height);
            _zoom = Math.Max(0.02f, Math.Min(1f, z));
            CenterView();
            _viewFitted = true;
            _antsScreenPath = null;
            UpdateStatus();
        }

        void CenterView()
        {
            Rectangle area = ViewArea();
            _origin = new PointF(area.X + (area.Width - _canvas.Width * _zoom) / 2f,
                                 area.Y + (area.Height - _canvas.Height * _zoom) / 2f);
            _antsScreenPath = null;
        }

        void SetZoom(float z)
        {
            Rectangle area = ViewArea();
            ZoomAt(new Point(area.X + area.Width / 2, area.Y + area.Height / 2), z / _zoom);
        }

        void ZoomAt(Point screenPt, float factor)
        {
            if (!_hasDoc) return;
            float z = Math.Max(0.02f, Math.Min(32f, _zoom * factor));
            if (Math.Abs(z - _zoom) < 0.0001f) return;
            PointF before = ScreenToCanvas(screenPt);
            _zoom = z;
            _origin = new PointF(screenPt.X - before.X * _zoom, screenPt.Y - before.Y * _zoom);
            _viewFitted = false;
            _antsScreenPath = null;
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        void UpdateStatus()
        {
            if (_statusLeft == null) return;
            _statusLeft.Text = _hasDoc
                ? string.Format("{0} × {1} px    {2} layer{3}{4}", _canvas.Width, _canvas.Height,
                                _layers.Count, _layers.Count == 1 ? "" : "s",
                                HasSelection ? "    selection " + _selection.Bounds.Width + " × " + _selection.Bounds.Height : "")
                : "No image yet";
            if (_zoomBox != null && !_zoomBox.ContainsFocus)
            {
                _syncingZoom = true;
                _zoomBox.Value = (decimal)Math.Round(_zoom * 100, 1);
                _syncingZoom = false;
            }
            _statusRight.Text = ToolHint(_tool);
            if (_optToolLbl != null) _optToolLbl.Text = _xf != null ? "Free Transform" : ToolName(_tool);
        }

        static string ToolName(Tool t)
        {
            switch (t)
            {
                case Tool.Move: return "Move";
                case Tool.MarqueeRect: return "Rectangular Marquee";
                case Tool.MarqueeEllipse: return "Elliptical Marquee";
                case Tool.Lasso: return "Lasso";
                case Tool.PolyLasso: return "Polygonal Lasso";
                case Tool.Wand: return "Magic Wand";
                case Tool.Crop: return "Crop";
                case Tool.Eyedropper: return "Eyedropper";
                case Tool.Brush: return "Brush";
                case Tool.Pencil: return "Pencil";
                case Tool.Eraser: return "Eraser";
                case Tool.Clone: return "Clone Stamp";
                case Tool.Bucket: return "Paint Bucket";
                case Tool.Gradient: return "Gradient";
                case Tool.Blur: return "Blur";
                case Tool.Sharpen: return "Sharpen";
                case Tool.Dodge: return "Dodge";
                case Tool.Burn: return "Burn";
                case Tool.Text: return "Type";
                case Tool.ShapeRect: return "Rectangle";
                case Tool.ShapeRoundRect: return "Rounded Rectangle";
                case Tool.ShapeEllipse: return "Ellipse";
                case Tool.ShapePolygon: return "Polygon";
                case Tool.ShapeLine: return "Line";
                case Tool.ShapeArrow: return "Arrow";
                case Tool.Hand: return "Hand";
                case Tool.Zoom: return "Zoom";
            }
            return "";
        }

        string ToolHint(Tool t)
        {
            if (_xf != null) return "drag inside moves · handles scale · outside a corner rotates · Ctrl-drag a corner distorts · Enter commits, Esc cancels";
            switch (t)
            {
                case Tool.Move: return "click a layer to select · drag moves · handles scale · Ctrl+T free transform · right-click for the layer menu";
                case Tool.MarqueeRect:
                case Tool.MarqueeEllipse: return "drag to select · Shift adds, Alt subtracts · Shift while dragging constrains · click empty to deselect";
                case Tool.Lasso: return "drag a freehand outline · Shift adds, Alt subtracts";
                case Tool.PolyLasso: return "click each corner · double-click, Enter or the first point closes · Backspace removes a point · Esc cancels";
                case Tool.Wand: return "click a colour to select it · Shift adds, Alt subtracts · tolerance and contiguous in the options bar";
                case Tool.Crop: return "drag the crop, adjust the handles, Enter applies and Esc cancels";
                case Tool.Eyedropper: return "click to set the foreground colour · Alt-click sets the background";
                case Tool.Brush: return "paint on the image layer · [ and ] change the size · Shift+[ ] hardness · 1-0 opacity · Alt-click picks a colour";
                case Tool.Pencil: return "draw a hard-edged stroke · every stroke is its own layer";
                case Tool.Eraser: return "erase pixels on the image layer · [ and ] change the size";
                case Tool.Clone: return "Alt-click the source, then paint to copy it";
                case Tool.Bucket: return "click to fill a colour area with the foreground colour";
                case Tool.Gradient: return "drag from the start colour to the end colour · fills the selection or the whole layer";
                case Tool.Blur: return "paint to soften · strength in the options bar";
                case Tool.Sharpen: return "paint to sharpen";
                case Tool.Dodge: return "paint to lighten";
                case Tool.Burn: return "paint to darken";
                case Tool.Text: return "click the canvas to place a text box · Ctrl+Enter commits · double-click text to edit it again";
                case Tool.ShapeRect:
                case Tool.ShapeRoundRect:
                case Tool.ShapeEllipse:
                case Tool.ShapePolygon: return "drag to draw · Shift keeps proportions · Alt draws from the centre";
                case Tool.ShapeLine:
                case Tool.ShapeArrow: return "drag from end to end · Shift snaps to 45°";
                case Tool.Hand: return "drag to pan · double-click fits the image";
                case Tool.Zoom: return "click to zoom in · Alt-click zooms out · drag a box to zoom to it";
            }
            return "";
        }

        // ================================================================== painting

        static TextureBrush _checker;

        static TextureBrush Checker()
        {
            if (_checker == null)
            {
                var tile = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(tile))
                {
                    g.Clear(Color.FromArgb(238, 238, 238));
                    using (var b = new SolidBrush(Color.FromArgb(205, 205, 205)))
                    {
                        g.FillRectangle(b, 0, 0, 8, 8);
                        g.FillRectangle(b, 8, 8, 8, 8);
                    }
                }
                _checker = new TextureBrush(tile);
            }
            return _checker;
        }

        void Canvas_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (!_hasDoc)
            {
                PaintEmptyState(g);
                return;
            }

            Rectangle screen = CanvasScreenRect();

            // a soft shadow so the canvas floats above the work area
            for (int i = 4; i >= 1; i--)
            {
                using (var sh = new SolidBrush(Color.FromArgb(14, 0, 0, 0)))
                    g.FillRectangle(sh, screen.X - i, screen.Y - i + 2, screen.Width + i * 2, screen.Height + i * 2);
            }

            // the canvas: checkerboard behind whatever transparency is left
            g.FillRectangle(Checker(), screen);

            Bitmap doc = Composite();
            GraphicsState st = g.Save();
            g.SetClip(screen);
            g.InterpolationMode = _zoom >= 2f ? InterpolationMode.NearestNeighbor
                                : _zoom < 1f ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(doc, new RectangleF(_origin.X, _origin.Y, _canvas.Width * _zoom, _canvas.Height * _zoom),
                        new RectangleF(0, 0, doc.Width, doc.Height), GraphicsUnit.Pixel);

            // the shape being drawn, crisp at any zoom
            if (_draft != null)
            {
                g.TranslateTransform(_origin.X, _origin.Y);
                g.ScaleTransform(_zoom, _zoom);
                EditorRender.Prepare(g);
                _draft.Draw(g);
            }
            g.Restore(st);

            using (var border = new Pen(Theme.Border))
                g.DrawRectangle(border, screen.X, screen.Y, screen.Width, screen.Height);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            PaintMarqueeDraft(g);
            if (_showExtras) PaintSelectionAnts(g);
            PaintBrushCursor(g);
            PaintCropOverlay(g);
            if (_showExtras) PaintTransformControls(g);
            PaintZoomRect(g);
            if (_showRulers) PaintRulers(g);
        }

        void PaintEmptyState(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle client = _canvasPanel.ClientRectangle;
            var zone = new Rectangle(client.Width / 2 - 190, client.Height / 2 - 110, 380, 220);
            using (GraphicsPath path = Theme.Round(zone, 14))
            using (var p = new Pen(Theme.Border, 1.6f) { DashStyle = DashStyle.Dash })
                g.DrawPath(p, path);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                int cx = zone.X + zone.Width / 2, iy = zone.Y + 28;
                using (var p = new Pen(Theme.TextDim, 2f) { LineJoin = LineJoin.Round })
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    using (GraphicsPath frame = Theme.Round(new Rectangle(cx - 28, iy, 56, 44), 7))
                        g.DrawPath(p, frame);
                    g.DrawEllipse(p, cx - 16, iy + 9, 9, 9);
                    g.DrawLines(p, new[]
                    {
                        new PointF(cx - 21, iy + 37), new PointF(cx - 6, iy + 20), new PointF(cx + 4, iy + 31),
                        new PointF(cx + 12, iy + 23), new PointF(cx + 21, iy + 37)
                    });
                }
                using (var strong = new SolidBrush(Theme.Text))
                    g.DrawString("Paste an image to begin", Theme.Strong, strong, new Rectangle(zone.X, zone.Y + 92, zone.Width, 30), sf);
                using (var dim = new SolidBrush(Theme.TextDim))
                    g.DrawString("Ctrl+V pastes the clipboard · drop a file anywhere\r\nor start blank with File > New (Ctrl+N)",
                                 Theme.Small, dim, new Rectangle(zone.X, zone.Y + 124, zone.Width, 60), sf);
            }
        }

        void PaintSelectionAnts(Graphics g)
        {
            if (!HasSelection) return;
            if (_antsScreenPath == null || _antsFor != _selection || _antsZoom != _zoom || _antsOrigin != _origin)
            {
                if (_antsScreenPath != null) _antsScreenPath.Dispose();
                _antsScreenPath = (GraphicsPath)_selection.Outline.Clone();
                using (var m = new Matrix())
                {
                    m.Translate(_origin.X, _origin.Y);
                    m.Scale(_zoom, _zoom);
                    _antsScreenPath.Transform(m);
                }
                _antsFor = _selection;
                _antsZoom = _zoom;
                _antsOrigin = _origin;
            }
            if (_antsScreenPath.PointCount == 0) return;
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.None;
            using (var white = new Pen(Color.White, 1f))
            using (var black = new Pen(Color.Black, 1f) { DashPattern = new[] { 4f, 4f }, DashOffset = _antsPhase })
            {
                g.DrawPath(white, _antsScreenPath);
                g.DrawPath(black, _antsScreenPath);
            }
            g.Restore(st);
        }

        void PaintRulers(Graphics g)
        {
            Rectangle client = _canvasPanel.ClientRectangle;
            using (var bg = new SolidBrush(Theme.Surface))
            {
                g.FillRectangle(bg, 0, 0, client.Width, RulerSize);
                g.FillRectangle(bg, 0, 0, RulerSize, client.Height);
            }
            // tick spacing: the smallest of 10/25/50/100/250/500/1000 px that is ≥ 60 screen px
            int[] steps = { 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };
            int step = steps[steps.Length - 1];
            foreach (int s in steps) if (s * _zoom >= 60) { step = s; break; }
            using (var p = new Pen(Theme.TextDim))
            using (var ink = new SolidBrush(Theme.TextDim))
            using (var f = new Font("Segoe UI", 7f))
            {
                int first = (int)Math.Floor((RulerSize - _origin.X) / _zoom / step) * step;
                for (int v = first; ; v += step)
                {
                    float x = _origin.X + v * _zoom;
                    if (x > client.Width) break;
                    if (x < RulerSize) continue;
                    g.DrawLine(p, x, RulerSize - 7, x, RulerSize);
                    g.DrawString(v.ToString(), f, ink, x + 2, 1);
                    for (int m = 1; m < 5; m++)
                    {
                        float mx = x + m * step / 5f * _zoom;
                        g.DrawLine(p, mx, RulerSize - 3, mx, RulerSize);
                    }
                }
                first = (int)Math.Floor((RulerSize - _origin.Y) / _zoom / step) * step;
                for (int v = first; ; v += step)
                {
                    float y = _origin.Y + v * _zoom;
                    if (y > client.Height) break;
                    if (y < RulerSize) continue;
                    g.DrawLine(p, RulerSize - 7, y, RulerSize, y);
                    GraphicsState st = g.Save();
                    g.TranslateTransform(1, y + 2);
                    g.RotateTransform(90);
                    g.DrawString(v.ToString(), f, ink, 0, -12);
                    g.Restore(st);
                    for (int m = 1; m < 5; m++)
                    {
                        float my = y + m * step / 5f * _zoom;
                        g.DrawLine(p, RulerSize - 3, my, RulerSize, my);
                    }
                }
                g.DrawLine(p, RulerSize, RulerSize - 1, client.Width, RulerSize - 1);
                g.DrawLine(p, RulerSize - 1, RulerSize, RulerSize - 1, client.Height);
                // the cursor's position
                using (var acc = new Pen(Theme.Accent))
                {
                    if (_mouseInside)
                    {
                        g.DrawLine(acc, _mouseScreen.X, 0, _mouseScreen.X, RulerSize - 1);
                        g.DrawLine(acc, 0, _mouseScreen.Y, RulerSize - 1, _mouseScreen.Y);
                    }
                }
            }
            using (var corner = new SolidBrush(Theme.Surface)) g.FillRectangle(corner, 0, 0, RulerSize, RulerSize);
        }

        // ================================================================== cursors

        /// <summary>Small hand-drawn cursors for rotate and skew, since Windows has none.</summary>
        static Cursor MakeCursor(string kind)
        {
            try
            {
                using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var outline = new Pen(Color.White, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        using (var ink = new Pen(Color.Black, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            if (kind == "rotate")
                            {
                                foreach (Pen p in new[] { outline, ink })
                                {
                                    g.DrawArc(p, 8, 8, 16, 16, 200, 250);
                                    g.DrawLine(p, 22, 6, 23.5f, 12.5f);
                                    g.DrawLine(p, 23.5f, 12.5f, 17, 11);
                                }
                            }
                            else
                            {
                                foreach (Pen p in new[] { outline, ink })
                                {
                                    g.DrawLine(p, 6, 12, 26, 12);
                                    g.DrawLine(p, 6, 20, 26, 20);
                                    g.DrawLine(p, 22, 8, 26, 12); g.DrawLine(p, 22, 16, 26, 12);
                                    g.DrawLine(p, 10, 16, 6, 20); g.DrawLine(p, 10, 24, 6, 20);
                                }
                            }
                        }
                    }
                    IntPtr h = bmp.GetHicon();
                    return new Cursor(h);
                }
            }
            catch { return Cursors.Cross; }
        }

        // ================================================================== keyboard

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && !IsTypingContext() && !_spaceDown)
            {
                _spaceDown = true;
                if (_drag == Drag.None) _canvasPanel.Cursor = Cursors.Hand;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                _spaceDown = false;
                if (_drag == Drag.None) _canvasPanel.Cursor = ToolCursor(_tool);
            }
            // a marquee started with Shift/Alt (add/subtract): releasing and pressing the key
            // again during the drag turns it into constrain / from-centre, as in Photoshop
            if (e.KeyCode == Keys.ShiftKey && _drag != Drag.None) _shiftReleasedInDrag = true;
            if (e.KeyCode == Keys.Menu && _drag != Drag.None) _altReleasedInDrag = true;
            base.OnKeyUp(e);
        }

        bool IsTypingContext()
        {
            if (_inlineEdit != null && _inlineEdit.Visible) return true;
            if (_renameBox != null && _renameBox.Visible) return true;
            if (_zoomBox != null && _zoomBox.ContainsFocus) return true;
            Control c = ActiveControl;
            // focus may sit on the inner edit of a number field, or on a combo / slider that wants the arrows
            while (c != null)
            {
                if (c is TextBoxBase || c is ModernNumber || c is ModernCombo || c is ModernSlider) return true;
                var cc = c as ContainerControl;
                c = cc != null ? cc.ActiveControl : null;
            }
            return false;
        }

        static readonly Keys[] TextEditingKeys =
        {
            Keys.Control | Keys.A, Keys.Control | Keys.C, Keys.Control | Keys.X, Keys.Control | Keys.V,
            Keys.Control | Keys.Z, Keys.Control | Keys.Y, Keys.Control | Keys.Shift | Keys.Z,
            Keys.Delete, Keys.Back, Keys.Home, Keys.End, Keys.Left, Keys.Right, Keys.Up, Keys.Down,
            Keys.Shift | Keys.Left, Keys.Shift | Keys.Right, Keys.Shift | Keys.Up, Keys.Shift | Keys.Down,
            Keys.Shift | Keys.Home, Keys.Shift | Keys.End, Keys.Control | Keys.Left, Keys.Control | Keys.Right,
            Keys.Control | Keys.Back, Keys.Control | Keys.Delete, Keys.Tab, Keys.Enter, Keys.Control | Keys.J
        };

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool typing = IsTypingContext();

            if (keyData == Keys.Escape)
            {
                if (_editing != null) { CancelInlineEdit(true); return true; }
                if (_renameBox != null && _renameBox.Visible) { _renameBox.Visible = false; return true; }
                if (_zoomBox != null && _zoomBox.ContainsFocus) { _canvasPanel.Focus(); UpdateStatus(); return true; }
                if (_xf != null) { CancelTransform(); return true; }
                if (_polyPts != null) { _polyPts = null; _canvasPanel.Invalidate(); return true; }
                if (_cropRect.HasValue) { _cropRect = null; RelayoutOptions(); _canvasPanel.Invalidate(); return true; }
                if (_drag != Drag.None) { AbortDrag(); return true; }
                if (HasSelection) { SetSelection(null); return true; }
                if (_sel >= 0 && _tool == Tool.Move) { _sel = -1; RefreshLayerList(); RelayoutOptions(); _canvasPanel.Invalidate(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (typing)
            {
                // hand the standard editing combos to the text box instead of the menu
                if (Array.IndexOf(TextEditingKeys, keyData) >= 0) return false;
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == Keys.F2 && _assetTree.ContainsFocus) { RenameAssetFolder(); return true; }

            switch (keyData)
            {
                case Keys.Enter:
                    if (_xf != null) { CommitTransform(); return true; }
                    if (_polyPts != null) { ClosePolyLasso(); return true; }
                    if (_tool == Tool.Crop && _cropRect.HasValue) { ApplyCrop(); return true; }
                    break;
                case Keys.Delete:
                case Keys.Back:
                    if (_assetList.ContainsFocus || _assetTree.ContainsFocus) break;
                    if (_polyPts != null && keyData == Keys.Back) { if (_polyPts.Count > 0) _polyPts.RemoveAt(_polyPts.Count - 1); _canvasPanel.Invalidate(); return true; }
                    if (HasSelection && SelectedRaster() != null) { ClearSelection(); return true; }
                    if (_sel >= 0) { DeleteLayer(); return true; }
                    break;

                // ---- Photoshop tool keys; Shift cycles the tools that share a slot ----
                case Keys.V: SelectTool(Tool.Move); return true;
                case Keys.M: CycleTool(Tool.MarqueeRect, false); return true;
                case Keys.Shift | Keys.M: CycleTool(Tool.MarqueeRect, true); return true;
                case Keys.L: CycleTool(Tool.Lasso, false); return true;
                case Keys.Shift | Keys.L: CycleTool(Tool.Lasso, true); return true;
                case Keys.W: SelectTool(Tool.Wand); return true;
                case Keys.C: SelectTool(Tool.Crop); return true;
                case Keys.I: SelectTool(Tool.Eyedropper); return true;
                case Keys.B: CycleTool(Tool.Brush, false); return true;
                case Keys.Shift | Keys.B: CycleTool(Tool.Brush, true); return true;
                case Keys.E: SelectTool(Tool.Eraser); return true;
                case Keys.S: SelectTool(Tool.Clone); return true;
                case Keys.G: CycleTool(Tool.Gradient, false); return true;
                case Keys.Shift | Keys.G: CycleTool(Tool.Gradient, true); return true;
                case Keys.R: CycleTool(Tool.Blur, false); return true;
                case Keys.Shift | Keys.R: CycleTool(Tool.Blur, true); return true;
                case Keys.O: CycleTool(Tool.Dodge, false); return true;
                case Keys.Shift | Keys.O: CycleTool(Tool.Dodge, true); return true;
                case Keys.T: SelectTool(Tool.Text); return true;
                case Keys.U: CycleTool(Tool.ShapeRect, false); return true;
                case Keys.Shift | Keys.U: CycleTool(Tool.ShapeRect, true); return true;
                case Keys.H: SelectTool(Tool.Hand); return true;
                case Keys.Z: SelectTool(Tool.Zoom); return true;
                case Keys.D: _fg = Color.Black; _bg = Color.White; _toolRail.Invalidate(); return true;
                case Keys.X: { Color t = _fg; _fg = _bg; _bg = t; _toolRail.Invalidate(); return true; }
                case Keys.Control | Keys.Y: DoRedo(); return true;

                // ---- brush size / hardness ----
                case Keys.OemOpenBrackets: NudgeBrush(-1, false); return true;
                case Keys.OemCloseBrackets: NudgeBrush(1, false); return true;
                case Keys.Shift | Keys.OemOpenBrackets: NudgeBrush(-1, true); return true;
                case Keys.Shift | Keys.OemCloseBrackets: NudgeBrush(1, true); return true;

                // ---- view ----
                case Keys.Control | Keys.D0: FitView(); _canvasPanel.Invalidate(); return true;
                case Keys.Control | Keys.D1: SetZoom(1f); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add:
                case Keys.Oemplus:
                case Keys.Add:
                    SetZoom(_zoom * 1.25f); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract:
                case Keys.OemMinus:
                case Keys.Subtract:
                    SetZoom(_zoom / 1.25f); return true;
            }

            // 1..0 set the brush (or layer) opacity, as in Photoshop
            if (keyData >= Keys.D0 && keyData <= Keys.D9)
            {
                int pct = keyData == Keys.D0 ? 100 : (int)(keyData - Keys.D0) * 10;
                if (IsBrushTool(_tool)) { _brushOpacity = pct; SyncBrushOptions(); Toast.Show("Opacity " + pct + "%"); }
                else if (SelectedLayer() != null) { PushUndoCoalesced("opacity", "Layer Opacity"); SelectedLayer().Opacity = pct; AfterDocumentChange(); }
                return true;
            }

            // arrows nudge the selected layer (or the selection outline with a marquee tool)
            {
                int dx = 0, dy = 0;
                Keys bare = keyData & ~Keys.Shift;
                if (bare == Keys.Left) dx = -1;
                else if (bare == Keys.Right) dx = 1;
                else if (bare == Keys.Up) dy = -1;
                else if (bare == Keys.Down) dy = 1;
                if (dx != 0 || dy != 0)
                {
                    int step = (keyData & Keys.Shift) == Keys.Shift ? 10 : 1;
                    if (IsSelectionTool(_tool) && HasSelection)
                    {
                        SetSelection(_selection.Offset(dx * step, dy * step));
                        return true;
                    }
                    if (_sel >= 0 && !_layers[_sel].Locked)
                    {
                        PushUndoCoalesced("nudge", "Nudge");
                        RectangleF b = _layers[_sel].Bounds;
                        b.Offset(dx * step, dy * step);
                        _layers[_sel].Bounds = b;
                        InvalidateDoc();
                        return true;
                    }
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static bool IsBrushTool(Tool t)
        {
            return t == Tool.Brush || t == Tool.Eraser || t == Tool.Clone || t == Tool.Blur || t == Tool.Sharpen || t == Tool.Dodge || t == Tool.Burn;
        }

        static bool IsSelectionTool(Tool t)
        {
            return t == Tool.MarqueeRect || t == Tool.MarqueeEllipse || t == Tool.Lasso || t == Tool.PolyLasso || t == Tool.Wand;
        }

        static bool IsShapeTool(Tool t)
        {
            return t == Tool.ShapeRect || t == Tool.ShapeRoundRect || t == Tool.ShapeEllipse || t == Tool.ShapePolygon ||
                   t == Tool.ShapeLine || t == Tool.ShapeArrow || t == Tool.Pencil;
        }

        void NudgeBrush(int dir, bool hardness)
        {
            if (hardness)
            {
                _brushHardness = Math.Max(0, Math.Min(100, _brushHardness + dir * 25));
                Toast.Show("Hardness " + _brushHardness + "%");
            }
            else
            {
                int step = _brushSize < 10 ? 1 : _brushSize < 50 ? 5 : _brushSize < 200 ? 10 : 50;
                _brushSize = Math.Max(1, Math.Min(2000, _brushSize + dir * step));
            }
            SyncBrushOptions();
            _canvasPanel.Invalidate();
        }

        // ================================================================= tools etc

        static readonly Tool[][] ToolGroups =
        {
            new[] { Tool.Move },
            new[] { Tool.MarqueeRect, Tool.MarqueeEllipse },
            new[] { Tool.Lasso, Tool.PolyLasso },
            new[] { Tool.Wand },
            new[] { Tool.Crop },
            new[] { Tool.Eyedropper },
            new[] { Tool.Brush, Tool.Pencil },
            new[] { Tool.Eraser },
            new[] { Tool.Clone },
            new[] { Tool.Gradient, Tool.Bucket },
            new[] { Tool.Blur, Tool.Sharpen },
            new[] { Tool.Dodge, Tool.Burn },
            new[] { Tool.Text },
            new[] { Tool.ShapeRect, Tool.ShapeRoundRect, Tool.ShapeEllipse, Tool.ShapePolygon, Tool.ShapeLine, Tool.ShapeArrow },
            new[] { Tool.Hand },
            new[] { Tool.Zoom }
        };

        static Tool[] GroupOf(Tool t)
        {
            foreach (Tool[] g in ToolGroups) if (Array.IndexOf(g, t) >= 0) return g;
            return new[] { t };
        }

        /// <summary>The key of a group: picks the group's current tool; with Shift, the next one in the group.</summary>
        void CycleTool(Tool groupHead, bool next)
        {
            Tool[] group = GroupOf(groupHead);
            int cur = Array.IndexOf(group, _tool);
            if (cur < 0)
            {
                Tool remembered;
                SelectTool(_groupChoice.TryGetValue(group[0], out remembered) ? remembered : group[0]);
                return;
            }
            if (!next) return;
            SelectTool(group[(cur + 1) % group.Length]);
        }

        readonly Dictionary<Tool, Tool> _groupChoice = new Dictionary<Tool, Tool>();

        void SelectTool(Tool tool)
        {
            if (_tool != tool)
            {
                CommitInlineEdit();
                CommitTransform();
                if (_polyPts != null) { _polyPts = null; }
                if (_drag != Drag.None) AbortDrag();
            }
            _tool = tool;
            _groupChoice[GroupOf(tool)[0]] = tool;
            if (tool != Tool.Crop) _cropRect = null;
            if (_toolRail != null) _toolRail.Invalidate();
            UpdateStatus();
            RelayoutOptions();
            _canvasPanel.Cursor = ToolCursor(tool);
            _canvasPanel.Invalidate();
        }

        Cursor ToolCursor(Tool tool)
        {
            switch (tool)
            {
                case Tool.Move: return Cursors.Default;
                case Tool.Hand: return Cursors.Hand;
                case Tool.Zoom: return Cursors.Cross;
                case Tool.Text: return Cursors.IBeam;
                case Tool.Brush:
                case Tool.Eraser:
                case Tool.Clone:
                case Tool.Blur:
                case Tool.Sharpen:
                case Tool.Dodge:
                case Tool.Burn:
                    return _brushSize * _zoom >= 6 ? BlankCursor() : Cursors.Cross;   // the ring is drawn instead
                default: return Cursors.Cross;
            }
        }

        static Cursor _blank;
        static Cursor BlankCursor()
        {
            if (_blank == null)
            {
                try
                {
                    using (var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.FillRectangle(Brushes.White, 3, 0, 2, 8);
                            g.FillRectangle(Brushes.White, 0, 3, 8, 2);
                            g.FillRectangle(Brushes.Black, 3, 1, 2, 6);
                            g.FillRectangle(Brushes.Black, 1, 3, 6, 2);
                        }
                        _blank = new Cursor(bmp.GetHicon());
                    }
                }
                catch { _blank = Cursors.Cross; }
            }
            return _blank;
        }

        // =============================================================== crop

        void ApplyCrop()
        {
            if (!_cropRect.HasValue) return;
            Rectangle r = Rectangle.Round(_cropRect.Value);
            if (_cropDeletePixels) r.Intersect(new Rectangle(0, 0, _canvas.Width, _canvas.Height));
            if (r.Width < 1 || r.Height < 1) { _cropRect = null; _canvasPanel.Invalidate(); return; }
            CropTo(r, "Crop");
            SelectTool(Tool.Move);
        }

        /// <summary>Cuts the canvas down to <paramref name="r"/>: layers shift, the selection follows.</summary>
        void CropTo(Rectangle r, string undoName)
        {
            PushUndo(undoName);
            _canvas = r.Size;
            foreach (EditorLayer layer in _layers)
            {
                RectangleF b = layer.Bounds;
                b.Offset(-r.X, -r.Y);
                layer.Bounds = b;
            }
            if (_selection != null) _selection = _selection.Rebase(r.Size, r.X, r.Y);
            _antsScreenPath = null;
            _cropRect = null;
            FitView();
            AfterDocumentChange();
        }

        /// <summary>Image &gt; Crop: to the selection's box.</summary>
        void CropToSelection()
        {
            if (!EnsureDoc()) return;
            if (!HasSelection) { Toast.Show("Make a selection first."); return; }
            Rectangle r = _selection.Bounds;
            r.Intersect(new Rectangle(0, 0, _canvas.Width, _canvas.Height));
            if (r.Width < 1 || r.Height < 1) return;
            CropTo(r, "Crop");
            SetSelection(null);
        }

        /// <summary>Image &gt; Trim: drops the transparent border around the picture.</summary>
        void TrimCanvas()
        {
            if (!EnsureDoc()) return;
            Pixels p = Pixels.From(Composite());
            int minX = p.Width, minY = p.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < p.Height; y++)
                for (int x = 0; x < p.Width; x++)
                {
                    if (p.Data[p.Index(x, y) + 3] == 0) continue;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            if (maxX < 0) { Toast.Show("The image is empty."); return; }
            var r = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            if (r.Width == _canvas.Width && r.Height == _canvas.Height) { Toast.Show("Nothing to trim."); return; }
            CropTo(r, "Trim");
        }

        /// <summary>Image &gt; Reveal All: grows the canvas to hold every layer.</summary>
        void RevealAll()
        {
            if (!EnsureDoc()) return;
            RectangleF all = new RectangleF(0, 0, _canvas.Width, _canvas.Height);
            foreach (EditorLayer l in _layers) all = RectangleF.Union(all, l.CanvasBox());
            var r = Rectangle.Round(all);
            if (r.Width == _canvas.Width && r.Height == _canvas.Height) return;
            CropTo(r, "Reveal All");
        }

        void ResizeDocument()
        {
            CommitInlineEdit();
            CommitTransform();
            if (!EnsureDoc()) return;
            int w = _canvas.Width, h = _canvas.Height;
            bool scaleLayers = true;
            if (!CanvasSizeDialog.AskResize(this, ref w, ref h, _canvas, ref scaleLayers)) return;
            if (w == _canvas.Width && h == _canvas.Height) return;

            PushUndo("Image Size");
            float fx = (float)w / _canvas.Width, fy = (float)h / _canvas.Height;
            _canvas = new Size(w, h);
            if (scaleLayers)
            {
                float f = (fx + fy) / 2f;
                foreach (EditorLayer layer in _layers)
                {
                    RectangleF b = layer.Bounds;
                    layer.Bounds = new RectangleF(b.X * fx, b.Y * fy, b.Width * fx, b.Height * fy);
                    var text = layer as TextLayer;
                    if (text != null) text.FontSize = Math.Max(4f, text.FontSize * f);
                    var shape = layer as ShapeLayer;
                    if (shape != null) shape.StrokeWidth = Math.Max(1f, shape.StrokeWidth * f);
                }
            }
            _selection = null;
            FitView();
            AfterDocumentChange();
        }

        void CanvasSize()
        {
            CommitInlineEdit();
            CommitTransform();
            if (!EnsureDoc()) return;
            Size size; Point offset;
            if (!CanvasExtendDialog.Ask(this, _canvas, out size, out offset)) return;
            if (size == _canvas) return;
            PushUndo("Canvas Size");
            _canvas = size;
            foreach (EditorLayer layer in _layers)
            {
                RectangleF b = layer.Bounds;
                b.Offset(offset.X, offset.Y);
                layer.Bounds = b;
            }
            if (_selection != null) _selection = _selection.Rebase(size, -offset.X, -offset.Y);
            _antsScreenPath = null;
            FitView();
            AfterDocumentChange();
        }

        void RotateCanvas(int degrees)
        {
            if (!EnsureDoc()) return;
            CommitTransform();
            PushUndo("Image Rotation");
            Size old = _canvas;
            if (degrees == 180)
            {
                foreach (EditorLayer layer in _layers)
                {
                    PointF c = layer.Center;
                    var nc = new PointF(old.Width - c.X, old.Height - c.Y);
                    layer.RotationDeg = Normalise(layer.RotationDeg + 180);
                    layer.Bounds = new RectangleF(nc.X - layer.Bounds.Width / 2f, nc.Y - layer.Bounds.Height / 2f, layer.Bounds.Width, layer.Bounds.Height);
                }
            }
            else
            {
                bool clockwise = degrees == 90;
                _canvas = new Size(old.Height, old.Width);
                foreach (EditorLayer layer in _layers)
                {
                    PointF c = layer.Center;
                    PointF nc = clockwise ? new PointF(old.Height - c.Y, c.X) : new PointF(c.Y, old.Width - c.X);
                    layer.RotationDeg = Normalise(layer.RotationDeg + (clockwise ? 90 : -90));
                    layer.Bounds = new RectangleF(nc.X - layer.Bounds.Width / 2f, nc.Y - layer.Bounds.Height / 2f, layer.Bounds.Width, layer.Bounds.Height);
                }
            }
            _selection = null;
            FitView();
            AfterDocumentChange();
        }

        void FlipCanvas(bool horizontal)
        {
            if (!EnsureDoc()) return;
            CommitTransform();
            PushUndo(horizontal ? "Flip Canvas Horizontal" : "Flip Canvas Vertical");
            foreach (EditorLayer layer in _layers)
            {
                PointF c = layer.Center;
                PointF nc = horizontal ? new PointF(_canvas.Width - c.X, c.Y) : new PointF(c.X, _canvas.Height - c.Y);
                if (horizontal) layer.FlipH = !layer.FlipH; else layer.FlipV = !layer.FlipV;
                layer.RotationDeg = Normalise(-layer.RotationDeg);
                layer.ShearX = -layer.ShearX;
                layer.ShearY = -layer.ShearY;
                layer.Bounds = new RectangleF(nc.X - layer.Bounds.Width / 2f, nc.Y - layer.Bounds.Height / 2f, layer.Bounds.Width, layer.Bounds.Height);
            }
            _selection = null;
            AfterDocumentChange();
        }

        static float Normalise(float deg)
        {
            deg = deg % 360f;
            if (deg > 180f) deg -= 360f;
            if (deg < -180f) deg += 360f;
            return deg;
        }

        static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static PointF TransformPoint(Matrix m, PointF p)
        {
            PointF[] pts = { p };
            m.TransformPoints(pts);
            return pts[0];
        }

        PointF Clamp(PointF p)
        {
            return new PointF(Math.Max(0, Math.Min(_canvas.Width, p.X)), Math.Max(0, Math.Min(_canvas.Height, p.Y)));
        }
    }

    /// <summary>The drawing surface: double-buffered and focusable so it can take the keys.</summary>
    class CanvasPanel : Panel
    {
        public CanvasPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // arrows and Enter are ours (nudging, committing), not focus navigation
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Enter || k == Keys.Tab) return true;
            return base.IsInputKey(keyData);
        }
    }

    /// <summary>
    /// A colour well for the options bar. Left-click opens the picker; right-click (when
    /// clearing is allowed) sets "none", drawn as a checkerboard with a red slash.
    /// </summary>
    class SwatchButton : Button
    {
        readonly bool _allowClear;
        Color _color = Color.Red;

        public event EventHandler ColorChanged;

        public SwatchButton(bool allowClear)
        {
            _allowClear = allowClear;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderColor = Theme.Border;
            BackColor = Theme.Surface;
            TabStop = false;
        }

        public Color Color
        {
            get { return _color; }
            set { _color = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle r = new Rectangle(5, 5, Width - 11, Height - 11);
            Graphics g = e.Graphics;
            if (_color.A < 255)
            {
                using (var light = new SolidBrush(System.Drawing.Color.White))
                using (var dark = new SolidBrush(System.Drawing.Color.FromArgb(200, 200, 200)))
                {
                    g.FillRectangle(light, r);
                    for (int y = 0; y < r.Height; y += 5)
                        for (int x = y / 5 % 2 == 0 ? 0 : 5; x < r.Width; x += 10)
                            g.FillRectangle(dark, r.X + x, r.Y + y, Math.Min(5, r.Width - x), Math.Min(5, r.Height - y));
                }
            }
            if (_color.A > 0)
                using (var b = new SolidBrush(_color)) g.FillRectangle(b, r);
            else
                using (var p = new Pen(System.Drawing.Color.Red, 1.6f)) g.DrawLine(p, r.Left, r.Bottom, r.Right, r.Top);
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, r);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right && _allowClear)
            {
                Color = System.Drawing.Color.Transparent;
                if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            using (var dialog = new ColorDialog { Color = _color.A > 0 ? _color : System.Drawing.Color.White, FullOpen = true })
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    Color = dialog.Color;
                    if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
                }
            }
        }
    }
}
