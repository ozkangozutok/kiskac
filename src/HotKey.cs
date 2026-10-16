using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Kiskac
{
    [Flags]
    enum Mods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

    // Sanal tuş kodu + değiştiriciler. Kayıt defterinde "vk,mods" olarak tutulur.
    struct HotKey : IEquatable<HotKey>
    {
        public readonly int Vk;
        public readonly Mods Mods;

        public static readonly HotKey Default = new HotKey(Native.VK_SNAPSHOT, Mods.None);

        public HotKey(int vk, Mods mods)
        {
            Vk = vk;
            Mods = mods;
        }

        public static Mods CurrentMods()
        {
            var m = Mods.None;
            if (Native.IsKeyDown(0x11)) m |= Mods.Ctrl;
            if (Native.IsKeyDown(0x12)) m |= Mods.Alt;
            if (Native.IsKeyDown(0x10)) m |= Mods.Shift;
            if (Native.IsKeyDown(0x5B) || Native.IsKeyDown(0x5C)) m |= Mods.Win;
            return m;
        }

        public static bool IsModifierKey(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C || (vk >= 0xA0 && vk <= 0xA5);
        }

        // PrtSc, F1–F24, Pause ve Scroll Lock tek başına kısayol olabilir; yazmayı bozmazlar.
        bool IsStandalone
        {
            get { return Vk == Native.VK_SNAPSHOT || (Vk >= 0x70 && Vk <= 0x87) || Vk == 0x13 || Vk == 0x91; }
        }

        static readonly int[] StandardCtrlKeys =
        {
            'A', 'C', 'V', 'X', 'Z', 'Y', 'S', 'F', 'N', 'O', 'P', 'W', 'T', 'Q', 'R', 0x09, 0x20
        };

        // Kısayol kullanılamıyorsa nedenini döner.
        public string Problem
        {
            get
            {
                if (Vk == 0x1B) return "Esc kısayol olamaz.";
                if ((Mods & Mods.Win) != 0) return "Win tuşlu kısayollar Windows'a ayrılmıştır, başka bir tane seç.";
                if (IsStandalone) return null;
                if ((Mods & Mods.Ctrl) == 0)
                    return "Ctrl ile birlikte bir tuşa bas. PrtSc ve F tuşları tek başına da olur.";
                if ((Mods == Mods.Ctrl || Mods == (Mods.Ctrl | Mods.Shift)) && Array.IndexOf(StandardCtrlKeys, Vk) >= 0)
                    return Display + " uygulamalarda standart bir kısayol, başka bir tane seç.";
                if ((Mods & (Mods.Ctrl | Mods.Alt)) == (Mods.Ctrl | Mods.Alt) && (Vk == 0x2E || TypesAltGrCharacter()))
                    return Display + " bir karakter yazar (AltGr) ya da Windows'a ayrılmıştır, başka bir tane seç.";
                return null;
            }
        }

        // Ctrl+Alt = AltGr: Türkçe klavyede AltGr+Q '@' yazar; bu birleşim kısayol olursa yazılamaz.
        bool TypesAltGrCharacter()
        {
            var state = new byte[256];
            state[0x11] = 0x80;
            state[0x12] = 0x80;
            if ((Mods & Mods.Shift) != 0) state[0x10] = 0x80;
            var sb = new StringBuilder(8);
            uint scan = Native.MapVirtualKey((uint)Vk, 0);
            int n = Native.ToUnicode((uint)Vk, scan, state, sb, sb.Capacity, 0x4); // 0x4: klavye durumunu değiştirme
            return n > 0 && !char.IsControl(sb[0]);
        }

        public string KeyName
        {
            get
            {
                if (Vk == Native.VK_SNAPSHOT) return "PrtSc";
                if (Vk >= 0x70 && Vk <= 0x87) return "F" + (Vk - 0x6F);
                switch (Vk)
                {
                    case 0x20: return "Boşluk";
                    case 0x0D: return "Enter";
                    case 0x09: return "Tab";
                    case 0x08: return "Geri";
                    case 0x2E: return "Del";
                    case 0x2D: return "Ins";
                    case 0x24: return "Home";
                    case 0x23: return "End";
                    case 0x21: return "PgUp";
                    case 0x22: return "PgDn";
                    case 0x25: return "←";
                    case 0x27: return "→";
                    case 0x26: return "↑";
                    case 0x28: return "↓";
                    case 0x13: return "Pause";
                    case 0x91: return "ScrLk";
                }
                // Etkin klavye düzenindeki karşılık (Türkçe Q'da Ö, Ç, Ş…)
                uint ch = Native.MapVirtualKey((uint)Vk, 2) & 0x7FFFFFFF;
                if (ch > 0x20) return char.ToUpper((char)ch, new System.Globalization.CultureInfo("tr-TR")).ToString();
                return ((Keys)Vk).ToString();
            }
        }

        public string Display
        {
            get
            {
                var s = "";
                if ((Mods & Mods.Ctrl) != 0) s += "Ctrl+";
                if ((Mods & Mods.Alt) != 0) s += "Alt+";
                if ((Mods & Mods.Shift) != 0) s += "Shift+";
                if ((Mods & Mods.Win) != 0) s += "Win+";
                return s + KeyName;
            }
        }

        public override string ToString()
        {
            return Vk + "," + (int)Mods;
        }

        public static HotKey Parse(string s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                var p = s.Split(',');
                int vk, m;
                if (p.Length == 2 && int.TryParse(p[0], out vk) && int.TryParse(p[1], out m) && vk > 0 && vk < 256)
                    return new HotKey(vk, (Mods)m);
            }
            return Default;
        }

        public bool Equals(HotKey o) { return Vk == o.Vk && Mods == o.Mods; }
        public override bool Equals(object o) { return o is HotKey && Equals((HotKey)o); }
        public override int GetHashCode() { return Vk * 16 + (int)Mods; }
        public static bool operator ==(HotKey a, HotKey b) { return a.Equals(b); }
        public static bool operator !=(HotKey a, HotKey b) { return !a.Equals(b); }
    }
}
