using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    [DllImport("user32.dll",EntryPoint="PostMessageW",SetLastError=true)]
    static extern bool PostTrayFailureTestMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);

    internal static async void BeginTrayFailureChecks(Application app,AppController controller,string report,bool failSave)
    {
        var results=new Dictionary<string,object> { ["SimulatedSaveFailure"]=failSave,["ProcessId"]=Environment.ProcessId };
        SettingsWindow? window=null;
        FileStream? saveLock=null;
        string? lockPath=null;
        bool createdLockFile=false,windowClosed=false,closePosted=false,checksPassed=false;
        var watchdog=new DispatcherTimer {Interval=TimeSpan.FromSeconds(5)};
        void Check(bool condition,string name)
        {results[name]=condition;if(!condition)throw new InvalidOperationException(name);}
        void ReleaseSaveLock()
        {
            saveLock?.Dispose();saveLock=null;
            if(createdLockFile && lockPath!=null){File.Delete(lockPath);createdLockFile=false;}
        }
        watchdog.Tick+=(_,_)=>
        {
            watchdog.Stop();results["CloseTimedOut"]=true;checksPassed=false;
            controller.Exit();
        };
        // Program's Exit handler disposes the controller before this report is written.
        app.Exit+=(_,e)=>
        {
            watchdog.Stop();
            results["SettingsWindowClosed"]=windowClosed;
            results["CloseRequestedApplicationExit"]=closePosted && controller.IsExiting;
            results["NativeHooksReleasedAndThreadStopped"]=controller.InputResourcesReleased;
            results["ShellTrayIconRemoved"]=controller.TrayRemoved;
            bool saveErrorPreserved=!failSave || window?.FindName("SaveStatus") is TextBlock status && status.Text.StartsWith("保存失败：",StringComparison.Ordinal);
            if(failSave)results["SaveFailureMessagePreserved"]=saveErrorPreserved;
            try {ReleaseSaveLock();}
            catch(Exception ex){results["CleanupError"]=ex.ToString();checksPassed=false;}
            results["Success"]=checksPassed && windowClosed && closePosted && controller.IsExiting && controller.InputResourcesReleased && controller.TrayRemoved && saveErrorPreserved;
            File.WriteAllText(report,JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
            if(!Equals(results["Success"],true))e.ApplicationExitCode=1;
        };
        try
        {
            Check(!controller.TrayRegistered,"TrayRegistrationActuallyFailed");
            results["TrayRegistrationError"]=controller.TrayError;
            controller.ShowSettings();await Task.Delay(150);
            window=app.Windows.OfType<SettingsWindow>().Single();
            Check(window.IsVisible,"SettingsVisibleWithoutTray");
            if(failSave)
            {
                // Hold the real atomic-save temporary file open exclusively. This
                // exercises Flush's IOException path without changing saved settings.
                lockPath=new ConfigStore().FilePath+".tmp";
                createdLockFile=!File.Exists(lockPath);
                saveLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                Check(!window.Flush(),"RealConfigurationSaveFailureObserved");
                Check(((TextBlock)window.FindName("SaveStatus")).Text.StartsWith("保存失败：",StringComparison.Ordinal),"ConfigurationSaveFailureDisplayed");
            }
            window.Closed+=(_,_)=>windowClosed=true;
            var handle=new WindowInteropHelper(window).Handle;
            Check(handle!=IntPtr.Zero,"SettingsNativeWindowCreated");
            // SC_CLOSE takes the same native system-command route as title-bar X.
            closePosted=PostTrayFailureTestMessage(handle,0x112,new IntPtr(0xF060),IntPtr.Zero);
            Check(closePosted,"TitleBarCloseCommandPosted");
            checksPassed=true;watchdog.Start();
        }
        catch(Exception ex)
        {
            checksPassed=false;results["Error"]=ex.ToString();
            controller.Exit();
        }
    }
}
