using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphInfo;

namespace CodexVPet;

internal sealed partial class CompanionWindow
{
    private bool pointerPressed, pointerDragging;
    private int dragStarts, dragEnds, pointerClicks;
    private Point pointerAnchor;
    private readonly Popup speech = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Top, VerticalOffset = -8, IsHitTestVisible = true };
    private readonly TextBlock speechText = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Color.FromRgb(58, 47, 71)), MaxHeight = 59 };
    private readonly DispatcherTimer speechTimer = new() { Interval = TimeSpan.FromSeconds(7) };

    private static ResourceDictionary ChromeResources() => (ResourceDictionary)XamlReader.Parse("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
 <Style TargetType="Button">
  <Setter Property="FontFamily" Value="Microsoft YaHei UI"/><Setter Property="FontSize" Value="12"/>
  <Setter Property="Foreground" Value="#49365E"/><Setter Property="Background" Value="#F1EBF8"/>
  <Setter Property="BorderBrush" Value="#DDD2E9"/><Setter Property="BorderThickness" Value="1"/>
  <Setter Property="Padding" Value="12,7"/><Setter Property="MinHeight" Value="30"/><Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
   <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}">
    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
   </Border><ControlTemplate.Triggers>
    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="#E8DDF4"/><Setter Property="Foreground" Value="#49365E"/></Trigger>
    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#8966AE"/></Trigger>
    <Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Background" Value="#DACAEA"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.55"/></Trigger>
    <MultiTrigger><MultiTrigger.Conditions><Condition Property="Tag" Value="primary"/><Condition Property="IsMouseOver" Value="True"/></MultiTrigger.Conditions><Setter TargetName="Surface" Property="Background" Value="#63437F"/></MultiTrigger>
    <MultiTrigger><MultiTrigger.Conditions><Condition Property="Tag" Value="primary"/><Condition Property="IsPressed" Value="True"/></MultiTrigger.Conditions><Setter TargetName="Surface" Property="Background" Value="#533566"/></MultiTrigger>
   </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="TextBox">
  <Setter Property="Foreground" Value="#342B3E"/><Setter Property="Background" Value="White"/>
  <Setter Property="CaretBrush" Value="#79549C"/><Setter Property="SelectionBrush" Value="#D9C9ED"/>
  <Setter Property="BorderBrush" Value="#DDD5E7"/><Setter Property="BorderThickness" Value="1"/>
  <Setter Property="Padding" Value="10"/><Setter Property="FontSize" Value="13"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
   <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9" Padding="{TemplateBinding Padding}">
    <ScrollViewer x:Name="PART_ContentHost" Focusable="False"/>
   </Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#9672BA"/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="MenuItem">
  <Setter Property="FontFamily" Value="Microsoft YaHei UI"/><Setter Property="FontSize" Value="12.5"/>
  <Setter Property="Foreground" Value="#342B3E"/><Setter Property="Opacity" Value="1"/>
  <Setter Property="Background" Value="Transparent"/><Setter Property="MinHeight" Value="30"/>
  <Setter Property="Padding" Value="8,5"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
   <Grid><Border x:Name="Surface" Background="{TemplateBinding Background}" CornerRadius="7" Padding="{TemplateBinding Padding}">
    <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="18"/><ColumnDefinition Width="*"/><ColumnDefinition Width="16"/></Grid.ColumnDefinitions>
     <TextBlock x:Name="Check" Text="✓" Visibility="Hidden" VerticalAlignment="Center" Foreground="#79549C"/>
     <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center" RecognizesAccessKey="True"/>
     <TextBlock x:Name="Arrow" Grid.Column="2" Text="›" Visibility="Hidden" HorizontalAlignment="Right" VerticalAlignment="Center" Foreground="#9A86AE"/>
    </Grid>
   </Border>
   <Popup x:Name="PART_Popup" Placement="Right" HorizontalOffset="-2" VerticalOffset="-6" AllowsTransparency="True" Focusable="False" IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" PopupAnimation="None">
    <Border Background="#FFFDFCFF" BorderBrush="#DED5E8" BorderThickness="1" CornerRadius="11" Padding="5" MinWidth="190"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/></Border>
   </Popup></Grid>
   <ControlTemplate.Triggers>
    <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Surface" Property="Background" Value="#EEE6F7"/></Trigger>
    <Trigger Property="IsSubmenuOpen" Value="True"><Setter TargetName="Surface" Property="Background" Value="#EEE6F7"/></Trigger>
    <Trigger Property="HasItems" Value="True"><Setter TargetName="Arrow" Property="Visibility" Value="Visible"/></Trigger>
    <Trigger Property="IsChecked" Value="True"><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#8A7C98"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="Separator"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Height="1" Background="#EAE3F0" Margin="10,5"/></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>
""");

    private ContextMenu CreatePetMenu()
    {
        var menu = new ContextMenu { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(52,43,62)), Opacity = 1, MinWidth = 200, MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 40), UseLayoutRounding = true };
        menu.Resources = ChromeResources();
        menu.Template = (ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
 <Border Background="#FFFDFCFF" BorderBrush="#DED5E8" BorderThickness="1" CornerRadius="12" Padding="5">
  <ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/></ScrollViewer>
 </Border>
</ControlTemplate>
""");
        return menu;
    }

    private void InitializeSpeech(Control native)
    {
        native.Opacity = 0; native.IsHitTestVisible = false;
        var close = new Button { Content = "×", Width = 22, MinHeight = 20, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(133,113,151)), FontSize = 16, VerticalAlignment = VerticalAlignment.Top };
        close.Click += (_, _) => HideSpeech();
        var text = new Grid(); text.ColumnDefinitions.Add(new ColumnDefinition()); text.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        text.Children.Add(speechText); Grid.SetColumn(close, 1); text.Children.Add(close);
        speechText.FontFamily = new FontFamily("Microsoft YaHei UI");
        var bubble = new Border { Width = 236, Background = new SolidColorBrush(Color.FromRgb(253,250,255)), BorderBrush = new SolidColorBrush(Color.FromRgb(219,206,233)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(13,10,7,11), Child = text, Resources = ChromeResources() };
        speech.Child = bubble; speech.PlacementTarget = this;
        speechTimer.Tick += (_, _) => HideSpeech();
        if (native.FindName("TText") is TextBox original)
            original.TextChanged += (_, _) =>
            {
                if (disposed || !IsVisible || pointerPressed || pointerDragging || original.Text.Length == 0) return;
                speechText.Text = Short(original.Text, 42); speech.IsOpen = true;
                speechTimer.Stop(); speechTimer.Start();
            };
    }
    private void HideSpeech() { speechTimer.Stop(); speech.IsOpen = false; }

    private void PetPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (!ready || e.ChangedButton != MouseButton.Left) return;
        Point point = e.GetPosition(this);
        if (point.Y < 0 || point.Y > ActualHeight) return;
        pointerAnchor = point; pointerPressed = true; pointerPaused = true;
        if (pet?.DisplayType.Type == GraphType.Move) StopAutonomousControls();
        HideSpeech(); CaptureMouse(); e.Handled = true;
    }
    private void PetPointerMove(object sender, MouseEventArgs e)
    {
        if (!pointerPressed || pointerDragging || e.LeftButton != MouseButtonState.Pressed) return;
        Vector delta = e.GetPosition(this) - pointerAnchor;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        e.Handled = true;
        StartPetDrag(delta, DragMove);
    }
    private void StartPetDrag(Vector delta, Action systemDrag)
    {
        pointerPressed = false; pointerDragging = true;
        StopAutonomousControls(); HideSpeech();
        // Preserve the first movement; ZoomRatio scales the artwork, not input.
        Left += delta.X; Top += delta.Y; Clamp();
        ReleaseMouseCapture(); Cursor = Cursors.SizeAll;
        dragStarts++;
        pet?.Display(GraphType.Raised_Static, AnimatType.B_Loop, pet.DisplayBLoopingForce);
        try { systemDrag(); } catch (InvalidOperationException) { }
        finally
        {
            FinishPetDrag();
        }
    }
    private void PetPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!pointerPressed || e.ChangedButton != MouseButton.Left) return;
        pointerPressed = false; pointerPaused = false; pointerClicks++; ReleaseMouseCapture(); e.Handled = true;
        if (pointerAnchor.Y >= settings.Size) ShowStatus();
        else if (pointerAnchor.Y < settings.Size*.56) TouchHead(); else TouchBody();
    }
    private void PetPointerCaptureLost(object sender, MouseEventArgs e)
    {
        if (!pointerPressed || pointerDragging) return;
        pointerPressed = false; pointerPaused = false; ApplyAutonomy();
    }
    private void FinishPetDrag()
    {
        dragEnds++; pointerDragging = false; pointerPaused = false; Cursor = Cursors.Arrow; Clamp(); SaveSettings();
        Wake(() => { if (pendingCompletion) { pendingCompletion = false; CelebrateCompletion(); } else ResumeDefault(); });
    }
}
