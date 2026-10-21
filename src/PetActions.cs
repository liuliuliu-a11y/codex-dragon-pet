using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: System.Reflection.AssemblyVersion("0.4.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.4.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.4.0")]

namespace CodexDragon
{
    internal enum PetAction { None, Tea, Blink, Stretch, Drag, Land, Celebrate }

    // Monotonic elapsed time keeps poses independent of dispatcher delays and wall-clock changes.
    internal sealed class ActionPlayback
    {
        public PetAction Current { get; private set; }
        public bool CelebrationPending { get; private set; }
        public double Elapsed { get; private set; }
        private int[] frames = new int[0], durations = new int[0];
        public int Frame
        {
            get
            {
                if (Current == PetAction.None) return -1;
                double at = Current == PetAction.Drag ? Elapsed % Duration : Elapsed;
                for (int i = 0; i < durations.Length; i++) { if (at < durations[i]) return frames[i]; at -= durations[i]; }
                return frames[frames.Length - 1];
            }
        }
        public double Duration { get { double total = 0; foreach (int value in durations) total += value; return total; } }
        public double Progress { get { return Duration == 0 ? 0 : Math.Min(1, Elapsed / Duration); } }
        public bool Start(PetAction next)
        {
            if (next == PetAction.None) return false;
            if (next == PetAction.Celebrate && (Current == PetAction.Drag || Current == PetAction.Land))
            { CelebrationPending = true; return false; }
            if (next == PetAction.Drag) CelebrationPending |= Current == PetAction.Celebrate;
            else if (Current == PetAction.Celebrate) return false;
            if ((next == PetAction.Blink || next == PetAction.Stretch) && Current != PetAction.None) return false;
            if (next == PetAction.Tea && (Current == PetAction.Drag || Current == PetAction.Land)) return false;
            Current = next; Elapsed = 0;
            switch (next)
            {
                case PetAction.Tea:
                    frames = new[] { -1 };
                    durations = new[] { (int)(LayeredDragon.Pose.TeaDurationSeconds * 1000) }; break;
                case PetAction.Blink:
                    frames = new[] { -1 }; durations = new[] { 460 }; break;
                case PetAction.Stretch:
                    frames = new[] { 12, 13, 14, 15, 8 }; durations = new[] { 280, 600, 850, 380, 160 }; break;
                case PetAction.Drag:
                    frames = new[] { 16, 17 }; durations = new[] { 280, 280 }; break;
                case PetAction.Land:
                    frames = new[] { 18, 19, 8 }; durations = new[] { 140, 180, 140 }; break;
                default:
                    frames = new[] { 20, 21, 22, 21, 22, 23, 8 };
                    durations = new[] { 180, 220, 260, 200, 260, 380, 200 }; break;
            }
            return true;
        }
        public void Advance(double milliseconds)
        {
            if (Current == PetAction.None || milliseconds <= 0) return;
            Elapsed += milliseconds;
            if (Current == PetAction.Drag || Elapsed < Duration) return;
            bool celebrate = CelebrationPending;
            Cancel();
            if (celebrate) Start(PetAction.Celebrate);
        }
        public void Cancel() { Current = PetAction.None; Elapsed = 0; CelebrationPending = false; }
    }

    public sealed partial class PetWindow
    {
        internal const string LocalVersion = "0.4.0";
        private readonly ActionPlayback action = new ActionPlayback();
        private readonly BitmapSource[] actionSprites = new BitmapSource[24];
        private readonly ScaleTransform actionScale = new ScaleTransform(1, 1);
        private readonly TranslateTransform actionOffset = new TranslateTransform();
        private DispatcherTimer actionTimer, clickTimer;
        private readonly Stopwatch actionClock = new Stopwatch();
        private double actionLastTick;
        private int renderedActionFrame = -1;
        private Mood displayMood;
        private DateTime nextIdleAction = DateTime.UtcNow.AddSeconds(8);
        private bool nextIdleStretch, suppressClick;
        private string observedRunningTurn = "";

        private void InitializeActions()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "dragon-actions.png");
            var sheet = new BitmapImage(); sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad;
            sheet.UriSource = new Uri(path); sheet.EndInit(); sheet.Freeze();
            if (sheet.PixelWidth % 4 != 0 || sheet.PixelHeight % 6 != 0) throw new InvalidDataException("Invalid dragon action atlas.");
            int width = sheet.PixelWidth / 4, height = sheet.PixelHeight / 6;
            for (int i = 0; i < actionSprites.Length; i++)
            {
                var frame = new CroppedBitmap(sheet, new Int32Rect(i % 4 * width, i / 4 * height, width, height));
                frame.Freeze(); actionSprites[i] = frame;
            }
            actionTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            actionTimer.Tick += delegate { AdvanceAction(); };
            clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime) };
            clickTimer.Tick += delegate { clickTimer.Stop(); if (!dragging && IsVisible) PetHead(); };
        }
        private bool PlayAction(PetAction kind)
        {
            if (!IsVisible || settings.ReduceMotion) return false;
            if (!action.Start(kind)) return false;
            renderedActionFrame = -1;
            sprite.BeginAnimation(UIElement.OpacityProperty, null); sprite.Opacity = 1;
            previousSprite.BeginAnimation(UIElement.OpacityProperty, null); previousSprite.Opacity = 0;
            tiltTransform.BeginAnimation(RotateTransform.AngleProperty, null); tiltTransform.Angle = 0;
            actionClock.Restart(); actionLastTick = 0;
            ApplyMotion(); RenderAction(); actionTimer.Start(); return true;
        }
        private void AdvanceAction()
        {
            double now = actionClock.Elapsed.TotalMilliseconds;
            action.Advance(now - actionLastTick); actionLastTick = now;
            if (action.Current == PetAction.None) { StopAction(); return; }
            RenderAction();
        }
        private void RenderAction()
        {
            RefreshLayeredPet();
            if (action.Current == PetAction.Tea || action.Current == PetAction.Blink) return;
            if (action.Frame != renderedActionFrame)
            { renderedActionFrame = action.Frame; sprite.Source = actionSprites[renderedActionFrame]; }
            double progress = action.Progress;
            actionScale.ScaleX = actionScale.ScaleY = 1; actionOffset.Y = 0;
            if (action.Current == PetAction.Drag) tiltTransform.Angle = Math.Sin(action.Elapsed / 180) * 3;
            else if (action.Current == PetAction.Land)
            {
                double bounce = Math.Sin(progress * Math.PI);
                actionScale.ScaleX = 1 + .06 * bounce; actionScale.ScaleY = 1 - .09 * bounce;
                actionOffset.Y = 3 * bounce;
            }
            else if (action.Current == PetAction.Celebrate)
                actionOffset.Y = -Math.Abs(Math.Sin(progress * Math.PI * 3)) * 12 * (1 - progress);
        }
        private void StopAction()
        {
            action.Cancel(); actionClock.Stop();
            if (actionTimer != null) actionTimer.Stop();
            actionScale.ScaleX = actionScale.ScaleY = 1; actionOffset.Y = 0; renderedActionFrame = -1;
            tiltTransform.BeginAnimation(RotateTransform.AngleProperty, null); tiltTransform.Angle = 0;
            activeFrame = -1; SetMood(displayMood); ApplyMotion();
        }
        internal void DrinkTea()
        {
            if (PlayAction(PetAction.Tea)) Say("一起喝杯茶", "呼——慢慢来，陪你歇一小会儿。", 11);
            else if (settings.ReduceMotion) Say("一起喝杯茶", "收到这杯茶，暖暖的。", 5);
        }
        internal void BeginPetDrag()
        { clickTimer.Stop(); dragging = true; PlayAction(PetAction.Drag); }
        internal void EndPetDrag()
        { dragging = false; PlayAction(PetAction.Land); ClampPosition(); SavePosition(); }
        private void SetDailyActions(bool enabled)
        {
            settings.DailyActions = enabled;
            if (!enabled && (action.Current == PetAction.Blink || action.Current == PetAction.Stretch)) StopAction();
            ApplyMotion();
            nextIdleAction = DateTime.UtcNow.AddSeconds(8); Persist(); ContextMenu = MakeMenu();
        }
        private void ObserveActionState()
        {
            string key = selected != null && selected.Running ? selected.Id + ":" + selected.TurnId : "";
            if (key.Length > 0 && key != observedRunningTurn)
            {
                if (!dragging) StopAction();
                else { action.Cancel(); PlayAction(PetAction.Drag); }
                if (speechTitle == "本轮工作结束啦") speechUntil = DateTime.MinValue;
            }
            observedRunningTurn = key;
            if (!IsVisible || !settings.DailyActions || settings.ReduceMotion || settings.Quiet || dragging || hovering ||
                action.Current != PetAction.None || (selected != null && selected.Running) ||
                DateTime.UtcNow < speechUntil || DateTime.UtcNow < nextIdleAction) return;
            PlayAction(nextIdleStretch ? PetAction.Stretch : PetAction.Blink);
            nextIdleStretch = !nextIdleStretch;
            nextIdleAction = DateTime.UtcNow.AddSeconds(nextIdleStretch ? random.Next(12, 20) : random.Next(30, 46));
        }
        private void ActionVisibilityChanged()
        {
            if (!IsVisible) { clickTimer.Stop(); StopAction(); }
            else nextIdleAction = DateTime.UtcNow.AddSeconds(8);
            ApplyMotion();
        }
    }
}
