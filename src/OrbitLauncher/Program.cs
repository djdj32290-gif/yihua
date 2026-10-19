using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace OrbitLauncher;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // This isolated test window owns no configuration, tray icon or input hooks.
        // Handle it before the production single-instance objects are created.
        if(args.Length==2 && args[0]=="--input-test-target" && Guid.TryParseExact(args[1],"N",out _))return SelfTests.RunInputTestTarget(args[1]);
        if(Array.Exists(args,a=>a.StartsWith("--verify-",StringComparison.Ordinal) || a=="--self-test" || a=="--benchmark-render" || a=="--export-preview"))WebsiteIcons.AutomaticLoading=false;
        if(Array.IndexOf(args,"--verify-web-icons")>=0)return SelfTests.VerifyWebsiteIconNetwork(args);
        if(Array.IndexOf(args,"--self-test")>=0) return SelfTests.Run(args);
        if(Array.IndexOf(args,"--verify-services")>=0) return SelfTests.VerifyServices(args);
        if(Array.IndexOf(args,"--export-preview")>=0) return SelfTests.ExportPreview(args);
        if(Array.IndexOf(args,"--benchmark-render")>=0) return SelfTests.BenchmarkRender(args);
        bool verification=Array.Exists(args,a=>a.StartsWith("--verify-",StringComparison.Ordinal));
        if(!verification && Array.IndexOf(args,"--exit")<0 && ProcessIntegrity.Level is >=0 and <8192)
        {
            MessageBox.Show("当前文件夹限制了程序权限，Windows 因此阻止一划连接系统托盘。\n\n请退出后，从 ZIP 完整解压到普通目录（例如 D:\\Apps\\一划）再双击运行。不要继续在当前受限目录启动。\n\n当前程序："+Environment.ProcessPath,"一划 · 请更换解压目录",MessageBoxButton.OK,MessageBoxImage.Information);
            return 2;
        }
        try { return RunDesktop(args); }
        catch(UnauthorizedAccessException)
        {
            MessageBox.Show("无法连接已经运行的一划实例。请先从托盘退出原来以管理员权限运行的版本，再双击启动这一版。","一划",MessageBoxButton.OK,MessageBoxImage.Information);
            return 1;
        }
    }
    static int RunDesktop(string[] args)
    {
        string identity=WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\OrbitLauncher.Show."+identity);
        using var exitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\OrbitLauncher.Exit."+identity);
        if(Array.IndexOf(args,"--exit")>=0) { exitSignal.Set(); return 0; }
        using var mutex=new Mutex(true,@"Local\OrbitLauncher.Instance."+identity,out bool first);
        if(!first) { signal.Set(); return 0; }
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/OrbitLauncher;component/Controls.xaml",UriKind.Relative)});
        AppController? controller=null;
        app.DispatcherUnhandledException+=(_,e)=>
        {
            try { controller?.Recover(); Directory.CreateDirectory(Path.GetDirectoryName(new ConfigStore().FilePath)!); File.AppendAllText(Path.Combine(Path.GetDirectoryName(new ConfigStore().FilePath)!,"error.log"),DateTime.Now+" "+e.Exception+Environment.NewLine); } catch { }
            MessageBox.Show("操作未完成："+e.Exception.Message,"一划",MessageBoxButton.OK,MessageBoxImage.Warning); e.Handled=true;
        };
        RegisteredWaitHandle? waiter=null;
        RegisteredWaitHandle? exitWaiter=null;
        try
        {
            app.Startup+=(_,_)=>
            {
                try
                {
                    bool trayFailureTest=Array.Exists(args,a=>a is "--verify-tray-failure" or "--verify-tray-failure-save");
                    controller=new AppController(app,trayFailureTest);
                    waiter=ThreadPool.RegisterWaitForSingleObject(signal,(_,_)=>app.Dispatcher.BeginInvoke(()=>controller.ShowSettings()),null,Timeout.Infinite,false);
                    exitWaiter=ThreadPool.RegisterWaitForSingleObject(exitSignal,(_,_)=>app.Dispatcher.BeginInvoke(()=>controller.Exit()),null,Timeout.Infinite,false);
                    bool nativeInputTest=Array.IndexOf(args,"--verify-native-input")>=0;
                    controller.NativeInputTesting=nativeInputTest;
                    controller.IsTesting=!nativeInputTest && Array.Exists(args,a=>a.StartsWith("--verify-",StringComparison.Ordinal));
                    if(!nativeInputTest && !controller.IsTesting && (!controller.HasConfig || !controller.TrayVisible || Array.IndexOf(args,"--background")<0 && Array.IndexOf(args,"--settings")>=0))controller.ShowSettings();
                    controller.ShowWarning();
                    if(!nativeInputTest && !trayFailureTest && Array.IndexOf(args,"--verify-ui")<0 && Array.IndexOf(args,"--verify-hold")<0)controller.ShowRunningHint();
                    if(nativeInputTest)SelfTests.BeginNativeInputChecks(app,controller,args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"native-input-test.json"));
                    if(Array.IndexOf(args,"--verify-ui")>=0)SelfTests.BeginUiChecks(app,controller,args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"ui-test.json"));
                    if(Array.IndexOf(args,"--verify-hold")>=0)SelfTests.BeginHoldChecks(app,controller,args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"hold-test.json"));
                    if(trayFailureTest)SelfTests.BeginTrayFailureChecks(app,controller,args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"tray-failure-test.json"),Array.IndexOf(args,"--verify-tray-failure-save")>=0);
                }
                catch(Exception ex) { MessageBox.Show("一划无法启动："+ex.Message,"一划",MessageBoxButton.OK,MessageBoxImage.Error); app.Shutdown(1); }
            };
            app.Exit+=(_,_)=> { waiter?.Unregister(null); exitWaiter?.Unregister(null); controller?.Dispose(); };
            return app.Run();
        }
        finally { mutex.ReleaseMutex(); }
    }
}
