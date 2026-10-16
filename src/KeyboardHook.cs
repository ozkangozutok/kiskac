using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kiskac
{
    // PrtSc tuşunu sistem genelinde yakalar. Kanca kendi iş parçacığında çalışır;
    // böylece arayüz meşgulken bile klavye gecikmez ve Windows kancayı düşürmez.
    sealed class KeyboardHook : IDisposable
    {
        readonly Action onPrintScreen;
        readonly Native.LowLevelKeyboardProc proc;
        readonly Thread thread;
        IntPtr hook;
        uint threadId;

        public bool Installed { get; private set; }

        public KeyboardHook(Action onPrintScreen)
        {
            this.onPrintScreen = onPrintScreen;
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
                // Alt+PrtSc, Win+PrtSc gibi kombinasyonlar Windows'a bırakılır.
                if (vk == Native.VK_SNAPSHOT && !ModifierDown())
                {
                    int msg = wParam.ToInt32();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        Util.Log("PrtSc yakalandı");
                        try { onPrintScreen(); }
                        catch { }
                    }
                    return (IntPtr)1; // tuşu yut: Ekran Alıntısı Aracı / Lightshot tetiklenmesin
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        static bool ModifierDown()
        {
            return Native.IsKeyDown(0x10) || Native.IsKeyDown(0x11) || Native.IsKeyDown(0x12)
                || Native.IsKeyDown(0x5B) || Native.IsKeyDown(0x5C);
        }

        public void Dispose()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
