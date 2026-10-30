using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PetMain = VPet_Simulator.Core.Main;

internal static partial class Program
{
    private static readonly Assembly Host = Assembly.Load("CodexVPet");
    private static readonly Type WindowType = Host.GetType("CodexVPet.CompanionWindow")!;
    private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private static int passed;
    private static object Field(object obj, string key) => obj.GetType().GetField(key, Flags)!.GetValue(obj)!;
    private static void Set(object obj, string key, object value) => obj.GetType().GetField(key, Flags)!.SetValue(obj, value);
    private static object? Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Flags)!.Invoke(obj, args);
    private static void Property(object obj, string key, object value) => obj.GetType().GetProperty(key)!.SetValue(obj, value);
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
    private static async Task Wait(Func<bool> predicate, string label)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate()) { if (clock.Elapsed.TotalSeconds > 15) throw new Exception("Timed out: " + label); await Task.Delay(70); }
    }
    private static object Message(string emotion)
    {
        var type = Host.GetType("CodexVPet.LifeMessage")!;
        return JsonSerializer.Deserialize("{\"role\":\"assistant\",\"kind\":\"chat\",\"text\":\"隔离验收的一句回复。\",\"emotion\":\"" + emotion + "\"}", type)!;
    }
    private static bool Observed(object pet, string key)
    {
        var dictionary = Field(pet, "observedGraphs");
        return (bool)dictionary.GetType().GetMethod("ContainsKey")!.Invoke(dictionary, new object[] { key })!;
    }
    private static bool Idle(object window) => ((PetMain)Field(window, "pet")).DisplayType.Type.ToString() == "Default" && !(bool)Field(window, "transitionActive");
    [STAThread]
    public static int Main(string[] args)
    {
        string evidence = Path.GetFullPath(args[0]); Directory.CreateDirectory(evidence);
        string profile = Path.Combine(evidence, "profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(profile);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        foreach (string name in new[] { "Theme.xaml", "basestyle.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/VPet-Simulator.Core;component/Display/" + name) });
        var pet = (Window)Activator.CreateInstance(WindowType, Flags, null, new object[] { profile, true, evidence }, null)!;
        int result = 0;
        pet.Loaded += async (_, _) =>
        {
            Window? chat = null;
            try
            {
                await Wait(() => (bool)Field(pet, "ready"), "native engine ready");
                ((DispatcherTimer)Field(pet, "demoTimer")).Stop();
                await Wait(() => Observed(pet, "Default:idle:Single"), "initial idle frame");
                Check(((DispatcherTimer)Field(pet, "lifeTimer")).IsEnabled == false, "isolated demo never polls the real companion");
                Call(pet, "StartLifeService"); var nextStart = (DateTime)Field(pet, "lifeNextStart");
                Check(nextStart > DateTime.UtcNow, "offline service attempts use a bounded retry delay");
                Call(pet, "StartLifeService"); Check((DateTime)Field(pet, "lifeNextStart") == nextStart, "repeated polls do not spawn repeated startup attempts");
                var settings = Field(pet, "settings"); Property(settings, "Quiet", false);
                chat = new Window { Title = "隔离动作验收", Width = 220, Height = 100 }; chat.Show(); Set(pet, "lifeChat", chat);
                Call(pet, "ShowLifeReply", Message("happy"));
                Call(pet, "SaveEvidence", "happy-start");
                await Wait(() => Observed(pet, "Common:celebrate:Single"), "happy gesture");
                Check((int)Field(pet, "lifeReplyGestures") == 1, "happy reply plays one greeting animation");
                await Wait(() => Idle(pet), "happy gesture finishes");
                Call(pet, "ShowLifeReply", Message("curious"));
                await Wait(() => Observed(pet, "Touch_Body:curious:C_End"), "curious sequence");
                Check(Observed(pet, "Touch_Body:curious:A_Start") && Observed(pet, "Touch_Body:curious:B_Loop"), "curious reply completes start loop and end");
                await Wait(() => Idle(pet), "curious gesture finishes");
                int before = (int)Field(pet, "lifeReplyGestures");
                Property(settings, "Quiet", true); Call(pet, "ShowLifeReply", Message("happy"));
                Check((int)Field(pet, "lifeReplyGestures") == before, "quiet mode preserves text without reply dancing");
                Property(settings, "Quiet", false);
                var link = Field(pet, "lifeSettings"); Property(link, "ReduceMotion", true); Call(pet, "ShowLifeReply", Message("happy"));
                Check((int)Field(pet, "lifeReplyGestures") == before, "reduced motion suppresses reply gestures");
                Property(link, "ReduceMotion", false); Call(pet, "ShowLifeReply", Message("unknown"));
                Check((int)Field(pet, "lifeReplyGestures") == before, "unknown emotions cannot select an executable action");
                Set(pet, "pointerPaused", true); Call(pet, "ShowLifeReply", Message("happy"));
                Check((int)Field(pet, "lifeReplyGestures") == before, "pointer interaction is never interrupted by a reply"); Set(pet, "pointerPaused", false);
                Call(pet, "Sleep"); await Wait(() => ((PetMain)Field(pet, "pet")).State == PetMain.WorkingState.Sleep, "sleep begins"); Call(pet, "ShowLifeReply", Message("happy"));
                Check((int)Field(pet, "lifeReplyGestures") == before, "reply does not wake a resting pet");
                Check(((PetMain)Field(pet, "pet")).State == PetMain.WorkingState.Sleep, "reply text preserves the native sleeping state");
                Call(pet, "Wake", new object?[] { null }!); await Wait(() => Idle(pet), "wake finishes");
                Property(link, "LastInteraction", "-");
                using var command = JsonDocument.Parse("{\"id\":\"web-1\",\"origin\":\"web\",\"at\":\"" + DateTimeOffset.UtcNow.ToString("o") + "\",\"action\":\"feed\"}");
                Call(pet, "ObserveLifeInteraction", command.RootElement);
                await Wait(() => Observed(pet, "Common:dragon-eat:Single"), "web feeding");
                Check((string)link.GetType().GetProperty("LastInteraction")!.GetValue(link)! == "web-1", "web interaction is consumed once and cursor persists");
                int generation = (int)Field(pet, "interactionGeneration"); Call(pet, "ObserveLifeInteraction", command.RootElement);
                Check((int)Field(pet, "interactionGeneration") == generation, "same web interaction is not replayed");
                await Wait(() => Idle(pet), "feeding finishes before review notices");
                chat.Close(); Set(pet, "lifeChat", null!);
                await ReviewNotifications(pet, evidence);
                Call(pet, "SaveEvidence", "accepted");
            }
            catch (Exception ex) { result = 1; Console.WriteLine(ex); Call(pet, "SaveEvidence", "failed"); }
            finally { chat?.Close(); pet.Close(); }
        };
        app.Run(pet);
        File.WriteAllText(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { passed, failed = result }));
        Console.WriteLine($"{passed} native companion checks passed; result {result}."); return result;
    }
}
