using System;
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
    public class AudioCapture : IDisposable
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

        public static AudioCapture TryCreate(bool loopback)
        {
            try
            {
                return new AudioCapture(loopback);
            }
            catch (Exception)
            {
                return null;   // no device, disabled endpoint, exclusive-mode holder...
            }
        }

        private AudioCapture(bool loopback)
        {
            var enumerator = (Wasapi.IMMDeviceEnumerator)new Wasapi.MMDeviceEnumeratorComObject();
            try
            {
                Wasapi.IMMDevice device;
                enumerator.GetDefaultAudioEndpoint(loopback ? 0 : 1 /* eRender : eCapture */, 0, out device);
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

            Channels = Math.Min(2, _deviceChannels);
            _resample = _deviceRate != 44100 && _deviceRate != 48000;
            SampleRate = _resample ? 48000 : _deviceRate;
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
            if (!silent)
            {
                if (_deviceFloat)
                {
                    var raw = new float[frames * _deviceChannels];
                    Marshal.Copy(data, raw, 0, raw.Length);
                    for (int f = 0; f < frames; f++)
                    {
                        for (int c = 0; c < Channels; c++)
                        {
                            float v = raw[f * _deviceChannels + c];
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
                            pcm[f * Channels + c] = raw[f * _deviceChannels + c];
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
                            pcm[f * Channels + c] = (short)(raw[f * _deviceChannels + c] >> 16);
                        }
                    }
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

    /// <summary>The slice of WASAPI the capture needs.</summary>
    internal static class Wasapi
    {
        public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT =
            new Guid("00000003-0000-0010-8000-00AA00389B71");

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        public class MMDeviceEnumeratorComObject
        {
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
