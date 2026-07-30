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
        public enum Glyph { NoSpaces, NoNewlines, ShortDate, LongDate, Timestamp, NewNote, List, Gear, CloseAll, Trash }

        private readonly Glyph _glyph;
        private bool _hover;

        /// <summary>Boxed buttons always show their border, so they read as buttons outside a toolbar.</summary>
        public bool Boxed { get; set; }

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

            int cx = Width / 2, cy = Height / 2;
            using (var pen = new Pen(Theme.Text, 1.6f))
            using (var red = new Pen(Color.FromArgb(230, 70, 70), 1.8f))
            using (var brush = new SolidBrush(Theme.Text))
            {
                switch (_glyph)
                {
                    case Glyph.NoSpaces:
                        // the space-bar bracket, struck through
                        g.DrawLines(pen, new[]
                        {
                            new Point(cx - 7, cy - 1), new Point(cx - 7, cy + 4),
                            new Point(cx + 7, cy + 4), new Point(cx + 7, cy - 1)
                        });
                        g.DrawLine(red, cx - 8, cy + 7, cx + 8, cy - 6);
                        break;
                    case Glyph.NoNewlines:
                        // an enter arrow, struck through
                        g.DrawLine(pen, cx + 6, cy - 6, cx + 6, cy + 2);
                        g.DrawLine(pen, cx + 6, cy + 2, cx - 5, cy + 2);
                        g.DrawLine(pen, cx - 5, cy + 2, cx - 1, cy - 2);
                        g.DrawLine(pen, cx - 5, cy + 2, cx - 1, cy + 6);
                        g.DrawLine(red, cx - 8, cy + 8, cx + 8, cy - 8);
                        break;
                    case Glyph.ShortDate:
                        g.DrawRectangle(pen, cx - 7, cy - 5, 14, 11);
                        g.DrawLine(pen, cx - 7, cy - 2, cx + 7, cy - 2);
                        g.DrawLine(pen, cx - 4, cy - 8, cx - 4, cy - 5);
                        g.DrawLine(pen, cx + 4, cy - 8, cx + 4, cy - 5);
                        break;
                    case Glyph.LongDate:
                        g.DrawRectangle(pen, cx - 7, cy - 5, 14, 11);
                        g.DrawLine(pen, cx - 7, cy - 2, cx + 7, cy - 2);
                        g.DrawLine(pen, cx - 4, cy - 8, cx - 4, cy - 5);
                        g.DrawLine(pen, cx + 4, cy - 8, cx + 4, cy - 5);
                        g.DrawLine(pen, cx - 4, cy + 1, cx + 4, cy + 1);
                        g.DrawLine(pen, cx - 4, cy + 4, cx + 1, cy + 4);
                        break;
                    case Glyph.Timestamp:
                        g.DrawEllipse(pen, cx - 7, cy - 7, 14, 14);
                        g.DrawLine(pen, cx, cy, cx, cy - 5);
                        g.DrawLine(pen, cx, cy, cx + 4, cy + 2);
                        break;
                    case Glyph.NewNote:
                        g.DrawRectangle(pen, cx - 7, cy - 7, 10, 14);
                        g.DrawLine(pen, cx + 3, cy + 2, cx + 9, cy + 2);
                        g.DrawLine(pen, cx + 6, cy - 1, cx + 6, cy + 5);
                        break;
                    case Glyph.List:
                        for (int i = -1; i <= 1; i++)
                        {
                            int y = cy + i * 5;
                            g.FillEllipse(brush, cx - 8, y - 1, 3, 3);
                            g.DrawLine(pen, cx - 2, y, cx + 8, y);
                        }
                        break;
                    case Glyph.Gear:
                        g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
                        for (int i = 0; i < 8; i++)
                        {
                            double angle = i * Math.PI / 4;
                            int x1 = cx + (int)Math.Round(Math.Cos(angle) * 5);
                            int y1 = cy + (int)Math.Round(Math.Sin(angle) * 5);
                            int x2 = cx + (int)Math.Round(Math.Cos(angle) * 8);
                            int y2 = cy + (int)Math.Round(Math.Sin(angle) * 8);
                            g.DrawLine(pen, x1, y1, x2, y2);
                        }
                        break;
                    case Glyph.CloseAll:
                        // two stacked windows, an X in the front one
                        g.DrawLines(pen, new[]
                        {
                            new Point(cx - 3, cy - 7), new Point(cx + 8, cy - 7), new Point(cx + 8, cy + 3)
                        });
                        g.DrawRectangle(pen, cx - 8, cy - 3, 11, 10);
                        g.DrawLine(red, cx - 6, cy - 1, cx + 1, cy + 5);
                        g.DrawLine(red, cx + 1, cy - 1, cx - 6, cy + 5);
                        break;
                    case Glyph.Trash:
                        g.DrawLine(pen, cx - 8, cy - 5, cx + 8, cy - 5);
                        g.DrawLine(pen, cx - 3, cy - 8, cx + 3, cy - 8);
                        g.DrawLines(pen, new[]
                        {
                            new Point(cx - 6, cy - 5), new Point(cx - 5, cy + 8),
                            new Point(cx + 5, cy + 8), new Point(cx + 6, cy - 5)
                        });
                        g.DrawLine(pen, cx - 2, cy - 2, cx - 2, cy + 5);
                        g.DrawLine(pen, cx + 2, cy - 2, cx + 2, cy + 5);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// One note = one window = one .txt file, saved as you type. The hot key opens a
    /// fresh note every time; older notes come back through the list. The toolbar holds
    /// the clean-up tools (strip spaces, join lines), the date/time inserts, and the AI
    /// grammar fixer; the spell checker underlines as you pause.
    /// </summary>
    public class NoteForm : Form
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

        public static void ShowNew()
        {
            new NoteForm(NoteStore.NewPath()).Show();
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
            MinimumSize = new Size(430, 280);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = !Properties.Settings.Default.NoteHideTaskbar;
            KeyPreview = true;
            try { Icon = Properties.Resources.AppIcon; } catch (Exception) { }

            var tips = new ToolTip();

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
            AddTool(toolbar, tips, NoteToolButton.Glyph.Timestamp, "Insert timestamp", x,
                (s, e) => InsertText(Format(Properties.Settings.Default.NoteTimestampFormat, "yyyy-MM-dd HH:mm:ss")));

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
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = new Font("Consolas", 11F),   // Notepad's default face and size
                AcceptsTab = true,
                DetectUrls = false,
                HideSelection = false
            };
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 8, 6, 8) };
            host.Controls.Add(_box);

            Controls.Add(host);
            Controls.Add(toolbar);

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

            _box.TextChanged += (s, e) =>
            {
                _dirty = true;
                _saveTimer.Stop(); _saveTimer.Start();
                _spellTimer.Stop(); _spellTimer.Start();
            };
            _box.MouseUp += Box_MouseUp;

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.Control && e.KeyCode == Keys.S) { SaveNow(); e.SuppressKeyPress = true; }
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
                    title = line.Length > 80 ? line.Substring(0, 80) + "…" : line;
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
            if (char.IsLetter(c) || c == '\'' || c == '’') return true;
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
                        if (c > 0x024F && c != '’') { latin = false; break; }
                    }
                    if (!latin) continue;
                }
                if (service.IsMisspelled(word.Word)) errors.Add(word);
            }
            _box.SetErrors(errors);
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
            _grammarButton.Text = "Fixing…";

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
                        row.Preview = trimmed.Length > 90 ? trimmed.Substring(0, 90) + "…" : trimmed;
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
    public class NoteListForm : Form
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
