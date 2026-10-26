using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using LayeredDragon;

namespace CodexDragon
{
    public sealed partial class PetWindow
    {
        private DragonRig layeredRig;
        private Viewbox layeredView;
        private SteamOverlay layeredSteam;
        private DispatcherTimer layeredTimer;
        private readonly Stopwatch layeredClock = new Stopwatch();
        private double layeredLastTick, layeredTime;
        private long layeredUpdates;
        private double layeredLook, layeredLookTarget;
        private bool LayeredRequested
        {
            get { return true; }
        }

        private void InitializeLayeredPet()
        {
            layeredRig = new DragonRig(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "layered"));
            var art = new Grid { Width = 512, Height = 512, IsHitTestVisible = false, ClipToBounds = false };
            var viewport = new Viewport3D { IsHitTestVisible = false, ClipToBounds = false,
                Camera = new OrthographicCamera(new Point3D(256, -256, 1000), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 512) };
            RenderOptions.SetBitmapScalingMode(viewport, BitmapScalingMode.HighQuality);
            viewport.Children.Add(layeredRig.Visual); art.Children.Add(viewport);
            layeredSteam = new SteamOverlay(layeredRig, 512); art.Children.Add(layeredSteam);
            layeredView = new Viewbox { Child = art, Stretch = Stretch.Uniform, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            character.Children.Add(layeredView);
            layeredTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
            layeredTimer.Tick += delegate { RenderLayeredPet(); };
            RefreshLayeredPet();
        }

        private void RefreshLayeredPet()
        {
            if (layeredView == null) return;
            bool show = LayeredRequested;
            bool entering = show && layeredView.Visibility != Visibility.Visible;
            layeredView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            sprite.Visibility = previousSprite.Visibility = show ? Visibility.Hidden : Visibility.Visible;
            bool animate = show && IsVisible && !closed && !settings.ReduceMotion &&
                (action.Current != PetAction.None || (settings.DailyActions && !settings.Quiet));
            if (animate)
            {
                if (!layeredTimer.IsEnabled)
                {
                    layeredClock.Start(); layeredLastTick = layeredClock.Elapsed.TotalSeconds;
                    layeredTimer.Start();
                }
                if (entering) UpdateLayeredPose();
            }
            else
            {
                layeredTimer.Stop(); layeredClock.Stop();
                layeredLook = layeredLookTarget = 0;
                if (show) { layeredRig.Update(0, -1, false, mood: activeFrame, snap: true); layeredSteam.InvalidateVisual(); }
                interactionBubbleOffset.Y = 0;
            }
        }

        private void RenderLayeredPet()
        {
            double now = layeredClock.Elapsed.TotalSeconds;
            if (now - layeredLastTick < 1.0 / 70) return;
            double delta = now - layeredLastTick;
            layeredTime += delta; layeredLastTick = now;
            layeredLook += (layeredLookTarget - layeredLook) * (1 - Math.Exp(-delta * 9));
            UpdateLayeredPose();
        }
        private void UpdateLayeredPose()
        {
            double elapsed = action.Current == PetAction.None ? 0 : action.Elapsed + Math.Max(0, actionClock.Elapsed.TotalMilliseconds - actionLastTick);
            double progress = action.Duration == 0 ? 0 : Math.Min(1, elapsed / action.Duration);
            if (walkActive && action.Current == PetAction.Walk)
                Left = Pose.Mix(walkStart, walkEnd, Pose.Smooth(0, 1, progress));
            double teaTime = action.Current == PetAction.Tea ? elapsed / 1000 : -1;
            double blink = action.Current == PetAction.Blink ? Math.Pow(Math.Sin(progress * Math.PI), 2) : -1;
            MotionKind motion = MotionKind.Idle;
            switch (action.Current)
            {
                case PetAction.Pet: motion = MotionKind.Pet; break;
                case PetAction.Hop: motion = MotionKind.Hop; break;
                case PetAction.Stretch: motion = MotionKind.Stretch; break;
                case PetAction.Drag: motion = MotionKind.Drag; break;
                case PetAction.Land: motion = MotionKind.Land; break;
                case PetAction.Celebrate: motion = MotionKind.Celebrate; break;
                case PetAction.Greet: motion = MotionKind.Greet; break;
                case PetAction.Sleep: motion = MotionKind.Sleep; break;
                case PetAction.Wake: motion = MotionKind.Wake; break;
                case PetAction.Walk: motion = MotionKind.Walk; break;
                case PetAction.Feed: motion = MotionKind.Feed; break;
            }
            layeredRig.Update(layeredTime, teaTime, false, blink, layeredLook * 4, motion: motion,
                progress: progress, seconds: elapsed / 1000, mood: activeFrame);
            // Lift the bubble along with upward movement, including interruption blends.
            interactionBubbleOffset.Y = Math.Min(0, layeredRig.Current.OffsetY * .8);
            layeredSteam.InvalidateVisual(); layeredUpdates++;
        }
    }
}
