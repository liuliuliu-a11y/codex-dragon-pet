using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PetMain = VPet_Simulator.Core.Main;

internal static partial class Program
{
    private sealed class ReviewHandler : HttpMessageHandler
    {
        internal string Snapshot = "{\"protocol\":1}";
        internal readonly List<(string method, string path, string body)> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method.Method, path, body));
            string response = Snapshot;
            if (path == "/api/review/notifications/seen")
            {
                using var payload = JsonDocument.Parse(body);
                response = JsonSerializer.Serialize(new { seen = payload.RootElement.GetProperty("ids").EnumerateArray().Select(value => value.GetString()).ToArray() });
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
    private static Dictionary<string, object> ReviewRow(long sequence, bool pending = true, bool seen = false)
    {
        string proposal = sequence.ToString("x32"), digest = new string('a', 64);
        return new Dictionary<string, object> {
            ["id"] = "review:" + sequence, ["event_seq"] = sequence, ["proposal_id"] = proposal, ["version"] = 2, ["digest"] = digest,
            ["kind"] = pending ? "pending" : "result", ["status"] = pending ? "pending" : "succeeded", ["text"] = "隔离验收通知 " + sequence + "：完整内容保留在独立通知卡中。",
            ["at"] = DateTimeOffset.UtcNow.ToString("o"), ["seen"] = seen, ["actionable"] = pending,
            ["href"] = $"/review?proposal={proposal}&version=2&digest={digest}#proposals"
        };
    }
    private static object ReviewObject(Dictionary<string, object> row) => JsonSerializer.Deserialize(JsonSerializer.Serialize(row), Host.GetType("CodexVPet.LifeReviewNotification")!)!;
    private static string ReviewSnapshot(IEnumerable<Dictionary<string, object>> rows, int unread, int pending) => JsonSerializer.Serialize(new { protocol = 1, review_inbox = new { notifications = rows, unread_count = unread, pending_count = pending } });
    private static void ObserveReviews(object window, string json)
    {
        var snapshot = JsonSerializer.Deserialize(json, Host.GetType("CodexVPet.LifeSnapshot")!)!;
        Set(window, "lifeSnapshot", snapshot);
        Call(window, "ObserveLifeReviews", snapshot.GetType().GetProperty("review_inbox")!.GetValue(snapshot)!);
        Call(window, "RenderLifeChat");
    }
    private static string? ReviewUri(Dictionary<string, object> row) => (string?)WindowType.GetMethod("LifeReviewUri", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new[] { ReviewObject(row) });
    private static int ReviewBubbles(object window) => (int)Field(window, "lifeReviewBubbles");
    private static void SaveReviewChat(Window chat, string file)
    {
        chat.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(chat.ActualWidth * 2), (int)Math.Ceiling(chat.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(chat); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
    private static async Task ReviewNotifications(object window, string evidence)
    {
        var settings = Field(window, "settings"); var link = Field(window, "lifeSettings");
        Property(settings, "Quiet", false); Property(link, "Notices", true); Property(link, "LastMessage", "chat-cursor-kept"); Property(link, "LastReviewSequence", 0L);
        var rows = new[] { ReviewRow(4), ReviewRow(3, false), ReviewRow(2) };
        string snapshot = ReviewSnapshot(rows, 3, 2);
        int before = ReviewBubbles(window), gestures = (int)Field(window, "lifeReplyGestures");
        ObserveReviews(window, snapshot);
        Check(ReviewBubbles(window) == before + 1, "three new review events produce one text bubble");
        Check((long)link.GetType().GetProperty("LastReviewSequence")!.GetValue(link)! == 4, "review cursor consumes the maximum stable event sequence");
        Check((string)link.GetType().GetProperty("LastMessage")!.GetValue(link)! == "chat-cursor-kept", "review events never replace the chat cursor");
        Check((int)Field(window, "lifeReplyGestures") == gestures, "review bubbles do not trigger reply animations");
        ObserveReviews(window, snapshot);
        Check(ReviewBubbles(window) == before + 1, "repeated polling never replays the same review events");
        string settingsFile = Path.Combine((string)Field(window, "profile"), "life-link-settings.json");
        link = JsonSerializer.Deserialize(File.ReadAllText(settingsFile), Host.GetType("CodexVPet.LifeLinkSettings")!)!; Set(window, "lifeSettings", link);
        ObserveReviews(window, snapshot);
        Check(ReviewBubbles(window) == before + 1, "persisted review cursor prevents bubbles after a host restart");
        var badge = (Button)Field(window, "lifeReviewBadge");
        Check(badge.Visibility == Visibility.Visible && badge.Content.ToString() == "待审 2 · 未读 3", "independent corner badge shows pending and unread counts");

        var handler = new ReviewHandler { Snapshot = snapshot };
        ((HttpClient)Field(window, "lifeHttp")).Dispose(); Set(window, "lifeHttp", new HttpClient(handler));
        Call(window, "OpenLifeChat");
        var chat = (Window)Field(window, "lifeChat");
        try
        {
            await Wait(() => !(bool)Field(window, "lifePolling"), "isolated review snapshot renders");
            var cards = (StackPanel)Field(window, "lifeReviews");
            Check(cards.Children.OfType<Border>().Count() == 3, "chat retains a separate complete card for every returned notification");
            var buttons = cards.Children.OfType<Border>().Select(card => ((StackPanel)card.Child).Children.OfType<Button>().Single()).ToList();
            Check(buttons.Count(button => button.Content.ToString() == "查看提案 v2") == 2 && buttons.Count(button => button.Content.ToString() == "查看结果 v2") == 1, "review and result cards expose explicit versioned view buttons");
            Check(handler.Requests.All(request => request.path == "/api/pet"), "showing cards does not mark notifications read or submit approvals");
            SaveReviewChat(chat, Path.Combine(evidence, "review-chat.png")); Call(window, "SaveEvidence", "review-badge");
            var inbox = Field(window, "lifeSnapshot").GetType().GetProperty("review_inbox")!.GetValue(Field(window, "lifeSnapshot"))!;
            var item = ((IList)inbox.GetType().GetProperty("notifications")!.GetValue(inbox)!)[0]!;
            await (Task)Call(window, "MarkLifeReviewSeen", item)!;
            var receipt = handler.Requests.Last();
            using var receiptBody = JsonDocument.Parse(receipt.body);
            Check(receipt.method == "POST" && receipt.path == "/api/review/notifications/seen" && receiptBody.RootElement.EnumerateObject().Count() == 1 && receiptBody.RootElement.GetProperty("ids")[0].GetString() == "review:4", "view receipt sends only the stable notification ID to the seen endpoint");
            Check(badge.Content.ToString() == "待审 2 · 未读 2", "reading a pending proposal reduces unread but preserves pending count");
            int requests = handler.Requests.Count; await (Task)Call(window, "MarkLifeReviewSeen", item)!;
            Check(handler.Requests.Count == requests, "already read cards do not resend a seen receipt");

            var valid = ReviewRow(10);
            Check(ReviewUri(valid) == "http://127.0.0.1:8766" + (string)valid["href"], "review links are constructed from the fixed loopback origin and validated identity");
            foreach (var bad in new Dictionary<string, object>[] {
                new(valid) { ["href"] = "https://example.com/review" }, new(valid) { ["href"] = "http://127.0.0.1:8766/review" },
                new(valid) { ["proposal_id"] = "../arbitrary" }, new(valid) { ["proposal_id"] = new string('A', 32) },
                new(valid) { ["digest"] = new string('g', 64) }, new(valid) { ["version"] = 0 },
                new(valid) { ["id"] = "review:11" }, new(valid) { ["kind"] = "approve" }
            }) Check(ReviewUri(bad) == null, "malformed or arbitrary review link is rejected: " + JsonSerializer.Serialize(bad.Where(pair => !Equals(pair.Value, valid[pair.Key])).ToDictionary(pair => pair.Key, pair => pair.Value)));
            await (Task)Call(window, "MarkLifeReviewSeen", ReviewObject(new(valid) { ["href"] = "https://example.com" }))!;
            Check(handler.Requests.Count == requests, "invalid links cannot send seen receipts");

            before = ReviewBubbles(window); Property(settings, "Quiet", true);
            ObserveReviews(window, ReviewSnapshot(new[] { ReviewRow(11) }, 4, 3));
            Check(ReviewBubbles(window) == before && badge.Visibility == Visibility.Visible && badge.Content.ToString() == "待审 3 · 未读 4", "quiet mode suppresses review bubbles while keeping all backend counts visible");
            Property(settings, "Quiet", false); Property(link, "Notices", false);
            ObserveReviews(window, ReviewSnapshot(new[] { ReviewRow(12, false) }, 5, 3));
            Check(ReviewBubbles(window) == before && badge.Visibility == Visibility.Visible, "disabled proactive notices preserve the unread marker"); Property(link, "Notices", true);
            Call(window, "Sleep"); await Wait(() => ((PetMain)Field(window, "pet")).State == PetMain.WorkingState.Sleep, "review test pet sleeps");
            ObserveReviews(window, ReviewSnapshot(new[] { ReviewRow(13) }, 6, 4));
            Check(ReviewBubbles(window) == before && ((PetMain)Field(window, "pet")).State == PetMain.WorkingState.Sleep && badge.Visibility == Visibility.Visible, "review notifications never wake a sleeping pet and retain its badge");
            Call(window, "Wake", new object?[] { null }!); await Wait(() => Idle(window), "review test wake completes");
            Set(window, "pointerPaused", true); ObserveReviews(window, ReviewSnapshot(new[] { ReviewRow(14) }, 7, 5)); Set(window, "pointerPaused", false);
            Set(window, "transitionActive", true); ObserveReviews(window, ReviewSnapshot(new[] { ReviewRow(15) }, 8, 6)); Set(window, "transitionActive", false);
            Check(ReviewBubbles(window) == before, "review bubbles do not interrupt pointer or manual action transitions");
            ObserveReviews(window, ReviewSnapshot(new[] { ReviewRow(15, true, true) }, 0, 6));
            Check(badge.Visibility == Visibility.Visible && badge.Content.ToString() == "待审 6 · 未读 0", "pending marker remains visible after every displayed notification is read");
            ObserveReviews(window, "{\"protocol\":1,\"messages\":[]}");
            Check(badge.Visibility == Visibility.Collapsed && ((ScrollViewer)Field(window, "lifeReviewScroll")).Visibility == Visibility.Collapsed, "old protocol 1 snapshots without review_inbox remain compatible");
            Check((long)link.GetType().GetProperty("LastReviewSequence")!.GetValue(link)! == 15, "missing review_inbox does not reset the durable review cursor");
            ObserveReviews(window, ReviewSnapshot(Enumerable.Range(81, 20).Reverse().Select(sequence => ReviewRow(sequence, false)), 26, 3));
            Check(ReviewBubbles(window) == before + 1 && cards.Children.OfType<Border>().Count() == 20, "twenty simultaneous results retain all cards and use one summary bubble");
            Check(badge.Content.ToString() == "待审 3 · 未读 26", "badge uses total inbox counts even when the returned card window is limited");
            Check((int)Field(window, "lifeReplyGestures") == gestures, "all review scenarios preserve the existing reply animation count");
            Check(handler.Requests.All(request => request.path is "/api/pet" or "/api/review/notifications/seen"), "native notifications never call proposal approval or execution APIs");
        }
        finally { chat.Close(); }
    }
}
