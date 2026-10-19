using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    static readonly UIntPtr NativeTestTag=new(0x4F524254);
    internal static int RunInputTestTarget(string token)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
        var window=new Window {Title="一划 · 跨进程输入测试 · "+token,Width=Math.Min(900,SystemParameters.WorkArea.Width*.75),Height=Math.Min(700,SystemParameters.WorkArea.Height*.75),
            WindowStartupLocation=WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize,Background=Brushes.White,
            Content=new TextBlock {Text="跨进程输入测试窗口\n此窗口不注册托盘、不监听输入，也不执行任何入口。",FontSize=20,Margin=new Thickness(30),TextWrapping=TextWrapping.Wrap}};
        // A parent crash must not leave this helper open indefinitely.
        var expiry=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMinutes(2)};
        expiry.Tick+=(_,_)=>window.Close();expiry.Start();
        return app.Run(window);
    }
    public static async void BeginNativeInputChecks(Application app,AppController controller,string report)
    {
        var results=new Dictionary<string,object>();
        var originalItems=controller.Config.Items;bool originalGame=controller.Config.GameModeEnabled,originalFullscreen=controller.Config.PauseInFullscreen;
        string originalModifier=controller.Config.Modifier;
        Process? externalTarget=null;
        Native.GetCursorPos(out var originalCursor);
        var window=new Window {Title="一划 · 系统输入回归验证",Width=900,Height=700,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Brushes.White,
            Content=new TextBlock {Text="正在验证 Alt 拖动、选中、松键收起。测试不会启动实际入口。",FontSize=20,Margin=new Thickness(30),TextWrapping=TextWrapping.Wrap}};
        controller.TestLaunch=_=>{};
        void Check(bool condition,string name){results[name]=condition;if(!condition)throw new Exception(name);}
        void Send(Native.INPUT input)
        {
            var watch=Stopwatch.StartNew();uint sent=Native.SendInput(1,new[]{input},Marshal.SizeOf<Native.INPUT>());
            Check(sent==1,"SystemInputAccepted");
            results["LastSendMilliseconds"]=watch.Elapsed.TotalMilliseconds;
        }
        void Key(ushort vk,bool up)=>Send(new Native.INPUT{Type=1,Data=new Native.INPUTUNION{Key=new Native.KEYBDINPUT{Vk=vk,Flags=(up?2u:0u)|(vk==0xA5?1u:0u),ExtraInfo=NativeTestTag}}});
        void Button(bool up)
        {
            Send(new Native.INPUT{Type=0,Data=new Native.INPUTUNION{Mouse=new Native.MOUSEINPUT{Flags=up?4u:2u,ExtraInfo=NativeTestTag}}});
            results[up?"MouseUpInputInserted":"MouseDownInputInserted"]=true;
        }
        void Move(int px,int py)=>Send(new Native.INPUT{Type=0,Data=new Native.INPUTUNION{Mouse=new Native.MOUSEINPUT{Dx=(int)Math.Round((px-GetSystemMetrics(76))*65535d/(GetSystemMetrics(78)-1)),Dy=(int)Math.Round((py-GetSystemMetrics(77))*65535d/(GetSystemMetrics(79)-1)),Flags=0xC001,ExtraInfo=NativeTestTag}}});
        int x=0,y=0;double scale=1;
        async Task Begin()
        {
            Move(x,y);await Task.Delay(60);results["NativeInputBeforeAlt"]=controller.InputSnapshot;
            Key(0xA4,false);await Task.Delay(60);results["NativeInputAfterAlt"]=controller.InputSnapshot;
            Button(false);await Task.Delay(60);results["NativeInputAfterMouseDown"]=controller.InputSnapshot;
            Check(controller.GesturePhase==GesturePhase.Pending,"NativeMouseDownEntersPending");
            Move(x+(int)(120*scale),y);await Task.Delay(300);
            results["InputAtBegin"]=controller.InputSnapshot;
            Check(controller.WheelVisible && controller.GesturePhase==GesturePhase.Active,"NativeAltDragShowsWheel");
        }
        try
        {
            WebsiteIcons.AutomaticLoading=true;
            controller.Config.Items=DemoSettings().Items.GetRange(0,4);
            controller.Config.Items[0]=new LaunchItem{Name="GitHub 图标验证",Target="https://github.com/"};
            controller.Config.Items[1]=new LaunchItem{Name="哔哩哔哩图标验证",Target="https://www.bilibili.com/"};
            controller.Config.GameModeEnabled=false;controller.Config.Modifier="LeftAlt";controller.Apply();
            results["WebsiteIconLoadingEnabled"]=true;
            window.Show();var ownHandle=new WindowInteropHelper(window).Handle;
            ShowWindow(ownHandle,1);window.Activate();SetForegroundWindow(ownHandle);await Task.Delay(250);
            var ownForeground=GetForegroundWindow();GetWindowThreadProcessId(ownForeground,out uint ownForegroundPid);
            results["OwnTestForeground"]=new {Handle=ownForeground.ToInt64(),ProcessId=ownForegroundPid,ExpectedProcessId=Environment.ProcessId};
            Check(ownForeground==ownHandle && ownForegroundPid==(uint)Environment.ProcessId,"OwnTestWindowHasForeground");
            Key(0xA4,true);Key(0xA5,true);Button(true);await Task.Delay(150);
            results["InputAfterPreparation"]=controller.InputSnapshot;
            var centre=window.PointToScreen(new Point(window.ActualWidth/2,window.ActualHeight/2));x=(int)centre.X;y=(int)centre.Y;
            scale=VisualTreeHelper.GetDpi(window).DpiScaleX;
            controller.TestDisconnectKeyboardHook();
            await Begin();
            Check(controller.WheelVisible,"KeyboardHookLostWhileIdleStillAllowsFirstGesture");
            Move(x+(int)(155*scale),y);await Task.Delay(250);
            Check(controller.TestWheel.Selection.Stops.Count==1,"NativeMouseMoveSelectsEntry");
            Move(x+(int)(265*scale),y);await Task.Delay(120);
            Check(controller.TestWheel.Selection.Confirmed,"NativeOutwardMoveConfirms");
            Button(true);await Task.Delay(200);Key(0xA4,true);await Task.Delay(120);
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle && controller.LaunchAttempts==1,"NativeMouseReleaseClosesAndCommitsOnce");
            Move(x,y);Key(0xA5,false);Key(0xA4,false);await Task.Delay(80);Button(false);Move(x+(int)(120*scale),y);await Task.Delay(180);
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle,"NativeRightAltCombinationStillPassesThrough");
            Button(true);Key(0xA4,true);Key(0xA5,true);await Task.Delay(100);
            controller.TestStaleRightAltDown();await Begin();
            Check(controller.WheelVisible,"MissedRightAltReleaseDoesNotBlockNextLeftAltGesture");
            Move(x,y);Button(true);Key(0xA4,true);await Task.Delay(180);
            Check(!controller.WheelVisible && controller.LaunchAttempts==1,"RecoveredRightAltStateDoesNotCauseUnexpectedLaunch");
            await Begin();controller.TestDisconnectMouseHook();await Task.Delay(180);
            Move(x,y-(int)(155*scale));await Task.Delay(350);
            Check(controller.TestWheel.Selection.Hovered==0,"DisconnectedMouseHookMovementRecovers");
            Button(true);await Task.Delay(200);Key(0xA4,true);await Task.Delay(120);
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle,"RecoveredMouseHookReleaseClosesWheel");
            await Begin();Key(0xA4,true);await Task.Delay(200);Button(true);await Task.Delay(100);
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle && controller.LaunchAttempts==1,"NativeAltReleaseCancelsAndCloses");
            await Begin();Key(0x1B,false);Key(0x1B,true);await Task.Delay(150);Button(true);Key(0xA4,true);await Task.Delay(150);
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle && controller.LaunchAttempts==1,"NativeEscapeCancelsAndCloses");
            for(int i=0;i<8;i++){await Begin();Move(x,y);await Task.Delay(50);Button(true);Key(0xA4,true);await Task.Delay(150);Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle,$"NativeRepeatedGesture{i+1}Closes");}

            // The original checks use our own foreground window and disabled protection.
            // A separate process catches input paths that only work while settings has focus.
            controller.Config.GameModeEnabled=true;controller.Config.PauseInFullscreen=true;controller.Apply();
            string token=Guid.NewGuid().ToString("N");
            var start=new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate native input test executable"))
                {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
            if(string.Equals(Path.GetFileNameWithoutExtension(start.FileName),"dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(SelfTests).Assembly.Location);
            start.ArgumentList.Add("--input-test-target");start.ArgumentList.Add(token);
            externalTarget=Process.Start(start) ?? throw new InvalidOperationException("Cannot launch external input test target");
            var ready=Stopwatch.StartNew();IntPtr targetHandle=IntPtr.Zero;
            while(ready.Elapsed<TimeSpan.FromSeconds(8) && !externalTarget.HasExited)
            {
                externalTarget.Refresh();targetHandle=externalTarget.MainWindowHandle;
                if(targetHandle!=IntPtr.Zero)break;
                await Task.Delay(50);
            }
            Check(targetHandle!=IntPtr.Zero,"ExternalTargetWindowCreated");
            results["ExternalExpectedProcessId"]=externalTarget.Id;results["ExternalTargetHandle"]=targetHandle.ToInt64();
            Check(externalTarget.Id!=Environment.ProcessId,"ExternalTargetIsSeparateProcess");
            Check(Native.GetWindowRect(targetHandle,out var targetBounds),"ExternalTargetBoundsAvailable");
            x=(targetBounds.Left+targetBounds.Right)/2;y=(targetBounds.Top+targetBounds.Bottom)/2;
            scale=Native.ScreenAt(x,y).Scale;
            results["ExternalGameProtectionEnabled"]=controller.Config.GameModeEnabled;
            results["ExternalFullscreenProtectionEnabled"]=controller.Config.PauseInFullscreen;
            async Task FocusExternal(string stage)
            {
                AllowSetForegroundWindow((uint)externalTarget.Id);SetForegroundWindow(targetHandle);await Task.Delay(150);
                var foreground=GetForegroundWindow();GetWindowThreadProcessId(foreground,out uint foregroundPid);
                results[stage+"Foreground"]=new {Handle=foreground.ToInt64(),ProcessId=foregroundPid,ExpectedProcessId=externalTarget.Id};
                Check(foreground==targetHandle && foregroundPid==(uint)externalTarget.Id,stage+"HasExternalForeground");
            }
            async Task BeginExternal(string stage)
            {
                await FocusExternal(stage);Move(x,y);await Task.Delay(60);
                results[stage+"InputBeforeDown"]=controller.InputSnapshot;
                Key(0xA4,false);await Task.Delay(80);Button(false);await Task.Delay(80);
                results[stage+"InputAfterDown"]=controller.InputSnapshot;
                Check(controller.GesturePhase==GesturePhase.Pending,stage+"MouseDownEntersPending");
                Move(x+(int)(120*scale),y);await Task.Delay(300);
                results[stage+"InputAfterMove"]=controller.InputSnapshot;
                Check(controller.WheelVisible && controller.GesturePhase==GesturePhase.Active,stage+"DragShowsWheel");
            }
            await BeginExternal("ExternalCommit");
            Move(x+(int)(155*scale),y);await Task.Delay(200);
            Check(controller.TestWheel.Selection.Stops.Count==1,"ExternalMouseMoveSelectsEntry");
            Move(x+(int)(265*scale),y);await Task.Delay(120);
            Check(controller.TestWheel.Selection.Confirmed,"ExternalOutwardMoveConfirms");
            int beforeCommit=controller.LaunchAttempts;Button(true);await Task.Delay(200);Key(0xA4,true);await Task.Delay(120);
            results["ExternalInputAfterMouseRelease"]=controller.InputSnapshot;
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle && controller.LaunchAttempts==beforeCommit+1,"ExternalMouseReleaseClosesAndCommitsOnce");
            await BeginExternal("ExternalCancel");int beforeCancel=controller.LaunchAttempts;
            Key(0xA4,true);await Task.Delay(200);Button(true);await Task.Delay(120);
            results["ExternalInputAfterAltRelease"]=controller.InputSnapshot;
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle && controller.LaunchAttempts==beforeCancel,"ExternalAltReleaseCancelsAndCloses");
            await FocusExternal("ExternalNoAlt");
            Move(x,y);Key(0xA4,false);await Task.Delay(80);Key(0xA4,true);await Task.Delay(100);
            Button(false);await Task.Delay(80);Move(x+(int)(155*scale),y);await Task.Delay(200);
            results["ExternalNoAltInputDuringDrag"]=controller.InputSnapshot;
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Idle,"ExternalNoAltMouseDragDoesNotShowWheel");
            Button(true);await Task.Delay(100);
            Check(controller.LaunchAttempts==beforeCancel,"ExternalNoAltDragDoesNotLaunch");
            results["Success"]=true;
        }
        catch(Exception ex){results["Success"]=false;results["Error"]=ex.ToString();results["InputAtFailure"]=controller.InputSnapshot;}
        finally
        {
            try {Key(0xA4,true);Key(0xA5,true);Key(0x1B,true);Button(true);controller.Recover();}
            catch(Exception ex){results["InputCleanupError"]=ex.ToString();results["Success"]=false;}
            try {controller.Config.Items=originalItems;controller.Config.GameModeEnabled=originalGame;controller.Config.PauseInFullscreen=originalFullscreen;controller.Config.Modifier=originalModifier;controller.Apply();}
            catch(Exception ex){results["ConfigRestoreError"]=ex.ToString();results["Success"]=false;}
            if(externalTarget!=null)
            {
                try
                {
                    if(!externalTarget.HasExited)
                    {
                        externalTarget.CloseMainWindow();
                        await Task.WhenAny(externalTarget.WaitForExitAsync(),Task.Delay(3000));
                        if(!externalTarget.HasExited){externalTarget.Kill();await externalTarget.WaitForExitAsync();}
                    }
                    results["ExternalTargetExited"]=externalTarget.HasExited;
                }
                catch(Exception ex){results["ExternalTargetCleanupError"]=ex.ToString();results["Success"]=false;}
                finally {externalTarget.Dispose();}
            }
            window.Close();SetCursorPos(originalCursor.X,originalCursor.Y);
            File.WriteAllText(report,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            app.Shutdown(results.TryGetValue("Success",out var success) && Equals(success,true)?0:1);
        }
    }
}
