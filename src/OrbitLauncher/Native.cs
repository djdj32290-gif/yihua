using System;
using System.Runtime.InteropServices;

namespace OrbitLauncher;
internal static class Native
{
    internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    internal delegate void WinEventProc(IntPtr hook,uint eventType,IntPtr hwnd,int objectId,int childId,uint thread,uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; public POINT(int x,int y) { X=x;Y=y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct MSLLHOOKSTRUCT { public POINT Pt; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KBDLLHOOKSTRUCT { public uint VkCode, ScanCode, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Key; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int Dx,Dy; public uint MouseData,Flags,Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags,Time; public UIntPtr ExtraInfo; }
    internal static readonly UIntPtr Tag = new(0x4F524249);
    [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT pt,uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
    [DllImport("user32.dll", SetLastError=true)] internal static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    internal static void Mouse(uint flags) => SendInput(1, new[] { new INPUT { Type=0, Data=new INPUTUNION { Mouse=new MOUSEINPUT { Flags=flags,ExtraInfo=Tag } } } }, Marshal.SizeOf<INPUT>());
    internal static void Click()
    {
        var down=new INPUT { Type=0,Data=new INPUTUNION { Mouse=new MOUSEINPUT { Flags=2,ExtraInfo=Tag } } };
        var up=down;up.Data.Mouse.Flags=4;SendInput(2,new[] { down,up },Marshal.SizeOf<INPUT>());
    }
    internal static void MaskAltMenu()
    {
        var down = new INPUT { Type=1, Data=new INPUTUNION { Key=new KEYBDINPUT { Vk=0xE8,ExtraInfo=Tag } } };
        var up=down; up.Data.Key.Flags=2; SendInput(2,new[] { down,up },Marshal.SizeOf<INPUT>());
    }
    internal static (RECT Work,double Scale) ScreenAt(int x,int y)
    {
        var m=MonitorFromPoint(new POINT(x,y),2); var mi=new MONITORINFO { Size=Marshal.SizeOf<MONITORINFO>() }; GetMonitorInfo(m,ref mi);
        GetDpiForMonitor(m,0,out var dpi,out _); return(mi.Work,dpi > 0 ? dpi/96d : 1);
    }
}
