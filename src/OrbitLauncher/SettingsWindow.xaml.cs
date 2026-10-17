using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
using Microsoft.Win32;
using System.Windows.Input;
using System.Collections.Generic;
using System.IO;

namespace OrbitLauncher;
public partial class SettingsWindow : Window
{
    readonly AppController controller;
    readonly DispatcherTimer saveTimer,statusTimer;
    bool loading=true;
    DateTime lastStatusWrite=DateTime.MinValue;
    List<ForegroundGuard.ApplicationWindow> runningApps=new();
    Settings Config=>controller.Config;
    public SettingsWindow(AppController controller)
    {
        this.controller=controller;InitializeComponent();Title="一划 1.3.2 · 设置";
        saveTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(300)};saveTimer.Tick+=(_,_)=>Flush();
        statusTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(500)};statusTimer.Tick+=(_,_)=>RefreshRuntime();
        SizeSlider.Value=Config.Diameter;CancelSlider.Value=Config.CancelRadius;ThresholdSlider.Value=Config.DragThreshold;
        ThemeBox.ItemsSource=Themes.Options;ThemeBox.SelectedValue=Config.Theme;ThemeHint.Text=Themes.Get(Config.Theme).Description+" 设置与轮盘同步换肤。";
        ModifierBox.SelectedIndex=Config.Modifier switch {"LeftControl"=>1,"LeftShift"=>2,_=>0};
        GameModeCheck.IsChecked=Config.GameModeEnabled;FullscreenCheck.IsChecked=Config.PauseInFullscreen;RefreshAppList();
        WheelPreview.EditRequested+=OpenEditor;
        WheelPreview.SwapRequested+=SwapItems;
        WheelPreview.DeleteSelectionChanged+=RefreshDeleteActions;
        SettingsTabs.SelectionChanged+=(_,e)=>{if(!loading && ReferenceEquals(e.Source,SettingsTabs) && SettingsTabs.SelectedIndex==2)RefreshRunningApps();};
        loading=false;RefreshPreview();UpdateLabels();RefreshChecks();
        Closing+=(_,e)=> {if(!Flush() && !controller.IsExiting)e.Cancel=true;if(!controller.TrayRegistered && !controller.IsExiting && !controller.IsTesting){e.Cancel=true;SaveStatus.Text="托盘未注册，请先点“退出一划”，并检查项目内便携版目录的运行权限。";}};
        SourceInitialized+=(_,_)=>StyleWindow(this);
        Loaded+=(_,_)=> {RefreshRuntime();statusTimer.Start();};Closed+=(_,_)=>statusTimer.Stop();
    }
    internal static void StyleWindow(Window window) {window.Icon=Branding.Logo;int dark=Themes.Current.IsDark?1:0,rounded=2;var h=new WindowInteropHelper(window).Handle;if(h==IntPtr.Zero)return;Native.DwmSetWindowAttribute(h,20,ref dark,4);Native.DwmSetWindowAttribute(h,33,ref rounded,4);}
    void RefreshRuntime() {RuntimeTrayStatus.Text=controller.TrayRegistered?"系统托盘 · 已连接":controller.TrayStatus;RuntimeInputStatus.Text=controller.InputStatus;if(DateTime.UtcNow-lastStatusWrite>TimeSpan.FromSeconds(3)){controller.SaveRuntimeStatus();lastStatusWrite=DateTime.UtcNow;}}
    public void RefreshChecks()
    {
        StartupCheck.IsChecked=StartupService.Enabled;PauseCheck.IsChecked=controller.Paused;
        if(!controller.TrayRegistered)SaveStatus.Text=ProcessIntegrity.Level is >=0 and <8192?"当前目录受低完整性限制。请使用项目内已验证的便携版目录。":$"系统托盘未就绪（错误 {controller.TrayError}）。";
    }
    internal void RefreshPreview()
    {
        WheelPreview.Width=WheelPreview.Height=Config.Diameter+32;WheelPreview.Selected=-1;WheelPreview.Configure(Config.Copy());
        CountLabel.Text=$"{Config.Items.Count} / 16 个选项";
        RefreshDeleteActions();
    }
    void DeleteMode_Click(object sender,RoutedEventArgs e)=>SetDeleteMode(!WheelPreview.DeleteMode);
    internal void SetDeleteMode(bool enabled)
    {controller.Recover();WheelPreview.SetDeleteMode(enabled);SaveStatus.Text=enabled?"点击多个扇区勾选，再点“删除所选”；再次点击可取消勾选。":"已退出删除模式，未删除的配置保持不变。";}
    void RefreshDeleteActions()
    {
        bool active=WheelPreview.DeleteMode;int count=WheelPreview.DeleteSelection.Count;
        DeleteModeButton.Content=active?"退出删除模式":"批量删除";
        DeleteSelectedButton.Visibility=active?Visibility.Visible:Visibility.Collapsed;
        DeleteSelectedButton.Content=$"删除所选（{count}）";DeleteSelectedButton.IsEnabled=count>0;
        WheelHint.Text=active?"点击勾选 / 再点取消 / 删除所选配置":"点击编辑  /  拖动交换  /  中心添加";
    }
    void DeleteSelected_Click(object sender,RoutedEventArgs e)=>DeleteSelectedItems();
    internal bool DeleteSelectedItems()
    {
        var indices=WheelPreview.DeleteSelection.Where(i=>i>=0 && i<Config.Items.Count).ToHashSet();
        if(!WheelPreview.DeleteMode || indices.Count==0)return false;
        var before=Config.Items.ToList();
        try
        {
            Config.Items=Config.Items.Where((_,i)=>!indices.Contains(i)).ToList();controller.Apply();
            WheelPreview.SetDeleteMode(false);RefreshPreview();SaveStatus.Text=$"已删除 {indices.Count} 个选项的配置；目标文件未删除。";return true;
        }
        catch(Exception ex){Config.Items=before;try{controller.Apply();}catch{}RefreshPreview();SaveStatus.Text="删除保存失败："+ex.Message;return false;}
    }
    internal ItemEditorWindow CreateEditor(int index)
    {
        if(index < -1 || index>=Config.Items.Count)throw new ArgumentOutOfRangeException(nameof(index));
        return new ItemEditorWindow(index>=0?Config.Items[index].Copy():new LaunchItem(),index,Config.Items.Count,CommitEditor,controller.Launch){Owner=this};
    }
    void OpenEditor(int index)
    {
        if(WheelPreview.DeleteMode)return;
        if(index<0 && Config.Items.Count>=16){SaveStatus.Text="已达到 16 个选项，请点击已有选项编辑或删除。";return;}
        controller.Recover();CreateEditor(index).ShowDialog();
    }
    internal void CommitEditor(LaunchItem draft,int originalIndex,int position,bool delete)
    {
        var before=Config.Items.Select(i=>i.Copy()).ToList();
        try
        {
            if(originalIndex<0 && Config.Items.Count>=16)throw new InvalidOperationException("最多支持 16 个选项。");
            if(originalIndex>=0)Config.Items.RemoveAt(originalIndex);
            if(!delete)Config.Items.Insert(Math.Clamp(position,0,Config.Items.Count),draft.Copy());
            controller.Apply();RefreshPreview();SaveStatus.Text=delete?"已删除选项，轮盘已更新。":"已保存选项，立即生效。";
        }
        catch {Config.Items=before;try {controller.Apply();}catch {}RefreshPreview();throw;}
    }
    void SwapItems(int source,int target)
    {
        if(source==target || source<0 || target<0 || source>=Config.Items.Count || target>=Config.Items.Count)return;
        (Config.Items[source],Config.Items[target])=(Config.Items[target],Config.Items[source]);
        try{controller.Apply();SaveStatus.Text="已交换两个选项的位置，立即生效。";}
        catch(Exception ex){(Config.Items[source],Config.Items[target])=(Config.Items[target],Config.Items[source]);try{controller.Apply();}catch{}SaveStatus.Text="交换保存失败："+ex.Message;}
        RefreshPreview();
    }
    void ScheduleSave(){saveTimer.Stop();saveTimer.Start();SaveStatus.Text="正在保存全局设置…";}
    internal bool Flush()
    {
        saveTimer.Stop();
        try {controller.Apply();RefreshPreview();SaveStatus.Text="已保存 · 立即生效。";return true;}
        catch(Exception ex){SaveStatus.Text="保存失败："+ex.Message;return false;}
    }
    void UpdateLabels()
    {
        SizeLabel.Text=$"轮盘直径  {Config.Diameter} DIP";CancelLabel.Text=$"中心取消半径  {Config.CancelRadius} DIP";ThresholdLabel.Text=$"触发拖动距离  {Config.DragThreshold} DIP";
        GestureHint.Text=$"按住{(Config.Modifier=="LeftControl"?"左 Ctrl":Config.Modifier=="LeftShift"?"左 Shift":"左 Alt")} + 鼠标左键，拖动超过阈值唤出轮盘";
    }
    void Appearance_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {if(loading)return;Config.Diameter=(int)SizeSlider.Value;Config.CancelRadius=(int)CancelSlider.Value;Config.DragThreshold=(int)ThresholdSlider.Value;Config.Normalize();CancelSlider.Value=Config.CancelRadius;UpdateLabels();RefreshPreview();ScheduleSave();}
    void Modifier_Changed(object sender,SelectionChangedEventArgs e){if(loading)return;Config.Modifier=ModifierBox.SelectedIndex switch {1=>"LeftControl",2=>"LeftShift",_=>"LeftAlt"};UpdateLabels();Flush();}
    void Theme_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(loading || ThemeBox.SelectedItem is not ThemePalette palette)return;
        string previous=Config.Theme;Config.Theme=palette.Id;
        if(!Flush()){Config.Theme=previous;Themes.Apply(previous);loading=true;ThemeBox.SelectedValue=previous;loading=false;RefreshPreview();return;}
        ThemeHint.Text=palette.Description+" 设置与轮盘同步换肤。";SaveStatus.Text=$"已应用「{palette.Name}」，下次启动仍保留。";
    }
    void Startup_Click(object sender,RoutedEventArgs e){controller.SetStartup(StartupCheck.IsChecked==true);RefreshChecks();}
    void Pause_Click(object sender,RoutedEventArgs e){controller.Paused=PauseCheck.IsChecked==true;}
    void RefreshAppList()
    {
        string? selection=(BlockedAppList.SelectedItem as ForegroundGuard.ApplicationWindow)?.Name;
        BlockedAppList.ItemsSource=Config.BlockedApplications.Select(name=>new ForegroundGuard.ApplicationWindow(name,"",Config.ApplicationIconPaths.GetValueOrDefault(name,""))).ToArray();
        if(selection!=null)BlockedAppList.SelectedItem=BlockedAppList.Items.OfType<ForegroundGuard.ApplicationWindow>().FirstOrDefault(x=>string.Equals(x.Name,selection,StringComparison.OrdinalIgnoreCase));
        FullscreenCheck.IsEnabled=Config.GameModeEnabled;ProtectedCount.Text=$"保护名单 · {Config.BlockedApplications.Count}";
        var selected=(RunningAppsList.SelectedItem as ForegroundGuard.ApplicationWindow)?.Name;
        var available=runningApps.Where(app=>!Config.BlockedApplications.Contains(app.Name,StringComparer.OrdinalIgnoreCase)).ToArray();
        RunningAppsList.ItemsSource=available;RunningAppsList.SelectedItem=available.FirstOrDefault(x=>x.Name==selected);
        RunningCount.Text=$"运行中的程序 · {available.Length}";RunningEmpty.Visibility=available.Length==0?Visibility.Visible:Visibility.Collapsed;
    }
    void Protection_Click(object sender,RoutedEventArgs e){if(loading)return;Config.GameModeEnabled=GameModeCheck.IsChecked==true;Config.PauseInFullscreen=FullscreenCheck.IsChecked==true;RefreshAppList();Flush();}
    void RefreshRunningApps()
    {
        try
        {
            runningApps=ForegroundGuard.RunningWindows();bool metadataChanged=false;
            foreach(var app in runningApps)
                if(app.Path.Length>0 && Config.BlockedApplications.Contains(app.Name,StringComparer.OrdinalIgnoreCase) && Config.ApplicationIconPaths.GetValueOrDefault(app.Name)!=app.Path)
                {Config.ApplicationIconPaths[app.Name]=app.Path;metadataChanged=true;}
            RefreshAppList();if(metadataChanged)Flush();
        }
        catch(Exception ex){SaveStatus.Text="读取运行窗口失败："+ex.Message;}
    }
    void RefreshRunningApps_Click(object sender,RoutedEventArgs e)=>RefreshRunningApps();
    void AddRunningApp_Click(object sender,RoutedEventArgs e){if(RunningAppsList.SelectedItem is ForegroundGuard.ApplicationWindow app)AddProtectedApp(app.Name);}
    void RunningApps_DoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left || e.OriginalSource is not DependencyObject source)return;
        if(ItemsControl.ContainerFromElement(RunningAppsList,source) is ListBoxItem item && item.DataContext is ForegroundGuard.ApplicationWindow app)
        {RunningAppsList.SelectedItem=app;AddProtectedApp(app.Path.Length>0?app.Path:app.Name);e.Handled=true;}
    }
    void AddAppFile_Click(object sender,RoutedEventArgs e){var dialog=new OpenFileDialog{Title="选择要停用手势的游戏或应用",Filter="应用程序|*.exe",CheckFileExists=true};if(dialog.ShowDialog(this)==true)AddProtectedApp(dialog.FileName);}
    void AddProtectedApp(string value)
    {
        string name=ForegroundGuard.NormalizeName(value);if(name.Length==0)return;
        bool duplicate=Config.BlockedApplications.Contains(name,StringComparer.OrdinalIgnoreCase);if(!duplicate)Config.BlockedApplications.Add(name);
        string path=File.Exists(value)?value:runningApps.FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase))?.Path??"";
        bool iconChanged=path.Length>0 && Config.ApplicationIconPaths.GetValueOrDefault(name)!=path;
        if(path.Length>0)Config.ApplicationIconPaths[name]=path;
        RefreshAppList();BlockedAppList.SelectedItem=BlockedAppList.Items.OfType<ForegroundGuard.ApplicationWindow>().FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase));BlockedAppList.ScrollIntoView(BlockedAppList.SelectedItem);
        if(duplicate && !iconChanged)SaveStatus.Text=$"{name} 已在保护名单中。";else if(Flush())SaveStatus.Text=$"已将 {name} 加入保护名单。";
    }
    void RemoveApp_Click(object sender,RoutedEventArgs e){int index=BlockedAppList.SelectedIndex;if(index<0)return;Config.ApplicationIconPaths.Remove(Config.BlockedApplications[index]);Config.BlockedApplications.RemoveAt(index);RefreshAppList();Flush();}
    void Done_Click(object sender,RoutedEventArgs e)=>Close();
    void Exit_Click(object sender,RoutedEventArgs e)=>controller.Exit();
}
