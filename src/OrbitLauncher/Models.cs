using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrbitLauncher;

public sealed class LaunchItem
{
    public string Name { get; set; } = "新选项";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public LaunchItem Copy() => (LaunchItem)MemberwiseClone();
}
public sealed class Settings
{
    public int Diameter { get; set; } = 420;
    public int CancelRadius { get; set; } = 58;
    public int DragThreshold { get; set; } = 12;
    public string Modifier { get; set; } = "LeftAlt";
    public string Theme {get;set;}="Obsidian";
    public bool GameModeEnabled { get; set; } = true;
    public bool PauseInFullscreen { get; set; } = true;
    public List<string> BlockedApplications { get; set; } = new() {"League of Legends.exe","LeagueClient.exe","LeagueClientUx.exe"};
    public Dictionary<string,string> ApplicationIconPaths {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public List<LaunchItem> Items { get; set; } = new();
    public Settings Copy() => new() { Diameter=Diameter,CancelRadius=CancelRadius,DragThreshold=DragThreshold,Modifier=Modifier,Theme=Theme,GameModeEnabled=GameModeEnabled,PauseInFullscreen=PauseInFullscreen,BlockedApplications=BlockedApplications.ToList(),ApplicationIconPaths=new(ApplicationIconPaths,StringComparer.OrdinalIgnoreCase),Items=Items.Select(x=>x.Copy()).ToList() };
    public void Normalize()
    {
        Diameter = Math.Clamp(Diameter, 300, 680);
        CancelRadius = Math.Clamp(CancelRadius, 28, Math.Min(110, Diameter / 4));
        DragThreshold = Math.Clamp(DragThreshold, 4, 60);
        Theme=Themes.Normalize(Theme);
        if (Modifier != "LeftAlt" && Modifier != "LeftControl" && Modifier != "LeftShift") Modifier = "LeftAlt";
        BlockedApplications=(BlockedApplications ?? new()).Select(ForegroundGuard.NormalizeName).Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToList();
        var iconPaths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in ApplicationIconPaths ?? new()) {var name=ForegroundGuard.NormalizeName(pair.Key);if(BlockedApplications.Contains(name,StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))iconPaths[name]=pair.Value;}
        ApplicationIconPaths=iconPaths;
        Items = (Items ?? new()).Where(x => x != null).Take(16).ToList();
        foreach (var i in Items) { i.Name ??= "新选项"; i.Target ??= ""; i.Arguments ??= ""; i.WorkingDirectory ??= ""; }
    }
    public static Settings Defaults() => new() { Items = new() {
        new() { Name="文件资源管理器", Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") },
        new() { Name="记事本", Target=Path.Combine(Environment.SystemDirectory, "notepad.exe") },
        new() { Name="计算器", Target=Path.Combine(Environment.SystemDirectory, "calc.exe") },
        new() { Name="下载", Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") },
        new() { Name="文档", Target=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        new() { Name="图片", Target=Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
        new() { Name="桌面", Target=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
        new() { Name="用户文件夹", Target=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
    }};
}
public sealed class ConfigStore
{
    public string FilePath { get; }
    public string? Warning { get; private set; }
    public ConfigStore(string? path = null) => FilePath = path ?? DefaultPath();
    static string DefaultPath()
    {
        var portable=Path.Combine(AppContext.BaseDirectory,"Data");
        try { Directory.CreateDirectory(portable); var probe=Path.Combine(portable,".write-test"); File.WriteAllText(probe,""); File.Delete(probe); return Path.Combine(portable,"settings.json"); }
        catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OrbitLauncher","settings.json"); }
    }
    public Settings Load()
    {
        if (!File.Exists(FilePath)) return Settings.Defaults();
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException(); s.Normalize(); return s; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        { Warning = "配置无法读取，已使用默认配置。原文件保留为 settings.json.broken。"; try { File.Copy(FilePath, FilePath + ".broken", true); } catch { } return Settings.Defaults(); }
    }
    public void Save(Settings settings)
    {
        settings.Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
public static class RadialMath
{
    public static int Select(double dx, double dy, double cancelRadius, int count)
    {
        if (count == 0 || dx * dx + dy * dy <= cancelRadius * cancelRadius) return -1;
        var angle = (Math.Atan2(dy, dx) + Math.PI / 2 + Math.PI * 2) % (Math.PI * 2);
        return (int)Math.Floor(angle / (Math.PI * 2 / count) + .5) % count;
    }
    public static double ClampCenter(double value, double start, double end, double half) => end - start < half * 2 ? (start + end) / 2 : Math.Clamp(value, start + half, end - half);
}

// Pure state machine: native input is handled by InputHooks, UI by the controller.
public enum GesturePhase { Idle, Pending, Active, Draining }
public enum GestureAction { None, Begin, Update, Commit, Cancel, ReplayClick, ReplayDown }
public readonly record struct GestureResult(bool Block, GestureAction Action = GestureAction.None);
public sealed class GestureMachine
{
    public static bool Eligible(bool modifierDown,bool rightAltDown,bool paused,int count)=>modifierDown && !rightAltDown && !paused && count>0;
    volatile GesturePhase phase;
    public GesturePhase Phase { get=>phase; private set=>phase=value; }
    public int OriginX { get; private set; }
    public int OriginY { get; private set; }
    public GestureResult Down(int x, int y, bool eligible)
    {
        if (Phase != GesturePhase.Idle) return new(true);
        if (!eligible) return new(false);
        OriginX = x; OriginY = y; Phase = GesturePhase.Pending; return new(true);
    }
    public GestureResult Move(int x, int y, double threshold)
    {
        if (Phase == GesturePhase.Pending && Math.Pow(x - OriginX, 2) + Math.Pow(y - OriginY, 2) >= threshold * threshold)
        { Phase = GesturePhase.Active; return new(true, GestureAction.Begin); }
        return Phase switch { GesturePhase.Active => new(true, GestureAction.Update), GesturePhase.Pending or GesturePhase.Draining => new(true), _ => new(false) };
    }
    public GestureResult Up()
    {
        var p = Phase; Phase = GesturePhase.Idle;
        return p switch { GesturePhase.Pending => new(true, GestureAction.ReplayClick), GesturePhase.Active => new(true, GestureAction.Commit), GesturePhase.Draining => new(true), _ => new(false) };
    }
    public GestureResult ModifierUp()
    {
        if (Phase == GesturePhase.Pending) { Phase = GesturePhase.Idle; return new(false, GestureAction.ReplayDown); }
        if (Phase == GesturePhase.Active) { Phase = GesturePhase.Draining; return new(false, GestureAction.Cancel); }
        return new(false);
    }
    public GestureResult Cancel()
    {
        if (Phase is GesturePhase.Active or GesturePhase.Pending) { Phase = GesturePhase.Draining; return new(true, GestureAction.Cancel); }
        return new(false);
    }
    public GestureResult Reset()
    {
        bool busy=Phase!=GesturePhase.Idle; Phase=GesturePhase.Idle;
        return new(false,busy?GestureAction.Cancel:GestureAction.None);
    }
}
