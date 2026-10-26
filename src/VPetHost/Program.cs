using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace CodexVPet;

internal static class Program
{
    internal static readonly string DataRoot = ResolveDataRoot();
    private static string ResolveDataRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("CODEX_WHITE_DRAGON_DATA_DIR");
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? Path.GetFullPath(configured)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDragonPet");
    }
    [STAThread]
    public static int Main(string[] args)
    {
        bool demo = Array.IndexOf(args, "--demo") >= 0;
        string? evidence = Value(args, "--verify");
        string profile = demo && evidence != null ? Path.Combine(Path.GetFullPath(evidence), "profile") : DataRoot;
        Directory.CreateDirectory(profile);
        try
        {
            using var mutex = new Mutex(true, demo ? "Local\\CodexVPetDemo" : "Local\\CodexVPet", out bool first);
            using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, demo ? "Local\\CodexVPetDemoWake" : "Local\\CodexVPetWake");
            if (!first) { wake.Set(); return 0; }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (_, e) =>
            {
                File.AppendAllText(Path.Combine(profile, "vpet-error.log"), DateTime.UtcNow.ToString("o") + " " + e.Exception + Environment.NewLine);
                // Failing visibly is preferable to a half-running invisible companion.
                app.Shutdown(1); e.Handled = true;
            };
            foreach (string name in new[] { "Theme.xaml", "basestyle.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/VPet-Simulator.Core;component/Display/" + name) });
            var window = new CompanionWindow(profile, demo, evidence);
            var registration = ThreadPool.RegisterWaitForSingleObject(wake, (_, _) =>
            {
                if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(new Action(window.ShowAwake));
            }, null, Timeout.Infinite, false);
            try { int result = app.Run(window); return result != 0 ? result : Environment.ExitCode; }
            finally { registration.Unregister(null); mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(profile, "vpet-error.log"), DateTime.UtcNow.ToString("o") + " " + ex + Environment.NewLine);
            if (!demo) MessageBox.Show("桌宠启动失败。诊断记录：\n" + Path.Combine(profile, "vpet-error.log"), "Codex 桌面伙伴");
            return 1;
        }
    }
    private static string? Value(string[] args, string key)
    { int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
}
