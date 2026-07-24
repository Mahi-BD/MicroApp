using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Records a screen region to an animated GIF. Frames are grabbed on a background
    /// thread and encoded straight to disk, so a long recording costs no more memory
    /// than a short one.
    /// </summary>
    public class GifRecorder : IDisposable
    {
        private readonly Rectangle _region;
        private readonly int _fps;
        private readonly int _maxSeconds;
        private readonly GifWriter _writer;

        private Thread _thread;
        private volatile bool _stop;

        public string Path { get { return _writer.Path; } }
        public int FrameCount { get { return _writer.FrameCount; } }
        public bool Running { get { return _thread != null && _thread.IsAlive; } }

        public GifRecorder(Rectangle region, string path, int fps, int maxSeconds)
        {
            _region = region;
            _fps = Math.Max(1, Math.Min(30, fps));
            _maxSeconds = Math.Max(1, Math.Min(600, maxSeconds));
            _writer = new GifWriter(path, 1000 / _fps);
        }

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "MicroApp GIF recorder" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            if (_thread != null) _thread.Join(4000);
        }

        private void Loop()
        {
            int frameMs = 1000 / _fps;
            int maxFrames = _fps * _maxSeconds;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            using (var frame = new Bitmap(_region.Width, _region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(frame))
            {
                for (int i = 0; i < maxFrames && !_stop; i++)
                {
                    long due = (long)i * frameMs;
                    long wait = due - clock.ElapsedMilliseconds;
                    if (wait > 0) Thread.Sleep((int)wait);

                    g.CopyFromScreen(_region.Location, Point.Empty, _region.Size, CopyPixelOperation.SourceCopy);
                    DrawCursor(g);
                    _writer.AddFrame(frame);
                }
            }
        }

        /// <summary>The screen copy leaves the pointer out; draw it back in.</summary>
        private void DrawCursor(Graphics g)
        {
            try
            {
                var pos = Cursor.Position;
                if (!_region.Contains(pos)) return;
                var cursor = Cursors.Default;
                cursor.Draw(g, new Rectangle(pos.X - _region.X, pos.Y - _region.Y,
                                             cursor.Size.Width, cursor.Size.Height));
            }
            catch (Exception)
            {
                // drawing the pointer is a nicety, never a reason to lose the frame
            }
        }

        public void Dispose()
        {
            _stop = true;
            if (_thread != null && _thread.IsAlive) _thread.Join(4000);
            _writer.Dispose();
        }
    }

    /// <summary>
    /// Small "recording" badge shown while a GIF is being captured. It is placed
    /// outside the recorded region whenever there is room, so it stays out of the
    /// frames, and it never takes focus from whatever the user is recording.
    /// </summary>
    public class RecordingIndicator : Form
    {
        private readonly System.Windows.Forms.Timer _tick;
        private readonly DateTime _started = DateTime.Now;
        private readonly int _maxSeconds;
        private string _text = "REC";

        public event EventHandler StopRequested;

        public RecordingIndicator(Rectangle region, int maxSeconds)
        {
            _maxSeconds = maxSeconds;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(196, 38);
            Cursor = Cursors.Hand;
            Location = PlaceOutside(region, Size);

            _tick = new System.Windows.Forms.Timer { Interval = 100 };
            _tick.Tick += (s, e) =>
            {
                double elapsed = (DateTime.Now - _started).TotalSeconds;
                _text = $"REC  {elapsed:0.0}s / {_maxSeconds}s";
                Invalidate();
            };
            _tick.Start();

            Click += (s, e) => RaiseStop();
        }

        /// <summary>Prefer just above the region, then below, then its top-left corner.</summary>
        private static Point PlaceOutside(Rectangle region, Size size)
        {
            var screen = Screen.FromRectangle(region).WorkingArea;
            int x = Math.Min(region.Left, screen.Right - size.Width);
            if (region.Top - size.Height - 8 >= screen.Top) return new Point(x, region.Top - size.Height - 8);
            if (region.Bottom + 8 + size.Height <= screen.Bottom) return new Point(x, region.Bottom + 8);
            return new Point(x + 12, region.Top + 12);
        }

        public void RaiseStop()
        {
            var handler = StopRequested;
            if (handler != null) handler(this, EventArgs.Empty);
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

            using (var path = Theme.Round(r, 8))
            using (var fill = new SolidBrush(Theme.Dark ? Color.FromArgb(28, 28, 32) : Color.FromArgb(24, 24, 28)))
            using (var pen = new Pen(Color.FromArgb(200, 70, 70)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            using (var dot = new SolidBrush(Color.FromArgb(230, 70, 70)))
            {
                g.FillEllipse(dot, 12, Height / 2 - 5, 10, 10);
            }
            TextRenderer.DrawText(g, _text, Theme.Strong, new Rectangle(30, 0, Width - 36, Height),
                                  Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Esc", Theme.Small, new Rectangle(0, 0, Width - 12, Height),
                                  Color.FromArgb(170, 170, 178),
                                  TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _tick != null)
            {
                _tick.Stop();
                _tick.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
