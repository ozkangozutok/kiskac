using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Kiskac
{
    // Kıskaç logosu: mercan zemin, beyaz yengeç kıskacı, ağzında sarı bir ekran parçası.
    // Kodla çizilir; tepsi (16px) ile .exe simgesi (256px) aynı kaynaktan üretilir.
    public static class Logo
    {
        static readonly Color BgTop = Color.FromArgb(255, 122, 69);
        static readonly Color BgBottom = Color.FromArgb(226, 36, 90);
        static readonly Color Piece = Color.FromArgb(255, 214, 72);

        // 32 birimlik ızgara. Isırık çemberinin merkezi gövdenin içinde olduğu için
        // ağız kenarda daralır ve çeneler içe doğru kıvrılır: kıskaç görünümü buradan gelir.
        static readonly PointF BodyCenter = new PointF(14.5f, 17.5f);
        const float BodyRadius = 10f;
        static readonly PointF BiteCenter = new PointF(19.2f, 12.3f);
        const float BiteRadius = 5.6f;
        static readonly PointF PieceCenter = new PointF(19.6f, 11.9f);

        public static Bitmap Draw(int size)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size / 32f;
                g.ScaleTransform(s, s);

                // Zemin
                var bg = new RectangleF(1, 1, 30, 30);
                using (var path = RoundRect(bg, 7.5f))
                {
                    using (var br = new LinearGradientBrush(new PointF(1, 1), new PointF(31, 31), BgTop, BgBottom))
                        g.FillPath(br, path);
                    // üstte hafif parlama (fırça 16'dan sonra tekrarlamasın diye kırpılır)
                    var st0 = g.Save();
                    g.SetClip(new RectangleF(0, 0, 32, 15.9f), CombineMode.Intersect);
                    using (var hl = new LinearGradientBrush(new PointF(0, 0), new PointF(0, 16),
                        Color.FromArgb(50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                        g.FillPath(hl, path);
                    g.Restore(st0);
                }

                using (var claw = ClawPath())
                {
                    // kol: kıskaçtan sol alta uzanan, yuvarlak biten kısa bilek.
                    // Kıskaçla tek şekil olsun diye aynı yola eklenir (Winding: üst üste binen yer tek dolar).
                    using (var arm = new GraphicsPath())
                    using (var armPen = new Pen(Color.White, 6.5f))
                    {
                        armPen.StartCap = LineCap.Round;
                        armPen.EndCap = LineCap.Round;
                        arm.AddLine(BodyCenter, new PointF(7f, 25f));
                        arm.Widen(armPen);
                        claw.AddPath(arm, false);
                    }
                    claw.FillMode = FillMode.Winding;

                    // gölge
                    if (size >= 24)
                    {
                        var st = g.Save();
                        g.TranslateTransform(0, 1.1f);
                        using (var sh = new SolidBrush(Color.FromArgb(55, 90, 0, 30)))
                            g.FillPath(sh, claw);
                        g.Restore(st);
                    }
                    using (var white = new SolidBrush(Color.White))
                        g.FillPath(white, claw);
                }

                // Kıskacın ağzındaki ekran parçası (eğik)
                var state = g.Save();
                g.TranslateTransform(PieceCenter.X, PieceCenter.Y);
                g.RotateTransform(-12);
                float ps = size <= 20 ? 6.4f : 5.6f;
                using (var piece = RoundRect(new RectangleF(-ps / 2, -ps / 2, ps, ps), ps * 0.22f))
                {
                    using (var br = new SolidBrush(Piece))
                        g.FillPath(br, piece);
                    if (size >= 32)
                        using (var edge = new Pen(Color.FromArgb(120, 150, 60, 0), 0.5f))
                            g.DrawPath(edge, piece);
                }
                g.Restore(state);
            }
            return b;
        }

        // Büyük bir daireden ikinci bir daireyi "ısırarak" iki sivri çeneli kıskaç oluşturur.
        static GraphicsPath ClawPath()
        {
            PointF c1 = BodyCenter, c2 = BiteCenter;
            float r1 = BodyRadius, r2 = BiteRadius;
            double dx = c2.X - c1.X, dy = c2.Y - c1.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            double a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
            double h = Math.Sqrt(r1 * r1 - a * a);
            double ux = dx / d, uy = dy / d;
            var pA = new PointF((float)(c1.X + a * ux - h * uy), (float)(c1.Y + a * uy + h * ux));
            var pB = new PointF((float)(c1.X + a * ux + h * uy), (float)(c1.Y + a * uy - h * ux));

            double toBite = Deg(Math.Atan2(dy, dx));
            double toBody = Deg(Math.Atan2(-dy, -dx));

            // Gövde çemberinde, ısırık tarafını içermeyen uzun yay
            double aA = Angle(c1, pA), aB = Angle(c1, pB);
            double start = aA, sweep = Mod(aB - aA);
            if (InSweep(start, sweep, toBite))
            {
                start = aB;
                sweep = Mod(aA - aB);
            }
            var end = start == aA ? pB : pA;
            var begin = start == aA ? pA : pB;

            // Isırık çemberinde, gövdenin içinde kalan (içbükey) yay: end -> begin
            double e2 = Angle(c2, end), b2 = Angle(c2, begin);
            double cw = Mod(b2 - e2);
            double sweep2 = InSweep(e2, cw, toBody) ? cw : -(360 - cw);

            var path = new GraphicsPath();
            path.AddArc(c1.X - r1, c1.Y - r1, r1 * 2, r1 * 2, (float)start, (float)sweep);
            path.AddArc(c2.X - r2, c2.Y - r2, r2 * 2, r2 * 2, (float)e2, (float)sweep2);
            path.CloseFigure();
            return path;
        }

        static double Deg(double rad) { return rad * 180 / Math.PI; }
        static double Mod(double v) { v %= 360; return v < 0 ? v + 360 : v; }
        static double Angle(PointF c, PointF p) { return Deg(Math.Atan2(p.Y - c.Y, p.X - c.X)); }

        static bool InSweep(double start, double sweep, double angle)
        {
            return Mod(angle - start) <= sweep;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
