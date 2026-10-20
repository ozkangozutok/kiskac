using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

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
        readonly ToolStripMenuItem videoItem, fullScreenItem;
        volatile bool exiting;
        volatile bool capturing;
        OverlayForm overlay;
        SettingsForm settingsForm;
        ScreenRecorder recorder;
        RecordingBorder recBorder;
        RecordingBar recBar;
        bool recStarted; // geri sayım bitti mi
        string balloonPath; // bildirime tıklanınca klasörde gösterilecek dosya

        public TrayContext(bool justUpdated)
        {
            invoker = new Control();
            GC.KeepAlive(invoker.Handle); // BeginInvoke için pencere tutamacı gerekiyor

            var menu = new ContextMenuStrip();
            captureItem = new ToolStripMenuItem("Ekran görüntüsü al", null, delegate { StartCaptureDelayed(); });
            captureItem.Font = new Font(captureItem.Font, FontStyle.Bold);
            menu.Items.Add(captureItem);
            videoItem = new ToolStripMenuItem("Video kaydet…", null, delegate
            {
                if (recorder != null) StopRecording();
                else StartCaptureDelayed(true);
            });
            menu.Items.Add(videoItem);
            fullScreenItem = new ToolStripMenuItem("Tam ekran video");
            fullScreenItem.DropDownItems.Add("-"); // alt menü açılırken ekranlarla doldurulur
            fullScreenItem.DropDownOpening += delegate { FillFullScreenMenu(); };
            menu.Items.Add(fullScreenItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Kayıt klasörünü aç", null, delegate
            {
                Process.Start("explorer.exe", "\"" + Settings.SaveDir + "\"");
            });
            menu.Items.Add("Video klasörünü aç", null, delegate
            {
                Process.Start("explorer.exe", "\"" + Settings.VideoDir + "\"");
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
                videoItem.Text = recorder != null ? "Kaydı durdur" : "Video kaydet…";
                videoItem.ShortcutKeyDisplayString = recorder != null ? hook.HotKey.Display : "";
                fullScreenItem.Visible = recorder == null;
            };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = Icons.CreateIcon(SystemInformation.SmallIconSize.Width);
            tray.Text = TrayText(Settings.HotKey);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (recorder != null) StopRecording();
                else StartCaptureDelayed();
            };
            tray.BalloonTipClicked += delegate
            {
                if (balloonPath != null && File.Exists(balloonPath))
                    Process.Start("explorer.exe", "/select,\"" + balloonPath + "\"");
            };
            tray.Visible = true;
            SystemEvents.SessionEnding += OnSessionEnding;

            hook = new KeyboardHook(Settings.HotKey, () => { if (!exiting) invoker.BeginInvoke(new Action(StartCapture)); });
            hook.VideoAction = () => { if (!exiting) invoker.BeginInvoke(new Action(ToggleFullScreenRecording)); };

            updater = new Updater(invoker, ExitThread);
            updater.CanInterrupt = () => overlay == null && !capturing && recorder == null;
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
        void StartCaptureDelayed(bool video = false)
        {
            var t = new System.Windows.Forms.Timer();
            t.Interval = 250;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                StartCapture(video);
            };
            t.Start();
        }

        void ShowBalloon(string title, string text, ToolTipIcon icon, string clickPath)
        {
            balloonPath = clickPath;
            tray.ShowBalloonTip(4000, title, text, icon);
        }

        void StartCapture()
        {
            StartCapture(false);
        }

        void StartCapture(bool video)
        {
            // Kayıt sürerken kısayol (veya yeniden açma) kaydı durdurur.
            if (recorder != null)
            {
                StopRecording();
                return;
            }
            try
            {
                StartCaptureCore(video);
            }
            catch (Exception ex)
            {
                Util.Log("Çekim hatası: " + ex);
                tray.ShowBalloonTip(3000, "Kıskaç", "Hata: " + ex.Message, ToolTipIcon.Error);
            }
        }

        void StartCaptureCore(bool video)
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
            if (video) form.UseVideoHint();
            overlay = form;
            form.FormClosed += delegate
            {
                overlay = null;
                bmp.Dispose();
                if (form.RecordFormat.HasValue) StartRecording(form.RecordRegion, form.RecordFormat.Value);
                if (form.OcrImage != null) CopyTextFrom(form.OcrImage);
            };
            form.Show();
            Util.Log("Overlay gösterildi: " + vb);
        }

        // ---------------------------------------------------------------- metin kopyalama (OCR)

        // Yazıyı arka planda okur, sonucu panoya koyar (pano işlemi arayüz iş parçacığında).
        void CopyTextFrom(Bitmap image)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text = null;
                Exception error = null;
                try { text = TextRecognizer.Recognize(image); }
                catch (Exception ex)
                {
                    error = ex.InnerException ?? ex;
                    Util.Log("Metin tanıma hatası: " + ex);
                }
                finally { image.Dispose(); }

                try { invoker.BeginInvoke(new Action(() => OnTextRecognized(text, error))); }
                catch (InvalidOperationException) { }
            });
        }

        void OnTextRecognized(string text, Exception error)
        {
            if (exiting) return;
            if (error != null)
            {
                ShowBalloon("Metin okunamadı", error.Message, ToolTipIcon.Warning, null);
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowBalloon("Yazı bulunamadı", "Seçili alanda okunabilir bir yazı yok. Daha geniş ya da daha net bir alan seçmeyi dene.", ToolTipIcon.Info, null);
                return;
            }
            try { Clipboard.SetDataObject(text, true, 10, 100); }
            catch (Exception ex)
            {
                ShowBalloon("Panoya kopyalanamadı", ex.Message, ToolTipIcon.Warning, null);
                return;
            }
            int lines = text.Split('\n').Length;
            string preview = text.Replace("\r", "").Replace('\n', ' ');
            if (preview.Length > 120) preview = preview.Substring(0, 117) + "…";
            ShowBalloon("Metin kopyalandı (" + lines + " satır)", preview, ToolTipIcon.Info, null);
        }

        // ---------------------------------------------------------------- video kaydı

        // Shift+kısayol: imlecin bulunduğu ekran için kayıt çubuğunu açar; çubuk hazırken ▶ gibi başlatır,
        // kayıt sürüyorsa durdurur.
        void ToggleFullScreenRecording()
        {
            if (recorder != null)
            {
                if (!recStarted && recBar != null && recBar.IsReady) recBar.Play();
                else StopRecording();
                return;
            }
            if (overlay != null && !overlay.IsDisposed) return; // seçim ekranı açıkken karışmasın
            StartRecording(Screen.FromPoint(Cursor.Position).Bounds, VideoFormat.Mp4);
        }

        void FillFullScreenMenu()
        {
            var items = fullScreenItem.DropDownItems;
            items.Clear();
            var video = hook.HotKey.VideoVariant;
            var current = new ToolStripMenuItem("İmlecin bulunduğu ekran", null, delegate
            {
                StartRecordingDelayed(Screen.FromPoint(Cursor.Position).Bounds);
            });
            if (video.HasValue) current.ShortcutKeyDisplayString = video.Value.Display;
            items.Add(current);

            var screens = Screen.AllScreens;
            if (screens.Length > 1)
            {
                items.Add(new ToolStripSeparator());
                for (int i = 0; i < screens.Length; i++)
                {
                    var b = screens[i].Bounds;
                    string text = "Ekran " + (i + 1) + "  —  " + b.Width + "×" + b.Height + (screens[i].Primary ? " (ana)" : "");
                    items.Add(text, null, delegate { StartRecordingDelayed(b); });
                }
                var all = SystemInformation.VirtualScreen;
                items.Add("Tüm ekranlar  —  " + all.Width + "×" + all.Height, null, delegate { StartRecordingDelayed(all); });
            }
        }

        // Menüden başlatınca menünün kapanma animasyonu kayda girmesin.
        void StartRecordingDelayed(Rectangle region)
        {
            var t = new System.Windows.Forms.Timer { Interval = 250 };
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                StartRecording(region, VideoFormat.Mp4);
            };
            t.Start();
        }

        void StartRecording(Rectangle region, VideoFormat format)
        {
            if (recorder != null || exiting) return;
            bool gif = format == VideoFormat.Gif;
            string dir = Settings.VideoDir, ext = gif ? ".gif" : ".mp4";
            string baseName = "Kıskaç_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(dir, baseName + ext);
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(dir, baseName + " (" + n + ")" + ext);

            var options = new RecordingOptions
            {
                Region = region,
                Format = format,
                Fps = gif ? Settings.GifFps : Settings.VideoFps,
                Cursor = Settings.RecordCursor,
                Audio = gif ? AudioMode.None : Settings.RecordAudio,
                SystemVolume = Settings.SystemVolume,
                MicVolume = Settings.MicVolume,
                Path = path
            };
            var rec = new ScreenRecorder(options);
            rec.Finished += (p, err) =>
            {
                try { invoker.BeginInvoke(new Action(() => OnRecordingFinished(rec, p, err))); }
                catch (InvalidOperationException) { }
            };

            // Bir ekranın tamamı kaydediliyorsa çerçeve gereksiz; yan ekranlara taşar da.
            bool wholeScreen = Array.Exists(Screen.AllScreens, s => region.Contains(s.Bounds));
            recBorder = wholeScreen ? null : new RecordingBorder(region);
            recBar = new RecordingBar(rec);
            recBar.PauseClicked += delegate
            {
                rec.Paused = !rec.Paused;
                if (recBorder != null) recBorder.Paused = rec.Paused;
            };
            recBar.StopClicked += delegate { StopRecording(); };
            recBar.AudioChanged += mode =>
            {
                rec.SetAudio(mode);
                Settings.RecordAudio = mode; // son seçim hatırlanır
            };
            recBar.VolumeChanged += (source, percent) =>
            {
                rec.SetVolume(source, percent);
                if (source == AudioMode.System) Settings.SystemVolume = percent;
                else Settings.MicVolume = percent;
            };
            // Geri sayım bitince (ya da "şimdi başla") kayıt gerçekten başlar.
            recBar.Begin += delegate
            {
                if (recorder != rec) return;
                recStarted = true;
                if (recBorder != null) recBorder.Paused = false;
                tray.Text = "Kıskaç — kaydediliyor (" + hook.HotKey.Display + ": durdur)";
                if (tray.Text.Length > 63) tray.Text = tray.Text.Substring(0, 63);
                rec.Start();
                Util.Log("Kayıt başladı: " + region + " " + format + " → " + path);
            };
            if (recBorder != null)
            {
                recBorder.Paused = true; // geri sayımda sarı
                recBorder.Show();
            }
            recorder = rec;
            recStarted = false;
            // Çubuk "Hazır" açılır; kayıt ▶'ye basılınca (geri sayımdan sonra) başlar.
            recBar.CountdownSeconds = Settings.RecordCountdown ? 3 : 0;
            recBar.Show();
        }

        void StopRecording()
        {
            if (recorder == null) return;
            if (!recStarted)
            {
                // Geri sayım sırasında: iptal, dosya oluşmaz.
                CloseRecordingUi();
                recorder = null;
                return;
            }
            recorder.Stop();
            if (recBorder != null) recBorder.Hide();
            if (recBar != null) recBar.ShowStopping();
        }

        void CloseRecordingUi()
        {
            if (recBorder != null) { recBorder.Close(); recBorder = null; }
            if (recBar != null) { recBar.Close(); recBar = null; }
            tray.Text = TrayText(hook.HotKey);
        }

        void OnRecordingFinished(ScreenRecorder rec, string path, Exception error)
        {
            if (rec != recorder) return;
            recorder = null;
            CloseRecordingUi();
            if (exiting) return;

            if (error != null)
                ShowBalloon("Kayıt başarısız", error.Message, ToolTipIcon.Error, null);
            else
            {
                string text = Path.GetFileName(path) + " kaydedildi. Klasörde göstermek için tıkla.";
                if (rec.Warning != null) text += "\n" + rec.Warning;
                ShowBalloon(rec.Options.Format == VideoFormat.Gif ? "GIF kaydedildi" : "Video kaydedildi", text, ToolTipIcon.Info, path);
            }
        }

        // Oturum kapanırken kayıt yarım kalmasın: durdur ve dosyayı bitir.
        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            FinishRecordingNow();
        }

        void FinishRecordingNow()
        {
            var rec = recorder;
            if (rec == null) return;
            rec.Stop();
            rec.Wait(15000);
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            SystemEvents.SessionEnding -= OnSessionEnding;
            FinishRecordingNow();
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
