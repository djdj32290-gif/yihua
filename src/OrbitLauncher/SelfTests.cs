using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows.Input;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    public static async void BeginHoldChecks(Application app,AppController controller,string report)
    {
        var results=new Dictionary<string,object>();
        void Check(bool value,string name) { results[name]=value;if(!value)throw new Exception(name); }
        app.Exit+=(_,_)=> { results["NativeHooksReleasedAndThreadStopped"]=controller.InputResourcesReleased;results["ShellTrayIconRemoved"]=controller.TrayRemoved;File.WriteAllText(report,JsonSerializer.Serialize(results,new JsonSerializerOptions { WriteIndented=true })); };
        try
        {
            // Exercise real WPF and input-handler dispatch; no SendInput / desktop automation.
            await Task.Delay(100);
            results["TrayRegistrationError"]=controller.TrayError;
            foreach(var w in app.Windows.OfType<SettingsWindow>().ToArray())w.Close();
            await Task.Delay(100);
            results["ShellRegisteredTrayIcon"]=controller.TrayRegistered;
            results["ShellTrayIconRegisteredAndLocated"]=controller.TrayVisible && controller.TrayLocated;
            Check(controller.TrayHasExit,"ContextMenuContainsExit");
            int launches=controller.LaunchAttempts;
            var origin=controller.TestHoldStart();await Task.Delay(1600);
            Check(controller.GesturePhase==GesturePhase.Active && controller.WheelVisible,"HeldGestureSurvives1600msWithoutAsyncButtonState");
            controller.TestHoldRelease(origin.X,origin.Y);await Task.Delay(180);
            Check(controller.GesturePhase==GesturePhase.Idle && !controller.WheelVisible && controller.LaunchAttempts==launches,"CenterReleaseCancelsWithoutLaunch");
            origin=controller.TestHoldStart();await Task.Delay(350);controller.TestModifierUp();await Task.Delay(180);
            Check(controller.GesturePhase==GesturePhase.Draining && !controller.WheelVisible,"ModifierReleaseHidesWheel");
            controller.TestHoldRelease(origin.X+200,origin.Y);await Task.Delay(100);
            Check(controller.GesturePhase==GesturePhase.Idle && controller.LaunchAttempts==launches,"MouseReleaseAfterModifierCancelCannotLaunch");
            origin=controller.TestHoldStart();await Task.Delay(350);controller.TestEscape();await Task.Delay(180);
            Check(!controller.WheelVisible && controller.GesturePhase==GesturePhase.Draining,"EscapeCancelsVisibleWheel");
            controller.TestHoldRelease(origin.X+200,origin.Y);await Task.Delay(100);
            Check(controller.LaunchAttempts==launches,"MouseReleaseAfterEscapeCannotLaunch");
            bool originalGameMode=controller.Config.GameModeEnabled;controller.Config.GameModeEnabled=true;controller.Apply();
            controller.TestProtection("游戏保护：窗口模式测试游戏.exe");controller.TestModifierDown();
            var down=controller.TestPointer(0x201,origin.X,origin.Y);var move=controller.TestPointer(0x200,origin.X+150,origin.Y);var up=controller.TestPointer(0x202,origin.X+150,origin.Y);await Task.Delay(150);
            Check(!down.Block && !move.Block && !up.Block,"ProtectedGameInputPassesThrough");
            Check(controller.GesturePhase==GesturePhase.Idle && !controller.WheelVisible && controller.LaunchAttempts==launches,"WindowedGameProtectionPreventsWheelAndLaunch");
            controller.TestProtection(null);origin=controller.TestHoldStart();await Task.Delay(150);
            Check(controller.WheelVisible,"ReturningToUnprotectedAppRestoresGestures");
            controller.TestProtection("游戏保护：窗口模式测试游戏.exe");controller.TestHoldRelease(origin.X+200,origin.Y);await Task.Delay(150);
            Check(!controller.WheelVisible && controller.LaunchAttempts==launches && controller.GesturePhase==GesturePhase.Idle,"GameBecomingForegroundBeforeReleaseCancelsLaunch");
            controller.TestProtection(null);controller.TestModifierUp();controller.Config.GameModeEnabled=originalGameMode;controller.Apply();
            await VerifyMultiGesture(controller,Path.GetDirectoryName(report)!,Check);
            await VerifyEdgeGestures(controller,Path.GetDirectoryName(report)!,Check);
            results["GestureChecksPassed"]=true;
            results["Success"]=controller.TrayRegistered && controller.TrayLocated;
            controller.TestTrayExit();
        }
        catch(Exception ex) { results["Success"]=false;results["Error"]=ex.ToString();app.Shutdown(1); }
    }
    public static int ExportPreview(string[] args)
    {
        string file=args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"preview.png");
        var surface=new RadialSurface();surface.Configure(Settings.Defaults());surface.Selected=1;
        surface.Measure(new Size(452,452));surface.Arrange(new Rect(0,0,452,452));surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(904,904,192,192,PixelFormats.Pbgra32);bitmap.Render(surface);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);png.Save(stream);return 0;
    }
    public static async void BeginUiChecks(Application app,AppController controller,string report)
    {
        var results=new Dictionary<string,object>();
        var original=controller.Config.Copy();
        void Check(bool condition,string name){results[name]=condition;if(!condition)throw new Exception(name);}
        app.Exit+=(_,_)=> {results["NativeHooksReleasedAndThreadStopped"]=controller.InputResourcesReleased;results["ShellTrayIconRemoved"]=controller.TrayRemoved;File.WriteAllText(report,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));};
        try
        {
            controller.ShowSettings();await Task.Delay(200);
            var window=app.Windows.OfType<SettingsWindow>().Single();var preview=(RadialSurface)window.FindName("WheelPreview");
            Check(preview.EditorMode && preview.ActualWidth>0,"EmbeddedEditableWheelVisible");
            Check(window.FindName("ItemList")==null,"VerticalListRemoved");
            Check(((TabControl)window.FindName("SettingsTabs")).Items.Count==3,"GlobalSettingsAndGameProtectionInSeparateTabs");
            Check((string)((Button)window.FindName("SaveCloseButton")).Content=="保存并关闭设置","SaveCloseLabel");
            var center=new Point(preview.ActualWidth/2,preview.ActualHeight/2);
            var top=new Point(center.X,center.Y-(preview.ActualWidth/2-30));
            Check(preview.EditorHit(center)==-1 && preview.EditorHit(top)==(controller.Config.Items.Count>0?0:-1) && preview.EditorHit(new Point(0,0))==-2,"CenterAddSectorEditAndOutsideHitTests");
            void Edit(Point point,Action<ItemEditorWindow> action)
            {
                Exception? error=null;
                app.Dispatcher.BeginInvoke(()=> {try {action(app.Windows.OfType<ItemEditorWindow>().Single());}catch(Exception ex){error=ex;foreach(var editor in app.Windows.OfType<ItemEditorWindow>().ToArray())editor.Close();}});
                preview.EditorPointerDown(point);preview.EditorPointerUp(point);if(error!=null)throw error;
            }
            SaveVisual(window,Path.Combine(Path.GetDirectoryName(report)!,"设置页-1.3.2.png"));
            var headerTabs=(TabControl)window.FindName("SettingsTabs");
            for(int tabIndex=0;tabIndex<headerTabs.Items.Count;tabIndex++)
            {
                headerTabs.SelectedIndex=tabIndex;var tab=(TabItem)headerTabs.Items[tabIndex];tab.Focus();await Task.Delay(80);window.UpdateLayout();
                SaveVisual(window,Path.Combine(Path.GetDirectoryName(report)!,$"标签{tabIndex}-1.3.2.png"));
                Check(RoundedTabCorners(tab),$"SelectedFocusedTab{tabIndex}HasFourRoundedCorners");
            }
            SaveVisual(window,Path.Combine(Path.GetDirectoryName(report)!,"标签焦点-1.3.2.png"));headerTabs.SelectedIndex=0;window.UpdateLayout();
            await VerifyThemes(controller,window,preview,Path.GetDirectoryName(report)!,Check);
            await VerifyRunningApplications(controller,window,Path.GetDirectoryName(report)!,Check);
            VerifyBatchDelete(controller,window,preview,Path.GetDirectoryName(report)!,Check);
            int count=controller.Config.Items.Count;
            if(count>1)
            {
                double angle=-Math.PI/2+2*Math.PI/count;double radius=preview.ActualWidth/2-30;
                var next=new Point(center.X+Math.Cos(angle)*radius,center.Y+Math.Sin(angle)*radius);
                var first=controller.Config.Items[0];var secondItem=controller.Config.Items[1];
                preview.EditorPointerDown(top);preview.EditorPointerMove(next);
                SaveVisual(window,Path.Combine(Path.GetDirectoryName(report)!,"拖动交换-1.3.2.png"));preview.EditorPointerUp(next);
                Check(ReferenceEquals(controller.Config.Items[0],secondItem) && ReferenceEquals(controller.Config.Items[1],first),"DragSwapsExactlyTwoItems");
                Check(!app.Windows.OfType<ItemEditorWindow>().Any(),"DragDoesNotOpenEditor");
                Check(new ConfigStore().Load().Items[0].Name==secondItem.Name,"DragSwapSavedToDisk");
                preview.EditorPointerDown(top);preview.EditorPointerMove(next);preview.EditorPointerUp(next);
                Check(ReferenceEquals(controller.Config.Items[0],first) && ReferenceEquals(controller.Config.Items[1],secondItem),"SecondSwapRestoresOrder");
                preview.EditorPointerDown(top);preview.EditorPointerMove(center);preview.EditorPointerUp(center);
                Check(ReferenceEquals(controller.Config.Items[0],first) && !app.Windows.OfType<ItemEditorWindow>().Any(),"DroppingInCenterDoesNotSwapOrAdd");
                preview.EditorPointerDown(top);preview.EditorPointerMove(new Point(0,0));preview.EditorPointerUp(new Point(0,0));
                Check(ReferenceEquals(controller.Config.Items[0],first),"DroppingOutsideCancelsSwap");
                preview.EditorPointerDown(top);preview.EditorPointerMove(next);preview.CancelEditorDrag();preview.EditorPointerUp(next);
                Check(ReferenceEquals(controller.Config.Items[0],first),"CancelledDragCannotSwapOnRelease");
            }
            if(count<16)
            {
                Edit(center,editor=> {Check(!editor.SaveItem(),"EmptyNewItemCannotSave");editor.Close();});
                Check(controller.Config.Items.Count==count,"CancelAddLeavesNoEmptyItem");
                Edit(center,editor=> {((TextBox)editor.FindName("NameBox")).Text="测试新增选项";((TextBox)editor.FindName("TargetBox")).Text=Path.Combine(Environment.SystemDirectory,"notepad.exe");Check(editor.SaveItem(),"AddDialogSaves");});
                Check(controller.Config.Items.Count==count+1 && new ConfigStore().Load().Items.Last().Name=="测试新增选项","AddUpdatesPreviewAndDisk");
                double angle=-Math.PI/2+count*2*Math.PI/(count+1);var last=new Point(center.X+Math.Cos(angle)*(preview.ActualWidth/2-30),center.Y+Math.Sin(angle)*(preview.ActualWidth/2-30));
                Edit(last,editor=> {((ComboBox)editor.FindName("PositionBox")).SelectedIndex=0;Check(editor.SaveItem(),"ReorderFromItemDialog");});
                Check(controller.Config.Items[0].Name=="测试新增选项","ReorderUpdatesDirection");
                Edit(top,editor=>Check(editor.DeleteItem(),"DeleteFromItemDialog"));
                Check(controller.Config.Items.Count==count,"DeleteUpdatesCount");
            }
            if(count>0)
            {
                string name=controller.Config.Items[0].Name;
                Edit(top,editor=> {((TextBox)editor.FindName("NameBox")).Text="未保存草稿";editor.Close();});
                Check(controller.Config.Items[0].Name==name,"CancelEditPreservesOriginal");
                Edit(top,editor=> {
                    editor.UpdateLayout();SaveVisual(editor,Path.Combine(Path.GetDirectoryName(report)!,"选项详情-1.3.2.png"));
                    var form=(FrameworkElement)editor.FindName("EditorForm");var testButton=(Button)editor.FindName("TestItemButton");
                    Check(form.Parent is Grid,"EditorFormHasNoOuterScrollViewer");
                    editor.Width=editor.MinWidth;editor.Height=editor.MinHeight;
                    var status=(TextBlock)editor.FindName("EditorStatus");status.Text=new string('测',120);editor.UpdateLayout();
                    Check(testButton.TranslatePoint(new Point(0,testButton.ActualHeight),form).Y<=form.ActualHeight+.5,"AllEditorFieldsFitAtMinimumSizeWithValidationMessage");
                    var runningButton=(Button)editor.FindName("RunningAppsButton");Check(runningButton.TranslatePoint(new Point(runningButton.ActualWidth,0),form).X<=form.ActualWidth+.5,"RunningProgramBrowseButtonFitsMinimumEditorWidth");
                    SaveVisual(editor,Path.Combine(Path.GetDirectoryName(report)!,"紧凑选项详情-1.3.2.png"));
                    ((TextBox)editor.FindName("NameBox")).Text="中文编辑保存验证";Check(editor.SaveItem(),"ExistingItemSaves");});
                Check(new ConfigStore().Load().Items[0].Name=="中文编辑保存验证","EditedNameReadback");
                Edit(top,editor=> {((TextBox)editor.FindName("NameBox")).Text=name;Check(editor.SaveItem(),"RestoreEditedName");});
            }
            var tabs=(TabControl)window.FindName("SettingsTabs");tabs.SelectedIndex=2;await Task.Delay(100);
            var gameMode=(CheckBox)window.FindName("GameModeCheck");gameMode.IsChecked=!original.GameModeEnabled;gameMode.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(new ConfigStore().Load().GameModeEnabled==!original.GameModeEnabled,"GameModeToggleSaves");gameMode.IsChecked=original.GameModeEnabled;gameMode.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(((ListBox)window.FindName("BlockedAppList")).Items.Count==original.BlockedApplications.Count,"ExcludedApplicationsShown");
            var running=(ListBox)window.FindName("RunningAppsList");var blocked=(ListBox)window.FindName("BlockedAppList");
            Check(running.IsVisible && blocked.IsVisible && running.TranslatePoint(new Point(),window).X<blocked.TranslatePoint(new Point(),window).X,"RunningAppsLeftProtectedListRight");
            ((Button)window.FindName("RefreshRunningButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var appItem=running.Items.Cast<ForegroundGuard.ApplicationWindow>().FirstOrDefault(x=>!original.BlockedApplications.Contains(x.Name,StringComparer.OrdinalIgnoreCase));
            if(appItem==null)throw new Exception("没有可用于验证双击添加的外部运行窗口");
            void DoubleClickRunningApp()
            {
                running.SelectedItem=appItem;running.ScrollIntoView(appItem);running.UpdateLayout();
                var row=(ListBoxItem)running.ItemContainerGenerator.ContainerFromItem(appItem);
                running.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=row});
            }
            DoubleClickRunningApp();
            Check(new ConfigStore().Load().BlockedApplications.Contains(appItem.Name),"DoubleClickRunningAppAddsAndSaves");
            Check(!running.Items.Cast<ForegroundGuard.ApplicationWindow>().Any(x=>x.Name==appItem.Name),"AddedApplicationDisappearsFromLeft");
            var protectedItem=blocked.Items.Cast<ForegroundGuard.ApplicationWindow>().Single(x=>x.Name==appItem.Name);
            Check(protectedItem.Path==appItem.Path && new ConfigStore().Load().ApplicationIconPaths.GetValueOrDefault(appItem.Name)==appItem.Path,"ProtectedApplicationIconPathSaved");
            blocked.ScrollIntoView(protectedItem);blocked.UpdateLayout();
            var protectedRow=(ListBoxItem)blocked.ItemContainerGenerator.ContainerFromItem(protectedItem);
            Check(VisualChild<Image>(protectedRow)?.Source!=null,"ProtectedListRendersApplicationIcon");
            ((Button)window.FindName("RefreshRunningButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!running.Items.Cast<ForegroundGuard.ApplicationWindow>().Any(x=>x.Name==appItem.Name),"RefreshKeepsProtectedAppsOutOfLeft");
            blocked.SelectedItem=blocked.Items.Cast<ForegroundGuard.ApplicationWindow>().Single(x=>x.Name==appItem.Name);((Button)window.FindName("RemoveAppButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(new ConfigStore().Load().BlockedApplications.SequenceEqual(original.BlockedApplications),"RemoveProtectedApplicationRestoresList");
            Check(running.Items.Cast<ForegroundGuard.ApplicationWindow>().Any(x=>x.Name==appItem.Name),"RemovingRuleReturnsRunningApplicationToLeft");
            SaveVisual(window,Path.Combine(Path.GetDirectoryName(report)!,"游戏保护-1.3.2.png"));tabs.SelectedIndex=1;await Task.Delay(100);
            var size=(Slider)window.FindName("SizeSlider");size.Value=original.Diameter==680?670:original.Diameter+10;await Task.Delay(500);
            Check(new ConfigStore().Load().Diameter==(int)size.Value,"GlobalAppearanceSavesIndependently");
            Check(controller.Config.Items.Count==count,"GlobalChangeDoesNotChangeItems");size.Value=original.Diameter;window.Flush();
            SaveVisual(window,Path.Combine(Path.GetDirectoryName(report)!,"全局设置-1.3.2.png"));
            window.Width=760;window.Height=640;await Task.Delay(100);
            SaveVisual(window,Path.Combine(Path.GetDirectoryName(report)!,"紧凑全局设置-1.3.2.png"));
            tabs.SelectedIndex=0;await Task.Delay(100);
            SaveVisual(window,Path.Combine(Path.GetDirectoryName(report)!,"紧凑设置页-1.3.2.png"));
            ((Button)window.FindName("SaveCloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(controller.TrayVisible && !app.Windows.OfType<SettingsWindow>().Any(),"SaveCloseKeepsTray");
            controller.ShowSettings();Check(app.Windows.OfType<SettingsWindow>().Single().IsVisible,"SettingsReopen");
            using var second=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false});
            Check(second!=null && second.WaitForExit(5000),"SecondInstanceExits");await Task.Delay(300);
            var processes=Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath!));Check(processes.Length==1,"SingleInstanceMaintained");foreach(var p in processes)p.Dispose();
            Check(controller.TrayRegistered && controller.TrayLocated,"NativeTrayStillRegistered");
            Check(controller.TrayHasExit,"NativeContextMenuContainsExit");results["Success"]=true;controller.TestTrayExit();
        }
        catch(Exception ex)
        {
            controller.Config.Items=original.Items;controller.Config.Diameter=original.Diameter;controller.Config.GameModeEnabled=original.GameModeEnabled;controller.Config.BlockedApplications=original.BlockedApplications;controller.Config.ApplicationIconPaths=original.ApplicationIconPaths;try{controller.Apply();}catch{}
            results["Success"]=false;results["Error"]=ex.ToString();foreach(var editor in app.Windows.OfType<ItemEditorWindow>().ToArray())editor.Close();app.Shutdown(1);
        }
    }
    static void SaveVisual(Window window,string path)
    {
        window.UpdateLayout();const double scale=1.5;
        var visual=(FrameworkElement)VisualTreeHelper.GetChild(window,0);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*scale),(int)Math.Ceiling(visual.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);png.Save(stream);
    }
    static T? VisualChild<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T result)return result;var nested=VisualChild<T>(child);if(nested!=null)return nested;}return null;}
    static bool RoundedTabCorners(TabItem tab)
    {
        var border=(Border)tab.Template.FindName("Bd",tab);
        int width=(int)Math.Ceiling(border.ActualWidth*2),height=(int)Math.Ceiling(border.ActualHeight*2);
        if(width<20 || height<20 || tab.FocusVisualStyle!=null)return false;
        if(border.ActualWidth>tab.ActualWidth+.5 || border.ActualHeight>tab.ActualHeight+.5)return false;
        var bitmap=new RenderTargetBitmap(width,height,192,192,PixelFormats.Pbgra32);bitmap.Render(border);
        var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
        return new[]{(1,1),(width-2,1),(1,height-2),(width-2,height-2)}.All(p=>pixels[(p.Item2*width+p.Item1)*4+3]<30)
            && pixels[((height/2)*width+width/2)*4+3]>200;
    }
    static bool ContinuousRing(int count,bool editor=true,string theme="Obsidian")
    {
        var settings=Settings.Defaults();settings.Theme=theme;string[] targets={"https://example.com/","app.exe",Environment.GetFolderPath(Environment.SpecialFolder.Windows),"document.pdf"};
        settings.Items=Enumerable.Range(0,count).Select(i=>new LaunchItem{Name="选项",Target=targets[i%targets.Length]}).ToList();
        int size=editor?452:632,pixelSize=size*2;
        var surface=new RadialSurface{EditorMode=editor,Width=size,Height=size};surface.Configure(settings);surface.Measure(new Size(size,size));surface.Arrange(new Rect(0,0,size,size));surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(pixelSize,pixelSize,192,192,PixelFormats.Pbgra32);bitmap.Render(surface);var pixels=new byte[pixelSize*pixelSize*4];bitmap.CopyPixels(pixels,pixelSize*4,0);
        var accents=settings.Items.Select(i=>ItemAppearance.For(ItemAppearance.Classify(i.Target),theme).Accent.Color).ToArray();
        int minR=accents.Min(c=>c.R)-5,maxR=accents.Max(c=>c.R)+5,minG=accents.Min(c=>c.G)-5,maxG=accents.Max(c=>c.G)+5,minB=accents.Min(c=>c.B)-5,maxB=accents.Max(c=>c.B)+5;
        for(int i=0;i<1440;i++)
        {
            double angle=i*Math.PI*2/1440;int x=(int)Math.Round(size+404*Math.Cos(angle)),y=(int)Math.Round(size+404*Math.Sin(angle)),offset=(y*pixelSize+x)*4;
            // Adjacent blue/gold strokes can blend to grey. Every opaque blend must
            // remain within the palette's channel bounds; a background gap will not.
            if(pixels[offset+3]<200 || pixels[offset]<minB || pixels[offset]>maxB || pixels[offset+1]<minG || pixels[offset+1]>maxG || pixels[offset+2]<minR || pixels[offset+2]>maxR)
            {
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(Path.GetTempPath(),"ring-"+theme+".png")))png.Save(stream);
                throw new Exception($"Ring {theme}: sample {i} @ ({x},{y}), BGRA {pixels[offset]},{pixels[offset+1]},{pixels[offset+2]},{pixels[offset+3]}");
            }
        }
        return true;
    }
    public static int VerifyServices(string[] args)
    {
        var results=new Dictionary<string,object>();
        string report=args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"services-test.json");
        const string key=@"Software\Microsoft\Windows\CurrentVersion\Run";
        RegistryKey? registry=null; object? previous=null; RegistryValueKind kind=RegistryValueKind.String; bool changed=false;
        try
        {
            try
            {
                registry=Registry.CurrentUser.CreateSubKey(key); previous=registry.GetValue("OrbitLauncher"); kind=previous!=null?registry.GetValueKind("OrbitLauncher"):RegistryValueKind.String;
                results["StartupBefore"]=StartupService.Enabled; changed=true;
                StartupService.Set(true); if(!StartupService.Enabled)throw new Exception("自启动启用回读失败"); results["StartupEnableReadback"]=true;
                StartupService.Set(false); if(StartupService.Enabled || registry.GetValue("OrbitLauncher")!=null)throw new Exception("自启动关闭回读失败"); results["StartupDisableReadback"]=true;
            }
            catch(Exception ex) { results["StartupUnavailable"]=ex.Message; }
            string folder=Path.GetDirectoryName(report)!; Directory.CreateDirectory(folder);
            string file=Path.Combine(folder,"启动验证.txt"); File.WriteAllText(file,"一划启动验证：此文件由自动检查创建，可以关闭。\r\n");
            string? appError=Launcher.Open(new LaunchItem { Name="记事本启动验证",Target=Path.Combine(Environment.SystemDirectory,"notepad.exe"),Arguments="\""+file+"\"",WorkingDirectory=folder });
            results["ApplicationLaunchDispatched"]=appError??"OK";
            string? folderError=Launcher.Open(new LaunchItem { Name="文件夹启动验证",Target=folder }); results["FolderLaunchDispatched"]=folderError??"OK";
            string? fileError=Launcher.Open(new LaunchItem { Name="文件启动验证",Target=file }); results["FileLaunchDispatched"]=fileError??"OK";
            var shellType=Type.GetTypeFromProgID("WScript.Shell");
            if(shellType!=null)
            {
                dynamic shell=Activator.CreateInstance(shellType)!; string link=Path.Combine(folder,"记事本验证.lnk"); dynamic shortcut=shell.CreateShortcut(link);
                shortcut.TargetPath=Path.Combine(Environment.SystemDirectory,"notepad.exe"); shortcut.Arguments="\""+file+"\""; shortcut.WorkingDirectory=folder; shortcut.Save();
                string? linkError=Launcher.Open(new LaunchItem { Name="快捷方式验证",Target=link }); results["ShortcutLaunchDispatched"]=linkError??"OK";
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
            results["Success"]=appError==null && folderError==null && fileError==null;
            return (bool)results["Success"]?0:1;
        }
        catch(Exception ex) { results["Success"]=false;results["Error"]=ex.ToString();return 1; }
        finally
        {
            if(changed && registry!=null)
            { if(previous==null)registry.DeleteValue("OrbitLauncher",false);else registry.SetValue("OrbitLauncher",previous,kind); results["StartupRestored"]=Equals(previous,registry.GetValue("OrbitLauncher")); }
            registry?.Dispose();
            File.WriteAllText(report,JsonSerializer.Serialize(results,new JsonSerializerOptions { WriteIndented=true }));
        }
    }
    public static int Run(string[] args)
    {
        var checks=new List<string>();
        string report=args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"self-test.json");
        void Check(bool condition,string name) { if(!condition)throw new Exception(name); checks.Add(name); }
        var root=Path.Combine(Path.GetTempPath(),"OrbitLauncherTests-"+Guid.NewGuid());
        try
        {
            VerifySelectionModel(Check);
            VerifyThemeModel(Check);
            for(int count=1;count<=16;count++)
                for(int i=0;i<count;i++) { double a=-Math.PI/2+i*2*Math.PI/count; Check(RadialMath.Select(Math.Cos(a)*10000,Math.Sin(a)*10000,58,count)==i,$"{count} 扇区 / 方向 {i}，环外选择"); }
            Check(RadialMath.Select(0,0,58,8)==-1,"中心取消");
            Check(RadialMath.Select(58,0,58,8)==-1,"取消半径边界");
            Check(RadialMath.Select(100,0,58,0)==-1,"空配置取消");
            VerifyConfirmationAreaAndPlacement(Check);
            var g=new GestureMachine();
            foreach(int count in new[]{1,8,9,16}){Check(ContinuousRing(count),$"{count} 项编辑彩色外圈 1440 个采样点连续无暗缝");Check(ContinuousRing(count,false),$"{count} 项手势彩色外圈 1440 个采样点连续无暗缝");}
            Check(ItemAppearance.Classify("https://example.com/")==LaunchKind.Web && ItemAppearance.Classify("saved.url")==LaunchKind.Web,"网页和 URL 快捷文件使用网页配色");
            Check(ItemAppearance.Classify("C:\\Apps\\TEST.EXE")==LaunchKind.Application && ItemAppearance.Classify("app.lnk")==LaunchKind.Application,"exe 和快捷方式使用应用配色");
            Check(ItemAppearance.Classify(Environment.GetFolderPath(Environment.SpecialFolder.Windows))==LaunchKind.Folder,"实际文件夹使用文件夹配色");
            Check(ItemAppearance.Classify("document.pdf")==LaunchKind.File,"普通文件使用文件配色");
            var protection=Settings.Defaults();
            Check(protection.GameModeEnabled && protection.PauseInFullscreen,"默认启用游戏保护和全屏补充保护");
            Check(ForegroundGuard.Decide(protection,"League of Legends.exe",false)!=null,"窗口模式 LOL 按进程屏蔽");
            Check(ForegroundGuard.Decide(protection,"LEAGUECLIENTUX.EXE",false)!=null,"进程匹配忽略大小写");
            Check(ForegroundGuard.Decide(protection,"notepad.exe",false)==null,"普通窗口不受游戏名单影响");
            Check(ForegroundGuard.Decide(protection,"notepad.exe",true)!=null,"其它全屏应用可补充屏蔽");
            Check(ForegroundGuard.Decide(protection,"",false,false)!=null,"无法识别前台进程时暂停手势");
            protection.PauseInFullscreen=false;Check(ForegroundGuard.Decide(protection,"notepad.exe",true)==null && ForegroundGuard.Decide(protection,"League of Legends.exe",false)!=null,"关闭全屏保护仍屏蔽窗口游戏");
            protection.GameModeEnabled=false;Check(ForegroundGuard.Decide(protection,"League of Legends.exe",true)==null,"可关闭游戏保护");
            Check(ForegroundGuard.NormalizeName("\"D:\\Games\\League of Legends.exe\"")=="League of Legends.exe" && ForegroundGuard.NormalizeName("notepad")=="notepad.exe","应用规则接受 exe 路径与进程名");
            var monitor=new Native.RECT{Left=-1920,Top=0,Right=0,Bottom=1080};
            Check(ForegroundGuard.CoversMonitor(monitor,monitor),"负坐标显示器全屏判定");
            Check(!ForegroundGuard.CoversMonitor(new Native.RECT{Left=-1920,Top=0,Right=0,Bottom=1040},monitor),"保留任务栏的最大化窗口不算全屏");
            Check(!ForegroundGuard.CoversMonitor(new Native.RECT(),monitor),"无效窗口边界不算全屏");
            Check(ForegroundGuard.ReadProcessPath((uint)Environment.ProcessId).Equals(Environment.ProcessPath,StringComparison.OrdinalIgnoreCase),"普通用户只读查询实际进程路径");
            var modifiers=new ModifierState();
            Check(ModifierState.Normalize(0x12,0x38,0)==0xA4,"通用 Alt 消息归一为左 Alt");
            Check(ModifierState.Normalize(0x12,0x38,1)==0xA5,"扩展 Alt 消息归一为右 Alt");
            Check(ModifierState.Normalize(0x11,0x1D,0)==0xA2 && ModifierState.Normalize(0x11,0x1D,1)==0xA3,"区分左右 Ctrl");
            Check(ModifierState.Normalize(0x10,0x2A,0)==0xA0 && ModifierState.Normalize(0x10,0x36,0)==0xA1,"区分左右 Shift");
            modifiers.Set(0xA4,true);
            Check(GestureMachine.Eligible(modifiers.IsDown(0xA4),modifiers.RightAlt,false,8),"事件记录的左 Alt 可触发");
            modifiers.Set(0xA5,true);
            Check(!GestureMachine.Eligible(modifiers.IsDown(0xA4),modifiers.RightAlt,false,8),"事件记录的右 Alt 阻止触发");
            modifiers.Reset();Check(!modifiers.IsDown(0xA4) && !modifiers.RightAlt,"桌面切换清空修饰键状态");
            Check(GestureMachine.Eligible(true,false,false,8),"左修饰键可触发");
            Check(!GestureMachine.Eligible(false,true,false,8),"单独右 Alt 不触发");
            Check(!GestureMachine.Eligible(true,true,false,8),"右 Alt / AltGr 组合不触发");
            Check(!GestureMachine.Eligible(true,false,true,8),"暂停时不触发");
            Check(!GestureMachine.Eligible(true,false,false,0),"空轮盘不触发");
            Check(!g.Down(0,0,false).Block && !g.Move(100,100,12).Block && !g.Up().Block,"普通鼠标事件透传");
            Check(g.Down(100,100,true).Block,"候选按下截获");
            Check(g.Move(108,104,12).Action==GestureAction.None,"阈值以下不触发");
            Check(g.Up().Action==GestureAction.ReplayClick && g.Phase==GesturePhase.Idle,"短点击补回");
            g.Down(0,0,true); Check(g.Move(12,0,12).Action==GestureAction.Begin,"阈值边界触发");
            Check(g.Move(100,0,12).Action==GestureAction.Update,"持续跟踪方向");
            Check(g.Up().Action==GestureAction.Commit && g.Phase==GesturePhase.Idle,"鼠标先释放提交");
            Check(g.ModifierUp().Action==GestureAction.None,"提交后修饰键释放无二次操作");
            g.Down(0,0,true);g.Move(20,0,12);
            Check(g.ModifierUp().Action==GestureAction.Cancel && g.Phase==GesturePhase.Draining,"修饰键先释放取消");
            Check(g.Up().Block && g.Phase==GesturePhase.Idle,"取消后鼠标释放吞掉且不提交");
            g.Down(0,0,true);g.Move(20,0,12);
            Check(g.Cancel().Action==GestureAction.Cancel,"Esc 取消");
            Check(g.Up().Action==GestureAction.None,"Esc 后释放不提交");
            g.Down(0,0,true); Check(g.ModifierUp().Action==GestureAction.ReplayDown,"阈值前释放修饰键恢复原生拖动");
            Check(!g.Up().Block,"恢复后原生按钮释放透传");
            g.Down(0,0,true);g.Cancel();Check(g.Up().Action==GestureAction.None,"候选期间取消不补点击");
            g.Down(0,0,true);g.Move(20,0,12);Check(g.Reset().Action==GestureAction.Cancel && g.Phase==GesturePhase.Idle,"切换桌面重置手势");
            Check(g.Down(0,0,true).Block && g.Phase==GesturePhase.Pending,"重置后下一次手势可触发");g.Cancel();g.Up();
            Directory.CreateDirectory(root); var store=new ConfigStore(Path.Combine(root,"settings.json")); var config=Settings.Defaults();
            config.Diameter=500; config.Items[0].Name="中文配置回读"; config.Items[0].Arguments="--hello \"中文\""; config.Modifier="LeftShift";
            store.Save(config); var restored=store.Load();
            Check(restored.Diameter==500 && restored.Items[0].Name=="中文配置回读" && restored.Items[0].Arguments=="--hello \"中文\"" && restored.Modifier=="LeftShift","中文、参数、尺寸、修饰键配置往返");
            config.ApplicationIconPaths["LeagueClient.exe"]="C:\\Games\\LeagueClient.exe";store.Save(config);
            Check(new ConfigStore(store.FilePath).Load().ApplicationIconPaths.GetValueOrDefault("leagueclient.exe")=="C:\\Games\\LeagueClient.exe","保护名单图标路径保存及大小写兼容");
            config.ApplicationIconPaths["removed.exe"]="C:\\removed.exe";config.Normalize();Check(!config.ApplicationIconPaths.ContainsKey("removed.exe"),"移除规则后清理对应图标元数据");
            config.Diameter=-10;config.CancelRadius=999;config.DragThreshold=0;config.Normalize();
            Check(config.Diameter==300 && config.CancelRadius==75 && config.DragThreshold==4,"非法配置数值归一化");
            File.WriteAllText(store.FilePath,"{bad json"); var fallback=store.Load();
            Check(fallback.Items.Count==8 && File.Exists(store.FilePath+".broken"),"损坏配置保留并恢复默认");
            Check(Launcher.Validate(new LaunchItem { Target=Path.Combine(root,"absent.exe") })!=null,"不存在的路径不启动");
            Check(Launcher.Validate(new LaunchItem { Target=root })==null,"文件夹验证");
            Check(Launcher.Validate(new LaunchItem { Target="https://example.com/" })==null,"https 网址验证");
            Check(Launcher.Validate(new LaunchItem { Target="javascript:alert(1)" })!=null,"不接受脚本网址");
            Check(Icons.For(Path.Combine(root,"absent.exe"))!=null,"缺失路径统一图标回退");
            File.WriteAllText(report,JsonSerializer.Serialize(new { Success=true,Count=checks.Count,Checks=checks },new JsonSerializerOptions { WriteIndented=true })); return 0;
        }
        catch(Exception ex) { File.WriteAllText(report,JsonSerializer.Serialize(new { Success=false,Count=checks.Count,Error=ex.ToString(),Checks=checks },new JsonSerializerOptions { WriteIndented=true })); return 1; }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
