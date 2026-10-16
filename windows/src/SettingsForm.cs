using System;
using System.Drawing;
using System.Windows.Forms;

namespace Kiskac
{
    // Ayarlar penceresi. Değişiklikler hemen uygulanır ve kayıt defterine yazılır.
    sealed class SettingsForm : Form
    {
        readonly KeyboardHook hook;
        readonly Action checkForUpdates;
        readonly Button hotKeyButton;
        readonly LinkLabel resetLink;
        readonly Label hotKeyMessage;
        readonly Label dimValue, qualityValue;
        readonly TrackBar quality;
        readonly TextBox saveDir;
        bool recording;

        public SettingsForm(KeyboardHook hook, Action checkForUpdates)
        {
            this.hook = hook;
            this.checkForUpdates = checkForUpdates;

            Text = "Kıskaç Ayarları";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Icon = Icons.CreateIcon(32);
            Padding = new Padding(12);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };

            // ---------------------------------------------------------------- kısayol
            var g = Group("Kısayol");
            hotKeyButton = new Button { AutoSize = false, Size = new Size(170, 30), Font = new Font(Font, FontStyle.Bold) };
            hotKeyButton.Click += delegate { if (recording) StopRecording(); else StartRecording(); };
            resetLink = new LinkLabel { Text = "Varsayılan", AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
            resetLink.LinkClicked += delegate { Apply(HotKey.Default); };
            var row = Flow();
            row.Controls.Add(hotKeyButton);
            row.Controls.Add(resetLink);
            AddRow(g, "Ekran görüntüsü al", row);
            hotKeyMessage = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(440, 0), Margin = new Padding(3, 2, 3, 4) };
            AddFull(g, hotKeyMessage);
            root.Controls.Add(g.Parent);

            // ---------------------------------------------------------------- genel
            g = Group("Genel");
            AddFull(g, Check("Windows açılınca başlat", Startup.IsEnabled(), v => Startup.Set(v)));
            root.Controls.Add(g.Parent);

            // ---------------------------------------------------------------- seçim ekranı
            g = Group("Seçim ekranı");
            var dim = new TrackBar { Minimum = 15, Maximum = 75, TickFrequency = 10, Value = Settings.DimPercent, Width = 220, AutoSize = false, Height = 30 };
            dimValue = new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
            dim.ValueChanged += delegate { Settings.DimPercent = dim.Value; dimValue.Text = "%" + dim.Value; };
            dimValue.Text = "%" + dim.Value;
            row = Flow();
            row.Controls.Add(dim);
            row.Controls.Add(dimValue);
            AddRow(g, "Karartma", row);
            AddFull(g, Check("Başta ipucu metnini göster", Settings.ShowHint, v => Settings.ShowHint = v));
            AddFull(g, Check("Son kullanılan renk ve kalınlığı hatırla", Settings.RememberStyle, v => Settings.RememberStyle = v));
            root.Controls.Add(g.Parent);

            // ---------------------------------------------------------------- kaydetme
            g = Group("Kaydetme ve kopyalama");
            saveDir = new TextBox { ReadOnly = true, Width = 240, Text = Settings.SaveDir, Margin = new Padding(3, 5, 3, 3) };
            var browse = new Button { Text = "Değiştir…", AutoSize = true };
            browse.Click += delegate { ChooseSaveDir(); };
            row = Flow();
            row.Controls.Add(saveDir);
            row.Controls.Add(browse);
            AddRow(g, "Kayıt klasörü", row);

            var format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            format.Items.AddRange(new object[] { "PNG", "JPEG", "BMP" });
            format.SelectedIndex = Settings.Format;
            AddRow(g, "Biçim", format);

            quality = new TrackBar { Minimum = 50, Maximum = 100, TickFrequency = 10, Value = Settings.JpegQuality, Width = 220, AutoSize = false, Height = 30 };
            qualityValue = new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 0), Text = "%" + Settings.JpegQuality };
            quality.ValueChanged += delegate { Settings.JpegQuality = quality.Value; qualityValue.Text = "%" + quality.Value; };
            row = Flow();
            row.Controls.Add(quality);
            row.Controls.Add(qualityValue);
            AddRow(g, "JPEG kalitesi", row);
            format.SelectedIndexChanged += delegate
            {
                Settings.Format = format.SelectedIndex;
                quality.Enabled = qualityValue.Enabled = format.SelectedIndex == 1;
            };
            quality.Enabled = qualityValue.Enabled = Settings.Format == 1;

            AddFull(g, Check("Ctrl+S sormadan doğrudan kayıt klasörüne kaydetsin", Settings.QuickSave, v => Settings.QuickSave = v));
            AddFull(g, Check("Kaydettikten sonra panoya da kopyala", Settings.CopyAfterSave, v => Settings.CopyAfterSave = v));
            root.Controls.Add(g.Parent);

            // ---------------------------------------------------------------- güncelleme
            g = Group("Güncelleme");
            var check = new Button { Text = "Şimdi denetle", AutoSize = true };
            check.Click += delegate { checkForUpdates(); };
            row = Flow();
            row.Controls.Add(new Label { Text = Updater.Current.ToString(), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 8, 0) });
            row.Controls.Add(check);
            AddRow(g, "Sürüm", row);
            AddFull(g, Check("Güncellemeleri otomatik denetle", Settings.AutoUpdate, v => Settings.AutoUpdate = v));
            root.Controls.Add(g.Parent);

            var close = new Button { Text = "Kapat", AutoSize = true, Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            close.Click += delegate { Close(); };
            root.Controls.Add(close);
            CancelButton = close;

            Controls.Add(root);
            ShowHotKey();
        }

        // ---------------------------------------------------------------- yerleşim yardımcıları

        TableLayoutPanel Group(string title)
        {
            var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10) };
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            box.Controls.Add(t);
            return t;
        }

        static FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        }

        static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 0) });
            t.Controls.Add(c);
        }

        static void AddFull(TableLayoutPanel t, Control c)
        {
            t.Controls.Add(c);
            t.SetColumnSpan(c, 2);
        }

        static CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(3, 4, 3, 2) };
            c.CheckedChanged += delegate { set(c.Checked); };
            return c;
        }

        void ChooseSaveDir()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "Kayıt klasörünü seç";
                d.SelectedPath = Settings.SaveDir;
                d.ShowNewFolderButton = true;
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    Settings.SaveDir = d.SelectedPath;
                    saveDir.Text = d.SelectedPath;
                }
            }
        }

        // ---------------------------------------------------------------- kısayol kaydı

        void ShowHotKey()
        {
            hotKeyButton.Text = recording ? "Kısayola bas…" : Settings.HotKey.Display;
            resetLink.Visible = !recording && Settings.HotKey != HotKey.Default;
        }

        // Kanca tüm tuşları yakalayıp buraya iletir; PrtSc gibi WinForms'a hiç ulaşmayan tuşlar da kaydedilebilir.
        void StartRecording()
        {
            recording = true;
            hotKeyMessage.Text = "Yeni kısayola bas. Vazgeçmek için Esc.";
            hotKeyMessage.ForeColor = SystemColors.GrayText;
            ShowHotKey();
            hook.Recorder = k =>
            {
                try { BeginInvoke(new Action(() => OnRecorded(k))); }
                catch (InvalidOperationException) { }
            };
        }

        void StopRecording()
        {
            hook.Recorder = null;
            recording = false;
            if (hotKeyMessage.ForeColor == SystemColors.GrayText) hotKeyMessage.Text = "";
            ShowHotKey();
        }

        void OnRecorded(HotKey k)
        {
            if (!recording) return;
            if (k.Vk == 0x1B && k.Mods == Mods.None)
            {
                StopRecording();
                return;
            }
            var problem = k.Problem;
            if (problem != null)
            {
                hotKeyMessage.ForeColor = Color.Firebrick;
                hotKeyMessage.Text = problem;
                return;
            }
            Apply(k);
        }

        void Apply(HotKey k)
        {
            Settings.HotKey = k;
            hook.HotKey = k;
            hotKeyMessage.Text = "";
            StopRecording();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (recording) StopRecording(); // başka pencereye geçilirse tuşlar yutulmaya devam etmesin
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            hook.Recorder = null;
            base.OnFormClosed(e);
        }
    }
}
