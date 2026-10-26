using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using LayeredDragon;

// Offline WPF renders of the production rig; no user settings, sessions or network.
public static class MotionRenderTests
{
    private sealed class Clip
    {
        public MotionKind Kind; public string Label; public double Duration; public bool Tea;
        public Clip(MotionKind kind, string label, double duration, bool tea = false)
        { Kind = kind; Label = label; Duration = duration; Tea = tea; }
    }
    private static int failures, checks;
    private static void Check(bool ok, string label)
    { checks++; if (!ok) failures++; Console.WriteLine((ok ? "PASS " : "FAIL ") + label); }
    [STAThread]
    public static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        string frames = Path.Combine(output, "frames"); Directory.CreateDirectory(frames);
        var rig = new DragonRig(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "layered"));
        var root = new Grid { Width = 512, Height = 570, Background = new SolidColorBrush(Color.FromRgb(249, 247, 252)) };
        var viewport = new Viewport3D { Width = 512, Height = 512, VerticalAlignment = VerticalAlignment.Top,
            Camera = new OrthographicCamera(new Point3D(256, -256, 1000), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 576) };
        viewport.Children.Add(rig.Visual); root.Children.Add(viewport);
        var overlay = new SteamOverlay(rig, 576) { Width = 512, Height = 512, VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(overlay);
        var label = new TextBlock { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 19,
            Foreground = new SolidColorBrush(Color.FromRgb(111, 78, 150)), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20) };
        root.Children.Add(label); root.Measure(new Size(512, 570)); root.Arrange(new Rect(0, 0, 512, 570)); root.UpdateLayout();
        var clips = new[] {
            new Clip(MotionKind.Idle, "自然待机", 1), new Clip(MotionKind.Greet, "点头打招呼", 1.9),
            new Clip(MotionKind.Pet, "摸摸头", 1.4), new Clip(MotionKind.Stretch, "伸个懒腰", 2.27),
            new Clip(MotionKind.Hop, "开心跳一下", 1.1), new Clip(MotionKind.Idle, "一起喝茶", 10, true),
            new Clip(MotionKind.Drag, "轻轻拿起", 1.5), new Clip(MotionKind.Land, "落地缓冲", .46),
            new Clip(MotionKind.Celebrate, "工作结束啦", 1.7),
            new Clip(MotionKind.Sleep, "蜷身休息 · 轻轻呼吸", 4.5), new Clip(MotionKind.Wake, "慢慢醒来", 2.4),
            new Clip(MotionKind.Walk, "走一小段 · 原地步态展示", 4), new Clip(MotionKind.Feed, "捧起点心 · 吃一口 · 收手", 5.6),
            new Clip(MotionKind.Idle, "回到待机", 1)
        };
        int frame = 0, contactIndex = 0; double time = 0, maxStep = 0;
        Point[] probes = { new Point(220, 160), new Point(300, 195), new Point(260, 250), new Point(180, 300), new Point(370, 400), new Point(270, 482) };
        Point[] previous = null;
        var contact = new WrapPanel { Width = 1024, Background = Brushes.White };
        foreach (Clip clip in clips)
        {
            label.Text = clip.Label;
            int count = (int)Math.Ceiling(clip.Duration * 60);
            bool rendered = false;
            for (int i = 0; i < count; i++, time += 1.0 / 60)
            {
                double seconds = i / 60.0, progress = Math.Min(1, seconds / clip.Duration);
                if (clip.Kind == MotionKind.Sleep) progress = Math.Min(1, seconds / 2.2);
                rig.Update(time, clip.Tea ? seconds : -1, false, motion: clip.Kind, progress: progress, seconds: seconds);
                var current = new Point[probes.Length];
                for (int p = 0; p < probes.Length; p++)
                {
                    current[p] = rig.Current.Warp(probes[p]);
                    if (previous != null) maxStep = Math.Max(maxStep, (current[p] - previous[p]).Length);
                }
                previous = current;
                if (i % 2 != 0) continue;
                overlay.InvalidateVisual();
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(512, 570, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); bitmap.Freeze();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(frames, (frame++).ToString("D5") + ".png"))) encoder.Save(stream);
                if (!rendered && progress >= .43)
                {
                    contact.Children.Add(new Image { Source = bitmap, Width = 256, Height = 285 });
                    rendered = true; contactIndex++;
                }
            }
            Check(rig.Current.CupOpacity >= 0 && rig.Current.CupOpacity <= 1 && rig.Current.Arms >= 0 && rig.Current.Arms <= 1, clip.Kind + " opacity stays bounded");
        }
        Check(maxStep < 8, "all body probes remain continuous across the full gesture sequence (60 Hz)");
        Console.WriteLine("Maximum body step: " + maxStep.ToString("F4") + " design pixels; frames: " + frame);
        // Interrupt a tea sip at the mouth with dragging: first pose must keep the current geometry.
        rig.Update(100, 5, false, snap: true);
        Pose old = rig.Current.Copy();
        rig.Update(100, -1, false, motion: MotionKind.Drag, seconds: 0);
        Check((rig.Current.Warp(probes[0]) - old.Warp(probes[0])).Length < .00001 && rig.Current.CupOpacity == old.CupOpacity,
            "tea-to-drag interruption begins at the exact visible pose");
        rig.Update(100.25, -1, false, motion: MotionKind.Drag, seconds: .25);
        Check(rig.Current.CupOpacity == 0, "interrupted cup fades away within the bounded blend");
        foreach (MotionKind kind in new[] { MotionKind.Pet, MotionKind.Hop, MotionKind.Stretch, MotionKind.Land, MotionKind.Celebrate, MotionKind.Greet, MotionKind.Wake, MotionKind.Walk, MotionKind.Feed })
        {
            Pose end = Pose.At(0, -1, false); end.ApplyMotion(kind, 1, 3, 0);
            Check(Math.Abs(end.OffsetY) < .00001 && Math.Abs(end.ScaleY - 1) < .00001 && Math.Abs(end.Arms) < .00001 && Math.Abs(end.HeadNod) < .00001,
                kind + " ends at a settled rest pose");
        }
        rig.Update(200, -1, false, motion: MotionKind.Sleep, progress: 1, seconds: 4, snap: true);
        Pose sleeping = rig.Current.Copy();
        rig.Update(200, -1, false, motion: MotionKind.Drag);
        Check((rig.Current.Warp(probes[0]) - sleeping.Warp(probes[0])).Length < .00001, "sleep interruption retains the exact visible pose");
        double interruptionStep = 0;
        Point last = rig.Current.Warp(probes[0]);
        for (int i = 1; i <= 60; i++)
        {
            rig.Update(200 + i / 60.0, -1, false, motion: MotionKind.Drag, seconds: i / 60.0);
            Point next = rig.Current.Warp(probes[0]); interruptionStep = Math.Max(interruptionStep, (next - last).Length); last = next;
        }
        Check(interruptionStep < 8, "picking up a sleeping pet has a gradual full-body transition");
        double height = Math.Ceiling(contactIndex / 4.0) * 285;
        contact.Measure(new Size(1024, height)); contact.Arrange(new Rect(0, 0, 1024, height)); contact.UpdateLayout();
        var sheet = new RenderTargetBitmap(1024, (int)height, 96, 96, PixelFormats.Pbgra32); sheet.Render(contact);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(sheet));
        using (var stream = File.Create(Path.Combine(output, "motion-contact.png"))) png.Save(stream);
        Console.WriteLine(checks + " motion checks; " + failures + " failed.");
        return failures == 0 ? 0 : 1;
    }
}
