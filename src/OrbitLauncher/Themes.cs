using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OrbitLauncher;

public sealed class ThemePalette
{
    readonly Dictionary<string,SolidColorBrush> brushes;
    public string Id {get;}
    public string Name {get;}
    public string Description {get;}
    public bool IsDark {get;}
    public SolidColorBrush PreviewBackground=>Brush("Window");
    public SolidColorBrush PreviewForeground=>Brush("Text");
    public SolidColorBrush PreviewAccent=>Brush("Accent");
    internal IEnumerable<KeyValuePair<string,SolidColorBrush>> Resources=>brushes;
    internal ThemePalette(string id,string name,string description,bool dark,Dictionary<string,string> colors)
    {Id=id;Name=name;Description=description;IsDark=dark;brushes=colors.ToDictionary(p=>p.Key,p=>Ink(p.Value));}
    internal SolidColorBrush Brush(string key)=>brushes[key];
    internal static SolidColorBrush Ink(string hex){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));brush.Freeze();return brush;}
}

internal static class Themes
{
    static Dictionary<string,string> Dark()=>new()
    {
        ["Window"]="#0E1520",["Surface"]="#121D2B",["Card"]="#172232",["List"]="#141E2C",
        ["Text"]="#D9E3F2",["TextStrong"]="#E8EEF9",["Muted"]="#8D9FB9",["Subtle"]="#8194AD",["Border"]="#2D4055",
        ["Accent"]="#3CC8AA",["AccentHover"]="#58DFC2",["AccentText"]="#8EECD6",["OnAccent"]="#092A25",
        ["Selected"]="#203C39",["SelectedBorder"]="#365D56",["Hover"]="#1C2D3E",
        ["Button"]="#233245",["Input"]="#0E1824",["InputBorder"]="#304357",["Selection"]="#287B72",
        ["ToggleOff"]="#354356",["ToggleKnob"]="#BDCADB",["SliderRail"]="#3D4D63",["SliderKnob"]="#EAFFF9",
        ["Combo"]="#152133",["Popup"]="#1B2B40",["Scroll"]="#3B4E66",
        ["DangerBackground"]="#542F3B",["DangerBorder"]="#AD596D",["DangerText"]="#FFD1D8",["Validation"]="#F0BC85",
        ["Hint"]="#14232F",["HintBorder"]="#233E47",["HintText"]="#B9DBD8",
        ["Badge"]="#203046",["BadgeText"]="#ADC4DF",["Status"]="#1A2C31",["StatusBorder"]="#2C4C4B",["StatusText"]="#A7CCCA",
        ["WheelBackground"]="#EE121721",["WheelBorder"]="#327A99C0",["WheelCenter"]="#141C2A",["WheelText"]="#E8EEF9",
        ["WheelMuted"]="#8E9DB5",["WheelLabel"]="#BCC9DD",["WheelAccent"]="#89C9FF",["WheelPath"]="#74FADB",
        ["WheelNode"]="#0D282B",["WheelNodeText"]="#FFFFFF",["WheelConfirm"]="#EE0D1D27",["WheelDelete"]="#FF7D8F"
    };
    static Dictionary<string,string> Light()=>new()
    {
        ["Window"]="#F4F7FA",["Surface"]="#FFFFFF",["Card"]="#FFFFFF",["List"]="#F1F5F8",
        ["Text"]="#263B4B",["TextStrong"]="#172C3A",["Muted"]="#5C7081",["Subtle"]="#64778A",["Border"]="#D3DFE7",
        ["Accent"]="#127E6F",["AccentHover"]="#179681",["AccentText"]="#126C60",["OnAccent"]="#FFFFFF",
        ["Selected"]="#DFF1EB",["SelectedBorder"]="#9ACDBD",["Hover"]="#EAF2F6",
        ["Button"]="#EDF3F6",["Input"]="#F9FBFC",["InputBorder"]="#C6D4DD",["Selection"]="#BDE7DD",
        ["ToggleOff"]="#BCCBD5",["ToggleKnob"]="#FFFFFF",["SliderRail"]="#CDD9E1",["SliderKnob"]="#FFFFFF",
        ["Combo"]="#F8FAFC",["Popup"]="#FFFFFF",["Scroll"]="#9FB2C1",
        ["DangerBackground"]="#FFF0F3",["DangerBorder"]="#CC7890",["DangerText"]="#9F2E49",["Validation"]="#95551C",
        ["Hint"]="#E8F4F0",["HintBorder"]="#C0DED5",["HintText"]="#245D55",
        ["Badge"]="#EDF2F8",["BadgeText"]="#4B6680",["Status"]="#E7F3EE",["StatusBorder"]="#BADACE",["StatusText"]="#2D665A",
        ["WheelBackground"]="#F5F8FBFD",["WheelBorder"]="#A2C5D4E0",["WheelCenter"]="#FFFFFF",["WheelText"]="#1D3645",
        ["WheelMuted"]="#5C7284",["WheelLabel"]="#334F62",["WheelAccent"]="#127E6F",["WheelPath"]="#127E6F",
        ["WheelNode"]="#F8FFFC",["WheelNodeText"]="#125D50",["WheelConfirm"]="#F5FFFFFF",["WheelDelete"]="#B34762"
    };
    static ThemePalette Create(string id,string name,string description,bool dark,Dictionary<string,string> colors,params (string Key,string Value)[] overrides)
    {foreach(var p in overrides)colors[p.Key]=p.Value;return new ThemePalette(id,name,description,dark,colors);}
    internal static IReadOnlyList<ThemePalette> Options {get;}=Array.AsReadOnly(new[]
    {
        Create("Obsidian","曜石黑","深色石墨底，搭配清透青绿。",true,Dark()),
        Create("Cloud","云雾白","干净的雾白与薄荷绿，明亮轻盈。",false,Light()),
        Create("Midnight","午夜蓝","深海蓝底，搭配柔和冰蓝。",true,Dark(),
            ("Window","#101729"),("Surface","#151F35"),("Card","#19253E"),("List","#121E34"),
            ("Accent","#90AEFF"),("AccentHover","#B0C5FF"),("AccentText","#BBCEFF"),("OnAccent","#122343"),
            ("Selected","#293B60"),("SelectedBorder","#4C6698"),("Hint","#1A2A46"),("HintBorder","#344D73"),
            ("HintText","#C4D5FA"),("Status","#233556"),("StatusBorder","#435D86"),("StatusText","#BCD1F3"),
            ("WheelBackground","#F0141E33"),("WheelCenter","#16243C"),("WheelAccent","#9BBEFF"),("WheelPath","#A9CFFF"),("WheelNode","#172D4B")),
        Create("Sand","暖砂","奶油白与暖铜色，柔和安静。",false,Light(),
            ("Window","#F7F2E9"),("Surface","#FFFCF7"),("Card","#FFFCF7"),("List","#F3ECE1"),
            ("Text","#4B3C31"),("TextStrong","#352A23"),("Muted","#786756"),("Subtle","#7B6C5E"),("Border","#DED1C1"),
            ("Accent","#9D5E37"),("AccentHover","#B06D43"),("AccentText","#88522E"),("Selected","#F1E2D0"),("SelectedBorder","#CDAA82"),
            ("Hover","#F4ECE1"),("Button","#F3EDE4"),("Input","#FFFEFA"),("InputBorder","#D7C7B5"),("Selection","#E9CFAD"),
            ("Combo","#FFFCF7"),("Popup","#FFFCF7"),("Hint","#F1E8D8"),("HintBorder","#DBCAAE"),("HintText","#735238"),
            ("Status","#F0E6D5"),("StatusBorder","#D9C4A7"),("StatusText","#775737"),("Badge","#EEE6D9"),("BadgeText","#79624B"),
            ("WheelBackground","#F5FFFAF1"),("WheelCenter","#FFFCF7"),("WheelText","#493526"),("WheelLabel","#66503C"),
            ("WheelMuted","#806A54"),("WheelAccent","#9D5E37"),("WheelPath","#9D5E37"),("WheelNode","#FFF9EF"),("WheelNodeText","#73421F"),("WheelConfirm","#F5FFFCF7"))
    });
    internal static ThemePalette Get(string? id){for(int i=0;i<Options.Count;i++)if(Options[i].Id==id)return Options[i];return Options[0];}
    internal static string Normalize(string? id)=>Get(id).Id;
    internal static ThemePalette Current {get;private set;}=Options[0];
    static Application? lastApplication;
    internal static void Apply(string id)
    {
        var palette=Get(id);var app=Application.Current;
        if(app==null)return;
        if(ReferenceEquals(lastApplication,app) && ReferenceEquals(Current,palette))return;
        Current=palette;lastApplication=app;
        foreach(var entry in palette.Resources)app.Resources["Theme."+entry.Key]=entry.Value;
        foreach(var kind in Enum.GetValues<LaunchKind>())app.Resources["Theme.Category"+kind]=ItemAppearance.For(kind,palette.Id).Accent;
        foreach(Window window in app.Windows)SettingsWindow.StyleWindow(window);
    }
}
