using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Kiskac
{
    enum Tool { Select, Pen, Line, Arrow, Rect, Marker, Text }

    sealed class ToolButton
    {
        public string Id;
        public string Tip;
        public Rectangle Rect;
        public Tool? Tool;
        public int ColorIndex = -1;
        public bool SeparatorBefore;
    }

    // Tüm sanal ekranı kaplayan, karartılmış ekran görüntüsü üzerinde alan seçme ve çizim penceresi.
    sealed class OverlayForm : Form
    {
        static readonly Color[] Palette =
        {
            Color.FromArgb(235, 45, 45),
            Color.FromArgb(255, 195, 0),
            Color.FromArgb(40, 185, 85),
            Color.FromArgb(0, 125, 255),
            Color.FromArgb(20, 20, 20),
            Color.White
        };
        static readonly float[] Widths = { 2, 3, 5, 8, 12 };
        static readonly Color Accent = Color.FromArgb(0, 120, 215);
        const string HintText = "Alanı seçmek için sürükleyin   •   Tıklayın: tüm ekran   •   Esc: iptal";

        enum Mode { None, Selecting, Moving, Resizing, Drawing }

        readonly Bitmap shot;
        readonly Bitmap dimmed;
        readonly Rectangle vbounds;
        float scale = 1f;
        Font uiFont, glyphFont, letterFont;

        Rectangle sel;
        bool hasSel;
        Mode mode = Mode.None;
        Point dragStart;
        Rectangle selStart;
        int resizeHandle = -1;

        Tool tool = Tool.Select;
        int colorIndex;
        float penWidth = 3f;
        readonly List<Shape> shapes = new List<Shape>();
        Shape current;
        TextShape editing;

        readonly List<ToolButton> buttons = new List<ToolButton>();
        Rectangle toolbarRect;
        ToolButton hover;
        Rectangle lastVisual;

        public OverlayForm(Bitmap shot, Rectangle vbounds)
        {
            this.shot = shot;
            this.vbounds = vbounds;
            dimmed = MakeDimmed(shot);
            if (Settings.RememberStyle)
            {
                colorIndex = Math.Max(0, Math.Min(Palette.Length - 1, Settings.LastColor));
                penWidth = Math.Max(1, Math.Min(40, Settings.LastWidth));
            }

            Text = "Kıskaç";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            Bounds = vbounds;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

            BuildButtons();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: Alt+Tab listesinde görünmesin
                return cp;
            }
        }

        static Bitmap MakeDimmed(Bitmap src)
        {
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            var all = new Rectangle(0, 0, src.Width, src.Height);
            using (var g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, all, all, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                using (var br = new SolidBrush(Color.FromArgb(Settings.DimPercent * 255 / 100, 0, 0, 0)))
                    g.FillRectangle(br, all);
            }
            return b;
        }

        void BuildButtons()
        {
            AddTool("select", Tool.Select, "Seç / taşı (S)");
            AddTool("pen", Tool.Pen, "Kalem (P)");
            AddTool("line", Tool.Line, "Çizgi (L)  •  Shift: 45°");
            AddTool("arrow", Tool.Arrow, "Ok (A)  •  Shift: 45°");
            AddTool("rect", Tool.Rect, "Dikdörtgen (R)  •  Shift: kare");
            AddTool("marker", Tool.Marker, "Fosforlu kalem (M)");
            AddTool("text", Tool.Text, "Metin (T)  •  Enter: bitir");
            for (int i = 0; i < Palette.Length; i++)
                buttons.Add(new ToolButton { Id = "color", ColorIndex = i, Tip = "Renk", SeparatorBefore = i == 0 });
            buttons.Add(new ToolButton { Id = "width", Tip = "Kalınlık  •  fare tekerleği" });
            buttons.Add(new ToolButton { Id = "undo", Tip = "Geri al (Ctrl+Z)", SeparatorBefore = true });
            buttons.Add(new ToolButton { Id = "save", Tip = "Kaydet (Ctrl+S)" });
            buttons.Add(new ToolButton { Id = "copy", Tip = "Kopyala (Ctrl+C / Enter)" });
            buttons.Add(new ToolButton { Id = "close", Tip = "Kapat (Esc)" });
        }

        void AddTool(string id, Tool t, string tip)
        {
            buttons.Add(new ToolButton { Id = id, Tool = t, Tip = tip });
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            scale = Native.DpiScale(Handle);
            uiFont = new Font("Segoe UI", 12.5f * scale, GraphicsUnit.Pixel);
            glyphFont = new Font("Segoe MDL2 Assets", 15f * scale, GraphicsUnit.Pixel);
            letterFont = new Font("Segoe UI", 17f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Bounds != vbounds) Bounds = vbounds;
            Native.ForceForeground(Handle);
            Activate();
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            // Pencere birden fazla monitöre yayıldığı için DPI değişiminde yeniden boyutlanmasın.
            if (m.Msg == Native.WM_DPICHANGED)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        int S(float v)
        {
            return (int)Math.Round(v * scale);
        }

        Color CurrentColor
        {
            get { return Palette[colorIndex]; }
        }

        bool ToolbarVisible
        {
            get { return hasSel && mode != Mode.Selecting; }
        }

        Rectangle MonitorRect(Point clientPt)
        {
            var b = Screen.FromPoint(new Point(clientPt.X + vbounds.X, clientPt.Y + vbounds.Y)).Bounds;
            b.Offset(-vbounds.X, -vbounds.Y);
            return b;
        }

        Point Clamp(Point p)
        {
            return new Point(Math.Max(0, Math.Min(p.X, ClientSize.Width)), Math.Max(0, Math.Min(p.Y, ClientSize.Height)));
        }

        static Rectangle Normalize(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        // ---------------------------------------------------------------- yerleşim

        void LayoutToolbar()
        {
            if (!hasSel)
            {
                toolbarRect = Rectangle.Empty;
                return;
            }
            int cell = S(32), sep = S(10), pad = S(5), gap = S(8);
            int w = pad * 2;
            foreach (var b in buttons)
                w += cell + (b.SeparatorBefore ? sep : 0);
            int h = cell + pad * 2;

            var mon = MonitorRect(new Point(sel.X + sel.Width / 2, sel.Y + sel.Height / 2));
            int x = sel.Right - w;
            int y = sel.Bottom + gap;
            if (y + h > mon.Bottom - 2)
            {
                y = sel.Top - gap - h;
                if (y < mon.Top + 2) y = sel.Bottom - h - gap;
            }
            x = Math.Max(mon.Left + 2, Math.Min(x, mon.Right - w - 2));
            y = Math.Max(mon.Top + 2, Math.Min(y, mon.Bottom - h - 2));
            toolbarRect = new Rectangle(x, y, w, h);

            int cx = x + pad;
            foreach (var b in buttons)
            {
                if (b.SeparatorBefore) cx += sep;
                b.Rect = new Rectangle(cx, y + pad, cell, cell);
                cx += cell;
            }
        }

        Rectangle TooltipRect()
        {
            if (hover == null || !ToolbarVisible) return Rectangle.Empty;
            var sz = TextRenderer.MeasureText(hover.Tip, uiFont);
            int w = sz.Width + S(14), h = sz.Height + S(8);
            var mon = MonitorRect(hover.Rect.Location);
            int x = hover.Rect.X + hover.Rect.Width / 2 - w / 2;
            int y = toolbarRect.Top - h - S(6);
            if (y < mon.Top) y = toolbarRect.Bottom + S(6);
            x = Math.Max(mon.Left + 2, Math.Min(x, mon.Right - w - 2));
            return new Rectangle(x, y, w, h);
        }

        string SizeText()
        {
            return sel.Width + " × " + sel.Height;
        }

        Rectangle SizeLabelRect()
        {
            var sz = TextRenderer.MeasureText(SizeText(), uiFont);
            int w = sz.Width + S(10), h = sz.Height + S(4);
            int x = sel.X, y = sel.Y - h - S(4);
            if (y < MonitorRect(sel.Location).Top)
            {
                x = sel.X + S(4);
                y = sel.Y + S(4);
            }
            return new Rectangle(x, y, w, h);
        }

        Rectangle HintRect()
        {
            var mon = MonitorRect(PointToClient(Cursor.Position));
            var sz = TextRenderer.MeasureText(HintText, uiFont);
            int w = sz.Width + S(28), h = sz.Height + S(16);
            return new Rectangle(mon.X + (mon.Width - w) / 2, mon.Y + S(40), w, h);
        }

        Rectangle VisualBounds()
        {
            if (!hasSel) return HintRect();
            var r = Rectangle.Inflate(sel, S(12), S(12));
            r = Util.Union(r, Rectangle.Inflate(SizeLabelRect(), 2, 2));
            if (ToolbarVisible)
            {
                r = Util.Union(r, Rectangle.Inflate(toolbarRect, 2, 2));
                var t = TooltipRect();
                if (!t.IsEmpty) r = Util.Union(r, Rectangle.Inflate(t, 2, 2));
            }
            return r;
        }

        // Yalnızca değişen bölgeyi yeniden çizer; 4K ekranda bile akıcı kalsın diye.
        void UpdateView()
        {
            if (IsDisposed) return;
            LayoutToolbar();
            var nb = VisualBounds();
            Invalidate(Util.Union(lastVisual, nb));
            lastVisual = nb;
        }

        Point HandlePoint(int i)
        {
            int l = sel.Left, t = sel.Top, r = sel.Right, b = sel.Bottom;
            int mx = l + sel.Width / 2, my = t + sel.Height / 2;
            switch (i)
            {
                case 0: return new Point(l, t);
                case 1: return new Point(mx, t);
                case 2: return new Point(r, t);
                case 3: return new Point(r, my);
                case 4: return new Point(r, b);
                case 5: return new Point(mx, b);
                case 6: return new Point(l, b);
                default: return new Point(l, my);
            }
        }

        int HitHandle(Point p)
        {
            int tol = S(7);
            for (int i = 0; i < 8; i++)
            {
                var h = HandlePoint(i);
                if (Math.Abs(p.X - h.X) <= tol && Math.Abs(p.Y - h.Y) <= tol) return i;
            }
            return -1;
        }

        ToolButton HitButton(Point p)
        {
            foreach (var b in buttons)
                if (b.Rect.Contains(p)) return b;
            return null;
        }

        Cursor CursorFor(Point p)
        {
            if (!hasSel) return Cursors.Cross;
            if (toolbarRect.Contains(p)) return Cursors.Hand;
            if (tool == Tool.Select)
            {
                switch (HitHandle(p))
                {
                    case 0: case 4: return Cursors.SizeNWSE;
                    case 2: case 6: return Cursors.SizeNESW;
                    case 1: case 5: return Cursors.SizeNS;
                    case 3: case 7: return Cursors.SizeWE;
                }
                return sel.Contains(p) ? Cursors.SizeAll : Cursors.Cross;
            }
            if (sel.Contains(p)) return tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            return Cursors.Default;
        }

        // ---------------------------------------------------------------- fare

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                if (editing != null) CancelText();
                else Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            var p = e.Location;
            if (ToolbarVisible && toolbarRect.Contains(p))
            {
                var b = HitButton(p);
                if (b != null) OnButton(b);
                return;
            }

            if (editing != null) CommitText();

            if (!hasSel || tool == Tool.Select)
            {
                int h = hasSel ? HitHandle(p) : -1;
                if (h >= 0)
                {
                    mode = Mode.Resizing;
                    resizeHandle = h;
                }
                else if (hasSel && sel.Contains(p))
                {
                    mode = Mode.Moving;
                }
                else
                {
                    mode = Mode.Selecting;
                    hasSel = true;
                    sel = new Rectangle(p, Size.Empty);
                    shapes.Clear();
                }
                dragStart = p;
                selStart = sel;
            }
            else
            {
                if (!sel.Contains(p)) return;
                var c = CurrentColor;
                switch (tool)
                {
                    case Tool.Text:
                        editing = new TextShape(p, c, penWidth);
                        UpdateView();
                        return;
                    case Tool.Pen: current = new FreehandShape(p, c, penWidth, false); break;
                    case Tool.Marker: current = new FreehandShape(p, c, penWidth, true); break;
                    case Tool.Line: current = new LineShape(p, c, penWidth, false); break;
                    case Tool.Arrow: current = new LineShape(p, c, penWidth, true); break;
                    case Tool.Rect: current = new RectShape(p, c, penWidth); break;
                }
                mode = Mode.Drawing;
            }
            UpdateView();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.Location;
            switch (mode)
            {
                case Mode.Selecting:
                    sel = Normalize(dragStart, Clamp(p));
                    UpdateView();
                    break;

                case Mode.Moving:
                    {
                        var r = selStart;
                        r.Offset(p.X - dragStart.X, p.Y - dragStart.Y);
                        r.X = Math.Max(0, Math.Min(r.X, ClientSize.Width - r.Width));
                        r.Y = Math.Max(0, Math.Min(r.Y, ClientSize.Height - r.Height));
                        sel = r;
                        UpdateView();
                        break;
                    }

                case Mode.Resizing:
                    sel = ResizeRect(selStart, resizeHandle, p.X - dragStart.X, p.Y - dragStart.Y);
                    UpdateView();
                    break;

                case Mode.Drawing:
                    current.Update(p, (ModifierKeys & Keys.Shift) != 0);
                    UpdateView();
                    break;

                default:
                    var b = ToolbarVisible && toolbarRect.Contains(p) ? HitButton(p) : null;
                    if (b != hover)
                    {
                        hover = b;
                        UpdateView();
                    }
                    else if (!hasSel) UpdateView(); // ipucu imlecin bulunduğu monitörü izlesin
                    var c = CursorFor(p);
                    if (Cursor != c) Cursor = c;
                    break;
            }
        }

        Rectangle ResizeRect(Rectangle r, int handle, int dx, int dy)
        {
            int l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            if (handle == 0 || handle == 6 || handle == 7) l += dx;
            if (handle == 2 || handle == 3 || handle == 4) rt += dx;
            if (handle == 0 || handle == 1 || handle == 2) t += dy;
            if (handle == 4 || handle == 5 || handle == 6) b += dy;
            return Normalize(Clamp(new Point(l, t)), Clamp(new Point(rt, b)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var m = mode;
            mode = Mode.None;

            if (m == Mode.Selecting && (sel.Width < 4 || sel.Height < 4))
                sel = MonitorRect(e.Location); // tek tıklama: o monitörün tamamı
            else if (m == Mode.Drawing && current != null)
            {
                if (!current.IsEmpty) shapes.Add(current);
                current = null;
            }
            UpdateView();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && hasSel && tool == Tool.Select && sel.Contains(e.Location)
                && !toolbarRect.Contains(e.Location))
                CopyAndClose();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            penWidth = Math.Max(1f, Math.Min(40f, penWidth + (e.Delta > 0 ? 1f : -1f)));
            if (editing != null) editing.Width = penWidth;
            UpdateView();
        }

        // ---------------------------------------------------------------- klavye

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (editing != null)
            {
                switch (keyData)
                {
                    case Keys.Escape: CancelText(); return true;
                    case Keys.Enter: CommitText(); return true;
                    case Keys.Shift | Keys.Enter: editing.Text += "\n"; UpdateView(); return true;
                    case Keys.Back:
                        if (editing.Text.Length > 0) editing.Text = editing.Text.Substring(0, editing.Text.Length - 1);
                        UpdateView();
                        return true;
                    case Keys.Control | Keys.V:
                        if (Clipboard.ContainsText()) editing.Text += Clipboard.GetText().Replace("\r\n", "\n");
                        UpdateView();
                        return true;
                    case Keys.Control | Keys.C: CommitText(); CopyAndClose(); return true;
                    case Keys.Control | Keys.S: CommitText(); SaveAs(); return true;
                    case Keys.Control | Keys.Z: CancelText(); return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Enter:
                case Keys.Control | Keys.C:
                    if (hasSel) CopyAndClose();
                    return true;
                case Keys.Control | Keys.S:
                    if (hasSel) SaveAs();
                    return true;
                case Keys.Control | Keys.Z: Undo(); return true;
                case Keys.S: SetTool(Tool.Select); return true;
                case Keys.P: SetTool(Tool.Pen); return true;
                case Keys.L: SetTool(Tool.Line); return true;
                case Keys.A: SetTool(Tool.Arrow); return true;
                case Keys.R: SetTool(Tool.Rect); return true;
                case Keys.M: SetTool(Tool.Marker); return true;
                case Keys.T: SetTool(Tool.Text); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (editing != null && !char.IsControl(e.KeyChar))
            {
                editing.Text += e.KeyChar;
                e.Handled = true;
                UpdateView();
            }
        }

        // ---------------------------------------------------------------- eylemler

        void SetTool(Tool t)
        {
            if (!hasSel) return;
            tool = t;
            UpdateView();
        }

        void OnButton(ToolButton b)
        {
            if (b.Tool.HasValue)
            {
                if (editing != null) CommitText();
                tool = b.Tool.Value;
            }
            else if (b.ColorIndex >= 0)
            {
                colorIndex = b.ColorIndex;
                if (editing != null) editing.Color = CurrentColor;
            }
            else
            {
                switch (b.Id)
                {
                    case "width":
                        penWidth = NextWidth();
                        if (editing != null) editing.Width = penWidth;
                        break;
                    case "undo": Undo(); break;
                    case "save": SaveAs(); return;
                    case "copy": CopyAndClose(); return;
                    case "close": Close(); return;
                }
            }
            UpdateView();
        }

        float NextWidth()
        {
            foreach (float w in Widths)
                if (w > penWidth) return w;
            return Widths[0];
        }

        void Undo()
        {
            if (editing != null) CancelText();
            else if (shapes.Count > 0) shapes.RemoveAt(shapes.Count - 1);
            UpdateView();
        }

        void CommitText()
        {
            if (editing == null) return;
            if (!editing.IsEmpty) shapes.Add(editing);
            editing = null;
            UpdateView();
        }

        void CancelText()
        {
            editing = null;
            UpdateView();
        }

        Bitmap RenderResult()
        {
            var bmp = new Bitmap(sel.Width, sel.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(shot, new Rectangle(0, 0, sel.Width, sel.Height), sel, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                g.InterpolationMode = InterpolationMode.Default;
                g.PixelOffsetMode = PixelOffsetMode.Default;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform(-sel.X, -sel.Y);
                foreach (var s in shapes) s.Draw(g);
            }
            return bmp;
        }

        void CopyAndClose()
        {
            if (!hasSel || sel.Width < 1 || sel.Height < 1) return;
            CommitText();
            try
            {
                using (var b = RenderResult())
                    Clipboard.SetDataObject(b, true, 10, 100);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Panoya kopyalanamadı: " + ex.Message, "Kıskaç", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Close();
        }

        void SaveAs()
        {
            if (!hasSel || sel.Width < 1 || sel.Height < 1) return;
            CommitText();
            string baseName = "Kıskaç_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path;
            if (Settings.QuickSave)
            {
                // Sormadan kayıt klasörüne; aynı adlı dosya varsa numara eklenir.
                string dir = Settings.SaveDir, ext = Settings.FormatExtension;
                path = Path.Combine(dir, baseName + ext);
                for (int n = 2; File.Exists(path); n++)
                    path = Path.Combine(dir, baseName + " (" + n + ")" + ext);
            }
            else
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = "Ekran görüntüsünü kaydet";
                    dlg.Filter = "PNG resmi (*.png)|*.png|JPEG resmi (*.jpg)|*.jpg|BMP resmi (*.bmp)|*.bmp";
                    dlg.FilterIndex = Settings.Format + 1;
                    dlg.FileName = baseName + Settings.FormatExtension;
                    dlg.InitialDirectory = Settings.SaveDir;
                    dlg.AddExtension = true;
                    dlg.OverwritePrompt = true;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    path = dlg.FileName;
                }
            }

            try
            {
                using (var b = RenderResult())
                {
                    Util.SaveImage(b, path);
                    if (Settings.CopyAfterSave) Clipboard.SetDataObject(b, true, 10, 100);
                }
                if (!Settings.QuickSave) Settings.SaveDir = Path.GetDirectoryName(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Kaydedilemedi: " + ex.Message, "Kıskaç", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Close();
        }

        // ---------------------------------------------------------------- çizim

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;

            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(dimmed, clip, clip, GraphicsUnit.Pixel);
            if (hasSel)
            {
                var r = Rectangle.Intersect(sel, clip);
                if (r.Width > 0 && r.Height > 0) g.DrawImage(shot, r, r, GraphicsUnit.Pixel);
            }
            g.CompositingMode = CompositingMode.SourceOver;
            g.InterpolationMode = InterpolationMode.Default;
            g.PixelOffsetMode = PixelOffsetMode.Default;

            if (!hasSel)
            {
                DrawHint(g);
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var state = g.Save();
            g.SetClip(sel, CombineMode.Intersect);
            foreach (var s in shapes) s.Draw(g);
            if (current != null) current.Draw(g);
            if (editing != null) editing.DrawEditing(g);
            g.Restore(state);

            g.SmoothingMode = SmoothingMode.None;
            DrawFrame(g);
            DrawSizeLabel(g);
            if (ToolbarVisible) DrawToolbar(g);
        }

        void DrawFrame(Graphics g)
        {
            var r = new Rectangle(sel.X - 1, sel.Y - 1, sel.Width + 1, sel.Height + 1);
            using (var white = new Pen(Color.White, 1))
                g.DrawRectangle(white, r);
            using (var dash = new Pen(Accent, 1))
            {
                dash.DashStyle = DashStyle.Dash;
                g.DrawRectangle(dash, r);
            }

            if (tool != Tool.Select || mode == Mode.Selecting) return;
            int hs = S(8);
            using (var fill = new SolidBrush(Color.White))
            using (var border = new Pen(Accent, 1))
                for (int i = 0; i < 8; i++)
                {
                    var p = HandlePoint(i);
                    var hr = new Rectangle(p.X - hs / 2, p.Y - hs / 2, hs, hs);
                    g.FillRectangle(fill, hr);
                    g.DrawRectangle(border, hr);
                }
        }

        void DrawPill(Graphics g, Rectangle r, int alpha, string text, TextFormatFlags extra)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Util.RoundRect(r, S(5)))
            using (var br = new SolidBrush(Color.FromArgb(alpha, 25, 25, 28)))
                g.FillPath(br, path);
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, text, uiFont, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | extra);
        }

        void DrawHint(Graphics g)
        {
            if (!Settings.ShowHint) return;
            DrawPill(g, HintRect(), 225, HintText, TextFormatFlags.Default);
        }

        void DrawSizeLabel(Graphics g)
        {
            DrawPill(g, SizeLabelRect(), 210, SizeText(), TextFormatFlags.Default);
        }

        void DrawToolbar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Util.RoundRect(toolbarRect, S(8)))
            using (var br = new SolidBrush(Color.FromArgb(245, 32, 32, 36)))
            using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255)))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }

            using (var sepPen = new Pen(Color.FromArgb(60, 255, 255, 255)))
            using (var hoverBrush = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
            using (var activeBrush = new SolidBrush(Accent))
                foreach (var b in buttons)
                {
                    if (b.SeparatorBefore)
                    {
                        int sx = b.Rect.X - S(5);
                        g.DrawLine(sepPen, sx, b.Rect.Top + S(6), sx, b.Rect.Bottom - S(6));
                    }
                    bool active = b.Tool.HasValue && b.Tool.Value == tool;
                    if (active || b == hover)
                        using (var bp = Util.RoundRect(Rectangle.Inflate(b.Rect, -S(2), -S(2)), S(5)))
                            g.FillPath(active ? activeBrush : hoverBrush, bp);
                    DrawButtonIcon(g, b);
                }

            var tip = TooltipRect();
            if (!tip.IsEmpty) DrawPill(g, tip, 240, hover.Tip, TextFormatFlags.Default);
        }

        static PointF P(PointF c, float u, float x, float y)
        {
            return new PointF(c.X + x * u, c.Y + y * u);
        }

        void DrawButtonIcon(Graphics g, ToolButton b)
        {
            var r = b.Rect;
            var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            float u = r.Width / 32f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var glyphFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

            using (var pen = new Pen(Color.White, 2f * u))
            using (var white = new SolidBrush(Color.White))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (b.Id)
                {
                    case "select":
                        g.FillPolygon(white, new[]
                        {
                            P(c, u, -5, -9), P(c, u, -5, 6), P(c, u, -1.5f, 2.5f), P(c, u, 1.5f, 9),
                            P(c, u, 4, 8), P(c, u, 1, 1.5f), P(c, u, 6, 1.5f)
                        });
                        break;
                    case "pen":
                        g.DrawBezier(pen, P(c, u, -8, 5), P(c, u, -3, -9), P(c, u, 2, 11), P(c, u, 8, -5));
                        break;
                    case "line":
                        g.DrawLine(pen, P(c, u, -7, 7), P(c, u, 7, -7));
                        break;
                    case "arrow":
                        g.DrawLine(pen, P(c, u, -7, 7), P(c, u, 3, -3));
                        g.FillPolygon(white, new[] { P(c, u, 8, -8), P(c, u, -1, -6), P(c, u, 6, 1) });
                        break;
                    case "rect":
                        pen.LineJoin = LineJoin.Miter;
                        g.DrawRectangle(pen, c.X - 8 * u, c.Y - 6 * u, 16 * u, 12 * u);
                        break;
                    case "marker":
                        using (var hl = new SolidBrush(Color.FromArgb(200, 255, 215, 0)))
                            g.FillRectangle(hl, c.X - 9 * u, c.Y - 3.5f * u, 18 * u, 7 * u);
                        break;
                    case "text":
                        TextRenderer.DrawText(g, "T", letterFont, r, Color.White, glyphFlags);
                        break;
                    case "color":
                        {
                            float d = 15 * u;
                            if (b.ColorIndex == colorIndex)
                                using (var ring = new Pen(Color.White, 2f * u))
                                    g.DrawEllipse(ring, c.X - 11 * u, c.Y - 11 * u, 22 * u, 22 * u);
                            using (var cb = new SolidBrush(Palette[b.ColorIndex]))
                                g.FillEllipse(cb, c.X - d / 2, c.Y - d / 2, d, d);
                            using (var edge = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
                                g.DrawEllipse(edge, c.X - d / 2, c.Y - d / 2, d, d);
                            break;
                        }
                    case "width":
                        {
                            float d = Math.Min(4 + penWidth * 1.1f, 20) * u;
                            using (var cb = new SolidBrush(CurrentColor))
                                g.FillEllipse(cb, c.X - d / 2, c.Y - d / 2, d, d);
                            using (var edge = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                                g.DrawEllipse(edge, c.X - d / 2, c.Y - d / 2, d, d);
                            break;
                        }
                    case "undo":
                        TextRenderer.DrawText(g, "", glyphFont, r, Color.White, glyphFlags);
                        break;
                    case "save":
                        TextRenderer.DrawText(g, "", glyphFont, r, Color.White, glyphFlags);
                        break;
                    case "copy":
                        TextRenderer.DrawText(g, "", glyphFont, r, Color.White, glyphFlags);
                        break;
                    case "close":
                        TextRenderer.DrawText(g, "", glyphFont, r, Color.White, glyphFlags);
                        break;
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (Settings.RememberStyle)
            {
                Settings.LastColor = colorIndex;
                Settings.LastWidth = (int)Math.Round(penWidth);
            }
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dimmed.Dispose();
                if (uiFont != null) uiFont.Dispose();
                if (glyphFont != null) glyphFont.Dispose();
                if (letterFont != null) letterFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
