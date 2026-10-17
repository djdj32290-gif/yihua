using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OrbitLauncher;

// Native Shell registration uses a real WPF-dispatched HWND; no WinForms message loop dependency.
internal sealed class TrayIcon : IDisposable
{
    internal const uint SettingsCommand=1,StartupCommand=2,PauseCommand=3,ExitCommand=4;
    const uint Callback=0x8001;
    readonly HwndSource source;
    readonly Icon icon;
    readonly Guid identity;
    readonly DispatcherTimer retry;
    readonly uint taskbarCreated;
    readonly Action<uint> command;
    readonly Func<bool> paused,autostart;
    bool disposed,useGuid=true;
    string tooltip="一划 · 正在运行 · 左键设置 / 右键退出";
    public bool IsRegistered { get; private set; }
    public bool Removed { get; private set; }
    internal int RegistrationError { get; private set; }

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public uint Size;public IntPtr Hwnd;public uint Id,Flags,CallbackMessage;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Title;
        public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]struct ICONIDENTIFIER { public uint Size;public IntPtr Hwnd;public uint Id;public Guid Guid; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,EntryPoint="Shell_NotifyIconW",SetLastError=true)]static extern bool ShellNotify(uint operation,ref NOTIFYICONDATA data);
    [DllImport("shell32.dll")]static extern int Shell_NotifyIconGetRect(ref ICONIDENTIFIER icon,out Native.RECT rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool AppendMenu(IntPtr menu,uint flags,UIntPtr command,string? label);
    [DllImport("user32.dll")]static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]static extern uint TrackPopupMenu(IntPtr menu,uint flags,int x,int y,int reserved,IntPtr owner,IntPtr rect);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")]static extern uint GetMenuState(IntPtr menu,uint id,uint flags);

    public TrayIcon(Action<uint> command,Func<bool> paused,Func<bool> autostart)
    {
        this.command=command;this.paused=paused;this.autostart=autostart;
        // Stable for this portable location; a moved portable copy receives its own identity.
        identity=new Guid(SHA256.HashData(Encoding.Unicode.GetBytes(Environment.ProcessPath!.ToUpperInvariant())).AsSpan(0,16));
        icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone();
        source=new HwndSource(new HwndSourceParameters("一划托盘消息窗口") { Width=1,Height=1,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x80 });
        source.AddHook(WindowProc); taskbarCreated=RegisterWindowMessage("TaskbarCreated");
        retry=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
        retry.Tick+=(_,_)=>Register(); Register();
    }
    NOTIFYICONDATA Data(uint flags)=>new() { Size=(uint)Marshal.SizeOf<NOTIFYICONDATA>(),Hwnd=source.Handle,Id=1,Guid=useGuid?identity:Guid.Empty,Flags=flags|(useGuid?0x20u:0),CallbackMessage=Callback,Icon=icon.Handle,Tip=tooltip,Info="",Title="" };
    void Register()
    {
        if(disposed)return;
        var data=Data(1|2|4|0x80);
        IsRegistered=ShellNotify(0,ref data) || ShellNotify(1,ref data);
        if(!IsRegistered && useGuid)
        {
            // A locked-down profile can reject GUID registration. HWND + ID remains supported.
            useGuid=false;data=Data(1|2|4|0x80);IsRegistered=ShellNotify(0,ref data) || ShellNotify(1,ref data);
        }
        RegistrationError=IsRegistered?0:Marshal.GetLastWin32Error();
        if(IsRegistered) { var version=Data(0);version.Version=4;ShellNotify(4,ref version);retry.Stop(); }
        else { retry.Interval=TimeSpan.FromSeconds(RegistrationError==5?5:1);retry.Start(); }
    }
    public void Update(string hint)
    {
        tooltip=hint.Length>127?hint[..127]:hint;
        if(!IsRegistered) { Register();return; }
        var data=Data(4|0x80);
        if(!ShellNotify(1,ref data)) { IsRegistered=false;retry.Start(); }
    }
    public void Balloon(string title,string text,bool warning=false)
    {
        if(!IsRegistered)Register();
        if(!IsRegistered)return;
        var data=Data(0x10);data.Title=title.Length>63?title[..63]:title;data.Info=text.Length>255?text[..255]:text;data.InfoFlags=(warning?2u:1u)|0x10;
        ShellNotify(1,ref data);
    }
    internal bool CanLocateIcon()
    {
        var id=new ICONIDENTIFIER { Size=(uint)Marshal.SizeOf<ICONIDENTIFIER>(),Hwnd=source.Handle,Id=1,Guid=useGuid?identity:Guid.Empty };
        return Shell_NotifyIconGetRect(ref id,out _)>=0;
    }
    IntPtr BuildMenu()
    {
        var menu=CreatePopupMenu();
        AppendMenu(menu,2,new UIntPtr(0),paused()?"一划 · 手势已暂停":"一划 · 正在运行");
        AppendMenu(menu,0x800,new UIntPtr(0),null);
        AppendMenu(menu,0,new UIntPtr(SettingsCommand),"设置");
        AppendMenu(menu,autostart()?8u:0,new UIntPtr(StartupCommand),"开机自启动");
        AppendMenu(menu,paused()?8u:0,new UIntPtr(PauseCommand),"暂时停用手势");
        AppendMenu(menu,0x800,new UIntPtr(0),null);
        AppendMenu(menu,0,new UIntPtr(ExitCommand),"退出一划");
        return menu;
    }
    internal bool HasExitMenuItem()
    {
        var menu=BuildMenu();try { return GetMenuItemCount(menu)==7 && GetMenuState(menu,ExitCommand,0)!=uint.MaxValue; }finally { DestroyMenu(menu); }
    }
    internal void Execute(uint id)=>source.Dispatcher.BeginInvoke(()=>command(id));
    void OpenMenu()
    {
        var menu=BuildMenu();
        try
        {
            Native.GetCursorPos(out var point);SetForegroundWindow(source.Handle);
            uint selected=TrackPopupMenu(menu,0x100|2,point.X,point.Y,0,source.Handle,IntPtr.Zero);
            PostMessage(source.Handle,0,IntPtr.Zero,IntPtr.Zero);
            if(selected!=0)Execute(selected);
        }
        finally { DestroyMenu(menu); }
    }
    IntPtr WindowProc(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    {
        if((uint)message==taskbarCreated) { IsRegistered=false;Register(); }
        if((uint)message==Callback)
        {
            int notification=(int)(l.ToInt64()&0xFFFF);
            if(notification is 0x7B or 0x205)OpenMenu();
            else if(notification is 0x400 or 0x401 or 0x202 or 0x203 or 0x405)Execute(SettingsCommand);
            handled=true;
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;retry.Stop();
        var data=Data(0);Removed=!IsRegistered || ShellNotify(2,ref data);IsRegistered=false;
        source.RemoveHook(WindowProc);source.Dispose();icon.Dispose();
    }
}
