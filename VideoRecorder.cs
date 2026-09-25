using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly IAudioSource _audio;

        // live camera: follow the pointer and zoom, changed from the badge while recording.
        // The video keeps the region's size; what changes is the patch of screen scaled into it.
        private volatile bool _follow;
        private volatile int _zoomPercent = 100;
        private PointF _cam;                     // the centre of the captured patch, eased towards its target
        private Rectangle _source;               // the patch captured for the latest frame
        private readonly object _sourceLock = new object();
        private Bitmap _grab;                    // the patch at screen size, before scaling

        // click glows: where and when each recent click happened (screen coords, stopwatch ms)
        private readonly List<Tuple<Point, MouseButtons, long>> _clicks = new List<Tuple<Point, MouseButtons, long>>();
        private readonly System.Diagnostics.Stopwatch _clickClock = System.Diagnostics.Stopwatch.StartNew();
        const int GlowMs = 500;

        // the video's own clock: runs while recording, stops while paused (UI thread only)
        private readonly System.Diagnostics.Stopwatch _videoClock = new System.Diagnostics.Stopwatch();
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
        /// <summary>The microphone was asked for (alone or mixed) but none is available.</summary>
        public bool MicMissing { get; private set; }
        /// <summary>A microphone is being recorded, so the badge offers its mute button.</summary>
        public bool HasMicrophone { get; private set; }

        /// <summary>The captured patch follows the mouse pointer (eased), clamped to the screens.</summary>
        public bool Follow { get { return _follow; } set { _follow = value; } }

        /// <summary>100 = the region at 1:1; 200 = half the width and height, enlarged; 50 = twice as much, reduced.</summary>
        public int ZoomPercent
        {
            get { return _zoomPercent; }
            set { _zoomPercent = Math.Max(25, Math.Min(800, value)); }
        }

        /// <summary>The video's width and height in pixels.</summary>
        public Size FrameSize { get { return _region.Size; } }

        /// <summary>The pointer is drawn at its normal size even when zoomed (false: it scales with the picture).</summary>
        public bool LockCursorSize { get; set; }

        /// <summary>Clicks leave a short glow in the video (yellow left, blue right, green middle).</summary>
        public bool ClickGlow { get; set; }

        /// <summary>How far into the video we are: paused stretches do not count, as they are not in the file.</summary>
        public TimeSpan VideoTime { get { return _videoClock.Elapsed; } }

        /// <summary>A mouse button went down at this screen position (from the global hook).</summary>
        public void AddClick(Point screen, MouseButtons button)
        {
            if (!ClickGlow || _paused) return;
            lock (_clicks) _clicks.Add(Tuple.Create(screen, button, _clickClock.ElapsedMilliseconds));
        }

        /// <summary>Expanding, fading rings where the recent clicks were, mapped like the picture.</summary>
        private void DrawClicks(Graphics g, Rectangle src)
        {
            List<Tuple<Point, MouseButtons, long>> live;
            long now = _clickClock.ElapsedMilliseconds;
            lock (_clicks)
            {
                _clicks.RemoveAll(c => now - c.Item3 > GlowMs);
                if (_clicks.Count == 0) return;
                live = new List<Tuple<Point, MouseButtons, long>>(_clicks);
            }
            float sx = (float)_region.Width / src.Width, sy = (float)_region.Height / src.Height;
            float scale = LockCursorSize ? 1f : sx;
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            foreach (var c in live)
            {
                if (!src.Contains(c.Item1)) continue;
                float t = (now - c.Item3) / (float)GlowMs;           // 0 .. 1
                float cx = (c.Item1.X - src.X) * sx, cy = (c.Item1.Y - src.Y) * sy;
                Color baseColor = c.Item2 == MouseButtons.Right ? Color.FromArgb(80, 160, 255)
                                : c.Item2 == MouseButtons.Middle ? Color.FromArgb(70, 210, 120)
                                : Color.FromArgb(255, 200, 40);
                float r = (10 + 22 * t) * scale;
                int alpha = (int)(200 * (1 - t));
                using (var fill = new SolidBrush(Color.FromArgb(alpha / 3, baseColor)))
                    g.FillEllipse(fill, cx - r, cy - r, 2 * r, 2 * r);
                using (var ring = new Pen(Color.FromArgb(alpha, baseColor), 3f * scale))
                    g.DrawEllipse(ring, cx - r, cy - r, 2 * r, 2 * r);
            }
            g.SmoothingMode = old;
        }

        /// <summary>The patch of screen the latest frame came from - the red frame follows it.</summary>
        public Rectangle CurrentSource { get { lock (_sourceLock) return _source; } }

        public bool MicrophoneMuted
        {
            get { return _audio != null && HasMicrophone && _audio.Muted; }
            set { if (_audio != null && HasMicrophone) _audio.Muted = value; }
        }
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

            if (audioSource == VideoAudioSource.SystemAndMicrophone)
            {
                bool micMissing, systemMissing;
                _audio = AudioMixer.TryCreate(out micMissing, out systemMissing);
                AudioMissing = _audio == null;
                MicMissing = micMissing;
                HasMicrophone = !micMissing;
            }
            else if (audioSource != VideoAudioSource.None)
            {
                _audio = AudioCapture.TryCreate(audioSource == VideoAudioSource.System);
                AudioMissing = _audio == null;
                MicMissing = audioSource == VideoAudioSource.Microphone && _audio == null;
                HasMicrophone = audioSource == VideoAudioSource.Microphone && _audio != null;
            }
            _cam = new PointF(_region.X + _region.Width / 2f, _region.Y + _region.Height / 2f);
            _source = _region;

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
            _videoClock.Start();
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
        public void Pause() { _paused = true; _videoClock.Stop(); }
        public void Resume() { _paused = false; _videoClock.Start(); }
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
                        Rectangle src = NextSource();
                        if (src.Size == _region.Size)
                        {
                            g.CopyFromScreen(src.Location, Point.Empty, src.Size, CopyPixelOperation.SourceCopy);
                        }
                        else
                        {
                            // zoomed: grab the patch at screen size, then scale it into the frame
                            if (_grab == null || _grab.Size != src.Size)
                            {
                                if (_grab != null) _grab.Dispose();
                                _grab = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                            }
                            using (var gg = Graphics.FromImage(_grab))
                                gg.CopyFromScreen(src.Location, Point.Empty, src.Size, CopyPixelOperation.SourceCopy);
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                            g.DrawImage(_grab, new Rectangle(0, 0, _region.Width, _region.Height));
                        }
                        DrawClicks(g, src);
                        DrawCursor(g, src);

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

        /// <summary>
        /// The patch of screen for the next frame: the region's size divided by the zoom,
        /// centred on the pointer while following (eased, so the picture glides instead of
        /// jittering) or on the region's centre otherwise, and kept on the screens.
        /// </summary>
        private Rectangle NextSource()
        {
            float zoom = _zoomPercent / 100f;
            bool follow = _follow;
            Rectangle screens = SystemInformation.VirtualScreen;
            if (!follow && _zoomPercent == 100)
            {
                // the plain case stays exactly the region, pixel for pixel
                var centre = new PointF(_region.X + _region.Width / 2f, _region.Y + _region.Height / 2f);
                if (Math.Abs(_cam.X - centre.X) < 1 && Math.Abs(_cam.Y - centre.Y) < 1)
                {
                    _cam = centre;
                    lock (_sourceLock) _source = _region;
                    return _region;
                }
            }

            double w = _region.Width / zoom, h = _region.Height / zoom;
            double fit = Math.Min(1.0, Math.Min(screens.Width / w, screens.Height / h));   // never more than the screens hold
            int sw = Math.Max(16, (int)Math.Round(w * fit)) & ~1;
            int sh = Math.Max(16, (int)Math.Round(h * fit)) & ~1;

            PointF target;
            if (follow)
            {
                Point p = Cursor.Position;
                target = new PointF(p.X, p.Y);
            }
            else target = new PointF(_region.X + _region.Width / 2f, _region.Y + _region.Height / 2f);
            // ease a quarter of the way per frame: smooth at any frame rate that matters here
            _cam = new PointF(_cam.X + (target.X - _cam.X) * 0.25f, _cam.Y + (target.Y - _cam.Y) * 0.25f);

            int x = (int)Math.Round(_cam.X - sw / 2f), y = (int)Math.Round(_cam.Y - sh / 2f);
            x = Math.Max(screens.Left, Math.Min(screens.Right - sw, x));
            y = Math.Max(screens.Top, Math.Min(screens.Bottom - sh, y));
            var src = new Rectangle(x, y, sw, sh);
            lock (_sourceLock) _source = src;
            return src;
        }

        /// <summary>The screen copy leaves the pointer out; draw it back in, scaled like the picture.</summary>
        private void DrawCursor(Graphics g, Rectangle src)
        {
            try
            {
                var pos = Cursor.Position;
                if (!src.Contains(pos)) return;
                float sx = (float)_region.Width / src.Width, sy = (float)_region.Height / src.Height;
                var cursor = Cursors.Default;
                // the tip stays on the spot it points at; only its size depends on the setting
                float cw = LockCursorSize ? 1f : sx, ch = LockCursorSize ? 1f : sy;
                cursor.Draw(g, new Rectangle((int)((pos.X - src.X) * sx), (int)((pos.Y - src.Y) * sy),
                                             (int)(cursor.Size.Width * cw), (int)(cursor.Size.Height * ch)));
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
            if (_grab != null) { _grab.Dispose(); _grab = null; }
        }
    }

    /// <summary>
    /// The badge shown while a video records. Like the GIF one it never takes focus, and
    /// it is left out of screen captures, so it never appears in the video even when the
    /// camera passes over it. A video has no time limit, so it carries its own controls:
    /// follow the pointer, zoom out / in (or the mouse wheel over the badge; click the
    /// percentage for 100 %), mute the microphone, pause/resume and save.
    /// </summary>
    public class VideoRecordingIndicator : Form
    {
        static readonly int[] ZoomSteps = { 50, 75, 100, 125, 150, 200, 250, 300, 400 };
        enum Btn { None, Follow, ZoomOut, ZoomValue, ZoomIn, Mic, Pause, Save }

        private readonly System.Windows.Forms.Timer _tick;
        private readonly System.Diagnostics.Stopwatch _elapsed = System.Diagnostics.Stopwatch.StartNew();
        private readonly Dictionary<Btn, Rectangle> _buttons = new Dictionary<Btn, Rectangle>();
        private readonly ToolTip _tip = new ToolTip();
        private readonly bool _hasMic;
        private bool _paused, _follow, _micMuted;
        private int _zoom = 100;
        private Btn _hover;
        private bool _dragging;
        private Point _dragFrom, _dragStart;

        /// <summary>The pause shortcut (Ctrl+Alt+P): same as clicking the pause button.</summary>
        public void TogglePause() { OnButton(Btn.Pause); }

        /// <summary>Raised with the new paused state after the pause button toggles it.</summary>
        public event EventHandler<bool> PauseToggled;
        /// <summary>The save button: stop recording and keep the file.</summary>
        public event EventHandler SaveRequested;
        public event EventHandler<bool> FollowToggled;
        public event EventHandler<int> ZoomChanged;
        public event EventHandler<bool> MicMuteToggled;

        public VideoRecordingIndicator(Rectangle region, bool hasMic)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            _hasMic = hasMic;

            // left to right after the REC read-out: follow | - 100% + | mic | pause save
            int x = 116;
            _buttons[Btn.Follow] = new Rectangle(x, 5, 32, 28); x += 40;
            _buttons[Btn.ZoomOut] = new Rectangle(x, 5, 26, 28); x += 28;
            _buttons[Btn.ZoomValue] = new Rectangle(x, 5, 46, 28); x += 48;
            _buttons[Btn.ZoomIn] = new Rectangle(x, 5, 26, 28); x += 34;
            if (hasMic) { _buttons[Btn.Mic] = new Rectangle(x, 5, 32, 28); x += 40; }
            _buttons[Btn.Pause] = new Rectangle(x, 5, 36, 28); x += 40;
            _buttons[Btn.Save] = new Rectangle(x, 5, 36, 28); x += 44;
            Size = new Size(x, 38);
            Location = PlaceOutside(region, Size);

            _tick = new System.Windows.Forms.Timer { Interval = 200 };
            _tick.Tick += (s, e) => Invalidate();
            _tick.Start();

            MouseMove += (s, e) =>
            {
                Btn hover = HitButton(e.Location);
                if (hover != _hover)
                {
                    _hover = hover;
                    Invalidate();
                    _tip.SetToolTip(this, TipFor(hover));
                }
                Cursor = hover != Btn.None ? Cursors.Hand : Cursors.Default;
            };
            MouseLeave += (s, e) => { _hover = Btn.None; Invalidate(); };
            // drag the badge by any spot that is not a button
            MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && HitButton(e.Location) == Btn.None)
                {
                    _dragFrom = Cursor.Position;
                    _dragStart = Location;
                    _dragging = true;
                }
            };
            MouseMove += (s, e) =>
            {
                if (!_dragging) { if (HitButton(e.Location) == Btn.None) Cursor = Cursors.SizeAll; return; }
                Point p = Cursor.Position;
                Location = new Point(_dragStart.X + p.X - _dragFrom.X, _dragStart.Y + p.Y - _dragFrom.Y);
            };
            MouseUp += (s, e) =>
            {
                if (_dragging) { _dragging = false; return; }
                OnButton(HitButton(e.Location));
            };
            MouseWheel += (s, e) => StepZoom(e.Delta > 0 ? 1 : -1);
        }

        Btn HitButton(Point p)
        {
            foreach (var kv in _buttons) if (kv.Value.Contains(p)) return kv.Key;
            return Btn.None;
        }

        string TipFor(Btn b)
        {
            switch (b)
            {
                case Btn.Follow: return _follow ? "Following the pointer - click to stop" : "Follow the mouse pointer";
                case Btn.ZoomOut: return "Zoom out (or scroll the wheel over this badge)";
                case Btn.ZoomIn: return "Zoom in (or scroll the wheel over this badge)";
                case Btn.ZoomValue: return "Back to 100 %";
                case Btn.Mic: return _micMuted ? "Microphone muted - click to unmute" : "Mute the microphone";
                case Btn.Pause: return _paused ? "Resume (Ctrl+Alt+P)" : "Pause (Ctrl+Alt+P)";
                case Btn.Save: return "Stop and save";
            }
            return "";
        }

        void OnButton(Btn b)
        {
            switch (b)
            {
                case Btn.Pause:
                    _paused = !_paused;
                    if (_paused) _elapsed.Stop(); else _elapsed.Start();
                    Raise(PauseToggled, _paused);
                    break;
                case Btn.Save:
                    var handler = SaveRequested;
                    if (handler != null) handler(this, EventArgs.Empty);
                    return;
                case Btn.Follow:
                    _follow = !_follow;
                    Raise(FollowToggled, _follow);
                    break;
                case Btn.ZoomOut: StepZoom(-1); return;
                case Btn.ZoomIn: StepZoom(1); return;
                case Btn.ZoomValue:
                    if (_zoom != 100) { _zoom = 100; Raise(ZoomChanged, _zoom); }
                    break;
                case Btn.Mic:
                    _micMuted = !_micMuted;
                    Raise(MicMuteToggled, _micMuted);
                    break;
                default: return;
            }
            _tip.SetToolTip(this, TipFor(b));
            Invalidate();
        }

        void StepZoom(int dir)
        {
            int i = Array.IndexOf(ZoomSteps, _zoom);
            if (i < 0) i = Array.IndexOf(ZoomSteps, 100);
            int next = ZoomSteps[Math.Max(0, Math.Min(ZoomSteps.Length - 1, i + dir))];
            if (next == _zoom) return;
            _zoom = next;
            Raise(ZoomChanged, _zoom);
            Invalidate();
        }

        void Raise<T>(EventHandler<T> h, T value) { if (h != null) h(this, value); }

        /// <summary>Prefer just above the region, then below, then its top-left corner.</summary>
        private static Point PlaceOutside(Rectangle region, Size size)
        {
            var screen = Screen.FromRectangle(region).WorkingArea;
            int x = Math.Max(screen.Left, Math.Min(region.Left, screen.Right - size.Width));
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CaptureExclusion.Apply(Handle);
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
            string text = (_paused ? "PAUSED " : "REC ") +
                          (t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                                             : $"{t.Minutes}:{t.Seconds:00}");
            TextRenderer.DrawText(g, text, Theme.Strong, new Rectangle(28, 0, _buttons[Btn.Follow].Left - 30, Height),
                                  Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Color accent = Color.FromArgb(99, 102, 241);
            foreach (var kv in _buttons)
            {
                bool on = (kv.Key == Btn.Follow && _follow) || (kv.Key == Btn.Mic && _micMuted);
                if (kv.Key == Btn.ZoomValue && _zoom == 100 && _hover != Btn.ZoomValue) continue;   // just the number
                DrawButton(g, kv.Value, _hover == kv.Key, on ? (kv.Key == Btn.Mic ? Color.FromArgb(200, 70, 70) : accent) : Color.Empty);
            }

            using (var pen = new Pen(Color.White, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            using (var brush = new SolidBrush(Color.White))
            {
                // follow: a target with the pointer at its centre
                Rectangle f = _buttons[Btn.Follow];
                int fx = f.X + f.Width / 2, fy = f.Y + f.Height / 2;
                g.DrawEllipse(pen, fx - 8, fy - 8, 16, 16);
                g.DrawLine(pen, fx, fy - 11, fx, fy - 6); g.DrawLine(pen, fx, fy + 6, fx, fy + 11);
                g.DrawLine(pen, fx - 11, fy, fx - 6, fy); g.DrawLine(pen, fx + 6, fy, fx + 11, fy);
                g.FillEllipse(brush, fx - 2, fy - 2, 4, 4);

                // zoom - and +
                Rectangle zo = _buttons[Btn.ZoomOut], zi = _buttons[Btn.ZoomIn];
                int zy = zo.Y + zo.Height / 2;
                g.DrawLine(pen, zo.X + 8, zy, zo.Right - 8, zy);
                g.DrawLine(pen, zi.X + 8, zy, zi.Right - 8, zy);
                g.DrawLine(pen, zi.X + zi.Width / 2, zy - 5, zi.X + zi.Width / 2, zy + 5);
                TextRenderer.DrawText(g, _zoom + "%", Theme.Base, _buttons[Btn.ZoomValue],
                    _zoom == 100 ? Color.FromArgb(200, 200, 208) : Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                // microphone, struck through while muted
                if (_hasMic)
                {
                    Rectangle m = _buttons[Btn.Mic];
                    int mx = m.X + m.Width / 2, my = m.Y + m.Height / 2;
                    using (var cap = Theme.Round(new Rectangle(mx - 4, my - 9, 8, 12), 4)) g.DrawPath(pen, cap);
                    g.DrawArc(pen, mx - 7, my - 5, 14, 11, 0, 180);
                    g.DrawLine(pen, mx, my + 6, mx, my + 9);
                    if (_micMuted) g.DrawLine(pen, mx - 8, my - 9, mx + 8, my + 9);
                }

                // pause: two bars while recording, a play triangle while paused
                Rectangle pr = _buttons[Btn.Pause];
                int cx = pr.X + pr.Width / 2, cy = pr.Y + pr.Height / 2;
                if (_paused)
                    g.FillPolygon(brush, new[] { new Point(cx - 4, cy - 6), new Point(cx - 4, cy + 6), new Point(cx + 6, cy) });
                else
                {
                    g.FillRectangle(brush, cx - 6, cy - 6, 4, 12);
                    g.FillRectangle(brush, cx + 2, cy - 6, 4, 12);
                }

                // save: a little floppy disk
                Rectangle sr = _buttons[Btn.Save];
                int sx = sr.X + sr.Width / 2 - 8, sy = sr.Y + sr.Height / 2 - 8;
                using (var thin = new Pen(Color.White, 1.6f))
                {
                    g.DrawLines(thin, new[]
                    {
                        new Point(sx, sy), new Point(sx + 12, sy), new Point(sx + 16, sy + 4),
                        new Point(sx + 16, sy + 16), new Point(sx, sy + 16), new Point(sx, sy)
                    });
                    g.FillRectangle(brush, sx + 3, sy, 8, 5);          // shutter
                    g.DrawRectangle(thin, sx + 3, sy + 9, 10, 7);      // label
                }
            }
        }

        private static void DrawButton(Graphics g, Rectangle r, bool hover, Color on)
        {
            using (var path = Theme.Round(r, 6))
            using (var fill = new SolidBrush(on != Color.Empty ? on : Color.FromArgb(hover ? 80 : 45, 255, 255, 255)))
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
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Keeps a window out of screen captures (SetWindowDisplayAffinity WDA_EXCLUDEFROMCAPTURE,
    /// Windows 10 2004+), so the recording badge and frame never end up in the video even
    /// when the camera follows the pointer over them. Older Windows: a no-op.
    /// </summary>
    static class CaptureExclusion
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        public static void Apply(IntPtr hwnd)
        {
            try { SetWindowDisplayAffinity(hwnd, 0x11 /* WDA_EXCLUDEFROMCAPTURE */); } catch { }
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

        /// <summary>Moves the frame to mark a new captured patch (follow cursor / zoom).</summary>
        public void MarkCaptured(Rectangle captured)
        {
            var bounds = Rectangle.Inflate(captured, Thickness, Thickness);
            if (bounds == Bounds) return;
            bool resized = bounds.Size != Bounds.Size;
            Bounds = bounds;
            if (resized)
            {
                var frame = new Region(new Rectangle(0, 0, bounds.Width, bounds.Height));
                frame.Exclude(new Rectangle(Thickness, Thickness, bounds.Width - 2 * Thickness, bounds.Height - 2 * Thickness));
                Region = frame;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CaptureExclusion.Apply(Handle);
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

    /// <summary>
    /// The action log written next to a video (same name, .txt): every mouse click and key
    /// press, stamped with the second of the video it happens at. Paused stretches are not
    /// in the video, so nothing is logged while paused and the clock skips them. Plain
    /// typing is gathered into one "Typed" line per burst; shortcuts and special keys get
    /// a line each. The file is appended as the recording goes, so it survives a crash.
    /// </summary>
    public class ActionLog : IDisposable
    {
        readonly System.IO.StreamWriter _w;
        readonly VideoRecorder _rec;
        readonly System.Text.StringBuilder _typed = new System.Text.StringBuilder();
        TimeSpan _typedAt;
        DateTime _typedLast;

        public string Path { get; private set; }

        public ActionLog(VideoRecorder recorder, string videoPath)
        {
            _rec = recorder;
            Path = System.IO.Path.ChangeExtension(videoPath, ".txt");
            _w = new System.IO.StreamWriter(Path, true, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            _w.WriteLine("MicroApp action log for " + System.IO.Path.GetFileName(videoPath));
            _w.WriteLine("Recorded " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ". Times are positions in the video; positions are pixels in the video (0, 0 is its top-left).");
            _w.WriteLine();
        }

        static string Stamp(TimeSpan t)
        {
            return t.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}.{3}", (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds / 100)
                : string.Format("{0:00}:{1:00}.{2}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds / 100);
        }

        void Line(TimeSpan t, string text)
        {
            try { _w.WriteLine(Stamp(t) + "  " + text); } catch { }
        }

        void FlushTyped()
        {
            if (_typed.Length == 0) return;
            Line(_typedAt, "Typed \"" + _typed + "\"");
            _typed.Clear();
        }

        /// <summary>Where a screen point lands in the video.</summary>
        string Where(Point screen, Size videoSize)
        {
            Rectangle src = _rec.CurrentSource;
            if (!src.Contains(screen)) return "outside the recording";
            int x = (int)((screen.X - src.X) * (double)videoSize.Width / src.Width);
            int y = (int)((screen.Y - src.Y) * (double)videoSize.Height / src.Height);
            return "at " + x + ", " + y;
        }

        public void Mouse(Point screen, MouseButtons button, Size videoSize)
        {
            if (_rec.Paused) return;
            FlushTyped();
            string which = button == MouseButtons.Right ? "Right click" : button == MouseButtons.Middle ? "Middle click"
                         : button == MouseButtons.Left ? "Left click" : button + " click";
            Line(_rec.VideoTime, which + " " + Where(screen, videoSize));
        }

        /// <summary>A key went down: shortcuts and special keys are logged here, plain characters by KeyChar.</summary>
        public void KeyDown(Keys key, Keys modifiers)
        {
            if (_rec.Paused) return;
            Keys k = key & Keys.KeyCode;
            if (k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin ||
                k == Keys.LShiftKey || k == Keys.RShiftKey || k == Keys.LControlKey || k == Keys.RControlKey ||
                k == Keys.LMenu || k == Keys.RMenu) return;
            bool chord = (modifiers & (Keys.Control | Keys.Alt)) != 0 || IsWinDown();
            if (!chord && IsCharacterKey(k)) return;
            FlushTyped();
            var name = new System.Text.StringBuilder();
            if ((modifiers & Keys.Control) != 0) name.Append("Ctrl+");
            if ((modifiers & Keys.Alt) != 0) name.Append("Alt+");
            if ((modifiers & Keys.Shift) != 0) name.Append("Shift+");
            if (IsWinDown()) name.Append("Win+");
            name.Append(KeyName(k));
            Line(_rec.VideoTime, "Key " + name);
        }

        /// <summary>A character was typed (layout-aware, from the hook's KeyPress).</summary>
        public void KeyChar(char c)
        {
            if (_rec.Paused || c < 32) return;
            if ((Control.ModifierKeys & (Keys.Control | Keys.Alt)) != 0) return;
            DateTime now = DateTime.UtcNow;
            if (_typed.Length > 0 && (now - _typedLast).TotalSeconds > 1.5) FlushTyped();
            if (_typed.Length == 0) _typedAt = _rec.VideoTime;
            _typed.Append(c);
            _typedLast = now;
        }

        static bool IsCharacterKey(Keys k)
        {
            return (k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9) ||
                   k == Keys.Space || k == Keys.Multiply || k == Keys.Add || k == Keys.Subtract || k == Keys.Decimal || k == Keys.Divide ||
                   (k >= Keys.Oem1 && k <= Keys.Oem102) || k == Keys.OemMinus || k == Keys.Oemplus || k == Keys.Oemcomma || k == Keys.OemPeriod;
        }

        static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Escape: return "Esc";
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
                case Keys.Capital: return "CapsLock";
                case Keys.Snapshot: return "PrintScreen";
            }
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            return k.ToString();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        static bool IsWinDown()
        {
            try { return (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0; }
            catch { return false; }
        }

        public void Paused(bool paused)
        {
            FlushTyped();
            Line(_rec.VideoTime, paused ? "(recording paused)" : "(recording resumed)");
        }

        public void Dispose()
        {
            FlushTyped();
            try { Line(_rec.VideoTime, "(recording stopped)"); _w.Dispose(); } catch { }
        }
    }
}
