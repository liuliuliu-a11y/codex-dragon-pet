using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace LayeredDragon
{
    public sealed class Preview : Window
    {
        private readonly DragonRig rig;
        private readonly Grid artwork;
        private readonly SteamOverlay steam;
        private readonly TextBlock stage, clockText;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<double> ticks = new List<double>();
        private readonly List<double> rigCosts = new List<double>();
        private readonly List<double> compositionTimes = new List<double>();
        private double lastTick = -1, previousClock, elapsed, manualTeaStart = -1;
        private bool paused, exploded;
        private string mode = "auto";
        private readonly string evidence;
        private readonly bool automaticExit;
        private int nextCapture;
        private readonly double[] captureTimes = { .1, 2.2, 5.5, 7.8, 9.0, 12.4, 14.0 };
        private readonly string commandPath;

        [STAThread]
        public static void Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string evidence = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "evidence");
            bool autoExit = args.Contains("--capture");
            Directory.CreateDirectory(evidence);
            new Application().Run(new Preview(root, evidence, autoExit));
        }

        public Preview(string root, string evidenceDirectory, bool exitAfterCapture)
        {
            evidence = evidenceDirectory; automaticExit = exitAfterCapture;
            commandPath = Path.Combine(evidence, "command.txt");
            Title = "Layered Dragon Preview";
            Width = 590; Height = Math.Min(710, SystemParameters.WorkArea.Height - 30);
            MinHeight = 510; MinWidth = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = ColorBrush("#F2EFEA"); FontFamily = new FontFamily("Microsoft YaHei UI");
            var layout = new Grid { Margin = new Thickness(28, 20, 28, 18) };
            foreach (var h in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
                layout.RowDefinitions.Add(new RowDefinition { Height = h });
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = "小龙 · 动作小样", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = ColorBrush("#343140") });
            heading.Children.Add(new TextBlock { Text = "待机与喝茶", FontSize = 12, Foreground = ColorBrush("#77727D"), Margin = new Thickness(0, 5, 0, 0) });
            layout.Children.Add(heading);

            artwork = new Grid { Width = 512, Height = 512, ClipToBounds = false };
            var viewport = new Viewport3D { IsHitTestVisible = false, ClipToBounds = false,
                Camera = new OrthographicCamera(new Point3D(256, -256, 1000), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 560) };
            RenderOptions.SetBitmapScalingMode(viewport, BitmapScalingMode.HighQuality);
            rig = new DragonRig(Path.Combine(root, "assets")); viewport.Children.Add(rig.Visual);
            artwork.Children.Add(viewport);
            steam = new SteamOverlay(rig); artwork.Children.Add(steam);
            var artView = new Viewbox { Stretch = Stretch.Uniform, Child = artwork, Margin = new Thickness(0, 9, 0, 0) };
            Grid.SetRow(artView, 1); layout.Children.Add(artView);
            var caption = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 13) };
            stage = new TextBlock { FontSize = 15, Foreground = ColorBrush("#4F485B"), HorizontalAlignment = HorizontalAlignment.Center };
            clockText = new TextBlock { FontSize = 11, Foreground = ColorBrush("#8C8691"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            caption.Children.Add(stage); caption.Children.Add(clockText); Grid.SetRow(caption, 2); layout.Children.Add(caption);
            var controls = new UniformGrid { Columns = 4, HorizontalAlignment = HorizontalAlignment.Center };
            Button pause = null, explode = null;
            controls.Children.Add(Button("待机", delegate {
                mode = "idle"; manualTeaStart = -1; paused = false; exploded = false;
                pause.Content = "暂停"; explode.Content = "拆开看看";
            }));
            controls.Children.Add(Button("喝茶", delegate {
                mode = "tea"; manualTeaStart = elapsed; paused = false; exploded = false;
                pause.Content = "暂停"; explode.Content = "拆开看看";
            }));
            pause = Button("暂停", null); pause.Click += delegate { paused = !paused; pause.Content = paused ? "继续" : "暂停"; }; controls.Children.Add(pause);
            explode = Button("拆开看看", null); explode.Click += delegate {
                exploded = !exploded; explode.Content = exploded ? "合起来" : "拆开看看";
                mode = "tea"; manualTeaStart = elapsed - 5;
            }; controls.Children.Add(explode);
            Grid.SetRow(controls, 3); layout.Children.Add(controls); Content = layout;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); if (e.Key == Key.Space) paused = !paused; };
            var renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
            renderTimer.Tick += Render;
            Loaded += delegate { renderTimer.Start(); CompositionTarget.Rendering += ObserveComposition; Activate(); };
            Closed += delegate { renderTimer.Stop(); CompositionTarget.Rendering -= ObserveComposition; SaveMetrics(); };
            var commands = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            commands.Tick += delegate {
                if (!File.Exists(commandPath)) return;
                string command = File.ReadAllText(commandPath).Trim();
                if (command == "close") Close();
            }; commands.Start();
        }
        private static SolidColorBrush ColorBrush(string color)
        { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush; }
        private static Button Button(string label, Action action)
        {
            var button = new Button { Content = label, FontSize = 12, Padding = new Thickness(13, 9, 13, 9), Margin = new Thickness(4, 0, 4, 0),
                Background = Brushes.White, BorderBrush = ColorBrush("#DBD5DF"), Foreground = ColorBrush("#51495D"), Cursor = Cursors.Hand };
            if (action != null) button.Click += delegate { action(); };
            return button;
        }
        private void Render(object sender, EventArgs args)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (lastTick >= 0 && now - lastTick < 1.0 / 70) return;
            double delta = now - previousClock; previousClock = now; lastTick = now;
            if (!paused) elapsed += delta;
            ticks.Add(now * 1000);
            double teaTime = -1;
            if (mode == "auto") { double t = elapsed % 17; if (t >= 3 && t < 13) teaTime = t - 3; }
            else if (mode == "tea") { teaTime = exploded ? 5 : elapsed - manualTeaStart; if (teaTime >= 10) { mode = "idle"; teaTime = -1; } }
            var updateWatch = Stopwatch.StartNew();
            rig.Update(elapsed, teaTime, exploded); steam.InvalidateVisual();
            rigCosts.Add(updateWatch.Elapsed.TotalMilliseconds);
            stage.Text = exploded ? "身体 / 左袖与手 / 右袖与手 / 茶杯" : rig.Current.Stage;
            clockText.Text = teaTime >= 0 ? string.Format("{0:0.0} / 10.0 秒", teaTime) : "轻轻呼吸，偶尔眨眼";
            if (nextCapture < captureTimes.Length && elapsed >= captureTimes[nextCapture])
            {
                SaveSnapshot(string.Format("frame-{0:00}-{1:0.0}s.png", nextCapture, elapsed)); nextCapture++;
            }
            if (automaticExit && elapsed > 16.6) Close();
        }
        private void ObserveComposition(object sender, EventArgs args)
        { compositionTimes.Add(clock.Elapsed.TotalMilliseconds); }
        private void SaveSnapshot(string name)
        {
            var image = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.Combine(evidence, name))) encoder.Save(output);
        }
        private void SaveMetrics()
        {
            var intervals = ticks.Skip(1).Select((t, i) => t - ticks[i]).OrderBy(v => v).ToArray();
            var data = new { mode = "continuous layered mesh/keyframe animation", elapsedSeconds = elapsed, renderUpdates = ticks.Count,
                averageUpdateFps = ticks.Count < 2 ? 0 : (ticks.Count - 1) * 1000.0 / (ticks.Last() - ticks.First()),
                p95UpdateIntervalMs = intervals.Length == 0 ? 0 : intervals[(int)(intervals.Length * .95)],
                maxUpdateIntervalMs = intervals.Length == 0 ? 0 : intervals.Last(),
                meanRigUpdateMs = rigCosts.Count == 0 ? 0 : rigCosts.Average(),
                maxRigUpdateMs = rigCosts.Count == 0 ? 0 : rigCosts.Max(),
                compositionEvents = compositionTimes.Count,
                averageCompositionFps = compositionTimes.Count < 2 ? 0 : (compositionTimes.Count - 1) * 1000.0 / (compositionTimes.Last() - compositionTimes.First()),
                intervalsAbove100Ms = intervals.Count(v => v > 100),
                renderTier = RenderCapability.Tier >> 16, originalFaceTextureRetained = true,
                peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                note = "WPF render update timings; visual quality is evaluated separately from these timings." };
            File.WriteAllText(Path.Combine(evidence, "playback-metrics.json"), new JavaScriptSerializer().Serialize(data), Encoding.UTF8);
        }
    }
}
