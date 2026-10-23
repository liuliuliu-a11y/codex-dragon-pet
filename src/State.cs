using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexDragon
{
    public enum Mood { Idle, Working, Thinking, Done, Waiting, LowQuota, Stopped }

    public static class Json
    {
        public static IDictionary<string, object> Parse(string text)
        {
            return new JavaScriptSerializer { MaxJsonLength = 2097152, RecursionLimit = 60 }
                .DeserializeObject(text) as IDictionary<string, object>;
        }
        public static IDictionary<string, object> Map(object value)
        { return value as IDictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(IDictionary<string, object> map, params string[] names)
        {
            foreach (string name in names) { object value; if (map.TryGetValue(name, out value)) return value; }
            return null;
        }
        public static string Text(IDictionary<string, object> map, params string[] names)
        { return Convert.ToString(Get(map, names), CultureInfo.InvariantCulture) ?? ""; }
        public static double? Number(IDictionary<string, object> map, params string[] names)
        {
            object value = Get(map, names); double result;
            if (value == null || !Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) || Double.IsInfinity(result) || Double.IsNaN(result)) return null;
            return result;
        }
        public static DateTime Time(string text, DateTime fallback)
        {
            DateTime value;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value) ? value.ToUniversalTime() : fallback;
        }
        public static string Write(object value) { return new JavaScriptSerializer().Serialize(value); }
    }

    public sealed class QuotaWindow
    {
        public double Used;
        public int Minutes;
        public DateTime? Reset;
        public string Label
        {
            get
            {
                if (Minutes <= 0) return "额度窗口";
                if (Minutes % 1440 == 0) return (Minutes / 1440) + " 天额度";
                if (Minutes % 60 == 0) return (Minutes / 60) + " 小时额度";
                return Minutes + " 分钟额度";
            }
        }
        public bool Expired { get { return Reset.HasValue && Reset.Value <= DateTime.UtcNow; } }
        public static QuotaWindow Read(object raw)
        {
            var map = Json.Map(raw); double? used = Json.Number(map, "usedPercent", "used_percent");
            if (!used.HasValue) return null;
            var window = new QuotaWindow { Used = Math.Max(0, Math.Min(100, used.Value)), Minutes = (int)(Json.Number(map, "windowDurationMins", "window_minutes") ?? 0) };
            double? seconds = Json.Number(map, "resetsAt", "resets_at");
            if (seconds.HasValue) { try { window.Reset = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds.Value); } catch (ArgumentOutOfRangeException) { } }
            return window;
        }
    }

    public sealed class Quota
    {
        public string Id = "codex", Name = "Codex", Source = "等待额度数据";
        public List<QuotaWindow> Windows = new List<QuotaWindow>();
        public DateTime Updated = DateTime.MinValue;
        public bool Fresh { get { return DateTime.UtcNow - Updated < TimeSpan.FromMinutes(3); } }
        public QuotaWindow Main { get { return Windows.OrderByDescending(w => w.Used).FirstOrDefault(); } }
        public static Quota Read(IDictionary<string, object> bucket, DateTime at, string source)
        {
            var result = new Quota { Id = Json.Text(bucket, "limitId", "limit_id"), Updated = at, Source = source };
            result.Name = Json.Text(bucket, "limitName", "limit_name");
            if (String.IsNullOrEmpty(result.Id)) result.Id = "codex";
            if (String.IsNullOrEmpty(result.Name)) result.Name = result.Id == "codex" ? "Codex" : result.Id;
            foreach (string key in new[] { "primary", "secondary" })
            { var window = QuotaWindow.Read(Json.Get(bucket, key)); if (window != null) result.Windows.Add(window); }
            return result;
        }
        public static List<Quota> ReadAccount(IDictionary<string, object> result, DateTime at)
        {
            var buckets = Json.Map(Json.Get(result, "rateLimitsByLimitId"));
            var list = new List<Quota>();
            foreach (var bucket in buckets.Values) list.Add(Read(Json.Map(bucket), at, "Codex 账户"));
            if (list.Count == 0 && Json.Get(result, "rateLimits") != null) list.Add(Read(Json.Map(Json.Get(result, "rateLimits")), at, "Codex 账户"));
            return list;
        }
    }

    public sealed class Session
    {
        public string Id, FilePath, Title = "Codex 聊天", Project = "", TurnId = "", Stage = "在这里陪你", PublicMessage = "", Result = "", Model = "";
        public Mood Mood = Mood.Idle;
        public bool Running, IsChild;
        public bool Finishing { get; private set; }
        public bool HasFinalOutput { get; private set; }
        public DateTime Updated = DateTime.MinValue, Started = DateTime.MinValue;
        public double Tokens;
        private readonly HashSet<string> completed = new HashSet<string>();
        public Session(string file)
        {
            FilePath = file;
            var match = Regex.Match(Path.GetFileName(file), @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
            Id = match.Success ? match.Value : Path.GetFileNameWithoutExtension(file);
        }

        // Only public commentary is copied into the UI. Reasoning records are never read for text.
        public bool Apply(IDictionary<string, object> entry, bool history, Action<Quota> quotaChanged)
        {
            if (entry == null) return false;
            string kind = Json.Text(entry, "type"); var payload = Json.Map(Json.Get(entry, "payload"));
            var at = Json.Time(Json.Text(entry, "timestamp"), DateTime.UtcNow);
            if (kind == "session_meta")
            {
                Project = Json.Text(payload, "cwd");
                var source = Json.Get(payload, "source");
                IsChild = source is IDictionary<string, object> && Json.Map(source).ContainsKey("subagent");
                string id = Json.Text(payload, "id", "session_id"); if (id.Length > 0) Id = id;
                return false;
            }
            if (kind == "turn_context") { Model = Json.Text(payload, "model"); return false; }
            if (kind == "event_msg")
            {
                string type = Json.Text(payload, "type");
                if (type == "token_count")
                {
                    var info = Json.Map(Json.Get(payload, "info"));
                    Tokens = Json.Number(Json.Map(Json.Get(info, "total_token_usage")), "total_tokens") ?? Tokens;
                    if (Json.Get(payload, "rate_limits") != null && quotaChanged != null)
                        quotaChanged(Quota.Read(Json.Map(Json.Get(payload, "rate_limits")), at, "Codex 聊天记录"));
                    return false;
                }
                if (type == "task_started")
                {
                    TurnId = Json.Text(payload, "turn_id"); Running = true; Mood = Mood.Thinking;
                    Finishing = false; HasFinalOutput = false;
                    Stage = "正在准备"; PublicMessage = ""; Result = ""; Started = at; Updated = at;
                }
                else if (type == "task_complete")
                {
                    string id = Json.Text(payload, "turn_id", "root_turn_id");
                    if (id.Length == 0) id = at.Ticks.ToString(CultureInfo.InvariantCulture);
                    if (completed.Contains(id)) return false;
                    // A late completion from an older turn must not finish the current turn.
                    if (TurnId.Length > 0 && id != TurnId) return false;
                    completed.Add(id);
                    if (completed.Count > 256) { completed.Clear(); completed.Add(id); }
                    bool notify = Running && !history;
                    string result = CleanText(ReadMessage(Json.Get(payload, "last_agent_message")));
                    if (result.Length > 0) { Result = result; HasFinalOutput = true; }
                    TurnId = id; Finishing = notify; Running = notify; Updated = at;
                    Mood = notify ? Mood.Working : Mood.Done;
                    Stage = notify ? "正在收尾，确认结果" : "本轮工作结束啦";
                    return notify;
                }
                else if (type == "turn_aborted" || type == "task_interrupted" || type == "task_cancelled")
                { Finishing = false; Running = false; Mood = Mood.Stopped; Stage = "本轮已中断"; Updated = at; }
                else if (type == "task_failed" || type == "error")
                { Finishing = false; Running = false; Mood = Mood.Stopped; Stage = "遇到问题，回 Codex 看看"; Updated = at; }
                else if (type == "item_completed")
                {
                    var item = Json.Map(Json.Get(payload, "item")); string itemType = Json.Text(item, "type");
                    if (itemType == "AgentMessage") ReadAssistant(Json.Text(item, "phase"), ReadMessage(Json.Get(item, "content")), at);
                    if (Running && itemType == "CommandExecution") SetActivity(Json.Text(item, "command"), at);
                    if (Running && itemType == "McpToolCall") SetActivity(Json.Text(item, "tool"), at);
                }
                // Other event payloads (including agent_reasoning) are deliberately ignored.
                return false;
            }
            if (kind != "response_item") return false;
            string responseType = Json.Text(payload, "type");
            if (responseType == "message" && Json.Text(payload, "role") == "assistant")
                ReadAssistant(Json.Text(payload, "phase"), ReadMessage(Json.Get(payload, "content")), at);
            else if (responseType == "function_call" || responseType == "custom_tool_call")
            {
                // Startup reads a bounded tail; the matching start may be outside that tail.
                if (Updated == DateTime.MinValue && Mood == Mood.Idle) Running = true;
                if (Running) SetActivity(Json.Text(payload, "name") + " " + Json.Text(payload, "arguments", "input"), at);
            }
            return false;
        }

        // The monitor confirms only after the entire log batch and a quiet interval.
        public bool ConfirmCompletion()
        {
            if (!Finishing || !HasFinalOutput) return false;
            Finishing = false; Running = false; Mood = Mood.Done; Stage = "本轮工作结束啦";
            return true;
        }
        private void ResumeActivity()
        {
            if (!Finishing) return;
            completed.Remove(TurnId);
            Finishing = false; HasFinalOutput = false; Result = "";
            Mood = Mood.Working; Stage = "正在处理任务";
        }
        private void ReadAssistant(string phase, string text, DateTime at)
        {
            if (phase == "commentary") { ResumeActivity(); SetPublic(text); Updated = at; }
            else if (phase == "final_answer" || phase == "final")
            {
                string result = CleanText(text);
                if (result.Length > 0) { Result = result; HasFinalOutput = true; }
                if (Running) { Mood = Mood.Working; Stage = Finishing ? "正在收尾，确认结果" : "正在输出结果"; Updated = at; }
            }
        }

        private static string ReadMessage(object raw)
        {
            var blocks = raw as IEnumerable;
            if (raw is string) return (string)raw;
            if (blocks == null) return "";
            var list = new List<string>();
            foreach (object value in blocks)
            {
                var block = Json.Map(value); string type = Json.Text(block, "type");
                if (type == "output_text" || type == "text") list.Add(Json.Text(block, "text"));
            }
            return String.Join(" ", list);
        }
        private void SetPublic(string text)
        {
            text = CleanText(text);
            if (text.Length > 0) PublicMessage = text;
        }
        private static string CleanText(string text)
        {
            text = Regex.Replace(text ?? "", @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            text = Regex.Replace(text, @"[`*#]", "");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text.Length <= 300 ? text : text.Substring(0, 300) + "…";
        }
        private void SetActivity(string activity, DateTime at)
        {
            ResumeActivity();
            activity = (activity ?? "").ToLowerInvariant(); Mood = Mood.Working;
            if (activity.Contains("imagegen") || activity.Contains("image_gen")) Stage = "正在画图";
            else if (activity.Contains("test") || activity.Contains("pytest") || activity.Contains("验证")) Stage = "正在检查和测试";
            else if (activity.Contains("apply_patch") || activity.Contains("write_file")) Stage = "正在修改文件";
            else if (activity.Contains("web__") || activity.Contains("search") || activity.Contains("browser") || activity.Contains("cua")) Stage = "正在查阅资料";
            else if (activity.Contains("get-content") || activity.Contains("read_file") || activity.Contains("rg ")) Stage = "正在阅读文件";
            else if (activity.Contains("wait") || activity.Contains("sleep")) { Stage = "正在等待结果"; Mood = Mood.Waiting; }
            else Stage = "正在处理任务";
            Updated = at;
        }
    }

    public sealed class Settings
    {
        public double Left = -999999, Top = -999999, Scale = .5;
        public bool Sound = true, Topmost = true, Bubble = true, SmallTalk = true, Notifications = true;
        public bool AutoBubble = true, ClickThrough, Quiet, ReduceMotion;
        public bool DailyActions = true;
        public string PinnedThread = "";
        public static readonly string Folder = DataFolder();
        private static string DataFolder()
        {
            string explicitFolder = Environment.GetEnvironmentVariable("CODEX_PET_DATA_DIR");
            return !String.IsNullOrWhiteSpace(explicitFolder) && Path.IsPathRooted(explicitFolder)
                ? Path.GetFullPath(explicitFolder)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDragonPet");
        }
        public static Settings Load()
        {
            try { return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder, "settings.json"))) ?? new Settings(); }
            catch { return new Settings(); }
        }
        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, "settings.json"), temp = file + ".tmp";
            File.WriteAllText(temp, Json.Write(this));
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        }
    }
}
