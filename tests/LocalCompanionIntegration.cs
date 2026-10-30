using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexDragon;

public static class LocalCompanionIntegration
{
    private static int passed, failed, step;
    private static object sync = new object();
    private static IDictionary<string, object> snapshot;
    private static List<string> gestures = new List<string>();
    private static string posted = "";
    private static object Field(object value, string name) { return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }
    private static void Call(object value, string name, params object[] args) { value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, args); }
    private static void Check(bool value, string text) { Console.WriteLine((value ? "PASS " : "FAIL ") + text); if (value) passed++; else failed++; }
    private static IDictionary<string, object> State(string kind, string id, string text, string status)
    {
        return Json.Parse(Json.Write(new { protocol = 1, name = "小G", messages = new[] { new { id = id, role = "assistant", kind = kind, text = text } },
            jobs = status == "running" ? new[] { new { id = new string('a', 32), message = "在想怎么回复你" } } : new object[0],
            recent_jobs = new[] { new { id = new string('a',32), status = status, message = status == "error" ? "测试模型失败" : "已完成" } } }));
    }
    [STAThread]
    public static int Main(string[] args)
    {
        string evidence = Path.GetFullPath(args[0]); Directory.CreateDirectory(evidence);
        Environment.SetEnvironmentVariable("CODEX_PET_DATA_DIR", Path.Combine(evidence, "profile-" + Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(evidence, "empty-codex"));
        snapshot = State("chat", "old", "旧回复不应再次弹出", "done");
        LocalCompanionClient.VerificationTransport = delegate(string path, object body) {
            lock (sync) {
                if (path == "api/pet") return Json.Parse(Json.Write(snapshot));
                var request = Json.Parse(Json.Write(body));
                if (path == "api/pet/interaction") { gestures.Add(Json.Text(request,"kind")); return Json.Parse("{\"saved\":true}"); }
                posted = Json.Text(request,"message"); snapshot = State("chat","old","旧回复不应再次弹出","running");
                return Json.Parse("{\"job_id\":\""+new string('a',32)+"\"}");
            }
        };
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var pet = new PetWindow(true) { IsHitTestVisible = false };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        timer.Tick += delegate {
            try {
                step++;
                if (step == 1) Call(pet,"OpenLifeChat");
                if (step == 2) {
                    Check((bool)Field(pet,"lifeOnline"),"native chat connects to the shared companion protocol");
                    Check(!Convert.ToString(Field(pet,"speech")).Contains("旧回复"),"first connection does not replay old messages");
                    Check((Window)Field(pet,"lifeChat") != null,"real WPF chat window opens");
                    Call(pet,"SendLifeMessage","请记住这是一条隔离测试消息");
                }
                if (step == 3) {
                    Check(posted.Contains("隔离测试消息"),"native send passes the actual user message to the companion");
                    Check(!((Button)Field(pet,"lifeSend")).IsEnabled,"send is disabled while a shared job is active");
                    lock(sync) snapshot=State("chat","reply","收到，这是共享记忆的回复。","done"); Call(pet,"PollLife");
                }
                if (step == 4) {
                    Check(Convert.ToString(Field(pet,"speech")).Contains("共享记忆"),"assistant reply reaches the desktop speech bubble");
                    Check(Convert.ToString(Field(pet,"lifePending"))=="","finished job releases pending state");
                    Check(((StackPanel)Field(pet,"lifeMessages")).Children.Count==1,"chat history updates from shared state");
                    Call(pet,"SetQuiet",true);
                    lock(sync) snapshot=State("proactive","letter","安静模式下不应主动弹出这封信","done"); Call(pet,"PollLife");
                }
                if(step==5){
                    Check(!Convert.ToString(Field(pet,"speech")).Contains("不应主动"),"quiet mode suppresses unsolicited letters even with chat open");
                    ((Settings)Field(pet,"settings")).ReduceMotion=true; Call(pet,"StopAction");
                    lock(sync) snapshot=State("chat","reply2","减少动画仍然有文字回复","done"); Call(pet,"PollLife");
                }
                if(step==6){
                    Check(Convert.ToString(Field(pet,"speech")).Contains("减少动画"),"reduced motion retains direct text replies");
                    Check(((ActionPlayback)Field(pet,"action")).Current==PetAction.None,"reduced motion never starts a reply gesture");
                    Check(Settings.Load().LastLifeMessage=="reply2","message cursor survives saving and reloading without replay");
                    Call(pet,"Feed");
                }
                if(step==7){
                    lock(sync) Check(gestures.Contains("feed"),"manual feeding is recorded without any model generation");
                    lock(sync) snapshot=State("chat","failed","上一条旧回复","error");
                    pet.GetType().GetField("lifePending",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,new string('a',32)); Call(pet,"PollLife");
                }
                if(step==8){
                    Check(Convert.ToString(Field(pet,"lifeChatError")).Contains("模型失败"),"backend failure is shown rather than reported as a reply");
                    Call(pet,"SetLifeLink",false);
                    Check(!((DispatcherTimer)Field(pet,"lifeTimer")).IsEnabled,"pausing link stops its polling timer");
                    var chat=(Window)Field(pet,"lifeChat"); var root=(FrameworkElement)chat.Content;root.UpdateLayout();
                    var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(evidence,"native-chat.png")))encoder.Save(stream);
                    timer.Stop();pet.Close();Check((Window)Field(pet,"lifeChat")==null,"closing pet releases its native chat window");
                }
            } catch(Exception ex){Console.WriteLine(ex);failed++;timer.Stop();pet.Close();}
        };
        pet.Loaded+=delegate{timer.Start();};app.Run(pet);
        File.WriteAllText(Path.Combine(evidence,"result.json"),Json.Write(new{passed=passed,failed=failed}));
        Console.WriteLine(passed+" companion integration checks passed; "+failed+" failed.");return failed==0?0:1;
    }
}
