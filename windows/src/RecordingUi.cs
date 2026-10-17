using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Kiskac
{
    // Kayıt penceresinin dışında kalan ince kırmızı çerçeve. Tıklamalar alttaki pencerelere geçer.
    sealed class RecordingBorder : Form
    {
        const int Thickness = 3;
        bool paused;

        public RecordingBorder(Rectangle region)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Bounds = Rectangle.Inflate(region, Thickness, Thickness);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // LAYERED | TRANSPARENT (tıklama geçirgen) | TOOLWINDOW | NOACTIVATE | TOPMOST
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Recording.ExcludeFromCapture(Handle);
        }

        // Sarı: geri sayım ya da duraklatma; kırmızı: kayıt
        public bool Paused
        {
            set
            {
                paused = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Magenta);
            using (var b = new SolidBrush(paused ? Color.FromArgb(255, 190, 0) : Color.FromArgb(235, 45, 45)))
            {
                e.Graphics.FillRectangle(b, 0, 0, Width, Thickness);
                e.Graphics.FillRectangle(b, 0, Height - Thickness, Width, Thickness);
                e.Graphics.FillRectangle(b, 0, 0, Thickness, Height);
                e.Graphics.FillRectangle(b, Width - Thickness, 0, Thickness, Height);
            }
        }
    }

    // Kayıt çubuğu. Önce geri sayar (bu sırada ses kaynakları seçilebilir, hemen başlatılabilir ya da iptal edilebilir),
    // sonra süreyi, ses düğmelerini, duraklat ve durdur düğmelerini gösterir. Odak çalmaz, boş yerinden sürüklenir.
    sealed class RecordingBar : Form
    {
        enum Btn { None, System, Mic, Pause, Stop }
        enum State { Countdown, Recording, Stopping }

        readonly ScreenRecorder recorder;
        readonly Rectangle region;
        readonly bool hasAudio;
        readonly Timer timer;
        readonly ToolTip tip = new ToolTip();
        float scale = 1f;
        Font timeFont, smallFont, glyphFont;
        Rectangle sysRect, micRect, pauseRect, stopRect;
        Btn hover;
        State state = State.Countdown;
        DateTime countdownEnd;
        bool blink;
        AudioMode audio;

        // Geri sayım bitti ya da "şimdi başla"ya basıldı
        public event EventHandler Begin;
        public event EventHandler PauseClicked;
        // Kayıtta: durdur; geri sayımda: iptal
        public event EventHandler StopClicked;
        public event Action<AudioMode> AudioChanged;

        public RecordingBar(ScreenRecorder recorder)
        {
            this.recorder = recorder;
            region = recorder.Options.Region;
            hasAudio = recorder.Options.Format == VideoFormat.Mp4;
            audio = recorder.Options.Audio;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(32, 32, 36);
            Text = "Kıskaç kaydı";
            Bounds = new Rectangle(region.X, region.Bottom + 8, 300, 40);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            timer = new Timer { Interval = 100 };
            timer.Tick += delegate { OnTick(); };
        }

        public bool Counting { get { return state == State.Countdown; } }

        // seconds <= 0: geri saymadan hemen başlar
        public void StartCountdown(int seconds)
        {
            countdownEnd = DateTime.UtcNow.AddSeconds(seconds);
            state = State.Countdown;
            if (seconds <= 0) BeginNow();
        }

        void BeginNow()
        {
            if (state != State.Countdown) return;
            state = State.Recording;
            tip.Hide(this);
            Invalidate();
            if (Begin != null) Begin(this, EventArgs.Empty);
        }

        public void ShowStopping()
        {
            state = State.Stopping;
            tip.Hide(this);
            Invalidate();
        }

        int tick;
        void OnTick()
        {
            if (state == State.Countdown && DateTime.UtcNow >= countdownEnd) BeginNow();
            if (++tick % 3 == 0) blink = !blink;
            Invalidate();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8; // TOOLWINDOW | NOACTIVATE | TOPMOST
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Recording.ExcludeFromCapture(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            scale = Native.DpiScale(Handle);
            timeFont = new Font("Segoe UI Semibold", 14f * scale, GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", 11f * scale, GraphicsUnit.Pixel);
            glyphFont = new Font("Segoe MDL2 Assets", 14f * scale, GraphicsUnit.Pixel);
            Place();
            timer.Start();
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        // Kayıt alanının altına, yer yoksa üstüne, o da yoksa alanın içine (görüntüye girmez) yerleşir.
        void Place()
        {
            int w = S(hasAudio ? 300 : 236), h = S(40), gap = S(8);
            var mon = Screen.FromRectangle(region).Bounds;
            int x = region.X + (region.Width - w) / 2;
            int y = region.Bottom + gap;
            if (y + h > mon.Bottom)
            {
                y = region.Top - gap - h;
                if (y < mon.Top) y = region.Bottom - h - gap * 3;
            }
            x = Math.Max(mon.Left + 4, Math.Min(x, mon.Right - w - 4));
            y = Math.Max(mon.Top + 4, Math.Min(y, mon.Bottom - h - 4));
            Bounds = new Rectangle(x, y, w, h);

            using (var p = Util.RoundRect(new RectangleF(0, 0, w, h), S(9)))
                Region = new Region(p);
            int b = S(30), top = (h - b) / 2;
            stopRect = new Rectangle(w - S(6) - b, top, b, b);
            pauseRect = new Rectangle(stopRect.X - S(2) - b, top, b, b);
            if (hasAudio)
            {
                micRect = new Rectangle(pauseRect.X - S(12) - b, top, b, b);
                sysRect = new Rectangle(micRect.X - S(2) - b, top, b, b);
            }
        }

        static bool Has(AudioMode m, AudioMode flag)
        {
            return ((int)m & (int)flag) != 0;
        }

        Btn HitTest(Point p)
        {
            if (state == State.Stopping) return Btn.None;
            if (hasAudio && sysRect.Contains(p)) return Btn.System;
            if (hasAudio && micRect.Contains(p)) return Btn.Mic;
            if (pauseRect.Contains(p)) return Btn.Pause;
            if (stopRect.Contains(p)) return Btn.Stop;
            return Btn.None;
        }

        // İpucu ayrı bir pencere olduğu için kayda girebilir: yalnızca geri sayımda gösterilir.
        string TipFor(Btn b)
        {
            if (state != State.Countdown) return null;
            switch (b)
            {
                case Btn.System: return Has(audio, AudioMode.System) ? "Sistem sesi kaydediliyor (kapatmak için tıkla)" : "Sistem sesi kapalı (açmak için tıkla)";
                case Btn.Mic: return Has(audio, AudioMode.Microphone) ? "Mikrofon kaydediliyor (kapatmak için tıkla)" : "Mikrofon kapalı (açmak için tıkla)";
                case Btn.Pause: return state == State.Countdown ? "Şimdi başla" : recorder.Paused ? "Devam et" : "Duraklat";
                case Btn.Stop: return state == State.Countdown ? "İptal" : "Durdur ve kaydet";
            }
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            bool paused = recorder.Paused;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var white = Color.White;
            var gray = Color.FromArgb(140, 140, 145);

            // durum noktası
            int d = S(12);
            var dot = new Rectangle(S(14), (Height - d) / 2, d, d);
            Color dotColor = state == State.Stopping ? Color.Gray
                : state == State.Countdown || paused ? Color.FromArgb(255, 190, 0)
                : blink ? Color.FromArgb(235, 45, 45) : Color.FromArgb(150, 40, 40);
            using (var br = new SolidBrush(dotColor))
                g.FillEllipse(br, dot);

            int textRight = (hasAudio ? sysRect.X : pauseRect.X) - S(4);
            var textRect = new Rectangle(dot.Right + S(10), 0, textRight - dot.Right - S(10), Height);
            if (state == State.Stopping)
            {
                TextRenderer.DrawText(g, "Kaydediliyor…", smallFont, textRect, white, flags);
                return;
            }
            if (state == State.Countdown)
            {
                int left = Math.Max(1, (int)Math.Ceiling((countdownEnd - DateTime.UtcNow).TotalSeconds));
                TextRenderer.DrawText(g, "Başlıyor  " + left, timeFont, textRect, white, flags);
            }
            else
            {
                var t = recorder.Elapsed;
                string time = ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
                TextRenderer.DrawText(g, time, timeFont, textRect, white, flags);
                var sz = TextRenderer.MeasureText(time, timeFont);
                string fmt = hasAudio ? "MP4" : "GIF";
                TextRenderer.DrawText(g, fmt, smallFont, new Rectangle(textRect.X + sz.Width - S(4), 0, S(36), Height), gray, flags);
            }

            // vurgu
            using (var hb = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
            {
                var hr = hover == Btn.System ? sysRect : hover == Btn.Mic ? micRect : hover == Btn.Pause ? pauseRect
                    : hover == Btn.Stop ? stopRect : Rectangle.Empty;
                if (!hr.IsEmpty) using (var p = Util.RoundRect(hr, S(6))) g.FillPath(hb, p);
            }

            // ses düğmeleri: açıkken beyaz simge, kapalıyken üstü çizili gri; açılamadıysa turuncu
            if (hasAudio)
            {
                var failed = recorder.FailedAudio;
                var orange = Color.FromArgb(255, 150, 40);
                bool sysOn = Has(audio, AudioMode.System), micOn = Has(audio, AudioMode.Microphone);
                TextRenderer.DrawText(g, sysOn ? "" : "", glyphFont, sysRect,
                    sysOn ? (Has(failed, AudioMode.System) ? orange : white) : gray, center);
                TextRenderer.DrawText(g, "", glyphFont, micRect,
                    micOn ? (Has(failed, AudioMode.Microphone) ? orange : white) : gray, center);
                if (!micOn)
                    using (var slash = new Pen(gray, Math.Max(1.5f, 1.6f * scale)))
                        g.DrawLine(slash, micRect.X + S(8), micRect.Bottom - S(8), micRect.Right - S(8), micRect.Y + S(8));
                using (var sep = new Pen(Color.FromArgb(60, 255, 255, 255)))
                    g.DrawLine(sep, pauseRect.X - S(7), S(10), pauseRect.X - S(7), Height - S(10));
            }

            // başlat/duraklat ve durdur/iptal
            if (state == State.Countdown)
            {
                int c = S(12);
                using (var red = new SolidBrush(Color.FromArgb(235, 45, 45)))
                    g.FillEllipse(red, pauseRect.X + (pauseRect.Width - c) / 2, pauseRect.Y + (pauseRect.Height - c) / 2, c, c);
                TextRenderer.DrawText(g, "", glyphFont, stopRect, white, center);
            }
            else
            {
                TextRenderer.DrawText(g, paused ? "" : "", glyphFont, pauseRect, white, center);
                int sq = S(12);
                using (var red = new SolidBrush(Color.FromArgb(235, 45, 45)))
                using (var p = Util.RoundRect(new RectangleF(stopRect.X + (stopRect.Width - sq) / 2f, stopRect.Y + (stopRect.Height - sq) / 2f, sq, sq), S(2)))
                    g.FillPath(red, p);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(e.Location);
            if (h == hover) return;
            hover = h;
            Cursor = h != Btn.None ? Cursors.Hand : Cursors.SizeAll;
            var text = TipFor(h);
            if (text != null) tip.Show(text, this, 0, Height + S(4), 3000);
            else tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = Btn.None;
            tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || state == State.Stopping) return;
            switch (HitTest(e.Location))
            {
                case Btn.System:
                    ToggleAudio(AudioMode.System);
                    break;
                case Btn.Mic:
                    ToggleAudio(AudioMode.Microphone);
                    break;
                case Btn.Pause:
                    if (state == State.Countdown) BeginNow();
                    else if (PauseClicked != null) PauseClicked(this, EventArgs.Empty);
                    break;
                case Btn.Stop:
                    if (StopClicked != null) StopClicked(this, EventArgs.Empty);
                    break;
                default:
                    // boş yerden sürükle
                    Recording.ReleaseCapture();
                    Recording.SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
                    return;
            }
            var text = TipFor(hover);
            if (text != null) tip.Show(text, this, 0, Height + S(4), 3000);
            else tip.Hide(this);
            Invalidate();
        }

        void ToggleAudio(AudioMode flag)
        {
            audio = (AudioMode)((int)audio ^ (int)flag);
            if (AudioChanged != null) AudioChanged(audio);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tip.Dispose();
                if (timeFont != null) timeFont.Dispose();
                if (smallFont != null) smallFont.Dispose();
                if (glyphFont != null) glyphFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    static class Recording
    {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // Windows 10 2004+: pencere ekran kayıtlarında görünmez (WDA_EXCLUDEFROMCAPTURE). Eski sürümlerde sessizce geçer.
        public static void ExcludeFromCapture(IntPtr hWnd)
        {
            try { Gdi.SetWindowDisplayAffinity(hWnd, 0x11); }
            catch (EntryPointNotFoundException) { }
        }
    }
}
