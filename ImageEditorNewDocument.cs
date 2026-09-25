using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>What File &gt; New asked for: the tab's name, the size in pixels and the background.</summary>
    class NewDocumentSpec
    {
        public string Name;
        public int Width, Height;
        public Color Background;
    }

    /// <summary>
    /// File &gt; New, laid out like Photoshop's New Document window: preset categories along
    /// the top (Recent, Print, Ratio, Photo, Web, Mobile, Film &amp; Video), a grid of preset
    /// cards, and the Preset Details column on the right where the size can be typed in
    /// pixels, inches, centimetres, millimetres or points at any resolution.
    /// </summary>
    class NewDocumentDialog : PixelPerfectForm
    {
        // units: index into UnitNames
        const int Px = 0, In = 1, Cm = 2, Mm = 3, Pt = 4;
        static readonly string[] UnitNames = { "Pixels", "Inches", "Centimeters", "Millimeters", "Points" };
        static readonly string[] UnitShort = { "px", "in", "cm", "mm", "pt" };
        static readonly double[] UnitsPerInch = { 0, 1, 2.54, 25.4, 72 };
        const int MaxPx = 20000;

        class Preset
        {
            public string Name;
            public double W, H;     // in Unit
            public int Unit;
            public int Ppi;
            public bool Ratio;      // a ratio preset: constrain proportions comes on

            public Preset(string name, double w, double h, int unit, int ppi, bool ratio = false)
            {
                Name = name; W = w; H = h; Unit = unit; Ppi = ppi; Ratio = ratio;
            }

            public int PxW { get { return ToPx(W, Unit, Ppi); } }
            public int PxH { get { return ToPx(H, Unit, Ppi); } }

            public string Detail
            {
                get
                {
                    if (Unit == Px) return string.Format("{0} × {1} px", W, H);
                    return string.Format(CultureInfo.CurrentCulture, "{0:0.##} × {1:0.##} {2} @ {3} ppi", W, H, UnitShort[Unit], Ppi);
                }
            }
        }

        static readonly string[] Categories = { "Recent", "Print", "Ratio", "Photo", "Web", "Mobile", "Film & Video" };

        static List<Preset> Builtin(int category)
        {
            switch (category)
            {
                case 1: // Print
                    return new List<Preset>
                    {
                        new Preset("A4", 210, 297, Mm, 300), new Preset("A3", 297, 420, Mm, 300),
                        new Preset("A5", 148, 210, Mm, 300), new Preset("A6", 105, 148, Mm, 300),
                        new Preset("A2", 420, 594, Mm, 300), new Preset("A1", 594, 841, Mm, 150),
                        new Preset("A0", 841, 1189, Mm, 150), new Preset("B4", 250, 353, Mm, 300),
                        new Preset("B5", 176, 250, Mm, 300), new Preset("Letter", 8.5, 11, In, 300),
                        new Preset("Legal", 8.5, 14, In, 300), new Preset("Tabloid", 11, 17, In, 300),
                        new Preset("Business Card", 3.5, 2, In, 300), new Preset("Envelope DL", 220, 110, Mm, 300),
                    };
                case 2: // Ratio
                    return new List<Preset>
                    {
                        new Preset("16:9", 1920, 1080, Px, 72, true), new Preset("21:9", 2520, 1080, Px, 72, true),
                        new Preset("4:3", 1600, 1200, Px, 72, true), new Preset("1:1", 1080, 1080, Px, 72, true),
                        new Preset("3:2", 1800, 1200, Px, 72, true), new Preset("16:10", 1920, 1200, Px, 72, true),
                        new Preset("32:9", 3840, 1080, Px, 72, true), new Preset("5:4", 1280, 1024, Px, 72, true),
                        new Preset("2:1", 2000, 1000, Px, 72, true), new Preset("9:16", 1080, 1920, Px, 72, true),
                        new Preset("4:5", 1080, 1350, Px, 72, true), new Preset("2:3", 1200, 1800, Px, 72, true),
                        new Preset("3:4", 1200, 1600, Px, 72, true),
                    };
                case 3: // Photo
                    return new List<Preset>
                    {
                        new Preset("4 × 6", 6, 4, In, 300), new Preset("5 × 7", 7, 5, In, 300),
                        new Preset("8 × 10", 10, 8, In, 300), new Preset("11 × 14", 14, 11, In, 300),
                        new Preset("Passport 35 × 45", 35, 45, Mm, 300), new Preset("Passport 2 × 2", 2, 2, In, 300),
                        new Preset("Postcard", 148, 105, Mm, 300),
                    };
                case 4: // Web
                    return new List<Preset>
                    {
                        new Preset("Web Common", 1366, 768, Px, 72), new Preset("Web Large", 1920, 1080, Px, 72),
                        new Preset("Web Medium", 1440, 900, Px, 72), new Preset("Web Minimum", 1024, 768, Px, 72),
                        new Preset("Ultrawide", 2560, 1080, Px, 72), new Preset("Ultrawide QHD", 3440, 1440, Px, 72),
                        new Preset("Facebook Cover", 820, 312, Px, 72), new Preset("Facebook Post", 1200, 630, Px, 72),
                        new Preset("YouTube Thumbnail", 1280, 720, Px, 72), new Preset("YouTube Banner", 2560, 1440, Px, 72),
                        new Preset("Instagram Post", 1080, 1080, Px, 72), new Preset("Instagram Portrait", 1080, 1350, Px, 72),
                        new Preset("Story / Reel", 1080, 1920, Px, 72), new Preset("X Post", 1600, 900, Px, 72),
                        new Preset("LinkedIn Banner", 1584, 396, Px, 72),
                    };
                case 5: // Mobile
                    return new List<Preset>
                    {
                        new Preset("iPhone 15 Pro", 1179, 2556, Px, 72), new Preset("iPhone 15 Pro Max", 1290, 2796, Px, 72),
                        new Preset("iPhone SE", 750, 1334, Px, 72), new Preset("Android Large", 1440, 3200, Px, 72),
                        new Preset("Android", 1080, 2400, Px, 72), new Preset("Android Compact", 1080, 1920, Px, 72),
                        new Preset("iPad Pro 12.9\"", 2048, 2732, Px, 72), new Preset("iPad Air", 1640, 2360, Px, 72),
                        new Preset("Apple Watch 45mm", 396, 484, Px, 72),
                    };
                case 6: // Film & Video
                    return new List<Preset>
                    {
                        new Preset("HD 720p", 1280, 720, Px, 72), new Preset("Full HD 1080p", 1920, 1080, Px, 72),
                        new Preset("QHD 1440p", 2560, 1440, Px, 72), new Preset("4K UHD", 3840, 2160, Px, 72),
                        new Preset("2K DCI", 2048, 1080, Px, 72), new Preset("4K DCI", 4096, 2160, Px, 72),
                        new Preset("8K UHD", 7680, 4320, Px, 72), new Preset("Vertical 1080 × 1920", 1080, 1920, Px, 72),
                        new Preset("Square 1080", 1080, 1080, Px, 72),
                    };
            }
            return new List<Preset>();
        }

        static int ToPx(double v, int unit, int ppi)
        {
            if (unit == Px) return (int)Math.Round(v);
            return (int)Math.Round(v / UnitsPerInch[unit] * ppi);
        }

        static double FromPx(int px, int unit, int ppi)
        {
            if (unit == Px) return px;
            return px * UnitsPerInch[unit] / ppi;
        }

        // ---- recent: the sizes last created, kept next to the asset library ------------
        static string RecentFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicroApp", "editor-new-recent.txt"); }
        }

        static List<Preset> LoadRecent()
        {
            var list = new List<Preset>();
            try
            {
                if (!File.Exists(RecentFile)) return list;
                foreach (string line in File.ReadAllLines(RecentFile))
                {
                    string[] f = line.Split('|');
                    if (f.Length < 5) continue;
                    double w, h; int unit, ppi;
                    if (!double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out w) ||
                        !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out h) ||
                        !int.TryParse(f[3], out unit) || !int.TryParse(f[4], out ppi)) continue;
                    if (unit < 0 || unit >= UnitNames.Length || ppi < 1 || w <= 0 || h <= 0) continue;
                    list.Add(new Preset(f[0], w, h, unit, ppi));
                }
            }
            catch { }
            return list;
        }

        static void SaveRecent(Preset p)
        {
            try
            {
                List<Preset> list = LoadRecent();
                list.RemoveAll(r => r.PxW == p.PxW && r.PxH == p.PxH && r.Unit == p.Unit && r.Ppi == p.Ppi);
                list.Insert(0, p);
                if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
                Directory.CreateDirectory(Path.GetDirectoryName(RecentFile));
                File.WriteAllLines(RecentFile, list.Select(r => string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}",
                    r.Name.Replace("|", " "), r.W, r.H, r.Unit, r.Ppi)));
            }
            catch { }
        }

        static int _lastCategory = -1;

        // ---- ui ------------------------------------------------------------------------
        readonly CategoryBar _categories;
        readonly Panel _gridHost;
        readonly PresetGrid _grid;
        readonly Label _gridTitle;
        readonly TextBox _name;
        readonly ModernNumber _w, _h, _ppi;
        readonly ModernCombo _unit, _background;
        readonly ModernCheckBox _constrain;
        readonly OrientationButton _portrait, _landscape;
        readonly Label _info;
        readonly Size _clipSize, _currentSize;
        readonly Color _bgColor;

        int _pxW = 1200, _pxH = 800, _unitIndex = Px, _ppiValue = 72;
        double _ratio = 1.5;
        bool _syncing;
        string _presetName = "Custom";

        NewDocumentDialog(Size current, Size clip, Color bgColor, string defaultName)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            _clipSize = clip;
            _currentSize = current;
            _bgColor = bgColor;

            Text = "New Document";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(960, 600);
            BackColor = Theme.Bg;
            Font = Theme.Base;
            KeyPreview = true;

            // ---- left: categories + preset cards
            _categories = new CategoryBar(Categories) { Location = new Point(20, 14), Size = new Size(640, 38) };
            _categories.SelectedChanged += delegate { ShowCategory(_categories.Selected); };
            _gridTitle = new Label
            {
                Location = new Point(22, 66), AutoSize = true, ForeColor = Theme.TextDim, Font = Theme.Small, BackColor = Theme.Bg
            };
            _gridHost = new Panel { Location = new Point(20, 88), Size = new Size(648, 492), AutoScroll = true, BackColor = Theme.Bg };
            _grid = new PresetGrid { Location = new Point(0, 0), Width = 628 };
            _grid.Picked += delegate { ApplyPreset(_grid.SelectedPreset); };
            _grid.Activated += delegate { ApplyPreset(_grid.SelectedPreset); Create(); };
            _grid.MouseEnter += delegate { if (!_name.Focused && !ContainsFocusIn(_side)) _gridHost.Focus(); };
            _gridHost.Controls.Add(_grid);
            Controls.Add(_categories);
            Controls.Add(_gridTitle);
            Controls.Add(_gridHost);

            // ---- right: preset details
            _side = new SidePanel { Location = new Point(688, 0), Size = new Size(272, 600) };
            Controls.Add(_side);
            int x = 20, y = 18, w = 232;
            _side.Controls.Add(SideLabel("PRESET DETAILS", x, y, Theme.Strong, Theme.Text));
            y += 34;
            var nameHost = new FieldHost { Location = new Point(x, y), Size = new Size(w, 30), BackColor = Theme.Surface };
            _name = new TextBox { Text = defaultName, BorderStyle = BorderStyle.None, BackColor = Theme.FieldBg, ForeColor = Theme.Text, Font = Theme.Base };
            nameHost.Controls.Add(_name);
            _side.Controls.Add(nameHost);
            y += 44;

            _side.Controls.Add(SideLabel("Width", x, y, Theme.Small, Theme.TextDim));
            y += 18;
            _w = new ModernNumber { Location = new Point(x, y), Size = new Size(120, 28), Minimum = 0.01m, Maximum = MaxPx };
            _unit = new ModernCombo { Location = new Point(x + 128, y + 1), Size = new Size(w - 128, 26) };
            _unit.Items.AddRange(UnitNames);
            _side.Controls.Add(_w);
            _side.Controls.Add(_unit);
            y += 38;
            _side.Controls.Add(SideLabel("Height", x, y, Theme.Small, Theme.TextDim));
            y += 18;
            _h = new ModernNumber { Location = new Point(x, y), Size = new Size(120, 28), Minimum = 0.01m, Maximum = MaxPx };
            _side.Controls.Add(_h);
            _constrain = new ModernCheckBox { Text = "Constrain", Location = new Point(x + 128, y + 3), Size = new Size(w - 128, 22) };
            _side.Controls.Add(_constrain);
            y += 40;

            _side.Controls.Add(SideLabel("Orientation", x, y, Theme.Small, Theme.TextDim));
            y += 18;
            _portrait = new OrientationButton(true) { Location = new Point(x, y) };
            _landscape = new OrientationButton(false) { Location = new Point(x + 40, y) };
            _portrait.Click += delegate { SetOrientation(true); };
            _landscape.Click += delegate { SetOrientation(false); };
            _side.Controls.Add(_portrait);
            _side.Controls.Add(_landscape);
            y += 44;

            _side.Controls.Add(SideLabel("Resolution", x, y, Theme.Small, Theme.TextDim));
            y += 18;
            _ppi = new ModernNumber { Location = new Point(x, y), Size = new Size(120, 28), Minimum = 1, Maximum = 2400 };
            _side.Controls.Add(_ppi);
            _side.Controls.Add(SideLabel("Pixels/Inch", x + 128, y + 5, Theme.Base, Theme.TextDim));
            y += 40;

            _side.Controls.Add(SideLabel("Background Contents", x, y, Theme.Small, Theme.TextDim));
            y += 18;
            _background = new ModernCombo { Location = new Point(x, y), Size = new Size(w, 26) };
            _background.Items.AddRange(new[] { "White", "Black", "Background Color", "Transparent" });
            _background.SelectedIndex = 0;
            _side.Controls.Add(_background);
            y += 40;

            _info = new Label { Location = new Point(x, y), Size = new Size(w, 40), ForeColor = Theme.TextDim, Font = Theme.Small, BackColor = Theme.Surface };
            _side.Controls.Add(_info);

            var create = new ModernButton { Text = "Create", Accent = true, Size = new Size(110, 34), Location = new Point(x + w - 110, 600 - 54), BackColor = Theme.Surface };
            var close = new ModernButton { Text = "Close", Size = new Size(100, 34), Location = new Point(create.Left - 108, 600 - 54), DialogResult = DialogResult.Cancel, BackColor = Theme.Surface };
            create.Click += delegate { Create(); };
            _side.Controls.Add(create);
            _side.Controls.Add(close);
            CancelButton = close;

            _w.ValueChanged += delegate { if (!_syncing) OnSizeTyped(true); };
            _h.ValueChanged += delegate { if (!_syncing) OnSizeTyped(false); };
            _ppi.ValueChanged += delegate { if (!_syncing) OnPpiChanged(); };
            _unit.SelectedIndexChanged += delegate { if (!_syncing) { _unitIndex = Math.Max(0, _unit.SelectedIndex); ShowFields(); } };
            _constrain.CheckedChanged += delegate { if (_constrain.Checked && _pxH > 0) _ratio = (double)_pxW / _pxH; };

            // start from the last size made, else the clipboard, else the document in front
            List<Preset> recent = LoadRecent();
            if (recent.Count > 0) ApplyPreset(recent[0]);
            else if (!clip.IsEmpty) ApplyPreset(new Preset("Clipboard", clip.Width, clip.Height, Px, 72));
            else if (!current.IsEmpty) ApplyPreset(new Preset("Current Document", current.Width, current.Height, Px, 72));
            else ApplyPreset(new Preset("Custom", 1200, 800, Px, 72));

            int category = _lastCategory >= 0 ? _lastCategory : (RecentPresets().Count > 0 ? 0 : 1);
            _categories.Selected = category;
            ShowCategory(category);

            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
            Shown += delegate { _name.Focus(); _name.SelectAll(); };
        }

        readonly SidePanel _side;

        static bool ContainsFocusIn(Control c) { return c != null && c.ContainsFocus; }

        static Label SideLabel(string text, int x, int y, Font font, Color color)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Theme.Surface };
        }

        List<Preset> RecentPresets()
        {
            var list = new List<Preset>();
            if (!_clipSize.IsEmpty) list.Add(new Preset("Clipboard", _clipSize.Width, _clipSize.Height, Px, 72));
            if (!_currentSize.IsEmpty) list.Add(new Preset("Current Document", _currentSize.Width, _currentSize.Height, Px, 72));
            list.AddRange(LoadRecent());
            return list;
        }

        void ShowCategory(int index)
        {
            _lastCategory = index;
            List<Preset> presets = index == 0 ? RecentPresets() : Builtin(index);
            _gridTitle.Text = (index == 0 ? "RECENT" : "BLANK DOCUMENT PRESETS") + "  (" + presets.Count + ")";
            _grid.SetPresets(presets, _pxW, _pxH);
            _gridHost.AutoScrollPosition = Point.Empty;
            if (presets.Count == 0) _gridTitle.Text = "RECENT - the sizes you create show up here";
        }

        void ApplyPreset(Preset p)
        {
            if (p == null) return;
            _presetName = p.Name;
            _unitIndex = p.Unit;
            _ppiValue = p.Ppi;
            _pxW = Clamp(p.PxW);
            _pxH = Clamp(p.PxH);
            _syncing = true;
            _constrain.Checked = p.Ratio;
            _syncing = false;
            _ratio = (double)_pxW / Math.Max(1, _pxH);
            ShowFields();
        }

        static int Clamp(int px) { return Math.Max(1, Math.Min(MaxPx, px)); }

        /// <summary>Puts the pixel size back into the boxes in the chosen unit.</summary>
        void ShowFields()
        {
            _syncing = true;
            int decimals = _unitIndex == Px ? 0 : _unitIndex == Mm || _unitIndex == Pt ? 1 : 2;
            foreach (ModernNumber n in new[] { _w, _h })
            {
                n.DecimalPlaces = decimals;
                n.Increment = _unitIndex == Px ? 1 : _unitIndex == In ? 0.1m : 1;
                n.Maximum = (decimal)Math.Max(1, FromPx(MaxPx, _unitIndex, _ppiValue));
                n.Minimum = _unitIndex == Px ? 1 : 0.01m;
            }
            _w.Value = (decimal)FromPx(_pxW, _unitIndex, _ppiValue);
            _h.Value = (decimal)FromPx(_pxH, _unitIndex, _ppiValue);
            _unit.SelectedIndex = _unitIndex;
            _ppi.Value = _ppiValue;
            _syncing = false;
            UpdateInfo();
        }

        void OnSizeTyped(bool width)
        {
            if (width) _pxW = Clamp(ToPx((double)_w.Value, _unitIndex, _ppiValue));
            else _pxH = Clamp(ToPx((double)_h.Value, _unitIndex, _ppiValue));
            if (_constrain.Checked && _ratio > 0)
            {
                if (width) _pxH = Clamp((int)Math.Round(_pxW / _ratio));
                else _pxW = Clamp((int)Math.Round(_pxH * _ratio));
                _syncing = true;
                if (width) _h.Value = (decimal)FromPx(_pxH, _unitIndex, _ppiValue);
                else _w.Value = (decimal)FromPx(_pxW, _unitIndex, _ppiValue);
                _syncing = false;
            }
            else if (_pxH > 0) _ratio = (double)_pxW / _pxH;
            _presetName = "Custom";
            UpdateInfo();
        }

        /// <summary>Photoshop keeps the printed size when the resolution changes, so the pixels follow.</summary>
        void OnPpiChanged()
        {
            int newPpi = (int)_ppi.Value;
            if (_unitIndex != Px)
            {
                _pxW = Clamp((int)Math.Round((double)_pxW * newPpi / _ppiValue));
                _pxH = Clamp((int)Math.Round((double)_pxH * newPpi / _ppiValue));
            }
            _ppiValue = newPpi;
            ShowFields();
        }

        void SetOrientation(bool portrait)
        {
            bool isPortrait = _pxH >= _pxW;
            if (portrait == isPortrait || _pxW == _pxH) return;
            int t = _pxW; _pxW = _pxH; _pxH = t;
            _ratio = (double)_pxW / Math.Max(1, _pxH);
            ShowFields();
        }

        void UpdateInfo()
        {
            bool portrait = _pxH >= _pxW;
            _portrait.Checked = portrait;
            _landscape.Checked = !portrait || _pxW == _pxH;
            if (_pxW == _pxH) _portrait.Checked = true;
            _grid.Highlight(_pxW, _pxH);
            double mb = (double)_pxW * _pxH * 4 / 1048576.0;
            _info.ForeColor = mb > 400 ? Theme.Danger : Theme.TextDim;
            _info.Text = string.Format("{0} × {1} px  ·  {2:0.#} MB per layer{3}", _pxW, _pxH, mb,
                mb > 400 ? "\r\nVery large - lower the resolution if the editor runs out of memory" : "");
        }

        void Create()
        {
            _w.Commit(); _h.Commit(); _ppi.Commit();
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && !(ActiveControl is ModernCombo)) { Create(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public static NewDocumentSpec Ask(IWin32Window owner, Size current, Size clip, Color bgColor, string defaultName)
        {
            using (var d = new NewDocumentDialog(current, clip, bgColor, defaultName))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                int bg = d._background.SelectedIndex;
                var spec = new NewDocumentSpec
                {
                    Name = string.IsNullOrWhiteSpace(d._name.Text) ? defaultName : d._name.Text.Trim(),
                    Width = d._pxW,
                    Height = d._pxH,
                    Background = bg == 1 ? Color.Black : bg == 2 ? bgColor : bg == 3 ? Color.Transparent : Color.White
                };
                string recentName = d._presetName == "Clipboard" || d._presetName == "Current Document" ? "Custom" : d._presetName;
                SaveRecent(new Preset(recentName, FromPx(d._pxW, d._unitIndex, d._ppiValue), FromPx(d._pxH, d._unitIndex, d._ppiValue), d._unitIndex, d._ppiValue));
                return spec;
            }
        }

        // ================================================================ controls

        /// <summary>The right-hand column: a raised surface with a hairline on its left.</summary>
        sealed class SidePanel : Panel
        {
            public SidePanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                BackColor = Theme.Surface;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, ClientRectangle);
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, 0, Height);
            }
        }

        /// <summary>The category row along the top, underlined like Photoshop's.</summary>
        sealed class CategoryBar : Control
        {
            readonly string[] _names;
            readonly List<Rectangle> _rects = new List<Rectangle>();
            int _sel, _hover = -1;
            public event EventHandler SelectedChanged;

            public CategoryBar(string[] names)
            {
                _names = names;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Font = Theme.Strong;
                Cursor = Cursors.Hand;
            }

            public int Selected
            {
                get { return _sel; }
                set { _sel = Math.Max(0, Math.Min(_names.Length - 1, value)); Invalidate(); }
            }

            void Measure(Graphics g)
            {
                _rects.Clear();
                int x = 0;
                foreach (string n in _names)
                {
                    int w = TextRenderer.MeasureText(g, n, Font).Width + 18;
                    _rects.Add(new Rectangle(x, 0, w, Height));
                    x += w + 4;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Measure(g);
                using (var bg = new SolidBrush(Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
                using (var line = new Pen(Theme.Border)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
                for (int i = 0; i < _names.Length; i++)
                {
                    Rectangle r = _rects[i];
                    Color c = i == _sel ? Theme.Text : i == _hover ? Theme.Text : Theme.TextDim;
                    TextRenderer.DrawText(g, _names[i], Font, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    if (i == _sel)
                        using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, r.X + 6, Height - 3, r.Width - 12, 3);
                }
            }

            int Hit(Point p)
            {
                for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(p)) return i;
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = Hit(e.Location);
                if (h != _hover) { _hover = h; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                int h = Hit(e.Location);
                if (h < 0 || h == _sel) return;
                _sel = h;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>The preset cards: a page drawn in the preset's proportions, its name and its size.</summary>
        sealed class PresetGrid : Control
        {
            List<Preset> _presets = new List<Preset>();
            int _sel = -1, _hover = -1;
            const int CardW = 148, CardH = 136, Gap = 10, Cols = 4;
            public event EventHandler Picked, Activated;

            public PresetGrid()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }

            public Preset SelectedPreset { get { return _sel >= 0 && _sel < _presets.Count ? _presets[_sel] : null; } }

            public void SetPresets(List<Preset> presets, int pxW, int pxH)
            {
                _presets = presets;
                int rows = (presets.Count + Cols - 1) / Cols;
                Height = Math.Max(1, rows * (CardH + Gap));
                _hover = -1;
                Highlight(pxW, pxH);
                Invalidate();
            }

            /// <summary>Marks the card that matches the size in the details (either orientation), if any.</summary>
            public void Highlight(int pxW, int pxH)
            {
                int found = -1;
                for (int i = 0; i < _presets.Count && found < 0; i++)
                {
                    int w = _presets[i].PxW, h = _presets[i].PxH;
                    if ((w == pxW && h == pxH) || (w == pxH && h == pxW)) found = i;
                }
                if (found != _sel) { _sel = found; Invalidate(); }
            }

            Rectangle CardRect(int i)
            {
                return new Rectangle((i % Cols) * (CardW + Gap), (i / Cols) * (CardH + Gap), CardW, CardH);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (var bg = new SolidBrush(Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < _presets.Count; i++)
                {
                    Rectangle r = CardRect(i);
                    if (!r.IntersectsWith(e.ClipRectangle)) continue;
                    Preset p = _presets[i];
                    bool sel = i == _sel, hover = i == _hover;
                    using (GraphicsPath path = Theme.Round(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 8))
                    {
                        using (var fill = new SolidBrush(sel ? Color.FromArgb(Theme.Dark ? 40 : 22, Theme.Accent) : Theme.Surface))
                            g.FillPath(fill, path);
                        using (var pen = new Pen(sel ? Theme.Accent : hover ? Theme.FieldBorder : Theme.Border, sel ? 2f : 1f))
                            g.DrawPath(pen, path);
                    }

                    // the page, in proportion, inside a 64 × 56 box
                    float aw = p.PxW, ah = p.PxH;
                    float s = Math.Min(64f / aw, 52f / ah);
                    float pw = Math.Max(6, aw * s), ph = Math.Max(6, ah * s);
                    var page = new RectangleF(r.X + (r.Width - pw) / 2f, r.Y + 14 + (52 - ph) / 2f, pw, ph);
                    using (var pf = new SolidBrush(Theme.Dark ? Color.FromArgb(58, 58, 66) : Color.FromArgb(236, 236, 242)))
                        g.FillRectangle(pf, page);
                    using (var pp = new Pen(sel ? Theme.Accent : Theme.TextDim, 1.2f))
                        g.DrawRectangle(pp, page.X, page.Y, page.Width, page.Height);
                    if (p.Ratio)
                        TextRenderer.DrawText(g, p.Name, Theme.Small, Rectangle.Round(page), Theme.TextDim,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                    var nameRect = new Rectangle(r.X + 6, r.Y + 76, r.Width - 12, 22);
                    TextRenderer.DrawText(g, p.Name, Theme.Strong, nameRect, Theme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    var detailRect = new Rectangle(r.X + 4, r.Y + 98, r.Width - 8, 30);
                    TextRenderer.DrawText(g, p.Detail, Theme.Small, detailRect, Theme.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }
            }

            int Hit(Point pt)
            {
                for (int i = 0; i < _presets.Count; i++) if (CardRect(i).Contains(pt)) return i;
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = Hit(e.Location);
                if (h != _hover) { _hover = h; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                int h = Hit(e.Location);
                if (h < 0) return;
                _sel = h;
                Invalidate();
                if (Picked != null) Picked(this, EventArgs.Empty);
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                if (e.Button == MouseButtons.Left && Hit(e.Location) >= 0 && Activated != null) Activated(this, EventArgs.Empty);
            }
        }

        /// <summary>Portrait / landscape toggle: a small page icon, accent-framed when on.</summary>
        sealed class OrientationButton : Control
        {
            readonly bool _portrait;
            bool _checked, _hover;

            public OrientationButton(bool portrait)
            {
                _portrait = portrait;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Size = new Size(34, 32);
                Cursor = Cursors.Hand;
                new ToolTip().SetToolTip(this, portrait ? "Portrait" : "Landscape");
            }

            public bool Checked
            {
                get { return _checked; }
                set { if (_checked != value) { _checked = value; Invalidate(); } }
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 6))
                {
                    using (var f = new SolidBrush(_checked ? Color.FromArgb(Theme.Dark ? 50 : 28, Theme.Accent) : _hover ? Theme.FieldBg : Theme.Surface))
                        g.FillPath(f, path);
                    using (var p = new Pen(_checked ? Theme.Accent : Theme.FieldBorder, _checked ? 1.6f : 1f)) g.DrawPath(p, path);
                }
                Rectangle page = _portrait ? new Rectangle(Width / 2 - 6, Height / 2 - 8, 12, 16) : new Rectangle(Width / 2 - 8, Height / 2 - 6, 16, 12);
                using (var p = new Pen(_checked ? Theme.Accent : Theme.TextDim, 1.4f)) g.DrawRectangle(p, page);
            }
        }
    }
}
