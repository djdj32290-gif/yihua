using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace OrbitLauncher;
public partial class ItemEditorWindow : Window
{
    readonly int originalIndex;
    readonly Action<LaunchItem,int,int,bool> commit;
    readonly Action<LaunchItem> launch;
    public ItemEditorWindow(LaunchItem draft,int index,int count,Action<LaunchItem,int,int,bool> commit,Action<LaunchItem> launch)
    {
        InitializeComponent();originalIndex=index;this.commit=commit;this.launch=launch;
        bool adding=index<0;Title=adding?"添加选项":"选项详情";EditorHeading.Text=Title;DeleteButton.Visibility=adding?Visibility.Collapsed:Visibility.Visible;
        NameBox.Text=adding?"":draft.Name;TargetBox.Text=draft.Target;ArgsBox.Text=draft.Arguments;WorkBox.Text=draft.WorkingDirectory;
        int total=count+(adding?1:0);for(int i=0;i<total;i++)PositionBox.Items.Add($"第 {i+1} 项"+(i==0?" · 正上方":""));PositionBox.SelectedIndex=adding?count:index;
        SourceInitialized+=(_,_)=>SettingsWindow.StyleWindow(this);Loaded+=(_,_)=>NameBox.Focus();
    }
    LaunchItem Draft()=>new(){Name=NameBox.Text.Trim(),Target=TargetBox.Text.Trim(),Arguments=ArgsBox.Text,WorkingDirectory=WorkBox.Text.Trim()};
    void Save_Click(object sender,RoutedEventArgs e)=>SaveItem();
    internal bool SaveItem()
    {
        var draft=Draft();if(string.IsNullOrWhiteSpace(draft.Name)){EditorStatus.Text="请填写显示名称。";NameBox.Focus();return false;}
        var error=Launcher.Validate(draft);if(error!=null){EditorStatus.Text=error;return false;}
        return Commit(draft,false);
    }
    bool Commit(LaunchItem draft,bool delete)
    {try {commit(draft,originalIndex,PositionBox.SelectedIndex,delete);Close();return true;}catch(Exception ex){EditorStatus.Text="保存失败："+ex.Message;return false;}}
    void Delete_Click(object sender,RoutedEventArgs e)=>DeleteItem();
    internal bool DeleteItem()=>originalIndex>=0 && Commit(Draft(),true);
    void Cancel_Click(object sender,RoutedEventArgs e)=>Close();
    void BrowseFile_Click(object sender,RoutedEventArgs e){var dialog=new OpenFileDialog{Title="选择应用、快捷方式或文件",Filter="应用和快捷方式|*.exe;*.lnk|所有文件|*.*",CheckFileExists=true,DereferenceLinks=false};if(dialog.ShowDialog(this)==true)SetTarget(dialog.FileName);}
    void BrowseFolder_Click(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog{Title="选择启动文件夹"};if(dialog.ShowDialog(this)==true)SetTarget(dialog.FolderName);}
    void BrowseRunning_Click(object sender,RoutedEventArgs e)=>BrowseRunning();
    internal void BrowseRunning()
    {
        var dialog=new RunningApplicationsWindow{Owner=this};
        if(dialog.ShowDialog()==true && dialog.SelectedApplication is {} app)ApplyRunningApplication(app);
    }
    internal void ApplyRunningApplication(RunningApplicationsWindow.ApplicationEntry app)
    {
        if(!File.Exists(app.Path)){EditorStatus.Text="程序文件已不可用，请刷新运行列表或重新选择文件。";return;}
        bool changed=!string.Equals(TargetBox.Text.Trim(),app.Path,StringComparison.OrdinalIgnoreCase);
        // Keep a name the user has already entered; a new entry gets the application's own name.
        if(string.IsNullOrWhiteSpace(NameBox.Text))NameBox.Text=app.Name;
        TargetBox.Text=app.Path;WorkBox.Text=Path.GetDirectoryName(app.Path)??"";
        if(changed)ArgsBox.Clear();
        EditorStatus.Text="已选择运行中的程序，保存后加入轮盘。";
    }
    void BrowseWork_Click(object sender,RoutedEventArgs e){var dialog=new OpenFolderDialog{Title="选择工作目录"};if(dialog.ShowDialog(this)==true)WorkBox.Text=dialog.FolderName;}
    void SetTarget(string path){TargetBox.Text=path;if(string.IsNullOrWhiteSpace(NameBox.Text))NameBox.Text=Path.GetFileNameWithoutExtension(path);}
    void Test_Click(object sender,RoutedEventArgs e){var draft=Draft();var error=Launcher.Validate(draft);if(error!=null)EditorStatus.Text=error;else launch(draft);}
}
