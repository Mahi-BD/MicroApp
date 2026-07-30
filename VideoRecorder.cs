using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Records a screen region to an MP4 (H.264 + AAC) with optional sound. Frames are
    /// grabbed on a background thread exactly like the GIF recorder, but they go to the
    /// Windows encoder instead of a GIF, so a minute of screen costs megabytes rather
    /// than hundreds. Audio is captured on its own thread and interleaved here.
    /// </summary>
    public class VideoRecorder : IDisposable
    {
        private readonly Rectangle _region;
        private readonly int _fps;
        private readonly int _maxSeconds;
        private readonly AudioCapture _audio;
        private readonly ConcurrentQueue<byte[]> _audioQueue = new ConcurrentQueue<byte[]>();
        private readonly int _audioBlockAlign;
        private readonly string _path;
        private readonly int _videoBitrate;
        private readonly int _audioBytesPerSecond;

        // The MF sink writer must live entirely on the recorder thread: it does not
        // marshal between COM apartments, so a writer created on the (STA) UI thread
        // throws E_NOINTERFACE on the first write from the (MTA) recording thread.
        private Mp4Writer _writer;
        private readonly ManualResetEvent _writerReady = new ManualResetEvent(false);
        private readonly ManualResetEvent _go = new ManualResetEvent(false);
        private Exception _writerError;

        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _paused;
        private long _audioSamples;
        private bool _audioDead;

        public string Path { get { return _path; } }
        public int FrameCount { get; private set; }
        public bool Running { get { return _thread != null && _thread.IsAlive; } }
        /// <summary>True when sound was asked for but no capture device was available.</summary>
        public bool AudioMissing { get; private set; }
        /// <summary>Set when the encoder failed mid-recording or the file could not be finalised.</summary>
        public string Error { get; private set; }

        /// <param name="maxSeconds">0 records until stopped; anything else is a hard limit.</param>
        public VideoRecorder(Rectangle region, string path, int fps, int maxSeconds,
                             VideoQuality quality, VideoAudioSource audioSource)
        {
            // H.264 wants even dimensions
            _region = new Rectangle(region.X, region.Y, region.Width & ~1, region.Height & ~1);
            _fps = Math.Max(1, Math.Min(30, fps));
            _maxSeconds = maxSeconds <= 0 ? 0 : Math.Max(1, Math.Min(3600, maxSeconds));

            if (audioSource != VideoAudioSource.None)
            {
                _audio = AudioCapture.TryCreate(audioSource == VideoAudioSource.System);
                AudioMissing = _audio == null;
            }

            // aim for "screen content" rates: quality picks the bits per pixel per frame
            double bitsPerPixel = quality == VideoQuality.Small ? 0.045
                                : quality == VideoQuality.Sharp ? 0.18
                                : 0.09;
            _videoBitrate = (int)Math.Max(250_000,
                Math.Min(12_000_000, (double)_region.Width * _region.Height * _fps * bitsPerPixel));
            _audioBytesPerSecond = quality == VideoQuality.Small ? 12000 : 16000;
            _audioBlockAlign = _audio != null ? _audio.Channels * 2 : 0;
            _path = path;

            // the recorder thread creates the writer; wait for that here so a broken
            // encoder still surfaces as a construction failure, same as before
            _thread = new Thread(Loop) { IsBackground = true, Name = "MicroApp video recorder" };
            _thread.Start();
            _writerReady.WaitOne();
            if (_writerError != null)
            {
                if (_audio != null) _audio.Dispose();
                throw _writerError;
            }
        }

        public void Start()
        {
            if (_audio != null)
            {
                _audio.Start((buffer, bytes) => _audioQueue.Enqueue(buffer));
            }
            _go.Set();
        }

        public void Stop()
        {
            _stop = true;
            _go.Set();   // release a loop that was never started
            if (_thread != null) _thread.Join(6000);
        }

        /// <summary>
        /// Freezes the recording: no frames, no sound, and no time passes in the file.
        /// The paused stretch is simply absent from the video.
        /// </summary>
        public void Pause() { _paused = true; }
        public void Resume() { _paused = false; }
        public bool Paused { get { return _paused; } }

        private void Loop()
        {
            try
            {
                _writer = new Mp4Writer(_path, _region.Width, _region.Height, _fps, _videoBitrate,
                                        _audio != null ? _audio.SampleRate : 0,
                                        _audio != null ? _audio.Channels : 0,
                                        _audioBytesPerSecond);
            }
            catch (Exception ex)
            {
                _writerError = ex;
                _writerReady.Set();
                return;
            }
            _writerReady.Set();

            _go.WaitOne();   // released by Start(), or by Stop()/Dispose() if never started
            if (_stop)
            {
                FinishWriter();
                return;
            }

            int frameMs = 1000 / _fps;
            int maxFrames = _maxSeconds == 0 ? int.MaxValue : _fps * _maxSeconds;
            long frameDuration = 10_000_000L / _fps;
            long pausedMs = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            using (var frame = new Bitmap(_region.Width, _region.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            using (var g = Graphics.FromImage(frame))
            {
                long lastTimestamp = -1;
                for (int i = 0; i < maxFrames && !_stop; i++)
                {
                    if (_paused)
                    {
                        long pauseStart = clock.ElapsedMilliseconds;
                        byte[] junk;
                        while (_paused && !_stop)
                        {
                            while (_audioQueue.TryDequeue(out junk)) { }   // sound while paused is dropped
                            Thread.Sleep(50);
                        }
                        pausedMs += clock.ElapsedMilliseconds - pauseStart;
                        while (_audioQueue.TryDequeue(out junk)) { }
                        if (_stop) break;
                    }

                    long due = (long)i * frameMs + pausedMs;
                    long wait = due - clock.ElapsedMilliseconds;
                    if (wait > 0) Thread.Sleep((int)wait);

                    try
                    {
                        g.CopyFromScreen(_region.Location, Point.Empty, _region.Size, CopyPixelOperation.SourceCopy);
                        DrawCursor(g);

                        // timestamps follow the wall clock minus paused time, not the frame
                        // index: when a grab runs late the video stays in step with the sound
                        // instead of speeding up. Encoders insist they strictly increase.
                        long timestamp = clock.ElapsedTicks * 10_000_000L / System.Diagnostics.Stopwatch.Frequency
                                         - pausedMs * 10_000L;
                        if (timestamp <= lastTimestamp) timestamp = lastTimestamp + frameDuration;
                        lastTimestamp = timestamp;

                        var bits = frame.LockBits(new Rectangle(0, 0, _region.Width, _region.Height),
                                                  System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                                  System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                        try
                        {
                            _writer.WriteVideoFrame(bits.Scan0, bits.Stride, timestamp);
                        }
                        finally
                        {
                            frame.UnlockBits(bits);
                        }
                        FrameCount++;

                        DrainAudio(timestamp, false);
                    }
                    catch (Exception ex)
                    {
                        // encoder gave up mid-flight: stop here and try to finalise what
                        // exists, so the frames already written are not lost too
                        Error = ex.Message;
                        break;
                    }
                }
            }

            if (_audio != null) _audio.Stop();
            DrainAudio(long.MaxValue, true);
            FinishWriter();
        }

        /// <summary>
        /// Finalises the MP4 on the recorder thread — the only thread allowed to touch
        /// the writer. Skipping Finish leaves the file unplayable.
        /// </summary>
        private void FinishWriter()
        {
            try
            {
                _writer.Finish();
            }
            catch (Exception ex)
            {
                if (Error == null) Error = ex.Message;
            }
            _writer.Dispose();
        }

        /// <summary>
        /// Writes queued audio with a running sample clock. A loopback tap goes quiet
        /// while nothing is playing, so when the queue starves the gap is filled with
        /// silence; otherwise the sound track would fall behind the picture.
        /// </summary>
        private void DrainAudio(long elapsed, bool final)
        {
            if (_audio == null || _audioDead) return;
            try
            {
                byte[] buffer;
                while (_audioQueue.TryDequeue(out buffer))
                {
                    _writer.WriteAudio(buffer, buffer.Length, AudioTime());
                    _audioSamples += buffer.Length / _audioBlockAlign;
                }

                if (!final)
                {
                    long target = elapsed * _audio.SampleRate / 10_000_000L;
                    long deficit = target - _audioSamples;
                    if (deficit > _audio.SampleRate / 4)
                    {
                        // stay ~100 ms behind real time so late real packets still fit
                        int fill = (int)Math.Min(deficit - _audio.SampleRate / 10, _audio.SampleRate);
                        var silence = new byte[fill * _audioBlockAlign];
                        _writer.WriteAudio(silence, silence.Length, AudioTime());
                        _audioSamples += fill;
                    }
                }
            }
            catch (Exception)
            {
                _audioDead = true;   // keep the picture even if the sound track fails
            }
        }

        private long AudioTime()
        {
            return _audioSamples * 10_000_000L / _audio.SampleRate;
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
            _go.Set();
            if (_thread != null && _thread.IsAlive) _thread.Join(6000);
            if (_audio != null) _audio.Dispose();
        }
    }

    /// <summary>
    /// The badge shown while a video records. Like the GIF one it sits outside the
    /// recorded region and never takes focus, but a video has no time limit, so it
    /// carries its own controls: a pause/resume button and a save button.
    /// </summary>
    public class VideoRecordingIndicator : Form
    {
        private readonly System.Windows.Forms.Timer _tick;
        private readonly System.Diagnostics.Stopwatch _elapsed = System.Diagnostics.Stopwatch.StartNew();
        private bool _paused;
        private Rectangle _pauseRect;
        private Rectangle _saveRect;
        private int _hover;   // 0 none, 1 pause, 2 save

        /// <summary>Raised with the new paused state after the pause button toggles it.</summary>
        public event EventHandler<bool> PauseToggled;
        /// <summary>The save button: stop recording and keep the file.</summary>
        public event EventHandler SaveRequested;

        public VideoRecordingIndicator(Rectangle region)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(238, 38);
            _pauseRect = new Rectangle(Width - 84, 5, 36, 28);
            _saveRect = new Rectangle(Width - 44, 5, 36, 28);
            Location = PlaceOutside(region, Size);

            _tick = new System.Windows.Forms.Timer { Interval = 200 };
            _tick.Tick += (s, e) => Invalidate();
            _tick.Start();

            MouseMove += (s, e) =>
            {
                int hover = _pauseRect.Contains(e.Location) ? 1 : _saveRect.Contains(e.Location) ? 2 : 0;
                if (hover != _hover) { _hover = hover; Invalidate(); }
                Cursor = hover != 0 ? Cursors.Hand : Cursors.Default;
            };
            MouseUp += (s, e) =>
            {
                if (_pauseRect.Contains(e.Location))
                {
                    _paused = !_paused;
                    if (_paused) _elapsed.Stop(); else _elapsed.Start();
                    Invalidate();
                    var handler = PauseToggled;
                    if (handler != null) handler(this, _paused);
                }
                else if (_saveRect.Contains(e.Location))
                {
                    var handler = SaveRequested;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            };
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
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var path = Theme.Round(r, 8))
            using (var fill = new SolidBrush(Color.FromArgb(24, 24, 28)))
            using (var pen = new Pen(_paused ? Color.FromArgb(150, 150, 158) : Color.FromArgb(200, 70, 70)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            using (var dot = new SolidBrush(_paused ? Color.FromArgb(150, 150, 158) : Color.FromArgb(230, 70, 70)))
            {
                g.FillEllipse(dot, 12, Height / 2 - 5, 10, 10);
            }

            var t = _elapsed.Elapsed;
            string text = (_paused ? "PAUSED  " : "REC  ") +
                          (t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                                             : $"{t.Minutes}:{t.Seconds:00}");
            TextRenderer.DrawText(g, text, Theme.Strong, new Rectangle(30, 0, _pauseRect.Left - 32, Height),
                                  Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            DrawButton(g, _pauseRect, _hover == 1);
            DrawButton(g, _saveRect, _hover == 2);

            // pause: two bars while recording, a play triangle while paused
            int cx = _pauseRect.X + _pauseRect.Width / 2, cy = _pauseRect.Y + _pauseRect.Height / 2;
            if (_paused)
            {
                using (var brush = new SolidBrush(Color.White))
                {
                    g.FillPolygon(brush, new[]
                    {
                        new Point(cx - 4, cy - 6), new Point(cx - 4, cy + 6), new Point(cx + 6, cy)
                    });
                }
            }
            else
            {
                using (var brush = new SolidBrush(Color.White))
                {
                    g.FillRectangle(brush, cx - 6, cy - 6, 4, 12);
                    g.FillRectangle(brush, cx + 2, cy - 6, 4, 12);
                }
            }

            // save: a little floppy disk
            int sx = _saveRect.X + _saveRect.Width / 2 - 8, sy = _saveRect.Y + _saveRect.Height / 2 - 8;
            using (var pen = new Pen(Color.White, 1.6f))
            using (var brush = new SolidBrush(Color.White))
            {
                g.DrawLines(pen, new[]
                {
                    new Point(sx, sy), new Point(sx + 12, sy), new Point(sx + 16, sy + 4),
                    new Point(sx + 16, sy + 16), new Point(sx, sy + 16), new Point(sx, sy)
                });
                g.FillRectangle(brush, sx + 3, sy, 8, 5);          // shutter
                g.DrawRectangle(pen, sx + 3, sy + 9, 10, 7);       // label
            }
        }

        private static void DrawButton(Graphics g, Rectangle r, bool hover)
        {
            using (var path = Theme.Round(r, 6))
            using (var fill = new SolidBrush(Color.FromArgb(hover ? 80 : 45, 255, 255, 255)))
            {
                g.FillPath(fill, path);
            }
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

    /// <summary>
    /// Thin frame that marks the recorded region for the whole take, until the video is
    /// saved. It sits entirely outside the captured pixels (CopyFromScreen grabs whatever
    /// is on screen, so anything inside the region would end up in the file) and is
    /// hit-transparent, so clicks pass straight through to what is being recorded.
    /// </summary>
    public class RecordingRegionFrame : Form
    {
        const int Thickness = 2;

        public RecordingRegionFrame(Rectangle region)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(230, 70, 70);

            // the recorder trims the region to even dimensions; mark exactly what it grabs
            var captured = new Rectangle(region.X, region.Y, region.Width & ~1, region.Height & ~1);
            var bounds = Rectangle.Inflate(captured, Thickness, Thickness);
            Bounds = bounds;
            var frame = new Region(new Rectangle(0, 0, bounds.Width, bounds.Height));
            frame.Exclude(new Rectangle(Thickness, Thickness,
                bounds.Width - 2 * Thickness, bounds.Height - 2 * Thickness));
            Region = frame;
        }

        /// <summary>Grey while paused, red while recording — same colours as the badge.</summary>
        public void SetPaused(bool paused)
        {
            BackColor = paused ? Color.FromArgb(150, 150, 158) : Color.FromArgb(230, 70, 70);
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
    }
}
