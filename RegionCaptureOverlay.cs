using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Optional shape restriction for a capture: a locked aspect ratio, or an exact
    /// pixel size. Pixel lock wins when both are set -- a fixed size already fixes
    /// the ratio.
    /// </summary>
    public class CaptureConstraint
    {
        public bool LockRatio { get; set; }
        public double Ratio { get; set; }          // width / height, e.g. 16:9 -> 1.777
        public string RatioName { get; set; }

        public bool LockPixel { get; set; }
        public Size PixelSize { get; set; }

        public static readonly CaptureConstraint None = new CaptureConstraint();

        /// <summary>Builds the constraint that the user configured in Capture Setting.</summary>
        public static CaptureConstraint FromSettings()
        {
            var c = new CaptureConstraint
            {
                LockPixel = Properties.Settings.Default.LockPixel,
                PixelSize = new Size(Math.Max(8, Properties.Settings.Default.PixelWidth),
                                     Math.Max(8, Properties.Settings.Default.PixelHeight)),
                LockRatio = Properties.Settings.Default.LockRatio,
                RatioName = Properties.Settings.Default.RatioPreset
            };
            c.Ratio = ParseRatio(c.RatioName);
            if (c.Ratio <= 0)
            {
                c.LockRatio = false;
            }
            return c;
        }

        /// <summary>The constraint configured for GIF recording, which is kept separate.</summary>
        public static CaptureConstraint FromGifSettings()
        {
            var c = new CaptureConstraint
            {
                LockPixel = Properties.Settings.Default.GifLockPixel,
                PixelSize = new Size(Math.Max(8, Properties.Settings.Default.GifPixelWidth),
                                     Math.Max(8, Properties.Settings.Default.GifPixelHeight)),
                LockRatio = Properties.Settings.Default.GifLockRatio,
                RatioName = Properties.Settings.Default.GifRatioPreset
            };
            c.Ratio = ParseRatio(c.RatioName);
            if (c.Ratio <= 0)
            {
                c.LockRatio = false;
            }
            return c;
        }

        /// <summary>The constraint configured for video recording, which is kept separate.</summary>
        public static CaptureConstraint FromVideoSettings()
        {
            var c = new CaptureConstraint
            {
                LockPixel = Properties.Settings.Default.VideoLockPixel,
                PixelSize = new Size(Math.Max(8, Properties.Settings.Default.VideoPixelWidth),
                                     Math.Max(8, Properties.Settings.Default.VideoPixelHeight)),
                LockRatio = Properties.Settings.Default.VideoLockRatio,
                RatioName = Properties.Settings.Default.VideoRatioPreset
            };
            c.Ratio = ParseRatio(c.RatioName);
            if (c.Ratio <= 0)
            {
                c.LockRatio = false;
            }
            return c;
        }

        /// <summary>Parses "16:9" (or "16x9", "1.5") into a width/height factor.</summary>
        public static double ParseRatio(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var parts = text.Split(':', 'x', 'X', '/');
            double w, h;
            if (parts.Length == 2 &&
                double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out w) &&
                double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out h) &&
                h > 0 && w > 0)
            {
                return w / h;
            }
            double single;
            if (double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out single) && single > 0)
            {
                return single;
            }
            return 0;
        }
    }

    /// <summary>
    /// Snipping-style region picker. Freezes the screen, dims it, turns the cursor
    /// into a crosshair and lets the user drag a rectangle over anything on screen --
    /// a browser, a PDF, a video frame, a remote desktop window. Because the picker
    /// works on a frozen copy of the screen, the dimming never ends up in the capture.
    /// </summary>
    public class RegionCaptureOverlay : Form
    {
        private const int MinSide = 8;         // smallest frame the user can resize down to
        private const int HandleSize = 10;     // drawn size of a resize grip
        private const int HandleGrab = 16;     // its (larger) hit area
        private const int ButtonSize = 30;     // the take / cancel buttons

        private readonly Bitmap _screen;       // frozen copy of the whole virtual desktop
        private readonly Rectangle _virtual;   // its position in desktop coordinates
        private readonly CaptureConstraint _constraint;
        private readonly string _hint;
        private readonly bool _adjustable;     // let the frame be moved and resized before it is taken

        private Point _anchor;
        private Rectangle _selection = Rectangle.Empty;
        private bool _dragging;

        private bool _adjusting;               // a frame is placed and waiting to be confirmed
        private int _grab = -1;                // 0..7 = a handle, 8 = the whole frame, -1 = nothing
        private Point _grabPoint;
        private Rectangle _grabStart;
        private Rectangle _okButton, _cancelButton;
        private int _hotButton = -1;           // 0 = take, 1 = cancel

        /// <summary>The captured region, or null when the user cancelled.</summary>
        public Bitmap Captured { get; private set; }

        /// <summary>Where the selection sat, in overlay coordinates.</summary>
        public Rectangle Selection { get { return _selection; } }

        /// <summary>
        /// Desktop-coordinate rectangle of the most recent successful selection.
        /// GIF recording needs the location, not just the pixels.
        /// </summary>
        public static Rectangle LastRegion { get; private set; }

        private RegionCaptureOverlay(Bitmap screen, Rectangle bounds, CaptureConstraint constraint,
                                     string hint, bool adjustable)
        {
            _screen = screen;
            _virtual = bounds;
            _constraint = constraint ?? CaptureConstraint.None;
            _hint = hint;
            // a fixed pixel size has nothing to adjust -- the box already follows the pointer
            _adjustable = adjustable && !_constraint.LockPixel;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            Cursor = Cursors.Cross;      // the "+" pointer
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "MicroApp capture";
        }

        /// <summary>
        /// Runs the picker modally. Returns the captured bitmap, or null if the user
        /// pressed Esc / right-clicked / selected nothing.
        /// </summary>
        public static Bitmap SelectRegion()
        {
            return SelectRegion(CaptureConstraint.None, "Drag over the text you want.   Esc cancels.", false);
        }

        /// <summary>
        /// Same picker, restricted to a locked ratio or a fixed pixel size. With a
        /// pixel lock the box simply follows the pointer and one click takes it.
        /// When <paramref name="adjustable"/> is set, letting go of the mouse leaves
        /// the frame on screen to be moved and resized, and it is taken on Enter (or
        /// the tick button) instead of straight away.
        /// </summary>
        public static Bitmap SelectRegion(CaptureConstraint constraint, string hint, bool adjustable)
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            Bitmap screen = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(screen))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            using (var overlay = new RegionCaptureOverlay(screen, bounds, constraint, hint, adjustable))
            {
                overlay.ShowDialog();
                var captured = overlay.Captured;
                if (captured != null)
                {
                    var sel = overlay.Selection;
                    LastRegion = new Rectangle(bounds.X + sel.X, bounds.Y + sel.Y, sel.Width, sel.Height);
                }
                screen.Dispose();
                return captured;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Captured = null;
                Close();
            }
        }

        /// <summary>
        /// While a frame is waiting to be confirmed, Enter takes it and the arrow keys
        /// nudge it -- Ctrl for a bigger step, Shift to resize instead of move.
        /// Arrows never reach OnKeyDown on a bare form, hence ProcessCmdKey.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_adjusting) return base.ProcessCmdKey(ref msg, keyData);

            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;

            if (key == Keys.Enter || key == Keys.Space)
            {
                Confirm();
                return true;
            }

            int step = ctrl ? 10 : 1;
            var delta = Point.Empty;
            if (key == Keys.Left) delta = new Point(-step, 0);
            else if (key == Keys.Right) delta = new Point(step, 0);
            else if (key == Keys.Up) delta = new Point(0, -step);
            else if (key == Keys.Down) delta = new Point(0, step);
            else return base.ProcessCmdKey(ref msg, keyData);

            _selection = shift ? ResizeTo(_selection, 4, delta) : MoveTo(_selection, delta);
            LayoutButtons();
            Invalidate();
            return true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                Captured = null;
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_constraint.LockPixel)
            {
                // fixed size: the box already follows the pointer, one click takes it
                _selection = FixedBoxAt(e.Location);
                Captured = _screen.Clone(_selection, _screen.PixelFormat);
                Close();
                return;
            }

            if (_adjusting)
            {
                if (_okButton.Contains(e.Location)) { Confirm(); return; }
                if (_cancelButton.Contains(e.Location)) { Captured = null; Close(); return; }

                int handle = HitHandle(e.Location);
                if (handle < 0 && _selection.Contains(e.Location)) handle = 8;   // move the whole frame
                if (handle >= 0)
                {
                    _grab = handle;
                    _grabPoint = e.Location;
                    _grabStart = _selection;
                    return;
                }
                _adjusting = false;      // a drag outside the frame starts over
            }

            _anchor = e.Location;
            _selection = new Rectangle(e.X, e.Y, 0, 0);
            _dragging = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_constraint.LockPixel)
            {
                _selection = FixedBoxAt(e.Location);
                Invalidate();
                return;
            }

            if (_grab >= 0)
            {
                var delta = new Point(e.X - _grabPoint.X, e.Y - _grabPoint.Y);
                _selection = _grab == 8 ? MoveTo(_grabStart, delta) : ResizeTo(_grabStart, _grab, delta);
                LayoutButtons();
                Invalidate();
                return;
            }

            if (_dragging)
            {
                _selection = Normalize(_anchor, e.Location);
                Invalidate();
                return;
            }

            if (_adjusting) TrackHover(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (_grab >= 0)
            {
                _grab = -1;
                TrackHover(e.Location);
                return;
            }
            if (!_dragging) return;

            _dragging = false;
            _selection = Normalize(_anchor, e.Location);

            // a click without a drag is a cancel, not a 1px capture
            if (_selection.Width < 4 || _selection.Height < 4)
            {
                Captured = null;
                Close();
                return;
            }

            if (_adjustable)
            {
                // hand the frame over to be moved and resized; Enter or the tick takes it
                _adjusting = true;
                LayoutButtons();
                TrackHover(e.Location);
                Invalidate();
                return;
            }

            Confirm();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_adjusting && e.Button == MouseButtons.Left && _selection.Contains(e.Location)) Confirm();
        }

        /// <summary>Takes the frame as it stands and ends the picker.</summary>
        private void Confirm()
        {
            var rect = Rectangle.Intersect(_selection, new Rectangle(Point.Empty, _screen.Size));
            if (rect.Width < 4 || rect.Height < 4)
            {
                Captured = null;
                Close();
                return;
            }
            _selection = rect;
            Captured = _screen.Clone(rect, _screen.PixelFormat);
            Close();
        }

        /// <summary>Keeps the cursor and the button highlight in step with the pointer.</summary>
        private void TrackHover(Point p)
        {
            int button = _okButton.Contains(p) ? 0 : _cancelButton.Contains(p) ? 1 : -1;
            if (button != _hotButton)
            {
                _hotButton = button;
                Invalidate();
            }

            if (button >= 0) { Cursor = Cursors.Hand; return; }
            int handle = HitHandle(p);
            if (handle < 0 && _selection.Contains(p)) handle = 8;
            Cursor = CursorFor(handle);
        }

        private Rectangle Normalize(Point a, Point b)
        {
            int w = Math.Abs(a.X - b.X);
            int h = Math.Abs(a.Y - b.Y);

            if (_constraint.LockRatio && _constraint.Ratio > 0)
            {
                // grow the short side so the box always sits on the locked ratio
                if (w / _constraint.Ratio >= h) h = (int)Math.Round(w / _constraint.Ratio);
                else w = (int)Math.Round(h * _constraint.Ratio);
            }

            int x = b.X >= a.X ? a.X : a.X - w;
            int y = b.Y >= a.Y ? a.Y : a.Y - h;

            var r = new Rectangle(x, y, w, h);
            return Rectangle.Intersect(r, new Rectangle(Point.Empty, _screen.Size));
        }

        /// <summary>
        /// Where handle <paramref name="i"/> sits: 0..7 clockwise from the top left
        /// corner, odd numbers being the middles of the edges.
        /// </summary>
        private Point HandleAt(int i)
        {
            var s = _selection;
            int mx = s.Left + s.Width / 2, my = s.Top + s.Height / 2;
            switch (i)
            {
                case 0: return new Point(s.Left, s.Top);
                case 1: return new Point(mx, s.Top);
                case 2: return new Point(s.Right, s.Top);
                case 3: return new Point(s.Right, my);
                case 4: return new Point(s.Right, s.Bottom);
                case 5: return new Point(mx, s.Bottom);
                case 6: return new Point(s.Left, s.Bottom);
                default: return new Point(s.Left, my);
            }
        }

        private static Rectangle Around(Point p, int size)
        {
            return new Rectangle(p.X - size / 2, p.Y - size / 2, size, size);
        }

        /// <summary>A small frame only gets its corners, or the handles would overlap.</summary>
        private bool EdgeHandles { get { return _selection.Width >= 48 && _selection.Height >= 48; } }

        private int HitHandle(Point p)
        {
            if (!_adjusting) return -1;
            for (int i = 0; i < 8; i++)
            {
                if (!EdgeHandles && (i % 2) == 1) continue;
                if (Around(HandleAt(i), HandleGrab).Contains(p)) return i;
            }
            return -1;
        }

        private static Cursor CursorFor(int handle)
        {
            switch (handle)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                case 3: case 7: return Cursors.SizeWE;
                case 8: return Cursors.SizeAll;
                default: return Cursors.Cross;
            }
        }

        /// <summary>Slides the frame by <paramref name="delta"/>, never off the desktop.</summary>
        private Rectangle MoveTo(Rectangle start, Point delta)
        {
            int x = Math.Max(0, Math.Min(start.X + delta.X, _screen.Width - start.Width));
            int y = Math.Max(0, Math.Min(start.Y + delta.Y, _screen.Height - start.Height));
            return new Rectangle(x, y, start.Width, start.Height);
        }

        /// <summary>
        /// Drags one handle of <paramref name="start"/> by <paramref name="delta"/>.
        /// A locked ratio is honoured, and a move that cannot honour it -- because the
        /// box would leave the screen -- is simply ignored.
        /// </summary>
        private Rectangle ResizeTo(Rectangle start, int handle, Point delta)
        {
            bool west = handle == 0 || handle == 6 || handle == 7;
            bool east = handle == 2 || handle == 3 || handle == 4;
            bool north = handle == 0 || handle == 1 || handle == 2;
            bool south = handle == 4 || handle == 5 || handle == 6;

            int l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
            if (west) l = Math.Min(start.Right - MinSide, l + delta.X);
            if (east) r = Math.Max(start.Left + MinSide, r + delta.X);
            if (north) t = Math.Min(start.Bottom - MinSide, t + delta.Y);
            if (south) b = Math.Max(start.Top + MinSide, b + delta.Y);

            var rect = Rectangle.FromLTRB(l, t, r, b);
            var bounds = new Rectangle(Point.Empty, _screen.Size);

            if (_constraint.LockRatio && _constraint.Ratio > 0)
            {
                rect = ApplyRatio(rect, handle);
                if (!bounds.Contains(rect)) return _selection;
            }
            else
            {
                rect = Rectangle.Intersect(rect, bounds);
            }

            if (rect.Width < MinSide || rect.Height < MinSide) return _selection;
            return rect;
        }

        /// <summary>
        /// Pulls a rectangle back onto the locked ratio, keeping whichever edge or
        /// corner the user is not dragging where it is.
        /// </summary>
        private Rectangle ApplyRatio(Rectangle r, int handle)
        {
            bool horizontal = handle == 3 || handle == 7;   // a side handle: height follows width
            bool vertical = handle == 1 || handle == 5;     // a top/bottom handle: width follows height

            int w = r.Width, h = r.Height;
            if (vertical) w = (int)Math.Round(h * _constraint.Ratio);
            else h = (int)Math.Round(w / _constraint.Ratio);
            w = Math.Max(MinSide, w);
            h = Math.Max(MinSide, h);

            int x = (handle == 0 || handle == 6 || handle == 7) ? r.Right - w : r.Left;
            int y = (handle == 0 || handle == 1 || handle == 2) ? r.Bottom - h : r.Top;
            if (vertical) x = r.Left + (r.Width - w) / 2;      // grow either way about the centre
            if (horizontal) y = r.Top + (r.Height - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        /// <summary>Places the take / cancel buttons under the frame, or inside it when there is no room.</summary>
        private void LayoutButtons()
        {
            const int gap = 8;
            int width = ButtonSize * 2 + gap;
            int x = _selection.Right - width;
            int y = _selection.Bottom + gap;
            if (y + ButtonSize > _screen.Height) y = _selection.Bottom - ButtonSize - gap;
            x = Math.Max(0, Math.Min(x, _screen.Width - width));
            y = Math.Max(0, Math.Min(y, _screen.Height - ButtonSize));

            _okButton = new Rectangle(x, y, ButtonSize, ButtonSize);
            _cancelButton = new Rectangle(x + ButtonSize + gap, y, ButtonSize, ButtonSize);
        }

        /// <summary>Fixed-size box centred on the pointer, kept inside the desktop.</summary>
        private Rectangle FixedBoxAt(Point p)
        {
            var size = _constraint.PixelSize;
            size.Width = Math.Min(size.Width, _screen.Width);
            size.Height = Math.Min(size.Height, _screen.Height);

            int x = p.X - size.Width / 2;
            int y = p.Y - size.Height / 2;
            x = Math.Max(0, Math.Min(x, _screen.Width - size.Width));
            y = Math.Max(0, Math.Min(y, _screen.Height - size.Height));
            return new Rectangle(x, y, size.Width, size.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(_screen, 0, 0);

            using (var dim = new SolidBrush(Color.FromArgb(140, 12, 12, 16)))
            {
                if (_selection.Width < 1 || _selection.Height < 1)
                {
                    g.FillRectangle(dim, ClientRectangle);
                }
                else
                {
                    // dim everything except the live selection
                    g.FillRectangle(dim, new Rectangle(0, 0, Width, _selection.Top));
                    g.FillRectangle(dim, new Rectangle(0, _selection.Bottom, Width, Height - _selection.Bottom));
                    g.FillRectangle(dim, new Rectangle(0, _selection.Top, _selection.Left, _selection.Height));
                    g.FillRectangle(dim, new Rectangle(_selection.Right, _selection.Top,
                                                       Width - _selection.Right, _selection.Height));

                    using (var pen = new Pen(Theme.Accent, 2f))
                    {
                        g.DrawRectangle(pen, _selection.X, _selection.Y,
                                        Math.Max(1, _selection.Width - 1), Math.Max(1, _selection.Height - 1));
                    }
                    if (_adjusting)
                    {
                        DrawHandles(g);
                        DrawMoveGlyph(g);
                        DrawButtons(g);
                    }
                    DrawSizeBadge(g);
                }
            }

            if (_selection.Width < 1)
            {
                DrawHint(g);
            }
        }

        /// <summary>The grips that resize the frame: every corner, plus the edges when it is big enough.</summary>
        private void DrawHandles(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            using (var fill = new SolidBrush(Color.White))
            using (var pen = new Pen(Theme.Accent, 2f))
            {
                for (int i = 0; i < 8; i++)
                {
                    if (!EdgeHandles && (i % 2) == 1) continue;
                    var r = Around(HandleAt(i), HandleSize);
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(pen, r);
                }
            }
        }

        /// <summary>The four-way arrow in the middle, saying the frame can be dragged.</summary>
        private void DrawMoveGlyph(Graphics g)
        {
            if (_selection.Width < 96 || _selection.Height < 80) return;

            int cx = _selection.Left + _selection.Width / 2;
            int cy = _selection.Top + _selection.Height / 2;
            const int arm = 12, head = 5;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new Rectangle(cx - 21, cy - 21, 42, 42), 12))
            using (var shade = new SolidBrush(Color.FromArgb(110, 10, 10, 14)))
            {
                g.FillPath(shade, path);
            }
            using (var pen = new Pen(Color.FromArgb(238, 255, 255, 255), 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                g.DrawLine(pen, cx, cy - arm, cx, cy + arm);

                g.DrawLine(pen, cx - arm, cy, cx - arm + head, cy - head);
                g.DrawLine(pen, cx - arm, cy, cx - arm + head, cy + head);
                g.DrawLine(pen, cx + arm, cy, cx + arm - head, cy - head);
                g.DrawLine(pen, cx + arm, cy, cx + arm - head, cy + head);
                g.DrawLine(pen, cx, cy - arm, cx - head, cy - arm + head);
                g.DrawLine(pen, cx, cy - arm, cx + head, cy - arm + head);
                g.DrawLine(pen, cx, cy + arm, cx - head, cy + arm - head);
                g.DrawLine(pen, cx, cy + arm, cx + head, cy + arm - head);
            }
        }

        /// <summary>Take it / drop it, next to the frame, with the keyboard way of doing the same.</summary>
        private void DrawButtons(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawButton(g, _okButton, true, _hotButton == 0);
            DrawButton(g, _cancelButton, false, _hotButton == 1);

            string hint = _selection.Width >= 300
                ? "Drag to move, handles resize   -   Enter takes it"
                : "Enter takes it";
            var size = TextRenderer.MeasureText(hint, Theme.Small);
            var box = new Rectangle(_okButton.Left - size.Width - 22, _okButton.Y + (ButtonSize - size.Height - 8) / 2,
                                    size.Width + 14, size.Height + 8);
            if (box.X < 2) return;      // no room beside the buttons, leave the hint off

            using (var path = Theme.Round(box, 4))
            using (var fill = new SolidBrush(Color.FromArgb(215, 20, 20, 24)))
            {
                g.FillPath(fill, path);
            }
            TextRenderer.DrawText(g, hint, Theme.Small, box, Color.FromArgb(235, 255, 255, 255),
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawButton(Graphics g, Rectangle r, bool accept, bool hot)
        {
            Color back = accept
                ? (hot ? Theme.Accent : Color.FromArgb(235, Theme.Accent))
                : (hot ? Color.FromArgb(240, 58, 58, 66) : Color.FromArgb(225, 32, 32, 38));

            using (var fill = new SolidBrush(back))
            using (var edge = new Pen(Color.FromArgb(120, 255, 255, 255)))
            {
                g.FillEllipse(fill, r);
                g.DrawEllipse(edge, r);
            }

            using (var pen = new Pen(Color.White, 2.2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
                if (accept)
                {
                    g.DrawLines(pen, new[]
                    {
                        new Point(cx - 7, cy),
                        new Point(cx - 2, cy + 5),
                        new Point(cx + 7, cy - 5)
                    });
                }
                else
                {
                    g.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy + 6);
                    g.DrawLine(pen, cx + 6, cy - 6, cx - 6, cy + 6);
                }
            }
        }

        private void DrawSizeBadge(Graphics g)
        {
            string text = $"{_selection.Width} x {_selection.Height}";
            if (_constraint.LockPixel) text += "  locked size";
            else if (_constraint.LockRatio) text += $"  locked {_constraint.RatioName}";
            var size = TextRenderer.MeasureText(text, Theme.Small);
            int x = _selection.X;
            int y = _selection.Y - size.Height - 10;
            if (y < 4) y = _selection.Bottom + 6;

            var box = new Rectangle(x, y, size.Width + 14, size.Height + 8);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(box, 4))
            using (var fill = new SolidBrush(Color.FromArgb(230, Theme.Accent)))
            {
                g.FillPath(fill, path);
            }
            TextRenderer.DrawText(g, text, Theme.Small, box, Color.White,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawHint(Graphics g)
        {
            string hint = _hint ?? "Drag to select.   Esc cancels.";
            var screen = Screen.FromPoint(Cursor.Position).Bounds;
            var local = new Rectangle(screen.X - _virtual.X, screen.Y - _virtual.Y, screen.Width, screen.Height);

            var size = TextRenderer.MeasureText(hint, Theme.Base);
            var box = new Rectangle(local.X + (local.Width - size.Width) / 2 - 18,
                                    local.Y + local.Height / 2 - 22,
                                    size.Width + 36, size.Height + 20);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(box, 8))
            using (var fill = new SolidBrush(Color.FromArgb(225, 20, 20, 24)))
            using (var pen = new Pen(Color.FromArgb(120, Theme.Accent)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, hint, Theme.Base, box, Color.White,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
