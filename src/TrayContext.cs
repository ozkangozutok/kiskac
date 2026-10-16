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
        readonly ToolStripMenuItem captureItem;
        readonly ToolStripMenuItem updateItem;
        readonly Updater updater;
        volatile bool exiting;
        volatile bool capturing;
        OverlayForm overlay;
        SettingsForm settingsForm;

        public TrayContext(bool justUpdated)
        {
            invoker = new Control();
            GC.KeepAlive(invoker.Handle); // BeginInvoke için pencere tutamacı gerekiyor

            var menu = new ContextMenuStrip();
            captureItem = new ToolStripMenuItem("Ekran görüntüsü al", null, delegate { StartCaptureDelayed(); });
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
            menu.Items.Add("Ayarlar…", null, delegate { ShowSettings(); });
            updateItem = new ToolStripMenuItem("Güncellemeleri denetle…", null, delegate { updater.Check(true); });
            menu.Items.Add(updateItem);
            menu.Opening += delegate
            {
                startupItem.Checked = Startup.IsEnabled();
                captureItem.ShortcutKeyDisplayString = hook.HotKey.Display;
            };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = Icons.CreateIcon(SystemInformation.SmallIconSize.Width);
            tray.Text = TrayText(Settings.HotKey);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) StartCaptureDelayed(); };
            tray.Visible = true;

            hook = new KeyboardHook(Settings.HotKey, () => { if (!exiting) invoker.BeginInvoke(new Action(StartCapture)); });

            updater = new Updater(invoker, ExitThread);
            updater.CanInterrupt = () => overlay == null && !capturing;
            updater.StartAutomaticChecks();

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
                    Settings.HotKey.Display + " tuşuna bas ve alanı seç. Ctrl+C kopyalar, Ctrl+S kaydeder. Kısayolu Ayarlar'dan değiştirebilirsin.",
                    ToolTipIcon.Info);
            }
            else if (justUpdated)
                tray.ShowBalloonTip(4000, "Kıskaç güncellendi", "Kıskaç " + Updater.Current + " sürümüne güncellendi.", ToolTipIcon.Info);
            else if (Startup.IsEnabled())
                Startup.Set(true); // .exe taşındıysa başlangıç yolunu güncelle

            if (!hook.Installed)
                tray.ShowBalloonTip(5000, "Kıskaç",
                    "Kısayol yakalanamadı. Tepsi simgesine tıklayarak çekim yapabilirsin.", ToolTipIcon.Warning);
        }

        static string TrayText(HotKey k)
        {
            var s = "Kıskaç — " + k.Display + " ile ekran görüntüsü";
            return s.Length > 63 ? s.Substring(0, 63) : s; // NotifyIcon.Text en fazla 63 karakter
        }

        void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(hook, () => updater.Check(true));
                settingsForm.FormClosed += delegate
                {
                    tray.Text = TrayText(hook.HotKey);
                    settingsForm = null;
                };
                settingsForm.Show();
            }
            settingsForm.Activate();
            Native.ForceForeground(settingsForm.Handle);
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
            capturing = true;
            try
            {
                bmp = Util.CaptureScreen(vb);
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(3000, "Kıskaç", "Ekran yakalanamadı: " + ex.Message, ToolTipIcon.Error);
                return;
            }
            finally
            {
                capturing = false;
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
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
