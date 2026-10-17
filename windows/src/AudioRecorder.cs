using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kiskac
{
    // Sistem sesi (WASAPI loopback) ve/veya mikrofonu yakalar, 48 kHz stereoya çevirip karıştırır ve MP4'e AAC olarak yazar.
    // Zaman çizelgesi video ile ortak RecordingClock'tan gelir: kaynaktan veri gelmeyen anlar (ör. hiç ses çalmıyorken
    // loopback paket göndermez) sessizlikle doldurulur, böylece ses kaymaz. Tüm COM nesneleri kendi MTA iş parçacığındadır.
    sealed class AudioRecorder : IDisposable
    {
        const int Rate = Mp4Writer.AudioRate;
        const long LatencyTicks = 1500000; // 150 ms: kaynakların paket gecikmesine pay
        const int MaxBacklog = Rate / 3;    // kaynakta birikebilecek en fazla ses (~330 ms); fazlası atılır

        readonly AudioMode mode;
        readonly RecordingClock clock;
        readonly Mp4Writer writer;
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly List<AudioSource> sources = new List<AudioSource>();
        Thread thread;
        string warning;
        long produced; // yazılan örnek sayısı (48 kHz)

        public AudioRecorder(AudioMode mode, RecordingClock clock, Mp4Writer writer)
        {
            this.mode = mode;
            this.clock = clock;
            this.writer = writer;
        }

        public bool Paused { get; set; }

        // Kaynakları açar; sorun varsa kullanıcıya gösterilecek uyarıyı döner (kayıt yine de sürer).
        public string Start()
        {
            thread = new Thread(Run);
            thread.SetApartmentState(ApartmentState.MTA);
            thread.IsBackground = true;
            thread.Name = "Kıskaç ses kaydı";
            thread.Start();
            ready.WaitOne(5000);
            return warning;
        }

        public void Stop()
        {
            stop.Set();
            if (thread != null) thread.Join(5000);
        }

        void Run()
        {
            var problems = new List<string>();
            if (mode == AudioMode.System || mode == AudioMode.Both) Open(true, problems);
            if (mode == AudioMode.Microphone || mode == AudioMode.Both) Open(false, problems);
            if (problems.Count > 0) warning = string.Join(" ", problems.ToArray());
            ready.Set();

            var mix = new float[Rate * 2];
            var pcm = new short[Rate * 2];
            var handle = GCHandle.Alloc(pcm, GCHandleType.Pinned);
            try
            {
                while (true)
                {
                    bool stopping = stop.WaitOne(10);
                    foreach (var s in sources) s.Drain();

                    if (!clock.Running || Paused || clock.Paused)
                    {
                        // Kayıt başlamadan / duraklatılmışken gelen ses atılır.
                        foreach (var s in sources) s.Fifo.Clear();
                        if (stopping) break;
                        continue;
                    }

                    long target = clock.Now - (stopping ? 0 : LatencyTicks);
                    long targetSamples = target * Rate / 10000000;
                    int need = (int)Math.Min(Rate, targetSamples - produced);
                    if (need > 0)
                    {
                        Array.Clear(mix, 0, need * 2);
                        foreach (var s in sources)
                        {
                            // Kaynak çok geride kaldıysa (ör. sürücü toplu teslim etti) gecikme büyümesin.
                            int excess = s.Fifo.Frames - need - MaxBacklog;
                            if (excess > 0) s.Fifo.Drop(excess);
                            s.Fifo.MixInto(mix, need);
                        }
                        for (int i = 0; i < need * 2; i++)
                        {
                            float v = mix[i];
                            if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                            pcm[i] = (short)(v * 32767f);
                        }
                        long t = produced * 10000000L / Rate;
                        long d = (produced + need) * 10000000L / Rate - t;
                        writer.WriteAudio(handle.AddrOfPinnedObject(), need * 4, t, d);
                        produced += need;
                    }
                    if (stopping) break;
                }
            }
            catch (Exception ex)
            {
                Util.Log("Ses kaydı hatası: " + ex);
            }
            finally
            {
                handle.Free();
                foreach (var s in sources) s.Dispose();
                sources.Clear();
            }
        }

        void Open(bool loopback, List<string> problems)
        {
            try
            {
                sources.Add(new AudioSource(loopback));
            }
            catch (Exception ex)
            {
                Util.Log((loopback ? "Sistem sesi" : "Mikrofon") + " açılamadı: " + ex);
                if (loopback) problems.Add("Sistem sesi kaydedilemedi.");
                else if (ex is UnauthorizedAccessException || (uint)Marshal.GetHRForException(ex) == 0x80070005)
                    problems.Add("Mikrofona erişim izni yok (Ayarlar › Gizlilik › Mikrofon).");
                else problems.Add("Mikrofon bulunamadı.");
            }
        }

        public void Dispose()
        {
            stop.Set();
        }
    }

    // Tek bir WASAPI yakalama aygıtı. Gelen veriyi 48 kHz stereo float'a çevirip Fifo'ya ekler.
    sealed class AudioSource : IDisposable
    {
        static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        static readonly Guid SubFormatFloat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        public readonly FloatFifo Fifo = new FloatFifo();
        readonly bool loopback;
        IAudioClient client;
        IAudioCaptureClient capture;
        readonly int channels, srcRate, bits, blockAlign;
        readonly bool isFloat;
        readonly Resampler resampler;
        float[] convert = new float[0];
        bool dead;

        public AudioSource(bool loopback)
        {
            this.loopback = loopback;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator));
            IMMDevice device = null;
            IntPtr format = IntPtr.Zero;
            try
            {
                // eRender=0 / eCapture=1, eConsole=0
                enumerator.GetDefaultAudioEndpoint(loopback ? 0 : 1, 0, out device);
                Guid iid = IID_IAudioClient;
                object o;
                device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o);
                client = (IAudioClient)o;

                client.GetMixFormat(out format);
                int tag = Marshal.ReadInt16(format, 0) & 0xFFFF;
                channels = Marshal.ReadInt16(format, 2);
                srcRate = Marshal.ReadInt32(format, 4);
                blockAlign = Marshal.ReadInt16(format, 12);
                bits = Marshal.ReadInt16(format, 14);
                if (tag == 0xFFFE) // WAVEFORMATEXTENSIBLE
                {
                    var sub = (Guid)Marshal.PtrToStructure(format + 24, typeof(Guid));
                    isFloat = sub == SubFormatFloat;
                }
                else isFloat = tag == 3;

                const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
                client.Initialize(0 /* paylaşımlı */, loopback ? AUDCLNT_STREAMFLAGS_LOOPBACK : 0, 10000000 /* 1 sn tampon */, 0, format, IntPtr.Zero);
                iid = IID_IAudioCaptureClient;
                client.GetService(ref iid, out o);
                capture = (IAudioCaptureClient)o;
                resampler = new Resampler(srcRate, Mp4Writer.AudioRate);
                client.Start();
                Util.Log((loopback ? "Sistem sesi" : "Mikrofon") + ": " + srcRate + " Hz, " + channels + " kanal, " + bits + " bit" + (isFloat ? " float" : ""));
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                MF.Release(device);
                MF.Release(enumerator);
            }
        }

        // Bekleyen tüm paketleri okur.
        public void Drain()
        {
            if (dead || capture == null) return;
            try
            {
                int packet;
                capture.GetNextPacketSize(out packet);
                while (packet > 0)
                {
                    IntPtr data;
                    int frames, flags;
                    long devPos, qpc;
                    capture.GetBuffer(out data, out frames, out flags, out devPos, out qpc);
                    Convert(data, frames, (flags & 0x2) != 0 /* AUDCLNT_BUFFERFLAGS_SILENT */);
                    capture.ReleaseBuffer(frames);
                    capture.GetNextPacketSize(out packet);
                }
            }
            catch (Exception ex)
            {
                // Aygıt çıkarıldı vb.: kayıt sürer, bu kaynak sessiz kalır.
                dead = true;
                Util.Log((loopback ? "Sistem sesi" : "Mikrofon") + " kesildi: " + ex.Message);
            }
        }

        void Convert(IntPtr data, int frames, bool silent)
        {
            if (frames <= 0) return;
            if (convert.Length < frames * 2) convert = new float[frames * 2];
            if (silent || data == IntPtr.Zero)
                Array.Clear(convert, 0, frames * 2);
            else
            {
                var raw = new byte[frames * blockAlign];
                Marshal.Copy(data, raw, 0, raw.Length);
                int bytesPer = bits / 8;
                for (int f = 0; f < frames; f++)
                {
                    int o = f * blockAlign;
                    float l = Sample(raw, o, bytesPer);
                    float r = channels > 1 ? Sample(raw, o + bytesPer, bytesPer) : l;
                    convert[f * 2] = l;
                    convert[f * 2 + 1] = r;
                }
            }
            resampler.Process(convert, frames, Fifo);
        }

        float Sample(byte[] b, int o, int bytesPer)
        {
            if (isFloat) return BitConverter.ToSingle(b, o);
            switch (bytesPer)
            {
                case 2: return BitConverter.ToInt16(b, o) / 32768f;
                case 3: return ((b[o] << 8 | b[o + 1] << 16 | b[o + 2] << 24) >> 8) / 8388608f;
                case 4: return BitConverter.ToInt32(b, o) / 2147483648f;
                default: return (b[o] - 128) / 128f;
            }
        }

        public void Dispose()
        {
            try { if (client != null) client.Stop(); } catch { }
            MF.Release(capture);
            MF.Release(client);
            capture = null;
            client = null;
        }
    }

    // Doğrusal yeniden örnekleyici (stereo, float). Paket sınırları arasında durumunu korur.
    sealed class Resampler
    {
        readonly double step;
        readonly bool passthrough;
        float[] buf = new float[0];
        int count; // buf'taki kare sayısı
        double pos;
        float[] outBuf = new float[0];

        public Resampler(int from, int to)
        {
            step = (double)from / to;
            passthrough = from == to;
        }

        public void Process(float[] input, int frames, FloatFifo output)
        {
            if (passthrough)
            {
                output.Append(input, frames);
                return;
            }
            if (buf.Length < (count + frames) * 2)
            {
                var nb = new float[(count + frames) * 2 * 2];
                Array.Copy(buf, nb, count * 2);
                buf = nb;
            }
            Array.Copy(input, 0, buf, count * 2, frames * 2);
            count += frames;

            int maxOut = (int)((count - pos) / step) + 2;
            if (outBuf.Length < maxOut * 2) outBuf = new float[maxOut * 2];
            int n = 0;
            while (pos + 1 < count)
            {
                int i = (int)pos;
                float f = (float)(pos - i);
                outBuf[n * 2] = buf[i * 2] + (buf[i * 2 + 2] - buf[i * 2]) * f;
                outBuf[n * 2 + 1] = buf[i * 2 + 1] + (buf[i * 2 + 3] - buf[i * 2 + 1]) * f;
                n++;
                pos += step;
            }
            output.Append(outBuf, n);

            int used = (int)pos;
            if (used > 0)
            {
                Array.Copy(buf, used * 2, buf, 0, (count - used) * 2);
                count -= used;
                pos -= used;
            }
        }
    }

    // Stereo float kuyruğu (iç içe L,R).
    sealed class FloatFifo
    {
        float[] buf = new float[48000 * 2];
        int start, frames;

        public int Frames { get { return frames; } }

        public void Append(float[] data, int n)
        {
            if ((start + frames + n) * 2 > buf.Length)
            {
                // Başa kaydır, gerekirse büyüt
                var nb = (frames + n) * 2 > buf.Length / 2 ? new float[Math.Max(buf.Length * 2, (frames + n) * 4)] : buf;
                Array.Copy(buf, start * 2, nb, 0, frames * 2);
                buf = nb;
                start = 0;
            }
            Array.Copy(data, 0, buf, (start + frames) * 2, n * 2);
            frames += n;
        }

        // En fazla n kareyi mix'e ekler (eksikse kalan kısım sessiz kalır) ve kuyruktan çıkarır.
        public void MixInto(float[] mix, int n)
        {
            int take = Math.Min(n, frames);
            int o = start * 2;
            for (int i = 0; i < take * 2; i++) mix[i] += buf[o + i];
            Drop(take);
        }

        public void Drop(int n)
        {
            n = Math.Min(n, frames);
            start += n;
            frames -= n;
            if (frames == 0) start = 0;
        }

        public void Clear()
        {
            start = 0;
            frames = 0;
        }
    }

    // ---------------------------------------------------------------- WASAPI COM tanımları

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints();
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        void GetDevice();
        void RegisterEndpointNotificationCallback();
        void UnregisterEndpointNotificationCallback();
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate([In] ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore();
        void GetId();
        void GetState();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out int frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out int padding);
        void IsFormatSupported();
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService([In] ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(int frames);
        void GetNextPacketSize(out int frames);
    }
}
