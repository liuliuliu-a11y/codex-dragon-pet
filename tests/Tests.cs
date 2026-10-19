using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodexDragon;

public static class Tests
{
    private static int passed;
    private static void Check(bool condition, string description)
    { if (!condition) throw new Exception(description); passed++; Console.WriteLine("PASS " + description); }
    private static string Record(string payload)
    { return "{\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\",\"type\":\"event_msg\",\"payload\":" + payload + "}"; }
    private static bool Apply(Session session, string payload)
    { return session.Apply(Json.Parse(Record(payload)), false, null); }

    public static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "codex-pet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var session = new Session("rollout-00000000-0000-0000-0000-000000000001.jsonl");
            Apply(session, "{\"type\":\"task_started\",\"turn_id\":\"turn-1\"}");
            Check(session.Running && session.Mood == Mood.Thinking, "task starts in thinking state");
            session.Apply(Json.Parse("{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"commentary\",\"content\":[{\"type\":\"output_text\",\"text\":\"正在检查代码\"}]}}"), false, null);
            Apply(session, "{\"type\":\"agent_reasoning\",\"text\":\"SECRET INTERNAL TEXT\"}");
            Apply(session, "{\"type\":\"item_completed\",\"item\":{\"type\":\"Reasoning\",\"raw_content\":\"SECRET INTERNAL TEXT\"}}");
            Check(session.PublicMessage == "正在检查代码", "only public commentary is exposed");
            session.Apply(Json.Parse("{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"final_answer\",\"content\":[{\"type\":\"output_text\",\"text\":\"done\"}]}}"), false, null);
            Check(session.Running, "final message alone does not fake task completion");
            Check(Apply(session, "{\"type\":\"task_complete\",\"turn_id\":\"turn-1\"}"), "explicit completion notifies");
            Check(!Apply(session, "{\"type\":\"task_complete\",\"turn_id\":\"turn-1\"}"), "duplicate completion is silent");
            var summary = new Session("summary");
            Apply(summary, "{\"type\":\"task_started\",\"turn_id\":\"summary-turn\"}");
            Apply(summary, "{\"type\":\"task_complete\",\"turn_id\":\"summary-turn\",\"last_agent_message\":\"已经修复 **问题**，见 [说明](https://example.com)。\"}");
            Check(summary.Result.Contains("问题") && summary.Result.Contains("说明") && !summary.Result.Contains("**") && !summary.Result.Contains("https://"), "public completion summary preserves meaning without Markdown markup");
            Apply(summary, "{\"type\":\"task_started\",\"turn_id\":\"next-turn\"}");
            Check(summary.Result.Length == 0, "a new task clears the previous result summary");
            Apply(session, "{\"type\":\"task_started\",\"turn_id\":\"turn-2\"}");
            Check(!Apply(session, "{\"type\":\"task_complete\",\"turn_id\":\"older-turn\"}") && session.Running, "late old completion cannot end current turn");
            Apply(session, "{\"type\":\"turn_aborted\"}");
            Check(!session.Running && session.Mood == Mood.Stopped, "interruption is distinct from success");
            var tail = new Session("tail");
            tail.Apply(Json.Parse("{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"exec_command\",\"arguments\":\"run tests\"}}"), true, null);
            Check(tail.Running && tail.Mood == Mood.Working, "bounded startup tail can restore work without a start record");
            var child = new Session("child");
            child.Apply(Json.Parse("{\"type\":\"session_meta\",\"payload\":{\"source\":{\"subagent\":{\"parent_thread_id\":\"parent\"}}}}"), true, null);
            Check(child.IsChild, "subagent sessions are identified for notification filtering");

            var quota = Quota.Read(Json.Parse("{\"primary\":{\"used_percent\":23,\"window_minutes\":10080},\"secondary\":null}"), DateTime.UtcNow, "log");
            Check(quota.Windows.Count == 1 && quota.Main.Used == 23 && quota.Main.Label == "7 天额度", "snake-case quota and actual window duration");
            Check(Quota.Read(Json.Parse("{\"primary\":null,\"secondary\":null}"), DateTime.UtcNow, "log").Windows.Count == 0, "unavailable quota is not zero");
            Check(Quota.Read(Json.Parse("{\"primary\":{\"usedPercent\":0}}"), DateTime.UtcNow, "account").Main.Used == 0, "explicit zero usage remains available");
            var multiple = Quota.ReadAccount(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":99}},\"rateLimitsByLimitId\":{\"codex\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":8}},\"other\":{\"limitId\":\"other\",\"secondary\":{\"usedPercent\":12}}}}"), DateTime.UtcNow);
            Check(multiple.Count == 2 && multiple[0].Main.Used == 8, "multi-bucket quota takes precedence over legacy bucket");

            string cursorFile = Path.Combine(folder, "cursor.txt");
            var cursor = new LogCursor(); byte[] prefix = Encoding.UTF8.GetBytes("小龙\n");
            File.WriteAllBytes(cursorFile, prefix.Take(2).ToArray());
            Check(cursor.Read(cursorFile, true).Count == 0, "partial UTF-8 record is buffered");
            using (var stream = new FileStream(cursorFile, FileMode.Append)) stream.Write(prefix, 2, prefix.Length - 2);
            Check(cursor.Read(cursorFile, false).Single() == "小龙", "split UTF-8 character is preserved");
            File.WriteAllText(cursorFile, "x\n", new UTF8Encoding(false));
            Check(cursor.Read(cursorFile, false).Single() == "x", "truncated file restarts safely");
            File.WriteAllText(cursorFile, new string('z', LogCursor.MaxLine + 20) + "\nvalid\n", new UTF8Encoding(false));
            cursor = new LogCursor();
            Check(cursor.Read(cursorFile, false).Single() == "valid", "oversized records are bounded and next line survives");

            string liveFile = Path.Combine(folder, "rollout-00000000-0000-0000-0000-000000000002.jsonl");
            File.WriteAllText(liveFile, Record("{\"type\":\"task_started\",\"turn_id\":\"live\"}") + "\n", new UTF8Encoding(false));
            using (var monitor = new SessionMonitor(folder))
            {
                int alerts = 0; monitor.Completed += delegate { alerts++; }; monitor.Poll();
                Check(alerts == 0 && monitor.Select("").Running, "startup restores active work without alerts");
                string complete = Record("{\"type\":\"task_complete\",\"turn_id\":\"live\"}");
                File.AppendAllText(liveFile, complete.Substring(0, 25));
                System.Threading.Thread.Sleep(150); monitor.Poll();
                Check(alerts == 0, "incomplete completion write does not notify");
                File.AppendAllText(liveFile, complete.Substring(25) + "\n");
                System.Threading.Thread.Sleep(150); monitor.Poll();
                Check(alerts == 1 && !monitor.Select("").Running, "live filesystem completion emits exactly one alert");
                File.AppendAllText(liveFile, complete + "\ninvalid json\n");
                System.Threading.Thread.Sleep(150); monitor.Poll();
                Check(alerts == 1, "duplicate and malformed records do not produce alerts");
                monitor.SetQuota(Quota.Read(Json.Parse("{\"primary\":{\"usedPercent\":40}}"), DateTime.UtcNow, "new"));
                monitor.SetQuota(Quota.Read(Json.Parse("{\"primary\":{\"usedPercent\":2}}"), DateTime.UtcNow.AddHours(-1), "old"));
                Check(monitor.PrimaryQuota.Main.Used == 40, "old quota cannot overwrite fresh data");
            }
            using (var monitor = new SessionMonitor(folder))
            {
                int alerts = 0; monitor.Completed += delegate { alerts++; }; monitor.Poll();
                Check(alerts == 0 && monitor.Select("").Mood == Mood.Done, "restarting after completion does not replay alerts");
            }
            Console.WriteLine(passed + " checks passed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL " + e); return 1; }
        finally
        {
            // The random test directory is the only cleanup target.
            string target = Path.GetFullPath(folder), tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("codex-pet-tests-")) Directory.Delete(target, true);
        }
    }
}
