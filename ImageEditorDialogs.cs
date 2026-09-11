using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>Display names for the blend mode combo boxes, in enum order.</summary>
    static class BlendModes
    {
        public static readonly string[] Names =
        {
            "Normal",
            "Darken", "Multiply", "Color Burn", "Linear Burn",
            "Lighten", "Screen", "Color Dodge", "Linear Dodge (Add)",
            "Overlay", "Soft Light", "Hard Light",
            "Difference", "Exclusion",
            "Hue", "Saturation", "Color", "Luminosity"
        };
    }

    /// <summary>
    /// The frame every Image &gt; Adjustments and Filter dialog is built on: a column of
    /// labelled sliders (with a number box), check boxes, combos and colour wells, a
    /// Preview toggle, Reset, and OK/Cancel. Changes raise <see cref="Changed"/> after a
    /// short debounce so the caller can re-render a live preview.
    /// </summary>
    class AdjustDialog : PixelPerfectForm
    {
        public event EventHandler Changed;

        readonly Panel _body;
        readonly Dictionary<string, Control> _controls = new Dictionary<string, Control>();
        readonly Dictionary<string, object> _defaults = new Dictionary<string, object>();
        readonly Dictionary<string, ModernNumber> _numbers = new Dictionary<string, ModernNumber>();
        readonly Timer _debounce = new Timer { Interval = 60 };
        ModernCheckBox _preview;
        int _y = 16;
        bool _syncing;
        const int W = 460;

        public AdjustDialog(string title)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            Font = Theme.Base;
            ClientSize = new Size(W, 200);
            _body = new Panel { Location = new Point(0, 0), Size = new Size(W, 100), BackColor = Theme.Bg };
            Controls.Add(_body);
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                if (Changed != null) Changed(this, EventArgs.Empty);
            };
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        public bool PreviewOn { get { return _preview == null || _preview.Checked; } }

        bool _closed;

        void Bump()
        {
            if (_syncing || _closed) return;
            _debounce.Stop();
            _debounce.Start();
        }

        /// <summary>No preview may fire once the dialog is gone: the caller has freed its preview bitmaps by then.</summary>
        public void StopPreview()
        {
            _closed = true;
            _debounce.Stop();
            Changed = null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopPreview();
            base.OnFormClosed(e);
        }

        Label Caption(string text, int x, int y)
        {
            var l = new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Theme.TextDim, BackColor = Color.Transparent, Font = Theme.Small };
            _body.Controls.Add(l);
            return l;
        }

        /// <summary>A slider with a number box; <paramref name="suffix"/> decorates the read-out ("px", "%", "°").</summary>
        public ModernSlider AddSlider(string key, string label, int min, int max, int value, string suffix = "")
        {
            Caption(label, 24, _y + 3);
            int numW = 78 + (suffix.Length > 0 ? TextRenderer.MeasureText(suffix, Theme.Small).Width + 6 : 0);
            var num = new ModernNumber { Minimum = min, Maximum = max, Suffix = suffix, Location = new Point(W - 24 - numW, _y - 1), Size = new Size(numW, 26) };
            num.Value = Math.Max(min, Math.Min(max, value));
            var bar = new ModernSlider
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)),
                Location = new Point(18, _y + 26), Size = new Size(W - 36, 24)
            };
            bar.ValueChanged += delegate
            {
                if (_syncing) return;
                _syncing = true; num.Value = bar.Value; _syncing = false;
                Bump();
            };
            num.ValueChanged += delegate
            {
                if (_syncing) return;
                _syncing = true; bar.Value = (int)num.Value; _syncing = false;
                Bump();
            };
            _body.Controls.Add(num);
            _body.Controls.Add(bar);
            _controls[key] = bar;
            _numbers[key] = num;
            _defaults[key] = value;
            _y += 56;
            return bar;
        }

        public ModernCheckBox AddCheck(string key, string label, bool value)
        {
            var c = new ModernCheckBox { Text = label, Checked = value, Location = new Point(24, _y), Size = new Size(W - 48, 22) };
            c.CheckedChanged += delegate { Bump(); };
            _body.Controls.Add(c);
            _controls[key] = c;
            _defaults[key] = value;
            _y += 30;
            return c;
        }

        public ModernCombo AddCombo(string key, string label, string[] items, int index)
        {
            Caption(label, 24, _y + 5);
            var c = new ModernCombo { Location = new Point(W - 24 - 190, _y), Width = 190 };
            c.Items.AddRange(items);
            c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, index));
            c.SelectedIndexChanged += delegate { Bump(); };
            _body.Controls.Add(c);
            _controls[key] = c;
            _defaults[key] = index;
            _y += 34;
            return c;
        }

        public SwatchButton AddColor(string key, string label, Color value)
        {
            Caption(label, 24, _y + 4);
            var b = new SwatchButton(false) { Color = value, Location = new Point(W - 24 - 60, _y), Size = new Size(60, 26) };
            b.ColorChanged += delegate { Bump(); };
            _body.Controls.Add(b);
            _controls[key] = b;
            _defaults[key] = value;
            _y += 34;
            return b;
        }

        /// <summary>Hosts a custom control (histogram, curve grid) as a row.</summary>
        public void AddCustom(Control c, int height)
        {
            c.Location = new Point((W - c.Width) / 2, _y);
            _body.Controls.Add(c);
            _y += height + 10;
        }

        public void AddGap(int px) { _y += px; }

        /// <summary>Lays out the footer; call once after the rows are added.</summary>
        public void Finish(bool withPreview = true)
        {
            _body.Size = new Size(W, _y + 6);
            int footerY = _y + 12;
            var reset = new ModernButton { Text = "Reset", Size = new Size(84, 30), Location = new Point(24, footerY) };
            reset.Click += delegate { ResetAll(); };
            Controls.Add(reset);
            if (withPreview)
            {
                int pw = TextRenderer.MeasureText("Preview", Theme.Base).Width + 30;
                _preview = new ModernCheckBox { Text = "Preview", Checked = true, Location = new Point(reset.Right + 12, footerY + 4), Size = new Size(pw, 22) };
                _preview.CheckedChanged += delegate { Bump(); };
                Controls.Add(_preview);
            }
            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(92, 30), DialogResult = DialogResult.OK };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(92, 30), DialogResult = DialogResult.Cancel };
            ok.Location = new Point(W - 24 - 92, footerY);
            cancel.Location = new Point(ok.Left - 92 - 8, footerY);
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(W, footerY + 30 + 18);
            Theme.Apply(this);
            foreach (Control c in _body.Controls)
                if (c is Label) c.BackColor = Color.Transparent;
        }

        public void ResetAll()
        {
            _syncing = true;
            foreach (KeyValuePair<string, Control> kv in _controls)
            {
                object def = _defaults[kv.Key];
                var bar = kv.Value as ModernSlider;
                if (bar != null)
                {
                    bar.Value = (int)def;
                    ModernNumber num;
                    if (_numbers.TryGetValue(kv.Key, out num)) num.Value = (int)def;
                    continue;
                }
                var chk = kv.Value as ModernCheckBox;
                if (chk != null) { chk.Checked = (bool)def; continue; }
                var combo = kv.Value as ModernCombo;
                if (combo != null) { combo.SelectedIndex = (int)def; continue; }
                var sw = kv.Value as SwatchButton;
                if (sw != null) { sw.Color = (Color)def; continue; }
            }
            _syncing = false;
            Bump();
        }

        public int Value(string key) { return ((ModernSlider)_controls[key]).Value; }
        public bool Check(string key) { return ((ModernCheckBox)_controls[key]).Checked; }
        public int Index(string key) { return ((ModernCombo)_controls[key]).SelectedIndex; }
        public Color ColorOf(string key) { return ((SwatchButton)_controls[key]).Color; }

        public void SetValue(string key, int v)
        {
            var bar = (ModernSlider)_controls[key];
            _syncing = true;
            bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, v));
            ModernNumber num;
            if (_numbers.TryGetValue(key, out num)) num.Value = bar.Value;
            _syncing = false;
            Bump();
        }

        /// <summary>Fires Changed immediately (the first preview, before any slider moves).</summary>
        public void Kick() { Bump(); }
    }

    /// <summary>
    /// The Levels dialog's heart: a histogram with the three input triangles (black,
    /// gamma, white) under it and the two output triangles under that.
    /// </summary>
    class LevelsControl : Control
    {
        public int InBlack, InWhite = 255, OutBlack, OutWhite = 255;
        public float Gamma = 1f;
        public int[] Histogram;
        public event EventHandler Changed;

        int _drag = -1;     // 0 black, 1 gamma, 2 white, 3 out black, 4 out white
        const int Pad = 12;
        const int HistH = 96;

        public LevelsControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(352, HistH + 78);
            BackColor = Theme.Bg;
        }

        int Track { get { return Width - Pad * 2; } }
        float XOf(int level) { return Pad + level * (float)Track / 255f; }
        int LevelOf(float x) { return Math.Max(0, Math.Min(255, (int)Math.Round((x - Pad) * 255f / Track))); }

        int GammaLevel
        {
            get { return InBlack + (int)Math.Round((InWhite - InBlack) * Math.Pow(0.5, Gamma)); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var histRect = new Rectangle(Pad, 4, Track, HistH);
            using (var bg = new SolidBrush(Theme.FieldBg)) g.FillRectangle(bg, histRect);
            if (Histogram != null)
            {
                int max = 1;
                foreach (int v in Histogram) if (v > max) max = v;
                double lmax = Math.Log(max + 1);
                using (var bar = new SolidBrush(Theme.TextDim))
                {
                    for (int i = 0; i < 256; i++)
                    {
                        float x0 = XOf(i), x1 = XOf(i + 1);
                        float h = (float)(Math.Log(Histogram[i] + 1) / lmax) * (HistH - 4);
                        g.FillRectangle(bar, x0, histRect.Bottom - h, Math.Max(1f, x1 - x0), h);
                    }
                }
            }
            using (var edge = new Pen(Theme.Border)) g.DrawRectangle(edge, histRect);

            // input strip: a black→white ramp with the three triangles
            int inY = histRect.Bottom + 4;
            using (var ramp = new LinearGradientBrush(new Rectangle(Pad, inY, Track, 10), Color.Black, Color.White, LinearGradientMode.Horizontal))
                g.FillRectangle(ramp, Pad, inY, Track, 10);
            Triangle(g, XOf(InBlack), inY + 10, Color.Black);
            Triangle(g, XOf(GammaLevel), inY + 10, Color.Gray);
            Triangle(g, XOf(InWhite), inY + 10, Color.White);

            int outY = inY + 34;
            using (var ramp = new LinearGradientBrush(new Rectangle(Pad, outY, Track, 10), Color.Black, Color.White, LinearGradientMode.Horizontal))
                g.FillRectangle(ramp, Pad, outY, Track, 10);
            Triangle(g, XOf(OutBlack), outY + 10, Color.Black);
            Triangle(g, XOf(OutWhite), outY + 10, Color.White);

            using (var b = new SolidBrush(Theme.TextDim))
            {
                g.DrawString("Input", Theme.Small, b, Pad, inY + 20);
                g.DrawString("Output", Theme.Small, b, Pad, outY + 20);
            }
        }

        void Triangle(Graphics g, float x, int y, Color c)
        {
            var pts = new[] { new PointF(x, y), new PointF(x - 6, y + 10), new PointF(x + 6, y + 10) };
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
            using (var p = new Pen(Theme.Text, 1f)) g.DrawPolygon(p, pts);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int inY = 4 + HistH + 4 + 10, outY = inY + 34;
            float[] xs = { XOf(InBlack), XOf(GammaLevel), XOf(InWhite), XOf(OutBlack), XOf(OutWhite) };
            int best = -1; float bestD = 12;
            for (int i = 0; i < 5; i++)
            {
                int hy = i < 3 ? inY : outY;
                if (e.Y < hy - 8 || e.Y > hy + 16) continue;
                float d = Math.Abs(e.X - xs[i]);
                if (d < bestD) { bestD = d; best = i; }
            }
            _drag = best;
            if (_drag >= 0) Apply(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag >= 0) Apply(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = -1; }

        void Apply(float x)
        {
            int lv = LevelOf(x);
            switch (_drag)
            {
                case 0: InBlack = Math.Min(lv, InWhite - 2); break;
                case 2: InWhite = Math.Max(lv, InBlack + 2); break;
                case 1:
                {
                    float t = (lv - InBlack) / (float)Math.Max(1, InWhite - InBlack);
                    t = Math.Max(0.01f, Math.Min(0.99f, t));
                    Gamma = (float)(Math.Log(t) / Math.Log(0.5));
                    Gamma = Math.Max(0.1f, Math.Min(9.99f, Gamma));
                    break;
                }
                case 3: OutBlack = Math.Min(lv, OutWhite); break;
                case 4: OutWhite = Math.Max(lv, OutBlack); break;
            }
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void Set(int inB, int inW, float gamma, int outB, int outW)
        {
            InBlack = inB; InWhite = inW; Gamma = gamma; OutBlack = outB; OutWhite = outW;
            Invalidate();
        }
    }

    /// <summary>The Curves grid: click to add a point, drag to shape, drag off the grid to remove.</summary>
    class CurveControl : Control
    {
        public readonly List<PointF>[] Channels = { new List<PointF>(), new List<PointF>(), new List<PointF>(), new List<PointF>() };
        public int Channel;      // 0 RGB, 1 R, 2 G, 3 B
        public int[] Histogram;
        public event EventHandler Changed;

        int _dragIndex = -1;
        const int Pad = 10;
        const int Grid = 256;

        public CurveControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(Grid + Pad * 2, Grid + Pad * 2);
            BackColor = Theme.Bg;
            ResetAll();
        }

        public void ResetAll()
        {
            for (int c = 0; c < 4; c++)
            {
                Channels[c].Clear();
                Channels[c].Add(new PointF(0, 0));
                Channels[c].Add(new PointF(255, 255));
            }
            Invalidate();
        }

        public bool IsIdentity(int c)
        {
            List<PointF> p = Channels[c];
            return p.Count == 2 && p[0].X == 0 && p[0].Y == 0 && p[1].X == 255 && p[1].Y == 255;
        }

        List<PointF> Pts { get { return Channels[Channel]; } }

        PointF ToScreen(PointF p) { return new PointF(Pad + p.X, Pad + Grid - p.Y); }
        PointF ToLevel(Point s) { return new PointF(Math.Max(0, Math.Min(255, s.X - Pad)), Math.Max(0, Math.Min(255, Grid - (s.Y - Pad)))); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            var box = new Rectangle(Pad, Pad, Grid, Grid);
            using (var bg = new SolidBrush(Theme.FieldBg)) g.FillRectangle(bg, box);
            if (Histogram != null)
            {
                int max = 1;
                foreach (int v in Histogram) if (v > max) max = v;
                double lmax = Math.Log(max + 1);
                using (var bar = new SolidBrush(Color.FromArgb(70, Theme.TextDim)))
                    for (int i = 0; i < 256; i++)
                    {
                        float h = (float)(Math.Log(Histogram[i] + 1) / lmax) * Grid;
                        g.FillRectangle(bar, Pad + i, Pad + Grid - h, 1, h);
                    }
            }
            using (var grid = new Pen(Theme.Border))
            {
                for (int i = 1; i < 4; i++)
                {
                    g.DrawLine(grid, Pad + i * 64, Pad, Pad + i * 64, Pad + Grid);
                    g.DrawLine(grid, Pad, Pad + i * 64, Pad + Grid, Pad + i * 64);
                }
                g.DrawRectangle(grid, box);
                g.DrawLine(grid, Pad, Pad + Grid, Pad + Grid, Pad);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color ink = Channel == 1 ? Color.FromArgb(230, 70, 70) : Channel == 2 ? Color.FromArgb(60, 180, 90) : Channel == 3 ? Color.FromArgb(80, 120, 240) : Theme.Text;
            byte[] lut = PixelOps.CurveLut(Pts);
            var poly = new PointF[256];
            for (int i = 0; i < 256; i++) poly[i] = ToScreen(new PointF(i, lut[i]));
            using (var p = new Pen(ink, 1.8f)) g.DrawLines(p, poly);
            using (var fill = new SolidBrush(Theme.Surface))
            using (var edge = new Pen(ink, 1.5f))
                foreach (PointF pt in Pts)
                {
                    PointF s = ToScreen(pt);
                    g.FillEllipse(fill, s.X - 4, s.Y - 4, 8, 8);
                    g.DrawEllipse(edge, s.X - 4, s.Y - 4, 8, 8);
                }
        }

        int HitPoint(Point s)
        {
            for (int i = 0; i < Pts.Count; i++)
            {
                PointF p = ToScreen(Pts[i]);
                if (Math.Abs(p.X - s.X) <= 7 && Math.Abs(p.Y - s.Y) <= 7) return i;
            }
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int hit = HitPoint(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (hit > 0 && hit < Pts.Count - 1) { Pts.RemoveAt(hit); Fire(); }
                return;
            }
            if (hit >= 0) { _dragIndex = hit; return; }
            if (!new Rectangle(Pad, Pad, Grid, Grid).Contains(e.Location) || Pts.Count >= 16) return;
            PointF lv = ToLevel(e.Location);
            int insert = 0;
            while (insert < Pts.Count && Pts[insert].X < lv.X) insert++;
            Pts.Insert(insert, lv);
            _dragIndex = insert;
            Fire();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragIndex < 0) return;
            var box = new Rectangle(Pad - 30, Pad - 30, Grid + 60, Grid + 60);
            if (!box.Contains(e.Location) && _dragIndex > 0 && _dragIndex < Pts.Count - 1)
            {
                Pts.RemoveAt(_dragIndex);   // dragged off the grid: the point goes away
                _dragIndex = -1;
                Fire();
                return;
            }
            PointF lv = ToLevel(e.Location);
            float minX = _dragIndex == 0 ? 0 : Pts[_dragIndex - 1].X + 1;
            float maxX = _dragIndex == Pts.Count - 1 ? 255 : Pts[_dragIndex + 1].X - 1;
            if (_dragIndex == 0) lv.X = 0;
            else if (_dragIndex == Pts.Count - 1) lv.X = 255;
            else lv.X = Math.Max(minX, Math.Min(maxX, lv.X));
            Pts[_dragIndex] = lv;
            Fire();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragIndex = -1; }

        void Fire()
        {
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Layer Style: the effect list on the left (tick to enable), the chosen effect's
    /// settings on the right, and a live preview on the layer while it is open.
    /// </summary>
    class LayerStyleDialog : PixelPerfectForm
    {
        public readonly LayerEffects Fx;
        public event EventHandler Changed;

        readonly ListBox _list;
        readonly Panel _page;
        readonly Timer _debounce = new Timer { Interval = 60 };
        static readonly string[] Names = { "Drop Shadow", "Outer Glow", "Stroke", "Color Overlay" };
        bool _building;

        public LayerStyleDialog(LayerEffects fx, int page)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            Fx = fx;
            Text = "Layer Style";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            Font = Theme.Base;
            ClientSize = new Size(560, 380);

            _list = new ListBox
            {
                Location = new Point(16, 16), Size = new Size(160, 290),
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 30,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.FieldBg, ForeColor = Theme.Text, IntegralHeight = false
            };
            _list.Items.AddRange(Names);
            _list.DrawItem += List_DrawItem;
            _list.MouseDown += List_MouseDown;
            _list.SelectedIndexChanged += delegate { BuildPage(); };
            Controls.Add(_list);

            _page = new Panel { Location = new Point(192, 16), Size = new Size(352, 290), BackColor = Theme.Surface };
            _page.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, _page.Width - 1, _page.Height - 1);
            };
            Controls.Add(_page);

            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(92, 30), DialogResult = DialogResult.OK, Location = new Point(560 - 16 - 92, 330) };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(92, 30), DialogResult = DialogResult.Cancel, Location = new Point(560 - 16 - 92 - 100, 330) };
            var clear = new ModernButton { Text = "Clear all", Size = new Size(92, 30), Location = new Point(16, 330) };
            clear.Click += delegate
            {
                Fx.DropShadow = Fx.OuterGlow = Fx.Stroke = Fx.ColorOverlay = false;
                _list.Invalidate();
                BuildPage();
                Bump();
            };
            Controls.Add(ok); Controls.Add(cancel); Controls.Add(clear);
            AcceptButton = ok;
            CancelButton = cancel;
            _debounce.Tick += delegate { _debounce.Stop(); if (Changed != null) Changed(this, EventArgs.Empty); };
            _list.SelectedIndex = Math.Max(0, Math.Min(3, page));
            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        bool _closed;

        void Bump() { if (_closed) return; _debounce.Stop(); _debounce.Start(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closed = true;
            _debounce.Stop();
            Changed = null;
            base.OnFormClosed(e);
        }

        bool IsOn(int i)
        {
            switch (i) { case 0: return Fx.DropShadow; case 1: return Fx.OuterGlow; case 2: return Fx.Stroke; default: return Fx.ColorOverlay; }
        }

        void SetEnabled(int i, bool on)
        {
            switch (i) { case 0: Fx.DropShadow = on; break; case 1: Fx.OuterGlow = on; break; case 2: Fx.Stroke = on; break; default: Fx.ColorOverlay = on; break; }
        }

        void List_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var bg = new SolidBrush(sel ? Color.FromArgb(Theme.Dark ? 46 : 30, Theme.Accent) : Theme.FieldBg)) g.FillRectangle(bg, e.Bounds);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 7, 15, 15);
            using (GraphicsPath rp = Theme.Round(box, 3))
            {
                if (IsOn(e.Index))
                {
                    using (var f = new SolidBrush(Theme.Accent)) g.FillPath(f, rp);
                    using (var tick = new Pen(Theme.OnAccent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLines(tick, new[] { new PointF(box.X + 3.5f, box.Y + 7.5f), new PointF(box.X + 6.5f, box.Y + 10.5f), new PointF(box.X + 11.5f, box.Y + 4.5f) });
                }
                else
                    using (var p = new Pen(Theme.FieldBorder)) g.DrawPath(p, rp);
            }
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString(Names[e.Index], Theme.Base, b, e.Bounds.X + 30, e.Bounds.Y + 7);
        }

        void List_MouseDown(object sender, MouseEventArgs e)
        {
            int i = _list.IndexFromPoint(e.Location);
            if (i < 0) return;
            if (e.X < 28)
            {
                SetEnabled(i, !IsOn(i));
                _list.Invalidate();
                if (_list.SelectedIndex == i) BuildPage();
                Bump();
            }
        }

        void BuildPage()
        {
            _building = true;
            _page.Controls.Clear();
            int i = _list.SelectedIndex;
            int y = 14;
            var title = new Label { Text = Names[i], Font = Theme.Strong, ForeColor = Theme.Text, BackColor = Color.Transparent, Location = new Point(16, y), AutoSize = true };
            _page.Controls.Add(title);
            var enable = new ModernCheckBox { Text = "Enable " + Names[i], Location = new Point(16, y + 26), Size = new Size(300, 22), Checked = IsOn(i) };
            enable.CheckedChanged += delegate { if (_building) return; SetEnabled(i, enable.Checked); _list.Invalidate(); Bump(); };
            _page.Controls.Add(enable);
            y += 62;
            switch (i)
            {
                case 0:
                    Slider(ref y, "Opacity", 0, 100, Fx.ShadowOpacity, v => Fx.ShadowOpacity = v);
                    Slider(ref y, "Angle", 0, 360, Fx.ShadowAngle, v => Fx.ShadowAngle = v);
                    Slider(ref y, "Distance", 0, 100, Fx.ShadowDistance, v => Fx.ShadowDistance = v);
                    Slider(ref y, "Size", 0, 80, Fx.ShadowSize, v => Fx.ShadowSize = v);
                    ColorRow(ref y, "Colour", Fx.ShadowColor, c => Fx.ShadowColor = c);
                    break;
                case 1:
                    Slider(ref y, "Opacity", 0, 100, Fx.GlowOpacity, v => Fx.GlowOpacity = v);
                    Slider(ref y, "Size", 0, 80, Fx.GlowSize, v => Fx.GlowSize = v);
                    ColorRow(ref y, "Colour", Fx.GlowColor, c => Fx.GlowColor = c);
                    break;
                case 2:
                    Slider(ref y, "Size", 1, 40, Fx.StrokeSize, v => Fx.StrokeSize = v);
                    Slider(ref y, "Opacity", 0, 100, Fx.StrokeOpacity, v => Fx.StrokeOpacity = v);
                    Combo(ref y, "Position", new[] { "Outside", "Inside", "Center" }, Fx.StrokePosition, v => Fx.StrokePosition = v);
                    ColorRow(ref y, "Colour", Fx.StrokeColor, c => Fx.StrokeColor = c);
                    break;
                default:
                    Slider(ref y, "Opacity", 0, 100, Fx.OverlayOpacity, v => Fx.OverlayOpacity = v);
                    ColorRow(ref y, "Colour", Fx.OverlayColor, c => Fx.OverlayColor = c);
                    break;
            }
            _building = false;
        }

        void Slider(ref int y, string label, int min, int max, int value, Action<int> set)
        {
            var l = new Label { Text = label, Location = new Point(16, y), AutoSize = true, ForeColor = Theme.TextDim, BackColor = Color.Transparent, Font = Theme.Small };
            var num = new ModernNumber { Minimum = min, Maximum = max, Size = new Size(74, 26), Location = new Point(_page.Width - 16 - 74, y - 4) };
            num.Value = Math.Max(min, Math.Min(max, value));
            var bar = new ModernSlider
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)),
                Location = new Point(12, y + 22), Size = new Size(_page.Width - 24, 24)
            };
            bool sync = false;
            bar.ValueChanged += delegate { if (sync || _building) return; sync = true; num.Value = bar.Value; sync = false; set(bar.Value); Bump(); };
            num.ValueChanged += delegate { if (sync || _building) return; sync = true; bar.Value = (int)num.Value; sync = false; set(bar.Value); Bump(); };
            _page.Controls.Add(l); _page.Controls.Add(num); _page.Controls.Add(bar);
            y += 50;
        }

        void ColorRow(ref int y, string label, Color value, Action<Color> set)
        {
            var l = new Label { Text = label, Location = new Point(16, y + 4), AutoSize = true, ForeColor = Theme.TextDim, BackColor = Color.Transparent, Font = Theme.Small };
            var sw = new SwatchButton(false) { Color = value, Location = new Point(_page.Width - 16 - 60, y), Size = new Size(60, 26) };
            sw.ColorChanged += delegate { if (_building) return; set(sw.Color); Bump(); };
            _page.Controls.Add(l); _page.Controls.Add(sw);
            y += 34;
        }

        void Combo(ref int y, string label, string[] items, int index, Action<int> set)
        {
            var l = new Label { Text = label, Location = new Point(16, y + 4), AutoSize = true, ForeColor = Theme.TextDim, BackColor = Color.Transparent, Font = Theme.Small };
            var c = new ModernCombo { Location = new Point(_page.Width - 16 - 140, y), Width = 140 };
            c.Items.AddRange(items);
            c.SelectedIndex = index;
            c.SelectedIndexChanged += delegate { if (_building) return; set(c.SelectedIndex); Bump(); };
            _page.Controls.Add(l); _page.Controls.Add(c);
            y += 34;
        }
    }

    /// <summary>Width/height dialog for File &gt; New and Image &gt; Image Size, in the app's style.</summary>
    class CanvasSizeDialog : PixelPerfectForm
    {
        readonly ModernNumber _w, _h;
        readonly ModernCheckBox _scale;
        readonly ModernRadioButton _white, _transparent, _fg;
        readonly Size _original;
        readonly ModernCheckBox _lockRatio;
        bool _linking;

        CanvasSizeDialog(string title, int w, int h, bool askBackground, bool white, bool askScale, Size original)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            _original = original;

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, askBackground || askScale ? 212 : 156);
            BackColor = Theme.Bg;
            Font = Theme.Base;

            var wl = new Label { Text = "Width (px)", Location = new Point(24, 24), AutoSize = true, ForeColor = Theme.TextDim };
            _w = new ModernNumber { Minimum = 1, Maximum = 20000, Location = new Point(24, 44), Width = 130, Suffix = "px" };
            _w.Value = Math.Max(1, Math.Min(20000, w));
            var hl = new Label { Text = "Height (px)", Location = new Point(182, 24), AutoSize = true, ForeColor = Theme.TextDim };
            _h = new ModernNumber { Minimum = 1, Maximum = 20000, Location = new Point(182, 44), Width = 130, Suffix = "px" };
            _h.Value = Math.Max(1, Math.Min(20000, h));
            Controls.Add(wl); Controls.Add(_w); Controls.Add(hl); Controls.Add(_h);

            int y = 78;
            if (askScale)
            {
                _lockRatio = new ModernCheckBox { Text = "Constrain proportions", Location = new Point(24, y), Size = new Size(290, 22), Checked = true };
                Controls.Add(_lockRatio);
                y += 28;
                _scale = new ModernCheckBox { Text = "Resample (scale the layers with the canvas)", Location = new Point(24, y), Size = new Size(300, 22), Checked = true };
                Controls.Add(_scale);
                y += 30;
                _w.ValueChanged += LinkedW;
                _h.ValueChanged += LinkedH;
            }
            if (askBackground)
            {
                var bl = new Label { Text = "Background contents", Location = new Point(24, y), AutoSize = true, ForeColor = Theme.TextDim, Font = Theme.Small };
                Controls.Add(bl);
                y += 20;
                _white = new ModernRadioButton { Text = "White", Location = new Point(24, y), Size = new Size(80, 22), Checked = white };
                _transparent = new ModernRadioButton { Text = "Transparent", Location = new Point(110, y), Size = new Size(110, 22), Checked = !white };
                _fg = new ModernRadioButton { Text = "Background colour", Location = new Point(222, y), Size = new Size(120, 22) };
                Controls.Add(_white); Controls.Add(_transparent); Controls.Add(_fg);
                y += 30;
            }

            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(96, 32), DialogResult = DialogResult.OK };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(96, 32), DialogResult = DialogResult.Cancel };
            ok.Location = new Point(ClientSize.Width - 96 - 24, ClientSize.Height - 44);
            cancel.Location = new Point(ok.Left - 96 - 8, ClientSize.Height - 44);
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        void LinkedW(object sender, EventArgs e)
        {
            if (_linking || _lockRatio == null || !_lockRatio.Checked || _original.Width < 1) return;
            _linking = true;
            decimal v = Math.Round(_w.Value * _original.Height / _original.Width);
            _h.Value = Math.Max(_h.Minimum, Math.Min(_h.Maximum, v));
            _linking = false;
        }

        void LinkedH(object sender, EventArgs e)
        {
            if (_linking || _lockRatio == null || !_lockRatio.Checked || _original.Height < 1) return;
            _linking = true;
            decimal v = Math.Round(_h.Value * _original.Width / _original.Height);
            _w.Value = Math.Max(_w.Minimum, Math.Min(_w.Maximum, v));
            _linking = false;
        }

        /// <summary>background: 0 white, 1 transparent, 2 the editor's background colour.</summary>
        public static bool Ask(IWin32Window owner, string title, ref int w, ref int h, bool askBackground, ref int background)
        {
            using (var dialog = new CanvasSizeDialog(title, w, h, askBackground, background == 0, false, Size.Empty))
            {
                if (askBackground)
                {
                    dialog._white.Checked = background == 0;
                    dialog._transparent.Checked = background == 1;
                    dialog._fg.Checked = background == 2;
                }
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                w = (int)dialog._w.Value;
                h = (int)dialog._h.Value;
                if (dialog._white != null) background = dialog._white.Checked ? 0 : dialog._transparent.Checked ? 1 : 2;
                return true;
            }
        }

        public static bool AskResize(IWin32Window owner, ref int w, ref int h, Size original, ref bool scaleLayers)
        {
            using (var dialog = new CanvasSizeDialog("Image Size", w, h, false, true, true, original))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                w = (int)dialog._w.Value;
                h = (int)dialog._h.Value;
                scaleLayers = dialog._scale.Checked;
                return true;
            }
        }
    }

    /// <summary>Image &gt; Canvas Size: new dimensions plus the anchor that says which side grows.</summary>
    class CanvasExtendDialog : PixelPerfectForm
    {
        readonly ModernNumber _w, _h;
        readonly ModernCheckBox _relative;
        readonly Button[] _anchors = new Button[9];
        int _anchor = 4;
        readonly Size _original;

        CanvasExtendDialog(Size original)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            _original = original;
            Text = "Canvas Size";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 250);
            BackColor = Theme.Bg;
            Font = Theme.Base;

            var cur = new Label
            {
                Text = "Current size: " + original.Width + " × " + original.Height + " px",
                Location = new Point(24, 16), AutoSize = true, ForeColor = Theme.TextDim, Font = Theme.Small
            };
            Controls.Add(cur);
            var wl = new Label { Text = "Width", Location = new Point(24, 44), AutoSize = true, ForeColor = Theme.TextDim };
            _w = new ModernNumber { Minimum = -20000, Maximum = 20000, Location = new Point(24, 64), Width = 120, Suffix = "px" };
            _w.Value = original.Width;
            var hl = new Label { Text = "Height", Location = new Point(24, 96), AutoSize = true, ForeColor = Theme.TextDim };
            _h = new ModernNumber { Minimum = -20000, Maximum = 20000, Location = new Point(24, 116), Width = 120, Suffix = "px" };
            _h.Value = original.Height;
            _relative = new ModernCheckBox { Text = "Relative", Location = new Point(24, 150), Size = new Size(120, 22) };
            _relative.CheckedChanged += delegate
            {
                _w.Value = _relative.Checked ? 0 : original.Width;
                _h.Value = _relative.Checked ? 0 : original.Height;
            };
            Controls.Add(wl); Controls.Add(_w); Controls.Add(hl); Controls.Add(_h); Controls.Add(_relative);

            var al = new Label { Text = "Anchor", Location = new Point(200, 44), AutoSize = true, ForeColor = Theme.TextDim };
            Controls.Add(al);
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Size = new Size(34, 34), Location = new Point(200 + (i % 3) * 36, 64 + (i / 3) * 36),
                    FlatStyle = FlatStyle.Flat, BackColor = Theme.FieldBg, ForeColor = Theme.Text, TabStop = false,
                    Font = new Font("Segoe UI", 11f)
                };
                b.FlatAppearance.BorderColor = Theme.Border;
                b.Click += delegate { _anchor = idx; PaintAnchors(); };
                _anchors[i] = b;
                Controls.Add(b);
            }
            PaintAnchors();

            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(96, 32), DialogResult = DialogResult.OK, Location = new Point(360 - 96 - 24, 250 - 44) };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(96, 32), DialogResult = DialogResult.Cancel, Location = new Point(360 - 96 - 24 - 104, 250 - 44) };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        void PaintAnchors()
        {
            // arrows point away from the anchor: the canvas grows in those directions
            string[] arrows = { "↖", "↑", "↗", "←", "•", "→", "↙", "↓", "↘" };
            int ar = _anchor / 3, ac = _anchor % 3;
            for (int i = 0; i < 9; i++)
            {
                int r = i / 3, c = i % 3;
                string t = i == _anchor ? "•" : "";
                if (i != _anchor && Math.Abs(r - ar) <= 1 && Math.Abs(c - ac) <= 1)
                {
                    int dr = r - ar, dc = c - ac;
                    t = arrows[(dr + 1) * 3 + (dc + 1)];
                }
                _anchors[i].Text = t;
                _anchors[i].BackColor = i == _anchor ? Theme.Accent : Theme.FieldBg;
                _anchors[i].ForeColor = i == _anchor ? Theme.OnAccent : Theme.Text;
            }
        }

        /// <summary>Returns the new size and the offset the existing content moves by.</summary>
        public static bool Ask(IWin32Window owner, Size original, out Size size, out Point offset)
        {
            size = original;
            offset = Point.Empty;
            using (var d = new CanvasExtendDialog(original))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return false;
                int w = (int)d._w.Value, h = (int)d._h.Value;
                if (d._relative.Checked) { w += original.Width; h += original.Height; }
                w = Math.Max(1, w); h = Math.Max(1, h);
                int dx = w - original.Width, dy = h - original.Height;
                int ac = d._anchor % 3, ar = d._anchor / 3;
                offset = new Point(ac == 0 ? 0 : ac == 1 ? dx / 2 : dx, ar == 0 ? 0 : ar == 1 ? dy / 2 : dy);
                size = new Size(w, h);
                return true;
            }
        }
    }

    /// <summary>A one-line themed prompt (layer rename, asset category names).</summary>
    class EditorPrompt : PixelPerfectForm
    {
        readonly TextBox _box;

        EditorPrompt(string title, string label, string initial)
        {
            Theme.Init(ThemeHelper.IsDarkMode);

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 130);
            BackColor = Theme.Bg;
            Font = Theme.Base;

            var caption = new Label { Text = label, Location = new Point(24, 18), AutoSize = true, ForeColor = Theme.TextDim };
            _box = new TextBox
            {
                Location = new Point(24, 40), Width = 292,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.FieldBg, ForeColor = Theme.Text, Text = initial
            };
            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(96, 32), DialogResult = DialogResult.OK, Location = new Point(220, 82) };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(96, 32), DialogResult = DialogResult.Cancel, Location = new Point(116, 82) };
            Controls.Add(caption); Controls.Add(_box); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        public static string Ask(IWin32Window owner, string title, string label, string initial)
        {
            using (var dialog = new EditorPrompt(title, label, initial))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                return dialog._box.Text.Trim();
            }
        }
    }

    /// <summary>"Feather Radius: [ 5 ] pixels" - one number, for Select &gt; Modify and friends.</summary>
    class NumberPrompt : PixelPerfectForm
    {
        readonly ModernNumber _num;

        NumberPrompt(string title, string label, int min, int max, int value, string unit)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 120);
            BackColor = Theme.Bg;
            Font = Theme.Base;
            var caption = new Label { Text = label, Location = new Point(24, 26), AutoSize = true, ForeColor = Theme.TextDim };
            _num = new ModernNumber { Minimum = min, Maximum = max, Location = new Point(190, 22), Width = 90 };
            _num.Value = Math.Max(min, Math.Min(max, value));
            var unitL = new Label { Text = unit, Location = new Point(286, 26), AutoSize = true, ForeColor = Theme.TextDim };
            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(96, 32), DialogResult = DialogResult.OK, Location = new Point(220, 72) };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(96, 32), DialogResult = DialogResult.Cancel, Location = new Point(116, 72) };
            Controls.Add(caption); Controls.Add(_num); Controls.Add(unitL); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
            Shown += delegate { _num.Focus(); _num.SelectAll(); };
        }

        public static int? Ask(IWin32Window owner, string title, string label, int min, int max, int value, string unit)
        {
            using (var d = new NumberPrompt(title, label, min, max, value, unit))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                return (int)d._num.Value;
            }
        }
    }

    /// <summary>Edit &gt; Fill: what to fill with, how it blends, how strong.</summary>
    class FillDialog : PixelPerfectForm
    {
        public int Contents;            // 0 fg, 1 bg, 2 colour, 3 black, 4 50% grey, 5 white
        public Color CustomColor = Color.Gray;
        public BlendMode Mode = BlendMode.Normal;
        public int FillOpacity = 100;
        public bool PreserveTransparency;

        FillDialog(Color fg, Color bg)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            Text = "Fill";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 236);
            BackColor = Theme.Bg;
            Font = Theme.Base;

            var cl = new Label { Text = "Contents", Location = new Point(24, 22), AutoSize = true, ForeColor = Theme.TextDim };
            var contents = new ModernCombo { Location = new Point(130, 18), Width = 150 };
            contents.Items.AddRange(new[] { "Foreground Color", "Background Color", "Color…", "Black", "50% Gray", "White" });
            contents.SelectedIndex = 0;
            var swatch = new SwatchButton(false) { Color = fg, Location = new Point(290, 18), Size = new Size(46, 26) };
            contents.SelectedIndexChanged += delegate
            {
                Contents = contents.SelectedIndex;
                if (Contents == 2)
                {
                    using (var cd = new ColorDialog { Color = CustomColor, FullOpen = true })
                        if (cd.ShowDialog(this) == DialogResult.OK) CustomColor = cd.Color;
                }
                swatch.Color = Contents == 0 ? fg : Contents == 1 ? bg : Contents == 2 ? CustomColor : Contents == 3 ? Color.Black : Contents == 4 ? Color.Gray : Color.White;
            };
            swatch.ColorChanged += delegate { CustomColor = swatch.Color; contents.SelectedIndex = 2; };
            var ml = new Label { Text = "Mode", Location = new Point(24, 62), AutoSize = true, ForeColor = Theme.TextDim };
            var mode = new ModernCombo { Location = new Point(130, 58), Width = 150 };
            mode.Items.AddRange(BlendModes.Names);
            mode.SelectedIndex = 0;
            mode.SelectedIndexChanged += delegate { Mode = (BlendMode)mode.SelectedIndex; };
            var ol = new Label { Text = "Opacity", Location = new Point(24, 102), AutoSize = true, ForeColor = Theme.TextDim };
            var op = new ModernNumber { Minimum = 1, Maximum = 100, Location = new Point(130, 98), Width = 80, Suffix = "%" };
            op.Value = 100;
            op.ValueChanged += delegate { FillOpacity = (int)op.Value; };
            var pl = new Label { Text = "", Location = new Point(204, 102), AutoSize = true, ForeColor = Theme.TextDim };
            var pres = new ModernCheckBox { Text = "Preserve transparency", Location = new Point(24, 136), Size = new Size(240, 22) };
            pres.CheckedChanged += delegate { PreserveTransparency = pres.Checked; };
            Controls.Add(cl); Controls.Add(contents); Controls.Add(swatch); Controls.Add(ml); Controls.Add(mode);
            Controls.Add(ol); Controls.Add(op); Controls.Add(pl); Controls.Add(pres);

            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(96, 32), DialogResult = DialogResult.OK, Location = new Point(360 - 96 - 24, 236 - 44) };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(96, 32), DialogResult = DialogResult.Cancel, Location = new Point(360 - 96 - 24 - 104, 236 - 44) };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        public Color ResolvedColor(Color fg, Color bg)
        {
            switch (Contents)
            {
                case 0: return fg;
                case 1: return bg;
                case 2: return CustomColor;
                case 3: return Color.Black;
                case 4: return Color.FromArgb(128, 128, 128);
                default: return Color.White;
            }
        }

        public static FillDialog Ask(IWin32Window owner, Color fg, Color bg)
        {
            var d = new FillDialog(fg, bg);
            if (d.ShowDialog(owner) != DialogResult.OK) { d.Dispose(); return null; }
            return d;
        }
    }

    /// <summary>Edit &gt; Stroke: outline the selection on the layer.</summary>
    class StrokeDialog : PixelPerfectForm
    {
        public int StrokeWidth = 3;
        public Color StrokeColor;
        public int StrokeLocation;    // 0 inside, 1 centre, 2 outside
        public int StrokeOpacity = 100;

        StrokeDialog(Color initial)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            StrokeColor = initial;
            Text = "Stroke";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 236);
            BackColor = Theme.Bg;
            Font = Theme.Base;

            var wl = new Label { Text = "Width", Location = new Point(24, 22), AutoSize = true, ForeColor = Theme.TextDim };
            var w = new ModernNumber { Minimum = 1, Maximum = 250, Location = new Point(120, 18), Width = 84, Suffix = "px" };
            w.Value = 3;
            w.ValueChanged += delegate { StrokeWidth = (int)w.Value; };
            var pxl = new Label { Text = "", Location = new Point(194, 22), AutoSize = true, ForeColor = Theme.TextDim };
            var cl = new Label { Text = "Colour", Location = new Point(24, 60), AutoSize = true, ForeColor = Theme.TextDim };
            var sw = new SwatchButton(false) { Color = initial, Location = new Point(120, 56), Size = new Size(60, 26) };
            sw.ColorChanged += delegate { StrokeColor = sw.Color; };
            var ll = new Label { Text = "Location", Location = new Point(24, 100), AutoSize = true, ForeColor = Theme.TextDim, Font = Theme.Small };
            var inside = new ModernRadioButton { Text = "Inside", Location = new Point(24, 120), Size = new Size(80, 22), Checked = true };
            var centre = new ModernRadioButton { Text = "Center", Location = new Point(110, 120), Size = new Size(80, 22) };
            var outside = new ModernRadioButton { Text = "Outside", Location = new Point(196, 120), Size = new Size(90, 22) };
            inside.CheckedChanged += delegate { if (inside.Checked) StrokeLocation = 0; };
            centre.CheckedChanged += delegate { if (centre.Checked) StrokeLocation = 1; };
            outside.CheckedChanged += delegate { if (outside.Checked) StrokeLocation = 2; };
            var ol = new Label { Text = "Opacity", Location = new Point(24, 156), AutoSize = true, ForeColor = Theme.TextDim };
            var op = new ModernNumber { Minimum = 1, Maximum = 100, Location = new Point(120, 152), Width = 84, Suffix = "%" };
            op.Value = 100;
            op.ValueChanged += delegate { StrokeOpacity = (int)op.Value; };
            var pl = new Label { Text = "", Location = new Point(194, 156), AutoSize = true, ForeColor = Theme.TextDim };
            Controls.Add(wl); Controls.Add(w); Controls.Add(pxl); Controls.Add(cl); Controls.Add(sw); Controls.Add(ll);
            Controls.Add(inside); Controls.Add(centre); Controls.Add(outside); Controls.Add(ol); Controls.Add(op); Controls.Add(pl);
            var ok = new ModernButton { Text = "OK", Accent = true, Size = new Size(96, 32), DialogResult = DialogResult.OK, Location = new Point(340 - 96 - 24, 236 - 44) };
            var cancel = new ModernButton { Text = "Cancel", Size = new Size(96, 32), DialogResult = DialogResult.Cancel, Location = new Point(340 - 96 - 24 - 104, 236 - 44) };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }

        public static StrokeDialog Ask(IWin32Window owner, Color initial)
        {
            var d = new StrokeDialog(initial);
            if (d.ShowDialog(owner) != DialogResult.OK) { d.Dispose(); return null; }
            return d;
        }
    }

    /// <summary>Help &gt; Keyboard Shortcuts: every key the editor answers to, grouped.</summary>
    class ShortcutsDialog : PixelPerfectForm
    {
        public ShortcutsDialog(IList<KeyValuePair<string, string>> rows)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            Text = "Image Editor Shortcuts";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 560);
            BackColor = Theme.Bg;
            Font = Theme.Base;
            var list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Theme.FieldBg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, ShowGroups = true
            };
            list.Columns.Add("Command", 300);
            list.Columns.Add("Keys", 190);
            ListViewGroup group = null;
            foreach (KeyValuePair<string, string> kv in rows)
            {
                if (kv.Value == null)
                {
                    group = new ListViewGroup(kv.Key);
                    list.Groups.Add(group);
                    continue;
                }
                var item = new ListViewItem(new[] { kv.Key, kv.Value });
                if (group != null) item.Group = group;
                list.Items.Add(item);
            }
            Controls.Add(list);
            Theme.Apply(this);
            HandleCreated += delegate { Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode); };
        }
    }
}
