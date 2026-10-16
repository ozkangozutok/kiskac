using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Kiskac
{
    static class Util
    {
        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "Kiskac.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Rectangle.Union boş dikdörtgeni (0,0) noktası olarak sayar; bu sürüm saymaz.
        public static Rectangle Union(Rectangle a, Rectangle b)
        {
            if (a.Width <= 0 || a.Height <= 0) return b;
            if (b.Width <= 0 || b.Height <= 0) return a;
            return Rectangle.Union(a, b);
        }

        public static Bitmap CaptureScreen(Rectangle bounds)
        {
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        public static void SaveImage(Bitmap bmp, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
            {
                var codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var ps = new EncoderParameters(1))
                {
                    ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Settings.JpegQuality);
                    bmp.Save(path, codec, ps);
                }
            }
            else if (ext == ".bmp")
                bmp.Save(path, ImageFormat.Bmp);
            else
                bmp.Save(path, ImageFormat.Png);
        }
    }

    static class Icons
    {
        public static Bitmap DrawLogo(int size)
        {
            return Logo.Draw(size);
        }

        public static Icon CreateIcon(int size)
        {
            using (var b = DrawLogo(size))
            {
                IntPtr h = b.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }

        // Derleme sırasında .exe simgesi üretmek için: PNG gömülü çok boyutlu .ico
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var b = DrawLogo(s))
                using (var ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim);
                    w.Write(dim);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(pngs[i].Length);
                    w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var png in pngs) w.Write(png);
            }
        }
    }

    // Tüm ayarlar HKCU\Software\Kiskac altında tutulur.
    static class Settings
    {
        const string Key = @"Software\Kiskac";

        static object Get(string name)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key))
                return k == null ? null : k.GetValue(name);
        }

        static void Set(string name, object value)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
                k.SetValue(name, value);
        }

        static int GetInt(string name, int def)
        {
            var v = Get(name);
            return v is int ? (int)v : def;
        }

        static bool GetBool(string name, bool def)
        {
            var v = Get(name);
            return v is int ? (int)v != 0 : def;
        }

        static void SetBool(string name, bool value)
        {
            Set(name, value ? 1 : 0);
        }

        public static string SaveDir
        {
            get
            {
                var v = Get("SaveDir") as string;
                if (string.IsNullOrEmpty(v) || !Directory.Exists(v))
                {
                    v = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Kıskaç");
                    Directory.CreateDirectory(v);
                }
                return v;
            }
            set { Set("SaveDir", value); }
        }

        public static bool Initialized
        {
            get { return Get("Initialized") != null; }
            set { Set("Initialized", 1); }
        }

        public static HotKey HotKey
        {
            get { return HotKey.Parse(Get("HotKey") as string); }
            set { Set("HotKey", value.ToString()); }
        }

        // 15–75 (yüzde)
        public static int DimPercent
        {
            get { return Math.Max(15, Math.Min(75, GetInt("DimPercent", 45))); }
            set { Set("DimPercent", value); }
        }

        public static bool ShowHint { get { return GetBool("ShowHint", true); } set { SetBool("ShowHint", value); } }
        public static bool RememberStyle { get { return GetBool("RememberStyle", true); } set { SetBool("RememberStyle", value); } }
        public static int LastColor { get { return GetInt("LastColor", 0); } set { Set("LastColor", value); } }
        public static int LastWidth { get { return GetInt("LastWidth", 3); } set { Set("LastWidth", value); } }

        // 0: PNG, 1: JPEG, 2: BMP
        public static int Format
        {
            get { return Math.Max(0, Math.Min(2, GetInt("Format", 0))); }
            set { Set("Format", value); }
        }

        public static string FormatExtension
        {
            get { return Format == 1 ? ".jpg" : Format == 2 ? ".bmp" : ".png"; }
        }

        // 50–100
        public static int JpegQuality
        {
            get { return Math.Max(50, Math.Min(100, GetInt("JpegQuality", 92))); }
            set { Set("JpegQuality", value); }
        }

        public static bool QuickSave { get { return GetBool("QuickSave", false); } set { SetBool("QuickSave", value); } }
        public static bool CopyAfterSave { get { return GetBool("CopyAfterSave", false); } set { SetBool("CopyAfterSave", value); } }
        public static bool AutoUpdate { get { return GetBool("AutoUpdate", true); } set { SetBool("AutoUpdate", value); } }

        public static DateTime LastUpdateCheck
        {
            get
            {
                long t;
                var v = Get("LastUpdateCheck") as string;
                return v != null && long.TryParse(v, out t) ? DateTime.FromBinary(t) : DateTime.MinValue;
            }
            set { Set("LastUpdateCheck", value.ToBinary().ToString()); }
        }

        public static string SkippedVersion
        {
            get { return Get("SkippedVersion") as string; }
            set { Set("SkippedVersion", value ?? ""); }
        }
    }

    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Kıskaç";

        public static bool IsEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(Name) != null;
        }

        public static void Set(bool enabled)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(Name, false);
            }
        }
    }
}
