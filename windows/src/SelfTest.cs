using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace Kiskac
{
    // GitHub Actions'ta Windows üzerinde çalışan iç denetimler: Kiskac.exe --selftest <günlük>
    // Başarısızlıkta 1 döner.
    static class SelfTest
    {
        public static int Run(string logPath)
        {
            var log = new StringBuilder();
            int fails = 0;
            Action<string, bool> check = (name, ok) =>
            {
                log.AppendLine((ok ? "OK    " : "FAIL  ") + name);
                if (!ok) fails++;
            };

            try
            {
                // Kısayol modeli
                var ctrlShiftX = new HotKey('X', Mods.Ctrl | Mods.Shift);
                check("HotKey gidiş-dönüş", HotKey.Parse(ctrlShiftX.ToString()) == ctrlShiftX);
                check("HotKey bozuk değer → varsayılan", HotKey.Parse("abc") == HotKey.Default && HotKey.Parse(null) == HotKey.Default);
                check("PrtSc geçerli", HotKey.Default.Problem == null);
                check("Ctrl+Shift+X geçerli", ctrlShiftX.Problem == null);
                check("F13 geçerli", new HotKey(0x7C, Mods.None).Problem == null);
                check("Ctrl+C engelli", new HotKey('C', Mods.Ctrl).Problem != null);
                check("Ctrl+Shift+Z engelli", new HotKey('Z', Mods.Ctrl | Mods.Shift).Problem != null);
                check("Win+S engelli", new HotKey('S', Mods.Win).Problem != null);
                check("Alt+K engelli", new HotKey('K', Mods.Alt).Problem != null);
                check("K engelli", new HotKey('K', Mods.None).Problem != null);
                check("Esc engelli", new HotKey(0x1B, Mods.None).Problem != null);
                check("Ctrl+Alt+Del engelli", new HotKey(0x2E, Mods.Ctrl | Mods.Alt).Problem != null);
                check("Görünen ad Ctrl+Shift+X", ctrlShiftX.Display == "Ctrl+Shift+X");
                check("Görünen ad PrtSc", HotKey.Default.Display == "PrtSc");
                log.AppendLine("      Ctrl+Alt+Q (AltGr): " + (new HotKey('Q', Mods.Ctrl | Mods.Alt).Problem ?? "geçerli"));
                log.AppendLine("      Ctrl+OEM1 adı: " + new HotKey(0xBA, Mods.Ctrl).Display);

                // GitHub sürüm yanıtı
                const string withExe = @"{""url"":""x"",""tag_name"":""v9.9.9"",""name"":""Kıskaç"",""draft"":false,""prerelease"":false,
                    ""html_url"":""https://github.com/o/r/releases/tag/v9.9.9"",""body"":null,
                    ""assets"":[{""name"":""Kiskac-macOS.zip"",""digest"":null,""size"":1,""browser_download_url"":""https://e/zip""},
                                {""name"":""Kiskac.exe"",""digest"":""sha256:abc"",""size"":2,""browser_download_url"":""https://e/exe""}]}";
                var r = Updater.ParseRelease(withExe);
                check("Sürüm 9.9.9", r.Version == new Version(9, 9, 9));
                check("Kiskac.exe adresi", r.AssetUrl == "https://e/exe");
                check("Özet", r.Digest == "sha256:abc");
                check("Boş sürüm notu", r.Body == null);

                const string noExe = @"{""tag_name"":""v1.2"",""html_url"":""h"",""assets"":[{""name"":""Kiskac-macOS.zip"",""digest"":null,""browser_download_url"":""z""}]}";
                var r2 = Updater.ParseRelease(noExe);
                check("Exe yokken adres boş", r2.AssetUrl == null && r2.Version == new Version(1, 2, 0));

                // Doğrulama: kendi exe'miz kendi özeti ve sürümüyle geçmeli
                string exe = Application.ExecutablePath;
                string hex;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(exe))
                    hex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                var self = new Updater.Release { Version = Updater.Current, Digest = "sha256:" + hex };
                check("Doğrulama geçer", Throws(() => Updater.Verify(exe, self)) == null);
                var badDigest = new Updater.Release { Version = Updater.Current, Digest = "sha256:" + new string('0', 64) };
                check("Yanlış özet reddedilir", Throws(() => Updater.Verify(exe, badDigest)) != null);
                var badVersion = new Updater.Release { Version = new Version(0, 0, 1), Digest = "sha256:" + hex };
                check("Yanlış sürüm reddedilir", Throws(() => Updater.Verify(exe, badVersion)) != null);

                // Video: yeniden örnekleme, GIF ve (Media Foundation varsa) MP4 + AAC. Kayıttaki gibi MTA iş parçacığında.
                // Media Foundation olmayan makinelerde MP4 denetimleri SKIP olur.
                string videoDir = Path.Combine(Path.GetTempPath(), "kiskac-selftest");
                Directory.CreateDirectory(videoDir);
                var video = new System.Threading.Thread(() =>
                {
                    try { VideoTest.Run(videoDir, (n, ok) => check(n, ok), log, false); }
                    catch (Exception ex) { check("Video denetimleri: " + ex.Message, false); }
                });
                video.SetApartmentState(System.Threading.ApartmentState.MTA);
                video.Start();
                video.Join();

                log.AppendLine("      Sürüm: " + Updater.Current);
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL  istisna: " + ex);
                fails++;
            }

            log.AppendLine(fails == 0 ? "Tüm denetimler geçti." : fails + " denetim başarısız.");
            File.WriteAllText(logPath, log.ToString(), Encoding.UTF8);
            return fails == 0 ? 0 : 1;
        }

        static Exception Throws(Action a)
        {
            try { a(); return null; }
            catch (Exception ex) { return ex; }
        }
    }
}
