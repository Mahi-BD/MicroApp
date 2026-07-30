using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Where notes live: a Notes folder next to the exe when that is writable (portable
    /// use), otherwise under %AppData%\MicroApp\Notes. One .txt file per note.
    /// </summary>
    public static class NoteStore
    {
        private static string _folder;

        public static string Folder
        {
            get
            {
                if (_folder != null) return _folder;
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Notes");
                try
                {
                    Directory.CreateDirectory(local);
                    string probe = Path.Combine(local, ".write-probe");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    _folder = local;
                }
                catch (Exception)
                {
                    _folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "MicroApp", "Notes");
                    Directory.CreateDirectory(_folder);
                }
                return _folder;
            }
        }

        public static string NewPath()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string path = Path.Combine(Folder, "Note-" + stamp + ".txt");
            int extra = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(Folder, "Note-" + stamp + "-" + extra + ".txt");
                extra++;
            }
            return path;
        }
    }

    public struct SpellError
    {
        public int Start;
        public int Length;
        public string Word;
    }

    /// <summary>
    /// The note's text box: a RichTextBox that only ever holds plain text (paste is
    /// flattened) and paints red squiggles under the ranges the spell checker flagged.
    /// </summary>
    public class SpellBox : RichTextBox
    {
        private const int WM_PAINT = 0x000F;
        private List<SpellError> _errors = new List<SpellError>();

        public IList<SpellError> Errors { get { return _errors; } }

        public void SetErrors(List<SpellError> errors)
        {
            errors = errors ?? new List<SpellError>();
            if (SameErrors(_errors, errors)) return;   // nothing moved: no repaint, no flicker
            _errors = errors;
            Invalidate();
        }

        private static bool SameErrors(List<SpellError> a, List<SpellError> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Start != b[i].Start || a[i].Length != b[i].Length) return false;
            }
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // paste as plain text, always
            if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert))
            {
                if (Clipboard.ContainsText()) SelectedText = Clipboard.GetText();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && _errors.Count > 0) DrawSquiggles();
        }

        private void DrawSquiggles()
        {
            using (var g = CreateGraphics())
            using (var pen = new Pen(Color.FromArgb(230, 70, 70), 1.4f))
            {
                int lineHeight = Font.Height;
                foreach (var error in _errors)
                {
                    if (error.Start >= TextLength) continue;
                    Point start = GetPositionFromCharIndex(error.Start);
                    int endIndex = error.Start + error.Length;
                    int endX;
                    if (endIndex < TextLength)
                    {
                        Point end = GetPositionFromCharIndex(endIndex);
                        endX = end.Y == start.Y ? end.X : ClientSize.Width - 4;
                    }
                    else
                    {
                        Point last = GetPositionFromCharIndex(TextLength - 1);
                        endX = last.Y == start.Y ? last.X + lineHeight / 2 : ClientSize.Width - 4;
                    }

                    int y = start.Y + lineHeight;
                    if (y < 0 || y > ClientSize.Height || endX <= start.X) continue;

                    // a small zigzag from start to end
                    int x = start.X;
                    bool up = true;
                    var points = new List<Point>();
                    while (x <= endX)
                    {
                        points.Add(new Point(x, up ? y : y - 2));
                        up = !up;
                        x += 3;
                    }
                    if (points.Count > 1) g.DrawLines(pen, points.ToArray());
                }
            }
        }
    }

    /// <summary>A small flat toolbar button with a hand-drawn glyph, themed like the rest of the app.</summary>
    public class NoteToolButton : Control
    {
        public enum Glyph { NoSpaces, NoNewlines, ShortDate, LongDate, Timestamp, NewNote, List, Gear, CloseAll, Trash, Language, Send, Undo, Redo, SmallerText, BiggerText }

        private readonly Glyph _glyph;
        private bool _hover;

        // Fluent system icons (Segoe MDL2 Assets ships with Windows 10).
        private static readonly Font IconFont = new Font("Segoe MDL2 Assets", 10F);

        private static string FluentGlyph(Glyph glyph)
        {
            switch (glyph)
            {
                case Glyph.NewNote: return "\uE70B";    // QuickNote
                case Glyph.List: return "\uE8FD";       // BulletedList
                case Glyph.ShortDate: return "\uE8BF";  // CalendarDay
                case Glyph.LongDate: return "\uE787";   // Calendar
                case Glyph.Timestamp: return "\uE823";  // Recent (clock)
                case Glyph.Gear: return "\uE713";       // Settings
                case Glyph.CloseAll: return "\uE8BB";   // ChromeClose
                case Glyph.Trash: return "\uE74D";      // Delete
                case Glyph.Send: return "\uE724";       // Send
                case Glyph.Undo: return "\uE7A7";       // Undo
                case Glyph.Redo: return "\uE7A6";       // Redo
                case Glyph.SmallerText: return "\uE8E7";  // FontDecrease
                case Glyph.BiggerText: return "\uE8E8";   // FontIncrease
                default: return null;                   // Language and the strike-through ops are custom
            }
        }

        /// <summary>Boxed buttons always show their border, so they read as buttons outside a toolbar.</summary>
        public bool Boxed { get; set; }

        /// <summary>For the Language glyph: false = English ("E"), true = Bangla phonetic ("\u0995").</summary>
        public bool BanglaOn { get; set; }

        private static readonly Font LangFont = new Font("Segoe UI Semibold", 10F);

        public NoteToolButton(Glyph glyph)
        {
            _glyph = glyph;
            Size = new Size(30, 28);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (_hover || Boxed)
            {
                using (var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 6))
                using (var fill = new SolidBrush(_hover ? Theme.FieldBg : Theme.Surface))
                using (var pen = new Pen(Theme.FieldBorder))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
            }

            if (_glyph == Glyph.Language)
            {
                TextRenderer.DrawText(g, BanglaOn ? "\u0995" : "E", LangFont, ClientRectangle,
                    BanglaOn ? Theme.Accent : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                return;
            }

            string fluent = FluentGlyph(_glyph);
            if (fluent != null)
            {
                TextRenderer.DrawText(g, fluent, IconFont, ClientRectangle, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                return;
            }

            // The two strike-through text ops have no Fluent icon; drawn to the same
            // 16px grid and stroke weight so they sit next to the font glyphs cleanly.
            float cx = Width / 2f, cy = Height / 2f;
            using (var pen = new Pen(Theme.Text, 1.5f))
            using (var red = new Pen(Color.FromArgb(224, 82, 82), 1.7f))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                red.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                red.EndCap = System.Drawing.Drawing2D.LineCap.Round;

                if (_glyph == Glyph.NoSpaces)
                {
                    // the space-bar bracket, struck through
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - 6.5f, cy - 1f), new PointF(cx - 6.5f, cy + 3.5f),
                        new PointF(cx + 6.5f, cy + 3.5f), new PointF(cx + 6.5f, cy - 1f)
                    });
                    g.DrawLine(red, cx - 7.5f, cy + 6.5f, cx + 7.5f, cy - 5.5f);
                }
                else if (_glyph == Glyph.NoNewlines)
                {
                    // an enter arrow, struck through
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx + 6f, cy - 6f), new PointF(cx + 6f, cy + 1.5f),
                        new PointF(cx - 5.5f, cy + 1.5f)
                    });
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - 2f, cy - 2f), new PointF(cx - 5.5f, cy + 1.5f),
                        new PointF(cx - 2f, cy + 5f)
                    });
                    g.DrawLine(red, cx - 7.5f, cy + 7f, cx + 7.5f, cy - 7f);
                }
            }
        }
    }

    /// <summary>
    /// The note editor's scrollbar: the same slim overlay the notes list uses. The
    /// RichTextBox keeps its native bar (so the wheel, the caret and the keyboard all
    /// scroll normally) but is laid out wide enough that the fat bar falls outside the
    /// host panel and is clipped away; this control draws over the gap instead.
    /// </summary>
    public class NoteScrollBar : Control
    {
        private const int BarWidth = 6;
        private const int MinThumb = 28;

        private readonly RichTextBox _target;
        private bool _hover;
        private bool _dragging;
        private int _grabOffset;

        public NoteScrollBar(RichTextBox target)
        {
            _target = target;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;

            _target.VScroll += (s, e) => Invalidate();
            _target.TextChanged += (s, e) => Invalidate();
            _target.Resize += (s, e) => Invalidate();
        }

        /// <summary>
        /// Visible geometry in whole lines. Rich edit ignores a WM_VSCROLL thumb message
        /// and its scroll units are undocumented, so everything here works in the line
        /// units EM_GETFIRSTVISIBLELINE and EM_LINESCROLL actually agree on.
        /// </summary>
        private bool Lines(out int first, out int visible, out int total)
        {
            first = visible = total = 0;
            if (!_target.IsHandleCreated || _target.TextLength == 0) return false;

            first = (int)Native.SendMessage(_target.Handle, Native.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            total = _target.GetLineFromCharIndex(_target.TextLength) + 1;

            // hit-test the bottom of the client area instead of dividing by the font
            // height: mixed Latin/Bengali runs do not all share one line height
            int bottomChar = _target.GetCharIndexFromPosition(new Point(4, _target.ClientSize.Height - 4));
            int lastVisible = _target.GetLineFromCharIndex(bottomChar);
            visible = Math.Max(1, lastVisible - first + 1);
            return total > visible;
        }

        private Rectangle ThumbBounds(int first, int visible, int total)
        {
            int thumb = Math.Max(MinThumb, (int)((long)Height * visible / total));
            int span = Math.Max(1, total - visible);
            int y = (int)((long)(Height - thumb) * Math.Min(first, span) / span);
            return new Rectangle(Width - BarWidth - 2, y, BarWidth, thumb);
        }

        /// <summary>Scroll so the given line is the first one showing.</summary>
        private void ScrollToLine(int targetFirst, int first)
        {
            int delta = targetFirst - first;
            if (delta == 0) return;
            Native.SendMessage(_target.Handle, Native.EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
            Invalidate();
        }

        private void ScrollToThumbY(int y, int first, int visible, int total)
        {
            int thumb = ThumbBounds(first, visible, total).Height;
            int track = Math.Max(1, Height - thumb);
            y = Math.Max(0, Math.Min(track, y));
            ScrollToLine((int)((long)y * (total - visible) / track), first);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int first, visible, total;
            if (!Lines(out first, out visible, out total)) return;
            var thumb = ThumbBounds(first, visible, total);
            if (thumb.Contains(e.Location))
            {
                _dragging = true;
                _grabOffset = e.Y - thumb.Y;
            }
            else
            {
                ScrollToThumbY(e.Y - thumb.Height / 2, first, visible, total);   // track jump
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int first, visible, total;
            if (!Lines(out first, out visible, out total)) return;
            if (_dragging)
            {
                ScrollToThumbY(e.Y - _grabOffset, first, visible, total);
                return;
            }
            bool hover = ThumbBounds(first, visible, total).Contains(e.Location);
            if (hover != _hover) { _hover = hover; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover) { _hover = false; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int lines = -Math.Sign(e.Delta) * SystemInformation.MouseWheelScrollLines;
            Native.SendMessage(_target.Handle, Native.EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var back = new SolidBrush(BackColor))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            int first, visible, total;
            if (!Lines(out first, out visible, out total)) return;   // it all fits: no bar at all

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int alpha = _dragging ? 180 : _hover ? 150 : 90;
            using (var brush = new SolidBrush(Color.FromArgb(alpha, Theme.TextDim)))
            using (var path = Theme.Round(ThumbBounds(first, visible, total), BarWidth / 2))
            {
                g.FillPath(brush, path);
            }
        }
    }

    /// <summary>
    /// The phonetic candidate dropdown for Bangla typing: Bangla on the left, the
    /// phonetic key as a small tag on the right, first row pre-selected. It never takes
    /// focus - every key stays in the note and the note drives the selection.
    /// </summary>
    class BanglaSuggestPopup : PixelPerfectForm
    {
        private const int RowHeight = 32;
        private static readonly Font BanglaFont = new Font("Nirmala UI", 11F);

        private List<BanglaSuggestion> _items = new List<BanglaSuggestion>();
        private int _selected;

        /// <summary>A row was clicked; the note commits the (now selected) suggestion.</summary>
        public event EventHandler RowClicked;

        public int Count { get { return _items.Count; } }
        public int Selected { get { return _selected; } }
        public BanglaSuggestion this[int index] { get { return _items[index]; } }

        public BanglaSuggestPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            HandleCreated += (s, e) => Theme.RoundWindowCorners(Handle);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                var p = base.CreateParams;
                p.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return p;
            }
        }

        public void Present(Form owner, Point screenLocation, List<BanglaSuggestion> items)
        {
            _items = items;
            _selected = 0;

            int width = 170;
            foreach (var item in _items)
            {
                int w = 24 + TextRenderer.MeasureText(item.Bangla, BanglaFont).Width +
                        TextRenderer.MeasureText(item.Phonetic, Theme.Small).Width + 46;
                if (w > width) width = w;
            }
            Bounds = new Rectangle(screenLocation.X, screenLocation.Y,
                                   Math.Min(width, 360), _items.Count * RowHeight + 2);
            if (!Visible) Show(owner);
            Invalidate();
        }

        public void MoveSelection(int delta)
        {
            if (_items.Count == 0) return;
            _selected = (_selected + delta + _items.Count) % _items.Count;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int row = (e.Y - 1) / RowHeight;
            if (row >= 0 && row < _items.Count)
            {
                _selected = row;
                var handler = RowClicked;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(Theme.Surface))
            {
                g.FillRectangle(bg, ClientRectangle);
            }

            for (int i = 0; i < _items.Count; i++)
            {
                var row = new Rectangle(1, 1 + i * RowHeight, Width - 2, RowHeight);
                if (i == _selected)
                {
                    using (var fill = new SolidBrush(Color.FromArgb(Theme.Dark ? 46 : 26, Theme.Accent)))
                    using (var bar = new SolidBrush(Theme.Accent))
                    {
                        g.FillRectangle(fill, row);
                        g.FillRectangle(bar, new Rectangle(row.X, row.Y, 3, row.Height));
                    }
                }

                var textRect = new Rectangle(row.X + 14, row.Y, row.Width - 14, row.Height);
                TextRenderer.DrawText(g, _items[i].Bangla, BanglaFont, textRect,
                    i == _selected ? Theme.Accent : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                string tag = _items[i].Phonetic;
                if (!string.IsNullOrEmpty(tag))
                {
                    var size = TextRenderer.MeasureText(tag, Theme.Small);
                    var tagRect = new Rectangle(row.Right - size.Width - 18,
                                                row.Y + (RowHeight - size.Height - 6) / 2,
                                                size.Width + 10, size.Height + 6);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var fill = new SolidBrush(Theme.FieldBg))
                    using (var path = Theme.Round(tagRect, 4))
                    {
                        g.FillPath(fill, path);
                    }
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
                    TextRenderer.DrawText(g, tag, Theme.Small, tagRect, Theme.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }

                if (i < _items.Count - 1)
                {
                    using (var pen = new Pen(Theme.Border))
                    {
                        g.DrawLine(pen, row.X + 8, row.Bottom - 1, row.Right - 8, row.Bottom - 1);
                    }
                }
            }

            using (var pen = new Pen(Theme.Border))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    /// <summary>
    /// One note = one window = one .txt file, saved as you type. The hot key opens a
    /// fresh note every time; older notes come back through the list. The toolbar holds
    /// the clean-up tools (strip spaces, join lines), the date/time inserts, and the AI
    /// grammar fixer; the spell checker underlines as you pause.
    /// </summary>
    public class NoteForm : PixelPerfectForm
    {
        /// <summary>Wired by the tray so the gear behaves exactly like the "Note Setting" menu item.</summary>
        public static Action OpenSettings;

        private static readonly Dictionary<string, NoteForm> OpenForms =
            new Dictionary<string, NoteForm>(StringComparer.OrdinalIgnoreCase);
        private static SpellCheckService _spellService;
        private static bool _spellTried;

        private readonly string _path;
        private readonly SpellBox _box;
        private readonly ModernButton _grammarButton;
        private readonly Timer _saveTimer;
        private readonly Timer _spellTimer;
        private bool _dirty;

        private const string AskHintText = "Ask AI: e.g. rewrite this note as a Facebook post";

        private bool _bangla;
        private NoteToolButton _langButton;
        private ToolTip _tips;
        private BanglaSuggestPopup _suggestPopup;
        private readonly Timer _suggestTimer;
        private int _suggestSeq;
        private TextBox _askBox;
        private NoteToolButton _askButton;
        private bool _askHint = true;

        public static void ShowNew()
        {
            var form = new NoteForm(NoteStore.NewPath());
            // The hot key fires while another app is foreground, and Windows may refuse
            // the focus switch \u2014 pin the new note above everything for its first show,
            // then unpin so it behaves like a normal window afterwards.
            form.TopMost = true;
            form.Show();
            Native.SetForegroundWindow(form.Handle);
            form.BeginInvoke(new Action(() => form.TopMost = false));
        }

        public static void ShowExisting(string path)
        {
            NoteForm form;
            if (OpenForms.TryGetValue(path, out form))
            {
                form.Activate();
                return;
            }
            new NoteForm(path).Show();
        }

        /// <summary>Closes every open note window (each one saves itself on the way out).</summary>
        public static void CloseAll()
        {
            var open = new List<NoteForm>(OpenForms.Values);
            foreach (var form in open)
            {
                try { form.Close(); } catch (Exception) { }
            }
        }

        /// <summary>
        /// Re-applies the hide-from-taskbar setting to every open note. Flipping
        /// ShowInTaskbar recreates the window handle, so the dark title bar and the
        /// rounded corners have to be painted onto the new handle again.
        /// </summary>
        public static void ApplyTaskbarSetting()
        {
            bool show = !Properties.Settings.Default.NoteHideTaskbar;
            foreach (var form in new List<NoteForm>(OpenForms.Values))
            {
                try
                {
                    if (form.ShowInTaskbar == show) continue;
                    form.ShowInTaskbar = show;
                    Native.SetDarkModeForWindow(form.Handle, ThemeHelper.IsDarkMode);
                    Theme.RoundWindowCorners(form.Handle);
                }
                catch (Exception) { }
            }
        }

        private NoteForm(string path)
        {
            _path = path;
            OpenForms[path] = this;

            bool dark = ThemeHelper.IsDarkMode;
            Theme.Init(dark);

            Text = Path.GetFileNameWithoutExtension(path);
            Font = new Font("Segoe UI", 9F);
            BackColor = Theme.Bg;
            ClientSize = new Size(560, 400);
            MinimumSize = new Size(576, 300);   // the toolbar's 12 icons + Grammar + gear need the room
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = !Properties.Settings.Default.NoteHideTaskbar;
            KeyPreview = true;
            try { Icon = Properties.Resources.AppIcon; } catch (Exception) { }

            var tips = new ToolTip();
            _tips = tips;

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.Bg };
            toolbar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
                }
            };

            int x = 8;
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.NewNote, "New note", x, (s, e) => ShowNew());
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.List, "All notes", x, (s, e) => NoteListForm.Open());
            x += 8;
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.NoSpaces, "Remove every space", x, (s, e) => RemoveSpaces());
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.NoNewlines, "Join all lines into one", x, (s, e) => RemoveNewlines());
            x += 8;
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.ShortDate, "Insert date", x,
                (s, e) => InsertText(Format(Properties.Settings.Default.NoteDateFormat, "yyyy-MM-dd")));
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.LongDate, "Insert long date", x,
                (s, e) => InsertText(Format(Properties.Settings.Default.NoteLongDateFormat, "dddd, dd MMMM yyyy")));
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.Timestamp, "Insert timestamp", x,
                (s, e) => InsertText(Format(Properties.Settings.Default.NoteTimestampFormat, "yyyy-MM-dd HH:mm:ss")));
            x += 8;
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.Undo, "Undo (Ctrl+Z)", x,
                (s, e) => { if (_box.CanUndo) _box.Undo(); _box.Focus(); });
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.Redo, "Redo (Ctrl+Y)", x,
                (s, e) => { if (_box.CanRedo) _box.Redo(); _box.Focus(); });
            x += 8;
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.SmallerText, "Smaller text", x,
                (s, e) => StepFontSize(-1));
            x = AddTool(toolbar, tips, NoteToolButton.Glyph.BiggerText, "Bigger text", x,
                (s, e) => StepFontSize(1));
            x += 8;
            _langButton = new NoteToolButton(NoteToolButton.Glyph.Language) { Location = new Point(x, 7) };
            tips.SetToolTip(_langButton, "Bangla phonetic typing: off (Ctrl+Shift+L for Bangla)");
            _langButton.Click += (s, e) => ToggleBangla();
            toolbar.Controls.Add(_langButton);

            var gear = new NoteToolButton(NoteToolButton.Glyph.Gear) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            gear.Location = new Point(toolbar.Width, 7);   // fixed up below, once widths are known
            tips.SetToolTip(gear, "Note Setting");
            gear.Click += (s, e) => { var open = OpenSettings; if (open != null) open(); };
            toolbar.Controls.Add(gear);

            _grammarButton = new ModernButton
            {
                Text = "Grammar",
                Size = new Size(86, 28),
                Accent = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            tips.SetToolTip(_grammarButton, "Fix spelling and grammar with AI (English or Bangla)");
            _grammarButton.Click += (s, e) => FixGrammar();
            toolbar.Controls.Add(_grammarButton);

            toolbar.Resize += (s, e) =>
            {
                gear.Location = new Point(toolbar.Width - gear.Width - 8, 7);
                _grammarButton.Location = new Point(gear.Left - _grammarButton.Width - 6, 7);
            };

            _box = new SpellBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                // Nirmala UI has matching Latin and Bengali designs, so mixed text sits
                // on one visual size (Consolas has no Bangla and the fallback ran large)
                Font = new Font("Nirmala UI", NoteFontSize()),
                ScrollBars = RichTextBoxScrollBars.Vertical,
                AcceptsTab = true,
                DetectUrls = false,
                HideSelection = false
            };
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            var slimBar = new NoteScrollBar(_box);
            host.Controls.Add(slimBar);
            host.Controls.Add(_box);
            slimBar.BringToFront();
            // The box is laid out one system-scrollbar wider than the host, so its fat
            // native bar sits past the right edge and the panel clips it; the slim bar
            // is drawn on top of that gap instead.
            host.Resize += (s, e) => LayoutEditor(host, slimBar);
            LayoutEditor(host, slimBar);

            // the Ask AI bar: type a direction ("rewrite as a Facebook post"), Enter or
            // the send button reshapes the whole note through the configured AI
            var askBar = new Panel { Size = new Size(ClientSize.Width, 46), Dock = DockStyle.Bottom, BackColor = Theme.Bg };
            askBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 0, 0, askBar.Width, 0);
                }
            };

            _askButton = new NoteToolButton(NoteToolButton.Glyph.Send)
            {
                Boxed = true,
                Size = new Size(38, 32),
                Location = new Point(askBar.Width - 48, 7),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            tips.SetToolTip(_askButton, "Send the direction to the AI");
            _askButton.Click += (s, e) => RunInstruction();

            var askHost = new FieldHost
            {
                Location = new Point(10, 7),
                Size = new Size(askBar.Width - 68, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _askBox = new TextBox
            {
                Location = new Point(10, 8),
                Size = new Size(askHost.Width - 20, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.TextDim,
                Text = AskHintText
            };
            _askBox.GotFocus += (s, e) =>
            {
                if (_askHint) { _askHint = false; _askBox.Text = ""; _askBox.ForeColor = Theme.Text; }
            };
            _askBox.LostFocus += (s, e) =>
            {
                if (!_askHint && _askBox.Text.Trim().Length == 0)
                {
                    _askHint = true; _askBox.ForeColor = Theme.TextDim; _askBox.Text = AskHintText;
                }
            };
            _askBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { RunInstruction(); e.SuppressKeyPress = true; }
            };
            askHost.Controls.Add(_askBox);
            askBar.Controls.Add(askHost);
            askBar.Controls.Add(_askButton);

            Controls.Add(host);
            Controls.Add(toolbar);
            Controls.Add(askBar);

            if (File.Exists(path))
            {
                try { _box.Text = File.ReadAllText(path); } catch (Exception) { }
            }
            UpdateTitle();

            // realtime save: the file follows the text with less than a second of lag
            _saveTimer = new Timer { Interval = 700 };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveNow(); };

            _spellTimer = new Timer { Interval = 600 };
            _spellTimer.Tick += (s, e) => { _spellTimer.Stop(); RunSpellCheck(); };

            _suggestTimer = new Timer { Interval = 120 };
            _suggestTimer.Tick += (s, e) => { _suggestTimer.Stop(); QuerySuggestions(); };

            _box.TextChanged += (s, e) =>
            {
                _dirty = true;
                _saveTimer.Stop(); _saveTimer.Start();
                _spellTimer.Stop(); _spellTimer.Start();
                if (_bangla)
                {
                    int start; string word;
                    if (CurrentWord(out start, out word)) { _suggestTimer.Stop(); _suggestTimer.Start(); }
                    else HideSuggest();
                }
            };
            _box.MouseUp += Box_MouseUp;
            _box.KeyDown += Box_SuggestKeyDown;
            _box.KeyPress += Box_SuggestKeyPress;
            _box.SelectionChanged += (s, e) =>
            {
                // the caret walked away from the word the popup was for
                if (_suggestPopup == null || !_suggestPopup.Visible) return;
                int start; string word;
                if (!CurrentWord(out start, out word)) HideSuggest();
            };
            Deactivate += (s, e) => HideSuggest();

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    if (_suggestPopup != null && _suggestPopup.Visible)
                    {
                        HideSuggest();
                        e.SuppressKeyPress = true;   // first Esc only closes the popup
                    }
                    else Close();
                }
                else if (e.Control && e.KeyCode == Keys.S) { SaveNow(); e.SuppressKeyPress = true; }
                else if (e.Control && (e.KeyCode == Keys.Y || (e.Shift && e.KeyCode == Keys.Z)))
                {
                    if (_box.CanRedo) _box.Redo();
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.Shift && e.KeyCode == Keys.L)
                {
                    ToggleBangla();
                    e.SuppressKeyPress = true;
                }
            };

            FormClosing += (s, e) =>
            {
                SaveNow();
                if (_box.Text.Trim().Length == 0)
                {
                    try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception) { }
                }
            };
            FormClosed += (s, e) => OpenForms.Remove(_path);

            Native.SetDarkModeForWindow(Handle, dark);
            Theme.RoundWindowCorners(Handle);

            Shown += (s, e) => { _box.Focus(); RunSpellCheck(); };
        }

        private static int AddTool(Panel toolbar, ToolTip tips, NoteToolButton.Glyph glyph,
                                   string tip, int x, EventHandler onClick)
        {
            var button = new NoteToolButton(glyph) { Location = new Point(x, 7) };
            tips.SetToolTip(button, tip);
            button.Click += onClick;
            toolbar.Controls.Add(button);
            return x + button.Width + 2;
        }

        private static string Format(string format, string fallback)
        {
            if (string.IsNullOrWhiteSpace(format)) format = fallback;
            try { return DateTime.Now.ToString(format); }
            catch (Exception) { return DateTime.Now.ToString(fallback); }
        }

        private void LayoutEditor(Panel host, NoteScrollBar slimBar)
        {
            const int padLeft = 10, padTop = 8, padRight = 6, padBottom = 8;
            int width = host.ClientSize.Width, height = host.ClientSize.Height;
            if (width <= 0 || height <= 0) return;

            int fat = SystemInformation.VerticalScrollBarWidth;
            _box.Bounds = new Rectangle(padLeft, padTop,
                                        Math.Max(1, width - padLeft - padRight + fat),
                                        Math.Max(1, height - padTop - padBottom));
            slimBar.Bounds = new Rectangle(width - padRight - 10, padTop, 10 + padRight,
                                           Math.Max(1, height - padTop - padBottom));
            slimBar.Invalidate();
        }

        private static int NoteFontSize()
        {
            return Math.Max(8, Math.Min(28, Properties.Settings.Default.NoteFontSize));
        }

        /// <summary>A-/A+ toolbar buttons: persist the size and restyle every open note.</summary>
        private static void StepFontSize(int delta)
        {
            int size = Math.Max(8, Math.Min(28, NoteFontSize() + delta));
            if (size == Properties.Settings.Default.NoteFontSize) return;
            Properties.Settings.Default.NoteFontSize = size;
            Properties.Settings.Default.Save();

            var font = new Font("Nirmala UI", size);
            foreach (var form in new List<NoteForm>(OpenForms.Values))
            {
                try
                {
                    int start = form._box.SelectionStart, length = form._box.SelectionLength;
                    form._box.SelectAll();
                    form._box.SelectionFont = font;   // existing text (incl. Bangla runs) follows too
                    form._box.Font = font;
                    form._box.Select(start, length);
                }
                catch (Exception) { }
            }
        }

        #region Bangla phonetic typing

        private void ToggleBangla()
        {
            if (!_bangla && !BanglaPhonetic.HasToken)
            {
                ModernDialog.Info("Bangla typing", "Add your string.bd API token in Note Setting first.");
                return;
            }
            _bangla = !_bangla;
            _langButton.BanglaOn = _bangla;
            _langButton.Invalidate();
            _tips.SetToolTip(_langButton, _bangla ? "Bangla phonetic typing: on (Ctrl+Shift+L for English)"
                                                  : "Bangla phonetic typing: off (Ctrl+Shift+L for Bangla)");
            if (!_bangla) HideSuggest();
            _box.Focus();
        }

        private static bool IsPhonetic(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '\'' || c == '`';
        }

        /// <summary>The phonetic run that ends at the caret - the word the popup is about.</summary>
        private bool CurrentWord(out int start, out string word)
        {
            start = -1;
            word = null;
            if (_box.SelectionLength > 0) return false;
            int end = _box.SelectionStart;
            string text = _box.Text;
            if (end > text.Length) end = text.Length;
            int from = end;
            while (from > 0 && IsPhonetic(text[from - 1])) from--;
            if (from >= end) return false;
            start = from;
            word = text.Substring(from, end - from);
            return true;
        }

        private void QuerySuggestions()
        {
            int start; string word;
            if (!_bangla || !CurrentWord(out start, out word)) { HideSuggest(); return; }

            int seq = ++_suggestSeq;
            System.Threading.Tasks.Task.Factory.StartNew(() => BanglaPhonetic.Suggest(word, 8))
                .ContinueWith(task =>
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (seq != _suggestSeq || !_bangla) return;   // an older lookup finishing late
                            int nowStart; string nowWord;
                            if (!CurrentWord(out nowStart, out nowWord) ||
                                nowStart != start || nowWord != word) return;
                            var items = task.IsFaulted ? null : task.Result;
                            if (items == null || items.Count == 0) { HideSuggest(); return; }
                            ShowSuggest(start, items);
                        }));
                    }
                    catch (Exception) { }   // the note was closed mid-lookup
                });
        }

        private void ShowSuggest(int wordStart, List<BanglaSuggestion> items)
        {
            if (_suggestPopup == null)
            {
                _suggestPopup = new BanglaSuggestPopup();
                _suggestPopup.RowClicked += (s, e) => CommitSuggestion();
            }
            Point caret = _box.GetPositionFromCharIndex(wordStart);
            Point screen = _box.PointToScreen(new Point(Math.Max(0, caret.X), caret.Y + _box.Font.Height + 4));
            _suggestPopup.Present(this, screen, items);
        }

        private void HideSuggest()
        {
            if (_suggestPopup != null && _suggestPopup.Visible) _suggestPopup.Hide();
        }

        private void CommitSuggestion()
        {
            if (_suggestPopup == null || !_suggestPopup.Visible || _suggestPopup.Count == 0) return;
            int start; string word;
            if (!CurrentWord(out start, out word)) { HideSuggest(); return; }
            string bangla = _suggestPopup[_suggestPopup.Selected].Bangla;
            HideSuggest();
            _box.Select(start, word.Length);
            _box.SelectedText = bangla;   // caret lands right after the Bangla word
        }

        /// <summary>
        /// Machine-fast typing can finish a word before its lookup lands, leaving the
        /// popup closed. If that word is already in the cache, convert it anyway.
        /// </summary>
        private void CommitFromCache()
        {
            int start; string word;
            if (!CurrentWord(out start, out word)) return;
            List<BanglaSuggestion> items;
            if (!BanglaPhonetic.TryCached(word, out items)) return;
            _suggestTimer.Stop();
            _suggestSeq++;               // a lookup still in flight must not reopen the popup
            _box.Select(start, word.Length);
            _box.SelectedText = items[0].Bangla;
        }

        private void Box_SuggestKeyDown(object sender, KeyEventArgs e)
        {
            if (_suggestPopup == null || !_suggestPopup.Visible) return;
            switch (e.KeyCode)
            {
                case Keys.Down: _suggestPopup.MoveSelection(1); e.SuppressKeyPress = true; break;
                case Keys.Up: _suggestPopup.MoveSelection(-1); e.SuppressKeyPress = true; break;
                case Keys.Enter:
                case Keys.Tab: CommitSuggestion(); e.SuppressKeyPress = true; break;
            }
        }

        private void Box_SuggestKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!_bangla) return;
            char c = e.KeyChar;
            bool popup = _suggestPopup != null && _suggestPopup.Visible;

            // the word is done: commit it first, then let the punctuation itself go in
            if (c >= ' ' && !IsPhonetic(c))
            {
                if (popup) CommitSuggestion();
                else CommitFromCache();   // typed faster than the lookup, but we know this word
            }

            if (c == '.')
            {
                e.Handled = true;
                _box.SelectedText = "\u0964";   // dari
            }
            else if (c >= '0' && c <= '9')
            {
                e.Handled = true;
                _box.SelectedText = ((char)(0x09E6 + (c - '0'))).ToString();   // Bengali numerals
            }
        }

        #endregion

        /// <summary>The Ask AI bar: reshape the whole note per the typed direction.</summary>
        private void RunInstruction()
        {
            if (_askHint) { _askBox.Focus(); return; }
            string instruction = _askBox.Text.Trim();
            if (instruction.Length == 0) { _askBox.Focus(); return; }
            if (!_askButton.Enabled) return;

            // a selection scopes the request: only that stretch is rewritten, the rest
            // of the note is left exactly as it was. Whitespace at either end of the
            // selection stays put - a trailing newline caught by Shift+End must not be
            // handed to the AI, or its reply would swallow the line break.
            int selStart = _box.SelectionStart;
            bool scoped = _box.SelectionLength > 0;
            string text = _box.Text;
            if (scoped)
            {
                string selected = _box.SelectedText;
                int lead = 0;
                while (lead < selected.Length && char.IsWhiteSpace(selected[lead])) lead++;
                int trail = selected.Length;
                while (trail > lead && char.IsWhiteSpace(selected[trail - 1])) trail--;
                selStart += lead;
                text = selected.Substring(lead, trail - lead);
            }
            int selLength = scoped ? text.Length : 0;
            if (text.Trim().Length == 0) return;

            _askButton.Enabled = false;
            _askBox.Enabled = false;

            System.Threading.Tasks.Task.Factory.StartNew(() => NoteAi.Apply(text, instruction))
                .ContinueWith(task =>
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _askButton.Enabled = true;
                            _askBox.Enabled = true;
                            if (task.IsFaulted)
                            {
                                string reason = task.Exception != null && task.Exception.InnerException != null
                                    ? task.Exception.InnerException.Message
                                    : "The AI request failed.";
                                ModernDialog.Info("Ask AI", reason);
                                return;
                            }
                            if (scoped)
                            {
                                _box.Select(selStart, Math.Min(selLength, _box.TextLength - selStart));
                                _box.SelectedText = task.Result;
                                _box.Select(selStart, task.Result.Length);   // keep the new text selected
                            }
                            else
                            {
                                int caret = _box.SelectionStart;
                                _box.Text = task.Result;
                                _box.SelectionStart = Math.Min(caret, _box.TextLength);
                            }
                            SaveNow();
                            _askHint = true;
                            _askBox.ForeColor = Theme.TextDim;
                            _askBox.Text = AskHintText;
                            _box.Focus();
                            Toast.Show(scoped ? "Applied to the selection." : "Applied.");
                        }));
                    }
                    catch (Exception) { }   // the note was closed while the AI was thinking
                });
        }

        /// <summary>
        /// The title bar follows the note: its first non-empty line, or the file name while
        /// empty. Runs on save, not on every keystroke - the title bar repaint is visible.
        /// </summary>
        private void UpdateTitle()
        {
            string text = _box.Text;
            string title = null;
            int start = 0;
            while (start < text.Length)
            {
                int nl = text.IndexOf('\n', start);
                string line = (nl < 0 ? text.Substring(start) : text.Substring(start, nl - start)).Trim();
                if (line.Length > 0)
                {
                    title = line.Length > 80 ? line.Substring(0, 80) + "\u2026" : line;
                    break;
                }
                if (nl < 0) break;
                start = nl + 1;
            }
            if (title == null) title = Path.GetFileNameWithoutExtension(_path);
            if (Text != title) Text = title;
        }

        private void SaveNow()
        {
            if (!_dirty) return;
            UpdateTitle();
            try
            {
                File.WriteAllText(_path, _box.Text, new UTF8Encoding(false));
                _dirty = false;
            }
            catch (Exception) { }   // a locked disk should not crash typing; the timer retries
        }

        private void InsertText(string text)
        {
            _box.SelectedText = text;
            _box.Focus();
        }

        private void RemoveSpaces()
        {
            int selection = _box.SelectionStart;
            _box.Text = _box.Text.Replace(" ", "").Replace("\t", "");
            _box.SelectionStart = Math.Min(selection, _box.TextLength);
            _box.Focus();
        }

        private void RemoveNewlines()
        {
            string text = _box.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\n', ' ');
            while (text.IndexOf("  ", StringComparison.Ordinal) >= 0) text = text.Replace("  ", " ");
            _box.Text = text.Trim();
            _box.SelectionStart = _box.TextLength;
            _box.Focus();
        }

        // ---- spell check ----------------------------------------------------------

        private static SpellCheckService Spell()
        {
            if (!_spellTried)
            {
                _spellTried = true;
                _spellService = SpellCheckService.TryCreate();
            }
            return _spellService;
        }

        private static bool IsWordChar(char c)
        {
            if (char.IsLetter(c) || c == '\'' || c == '\u2019') return true;
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark;
        }

        /// <summary>Walks the text, yielding each word span into <paramref name="words"/>.</summary>
        private static void Tokenize(string text, List<SpellError> words)
        {
            int i = 0;
            while (i < text.Length)
            {
                if (!IsWordChar(text[i])) { i++; continue; }
                int start = i;
                while (i < text.Length && IsWordChar(text[i])) i++;
                words.Add(new SpellError { Start = start, Length = i - start, Word = text.Substring(start, i - start) });
            }
        }

        private void RunSpellCheck()
        {
            var service = Spell();
            if (service == null) return;

            string text = _box.Text;
            var words = new List<SpellError>();
            Tokenize(text, words);

            var errors = new List<SpellError>();
            foreach (var word in words)
            {
                if (word.Length < 2) continue;
                bool bangla = SpellCheckService.IsBangla(word.Word);
                if (!bangla)
                {
                    // only plain-Latin words go to the English checker
                    bool latin = true;
                    foreach (char c in word.Word)
                    {
                        if (c > 0x024F && c != '\u2019') { latin = false; break; }
                    }
                    if (!latin) continue;
                }
                if (service.IsMisspelled(word.Word)) errors.Add(word);
            }
            _box.SetErrors(errors);
        }

        private static bool IsBanglaLetter(char c) { return c >= '\u0980' && c <= '\u09FF'; }

        /// <summary>The whole word around a character index - Latin or Bangla, either way.</summary>
        private bool WordAt(int index, out int start, out int length)
        {
            start = length = 0;
            string text = _box.Text;
            if (text.Length == 0) return false;
            if (index >= text.Length) index = text.Length - 1;
            if (index < 0 || !IsWordChar(text[index])) return false;

            int from = index, to = index;
            while (from > 0 && IsWordChar(text[from - 1])) from--;
            while (to + 1 < text.Length && IsWordChar(text[to + 1])) to++;
            start = from;
            length = to - from + 1;
            return true;
        }

        /// <summary>
        /// "Bangla for X" / "English for X" - the dictionary handles English to Bangla,
        /// the AI handles the way back. Filled in asynchronously so the menu opens now.
        /// </summary>
        private ToolStripMenuItem TranslateMenu(int clickIndex)
        {
            int start, length;
            // a right click inside a selection translates the selection, not one word
            if (_box.SelectionLength > 0 &&
                clickIndex >= _box.SelectionStart && clickIndex <= _box.SelectionStart + _box.SelectionLength)
            {
                start = _box.SelectionStart;
                length = _box.SelectionLength;
            }
            else if (!WordAt(clickIndex, out start, out length)) return null;

            string word = _box.Text.Substring(start, length).Trim();
            if (word.Length == 0) return null;

            bool bangla = false;
            foreach (char c in word) { if (IsBanglaLetter(c)) { bangla = true; break; } }

            var item = new ToolStripMenuItem(
                (bangla ? "English for \u201C" : "Bangla for \u201C") + Shorten(word) + "\u201D");
            item.DropDownItems.Add(new ToolStripMenuItem("Looking up\u2026") { Enabled = false });

            if (bangla && (Properties.Settings.Default.NoteAiApiKey ?? "").Trim().Length == 0)
            {
                item.DropDownItems[0].Text = "Set an AI key in Note Setting";
                return item;
            }
            if (!bangla && !BanglaPhonetic.HasToken)
            {
                item.DropDownItems[0].Text = "Set the Bangla token in Note Setting";
                return item;
            }

            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                if (bangla) return NoteAi.BanglaToEnglish(word, 6);
                var hits = BanglaPhonetic.Translate(word, 8);
                if (hits.Count > 0) return hits;
                // not an English word the dictionary knows: offer the phonetic reading
                var phonetic = new List<string>();
                foreach (var s in BanglaPhonetic.Suggest(word.ToLowerInvariant(), 8)) phonetic.Add(s.Bangla);
                return phonetic;
            }).ContinueWith(task =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        item.DropDownItems.Clear();
                        var options = task.IsFaulted ? null : task.Result;
                        if (options == null || options.Count == 0)
                        {
                            string why = task.IsFaulted && task.Exception != null && task.Exception.InnerException != null
                                ? task.Exception.InnerException.Message
                                : "(nothing found)";
                            item.DropDownItems.Add(new ToolStripMenuItem(Shorten(why)) { Enabled = false });
                            return;
                        }
                        int wordStart = start, wordLength = length;
                        foreach (string option in options)
                        {
                            var pick = new ToolStripMenuItem(option);
                            string replacement = option;
                            pick.Click += (s3, e3) =>
                            {
                                if (wordStart + wordLength > _box.TextLength) return;   // the note moved on
                                _box.Select(wordStart, wordLength);
                                _box.SelectedText = replacement;
                            };
                            item.DropDownItems.Add(pick);
                        }
                    }));
                }
                catch (Exception) { }   // the note (or the menu) is gone
            });

            return item;
        }

        private static string Shorten(string text)
        {
            text = text.Replace("\n", " ").Replace("\r", " ").Trim();
            return text.Length <= 40 ? text : text.Substring(0, 39) + "\u2026";
        }

        private void Box_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            var menu = new ContextMenuStrip
            {
                Renderer = new ModernMenuRenderer(),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Base,
                ShowImageMargin = false
            };

            int index = _box.GetCharIndexFromPosition(e.Location);
            SpellError hit = new SpellError();
            bool found = false;
            foreach (var error in _box.Errors)
            {
                if (index >= error.Start && index < error.Start + error.Length) { hit = error; found = true; break; }
            }

            var service = Spell();
            if (found && service != null)
            {
                var suggestions = service.Suggest(hit.Word, 5);
                if (suggestions.Count == 0)
                {
                    menu.Items.Add(new ToolStripMenuItem("(no suggestions)") { Enabled = false });
                }
                foreach (var suggestion in suggestions)
                {
                    var pick = new ToolStripMenuItem(suggestion) { Font = Theme.Strong };
                    var target = hit;
                    string replacement = suggestion;
                    pick.Click += (s2, e2) =>
                    {
                        _box.Select(target.Start, target.Length);
                        _box.SelectedText = replacement;
                    };
                    menu.Items.Add(pick);
                }
                var add = new ToolStripMenuItem("Add to dictionary");
                var word = hit.Word;
                add.Click += (s2, e2) => { service.Add(word); RunSpellCheck(); };
                menu.Items.Add(add);
                menu.Items.Add(new ToolStripSeparator());
            }

            var translate = TranslateMenu(index);
            if (translate != null)
            {
                menu.Items.Add(translate);
                menu.Items.Add(new ToolStripSeparator());
            }

            var cut = new ToolStripMenuItem("Cut");
            cut.Click += (s2, e2) => _box.Cut();
            var copy = new ToolStripMenuItem("Copy");
            copy.Click += (s2, e2) => _box.Copy();
            var paste = new ToolStripMenuItem("Paste");
            paste.Click += (s2, e2) => { if (Clipboard.ContainsText()) _box.SelectedText = Clipboard.GetText(); };
            var all = new ToolStripMenuItem("Select All");
            all.Click += (s2, e2) => _box.SelectAll();
            menu.Items.Add(cut);
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            menu.Items.Add(all);

            menu.Show(_box, e.Location);
        }

        // ---- AI grammar ------------------------------------------------------------

        private void FixGrammar()
        {
            string text = _box.Text;
            if (text.Trim().Length == 0) return;
            if (!_grammarButton.Enabled) return;

            _grammarButton.Enabled = false;
            string idle = _grammarButton.Text;
            _grammarButton.Text = "Fixing\u2026";

            System.Threading.Tasks.Task.Factory.StartNew(() => NoteAi.CorrectGrammar(text))
                .ContinueWith(task =>
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _grammarButton.Enabled = true;
                            _grammarButton.Text = idle;
                            if (task.IsFaulted)
                            {
                                string reason = task.Exception != null && task.Exception.InnerException != null
                                    ? task.Exception.InnerException.Message
                                    : "The AI request failed.";
                                ModernDialog.Info("Grammar", reason);
                                return;
                            }
                            int selection = _box.SelectionStart;
                            _box.Text = task.Result;
                            _box.SelectionStart = Math.Min(selection, _box.TextLength);
                            SaveNow();
                            Toast.Show("Grammar fixed.");
                        }));
                    }
                    catch (Exception) { }   // the note was closed while the AI was thinking
                });
        }
    }

    /// <summary>
    /// The notes list itself: custom-drawn rows with hover and accent selection, and a
    /// slim overlay scrollbar instead of the fat system one. One click selects, a click
    /// on the already-selected note (or Enter, or a double click) opens it.
    /// </summary>
    public class NoteListView : Control
    {
        public const int RowHeight = 48;
        private const int BarWidth = 6;    // the slim scrollbar thumb
        private const int BarHit = 16;     // the grab zone is wider than the thumb looks

        private readonly List<string> _paths = new List<string>();
        private readonly Dictionary<string, CachedRow> _cache =
            new Dictionary<string, CachedRow>(StringComparer.OrdinalIgnoreCase);
        private int _selected = -1;
        private int _hoverRow = -1;
        private int _scroll;               // pixels
        private bool _dragging;
        private int _dragOffset;           // pointer offset inside the thumb while dragging
        private bool _barHover;
        private bool _openArmed;           // the pressed row was already selected: release opens it

        private struct CachedRow { public DateTime Stamp; public string When; public string Preview; }

        /// <summary>Raised when the user asks for the selected note (click on selection, Enter, double click).</summary>
        public event EventHandler OpenRequested;

        public NoteListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Surface;
            TabStop = true;
        }

        public IList<string> Paths { get { return _paths; } }

        public string SelectedPath
        {
            get { return _selected >= 0 && _selected < _paths.Count ? _paths[_selected] : null; }
        }

        public void SetPaths(List<string> paths, string keepSelected)
        {
            _paths.Clear();
            _paths.AddRange(paths);
            _selected = keepSelected != null ? _paths.IndexOf(keepSelected) : -1;
            if (_selected < 0 && _paths.Count > 0) _selected = 0;
            _hoverRow = -1;
            ClampScroll();
            Invalidate();
        }

        // ---- geometry -------------------------------------------------------------

        private int ContentHeight { get { return _paths.Count * RowHeight; } }
        private int MaxScroll { get { return Math.Max(0, ContentHeight - Height); } }
        private bool Overflowing { get { return MaxScroll > 0; } }

        private void ClampScroll()
        {
            _scroll = Math.Max(0, Math.Min(_scroll, MaxScroll));
        }

        private Rectangle ThumbBounds()
        {
            int track = Height - 4;
            int thumb = Math.Max(24, (int)((long)track * Height / Math.Max(1, ContentHeight)));
            int span = track - thumb;
            int y = 2 + (MaxScroll > 0 ? (int)((long)span * _scroll / MaxScroll) : 0);
            return new Rectangle(Width - BarWidth - 2, y, BarWidth, thumb);
        }

        private int RowAt(int y)
        {
            int index = (y + _scroll) / RowHeight;
            return index >= 0 && index < _paths.Count ? index : -1;
        }

        private void EnsureVisible(int index)
        {
            if (index < 0) return;
            int top = index * RowHeight;
            if (top < _scroll) _scroll = top;
            else if (top + RowHeight > _scroll + Height) _scroll = top + RowHeight - Height;
            ClampScroll();
        }

        // ---- mouse ----------------------------------------------------------------

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // take the wheel only when this window already owns the keyboard - grabbing
            // focus on hover was yanking it out of the note the user was typing in
            var form = FindForm();
            if (form != null && form.ContainsFocus) Focus();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverRow = -1;
            _barHover = false;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                int track = Height - 4;
                int thumb = ThumbBounds().Height;
                int span = Math.Max(1, track - thumb);
                _scroll = (int)((long)(e.Y - 2 - _dragOffset) * MaxScroll / span);
                ClampScroll();
                Invalidate();
                return;
            }

            bool barHover = Overflowing && e.X >= Width - BarHit;
            int row = barHover ? -1 : RowAt(e.Y);
            if (barHover != _barHover || row != _hoverRow)
            {
                _barHover = barHover;
                _hoverRow = row;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;

            if (Overflowing && e.X >= Width - BarHit)
            {
                var thumb = ThumbBounds();
                if (e.Y >= thumb.Top && e.Y < thumb.Bottom)
                {
                    _dragOffset = e.Y - thumb.Top;
                }
                else
                {
                    // jump the thumb to the click, then drag from its middle
                    _dragOffset = thumb.Height / 2;
                    int track = Height - 4;
                    int span = Math.Max(1, track - thumb.Height);
                    _scroll = (int)((long)(e.Y - 2 - _dragOffset) * MaxScroll / span);
                    ClampScroll();
                }
                _dragging = true;
                Invalidate();
                return;
            }

            int row = RowAt(e.Y);
            if (row < 0) { _openArmed = false; return; }
            _openArmed = row == _selected;
            if (!_openArmed)
            {
                _selected = row;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left || !_openArmed) return;
            _openArmed = false;
            if (RowAt(e.Y) == _selected && _selected >= 0)
            {
                var open = OpenRequested;
                if (open != null) open(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll -= e.Delta / 120 * RowHeight;
            ClampScroll();
            Invalidate();
        }

        // ---- keyboard -------------------------------------------------------------

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                case Keys.Return:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_paths.Count == 0) return;
            int page = Math.Max(1, Height / RowHeight);
            int target = _selected;
            switch (e.KeyCode)
            {
                case Keys.Up: target = Math.Max(0, _selected - 1); break;
                case Keys.Down: target = Math.Min(_paths.Count - 1, _selected + 1); break;
                case Keys.PageUp: target = Math.Max(0, _selected - page); break;
                case Keys.PageDown: target = Math.Min(_paths.Count - 1, _selected + page); break;
                case Keys.Home: target = 0; break;
                case Keys.End: target = _paths.Count - 1; break;
                case Keys.Return:
                    if (_selected >= 0)
                    {
                        var open = OpenRequested;
                        if (open != null) open(this, EventArgs.Empty);
                    }
                    return;
                default:
                    return;
            }
            if (target != _selected)
            {
                _selected = target;
                EnsureVisible(_selected);
                Invalidate();
            }
        }

        // ---- painting -------------------------------------------------------------

        private CachedRow GetRow(string path)
        {
            CachedRow row;
            DateTime stamp = DateTime.MinValue;
            try { stamp = File.GetLastWriteTime(path); } catch (Exception) { }
            if (_cache.TryGetValue(path, out row) && row.Stamp == stamp) return row;

            row.Stamp = stamp;
            row.When = stamp == DateTime.MinValue ? "" : stamp.ToString("dd MMM yyyy HH:mm");
            row.Preview = "";
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        row.Preview = trimmed.Length > 90 ? trimmed.Substring(0, 90) + "\u2026" : trimmed;
                        break;
                    }
                }
            }
            catch (Exception) { }
            if (row.Preview.Length == 0) row.Preview = "(empty)";
            _cache[path] = row;
            return row;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var back = new SolidBrush(Theme.Surface))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            int first = _scroll / RowHeight;
            int last = Math.Min(_paths.Count - 1, (_scroll + Height) / RowHeight);
            for (int i = first; i <= last; i++)
            {
                var bounds = new Rectangle(0, i * RowHeight - _scroll, Width, RowHeight);
                bool selected = i == _selected;
                bool hover = i == _hoverRow && !selected;

                if (selected)
                {
                    using (var fill = new SolidBrush(Color.FromArgb(Theme.Dark ? 46 : 26, Theme.Accent)))
                    using (var bar = new SolidBrush(Theme.Accent))
                    {
                        g.FillRectangle(fill, bounds);
                        g.FillRectangle(bar, new Rectangle(bounds.X, bounds.Y, 3, bounds.Height));
                    }
                }
                else if (hover)
                {
                    using (var fill = new SolidBrush(Theme.FieldBg))
                    {
                        g.FillRectangle(fill, bounds);
                    }
                }

                string path = _paths[i];
                var row = GetRow(path);
                string title = Path.GetFileNameWithoutExtension(path);

                var top = new Rectangle(bounds.X + 12, bounds.Y + 6, bounds.Width - 152, 20);
                var stamp = new Rectangle(bounds.Right - 140, bounds.Y + 6, 122, 20);
                var bottom = new Rectangle(bounds.X + 12, bounds.Y + 25, bounds.Width - 32, 18);
                TextRenderer.DrawText(g, title, Theme.Strong, top, Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, row.When, Theme.Small, stamp, Theme.TextDim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, row.Preview, Theme.Small, bottom, Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                using (var pen = new Pen(Theme.Border))
                {
                    g.DrawLine(pen, bounds.X + 6, bounds.Bottom - 1, bounds.Right - 6, bounds.Bottom - 1);
                }
            }

            if (Overflowing)
            {
                var thumb = ThumbBounds();
                int alpha = _dragging ? 180 : _barHover ? 150 : 90;
                using (var brush = new SolidBrush(Color.FromArgb(alpha, Theme.TextDim)))
                using (var path = Theme.Round(thumb, BarWidth / 2))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillPath(brush, path);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }
    }

    /// <summary>All saved notes in one place: newest first, click to select, click again to open.</summary>
    public class NoteListForm : PixelPerfectForm
    {
        private static NoteListForm _open;

        private readonly NoteListView _list;

        public static void Open()
        {
            if (_open != null)
            {
                _open.Reload();
                _open.Activate();
                return;
            }
            _open = new NoteListForm();
            _open.FormClosed += (s, e) => _open = null;
            _open.Show();
        }

        private NoteListForm()
        {
            bool dark = ThemeHelper.IsDarkMode;
            Theme.Init(dark);

            Text = "Notes";
            Font = new Font("Segoe UI", 9F);
            BackColor = Theme.Bg;
            ClientSize = new Size(520, 420);
            MinimumSize = new Size(400, 280);
            StartPosition = FormStartPosition.CenterScreen;
            RestoreWindowBounds();
            KeyPreview = true;
            try { Icon = Properties.Resources.AppIcon; } catch (Exception) { }

            var tips = new ToolTip();

            _list = new NoteListView { Dock = DockStyle.Fill };
            _list.OpenRequested += (s, e) => OpenSelected();

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(6) };
            listHost.Controls.Add(_list);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Bg };
            var newButton = new ModernButton { Text = "New note", Size = new Size(96, 32), Location = new Point(12, 10) };
            newButton.Click += (s, e) => NoteForm.ShowNew();
            var closeAllButton = new NoteToolButton(NoteToolButton.Glyph.CloseAll)
            {
                Boxed = true, Size = new Size(38, 32), Location = new Point(116, 10)
            };
            tips.SetToolTip(closeAllButton, "Close all open notes");
            closeAllButton.Click += (s, e) => NoteForm.CloseAll();
            var deleteAllButton = new NoteToolButton(NoteToolButton.Glyph.Trash)
            {
                Boxed = true, Size = new Size(38, 32), Location = new Point(160, 10)
            };
            tips.SetToolTip(deleteAllButton, "Delete all notes");
            deleteAllButton.Click += (s, e) => DeleteAll();
            var openButton = new ModernButton { Text = "Open", Size = new Size(80, 32), Accent = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            openButton.Click += (s, e) => OpenSelected();
            var deleteButton = new ModernButton { Text = "Delete", Size = new Size(80, 32), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            deleteButton.Click += (s, e) => DeleteSelected();
            footer.Controls.Add(newButton);
            footer.Controls.Add(closeAllButton);
            footer.Controls.Add(deleteAllButton);
            footer.Controls.Add(openButton);
            footer.Controls.Add(deleteButton);
            footer.Resize += (s, e) =>
            {
                openButton.Location = new Point(footer.Width - openButton.Width - 12, 10);
                deleteButton.Location = new Point(openButton.Left - deleteButton.Width - 8, 10);
            };

            Controls.Add(listHost);
            Controls.Add(footer);

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Activated += (s, e) => Reload();   // fresh list whenever the window comes forward
            FormClosing += (s, e) => StoreWindowBounds();

            Native.SetDarkModeForWindow(Handle, dark);
            Theme.RoundWindowCorners(Handle);

            Reload();
        }

        /// <summary>Reopens where it was left: "L,T,W,H" from last time, if that spot is still on a screen.</summary>
        private void RestoreWindowBounds()
        {
            string saved = Properties.Settings.Default.NoteListBounds;
            if (string.IsNullOrEmpty(saved)) return;
            var parts = saved.Split(',');
            if (parts.Length != 4) return;
            int left, top, width, height;
            if (!int.TryParse(parts[0], out left) || !int.TryParse(parts[1], out top) ||
                !int.TryParse(parts[2], out width) || !int.TryParse(parts[3], out height)) return;
            var bounds = new Rectangle(left, top,
                Math.Max(width, MinimumSize.Width), Math.Max(height, MinimumSize.Height));
            bool onScreen = false;
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(bounds)) { onScreen = true; break; }
            }
            if (!onScreen) return;   // the monitor it lived on is gone: fall back to center screen
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }

        private void StoreWindowBounds()
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Properties.Settings.Default.NoteListBounds =
                bounds.Left + "," + bounds.Top + "," + bounds.Width + "," + bounds.Height;
            try { Properties.Settings.Default.Save(); } catch (Exception) { }
        }

        private void Reload()
        {
            string selected = _list.SelectedPath;
            var paths = new List<string>();
            try
            {
                var files = new List<FileInfo>();
                foreach (var file in Directory.GetFiles(NoteStore.Folder, "*.txt"))
                {
                    files.Add(new FileInfo(file));
                }
                files.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                foreach (var file in files) paths.Add(file.FullName);
            }
            catch (Exception) { }
            _list.SetPaths(paths, selected);
        }

        private void OpenSelected()
        {
            string path = _list.SelectedPath;
            if (path != null) NoteForm.ShowExisting(path);
        }

        private void DeleteSelected()
        {
            string path = _list.SelectedPath;
            if (path == null) return;
            if (!ModernDialog.Confirm("Delete note",
                Path.GetFileName(path) + " will be deleted for good.", "Delete", "Keep it")) return;
            try { File.Delete(path); } catch (Exception) { }
            Reload();
        }

        private void DeleteAll()
        {
            var paths = new List<string>(_list.Paths);
            if (paths.Count == 0) return;
            if (!ModernDialog.Confirm("Delete all notes",
                "All " + paths.Count + " notes will be deleted for good.", "Delete all", "Keep them")) return;
            NoteForm.CloseAll();   // closing saves them; the files go right after
            foreach (var path in paths)
            {
                try { File.Delete(path); } catch (Exception) { }
            }
            Reload();
        }
    }
}
