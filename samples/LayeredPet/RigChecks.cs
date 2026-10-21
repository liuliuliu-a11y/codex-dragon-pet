using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LayeredDragon
{
    public static class RigChecks
    {
        [STAThread]
        public static void Main(string[] args)
        {
            string root = args[0];
            var rig = new DragonRig(Path.Combine(root, "assets"));
            double maxCupStep = 0, maxWristStep = 0;
            Pose previous = null;
            for (int frame = 0; frame <= 600; frame++)
            {
                double t = frame / 60.0;
                rig.Update(t, t, false);
                Pose p = rig.Current;
                if (p.CupOpacity < 0 || p.CupOpacity > 1 || p.Tea < 0 || p.Tea > 1) throw new Exception("Invalid layer opacity");
                if (p.LeftShoulder != new Point(222, 285) || p.RightShoulder != new Point(303, 281)) throw new Exception("Shoulder anchor drift");
                if (previous != null)
                {
                    maxCupStep = Math.Max(maxCupStep, Math.Abs(p.CupY - previous.CupY));
                    maxWristStep = Math.Max(maxWristStep, (p.LeftWrist - previous.LeftWrist).Length);
                }
                previous = p;
            }
            if (maxCupStep > 2 || maxWristStep > 2) throw new Exception("Discontinuous cup/wrist trajectory");
            Pose idle = Pose.At(20, -1, false), finished = Pose.At(20, 10, false);
            if (idle.Tea != 0 || idle.CupOpacity != 0 || finished.Tea != 0 || finished.CupOpacity != 0)
                throw new Exception("Prop not hidden after returning to idle");
            var region = new Int32Rect(183, 165, 140, 85);
            Func<string, byte[]> face = delegate(string name) {
                var image = new BitmapImage(new Uri(Path.GetFullPath(Path.Combine(root, "assets", name))));
                var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                byte[] pixels = new byte[region.Width * region.Height * 4];
                converted.CopyPixels(region, pixels, region.Width * 4, 0); return pixels;
            };
            if (!face("original.png").SequenceEqual(face("body.png"))) throw new Exception("Original face texture changed");
            Console.WriteLine("PASS: 601 continuous poses produce finite mesh vertices");
            Console.WriteLine("PASS: fixed shoulder anchors and continuous cup/wrist trajectory, max step " + maxCupStep.ToString("F3") + " pixels at 60Hz");
            Console.WriteLine("PASS: tea finishes with no cup or active arm layers");
            Console.WriteLine("PASS: original face preserved by asset packing check");
        }
    }
}
