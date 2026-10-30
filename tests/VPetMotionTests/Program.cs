using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PetMain = VPet_Simulator.Core.Main;

internal static class Program
{
    private static readonly Assembly Host = Assembly.Load("CodexVPet");
    private static readonly Type WindowType = Host.GetType("CodexVPet.CompanionWindow")!;
    private static readonly Type PlayerType = Host.GetType("CodexVPet.SmoothSpritePlayer")!;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static int passed;
    private static object Field(object obj, string name) => obj.GetType().GetField(name, Flags)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Flags)!.Invoke(obj, args);
    private static T Property<T>(object obj, string name) => (T)obj.GetType().GetProperty(name, Flags)!.GetValue(obj)!;
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static async Task Wait(Func<bool> condition, string label)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { if (clock.Elapsed.TotalSeconds > 12) throw new Exception("Timeout: " + label); await Task.Delay(30); }
    }
    private static void Capture(Window window, string path)
    {
        var scene = (Grid)Field(window, "scene"); scene.UpdateLayout();
        var image = new RenderTargetBitmap(360, 412, 192, 192, PixelFormats.Pbgra32); image.Render(scene);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); png.Save(file);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        string evidence = Path.GetFullPath(args[0]); Directory.CreateDirectory(evidence);
        string profile = Path.Combine(evidence, "profile"); Directory.CreateDirectory(profile);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        foreach (string name in new[] { "Theme.xaml", "basestyle.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/VPet-Simulator.Core;component/Display/" + name) });
        var window = (Window)Activator.CreateInstance(WindowType, Flags, null, new object[] { profile, true, evidence }, null)!;
        int result = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Wait(() => (bool)Field(window, "ready"), "native ready");
                ((DispatcherTimer)Field(window, "demoTimer")).Stop();
                object player = Field(window, "smoothSprites") ?? throw new Exception("Smooth player was not loaded");
                Check(Property<int>(player, "ReplacedGraphs") == 33, "all 33 native action segments use the smooth player");
                var resolve = PlayerType.GetMethod("FormFor", BindingFlags.Static | BindingFlags.NonPublic)!;
                var inputs = new[] { ("gpt-6.1-sol", "low", "low"), ("gpt-6.1-sol", "medium", "medium"), ("gpt-6.1-sol", "high", "high"), ("gpt-6.1-sol", "xhigh", "xhigh"), ("gpt-6.1-sol", "ultra", "ultra"), ("gpt-6-astra", "ultra", "astra"), ("gpt-6-astra", "high", "high"), ("unknown", "", "low") };
                foreach (var item in inputs)
                    Check((string)resolve.Invoke(null, new object[] { item.Item1, item.Item2 })! == item.Item3, "model/effort mapping " + item.Item1 + "/" + item.Item2);
                // The long idle hold must not defer a manual action by seconds.
                Call(window, "Wake", new object?[] { null });
                await Task.Delay(350);
                var latency = Stopwatch.StartNew(); Call(window, "TouchHead");
                await Wait(() => ((PetMain)Field(window, "pet")).DisplayType.Type.ToString() == "Touch_Head", "headpat interrupts idle");
                Check(latency.ElapsedMilliseconds < 250, "manual action interrupts a multi-second idle hold within 250ms");
                await Task.Delay(1000); Call(window, "Wake", new object?[] { null }); await Task.Delay(500);
                long before = Property<long>(player, "FramesChanged");
                Call(window, "Feed", true); await Task.Delay(1250);
                long changed = Property<long>(player, "FramesChanged") - before;
                Check(changed >= 20, "tea changes at least 20 authored/intermediate frames in 1.25 seconds");
                Capture(window, Path.Combine(evidence, "tea-midpoint.png"));
                await Task.Delay(1800);
                Check(((PetMain)Field(window, "pet")).DisplayType.Type.ToString() == "Default", "tea callback returns to idle once");
                Call(window, "SetWorking", true); await Task.Delay(600);
                long workBefore = Property<long>(player, "FramesChanged"); await Task.Delay(1100);
                Check(Property<long>(player, "FramesChanged") - workBefore >= 20, "held laptop work pose has continuous hand and hair motion");
                Call(window, "SetWorking", false); await Task.Delay(500);
                Call(window, "Sleep"); await Task.Delay(1150);
                Call(window, "Feed", false); await Task.Delay(2600);
                Check(((PetMain)Field(window, "pet")).State != PetMain.WorkingState.Sleep, "feeding wakes and interrupts sleep safely");
                // Test six real decoded forms, including repeated rapid switches.
                foreach (var item in inputs.Take(6))
                {
                    Call(player, "SetContext", item.Item1, item.Item2); await Task.Delay(600);
                    Check(Property<string>(player, "Form") == item.Item3, "loaded form " + item.Item3);
                    Capture(window, Path.Combine(evidence, "form-" + item.Item3 + ".png"));
                }
                Call(player, "SetContext", "gpt-6.1-sol", "low");
                Call(player, "SetContext", "gpt-6-astra", "ultra");
                Call(window, "TouchHead"); await Task.Delay(900);
                Check(Property<string>(player, "Form") == "astra", "rapid model changes cannot restore stale form decoding");
                Check(Property<string>(player, "LastError").Length == 0, "all decoded animations finish without renderer errors");
                // Record actual WPF window rendering at a 30 Hz target for visual QA.
                string frames = Path.Combine(evidence, "preview-frames"); Directory.CreateDirectory(frames);
                var clock = Stopwatch.StartNew(); int nextAction = 0, count = 0;
                var actions = new (double At, Action Run)[] {
                    (0, () => { Call(player, "SetContext", "gpt-6.1-sol", "low"); Call(window, "Wake", new object?[] { null }); }),
                    (1.2, () => Call(window, "TouchHead")),
                    (2.6, () => Call(player, "SetContext", "gpt-6.1-sol", "medium")),
                    (3.5, () => Call(window, "TouchBody")),
                    (5, () => Call(player, "SetContext", "gpt-6.1-sol", "high")),
                    (6, () => Call(window, "Feed", true)),
                    (9, () => Call(player, "SetContext", "gpt-6.1-sol", "xhigh")),
                    (10, () => Call(window, "Feed", false)),
                    (12, () => Call(player, "SetContext", "gpt-6.1-sol", "ultra")),
                    (13, () => Call(window, "Sleep")),
                    (15, () => Call(window, "Wake", new object?[] { null })),
                    (17, () => Call(player, "SetContext", "gpt-6-astra", "ultra")),
                    (18, () => Call(window, "CelebrateCompletion")),
                    (20, () => Call(window, "Walk")),
                    (22, () => Call(window, "SetWorking", true))
                };
                while (clock.Elapsed.TotalSeconds < 25)
                {
                    while (nextAction < actions.Length && clock.Elapsed.TotalSeconds >= actions[nextAction].At) actions[nextAction++].Run();
                    Capture(window, Path.Combine(frames, (count++).ToString("D5") + ".png"));
                    await Task.Delay(Math.Max(1, (int)((count / 30d - clock.Elapsed.TotalSeconds)*1000)));
                }
                Call(window, "SaveEvidence", "motion-accepted");
                File.WriteAllText(Path.Combine(evidence, "timing.json"), JsonSerializer.Serialize(new { frames = count, duration = clock.Elapsed.TotalSeconds, tickCount = Property<long>(player, "RenderTicks"), maximumGapMs = Property<double>(player, "MaximumTickGap") }));
            }
            catch (Exception ex) { Console.WriteLine(ex); result = 1; Call(window, "SaveEvidence", "motion-failed"); }
            finally { window.Close(); }
        };
        app.Run(window);
        File.WriteAllText(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { passed, failed = result }));
        Console.WriteLine($"{passed} motion checks passed; result {result}."); return result;
    }
}
