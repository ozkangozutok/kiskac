using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kiskac
{
    // Ekran görüntüsü kısayolunu sistem genelinde yakalar. Kanca kendi iş parçacığında çalışır;
    // böylece arayüz meşgulken bile klavye gecikmez ve Windows kancayı düşürmez.
    sealed class KeyboardHook : IDisposable
    {
        readonly Action onHotKey;
        readonly Native.LowLevelKeyboardProc proc;
        readonly Thread thread;
        IntPtr hook;
        uint threadId;
        volatile object hotKey; // kutulanmış HotKey: kanca iş parçacığı kilitsiz okur
        volatile Action<HotKey> recorder;
        volatile Action videoAction;
        bool pressed;

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
            var ready = new ManualResetEvent(false);
            thread = new Thread(() =>
            {
                threadId = Native.GetCurrentThreadId();
                hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
                Installed = hook != IntPtr.Zero;
                ready.Set();

                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
                if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            });
            thread.IsBackground = true;
            thread.Name = "Kıskaç klavye kancası";
            thread.Start();
            ready.WaitOne(3000);
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
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
                    // Değiştiriciler birebir tutmalı: PrtSc seçiliyken Alt+PrtSc, Win+PrtSc Windows'a kalır.
                    // Aynı tuş + Shift tam ekran video kaydını başlatır/durdurur.
                    Action action = null;
                    if (down)
                    {
                        var mods = HotKey.CurrentMods();
                        var video = hk.VideoVariant;
                        if (mods == hk.Mods) action = onHotKey;
                        else if (video.HasValue && mods == video.Value.Mods) action = videoAction;
                    }
                    if (action != null)
                    {
                        if (!pressed)
                        {
                            pressed = true; // basılı tutunca tekrar tekrar tetiklenmesin
                            Util.Log("Kısayol yakalandı: " + (action == onHotKey ? hk.Display : "tam ekran video"));
                            try { action(); }
                            catch { }
                        }
                        return (IntPtr)1; // tuşu yut: Ekran Alıntısı Aracı vb. tetiklenmesin
                    }
                    if (!down && pressed)
                    {
                        pressed = false;
                        return (IntPtr)1;
                    }
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
