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
            Gif(dir, check, log); // Media Foundation gerektirmez

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
                GifSpeed(dir, log);
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

        // 4 kare yazılır; 2. kare 1.'nin, 4. kare 3.'nün aynısı → GIF'te 2 kare, her biri 200 ms olmalı.
        static void Gif(string dir, Check check, StringBuilder log)
        {
            const int w = 640, h = 360;
            string path = Path.Combine(dir, "kiskac-test.gif");
            try
            {
                var px = new int[w * h];
                var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
                try
                {
                    using (var gif = new GifSink(path, w, h))
                    {
                        const long ms100 = 1000000;
                        for (int f = 0; f < 4; f++)
                        {
                            Synthetic(px, w, h, f < 2 ? 0 : 10);
                            gif.WriteFrame(handle.AddrOfPinnedObject(), f * ms100, ms100);
                        }
                        gif.Finish();
                    }
                }
                finally
                {
                    handle.Free();
                }

                using (var img = System.Drawing.Image.FromFile(path))
                {
                    var dim = System.Drawing.Imaging.FrameDimension.Time;
                    int frames = img.GetFrameCount(dim);
                    var delays = img.GetPropertyItem(0x5100).Value; // her kare için 4 bayt, 1/100 sn
                    int d0 = BitConverter.ToInt32(delays, 0), d1 = frames > 1 ? BitConverter.ToInt32(delays, 4) : -1;
                    log.AppendLine("      GIF: " + frames + " kare, süreler " + d0 + "/" + d1 + " cs, " + new FileInfo(path).Length / 1024 + " KB");
                    check("GIF kare sayısı (değişmeyen kareler birleşir)", frames == 2);
                    check("GIF kare süreleri", d0 == 20 && d1 == 20);

                    img.SelectActiveFrame(dim, 0);
                    using (var b0 = new System.Drawing.Bitmap(img))
                        check("GIF ilk kare renkleri", IsColor(b0.GetPixel(24, 24).ToArgb(), 255, 0, 0)
                            && IsColor(b0.GetPixel(w - 24, h - 24).ToArgb(), 0, 0, 255)
                            && IsColor(b0.GetPixel(20, h / 2), 255, 255, 255));
                    img.SelectActiveFrame(dim, 1);
                    using (var b1 = new System.Drawing.Bitmap(img))
                    {
                        // 2. karede beyaz kutu kaymış olmalı; değişmeyen köşeler önceki kareden kalır
                        int bar = 10 * 13 + 20;
                        check("GIF ikinci kare (yalnızca değişen bölge)", IsColor(b1.GetPixel(bar, h / 2).ToArgb(), 255, 255, 255)
                            && !IsColor(b1.GetPixel(5, h / 2).ToArgb(), 255, 255, 255)
                            && IsColor(b1.GetPixel(24, 24).ToArgb(), 255, 0, 0));
                    }
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL  GIF: " + ex);
                check("GIF", false);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        // En kötü durum: her karesi tamamen değişen, çok renkli 1280x720 akış. Bilgi amaçlı süre ölçümü.
        static void GifSpeed(string dir, StringBuilder log)
        {
            const int w = 1280, h = 720, frames = 20;
            string path = Path.Combine(dir, "kiskac-hiz.gif");
            var px = new int[w * h];
            var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
            var rnd = new Random(1);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var gif = new GifSink(path, w, h))
                {
                    for (int f = 0; f < frames; f++)
                    {
                        Synthetic(px, w, h, f);
                        for (int i = 0; i < px.Length; i += 7) px[i] ^= rnd.Next(0x404040);
                        gif.WriteFrame(handle.AddrOfPinnedObject(), f * 1000000L, 1000000L);
                    }
                    gif.Finish();
                }
                log.AppendLine("      GIF hızı (en kötü durum 1280x720): " + (sw.ElapsedMilliseconds / frames) + " ms/kare, "
                    + new FileInfo(path).Length / 1024 + " KB");
            }
            finally
            {
                handle.Free();
                try { File.Delete(path); } catch { }
            }
        }

        static bool IsColor(System.Drawing.Color c, int r, int g, int b)
        {
            return IsColor(c.ToArgb(), r, g, b);
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

        public static int RecordCli(string[] a)
        {
            string path = Path.GetFullPath(a[1]);
            var log = new StringBuilder();
            try
            {
                var o = new RecordingOptions
                {
                    Path = path,
                    Region = new System.Drawing.Rectangle(int.Parse(a[3]), int.Parse(a[4]), int.Parse(a[5]), int.Parse(a[6])),
                    Format = a[7] == "gif" ? VideoFormat.Gif : VideoFormat.Mp4,
                    Audio = (AudioMode)int.Parse(a[8]),
                    Cursor = true
                };
                o.Fps = o.Format == VideoFormat.Gif ? Settings.GifFps : Settings.VideoFps;
                var rec = new ScreenRecorder(o);
                Exception err = null;
                rec.Finished += (p, e) => { err = e; };
                rec.Start();
                System.Threading.Thread.Sleep((int)(double.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) * 1000));
                rec.Stop();
                rec.Wait(30000);
                log.AppendLine("Hata: " + (err == null ? "yok" : err.ToString()));
                if (rec.Warning != null) log.AppendLine("Uyarı: " + rec.Warning);
                if (err == null && o.Format == VideoFormat.Mp4)
                {
                    var info = Mp4Probe.Read(path, true);
                    double secs = info.Duration / 1e7;
                    log.AppendLine("MP4: " + info.Width + "x" + info.Height + ", " + secs.ToString("0.00") + " sn, " + info.FrameCount + " kare ("
                        + (info.FrameCount / Math.Max(0.01, secs)).ToString("0.0") + " kare/sn), ses: " + info.HasAudio
                        + ", " + new FileInfo(path).Length / 1024 + " KB");
                    if (info.FirstFrame != null)
                        using (var bmp = new System.Drawing.Bitmap(info.Width, info.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                        {
                            var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, info.Width, info.Height),
                                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                            for (int y = 0; y < info.Height; y++)
                                Marshal.Copy(info.FirstFrame, y * info.Width, bd.Scan0 + y * bd.Stride, info.Width);
                            bmp.UnlockBits(bd);
                            bmp.Save(path + ".ilk-kare.png", System.Drawing.Imaging.ImageFormat.Png);
                        }
                }
                else if (err == null)
                    log.AppendLine("GIF: " + new FileInfo(path).Length / 1024 + " KB");
                File.WriteAllText(path + ".log", log.ToString(), Encoding.UTF8);
                return err == null ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(path + ".log", log + "İstisna: " + ex, Encoding.UTF8);
                return 1;
            }
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
