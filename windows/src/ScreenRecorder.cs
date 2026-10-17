using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kiskac
{
    enum VideoFormat { Mp4, Gif }

    // 0: ses yok, 1: sistem sesi, 2: mikrofon, 3: ikisi
    enum AudioMode { None = 0, System = 1, Microphone = 2, Both = 3 }

    sealed class RecordingOptions
    {
        public Rectangle Region;     // ekran koordinatları (fiziksel piksel)
        public VideoFormat Format;
        public int Fps;
        public bool Cursor;
        public AudioMode Audio;
        public int SystemVolume = 100; // yüzde
        public int MicVolume = 100;    // yüzde
        public string Path;          // son dosya yolu
    }

    // Kayıt hedefi: MP4 ya da GIF. Kareler BGRA, üstten alta, Width*Height*4 bayt.
    interface IVideoSink : IDisposable
    {
        int Width { get; }
        int Height { get; }
        void WriteFrame(IntPtr bgra, long time, long duration);
        void Finish();
    }

    // Duraklatılabilen kayıt saati. Video ve ses aynı saati kullanır, böylece senkron kalır.
    // "Mutlak" zaman, performans sayacının 100 ns birimine çevrilmiş hâlidir; WASAPI paketlerinin
    // yakalanma zamanı (qpcPosition) da aynı birimdedir, bu yüzden ses paketleri doğrudan yerleştirilebilir.
    sealed class RecordingClock
    {
        readonly object gate = new object();
        readonly List<long[]> pauses = new List<long[]>(); // tamamlanmış duraklamalar: {başlangıç, bitiş}
        long start = -1, pausedTotal, pauseStart = -1;

        public static long AbsoluteNow
        {
            get { return (long)(Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency)); }
        }

        public void Start() { Start(AbsoluteNow); }

        public void Start(long abs)
        {
            lock (gate) start = abs;
        }

        public bool Running
        {
            get { lock (gate) return start >= 0; }
        }

        public bool Paused
        {
            get { lock (gate) return pauseStart >= 0; }
            set { SetPaused(value, AbsoluteNow); }
        }

        public void SetPaused(bool paused, long abs)
        {
            lock (gate)
            {
                if (paused == (pauseStart >= 0)) return;
                if (paused) pauseStart = abs;
                else
                {
                    pauses.Add(new[] { pauseStart, abs });
                    pausedTotal += abs - pauseStart;
                    pauseStart = -1;
                }
            }
        }

        // Duraklamalar çıkarılmış geçen süre, 100 ns biriminde
        public long Now
        {
            get { return NowAt(AbsoluteNow); }
        }

        public long NowAt(long abs)
        {
            lock (gate)
            {
                if (start < 0) return 0;
                if (pauseStart >= 0) abs = pauseStart;
                return abs - start - pausedTotal;
            }
        }

        // Mutlak bir anın kayıttaki yeri (100 ns). Kayıt öncesi ya da duraklama sırasındaysa -1.
        public long ToRecording(long abs)
        {
            lock (gate)
            {
                if (start < 0 || abs < start) return -1;
                if (pauseStart >= 0 && abs >= pauseStart) return -1;
                long skipped = 0;
                foreach (var p in pauses)
                {
                    if (abs >= p[1]) skipped += p[1] - p[0];
                    else if (abs >= p[0]) return -1;
                }
                return abs - start - skipped;
            }
        }
    }

    // Seçili ekran bölgesini sabit kare hızında yakalayıp bir IVideoSink'e yazar. Tüm iş kendi MTA iş parçacığında yürür.
    sealed class ScreenRecorder
    {
        readonly RecordingOptions options;
        readonly RecordingClock clock = new RecordingClock();
        readonly ManualResetEvent stopSignal = new ManualResetEvent(false);
        Thread thread;
        volatile AudioRecorder audio;
        volatile int audioMode; // AudioMode; kayıt sırasında değişebilir

        // İş parçacığından çağrılır: (dosya yolu, hata). Hata yoksa ikincisi null.
        public event Action<string, Exception> Finished;

        public RecordingOptions Options { get { return options; } }
        public TimeSpan Elapsed { get { return TimeSpan.FromTicks(clock.Now); } }

        public string Warning
        {
            get { var a = audio; return a == null ? null : a.Warning; }
        }

        // İstenip açılamayan ses kaynakları (çubukta turuncu gösterilir)
        public AudioMode FailedAudio
        {
            get { var a = audio; return a == null ? AudioMode.None : a.Failed; }
        }

        // Ses kaynaklarını kayıt öncesinde ya da sırasında değiştirir (yalnızca MP4).
        public void SetAudio(AudioMode mode)
        {
            audioMode = (int)mode;
            options.Audio = mode;
            var a = audio;
            if (a != null) a.Mode = mode;
        }

        // Ses seviyeleri (yüzde, 0–200); kayıt öncesinde ya da sırasında değiştirilebilir.
        public void SetVolume(AudioMode source, int percent)
        {
            if (source == AudioMode.System) options.SystemVolume = percent;
            else options.MicVolume = percent;
            var a = audio;
            if (a != null) a.SetGain(source, percent / 100f);
        }

        // Kaynağın anlık seviyesi (0–1): çubuktaki gösterge için
        public float LevelOf(AudioMode source)
        {
            var a = audio;
            return a == null ? 0f : a.LevelOf(source);
        }

        public bool Paused
        {
            get { return clock.Paused; }
            set { clock.Paused = value; }
        }

        public ScreenRecorder(RecordingOptions options)
        {
            this.options = options;
            audioMode = (int)options.Audio;
        }

        public void Start()
        {
            thread = new Thread(Run);
            thread.SetApartmentState(ApartmentState.MTA);
            thread.IsBackground = false; // uygulama kapanırken dosya yarım kalmasın
            thread.Name = "Kıskaç ekran kaydı";
            thread.Start();
        }

        public void Stop()
        {
            stopSignal.Set();
        }

        public bool Wait(int milliseconds)
        {
            return thread == null || thread.Join(milliseconds);
        }

        void Run()
        {
            // Geçici ad gerçek uzantıyla bitmeli: Media Foundation kap biçimini (MP4) uzantıdan seçer.
            string temp = Path.ChangeExtension(options.Path, ".kayit" + Path.GetExtension(options.Path));
            Exception error = null;
            IVideoSink sink = null;
            var capture = new RegionCapture();
            Gdi.timeBeginPeriod(1);
            try
            {
                Thread.Sleep(200); // seçim ekranı tamamen kaybolsun
                if (File.Exists(temp)) File.Delete(temp);

                sink = CreateSink(temp);
                capture.Init(options.Region, sink.Width, sink.Height, options.Cursor);

                // MP4'te ses akışı her zaman vardır: kaynaklar kayıt sırasında açılıp kapatılabilsin (kapalıyken sessizlik).
                var mp4 = sink as Mp4Sink;
                if (mp4 != null && mp4.Writer.HasAudio)
                {
                    var a = new AudioRecorder((AudioMode)audioMode, clock, mp4.Writer);
                    a.SetGain(AudioMode.System, options.SystemVolume / 100f);
                    a.SetGain(AudioMode.Microphone, options.MicVolume / 100f);
                    audio = a;
                    a.Mode = (AudioMode)audioMode; // oluşturulurken değiştiyse
                    a.Start();
                }

                long frameDur = 10000000L / options.Fps;
                long lastIndex = -1;
                Util.Log("Kayıt hazır: " + sink.Width + "x" + sink.Height + " " + options.Fps + " kare/sn");
                clock.Start();
                // En az bir kare yazılır: hemen durdurulsa bile dosya geçerli olsun.
                while (lastIndex < 0 || !stopSignal.WaitOne(0))
                {
                    if (clock.Paused)
                    {
                        stopSignal.WaitOne(20);
                        continue;
                    }
                    long now = clock.Now;
                    long index = now / frameDur;
                    if (index <= lastIndex)
                    {
                        long wait = ((lastIndex + 1) * frameDur - now) / 10000;
                        stopSignal.WaitOne((int)Math.Max(1, Math.Min(wait, 50)));
                        continue;
                    }
                    // Geride kalındıysa kare atlanır; zaman damgası gerçek zamandan gelir, böylece süre doğru kalır.
                    capture.Grab();
                    sink.WriteFrame(capture.Bits, index * frameDur, frameDur);
                    lastIndex = index;
                }

                if (audio != null) audio.Stop();
                sink.Finish();
                sink.Dispose();
                sink = null;
                if (File.Exists(options.Path)) File.Delete(options.Path);
                File.Move(temp, options.Path);
            }
            catch (Exception ex)
            {
                error = ex;
                Util.Log("Kayıt hatası: " + ex);
            }
            finally
            {
                if (audio != null) audio.Dispose();
                if (sink != null) sink.Dispose();
                capture.Dispose();
                Gdi.timeEndPeriod(1);
                if (error != null) try { File.Delete(temp); } catch { }
            }

            var h = Finished;
            if (h != null) h(options.Path, error);
        }

        IVideoSink CreateSink(string path)
        {
            var r = options.Region;
            if (options.Format == VideoFormat.Gif)
            {
                var s = Fit(r.Width, r.Height, GifSink.MaxSide, GifSink.MaxSide, false);
                return new GifSink(path, s.Width, s.Height);
            }

            const bool withAudio = true; // bkz. Run: ses kaynakları kayıt sırasında açılabilir
            var full = Fit(r.Width, r.Height, int.MaxValue, int.MaxValue, true);
            try
            {
                return new Mp4Sink(path, full.Width, full.Height, options.Fps, withAudio);
            }
            catch (Exception ex)
            {
                // Bazı donanım kodlayıcıları 4096 pikselden geniş kareleri kabul etmez: küçültüp yeniden dene.
                var small = Fit(r.Width, r.Height, 4096, 2304, true);
                if (small == full) throw;
                Util.Log("MP4 " + full + " açılamadı, " + small + " deneniyor: " + ex.Message);
                try { File.Delete(path); } catch { }
                return new Mp4Sink(path, small.Width, small.Height, options.Fps, withAudio);
            }
        }

        // En-boy oranını koruyarak sığdırır; even=true ise H.264 için çift sayıya yuvarlar.
        static Size Fit(int w, int h, int maxW, int maxH, bool even)
        {
            double s = Math.Min(1.0, Math.Min((double)maxW / w, (double)maxH / h));
            int fw = Math.Max(2, (int)(w * s)), fh = Math.Max(2, (int)(h * s));
            if (even)
            {
                fw &= ~1;
                fh &= ~1;
            }
            return new Size(fw, fh);
        }
    }

    sealed class Mp4Sink : IVideoSink
    {
        public readonly Mp4Writer Writer;

        public Mp4Sink(string path, int w, int h, int fps, bool audio)
        {
            Writer = new Mp4Writer(path, w, h, fps, audio);
        }

        public int Width { get { return Writer.Width; } }
        public int Height { get { return Writer.Height; } }
        public void WriteFrame(IntPtr bgra, long time, long duration) { Writer.WriteVideo(bgra, time, duration); }
        public void Finish() { Writer.Finish(); }
        public void Dispose() { Writer.Dispose(); }
    }

    // Ekran bölgesini GDI ile üstten alta bir DIB'e kopyalar; gerekirse küçültür ve imleci çizer.
    sealed class RegionCapture : IDisposable
    {
        Rectangle region;
        int width, height;
        bool cursor, scaled;
        IntPtr screenDc, memDc, dib, oldBitmap, bits;
        readonly Dictionary<IntPtr, Point> hotspots = new Dictionary<IntPtr, Point>();

        public IntPtr Bits { get { return bits; } }

        public void Init(Rectangle region, int width, int height, bool cursor)
        {
            this.region = region;
            this.width = width;
            this.height = height;
            this.cursor = cursor;
            scaled = width != region.Width || height != region.Height;

            screenDc = Gdi.GetDC(IntPtr.Zero);
            memDc = Gdi.CreateCompatibleDC(screenDc);
            var bmi = new Gdi.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(Gdi.BITMAPINFOHEADER)),
                biWidth = width,
                biHeight = -height, // negatif: üstten alta
                biPlanes = 1,
                biBitCount = 32
            };
            dib = Gdi.CreateDIBSection(screenDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new OutOfMemoryException("Kayıt tamponu oluşturulamadı.");
            oldBitmap = Gdi.SelectObject(memDc, dib);
            if (scaled)
            {
                Gdi.SetStretchBltMode(memDc, Gdi.HALFTONE);
                Gdi.SetBrushOrgEx(memDc, 0, 0, IntPtr.Zero);
            }
        }

        public void Grab()
        {
            const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
            if (scaled)
                Gdi.StretchBlt(memDc, 0, 0, width, height, screenDc, region.X, region.Y, region.Width, region.Height, SRCCOPY | CAPTUREBLT);
            else
                Gdi.BitBlt(memDc, 0, 0, width, height, screenDc, region.X, region.Y, SRCCOPY | CAPTUREBLT);
            if (cursor) DrawCursor();
        }

        void DrawCursor()
        {
            var ci = new Gdi.CURSORINFO { cbSize = Marshal.SizeOf(typeof(Gdi.CURSORINFO)) };
            if (!Gdi.GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;

            Point hs;
            if (!hotspots.TryGetValue(ci.hCursor, out hs))
            {
                Gdi.ICONINFO ii;
                if (Gdi.GetIconInfo(ci.hCursor, out ii))
                {
                    hs = new Point(ii.xHotspot, ii.yHotspot);
                    if (ii.hbmMask != IntPtr.Zero) Gdi.DeleteObject(ii.hbmMask);
                    if (ii.hbmColor != IntPtr.Zero) Gdi.DeleteObject(ii.hbmColor);
                }
                hotspots[ci.hCursor] = hs;
            }

            double sx = (double)width / region.Width, sy = (double)height / region.Height;
            int x = (int)((ci.x - region.X) * sx) - hs.X;
            int y = (int)((ci.y - region.Y) * sy) - hs.Y;
            if (x > width || y > height || x < -64 || y < -64) return;
            Gdi.DrawIconEx(memDc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3 /* DI_NORMAL */);
        }

        public void Dispose()
        {
            if (memDc != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero) Gdi.SelectObject(memDc, oldBitmap);
                Gdi.DeleteDC(memDc);
                memDc = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero)
            {
                Gdi.DeleteObject(dib);
                dib = IntPtr.Zero;
            }
            if (screenDc != IntPtr.Zero)
            {
                Gdi.ReleaseDC(IntPtr.Zero, screenDc);
                screenDc = IntPtr.Zero;
            }
        }
    }

    static class Gdi
    {
        public const int HALFTONE = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, int usage, out IntPtr bits, IntPtr section, int offset);
        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")]
        public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
        [DllImport("user32.dll")]
        public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int step, IntPtr brush, int flags);
        [DllImport("winmm.dll")] public static extern int timeBeginPeriod(int ms);
        [DllImport("winmm.dll")] public static extern int timeEndPeriod(int ms);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, int affinity);
    }
}
