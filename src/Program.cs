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
                Application.Run(new TrayContext());
            }
            return 0;
        }
    }
}
