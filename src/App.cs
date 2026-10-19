using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexDragon
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--diagnose")
                {
                    string path = args.Length > 1 ? args[1] : System.IO.Path.Combine(Settings.Folder, "diagnostics.json");
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
                    using (var monitor = new SessionMonitor(SessionRoot()))
                    {
                        monitor.Poll(); var account = AccountReader.Read(AccountReader.FindCodex());
                        File.WriteAllText(path, Json.Write(new { connection = monitor.Connection, sessions = monitor.Sessions.Count, running = monitor.Sessions.Values.Count(s => s.Running && !s.IsChild), quota = account.Select(q => new { id = q.Id, windows = q.Windows.Select(w => new { used = w.Used, minutes = w.Minutes }), source = q.Source }), at = DateTime.UtcNow.ToString("o") }));
                    }
                    return 0;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                if (args.Length > 0 && (args[0] == "--preview" || args[0] == "--preview-dashboard"))
                {
                    var preview = new PetWindow(false);
                    if (args[0] == "--preview-dashboard") preview.PreviewDashboard(args.Length > 1 ? args[1] : "dashboard.png");
                    else preview.Preview(args.Length > 1 ? args[1] : "preview.png");
                    return 0;
                }
                bool first;
                using (var mutex = new Mutex(true, @"Local\CodexDragonPet", out first))
                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CodexDragonPetShow"))
                {
                    if (!first) { show.Set(); return 0; }
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        LogError(e.Exception); e.Handled = true;
                    };
                    var window = new PetWindow(true);
                    if (args.Length > 1 && args[0] == "--verify-live")
                    {
                        string evidence = args[1];
                        window.Loaded += delegate
                        {
                            var check = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                            check.Tick += delegate { check.Stop(); window.SaveEvidence(evidence); }; check.Start();
                        };
                    }
                    var registration = ThreadPool.RegisterWaitForSingleObject(show, delegate
                    { if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(new Action(window.Show)); }, null, Timeout.Infinite, false);
                    try { app.Run(window); }
                    finally { registration.Unregister(null); mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception e)
            {
                LogError(e);
                if (args.Length == 0) MessageBox.Show("小龙启动遇到问题。请查看：\n" + System.IO.Path.Combine(Settings.Folder, "error.log"), "Codex 小龙");
                return 1;
            }
        }
        public static string SessionRoot()
        {
            string codexRoot = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrEmpty(codexRoot)) codexRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return System.IO.Path.Combine(codexRoot, "sessions");
        }
        public static void LogError(Exception e)
        {
            try { Directory.CreateDirectory(Settings.Folder); File.AppendAllText(System.IO.Path.Combine(Settings.Folder, "error.log"), DateTime.Now.ToString("s") + " " + e.GetType().Name + ": " + e.Message + "\r\n"); } catch { }
        }
    }

    public static class Ui
    {
        public static readonly Brush Ink = Brush("#30283D"), Muted = Brush("#776F84"), Accent = Brush("#8063AB"), Soft = Brush("#F4EFFA"), Line = Brush("#E4DBEE");
        public static Brush Brush(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
        public static TextBlock Text(string text, double size, Brush color)
        { return new TextBlock { Text = text, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Microsoft YaHei UI"), LineHeight = size * 1.55 }; }
        public static Button Button(string text, Action action)
        {
            var b = new Button { Content = text, Background = Soft, Foreground = Ink, BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, FontSize = 12, FontFamily = new FontFamily("Microsoft YaHei UI") };
            var template = new ControlTemplate(typeof(Button));
            var surface = new FrameworkElementFactory(typeof(Border), "Surface");
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            surface.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            surface.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            surface.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            surface.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content");
            presenter.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            surface.AppendChild(presenter); template.VisualTree = surface;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#EAE1F3"), "Surface")); template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Surface")); template.Triggers.Add(focus);
            b.Template = template;
            b.Click += delegate { action(); }; return b;
        }
        public static string Short(string text, int length) { return text == null ? "" : text.Length <= length ? text : text.Substring(0, length) + "…"; }
        public static string Percent(double value) { return value.ToString("0.#", CultureInfo.InvariantCulture) + "%"; }
        public static string TimeSince(DateTime at)
        {
            if (at == DateTime.MinValue) return "尚未更新";
            var elapsed = DateTime.UtcNow - at;
            if (elapsed.TotalSeconds < 10) return "刚刚更新";
            if (elapsed.TotalMinutes < 1) return (int)elapsed.TotalSeconds + " 秒前更新";
            if (elapsed.TotalHours < 1) return (int)elapsed.TotalMinutes + " 分钟前更新";
            return at.ToLocalTime().ToString("MM-dd HH:mm");
        }
    }

    public sealed partial class PetWindow : Window
    {
        private readonly Settings settings;
        private readonly Grid scene;
        private readonly Image sprite;
        private readonly Border bubble;
        private readonly TextBlock bubbleTitle, bubbleText, statusText, quotaText;
        private readonly BitmapSource[] sprites = new BitmapSource[6];
        private readonly TranslateTransform floatTransform = new TranslateTransform();
        private readonly RotateTransform tiltTransform = new RotateTransform();
        private readonly DispatcherTimer timer;
        private readonly SessionMonitor monitor;
        private Forms.NotifyIcon tray;
        private System.Drawing.Icon trayArt;
        private Window dashboard;
        private TextBlock dashStatus, dashMessage, dashConnection;
        private StackPanel quotaPanel;
        private ComboBox sessionPicker;
        private bool updatingPicker, closed, dragging;
        private System.Windows.Point down;
        private DateTime lastQuotaRequest = DateTime.MinValue, speechUntil = DateTime.MinValue, lastTalk = DateTime.UtcNow;
        private int lastThreshold;
        private string lastThresholdWindow = "", lastCompletedId = "";
        private string speech = "", speechTitle = "", quotaError = "";
        private bool quotaBusy;
        private string quotaRenderKey = "";
        private readonly Random random = new Random();
        private Session selected;
        private Quota quota;
        private readonly string[] idleLines = { "你忙你的，我在这里陪你。", "代码可以慢慢写，水也要记得喝。", "翅膀收好了，不挡你的编辑器。", "今天也一起把事情做好。", "戳一下小龙，就能打开工作面板。" };

        public PetWindow(bool live)
        {
            settings = live ? Settings.Load() : new Settings();
            settings.Scale = Math.Max(.65, Math.Min(1.5, settings.Scale));
            Title = "Codex 小龙 · 桌面伙伴"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
            Topmost = settings.Topmost; Width = 330 * settings.Scale; Height = 438 * settings.Scale;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            scene = new Grid { Width = 330, Height = 438, Background = null };
            var view = new Viewbox { Child = scene, Stretch = Stretch.Uniform }; Content = view;
            LoadSprites();

            var bubbleStack = new StackPanel();
            bubbleTitle = Ui.Text("Codex 小龙", 11, Ui.Accent); bubbleTitle.FontWeight = FontWeights.SemiBold;
            bubbleText = Ui.Text("你好，我来陪你工作啦。", 12, Ui.Ink); bubbleText.Margin = new Thickness(0, 5, 0, 0); bubbleText.MaxHeight = 76; bubbleText.TextTrimming = TextTrimming.CharacterEllipsis;
            bubbleStack.Children.Add(bubbleTitle); bubbleStack.Children.Add(bubbleText);
            bubble = new Border { Child = bubbleStack, Width = 292, Padding = new Thickness(15, 11, 15, 12), CornerRadius = new CornerRadius(17, 17, 5, 17), Background = Ui.Brush("#FAFFFDFF"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 304, 0) };
            // Anchor the bubble above the character without covering her face.
            bubble.Margin = new Thickness(0, 0, 0, 300); scene.Children.Add(bubble);

            var shadow = new Ellipse { Width = 128, Height = 11, Fill = Ui.Brush("#187B668D"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 49), IsHitTestVisible = false };
            scene.Children.Add(shadow);
            var transform = new TransformGroup(); transform.Children.Add(tiltTransform); transform.Children.Add(floatTransform);
            previousSprite = new Image { Source = sprites[0], Opacity = 0, IsHitTestVisible = false, Width = 258, Height = 258, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 48), RenderTransform = transform, RenderTransformOrigin = new System.Windows.Point(.5, .85) };
            scene.Children.Add(previousSprite);
            sprite = new Image { Name = "PetCharacter", Source = sprites[0], Width = 258, Height = 258, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 48), RenderTransform = transform, RenderTransformOrigin = new System.Windows.Point(.5, .85), Cursor = Cursors.Hand, ToolTip = "单击摸摸头 · 拖动调整位置 · 双击工作面板" };
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
            scene.Children.Add(sprite);

            var statusStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            statusText = Ui.Text("● 在这里陪你", 10, Ui.Ink); statusStack.Children.Add(statusText);
            quotaText = Ui.Text("  ·  额度连接中", 10, Ui.Muted); statusStack.Children.Add(quotaText);
            var status = new Border { Name = "PetStatus", Child = statusStack, Padding = new Thickness(13, 7, 13, 7), CornerRadius = new CornerRadius(15), Background = Ui.Brush("#F5FFFDFF"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 9), Cursor = Cursors.Hand, ToolTip = "打开工作与额度面板" };
            status.MouseLeftButtonUp += delegate { OpenDashboard(); }; scene.Children.Add(status);

            sprite.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) { OpenDashboard(); e.Handled = true; return; }
                down = e.GetPosition(this); dragging = false;
            };
            sprite.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.LeftButton != MouseButtonState.Pressed || dragging) return;
                var position = e.GetPosition(this);
                if (Math.Abs(position.X - down.X) + Math.Abs(position.Y - down.Y) < 5) return;
                dragging = true;
                if (!settings.ReduceMotion) AnimateTilt(4, .15);
                try { DragMove(); } catch (InvalidOperationException) { }
                if (!settings.ReduceMotion) AnimateTilt(0, .25);
                ClampPosition(); SavePosition();
            };
            sprite.MouseLeftButtonUp += delegate { if (!dragging) PetHead(); };
            ContextMenu = MakeMenu();
            InitializeCompanion();
            IsVisibleChanged += delegate { ApplyMotion(); };
            Loaded += delegate { SetPosition(); };
            Closing += delegate { Cleanup(); };

            if (live)
            {
                monitor = new SessionMonitor(Program.SessionRoot());
                monitor.Completed += OnCompleted;
                SetupTray();
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += delegate { Tick(); }; timer.Start(); Tick();
                Say("Codex 小龙", "已就位，陪你工作。", 8);
            }
            else monitor = new SessionMonitor(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codex-pet-preview-no-sessions"));
        }

        private void LoadSprites()
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "dragon-sprites-clean.png");
            var sheet = new BitmapImage(); sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad; sheet.UriSource = new Uri(path); sheet.EndInit(); sheet.Freeze();
            int width = sheet.PixelWidth / 3, height = sheet.PixelHeight / 2;
            for (int i = 0; i < 6; i++)
            {
                // Runtime source rectangles preserve the original generated alpha and artwork.
                var image = new CroppedBitmap(sheet, new Int32Rect((i % 3) * width, (i / 3) * height, width, height)); image.Freeze(); sprites[i] = image;
            }
        }
        private void Tick()
        {
            if (closed) return;
            try
            {
                monitor.Poll(); selected = monitor.Select(settings.PinnedThread); quota = monitor.PrimaryQuota;
                if (DateTime.UtcNow - lastQuotaRequest > TimeSpan.FromSeconds(60))
                {
                    if (lastQuotaRequest != DateTime.MinValue && quota != null && quota.Source == "Codex 聊天记录" && DateTime.UtcNow - quota.Updated < TimeSpan.FromSeconds(45)) lastQuotaRequest = DateTime.UtcNow;
                    else RefreshQuota();
                }
                ObserveProgress();
                CheckThreshold(); RefreshView();
                if (settings.SmallTalk && !settings.Quiet && DateTime.UtcNow > speechUntil && (selected == null || !selected.Running) && DateTime.UtcNow - lastTalk > TimeSpan.FromSeconds(100))
                { Say("角色碎碎念", idleLines[random.Next(idleLines.Length)], 10); lastTalk = DateTime.UtcNow; }
            }
            catch (Exception e) { Program.LogError(e); }
        }
        private void RefreshQuota()
        {
            if (quotaBusy || closed) return;
            quotaBusy = true; lastQuotaRequest = DateTime.UtcNow;
            Task.Factory.StartNew(delegate { return AccountReader.Read(AccountReader.FindCodex()); }).ContinueWith(task =>
            {
                if (closed || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    quotaBusy = false; if (closed) return;
                    if (task.IsFaulted) { quotaError = task.Exception.GetBaseException().Message; }
                    else { foreach (var q in task.Result) monitor.SetQuota(q); quotaError = ""; quota = monitor.PrimaryQuota; }
                    RefreshView();
                }));
            });
        }
        private void OnCompleted(Session session)
        {
            if (!settings.Notifications) return;
            if (!String.IsNullOrEmpty(settings.PinnedThread) && session.Id != settings.PinnedThread) return;
            lastCompletedId = session.Id;
            Say("本轮工作结束啦", "回来看看结果吧。", 25); SetMood(Mood.Done); Wiggle();
            if (settings.Sound && !settings.Quiet) SystemSounds.Asterisk.Play();
            if (tray != null && !settings.Quiet)
            {
                tray.BalloonTipTitle = "Codex 小龙：本轮工作结束啦";
                tray.BalloonTipText = "回到 Codex 查看结果。";
                tray.ShowBalloonTip(6000);
            }
        }
        private void CheckThreshold()
        {
            var window = quota == null ? null : quota.Main;
            if (window == null || !quota.Fresh || window.Expired) return;
            string windowKey = quota.Id + ":" + window.Minutes + ":" + (window.Reset.HasValue ? window.Reset.Value.Ticks.ToString() : "unknown");
            if (windowKey != lastThresholdWindow) { lastThreshold = 0; lastThresholdWindow = windowKey; }
            int threshold = window.Used >= 95 ? 95 : window.Used >= 80 ? 80 : 0;
            if (threshold == 0) { lastThreshold = 0; return; }
            if (threshold <= lastThreshold) return;
            lastThreshold = threshold;
            Say("额度提醒", window.Label + "已用 " + Ui.Percent(window.Used) + "，剩余 " + Ui.Percent(100 - window.Used) + "。", 20);
            if (settings.Notifications && !settings.Quiet && tray != null) tray.ShowBalloonTip(6000, "Codex 额度提醒", window.Label + "已用 " + Ui.Percent(window.Used), Forms.ToolTipIcon.Info);
        }

        private void RefreshView()
        {
            Mood mood = selected == null ? Mood.Idle : selected.Mood;
            var window = quota == null ? null : quota.Main;
            if (window != null && quota.Fresh && !window.Expired && window.Used >= 80 && (selected == null || !selected.Running)) mood = Mood.LowQuota;
            if (DateTime.UtcNow < speechUntil && speechTitle == "本轮工作结束啦") mood = Mood.Done;
            SetMood(mood);
            string stage = selected == null ? "待命中" : selected.Stage;
            statusText.Text = "● " + Ui.Short(stage, 12);
            statusText.Foreground = selected != null && selected.Running ? Ui.Accent : Ui.Ink;
            quotaText.Text = (window == null ? "  ·  额度待获取" : "  ·  已用 " + Ui.Percent(window.Used) + ((!quota.Fresh || window.Expired) ? " *" : "")) + (settings.ClickThrough ? " · 穿透" : "");
            quotaText.Foreground = window != null && window.Used >= 80 ? Ui.Brush("#AA6646") : Ui.Muted;
            if (DateTime.UtcNow < speechUntil)
            { bubbleTitle.Text = speechTitle; bubbleText.Text = speech; }
            else
            {
                bubbleTitle.Text = selected != null && selected.Running ? "Codex · 工作进展" : "Codex 小龙";
                bubbleText.Text = selected != null && selected.Running ? stage : mood == Mood.Done ? "结果已就绪。" : "我在这里。";
            }
            UpdateBubbleVisibility();
            if (tray != null) tray.Text = Ui.Short("Codex 小龙 · " + stage, 60);
            RefreshDashboard();
        }
        private void SetMood(Mood mood)
        {
            int frame = mood == Mood.Working ? 1 : mood == Mood.Thinking ? 2 : mood == Mood.Done ? 3 : mood == Mood.Waiting ? 4 : mood == Mood.LowQuota || mood == Mood.Stopped ? 5 : 0;
            if (frame == activeFrame) return;
            bool animate = activeFrame >= 0 && IsLoaded && IsVisible && !settings.ReduceMotion;
            previousSprite.Source = sprite.Source;
            sprite.Source = sprites[frame];
            activeFrame = frame;
            if (animate)
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(.22)) { FillBehavior = FillBehavior.Stop };
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(.22)) { FillBehavior = FillBehavior.Stop };
                Timeline.SetDesiredFrameRate(fadeIn, 24); Timeline.SetDesiredFrameRate(fadeOut, 24);
                sprite.BeginAnimation(UIElement.OpacityProperty, fadeIn); previousSprite.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }
        private void Say(string title, string text, int seconds)
        { speechTitle = title; speech = text; speechUntil = DateTime.UtcNow.AddSeconds(seconds); bubbleTitle.Text = title; bubbleText.Text = text; UpdateBubbleVisibility(); }
        private void Wiggle()
        {
            if (settings.ReduceMotion) return;
            var wave = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(.65) };
            wave.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromPercent(.2)));
            wave.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromPercent(.5)));
            wave.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Timeline.SetDesiredFrameRate(wave, 24);
            tiltTransform.BeginAnimation(RotateTransform.AngleProperty, wave);
        }
        private ContextMenu MakeMenu()
        {
            var menu = new ContextMenu { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13 };
            AddMenu(menu, "工作与额度面板", OpenDashboard);
            AddMenu(menu, "回到当前 Codex 聊天", OpenCodex);
            menu.Items.Add(new Separator());
            AddMenu(menu, settings.ClickThrough ? "关闭鼠标穿透" : "开启鼠标穿透", delegate { SetClickThrough(!settings.ClickThrough); });
            AddMenu(menu, settings.Quiet ? "恢复日常陪伴" : "安静陪伴", delegate { SetQuiet(!settings.Quiet); });
            AddMenu(menu, settings.Bubble ? "隐藏气泡" : "显示气泡", delegate { settings.Bubble = !settings.Bubble; Persist(); RefreshView(); ContextMenu = MakeMenu(); });
            AddMenu(menu, settings.Sound ? "关闭提示音" : "开启提示音", delegate { settings.Sound = !settings.Sound; Persist(); ContextMenu = MakeMenu(); });
            AddMenu(menu, settings.Topmost ? "取消置顶" : "置顶显示", delegate { settings.Topmost = !settings.Topmost; Topmost = settings.Topmost; Persist(); ContextMenu = MakeMenu(); });
            var sizes = new MenuItem { Header = "调整大小" };
            foreach (double size in new[] { .75, 1.0, 1.25, 1.5 })
            { double value = size; var item = new MenuItem { Header = (size * 100) + "%", IsChecked = Math.Abs(settings.Scale - size) < .01 }; item.Click += delegate { settings.Scale = value; Width = 330 * value; Height = 438 * value; ClampPosition(); SavePosition(); ContextMenu = MakeMenu(); }; sizes.Items.Add(item); }
            menu.Items.Add(sizes); menu.Items.Add(new Separator());
            AddMenu(menu, "收起到托盘", delegate { Hide(); });
            AddMenu(menu, "退出小龙", Close); return menu;
        }
        private static void AddMenu(ContextMenu menu, string title, Action action)
        { var item = new MenuItem { Header = title }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        private void SetupTray()
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "tray.ico");
            trayArt = new System.Drawing.Icon(iconPath, Forms.SystemInformation.SmallIconSize);
            tray = new Forms.NotifyIcon { Icon = trayArt, Text = "Codex 小龙", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示小龙", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { Show(); })); });
            menu.Items.Add("工作与额度面板", null, delegate { Dispatcher.BeginInvoke(new Action(OpenDashboard)); });
            trayClickThrough = new Forms.ToolStripMenuItem("鼠标穿透（取消勾选可恢复互动）") { Checked = settings.ClickThrough };
            trayClickThrough.Click += delegate { Dispatcher.BeginInvoke(new Action(delegate { SetClickThrough(!settings.ClickThrough); })); };
            menu.Items.Add(trayClickThrough);
            menu.Items.Add("退出", null, delegate { Dispatcher.BeginInvoke(new Action(Close)); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(delegate { Show(); OpenDashboard(); })); };
            tray.BalloonTipClicked += delegate { Dispatcher.BeginInvoke(new Action(delegate { OpenThread(lastCompletedId.Length > 0 ? lastCompletedId : selected == null ? "" : selected.Id); })); };
        }
        private void OpenCodex()
        {
            OpenThread(selected == null ? "" : selected.Id);
        }
        private void OpenThread(string id)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[0-9a-f-]{36}$")) return;
            try { Process.Start(new ProcessStartInfo("codex://threads/" + id) { UseShellExecute = true }); }
            catch { Say("打开 Codex", "请在任务栏里切回 Codex 查看结果。", 12); }
        }

        private void OpenDashboard()
        { OpenDashboard(true); }
        private void OpenDashboard(bool show)
        {
            if (dashboard != null) { dashboard.Activate(); return; }
            dashboard = new Window { Title = "Codex 小龙 · 工作与额度", Width = 610, Height = 760, MinWidth = 540, MinHeight = 600, ResizeMode = ResizeMode.CanResize, Background = Ui.Brush("#FFFDFF"), FontFamily = new FontFamily("Microsoft YaHei UI"), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            var content = new StackPanel { Margin = new Thickness(28, 22, 28, 24) };
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            var words = new StackPanel(); words.Children.Add(Ui.Text("一起，把事情做好。", 24, Ui.Ink)); var subtitle = Ui.Text("Codex 小龙 / 你的桌面伙伴", 12, Ui.Muted); subtitle.Margin = new Thickness(0, 7, 0, 0); words.Children.Add(subtitle); heading.Children.Add(words);
            var portrait = new Image { Source = sprites[0], Width = 90, Height = 90 }; Grid.SetColumn(portrait, 1); heading.Children.Add(portrait); content.Children.Add(heading);
            var rule = new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 10, 0, 20) }; content.Children.Add(rule);
            content.Children.Add(Ui.Text("跟随的聊天", 11, Ui.Muted));
            sessionPicker = new ComboBox { Background = Brushes.White, BorderBrush = Ui.Line, Foreground = Ui.Ink, Margin = new Thickness(0, 6, 0, 16), Padding = new Thickness(7), FontSize = 12 };
            sessionPicker.SelectionChanged += delegate
            {
                if (updatingPicker) return; var item = sessionPicker.SelectedItem as ComboBoxItem;
                if (item == null) return; settings.PinnedThread = Convert.ToString(item.Tag); Persist(); Tick();
            }; content.Children.Add(sessionPicker); PopulatePicker();
            dashStatus = Ui.Text("正在连接", 17, Ui.Ink); content.Children.Add(dashStatus);
            dashMeta = Ui.Text("", 10, Ui.Accent); dashMeta.Margin = new Thickness(0, 3, 0, 0); content.Children.Add(dashMeta);
            dashMessage = Ui.Text("", 12, Ui.Muted); dashMessage.Margin = new Thickness(0, 7, 0, 20); dashMessage.MaxHeight = 94; dashMessage.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(dashMessage);
            content.Children.Add(Ui.Text("账户额度 · 已用比例", 11, Ui.Muted));
            quotaPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) }; content.Children.Add(quotaPanel);
            dashConnection = Ui.Text("", 10, Ui.Muted); dashConnection.Margin = new Thickness(0, 0, 0, 16); content.Children.Add(dashConnection);
            var actions = new WrapPanel(); actions.Children.Add(Ui.Button("回到 Codex", OpenCodex)); actions.Children.Add(Ui.Button("刷新额度", RefreshQuota)); actions.Children.Add(Ui.Button("演示完成提醒", delegate { Say("演示 · 完成提醒", "叮！这是一条示例提醒，实际任务状态没有改变。", 12); SetMood(Mood.Done); Wiggle(); if (settings.Sound && !settings.Quiet) SystemSounds.Asterisk.Play(); if (tray != null && !settings.Quiet) tray.ShowBalloonTip(5000, "演示 · 完成提醒", "这是示例提醒，不代表实际任务已经完成。", Forms.ToolTipIcon.Info); })); content.Children.Add(actions);
            var options = new WrapPanel { Margin = new Thickness(0, 9, 0, 15) };
            AddCheck(options, "提示音", settings.Sound, delegate(bool value) { settings.Sound = value; });
            AddCheck(options, "完成提醒", settings.Notifications, delegate(bool value) { settings.Notifications = value; });
            AddCheck(options, "日常碎碎念", settings.SmallTalk, delegate(bool value) { settings.SmallTalk = value; }); content.Children.Add(options);
            AddCompanionOptions(content);
            content.Children.Add(Ui.Text("工作进展来自公开的状态与消息；碎碎念是角色台词。\n额度每分钟刷新，也随聊天记录更新；本轮结束提醒以 Codex 完成事件为准。", 10, Ui.Muted));
            dashboard.Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.Brush("#FFFDFF") };
            dashboard.Closed += delegate { dashboard = null; dashStatus = null; dashMeta = null; recentPanel = null; clickThroughCheck = null; sessionPicker = null; quotaPanel = null; };
            quotaRenderKey = "";
            RefreshDashboard(); if (show) dashboard.Show();
        }
        private void AddCheck(Panel panel, string title, bool value, Action<bool> changed)
        {
            var check = new CheckBox { Content = title, IsChecked = value, FontSize = 11, Foreground = Ui.Ink, Margin = new Thickness(0, 0, 20, 5), VerticalContentAlignment = VerticalAlignment.Center };
            check.Click += delegate { changed(check.IsChecked == true); Persist(); ContextMenu = MakeMenu(); }; panel.Children.Add(check);
        }
        private void PopulatePicker()
        {
            if (sessionPicker == null || monitor == null) return;
            updatingPicker = true; sessionPicker.Items.Clear();
            sessionPicker.Items.Add(new ComboBoxItem { Content = "自动跟随正在工作的聊天", Tag = "" });
            int selection = 0;
            foreach (var session in monitor.Sessions.Values.Where(s => !s.IsChild).OrderByDescending(s => s.Updated).Take(16))
            {
                string title = System.IO.Path.GetFileName(session.Project.TrimEnd('\\', '/'));
                sessionPicker.Items.Add(new ComboBoxItem { Content = (String.IsNullOrEmpty(title) ? "聊天" : title) + " · " + Ui.Short(session.Id, 8), Tag = session.Id });
                if (session.Id == settings.PinnedThread) selection = sessionPicker.Items.Count - 1;
            }
            if (settings.PinnedThread.Length > 0 && selection == 0)
            { sessionPicker.Items.Add(new ComboBoxItem { Content = "已固定的聊天 · 等待活动", Tag = settings.PinnedThread }); selection = sessionPicker.Items.Count - 1; }
            sessionPicker.SelectedIndex = selection; updatingPicker = false;
        }
        private void RefreshDashboard()
        {
            if (dashboard == null || dashStatus == null) return;
            dashStatus.Text = selected == null ? "待命中" : selected.Stage;
            dashMessage.Text = selected == null ? "打开 Codex 开始聊天，我就会跟随工作进展。" : (selected.Mood == Mood.Done && selected.Result.Length > 0 ? selected.Result : selected.PublicMessage.Length > 0 ? selected.PublicMessage : "等待新的公开工作进展。") + "\n" + Ui.TimeSince(selected.Updated);
            RefreshCompanionInfo();
            string newQuotaKey = String.Join("|", monitor.Quotas.Values.Select(q => q.Id + q.Name + q.Fresh + String.Join(",", q.Windows.Select(w => w.Used + ":" + w.Minutes + ":" + w.Reset + ":" + w.Expired))));
            if (newQuotaKey != quotaRenderKey || quotaPanel.Children.Count == 0)
            {
            quotaRenderKey = newQuotaKey;
            quotaPanel.Children.Clear();
            var quotas = monitor.Quotas.Values.OrderBy(q => q.Id == "codex" ? 0 : 1).ToArray();
            foreach (var q in quotas)
            {
                foreach (var window in q.Windows)
                {
                    quotaPanel.Children.Add(Ui.Text(q.Name + " · " + window.Label + "    已用 " + Ui.Percent(window.Used) + " / 剩余 " + Ui.Percent(100 - window.Used), 12, Ui.Ink));
                    quotaPanel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = window.Used, Height = 6, Foreground = window.Used >= 80 ? Ui.Brush("#BF805B") : Ui.Accent, Background = Ui.Soft, BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 6) });
                    quotaPanel.Children.Add(Ui.Text((window.Reset.HasValue ? "重置时间 " + window.Reset.Value.ToLocalTime().ToString("MM-dd HH:mm") : "重置时间暂不可用") + (window.Expired ? " · 等待新数据" : "") + (!q.Fresh ? " · 数据较旧" : ""), 10, Ui.Muted));
                    quotaPanel.Children.Add(new Border { Height = 14 });
                }
            }
            if (quotas.Length == 0 || quotas.All(q => q.Windows.Count == 0)) quotaPanel.Children.Add(Ui.Text("账户尚未返回额度数据", 12, Ui.Muted));
            }
            dashConnection.Text = monitor.Connection + " · " + (quotaBusy ? "正在刷新额度" : quota != null ? quota.Source + " · " + Ui.TimeSince(quota.Updated) : "等待账户连接") + (quotaError.Length > 0 ? "\n" + Ui.Short(quotaError, 80) : "");
        }
        private void SetPosition()
        {
            var area = SystemParameters.WorkArea;
            Left = settings.Left <= -999999 ? area.Right - Width - 22 : settings.Left;
            Top = settings.Top <= -999999 ? area.Bottom - Height - 12 : settings.Top; ClampPosition();
        }
        private void ClampPosition()
        {
            // Work in WPF device-independent units; retain a visible drag target after resolution changes.
            var screens = Forms.Screen.AllScreens;
            var source = PresentationSource.FromVisual(this);
            var transform = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
            var visible = screens.Select(s => { var p = transform.Transform(new System.Windows.Point(s.WorkingArea.Left, s.WorkingArea.Top)); var end = transform.Transform(new System.Windows.Point(s.WorkingArea.Right, s.WorkingArea.Bottom)); return new Rect(p, end); }).ToArray();
            if (visible.Any(r => r.Contains(new System.Windows.Point(Left + Width / 2, Top + Height * .7)))) return;
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width)); Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
        }
        private void SavePosition() { settings.Left = Left; settings.Top = Top; Persist(); }
        private void Persist() { try { settings.Save(); } catch (IOException) { Say("设置未保存", "当前设置仍然生效，稍后再试。", 8); } catch (UnauthorizedAccessException) { } }
        private void Cleanup()
        {
            if (closed) return; closed = true;
            if (timer != null) timer.Stop();
            if (monitor != null) monitor.Dispose();
            AccountReader.CancelPending();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (trayArt != null) trayArt.Dispose();
            if (dashboard != null) dashboard.Close(); SavePosition();
        }
        public void Preview(string path)
        {
            floatTransform.BeginAnimation(TranslateTransform.YProperty, null);
            scene.Background = Ui.Brush("#F6F4F8");
            SetMood(Mood.Working); bubbleTitle.Text = "Codex · 工作进展";
            bubbleText.Text = "正在检查和测试。";
            statusText.Text = "● 正在工作"; quotaText.Text = "  ·  已用 2%（示例）";
            scene.Measure(new Size(330, 438)); scene.Arrange(new Rect(0, 0, 330, 438)); scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap(660, 876, 192, 192, PixelFormats.Pbgra32); bitmap.Render(scene);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        public void PreviewDashboard(string path)
        {
            selected = new Session("preview") { Stage = "正在检查和测试", PublicMessage = "工作面板预览：小龙会跟随 Codex 的公开工作进展，在本轮结束时提醒你。", Running = true, Updated = DateTime.UtcNow };
            quota = Quota.Read(Json.Parse("{\"limitId\":\"codex\",\"limitName\":\"Codex（示例数据）\",\"primary\":{\"usedPercent\":2,\"windowDurationMins\":10080}}"), DateTime.UtcNow, "预览");
            monitor.SetQuota(quota); OpenDashboard(false);
            var root = (FrameworkElement)dashboard.Content;
            root.Measure(new Size(574, 800)); root.Arrange(new Rect(0, 0, 574, 800)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1148, 1600, 192, 192, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        public void SaveEvidence(string folder)
        {
            Directory.CreateDirectory(folder);
            var live = new RenderTargetBitmap(660, 876, 192, 192, PixelFormats.Pbgra32); live.Render(scene);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(live));
            using (var stream = File.Create(System.IO.Path.Combine(folder, "live-pet.png"))) encoder.Save(stream);
            bool hiddenDashboard = dashboard == null;
            if (hiddenDashboard) OpenDashboard(false);
            var root = (FrameworkElement)dashboard.Content;
            root.Measure(new Size(574, 648)); root.Arrange(new Rect(0, 0, 574, 648)); root.UpdateLayout();
            var view = new RenderTargetBitmap(1148, 1296, 192, 192, PixelFormats.Pbgra32); view.Render(root);
            encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(view));
            using (var stream = File.Create(System.IO.Path.Combine(folder, "live-dashboard.png"))) encoder.Save(stream);
            if (hiddenDashboard) dashboard.Close();
            File.WriteAllText(System.IO.Path.Combine(folder, "live-check.json"), Json.Write(new {
                processId = Process.GetCurrentProcess().Id, windowLoaded = IsLoaded, windowVisible = IsVisible,
                topmost = Topmost, width = ActualWidth, height = ActualHeight,
                connection = monitor.Connection, trackedThreadId = selected == null ? "" : selected.Id,
                stage = selected == null ? "" : selected.Stage, running = selected != null && selected.Running,
                spriteFrame = Array.IndexOf(sprites, sprite.Source), bubbleTitle = bubbleTitle.Text,
                bubbleText = bubbleText.Text, lastCompletedThreadId = lastCompletedId,
                bubbleVisible = bubble.Visibility == Visibility.Visible, pettingCount = pettingCount,
                clickThrough = settings.ClickThrough, nativeClickThrough = NativeWindow.IsClickThrough(new System.Windows.Interop.WindowInteropHelper(this).Handle),
                trayRecoveryAvailable = trayClickThrough != null && trayClickThrough.Enabled,
                customTrayIcon = trayArt != null && trayArt.Handle != IntPtr.Zero,
                quiet = settings.Quiet, reduceMotion = settings.ReduceMotion, breathingAnimated = floatTransform.HasAnimatedProperties,
                renderTier = RenderCapability.Tier >> 16, animationFrameRate = settings.ReduceMotion ? 0 : settings.Quiet ? 3 : 6,
                quota = quota == null ? null : new { source = quota.Source, fresh = quota.Fresh, windows = quota.Windows.Select(w => new { used = w.Used, minutes = w.Minutes }) },
                at = DateTime.UtcNow.ToString("o")
            }));
        }
    }
}
