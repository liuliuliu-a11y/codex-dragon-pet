using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Linq;
using System.Collections.Generic;
using CodexDragon;

// Exercises the real WPF window, file watcher, dispatcher, completion callback and renderer.
// CODEX_HOME and pet settings both point to disposable verification folders, never user sessions.
public static class UiIntegration
{
    private const string ThreadId = "00000000-0000-0000-0000-000000000123";
    private static int failures;
    private static string Record(string payload)
    { return "{\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\",\"type\":\"event_msg\",\"payload\":" + payload + "}\n"; }
    private static void Check(bool condition, string description)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + description);
        if (!condition) failures++;
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            var node = child as DependencyObject; if (node == null) continue;
            yield return node; foreach (var descendant in Children(node)) yield return descendant;
        }
    }
    private static void Toggle(CheckBox box, bool value)
    { box.IsChecked = value; box.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); }
    [STAThread]
    public static int Main(string[] args)
    {
        string evidence = Path.GetFullPath(args[0]);
        string fixture = Path.Combine(evidence, "fixture-" + Guid.NewGuid().ToString("N"));
        string sessions = Path.Combine(fixture, "sessions");
        Directory.CreateDirectory(sessions);
        Environment.SetEnvironmentVariable("CODEX_HOME", fixture);
        Environment.SetEnvironmentVariable("CODEX_PET_DATA_DIR", Path.Combine(fixture, "pet-data"));
        string file = Path.Combine(sessions, "rollout-" + ThreadId + ".jsonl");
        string otherFile = Path.Combine(sessions, "rollout-00000000-0000-0000-0000-000000000124.jsonl");
        File.WriteAllText(otherFile, Record("{\"type\":\"task_started\",\"turn_id\":\"other-turn\"}"), new UTF8Encoding(false));
        File.WriteAllText(file,
            Record("{\"type\":\"task_started\",\"turn_id\":\"ui-integration-turn\"}") +
            "{\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"exec_command\",\"arguments\":\"run tests\"}}\n" +
            Record("{\"type\":\"token_count\",\"rate_limits\":{\"limit_id\":\"codex\",\"primary\":{\"used_percent\":42,\"window_minutes\":10080}}}"),
            new UTF8Encoding(false));
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        // Keep live desktop input from renewing speech timers during the scripted test.
        // Routed events below still exercise the real character input handlers.
        var pet = new PetWindow(true) { Title = "Codex 小龙 · 隔离验收演示", IsHitTestVisible = false };
        int step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += delegate
        {
            try
            {
                step++;
                if (step == 1)
                {
                    string working = Path.Combine(evidence, "working"); pet.SaveEvidence(working);
                    var state = Json.Parse(File.ReadAllText(Path.Combine(working, "live-check.json")));
                    Check(Json.Get(state, "windowVisible").Equals(true), "native WPF pet is visible");
                    Check(Json.Get(state, "customTrayIcon").Equals(true), "custom dragon icon is loaded into the live system tray");
                    Check(Json.Get(state, "running").Equals(true) && Json.Number(state, "spriteFrame") == 1, "real work event selects the working sprite");
                    var quota = Json.Map(Json.Get(state, "quota"));
                    var windows = (object[])Json.Get(quota, "windows");
                    Check(Json.Number(Json.Map(windows[0]), "used") == 42, "quota event reaches the rendered pet state");
                    File.AppendAllText(otherFile, Record("{\"type\":\"task_complete\",\"turn_id\":\"other-turn\",\"last_agent_message\":\"Other result\"}"), new UTF8Encoding(false));
                    var character = (FrameworkElement)LogicalTreeHelper.FindLogicalNode(pet, "PetCharacterHost");
                    character.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                }
                if (step == 4)
                {
                    string other = Path.Combine(evidence, "other-chat-completed"); pet.SaveEvidence(other);
                    var state = Json.Parse(File.ReadAllText(Path.Combine(other, "live-check.json")));
                    Check(Json.Get(state, "running").Equals(true) && Json.Text(state, "trackedThreadId") == ThreadId, "other chat completion keeps the active chat selected");
                    Check(Json.Text(state, "lastCompletedThreadId") == "" && Json.Text(state, "bubbleTitle") != "本轮工作结束啦" && Json.Number(state, "stateFrame") != 3, "other chat cannot display a completion bubble or pose over active work");
                }
                if (step == 2)
                {
                    string interaction = Path.Combine(evidence, "petting"); pet.SaveEvidence(interaction);
                    var petted = Json.Parse(File.ReadAllText(Path.Combine(interaction, "live-check.json")));
                    Check(Json.Number(petted, "pettingCount") == 1 && Json.Text(petted, "bubbleTitle") == "角色碎碎念", "character click gives feedback after the double-click window");
                    pet.SetClickThrough(true);
                    string passthrough = Path.Combine(evidence, "click-through"); pet.SaveEvidence(passthrough);
                    var state = Json.Parse(File.ReadAllText(Path.Combine(passthrough, "live-check.json")));
                    Check(Json.Get(state, "nativeClickThrough").Equals(true) && Json.Get(state, "trayRecoveryAvailable").Equals(true), "click-through reaches Windows and tray recovery stays available");
                    pet.SetClickThrough(false);
                    Check(!NativeWindow.IsClickThrough(new WindowInteropHelper(pet).Handle), "mouse interaction can be restored");
                    var status = (Border)LogicalTreeHelper.FindLogicalNode(pet, "PetStatus");
                    status.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                    var dashboard = app.Windows.OfType<Window>().Single(w => w != pet && w.Title == "Codex 小龙 · 工作与额度");
                    dashboard.IsHitTestVisible = false;
                    Check(dashboard.IsVisible, "status entry opens the real work and quota dashboard");
                    var quiet = Children(dashboard).OfType<CheckBox>().Single(b => Convert.ToString(b.Content) == "安静陪伴");
                    var reduce = Children(dashboard).OfType<CheckBox>().Single(b => Convert.ToString(b.Content) == "减少动画");
                    Toggle(quiet, true); Toggle(reduce, true);
                    string prefs = Path.Combine(evidence, "preferences"); pet.SaveEvidence(prefs);
                    var preferences = Json.Parse(File.ReadAllText(Path.Combine(prefs, "live-check.json")));
                    Check(Json.Get(preferences, "quiet").Equals(true) && Json.Get(preferences, "breathingAnimated").Equals(false), "quiet and reduced-motion controls apply to the live pet");
                    Check(Settings.Load().Quiet && Settings.Load().ReduceMotion, "preferences survive saving and reloading");
                    Toggle(quiet, false); Toggle(reduce, false); dashboard.Close();
                    pet.Hide(); Check(!pet.IsVisible, "pet can be hidden while the application stays alive");
                    pet.Show(); Check(pet.IsVisible, "pet can be restored after hiding");
                }
                if (step == 7)
                {
                    string folded = Path.Combine(evidence, "auto-folded"); pet.SaveEvidence(folded);
                    var state = Json.Parse(File.ReadAllText(Path.Combine(folded, "live-check.json")));
                    Check(Json.Get(state, "bubbleVisible").Equals(false), "ordinary bubble folds automatically after interaction");
                    File.AppendAllText(file, Record("{\"type\":\"task_complete\",\"turn_id\":\"ui-integration-turn\",\"last_agent_message\":\"任务结果已就绪。\"}"), new UTF8Encoding(false));
                }
                if (step == 8)
                {
                    string finalizing = Path.Combine(evidence, "finalizing"); pet.SaveEvidence(finalizing);
                    var state = Json.Parse(File.ReadAllText(Path.Combine(finalizing, "live-check.json")));
                    Check(Json.Get(state, "finishing").Equals(true) && Json.Get(state, "running").Equals(true) && Json.Number(state, "stateFrame") != 3, "real UI stays working while final output settles");
                    Check(Json.Text(state, "lastCompletedThreadId") == "" && Json.Text(state, "bubbleTitle") != "本轮工作结束啦", "no notification is dispatched during finalization");
                }
                if (step == 10)
                {
                    string completed = Path.Combine(evidence, "completed"); pet.SaveEvidence(completed);
                    var state = Json.Parse(File.ReadAllText(Path.Combine(completed, "live-check.json")));
                    Check(Json.Get(state, "running").Equals(false) && Json.Number(state, "stateFrame") == 3, "completion event selects celebration state beneath the transient action");
                    Check(Json.Text(state, "bubbleTitle") == "本轮工作结束啦", "completion bubble is actually displayed");
                    Check(Json.Get(state, "bubbleVisible").Equals(true) && Json.Text(state, "bubbleText") == "回来看看结果吧。", "completion overrides folding with a short status message");
                    Check(Json.Text(state, "lastCompletedThreadId") == ThreadId, "real notification callback was dispatched");
                    Check(File.Exists(Path.Combine(completed, "live-pet.png")), "completed native UI was rendered for inspection");
                    timer.Stop(); pet.Close();
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e); failures++; timer.Stop(); pet.Close(); }
        };
        pet.Loaded += delegate { pet.Left = 20; pet.Top = 20; timer.Start(); };
        app.Run(pet);
        Console.WriteLine(failures == 0 ? "UI integration passed." : "UI integration failed.");
        return failures == 0 ? 0 : 1;
    }
}
