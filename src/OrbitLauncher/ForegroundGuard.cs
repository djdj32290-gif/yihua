using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;

namespace OrbitLauncher;
internal sealed class ForegroundGuard
{
    internal sealed record ApplicationWindow(string Name,string Title,string Path="")
    {
        public ImageSource Icon=>Icons.For(Path.Length>0?Path:Name);
    }
    delegate bool EnumProc(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder value,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder value,int count);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Native.RECT rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder value,ref int length);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out Native.RECT value,int size);
    IntPtr cachedWindow;
    uint cachedProcess;
    long cachedAt;
    string cachedName="";
    internal void Invalidate()=>cachedWindow=IntPtr.Zero;
    internal static string NormalizeName(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return "";
        string name=value.Trim().Trim('"').Replace('/','\\');name=name[(name.LastIndexOf('\\')+1)..].Trim();
        if(name.Length==0 || name.IndexOfAny(new[]{'*','?','\r','\n',';','|'})>=0)return "";
        return name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?name:name+".exe";
    }
    internal static bool IsExcluded(string process,IEnumerable<string> applications)=>applications.Any(name=>string.Equals(NormalizeName(name),NormalizeName(process),StringComparison.OrdinalIgnoreCase));
    internal static bool CoversMonitor(Native.RECT window,Native.RECT monitor)=>window.Right>window.Left && window.Bottom>window.Top && window.Left<=monitor.Left+2 && window.Top<=monitor.Top+2 && window.Right>=monitor.Right-2 && window.Bottom>=monitor.Bottom-2;
    internal static string? Decide(Settings settings,string process,bool fullscreen,bool identityReadable=true)
    {
        if(!settings.GameModeEnabled)return null;
        if(!identityReadable)return "前台程序无法识别，游戏保护已停用本次手势";
        if(IsExcluded(process,settings.BlockedApplications))return "游戏保护："+NormalizeName(process);
        return settings.PauseInFullscreen && fullscreen?"全屏应用保护":null;
    }
    internal string? Reason(Settings settings,Native.POINT? pointer=null)
    {
        if(!settings.GameModeEnabled)return null;
        var hwnd=GetForegroundWindow();if(hwnd==IntPtr.Zero)return Decide(settings,"",false,false);
        var reason=ReasonForWindow(settings,hwnd);if(reason!=null)return reason;
        // Low-level down arrives before Windows activates the clicked window.
        // Check its root too, so an Alt-click into a windowed game stays native.
        if(pointer.HasValue){var target=GetAncestor(WindowFromPoint(pointer.Value),2);if(target!=IntPtr.Zero && target!=hwnd)return ReasonForWindow(settings,target);}
        return null;
    }
    string? ReasonForWindow(Settings settings,IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd,out uint pid);
        if(pid==Environment.ProcessId || IsShell(hwnd))return null;
        if(hwnd!=cachedWindow || pid!=cachedProcess || Environment.TickCount64-cachedAt>1500)
        {cachedWindow=hwnd;cachedProcess=pid;cachedName=NormalizeName(ReadProcessPath(pid));cachedAt=Environment.TickCount64;}
        if(cachedName.Length==0)return Decide(settings,"",false,false);
        bool fullscreen=false;
        if(settings.PauseInFullscreen && !IsIconic(hwnd))
        {
            var monitor=MonitorFromWindow(hwnd,2);var info=new Native.MONITORINFO{Size=Marshal.SizeOf<Native.MONITORINFO>()};
            bool hasBounds=DwmGetWindowAttribute(hwnd,9,out var bounds,Marshal.SizeOf<Native.RECT>())>=0;
            if(!hasBounds || bounds.Right<=bounds.Left)hasBounds=GetWindowRect(hwnd,out bounds);
            fullscreen=hasBounds && Native.GetMonitorInfo(monitor,ref info) && CoversMonitor(bounds,info.Monitor);
        }
        return Decide(settings,cachedName,fullscreen);
    }
    static bool IsShell(IntPtr hwnd)
    {var name=new StringBuilder(128);GetClassName(hwnd,name,name.Capacity);return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";}
    internal static string ReadProcessPath(uint pid)
    {
        var process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)return "";
        try {var path=new StringBuilder(4096);int count=path.Capacity;return QueryFullProcessImageName(process,0,path,ref count)?path.ToString():"";}finally{CloseHandle(process);}
    }
    internal static List<ApplicationWindow> RunningWindows(bool distinctPaths=false)
    {
        var result=new List<ApplicationWindow>();
        EnumWindows((hwnd,_)=>
        {
            if(!IsWindowVisible(hwnd) || IsShell(hwnd))return true;
            GetWindowThreadProcessId(hwnd,out uint pid);if(pid==Environment.ProcessId)return true;
            var title=new StringBuilder(512);if(GetWindowText(hwnd,title,title.Capacity)==0)return true;
            string path=ReadProcessPath(pid),name=NormalizeName(path);if(name.Length>0)result.Add(new(name,title.ToString(),path));return true;
        },IntPtr.Zero);
        return result.GroupBy(x=>distinctPaths?x.Path:x.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
