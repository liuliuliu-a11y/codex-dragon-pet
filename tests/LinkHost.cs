using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

// A disposable two-window desktop host for the native activation integration test.
public static class LinkHost
{
    [STAThread]
    public static void Main(string[] args)
    {
        string folder = args[0];
        var first = new Form { Text = "Dragon link test - first window", Width = 360, Height = 100 };
        Form second = null;
        first.Shown += delegate { Mark(folder, "first", first); };
        var timer = new Timer { Interval = 100 };
        timer.Tick += delegate
        {
            try
            {
                string command = File.ReadAllText(Path.Combine(folder, "command.txt")).Trim();
                if (command == "second" && second == null)
                {
                    second = new Form { Text = "Dragon link test - second window", Width = 360, Height = 100 };
                    second.Shown += delegate { Mark(folder, "second", second); };
                    second.Show(); second.Activate(); first.Hide();
                }
                if (command == "quit") { timer.Stop(); Application.Exit(); }
            }
            catch (IOException) { }
        };
        timer.Start(); Application.Run(first); timer.Dispose();
    }
    private static void Mark(string folder, string name, Form form)
    {
        File.WriteAllText(Path.Combine(folder, name + ".txt"),
            Process.GetCurrentProcess().Id + ":" + form.Handle.ToInt64());
    }
}
