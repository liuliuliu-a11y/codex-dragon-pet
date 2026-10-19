using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexDragonLink
{
    internal static class Program
    {
        private delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int obj, int child, uint thread, uint time);
        private static readonly WinEvent Callback = OnForeground;
        private static int lastHost;
        private static IntPtr lastHostWindow;
        private static readonly int Session = Process.GetCurrentProcess().SessionId;
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Message
        { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Position; public uint Private; }
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);

        [STAThread]
        private static int Main(string[] args)
        {
            bool first;
            if (args.Length > 0 && args[0] == "--probe")
            {
                bool found = FindHost();
                if (args.Length > 1) File.WriteAllText(args[1], "{\"hostFound\":" + (found ? "true" : "false") + ",\"petStartRequested\":" + (found ? "true" : "false") + "}");
                return found ? 0 : 2;
            }
            using (var mutex = new Mutex(true, @"Local\CodexDragonPetWatcher", out first))
            {
                if (!first) return 0;
                // Foreground activation events avoid a polling loop and do not inspect window text or input.
                IntPtr hook = SetWinEventHook(3, 3, IntPtr.Zero, Callback, 0, 0, 2);
                if (hook == IntPtr.Zero) return 1;
                try
                {
                    FindHost();
                    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDragonPet");
                    Directory.CreateDirectory(data);
                    File.WriteAllText(Path.Combine(data, "link-status.json"), "{\"ready\":true,\"eventDriven\":true}");
                    Message message; int result;
                    while ((result = GetMessage(out message, IntPtr.Zero, 0, 0)) > 0)
                    { TranslateMessage(ref message); DispatchMessage(ref message); }
                    return result == -1 ? 1 : 0;
                }
                finally { UnhookWinEvent(hook); mutex.ReleaseMutex(); }
            }
        }
        private static bool IsHost(Process process)
        {
            string name = process.ProcessName;
            return process.SessionId == Session && (name.Equals("Codex", StringComparison.OrdinalIgnoreCase) || name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase));
        }
        private static bool FindHost()
        {
            foreach (string name in new[] { "ChatGPT", "Codex" })
                foreach (var process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        try
                        {
                            if (IsHost(process) && process.MainWindowHandle != IntPtr.Zero)
                            { lastHost = process.Id; lastHostWindow = process.MainWindowHandle; StartPet(); return true; }
                        }
                        catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                    }
            return false;
        }
        private static void OnForeground(IntPtr hook, uint kind, IntPtr window, int obj, int child, uint thread, uint time)
        {
            try
            {
                uint id; GetWindowThreadProcessId(window, out id);
                using (var process = Process.GetProcessById((int)id))
                {
                    if (IsHost(process) && process.MainWindowHandle != IntPtr.Zero)
                    {
                        // A desktop host can reuse its process for a newly opened window.
                        if (lastHost != process.Id || lastHostWindow != window)
                        { lastHost = process.Id; lastHostWindow = window; StartPet(); }
                        return;
                    }
                }
                if (lastHost != 0)
                {
                    try { using (var previous = Process.GetProcessById(lastHost)) if (previous.HasExited || previous.MainWindowHandle == IntPtr.Zero) lastHost = 0; }
                    catch (ArgumentException) { lastHost = 0; }
                }
            }
            catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
        private static void StartPet()
        {
            foreach (var process in Process.GetProcessesByName("CodexDragonPet"))
                using (process) { try { if (process.SessionId == Session) return; } catch (InvalidOperationException) { } }
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CodexDragonPet.exe");
            if (File.Exists(executable)) Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
        }
    }
}
