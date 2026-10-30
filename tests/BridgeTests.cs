using System;
using System.IO;
using System.Text;
using CodexDragon;

internal static class BridgeTests
{
    private static int passed;
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static string Record(string payload, DateTime at)
    { return "{\"timestamp\":\"" + at.ToString("o") + "\",\"type\":\"event_msg\",\"payload\":" + payload + "}\n"; }
    public static int Main(string[] args)
    {
        string fixture = Path.Combine(args[0], "bridge-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        string file = Path.Combine(fixture, "rollout-00000000-0000-0000-0000-000000000907.jsonl");
        DateTime now = DateTime.UtcNow; long serial = 0; string completed = "";
        try
        {
            using (var monitor = new SessionMonitor(fixture, () => now))
            {
                monitor.Completed += session => { if (monitor.Select("") == session) { serial++; completed = session.Id; } };
                monitor.Poll();
                Check(!BridgeSnapshot.Read(monitor, "", serial, completed).connected, "no sessions is represented without invented connection");
                now = now.AddSeconds(1);
                File.WriteAllText(file, Record("{\"type\":\"task_started\",\"turn_id\":\"bridge-test\"}", now), new UTF8Encoding(false));
                now = now.AddSeconds(21); monitor.Poll();
                Check(BridgeSnapshot.Read(monitor, "", serial, completed).running, "real work event reaches the bridge");
                File.AppendAllText(file, "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-6.1-sol\",\"effort\":\"ultra\"}}\n", new UTF8Encoding(false));
                now = now.AddSeconds(21); monitor.Poll();
                var context = BridgeSnapshot.Read(monitor, "", serial, completed);
                Check(context.model == "gpt-6.1-sol" && context.reasoningEffort == "ultra", "actual model and effort reach the bridge");
                File.AppendAllText(file, "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-6-astra\",\"reasoning\":{\"effort\":\"high\"}}}\n", new UTF8Encoding(false));
                now = now.AddSeconds(21); monitor.Poll();
                context = BridgeSnapshot.Read(monitor, "", serial, completed);
                Check(context.model == "gpt-6-astra" && context.reasoningEffort == "high", "changing model and nested effort replaces the old context");
                File.AppendAllText(file, "{\"type\":\"turn_context\",\"payload\":{\"model\":\"unknown-model\"}}\n", new UTF8Encoding(false));
                now = now.AddSeconds(21); monitor.Poll();
                Check(BridgeSnapshot.Read(monitor, "", serial, completed).reasoningEffort == "", "missing effort is not inherited from an earlier turn");
                File.AppendAllText(file, Record("{\"type\":\"agent_reasoning\",\"text\":\"SECRET INTERNAL CONTENT\"}", now), new UTF8Encoding(false));
                File.AppendAllText(file, "{\"timestamp\":\"" + now.ToString("o") + "\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"commentary\",\"content\":[{\"type\":\"output_text\",\"text\":\"正在测试接入\"}]}}\n", new UTF8Encoding(false));
                now = now.AddSeconds(21); monitor.Poll();
                string snapshot = Json.Write(BridgeSnapshot.Read(monitor, "", serial, completed));
                Check(snapshot.Contains("正在测试接入") && !snapshot.Contains("SECRET"), "only public progress is exported");
                Check(!snapshot.Contains("FilePath") && !snapshot.Contains(fixture), "source log paths are absent from the bridge contract");
                File.AppendAllText(file, Record("{\"type\":\"task_complete\",\"turn_id\":\"bridge-test\",\"last_agent_message\":\"done\"}", now), new UTF8Encoding(false));
                now = now.AddSeconds(21); monitor.Poll();
                Check(BridgeSnapshot.Read(monitor, "", serial, completed).finishing && serial == 0, "finalization does not prematurely notify");
                now = now.AddSeconds(4); monitor.Poll();
                var finished = BridgeSnapshot.Read(monitor, "", serial, completed);
                Check(!finished.running && finished.completionSerial == 1 && finished.completedId.Length > 0, "confirmed completion produces one notification token");
                now = now.AddSeconds(4); monitor.Poll();
                Check(serial == 1, "polling never duplicates a completion");
                var quota = new Quota { Updated = DateTime.UtcNow };
                quota.Windows.Add(new QuotaWindow { Used = 42, Minutes = 300 }); monitor.SetQuota(quota);
                Check(BridgeSnapshot.Read(monitor, "", serial, completed).quotaUsed == 42, "fresh quota reaches the host");
                quota.Windows[0].Reset = DateTime.UtcNow.AddMinutes(-1);
                Check(BridgeSnapshot.Read(monitor, "", serial, completed).quotaUsed == null, "expired quota is withheld");
            }
            using (var restart = new SessionMonitor(fixture))
            {
                bool replayed = false; restart.Completed += session => replayed = true; restart.Poll();
                Check(!replayed, "restart does not replay historical completion");
            }
            // Context outside the incremental tail must survive an app restart.
            File.AppendAllText(file, "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-6-astra\",\"effort\":\"ultra\"}}\n", new UTF8Encoding(false));
            File.AppendAllText(file, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_reasoning\",\"text\":\"" + new string('x', 3 * 1024 * 1024) + "\"}}\n", new UTF8Encoding(false));
            using (var restart = new SessionMonitor(fixture))
            {
                restart.Poll(); var context = BridgeSnapshot.Read(restart, "", 0, "");
                Check(context.model == "gpt-6-astra" && context.reasoningEffort == "ultra", "restart recovers execution settings before a long turn tail");
                Check(!Json.Write(context).Contains(new string('x', 100)), "context recovery never exports reasoning text");
            }
            Console.WriteLine(passed + " bridge checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
