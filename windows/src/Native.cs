using System;
using System.Runtime.InteropServices;

namespace Kiskac
{
    static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_QUIT = 0x0012;
        public const int WM_DPICHANGED = 0x02E0;
        public const int VK_SNAPSHOT = 0x2C;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vk);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ToUnicode(uint vk, uint scan, byte[] state,
            [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder buf, int bufSize, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hWnd);

        public static bool IsKeyDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        // Windows normalde arka plandaki bir uygulamanın öne gelmesine izin vermez;
        // ön plandaki pencerenin girdi kuyruğuna geçici olarak bağlanarak odak alınır.
        public static void ForceForeground(IntPtr hWnd)
        {
            IntPtr fg = GetForegroundWindow();
            uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, IntPtr.Zero);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            if (attached) AttachThreadInput(me, fgThread, false);
        }

        public static float DpiScale(IntPtr hWnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hWnd);
                if (dpi > 0) return dpi / 96f;
            }
            catch (EntryPointNotFoundException) { }
            return 1f;
        }
    }
}
