using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Kiskac
{
    // GitHub Releases üzerinden güncelleme: en son sürümü denetler, Kiskac.exe'yi exe'nin yanına indirir,
    // SHA256 özetini ve derleme kimliğini doğrular; çalışan exe yeniden adlandırılıp yenisi yerine konur.
    sealed class Updater
    {
        const string Repo = "ozkangozutok/kiskac";
        const string AssetName = "Kiskac.exe";
        static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        internal sealed class Release
        {
            public Version Version;
            public string Tag, Body, HtmlUrl, AssetUrl, Digest;
        }

        readonly Control invoker;
        readonly Action exit;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        volatile bool busy;

        // Seçim ekranı açıkken otomatik denetim pencere açmaz.
        public Func<bool> CanInterrupt = () => true;

        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public Updater(Control invoker, Action exit)
        {
            this.invoker = invoker;
            this.exit = exit;
        }

        // Açılıştan 20 sn sonra, sonra her 3 saatte bir; son denetim 24 saatten eskiyse.
        public void StartAutomaticChecks()
        {
            timer.Interval = 20000;
            timer.Tick += delegate
            {
                timer.Interval = 3 * 3600 * 1000;
                if (Settings.AutoUpdate && CanInterrupt() && DateTime.UtcNow - Settings.LastUpdateCheck > CheckInterval)
                    Check(false);
            };
            timer.Start();
        }

        public void Check(bool userInitiated)
        {
            if (busy) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Release r = null;
                Exception error = null;
                try { r = FetchLatest(); }
                catch (Exception ex) { error = ex; }
                Ui(() =>
                {
                    busy = false;
                    if (error != null)
                    {
                        Util.Log("Güncelleme denetimi: " + error.Message);
                        if (userInitiated) Message("Güncellemeler denetlenemedi: " + error.Message, MessageBoxIcon.Warning);
                        return;
                    }
                    // İstek sürerken çekim başladıysa sonraki denetime bırakılır.
                    if (!userInitiated && !CanInterrupt()) return;
                    bool newer = r.Version > Current && r.AssetUrl != null;
                    // Sürüm yayınlandı ama Kiskac.exe henüz eklenmediyse (CI derliyor) 24 saat beklenmez.
                    if (r.Version <= Current || r.AssetUrl != null) Settings.LastUpdateCheck = DateTime.UtcNow;
                    if (newer && (userInitiated || Settings.SkippedVersion != r.Version.ToString()))
                        Offer(r);
                    else if (userInitiated)
                        Message(r.Version > Current
                            ? "Kıskaç " + r.Version + " yayınlandı ama Windows dosyası henüz hazır değil. Biraz sonra yeniden dene."
                            : "Kıskaç güncel. Kullandığın " + Current + " sürümü en son sürüm.", MessageBoxIcon.Information);
                });
            });
        }

        void Ui(Action a)
        {
            try { invoker.BeginInvoke(a); }
            catch (InvalidOperationException) { }
        }

        static string Get(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Kiskac/" + Current;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 20000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
                return sr.ReadToEnd();
        }

        static Release FetchLatest()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            return ParseRelease(Get("https://api.github.com/repos/" + Repo + "/releases/latest"));
        }

        internal static Release ParseRelease(string text)
        {
            var json = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
            if (json == null) throw new InvalidDataException("GitHub yanıtı okunamadı.");

            var r = new Release();
            r.Tag = (json.ContainsKey("tag_name") ? json["tag_name"] as string : null) ?? "";
            r.Body = json.ContainsKey("body") ? json["body"] as string : null;
            r.HtmlUrl = json.ContainsKey("html_url") ? json["html_url"] as string : "https://github.com/" + Repo + "/releases";
            Version v;
            if (!Version.TryParse(r.Tag.TrimStart('v', 'V'), out v)) throw new InvalidDataException("Sürüm etiketi okunamadı: " + r.Tag);
            r.Version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));

            var assets = json["assets"] as IEnumerable;
            if (assets != null)
                foreach (var item in assets)
                {
                    var a = item as Dictionary<string, object>;
                    if (a != null && a.ContainsKey("name") && (a["name"] as string) == AssetName)
                    {
                        r.AssetUrl = a["browser_download_url"] as string;
                        r.Digest = a.ContainsKey("digest") ? a["digest"] as string : null;
                    }
                }
            return r;
        }

        void Offer(Release r)
        {
            using (var f = new UpdateForm(r.Version.ToString(), Current.ToString(), r.Body))
            {
                switch (f.ShowDialog())
                {
                    case DialogResult.Yes: Install(r); break;
                    case DialogResult.Ignore: Settings.SkippedVersion = r.Version.ToString(); break;
                }
            }
        }

        // ---------------------------------------------------------------- kurulum

        void Install(Release r)
        {
            string exe = Application.ExecutablePath;
            string fresh = exe + ".new";
            if (r.Digest == null || !r.Digest.StartsWith("sha256:"))
            {
                Message("Güncellemenin özeti GitHub'da yok; güvenlik için elle kurman gerekiyor.", MessageBoxIcon.Warning);
                Process.Start(r.HtmlUrl);
                return;
            }

            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception error = null;
                try
                {
                    Download(r.AssetUrl, fresh);
                    Verify(fresh, r);
                }
                catch (Exception ex)
                {
                    error = ex;
                    TryDelete(fresh);
                }
                Ui(() =>
                {
                    busy = false;
                    if (error is UnauthorizedAccessException)
                    {
                        Message("Kıskaç'ın bulunduğu klasöre yazma iznin yok. Yeni sürümü indirme sayfasından elle kurabilirsin.",
                            MessageBoxIcon.Warning);
                        Process.Start(r.HtmlUrl);
                    }
                    else if (error != null)
                        Message("Güncelleme kurulamadı: " + error.Message, MessageBoxIcon.Warning);
                    else
                        SwapAndRestart(exe, fresh);
                });
            });
        }

        static void Download(string url, string path)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Kiskac/" + Current;
            req.Timeout = 60000;
            using (var resp = req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(path))
                src.CopyTo(dst);
        }

        internal static void Verify(string path, Release r)
        {
            string hex;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                hex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (hex != r.Digest.Substring(7).ToLowerInvariant())
                throw new InvalidDataException("İndirilen dosyanın özeti tutmuyor.");

            AssemblyName an;
            try { an = AssemblyName.GetAssemblyName(path); }
            catch (BadImageFormatException) { throw new InvalidDataException("İndirilen dosya Kıskaç değil."); }
            var v = an.Version;
            if (an.Name != "Kiskac" || new Version(v.Major, v.Minor, Math.Max(0, v.Build)) != r.Version)
                throw new InvalidDataException("İndirilen sürüm (" + an.Name + " " + v + ") beklenenle aynı değil.");
        }

        // Çalışan exe silinemez ama yeniden adlandırılabilir: eskisi .old olur, yenisi yerine geçer.
        // Yeni süreç bu sürecin kapanmasını bekler, .old dosyasını siler.
        void SwapAndRestart(string exe, string fresh)
        {
            string old = exe + ".old";
            try
            {
                TryDelete(old);
                File.Move(exe, old);
                try { File.Move(fresh, exe); }
                catch
                {
                    File.Move(old, exe);
                    throw;
                }
                Process.Start(exe, "--after-update " + Process.GetCurrentProcess().Id);
            }
            catch (Exception ex)
            {
                TryDelete(fresh);
                Message("Güncelleme kurulamadı: " + ex.Message, MessageBoxIcon.Warning);
                return;
            }
            exit();
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        // Güncellemeden sonra ilk açılışta: eski süreci bekle, artığı temizle.
        public static void FinishUpdate(int oldPid)
        {
            try
            {
                using (var p = Process.GetProcessById(oldPid))
                    p.WaitForExit(15000);
            }
            catch (ArgumentException) { } // zaten kapanmış
            string old = Application.ExecutablePath + ".old";
            for (int i = 0; i < 20 && File.Exists(old); i++)
            {
                TryDelete(old);
                if (File.Exists(old)) Thread.Sleep(250);
            }
        }

        public static void CleanupLeftovers()
        {
            TryDelete(Application.ExecutablePath + ".old");
            TryDelete(Application.ExecutablePath + ".new");
        }

        static void Message(string text, MessageBoxIcon icon)
        {
            MessageBox.Show(text, "Kıskaç", MessageBoxButtons.OK, icon);
        }
    }

    // Yeni sürüm penceresi: sürüm notları ve Şimdi güncelle / Sonra / Bu sürümü atla.
    sealed class UpdateForm : Form
    {
        public UpdateForm(string version, string current, string notes)
        {
            Text = "Kıskaç güncellemesi";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowIcon = false;
            Padding = new Padding(14);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
            layout.Controls.Add(new Label
            {
                Text = "Kıskaç " + version + " hazır",
                Font = new Font(Font.FontFamily, Font.Size * 1.35f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            });
            layout.Controls.Add(new Label
            {
                Text = "Kullandığın sürüm: " + current + ". Güncelleme indirilip kurulduktan sonra Kıskaç yeniden açılır.",
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Margin = new Padding(0, 0, 0, 10)
            });
            if (!string.IsNullOrEmpty(notes))
                layout.Controls.Add(new TextBox
                {
                    Text = notes.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Size = new Size(460, 200),
                    Margin = new Padding(0, 0, 0, 12),
                    BackColor = SystemColors.Window
                });

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            var update = new Button { Text = "Şimdi güncelle", DialogResult = DialogResult.Yes, AutoSize = true, Padding = new Padding(6, 0, 6, 0) };
            var later = new Button { Text = "Sonra", DialogResult = DialogResult.No, AutoSize = true };
            var skip = new Button { Text = "Bu sürümü atla", DialogResult = DialogResult.Ignore, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { update, later, skip });
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            AcceptButton = update;
            CancelButton = later;
        }
    }
}
