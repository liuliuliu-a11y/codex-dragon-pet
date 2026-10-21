using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace LayeredDragon
{
    public sealed class Pose
    {
        public const double TeaDurationSeconds = 10;
        public double Time, Tea, CupOpacity, CupX, CupY, CupAngle, Blink, Lift, Explode;
        public Point LeftShoulder, LeftElbow, LeftWrist, RightShoulder, RightElbow, RightWrist;
        public string Stage;
        public static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }
        public static double Smooth(double a, double b, double v)
        { double u = Clamp((v - a) / (b - a)); return u * u * (3 - 2 * u); }
        public static double Mix(double a, double b, double u) { return a + (b - a) * u; }
        private static double Key(double t, double[] times, double[] values)
        {
            for (int i = 1; i < times.Length; i++)
                if (t <= times[i]) return Mix(values[i - 1], values[i], Smooth(times[i - 1], times[i], t));
            return values[values.Length - 1];
        }
        public static Pose At(double time, double teaTime, bool exploded)
        {
            var p = new Pose { Time = time, CupX = 251, CupY = 338, Stage = "自然待机", Explode = exploded ? 1 : 0 };
            double phase = time % 7.7;
            p.Blink = Math.Exp(-Math.Pow((phase - 2.2) / .065, 2)) + Math.Exp(-Math.Pow((phase - 5.9) / .075, 2));
            if (teaTime >= 0 && teaTime < TeaDurationSeconds)
            {
                double t = teaTime;
                p.Tea = Smooth(.35, .95, t) * (1 - Smooth(9.15, 9.8, t));
                p.CupOpacity = Smooth(1.1, 1.55, t) * (1 - Smooth(8.85, 9.35, t));
                double[] keys = { 0, 1.1, 2.5, 4.0, 6.4, 8.0, 9.3, 10 };
                p.CupY = Key(t, keys, new double[] { 338, 338, 304, 240, 240, 304, 338, 338 });
                p.Lift = Clamp((338 - p.CupY) / 98);
                p.CupX = 251 + p.Lift * 4;
                double drinking = Smooth(3.95, 4.4, t) * (1 - Smooth(6.0, 6.45, t));
                p.CupY += drinking * Math.Sin(t * 6) * .8;
                p.CupAngle = drinking * (-7 + Math.Sin(t * 5) * 1.5);
                p.Blink = Math.Max(p.Blink, drinking * .98);
                p.Stage = t < 1.3 ? "舒展双手" : t < 2.5 ? "从袖间拿杯" : t < 4 ? "缓缓举杯" : t < 6.4 ? "轻轻喝茶" : t < 8 ? "放下茶杯" : t < 9.35 ? "把杯子收好" : "回到待机";
            }
            p.LeftShoulder = new Point(222, 285);
            p.RightShoulder = new Point(303, 281);
            p.LeftElbow = new Point(204 - p.Lift * 10, 314 - p.Lift * 28);
            p.RightElbow = new Point(322 + p.Lift * 7, 313 - p.Lift * 27);
            p.LeftWrist = new Point(230 - p.Lift * 2, p.CupY + 15);
            p.RightWrist = new Point(274 + p.Lift * 2, p.CupY + 15);
            p.Blink = Clamp(p.Blink);
            return p;
        }
        public Point Warp(Point source)
        {
            double x = source.X, y = source.Y;
            double breath = -1.1 * Math.Sin(Time * Math.PI * 2 / 4.8);
            y += breath * Clamp((494 - source.Y) / 150);
            double headWeight = 1 - Smooth(235, 292, source.Y);
            double angle = (.48 * Math.Sin(Time * 1.05) + Lift * 1.5) * Math.PI / 180 * headWeight;
            double dx = x - 260, dy = y - 250;
            x = 260 + dx * Math.Cos(angle) - dy * Math.Sin(angle);
            y = 250 + dx * Math.Sin(angle) + dy * Math.Cos(angle);
            double hair = Smooth(245, 400, source.Y) * (1 - Smooth(420, 465, source.Y)) *
                Math.Exp(-Math.Pow((Math.Abs(source.X - 260) - 110) / 32, 2));
            x += hair * Math.Sin(Time * 1.65 + source.Y * .012) * 2.6;
            double wing = Smooth(245, 280, source.Y) * (1 - Smooth(325, 365, source.Y)) *
                Clamp((Math.Abs(source.X - 260) - 130) / 45);
            y += wing * Math.Sin(Time * 1.4) * 1.6;
            double tail = Smooth(348, 440, source.X) * Smooth(345, 390, source.Y);
            x += tail * Math.Sin(Time * 1.3 + .6) * 3.2;
            y += tail * Math.Cos(Time * 1.3) * 2.1;
            return new Point(x, y);
        }
    }

    internal sealed class Surface
    {
        public readonly GeometryModel3D Model;
        public readonly ImageBrush Brush;
        private readonly MeshGeometry3D mesh;
        private readonly List<Point> vertices = new List<Point>();
        private readonly double depth;
        public Surface(string path, Rect coordinates, int columns, int rows, double z)
        {
            depth = z;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(path)); image.EndInit(); image.Freeze();
            Brush = new ImageBrush(image) { Stretch = Stretch.Fill };
            var texture = new PointCollection(); var indices = new Int32Collection();
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
            {
                vertices.Add(new Point(coordinates.X + coordinates.Width * x / columns, coordinates.Y + coordinates.Height * y / rows));
                texture.Add(new Point((double)x / columns, (double)y / rows));
            }
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                int a = y * (columns + 1) + x, b = a + 1, c = a + columns + 1, d = c + 1;
                indices.Add(a); indices.Add(d); indices.Add(b); indices.Add(a); indices.Add(c); indices.Add(d);
            }
            texture.Freeze(); indices.Freeze();
            mesh = new MeshGeometry3D { TextureCoordinates = texture, TriangleIndices = indices };
            Model = new GeometryModel3D(mesh, new DiffuseMaterial(Brush));
        }
        public void Update(Func<Point, Point> map, double opacity)
        {
            Brush.Opacity = opacity;
            var positions = new Point3DCollection(vertices.Count);
            foreach (Point v in vertices)
            {
                Point p = map(v);
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y))
                    throw new InvalidDataException("Non-finite rig vertex.");
                positions.Add(new Point3D(p.X, -p.Y, depth));
            }
            positions.Freeze(); mesh.Positions = positions;
        }
    }

    public sealed class DragonRig
    {
        public readonly ModelVisual3D Visual;
        private readonly Surface body, restingArms, cup, leftArm, rightArm, eyelids;
        public Pose Current { get; private set; }
        public DragonRig(string assetDirectory)
        {
            body = new Surface(Path.Combine(assetDirectory, "body.png"), new Rect(0, 0, 512, 512), 24, 24, 0);
            restingArms = new Surface(Path.Combine(assetDirectory, "rest-arms.png"), new Rect(0, 0, 512, 512), 16, 16, 4);
            cup = new Surface(Path.Combine(assetDirectory, "cup.png"), new Rect(-23, -18, 46, 36), 1, 1, 2);
            leftArm = new Surface(Path.Combine(assetDirectory, "left-arm.png"), new Rect(0, 0, 295, 420), 12, 18, 3);
            rightArm = new Surface(Path.Combine(assetDirectory, "right-arm.png"), new Rect(0, 0, 275, 425), 12, 18, 3.1);
            eyelids = new Surface(Path.Combine(assetDirectory, "eyelids.png"), new Rect(180, 156, 144, 82), 9, 6, 5);
            var group = new Model3DGroup();
            group.Children.Add(new AmbientLight(Colors.White));
            foreach (var s in new[] { body, cup, leftArm, rightArm, restingArms, eyelids }) group.Children.Add(s.Model);
            Visual = new ModelVisual3D { Content = group };
            Update(0, -1, false);
        }
        private static Point Bone(Point p, Point a, Point b, Point c, Point d)
        {
            Vector source = b - a, target = d - c, v = p - a;
            double denom = source.LengthSquared;
            double u = Vector.Multiply(v, source) / denom;
            double side = (source.X * v.Y - source.Y * v.X) / denom;
            return c + target * u + new Vector(-target.Y, target.X) * side;
        }
        internal static Point Arm(Point p, Point s, Point e, Point w, Point ds, Point de, Point dw)
        {
            Point upper = Bone(p, s, e, ds, de);
            Point rigidForearm = Bone(p, e, w, de, dw);
            double u = (p.X - e.X) / (w.X - e.X);
            double vertical = p.Y - Pose.Mix(e.Y, w.Y, u);
            Point lower = rigidForearm;
            if (vertical > 0)
            {
                // Integrate a gradually downward sleeve tangent. Blending absolute positions
                // can turn the texture inside out at the elbow and make back-facing holes.
                Vector src = w - e, dst = dw - de;
                double a = Vector.Multiply(src, dst) / src.LengthSquared;
                double b = (dst.Y * src.X - dst.X * src.Y) / src.LengthSquared;
                double fall = 42 * (1 - Math.Exp(-vertical / 42));
                lower = de + dst * u + new Vector(-b * fall, .27 * vertical + (a - .27) * fall);
            }
            // Keep palms level under the cup instead of rotating fingers across its rim.
            double handAngle = (w.X > e.X ? 0 : Math.PI) - Math.Atan2(w.Y - e.Y, w.X - e.X);
            Vector handOffset = p - w;
            Point hand = dw + new Vector(
                (handOffset.X * Math.Cos(handAngle) - handOffset.Y * Math.Sin(handAngle)) * .28,
                (handOffset.X * Math.Sin(handAngle) + handOffset.Y * Math.Cos(handAngle)) * .28);
            double handWeight = Pose.Smooth(.85, 1.08, u) * (1 - Pose.Smooth(25, 48, Math.Abs(vertical)));
            lower = new Point(Pose.Mix(lower.X, hand.X, handWeight), Pose.Mix(lower.Y, hand.Y, handWeight));
            // Points along the wrist side belong to the forearm even above the elbow's y.
            // A y-only blend folds the hand/sleeve texture when the elbow is bent upward.
            double fore = Math.Max(Pose.Smooth(-.16, .30, u), Pose.Smooth(e.Y + 4, e.Y + 40, p.Y));
            return new Point(Pose.Mix(upper.X, lower.X, fore), Pose.Mix(upper.Y, lower.Y, fore));
        }
        public void Update(double time, double teaTime, bool exploded, double blink = -1)
        {
            Pose p = Current = Pose.At(time, teaTime, exploded);
            if (blink >= 0) p.Blink = Pose.Clamp(blink);
            body.Update(p.Warp, 1);
            restingArms.Update(p.Warp, 1 - p.Tea);
            leftArm.Update(v => p.Warp(Arm(v, new Point(45, 43), new Point(80, 103), new Point(197, 126),
                p.LeftShoulder, p.LeftElbow, p.LeftWrist) + new Vector(-85 * p.Explode, -8 * p.Explode)), p.Tea);
            rightArm.Update(v => p.Warp(Arm(v, new Point(235, 42), new Point(205, 99), new Point(101, 126),
                p.RightShoulder, p.RightElbow, p.RightWrist) + new Vector(85 * p.Explode, -8 * p.Explode)), p.Tea);
            cup.Update(v => {
                double a = p.CupAngle * Math.PI / 180;
                return p.Warp(new Point(p.CupX + v.X * Math.Cos(a) - v.Y * Math.Sin(a) + 130 * p.Explode,
                    p.CupY + v.X * Math.Sin(a) + v.Y * Math.Cos(a) - 80 * p.Explode));
            }, p.CupOpacity);
            eyelids.Update(p.Warp, p.Blink);
        }
    }

    internal sealed class SteamOverlay : FrameworkElement
    {
        private readonly DragonRig rig;
        private readonly double cameraWidth;
        public SteamOverlay(DragonRig value, double width = 560)
        { rig = value; cameraWidth = width; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext dc)
        {
            Pose p = rig.Current;
            if (p == null || p.CupOpacity < .01 || p.Explode > 0) return;
            for (int i = 0; i < 3; i++)
            {
                double cycle = (p.Time * .45 + i / 3.0) % 1;
                Point origin = p.Warp(new Point(p.CupX, p.CupY - 18));
                double x = 256 + (origin.X - 256) * 512 / cameraWidth;
                double y = 256 + (origin.Y - 256) * 512 / cameraWidth - cycle * 27;
                var geometry = new StreamGeometry();
                using (var c = geometry.Open())
                {
                    c.BeginFigure(new Point(x + i * 3 - 3, y), false, false);
                    c.BezierTo(new Point(x - 4, y - 7), new Point(x + 6, y - 11), new Point(x + Math.Sin(p.Time + i) * 3, y - 18), true, false);
                }
                geometry.Freeze();
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(75 * Math.Sin(cycle * Math.PI) * p.CupOpacity), 176, 158, 180)), 1.2);
                dc.DrawGeometry(null, pen, geometry);
            }
        }
    }
}
