using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    static async Task VerifyRunningApplications(AppController controller,SettingsWindow window,string folder,Action<bool,string> check)
    {
        var app=Application.Current;string theme=controller.Config.Theme;
        string config=JsonSerializer.Serialize(controller.Config),disk=File.ReadAllText(new ConfigStore().FilePath);
        var editor=window.CreateEditor(-1);editor.Show();await Task.Delay(50);
        var name=(TextBox)editor.FindName("NameBox");var target=(TextBox)editor.FindName("TargetBox");
        var work=(TextBox)editor.FindName("WorkBox");var args=(TextBox)editor.FindName("ArgsBox");
        RunningApplicationsWindow.ApplicationEntry? selected=null;
        void OpenPicker(Action<RunningApplicationsWindow> action)
        {
            Exception? error=null;
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                var picker=app.Windows.OfType<RunningApplicationsWindow>().Single();
                try {action(picker);}
                catch(Exception ex){error=ex;picker.Close();}
            }));
            ((Button)editor.FindName("RunningAppsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(error!=null)throw error;
        }
        try
        {
            args.Text="--old-target-argument";
            OpenPicker(picker=>
            {
                var list=(ListBox)picker.FindName("ApplicationsList");var search=(TextBox)picker.FindName("SearchBox");var use=(Button)picker.FindName("UseButton");
                check(list.Items.Count>0 && !use.IsEnabled,"RunningPickerListsActualApplicationsWithoutDefaultSelection");
                var entries=list.Items.Cast<RunningApplicationsWindow.ApplicationEntry>().ToArray();
                check(entries.All(x=>File.Exists(x.Path)) && entries.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==entries.Length,"RunningPickerReadablePathsAndNoDuplicateExecutables");
                selected=entries[0];list.SelectedItem=selected;list.ScrollIntoView(selected);picker.UpdateLayout();
                var row=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(selected);
                check(VisualChild<Image>(row)?.Source!=null && !string.IsNullOrWhiteSpace(selected.Name),"RunningPickerRendersRealApplicationIconAndName");
                search.Text=selected.Path;check(list.Items.Count==1 && use.IsEnabled,"RunningPickerSearchesFullPathAndPreservesSelection");
                picker.RefreshApplications();check(list.Items.Count==1 && use.IsEnabled,"RunningPickerRefreshPreservesSearchAndSelection");
                search.Text="__yihua_no_such_application_78641__";
                check(list.Items.Count==0 && !use.IsEnabled && ((TextBlock)picker.FindName("EmptyState")).Visibility==Visibility.Visible,"RunningPickerEmptySearchCannotChoose");
                search.Clear();list.SelectedItem=selected;
                foreach(var palette in Themes.Options)
                {
                    Themes.Apply(palette.Id);picker.UpdateLayout();
                    check(((SolidColorBrush)picker.Background).Color==palette.Brush("Window").Color && ((SolidColorBrush)list.Background).Color==palette.Brush("List").Color,$"{palette.Id} RunningPickerFollowsTheme");
                    SaveVisual(picker,Path.Combine(folder,$"运行程序-{palette.Id}-1.3.4.png"));
                }
                Themes.Apply(theme);picker.Width=picker.MinWidth;picker.Height=picker.MinHeight;picker.UpdateLayout();
                check(use.TranslatePoint(new Point(use.ActualWidth,use.ActualHeight),picker).X<=picker.ActualWidth && use.TranslatePoint(new Point(0,use.ActualHeight),picker).Y<=picker.ActualHeight,"RunningPickerActionsFitMinimumWindow");
                SaveVisual(picker,Path.Combine(folder,"运行程序-最小窗口-1.3.4.png"));
                use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            check(selected!=null && target.Text==selected.Path && name.Text==selected.Name && work.Text==Path.GetDirectoryName(selected.Path),"RunningApplicationFillsNamePathAndWorkingDirectory");
            check(args.Text=="","ChangingExecutableClearsOldArguments");
            check(JsonSerializer.Serialize(controller.Config)==config && File.ReadAllText(new ConfigStore().FilePath)==disk,"ChoosingRunningApplicationDoesNotCommitConfiguration");
            string draft=string.Join("|",name.Text,target.Text,args.Text,work.Text);
            OpenPicker(picker=>picker.Close());
            check(draft==string.Join("|",name.Text,target.Text,args.Text,work.Text),"CancelRunningPickerPreservesDraft");
            name.Text="我的自定义名称";args.Text="--keep-same-target";
            OpenPicker(picker=>
            {
                var list=(ListBox)picker.FindName("ApplicationsList");list.SelectedItem=list.Items.Cast<RunningApplicationsWindow.ApplicationEntry>().Single(x=>x.Path==selected!.Path);list.ScrollIntoView(list.SelectedItem);picker.UpdateLayout();
                var row=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
                list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});
                check(picker.SelectedApplication?.Path==selected!.Path,"DoubleClickRunningProgramAcceptsSelection");
            });
            check(name.Text=="我的自定义名称" && args.Text=="--keep-same-target","ChoosingSameExecutablePreservesCustomNameAndArguments");
            var missing=new RunningApplicationsWindow.ApplicationEntry("失效程序","失效窗口",Path.Combine(folder,"__missing_application__.exe"));
            OpenPicker(picker=>
            {
                var list=(ListBox)picker.FindName("ApplicationsList");list.ItemsSource=new[]{missing};list.SelectedItem=missing;
                check(!picker.ChooseSelected() && picker.SelectedApplication==null && ((TextBlock)picker.FindName("ErrorStatus")).Text.Length>0,"DisappearedExecutableCannotBeAccepted");picker.Close();
            });
            editor.ApplyRunningApplication(missing);check(target.Text==selected!.Path,"UnavailableExecutableDoesNotOverwriteDraft");
            // Use a recording commit so the full save path is checked without adding a user shortcut.
            LaunchItem? saved=null;var saving=new ItemEditorWindow(new LaunchItem(),-1,0,(item,_,_,_)=>saved=item,_=>throw new Exception("Unexpected launch")){Owner=window};
            try {saving.ApplyRunningApplication(selected);check(saving.SaveItem() && saved?.Target==selected.Path && saved.WorkingDirectory==Path.GetDirectoryName(selected.Path),"RunningApplicationDraftCanBeSavedAsShortcut");}finally{saving.Close();}
            check(JsonSerializer.Serialize(controller.Config)==config && File.ReadAllText(new ConfigStore().FilePath)==disk,"RunningPickerTestsLeavePersonalConfigurationUnchanged");
        }
        finally {Themes.Apply(theme);foreach(var picker in app.Windows.OfType<RunningApplicationsWindow>().ToArray())picker.Close();editor.Close();}
    }
}
