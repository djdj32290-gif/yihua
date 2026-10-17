using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace OrbitLauncher;
internal static class StartupService
{
    const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name="OrbitLauncher";
    static string Command => "\""+Environment.ProcessPath+"\" --background";
    public static bool Enabled { get { using var k=Registry.CurrentUser.OpenSubKey(Key); return string.Equals(k?.GetValue(Name) as string,Command,StringComparison.OrdinalIgnoreCase); } }
    public static void Set(bool enabled) { using var k=Registry.CurrentUser.CreateSubKey(Key); if(enabled) k.SetValue(Name,Command); else k.DeleteValue(Name,false); }
}
internal static class Launcher
{
    public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
    public static bool IsUrl(string value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https";
    public static string? Validate(LaunchItem item)
    {
        var target=Expand(item.Target);
        if(string.IsNullOrWhiteSpace(target)) return "请选择应用、文件、文件夹，或输入 http/https 网址。";
        if(!IsUrl(target) && !File.Exists(target) && !Directory.Exists(target)) return "目标不存在："+target;
        var work=Expand(item.WorkingDirectory);
        if(work.Length>0 && !Directory.Exists(work)) return "工作目录不存在："+work;
        return null;
    }
    public static string? Open(LaunchItem item)
    {
        var error=Validate(item); if(error!=null) return error;
        try
        {
            var target=Expand(item.Target);
            // Explorer is an executable entry point for directories, independent of folder associations.
            var p=Directory.Exists(target)
                ? new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")) { UseShellExecute=true }
                : new ProcessStartInfo(target) { UseShellExecute=true,Arguments=item.Arguments };
            if(Directory.Exists(target))p.ArgumentList.Add(target);
            if(item.WorkingDirectory.Trim().Length>0) p.WorkingDirectory=Expand(item.WorkingDirectory);
            Process.Start(p); return null;
        }
        catch(Exception ex) { return "无法打开「"+item.Name+"」："+ex.Message; }
    }
}
internal static class Icons
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct SHFILEINFO { public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string DisplayName; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string TypeName; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path,uint attributes,ref SHFILEINFO info,uint size,uint flags);
    static readonly Dictionary<string,ImageSource> cache=new(StringComparer.OrdinalIgnoreCase);
    public static ImageSource For(string target)
    {
        target=Launcher.Expand(target);
        if(cache.TryGetValue(target,out var cached)) return cached;
        ImageSource? image=null;
        try
        {
            var info=new SHFILEINFO();
            var path=Launcher.IsUrl(target) ? ".html" : target;
            var flags=0x100u | (Launcher.IsUrl(target) || (!File.Exists(path)&&!Directory.Exists(path)) ? 0x10u : 0);
            if(SHGetFileInfo(path,0x80,ref info,(uint)Marshal.SizeOf<SHFILEINFO>(),flags)!=IntPtr.Zero && info.Icon!=IntPtr.Zero)
            { try { image=Imaging.CreateBitmapSourceFromHIcon(info.Icon,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(48,48)); image.Freeze(); } finally { Native.DestroyIcon(info.Icon); } }
        } catch { }
        image ??= Default(); if(cache.Count>=256)cache.Clear(); cache[target]=image; return image;
    }
    static ImageSource Default()
    {
        var group=new DrawingGroup(); using(var dc=group.Open())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(54,72,102)),null,new Rect(2,2,44,44),12,12);
            var pen=new Pen(new SolidColorBrush(Color.FromRgb(177,216,255)),3);
            dc.DrawRoundedRectangle(null,pen,new Rect(13,13,22,22),4,4);
            dc.DrawLine(pen,new Point(20,28),new Point(30,18)); dc.DrawLine(pen,new Point(22,18),new Point(30,18)); dc.DrawLine(pen,new Point(30,18),new Point(30,26));
        }
        var img=new DrawingImage(group); img.Freeze(); return img;
    }
}
