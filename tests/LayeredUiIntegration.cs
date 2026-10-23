using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexDragon;

// Real production PetWindow and rig; no user sessions, settings or downloaded dependencies.
public static class LayeredUiIntegration
{
    private static int passed, failures;
    private static void Check(bool condition, string text)
    { Console.WriteLine((condition ? "PASS " : "FAIL ") + text); if (condition) passed++; else failures++; }
    [STAThread]
    public static int Main(string[] args)
    {
        string evidence = Path.GetFullPath(args[0]); Directory.CreateDirectory(evidence);
        string userSettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDragonPet", "settings.json");
        byte[] userSettingsBefore = File.Exists(userSettingsFile) ? File.ReadAllBytes(userSettingsFile) : null;
        string fixture = Path.Combine(evidence, "fixture-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEX_HOME", fixture);
        Environment.SetEnvironmentVariable("CODEX_PET_DATA_DIR", Path.Combine(fixture, "pet-data"));
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var pet = new PetWindow(false) { Title = "Codex Layered Integration", ShowInTaskbar = true, IsHitTestVisible = false };
        var clock = new Stopwatch();
        var steps = new List<KeyValuePair<int, Action>>();
        Action<int, Action> at = delegate(int time, Action run) { steps.Add(new KeyValuePair<int, Action>(time, run)); };
        Action<string> save = delegate(string name) { pet.SaveEvidence(Path.Combine(evidence, name)); };
        long firstUpdates = 0; double firstTime = 0, fps = 0;
        double previousCupY = 0, maxCupStep = 0; bool haveCup = false;
        bool naturalBlinkSeen = false;
        at(600, delegate {
            Check(pet.LayeredActive && pet.LayeredTicking && pet.LayeredFrames > 5, "idle uses the production layered viewport and continuous updates");
            Check(pet.LayeredInputAligned, "layered art and input target share the original character bounds");
            save("idle"); pet.LayeredClick();
        });
        at(1600, delegate { Check(pet.LayeredPettingCount == 1, "layered idle keeps the real character click feedback"); });
        at(2800, delegate { Check(naturalBlinkSeen, "natural idle blink comes from the eyelid layer"); pet.DrinkTea(); });
        at(3100, delegate { firstUpdates = pet.LayeredFrames; firstTime = clock.Elapsed.TotalSeconds; });
        at(5000, delegate {
            Check(pet.LayeredAction == PetAction.Tea && pet.LayeredActive && pet.LayeredCup > .95, "tea lifting uses the cup and arm layers in the desktop window");
            Check(pet.LayeredActionFrame == -1, "tea rendering does not select a full-character atlas frame");
            save("tea-lifting");
        });
        at(7800, delegate {
            Check(pet.LayeredCupY < 242 && pet.LayeredBlink > .9, "sip raises the cup to the face and closes the eyelids");
            save("tea-sipping");
        });
        at(12300, delegate {
            fps = (pet.LayeredFrames - firstUpdates) / (clock.Elapsed.TotalSeconds - firstTime);
            Check(fps >= 24, "real desktop tea rig averages at least 24 updates per second");
            Check(maxCupStep < 8 && haveCup, "live cup trajectory stays continuous between dispatcher observations");
        });
        at(13400, delegate {
            Check(pet.LayeredAction == PetAction.None && pet.LayeredActive && pet.LayeredCup == 0, "ten-second tea restores idle with no remaining cup");
            save("tea-restored"); pet.DrinkTea();
        });
        at(13800, delegate { pet.LayeredSetWork(); });
        at(14100, delegate {
            Check(pet.LayeredAction == PetAction.None && pet.LayeredActive && pet.LayeredTicking && pet.LayeredStateFrame == 1 && pet.LayeredCup == 0, "new work cancels tea while keeping the same layered character");
            save("working"); pet.DrinkTea();
        });
        at(14600, delegate { Check(pet.LayeredActive && pet.LayeredAction == PetAction.Tea, "manual tea is available while a task is working"); pet.LayeredComplete(); });
        at(14900, delegate {
            Check(pet.LayeredAction == PetAction.Celebrate && pet.LayeredActive && pet.LayeredTicking && pet.LayeredCup == 0, "celebration interrupts tea on the same rig and fades out the cup");
            save("celebration");
        });
        at(16600, delegate {
            Check(pet.LayeredAction == PetAction.None && pet.LayeredActive && pet.LayeredCup == 0, "celebration returns to natural layered idle");
            pet.DrinkTea(); pet.BeginPetDrag();
        });
        at(16800, delegate {
            Check(pet.LayeredAction == PetAction.Drag && pet.LayeredActive && pet.LayeredTicking, "drag interrupts tea without switching character artwork");
            pet.EndPetDrag();
        });
        at(16950, delegate { Check(pet.LayeredAction == PetAction.Land, "landing still follows a drag that interrupted tea"); });
        at(17500, delegate { Check(pet.LayeredActive && pet.LayeredCup == 0, "landing restores layered idle without a stale cup"); pet.DrinkTea(); pet.LayeredReduce(true); });
        at(17900, delegate {
            Check(pet.LayeredAction == PetAction.None && !pet.LayeredTicking && !pet.LayeredActionTicking && pet.LayeredCup == 0, "reduced motion stops both tea and idle animation timers");
            pet.DrinkTea(); Check(pet.LayeredAction == PetAction.None && pet.LayeredBubble == "一起喝杯茶", "reduced motion retains tea text without starting animation");
            pet.LayeredReduce(false); pet.LayeredQuiet(true);
        });
        at(18300, delegate { Check(!pet.LayeredTicking, "quiet mode keeps unsolicited layered idle still"); pet.DrinkTea(); });
        at(18700, delegate {
            Check(pet.LayeredTicking && pet.LayeredAction == PetAction.Tea, "quiet mode still allows an explicitly requested tea");
            pet.Hide();
        });
        at(19000, delegate {
            Check(!pet.LayeredTicking && !pet.LayeredActionTicking && pet.LayeredCup == 0, "hiding cancels the cup and both render timers");
            pet.Show();
        });
        at(19300, delegate {
            Check(pet.LayeredAction == PetAction.None && !pet.LayeredTicking && pet.LayeredCup == 0, "showing restores quiet idle without replaying hidden tea");
            pet.LayeredQuiet(false); pet.LayeredDaily(false);
        });
        at(19700, delegate { Check(!pet.LayeredTicking, "daily-action switch pauses procedural idle"); pet.DrinkTea(); });
        at(20100, delegate {
            Check(pet.LayeredTicking && pet.LayeredAction == PetAction.Tea, "daily-action switch leaves manual tea available");
            pet.LayeredStop(); pet.LayeredDaily(true); pet.LayeredScale(.75);
        });
        at(20500, delegate { Check(Math.Abs(pet.ActualWidth - 247.5) < 2 && pet.LayeredInputAligned, "original small desktop scale keeps layered art and input aligned"); pet.LayeredScale(1.5); });
        at(21000, delegate {
            Check(Math.Abs(pet.ActualWidth - 495) < 2 && pet.LayeredInputAligned, "large desktop scale keeps the character inside its original layout");
            pet.LayeredScale(1); save("idle-final");
        });
        at(21400, delegate {
            pet.LayeredMenuScale("50%");
            Check(Math.Abs(pet.ActualWidth - 165) < 2 && pet.LayeredInputAligned, "50 percent keeps the rendered character and hit target together");
            Check(Math.Abs(Settings.Load().Scale - .5) < .001, "small size survives a settings reload");
            save("compact-idle"); pet.LayeredPointer(1);
        });
        at(21900, delegate {
            Check(pet.LayeredHeadTurn > 3, "hover smoothly turns the head toward the pointer");
            save("hover-right"); pet.LayeredLeave();
        });
        at(22400, delegate {
            Check(Math.Abs(pet.LayeredHeadTurn) < .2, "leaving the character smoothly restores its head");
            pet.LayeredPet();
        });
        at(23100, delegate {
            Check(pet.LayeredAction == PetAction.Pet && pet.LayeredBlink > .95 && pet.LayeredActive, "petting closes eyelids on the live layered character");
            save("petting"); pet.HappyHop();
            Check(pet.LayeredAction == PetAction.Pet, "hop does not restart an unfinished petting reaction");
        });
        at(24000, delegate {
            Check(pet.LayeredAction == PetAction.None && pet.LayeredPetAmount < .01, "petting restores the natural pose");
            pet.ContextMenu.Items.OfType<MenuItem>().Single(item => Convert.ToString(item.Header) == "开心跳一下").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        });
        at(24500, delegate {
            Check(pet.LayeredAction == PetAction.Hop && pet.LayeredJumpY < -15 && pet.LayeredActive, "menu hop raises the actual layered character");
            Check(pet.LayeredBubbleY < -20, "hop moves its speech bubble clear of the rising horns");
            save("hop-airborne");
        });
        at(25300, delegate {
            Check(pet.LayeredAction == PetAction.None && Math.Abs(pet.LayeredJumpY) < .05 && Math.Abs(pet.LayeredBubbleY) < .05, "hop lands and releases character and bubble transforms");
            pet.HappyHop(); pet.BeginPetDrag();
            Check(pet.LayeredAction == PetAction.Drag && Math.Abs(pet.LayeredJumpY) < .05, "drag starts from the current pose rather than a stale jump offset");
            pet.EndPetDrag();
        });
        at(26100, delegate { pet.LayeredPet(); pet.LayeredReduce(true); });
        at(26500, delegate {
            Check(!pet.LayeredTicking && !pet.LayeredActionTicking && pet.LayeredPetAmount == 0 && pet.LayeredHeartOpacity == 0, "reduced motion clears touch animation and heart");
            pet.HappyHop(); Check(pet.LayeredAction == PetAction.None && pet.LayeredBubble == "开心一下", "reduced motion keeps hop text feedback without movement");
            pet.LayeredReduce(false); pet.LayeredPet(); pet.Hide();
        });
        at(26900, delegate {
            Check(!pet.LayeredTicking && !pet.LayeredActionTicking && pet.LayeredHeartOpacity == 0, "hidden pet cancels interaction timers and heart");
            pet.Show(); pet.LayeredMenuScale("40%");
        });
        at(27400, delegate {
            Check(Math.Abs(pet.ActualWidth - 132) < 2 && pet.LayeredInputAligned, "40 percent menu size keeps input aligned");
            pet.LayeredMenuScale("50%"); save("compact-final");
        });
        at(27800, delegate {
            pet.ContextMenu.Items.OfType<MenuItem>().Single(item => Convert.ToString(item.Header) == "点头打招呼").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        });
        at(28250, delegate {
            Check(pet.LayeredAction == PetAction.Greet && pet.LayeredNod > 2 && pet.LayeredActive, "greeting menu animates the existing head without replacing the character");
            save("greeting");
        });
        at(30100, delegate {
            Check(pet.LayeredAction == PetAction.None && Math.Abs(pet.LayeredNod) < .1, "greeting settles fully into idle");
            pet.LayeredStretch();
        });
        at(31000, delegate {
            Check(pet.LayeredArms > .95 && pet.LayeredCup == 0 && pet.LayeredActive, "stretch raises continuous arms without a cup or atlas switch");
            save("continuous-stretch"); pet.Hide(); pet.Show();
        });
        at(31400, delegate { Check(pet.LayeredAction == PetAction.None && pet.LayeredArms == 0, "hide clears the raised-arm pose before resuming idle"); });
        int index = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += delegate
        {
            try
            {
                if (clock.Elapsed.TotalSeconds < 2.8 && pet.LayeredBlink > .65) naturalBlinkSeen = true;
                if (pet.LayeredAction == PetAction.Tea && clock.Elapsed.TotalSeconds < 12.5)
                {
                    if (haveCup) maxCupStep = Math.Max(maxCupStep, Math.Abs(pet.LayeredCupY - previousCupY));
                    previousCupY = pet.LayeredCupY; haveCup = true;
                }
                while (index < steps.Count && clock.ElapsedMilliseconds >= steps[index].Key) steps[index++].Value();
                if (index == steps.Count)
                {
                    timer.Stop(); pet.Close();
                    Check(!pet.LayeredTicking && !pet.LayeredActionTicking, "closing releases both animation timers");
                    Check(userSettingsBefore == null ? !File.Exists(userSettingsFile) : userSettingsBefore.SequenceEqual(File.ReadAllBytes(userSettingsFile)), "isolated layered suite preserves real user settings byte for byte");
                    File.WriteAllText(Path.Combine(evidence, "result.json"), Json.Write(new {
                        passed = passed, failures = failures, averageTeaUpdateHz = fps, maxObservedCupStep = maxCupStep,
                        peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                        note = "Real WPF update counts; screen presentation is checked in a separate native recording."
                    }));
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e); failures++; timer.Stop(); pet.Close(); }
        };
        pet.Loaded += delegate {
            // Access Settings only after process-local environment setup; its field initializer
            // may otherwise be hoisted to the beginning of Main by the .NET Framework JIT.
            if (Settings.Folder != Path.Combine(fixture, "pet-data")) { Console.Error.WriteLine("FAIL Test settings are not isolated"); failures++; pet.Close(); return; }
            Check(true, "layered test uses a dedicated settings directory before interaction");
            pet.Left = 20; pet.Top = 20; clock.Start(); timer.Start();
        };
        app.Run(pet);
        Console.WriteLine(passed + " layered UI checks passed; " + failures + " failed.");
        return failures == 0 ? 0 : 1;
    }
}

namespace CodexDragon
{
    public sealed partial class PetWindow
    {
        internal bool LayeredActive { get { return layeredView.Visibility == Visibility.Visible; } }
        internal bool LayeredTicking { get { return layeredTimer.IsEnabled; } }
        internal long LayeredFrames { get { return layeredUpdates; } }
        internal double LayeredCup { get { return layeredRig.Current.CupOpacity; } }
        internal double LayeredCupY { get { return layeredRig.Current.CupY; } }
        internal double LayeredBlink { get { return layeredRig.Current.Blink; } }
        internal int LayeredActionFrame { get { return action.Frame; } }
        internal PetAction LayeredAction { get { return action.Current; } }
        internal int LayeredStateFrame { get { return activeFrame; } }
        internal bool LayeredActionTicking { get { return actionTimer.IsEnabled; } }
        internal int LayeredPettingCount { get { return pettingCount; } }
        internal string LayeredBubble { get { return speechTitle; } }
        internal bool LayeredInputAligned { get { return character.ActualWidth == 258 && character.ActualHeight == 258 && character.Children.Contains(layeredView) && character.Children.Contains(sprite); } }
        internal void LayeredClick() { character.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent }); }
        internal void LayeredSetWork() { selected = new Session("layered-fixture") { Running = true, TurnId = "new-work", Mood = Mood.Working }; ObserveActionState(); RefreshView(); }
        internal void LayeredComplete() { selected.Running = false; selected.Mood = Mood.Done; SetMood(Mood.Done); PlayAction(PetAction.Celebrate); }
        internal void LayeredReduce(bool value) { settings.ReduceMotion = value; if (value) StopAction(); ApplyMotion(); }
        internal void LayeredQuiet(bool value) { SetQuiet(value); }
        internal void LayeredDaily(bool value) { SetDailyActions(value); }
        internal void LayeredStop() { StopAction(); }
        internal void LayeredScale(double value) { settings.Scale = value; Width = 330 * value; Height = 438 * value; }
        internal double LayeredHeadTurn { get { return layeredRig.Current.HeadTurn; } }
        internal double LayeredPetAmount { get { return layeredRig.Current.Pet; } }
        internal double LayeredJumpY { get { return layeredRig.Current.OffsetY * .5; } }
        internal double LayeredBubbleY { get { return interactionBubbleOffset.Y; } }
        internal double LayeredHeartOpacity { get { return heart.Opacity; } }
        internal double LayeredNod { get { return layeredRig.Current.HeadNod; } }
        internal double LayeredArms { get { return layeredRig.Current.Arms; } }
        internal void LayeredStretch() { PlayAction(PetAction.Stretch); }
        internal void LayeredPointer(double x) { TrackPointer(x); }
        internal void LayeredLeave() { character.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = MouseLeaveEvent }); }
        internal void LayeredPet() { PetHead(); }
        internal void LayeredMenuScale(string title)
        {
            var sizes = ContextMenu.Items.OfType<MenuItem>().Single(item => Convert.ToString(item.Header) == "调整大小");
            sizes.Items.OfType<MenuItem>().Single(item => Convert.ToString(item.Header) == title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            UpdateLayout();
        }
    }
}
