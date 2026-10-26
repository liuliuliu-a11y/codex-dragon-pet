using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Registers intact generated cells on a common 500-unit canvas; no artwork repainting.
internal static class PackWhiteDragon
{
    private static string source, output;
    private static BitmapSource[] Read(string name)
    {
        var sheet = new BitmapImage(); sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.UriSource = new Uri(Path.Combine(source, name + ".png")); sheet.EndInit(); sheet.Freeze();
        var frames = new BitmapSource[16];
        int width = sheet.PixelWidth / 4, height = sheet.PixelHeight / 4;
        var cellBounds = new Rect[16]; var cells = new BitmapSource[16];
        for (int i = 0; i < 16; i++) cellBounds[i] = Bounds(new CroppedBitmap(sheet, new Int32Rect((i % 4) * width, (i / 4) * height, width, height)), out cells[i]);
        double scale = Math.Min(430.0 / cellBounds.Max(b => b.Height), 440.0 / cellBounds.Max(b => b.Width));
        for (int i = 0; i < 16; i++)
        {
            var cell = cells[i];
            Rect bounds = cellBounds[i];
            var isolated = new CroppedBitmap(cell, new Int32Rect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawImage(isolated, new Rect(250 + (bounds.X - width / 2.0) * scale, 470 - bounds.Height * scale, bounds.Width * scale, bounds.Height * scale));
            var target = new RenderTargetBitmap(500, 500, 96, 96, PixelFormats.Pbgra32); target.Render(visual); target.Freeze(); frames[i] = target;
        }
        return frames;
    }
    private static Rect Bounds(BitmapSource source, out BitmapSource isolated)
    {
        var rgba = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = rgba.PixelWidth, height = rgba.PixelHeight;
        byte[] pixels = new byte[width * height * 4]; rgba.CopyPixels(pixels, width * 4, 0);
        var visited = new bool[width * height]; var queue = new int[width * height];
        int minX = width, minY = height, maxX = 0, maxY = 0, largest = 0; int[] principal = null;
        // Isolate the principal intact sprite; neighbouring-cell feet and stray atlas
        // fragments must never become part of this frame's crop or baseline.
        for (int pixel = 0; pixel < visited.Length; pixel++)
        {
            if (visited[pixel] || pixels[pixel * 4 + 3] < 64) continue;
            int read = 0, count = 1, x0 = pixel % width, y0 = pixel / width, x1 = x0, y1 = y0;
            queue[0] = pixel; visited[pixel] = true;
            while (read < count)
            {
                int current = queue[read++], x = current % width, y = current / width;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int next = ny * width + nx;
                    if (!visited[next] && pixels[next * 4 + 3] >= 64) { visited[next] = true; queue[count++] = next; }
                }
            }
            if (count > largest) { largest = count; minX = x0; maxX = x1; minY = y0; maxY = y1; principal = queue.Take(count).ToArray(); }
        }
        if (minY >= maxY) throw new InvalidDataException("Empty sprite cell");
        var include = new bool[width * height];
        foreach (int point in principal)
        {
            int x = point % width, y = point / width;
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
            { int nx = x + dx, ny = y + dy; if (nx >= 0 && nx < width && ny >= 0 && ny < height) include[ny * width + nx] = true; }
        }
        for (int i = 0; i < include.Length; i++) if (!include[i]) { pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = pixels[i * 4 + 3] = 0; }
        isolated = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); isolated.Freeze();
        minX = Math.Max(0, minX - 2); minY = Math.Max(0, minY - 2); maxX = Math.Min(width - 1, maxX + 2); maxY = Math.Min(height - 1, maxY + 2);
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
    private static void Write(string path, BitmapSource[] sheet, int[] indices, int[] durations = null, bool flip = false)
    {
        string folder = Path.Combine(output, "white", path); Directory.CreateDirectory(folder);
        for (int i = 0; i < indices.Length; i++)
        {
            BitmapSource frame = sheet[indices[i]];
            if (flip) { frame = new TransformedBitmap(frame, new ScaleTransform(-1, 1)); frame.Freeze(); }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
            string name = path.Split('/')[1];
            using (var stream = File.Create(Path.Combine(folder, name + "_" + i.ToString("D3") + "_" + (durations == null ? 140 : durations[i]) + ".png"))) encoder.Save(stream);
        }
    }
    private static int[] Range(int start, int count) { return Enumerable.Range(start, count).ToArray(); }
    [STAThread]
    public static int Main(string[] args)
    {
        source = Path.GetFullPath(args[0]); output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var walkSleep = Read("walk-sleep"); var idlePet = Read("idle-pet"); var liftWave = Read("lift-wave");
        var teaFood = Read("tea-food"); var workThink = Read("work-think");
        Write("Default/idle/Nomal", idlePet, Range(0, 8), new[] { 1700, 120, 90, 80, 90, 120, 240, 900 });
        Write("Touch_Head/pet/A_Nomal", idlePet, new[] { 8, 9 }, new[] { 160, 160 });
        Write("Touch_Head/pet/B_Nomal", idlePet, new[] { 10, 11, 12, 11 }, new[] { 220, 220, 220, 220 });
        Write("Touch_Head/pet/C_Nomal", idlePet, new[] { 13, 14, 15 }, new[] { 160, 160, 180 });
        Write("Touch_Body/curious/A_Nomal", workThink, new[] { 8, 9, 10 });
        Write("Touch_Body/curious/B_Nomal", workThink, new[] { 11, 12, 13, 12 });
        Write("Touch_Body/curious/C_Nomal", workThink, new[] { 14, 15 });
        Write("Sleep/nap/A_Nomal", walkSleep, Range(8, 6), new[] { 200, 250, 220, 220, 250, 250 });
        Write("Sleep/nap/B_Nomal", walkSleep, new[] { 13, 15 }, new[] { 1200, 1200 });
        Write("Sleep/nap/C_Nomal", walkSleep, Range(8, 6).Reverse().ToArray(), new[] { 220, 220, 220, 220, 220, 180 });
        Write("Raised_Static/dangle/A_Nomal", liftWave, new[] { 0, 1, 2 });
        Write("Raised_Static/dangle/B_Nomal", liftWave, new[] { 3, 4, 5, 6, 7, 6, 5, 4 });
        Write("Raised_Static/dangle/C_Nomal", liftWave, new[] { 2, 1, 0 });
        Write("Raised_Dynamic/dangle/Nomal", liftWave, new[] { 3, 4, 5, 6, 7, 6, 5, 4 });
        foreach (string direction in new[] { "left", "right" })
        {
            bool flip = direction == "left";
            Write("MOVE/walk." + direction + "/A_Nomal", walkSleep, new[] { 0, 1 }, null, flip);
            Write("MOVE/walk." + direction + "/B_Nomal", walkSleep, Range(0, 8), new[] { 110, 110, 110, 110, 110, 110, 110, 110 }, flip);
            Write("MOVE/walk." + direction + "/C_Nomal", walkSleep, new[] { 6, 7 }, null, flip);
        }
        Write("Work/typing/A_Nomal", idlePet, new[] { 0 });
        Write("Work/typing/B_Nomal", workThink, Range(0, 8), new[] { 250, 150, 120, 220, 120, 220, 120, 300 });
        Write("Work/typing/C_Nomal", idlePet, new[] { 0 });
        Write("Idel/thinking/A_Nomal", workThink, new[] { 8, 9, 10 });
        Write("Idel/thinking/B_Nomal", workThink, new[] { 11, 12, 13, 12 });
        Write("Idel/thinking/C_Nomal", workThink, new[] { 14, 15 });
        Write("Say/speaking/A_Nomal", idlePet, new[] { 0, 8 });
        Write("Say/speaking/B_Nomal", idlePet, new[] { 8, 9 }, new[] { 180, 180 });
        Write("Say/speaking/C_Nomal", idlePet, new[] { 15 });
        Write("StartUP/hello/Nomal", liftWave, Range(8, 8));
        Write("Common/celebrate/Nomal", liftWave, Range(8, 8));
        Write("Common/dragon-eat/Nomal", teaFood, Range(8, 8), new[] { 200, 250, 350, 500, 400, 500, 300, 250 });
        Write("Common/dragon-drink/Nomal", teaFood, Range(0, 8), new[] { 200, 300, 400, 450, 650, 500, 400, 300 });
        File.WriteAllText(Path.Combine(output, "vup.lps"),
            "pet#white:|path#white:|petname#小白龙:|\n" +
            "touchhead:|px#135:|py#35:|sw#230:|sh#210:|\n" +
            "touchbody:|px#155:|py#250:|sw#190:|sh#160:|\n" +
            "touchraised:|nomal_px#90:|nomal_py#10:|nomal_sw#320:|nomal_sh#220:|\n" +
            "raisepoint:|nomal_x#250:|nomal_y#85:|\n" +
            "work:|Type#Work:|Name#陪你工作:|Graph#typing:|Time#60:|MoneyBase#0:|StrengthFood#0:|StrengthDrink#0:|Feeling#0:|\n" +
            "move:|graph#walk.left:|TriggerLeft#120:|TriggerBottom#100:|TriggerType#16:|CheckType#16:|CheckLeft#80:|SpeedX#-5:|Distance#6:|ModeType#14:|\n" +
            "move:|graph#walk.right:|TriggerRight#120:|TriggerBottom#100:|TriggerType#32:|CheckType#32:|CheckRight#80:|SpeedX#5:|Distance#6:|ModeType#14:|\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "CHARACTER-CREDIT.txt"), "小白龙：本项目既有白龙形象，以原图为参考生成成套动作。\n动画内核：VPet-Simulator.Core，https://github.com/LorisYounger/VPet 。\n本包不含VPet默认角色或其动画素材。", new UTF8Encoding(false));
        Console.WriteLine("White dragon pack: " + Directory.GetFiles(output, "*.png", SearchOption.AllDirectories).Length + " registered frames.");
        return 0;
    }
}
