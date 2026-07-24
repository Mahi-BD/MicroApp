using System;
using System.Drawing;
using System.IO;

namespace MicroApp
{
    /// <summary>
    /// Streams an animated GIF straight to disk, one frame at a time.
    ///
    /// GDI+ can only encode single-image GIFs, so each frame is encoded on its own and
    /// this class splices the pieces into one GIF89a: the first frame supplies the
    /// screen descriptor, every frame contributes a Graphic Control Extension (the
    /// delay) plus its own image block with its palette carried over as a local colour
    /// table. Writing incrementally keeps memory flat -- a ten second recording never
    /// holds more than one frame in RAM.
    /// </summary>
    public class GifWriter : IDisposable
    {
        private readonly FileStream _file;
        private readonly int _delayHundredths;
        private bool _wroteHeader;

        public int FrameCount { get; private set; }
        public string Path { get; private set; }

        /// <param name="delayMs">Delay between frames; GIF stores this in 1/100 s.</param>
        public GifWriter(string path, int delayMs)
        {
            Path = path;
            _delayHundredths = Math.Max(2, (int)Math.Round(delayMs / 10.0)); // browsers clamp < 20ms anyway
            _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024);
        }

        public void AddFrame(Image frame)
        {
            byte[] src;
            using (var buffer = new MemoryStream())
            {
                frame.Save(buffer, System.Drawing.Imaging.ImageFormat.Gif);
                src = buffer.ToArray();
            }

            // header(6) + logical screen descriptor(7)
            int packed = src[10];
            int colorTableSize = (packed & 0x80) != 0 ? 3 * (1 << ((packed & 0x07) + 1)) : 0;
            int pos = 13;
            int colorTableAt = pos;
            pos += colorTableSize;

            int imageAt = FindImageDescriptor(src, pos);
            if (imageAt < 0) return;   // encoder gave us something unexpected: skip the frame

            if (!_wroteHeader)
            {
                _file.Write(src, 0, 13);                                   // GIF89a + screen descriptor
                if (colorTableSize > 0) _file.Write(src, colorTableAt, colorTableSize);
                WriteLoopingExtension();
                _wroteHeader = true;
            }

            WriteGraphicControlExtension();

            // image descriptor, with this frame's palette attached as a local colour table
            var descriptor = new byte[10];
            Array.Copy(src, imageAt, descriptor, 0, 10);
            if (colorTableSize > 0)
            {
                descriptor[9] = (byte)((descriptor[9] & 0xF8) | 0x80 | (packed & 0x07));
            }
            _file.Write(descriptor, 0, 10);
            if (colorTableSize > 0) _file.Write(src, colorTableAt, colorTableSize);

            // LZW minimum code size + data sub-blocks, up to and including the terminator
            int data = imageAt + 10;
            _file.WriteByte(src[data++]);
            while (data < src.Length)
            {
                int length = src[data];
                _file.WriteByte(src[data++]);
                if (length == 0) break;
                _file.Write(src, data, length);
                data += length;
            }

            FrameCount++;
        }

        /// <summary>Walks past any extension blocks to the first image descriptor (0x2C).</summary>
        private static int FindImageDescriptor(byte[] src, int pos)
        {
            while (pos < src.Length)
            {
                byte block = src[pos];
                if (block == 0x2C) return pos;
                if (block == 0x3B) return -1;          // trailer
                if (block == 0x21)
                {
                    pos += 2;                          // marker + label
                    while (pos < src.Length && src[pos] != 0)
                    {
                        pos += src[pos] + 1;           // sub-block
                    }
                    pos++;                             // block terminator
                    continue;
                }
                return -1;
            }
            return -1;
        }

        /// <summary>NETSCAPE2.0 application extension: loop forever.</summary>
        private void WriteLoopingExtension()
        {
            byte[] netscape =
            {
                0x21, 0xFF, 0x0B,
                (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
                (byte)'2', (byte)'.', (byte)'0',
                0x03, 0x01, 0x00, 0x00,   // loop count 0 = forever
                0x00
            };
            _file.Write(netscape, 0, netscape.Length);
        }

        private void WriteGraphicControlExtension()
        {
            byte[] gce =
            {
                0x21, 0xF9, 0x04,
                0x04,                                  // disposal: leave the frame in place
                (byte)(_delayHundredths & 0xFF),
                (byte)((_delayHundredths >> 8) & 0xFF),
                0x00,                                  // no transparent index
                0x00
            };
            _file.Write(gce, 0, gce.Length);
        }

        public void Dispose()
        {
            if (_file == null) return;
            if (_wroteHeader)
            {
                _file.WriteByte(0x3B);                 // trailer
            }
            _file.Flush();
            _file.Dispose();
        }
    }
}
