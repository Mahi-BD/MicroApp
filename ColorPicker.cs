using Gma.System.MouseKeyHook;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Screen colour picker: "+" crosshair everywhere, a magnified loupe of the pixels
    /// under it with the live #RRGGBB, and one click copies that hex code to the clipboard.
    /// Right-click or Esc cancels.
    /// </summary>
    static class ColorPicker
    {
        static IKeyboardMouseEvents _hook;
        static ColorPickerHud _hud;
        static Timer _timer;
        static Action<string> _picked;

        public static bool Active { get { return _hook != null; } }

        /// <summary>Starts picking; <paramref name="picked"/> gets "#RRGGBB" on a click.</summary>
        public static void Start(Action<string> picked)
        {
            if (_hook != null) return;
            _picked = picked;

            uint[] cursors = { Native.NORMAL, Native.IBEAM, Native.HAND };
            for (int i = 0; i < cursors.Length; i++)
                Native.SetSystemCursor(Native.CopyIcon(Native.LoadCursor(IntPtr.Zero, (int)Native.CROSS)), cursors[i]);

            _hud = new ColorPickerHud();
            _hud.Sample(Cursor.Position);
            _hud.Show();

            _hook = Hook.GlobalEvents();
            _hook.MouseDownExt += Hook_MouseDownExt;
            _hook.KeyDown += Hook_KeyDown;

            _timer = new Timer { Interval = 30 };
            _timer.Tick += (s, e) => { if (_hud != null) _hud.Sample(Cursor.Position); };
            _timer.Start();
        }

        static void Hook_MouseDownExt(object sender, MouseEventExtArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                e.Handled = true;   // the click belongs to the picker, not the app underneath
                var p = new Point(e.X, e.Y);
                _hud.BeginInvoke(new Action(() => Finish(p)));
            }
            else if (e.Button == MouseButtons.Right)
            {
                e.Handled = true;
                _hud.BeginInvoke(new Action(Cancel));
            }
        }

        static void Hook_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                _hud.BeginInvoke(new Action(Cancel));
            }
        }

        static void Finish(Point p)
        {
            if (_hook == null) return;
            Color c = ReadPixel(p);
            var callback = _picked;
            End();
            callback?.Invoke(Hex(c));
        }

        public static void Cancel()
        {
            if (_hook == null) return;
            End();
        }

        static void End()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            if (_hook != null)
            {
                _hook.MouseDownExt -= Hook_MouseDownExt;
                _hook.KeyDown -= Hook_KeyDown;
                _hook.Dispose();
                _hook = null;
            }
            if (_hud != null)
            {
                _hud.Close();
                _hud.Dispose();
                _hud = null;
            }
            _picked = null;
            Native.SystemParametersInfo(0x0057, 0, null, 0);   // restore the real cursors
        }

        public static string Hex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        public static Color ReadPixel(Point p)
        {
            try
            {
                using (var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(p.X, p.Y, 0, 0, new Size(1, 1));
                    return bmp.GetPixel(0, 0);
                }
            }
            catch (Exception)
            {
                return Color.Black;
            }
        }
    }

    /// <summary>The loupe that follows the crosshair: zoomed pixels, swatch, hex and RGB.</summary>
    class ColorPickerHud : Form
    {
        const int Cells = 11;          // odd, so there is a centre pixel
        const int Zoom = 10;           // screen pixels → loupe pixels
        const int Pad = 8;
        const int Loupe = Cells * Zoom;
        const string Hint = "Click to copy.  Esc cancels.";

        readonly Bitmap _grab = new Bitmap(Cells, Cells, PixelFormat.Format32bppArgb);
        Color _center = Color.Black;

        public ColorPickerHud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Surface;
            Font = Theme.Base;
            DoubleBuffered = true;
            int textH = Theme.Strong.Height + Theme.Small.Height * 2 + 10;
            int hintW = TextRenderer.MeasureText(Hint, Theme.Small).Width;
            Size = new Size(Math.Max(Loupe, hintW) + Pad * 2, Pad + Loupe + 6 + textH + Pad);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x8;   // no-activate, toolwindow, hit-transparent, topmost
                return cp;
            }
        }

        public void Sample(Point cursor)
        {
            try
            {
                using (var g = Graphics.FromImage(_grab))
                {
                    g.Clear(Color.Black);
                    g.CopyFromScreen(cursor.X - Cells / 2, cursor.Y - Cells / 2, 0, 0, new Size(Cells, Cells));
                }
                _center = _grab.GetPixel(Cells / 2, Cells / 2);
            }
            catch (Exception) { }

            // sit below-right of the crosshair, flipping when near a screen edge
            var screen = Screen.FromPoint(cursor).Bounds;
            int x = cursor.X + 20, y = cursor.Y + 24;
            if (x + Width > screen.Right - 4) x = cursor.X - Width - 20;
            if (y + Height > screen.Bottom - 4) y = cursor.Y - Height - 24;
            if (Location.X != x || Location.Y != y) Location = new Point(x, y);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var fill = new SolidBrush(Theme.Surface))
                g.FillRectangle(fill, ClientRectangle);
            using (var border = new Pen(Theme.Accent))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            // magnified pixels, crisp
            var loupe = new Rectangle((Width - Loupe) / 2, Pad, Loupe, Loupe);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_grab, loupe);
            g.PixelOffsetMode = PixelOffsetMode.Default;

            // the centre cell, outlined in a colour that shows up on it
            var cell = new Rectangle(loupe.X + (Cells / 2) * Zoom, loupe.Y + (Cells / 2) * Zoom, Zoom, Zoom);
            bool light = (_center.R * 299 + _center.G * 587 + _center.B * 114) / 1000 > 140;
            using (var pen = new Pen(light ? Color.Black : Color.White))
                g.DrawRectangle(pen, cell.X - 1, cell.Y - 1, cell.Width + 1, cell.Height + 1);
            using (var pen = new Pen(Theme.Border))
                g.DrawRectangle(pen, loupe.X - 1, loupe.Y - 1, loupe.Width + 1, loupe.Height + 1);

            // swatch + hex
            int y = loupe.Bottom + 6;
            int sw = Theme.Strong.Height;
            using (var b = new SolidBrush(_center))
                g.FillRectangle(b, Pad, y, sw, sw);
            using (var pen = new Pen(Theme.Border))
                g.DrawRectangle(pen, Pad, y, sw - 1, sw - 1);
            TextRenderer.DrawText(g, ColorPicker.Hex(_center), Theme.Strong,
                new Point(Pad + sw + 6, y), Theme.Text, TextFormatFlags.NoPrefix);
            y += sw + 2;
            TextRenderer.DrawText(g, string.Format("RGB {0}, {1}, {2}", _center.R, _center.G, _center.B), Theme.Small,
                new Point(Pad, y), Theme.TextDim, TextFormatFlags.NoPrefix);
            y += Theme.Small.Height + 2;
            TextRenderer.DrawText(g, Hint, Theme.Small, new Rectangle(Pad, y, Width - Pad * 2, Theme.Small.Height + 4),
                Theme.TextDim, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _grab.Dispose();
            base.Dispose(disposing);
        }
    }
    /// <summary>Tray → Color Setting: the colour picker's hot key.</summary>
    class ColorPickSettingsForm : PixelPerfectForm
    {
        static readonly string[] ModNames = { "Alt", "Ctrl", "Shift", "Win" };
        static readonly int[] ModBits = { 1, 2, 4, 8 };

        readonly ModernCheckBox[] _mods = new ModernCheckBox[4];
        readonly TextBox _key;

        public ColorPickSettingsForm()
        {
            Theme.Init(ThemeHelper.IsDarkMode);

            Text = "MicroApp - Color Picker";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.Base;

            SuspendLayout();

            var iconBox = new PictureBox
            {
                Location = new Point(28, 24),
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                TabStop = false
            };
            using (var large = new Icon(Properties.Resources.AppIcon, 40, 40))
                iconBox.Image = large.ToBitmap();
            Controls.Add(iconBox);
            Controls.Add(new Label
            {
                Location = new Point(84, 24),
                AutoSize = true,
                Text = "Color Picker",
                Font = new Font("Segoe UI Semibold", 12.5F),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            });
            Controls.Add(new Label
            {
                Location = new Point(86, 52),
                Size = new Size(430, 32),
                Text = "Press the hot key (or tray \u2192 Pick Color), then click any pixel on screen: " +
                       "its #RRGGBB code is copied to the clipboard.",
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent
            });

            // ----- hot key --------------------------------------------------------------
            var keyCard = new Card
            {
                Location = new Point(24, 96),
                Size = new Size(500, 110),
                Title = "Hot key",
                Description = "Tick the modifiers and give it a key. Clear the key box to turn it off."
            };
            Controls.Add(keyCard);
            int mods = Properties.Settings.Default.ColorPickHotKeyModifier;
            for (int m = 0; m < 4; m++)
            {
                _mods[m] = new ModernCheckBox
                {
                    Location = new Point(20 + m * 78, 66),
                    AutoSize = false,
                    Size = new Size(74, 24),
                    Text = ModNames[m],
                    Checked = (mods & ModBits[m]) != 0
                };
                keyCard.Controls.Add(_mods[m]);
            }
            keyCard.Controls.Add(new Label { Location = new Point(338, 68), AutoSize = true, Text = "Key", Font = Theme.Base, ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            // press a key to set it, Delete clears it
            var keyHost = new FieldHost { Location = new Point(374, 60), Size = new Size(96, 32) };
            _key = new TextBox
            {
                Location = new Point(10, 8),
                Size = new Size(76, 16),
                MaxLength = 12,
                Text = Properties.Settings.Default.ColorPickHotKey,
                TextAlign = HorizontalAlignment.Center,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text
            };
            _key.KeyDown += Key_KeyDown;
            keyHost.Controls.Add(_key);
            keyCard.Controls.Add(keyHost);

            // ----- buttons ------------------------------------------------------------------
            var save = new ModernButton
            {
                Text = "Save",
                Accent = true,
                Size = new Size(112, 36),
                Location = new Point(500 + 24 - 112, keyCard.Bottom + 14)
            };
            save.Click += Save_Click;
            var cancel = new ModernButton
            {
                Text = "Cancel",
                Size = new Size(96, 36),
                Location = new Point(save.Left - 96 - 8, keyCard.Bottom + 14),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;

            ClientSize = new Size(500 + 48, save.Bottom + 20);
            ResumeLayout();

            Theme.Apply(this);
            Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode);
            Theme.RoundWindowCorners(Handle);
            Icon = Properties.Resources.AppIcon;
        }

        /// <summary>Shows the pressed key in the box, ignoring bare modifiers.</summary>
        void Key_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
                case Keys.LWin: case Keys.RWin: case Keys.Return: case Keys.Tab: case Keys.Escape:
                    return;
                case Keys.Delete:
                case Keys.Back:
                    _key.Text = string.Empty;
                    break;
                default:
                    _key.Text = e.KeyCode.ToString();
                    break;
            }
            e.SuppressKeyPress = true;
        }

        void Save_Click(object sender, EventArgs e)
        {
            string letter = _key.Text.Trim();
            if (letter.Length == 1) letter = letter.ToUpperInvariant();
            if (letter.Length > 0)
            {
                Keys parsed;
                if (!Enum.TryParse(letter, true, out parsed))
                {
                    ModernDialog.Info("That is not a key", "Use a letter, a digit or a key name such as F8.");
                    return;
                }
            }
            int mods = 0;
            for (int m = 0; m < 4; m++) if (_mods[m].Checked) mods |= ModBits[m];

            Properties.Settings.Default.ColorPickHotKey = letter;
            Properties.Settings.Default.ColorPickHotKeyModifier = mods;
            Properties.Settings.Default.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
