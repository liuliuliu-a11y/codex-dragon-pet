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
            Console.WriteLine(passed + " bridge checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
