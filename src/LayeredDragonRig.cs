using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace LayeredDragon
{
    public enum MotionKind { Idle, Pet, Hop, Stretch, Drag, Land, Celebrate, Greet }

    public sealed class Pose
    {
        public const double TeaDurationSeconds = 10;
        public double Time, Tea, CupOpacity, CupX, CupY, CupAngle, Blink, Lift, Explode;
        public double HeadTurn, Pet;
        public double HeadNod, Arms, Lean, OffsetY, Wing, Tail;
        public double ScaleX = 1, ScaleY = 1;
        public Point LeftShoulder, LeftElbow, LeftWrist, RightShoulder, RightElbow, RightWrist;
        public string Stage;
        public static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }
        public static double Smooth(double a, double b, double v)
        { double u = Clamp((v - a) / (b - a)); return u * u * u * (u * (u * 6 - 15) + 10); }
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
            p.Arms = p.Tea;
            return p;
        }
        private static double Pulse(double t) { return Math.Pow(Math.Sin(Math.PI * Clamp(t)), 2); }
        public void ApplyMotion(MotionKind kind, double progress, double seconds, int mood)
        {
            double t = Clamp(progress), envelope = Smooth(0, .22, t) * (1 - Smooth(.75, 1, t));
            if (kind == MotionKind.Idle)
            {
                // State is expressed on the same face and body, never by changing character artwork.
                HeadTurn += (mood == 1 ? -2 : mood == 2 ? 3 : mood == 4 ? -3 : 0) * (1 - Tea);
                HeadNod = (mood == 1 ? 2 : mood == 5 ? 4 : 0) * (1 - Tea);
                if (mood == 5) Blink = Math.Max(Blink, .2 * (1 - Tea));
            }
            if (kind == MotionKind.Pet)
            {
                Pet = envelope; Blink = Math.Max(Blink, envelope);
                HeadTurn += Math.Sin(t * Math.PI * 4) * 3 * envelope;
                HeadNod = 3 * envelope; Tail = 5 * Math.Sin(t * Math.PI * 6) * envelope;
            }
            else if (kind == MotionKind.Hop)
            {
                double crouch = t < .22 ? Pulse(t / .22) : t > .72 ? .65 * Pulse((t - .72) / .28) : 0;
                double flight = t >= .22 && t <= .72 ? Pulse((t - .22) / .5) : 0;
                ScaleX = 1 + .055 * crouch - .02 * flight;
                ScaleY = 1 - .065 * crouch + .025 * flight;
                OffsetY = 5 * crouch - 44 * flight;
                Wing = -6 * flight; Tail = 5 * Math.Sin(t * Math.PI * 2) * envelope;
            }
            else if (kind == MotionKind.Stretch || kind == MotionKind.Celebrate)
            {
                Arms = envelope;
                double lift = kind == MotionKind.Stretch ? 1 : .8;
                // Keep a forward forearm projection so the broad hanging sleeves do not fold edge-on.
                LeftElbow = new Point(178, 284);
                RightElbow = new Point(349, 282);
                LeftWrist = new Point(219 - 8 * lift, 274 - 24 * lift);
                RightWrist = new Point(300 + 8 * lift, 272 - 22 * lift);
                HeadNod = -3 * envelope; ScaleY = 1 + .025 * envelope;
                Blink = Math.Max(Blink, .96 * envelope);
                Wing = -5 * envelope;
                if (kind == MotionKind.Stretch) Lean = -1.5 * Math.Sin(t * Math.PI * 2) * envelope;
                else
                {
                    OffsetY = -20 * Math.Pow(Math.Sin(t * Math.PI * 2), 2) * envelope;
                    Tail = 6 * Math.Sin(t * Math.PI * 5) * envelope;
                }
            }
            else if (kind == MotionKind.Drag)
            {
                Lean = 2.5 * Math.Sin(seconds * 3.2);
                HeadTurn += -Lean * .5; OffsetY = -5; ScaleY = 1.015;
                Wing = 3 * Math.Sin(seconds * 4); Tail = -Lean * 1.5;
            }
            else if (kind == MotionKind.Land)
            {
                double bounce = Pulse(t);
                ScaleX = 1 + .045 * bounce; ScaleY = 1 - .055 * bounce; OffsetY = 4 * bounce;
                Wing = 2 * Math.Sin(t * Math.PI * 2) * (1 - t);
            }
            else if (kind == MotionKind.Greet)
            {
                HeadNod = 7 * Math.Pow(Math.Sin(t * Math.PI * 2), 2) * envelope;
                HeadTurn += 2 * Math.Sin(t * Math.PI * 2) * envelope;
                Blink = Math.Max(Blink, .9 * Pulse((t - .14) / .28));
                Lean = 1.5 * envelope; Tail = 3 * Math.Sin(t * Math.PI * 3) * envelope;
            }
        }
        public Pose Copy() { return (Pose)MemberwiseClone(); }
        public void BlendFrom(Pose from, double u)
        {
            Func<double, double, double> mix = (a, b) => Mix(a, b, u);
            Func<Point, Point, Point> point = (a, b) => new Point(mix(a.X, b.X), mix(a.Y, b.Y));
            Tea = mix(from.Tea, Tea); Arms = mix(from.Arms, Arms); Blink = mix(from.Blink, Blink);
            CupOpacity = mix(from.CupOpacity, CupOpacity); CupX = mix(from.CupX, CupX); CupY = mix(from.CupY, CupY); CupAngle = mix(from.CupAngle, CupAngle);
            Lift = mix(from.Lift, Lift); HeadTurn = mix(from.HeadTurn, HeadTurn); HeadNod = mix(from.HeadNod, HeadNod); Pet = mix(from.Pet, Pet);
            Lean = mix(from.Lean, Lean); OffsetY = mix(from.OffsetY, OffsetY); Wing = mix(from.Wing, Wing); Tail = mix(from.Tail, Tail);
            ScaleX = mix(from.ScaleX, ScaleX); ScaleY = mix(from.ScaleY, ScaleY);
            LeftShoulder = point(from.LeftShoulder, LeftShoulder); RightShoulder = point(from.RightShoulder, RightShoulder);
            LeftElbow = point(from.LeftElbow, LeftElbow); RightElbow = point(from.RightElbow, RightElbow);
            LeftWrist = point(from.LeftWrist, LeftWrist); RightWrist = point(from.RightWrist, RightWrist);
        }
        public Point Warp(Point source)
        {
            double x = source.X, y = source.Y;
            double breath = -1.1 * Math.Sin(Time * Math.PI * 2 / 4.8);
            y += breath * Clamp((494 - source.Y) / 150);
            double headWeight = 1 - Smooth(235, 292, source.Y);
            double angle = (.48 * Math.Sin(Time * 1.05) + Lift * 1.5 + HeadTurn) * Math.PI / 180 * headWeight;
            double dx = x - 260, dy = y - 250;
            x = 260 + dx * Math.Cos(angle) - dy * Math.Sin(angle);
            y = 250 + dx * Math.Sin(angle) + dy * Math.Cos(angle);
            y += HeadNod * headWeight;
            double hair = Smooth(245, 400, source.Y) * (1 - Smooth(420, 465, source.Y)) *
                Math.Exp(-Math.Pow((Math.Abs(source.X - 260) - 110) / 32, 2));
            x += hair * Math.Sin(Time * 1.65 + source.Y * .012) * 2.6;
            double wing = Smooth(245, 280, source.Y) * (1 - Smooth(325, 365, source.Y)) *
                Clamp((Math.Abs(source.X - 260) - 130) / 45);
            y += wing * Math.Sin(Time * 1.4) * 1.6;
            double tail = Smooth(348, 440, source.X) * Smooth(345, 390, source.Y);
            x += tail * Math.Sin(Time * 1.3 + .6) * 3.2;
            y += tail * Math.Cos(Time * 1.3) * 2.1;
            x += tail * Tail;
            y += wing * Wing;
            x = 256 + (x - 256) * ScaleX; y = 486 + (y - 486) * ScaleY;
            double lean = Lean * Math.PI / 180;
            dx = x - 256; dy = y - 486;
            x = 256 + dx * Math.Cos(lean) - dy * Math.Sin(lean);
            y = 486 + dx * Math.Sin(lean) + dy * Math.Cos(lean) + OffsetY;
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
        private Pose transitionFrom;
        private string transitionKey = "";
        private double transitionStart;
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
        public void Update(double time, double teaTime, bool exploded, double blink = -1, double headTurn = 0, double pet = 0,
            MotionKind motion = MotionKind.Idle, double progress = 0, double seconds = 0, int mood = 0, bool snap = false)
        {
            Pose p = Pose.At(time, teaTime, exploded);
            p.HeadTurn = headTurn; p.Pet = Pose.Clamp(pet);
            if (blink >= 0) p.Blink = Pose.Clamp(blink);
            p.ApplyMotion(motion, progress, seconds, mood);
            string key = motion + ":" + (teaTime >= 0) + ":" + mood;
            if (key != transitionKey)
            { transitionFrom = Current == null ? null : Current.Copy(); transitionStart = time; transitionKey = key; }
            if (snap) transitionFrom = null;
            if (transitionFrom != null)
            {
                double amount = Pose.Smooth(0, .22, time - transitionStart);
                p.BlendFrom(transitionFrom, amount);
                if (amount >= 1) transitionFrom = null;
            }
            Current = p;
            body.Update(p.Warp, 1);
            restingArms.Update(p.Warp, 1 - p.Arms);
            leftArm.Update(v => p.Warp(Arm(v, new Point(45, 43), new Point(80, 103), new Point(197, 126),
                p.LeftShoulder, p.LeftElbow, p.LeftWrist) + new Vector(-85 * p.Explode, -8 * p.Explode)), p.Arms);
            rightArm.Update(v => p.Warp(Arm(v, new Point(235, 42), new Point(205, 99), new Point(101, 126),
                p.RightShoulder, p.RightElbow, p.RightWrist) + new Vector(85 * p.Explode, -8 * p.Explode)), p.Arms);
            cup.Update(v => {
                double a = p.CupAngle * Math.PI / 180;
                return p.Warp(new Point(p.CupX + v.X * Math.Cos(a) - v.Y * Math.Sin(a) + 130 * p.Explode,
                    p.CupY + v.X * Math.Sin(a) + v.Y * Math.Cos(a) - 80 * p.Explode));
            }, p.CupOpacity);
            // A brief closure avoids long translucent double-eye images during a relaxed blink.
            eyelids.Update(p.Warp, Pose.Smooth(.35, .65, p.Blink));
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
