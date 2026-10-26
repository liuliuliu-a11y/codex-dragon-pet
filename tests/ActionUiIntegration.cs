using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexDragon;

public static class ActionUiIntegration
{
    private static int passed, failures;
    private sealed class Step
    {
        public int At;
        public Action Run;
        public Func<bool> Ready;
        public bool Waited;
        public Step(int at, Action run, Func<bool> ready = null) { At = at; Run = run; Ready = ready; }
    }
    private static void Check(bool condition, string description)
    { Console.WriteLine((condition ? "PASS " : "FAIL ") + description); if (condition) passed++; else failures++; }
    private static string Record(string payload)
    { return "{\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\",\"type\":\"event_msg\",\"payload\":" + payload + "}\n"; }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        foreach (object value in LogicalTreeHelper.GetChildren(parent))
        {
            var child = value as DependencyObject; if (child == null) continue;
            yield return child; foreach (var node in Children(child)) yield return node;
        }
    }
    private static void Menu(PetWindow pet, string title)
    { pet.ContextMenu.Items.OfType<MenuItem>().Single(item => Convert.ToString(item.Header) == title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
    private static void Toggle(PetWindow pet, string title, bool enabled)
    {
        Menu(pet, "工作与额度面板");
        var dashboard = Application.Current.Windows.OfType<Window>().Single(window => window != pet);
        dashboard.IsHitTestVisible = false;
        var check = Children(dashboard).OfType<CheckBox>().Single(box => Convert.ToString(box.Content) == title);
        check.IsChecked = enabled; check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); dashboard.Close();
    }
    [STAThread]
    public static int Main(string[] args)
    {
        string evidence = Path.GetFullPath(args[0]); Directory.CreateDirectory(evidence);
        string fixture = Path.Combine(evidence, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixture, "sessions"));
        Environment.SetEnvironmentVariable("CODEX_HOME", fixture);
        Environment.SetEnvironmentVariable("CODEX_PET_DATA_DIR", Path.Combine(fixture, "pet-data"));
        string file = Path.Combine(fixture, "sessions", "rollout-00000000-0000-0000-0000-000000000456.jsonl");
        string work = "{\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"exec_command\"}}\n";
        File.WriteAllText(file, Record("{\"type\":\"task_started\",\"turn_id\":\"tea-turn\"}") + work, new UTF8Encoding(false));
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var pet = new PetWindow(true) { Title = "Codex 小龙 · 动作隔离验收", IsHitTestVisible = false };
        var seen = new HashSet<PetAction>();
        Action<string> save = delegate(string name) { pet.SaveEvidence(Path.Combine(evidence, name)); };
        double beforeWalk = 0, stoppedWalk = 0;
        var steps = new List<Step> {
            new Step(100, delegate { Menu(pet, "一起喝茶"); Check(pet.VerificationAction == PetAction.Tea, "real tea menu starts the action"); save("tea-start"); }),
            new Step(1000, delegate { save("tea-opening"); Check(pet.VerificationLayered && pet.VerificationFrame == -1 && pet.VerificationTea > .5, "tea starts continuous sleeve motion without a pose atlas"); }),
            new Step(2150, delegate { save("tea-lifting"); Check(pet.VerificationLayered && pet.VerificationCup > .9 && pet.VerificationTea > 1.7, "tea produces the rigged cup during the lifting phase"); }),
            new Step(4300, delegate { save("tea-restored"); Check(pet.VerificationAction == PetAction.None && pet.VerificationState == 1, "tea restores the current work state after putting away the cup"); }),
            new Step(4400, delegate { pet.DrinkTea(); File.AppendAllText(file, Record("{\"type\":\"task_complete\",\"turn_id\":\"tea-turn\",\"last_agent_message\":\"Tea result\"}"), new UTF8Encoding(false)); }),
            new Step(5600, delegate { save("completion-interrupts-tea"); Check(pet.VerificationAction == PetAction.Celebrate, "actual completion event interrupts tea with celebration"); }, delegate { return pet.VerificationAction == PetAction.Celebrate; }),
            new Step(7200, delegate { Check(pet.VerificationAction == PetAction.None && pet.VerificationState == 3, "celebration returns to the completed state"); }, delegate { return pet.VerificationAction == PetAction.None; }),
            new Step(7300, delegate { pet.DrinkTea(); File.AppendAllText(file, Record("{\"type\":\"task_started\",\"turn_id\":\"drag-turn\"}") + work, new UTF8Encoding(false)); }),
            new Step(8600, delegate { save("new-task"); Check(pet.VerificationAction == PetAction.None && pet.VerificationState == 1, "a new task cancels tea and clears the previous completion pose"); }),
            new Step(8700, delegate { pet.BeginPetDrag(); }),
            new Step(8800, delegate { File.AppendAllText(file, Record("{\"type\":\"task_complete\",\"turn_id\":\"drag-turn\",\"last_agent_message\":\"Drag result\"}"), new UTF8Encoding(false)); }),
            new Step(10100, delegate { save("drag"); Check(pet.VerificationAction == PetAction.Drag && pet.VerificationPending, "task completion waits while the pet is being held"); }, delegate { return pet.VerificationPending; }),
            new Step(10200, delegate { pet.EndPetDrag(); }),
            new Step(10300, delegate { save("landing"); Check(pet.VerificationAction == PetAction.Land, "release plays the landing action"); }),
            new Step(11000, delegate { save("queued-celebration"); Check(pet.VerificationAction == PetAction.Celebrate, "queued celebration follows landing"); }),
            new Step(13000, delegate { Check(pet.VerificationAction == PetAction.None, "queued celebration does not remain stuck"); }),
            new Step(13100, delegate { Menu(pet, "伸个懒腰"); }),
            new Step(14000, delegate { save("stretch"); Check(pet.VerificationAction == PetAction.Stretch, "manual daily stretch uses real action frames"); Toggle(pet, "减少动画", true); }),
            new Step(14300, delegate { pet.DrinkTea(); save("reduced-motion"); Check(pet.VerificationAction == PetAction.None && !pet.VerificationTimer, "reduced motion cancels playback and leaves tea text available"); Toggle(pet, "减少动画", false); }),
            new Step(14500, delegate { pet.DrinkTea(); }),
            new Step(14600, delegate { pet.Hide(); Check(pet.VerificationAction == PetAction.None && !pet.VerificationTimer, "hiding cancels animation and its timer"); }),
            new Step(14700, delegate { pet.Show(); Check(pet.VerificationAction == PetAction.None, "showing does not replay hidden interactions"); }),
            new Step(14800, delegate { pet.PrepareIdleVerification(); }),
            new Step(16100, delegate { Check(seen.Contains(PetAction.Blink), "idle scheduling produces a blink without manual input"); pet.PrepareIdleVerification(); }),
            new Step(17800, delegate { save("automatic-stretch"); Check(pet.VerificationAction == PetAction.Stretch, "idle scheduling also produces a stretch and yawn"); }),
            new Step(19500, delegate { Toggle(pet, "日常小动作", false); Check(!Settings.Load().DailyActions, "daily-action preference saves and reloads"); Toggle(pet, "安静陪伴", true); pet.PrepareIdleVerification(); }),
            new Step(21300, delegate { save("quiet"); Check(pet.VerificationAction == PetAction.None, "quiet companionship suppresses unsolicited idle actions"); }),
            new Step(21400, delegate { Menu(pet, "休息一会儿"); Check(pet.VerificationAction == PetAction.Sleep, "rest menu starts sleeping even in quiet mode"); }),
            new Step(24000, delegate { save("sleeping"); Check(pet.VerificationRest > .99 && pet.VerificationBlink > .99, "sleep settles with closed eyes and stays asleep"); Menu(pet, "摸摸头"); Check(pet.VerificationAction == PetAction.Wake, "touching a sleeping pet wakes it"); }),
            new Step(26700, delegate { save("awake"); Check(pet.VerificationAction == PetAction.None && pet.VerificationRest < .01, "waking returns to the upright pose"); Menu(pet, "休息一会儿"); }),
            new Step(29200, delegate { Menu(pet, "喂一块点心"); Check(pet.VerificationAction == PetAction.Wake, "feeding waits for the sleeping pet to rise"); }),
            new Step(34300, delegate { save("feeding"); Check(pet.VerificationAction == PetAction.Feed && pet.VerificationSnack > .8, "queued feeding renders a visible snack near the hands"); }),
            new Step(37500, delegate { Check(pet.VerificationAction == PetAction.None && pet.VerificationSnack < .01, "feeding finishes with no leftover prop"); beforeWalk = pet.Left; Menu(pet, "走一小段"); }),
            new Step(39500, delegate { save("walking"); Check(Math.Abs(pet.Left - beforeWalk) > 5 && Math.Abs(pet.Left - beforeWalk) <= 100 * Settings.Load().Scale, "walk moves the actual window a bounded distance"); pet.BeginPetDrag(); stoppedWalk = pet.Left; }),
            new Step(39800, delegate { Check(pet.Left == stoppedWalk && pet.VerificationAction == PetAction.Drag, "drag interrupts walking without position drift"); pet.EndPetDrag(); }),
            new Step(40500, delegate { pet.Left = SystemParameters.WorkArea.Right - pet.Width; beforeWalk = pet.Left; Menu(pet, "走一小段"); }),
            new Step(44900, delegate { Check(pet.VerificationAction == PetAction.None && beforeWalk - pet.Left > 30 && Settings.Load().Left == pet.Left && pet.Left >= SystemParameters.WorkArea.Left, "walk turns inward at the screen edge and saves its new position"); Menu(pet, "休息一会儿"); pet.Hide(); Check(pet.VerificationAction == PetAction.None && !pet.VerificationTimer, "hiding during sleep stops the sleeping timer"); pet.Show(); }),
            new Step(45200, delegate { Toggle(pet, "减少动画", true); Menu(pet, "喂一块点心"); Menu(pet, "走一小段"); Menu(pet, "休息一会儿"); Check(pet.VerificationAction == PetAction.None && !pet.VerificationTimer, "reduced motion keeps all new interactions still"); })
        };
        // Keep each observation relative to the real three-second completion confirmation.
        foreach (Step item in steps) item.At += (item.At >= 4300 ? 6500 : 0) + (item.At >= 10100 ? 6000 : item.At >= 5600 ? 3000 : 0);
        var clock = new Stopwatch(); int index = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += delegate
        {
            try
            {
                seen.Add(pet.VerificationAction);
                while (index < steps.Count && clock.ElapsedMilliseconds >= steps[index].At)
                {
                    Step item = steps[index];
                    if (item.Ready != null && !item.Ready())
                    {
                        item.Waited = true;
                        if (clock.ElapsedMilliseconds - item.At < 5000) break;
                    }
                    // Filesystem reads and the one-second poll may land on adjacent ticks.
                    // Once ready, preserve the following drag/landing observation intervals.
                    if (item.Waited)
                    {
                        int delay = (int)(clock.ElapsedMilliseconds - item.At);
                        foreach (Step later in steps.Skip(index + 1)) later.At += delay;
                    }
                    index++; item.Run();
                }
                if (index == steps.Count)
                {
                    timer.Stop();
                    File.WriteAllText(Path.Combine(evidence, "result.json"), Json.Write(new { passed = passed, failures = failures, observedActions = seen.Select(value => value.ToString()).ToArray() }));
                    pet.Close();
                }
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); failures++; timer.Stop(); pet.Close(); }
        };
        pet.Loaded += delegate { pet.Left = 20; pet.Top = 20; clock.Start(); timer.Start(); };
        app.Run(pet);
        Console.WriteLine(passed + " action UI checks passed; " + failures + " failed.");
        return failures == 0 ? 0 : 1;
    }
}

namespace CodexDragon
{
    // Test-only access stays outside the shipped binary.
    public sealed partial class PetWindow
    {
        internal PetAction VerificationAction { get { return action.Current; } }
        internal int VerificationFrame { get { return action.Frame; } }
        internal int VerificationState { get { return activeFrame; } }
        internal bool VerificationPending { get { return action.CelebrationPending; } }
        internal bool VerificationTimer { get { return actionTimer.IsEnabled; } }
        internal bool VerificationLayered { get { return layeredView.Visibility == Visibility.Visible && layeredTimer.IsEnabled; } }
        internal double VerificationTea { get { return actionClock.Elapsed.TotalSeconds; } }
        internal double VerificationCup { get { return layeredRig.Current.CupOpacity; } }
        internal double VerificationRest { get { return layeredRig.Current.Rest; } }
        internal double VerificationBlink { get { return layeredRig.Current.Blink; } }
        internal double VerificationSnack { get { return layeredRig.Current.SnackOpacity; } }
        internal void PrepareIdleVerification() { speechUntil = DateTime.MinValue; nextIdleAction = DateTime.MinValue; hovering = false; }
    }
}
