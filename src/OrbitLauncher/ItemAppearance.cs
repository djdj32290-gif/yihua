using System;
using System.IO;
using System.Windows.Media;

namespace OrbitLauncher;
internal enum LaunchKind {Web,Application,Folder,File}
internal static class ItemAppearance
{
    internal sealed record Palette(string Label,SolidColorBrush Accent,SolidColorBrush Background,SolidColorBrush Hover,SolidColorBrush Border);
    static SolidColorBrush Ink(string hex){var value=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));value.Freeze();return value;}
    static readonly Palette Web=new("网页",Ink("#4DD6C3"),Ink("#173A3B"),Ink("#23685F"),Ink("#315C59"));
    static readonly Palette App=new("应用",Ink("#7BAFFF"),Ink("#1E2D48"),Ink("#365E99"),Ink("#3C5373"));
    static readonly Palette Folder=new("文件夹",Ink("#F0C36D"),Ink("#3D3322"),Ink("#796035"),Ink("#675539"));
    static readonly Palette File=new("文件",Ink("#C4A3F4"),Ink("#342941"),Ink("#675184"),Ink("#584367"));
    static readonly Palette LightWeb=new("网页",Ink("#147E69"),Ink("#E1F3EB"),Ink("#B6E4D3"),Ink("#B3D6C9"));
    static readonly Palette LightApp=new("应用",Ink("#356AC1"),Ink("#E7EFFC"),Ink("#BED4F6"),Ink("#C0D2EA"));
    static readonly Palette LightFolder=new("文件夹",Ink("#94620D"),Ink("#FAEED3"),Ink("#EDDAA6"),Ink("#DBCCA4"));
    static readonly Palette LightFile=new("文件",Ink("#8054B2"),Ink("#F0E8F9"),Ink("#DBC9F0"),Ink("#D1BEE4"));
    internal static LaunchKind Classify(string target)
    {
        string path=Launcher.Expand(target);
        if(Launcher.IsUrl(path))return LaunchKind.Web;
        if(Directory.Exists(path))return LaunchKind.Folder;
        string extension=Path.GetExtension(path).ToLowerInvariant();
        return extension switch {".url"=>LaunchKind.Web,".exe" or ".lnk" or ".com" or ".bat" or ".cmd" or ".ps1" or ".msi"=>LaunchKind.Application,_=>LaunchKind.File};
    }
    internal static Palette For(LaunchKind kind)=>kind switch{LaunchKind.Web=>Web,LaunchKind.Application=>App,LaunchKind.Folder=>Folder,_=>File};
    internal static Palette For(LaunchKind kind,string theme)=>Themes.Get(theme).IsDark?For(kind):kind switch{LaunchKind.Web=>LightWeb,LaunchKind.Application=>LightApp,LaunchKind.Folder=>LightFolder,_=>LightFile};
}
