using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Kiskac
{
    // Tüm şekiller ekran görüntüsü bitmap'inin piksel koordinatlarında tutulur.
    abstract class Shape
    {
        public Color Color;
        public float Width;

        public abstract void Update(Point p, bool shift);
        public abstract void Draw(Graphics g);

        public virtual bool IsEmpty { get { return false; } }

        protected static Pen RoundPen(Color c, float w)
        {
            var p = new Pen(c, w);
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }

        protected static Point Snap45(Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double step = Math.PI / 4;
            double ang = Math.Round(Math.Atan2(dy, dx) / step) * step;
            return new Point(a.X + (int)Math.Round(Math.Cos(ang) * len), a.Y + (int)Math.Round(Math.Sin(ang) * len));
        }
    }

    sealed class FreehandShape : Shape
    {
        readonly List<Point> pts = new List<Point>();
        readonly bool marker;

        public FreehandShape(Point start, Color c, float w, bool marker)
        {
            this.marker = marker;
            Color = marker ? Color.FromArgb(110, c.R, c.G, c.B) : c;
            Width = marker ? Math.Max(w * 4f, 12f) : w;
            pts.Add(start);
        }

        public override void Update(Point p, bool shift)
        {
            if (shift)
            {
                // Shift: düz çizgi (fosforlu kalemde yatay)
                var a = pts[0];
                pts.Clear();
                pts.Add(a);
                pts.Add(marker ? new Point(p.X, a.Y) : p);
                return;
            }
            if (pts[pts.Count - 1] != p) pts.Add(p);
        }

        public override void Draw(Graphics g)
        {
            if (pts.Count == 1)
            {
                using (var b = new SolidBrush(Color))
                    g.FillEllipse(b, pts[0].X - Width / 2, pts[0].Y - Width / 2, Width, Width);
                return;
            }
            using (var pen = RoundPen(Color, Width))
                g.DrawLines(pen, pts.ToArray());
        }
    }

    sealed class LineShape : Shape
    {
        readonly Point a;
        Point b;
        readonly bool arrow;

        public LineShape(Point start, Color c, float w, bool arrow)
        {
            a = b = start;
            Color = c;
            Width = w;
            this.arrow = arrow;
        }

        public override void Update(Point p, bool shift)
        {
            b = shift ? Snap45(a, p) : p;
        }

        public override bool IsEmpty
        {
            get { return Math.Abs(b.X - a.X) < 3 && Math.Abs(b.Y - a.Y) < 3; }
        }

        public override void Draw(Graphics g)
        {
            using (var pen = RoundPen(Color, Width))
            {
                if (!arrow)
                {
                    g.DrawLine(pen, a, b);
                    return;
                }

                float dx = b.X - a.X, dy = b.Y - a.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) return;
                float ux = dx / len, uy = dy / len;
                float head = Math.Min(Math.Max(12f, Width * 4f), len);
                float half = head * 0.5f;
                float bx = b.X - ux * head, by = b.Y - uy * head;

                // Gövde ok başının içinde biter, yuvarlak uç başın dışına taşmaz.
                g.DrawLine(pen, new PointF(a.X, a.Y), new PointF(b.X - ux * head * 0.6f, b.Y - uy * head * 0.6f));
                using (var br = new SolidBrush(Color))
                    g.FillPolygon(br, new[]
                    {
                        new PointF(b.X, b.Y),
                        new PointF(bx - uy * half, by + ux * half),
                        new PointF(bx + uy * half, by - ux * half)
                    });
            }
        }
    }

    sealed class RectShape : Shape
    {
        readonly Point a;
        Point b;

        public RectShape(Point start, Color c, float w)
        {
            a = b = start;
            Color = c;
            Width = w;
        }

        public override void Update(Point p, bool shift)
        {
            if (shift)
            {
                int dx = p.X - a.X, dy = p.Y - a.Y;
                int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                b = new Point(a.X + (dx < 0 ? -d : d), a.Y + (dy < 0 ? -d : d));
            }
            else b = p;
        }

        public override bool IsEmpty
        {
            get { return Math.Abs(b.X - a.X) < 2 && Math.Abs(b.Y - a.Y) < 2; }
        }

        public override void Draw(Graphics g)
        {
            var r = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            using (var pen = new Pen(Color, Width))
            {
                pen.LineJoin = LineJoin.Miter;
                g.DrawRectangle(pen, r);
            }
        }
    }

    sealed class TextShape : Shape
    {
        readonly Point p;
        public string Text = "";

        public TextShape(Point at, Color c, float w)
        {
            p = at;
            Color = c;
            Width = w;
        }

        public override void Update(Point at, bool shift) { }

        public override bool IsEmpty
        {
            get { return Text.Trim().Length == 0; }
        }

        float FontPx
        {
            get { return 14f + Width * 2.5f; }
        }

        public override void Draw(Graphics g)
        {
            Render(g, false);
        }

        public void DrawEditing(Graphics g)
        {
            Render(g, true);
        }

        void Render(Graphics g, bool editing)
        {
            using (var font = new Font("Segoe UI", FontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color))
            using (var fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                string[] lines = Text.Split('\n');
                float lh = font.GetHeight(g);
                float x = p.X, y = p.Y - lh / 2;
                float maxW = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    g.DrawString(lines[i], font, brush, x, y + i * lh, fmt);
                    maxW = Math.Max(maxW, g.MeasureString(lines[i], font, PointF.Empty, fmt).Width);
                }

                if (!editing) return;

                float cx = x + g.MeasureString(lines[lines.Length - 1], font, PointF.Empty, fmt).Width + 1;
                float cy = y + (lines.Length - 1) * lh;
                using (var caret = new Pen(Color, Math.Max(1.5f, FontPx / 14f)))
                    g.DrawLine(caret, cx, cy + 2, cx, cy + lh - 2);
                using (var frame = new Pen(Color.FromArgb(170, 255, 255, 255), 1f))
                {
                    frame.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(frame, x - 5, y - 3, Math.Max(maxW, 8) + 12, lines.Length * lh + 6);
                }
            }
        }
    }
}
