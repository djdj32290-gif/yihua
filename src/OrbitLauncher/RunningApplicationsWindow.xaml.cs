using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OrbitLauncher;
public partial class RunningApplicationsWindow : Window
{
    internal sealed record ApplicationEntry(string Name,string WindowTitle,string Path)
    {
        public ImageSource Icon=>Icons.For(Path);
        internal static ApplicationEntry FromWindow(ForegroundGuard.ApplicationWindow window)
        {
            string name=System.IO.Path.GetFileNameWithoutExtension(window.Path);
            try {var description=FileVersionInfo.GetVersionInfo(window.Path).FileDescription;if(!string.IsNullOrWhiteSpace(description))name=description.Trim();}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception){}
            return new(name,window.Title,window.Path);
        }
    }
    List<ApplicationEntry> applications=new();
    internal ApplicationEntry? SelectedApplication {get;private set;}
    public RunningApplicationsWindow()
    {
        InitializeComponent();SourceInitialized+=(_,_)=>SettingsWindow.StyleWindow(this);
        Loaded+=(_,_)=>{RefreshApplications();SearchBox.Focus();};
    }
    internal void RefreshApplications()
    {
        string? selected=(ApplicationsList.SelectedItem as ApplicationEntry)?.Path;
        try
        {
            applications=ForegroundGuard.RunningWindows(distinctPaths:true).Where(x=>File.Exists(x.Path))
                .Select(ApplicationEntry.FromWindow).OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
            ErrorStatus.Text="";FilterApplications(selected);
        }
        catch(Exception ex){ErrorStatus.Text="读取运行程序失败，请重试刷新："+ex.Message;}
    }
    void FilterApplications(string? selected=null)
    {
        if(ApplicationsList==null)return;
        selected??=(ApplicationsList.SelectedItem as ApplicationEntry)?.Path;
        string query=SearchBox.Text.Trim();
        var visible=applications.Where(x=>query.Length==0 || x.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase) || x.WindowTitle.Contains(query,StringComparison.CurrentCultureIgnoreCase) || x.Path.Contains(query,StringComparison.OrdinalIgnoreCase)).ToList();
        ApplicationsList.ItemsSource=visible;
        ApplicationsList.SelectedItem=visible.FirstOrDefault(x=>string.Equals(x.Path,selected,StringComparison.OrdinalIgnoreCase));
        EmptyState.Text=query.Length==0?"没有可添加的运行程序。请先打开应用，再点击刷新。":"没有匹配的程序，试试其它名称或路径。";
        EmptyState.Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;
        ListStatus.Text=$"{visible.Count} 个程序 · 仅显示可读取路径的应用窗口，相同程序合并显示。";
        UseButton.IsEnabled=ApplicationsList.SelectedItem is ApplicationEntry;
    }
    internal bool ChooseSelected()
    {
        if(ApplicationsList.SelectedItem is not ApplicationEntry app)return false;
        if(!File.Exists(app.Path)){ErrorStatus.Text="程序文件已不可用，请刷新列表后重新选择。";return false;}
        SelectedApplication=app;DialogResult=true;return true;
    }
    void Refresh_Click(object sender,RoutedEventArgs e)=>RefreshApplications();
    void Search_Changed(object sender,TextChangedEventArgs e)=>FilterApplications();
    void Selection_Changed(object sender,SelectionChangedEventArgs e){if(UseButton!=null)UseButton.IsEnabled=ApplicationsList.SelectedItem is ApplicationEntry;}
    void Use_Click(object sender,RoutedEventArgs e)=>ChooseSelected();
    void Application_DoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ApplicationsList,source) is ListBoxItem)ChooseSelected();
    }
    void Application_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){ChooseSelected();e.Handled=true;}}
    void Cancel_Click(object sender,RoutedEventArgs e)=>Close();
}
