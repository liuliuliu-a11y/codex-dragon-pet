using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Core;

namespace CodexVPet;

// Plays precomputed motion-compensated clips against elapsed time. No native
// Thread.Sleep(frameDuration), bitmap decoding or optical flow on the UI thread.
internal sealed class SmoothSpritePlayer : IDisposable
{
    private sealed class Track
    {
        public string[] frames { get; set; } = Array.Empty<string>();
        public double[] milliseconds { get; set; } = Array.Empty<double>();
        public bool flip { get; set; }
    }
    private sealed record Clip(BitmapSource[] Frames, double[] Ends, bool Flip)
    {
        internal double Duration => Ends[^1];
        internal int FrameAt(double elapsed)
        {
            int index = Array.BinarySearch(Ends, elapsed);
            return Math.Clamp(index >= 0 ? index + 1 : ~index, 0, Frames.Length - 1);
        }
    }
    private readonly string root;
    private readonly Dispatcher dispatcher;
    private readonly Func<bool> reduced, visible;
    private readonly Dictionary<string, Dictionary<string, Track>> tracks = new();
    private readonly Dictionary<string, Task<Clip>> cache = new();
    private readonly Queue<string> cacheOrder = new();
    private readonly Image image = new() { Width = 500, Height = 500, Stretch = Stretch.Fill };
    private readonly Image outgoing = new() { Width = 500, Height = 500, Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Grid surface = new() { Width = 500, Height = 500, Background = Brushes.Transparent };
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ScaleTransform scale = new(1, 1, 250, 470);
    private readonly TranslateTransform sway = new();
    private SpriteGraph? active;
    private Clip? clip;
    private int revision, frameIndex = -1;
    private double startedAt, transitionAt, fadeDuration, lastTick;
    private bool disposed;
    internal string Form { get; private set; } = "low";
    internal string LastError { get; private set; } = "";
    internal long RenderTicks { get; private set; }
    internal long FramesChanged { get; private set; }
    internal double MaximumTickGap { get; private set; }
    internal int ReplacedGraphs { get; private set; }

    internal SmoothSpritePlayer(string folder, Dispatcher ui, Func<bool> reduceMotion, Func<bool> isVisible)
    {
        root = folder; dispatcher = ui; reduced = reduceMotion; visible = isVisible;
        foreach (string name in new[] { "low", "medium", "high", "xhigh", "ultra", "astra" })
        {
            string file = Path.Combine(root, name, "clips.json");
            if (File.Exists(file)) tracks[name] = new Dictionary<string, Track>(
                JsonSerializer.Deserialize<Dictionary<string, Track>>(File.ReadAllText(file))!, StringComparer.OrdinalIgnoreCase);
        }
        var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(sway);
        surface.RenderTransform = transform; surface.Children.Add(outgoing); surface.Children.Add(image);
        RenderOptions.SetBitmapScalingMode(surface, BitmapScalingMode.HighQuality);
        timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(1000d / 60) };
        timer.Tick += (_, _) => Tick();
    }
    internal static string FormFor(string model, string effort)
    {
        string level = (effort ?? "").Trim().ToLowerInvariant();
        if (level is "ultra" or "max")
            return (model ?? "").IndexOf("astra", StringComparison.OrdinalIgnoreCase) >= 0 ? "astra" : "ultra";
        return level switch { "medium" => "medium", "high" => "high", "xhigh" => "xhigh", _ => "low" };
    }
    internal void Install(GraphCore graph)
    {
        if (!tracks.TryGetValue("low", out var available)) return;
        var replacements = new Dictionary<IGraph, IGraph>();
        foreach (IGraph original in graph.GraphsALL.ToArray())
        {
            string key = Key(original.GraphInfo);
            if (!available.ContainsKey(key)) continue;
            replacements[original] = new SpriteGraph(this, original.GraphInfo, original.IsLoop);
        }
        for (int i = 0; i < graph.GraphsALL.Count; i++)
            if (replacements.TryGetValue(graph.GraphsALL[i], out var next)) graph.GraphsALL[i] = next;
        foreach (var animations in graph.GraphsList.Values)
            foreach (var list in animations.Values)
                for (int i = 0; i < list.Count; i++)
                    if (replacements.TryGetValue(list[i], out var next)) list[i] = next;
        foreach (var original in replacements.Keys) original.Dispose();
        ReplacedGraphs = replacements.Count;
    }
    internal void SetContext(string model, string effort)
    {
        string form = FormFor(model, effort);
        if (Form == form || !tracks.ContainsKey(form)) return;
        Form = form;
        if (active != null) _ = LoadAndShow(active, true);
    }
    private static string Key(GraphInfo info) => info.Type + ":" + info.Name + ":" + info.Animat;
    private Task<Clip> Load(string form, string key)
    {
        string cacheKey = form + ":" + key;
        if (cache.TryGetValue(cacheKey, out var existing)) return existing;
        Track data = tracks[form][key];
        var task = Task.Run(() =>
        {
            var frames = new BitmapSource[data.frames.Length]; var ends = new double[frames.Length]; double end = 0;
            if (frames.Length == 0 || frames.Length != data.milliseconds.Length) throw new InvalidDataException("Invalid animation timeline");
            for (int i = 0; i < frames.Length; i++)
            {
                if (data.milliseconds[i] <= 0 || !double.IsFinite(data.milliseconds[i])) throw new InvalidDataException("Invalid frame duration");
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(Path.Combine(root, form, data.frames[i])); bitmap.EndInit(); bitmap.Freeze();
                frames[i] = bitmap; ends[i] = end += data.milliseconds[i];
            }
            return new Clip(frames, ends, data.flip);
        });
        cache[cacheKey] = task; cacheOrder.Enqueue(cacheKey);
        while (cacheOrder.Count > 6) cache.Remove(cacheOrder.Dequeue());
        return task;
    }
    private void Begin(SpriteGraph graph, Decorator parent, Action? end)
    {
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(() => Begin(graph, parent, end)); return; }
        if (disposed) return;
        timer.Stop();
        if (active?.Control != null) { active.Control.EndAction = null; active.Control.Type = IGraph.TaskControl.ControlType.Status_Stoped; }
        active = graph; graph.Control = new IGraph.TaskControl(end);
        if (surface.Parent is Decorator old && old != parent) old.Child = null;
        parent.Child = surface; parent.Tag = graph;
        _ = LoadAndShow(graph, false);
    }
    private async Task LoadAndShow(SpriteGraph graph, bool formChange)
    {
        int request = ++revision;
        double fraction = formChange && clip != null ? Math.Clamp((clock.Elapsed.TotalMilliseconds-startedAt)/clip.Duration, 0, .999) : 0;
        try
        {
            Clip loaded = await Load(Form, Key(graph.GraphInfo));
            if (disposed || request != revision || active != graph || graph.Control?.PlayState != true) return;
            outgoing.Source = image.Source;
            outgoing.RenderTransform = image.RenderTransform;
            clip = loaded; frameIndex = -1;
            image.RenderTransform = loaded.Flip ? new ScaleTransform(-1, 1, 250, 250) : Transform.Identity;
            startedAt = clock.Elapsed.TotalMilliseconds - loaded.Duration*fraction;
            transitionAt = clock.Elapsed.TotalMilliseconds;
            fadeDuration = reduced() ? 0 : outgoing.Source == null ? 0 : formChange ? 420 : 100;
            lastTick = transitionAt; timer.Start(); Tick();
            // Warm the normal return clip while this action is playing.
            if (!Key(graph.GraphInfo).StartsWith("Default:", StringComparison.Ordinal)) _ = Load(Form, "Default:idle:Single");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            LastError = ex.Message;
            if (request == revision) Finish(graph);
        }
    }
    private void Tick()
    {
        if (disposed || active == null || clip == null) return;
        var control = active.Control;
        if (control == null || !control.PlayState) { timer.Stop(); return; }
        double now = clock.Elapsed.TotalMilliseconds;
        if (!visible()) { lastTick = now; return; }
        MaximumTickGap = Math.Max(MaximumTickGap, now-lastTick); lastTick = now; RenderTicks++;
        double elapsed = now-startedAt;
        if (elapsed >= clip.Duration)
        {
            if (active.IsLoop || control.Type == IGraph.TaskControl.ControlType.Continue)
            {
                if (!active.IsLoop) control.Type = IGraph.TaskControl.ControlType.Status_Quo;
                startedAt = now-elapsed%clip.Duration; elapsed %= clip.Duration;
            }
            else { image.Source = clip.Frames[^1]; Finish(active); return; }
        }
        int next = clip.FrameAt(elapsed);
        if (next != frameIndex) { image.Source = clip.Frames[next]; frameIndex = next; FramesChanged++; }
        double fade = fadeDuration <= 0 ? 1 : Math.Clamp((now-transitionAt)/fadeDuration, 0, 1);
        fade = fade*fade*(3-2*fade); image.Opacity = fade; outgoing.Opacity = 1-fade;
        if (fade >= 1) outgoing.Source = null;
        double breath = reduced() ? 0 : Math.Sin(now / 650);
        scale.ScaleY = 1 + breath*.0025;
        sway.Y = reduced() ? 0 : Math.Sin(now / 1000)*.6;
    }
    private void Finish(SpriteGraph graph)
    {
        timer.Stop(); revision++;
        var control = graph.Control;
        if (control == null) return;
        Action? end = control.EndAction; control.EndAction = null; control.Type = IGraph.TaskControl.ControlType.Status_Stoped;
        if (end != null) dispatcher.BeginInvoke(end);
    }
    private void Stop(SpriteGraph graph, bool suppress)
    {
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(() => Stop(graph, suppress)); return; }
        if (graph.Control == null) return;
        if (suppress) graph.Control.EndAction = null;
        if (active == graph) Finish(graph);
        else graph.Control.Type = IGraph.TaskControl.ControlType.Status_Stoped;
    }
    public void Dispose() { disposed = true; revision++; timer.Stop(); cache.Clear(); image.Source = null; outgoing.Source = null; }

    private sealed class SpriteGraph : IGraph
    {
        private readonly SmoothSpritePlayer player;
        internal SpriteGraph(SmoothSpritePlayer owner, GraphInfo info, bool loop) { player = owner; GraphInfo = info; IsLoop = loop; }
        public GraphInfo GraphInfo { get; }
        public bool IsLoop { get; set; }
        public bool IsReady => true;
        public bool IsFail => false;
        public string FailMessage => player.LastError;
        public string? Path => null;
        public IGraph.TaskControl? Control { get; internal set; }
        public void Run(Decorator parent, Action? end = null) => player.Begin(this, parent, end);
        public void Stop(bool suppress) => player.Stop(this, suppress);
        public void Dispose() { Stop(true); }
    }
}
