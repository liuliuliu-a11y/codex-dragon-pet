using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Program
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Assembly Host = Assembly.Load("CodexVPet");
    private static readonly Type WindowType = Host.GetType("CodexVPet.CompanionWindow")!;
    private static int passed;
    private const string ChatFixture = "{\"protocol\":1,\"messages\":[{\"role\":\"user\",\"text\":\"今天我们一起把桌宠做好。\"},{\"role\":\"assistant\",\"text\":\"好。我会在这里陪你，想聊的时候点我就好。\"}]}";
    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(ChatFixture)});
    }
    private static object Field(object obj, string name) => obj.GetType().GetField(name, Flags)!.GetValue(obj)!;
    private static void Set(object obj, string name, object? value) => obj.GetType().GetField(name, Flags)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Flags)!.Invoke(obj, args);
    private static T Property<T>(object obj, string name) => (T)obj.GetType().GetProperty(name, Flags)!.GetValue(obj)!;
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static async Task Wait(Func<bool> condition, string label)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if(watch.Elapsed.TotalSeconds>15) throw new Exception("Timeout: "+label); await Task.Delay(30); }
    }
    private static byte[] Capture(FrameworkElement element, string path, double scale=2)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(element.ActualWidth*scale)),Math.Max(1,(int)Math.Ceiling(element.ActualHeight*scale)),96*scale,96*scale,PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(path)) encoder.Save(file);
        var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0); return bytes;
    }
    [STAThread]
    public static int Main(string[] args)
    {
        string evidence=Path.GetFullPath(args.Length>0?args[0]:Path.Combine(AppContext.BaseDirectory,"verification")); Directory.CreateDirectory(evidence);
        string profile=Path.Combine(evidence,"profile"); Directory.CreateDirectory(profile);
        var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
        foreach(string name in new[]{"Theme.xaml","basestyle.xaml"}) app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/VPet-Simulator.Core;component/Display/"+name)});
        var window=(Window)Activator.CreateInstance(WindowType,Flags,null,new object[]{profile,true,evidence},null)!;
        window.Title="白龙 · 交互验收"; window.ShowInTaskbar=true;
        int result=0;
        window.Loaded+=async (_,_)=>
        {
            try
            {
                await Wait(()=>(bool)Field(window,"ready"),"native ready");
                ((DispatcherTimer)Field(window,"demoTimer")).Stop();
                object player=Field(window,"smoothSprites");
                var forms=new[]{("gpt-6.1-sol","low","low"),("gpt-6.1-sol","medium","medium"),("gpt-6.1-sol","high","high"),("gpt-6.1-sol","xhigh","xhigh"),("gpt-6.1-sol","ultra","ultra"),("gpt-6-astra","ultra","astra")};
                byte[]? previous=null;
                foreach(var form in forms)
                {
                    File.WriteAllText(Path.Combine(profile,"vpet-status.json"),JsonSerializer.Serialize(new{model=form.Item1,reasoningEffort=form.Item2,updatedAt=DateTime.UtcNow,connected=true,running=false,title="隔离形态验收",stage="形态切换验收"}));
                    Call(window,"ReadState"); await Task.Delay(750);
                    Check(Property<string>(player,"Form")==form.Item3,"actual ReadState selects "+form.Item3);
                    byte[] pixels=Capture((FrameworkElement)Field(window,"scene"),Path.Combine(evidence,"form-"+form.Item3+".png"));
                    if(previous!=null){double difference=previous.Zip(pixels,(a,b)=>Math.Abs(a-b)).Average(); Check(difference>1.5,"rendered character visibly changes to "+form.Item3+" (pixel delta "+difference.ToString("0.00")+")");}
                    previous=pixels;
                }
                File.WriteAllText(Path.Combine(profile,"vpet-status.json"),JsonSerializer.Serialize(new{model="gpt-6.1-sol",reasoningEffort="low",updatedAt=DateTime.UtcNow})); Call(window,"ReadState");
                File.WriteAllText(Path.Combine(profile,"vpet-status.json"),JsonSerializer.Serialize(new{model="gpt-6-astra",reasoningEffort="ultra",updatedAt=DateTime.UtcNow})); Call(window,"ReadState"); await Task.Delay(700);
                Check(Property<string>(player,"Form")=="astra","rapid bridge snapshots cannot restore the previous model");
                var menu=(ContextMenu)Call(window,"BuildMenu")!;
                menu.PlacementTarget=window; menu.Placement=PlacementMode.Right; menu.IsOpen=true; await Task.Delay(200);
                Check(menu.ActualHeight<350,"top-level menu fits below 350 logical pixels");
                Check(menu.Items.OfType<MenuItem>().Count(i=>i.IsEnabled)<=8,"top level exposes at most eight actionable entries");
                Check(menu.Opacity==1,"context menu remains opaque and readable");
                Capture(menu,Path.Combine(evidence,"menu.png"));
                var interaction=menu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"更多互动"));
                interaction.IsSubmenuOpen=true; await Task.Delay(250);
                Check(interaction.Items.OfType<MenuItem>().Count()==6,"all six extra interactions remain reachable");
                menu.IsOpen=false;
                var snapshotType=Host.GetType("CodexVPet.LifeSnapshot")!;
                Set(window,"lifeHttp",new HttpClient(new FixtureHandler()));
                Set(window,"lifeSnapshot",JsonSerializer.Deserialize(ChatFixture,snapshotType));
                Set(window,"lifeOnline",true); Call(window,"OpenLifeChat"); await Task.Delay(250);
                var chat=(Window)Field(window,"lifeChat");
                Capture(chat,Path.Combine(evidence,"chat.png"));
                Check(chat.Resources.Contains(typeof(Button)) && chat.Resources.Contains(typeof(TextBox)),"chat controls use their own readable styles");
                Check(((StackPanel)Field(window,"lifeHistory")).Children.Count==2,"chat restyling preserves both message rows");
                chat.Close();
                var pet=(VPet_Simulator.Core.Main)Field(window,"pet");
                var native=(Control)pet.MsgBar.This;
                ((TextBox)native.FindName("TText")).Text="这是一条较长的测试消息。气泡会放在角色上方，完整回复仍保留在聊天窗口中，不会挡住小龙的身体。";
                await Task.Delay(120);
                var speech=(Popup)Field(window,"speech");
                Check(speech.IsOpen && speech.Placement==PlacementMode.Top,"compact speech appears above the character");
                Check(native.Opacity==0 && !native.IsHitTestVisible,"old body-covering speech cannot intercept dragging");
                Check(((TextBlock)Field(window,"speechText")).Text.Length<=43,"long speech is summarized within the bubble");
                Capture((FrameworkElement)speech.Child,Path.Combine(evidence,"bubble.png")); Call(window,"HideSpeech");
                Set(window,"pointerDragging",true); Call(window,"CelebrateCompletion");
                Check((bool)Field(window,"pendingCompletion"),"completion waits while the user is dragging");
                Set(window,"pointerDragging",false); Set(window,"pendingCompletion",false);
                Call(window,"Wake",new object?[]{null}); await Task.Delay(600);
                Call(window,"SaveEvidence","chrome-accepted");
                double beforeX=window.Left, beforeY=window.Top;
                int clicks=(int)Field(window,"pointerClicks");
                Call(window,"StartPetDrag",new Vector(80,40),(Action)(()=>
                {
                    Check(Math.Abs(window.Left-beforeX-80)<1 && Math.Abs(window.Top-beforeY-40)<1,"first drag delta is preserved without multiplying by avatar scale");
                    Check((bool)Field(window,"pointerDragging"),"drag owns input until the system movement finishes");
                }));
                await Task.Delay(700);
                Check(!(bool)Field(window,"pointerDragging") && (int)Field(window,"dragStarts")==1 && (int)Field(window,"dragEnds")==1,"drag controller releases and lands exactly once");
                Check((int)Field(window,"pointerClicks")==clicks,"drag controller never fires a petting click");
                var graphs=(System.Collections.Concurrent.ConcurrentDictionary<string,int>)Field(window,"observedGraphs");
                int landBefore=graphs.GetValueOrDefault("Raised_Static:dangle:C_End"),celebrateBefore=graphs.GetValueOrDefault("Common:celebrate:Single");
                Call(window,"StartPetDrag",new Vector(0,0),(Action)(()=>Call(window,"CelebrateCompletion")));
                await Task.Delay(1100);
                Check(graphs.GetValueOrDefault("Raised_Static:dangle:C_End")==landBefore+1,"queued completion plays landing only once");
                Check(graphs.GetValueOrDefault("Common:celebrate:Single")==celebrateBefore+1,"queued completion starts one celebration after landing");
                if(Array.IndexOf(args,"--interactive")>=0)
                {
                    int dragBaseline=(int)Field(window,"dragStarts"),endBaseline=(int)Field(window,"dragEnds"),clickBaseline=(int)Field(window,"pointerClicks");
                    window.Left=100; window.Top=180; window.Activate();
                    File.WriteAllText(Path.Combine(evidence,"interactive-ready.json"),JsonSerializer.Serialize(new{left=window.Left,top=window.Top,width=window.Width,height=window.Height,dragStarts=(int)Field(window,"dragStarts"),pointerClicks=(int)Field(window,"pointerClicks")}));
                    var watch=Stopwatch.StartNew();
                    while(!File.Exists(Path.Combine(evidence,"finish-interactive")) && watch.Elapsed.TotalMinutes<5)
                    {Call(window,"SaveEvidence","interactive"); await Task.Delay(500);}
                    Capture((FrameworkElement)Field(window,"scene"),Path.Combine(evidence,"after-drag.png"));
                    Check((int)Field(window,"dragStarts")>dragBaseline && (int)Field(window,"dragEnds")>endBaseline,"real mouse drag starts and releases cleanly");
                    Check(Math.Abs(window.Left-100)>20 || Math.Abs(window.Top-180)>20,"real drag visibly moves the window");
                    Check((int)Field(window,"pointerClicks")==clickBaseline,"drag never fires a click or petting action");
                }
                Check(Property<string>(player,"LastError").Length==0,"UI changes preserve animation decoding");
            }
            catch(Exception ex){result=1;Console.WriteLine(ex);Call(window,"SaveEvidence","chrome-failed");}
            finally{window.Close();}
        };
        app.Run(window);
        File.WriteAllText(Path.Combine(evidence,"result.json"),JsonSerializer.Serialize(new{passed,failed=result}));
        Console.WriteLine($"{passed} chrome checks passed; result {result}.");return result;
    }
}
