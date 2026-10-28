using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// File &gt; Print, laid out like Photoshop's Print Settings: the page on the left with
    /// the picture where it will land (drag it to move it), and on the right the printer,
    /// copies, paper and orientation, the position, and the scaled print size. Nothing is
    /// sent until Print is pressed. All lengths are kept in hundredths of an inch, the
    /// unit System.Drawing.Printing works in.
    /// </summary>
    class EditorPrintDialog : PixelPerfectForm
    {
        static readonly string[] UnitNames = { "Inches", "Centimeters", "Millimeters" };
        static readonly float[] UnitPerHundredth = { 0.01f, 0.0254f, 0.254f };

        // remembered for the session
        static string _lastPrinter, _lastPaper;
        static bool _lastLandscape;
        static int _lastUnits;

        Bitmap _image;                   // the flattened page being previewed (owned here)
        Bitmap _thumb;                   // a small copy for the preview
        readonly Func<int, Bitmap> _render;   // flattens page i (0-based) of the document
        readonly IList<string> _pageNames;
        readonly int _currentPage;
        int _previewPage;
        readonly float _ppi;
        readonly string _docName;
        readonly PrinterSettings _ps = new PrinterSettings();
        bool _havePrinter;
        readonly List<PaperSize> _papers = new List<PaperSize>();

        // layout state (hundredths of an inch)
        bool _landscape, _center = true, _fit;
        float _scale = 1f;              // 1 = the document's own size at its resolution
        float _left, _top;
        float _hardX, _hardY;           // the printer's unprintable edge
        int _units;
        bool _syncing;

        readonly ModernCombo _printer, _paper, _unitCombo;
        readonly ModernNumber _copies, _topBox, _leftBox, _scaleBox, _wBox, _hBox;
        readonly ModernCheckBox _centerChk, _fitChk;
        readonly NewDocumentDialog.OrientationButton _portraitBtn, _landscapeBtn;
        readonly Label _resLbl, _status;
        readonly ModernButton _printBtn;
        readonly PagePreview _preview;
        readonly ModernCombo _pagesCombo, _previewCombo;
        readonly TextBox _rangeBox;

        bool MultiPage { get { return _pageNames.Count > 1; } }

        EditorPrintDialog(Func<int, Bitmap> render, IList<string> pageNames, int currentPage, float ppi, string docName)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            _render = render;
            _pageNames = pageNames;
            _currentPage = _previewPage = Math.Max(0, Math.Min(pageNames.Count - 1, currentPage));
            _image = render(_previewPage);
            Bitmap image = _image;
            _ppi = ppi > 0 ? ppi : 72;
            _docName = docName;
            _thumb = MakeThumb(image, 1400);
            int extra = pageNames.Count > 1 ? 142 : 0;   // room for the PAGES section
            int height = 660 + extra;

            Text = "Print Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, height);
            BackColor = Theme.Bg;
            Font = Theme.Base;

            _preview = new PagePreview(this) { Location = new Point(0, 0), Size = new Size(640, height) };
            Controls.Add(_preview);

            var side = new Panel { Location = new Point(640, 0), Size = new Size(360, height), BackColor = Theme.Surface, AutoScroll = false };
            side.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, 0, side.Height); };
            Controls.Add(side);

            int x = 20, w = 320, y = 16;
            side.Controls.Add(Heading("PRINTER SETUP", x, y)); y += 28;
            side.Controls.Add(Caption("Printer", x, y)); y += 18;
            _printer = new ModernCombo { Location = new Point(x, y), Size = new Size(w, 26) };
            side.Controls.Add(_printer); y += 34;
            side.Controls.Add(Caption("Copies", x, y));
            side.Controls.Add(Caption("Paper", x + 100, y)); y += 18;
            _copies = new ModernNumber { Location = new Point(x, y), Size = new Size(88, 28), Minimum = 1, Maximum = 999, Value = 1 };
            _paper = new ModernCombo { Location = new Point(x + 100, y + 1), Size = new Size(w - 100, 26) };
            side.Controls.Add(_copies); side.Controls.Add(_paper); y += 38;
            side.Controls.Add(Caption("Layout", x, y)); y += 18;
            _portraitBtn = new NewDocumentDialog.OrientationButton(true) { Location = new Point(x, y) };
            _landscapeBtn = new NewDocumentDialog.OrientationButton(false) { Location = new Point(x + 40, y) };
            _portraitBtn.Click += delegate { SetLandscape(false); };
            _landscapeBtn.Click += delegate { SetLandscape(true); };
            side.Controls.Add(_portraitBtn); side.Controls.Add(_landscapeBtn);
            var props = new ModernButton { Text = "Printer Settings…", Size = new Size(150, 30), Location = new Point(x + w - 150, y + 1), BackColor = Theme.Surface };
            props.Click += delegate { ShowPrinterProperties(); };
            side.Controls.Add(props); y += 46;

            if (MultiPage)
            {
                side.Controls.Add(Heading("PAGES", x, y)); y += 28;
                side.Controls.Add(Caption("Print", x, y));
                side.Controls.Add(Caption("Pages (e.g. 1-3, 5)", x + 170, y)); y += 18;
                _pagesCombo = new ModernCombo { Location = new Point(x, y + 1), Size = new Size(160, 26) };
                _pagesCombo.Items.Add("All pages (" + pageNames.Count + ")");
                _pagesCombo.Items.Add("Current page (" + (_currentPage + 1) + ")");
                _pagesCombo.Items.Add("Pages…");
                _pagesCombo.SelectedIndex = 0;
                _rangeBox = new TextBox
                {
                    Location = new Point(x + 170, y + 2), Width = w - 170,
                    BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.FieldBg, ForeColor = Theme.Text,
                    Text = "1-" + pageNames.Count, Enabled = false
                };
                side.Controls.Add(_pagesCombo); side.Controls.Add(_rangeBox); y += 38;
                side.Controls.Add(Caption("Preview", x, y)); y += 18;
                _previewCombo = new ModernCombo { Location = new Point(x, y), Size = new Size(w, 26) };
                for (int i = 0; i < pageNames.Count; i++) _previewCombo.Items.Add((i + 1) + "  -  " + pageNames[i]);
                _previewCombo.SelectedIndex = _previewPage;
                side.Controls.Add(_previewCombo); y += 40;
            }

            side.Controls.Add(Heading("POSITION AND SIZE", x, y)); y += 28;
            _centerChk = new ModernCheckBox { Text = "Center", Checked = true, Location = new Point(x, y), Size = new Size(100, 22), BackColor = Theme.Surface };
            _unitCombo = new ModernCombo { Location = new Point(x + w - 130, y - 2), Size = new Size(130, 26) };
            _unitCombo.Items.AddRange(UnitNames);
            side.Controls.Add(_centerChk); side.Controls.Add(_unitCombo); y += 28;
            side.Controls.Add(Caption("Top", x, y)); side.Controls.Add(Caption("Left", x + 164, y)); y += 18;
            _topBox = Length(x, y, 156); _leftBox = Length(x + 164, y, 156);
            side.Controls.Add(_topBox); side.Controls.Add(_leftBox); y += 40;

            _fitChk = new ModernCheckBox { Text = "Scale to Fit Media", Location = new Point(x, y), Size = new Size(w, 22), BackColor = Theme.Surface };
            side.Controls.Add(_fitChk); y += 28;
            side.Controls.Add(Caption("Scale", x, y)); side.Controls.Add(Caption("Width", x + 108, y)); side.Controls.Add(Caption("Height", x + 216, y)); y += 18;
            _scaleBox = new ModernNumber { Location = new Point(x, y), Size = new Size(100, 28), Minimum = 1, Maximum = 5000, DecimalPlaces = 1, Suffix = "%" };
            _wBox = Length(x + 108, y, 104); _hBox = Length(x + 216, y, 104);
            side.Controls.Add(_scaleBox); side.Controls.Add(_wBox); side.Controls.Add(_hBox); y += 36;
            _resLbl = new Label { Location = new Point(x, y), Size = new Size(w, 20), ForeColor = Theme.TextDim, Font = Theme.Small, BackColor = Theme.Surface };
            side.Controls.Add(_resLbl); y += 24;
            _status = new Label { Location = new Point(x, y), Size = new Size(w, 110), ForeColor = Theme.TextDim, Font = Theme.Small, BackColor = Theme.Surface };
            side.Controls.Add(_status);

            _printBtn = new ModernButton { Text = "Print", Accent = true, Size = new Size(130, 34), Location = new Point(x + w - 130, height - 54), BackColor = Theme.Surface };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(100, 34), Location = new Point(_printBtn.Left - 108, height - 54), DialogResult = DialogResult.Cancel, BackColor = Theme.Surface };
            _printBtn.Click += delegate { DoPrint(); };
            side.Controls.Add(_printBtn); side.Controls.Add(cancel);
            CancelButton = cancel;

            LoadPrinters();
            _units = Math.Max(0, Math.Min(UnitNames.Length - 1, _lastUnits));
            _unitCombo.SelectedIndex = _units;
            // a picture wider than tall starts on a landscape page, unless the last print said otherwise
            _landscape = _lastPaper != null ? _lastLandscape : image.Width > image.Height;
            // a page too small for the picture at its own size starts fitted
            RefreshPage();
            RectangleF area = Printable();
            _fit = ActualSize().Width > area.Width || ActualSize().Height > area.Height;

            _printer.SelectedIndexChanged += delegate { if (!_syncing) OnPrinterChanged(); };
            _paper.SelectedIndexChanged += delegate { if (!_syncing) RefreshPage(); };
            _unitCombo.SelectedIndexChanged += delegate { if (!_syncing) { _units = Math.Max(0, _unitCombo.SelectedIndex); ShowFields(); } };
            _centerChk.CheckedChanged += delegate { if (_syncing) return; _center = _centerChk.Checked; ShowFields(); };
            _fitChk.CheckedChanged += delegate { if (_syncing) return; _fit = _fitChk.Checked; if (!_fit) _scale = FittedScale(); ShowFields(); };
            _scaleBox.ValueChanged += delegate { if (_syncing) return; _fit = false; _scale = (float)_scaleBox.Value / 100f; ShowFields(); };
            _wBox.ValueChanged += delegate { if (_syncing) return; _fit = false; _scale = FromUnits(_wBox.Value) / ActualSize().Width; ShowFields(); };
            _hBox.ValueChanged += delegate { if (_syncing) return; _fit = false; _scale = FromUnits(_hBox.Value) / ActualSize().Height; ShowFields(); };
            _topBox.ValueChanged += delegate { if (_syncing) return; _center = false; _top = FromUnits(_topBox.Value); ShowFields(); };
            _leftBox.ValueChanged += delegate { if (_syncing) return; _center = false; _left = FromUnits(_leftBox.Value); ShowFields(); };
            if (MultiPage)
            {
                _pagesCombo.SelectedIndexChanged += delegate
                {
                    _rangeBox.Enabled = _pagesCombo.SelectedIndex == 2;
                    if (_pagesCombo.SelectedIndex == 1) ShowPreviewPage(_currentPage);
                    if (_rangeBox.Enabled) _rangeBox.Focus();
                    ShowFields();
                };
                _rangeBox.TextChanged += delegate { ShowFields(); };
                _previewCombo.SelectedIndexChanged += delegate { if (!_syncing) ShowPreviewPage(_previewCombo.SelectedIndex); };
            }

            ShowFields();
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
            FormClosed += delegate { _thumb.Dispose(); _image.Dispose(); };
        }

        static Label Heading(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = Theme.Strong, ForeColor = Theme.Text, BackColor = Theme.Surface };
        }

        static Label Caption(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextDim, BackColor = Theme.Surface };
        }

        static ModernNumber Length(int x, int y, int w)
        {
            return new ModernNumber { Location = new Point(x, y), Size = new Size(w, 28), Minimum = -1000, Maximum = 100000, DecimalPlaces = 2, Increment = 0.1m };
        }

        static Bitmap MakeThumb(Bitmap src, int max)
        {
            float s = Math.Min(1f, (float)max / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)(src.Width * s)), h = Math.Max(1, (int)(src.Height * s));
            return Pixels.Downscale(src, w, h);
        }

        // ================================================================ printer + paper

        void LoadPrinters()
        {
            _syncing = true;
            _printer.Items.Clear();
            var names = new List<string>();
            try { foreach (string n in PrinterSettings.InstalledPrinters) names.Add(n); }
            catch { }
            if (names.Count == 0)
            {
                _printer.Items.Add("No printer installed");
                _printer.SelectedIndex = 0;
                _havePrinter = false;
            }
            else
            {
                _printer.Items.AddRange(names);
                string want = _lastPrinter != null && names.Contains(_lastPrinter) ? _lastPrinter : SafeDefaultPrinter(names);
                _printer.SelectedIndex = Math.Max(0, names.IndexOf(want));
                _ps.PrinterName = names[_printer.SelectedIndex];
                _havePrinter = SafeIsValid();
            }
            _syncing = false;
            LoadPapers();
        }

        string SafeDefaultPrinter(List<string> names)
        {
            try { string d = new PrinterSettings().PrinterName; if (names.Contains(d)) return d; } catch { }
            return names[0];
        }

        bool SafeIsValid()
        {
            try { return _ps.IsValid; } catch { return false; }
        }

        void OnPrinterChanged()
        {
            if (_printer.SelectedItem == null || !_printer.Items.Contains(_printer.SelectedItem as string)) return;
            try { _ps.PrinterName = (string)_printer.SelectedItem; } catch { }
            _havePrinter = SafeIsValid();
            LoadPapers();
        }

        void LoadPapers()
        {
            string keep = _paper.SelectedItem as string ?? _lastPaper;
            _papers.Clear();
            if (_havePrinter)
            {
                try { foreach (PaperSize p in _ps.PaperSizes) if (p.Width > 0 && p.Height > 0) _papers.Add(p); }
                catch { }
            }
            if (_papers.Count == 0)
            {
                // no printer (or it reports nothing): the common sheets, so the preview still means something
                _papers.Add(new PaperSize("A4", 827, 1169));
                _papers.Add(new PaperSize("A3", 1169, 1654));
                _papers.Add(new PaperSize("A5", 583, 827));
                _papers.Add(new PaperSize("Letter", 850, 1100));
                _papers.Add(new PaperSize("Legal", 850, 1400));
            }
            _syncing = true;
            _paper.Items.Clear();
            foreach (PaperSize p in _papers) _paper.Items.Add(p.PaperName);
            int index = keep != null ? _paper.Items.IndexOf(keep) : -1;
            if (index < 0)
            {
                string def = null;
                try { if (_havePrinter) def = _ps.DefaultPageSettings.PaperSize.PaperName; } catch { }
                index = def != null ? _paper.Items.IndexOf(def) : -1;
            }
            _paper.SelectedIndex = Math.Max(0, index);
            _syncing = false;
            RefreshPage();
        }

        PaperSize CurrentPaper { get { return _papers[Math.Max(0, Math.Min(_papers.Count - 1, _paper.SelectedIndex))]; } }

        /// <summary>The page as it will print, with the printer's hard margins for that paper and orientation.</summary>
        PageSettings BuildPageSettings()
        {
            var pg = new PageSettings(_ps);
            try
            {
                pg.PaperSize = CurrentPaper;
                pg.Landscape = _landscape;
                pg.Margins = new Margins(0, 0, 0, 0);
            }
            catch { }
            return pg;
        }

        void RefreshPage()
        {
            _hardX = _hardY = 25;   // a quarter inch when the printer cannot say
            if (_havePrinter)
            {
                try
                {
                    PageSettings pg = BuildPageSettings();
                    _hardX = pg.HardMarginX;
                    _hardY = pg.HardMarginY;
                }
                catch { }
            }
            ShowFields();
        }

        void ShowPrinterProperties()
        {
            if (!_havePrinter) { ModernDialog.Info("No printer", "Install a printer in Windows Settings > Printers & scanners first."); return; }
            using (var pd = new PrintDialog { PrinterSettings = _ps, AllowSomePages = false, UseEXDialog = true })
            {
                _ps.Copies = (short)_copies.Value;
                _ps.DefaultPageSettings.Landscape = _landscape;
                try { _ps.DefaultPageSettings.PaperSize = CurrentPaper; } catch { }
                if (pd.ShowDialog(this) != DialogResult.OK) return;
                _syncing = true;
                int i = _printer.Items.IndexOf(_ps.PrinterName);
                if (i >= 0) _printer.SelectedIndex = i;
                _copies.Value = Math.Max(1, (int)_ps.Copies);
                _landscape = _ps.DefaultPageSettings.Landscape;
                _syncing = false;
                _havePrinter = SafeIsValid();
                string paper = null;
                try { paper = _ps.DefaultPageSettings.PaperSize.PaperName; } catch { }
                _papers.Clear();
                _paper.Items.Clear();
                if (paper != null) _lastPaper = paper;
                LoadPapers();
                if (paper != null && _paper.Items.IndexOf(paper) >= 0) { _paper.SelectedIndex = _paper.Items.IndexOf(paper); }
            }
        }

        // ================================================================ geometry

        SizeF PageSize()
        {
            PaperSize p = CurrentPaper;
            return _landscape ? new SizeF(p.Height, p.Width) : new SizeF(p.Width, p.Height);
        }

        RectangleF Printable()
        {
            SizeF page = PageSize();
            return new RectangleF(_hardX, _hardY, Math.Max(10, page.Width - 2 * _hardX), Math.Max(10, page.Height - 2 * _hardY));
        }

        /// <summary>The picture at 100 %: its pixels at the document's resolution.</summary>
        SizeF ActualSize()
        {
            return ActualSize(_image.Size);
        }

        SizeF ActualSize(Size px)
        {
            return new SizeF(px.Width / _ppi * 100f, px.Height / _ppi * 100f);
        }

        float FittedScale()
        {
            return FittedScale(_image.Size);
        }

        float FittedScale(Size px)
        {
            RectangleF area = Printable();
            SizeF a = ActualSize(px);
            return Math.Min(area.Width / a.Width, area.Height / a.Height);
        }

        /// <summary>
        /// Where a page of this pixel size lands with the current settings: fitted and centred
        /// pages each get their own fit / centre, otherwise the previewed page's scale and offset.
        /// </summary>
        RectangleF RectFor(Size px)
        {
            float scale = _fit ? FittedScale(px) : _scale;
            SizeF a = ActualSize(px);
            float w = a.Width * scale, h = a.Height * scale;
            if (!_center) return new RectangleF(_left, _top, w, h);
            SizeF page = PageSize();
            return new RectangleF((page.Width - w) / 2f, (page.Height - h) / 2f, w, h);
        }

        /// <summary>The pages the Print button sends (0-based), or null with a reason.</summary>
        List<int> PagesToPrint(out string error)
        {
            error = null;
            if (!MultiPage) return new List<int> { 0 };
            if (_pagesCombo.SelectedIndex == 0) return Enumerable.Range(0, _pageNames.Count).ToList();
            if (_pagesCombo.SelectedIndex == 1) return new List<int> { _currentPage };
            var list = new List<int>();
            int n = _pageNames.Count;
            foreach (string raw in _rangeBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                int dash = part.IndexOf('-');
                int a, b;
                if (dash < 0)
                {
                    if (!int.TryParse(part, out a)) { error = "\"" + part + "\" is not a page number."; return null; }
                    b = a;
                }
                else
                {
                    string left = part.Substring(0, dash).Trim(), right = part.Substring(dash + 1).Trim();
                    if (!int.TryParse(left.Length == 0 ? "1" : left, out a) || !int.TryParse(right.Length == 0 ? n.ToString() : right, out b))
                    { error = "\"" + part + "\" is not a page range."; return null; }
                }
                if (a < 1 || b < 1 || a > n || b > n) { error = "Pages run from 1 to " + n + "."; return null; }
                for (int i = a; a <= b ? i <= b : i >= b; i += a <= b ? 1 : -1)
                    if (!list.Contains(i - 1)) list.Add(i - 1);
            }
            if (list.Count == 0) { error = "Type the pages to print, e.g. 1-3, 5."; return null; }
            return list;
        }

        /// <summary>The preview (and the size fields) show another page.</summary>
        void ShowPreviewPage(int index)
        {
            if (index < 0 || index >= _pageNames.Count || index == _previewPage) return;
            Bitmap img;
            try { img = _render(index); }
            catch (Exception ex) { ModernDialog.Info("Could not show that page", ex.Message); return; }
            _image.Dispose();
            _thumb.Dispose();
            _image = img;
            _thumb = MakeThumb(img, 1400);
            _previewPage = index;
            _syncing = true;
            _previewCombo.SelectedIndex = index;
            _syncing = false;
            ShowFields();
        }

        /// <summary>Where the picture lands on the page, in hundredths of an inch from the paper's top-left corner.</summary>
        public RectangleF ImageRect()
        {
            if (_fit) _scale = FittedScale();
            SizeF a = ActualSize();
            float w = a.Width * _scale, h = a.Height * _scale;
            if (_center)
            {
                SizeF page = PageSize();
                _left = (page.Width - w) / 2f;
                _top = (page.Height - h) / 2f;
            }
            return new RectangleF(_left, _top, w, h);
        }

        float ToUnits(float hundredths) { return hundredths * UnitPerHundredth[_units]; }
        float FromUnits(decimal v) { return (float)v / UnitPerHundredth[_units]; }

        void ShowFields()
        {
            if (_papers.Count == 0) return;
            RectangleF r = ImageRect();
            _syncing = true;
            _centerChk.Checked = _center;
            _fitChk.Checked = _fit;
            _portraitBtn.Checked = !_landscape;
            _landscapeBtn.Checked = _landscape;
            int decimals = _units == 2 ? 1 : 2;
            foreach (ModernNumber n in new[] { _topBox, _leftBox, _wBox, _hBox })
            {
                n.DecimalPlaces = decimals;
                n.Increment = _units == 0 ? 0.1m : _units == 1 ? 0.1m : 1m;
            }
            _topBox.Value = (decimal)ToUnits(r.Y);
            _leftBox.Value = (decimal)ToUnits(r.X);
            _wBox.Value = (decimal)ToUnits(r.Width);
            _hBox.Value = (decimal)ToUnits(r.Height);
            _scaleBox.Value = (decimal)Math.Max(1, Math.Min(5000, _scale * 100));
            _syncing = false;

            float effectivePpi = _ppi / Math.Max(0.0001f, _scale);
            _resLbl.Text = string.Format(CultureInfo.CurrentCulture, "Print resolution: {0:0} PPI   ·   {1} × {2} px", effectivePpi, _image.Width, _image.Height);
            _resLbl.ForeColor = effectivePpi < 150 ? Theme.Danger : Theme.TextDim;

            var notes = new List<string>();
            if (!_havePrinter) notes.Add("No printer is available - the preview shows the paper you pick.");
            if (effectivePpi < 150) notes.Add("Under 150 PPI the print may look soft or pixelated.");
            RectangleF area = Printable();
            if (!area.Contains(r)) notes.Add("Part of the picture is outside the printable area and will be cut off.");
            bool warn = notes.Count > 0 && _havePrinter;
            string pagesError;
            List<int> pages = PagesToPrint(out pagesError);
            if (pagesError != null) notes.Insert(0, pagesError);
            else if (MultiPage && pages.Count > 1) notes.Add(pages.Count + " pages will print, each on its own sheet.");
            _status.Text = string.Join("\r\n", notes);
            _status.ForeColor = warn || pagesError != null ? Theme.Danger : Theme.TextDim;
            _printBtn.Text = pages != null && pages.Count > 1 ? "Print " + pages.Count + " pages" : "Print";
            _printBtn.Enabled = _havePrinter && pages != null;
            _preview.Invalidate();
        }

        void SetLandscape(bool landscape)
        {
            if (_landscape == landscape) return;
            _landscape = landscape;
            RefreshPage();
        }

        /// <summary>A drag in the preview moved the picture by this much (hundredths of an inch).</summary>
        void MoveBy(float dx, float dy)
        {
            RectangleF r = ImageRect();
            _center = false;
            _left = r.X + dx;
            _top = r.Y + dy;
            ShowFields();
        }

        // ================================================================ print

        void DoPrint()
        {
            if (!_havePrinter) return;
            _copies.Commit();
            string error;
            List<int> pages = PagesToPrint(out error);
            if (pages == null) { ModernDialog.Info("Which pages?", error); return; }
            ImageRect();   // settles the previewed page's scale and offset, which RectFor uses
            int next = 0;
            Bitmap current = null;
            try
            {
                using (var doc = new PrintDocument())
                {
                    doc.DocumentName = _docName;
                    doc.PrinterSettings = _ps;
                    doc.PrinterSettings.Copies = (short)_copies.Value;
                    doc.PrinterSettings.Collate = true;
                    doc.DefaultPageSettings = BuildPageSettings();
                    doc.OriginAtMargins = false;
                    doc.BeginPrint += delegate { next = 0; };
                    doc.PrintPage += delegate(object s, PrintPageEventArgs e)
                    {
                        // one page at a time, so a long document never holds every page in memory
                        int index = pages[next];
                        if (current != null && current != _image) current.Dispose();
                        current = index == _previewPage ? _image : _render(index);
                        RectangleF r = RectFor(current.Size);
                        Graphics g = e.Graphics;
                        g.PageUnit = GraphicsUnit.Display;   // hundredths of an inch on printers
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        // the printer's origin is its hard margin, not the paper corner
                        float hx = e.PageSettings.HardMarginX, hy = e.PageSettings.HardMarginY;
                        g.DrawImage(current, new RectangleF(r.X - hx, r.Y - hy, r.Width, r.Height));
                        next++;
                        e.HasMorePages = next < pages.Count;
                    };
                    doc.Print();
                }
                if (current != null && current != _image) { current.Dispose(); current = null; }
                _lastPrinter = _ps.PrinterName;
                _lastPaper = CurrentPaper.PaperName;
                _lastLandscape = _landscape;
                _lastUnits = _units;
                Toast.Show((pages.Count > 1 ? pages.Count + " pages sent to " : "Sent to ") + _ps.PrinterName + ".");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                if (current != null && current != _image) current.Dispose();
                ModernDialog.Info("Could not print", ex.Message);
            }
        }

        /// <summary>
        /// The print window for a document of <paramref name="pageNames"/>.Count pages;
        /// <paramref name="render"/> flattens one page (the dialog disposes what it returns).
        /// </summary>
        public static void Run(IWin32Window owner, Func<int, Bitmap> render, IList<string> pageNames, int currentPage, float ppi, string docName)
        {
            using (var d = new EditorPrintDialog(render, pageNames, currentPage, ppi, docName))
            {
                d.ShowDialog(owner);
                if (d._paper.SelectedItem != null)
                {
                    _lastPaper = d._paper.SelectedItem as string;
                    _lastLandscape = d._landscape;
                    _lastUnits = d._units;
                    if (d._havePrinter) _lastPrinter = d._ps.PrinterName;
                }
            }
        }

        // ================================================================ preview

        /// <summary>The sheet of paper, to scale, with the printable area dashed and the picture on it.</summary>
        sealed class PagePreview : Control
        {
            readonly EditorPrintDialog _d;
            bool _dragging;
            Point _last;

            public PagePreview(EditorPrintDialog d)
            {
                _d = d;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            float Zoom(out RectangleF pageOnScreen)
            {
                SizeF page = _d.PageSize();
                float z = Math.Min((Width - 80f) / page.Width, (Height - 80f) / page.Height);
                float pw = page.Width * z, ph = page.Height * z;
                pageOnScreen = new RectangleF((Width - pw) / 2f, (Height - ph) / 2f, pw, ph);
                return z;
            }

            RectangleF ImageOnScreen()
            {
                RectangleF page;
                float z = Zoom(out page);
                RectangleF r = _d.ImageRect();
                return new RectangleF(page.X + r.X * z, page.Y + r.Y * z, r.Width * z, r.Height * z);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (var bg = new SolidBrush(Theme.Dark ? Color.FromArgb(30, 30, 34) : Color.FromArgb(200, 200, 205))) g.FillRectangle(bg, ClientRectangle);
                if (_d._papers.Count == 0) return;
                RectangleF page;
                float z = Zoom(out page);

                using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(shadow, page.X + 4, page.Y + 5, page.Width, page.Height);
                g.FillRectangle(Brushes.White, page);

                // the picture, clipped to the paper
                RectangleF img = ImageOnScreen();
                GraphicsState st = g.Save();
                g.SetClip(page);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(_d._thumb, img);
                g.Restore(st);

                // the printable area and the picture's frame
                RectangleF area = _d.Printable();
                var areaOnScreen = new RectangleF(page.X + area.X * z, page.Y + area.Y * z, area.Width * z, area.Height * z);
                using (var dash = new Pen(Color.FromArgb(150, 120, 120, 130)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(dash, areaOnScreen.X, areaOnScreen.Y, areaOnScreen.Width, areaOnScreen.Height);
                using (var frame = new Pen(Theme.Accent, 1.4f))
                    g.DrawRectangle(frame, img.X, img.Y, img.Width, img.Height);

                string caption = string.Format(CultureInfo.CurrentCulture, "{0}  ·  {1:0.##} × {2:0.##} in",
                    _d.CurrentPaper.PaperName, _d.PageSize().Width / 100f, _d.PageSize().Height / 100f);
                TextRenderer.DrawText(g, caption, Theme.Small, new Rectangle(0, (int)page.Bottom + 10, Width, 20), Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || !ImageOnScreen().Contains(e.Location)) return;
                _dragging = true;
                _last = e.Location;
                Capture = true;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                Cursor = _dragging || ImageOnScreen().Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
                if (!_dragging) return;
                RectangleF page;
                float z = Zoom(out page);
                float dx = (e.X - _last.X) / z, dy = (e.Y - _last.Y) / z;
                _last = e.Location;
                if (dx != 0 || dy != 0) _d.MoveBy(dx, dy);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _dragging = false;
                Capture = false;
            }
        }
    }
}
