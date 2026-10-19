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
        if(Array.Exists(args,a=>a.StartsWith("--verify-",StringComparison.Ordinal) || a=="--self-test" || a=="--benchmark-render" || a=="--export-preview"))WebsiteIcons.AutomaticLoading=false;
        if(Array.IndexOf(args,"--verify-web-icons")>=0)return SelfTests.VerifyWebsiteIconNetwork(args);
        if(Array.IndexOf(args,"--self-test")>=0) return SelfTests.Run(args);
        if(Array.IndexOf(args,"--verify-services")>=0) return SelfTests.VerifyServices(args);
        if(Array.IndexOf(args,"--export-preview")>=0) return SelfTests.ExportPreview(args);
        if(Array.IndexOf(args,"--benchmark-render")>=0) return SelfTests.BenchmarkRender(args);
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
                    controller=new AppController(app);
                    waiter=ThreadPool.RegisterWaitForSingleObject(signal,(_,_)=>app.Dispatcher.BeginInvoke(()=>controller.ShowSettings()),null,Timeout.Infinite,false);
                    exitWaiter=ThreadPool.RegisterWaitForSingleObject(exitSignal,(_,_)=>app.Dispatcher.BeginInvoke(()=>controller.Exit()),null,Timeout.Infinite,false);
                    controller.IsTesting=Array.Exists(args,a=>a.StartsWith("--verify-",StringComparison.Ordinal));
                    if(!controller.IsTesting && (!controller.HasConfig || !controller.TrayVisible || Array.IndexOf(args,"--background")<0 && Array.IndexOf(args,"--settings")>=0))controller.ShowSettings();
                    controller.ShowWarning();
                    if(Array.IndexOf(args,"--verify-ui")<0 && Array.IndexOf(args,"--verify-hold")<0)controller.ShowRunningHint();
                    if(Array.IndexOf(args,"--verify-ui")>=0)SelfTests.BeginUiChecks(app,controller,args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"ui-test.json"));
                    if(Array.IndexOf(args,"--verify-hold")>=0)SelfTests.BeginHoldChecks(app,controller,args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"hold-test.json"));
                }
                catch(Exception ex) { MessageBox.Show("一划无法启动："+ex.Message,"一划",MessageBoxButton.OK,MessageBoxImage.Error); app.Shutdown(1); }
            };
            app.Exit+=(_,_)=> { waiter?.Unregister(null); exitWaiter?.Unregister(null); controller?.Dispose(); };
            return app.Run();
        }
        finally { mutex.ReleaseMutex(); }
    }
}
