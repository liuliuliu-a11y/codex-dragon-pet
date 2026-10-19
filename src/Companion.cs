using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace CodexDragon
{
    public sealed partial class PetWindow
    {
        private Image previousSprite;
        private TextBlock heart, dashMeta;
        private StackPanel recentPanel;
        private Forms.ToolStripMenuItem trayClickThrough;
        private CheckBox clickThroughCheck;
        private int activeFrame = -1, pettingCount;
        private string progressKey = "", recentKey = "";
        private DateTime progressUntil = DateTime.MinValue;
        private bool hovering;

        private void InitializeCompanion()
        {
            heart = Ui.Text("♡", 31, Ui.Accent);
            heart.IsHitTestVisible = false; heart.Opacity = 0;
            heart.HorizontalAlignment = HorizontalAlignment.Center; heart.VerticalAlignment = VerticalAlignment.Bottom;
            heart.Margin = new Thickness(105, 0, 0, 260); scene.Children.Add(heart);
            sprite.MouseEnter += delegate { hovering = true; RefreshView(); };
            sprite.MouseLeave += delegate
            {
                hovering = false;
                if (!settings.ReduceMotion && !dragging) AnimateTilt(0, .25);
            };
            sprite.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (settings.ReduceMotion || dragging || e.LeftButton == MouseButtonState.Pressed) return;
                double x = e.GetPosition(sprite).X / Math.Max(1, sprite.ActualWidth) - .5;
                AnimateTilt(Math.Max(-1.8, Math.Min(1.8, x * 3.6)), .18);
            };
            SourceInitialized += delegate { ApplyClickThrough(); };
            ApplyMotion();
        }
        private void ApplyMotion()
        {
            floatTransform.BeginAnimation(TranslateTransform.YProperty, null);
            if (settings.ReduceMotion || !IsVisible) { floatTransform.Y = 0; tiltTransform.BeginAnimation(RotateTransform.AngleProperty, null); tiltTransform.Angle = 0; return; }
            var breathing = new DoubleAnimation(-2, 3, TimeSpan.FromSeconds(2.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(breathing, settings.Quiet ? 3 : 6);
            floatTransform.BeginAnimation(TranslateTransform.YProperty, breathing);
        }
        private void AnimateTilt(double angle, double seconds)
        {
            var tilt = new DoubleAnimation(angle, TimeSpan.FromSeconds(seconds));
            Timeline.SetDesiredFrameRate(tilt, 24);
            tiltTransform.BeginAnimation(RotateTransform.AngleProperty, tilt);
        }
        private void PetHead()
        {
            pettingCount++;
            string[] replies = { "收到摸摸，继续加油。", "角角有点痒。", "今天也一起加油。" };
            Say("角色碎碎念", replies[random.Next(replies.Length)], 8); Wiggle();
            if (settings.ReduceMotion) return;
            heart.Opacity = 1;
            var rise = new TranslateTransform(); heart.RenderTransform = rise;
            var lift = new DoubleAnimation(0, -24, TimeSpan.FromSeconds(1.1));
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.1));
            Timeline.SetDesiredFrameRate(lift, 24); Timeline.SetDesiredFrameRate(fade, 24);
            rise.BeginAnimation(TranslateTransform.YProperty, lift);
            heart.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        private void UpdateBubbleVisibility()
        {
            bool visible = settings.Bubble && (!settings.AutoBubble || hovering || DateTime.UtcNow < speechUntil || DateTime.UtcNow < progressUntil);
            bubble.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        private void ObserveProgress()
        {
            string key = selected == null ? "" : selected.Id + ":" + selected.Stage + ":" + selected.PublicMessage;
            if (key == progressKey) return;
            progressKey = key;
            if (!settings.Quiet && selected != null && selected.Running) progressUntil = DateTime.UtcNow.AddSeconds(8);
        }
        internal void SetClickThrough(bool enabled)
        {
            settings.ClickThrough = enabled;
            if (!ApplyClickThrough()) settings.ClickThrough = false;
            if (trayClickThrough != null) trayClickThrough.Checked = settings.ClickThrough;
            if (clickThroughCheck != null) clickThroughCheck.IsChecked = settings.ClickThrough;
            if (settings.ClickThrough) hovering = false;
            Persist(); ContextMenu = MakeMenu();
            Say("桌面陪伴", settings.ClickThrough ? "我让开啦。右下角托盘可以关闭穿透，恢复互动。" : "可以摸摸、拖动我啦。", 7);
        }
        private bool ApplyClickThrough()
        {
            try { NativeWindow.SetClickThrough(new WindowInteropHelper(this).Handle, settings.ClickThrough); return true; }
            catch (System.ComponentModel.Win32Exception e) { Program.LogError(e); return false; }
        }
        private void SetQuiet(bool quiet)
        {
            settings.Quiet = quiet; ApplyMotion(); Persist();
            if (quiet) progressUntil = DateTime.MinValue;
            Say("陪伴方式", quiet ? "进入安静陪伴。完成时给你看气泡，声音和弹窗先收起来。" : "恢复日常陪伴啦。", 8);
            ContextMenu = MakeMenu();
        }
        private void AddCompanionOptions(StackPanel content)
        {
            var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            AddCheck(options, "气泡自动收起", settings.AutoBubble, delegate(bool value) { settings.AutoBubble = value; UpdateBubbleVisibility(); });
            AddCheck(options, "安静陪伴", settings.Quiet, SetQuiet);
            AddCheck(options, "减少动画", settings.ReduceMotion, delegate(bool value) { settings.ReduceMotion = value; ApplyMotion(); });
            clickThroughCheck = new CheckBox { Content = "鼠标穿透", IsChecked = settings.ClickThrough, FontSize = 11, Foreground = Ui.Ink, Margin = new Thickness(0, 0, 20, 5) };
            clickThroughCheck.Click += delegate { SetClickThrough(clickThroughCheck.IsChecked == true); };
            options.Children.Add(clickThroughCheck); content.Children.Add(options);
            content.Children.Add(Ui.Text("最近结束的聊天", 11, Ui.Muted));
            recentPanel = new StackPanel { Margin = new Thickness(0, 7, 0, 16) }; content.Children.Add(recentPanel); recentKey = "";
        }
        private void RefreshCompanionInfo()
        {
            if (dashMeta != null)
            {
                string model = selected == null || selected.Model.Length == 0 ? "" : selected.Model + " · ";
                string elapsed = "";
                if (selected != null && selected.Started != DateTime.MinValue)
                {
                    TimeSpan duration = (selected.Running ? DateTime.UtcNow : selected.Updated) - selected.Started;
                    if (duration.TotalSeconds >= 0) elapsed = "本轮 " + (int)duration.TotalMinutes + "分" + duration.Seconds + "秒";
                }
                string tokens = selected != null && selected.Tokens > 0 ? " · 聊天累计 " + selected.Tokens.ToString("N0") + " Token" : "";
                dashMeta.Text = model + elapsed + tokens;
            }
            if (recentPanel == null) return;
            var ended = monitor.Sessions.Values.Where(s => !s.IsChild && !s.Running && s.Mood == Mood.Done).OrderByDescending(s => s.Updated).Take(3).ToArray();
            string key = String.Join("|", ended.Select(s => s.Id + s.Updated.Ticks));
            if (key == recentKey && recentPanel.Children.Count > 0) return;
            recentKey = key; recentPanel.Children.Clear();
            foreach (Session session in ended)
            {
                string id = session.Id;
                string caption = session.Result.Length > 0 ? Ui.Short(session.Result, 32) : "本轮已结束，点此查看结果";
                var button = Ui.Button(session.Updated.ToLocalTime().ToString("HH:mm") + "  " + caption, delegate { OpenThread(id); });
                button.HorizontalContentAlignment = HorizontalAlignment.Left; recentPanel.Children.Add(button);
            }
            if (ended.Length == 0) recentPanel.Children.Add(Ui.Text("本机聊天结束后，结果会显示在这里。", 11, Ui.Muted));
        }
    }
}
