using System;
using System.Runtime.InteropServices;

namespace MicroApp
{
    /// <summary>
    /// Streams H.264 video and AAC audio into an MP4 through the Media Foundation
    /// sink writer that ships with Windows, so a recording is a fraction of the size
    /// of the same material as a GIF and no encoder has to be bundled. Frames and
    /// audio go straight to the encoder as they arrive; memory stays flat.
    ///
    /// Video comes in as 32-bit RGB rows copied bottom-up (the layout Media
    /// Foundation assumes for RGB when no stride is declared), audio as 16-bit PCM.
    /// The writer inserts the colour converter and the encoders itself.
    /// </summary>
    public class Mp4Writer : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly long _frameDuration;
        private Mf.IMFSinkWriter _writer;
        private readonly int _videoStream;
        private readonly int _audioStream = -1;
        private readonly int _audioRate;
        private readonly int _audioBlockAlign;
        private bool _finished;

        public string Path { get; private set; }

        /// <param name="videoBitrate">Average H.264 bits per second.</param>
        /// <param name="audioRate">PCM sample rate, 44100 or 48000; 0 records without sound.</param>
        /// <param name="audioChannels">1 or 2.</param>
        /// <param name="audioBytesPerSecond">AAC size: 12000, 16000, 20000 or 24000 bytes/s.</param>
        public Mp4Writer(string path, int width, int height, int fps, int videoBitrate,
                         int audioRate, int audioChannels, int audioBytesPerSecond)
        {
            Path = path;
            _width = width;
            _height = height;
            _frameDuration = 10_000_000L / Math.Max(1, fps);
            _audioRate = audioRate;
            _audioBlockAlign = audioChannels * 2;

            Mf.Startup();

            Mf.IMFAttributes attrs;
            Mf.Check(Mf.MFCreateAttributes(out attrs, 2));
            Mf.SetU32(attrs, Mf.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            Mf.SetU32(attrs, Mf.MF_SINK_WRITER_DISABLE_THROTTLING, 1);

            try
            {
                Mf.Check(Mf.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attrs, out _writer));

                // H.264 out, raw RGB32 in; the writer supplies the converter chain
                var vOut = Mf.CreateMediaType();
                Mf.SetGuid(vOut, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Video);
                Mf.SetGuid(vOut, Mf.MF_MT_SUBTYPE, Mf.MFVideoFormat_H264);
                Mf.SetU32(vOut, Mf.MF_MT_AVG_BITRATE, videoBitrate);
                Mf.SetU32(vOut, Mf.MF_MT_INTERLACE_MODE, 2);            // progressive
                Mf.SetU32(vOut, Mf.MF_MT_MPEG2_PROFILE, 77);            // H.264 Main
                Mf.SetU64(vOut, Mf.MF_MT_FRAME_SIZE, ((long)width << 32) | (uint)height);
                Mf.SetU64(vOut, Mf.MF_MT_FRAME_RATE, ((long)fps << 32) | 1);
                Mf.SetU64(vOut, Mf.MF_MT_PIXEL_ASPECT_RATIO, (1L << 32) | 1);
                _writer.AddStream(vOut, out _videoStream);

                var vIn = Mf.CreateMediaType();
                Mf.SetGuid(vIn, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Video);
                Mf.SetGuid(vIn, Mf.MF_MT_SUBTYPE, Mf.MFVideoFormat_RGB32);
                Mf.SetU32(vIn, Mf.MF_MT_INTERLACE_MODE, 2);
                Mf.SetU64(vIn, Mf.MF_MT_FRAME_SIZE, ((long)width << 32) | (uint)height);
                Mf.SetU64(vIn, Mf.MF_MT_FRAME_RATE, ((long)fps << 32) | 1);
                Mf.SetU64(vIn, Mf.MF_MT_PIXEL_ASPECT_RATIO, (1L << 32) | 1);
                _writer.SetInputMediaType(_videoStream, vIn, null);
                Mf.Release(vOut);
                Mf.Release(vIn);

                if (audioRate > 0)
                {
                    var aOut = Mf.CreateMediaType();
                    Mf.SetGuid(aOut, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Audio);
                    Mf.SetGuid(aOut, Mf.MF_MT_SUBTYPE, Mf.MFAudioFormat_AAC);
                    Mf.SetU32(aOut, Mf.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                    Mf.SetU32(aOut, Mf.MF_MT_AUDIO_SAMPLES_PER_SECOND, audioRate);
                    Mf.SetU32(aOut, Mf.MF_MT_AUDIO_NUM_CHANNELS, audioChannels);
                    Mf.SetU32(aOut, Mf.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, audioBytesPerSecond);
                    _writer.AddStream(aOut, out _audioStream);

                    var aIn = Mf.CreateMediaType();
                    Mf.SetGuid(aIn, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Audio);
                    Mf.SetGuid(aIn, Mf.MF_MT_SUBTYPE, Mf.MFAudioFormat_PCM);
                    Mf.SetU32(aIn, Mf.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                    Mf.SetU32(aIn, Mf.MF_MT_AUDIO_SAMPLES_PER_SECOND, audioRate);
                    Mf.SetU32(aIn, Mf.MF_MT_AUDIO_NUM_CHANNELS, audioChannels);
                    Mf.SetU32(aIn, Mf.MF_MT_AUDIO_BLOCK_ALIGNMENT, _audioBlockAlign);
                    Mf.SetU32(aIn, Mf.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, audioRate * _audioBlockAlign);
                    _writer.SetInputMediaType(_audioStream, aIn, null);
                    Mf.Release(aOut);
                    Mf.Release(aIn);
                }

                _writer.BeginWriting();
            }
            finally
            {
                Mf.Release(attrs);
            }
        }

        /// <summary>Top-down 32bpp rows from Bitmap.LockBits; copied here bottom-up.</summary>
        public void WriteVideoFrame(IntPtr topDownScan0, int srcStride, long timestamp)
        {
            int rowBytes = _width * 4;
            Mf.IMFMediaBuffer buffer;
            Mf.Check(Mf.MFCreateMemoryBuffer(rowBytes * _height, out buffer));
            try
            {
                IntPtr dst;
                int max, cur;
                buffer.Lock(out dst, out max, out cur);
                try
                {
                    for (int y = 0; y < _height; y++)
                    {
                        IntPtr srcRow = topDownScan0 + (_height - 1 - y) * srcStride;
                        Mf.CopyMemory(dst + y * rowBytes, srcRow, (uint)rowBytes);
                    }
                }
                finally
                {
                    buffer.Unlock();
                }
                buffer.SetCurrentLength(rowBytes * _height);
                WriteSample(_videoStream, buffer, timestamp, _frameDuration);
            }
            finally
            {
                Mf.Release(buffer);
            }
        }

        /// <summary>16-bit PCM in the rate/channels given to the constructor.</summary>
        public void WriteAudio(byte[] pcm, int bytes, long timestamp)
        {
            if (_audioStream < 0 || bytes <= 0) return;

            Mf.IMFMediaBuffer buffer;
            Mf.Check(Mf.MFCreateMemoryBuffer(bytes, out buffer));
            try
            {
                IntPtr dst;
                int max, cur;
                buffer.Lock(out dst, out max, out cur);
                try
                {
                    Marshal.Copy(pcm, 0, dst, bytes);
                }
                finally
                {
                    buffer.Unlock();
                }
                buffer.SetCurrentLength(bytes);
                long duration = (long)(bytes / _audioBlockAlign) * 10_000_000L / _audioRate;
                WriteSample(_audioStream, buffer, timestamp, duration);
            }
            finally
            {
                Mf.Release(buffer);
            }
        }

        private void WriteSample(int stream, Mf.IMFMediaBuffer buffer, long timestamp, long duration)
        {
            Mf.IMFSample sample;
            Mf.Check(Mf.MFCreateSample(out sample));
            try
            {
                sample.AddBuffer(buffer);
                sample.SetSampleTime(timestamp);
                sample.SetSampleDuration(duration);
                _writer.WriteSample(stream, sample);
            }
            finally
            {
                Mf.Release(sample);
            }
        }

        /// <summary>Flushes the encoders and writes the MP4 index. Must run, or the file will not play.</summary>
        public void Finish()
        {
            if (_finished || _writer == null) return;
            _finished = true;
            _writer.Finalize_();
        }

        public void Dispose()
        {
            try
            {
                Finish();
            }
            catch (Exception)
            {
                // a recording that cannot be finalised is already lost; don't throw from Dispose
            }
            Mf.Release(_writer);
            _writer = null;
        }
    }

    /// <summary>The slice of Media Foundation the MP4 writer needs.</summary>
    internal static class Mf
    {
        private static bool _started;

        public static void Startup()
        {
            if (_started) return;
            Check(MFStartup(0x00020070, 0));   // MF_VERSION for Windows 7 and later
            _started = true;                   // stays up for the life of the tray process
        }

        public static void Check(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        public static void Release(object com)
        {
            if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }

        public static IMFMediaType CreateMediaType()
        {
            IMFMediaType type;
            Check(MFCreateMediaType(out type));
            return type;
        }

        public static void SetGuid(IMFMediaType type, Guid key, Guid value) { type.SetGUID(ref key, ref value); }
        public static void SetU32(IMFMediaType type, Guid key, int value) { type.SetUINT32(ref key, value); }
        public static void SetU64(IMFMediaType type, Guid key, long value) { type.SetUINT64(ref key, value); }
        public static void SetU32(IMFAttributes attrs, Guid key, int value) { attrs.SetUINT32(ref key, value); }

        // media types
        public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFAudioFormat_AAC = new Guid("00001610-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");

        // attributes
        public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
        public static readonly Guid MF_MT_SUBTYPE = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
        public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
        public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
        public static readonly Guid MF_MT_MPEG2_PROFILE = new Guid("AD76A80B-2D5C-4E0B-B375-64E520137036");
        public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652C33D-D6B2-4012-B834-72030849A37D");
        public static readonly Guid MF_MT_FRAME_RATE = new Guid("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
        public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
        public static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669");
        public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
        public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
        public static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1AAB75C8-CFEF-451C-AB95-AC034B8E1731");
        public static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322DE230-9EEB-43BD-AB7A-FF412251541D");
        public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("A634A91C-822B-41B9-A494-4DE4643612B0");
        public static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new Guid("08B845D8-2B74-4AFE-9D53-BE16D2D5AE4F");

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFStartup(int version, int flags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMediaType(out IMFMediaType type);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateSample(out IMFSample sample);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        public static extern int MFCreateSinkWriterFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string outputUrl, IntPtr byteStream,
            IMFAttributes attributes, out IMFSinkWriter writer);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", ExactSpelling = true)]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        // COM interfaces: every inherited method is re-declared because .NET builds the
        // vtable from the declared list; the order below must match mfobjects.h exactly.

        [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFAttributes
        {
            void GetItem([In] ref Guid key, IntPtr value);
            void GetItemType([In] ref Guid key, out int type);
            void CompareItem([In] ref Guid key, IntPtr value, out int result);
            void Compare(IMFAttributes theirs, int matchType, out int result);
            void GetUINT32([In] ref Guid key, out int value);
            void GetUINT64([In] ref Guid key, out long value);
            void GetDouble([In] ref Guid key, out double value);
            void GetGUID([In] ref Guid key, out Guid value);
            void GetStringLength([In] ref Guid key, out int length);
            void GetString([In] ref Guid key, IntPtr value, int size, out int length);
            void GetAllocatedString([In] ref Guid key, out IntPtr value, out int length);
            void GetBlobSize([In] ref Guid key, out int size);
            void GetBlob([In] ref Guid key, [Out] byte[] buffer, int size, out int written);
            void GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out int size);
            void GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr unknown);
            void SetItem([In] ref Guid key, IntPtr value);
            void DeleteItem([In] ref Guid key);
            void DeleteAllItems();
            void SetUINT32([In] ref Guid key, int value);
            void SetUINT64([In] ref Guid key, long value);
            void SetDouble([In] ref Guid key, double value);
            void SetGUID([In] ref Guid key, [In] ref Guid value);
            void SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            void SetBlob([In] ref Guid key, [In] byte[] buffer, int size);
            void SetUnknown([In] ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
            void LockStore();
            void UnlockStore();
            void GetCount(out int count);
            void GetItemByIndex(int index, out Guid key, IntPtr value);
            void CopyAllItems(IMFAttributes destination);
        }

        [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFMediaType
        {
            // IMFAttributes
            void GetItem([In] ref Guid key, IntPtr value);
            void GetItemType([In] ref Guid key, out int type);
            void CompareItem([In] ref Guid key, IntPtr value, out int result);
            void Compare(IMFAttributes theirs, int matchType, out int result);
            void GetUINT32([In] ref Guid key, out int value);
            void GetUINT64([In] ref Guid key, out long value);
            void GetDouble([In] ref Guid key, out double value);
            void GetGUID([In] ref Guid key, out Guid value);
            void GetStringLength([In] ref Guid key, out int length);
            void GetString([In] ref Guid key, IntPtr value, int size, out int length);
            void GetAllocatedString([In] ref Guid key, out IntPtr value, out int length);
            void GetBlobSize([In] ref Guid key, out int size);
            void GetBlob([In] ref Guid key, [Out] byte[] buffer, int size, out int written);
            void GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out int size);
            void GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr unknown);
            void SetItem([In] ref Guid key, IntPtr value);
            void DeleteItem([In] ref Guid key);
            void DeleteAllItems();
            void SetUINT32([In] ref Guid key, int value);
            void SetUINT64([In] ref Guid key, long value);
            void SetDouble([In] ref Guid key, double value);
            void SetGUID([In] ref Guid key, [In] ref Guid value);
            void SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            void SetBlob([In] ref Guid key, [In] byte[] buffer, int size);
            void SetUnknown([In] ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
            void LockStore();
            void UnlockStore();
            void GetCount(out int count);
            void GetItemByIndex(int index, out Guid key, IntPtr value);
            void CopyAllItems(IMFAttributes destination);
            // IMFMediaType
            void GetMajorType(out Guid majorType);
            void IsCompressedFormat(out int compressed);
            void IsEqual(IMFMediaType other, out int flags);
            void GetRepresentation(Guid representation, out IntPtr data);
            void FreeRepresentation(Guid representation, IntPtr data);
        }

        [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFSample
        {
            // IMFAttributes
            void GetItem([In] ref Guid key, IntPtr value);
            void GetItemType([In] ref Guid key, out int type);
            void CompareItem([In] ref Guid key, IntPtr value, out int result);
            void Compare(IMFAttributes theirs, int matchType, out int result);
            void GetUINT32([In] ref Guid key, out int value);
            void GetUINT64([In] ref Guid key, out long value);
            void GetDouble([In] ref Guid key, out double value);
            void GetGUID([In] ref Guid key, out Guid value);
            void GetStringLength([In] ref Guid key, out int length);
            void GetString([In] ref Guid key, IntPtr value, int size, out int length);
            void GetAllocatedString([In] ref Guid key, out IntPtr value, out int length);
            void GetBlobSize([In] ref Guid key, out int size);
            void GetBlob([In] ref Guid key, [Out] byte[] buffer, int size, out int written);
            void GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out int size);
            void GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr unknown);
            void SetItem([In] ref Guid key, IntPtr value);
            void DeleteItem([In] ref Guid key);
            void DeleteAllItems();
            void SetUINT32([In] ref Guid key, int value);
            void SetUINT64([In] ref Guid key, long value);
            void SetDouble([In] ref Guid key, double value);
            void SetGUID([In] ref Guid key, [In] ref Guid value);
            void SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            void SetBlob([In] ref Guid key, [In] byte[] buffer, int size);
            void SetUnknown([In] ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object unknown);
            void LockStore();
            void UnlockStore();
            void GetCount(out int count);
            void GetItemByIndex(int index, out Guid key, IntPtr value);
            void CopyAllItems(IMFAttributes destination);
            // IMFSample
            void GetSampleFlags(out int flags);
            void SetSampleFlags(int flags);
            void GetSampleTime(out long time);
            void SetSampleTime(long time);
            void GetSampleDuration(out long duration);
            void SetSampleDuration(long duration);
            void GetBufferCount(out int count);
            void GetBufferByIndex(int index, out IMFMediaBuffer buffer);
            void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            void AddBuffer(IMFMediaBuffer buffer);
            void RemoveBufferByIndex(int index);
            void RemoveAllBuffers();
            void GetTotalLength(out int length);
            void CopyToBuffer(IMFMediaBuffer buffer);
        }

        [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFMediaBuffer
        {
            void Lock(out IntPtr data, out int maxLength, out int currentLength);
            void Unlock();
            void GetCurrentLength(out int length);
            void SetCurrentLength(int length);
            void GetMaxLength(out int length);
        }

        [ComImport, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFSinkWriter
        {
            void AddStream(IMFMediaType targetMediaType, out int streamIndex);
            void SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IMFAttributes parameters);
            void BeginWriting();
            void WriteSample(int streamIndex, IMFSample sample);
            void SendStreamTick(int streamIndex, long timestamp);
            void PlaceMarker(int streamIndex, IntPtr context);
            void NotifyEndOfSegment(int streamIndex);
            void Flush(int streamIndex);
            void Finalize_();
            void GetServiceForStream(int streamIndex, [In] ref Guid service, [In] ref Guid iid, out IntPtr obj);
            void GetStatistics(int streamIndex, IntPtr statistics);
        }
    }
}
