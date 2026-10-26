using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LinePutScript;
using VPet_Simulator.Core;
using Forms = System.Windows.Forms;
using PetMain = VPet_Simulator.Core.Main;
using static VPet_Simulator.Core.GraphInfo;

namespace CodexVPet;

internal sealed class CompanionWindow : Window, IController
{
    private readonly string profile, settingsFile, snapshotFile;
    private readonly bool demo;
    private readonly string? evidence;
    private HostSettings settings = new();
    private readonly Grid scene = new();
    private readonly TextBlock status = new() { FontSize = 10, Text = "正在准备见面…", Foreground = new SolidColorBrush(Color.FromRgb(94, 77, 113)), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border statusPill;
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private PetMain? pet;
    private GameCore? core;
    private Process? bridge;
    private DateTime nextBridgeAttempt = DateTime.MinValue, minimumSnapshotAt = DateTime.MinValue;
    private int bridgeFailures, bridgeStarts, notificationCount;
    private Forms.NotifyIcon? tray;
    private BridgeState current = new();
    private long observedCompletion = -1;
    private bool working, ready, disposed, positionDirty, transitionActive, pendingCompletion, pointerPaused;
    private int interactionGeneration;
    private string workName = "";
    private string characterName = "桌面伙伴";
    private string characterCredit = "本项目原白龙形象，经参考生成成套动作，VPet 仅提供动画内核。";
    private readonly ConcurrentDictionary<string, int> observedGraphs = new();
    private readonly Stopwatch demoClock = new();
    private readonly List<object> demoActions = new();
    private DispatcherTimer? demoTimer;
    private int frame, demoStep;

    internal CompanionWindow(string profileDirectory, bool demoMode, string? evidenceDirectory)
    {
        profile = profileDirectory; demo = demoMode;
        evidence = evidenceDirectory == null ? null : Path.GetFullPath(evidenceDirectory);
        if (evidence != null) Directory.CreateDirectory(evidence);
        settingsFile = Path.Combine(profile, "vpet-host.json");
        snapshotFile = Path.Combine(profile, "vpet-status.json");
        try { if (File.Exists(settingsFile)) settings = JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(settingsFile)) ?? settings; } catch (JsonException) { }
        settings.Size = Math.Clamp(settings.Size, 150, 340);
        Title = "Codex 桌面伙伴 · VPet"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = settings.Topmost; ShowInTaskbar = false;
        Width = settings.Size; Height = settings.Size + 26;
        scene.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        scene.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        statusPill = new Border { Background = new SolidColorBrush(Color.FromArgb(237, 255, 252, 255)), CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 5, 10, 4), Margin = new Thickness(8, 0, 8, 2), Child = status, Cursor = Cursors.Hand };
        statusPill.MouseLeftButtonUp += (_, _) => ShowStatus();
        Grid.SetRow(statusPill, 1); scene.Children.Add(statusPill); Content = scene;
        Loaded += async (_, _) => await Initialize();
        Closing += (_, _) => { if (!demo) SaveSettings(); Cleanup(); };
        LocationChanged += (_, _) => { if (ready && !demo) positionDirty = true; };
        poll.Tick += (_, _) => { ReadState(); if (positionDirty) SaveSettings(); };
        ContextMenu = BuildMenu();
        PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; ContextMenu = BuildMenu(); ContextMenu.IsOpen = true; };
        PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (pet?.DisplayType.Type != GraphType.Move || core?.Graph == null) return;
            // A native short click or long-press may choose different actions.
            // Retire the walk now, then let the core's hit area choose that action.
            interactionGeneration++; transitionActive = false; pointerPaused = true;
            StopAutonomousControls();
        };
        IsVisibleChanged += (_, _) => ApplyAutonomy();
    }

    private async Task Initialize()
    {
        var area = SystemParameters.WorkArea;
        Left = settings.Left.HasValue && double.IsFinite(settings.Left.Value) ? settings.Left.Value : area.Right - Width - 24;
        Top = settings.Top.HasValue && double.IsFinite(settings.Top.Value) ? settings.Top.Value : area.Bottom - Height - 12;
        if (demo) { Left = area.Left + 70; Top = area.Top + 70; }
        Clamp();
        string assets = Path.Combine(AppContext.BaseDirectory, "assets", "vpet");
        if (!File.Exists(Path.Combine(assets, "vup.lps"))) throw new FileNotFoundException("完整的 VPet 动画资源尚未安装。", Path.Combine(assets, "vup.lps"));
        GraphCore.CachePath = Path.Combine(profile, "vpet-cache");
        string configuration = File.ReadAllText(Path.Combine(assets, "vup.lps"));
        var nameMatch = Regex.Match(configuration, @"petname#([^:|\r\n]+)");
        if (nameMatch.Success) characterName = nameMatch.Groups[1].Value;
        string creditFile = Path.Combine(assets, "CHARACTER-CREDIT.txt");
        if (File.Exists(creditFile)) characterCredit = File.ReadAllText(creditFile).Trim();
        var loader = new PetLoader(new LpsDocument(configuration), new DirectoryInfo(assets));
        core = new GameCore { Controller = this, Save = new GameSave(characterName) };
        core.Save.Mode = IGameSave.ModeType.Nomal;
        core.Graph = loader.Graph(500, Dispatcher);
        pet = new PetMain(core) { NoFunctionMOD = IGameSave.ModeType.Nomal, Width = 500, Height = 500 };
        core.Save.Mode = IGameSave.ModeType.Nomal;
        pet.GraphDisplayHandler += graph =>
        {
            observedGraphs.AddOrUpdate(graph.Type + ":" + graph.Name + ":" + graph.Animat, 1, (_, count) => count + 1);
            if (pointerPaused && graph.Type != GraphType.Move)
                Dispatcher.BeginInvoke(new Action(() => { pointerPaused = false; ApplyAutonomy(); }));
        };
        pet.Event_MoveEnd += _ => pet.MoveTimerSmartMove = !demo && settings.Roaming && !settings.Quiet;
        // The core lays out its graphics in 500 design units. Scale the complete
        // control, including hit testing and authored motion, into the pet row.
        var petView = new Viewbox { Child = pet, Stretch = Stretch.Uniform, ClipToBounds = false };
        scene.Children.Insert(0, petView);
        pet.Load_0_BaseConsole(); StyleMessageBar(); pet.Load_2_TouchEvent();
        // Route native hit areas through the same takeover path as the menu.
        pet.DisplayTouchHead = TouchHead; pet.DisplayTouchBody = TouchBody;
        pet.DisplayNomal = ResumeDefault;
        await pet.Load_2_WaitGraph();
        pet.Load_3_BindingTimer(); pet.Load_4_Start();
        pet.SetMoveMode(!demo && settings.Roaming && !settings.Quiet, false, 20000);
        // Keep manual touches available; autonomous interactions run at a quiet cadence.
        if (!settings.Quiet && !demo) pet.EventTimer.Start();
        workName = core.Graph.GraphsName.TryGetValue(GraphType.Work, out var names) ? names.FirstOrDefault(n => n.Contains("study", StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault() ?? "" : "";
        ready = true; status.Text = "准备好了，陪你工作";
        SetupTray();
        if (demo) StartDemo();
        else
        {
            StartBridge(); ReadState(); poll.Start();
            pet.Say("见面啦，我是" + characterName + "。今天也陪着你。", force: true);
            if (evidence != null)
            {
                var verify = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                verify.Tick += (_, _) => { if (disposed) verify.Stop(); else SaveEvidence("live"); }; verify.Start();
            }
        }
    }

    private void StartBridge()
    {
        if (demo || disposed || DateTime.UtcNow < nextBridgeAttempt) return;
        if (bridge != null)
        {
            if (!bridge.HasExited) return;
            bridge.Dispose(); bridge = null;
        }
        double retrySeconds = Math.Min(30, 5 * Math.Pow(2, Math.Min(bridgeFailures++, 3)));
        nextBridgeAttempt = DateTime.UtcNow.AddSeconds(retrySeconds);
        string exe = Path.Combine(AppContext.BaseDirectory, "CodexStateBridge.exe");
        if (!File.Exists(exe)) { status.Text = "陪伴已就位 · 工作连接待安装"; return; }
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--bridge"); start.ArgumentList.Add(snapshotFile); start.ArgumentList.Add(Environment.ProcessId.ToString());
        try
        {
            minimumSnapshotAt = DateTime.UtcNow; observedCompletion = -1;
            bridge = Process.Start(start); bridgeStarts++;
        }
        catch (System.ComponentModel.Win32Exception) { status.Text = "工作连接恢复中 · 继续陪着你"; }
    }
    private void RecoverStaleBridge()
    {
        if (bridge != null && !bridge.HasExited)
        {
            try { bridge.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            nextBridgeAttempt = DateTime.UtcNow.AddSeconds(Math.Min(30, 5 * Math.Pow(2, Math.Min(bridgeFailures++, 3))));
        }
        StartBridge();
    }
    private void StyleMessageBar()
    {
        if (pet?.MsgBar?.This is not Control message) return;
        var surface = new SolidColorBrush(Color.FromRgb(247, 242, 252));
        var edge = new SolidColorBrush(Color.FromRgb(217, 201, 234));
        var ink = new SolidColorBrush(Color.FromRgb(94, 77, 113));
        surface.Freeze(); edge.Freeze(); ink.Freeze();
        // MessageBar owns a local Theme dictionary, so apply its palette at
        // that resource scope and directly to the authored named controls.
        message.Resources["Primary"] = surface;
        message.Resources["DARKPrimaryDark"] = edge;
        message.Resources["PrimaryText"] = ink;
        message.Resources["DARKPrimaryText"] = ink;
        message.Foreground = ink;
        message.FontFamily = new FontFamily("Microsoft YaHei UI");
        if (message.FindName("BorderMain") is Border border)
        {
            border.Background = surface; border.BorderBrush = edge;
            border.BorderThickness = new Thickness(2.5); border.CornerRadius = new CornerRadius(26);
            border.Padding = new Thickness(17, 13, 17, 14);
        }
        if (message.FindName("LName") is Label name)
        {
            name.Foreground = ink; name.FontSize = 28; name.FontWeight = FontWeights.SemiBold;
            name.Margin = new Thickness(0, 0, 0, 7);
        }
        if (message.FindName("TText") is TextBox text)
        {
            text.Foreground = ink; text.CaretBrush = ink; text.SelectionBrush = edge;
            text.FontSize = 28; text.Padding = new Thickness(0);
        }
    }
    private void ReadState()
    {
        if (!ready || pet == null) return;
        try
        {
            StartBridge();
            if (!File.Exists(snapshotFile))
            {
                if (minimumSnapshotAt != DateTime.MinValue && DateTime.UtcNow - minimumSnapshotAt > TimeSpan.FromSeconds(12)) RecoverStaleBridge();
                return;
            }
            var snapshot = JsonSerializer.Deserialize<BridgeState>(File.ReadAllText(snapshotFile));
            if (snapshot == null) return;
            if (snapshot.updatedAt.ToUniversalTime() < minimumSnapshotAt)
            {
                if (DateTime.UtcNow - minimumSnapshotAt > TimeSpan.FromSeconds(12)) RecoverStaleBridge();
                return;
            }
            if (DateTime.UtcNow - snapshot.updatedAt.ToUniversalTime() > TimeSpan.FromSeconds(12))
            { status.Text = "工作连接恢复中 · 继续陪着你"; SetWorking(false); RecoverStaleBridge(); return; }
            bridgeFailures = 0;
            current = snapshot;
            string title = snapshot.running ? snapshot.finishing ? "正在收尾" : Short(snapshot.stage, 15) : snapshot.connected ? "我在这里，随时陪你" : "等待 Codex 开始工作";
            status.Text = title + (snapshot.quotaUsed.HasValue ? " · " + snapshot.quotaUsed.Value.ToString("0") + "%" : "");
            statusPill.ToolTip = Short(snapshot.title, 80) + "\n" + Short(snapshot.message, 180);
            if (observedCompletion < 0 || snapshot.completionSerial < observedCompletion) observedCompletion = snapshot.completionSerial;
            else if (snapshot.completionSerial > observedCompletion)
            {
                observedCompletion = snapshot.completionSerial;
                notificationCount++;
                if (IsVisible) CelebrateCompletion();
                else
                {
                    pendingCompletion = true; working = false;
                    status.Text = "工作已完成 · 结果等你查看";
                    if (!settings.Quiet) tray?.ShowBalloonTip(6000, "Codex 工作完成", "本轮工作完成啦，回来看看结果吧。", Forms.ToolTipIcon.Info);
                }
            }
            SetWorking(snapshot.running && !snapshot.finishing);
        }
        catch (IOException) { }
        catch (JsonException) { }
    }
    private void SetWorking(bool active)
    {
        if (working == active || pet == null || core == null) return;
        working = active;
        if (active && workName.Length > 0)
        {
            if (pet.State != PetMain.WorkingState.Sleep)
                Wake(() => pet.Display(workName, AnimatType.A_Start, GraphType.Work, WorkLoop));
        }
        else
        {
            if (pet.State != PetMain.WorkingState.Sleep) Wake();
        }
    }
    private void WorkLoop()
    {
        if (pet == null || transitionActive || disposed) return;
        if (working) pet.Display(workName, AnimatType.B_Loop, GraphType.Work, WorkLoop);
        else pet.DisplayDefault();
    }
    private void ResumeDefault()
    {
        if (disposed || transitionActive || pet == null) return;
        if (working && workName.Length > 0) WorkLoop(); else pet.DisplayDefault();
    }
    private void ApplyAutonomy()
    {
        if (!ready || pet == null || disposed) return;
        bool enabled = IsVisible && !demo && !settings.Quiet && !transitionActive;
        if (enabled) pet.EventTimer.Start(); else pet.EventTimer.Stop();
        pet.SetMoveMode(enabled && settings.Roaming, false, 20000);
        if (!enabled) { pet.MoveTimer.Stop(); pet.MoveTimerSmartMove = false; }
    }
    private void Wake(Action? after = null)
    {
        if (pet == null || core?.Graph == null || core.Save == null || disposed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Wake(after))); return; }
        int generation = ++interactionGeneration;
        string previousName = pet.DisplayType.Name;
        transitionActive = true;
        StopAutonomousControls();
        pet.State = PetMain.WorkingState.Nomal;
        Action continueAction = () => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (disposed || generation != interactionGeneration) return;
            transitionActive = false;
            ApplyAutonomy();
            (after ?? pet.DisplayToNomal)();
        }));
        IGraph? ending = FindNamedGraph(previousName, AnimatType.C_End);
        if (ending != null) pet.Display(ending, continueAction); else continueAction();
    }
    private void StopAutonomousControls()
    {
        if (pet == null || core?.Graph == null) return;
        pet.EventTimer.Stop();
        // SetMoveMode(false) stops displacement but does not clear the native
        // Move.Displaying closure which can schedule another B_Loop afterwards.
        // Cancel those completion callbacks before the explicit C_End handoff.
        pet.SetMoveMode(true, false, 20000); pet.SetMoveMode(false, false, 20000);
        pet.MoveTimer.Stop(); pet.MoveTimerSmartMove = false; pet.CleanState();
        foreach (IGraph graph in core.Graph.GraphsALL) graph.Stop(true);
    }
    internal void ShowAwake()
    {
        Show(); Clamp();
        if (!ready) return;
        if (pendingCompletion) { pendingCompletion = false; CelebrateCompletion(); }
        else Wake();
    }
    private void TouchHead() { Wake(() => pet?.DisplayToTouchHead()); }
    private void TouchBody() { Wake(() => pet?.DisplayToTouchBody()); }
    private void Sleep() { if (ready) Wake(() => pet?.DisplaySleep(true)); }
    private void Walk()
    {
        Wake(() =>
        {
            if (pet == null) return;
            pet.MoveTimerSmartMove = true;
            if (!pet.DisplayToMove()) { pet.MoveTimerSmartMove = !demo && settings.Roaming && !settings.Quiet; pet.Say("这里有点挤，换个位置再走走吧。"); }
        });
    }
    private void Feed(bool drink)
    {
        if (pet == null || core?.Graph == null || core.Save == null) return;
        string ownName = drink ? "dragon-drink" : "dragon-eat";
        IGraph? ownAnimation = FindNamedGraph(ownName);
        if (ownAnimation != null)
        {
            Wake(() =>
            {
                status.Text = drink ? "一起喝杯茶，慢慢来" : "收到小点心，啊呜——";
                pet.Display(ownAnimation, pet.DisplayToNomal);
            });
            return;
        }
        string name = drink ? "drink" : "eat";
        if (core.Graph.FindGraph(name, AnimatType.Single, core.Save.Mode) is not FoodAnimation)
        { pet.Say("这份点心还没准备好，摸摸头也可以。", force: true); return; }
        Wake(() =>
        {
            status.Text = drink ? "一起喝口水，慢慢来" : "收到小点心，啊呜——";
            pet.Display(name, MakeFoodImage(drink), pet.DisplayToNomal);
        });
    }
    private IGraph? FindNamedGraph(string name, AnimatType animation = AnimatType.Single)
    {
        if (core?.Graph == null || core.Save == null) return null;
        // Common animations intentionally do not participate in GraphsName's
        // automatic random actions. They are still present in GraphsALL.
        return core.Graph.GraphsALL
            .Where(graph => string.Equals(graph.GraphInfo.Name, name, StringComparison.OrdinalIgnoreCase) && graph.GraphInfo.Animat == animation)
            .OrderByDescending(graph => graph.GraphInfo.ModeType == core.Save.Mode)
            .FirstOrDefault() ?? core.Graph.FindGraph(name, animation, core.Save.Mode);
    }
    private void CelebrateCompletion()
    {
        if (pet == null) return;
        working = false;
        Wake(() =>
        {
            Action notify = () => pet.SayRnd("本轮工作完成啦，回来看看结果吧。", true);
            var celebration = FindNamedGraph("celebrate");
            if (celebration != null) pet.Display(celebration, notify);
            else notify();
        });
    }
    private void DemoRaise()
    {
        if (pet == null) return;
        Wake(() => pet.Display(GraphType.Raised_Dynamic, AnimatType.Single,
            () => pet.Display(GraphType.Raised_Static, AnimatType.A_Start,
                () => pet.Display(GraphType.Raised_Static, AnimatType.B_Loop, pet.DisplayBLoopingForce))));
    }
    private static ImageSource MakeFoodImage(bool drink)
    {
        // The middle prop follows VPet's authored FoodAnimation trajectory and occlusion.
        // The character's hands, face and body come from its original front/back frames.
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            if (drink)
            {
                var outline = new Pen(new SolidColorBrush(Color.FromRgb(83, 70, 69)), 3);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(251, 245, 230)), outline, new Point(79, 50), 15, 17);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(244, 251, 255)), outline, new Rect(19, 27, 55, 52), 9, 9);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(171, 111, 63)), outline, new Point(46.5, 28), 27.5, 8);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(184, 214, 233)), null, new Rect(26, 42, 8, 25), 3, 3);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(208, 196, 194)), 3), new Point(35, 14), new Point(39, 4));
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(208, 196, 194)), 3), new Point(54, 14), new Point(58, 4));
            }
            else
            {
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(143, 89, 46)), 3);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(239, 180, 97)), edge, new Point(50, 50), 42, 37);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(252, 207, 132)), null, new Point(47, 46), 34, 29);
                foreach (Point chip in new[] { new Point(31, 33), new Point(62, 29), new Point(47, 52), new Point(69, 62), new Point(29, 65) })
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(117, 73, 48)), null, chip, 4, 4);
            }
        }
        drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); return image;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13 };
        void Add(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("摸摸头", TouchHead); Add("拍拍身体", TouchBody); Add("休息一会儿", Sleep); Add("叫醒伙伴", () => Wake()); Add("出去走走", Walk);
        Add("喂一块点心", () => Feed(false)); Add("一起喝杯茶", () => Feed(true));
        menu.Items.Add(new Separator()); Add("工作状态与额度", ShowStatus);
        var sizes = new MenuItem { Header = "调整大小" };
        foreach (double size in new[] { 150d, 180, 200, 240, 280, 340 })
        { double value = size; var item = new MenuItem { Header = size + " 像素", IsChecked = Math.Abs(settings.Size - size) < .1 }; item.Click += (_, _) => { settings.Size = value; Width = value; Height = value + 26; Clamp(); SaveSettings(); }; sizes.Items.Add(item); }
        menu.Items.Add(sizes);
        Add(settings.Roaming ? "暂停自主走动" : "允许自主走动", () => { settings.Roaming = !settings.Roaming; pet?.SetMoveMode(settings.Roaming && !settings.Quiet, false, 20000); SaveSettings(); });
        Add(settings.Quiet ? "恢复日常陪伴" : "安静陪伴", () =>
        {
            settings.Quiet = !settings.Quiet;
            if (pet != null) { pet.SetMoveMode(settings.Roaming && !settings.Quiet, false, 20000); if (settings.Quiet) pet.EventTimer.Stop(); else pet.EventTimer.Start(); }
            SaveSettings();
        });
        Add("回到屏幕右下角", () => { var area = WorkArea(); Left = area.Right - Width - 24; Top = area.Bottom - Height - 12; Clamp(); SaveSettings(); });
        Add("角色来源与关于", () => MessageBox.Show("角色：" + characterName + "\n" + characterCredit + "\n\n动画内核：VPet-Simulator.Core\nhttps://github.com/LorisYounger/VPet\n\n本程序接入本机 Codex 的公开工作状态、完成提醒与额度。角色图形、动作素材与动画内核分别依照各自资源声明使用。", "关于桌面伙伴"));
        menu.Items.Add(new Separator()); Add("收起到托盘", Hide); Add("退出", Close); return menu;
    }
    private void ShowStatus()
    {
        string info = current.connected ? (current.running ? "正在工作：" : "当前状态：") + Short(current.stage, 120) : "等待本机 Codex 开始工作。";
        if (current.quotaUsed.HasValue) info += "\n\n额度已用：" + current.quotaUsed.Value.ToString("0.#") + "%" + (current.quotaMinutes.HasValue ? "（" + current.quotaMinutes + " 分钟窗口）" : "");
        if (current.message.Length > 0) info += "\n\n" + Short(current.message, 400);
        MessageBox.Show(this, info, "Codex 工作状态");
    }
    private void SetupTray()
    {
        if (demo) return;
        string icon = Path.Combine(AppContext.BaseDirectory, "assets", "tray.ico");
        tray = new Forms.NotifyIcon { Text = "Codex 桌面伙伴", Visible = true, Icon = File.Exists(icon) ? new System.Drawing.Icon(icon) : System.Drawing.SystemIcons.Application };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示并叫醒伙伴", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowAwake)));
        menu.Items.Add("工作状态与额度", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowStatus)));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(new Action(Close)));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowAwake));
    }

    private void StartDemo()
    {
        if (pet == null || evidence == null) throw new InvalidOperationException("--demo requires --verify <folder>");
        Directory.CreateDirectory(Path.Combine(evidence, "frames"));
        status.Text = "实际 VPet 动画 · 独立验收";
        demoClock.Start();
        var steps = new (double at, string label, Action action)[] {
            (1.5, "待机", () => SaveEvidence("idle")),
            (2, "摸头", TouchHead), (4.2, "摸头截图", () => SaveEvidence("headpat")),
            (5.5, "戳戳脸", TouchBody), (7.5, "互动截图", () => SaveEvidence("touch")),
            (8.5, "提起", DemoRaise), (10.5, "提起截图", () => SaveEvidence("raised")),
            (11, "落地", () => Wake()), (11.3, "落地截图", () => SaveEvidence("landing")),
            (13, "休息", Sleep), (15.5, "睡眠截图", () => SaveEvidence("sleep")),
            (16, "唤醒", () => Wake()), (16.3, "醒来截图", () => SaveEvidence("waking")),
            (18, "清醒截图", () => SaveEvidence("awake")), (19, "走动", Walk),
            (21.5, "走动截图", () => SaveEvidence("walk")),
            (23, "点心", () => Feed(false)), (24.5, "投喂截图", () => SaveEvidence("feeding")),
            (27, "喝茶", () => Feed(true)), (28.5, "喝茶截图", () => SaveEvidence("drinking")),
            (31, "工作", () => SetWorking(true)), (34, "工作截图", () => SaveEvidence("working")),
            (35, "工作完成", CelebrateCompletion), (35.5, "挥手截图", () => SaveEvidence("celebrating")),
            (38, "完成截图", () => SaveEvidence("completed"))
        };
        demoTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        demoTimer.Tick += (_, _) =>
        {
            double time = demoClock.Elapsed.TotalSeconds;
            while (demoStep < steps.Length && time >= steps[demoStep].at)
            {
                var step = steps[demoStep++]; step.action(); demoActions.Add(new { step.label, at = time });
            }
            if (time <= 40) SaveImage(Path.Combine(evidence, "frames", (frame++).ToString("D5") + ".png"));
            if (time >= 40)
            {
                demoTimer.Stop(); SaveEvidence("final");
                var checks = new Dictionary<string, bool> {
                    ["ready"] = ready, ["headpat"] = observedGraphs.Keys.Any(n => n.StartsWith("Touch_Head:")),
                    ["bodyTouch"] = observedGraphs.Keys.Any(n => n.StartsWith("Touch_Body:")),
                    ["sleep"] = observedGraphs.Keys.Any(n => n.StartsWith("Sleep:")),
                    ["waking"] = observedGraphs.Keys.Any(n => n.StartsWith("Sleep:") && n.EndsWith(":" + AnimatType.C_End)),
                    ["raised"] = observedGraphs.Keys.Any(n => n.StartsWith("Raised_Dynamic:")),
                    ["landing"] = observedGraphs.Keys.Any(n => n.StartsWith("Raised_Static:") && n.EndsWith(":" + AnimatType.C_End)),
                    ["walking"] = observedGraphs.Keys.Any(n => n.StartsWith("Move:")),
                    ["work"] = observedGraphs.Keys.Any(n => n.StartsWith("Work:")),
                    ["feeding"] = observedGraphs.Keys.Any(n => n.Contains(":eat:") || n.Contains(":dragon-eat:")),
                    ["drinking"] = observedGraphs.Keys.Any(n => n.Contains(":drink:") || n.Contains(":dragon-drink:")),
                    ["celebrating"] = observedGraphs.Keys.Any(n => n.Contains(":celebrate:")),
                    ["loadedWithoutErrors"] = pet.ErrorMessage.Count == 0, ["frames"] = frame > 100
                };
                File.WriteAllText(Path.Combine(evidence, "demo-result.json"), JsonSerializer.Serialize(new { checks, frames = frame, duration = time, actions = demoActions, graphs = observedGraphs }, JsonOptions));
                if (checks.Values.Any(v => !v)) Environment.ExitCode = 1;
                Close();
            }
        };
        demoTimer.Start();
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private void SaveEvidence(string name)
    {
        if (evidence == null) return;
        SaveImage(Path.Combine(evidence, name + ".png"));
        File.WriteAllText(Path.Combine(evidence, name + ".json"), JsonSerializer.Serialize(new {
            version = "0.7.0", characterName, visible = IsVisible, ready, left = Left, top = Top, width = Width, height = Height,
            state = pet?.State.ToString(), graph = pet?.DisplayType.Name, type = pet?.DisplayType.Type.ToString(),
            graphCount = core?.Graph?.GraphsALL.Count, errors = pet?.ErrorMessage, bridgeRunning = bridge != null && !bridge.HasExited,
            bridgePid = bridge != null && !bridge.HasExited ? bridge.Id : 0, bridgeStarts, observedCompletion, notificationCount, pendingCompletion, interactionGeneration, transitionActive,
            loadedGraphs = core?.Graph?.GraphsALL.Select(graph => graph.GraphInfo.Type + ":" + graph.GraphInfo.Name + ":" + graph.GraphInfo.Animat).Distinct().ToArray(),
            observedGraphs, current
        }, JsonOptions));
    }
    private void SaveImage(string file)
    {
        scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(ActualWidth * 2)), Math.Max(1, (int)Math.Ceiling(ActualHeight * 2)), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(scene); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
    private void SaveSettings()
    {
        if (demo) return;
        settings.Left = Left; settings.Top = Top;
        try
        {
            string temporary = settingsFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions)); File.Move(temporary, settingsFile, true); positionDirty = false;
        }
        catch (IOException) { }
    }
    private Rect WorkArea()
    {
        var source = PresentationSource.FromVisual(this);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Point center = toDevice.Transform(new Point(Left + Width / 2, Top + Height / 2));
        var rectangle = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y)).WorkingArea;
        return new Rect(fromDevice.Transform(new Point(rectangle.Left, rectangle.Top)), fromDevice.Transform(new Point(rectangle.Right, rectangle.Bottom)));
    }
    private void Clamp()
    {
        var area = WorkArea(); Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)); Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }
    private void Cleanup()
    {
        if (disposed) return; disposed = true;
        poll.Stop(); demoTimer?.Stop();
        if (pet != null) { pet.SetMoveMode(false, false, 20000); pet.EventTimer.Stop(); pet.MoveTimer.Stop(); }
        core?.Graph?.Dispose();
        if (bridge != null) { try { if (!bridge.HasExited) bridge.Kill(); } catch (InvalidOperationException) { } bridge.Dispose(); }
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
    }
    private static string Short(string value, int length) => value.Length <= length ? value : value.Substring(0, length) + "…";

    public double ZoomRatio => settings.Size / 500;
    public int PressLength => 260;
    public bool EnableFunction => false;
    public int InteractionCycle => 3;
    public bool RePositionActive { get; set; } = false;
    public void MoveWindows(double x, double y)
    {
        if (disposed) return;
        Dispatcher.Invoke(() => { Left += x * ZoomRatio; Top += y * ZoomRatio; Clamp(); });
    }
    public double GetWindowsDistanceLeft() => Dispatcher.Invoke(() => Left - WorkArea().Left);
    public double GetWindowsDistanceRight() => Dispatcher.Invoke(() => WorkArea().Right - Left - Width);
    public double GetWindowsDistanceUp() => Dispatcher.Invoke(() => Top - WorkArea().Top);
    public double GetWindowsDistanceDown() => Dispatcher.Invoke(() => WorkArea().Bottom - Top - Height);
    public void ShowPanel() => Dispatcher.BeginInvoke(new Action(ShowStatus));
    public void ResetPosition() => Dispatcher.Invoke(Clamp);
    public bool CheckPosition() => true;
}
