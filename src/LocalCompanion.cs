using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CodexDragon
{
    internal static class LocalCompanionClient
    {
        internal const string Home = "http://127.0.0.1:8766/";
        internal static Func<string, object, IDictionary<string, object>> VerificationTransport = null;
        internal static IDictionary<string, object> Request(string path, object body = null)
        {
            // A fixed loopback destination; neither model text nor configuration supplies URLs.
            if (path != "api/pet" && path != "api/life/chat" && path != "api/life/browse" && path != "api/pet/interaction")
                throw new ArgumentException("Unsupported companion request");
            if (VerificationTransport != null) return VerificationTransport(path, body);
            var request = (HttpWebRequest)WebRequest.Create(Home + path);
            request.Proxy = null; request.AllowAutoRedirect = false;
            request.Timeout = 2500; request.ReadWriteTimeout = 2500;
            if (body != null)
            {
                request.Method = "POST"; request.ContentType = "application/json; charset=utf-8";
                byte[] bytes = Encoding.UTF8.GetBytes(Json.Write(body)); request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            try
            {
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return Json.Parse(reader.ReadToEnd());
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    using (var response = ex.Response)
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        var error = Json.Parse(reader.ReadToEnd());
                        throw new InvalidOperationException(Json.Text(error, "error"));
                    }
                }
                throw;
            }
        }
        internal static List<IDictionary<string, object>> Rows(IDictionary<string, object> map, string name)
        {
            var values = Json.Get(map, name) as IEnumerable;
            return values == null ? new List<IDictionary<string, object>>() : values.Cast<object>().Select(Json.Map).ToList();
        }
    }

    public sealed partial class PetWindow
    {
        private DispatcherTimer lifeTimer;
        private Window lifeChat;
        private StackPanel lifeMessages;
        private ScrollViewer lifeScroll;
        private TextBox lifeInput;
        private TextBlock lifeStatus;
        private Button lifeSend;
        private bool lifePolling, lifeOnline, lifeBusy, lifePosting, lifeStarted, lifeOpenBrowser;
        private string lifeName = "小G", lifePending = "", lifeRenderKey = "", lifeChatError = "";
        private List<IDictionary<string, object>> lifeHistory = new List<IDictionary<string, object>>();

        private void InitializeLifeLink(bool live, bool manual = false)
        {
            if (!live) return;
            // Isolated verification profiles must never write gestures into the user's real companion.
            if (!manual && !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_PET_DATA_DIR"))) return;
            if (lifeTimer != null) return;
            lifeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            lifeTimer.Tick += delegate { PollLife(); };
            if (settings.LifeLink) { lifeTimer.Start(); PollLife(); }
        }
        private void SetLifeLink(bool enabled)
        {
            settings.LifeLink = enabled;
            if (lifeTimer != null) { if (enabled) { lifeTimer.Start(); PollLife(); } else lifeTimer.Stop(); }
            if (!enabled) { lifeBusy = false; lifeOnline = false; }
            Persist(); ContextMenu = MakeMenu(); UpdateLifeChat();
        }
        private void StartLifeService()
        {
            if (lifeOnline || lifeStarted || !settings.LifeLink || closed) return;
            lifeStarted = true;
            try
            {
                string link = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "companion-link.json");
                if (!File.Exists(link)) return;
                var config = Json.Parse(File.ReadAllText(link));
                string directory = Path.GetFullPath(Json.Text(config, "projectDirectory"));
                string python = Path.GetFullPath(Json.Text(config, "pythonExecutable"));
                string script = Path.Combine(directory, "app.py");
                if (!File.Exists(script) || !File.Exists(python) || !Path.GetFileName(python).Equals("python.exe", StringComparison.OrdinalIgnoreCase)) return;
                var service = Process.Start(new ProcessStartInfo(python, "-X utf8 \"" + script + "\" --port 8766") {
                    WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                });
                if (service != null)
                {
                    string runtime = Path.Combine(directory, "tmp"); Directory.CreateDirectory(runtime);
                    File.WriteAllText(Path.Combine(runtime, "server.pid"), service.Id.ToString()); service.Dispose();
                }
            }
            catch (Exception ex) { Program.LogError(ex); }
        }
        private void PollLife()
        {
            if (closed || lifePolling || !settings.LifeLink) return;
            lifePolling = true;
            Task.Factory.StartNew(delegate { return LocalCompanionClient.Request("api/pet"); }).ContinueWith(task =>
            {
                if (closed || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    lifePolling = false; if (closed || !settings.LifeLink) return;
                    if (task.IsFaulted)
                    {
                        lifeOnline = false; lifeBusy = false;
                        lifeTimer.Interval = TimeSpan.FromSeconds(10);
                        StartLifeService(); UpdateLifeChat(); return;
                    }
                    var snapshot = task.Result;
                    if (snapshot == null || Json.Number(snapshot, "protocol") != 1) { lifeOnline = false; UpdateLifeChat(); return; }
                    lifeOnline = true; lifeTimer.Interval = TimeSpan.FromSeconds(3);
                    lifeName = Ui.Short(Json.Text(snapshot, "name"), 30);
                    lifeHistory = LocalCompanionClient.Rows(snapshot, "messages");
                    var jobs = LocalCompanionClient.Rows(snapshot, "jobs"); lifeBusy = jobs.Count > 0;
                    if (lifePending.Length > 0)
                    {
                        var pending = LocalCompanionClient.Rows(snapshot, "recent_jobs").FirstOrDefault(j => Json.Text(j, "id") == lifePending);
                        if (pending == null || Json.Text(pending, "status") != "running")
                        {
                            if (pending == null || Json.Text(pending, "status") == "error")
                            {
                                lifeChatError = pending == null ? "上一条任务状态未能确认，可能是服务已重启。" : Json.Text(pending, "message");
                                Say(lifeName + " · 没有完成", Ui.Short(lifeChatError, 70), 12);
                            }
                            lifePending = "";
                        }
                    }
                    ObserveLifeMessages(); UpdateLifeChat(); RefreshView();
                    if (lifeStatus != null && lifeBusy) lifeStatus.Text = Ui.Short(Json.Text(jobs[0], "message"), 90);
                    if (lifeOpenBrowser) { lifeOpenBrowser = false; Process.Start(new ProcessStartInfo(LocalCompanionClient.Home) { UseShellExecute = true }); }
                }));
            });
        }
        private void ObserveLifeMessages()
        {
            if (lifeHistory.Count == 0) return;
            string latest = Json.Text(lifeHistory.Last(), "id");
            if (settings.LastLifeMessage == latest) return;
            bool first = settings.LastLifeMessage.Length == 0;
            int previous = lifeHistory.FindIndex(m => Json.Text(m, "id") == settings.LastLifeMessage);
            var message = lifeHistory.Skip(previous + 1).LastOrDefault(m => Json.Text(m, "role") == "assistant");
            settings.LastLifeMessage = latest; Persist();
            if (first || message == null) return;
            string kind = Json.Text(message, "kind");
            bool manual = lifeChat != null && lifeChat.IsVisible && (kind == "chat" || kind == "tool");
            if (!manual && (!settings.LifeNotices || !settings.Notifications || (settings.Quiet && kind != "reminder"))) return;
            string title = lifeName + (kind == "reminder" ? " · 提醒你" : kind == "proactive" ? " · 给你的信" : kind == "share" ? " · 看到一条视频" : " · 回复你");
            Say(title, Ui.Short(Json.Text(message, "text"), 85), 18);
            if (!settings.Quiet && !dragging && action.Current == PetAction.None) PlayAction(PetAction.Greet);
        }
        private void RecordLifeInteraction(string kind)
        {
            if (!lifeOnline || !settings.LifeLink || lifeTimer == null) return;
            Task.Factory.StartNew(delegate { return LocalCompanionClient.Request("api/pet/interaction", new { kind = kind }); })
                .ContinueWith(task => { if (task.IsFaulted) { var ignored = task.Exception; } });
        }
        private void OpenLifeHome()
        {
            InitializeLifeLink(true, true);
            if (!settings.LifeLink) SetLifeLink(true);
            if (lifeOnline) { Process.Start(new ProcessStartInfo(LocalCompanionClient.Home) { UseShellExecute = true }); return; }
            lifeOpenBrowser = true; lifeStarted = false; StartLifeService(); PollLife(); OpenLifeChat();
        }
        internal void OpenLifeChat()
        {
            InitializeLifeLink(true, true);
            if (!settings.LifeLink) SetLifeLink(true);
            if (lifeChat != null) { lifeChat.Show(); lifeChat.Activate(); lifeInput.Focus(); return; }
            lifeChat = new Window { Title = "小G · 和桌宠聊聊", Width = 480, Height = 660, MinWidth = 390, MinHeight = 480,
                Background = Ui.Brush("#FFFDFF"), FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            var root = new DockPanel { Margin = new Thickness(20) };
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top);
            header.Children.Add(Ui.Text("和小G聊聊", 23, Ui.Ink));
            lifeStatus = Ui.Text("正在连接本机小G…", 11, Ui.Muted); lifeStatus.Margin = new Thickness(0, 8, 0, 12); header.Children.Add(lifeStatus);
            var quick = new WrapPanel();
            quick.Children.Add(Ui.Button("摸摸头", PetHead)); quick.Children.Add(Ui.Button("一起喝茶", DrinkTea)); quick.Children.Add(Ui.Button("喂点心", Feed));
            quick.Children.Add(Ui.Button("给我写封信", delegate { SendLifeMessage("/日记"); }));
            quick.Children.Add(Ui.Button("一起看视频", delegate { SubmitLife("api/life/browse", new { }); }));
            quick.Children.Add(Ui.Button("打开日常页", OpenLifeHome)); header.Children.Add(quick); root.Children.Add(header);
            var composer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(composer, Dock.Bottom);
            lifeInput = new TextBox { Name = "LifeMessageInput", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 78,
                MaxLength = 4000, Padding = new Thickness(10), FontSize = 13, BorderBrush = Ui.Line, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            System.Windows.Automation.AutomationProperties.SetName(lifeInput, "给小G的消息");
            lifeInput.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { SendLifeMessage(lifeInput.Text); e.Handled = true; } };
            composer.Children.Add(lifeInput);
            var sendRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            lifeSend = Ui.Button("发送", delegate { SendLifeMessage(lifeInput.Text); }); DockPanel.SetDock(lifeSend, Dock.Right); sendRow.Children.Add(lifeSend);
            sendRow.Children.Add(Ui.Text("Ctrl+Enter 发送 · 与日常页共用记忆", 10, Ui.Muted)); composer.Children.Add(sendRow); root.Children.Add(composer);
            lifeMessages = new StackPanel(); lifeScroll = new ScrollViewer { Content = lifeMessages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(lifeScroll);
            lifeChat.Content = root;
            lifeChat.Closed += delegate { lifeChat = null; lifeInput = null; lifeMessages = null; lifeScroll = null; lifeSend = null; lifeStatus = null; lifeRenderKey = ""; };
            UpdateLifeChat(); lifeChat.Show(); lifeInput.Focus(); lifeStarted = false; StartLifeService(); PollLife();
        }
        private void SendLifeMessage(string message)
        {
            message = (message ?? "").Trim();
            if (message.Length == 0 || message.Length > 4000) return;
            SubmitLife("api/life/chat", new { message = message });
        }
        private void SubmitLife(string path, object body)
        {
            if (closed || lifePosting || lifeBusy || lifePending.Length > 0) return;
            if (!lifeOnline) { lifeChatError = "小G还没连接，正在尝试唤醒。请稍后再发送。"; lifeStarted = false; StartLifeService(); PollLife(); UpdateLifeChat(); return; }
            lifePosting = true; lifeChatError = ""; UpdateLifeChat();
            if (action.Current == PetAction.Sleep) WakeUp();
            Task.Factory.StartNew(delegate { return LocalCompanionClient.Request(path, body); }).ContinueWith(task =>
            {
                if (closed || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    lifePosting = false; if (closed) return;
                    if (task.IsFaulted) lifeChatError = task.Exception.GetBaseException().Message;
                    else
                    {
                        lifePending = Json.Text(task.Result, "job_id");
                        if (lifeInput != null) lifeInput.Clear();
                        Say(lifeName + " · 收到啦", "让我想一想，完整回复会留在聊天窗里。", 12);
                    }
                    UpdateLifeChat(); PollLife();
                }));
            });
        }
        private void UpdateLifeChat()
        {
            if (lifeChat == null || lifeMessages == null) return;
            lifeStatus.Text = lifeChatError.Length > 0 ? lifeChatError : !lifeOnline ? "小G未连接。可先打开日常页，或等待她启动。" : lifeBusy || lifePosting || lifePending.Length > 0 ? "小G正在回应你…" : "已连接 · 记忆和对话与日常页相同";
            lifeSend.IsEnabled = lifeOnline && !lifeBusy && !lifePosting && lifePending.Length == 0;
            string key = Json.Write(lifeHistory);
            if (key == lifeRenderKey) return;
            lifeRenderKey = key; lifeMessages.Children.Clear();
            foreach (var message in lifeHistory)
            {
                bool user = Json.Text(message, "role") == "user";
                var box = new StackPanel { Margin = new Thickness(user ? 35 : 0, 8, user ? 0 : 20, 8) };
                box.Children.Add(Ui.Text(user ? "你" : lifeName, 10, Ui.Muted));
                var text = new TextBox { Text = Json.Text(message, "text"), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    BorderThickness = new Thickness(0), Background = user ? Ui.Soft : Ui.Brush("#F8F7FA"), Foreground = Ui.Ink, Padding = new Thickness(11),
                    FontSize = 12, Margin = new Thickness(0, 5, 0, 0) };
                box.Children.Add(text); lifeMessages.Children.Add(box);
            }
            lifeScroll.ScrollToEnd();
        }
    }
}
