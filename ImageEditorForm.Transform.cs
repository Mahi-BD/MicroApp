using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MicroApp
{
    enum TransformMode { Free, Scale, Rotate, Skew, Distort, Perspective }

    /// <summary>
    /// Free Transform (Ctrl+T) and the Edit &gt; Transform commands. Scale, rotate and skew
    /// stay live on the layer's own geometry; Distort and Perspective switch the box to a
    /// free quadrilateral that is previewed with a projective warp and baked into pixels
    /// on commit - which is exactly what Photoshop does to a pixel layer.
    /// </summary>
    partial class ImageEditorForm
    {
        class TransformState
        {
            public EditorLayer Layer;
            public TransformMode Mode;
            public bool Quad;                 // Distort / Perspective: the box is four free points
            public PointF[] QuadPts;          // canvas coords TL, TR, BR, BL
            public Bitmap Preview;            // canvas-sized warp preview
            public string PreviewKey;
            public Bitmap PreviewSource;      // possibly downscaled copy of the layer's pixels
            public bool Changed;
            public RasterLayer FloatHost;     // set when the box holds a selection lifted out of this layer
            public bool Activated;            // the lift really happened (first move / scale / rotate)
            public EditorSelection Selection0;// the selection the transform started from

            // the drag in flight
            public int Handle = -1;
            public bool Moving, Rotating, Skewing;
            public RectangleF DragBounds0;
            public float DragRot0, DragShearX0, DragShearY0, RotStart;
            public PointF[] DragQuad0;
            public PointF AnchorCanvas, AnchorLocal;
            public Matrix Matrix0;
            public float FontSize0;

            public bool HitInside(Point screen) { return Inside != null && Inside(screen); }
            public Func<Point, bool> Inside;
        }

        TransformState _xf;
        float[] _lastXform;                   // dx, dy, sx, sy, drot - for Transform Again

        // options bar controls for the transform
        Label _xfXLbl, _xfYLbl, _xfWLbl, _xfHLbl, _xfALbl, _xfSxLbl, _xfSyLbl;
        ModernNumber _xfX, _xfY, _xfW, _xfH, _xfA, _xfSx, _xfSy;
        Button _xfLink, _xfOk, _xfCancel;
        bool _xfLinked = true;

        void BeginTransform(EditorLayer layer, TransformMode mode)
        {
            if (layer == null) { Toast.Show("Select a layer first."); return; }
            if (layer.Locked) { Toast.Show("The layer is locked."); return; }
            if (_xf != null)
            {
                if (_xf.Layer == layer) { SetTransformMode(mode); return; }
                CommitTransform();
            }
            CommitInlineEdit();
            PushUndo("Free Transform");
            RasterLayer floatHost = null;
            EditorSelection selection0 = null;
            var hostRaster = layer as RasterLayer;
            if (hostRaster != null && HasSelection && !_selection.IsAll)
            {
                // Photoshop transforms the selected pixels, not the whole layer. The piece is
                // only lifted out (hole, extra pixels in the stack) once it actually changes.
                floatHost = hostRaster;
                selection0 = _selection;
                layer = CreateFloating(hostRaster, _selection);
                _selection = null;
                _antsScreenPath = null;
            }
            _xf = new TransformState { Layer = layer, Mode = mode, FloatHost = floatHost, Selection0 = selection0 };
            _xf.Inside = screen => _xf != null && (_xf.Quad ? PointInQuad(_xf.QuadPts, ScreenToCanvas(screen)) : _xf.Layer.HitTest(ScreenToCanvas(screen)));
            if (mode == TransformMode.Distort || mode == TransformMode.Perspective) EnterQuadMode();
            if (floatHost == null) _sel = _layers.IndexOf(layer);
            RefreshLayerList();
            RelayoutOptions();
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        void SetTransformMode(TransformMode mode)
        {
            if (_xf == null) return;
            _xf.Mode = mode;
            if ((mode == TransformMode.Distort || mode == TransformMode.Perspective) && !_xf.Quad) EnterQuadMode();
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        /// <summary>The first real change to a transform on a selection: lift the pixels out now.</summary>
        void EnsureLifted()
        {
            if (_xf == null || _xf.FloatHost == null || _xf.Activated) return;
            ActivateFloating(_xf.FloatHost, (RasterLayer)_xf.Layer, _xf.Selection0);
            _xf.Activated = true;
            RefreshLayerList();
            InvalidateDoc();
        }

        /// <summary>Switches the transform to four free corners; text and shapes become pixels first.</summary>
        void EnterQuadMode()
        {
            if (_xf == null || _xf.Quad) return;
            EnsureLifted();
            EditorLayer layer = _xf.Layer;
            var raster = layer as RasterLayer;
            if (raster == null)
            {
                int idx = _layers.IndexOf(layer);
                raster = layer.Rasterize(_canvas);
                _layers[idx] = raster;
                _xf.Layer = raster;
                Toast.Show(layer.KindLabel + " layer rasterized for the warp.");
            }
            _xf.Quad = true;
            _xf.QuadPts = raster.CanvasCorners();
            // preview from a smaller copy when the layer is big - the warp runs per mouse move
            Bitmap src = raster.Image;
            long px = (long)src.Width * src.Height;
            if (px > 1_500_000)
            {
                float f = (float)Math.Sqrt(1_500_000.0 / px);
                var small = new Bitmap(Math.Max(1, (int)(src.Width * f)), Math.Max(1, (int)(src.Height * f)), PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, 0, 0, small.Width, small.Height);
                }
                _xf.PreviewSource = small;
            }
            else _xf.PreviewSource = src;
            InvalidateDoc();
        }

        /// <summary>Compose() asks for every layer; the one being warped answers with its preview.</summary>
        Bitmap TransformPreviewOf(EditorLayer layer)
        {
            if (_xf == null || !_xf.Quad || layer != _xf.Layer) return null;
            bool fast = _xf.Handle >= 0 || _xf.Moving || _xf.Rotating;
            string key = string.Concat(fast ? "f" : "q", _xf.QuadPts[0], _xf.QuadPts[1], _xf.QuadPts[2], _xf.QuadPts[3]);
            if (_xf.Preview != null && _xf.PreviewKey == key) return _xf.Preview;
            if (_xf.Preview != null) _xf.Preview.Dispose();
            Point origin;
            var canvasBmp = new Bitmap(Math.Max(1, _canvas.Width), Math.Max(1, _canvas.Height), PixelFormat.Format32bppArgb);
            try
            {
                using (Bitmap warped = PixelOps.WarpQuad(_xf.PreviewSource, _xf.QuadPts, out origin, fast))
                using (Graphics g = Graphics.FromImage(canvasBmp))
                    g.DrawImageUnscaled(warped, origin.X, origin.Y);
            }
            catch { }
            _xf.Preview = canvasBmp;
            _xf.PreviewKey = key;
            return canvasBmp;
        }

        void CommitTransform()
        {
            if (_xf == null) return;
            TransformState xf = _xf;
            _xf = null;
            EditorLayer layer = xf.Layer;
            if (xf.FloatHost != null && !xf.Activated)
            {
                // Ctrl+T on a selection, then Enter: the selection simply comes back
                _selection = xf.Selection0;
                _antsScreenPath = null;
                PopUndo();
                if (xf.Matrix0 != null) xf.Matrix0.Dispose();
                RelayoutOptions();
                AfterDocumentChange();
                return;
            }
            if (xf.Quad)
            {
                var raster = (RasterLayer)layer;
                try
                {
                    Point origin;
                    Bitmap warped = PixelOps.WarpQuad(raster.Image, xf.QuadPts, out origin, false);
                    var replaced = new RasterLayer(warped)
                    {
                        Name = raster.Name, Visible = raster.Visible, Locked = raster.Locked, Opacity = raster.Opacity,
                        Blend = raster.Blend, Fx = raster.Fx, Bounds = new RectangleF(origin.X, origin.Y, warped.Width, warped.Height)
                    };
                    int idx = _layers.IndexOf(raster);
                    _layers[idx] = replaced;
                    _sel = idx;
                }
                catch (Exception ex) { ModernDialog.Info("The warp failed", ex.Message); }
                if (xf.PreviewSource != null && xf.PreviewSource != raster.Image) xf.PreviewSource.Dispose();
                if (xf.Preview != null) xf.Preview.Dispose();
            }
            else if (!xf.Changed)
            {
                PopUndo();   // Ctrl+T then Enter: nothing happened
            }
            else
            {
                Snapshot before = _undo.Count > 0 && xf.FloatHost == null ? _undo[_undo.Count - 1] : null;
                if (before != null)
                {
                    int idx = _layers.IndexOf(layer);
                    if (idx >= 0 && idx < before.Layers.Length)
                    {
                        EditorLayer was = before.Layers[idx];
                        _lastXform = new[]
                        {
                            layer.Center.X - was.Center.X, layer.Center.Y - was.Center.Y,
                            layer.Bounds.Width / Math.Max(1f, was.Bounds.Width), layer.Bounds.Height / Math.Max(1f, was.Bounds.Height),
                            layer.RotationDeg - was.RotationDeg
                        };
                    }
                }
            }
            if (xf.Matrix0 != null) xf.Matrix0.Dispose();
            if (xf.FloatHost != null)
            {
                // the selection follows the transformed pixels, then they land back in their layer
                var floating = _layers[_sel] as RasterLayer;
                if (floating != null && _layers.Contains(xf.FloatHost))
                {
                    using (Bitmap alone = floating.RenderAlone(_canvas, false))
                    {
                        Pixels p = Pixels.From(alone);
                        var mask = new byte[p.Width * p.Height];
                        for (int i = 0, k = 0; k < mask.Length; i += 4, k++) mask[k] = p.Data[i + 3];
                        _selection = EditorSelection.FromMask(mask, _canvas, 0);
                        _antsScreenPath = null;
                    }
                    MergeFloating(xf.FloatHost, floating);
                    _sel = _layers.IndexOf(xf.FloatHost);
                }
            }
            RelayoutOptions();
            AfterDocumentChange();
        }

        void CancelTransform()
        {
            if (_xf == null) return;
            TransformState xf = _xf;
            _xf = null;
            if (xf.Preview != null) xf.Preview.Dispose();
            if (xf.PreviewSource != null && !(xf.Layer is RasterLayer && ((RasterLayer)xf.Layer).Image == xf.PreviewSource)) xf.PreviewSource.Dispose();
            if (xf.Matrix0 != null) xf.Matrix0.Dispose();
            RevertLastUndo();     // the snapshot taken when the transform began
            RelayoutOptions();
            AfterDocumentChange();
        }

        /// <summary>Edit &gt; Transform &gt; Again: repeats the last committed move/scale/rotate on the current layer.</summary>
        void TransformAgain()
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null || _lastXform == null) { Toast.Show("Nothing to repeat yet."); return; }
            if (sel.Locked) { Toast.Show("The layer is locked."); return; }
            CommitTransform();
            PushUndo("Transform Again");
            PointF c = sel.Center;
            float w = sel.Bounds.Width * _lastXform[2], h = sel.Bounds.Height * _lastXform[3];
            sel.Bounds = new RectangleF(c.X + _lastXform[0] - w / 2f, c.Y + _lastXform[1] - h / 2f, w, h);
            sel.RotationDeg = Normalise(sel.RotationDeg + _lastXform[4]);
            AfterDocumentChange();
        }

        /// <summary>Instant transforms (Edit &gt; Transform &gt; Rotate 180° and friends).</summary>
        void QuickTransform(string what)
        {
            EditorLayer sel = SelectedLayer();
            if (sel == null) { Toast.Show("Select a layer first."); return; }
            if (sel.Locked) { Toast.Show("The layer is locked."); return; }
            bool live = _xf != null && (_xf.Layer == sel || (_xf.FloatHost != null && _xf.FloatHost == sel));
            if (live) { EnsureLifted(); sel = _xf.Layer; }
            if (!live) PushUndo(what);
            if (live && _xf.Quad)
            {
                // rotate/flip the four points about their centre
                PointF[] q = _xf.QuadPts;
                float cx = (q[0].X + q[1].X + q[2].X + q[3].X) / 4f, cy = (q[0].Y + q[1].Y + q[2].Y + q[3].Y) / 4f;
                for (int i = 0; i < 4; i++)
                {
                    float x = q[i].X - cx, y = q[i].Y - cy;
                    switch (what)
                    {
                        case "Rotate 180°": q[i] = new PointF(cx - x, cy - y); break;
                        case "Rotate 90° CW": q[i] = new PointF(cx - y, cy + x); break;
                        case "Rotate 90° CCW": q[i] = new PointF(cx + y, cy - x); break;
                        case "Flip Horizontal": q[i] = new PointF(cx - x, cy + y); break;
                        case "Flip Vertical": q[i] = new PointF(cx + x, cy - y); break;
                    }
                }
                // mirrored corner positions mirror the mapping, so a flip needs nothing more
                _xf.PreviewKey = null;
                _xf.Changed = true;
                InvalidateDoc();
                return;
            }
            switch (what)
            {
                case "Rotate 180°": sel.RotationDeg = Normalise(sel.RotationDeg + 180); break;
                case "Rotate 90° CW": sel.RotationDeg = Normalise(sel.RotationDeg + 90); break;
                case "Rotate 90° CCW": sel.RotationDeg = Normalise(sel.RotationDeg - 90); break;
                case "Flip Horizontal": sel.FlipH = !sel.FlipH; break;
                case "Flip Vertical": sel.FlipV = !sel.FlipV; break;
            }
            if (live) { _xf.Changed = true; SyncTransformOptions(); }
            AfterDocumentChange();
        }

        // ============================================================== mouse

        static bool PointInQuad(PointF[] q, PointF p)
        {
            bool inside = false;
            for (int i = 0, j = 3; i < 4; j = i++)
            {
                if ((q[i].Y > p.Y) != (q[j].Y > p.Y) &&
                    p.X < (q[j].X - q[i].X) * (p.Y - q[i].Y) / (q[j].Y - q[i].Y) + q[i].X)
                    inside = !inside;
            }
            return inside;
        }

        PointF[] TransformHandleScreen()
        {
            if (_xf.Quad)
            {
                PointF[] q = _xf.QuadPts;
                var r = new PointF[8];
                r[0] = CanvasToScreen(q[0]); r[2] = CanvasToScreen(q[1]); r[4] = CanvasToScreen(q[2]); r[6] = CanvasToScreen(q[3]);
                r[1] = Mid(r[0], r[2]); r[3] = Mid(r[2], r[4]); r[5] = Mid(r[4], r[6]); r[7] = Mid(r[6], r[0]);
                return r;
            }
            return HandleScreenPositions(_xf.Layer);
        }

        static PointF Mid(PointF a, PointF b) { return new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f); }

        int TransformHitHandle(Point screen)
        {
            PointF[] hs = TransformHandleScreen();
            for (int i = 0; i < 8; i++)
                if (Math.Abs(screen.X - hs[i].X) <= 6 && Math.Abs(screen.Y - hs[i].Y) <= 6) return i;
            return -1;
        }

        bool TransformHitRotate(Point screen)
        {
            if (_xf.HitInside(screen)) return false;
            PointF[] hs = TransformHandleScreen();
            foreach (int i in new[] { 0, 2, 4, 6 })
            {
                float d = Dist(hs[i], screen);
                if (d > 7 && d <= 28) return true;
            }
            return false;
        }

        /// <summary>Returns false when the press lands nowhere near the box (the caller commits).</summary>
        bool TransformMouseDown(Point screen, PointF cp)
        {
            TransformState xf = _xf;
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            bool alt = (ModifierKeys & Keys.Alt) == Keys.Alt;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            int h = TransformHitHandle(screen);
            bool corner = h >= 0 && h % 2 == 0;
            bool edge = h >= 0 && h % 2 == 1;

            if (xf.Quad)
            {
                if (h >= 0)
                {
                    EnsureLifted();
                    xf.Handle = h;
                    xf.DragQuad0 = (PointF[])xf.QuadPts.Clone();
                    _downCanvas = cp;
                    return true;
                }
                if (TransformHitRotate(screen) || xf.Mode == TransformMode.Rotate && xf.HitInside(screen))
                {
                    EnsureLifted();
                    xf.Rotating = true;
                    xf.DragQuad0 = (PointF[])xf.QuadPts.Clone();
                    PointF c = QuadCenterScreen();
                    xf.RotStart = (float)(Math.Atan2(screen.Y - c.Y, screen.X - c.X) * 180 / Math.PI);
                    return true;
                }
                if (xf.HitInside(screen))
                {
                    EnsureLifted();
                    xf.Moving = true;
                    xf.DragQuad0 = (PointF[])xf.QuadPts.Clone();
                    _downCanvas = cp;
                    return true;
                }
                return false;
            }

            EditorLayer layer = xf.Layer;
            xf.DragBounds0 = layer.Bounds;
            xf.DragRot0 = layer.RotationDeg;
            xf.DragShearX0 = layer.ShearX;
            xf.DragShearY0 = layer.ShearY;
            if (xf.Matrix0 != null) xf.Matrix0.Dispose();
            xf.Matrix0 = layer.GetMatrix();
            var text = layer as TextLayer;
            xf.FontSize0 = text != null ? text.FontSize : 0;
            _downCanvas = cp;

            if (h >= 0)
            {
                EnsureLifted();
                bool wantDistort = xf.Mode == TransformMode.Distort || (xf.Mode == TransformMode.Free && ctrl && !alt && !shift);
                bool wantPerspective = xf.Mode == TransformMode.Perspective || (xf.Mode == TransformMode.Free && ctrl && alt && shift);
                bool wantSkew = xf.Mode == TransformMode.Skew || (xf.Mode == TransformMode.Free && ctrl && !alt && edge) || (xf.Mode == TransformMode.Free && ctrl && shift && edge);
                if (corner && (wantDistort || wantPerspective))
                {
                    if (wantPerspective) xf.Mode = TransformMode.Perspective; else if (xf.Mode != TransformMode.Perspective) xf.Mode = TransformMode.Distort;
                    EnterQuadMode();
                    xf.Handle = h;
                    xf.DragQuad0 = (PointF[])xf.QuadPts.Clone();
                    return true;
                }
                if (edge && (wantSkew || wantDistort))
                {
                    xf.Skewing = true;
                    xf.Handle = h;
                    PointF[] local = HandleLocalPositions(xf.DragBounds0);
                    xf.AnchorLocal = local[(h + 4) % 8];
                    xf.AnchorCanvas = TransformPoint(xf.Matrix0, xf.AnchorLocal);
                    return true;
                }
                if (xf.Mode == TransformMode.Rotate) { StartRotate(screen); return true; }
                xf.Handle = h;
                PointF[] loc = HandleLocalPositions(xf.DragBounds0);
                xf.AnchorLocal = alt ? new PointF(xf.DragBounds0.X + xf.DragBounds0.Width / 2f, xf.DragBounds0.Y + xf.DragBounds0.Height / 2f) : loc[(h + 4) % 8];
                xf.AnchorCanvas = TransformPoint(xf.Matrix0, xf.AnchorLocal);
                return true;
            }
            if (TransformHitRotate(screen) || (xf.Mode == TransformMode.Rotate && xf.HitInside(screen)))
            {
                EnsureLifted();
                StartRotate(screen);
                return true;
            }
            if (xf.HitInside(screen))
            {
                EnsureLifted();
                xf.Moving = true;
                return true;
            }
            return false;
        }

        void StartRotate(Point screen)
        {
            _xf.Rotating = true;
            PointF c = CanvasToScreen(_xf.Layer.Center);
            _xf.RotStart = (float)(Math.Atan2(screen.Y - c.Y, screen.X - c.X) * 180 / Math.PI);
        }

        PointF QuadCenterScreen()
        {
            PointF[] q = _xf.QuadPts;
            return CanvasToScreen(new PointF((q[0].X + q[1].X + q[2].X + q[3].X) / 4f, (q[0].Y + q[1].Y + q[2].Y + q[3].Y) / 4f));
        }

        void TransformMouseMove(Point screen, PointF cp)
        {
            TransformState xf = _xf;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            if (xf.Handle < 0 && !xf.Moving && !xf.Rotating && !xf.Skewing)
            {
                // idle: cursor feedback
                int h = TransformHitHandle(screen);
                bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
                if (h >= 0) _canvasPanel.Cursor = (ctrl && h % 2 == 1) || xf.Mode == TransformMode.Skew ? _skewCursor : HandleCursor(h);
                else if (TransformHitRotate(screen) || xf.Mode == TransformMode.Rotate) _canvasPanel.Cursor = _rotateCursor;
                else _canvasPanel.Cursor = xf.HitInside(screen) ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            xf.Changed = true;
            float dx = cp.X - _downCanvas.X, dy = cp.Y - _downCanvas.Y;

            if (xf.Quad)
            {
                PointF[] q0 = xf.DragQuad0;
                PointF[] q = xf.QuadPts;
                if (xf.Moving)
                {
                    if (shift) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }
                    for (int i = 0; i < 4; i++) q[i] = new PointF(q0[i].X + dx, q0[i].Y + dy);
                }
                else if (xf.Rotating)
                {
                    PointF c = QuadCenterScreen();
                    float a = (float)(Math.Atan2(screen.Y - c.Y, screen.X - c.X) * 180 / Math.PI) - xf.RotStart;
                    if (shift) a = (float)Math.Round(a / 15f) * 15f;
                    double rad = a * Math.PI / 180;
                    float cx = (q0[0].X + q0[1].X + q0[2].X + q0[3].X) / 4f, cy = (q0[0].Y + q0[1].Y + q0[2].Y + q0[3].Y) / 4f;
                    for (int i = 0; i < 4; i++)
                    {
                        float x = q0[i].X - cx, y = q0[i].Y - cy;
                        q[i] = new PointF(cx + (float)(x * Math.Cos(rad) - y * Math.Sin(rad)), cy + (float)(x * Math.Sin(rad) + y * Math.Cos(rad)));
                    }
                }
                else if (xf.Handle % 2 == 0)
                {
                    int ci = xf.Handle / 2;   // 0 TL, 1 TR, 2 BR, 3 BL
                    for (int i = 0; i < 4; i++) q[i] = q0[i];
                    q[ci] = new PointF(q0[ci].X + dx, q0[ci].Y + dy);
                    if (xf.Mode == TransformMode.Perspective)
                    {
                        int hn = ci == 0 ? 1 : ci == 1 ? 0 : ci == 2 ? 3 : 2;   // neighbour along the top/bottom edge
                        int vn = ci == 0 ? 3 : ci == 1 ? 2 : ci == 2 ? 1 : 0;   // neighbour along the left/right edge
                        q[hn] = new PointF(q0[hn].X - dx, q0[hn].Y);   // keystone: the top/bottom edge widens symmetrically
                        q[vn] = new PointF(q0[vn].X, q0[vn].Y - dy);   // ...and the left/right edge stretches symmetrically
                    }
                }
                else
                {
                    int a = xf.Handle / 2, b = (a + 1) % 4;   // the edge's two corners
                    for (int i = 0; i < 4; i++) q[i] = q0[i];
                    q[a] = new PointF(q0[a].X + dx, q0[a].Y + dy);
                    q[b] = new PointF(q0[b].X + dx, q0[b].Y + dy);
                }
                InvalidateDoc();
                return;
            }

            EditorLayer layer = xf.Layer;
            if (xf.Moving)
            {
                if (shift) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }
                RectangleF b = xf.DragBounds0;
                b.Offset(dx, dy);
                layer.Bounds = b;
            }
            else if (xf.Rotating)
            {
                PointF c = CanvasToScreen(layer.Center);
                float a = (float)(Math.Atan2(screen.Y - c.Y, screen.X - c.X) * 180 / Math.PI);
                float rot = xf.DragRot0 + (a - xf.RotStart);
                if (shift) rot = (float)Math.Round(rot / 15f) * 15f;
                layer.RotationDeg = Normalise(rot);
                _statusRight.Text = string.Format("rotation {0:0.0}°", layer.RotationDeg);
            }
            else if (xf.Skewing)
            {
                PointF local, down;
                using (Matrix inv = xf.Matrix0.Clone())
                {
                    inv.Invert();
                    local = TransformPoint(inv, cp);
                    down = TransformPoint(inv, _downCanvas);
                }
                float w = Math.Max(1f, xf.DragBounds0.Width), hgt = Math.Max(1f, xf.DragBounds0.Height);
                layer.ShearX = xf.DragShearX0;
                layer.ShearY = xf.DragShearY0;
                switch (xf.Handle)
                {
                    case 1: layer.ShearX = xf.DragShearX0 - 2f * (local.X - down.X) / hgt; break;   // top edge
                    case 5: layer.ShearX = xf.DragShearX0 + 2f * (local.X - down.X) / hgt; break;   // bottom edge
                    case 3: layer.ShearY = xf.DragShearY0 + 2f * (local.Y - down.Y) / w; break;     // right edge
                    case 7: layer.ShearY = xf.DragShearY0 - 2f * (local.Y - down.Y) / w; break;     // left edge
                }
                layer.ShearX = Math.Max(-4f, Math.Min(4f, layer.ShearX));
                layer.ShearY = Math.Max(-4f, Math.Min(4f, layer.ShearY));
                layer.Bounds = xf.DragBounds0;
                KeepAnchor(layer, xf.AnchorLocal, xf.AnchorCanvas);
            }
            else if (xf.Handle >= 0)
            {
                ScaleDrag(cp, shift);
            }
            SyncTransformOptions();
            InvalidateDoc();
        }

        /// <summary>After the geometry changed, shift the layer so the anchor stays where it was.</summary>
        static void KeepAnchor(EditorLayer layer, PointF anchorLocal, PointF anchorCanvas)
        {
            PointF now = layer.ToCanvas(anchorLocal);
            RectangleF b = layer.Bounds;
            b.Offset(anchorCanvas.X - now.X, anchorCanvas.Y - now.Y);
            layer.Bounds = b;
        }

        /// <summary>Corner drags keep proportions (Shift frees them), edge drags stretch one way; Alt scaled about the centre.</summary>
        void ScaleDrag(PointF canvasPt, bool shift)
        {
            TransformState xf = _xf;
            EditorLayer sel = xf.Layer;
            PointF local;
            using (Matrix inv = xf.Matrix0.Clone())
            {
                inv.Invert();
                local = TransformPoint(inv, canvasPt);
            }
            RectangleF b0 = xf.DragBounds0;
            PointF anchor = xf.AnchorLocal;
            bool aboutCenter = Math.Abs(anchor.X - (b0.X + b0.Width / 2f)) < 0.01f && Math.Abs(anchor.Y - (b0.Y + b0.Height / 2f)) < 0.01f;
            int h = xf.Handle;
            bool horizontal = h != 1 && h != 5;
            bool vertical = h != 3 && h != 7;
            bool corner = h % 2 == 0;

            float left = b0.Left, right = b0.Right, top = b0.Top, bottom = b0.Bottom;
            if (aboutCenter)
            {
                float cx = b0.X + b0.Width / 2f, cy = b0.Y + b0.Height / 2f;
                if (horizontal) { float hw = Math.Max(4, Math.Abs(local.X - cx)); left = cx - hw; right = cx + hw; }
                if (vertical) { float hh = Math.Max(4, Math.Abs(local.Y - cy)); top = cy - hh; bottom = cy + hh; }
            }
            else
            {
                if (horizontal)
                {
                    if (anchor.X > b0.Left + b0.Width / 2f) { left = Math.Min(local.X, anchor.X - 4); right = anchor.X; }
                    else { right = Math.Max(local.X, anchor.X + 4); left = anchor.X; }
                }
                if (vertical)
                {
                    if (anchor.Y > b0.Top + b0.Height / 2f) { top = Math.Min(local.Y, anchor.Y - 4); bottom = anchor.Y; }
                    else { bottom = Math.Max(local.Y, anchor.Y + 4); top = anchor.Y; }
                }
            }
            var nb = RectangleF.FromLTRB(left, top, right, bottom);
            bool proportional = corner ? !shift : shift;
            if (proportional && b0.Width > 1 && b0.Height > 1)
            {
                float ratio = b0.Width / b0.Height;
                float w = nb.Width, hh = nb.Height;
                if (corner)
                {
                    if (w / Math.Max(1f, hh) > ratio) w = hh * ratio; else hh = w / ratio;
                }
                else if (horizontal) hh = w / ratio;
                else w = hh * ratio;
                if (aboutCenter)
                {
                    float cx = b0.X + b0.Width / 2f, cy = b0.Y + b0.Height / 2f;
                    nb = new RectangleF(cx - w / 2f, cy - hh / 2f, w, hh);
                }
                else
                {
                    float x = anchor.X > b0.Left + b0.Width / 2f ? anchor.X - w : anchor.X;
                    float y = anchor.Y > b0.Top + b0.Height / 2f ? anchor.Y - hh : anchor.Y;
                    if (!horizontal) x = b0.X + (b0.Width - w) / 2f;
                    if (!vertical) y = b0.Y + (b0.Height - hh) / 2f;
                    nb = new RectangleF(x, y, w, hh);
                }
            }

            // a corner drag on text scales the type itself; an edge drag reflows the box
            var text = sel as TextLayer;
            if (text != null && corner && b0.Height > 1)
                text.FontSize = Math.Max(4f, xf.FontSize0 * (nb.Height / b0.Height));

            sel.Bounds = nb;
            KeepAnchor(sel, anchor, xf.AnchorCanvas);
        }

        void TransformMouseUp()
        {
            TransformState xf = _xf;
            bool wasDragging = xf.Handle >= 0 || xf.Moving || xf.Rotating || xf.Skewing;
            xf.Handle = -1;
            xf.Moving = xf.Rotating = xf.Skewing = false;
            _canvasPanel.Cursor = Cursors.Default;
            if (wasDragging && xf.Quad) InvalidateDoc();   // full-quality preview now the drag is over
            SyncTransformOptions();
            UpdateStatus();
            _canvasPanel.Invalidate();
        }

        // ============================================================== painting

        void PaintTransformControls(Graphics g)
        {
            EditorLayer sel = SelectedLayer();
            bool live = _xf != null;
            if (!live && !(_tool == Tool.Move && sel != null && _showTransformControls && _editing == null && _drag != Drag.FloatMove)) return;
            if (!live && sel == null) return;

            PointF[] corners;
            PointF[] handles;
            if (live && _xf.Quad)
            {
                handles = TransformHandleScreen();
                corners = new[] { handles[0], handles[2], handles[4], handles[6] };
            }
            else
            {
                EditorLayer l = live ? _xf.Layer : sel;
                corners = Array.ConvertAll(l.CanvasCorners(), p => CanvasToScreen(p));
                handles = HandleScreenPositions(l);
            }

            Color ink = live ? Theme.Accent : Color.FromArgb(180, Theme.Accent);
            using (var p = new Pen(ink, live ? 1.4f : 1f) { DashStyle = live ? DashStyle.Solid : DashStyle.Dash })
                g.DrawPolygon(p, corners);
            using (var fill = new SolidBrush(Color.White))
            using (var edge = new Pen(ink, 1.2f))
            {
                for (int i = 0; i < 8; i++)
                {
                    g.FillRectangle(fill, handles[i].X - 4, handles[i].Y - 4, 8, 8);
                    g.DrawRectangle(edge, handles[i].X - 4, handles[i].Y - 4, 8, 8);
                }
                if (live)
                {
                    // the reference point in the middle
                    PointF c = _xf.Quad ? QuadCenterScreen() : CanvasToScreen(_xf.Layer.Center);
                    g.DrawEllipse(edge, c.X - 4, c.Y - 4, 8, 8);
                    g.DrawLine(edge, c.X - 7, c.Y, c.X + 7, c.Y);
                    g.DrawLine(edge, c.X, c.Y - 7, c.X, c.Y + 7);
                }
            }
        }

        // =========================================================== options bar

        void BuildTransformOptions()
        {
            _xfXLbl = OptLabel("X");
            _xfX = OptNumeric(-20000, 20000, 0, delegate { ApplyTransformOptions(); });
            _xfYLbl = OptLabel("Y");
            _xfY = OptNumeric(-20000, 20000, 0, delegate { ApplyTransformOptions(); });
            _xfWLbl = OptLabel("W");
            _xfW = OptNumeric(1, 40000, 100, delegate { ApplyTransformOptions(true); });
            _xfLink = OptGlyphButton("link", "Maintain aspect ratio", delegate
            {
                _xfLinked = !_xfLinked;
                StyleToggle(_xfLink, _xfLinked);
            });
            _xfHLbl = OptLabel("H");
            _xfH = OptNumeric(1, 40000, 100, delegate { ApplyTransformOptions(false); });
            _xfALbl = OptLabel("∠");
            _xfA = OptNumeric(-180, 180, 0, delegate { ApplyTransformOptions(); });
            _xfSxLbl = OptLabel("H skew");
            _xfSx = OptNumeric(-75, 75, 0, delegate { ApplyTransformOptions(); });
            _xfSyLbl = OptLabel("V skew");
            _xfSy = OptNumeric(-75, 75, 0, delegate { ApplyTransformOptions(); });
            _xfCancel = OptGlyphButton("cancel", "Cancel transform (Esc)", delegate { CancelTransform(); });
            _xfOk = OptGlyphButton("check", "Commit transform (Enter)", delegate { CommitTransform(); });
            _xfX.DecimalPlaces = _xfY.DecimalPlaces = _xfW.DecimalPlaces = _xfH.DecimalPlaces = 0;
            _xfA.DecimalPlaces = 1;
            _xfX.Width = _xfY.Width = _xfW.Width = _xfH.Width = 74;
            _xfX.Suffix = _xfY.Suffix = _xfW.Suffix = _xfH.Suffix = "px";
            _xfA.Suffix = _xfSx.Suffix = _xfSy.Suffix = "°";
            _xfA.Width = _xfSx.Width = _xfSy.Width = 70;
            StyleToggle(_xfLink, _xfLinked);
        }

        void ShowTransformOptions(bool show)
        {
            if (show)
                ShowOpts(_xfXLbl, _xfX, _xfYLbl, _xfY, _xfWLbl, _xfW, _xfLink, _xfHLbl, _xfH, _xfALbl, _xfA, _xfSxLbl, _xfSx, _xfSyLbl, _xfSy, _xfCancel, _xfOk);
            bool numeric = show && !(_xf != null && _xf.Quad);
            foreach (Control c in new Control[] { _xfX, _xfY, _xfW, _xfH, _xfA, _xfSx, _xfSy, _xfLink })
                c.Enabled = numeric;
            if (show) SyncTransformOptions();
        }

        void SyncTransformOptions()
        {
            if (_xf == null || _xfX == null || !_shownOptions.Contains(_xfX)) return;
            EditorLayer l = _xf.Layer;
            _syncingOptions = true;
            try
            {
                _xfX.Value = Clamp(_xfX, (decimal)Math.Round(l.Bounds.X));
                _xfY.Value = Clamp(_xfY, (decimal)Math.Round(l.Bounds.Y));
                _xfW.Value = Clamp(_xfW, (decimal)Math.Round(l.Bounds.Width));
                _xfH.Value = Clamp(_xfH, (decimal)Math.Round(l.Bounds.Height));
                _xfA.Value = Clamp(_xfA, (decimal)Math.Round(l.RotationDeg, 1));
                _xfSx.Value = Clamp(_xfSx, (decimal)Math.Round(Math.Atan(l.ShearX) * 180 / Math.PI));
                _xfSy.Value = Clamp(_xfSy, (decimal)Math.Round(Math.Atan(l.ShearY) * 180 / Math.PI));
            }
            finally { _syncingOptions = false; }
        }

        static decimal Clamp(ModernNumber n, decimal v)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        }

        void ApplyTransformOptions(bool? widthChanged = null)
        {
            if (_xf == null || _syncingOptions || _xf.Quad) return;
            EnsureLifted();
            EditorLayer l = _xf.Layer;
            _xf.Changed = true;
            float w = (float)_xfW.Value, h = (float)_xfH.Value;
            if (widthChanged.HasValue && _xfLinked && l.Bounds.Width > 0 && l.Bounds.Height > 0)
            {
                float ratio = l.Bounds.Width / l.Bounds.Height;
                if (widthChanged.Value) h = w / ratio; else w = h * ratio;
            }
            var text = l as TextLayer;
            if (text != null && l.Bounds.Height > 0 && Math.Abs(h - l.Bounds.Height) > 0.5f)
                text.FontSize = Math.Max(4f, text.FontSize * h / l.Bounds.Height);
            l.Bounds = new RectangleF((float)_xfX.Value, (float)_xfY.Value, Math.Max(1, w), Math.Max(1, h));
            l.RotationDeg = (float)_xfA.Value;
            l.ShearX = (float)Math.Tan((double)_xfSx.Value * Math.PI / 180);
            l.ShearY = (float)Math.Tan((double)_xfSy.Value * Math.PI / 180);
            SyncTransformOptions();
            InvalidateDoc();
        }
    }
}
