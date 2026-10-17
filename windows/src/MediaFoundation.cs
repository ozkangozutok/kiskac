using System;
using System.Runtime.InteropServices;

namespace Kiskac
{
    // Windows'la gelen Media Foundation için elle yazılmış COM tanımları. Ek kitaplık gerekmez.
    // Arayüzlerde yöntem SIRASI sanal tablo düzenini belirler; mfobjects.h / mfreadwrite.h ile birebir aynı olmalı.
    // Kullanılmayan yöntemler parametresiz yer tutucudur: hiç çağrılmazlar, yalnızca sıradaki yeri doldururlar.

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        void GetItem();
        void GetItemType();
        void CompareItem();
        void Compare();
        void GetUINT32([In] ref Guid key, out int value);
        void GetUINT64([In] ref Guid key, out long value);
        void GetDouble();
        void GetGUID([In] ref Guid key, out Guid value);
        void GetStringLength();
        void GetString();
        void GetAllocatedString();
        void GetBlobSize();
        void GetBlob();
        void GetAllocatedBlob();
        void GetUnknown();
        void SetItem();
        void DeleteItem();
        void DeleteAllItems();
        void SetUINT32([In] ref Guid key, int value);
        void SetUINT64([In] ref Guid key, long value);
        void SetDouble();
        void SetGUID([In] ref Guid key, [In] ref Guid value);
        void SetString();
        void SetBlob();
        void SetUnknown();
        void LockStore();
        void UnlockStore();
        void GetCount();
        void GetItemByIndex();
        void CopyAllItems();
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        // IMFAttributes (30)
        void GetItem();
        void GetItemType();
        void CompareItem();
        void Compare();
        void GetUINT32();
        void GetUINT64();
        void GetDouble();
        void GetGUID();
        void GetStringLength();
        void GetString();
        void GetAllocatedString();
        void GetBlobSize();
        void GetBlob();
        void GetAllocatedBlob();
        void GetUnknown();
        void SetItem();
        void DeleteItem();
        void DeleteAllItems();
        void SetUINT32();
        void SetUINT64();
        void SetDouble();
        void SetGUID();
        void SetString();
        void SetBlob();
        void SetUnknown();
        void LockStore();
        void UnlockStore();
        void GetCount();
        void GetItemByIndex();
        void CopyAllItems();
        // IMFSample
        void GetSampleFlags();
        void SetSampleFlags();
        void GetSampleTime(out long time);
        void SetSampleTime(long time);
        void GetSampleDuration(out long duration);
        void SetSampleDuration(long duration);
        void GetBufferCount(out int count);
        void GetBufferByIndex(int index, out IMFMediaBuffer buffer);
        void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        void AddBuffer(IMFMediaBuffer buffer);
        void RemoveBufferByIndex();
        void RemoveAllBuffers();
        void GetTotalLength();
        void CopyToBuffer();
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        void Unlock();
        void GetCurrentLength(out int length);
        void SetCurrentLength(int length);
        void GetMaxLength(out int length);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSinkWriter
    {
        void AddStream(IntPtr targetMediaType, out int streamIndex);
        void SetInputMediaType(int streamIndex, IntPtr inputMediaType, IMFAttributes encodingParameters);
        void BeginWriting();
        void WriteSample(int streamIndex, IMFSample sample);
        void SendStreamTick(int streamIndex, long timestamp);
        void PlaceMarker();
        void NotifyEndOfSegment();
        void Flush(int streamIndex);
        void FinalizeWriting(); // IMFSinkWriter::Finalize
        void GetServiceForStream();
        void GetStatistics();
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        void GetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] out bool selected);
        void SetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);
        void GetNativeMediaType(int streamIndex, int typeIndex, out IMFAttributes mediaType);
        void GetCurrentMediaType(int streamIndex, out IMFAttributes mediaType);
        void SetCurrentMediaType(int streamIndex, IntPtr reserved, IntPtr mediaType);
        void SetCurrentPosition();
        void ReadSample(int streamIndex, int controlFlags, out int actualStreamIndex, out int streamFlags, out long timestamp, out IMFSample sample);
        void Flush();
        void GetServiceForStream();
        void GetPresentationAttribute(int streamIndex, [In] ref Guid key, IntPtr propVariant);
    }

    static class MF
    {
        // ---------------------------------------------------------------- GUID'ler
        public static readonly Guid IID_IMFMediaType = new Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555");

        public static Guid MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static Guid MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static Guid MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static Guid MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static Guid MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static Guid MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static Guid MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static Guid MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static Guid MT_MPEG2_PROFILE = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static Guid MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        public static Guid MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        public static Guid MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        public static Guid MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        public static Guid MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");

        public static Guid MediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static Guid MediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        public static Guid VideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static Guid VideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        public static Guid AudioFormat_AAC = new Guid("00001610-0000-0010-8000-00AA00389B71");
        public static Guid AudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");

        public static Guid READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
        public static Guid SOURCE_READER_ENABLE_VIDEO_PROCESSING = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
        public static Guid PD_DURATION = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

        public const int SOURCE_READER_FIRST_VIDEO_STREAM = unchecked((int)0xFFFFFFFC);
        public const int SOURCE_READER_FIRST_AUDIO_STREAM = unchecked((int)0xFFFFFFFD);
        public const int SOURCE_READER_MEDIASOURCE = unchecked((int)0xFFFFFFFF);
        public const int SOURCE_READERF_ENDOFSTREAM = 0x2;

        // ---------------------------------------------------------------- işlevler
        const int MF_VERSION = 0x00020070;

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void MFStartup(int version, int flags);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateMediaType(out IMFAttributes mediaType);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateAttributes(out IMFAttributes attributes, int initialSize);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateSample(out IMFSample sample);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

        [DllImport("mfreadwrite.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url,
            IntPtr byteStream, IMFAttributes attributes, out IMFSinkWriter sinkWriter);

        [DllImport("mfreadwrite.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url,
            IMFAttributes attributes, out IMFSourceReader sourceReader);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, UIntPtr count);

        static readonly object startLock = new object();
        static int startCount;

        // MFStartup/MFShutdown sayaçlı çağrılır. Media Foundation yoksa (Windows N sürümleri) istisna fırlatır.
        public static void Startup()
        {
            lock (startLock)
            {
                if (startCount == 0) MFStartup(MF_VERSION, 0);
                startCount++;
            }
        }

        public static void Shutdown()
        {
            lock (startLock)
            {
                if (startCount == 0) return;
                if (--startCount == 0) MFShutdown();
            }
        }

        public static long Pack(int high, int low)
        {
            return ((long)high << 32) | (uint)low;
        }

        // IMFSinkWriter ve IMFSourceReader IMFMediaType* bekler; IMFAttributes RCW'sinden asıl arayüzü alır.
        public static IntPtr MediaTypePtr(IMFAttributes type)
        {
            IntPtr unk = Marshal.GetIUnknownForObject(type);
            try
            {
                Guid iid = IID_IMFMediaType;
                IntPtr p;
                int hr = Marshal.QueryInterface(unk, ref iid, out p);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                return p;
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        public static void Release(object com)
        {
            if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }
    }

    // H.264 video (+ isteğe bağlı AAC ses) ile MP4 yazar. Video BGRA (üstten alta) kareler alır, ses 48 kHz stereo 16 bit PCM.
    // Yazma çağrıları kilitlidir: video ve ses farklı iş parçacıklarından gelebilir (ikisi de MTA).
    sealed class Mp4Writer : IDisposable
    {
        public const int AudioRate = 48000;
        public const int AudioChannels = 2;

        readonly object gate = new object();
        readonly int width, height;
        IMFSinkWriter writer;
        int videoStream = -1, audioStream = -1;
        bool started;

        public int Width { get { return width; } }
        public int Height { get { return height; } }
        public bool HasAudio { get { return audioStream >= 0; } }

        public Mp4Writer(string path, int width, int height, int fps, bool audio)
        {
            if (width % 2 != 0 || height % 2 != 0) throw new ArgumentException("H.264 için boyutlar çift olmalı.");
            this.width = width;
            this.height = height;

            MF.Startup();
            started = true;
            try
            {
                IMFAttributes attrs;
                MF.MFCreateAttributes(out attrs, 1);
                attrs.SetUINT32(ref MF.READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                MF.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attrs, out writer);
                MF.Release(attrs);

                long bitrate = Math.Max(2000000L, Math.Min(40000000L, (long)width * height * fps / 6));
                videoStream = AddStream(Video(MF.VideoFormat_H264, fps, (int)bitrate, true),
                                        Video(MF.VideoFormat_RGB32, fps, 0, false));
                if (audio)
                    audioStream = AddStream(Audio(MF.AudioFormat_AAC), Audio(MF.AudioFormat_PCM));

                writer.BeginWriting();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        IMFAttributes Video(Guid subtype, int fps, int bitrate, bool output)
        {
            IMFAttributes t;
            MF.MFCreateMediaType(out t);
            t.SetGUID(ref MF.MT_MAJOR_TYPE, ref MF.MediaType_Video);
            t.SetGUID(ref MF.MT_SUBTYPE, ref subtype);
            t.SetUINT32(ref MF.MT_INTERLACE_MODE, 2); // ilerlemeli
            t.SetUINT64(ref MF.MT_FRAME_SIZE, MF.Pack(width, height));
            t.SetUINT64(ref MF.MT_FRAME_RATE, MF.Pack(fps, 1));
            t.SetUINT64(ref MF.MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
            if (output)
            {
                t.SetUINT32(ref MF.MT_AVG_BITRATE, bitrate);
                t.SetUINT32(ref MF.MT_MPEG2_PROFILE, 100); // H.264 High
            }
            else
                t.SetUINT32(ref MF.MT_DEFAULT_STRIDE, width * 4); // pozitif adım: satırlar üstten alta
            return t;
        }

        static IMFAttributes Audio(Guid subtype)
        {
            IMFAttributes t;
            MF.MFCreateMediaType(out t);
            t.SetGUID(ref MF.MT_MAJOR_TYPE, ref MF.MediaType_Audio);
            t.SetGUID(ref MF.MT_SUBTYPE, ref subtype);
            t.SetUINT32(ref MF.MT_AUDIO_BITS_PER_SAMPLE, 16);
            t.SetUINT32(ref MF.MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
            t.SetUINT32(ref MF.MT_AUDIO_NUM_CHANNELS, AudioChannels);
            if (subtype == MF.AudioFormat_AAC)
                t.SetUINT32(ref MF.MT_AUDIO_AVG_BYTES_PER_SECOND, 24000); // 192 kbit/s
            else
            {
                t.SetUINT32(ref MF.MT_AUDIO_BLOCK_ALIGNMENT, AudioChannels * 2);
                t.SetUINT32(ref MF.MT_AUDIO_AVG_BYTES_PER_SECOND, AudioRate * AudioChannels * 2);
            }
            return t;
        }

        int AddStream(IMFAttributes output, IMFAttributes input)
        {
            IntPtr outPtr = IntPtr.Zero, inPtr = IntPtr.Zero;
            try
            {
                int index;
                outPtr = MF.MediaTypePtr(output);
                writer.AddStream(outPtr, out index);
                inPtr = MF.MediaTypePtr(input);
                writer.SetInputMediaType(index, inPtr, null);
                return index;
            }
            finally
            {
                if (outPtr != IntPtr.Zero) Marshal.Release(outPtr);
                if (inPtr != IntPtr.Zero) Marshal.Release(inPtr);
                MF.Release(output);
                MF.Release(input);
            }
        }

        // pixels: width*height*4 bayt BGRA, üstten alta. Zamanlar 100 ns biriminde.
        public void WriteVideo(IntPtr pixels, long time, long duration)
        {
            WriteSample(videoStream, pixels, width * height * 4, time, duration);
        }

        public void WriteAudio(IntPtr pcm, int bytes, long time, long duration)
        {
            if (audioStream < 0) return;
            WriteSample(audioStream, pcm, bytes, time, duration);
        }

        void WriteSample(int stream, IntPtr data, int bytes, long time, long duration)
        {
            lock (gate)
            {
                if (writer == null) return;
                IMFMediaBuffer buffer = null;
                IMFSample sample = null;
                try
                {
                    MF.MFCreateMemoryBuffer(bytes, out buffer);
                    IntPtr dst;
                    int max, cur;
                    buffer.Lock(out dst, out max, out cur);
                    MF.CopyMemory(dst, data, (UIntPtr)(uint)bytes);
                    buffer.Unlock();
                    buffer.SetCurrentLength(bytes);

                    MF.MFCreateSample(out sample);
                    sample.AddBuffer(buffer);
                    sample.SetSampleTime(time);
                    sample.SetSampleDuration(duration);
                    writer.WriteSample(stream, sample);
                }
                finally
                {
                    // Büyük yerel tamponlar GC'yi beklemeden bırakılsın
                    MF.Release(sample);
                    MF.Release(buffer);
                }
            }
        }

        // Dosyayı oynatılabilir hale getirir. Çağrılmazsa MP4 bozuk kalır.
        public void Finish()
        {
            lock (gate)
            {
                if (writer == null) return;
                try { writer.FinalizeWriting(); }
                finally { ReleaseWriter(); }
            }
        }

        void ReleaseWriter()
        {
            MF.Release(writer);
            writer = null;
        }

        public void Dispose()
        {
            lock (gate)
            {
                ReleaseWriter();
                if (started)
                {
                    started = false;
                    MF.Shutdown();
                }
            }
        }
    }

    // Test ve doğrulama için: bir MP4'ün süresini, boyutunu ve ilk karesini okur.
    static class Mp4Probe
    {
        public sealed class Info
        {
            public long Duration; // 100 ns
            public int Width, Height;
            public bool HasAudio;
            public int[] FirstFrame; // BGRA, üstten alta, Width*Height
        }

        public static Info Read(string path)
        {
            MF.Startup();
            IMFSourceReader reader = null;
            try
            {
                IMFAttributes attrs;
                MF.MFCreateAttributes(out attrs, 1);
                attrs.SetUINT32(ref MF.SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
                MF.MFCreateSourceReaderFromURL(path, attrs, out reader);
                MF.Release(attrs);

                var info = new Info();
                IntPtr pv = Marshal.AllocCoTaskMem(32);
                try
                {
                    for (int i = 0; i < 32; i++) Marshal.WriteByte(pv, i, 0);
                    reader.GetPresentationAttribute(MF.SOURCE_READER_MEDIASOURCE, ref MF.PD_DURATION, pv);
                    info.Duration = Marshal.ReadInt64(pv, 8);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pv);
                }

                try
                {
                    IMFAttributes a;
                    reader.GetNativeMediaType(MF.SOURCE_READER_FIRST_AUDIO_STREAM, 0, out a);
                    MF.Release(a);
                    info.HasAudio = true;
                }
                catch (COMException) { }

                IMFAttributes rgb;
                MF.MFCreateMediaType(out rgb);
                rgb.SetGUID(ref MF.MT_MAJOR_TYPE, ref MF.MediaType_Video);
                rgb.SetGUID(ref MF.MT_SUBTYPE, ref MF.VideoFormat_RGB32);
                IntPtr rgbPtr = MF.MediaTypePtr(rgb);
                try { reader.SetCurrentMediaType(MF.SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, rgbPtr); }
                finally
                {
                    Marshal.Release(rgbPtr);
                    MF.Release(rgb);
                }

                IMFAttributes cur;
                reader.GetCurrentMediaType(MF.SOURCE_READER_FIRST_VIDEO_STREAM, out cur);
                long size;
                cur.GetUINT64(ref MF.MT_FRAME_SIZE, out size);
                info.Width = (int)(size >> 32);
                info.Height = (int)(size & 0xFFFFFFFF);
                int stride = info.Width * 4;
                try { cur.GetUINT32(ref MF.MT_DEFAULT_STRIDE, out stride); }
                catch (COMException) { }
                MF.Release(cur);

                for (int attempt = 0; attempt < 10 && info.FirstFrame == null; attempt++)
                {
                    int actual, flags;
                    long ts;
                    IMFSample sample;
                    reader.ReadSample(MF.SOURCE_READER_FIRST_VIDEO_STREAM, 0, out actual, out flags, out ts, out sample);
                    if ((flags & MF.SOURCE_READERF_ENDOFSTREAM) != 0) break;
                    if (sample == null) continue;
                    IMFMediaBuffer buf;
                    sample.ConvertToContiguousBuffer(out buf);
                    IntPtr p;
                    int max, len;
                    buf.Lock(out p, out max, out len);
                    try
                    {
                        var px = new int[info.Width * info.Height];
                        int absStride = Math.Abs(stride);
                        for (int y = 0; y < info.Height; y++)
                        {
                            // negatif adım: bellekte alttan üste
                            int srcRow = stride > 0 ? y : info.Height - 1 - y;
                            Marshal.Copy(p + srcRow * absStride, px, y * info.Width, info.Width);
                        }
                        info.FirstFrame = px;
                    }
                    finally
                    {
                        buf.Unlock();
                        MF.Release(buf);
                        MF.Release(sample);
                    }
                }
                return info;
            }
            finally
            {
                MF.Release(reader);
                MF.Shutdown();
            }
        }
    }
}
