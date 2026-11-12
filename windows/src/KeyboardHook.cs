using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kiskac
{
    // Ekran görüntüsü kısayolunu sistem genelinde yakalar. Kanca kendi iş parçacığında çalışır;
    // böylece arayüz meşgulken bile klavye gecikmez ve Windows kancayı düşürmez.
    //
    // Windows, geri çağrısı LowLevelHooksTimeout süresini (≈1 sn) aşan bir alt düzey kancayı hiçbir
    // bildirim yapmadan kaldırır. .NET'te büyük bir çöp toplama, uyku/kilit sonrası diske düşmüş
    // sayfalar ya da yavaş bir disk bunu tetikleyebilir ve PrtSc uygulama yeniden başlayana kadar
    // ölür. Bu yüzden kanca düzenli aralıklarla ve uyanma/kilit açma gibi anlarda yeniden kurulur.
    sealed class KeyboardHook : IDisposable
    {
        const int WM_REINSTALL = Native.WM_APP + 1;
        const uint ReinstallIntervalMs = 60 * 1000;

        readonly Action onHotKey;
        readonly Native.LowLevelKeyboardProc proc;
        readonly Thread thread;
        readonly uint repeatGapMs;
        IntPtr hook;
        uint threadId;
        volatile object hotKey; // kutulanmış HotKey: kanca iş parçacığı kilitsiz okur
        volatile Action<HotKey> recorder;
        volatile Action videoAction;
        bool pressed;
        uint lastDownTime;

        // Kısayol + Shift basılınca çağrılır (kanca iş parçacığında).
        public Action VideoAction
        {
            get { return videoAction; }
            set { videoAction = value; }
        }

        public bool Installed { get; private set; }

        public HotKey HotKey
        {
            get { return (HotKey)hotKey; }
            set { hotKey = value; }
        }

        // Ayarlar penceresi kısayol kaydederken: değiştirici olmayan ilk tuş buraya gelir ve yutulur.
        // Çağrı kanca iş parçacığındadır.
        public Action<HotKey> Recorder
        {
            get { return recorder; }
            set { recorder = value; }
        }

        public KeyboardHook(HotKey key, Action onHotKey)
        {
            hotKey = key;
            this.onHotKey = onHotKey;
            proc = Callback;
            // Basılı tutulan tuşun ilk tekrarı en geç bu kadar sonra gelir; daha uzun bir aradan sonra
            // gelen "basıldı" yeni bir basıştır (arada bırakma olayı kaçmış olsa bile).
            int delay = 1;
            try { delay = System.Windows.Forms.SystemInformation.KeyboardDelay; } catch { }
            repeatGapMs = (uint)((Math.Max(0, Math.Min(3, delay)) + 1) * 250 + 300);

            var ready = new ManualResetEvent(false);
            thread = new Thread(() =>
            {
                threadId = Native.GetCurrentThreadId();
                Install(null);
                ready.Set();
                Native.SetTimer(IntPtr.Zero, UIntPtr.Zero, ReinstallIntervalMs, IntPtr.Zero);

                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.hwnd == IntPtr.Zero && (msg.message == Native.WM_TIMER || msg.message == WM_REINSTALL))
                    {
                        string reason = null;
                        if (msg.message == WM_REINSTALL && msg.lParam != IntPtr.Zero)
                        {
                            var h = GCHandle.FromIntPtr(msg.lParam);
                            reason = h.Target as string;
                            h.Free();
                        }
                        Install(reason);
                        continue;
                    }
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
                if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest; // yük altında zaman aşımına düşmesin
            thread.Name = "Kıskaç klavye kancası";
            thread.Start();
            ready.WaitOne(3000);
        }

        // Kanca iş parçacığında çalışır: yenisini kurar, eskisini kaldırır. Windows eskisini sessizce
        // düşürmüş olsa bile kısayol böylece geri gelir.
        void Install(string reason)
        {
            var fresh = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
            if (fresh == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                LogAsync("Kanca kurulamadı (hata " + err + ")" + (reason != null ? " — " + reason : ""));
                if (hook == IntPtr.Zero) Installed = false;
                return;
            }
            var old = hook;
            hook = fresh;
            Installed = true;
            pressed = false;
            if (old != IntPtr.Zero) Native.UnhookWindowsHookEx(old);
            if (reason != null) LogAsync("Kanca yenilendi: " + reason);
        }

        // Kancayı yeniden kurdurur (her iş parçacığından çağrılabilir). reason null ise günlüğe yazılmaz.
        public void Reinstall(string reason)
        {
            if (threadId == 0) return;
            IntPtr lp = reason != null ? GCHandle.ToIntPtr(GCHandle.Alloc(reason)) : IntPtr.Zero;
            if (!Native.PostThreadMessage(threadId, WM_REINSTALL, IntPtr.Zero, lp) && lp != IntPtr.Zero)
                GCHandle.FromIntPtr(lp).Free();
        }

        // Geri çağrı içinde diske yazılmaz: yavaş bir disk kancanın düşürülmesine yol açabilir.
        static void LogAsync(string msg)
        {
            ThreadPool.QueueUserWorkItem(_ => Util.Log(msg));
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                uint time = (uint)Marshal.ReadInt32(lParam, 12); // KBDLLHOOKSTRUCT.time
                int msg = wParam.ToInt32();
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;

                var rec = recorder;
                if (rec != null)
                {
                    if (HotKey.IsModifierKey(vk)) return Native.CallNextHookEx(hook, nCode, wParam, lParam);
                    if (down)
                    {
                        try { rec(new HotKey(vk, HotKey.CurrentMods())); }
                        catch { }
                    }
                    return (IntPtr)1;
                }

                var hk = (HotKey)hotKey;
                if (vk == hk.Vk)
                {
                    // Bırakma olayı bir şekilde kaçtıysa "basılı" işareti takılı kalıp sonraki basışı yutmasın:
                    // otomatik tekrardan daha uzun bir aradan sonra gelen basış yeni sayılır.
                    bool freshPress = down && unchecked(time - lastDownTime) > repeatGapMs;
                    if (down)
                    {
                        if (freshPress) pressed = false;
                        lastDownTime = time;
                    }

                    // Değiştiriciler birebir tutmalı: PrtSc seçiliyken Alt+PrtSc, Win+PrtSc Windows'a kalır.
                    // Aynı tuş + Shift tam ekran video kaydını başlatır/durdurur.
                    Action action = null;
                    Mods mods = Mods.None;
                    if (down)
                    {
                        mods = HotKey.CurrentMods();
                        var video = hk.VideoVariant;
                        if (mods == hk.Mods) action = onHotKey;
                        else if (video.HasValue && mods == video.Value.Mods) action = videoAction;
                    }
                    if (action != null)
                    {
                        if (!pressed)
                        {
                            pressed = true; // basılı tutunca tekrar tekrar tetiklenmesin
                            LogAsync("Kısayol yakalandı: " + (action == onHotKey ? hk.Display : "tam ekran video"));
                            try { action(); }
                            catch { }
                        }
                        return (IntPtr)1; // tuşu yut: Ekran Alıntısı Aracı vb. tetiklenmesin
                    }
                    if (freshPress)
                        LogAsync("Kısayol tuşu geldi ama değiştiriciler uymadı: " + mods);
                    if (!down && pressed)
                    {
                        pressed = false;
                        return (IntPtr)1;
                    }
                }
                else if (down && pressed && !HotKey.IsModifierKey(vk))
                {
                    pressed = false; // başka bir tuşa basıldı: kısayol tuşu artık basılı değil
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
