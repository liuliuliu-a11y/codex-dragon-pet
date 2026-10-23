using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: System.Reflection.AssemblyVersion("0.5.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.5.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.5.0")]

namespace CodexDragon
{
    internal enum PetAction { None, Tea, Blink, Stretch, Drag, Land, Celebrate, Pet, Hop, Greet }

    // Monotonic elapsed time keeps poses independent of dispatcher delays and wall-clock changes.
    internal sealed class ActionPlayback
    {
        public PetAction Current { get; private set; }
        public bool CelebrationPending { get; private set; }
        public double Elapsed { get; private set; }
        public int Frame { get { return -1; } }
        public double Duration { get; private set; }
        public double Progress { get { return Duration == 0 ? 0 : Math.Min(1, Elapsed / Duration); } }
        public bool Start(PetAction next)
        {
            if (next == PetAction.None) return false;
            if ((next == PetAction.Pet || next == PetAction.Hop || next == PetAction.Greet) && Current != PetAction.None &&
                Current != PetAction.Blink && Current != PetAction.Stretch) return false;
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
                    Duration = LayeredDragon.Pose.TeaDurationSeconds * 1000; break;
                case PetAction.Blink:
                    Duration = 460; break;
                case PetAction.Pet:
                    Duration = 1400; break;
                case PetAction.Hop:
                    Duration = 1100; break;
                case PetAction.Greet:
                    Duration = 1900; break;
                case PetAction.Stretch:
                    Duration = 2270; break;
                case PetAction.Drag:
                    Duration = 560; break;
                case PetAction.Land:
                    Duration = 460; break;
                default:
                    Duration = 1700; break;
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
        internal const string LocalVersion = "0.5.0";
        private readonly ActionPlayback action = new ActionPlayback();
        private readonly ScaleTransform actionScale = new ScaleTransform(1, 1);
        private readonly TranslateTransform actionOffset = new TranslateTransform();
        private readonly TranslateTransform interactionBubbleOffset = new TranslateTransform();
        private DispatcherTimer actionTimer, clickTimer;
        private readonly Stopwatch actionClock = new Stopwatch();
        private double actionLastTick;
        private Mood displayMood;
        private DateTime nextIdleAction = DateTime.UtcNow.AddSeconds(8);
        private bool nextIdleStretch, suppressClick;
        private string observedRunningTurn = "";

        private void InitializeActions()
        {
            actionTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            actionTimer.Tick += delegate { AdvanceAction(); };
            clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime) };
            clickTimer.Tick += delegate { clickTimer.Stop(); if (!dragging && IsVisible) PetHead(); };
        }
        private bool PlayAction(PetAction kind)
        {
            if (!IsVisible || settings.ReduceMotion) return false;
            if (!action.Start(kind)) return false;
            actionScale.ScaleX = actionScale.ScaleY = 1; actionOffset.Y = 0;
            interactionBubbleOffset.Y = 0;
            layeredLookTarget = 0;
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
        }
        private void StopAction()
        {
            action.Cancel(); actionClock.Stop();
            if (actionTimer != null) actionTimer.Stop();
            actionScale.ScaleX = actionScale.ScaleY = 1; actionOffset.Y = 0;
            interactionBubbleOffset.Y = 0;
            tiltTransform.BeginAnimation(RotateTransform.AngleProperty, null); tiltTransform.Angle = 0;
            activeFrame = -1; SetMood(displayMood); ApplyMotion();
        }
        internal void DrinkTea()
        {
            if (PlayAction(PetAction.Tea)) Say("一起喝杯茶", "呼——慢慢来，陪你歇一小会儿。", 11);
            else if (settings.ReduceMotion) Say("一起喝杯茶", "收到这杯茶，暖暖的。", 5);
        }
        internal void HappyHop()
        {
            if (PlayAction(PetAction.Hop) || settings.ReduceMotion)
                Say("开心一下", "给你一点小龙的元气！", 5);
        }
        internal void Greet()
        {
            if (PlayAction(PetAction.Greet) || settings.ReduceMotion)
                Say("打个招呼", "嗯，我在。今天也陪着你。", 5);
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
            if (!IsVisible) { clickTimer.Stop(); hovering = false; layeredLook = layeredLookTarget = 0; StopAction(); ClearHeart(); }
            else nextIdleAction = DateTime.UtcNow.AddSeconds(8);
            ApplyMotion();
        }
    }
}
