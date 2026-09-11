using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// A number field in the app's own style: a rounded well with the value, an optional
    /// unit after it, and two small chevrons on the right. Replaces the system
    /// NumericUpDown wherever the editor shows a number. Type and press Enter (or leave),
    /// click or hold the chevrons, roll the wheel, or use the arrow keys.
    /// </summary>
    public class ModernNumber : Control
    {
        readonly TextBox _box;
        readonly Timer _repeat = new Timer { Interval = 60 };
        decimal _value, _min, _max = 100, _inc = 1;
        int _decimals;
        string _suffix = "";
        bool _hoverUp, _hoverDown, _pressUp, _pressDown;
        int _repeatDir, _repeatTicks;
        const int Arrows = 16;

        public event EventHandler ValueChanged;

        public ModernNumber()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            Size = new Size(66, 26);
            BackColor = Theme.FieldBg;
            Cursor = Cursors.Default;
            _box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Right,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                Font = Theme.Base,
                Text = "0"
            };
            _box.KeyDown += Box_KeyDown;
            _box.LostFocus += delegate { Commit(); Invalidate(); };
            _box.GotFocus += delegate { Invalidate(); BeginInvoke(new Action(() => { if (_box.Focused) _box.SelectAll(); })); };
            _box.MouseWheel += delegate(object s, MouseEventArgs e) { Step(e.Delta > 0 ? 1 : -1); };
            Controls.Add(_box);
            _repeat.Tick += delegate
            {
                _repeatTicks++;
                if (_repeatTicks > 6) Step(_repeatDir);   // ~350 ms before auto-repeat kicks in
            };
            LayoutBox();
        }

        public decimal Minimum { get { return _min; } set { _min = value; if (_max < _min) _max = _min; Value = _value; } }
        public decimal Maximum { get { return _max; } set { _max = value; if (_min > _max) _min = _max; Value = _value; } }
        public decimal Increment { get { return _inc; } set { _inc = value <= 0 ? 1 : value; } }
        public int DecimalPlaces { get { return _decimals; } set { _decimals = Math.Max(0, Math.Min(4, value)); UpdateText(); } }
        public string Suffix { get { return _suffix; } set { _suffix = value ?? ""; LayoutBox(); Invalidate(); } }

        public decimal Value
        {
            get { return _value; }
            set
            {
                decimal v = Math.Round(Math.Max(_min, Math.Min(_max, value)), _decimals);
                bool changed = v != _value;
                _value = v;
                UpdateText();
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void SelectAll() { _box.SelectAll(); }

        public override bool Focused { get { return _box.Focused; } }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); _box.Focus(); }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _box.Enabled = Enabled;
            _box.ForeColor = Enabled ? Theme.Text : Theme.TextDim;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutBox(); }

        void LayoutBox()
        {
            if (_box == null) return;
            int sufW = _suffix.Length == 0 ? 0 : TextRenderer.MeasureText(_suffix, Theme.Small).Width + 2;
            int w = Math.Max(10, Width - 8 - sufW - Arrows - 2);
            _box.SetBounds(7, Math.Max(1, (Height - _box.Height) / 2), w, _box.Height);
        }

        void UpdateText()
        {
            string t = _value.ToString("F" + _decimals, CultureInfo.CurrentCulture);
            if (_box.Text != t) _box.Text = t;
        }

        /// <summary>Takes whatever was typed as the new value; anything unreadable snaps back.</summary>
        public void Commit()
        {
            string t = _box.Text.Trim();
            if (_suffix.Length > 0 && t.EndsWith(_suffix)) t = t.Substring(0, t.Length - _suffix.Length).Trim();
            decimal v;
            if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out v) ||
                decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out v))
                Value = v;
            else
                UpdateText();
        }

        void Step(int dir)
        {
            Value = _value + dir * _inc;
            _box.SelectAll();
        }

        void Box_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up: Step(e.Shift ? 10 : 1); e.Handled = true; e.SuppressKeyPress = true; break;
                case Keys.Down: Step(e.Shift ? -10 : -1); e.Handled = true; e.SuppressKeyPress = true; break;
                case Keys.Enter: Commit(); _box.SelectAll(); e.Handled = true; e.SuppressKeyPress = true; break;
                case Keys.Escape: UpdateText(); _box.SelectAll(); e.Handled = true; e.SuppressKeyPress = true; break;
            }
        }

        Rectangle UpRect { get { return new Rectangle(Width - Arrows - 1, 1, Arrows, Height / 2 - 1); } }
        Rectangle DownRect { get { return new Rectangle(Width - Arrows - 1, Height / 2, Arrows, Height - Height / 2 - 1); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool up = UpRect.Contains(e.Location), down = DownRect.Contains(e.Location);
            if (up != _hoverUp || down != _hoverDown) { _hoverUp = up; _hoverDown = down; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverUp = _hoverDown = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (UpRect.Contains(e.Location)) { _pressUp = true; _repeatDir = 1; }
            else if (DownRect.Contains(e.Location)) { _pressDown = true; _repeatDir = -1; }
            else { _box.Focus(); return; }
            _box.Focus();
            Step(_repeatDir);
            _repeatTicks = 0;
            _repeat.Start();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _repeat.Stop();
            _pressUp = _pressDown = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Step(e.Delta > 0 ? 1 : -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Enabled ? Theme.FieldBg : (Theme.Dark ? Color.FromArgb(34, 34, 39) : Color.FromArgb(240, 240, 244));
            bool focused = _box.Focused;
            using (GraphicsPath path = Theme.Round(r, 5))
            using (var b = new SolidBrush(fill))
            using (var p = new Pen(focused ? Theme.Accent : Theme.FieldBorder, focused ? 1.6f : 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            if (_box.BackColor != fill) _box.BackColor = fill;

            if (_suffix.Length > 0)
            {
                var sr = new Rectangle(_box.Right + 1, 0, Width - _box.Right - Arrows - 2, Height);
                TextRenderer.DrawText(g, _suffix, Theme.Small, sr, Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // the chevrons
            using (var sep = new Pen(Theme.FieldBorder)) g.DrawLine(sep, Width - Arrows - 1, 4, Width - Arrows - 1, Height - 4);
            DrawChevron(g, UpRect, true, _hoverUp || _pressUp);
            DrawChevron(g, DownRect, false, _hoverDown || _pressDown);
        }

        void DrawChevron(Graphics g, Rectangle zone, bool up, bool hot)
        {
            if (hot && Enabled)
                using (var wash = new SolidBrush(Color.FromArgb(Theme.Dark ? 60 : 28, Theme.Accent)))
                    g.FillRectangle(wash, zone);
            float cx = zone.X + zone.Width / 2f, cy = zone.Y + zone.Height / 2f;
            using (var p = new Pen(Enabled ? (hot ? Theme.Accent : Theme.TextDim) : Theme.Border, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (up) g.DrawLines(p, new[] { new PointF(cx - 3, cy + 1.5f), new PointF(cx, cy - 1.5f), new PointF(cx + 3, cy + 1.5f) });
                else g.DrawLines(p, new[] { new PointF(cx - 3, cy - 1.5f), new PointF(cx, cy + 1.5f), new PointF(cx + 3, cy - 1.5f) });
            }
        }
    }

    /// <summary>
    /// A drop-down list in the app's style: a rounded well showing the choice with a
    /// chevron, opening a themed, scrollable list underneath. Replaces the system ComboBox.
    /// </summary>
    public class ModernCombo : Control
    {
        public readonly List<string> Items = new List<string>();
        int _sel = -1;
        bool _hover, _open;
        ToolStripDropDown _drop;

        public event EventHandler SelectedIndexChanged;

        public ModernCombo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(130, 26);
            BackColor = Theme.FieldBg;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int SelectedIndex
        {
            get { return _sel; }
            set
            {
                int v = Math.Max(-1, Math.Min(Items.Count - 1, value));
                if (v == _sel) return;
                _sel = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get { return _sel >= 0 && _sel < Items.Count ? Items[_sel] : null; }
            set { SelectedIndex = value == null ? -1 : Items.IndexOf(value.ToString()); }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Up || k == Keys.Down || k == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, _sel - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down && !e.Alt) { SelectedIndex = Math.Min(Items.Count - 1, _sel + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || (e.KeyCode == Keys.Down && e.Alt)) { Open(); e.Handled = true; }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            if (_open) { if (_drop != null) _drop.Close(); return; }
            Open();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Items.Count == 0) return;
            SelectedIndex = Math.Max(0, Math.Min(Items.Count - 1, _sel + (e.Delta > 0 ? -1 : 1)));
        }

        void Open()
        {
            if (Items.Count == 0 || _open) return;
            var list = new ListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Base,
                IntegralHeight = false
            };
            foreach (string s in Items) list.Items.Add(s);
            int rows = Math.Min(Items.Count, 12);
            list.Size = new Size(Math.Max(Width, 120), rows * 24 + 4);
            int hoverIndex = -1;
            list.DrawItem += delegate(object s, DrawItemEventArgs de)
            {
                if (de.Index < 0) return;
                Graphics g = de.Graphics;
                bool selected = de.Index == _sel;
                bool hot = de.Index == hoverIndex;
                using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, de.Bounds);
                if (selected || hot)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var rr = new Rectangle(de.Bounds.X + 3, de.Bounds.Y + 1, de.Bounds.Width - 6, de.Bounds.Height - 2);
                    using (GraphicsPath rp = Theme.Round(rr, 5))
                    using (var wash = new SolidBrush(Color.FromArgb(Theme.Dark ? (selected ? 90 : 50) : (selected ? 40 : 22), Theme.Accent)))
                        g.FillPath(wash, rp);
                }
                TextRenderer.DrawText(g, (string)list.Items[de.Index], Theme.Base, new Rectangle(de.Bounds.X + 10, de.Bounds.Y, de.Bounds.Width - 14, de.Bounds.Height),
                                      Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            };
            list.MouseMove += delegate(object s, MouseEventArgs me)
            {
                int i = list.IndexFromPoint(me.Location);
                if (i != hoverIndex) { hoverIndex = i; list.Invalidate(); }
            };
            list.MouseUp += delegate(object s, MouseEventArgs me)
            {
                int i = list.IndexFromPoint(me.Location);
                if (i >= 0) { SelectedIndex = i; if (_drop != null) _drop.Close(); }
            };
            list.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter) { if (list.SelectedIndex >= 0) SelectedIndex = list.SelectedIndex; if (_drop != null) _drop.Close(); ke.Handled = true; }
                else if (ke.KeyCode == Keys.Escape) { if (_drop != null) _drop.Close(); ke.Handled = true; }
            };
            list.SelectedIndex = _sel;
            var host = new ToolStripControlHost(list) { AutoSize = false, Size = list.Size, Margin = Padding.Empty, Padding = Padding.Empty };
            _drop = new ToolStripDropDown
            {
                AutoSize = false,
                Padding = new Padding(1),
                Margin = Padding.Empty,
                BackColor = Theme.Surface,
                DropShadowEnabled = true,
                Size = new Size(list.Width + 2, list.Height + 2)
            };
            _drop.Renderer = new ModernMenuRenderer();
            _drop.Items.Add(host);
            _drop.Closed += delegate { _open = false; _drop = null; Invalidate(); };
            _open = true;
            Invalidate();
            _drop.Show(this, new Point(0, Height + 2));
            list.Focus();
            if (_sel >= 0) list.TopIndex = Math.Max(0, _sel - rows / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled ? (Theme.Dark ? Color.FromArgb(34, 34, 39) : Color.FromArgb(240, 240, 244))
                       : _hover || _open ? (Theme.Dark ? Color.FromArgb(48, 48, 55) : Color.FromArgb(248, 248, 251)) : Theme.FieldBg;
            using (GraphicsPath path = Theme.Round(r, 5))
            using (var b = new SolidBrush(fill))
            using (var p = new Pen(Focused || _open ? Theme.Accent : Theme.FieldBorder, Focused || _open ? 1.6f : 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            string text = SelectedItem as string ?? "";
            TextRenderer.DrawText(g, text, Theme.Base, new Rectangle(9, 0, Width - 30, Height), Enabled ? Theme.Text : Theme.TextDim,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            float cx = Width - 13, cy = Height / 2f;
            using (var p = new Pen(Enabled ? Theme.TextDim : Theme.Border, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(p, new[] { new PointF(cx - 3.5f, cy - 1.5f), new PointF(cx, cy + 2f), new PointF(cx + 3.5f, cy - 1.5f) });
        }
    }

    /// <summary>
    /// A slider in the app's style: a thin track with an accent fill and a round thumb.
    /// Replaces the system TrackBar in the adjustment dialogs.
    /// </summary>
    public class ModernSlider : Control
    {
        int _min, _max = 100, _val;
        bool _drag, _hover;
        const int Pad = 9;

        public event EventHandler ValueChanged;

        public ModernSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(200, 24);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return _min; } set { _min = value; if (_max < _min) _max = _min; Value = _val; } }
        public int Maximum { get { return _max; } set { _max = value; if (_min > _max) _min = _max; Value = _val; } }

        public int Value
        {
            get { return _val; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _val) return;
                _val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        float ThumbX { get { return Pad + (_max > _min ? (_val - _min) / (float)(_max - _min) : 0f) * (Width - Pad * 2); } }

        int ValueAt(int x)
        {
            float t = (x - Pad) / (float)Math.Max(1, Width - Pad * 2);
            return _min + (int)Math.Round(Math.Max(0, Math.Min(1, t)) * (_max - _min));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int big = Math.Max(1, (_max - _min) / 10);
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value = _val - (e.Shift ? big : 1); e.Handled = true; break;
                case Keys.Right: case Keys.Up: Value = _val + (e.Shift ? big : 1); e.Handled = true; break;
                case Keys.PageDown: Value = _val - big; e.Handled = true; break;
                case Keys.PageUp: Value = _val + big; e.Handled = true; break;
                case Keys.Home: Value = _min; e.Handled = true; break;
                case Keys.End: Value = _max; e.Handled = true; break;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _drag = true;
            Value = ValueAt(e.X);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) Value = ValueAt(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = _val + (e.Delta > 0 ? 1 : -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = Height / 2f;
            float x0 = Pad, x1 = Width - Pad;
            float tx = ThumbX;
            // the track, with the accent fill running from zero (or the left end) to the thumb
            using (var track = new Pen(Theme.FieldBorder, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(track, x0, cy, x1, cy);
            float zeroX = _min < 0 && _max > 0 ? Pad + (0 - _min) / (float)(_max - _min) * (Width - Pad * 2) : x0;
            using (var fill = new Pen(Enabled ? Theme.Accent : Theme.Border, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                if (Math.Abs(tx - zeroX) > 0.5f) g.DrawLine(fill, zeroX, cy, tx, cy);
            float r = _drag ? 8f : _hover || Focused ? 7.5f : 7f;
            using (var white = new SolidBrush(Theme.Dark ? Color.FromArgb(236, 236, 242) : Color.White))
            using (var ring = new Pen(Enabled ? Theme.Accent : Theme.Border, 2f))
            {
                g.FillEllipse(white, tx - r, cy - r, r * 2, r * 2);
                g.DrawEllipse(ring, tx - r, cy - r, r * 2, r * 2);
            }
        }
    }
}
