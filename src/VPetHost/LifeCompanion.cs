using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PetMain = VPet_Simulator.Core.Main;
using static VPet_Simulator.Core.GraphInfo;

namespace CodexVPet;

internal sealed class LifeMessage
{
    public string id { get; set; } = "";
    public string role { get; set; } = "";
    public string text { get; set; } = "";
    public string kind { get; set; } = "";
    public string emotion { get; set; } = "calm";
    public string[] memory_ids { get; set; } = Array.Empty<string>();
}
internal sealed class LifeJob
{
    public string id { get; set; } = "";
    public string status { get; set; } = "";
    public string message { get; set; } = "";
}
internal sealed class LifeReviewNotification
{
    public string id { get; set; } = "";
    public long event_seq { get; set; }
    public string proposal_id { get; set; } = "";
    public int version { get; set; }
    public string digest { get; set; } = "";
    public string kind { get; set; } = "";
    public string status { get; set; } = "";
    public string text { get; set; } = "";
    public string at { get; set; } = "";
    public bool seen { get; set; }
    public bool actionable { get; set; }
    public string href { get; set; } = "";
}
internal sealed class LifeReviewInbox
{
    public List<LifeReviewNotification> notifications { get; set; } = new();
    public int unread_count { get; set; }
    public int pending_count { get; set; }
}
internal sealed class LifeSnapshot
{
    public int protocol { get; set; }
    public List<LifeMessage> messages { get; set; } = new();
    public List<LifeJob> jobs { get; set; } = new();
    public List<LifeJob> recent_jobs { get; set; } = new();
    public JsonElement pet_state { get; set; }
    public JsonElement interaction { get; set; }
    public LifeReviewInbox? review_inbox { get; set; }
}
internal sealed class LifeLinkSettings
{
    public bool Enabled { get; set; } = true;
    public bool Notices { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public string LastMessage { get; set; } = "";
    public string LastInteraction { get; set; } = "";
    public long LastReviewSequence { get; set; }
}

internal sealed partial class CompanionWindow
{
    private const string LifeHome = "http://127.0.0.1:8766/";
    private readonly HttpClient lifeHttp = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
    private readonly CancellationTokenSource lifeCancel = new();
    private readonly DispatcherTimer lifeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private LifeLinkSettings lifeSettings = new();
    private LifeSnapshot? lifeSnapshot;
    private bool lifePolling, lifePosting, lifeOnline;
    private DateTime lifeNextStart = DateTime.MinValue;
    private string lifePending = "", lifeError = "", lifeRendered = "";
    private string lifeLastEmotion = "calm";
    private int lifeReplyGestures;
    private int lifeReviewBubbles;
    private Window? lifeChat;
    private TextBox? lifeInput;
    private TextBlock? lifeStatus, lifeGrowth;
    private StackPanel? lifeHistory;
    private ScrollViewer? lifeScroll;
    private Button? lifeSend;
    private Button? lifeReviewBadge;
    private StackPanel? lifeReviews;
    private ScrollViewer? lifeReviewScroll;
    private string lifeReviewsRendered = "";
    private string LifeSettingsFile => Path.Combine(profile, "life-link-settings.json");

    private void InitializeLife()
    {
        InitializeLifeReviewBadge();
        if (demo) return;
        try { if (File.Exists(LifeSettingsFile)) lifeSettings = JsonSerializer.Deserialize<LifeLinkSettings>(File.ReadAllText(LifeSettingsFile)) ?? new(); } catch (JsonException) { }
        lifeTimer.Tick += async (_, _) => await PollLife();
        if (lifeSettings.Enabled) { lifeTimer.Start(); _ = PollLife(); }
    }
    private void SaveLifeSettings()
    {
        File.WriteAllText(LifeSettingsFile + ".tmp", JsonSerializer.Serialize(lifeSettings));
        File.Move(LifeSettingsFile + ".tmp", LifeSettingsFile, true);
    }
    private void StartLifeService()
    {
        if (DateTime.UtcNow < lifeNextStart || disposed) return;
        lifeNextStart = DateTime.UtcNow.AddSeconds(30);
        string file = Path.Combine(AppContext.BaseDirectory, "companion-link.json");
        if (!File.Exists(file)) return;
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(file));
            string directory = Path.GetFullPath(config.RootElement.GetProperty("projectDirectory").GetString()!);
            string python = Path.GetFullPath(config.RootElement.GetProperty("pythonExecutable").GetString()!);
            string script = Path.Combine(directory, "app.py");
            if (!File.Exists(script) || !File.Exists(python) || !Path.GetFileName(python).Equals("python.exe", StringComparison.OrdinalIgnoreCase)) return;
            var start = new ProcessStartInfo(python) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in new[] { "-X", "utf8", script, "--port", "8766" }) start.ArgumentList.Add(argument);
            using var service = Process.Start(start);
            if (service != null) { Directory.CreateDirectory(Path.Combine(directory, "tmp")); File.WriteAllText(Path.Combine(directory, "tmp", "server.pid"), service.Id.ToString()); }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        { lifeError = "小G启动失败，请从日常页重试。"; }
    }
    private async Task<JsonDocument> LifeRequest(string path, object? body = null)
    {
        if (path is not ("api/pet" or "api/pet/interaction" or "api/life/chat" or "api/life/browse" or "api/review/notifications/seen")) throw new ArgumentException("Unsupported companion request");
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, LifeHome + path);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await lifeHttp.SendAsync(request, lifeCancel.Token);
        string text = await response.Content.ReadAsStringAsync(lifeCancel.Token);
        if (!response.IsSuccessStatusCode)
        {
            try { using var error = JsonDocument.Parse(text); throw new InvalidOperationException(error.RootElement.GetProperty("error").GetString()); }
            catch (JsonException) { throw new InvalidOperationException("小G暂时无法处理这次请求。"); }
        }
        return JsonDocument.Parse(text);
    }
    private async Task PollLife()
    {
        if (lifePolling || disposed || !lifeSettings.Enabled) return;
        lifePolling = true;
        try
        {
            using var result = await LifeRequest("api/pet");
            if (disposed || !lifeSettings.Enabled) return;
            var next = result.RootElement.Deserialize<LifeSnapshot>();
            if (next?.protocol != 1) throw new InvalidOperationException("小G连接版本不兼容。");
            lifeSnapshot = next; lifeOnline = true;
            ObserveLifeReviews(next.review_inbox);
            ObserveLifeInteraction(next.interaction);
            if (lifePending.Length > 0)
            {
                var job = next.recent_jobs.FirstOrDefault(j => j.id == lifePending);
                if (job?.status != "running") { lifePending = ""; lifeError = job?.status == "done" ? "" : job?.message ?? "任务状态已丢失，请检查日常页。"; }
            }
            LifeMessage? latest = next.messages.LastOrDefault();
            if (latest != null && latest.id != lifeSettings.LastMessage)
            {
                bool first = lifeSettings.LastMessage.Length == 0;
                lifeSettings.LastMessage = latest.id; SaveLifeSettings();
                if (!first && latest.role == "assistant") ShowLifeReply(latest);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException)
        { lifeOnline = false; if (!disposed) StartLifeService(); }
        finally { lifePolling = false; if (!disposed) RenderLifeChat(); }
    }
    private void ShowLifeReply(LifeMessage message)
    {
        bool direct = lifeChat?.IsVisible == true && message.kind is "chat" or "tool";
        if (!direct && (!lifeSettings.Notices || settings.Quiet && message.kind != "reminder")) return;
        if (!IsVisible || pet == null) return;
        string type = pet.DisplayType.Type.ToString();
        // Force-saying can make the core select its speaking/default animation.
        // Keep sleeping, lifting and manual actions intact; the chat retains text.
        if (transitionActive || pointerPaused || pet.State == PetMain.WorkingState.Sleep || type.Contains("Sleep") || type.Contains("Raised") || type.Contains("Touch") || type.Contains("Common")) return;
        // Text-only bubble: force=true starts the core's own animation task and
        // races the explicitly selected emotion sequence below.
        pet.Say(Short(message.text, 100), force: false);
        // Reply gestures are expression only: never report them as a user touch.
        if (lifeSettings.ReduceMotion || settings.Quiet || transitionActive || pointerPaused || pet.State != PetMain.WorkingState.Nomal || working && !direct) return;
        string? name = ReplyAnimation(message.emotion);
        if (name == null) return;
        var graph = FindNamedGraph(name);
        // The core may return A_Start as a compatibility fallback for a missing
        // Single. Only a real Single can skip the start/loop/end sequence.
        if (graph?.GraphInfo.Animat != AnimatType.Single) graph = null;
        var start = FindNamedGraph(name, AnimatType.A_Start);
        if (graph == null && start == null) return;
        lifeLastEmotion = message.emotion; lifeReplyGestures++;
        Wake(() =>
        {
            int generation = interactionGeneration;
            // Old core callbacks can ask for the default animation while a new
            // expression is running. Own this bounded segment until it ends.
            transitionActive = true; ApplyAutonomy();
            bool finished = false;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            void End() => Dispatcher.BeginInvoke(new Action(() =>
            {
                timeout.Stop();
                if (finished || disposed || generation != interactionGeneration) return;
                finished = true; transitionActive = false; ApplyAutonomy(); ResumeDefault();
            }));
            timeout.Tick += (_, _) => End(); timeout.Start();
            if (graph != null) { pet.Display(graph, End); return; }
            void Close() { if (generation != interactionGeneration) return; var ending = FindNamedGraph(name, AnimatType.C_End); if (ending != null) pet.Display(ending, End); else End(); }
            pet.Display(start!, () => { if (generation != interactionGeneration) return; var loop = FindNamedGraph(name, AnimatType.B_Loop); if (loop != null) pet.Display(loop, Close); else Close(); });
        });
    }
    internal static string? ReplyAnimation(string emotion) => emotion switch { "happy" => "celebrate", "curious" or "thoughtful" => "curious", "caring" => "pet", _ => null };
    private void InitializeLifeReviewBadge()
    {
        lifeReviewBadge = new Button { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 5, 4, 0), Padding = new Thickness(8, 5, 8, 5), FontSize = 10, Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(255, 245, 221)), Foreground = new SolidColorBrush(Color.FromRgb(101, 74, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 199, 154)), FontFamily = new FontFamily("Microsoft YaHei UI") };
        lifeReviewBadge.Click += (_, _) => OpenLifeChat();
        Panel.SetZIndex(lifeReviewBadge, 10); scene.Children.Add(lifeReviewBadge);
    }
    private void RenderLifeReviewBadge()
    {
        if (lifeReviewBadge == null) return;
        var inbox = lifeSnapshot?.review_inbox;
        int unread = Math.Max(0, inbox?.unread_count ?? 0), pending = Math.Max(0, inbox?.pending_count ?? 0);
        string label = $"待审 {pending} · 未读 {unread}";
        lifeReviewBadge.Content = label;
        lifeReviewBadge.Visibility = unread > 0 || pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        lifeReviewBadge.ToolTip = label + "\n点击查看提案通知；查看不代表批准。" + (!lifeSettings.Enabled ? "\n连接已暂停，显示上次通知状态。" : "");
        System.Windows.Automation.AutomationProperties.SetName(lifeReviewBadge, "提案通知，" + label);
    }
    private void ObserveLifeReviews(LifeReviewInbox? inbox)
    {
        var rows = inbox?.notifications ?? new();
        var stable = rows.Where(row => row != null && row.event_seq > 0 && row.id == "review:" + row.event_seq).ToList();
        var fresh = stable.Where(row => row.event_seq > lifeSettings.LastReviewSequence && !row.seen).GroupBy(row => row.id).Select(group => group.First()).ToList();
        long newest = stable.Select(row => row.event_seq).DefaultIfEmpty(lifeSettings.LastReviewSequence).Max();
        if (newest > lifeSettings.LastReviewSequence) { lifeSettings.LastReviewSequence = newest; SaveLifeSettings(); }
        RenderLifeReviewBadge();
        if (fresh.Count == 0 || !lifeSettings.Enabled || !lifeSettings.Notices || settings.Quiet || !IsVisible || pet == null) return;
        string type = pet.DisplayType.Type.ToString();
        if (transitionActive || pointerPaused || pet.State == PetMain.WorkingState.Sleep || type.Contains("Sleep") || type.Contains("Raised") || type.Contains("Touch") || type.Contains("Common")) return;
        int pending = fresh.Count(row => row.kind == "pending" && row.status == "pending");
        string text = pending == fresh.Count ? $"有 {pending} 项提案等你审查，点角标查看。" : pending == 0 ? $"收到 {fresh.Count} 条提案结果，点角标查看。" : $"有 {pending} 项提案待审、{fresh.Count - pending} 条结果，点角标查看。";
        pet.Say(text, force: false); lifeReviewBubbles++;
    }
    internal static string? LifeReviewUri(LifeReviewNotification row)
    {
        static bool Hex(string? value, int length) => value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        if (row.event_seq <= 0 || row.id != "review:" + row.event_seq || row.version <= 0 || !Hex(row.proposal_id, 32) || !Hex(row.digest, 64) || row.kind is not ("pending" or "result")) return null;
        string path = $"/review?proposal={row.proposal_id}&version={row.version}&digest={row.digest}#proposals";
        // Never pass a service-provided URI to the shell, even for loopback.
        if (row.href != path) return null;
        return LifeHome.TrimEnd('/') + path;
    }
    private async Task OpenLifeReview(LifeReviewNotification row)
    {
        string? uri = LifeReviewUri(row);
        if (uri == null) { lifeError = "这条通知的审查链接无效，请从日常页查看。"; RenderLifeChat(); return; }
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { lifeError = "审查页未能打开，请从日常页查看。"; RenderLifeChat(); return; }
        await MarkLifeReviewSeen(row);
    }
    private async Task MarkLifeReviewSeen(LifeReviewNotification row)
    {
        if (row.seen || LifeReviewUri(row) == null) return;
        try
        {
            using var result = await LifeRequest("api/review/notifications/seen", new { ids = new[] { row.id } });
            if (result.RootElement.TryGetProperty("seen", out var seen) && seen.ValueKind == JsonValueKind.Array && seen.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == row.id))
            {
                var inbox = lifeSnapshot?.review_inbox;
                var current = inbox?.notifications?.FirstOrDefault(value => value.id == row.id);
                if (current != null && !current.seen) { current.seen = true; inbox!.unread_count = Math.Max(0, inbox.unread_count - 1); }
                row.seen = true;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        { lifeError = "审查页已打开，但已读回执未保存，连接恢复后可再次查看。"; }
        if (!disposed) RenderLifeChat();
    }
    private void RenderLifeReviews()
    {
        if (lifeReviews == null || lifeReviewScroll == null) return;
        var inbox = lifeSnapshot?.review_inbox;
        string key = JsonSerializer.Serialize(inbox);
        if (key == lifeReviewsRendered) return; lifeReviewsRendered = key; lifeReviews.Children.Clear();
        var rows = inbox?.notifications ?? new();
        lifeReviewScroll.Visibility = rows.Count > 0 || (inbox?.pending_count ?? 0) > 0 || (inbox?.unread_count ?? 0) > 0 ? Visibility.Visible : Visibility.Collapsed;
        lifeReviews.Children.Add(new TextBlock { Text = $"提案通知 · 待审 {Math.Max(0, inbox?.pending_count ?? 0)} · 未读 {Math.Max(0, inbox?.unread_count ?? 0)}", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
        foreach (var row in rows)
        {
            var card = new StackPanel { Margin = new Thickness(9) };
            string state = row.kind == "pending" && row.status == "pending" ? "待审" : "结果";
            string stamp = DateTimeOffset.TryParse(row.at, out var at) ? " · " + at.LocalDateTime.ToString("MM-dd HH:mm") : "";
            card.Children.Add(new TextBlock { Text = (row.seen ? "已读" : "未读") + " · " + state + " · v" + row.version + stamp, FontSize = 10, Foreground = Brushes.DimGray });
            card.Children.Add(new TextBlock { Text = row.text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 5, 0, 7) });
            var button = new Button { Content = (row.kind == "pending" ? "查看提案 v" : "查看结果 v") + row.version, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(9, 4, 9, 4), IsEnabled = LifeReviewUri(row) != null };
            button.Click += async (_, _) => await OpenLifeReview(row); card.Children.Add(button);
            lifeReviews.Children.Add(new Border { Child = card, Background = new SolidColorBrush(Color.FromRgb(250, 247, 240)), BorderBrush = new SolidColorBrush(Color.FromRgb(232, 222, 202)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 3, 7) });
        }
    }
    private void ObserveLifeInteraction(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) { if (lifeSettings.LastInteraction.Length == 0) { lifeSettings.LastInteraction = "-"; SaveLifeSettings(); } return; }
        string id = row.GetProperty("id").GetString() ?? "";
        if (id == lifeSettings.LastInteraction) return;
        bool first = lifeSettings.LastInteraction.Length == 0;
        lifeSettings.LastInteraction = id; SaveLifeSettings();
        if (first || !IsVisible || !row.TryGetProperty("origin", out var origin) || origin.GetString() != "web" ||
            !row.TryGetProperty("at", out var at) || !DateTimeOffset.TryParse(at.GetString(), out var stamp) || DateTimeOffset.UtcNow - stamp > TimeSpan.FromSeconds(20)) return;
        switch (row.GetProperty("action").GetString())
        {
            case "pet": TouchHead(); break;
            case "tea": Feed(true); break;
            case "feed": Feed(false); break;
            case "rest": Sleep(); break;
            case "wake": Wake(); break;
        }
    }
    private async void RecordLifeInteraction(string kind)
    {
        if (demo || disposed || !lifeOnline || !lifeSettings.Enabled) return;
        try { using var result = await LifeRequest("api/pet/interaction", new { kind }); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { lifeError = "这次动作已播放，但日常记录没有保存，请检查本机连接。"; }
        if (!disposed) await PollLife();
    }
    private void OpenLifeHome() => Process.Start(new ProcessStartInfo(LifeHome) { UseShellExecute = true });
    internal void OpenLifeChat()
    {
        if (!lifeSettings.Enabled) { lifeSettings.Enabled = true; SaveLifeSettings(); lifeTimer.Start(); }
        if (lifeChat != null) { lifeChat.Show(); lifeChat.Activate(); lifeInput?.Focus(); return; }
        lifeChat = new Window { Title = "小G · 和白龙聊聊", Width = 440, Height = 640, MinWidth = 380, MinHeight = 480,
            Background = new SolidColorBrush(Color.FromRgb(251,249,253)), Foreground = new SolidColorBrush(Color.FromRgb(52,43,62)), FontFamily = new FontFamily("Microsoft YaHei UI"), WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Resources = ChromeResources(), UseLayoutRounding = true };
        var root = new DockPanel { Margin = new Thickness(20,16,20,16), Background = lifeChat.Background };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "和小G聊聊", FontSize = 19, FontWeight = FontWeights.SemiBold });
        lifeStatus = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), Foreground = Brushes.DimGray }; header.Children.Add(lifeStatus);
        lifeGrowth = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 12), Foreground = new SolidColorBrush(Color.FromRgb(94, 77, 113)), TextWrapping = TextWrapping.Wrap }; header.Children.Add(lifeGrowth);
        var quick = new WrapPanel(); header.Children.Add(quick);
        void Quick(string label, Action action) { var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 5, 10, 5) }; button.Click += (_, _) => action(); quick.Children.Add(button); }
        Quick("摸摸头", TouchHead); Quick("一起喝茶", () => Feed(true)); Quick("喂点心", () => Feed(false));
        Quick("休息", Sleep); Quick("叫醒", () => { Wake(); RecordLifeInteraction("wake"); });
        Quick("记忆与日常 ↗", OpenLifeHome);
        lifeReviews = new StackPanel(); lifeReviewScroll = new ScrollViewer { Content = lifeReviews, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        header.Children.Add(lifeReviewScroll);
        var composer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(composer, Dock.Bottom); root.Children.Add(composer);
        lifeInput = new TextBox { AcceptsReturn = true, Height = 76, Padding = new Thickness(11), MaxLength = 4000, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(lifeInput, "给小G的消息");
        lifeInput.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; await SendLife(); } }; composer.Children.Add(lifeInput);
        var bottom = new DockPanel { Margin = new Thickness(0, 9, 0, 0) }; composer.Children.Add(bottom);
        lifeSend = new Button { Content = "发送", Tag = "primary", Padding = new Thickness(22, 7, 22, 7), Background = new SolidColorBrush(Color.FromRgb(117,84,150)), Foreground = Brushes.White, BorderThickness = new Thickness(0) }; lifeSend.Click += async (_, _) => await SendLife(); DockPanel.SetDock(lifeSend, Dock.Right); bottom.Children.Add(lifeSend);
        bottom.Children.Add(new TextBlock { Text = "Ctrl+Enter 发送 · 与日常页共用记忆", Foreground = Brushes.DimGray, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        lifeHistory = new StackPanel(); lifeScroll = new ScrollViewer { Content = lifeHistory, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(lifeScroll);
        lifeChat.Content = root; lifeChat.Closed += (_, _) => { lifeChat = null; lifeRendered = ""; lifeReviewsRendered = ""; };
        RenderLifeChat(); lifeChat.Show(); lifeInput.Focus(); lifeNextStart = DateTime.MinValue; _ = PollLife();
    }
    private async Task SendLife()
    {
        string message = lifeInput?.Text.Trim() ?? "";
        if (!lifeSettings.Enabled || !lifeOnline || lifePosting || lifePending.Length > 0 || lifeSnapshot?.jobs.Count > 0 || message.Length == 0) return;
        lifePosting = true; lifeError = ""; RenderLifeChat();
        try
        {
            using var result = await LifeRequest("api/life/chat", new { message });
            lifePending = result.RootElement.GetProperty("job_id").GetString() ?? "";
            lifeInput?.Clear();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { lifeError = ex.Message; }
        finally { lifePosting = false; if (!disposed) { RenderLifeChat(); await PollLife(); } }
    }
    private void RenderLifeChat()
    {
        RenderLifeReviewBadge(); RenderLifeReviews();
        if (lifeChat == null || lifeStatus == null || lifeHistory == null || lifeSend == null) return;
        bool busy = lifePosting || lifePending.Length > 0 || lifeSnapshot?.jobs.Count > 0;
        lifeStatus.Text = !lifeSettings.Enabled ? "小G连接已暂停。从桌宠右键菜单恢复。" : lifeError.Length > 0 ? lifeError : !lifeOnline ? "正在连接本机小G…" : busy ? "小G正在回应你…" : "已连接 · 记忆与对话和日常页相同";
        lifeSend.IsEnabled = lifeSettings.Enabled && lifeOnline && !busy;
        if (lifeSnapshot?.pet_state.ValueKind == JsonValueKind.Object && lifeGrowth != null)
        {
            var game = lifeSnapshot.pet_state;
            lifeGrowth.Text = $"{game.GetProperty("stage").GetString()} · 精力 {game.GetProperty("energy")} · 饱腹 {game.GetProperty("satiety")} · 熟悉度 {game.GetProperty("familiarity")}";
        }
        string key = JsonSerializer.Serialize(lifeSnapshot?.messages);
        if (key == lifeRendered) return; lifeRendered = key; lifeHistory.Children.Clear();
        foreach (var message in lifeSnapshot?.messages ?? new())
        {
            var row = new StackPanel { Margin = new Thickness(message.role == "user" ? 30 : 0, 8, message.role == "user" ? 0 : 20, 8) };
            row.Children.Add(new TextBlock { Text = message.role == "user" ? "你" : "小G" + (message.memory_ids.Length > 0 ? " · 已记下偏好，可在记忆页修改" : ""), FontSize = 10, Foreground = Brushes.DimGray });
            var text = new TextBox { Text = message.text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(0), FontSize = 13, Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(52,43,62)) };
            row.Children.Add(new Border { Child = text, Background = new SolidColorBrush(message.role == "user" ? Color.FromRgb(235,226,245) : Colors.White), BorderBrush = new SolidColorBrush(Color.FromRgb(232,224,240)), BorderThickness = new Thickness(message.role == "user" ? 0 : 1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12,10,12,10), Margin = new Thickness(0,5,0,0) });
            lifeHistory.Children.Add(row);
        }
        lifeScroll?.ScrollToEnd();
    }
    private void AddLifeMenu(MenuItem menu)
    {
        void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("记忆、养成与日常", OpenLifeHome);
        Add(lifeSettings.Enabled ? "暂停小G连接" : "连接小G", () => { lifeSettings.Enabled = !lifeSettings.Enabled; SaveLifeSettings(); if (lifeSettings.Enabled) { lifeTimer.Start(); _ = PollLife(); } else { lifeTimer.Stop(); lifeOnline = false; } RenderLifeChat(); });
        Add(lifeSettings.Notices ? "关闭主动来信气泡" : "开启主动来信气泡", () => { lifeSettings.Notices = !lifeSettings.Notices; SaveLifeSettings(); });
        Add(lifeSettings.ReduceMotion ? "恢复回复动作" : "减少回复动作", () => { lifeSettings.ReduceMotion = !lifeSettings.ReduceMotion; SaveLifeSettings(); });
        menu.Items.Add(new Separator());
    }
    private void CleanupLife() { lifeTimer.Stop(); lifeCancel.Cancel(); lifeChat?.Close(); lifeHttp.Dispose(); lifeCancel.Dispose(); }
}
