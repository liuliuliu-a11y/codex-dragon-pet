using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CodexDragon
{
    // A bounded incremental JSONL reader. Partial writes and split UTF-8 characters survive ticks.
    public sealed class LogCursor
    {
        public const int MaxLine = 2 * 1024 * 1024;
        public long Position;
        private readonly Decoder decoder = new UTF8Encoding(false, false).GetDecoder();
        private readonly StringBuilder pending = new StringBuilder();
        private bool skipping;
        public bool HasPendingRecord { get { return skipping || pending.Length > 0; } }
        public List<string> Read(string file, bool first, byte[] bytes = null, char[] chars = null)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (first || stream.Length < Position)
                {
                    Position = Math.Max(0, stream.Length - 2L * 1024 * 1024);
                    skipping = Position > 0; pending.Clear(); decoder.Reset();
                }
                stream.Position = Position;
                if (bytes == null) bytes = new byte[32768];
                if (chars == null) chars = new char[bytes.Length];
                int read, budget = 4 * 1024 * 1024;
                while (budget > 0 && (read = stream.Read(bytes, 0, Math.Min(bytes.Length, budget))) > 0)
                {
                    Position += read; budget -= read;
                    int count = decoder.GetChars(bytes, 0, read, chars, 0, false);
                    for (int i = 0; i < count; i++)
                    {
                        char c = chars[i];
                        if (c == '\n') { if (!skipping && pending.Length > 0) lines.Add(pending.ToString().TrimEnd('\r')); pending.Clear(); skipping = false; }
                        else if (!skipping)
                        { if (pending.Length < MaxLine) pending.Append(c); else { pending.Clear(); skipping = true; } }
                    }
                }
            }
            return lines;
        }
    }

    public sealed class SessionMonitor : IDisposable
    {
        public readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Quota> Quotas = new Dictionary<string, Quota>();
        public event Action<Session> Completed;
        public string Connection = "正在连接本机 Codex";
        private readonly string root;
        private readonly Dictionary<string, LogCursor> cursors = new Dictionary<string, LogCursor>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> dirty = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private sealed class Completion
        {
            public Session Session;
            public string TurnId;
            public DateTime QuietSince;
            public long Length = -1;
        }
        private readonly Dictionary<string, Completion> pending = new Dictionary<string, Completion>(StringComparer.OrdinalIgnoreCase);
        public static readonly TimeSpan CompletionQuietPeriod = TimeSpan.FromSeconds(3);
        private readonly Func<DateTime> utcNow;
        private FileSystemWatcher watcher;
        private DateTime lastScan = DateTime.MinValue;
        private readonly DateTime boot;
        private readonly byte[] readBytes = new byte[32768];
        private readonly char[] readChars = new char[32768];
        private bool initial = true;
        public SessionMonitor(string sessionsRoot, Func<DateTime> clock = null)
        {
            root = sessionsRoot; utcNow = clock ?? delegate { return DateTime.UtcNow; }; boot = utcNow();
            if (Directory.Exists(root))
            {
                watcher = new FileSystemWatcher(root, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                watcher.Changed += delegate(object sender, FileSystemEventArgs e) { dirty[e.FullPath] = 0; };
                watcher.Created += delegate(object sender, FileSystemEventArgs e) { dirty[e.FullPath] = 0; };
                watcher.Error += delegate { lastScan = DateTime.MinValue; };
                watcher.EnableRaisingEvents = true;
            }
        }
        public void Poll()
        {
            if (!Directory.Exists(root)) { Connection = "尚未发现本机聊天记录"; return; }
            DateTime now = utcNow();
            if (now - lastScan > TimeSpan.FromSeconds(20))
            {
                try
                {
                    foreach (var file in new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).Take(24)) dirty[file.FullName] = 0;
                    lastScan = now;
                }
                catch (IOException) { Connection = "聊天记录暂时不可读，正在重试"; }
                catch (UnauthorizedAccessException) { Connection = "聊天记录访问受限"; }
            }
            foreach (string file in dirty.Keys.ToArray())
            {
                byte ignored; dirty.TryRemove(file, out ignored);
                bool fresh = !cursors.ContainsKey(file);
                if (fresh) { cursors[file] = new LogCursor(); Sessions[file] = new Session(file); ReadMetadata(file, Sessions[file]); }
                try
                {
                    bool truncated = !fresh && new FileInfo(file).Length < cursors[file].Position;
                    if (truncated) { Sessions[file] = new Session(file); ReadMetadata(file, Sessions[file]); }
                    Session session = Sessions[file];
                    foreach (string line in cursors[file].Read(file, fresh, readBytes, readChars))
                    {
                        try
                        {
                            var record = Json.Parse(line);
                            // Initial old records are restored silently; a newly created live file may notify.
                            DateTime at = Json.Time(Json.Text(record ?? Json.Map(null), "timestamp"), DateTime.MinValue);
                            bool history = initial || truncated || (fresh && at < boot);
                            bool done = session.Apply(record, history, SetQuota);
                            if (done) pending[file] = new Completion { Session = session, TurnId = session.TurnId, QuietSince = now };
                        }
                        catch (ArgumentException) { }
                        catch (InvalidOperationException) { }
                    }
                    // Long turns can put turn_context before the 2 MiB event
                    // tail. Recover the latest execution settings on restart.
                    if (fresh || truncated) ReadLatestContext(file, session);
                    if (new FileInfo(file).Length > cursors[file].Position) dirty[file] = 0;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            ConfirmCompletions(now);
            initial = false;
            if (Sessions.Count > 64)
            {
                foreach (var file in Sessions.Values.OrderByDescending(s => s.Updated).Skip(64).Select(s => s.FilePath).ToArray()) { Sessions.Remove(file); cursors.Remove(file); pending.Remove(file); }
            }
            Connection = Sessions.Count > 0 ? "已连接本机 Codex" : "等待 Codex 开始聊天";
        }
        private void ConfirmCompletions(DateTime now)
        {
            var ready = new List<Session>();
            foreach (var pair in pending.ToArray())
            {
                Session current; Completion completion = pair.Value;
                if (!Sessions.TryGetValue(pair.Key, out current) || current != completion.Session || !current.Finishing || current.TurnId != completion.TurnId)
                { pending.Remove(pair.Key); continue; }
                try
                {
                    long length = new FileInfo(pair.Key).Length;
                    if (length != completion.Length || cursors[pair.Key].HasPendingRecord || cursors[pair.Key].Position != length)
                    {
                        completion.Length = length; completion.QuietSince = now;
                        // Retry unread data even if a filesystem notification was missed.
                        if (cursors[pair.Key].Position != length) dirty[pair.Key] = 0;
                        continue;
                    }
                    if (now - completion.QuietSince < CompletionQuietPeriod || !current.ConfirmCompletion()) continue;
                    pending.Remove(pair.Key); ready.Add(current);
                }
                catch (IOException) { completion.QuietSince = now; }
                catch (UnauthorizedAccessException) { completion.QuietSince = now; }
            }
            // All sessions must have their final state before a notification chooses its chat.
            foreach (Session session in ready) if (!session.IsChild && Completed != null) Completed(session);
        }
        private static void ReadMetadata(string file, Session session)
        {
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    char[] buffer = new char[65536]; int length = reader.Read(buffer, 0, buffer.Length);
                    string head = new string(buffer, 0, length); int end = head.IndexOf('\n');
                    if (end >= 0) session.Apply(Json.Parse(head.Substring(0, end)), true, null);
                }
            }
            catch (IOException) { } catch (ArgumentException) { } catch (InvalidOperationException) { } catch (UnauthorizedAccessException) { }
        }
        private static void ReadLatestContext(string file, Session session)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long end = stream.Length, remaining = 64L * 1024 * 1024;
                string carry = "";
                while (end > 0 && remaining > 0)
                {
                    int count = (int)Math.Min(end, 1024 * 1024);
                    long start = end - count;
                    byte[] data = new byte[count]; stream.Position = start;
                    int read = 0, n;
                    while (read < count && (n = stream.Read(data, read, count - read)) > 0) read += n;
                    string chunk = Encoding.UTF8.GetString(data, 0, read) + carry;
                    int boundary = chunk.IndexOf('\n');
                    string whole = start == 0 ? chunk : boundary < 0 ? "" : chunk.Substring(boundary + 1);
                    var lines = whole.Split('\n');
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        string line = lines[i];
                        if (line.Length > LogCursor.MaxLine || line.IndexOf("\"turn_context\"", 0, Math.Min(line.Length, 256), StringComparison.Ordinal) < 0) continue;
                        try
                        {
                            var entry = Json.Parse(line);
                            if (entry != null && Json.Text(entry, "type") == "turn_context") { session.Apply(entry, true, null); return; }
                        }
                        catch (ArgumentException) { } catch (InvalidOperationException) { }
                    }
                    carry = boundary < 0 ? chunk : chunk.Substring(0, boundary);
                    if (carry.Length > LogCursor.MaxLine) carry = "";
                    end = start; remaining -= count;
                }
            }
        }
        public void SetQuota(Quota quota)
        {
            Quota old;
            if (!Quotas.TryGetValue(quota.Id, out old) || quota.Updated >= old.Updated) Quotas[quota.Id] = quota;
        }
        public Quota PrimaryQuota { get { Quota q; return Quotas.TryGetValue("codex", out q) ? q : Quotas.Values.FirstOrDefault(); } }
        public Session Select(string pinned)
        {
            var sessions = Sessions.Values.Where(s => !s.IsChild);
            if (!String.IsNullOrEmpty(pinned)) return sessions.FirstOrDefault(s => s.Id == pinned);
            // An abandoned old turn must not hide a chat that is active now.
            return sessions.OrderByDescending(s => s.Running && utcNow() - s.Updated < TimeSpan.FromMinutes(10)).ThenByDescending(s => s.Updated).FirstOrDefault();
        }
        public void Dispose() { if (watcher != null) watcher.Dispose(); }
    }

    public static class AccountReader
    {
        private static readonly object processGate = new object();
        private static Process pending;
        public static void CancelPending()
        {
            lock (processGate)
            { if (pending != null) { try { if (!pending.HasExited) pending.Kill(); } catch (InvalidOperationException) { } } }
        }
        public static string FindCodex()
        {
            foreach (var process in Process.GetProcessesByName("codex"))
            {
                using (process) { try { string path = process.MainModule.FileName; if (path.EndsWith("codex.exe", StringComparison.OrdinalIgnoreCase)) return path; } catch { } }
            }
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            try { return Directory.GetFiles(directory, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault(); }
            catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
        }

        // Only initialize and account/rateLimits/read are sent. No inference turn is created.
        public static List<Quota> Read(string executable)
        {
            if (String.IsNullOrEmpty(executable) || !File.Exists(executable)) throw new IOException("未找到 Codex 程序，先打开 Codex 后再试。");
            using (var process = new Process())
            using (var finished = new ManualResetEvent(false))
            {
                List<Quota> result = null; string error = null;
                object gate = new object(); bool closing = false;
                process.StartInfo = new ProcessStartInfo(executable, "app-server --listen stdio://") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    lock (gate)
                    {
                        if (closing || e.Data == null) return;
                        try
                        {
                            var message = Json.Parse(e.Data); var id = Json.Number(message, "id");
                            if (id == 1)
                            {
                                if (Json.Get(message, "error") != null) { error = "Codex 额度连接初始化失败"; finished.Set(); return; }
                                process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                                process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                            }
                            if (id == 2)
                            {
                                if (Json.Get(message, "error") != null) error = "额度暂不可用，请检查 Codex 登录状态";
                                else result = Quota.ReadAccount(Json.Map(Json.Get(message, "result")), DateTime.UtcNow);
                                finished.Set();
                            }
                        }
                        catch (Exception ex) { if (ex is OutOfMemoryException) throw; error = "额度连接返回了无法识别的数据"; finished.Set(); }
                    }
                };
                process.ErrorDataReceived += delegate { }; // Never copy server diagnostics or credentials into pet logs.
                try
                {
                    process.Start(); lock (processGate) { pending = process; }
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex_dragon_pet\",\"title\":\"Codex Dragon Pet\",\"version\":\"0.2.0\"}}}");
                    if (!finished.WaitOne(15000)) throw new IOException("额度刷新超时，稍后自动重试");
                    if (error != null) throw new IOException(error);
                    if (result == null || result.Count == 0) throw new IOException("账户尚未返回额度数据");
                    return result;
                }
                finally
                {
                    lock (gate) { closing = true; }
                    lock (processGate) { if (pending == process) pending = null; }
                    try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                    try { process.WaitForExit(1000); } catch (InvalidOperationException) { }
                }
            }
        }
    }
}
