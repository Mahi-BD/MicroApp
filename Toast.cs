using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Brief notice in the corner of the screen. Replaces NotifyIcon balloons, which
    /// Windows keeps on screen for several seconds no matter what timeout you pass --
    /// this one is gone in about a second, and never steals focus.
    /// </summary>
    public class Toast : Form
    {
        private const int VisibleMs = 1000;    // how long the message stays put
        private const int FadeMs = 160;        // then a short fade so it doesn't just vanish

        private static Toast _current;

        private readonly System.Windows.Forms.Timer _life;
        private string _message;
        private int _elapsed;

        private Toast(string message)
        {
            _message = message ?? string.Empty;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = Measure(_message);
            Location = Corner(Size);
            Cursor = Cursors.Hand;
            Click += (s, e) => Finish();

            _life = new System.Windows.Forms.Timer { Interval = 40 };
            _life.Tick += (s, e) =>
            {
                _elapsed += _life.Interval;
                if (_elapsed >= VisibleMs + FadeMs) { Finish(); return; }
                if (_elapsed > VisibleMs)
                {
                    Opacity = Math.Max(0, 1.0 - (double)(_elapsed - VisibleMs) / FadeMs);
                }
            };
        }

        /// <summary>Shows a message for about a second. Any previous toast is replaced.</summary>
        public static void Show(string message)
        {
            if (_current != null && !_current.IsDisposed)
            {
                _current.Replace(message);
                return;
            }
            var toast = new Toast(message);
            _current = toast;
            toast.Show();
            toast._life.Start();
        }

        private void Replace(string message)
        {
            _message = message ?? string.Empty;
            _elapsed = 0;
            Opacity = 1;
            Size = Measure(_message);
            Location = Corner(Size);
            Invalidate();
        }

        private void Finish()
        {
            _life.Stop();
            if (_current == this) _current = null;
            Close();
        }

        private static Size Measure(string message)
        {
            var text = TextRenderer.MeasureText(message, Theme.Base, new Size(420, 200),
                                                TextFormatFlags.WordBreak);
            return new Size(Math.Max(220, Math.Min(460, text.Width + 40)),
                            Math.Max(56, text.Height + 32));
        }

        private static Point Corner(Size size)
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            return new Point(area.Right - size.Width - 16, area.Bottom - size.Height - 16);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                var p = base.CreateParams;
                p.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return p;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color back = Theme.Dark ? Color.FromArgb(38, 38, 44) : Color.FromArgb(28, 28, 34);

            using (var path = Theme.Round(r, 10))
            using (var fill = new SolidBrush(back))
            using (var pen = new Pen(Color.FromArgb(90, Theme.Accent)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            using (var stripe = new SolidBrush(Theme.Accent))
            {
                g.FillRectangle(stripe, 0, 10, 3, Height - 20);
            }
            TextRenderer.DrawText(g, _message, Theme.Base,
                                  new Rectangle(16, 8, Width - 28, Height - 16),
                                  Color.FromArgb(238, 238, 242),
                                  TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _life != null)
            {
                _life.Stop();
                _life.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
