using System;
using System.Threading;
using System.Windows.Forms;

namespace Kiskac
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--export-icon")
            {
                Icons.WriteIco(args[1]);
                return 0;
            }

            // CI'da çalışan iç denetimler (GitHub Actions, windows.yml)
            if (args.Length == 2 && args[0] == "--selftest")
                return SelfTest.Run(args[1]);

            // Video kodlayıcıyı yapay karelerle elle denetle (kayıttaki gibi MTA iş parçacığında)
            if (args.Length == 2 && args[0] == "--video-test")
            {
                int code = 1;
                var t = new Thread(() => { code = VideoTest.RunCli(args[1]); });
                t.SetApartmentState(ApartmentState.MTA);
                t.Start();
                t.Join();
                return code;
            }

            // Arayüzsüz gerçek ekran kaydı (geliştirme ve doğrulama için):
            // --record <dosya> <saniye> <x> <y> <gen> <yük> <mp4|gif> <ses 0-3>  → <dosya>.log
            // İsteğe bağlı 10. değer "<ses>@<saniye>": kayıt sırasında ses kaynağını değiştir (ör. 2@2.5)
            if ((args.Length == 9 || args.Length == 10) && args[0] == "--record")
                return VideoTest.RecordCli(args);

            // Ekran bölgesindeki yazıyı oku (geliştirme ve doğrulama için): --ocr <x> <y> <gen> <yük> <çıktı.txt>
            if (args.Length == 6 && args[0] == "--ocr")
            {
                int code = 1;
                var t = new Thread(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    using (var bmp = Util.CaptureScreen(new System.Drawing.Rectangle(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]))))
                    {
                        string text;
                        try { text = TextRecognizer.Recognize(bmp); code = 0; }
                        catch (Exception ex) { text = "HATA: " + ex; }
                        System.IO.File.WriteAllText(args[5], "süre: " + sw.ElapsedMilliseconds + " ms\r\n" + text, System.Text.Encoding.UTF8);
                    }
                });
                t.SetApartmentState(ApartmentState.MTA);
                t.Start();
                t.Join();
                return code;
            }

            // Güncellemeden sonra: eski süreç kapanana kadar bekle, artıkları temizle.
            bool justUpdated = false;
            int oldPid;
            if (args.Length == 2 && args[0] == "--after-update" && int.TryParse(args[1], out oldPid))
            {
                Updater.FinishUpdate(oldPid);
                justUpdated = true;
            }
            else
                Updater.CleanupLeftovers();

            bool created;
            using (var mutex = new Mutex(true, "Kiskac_SingleInstance", out created))
            {
                if (!created)
                {
                    // Zaten çalışıyorsa: yeniden açmak = ekran görüntüsü al
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting(TrayContext.CaptureEventName, out ev))
                        using (ev) ev.Set();
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                    MessageBox.Show(e.Exception.ToString(), "Kıskaç hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Run(new TrayContext(justUpdated));
            }
            return 0;
        }
    }
}
