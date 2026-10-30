using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexDragon
{
    // Reuse the existing event parser; the avatar receives only public status.
    internal sealed class BridgeSnapshot
    {
        public string version = "1";
        public bool connected, running, finishing;
        public string title, stage, message, completedId, updatedAt;
        public string model, reasoningEffort, sessionId;
        public long completionSerial;
        public double? quotaUsed;
        public int? quotaMinutes;

        public static BridgeSnapshot Read(SessionMonitor monitor, string pinned, long serial, string completed)
        {
            Session session = monitor.Select(pinned);
            Quota quota = monitor.PrimaryQuota;
            QuotaWindow window = quota == null || !quota.Fresh ? null : quota.Main;
            if (window != null && window.Expired) window = null;
            string publicText = session == null ? "" : session.PublicMessage;
            return new BridgeSnapshot {
                connected = monitor.Sessions.Count > 0,
                running = session != null && session.Running,
                finishing = session != null && session.Finishing,
                title = session == null ? "Codex 陪伴" : session.Title,
                model = session == null ? "" : session.Model,
                reasoningEffort = session == null ? "" : session.ReasoningEffort,
                sessionId = session == null ? "" : session.Id,
                stage = session == null ? "等你开始工作" : session.Stage,
                message = publicText.Length > 180 ? publicText.Substring(0, 180) + "…" : publicText,
                completedId = completed, completionSerial = serial,
                quotaUsed = window == null ? (double?)null : window.Used,
                quotaMinutes = window == null ? (int?)null : window.Minutes,
                updatedAt = DateTime.UtcNow.ToString("o")
            };
        }
    }

    internal static class BridgeProgram
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length < 2 || args[0] != "--bridge") return 2;
            int parent = 0;
            if (args.Length > 2) Int32.TryParse(args[2], out parent);
            try { return Run(args[1], parent); }
            catch (Exception error)
            {
                try { File.WriteAllText(Path.GetFullPath(args[1]) + ".error", error.GetType().Name + ": " + error.Message); } catch { }
                return 1;
            }
        }
        private static int Run(string destination, int parentId)
        {
            string output = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string temporary = output + ".tmp";
            var preferences = Settings.Load();
            long serial = 0; string completed = "";
            Task<System.Collections.Generic.List<Quota>> quotaTask = null;
            DateTime nextQuota = DateTime.MinValue;
            string codexRoot = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(codexRoot)) codexRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            using (var monitor = new SessionMonitor(Path.Combine(codexRoot, "sessions")))
            {
                monitor.Completed += delegate(Session session) {
                    if (preferences.Notifications && monitor.Select(preferences.PinnedThread) == session && !session.Running)
                    { completed = session.Id; serial++; }
                };
                while (ParentAlive(parentId))
                {
                    try
                    {
                        monitor.Poll();
                        if (quotaTask != null && quotaTask.IsCompleted)
                        {
                            if (!quotaTask.IsFaulted) foreach (var quota in quotaTask.Result) monitor.SetQuota(quota);
                            quotaTask = null;
                        }
                        if (DateTime.UtcNow >= nextQuota && quotaTask == null)
                        {
                            nextQuota = DateTime.UtcNow.AddMinutes(1);
                            quotaTask = Task.Factory.StartNew(() => AccountReader.Read(AccountReader.FindCodex()));
                        }
                        File.WriteAllText(temporary, Json.Write(BridgeSnapshot.Read(monitor, preferences.PinnedThread, serial, completed)), new UTF8Encoding(false));
                        if (File.Exists(output)) File.Replace(temporary, output, null); else File.Move(temporary, output);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    Thread.Sleep(750);
                }
            }
            AccountReader.CancelPending();
            return 0;
        }
        private static bool ParentAlive(int id)
        {
            if (id <= 0) return true;
            try { using (var parent = Process.GetProcessById(id)) return !parent.HasExited; }
            catch (ArgumentException) { return false; }
        }
    }
}
