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
        private readonly Bitmap _screen;       // frozen copy of the whole virtual desktop
        private readonly Rectangle _virtual;   // its position in desktop coordinates
        private readonly CaptureConstraint _constraint;
        private readonly string _hint;

        private Point _anchor;
        private Rectangle _selection = Rectangle.Empty;
        private bool _dragging;

        /// <summary>The captured region, or null when the user cancelled.</summary>
        public Bitmap Captured { get; private set; }

        /// <summary>Where the selection sat, in overlay coordinates.</summary>
        public Rectangle Selection { get { return _selection; } }

        /// <summary>
        /// Desktop-coordinate rectangle of the most recent successful selection.
        /// GIF recording needs the location, not just the pixels.
        /// </summary>
        public static Rectangle LastRegion { get; private set; }

        private RegionCaptureOverlay(Bitmap screen, Rectangle bounds, CaptureConstraint constraint, string hint)
        {
            _screen = screen;
            _virtual = bounds;
            _constraint = constraint ?? CaptureConstraint.None;
            _hint = hint;

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
            return SelectRegion(CaptureConstraint.None, "Drag over the text you want.   Esc cancels.");
        }

        /// <summary>
        /// Same picker, restricted to a locked ratio or a fixed pixel size. With a
        /// pixel lock the box simply follows the pointer and one click takes it.
        /// </summary>
        public static Bitmap SelectRegion(CaptureConstraint constraint, string hint)
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            Bitmap screen = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(screen))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            using (var overlay = new RegionCaptureOverlay(screen, bounds, constraint, hint))
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
            if (!_dragging) return;
            _selection = Normalize(_anchor, e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging || e.Button != MouseButtons.Left) return;

            _dragging = false;
            _selection = Normalize(_anchor, e.Location);

            // a click without a drag is a cancel, not a 1px capture
            if (_selection.Width < 4 || _selection.Height < 4)
            {
                Captured = null;
                Close();
                return;
            }

            Captured = _screen.Clone(_selection, _screen.PixelFormat);
            Close();
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
                    DrawSizeBadge(g);
                }
            }

            if (_selection.Width < 1)
            {
                DrawHint(g);
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
