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
        private readonly Mp4Writer _writer;
        private readonly AudioCapture _audio;
        private readonly ConcurrentQueue<byte[]> _audioQueue = new ConcurrentQueue<byte[]>();
        private readonly int _audioBlockAlign;

        private Thread _thread;
        private volatile bool _stop;
        private long _audioSamples;
        private bool _audioDead;

        public string Path { get { return _writer.Path; } }
        public int FrameCount { get; private set; }
        public bool Running { get { return _thread != null && _thread.IsAlive; } }
        /// <summary>True when sound was asked for but no capture device was available.</summary>
        public bool AudioMissing { get; private set; }

        public VideoRecorder(Rectangle region, string path, int fps, int maxSeconds,
                             VideoQuality quality, VideoAudioSource audioSource)
        {
            // H.264 wants even dimensions
            _region = new Rectangle(region.X, region.Y, region.Width & ~1, region.Height & ~1);
            _fps = Math.Max(1, Math.Min(30, fps));
            _maxSeconds = Math.Max(1, Math.Min(3600, maxSeconds));

            if (audioSource != VideoAudioSource.None)
            {
                _audio = AudioCapture.TryCreate(audioSource == VideoAudioSource.System);
                AudioMissing = _audio == null;
            }

            // aim for "screen content" rates: quality picks the bits per pixel per frame
            double bitsPerPixel = quality == VideoQuality.Small ? 0.045
                                : quality == VideoQuality.Sharp ? 0.18
                                : 0.09;
            int videoBitrate = (int)Math.Max(250_000,
                Math.Min(12_000_000, (double)_region.Width * _region.Height * _fps * bitsPerPixel));
            int audioBytesPerSecond = quality == VideoQuality.Small ? 12000 : 16000;

            try
            {
                _writer = new Mp4Writer(path, _region.Width, _region.Height, _fps, videoBitrate,
                                        _audio != null ? _audio.SampleRate : 0,
                                        _audio != null ? _audio.Channels : 0,
                                        audioBytesPerSecond);
            }
            catch (Exception)
            {
                if (_audio != null) _audio.Dispose();
                throw;
            }
            _audioBlockAlign = _audio != null ? _audio.Channels * 2 : 0;
        }

        public void Start()
        {
            if (_audio != null)
            {
                _audio.Start((buffer, bytes) => _audioQueue.Enqueue(buffer));
            }
            _thread = new Thread(Loop) { IsBackground = true, Name = "MicroApp video recorder" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            if (_thread != null) _thread.Join(6000);
        }

        private void Loop()
        {
            int frameMs = 1000 / _fps;
            int maxFrames = _fps * _maxSeconds;
            long frameDuration = 10_000_000L / _fps;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            using (var frame = new Bitmap(_region.Width, _region.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            using (var g = Graphics.FromImage(frame))
            {
                for (int i = 0; i < maxFrames && !_stop; i++)
                {
                    long due = (long)i * frameMs;
                    long wait = due - clock.ElapsedMilliseconds;
                    if (wait > 0) Thread.Sleep((int)wait);

                    g.CopyFromScreen(_region.Location, Point.Empty, _region.Size, CopyPixelOperation.SourceCopy);
                    DrawCursor(g);

                    // timestamps follow the wall clock, not the frame index: when a grab
                    // runs late the video stays in step with the sound instead of speeding up
                    long timestamp = clock.ElapsedTicks * 10_000_000L / System.Diagnostics.Stopwatch.Frequency;
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
            }

            if (_audio != null) _audio.Stop();
            DrainAudio(long.MaxValue, true);
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
            if (_thread != null && _thread.IsAlive) _thread.Join(6000);
            if (_audio != null) _audio.Dispose();
            _writer.Dispose();
        }
    }
}
