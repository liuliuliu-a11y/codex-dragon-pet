using System;
using System.IO;
using System.Text.Json;

namespace CodexVPet;

internal sealed class HostSettings
{
    public double Scale { get; set; } = .36;
    public double Size { get => Scale * 500; set => Scale = value / 500; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; } = true;
    public bool Roam { get; set; } = true;
    public bool Roaming { get => Roam; set => Roam = value; }
    public bool Quiet { get; set; }
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDragonPet");
    private static string FilePath => Path.Combine(Folder, "vpet-host.json");
    public static HostSettings Load()
    {
        try { return JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(FilePath)) ?? new HostSettings(); }
        catch { return new HostSettings(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, FilePath, true);
    }
}

internal sealed class BridgeState
{
    public string version { get; set; } = "";
    public bool connected { get; set; }
    public bool running { get; set; }
    public bool finishing { get; set; }
    public string title { get; set; } = "";
    public string stage { get; set; } = "";
    public string message { get; set; } = "";
    public string completedId { get; set; } = "";
    public long completionSerial { get; set; }
    public double? quotaUsed { get; set; }
    public int? quotaMinutes { get; set; }
    public DateTime updatedAt { get; set; }
}
