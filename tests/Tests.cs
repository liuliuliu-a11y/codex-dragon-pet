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

    private static void CheckCompletionHandoff(string folder)
    {
        string root = Path.Combine(folder, "handoff"); Directory.CreateDirectory(root);
        string file = Path.Combine(root, "handoff.jsonl");
        File.WriteAllText(file, Record("{\"type\":\"task_started\",\"turn_id\":\"first\"}") + "\n");
        DateTime now = DateTime.UtcNow;
        using (var monitor = new SessionMonitor(root, delegate { return now; }))
        {
            int alerts = 0; monitor.Completed += delegate { alerts++; }; monitor.Poll();
            File.AppendAllText(file,
                Record("{\"type\":\"task_complete\",\"turn_id\":\"first\",\"last_agent_message\":\"First result\"}") + "\n" +
                Record("{\"type\":\"task_started\",\"turn_id\":\"queued\"}") + "\n");
            System.Threading.Thread.Sleep(150); monitor.Poll();
            Check(alerts == 0 && monitor.Select("").Running, "queued work in the same log batch cannot emit a premature completion");
            now = now.AddSeconds(4); monitor.Poll();
            Check(alerts == 0 && !monitor.Select("").Finishing, "superseded completion remains cancelled after the quiet interval");
        }
    }

    private static void CheckCompletionStability(string folder)
    {
        string root = Path.Combine(folder, "stability"); Directory.CreateDirectory(root);
        string file = Path.Combine(root, "stability.jsonl");
        File.WriteAllText(file, Record("{\"type\":\"task_started\",\"turn_id\":\"output\"}") + "\n");
        DateTime now = DateTime.UtcNow;
        using (var monitor = new SessionMonitor(root, delegate { return now; }))
        {
            int alerts = 0; monitor.Completed += delegate { alerts++; }; monitor.Poll();
            Action<string> append = delegate(string records) { File.AppendAllText(file, records); System.Threading.Thread.Sleep(100); monitor.Poll(); };
            Action<double> advance = delegate(double seconds) { now = now.AddSeconds(seconds); monitor.Poll(); };
            string final = "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"final_answer\",\"content\":[{\"type\":\"output_text\",\"text\":\"Result\"}]}}\n";
            append(final);
            Check(monitor.Select("").Running && monitor.Select("").Stage == "正在输出结果" && alerts == 0, "final output remains working before the terminal event");
            append(Record("{\"type\":\"task_complete\",\"turn_id\":\"output\"}") + "\n");
            Check(monitor.Select("").Finishing && monitor.Select("").Running && alerts == 0, "terminal event first enters finalization without celebrating");
            advance(2);
            append(Record("{\"type\":\"token_count\"}") + "\n");
            advance(2.9);
            Check(alerts == 0 && monitor.Select("").Finishing, "later log writes restart the completion quiet interval");
            advance(0.2);
            Check(alerts == 1 && !monitor.Select("").Running && monitor.Select("").Mood == Mood.Done, "final output plus three quiet seconds confirms completion once");
            append(Record("{\"type\":\"task_complete\",\"turn_id\":\"output\"}") + "\n"); advance(4);
            Check(alerts == 1, "duplicate terminal events cannot replay a confirmed celebration");

            append(Record("{\"type\":\"task_started\",\"turn_id\":\"before-partial\"}") + "\n" +
                Record("{\"type\":\"task_complete\",\"turn_id\":\"before-partial\",\"last_agent_message\":\"Result\"}") + "\n");
            string next = Record("{\"type\":\"task_started\",\"turn_id\":\"after-partial\"}") + "\n";
            append(next.Substring(0, 25)); advance(5);
            Check(alerts == 1 && monitor.Select("").Finishing, "a partial next record blocks completion even beyond the quiet interval");
            append(next.Substring(25)); advance(5);
            Check(alerts == 1 && monitor.Select("").Running && !monitor.Select("").Finishing, "finishing a partial queued start cancels the pending celebration");

            append(Record("{\"type\":\"task_complete\",\"turn_id\":\"after-partial\",\"last_agent_message\":\"Result\"}") + "\n");
            advance(1);
            append(Record("{\"type\":\"turn_aborted\"}") + "\n"); advance(5);
            Check(alerts == 1 && monitor.Select("").Mood == Mood.Stopped, "an interruption during finalization cancels the completion reminder");

            append(Record("{\"type\":\"task_started\",\"turn_id\":\"resumed-tool\"}") + "\n" +
                Record("{\"type\":\"task_complete\",\"turn_id\":\"resumed-tool\",\"last_agent_message\":\"Result\"}") + "\n");
            append("{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"exec_command\"}}\n"); advance(5);
            Check(alerts == 1 && monitor.Select("").Running && !monitor.Select("").Finishing, "continued tool work cancels a pending terminal event");
            append(Record("{\"type\":\"task_complete\",\"turn_id\":\"resumed-tool\",\"last_agent_message\":\"Final tool result\"}") + "\n"); advance(3.1);
            Check(alerts == 2 && !monitor.Select("").Running, "continued work can finish with a later terminal event in the same turn");

            append(Record("{\"type\":\"task_started\",\"turn_id\":\"late-output\"}") + "\n" +
                Record("{\"type\":\"task_complete\",\"turn_id\":\"late-output\"}") + "\n"); advance(5);
            Check(alerts == 2 && monitor.Select("").Finishing, "a terminal event without final output cannot announce ready");
            append(final); advance(3.1);
            Check(alerts == 3 && !monitor.Select("").Running, "final output arriving after the terminal event can complete after settling");

            append(Record("{\"type\":\"task_started\",\"turn_id\":\"resumed-commentary\"}") + "\n" +
                Record("{\"type\":\"task_complete\",\"turn_id\":\"resumed-commentary\",\"last_agent_message\":\"Result\"}") + "\n");
            append("{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"commentary\",\"content\":[{\"type\":\"output_text\",\"text\":\"Still working\"}]}}\n"); advance(5);
            Check(alerts == 3 && monitor.Select("").Running && !monitor.Select("").Finishing, "continued public progress cancels pending completion");
        }
    }

    private static void CheckActions()
    {
        var playback = new ActionPlayback();
        Check(playback.Frame == -1 && playback.Current == PetAction.None, "no action owns the state sprite initially");
        Check(playback.Start(PetAction.Tea), "tea interaction starts");
        playback.Advance(9999);
        Check(playback.Current == PetAction.Tea && playback.Frame == -1, "tea uses a ten-second continuous rig without atlas frames");
        playback.Advance(1);
        Check(playback.Elapsed == 0 && playback.Frame == -1, "tea releases the state sprite at its end");
        playback.Start(PetAction.Tea); playback.Advance(1700);
        Check(!playback.Start(PetAction.Blink) && playback.Current == PetAction.Tea, "automatic idle cannot interrupt drinking");
        Check(playback.Start(PetAction.Celebrate) && playback.Frame >= 20, "task completion interrupts tea with celebration");
        Check(!playback.Start(PetAction.Tea), "manual tea does not hide a task completion celebration");
        Check(playback.Start(PetAction.Drag) && playback.CelebrationPending, "drag responds immediately and retains interrupted celebration");
        playback.Advance(60000);
        Check(playback.Current == PetAction.Drag && playback.Frame >= 16 && playback.Frame <= 17, "drag keeps looping after a long hold");
        Check(!playback.Start(PetAction.Celebrate) && playback.CelebrationPending, "completion during dragging queues one celebration");
        Check(!playback.Start(PetAction.Tea), "tea cannot override a live drag");
        playback.Start(PetAction.Land); playback.Advance(500);
        Check(playback.Current == PetAction.Celebrate && !playback.CelebrationPending, "landing completes before a queued celebration begins");
        playback.Advance(10000);
        Check(playback.Current == PetAction.None, "dispatcher delay cannot leave a celebration stuck");
        playback.Start(PetAction.Drag); playback.Start(PetAction.Celebrate); playback.Cancel();
        playback.Start(PetAction.Land); playback.Advance(1000);
        Check(playback.Current == PetAction.None, "cancel clears a stale queued completion before the next task");
        playback.Start(PetAction.Blink); playback.Advance(500);
        Check(playback.Current == PetAction.None, "blink finishes without leaving the eyes closed");
        playback.Start(PetAction.Stretch); playback.Advance(3000);
        Check(playback.Current == PetAction.None, "stretch and yawn release their artwork after playback");
        var oldSettings = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>("{\"Sound\":false,\"Quiet\":true,\"Scale\":1.25,\"PinnedThread\":\"existing-thread\"}");
        Check(oldSettings.DailyActions && !oldSettings.Sound && oldSettings.Quiet && oldSettings.Scale == 1.25 && oldSettings.PinnedThread == "existing-thread", "old settings retain preferences and receive the daily-action default");
    }

    public static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "codex-pet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            CheckCompletionHandoff(folder);
            CheckCompletionStability(folder);
            CheckActions();
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
            Check(session.Finishing && session.Running && session.ConfirmCompletion() && !session.Running, "live completion requires separate finalization confirmation");
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

            string liveRoot = Path.Combine(folder, "live"); Directory.CreateDirectory(liveRoot);
            string liveFile = Path.Combine(liveRoot, "rollout-00000000-0000-0000-0000-000000000002.jsonl");
            File.WriteAllText(liveFile, Record("{\"type\":\"task_started\",\"turn_id\":\"live\"}") + "\n", new UTF8Encoding(false));
            DateTime liveNow = DateTime.UtcNow;
            using (var monitor = new SessionMonitor(liveRoot, delegate { return liveNow; }))
            {
                int alerts = 0; monitor.Completed += delegate { alerts++; }; monitor.Poll();
                Check(alerts == 0 && monitor.Select("").Running, "startup restores active work without alerts");
                string complete = Record("{\"type\":\"task_complete\",\"turn_id\":\"live\",\"last_agent_message\":\"Result\"}");
                File.AppendAllText(liveFile, complete.Substring(0, 25));
                System.Threading.Thread.Sleep(150); monitor.Poll();
                Check(alerts == 0, "incomplete completion write does not notify");
                File.AppendAllText(liveFile, complete.Substring(25) + "\n");
                System.Threading.Thread.Sleep(150); monitor.Poll();
                Check(alerts == 0 && monitor.Select("").Finishing, "complete filesystem record still waits for finalization");
                liveNow = liveNow.AddSeconds(3.1); monitor.Poll();
                Check(alerts == 1 && !monitor.Select("").Running, "live filesystem completion emits exactly one alert");
                File.AppendAllText(liveFile, complete + "\ninvalid json\n");
                System.Threading.Thread.Sleep(150); monitor.Poll();
                Check(alerts == 1, "duplicate and malformed records do not produce alerts");
                monitor.SetQuota(Quota.Read(Json.Parse("{\"primary\":{\"usedPercent\":40}}"), DateTime.UtcNow, "new"));
                monitor.SetQuota(Quota.Read(Json.Parse("{\"primary\":{\"usedPercent\":2}}"), DateTime.UtcNow.AddHours(-1), "old"));
                Check(monitor.PrimaryQuota.Main.Used == 40, "old quota cannot overwrite fresh data");
            }
            using (var monitor = new SessionMonitor(liveRoot))
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
