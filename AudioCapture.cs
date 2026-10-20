using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MicroApp
{
    /// <summary>
    /// Captures audio for a video recording through WASAPI: either what the machine is
    /// playing (a loopback tap on the default output) or the default microphone.
    /// Whatever the device delivers (float or PCM, any channel count, any rate) is
    /// converted to 16-bit PCM in at most two channels, resampled to 48 kHz when the
    /// device runs at a rate AAC does not take, and handed to the callback in the
    /// capture thread. No audio device is not an error: TryCreate returns null and the
    /// recording simply has no sound track.
    /// </summary>
    /// <summary>A source of 16-bit PCM for the video recorder: one device, or the mix of two.</summary>
    public interface IAudioSource : IDisposable
    {
        int SampleRate { get; }
        int Channels { get; }
        /// <summary>Muted: the source keeps its clock but delivers silence.</summary>
        bool Muted { get; set; }
        void Start(Action<byte[], int> onPcm16);
        void Stop();
    }

    public class AudioCapture : IAudioSource
    {
        private readonly Wasapi.IAudioClient _client;
        private readonly Wasapi.IAudioCaptureClient _capture;
        private readonly int _deviceRate;
        private readonly int _deviceChannels;
        private readonly int _deviceBlockAlign;
        private readonly bool _deviceFloat;
        private readonly int _deviceBits;

        private Thread _thread;
        private volatile bool _stop;
        private Action<byte[], int> _onPcm;

        // linear resampler state, carried across packets
        private readonly bool _resample;
        private double _resamplePos;
        private short[] _lastFrame;

        /// <summary>Rate of the PCM handed to the callback: always 44100 or 48000.</summary>
        public int SampleRate { get; private set; }
        /// <summary>Channels handed to the callback: 1 or 2.</summary>
        public int Channels { get; private set; }

        /// <summary>Muted: the device keeps running (and the clock with it) but hands over silence.</summary>
        public bool Muted { get; set; }

        // playback gain applied to every sample, 1 = unchanged, 2 = +6 dB ("200%"). Clamped on set.
        private float _gain = 1f;

        /// <summary>Linear gain on the captured signal: 1 = 100% (no change), 2 = 200% (hard-clamped).</summary>
        public float Gain
        {
            get { return _gain; }
            set { _gain = value < 0f ? 0f : (value > 2f ? 2f : value); }
        }

        /// <summary>A capture endpoint the user can choose: its WASAPI id and a friendly name.</summary>
        public struct AudioDevice
        {
            public string Id;
            public string Name;
            public override string ToString() { return Name; }
        }

        /// <param name="rate">0: the device's own rate when it is 44.1/48 kHz; otherwise resample to this.</param>
        /// <param name="stereo">Always hand over two channels (a mono microphone is copied to both).</param>
        /// <param name="deviceId">A specific capture device (from <see cref="ListMicrophones"/>); null/empty is the default. Ignored for loopback.</param>
        /// <param name="gainPercent">Linear gain 0-200; 100 leaves the signal untouched.</param>
        public static AudioCapture TryCreate(bool loopback, int rate = 0, bool stereo = false,
                                             string deviceId = null, int gainPercent = 100)
        {
            try
            {
                return new AudioCapture(loopback, rate, stereo, deviceId, gainPercent);
            }
            catch (Exception)
            {
                return null;   // no device, disabled endpoint, exclusive-mode holder...
            }
        }

        /// <summary>
        /// Every active capture endpoint (microphone / line-in), each with its WASAPI id and
        /// friendly name. The empty list means none; the caller then falls back to the default.
        /// Never throws: a device that cannot be read is simply skipped.
        /// </summary>
        public static List<AudioDevice> ListMicrophones()
        {
            var list = new List<AudioDevice>();
            Wasapi.IMMDeviceEnumerator enumerator = null;
            try
            {
                enumerator = (Wasapi.IMMDeviceEnumerator)new Wasapi.MMDeviceEnumeratorComObject();
                IntPtr collPtr;
                enumerator.EnumAudioEndpoints(1 /* eCapture */, 0x1 /* DEVICE_STATE_ACTIVE */, out collPtr);
                if (collPtr == IntPtr.Zero) return list;
                var coll = (Wasapi.IMMDeviceCollection)Marshal.GetObjectForIUnknown(collPtr);
                Marshal.Release(collPtr);
                try
                {
                    int count;
                    coll.GetCount(out count);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr devPtr;
                        coll.Item(i, out devPtr);
                        if (devPtr == IntPtr.Zero) continue;
                        var device = (Wasapi.IMMDevice)Marshal.GetObjectForIUnknown(devPtr);
                        Marshal.Release(devPtr);
                        try
                        {
                            string id = ReadDeviceId(device);
                            string name = ReadFriendlyName(device);
                            if (!string.IsNullOrEmpty(id))
                            {
                                list.Add(new AudioDevice { Id = id, Name = string.IsNullOrEmpty(name) ? "Microphone" : name });
                            }
                        }
                        finally { Mf.Release(device); }
                    }
                }
                finally { Mf.Release(coll); }
            }
            catch (Exception)
            {
                // enumeration unavailable: hand back whatever we managed to collect
            }
            finally { Mf.Release(enumerator); }
            return list;
        }

        private static string ReadDeviceId(Wasapi.IMMDevice device)
        {
            IntPtr idPtr = IntPtr.Zero;
            try
            {
                device.GetId(out idPtr);
                return idPtr == IntPtr.Zero ? null : Marshal.PtrToStringUni(idPtr);
            }
            catch (Exception) { return null; }
            finally { if (idPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(idPtr); }
        }

        private static string ReadFriendlyName(Wasapi.IMMDevice device)
        {
            IntPtr storePtr = IntPtr.Zero;
            IntPtr pv = IntPtr.Zero;
            try
            {
                device.OpenPropertyStore(0 /* STGM_READ */, out storePtr);
                if (storePtr == IntPtr.Zero) return null;
                var store = (Wasapi.IPropertyStore)Marshal.GetObjectForIUnknown(storePtr);
                Marshal.Release(storePtr);
                try
                {
                    pv = Marshal.AllocCoTaskMem(32);   // PROPVARIANT (16 on x86, 24 on x64; 32 is safe)
                    for (int i = 0; i < 32; i++) Marshal.WriteByte(pv, i, 0);
                    var key = Wasapi.PKEY_Device_FriendlyName;
                    store.GetValue(ref key, pv);
                    IntPtr strPtr = Marshal.ReadIntPtr(pv, 8);   // VT_LPWSTR payload
                    return strPtr == IntPtr.Zero ? null : Marshal.PtrToStringUni(strPtr);
                }
                finally
                {
                    if (pv != IntPtr.Zero) { try { Wasapi.PropVariantClear(pv); } catch (Exception) { } Marshal.FreeCoTaskMem(pv); }
                    Mf.Release(store);
                }
            }
            catch (Exception) { return null; }
        }

        private AudioCapture(bool loopback, int rate, bool stereo, string deviceId, int gainPercent)
        {
            _gain = gainPercent < 0 ? 0f : (gainPercent > 200 ? 2f : gainPercent / 100f);
            var enumerator = (Wasapi.IMMDeviceEnumerator)new Wasapi.MMDeviceEnumeratorComObject();
            try
            {
                Wasapi.IMMDevice device;
                if (!loopback && !string.IsNullOrEmpty(deviceId))
                {
                    IntPtr devPtr;
                    enumerator.GetDevice(deviceId, out devPtr);
                    device = (Wasapi.IMMDevice)Marshal.GetObjectForIUnknown(devPtr);
                    Marshal.Release(devPtr);   // balance GetDevice's ref; the RCW keeps its own
                }
                else
                {
                    enumerator.GetDefaultAudioEndpoint(loopback ? 0 : 1 /* eRender : eCapture */, 0, out device);
                }
                try
                {
                    object clientObj;
                    Guid iid = typeof(Wasapi.IAudioClient).GUID;
                    device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out clientObj);
                    _client = (Wasapi.IAudioClient)clientObj;

                    IntPtr format;
                    _client.GetMixFormat(out format);
                    try
                    {
                        int tag = Marshal.ReadInt16(format, 0);
                        _deviceChannels = Marshal.ReadInt16(format, 2);
                        _deviceRate = Marshal.ReadInt32(format, 4);
                        _deviceBlockAlign = Marshal.ReadInt16(format, 12);
                        _deviceBits = Marshal.ReadInt16(format, 14);
                        if (tag == -2 /* WAVE_FORMAT_EXTENSIBLE */)
                        {
                            var sub = new byte[16];
                            Marshal.Copy(format + 24, sub, 0, 16);
                            _deviceFloat = new Guid(sub) == Wasapi.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
                        }
                        else
                        {
                            _deviceFloat = tag == 3 /* WAVE_FORMAT_IEEE_FLOAT */;
                        }
                        if (!_deviceFloat && _deviceBits != 16 && _deviceBits != 32)
                        {
                            throw new NotSupportedException("Unsupported mix format: " + _deviceBits + " bits");
                        }

                        _client.Initialize(0 /* shared */, loopback ? 0x00020000 : 0 /* LOOPBACK */,
                                           2_000_000 /* 200 ms buffer */, 0, format, IntPtr.Zero);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(format);
                    }

                    object captureObj;
                    Guid captureIid = typeof(Wasapi.IAudioCaptureClient).GUID;
                    _client.GetService(ref captureIid, out captureObj);
                    _capture = (Wasapi.IAudioCaptureClient)captureObj;
                }
                finally
                {
                    Mf.Release(device);
                }
            }
            finally
            {
                Mf.Release(enumerator);
            }

            Channels = stereo ? 2 : Math.Min(2, _deviceChannels);
            if (rate > 0)
            {
                SampleRate = rate;
                _resample = _deviceRate != rate;
            }
            else
            {
                _resample = _deviceRate != 44100 && _deviceRate != 48000;
                SampleRate = _resample ? 48000 : _deviceRate;
            }
        }

        public void Start(Action<byte[], int> onPcm16)
        {
            _onPcm = onPcm16;
            _client.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "MicroApp audio capture" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            if (_thread != null) _thread.Join(2000);
            try { _client.Stop(); } catch (Exception) { }
        }

        private void Loop()
        {
            while (!_stop)
            {
                try
                {
                    int packetFrames;
                    _capture.GetNextPacketSize(out packetFrames);
                    while (packetFrames > 0 && !_stop)
                    {
                        IntPtr data;
                        int frames, flags;
                        long devPos, qpcPos;
                        _capture.GetBuffer(out data, out frames, out flags, out devPos, out qpcPos);
                        try
                        {
                            bool silent = (flags & 1) != 0;   // AUDCLNT_BUFFERFLAGS_SILENT
                            Deliver(data, frames, silent);
                        }
                        finally
                        {
                            _capture.ReleaseBuffer(frames);
                        }
                        _capture.GetNextPacketSize(out packetFrames);
                    }
                }
                catch (Exception)
                {
                    // a hiccup on the device shouldn't kill the video; try again next tick
                }
                Thread.Sleep(20);
            }
        }

        /// <summary>Device frames to 16-bit, at most stereo, AAC-friendly rate.</summary>
        private void Deliver(IntPtr data, int frames, bool silent)
        {
            if (frames <= 0) return;

            var pcm = new short[frames * Channels];
            if (!silent && !Muted)
            {
                if (_deviceFloat)
                {
                    var raw = new float[frames * _deviceChannels];
                    Marshal.Copy(data, raw, 0, raw.Length);
                    for (int f = 0; f < frames; f++)
                    {
                        for (int c = 0; c < Channels; c++)
                        {
                            float v = raw[f * _deviceChannels + Math.Min(c, _deviceChannels - 1)];
                            if (v > 1f) v = 1f;
                            else if (v < -1f) v = -1f;
                            pcm[f * Channels + c] = (short)(v * 32767f);
                        }
                    }
                }
                else if (_deviceBits == 16)
                {
                    var raw = new short[frames * _deviceChannels];
                    Marshal.Copy(data, raw, 0, raw.Length);
                    for (int f = 0; f < frames; f++)
                    {
                        for (int c = 0; c < Channels; c++)
                        {
                            pcm[f * Channels + c] = raw[f * _deviceChannels + Math.Min(c, _deviceChannels - 1)];
                        }
                    }
                }
                else   // 32-bit integer PCM
                {
                    var raw = new int[frames * _deviceChannels];
                    Marshal.Copy(data, raw, 0, raw.Length);
                    for (int f = 0; f < frames; f++)
                    {
                        for (int c = 0; c < Channels; c++)
                        {
                            pcm[f * Channels + c] = (short)(raw[f * _deviceChannels + Math.Min(c, _deviceChannels - 1)] >> 16);
                        }
                    }
                }
            }

            // digital boost/cut: scale every sample and hard-clamp so it can never wrap
            if (_gain != 1f && !silent && !Muted)
            {
                for (int i = 0; i < pcm.Length; i++)
                {
                    int v = (int)(pcm[i] * _gain);
                    pcm[i] = (short)(v > short.MaxValue ? short.MaxValue : v < short.MinValue ? short.MinValue : v);
                }
            }

            if (_resample) pcm = Resample(pcm, frames);
            if (pcm.Length == 0) return;

            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            var handler = _onPcm;
            if (handler != null) handler(bytes, bytes.Length);
        }

        /// <summary>
        /// Plain linear interpolation from the device rate to 48 kHz. Only exotic mix
        /// rates come through here; 44.1 and 48 kHz devices bypass it entirely.
        /// </summary>
        private short[] Resample(short[] input, int frames)
        {
            double step = (double)_deviceRate / SampleRate;
            int outFrames = (int)((frames - _resamplePos) / step);
            if (outFrames <= 0)
            {
                _resamplePos -= frames;
                _lastFrame = CopyLastFrame(input, frames);
                return new short[0];
            }

            var output = new short[outFrames * Channels];
            for (int o = 0; o < outFrames; o++)
            {
                double pos = _resamplePos + o * step;
                int i = (int)Math.Floor(pos);
                double frac = pos - i;
                for (int c = 0; c < Channels; c++)
                {
                    short a = i >= 0 ? input[i * Channels + c]
                                     : (_lastFrame != null ? _lastFrame[c] : input[c]);
                    short b = input[Math.Min(i + 1, frames - 1) * Channels + c];
                    output[o * Channels + c] = (short)(a + (b - a) * frac);
                }
            }
            _resamplePos = _resamplePos + outFrames * step - frames;
            _lastFrame = CopyLastFrame(input, frames);
            return output;
        }

        private short[] CopyLastFrame(short[] input, int frames)
        {
            var last = new short[Channels];
            Array.Copy(input, (frames - 1) * Channels, last, 0, Channels);
            return last;
        }

        public void Dispose()
        {
            Stop();
            Mf.Release(_capture);
            Mf.Release(_client);
        }
    }

    /// <summary>
    /// System sound and the microphone in one track: both devices run at 48 kHz stereo, and
    /// a mixer thread adds them every 20 ms. The microphone never stops delivering, so it
    /// sets the pace; the loopback tap goes quiet while nothing plays, and whatever it has
    /// is mixed in on top (silence otherwise). If the microphone stops delivering, the
    /// system sound carries on alone. Muted silences the microphone only.
    /// </summary>
    public class AudioMixer : IAudioSource
    {
        const int Rate = 48000;
        readonly AudioCapture _system, _mic;
        readonly List<short> _sysBuf = new List<short>(), _micBuf = new List<short>();
        readonly object _lock = new object();
        Thread _thread;
        volatile bool _stop;
        Action<byte[], int> _onPcm;
        long _micLastTicks;

        public int SampleRate { get { return Rate; } }
        public int Channels { get { return 2; } }
        public bool Muted { get { return _mic.Muted; } set { _mic.Muted = value; } }

        AudioMixer(AudioCapture system, AudioCapture mic)
        {
            _system = system;
            _mic = mic;
        }

        /// <summary>
        /// Both devices mixed; when only one of them is available, that one alone (at the same
        /// 48 kHz stereo), with <paramref name="micMissing"/> / <paramref name="systemMissing"/> saying which.
        /// </summary>
        public static IAudioSource TryCreate(out bool micMissing, out bool systemMissing,
                                             string micDeviceId = null, int micGain = 100)
        {
            AudioCapture system = AudioCapture.TryCreate(true, Rate, true);
            AudioCapture mic = AudioCapture.TryCreate(false, Rate, true, micDeviceId, micGain);
            micMissing = mic == null;
            systemMissing = system == null;
            if (system != null && mic != null) return new AudioMixer(system, mic);
            return (IAudioSource)mic ?? system;
        }

        public void Start(Action<byte[], int> onPcm16)
        {
            _onPcm = onPcm16;
            _micLastTicks = DateTime.UtcNow.Ticks;
            _system.Start((b, n) => Append(_sysBuf, b, n));
            _mic.Start((b, n) => { Append(_micBuf, b, n); _micLastTicks = DateTime.UtcNow.Ticks; });
            _thread = new Thread(Loop) { IsBackground = true, Name = "MicroApp audio mixer" };
            _thread.Start();
        }

        void Append(List<short> buf, byte[] bytes, int count)
        {
            var samples = new short[count / 2];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
            lock (_lock) buf.AddRange(samples);
        }

        void Loop()
        {
            while (!_stop)
            {
                Thread.Sleep(20);
                short[] mixed = null;
                lock (_lock)
                {
                    bool micAlive = (DateTime.UtcNow.Ticks - _micLastTicks) < TimeSpan.TicksPerMillisecond * 500;
                    int n = micAlive ? _micBuf.Count : _sysBuf.Count;   // samples (both are stereo)
                    n -= n % 2;
                    if (n > 0)
                    {
                        mixed = new short[n];
                        int fromMic = Math.Min(n, _micBuf.Count), fromSys = Math.Min(n, _sysBuf.Count);
                        for (int i = 0; i < fromMic; i++) mixed[i] = _micBuf[i];
                        for (int i = 0; i < fromSys; i++)
                        {
                            int v = mixed[i] + _sysBuf[i];
                            mixed[i] = (short)(v > short.MaxValue ? short.MaxValue : v < short.MinValue ? short.MinValue : v);
                        }
                        _micBuf.RemoveRange(0, fromMic);
                        _sysBuf.RemoveRange(0, fromSys);
                    }
                    // the two devices' clocks drift apart slowly; never let system sound lag more than 0.5 s
                    int maxBacklog = Rate;   // 0.5 s of stereo samples
                    if (_sysBuf.Count > maxBacklog) _sysBuf.RemoveRange(0, _sysBuf.Count - maxBacklog);
                    if (_micBuf.Count > maxBacklog * 2) _micBuf.RemoveRange(0, _micBuf.Count - maxBacklog * 2);
                }
                if (mixed != null)
                {
                    var bytes = new byte[mixed.Length * 2];
                    Buffer.BlockCopy(mixed, 0, bytes, 0, bytes.Length);
                    var handler = _onPcm;
                    if (handler != null) handler(bytes, bytes.Length);
                }
            }
        }

        public void Stop()
        {
            _mic.Stop();
            _system.Stop();
            _stop = true;
            if (_thread != null) _thread.Join(1000);
        }

        public void Dispose()
        {
            Stop();
            _mic.Dispose();
            _system.Dispose();
        }
    }

    /// <summary>The slice of WASAPI the capture needs.</summary>
    internal static class Wasapi
    {
        public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT =
            new Guid("00000003-0000-0010-8000-00AA00389B71");

        // PKEY_Device_FriendlyName — the human-readable endpoint name ("Microphone (Realtek...)").
        public static PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(IntPtr pvar);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct PROPERTYKEY
        {
            public Guid fmtid;
            public int pid;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        public class MMDeviceEnumeratorComObject
        {
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceCollection
        {
            void GetCount(out int count);
            void Item(int index, out IntPtr device);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore
        {
            void GetCount(out int count);
            void GetAt(int index, out PROPERTYKEY key);
            void GetValue([In] ref PROPERTYKEY key, [In] IntPtr pv);
            void SetValue([In] ref PROPERTYKEY key, [In] IntPtr pv);
            void Commit();
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
            void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
            void RegisterEndpointNotificationCallback(IntPtr client);
            void UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            void Activate([In] ref Guid iid, int clsCtx, IntPtr activationParams,
                          [MarshalAs(UnmanagedType.IUnknown)] out object activated);
            void OpenPropertyStore(int access, out IntPtr properties);
            void GetId(out IntPtr id);
            void GetState(out int state);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient
        {
            void Initialize(int shareMode, int streamFlags, long bufferDuration,
                            long periodicity, IntPtr format, IntPtr audioSessionGuid);
            void GetBufferSize(out int frames);
            void GetStreamLatency(out long latency);
            void GetCurrentPadding(out int padding);
            void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
            void GetMixFormat(out IntPtr format);
            void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            void Start();
            void Stop();
            void Reset();
            void SetEventHandle(IntPtr handle);
            void GetService([In] ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioCaptureClient
        {
            void GetBuffer(out IntPtr data, out int frames, out int flags,
                           out long devicePosition, out long qpcPosition);
            void ReleaseBuffer(int frames);
            void GetNextPacketSize(out int frames);
        }
    }
}
