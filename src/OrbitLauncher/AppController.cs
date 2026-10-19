using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace OrbitLauncher;
public sealed class AppController : IDisposable
{
    readonly Application app;
    readonly ConfigStore store=new();
    readonly RadialWindow radial=new();
    readonly InputHooks hooks;
    readonly TrayIcon tray;
    readonly DispatcherTimer previewTimer;
    readonly DispatcherTimer runtimeTimer;
    readonly Queue<object> runtimeHistory=new();
    SettingsWindow? settingsWindow;
    bool disposed;
    LaunchItem[] activeItems=Array.Empty<LaunchItem>();
    public Settings Config { get; }
    public bool HasConfig { get; }
    internal bool TrayVisible=>tray.IsRegistered;
    internal bool TrayRegistered=>tray.IsRegistered;
    internal bool IsExiting { get; private set; }
    internal bool IsTesting { get=>hooks.TestMode;set=>hooks.TestMode=value; }
    internal bool TrayLocated=>tray.IsRegistered && tray.CanLocateIcon();
    internal int TrayError=>tray.RegistrationError;
    internal bool TrayHasExit=>tray.HasExitMenuItem();
    internal bool TrayRemoved=>tray.Removed;
    internal string InputStatus=>$"运行权限：{ProcessIntegrity.Label}\n"+hooks.StatusText;
    internal string TrayStatus=>tray.IsRegistered?(tray.CanLocateIcon()?"系统托盘：已注册（图标可能被收进任务栏 ^）":"系统托盘：注册返回成功，但未查询到图标位置"):$"系统托盘：注册失败，参考信息 {tray.RegistrationError}";
    object RuntimeSnapshot()=>new {CollectedUtc=DateTime.UtcNow.ToString("O"),Version=typeof(Program).Assembly.GetName().Version?.ToString(),OperatingSystem=Environment.OSVersion.VersionString,ProcessId=Environment.ProcessId,ProcessPath=Environment.ProcessPath,IntegrityLevel=ProcessIntegrity.Level,IntegrityLabel=ProcessIntegrity.Label,TrayRegistered=tray.IsRegistered,TrayLocated=tray.IsRegistered&&tray.CanLocateIcon(),TrayError=tray.RegistrationError,TrayRegistrationAttempts=tray.RegistrationAttempts,ItemCount=Config.Items.Count,Modifier=Config.Modifier,Foreground=ForegroundGuard.CaptureForeground(),Input=hooks.Snapshot};
    internal void SaveRuntimeStatus()
    {
        try { var sample=RuntimeSnapshot();runtimeHistory.Enqueue(sample);while(runtimeHistory.Count>20)runtimeHistory.Dequeue();File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.FilePath)!,"runtime-status.json"),JsonSerializer.Serialize(sample,new JsonSerializerOptions {WriteIndented=true})); }catch { }
    }
    internal string ExportDiagnostics()
    {
        SaveRuntimeStatus();var path=Path.Combine(Path.GetDirectoryName(store.FilePath)!,"一划诊断.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new {Current=RuntimeSnapshot(),RecentRuntime=runtimeHistory.ToArray()},new JsonSerializerOptions{WriteIndented=true}));return path;
    }
    internal bool WheelVisible=>radial.IsVisible;
    internal GesturePhase GesturePhase=>hooks.Phase;
    internal int LaunchAttempts { get; private set; }
    internal bool NativeInputTesting {get;set;}
    internal object InputSnapshot=>hooks.Snapshot;
    internal void TestDisconnectMouseHook()=>hooks.TestDisconnectMouseHook();
    internal void TestDisconnectKeyboardHook()=>hooks.TestDisconnectKeyboardHook();
    internal void TestStaleRightAltDown()=>hooks.TestStaleRightAltDown();
    internal void TestUnavailableAsyncState()=>hooks.TestUnavailableAsyncState();
    internal Action<LaunchItem>? TestLaunch {get;set;}
    internal RadialWindow TestWheel=>radial;
    internal (int X,int Y) TestHoldStart()
    {
        var screen=Native.ScreenAt(400,400).Work;int x=(screen.Left+screen.Right)/2,y=(screen.Top+screen.Bottom)/2;
        hooks.TestModifierDown();hooks.TestPointer(0x201,x,y);hooks.TestPointer(0x200,x+100,y);return(x,y);
    }
    internal void TestHoldRelease(int x,int y)=>hooks.TestPointer(0x202,x,y);
    internal void TestModifierUp()=>hooks.TestModifierUp();
    internal void TestEscape()=>hooks.TestEscape();
    internal void TestProtection(string? reason)=>hooks.TestProtection(reason);
    internal void TestModifierDown()=>hooks.TestModifierDown();
    internal GestureResult TestPointer(int message,int x,int y)=>hooks.TestPointer(message,x,y);
    internal void TestPointerBurst(params (int Message,int X,int Y)[] events)=>hooks.TestPointerBurst(events);
    internal bool InputResourcesReleased=>hooks.ResourcesReleased;
    internal void TestTrayExit()=>tray.Execute(TrayIcon.ExitCommand);
    public bool Paused { get=>hooks.Paused; set { hooks.Paused=value; if(value)hooks.Cancel(); UpdateTray();settingsWindow?.RefreshChecks(); } }
    public AppController(Application app):this(app,false) { }
    internal AppController(Application app,bool simulateTrayRegistrationFailure)
    {
        this.app=app; HasConfig=File.Exists(store.FilePath); Config=store.Load();Themes.Apply(Config.Theme);radial.Prepare(Config);
        hooks=new InputHooks(()=>Config,app.Dispatcher);
        hooks.Began+=(x,y)=> { previewTimer?.Stop(); activeItems=Config.Items.Select(i=>i.Copy()).ToArray(); radial.OpenAt(x,y,Config); };
        hooks.Moved+=(x,y)=>radial.Track(x,y);
        hooks.Ended+=commit=> { var indices=radial.Finish(commit);var items=indices.Where(i=>i>=0 && i<activeItems.Length).Select(i=>activeItems[i]).ToArray();radial.Dismiss();activeItems=Array.Empty<LaunchItem>();foreach(var item in items)Launch(item); };
        tray=new TrayIcon(TrayCommand,()=>Paused,()=>StartupService.Enabled,simulateTrayRegistrationFailure);
        UpdateTray();
        previewTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(3) }; previewTimer.Tick+=(_,_)=> { previewTimer.Stop(); if(!hooks.Busy)radial.Dismiss(); };
        runtimeTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(3)};
        runtimeTimer.Tick+=(_,_)=>SaveRuntimeStatus();runtimeTimer.Start();
        if(!HasConfig)store.Save(Config);
        SaveRuntimeStatus();
    }
    void TrayCommand(uint command)
    {
        switch(command)
        {
            case TrayIcon.SettingsCommand:ShowSettings();break;
            case TrayIcon.StartupCommand:SetStartup(!StartupService.Enabled);break;
            case TrayIcon.PauseCommand:Paused=!Paused;break;
            case TrayIcon.ExitCommand:Exit();break;
        }
    }
    void UpdateTray()=>tray.Update(Paused?"一划 · 手势已暂停 · 右键退出":"一划 · 正在运行 · 左键设置 / 右键退出");
    public void ShowRunningHint()=>tray.Balloon("一划已在后台运行","右下角青绿箭头图标：左键打开设置，右键可退出。若图标被 Windows 收起，请点任务栏的向上箭头 ^。");
    public void ShowSettings()
    {
        hooks.Cancel(); radial.Dismiss(true);
        if(settingsWindow==null) { settingsWindow=new SettingsWindow(this); settingsWindow.Closed+=(_,_)=>settingsWindow=null; }
        settingsWindow.RefreshChecks(); settingsWindow.Show(); if(settingsWindow.WindowState==WindowState.Minimized)settingsWindow.WindowState=WindowState.Normal; settingsWindow.Activate();
    }
    public void Apply()
    {
        hooks.Cancel(); Config.Normalize();store.Save(Config);Themes.Apply(Config.Theme);hooks.UpdateSettings(Config);radial.Prepare(Config);
        UpdateTray();
    }
    public void Launch(LaunchItem item) { LaunchAttempts++;if((IsTesting || NativeInputTesting) && TestLaunch!=null){TestLaunch(item.Copy());return;}var error=Launcher.Open(item); if(error!=null)Notify(error); }
    void Notify(string text) { if(tray.IsRegistered)tray.Balloon("一划",text,true);else MessageBox.Show(text,"一划",MessageBoxButton.OK,MessageBoxImage.Warning); }
    public void ShowWarning() { if(store.Warning!=null)Notify(store.Warning); }
    public void SetStartup(bool enabled)
    { try { StartupService.Set(enabled); } catch(Exception ex) { Notify("无法修改开机自启动："+ex.Message); } settingsWindow?.RefreshChecks(); }
    public void Preview() { hooks.Cancel(); Native.GetCursorPos(out var p); radial.OpenAt(p.X,p.Y,Config); previewTimer.Stop(); previewTimer.Start(); }
    public void Recover() { hooks.Cancel(); radial.Dismiss(true); }
    public void Exit() { IsExiting=true;app.Shutdown(); }
    public void Dispose()
    {
        if(disposed)return; disposed=true;
        settingsWindow?.Flush(); previewTimer.Stop();runtimeTimer.Stop(); hooks.Dispose(); radial.Close();tray.Dispose();
    }
}
