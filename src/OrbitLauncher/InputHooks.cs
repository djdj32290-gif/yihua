using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace OrbitLauncher;
internal sealed class InputHooks : IDisposable
{
    readonly Native.HookProc mouseProc,keyProc;
    readonly Native.WinEventProc desktopProc,foregroundProc;
    readonly ForegroundGuard protection=new();
    readonly Dispatcher ui;
    readonly Thread thread;
    Dispatcher worker=null!;
    IntPtr mouseHook,keyHook,desktopHook,foregroundHook;
    readonly GestureMachine machine=new();
    readonly ModifierState modifiers=new();
    Settings config;
    bool disposed,moveQueued,escapeCaptured,simulatedInput;
    volatile bool paused;
    volatile bool released;
    internal volatile bool TestMode;
    string? testProtectionReason,lastProtectionReason;
    int latestX,latestY;
    double threshold;
    long keyboardEvents,mouseEvents,desktopEvents,begunGestures;
    volatile bool leftButtonHeld;
    volatile string lastSignal="等待输入";
    public bool Paused { get=>paused; set=>paused=value; }
    public bool Busy => machine.Phase!=GesturePhase.Idle;
    internal bool ResourcesReleased=>released && !thread.IsAlive;
    internal GesturePhase Phase=>machine.Phase;
    internal object Snapshot=>new { KeyboardEvents=Interlocked.Read(ref keyboardEvents),MouseEvents=Interlocked.Read(ref mouseEvents),DesktopEvents=Interlocked.Read(ref desktopEvents),BegunGestures=Interlocked.Read(ref begunGestures),LeftAlt=modifiers.IsDown(0xA4),RightAlt=modifiers.RightAlt,ModifierDown=modifiers.IsDown(ModifierKey),LeftButton=leftButtonHeld,Phase=machine.Phase.ToString(),LastSignal=lastSignal,Paused=paused,GameProtection=config.GameModeEnabled,ProtectionReason=lastProtectionReason };
    internal string StatusText=>$"输入事件：键盘 {Interlocked.Read(ref keyboardEvents)} / 鼠标 {Interlocked.Read(ref mouseEvents)}　左 Alt：{(modifiers.IsDown(0xA4)?"按下":"松开")}　右 Alt：{(modifiers.RightAlt?"按下":"松开")}　左键：{(leftButtonHeld?"按下":"松开")}\n手势：{machine.Phase switch {GesturePhase.Pending=>"等待拖动",GesturePhase.Active=>"轮盘已触发",GesturePhase.Draining=>"已取消，等待松左键",_=>"空闲"}} · {lastSignal}";
    public event Action<int,int>? Began;
    public event Action<int,int>? Moved;
    public event Action<bool>? Ended;
    int ModifierKey=>config.Modifier switch { "LeftControl"=>0xA2,"LeftShift"=>0xA0,_=>0xA4 };
    public InputHooks(Func<Settings> settings,Dispatcher dispatcher)
    {
        config=settings().Copy(); ui=dispatcher; mouseProc=Mouse; keyProc=Keyboard;
        desktopProc=(_,_,_,_,_,_,_)=> { Interlocked.Increment(ref desktopEvents);ResetCore("桌面切换");modifiers.Reset(); };
        foregroundProc=(_,_,_,_,_,_,_)=> {protection.Invalidate();if(!TestMode && machine.Phase is GesturePhase.Pending or GesturePhase.Active){var reason=ProtectionReason();if(reason!=null)CancelForProtection(reason);}};
        using var ready=new ManualResetEventSlim(); Exception? error=null;
        thread=new Thread(()=>
        {
            worker=Dispatcher.CurrentDispatcher;
            try
            {
                keyHook=Native.SetWindowsHookEx(13,keyProc,Native.GetModuleHandle(null),0);
                mouseHook=Native.SetWindowsHookEx(14,mouseProc,Native.GetModuleHandle(null),0);
                if(keyHook==IntPtr.Zero || mouseHook==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法注册全局输入钩子");
                foreach(int key in new[]{0xA4,0xA2,0xA0,0xA5})modifiers.Set(key,(Native.GetAsyncKeyState(key)&0x8000)!=0);
                desktopHook=Native.SetWinEventHook(0x20,0x20,IntPtr.Zero,desktopProc,0,0,0);
                foregroundHook=Native.SetWinEventHook(3,3,IntPtr.Zero,foregroundProc,0,0,2);
            }
            catch(Exception ex) { error=ex; }
            finally { ready.Set(); }
            try { if(error==null)Dispatcher.Run(); }
            finally { bool m=mouseHook==IntPtr.Zero || Native.UnhookWindowsHookEx(mouseHook); bool k=keyHook==IntPtr.Zero || Native.UnhookWindowsHookEx(keyHook); bool d=desktopHook==IntPtr.Zero || Native.UnhookWinEvent(desktopHook);bool f=foregroundHook==IntPtr.Zero || Native.UnhookWinEvent(foregroundHook); mouseHook=keyHook=desktopHook=foregroundHook=IntPtr.Zero; released=m&&k&&d&&f; }
        }) { IsBackground=true,Name="Orbit input" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
        if(error!=null)throw error;
    }
    void Queue(Action action)=>ui.BeginInvoke(action,DispatcherPriority.Input);
    public void UpdateSettings(Settings settings)
    {
        var copy=settings.Copy(); worker.Invoke(()=> { CancelCore(); config=copy; });
    }
    void MoveOnUi(int x,int y)
    {
        // Only one queued move at a time, even with a 1000 Hz mouse.
        lock(machine)
        {
            latestX=x; latestY=y; if(moveQueued)return; moveQueued=true;
            Queue(()=> { int px,py; lock(machine) { px=latestX;py=latestY;moveQueued=false; } Moved?.Invoke(px,py); });
        }
    }
    IntPtr Mouse(int code,IntPtr w,IntPtr l)
    {
        if(code<0)return Native.CallNextHookEx(mouseHook,code,w,l);
        var data=Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(l);
        if(data.ExtraInfo==Native.Tag)return Native.CallNextHookEx(mouseHook,code,w,l);
        if(TestMode)return Native.CallNextHookEx(mouseHook,code,w,l);
        Interlocked.Increment(ref mouseEvents);
        var result=ProcessPointer(w.ToInt32(),data.Pt,null,false);
        // Keep the hardware cursor moving. Only button messages are intercepted.
        return result.Block && w.ToInt32()!=0x200?new IntPtr(1):Native.CallNextHookEx(mouseHook,code,w,l);
    }
    GestureResult ProcessPointer(int message,Native.POINT point,bool? eligibleOverride,bool isTest)
    {
        if(message==0x201)simulatedInput=isTest;
        GestureResult result=new(false);
        switch(message)
        {
            case 0x201:
                leftButtonHeld=true;
                // A fresh physical down also recovers a release missed on another desktop.
                if(machine.Phase!=GesturePhase.Idle)ResetCore();
                bool eligible=eligibleOverride ?? GestureMachine.Eligible(modifiers.IsDown(ModifierKey),modifiers.RightAlt,paused,config.Items.Count);
                lastSignal=eligible?"已收到组合按下，等待超过拖动阈值":paused?"手势已暂停":modifiers.RightAlt?"右 Alt 不触发":!modifiers.IsDown(ModifierKey)?"未收到指定左修饰键按下":"轮盘列表为空";
                if(eligible){lastProtectionReason=ProtectionReason(point);if(lastProtectionReason!=null){eligible=false;lastSignal=lastProtectionReason+"，本次输入原样放行";}}
                if(eligible)threshold=config.DragThreshold*Native.ScreenAt(point.X,point.Y).Scale;
                result=machine.Down(point.X,point.Y,eligible); break;
            case 0x200:result=machine.Move(point.X,point.Y,threshold);break;
            case 0x202:
                leftButtonHeld=false;
                if(machine.Phase is GesturePhase.Pending or GesturePhase.Active){var reason=ProtectionReason(point);if(reason!=null)CancelForProtection(reason);}
                result=machine.Up();break;
            default:if(machine.Phase is GesturePhase.Active or GesturePhase.Draining)result=new(true);break;
        }
        switch(result.Action)
        {
            case GestureAction.Begin:
                var blocked=ProtectionReason(point);if(blocked!=null){CancelForProtection(blocked);return new(true);}
                Interlocked.Increment(ref begunGestures);lastSignal="已超过阈值，显示轮盘";
                MaskMenu();
                var ox=machine.OriginX;var oy=machine.OriginY;
                Queue(()=> { Began?.Invoke(ox,oy);Moved?.Invoke(point.X,point.Y); });break;
            case GestureAction.Update:MoveOnUi(point.X,point.Y);break;
            case GestureAction.Commit:lastSignal="左键释放，完成选择";Queue(()=> { Moved?.Invoke(point.X,point.Y);Ended?.Invoke(true); });break;
            case GestureAction.ReplayClick:if(!isTest)Native.Click();break;
        }
        return result;
    }
    string? ProtectionReason(Native.POINT? pointer=null)=>!config.GameModeEnabled?null:TestMode?testProtectionReason:protection.Reason(config,pointer);
    void CancelForProtection(string reason)
    {lastProtectionReason=reason;lastSignal=reason+"，已取消本次手势";if(machine.Cancel().Action==GestureAction.Cancel)Queue(()=>Ended?.Invoke(false));}
    IntPtr Keyboard(int code,IntPtr w,IntPtr l)
    {
        if(code<0)return Native.CallNextHookEx(keyHook,code,w,l);
        var data=Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(l);
        if(data.ExtraInfo==Native.Tag)return Native.CallNextHookEx(keyHook,code,w,l);
        if(TestMode)return Native.CallNextHookEx(keyHook,code,w,l);
        Interlocked.Increment(ref keyboardEvents);
        bool up=w.ToInt32() is 0x101 or 0x105;
        return ProcessKey(ModifierState.Normalize(data.VkCode,data.ScanCode,data.Flags),up)?new IntPtr(1):Native.CallNextHookEx(keyHook,code,w,l);
    }
    bool ProcessKey(uint key,bool up)
    {
        modifiers.Set((int)key,!up);
        if(key==ModifierKey)lastSignal=up?"指定修饰键已松开":"指定左修饰键已按下";
        if(key==ModifierKey && up)
        {
            var r=machine.ModifierUp();
            if(r.Action==GestureAction.ReplayDown && !simulatedInput)Native.Mouse(2);
            if(r.Action==GestureAction.Cancel)Queue(()=>Ended?.Invoke(false));
        }
        if(key==0x1B)
        {
            if(up && escapeCaptured) { escapeCaptured=false;return true; }
            if(!up && (escapeCaptured || machine.Phase!=GesturePhase.Idle))
            {
                lastSignal="Esc 取消";
                escapeCaptured=true;var r=machine.Cancel();
                if(r.Action==GestureAction.Cancel) { MaskMenu();Queue(()=>Ended?.Invoke(false)); }
                return true;
            }
        }
        if(!up && key!=ModifierKey && key!=0x1B)CancelCore();
        return false;
    }
    void MaskMenu() { if(!simulatedInput && ModifierKey==0xA4)Native.MaskAltMenu(); }
    void CancelCore()
    {
        var r=machine.Cancel();
        if(r.Action==GestureAction.Cancel) { MaskMenu();Queue(()=>Ended?.Invoke(false)); }
    }
    public void Cancel() { if(!disposed)worker.Invoke(CancelCore); }
    void ResetCore(string reason="恢复上一笔输入")
    {
        // Intercepted mouse-downs do NOT reliably appear in GetAsyncKeyState(VK_LBUTTON).
        // Never use that state (or an elapsed timeout) to end a held gesture.
        if(machine.Reset().Action==GestureAction.Cancel) { lastSignal=reason+"，已取消";Queue(()=>Ended?.Invoke(false)); }
    }
    // In-process integration tests use the same handlers, without synthesizing OS input.
    internal GestureResult TestPointer(int message,int x,int y)=>worker.Invoke(()=>ProcessPointer(message,new Native.POINT(x,y),null,true));
    internal void TestModifierDown()=>worker.Invoke(()=>ProcessKey((uint)ModifierKey,false));
    internal void TestModifierUp()=>worker.Invoke(()=>ProcessKey((uint)ModifierKey,true));
    internal void TestEscape()=>worker.Invoke(()=> { ProcessKey(0x1B,false);ProcessKey(0x1B,true); });
    internal void TestProtection(string? reason)=>worker.Invoke(()=>testProtectionReason=reason);
    public void Dispose()
    {
        if(disposed)return;disposed=true;worker.BeginInvokeShutdown(DispatcherPriority.Send);thread.Join(1500);
    }
}
