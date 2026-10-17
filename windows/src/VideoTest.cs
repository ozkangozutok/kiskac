using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Kiskac
{
    // Video kodlayıcının yapay karelerle denetimi. Hem --selftest hem de elle --video-test <klasör> kullanır.
    // Media Foundation yoksa (Windows N, bazı CI makineleri) sonuç SKIP olur, FAIL değil.
    static class VideoTest
    {
        public delegate void Check(string name, bool ok);

        public static void Run(string dir, Check check, StringBuilder log, bool large)
        {
            try
            {
                MF.Startup();
                MF.Shutdown();
            }
            catch (Exception ex)
            {
                log.AppendLine("SKIP  Media Foundation yok: " + ex.Message);
                return;
            }

            Mp4(dir, 640, 360, true, check, log);
            if (large)
            {
                Mp4(dir, 1920, 1080, false, check, log);
                Mp4(dir, 4096, 1316, false, check, log);
            }
        }

        static void Mp4(string dir, int w, int h, bool audio, Check check, StringBuilder log)
        {
            string name = "MP4 " + w + "x" + h + (audio ? " + ses" : "");
            string path = Path.Combine(dir, "kiskac-test-" + w + "x" + h + ".mp4");
            const int fps = 30, frames = 60; // 2 saniye
            const long frameDur = 10000000L / fps;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                var px = new int[w * h];
                var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
                try
                {
                    using (var mp4 = new Mp4Writer(path, w, h, fps, audio))
                    {
                        for (int f = 0; f < frames; f++)
                        {
                            Synthetic(px, w, h, f);
                            mp4.WriteVideo(handle.AddrOfPinnedObject(), f * frameDur, frameDur);
                        }
                        if (audio) WriteTone(mp4, 2.0);
                        mp4.Finish();
                    }
                }
                finally
                {
                    handle.Free();
                }

                var info = Mp4Probe.Read(path);
                double secs = info.Duration / 1e7;
                log.AppendLine("      " + name + ": " + info.Width + "x" + info.Height + ", " + secs.ToString("0.00") + " sn, ses: " + info.HasAudio
                    + ", " + (new FileInfo(path).Length / 1024) + " KB");
                check(name + " boyut", info.Width == w && info.Height == h);
                check(name + " süre", secs > 1.8 && secs < 2.3);
                check(name + " ses akışı", info.HasAudio == audio);
                bool frameOk = info.FirstFrame != null;
                if (frameOk)
                {
                    // Sol üstte kırmızı, sağ altta mavi kare: yön ve renk kanalı sırası doğru mu?
                    frameOk = IsColor(info.FirstFrame[24 * w + 24], 255, 0, 0)
                           && IsColor(info.FirstFrame[(h - 24) * w + (w - 24)], 0, 0, 255);
                    if (!frameOk)
                        log.AppendLine("      sol üst: " + Hex(info.FirstFrame[24 * w + 24]) + "  sağ alt: " + Hex(info.FirstFrame[(h - 24) * w + (w - 24)]));
                }
                check(name + " ilk kare yönü ve renkleri", frameOk);
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL  " + name + ": " + ex);
                check(name, false);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        static void Synthetic(int[] px, int w, int h, int frame)
        {
            for (int y = 0; y < h; y++)
            {
                int g = 40 + y * 120 / h;
                for (int x = 0; x < w; x++)
                    px[y * w + x] = unchecked((int)0xFF000000) | (g << 16) | (g << 8) | (60 + x * 120 / w);
            }
            Fill(px, w, 0, 0, 64, 64, 0xFF0000);
            Fill(px, w, w - 64, h - 64, 64, 64, 0x0000FF);
            int bar = (frame * 13) % Math.Max(1, w - 40);
            Fill(px, w, bar, h / 2 - 20, 40, 40, 0xFFFFFF);
        }

        static void Fill(int[] px, int w, int x0, int y0, int fw, int fh, int rgb)
        {
            int c = unchecked((int)0xFF000000) | rgb;
            for (int y = y0; y < y0 + fh; y++)
                for (int x = x0; x < x0 + fw; x++)
                    px[y * w + x] = c;
        }

        static void WriteTone(Mp4Writer mp4, double seconds)
        {
            const int chunk = 960; // 20 ms
            int total = (int)(seconds * Mp4Writer.AudioRate);
            var pcm = new short[chunk * 2];
            var handle = GCHandle.Alloc(pcm, GCHandleType.Pinned);
            try
            {
                for (int pos = 0; pos < total; pos += chunk)
                {
                    for (int i = 0; i < chunk; i++)
                    {
                        short v = (short)(Math.Sin(2 * Math.PI * 440 * (pos + i) / Mp4Writer.AudioRate) * 8000);
                        pcm[i * 2] = v;
                        pcm[i * 2 + 1] = v;
                    }
                    long t = (long)pos * 10000000L / Mp4Writer.AudioRate;
                    long d = (long)chunk * 10000000L / Mp4Writer.AudioRate;
                    mp4.WriteAudio(handle.AddrOfPinnedObject(), pcm.Length * 2, t, d);
                }
            }
            finally
            {
                handle.Free();
            }
        }

        static bool IsColor(int argb, int r, int g, int b)
        {
            int R = (argb >> 16) & 255, G = (argb >> 8) & 255, B = argb & 255;
            return Math.Abs(R - r) < 50 && Math.Abs(G - g) < 50 && Math.Abs(B - b) < 50;
        }

        static string Hex(int argb)
        {
            return "#" + (argb & 0xFFFFFF).ToString("X6");
        }

        // Elle çalıştırma: Kiskac.exe --video-test <klasör>  → <klasör>\video-test.log
        public static int RunCli(string dir)
        {
            var log = new StringBuilder();
            int fails = 0;
            Directory.CreateDirectory(dir);
            Run(dir, (n, ok) =>
            {
                log.AppendLine((ok ? "OK    " : "FAIL  ") + n);
                if (!ok) fails++;
            }, log, true);
            log.AppendLine(fails == 0 ? "Tüm video denetimleri geçti." : fails + " video denetimi başarısız.");
            File.WriteAllText(Path.Combine(dir, "video-test.log"), log.ToString(), Encoding.UTF8);
            return fails == 0 ? 0 : 1;
        }
    }
}
