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
    DispatcherTimer? observer;
    IntPtr mouseHook,keyHook,desktopHook,foregroundHook;
    readonly GestureMachine machine=new();
    readonly ModifierState modifiers=new();
    Settings config;
    bool disposed,escapeCaptured,simulatedInput;
    volatile bool paused;
    volatile bool released;
    internal volatile bool TestMode;
    string? testProtectionReason,lastProtectionReason;
    sealed class MoveBatch { internal int X,Y;internal bool Queued; }
    MoveBatch moves=new();
    double threshold;
    long keyboardEvents,mouseEvents,desktopEvents,begunGestures;
    long mouseDownEvents,mouseUpEvents;
    long lastMouseHookAt=Environment.TickCount64,hookRecoveries;
    Native.POINT lastHookPoint;
    volatile bool leftButtonHeld;
    volatile string lastSignal="等待输入";
    string[] recentSignals=Array.Empty<string>();
    void RecordSignal(string signal)
    {
        var previous=recentSignals;var next=new string[Math.Min(previous.Length+1,20)];
        Array.Copy(previous,Math.Max(0,previous.Length-next.Length+1),next,0,next.Length-1);
        next[^1]=$"{DateTime.Now:HH:mm:ss.fff} {signal}";recentSignals=next;
    }
    public bool Paused { get=>paused; set=>paused=value; }
    public bool Busy => machine.Phase!=GesturePhase.Idle;
    internal bool ResourcesReleased=>released && !thread.IsAlive;
    internal GesturePhase Phase=>machine.Phase;
    internal object Snapshot=>new { KeyboardEvents=Interlocked.Read(ref keyboardEvents),MouseEvents=Interlocked.Read(ref mouseEvents),MouseDownEvents=Interlocked.Read(ref mouseDownEvents),MouseUpEvents=Interlocked.Read(ref mouseUpEvents),DesktopEvents=Interlocked.Read(ref desktopEvents),BegunGestures=Interlocked.Read(ref begunGestures),HookRecoveries=Interlocked.Read(ref hookRecoveries),LeftAlt=modifiers.IsDown(0xA4),RightAlt=modifiers.RightAlt,ModifierDown=modifiers.IsDown(ModifierKey),LeftButton=leftButtonHeld,Phase=machine.Phase.ToString(),LastSignal=lastSignal,Paused=paused,GameProtection=config.GameModeEnabled,ProtectionReason=lastProtectionReason,RecentSignals=recentSignals };
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
                observer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(50)};
                observer.Tick+=(_,_)=>ObserveInput();observer.Start();
            }
            catch(Exception ex) { error=ex; }
            finally { ready.Set(); }
            try { if(error==null)Dispatcher.Run(); }
            finally { observer?.Stop();bool m=mouseHook==IntPtr.Zero || Native.UnhookWindowsHookEx(mouseHook); bool k=keyHook==IntPtr.Zero || Native.UnhookWindowsHookEx(keyHook); bool d=desktopHook==IntPtr.Zero || Native.UnhookWinEvent(desktopHook);bool f=foregroundHook==IntPtr.Zero || Native.UnhookWinEvent(foregroundHook); mouseHook=keyHook=desktopHook=foregroundHook=IntPtr.Zero; released=m&&k&&d&&f; }
        }) { IsBackground=true,Name="Orbit input" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
        if(error!=null)throw error;
    }
    void Queue(Action action)=>ui.BeginInvoke(action,DispatcherPriority.Input);
    bool RecoverModifierDowns(Func<int,short>? query=null)
    {
        query ??= Native.GetAsyncKeyState;
        bool changed=false;
        bool alt=(query(0xA4)&0x8000)!=0,control=(query(0xA2)&0x8000)!=0,
            shift=(query(0xA0)&0x8000)!=0,rightAlt=(query(0xA5)&0x8000)!=0;
        // A positive state can recover a missed down. Zero may also mean that
        // Windows denied the query, so do not erase a received left-key down.
        void Recover(int key,bool down){if(down && !modifiers.IsDown(key)){modifiers.Set(key,true);changed=true;}}
        Recover(0xA4,alt);Recover(0xA2,control);Recover(0xA0,shift);Recover(0xA5,rightAlt);
        bool selectedDown=ModifierKey switch{0xA2=>control,0xA0=>shift,_=>alt};
        // A readable held left key also lets us clear a right-Alt up that was
        // missed, without treating an all-zero/inaccessible desktop as valid.
        if(selectedDown && !rightAlt && modifiers.RightAlt){modifiers.Set(0xA5,false);changed=true;}
        if(changed)RecordSignal("系统修饰键状态与事件缓存不一致，已同步并请求恢复监听");
        return changed;
    }
    void ObserveInput()
    {
        if(disposed || TestMode || simulatedInput)return;
        ObserveInputState(Native.GetAsyncKeyState,true);
    }
    void ObserveInputState(Func<int,short> query,bool observePointer)
    {
        if(RecoverModifierDowns(query))RefreshInputHooks();
        if(!Busy && !modifiers.IsDown(ModifierKey))return;
        // A zero query can also mean access was denied or the input desktop
        // changed. Only a keyboard-up event may end a held modifier gesture.
        // Intercepted left-button states are likewise never used to end it.
        if(!observePointer || Environment.TickCount64-lastMouseHookAt<150 || !Native.GetCursorPos(out var point) || point.X==lastHookPoint.X && point.Y==lastHookPoint.Y)return;
        RecordSignal("光标已移动但监听未收到，补充位置并恢复输入监听");
        ProcessPointer(0x200,point,null,false);RefreshInputHooks();
    }
    void RefreshInputHooks()
    {
        if(disposed)return;
        var newMouse=Native.SetWindowsHookEx(14,mouseProc,Native.GetModuleHandle(null),0);
        var newKey=Native.SetWindowsHookEx(13,keyProc,Native.GetModuleHandle(null),0);
        if(newMouse!=IntPtr.Zero){if(mouseHook!=IntPtr.Zero)Native.UnhookWindowsHookEx(mouseHook);mouseHook=newMouse;}
        if(newKey!=IntPtr.Zero){if(keyHook!=IntPtr.Zero)Native.UnhookWindowsHookEx(keyHook);keyHook=newKey;}
        lastMouseHookAt=Environment.TickCount64;Native.GetCursorPos(out lastHookPoint);
        if(newMouse!=IntPtr.Zero && newKey!=IntPtr.Zero)Interlocked.Increment(ref hookRecoveries);
        else RecordSignal("恢复输入监听失败："+Marshal.GetLastWin32Error());
    }
    public void UpdateSettings(Settings settings)
    {
        var copy=settings.Copy(); worker.Invoke(()=> { CancelCore(); config=copy; });
    }
    void MoveOnUi(int x,int y)
    {
        // Only one queued move at a time, even with a 1000 Hz mouse.
        lock(machine)
        {
            var batch=moves;batch.X=x;batch.Y=y;if(batch.Queued)return;batch.Queued=true;
            Queue(()=> { int px,py; lock(machine) { px=batch.X;py=batch.Y;batch.Queued=false; } Moved?.Invoke(px,py); });
        }
    }
    IntPtr Mouse(int code,IntPtr w,IntPtr l)
    {
        if(code<0)return Native.CallNextHookEx(mouseHook,code,w,l);
        var data=Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(l);
        if(data.ExtraInfo==Native.Tag)return Native.CallNextHookEx(mouseHook,code,w,l);
        if(TestMode)return Native.CallNextHookEx(mouseHook,code,w,l);
        Interlocked.Increment(ref mouseEvents);
        if(w.ToInt32()==0x201)Interlocked.Increment(ref mouseDownEvents);
        if(w.ToInt32()==0x202)Interlocked.Increment(ref mouseUpEvents);
        lastMouseHookAt=Environment.TickCount64;lastHookPoint=data.Pt;
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
                if(!isTest && !TestMode && RecoverModifierDowns())worker.BeginInvoke(RefreshInputHooks,DispatcherPriority.Background);
                // A fresh physical down also recovers a release missed on another desktop.
                if(machine.Phase!=GesturePhase.Idle)ResetCore();
                bool eligible=eligibleOverride ?? GestureMachine.Eligible(modifiers.IsDown(ModifierKey),modifiers.RightAlt,paused,config.Items.Count);
                lastSignal=eligible?"已收到组合按下，等待超过拖动阈值":paused?"手势已暂停":modifiers.RightAlt?"右 Alt 不触发":!modifiers.IsDown(ModifierKey)?"未收到指定左修饰键按下":"轮盘列表为空";
                if(eligible){lastProtectionReason=ProtectionReason(point);if(lastProtectionReason!=null){eligible=false;lastSignal=lastProtectionReason+"，本次输入原样放行";}}
                if(eligible)threshold=config.DragThreshold*Native.ScreenAt(point.X,point.Y).Scale;
                result=machine.Down(point.X,point.Y,eligible);RecordSignal($"左键按下 ({point.X},{point.Y})：{lastSignal}；阶段 {machine.Phase}；系统左键状态 Alt={(Native.GetAsyncKeyState(0xA4)&0x8000)!=0} Ctrl={(Native.GetAsyncKeyState(0xA2)&0x8000)!=0}"); break;
            case 0x200:result=machine.Move(point.X,point.Y,threshold);break;
            case 0x202:
                leftButtonHeld=false;
                if(machine.Phase is GesturePhase.Pending or GesturePhase.Active){var reason=ProtectionReason(point);if(reason!=null)CancelForProtection(reason);}
                RecordSignal($"左键松开：阶段 {machine.Phase}");result=machine.Up();break;
            default:if(machine.Phase is GesturePhase.Active or GesturePhase.Draining)result=new(true);break;
        }
        switch(result.Action)
        {
            case GestureAction.Begin:
                var blocked=ProtectionReason(point);if(blocked!=null){CancelForProtection(blocked);return new(true);}
                Interlocked.Increment(ref begunGestures);lastSignal="已超过阈值，显示轮盘";
                RecordSignal(lastSignal);
                MaskMenu();
                // Queued positions belong to this gesture, even if another drag
                // starts before WPF has processed the previous release.
                lock(machine)moves=new MoveBatch();
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
        uint key=ModifierState.Normalize(data.VkCode,data.ScanCode,data.Flags);
        if(data.VkCode is 0x12 or 0xA4 or 0xA5 && modifiers.IsDown((int)key)==up)RecordSignal($"Alt 原始事件：vk={data.VkCode:X} flags={data.Flags:X} simulated={(data.Flags&0x10)!=0} scan={data.ScanCode:X} time={data.Time} extra={data.ExtraInfo}");
        return ProcessKey(key,up)?new IntPtr(1):Native.CallNextHookEx(keyHook,code,w,l);
    }
    bool ProcessKey(uint key,bool up)
    {
        bool changed=modifiers.IsDown((int)key)==up;
        modifiers.Set((int)key,!up);
        if(key==ModifierKey && changed)lastSignal=up?"指定修饰键已松开":"指定左修饰键已按下";
        if((key==ModifierKey || key==0xA5) && changed)RecordSignal($"修饰键 {key:X} {(up?"松开":"按下")}；阶段 {machine.Phase}");
        if(key==ModifierKey && up)
        {
            var r=machine.ModifierUp();
            if(r.Action==GestureAction.ReplayDown && !simulatedInput)Native.Mouse(2);
            if(r.Action==GestureAction.Cancel){Queue(()=>Ended?.Invoke(false));if(!TestMode && !simulatedInput)worker.BeginInvoke(RefreshInputHooks,DispatcherPriority.Background);}
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
        if(!up && key!=ModifierKey && key!=0x1B){if(Busy)RecordSignal($"其它按键 {key:X} 取消手势");CancelCore();}
        return false;
    }
    void MaskMenu() { if(!simulatedInput && ModifierKey==0xA4)Queue(Native.MaskAltMenu); }
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
    internal void TestPointerBurst((int Message,int X,int Y)[] events)=>worker.Invoke(()=>{foreach(var e in events)ProcessPointer(e.Message,new Native.POINT(e.X,e.Y),null,true);});
    internal void TestModifierDown()=>worker.Invoke(()=>ProcessKey((uint)ModifierKey,false));
    internal void TestModifierUp()=>worker.Invoke(()=>ProcessKey((uint)ModifierKey,true));
    internal void TestEscape()=>worker.Invoke(()=> { ProcessKey(0x1B,false);ProcessKey(0x1B,true); });
    internal void TestProtection(string? reason)=>worker.Invoke(()=>testProtectionReason=reason);
    internal void TestDisconnectMouseHook()=>worker.Invoke(()=>{if(mouseHook!=IntPtr.Zero)Native.UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;});
    internal void TestDisconnectKeyboardHook()=>worker.Invoke(()=>{if(keyHook!=IntPtr.Zero)Native.UnhookWindowsHookEx(keyHook);keyHook=IntPtr.Zero;});
    internal void TestStaleRightAltDown()=>worker.Invoke(()=>modifiers.Set(0xA5,true));
    internal void TestUnavailableAsyncState()=>worker.Invoke(()=>ObserveInputState(_=>0,false));
    public void Dispose()
    {
        if(disposed)return;disposed=true;worker.BeginInvokeShutdown(DispatcherPriority.Send);thread.Join(1500);
    }
}
