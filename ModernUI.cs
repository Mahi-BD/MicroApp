using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Base for MicroApp's fixed-pixel windows (settings canvases, dialogs, notes).
    /// Their layout is hand-drawn at 96 DPI coordinates, so at 125%/150% display
    /// scaling the window is created DPI-unaware (GDI-scaled) and Windows stretches
    /// it as a whole instead of WinForms half-scaling it. The process itself stays
    /// DPI-aware — capture/paste overlays need real physical pixels.
    /// </summary>
    public class PixelPerfectForm : Form
    {
        protected override void CreateHandle()
        {
            IntPtr prev = IntPtr.Zero;
            bool switched = false;
            try
            {
                prev = Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED);
                if (prev == IntPtr.Zero)  // pre-1809: GDISCALED unknown, fall back to plain bitmap stretch
                    prev = Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_UNAWARE);
                switched = prev != IntPtr.Zero;
            }
            catch (EntryPointNotFoundException) { }  // pre-1607: no per-window DPI, behave as before
            catch (DllNotFoundException) { }

            try { base.CreateHandle(); }
            finally { if (switched) Native.SetThreadDpiAwarenessContext(prev); }
        }
    }

    /// <summary>
    /// Palette + fonts for the modern (Fluent-flavoured) look, in light and dark.
    /// Call <see cref="Init"/> once at startup, then <see cref="Apply"/> per form.
    /// </summary>
    public static class Theme
    {
        public static bool Dark { get; private set; }

        // surfaces
        public static Color Bg { get; private set; }
        public static Color Surface { get; private set; }
        public static Color Border { get; private set; }
        public static Color FieldBg { get; private set; }
        public static Color FieldBorder { get; private set; }

        // ink
        public static Color Text { get; private set; }
        public static Color TextDim { get; private set; }

        // brand
        public static readonly Color Accent = Color.FromArgb(99, 102, 241);        // #6366F1
        public static readonly Color AccentHover = Color.FromArgb(124, 124, 247);
        public static readonly Color AccentPressed = Color.FromArgb(79, 70, 229);
        public static readonly Color OnAccent = Color.White;
        public static readonly Color Danger = Color.FromArgb(244, 63, 94);   // for the one line that says what went wrong

        // fonts (static: created once, never disposed)
        public static Font Base { get; private set; }
        public static Font Strong { get; private set; }
        public static Font Heading { get; private set; }
        public static Font Small { get; private set; }

        public static void Init(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Bg = Color.FromArgb(24, 24, 27);
                Surface = Color.FromArgb(33, 33, 38);
                Border = Color.FromArgb(52, 52, 59);
                FieldBg = Color.FromArgb(41, 41, 47);
                FieldBorder = Color.FromArgb(70, 70, 79);
                Text = Color.FromArgb(240, 240, 244);
                TextDim = Color.FromArgb(154, 154, 166);
            }
            else
            {
                Bg = Color.FromArgb(245, 245, 248);
                Surface = Color.White;
                Border = Color.FromArgb(226, 226, 233);
                FieldBg = Color.White;
                FieldBorder = Color.FromArgb(210, 210, 219);
                Text = Color.FromArgb(27, 27, 31);
                TextDim = Color.FromArgb(110, 110, 122);
            }

            if (Base == null)
            {
                Base = new Font("Segoe UI", 9F);
                Strong = new Font("Segoe UI Semibold", 9.75F);
                Heading = new Font("Segoe UI Semibold", 14F);
                Small = new Font("Segoe UI", 8.25F);
            }
        }

        /// <summary>
        /// Fills a custom-painted control's backdrop. ButtonBase-derived controls
        /// (radio/check) do not paint their own background once UserPaint is on, so
        /// every OnPaint here starts with this. A transparent BackColor is left alone:
        /// the parent surface has already shown through.
        /// </summary>
        public static void PaintBackdrop(Control c, Graphics g)
        {
            if (c.BackColor.A < 255) return;
            // aliased fill: antialiasing here blends the edge row with the stale
            // double-buffer contents and leaves a hairline along the control's top
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var brush = new SolidBrush(c.BackColor))
            {
                g.FillRectangle(brush, c.ClientRectangle);
            }
            g.SmoothingMode = mode;
        }

        /// <summary>Rounded rectangle path, used by every custom control here.</summary>
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Walks the control tree applying theme colours. Children of a <see cref="Card"/>
        /// get the card surface as their background so custom paints blend seamlessly.
        /// </summary>
        public static void Apply(Control root, Color? host = null)
        {
            Color back = host ?? Bg;

            if (root is Form)
            {
                root.BackColor = Bg;
                root.ForeColor = Text;
                root.Font = Base;
            }
            else if (root is Card card)
            {
                card.BackColor = back;   // outside the rounded corners
                back = Surface;          // inside
            }
            else if (root is FieldHost field)
            {
                field.BackColor = back;
                back = FieldBg;
            }
            else if (root is TextBox box)
            {
                box.BorderStyle = BorderStyle.None;
                box.BackColor = FieldBg;
                box.ForeColor = Text;
                box.Font = Base;
            }
            else if (root is ModernButton)
            {
                root.BackColor = back;
            }
            else if (root is Label && (root.Parent is Card || root.Parent is HeaderBar))
            {
                // sits on a custom-painted surface: let the parent's paint show through
                root.BackColor = Color.Transparent;
                root.ForeColor = Text;
            }
            else if (root is Label || root is CheckBox || root is RadioButton || root is Panel)
            {
                root.BackColor = back;
                root.ForeColor = Text;
            }

            foreach (Control child in root.Controls)
            {
                Apply(child, back);
            }
        }

        /// <summary>Win11 rounded window corners; a no-op on Windows 10 and earlier.</summary>
        public static void RoundWindowCorners(IntPtr handle)
        {
            try
            {
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref pref, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    /// <summary>Rounded surface panel with a title and optional description.</summary>
    public class Card : Panel
    {
        private string _title = string.Empty;
        private string _description = string.Empty;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        public string Title
        {
            get { return _title; }
            set { _title = value ?? string.Empty; Invalidate(); }
        }

        public string Description
        {
            get { return _description; }
            set { _description = value ?? string.Empty; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, 10))
            using (var fill = new SolidBrush(Theme.Surface))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            if (_title.Length > 0)
            {
                TextRenderer.DrawText(g, _title, Theme.Strong, new Point(15, 13), Theme.Text,
                                      TextFormatFlags.NoPadding);
            }
            if (_description.Length > 0)
            {
                TextRenderer.DrawText(g, _description, Theme.Small,
                                      new Rectangle(15, 32, Width - 30, 18), Theme.TextDim,
                                      TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>Header strip: accent-tinted wash with a hairline divider underneath.</summary>
    public class HeaderBar : Panel
    {
        public HeaderBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, Width, Height);
            Color tint = Theme.Dark
                ? Color.FromArgb(38, 38, 46)
                : Color.FromArgb(252, 252, 255);
            using (var brush = new LinearGradientBrush(r, tint, Theme.Surface, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, r);
            }
            using (var accent = new SolidBrush(Color.FromArgb(Theme.Dark ? 38 : 22, Theme.Accent)))
            {
                g.FillRectangle(accent, new Rectangle(0, 0, Width, Height));
            }
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    /// <summary>Small rounded chip -- used for the version badge.</summary>
    public class Pill : Label
    {
        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = false;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, Height / 2))
            using (var fill = new SolidBrush(Color.FromArgb(Theme.Dark ? 46 : 26, Theme.Accent)))
            using (var pen = new Pen(Color.FromArgb(Theme.Dark ? 90 : 60, Theme.Accent)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Theme.Small, r,
                                  Theme.Dark ? Color.FromArgb(186, 188, 255) : Theme.AccentPressed,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Flat rounded button, accent (primary) or quiet (secondary).</summary>
    public class ModernButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public ModernButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
        }

        /// <summary>True for the primary action (filled with the accent colour).</summary>
        public bool Accent { get; set; }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, ink, border;

            if (Accent)
            {
                fill = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                ink = Theme.OnAccent;
                border = fill;
            }
            else
            {
                fill = _pressed
                    ? (Theme.Dark ? Color.FromArgb(56, 56, 64) : Color.FromArgb(232, 232, 238))
                    : _hover
                        ? (Theme.Dark ? Color.FromArgb(48, 48, 55) : Color.FromArgb(243, 243, 247))
                        : Theme.Surface;
                ink = Theme.Text;
                border = Theme.Border;
            }
            if (!Enabled)
            {
                fill = Theme.Dark ? Color.FromArgb(40, 40, 46) : Color.FromArgb(238, 238, 242);
                ink = Theme.TextDim;
                border = Theme.Border;
            }

            using (var path = Theme.Round(r, 6))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
                if (Focused && ShowFocusCues)
                {
                    using (var focus = new Pen(Accent ? Theme.OnAccent : Theme.Accent) { DashStyle = DashStyle.Dot })
                    using (var inner = Theme.Round(Rectangle.Inflate(r, -3, -3), 4))
                    {
                        g.DrawPath(focus, inner);
                    }
                }
            }

            TextRenderer.DrawText(g, Text, Theme.Strong, r, ink,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Radio button drawn as a modern ring + dot.</summary>
    public class ModernRadioButton : RadioButton
    {
        private bool _hover;

        public ModernRadioButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const int size = 17;
            int top = (Height - size) / 2;
            var box = new Rectangle(1, top, size, size);

            if (Checked)
            {
                using (var fill = new SolidBrush(_hover ? Theme.AccentHover : Theme.Accent))
                {
                    g.FillEllipse(fill, box);
                }
                var dot = Rectangle.Inflate(box, -5, -5);
                using (var white = new SolidBrush(Theme.OnAccent))
                {
                    g.FillEllipse(white, dot);
                }
            }
            else
            {
                using (var fill = new SolidBrush(Theme.FieldBg))
                using (var pen = new Pen(_hover ? Theme.Accent : Theme.FieldBorder, 1.4f))
                {
                    g.FillEllipse(fill, box);
                    g.DrawEllipse(pen, box);
                }
            }

            var textRect = new Rectangle(size + 10, 0, Width - size - 10, Height);
            TextRenderer.DrawText(g, Text, Theme.Base, textRect, Enabled ? Theme.Text : Theme.TextDim,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                  TextFormatFlags.EndEllipsis);

            // keyboard focus only -- a ring on every click reads as a text field
            if (Focused && ShowFocusCues)
            {
                using (var focus = new Pen(Theme.TextDim) { DashStyle = DashStyle.Dot })
                {
                    var textSize = TextRenderer.MeasureText(Text, Theme.Base);
                    g.DrawRectangle(focus, new Rectangle(textRect.X - 3, (Height - textSize.Height) / 2 - 2,
                                                         Math.Min(textSize.Width + 5, textRect.Width), textSize.Height + 3));
                }
            }
        }
    }

    /// <summary>Check box drawn as a modern rounded square with a stroked tick.</summary>
    public class ModernCheckBox : CheckBox
    {
        private bool _hover;

        public ModernCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const int size = 17;
            int top = (Height - size) / 2;
            var box = new Rectangle(1, top, size, size);

            using (var path = Theme.Round(box, 4))
            {
                if (Checked)
                {
                    using (var fill = new SolidBrush(_hover ? Theme.AccentHover : Theme.Accent))
                    {
                        g.FillPath(fill, path);
                    }
                    using (var tick = new Pen(Theme.OnAccent, 1.9f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round
                    })
                    {
                        g.DrawLines(tick, new[]
                        {
                            new PointF(box.X + 4.2f, box.Y + 8.6f),
                            new PointF(box.X + 7.2f, box.Y + 11.8f),
                            new PointF(box.X + 12.8f, box.Y + 5.2f)
                        });
                    }
                }
                else
                {
                    using (var fill = new SolidBrush(Theme.FieldBg))
                    using (var pen = new Pen(_hover ? Theme.Accent : Theme.FieldBorder, 1.4f))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path);
                    }
                }
            }

            var textRect = new Rectangle(size + 10, 0, Width - size - 10, Height);
            TextRenderer.DrawText(g, Text, Theme.Base, textRect, Enabled ? Theme.Text : Theme.TextDim,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                  TextFormatFlags.EndEllipsis);

            // keyboard focus only -- a ring on every click reads as a text field
            if (Focused && ShowFocusCues)
            {
                using (var focus = new Pen(Theme.TextDim) { DashStyle = DashStyle.Dot })
                {
                    var textSize = TextRenderer.MeasureText(Text, Theme.Base);
                    g.DrawRectangle(focus, new Rectangle(textRect.X - 3, (Height - textSize.Height) / 2 - 2,
                                                         Math.Min(textSize.Width + 5, textRect.Width), textSize.Height + 3));
                }
            }
        }
    }

    /// <summary>
    /// Rounded input container. Hosts a borderless <see cref="TextBox"/> and draws the
    /// field chrome itself, including the accent focus ring.
    /// </summary>
    public class FieldHost : Panel
    {
        private Control _inner;

        public FieldHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.IBeam;
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control is TextBox || e.Control is ComboBox)
            {
                _inner = e.Control;
                if (_inner is TextBox box) box.BorderStyle = BorderStyle.None;
                if (_inner is ComboBox combo)
                {
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                }
                _inner.Enter += (s, a) => Invalidate();
                _inner.Leave += (s, a) => Invalidate();
                _inner.EnabledChanged += (s, a) => Invalidate();
                LayoutInner();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _inner?.Focus();
        }

        private void LayoutInner()
        {
            if (_inner == null) return;

            var multiline = _inner as TextBoxBase;
            if (multiline != null && multiline.Multiline)
            {
                // text area: fill the host, inset by the chrome
                _inner.SetBounds(10, 8, Math.Max(10, Width - 20), Math.Max(10, Height - 16));
                return;
            }

            _inner.Width = Math.Max(10, Width - 20);
            _inner.Left = 10;
            _inner.Top = Math.Max(0, (Height - _inner.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool focused = _inner != null && _inner.Focused;
            bool enabled = _inner == null || _inner.Enabled;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill = enabled
                ? Theme.FieldBg
                : (Theme.Dark ? Color.FromArgb(34, 34, 39) : Color.FromArgb(240, 240, 244));
            if (_inner != null && _inner.BackColor != fill) _inner.BackColor = fill;
            if (_inner != null) _inner.ForeColor = enabled ? Theme.Text : Theme.TextDim;

            using (var path = Theme.Round(r, 6))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(focused ? Theme.Accent : Theme.FieldBorder, focused ? 1.6f : 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Tray menu renderer that follows the app theme (light or dark).</summary>
    public class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        public ModernMenuRenderer() : base(new ModernMenuColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // the same handler paints the label and, in a second call, the hot key hint
            var menuItem = e.Item as ToolStripMenuItem;
            bool isShortcut = menuItem != null &&
                              !string.IsNullOrEmpty(menuItem.ShortcutKeyDisplayString) &&
                              e.Text == menuItem.ShortcutKeyDisplayString;
            e.TextColor = isShortcut ? Theme.TextDim : Theme.Text;
            e.TextFont = isShortcut ? Theme.Small : Theme.Base;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(3, 1, e.Item.Width - 7, e.Item.Height - 2);
            using (var path = Theme.Round(r, 5))
            using (var brush = new SolidBrush(Color.FromArgb(Theme.Dark ? 70 : 32, Theme.Accent)))
            {
                g.FillPath(brush, path);
            }
        }
    }

    public class ModernMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}
