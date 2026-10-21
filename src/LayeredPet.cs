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
        private bool LayeredRequested
        {
            get { return action.Current == PetAction.Tea || action.Current == PetAction.Blink ||
                (action.Current == PetAction.None && (displayMood == Mood.Idle || displayMood == Mood.Done)); }
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
                (action.Current == PetAction.Tea || action.Current == PetAction.Blink || (settings.DailyActions && !settings.Quiet));
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
                if (show) { layeredRig.Update(0, -1, false); layeredSteam.InvalidateVisual(); }
            }
        }

        private void RenderLayeredPet()
        {
            double now = layeredClock.Elapsed.TotalSeconds;
            if (now - layeredLastTick < 1.0 / 70) return;
            layeredTime += now - layeredLastTick; layeredLastTick = now;
            UpdateLayeredPose();
        }
        private void UpdateLayeredPose()
        {
            double teaTime = action.Current == PetAction.Tea ? actionClock.Elapsed.TotalSeconds : -1;
            double blink = action.Current == PetAction.Blink ? Math.Pow(Math.Sin(action.Progress * Math.PI), 2) : -1;
            layeredRig.Update(layeredTime, teaTime, false, blink);
            layeredSteam.InvalidateVisual(); layeredUpdates++;
        }
    }
}
