using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>The mouse machine: what every tool does on press, drag and release.</summary>
    partial class ImageEditorForm
    {
        // selection tools
        SelectionMode _selMode;
        bool _shiftReleasedInDrag, _altReleasedInDrag;
        RectangleF? _marqueeDraft;         // canvas coords
        List<PointF> _lassoPts;            // freehand lasso, canvas coords
        List<PointF> _polyPts;             // polygonal lasso vertices, canvas coords
        Rectangle? _zoomRect;              // screen coords
        PointF _gradientEnd;

        // floating pixels (Move tool dragging a selection)
        RasterLayer _floatLayer, _floatHost;
        EditorSelection _floatSel0;
        int _floatHostIndex;
        // while a piece floats, its host is painted with the hole where the piece came from -
        // a preview only; the host's own pixels change at the apply
        RasterLayer _holeHost;
        Bitmap _holePreview;

        // painting
        RasterLayer _paintLayer;
        Bitmap _paintOriginal;             // the bitmap the layer had before the stroke (owned by undo)
        Pixels _paintOrig, _paintWork, _paintFiltered;
        Bitmap _paintBitmap;
        byte[] _strokeMask, _paintSelMask;
        PointF _lastDab;
        float _dabRadius, _dabSpacing, _dabCarry;
        Tool _strokeTool;
        Point? _cloneSource;               // in the layer's pixels
        Point _cloneOffset;
        bool _cloneOffsetSet;

        // text
        string _editTextBefore;
        bool _editingIsNew;

        void BuildCanvas()
        {
            _canvasPanel = new CanvasPanel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeHelper.IsDarkMode ? Color.FromArgb(30, 30, 34) : Color.FromArgb(200, 200, 205)
            };
            _canvasPanel.Paint += Canvas_Paint;
            _canvasPanel.MouseDown += Canvas_MouseDown;
            _canvasPanel.MouseMove += Canvas_MouseMove;
            _canvasPanel.MouseUp += Canvas_MouseUp;
            _canvasPanel.MouseWheel += Canvas_MouseWheel;
            _canvasPanel.MouseDoubleClick += Canvas_MouseDoubleClick;
            _canvasPanel.MouseEnter += delegate
            {
                _mouseInside = true;
                if (ActiveControl == null || !(ActiveControl is TextBoxBase)) _canvasPanel.Focus();
            };
            _canvasPanel.MouseLeave += delegate { _mouseInside = false; _canvasPanel.Invalidate(); };
            _canvasPanel.Resize += delegate { if (_viewFitted) FitView(); _antsScreenPath = null; _canvasPanel.Invalidate(); };
        }

        void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            ZoomAt(e.Location, e.Delta > 0 ? 1.15f : 1f / 1.15f);
        }

        void Canvas_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_hasDoc) return;
            if (_tool == Tool.Hand) { FitView(); _canvasPanel.Invalidate(); return; }
            if (_xf != null && _xf.HitInside(e.Location)) { CommitTransform(); return; }
            if (_tool == Tool.PolyLasso && _polyPts != null) { ClosePolyLasso(); return; }
        }

        // ============================================================== mouse down

        void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            _canvasPanel.Focus();
            bool wasEditingText = _editing != null;
            CommitInlineEdit();
            _mouseScreen = e.Location;
            _downScreen = e.Location;
            _dragUndoPushed = false;
            _shiftReleasedInDrag = _altReleasedInDrag = false;
            if (!_hasDoc) return;
            PointF cp = ScreenToCanvas(e.Location);
            _downCanvas = cp;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            bool alt = (ModifierKeys & Keys.Alt) == Keys.Alt;
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;

            if (_spaceDown && !SpaceHeld()) { _spaceDown = false; _canvasPanel.Cursor = ToolCursor(_tool); }
            if (e.Button == MouseButtons.Middle || (_spaceDown && SpaceHeld() && e.Button == MouseButtons.Left) || (_tool == Tool.Hand && e.Button == MouseButtons.Left))
            {
                _drag = Drag.Pan;
                _panOrigin0 = _origin;
                _canvasPanel.Cursor = Cursors.SizeAll;
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                ShowCanvasContextMenu(e.Location, cp);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            // a transform in progress owns the mouse until it is committed
            if (_xf != null)
            {
                if (TransformMouseDown(e.Location, cp)) return;
                CommitTransform();
            }

            switch (_tool)
            {
                case Tool.Move:
                    MoveToolDown(e, cp, shift, alt, ctrl);
                    break;

                case Tool.MarqueeRect:
                case Tool.MarqueeEllipse:
                    _selMode = ModeFromModifiers(shift, alt);
                    _drag = Drag.Marquee;
                    _marqueeDraft = new RectangleF(cp, SizeF.Empty);
                    break;

                case Tool.Lasso:
                    _selMode = ModeFromModifiers(shift, alt);
                    _drag = Drag.Lasso;
                    _lassoPts = new List<PointF> { cp };
                    break;

                case Tool.PolyLasso:
                    if (_polyPts == null)
                    {
                        _selMode = ModeFromModifiers(shift, alt);
                        _polyPts = new List<PointF> { cp };
                    }
                    else
                    {
                        PointF first = CanvasToScreen(_polyPts[0]);
                        if (_polyPts.Count >= 3 && Dist(first, e.Location) < 8) { ClosePolyLasso(); break; }
                        _polyPts.Add(cp);
                    }
                    _canvasPanel.Invalidate();
                    break;

                case Tool.Wand:
                    WandClick(cp, ModeFromModifiers(shift, alt));
                    break;

                case Tool.Crop:
                    CropToolDown(e.Location, cp);
                    break;

                case Tool.Eyedropper:
                    Eyedrop(cp, alt);
                    break;

                case Tool.Brush:
                case Tool.Eraser:
                case Tool.Clone:
                case Tool.Blur:
                case Tool.Sharpen:
                case Tool.Dodge:
                case Tool.Burn:
                    if (alt && _tool == Tool.Brush) { Eyedrop(cp, false); break; }
                    if (alt && _tool == Tool.Clone) { SetCloneSource(cp); break; }
                    BeginStroke(cp);
                    break;

                case Tool.Pencil:
                    _drag = Drag.Draw;
                    _penPts = new List<PointF> { cp };
                    _draft = null;
                    break;

                case Tool.ShapeRect:
                case Tool.ShapeRoundRect:
                case Tool.ShapeEllipse:
                case Tool.ShapePolygon:
                case Tool.ShapeLine:
                case Tool.ShapeArrow:
                    _drag = Drag.Draw;
                    _draft = null;
                    break;

                case Tool.Bucket:
                    BucketClick(cp);
                    break;

                case Tool.Gradient:
                    _drag = Drag.Gradient;
                    _gradientEnd = cp;
                    break;

                case Tool.Text:
                    if (wasEditingText) break;
                    {
                        // clicking existing text edits it, the way the Type tool does
                        TextLayer under = null;
                        for (int i = _layers.Count - 1; i >= 0; i--)
                        {
                            var t = _layers[i] as TextLayer;
                            if (t != null && t.Visible && t.HitTest(cp)) { under = t; _sel = i; break; }
                        }
                        if (under != null) { RefreshLayerList(); PushUndo("Edit Type"); BeginInlineEdit(under, false); }
                        else PlaceTextLayer(cp);
                    }
                    break;

                case Tool.Zoom:
                    _drag = Drag.Zoom;
                    _zoomRect = new Rectangle(e.Location, Size.Empty);
                    break;
            }
        }

        static SelectionMode ModeFromModifiers(bool shift, bool alt)
        {
            if (shift && alt) return SelectionMode.Intersect;
            if (shift) return SelectionMode.Add;
            if (alt) return SelectionMode.Subtract;
            return SelectionMode.New;
        }

        void MoveToolDown(MouseEventArgs e, PointF cp, bool shift, bool alt, bool ctrl)
        {
            EditorLayer sel = SelectedLayer();
            if (sel != null && _showTransformControls && !sel.Locked && sel.Visible)
            {
                int h = HitHandle(sel, e.Location);
                if (h >= 0 || HitRotateZone(sel, e.Location))
                {
                    BeginTransform(sel, TransformMode.Free);
                    TransformMouseDown(e.Location, cp);
                    return;
                }
            }

            // with a selection on an image layer, the Move tool moves the selected pixels -
            // wherever on the canvas the drag starts, as in Photoshop
            var selRaster = sel as RasterLayer;
            if (selRaster != null && HasSelection && !sel.Locked && sel.Visible && !alt)
            {
                BeginFloatMove(selRaster, _sel, cp);
                return;
            }

            // pick the topmost layer under the cursor (Auto-Select), else keep the current one
            int hit = -1;
            if (_autoSelect || ctrl || sel == null || !sel.HitTest(cp))
            {
                for (int i = _layers.Count - 1; i >= 0; i--)
                    if (_layers[i].Visible && !_layers[i].Floating && _layers[i].HitTest(cp)) { hit = i; break; }
            }
            else hit = _sel;

            if (hit >= 0 && alt)
            {
                // Alt-drag: move a copy
                _sel = hit;
                DuplicateLayer(false);
                hit = _sel;
            }
            _sel = hit;
            RefreshLayerList();
            SyncOptionsFromSelection();
            RelayoutOptions();
            if (hit < 0) { _canvasPanel.Invalidate(); return; }

            var hitText = _layers[hit] as TextLayer;
            if (e.Clicks == 2 && hitText != null)
            {
                PushUndo("Edit Type");
                BeginInlineEdit(hitText, false);
                return;
            }
            if (_layers[hit].Locked) { Toast.Show("The layer is locked."); _canvasPanel.Invalidate(); return; }

            var raster = _layers[hit] as RasterLayer;
            if (raster != null && HasSelection && !raster.Locked)
            {
                BeginFloatMove(raster, hit, cp);
                return;
            }
            _drag = Drag.Move;
            _bounds0 = _layers[hit].Bounds;
            _canvasPanel.Invalidate();
        }

        // ============================================================== mouse move

        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            _mouseScreen = e.Location;
            if (!_hasDoc) return;
            PointF cp = ScreenToCanvas(e.Location);
            if (_showRulers) _canvasPanel.Invalidate(new Rectangle(0, 0, _canvasPanel.Width, RulerSize));
            if (_showRulers) _canvasPanel.Invalidate(new Rectangle(0, 0, RulerSize, _canvasPanel.Height));

            if (_xf != null && _drag != Drag.Pan)
            {
                TransformMouseMove(e.Location, cp);
                return;
            }

            switch (_drag)
            {
                case Drag.Pan:
                    _origin = new PointF(_panOrigin0.X + (e.X - _downScreen.X), _panOrigin0.Y + (e.Y - _downScreen.Y));
                    _viewFitted = false;
                    _antsScreenPath = null;
                    _canvasPanel.Invalidate();
                    return;

                case Drag.Move:
                {
                    EditorLayer sel = SelectedLayer();
                    if (sel == null) return;
                    DragUndoOnce("Move");
                    RectangleF b = _bounds0;
                    float dx = cp.X - _downCanvas.X, dy = cp.Y - _downCanvas.Y;
                    if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                    {
                        if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0;   // Shift: straight moves
                    }
                    b.Offset(dx, dy);
                    sel.Bounds = b;
                    InvalidateDoc();
                    return;
                }

                case Drag.FloatMove:
                    FloatMoveTo(cp);
                    return;

                case Drag.Draw:
                    if (_tool == Tool.Pencil)
                    {
                        if (_penPts != null && (_penPts.Count == 0 || Dist(_penPts[_penPts.Count - 1], cp) > 1.5f / _zoom))
                            _penPts.Add(cp);
                        _draft = PenDraft();
                    }
                    else
                    {
                        _draft = ShapeDraft(_downCanvas, cp, (ModifierKeys & Keys.Shift) == Keys.Shift, (ModifierKeys & Keys.Alt) == Keys.Alt);
                    }
                    _canvasPanel.Invalidate();
                    return;

                case Drag.Marquee:
                    _marqueeDraft = MarqueeRect(cp);
                    _canvasPanel.Invalidate();
                    return;

                case Drag.Lasso:
                    if (_lassoPts != null && Dist(_lassoPts[_lassoPts.Count - 1], cp) > 1f / _zoom) _lassoPts.Add(cp);
                    _canvasPanel.Invalidate();
                    return;

                case Drag.Crop:
                {
                    PointF a = _downCanvas, b = cp;
                    var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                    _cropRect = ApplyCropRatio(r, b.X < a.X, b.Y < a.Y);
                    RelayoutOptions();
                    _canvasPanel.Invalidate();
                    return;
                }

                case Drag.CropAdjust:
                    CropAdjustTo(cp);
                    return;

                case Drag.Paint:
                    ContinueStroke(cp);
                    return;

                case Drag.Gradient:
                    _gradientEnd = cp;
                    if ((ModifierKeys & Keys.Shift) == Keys.Shift) _gradientEnd = Snap45(_downCanvas, cp);
                    _canvasPanel.Invalidate();
                    return;

                case Drag.Zoom:
                    _zoomRect = Rectangle.FromLTRB(Math.Min(_downScreen.X, e.X), Math.Min(_downScreen.Y, e.Y), Math.Max(_downScreen.X, e.X), Math.Max(_downScreen.Y, e.Y));
                    _canvasPanel.Invalidate();
                    return;
            }

            // idle: cursor feedback
            IdleCursor(e.Location, cp);
        }

        void IdleCursor(Point screen, PointF cp)
        {
            if (_polyPts != null) { _canvasPanel.Invalidate(); return; }
            if (IsBrushTool(_tool))
            {
                _canvasPanel.Invalidate();
                _canvasPanel.Cursor = ToolCursor(_tool);
                return;
            }
            if (_tool == Tool.Eyedropper)
            {
                Color c = PixelOps.Sample(Composite(), (int)cp.X, (int)cp.Y, 1);
                _statusColor.Text = c.A == 0 ? "" : string.Format("#{0:X2}{1:X2}{2:X2}   R{3} G{4} B{5}", c.R, c.G, c.B, c.R, c.G, c.B);
                return;
            }
            if (_tool == Tool.Crop && _cropRect.HasValue)
            {
                int h = HitCropHandle(screen);
                _canvasPanel.Cursor = h < 0 ? Cursors.Cross : h == 8 ? Cursors.SizeAll : HandleCursor(h);
                return;
            }
            if (_tool == Tool.Move)
            {
                EditorLayer sel = SelectedLayer();
                if (sel != null && _showTransformControls && !sel.Locked)
                {
                    int h = HitHandle(sel, screen);
                    if (h >= 0) { _canvasPanel.Cursor = HandleCursor(h); return; }
                    if (HitRotateZone(sel, screen)) { _canvasPanel.Cursor = _rotateCursor; return; }
                }
                bool over = false;
                for (int i = _layers.Count - 1; i >= 0; i--)
                    if (_layers[i].Visible && !_layers[i].Floating && _layers[i].HitTest(cp)) { over = true; break; }
                if (over && HasSelection && sel is RasterLayer && _selection.Contains((int)cp.X, (int)cp.Y))
                    _canvasPanel.Cursor = Cursors.SizeAll;
                else
                    _canvasPanel.Cursor = over ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            if (_spaceDown && !SpaceHeld()) _spaceDown = false;
            _canvasPanel.Cursor = _spaceDown ? Cursors.Hand : ToolCursor(_tool);
        }

        static Cursor HandleCursor(int h)
        {
            return (h == 1 || h == 5) ? Cursors.SizeNS
                 : (h == 3 || h == 7) ? Cursors.SizeWE
                 : (h == 0 || h == 4) ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }

        static PointF Snap45(PointF a, PointF b)
        {
            double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double snap = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
            float len = Dist(a, b);
            return new PointF(a.X + (float)(Math.Cos(snap) * len), a.Y + (float)(Math.Sin(snap) * len));
        }

        // ================================================================ mouse up

        void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) return;
            if (_xf != null && _drag != Drag.Pan)
            {
                TransformMouseUp();
                return;
            }
            Drag was = _drag;
            _drag = Drag.None;
            _canvasPanel.Cursor = ToolCursor(_tool);
            PointF cp = ScreenToCanvas(e.Location);

            switch (was)
            {
                case Drag.Draw:
                {
                    ShapeLayer done = _draft;
                    _draft = null;
                    _penPts = null;
                    if (done != null && (done.Bounds.Width > 2 || done.Bounds.Height > 2))
                    {
                        PushUndo(done.Kind == ShapeKind.Freehand ? "Pencil" : ToolName(_tool));
                        done.Name = NextName(done.Kind == ShapeKind.Freehand ? "Stroke" : ToolName(_tool));
                        int at = _sel >= 0 ? _sel + 1 : _layers.Count;
                        _layers.Insert(at, done);
                        _sel = at;
                        AfterDocumentChange();
                    }
                    else _canvasPanel.Invalidate();
                    break;
                }

                case Drag.Marquee:
                {
                    RectangleF r = _marqueeDraft ?? RectangleF.Empty;
                    _marqueeDraft = null;
                    if (r.Width < 1 && r.Height < 1)
                    {
                        if (_selMode == SelectionMode.New && HasSelection) { PushUndo("Deselect"); SetSelection(null); }
                        else _canvasPanel.Invalidate();
                        break;
                    }
                    EditorSelection made = _tool == Tool.MarqueeEllipse
                        ? EditorSelection.FromEllipse(r, _canvas)
                        : EditorSelection.FromRect(r, _canvas);
                    CommitSelection(made, ToolName(_tool));
                    break;
                }

                case Drag.Lasso:
                {
                    List<PointF> pts = _lassoPts;
                    _lassoPts = null;
                    if (pts == null || pts.Count < 3)
                    {
                        if (_selMode == SelectionMode.New && HasSelection) { PushUndo("Deselect"); SetSelection(null); }
                        else _canvasPanel.Invalidate();
                        break;
                    }
                    CommitSelection(EditorSelection.FromPolygon(pts, _canvas), "Lasso");
                    break;
                }

                case Drag.Crop:
                    if (_cropRect.HasValue && (_cropRect.Value.Width < 3 || _cropRect.Value.Height < 3))
                        _cropRect = null;
                    RelayoutOptions();
                    _canvasPanel.Invalidate();
                    break;

                case Drag.CropAdjust:
                    _cropHandle = -1;
                    RelayoutOptions();
                    _canvasPanel.Invalidate();
                    break;

                case Drag.Paint:
                    EndStroke();
                    break;

                case Drag.Gradient:
                    ApplyGradient(_downCanvas, _gradientEnd);
                    break;

                case Drag.Move:
                    if (_matrix0 != null) { _matrix0.Dispose(); _matrix0 = null; }
                    InvalidateDoc();
                    break;

                case Drag.FloatMove:
                    LandFloat();
                    break;

                case Drag.Zoom:
                {
                    Rectangle r = _zoomRect ?? Rectangle.Empty;
                    _zoomRect = null;
                    if (r.Width < 6 || r.Height < 6)
                    {
                        ZoomAt(e.Location, (ModifierKeys & Keys.Alt) == Keys.Alt ? 0.5f : 2f);
                    }
                    else
                    {
                        PointF a = ScreenToCanvas(r.Location), b = ScreenToCanvas(new Point(r.Right, r.Bottom));
                        Rectangle area = ViewArea();
                        float z = Math.Min(area.Width / Math.Max(1f, b.X - a.X), area.Height / Math.Max(1f, b.Y - a.Y));
                        _zoom = Math.Max(0.02f, Math.Min(32f, z));
                        PointF c = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
                        _origin = new PointF(area.X + area.Width / 2f - c.X * _zoom, area.Y + area.Height / 2f - c.Y * _zoom);
                        _viewFitted = false;
                        _antsScreenPath = null;
                        UpdateStatus();
                    }
                    _canvasPanel.Invalidate();
                    break;
                }
            }
        }

        /// <summary>Drops whatever drag is in flight (Esc, tool switch).</summary>
        void AbortDrag()
        {
            Drag was = _drag;
            _drag = Drag.None;
            switch (was)
            {
                case Drag.Paint: AbortStroke(); break;
                case Drag.Draw: _draft = null; _penPts = null; break;
                case Drag.Marquee: _marqueeDraft = null; break;
                case Drag.Lasso: _lassoPts = null; break;
                case Drag.Zoom: _zoomRect = null; break;
                case Drag.Move:
                    if (_dragUndoPushed) RevertLastUndo();
                    break;
                case Drag.FloatMove:
                    ClearHolePreview();
                    if (_floatLayer != null) RevertLastUndo();
                    _floatLayer = null; _floatHost = null;
                    break;
            }
            _canvasPanel.Cursor = ToolCursor(_tool);
            InvalidateDoc();
        }

        /// <summary>Puts the document back to the last undo snapshot and forgets that step.</summary>
        void RevertLastUndo()
        {
            if (_undo.Count == 0) return;
            Snapshot s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            RestoreSnapshot(s);
            RefreshHistoryList();
        }

        /// <summary>The undo step for a move: taken at the first real movement, so a click that selects and lets go never burns one.</summary>
        void DragUndoOnce(string name)
        {
            if (_dragUndoPushed) return;
            _dragUndoPushed = true;
            PushUndo(name);
        }

        // =============================================================== selections

        RectangleF MarqueeRect(PointF cp)
        {
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            bool alt = (ModifierKeys & Keys.Alt) == Keys.Alt;
            // Shift held before the click means "add" - it only constrains once released and pressed again
            bool constrain = shift && (_selMode != SelectionMode.Add && _selMode != SelectionMode.Intersect || _shiftReleasedInDrag);
            bool fromCenter = alt && (_selMode != SelectionMode.Subtract && _selMode != SelectionMode.Intersect || _altReleasedInDrag);
            PointF a = _downCanvas, b = cp;
            float w = b.X - a.X, h = b.Y - a.Y;
            if (constrain)
            {
                float m = Math.Max(Math.Abs(w), Math.Abs(h));
                w = Math.Sign(w) * m; h = Math.Sign(h) * m;
                if (w == 0) w = m; if (h == 0) h = m;
            }
            RectangleF r = fromCenter
                ? new RectangleF(a.X - Math.Abs(w), a.Y - Math.Abs(h), Math.Abs(w) * 2, Math.Abs(h) * 2)
                : RectangleF.FromLTRB(Math.Min(a.X, a.X + w), Math.Min(a.Y, a.Y + h), Math.Max(a.X, a.X + w), Math.Max(a.Y, a.Y + h));
            return r;
        }

        void CommitSelection(EditorSelection made, string name)
        {
            if (_marqueeFeather > 0 && (_tool == Tool.MarqueeRect || _tool == Tool.MarqueeEllipse || _tool == Tool.Lasso || _tool == Tool.PolyLasso))
                made = made.Feathered(_marqueeFeather);
            EditorSelection result = _selection == null ? (_selMode == SelectionMode.Subtract || _selMode == SelectionMode.Intersect ? null : made)
                                                        : _selection.Combine(made, _selMode);
            PushUndo(name);
            SetSelection(result);
        }

        void ClosePolyLasso()
        {
            List<PointF> pts = _polyPts;
            _polyPts = null;
            if (pts == null || pts.Count < 3) { _canvasPanel.Invalidate(); return; }
            CommitSelection(EditorSelection.FromPolygon(pts, _canvas), "Polygonal Lasso");
        }

        void WandClick(PointF cp, SelectionMode mode)
        {
            int x = (int)Math.Floor(cp.X), y = (int)Math.Floor(cp.Y);
            if (x < 0 || y < 0 || x >= _canvas.Width || y >= _canvas.Height) return;
            Bitmap source;
            bool dispose = false;
            if (_sampleAllLayers || SelectedLayer() == null) source = Composite();
            else { source = SelectedLayer().RenderAlone(_canvas, false); dispose = true; }
            Pixels px = Pixels.From(source);
            if (dispose) source.Dispose();
            byte[] mask = PixelOps.FloodMask(px, x, y, _wandTolerance, _wandContiguous);
            _selMode = mode;
            CommitSelection(EditorSelection.FromMask(mask, _canvas, 0), "Magic Wand");
        }

        /// <summary>Delete / Edit &gt; Clear: the selected pixels of the current image layer go transparent.</summary>
        void ClearSelection()
        {
            RasterLayer layer = SelectedRaster();
            if (layer == null || !HasSelection) return;
            if (layer.Locked) { Toast.Show("The layer is locked."); return; }
            byte[] mask = _selection.MaskForLayer(layer);
            PushUndo("Clear");
            Pixels px = Pixels.From(layer.Image);
            byte[] d = px.Data;
            for (int i = 0, k = 0; i < d.Length; i += 4, k++)
            {
                int m = mask == null ? 255 : mask[k];
                if (m == 0) continue;
                d[i + 3] = (byte)(d[i + 3] * (255 - m) / 255);
            }
            layer.Image = px.ToBitmap();
            AfterDocumentChange();
        }

        /// <summary>Edit &gt; Copy: the selected pixels of the current layer (the whole layer without a selection).</summary>
        void CopySelection(bool cut)
        {
            EditorLayer layer = SelectedLayer();
            if (layer == null) { Toast.Show("Select a layer first."); return; }
            using (Bitmap alone = layer.RenderAlone(_canvas, false))
            {
                if (HasSelection)
                {
                    using (Bitmap cutOut = CutOut(alone, _selection)) PutOnClipboard(cutOut, _selection.Bounds.Location);
                }
                else
                {
                    RectangleF box = layer.CanvasBox();
                    var r = Rectangle.Round(box);
                    r.Intersect(new Rectangle(0, 0, _canvas.Width, _canvas.Height));
                    if (r.Width < 1 || r.Height < 1) return;
                    using (Bitmap crop = alone.Clone(r, PixelFormat.Format32bppArgb)) PutOnClipboard(crop, r.Location);
                }
            }
            if (cut)
            {
                if (HasSelection && layer is RasterLayer) ClearSelection();
                else DeleteLayer();
            }
            else Toast.Show("Copied.");
        }

        // ============================================================ floating move

        /// <summary>
        /// The selected pixels of <paramref name="host"/> as a floating piece (a layer object
        /// that is NOT in the stack yet). Nothing changes on the canvas until
        /// <see cref="ActivateFloating"/> lifts it for real - Photoshop shows the piece, the
        /// hole and the history step only once the user actually moves it.
        /// </summary>
        RasterLayer CreateFloating(RasterLayer host, EditorSelection sel)
        {
            using (Bitmap alone = host.RenderAlone(_canvas, false))
            {
                Bitmap cut = CutOut(alone, sel);
                return new RasterLayer(cut) { Name = host.Name, Bounds = sel.Bounds, Opacity = host.Opacity, Blend = host.Blend, Floating = true };
            }
        }

        /// <summary>
        /// Puts the floating piece in the stack just above its host so it can be dragged
        /// about as a preview. The host's own pixels are NOT touched here: the layer only
        /// changes when the move or transform is applied (<see cref="CutSelectionFromHost"/>
        /// then <see cref="MergeFloating"/>), so cancelling never has to put anything back.
        /// </summary>
        void ActivateFloating(RasterLayer host, RasterLayer floating, EditorSelection sel)
        {
            int hostIndex = _layers.IndexOf(host);
            _layers.Insert(hostIndex + 1, floating);
            _sel = hostIndex + 1;
            // the hole: the host as it will look once the piece has left, shown in its place
            ClearHolePreview();
            try
            {
                using (Bitmap alone = host.RenderAlone(_canvas, false))
                {
                    Pixels p = Pixels.From(alone);
                    byte[] d = p.Data, m = sel.Mask;
                    for (int i = 0, k = 0; k < m.Length && i < d.Length; i += 4, k++)
                        if (m[k] != 0) d[i + 3] = (byte)(d[i + 3] * (255 - m[k]) / 255);
                    _holePreview = p.ToBitmap();
                    _holeHost = host;
                }
            }
            catch { ClearHolePreview(); }
        }

        void ClearHolePreview()
        {
            if (_holePreview != null) { try { _holePreview.Dispose(); } catch { } }
            _holePreview = null;
            _holeHost = null;
        }

        /// <summary>Compose() asks for every layer: a host with a piece floating answers with its hole, the piece being warped with its warp.</summary>
        Bitmap PreviewOf(EditorLayer layer)
        {
            if (_holePreview != null && layer == _holeHost) return _holePreview;
            return TransformPreviewOf(layer);
        }

        /// <summary>The apply step: the pixels the piece came from are cleared out of the host.</summary>
        void CutSelectionFromHost(RasterLayer host, EditorSelection sel)
        {
            if (host == null || sel == null || host.Image == null) return;
            byte[] mask = sel.MaskForLayer(host);
            Pixels px = Pixels.From(host.Image);
            byte[] d = px.Data;
            for (int i = 0, k = 0; i < d.Length; i += 4, k++)
            {
                int m = mask == null ? 255 : mask[k];
                if (m != 0) d[i + 3] = (byte)(d[i + 3] * (255 - m) / 255);
            }
            host.Image = px.ToBitmap();
            host.ContentVersion++;
        }

        RasterLayer LiftSelection(RasterLayer host)
        {
            RasterLayer floating = CreateFloating(host, _selection);
            ActivateFloating(host, floating, _selection);
            return floating;
        }

        /// <summary>A Move-tool press on a selected image layer: the lift waits for the first real movement.</summary>
        void BeginFloatMove(RasterLayer host, int hostIndex, PointF cp)
        {
            _floatLayer = null;
            _floatHost = host;
            _floatHostIndex = hostIndex;
            _floatSel0 = _selection;
            _bounds0 = RectangleF.Empty;
            _drag = Drag.FloatMove;
            _dragUndoPushed = false;
        }

        void FloatMoveTo(PointF cp)
        {
            if (_floatHost == null) return;
            float dx = cp.X - _downCanvas.X, dy = cp.Y - _downCanvas.Y;
            if ((ModifierKeys & Keys.Shift) == Keys.Shift) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }
            int ix = (int)Math.Round(dx), iy = (int)Math.Round(dy);
            if (_floatLayer == null)
            {
                if (ix == 0 && iy == 0) return;          // a click, or not far enough yet
                PushUndo("Move");
                _dragUndoPushed = true;
                _selection = _floatSel0;
                _floatLayer = LiftSelection(_floatHost);
                _bounds0 = _floatLayer.Bounds;
                RefreshLayerList();
            }
            RectangleF b = _bounds0;
            b.Offset(ix, iy);
            _floatLayer.Bounds = b;
            _selection = _floatSel0.Offset(ix, iy);
            _antsScreenPath = null;
            InvalidateDoc();
        }

        /// <summary>
        /// The release of a Move drag is its apply: the pixels leave their old place in the
        /// layer and land at the new one. A plain click changes nothing.
        /// </summary>
        void LandFloat()
        {
            RasterLayer host = _floatHost, floating = _floatLayer;
            EditorSelection from = _floatSel0;
            _floatHost = null; _floatLayer = null;
            if (host == null) return;
            if (floating == null) { _canvasPanel.Invalidate(); return; }
            ClearHolePreview();
            CutSelectionFromHost(host, from);
            MergeFloating(host, floating);
            _sel = _layers.IndexOf(host);
            AfterDocumentChange();
        }

        /// <summary>Merges a floating layer back into its host and removes it from the stack.</summary>
        void MergeFloating(RasterLayer host, RasterLayer floating)
        {
            _layers.Remove(floating);
            bool floatingPlain = floating.RotationDeg == 0 && floating.ShearX == 0 && floating.ShearY == 0 && !floating.FlipH && !floating.FlipV &&
                                 Math.Abs(floating.Bounds.Width - floating.Image.Width) < 0.01f && Math.Abs(floating.Bounds.Height - floating.Image.Height) < 0.01f;
            bool plain = floatingPlain && host.RotationDeg == 0 && host.ShearX == 0 && host.ShearY == 0 && !host.FlipH && !host.FlipV &&
                         Math.Abs(host.Bounds.Width - host.Image.Width) < 0.01f && Math.Abs(host.Bounds.Height - host.Image.Height) < 0.01f;
            if (plain)
            {
                // merge in the host's own pixel space, growing it if the pixels went outside
                int hx = (int)Math.Round(host.Bounds.X), hy = (int)Math.Round(host.Bounds.Y);
                int fx = (int)Math.Round(floating.Bounds.X), fy = (int)Math.Round(floating.Bounds.Y);
                var hostRect = new Rectangle(hx, hy, host.Image.Width, host.Image.Height);
                var floatRect = new Rectangle(fx, fy, floating.Image.Width, floating.Image.Height);
                Rectangle union = Rectangle.Union(hostRect, floatRect);
                var bmp = new Bitmap(union.Width, union.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImageUnscaled(host.Image, hx - union.X, hy - union.Y);
                    g.DrawImageUnscaled(floating.Image, fx - union.X, fy - union.Y);
                }
                host.Image = bmp;
                host.Bounds = new RectangleF(union.X, union.Y, union.Width, union.Height);
            }
            else
            {
                // a transformed host or a scaled/rotated floater: bake both flat in canvas space
                var two = new List<EditorLayer> { host, floating };
                bool v = host.Visible; host.Visible = true; floating.Visible = true;
                Bitmap baked = EditorRender.Compose(two, _canvas, Color.Transparent, null);
                host.Visible = v;
                host.Image = baked;
                host.Bounds = new RectangleF(0, 0, _canvas.Width, _canvas.Height);
                host.RotationDeg = 0; host.ShearX = host.ShearY = 0; host.FlipH = host.FlipV = false;
            }
            host.ContentVersion++;
            floating.Image.Dispose();
        }

        // ================================================================== crop

        void CropToolDown(Point screen, PointF cp)
        {
            if (_cropRect.HasValue)
            {
                int h = HitCropHandle(screen);
                if (h >= 0)
                {
                    _cropHandle = h;
                    _bounds0 = _cropRect.Value;
                    _drag = Drag.CropAdjust;
                    return;
                }
            }
            _drag = Drag.Crop;
            _cropRect = new RectangleF(cp.X, cp.Y, 0, 0);
        }

        int HitCropHandle(Point screen)
        {
            if (!_cropRect.HasValue) return -1;
            RectangleF c = _cropRect.Value;
            PointF[] hs = HandleLocalPositions(c);
            for (int i = 0; i < 8; i++)
            {
                PointF s = CanvasToScreen(hs[i]);
                if (Math.Abs(s.X - screen.X) <= 7 && Math.Abs(s.Y - screen.Y) <= 7) return i;
            }
            PointF tl = CanvasToScreen(new PointF(c.Left, c.Top)), br = CanvasToScreen(new PointF(c.Right, c.Bottom));
            if (screen.X > tl.X && screen.X < br.X && screen.Y > tl.Y && screen.Y < br.Y) return 8;
            return -1;
        }

        float CropRatioValue()
        {
            switch (_cropRatio)
            {
                case 1: return 1f;
                case 2: return 4f / 3f;
                case 3: return 16f / 9f;
                case 4: return 3f / 2f;
                case 5: return _canvas.Height > 0 ? (float)_canvas.Width / _canvas.Height : 1f;
                default: return 0;
            }
        }

        RectangleF ApplyCropRatio(RectangleF r, bool anchorRight, bool anchorBottom)
        {
            float ratio = CropRatioValue();
            if (ratio <= 0) return r;
            float w = r.Width, h = r.Height;
            if (w / Math.Max(1f, h) > ratio) h = w / ratio; else w = h * ratio;
            float x = anchorRight ? r.Right - w : r.Left;
            float y = anchorBottom ? r.Bottom - h : r.Top;
            return new RectangleF(x, y, w, h);
        }

        void CropAdjustTo(PointF cp)
        {
            RectangleF r = _bounds0;
            float dx = cp.X - _downCanvas.X, dy = cp.Y - _downCanvas.Y;
            if (_cropHandle == 8)
            {
                r.Offset(dx, dy);
                _cropRect = r;
                _canvasPanel.Invalidate();
                RelayoutOptions();
                return;
            }
            float l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            bool left = _cropHandle == 0 || _cropHandle == 7 || _cropHandle == 6;
            bool right = _cropHandle == 2 || _cropHandle == 3 || _cropHandle == 4;
            bool top = _cropHandle == 0 || _cropHandle == 1 || _cropHandle == 2;
            bool bottom = _cropHandle == 6 || _cropHandle == 5 || _cropHandle == 4;
            if (left) l = Math.Min(rt - 4, l + dx);
            if (right) rt = Math.Max(l + 4, rt + dx);
            if (top) t = Math.Min(b - 4, t + dy);
            if (bottom) b = Math.Max(t + 4, b + dy);
            var nr = RectangleF.FromLTRB(l, t, rt, b);
            float ratio = CropRatioValue();
            if (ratio > 0)
            {
                bool corner = _cropHandle % 2 == 0;
                if (corner) nr = ApplyCropRatio(nr, left, top);
                else if (left || right) { float h = nr.Width / ratio; nr = new RectangleF(nr.X, nr.Y + (nr.Height - h) / 2f, nr.Width, h); }
                else { float w = nr.Height * ratio; nr = new RectangleF(nr.X + (nr.Width - w) / 2f, nr.Y, w, nr.Height); }
            }
            _cropRect = nr;
            RelayoutOptions();
            _canvasPanel.Invalidate();
        }

        void PaintCropOverlay(Graphics g)
        {
            if (!_cropRect.HasValue) return;
            RectangleF c = _cropRect.Value;
            PointF tl = CanvasToScreen(new PointF(c.Left, c.Top));
            PointF br = CanvasToScreen(new PointF(c.Right, c.Bottom));
            var cropScreen = new RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
            using (var outside = new Region(_canvasPanel.ClientRectangle))
            using (var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            {
                outside.Exclude(cropScreen);
                g.FillRegion(dim, outside);
            }
            using (var p = new Pen(Color.White, 1f))
            using (var thirds = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
            {
                g.DrawRectangle(p, cropScreen.X, cropScreen.Y, cropScreen.Width, cropScreen.Height);
                for (int i = 1; i < 3; i++)
                {
                    g.DrawLine(thirds, cropScreen.X + cropScreen.Width * i / 3f, cropScreen.Y, cropScreen.X + cropScreen.Width * i / 3f, cropScreen.Bottom);
                    g.DrawLine(thirds, cropScreen.X, cropScreen.Y + cropScreen.Height * i / 3f, cropScreen.Right, cropScreen.Y + cropScreen.Height * i / 3f);
                }
            }
            PointF[] hs = HandleLocalPositions(c);
            using (var fill = new SolidBrush(Color.White))
            using (var edge = new Pen(Color.Black, 1f))
                for (int i = 0; i < 8; i++)
                {
                    PointF s = CanvasToScreen(hs[i]);
                    g.FillRectangle(fill, s.X - 4, s.Y - 4, 8, 8);
                    g.DrawRectangle(edge, s.X - 4, s.Y - 4, 8, 8);
                }
        }

        // ============================================================= eyedropper

        void Eyedrop(PointF cp, bool background)
        {
            int size = _sampleSize == 0 ? 1 : _sampleSize == 1 ? 3 : 5;
            Color c = PixelOps.Sample(Composite(), (int)Math.Floor(cp.X), (int)Math.Floor(cp.Y), size);
            if (c.A == 0) return;
            c = Color.FromArgb(255, c.R, c.G, c.B);
            if (background) _bg = c; else _fg = c;
            _toolRail.Invalidate();
            _statusColor.Text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            SyncOptionsFromSelection();
        }

        // ================================================================= brushes

        /// <summary>The image layer a brush paints into; the Brush tool makes one when there is none.</summary>
        RasterLayer PaintTarget(bool createIfNeeded, string what)
        {
            RasterLayer target = SelectedRaster();
            if (target != null && target.Locked) { Toast.Show("The layer is locked."); return null; }
            if (target != null) return target;
            if (!createIfNeeded)
            {
                Toast.Show(what + " works on image layers - select one first.");
                return null;
            }
            var layer = new RasterLayer(NewTransparentBitmap(_canvas.Width, _canvas.Height))
            {
                Name = NextName("Layer"),
                Bounds = new RectangleF(0, 0, _canvas.Width, _canvas.Height)
            };
            int at = _sel >= 0 ? _sel + 1 : _layers.Count;
            _layers.Insert(at, layer);
            _sel = at;
            RefreshLayerList();
            return layer;
        }

        void SetCloneSource(PointF cp)
        {
            RasterLayer target = PaintTarget(false, "Clone Stamp");
            if (target == null) return;
            PointF p = target.ToPixel(cp);
            _cloneSource = new Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
            _cloneOffsetSet = false;
            Toast.Show("Clone source set.");
        }

        void BeginStroke(PointF cp)
        {
            bool creates = _tool == Tool.Brush;
            string name = ToolName(_tool);
            if (_tool == Tool.Clone && !_cloneSource.HasValue) { Toast.Show("Alt-click to set the clone source first."); return; }
            PushUndo(name);   // before PaintTarget, which may add the layer the stroke lands on
            RasterLayer target = PaintTarget(creates, name);
            if (target == null) { PopUndo(); return; }

            _strokeTool = _tool;
            _paintLayer = target;
            _paintOriginal = target.Image;
            _paintOrig = Pixels.From(target.Image);
            _paintWork = _paintOrig.Clone();
            _paintBitmap = _paintOrig.ToBitmap();
            _paintSelMask = HasSelection ? _selection.MaskForLayer(target) : null;
            _strokeMask = new byte[_paintOrig.Width * _paintOrig.Height];
            target.Image = _paintBitmap;
            target.ContentVersion++;

            float ppu = (target.PixelsPerUnitX + target.PixelsPerUnitY) / 2f;
            _dabRadius = Math.Max(0.5f, _brushSize / 2f * ppu);
            _dabSpacing = Math.Max(1f, _dabRadius * 0.25f);
            _dabCarry = 0;

            _paintFiltered = null;
            switch (_tool)
            {
                case Tool.Blur:
                    _paintFiltered = _paintOrig.Clone();
                    PixelOps.GaussianBlur(_paintFiltered, Math.Max(1f, _blurStrength * ppu), null);
                    break;
                case Tool.Sharpen:
                    _paintFiltered = _paintOrig.Clone();
                    PixelOps.UnsharpMask(_paintFiltered, 60 + _blurStrength * 20, 1.5f, 0, null);
                    break;
                case Tool.Dodge:
                    _paintFiltered = _paintOrig.Clone();
                    PixelOps.Exposure(_paintFiltered, _exposure / 100f * 1.5f, 0, 1f, null);
                    break;
                case Tool.Burn:
                    _paintFiltered = _paintOrig.Clone();
                    PixelOps.Exposure(_paintFiltered, -_exposure / 100f * 1.5f, 0, 1f, null);
                    break;
            }
            if (_tool == Tool.Clone && !_cloneOffsetSet)
            {
                PointF p0 = target.ToPixel(cp);
                _cloneOffset = new Point(_cloneSource.Value.X - (int)Math.Round(p0.X), _cloneSource.Value.Y - (int)Math.Round(p0.Y));
                _cloneOffsetSet = true;
            }
            _drag = Drag.Paint;
            PointF p = target.ToPixel(cp);
            _lastDab = p;
            StampDab(p);
        }

        void ContinueStroke(PointF cp)
        {
            if (_paintLayer == null) return;
            PointF p = _paintLayer.ToPixel(cp);
            float dist = Dist(_lastDab, p);
            if (dist <= 0.001f) return;
            float dx = (p.X - _lastDab.X) / dist, dy = (p.Y - _lastDab.Y) / dist;
            float t = _dabSpacing - _dabCarry;
            while (t <= dist)
            {
                StampDab(new PointF(_lastDab.X + dx * t, _lastDab.Y + dy * t));
                t += _dabSpacing;
            }
            _dabCarry = dist - (t - _dabSpacing);
            _lastDab = p;
        }

        void StampDab(PointF p)
        {
            int w = _paintOrig.Width, h = _paintOrig.Height;
            var roi = new Rectangle((int)Math.Floor(p.X - _dabRadius) - 2, (int)Math.Floor(p.Y - _dabRadius) - 2,
                                    (int)Math.Ceiling(_dabRadius * 2) + 5, (int)Math.Ceiling(_dabRadius * 2) + 5);
            roi.Intersect(new Rectangle(0, 0, w, h));
            if (roi.Width < 1 || roi.Height < 1) return;
            int hardness = _strokeTool == Tool.Eraser || _strokeTool == Tool.Brush ? _brushHardness : Math.Min(_brushHardness, 50);
            PixelOps.DabMask(_strokeMask, w, h, p.X, p.Y, _dabRadius, hardness, _brushFlow);
            float opacity = _brushOpacity / 100f;
            switch (_strokeTool)
            {
                case Tool.Brush:
                    PixelOps.PaintColor(_paintWork, _paintOrig, _strokeMask, _fg, opacity, _paintSelMask, roi);
                    break;
                case Tool.Eraser:
                    PixelOps.Erase(_paintWork, _paintOrig, _strokeMask, opacity, _paintSelMask, roi);
                    break;
                case Tool.Clone:
                    PixelOps.CloneStamp(_paintWork, _paintOrig, _paintOrig, _cloneOffset.X, _cloneOffset.Y, _strokeMask, opacity, _paintSelMask, roi);
                    break;
                default:
                    PixelOps.MixFiltered(_paintWork, _paintOrig, _paintFiltered, _strokeMask, opacity, _paintSelMask, roi);
                    break;
            }
            _paintWork.WriteTo(_paintBitmap, roi);
            _paintLayer.ContentVersion++;
            InvalidateDoc();
        }

        void EndStroke()
        {
            if (_paintLayer == null) return;
            _paintLayer.ContentVersion++;
            _paintLayer = null;
            _paintOrig = _paintWork = _paintFiltered = null;
            _strokeMask = _paintSelMask = null;
            _paintOriginal = null;
            _paintBitmap = null;
            AfterDocumentChange();
        }

        void AbortStroke()
        {
            if (_paintLayer == null) return;
            _paintLayer.Image = _paintOriginal;
            _paintLayer.ContentVersion++;
            _paintLayer = null;
            _paintOrig = _paintWork = _paintFiltered = null;
            _strokeMask = _paintSelMask = null;
            _paintOriginal = null;
            if (_paintBitmap != null) { _paintBitmap.Dispose(); _paintBitmap = null; }
            PopUndo();
            AfterDocumentChange();
        }

        void PaintBrushCursor(Graphics g)
        {
            if (!IsBrushTool(_tool) || !_mouseInside) return;
            float d = _brushSize * _zoom;
            if (d < 6) return;
            using (var white = new Pen(Color.FromArgb(200, 255, 255, 255), 1f))
            using (var black = new Pen(Color.FromArgb(200, 0, 0, 0), 1f))
            {
                g.DrawEllipse(white, _mouseScreen.X - d / 2 - 0.5f, _mouseScreen.Y - d / 2 - 0.5f, d + 1, d + 1);
                g.DrawEllipse(black, _mouseScreen.X - d / 2, _mouseScreen.Y - d / 2, d, d);
                if (_brushHardness < 100)
                {
                    float inner = d * _brushHardness / 100f;
                    using (var soft = new Pen(Color.FromArgb(90, 0, 0, 0), 1f) { DashStyle = DashStyle.Dot })
                        g.DrawEllipse(soft, _mouseScreen.X - inner / 2, _mouseScreen.Y - inner / 2, inner, inner);
                }
            }
            if (_tool == Tool.Clone && _cloneSource.HasValue && _paintLayer == null)
            {
                RasterLayer t = SelectedRaster();
                if (t != null)
                {
                    PointF src = t.ToCanvas(new PointF(t.Bounds.X + _cloneSource.Value.X / t.PixelsPerUnitX, t.Bounds.Y + _cloneSource.Value.Y / t.PixelsPerUnitY));
                    PointF s = CanvasToScreen(src);
                    using (var p = new Pen(Theme.Accent, 1.4f))
                    {
                        g.DrawLine(p, s.X - 6, s.Y, s.X + 6, s.Y);
                        g.DrawLine(p, s.X, s.Y - 6, s.X, s.Y + 6);
                    }
                }
            }
        }

        // ============================================================ bucket & gradient

        void BucketClick(PointF cp)
        {
            PushUndo("Paint Bucket");
            RasterLayer target = PaintTarget(true, "Paint Bucket");
            if (target == null) { PopUndo(); return; }
            PointF p = target.ToPixel(cp);
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            if (x < 0 || y < 0 || x >= target.Image.Width || y >= target.Image.Height) { PopUndo(); AfterDocumentChange(); return; }
            Pixels px = Pixels.From(target.Image);
            byte[] mask = PixelOps.FloodMask(px, x, y, _bucketTolerance, _wandContiguous);
            byte[] sel = HasSelection ? _selection.MaskForLayer(target) : null;
            if (sel != null) for (int i = 0; i < mask.Length; i++) mask[i] = (byte)(mask[i] * sel[i] / 255);
            PixelOps.Fill(px, _fg, _brushOpacity / 100f, mask, BlendMode.Normal);
            target.Image = px.ToBitmap();
            AfterDocumentChange();
        }

        void ApplyGradient(PointF a, PointF b)
        {
            if (Dist(a, b) < 1) { _canvasPanel.Invalidate(); return; }
            PushUndo("Gradient");
            RasterLayer target = PaintTarget(true, "Gradient");
            if (target == null) { PopUndo(); return; }
            Pixels px = Pixels.From(target.Image);
            byte[] sel = HasSelection ? _selection.MaskForLayer(target) : null;
            Color c2 = _gradientToTransparent ? Color.FromArgb(0, _fg) : _bg;
            PixelOps.Gradient(px, target.ToPixel(a), target.ToPixel(b), _fg, c2, _gradientKind == 1, _gradientReverse, _brushOpacity / 100f, sel);
            target.Image = px.ToBitmap();
            AfterDocumentChange();
        }

        // ================================================================== shapes

        ShapeLayer ShapeDraft(PointF a, PointF b, bool constrain, bool fromCenter)
        {
            ShapeKind kind = _tool == Tool.ShapeRect ? ShapeKind.Rectangle
                           : _tool == Tool.ShapeRoundRect ? ShapeKind.RoundedRectangle
                           : _tool == Tool.ShapeEllipse ? ShapeKind.Ellipse
                           : _tool == Tool.ShapePolygon ? ShapeKind.Polygon
                           : _tool == Tool.ShapeLine ? ShapeKind.Line : ShapeKind.Arrow;
            var s = new ShapeLayer { Kind = kind, Stroke = _stroke, StrokeWidth = _strokeW, Fill = _fill, CornerRadius = _cornerRadius, Sides = _polySides };
            if (kind == ShapeKind.Line || kind == ShapeKind.Arrow)
            {
                if (constrain) b = Snap45(a, b);
                var box = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                if (box.Width < 1) box.Width = 1;
                if (box.Height < 1) box.Height = 1;
                s.Bounds = box;
                s.Points.Add(new PointF((a.X - box.X) / box.Width, (a.Y - box.Y) / box.Height));
                s.Points.Add(new PointF((b.X - box.X) / box.Width, (b.Y - box.Y) / box.Height));
            }
            else
            {
                float w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
                if (constrain) w = h = Math.Max(w, h);
                if (fromCenter)
                {
                    s.Bounds = new RectangleF(a.X - w, a.Y - h, w * 2, h * 2);
                }
                else
                {
                    float x = b.X < a.X ? a.X - w : a.X;
                    float y = b.Y < a.Y ? a.Y - h : a.Y;
                    s.Bounds = new RectangleF(x, y, w, h);
                }
            }
            return s;
        }

        ShapeLayer PenDraft()
        {
            if (_penPts == null || _penPts.Count < 2) return null;
            float minX = _penPts.Min(p => p.X), maxX = _penPts.Max(p => p.X);
            float minY = _penPts.Min(p => p.Y), maxY = _penPts.Max(p => p.Y);
            var box = new RectangleF(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            var s = new ShapeLayer { Kind = ShapeKind.Freehand, Stroke = _stroke, StrokeWidth = _strokeW, Bounds = box };
            foreach (PointF p in _penPts)
                s.Points.Add(new PointF((p.X - box.X) / box.Width, (p.Y - box.Y) / box.Height));
            return s;
        }

        // ================================================================ overlays

        void PaintMarqueeDraft(Graphics g)
        {
            using (var white = new Pen(Color.White, 1f))
            using (var black = new Pen(Color.Black, 1f) { DashPattern = new[] { 4f, 4f } })
            {
                if (_marqueeDraft.HasValue && _drag == Drag.Marquee)
                {
                    RectangleF r = _marqueeDraft.Value;
                    PointF tl = CanvasToScreen(new PointF(r.Left, r.Top)), br = CanvasToScreen(new PointF(r.Right, r.Bottom));
                    var sr = RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y);
                    if (_tool == Tool.MarqueeEllipse) { g.DrawEllipse(white, sr); g.DrawEllipse(black, sr); }
                    else { g.DrawRectangle(white, sr.X, sr.Y, sr.Width, sr.Height); g.DrawRectangle(black, sr.X, sr.Y, sr.Width, sr.Height); }
                }
                if (_lassoPts != null && _lassoPts.Count > 1)
                {
                    PointF[] pts = _lassoPts.Select(p => CanvasToScreen(p)).ToArray();
                    g.DrawLines(white, pts);
                    g.DrawLines(black, pts);
                }
                if (_polyPts != null && _polyPts.Count > 0)
                {
                    var pts = _polyPts.Select(p => CanvasToScreen(p)).ToList();
                    pts.Add(_mouseScreen);
                    if (pts.Count > 1) { g.DrawLines(white, pts.ToArray()); g.DrawLines(black, pts.ToArray()); }
                    PointF first = pts[0];
                    using (var accent = new Pen(Theme.Accent, 1.5f)) g.DrawRectangle(accent, first.X - 4, first.Y - 4, 8, 8);
                }
                if (_drag == Drag.Gradient)
                {
                    PointF a = CanvasToScreen(_downCanvas), b = CanvasToScreen(_gradientEnd);
                    g.DrawLine(white, a, b);
                    g.DrawLine(black, a, b);
                    g.DrawEllipse(white, a.X - 3, a.Y - 3, 6, 6);
                    g.DrawEllipse(white, b.X - 3, b.Y - 3, 6, 6);
                }
            }
        }

        void PaintZoomRect(Graphics g)
        {
            if (!_zoomRect.HasValue || _drag != Drag.Zoom) return;
            Rectangle r = _zoomRect.Value;
            using (var p = new Pen(Theme.Accent, 1f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r);
        }

        // ================================================================ handles

        /// <summary>
        /// Handle layout in layer-local coordinates:
        /// 0 TL, 1 TM, 2 TR, 3 MR, 4 BR, 5 BM, 6 BL, 7 ML - opposite handle = (i+4)%8.
        /// </summary>
        static PointF[] HandleLocalPositions(RectangleF b)
        {
            float mx = b.X + b.Width / 2f, my = b.Y + b.Height / 2f;
            return new[]
            {
                new PointF(b.Left, b.Top), new PointF(mx, b.Top), new PointF(b.Right, b.Top),
                new PointF(b.Right, my), new PointF(b.Right, b.Bottom), new PointF(mx, b.Bottom),
                new PointF(b.Left, b.Bottom), new PointF(b.Left, my)
            };
        }

        /// <summary>The eight resize handles in screen pixels.</summary>
        PointF[] HandleScreenPositions(EditorLayer layer)
        {
            PointF[] local = HandleLocalPositions(layer.Bounds);
            var result = new PointF[8];
            using (Matrix m = layer.GetMatrix())
                for (int i = 0; i < 8; i++) result[i] = CanvasToScreen(TransformPoint(m, local[i]));
            return result;
        }

        int HitHandle(EditorLayer layer, Point screenPt)
        {
            PointF[] handles = HandleScreenPositions(layer);
            for (int i = 0; i < 8; i++)
                if (Math.Abs(screenPt.X - handles[i].X) <= 6 && Math.Abs(screenPt.Y - handles[i].Y) <= 6) return i;
            return -1;
        }

        /// <summary>Just outside a corner: the rotate zone, as in Photoshop.</summary>
        bool HitRotateZone(EditorLayer layer, Point screenPt)
        {
            PointF[] handles = HandleScreenPositions(layer);
            PointF[] corners = { handles[0], handles[2], handles[4], handles[6] };
            bool inside = layer.HitTest(ScreenToCanvas(screenPt));
            if (inside) return false;
            foreach (PointF c in corners)
            {
                float d = Dist(c, screenPt);
                if (d > 7 && d <= 26) return true;
            }
            return false;
        }

        // ============================================================ text layers

        void PlaceTextLayer(PointF cp)
        {
            PushUndo("Type");
            var layer = new TextLayer
            {
                Name = NextName("Text"),
                Bounds = new RectangleF(cp.X, cp.Y, Math.Max(160, _fontSize * 8), _fontSize * 1.8f),
                FontFamily = _fontFamily,
                FontSize = _fontSize,
                Bold = _bold,
                Italic = _italic,
                Underline = _underline,
                Align = _textAlign,
                Color = _textColor,
                BackColor = _textBack,
                OutlineColor = _textOutline
            };
            int at = _sel >= 0 ? _sel + 1 : _layers.Count;
            _layers.Insert(at, layer);
            _sel = at;
            RefreshLayerList();
            BeginInlineEdit(layer, true);
        }

        void BeginInlineEdit(TextLayer layer, bool isNew)
        {
            CommitInlineEdit();
            _editing = layer;
            _editingIsNew = isNew;
            _editTextBefore = layer.Text;
            _editingWasVisible = layer.Visible;
            layer.Visible = false;

            if (_inlineEdit == null)
            {
                _inlineEdit = new TextBox
                {
                    Multiline = true,
                    AcceptsReturn = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    WordWrap = true,
                    Visible = false
                };
                _inlineEdit.KeyDown += InlineEdit_KeyDown;
                _inlineEdit.LostFocus += delegate { CommitInlineEdit(); };
                _canvasPanel.Controls.Add(_inlineEdit);
            }

            PointF tl = CanvasToScreen(new PointF(layer.Bounds.Left, layer.Bounds.Top));
            _inlineEdit.SetBounds((int)tl.X, (int)tl.Y,
                Math.Max(80, (int)(layer.Bounds.Width * _zoom)),
                Math.Max(30, (int)(layer.Bounds.Height * _zoom)));
            try { _inlineEdit.Font = new Font(layer.FontFamily, Math.Max(4f, layer.FontSize * _zoom), layer.Style & ~FontStyle.Underline, GraphicsUnit.Pixel); }
            catch { _inlineEdit.Font = Theme.Base; }
            _inlineEdit.TextAlign = layer.Align == 1 ? HorizontalAlignment.Center : layer.Align == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _inlineEdit.ForeColor = layer.Color.A > 60 ? Color.FromArgb(255, layer.Color) : Theme.Text;
            _inlineEdit.BackColor = layer.BackColor.A > 60 ? Color.FromArgb(255, layer.BackColor) : Theme.FieldBg;
            _inlineEdit.Text = layer.Text;
            _inlineEdit.Visible = true;
            _inlineEdit.Focus();
            _inlineEdit.SelectionStart = _inlineEdit.TextLength;
            InvalidateDoc();
            UpdateStatus();
        }

        void InlineEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelInlineEdit(true);
            }
            else if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CommitInlineEdit();
            }
        }

        void CommitInlineEdit()
        {
            if (_editing == null) return;
            TextLayer layer = _editing;
            _editing = null;
            string text = _inlineEdit.Text;
            _inlineEdit.Visible = false;
            layer.Visible = _editingWasVisible;

            if (string.IsNullOrEmpty(text.Trim()) && _editingIsNew)
            {
                // nothing typed: take the fresh layer (and its undo step) back out
                _layers.Remove(layer);
                PopUndo();
                _sel = Math.Min(_sel, _layers.Count - 1);
                AfterDocumentChange();
                return;
            }

            layer.Text = text;
            GrowTextBounds(layer);
            if (text == _editTextBefore && !_editingIsNew) PopUndo();   // opened and closed without changing anything
            AfterDocumentChange();
        }

        void CancelInlineEdit(bool restore)
        {
            if (_editing == null) return;
            TextLayer layer = _editing;
            _editing = null;
            _inlineEdit.Visible = false;
            layer.Visible = _editingWasVisible;
            if (restore)
            {
                if (_editingIsNew)
                {
                    _layers.Remove(layer);
                    PopUndo();
                    _sel = Math.Min(_sel, _layers.Count - 1);
                }
                else
                {
                    layer.Text = _editTextBefore;
                    PopUndo();
                }
            }
            AfterDocumentChange();
        }

        /// <summary>After editing, make sure the wrap box is tall enough for the text.</summary>
        void GrowTextBounds(TextLayer layer)
        {
            try
            {
                using (Graphics g = _canvasPanel.CreateGraphics())
                using (Font f = layer.MakeFont())
                {
                    SizeF size = g.MeasureString(layer.Text + " ", f, (int)Math.Max(20, layer.Bounds.Width));
                    RectangleF b = layer.Bounds;
                    if (size.Height > b.Height) { b.Height = size.Height + 4; layer.Bounds = b; }
                    if (size.Width > b.Width) { b.Width = size.Width + 4; layer.Bounds = b; }
                }
            }
            catch { }
        }
    }
}
