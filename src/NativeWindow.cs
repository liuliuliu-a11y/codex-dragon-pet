using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexDragon
{
    public static class NativeWindow
    {
        private const int ExtendedStyle = -20;
        private const long Transparent = 0x20;
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetStyle32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetStyle64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetStyle32(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetStyle64(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
        private static extern void ClearError(uint error);
        private static long Style(IntPtr window)
        { return IntPtr.Size == 8 ? GetStyle64(window, ExtendedStyle).ToInt64() : GetStyle32(window, ExtendedStyle); }
        public static bool IsClickThrough(IntPtr window)
        { return window != IntPtr.Zero && (Style(window) & Transparent) != 0; }
        public static void SetClickThrough(IntPtr window, bool enabled)
        {
            if (window == IntPtr.Zero) return;
            long style = Style(window);
            long changed = enabled ? style | Transparent : style & ~Transparent;
            ClearError(0);
            IntPtr previous = IntPtr.Size == 8 ? SetStyle64(window, ExtendedStyle, new IntPtr(changed)) : new IntPtr(SetStyle32(window, ExtendedStyle, (int)changed));
            int error = Marshal.GetLastWin32Error();
            if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error);
            // Refresh only this pet's style, preserving size, position, z-order and focus.
            if (!SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0037)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
