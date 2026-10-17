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

    // Kayıt sırasında süre, duraklat ve durdur düğmelerini gösteren küçük çubuk. Odak çalmaz, sürüklenebilir.
    sealed class RecordingBar : Form
    {
        readonly ScreenRecorder recorder;
        readonly Rectangle region;
        readonly Timer timer;
        float scale = 1f;
        Font timeFont, smallFont, glyphFont;
        Rectangle pauseRect, stopRect;
        int hover; // 0 yok, 1 duraklat, 2 durdur
        bool blink, stopping;

        public event EventHandler PauseClicked;
        public event EventHandler StopClicked;

        public RecordingBar(ScreenRecorder recorder)
        {
            this.recorder = recorder;
            region = recorder.Options.Region;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(32, 32, 36);
            Text = "Kıskaç kaydı";
            Bounds = new Rectangle(region.X, region.Bottom + 8, 240, 40);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            timer = new Timer { Interval = 250 };
            timer.Tick += delegate
            {
                blink = !blink;
                Invalidate();
            };
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
            glyphFont = new Font("Segoe MDL2 Assets", 13f * scale, GraphicsUnit.Pixel);
            Place();
            timer.Start();
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        // Kayıt alanının altına, yer yoksa üstüne, o da yoksa alanın içine (görüntüye girmez) yerleşir.
        void Place()
        {
            int w = S(232), h = S(40), gap = S(8);
            var mon = Screen.FromRectangle(region).Bounds;
            int x = region.X + (region.Width - w) / 2;
            int y = region.Bottom + gap;
            if (y + h > mon.Bottom)
            {
                y = region.Top - gap - h;
                if (y < mon.Top) y = region.Bottom - h - gap;
            }
            x = Math.Max(mon.Left + 4, Math.Min(x, mon.Right - w - 4));
            y = Math.Max(mon.Top + 4, Math.Min(y, mon.Bottom - h - 4));
            Bounds = new Rectangle(x, y, w, h);

            using (var p = Util.RoundRect(new RectangleF(0, 0, w, h), S(9)))
                Region = new Region(p);
            int b = S(30);
            stopRect = new Rectangle(w - S(6) - b, (h - b) / 2, b, b);
            pauseRect = new Rectangle(stopRect.X - S(4) - b, stopRect.Y, b, b);
        }

        public void ShowStopping()
        {
            stopping = true;
            timer.Stop();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            bool paused = recorder.Paused;

            // durum noktası
            int d = S(12);
            var dot = new Rectangle(S(14), (Height - d) / 2, d, d);
            Color dotColor = stopping ? Color.Gray : paused ? Color.FromArgb(255, 190, 0)
                : blink ? Color.FromArgb(235, 45, 45) : Color.FromArgb(150, 40, 40);
            using (var br = new SolidBrush(dotColor))
                g.FillEllipse(br, dot);

            // süre ve biçim
            var t = recorder.Elapsed;
            string time = stopping ? "Kaydediliyor…" : ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
            var textRect = new Rectangle(dot.Right + S(10), 0, pauseRect.X - dot.Right - S(10), Height);
            TextRenderer.DrawText(g, time, stopping ? smallFont : timeFont, textRect, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
            if (!stopping)
            {
                var sz = TextRenderer.MeasureText(time, timeFont);
                string fmt = recorder.Options.Format == VideoFormat.Gif ? "GIF" : "MP4";
                var fr = new Rectangle(textRect.X + sz.Width - S(4), 0, S(40), Height);
                TextRenderer.DrawText(g, fmt, smallFont, fr, Color.FromArgb(150, 150, 155),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
            }
            if (stopping) return;

            // düğmeler
            using (var hb = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
            {
                if (hover == 1) using (var p = Util.RoundRect(pauseRect, S(6))) g.FillPath(hb, p);
                if (hover == 2) using (var p = Util.RoundRect(stopRect, S(6))) g.FillPath(hb, p);
            }
            TextRenderer.DrawText(g, paused ? "" : "", glyphFont, pauseRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            int sq = S(12);
            using (var red = new SolidBrush(Color.FromArgb(235, 45, 45)))
            using (var p = Util.RoundRect(new RectangleF(stopRect.X + (stopRect.Width - sq) / 2f, stopRect.Y + (stopRect.Height - sq) / 2f, sq, sq), S(2)))
                g.FillPath(red, p);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = pauseRect.Contains(e.Location) ? 1 : stopRect.Contains(e.Location) ? 2 : 0;
            if (h != hover)
            {
                hover = h;
                Cursor = h != 0 ? Cursors.Hand : Cursors.SizeAll;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = 0;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || stopping) return;
            if (pauseRect.Contains(e.Location))
            {
                if (PauseClicked != null) PauseClicked(this, EventArgs.Empty);
                Invalidate();
            }
            else if (stopRect.Contains(e.Location))
            {
                if (StopClicked != null) StopClicked(this, EventArgs.Empty);
            }
            else
            {
                // boş yerden sürükle
                Recording.ReleaseCapture();
                Recording.SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
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
