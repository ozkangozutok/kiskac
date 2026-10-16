using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Kiskac
{
    // Uygulamanın gövdesi: sistem tepsisi simgesi, PrtSc kancası ve ikinci örnekten gelen çekim istekleri.
    sealed class TrayContext : ApplicationContext
    {
        public const string CaptureEventName = "Kiskac_CaptureEvent";

        readonly NotifyIcon tray;
        readonly Control invoker;
        readonly KeyboardHook hook;
        readonly EventWaitHandle captureEvent;
        readonly ToolStripMenuItem startupItem;
        volatile bool exiting;
        OverlayForm overlay;

        public TrayContext()
        {
            invoker = new Control();
            GC.KeepAlive(invoker.Handle); // BeginInvoke için pencere tutamacı gerekiyor

            var menu = new ContextMenuStrip();
            var captureItem = new ToolStripMenuItem("Ekran görüntüsü al", null, delegate { StartCaptureDelayed(); });
            captureItem.ShortcutKeyDisplayString = "PrtSc";
            captureItem.Font = new Font(captureItem.Font, FontStyle.Bold);
            menu.Items.Add(captureItem);
            menu.Items.Add("Kayıt klasörünü aç", null, delegate
            {
                Process.Start("explorer.exe", "\"" + Settings.SaveDir + "\"");
            });
            startupItem = new ToolStripMenuItem("Windows açılınca başlat");
            startupItem.Click += delegate
            {
                Startup.Set(!Startup.IsEnabled());
                startupItem.Checked = Startup.IsEnabled();
            };
            menu.Items.Add(startupItem);
            menu.Opening += delegate { startupItem.Checked = Startup.IsEnabled(); };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = Icons.CreateIcon(SystemInformation.SmallIconSize.Width);
            tray.Text = "Kıskaç — PrtSc ile ekran görüntüsü";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) StartCaptureDelayed(); };
            tray.Visible = true;

            hook = new KeyboardHook(() => { if (!exiting) invoker.BeginInvoke(new Action(StartCapture)); });

            captureEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CaptureEventName);
            var listener = new Thread(() =>
            {
                while (true)
                {
                    captureEvent.WaitOne();
                    if (exiting) return;
                    try { invoker.BeginInvoke(new Action(StartCapture)); }
                    catch (InvalidOperationException) { return; }
                }
            });
            listener.IsBackground = true;
            listener.Start();
            Util.Log("Başladı. Kanca kuruldu: " + hook.Installed);

            if (!Settings.Initialized)
            {
                Startup.Set(true);
                Settings.Initialized = true;
                tray.ShowBalloonTip(5000, "Kıskaç çalışıyor",
                    "PrtSc tuşuna bas ve alanı seç. Ctrl+C kopyalar, Ctrl+S kaydeder.", ToolTipIcon.Info);
            }
            else if (Startup.IsEnabled())
                Startup.Set(true); // .exe taşındıysa başlangıç yolunu güncelle

            if (!hook.Installed)
                tray.ShowBalloonTip(5000, "Kıskaç",
                    "PrtSc tuşu yakalanamadı. Tepsi simgesine tıklayarak çekim yapabilirsin.", ToolTipIcon.Warning);
        }

        // Menü / tepsi tıklamasında menünün kapanma animasyonu görüntüye girmesin.
        void StartCaptureDelayed()
        {
            var t = new System.Windows.Forms.Timer();
            t.Interval = 250;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                StartCapture();
            };
            t.Start();
        }

        void StartCapture()
        {
            try
            {
                StartCaptureCore();
            }
            catch (Exception ex)
            {
                Util.Log("Çekim hatası: " + ex);
                tray.ShowBalloonTip(3000, "Kıskaç", "Hata: " + ex.Message, ToolTipIcon.Error);
            }
        }

        void StartCaptureCore()
        {
            Util.Log("StartCapture");
            if (exiting) return;
            if (overlay != null && !overlay.IsDisposed)
            {
                Native.ForceForeground(overlay.Handle);
                return;
            }

            Rectangle vb = SystemInformation.VirtualScreen;
            Bitmap bmp;
            try
            {
                bmp = Util.CaptureScreen(vb);
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(3000, "Kıskaç", "Ekran yakalanamadı: " + ex.Message, ToolTipIcon.Error);
                return;
            }

            var form = new OverlayForm(bmp, vb);
            overlay = form;
            form.FormClosed += delegate
            {
                overlay = null;
                bmp.Dispose();
            };
            form.Show();
            Util.Log("Overlay gösterildi: " + vb);
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            captureEvent.Set();
            hook.Dispose();
            if (overlay != null && !overlay.IsDisposed) overlay.Close();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
