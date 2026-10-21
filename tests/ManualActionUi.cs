using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CodexDragon;

public static class ManualActionUi
{
    [STAThread]
    public static int Main(string[] args)
    {
        string evidence = Path.GetFullPath(args[0]); Directory.CreateDirectory(evidence);
        string fixture = Path.Combine(evidence, "fixture"); Directory.CreateDirectory(Path.Combine(fixture, "sessions"));
        Environment.SetEnvironmentVariable("CODEX_HOME", fixture);
        Environment.SetEnvironmentVariable("CODEX_PET_DATA_DIR", Path.Combine(fixture, "pet-data"));
        new Settings { Left = 40, Top = 40, Sound = false, Notifications = false, SmallTalk = false, DailyActions = false }.Save();
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var pet = new PetWindow(true) { Title = "Codex 小龙 · 鼠标动作验收", ShowInTaskbar = true, ShowActivated = true };
        string lastSnapshot = "";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += delegate
        {
            File.WriteAllText(Path.Combine(evidence, "current.json"), pet.ManualState());
            string request = Path.Combine(evidence, "snapshot.request");
            if (File.Exists(request))
            {
                string key = File.ReadAllText(request).Trim();
                if (key != lastSnapshot) { lastSnapshot = key; pet.SaveEvidence(Path.Combine(evidence, key)); }
            }
            if (File.Exists(Path.Combine(evidence, "close.request"))) { timer.Stop(); pet.Close(); }
        };
        pet.Loaded += delegate { timer.Start(); };
        app.Run(pet); return 0;
    }
}

namespace CodexDragon
{
    public sealed partial class PetWindow
    {
        internal string ManualState()
        { return Json.Write(new { action = action.Current.ToString(), frame = action.Frame, left = Left, top = Top, visible = IsVisible, pettingCount = pettingCount, clickPending = clickTimer.IsEnabled }); }
    }
}
