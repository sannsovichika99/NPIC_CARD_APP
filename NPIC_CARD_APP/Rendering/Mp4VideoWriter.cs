using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Writes frames to an H.264 .mp4 file with Windows Media Foundation, which is part of Windows (no extra library).
    /// Frames go in as top-down 32-bit RGB bitmaps; the sink writer converts them to YUV and encodes them.
    /// Windows N / KN editions only have Media Foundation after the Media Feature Pack is installed.
    /// </summary>
    public sealed class Mp4VideoWriter : IDisposable
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int FramesPerSecond { get; private set; }
        public int FrameCount { get { return (int)_frame; } }

        private IMFSinkWriter _writer;
        private int _stream;
        private long _frame;
        private bool _mfStarted, _finished;

        /// <param name="bitsPerSecond">average H.264 bit rate, e.g. 12 000 000 for sharp 1080p</param>
        public Mp4VideoWriter(string path, int width, int height, int framesPerSecond, int bitsPerSecond)
        {
            if (width < 16 || height < 16 || (width & 1) != 0 || (height & 1) != 0)
                throw new ArgumentException("The video size must be even and at least 16 × 16 pixels.");
            if (framesPerSecond < 1 || framesPerSecond > 120) throw new ArgumentOutOfRangeException("framesPerSecond");

            Width = width;
            Height = height;
            FramesPerSecond = framesPerSecond;

            Check(MFStartup(MF_VERSION, MFSTARTUP_LITE));
            _mfStarted = true;
            try
            {
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out _writer));

                IMFMediaType output = NewVideoType(MFVideoFormat_H264);
                try
                {
                    output.SetUINT32(MF_MT_AVG_BITRATE, bitsPerSecond);
                    output.SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_High);
                    _writer.AddStream(output, out _stream);
                }
                finally { Marshal.ReleaseComObject(output); }

                IMFMediaType input = NewVideoType(MFVideoFormat_RGB32);
                try
                {
                    input.SetUINT32(MF_MT_DEFAULT_STRIDE, width * 4);   // positive stride: rows run top-down
                    _writer.SetInputMediaType(_stream, input, IntPtr.Zero);
                }
                finally { Marshal.ReleaseComObject(input); }

                _writer.BeginWriting();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private IMFMediaType NewVideoType(Guid subtype)
        {
            IMFMediaType type;
            Check(MFCreateMediaType(out type));
            type.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            type.SetGUID(MF_MT_SUBTYPE, subtype);
            type.SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            type.SetUINT64(MF_MT_FRAME_SIZE, Pack(Width, Height));
            type.SetUINT64(MF_MT_FRAME_RATE, Pack(FramesPerSecond, 1));
            type.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1));
            return type;
        }

        /// <summary>Appends one frame. The bitmap must be <see cref="Width"/> × <see cref="Height"/>; its alpha is ignored.</summary>
        public void WriteFrame(Bitmap frame)
        {
            if (_writer == null || _finished) throw new ObjectDisposedException("Mp4VideoWriter");
            if (frame == null) throw new ArgumentNullException("frame");
            if (frame.Width != Width || frame.Height != Height)
                throw new ArgumentException("The frame must be " + Width + " × " + Height + " pixels.");

            int rowBytes = Width * 4, size = rowBytes * Height;
            IMFMediaBuffer buffer = null;
            IMFSample sample = null;
            try
            {
                Check(MFCreateMemoryBuffer(size, out buffer));
                IntPtr dest;
                int max, current;
                buffer.Lock(out dest, out max, out current);
                try
                {
                    BitmapData data = frame.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    try
                    {
                        Check(MFCopyImage(dest, rowBytes, data.Scan0, data.Stride, rowBytes, Height));
                    }
                    finally { frame.UnlockBits(data); }
                }
                finally { buffer.Unlock(); }
                buffer.SetCurrentLength(size);

                // times in 100 ns units, computed from the frame number so they never drift
                long start = _frame * 10000000L / FramesPerSecond;
                long end = (_frame + 1) * 10000000L / FramesPerSecond;
                Check(MFCreateSample(out sample));
                sample.AddBuffer(buffer);
                sample.SetSampleTime(start);
                sample.SetSampleDuration(end - start);
                _writer.WriteSample(_stream, sample);
                _frame++;
            }
            finally
            {
                if (sample != null) Marshal.ReleaseComObject(sample);
                if (buffer != null) Marshal.ReleaseComObject(buffer);
            }
        }

        /// <summary>Flushes the encoder and completes the file. Without this the file is incomplete.</summary>
        public void Finish()
        {
            if (_writer == null || _finished) return;
            _writer.FinalizeWriting();
            _finished = true;
            Dispose();
        }

        public void Dispose()
        {
            if (_writer != null)
            {
                Marshal.ReleaseComObject(_writer);   // closes the file
                _writer = null;
            }
            if (_mfStarted)
            {
                _mfStarted = false;
                MFShutdown();
            }
        }

        private static long Pack(int high, int low)
        {
            return ((long)high << 32) | (uint)low;
        }

        private static void Check(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        // ===================== Media Foundation interop =====================
        private const int MF_VERSION = 0x00020070;
        private const int MFSTARTUP_LITE = 1;
        private const int MFVideoInterlace_Progressive = 2;
        private const int eAVEncH264VProfile_High = 100;

        private static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        private static readonly Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        private static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        private static readonly Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        private static readonly Guid MF_MT_MPEG2_PROFILE = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        private static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00aa00389b71");
        private static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00aa00389b71");
        private static readonly Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00aa00389b71");

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(int version, int flags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMediaType(out IMFMediaType mediaType);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateSample(out IMFSample sample);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCopyImage(IntPtr dest, int destStride, IntPtr src, int srcStride, int widthInBytes, int lines);

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        private static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream,
                                                            IntPtr attributes, out IMFSinkWriter writer);

        // Only the methods used here have real signatures; the others hold their place in the vtable (never called).

        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType   // : IMFAttributes
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
            void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
            void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
            void DeleteAllItems();
            void SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
            void SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
            void SetDouble();
            void SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample   // : IMFAttributes
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64();
            void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
            void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
            void DeleteAllItems(); void SetUINT32(); void SetUINT64(); void SetDouble(); void SetGUID(); void SetString();
            void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex();
            void CopyAllItems();
            // IMFSample
            void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime();
            void SetSampleTime(long time);
            void GetSampleDuration();
            void SetSampleDuration(long duration);
            void GetBufferCount(); void GetBufferByIndex(); void ConvertToContiguousBuffer();
            void AddBuffer(IMFMediaBuffer buffer);
        }

        [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
            void Unlock();
            void GetCurrentLength(out int length);
            void SetCurrentLength(int length);
        }

        [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSinkWriter
        {
            void AddStream(IMFMediaType targetType, out int streamIndex);
            void SetInputMediaType(int streamIndex, IMFMediaType inputType, IntPtr encodingParameters);
            void BeginWriting();
            void WriteSample(int streamIndex, IMFSample sample);
            void SendStreamTick(); void PlaceMarker(); void NotifyEndOfSegment(); void Flush();
            void FinalizeWriting();   // IMFSinkWriter::Finalize
        }
    }
}
